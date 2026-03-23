using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Verse;

namespace RimChat.Memory
{
    /// <summary>
    /// Dependencies: archive cache loader, file IO, and session compression scheduler.
    /// Responsibility: warm up archive cache asynchronously and defer compression scheduling to safe main-thread checkpoints.
    /// </summary>
    public sealed partial class RpgNpcDialogueArchiveManager
    {
        public void BeginPromptMemoryWarmup(Pawn targetNpc, Pawn currentInterlocutor = null)
        {
            int targetPawnLoadId = targetNpc?.thingIDNumber ?? -1;
            if (targetPawnLoadId <= 0)
            {
                return;
            }

            lock (_syncRoot)
            {
                if (_cacheLoaded)
                {
                    _pendingWarmupCompressionTargets.Add(targetPawnLoadId);
                    FlushPendingWarmupCompressionLockless(Find.TickManager?.TicksGame ?? 0);
                    return;
                }

                string saveKey;
                string sourceDir;
                try
                {
                    saveKey = CurrentSaveKey;
                    sourceDir = ResolveArchiveSourceDirectory();
                }
                catch (InvalidOperationException)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(saveKey) ||
                    string.IsNullOrWhiteSpace(sourceDir) ||
                    _warmupInFlightSaveKeys.Contains(saveKey))
                {
                    return;
                }

                _warmupInFlightSaveKeys.Add(saveKey);
                Task.Run(() => WarmupCacheInBackground(saveKey, sourceDir, targetPawnLoadId));
            }
        }

        private void WarmupCacheInBackground(string saveKey, string sourceDir, int targetPawnLoadId)
        {
            Dictionary<int, RpgNpcDialogueArchive> loaded = LoadArchiveSnapshot(sourceDir, saveKey);
            lock (_syncRoot)
            {
                _warmupInFlightSaveKeys.Remove(saveKey);
                if (_cacheLoaded && string.Equals(_loadedSaveKey, saveKey, StringComparison.Ordinal))
                {
                    _pendingWarmupCompressionTargets.Add(targetPawnLoadId);
                    return;
                }

                _archiveCache.Clear();
                foreach (KeyValuePair<int, RpgNpcDialogueArchive> pair in loaded)
                {
                    if (pair.Value == null)
                    {
                        continue;
                    }

                    if (_archiveCache.TryGetValue(pair.Key, out RpgNpcDialogueArchive existing))
                    {
                        MergeArchiveData(existing, pair.Value);
                    }
                    else
                    {
                        _archiveCache[pair.Key] = pair.Value;
                    }
                }

                InvalidatePromptMemoryCacheLockless();
                _loadedSaveKey = saveKey;
                _cacheLoaded = true;
                _pendingWarmupCompressionTargets.Add(targetPawnLoadId);
            }
        }

        private static Dictionary<int, RpgNpcDialogueArchive> LoadArchiveSnapshot(string sourceDir, string saveKey)
        {
            var snapshot = new Dictionary<int, RpgNpcDialogueArchive>();
            if (!Directory.Exists(sourceDir))
            {
                return snapshot;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(sourceDir, "*.json");
            }
            catch
            {
                return snapshot;
            }

            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    string json = File.ReadAllText(files[i]);
                    RpgNpcDialogueArchive archive = RpgNpcDialogueArchiveJsonCodec.ParseJson(json);
                    if (archive == null || archive.PawnLoadId <= 0)
                    {
                        continue;
                    }

                    if (!IsArchiveOwnedBySaveKey(archive, saveKey))
                    {
                        continue;
                    }

                    if (snapshot.TryGetValue(archive.PawnLoadId, out RpgNpcDialogueArchive existing))
                    {
                        MergeArchiveSnapshot(existing, archive);
                    }
                    else
                    {
                        snapshot[archive.PawnLoadId] = archive;
                    }
                }
                catch
                {
                    // Warmup should fail silently and never block gameplay flow.
                }
            }

            return snapshot;
        }

        private static bool IsArchiveOwnedBySaveKey(RpgNpcDialogueArchive archive, string saveKey)
        {
            if (archive == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(archive.SaveKey))
            {
                return true;
            }

            return string.Equals(archive.SaveKey, saveKey, StringComparison.Ordinal);
        }

        private static void MergeArchiveSnapshot(RpgNpcDialogueArchive target, RpgNpcDialogueArchive incoming)
        {
            if (target == null || incoming == null)
            {
                return;
            }

            target.PawnName = string.IsNullOrWhiteSpace(target.PawnName) ? incoming.PawnName : target.PawnName;
            target.FactionId = string.IsNullOrWhiteSpace(target.FactionId) ? incoming.FactionId : target.FactionId;
            target.FactionName = string.IsNullOrWhiteSpace(target.FactionName) ? incoming.FactionName : target.FactionName;
            target.LastInterlocutorName = string.IsNullOrWhiteSpace(target.LastInterlocutorName)
                ? incoming.LastInterlocutorName
                : target.LastInterlocutorName;
            target.LastInterlocutorPawnLoadId = target.LastInterlocutorPawnLoadId > 0
                ? target.LastInterlocutorPawnLoadId
                : incoming.LastInterlocutorPawnLoadId;
            target.LastInteractionTick = Math.Max(target.LastInteractionTick, incoming.LastInteractionTick);
            target.NextTurnSequence = Math.Max(target.NextTurnSequence, incoming.NextTurnSequence);
            target.Sessions ??= new List<RpgNpcDialogueSessionArchive>();
            if (incoming.Sessions != null && incoming.Sessions.Count > 0)
            {
                target.Sessions.AddRange(incoming.Sessions);
            }
        }

        private void FlushPendingWarmupCompressionLockless(int tick)
        {
            if (_pendingWarmupCompressionTargets.Count == 0 || _archiveCache.Count == 0)
            {
                return;
            }

            int safeTick = tick > 0 ? tick : (Find.TickManager?.TicksGame ?? 0);
            List<int> pendingTargets = _pendingWarmupCompressionTargets.ToList();
            _pendingWarmupCompressionTargets.Clear();
            for (int i = 0; i < pendingTargets.Count; i++)
            {
                if (_archiveCache.TryGetValue(pendingTargets[i], out RpgNpcDialogueArchive archive) && archive != null)
                {
                    TryScheduleSessionCompression(archive, safeTick);
                }
            }
        }
    }
}
