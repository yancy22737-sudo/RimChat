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

        // Runtime O(1) indexes, rebuilt on PostLoadInit and maintained incrementally.
        [Unsaved] private Dictionary<string, SocialProcessedOrigin> processedOriginsByKey;
        [Unsaved] private Dictionary<Faction, SocialFactionActionCooldown> factionActionCooldownsByFaction;
        [Unsaved] public HashSet<string> PublishedPostOriginKeys = new HashSet<string>();

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
                RebuildRuntimeIndexes();
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
            RebuildRuntimeIndexes();
        }

        public int GetFactionNextActionTick(Faction faction)
        {
            if (faction == null) return 0;
            if (factionActionCooldownsByFaction != null &&
                factionActionCooldownsByFaction.TryGetValue(faction, out var entry))
                return entry.NextActionAllowedTick;
            return 0;
        }

        public void SetFactionNextActionTick(Faction faction, int tick)
        {
            if (faction == null) return;
            SocialFactionActionCooldown entry;
            if (factionActionCooldownsByFaction != null &&
                factionActionCooldownsByFaction.TryGetValue(faction, out entry))
            {
                entry.NextActionAllowedTick = tick;
                return;
            }

            entry = new SocialFactionActionCooldown { Faction = faction, NextActionAllowedTick = tick };
            FactionActionCooldowns.Add(entry);
            if (factionActionCooldownsByFaction != null)
                factionActionCooldownsByFaction[faction] = entry;
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

            string key = BuildOriginKey(originType, originKey);
            SocialProcessedOrigin entry;
            if (processedOriginsByKey != null && processedOriginsByKey.TryGetValue(key, out entry))
            {
                entry.State = state;
                entry.ProcessedTick = processedTick;
                TrimProcessedOrigins();
                return;
            }

            entry = new SocialProcessedOrigin
            {
                OriginType = originType,
                OriginKey = originKey,
                State = state,
                ProcessedTick = processedTick
            };
            ProcessedOrigins.Add(entry);
            if (processedOriginsByKey != null)
                processedOriginsByKey[key] = entry;
            TrimProcessedOrigins();
        }

        public void ClearPendingOrigins()
        {
            for (int i = ProcessedOrigins.Count - 1; i >= 0; i--)
            {
                var item = ProcessedOrigins[i];
                if (item != null && item.State == SocialNewsGenerationState.Pending)
                {
                    ProcessedOrigins.RemoveAt(i);
                    if (processedOriginsByKey != null && !string.IsNullOrWhiteSpace(item.OriginKey))
                        processedOriginsByKey.Remove(BuildOriginKey(item.OriginType, item.OriginKey));
                }
            }
        }

        private SocialProcessedOrigin FindProcessedOrigin(SocialNewsOriginType originType, string originKey)
        {
            if (string.IsNullOrWhiteSpace(originKey))
            {
                return null;
            }

            if (processedOriginsByKey != null &&
                processedOriginsByKey.TryGetValue(BuildOriginKey(originType, originKey), out var entry))
            {
                return entry;
            }

            return null;
        }

        private static string BuildOriginKey(SocialNewsOriginType originType, string originKey)
        {
            return $"{(int)originType}:{originKey}";
        }

        private void RebuildRuntimeIndexes()
        {
            processedOriginsByKey = new Dictionary<string, SocialProcessedOrigin>();
            for (int i = 0; i < ProcessedOrigins.Count; i++)
            {
                var item = ProcessedOrigins[i];
                if (item != null && !string.IsNullOrWhiteSpace(item.OriginKey))
                    processedOriginsByKey[BuildOriginKey(item.OriginType, item.OriginKey)] = item;
            }

            factionActionCooldownsByFaction = new Dictionary<Faction, SocialFactionActionCooldown>();
            for (int i = 0; i < FactionActionCooldowns.Count; i++)
            {
                var item = FactionActionCooldowns[i];
                if (item?.Faction != null)
                    factionActionCooldownsByFaction[item.Faction] = item;
            }

            PublishedPostOriginKeys.Clear();
            for (int i = 0; i < Posts.Count; i++)
            {
                var post = Posts[i];
                if (post != null && !string.IsNullOrWhiteSpace(post.OriginKey))
                    PublishedPostOriginKeys.Add(BuildOriginKey(post.OriginType, post.OriginKey));
            }
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
            RebuildRuntimeIndexes();
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
