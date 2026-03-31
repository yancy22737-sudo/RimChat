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
            ModelOutputSanitizer.SplitVisibleAndTrailingActions(sanitized, out string visible, out string trailingActionsJson);
            return ValidateVisibleDialogueParts(visible, trailingActionsJson, sanitized);
        }

        public static RpgResponseContractCheckResult ValidateVisibleDialogueParts(
            string visibleDialogue,
            string trailingActionsJson,
            string placeholderSource = null)
        {
            string normalizedVisible = (visibleDialogue ?? string.Empty).Trim();
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

            string source = normalizedTrailing.Length > 0 ? normalizedTrailing : placeholderSource ?? string.Empty;
            if (PlaceholderTokenRegex.IsMatch(source))
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
    }
}
