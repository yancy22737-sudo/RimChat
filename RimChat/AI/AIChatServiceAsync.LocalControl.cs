using System;
using System.Collections.Generic;
using RimChat.Util;
using UnityEngine;
using UnityEngine.Networking;

namespace RimChat.AI
{
    /// <summary>/// Dependencies: DebugLogger, UnityWebRequest, AIChatServiceAsync shared request state.
 /// Responsibility: local-model queue gating, transient 5xx retry policy, and structured diagnostics.
 ///</summary>
    public partial class AIChatServiceAsync
    {
        private void EnqueueLocalRequest(string requestId)
        {
            lock (lockObject)
            {
                if (string.IsNullOrWhiteSpace(requestId))
                {
                    return;
                }

                if (queuedLocalRequestIds.Add(requestId))
                {
                    Queue<string> targetQueue = ResolveLocalRequestQueueLockless(requestId);
                    targetQueue.Enqueue(requestId);
                    MarkRequestQueuedLockless(requestId);
                    RefreshQueuedRequestPositionsLockless();
                }
            }
        }

        private bool TryAcquireLocalRequestSlot(string requestId)
        {
            lock (lockObject)
            {
                if (activeLocalRequestId == requestId)
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(activeLocalRequestId))
                {
                    return false;
                }

                if (!TryPeekNextLocalRequestIdLockless(out string nextRequestId) ||
                    !string.Equals(nextRequestId, requestId, StringComparison.Ordinal))
                {
                    return false;
                }

                DequeueNextLocalRequestLockless();
                queuedLocalRequestIds.Remove(requestId);
                activeLocalRequestId = requestId;
                MarkRequestProcessingStartedLockless(requestId);
                RefreshQueuedRequestPositionsLockless();
                return true;
            }
        }

        private void ReleaseLocalRequestSlot(string requestId)
        {
            lock (lockObject)
            {
                if (string.Equals(activeLocalRequestId, requestId, StringComparison.Ordinal))
                {
                    activeLocalRequestId = null;
                }

                RemoveLocalRequestLockless(requestId);
            }
        }

        private void RemoveLocalRequest(string requestId)
        {
            lock (lockObject)
            {
                RemoveLocalRequestLockless(requestId);
            }
        }

        private void RemoveLocalRequestLockless(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return;
            }

            if (string.Equals(activeLocalRequestId, requestId, StringComparison.Ordinal))
            {
                activeLocalRequestId = null;
            }

            bool removedQueuedId = queuedLocalRequestIds.Remove(requestId);
            if (!removedQueuedId)
            {
                return;
            }

            RebuildLocalRequestQueueWithoutLockless(interactiveLocalRequestQueue, requestId);
            RebuildLocalRequestQueueWithoutLockless(localRequestQueue, requestId);
            RefreshQueuedRequestPositionsLockless();
        }

        private bool TryGetRequestError(string requestId, out string error)
        {
            lock (lockObject)
            {
                error = null;
                if (!activeRequests.TryGetValue(requestId, out AIRequestResult result))
                {
                    return false;
                }

                if (result.State != AIRequestState.Error)
                {
                    return false;
                }

                error = string.IsNullOrWhiteSpace(result.Error)
                    ? "Request cancelled"
                    : result.Error;
                return true;
            }
        }

        private Queue<string> ResolveLocalRequestQueueLockless(string requestId)
        {
            if (activeRequests.TryGetValue(requestId, out AIRequestResult result) &&
                result.Priority == AIRequestPriority.Interactive)
            {
                return interactiveLocalRequestQueue;
            }

            return localRequestQueue;
        }

        private bool TryPeekNextLocalRequestIdLockless(out string requestId)
        {
            if (TryPeekNextLocalRequestIdFromQueueLockless(interactiveLocalRequestQueue, out requestId))
            {
                return true;
            }

            return TryPeekNextLocalRequestIdFromQueueLockless(localRequestQueue, out requestId);
        }

        private bool TryPeekNextLocalRequestIdFromQueueLockless(Queue<string> queue, out string requestId)
        {
            requestId = null;
            if (queue == null)
            {
                return false;
            }

            while (queue.Count > 0)
            {
                string candidate = queue.Peek();
                if (IsQueuedLocalRequestEligibleLockless(candidate))
                {
                    requestId = candidate;
                    return true;
                }

                queue.Dequeue();
                queuedLocalRequestIds.Remove(candidate);
            }

            return false;
        }

        private void DequeueNextLocalRequestLockless()
        {
            if (TryPeekNextLocalRequestIdFromQueueLockless(interactiveLocalRequestQueue, out _))
            {
                interactiveLocalRequestQueue.Dequeue();
                return;
            }

            if (TryPeekNextLocalRequestIdFromQueueLockless(localRequestQueue, out _))
            {
                localRequestQueue.Dequeue();
            }
        }

        private bool IsQueuedLocalRequestEligibleLockless(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId) || !queuedLocalRequestIds.Contains(requestId))
            {
                return false;
            }

            return activeRequests.TryGetValue(requestId, out AIRequestResult result) &&
                   IsInFlightState(result.State);
        }

        private static void RebuildLocalRequestQueueWithoutLockless(Queue<string> queue, string requestId)
        {
            if (queue == null || queue.Count == 0)
            {
                return;
            }

            var remaining = new Queue<string>(queue.Count);
            while (queue.Count > 0)
            {
                string queuedId = queue.Dequeue();
                if (!string.Equals(queuedId, requestId, StringComparison.Ordinal))
                {
                    remaining.Enqueue(queuedId);
                }
            }

            while (remaining.Count > 0)
            {
                queue.Enqueue(remaining.Dequeue());
            }
        }

        private static bool ShouldRetryLocalServerError(bool isLocalModel, long responseCode, int local5xxRetryCount)
        {
            if (!isLocalModel || !IsRetryableLocalServerStatus(responseCode))
            {
                return false;
            }

            return local5xxRetryCount < LocalServerMaxAttempts - 1;
        }

        private static bool IsRetryableLocalServerStatus(long responseCode)
        {
            return responseCode == 500 ||
                   responseCode == 502 ||
                   responseCode == 503 ||
                   responseCode == 504;
        }

        private static float GetLocalServerRetryDelaySeconds(int retryIndex)
        {
            float baseDelay = retryIndex <= 1 ? 0.35f : 1.10f;
            float jitter = UnityEngine.Random.Range(0f, 0.2f);
            return baseDelay + jitter;
        }

        private static bool ShouldRetryLocalConnectionError(
            bool isLocalModel,
            AIRequestDebugSource debugSource,
            string requestError,
            int localConnectionRetryCount)
        {
            if (!isLocalModel || localConnectionRetryCount >= LocalConnectionMaxAttempts - 1)
            {
                return false;
            }

            if (debugSource == AIRequestDebugSource.AirdropSelection)
            {
                return false;
            }

            return LooksLikeTimeoutError(requestError) ||
                   ContainsErrorToken(requestError, "connection reset") ||
                   ContainsErrorToken(requestError, "connection aborted") ||
                   ContainsErrorToken(requestError, "unexpected eof");
        }

        private static float GetLocalConnectionRetryDelaySeconds(int retryIndex)
        {
            float baseDelay = retryIndex <= 1 ? 0.5f : 1.2f;
            float jitter = UnityEngine.Random.Range(0f, 0.25f);
            return baseDelay + jitter;
        }

        private static bool LooksLikeTimeoutError(string requestError)
        {
            return ContainsErrorToken(requestError, "timeout") ||
                   ContainsErrorToken(requestError, "timed out");
        }

        private static bool ContainsErrorToken(string value, string token)
        {
            string source = value ?? string.Empty;
            if (source.Length == 0 || string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            return source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void LogRequestFingerprint(
            string requestId,
            int attempt,
            DialogueUsageChannel usageChannel,
            string model,
            string url,
            int messageCount,
            int jsonBytes,
            long elapsedMs,
            long httpCode,
            UnityWebRequest.Result result,
            string stage)
        {
            if (!DebugLogger.LogInternals)
            {
                return;
            }

            string hostPort = GetUrlHostPort(url);
            string channel = usageChannel.ToString();
            DebugLogger.LogInternal(
                "AIChatServiceAsync",
                $"fingerprint stage={stage} requestId={requestId} attempt={attempt} channel={channel} model={model} host={hostPort} messageCount={messageCount} jsonBytes={jsonBytes} elapsedMs={elapsedMs} httpCode={httpCode} result={result}");
        }

        private static void LogLocalServerRetryDecision(
            string requestId,
            int attempt,
            int nextAttempt,
            long responseCode,
            float retryDelaySeconds,
            string responseBody)
        {
            if (!DebugLogger.LogInternals)
            {
                return;
            }

            string responseSummary = BuildResponseBodySummary(responseBody, 160);
            DebugLogger.LogInternal(
                "AIChatServiceAsync",
                $"local_retry requestId={requestId} attempt={attempt} nextAttempt={nextAttempt} httpCode={responseCode} backoffMs={(int)(retryDelaySeconds * 1000f)} responseSummary=\"{responseSummary}\"");
        }

        private static void LogLocalConnectionRetryDecision(
            string requestId,
            int attempt,
            int nextAttempt,
            string requestError,
            float retryDelaySeconds)
        {
            if (!DebugLogger.LogInternals)
            {
                return;
            }

            string errorSummary = BuildResponseBodySummary(requestError, 120);
            DebugLogger.LogInternal(
                "AIChatServiceAsync",
                $"local_conn_retry requestId={requestId} attempt={attempt} nextAttempt={nextAttempt} backoffMs={(int)(retryDelaySeconds * 1000f)} error=\"{errorSummary}\"");
        }

        private static string BuildResponseBodySummary(string text, int maxChars)
        {
            string value = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (value.Length <= maxChars)
            {
                return value;
            }

            if (maxChars <= 3)
            {
                return value.Substring(0, maxChars);
            }

            return value.Substring(0, maxChars - 3) + "...";
        }

        private static string GetUrlHostPort(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            try
            {
                var uri = new Uri(url);
                return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
            }
            catch
            {
                return "invalid-url";
            }
        }
    }
}
