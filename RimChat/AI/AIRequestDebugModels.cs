using System;
using System.Collections.Generic;

namespace RimChat.AI
{
    /// <summary>
    /// Dependencies: AI request lifecycle and UI observability window.
    /// Responsibility: shared read models for API debug observability.
    /// </summary>
    public enum AIRequestDebugSource
    {
        DiplomacyDialogue = 0,
        RpgDialogue = 1,
        NpcPush = 2,
        PawnRpgPush = 3,
        SocialNews = 4,
        StrategySuggestion = 5,
        PersonaBootstrap = 6,
        MemorySummary = 7,
        ArchiveCompression = 8,
        SendImage = 9,
        ApiUsabilityTest = 10,
        Other = 99
    }

    public enum AIRequestDebugStatus
    {
        Success = 0,
        Error = 1,
        Cancelled = 2
    }

    public sealed class AIRequestDebugRecord
    {
        public string RequestId { get; set; }
        public DateTime RecordedAtUtc { get; set; }
        public AIRequestDebugSource Source { get; set; }
        public DialogueUsageChannel Channel { get; set; }
        public string Model { get; set; }
        public AIRequestDebugStatus Status { get; set; }
        public long DurationMs { get; set; }
        public long HttpStatusCode { get; set; }
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int TotalTokens { get; set; }
        public bool IsEstimatedTokens { get; set; }
        public string RequestText { get; set; }
        public string ResponseText { get; set; }
        public string ErrorText { get; set; }
        public string ContractValidationStatus { get; set; }
        public int ContractRetryCount { get; set; }
        public string ContractFailureReason { get; set; }

        public bool IsHighPrioritySource
        {
            get
            {
                return Source == AIRequestDebugSource.DiplomacyDialogue ||
                       Source == AIRequestDebugSource.RpgDialogue;
            }
        }

        public AIRequestDebugRecord Clone()
        {
            return new AIRequestDebugRecord
            {
                RequestId = RequestId ?? string.Empty,
                RecordedAtUtc = RecordedAtUtc,
                Source = Source,
                Channel = Channel,
                Model = Model ?? string.Empty,
                Status = Status,
                DurationMs = DurationMs,
                HttpStatusCode = HttpStatusCode,
                PromptTokens = PromptTokens,
                CompletionTokens = CompletionTokens,
                TotalTokens = TotalTokens,
                IsEstimatedTokens = IsEstimatedTokens,
                RequestText = RequestText ?? string.Empty,
                ResponseText = ResponseText ?? string.Empty,
                ErrorText = ErrorText ?? string.Empty,
                ContractValidationStatus = ContractValidationStatus ?? string.Empty,
                ContractRetryCount = ContractRetryCount,
                ContractFailureReason = ContractFailureReason ?? string.Empty
            };
        }
    }

    public sealed class AIRequestDebugSummary
    {
        public int RequestCount { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public int CancelledCount { get; set; }
        public int TotalTokens { get; set; }
        public float SuccessRatePercent { get; set; }
        public float AverageDurationMs { get; set; }
        public float HighPriorityTokenSharePercent { get; set; }
    }

    public sealed class AIRequestDebugBucket
    {
        public DateTime BucketStartUtc { get; set; }
        public int RequestCount { get; set; }
        public int TotalTokens { get; set; }
        public int HighPriorityTokens { get; set; }

        public AIRequestDebugBucket Clone()
        {
            return new AIRequestDebugBucket
            {
                BucketStartUtc = BucketStartUtc,
                RequestCount = RequestCount,
                TotalTokens = TotalTokens,
                HighPriorityTokens = HighPriorityTokens
            };
        }
    }

    public sealed class AIRequestDebugSnapshot
    {
        public DateTime GeneratedAtUtc { get; set; }
        public int WindowMinutes { get; set; }
        public List<AIRequestDebugBucket> Buckets { get; set; }
        public List<AIRequestDebugRecord> Records { get; set; }
        public AIRequestDebugSummary Summary { get; set; }
    }
}
