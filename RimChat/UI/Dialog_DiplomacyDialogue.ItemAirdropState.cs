using RimChat.Memory;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: diplomacy dialogue session runtime state.
    /// Responsibility: centralize airdrop confirmation state transitions and stale-pending cleanup.
    /// </summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private static void TransitionAirdropExecutionStage(
            FactionDialogueSession currentSession,
            AirdropExecutionStage nextStage,
            string reason)
        {
            if (currentSession == null)
            {
                return;
            }

            AirdropExecutionStage previousStage = currentSession.airdropExecutionStage;
            currentSession.airdropExecutionStage = nextStage;
            Log.Message($"[RimChat] AirdropStateTransition: {previousStage} -> {nextStage} reason={reason ?? "none"}");
        }

        private static void ResetAirdropConfirmationRuntime(
            FactionDialogueSession currentSession,
            string reason,
            bool disposeLease,
            bool clearTradeCardReference = false,
            bool resetStageToIdle = false)
        {
            if (currentSession == null)
            {
                return;
            }

            bool clearedPendingIntent = currentSession.ClearPendingAirdropSelectionIntentState();
            bool hadAsyncState =
                currentSession.isWaitingForAirdropSelection ||
                !string.IsNullOrWhiteSpace(currentSession.pendingAirdropRequestId) ||
                currentSession.pendingAirdropRequestLease != null;
            if (hadAsyncState)
            {
                ClearAirdropAsyncRequestState(currentSession, disposeLease);
            }

            if (clearTradeCardReference && currentSession.hasPendingAirdropTradeCardReference)
            {
                currentSession.ClearPendingAirdropTradeCardReference();
            }

            if (resetStageToIdle)
            {
                currentSession.airdropExecutionStage = AirdropExecutionStage.Idle;
            }

            if (clearedPendingIntent || hadAsyncState || clearTradeCardReference || resetStageToIdle)
            {
                currentSession.airdropRequestGeneration++;
                Log.Message(
                    $"[RimChat] AirdropPendingIntentInvalidated: reason={reason ?? "none"},clearedPendingIntent={clearedPendingIntent},clearedAsyncState={hadAsyncState},clearedTradeCard={clearTradeCardReference},resetStageToIdle={resetStageToIdle},generation={currentSession.airdropRequestGeneration}");
            }
        }

        private static bool HasStalePendingAirdropSelection(
            FactionDialogueSession currentSession,
            out string details)
        {
            details = string.Empty;
            if (currentSession == null)
            {
                return false;
            }

            bool hasPendingIntent = currentSession.HasPendingAirdropSelectionIntent();
            bool hasAsyncState =
                currentSession.isWaitingForAirdropSelection ||
                !string.IsNullOrWhiteSpace(currentSession.pendingAirdropRequestId) ||
                currentSession.pendingAirdropRequestLease != null;
            if (!hasPendingIntent && !hasAsyncState)
            {
                return false;
            }

            details =
                $"stage={currentSession.airdropExecutionStage},hasPendingIntent={hasPendingIntent},isWaitingForSelection={currentSession.isWaitingForAirdropSelection}," +
                $"requestId={currentSession.pendingAirdropRequestId ?? "none"},hasLease={(currentSession.pendingAirdropRequestLease != null)}";
            return true;
        }
    }
}
