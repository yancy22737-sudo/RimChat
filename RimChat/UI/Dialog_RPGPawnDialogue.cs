using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimChat.AI;
using RimChat.Config;
using RimChat.Util;
using RimChat.Core;
using RimChat.Memory;

namespace RimChat.UI
{
    /// <summary>/// Dependencies: RimWorld window/UI runtime, AI request callbacks, and RPG archive/session helpers.
 /// Responsibility: host the full-screen PawnRPG dialogue window and orchestrate live/history rendering.
 ///</summary>
    public partial class Dialog_RPGPawnDialogue : Window
    {
        private readonly Pawn initiator;
        private readonly Pawn target;
        private readonly string dialogueSessionId;
        private string currentDialogueText = "";
        private string displayedText = "";
        private string userReplyText = "";
        private int visibleChars = 0;
        private float lastCharTime = 0f;
        
        // Typing State
        private bool isTyping = false;
        
        // Logical States
        private bool isSendingInitialMessage = false;
        private bool isShowingUserText = false;
        private bool isWaitingForDelayAfterUser = false;
        private float timeUserTextFinished = 0f;
        
        // AI State
        private bool aiResponseReady = false;
        private string aiResponseText = "";
        private LLMRpgApiResponse pendingApiResponse = null;

        // NPC离开session后, 进入冷却拒聊
        private bool isDialogueEndedByNpc = false;
        private string dialogueEndReason = "";
        private bool sessionCloseSummaryCommitted = false;
        private bool archiveSessionFinalized = false;
        
        private string currentSpeakerName = "";
        
        private List<ChatMessageData> chatHistory = new List<ChatMessageData>();
        
        private struct DialoguePage
        {
            public string speakerName;
            public string text;
        }
        private List<DialoguePage> dialogPages = new List<DialoguePage>();
        private bool isViewingHistory = false;
        private int historyViewIndex = 0;
        
        private static readonly Color DialogueBoxColor = new Color(0.1f, 0.1f, 0.12f, 0.9f);
        private const float DialogueBoxHeight = 260f;
        private const float PortraitWidth = 400f;
        private const float PortraitHeight = 500f;
        
        private float globalFadeAlpha = 0f;
        private float initiatorFadeAlpha = 0f;
        private float targetFadeAlpha = 0f;
        private bool firstTargetSentenceDone = false;
        private const float FadeSpeed = 1.5f; // Real-time per second speed
        private const string UserReplyInputControlName = "UserReplyInput";
        private const string RpgStrictOutputContractReminder =
            "Strict RPG output contract: write natural dialogue as plain text. " +
            "Only if gameplay effects are needed, append exactly one raw JSON object in the form " +
            "{\"actions\":[...]} after the dialogue. " +
            "Never wrap dialogue into JSON fields like \"dialogue\", \"response\", or \"content\". " +
            "Inside each action object, use key \"action\" and optional flat fields " +
            "\"defName\"/\"amount\"/\"reason\".";

        public override Vector2 InitialSize => new Vector2(Verse.UI.screenWidth, Verse.UI.screenHeight);
        protected override float Margin => 0f;

        public Dialog_RPGPawnDialogue(Pawn initiator, Pawn target) : this(initiator, target, null)
        {
        }

        public Dialog_RPGPawnDialogue(Pawn initiator, Pawn target, string proactiveOpening)
        {
            this.initiator = initiator;
            this.target = target;
            dialogueSessionId = Guid.NewGuid().ToString("N");
            this.doCloseX = false;
            this.doCloseButton = false;
            this.closeOnClickedOutside = false;
            this.closeOnAccept = false;
            this.closeOnCancel = true;
            this.absorbInputAroundWindow = true;
            this.forcePause = true;
            this.preventCameraMotion = true;
            this.doWindowBackground = false;

            bool hasProactiveOpening = !string.IsNullOrWhiteSpace(proactiveOpening);
            bool hasPersonalMemory = !string.IsNullOrWhiteSpace(
                RpgNpcDialogueArchiveManager.Instance.BuildPromptMemoryBlock(target, initiator));
            bool shouldSeedProactiveOpening = hasProactiveOpening && !hasPersonalMemory;

            chatHistory = BuildRPGChatMessages();
            if (hasProactiveOpening && hasPersonalMemory)
            {
                chatHistory.Add(new ChatMessageData
                {
                    role = "user",
                    content = BuildProactiveOpeningCarryOverPrompt(proactiveOpening)
                });
            }

            if (!shouldSeedProactiveOpening || !TrySeedProactiveOpening(proactiveOpening))
            {
                SendInitialMessage();
            }
        }

        private List<ChatMessageData> BuildRPGChatMessages()
        {
            var messages = new List<ChatMessageData>();
            string systemPrompt = BuildRpgSystemPromptForRequest(false, string.Empty);
            messages.Add(new ChatMessageData { role = "system", content = systemPrompt });
            return messages;
        }

        private static List<string> ParseSceneTagsCsv(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
            {
                return null;
            }

            return csv
                .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(tag => tag.Trim().ToLowerInvariant())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct()
                .ToList();
        }

        private static string BuildProactiveOpeningCarryOverPrompt(string proactiveOpening)
        {
            return "A proactive trigger opened this chat from NPC side.\n"
                + "Use it only as scene context. Do not copy previous opening wording.\n"
                + "Generate a fresh in-character line with continuity from personal memory.";
        }

        private bool TrySeedProactiveOpening(string proactiveOpening)
        {
            if (string.IsNullOrWhiteSpace(proactiveOpening))
            {
                return false;
            }

            string opening = NormalizeVisibleNpcDialogueText(proactiveOpening);
            currentSpeakerName = target.LabelShort;
            currentDialogueText = opening;
            displayedText = "";
            visibleChars = 0;
            isTyping = true;
            lastCharTime = Time.realtimeSinceStartup;
            ResetDialogueTextPaging();
            chatHistory.Add(new ChatMessageData { role = "assistant", content = opening });
            dialogPages.Add(new DialoguePage { speakerName = target.LabelShort, text = opening });
            RpgDialogueTraceTracker.RegisterTurn(initiator, target, false, opening, dialogueSessionId);
            return true;
        }

        private float inputAlpha = 0.3f;

        private void SendInitialMessage()
        {
            isSendingInitialMessage = true;
            currentDialogueText = "";
            displayedText = "";
            visibleChars = 0;
            currentSpeakerName = target.LabelShort;
            List<ChatMessageData> requestMessages = BuildCompressedRpgRequestMessages();

            AIChatServiceAsync.Instance.SendChatRequestAsync(
                requestMessages,
                onSuccess: (response) =>
                {
                    if (RimChatMod.Settings.EnableRPGAPI)
                    {
                        pendingApiResponse = LLMRpgApiResponse.Parse(response);
                        pendingApiResponse.DialogueContent = NormalizeVisibleNpcDialogueText(pendingApiResponse.DialogueContent);
                        currentDialogueText = pendingApiResponse.DialogueContent;
                    }
                    else
                    {
                        currentDialogueText = NormalizeVisibleNpcDialogueText(response);
                    }

                    isSendingInitialMessage = false;
                    ResetDialogueTextPaging();
                    string visibleHistoryContent = NormalizeHistoryAssistantContent(response, currentDialogueText);
                    chatHistory.Add(new ChatMessageData { role = "assistant", content = visibleHistoryContent });
                    dialogPages.Add(new DialoguePage { speakerName = target.LabelShort, text = currentDialogueText });
                    RpgDialogueTraceTracker.RegisterTurn(initiator, target, false, currentDialogueText, dialogueSessionId);
                    if (pendingApiResponse != null)
                    {
                        EnsureRpgActionFallbacks(pendingApiResponse);
                    }
                    isTyping = true;
                    lastCharTime = Time.realtimeSinceStartup;

                    if (pendingApiResponse != null)
                    {
                        ApplyRPGAPIAndShowPopup(pendingApiResponse);
                        pendingApiResponse = null;
                    }
                },
                onError: (error) =>
                {
                    isSendingInitialMessage = false;
                    currentDialogueText = "Error: " + error;
                    isTyping = true;
                },
                usageChannel: DialogueUsageChannel.Rpg,
                debugSource: AIRequestDebugSource.RpgDialogue
            );
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Update Alphas based on real time
            float deltaTime = Time.deltaTime;
            globalFadeAlpha = Mathf.Clamp01(globalFadeAlpha + deltaTime * FadeSpeed);
            targetFadeAlpha = Mathf.Clamp01(targetFadeAlpha + deltaTime * FadeSpeed);
            
            // Player pawn starts fading in when target finishes first sentence or player speaks
            if (firstTargetSentenceDone || currentSpeakerName == initiator.LabelShort || dialogPages.Any(p => p.speakerName == initiator.LabelShort))
            {
                initiatorFadeAlpha = Mathf.Clamp01(initiatorFadeAlpha + deltaTime * FadeSpeed);
            }

            // Draw Portraits first (Portraits use their own alpha inside the methods)
            DrawPortraits(inRect);

            // Draw Dialogue Box with global alpha
            GUI.color = new Color(1f, 1f, 1f, globalFadeAlpha);
            DrawDialogueBox(inRect);
            GUI.color = Color.white;
            DrawActionFeedback(inRect);

            if (Event.current.type == EventType.MouseDown)
            {
                Rect dialogueBoxRect = new Rect(0, inRect.height - DialogueBoxHeight, inRect.width, DialogueBoxHeight);
                
                // Click outside dialogue box to close window
                if (!dialogueBoxRect.Contains(Event.current.mousePosition))
                {
                    Close();
                    Event.current.Use();
                }
                // Click inside dialogue box to skip text animation
                else if (isTyping)
                {
                    visibleChars = currentDialogueText.Length;
                    displayedText = currentDialogueText;
                    isTyping = false;
                    
                    if (isShowingUserText)
                    {
                        isWaitingForDelayAfterUser = true;
                        timeUserTextFinished = Time.realtimeSinceStartup;
                    }
                    
                    Event.current.Use();
                }
                else
                {
                    // Click inside dialogue box but not in input area -> clear focus
                    float inputHeight = 45f;
                    // Re-calculate the exact bottom area used for input
                    float dialogueBoxY = inRect.height - DialogueBoxHeight;
                    Rect bottomArea = new Rect(35f, dialogueBoxY + DialogueBoxHeight - 35f - inputHeight, inRect.width - 70f, inputHeight);
                    
                    if (!bottomArea.Contains(Event.current.mousePosition))
                    {
                        if (GUI.GetNameOfFocusedControl() == UserReplyInputControlName)
                        {
                            GUI.FocusControl(null);
                        }
                    }
                }
            }
        }

        private RenderTexture initiatorRT;
        private RenderTexture targetRT;

        public override void PreClose()
        {
            TryFinalizeArchiveSessionOnClose();
            TryCommitRpgSessionSummaryOnClose();
            base.PreClose();
            if (initiatorRT != null) { UnityEngine.Object.Destroy(initiatorRT); initiatorRT = null; }
            if (targetRT != null) { UnityEngine.Object.Destroy(targetRT); targetRT = null; }
        }

        private void TryFinalizeArchiveSessionOnClose()
        {
            if (archiveSessionFinalized)
            {
                return;
            }

            archiveSessionFinalized = true;
            RpgNpcDialogueArchiveManager.Instance.FinalizeSession(initiator, target, dialogueSessionId, chatHistory);
        }

        private void TryCommitRpgSessionSummaryOnClose()
        {
            if (sessionCloseSummaryCommitted)
            {
                return;
            }

            sessionCloseSummaryCommitted = true;
            DialogueSummaryService.TryPushRpgSessionSummaryOnClose(initiator, target, chatHistory);
        }

        private void DrawPawnPortrait(Rect rect, Pawn pawn, bool flip)
        {
            if (pawn == null) return;

            // 3x Super-resolution for extreme clarity (1200x1500 per portrait)
            int texWidth = (int)(rect.width * 3f);
            int texHeight = (int)(rect.height * 3f);

            RenderTexture rt = flip ? initiatorRT : targetRT;

            if (rt == null || rt.width != texWidth || rt.height != texHeight)
            {
                if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
                rt = new RenderTexture(texWidth, texHeight, 24, RenderTextureFormat.ARGB32);
                rt.antiAliasing = (QualitySettings.antiAliasing > 0) ? QualitySettings.antiAliasing : 8;
                rt.filterMode = FilterMode.Bilinear;
                rt.useMipMap = false; // Disable MipMaps to avoid fuzziness
                rt.Create();
                
                if (flip) initiatorRT = rt;
                else targetRT = rt;
            }

            // Only perform the expensive 3D render during Repaint event
            if (Event.current.type == EventType.Repaint)
            {
                Vector3 cameraOffset = new Vector3(0f, 0f, 0.15f);
                float zoom = 1.35f;

                // Directly Render pawn onto our custom high-res buffer every frame
                Find.PawnCacheRenderer.RenderPawn(pawn, rt, cameraOffset, zoom, 0f, Rot4.South, true, true, true, true, default(Vector3), null, null, true);
            }

            if (rt != null)
            {
                if (flip)
                {
                    // Horizontal flip using scale matrix
                    Matrix4x4 savedMatrix = GUI.matrix;
                    Vector2 center = rect.center;
                    GUIUtility.ScaleAroundPivot(new Vector2(-1f, 1f), center);
                    GUI.DrawTexture(rect, rt, ScaleMode.StretchToFill, true);
                    GUI.matrix = savedMatrix;
                }
                else
                {
                    GUI.DrawTexture(rect, rt, ScaleMode.StretchToFill, true);
                }
            }
        }

        private void DrawDialogueBox(Rect inRect)
        {
            // Check State Transitions
            if (isShowingUserText && isWaitingForDelayAfterUser && !isViewingHistory)
            {
                if (Time.realtimeSinceStartup - timeUserTextFinished >= 1.0f)
                {
                    if (aiResponseReady)
                    {
                        // Switch to AI text
                        isShowingUserText = false;
                        isWaitingForDelayAfterUser = false;
                        
                        currentSpeakerName = target.LabelShort;
                        currentDialogueText = NormalizeVisibleNpcDialogueText(aiResponseText);
                        displayedText = "";
                        visibleChars = 0;
                        isTyping = true;
                        lastCharTime = Time.realtimeSinceStartup;
                        ResetDialogueTextPaging();
                        
                        if (pendingApiResponse != null)
                        {
                            pendingApiResponse.DialogueContent = currentDialogueText;
                            ApplyRPGAPIAndShowPopup(pendingApiResponse);
                            pendingApiResponse = null;
                        }
                        
                        dialogPages.Add(new DialoguePage { speakerName = target.LabelShort, text = aiResponseText });
                    }
                }
            }

            Rect boxRect = new Rect(0, inRect.height - DialogueBoxHeight, inRect.width, DialogueBoxHeight);
            
            Widgets.DrawBoxSolid(boxRect, DialogueBoxColor);
            GUI.color = new Color(0.3f, 0.3f, 0.35f, 1f);
            Widgets.DrawBox(boxRect, 2);
            GUI.color = Color.white;

            Rect contentRect = boxRect.ContractedBy(35f);
            
            bool drawLive = !isViewingHistory;
            string renderSpeaker = drawLive ? currentSpeakerName : dialogPages[historyViewIndex].speakerName;
            string renderText = drawLive ? currentDialogueText : dialogPages[historyViewIndex].text;

            // Speaker Name Header (Huge Tag using Rich Text)
            if (renderSpeaker == initiator.LabelShort)
            {
                Text.Anchor = TextAnchor.UpperRight;
                Rect nameRectRight = new Rect(contentRect.xMax - 600f, contentRect.y - 35f, 600f, 55f);
                Widgets.Label(nameRectRight, $"<size=44><b><color=#e0e0e0>{renderSpeaker}</color></b></size>");
                Text.Anchor = TextAnchor.UpperLeft;
            }
            else
            {
                Rect nameRectLeft = new Rect(contentRect.x, contentRect.y - 35f, 600f, 55f);
                Widgets.Label(nameRectLeft, $"<size=44><b><color=#e0e0e0>{renderSpeaker}</color></b></size>");
            }

            // Text Label Box
            Rect textArea = new Rect(contentRect.x, contentRect.y + 20f, contentRect.width, contentRect.height - 70f);
            
            // If the player is speaking, set right alignment by adjusting Rect
            if (renderSpeaker == initiator.LabelShort)
            {
                string calcText = drawLive ? currentDialogueText : renderText;
                // Strip tags for accurate measurement
                string measureText = System.Text.RegularExpressions.Regex.Replace(calcText, "<.*?>", "");
                
                GameFont prevFont = Text.Font;
                Text.Font = GameFont.Medium;
                Vector2 size = Text.CalcSize(measureText);
                Text.Font = prevFont;
                
                // Scale factor: size=34 is ~1.5x of Medium. Add buffer to prevent wrap-around 'two columns' issue.
                float clampedWidth = Mathf.Min(size.x * 1.6f + 40f, contentRect.width * 0.85f);
                
                textArea.x = contentRect.xMax - clampedWidth;
                textArea.width = clampedWidth;
                // Use UpperLeft to maintain steady 'left-to-right' typing without text jumping
                Text.Anchor = TextAnchor.UpperLeft;
            }
            
            if (drawLive)
            {
                if (isSendingInitialMessage)
                {
                    string dots = new string('.', (int)(Time.time * 2) % 4);
                    Widgets.Label(textArea, $"<size=34><color=#b0b0b0>{BuildRpgThinkingText(dots)}</color></size>");
                }
                else if (isShowingUserText && isWaitingForDelayAfterUser && !aiResponseReady && Time.realtimeSinceStartup - timeUserTextFinished >= 3.0f)
                {
                    // The player text fully printed, delayed 3s, waiting for AI.
                    string dots = new string('.', (int)(Time.time * 2) % 4);
                    Widgets.Label(textArea, $"<size=34>{displayedText}\n<color=#b0b0b0>{BuildRpgOpponentThinkingText(dots)}</color></size>");
                }
                else
                {
                    UpdateTyping();
                    string liveText = ResolveDialogueTextForDisplay(drawLive, renderSpeaker, currentDialogueText, textArea);
                    string visibleText = isTyping ? displayedText : liveText;
                    Widgets.Label(textArea, $"<size=34>{visibleText}</size>");
                }
            }
            else
            {
                string historyText = ResolveDialogueTextForDisplay(drawLive, renderSpeaker, renderText, textArea);
                Widgets.Label(textArea, $"<size=34>{historyText}</size>");
            }

            // Restore anchor
            if (renderSpeaker == initiator.LabelShort)
            {
                Text.Anchor = TextAnchor.UpperLeft;
            }
            
            // Input Mode Display
            if (!isTyping && !isSendingInitialMessage && !isShowingUserText && drawLive && !isDialogueEndedByNpc)
            {
                float inputHeight = 45f;
                Rect bottomArea = new Rect(contentRect.x, contentRect.yMax - inputHeight, contentRect.width, inputHeight);
                
                // Update dynamic alpha for animation
                // Stay fully visible if either mouse is over OR if the input field has focus
                bool isFocused = GUI.GetNameOfFocusedControl() == UserReplyInputControlName;
                bool mouseInBottom = Mouse.IsOver(bottomArea);
                float targetAlpha = (mouseInBottom || isFocused) ? 1.0f : 0.25f;
                // Use Real-time delta for smooth transition regardless of frame rate
                inputAlpha = Mathf.Lerp(inputAlpha, targetAlpha, 0.12f);

                GUI.color = new Color(1f, 1f, 1f, inputAlpha);
                
                Rect inputRect = new Rect(bottomArea.x, bottomArea.y, bottomArea.width - 150f, inputHeight);
                
                // Draw a more subtle background for the input if not active
                if (inputAlpha < 0.9f) {
                    Widgets.DrawBoxSolid(inputRect, new Color(1f, 1f, 1f, 0.05f));
                }
                
                GUI.SetNextControlName(UserReplyInputControlName);
                if (ShouldSendFromKeyboard(Event.current))
                {
                    Event.current.Use();
                    TrySendMessage();
                }
                userReplyText = Widgets.TextField(inputRect, userReplyText);
                
                Rect sendRect = new Rect(bottomArea.xMax - 135f, bottomArea.y, 135f, inputHeight);
                string sendLabel = "RimChat_SendButton".Translate();
                
                // Custom-styled button for 'inconspicuous' look
                Color savedGuiColor = GUI.color;
                if (inputAlpha < 0.5f) {
                    // Just draw text when alpha is low
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(sendRect, sendLabel);
                    Text.Anchor = TextAnchor.UpperLeft;
                    if (Widgets.ButtonInvisible(sendRect)) TrySendMessage();
                } else {
                    if (Widgets.ButtonText(sendRect, sendLabel)) TrySendMessage();
                }

                DrawRpgPotentialActionsHint(sendRect, inputAlpha);

                GUI.color = Color.white;
            }
            else if (!isTyping && !isSendingInitialMessage && !isShowingUserText && drawLive && isDialogueEndedByNpc)
            {
                Rect blockedRect = new Rect(contentRect.x, contentRect.yMax - 42f, contentRect.width, 32f);
                GUI.color = new Color(0.95f, 0.55f, 0.55f, 0.95f);
                string blockText = string.IsNullOrEmpty(dialogueEndReason)
                    ? "RimChat_RPGDialogue_EndedByNpc".Translate()
                    : "RimChat_RPGDialogue_EndedByNpcReason".Translate(dialogueEndReason);
                Widgets.Label(blockedRect, blockText);
                GUI.color = Color.white;
            }
            
            DrawDialogueNavigation(boxRect);
        }

        private bool ShouldSendFromKeyboard(Event current)
        {
            if (!IsSubmitKeyPressed(current) || current.alt || IsImeComposing())
            {
                return false;
            }

            if (!IsUserReplyInputFocused())
            {
                return false;
            }

            return CanSendUserReplyFromKeyboard();
        }

        private static bool IsSubmitKeyPressed(Event current)
        {
            if (current == null)
            {
                return false;
            }

            if (current.keyCode != KeyCode.Return && current.keyCode != KeyCode.KeypadEnter)
            {
                return false;
            }

            return current.type == EventType.KeyDown || current.rawType == EventType.KeyDown;
        }

        private static bool IsImeComposing()
        {
            return !string.IsNullOrEmpty(Input.compositionString);
        }

        private static bool IsUserReplyInputFocused()
        {
            return GUI.GetNameOfFocusedControl() == UserReplyInputControlName;
        }

        private bool CanSendUserReplyFromKeyboard()
        {
            return !isDialogueEndedByNpc && !string.IsNullOrWhiteSpace(userReplyText);
        }

        private void TrySendMessage()
        {
            if (isDialogueEndedByNpc)
            {
                return;
            }

            var rpgManager = Current.Game?.GetComponent<RimChat.DiplomacySystem.GameComponent_RPGManager>();
            if (rpgManager != null && rpgManager.IsRpgDialogueOnCooldown(target, out int remainingTicks))
            {
                float remainingHours = Math.Max(0f, remainingTicks / 2500f);
                string cooldownText = "RimChat_RPGDialogue_CooldownBlockedWithHours".Translate(remainingHours.ToString("F1"));
                Messages.Message(
                    cooldownText,
                    MessageTypeDefOf.RejectInput,
                    false);
                isDialogueEndedByNpc = true;
                dialogueEndReason = cooldownText;
                return;
            }

            if (!string.IsNullOrWhiteSpace(userReplyText))
            {
                string textToSend = userReplyText.Trim();
                chatHistory.Add(new ChatMessageData { role = "user", content = textToSend });
                dialogPages.Add(new DialoguePage { speakerName = initiator.LabelShort, text = textToSend });
                RpgDialogueTraceTracker.RegisterTurn(initiator, target, true, textToSend, dialogueSessionId);
                userReplyText = "";
                GUI.FocusControl(null); // Release focus so it can fade out
                
                isViewingHistory = false; // Snap back to live mode
                ResetDialogueTextPaging();
                
                // Switch to User typing mode
                currentSpeakerName = initiator.LabelShort;
                currentDialogueText = textToSend;
                displayedText = "";
                visibleChars = 0;
                
                isTyping = true;
                isShowingUserText = true;
                isWaitingForDelayAfterUser = false;
                
                aiResponseReady = false;
                aiResponseText = "";
                
                // Request background
                AIChatServiceAsync.Instance.SendChatRequestAsync(
                    BuildCompressedRpgRequestMessages(),
                    onSuccess: (response) =>
                    {
                        if (RimChatMod.Settings.EnableRPGAPI)
                        {
                            pendingApiResponse = LLMRpgApiResponse.Parse(response);
                            pendingApiResponse.DialogueContent = NormalizeVisibleNpcDialogueText(pendingApiResponse.DialogueContent);
                            aiResponseText = pendingApiResponse.DialogueContent;
                        }
                        else
                        {
                            aiResponseText = NormalizeVisibleNpcDialogueText(response);
                        }

                        aiResponseReady = true;
                        string visibleHistoryContent = NormalizeHistoryAssistantContent(response, aiResponseText);
                        chatHistory.Add(new ChatMessageData { role = "assistant", content = visibleHistoryContent });
                        RpgDialogueTraceTracker.RegisterTurn(initiator, target, false, aiResponseText, dialogueSessionId);
                        if (pendingApiResponse != null)
                        {
                            EnsureRpgActionFallbacks(pendingApiResponse);
                        }
                    },
                    onError: (error) =>
                    {
                        aiResponseReady = true;
                        aiResponseText = "Error: " + error;
                    },
                    usageChannel: DialogueUsageChannel.Rpg,
                    debugSource: AIRequestDebugSource.RpgDialogue
                );
            }
        }

        private List<ChatMessageData> BuildCompressedRpgRequestMessages()
        {
            var request = new List<ChatMessageData>();
            bool openingTurn = !HasVisibleAssistantReply(chatHistory);
            string currentTurnUserIntent = ExtractLatestVisibleUserIntent(chatHistory);
            request.Add(new ChatMessageData
            {
                role = "system",
                content = BuildRpgSystemPromptForRequest(openingTurn, currentTurnUserIntent)
            });
            request.Add(new ChatMessageData
            {
                role = "user",
                content = RpgStrictOutputContractReminder
            });
            List<ChatMessageData> conversation = chatHistory
                .Where(message => !IsSystemRole(message?.role))
                .ToList();
            request.AddRange(DialogueContextCompressionService.BuildFromChatMessages(conversation));
            if (openingTurn && conversation.Count == 0 && !string.IsNullOrWhiteSpace(currentTurnUserIntent))
            {
                request.Add(new ChatMessageData
                {
                    role = "user",
                    content = currentTurnUserIntent
                });
            }
            return request;
        }

        private static bool IsSystemRole(string role)
        {
            return string.Equals(role, "system", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildRpgThinkingText(string dots)
        {
            return "RimChat_RPGThinking".Translate(dots);
        }

        private static string BuildRpgOpponentThinkingText(string dots)
        {
            return "RimChat_RPGOpponentThinking".Translate(dots);
        }

        private void UpdateTyping()
        {
            if (isTyping && visibleChars < currentDialogueText.Length)
            {
                float interval = 0.02f;
                if (Time.realtimeSinceStartup - lastCharTime > interval)
                {
                    visibleChars++;
                    
                    // Skip rich text tags <...> instantaneously
                    if (visibleChars < currentDialogueText.Length && currentDialogueText[visibleChars - 1] == '<')
                    {
                        int closeTagIndex = currentDialogueText.IndexOf('>', visibleChars - 1);
                        if (closeTagIndex != -1)
                        {
                            visibleChars = closeTagIndex + 1;
                        }
                    }

                    displayedText = currentDialogueText.Substring(0, Mathf.Min(visibleChars, currentDialogueText.Length));
                    lastCharTime = Time.realtimeSinceStartup;
                    
                    if (visibleChars % 3 == 0)
                    {
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    }
                }
                
                if (visibleChars >= currentDialogueText.Length)
                {
                    isTyping = false;
                    
                    // Trigger player pawn fade-in when target's first sentence is done
                    if (!firstTargetSentenceDone && currentSpeakerName == target.LabelShort)
                    {
                        firstTargetSentenceDone = true;
                    }

                    if (isShowingUserText)
                    {
                        isWaitingForDelayAfterUser = true;
                        timeUserTextFinished = Time.realtimeSinceStartup;
                    }
                }
            }
        }

    }
}
