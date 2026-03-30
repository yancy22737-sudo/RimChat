using System;
using System.Collections.Generic;
using RimChat.AI;
using RimChat.Dialogue;
using RimChat.DiplomacySystem;
using RimChat.Memory;
using RimChat.Prompting;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: diplomacy dialogue session, AI request queue, and RimWorld window stack.
    /// Responsibility: handle manual send-info actions such as taunt and caravan request.
    /// </summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private sealed class TauntSendInfoOption
        {
            public TauntSendInfoOption(
                string labelKey,
                string descriptionKey,
                string raidLabelKey,
                string forcedActionType,
                bool requiresConfirmation,
                bool requiresRandomWaves = false,
                bool explicitChallengeRequest = false)
            {
                LabelKey = labelKey;
                DescriptionKey = descriptionKey;
                RaidLabelKey = raidLabelKey;
                ForcedActionType = forcedActionType;
                RequiresConfirmation = requiresConfirmation;
                RequiresRandomWaves = requiresRandomWaves;
                ExplicitChallengeRequest = explicitChallengeRequest;
            }

            public string LabelKey { get; }

            public string DescriptionKey { get; }

            public string RaidLabelKey { get; }

            public string ForcedActionType { get; }

            public bool RequiresConfirmation { get; }

            public bool RequiresRandomWaves { get; }

            public bool ExplicitChallengeRequest { get; }
        }

        private sealed class Dialog_SendInfoTauntPicker : Window
        {
            private readonly Dialog_DiplomacyDialogue owner;

            public Dialog_SendInfoTauntPicker(Dialog_DiplomacyDialogue owner)
            {
                this.owner = owner;
                absorbInputAroundWindow = true;
                closeOnClickedOutside = true;
                doCloseButton = false;
                doCloseX = true;
                draggable = true;
                forcePause = true;
            }

            public override Vector2 InitialSize => new Vector2(560f, 300f);

            public override void DoWindowContents(Rect inRect)
            {
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "RimChat_SendInfoTauntTitle".Translate());
                Text.Font = GameFont.Small;

                float y = inRect.y + 44f;
                foreach (TauntSendInfoOption option in TauntSendInfoOptions)
                {
                    Rect cardRect = new Rect(inRect.x, y, inRect.width, 64f);
                    Widgets.DrawMenuSection(cardRect);

                    Rect buttonRect = new Rect(cardRect.x + 12f, cardRect.y + 10f, 160f, 34f);
                    if (Widgets.ButtonText(buttonRect, option.LabelKey.Translate()))
                    {
                        owner.SubmitTauntSendInfo(option);
                        Close();
                        return;
                    }

                    Rect descRect = new Rect(buttonRect.xMax + 12f, cardRect.y + 8f, cardRect.width - buttonRect.width - 36f, 44f);
                    Widgets.Label(descRect, option.DescriptionKey.Translate());
                    y += cardRect.height + 10f;
                }
            }
        }

        private static readonly TauntSendInfoOption[] TauntSendInfoOptions =
        {
            new TauntSendInfoOption(
                "RimChat_SendInfoTauntOptionStandard",
                "RimChat_SendInfoTauntOptionStandardDesc",
                "RimChat_SendInfoRaidLabelStandard",
                AIActionNames.RequestRaid,
                false),
            new TauntSendInfoOption(
                "RimChat_SendInfoTauntOptionWaves",
                "RimChat_SendInfoTauntOptionWavesDesc",
                "RimChat_SendInfoRaidLabelWaves",
                AIActionNames.RequestRaidWaves,
                false,
                requiresRandomWaves: true),
            new TauntSendInfoOption(
                "RimChat_SendInfoTauntOptionJoint",
                "RimChat_SendInfoTauntOptionJointDesc",
                "RimChat_SendInfoRaidLabelJoint",
                AIActionNames.RequestRaidCallEveryone,
                true,
                explicitChallengeRequest: true)
        };

        private void TryStartManualTauntSend()
        {
            if (!CanSendMessageNow() || session == null || faction == null)
            {
                return;
            }

            Find.WindowStack.Add(new Dialog_SendInfoTauntPicker(this));
        }

        private void TryStartManualCaravanRequestSend()
        {
            if (!CanSendMessageNow() || session == null || faction == null)
            {
                return;
            }

            SendSystemInfoRequest(
                "RimChat_SendInfoCaravanSystemMessage".Translate().ToString(),
                BuildSendInfoHiddenDirective(AIActionNames.RequestCaravan));
        }

        private void SubmitTauntSendInfo(TauntSendInfoOption option)
        {
            if (option == null || !CanSendMessageNow() || session == null || faction == null)
            {
                return;
            }

            if (option.RequiresConfirmation)
            {
                Find.WindowStack.Add(new Dialog_MessageBox(
                    "RimChat_SendInfoTauntConfirmJointBody".Translate(option.RaidLabelKey.Translate()),
                    "RimChat_SendInfoTauntConfirmAccept".Translate(),
                    () => SendSystemInfoRequest(BuildTauntSystemMessage(option), BuildTauntHiddenDirectiveForCurrentFaction(option)),
                    "RimChat_SendInfoTauntConfirmCancel".Translate(),
                    null,
                    "RimChat_SendInfoTauntConfirmJointTitle".Translate()));
                return;
            }

            SendSystemInfoRequest(BuildTauntSystemMessage(option), BuildTauntHiddenDirectiveForCurrentFaction(option));
        }

        private static string BuildTauntSystemMessage(TauntSendInfoOption option)
        {
            string raidLabel = option?.RaidLabelKey.Translate().ToString() ?? "RimChat_Unknown".Translate().ToString();
            return "RimChat_SendInfoTauntSystemMessage".Translate(raidLabel).ToString();
        }

        private static string BuildTauntHiddenDirective(TauntSendInfoOption option)
        {
            if (option == null)
            {
                return string.Empty;
            }

            int? randomWaves = option.RequiresRandomWaves ? Rand.RangeInclusive(2, 6) : (int?)null;
            return BuildSendInfoHiddenDirective(option.ForcedActionType, randomWaves, option.ExplicitChallengeRequest);
        }

        private string BuildTauntHiddenDirectiveForCurrentFaction(TauntSendInfoOption option)
        {
            if (faction == null || faction.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile)
            {
                return string.Empty;
            }

            return BuildTauntHiddenDirective(option);
        }

        private static string BuildSendInfoHiddenDirective(
            string forcedActionType,
            int? waves = null,
            bool explicitChallengeRequest = false)
        {
            if (string.IsNullOrWhiteSpace(forcedActionType))
            {
                return string.Empty;
            }

            string wavesLine = waves.HasValue ? $"\nwaves: {waves.Value}" : string.Empty;
            string explicitLine = explicitChallengeRequest ? "\nexplicit_challenge_request: true" : string.Empty;
            string challengeLine = explicitChallengeRequest
                ? "\nchallenge_phrase: call everyone | joint raid | 一起上 | 联合袭击"
                : string.Empty;
            return
                "[SendInfoDirective]\n" +
                "source: manual_send_info\n" +
                $"force_action: {forcedActionType}" +
                wavesLine +
                explicitLine +
                challengeLine +
                "\nrequire_matching_action: true\n" +
                "[/SendInfoDirective]\n" +
                "[SendInfoInstruction]\n" +
                "This hidden directive comes from the UI and must be executed this turn. " +
                "Keep the visible reply in character, but you MUST emit the exact forced action with the provided parameters." +
                "\n[/SendInfoInstruction]";
        }

        private void SendSystemInfoRequest(string systemMessage, string hiddenDirective = null)
        {
            if (string.IsNullOrWhiteSpace(systemMessage) || session == null || faction == null || !CanSendMessageNow())
            {
                return;
            }

            ClearPendingStrategySuggestions(session);

            FactionDialogueSession currentSession = session;
            Faction currentFaction = faction;
            currentSession.AddMessage("System", systemMessage, false, DialogueMessageType.System);

            if (!AIChatServiceAsync.Instance.IsConfigured())
            {
                Log.Message("[RimChat] AI not configured, using fallback response");
                AddFallbackResponseToSession(systemMessage, currentSession, currentFaction);
                return;
            }

            List<ChatMessageData> chatMessages;
            string aiDriverMessage = string.IsNullOrWhiteSpace(hiddenDirective)
                ? systemMessage
                : $"{systemMessage}\n\n{hiddenDirective}";
            try
            {
                chatMessages = BuildChatMessages(aiDriverMessage, currentSession, systemMessage);
            }
            catch (PromptRenderException ex)
            {
                HandlePromptRenderFailure(ex);
                return;
            }
            catch (Exception ex)
            {
                HandlePromptBuildFailure(ex, currentSession, currentFaction);
                return;
            }

            DialogueRuntimeContext requestContext = runtimeContext.WithCurrentRuntimeMarkers();
            bool resolved = DialogueContextResolver.TryResolveLiveContext(
                requestContext,
                out DialogueLiveContext liveContext,
                out string resolveReason);
            string validateReason = string.Empty;
            bool validated = resolved && DialogueContextValidator.ValidateRequestSend(requestContext, liveContext, out validateReason);
            if (!resolved || !validated)
            {
                Log.Warning(
                    $"[RimChat] Diplomacy request rejected before queue. " +
                    $"resolveReason={resolveReason ?? "null"}, validateReason={validateReason ?? "null"}, " +
                    $"faction={currentFaction?.Name ?? "null"}, negotiator={negotiator?.ThingID ?? "null"}, " +
                    $"pendingRequestId={currentSession?.pendingRequestId ?? "null"}, waiting={currentSession?.isWaitingForResponse ?? false}, " +
                    $"hasLease={currentSession?.pendingRequestLease != null}");
                HandleDroppedRequest(resolveReason, validateReason);
                return;
            }

            bool queued = conversationController.TrySendDialogueRequest(
                currentSession,
                currentFaction,
                chatMessages,
                requestContext,
                windowInstanceId,
                onSuccess: response =>
                {
                    AddAIResponseToSession(response, currentSession, currentFaction, aiDriverMessage);
                },
                onError: error =>
                {
                    Log.Warning($"[RimChat] AI request failed: {error}");
                    ShowDialogueRequestError(error);
                },
                onProgress: null,
                onDropped: reason =>
                {
                    HandleDroppedRequest(reason);
                });

            if (!queued)
            {
                if (conversationController.IsRequestDebounced(currentSession))
                {
                    HandleDroppedRequest("request_debounced");
                    return;
                }

                if (currentSession.isWaitingForResponse)
                {
                    HandleDroppedRequest("request_already_waiting");
                    return;
                }

                Log.Warning("[RimChat] Failed to queue diplomacy AI request.");
                HandleDroppedRequest(currentSession?.aiError, "request_queue_rejected");
            }
        }
    }
}
