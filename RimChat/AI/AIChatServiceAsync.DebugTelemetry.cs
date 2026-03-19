using System;
using System.Collections.Generic;
using System.Linq;

namespace RimChat.AI
{
    /// <summary>
    /// Dependencies: AIChatServiceAsync request lifecycle and token parser helpers.
    /// Responsibility: in-memory API request telemetry collection, cleanup, and read-only snapshots.
    /// </summary>
    public partial class AIChatServiceAsync
    {
        private const int DebugRecordMaxCount = 2000;
        private const int DebugWindowMinutes = 30;
        private const int DebugRetentionMinutes = 35;
        private const int DebugBucketMinutes = 1;

        private readonly List<AIRequestDebugRecord> requestDebugRecords = new List<AIRequestDebugRecord>();
        private readonly Dictionary<string, PendingDebugRecordContext> pendingDebugRecords = new Dictionary<string, PendingDebugRecordContext>(StringComparer.Ordinal);

        private sealed class PendingDebugRecordContext
        {
            public DateTime StartedAtUtc;
            public DialogueUsageChannel Channel;
            public AIRequestDebugSource Source;
            public string Model;
            public string RequestPayload;
        }

        private sealed class DebugTokenUsage
        {
            public int PromptTokens;
            public int CompletionTokens;
            public int TotalTokens;
            public bool IsEstimated;
        }

        private void BeginRequestDebugRecord(string requestId, DialogueUsageChannel usageChannel, AIRequestDebugSource source)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return;
            }

            lock (lockObject)
            {
                pendingDebugRecords[requestId] = new PendingDebugRecordContext
                {
                    StartedAtUtc = DateTime.UtcNow,
                    Channel = usageChannel,
                    Source = source,
                    Model = string.Empty,
                    RequestPayload = string.Empty
                };
            }
        }

        private void SetRequestDebugModel(string requestId, string model)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return;
            }

            lock (lockObject)
            {
                if (!pendingDebugRecords.TryGetValue(requestId, out PendingDebugRecordContext context))
                {
                    return;
                }

                context.Model = model ?? string.Empty;
            }
        }

        private void SetRequestDebugPayload(string requestId, string requestPayload)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return;
            }

            lock (lockObject)
            {
                if (!pendingDebugRecords.TryGetValue(requestId, out PendingDebugRecordContext context))
                {
                    return;
                }

                context.RequestPayload = requestPayload ?? string.Empty;
            }
        }

        private void FinalizeRequestDebugRecord(
            string requestId,
            List<ChatMessageData> messages,
            string rawResponseText,
            string parsedResponse,
            AIRequestDebugStatus status,
            long httpStatusCode,
            string errorText)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return;
            }

            lock (lockObject)
            {
                if (!pendingDebugRecords.TryGetValue(requestId, out PendingDebugRecordContext context))
                {
                    return;
                }

                pendingDebugRecords.Remove(requestId);
                DateTime nowUtc = DateTime.UtcNow;
                DebugTokenUsage tokenUsage = ResolveDebugTokenUsage(messages, rawResponseText, parsedResponse);
                var record = new AIRequestDebugRecord
                {
                    RequestId = requestId,
                    RecordedAtUtc = context.StartedAtUtc,
                    Source = context.Source,
                    Channel = context.Channel,
                    Model = context.Model ?? string.Empty,
                    Status = status,
                    DurationMs = Math.Max(0L, (long)(nowUtc - context.StartedAtUtc).TotalMilliseconds),
                    HttpStatusCode = httpStatusCode,
                    PromptTokens = tokenUsage.PromptTokens,
                    CompletionTokens = tokenUsage.CompletionTokens,
                    TotalTokens = tokenUsage.TotalTokens,
                    IsEstimatedTokens = tokenUsage.IsEstimated,
                    RequestText = context.RequestPayload ?? string.Empty,
                    ResponseText = rawResponseText ?? string.Empty,
                    ErrorText = errorText ?? string.Empty
                };
                requestDebugRecords.Add(record);
                CleanupRequestDebugRecordsLockless(nowUtc);
            }
        }

        public AIRequestDebugSnapshot GetRequestDebugSnapshot()
        {
            return BuildRequestDebugSnapshot(DateTime.UtcNow);
        }

        public static bool TryGetRequestDebugSnapshot(out AIRequestDebugSnapshot snapshot)
        {
            snapshot = null;
            if (_instance == null)
            {
                return false;
            }

            snapshot = _instance.GetRequestDebugSnapshot();
            return snapshot != null;
        }

        public static void RecordExternalDebugRecord(
            AIRequestDebugSource source,
            DialogueUsageChannel channel,
            string model,
            AIRequestDebugStatus status,
            long durationMs,
            long httpStatusCode,
            string requestText,
            string responseText,
            string errorText,
            DateTime? startedAtUtc = null)
        {
            if (_instance == null)
            {
                return;
            }

            _instance.AppendExternalDebugRecord(
                source,
                channel,
                model,
                status,
                durationMs,
                httpStatusCode,
                requestText,
                responseText,
                errorText,
                startedAtUtc);
        }

        private void AppendExternalDebugRecord(
            AIRequestDebugSource source,
            DialogueUsageChannel channel,
            string model,
            AIRequestDebugStatus status,
            long durationMs,
            long httpStatusCode,
            string requestText,
            string responseText,
            string errorText,
            DateTime? startedAtUtc)
        {
            DateTime nowUtc = DateTime.UtcNow;
            DateTime recordedAtUtc = startedAtUtc ?? nowUtc;
            if (recordedAtUtc > nowUtc)
            {
                recordedAtUtc = nowUtc;
            }

            long normalizedDuration = durationMs >= 0
                ? durationMs
                : Math.Max(0L, (long)(nowUtc - recordedAtUtc).TotalMilliseconds);

            var record = new AIRequestDebugRecord
            {
                RequestId = Guid.NewGuid().ToString("N"),
                RecordedAtUtc = recordedAtUtc,
                Source = source,
                Channel = channel,
                Model = model ?? string.Empty,
                Status = status,
                DurationMs = normalizedDuration,
                HttpStatusCode = httpStatusCode,
                PromptTokens = 0,
                CompletionTokens = 0,
                TotalTokens = 0,
                IsEstimatedTokens = true,
                RequestText = requestText ?? string.Empty,
                ResponseText = responseText ?? string.Empty,
                ErrorText = errorText ?? string.Empty
            };

            lock (lockObject)
            {
                requestDebugRecords.Add(record);
                CleanupRequestDebugRecordsLockless(nowUtc);
            }
        }

        private AIRequestDebugSnapshot BuildRequestDebugSnapshot(DateTime nowUtc)
        {
            lock (lockObject)
            {
                CleanupRequestDebugRecordsLockless(nowUtc);
                DateTime windowStartUtc = nowUtc.AddMinutes(-DebugWindowMinutes);
                List<AIRequestDebugRecord> windowRecords = new List<AIRequestDebugRecord>(Math.Min(requestDebugRecords.Count, DebugRecordMaxCount));
                List<AIRequestDebugBucket> buckets = BuildEmptyDebugBuckets(windowStartUtc);
                var summary = new AIRequestDebugSummary();
                int highPriorityTokens = 0;
                long totalDurationMs = 0L;
                int bucketCount = buckets.Count;

                for (int i = 0; i < requestDebugRecords.Count; i++)
                {
                    AIRequestDebugRecord record = requestDebugRecords[i];
                    if (record == null || record.RecordedAtUtc < windowStartUtc || record.RecordedAtUtc > nowUtc)
                    {
                        continue;
                    }

                    AIRequestDebugRecord clonedRecord = record.Clone();
                    windowRecords.Add(clonedRecord);

                    int totalTokens = Math.Max(0, clonedRecord.TotalTokens);
                    summary.RequestCount++;
                    if (clonedRecord.Status == AIRequestDebugStatus.Success)
                    {
                        summary.SuccessCount++;
                    }
                    else if (clonedRecord.Status == AIRequestDebugStatus.Error)
                    {
                        summary.ErrorCount++;
                    }
                    else if (clonedRecord.Status == AIRequestDebugStatus.Cancelled)
                    {
                        summary.CancelledCount++;
                    }

                    summary.TotalTokens += totalTokens;
                    totalDurationMs += Math.Max(0L, clonedRecord.DurationMs);
                    if (clonedRecord.IsHighPrioritySource)
                    {
                        highPriorityTokens += totalTokens;
                    }

                    int bucketIndex = (int)Math.Floor((clonedRecord.RecordedAtUtc - windowStartUtc).TotalMinutes / DebugBucketMinutes);
                    if (bucketIndex < 0 || bucketIndex >= bucketCount)
                    {
                        continue;
                    }

                    AIRequestDebugBucket bucket = buckets[bucketIndex];
                    bucket.RequestCount++;
                    bucket.TotalTokens += totalTokens;
                    if (clonedRecord.IsHighPrioritySource)
                    {
                        bucket.HighPriorityTokens += totalTokens;
                    }
                }

                if (windowRecords.Count > 1)
                {
                    windowRecords.Sort((left, right) => right.RecordedAtUtc.CompareTo(left.RecordedAtUtc));
                }

                if (summary.RequestCount > 0)
                {
                    summary.SuccessRatePercent = (float)summary.SuccessCount / summary.RequestCount * 100f;
                    summary.AverageDurationMs = (float)totalDurationMs / summary.RequestCount;
                }

                if (summary.TotalTokens > 0)
                {
                    summary.HighPriorityTokenSharePercent = (float)highPriorityTokens / summary.TotalTokens * 100f;
                }

                var snapshot = new AIRequestDebugSnapshot
                {
                    GeneratedAtUtc = nowUtc,
                    WindowMinutes = DebugWindowMinutes,
                    Records = windowRecords,
                    Buckets = buckets,
                    Summary = summary
                };
                return snapshot;
            }
        }

        private static List<AIRequestDebugBucket> BuildEmptyDebugBuckets(DateTime windowStartUtc)
        {
            int bucketCount = DebugWindowMinutes / DebugBucketMinutes;
            var buckets = new List<AIRequestDebugBucket>(bucketCount);
            for (int i = 0; i < bucketCount; i++)
            {
                buckets.Add(new AIRequestDebugBucket
                {
                    BucketStartUtc = windowStartUtc.AddMinutes(i * DebugBucketMinutes),
                    RequestCount = 0,
                    TotalTokens = 0,
                    HighPriorityTokens = 0
                });
            }

            return buckets;
        }

        private static List<AIRequestDebugBucket> BuildDebugBuckets(List<AIRequestDebugRecord> records, DateTime windowStartUtc)
        {
            int bucketCount = DebugWindowMinutes / DebugBucketMinutes;
            var buckets = new List<AIRequestDebugBucket>(bucketCount);
            for (int i = 0; i < bucketCount; i++)
            {
                buckets.Add(new AIRequestDebugBucket
                {
                    BucketStartUtc = windowStartUtc.AddMinutes(i * DebugBucketMinutes),
                    RequestCount = 0,
                    TotalTokens = 0,
                    HighPriorityTokens = 0
                });
            }

            if (records == null || records.Count == 0)
            {
                return buckets;
            }

            for (int i = 0; i < records.Count; i++)
            {
                AIRequestDebugRecord record = records[i];
                double deltaMinutes = (record.RecordedAtUtc - windowStartUtc).TotalMinutes;
                int bucketIndex = (int)Math.Floor(deltaMinutes / DebugBucketMinutes);
                if (bucketIndex < 0 || bucketIndex >= buckets.Count)
                {
                    continue;
                }

                AIRequestDebugBucket bucket = buckets[bucketIndex];
                bucket.RequestCount++;
                bucket.TotalTokens += Math.Max(0, record.TotalTokens);
                if (record.IsHighPrioritySource)
                {
                    bucket.HighPriorityTokens += Math.Max(0, record.TotalTokens);
                }
            }

            return buckets;
        }

        private static AIRequestDebugSummary BuildDebugSummary(List<AIRequestDebugRecord> records)
        {
            var summary = new AIRequestDebugSummary();
            if (records == null || records.Count == 0)
            {
                return summary;
            }

            int requestCount = records.Count;
            int successCount = records.Count(record => record.Status == AIRequestDebugStatus.Success);
            int errorCount = records.Count(record => record.Status == AIRequestDebugStatus.Error);
            int cancelledCount = records.Count(record => record.Status == AIRequestDebugStatus.Cancelled);
            int totalTokens = records.Sum(record => Math.Max(0, record.TotalTokens));
            int highPriorityTokens = records
                .Where(record => record.IsHighPrioritySource)
                .Sum(record => Math.Max(0, record.TotalTokens));

            summary.RequestCount = requestCount;
            summary.SuccessCount = successCount;
            summary.ErrorCount = errorCount;
            summary.CancelledCount = cancelledCount;
            summary.TotalTokens = totalTokens;
            summary.SuccessRatePercent = requestCount > 0 ? (float)successCount / requestCount * 100f : 0f;
            summary.AverageDurationMs = requestCount > 0 ? (float)records.Average(record => Math.Max(0L, record.DurationMs)) : 0f;
            summary.HighPriorityTokenSharePercent = totalTokens > 0 ? (float)highPriorityTokens / totalTokens * 100f : 0f;
            return summary;
        }

        private void CleanupRequestDebugRecordsLockless(DateTime nowUtc)
        {
            DateTime cutoffUtc = nowUtc.AddMinutes(-DebugRetentionMinutes);
            requestDebugRecords.RemoveAll(record => record == null || record.RecordedAtUtc < cutoffUtc);

            if (requestDebugRecords.Count > DebugRecordMaxCount)
            {
                requestDebugRecords.Sort((a, b) => a.RecordedAtUtc.CompareTo(b.RecordedAtUtc));
                int overflow = requestDebugRecords.Count - DebugRecordMaxCount;
                requestDebugRecords.RemoveRange(0, overflow);
            }

            List<string> stalePendingIds = null;
            foreach (KeyValuePair<string, PendingDebugRecordContext> kv in pendingDebugRecords)
            {
                if (kv.Value.StartedAtUtc >= cutoffUtc)
                {
                    continue;
                }

                if (stalePendingIds == null)
                {
                    stalePendingIds = new List<string>();
                }

                stalePendingIds.Add(kv.Key);
            }

            if (stalePendingIds == null || stalePendingIds.Count == 0)
            {
                return;
            }

            for (int i = 0; i < stalePendingIds.Count; i++)
            {
                pendingDebugRecords.Remove(stalePendingIds[i]);
            }
        }

        private static DebugTokenUsage ResolveDebugTokenUsage(
            List<ChatMessageData> messages,
            string rawResponseText,
            string parsedResponse)
        {
            EstimateTokenUsage(messages, parsedResponse, out int estimatedPromptTokens, out int estimatedCompletionTokens, out int estimatedTotalTokens);
            bool hasProviderUsage = TryExtractUsage(rawResponseText, out int providerPromptTokens, out int providerCompletionTokens, out int providerTotalTokens);
            bool providerLooksAbnormal = hasProviderUsage && ShouldUseEstimatedUsage(
                providerPromptTokens,
                providerCompletionTokens,
                providerTotalTokens,
                estimatedPromptTokens,
                estimatedCompletionTokens,
                estimatedTotalTokens);

            bool useEstimated = !hasProviderUsage || providerLooksAbnormal;
            int promptTokens = useEstimated ? estimatedPromptTokens : providerPromptTokens;
            int completionTokens = useEstimated ? estimatedCompletionTokens : providerCompletionTokens;
            int totalTokens = useEstimated ? estimatedTotalTokens : providerTotalTokens;
            if (totalTokens <= 0)
            {
                totalTokens = Math.Max(0, promptTokens) + Math.Max(0, completionTokens);
            }

            return new DebugTokenUsage
            {
                PromptTokens = Math.Max(0, promptTokens),
                CompletionTokens = Math.Max(0, completionTokens),
                TotalTokens = Math.Max(0, totalTokens),
                IsEstimated = useEstimated
            };
        }

        private static AIRequestDebugStatus ClassifyDebugStatusFromError(string errorText)
        {
            if (string.IsNullOrWhiteSpace(errorText))
            {
                return AIRequestDebugStatus.Error;
            }

            string lower = errorText.ToLowerInvariant();
            if (lower.Contains("cancel") ||
                lower.Contains("context change") ||
                lower.Contains("dropped"))
            {
                return AIRequestDebugStatus.Cancelled;
            }

            return AIRequestDebugStatus.Error;
        }
    }
}
