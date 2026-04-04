using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Verse;

namespace RimChat.AI
{
    public enum ImmersionViolationReason
    {
        None = 0,
        ParentheticalMetadata = 1,
        MechanicKeyword = 2,
        StatusPanelNumeric = 3,
        ReasoningLeakage = 4
    }

    public sealed class ImmersionGuardResult
    {
        public bool IsValid { get; set; }
        public string VisibleDialogue { get; set; }
        public string TrailingActionsJson { get; set; }
        public ImmersionViolationReason ViolationReason { get; set; }
        public string ViolationSnippet { get; set; }
    }

    /// <summary>
    /// Responsibility: fail-fast validation for visible dialogue to block immersion-breaking mechanic leakage.
    /// Scope: checks only visible narrative text and ignores one trailing {"actions":[...]} JSON block.
    /// </summary>
    public static class ImmersionOutputGuard
    {
        private static readonly Regex[] MechanicLeakageRegexes =
        {
            new Regex(@"(?i)\b(?:api[_\s-]?limits?|blocked\s*actions?|system\s*prompt|prompt\s*template|request\s*id|requestid|token\s*(?:usage|count|budget)?)\b", RegexOptions.Compiled),
            new Regex(@"(?i)^\s*(?:当前|此刻|现在)\s*(?:好感|关系|友好)\s*(?:为|是|:|：|=)\s*[-+]?\d+\s*(?:点|级)?\s*$", RegexOptions.Compiled | RegexOptions.Multiline),
            new Regex(@"(?i)^\s*(?:冷却|cooldown)\s*(?:为|是|:|：|=)\s*\d+\s*(?:秒|分钟|小时| ticks)?\s*$", RegexOptions.Compiled | RegexOptions.Multiline),
            new Regex(@"(?i)\b(?:status|system)\b.{0,20}\b(?:panel|state|value|metric)\b", RegexOptions.Compiled)
        };

        private static readonly Regex ParentheticalMetadataRegex = new Regex(
            @"[（(]\s*(?:当前|此刻|现在)\s*(?:状态|好感|关系|形势|局势)[^)）\r\n]{0,60}[)）]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex StatusPanelNumericRegex = new Regex(
            @"(?im)^(?:\s*(?:好感度|阈值|冷却|goodwill|threshold|cooldown|relation|status|api[_\s-]?limits?|blocked\s*actions?|token|request\s*id)\s*[:：]\s*-?\d+(?:\.\d+)?\s*[,;]?\s*)+$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

        private static readonly Regex ReasoningLeakageRegex = new Regex(
            @"(?i)(让我想想|先分析|先思考|思路如下|我的思考|推理过程|分析如下|根据以上规则|检查输出格式|I\s*(?:need|will|should)\s*(?:think|analyze|reason)|let\s+me\s+think|my\s+reasoning|step[\s-]*by[\s-]*step|according\s+to\s+the\s+rules)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static ImmersionGuardResult ValidateVisibleDialogue(string rawOutput)
        {
            string sanitizedOutput = ModelOutputSanitizer.StripReasoningTags(rawOutput);
            ModelOutputSanitizer.SplitVisibleAndTrailingActions(sanitizedOutput, out string visible, out string trailingJson);
            return ValidateVisibleDialogueParts(visible, trailingJson);
        }

        public static ImmersionGuardResult ValidateVisibleDialogueParts(string visibleDialogue, string trailingActionsJson = null)
        {
            string normalizedVisible = visibleDialogue?.Trim() ?? string.Empty;

            var result = new ImmersionGuardResult
            {
                IsValid = true,
                VisibleDialogue = normalizedVisible,
                TrailingActionsJson = trailingActionsJson ?? string.Empty,
                ViolationReason = ImmersionViolationReason.None,
                ViolationSnippet = string.Empty
            };

            if (string.IsNullOrWhiteSpace(normalizedVisible))
            {
                return result;
            }

            if (TryDetectViolation(normalizedVisible, out ImmersionViolationReason reason, out string snippet))
            {
                result.IsValid = false;
                result.ViolationReason = reason;
                result.ViolationSnippet = snippet ?? string.Empty;
            }

            return result;
        }

        public static string BuildViolationTag(ImmersionViolationReason reason)
        {
            return reason switch
            {
                ImmersionViolationReason.ParentheticalMetadata => "parenthetical_metadata",
                ImmersionViolationReason.MechanicKeyword => "mechanic_keyword",
                ImmersionViolationReason.StatusPanelNumeric => "status_panel_numeric",
                ImmersionViolationReason.ReasoningLeakage => "reasoning_leakage",
                _ => "unknown"
            };
        }

        public static string BuildLocalFallbackDialogue(DialogueUsageChannel channel)
        {
            return channel switch
            {
                DialogueUsageChannel.Rpg => "RimChat_ImmersionFallback_Rpg".Translate().ToString(),
                _ => "RimChat_ImmersionFallback_Diplomacy".Translate().ToString()
            };
        }

        private static bool TryDetectViolation(string visibleText, out ImmersionViolationReason reason, out string snippet)
        {
            Match parenthetical = ParentheticalMetadataRegex.Match(visibleText);
            if (parenthetical.Success)
            {
                reason = ImmersionViolationReason.ParentheticalMetadata;
                snippet = parenthetical.Value;
                return true;
            }

            if (TryFindMechanicKeyword(visibleText, out string keyword))
            {
                reason = ImmersionViolationReason.MechanicKeyword;
                snippet = keyword;
                return true;
            }

            Match panel = StatusPanelNumericRegex.Match(visibleText);
            if (panel.Success)
            {
                reason = ImmersionViolationReason.StatusPanelNumeric;
                snippet = panel.Value;
                return true;
            }

            Match reasoning = ReasoningLeakageRegex.Match(visibleText);
            if (reasoning.Success)
            {
                reason = ImmersionViolationReason.ReasoningLeakage;
                snippet = reasoning.Value;
                return true;
            }

            reason = ImmersionViolationReason.None;
            snippet = string.Empty;
            return false;
        }

        private static bool TryFindMechanicKeyword(string visibleText, out string keyword)
        {
            for (int i = 0; i < MechanicLeakageRegexes.Length; i++)
            {
                Match match = MechanicLeakageRegexes[i].Match(visibleText);
                if (match.Success)
                {
                    keyword = match.Value;
                    return true;
                }
            }

            keyword = string.Empty;
            return false;
        }

    }
}
