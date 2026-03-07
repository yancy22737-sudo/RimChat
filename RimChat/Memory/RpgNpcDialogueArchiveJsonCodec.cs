using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RimChat.Relation;
using Verse;

namespace RimChat.Memory
{
    /// <summary>
    /// Dependencies: RpgNpcDialogueArchive and RPGRelationValues.
    /// Responsibility: serialize/deserialize NPC-scoped RPG archive JSON with defensive parsing.
    /// </summary>
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
            sb.Append($"  \"lastInteractionTick\": {archive.LastInteractionTick},\n");
            sb.Append($"  \"cooldownUntilTick\": {archive.CooldownUntilTick},\n");
            sb.Append($"  \"personaPrompt\": \"{EscapeJson(archive.PersonaPrompt)}\",\n");
            AppendRelationValues(sb, archive.RelationValues);
            sb.Append(",\n");
            sb.Append($"  \"createdTimestamp\": {archive.CreatedTimestamp},\n");
            sb.Append($"  \"lastSavedTimestamp\": {archive.LastSavedTimestamp},\n");
            sb.Append("  \"turns\": [\n");

            List<RpgNpcDialogueTurnArchive> turns = archive.Turns ?? new List<RpgNpcDialogueTurnArchive>();
            for (int i = 0; i < turns.Count; i++)
            {
                RpgNpcDialogueTurnArchive turn = turns[i] ?? new RpgNpcDialogueTurnArchive();
                sb.Append("    {\n");
                sb.Append($"      \"isPlayer\": {turn.IsPlayer.ToString().ToLower()},\n");
                sb.Append($"      \"text\": \"{EscapeJson(turn.Text)}\",\n");
                sb.Append($"      \"gameTick\": {turn.GameTick}\n");
                sb.Append("    }");
                if (i < turns.Count - 1)
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
                    LastInteractionTick = ExtractJsonInt(json, "lastInteractionTick"),
                    CooldownUntilTick = ExtractJsonInt(json, "cooldownUntilTick"),
                    PersonaPrompt = ExtractJsonString(json, "personaPrompt"),
                    CreatedTimestamp = ExtractJsonLong(json, "createdTimestamp"),
                    LastSavedTimestamp = ExtractJsonLong(json, "lastSavedTimestamp"),
                    RelationValues = ParseRelationValues(json),
                    Turns = ParseTurns(json)
                };

                if (archive.PawnLoadId <= 0)
                {
                    return null;
                }

                if (archive.RelationValues == null)
                {
                    archive.RelationValues = new RPGRelationValues();
                }

                if (archive.Turns == null)
                {
                    archive.Turns = new List<RpgNpcDialogueTurnArchive>();
                }

                return archive;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to parse RPG NPC archive JSON: {ex.Message}");
                return null;
            }
        }

        private static void AppendRelationValues(StringBuilder sb, RPGRelationValues values)
        {
            RPGRelationValues relationValues = values ?? new RPGRelationValues();
            sb.Append("  \"relationValues\": {\n");
            sb.Append($"    \"favorability\": {relationValues.Favorability.ToString(CultureInfo.InvariantCulture)},\n");
            sb.Append($"    \"trust\": {relationValues.Trust.ToString(CultureInfo.InvariantCulture)},\n");
            sb.Append($"    \"fear\": {relationValues.Fear.ToString(CultureInfo.InvariantCulture)},\n");
            sb.Append($"    \"respect\": {relationValues.Respect.ToString(CultureInfo.InvariantCulture)},\n");
            sb.Append($"    \"dependency\": {relationValues.Dependency.ToString(CultureInfo.InvariantCulture)}\n");
            sb.Append("  }");
        }

        private static RPGRelationValues ParseRelationValues(string json)
        {
            var relation = new RPGRelationValues();
            if (!TryExtractJsonObject(json, "relationValues", out string content))
            {
                return relation;
            }

            relation.Favorability = ExtractJsonFloat(content, "favorability");
            relation.Trust = ExtractJsonFloat(content, "trust");
            relation.Fear = ExtractJsonFloat(content, "fear");
            relation.Respect = ExtractJsonFloat(content, "respect");
            relation.Dependency = ExtractJsonFloat(content, "dependency");
            return relation;
        }

        private static List<RpgNpcDialogueTurnArchive> ParseTurns(string json)
        {
            var turns = new List<RpgNpcDialogueTurnArchive>();
            if (!TryExtractJsonArray(json, "turns", out string content))
            {
                return turns;
            }

            foreach (string obj in SplitJsonObjects(content))
            {
                string text = ExtractJsonString(obj, "text");
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                turns.Add(new RpgNpcDialogueTurnArchive
                {
                    IsPlayer = ExtractJsonBool(obj, "isPlayer"),
                    Text = text,
                    GameTick = ExtractJsonInt(obj, "gameTick")
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

        private static bool TryExtractJsonObject(string json, string key, out string objectContent)
        {
            objectContent = string.Empty;
            string pattern = $"\"{key}\"\\s*:\\s*\\{{";
            Match match = Regex.Match(json ?? string.Empty, pattern);
            if (!match.Success)
            {
                return false;
            }

            int start = (json ?? string.Empty).IndexOf('{', match.Index);
            if (start < 0 || !TryFindJsonBlockEnd(json, start, '{', '}', out int end))
            {
                return false;
            }

            objectContent = json.Substring(start, end - start + 1);
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

        private static float ExtractJsonFloat(string json, string key)
        {
            string pattern = $"\"{key}\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)";
            Match match = Regex.Match(json ?? string.Empty, pattern);
            if (!match.Success)
            {
                return 0f;
            }

            return float.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value
                : 0f;
        }

        private static bool ExtractJsonBool(string json, string key)
        {
            string pattern = $"\"{key}\"\\s*:\\s*(true|false)";
            Match match = Regex.Match(json ?? string.Empty, pattern, RegexOptions.IgnoreCase);
            return match.Success && string.Equals(match.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
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
