using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>/// Dependencies: SendGateState, FactionDialogueSession, Unity realtime clock.
 /// Responsibility: own diplomacy input-host lifecycle so IME only reattaches after the full AI pipeline settles.
 ///</summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private const float InputHostReactivationStabilizationSeconds = 0.25f;
        private float inputHostBlockedUntilRealtime = -1f;

        private bool ShouldRenderInputAsReadOnly(SendGateState sendGate)
        {
            return sendGate.IsHardBlocked || sendGate.IsSoftBlocked;
        }

        private bool IsAiTurnInputHostOwned()
        {
            if (session == null)
            {
                return false;
            }

            return session.isWaitingForResponse ||
                   session.HasPendingImageRequests() ||
                   HasActiveNpcTypewriter() ||
                   strategySuggestionRequestPending;
        }

        private void RefreshInputHostReactivationBarrier(bool aiTurnOwnsInputHost)
        {
            if (!aiTurnOwnsInputHost)
            {
                return;
            }

            inputHostBlockedUntilRealtime = Time.realtimeSinceStartup + InputHostReactivationStabilizationSeconds;
        }

        private bool IsInputHostReactivationStabilizing()
        {
            return inputHostBlockedUntilRealtime > 0f &&
                   Time.realtimeSinceStartup < inputHostBlockedUntilRealtime;
        }

        private string BuildAiTurnInputLockReason()
        {
            return BuildAiTurnStatusText();
        }
    }
}
