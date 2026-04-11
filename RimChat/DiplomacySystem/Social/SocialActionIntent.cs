using System.Linq;
using RimChat.Util;
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
            string factionId = Faction?.GetUniqueLoadID() ?? string.Empty;
            Scribe_Values.Look(ref factionId, "factionId", string.Empty);
            // Remove legacy <faction> reference node from old saves without registering
            // in CrossRefHandler — prevents "Not all loadIDs consumed" on dead factions.
            LegacyScribeHelper.RemoveLegacyReferenceNode("faction");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (!string.IsNullOrEmpty(factionId))
                {
                    Faction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.GetUniqueLoadID() == factionId);
                }
            }
            Scribe_Values.Look(ref IntentType, "intentType", SocialIntentType.Raid);
            Scribe_Values.Look(ref Score, "score", 0f);
            Scribe_Values.Look(ref LastUpdatedTick, "lastUpdatedTick", 0);
            Scribe_Values.Look(ref LastExecuteTick, "lastExecuteTick", 0);
        }
    }
}
