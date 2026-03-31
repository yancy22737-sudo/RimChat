using System;
using System.Text.RegularExpressions;

namespace RimChat.AI
{
    internal enum DiplomacyResponseContractViolation
    {
        None = 0,
        CommitmentWithoutActionJson = 1
    }

    internal sealed class DiplomacyResponseContractCheckResult
    {
        public bool IsValid { get; set; }
        public string VisibleDialogue { get; set; }
        public string TrailingActionsJson { get; set; }
        public DiplomacyResponseContractViolation Violation { get; set; }
    }

    /// <summary>
    /// Dependencies: ModelOutputSanitizer, diplomacy output JSON contract.
    /// Responsibility: fail-fast diplomacy contract check for "explicit execution commitment must carry matching actions JSON".
    /// </summary>
    internal static class DiplomacyResponseContractGuard
    {
        private static readonly Regex StrongCommitmentRegex = new Regex(
            "(?i)(我(已经|已)?提交了请求|我(会|将|这就|马上)?(安排|派出|发送|提交|下单)|I\\s*(will|'ll)\\s*(arrange|dispatch|send|submit|place)|request\\s+submitted|order\\s+placed)",
            RegexOptions.Compiled);

        public static DiplomacyResponseContractCheckResult Validate(string rawOutput)
        {
            string sanitized = ModelOutputSanitizer.StripReasoningTags(rawOutput);
            ModelOutputSanitizer.SplitVisibleAndTrailingActions(sanitized, out string visible, out string trailingActionsJson);
            return ValidateVisibleDialogueParts(visible, trailingActionsJson);
        }

        public static DiplomacyResponseContractCheckResult ValidateVisibleDialogueParts(
            string visibleDialogue,
            string trailingActionsJson)
        {
            string normalizedVisible = (visibleDialogue ?? string.Empty).Trim();
            string normalizedTrailing = (trailingActionsJson ?? string.Empty).Trim();

            var result = new DiplomacyResponseContractCheckResult
            {
                IsValid = true,
                VisibleDialogue = normalizedVisible,
                TrailingActionsJson = normalizedTrailing,
                Violation = DiplomacyResponseContractViolation.None
            };

            if (string.IsNullOrWhiteSpace(normalizedVisible))
            {
                return result;
            }

            if (ContainsExplicitCommitment(normalizedVisible) && string.IsNullOrWhiteSpace(normalizedTrailing))
            {
                result.IsValid = false;
                result.Violation = DiplomacyResponseContractViolation.CommitmentWithoutActionJson;
            }

            return result;
        }

        public static string BuildViolationTag(DiplomacyResponseContractViolation violation)
        {
            return violation switch
            {
                DiplomacyResponseContractViolation.CommitmentWithoutActionJson => "commitment_without_action_json",
                _ => "none"
            };
        }

        public static string BuildFallbackClarification()
        {
            return "我可以继续安排，但先确认这次具体请求与预算。";
        }

        private static bool ContainsExplicitCommitment(string visibleDialogue)
        {
            if (string.IsNullOrWhiteSpace(visibleDialogue))
            {
                return false;
            }

            string text = visibleDialogue.Trim();
            string lower = text.ToLowerInvariant();
            if ((lower.Contains("如果") || lower.Contains("若")) &&
                (lower.Contains("会安排") || lower.Contains("会派出") || lower.Contains("会发送") || lower.Contains("会提交")))
            {
                return false;
            }

            if (lower.Contains("if ") && lower.Contains(" will "))
            {
                return false;
            }

            return StrongCommitmentRegex.IsMatch(text);
        }
    }
}
