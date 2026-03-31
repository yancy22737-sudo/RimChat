using System;
using System.Text.RegularExpressions;

namespace RimChat.AI
{
    /// <summary>
    /// Dependencies: .NET regex runtime.
    /// Responsibility: strip hidden reasoning tag blocks from model output text.
    /// </summary>
    public static class ModelOutputSanitizer
    {
        private static readonly Regex ClosedThinkBlockRegex = new Regex(
            @"<\s*(?:think|thinking)\b[^>]*>[\s\S]*?<\s*/\s*(?:think|thinking)\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex OpenThinkTagRegex = new Regex(
            @"<\s*(?:think|thinking)\b[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex CloseThinkTagRegex = new Regex(
            @"<\s*/\s*(?:think|thinking)\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string StripReasoningTags(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string sanitized = ClosedThinkBlockRegex.Replace(text, " ");
            sanitized = RemoveDanglingOpenThinkBlock(sanitized);
            sanitized = CloseThinkTagRegex.Replace(sanitized, " ");
            return sanitized;
        }

        public static void SplitVisibleAndTrailingActions(
            string rawOutput,
            out string visible,
            out string trailingActionsJson)
        {
            string trimmed = rawOutput?.Trim() ?? string.Empty;
            if (trimmed.Length == 0)
            {
                visible = string.Empty;
                trailingActionsJson = string.Empty;
                return;
            }

            int trailingStart = FindTrailingJsonObjectStart(trimmed);
            if (trailingStart < 0)
            {
                visible = trimmed;
                trailingActionsJson = string.Empty;
                return;
            }

            string candidate = trimmed.Substring(trailingStart).Trim();
            if (!LooksLikeActionsObject(candidate))
            {
                visible = trimmed;
                trailingActionsJson = string.Empty;
                return;
            }

            visible = trimmed.Substring(0, trailingStart).TrimEnd();
            trailingActionsJson = candidate;
        }

        public static string ComposeVisibleAndTrailingActions(string visible, string trailingActionsJson)
        {
            string normalizedVisible = visible?.Trim() ?? string.Empty;
            string normalizedActions = trailingActionsJson?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedVisible))
            {
                return normalizedActions;
            }

            if (string.IsNullOrWhiteSpace(normalizedActions))
            {
                return normalizedVisible;
            }

            return normalizedVisible + normalizedActions;
        }

        private static string RemoveDanglingOpenThinkBlock(string text)
        {
            int openIndex = GetLastMatchIndex(OpenThinkTagRegex, text);
            if (openIndex < 0)
            {
                return text;
            }

            int closeIndex = GetLastMatchIndex(CloseThinkTagRegex, text);
            if (closeIndex > openIndex)
            {
                return text;
            }

            return text.Substring(0, openIndex);
        }

        private static int GetLastMatchIndex(Regex regex, string text)
        {
            if (regex == null || string.IsNullOrEmpty(text))
            {
                return -1;
            }

            MatchCollection matches = regex.Matches(text);
            if (matches == null || matches.Count == 0)
            {
                return -1;
            }

            return matches[matches.Count - 1].Index;
        }

        private static bool LooksLikeActionsObject(string json)
        {
            return !string.IsNullOrWhiteSpace(json) &&
                json.StartsWith("{", StringComparison.Ordinal) &&
                json.IndexOf("\"actions\"", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int FindTrailingJsonObjectStart(string text)
        {
            int end = LastNonWhitespaceIndex(text);
            if (end < 0 || text[end] != '}')
            {
                return -1;
            }

            int depth = 0;
            bool inString = false;
            bool escape = false;
            int latestStart = -1;
            int latestEnd = -1;
            for (int i = 0; i < text.Length; i++)
            {
                char current = text[i];
                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                    }
                    else if (current == '\\')
                    {
                        escape = true;
                    }
                    else if (current == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                    continue;
                }

                if (current == '{')
                {
                    if (depth == 0)
                    {
                        latestStart = i;
                    }

                    depth++;
                    continue;
                }

                if (current != '}')
                {
                    continue;
                }

                if (depth <= 0)
                {
                    return -1;
                }

                depth--;
                if (depth == 0)
                {
                    latestEnd = i;
                }
            }

            if (depth != 0 || latestStart < 0 || latestEnd != end)
            {
                return -1;
            }

            return latestStart;
        }

        private static int LastNonWhitespaceIndex(string text)
        {
            for (int i = text.Length - 1; i >= 0; i--)
            {
                if (!char.IsWhiteSpace(text[i]))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
