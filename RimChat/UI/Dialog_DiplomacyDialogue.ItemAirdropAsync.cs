using System;
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
    /// Dependencies: Dialogue runtime context guards, GameAIInterface async airdrop API, session runtime state.
    /// Responsibility: track async airdrop request lifecycle and apply async completion safely.
    /// </summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private static bool IsAirdropAsyncRequestPending(FactionDialogueSession currentSession)
        {
            return currentSession != null &&
                   currentSession.isWaitingForAirdropSelection &&
                   !string.IsNullOrWhiteSpace(currentSession.pendingAirdropRequestId);
        }

        private static void BindAirdropAsyncRequest(
            FactionDialogueSession currentSession,
            DialogueRequestLease lease,
            string requestId,
            int timeoutSeconds)
        {
            if (currentSession == null || lease == null || string.IsNullOrWhiteSpace(requestId))
            {
                return;
            }

            lease.BindRequestId(requestId);
            currentSession.airdropRequestGeneration++;
            int requestGeneration = currentSession.airdropRequestGeneration;
            currentSession.pendingAirdropRequestId = requestId;
            currentSession.pendingAirdropRequestLease = lease;
            currentSession.isWaitingForAirdropSelection = true;
            currentSession.pendingAirdropRequestStartedRealtime = Time.realtimeSinceStartup;
            currentSession.pendingAirdropRequestTimeoutSeconds = Mathf.Max(0, timeoutSeconds);
            TransitionAirdropExecutionStage(currentSession, AirdropExecutionStage.SelectingCandidate, $"requestId={requestId},timeout={timeoutSeconds},generation={requestGeneration}");
        }

        private static void ClearAirdropAsyncRequestState(FactionDialogueSession currentSession, bool disposeLease)
        {
            if (currentSession == null)
            {
                return;
            }

            if (disposeLease)
            {
                currentSession.pendingAirdropRequestLease?.Dispose();
            }

            currentSession.pendingAirdropRequestId = null;
            currentSession.pendingAirdropRequestLease = null;
            currentSession.isWaitingForAirdropSelection = false;
            currentSession.pendingAirdropRequestStartedRealtime = -1f;
            currentSession.pendingAirdropRequestTimeoutSeconds = 0;
        }

        private void HandleAirdropAsyncPrepareCompleted(
            FactionDialogueSession currentSession,
            Faction currentFaction,
            DialogueRequestLease lease,
            DialogueRuntimeContext requestContext,
            AIAction sourceAction,
            int expectedGeneration,
            GameAIInterface.APIResult prepareResult)
        {
            if (!IsAirdropAsyncContextValid(currentSession, currentFaction, lease, requestContext, expectedGeneration))
            {
                Log.Warning($"[RimChat] AirdropStalePendingBlocked: requestId={lease?.RequestId ?? "none"},stage={currentSession?.airdropExecutionStage.ToString() ?? "null"},expectedGeneration={expectedGeneration},actualGeneration={currentSession?.airdropRequestGeneration ?? -1},faction={currentFaction?.Name ?? "null"}");
                return;
            }

            ClearAirdropAsyncRequestState(currentSession, true);

            if (prepareResult == null)
            {
                ResetAirdropConfirmationRuntime(currentSession, "prepareResult=null", true, true);
                TransitionAirdropExecutionStage(currentSession, AirdropExecutionStage.Failed, "prepareResult=null");
                currentSession.AddMessage(
                    "System",
                    "RimChat_ItemAirdropCommitFailedSystem".Translate("RimChat_Unknown".Translate().ToString()),
                    false,
                    DialogueMessageType.System);
                SaveFactionMemory(currentSession, currentFaction);
                return;
            }

            if (!prepareResult.Success)
            {
                string reason = string.IsNullOrWhiteSpace(prepareResult.Message)
                    ? "RimChat_Unknown".Translate().ToString()
                    : prepareResult.Message;
                ResetAirdropConfirmationRuntime(currentSession, "async_prepare_failed", true);
                TransitionAirdropExecutionStage(currentSession, AirdropExecutionStage.Failed, reason);
                currentSession.AddMessage(
                    "System",
                    "RimChat_ItemAirdropCommitFailedSystem".Translate(reason),
                    false,
                    DialogueMessageType.System);
                SaveFactionMemory(currentSession, currentFaction);
                return;
            }

            if (prepareResult.Data is ItemAirdropPendingSelectionData pendingSelection)
            {
                TransitionAirdropExecutionStage(currentSession, AirdropExecutionStage.SelectingCandidate, pendingSelection.FailureCode ?? "selection_pending");
                if (DeterminePendingSelectionResolution(pendingSelection) == AirdropPendingResolution.AutoPickTop1 &&
                    TryAutoPickPendingAirdropSelection(sourceAction, pendingSelection, currentSession, currentFaction, out _))
                {
                    SaveFactionMemory(currentSession, currentFaction);
                }
                else
                {
                    CacheAirdropPendingSelectionIntent(currentSession, sourceAction, pendingSelection);
                    currentSession.AddMessage(
                        "System",
                        "RimChat_ItemAirdropCommitFailedSystem".Translate(BuildAirdropPendingSelectionSystemText(pendingSelection)),
                        false,
                        DialogueMessageType.System);
                    SaveFactionMemory(currentSession, currentFaction);
                }
                return;
            }

            if (prepareResult.Data is ItemAirdropPreparedTradeData preparedTrade)
            {
                ClearPendingAirdropDialogState("async_prepare_new_confirmation", false);
                currentSession?.ClearPendingAirdropExecutionState();
                ResetAirdropConfirmationRuntime(currentSession, "async_prepared_trade_ready", true, true);
                TransitionAirdropExecutionStage(currentSession, AirdropExecutionStage.PreparedAwaitingConfirm, preparedTrade.SelectedDefName ?? "prepared_trade");
                ShowAirdropTradeConfirmationDialog(currentSession, currentFaction, preparedTrade, null, null);
                SaveFactionMemory(currentSession, currentFaction);
            }
        }

        private static bool IsAirdropAsyncContextValid(
            FactionDialogueSession currentSession,
            Faction currentFaction,
            DialogueRequestLease lease,
            DialogueRuntimeContext requestContext,
            int expectedGeneration)
        {
            if (currentSession == null || currentFaction == null || currentFaction.defeated || lease == null)
            {
                return false;
            }

            if (expectedGeneration != currentSession.airdropRequestGeneration)
            {
                return false;
            }

            string requestId = lease.RequestId;
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return false;
            }

            if (!string.Equals(currentSession.pendingAirdropRequestId, requestId, StringComparison.Ordinal))
            {
                return false;
            }

            if (currentSession.pendingAirdropRequestLease == null ||
                !ReferenceEquals(currentSession.pendingAirdropRequestLease, lease))
            {
                return false;
            }

            if (!lease.IsValidFor(requestId, requestContext?.DialogueSessionId ?? string.Empty, requestContext?.ContextVersion ?? -1))
            {
                return false;
            }

            DialogueRuntimeContext resolveContext = requestContext?.WithCurrentRuntimeMarkers();
            if (!DialogueContextResolver.TryResolveLiveContext(resolveContext, out DialogueLiveContext liveContext, out _))
            {
                return false;
            }

            if (!DialogueContextValidator.ValidateCallbackApply(requestContext, liveContext, requestContext?.DialogueSessionId, out _))
            {
                return false;
            }

            FactionDialogueSession liveSession = GameComponent_DiplomacyManager.Instance?.GetSession(currentFaction);
            return ReferenceEquals(liveSession, currentSession);
        }

        private void CancelPendingAirdropSelectionRequest()
        {
            if (session == null || string.IsNullOrWhiteSpace(session.pendingAirdropRequestId))
            {
                return;
            }

            GameAIInterface.Instance.CancelItemAirdropAsyncRequest(
                session.pendingAirdropRequestId,
                "airdrop_selection_cancelled_by_window_close",
                "Airdrop selection request cancelled by dialogue close.");
            ClearAirdropAsyncRequestState(session, true);
        }

        private bool TryGetPendingAirdropRequestStatus(out AIRequestResult status)
        {
            status = null;
            if (session == null || string.IsNullOrWhiteSpace(session.pendingAirdropRequestId))
            {
                return false;
            }

            status = AIChatServiceAsync.Instance.GetRequestStatus(session.pendingAirdropRequestId);
            return status != null;
        }

        private bool TryBuildAirdropAsyncStatusText(out string statusText)
        {
            statusText = string.Empty;
            if (!IsAirdropAsyncRequestPending(session))
            {
                return false;
            }

            if (TryGetPendingAirdropRequestStatus(out AIRequestResult status) && IsQueuedRequestState(status))
            {
                statusText = "RimChat_DiplomacyRequestQueued".Translate(GetQueuedRequestsAhead(status)).ToString();
                return true;
            }

            statusText = "RimChat_ItemAirdropSelectionInProgressBar".Translate().ToString();
            return true;
        }
    }
}
