using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using RimChat.AI;
using RimChat.Core;
using RimChat.DiplomacySystem;
using RimWorld;
using Verse;

namespace RimChat.Memory
{
    /// <summary>/// Dependencies: GameComponent_RPGManager, RimWorld save path, NPC dialogue turn feed.
 /// Responsibility: persist RPG dialogue archives per NPC into independent JSON files.
 ///</summary>
    public sealed partial class RpgNpcDialogueArchiveManager
    {
        private const string SaveRootDir = "RimChat";
        private const string SaveSubDir = "save_data";
        private const string NpcArchiveSubDir = "rpg_npc_dialogues";
        private const string PromptFolderName = "Prompt";
        private const string NpcPromptSubDir = "NPC";
        private const string DefaultSaveName = "Default";
        private const string LegacyMigrationBackupDirName = "_migration_backup";
        private const string LegacyDefaultBucketClaimMarker = ".legacy_default_bucket_claimed";
        private const BindingFlags InstanceStringMemberBinding =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
        private const BindingFlags StaticStringMemberBinding =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
        private const string DiplomacySummaryPrefix = "[DiplomacySummary] ";
        private const int MaxTurnsPerNpc = 300;
        private const int MaxSessionsPerNpc = 96;
        private const int CompressionRetryCooldownTicks = 2500;
        private const int MaxCompressionRequestsPerPass = 2;
        private const int CompressedSummaryMaxChars = 220;
        private const int MaxInjectedCompressedSessionSummaries = 4;
        private const int MaxInjectedCompressedSessionSummaryChars = 900;

        private static RpgNpcDialogueArchiveManager _instance;
        public static RpgNpcDialogueArchiveManager Instance => _instance ?? (_instance = new RpgNpcDialogueArchiveManager());

        private readonly Dictionary<int, RpgNpcDialogueArchive> _archiveCache = new Dictionary<int, RpgNpcDialogueArchive>();
        private readonly HashSet<string> _compressionInFlight = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _warmupInFlightSaveKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<int> _pendingWarmupCompressionTargets = new HashSet<int>();
        private readonly object _syncRoot = new object();
        private bool _cacheLoaded;
        private string _loadedSaveKey = string.Empty;
        private string _resolvedSaveKey = string.Empty;
        private string _lastResolvedSaveName = string.Empty;

        public void OnNewGame()
        {
            lock (_syncRoot)
            {
                _archiveCache.Clear();
                _compressionInFlight.Clear();
                ResetPromptMemoryCacheLockless();
                _cacheLoaded = false;
                _loadedSaveKey = string.Empty;
                _resolvedSaveKey = string.Empty;
                EnsureCacheLoaded();
            }
        }

        public void OnLoadedGame()
        {
            lock (_syncRoot)
            {
                _archiveCache.Clear();
                _compressionInFlight.Clear();
                _warmupInFlightSaveKeys.Clear();
                _pendingWarmupCompressionTargets.Clear();
                ResetPromptMemoryCacheLockless();
                _cacheLoaded = false;
                _loadedSaveKey = string.Empty;
                _resolvedSaveKey = string.Empty;
            }
        }

        public void OnAfterGameLoad()
        {
            lock (_syncRoot)
            {
                EnsureCacheLoaded();
                ApplyArchivesToRuntime();
            }
        }

        public void OnBeforeGameSave()
        {
            if (!TryValidatePersistenceContext(nameof(OnBeforeGameSave)))
            {
                return;
            }

            lock (_syncRoot)
            {
                EnsureCacheLoaded();
                InvalidatePromptMemoryCacheLockless();
                foreach (RpgNpcDialogueArchive archive in _archiveCache.Values)
                {
                    TryScheduleSessionCompression(archive, triggerTick: Find.TickManager?.TicksGame ?? 0);
                    SaveArchiveToFile(archive);
                }
            }
        }

        public void RecordTurn(Pawn initiator, Pawn targetNpc, bool isPlayerSpeaker, string text, int tick, string sessionId = null)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }
            if (!TryValidatePersistenceContext(nameof(RecordTurn)))
            {
                return;
            }

            lock (_syncRoot)
            {
                EnsureCacheLoaded();
                bool archiveMutated = false;
                List<Pawn> participants = CollectArchiveParticipants(initiator, targetNpc);
                for (int i = 0; i < participants.Count; i++)
                {
                    Pawn participant = participants[i];
                    RpgNpcDialogueArchive archive = GetOrCreateArchive(participant, tick);
                    if (archive == null)
                    {
                        continue;
                    }

                    archive.LastInteractionTick = tick;
                    archive.PawnName = ResolvePawnName(participant);
                    archive.FactionId = BuildFactionId(participant.Faction);
                    archive.FactionName = participant.Faction?.Name ?? string.Empty;
                    Pawn counterpart = ResolveCounterpartPawn(participant, initiator, targetNpc);
                    archive.LastInterlocutorPawnLoadId = counterpart?.thingIDNumber ?? -1;
                    archive.LastInterlocutorName = ResolvePawnName(counterpart);
                    long sequence = AllocateTurnSequence(archive);
                    RpgNpcDialogueSessionArchive session = GetOrCreateSession(archive, sessionId, counterpart, tick);
                    PrepareSessionForTurnAppend(session);
                    session.Turns.Add(BuildTurnArchive(initiator, targetNpc, isPlayerSpeaker, text, tick, sequence));
                    session.EndedTick = Math.Max(session.EndedTick, tick);
                    session.TurnCount = CountDialogueTurns(session.Turns);
                    NormalizeArchiveTurns(archive);
                    CaptureRuntimeRpgState(participant, archive);
                    SaveArchiveToFile(archive);
                    archiveMutated = true;
                }

                if (archiveMutated)
                {
                    InvalidatePromptMemoryCacheLockless();
                }
            }
        }

        public void FinalizeSession(Pawn initiator, Pawn targetNpc, string sessionId, List<ChatMessageData> chatHistory)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }
            if (!TryValidatePersistenceContext(nameof(FinalizeSession)))
            {
                return;
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            int historyTurnCount = CountDialogueTurnsFromChatHistory(chatHistory);

            lock (_syncRoot)
            {
                EnsureCacheLoaded();
                bool archiveMutated = false;
                List<Pawn> participants = CollectArchiveParticipants(initiator, targetNpc);
                for (int i = 0; i < participants.Count; i++)
                {
                    Pawn participant = participants[i];
                    if (participant == null || !_archiveCache.TryGetValue(participant.thingIDNumber, out RpgNpcDialogueArchive archive))
                    {
                        continue;
                    }

                    RpgNpcDialogueSessionArchive session = FindSession(archive, sessionId);
                    if (session == null)
                    {
                        continue;
                    }

                    session.EndedTick = Math.Max(session.EndedTick, tick);
                    if (session.StartedTick <= 0)
                    {
                        session.StartedTick = tick;
                    }

                    if (historyTurnCount > 0)
                    {
                        session.TurnCount = Math.Max(session.TurnCount, historyTurnCount);
                    }
                    else
                    {
                        session.TurnCount = CountDialogueTurns(session.Turns);
                    }

                    session.IsFinalized = true;
                    NormalizeArchiveTurns(archive);
                    TryScheduleSessionCompression(archive, tick);
                    SaveArchiveToFile(archive);
                    archiveMutated = true;
                }

                if (archiveMutated)
                {
                    InvalidatePromptMemoryCacheLockless();
                }
            }
        }

        public void RecordDiplomacySummary(
            Pawn negotiator,
            Faction faction,
            List<DialogueMessageData> allMessages,
            int baselineMessageCount)
        {
            string summary = BuildDiplomacySummaryText(faction, allMessages, baselineMessageCount);
            if (string.IsNullOrWhiteSpace(summary))
            {
                return;
            }
            if (!TryValidatePersistenceContext(nameof(RecordDiplomacySummary)))
            {
                return;
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            Pawn factionLeader = ResolveFactionLeaderPawn(faction);
            var participants = new List<Pawn>(2);
            TryAddParticipant(participants, negotiator, includePlayerFaction: true);
            TryAddParticipant(participants, factionLeader, includePlayerFaction: true);

            if (participants.Count == 0)
            {
                return;
            }

            lock (_syncRoot)
            {
                EnsureCacheLoaded();
                bool archiveMutated = false;
                for (int i = 0; i < participants.Count; i++)
                {
                    Pawn participant = participants[i];
                    RpgNpcDialogueArchive archive = GetOrCreateArchive(participant, tick);
                    if (archive == null)
                    {
                        continue;
                    }

                    Pawn counterpart = ResolveCounterpartForDiplomacySummary(participant, negotiator, factionLeader);
                    string counterpartName = ResolveFallbackCounterpartName(counterpart, faction);
                    archive.LastInteractionTick = Math.Max(archive.LastInteractionTick, tick);
                    archive.PawnName = ResolvePawnName(participant);
                    archive.FactionId = BuildFactionId(participant.Faction);
                    archive.FactionName = participant.Faction?.Name ?? string.Empty;
                    archive.LastInterlocutorPawnLoadId = counterpart?.thingIDNumber ?? -1;
                    archive.LastInterlocutorName = counterpartName;
                    long sequence = AllocateTurnSequence(archive);
                    string sessionId = BuildSystemSessionId("diplomacy", participant, tick);
                    RpgNpcDialogueSessionArchive session = GetOrCreateSession(archive, sessionId, counterpart, tick);
                    session.Turns.Add(new RpgNpcDialogueTurnArchive
                    {
                        IsPlayer = false,
                        TurnSequence = sequence,
                        SpeakerPawnLoadId = participant.thingIDNumber,
                        SpeakerName = ResolvePawnName(participant),
                        InterlocutorPawnLoadId = counterpart?.thingIDNumber ?? -1,
                        InterlocutorName = counterpartName,
                        Text = DiplomacySummaryPrefix + summary,
                        GameTick = tick
                    });
                    session.EndedTick = Math.Max(session.EndedTick, tick);
                    session.TurnCount = CountDialogueTurns(session.Turns);
                    session.IsFinalized = true;
                    NormalizeArchiveTurns(archive);
                    TryScheduleSessionCompression(archive, tick);
                    SaveArchiveToFile(archive);
                    archiveMutated = true;
                }

                if (archiveMutated)
                {
                    InvalidatePromptMemoryCacheLockless();
                }
            }
        }

        private string CurrentSaveKey
        {
            get
            {
                string resolved = ResolveCurrentSaveKey();
                if (!string.Equals(_resolvedSaveKey, resolved, StringComparison.Ordinal))
                {
                    _resolvedSaveKey = resolved;
                }
                return _resolvedSaveKey;
            }
        }

        private string CurrentArchiveDirPath =>
            Path.Combine(CurrentPromptNpcRootPath, CurrentSaveKey, NpcArchiveSubDir);

        private string CurrentPromptNpcRootPath
        {
            get
            {
                try
                {
                    ModContentPack mod = LoadedModManager.GetMod<RimChatMod>()?.Content;
                    if (mod != null)
                    {
                        string path = Path.Combine(mod.RootDir, PromptFolderName, NpcPromptSubDir);
                        if (!Directory.Exists(path))
                        {
                            Directory.CreateDirectory(path);
                        }
                        return path;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimChat] Failed to create archive directory: {ex.Message}");
                }

                string fallback = Path.Combine(GenFilePaths.ConfigFolderPath, SaveRootDir, PromptFolderName, NpcPromptSubDir);
                if (!Directory.Exists(fallback))
                {
                    Directory.CreateDirectory(fallback);
                }
                return fallback;
            }
        }

        private void EnsureCacheLoaded()
        {
            string currentSaveKey;
            try
            {
                currentSaveKey = CurrentSaveKey;
            }
            catch (InvalidOperationException ex)
            {
                _archiveCache.Clear();
                _cacheLoaded = false;
                _loadedSaveKey = string.Empty;
                InvalidatePromptMemoryCacheLockless();
                Log.Error($"[RimChat] RPG NPC archive cache load blocked: {ex.Message}");
                return;
            }

            if (_cacheLoaded && string.Equals(_loadedSaveKey, currentSaveKey, StringComparison.Ordinal))
            {
                return;
            }

            _archiveCache.Clear();
            InvalidatePromptMemoryCacheLockless();
            TryMigrateLegacyArchives(currentSaveKey);
            EnsureDataDirectoryExists();
            LoadAllArchivesFromFiles();
            _loadedSaveKey = currentSaveKey;
            _cacheLoaded = true;
        }

        private void EnsureDataDirectoryExists()
        {
            if (!Directory.Exists(CurrentArchiveDirPath))
            {
                Directory.CreateDirectory(CurrentArchiveDirPath);
            }
        }

        private void LoadAllArchivesFromFiles()
        {
            string sourceDir = ResolveArchiveSourceDirectory();
            if (!Directory.Exists(sourceDir))
            {
                InvalidatePromptMemoryCacheLockless();
                return;
            }

            string[] files = Directory.GetFiles(sourceDir, "*.json");
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    string json = File.ReadAllText(files[i]);
                    RpgNpcDialogueArchive archive = RpgNpcDialogueArchiveJsonCodec.ParseJson(json);
                    if (archive != null && archive.PawnLoadId > 0 && IsArchiveOwnedByCurrentSave(archive))
                    {
                        NormalizeArchiveTurns(archive);
                        if (_archiveCache.TryGetValue(archive.PawnLoadId, out RpgNpcDialogueArchive existing))
                        {
                            MergeArchiveData(existing, archive);
                        }
                        else
                        {
                            _archiveCache[archive.PawnLoadId] = archive;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimChat] Failed to load RPG NPC archive file '{files[i]}': {ex.Message}");
                }
            }

            InvalidatePromptMemoryCacheLockless();
        }

        private string ResolveArchiveSourceDirectory()
        {
            return CurrentArchiveDirPath;
        }

        private static bool DirectoryHasJsonFiles(string dir)
        {
            return Directory.Exists(dir) && Directory.GetFiles(dir, "*.json").Length > 0;
        }

        private bool IsArchiveOwnedByCurrentSave(RpgNpcDialogueArchive archive)
        {
            if (archive == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(archive.SaveKey))
            {
                return true;
            }

            return string.Equals(archive.SaveKey, CurrentSaveKey, StringComparison.Ordinal);
        }

        private bool TryValidatePersistenceContext(string operationName)
        {
            try
            {
                _ = CurrentSaveKey;
                return true;
            }
            catch (InvalidOperationException ex)
            {
                Log.Error($"[RimChat] RPG NPC archive persistence blocked in {operationName}: {ex.Message}");
                return false;
            }
        }

        private void TryMigrateLegacyArchives(string currentSaveKey)
        {
            if (string.IsNullOrWhiteSpace(currentSaveKey))
            {
                return;
            }

            string targetDir = CurrentArchiveDirPath;
            string markerPath = Path.Combine(targetDir, $".migration_complete_{currentSaveKey}.marker");
            if (File.Exists(markerPath))
            {
                return;
            }

            Directory.CreateDirectory(targetDir);
            List<string> legacyDirs = CollectLegacyArchiveSourceDirectories(targetDir);
            if (legacyDirs.Count == 0)
            {
                return;
            }

            if (HasClaimedDefaultBucketForAnotherSave(currentSaveKey, legacyDirs))
            {
                return;
            }

            string backupRoot = Path.Combine(
                CurrentPromptNpcRootPath,
                LegacyMigrationBackupDirName,
                $"{DateTime.UtcNow:yyyyMMddHHmmss}_{currentSaveKey}");

            int migratedCount = 0;
            for (int i = 0; i < legacyDirs.Count; i++)
            {
                string sourceDir = legacyDirs[i];
                string backupDir = Path.Combine(backupRoot, $"source_{i}");
                Directory.CreateDirectory(backupDir);
                CopyJsonFiles(sourceDir, backupDir, overwrite: true);
                migratedCount += CopyJsonFiles(sourceDir, targetDir, overwrite: false);
            }

            if (migratedCount > 0)
            {
                File.WriteAllText(markerPath, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                TryClaimDefaultBucket(currentSaveKey, legacyDirs);
                Log.Message($"[RimChat] Migrated {migratedCount} legacy NPC archive file(s) to {currentSaveKey}.");
            }
        }

        private List<string> CollectLegacyArchiveSourceDirectories(string targetDir)
        {
            var dirs = new List<string>();
            string rootLevelLegacyDir = Path.Combine(CurrentPromptNpcRootPath, NpcArchiveSubDir);
            TryAddLegacySourceDir(dirs, rootLevelLegacyDir, targetDir);

            string[] saveDirs = Directory.Exists(CurrentPromptNpcRootPath)
                ? Directory.GetDirectories(CurrentPromptNpcRootPath, "Save_*")
                : Array.Empty<string>();
            for (int i = 0; i < saveDirs.Length; i++)
            {
                string dirName = Path.GetFileName(saveDirs[i]);
                if (!dirName.EndsWith($"_{DefaultSaveName}", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string legacyArchiveDir = Path.Combine(saveDirs[i], NpcArchiveSubDir);
                TryAddLegacySourceDir(dirs, legacyArchiveDir, targetDir);
            }

            return dirs;
        }

        private static void TryAddLegacySourceDir(List<string> dirs, string sourceDir, string targetDir)
        {
            if (string.IsNullOrWhiteSpace(sourceDir) || !DirectoryHasJsonFiles(sourceDir))
            {
                return;
            }

            if (string.Equals(sourceDir, targetDir, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (dirs.Any(existing => string.Equals(existing, sourceDir, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            dirs.Add(sourceDir);
        }

        private static int CopyJsonFiles(string sourceDir, string targetDir, bool overwrite)
        {
            if (!DirectoryHasJsonFiles(sourceDir))
            {
                return 0;
            }

            Directory.CreateDirectory(targetDir);
            int copied = 0;
            string[] files = Directory.GetFiles(sourceDir, "*.json");
            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);
                string targetPath = Path.Combine(targetDir, fileName);
                if (!overwrite && File.Exists(targetPath))
                {
                    continue;
                }

                File.Copy(files[i], targetPath, overwrite);
                copied++;
            }

            return copied;
        }

        private bool HasClaimedDefaultBucketForAnotherSave(string currentSaveKey, List<string> legacyDirs)
        {
            if (legacyDirs == null || legacyDirs.Count == 0 || !legacyDirs.Any(IsDefaultBucketPath))
            {
                return false;
            }

            string claimPath = Path.Combine(CurrentPromptNpcRootPath, LegacyDefaultBucketClaimMarker);
            if (!File.Exists(claimPath))
            {
                return false;
            }

            string claimedSaveKey = File.ReadAllText(claimPath).Trim();
            if (string.IsNullOrWhiteSpace(claimedSaveKey))
            {
                return false;
            }

            return !string.Equals(claimedSaveKey, currentSaveKey, StringComparison.Ordinal);
        }

        private void TryClaimDefaultBucket(string currentSaveKey, List<string> legacyDirs)
        {
            if (legacyDirs == null || legacyDirs.Count == 0 || !legacyDirs.Any(IsDefaultBucketPath))
            {
                return;
            }

            string claimPath = Path.Combine(CurrentPromptNpcRootPath, LegacyDefaultBucketClaimMarker);
            if (!File.Exists(claimPath))
            {
                File.WriteAllText(claimPath, currentSaveKey);
            }
        }

        private static bool IsDefaultBucketPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string normalized = path.Replace('\\', '/');
            return normalized.Contains("/Save_") &&
                normalized.EndsWith($"/{NpcArchiveSubDir}", StringComparison.OrdinalIgnoreCase) &&
                normalized.Contains($"_{DefaultSaveName}/");
        }

        private RpgNpcDialogueArchive GetOrCreateArchive(Pawn pawn, int tick)
        {
            int pawnId = pawn?.thingIDNumber ?? -1;
            if (pawnId <= 0)
            {
                return null;
            }

            if (_archiveCache.TryGetValue(pawnId, out RpgNpcDialogueArchive existing))
            {
                return existing;
            }

            var archive = new RpgNpcDialogueArchive
            {
                SaveKey = CurrentSaveKey,
                PawnLoadId = pawnId,
                PawnName = ResolvePawnName(pawn),
                FactionId = BuildFactionId(pawn?.Faction),
                FactionName = pawn?.Faction?.Name ?? string.Empty,
                CreatedTimestamp = DateTime.UtcNow.Ticks,
                LastInteractionTick = tick
            };
            _archiveCache[pawnId] = archive;
            return archive;
        }

        private void CaptureRuntimeRpgState(Pawn pawn, RpgNpcDialogueArchive archive)
        {
            GameComponent_RPGManager rpgManager = GameComponent_RPGManager.Instance;
            if (rpgManager == null || pawn == null || archive == null)
            {
                return;
            }

            archive.PersonaPrompt = rpgManager.GetPawnPersonaPrompt(pawn) ?? string.Empty;
            archive.CooldownUntilTick = rpgManager.GetDialogueCooldownUntilTick(pawn);
        }

        private static long AllocateTurnSequence(RpgNpcDialogueArchive archive)
        {
            if (archive == null)
            {
                return 0L;
            }

            long next = archive.NextTurnSequence > 0 ? archive.NextTurnSequence : 1L;
            archive.NextTurnSequence = next + 1L;
            return next;
        }

        private static RpgNpcDialogueTurnArchive BuildTurnArchive(
            Pawn initiator,
            Pawn targetNpc,
            bool isPlayerSpeaker,
            string text,
            int tick,
            long turnSequence)
        {
            Pawn speaker = ResolveDialogueSpeakerPawn(initiator, targetNpc, isPlayerSpeaker);
            Pawn interlocutor = ResolveCounterpartPawn(speaker, initiator, targetNpc);
            return new RpgNpcDialogueTurnArchive
            {
                IsPlayer = isPlayerSpeaker,
                TurnSequence = turnSequence,
                SpeakerPawnLoadId = speaker?.thingIDNumber ?? -1,
                SpeakerName = ResolvePawnName(speaker),
                InterlocutorPawnLoadId = interlocutor?.thingIDNumber ?? -1,
                InterlocutorName = ResolvePawnName(interlocutor),
                Text = text.Trim(),
                GameTick = tick
            };
        }

        private static Pawn ResolveDialogueSpeakerPawn(Pawn initiator, Pawn targetNpc, bool isPlayerSpeaker)
        {
            if (isPlayerSpeaker)
            {
                return initiator ?? targetNpc;
            }

            return targetNpc ?? initiator;
        }

        private static Pawn ResolveCounterpartPawn(Pawn self, Pawn initiator, Pawn targetNpc)
        {
            if (self != null && initiator != null && self.thingIDNumber == initiator.thingIDNumber)
            {
                return targetNpc;
            }

            if (self != null && targetNpc != null && self.thingIDNumber == targetNpc.thingIDNumber)
            {
                return initiator;
            }

            Pawn playerPawn = GetPlayerPawn(initiator) ?? GetPlayerPawn(targetNpc);
            if (playerPawn != null && (self == null || playerPawn.thingIDNumber != self.thingIDNumber))
            {
                return playerPawn;
            }

            if (initiator != null && (self == null || initiator.thingIDNumber != self.thingIDNumber))
            {
                return initiator;
            }

            if (targetNpc != null && (self == null || targetNpc.thingIDNumber != self.thingIDNumber))
            {
                return targetNpc;
            }

            return null;
        }

        private static Pawn GetPlayerPawn(Pawn pawn)
        {
            return pawn != null && pawn.Faction != null && pawn.Faction.IsPlayer ? pawn : null;
        }

        private static RpgNpcDialogueSessionArchive GetOrCreateSession(
            RpgNpcDialogueArchive archive,
            string sessionId,
            Pawn counterpart,
            int tick)
        {
            if (archive == null)
            {
                return null;
            }

            string normalizedSessionId = string.IsNullOrWhiteSpace(sessionId)
                ? $"session_{tick}_{Guid.NewGuid():N}"
                : sessionId.Trim();

            RpgNpcDialogueSessionArchive existing = FindSession(archive, normalizedSessionId);
            if (existing != null)
            {
                if (existing.StartedTick <= 0)
                {
                    existing.StartedTick = tick;
                }

                if (counterpart != null)
                {
                    existing.InterlocutorPawnLoadId = counterpart.thingIDNumber;
                    existing.InterlocutorName = ResolvePawnName(counterpart);
                }

                if (string.IsNullOrWhiteSpace(existing.SummaryState))
                {
                    existing.SummaryState = RpgNpcDialogueSessionSummaryState.Pending;
                }

                existing.IsFinalized = false;
                return existing;
            }

            var session = new RpgNpcDialogueSessionArchive
            {
                SessionId = normalizedSessionId,
                StartedTick = tick,
                EndedTick = tick,
                TurnCount = 0,
                IsFinalized = false,
                InterlocutorPawnLoadId = counterpart?.thingIDNumber ?? -1,
                InterlocutorName = ResolvePawnName(counterpart),
                SummaryText = string.Empty,
                SummaryState = RpgNpcDialogueSessionSummaryState.Pending,
                LastSummaryAttemptTick = 0,
                Turns = new List<RpgNpcDialogueTurnArchive>()
            };

            if (archive.Sessions == null)
            {
                archive.Sessions = new List<RpgNpcDialogueSessionArchive>();
            }

            archive.Sessions.Add(session);
            return session;
        }

        private static RpgNpcDialogueSessionArchive FindSession(RpgNpcDialogueArchive archive, string sessionId)
        {
            if (archive?.Sessions == null || string.IsNullOrWhiteSpace(sessionId))
            {
                return null;
            }

            return archive.Sessions.FirstOrDefault(session =>
                session != null &&
                string.Equals(session.SessionId, sessionId, StringComparison.Ordinal));
        }

        private static string BuildSystemSessionId(string source, Pawn participant, int tick)
        {
            int participantId = participant?.thingIDNumber ?? -1;
            return $"sys_{source}_{participantId}_{tick}_{Guid.NewGuid():N}";
        }

        private static int CountDialogueTurns(List<RpgNpcDialogueTurnArchive> turns)
        {
            return turns?
                .Count(turn => turn != null && !string.IsNullOrWhiteSpace(turn.Text))
                ?? 0;
        }

        private static void PrepareSessionForTurnAppend(RpgNpcDialogueSessionArchive session)
        {
            if (session == null)
            {
                return;
            }

            session.IsFinalized = false;

            bool hadTerminalSummaryState =
                string.Equals(session.SummaryState, RpgNpcDialogueSessionSummaryState.Compressed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(session.SummaryState, RpgNpcDialogueSessionSummaryState.SummaryFailed, StringComparison.OrdinalIgnoreCase);
            if (hadTerminalSummaryState)
            {
                session.SummaryState = RpgNpcDialogueSessionSummaryState.Pending;
                session.LastSummaryAttemptTick = 0;
            }

            if (!string.IsNullOrWhiteSpace(session.SummaryText) &&
                !string.Equals(session.SummaryState, RpgNpcDialogueSessionSummaryState.Compressed, StringComparison.OrdinalIgnoreCase))
            {
                session.SummaryText = string.Empty;
            }
        }

        private static int CountDialogueTurnsFromChatHistory(List<ChatMessageData> chatHistory)
        {
            if (chatHistory == null || chatHistory.Count == 0)
            {
                return 0;
            }

            return chatHistory.Count(message =>
                message != null &&
                !string.IsNullOrWhiteSpace(message.content) &&
                !string.Equals(message.role, "system", StringComparison.OrdinalIgnoreCase));
        }

        private void SaveArchiveToFile(RpgNpcDialogueArchive archive)
        {
            if (archive == null || archive.PawnLoadId <= 0)
            {
                return;
            }

            try
            {
                EnsureDataDirectoryExists();
                archive.SaveKey = CurrentSaveKey;
                archive.LastSavedTimestamp = DateTime.UtcNow.Ticks;
                string fileName = BuildArchiveFileName(archive);
                string filePath = Path.Combine(CurrentArchiveDirPath, fileName);
                CleanupLegacyArchiveFiles(archive.PawnLoadId, fileName);
                string json = RpgNpcDialogueArchiveJsonCodec.ConvertToJson(archive);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to save RPG NPC archive {archive.PawnLoadId}: {ex.Message}");
            }
        }

        public bool HasPromptMemory(Pawn targetNpc, Pawn currentInterlocutor = null, bool allowCacheLoad = true)
        {
            if (targetNpc == null || targetNpc.Destroyed || targetNpc.Dead)
            {
                return false;
            }

            lock (_syncRoot)
            {
                if (allowCacheLoad)
                {
                    EnsureCacheLoaded();
                }
                else if (!_cacheLoaded)
                {
                    return false;
                }

                FlushPendingWarmupCompressionLockless(Find.TickManager?.TicksGame ?? 0);
                if (!_archiveCache.TryGetValue(targetNpc.thingIDNumber, out RpgNpcDialogueArchive archive) ||
                    archive == null)
                {
                    return false;
                }

                NormalizeArchiveTurns(archive);
                List<RpgNpcDialogueTurnArchive> retainedTurns = GetSessionTurns(SelectLatestRetainedFullSession(archive));
                if (retainedTurns.Count > 0)
                {
                    return true;
                }

                List<RpgNpcDialogueSessionArchive> compressedSessions = GetCompressedSessionsForInjection(archive);
                return compressedSessions.Count > 0;
            }
        }

        public string BuildPromptMemoryBlock(
            Pawn targetNpc,
            Pawn currentInterlocutor = null,
            int summaryTurnLimit = 8,
            int summaryCharBudget = 1200,
            bool allowCompressionScheduling = true,
            bool allowCacheLoad = true)
        {
            if (targetNpc == null || targetNpc.Destroyed || targetNpc.Dead)
            {
                return string.Empty;
            }

            lock (_syncRoot)
            {
                if (allowCacheLoad)
                {
                    EnsureCacheLoaded();
                }
                else if (!_cacheLoaded)
                {
                    return string.Empty;
                }

                int clampedSummaryTurnLimit = Math.Max(3, Math.Min(16, summaryTurnLimit));
                int clampedSummaryBudget = Math.Max(500, Math.Min(4000, summaryCharBudget));
                int tick = Find.TickManager?.TicksGame ?? 0;
                FlushPendingWarmupCompressionLockless(tick);
                int dayStamp = ResolveAbsoluteDayStamp(tick, targetNpc);
                int interlocutorId = currentInterlocutor?.thingIDNumber ?? -1;
                string cacheKey = BuildPromptMemoryCacheKey(
                    targetNpc.thingIDNumber,
                    interlocutorId,
                    clampedSummaryTurnLimit,
                    clampedSummaryBudget,
                    dayStamp);
                if (TryGetPromptMemoryCacheLockless(cacheKey, out string cachedMemoryBlock))
                {
                    return cachedMemoryBlock;
                }

                if (!_archiveCache.TryGetValue(targetNpc.thingIDNumber, out RpgNpcDialogueArchive archive) ||
                    archive == null)
                {
                    LogDebugMissingArchive(targetNpc, currentInterlocutor);
                    SetPromptMemoryCacheLockless(cacheKey, string.Empty);
                    return string.Empty;
                }

                NormalizeArchiveTurns(archive);
                if (allowCompressionScheduling)
                {
                    TryScheduleSessionCompression(archive, tick);
                }

                RpgNpcDialogueSessionArchive retainedSession = SelectLatestRetainedFullSession(archive);
                List<RpgNpcDialogueTurnArchive> retainedTurns = GetSessionTurns(retainedSession);
                List<RpgNpcDialogueSessionArchive> compressedSessions = GetCompressedSessionsForInjection(archive);
                if ((retainedTurns == null || retainedTurns.Count == 0) &&
                    (compressedSessions == null || compressedSessions.Count == 0))
                {
                    LogDebugMissingArchive(targetNpc, currentInterlocutor);
                    SetPromptMemoryCacheLockless(cacheKey, string.Empty);
                    return string.Empty;
                }

                string npcName = ResolvePawnName(targetNpc);
                string interlocutorName = ResolveInterlocutorName(archive, currentInterlocutor, retainedTurns);
                var sb = new StringBuilder();
                sb.AppendLine("=== NPC PERSONAL MEMORY (RPG DIALOGUE) ===");
                sb.AppendLine($"You are {npcName}. Keep continuity with your own previous conversations.");
                sb.AppendLine($"Current interlocutor in this scene: {interlocutorName}.");
                sb.AppendLine("Continuity rules:");
                sb.AppendLine("- Resolve latest unresolved player intent first.");
                sb.AppendLine("- Keep relationship tone continuous; do not reset to neutral.");
                sb.AppendLine("- Never reuse previous wording verbatim; paraphrase.");

                AppendCompressedSessionSummaries(
                    sb,
                    compressedSessions,
                    MaxInjectedCompressedSessionSummaries,
                    MaxInjectedCompressedSessionSummaryChars);

                if (retainedTurns != null && retainedTurns.Count > 0)
                {
                    List<RpgNpcDialogueTurnArchive> summaryTurns = BuildRelevantSummaryTurns(retainedTurns, currentInterlocutor, interlocutorName);
                    AppendDiplomacySummaryMemoryLines(sb, summaryTurns);

                    List<RpgNpcDialogueTurnArchive> interlocutorTurns = BuildRelevantInterlocutorTurns(
                        retainedTurns,
                        archive,
                        currentInterlocutor,
                        interlocutorName);
                    List<RpgNpcDialogueTurnArchive> selfTurns = BuildRelevantSelfTurns(
                        retainedTurns,
                        archive,
                        targetNpc,
                        currentInterlocutor,
                        interlocutorName);
                    List<RpgNpcDialogueTurnArchive> timelineTurns = BuildChronologicalDialogueTurns(selfTurns, interlocutorTurns);
                    bool shouldInjectUnresolvedIntent = !ShouldForgetLatestUnresolvedIntent(archive, targetNpc, tick);
                    if (shouldInjectUnresolvedIntent)
                    {
                        string unresolvedIntent = ExtractLatestUnresolvedIntent(interlocutorTurns, timelineTurns);
                        bool hostileIntent = IsHostileIntent(unresolvedIntent);
                        if (!string.IsNullOrWhiteSpace(unresolvedIntent))
                        {
                            sb.AppendLine($"Latest unresolved player intent: {TrimForPrompt(unresolvedIntent, 150)}");
                            sb.AppendLine($"Latest intent tone (hostile={hostileIntent.ToString().ToLowerInvariant()}).");
                        }
                    }

                    string recentSummary = BuildRecentDialogueSummaryText(
                        timelineTurns,
                        targetNpc,
                        currentInterlocutor,
                        npcName,
                        interlocutorName,
                        clampedSummaryTurnLimit,
                        clampedSummaryBudget);
                    if (!string.IsNullOrWhiteSpace(recentSummary))
                    {
                        sb.AppendLine("Recent dialogue summary (summary-first):");
                        sb.AppendLine(recentSummary);
                    }

                    AppendRecentRawQuotes(sb, timelineTurns, targetNpc, currentInterlocutor, npcName, interlocutorName);
                }

                string memoryBlock = sb.ToString().Trim();
                SetPromptMemoryCacheLockless(cacheKey, memoryBlock);
                return memoryBlock;
            }
        }

        public string BuildUnresolvedIntentSummary(Pawn targetNpc, Pawn currentInterlocutor = null)
        {
            if (targetNpc == null || targetNpc.Destroyed || targetNpc.Dead)
            {
                return string.Empty;
            }

            lock (_syncRoot)
            {
                EnsureCacheLoaded();
                FlushPendingWarmupCompressionLockless(Find.TickManager?.TicksGame ?? 0);
                if (!_archiveCache.TryGetValue(targetNpc.thingIDNumber, out RpgNpcDialogueArchive archive) ||
                    archive == null)
                {
                    return string.Empty;
                }

                NormalizeArchiveTurns(archive);
                int tick = Find.TickManager?.TicksGame ?? 0;
                TryScheduleSessionCompression(archive, tick);
                if (ShouldForgetLatestUnresolvedIntent(archive, targetNpc, tick))
                {
                    return string.Empty;
                }

                RpgNpcDialogueSessionArchive retainedSession = SelectLatestRetainedFullSession(archive);
                List<RpgNpcDialogueTurnArchive> retainedTurns = GetSessionTurns(retainedSession);
                if (retainedTurns == null || retainedTurns.Count == 0)
                {
                    return string.Empty;
                }

                string interlocutorName = ResolveInterlocutorName(archive, currentInterlocutor, retainedTurns);
                List<RpgNpcDialogueTurnArchive> interlocutorTurns = BuildRelevantInterlocutorTurns(
                    retainedTurns,
                    archive,
                    currentInterlocutor,
                    interlocutorName);
                List<RpgNpcDialogueTurnArchive> selfTurns = BuildRelevantSelfTurns(
                    retainedTurns,
                    archive,
                    targetNpc,
                    currentInterlocutor,
                    interlocutorName);
                List<RpgNpcDialogueTurnArchive> timelineTurns = BuildChronologicalDialogueTurns(selfTurns, interlocutorTurns);
                return TrimForPrompt(ExtractLatestUnresolvedIntent(interlocutorTurns, timelineTurns), 160);
            }
        }

        private static bool ShouldForgetLatestUnresolvedIntent(
            RpgNpcDialogueArchive archive,
            Pawn targetNpc,
            int currentTick)
        {
            if (archive == null || archive.LastInteractionTick <= 0 || currentTick <= archive.LastInteractionTick)
            {
                return false;
            }

            int previousDayStamp = ResolveAbsoluteDayStamp(archive.LastInteractionTick, targetNpc);
            int currentDayStamp = ResolveAbsoluteDayStamp(currentTick, targetNpc);
            return currentDayStamp > previousDayStamp;
        }

        private static int ResolveAbsoluteDayStamp(int tick, Pawn targetNpc)
        {
            float longitude = ResolveLongitude(targetNpc);
            int year = GenDate.Year(tick, longitude);
            int dayOfYear = GenDate.DayOfYear(tick, longitude);
            return checked((year * 60) + dayOfYear);
        }

        private static float ResolveLongitude(Pawn pawn)
        {
            Map map = pawn?.MapHeld ?? Find.CurrentMap;
            if (map != null && map.Tile >= 0 && Find.WorldGrid != null)
            {
                return Find.WorldGrid.LongLatOf(map.Tile).x;
            }

            return 0f;
        }

        private static string ExtractLatestUnresolvedIntent(
            List<RpgNpcDialogueTurnArchive> interlocutorTurns,
            List<RpgNpcDialogueTurnArchive> timelineTurns)
        {
            RpgNpcDialogueTurnArchive lastInterlocutorTurn = interlocutorTurns?
                .OrderByDescending(turn => turn.GameTick)
                .ThenByDescending(turn => turn.TurnSequence)
                .FirstOrDefault();
            if (lastInterlocutorTurn == null || string.IsNullOrWhiteSpace(lastInterlocutorTurn.Text))
            {
                return string.Empty;
            }

            if (timelineTurns == null || timelineTurns.Count == 0)
            {
                return lastInterlocutorTurn.Text.Trim();
            }

            RpgNpcDialogueTurnArchive lastTimeline = timelineTurns[timelineTurns.Count - 1];
            bool interlocutorIsLatest =
                lastTimeline != null &&
                (lastTimeline.IsPlayer ||
                 lastTimeline.SpeakerPawnLoadId == lastInterlocutorTurn.SpeakerPawnLoadId ||
                 string.Equals(lastTimeline.SpeakerName, lastInterlocutorTurn.SpeakerName, StringComparison.OrdinalIgnoreCase));
            if (interlocutorIsLatest)
            {
                return lastInterlocutorTurn.Text.Trim();
            }

            return lastInterlocutorTurn.Text.Trim();
        }

        private static string BuildRecentDialogueSummaryText(
            List<RpgNpcDialogueTurnArchive> timelineTurns,
            Pawn targetNpc,
            Pawn currentInterlocutor,
            string npcName,
            string interlocutorName,
            int turnLimit,
            int charBudget)
        {
            if (timelineTurns == null || timelineTurns.Count == 0)
            {
                return string.Empty;
            }

            int start = Math.Max(0, timelineTurns.Count - turnLimit);
            var summaryLines = new List<string>();
            int usedChars = 0;
            for (int i = start; i < timelineTurns.Count; i++)
            {
                RpgNpcDialogueTurnArchive turn = timelineTurns[i];
                string speaker = ResolvePromptSpeakerName(turn, targetNpc, npcName, currentInterlocutor, interlocutorName);
                string gist = TrimForPrompt(turn?.Text, 90);
                if (string.IsNullOrWhiteSpace(gist))
                {
                    continue;
                }

                string line = $"- {speaker}: {gist}";
                if (usedChars + line.Length > charBudget)
                {
                    break;
                }

                summaryLines.Add(line);
                usedChars += line.Length;
            }

            return string.Join("\n", summaryLines);
        }

        private static void AppendRecentRawQuotes(
            StringBuilder sb,
            List<RpgNpcDialogueTurnArchive> timelineTurns,
            Pawn targetNpc,
            Pawn currentInterlocutor,
            string npcName,
            string interlocutorName)
        {
            if (sb == null || timelineTurns == null || timelineTurns.Count == 0)
            {
                return;
            }

            int keep = Math.Min(3, timelineTurns.Count);
            int start = timelineTurns.Count - keep;
            sb.AppendLine("Recent raw snippets (limited):");
            for (int i = start; i < timelineTurns.Count; i++)
            {
                RpgNpcDialogueTurnArchive turn = timelineTurns[i];
                string speaker = ResolvePromptSpeakerName(turn, targetNpc, npcName, currentInterlocutor, interlocutorName);
                sb.AppendLine($"- {speaker}: {TrimForPrompt(turn?.Text, 80)}");
            }
        }

        private void ApplyArchivesToRuntime()
        {
            GameComponent_RPGManager rpgManager = GameComponent_RPGManager.Instance;
            if (rpgManager == null || _archiveCache.Count == 0)
            {
                return;
            }

            foreach (RpgNpcDialogueArchive archive in _archiveCache.Values)
            {
                if (archive == null || archive.PawnLoadId <= 0)
                {
                    continue;
                }

                Pawn pawn = FindPawnByLoadId(archive.PawnLoadId);
                if (pawn == null || pawn.Destroyed || pawn.Dead)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(archive.PersonaPrompt))
                {
                    rpgManager.SetPawnPersonaPrompt(pawn, archive.PersonaPrompt);
                }

                rpgManager.SetDialogueCooldownUntilTick(pawn, archive.CooldownUntilTick);
            }
        }

        private static Pawn FindPawnByLoadId(int pawnLoadId)
        {
            if (pawnLoadId <= 0)
            {
                return null;
            }

            IEnumerable<Pawn> worldPawns = Find.WorldPawns?.AllPawnsAliveOrDead;
            if (worldPawns != null)
            {
                Pawn found = worldPawns.FirstOrDefault(pawn => pawn != null && pawn.thingIDNumber == pawnLoadId);
                if (found != null)
                {
                    return found;
                }
            }

            if (Find.Maps == null)
            {
                return null;
            }

            foreach (Map map in Find.Maps)
            {
                Pawn found = map?.mapPawns?.AllPawnsSpawned?.FirstOrDefault(pawn => pawn != null && pawn.thingIDNumber == pawnLoadId);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static string ResolvePawnName(Pawn pawn)
        {
            if (pawn == null)
            {
                return "UnknownPawn";
            }

            return pawn.LabelShort ?? pawn.Name?.ToStringShort ?? pawn.Name?.ToStringFull ?? "UnknownPawn";
        }

        private static string BuildFactionId(Faction faction)
        {
            if (faction == null)
            {
                return string.Empty;
            }

            if (faction.def != null && !string.IsNullOrEmpty(faction.def.defName))
            {
                return $"{faction.def.defName}_{faction.loadID}";
            }

            return $"custom_{faction.loadID}";
        }

        private static List<Pawn> CollectArchiveParticipants(Pawn initiator, Pawn targetNpc)
        {
            var participants = new List<Pawn>(2);
            TryAddParticipant(participants, targetNpc, includePlayerFaction: true);

            bool includeInitiator =
                initiator != null &&
                targetNpc != null &&
                initiator.thingIDNumber != targetNpc.thingIDNumber;
            if (includeInitiator)
            {
                TryAddParticipant(participants, initiator, includePlayerFaction: true);
            }
            return participants;
        }

        private static void TryAddParticipant(List<Pawn> participants, Pawn pawn, bool includePlayerFaction)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead)
            {
                return;
            }

            if (!includePlayerFaction && pawn.Faction != null && pawn.Faction.IsPlayer)
            {
                return;
            }

            if (participants.Any(existing => existing != null && existing.thingIDNumber == pawn.thingIDNumber))
            {
                return;
            }

            participants.Add(pawn);
        }

        private string ResolveCurrentSaveKey()
        {
            if (Current.Game == null)
            {
                throw new InvalidOperationException("Current.Game is null. Persistence requires a loaded save.");
            }

            string saveName = GetCurrentSaveName();
            if (!string.Equals(saveName, DefaultSaveName, StringComparison.OrdinalIgnoreCase))
            {
                _lastResolvedSaveName = saveName;
                string hashKey = GetHashSaveKey(saveName);
                return $"{hashKey}_{saveName}".SanitizeFileName();
            }

            string persistentSlotId = ResolvePersistentRpgSaveSlotId();
            if (!string.IsNullOrWhiteSpace(persistentSlotId))
            {
                return $"Save_{persistentSlotId}".SanitizeFileName();
            }

            throw new InvalidOperationException(
                $"Failed to resolve active save identifier; refusing to write into shared Default bucket. Diagnostic={BuildSaveNameResolutionDiagnostic()}");
        }

        private string GetCurrentSaveName()
        {
            string trackedSaveName = SaveContextTracker.GetCurrentSaveName();
            if (!string.IsNullOrWhiteSpace(trackedSaveName))
            {
                _lastResolvedSaveName = trackedSaveName;
                return trackedSaveName;
            }

            object gameInfo = Current.Game?.Info;
            if (gameInfo == null)
            {
                string loadedGameName = TryResolveLoadedGameNameFromMetaHeader();
                return string.IsNullOrWhiteSpace(loadedGameName)
                    ? DefaultSaveName
                    : loadedGameName.SanitizeFileName();
            }

            string name = ReadStringMember(gameInfo, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                name = ReadStringMember(gameInfo, "Name");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = ReadStringMember(gameInfo, "fileName");
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                name = ReadStringMember(gameInfo, "FileName");
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                name = TryResolveNameFromAnyStringMember(gameInfo);
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                name = TryResolveLoadedGameNameFromMetaHeader();
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                name = TryResolveLoadedGameNameFromKnownVerseStatics();
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                name = _lastResolvedSaveName;
            }

            return string.IsNullOrWhiteSpace(name) ? DefaultSaveName : name.SanitizeFileName();
        }

        private static string ReadStringMember(object target, string memberName)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName))
            {
                return string.Empty;
            }

            try
            {
                PropertyInfo prop = target.GetType().GetProperty(memberName, InstanceStringMemberBinding);
                if (prop?.PropertyType == typeof(string))
                {
                    string value = prop.GetValue(target) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }

                FieldInfo field = target.GetType().GetField(memberName, InstanceStringMemberBinding);
                if (field?.FieldType == typeof(string))
                {
                    string value = field.GetValue(target) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static string TryResolveNameFromAnyStringMember(object target)
        {
            if (target == null)
            {
                return string.Empty;
            }

            try
            {
                Type type = target.GetType();
                foreach (PropertyInfo prop in type.GetProperties(InstanceStringMemberBinding))
                {
                    if (prop.PropertyType != typeof(string) || prop.GetIndexParameters().Length > 0)
                    {
                        continue;
                    }

                    string value = prop.GetValue(target) as string;
                    if (!string.IsNullOrWhiteSpace(value) && IsLikelySaveNameMember(prop.Name))
                    {
                        return value;
                    }
                }

                foreach (FieldInfo field in type.GetFields(InstanceStringMemberBinding))
                {
                    if (field.FieldType != typeof(string))
                    {
                        continue;
                    }

                    string value = field.GetValue(target) as string;
                    if (!string.IsNullOrWhiteSpace(value) && IsLikelySaveNameMember(field.Name))
                    {
                        return value;
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static bool IsLikelySaveNameMember(string memberName)
        {
            if (string.IsNullOrWhiteSpace(memberName))
            {
                return false;
            }

            string lower = memberName.ToLowerInvariant();
            return lower.Contains("name") || lower.Contains("file");
        }

        private static string TryResolveLoadedGameNameFromMetaHeader()
        {
            try
            {
                Type headerType = FindTypeInLoadedAssemblies("Verse.ScribeMetaHeaderUtility");
                if (headerType == null)
                {
                    return string.Empty;
                }

                PropertyInfo prop = headerType.GetProperty("loadedGameName", StaticStringMemberBinding);
                if (prop != null)
                {
                    string value = prop.GetValue(null, null) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }

                FieldInfo field = headerType.GetField("loadedGameName", StaticStringMemberBinding);
                if (field != null)
                {
                    string value = field.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static string TryResolveLoadedGameNameFromKnownVerseStatics()
        {
            string[] typeNames =
            {
                "Verse.SavedGameLoaderNow",
                "Verse.GameDataSaveLoader",
                "Verse.ScribeMetaHeaderUtility"
            };

            string[] memberNames =
            {
                "loadedGameName",
                "loadingFromSaveFileName",
                "loadingSaveFileName",
                "currentSaveFileName",
                "curSaveFileName",
                "curFileName",
                "saveFileName",
                "fileName",
                "lastLoadedFileName",
                "lastSaveName"
            };

            for (int i = 0; i < typeNames.Length; i++)
            {
                Type type = FindTypeInLoadedAssemblies(typeNames[i]);
                if (type == null)
                {
                    continue;
                }

                for (int j = 0; j < memberNames.Length; j++)
                {
                    string value = ReadStaticStringMember(type, memberNames[j]);
                    if (!string.IsNullOrWhiteSpace(value) &&
                        !string.Equals(value, DefaultSaveName, StringComparison.OrdinalIgnoreCase))
                    {
                        return value;
                    }
                }
            }

            return string.Empty;
        }

        private static string ReadStaticStringMember(Type targetType, string memberName)
        {
            if (targetType == null || string.IsNullOrWhiteSpace(memberName))
            {
                return string.Empty;
            }

            try
            {
                PropertyInfo prop = targetType.GetProperty(memberName, StaticStringMemberBinding);
                if (prop?.PropertyType == typeof(string))
                {
                    string value = prop.GetValue(null, null) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }

                FieldInfo field = targetType.GetField(memberName, StaticStringMemberBinding);
                if (field?.FieldType == typeof(string))
                {
                    string value = field.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private string BuildSaveNameResolutionDiagnostic()
        {
            object gameInfo = Current.Game?.Info;
            string[] instanceMembers = { "name", "Name", "fileName", "FileName", "permadeathModeUniqueName" };
            string[] staticMembers = { "loadedGameName", "loadingFromSaveFileName", "curFileName", "saveFileName" };

            string gameInfoType = gameInfo?.GetType().FullName ?? "null";
            string gameInfoValues = string.Join(", ",
                instanceMembers.Select(member => $"{member}='{ReadStringMember(gameInfo, member)}'"));

            string scribeValue = TryResolveLoadedGameNameFromMetaHeader();
            string trackedSaveName = SaveContextTracker.GetCurrentSaveName();
            string persistentSlotId = ResolvePersistentRpgSaveSlotId();
            string staticValues = string.Join(", ", staticMembers.Select(member =>
            {
                string savedGameLoaderNow = ReadStaticStringMember(FindTypeInLoadedAssemblies("Verse.SavedGameLoaderNow"), member);
                string gameDataSaveLoader = ReadStaticStringMember(FindTypeInLoadedAssemblies("Verse.GameDataSaveLoader"), member);
                return $"{member}:[SavedGameLoaderNow='{savedGameLoaderNow}',GameDataSaveLoader='{gameDataSaveLoader}']";
            }));

            return $"gameInfoType={gameInfoType}; gameInfo={gameInfoValues}; tracked='{trackedSaveName}'; slot='{persistentSlotId}'; metaHeader='{scribeValue}'; static={staticValues}";
        }

        private static Type FindTypeInLoadedAssemblies(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return null;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type found = assembly.GetType(fullName, false, true);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private string GetHashSaveKey(string saveName)
        {
            return $"Save_{ComputeStableHash(saveName).ToString(CultureInfo.InvariantCulture)}".SanitizeFileName();
        }

        private static string ResolvePersistentRpgSaveSlotId()
        {
            try
            {
                string slotId = GameComponent_RPGManager.Instance?.GetPersistentRpgSaveSlotId();
                return string.IsNullOrWhiteSpace(slotId) ? string.Empty : slotId.SanitizeFileName();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string BuildArchiveFileName(RpgNpcDialogueArchive archive)
        {
            string safeName = (archive?.PawnName ?? "UnknownPawn").SanitizeFileName();
            return $"npc_{archive.PawnLoadId}_{safeName}.json";
        }

        private void CleanupLegacyArchiveFiles(int pawnLoadId, string keepFileName)
        {
            if (!Directory.Exists(CurrentArchiveDirPath))
            {
                return;
            }

            string keepPath = Path.Combine(CurrentArchiveDirPath, keepFileName);
            IEnumerable<string> candidates = Directory.GetFiles(CurrentArchiveDirPath, $"npc_{pawnLoadId}.json")
                .Concat(Directory.GetFiles(CurrentArchiveDirPath, $"npc_{pawnLoadId}_*.json"))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (string path in candidates)
            {
                if (string.Equals(path, keepPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    File.Delete(path);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimChat] Failed to create archive directory: {ex.Message}");
                }
            }
        }

        private static void NormalizeArchiveTurns(RpgNpcDialogueArchive archive)
        {
            if (archive == null)
            {
                return;
            }

            if (archive.Sessions == null)
            {
                archive.Sessions = new List<RpgNpcDialogueSessionArchive>();
            }

            archive.Sessions = archive.Sessions
                .Where(session => session != null)
                .OrderBy(session => session.StartedTick > 0 ? session.StartedTick : int.MaxValue)
                .ThenBy(session => session.EndedTick > 0 ? session.EndedTick : int.MaxValue)
                .ThenBy(session => session.SessionId ?? string.Empty, StringComparer.Ordinal)
                .ToList();

            EnsureTurnSequenceState(archive);
            TrimArchiveSessions(archive);
        }

        private static void EnsureTurnSequenceState(RpgNpcDialogueArchive archive)
        {
            if (archive?.Sessions == null)
            {
                return;
            }

            long next = archive.NextTurnSequence > 0 ? archive.NextTurnSequence : 1L;
            for (int i = 0; i < archive.Sessions.Count; i++)
            {
                RpgNpcDialogueSessionArchive session = archive.Sessions[i];
                if (session == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(session.SessionId))
                {
                    session.SessionId = Guid.NewGuid().ToString("N");
                }

                if (string.IsNullOrWhiteSpace(session.SummaryState))
                {
                    session.SummaryState = RpgNpcDialogueSessionSummaryState.Pending;
                }

                List<RpgNpcDialogueTurnArchive> turns = session.Turns ?? new List<RpgNpcDialogueTurnArchive>();
                turns = turns
                    .Where(turn => turn != null && !string.IsNullOrWhiteSpace(turn.Text))
                    .GroupBy(turn =>
                        $"{turn.GameTick}|{turn.IsPlayer}|{turn.SpeakerPawnLoadId}|{turn.InterlocutorPawnLoadId}|{turn.Text.Trim()}")
                    .Select(group => group.OrderBy(turn => turn.TurnSequence).First())
                    .OrderBy(turn => turn.GameTick)
                    .ThenBy(turn => turn.TurnSequence)
                    .ToList();

                if (string.Equals(session.SummaryState, RpgNpcDialogueSessionSummaryState.Compressed, StringComparison.OrdinalIgnoreCase))
                {
                    session.IsFinalized = true;
                }

                if (string.Equals(session.SummaryState, RpgNpcDialogueSessionSummaryState.Compressed, StringComparison.OrdinalIgnoreCase) &&
                    turns.Count > 0)
                {
                    // Heal invalid mixed state generated by early compression of active sessions.
                    session.SummaryState = RpgNpcDialogueSessionSummaryState.Pending;
                    session.SummaryText = string.Empty;
                    session.LastSummaryAttemptTick = 0;
                }

                for (int turnIndex = 0; turnIndex < turns.Count; turnIndex++)
                {
                    RpgNpcDialogueTurnArchive turn = turns[turnIndex];
                    if (turn.TurnSequence <= 0)
                    {
                        turn.TurnSequence = next;
                        next++;
                        continue;
                    }

                    if (turn.TurnSequence >= next)
                    {
                        next = turn.TurnSequence + 1L;
                    }
                }

                session.Turns = turns;
                session.TurnCount = Math.Max(session.TurnCount, CountDialogueTurns(session.Turns));
                if (session.StartedTick <= 0 && session.Turns.Count > 0)
                {
                    session.StartedTick = session.Turns.Min(turn => turn.GameTick);
                }

                if (session.EndedTick <= 0 && session.Turns.Count > 0)
                {
                    session.EndedTick = session.Turns.Max(turn => turn.GameTick);
                }
            }

            archive.NextTurnSequence = Math.Max(archive.NextTurnSequence, next);
        }

        private static void TrimArchiveSessions(RpgNpcDialogueArchive archive)
        {
            if (archive?.Sessions == null || archive.Sessions.Count == 0)
            {
                return;
            }

            string retainedId = SelectLatestRetainedFullSession(archive)?.SessionId ?? string.Empty;

            while (archive.Sessions.Count > MaxSessionsPerNpc)
            {
                RpgNpcDialogueSessionArchive removable = archive.Sessions
                    .Where(session =>
                        session != null &&
                        !string.Equals(session.SessionId, retainedId, StringComparison.Ordinal))
                    .OrderBy(session =>
                        string.Equals(session.SummaryState, RpgNpcDialogueSessionSummaryState.Compressed, StringComparison.OrdinalIgnoreCase)
                            ? 0
                            : 1)
                    .ThenBy(session => session.EndedTick > 0 ? session.EndedTick : int.MaxValue)
                    .ThenBy(session => session.StartedTick > 0 ? session.StartedTick : int.MaxValue)
                    .FirstOrDefault();

                if (removable == null)
                {
                    break;
                }

                archive.Sessions.Remove(removable);
            }

            int totalTurns = archive.Sessions.Sum(session => session?.Turns?.Count ?? 0);
            if (totalTurns <= MaxTurnsPerNpc)
            {
                return;
            }

            while (totalTurns > MaxTurnsPerNpc)
            {
                RpgNpcDialogueSessionArchive target = archive.Sessions
                    .Where(session =>
                        session != null &&
                        session.Turns != null &&
                        session.Turns.Count > 0 &&
                        !string.Equals(session.SessionId, retainedId, StringComparison.Ordinal))
                    .OrderBy(session => session.EndedTick > 0 ? session.EndedTick : int.MaxValue)
                    .ThenBy(session => session.StartedTick > 0 ? session.StartedTick : int.MaxValue)
                    .FirstOrDefault();

                if (target == null)
                {
                    break;
                }

                int trimCount = Math.Min(totalTurns - MaxTurnsPerNpc, target.Turns.Count);
                if (trimCount <= 0)
                {
                    break;
                }

                target.Turns.RemoveRange(0, trimCount);
                target.TurnCount = Math.Max(target.TurnCount, CountDialogueTurns(target.Turns));
                totalTurns = archive.Sessions.Sum(session => session?.Turns?.Count ?? 0);
            }
        }

        private static void MergeArchiveData(RpgNpcDialogueArchive existing, RpgNpcDialogueArchive incoming)
        {
            if (existing == null || incoming == null)
            {
                return;
            }

            EnsureTurnSequenceState(existing);
            EnsureTurnSequenceState(incoming);
            if (string.IsNullOrWhiteSpace(existing.SaveKey) && !string.IsNullOrWhiteSpace(incoming.SaveKey))
            {
                existing.SaveKey = incoming.SaveKey;
            }

            if (incoming.LastInteractionTick > existing.LastInteractionTick)
            {
                existing.LastInteractionTick = incoming.LastInteractionTick;
                existing.PawnName = incoming.PawnName;
                existing.FactionId = incoming.FactionId;
                existing.FactionName = incoming.FactionName;
                existing.LastInterlocutorPawnLoadId = incoming.LastInterlocutorPawnLoadId;
                existing.LastInterlocutorName = incoming.LastInterlocutorName;
                existing.PersonaPrompt = incoming.PersonaPrompt;
                existing.CooldownUntilTick = incoming.CooldownUntilTick;
                existing.CreatedTimestamp = Math.Min(existing.CreatedTimestamp, incoming.CreatedTimestamp);
                existing.LastSavedTimestamp = Math.Max(existing.LastSavedTimestamp, incoming.LastSavedTimestamp);
                existing.NextTurnSequence = Math.Max(existing.NextTurnSequence, incoming.NextTurnSequence);
            }

            if (incoming.Sessions != null && incoming.Sessions.Count > 0)
            {
                if (existing.Sessions == null)
                {
                    existing.Sessions = new List<RpgNpcDialogueSessionArchive>();
                }

                for (int i = 0; i < incoming.Sessions.Count; i++)
                {
                    RpgNpcDialogueSessionArchive incomingSession = incoming.Sessions[i];
                    if (incomingSession == null)
                    {
                        continue;
                    }

                    string sessionId = string.IsNullOrWhiteSpace(incomingSession.SessionId)
                        ? Guid.NewGuid().ToString("N")
                        : incomingSession.SessionId;
                    RpgNpcDialogueSessionArchive existingSession = existing.Sessions.FirstOrDefault(session =>
                        session != null &&
                        string.Equals(session.SessionId, sessionId, StringComparison.Ordinal));

                    if (existingSession == null)
                    {
                        existingSession = CloneSession(incomingSession);
                        existingSession.SessionId = sessionId;
                        existing.Sessions.Add(existingSession);
                        continue;
                    }

                    existingSession.StartedTick = existingSession.StartedTick > 0
                        ? Math.Min(existingSession.StartedTick, incomingSession.StartedTick > 0 ? incomingSession.StartedTick : existingSession.StartedTick)
                        : incomingSession.StartedTick;
                    existingSession.EndedTick = Math.Max(existingSession.EndedTick, incomingSession.EndedTick);
                    existingSession.TurnCount = Math.Max(existingSession.TurnCount, incomingSession.TurnCount);
                    if (incomingSession.InterlocutorPawnLoadId > 0)
                    {
                        existingSession.InterlocutorPawnLoadId = incomingSession.InterlocutorPawnLoadId;
                    }
                    if (!string.IsNullOrWhiteSpace(incomingSession.InterlocutorName))
                    {
                        existingSession.InterlocutorName = incomingSession.InterlocutorName;
                    }
                    if (!string.IsNullOrWhiteSpace(incomingSession.SummaryText))
                    {
                        existingSession.SummaryText = incomingSession.SummaryText;
                    }
                    if (!string.IsNullOrWhiteSpace(incomingSession.SummaryState))
                    {
                        existingSession.SummaryState = incomingSession.SummaryState;
                    }
                    existingSession.IsFinalized = existingSession.IsFinalized && incomingSession.IsFinalized;
                    existingSession.LastSummaryAttemptTick = Math.Max(existingSession.LastSummaryAttemptTick, incomingSession.LastSummaryAttemptTick);
                    if (incomingSession.Turns != null && incomingSession.Turns.Count > 0)
                    {
                        if (existingSession.Turns == null)
                        {
                            existingSession.Turns = new List<RpgNpcDialogueTurnArchive>();
                        }

                        existingSession.Turns.AddRange(incomingSession.Turns.Where(turn => turn != null).Select(CloneTurn));
                    }
                }

                NormalizeArchiveTurns(existing);
            }
            else
            {
                existing.NextTurnSequence = Math.Max(existing.NextTurnSequence, incoming.NextTurnSequence);
            }
        }

        private static RpgNpcDialogueSessionArchive CloneSession(RpgNpcDialogueSessionArchive session)
        {
            if (session == null)
            {
                return null;
            }

            return new RpgNpcDialogueSessionArchive
            {
                SessionId = session.SessionId ?? string.Empty,
                StartedTick = session.StartedTick,
                EndedTick = session.EndedTick,
                TurnCount = session.TurnCount,
                IsFinalized = session.IsFinalized,
                InterlocutorPawnLoadId = session.InterlocutorPawnLoadId,
                InterlocutorName = session.InterlocutorName ?? string.Empty,
                SummaryText = session.SummaryText ?? string.Empty,
                SummaryState = session.SummaryState ?? RpgNpcDialogueSessionSummaryState.Pending,
                LastSummaryAttemptTick = session.LastSummaryAttemptTick,
                Turns = session.Turns?.Where(turn => turn != null).Select(CloneTurn).ToList() ?? new List<RpgNpcDialogueTurnArchive>()
            };
        }

        private static RpgNpcDialogueTurnArchive CloneTurn(RpgNpcDialogueTurnArchive turn)
        {
            if (turn == null)
            {
                return null;
            }

            return new RpgNpcDialogueTurnArchive
            {
                IsPlayer = turn.IsPlayer,
                TurnSequence = turn.TurnSequence,
                SpeakerPawnLoadId = turn.SpeakerPawnLoadId,
                SpeakerName = turn.SpeakerName ?? string.Empty,
                InterlocutorPawnLoadId = turn.InterlocutorPawnLoadId,
                InterlocutorName = turn.InterlocutorName ?? string.Empty,
                Text = turn.Text ?? string.Empty,
                GameTick = turn.GameTick
            };
        }

        private static uint ComputeStableHash(string text)
        {
            string input = string.IsNullOrWhiteSpace(text) ? "Default" : text;
            uint hash = 2166136261;
            for (int i = 0; i < input.Length; i++)
            {
                hash ^= input[i];
                hash *= 16777619;
            }
            return hash;
        }

        private static List<RpgNpcDialogueTurnArchive> BuildRelevantSummaryTurns(
            List<RpgNpcDialogueTurnArchive> sourceTurns,
            Pawn currentInterlocutor,
            string interlocutorName)
        {
            var summaryTurns = sourceTurns?
                .Where(turn => turn != null && IsDiplomacySummaryTurn(turn.Text))
                .OrderByDescending(turn => turn.GameTick)
                .ThenByDescending(turn => turn.TurnSequence)
                .ToList() ?? new List<RpgNpcDialogueTurnArchive>();

            if (summaryTurns.Count == 0)
            {
                return summaryTurns;
            }

            int interlocutorId = currentInterlocutor?.thingIDNumber ?? -1;
            if (interlocutorId > 0)
            {
                List<RpgNpcDialogueTurnArchive> byId = summaryTurns
                    .Where(turn => turn.InterlocutorPawnLoadId == interlocutorId || turn.SpeakerPawnLoadId == interlocutorId)
                    .ToList();
                if (byId.Count > 0)
                {
                    return byId;
                }
            }

            if (!IsPlaceholderInterlocutorName(interlocutorName))
            {
                List<RpgNpcDialogueTurnArchive> byName = summaryTurns
                    .Where(turn =>
                        string.Equals(turn.InterlocutorName, interlocutorName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(turn.SpeakerName, interlocutorName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (byName.Count > 0)
                {
                    return byName;
                }
            }

            return summaryTurns;
        }

        private static void AppendDiplomacySummaryMemoryLines(StringBuilder sb, List<RpgNpcDialogueTurnArchive> summaryTurns)
        {
            if (sb == null || summaryTurns == null || summaryTurns.Count == 0)
            {
                return;
            }

            List<RpgNpcDialogueTurnArchive> picked = summaryTurns
                .Take(3)
                .OrderBy(turn => turn.GameTick)
                .ThenBy(turn => turn.TurnSequence)
                .ToList();
            if (picked.Count == 0)
            {
                return;
            }

            sb.AppendLine("Recent diplomacy summary memories:");
            for (int i = 0; i < picked.Count; i++)
            {
                string text = StripDiplomacySummaryPrefix(picked[i].Text);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                sb.AppendLine($"- {TrimForPrompt(text, 180)}");
            }
        }

        private static bool IsDiplomacySummaryTurn(string text)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                text.StartsWith(DiplomacySummaryPrefix, StringComparison.Ordinal);
        }

        private static string StripDiplomacySummaryPrefix(string text)
        {
            if (!IsDiplomacySummaryTurn(text))
            {
                return text?.Trim() ?? string.Empty;
            }

            return text.Substring(DiplomacySummaryPrefix.Length).Trim();
        }

        private static Pawn ResolveFactionLeaderPawn(Faction faction)
        {
            Pawn leader = faction?.leader;
            if (leader == null || leader.Dead || leader.Destroyed)
            {
                return null;
            }

            return leader;
        }

        private static Pawn ResolveCounterpartForDiplomacySummary(Pawn participant, Pawn negotiator, Pawn factionLeader)
        {
            if (participant != null && negotiator != null && participant.thingIDNumber == negotiator.thingIDNumber)
            {
                return factionLeader;
            }

            if (participant != null && factionLeader != null && participant.thingIDNumber == factionLeader.thingIDNumber)
            {
                return negotiator;
            }

            return negotiator ?? factionLeader;
        }

        private static string ResolveFallbackCounterpartName(Pawn counterpart, Faction faction)
        {
            if (counterpart != null)
            {
                return ResolvePawnName(counterpart);
            }

            if (!string.IsNullOrWhiteSpace(faction?.Name))
            {
                return faction.Name;
            }

            return "FactionCounterpart";
        }

        private static string BuildDiplomacySummaryText(
            Faction faction,
            List<DialogueMessageData> allMessages,
            int baselineMessageCount)
        {
            if (allMessages == null || allMessages.Count <= baselineMessageCount)
            {
                return string.Empty;
            }

            int start = Math.Max(0, Math.Min(baselineMessageCount, allMessages.Count));
            List<DialogueMessageData> delta = allMessages
                .Skip(start)
                .Where(m => m != null && !m.IsSystemMessage() && !string.IsNullOrWhiteSpace(m.message))
                .ToList();
            if (delta.Count == 0)
            {
                return string.Empty;
            }

            string playerLast = delta.LastOrDefault(m => m.isPlayer)?.message ?? string.Empty;
            string factionLast = delta.LastOrDefault(m => !m.isPlayer)?.message ?? string.Empty;
            string topic = DetectDiplomacyTopic(delta.Select(m => m.message));
            string factionName = !string.IsNullOrWhiteSpace(faction?.Name) ? faction.Name : "the faction";
            return
                $"Diplomacy session with {factionName} on topic '{topic}'. " +
                $"Player intent: {TrimForPrompt(playerLast, 70)}. " +
                $"Faction stance: {TrimForPrompt(factionLast, 70)}.";
        }

        private static string DetectDiplomacyTopic(IEnumerable<string> lines)
        {
            if (lines == null)
            {
                return "general";
            }

            string joined = string.Join(" ", lines.Where(l => !string.IsNullOrWhiteSpace(l))).ToLowerInvariant();
            if (joined.Contains("trade") || joined.Contains("交易") || joined.Contains("商队")) return "trade";
            if (joined.Contains("peace") || joined.Contains("war") || joined.Contains("和平") || joined.Contains("宣战")) return "war-peace";
            if (joined.Contains("aid") || joined.Contains("help") || joined.Contains("援助") || joined.Contains("支援")) return "aid";
            if (joined.Contains("gift") || joined.Contains("礼物")) return "gift";
            return "general";
        }

        private static List<RpgNpcDialogueTurnArchive> BuildRelevantSelfTurns(
            List<RpgNpcDialogueTurnArchive> sourceTurns,
            RpgNpcDialogueArchive archive,
            Pawn targetNpc,
            Pawn currentInterlocutor,
            string interlocutorName)
        {
            var allTurns = sourceTurns?
                .Where(turn =>
                    turn != null &&
                    !string.IsNullOrWhiteSpace(turn.Text) &&
                    !IsDiplomacySummaryTurn(turn.Text))
                .OrderBy(turn => turn.GameTick)
                .ThenBy(turn => turn.TurnSequence)
                .ToList() ?? new List<RpgNpcDialogueTurnArchive>();
            if (allTurns.Count == 0)
            {
                return allTurns;
            }

            int selfId = targetNpc?.thingIDNumber ?? archive?.PawnLoadId ?? -1;
            int interlocutorId = currentInterlocutor?.thingIDNumber ?? -1;
            if (selfId > 0)
            {
                IEnumerable<RpgNpcDialogueTurnArchive> selfById = allTurns
                    .Where(turn => turn.SpeakerPawnLoadId == selfId)
                    .ToList();

                if (interlocutorId > 0)
                {
                    List<RpgNpcDialogueTurnArchive> pairById = selfById
                        .Where(turn => turn.InterlocutorPawnLoadId == interlocutorId)
                        .ToList();
                    if (pairById.Count > 0)
                    {
                        return pairById;
                    }
                }

                List<RpgNpcDialogueTurnArchive> byId = selfById.ToList();
                if (byId.Count > 0)
                {
                    return byId;
                }
            }

            string selfName = targetNpc?.LabelShort ?? archive?.PawnName ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(selfName))
            {
                IEnumerable<RpgNpcDialogueTurnArchive> selfByName = allTurns
                    .Where(turn => string.Equals(turn.SpeakerName, selfName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (!IsPlaceholderInterlocutorName(interlocutorName))
                {
                    List<RpgNpcDialogueTurnArchive> pairByName = selfByName
                        .Where(turn => string.Equals(turn.InterlocutorName, interlocutorName, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (pairByName.Count > 0)
                    {
                        return pairByName;
                    }
                }

                List<RpgNpcDialogueTurnArchive> byName = selfByName.ToList();
                if (byName.Count > 0)
                {
                    return byName;
                }
            }

            return allTurns.Where(turn => !turn.IsPlayer).ToList();
        }

        private static List<RpgNpcDialogueTurnArchive> BuildChronologicalDialogueTurns(
            List<RpgNpcDialogueTurnArchive> selfTurns,
            List<RpgNpcDialogueTurnArchive> interlocutorTurns)
        {
            IEnumerable<RpgNpcDialogueTurnArchive> merged = (selfTurns ?? new List<RpgNpcDialogueTurnArchive>())
                .Concat(interlocutorTurns ?? new List<RpgNpcDialogueTurnArchive>());

            return merged
                .Where(turn => turn != null && !string.IsNullOrWhiteSpace(turn.Text))
                .GroupBy(turn =>
                    $"{turn.GameTick}|{turn.TurnSequence}|{turn.SpeakerPawnLoadId}|{turn.InterlocutorPawnLoadId}|{turn.Text.Trim()}")
                .Select(group => group.First())
                .OrderBy(turn => turn.GameTick)
                .ThenBy(turn => turn.TurnSequence)
                .ToList();
        }

        private static List<RpgNpcDialogueTurnArchive> BuildRelevantInterlocutorTurns(
            List<RpgNpcDialogueTurnArchive> sourceTurns,
            RpgNpcDialogueArchive archive,
            Pawn currentInterlocutor,
            string interlocutorName)
        {
            var allTurns = sourceTurns?
                .Where(turn =>
                    turn != null &&
                    !string.IsNullOrWhiteSpace(turn.Text) &&
                    !IsDiplomacySummaryTurn(turn.Text))
                .OrderBy(turn => turn.GameTick)
                .ThenBy(turn => turn.TurnSequence)
                .ToList() ?? new List<RpgNpcDialogueTurnArchive>();

            if (allTurns.Count == 0)
            {
                return allTurns;
            }

            int interlocutorId = currentInterlocutor?.thingIDNumber ?? -1;
            if (interlocutorId > 0)
            {
                List<RpgNpcDialogueTurnArchive> strictById = allTurns
                    .Where(turn => turn.SpeakerPawnLoadId == interlocutorId)
                    .ToList();
                if (strictById.Count > 0)
                {
                    return strictById;
                }
            }

            if (!IsPlaceholderInterlocutorName(interlocutorName))
            {
                List<RpgNpcDialogueTurnArchive> byName = allTurns
                    .Where(turn => string.Equals(turn.SpeakerName, interlocutorName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (byName.Count > 0)
                {
                    return byName;
                }
            }

            List<RpgNpcDialogueTurnArchive> playerTurns = allTurns.Where(turn => turn.IsPlayer).ToList();
            if (playerTurns.Count > 0)
            {
                return playerTurns;
            }

            return allTurns.Where(turn => IsInterlocutorTurnFallback(turn, archive)).ToList();
        }

        private static string ResolvePromptSpeakerName(
            RpgNpcDialogueTurnArchive turn,
            Pawn selfPawn,
            string selfName,
            Pawn currentInterlocutor,
            string interlocutorName)
        {
            if (turn == null)
            {
                return "UnknownSpeaker";
            }

            int selfId = selfPawn?.thingIDNumber ?? -1;
            if (selfId > 0 && turn.SpeakerPawnLoadId == selfId)
            {
                return string.IsNullOrWhiteSpace(selfName) ? "You" : selfName;
            }

            int interlocutorId = currentInterlocutor?.thingIDNumber ?? -1;
            if (interlocutorId > 0 && turn.SpeakerPawnLoadId == interlocutorId)
            {
                return IsPlaceholderInterlocutorName(interlocutorName) ? "Interlocutor" : interlocutorName;
            }

            return ResolveTurnSpeakerName(turn, interlocutorName);
        }

        private static bool IsInterlocutorTurnFallback(RpgNpcDialogueTurnArchive turn, RpgNpcDialogueArchive archive)
        {
            if (turn == null || string.IsNullOrWhiteSpace(turn.Text))
            {
                return false;
            }

            if (turn.IsPlayer)
            {
                return true;
            }

            if (archive == null)
            {
                return false;
            }

            if (archive.LastInterlocutorPawnLoadId > 0 && turn.SpeakerPawnLoadId > 0)
            {
                return archive.LastInterlocutorPawnLoadId == turn.SpeakerPawnLoadId;
            }

            return !string.IsNullOrWhiteSpace(archive.LastInterlocutorName) &&
                string.Equals(archive.LastInterlocutorName, turn.SpeakerName, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveInterlocutorName(
            RpgNpcDialogueArchive archive,
            Pawn currentInterlocutor,
            List<RpgNpcDialogueTurnArchive> sourceTurns)
        {
            string currentName = ResolveOptionalPawnName(currentInterlocutor);
            if (!string.IsNullOrWhiteSpace(currentName))
            {
                return currentName;
            }

            if (!IsPlaceholderInterlocutorName(archive?.LastInterlocutorName))
            {
                return archive.LastInterlocutorName;
            }

            RpgNpcDialogueTurnArchive lastTurn = sourceTurns?
                .Where(turn => turn != null && !IsPlaceholderInterlocutorName(turn.SpeakerName))
                .OrderByDescending(turn => turn.GameTick)
                .ThenByDescending(turn => turn.TurnSequence)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(lastTurn?.SpeakerName))
            {
                return lastTurn.SpeakerName;
            }

            return "CurrentInterlocutor";
        }

        private static string ResolveTurnSpeakerName(RpgNpcDialogueTurnArchive turn, string fallbackName)
        {
            if (!string.IsNullOrWhiteSpace(turn?.SpeakerName) &&
                !IsPlaceholderInterlocutorName(turn.SpeakerName))
            {
                return turn.SpeakerName;
            }

            return string.IsNullOrWhiteSpace(fallbackName) ? "CurrentInterlocutor" : fallbackName;
        }

        private static string ResolveOptionalPawnName(Pawn pawn)
        {
            if (pawn == null)
            {
                return string.Empty;
            }

            return pawn.LabelShort ?? pawn.Name?.ToStringShort ?? pawn.Name?.ToStringFull ?? string.Empty;
        }

        private static bool IsPlaceholderInterlocutorName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ||
                string.Equals(value, "Interlocutor", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "CurrentInterlocutor", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "UnknownPawn", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHostileIntent(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string lower = text.ToLowerInvariant();
            string[] keywords =
            {
                "kill", "murder", "attack", "hurt", "destroy", "threat", "hate",
                "杀", "死", "干掉", "攻击", "伤害", "威胁", "仇恨"
            };

            for (int i = 0; i < keywords.Length; i++)
            {
                if (lower.Contains(keywords[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static string TrimForPrompt(string text, int maxLen)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string value = text.Trim();
            if (value.Length <= maxLen)
            {
                return value;
            }

            if (maxLen <= 3)
            {
                return value.Substring(0, maxLen);
            }

            return value.Substring(0, maxLen - 3) + "...";
        }

        private void LogDebugMissingArchive(Pawn targetNpc, Pawn currentInterlocutor)
        {
            if (RimChatMod.Settings?.EnableDebugLogging != true)
            {
                return;
            }

            int targetId = targetNpc?.thingIDNumber ?? -1;
            int interlocutorId = currentInterlocutor?.thingIDNumber ?? -1;
            string targetName = ResolvePawnName(targetNpc);
            string interlocutorName = ResolveOptionalPawnName(currentInterlocutor);
            bool hasSaveContext = TryResolveArchiveDebugContext(out string saveKey, out string archiveDir);
            string contextSuffix = hasSaveContext
                ? $"saveKey={saveKey}, dir={archiveDir}"
                : "saveKey=<unresolved>, dir=<unresolved>";
            Log.Message(
                $"[RimChat] RPG memory skipped: no archive sessions for target={targetName}({targetId}), " +
                $"interlocutor={interlocutorName}({interlocutorId}), {contextSuffix}");
        }

        private bool TryResolveArchiveDebugContext(out string saveKey, out string archiveDir)
        {
            saveKey = string.Empty;
            archiveDir = string.Empty;
            try
            {
                saveKey = CurrentSaveKey;
                archiveDir = CurrentArchiveDirPath;
                return true;
            }
            catch (InvalidOperationException ex)
            {
                Log.Warning($"[RimChat] RPG memory debug context unresolved: {ex.Message}");
                return false;
            }
        }
    }
}
