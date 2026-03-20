using System;
using System.Text.RegularExpressions;

namespace RimChat.AI
{
    internal enum RpgResponseContractViolation
    {
        None = 0,
        MissingVisibleDialogue = 1,
        PlaceholderActionPayload = 2,
        MultilineVisibleDialogue = 3
    }

    internal sealed class RpgResponseContractCheckResult
    {
        public bool IsValid { get; set; }
        public string VisibleDialogue { get; set; }
        public string TrailingActionsJson { get; set; }
        public RpgResponseContractViolation Violation { get; set; }
    }

    /// <summary>
    /// Responsibility: validate RPG output contract (one-line visible dialogue + optional trailing {"actions":[...]}).
    /// </summary>
    internal static class RpgResponseContractGuard
    {
        private static readonly Regex PlaceholderTokenRegex = new Regex(
            "(?i)(optionaldef|optionalreason|\"?amount\"?\\s*:\\s*0)",
            RegexOptions.Compiled);

        public static RpgResponseContractCheckResult Validate(string rawOutput)
        {
            string sanitized = ModelOutputSanitizer.StripReasoningTags(rawOutput);
            SplitVisibleAndTrailingActions(sanitized, out string visible, out string trailingActionsJson);
            string normalizedVisible = (visible ?? string.Empty).Trim();
            string normalizedTrailing = (trailingActionsJson ?? string.Empty).Trim();

            var result = new RpgResponseContractCheckResult
            {
                IsValid = true,
                VisibleDialogue = normalizedVisible,
                TrailingActionsJson = normalizedTrailing,
                Violation = RpgResponseContractViolation.None
            };

            if (string.IsNullOrWhiteSpace(normalizedVisible))
            {
                result.IsValid = false;
                result.Violation = RpgResponseContractViolation.MissingVisibleDialogue;
                return result;
            }

            if (normalizedVisible.IndexOf('\r') >= 0 || normalizedVisible.IndexOf('\n') >= 0)
            {
                result.IsValid = false;
                result.Violation = RpgResponseContractViolation.MultilineVisibleDialogue;
                return result;
            }

            string placeholderSource = normalizedTrailing.Length > 0 ? normalizedTrailing : sanitized ?? string.Empty;
            if (PlaceholderTokenRegex.IsMatch(placeholderSource))
            {
                result.IsValid = false;
                result.Violation = RpgResponseContractViolation.PlaceholderActionPayload;
                return result;
            }

            return result;
        }

        public static string BuildViolationTag(RpgResponseContractViolation violation)
        {
            return violation switch
            {
                RpgResponseContractViolation.MissingVisibleDialogue => "missing_visible_dialogue",
                RpgResponseContractViolation.PlaceholderActionPayload => "placeholder_action_payload",
                RpgResponseContractViolation.MultilineVisibleDialogue => "multiline_visible_dialogue",
                _ => "none"
            };
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
