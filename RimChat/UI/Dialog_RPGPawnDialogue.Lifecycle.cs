using System;
using System.Collections.Generic;
using RimChat.AI;
using RimChat.Core;
using RimChat.Dialogue;
using RimWorld;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Responsibilities: request lease lifecycle, stale-response fail-fast handling, and stage-B envelope apply.
    /// Dependencies: RpgDialogueConversationController, DialogueContextValidator/Resolver, action execution pipeline.
    /// </summary>
    public partial class Dialog_RPGPawnDialogue
    {
        public bool MatchesWindowLifecycleKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            return string.Equals(windowLifecycleKey, key.Trim(), StringComparison.Ordinal);
        }

        private void CloseActiveRequestLease()
        {
            if (activeRequestLease == null)
            {
                return;
            }

            conversationController.CloseLease(activeRequestLease);
            activeRequestLease = null;
            activeRequestRuntimeContext = null;
        }

        private void ReleaseActiveRequestLease()
        {
            if (activeRequestLease == null)
            {
                return;
            }

            activeRequestLease.Dispose();
            activeRequestLease = null;
            activeRequestRuntimeContext = null;
        }

        private void PrepareEnvelopeForDisplay(DialogueResponseEnvelope envelope)
        {
            if (envelope == null)
            {
                return;
            }

            if (RimChatMod.Settings.EnableRPGAPI)
            {
                var apiResponse = new LLMRpgApiResponse
                {
                    DialogueContent = envelope.DialogueText ?? string.Empty,
                    Actions = envelope.Actions ?? new List<LLMRpgApiResponse.ApiAction>()
                };
                EnsureRpgActionFallbacks(apiResponse);
                envelope.DialogueText = NormalizeVisibleNpcDialogueText(apiResponse.DialogueContent);
                envelope.Actions = apiResponse.Actions ?? new List<LLMRpgApiResponse.ApiAction>();
                return;
            }

            envelope.DialogueText = NormalizeVisibleNpcDialogueText(envelope.DialogueText ?? envelope.RawResponse);
            envelope.Actions = new List<LLMRpgApiResponse.ApiAction>();
        }

        private void TryApplyPendingEnvelope()
        {
            if (pendingResponseEnvelope == null)
            {
                return;
            }

            if (!RimChatMod.Settings.EnableRPGAPI || pendingResponseEnvelope.Actions == null || pendingResponseEnvelope.Actions.Count == 0)
            {
                pendingResponseEnvelope = null;
                ReleaseActiveRequestLease();
                return;
            }

            if (!conversationController.TryApplyResponseEnvelope(
                    activeRequestLease,
                    activeRequestRuntimeContext ?? runtimeContext,
                    pendingResponseEnvelope,
                    out string reason))
            {
                HandleDroppedResponse(reason);
                pendingResponseEnvelope = null;
                ReleaseActiveRequestLease();
                return;
            }

            var apiResponse = new LLMRpgApiResponse
            {
                DialogueContent = currentDialogueText,
                Actions = pendingResponseEnvelope.Actions
            };
            ApplyRPGAPIAndShowPopup(apiResponse);
            pendingResponseEnvelope = null;
            ReleaseActiveRequestLease();
        }

        private void HandleDroppedResponse(string reason)
        {
            if (isWindowClosing)
            {
                return;
            }

            string message = "RimChat_DialogueResponseDropped".Translate(reason ?? "unknown").ToString();
            aiResponseText = message;
            AddSystemFeedback(message, 4.5f);
            chatHistory.Add(new ChatMessageData { role = "system", content = message });
            dialogPages.Add(new DialoguePage { speakerName = "System", text = message });
            RecordSessionDialogueTurn("System", message, false);
            ReleaseActiveRequestLease();
        }
    }
}
