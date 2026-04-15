using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimChat.Core;
using RimChat.Memory;
using RimChat.WorldState;
using RimWorld;
using Verse;

namespace RimChat.Persistence
{
    /// <summary>
    /// Dependencies: PromptPersistenceService, LeaderMemoryManager, WorldEventLedgerComponent.
    /// Responsibility: build and maintain diplomacy prompt runtime snapshots with frame-budgeted warmup and explicit invalidation.
    /// </summary>
    public sealed class DiplomacyPromptSnapshotCache : IDiplomacyPromptSnapshotCache
    {
        private sealed class CacheEntry
        {
            public DiplomacyPromptRuntimeSnapshot Snapshot;
            public int NextRetryTick;
        }

        private const int RetryDelayTicks = 250;

        private readonly Dictionary<string, CacheEntry> cacheEntries =
            new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        private readonly Queue<Faction> warmupQueue = new Queue<Faction>();
        private readonly HashSet<string> queuedFactionIds = new HashSet<string>(StringComparer.Ordinal);

        private int lastObservedWorldEventRevision = -1;
        private long lastObservedPromptFilesStamp = -1;
        private int lastObservedSettingsSignature = int.MinValue;

        private static readonly DiplomacyPromptSnapshotCache Singleton = new DiplomacyPromptSnapshotCache();

        public static DiplomacyPromptSnapshotCache Instance => Singleton;

        private DiplomacyPromptSnapshotCache()
        {
        }

        public void WarmupOnLoad()
        {
            warmupQueue.Clear();
            queuedFactionIds.Clear();
            cacheEntries.Clear();
            lastObservedWorldEventRevision = ResolveWorldEventRevision();
            lastObservedPromptFilesStamp = ComputePromptFilesStampUtcTicks();
            lastObservedSettingsSignature = ComputeSettingsSignature();
            QueueAllCandidateFactions();
        }

        public bool TryGetSnapshot(Faction faction, out DiplomacyPromptRuntimeSnapshot snapshot)
        {
            snapshot = null;
            if (!IsValidFaction(faction))
            {
                return false;
            }

            RefreshGlobalInvalidationSignals();
            string factionId = faction.GetUniqueLoadID() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(factionId))
            {
                return false;
            }

            if (!cacheEntries.TryGetValue(factionId, out CacheEntry entry))
            {
                RequestWarmup(faction, "cache_miss");
                return false;
            }

            if (entry.Snapshot == null || !IsSnapshotFresh(faction, entry.Snapshot))
            {
                cacheEntries.Remove(factionId);
                RequestWarmup(faction, "stale_snapshot");
                return false;
            }

            snapshot = entry.Snapshot;
            return true;
        }

        public void Invalidate(Faction faction = null, string reason = "manual")
        {
            if (faction == null)
            {
                cacheEntries.Clear();
                warmupQueue.Clear();
                queuedFactionIds.Clear();
                QueueAllCandidateFactions();
                return;
            }

            string factionId = faction.GetUniqueLoadID() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(factionId))
            {
                return;
            }

            cacheEntries.Remove(factionId);
            RequestWarmup(faction, reason);
        }

        public void RequestWarmup(Faction faction, string reason = "request")
        {
            if (!IsValidFaction(faction))
            {
                return;
            }

            string factionId = faction.GetUniqueLoadID() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(factionId) || queuedFactionIds.Contains(factionId))
            {
                return;
            }

            warmupQueue.Enqueue(faction);
            queuedFactionIds.Add(factionId);
        }

        public void Tick(int currentTick, int maxBuildsPerTick = 1)
        {
            if (currentTick <= 0)
            {
                return;
            }

            RefreshGlobalInvalidationSignals();
            int budget = Math.Max(1, maxBuildsPerTick);
            for (int i = 0; i < budget; i++)
            {
                if (!TryDequeueNextBuildTarget(currentTick, out Faction faction))
                {
                    break;
                }

                TryBuildSnapshot(faction, currentTick);
            }
        }

        private static bool IsValidFaction(Faction faction)
        {
            return faction != null && !faction.IsPlayer && !faction.defeated && !(faction.def?.hidden ?? true);
        }

        private void QueueAllCandidateFactions()
        {
            IEnumerable<Faction> factions = Find.FactionManager?.AllFactions
                ?.Where(IsValidFaction)
                ?? Enumerable.Empty<Faction>();
            foreach (Faction faction in factions)
            {
                RequestWarmup(faction, "load_warmup");
            }
        }

        private void RefreshGlobalInvalidationSignals()
        {
            int worldEventRevision = ResolveWorldEventRevision();
            long promptStamp = ComputePromptFilesStampUtcTicks();
            int settingsSignature = ComputeSettingsSignature();

            bool changed = false;
            changed |= lastObservedWorldEventRevision >= 0 && worldEventRevision != lastObservedWorldEventRevision;
            changed |= lastObservedPromptFilesStamp >= 0 && promptStamp != lastObservedPromptFilesStamp;
            changed |= lastObservedSettingsSignature != int.MinValue && settingsSignature != lastObservedSettingsSignature;

            lastObservedWorldEventRevision = worldEventRevision;
            lastObservedPromptFilesStamp = promptStamp;
            lastObservedSettingsSignature = settingsSignature;

            if (!changed)
            {
                return;
            }

            cacheEntries.Clear();
            warmupQueue.Clear();
            queuedFactionIds.Clear();
            QueueAllCandidateFactions();
        }

        private bool TryDequeueNextBuildTarget(int currentTick, out Faction faction)
        {
            faction = null;
            while (warmupQueue.Count > 0)
            {
                Faction candidate = warmupQueue.Dequeue();
                string candidateId = candidate?.GetUniqueLoadID() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(candidateId))
                {
                    queuedFactionIds.Remove(candidateId);
                }

                if (!IsValidFaction(candidate))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(candidateId) &&
                    cacheEntries.TryGetValue(candidateId, out CacheEntry entry) &&
                    entry.NextRetryTick > currentTick)
                {
                    RequestWarmup(candidate, "retry_deferred");
                    continue;
                }

                faction = candidate;
                return true;
            }

            return false;
        }

        private void TryBuildSnapshot(Faction faction, int currentTick)
        {
            string factionId = faction.GetUniqueLoadID() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(factionId))
            {
                return;
            }

            try
            {
                int memoryRevision = LeaderMemoryManager.Instance.GetFactionMemoryRevision(faction);
                int worldEventRevision = ResolveWorldEventRevision();
                long promptStamp = ComputePromptFilesStampUtcTicks();
                int settingsSignature = ComputeSettingsSignature();
                var snapshot = PromptPersistenceService.Instance.BuildRuntimeSnapshotForFaction(
                    faction,
                    null,
                    currentTick,
                    memoryRevision,
                    worldEventRevision,
                    promptStamp,
                    settingsSignature);
                if (snapshot == null)
                {
                    cacheEntries[factionId] = new CacheEntry
                    {
                        Snapshot = null,
                        NextRetryTick = currentTick + RetryDelayTicks
                    };
                    return;
                }

                cacheEntries[factionId] = new CacheEntry
                {
                    Snapshot = snapshot,
                    NextRetryTick = 0
                };
            }
            catch (Exception ex)
            {
                cacheEntries[factionId] = new CacheEntry
                {
                    Snapshot = null,
                    NextRetryTick = currentTick + RetryDelayTicks
                };
                Log.Warning($"[RimChat] Prompt snapshot warmup failed for {faction?.Name ?? "Unknown"}: {ex.Message}");
            }
        }

        private bool IsSnapshotFresh(Faction faction, DiplomacyPromptRuntimeSnapshot snapshot)
        {
            if (snapshot == null || faction == null)
            {
                return false;
            }

            string currentFactionId = faction.GetUniqueLoadID() ?? string.Empty;
            if (!string.Equals(snapshot.FactionLoadId, currentFactionId, StringComparison.Ordinal))
            {
                return false;
            }

            if (snapshot.PlayerGoodwill != faction.PlayerGoodwill)
            {
                return false;
            }

            if (snapshot.PlayerRelationKind != faction.RelationKindWith(Faction.OfPlayer))
            {
                return false;
            }

            if (snapshot.MemoryRevision != LeaderMemoryManager.Instance.GetFactionMemoryRevision(faction))
            {
                return false;
            }

            if (snapshot.WorldEventRevision != ResolveWorldEventRevision())
            {
                return false;
            }

            if (snapshot.PromptFilesStampUtcTicks != ComputePromptFilesStampUtcTicks())
            {
                return false;
            }

            if (snapshot.SettingsSignature != ComputeSettingsSignature())
            {
                return false;
            }

            return true;
        }

        private static int ResolveWorldEventRevision()
        {
            WorldEventLedgerComponent ledger = WorldEventLedgerComponent.Instance;
            if (ledger == null)
            {
                return 0;
            }

            List<WorldEventRecord> worldEvents = ledger.GetRecentWorldEvents(
                null,
                daysWindow: 120,
                includePublic: true,
                includeDirect: true);
            List<RaidBattleReportRecord> raidReports = ledger.GetRecentRaidBattleReports(
                null,
                daysWindow: 120,
                includeDirect: true);

            int latestWorldEventTick = worldEvents?.Count > 0
                ? worldEvents[0]?.OccurredTick ?? 0
                : 0;
            int latestRaidTick = raidReports?.Count > 0
                ? raidReports[0]?.BattleEndTick ?? 0
                : 0;

            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (worldEvents?.Count ?? 0);
                hash = hash * 31 + (raidReports?.Count ?? 0);
                hash = hash * 31 + latestWorldEventTick;
                hash = hash * 31 + latestRaidTick;
                return hash;
            }
        }

        private static int ComputeSettingsSignature()
        {
            RimChatSettings settings = RimChatMod.Instance?.InstanceSettings;
            if (settings == null)
            {
                return 0;
            }

            unchecked
            {
                int hash = 17;
                hash = hash * 31 + settings.DialogueStyleMode.GetHashCode();
                hash = hash * 31 + settings.EnableSocialCircle.GetHashCode();
                hash = hash * 31 + settings.EnableAISimulationNews.GetHashCode();
                hash = hash * 31 + settings.EnablePlayerInfluenceNews.GetHashCode();
                hash = hash * 31 + settings.EnableNpcInitiatedDialogue.GetHashCode();
                return hash;
            }
        }

        private static long ComputePromptFilesStampUtcTicks()
        {
            long maxTicks = 0L;
            foreach (string path in EnumeratePromptFilePaths())
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    continue;
                }

                long ticks = File.GetLastWriteTimeUtc(path).Ticks;
                if (ticks > maxTicks)
                {
                    maxTicks = ticks;
                }
            }

            return maxTicks;
        }

        private static IEnumerable<string> EnumeratePromptFilePaths()
        {
            yield return PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.SystemPromptDefaultFileName);
            yield return PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.DiplomacyPromptDefaultFileName);
            yield return PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.SocialCirclePromptDefaultFileName);
            yield return PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.FactionPromptDefaultFileName);
            yield return PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.PromptUnifiedDefaultFileName);
            yield return PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SystemPromptCustomFileName);
            yield return PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.DiplomacyPromptCustomFileName);
            yield return PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SocialCirclePromptCustomFileName);
            yield return PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.FactionPromptCustomFileName);
            yield return PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.PromptUnifiedCustomFileName);
        }
    }
}
