using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: RimWorld Faction, Verse Scribe.
    /// Responsibility: serializable public social post record.
    /// </summary>
    public class PublicSocialPost : IExposable
    {
        public string PostId = string.Empty;
        public int CreatedTick;
        public Faction SourceFaction;
        public Faction TargetFaction;
        public SocialPostCategory Category = SocialPostCategory.Diplomatic;
        public int Sentiment;
        public float Credibility = 0.6f;
        public string Content = string.Empty;
        public string EffectSummary = string.Empty;
        public bool IsFromPlayerDialogue;
        public string IntentHint = string.Empty;
        public string SourceLeaderName = string.Empty;
        public string TargetLeaderName = string.Empty;
        public SocialPostImpactType ImpactType = SocialPostImpactType.Goodwill;
        public int ImpactMagnitude = 1;
        public int BaseLikeCount;
        public int CurrentLikeCount;
        public bool LikedByPlayer;

        public void ExposeData()
        {
            Scribe_Values.Look(ref PostId, "postId", string.Empty);
            Scribe_Values.Look(ref CreatedTick, "createdTick", 0);
            Scribe_References.Look(ref SourceFaction, "sourceFaction");
            Scribe_References.Look(ref TargetFaction, "targetFaction");
            Scribe_Values.Look(ref Category, "category", SocialPostCategory.Diplomatic);
            Scribe_Values.Look(ref Sentiment, "sentiment", 0);
            Scribe_Values.Look(ref Credibility, "credibility", 0.6f);
            Scribe_Values.Look(ref Content, "content", string.Empty);
            Scribe_Values.Look(ref EffectSummary, "effectSummary", string.Empty);
            Scribe_Values.Look(ref IsFromPlayerDialogue, "isFromPlayerDialogue", false);
            Scribe_Values.Look(ref IntentHint, "intentHint", string.Empty);
            Scribe_Values.Look(ref SourceLeaderName, "sourceLeaderName", string.Empty);
            Scribe_Values.Look(ref TargetLeaderName, "targetLeaderName", string.Empty);
            Scribe_Values.Look(ref ImpactType, "impactType", SocialPostImpactType.Goodwill);
            Scribe_Values.Look(ref ImpactMagnitude, "impactMagnitude", 1);
            Scribe_Values.Look(ref BaseLikeCount, "baseLikeCount", 0);
            Scribe_Values.Look(ref CurrentLikeCount, "currentLikeCount", 0);
            Scribe_Values.Look(ref LikedByPlayer, "likedByPlayer", false);
        }
    }
}

