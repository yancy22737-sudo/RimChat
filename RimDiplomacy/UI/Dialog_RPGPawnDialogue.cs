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
    public class Dialog_RPGPawnDialogue : Window
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
            
            var settings = RimDiplomacyMod.Settings;
            string systemPrompt = settings.RPGSystemPrompt;
            if (string.IsNullOrEmpty(systemPrompt))
            {
                systemPrompt = "You are playing a role-playing game. You are the character " + target.LabelShort + ". Keep responses concise and immersive.";
            }
            
            messages.Add(new ChatMessageData { role = "system", content = systemPrompt });
            messages.Add(new ChatMessageData { role = "user", content = "Initiate conversation with me." });
            
            return messages;
        }

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
                    currentDialogueText = response;
                    chatHistory.Add(new ChatMessageData { role = "assistant", content = response });
                    dialogPages.Add(new DialoguePage { speakerName = target.LabelShort, text = response });
                    isTyping = true;
                    lastCharTime = Time.realtimeSinceStartup;
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
            // Background is completely transparent now

            // Draw Portraits first, so it lives behind the dialogue box at the bottom
            DrawPortraits(inRect);

            // Draw Dialogue Box
            DrawDialogueBox(inRect);

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
            }
        }

        private void DrawPortraits(Rect inRect)
        {
            // Position portraits deeply into dialogue box
            float overlap = 150f;
            float yPos = inRect.height - DialogueBoxHeight - PortraitHeight + overlap;
            
            // Target (Left) - Now Target NPC is on the left
            Rect targetRect = new Rect(50f, yPos, PortraitWidth, PortraitHeight);
            DrawPawnPortrait(targetRect, target, false);

            // Initiator (Right) - Now Player Pawn is on the right
            Rect initiatorRect = new Rect(inRect.width - PortraitWidth - 50f, yPos, PortraitWidth, PortraitHeight);
            DrawPawnPortrait(initiatorRect, initiator, true);
        }

        private void DrawPawnPortrait(Rect rect, Pawn pawn, bool flip)
        {
            if (pawn == null) return;

            // Camera parameters to zoom and shift upwards slightly for RPG feel
            Vector2 size = new Vector2(rect.width, rect.height);
            Vector3 cameraOffset = new Vector3(0f, 0f, 0.15f);
            float zoom = 1.35f;

            RenderTexture portrait = PortraitsCache.Get(pawn, size, Rot4.South, cameraOffset, zoom, true, true, true, true, null, null, false);

            if (portrait != null)
            {
                if (flip)
                {
                    // Horizontal flip using GUI.matrix
                    Matrix4x4 savedMatrix = GUI.matrix;
                    Vector2 center = rect.center;
                    GUIUtility.ScaleAroundPivot(new Vector2(-1f, 1f), center);
                    GUI.DrawTexture(rect, portrait, ScaleMode.ScaleToFit, true);
                    GUI.matrix = savedMatrix;
                }
                else
                {
                    GUI.DrawTexture(rect, portrait, ScaleMode.ScaleToFit, true);
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
            
            // If the player is speaking, dynamically calculate text width to align the text block to the right
            if (renderSpeaker == initiator.LabelShort)
            {
                string calcText = drawLive ? currentDialogueText : renderText;
                GUIContent content = new GUIContent($"<size=34>{calcText}</size>");
                GUIStyle style = new GUIStyle(Text.CurFontStyle);
                style.richText = true;
                style.wordWrap = true;
                
                float maxAllowedWidth = contentRect.width * 0.85f;
                float widthUnbounded = style.CalcSize(content).x;
                float clampedWidth = Mathf.Min(widthUnbounded, maxAllowedWidth);
                
                textArea.x = contentRect.xMax - clampedWidth;
                textArea.width = clampedWidth;
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
            
            // Input Mode Display
            if (!isTyping && !isSendingInitialMessage && !isShowingUserText && drawLive)
            {
                float inputHeight = 40f;
                Rect bottomArea = new Rect(contentRect.x, contentRect.yMax - inputHeight, contentRect.width, inputHeight);
                
                Rect inputRect = new Rect(bottomArea.x, bottomArea.y, bottomArea.width - 150f, inputHeight);
                
                GUI.SetNextControlName("UserReplyInput");
                userReplyText = Widgets.TextField(inputRect, userReplyText);
                
                Rect sendRect = new Rect(bottomArea.xMax - 135f, bottomArea.y, 135f, inputHeight);
                
                // Allow pressing enter key or hitting button to send message
                bool enterPressed = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return;
                
                if (Widgets.ButtonText(sendRect, "发送") || enterPressed)
                {
                    if (!enterPressed || GUI.GetNameOfFocusedControl() == "UserReplyInput")
                    {
                        TrySendMessage();
                        if (enterPressed)
                        {
                            Event.current.Use();
                        }
                    }
                }
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
            if (!string.IsNullOrWhiteSpace(userReplyText))
            {
                string textToSend = userReplyText.Trim();
                chatHistory.Add(new ChatMessageData { role = "user", content = textToSend });
                dialogPages.Add(new DialoguePage { speakerName = initiator.LabelShort, text = textToSend });
                userReplyText = "";
                
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
                        aiResponseReady = true;
                        aiResponseText = response;
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
