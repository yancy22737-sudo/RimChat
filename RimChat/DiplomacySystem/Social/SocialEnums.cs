using System;

namespace RimChat.DiplomacySystem
{
    /// <summary>/// Dependencies: SocialCircle UI, SocialCircleService.
 /// Responsibility: shared enums for social circle content and automation.
 ///</summary>
    public enum SocialPostCategory
    {
        Military,
        Economic,
        Diplomatic,
        Anomaly
    }

    public enum SocialNewsOriginType
    {
        Unknown = 0,
        DialogueExplicit = 1,
        DialogueKeyword = 2,
        WorldEvent = 3,
        RaidBattleReport = 4,
        LeaderMemory = 5,
        DiplomacySummary = 6,
        QuestOutcome = 7,
        TradeDeal = 8,
        GoodwillShift = 9,
        RelationPivot = 10,
        AidArrival = 11
    }

    public enum SocialNewsGenerationState
    {
        Pending = 0,
        Completed = 1,
        Failed = 2
    }

    public enum SocialIntentType
    {
        Raid,
        Aid,
        Caravan
    }

    public enum DebugGenerateReason
    {
        Scheduled,
        ManualButton,
        DialogueExplicit,
        DialogueKeyword
    }

    public enum SocialForceGenerateFailureReason
    {
        Unknown = 0,
        Disabled = 1,
        AiUnavailable = 2,
        QueueFull = 3,
        NoAvailableSeed = 4
    }

    public enum SocialPostEnqueueFailureReason
    {
        None = 0,
        Unknown = 1,
        Disabled = 2,
        PlayerInfluenceDisabled = 3,
        MissingSourceFaction = 4,
        SourceFactionDefeated = 5,
        AiUnavailable = 6,
        QueueFull = 7,
        InvalidSeed = 8,
        OriginBlocked = 9,
        RequestDispatchFailed = 10,
        KeywordNotMatched = 11
    }

    public enum SocialPostGenerationFailureReason
    {
        None = 0,
        ParseFailed = 1,
        AiError = 2,
        InvalidDraft = 3,
        Unknown = 4
    }

    public sealed class SocialPostEnqueueResult
    {
        public bool Triggered = true;
        public bool Queued;
        public string RequestId = string.Empty;
        public SocialPostEnqueueFailureReason FailureReason = SocialPostEnqueueFailureReason.Unknown;
        public SocialNewsOriginType OriginType = SocialNewsOriginType.Unknown;
        public string OriginKey = string.Empty;
    }
}
