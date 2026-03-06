using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimDiplomacy.AI;
using RimDiplomacy.Config;
using RimDiplomacy.Util;
using RimDiplomacy.Core;

namespace RimDiplomacy.UI
{
    public partial class Dialog_RPGPawnDialogue : Window
    {
        private readonly Pawn initiator;
        private readonly Pawn target;
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

        // NPC离开会话后，进入冷却拒聊
        private bool isDialogueEndedByNpc = false;
        private string dialogueEndReason = "";
        
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

        public override Vector2 InitialSize => new Vector2(Verse.UI.screenWidth, Verse.UI.screenHeight);
        protected override float Margin => 0f;

        public Dialog_RPGPawnDialogue(Pawn initiator, Pawn target)
        {
            this.initiator = initiator;
            this.target = target;
            this.doCloseX = false;
            this.doCloseButton = false;
            this.closeOnClickedOutside = false;
            this.absorbInputAroundWindow = true;
            this.forcePause = true;
            this.preventCameraMotion = true;
            this.doWindowBackground = false;
            
            // Start initial conversation
            chatHistory = BuildRPGChatMessages();
            SendInitialMessage();
        }

        private List<ChatMessageData> BuildRPGChatMessages()
        {
            var messages = new List<ChatMessageData>();
            
            // 使用提示词持久化服务构建完整的系统提示词（包含动态数据注入）
            string systemPrompt = RimDiplomacy.Persistence.PromptPersistenceService.Instance.BuildRPGFullSystemPrompt(initiator, target);
            
            messages.Add(new ChatMessageData { role = "system", content = systemPrompt });
            messages.Add(new ChatMessageData { role = "user", content = "Initiate conversation with me." });
            
            return messages;
        }

        private float inputAlpha = 0.3f;

        private void SendInitialMessage()
        {
            isSendingInitialMessage = true;
            currentDialogueText = "";
            displayedText = "";
            visibleChars = 0;
            currentSpeakerName = target.LabelShort;

            AIChatServiceAsync.Instance.SendChatRequestAsync(
                chatHistory,
                onSuccess: (response) =>
                {
                    isSendingInitialMessage = false;
                    
                    if (RimDiplomacyMod.Settings.EnableRPGAPI)
                    {
                        pendingApiResponse = LLMRpgApiResponse.Parse(response);
                        EnsureRpgExitActionFallback(pendingApiResponse);
                        currentDialogueText = pendingApiResponse.DialogueContent;
                    }
                    else
                    {
                        currentDialogueText = response;
                    }

                    chatHistory.Add(new ChatMessageData { role = "assistant", content = response });
                    dialogPages.Add(new DialoguePage { speakerName = target.LabelShort, text = currentDialogueText });
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
                }
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
                        if (GUI.GetNameOfFocusedControl() == "UserReplyInput")
                        {
                            GUI.FocusControl(null);
                        }
                    }
                }
            }
        }

        private void DrawPortraits(Rect inRect)
        {
            // Position portraits deeply into dialogue box
            float overlap = 150f;
            float yPos = inRect.height - DialogueBoxHeight - PortraitHeight + overlap;
            
            // Target (Left) - Now Target NPC is on the left
            Rect targetRect = new Rect(50f, yPos, PortraitWidth, PortraitHeight);
            GUI.color = new Color(1f, 1f, 1f, globalFadeAlpha * targetFadeAlpha);
            DrawPawnPortrait(targetRect, target, false);

            // Initiator (Right) - Now Player Pawn is on the right
            Rect initiatorRect = new Rect(inRect.width - PortraitWidth - 50f, yPos, PortraitWidth, PortraitHeight);
            GUI.color = new Color(1f, 1f, 1f, globalFadeAlpha * initiatorFadeAlpha);
            DrawPawnPortrait(initiatorRect, initiator, true);
            
            GUI.color = new Color(1f, 1f, 1f, globalFadeAlpha);
        }

        private RenderTexture initiatorRT;
        private RenderTexture targetRT;

        public override void PreClose()
        {
            base.PreClose();
            if (initiatorRT != null) { UnityEngine.Object.Destroy(initiatorRT); initiatorRT = null; }
            if (targetRT != null) { UnityEngine.Object.Destroy(targetRT); targetRT = null; }
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
                        currentDialogueText = aiResponseText;
                        displayedText = "";
                        visibleChars = 0;
                        isTyping = true;
                        lastCharTime = Time.realtimeSinceStartup;
                        
                        if (pendingApiResponse != null)
                        {
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
            string renderText = drawLive ? "" : dialogPages[historyViewIndex].text;

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
                    Widgets.Label(textArea, $"<size=34><color=#b0b0b0>正在思考{dots}</color></size>");
                }
                else if (isShowingUserText && isWaitingForDelayAfterUser && !aiResponseReady && Time.realtimeSinceStartup - timeUserTextFinished >= 3.0f)
                {
                    // The player text fully printed, delayed 3s, waiting for AI.
                    string dots = new string('.', (int)(Time.time * 2) % 4);
                    Widgets.Label(textArea, $"<size=34>{displayedText}\n<color=#b0b0b0>对方正在思考{dots}</color></size>");
                }
                else
                {
                    UpdateTyping();
                    Widgets.Label(textArea, $"<size=34>{displayedText}</size>");
                }
            }
            else
            {
                Widgets.Label(textArea, $"<size=34>{renderText}</size>");
            }

            DrawActionFeedback(contentRect);

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
                bool isFocused = GUI.GetNameOfFocusedControl() == "UserReplyInput";
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
                
                GUI.SetNextControlName("UserReplyInput");
                userReplyText = Widgets.TextField(inputRect, userReplyText);
                
                Rect sendRect = new Rect(bottomArea.xMax - 135f, bottomArea.y, 135f, inputHeight);
                
                // Custom-styled button for 'inconspicuous' look
                Color savedGuiColor = GUI.color;
                if (inputAlpha < 0.5f) {
                    // Just draw text when alpha is low
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(sendRect, "发送");
                    Text.Anchor = TextAnchor.UpperLeft;
                    if (Widgets.ButtonInvisible(sendRect)) TrySendMessage();
                } else {
                    if (Widgets.ButtonText(sendRect, "发送")) TrySendMessage();
                }
                
                // Allow pressing enter key to send message
                if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
                {
                    if (GUI.GetNameOfFocusedControl() == "UserReplyInput")
                    {
                        TrySendMessage();
                        Event.current.Use();
                    }
                }

                GUI.color = Color.white;
            }
            else if (!isTyping && !isSendingInitialMessage && !isShowingUserText && drawLive && isDialogueEndedByNpc)
            {
                Rect blockedRect = new Rect(contentRect.x, contentRect.yMax - 42f, contentRect.width, 32f);
                GUI.color = new Color(0.95f, 0.55f, 0.55f, 0.95f);
                string blockText = string.IsNullOrEmpty(dialogueEndReason)
                    ? "RimDiplomacy_RPGDialogue_EndedByNpc".Translate()
                    : "RimDiplomacy_RPGDialogue_EndedByNpcReason".Translate(dialogueEndReason);
                Widgets.Label(blockedRect, blockText);
                GUI.color = Color.white;
            }
            
            // Draw History Navigation Buttons at bottom right corner
            if (dialogPages.Count > 0)
            {
                int maxDisplayCount = dialogPages.Count;
                int currentDisplay = isViewingHistory ? historyViewIndex : (maxDisplayCount - 1);
                
                Rect historyBox = new Rect(boxRect.xMax - 110f, boxRect.yMax - 30f, 100f, 25f);
                bool mouseOverHist = Mouse.IsOver(historyBox);
                
                GUI.color = mouseOverHist ? new Color(0.9f, 0.9f, 0.9f, 0.9f) : new Color(0.5f, 0.5f, 0.5f, 0.4f);
                
                Rect prevRect = new Rect(historyBox.x, historyBox.y, 30f, 25f);
                Rect countRect = new Rect(historyBox.x + 30f, historyBox.y, 40f, 25f);
                Rect nextRect = new Rect(historyBox.x + 70f, historyBox.y, 30f, 25f);
                
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                
                if (currentDisplay > 0)
                {
                    if (Widgets.ButtonInvisible(prevRect)) 
                    { 
                        historyViewIndex = currentDisplay - 1; 
                        isViewingHistory = true; 
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera(); 
                    }
                    Widgets.Label(prevRect, "<");
                }
                
                Widgets.Label(countRect, $"{currentDisplay + 1}/{maxDisplayCount}");
                
                if (currentDisplay < maxDisplayCount - 1)
                {
                    if (Widgets.ButtonInvisible(nextRect)) 
                    { 
                        historyViewIndex = currentDisplay + 1; 
                        if (historyViewIndex == maxDisplayCount - 1) isViewingHistory = false;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera(); 
                    }
                    Widgets.Label(nextRect, ">");
                }
                
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }
        }

        private void TrySendMessage()
        {
            if (isDialogueEndedByNpc)
            {
                return;
            }

            var rpgManager = Current.Game?.GetComponent<RimDiplomacy.DiplomacySystem.GameComponent_RPGManager>();
            if (rpgManager != null && rpgManager.IsRpgDialogueOnCooldown(target, out int remainingTicks))
            {
                Messages.Message(
                    "RimDiplomacy_RPGDialogue_CooldownBlocked".Translate(),
                    MessageTypeDefOf.RejectInput,
                    false);
                isDialogueEndedByNpc = true;
                return;
            }

            if (!string.IsNullOrWhiteSpace(userReplyText))
            {
                string textToSend = userReplyText.Trim();
                chatHistory.Add(new ChatMessageData { role = "user", content = textToSend });
                dialogPages.Add(new DialoguePage { speakerName = initiator.LabelShort, text = textToSend });
                userReplyText = "";
                GUI.FocusControl(null); // Release focus so it can fade out
                
                isViewingHistory = false; // Snap back to live mode
                
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
                    chatHistory,
                    onSuccess: (response) =>
                    {
                        if (RimDiplomacyMod.Settings.EnableRPGAPI)
                        {
                            pendingApiResponse = LLMRpgApiResponse.Parse(response);
                            EnsureRpgExitActionFallback(pendingApiResponse);
                            aiResponseText = pendingApiResponse.DialogueContent;
                        }
                        else
                        {
                            aiResponseText = response;
                        }
                        
                        aiResponseReady = true;
                        chatHistory.Add(new ChatMessageData { role = "assistant", content = response });
                    },
                    onError: (error) =>
                    {
                        aiResponseReady = true;
                        aiResponseText = "Error: " + error;
                    }
                );
            }
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
