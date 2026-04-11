using System.Linq;
using RimChat.Util;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>/// Dependencies: RimWorld Faction, Verse Scribe.
 /// Responsibility: serializable public social post record.
 ///</summary>
    public class PublicSocialPost : IExposable
    {
        public string PostId = string.Empty;
        public int CreatedTick;
        public Faction SourceFaction;
        public Faction TargetFaction;
        public SocialNewsOriginType OriginType = SocialNewsOriginType.Unknown;
        public string OriginKey = string.Empty;
        public SocialPostCategory Category = SocialPostCategory.Diplomatic;
        public int Sentiment;
        public float Credibility = 0.6f;
        public float CredibilityValue = 0.6f;
        public string CredibilityLabel = string.Empty;
        public string SourceLabel = string.Empty;
        public SocialNewsGenerationState GenerationState = SocialNewsGenerationState.Completed;
        public string Headline = string.Empty;
        public string Lead = string.Empty;
        public string Cause = string.Empty;
        public string Process = string.Empty;
        public string Outlook = string.Empty;
        public string Quote = string.Empty;
        public string QuoteAttribution = string.Empty;
        public string LocationName = string.Empty;
        public string Content = string.Empty;
        public string EffectSummary = string.Empty;
        public bool IsFromPlayerDialogue;
        public string IntentHint = string.Empty;
        public string SourceLeaderName = string.Empty;
        public string TargetLeaderName = string.Empty;

        public void ExposeData()
        {
            Scribe_Values.Look(ref PostId, "postId", string.Empty);
            Scribe_Values.Look(ref CreatedTick, "createdTick", 0);
            string sourceFactionId = SourceFaction?.GetUniqueLoadID() ?? string.Empty;
            Scribe_Values.Look(ref sourceFactionId, "sourceFactionId", string.Empty);
            string targetFactionId = TargetFaction?.GetUniqueLoadID() ?? string.Empty;
            Scribe_Values.Look(ref targetFactionId, "targetFactionId", string.Empty);
            // Remove legacy reference nodes from old saves without registering
            // in CrossRefHandler — prevents "Not all loadIDs consumed" on dead factions.
            LegacyScribeHelper.RemoveLegacyReferenceNodes("sourceFaction", "targetFaction");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (!string.IsNullOrEmpty(sourceFactionId))
                    SourceFaction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.GetUniqueLoadID() == sourceFactionId);
                if (!string.IsNullOrEmpty(targetFactionId))
                    TargetFaction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.GetUniqueLoadID() == targetFactionId);
                // If factionId is empty, faction remains null and will be cleaned up.
            }
            Scribe_Values.Look(ref OriginType, "originType", SocialNewsOriginType.Unknown);
            Scribe_Values.Look(ref OriginKey, "originKey", string.Empty);
            Scribe_Values.Look(ref Category, "category", SocialPostCategory.Diplomatic);
            Scribe_Values.Look(ref Sentiment, "sentiment", 0);
            Scribe_Values.Look(ref Credibility, "credibility", 0.6f);
            Scribe_Values.Look(ref CredibilityValue, "credibilityValue", 0.6f);
            Scribe_Values.Look(ref CredibilityLabel, "credibilityLabel", string.Empty);
            Scribe_Values.Look(ref SourceLabel, "sourceLabel", string.Empty);
            Scribe_Values.Look(ref GenerationState, "generationState", SocialNewsGenerationState.Completed);
            Scribe_Values.Look(ref Headline, "headline", string.Empty);
            Scribe_Values.Look(ref Lead, "lead", string.Empty);
            Scribe_Values.Look(ref Cause, "cause", string.Empty);
            Scribe_Values.Look(ref Process, "process", string.Empty);
            Scribe_Values.Look(ref Outlook, "outlook", string.Empty);
            Scribe_Values.Look(ref Quote, "quote", string.Empty);
            Scribe_Values.Look(ref QuoteAttribution, "quoteAttribution", string.Empty);
            Scribe_Values.Look(ref LocationName, "locationName", string.Empty);
            Scribe_Values.Look(ref Content, "content", string.Empty);
            Scribe_Values.Look(ref EffectSummary, "effectSummary", string.Empty);
            Scribe_Values.Look(ref IsFromPlayerDialogue, "isFromPlayerDialogue", false);
            Scribe_Values.Look(ref IntentHint, "intentHint", string.Empty);
            Scribe_Values.Look(ref SourceLeaderName, "sourceLeaderName", string.Empty);
            Scribe_Values.Look(ref TargetLeaderName, "targetLeaderName", string.Empty);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && CredibilityValue <= 0f)
            {
                CredibilityValue = Credibility;
            }
        }
    }
}

