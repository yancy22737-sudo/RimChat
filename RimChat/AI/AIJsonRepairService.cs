using System;
using System.Collections.Generic;
using System.Text;

namespace RimChat.AI
{
    /// <summary>
    /// Dependencies: string parsing utilities.
    /// Responsibility: repair truncated JSON tail blocks for action contracts.
    /// </summary>
    public static class AIJsonRepairService
    {
        public static bool TryRepairTrailingJsonBlock(string source, bool dropIncompleteLastAction, out string repaired)
        {
            repaired = string.Empty;
            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            string normalized = StripCodeFence(source);
            int jsonStart = FindTrailingJsonStart(normalized);
            if (jsonStart < 0)
            {
                return false;
            }

            string prefix = normalized.Substring(0, jsonStart).TrimEnd();
            string rawJson = normalized.Substring(jsonStart).Trim();
            if (!TryRepairJsonObject(rawJson, dropIncompleteLastAction, out string fixedJson))
            {
                return false;
            }

            repaired = string.IsNullOrWhiteSpace(prefix)
                ? fixedJson
                : prefix + "\n" + fixedJson;
            return true;
        }

        public static bool TryRepairJsonObject(string rawJson, bool dropIncompleteLastAction, out string repairedJson)
        {
            repairedJson = string.Empty;
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return false;
            }

            string normalized = StripCodeFence(rawJson).Trim();
            if (normalized.Length == 0 || normalized[0] != '{')
            {
                return false;
            }

            string pruned = dropIncompleteLastAction
                ? PruneIncompleteActionTail(normalized)
                : normalized;
            repairedJson = BalanceBrackets(pruned);
            return repairedJson.Length > 1;
        }

        private static string StripCodeFence(string text)
        {
            string value = text ?? string.Empty;
            value = RemoveTokenIgnoreCase(value, "```json");
            value = RemoveTokenIgnoreCase(value, "```");
            return value.Trim();
        }

        private static string RemoveTokenIgnoreCase(string source, string token)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(token))
            {
                return source ?? string.Empty;
            }

            int index = source.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return source;
            }

            var sb = new StringBuilder(source.Length);
            int cursor = 0;
            while (index >= 0)
            {
                sb.Append(source, cursor, index - cursor);
                cursor = index + token.Length;
                index = source.IndexOf(token, cursor, StringComparison.OrdinalIgnoreCase);
            }

            sb.Append(source, cursor, source.Length - cursor);
            return sb.ToString();
        }

        private static int FindTrailingJsonStart(string content)
        {
            int actionsKeyIndex = content.LastIndexOf("\"actions\"", StringComparison.OrdinalIgnoreCase);
            if (actionsKeyIndex >= 0)
            {
                int objectStart = content.LastIndexOf('{', actionsKeyIndex);
                if (objectStart >= 0)
                {
                    return objectStart;
                }
            }

            return content.LastIndexOf('{');
        }

        private static string PruneIncompleteActionTail(string json)
        {
            int actionsIndex = json.IndexOf("\"actions\"", StringComparison.OrdinalIgnoreCase);
            if (actionsIndex < 0)
            {
                return json;
            }

            int arrayStart = json.IndexOf('[', actionsIndex);
            if (arrayStart < 0)
            {
                return json;
            }

            int arrayEnd = FindMatchingBracket(json, arrayStart, '[', ']');
            if (arrayEnd >= 0)
            {
                return json;
            }

            if (!TryFindLastCompleteActionEnd(json, arrayStart, out int completeActionEnd))
            {
                return json.Substring(0, arrayStart + 1) + "]";
            }

            return json.Substring(0, completeActionEnd + 1) + "]";
        }

        private static bool TryFindLastCompleteActionEnd(string json, int arrayStart, out int endIndex)
        {
            endIndex = -1;
            bool inString = false;
            bool escape = false;
            int objectDepth = 0;
            int objectStart = -1;

            for (int i = arrayStart + 1; i < json.Length; i++)
            {
                char ch = json[i];
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escape = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    continue;
                }

                if (ch == '{')
                {
                    if (objectDepth == 0)
                    {
                        objectStart = i;
                    }
                    objectDepth++;
                    continue;
                }

                if (ch == '}')
                {
                    if (objectDepth > 0)
                    {
                        objectDepth--;
                        if (objectDepth == 0 && objectStart >= 0)
                        {
                            endIndex = i;
                        }
                    }
                }
            }

            return endIndex >= 0;
        }

        private static int FindMatchingBracket(string text, int start, char open, char close)
        {
            bool inString = false;
            bool escape = false;
            int depth = 0;

            for (int i = start; i < text.Length; i++)
            {
                char ch = text[i];
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escape = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    continue;
                }

                if (ch == open)
                {
                    depth++;
                    continue;
                }

                if (ch != close)
                {
                    continue;
                }

                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private static string BalanceBrackets(string json)
        {
            var stack = new Stack<char>();
            bool inString = false;
            bool escape = false;
            var sb = new StringBuilder(json.Length + 16);

            for (int i = 0; i < json.Length; i++)
            {
                char ch = json[i];
                sb.Append(ch);

                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escape = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    continue;
                }

                if (ch == '{' || ch == '[')
                {
                    stack.Push(ch);
                    continue;
                }

                if (ch == '}')
                {
                    if (stack.Count > 0 && stack.Peek() == '{')
                    {
                        stack.Pop();
                    }
                    continue;
                }

                if (ch == ']' && stack.Count > 0 && stack.Peek() == '[')
                {
                    stack.Pop();
                }
            }

            while (stack.Count > 0)
            {
                char open = stack.Pop();
                sb.Append(open == '{' ? '}' : ']');
            }

            return sb.ToString().Trim();
        }
    }
}
