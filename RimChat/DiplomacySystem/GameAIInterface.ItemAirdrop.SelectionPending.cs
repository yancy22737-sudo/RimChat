using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Config;
using UnityEngine;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: airdrop candidate models and selection window policy.
    /// Responsibility: build forced selection and timeout pending-selection payloads for item airdrop.
    /// </summary>
    public partial class GameAIInterface
    {
        private APIResult TryBuildForcedSelection(
            string forcedSelectedDefName,
            ItemAirdropIntent intent,
            ItemAirdropCandidatePack candidatePack,
            int budget,
            RimChatSettings settings,
            RequestedCountExtraction requestedCount,
            out ItemAirdropSelection selection,
            out string countSource,
            out int hardMax,
            out int maxByBudget)
        {
            selection = null;
            countSource = "selected_def_family_default";
            hardMax = 0;
            maxByBudget = 0;
            if (candidatePack?.Candidates == null || candidatePack.Candidates.Count == 0)
            {
                return BuildSelectionFailure("selection_out_of_candidates", "selected_def is not available in current candidate list.");
            }

            ThingDefRecord selectedRecord = candidatePack.Candidates
                .Select(candidate => candidate.Record)
                .FirstOrDefault(record =>
                    record?.Def != null &&
                    string.Equals(record.DefName, forcedSelectedDefName, StringComparison.OrdinalIgnoreCase));
            if (selectedRecord == null)
            {
                return BuildSelectionFailure(
                    "selection_out_of_candidates",
                    $"selected_def '{forcedSelectedDefName}' is not in candidate list.");
            }

            ComputeLegalCountWindow(budget, selectedRecord, settings, out maxByBudget, out int maxBySystem, out hardMax);
            if (hardMax <= 0)
            {
                string message = $"Budget {budget} is too low for {selectedRecord.DefName}. maxByBudget={maxByBudget},maxBySystem={maxBySystem},hardMax={hardMax}.";
                return BuildSelectionFailure("budget_too_low", message);
            }

            if (requestedCount.HasMultipleCounts)
            {
                return BuildSelectionFailure(
                    "need_count_ambiguous",
                    "need contains multiple explicit counts; request_item_airdrop supports single-item count only.");
            }

            int resolvedCount;
            if (requestedCount.HasExplicitCount)
            {
                countSource = "selected_def_explicit";
                if (requestedCount.RequestedCount > hardMax)
                {
                    string message = $"count {requestedCount.RequestedCount} exceeds max legal count {hardMax} (maxByBudget={maxByBudget},maxBySystem={maxBySystem},hardMax={hardMax}).";
                    return BuildSelectionFailure("selection_count_out_of_range", message);
                }

                resolvedCount = requestedCount.RequestedCount;
            }
            else
            {
                int baseCount = ResolveFamilyDefaultCount(intent?.Family ?? ItemAirdropNeedFamily.Unknown);
                resolvedCount = Mathf.Clamp(Math.Min(baseCount, hardMax), 1, hardMax);
                countSource = "selected_def_family_default";
            }

            selection = new ItemAirdropSelection
            {
                SelectedDefName = selectedRecord.DefName,
                Count = resolvedCount,
                Reason = "player_confirmed_selected_def"
            };
            return APIResult.SuccessResult("Selection from selected_def succeeded.", selection);
        }

        private APIResult BuildTimeoutPendingSelection(
            ItemAirdropIntent intent,
            ItemAirdropCandidatePack candidatePack,
            int budget,
            RimChatSettings settings,
            string failureCode,
            string failureReason)
        {
            string resolvedFailureCode = string.IsNullOrWhiteSpace(failureCode)
                ? "selection_timeout"
                : failureCode;
            string resolvedFailureReason = string.IsNullOrWhiteSpace(failureReason)
                ? "Second-pass LLM selection timed out."
                : failureReason;
            if (candidatePack?.Candidates == null || candidatePack.Candidates.Count == 0)
            {
                return BuildSelectionFailure(resolvedFailureCode, resolvedFailureReason);
            }

            var options = new List<ItemAirdropPendingSelectionOption>();
            foreach (ItemAirdropCandidate candidate in candidatePack.Candidates.Take(3))
            {
                if (candidate?.Record?.Def == null)
                {
                    continue;
                }

                ComputeLegalCountWindow(budget, candidate.Record, settings, out _, out _, out int hardMax);
                if (hardMax <= 0)
                {
                    continue;
                }

                options.Add(new ItemAirdropPendingSelectionOption
                {
                    Index = options.Count + 1,
                    DefName = candidate.Record.DefName ?? string.Empty,
                    Label = candidate.Record.Label ?? candidate.Record.DefName ?? string.Empty,
                    UnitPrice = Math.Max(0.01f, candidate.Record.MarketValue),
                    MaxLegalCount = hardMax
                });
            }

            if (options.Count == 0)
            {
                return BuildSelectionFailure(resolvedFailureCode, resolvedFailureReason);
            }

            var pendingData = new ItemAirdropPendingSelectionData
            {
                NeedText = intent?.NeedText ?? string.Empty,
                BudgetSilver = budget,
                FailureCode = resolvedFailureCode,
                FailureReason = resolvedFailureReason,
                Options = options
            };
            return APIResult.SuccessResult("Selection timeout; awaiting player confirmation.", pendingData);
        }

        private static string BuildPendingSelectionAuditDetails(ItemAirdropPendingSelectionData pendingData)
        {
            if (pendingData?.Options == null || pendingData.Options.Count == 0)
            {
                return "pending=true,options=none";
            }

            string summary = string.Join(
                "|",
                pendingData.Options.Take(3).Select(option =>
                    $"{option.Index}:{option.DefName}@{option.UnitPrice:F1}/max{option.MaxLegalCount}"));
            return $"pending=true,code={pendingData.FailureCode},reason={pendingData.FailureReason},options={summary}";
        }
    }
}
