using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimChat.AI;
using RimChat.Config;
using RimChat.Persistence;
using Verse;

namespace RimChat.Memory
{
    /// <summary>/// Dependencies: AIChatServiceAsync, RpgNpcDialogueArchive session model.
 /// Responsibility: orchestrate session-level compression and summary-first memory selection.
 ///</summary>
    public sealed partial class RpgNpcDialogueArchiveManager
    {
        private void TryScheduleSessionCompression(RpgNpcDialogueArchive archive, int triggerTick)
        {
            if (archive?.Sessions == null || archive.Sessions.Count == 0)
            {
                return;
            }

            if (!AIChatServiceAsync.Instance.IsConfigured())
            {
                return;
            }

            string retainedSessionId = SelectLatestRetainedFullSession(archive)?.SessionId ?? string.Empty;
            List<RpgNpcDialogueSessionArchive> candidates = archive.Sessions
                .Where(session => ShouldScheduleCompressionForSession(session, retainedSessionId, archive.PawnLoadId, triggerTick))
                .OrderByDescending(session => session.EndedTick)
                .ThenByDescending(session => session.StartedTick)
                .Take(MaxCompressionRequestsPerPass)
                .ToList();

            for (int i = 0; i < candidates.Count; i++)
            {
                RequestSessionCompression(archive, candidates[i], triggerTick);
            }
        }

        private bool ShouldScheduleCompressionForSession(
            RpgNpcDialogueSessionArchive session,
            string retainedSessionId,
            int pawnLoadId,
            int triggerTick)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.SessionId))
            {
                return false;
            }

            if (string.Equals(session.SessionId, retainedSessionId, StringComparison.Ordinal))
            {
                return false;
            }

            if (!session.IsFinalized)
            {
                return false;
            }

            if (session.Turns == null || session.Turns.Count == 0 || CountDialogueTurns(session.Turns) <= 0)
            {
                return false;
            }

            if (string.Equals(session.SummaryState, RpgNpcDialogueSessionSummaryState.Compressed, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string compressionKey = BuildCompressionKey(pawnLoadId, session.SessionId);
            if (_compressionInFlight.Contains(compressionKey))
            {
                return false;
            }

            bool failedRecently =
                string.Equals(session.SummaryState, RpgNpcDialogueSessionSummaryState.SummaryFailed, StringComparison.OrdinalIgnoreCase) &&
                session.LastSummaryAttemptTick > 0 &&
                triggerTick - session.LastSummaryAttemptTick < CompressionRetryCooldownTicks;
            return !failedRecently;
        }

        private void RequestSessionCompression(RpgNpcDialogueArchive archive, RpgNpcDialogueSessionArchive session, int triggerTick)
        {
            if (archive == null || session == null || string.IsNullOrWhiteSpace(session.SessionId))
            {
                return;
            }

            string compressionKey = BuildCompressionKey(archive.PawnLoadId, session.SessionId);
            if (!_compressionInFlight.Add(compressionKey))
            {
                return;
            }

            session.LastSummaryAttemptTick = triggerTick;
            List<ChatMessageData> request = BuildSessionSummaryRequestMessages(archive, session);
            if (request == null || request.Count == 0)
            {
                _compressionInFlight.Remove(compressionKey);
                return;
            }

            AIChatServiceAsync.Instance.SendChatRequestAsync(
                request,
                onSuccess: response =>
                {
                    lock (_syncRoot)
                    {
                        _compressionInFlight.Remove(compressionKey);
                        if (!_archiveCache.TryGetValue(archive.PawnLoadId, out RpgNpcDialogueArchive currentArchive) ||
                            currentArchive == null)
                        {
                            return;
                        }

                        RpgNpcDialogueSessionArchive currentSession = FindSession(currentArchive, session.SessionId);
                        if (currentSession == null)
                        {
                            return;
                        }

                        if (!currentSession.IsFinalized)
                        {
                            return;
                        }

                        string retainedSessionId = SelectLatestRetainedFullSession(currentArchive)?.SessionId ?? string.Empty;
                        if (string.Equals(currentSession.SessionId, retainedSessionId, StringComparison.Ordinal))
                        {
                            return;
                        }

                        string summary = NormalizeToSingleSentenceSummary(response);
                        if (string.IsNullOrWhiteSpace(summary))
                        {
                            MarkSummaryCompressionFailed(currentArchive, currentSession);
                            return;
                        }

                        currentSession.SummaryText = summary;
                        currentSession.SummaryState = RpgNpcDialogueSessionSummaryState.Compressed;
                        currentSession.LastSummaryAttemptTick = Find.TickManager?.TicksGame ?? currentSession.LastSummaryAttemptTick;
                        currentSession.TurnCount = Math.Max(currentSession.TurnCount, CountDialogueTurns(currentSession.Turns));
                        currentSession.IsFinalized = true;
                        currentSession.Turns.Clear();
                        NormalizeArchiveTurns(currentArchive);
                        InvalidatePromptMemoryCacheLockless();
                        SaveArchiveToFile(currentArchive);
                    }
                },
                onError: _ =>
                {
                    lock (_syncRoot)
                    {
                        _compressionInFlight.Remove(compressionKey);
                        if (!_archiveCache.TryGetValue(archive.PawnLoadId, out RpgNpcDialogueArchive currentArchive) ||
                            currentArchive == null)
                        {
                            return;
                        }

                        RpgNpcDialogueSessionArchive currentSession = FindSession(currentArchive, session.SessionId);
                        if (currentSession == null)
                        {
                            return;
                        }

                        MarkSummaryCompressionFailed(currentArchive, currentSession);
                    }
                },
                usageChannel: DialogueUsageChannel.Rpg,
                debugSource: AIRequestDebugSource.ArchiveCompression);
        }

        private void MarkSummaryCompressionFailed(RpgNpcDialogueArchive archive, RpgNpcDialogueSessionArchive session)
        {
            if (archive == null || session == null)
            {
                return;
            }

            session.SummaryState = RpgNpcDialogueSessionSummaryState.SummaryFailed;
            session.LastSummaryAttemptTick = Find.TickManager?.TicksGame ?? session.LastSummaryAttemptTick;
            session.TurnCount = Math.Max(session.TurnCount, CountDialogueTurns(session.Turns));
            NormalizeArchiveTurns(archive);
            InvalidatePromptMemoryCacheLockless();
            SaveArchiveToFile(archive);
        }

        private static List<ChatMessageData> BuildSessionSummaryRequestMessages(
            RpgNpcDialogueArchive archive,
            RpgNpcDialogueSessionArchive session)
        {
            List<RpgNpcDialogueTurnArchive> turns = GetSessionTurns(session);
            if (turns.Count == 0)
            {
                return new List<ChatMessageData>();
            }

            Pawn npcPawn = ResolveArchiveNpcPawn(archive);
            if (npcPawn == null)
            {
                Log.Warning(
                    "[RimChat] rpg_archive_compression skipped: archive NPC pawn is missing. " +
                    $"archive_pawn_load_id={(archive?.PawnLoadId ?? -1)}, session_id={session?.SessionId ?? string.Empty}");
                return new List<ChatMessageData>();
            }

            Pawn interlocutorPawn = ResolveArchiveInterlocutorPawn(archive, session, npcPawn);
            string npcName = ResolvePromptPawnName(npcPawn, archive?.PawnName, "NPC");
            string interlocutorName = ResolvePromptPawnName(
                interlocutorPawn,
                session?.InterlocutorName ?? archive?.LastInterlocutorName,
                "Interlocutor");
            string transcript = BuildSessionTranscript(turns);
            var variables = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["pawn.target.name"] = npcName,
                ["pawn.initiator.name"] = interlocutorName,
                ["dialogue.primary_objective"] = "Summarize the dialogue session into exactly one sentence.",
                ["dialogue.optional_followup"] = "Do not output bullet points, lists, line breaks, or JSON.",
                ["dialogue.latest_unresolved_intent"] = string.Empty,
                ["dialogue.session_transcript"] = transcript
            };
            DialogueScenarioContext context = DialogueScenarioContext.CreateRpg(
                interlocutorPawn,
                npcPawn,
                false,
                new[] { "channel:rpg_archive_compression", "phase:archive_compression" });
            string systemPrompt = PromptPersistenceService.Instance.BuildUnifiedChannelSystemPrompt(
                RimTalkPromptChannel.Rpg,
                RimTalkPromptEntryChannelCatalog.RpgArchiveCompression,
                context,
                null,
                variables,
                "session_transcript",
                $"npc={npcName}\ninterlocutor={interlocutorName}\n{transcript}");
            return new List<ChatMessageData>
            {
                new ChatMessageData
                {
                    role = "system",
                    content = systemPrompt
                }
            };
        }

        private static Pawn ResolveArchiveNpcPawn(RpgNpcDialogueArchive archive)
        {
            int pawnLoadId = archive?.PawnLoadId ?? -1;
            return FindPawnByLoadId(pawnLoadId);
        }

        private static Pawn ResolveArchiveInterlocutorPawn(
            RpgNpcDialogueArchive archive,
            RpgNpcDialogueSessionArchive session,
            Pawn npcPawn)
        {
            Pawn sessionPawn = FindPawnByLoadId(session?.InterlocutorPawnLoadId ?? -1);
            if (sessionPawn != null && sessionPawn != npcPawn)
            {
                return sessionPawn;
            }

            Pawn archivePawn = FindPawnByLoadId(archive?.LastInterlocutorPawnLoadId ?? -1);
            if (archivePawn != null && archivePawn != npcPawn)
            {
                return archivePawn;
            }

            Log.Warning(
                "[RimChat] rpg_archive_compression has no bindable interlocutor pawn; bind NPC only. " +
                $"archive_pawn_load_id={(archive?.PawnLoadId ?? -1)}, " +
                $"session_interlocutor_load_id={(session?.InterlocutorPawnLoadId ?? -1)}, " +
                $"archive_last_interlocutor_load_id={(archive?.LastInterlocutorPawnLoadId ?? -1)}, " +
                $"session_id={session?.SessionId ?? string.Empty}");
            return null;
        }

        private static string ResolvePromptPawnName(Pawn pawn, string fallback, string defaultName)
        {
            string pawnName = pawn?.LabelShortCap ?? pawn?.LabelShort ?? pawn?.Name?.ToStringShort;
            if (!string.IsNullOrWhiteSpace(pawnName))
            {
                return pawnName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(fallback))
            {
                return fallback.Trim();
            }

            return defaultName;
        }

        private static string BuildSessionTranscript(List<RpgNpcDialogueTurnArchive> turns)
        {
            var sb = new StringBuilder();
            int maxTurns = Math.Min(40, turns?.Count ?? 0);
            int start = Math.Max(0, (turns?.Count ?? 0) - maxTurns);
            for (int i = start; i < turns.Count; i++)
            {
                RpgNpcDialogueTurnArchive turn = turns[i];
                if (turn == null || string.IsNullOrWhiteSpace(turn.Text))
                {
                    continue;
                }

                string role = turn.IsPlayer ? "Player" : "NPC";
                string speaker = !string.IsNullOrWhiteSpace(turn.SpeakerName) ? turn.SpeakerName : role;
                sb.Append("- ")
                    .Append(speaker)
                    .Append(": ")
                    .Append(TrimForPrompt(turn.Text, 160))
                    .Append('\n');
            }

            return sb.ToString().Trim();
        }

        private static string NormalizeToSingleSentenceSummary(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            string text = raw
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
            while (text.Contains("  "))
            {
                text = text.Replace("  ", " ");
            }

            if (text.StartsWith("\"", StringComparison.Ordinal) && text.EndsWith("\"", StringComparison.Ordinal) && text.Length > 1)
            {
                text = text.Substring(1, text.Length - 2).Trim();
            }

            int sentenceEnd = FindFirstSentenceEnd(text);
            if (sentenceEnd > 0 && sentenceEnd < text.Length - 1)
            {
                text = text.Substring(0, sentenceEnd + 1).Trim();
            }

            if (text.Length > CompressedSummaryMaxChars)
            {
                text = text.Substring(0, CompressedSummaryMaxChars - 3).TrimEnd() + "...";
            }

            return text;
        }

        private static int FindFirstSentenceEnd(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return -1;
            }

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '.' || c == '!' || c == '?' || c == '。' || c == '！' || c == '？')
                {
                    return i;
                }
            }

            return -1;
        }

        private static string BuildCompressionKey(int pawnLoadId, string sessionId)
        {
            return $"{pawnLoadId}|{sessionId}";
        }

        private static RpgNpcDialogueSessionArchive SelectLatestRetainedFullSession(RpgNpcDialogueArchive archive)
        {
            if (archive?.Sessions == null || archive.Sessions.Count == 0)
            {
                return null;
            }

            return archive.Sessions
                .Where(session =>
                    session != null &&
                    session.TurnCount >= 2 &&
                    session.Turns != null &&
                    session.Turns.Count > 0 &&
                    !string.Equals(session.SummaryState, RpgNpcDialogueSessionSummaryState.Compressed, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(session => session.EndedTick)
                .ThenByDescending(session => session.StartedTick)
                .ThenByDescending(session => session.Turns.Max(turn => turn?.TurnSequence ?? 0L))
                .FirstOrDefault();
        }

        private static List<RpgNpcDialogueTurnArchive> GetSessionTurns(RpgNpcDialogueSessionArchive session)
        {
            return session?.Turns?
                .Where(turn => turn != null && !string.IsNullOrWhiteSpace(turn.Text))
                .OrderBy(turn => turn.GameTick)
                .ThenBy(turn => turn.TurnSequence)
                .ToList() ?? new List<RpgNpcDialogueTurnArchive>();
        }

        private static List<RpgNpcDialogueSessionArchive> GetCompressedSessionsForInjection(RpgNpcDialogueArchive archive)
        {
            return archive?.Sessions?
                .Where(session =>
                    session != null &&
                    string.Equals(session.SummaryState, RpgNpcDialogueSessionSummaryState.Compressed, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(session.SummaryText))
                .OrderByDescending(session => session.EndedTick)
                .ThenByDescending(session => session.StartedTick)
                .ToList() ?? new List<RpgNpcDialogueSessionArchive>();
        }

        private static void AppendCompressedSessionSummaries(
            StringBuilder sb,
            List<RpgNpcDialogueSessionArchive> compressedSessions,
            int maxItems,
            int maxChars)
        {
            if (sb == null || compressedSessions == null || compressedSessions.Count == 0)
            {
                return;
            }

            int itemLimit = Math.Max(1, maxItems);
            int charLimit = Math.Max(120, maxChars);
            int usedChars = 0;
            int emitted = 0;
            for (int i = 0; i < compressedSessions.Count && emitted < itemLimit; i++)
            {
                RpgNpcDialogueSessionArchive session = compressedSessions[i];
                if (session == null || string.IsNullOrWhiteSpace(session.SummaryText))
                {
                    continue;
                }

                string line = $"- {TrimForPrompt(session.SummaryText, 180)}";
                if (usedChars + line.Length > charLimit)
                {
                    break;
                }

                if (emitted == 0)
                {
                    sb.AppendLine("Historical session summaries:");
                }

                sb.AppendLine(line);
                usedChars += line.Length;
                emitted++;
            }
        }
    }
}
