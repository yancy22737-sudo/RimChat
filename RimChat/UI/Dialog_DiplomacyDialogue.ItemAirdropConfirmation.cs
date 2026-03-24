using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimChat.AI;
using RimChat.Dialogue;
using RimChat.DiplomacySystem;
using RimChat.Memory;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: GameAIInterface prepared airdrop trade API and dialogue session runtime state.
    /// Responsibility: show final confirmation dialog for barter airdrop and commit/cancel the prepared trade.
    /// </summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private const string AirdropPendingCandidatesKey = "__airdrop_pending_candidates";
        private const string AirdropPendingFailureCodeKey = "__airdrop_pending_failure_code";

        private static bool IsRequestItemAirdropAction(AIAction action)
        {
            return action != null &&
                   string.Equals(action.ActionType, AIActionNames.RequestItemAirdrop, StringComparison.Ordinal);
        }

        private bool TryHandleAirdropActionWithConfirmation(
            AIAction action,
            FactionDialogueSession currentSession,
            Faction currentFaction,
            out ActionExecutionOutcome outcome)
        {
            outcome = null;
            if (!IsRequestItemAirdropAction(action))
            {
                return false;
            }

            if (IsAirdropAsyncRequestPending(currentSession))
            {
                outcome = ActionExecutionOutcome.Success(
                    action,
                    BuildAirdropSelectionInProgressSystemText(),
                    new ItemAirdropAsyncQueuedData());
                return true;
            }

            AIAction actionSnapshot = new AIAction
            {
                ActionType = action.ActionType,
                Parameters = CloneParameters(action.Parameters),
                Reason = action.Reason
            };

            DialogueRuntimeContext requestContext = runtimeContext.WithCurrentRuntimeMarkers();
            string validateReason = string.Empty;
            bool resolved = DialogueContextResolver.TryResolveLiveContext(
                requestContext,
                out DialogueLiveContext liveContext,
                out string resolveReason);
            bool validated = resolved && DialogueContextValidator.ValidateRequestSend(requestContext, liveContext, out validateReason);
            if (!resolved || !validated)
            {
                string fallbackReason = string.IsNullOrWhiteSpace(validateReason) ? resolveReason : validateReason;
                outcome = ActionExecutionOutcome.Failure(action, fallbackReason ?? "RimChat_DialogueRequestUnavailable".Translate().ToString());
                return true;
            }

            var lease = new DialogueRequestLease(
                requestContext.DialogueSessionId,
                windowInstanceId,
                requestContext.ContextVersion);
            var prepareResult = GameAIInterface.Instance.BeginPrepareItemAirdropTradeAsync(
                currentFaction,
                action.Parameters,
                negotiator,
                completedResult => HandleAirdropAsyncPrepareCompleted(
                    currentSession,
                    currentFaction,
                    lease,
                    requestContext,
                    actionSnapshot,
                    completedResult),
                (requestId, timeoutSeconds) => BindAirdropAsyncRequest(currentSession, lease, requestId, timeoutSeconds));
            if (!prepareResult.Success)
            {
                lease.Dispose();
                string failureMessage = string.IsNullOrWhiteSpace(prepareResult?.Message)
                    ? "RimChat_Unknown".Translate().ToString()
                    : prepareResult.Message;
                outcome = ActionExecutionOutcome.Failure(action, failureMessage);
                return true;
            }

            if (prepareResult.Data is ItemAirdropAsyncQueuedData)
            {
                outcome = ActionExecutionOutcome.Success(
                    action,
                    BuildAirdropSelectionInProgressSystemText(),
                    prepareResult.Data);
                return true;
            }

            if (prepareResult.Data is ItemAirdropPendingSelectionData pendingSelection)
            {
                lease.Dispose();
                CacheAirdropPendingSelectionIntent(currentSession, action, pendingSelection);
                outcome = ActionExecutionOutcome.Success(
                    action,
                    BuildAirdropPendingSelectionSystemText(pendingSelection),
                    pendingSelection);
                return true;
            }

            if (!(prepareResult.Data is ItemAirdropPreparedTradeData preparedTrade))
            {
                lease.Dispose();
                outcome = ActionExecutionOutcome.Failure(action, "RimChat_Unknown".Translate().ToString());
                return true;
            }

            if (currentSession != null)
            {
                currentSession.pendingDelayedActionIntent = null;
                currentSession.lastDelayedActionIntent = null;
            }

            lease.Dispose();
            ShowAirdropTradeConfirmationDialog(currentSession, currentFaction, preparedTrade);
            outcome = ActionExecutionOutcome.Success(
                action,
                "RimChat_ItemAirdropAwaitingConfirmSystem".Translate().ToString(),
                preparedTrade);
            return true;
        }

        private static string BuildAirdropSelectionInProgressSystemText()
        {
            return "RimChat_ItemAirdropSelectionInProgressSystem".Translate().ToString();
        }

        private static void CacheAirdropPendingSelectionIntent(
            FactionDialogueSession currentSession,
            AIAction action,
            ItemAirdropPendingSelectionData pendingSelection)
        {
            if (currentSession == null || action == null || pendingSelection?.Options == null || pendingSelection.Options.Count == 0)
            {
                return;
            }

            Dictionary<string, object> parameters = CloneParameters(action.Parameters);
            parameters.Remove("selected_def");
            parameters[AirdropPendingFailureCodeKey] = pendingSelection.FailureCode ?? "selection_timeout";
            parameters[AirdropPendingCandidatesKey] = pendingSelection.Options
                .Select(option => new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["index"] = option.Index,
                    ["defName"] = option.DefName ?? string.Empty,
                    ["label"] = option.Label ?? option.DefName ?? string.Empty,
                    ["unitPrice"] = option.UnitPrice,
                    ["max_legal_count"] = option.MaxLegalCount
                })
                .Cast<object>()
                .ToList();

            var pendingAction = new AIAction
            {
                ActionType = AIActionNames.RequestItemAirdrop,
                Parameters = parameters,
                Reason = "selection_timeout_pending_confirmation"
            };

            int assistantRound = GetAssistantDialogueRound(currentSession) + 1;
            PendingDelayedActionIntent intent = CreatePendingDelayedIntent(
                pendingAction,
                assistantRound,
                true,
                "selected_def");
            if (intent == null)
            {
                return;
            }

            currentSession.pendingDelayedActionIntent = intent;
            currentSession.lastDelayedActionIntent = intent.Clone();
        }

        private static string BuildAirdropPendingSelectionSystemText(ItemAirdropPendingSelectionData pendingSelection)
        {
            if (pendingSelection?.Options == null || pendingSelection.Options.Count == 0)
            {
                return "RimChat_ItemAirdropAwaitingConfirmSystem".Translate().ToString();
            }

            string lines = string.Join(
                "\n",
                pendingSelection.Options
                    .OrderBy(option => option.Index)
                    .Select(option => "RimChat_ItemAirdropSelectionPendingLine".Translate(
                        option.Index,
                        option.Label ?? option.DefName ?? "RimChat_Unknown".Translate().ToString(),
                        option.DefName ?? "RimChat_Unknown".Translate().ToString(),
                        option.UnitPrice.ToString("F1", CultureInfo.InvariantCulture),
                        option.MaxLegalCount).ToString()));
            return "RimChat_ItemAirdropSelectionPendingSystem".Translate(lines).ToString();
        }

        private void ShowAirdropTradeConfirmationDialog(
            FactionDialogueSession currentSession,
            Faction currentFaction,
            ItemAirdropPreparedTradeData preparedTrade)
        {
            string body = BuildAirdropTradeConfirmationBody(currentFaction, preparedTrade);
            var dialog = new Dialog_MessageBox(
                body,
                "RimChat_ItemAirdropConfirmAccept".Translate(),
                () => CommitConfirmedAirdropTrade(currentSession, currentFaction, preparedTrade),
                "RimChat_ItemAirdropConfirmCancel".Translate(),
                () => CancelConfirmedAirdropTrade(currentSession, currentFaction),
                "RimChat_ItemAirdropConfirmTitle".Translate(),
                false,
                null,
                null,
                WindowLayer.Dialog);
            Find.WindowStack.Add(dialog);
        }

        private static string BuildAirdropTradeConfirmationBody(Faction faction, ItemAirdropPreparedTradeData preparedTrade)
        {
            if (preparedTrade == null)
            {
                return "RimChat_Unknown".Translate().ToString();
            }

            string deliveryLabel = string.IsNullOrWhiteSpace(preparedTrade.ResolvedLabel)
                ? preparedTrade.SelectedDefName
                : preparedTrade.ResolvedLabel;
            string deliverySummary = "RimChat_ItemAirdropConfirmDeliveryLine".Translate(deliveryLabel, preparedTrade.Quantity).ToString();

            List<string> paymentLines = new List<string>();
            if (preparedTrade.PaymentLines != null)
            {
                paymentLines.AddRange(preparedTrade.PaymentLines.Select(line =>
                {
                    string lineLabel = string.IsNullOrWhiteSpace(line?.Label) ? (line?.DefName ?? "unknown") : line.Label;
                    int subtotal = Mathf.RoundToInt(line?.SubtotalMarketValue ?? 0f);
                    int count = Math.Max(0, line?.Count ?? 0);
                    return "RimChat_ItemAirdropConfirmPaymentLine".Translate(lineLabel, count, subtotal).ToString();
                }));
            }

            if (paymentLines.Count == 0)
            {
                paymentLines.Add("RimChat_Unknown".Translate().ToString());
            }

            string paymentSummary = string.Join("\n", paymentLines);
            string factionName = faction?.Name ?? "RimChat_Unknown".Translate().ToString();
            return "RimChat_ItemAirdropConfirmBody".Translate(
                factionName,
                deliverySummary,
                paymentSummary,
                preparedTrade.BudgetSilver,
                preparedTrade.PaymentTotalSilver,
                preparedTrade.PaymentOverpaySilver).ToString();
        }

        private void CommitConfirmedAirdropTrade(
            FactionDialogueSession currentSession,
            Faction currentFaction,
            ItemAirdropPreparedTradeData preparedTrade)
        {
            var commitResult = GameAIInterface.Instance.CommitPreparedItemAirdropTrade(currentFaction, preparedTrade);
            if (commitResult.Success)
            {
                var payload = commitResult.Data as ItemAirdropResultData;
                string text = payload != null
                    ? BuildAirdropSuccessSystemMessage(payload)
                    : "RimChat_ItemAirdropCommitSuccessSystem".Translate().ToString();
                currentSession?.AddMessage("System", text, false, DialogueMessageType.System);
            }
            else
            {
                string reason = string.IsNullOrWhiteSpace(commitResult?.Message)
                    ? "RimChat_Unknown".Translate().ToString()
                    : commitResult.Message;
                currentSession?.AddMessage(
                    "System",
                    "RimChat_ItemAirdropCommitFailedSystem".Translate(reason),
                    false,
                    DialogueMessageType.System);
            }

            SaveFactionMemory(currentSession, currentFaction);
        }

        private void CancelConfirmedAirdropTrade(FactionDialogueSession currentSession, Faction currentFaction)
        {
            currentSession?.AddMessage(
                "System",
                "RimChat_ItemAirdropCancelledSystem".Translate(),
                false,
                DialogueMessageType.System);

            SaveFactionMemory(currentSession, currentFaction);
        }
    }
}
