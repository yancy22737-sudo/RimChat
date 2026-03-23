using System;
using RimChat.AI;
using RimChat.Dialogue;
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
                return "RimChat_DiplomacyRequestQueued".Translate(GetQueuedRequestsAhead(status)).ToString();
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

            ShowDialogueRequestError("RimChat_DialogueRequestUnavailable".Translate().ToString());
        }
    }
}
