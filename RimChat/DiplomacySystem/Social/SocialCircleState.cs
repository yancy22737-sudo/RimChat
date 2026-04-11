using System.Collections.Generic;
using System.Linq;
using RimChat.Util;
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
        private const int MaxScheduledEvents = 512;

        public List<PublicSocialPost> Posts = new List<PublicSocialPost>();
        public List<SocialActionIntent> ActionIntents = new List<SocialActionIntent>();
        public List<SocialFactionActionCooldown> FactionActionCooldowns = new List<SocialFactionActionCooldown>();
        public List<SocialProcessedOrigin> ProcessedOrigins = new List<SocialProcessedOrigin>();
        public List<ScheduledSocialEventRecord> ScheduledEvents = new List<ScheduledSocialEventRecord>();
        public int NextPostTick;
        public string LastReadPostId = string.Empty;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref Posts, "posts", LookMode.Deep);
            Scribe_Collections.Look(ref ActionIntents, "actionIntents", LookMode.Deep);
            Scribe_Collections.Look(ref FactionActionCooldowns, "factionActionCooldowns", LookMode.Deep);
            Scribe_Collections.Look(ref ProcessedOrigins, "processedOrigins", LookMode.Deep);
            Scribe_Collections.Look(ref ScheduledEvents, "scheduledEvents", LookMode.Deep);
            Scribe_Values.Look(ref NextPostTick, "nextPostTick", 0);
            Scribe_Values.Look(ref LastReadPostId, "lastReadPostId", string.Empty);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Posts = Posts ?? new List<PublicSocialPost>();
                ActionIntents = ActionIntents ?? new List<SocialActionIntent>();
                FactionActionCooldowns = FactionActionCooldowns ?? new List<SocialFactionActionCooldown>();
                ProcessedOrigins = ProcessedOrigins ?? new List<SocialProcessedOrigin>();
                ScheduledEvents = ScheduledEvents ?? new List<ScheduledSocialEventRecord>();
                CleanupInvalidEntries();
                ClearPendingOrigins();
            }
        }

        public void CleanupInvalidEntries()
        {
            Posts.RemoveAll(p =>
                p == null ||
                (p.SourceFaction != null && p.SourceFaction.defeated));
            ActionIntents.RemoveAll(i => i == null || i.Faction == null || i.Faction.defeated || i.Score <= 0.001f);
            FactionActionCooldowns.RemoveAll(c => c == null || c.Faction == null || c.Faction.defeated);
            ProcessedOrigins.RemoveAll(item => item == null || string.IsNullOrWhiteSpace(item.OriginKey));
            ScheduledEvents.RemoveAll(item =>
                item == null ||
                string.IsNullOrWhiteSpace(item.SourceKey) ||
                item.OccurredTick <= 0);
            TrimProcessedOrigins();
            TrimScheduledEvents();
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

        public bool HasHandledOrigin(SocialNewsOriginType originType, string originKey, bool includeFailed = true)
        {
            SocialProcessedOrigin entry = FindProcessedOrigin(originType, originKey);
            if (entry == null)
            {
                return false;
            }

            return includeFailed || entry.State != SocialNewsGenerationState.Failed;
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

        public void AddScheduledEvent(ScheduledSocialEventRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.SourceKey) || record.OccurredTick <= 0)
            {
                return;
            }

            ScheduledEvents ??= new List<ScheduledSocialEventRecord>();
            ScheduledEvents.RemoveAll(item =>
                item != null &&
                string.Equals(item.SourceKey, record.SourceKey, System.StringComparison.Ordinal));
            ScheduledEvents.Add(record);
            TrimScheduledEvents();
        }

        public List<ScheduledSocialEventRecord> GetRecentScheduledEvents(int minTickInclusive)
        {
            if (ScheduledEvents == null || ScheduledEvents.Count == 0)
            {
                return new List<ScheduledSocialEventRecord>();
            }

            return ScheduledEvents
                .Where(item => item != null && item.OccurredTick >= minTickInclusive)
                .OrderByDescending(item => item.OccurredTick)
                .ToList();
        }

        private void TrimScheduledEvents()
        {
            if (ScheduledEvents == null || ScheduledEvents.Count <= MaxScheduledEvents)
            {
                return;
            }

            ScheduledEvents = ScheduledEvents
                .OrderByDescending(item => item?.OccurredTick ?? 0)
                .Take(MaxScheduledEvents)
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

    /// <summary>/// Dependencies: social-circle scheduler seed collectors.
 /// Responsibility: classify persistent periodic social event records.
 ///</summary>
    public enum ScheduledSocialEventType
    {
        Unknown = 0,
        QuestResult = 1,
        TradeDeal = 2,
        GoodwillShift = 3,
        RelationShift = 4,
        AidArrival = 5
    }

    /// <summary>/// Dependencies: Verse Scribe.
 /// Responsibility: persist one structured event used as scheduled social-news seed input.
 ///</summary>
    public class ScheduledSocialEventRecord : IExposable
    {
        public ScheduledSocialEventType EventType = ScheduledSocialEventType.Unknown;
        public string SourceKey = string.Empty;
        public int OccurredTick;
        public Faction SourceFaction;
        public Faction TargetFaction;
        public string Summary = string.Empty;
        public string Detail = string.Empty;
        public int Value;

        public void ExposeData()
        {
            Scribe_Values.Look(ref EventType, "eventType", ScheduledSocialEventType.Unknown);
            Scribe_Values.Look(ref SourceKey, "sourceKey", string.Empty);
            Scribe_Values.Look(ref OccurredTick, "occurredTick", 0);
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
            Scribe_Values.Look(ref Summary, "summary", string.Empty);
            Scribe_Values.Look(ref Detail, "detail", string.Empty);
            Scribe_Values.Look(ref Value, "value", 0);
        }
    }
}
