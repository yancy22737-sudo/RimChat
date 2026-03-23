using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.AI;
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

            var prepareResult = GameAIInterface.Instance.PrepareItemAirdropTrade(currentFaction, action.Parameters, negotiator);
            if (!prepareResult.Success)
            {
                string failureMessage = string.IsNullOrWhiteSpace(prepareResult?.Message)
                    ? "RimChat_Unknown".Translate().ToString()
                    : prepareResult.Message;
                outcome = ActionExecutionOutcome.Failure(action, failureMessage);
                return true;
            }

            if (!(prepareResult.Data is ItemAirdropPreparedTradeData preparedTrade))
            {
                outcome = ActionExecutionOutcome.Failure(action, "RimChat_Unknown".Translate().ToString());
                return true;
            }

            if (currentSession != null)
            {
                currentSession.pendingDelayedActionIntent = null;
                currentSession.lastDelayedActionIntent = null;
            }

            ShowAirdropTradeConfirmationDialog(currentSession, currentFaction, preparedTrade);
            outcome = ActionExecutionOutcome.Success(
                action,
                "RimChat_ItemAirdropAwaitingConfirmSystem".Translate().ToString(),
                preparedTrade);
            return true;
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
