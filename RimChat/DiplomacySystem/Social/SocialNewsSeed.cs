using System.Collections.Generic;
using RimWorld;

namespace RimChat.DiplomacySystem
{
    /// <summary>/// Dependencies: RimWorld.Faction.
 /// Responsibility: carry one fact-grounded news seed into the social-circle generation pipeline.
 ///</summary>
    internal sealed class SocialNewsSeed
    {
        public SocialNewsOriginType OriginType = SocialNewsOriginType.Unknown;
        public string OriginKey = string.Empty;
        public Faction SourceFaction;
        public Faction TargetFaction;
        public SocialPostCategory Category = SocialPostCategory.Diplomatic;
        public int Sentiment;
        public int OccurredTick;
        public string Summary = string.Empty;
        public string IntentHint = string.Empty;
        public string SourceLabel = string.Empty;
        public string CredibilityLabel = string.Empty;
        public float CredibilityValue = 0.6f;
        public bool IsFromPlayerDialogue;
        public bool ApplyDiplomaticImpact;
        public DebugGenerateReason DebugReason = DebugGenerateReason.Scheduled;
        public string PrimaryClaim = string.Empty;
        public string QuoteAttributionHint = string.Empty;
        public List<string> Facts = new List<string>();

        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(OriginKey)
                && OccurredTick >= 0
                && Facts != null
                && Facts.Count > 0;
        }
    }

    /// <summary>/// Dependencies: none.
 /// Responsibility: hold validated structured news text returned by LLM.
 ///</summary>
    internal sealed class SocialNewsDraft
    {
        public string Headline = string.Empty;
        public string Lead = string.Empty;
        public string Cause = string.Empty;
        public string Process = string.Empty;
        public string Outlook = string.Empty;
        public string Quote = string.Empty;
        public string QuoteAttribution = string.Empty;
        public string NarrativeMode = string.Empty;
        public string LocationName = string.Empty;
    }
}
