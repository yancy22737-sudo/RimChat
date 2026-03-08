using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>/// Dependencies: RimWorld Faction, Verse Scribe.
 /// Responsibility: intent score cache used for optional social auto actions.
 ///</summary>
    public class SocialActionIntent : IExposable
    {
        public Faction Faction;
        public SocialIntentType IntentType = SocialIntentType.Raid;
        public float Score;
        public int LastUpdatedTick;
        public int LastExecuteTick;

        public void ExposeData()
        {
            Scribe_References.Look(ref Faction, "faction");
            Scribe_Values.Look(ref IntentType, "intentType", SocialIntentType.Raid);
            Scribe_Values.Look(ref Score, "score", 0f);
            Scribe_Values.Look(ref LastUpdatedTick, "lastUpdatedTick", 0);
            Scribe_Values.Look(ref LastExecuteTick, "lastExecuteTick", 0);
        }
    }
}
