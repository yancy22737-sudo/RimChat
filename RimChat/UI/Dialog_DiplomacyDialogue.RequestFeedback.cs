using System;
using System.Collections.Generic;
using RimChat.AI;
using RimChat.Dialogue;
using RimChat.DiplomacySystem;
using RimChat.Memory;
using RimWorld;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: AIChatServiceAsync request status snapshots, DialogueDropPolicy, and diplomacy session runtime state.
    /// Responsibility: translate shared request lifecycle into player-facing diplomacy feedback without leaking stale-callback noise.
    /// </summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private bool TryGetVisibleAiRequestStatus(out AIRequestResult status)
        {
            status = null;
            string requestId = session?.pendingRequestId;
            if (!string.IsNullOrWhiteSpace(requestId))
            {
                status = AIChatServiceAsync.Instance.GetRequestStatus(requestId);
                if (status != null)
                {
                    return true;
                }
            }

            if (session != null && !string.IsNullOrWhiteSpace(session.pendingAirdropRequestId))
            {
                status = AIChatServiceAsync.Instance.GetRequestStatus(session.pendingAirdropRequestId);
                if (status != null)
                {
                    return true;
                }
            }

            if (string.IsNullOrWhiteSpace(strategySuggestionRequestId))
            {
                return false;
            }

            status = AIChatServiceAsync.Instance.GetRequestStatus(strategySuggestionRequestId);
            return status != null;
        }

        private static bool IsQueuedRequestState(AIRequestResult status)
        {
            return status != null &&
                   (status.State == AIRequestState.Pending || status.State == AIRequestState.Queued);
        }

        private static int GetQueuedRequestsAhead(AIRequestResult status)
        {
            return Math.Max(0, (status?.QueuePosition ?? 0) - 1);
        }

        private string BuildAiTurnStatusText()
        {
            if (TryGetVisibleAiRequestStatus(out AIRequestResult status) && IsQueuedRequestState(status))
            {
                int requestsAhead = GetQueuedRequestsAhead(status);
                if (requestsAhead == 0)
                {
                    return "RimChat_DiplomacyRequestQueuedHead".Translate().ToString();
                }
                return "RimChat_DiplomacyRequestQueued".Translate(requestsAhead).ToString();
            }

            return "RimChat_DiplomacyInputLockedByTyping".Translate().ToString();
        }

        private void ShowDialogueRequestError(string error)
        {
            string resolved = string.IsNullOrWhiteSpace(error)
                ? "RimChat_DialogueRequestUnavailable".Translate().ToString()
                : error;

            if (session != null)
            {
                conversationController.CancelPendingRequest(session);
                session.aiError = resolved;
                session.isWaitingForResponse = false;
            }

            Messages.Message(resolved, MessageTypeDefOf.RejectInput, false);
        }

        private void HandleDroppedRequest(string primaryReason, string secondaryReason = null)
        {
            string reason = !string.IsNullOrWhiteSpace(primaryReason) ? primaryReason : secondaryReason;
            if (DialogueDropPolicy.ShouldSuppressUserFacingDrop(reason))
            {
                Log.Message($"[RimChat] Suppressed user-facing dropped diplomacy callback: reason={reason ?? "unknown"}");
                return;
            }

            string resolved = BuildDroppedRequestMessage(reason);
            LogDroppedRequestState(reason);
            ShowDialogueRequestError(resolved);
            session?.AddMessage("System", resolved, false, DialogueMessageType.System);
        }

        private string BuildDroppedRequestMessage(string reason)
        {
            string baseMessage = "RimChat_DialogueRequestUnavailable".Translate().ToString();
            if (string.IsNullOrWhiteSpace(reason))
            {
                return baseMessage;
            }

            return $"{baseMessage} [{reason.Trim()}]";
        }

        private void LogDroppedRequestState(string reason)
        {
            Log.Warning(
                $"[RimChat] Diplomacy request dropped. " +
                $"reason={reason ?? "unknown"}, faction={faction?.Name ?? "null"}, negotiator={negotiator?.ThingID ?? "null"}, " +
                $"pendingRequestId={session?.pendingRequestId ?? "null"}, waiting={session?.isWaitingForResponse ?? false}, " +
                $"hasLease={session?.pendingRequestLease != null}, queuedTick={session?.lastDiplomacyRequestQueuedTick ?? int.MinValue}, " +
                $"queuedRealtime={session?.lastDiplomacyRequestQueuedRealtime ?? -1f}, window={windowInstanceId}");
        }

        private void HandleSessionRequestError(FactionDialogueSession targetSession, string error)
        {
            if (targetSession == null) return;

            string resolved = string.IsNullOrWhiteSpace(error)
                ? "RimChat_DialogueRequestUnavailable".Translate().ToString()
                : error;

            conversationController.CancelPendingRequest(targetSession);
            targetSession.aiError = resolved;
            targetSession.isWaitingForResponse = false;

            if (ReferenceEquals(session, targetSession))
            {
                Messages.Message(resolved, MessageTypeDefOf.RejectInput, false);
            }
        }

        private void HandleSessionDroppedRequest(
            FactionDialogueSession targetSession,
            Faction targetFaction,
            string primaryReason,
            string secondaryReason = null)
        {
            string reason = !string.IsNullOrWhiteSpace(primaryReason) ? primaryReason : secondaryReason;
            if (DialogueDropPolicy.ShouldSuppressUserFacingDrop(reason))
            {
                Log.Message($"[RimChat] Suppressed user-facing dropped diplomacy callback: reason={reason ?? "unknown"}");
                return;
            }

            string resolved = BuildDroppedRequestMessage(reason);
            Log.Warning(
                $"[RimChat] Diplomacy request dropped (background). " +
                $"reason={reason ?? "unknown"}, faction={targetFaction?.Name ?? "null"}, " +
                $"pendingRequestId={targetSession?.pendingRequestId ?? "null"}, waiting={targetSession?.isWaitingForResponse ?? false}, " +
                $"hasLease={targetSession?.pendingRequestLease != null}, window={windowInstanceId}");
            HandleSessionRequestError(targetSession, resolved);
            targetSession?.AddMessage("System", resolved, false, DialogueMessageType.System);
        }

        private void CancelAllBackgroundDialogueRequests()
        {
            var allSessions = GameComponent_DiplomacyManager.Instance?.GetAllDialogueSessions();
            if (allSessions == null) return;

            foreach (var s in allSessions)
            {
                if (s != null && !string.IsNullOrEmpty(s.pendingRequestId))
                {
                    conversationController.CancelPendingRequest(s);
                }
            }
        }
    }
}
