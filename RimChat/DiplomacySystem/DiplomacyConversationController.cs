using System;
using System.Collections.Generic;
using RimChat.AI;
using RimChat.Dialogue;
using RimChat.Memory;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>/// Dependencies: AIChatServiceAsync, GameComponent_DiplomacyManager, FactionDialogueSession.
 /// Responsibility: own diplomacy request lifecycle, context validation, and cancellation.
 ///</summary>
    public class DiplomacyConversationController
    {
        private const int RequestDebounceTicks = 120;
        private const float RequestDebounceSeconds = 2f;

        public bool TrySendDialogueRequest(
            FactionDialogueSession session,
            Faction faction,
            List<ChatMessageData> messages,
            DialogueRuntimeContext runtimeContext,
            string ownerWindowId,
            Action<DialogueResponseEnvelope> onSuccess,
            Action<string> onError,
            Action<float> onProgress,
            Action<string> onDropped)
        {
            if (!CanStartRequest(session, faction, messages, runtimeContext))
            {
                return false;
            }

            CancelSupersededPendingRequest(session);
            session.isWaitingForResponse = true;
            session.aiRequestProgress = 0f;
            session.aiError = null;

            DialogueRuntimeContext requestContext = runtimeContext.WithCurrentRuntimeMarkers();
            DialogueRequestLease lease = new DialogueRequestLease(
                requestContext.DialogueSessionId,
                ownerWindowId,
                requestContext.ContextVersion);

            string requestId = AIChatServiceAsync.Instance.SendChatRequestAsync(
                messages,
                onSuccess: response => HandleSuccess(session, faction, lease, requestContext, response, onSuccess, onDropped),
                onError: error => HandleError(session, faction, lease, requestContext, error, onError, onDropped),
                onProgress: progress => HandleProgress(session, faction, lease, requestContext, progress, onProgress),
                usageChannel: DialogueUsageChannel.Diplomacy,
                debugSource: AIRequestDebugSource.DiplomacyDialogue);

            if (string.IsNullOrEmpty(requestId))
            {
                session.isWaitingForResponse = false;
                session.aiError = "Failed to queue AI request";
                return false;
            }

            lease.BindRequestId(requestId);
            session.pendingRequestId = requestId;
            session.pendingRequestLease = lease;
            session.lastDiplomacyRequestQueuedTick = GetCurrentTick();
            session.lastDiplomacyRequestQueuedRealtime = Time.realtimeSinceStartup;
            return true;
        }

        public bool IsRequestDebounced(FactionDialogueSession session)
        {
            if (session == null)
            {
                return false;
            }

            return IsWithinDebounceWindow(session);
        }

        public void CancelPendingRequest(FactionDialogueSession session)
        {
            if (session == null || string.IsNullOrEmpty(session.pendingRequestId))
            {
                return;
            }

            string requestId = session.pendingRequestId;
            AIChatServiceAsync.Instance.CancelRequest(
                requestId,
                "dialogue_window_closed",
                "Request cancelled by dialogue close");
            session.pendingRequestId = null;
            session.pendingRequestLease?.Dispose();
            session.pendingRequestLease = null;
            session.isWaitingForResponse = false;
            session.aiRequestProgress = 0f;
        }

        public void CloseLease(FactionDialogueSession session)
        {
            if (session == null)
            {
                return;
            }

            session.pendingRequestLease?.MarkClosing();
            CancelPendingRequest(session);
        }

        private static bool CanStartRequest(
            FactionDialogueSession session,
            Faction faction,
            List<ChatMessageData> messages,
            DialogueRuntimeContext runtimeContext)
        {
            if (session == null || faction == null || faction.defeated)
            {
                return false;
            }

            if (messages == null || messages.Count == 0)
            {
                return false;
            }

            DialogueRuntimeContext currentSnapshot = runtimeContext?.WithCurrentRuntimeMarkers();
            if (runtimeContext == null ||
                !DialogueContextResolver.TryResolveLiveContext(currentSnapshot, out DialogueLiveContext liveContext, out _) ||
                !DialogueContextValidator.ValidateRequestSend(currentSnapshot, liveContext, out _))
            {
                return false;
            }

            return !IsWithinDebounceWindow(session);
        }

        private static int GetCurrentTick()
        {
            return Find.TickManager?.TicksGame ?? 0;
        }

        private static bool IsWithinDebounceWindow(FactionDialogueSession session)
        {
            if (session == null)
            {
                return false;
            }

            bool gamePaused = Find.TickManager?.Paused ?? false;
            if (gamePaused)
            {
                return IsWithinRealtimeDebounce(session);
            }

            int lastQueuedTick = session.lastDiplomacyRequestQueuedTick;
            if (lastQueuedTick != int.MinValue)
            {
                int tickDelta = GetCurrentTick() - lastQueuedTick;
                if (tickDelta >= 0 && tickDelta < RequestDebounceTicks)
                {
                    return true;
                }
            }

            return IsWithinRealtimeDebounce(session);
        }

        private static bool IsWithinRealtimeDebounce(FactionDialogueSession session)
        {
            float lastQueuedRealtime = session.lastDiplomacyRequestQueuedRealtime;
            if (lastQueuedRealtime < 0f)
            {
                return false;
            }

            float realtimeDelta = Time.realtimeSinceStartup - lastQueuedRealtime;
            return realtimeDelta >= 0f && realtimeDelta < RequestDebounceSeconds;
        }

        private static void CancelSupersededPendingRequest(FactionDialogueSession session)
        {
            if (session == null || string.IsNullOrEmpty(session.pendingRequestId))
            {
                return;
            }

            string supersededRequestId = session.pendingRequestId;
            AIChatServiceAsync.Instance.CancelRequest(
                supersededRequestId,
                "request_superseded",
                "Request superseded by a newer dialogue turn");
            session.pendingRequestId = null;
            session.pendingRequestLease?.Dispose();
            session.pendingRequestLease = null;
            session.isWaitingForResponse = false;
            session.aiRequestProgress = 0f;
        }

        private static void HandleSuccess(
            FactionDialogueSession session,
            Faction faction,
            DialogueRequestLease lease,
            DialogueRuntimeContext runtimeContext,
            string response,
            Action<DialogueResponseEnvelope> onSuccess,
            Action<string> onDropped)
        {
            if (!IsRequestContextStillValid(session, faction, lease, runtimeContext, out string droppedReason))
            {
                onDropped?.Invoke(droppedReason);
                return;
            }

            session.pendingRequestId = null;
            session.pendingRequestLease?.Dispose();
            session.pendingRequestLease = null;
            session.isWaitingForResponse = false;
            session.aiRequestProgress = 1f;

            DialogueResponseEnvelope envelope = DialogueResponseEnvelopeParser.Parse(
                response, DialogueUsageChannel.Diplomacy);
            if (!envelope.IsValid && !string.IsNullOrWhiteSpace(response))
            {
                // All retries exhausted upstream; raw passthrough arrived as plain text.
                // The strict structured parser rejected it — fall back to legacy parsing
                // so the player sees the LLM's actual words instead of a generic fallback.
                DialogueResponseEnvelope legacyEnvelope = DialogueResponseEnvelopeParser.Parse(
                    response, DialogueUsageChannel.Unknown);
                if (legacyEnvelope.IsValid)
                {
                    envelope = legacyEnvelope;
                }
            }
            onSuccess?.Invoke(envelope);
        }

        private static void HandleError(
            FactionDialogueSession session,
            Faction faction,
            DialogueRequestLease lease,
            DialogueRuntimeContext runtimeContext,
            string error,
            Action<string> onError,
            Action<string> onDropped)
        {
            if (!IsRequestContextStillValid(session, faction, lease, runtimeContext, out string droppedReason))
            {
                onDropped?.Invoke(droppedReason);
                return;
            }

            session.pendingRequestId = null;
            session.pendingRequestLease?.Dispose();
            session.pendingRequestLease = null;
            session.isWaitingForResponse = false;
            session.aiError = error;
            onError?.Invoke(error);
        }

        private static void HandleProgress(
            FactionDialogueSession session,
            Faction faction,
            DialogueRequestLease lease,
            DialogueRuntimeContext runtimeContext,
            float progress,
            Action<float> onProgress)
        {
            if (!IsRequestContextStillValid(session, faction, lease, runtimeContext, out _))
            {
                return;
            }

            session.aiRequestProgress = progress;
            onProgress?.Invoke(progress);
        }

        private static bool IsRequestContextStillValid(
            FactionDialogueSession session,
            Faction faction,
            DialogueRequestLease lease,
            DialogueRuntimeContext runtimeContext,
            out string reason)
        {
            reason = string.Empty;
            if (session == null || faction == null || faction.defeated || lease == null)
            {
                reason = "request_context_null";
                return false;
            }

            string requestId = lease.RequestId;
            if (string.IsNullOrEmpty(requestId))
            {
                reason = "lease_request_id_empty";
                return false;
            }

            if (!string.Equals(session.pendingRequestId, requestId, StringComparison.Ordinal))
            {
                reason = "pending_request_mismatch";
                return false;
            }

            if (session.pendingRequestLease == null || !ReferenceEquals(session.pendingRequestLease, lease))
            {
                reason = "request_lease_mismatch";
                return false;
            }

            if (!lease.IsValidFor(requestId, runtimeContext?.DialogueSessionId ?? string.Empty, runtimeContext?.ContextVersion ?? -1))
            {
                reason = "request_lease_invalid";
                return false;
            }

            DialogueRuntimeContext resolveContext = runtimeContext?.WithCurrentRuntimeMarkers();
            if (!DialogueContextResolver.TryResolveLiveContext(resolveContext, out DialogueLiveContext liveContext, out reason))
            {
                return false;
            }

            if (!DialogueContextValidator.ValidateCallbackApply(runtimeContext, liveContext, runtimeContext?.DialogueSessionId, out reason))
            {
                return false;
            }

            FactionDialogueSession liveSession = GameComponent_DiplomacyManager.Instance?.GetSession(faction);
            if (!ReferenceEquals(liveSession, session))
            {
                reason = "session_reference_changed";
                return false;
            }

            return true;
        }
    }
}
