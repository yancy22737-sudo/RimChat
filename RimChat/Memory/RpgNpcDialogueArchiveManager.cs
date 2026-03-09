using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
        private readonly object _syncRoot = new object();
        private bool _cacheLoaded;
        private string _loadedSaveKey = string.Empty;
        private string _resolvedSaveKey = string.Empty;

        public void OnNewGame()
        {
            lock (_syncRoot)
            {
                _archiveCache.Clear();
                _compressionInFlight.Clear();
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
            lock (_syncRoot)
            {
                EnsureCacheLoaded();
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

            lock (_syncRoot)
            {
                EnsureCacheLoaded();
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
                }
            }
        }

        public void FinalizeSession(Pawn initiator, Pawn targetNpc, string sessionId, List<ChatMessageData> chatHistory)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            int historyTurnCount = CountDialogueTurnsFromChatHistory(chatHistory);

            lock (_syncRoot)
            {
                EnsureCacheLoaded();
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
                }
            }
        }

        private string CurrentSaveKey
        {
            get
            {
                if (ShouldRefreshResolvedSaveKey())
                {
                    _resolvedSaveKey = ResolveCurrentSaveKey();
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
                catch
                {
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
            string currentSaveKey = CurrentSaveKey;
            if (_cacheLoaded && string.Equals(_loadedSaveKey, currentSaveKey, StringComparison.Ordinal))
            {
                return;
            }

            _archiveCache.Clear();
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
                return;
            }

            string[] files = Directory.GetFiles(sourceDir, "*.json");
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    string json = File.ReadAllText(files[i]);
                    RpgNpcDialogueArchive archive = RpgNpcDialogueArchiveJsonCodec.ParseJson(json);
                    if (archive != null && archive.PawnLoadId > 0)
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
        }

        private string ResolveArchiveSourceDirectory()
        {
            if (DirectoryHasJsonFiles(CurrentArchiveDirPath))
            {
                return CurrentArchiveDirPath;
            }

            foreach (string legacyPromptDir in GetLegacyPromptArchiveDirectories())
            {
                if (!DirectoryHasJsonFiles(legacyPromptDir))
                {
                    continue;
                }

                MigrateArchiveFiles(legacyPromptDir, CurrentArchiveDirPath, "prompt save key");
                return CurrentArchiveDirPath;
            }

            foreach (string legacySaveDataDir in GetLegacySaveDataArchiveDirectories())
            {
                if (!DirectoryHasJsonFiles(legacySaveDataDir))
                {
                    continue;
                }

                MigrateArchiveFiles(legacySaveDataDir, CurrentArchiveDirPath, "legacy save_data");
                return CurrentArchiveDirPath;
            }

            return CurrentArchiveDirPath;
        }

        private static bool DirectoryHasJsonFiles(string dir)
        {
            return Directory.Exists(dir) && Directory.GetFiles(dir, "*.json").Length > 0;
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
                ? $"legacy_{tick}_{Guid.NewGuid():N}"
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
                IsLegacyImported = false,
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

        public string BuildPromptMemoryBlock(
            Pawn targetNpc,
            Pawn currentInterlocutor = null,
            int summaryTurnLimit = 8,
            int summaryCharBudget = 1200)
        {
            if (targetNpc == null || targetNpc.Destroyed || targetNpc.Dead)
            {
                return string.Empty;
            }

            lock (_syncRoot)
            {
                EnsureCacheLoaded();
                if (!_archiveCache.TryGetValue(targetNpc.thingIDNumber, out RpgNpcDialogueArchive archive) ||
                    archive == null)
                {
                    LogDebugMissingArchive(targetNpc, currentInterlocutor);
                    return string.Empty;
                }

                NormalizeArchiveTurns(archive);
                int tick = Find.TickManager?.TicksGame ?? 0;
                TryScheduleSessionCompression(archive, tick);

                RpgNpcDialogueSessionArchive retainedSession = SelectLatestRetainedFullSession(archive);
                List<RpgNpcDialogueTurnArchive> retainedTurns = GetSessionTurns(retainedSession);
                List<RpgNpcDialogueSessionArchive> compressedSessions = GetCompressedSessionsForInjection(archive);
                if ((retainedTurns == null || retainedTurns.Count == 0) &&
                    (compressedSessions == null || compressedSessions.Count == 0))
                {
                    LogDebugMissingArchive(targetNpc, currentInterlocutor);
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
                    string unresolvedIntent = ExtractLatestUnresolvedIntent(interlocutorTurns, timelineTurns);
                    bool hostileIntent = IsHostileIntent(unresolvedIntent);
                    if (!string.IsNullOrWhiteSpace(unresolvedIntent))
                    {
                        sb.AppendLine($"Latest unresolved player intent: {TrimForPrompt(unresolvedIntent, 150)}");
                        sb.AppendLine($"Latest intent tone (hostile={hostileIntent.ToString().ToLowerInvariant()}).");
                    }

                    int clampedSummaryTurnLimit = Math.Max(3, Math.Min(16, summaryTurnLimit));
                    int clampedSummaryBudget = Math.Max(500, Math.Min(4000, summaryCharBudget));
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

                return sb.ToString().Trim();
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
                if (!_archiveCache.TryGetValue(targetNpc.thingIDNumber, out RpgNpcDialogueArchive archive) ||
                    archive == null)
                {
                    return string.Empty;
                }

                NormalizeArchiveTurns(archive);
                TryScheduleSessionCompression(archive, Find.TickManager?.TicksGame ?? 0);

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

        private bool ShouldRefreshResolvedSaveKey()
        {
            if (string.IsNullOrWhiteSpace(_resolvedSaveKey))
            {
                return true;
            }

            string saveName = GetCurrentSaveName();
            if (string.Equals(saveName, "Default", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string expected = $"{GetHashSaveKey()}_{saveName}".SanitizeFileName();
            return !string.Equals(_resolvedSaveKey, expected, StringComparison.Ordinal);
        }

        private string ResolveCurrentSaveKey()
        {
            if (Current.Game == null)
            {
                return "Default";
            }

            string saveName = GetCurrentSaveName();
            string hashKey = GetHashSaveKey();
            return $"{hashKey}_{saveName}".SanitizeFileName();
        }

        private string GetCurrentSaveName()
        {
            object gameInfo = Current.Game?.Info;
            if (gameInfo == null)
            {
                return "Default";
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

            return string.IsNullOrWhiteSpace(name) ? "Default" : name.SanitizeFileName();
        }

        private static string ReadStringMember(object target, string memberName)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName))
            {
                return string.Empty;
            }

            try
            {
                var prop = target.GetType().GetProperty(memberName);
                if (prop != null)
                {
                    string value = prop.GetValue(target) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }

                var field = target.GetType().GetField(memberName);
                if (field != null)
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

        private string GetHashSaveKey()
        {
            string saveName = GetCurrentSaveName();
            return $"Save_{ComputeStableHash(saveName).ToString(CultureInfo.InvariantCulture)}".SanitizeFileName();
        }

        private IEnumerable<string> GetLegacyPromptArchiveDirectories()
        {
            string saveNameOnly = GetCurrentSaveName();
            string hashOnly = GetHashSaveKey();
            string saveNamePath = Path.Combine(CurrentPromptNpcRootPath, saveNameOnly, NpcArchiveSubDir);
            string hashPath = Path.Combine(CurrentPromptNpcRootPath, hashOnly, NpcArchiveSubDir);
            var candidates = new List<string> { saveNamePath, hashPath };

            try
            {
                if (Directory.Exists(CurrentPromptNpcRootPath))
                {
                    foreach (string dir in Directory.GetDirectories(CurrentPromptNpcRootPath, $"Save_*_{saveNameOnly}"))
                    {
                        candidates.Add(Path.Combine(dir, NpcArchiveSubDir));
                    }
                }
            }
            catch
            {
            }

            foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!string.Equals(candidate, CurrentArchiveDirPath, StringComparison.OrdinalIgnoreCase))
                {
                    yield return candidate;
                }
            }
        }

        private IEnumerable<string> GetLegacySaveDataArchiveDirectories()
        {
            string saveDataRoot = Path.Combine(GenFilePaths.SaveDataFolderPath, SaveRootDir, SaveSubDir);
            string saveNameOnly = GetCurrentSaveName();
            string hashOnly = GetHashSaveKey();
            var keys = new List<string> { CurrentSaveKey, saveNameOnly, hashOnly };
            try
            {
                if (Directory.Exists(saveDataRoot))
                {
                    keys.AddRange(Directory.GetDirectories(saveDataRoot, $"Save_*_{saveNameOnly}")
                        .Select(Path.GetFileName)
                        .Where(name => !string.IsNullOrWhiteSpace(name)));
                }
            }
            catch
            {
            }

            foreach (string key in keys.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                yield return Path.Combine(saveDataRoot, key, NpcArchiveSubDir);
            }
        }

        private static void MigrateArchiveFiles(string sourceDir, string targetDir, string reason)
        {
            try
            {
                Directory.CreateDirectory(targetDir);
                foreach (string file in Directory.GetFiles(sourceDir, "*.json"))
                {
                    string target = Path.Combine(targetDir, Path.GetFileName(file));
                    if (!File.Exists(target))
                    {
                        File.Copy(file, target, true);
                    }
                }
                Log.Message($"[RimChat] Migrated RPG NPC archives from {reason} to: {targetDir}");
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to migrate RPG NPC archives from {reason}: {ex.Message}");
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
                catch
                {
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
                    existingSession.IsLegacyImported = existingSession.IsLegacyImported || incomingSession.IsLegacyImported;
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
                IsLegacyImported = session.IsLegacyImported,
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

            return allTurns.Where(turn => IsInterlocutorTurnLegacy(turn, archive)).ToList();
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

        private static bool IsInterlocutorTurnLegacy(RpgNpcDialogueTurnArchive turn, RpgNpcDialogueArchive archive)
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
            Log.Message(
                $"[RimChat] RPG memory skipped: no archive sessions for target={targetName}({targetId}), " +
                $"interlocutor={interlocutorName}({interlocutorId}), saveKey={CurrentSaveKey}, dir={CurrentArchiveDirPath}");
        }
    }
}
