using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimDiplomacy.UI
{
    [StaticConstructorOnStartup]
    public class Dialog_DiplomacyDialogue : Window
    {
        private readonly Faction faction;
        private FactionDialogueSession session;
        private string inputText = "";
        private Vector2 messageScrollPosition = Vector2.zero;
        private Vector2 factionScrollPosition = Vector2.zero;
        private int lastMessageCount = 0;
        private bool userIsScrolling = false;
        private bool waitingForAIResponse = false;
        private float aiRequestProgress = 0f;
        private string aiError = null;
        private const int MAX_INPUT_LENGTH = 500;
        private const float FACTION_LIST_WIDTH = 220f;
        private const float INPUT_AREA_HEIGHT = 80f;
        private const float TIME_GAP_THRESHOLD_MINUTES = 15f;
        private const float BUBBLE_CORNER_RADIUS = 12f;

        // 玩家消息气泡颜色 #91ed61
        private static readonly Color PlayerBubbleColor = new Color(0.569f, 0.929f, 0.38f, 0.95f);
        private static readonly Color PlayerBubbleColorDark = new Color(0.51f, 0.85f, 0.33f, 0.95f);
        // AI消息气泡颜色
        private static readonly Color AIBubbleColor = new Color(0.25f, 0.26f, 0.3f, 0.95f);

        // 派系位置映射（用于动画定位）
        private readonly Dictionary<Faction, Rect> factionRowRects = new Dictionary<Faction, Rect>();

        public override Vector2 InitialSize => new Vector2(900f, 700f);

        public Dialog_DiplomacyDialogue(Faction faction)
        {
            this.faction = faction;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
            onlyOneOfTypeAllowed = false;

            session = GameComponent_DiplomacyManager.Instance?.GetOrCreateSession(faction);
            if (session != null)
            {
                session.MarkAsRead();
            }

            // 订阅好感度变化事件
            GoodwillChangeAnimator.OnGoodwillChanged += OnGoodwillChanged;

            Log.Message($"[RimDiplomacy] Dialogue opened with {faction.Name}, messages: {session?.messages.Count ?? 0}, AI configured: {AIChatService.Instance.IsConfigured()}");
        }

        public override void PreClose()
        {
            base.PreClose();
            // 取消订阅事件
            GoodwillChangeAnimator.OnGoodwillChanged -= OnGoodwillChanged;
        }

        /// <summary>
        /// 好感度变化事件处理
        /// </summary>
        private void OnGoodwillChanged(Faction changedFaction, int changeAmount)
        {
            if (changedFaction == null) return;

            // 查找派系在列表中的位置
            if (factionRowRects.TryGetValue(changedFaction, out Rect rowRect))
            {
                // 计算动画起始位置（在好感度数值附近）
                Vector2 startPos = new Vector2(
                    rowRect.x + 63f,
                    rowRect.y + 32f
                );

                // 创建动画
                GoodwillChangeAnimator.CreateAnimation(changedFaction, changeAmount, startPos);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            DrawTitleBar(inRect);

            float contentY = 45f;
            float contentHeight = inRect.height - contentY - 10f;

            Rect factionListRect = new Rect(inRect.x, inRect.y + contentY, FACTION_LIST_WIDTH, contentHeight);
            DrawFactionList(factionListRect);

            Rect chatRect = new Rect(inRect.x + FACTION_LIST_WIDTH + 10f, inRect.y + contentY,
                inRect.width - FACTION_LIST_WIDTH - 10f, contentHeight);
            DrawChatArea(chatRect);

            // 绘制好感度变化动画（在所有UI之上）
            GoodwillChangeAnimator.UpdateAndDrawAnimations();
        }

        private void DrawTitleBar(Rect inRect)
        {
            Widgets.DrawBoxSolid(new Rect(inRect.x, inRect.y, inRect.width, 40f), new Color(0.15f, 0.15f, 0.18f));
            
            // 左侧标题：RimChat
            Text.Font = GameFont.Medium;
            GUI.color = new Color(0.9f, 0.9f, 0.95f);
            string title = "RimChat";
            Widgets.Label(new Rect(inRect.x + 15f, inRect.y + 8f, 120f, 30f), title);

            // 中间：当前派系名称
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.7f, 0.7f, 0.75f);
            string factionTitle = faction.Name ?? "Unknown";
            float factionTitleWidth = Text.CalcSize(factionTitle).x;
            float centerX = inRect.x + (inRect.width - factionTitleWidth) / 2f;
            Widgets.Label(new Rect(centerX, inRect.y + 10f, factionTitleWidth + 10f, 25f), factionTitle);

            // 右侧：天气和时间
            string weatherTimeText = GetWeatherAndTimeText();
            float weatherTimeWidth = Text.CalcSize(weatherTimeText).x;
            GUI.color = new Color(0.8f, 0.8f, 0.85f);
            Widgets.Label(new Rect(inRect.xMax - weatherTimeWidth - 45f, inRect.y + 10f, weatherTimeWidth + 10f, 25f), weatherTimeText);

            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            Rect closeRect = new Rect(inRect.xMax - 35f, inRect.y + 5f, 30f, 30f);
            GUI.color = new Color(0.8f, 0.3f, 0.3f, 0.8f);
            if (Widgets.ButtonText(closeRect, "×"))
            {
                Close();
            }
            GUI.color = Color.white;
        }

        private string GetWeatherAndTimeText()
        {
            var map = Find.CurrentMap;
            if (map == null) return "";

            // 获取温度
            float temperature = map.mapTemperature?.OutdoorTemp ?? 0f;
            string tempText = $"{temperature:F0}°C";

            // 获取游戏时间
            int hour = GenLocalDate.HourOfDay(map);
            int minute = (int)((GenLocalDate.DayPercent(map) * 24f - hour) * 60f);
            string timeText = $"{hour:D2}:{minute:D2}";

            return $"{tempText}  {timeText}";
        }

        private void DrawFactionList(Rect rect)
        {
            // 清空位置映射
            factionRowRects.Clear();

            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.12f));
            Widgets.DrawBox(rect);

            Rect innerRect = rect.ContractedBy(8f);

            Text.Font = GameFont.Small;
            GUI.color = new Color(0.7f, 0.7f, 0.75f);
            Widgets.Label(new Rect(innerRect.x, innerRect.y, innerRect.width, 25f), "RimDiplomacy_FactionsTitle".Translate());
            GUI.color = Color.white;

            Widgets.DrawLineHorizontal(innerRect.x, innerRect.y + 28f, innerRect.width);

            var allFactions = GetAvailableFactions();
            float rowHeight = 65f;
            float contentHeight = allFactions.Count * (rowHeight + 5f);

            Rect viewRect = new Rect(0f, 0f, innerRect.width - 16f, Mathf.Max(contentHeight, innerRect.height - 35f));

            Rect scrollRect = new Rect(innerRect.x, innerRect.y + 35f, innerRect.width, innerRect.height - 35f);
            factionScrollPosition = GUI.BeginScrollView(scrollRect, factionScrollPosition, viewRect);

            float curY = 0f;
            foreach (var f in allFactions)
            {
                Rect rowRect = new Rect(5f, curY, viewRect.width - 10f, rowHeight);
                DrawFactionListItem(f, rowRect);

                // 记录派系位置（转换为屏幕坐标用于动画）
                Rect screenRect = new Rect(
                    rect.x + 8f + rowRect.x,
                    rect.y + 8f + 35f + rowRect.y - factionScrollPosition.y,
                    rowRect.width,
                    rowRect.height
                );
                factionRowRects[f] = screenRect;

                curY += rowHeight + 5f;
            }

            GUI.EndScrollView();

            // 检查好感度变化
            GoodwillChangeAnimator.CheckGoodwillChanges(allFactions);
        }

        private List<Faction> GetAvailableFactions()
        {
            var list = new List<Faction>();
            if (Find.FactionManager?.AllFactions != null)
            {
                foreach (var f in Find.FactionManager.AllFactions)
                {
                    if (f != null && !f.IsPlayer && !f.defeated && !f.Hidden)
                    {
                        list.Add(f);
                    }
                }
            }
            return list.OrderByDescending(f => f.PlayerGoodwill).ToList();
        }

        private void DrawFactionListItem(Faction f, Rect rect)
        {
            bool isSelected = f == faction;
            bool hasUnread = GameComponent_DiplomacyManager.Instance?.HasUnreadMessages(f) ?? false;
            
            if (isSelected)
            {
                Widgets.DrawBoxSolid(rect, new Color(0.25f, 0.45f, 0.7f, 0.6f));
            }
            else if (Mouse.IsOver(rect))
            {
                Widgets.DrawBoxSolid(rect, new Color(0.2f, 0.2f, 0.25f, 0.5f));
            }

            if (hasUnread && !isSelected)
            {
                Rect unreadRect = new Rect(rect.x, rect.y + 5f, 4f, rect.height - 10f);
                Widgets.DrawBoxSolid(unreadRect, new Color(0.3f, 0.8f, 1f));
            }

            float x = rect.x + 8f + (hasUnread && !isSelected ? 6f : 0f);
            float y = rect.y + 8f;

            Rect iconRect = new Rect(x, y, 45f, 45f);
            if (f.def != null)
            {
                Texture2D factionIcon = f.def.FactionIcon;
                if (factionIcon != null && factionIcon != BaseContent.BadTex)
                {
                    GUI.DrawTexture(iconRect, factionIcon);
                }
                else
                {
                    Widgets.DrawBoxSolid(iconRect, new Color(0.3f, 0.3f, 0.35f));
                }
            }
            x += 55f;

            Text.Font = GameFont.Small;
            GUI.color = isSelected ? Color.white : new Color(0.9f, 0.9f, 0.95f);
            Rect nameRect = new Rect(x, y, rect.width - x + rect.x - 10f, 22f);
            Widgets.Label(nameRect, f.Name ?? "Unknown");

            y += 24f;

            int goodwill = f.PlayerGoodwill;
            Color goodwillColor = GetGoodwillColor(goodwill);
            GUI.color = goodwillColor;
            Rect goodwillRect = new Rect(x, y, 50f, 18f);
            Widgets.Label(goodwillRect, goodwill.ToString());

            string relationLabel = GetRelationLabelShort(goodwill);
            Rect relationRect = new Rect(x + 55f, y, rect.width - x + rect.x - 65f, 18f);
            GUI.color = goodwillColor * 0.85f;
            Text.Font = GameFont.Tiny;
            Widgets.Label(relationRect, relationLabel);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            if (!isSelected && Widgets.ButtonInvisible(rect))
            {
                Find.WindowStack.Add(new Dialog_DiplomacyDialogue(f));
                Close();
            }
        }

        private void DrawChatArea(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.08f, 0.1f));
            Widgets.DrawBox(rect);

            Rect innerRect = rect.ContractedBy(10f);

            float messagesHeight = innerRect.height - INPUT_AREA_HEIGHT - 10f;
            Rect messagesRect = new Rect(innerRect.x, innerRect.y, innerRect.width, messagesHeight);
            DrawMessages(messagesRect);

            Widgets.DrawLineHorizontal(innerRect.x, innerRect.y + messagesHeight + 5f, innerRect.width);

            Rect inputRect = new Rect(innerRect.x, innerRect.y + messagesHeight + 10f, innerRect.width, INPUT_AREA_HEIGHT);
            DrawInputArea(inputRect);
        }

        private void DrawMessages(Rect rect)
        {
            if (session == null || session.messages.Count == 0)
            {
                GUI.color = new Color(0.4f, 0.4f, 0.45f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, "Start a conversation...");
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                lastMessageCount = 0;
                return;
            }

            float contentHeight = 10f;
            float availableWidth = rect.width - 50f;
            
            DialogueMessageData prevMsg = null;
            foreach (var msg in session.messages)
            {
                if (prevMsg != null && ShouldShowTimeGap(prevMsg.GetGameTick(), msg.GetGameTick()))
                {
                    contentHeight += 35f;
                }
                float msgHeight = CalculateMessageHeight(msg, availableWidth);
                contentHeight += msgHeight + 12f;
                prevMsg = msg;
            }
            contentHeight += 10f;

            float viewHeight = Mathf.Max(contentHeight, rect.height);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, viewHeight);
            
            bool hasNewMessage = session.messages.Count > lastMessageCount;
            float maxScroll = Mathf.Max(0f, contentHeight - rect.height);
            
            Vector2 beforeScroll = messageScrollPosition;
            
            if (hasNewMessage && !userIsScrolling)
            {
                messageScrollPosition.y = maxScroll;
            }
            lastMessageCount = session.messages.Count;
            
            messageScrollPosition = GUI.BeginScrollView(rect, messageScrollPosition, viewRect);

            if (Event.current.type == EventType.ScrollWheel || 
                (Event.current.type == EventType.MouseDrag && Mouse.IsOver(rect)))
            {
                userIsScrolling = true;
            }

            float curY = 10f;
            prevMsg = null;
            
            foreach (var msg in session.messages)
            {
                if (prevMsg != null && ShouldShowTimeGap(prevMsg.GetGameTick(), msg.GetGameTick()))
                {
                    DrawTimeGapLine(prevMsg.GetGameTick(), msg.GetGameTick(), viewRect.width, curY);
                    curY += 35f;
                }
                
                float msgHeight = CalculateMessageHeight(msg, viewRect.width - 30f);
                float maxBubbleWidth = Mathf.Min(480f, viewRect.width * 0.75f);
                float bubbleWidth = CalculateBubbleWidth(msg, maxBubbleWidth);

                float msgX = msg.isPlayer 
                    ? viewRect.width - bubbleWidth - 10f 
                    : 10f;
                
                Rect msgRect = new Rect(msgX, curY, bubbleWidth, msgHeight);
                DrawRoundedMessageBubble(msg, msgRect);

                curY += msgHeight + 12f;
                prevMsg = msg;
            }

            if (messageScrollPosition.y >= maxScroll - 10f)
            {
                userIsScrolling = false;
            }
            
            GUI.EndScrollView();
        }

        private bool ShouldShowTimeGap(int prevGameTick, int currentGameTick)
        {
            int tickDiff = currentGameTick - prevGameTick;
            float minutes = tickDiff / 2500f;
            return minutes >= TIME_GAP_THRESHOLD_MINUTES;
        }

        private void DrawTimeGapLine(int prevGameTick, int currentGameTick, float width, float y)
        {
            int tickDiff = currentGameTick - prevGameTick;
            string gapText = FormatGameTimeGap(tickDiff);

            float textWidth = Text.CalcSize(gapText).x;
            float centerX = width / 2f;
            float lineWidth = (width - textWidth - 40f) / 2f;

            GUI.color = new Color(0.4f, 0.4f, 0.45f, 0.6f);
            
            Widgets.DrawLineHorizontal(20f, y + 12f, lineWidth - 10f);
            Widgets.DrawLineHorizontal(centerX + textWidth / 2f + 10f, y + 12f, lineWidth - 10f);

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.5f, 0.5f, 0.55f, 0.8f);
            Rect textRect = new Rect(centerX - textWidth / 2f, y + 4f, textWidth, 16f);
            Widgets.Label(textRect, gapText);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        private string FormatGameTimeGap(int tickDiff)
        {
            float minutes = tickDiff / 2500f;
            float hours = minutes / 60f;
            float days = hours / 24f;

            if (minutes < 60f)
            {
                return $"{Mathf.RoundToInt(minutes)}分钟前";
            }
            else if (hours < 24f)
            {
                return $"{Mathf.RoundToInt(hours)}小时前";
            }
            else
            {
                return $"{Mathf.RoundToInt(days)}天前";
            }
        }

        private void DrawRoundedMessageBubble(DialogueMessageData msg, Rect rect)
        {
            Color bubbleColor;
            Color textColor;
            Color senderColor;
            
            if (msg.isPlayer)
            {
                bubbleColor = PlayerBubbleColor;
                textColor = new Color(0.15f, 0.25f, 0.1f);
                senderColor = new Color(0.2f, 0.35f, 0.12f);
            }
            else
            {
                bubbleColor = AIBubbleColor;
                textColor = new Color(0.95f, 0.95f, 0.97f);
                senderColor = new Color(0.75f, 0.8f, 0.9f);
            }

            // 绘制阴影
            Rect shadowRect = new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height);
            DrawRoundedRect(shadowRect, new Color(0f, 0f, 0f, 0.2f), BUBBLE_CORNER_RADIUS);

            // 绘制气泡背景（圆角）
            DrawRoundedRect(rect, bubbleColor, BUBBLE_CORNER_RADIUS);

            float padding = 12f;
            float contentX = rect.x + padding;
            float contentY = rect.y + padding;
            float contentWidth = rect.width - padding * 2f;

            // 发送者名称
            Text.Font = GameFont.Tiny;
            GUI.color = senderColor;
            Rect senderRect = new Rect(contentX, contentY, contentWidth, 16f);
            Widgets.Label(senderRect, msg.sender);

            // 时间戳
            string timeStr = GetTimestampString(msg);
            float timeWidth = Text.CalcSize(timeStr).x;
            Rect timeRect = new Rect(rect.xMax - timeWidth - padding, contentY, timeWidth, 16f);
            GUI.color = new Color(senderColor.r, senderColor.g, senderColor.b, 0.7f);
            Widgets.Label(timeRect, timeStr);

            Text.Font = GameFont.Small;
            GUI.color = textColor;

            // 消息内容
            contentY += 22f;
            Rect contentRect = new Rect(contentX, contentY, contentWidth, rect.height - padding * 2f - 22f);
            Widgets.Label(contentRect, msg.message);

            GUI.color = Color.white;
        }

        private static Texture2D _whiteTexture;
        private static Texture2D WhiteTexture => _whiteTexture;
        
        static Dialog_DiplomacyDialogue()
        {
            _whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _whiteTexture.SetPixel(0, 0, Color.white);
            _whiteTexture.Apply();
        }

        private void DrawRoundedRect(Rect rect, Color color, float radius)
        {
            GUI.color = color;

            // 绘制中心矩形
            GUI.DrawTexture(new Rect(rect.x + radius, rect.y, rect.width - radius * 2f, rect.height), WhiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y + radius, rect.width, rect.height - radius * 2f), WhiteTexture);

            // 绘制四个圆角
            float diameter = radius * 2f;
            DrawCorner(new Rect(rect.x, rect.y, diameter, diameter), radius, 0);
            DrawCorner(new Rect(rect.x + rect.width - diameter, rect.y, diameter, diameter), radius, 1);
            DrawCorner(new Rect(rect.x, rect.y + rect.height - diameter, diameter, diameter), radius, 2);
            DrawCorner(new Rect(rect.x + rect.width - diameter, rect.y + rect.height - diameter, diameter, diameter), radius, 3);

            GUI.color = Color.white;
        }

        private void DrawCorner(Rect rect, float radius, int quadrant)
        {
            Vector2 center = new Vector2(rect.x + radius, rect.y + radius);
            DrawCornerSimple(center, radius, quadrant);
        }

        private void DrawCornerSimple(Vector2 center, float radius, int quadrant)
        {
            float startAngle = quadrant * 90f;
            int segments = Mathf.Max(12, (int)(radius * 1.5f));

            for (int i = 0; i < segments; i++)
            {
                float angle1 = (startAngle + i * 90f / segments) * Mathf.Deg2Rad;
                float angle2 = (startAngle + (i + 1) * 90f / segments) * Mathf.Deg2Rad;

                Vector2 p1 = new Vector2(
                    center.x + Mathf.Cos(angle1) * radius,
                    center.y + Mathf.Sin(angle1) * radius
                );
                Vector2 p2 = new Vector2(
                    center.x + Mathf.Cos(angle2) * radius,
                    center.y + Mathf.Sin(angle2) * radius
                );

                float minX = Mathf.Min(center.x, Mathf.Min(p1.x, p2.x));
                float minY = Mathf.Min(center.y, Mathf.Min(p1.y, p2.y));
                float maxX = Mathf.Max(center.x, Mathf.Max(p1.x, p2.x));
                float maxY = Mathf.Max(center.y, Mathf.Max(p1.y, p2.y));

                GUI.DrawTexture(new Rect(minX, minY, maxX - minX, maxY - minY), WhiteTexture);
            }

            float innerRadius = radius * 0.6f;
            GUI.DrawTexture(new Rect(center.x - innerRadius, center.y - innerRadius, innerRadius * 2, innerRadius * 2), WhiteTexture);
        }

        private void DrawInputArea(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.15f));

            float padding = 10f;
            float inputWidth = rect.width - padding * 2f - 90f;
            float inputHeight = rect.height - padding * 2f - 20f;

            Rect textRect = new Rect(rect.x + padding, rect.y + padding, inputWidth, inputHeight);
            GUI.SetNextControlName("DialogueInput");
            
            Widgets.DrawBoxSolid(textRect, new Color(0.18f, 0.18f, 0.22f));
            Rect innerTextRect = textRect.ContractedBy(5f);
            
            HandleInputEvents();
            
            string newInput = Widgets.TextArea(innerTextRect, inputText);
            if (newInput.Length <= MAX_INPUT_LENGTH)
            {
                inputText = newInput;
            }

            int charCount = inputText.Length;
            Color countColor = charCount > MAX_INPUT_LENGTH * 0.8f ? Color.yellow : Color.gray;
            GUI.color = countColor;
            Text.Font = GameFont.Tiny;
            Rect countRect = new Rect(rect.x + padding, rect.y + rect.height - 18f, 100f, 16f);
            Widgets.Label(countRect, $"{charCount}/{MAX_INPUT_LENGTH}");
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            Rect sendRect = new Rect(rect.xMax - 85f, rect.y + padding, 75f, inputHeight);
            bool canSend = !string.IsNullOrWhiteSpace(inputText) && !waitingForAIResponse && charCount <= MAX_INPUT_LENGTH;
            
            Color buttonColor = canSend ? new Color(0.2f, 0.6f, 1f, 0.9f) : new Color(0.3f, 0.3f, 0.35f, 0.5f);
            GUI.color = buttonColor;
            Widgets.DrawBoxSolid(sendRect, buttonColor);
            GUI.color = Color.white;

            GUI.enabled = canSend;
            if (Widgets.ButtonText(sendRect, "RimDiplomacy_SendButton".Translate()))
            {
                SendMessage();
            }
            GUI.enabled = true;

            if (waitingForAIResponse)
            {
                Rect typingRect = new Rect(rect.x + padding + 110f, rect.y + rect.height - 18f, 150f, 16f);
                GUI.color = new Color(0.6f, 0.8f, 1f, 0.8f);
                Text.Font = GameFont.Tiny;
                string dots = new string('.', ((int)(Time.time * 3) % 3) + 1);
                string typingText = "RimDiplomacy_AIIsTyping".Translate();
                Widgets.Label(typingRect, $"{typingText}{dots}");
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }
            else if (!string.IsNullOrEmpty(aiError))
            {
                Rect errorRect = new Rect(rect.x + padding + 110f, rect.y + rect.height - 18f, 200f, 16f);
                GUI.color = Color.red;
                Text.Font = GameFont.Tiny;
                string errorLabel = "RimDiplomacy_ErrorLabel".Translate();
                Widgets.Label(errorRect, $"{errorLabel}: " + aiError.Substring(0, Mathf.Min(30, aiError.Length)));
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }
        }

        private void HandleInputEvents()
        {
            Event current = Event.current;
            if (current.type == EventType.KeyDown)
            {
                if (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter)
                {
                    bool altPressed = current.alt;
                    
                    if (altPressed)
                    {
                        inputText += "\n";
                        current.Use();
                    }
                    else if (!string.IsNullOrWhiteSpace(inputText) && !waitingForAIResponse)
                    {
                        current.Use();
                        SendMessage();
                    }
                }
            }
        }

        private float CalculateMessageHeight(DialogueMessageData msg, float width)
        {
            float textWidth = Mathf.Min(width - 50f, 450f);
            float textHeight = Text.CalcHeight(msg.message, textWidth);
            return Mathf.Max(60f, textHeight + 50f);
        }

        private float CalculateBubbleWidth(DialogueMessageData msg, float maxWidth)
        {
            float textWidth = Text.CalcSize(msg.message).x;
            float estimatedWidth = Mathf.Min(textWidth + 50f, maxWidth);
            return Mathf.Max(180f, estimatedWidth);
        }

        private string GetTimestampString(DialogueMessageData msg)
        {
            int currentTick = Find.TickManager.TicksGame;
            int messageTick = msg.GetGameTick();
            int tickDiff = currentTick - messageTick;
            
            float minutes = tickDiff / 2500f;
            float hours = minutes / 60f;
            float days = hours / 24f;

            if (minutes < 1f)
            {
                return "刚刚";
            }
            else if (minutes < 60f)
            {
                return $"{Mathf.RoundToInt(minutes)}分钟前";
            }
            else if (hours < 24f)
            {
                int hourOfDay = GenLocalDate.HourOfDay(Find.CurrentMap);
                int minuteOfHour = (int)((GenLocalDate.DayPercent(Find.CurrentMap) * 24f - hourOfDay) * 60f);
                return $"今天 {hourOfDay:D2}:{minuteOfHour:D2}";
            }
            else if (days < 2f)
            {
                int hourOfDay = GenLocalDate.HourOfDay(Find.CurrentMap);
                int minuteOfHour = (int)((GenLocalDate.DayPercent(Find.CurrentMap) * 24f - hourOfDay) * 60f);
                return $"昨天 {hourOfDay:D2}:{minuteOfHour:D2}";
            }
            else if (days < 7f)
            {
                return $"{Mathf.RoundToInt(days)}天前";
            }
            else
            {
                long absTicks = Find.TickManager.TicksAbs;
                Vector2 longLat = Find.WorldGrid.LongLatOf(Find.CurrentMap.Tile);
                int dayOfQuadrum = GenDate.DayOfQuadrum(absTicks, longLat.x) + 1;
                string quadrum = GenDate.Quadrum(absTicks, longLat.x).Label();
                int year = GenDate.Year(absTicks, longLat.x);
                return $"{quadrum}第{dayOfQuadrum:D2}天 (Y{year + 1})";
            }
        }

        private Color GetGoodwillColor(int goodwill)
        {
            if (goodwill >= 80) return new Color(0.3f, 0.9f, 0.3f);
            if (goodwill >= 40) return new Color(0.6f, 0.9f, 0.3f);
            if (goodwill >= 0) return new Color(0.9f, 0.9f, 0.3f);
            if (goodwill >= -40) return new Color(0.9f, 0.6f, 0.2f);
            return new Color(0.9f, 0.3f, 0.3f);
        }

        private string GetRelationLabelShort(int goodwill)
        {
            if (goodwill >= 80) return "RimDiplomacy_RelationAllyShort".Translate();
            if (goodwill >= 40) return "RimDiplomacy_RelationFriendShort".Translate();
            if (goodwill >= 0) return "RimDiplomacy_RelationNeutralShort".Translate();
            if (goodwill >= -40) return "RimDiplomacy_RelationHostileShort".Translate();
            return "RimDiplomacy_RelationEnemyShort".Translate();
        }

        private void SendMessage()
        {
            if (string.IsNullOrWhiteSpace(inputText) || waitingForAIResponse || session == null)
                return;

            string playerMessage = inputText.Trim();
            if (string.IsNullOrEmpty(playerMessage))
                return;

            inputText = "";

            session.AddMessage("RimDiplomacy_You".Translate(), playerMessage, true);

            waitingForAIResponse = true;
            aiRequestProgress = 0f;
            aiError = null;

            if (!AIChatService.Instance.IsConfigured())
            {
                Log.Message("[RimDiplomacy] AI not configured, using fallback response");
                AddFallbackResponse(playerMessage);
                return;
            }

            var chatMessages = BuildChatMessages(playerMessage);

            AIChatService.Instance.SendChatRequest(
                chatMessages,
                onSuccess: (response) =>
                {
                    AddAIResponse(response);
                    waitingForAIResponse = false;
                },
                onError: (error) =>
                {
                    Log.Warning($"[RimDiplomacy] AI request failed: {error}");
                    aiError = error;
                    AddFallbackResponse(playerMessage);
                    waitingForAIResponse = false;
                },
                onProgress: (progress) =>
                {
                    aiRequestProgress = progress;
                }
            );
        }

        private List<ChatMessageData> BuildChatMessages(string playerMessage)
        {
            var chatMessages = new List<ChatMessageData>();

            string systemPrompt = BuildSystemPrompt();
            chatMessages.Add(new ChatMessageData { role = "system", content = systemPrompt });

            int startIndex = Mathf.Max(0, session.messages.Count - 11);
            for (int i = startIndex; i < session.messages.Count - 1; i++)
            {
                var msg = session.messages[i];
                string role = msg.isPlayer ? "user" : "assistant";
                chatMessages.Add(new ChatMessageData { role = role, content = msg.message });
            }

            chatMessages.Add(new ChatMessageData { role = "user", content = playerMessage });

            Log.Message($"[RimDiplomacy] Built chat messages: {chatMessages.Count} messages, last message: {playerMessage.Substring(0, Math.Min(50, playerMessage.Length))}...");
            return chatMessages;
        }

        private string BuildSystemPrompt()
        {
            var sb = new System.Text.StringBuilder();

            if (RimDiplomacyMod.Instance != null && RimDiplomacyMod.Instance.InstanceSettings?.GlobalPrompt != null
                && !string.IsNullOrEmpty(RimDiplomacyMod.Instance.InstanceSettings.GlobalPrompt.SystemPrompt))
            {
                sb.AppendLine(RimDiplomacyMod.Instance.InstanceSettings.GlobalPrompt.SystemPrompt);
            }
            else
            {
                sb.AppendLine("You are the leader of a faction in RimWorld.");
            }

            sb.AppendLine();
            sb.AppendLine($"=== FACTION INFO ===");
            sb.AppendLine($"Name: {faction.Name}");
            sb.AppendLine($"Type: {faction.def?.label ?? "Unknown"}");
            sb.AppendLine($"Current Goodwill: {faction.PlayerGoodwill}");
            sb.AppendLine($"Relation: {GetRelationLabel(faction.PlayerGoodwill)}");

            if (faction.leader != null)
            {
                sb.AppendLine($"Leader: {faction.leader.Name?.ToStringFull ?? "Unknown"}");

                if (faction.leader.story?.traits?.allTraits != null)
                {
                    var traits = faction.leader.story.traits.allTraits;
                    if (traits.Count > 0)
                    {
                        sb.AppendLine($"Leader Traits: {string.Join(", ", traits.Select(t => t.Label))}");
                    }
                }
            }

            if (faction.ideos?.PrimaryIdeo != null)
            {
                sb.AppendLine($"Ideology: {faction.ideos.PrimaryIdeo.name}");
            }

            // 添加 API 调用说明
            sb.AppendLine();
            sb.AppendLine($"=== AVAILABLE ACTIONS ===");
            sb.AppendLine("You can perform diplomatic actions by including a JSON block in your response.");
            sb.AppendLine();

            // 获取当前设置
            var settings = RimDiplomacyMod.Instance?.InstanceSettings;
            if (settings != null)
            {
                sb.AppendLine("=== CURRENT API LIMITS (MUST FOLLOW) ===");
                sb.AppendLine($"- Max goodwill adjustment per call: {settings.MaxGoodwillAdjustmentPerCall} (range: 0 to {settings.MaxGoodwillAdjustmentPerCall})");
                sb.AppendLine($"- Max daily goodwill adjustment: {settings.MaxDailyGoodwillAdjustment}");
                sb.AppendLine($"- Goodwill cooldown: {settings.GoodwillCooldownTicks / 2500f:F1} hours");
                sb.AppendLine($"- Max gift silver: {settings.MaxGiftSilverAmount}");
                sb.AppendLine($"- Max gift goodwill gain: {settings.MaxGiftGoodwillGain}");
                sb.AppendLine($"- Min goodwill for aid: {settings.MinGoodwillForAid}");
                sb.AppendLine($"- Max goodwill for war declaration: {settings.MaxGoodwillForWarDeclaration}");
                sb.AppendLine($"- Max peace cost: {settings.MaxPeaceCost}");
                sb.AppendLine($"- Peace goodwill reset: {settings.PeaceGoodwillReset}");
                sb.AppendLine();
                sb.AppendLine("ENABLED FEATURES:");
                sb.AppendLine($"- Goodwill adjustment: {(settings.EnableAIGoodwillAdjustment ? "YES" : "NO")}");
                sb.AppendLine($"- Gift sending: {(settings.EnableAIGiftSending ? "YES" : "NO")}");
                sb.AppendLine($"- War declaration: {(settings.EnableAIWarDeclaration ? "YES" : "NO")}");
                sb.AppendLine($"- Peace making: {(settings.EnableAIPeaceMaking ? "YES" : "NO")}");
                sb.AppendLine($"- Trade caravan: {(settings.EnableAITradeCaravan ? "YES" : "NO")}");
                sb.AppendLine($"- Aid request: {(settings.EnableAIAidRequest ? "YES" : "NO")}");
                sb.AppendLine();
            }

            sb.AppendLine("ACTIONS:");
            sb.AppendLine($"1. adjust_goodwill - Change faction relations");
            sb.AppendLine($"   Parameters: amount (int, -{settings?.MaxGoodwillAdjustmentPerCall ?? 15} to {settings?.MaxGoodwillAdjustmentPerCall ?? 15}), reason (string)");
            sb.AppendLine($"   Daily limit remaining: {settings?.MaxDailyGoodwillAdjustment ?? 30} total per day");
            sb.AppendLine($"2. send_gift - Send silver to improve relations");
            sb.AppendLine($"   Parameters: silver (int, max {settings?.MaxGiftSilverAmount ?? 1000}), goodwill_gain (int, 1-{settings?.MaxGiftGoodwillGain ?? 10})");
            sb.AppendLine($"3. request_aid - Request military/medical aid (requires ally)");
            sb.AppendLine($"   Parameters: type (string: Military/Medical/Resources)");
            sb.AppendLine($"   Requirement: goodwill >= {settings?.MinGoodwillForAid ?? 40}");
            sb.AppendLine($"4. declare_war - Declare war");
            sb.AppendLine($"   Parameters: reason (string)");
            sb.AppendLine($"   Requirement: goodwill <= {settings?.MaxGoodwillForWarDeclaration ?? -50}");
            sb.AppendLine($"5. make_peace - Offer peace treaty (requires war)");
            sb.AppendLine($"   Parameters: cost (int, max {settings?.MaxPeaceCost ?? 5000} silver)");
            sb.AppendLine($"   Result: goodwill reset to {settings?.PeaceGoodwillReset ?? -20}");
            sb.AppendLine($"6. request_caravan - Request trade caravan");
            sb.AppendLine($"   Parameters: goods (string, optional)");
            sb.AppendLine($"   Requirement: not hostile");
            sb.AppendLine($"7. reject_request - Reject player's request");
            sb.AppendLine($"   Parameters: reason (string)");
            sb.AppendLine();
            sb.AppendLine("DECISION GUIDELINES:");
            sb.AppendLine($"- Current goodwill {faction.PlayerGoodwill}: {GetGoodwillGuideline(faction.PlayerGoodwill)}");
            sb.AppendLine("- Consider your leader's traits and ideology when making decisions");
            sb.AppendLine("- You can accept or reject player requests based on current relations");
            sb.AppendLine($"- Small goodwill changes (1-{Math.Max(1, (settings?.MaxGoodwillAdjustmentPerCall ?? 15) / 3)}) for minor interactions");
            sb.AppendLine($"- Medium changes ({Math.Max(1, (settings?.MaxGoodwillAdjustmentPerCall ?? 15) / 3)}-{Math.Max(2, (settings?.MaxGoodwillAdjustmentPerCall ?? 15) * 2 / 3)}) for moderate events");
            sb.AppendLine($"- Large changes ({Math.Max(2, (settings?.MaxGoodwillAdjustmentPerCall ?? 15) * 2 / 3)}-{settings?.MaxGoodwillAdjustmentPerCall ?? 15}) for significant diplomatic events");
            sb.AppendLine();
            sb.AppendLine("RESPONSE FORMAT:");
            sb.AppendLine("Respond with your in-character dialogue first, then optionally include a JSON block:");
            sb.AppendLine();
            sb.AppendLine("```json");
            sb.AppendLine("{");
            sb.AppendLine("  \"action\": \"action_name\",");
            sb.AppendLine("  \"parameters\": {");
            sb.AppendLine("    \"param1\": value,");
            sb.AppendLine("    \"param2\": value");
            sb.AppendLine("  },");
            sb.AppendLine("  \"response\": \"Your in-character response here\"");
            sb.AppendLine("}");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("IMPORTANT RULES:");
            sb.AppendLine("1. You MUST respond in the same language as the user's game language");
            sb.AppendLine($"   Game language: {GetGameLanguageName()}");
            sb.AppendLine("2. NEVER exceed the max values shown above");
            sb.AppendLine("2. ONLY use enabled features");
            sb.AppendLine("3. ALWAYS check requirements before using an action");
            sb.AppendLine("4. If a feature is disabled, you cannot use it - explain this to the player");
            sb.AppendLine();
            sb.AppendLine("If no action is needed, respond normally without JSON.");

            return sb.ToString();
        }

        private string GetRelationLabel(int goodwill)
        {
            if (goodwill >= 80) return "Ally";
            if (goodwill >= 40) return "Friend";
            if (goodwill >= 0) return "Neutral";
            if (goodwill >= -40) return "Hostile";
            return "Enemy";
        }

        private string GetGoodwillGuideline(int goodwill)
        {
            if (goodwill >= 80) return "Very friendly - likely to accept most requests";
            if (goodwill >= 40) return "Friendly - open to trade and cooperation";
            if (goodwill >= 0) return "Neutral - cautious but willing to negotiate";
            if (goodwill >= -40) return "Hostile - unlikely to cooperate, may threaten";
            return "Enemy - aggressive, may declare war";
        }

        private string GetGameLanguageName()
        {
            try
            {
                var lang = LanguageDatabase.activeLanguage;
                if (lang != null)
                {
                    return lang.FriendlyNameEnglish;
                }
            }
            catch
            {
            }
            return "English";
        }

        private void AddAIResponse(string response)
        {
            // 解析AI响应
            var parsedResponse = AIResponseParser.ParseResponse(response, faction);

            // 获取对话文本
            string dialogueText = parsedResponse.DialogueText;

            // 如果没有对话文本但有action，生成默认回复
            if (string.IsNullOrWhiteSpace(dialogueText) && parsedResponse.Actions.Count > 0)
            {
                dialogueText = GenerateResponseFromActions(parsedResponse.Actions);
            }

            // 添加对话消息
            string senderName = GetSenderName();
            session.AddMessage(senderName, dialogueText, false);

            // 执行AI动作
            if (parsedResponse.Actions.Count > 0)
            {
                ExecuteAIActions(parsedResponse.Actions);
            }
        }

        /// <summary>
        /// 根据动作生成响应文本
        /// </summary>
        private string GenerateResponseFromActions(List<AIAction> actions)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var action in actions)
            {
                switch (action.ActionType)
                {
                    case "adjust_goodwill":
                        if (action.Parameters.TryGetValue("amount", out object amount) && amount is int amt)
                        {
                            sb.AppendLine(amt > 0
                                ? "I appreciate your words. Our relations have improved."
                                : "Your words concern me. Our relations have suffered.");
                        }
                        break;
                    case "send_gift":
                        sb.AppendLine("I accept your gift. Let this strengthen our bond.");
                        break;
                    case "request_aid":
                        sb.AppendLine("As allies, we shall assist you.");
                        break;
                    case "declare_war":
                        sb.AppendLine("You leave me no choice. Prepare for conflict!");
                        break;
                    case "make_peace":
                        sb.AppendLine("Let us end this conflict. Peace is preferable.");
                        break;
                    case "request_caravan":
                        sb.AppendLine("Our traders will visit you soon.");
                        break;
                    case "reject_request":
                        string reason = action.Parameters.TryGetValue("reason", out object r)
                            ? r?.ToString()
                            : "I cannot fulfill this request.";
                        sb.AppendLine(reason);
                        break;
                }
            }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// 执行AI动作
        /// </summary>
        private void ExecuteAIActions(List<AIAction> actions)
        {
            var executor = new AIActionExecutor(faction);

            foreach (var action in actions)
            {
                Log.Message($"[RimDiplomacy] Executing AI action: {action.ActionType}");
                var result = executor.ExecuteAction(action);

                if (result.IsSuccess)
                {
                    Log.Message($"[RimDiplomacy] Action executed successfully: {result.Message}");
                }
                else
                {
                    Log.Warning($"[RimDiplomacy] Action failed: {result.Message}");
                    // 如果动作执行失败，添加一条说明消息
                    session.AddMessage("System", $"Action '{action.ActionType}' could not be executed: {result.Message}", false);
                }
            }
        }

        private void AddFallbackResponse(string playerMessage)
        {
            string senderName = GetSenderName();
            string response = GenerateSimulatedResponse(playerMessage);
            session.AddMessage(senderName, response, false);
        }

        private string GetSenderName()
        {
            if (faction.leader != null && faction.leader.Name != null)
            {
                return faction.leader.Name.ToString();
            }
            return faction.Name ?? "Unknown";
        }

        private string GenerateSimulatedResponse(string playerMessage)
        {
            if (string.IsNullOrEmpty(playerMessage))
                return "I see. What else would you like to discuss?";

            string lowerMessage = playerMessage.ToLower();

            if (lowerMessage.Contains("trade") || lowerMessage.Contains("caravan"))
            {
                return "We are open to trade. Our caravans can reach you soon.";
            }
            else if (lowerMessage.Contains("help") || lowerMessage.Contains("aid"))
            {
                if (faction.PlayerGoodwill >= 80)
                {
                    return "As allies, we shall send assistance immediately.";
                }
                else
                {
                    return "We are not yet close enough for such favors. Improve our relations first.";
                }
            }
            else if (lowerMessage.Contains("war") || lowerMessage.Contains("attack") || lowerMessage.Contains("raid"))
            {
                return "Threats will not be tolerated. Watch your words carefully.";
            }
            else if (lowerMessage.Contains("peace") || lowerMessage.Contains("friend"))
            {
                return "Peace is always preferable. We welcome friendly relations.";
            }
            else
            {
                return "Interesting. We shall consider your words carefully.";
            }
        }
    }
}
