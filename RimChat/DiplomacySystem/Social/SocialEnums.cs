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
        DiplomacySummary = 6
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
}

