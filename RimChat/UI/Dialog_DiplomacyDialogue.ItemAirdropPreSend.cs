using System;
using System.Collections.Generic;
using RimChat.AI;
using RimChat.Memory;
using RimWorld;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: pending airdrop selection runtime intent, airdrop confirmation pipeline.
    /// Responsibility: resolve explicit pending-candidate replies before sending a new AI request.
    /// </summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private bool TryHandlePendingAirdropSelectionBeforeAi(
            string playerMessage,
            FactionDialogueSession currentSession,
            Faction currentFaction)
        {
            if (currentSession?.pendingDelayedActionIntent == null || currentFaction == null)
            {
                return false;
            }

            PendingDelayedActionIntent pendingIntent = currentSession.pendingDelayedActionIntent;
            if (!string.Equals(pendingIntent.ActionType, AIActionNames.RequestItemAirdrop, StringComparison.Ordinal))
            {
                return false;
            }

            if (!TryReadPendingAirdropCandidates(pendingIntent.Parameters, out List<PendingAirdropSelectionCandidate> candidates) ||
                candidates.Count == 0)
            {
                return false;
            }

            if (!TryResolvePendingAirdropCandidate(playerMessage, candidates, out PendingAirdropSelectionCandidate selected))
            {
                return false;
            }

            Dictionary<string, object> mappedParameters = CloneParameters(pendingIntent.Parameters);
            mappedParameters.Remove(AirdropPendingCandidatesKey);
            mappedParameters.Remove(AirdropPendingFailureCodeKey);
            mappedParameters["selected_def"] = selected.DefName;
            if (TryExtractAirdropRequestedCount(playerMessage, out int requestedCount))
            {
                mappedParameters["count"] = requestedCount;
            }

            currentSession.ClearPendingAirdropTradeCardReference();

            var mappedAction = new AIAction
            {
                ActionType = AIActionNames.RequestItemAirdrop,
                Parameters = mappedParameters,
                Reason = "intent_map_pending_selection_pre_send"
            };

            if (!TryHandleAirdropActionWithConfirmation(mappedAction, currentSession, currentFaction, out ActionExecutionOutcome outcome))
            {
                return false;
            }

            string countHint = mappedParameters.TryGetValue("count", out object countRaw) ? countRaw?.ToString() ?? "none" : "none";
            Log.Message($"[RimChat] Pre-send pending airdrop selection resolved locally: def={selected.DefName},index={selected.Index},label={selected.Label},countHint={countHint}");
            currentSession.AddMessage(
                "System",
                "RimChat_ItemAirdropSelectionChosen".Translate(selected.Label, selected.DefName).ToString(),
                false,
                DialogueMessageType.System);

            RecordDelayedActionRuntimeState(new List<ActionExecutionOutcome> { outcome }, currentSession);
            if (outcome != null && outcome.IsSuccess)
            {
                AppendAirdropSuccessSystemMessage(outcome, currentSession, currentFaction);
            }
            else
            {
                string reason = string.IsNullOrWhiteSpace(outcome?.Message)
                    ? "RimChat_Unknown".Translate().ToString()
                    : outcome.Message;
                currentSession.AddMessage(
                    "System",
                    "RimChat_ItemAirdropCommitFailedSystem".Translate(reason),
                    false,
                    DialogueMessageType.System);
            }

            SaveFactionMemory(currentSession, currentFaction);
            return true;
        }
    }
}
