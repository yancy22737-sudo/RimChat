using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using RimDiplomacy.AI;
using RimDiplomacy.Memory;
using RimDiplomacy.Relation;
using RimDiplomacy.Config;
using RimDiplomacy.DiplomacySystem;
using RimDiplomacy.Persistence;
using RimDiplomacy.Util;

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
        private string currentRequestId = null;
        private const int MAX_INPUT_LENGTH = 500;
        private const float FACTION_LIST_WIDTH = 220f;
        private const float INPUT_AREA_HEIGHT = 80f;
        private const float TIME_GAP_THRESHOLD_MINUTES = 15f;
        private const float BUBBLE_CORNER_RADIUS = 12f;
        
        // 五维属性栏组件
        private readonly FiveDimensionBar fiveDimensionBar = new FiveDimensionBar();

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
            
            // 初始化五维属性栏
            fiveDimensionBar.UpdateFaction(faction);

            // 订阅好感度变化事件
            GoodwillChangeAnimator.OnGoodwillChanged += OnGoodwillChanged;

            Log.Message($"[RimDiplomacy] Dialogue opened with {faction.Name}, messages: {session?.messages.Count ?? 0}, AI configured: {AIChatService.Instance.IsConfigured()}");
        }

        public override void PreClose()
        {
            base.PreClose();
            // 取消订阅事件
            GoodwillChangeAnimator.OnGoodwillChanged -= OnGoodwillChanged;
            
            // 取消正在进行的AI请求
            if (!string.IsNullOrEmpty(currentRequestId))
            {
                AIChatServiceAsync.Instance?.CancelRequest(currentRequestId);
                currentRequestId = null;
            }
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
            
            // 左侧标题：Rim Diplomacy Terminal
            Text.Font = GameFont.Medium;
            GUI.color = new Color(0.9f, 0.9f, 0.95f);
            string title = "RimDiplomacy_TerminalTitle".Translate();
            Widgets.Label(new Rect(inRect.x + 15f, inRect.y + 8f, 250f, 30f), title);

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

            // 计算各区域高度 - 使用五维属性栏的实际高度
            float fiveDimHeight = fiveDimensionBar.GetPreferredHeight();
            float inputHeight = INPUT_AREA_HEIGHT;
            float spacing = 10f;
            float messagesHeight = innerRect.height - inputHeight - fiveDimHeight - spacing * 2f;

            // 消息区域
            Rect messagesRect = new Rect(innerRect.x, innerRect.y, innerRect.width, messagesHeight);
            DrawMessages(messagesRect);

            // 分隔线1 - 消息与五维属性栏之间
            float line1Y = innerRect.y + messagesHeight + 5f;
            Widgets.DrawLineHorizontal(innerRect.x, line1Y, innerRect.width);

            // 五维属性栏区域
            float fiveDimY = line1Y + 5f;
            Rect fiveDimRect = new Rect(innerRect.x, fiveDimY, innerRect.width, fiveDimHeight);
            fiveDimensionBar.Draw(fiveDimRect);

            // 分隔线2 - 五维属性栏与输入框之间
            float line2Y = fiveDimY + fiveDimHeight + 5f;
            Widgets.DrawLineHorizontal(innerRect.x, line2Y, innerRect.width);

            // 输入区域
            float inputY = line2Y + 5f;
            Rect inputRect = new Rect(innerRect.x, inputY, innerRect.width, inputHeight);
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
                
                if (msg.IsSystemMessage())
                {
                    // 系统消息：左对齐，使用完整宽度
                    float maxSystemWidth = viewRect.width - 60f;
                    float systemWidth = CalculateBubbleWidth(msg, maxSystemWidth);
                    Rect msgRect = new Rect(20f, curY, systemWidth, msgHeight);
                    DrawRoundedMessageBubble(msg, msgRect);
                }
                else
                {
                    // 普通消息：使用气泡样式
                    float maxBubbleWidth = Mathf.Min(480f, viewRect.width * 0.75f);
                    float bubbleWidth = CalculateBubbleWidth(msg, maxBubbleWidth);
                    float msgX = msg.isPlayer 
                        ? viewRect.width - bubbleWidth - 10f 
                        : 10f;
                    
                    Rect msgRect = new Rect(msgX, curY, bubbleWidth, msgHeight);
                    DrawRoundedMessageBubble(msg, msgRect);
                }

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
            if (msg.IsSystemMessage())
            {
                DrawSystemMessage(msg, rect);
            }
            else
            {
                DrawNormalMessageBubble(msg, rect);
            }
        }

        private void DrawSystemMessage(DialogueMessageData msg, Rect rect)
        {
            float padding = 4f;
            float contentX = rect.x + padding;
            float contentY = rect.y + padding;
            float contentWidth = rect.width - padding * 2f;

            GUI.color = new Color(0.5f, 0.5f, 0.55f, 0.9f);
            
            Text.Font = GameFont.Tiny;
            Rect contentRect = new Rect(contentX, contentY, contentWidth, rect.height - padding * 2f);
            Widgets.Label(contentRect, msg.message);
            
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawNormalMessageBubble(DialogueMessageData msg, Rect rect)
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
            if (msg.IsSystemMessage())
            {
                float systemTextWidth = Mathf.Min(width - 8f, 600f);
                float systemTextHeight = Text.CalcHeight(msg.message, systemTextWidth);
                return Mathf.Max(16f, systemTextHeight + 8f);
            }
            
            float textWidth = Mathf.Min(width - 50f, 450f);
            float textHeight = Text.CalcHeight(msg.message, textWidth);
            return Mathf.Max(60f, textHeight + 50f);
        }

        private float CalculateBubbleWidth(DialogueMessageData msg, float maxWidth)
        {
            float textWidth = Text.CalcSize(msg.message).x;
            
            if (msg.IsSystemMessage())
            {
                return Mathf.Min(textWidth + 40f, maxWidth);
            }
            
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

            if (!AIChatServiceAsync.Instance.IsConfigured())
            {
                Log.Message("[RimDiplomacy] AI not configured, using fallback response");
                AddFallbackResponse(playerMessage);
                return;
            }

            var chatMessages = BuildChatMessages(playerMessage);

            currentRequestId = AIChatServiceAsync.Instance.SendChatRequestAsync(
                chatMessages,
                onSuccess: (response) =>
                {
                    AddAIResponse(response);
                    waitingForAIResponse = false;
                    currentRequestId = null;
                },
                onError: (error) =>
                {
                    Log.Warning($"[RimDiplomacy] AI request failed: {error}");
                    aiError = error;
                    AddFallbackResponse(playerMessage);
                    waitingForAIResponse = false;
                    currentRequestId = null;
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
            PromptPersistenceService.Instance.Initialize();
            return PromptPersistenceService.Instance.BuildFullSystemPrompt(faction, PromptPersistenceService.Instance.LoadConfig());
        }


        private void AddAIResponse(string response)
        {
            // 解析 AI 响应
            var parsedResponse = AIResponseParser.ParseResponse(response, faction);

            // 获取对话文本
            string dialogueText = parsedResponse.DialogueText;

            // 如果没有对话文本但有 action，生成默认回复
            if (string.IsNullOrWhiteSpace(dialogueText) && parsedResponse.Actions.Count > 0)
            {
                dialogueText = GenerateResponseFromActions(parsedResponse.Actions);
            }

            // 添加对话消息
            string senderName = GetSenderName();
            session.AddMessage(senderName, dialogueText, false);

            // 执行 AI 动作
            if (parsedResponse.Actions.Count > 0)
            {
                ExecuteAIActions(parsedResponse.Actions);
            }

            // 处理五维关系值变化
            if (parsedResponse.RelationChanges != null && parsedResponse.RelationChanges.HasChanges())
            {
                ApplyRelationChanges(parsedResponse.RelationChanges);
            }

            // 对话结束后保存记忆
            SaveFactionMemory();
        }

        /// <summary>
        /// 应用五维关系值变化
        /// </summary>
        private void ApplyRelationChanges(RelationChanges changes)
        {
            try
            {
                var manager = GameComponent_DiplomacyManager.Instance;
                if (manager == null) return;

                // 更新五维关系值
                manager.UpdateRelationValues(
                    faction,
                    changes.Trust,
                    changes.Intimacy,
                    changes.Reciprocity,
                    changes.Respect,
                    changes.Influence,
                    changes.Reason
                );

                // 根据五维关系值变化计算并应用好感度变化
                int goodwillChange = CalculateGoodwillChangeFromRelations(changes);
                if (goodwillChange != 0)
                {
                    faction.TryAffectGoodwillWith(Faction.OfPlayer, goodwillChange, false, true, null);

                    // 添加系统消息通知玩家
                    string changeSummary = changes.GetChangeSummary();
                    string message = $"关系变化: {changeSummary}";
                    if (!string.IsNullOrEmpty(changes.Reason))
                    {
                        message += $"\n原因: {changes.Reason}";
                    }
                    session.AddMessage("System", message, false, DialogueMessageType.System);
                }
                
                // 更新五维属性栏显示
                fiveDimensionBar.UpdateFaction(faction);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimDiplomacy] 应用关系值变化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 根据五维关系值变化计算好感度变化
        /// </summary>
        private int CalculateGoodwillChangeFromRelations(RelationChanges changes)
        {
            // 计算五维变化的总和，加权平均
            float totalChange = changes.Trust * 0.3f +
                               changes.Intimacy * 0.25f +
                               changes.Reciprocity * 0.2f +
                               changes.Respect * 0.15f +
                               changes.Influence * 0.1f;

            // 转换为整数，限制在 -15 到 +15 之间
            int goodwillChange = (int)Math.Round(totalChange);
            return Math.Max(-15, Math.Min(15, goodwillChange));
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
        /// 执行 AI 动作
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
                    
                    // 记录重要事件到记忆
                    RecordSignificantEventForAction(action);
                }
                else
                {
                    Log.Warning($"[RimDiplomacy] Action failed: {result.Message}");
                    // 如果动作执行失败，添加一条系统消息
                    session.AddMessage("System", $"无法执行动作 '{action.ActionType}': {result.Message}", false, DialogueMessageType.System);
                }
            }
        }

        /// <summary>
        /// 为执行的 AI 动作记录重要事件（只更新内存）
        /// </summary>
        private void RecordSignificantEventForAction(AIAction action)
        {
            SignificantEventType? eventType = action.ActionType switch
            {
                "adjust_goodwill" => SignificantEventType.GoodwillChanged,
                "send_gift" => SignificantEventType.GiftSent,
                "request_aid" => SignificantEventType.AidRequested,
                "declare_war" => SignificantEventType.WarDeclared,
                "make_peace" => SignificantEventType.PeaceMade,
                "request_caravan" => SignificantEventType.TradeCaravan,
                "reject_request" => null,
                _ => null
            };

            if (eventType.HasValue)
            {
                string description = $"AI executed {action.ActionType} action";
                // 只更新内存，不保存到文件
                LeaderMemoryManager.Instance.RecordSignificantEvent(faction, eventType.Value, Faction.OfPlayer, description);
            }
        }

        private void SaveFactionMemory()
        {
            if (session == null || session.messages == null) return;

            // 只更新内存中的记忆，不保存到文件
            // 文件保存由存档保存时统一处理
            LeaderMemoryManager.Instance.UpdateFromDialogue(faction, session.messages);
        }

        private void AddFallbackResponse(string playerMessage)
        {
            string senderName = GetSenderName();
            string response = GenerateSimulatedResponse(playerMessage);
            session.AddMessage(senderName, response, false);
            
            // 保存记忆
            SaveFactionMemory();
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

