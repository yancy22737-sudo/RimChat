using System;

namespace RimDiplomacy.DiplomacySystem
{
    /// <summary>
    /// Dependencies: SocialCircle UI, SocialCircleService.
    /// Responsibility: shared enums for social circle content and automation.
    /// </summary>
    public enum SocialPostCategory
    {
        Military,
        Economic,
        Diplomatic,
        Anomaly
    }

    public enum SocialIntentType
    {
        Raid,
        Aid,
        Caravan
    }

    public enum SocialPostImpactType
    {
        Goodwill,
        SettlementGain,
        SettlementLoss,
        IncidentColdSnap,
        IncidentBlight
    }

    public enum DebugGenerateReason
    {
        Scheduled,
        ManualButton,
        DialogueExplicit,
        DialogueKeyword
    }
}

