using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: PromptVariableCatalog.
    /// Responsibility: scan prompt text and return token/text segments for valid namespaced variables.
    /// </summary>
    internal static class PromptVariableTokenScanner
    {
        private static readonly Regex VariableTokenRegex = new Regex(
            "\\{\\{\\s*([a-zA-Z_][a-zA-Z0-9_]*(?:\\.[a-zA-Z0-9_]+)+)\\s*\\}\\}",
            RegexOptions.Compiled);

        public static List<PromptTokenSegment> ParseSegments(string text)
        {
            string content = text ?? string.Empty;
            var segments = new List<PromptTokenSegment>();
            int cursor = 0;
            foreach (Match match in VariableTokenRegex.Matches(content))
            {
                if (!TryReadValidVariableMatch(match, out string variableName))
                {
                    continue;
                }

                AppendTextSegment(segments, content, cursor, match.Index);
                segments.Add(BuildTokenSegment(content, variableName, match));
                cursor = match.Index + match.Length;
            }

            AppendTextSegment(segments, content, cursor, content.Length);
            return segments;
        }

        private static bool TryReadValidVariableMatch(Match match, out string variableName)
        {
            variableName = string.Empty;
            if (match == null || !match.Success || match.Groups.Count < 2)
            {
                return false;
            }

            variableName = match.Groups[1].Value?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(variableName) && PromptVariableCatalog.Contains(variableName);
        }

        private static void AppendTextSegment(ICollection<PromptTokenSegment> output, string text, int start, int end)
        {
            if (end <= start)
            {
                return;
            }

            output.Add(new PromptTokenSegment
            {
                Kind = PromptTokenSegmentKind.Text,
                StartIndex = start,
                Length = end - start,
                Text = text.Substring(start, end - start)
            });
        }

        private static PromptTokenSegment BuildTokenSegment(string text, string variableName, Match match)
        {
            return new PromptTokenSegment
            {
                Kind = PromptTokenSegmentKind.VariableToken,
                StartIndex = match.Index,
                Length = match.Length,
                Text = text.Substring(match.Index, match.Length),
                VariableName = variableName
            };
        }
    }
}
