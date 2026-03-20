using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RimChat.AI
{
    internal enum TextIntegrityIssue
    {
        None = 0,
        ReplacementCharacter = 1,
        LowPrintableRatio = 2,
        FragmentedText = 3,
        ControlNoise = 4
    }

    internal sealed class TextIntegrityCheckResult
    {
        public bool IsValid { get; set; }
        public string VisibleDialogue { get; set; }
        public string TrailingActionsJson { get; set; }
        public TextIntegrityIssue Issue { get; set; }
        public string ReasonTag { get; set; }
    }

    internal static class TextIntegrityGuard
    {
        private const float MinPrintableRatio = 0.92f;
        private const float MaxShortTokenRatio = 0.62f;
        private const int MinTokenCountForFragmentJudge = 8;
        private const int MaxSummaryLength = 280;
        private const int MaxFactLength = 100;
        private static readonly Regex MultiWhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

        public static string SanitizeSummaryText(string text, int maxChars = MaxSummaryLength)
        {
            return SanitizeTextCore(text, Math.Max(1, maxChars));
        }

        public static string SanitizeKeyFact(string text, int maxChars = MaxFactLength)
        {
            return SanitizeTextCore(text, Math.Max(1, maxChars));
        }

        public static bool TryDetectCorruption(string text, out TextIntegrityIssue issue, out string reasonTag)
        {
            string normalized = SanitizeTextCore(text, int.MaxValue);
            return TryDetectCorruptionInternal(normalized, out issue, out reasonTag);
        }

        public static TextIntegrityCheckResult ValidateVisibleDialogue(string rawOutput)
        {
            string sanitizedOutput = ModelOutputSanitizer.StripReasoningTags(rawOutput);
            SplitVisibleAndTrailingActions(sanitizedOutput, out string visible, out string trailingJson);
            string normalizedVisible = SanitizeTextCore(visible, int.MaxValue);

            bool corrupted = TryDetectCorruptionInternal(normalizedVisible, out TextIntegrityIssue issue, out string reasonTag);
            return new TextIntegrityCheckResult
            {
                IsValid = !corrupted,
                VisibleDialogue = normalizedVisible,
                TrailingActionsJson = trailingJson ?? string.Empty,
                Issue = issue,
                ReasonTag = reasonTag ?? string.Empty
            };
        }

        private static bool TryDetectCorruptionInternal(string text, out TextIntegrityIssue issue, out string reasonTag)
        {
            issue = TextIntegrityIssue.None;
            reasonTag = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            int replacementCount = text.Count(ch => ch == '\uFFFD');
            if (replacementCount > 0)
            {
                issue = TextIntegrityIssue.ReplacementCharacter;
                reasonTag = "replacement_char";
                return true;
            }

            int printableCount = 0;
            int controlCount = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (char.IsControl(ch))
                {
                    if (ch != '\r' && ch != '\n' && ch != '\t')
                    {
                        controlCount++;
                    }
                }
                else
                {
                    printableCount++;
                }
            }

            if (controlCount > 0 && controlCount >= Math.Max(2, text.Length / 25))
            {
                issue = TextIntegrityIssue.ControlNoise;
                reasonTag = "control_noise";
                return true;
            }

            int total = printableCount + controlCount;
            if (total >= 24)
            {
                float printableRatio = printableCount / (float)total;
                if (printableRatio < MinPrintableRatio)
                {
                    issue = TextIntegrityIssue.LowPrintableRatio;
                    reasonTag = "low_printable_ratio";
                    return true;
                }
            }

            string[] tokens = Regex.Split(text, @"[\s,.;:!?，。！？；、]+")
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray();
            if (tokens.Length >= MinTokenCountForFragmentJudge)
            {
                int shortCount = tokens.Count(token => token.Length <= 2);
                int longCount = tokens.Count(token => token.Length >= 6);
                float shortRatio = shortCount / (float)tokens.Length;
                if (shortRatio >= MaxShortTokenRatio && longCount <= 2)
                {
                    issue = TextIntegrityIssue.FragmentedText;
                    reasonTag = "fragmented_text";
                    return true;
                }
            }

            return false;
        }

        private static string SanitizeTextCore(string text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(text.Length);
            bool previousWasWhitespace = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (char.IsControl(ch))
                {
                    if (ch == '\n' || ch == '\r' || ch == '\t')
                    {
                        if (!previousWasWhitespace)
                        {
                            sb.Append(' ');
                            previousWasWhitespace = true;
                        }
                    }

                    continue;
                }

                if (char.IsWhiteSpace(ch))
                {
                    if (!previousWasWhitespace)
                    {
                        sb.Append(' ');
                        previousWasWhitespace = true;
                    }

                    continue;
                }

                sb.Append(ch);
                previousWasWhitespace = false;
            }

            string normalized = MultiWhitespaceRegex.Replace(sb.ToString(), " ").Trim();
            if (maxChars > 0 && normalized.Length > maxChars)
            {
                normalized = normalized.Substring(0, Math.Max(1, maxChars)).TrimEnd();
            }

            return normalized;
        }

        private static void SplitVisibleAndTrailingActions(string rawOutput, out string visible, out string trailingActionsJson)
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
