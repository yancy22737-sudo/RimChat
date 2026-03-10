using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>/// Dependencies: social post/intent models, Verse Scribe.
 /// Responsibility: all persisted social circle state for one save.
 ///</summary>
    public class SocialCircleState : IExposable
    {
        private const int MaxProcessedOrigins = 512;

        public List<PublicSocialPost> Posts = new List<PublicSocialPost>();
        public List<SocialActionIntent> ActionIntents = new List<SocialActionIntent>();
        public List<SocialFactionActionCooldown> FactionActionCooldowns = new List<SocialFactionActionCooldown>();
        public List<SocialProcessedOrigin> ProcessedOrigins = new List<SocialProcessedOrigin>();
        public int NextPostTick;
        public string LastReadPostId = string.Empty;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref Posts, "posts", LookMode.Deep);
            Scribe_Collections.Look(ref ActionIntents, "actionIntents", LookMode.Deep);
            Scribe_Collections.Look(ref FactionActionCooldowns, "factionActionCooldowns", LookMode.Deep);
            Scribe_Collections.Look(ref ProcessedOrigins, "processedOrigins", LookMode.Deep);
            Scribe_Values.Look(ref NextPostTick, "nextPostTick", 0);
            Scribe_Values.Look(ref LastReadPostId, "lastReadPostId", string.Empty);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Posts = Posts ?? new List<PublicSocialPost>();
                ActionIntents = ActionIntents ?? new List<SocialActionIntent>();
                FactionActionCooldowns = FactionActionCooldowns ?? new List<SocialFactionActionCooldown>();
                ProcessedOrigins = ProcessedOrigins ?? new List<SocialProcessedOrigin>();
                CleanupInvalidEntries();
                ClearPendingOrigins();
            }
        }

        public void CleanupInvalidEntries()
        {
            Posts.RemoveAll(p => p == null || (p.SourceFaction != null && p.SourceFaction.defeated));
            ActionIntents.RemoveAll(i => i == null || i.Faction == null || i.Faction.defeated || i.Score <= 0.001f);
            FactionActionCooldowns.RemoveAll(c => c == null || c.Faction == null || c.Faction.defeated);
            ProcessedOrigins.RemoveAll(item => item == null || string.IsNullOrWhiteSpace(item.OriginKey));
            TrimProcessedOrigins();
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

        public bool HasHandledOrigin(SocialNewsOriginType originType, string originKey)
        {
            return FindProcessedOrigin(originType, originKey) != null;
        }

        public void MarkOriginState(
            SocialNewsOriginType originType,
            string originKey,
            SocialNewsGenerationState state,
            int processedTick)
        {
            if (string.IsNullOrWhiteSpace(originKey))
            {
                return;
            }

            SocialProcessedOrigin entry = FindProcessedOrigin(originType, originKey);
            if (entry == null)
            {
                entry = new SocialProcessedOrigin
                {
                    OriginType = originType,
                    OriginKey = originKey
                };
                ProcessedOrigins.Add(entry);
            }

            entry.State = state;
            entry.ProcessedTick = processedTick;
            TrimProcessedOrigins();
        }

        public void ClearPendingOrigins()
        {
            ProcessedOrigins.RemoveAll(item => item != null && item.State == SocialNewsGenerationState.Pending);
        }

        private SocialProcessedOrigin FindProcessedOrigin(SocialNewsOriginType originType, string originKey)
        {
            if (string.IsNullOrWhiteSpace(originKey))
            {
                return null;
            }

            return ProcessedOrigins.FirstOrDefault(item =>
                item != null &&
                item.OriginType == originType &&
                string.Equals(item.OriginKey, originKey, System.StringComparison.Ordinal));
        }

        private void TrimProcessedOrigins()
        {
            if (ProcessedOrigins.Count <= MaxProcessedOrigins)
            {
                return;
            }

            ProcessedOrigins = ProcessedOrigins
                .OrderByDescending(item => item?.ProcessedTick ?? 0)
                .Take(MaxProcessedOrigins)
                .ToList();
        }
    }

    /// <summary>/// Dependencies: RimWorld Faction, Verse Scribe.
 /// Responsibility: per-faction cooldown guard for social auto actions.
 ///</summary>
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

    /// <summary>/// Dependencies: Verse Scribe.
 /// Responsibility: persist processed social-news origins to prevent duplicate reporting.
 ///</summary>
    public class SocialProcessedOrigin : IExposable
    {
        public SocialNewsOriginType OriginType = SocialNewsOriginType.Unknown;
        public string OriginKey = string.Empty;
        public SocialNewsGenerationState State = SocialNewsGenerationState.Pending;
        public int ProcessedTick;

        public void ExposeData()
        {
            Scribe_Values.Look(ref OriginType, "originType", SocialNewsOriginType.Unknown);
            Scribe_Values.Look(ref OriginKey, "originKey", string.Empty);
            Scribe_Values.Look(ref State, "state", SocialNewsGenerationState.Pending);
            Scribe_Values.Look(ref ProcessedTick, "processedTick", 0);
        }
    }
}
