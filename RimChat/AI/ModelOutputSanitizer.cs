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
    }
}
