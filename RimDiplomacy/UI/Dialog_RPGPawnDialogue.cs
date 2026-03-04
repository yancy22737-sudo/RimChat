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
        private LLMRpgApiResponse pendingApiResponse = null;
        
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
            
            // 使用提示词持久化服务构建完整的系统提示词（包含动态数据注入）
            string systemPrompt = RimDiplomacy.Persistence.PromptPersistenceService.Instance.BuildRPGFullSystemPrompt(initiator, target);
            
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
                    
                    if (RimDiplomacyMod.Settings.EnableRPGAPI)
                    {
                        pendingApiResponse = LLMRpgApiResponse.Parse(response);
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

            // Restore anchor
            if (renderSpeaker == initiator.LabelShort)
            {
                Text.Anchor = TextAnchor.UpperLeft;
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
                        if (RimDiplomacyMod.Settings.EnableRPGAPI)
                        {
                            pendingApiResponse = LLMRpgApiResponse.Parse(response);
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
                    
                    if (isShowingUserText)
                    {
                        isWaitingForDelayAfterUser = true;
                        timeUserTextFinished = Time.realtimeSinceStartup;
                    }
                }
            }
        }

        private void ApplyRPGAPIAndShowPopup(LLMRpgApiResponse apiRes)
        {
            var rpgMan = Current.Game.GetComponent<RimDiplomacy.DiplomacySystem.GameComponent_RPGManager>();
            if (rpgMan != null)
            {
                var rel = rpgMan.GetOrCreateRelation(target);
                rel.UpdateFromLLM(apiRes.FavorabilityDelta, apiRes.TrustDelta, apiRes.FearDelta, apiRes.RespectDelta, apiRes.DependencyDelta);
            }
            
            // Execute Actions
            List<string> appliedMessages = new List<string>();
            foreach (var act in apiRes.Actions)
            {
                try {
                    if (act.action == "TryGainMemory" && !string.IsNullOrEmpty(act.defName))
                    {
                        ThoughtDef def = DefDatabase<ThoughtDef>.GetNamedSilentFail(act.defName);
                        if (def != null && target.needs?.mood?.thoughts?.memories != null) {
                            target.needs.mood.thoughts.memories.TryGainMemory(def, initiator);
                            appliedMessages.Add($"NPC获得了心情: {def.label}");
                        }
                    }
                    else if (act.action == "TryAffectSocialGoodwill")
                    {
                        if (target.Faction != null && initiator.Faction != null)
                        {
                            target.Faction.TryAffectGoodwillWith(initiator.Faction, act.amount, true, true, null);
                            appliedMessages.Add($"派系关系变更: {act.amount}");
                        }
                    }
                    else if (act.action == "ReduceResistance" && target.IsPrisoner && target.guest != null)
                    {
                        target.guest.resistance = Math.Max(0, target.guest.resistance - act.amount);
                        appliedMessages.Add($"招募抵抗度减少: {act.amount}");
                    }
                    else if (act.action == "ReduceWill" && target.IsPrisoner && target.guest != null)
                    {
                        target.guest.will = Math.Max(0, target.guest.will - act.amount);
                        appliedMessages.Add($"奴役意志减少: {act.amount}");
                    }
                    else if (act.action == "Recruit")
                    {
                        if (target.Faction != initiator.Faction)
                        {
                            RecruitUtility.Recruit(target, initiator.Faction, initiator);
                            appliedMessages.Add("成功招募NPC！");
                        }
                    }
                    else if (act.action == "TryTakeOrderedJob")
                    {
                        if (act.defName == "AttackMelee")
                        {
                            Verse.AI.Job attackJob = new Verse.AI.Job(JobDefOf.AttackMelee, initiator);
                            target.jobs?.TryTakeOrderedJob(attackJob, Verse.AI.JobTag.Misc);
                            appliedMessages.Add("NPC发起了攻击指令！");
                        }
                    }
                } catch { } // avoid UI crash inside API actions
            }

            if (apiRes.FavorabilityDelta != 0 || apiRes.TrustDelta != 0 || apiRes.FearDelta != 0 || apiRes.RespectDelta != 0 || apiRes.DependencyDelta != 0) {
                appliedMessages.Insert(0, $"五维属性波动: \n好感:{apiRes.FavorabilityDelta:F1} 信任:{apiRes.TrustDelta:F1} 恐惧:{apiRes.FearDelta:F1} 尊重:{apiRes.RespectDelta:F1} 依赖:{apiRes.DependencyDelta:F1}");
            }

            if (appliedMessages.Count > 0)
            {
                string title = "RimDiplomacy_RPGApiAppliedTitle".Translate();
                string summary = "RimDiplomacy_RPGApiAppliedDesc".Translate(string.Join("\n", appliedMessages));
                Find.WindowStack.Add(new Dialog_MessageBox(summary, "RimDiplomacy_NewsUnderstand".Translate(), null, null, null, title));
            }
        }
    }
}
