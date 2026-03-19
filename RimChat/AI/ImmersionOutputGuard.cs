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
        StatusPanelNumeric = 3
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
            new Regex(@"(?i)(?:当前|current|remaining)?\s*(?:好感度|goodwill|阈值|threshold|冷却|cooldown)\s*(?:[:：=]|为|是|remaining|current)\s*[-+]?\d+(?:\.\d+)?", RegexOptions.Compiled),
            new Regex(@"(?i)\b(?:status|system)\b.{0,20}\b(?:panel|state|value|metric)\b", RegexOptions.Compiled)
        };

        private static readonly Regex ParentheticalMetadataRegex = new Regex(
            @"[（(]\s*当前[^)）\r\n]{0,120}[)）]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex StatusPanelNumericRegex = new Regex(
            @"(?im)(?:好感度|阈值|冷却|goodwill|threshold|cooldown|relation|status|api[_\s-]?limits?|blocked\s*actions?|token|request\s*id)\s*[:：]\s*-?\d+(?:\.\d+)?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static ImmersionGuardResult ValidateVisibleDialogue(string rawOutput)
        {
            string sanitizedOutput = ModelOutputSanitizer.StripReasoningTags(rawOutput);
            SplitVisibleAndTrailingActions(sanitizedOutput, out string visible, out string trailingJson);
            string normalizedVisible = visible?.Trim() ?? string.Empty;

            var result = new ImmersionGuardResult
            {
                IsValid = true,
                VisibleDialogue = normalizedVisible,
                TrailingActionsJson = trailingJson ?? string.Empty,
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
