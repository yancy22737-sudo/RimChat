using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Verse;

namespace RimDiplomacy.Memory
{
    /// <summary>
    /// Dependencies: FactionLeaderMemory and cross-channel summary model.
    /// Responsibility: serialize/deserialize leader memory JSON with backward-compatible field mapping.
    /// </summary>
    internal static class LeaderMemoryJsonCodec
    {
        public static string ConvertMemoryToJson(FactionLeaderMemory memory)
        {
            if (memory == null)
            {
                return "{}";
            }

            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"ownerFactionId\": \"{EscapeJson(memory.OwnerFactionId)}\",\n");
            sb.Append($"  \"ownerFactionName\": \"{EscapeJson(memory.OwnerFactionName)}\",\n");
            sb.Append($"  \"leaderName\": \"{EscapeJson(memory.LeaderName)}\",\n");
            sb.Append($"  \"lastUpdatedTick\": {memory.LastUpdatedTick},\n");
            sb.Append($"  \"createdTimestamp\": {memory.CreatedTimestamp},\n");
            sb.Append($"  \"lastSavedTimestamp\": {memory.LastSavedTimestamp},\n");

            sb.Append("  \"factionMemories\": [\n");
            bool firstFaction = true;
            List<FactionMemoryEntry> memories = memory.FactionMemories ?? new List<FactionMemoryEntry>();
            for (int i = 0; i < memories.Count; i++)
            {
                FactionMemoryEntry fm = memories[i];
                if (fm == null)
                {
                    continue;
                }

                if ((fm.MentionCount <= 0 && fm.PositiveInteractions <= 0 && fm.NegativeInteractions <= 0) ||
                    fm.FactionId == memory.OwnerFactionId)
                {
                    continue;
                }

                if (!firstFaction) sb.Append(",\n");
                firstFaction = false;
                sb.Append("    {\n");
                sb.Append($"      \"factionName\": \"{EscapeJson(fm.FactionName)}\",\n");
                sb.Append($"      \"factionId\": \"{EscapeJson(fm.FactionId)}\",\n");
                sb.Append($"      \"mentionCount\": {fm.MentionCount},\n");
                sb.Append($"      \"positiveInteractions\": {fm.PositiveInteractions},\n");
                sb.Append($"      \"negativeInteractions\": {fm.NegativeInteractions}\n");
                sb.Append("    }");
            }
            sb.Append("\n  ],\n");

            sb.Append("  \"significantEvents\": [\n");
            List<SignificantEventMemory> events = (memory.SignificantEvents ?? new List<SignificantEventMemory>())
                .Where(evt => evt != null)
                .ToList();
            for (int i = 0; i < events.Count; i++)
            {
                SignificantEventMemory evt = events[i];
                sb.Append("    {\n");
                sb.Append($"      \"eventType\": \"{evt.EventType}\",\n");
                sb.Append($"      \"involvedFactionId\": \"{EscapeJson(evt.InvolvedFactionId)}\",\n");
                sb.Append($"      \"involvedFactionName\": \"{EscapeJson(evt.InvolvedFactionName)}\",\n");
                sb.Append($"      \"description\": \"{EscapeJson(evt.Description)}\",\n");
                sb.Append($"      \"occurredTick\": {evt.OccurredTick},\n");
                sb.Append($"      \"timestamp\": {evt.Timestamp}\n");
                sb.Append("    }");
                if (i < events.Count - 1) sb.Append(",");
                sb.Append("\n");
            }
            sb.Append("  ],\n");

            sb.Append("  \"dialogueHistory\": [\n");
            List<DialogueRecord> dialogues = (memory.DialogueHistory ?? new List<DialogueRecord>())
                .Where(dlg => dlg != null && !string.IsNullOrWhiteSpace(dlg.Message))
                .ToList();
            int startIndex = Math.Max(0, dialogues.Count - 50);
            List<DialogueRecord> recentDialogues = dialogues.Skip(startIndex).ToList();
            for (int i = 0; i < recentDialogues.Count; i++)
            {
                DialogueRecord dlg = recentDialogues[i];
                sb.Append("    {\n");
                sb.Append($"      \"isPlayer\": {dlg.IsPlayer.ToString().ToLower()},\n");
                sb.Append($"      \"message\": \"{EscapeJson(dlg.Message)}\",\n");
                sb.Append($"      \"gameTick\": {dlg.GameTick}\n");
                sb.Append("    }");
                if (i < recentDialogues.Count - 1) sb.Append(",");
                sb.Append("\n");
            }
            sb.Append("  ],\n");

            AppendSummaryArray(sb, "rpgDepartSummaries", memory.RpgDepartSummaries);
            sb.Append(",\n");
            AppendSummaryArray(sb, "diplomacySessionSummaries", memory.DiplomacySessionSummaries);
            sb.Append("\n");
            sb.Append("}");
            return sb.ToString();
        }

        public static FactionLeaderMemory ParseJsonToMemory(string json)
        {
            try
            {
                var memory = new FactionLeaderMemory();
                memory.OwnerFactionId = FirstNonEmpty(
                    ExtractJsonString(json, "ownerFactionId"),
                    ExtractJsonString(json, "OwnerFactionId"),
                    ExtractJsonString(json, "StringId"));
                memory.OwnerFactionName = FirstNonEmpty(
                    ExtractJsonString(json, "ownerFactionName"),
                    ExtractJsonString(json, "OwnerFactionName"));
                memory.LeaderName = FirstNonEmpty(
                    ExtractJsonString(json, "leaderName"),
                    ExtractJsonString(json, "LeaderName"),
                    ExtractJsonString(json, "Name"));
                memory.LastUpdatedTick = FirstNonZero(
                    ExtractJsonInt(json, "lastUpdatedTick"),
                    ExtractJsonInt(json, "LastUpdatedTick"));
                memory.CreatedTimestamp = FirstNonZeroLong(
                    ExtractJsonLong(json, "createdTimestamp"),
                    ExtractJsonLong(json, "CreatedTimestamp"),
                    DateTime.UtcNow.Ticks);
                memory.LastSavedTimestamp = FirstNonZeroLong(
                    ExtractJsonLong(json, "lastSavedTimestamp"),
                    ExtractJsonLong(json, "LastSavedTimestamp"),
                    memory.CreatedTimestamp);

                ParseFactionMemories(json, memory);
                ParseSignificantEvents(json, memory);
                ParseDialogueHistory(json, memory);
                ParseCrossChannelSummaries(json, memory);
                return memory;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimDiplomacy] Failed to parse JSON memory: {ex.Message}");
                return null;
            }
        }

        private static void AppendSummaryArray(StringBuilder sb, string key, List<CrossChannelSummaryRecord> records)
        {
            sb.Append($"  \"{key}\": [\n");
            List<CrossChannelSummaryRecord> source = records ?? new List<CrossChannelSummaryRecord>();
            for (int i = 0; i < source.Count; i++)
            {
                CrossChannelSummaryRecord record = source[i];
                if (record == null || string.IsNullOrWhiteSpace(record.SummaryText))
                {
                    continue;
                }

                sb.Append("    {\n");
                sb.Append($"      \"source\": \"{record.Source}\",\n");
                sb.Append($"      \"factionId\": \"{EscapeJson(record.FactionId)}\",\n");
                sb.Append($"      \"pawnLoadId\": {record.PawnLoadId},\n");
                sb.Append($"      \"pawnName\": \"{EscapeJson(record.PawnName)}\",\n");
                sb.Append($"      \"summaryText\": \"{EscapeJson(record.SummaryText)}\",\n");
                sb.Append($"      \"gameTick\": {record.GameTick},\n");
                sb.Append($"      \"confidence\": {record.Confidence.ToString(CultureInfo.InvariantCulture)},\n");
                sb.Append($"      \"contentHash\": \"{EscapeJson(record.ContentHash)}\",\n");
                sb.Append($"      \"isLlmFallback\": {record.IsLlmFallback.ToString().ToLower()},\n");
                sb.Append($"      \"createdTimestamp\": {record.CreatedTimestamp},\n");
                sb.Append("      \"keyFacts\": [");

                List<string> facts = record.KeyFacts ?? new List<string>();
                for (int factIndex = 0; factIndex < facts.Count; factIndex++)
                {
                    if (factIndex > 0) sb.Append(", ");
                    sb.Append($"\"{EscapeJson(facts[factIndex] ?? string.Empty)}\"");
                }
                sb.Append("]\n");
                sb.Append("    }");

                bool hasNext = false;
                for (int j = i + 1; j < source.Count; j++)
                {
                    if (source[j] != null && !string.IsNullOrWhiteSpace(source[j].SummaryText))
                    {
                        hasNext = true;
                        break;
                    }
                }

                if (hasNext)
                {
                    sb.Append(",");
                }
                sb.Append("\n");
            }

            sb.Append("  ]");
        }

        private static void ParseFactionMemories(string json, FactionLeaderMemory memory)
        {
            memory.FactionMemories = new List<FactionMemoryEntry>();
            if (!TryExtractJsonArray(json, "factionMemories", out string content) &&
                !TryExtractJsonArray(json, "FactionMemories", out content))
            {
                return;
            }

            foreach (string obj in SplitJsonObjects(content))
            {
                string factionId = FirstNonEmpty(
                    ExtractJsonString(obj, "factionId"),
                    ExtractJsonString(obj, "StringId"));
                if (string.IsNullOrWhiteSpace(factionId))
                {
                    continue;
                }

                memory.FactionMemories.Add(new FactionMemoryEntry
                {
                    FactionId = factionId,
                    FactionName = FirstNonEmpty(ExtractJsonString(obj, "factionName"), ExtractJsonString(obj, "Name")),
                    MentionCount = FirstNonZero(ExtractJsonInt(obj, "mentionCount"), ExtractJsonInt(obj, "MentionCount")),
                    PositiveInteractions = FirstNonZero(ExtractJsonInt(obj, "positiveInteractions"), ExtractJsonInt(obj, "PositiveInteractions")),
                    NegativeInteractions = FirstNonZero(ExtractJsonInt(obj, "negativeInteractions"), ExtractJsonInt(obj, "NegativeInteractions"))
                });
            }
        }

        private static void ParseSignificantEvents(string json, FactionLeaderMemory memory)
        {
            memory.SignificantEvents = new List<SignificantEventMemory>();
            if (!TryExtractJsonArray(json, "significantEvents", out string content) &&
                !TryExtractJsonArray(json, "SignificantEvents", out content))
            {
                return;
            }

            foreach (string obj in SplitJsonObjects(content))
            {
                string eventTypeRaw = FirstNonEmpty(ExtractJsonString(obj, "eventType"), ExtractJsonString(obj, "EventType"));
                if (!Enum.TryParse(eventTypeRaw, true, out SignificantEventType eventType))
                {
                    continue;
                }

                memory.SignificantEvents.Add(new SignificantEventMemory
                {
                    EventType = eventType,
                    InvolvedFactionId = FirstNonEmpty(ExtractJsonString(obj, "involvedFactionId"), ExtractJsonString(obj, "InvolvedFactionId")),
                    InvolvedFactionName = FirstNonEmpty(ExtractJsonString(obj, "involvedFactionName"), ExtractJsonString(obj, "InvolvedFactionName")),
                    Description = FirstNonEmpty(ExtractJsonString(obj, "description"), ExtractJsonString(obj, "Description")),
                    OccurredTick = FirstNonZero(ExtractJsonInt(obj, "occurredTick"), ExtractJsonInt(obj, "OccurredTick")),
                    Timestamp = FirstNonZeroLong(ExtractJsonLong(obj, "timestamp"), ExtractJsonLong(obj, "Timestamp"))
                });
            }
        }

        private static void ParseDialogueHistory(string json, FactionLeaderMemory memory)
        {
            memory.DialogueHistory = new List<DialogueRecord>();
            if (!TryExtractJsonArray(json, "dialogueHistory", out string content) &&
                !TryExtractJsonArray(json, "DialogueHistory", out content))
            {
                return;
            }

            foreach (string obj in SplitJsonObjects(content))
            {
                bool isPlayer = ExtractJsonBool(obj, "isPlayer");
                if (!obj.Contains("\"isPlayer\""))
                {
                    int side = ExtractJsonInt(obj, "Side");
                    isPlayer = side == 0;
                }

                string message = FirstNonEmpty(ExtractJsonString(obj, "message"), ExtractJsonString(obj, "Msg"));
                int tick = FirstNonZero(ExtractJsonInt(obj, "gameTick"), ExtractJsonInt(obj, "GameTick"));
                if (string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                memory.DialogueHistory.Add(new DialogueRecord
                {
                    IsPlayer = isPlayer,
                    Message = message,
                    GameTick = tick
                });
            }
        }

        private static void ParseCrossChannelSummaries(string json, FactionLeaderMemory memory)
        {
            memory.RpgDepartSummaries = ParseSummaryArray(json, "rpgDepartSummaries", "RpgDepartSummaries");
            memory.DiplomacySessionSummaries = ParseSummaryArray(json, "diplomacySessionSummaries", "DiplomacySessionSummaries");
        }

        private static List<CrossChannelSummaryRecord> ParseSummaryArray(string json, string primaryKey, string legacyKey)
        {
            var result = new List<CrossChannelSummaryRecord>();
            if (!TryExtractJsonArray(json, primaryKey, out string content) &&
                !TryExtractJsonArray(json, legacyKey, out content))
            {
                return result;
            }

            foreach (string obj in SplitJsonObjects(content))
            {
                string summary = ExtractJsonString(obj, "summaryText");
                if (string.IsNullOrWhiteSpace(summary))
                {
                    continue;
                }

                string sourceRaw = ExtractJsonString(obj, "source");
                if (!Enum.TryParse(sourceRaw, true, out CrossChannelSummarySource source))
                {
                    source = CrossChannelSummarySource.Unknown;
                }

                result.Add(new CrossChannelSummaryRecord
                {
                    Source = source,
                    FactionId = ExtractJsonString(obj, "factionId"),
                    PawnLoadId = ExtractJsonInt(obj, "pawnLoadId"),
                    PawnName = ExtractJsonString(obj, "pawnName"),
                    SummaryText = summary,
                    GameTick = ExtractJsonInt(obj, "gameTick"),
                    Confidence = ExtractJsonFloat(obj, "confidence"),
                    ContentHash = ExtractJsonString(obj, "contentHash"),
                    IsLlmFallback = ExtractJsonBool(obj, "isLlmFallback"),
                    CreatedTimestamp = ExtractJsonLong(obj, "createdTimestamp"),
                    KeyFacts = ParseStringArrayField(obj, "keyFacts")
                });
            }

            return result;
        }

        private static List<string> ParseStringArrayField(string json, string key)
        {
            if (!TryExtractJsonArray(json, key, out string content))
            {
                return new List<string>();
            }

            var list = new List<string>();
            MatchCollection matches = Regex.Matches(content, "\"((?:\\\\.|[^\"])*)\"");
            foreach (Match match in matches)
            {
                if (!match.Success || match.Groups.Count < 2)
                {
                    continue;
                }

                string value = match.Groups[1].Value
                    .Replace("\\n", "\n")
                    .Replace("\\r", "\r")
                    .Replace("\\t", "\t")
                    .Replace("\\\"", "\"")
                    .Replace("\\\\", "\\");
                if (!string.IsNullOrWhiteSpace(value))
                {
                    list.Add(value.Trim());
                }
            }

            return list;
        }

        private static bool TryExtractJsonArray(string json, string key, out string arrayContent)
        {
            arrayContent = string.Empty;
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
            {
                return false;
            }

            string pattern = $"\"{key}\"\\s*:\\s*\\[";
            Match match = Regex.Match(json, pattern);
            if (!match.Success)
            {
                return false;
            }

            int start = json.IndexOf('[', match.Index);
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
            bool escape = false;
            for (int i = blockStart; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escape = true;
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

                if (c == openChar) depth++;
                if (c == closeChar) depth--;
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
            int objectStart = -1;
            bool inString = false;
            bool escape = false;
            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];
                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escape = true;
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
                        objectStart = i;
                    }
                    depth++;
                    continue;
                }

                if (c == '}')
                {
                    depth--;
                    if (depth == 0 && objectStart >= 0)
                    {
                        result.Add(content.Substring(objectStart, i - objectStart + 1));
                        objectStart = -1;
                    }
                }
            }

            return result;
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
            return match.Success && int.TryParse(match.Groups[1].Value, out int result) ? result : 0;
        }

        private static long ExtractJsonLong(string json, string key)
        {
            string pattern = $"\"{key}\"\\s*:\\s*(-?\\d+)";
            Match match = Regex.Match(json ?? string.Empty, pattern);
            return match.Success && long.TryParse(match.Groups[1].Value, out long result) ? result : 0L;
        }

        private static float ExtractJsonFloat(string json, string key)
        {
            string pattern = $"\"{key}\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)";
            Match match = Regex.Match(json ?? string.Empty, pattern);
            if (!match.Success)
            {
                return 0f;
            }

            return float.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
                ? result
                : 0f;
        }

        private static bool ExtractJsonBool(string json, string key)
        {
            string pattern = $"\"{key}\"\\s*:\\s*(true|false)";
            Match match = Regex.Match(json ?? string.Empty, pattern, RegexOptions.IgnoreCase);
            return match.Success && string.Equals(match.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    return values[i].Trim();
                }
            }

            return string.Empty;
        }

        private static int FirstNonZero(params int[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] != 0)
                {
                    return values[i];
                }
            }

            return 0;
        }

        private static long FirstNonZeroLong(params long[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] != 0L)
                {
                    return values[i];
                }
            }

            return 0L;
        }
    }
}
