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
            public bool NeedsRefresh;
            public int NeedsRefreshSinceTick;
            public int LastValidatedTick;
        }

        private const int RetryDelayTicks = 250;
        private const int ValidationThrottleTicks = 150;
        private const int RefreshGracePeriodTicks = 1500;

        private readonly Dictionary<string, CacheEntry> cacheEntries =
            new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        private readonly Queue<Faction> warmupQueue = new Queue<Faction>();
        private readonly HashSet<string> queuedFactionIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly PromptFileStampCache _fileStampCache = new PromptFileStampCache();

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
            lastObservedPromptFilesStamp = _fileStampCache.GetStamp(Find.TickManager?.TicksGame ?? 0);
            lastObservedSettingsSignature = ComputeSettingsSignature();
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            _fileStampCache.Prime(currentTick);
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

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (entry.Snapshot == null || !ValidateSnapshot(faction, entry, currentTick))
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

            var refreshTargets = cacheEntries
                .Where(kvp => kvp.Value.NeedsRefresh && kvp.Value.NextRetryTick <= currentTick)
                .Select(kvp => FindFactionByLoadId(kvp.Key))
                .Where(f => f != null)
                .Take(budget)
                .ToList();

            foreach (Faction faction in refreshTargets)
            {
                TryBuildSnapshot(faction, currentTick);
                budget--;
            }

            for (int i = 0; i < budget; i++)
            {
                if (!TryDequeueNextBuildTarget(currentTick, out Faction faction))
                {
                    break;
                }

                TryBuildSnapshot(faction, currentTick);
            }
        }

        private static Faction FindFactionByLoadId(string factionId)
        {
            if (string.IsNullOrWhiteSpace(factionId))
            {
                return null;
            }

            List<Faction> factions = Find.FactionManager?.AllFactionsListForReading;
            if (factions == null)
            {
                return null;
            }

            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                if (f != null && string.Equals(f.GetUniqueLoadID(), factionId, StringComparison.Ordinal))
                {
                    return f;
                }
            }

            return null;
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
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            int worldEventRevision = ResolveWorldEventRevision();
            long promptStamp = _fileStampCache.GetStamp(currentTick);
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
                long promptStamp = _fileStampCache.GetStamp(currentTick);
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
                    NextRetryTick = 0,
                    NeedsRefresh = false,
                    NeedsRefreshSinceTick = 0,
                    LastValidatedTick = currentTick
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

        private bool ValidateSnapshot(Faction faction, CacheEntry entry, int currentTick)
        {
            DiplomacyPromptRuntimeSnapshot snapshot = entry.Snapshot;
            if (snapshot == null || faction == null)
            {
                return false;
            }

            string currentFactionId = faction.GetUniqueLoadID() ?? string.Empty;
            if (!string.Equals(snapshot.FactionLoadId, currentFactionId, StringComparison.Ordinal))
            {
                return false;
            }

            if (snapshot.PlayerRelationKind != faction.RelationKindWith(Faction.OfPlayer))
            {
                return false;
            }

            if (currentTick - entry.LastValidatedTick < ValidationThrottleTicks)
            {
                return true;
            }

            entry.LastValidatedTick = currentTick;

            bool l2Changed = snapshot.PlayerGoodwill != faction.PlayerGoodwill
                          || snapshot.MemoryRevision != LeaderMemoryManager.Instance.GetFactionMemoryRevision(faction);

            bool l3Changed = snapshot.WorldEventRevision != ResolveWorldEventRevision()
                          || snapshot.PromptFilesStampUtcTicks != _fileStampCache.GetStamp(currentTick)
                          || snapshot.SettingsSignature != ComputeSettingsSignature();

            if (l2Changed || l3Changed)
            {
                entry.NeedsRefresh = true;
                if (entry.NeedsRefreshSinceTick <= 0)
                {
                    entry.NeedsRefreshSinceTick = currentTick;
                }

                if (currentTick - entry.NeedsRefreshSinceTick > RefreshGracePeriodTicks)
                {
                    Log.Warning($"[RimChat] Snapshot for {faction.Name} expired after {RefreshGracePeriodTicks} ticks grace period, forcing rebuild.");
                    return false;
                }

                RequestWarmup(faction, l2Changed ? "l2_data_changed" : "l3_config_changed");
            }

            return true;
        }

        private static int ResolveWorldEventRevision()
        {
            return WorldEventLedgerComponent.GlobalEventRevision;
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
