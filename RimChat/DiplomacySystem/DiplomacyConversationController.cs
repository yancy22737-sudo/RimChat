using System;
using System.Collections.Generic;
using RimChat.AI;
using RimChat.Memory;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>/// Dependencies: AIChatServiceAsync, GameComponent_DiplomacyManager, FactionDialogueSession.
 /// Responsibility: own diplomacy request lifecycle, context validation, and cancellation.
 ///</summary>
    public class DiplomacyConversationController
    {
        private const int RequestDebounceTicks = 120;

        public bool TrySendDialogueRequest(
            FactionDialogueSession session,
            Faction faction,
            List<ChatMessageData> messages,
            Action<string> onSuccess,
            Action<string> onError,
            Action<float> onProgress)
        {
            if (!CanStartRequest(session, faction, messages))
            {
                return false;
            }

            CancelSupersededPendingRequest(session);
            session.isWaitingForResponse = true;
            session.aiRequestProgress = 0f;
            session.aiError = null;

            string requestId = string.Empty;
            requestId = AIChatServiceAsync.Instance.SendChatRequestAsync(
                messages,
                onSuccess: response => HandleSuccess(session, faction, requestId, response, onSuccess),
                onError: error => HandleError(session, faction, requestId, error, onError),
                onProgress: progress => HandleProgress(session, faction, requestId, progress, onProgress),
                usageChannel: DialogueUsageChannel.Diplomacy,
                debugSource: AIRequestDebugSource.DiplomacyDialogue);

            if (string.IsNullOrEmpty(requestId))
            {
                session.isWaitingForResponse = false;
                session.aiError = "Failed to queue AI request";
                return false;
            }

            session.pendingRequestId = requestId;
            session.lastDiplomacyRequestQueuedTick = GetCurrentTick();
            return true;
        }

        public bool IsRequestDebounced(FactionDialogueSession session)
        {
            if (session == null)
            {
                return false;
            }

            int lastQueuedTick = session.lastDiplomacyRequestQueuedTick;
            if (lastQueuedTick == int.MinValue)
            {
                return false;
            }

            int tickDelta = GetCurrentTick() - lastQueuedTick;
            return tickDelta >= 0 && tickDelta < RequestDebounceTicks;
        }

        public void CancelPendingRequest(FactionDialogueSession session)
        {
            if (session == null || string.IsNullOrEmpty(session.pendingRequestId))
            {
                return;
            }

            string requestId = session.pendingRequestId;
            AIChatServiceAsync.Instance.CancelRequest(requestId);
            session.pendingRequestId = null;
            session.isWaitingForResponse = false;
            session.aiRequestProgress = 0f;
        }

        private static bool CanStartRequest(FactionDialogueSession session, Faction faction, List<ChatMessageData> messages)
        {
            if (session == null || faction == null || faction.defeated)
            {
                return false;
            }

            if (messages == null || messages.Count == 0)
            {
                return false;
            }

            int lastQueuedTick = session.lastDiplomacyRequestQueuedTick;
            if (lastQueuedTick == int.MinValue)
            {
                return true;
            }

            int tickDelta = GetCurrentTick() - lastQueuedTick;
            return tickDelta >= RequestDebounceTicks || tickDelta < 0;
        }

        private static int GetCurrentTick()
        {
            return Find.TickManager?.TicksGame ?? 0;
        }

        private static void CancelSupersededPendingRequest(FactionDialogueSession session)
        {
            if (session == null || string.IsNullOrEmpty(session.pendingRequestId))
            {
                return;
            }

            string supersededRequestId = session.pendingRequestId;
            AIChatServiceAsync.Instance.CancelRequest(supersededRequestId);
            session.pendingRequestId = null;
            session.isWaitingForResponse = false;
            session.aiRequestProgress = 0f;
        }

        private static void HandleSuccess(
            FactionDialogueSession session,
            Faction faction,
            string requestId,
            string response,
            Action<string> onSuccess)
        {
            if (!IsRequestContextStillValid(session, faction, requestId))
            {
                return;
            }

            session.pendingRequestId = null;
            session.isWaitingForResponse = false;
            session.aiRequestProgress = 1f;
            onSuccess?.Invoke(response);
        }

        private static void HandleError(
            FactionDialogueSession session,
            Faction faction,
            string requestId,
            string error,
            Action<string> onError)
        {
            if (!IsRequestContextStillValid(session, faction, requestId))
            {
                return;
            }

            session.pendingRequestId = null;
            session.isWaitingForResponse = false;
            session.aiError = error;
            onError?.Invoke(error);
        }

        private static void HandleProgress(
            FactionDialogueSession session,
            Faction faction,
            string requestId,
            float progress,
            Action<float> onProgress)
        {
            if (!IsRequestContextStillValid(session, faction, requestId))
            {
                return;
            }

            session.aiRequestProgress = progress;
            onProgress?.Invoke(progress);
        }

        private static bool IsRequestContextStillValid(FactionDialogueSession session, Faction faction, string requestId)
        {
            if (session == null || faction == null || faction.defeated || string.IsNullOrEmpty(requestId))
            {
                return false;
            }

            if (!string.Equals(session.pendingRequestId, requestId, StringComparison.Ordinal))
            {
                return false;
            }

            FactionDialogueSession liveSession = GameComponent_DiplomacyManager.Instance?.GetSession(faction);
            return ReferenceEquals(liveSession, session);
        }
    }
}
