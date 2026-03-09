using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Verse;

namespace RimChat.Memory
{
    /// <summary>/// Dependencies: RpgNpcDialogueArchive/RpgNpcDialogueSessionArchive.
 /// Responsibility: serialize and parse NPC-scoped RPG archive JSON with sessions-first storage and legacy turns compatibility.
 ///</summary>
    internal static class RpgNpcDialogueArchiveJsonCodec
    {
        public static string ConvertToJson(RpgNpcDialogueArchive archive)
        {
            if (archive == null)
            {
                return "{}";
            }

            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"pawnLoadId\": {archive.PawnLoadId},\n");
            sb.Append($"  \"pawnName\": \"{EscapeJson(archive.PawnName)}\",\n");
            sb.Append($"  \"factionId\": \"{EscapeJson(archive.FactionId)}\",\n");
            sb.Append($"  \"factionName\": \"{EscapeJson(archive.FactionName)}\",\n");
            sb.Append($"  \"lastInterlocutorPawnLoadId\": {archive.LastInterlocutorPawnLoadId},\n");
            sb.Append($"  \"lastInterlocutorName\": \"{EscapeJson(archive.LastInterlocutorName)}\",\n");
            sb.Append($"  \"lastInteractionTick\": {archive.LastInteractionTick},\n");
            sb.Append($"  \"cooldownUntilTick\": {archive.CooldownUntilTick},\n");
            sb.Append($"  \"personaPrompt\": \"{EscapeJson(archive.PersonaPrompt)}\",\n");
            sb.Append($"  \"nextTurnSequence\": {archive.NextTurnSequence},\n");
            sb.Append($"  \"createdTimestamp\": {archive.CreatedTimestamp},\n");
            sb.Append($"  \"lastSavedTimestamp\": {archive.LastSavedTimestamp},\n");
            sb.Append("  \"sessions\": [\n");

            List<RpgNpcDialogueSessionArchive> sessions = archive.Sessions ?? new List<RpgNpcDialogueSessionArchive>();
            for (int i = 0; i < sessions.Count; i++)
            {
                RpgNpcDialogueSessionArchive session = sessions[i] ?? new RpgNpcDialogueSessionArchive();
                sb.Append("    {\n");
                sb.Append($"      \"sessionId\": \"{EscapeJson(session.SessionId)}\",\n");
                sb.Append($"      \"startedTick\": {session.StartedTick},\n");
                sb.Append($"      \"endedTick\": {session.EndedTick},\n");
                sb.Append($"      \"turnCount\": {session.TurnCount},\n");
                sb.Append($"      \"isFinalized\": {session.IsFinalized.ToString().ToLowerInvariant()},\n");
                sb.Append($"      \"interlocutorPawnLoadId\": {session.InterlocutorPawnLoadId},\n");
                sb.Append($"      \"interlocutorName\": \"{EscapeJson(session.InterlocutorName)}\",\n");
                sb.Append($"      \"summaryText\": \"{EscapeJson(session.SummaryText)}\",\n");
                sb.Append($"      \"summaryState\": \"{EscapeJson(session.SummaryState)}\",\n");
                sb.Append($"      \"lastSummaryAttemptTick\": {session.LastSummaryAttemptTick},\n");
                sb.Append($"      \"isLegacyImported\": {session.IsLegacyImported.ToString().ToLowerInvariant()},\n");
                sb.Append("      \"turns\": [\n");

                List<RpgNpcDialogueTurnArchive> turns = session.Turns ?? new List<RpgNpcDialogueTurnArchive>();
                for (int turnIndex = 0; turnIndex < turns.Count; turnIndex++)
                {
                    RpgNpcDialogueTurnArchive turn = turns[turnIndex] ?? new RpgNpcDialogueTurnArchive();
                    sb.Append("        {\n");
                    sb.Append($"          \"isPlayer\": {turn.IsPlayer.ToString().ToLowerInvariant()},\n");
                    sb.Append($"          \"turnSequence\": {turn.TurnSequence},\n");
                    sb.Append($"          \"speakerPawnLoadId\": {turn.SpeakerPawnLoadId},\n");
                    sb.Append($"          \"speakerName\": \"{EscapeJson(turn.SpeakerName)}\",\n");
                    sb.Append($"          \"interlocutorPawnLoadId\": {turn.InterlocutorPawnLoadId},\n");
                    sb.Append($"          \"interlocutorName\": \"{EscapeJson(turn.InterlocutorName)}\",\n");
                    sb.Append($"          \"text\": \"{EscapeJson(turn.Text)}\",\n");
                    sb.Append($"          \"gameTick\": {turn.GameTick}\n");
                    sb.Append("        }");
                    if (turnIndex < turns.Count - 1)
                    {
                        sb.Append(",");
                    }
                    sb.Append("\n");
                }

                sb.Append("      ]\n");
                sb.Append("    }");
                if (i < sessions.Count - 1)
                {
                    sb.Append(",");
                }
                sb.Append("\n");
            }

            sb.Append("  ]\n");
            sb.Append("}");
            return sb.ToString();
        }

        public static RpgNpcDialogueArchive ParseJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var archive = new RpgNpcDialogueArchive
                {
                    PawnLoadId = ExtractJsonInt(json, "pawnLoadId"),
                    PawnName = ExtractJsonString(json, "pawnName"),
                    FactionId = ExtractJsonString(json, "factionId"),
                    FactionName = ExtractJsonString(json, "factionName"),
                    LastInterlocutorPawnLoadId = ReadOptionalLoadId(json, "lastInterlocutorPawnLoadId"),
                    LastInterlocutorName = ExtractJsonString(json, "lastInterlocutorName"),
                    LastInteractionTick = ExtractJsonInt(json, "lastInteractionTick"),
                    CooldownUntilTick = ExtractJsonInt(json, "cooldownUntilTick"),
                    PersonaPrompt = ExtractJsonString(json, "personaPrompt"),
                    NextTurnSequence = ExtractJsonLong(json, "nextTurnSequence"),
                    CreatedTimestamp = ExtractJsonLong(json, "createdTimestamp"),
                    LastSavedTimestamp = ExtractJsonLong(json, "lastSavedTimestamp"),
                    Sessions = ParseSessions(json)
                };

                if (archive.PawnLoadId <= 0)
                {
                    return null;
                }

                if (archive.Sessions == null || archive.Sessions.Count == 0)
                {
                    List<RpgNpcDialogueTurnArchive> legacyTurns = ParseTurnsFromJsonArray(json, "turns");
                    archive.Sessions = ConvertLegacyTurnsToSessions(legacyTurns);
                }

                if (archive.Sessions == null)
                {
                    archive.Sessions = new List<RpgNpcDialogueSessionArchive>();
                }

                return archive;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to parse RPG NPC archive JSON: {ex.Message}");
                return null;
            }
        }

        private static List<RpgNpcDialogueSessionArchive> ParseSessions(string json)
        {
            var sessions = new List<RpgNpcDialogueSessionArchive>();
            if (!TryExtractJsonArray(json, "sessions", out string sessionsArray))
            {
                return sessions;
            }

            foreach (string sessionObject in SplitJsonObjects(sessionsArray))
            {
                List<RpgNpcDialogueTurnArchive> turns = ParseTurnsFromJsonArray(sessionObject, "turns");
                int startedTick = ExtractJsonInt(sessionObject, "startedTick");
                int endedTick = ExtractJsonInt(sessionObject, "endedTick");

                if (startedTick <= 0 && turns.Count > 0)
                {
                    startedTick = turns.Min(turn => turn.GameTick);
                }

                if (endedTick <= 0 && turns.Count > 0)
                {
                    endedTick = turns.Max(turn => turn.GameTick);
                }

                var session = new RpgNpcDialogueSessionArchive
                {
                    SessionId = ExtractJsonString(sessionObject, "sessionId"),
                    StartedTick = startedTick,
                    EndedTick = endedTick,
                    TurnCount = ExtractJsonInt(sessionObject, "turnCount"),
                    IsFinalized = ReadOptionalBool(sessionObject, "isFinalized", defaultValue: true),
                    InterlocutorPawnLoadId = ReadOptionalLoadId(sessionObject, "interlocutorPawnLoadId"),
                    InterlocutorName = ExtractJsonString(sessionObject, "interlocutorName"),
                    SummaryText = ExtractJsonString(sessionObject, "summaryText"),
                    SummaryState = ExtractJsonString(sessionObject, "summaryState"),
                    LastSummaryAttemptTick = ExtractJsonInt(sessionObject, "lastSummaryAttemptTick"),
                    IsLegacyImported = ExtractJsonBool(sessionObject, "isLegacyImported"),
                    Turns = turns
                };

                if (string.IsNullOrWhiteSpace(session.SessionId))
                {
                    session.SessionId = Guid.NewGuid().ToString("N");
                }

                if (string.IsNullOrWhiteSpace(session.SummaryState))
                {
                    session.SummaryState = RpgNpcDialogueSessionSummaryState.Pending;
                }

                if (session.TurnCount <= 0)
                {
                    session.TurnCount = CountDialogueTurns(session.Turns);
                }

                sessions.Add(session);
            }

            return sessions;
        }

        private static List<RpgNpcDialogueSessionArchive> ConvertLegacyTurnsToSessions(List<RpgNpcDialogueTurnArchive> legacyTurns)
        {
            var sessions = new List<RpgNpcDialogueSessionArchive>();
            if (legacyTurns == null || legacyTurns.Count == 0)
            {
                return sessions;
            }

            List<RpgNpcDialogueTurnArchive> normalized = legacyTurns
                .Where(turn => turn != null && !string.IsNullOrWhiteSpace(turn.Text))
                .OrderBy(turn => turn.GameTick)
                .ThenBy(turn => turn.TurnSequence)
                .ToList();
            if (normalized.Count == 0)
            {
                return sessions;
            }

            int index = 1;
            foreach (IGrouping<int, RpgNpcDialogueTurnArchive> group in normalized.GroupBy(turn => turn.GameTick))
            {
                List<RpgNpcDialogueTurnArchive> turns = group
                    .OrderBy(turn => turn.GameTick)
                    .ThenBy(turn => turn.TurnSequence)
                    .ToList();
                if (turns.Count == 0)
                {
                    continue;
                }

                RpgNpcDialogueTurnArchive last = turns[turns.Count - 1];
                sessions.Add(new RpgNpcDialogueSessionArchive
                {
                    SessionId = $"legacy_{group.Key}_{index}",
                    StartedTick = turns.Min(turn => turn.GameTick),
                    EndedTick = turns.Max(turn => turn.GameTick),
                    TurnCount = CountDialogueTurns(turns),
                    IsFinalized = true,
                    InterlocutorPawnLoadId = last.InterlocutorPawnLoadId > 0 ? last.InterlocutorPawnLoadId : -1,
                    InterlocutorName = last.InterlocutorName ?? string.Empty,
                    SummaryText = string.Empty,
                    SummaryState = RpgNpcDialogueSessionSummaryState.Pending,
                    LastSummaryAttemptTick = 0,
                    IsLegacyImported = true,
                    Turns = turns
                });
                index++;
            }

            return sessions;
        }

        private static int CountDialogueTurns(List<RpgNpcDialogueTurnArchive> turns)
        {
            return turns?
                .Count(turn => turn != null && !string.IsNullOrWhiteSpace(turn.Text))
                ?? 0;
        }

        private static List<RpgNpcDialogueTurnArchive> ParseTurnsFromJsonArray(string json, string key)
        {
            var turns = new List<RpgNpcDialogueTurnArchive>();
            if (!TryExtractJsonArray(json, key, out string turnsArray))
            {
                return turns;
            }

            foreach (string turnObject in SplitJsonObjects(turnsArray))
            {
                string text = ExtractJsonString(turnObject, "text");
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                turns.Add(new RpgNpcDialogueTurnArchive
                {
                    IsPlayer = ExtractJsonBool(turnObject, "isPlayer"),
                    TurnSequence = ExtractJsonLong(turnObject, "turnSequence"),
                    SpeakerPawnLoadId = ReadOptionalLoadId(turnObject, "speakerPawnLoadId"),
                    SpeakerName = ExtractJsonString(turnObject, "speakerName"),
                    InterlocutorPawnLoadId = ReadOptionalLoadId(turnObject, "interlocutorPawnLoadId"),
                    InterlocutorName = ExtractJsonString(turnObject, "interlocutorName"),
                    Text = text,
                    GameTick = ExtractJsonInt(turnObject, "gameTick")
                });
            }

            return turns;
        }

        private static bool TryExtractJsonArray(string json, string key, out string arrayContent)
        {
            arrayContent = string.Empty;
            string pattern = $"\"{key}\"\\s*:\\s*\\[";
            Match match = Regex.Match(json ?? string.Empty, pattern);
            if (!match.Success)
            {
                return false;
            }

            int start = (json ?? string.Empty).IndexOf('[', match.Index);
            if (start < 0 || !TryFindJsonBlockEnd(json, start, '[', ']', out int end))
            {
                return false;
            }

            arrayContent = json.Substring(start, end - start + 1);
            return true;
        }

        private static bool TryFindJsonBlockEnd(string json, int blockStart, char openChar, char closeChar, out int endIndex)
        {
            endIndex = -1;
            if (string.IsNullOrEmpty(json) || blockStart < 0 || blockStart >= json.Length || json[blockStart] != openChar)
            {
                return false;
            }

            int depth = 0;
            bool inString = false;
            bool escaped = false;
            for (int i = blockStart; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == openChar)
                {
                    depth++;
                }
                else if (c == closeChar)
                {
                    depth--;
                }

                if (depth == 0)
                {
                    endIndex = i;
                    return true;
                }
            }

            return false;
        }

        private static List<string> SplitJsonObjects(string arrayJson)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(arrayJson))
            {
                return result;
            }

            string content = arrayJson.Trim();
            if (content.StartsWith("["))
            {
                content = content.Substring(1);
            }

            if (content.EndsWith("]"))
            {
                content = content.Substring(0, content.Length - 1);
            }

            int depth = 0;
            int start = -1;
            bool inString = false;
            bool escaped = false;

            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{')
                {
                    if (depth == 0)
                    {
                        start = i;
                    }

                    depth++;
                    continue;
                }

                if (c != '}')
                {
                    continue;
                }

                depth--;
                if (depth == 0 && start >= 0)
                {
                    result.Add(content.Substring(start, i - start + 1));
                    start = -1;
                }
            }

            return result;
        }

        private static string ExtractJsonString(string json, string key)
        {
            string pattern = $"\"{key}\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"";
            Match match = Regex.Match(json ?? string.Empty, pattern);
            if (!match.Success || match.Groups.Count < 2)
            {
                return string.Empty;
            }

            return match.Groups[1].Value
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");
        }

        private static int ExtractJsonInt(string json, string key)
        {
            string pattern = $"\"{key}\"\\s*:\\s*(-?\\d+)";
            Match match = Regex.Match(json ?? string.Empty, pattern);
            return match.Success && int.TryParse(match.Groups[1].Value, out int value) ? value : 0;
        }

        private static long ExtractJsonLong(string json, string key)
        {
            string pattern = $"\"{key}\"\\s*:\\s*(-?\\d+)";
            Match match = Regex.Match(json ?? string.Empty, pattern);
            return match.Success && long.TryParse(match.Groups[1].Value, out long value) ? value : 0L;
        }

        private static bool ExtractJsonBool(string json, string key)
        {
            string pattern = $"\"{key}\"\\s*:\\s*(true|false)";
            Match match = Regex.Match(json ?? string.Empty, pattern, RegexOptions.IgnoreCase);
            return match.Success && string.Equals(match.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static int ReadOptionalLoadId(string json, string key)
        {
            int value = ExtractJsonInt(json, key);
            return value > 0 ? value : -1;
        }

        private static bool ReadOptionalBool(string json, string key, bool defaultValue)
        {
            string pattern = $"\"{key}\"\\s*:\\s*(true|false)";
            Match match = Regex.Match(json ?? string.Empty, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return defaultValue;
            }

            return string.Equals(match.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }
}
