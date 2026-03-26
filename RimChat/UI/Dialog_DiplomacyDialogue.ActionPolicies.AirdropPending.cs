using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using RimChat.AI;
using RimChat.Memory;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: delayed action runtime intent state and airdrop confirmation payload.
    /// Responsibility: map player follow-ups to pending airdrop timeout candidate selections.
    /// </summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private static readonly Regex AirdropPendingChoicePattern = new Regex(
            @"(?<!\d)(?<index>[1-9]\d?)(?!\d)",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex AirdropPendingCountPattern = new Regex(
            @"(?<!\d)(?<count>\d{1,5})(?!\d)",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex AirdropTradeCardNeedCountPattern = new Regex(
            @"(?:需求|need)\s+[^\r\n,，。]*?(?:x|×)\s*(?<count>\d{1,5})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static bool TryMapAirdropPendingSelectionFollowup(
            ParsedResponse response,
            FactionDialogueSession currentSession,
            PendingDelayedActionIntent baseIntent,
            string playerMessage,
            int assistantRound)
        {
            if (response == null || currentSession == null || baseIntent == null)
            {
                return false;
            }

            if (!string.Equals(baseIntent.ActionType, AIActionNames.RequestItemAirdrop, StringComparison.Ordinal))
            {
                return false;
            }

            if (!TryReadPendingAirdropCandidates(baseIntent.Parameters, out List<PendingAirdropSelectionCandidate> candidates) ||
                candidates.Count == 0)
            {
                return false;
            }

            if (!TryResolvePendingAirdropCandidate(playerMessage, candidates, out PendingAirdropSelectionCandidate selected))
            {
                string normalizedPlayer = (playerMessage ?? string.Empty).Trim().ToLowerInvariant();
                bool shouldClarify = ContainsAnyHint(normalizedPlayer, ConfirmationHints) ||
                                     ContainsAnyHint(normalizedPlayer, AmbiguousFollowupHints);
                if (!shouldClarify)
                {
                    return false;
                }

                string clarification = BuildPendingAirdropSelectionClarification(candidates);
                response.DialogueText = string.IsNullOrWhiteSpace(response.DialogueText)
                    ? clarification
                    : $"{response.DialogueText}\n\n{clarification}";

                return true;
            }

            Dictionary<string, object> mappedParameters = CloneParameters(baseIntent.Parameters);
            mappedParameters.Remove(AirdropPendingCandidatesKey);
            mappedParameters.Remove(AirdropPendingFailureCodeKey);
            mappedParameters["selected_def"] = selected.DefName;
            if (TryExtractAirdropRequestedCount(playerMessage, out int requestedCount))
            {
                mappedParameters["count"] = requestedCount;
            }

            currentSession.ClearPendingAirdropTradeCardReference();

            if (response.Actions == null)
            {
                response.Actions = new List<AIAction>();
            }

            var mappedAction = new AIAction
            {
                ActionType = AIActionNames.RequestItemAirdrop,
                Parameters = mappedParameters,
                Reason = "intent_map_pending_selection"
            };
            response.Actions.Add(mappedAction);

            if (string.IsNullOrWhiteSpace(response.DialogueText))
            {
                response.DialogueText = "RimChat_ItemAirdropSelectionChosen".Translate(
                    selected.Label,
                    selected.DefName).ToString();
            }

            var awaitingIntent = baseIntent.Clone();
            awaitingIntent.ActionType = AIActionNames.RequestItemAirdrop;
            awaitingIntent.Parameters = mappedParameters;
            awaitingIntent.Signature = BuildActionSignature(AIActionNames.RequestItemAirdrop, mappedParameters);
            awaitingIntent.AwaitingConfirmation = true;
            awaitingIntent.RequiredParameter = string.Empty;
            awaitingIntent.UpdatedAssistantRound = assistantRound;
            currentSession.pendingDelayedActionIntent = awaitingIntent;
            currentSession.lastDelayedActionIntent = awaitingIntent;
            return true;
        }

        private static bool TryReadPendingAirdropCandidates(
            Dictionary<string, object> parameters,
            out List<PendingAirdropSelectionCandidate> candidates)
        {
            candidates = new List<PendingAirdropSelectionCandidate>();
            if (parameters == null ||
                !parameters.TryGetValue(AirdropPendingCandidatesKey, out object rawCandidates) ||
                !(rawCandidates is IEnumerable<object> rows))
            {
                return false;
            }

            foreach (object row in rows)
            {
                if (!(row is Dictionary<string, object> data))
                {
                    continue;
                }

                string defName = ReadCandidateText(data, "defName");
                if (string.IsNullOrWhiteSpace(defName))
                {
                    continue;
                }

                string label = ReadCandidateText(data, "label");
                int index = ReadCandidateIndex(data, candidates.Count + 1);
                float unitPrice = ReadCandidateFloat(data, "unitPrice");
                int maxLegalCount = ReadCandidateIndex(data, "max_legal_count", 0);
                candidates.Add(new PendingAirdropSelectionCandidate
                {
                    Index = index,
                    DefName = defName,
                    Label = string.IsNullOrWhiteSpace(label) ? defName : label,
                    UnitPrice = unitPrice,
                    MaxLegalCount = maxLegalCount
                });
            }

            return candidates.Count > 0;
        }

        private static bool TryResolvePendingAirdropCandidate(
            string playerMessage,
            List<PendingAirdropSelectionCandidate> candidates,
            out PendingAirdropSelectionCandidate selected)
        {
            selected = null;
            if (candidates == null || candidates.Count == 0)
            {
                return false;
            }

            string text = (playerMessage ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            Match indexMatch = AirdropPendingChoicePattern.Match(text);
            if (indexMatch.Success &&
                int.TryParse(indexMatch.Groups["index"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedIndex))
            {
                selected = candidates.FirstOrDefault(candidate => candidate.Index == parsedIndex);
                if (selected != null)
                {
                    return true;
                }
            }

            int chineseIndex = TryParseChineseChoiceIndex(text);
            if (chineseIndex > 0)
            {
                selected = candidates.FirstOrDefault(candidate => candidate.Index == chineseIndex);
                if (selected != null)
                {
                    return true;
                }
            }

            string normalized = text.ToLowerInvariant();
            List<PendingAirdropSelectionCandidate> byName = candidates
                .Where(candidate =>
                    (!string.IsNullOrWhiteSpace(candidate.DefName) &&
                     normalized.Contains(candidate.DefName.ToLowerInvariant())) ||
                    (!string.IsNullOrWhiteSpace(candidate.Label) &&
                     normalized.Contains(candidate.Label.ToLowerInvariant())))
                .ToList();
            if (byName.Count == 1)
            {
                selected = byName[0];
                return true;
            }

            return false;
        }

        private static bool TryExtractAirdropRequestedCount(string playerMessage, out int requestedCount)
        {
            requestedCount = 0;
            if (string.IsNullOrWhiteSpace(playerMessage))
            {
                return false;
            }

            Match structuredNeedCountMatch = AirdropTradeCardNeedCountPattern.Match(playerMessage);
            if (structuredNeedCountMatch.Success &&
                int.TryParse(structuredNeedCountMatch.Groups["count"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int structuredNeedCount) &&
                structuredNeedCount > 0)
            {
                requestedCount = Math.Min(structuredNeedCount, 5000);
                return true;
            }

            MatchCollection matches = AirdropPendingCountPattern.Matches(playerMessage);
            if (matches == null || matches.Count <= 0)
            {
                return false;
            }

            int maxValue = 0;
            for (int i = 0; i < matches.Count; i++)
            {
                if (!int.TryParse(matches[i].Groups["count"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                {
                    continue;
                }

                maxValue = Math.Max(maxValue, value);
            }

            // Ignore pure option-index replies like "1/2/3/4/5".
            if (maxValue <= 5)
            {
                return false;
            }

            requestedCount = Math.Min(maxValue, 5000);
            return requestedCount > 0;
        }

        private static int TryParseChineseChoiceIndex(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            if (text.Contains("一"))
            {
                return 1;
            }

            if (text.Contains("二") || text.Contains("两"))
            {
                return 2;
            }

            if (text.Contains("三"))
            {
                return 3;
            }

            if (text.Contains("四"))
            {
                return 4;
            }

            if (text.Contains("五"))
            {
                return 5;
            }

            return 0;
        }

        private static string BuildPendingAirdropSelectionClarification(List<PendingAirdropSelectionCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return "RimChat_ItemAirdropAwaitingConfirmSystem".Translate().ToString();
            }

            string lines = string.Join(
                "\n",
                candidates
                    .OrderBy(candidate => candidate.Index)
                    .Take(5)
                    .Select(candidate => "RimChat_ItemAirdropSelectionPendingLine".Translate(
                        candidate.Index,
                        candidate.Label,
                        candidate.DefName,
                        candidate.UnitPrice.ToString("F1", CultureInfo.InvariantCulture),
                        Math.Max(0, candidate.MaxLegalCount)).ToString()));
            return "RimChat_ItemAirdropSelectionPendingSystem".Translate(lines).ToString();
        }

        private static string ReadCandidateText(Dictionary<string, object> values, string key)
        {
            if (values == null || !values.TryGetValue(key, out object raw) || raw == null)
            {
                return string.Empty;
            }

            return raw.ToString()?.Trim() ?? string.Empty;
        }

        private static int ReadCandidateIndex(Dictionary<string, object> values, int fallback)
        {
            return ReadCandidateIndex(values, "index", fallback);
        }

        private static int ReadCandidateIndex(Dictionary<string, object> values, string key, int fallback)
        {
            if (values == null || !values.TryGetValue(key, out object raw) || raw == null)
            {
                return fallback;
            }

            if (raw is int intValue)
            {
                return intValue > 0 ? intValue : fallback;
            }

            if (raw is long longValue && longValue > 0 && longValue <= int.MaxValue)
            {
                return (int)longValue;
            }

            return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
                ? parsed
                : fallback;
        }

        private static float ReadCandidateFloat(Dictionary<string, object> values, string key)
        {
            if (values == null || !values.TryGetValue(key, out object raw) || raw == null)
            {
                return 0f;
            }

            if (raw is float floatValue)
            {
                return floatValue;
            }

            if (raw is double doubleValue)
            {
                return (float)doubleValue;
            }

            return float.TryParse(raw.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                ? parsed
                : 0f;
        }

        private sealed class PendingAirdropSelectionCandidate
        {
            public int Index;
            public string DefName = string.Empty;
            public string Label = string.Empty;
            public float UnitPrice;
            public int MaxLegalCount;
        }
    }
}
