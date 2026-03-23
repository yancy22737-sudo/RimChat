using System;
using System.Collections.Generic;
using RimChat.AI;
using RimChat.Dialogue;

namespace RimChat.Rpg
{
    /// <summary>
    /// Owns RPG request lifecycle with lease + runtime context validation.
    /// </summary>
    public sealed class RpgDialogueConversationController
    {
        public DialogueRequestLease TrySend(
            DialogueRuntimeContext runtimeContext,
            string ownerWindowId,
            List<ChatMessageData> messages,
            Action<DialogueResponseEnvelope> onReady,
            Action<string> onError,
            Action<string> onDropped)
        {
            if (runtimeContext == null || messages == null || messages.Count == 0)
            {
                return null;
            }

            DialogueRuntimeContext sendContext = runtimeContext.WithCurrentRuntimeMarkers();
            bool resolved = DialogueContextResolver.TryResolveLiveContext(sendContext, out DialogueLiveContext liveContext, out string resolveReason);
            string validateReason = string.Empty;
            bool validated = resolved && DialogueContextValidator.ValidateRequestSend(sendContext, liveContext, out validateReason);
            if (!resolved || !validated)
            {
                onDropped?.Invoke(string.IsNullOrWhiteSpace(validateReason) ? resolveReason : validateReason);
                return null;
            }

            DialogueRequestLease lease = new DialogueRequestLease(sendContext.DialogueSessionId, ownerWindowId, sendContext.ContextVersion);
            string requestId = AIChatServiceAsync.Instance.SendChatRequestAsync(
                messages,
                onSuccess: response =>
                {
                    if (!TryValidateCallback(lease, sendContext, out string callbackReason))
                    {
                        onDropped?.Invoke(callbackReason);
                        return;
                    }

                    DialogueResponseEnvelope envelope = ParseEnvelope(response);
                    onReady?.Invoke(envelope);
                },
                onError: error =>
                {
                    if (!TryValidateCallback(lease, sendContext, out string callbackReason))
                    {
                        onDropped?.Invoke(callbackReason);
                        return;
                    }

                    onError?.Invoke(error);
                },
                usageChannel: DialogueUsageChannel.Rpg,
                debugSource: AIRequestDebugSource.RpgDialogue);

            if (string.IsNullOrWhiteSpace(requestId))
            {
                onError?.Invoke("Failed to queue AI request");
                return null;
            }

            lease.BindRequestId(requestId);
            return lease;
        }

        public void Cancel(DialogueRequestLease lease)
        {
            if (lease == null)
            {
                return;
            }

            string requestId = lease.RequestId;
            if (!string.IsNullOrWhiteSpace(requestId))
            {
                AIChatServiceAsync.Instance.CancelRequest(
                    requestId,
                    "dialogue_window_closed",
                    "Request cancelled by dialogue close");
            }
        }

        public void CloseLease(DialogueRequestLease lease)
        {
            if (lease == null)
            {
                return;
            }

            lease.MarkClosing();
            Cancel(lease);
            lease.Dispose();
        }

        public bool TryApplyResponseEnvelope(
            DialogueRequestLease lease,
            DialogueRuntimeContext runtimeContext,
            DialogueResponseEnvelope envelope,
            out string reason)
        {
            reason = string.Empty;
            if (lease == null || runtimeContext == null || envelope == null)
            {
                reason = "apply_context_null";
                return false;
            }

            int currentContextVersion = AIChatServiceAsync.Instance.GetCurrentContextVersionSnapshot();
            if (!lease.IsValidFor(lease.RequestId, runtimeContext.DialogueSessionId, currentContextVersion))
            {
                reason = "lease_invalid";
                return false;
            }

            DialogueRuntimeContext resolveContext = runtimeContext.WithCurrentRuntimeMarkers();
            if (!DialogueContextResolver.TryResolveLiveContext(resolveContext, out DialogueLiveContext liveContext, out reason))
            {
                return false;
            }

            return DialogueContextValidator.ValidateActionExecution(runtimeContext, liveContext, out reason);
        }

        private static DialogueResponseEnvelope ParseEnvelope(string response)
        {
            LLMRpgApiResponse parsed = LLMRpgApiResponse.Parse(response);
            return new DialogueResponseEnvelope
            {
                RawResponse = response ?? string.Empty,
                DialogueText = parsed?.DialogueContent ?? string.Empty,
                Actions = parsed?.Actions ?? new List<LLMRpgApiResponse.ApiAction>()
            };
        }

        private static bool TryValidateCallback(
            DialogueRequestLease lease,
            DialogueRuntimeContext runtimeContext,
            out string reason)
        {
            reason = string.Empty;
            if (lease == null || runtimeContext == null)
            {
                reason = "lease_runtime_null";
                return false;
            }

            if (!lease.IsValidFor(lease.RequestId, runtimeContext.DialogueSessionId, runtimeContext.ContextVersion))
            {
                reason = "lease_invalid";
                return false;
            }

            DialogueRuntimeContext resolveContext = runtimeContext.WithCurrentRuntimeMarkers();
            if (!DialogueContextResolver.TryResolveLiveContext(resolveContext, out DialogueLiveContext liveContext, out reason))
            {
                return false;
            }

            return DialogueContextValidator.ValidateCallbackApply(runtimeContext, liveContext, runtimeContext.DialogueSessionId, out reason);
        }
    }
}
