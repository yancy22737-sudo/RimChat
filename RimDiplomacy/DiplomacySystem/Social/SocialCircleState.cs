using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimDiplomacy.DiplomacySystem
{
    /// <summary>
    /// Dependencies: social post/intent models, Verse Scribe.
    /// Responsibility: all persisted social circle state for one save.
    /// </summary>
    public class SocialCircleState : IExposable
    {
        public List<PublicSocialPost> Posts = new List<PublicSocialPost>();
        public List<SocialActionIntent> ActionIntents = new List<SocialActionIntent>();
        public List<SocialFactionActionCooldown> FactionActionCooldowns = new List<SocialFactionActionCooldown>();
        public int NextPostTick;
        public string LastReadPostId = string.Empty;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref Posts, "posts", LookMode.Deep);
            Scribe_Collections.Look(ref ActionIntents, "actionIntents", LookMode.Deep);
            Scribe_Collections.Look(ref FactionActionCooldowns, "factionActionCooldowns", LookMode.Deep);
            Scribe_Values.Look(ref NextPostTick, "nextPostTick", 0);
            Scribe_Values.Look(ref LastReadPostId, "lastReadPostId", string.Empty);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Posts = Posts ?? new List<PublicSocialPost>();
                ActionIntents = ActionIntents ?? new List<SocialActionIntent>();
                FactionActionCooldowns = FactionActionCooldowns ?? new List<SocialFactionActionCooldown>();
                CleanupInvalidEntries();
            }
        }

        public void CleanupInvalidEntries()
        {
            Posts.RemoveAll(p => p == null || p.SourceFaction == null || p.SourceFaction.defeated);
            ActionIntents.RemoveAll(i => i == null || i.Faction == null || i.Faction.defeated || i.Score <= 0.001f);
            FactionActionCooldowns.RemoveAll(c => c == null || c.Faction == null || c.Faction.defeated);
        }

        public int GetFactionNextActionTick(Faction faction)
        {
            if (faction == null) return 0;
            SocialFactionActionCooldown entry = FactionActionCooldowns.FirstOrDefault(e => e.Faction == faction);
            return entry?.NextActionAllowedTick ?? 0;
        }

        public void SetFactionNextActionTick(Faction faction, int tick)
        {
            if (faction == null) return;
            SocialFactionActionCooldown entry = FactionActionCooldowns.FirstOrDefault(e => e.Faction == faction);
            if (entry == null)
            {
                entry = new SocialFactionActionCooldown { Faction = faction };
                FactionActionCooldowns.Add(entry);
            }
            entry.NextActionAllowedTick = tick;
        }
    }

    /// <summary>
    /// Dependencies: RimWorld Faction, Verse Scribe.
    /// Responsibility: per-faction cooldown guard for social auto actions.
    /// </summary>
    public class SocialFactionActionCooldown : IExposable
    {
        public Faction Faction;
        public int NextActionAllowedTick;

        public void ExposeData()
        {
            Scribe_References.Look(ref Faction, "faction");
            Scribe_Values.Look(ref NextActionAllowedTick, "nextActionAllowedTick", 0);
        }
    }
}
