using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimChat.AI;
using RimChat.Memory;
using RimChat.Config;
using RimChat.DiplomacySystem;
using RimChat.Persistence;
using RimChat.Util;
using RimChat.Core;

namespace RimChat.UI
{
    /// <summary>/// 逐字outputstate
 ///</summary>
    public class TypewriterState
    {
        public int VisibleCharCount = 0;
        public float AccumulatedTime = 0f;
        public bool IsComplete = false;
        public string FullText = "";
        public string DisplayText = "";
    }

    [StaticConstructorOnStartup]
    public partial class Dialog_DiplomacyDialogue : Window
    {
        private enum DialogueCloseIntent
        {
            Normal = 0,
            SwitchFaction = 1
        }

        private readonly Faction faction;
        private readonly Pawn negotiator;
        private FactionDialogueSession session;
        private DialogueCloseIntent closeIntent = DialogueCloseIntent.Normal;
        private readonly DiplomacyConversationController conversationController = new DiplomacyConversationController();
        private string inputText = "";
        private Vector2 messageScrollPosition = Vector2.zero;
        private Vector2 factionScrollPosition = Vector2.zero;
        private Vector2 blockedReasonScrollPosition = Vector2.zero;
        private int lastMessageCount = 0;
        private readonly int sessionMessageBaselineCount;
        private bool sessionCloseSummaryCommitted = false;
        private bool userIsScrolling = false;
        private const int MAX_INPUT_LENGTH = 500;
        private const float FACTION_LIST_WIDTH = 220f;
        private const float INPUT_AREA_HEIGHT = 80f;
        private const float STRATEGY_BAR_HEIGHT = 36f;
        private const float TIME_GAP_THRESHOLD_MINUTES = 15f;
        private const float BUBBLE_CORNER_RADIUS = 12f;
        private const float LayoutHeaderTop = 45f;
        private const float LayoutPanelPadding = 10f;
        private const float LayoutTabsHeight = 32f;
        private const float LayoutTabsSpacing = 4f;
        private const float LayoutTraderCardHeight = 60f;
        private const float LayoutTraderCardSpacing = 65f;
        private const float LayoutTitleBarHeight = 40f;
        private const float LayoutTitleLeftPadding = 15f;
        private const float LayoutTitleTopPadding = 8f;
        private const float LayoutTitleRightPadding = 45f;
        private const float LayoutCloseButtonSize = 30f;
        private const float LayoutFactionInnerPadding = 8f;
        private const float LayoutFactionHeaderHeight = 31f;
        private const float LayoutFactionRowHeight = 62f;
        private const float LayoutFactionRowSpacing = 4f;
        private const float LayoutFactionVerticalLineY = 26f;
        private const float LayoutGoodwillAnimOffsetX = 63f;
        private const float LayoutGoodwillAnimOffsetY = 32f;
        private const string DialogueInputControlName = "DialogueInput";
        
        // 玩家message气泡颜色 #91ed61
        private static readonly Color PlayerBubbleColor = new Color(0.58f, 0.88f, 0.43f, 1f);
        private static readonly Color PlayerBubbleColorDark = new Color(0.52f, 0.81f, 0.38f, 1f);
        // AImessage气泡颜色
        private static readonly Color AIBubbleColor = new Color(0.25f, 0.26f, 0.3f, 0.95f);

        // Faction位置映射 (used for动画定位)
        private readonly Dictionary<Faction, Rect> factionRowRects = new Dictionary<Faction, Rect>();
        private readonly Dictionary<Faction, float> goodwillValueRevealUntil = new Dictionary<Faction, float>();
        private readonly Dictionary<Faction, float> goodwillHoverAlpha = new Dictionary<Faction, float>();
        private const float GOODWILL_VALUE_REVEAL_SECONDS = 2.5f;

        // 逐字output效果
        private Dictionary<DialogueMessageData, TypewriterState> typewriterStates = new Dictionary<DialogueMessageData, TypewriterState>();
        private float lastTypewriterUpdate = 0f;

        // Social经验浮动动画state
        private float socialExpAnimStartTime = -100f;
        private int lastExpAmount = 0;

        // 通讯台environment音效
        private Sustainer sustainer;

        public override Vector2 InitialSize => new Vector2(900f, 700f);

        public Dialog_DiplomacyDialogue(Faction faction, Pawn negotiator = null, bool muteOpenSound = false)
        {
            this.faction = faction;
            this.negotiator = negotiator;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
            closeOnAccept = false;
            closeOnCancel = true;
            onlyOneOfTypeAllowed = false;
            forcePause = true;

            // Settings打开和关闭音效
            if (!muteOpenSound)
            {
                this.soundAppear = DefDatabase<SoundDef>.GetNamed("CommsWindow_Open");
            }
            this.soundClose = DefDatabase<SoundDef>.GetNamed("CommsWindow_Close");

            session = GameComponent_DiplomacyManager.Instance?.GetOrCreateSession(faction);
            if (session != null)
            {
                session.MarkAsRead();
            }
            sessionMessageBaselineCount = session?.messages?.Count ?? 0;
            RefreshPresenceOnDialogueOpen();

            // 订阅goodwill变化event
            GoodwillChangeAnimator.OnGoodwillChanged += OnGoodwillChanged;

            Log.Message($"[RimChat] Dialogue opened with {faction.Name}, messages: {session?.messages.Count ?? 0}, AI configured: {AIChatService.Instance.IsConfigured()}");
        }

        public override void PostOpen()
        {
            base.PostOpen();
            if (this.sustainer == null)
            {
                SoundDef ambience = DefDatabase<SoundDef>.GetNamed("RadioComms_Ambience", false);
                if (ambience != null)
                {
                    SoundInfo info = SoundInfo.OnCamera(MaintenanceType.None);
                    this.sustainer = ambience.TrySpawnSustainer(info);
                }
            }
        }

        public override void PreClose()
        {
            if (!IsSwitchingFactionOnClose())
            {
                TryCommitDiplomacySessionSummaryOnClose();
                LockPresenceCacheOnDialogueClose();
                conversationController.CancelPendingRequest(session);
                CancelStrategySuggestionRequest();
            }

            if (this.sustainer != null)
            {
                this.sustainer.End();
                this.sustainer = null;
            }
            base.PreClose();
            // 取消订阅event
            GoodwillChangeAnimator.OnGoodwillChanged -= OnGoodwillChanged;

            // 清理逐字state
            typewriterStates.Clear();
        }

        private void TryCommitDiplomacySessionSummaryOnClose()
        {
            if (sessionCloseSummaryCommitted)
            {
                return;
            }

            sessionCloseSummaryCommitted = true;
            if (session == null || session.messages == null || faction == null)
            {
                return;
            }

            if (session.messages.Count <= sessionMessageBaselineCount)
            {
                return;
            }

            DialogueSummaryService.TryRecordDiplomacySessionSummary(
                faction,
                session.messages,
                sessionMessageBaselineCount);

            RpgNpcDialogueArchiveManager.Instance.RecordDiplomacySummary(
                negotiator,
                faction,
                session.messages,
                sessionMessageBaselineCount);
        }

        /// <summary>/// goodwill变化eventprocessing
 ///</summary>
        private void OnGoodwillChanged(Faction changedFaction, int changeAmount)
        {
            if (changedFaction == null) return;
            goodwillValueRevealUntil[changedFaction] = Time.realtimeSinceStartup + GOODWILL_VALUE_REVEAL_SECONDS;

            // Lookupfaction在列表中的位置
            if (factionRowRects.TryGetValue(changedFaction, out Rect rowRect))
            {
                // 计算动画起始位置 (在goodwill数values附近)
                Vector2 startPos = new Vector2(
                    rowRect.x + LayoutGoodwillAnimOffsetX,
                    rowRect.y + LayoutGoodwillAnimOffsetY
                );

                // 创建动画
                GoodwillChangeAnimator.CreateAnimation(changedFaction, changeAmount, startPos);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            // 更新逐字output效果
            UpdateTypewriterEffect();

            DrawTitleBar(inRect);

            Rect factionListRect = new Rect(
                inRect.x,
                inRect.y + LayoutHeaderTop,
                FACTION_LIST_WIDTH,
                inRect.height - LayoutHeaderTop - LayoutPanelPadding);
            DrawFactionList(factionListRect);

            float rightX = inRect.x + FACTION_LIST_WIDTH + LayoutPanelPadding;
            float rightWidth = inRect.width - FACTION_LIST_WIDTH - LayoutPanelPadding;
            float contentY = LayoutHeaderTop;

            Rect tabsRect = new Rect(rightX, inRect.y + contentY, rightWidth, LayoutTabsHeight);
            contentY += DrawDialogueMainTabs(tabsRect) + LayoutTabsSpacing;

            if (IsChatTabActive())
            {
                TradeShip tradeShip = GetTradeShip();
                if (tradeShip != null)
                {
                    Rect cardRect = new Rect(rightX, inRect.y + contentY, rightWidth, LayoutTraderCardHeight);
                    DrawOrbitalTraderCard(cardRect, tradeShip);
                    contentY += LayoutTraderCardSpacing;
                }

                contentY += DrawExpandedActions(new Rect(rightX, inRect.y + contentY, rightWidth, inRect.height - contentY));
            }

            float contentHeight = inRect.height - contentY - LayoutPanelPadding;
            Rect rightPanelRect = new Rect(rightX, inRect.y + contentY, rightWidth, contentHeight);
            if (IsChatTabActive())
            {
                DrawChatArea(rightPanelRect);
            }
            else
            {
                DrawSocialCirclePanel(rightPanelRect);
            }

            // 绘制goodwill变化动画 (在所有 UI 之上)
            GoodwillChangeAnimator.UpdateAndDrawAnimations();
        }

        private void DrawTitleBar(Rect inRect)
        {
            Widgets.DrawBoxSolid(new Rect(inRect.x, inRect.y, inRect.width, LayoutTitleBarHeight), new Color(0.15f, 0.15f, 0.18f));
            
            // 左侧标题: RimChat Terminal
            Text.Font = GameFont.Medium;
            GUI.color = new Color(0.9f, 0.9f, 0.95f);
            string title = "RimChat_TerminalTitle".Translate();
            Widgets.Label(new Rect(inRect.x + LayoutTitleLeftPadding, inRect.y + LayoutTitleTopPadding, 250f, 30f), title);

            // 中间: 当前factionname
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.7f, 0.7f, 0.75f);
            string factionTitle = faction.Name ?? "Unknown";
            float factionTitleWidth = Text.CalcSize(factionTitle).x;
            float centerX = inRect.x + (inRect.width - factionTitleWidth) / 2f;
            Widgets.Label(new Rect(centerX, inRect.y + 10f, factionTitleWidth + 10f, 25f), factionTitle);
            DrawCurrentFactionPresenceStatus(new Rect(centerX + factionTitleWidth + 14f, inRect.y + 9f, 132f, 24f));

            // 右侧: 天气和时间
            string weatherTimeText = GetWeatherAndTimeText();
            float weatherTimeWidth = Text.CalcSize(weatherTimeText).x;
            GUI.color = new Color(0.8f, 0.8f, 0.85f);
            Widgets.Label(new Rect(inRect.xMax - weatherTimeWidth - LayoutTitleRightPadding, inRect.y + 10f, weatherTimeWidth + 10f, 25f), weatherTimeText);

            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            Rect closeRect = new Rect(inRect.xMax - (LayoutCloseButtonSize + 5f), inRect.y + 5f, LayoutCloseButtonSize, LayoutCloseButtonSize);
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

            // Get温度
            float temperature = map.mapTemperature?.OutdoorTemp ?? 0f;
            string tempText = $"{temperature:F0}°C";

            // Get游戏时间
            int hour = GenLocalDate.HourOfDay(map);
            int minute = (int)((GenLocalDate.DayPercent(map) * 24f - hour) * 60f);
            string timeText = $"{hour:D2}:{minute:D2}";

            return $"{tempText}  {timeText}";
        }

        private void DrawFactionList(Rect rect)
        {
            // 清空位置映射
            factionRowRects.Clear();

            Widgets.DrawBoxSolid(rect, new Color(0.085f, 0.085f, 0.11f, 0.98f));
            GUI.color = new Color(0.26f, 0.26f, 0.32f, 0.95f);
            Widgets.DrawBox(rect);
            GUI.color = Color.white;

            Rect innerRect = rect.ContractedBy(LayoutFactionInnerPadding);

            Text.Font = GameFont.Small;
            GUI.color = new Color(0.82f, 0.86f, 0.92f);
            Widgets.Label(new Rect(innerRect.x, innerRect.y, innerRect.width, 22f), "RimChat_FactionsTitle".Translate());
            GUI.color = Color.white;

            GUI.color = new Color(0.42f, 0.45f, 0.52f, 0.45f);
            Widgets.DrawLineHorizontal(innerRect.x, innerRect.y + LayoutFactionVerticalLineY, innerRect.width);
            GUI.color = Color.white;

            var allFactions = GetAvailableFactions(true);
            CleanupGoodwillHoverAlpha(allFactions);

            float rowHeight = LayoutFactionRowHeight;
            float contentHeight = allFactions.Count * (rowHeight + LayoutFactionRowSpacing);

            Rect viewRect = new Rect(0f, 0f, innerRect.width - 16f, Mathf.Max(contentHeight, innerRect.height - (LayoutFactionHeaderHeight + 4f)));

            Rect scrollRect = new Rect(innerRect.x, innerRect.y + LayoutFactionHeaderHeight, innerRect.width, innerRect.height - LayoutFactionHeaderHeight);
            factionScrollPosition = GUI.BeginScrollView(scrollRect, factionScrollPosition, viewRect);

            float curY = 0f;
            foreach (var f in allFactions)
            {
                Rect rowRect = new Rect(5f, curY, viewRect.width - 10f, rowHeight);
                DrawFactionListItem(f, rowRect);

                // Recordfaction位置 (转换为屏幕坐标used for动画)
                Rect screenRect = new Rect(
                    rect.x + 8f + rowRect.x,
                    rect.y + 8f + 31f + rowRect.y - factionScrollPosition.y,
                    rowRect.width,
                    rowRect.height
                );
                factionRowRects[f] = screenRect;

                curY += rowHeight + LayoutFactionRowSpacing;
            }

            GUI.EndScrollView();

            // 检查goodwill变化
            GoodwillChangeAnimator.CheckGoodwillChanges(allFactions);
        }

        private List<Faction> GetAvailableFactions(bool refreshPresence = false)
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
            if (refreshPresence)
            {
                GameComponent_DiplomacyManager.Instance?.RefreshPresenceForFactions(list);
            }
            return list
                .OrderBy(GetPresenceSortWeight)
                .ThenByDescending(f => f.PlayerGoodwill)
                .ToList();
        }

        private int GetPresenceSortWeight(Faction factionToSort)
        {
            var status = GameComponent_DiplomacyManager.Instance?.GetPresenceStatus(factionToSort) ?? FactionPresenceStatus.Online;
            switch (status)
            {
                case FactionPresenceStatus.Online:
                    return 0;
                case FactionPresenceStatus.DoNotDisturb:
                    return 1;
                default:
                    return 2;
            }
        }

        private void DrawFactionListItem(Faction f, Rect rect)
        {
            bool isSelected = f == faction;
            bool hasUnread = GameComponent_DiplomacyManager.Instance?.HasUnreadMessages(f) ?? false;
            bool isHovering = Mouse.IsOver(rect);
            int goodwill = f.PlayerGoodwill;
            Color goodwillColor = GetGoodwillColor(goodwill);
            float hoverAlpha = UpdateGoodwillHoverAlpha(f, isHovering);
            bool showGoodwillValue = hoverAlpha > 0.01f;

            Color rowColor = isSelected
                ? new Color(0.18f, 0.4f, 0.66f, 0.72f)
                : isHovering
                    ? new Color(0.15f, 0.17f, 0.23f, 0.82f)
                    : new Color(0.11f, 0.12f, 0.16f, 0.78f);
            Widgets.DrawBoxSolid(rect, rowColor);
            GUI.color = isSelected ? new Color(0.42f, 0.58f, 0.85f, 0.95f) : new Color(0.25f, 0.28f, 0.35f, 0.95f);
            Widgets.DrawBox(rect);
            GUI.color = Color.white;

            if (hasUnread && !isSelected)
            {
                Widgets.DrawBoxSolid(new Rect(rect.x + 2f, rect.y + 6f, 3f, rect.height - 12f), new Color(0.24f, 0.82f, 0.96f));
            }

            float x = rect.x + 8f + (hasUnread && !isSelected ? 5f : 0f);
            float y = rect.y + 6f;

            Rect iconFrame = new Rect(x, y, 40f, 40f);
            Widgets.DrawBoxSolid(iconFrame, new Color(0.18f, 0.2f, 0.25f, 0.95f));
            GUI.color = new Color(0.34f, 0.38f, 0.46f, 0.9f);
            Widgets.DrawBox(iconFrame);
            GUI.color = Color.white;

            Rect iconRect = iconFrame.ContractedBy(2f);
            Texture2D factionIcon = f.def?.FactionIcon;
            if (factionIcon != null && factionIcon != BaseContent.BadTex)
            {
                GUI.DrawTexture(iconRect, factionIcon);
            }
            x += 48f;

            float rightReserved = Mathf.Lerp(8f, 62f, hoverAlpha);
            float contentWidth = Mathf.Max(50f, rect.xMax - x - rightReserved);
            Rect nameRect = new Rect(x, y + 1f, contentWidth, 32f);
            GUI.color = isSelected ? Color.white : new Color(0.9f, 0.93f, 0.98f);
            bool previousWordWrap = Text.WordWrap;
            Text.WordWrap = true;
            Widgets.Label(nameRect, f.Name ?? "Unknown");
            Text.WordWrap = previousWordWrap;

            Rect presenceRect = new Rect(x, y + 34f, contentWidth, 16f);
            Text.Font = GameFont.Tiny;
            DrawFactionPresenceStatus(f, presenceRect, false);

            string goodwillText = goodwill >= 0 ? $"+{goodwill}" : goodwill.ToString();
            Rect goodwillRect = new Rect(rect.xMax - 60f, y + 1f, 52f, 20f);
            if (showGoodwillValue)
            {
                GUI.color = new Color(goodwillColor.r, goodwillColor.g, goodwillColor.b, hoverAlpha);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(goodwillRect, goodwillText);
                Text.Anchor = TextAnchor.UpperLeft;
            }

            Rect relationBgRect = new Rect(rect.xMax - 66f, y + 33f, 58f, 18f);
            Widgets.DrawBoxSolid(relationBgRect, new Color(goodwillColor.r * 0.3f, goodwillColor.g * 0.3f, goodwillColor.b * 0.3f, 0.55f));
            GUI.color = goodwillColor;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(relationBgRect, GetRelationLabelShort(goodwill));
            Text.Anchor = TextAnchor.UpperLeft;

            Text.Font = GameFont.Small;
            GUI.color = Color.white;

                if (!isSelected && Widgets.ButtonInvisible(rect))
                {
                    MarkCloseAsFactionSwitch();
                    // 关闭音效设为null以静音
                    this.soundClose = null;
                    Find.WindowStack.Add(new Dialog_DiplomacyDialogue(f, negotiator, true));
                    Close();
                }
        }

        private float UpdateGoodwillHoverAlpha(Faction faction, bool isHovering)
        {
            if (faction == null) return 0f;
            float current = goodwillHoverAlpha.TryGetValue(faction, out float alpha) ? alpha : 0f;
            float target = isHovering ? 1f : 0f;
            float next = Mathf.MoveTowards(current, target, 0.1f);
            goodwillHoverAlpha[faction] = next;
            return next;
        }

        private void CleanupGoodwillHoverAlpha(List<Faction> activeFactions)
        {
            if (activeFactions == null) return;
            for (int i = goodwillHoverAlpha.Count - 1; i >= 0; i--)
            {
                Faction key = goodwillHoverAlpha.Keys.ElementAt(i);
                if (!activeFactions.Contains(key))
                {
                    goodwillHoverAlpha.Remove(key);
                }
            }
        }

        private TradeShip GetTradeShip()
        {
            if (faction == null || Find.CurrentMap == null) return null;
            return Find.CurrentMap.passingShipManager?.passingShips
                .FirstOrDefault(x => x.Faction == faction && x is TradeShip) as TradeShip;
        }

        private void DrawOrbitalTraderCard(Rect rect, TradeShip tradeShip)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.15f, 0.2f, 0.25f, 0.8f));
            Widgets.DrawBox(rect);

            Rect innerRect = rect.ContractedBy(8f);
            
            // Text区域 - 移除图标, 直接从左侧开始
            float textX = innerRect.x;
            Rect labelRect = new Rect(textX, innerRect.y, innerRect.width - textX - 120f, 22f);
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.9f, 0.9f, 1f);
            
            // Display商船name和类型
            string shipName = tradeShip.name;
            string traderKind = tradeShip.def.LabelCap; // 使用 LabelCap get首字母大写的类型name
            Widgets.Label(labelRect, "RimChat_OrbitalTraderAvailable".Translate(shipName, traderKind));
            
            GUI.color = Color.white;
            
            Rect descRect = new Rect(textX, innerRect.y + 24f, innerRect.width - textX - 120f, 20f);
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(descRect, "RimChat_ClickToTrade".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            // Button区域
            Rect btnRect = new Rect(innerRect.xMax - 110f, innerRect.y + 6f, 110f, 32f);
            bool canTrade = negotiator != null && negotiator.Map == Find.CurrentMap && !negotiator.Downed && !negotiator.InMentalState;
            
            if (canTrade)
            {
                if (Widgets.ButtonText(btnRect, "RimChat_TradeButton".Translate()))
                {
                    Find.WindowStack.Add(new Dialog_Trade(negotiator, tradeShip, false));
                    Close();
                }
            }
            else
            {
                GUI.color = Color.gray;
                Widgets.DrawBoxSolid(btnRect, new Color(0.3f, 0.3f, 0.3f));
                Widgets.Label(btnRect, "RimChat_TradeButton".Translate());
                GUI.color = Color.white;
                
                if (Mouse.IsOver(btnRect))
                {
                    TooltipHandler.TipRegion(btnRect, "RimChat_NegotiatorUnavailable".Translate());
                }
            }
        }

        private float DrawExpandedActions(Rect rect)
        {
            float curY = 0f;
            
            // 皇权 DLC 动作
            if (ModsConfig.RoyaltyActive && faction.def == FactionDefOf.Empire && negotiator != null && negotiator.royalty != null)
            {
                float height = DrawRoyaltyActions(new Rect(rect.x, rect.y + curY, rect.width, rect.height - curY));
                curY += height;
            }

            // 任务动作
            float questHeight = DrawQuestActions(new Rect(rect.x, rect.y + curY, rect.width, rect.height - curY));
            curY += questHeight;

            return curY;
        }

        private float DrawRoyaltyActions(Rect rect)
        {
            if (negotiator.royalty == null) return 0f;

            // 检查whether有可用许可 (包括冷却中的)
            var permits = negotiator.royalty.AllFactionPermits.Where(p => p.Faction == faction).ToList();
            if (!permits.Any()) return 0f;

            float height = 40f; // 标题高度
            
            // 标题
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, 30f);
            Widgets.DrawBoxSolid(headerRect, new Color(0.2f, 0.15f, 0.1f));
            Text.Font = GameFont.Small;
            GUI.color = new Color(1f, 0.8f, 0.4f);
            Widgets.Label(new Rect(headerRect.x + 10f, headerRect.y + 5f, headerRect.width - 20f, 20f), "RimChat_RoyalActions".Translate());
            GUI.color = Color.white;

            // Contents区域
            // 这里我们只提供一个button来打开原版的许可interface, 或者如果可以, 直接display通讯台specific的许可
            // 考虑到通讯台的许可通常是 CallAid 之类的, 我们直接display这些
            
            float buttonHeight = 30f;
            float buttonY = rect.y + 35f;
            
            foreach (var permit in permits)
            {
                // 只display可以通过通讯台使用的许可 (通常是 workerClass 为 RoyalTitlePermitWorker_CallAid 或类似的)
                // 简单起见, 我们列出所有非被动许可
                if (permit.Permit.workerClass != null)
                {
                    Rect btnRect = new Rect(rect.x, buttonY, rect.width, buttonHeight);
                    
                    bool onCooldown = permit.OnCooldown;
                    string label = permit.Permit.LabelCap;
                    if (onCooldown)
                    {
                        label += " (" + "RimChat_PermitCooldown".Translate() + ")";
                    }
                    else
                    {
                        label += " (" + "RimChat_UsePermit".Translate() + ")";
                    }

                    if (Widgets.ButtonText(btnRect, label, active: !onCooldown))
                    {
                        // 许可权通常需要目标select, 直接调用比较复杂. 提示玩家在正确位置使用.
                        Messages.Message("RimChat_UsePermitHint".Translate(), MessageTypeDefOf.RejectInput, false);
                    }
                    buttonY += buttonHeight + 5f;
                    height += buttonHeight + 5f;
                }
            }

            return height + 5f;
        }

        private float DrawQuestActions(Rect rect)
        {
            var quests = Find.QuestManager.QuestsListForReading
                .Where(q => q.State == QuestState.Ongoing && q.InvolvedFactions.Contains(faction) && !q.hidden)
                .ToList();

            if (!quests.Any()) return 0f;

            float height = 40f;
            
            // 标题
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, 30f);
            Widgets.DrawBoxSolid(headerRect, new Color(0.1f, 0.15f, 0.2f));
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.4f, 0.8f, 1f);
            Widgets.Label(new Rect(headerRect.x + 10f, headerRect.y + 5f, headerRect.width - 20f, 20f), "RimChat_QuestActions".Translate());
            GUI.color = Color.white;

            float buttonHeight = 30f;
            float buttonY = rect.y + 35f;

            foreach (var quest in quests)
            {
                Rect btnRect = new Rect(rect.x, buttonY, rect.width, buttonHeight);
                if (Widgets.ButtonText(btnRect, quest.name))
                {
                    MainTabWindow_Quests questsWindow = (MainTabWindow_Quests)MainButtonDefOf.Quests.TabWindow;
                    questsWindow.Select(quest);
                    Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Quests, true);
                    Close();
                }
                buttonY += buttonHeight + 5f;
                height += buttonHeight + 5f;
            }

            return height + 5f;
        }

        private void DrawChatArea(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.08f, 0.1f));
            Widgets.DrawBox(rect);

            Rect innerRect = rect.ContractedBy(10f);

            float inputHeight = INPUT_AREA_HEIGHT;
            float controlsHeight = STRATEGY_BAR_HEIGHT;
            float spacing = 10f;
            float messagesHeight = innerRect.height - inputHeight - controlsHeight - spacing * 2f;
            if (messagesHeight < 60f)
            {
                messagesHeight = 60f;
            }

            // Message区域
            Rect messagesRect = new Rect(innerRect.x, innerRect.y, innerRect.width, messagesHeight);
            DrawMessages(messagesRect);

            // 分隔线1 - message与控制区之间
            float line1Y = innerRect.y + messagesHeight + 5f;
            Color oldLineColor = GUI.color;
            GUI.color = new Color(0.55f, 0.58f, 0.66f, 0.35f);
            Widgets.DrawLineHorizontal(innerRect.x, line1Y, innerRect.width);

            // 单行控制区: 策略button
            float controlsY = line1Y + 5f;
            Rect controlsRect = new Rect(innerRect.x, controlsY, innerRect.width, controlsHeight);
            DrawControlsRow(controlsRect);

            // 分隔线2 - 控制区与input框之间
            float line2Y = controlsY + controlsHeight + 5f;
            Widgets.DrawLineHorizontal(innerRect.x, line2Y, innerRect.width);
            GUI.color = oldLineColor;

            // Input区域
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
                float maxSystemWidth = (rect.width - 16f) - 60f;
                float maxBubbleWidth = Mathf.Min(480f, (rect.width - 16f) * 0.75f);
                float estBubbleWidth = msg.IsSystemMessage() ? CalculateBubbleWidth(msg, maxSystemWidth) : CalculateBubbleWidth(msg, maxBubbleWidth);
                float msgHeight = CalculateMessageHeight(msg, estBubbleWidth);
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
                
                float maxSystemWidth = viewRect.width - 60f;
                float maxBubbleWidth = Mathf.Min(480f, viewRect.width * 0.75f);
                float bubbleWidth = msg.IsSystemMessage() ? CalculateBubbleWidth(msg, maxSystemWidth) : CalculateBubbleWidth(msg, maxBubbleWidth);
                float msgHeight = CalculateMessageHeight(msg, bubbleWidth);
                
                if (msg.IsSystemMessage())
                {
                    // Systemmessage: 左对齐, 使用完整宽度
                    Rect msgRect = new Rect(20f, curY, bubbleWidth, msgHeight);
                    DrawRoundedMessageBubble(msg, msgRect);
                }
                else
                {
                    // 普通message: 使用气泡样式
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
                return "RimChat_MinutesAgo".Translate(Mathf.RoundToInt(minutes));
            }
            else if (hours < 24f)
            {
                return "RimChat_HoursAgo".Translate(Mathf.RoundToInt(hours));
            }
            else
            {
                return "RimChat_DaysAgo".Translate(Mathf.RoundToInt(days));
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
                textColor = new Color(0.1f, 0.1f, 0.1f);
                senderColor = new Color(0.2f, 0.3f, 0.15f);
            }
            else
            {
                bubbleColor = AIBubbleColor;
                textColor = new Color(0.95f, 0.95f, 0.97f);
                senderColor = new Color(0.75f, 0.8f, 0.9f);
            }

            // 绘制阴影 (更柔和, 现代的下拉阴影)
            Rect shadowRect = new Rect(rect.x + 1f, rect.y + 3f, rect.width, rect.height);
            DrawRoundedRect(shadowRect, new Color(0f, 0f, 0f, 0.12f), BUBBLE_CORNER_RADIUS);

            // 绘制气泡背景 (圆角)
            DrawRoundedRect(rect, bubbleColor, BUBBLE_CORNER_RADIUS);

            // 增加内边距
            float padding = 16f;
            float contentX = rect.x + padding;
            float contentY = rect.y + 12f;
            float contentWidth = rect.width - padding * 2f;

            // 发送者name与时间戳 (头部)
            Text.Font = GameFont.Tiny;
            float headerHeight = 18f; // Ensure enough vertical space for text
            
            GUI.color = senderColor;
            Rect senderRect = new Rect(contentX, contentY, contentWidth * 0.7f, headerHeight);
            Widgets.Label(senderRect, msg.sender);

            string timeStr = GetTimestampString(msg);
            float timeWidth = Text.CalcSize(timeStr).x + 5f;
            Rect timeRect = new Rect(rect.xMax - timeWidth - padding, contentY, timeWidth, headerHeight);
            GUI.color = new Color(senderColor.r, senderColor.g, senderColor.b, 0.65f);
            Widgets.Label(timeRect, timeStr);

            // Contents区域起始位置
            contentY += headerHeight + 2f;
            
            Text.Font = GameFont.Small;
            GUI.color = textColor;

            // Messagecontents (使用真正的逐字outputtext进行排版渲染)
            string displayText = GetDisplayText(msg);
            float actualTextHeight = Text.CalcHeight(displayText, contentWidth);
            Rect contentRect = new Rect(contentX, contentY, contentWidth, actualTextHeight);
            Widgets.Label(contentRect, displayText);

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private static Texture2D _whiteTexture;
        private static Texture2D WhiteTexture => _whiteTexture;
        
        static Dialog_DiplomacyDialogue()
        {
            _whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _whiteTexture.SetPixel(0, 0, Color.white);
            _whiteTexture.Apply();
        }

        private static Texture2D _circleTexture;
        private static Texture2D CircleTexture
        {
            get
            {
                if (_circleTexture == null)
                {
                    int radius = 32;
                    int size = radius * 2;
                    _circleTexture = new Texture2D(size, size, TextureFormat.ARGB32, false);
                    Color[] pixels = new Color[size * size];
                    Vector2 center = new Vector2(radius, radius);
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                            float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                            pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                        }
                    }
                    _circleTexture.SetPixels(pixels);
                    _circleTexture.Apply();
                }
                return _circleTexture;
            }
        }

        private void DrawRoundedRect(Rect rect, Color color, float radius)
        {
            GUI.color = color;
            float r = Mathf.Min(radius, rect.width / 2f, rect.height / 2f);

            // 绘制中心rectangle及十字区域
            GUI.DrawTexture(new Rect(rect.x + r, rect.y, rect.width - r * 2f, rect.height), WhiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y + r, rect.width, rect.height - r * 2f), WhiteTexture);

            // 使用高清抗锯齿圆角纹理进行圆滑边角绘制 (Unity GUI texCoords 中 0,0 为左下角)
            // 左上角
            GUI.DrawTextureWithTexCoords(new Rect(rect.x, rect.y, r, r), CircleTexture, new Rect(0f, 0.5f, 0.5f, 0.5f));
            // 右上角
            GUI.DrawTextureWithTexCoords(new Rect(rect.xMax - r, rect.y, r, r), CircleTexture, new Rect(0.5f, 0.5f, 0.5f, 0.5f));
            // 左下角
            GUI.DrawTextureWithTexCoords(new Rect(rect.x, rect.yMax - r, r, r), CircleTexture, new Rect(0f, 0f, 0.5f, 0.5f));
            // 右下角
            GUI.DrawTextureWithTexCoords(new Rect(rect.xMax - r, rect.yMax - r, r, r), CircleTexture, new Rect(0.5f, 0f, 0.5f, 0.5f));

            GUI.color = Color.white;
        }

        private void DrawInputArea(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.15f));

            float padding = 10f;
            float inputWidth = rect.width - padding * 2f - 90f;
            float inputHeight = rect.height - padding * 2f - 20f;

            Rect textRect = new Rect(rect.x + padding, rect.y + padding, inputWidth, inputHeight);
            GUI.SetNextControlName(DialogueInputControlName);
            
            Widgets.DrawBoxSolid(textRect, new Color(0.18f, 0.18f, 0.22f));
            Rect innerTextRect = textRect.ContractedBy(5f);

            bool showReinitiateButton = false;
            string blockedReason = null;
            bool inputBlocked = IsInputBlockedByPresence(out blockedReason, out showReinitiateButton);

            if (!inputBlocked)
            {
                HandleInputEvents();
            }

            string newInput;
            if (inputBlocked)
            {
                GUI.enabled = false;
                newInput = Widgets.TextArea(innerTextRect, inputText);
                GUI.enabled = true;
            }
            else
            {
                newInput = Widgets.TextArea(innerTextRect, inputText);
            }

            if (!inputBlocked && newInput.Length <= MAX_INPUT_LENGTH)
            {
                inputText = newInput;
            }

            int charCount = inputText.Length;
            Color countColor = charCount > MAX_INPUT_LENGTH * 0.8f ? Color.yellow : Color.gray;
            GUI.color = countColor;
            Text.Font = GameFont.Tiny;
            Rect countRect = new Rect(rect.x + padding, rect.y + rect.height - 20f, 100f, 18f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(countRect, $"{charCount}/{MAX_INPUT_LENGTH}");
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            Rect sendRect = new Rect(rect.xMax - 85f, rect.y + padding, 75f, inputHeight);
            bool canSend = !string.IsNullOrWhiteSpace(inputText) && !session.isWaitingForResponse && charCount <= MAX_INPUT_LENGTH && !inputBlocked;

            Color buttonColor = canSend ? new Color(0.2f, 0.6f, 1f, 0.9f) : new Color(0.3f, 0.3f, 0.35f, 0.5f);
            GUI.color = buttonColor;
            Widgets.DrawBoxSolid(sendRect, buttonColor);
            GUI.color = Color.white;

            GUI.enabled = canSend;
            if (Widgets.ButtonText(sendRect, "RimChat_SendButton".Translate()))
            {
                SendMessage();
            }
            GUI.enabled = true;
            DrawPotentialActionsHint(sendRect);

            if (session.isWaitingForResponse)
            {
                Rect typingRect = new Rect(rect.x + padding + 110f, rect.y + rect.height - 22f, 320f, 20f);
                DrawDiplomacyTypingStatus(typingRect);
            }
            else if (!string.IsNullOrEmpty(session.aiError))
            {
                Rect errorRect = new Rect(rect.x + padding + 110f, rect.y + rect.height - 20f, 240f, 18f);
                GUI.color = Color.red;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                string errorLabel = "RimChat_ErrorLabel".Translate();
                DrawSingleLineClippedLabel(errorRect, $"{errorLabel}: " + session.aiError.Substring(0, Mathf.Min(30, session.aiError.Length)));
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }
            else if (inputBlocked)
            {
                Rect blockedRect = new Rect(rect.x + padding + 110f, rect.y + rect.height - 21f, 460f, 20f);
                GUI.color = new Color(1f, 0.6f, 0.6f, 0.9f);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                DrawStatusLabelWithVerticalScroll(blockedRect, blockedReason ?? "RimChat_PresenceBlockedOffline".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }

            if (inputBlocked && showReinitiateButton)
            {
                if (DrawReinitiateActionButton(rect))
                {
                    ReinitiateConversation();
                }
            }

            // 绘制social经验上浮动画
            if (Time.time - socialExpAnimStartTime < 2f && negotiator != null)
            {
                float progress = (Time.time - socialExpAnimStartTime) / 2f;
                // 前20%淡入, 后80%淡出
                float alpha = progress < 0.2f ? progress * 5f : (1f - (progress - 0.2f) / 0.8f);
                float yOffset = progress * 40f;
                
                // 在发送button上方区域
                Rect expRect = new Rect(rect.xMax - 180f, rect.y - 15f - yOffset, 170f, 25f);
                
                GUI.color = new Color(0.9f, 0.8f, 0.2f, alpha); // 金色
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(expRect, "RimChat_SocialExpGained".Translate(negotiator.LabelShort, lastExpAmount));
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }
        }

        private void MarkCloseAsFactionSwitch()
        {
            closeIntent = DialogueCloseIntent.SwitchFaction;
        }

        private bool IsSwitchingFactionOnClose()
        {
            return closeIntent == DialogueCloseIntent.SwitchFaction;
        }

        private static void DrawSingleLineClippedLabel(Rect rect, string text)
        {
            bool previousWordWrap = Text.WordWrap;
            TextAnchor previousAnchor = Text.Anchor;
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            Text.WordWrap = false;
            string renderText = (text ?? string.Empty).Truncate(rect.width);
            Widgets.Label(rect, renderText);
            Text.WordWrap = previousWordWrap;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
            GUI.color = previousColor;
        }

        private void DrawStatusLabelWithVerticalScroll(Rect rect, string text)
        {
            string content = (text ?? string.Empty).Replace("\r", string.Empty);
            int lineCount = content.Split('\n').Length;
            if (lineCount <= 1)
            {
                blockedReasonScrollPosition = Vector2.zero;
                DrawSingleLineClippedLabel(rect, content);
                return;
            }

            bool previousWordWrap = Text.WordWrap;
            TextAnchor previousAnchor = Text.Anchor;
            float lineHeight = Mathf.Max(Text.LineHeight, 14f);
            float contentHeight = Mathf.Max(rect.height, lineCount * lineHeight + 2f);
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(1f, rect.width - 14f), contentHeight);
            blockedReasonScrollPosition.y = Mathf.Clamp(blockedReasonScrollPosition.y, 0f, Mathf.Max(0f, contentHeight - rect.height));

            blockedReasonScrollPosition = GUI.BeginScrollView(rect, blockedReasonScrollPosition, viewRect);
            Text.WordWrap = true;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(0f, 0f, viewRect.width, contentHeight), content);
            Text.Anchor = previousAnchor;
            Text.WordWrap = previousWordWrap;
            GUI.EndScrollView();
        }

        private bool DrawReinitiateActionButton(Rect inputAreaRect)
        {
            Text.Font = GameFont.Tiny;
            string label = "↻ " + "RimChat_ReinitiateDialogueButton".Translate();
            float width = Mathf.Clamp(Text.CalcSize(label).x + 14f, 96f, 142f);
            Rect buttonRect = new Rect(inputAreaRect.xMax - width - 10f, inputAreaRect.y + inputAreaRect.height - 22f, width, 18f);

            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.realtimeSinceStartup * 2.4f);
            DrawRoundedRect(buttonRect, new Color(0.12f, 0.21f, 0.27f, 0.95f), 7f);
            GUI.color = new Color(0.42f, 0.78f, 0.98f, pulse);
            Widgets.DrawBox(buttonRect);
            GUI.color = Color.white;

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(buttonRect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(buttonRect, "RimChat_ReinitiateDialogueButton".Translate());
            Text.Font = GameFont.Small;

            return Widgets.ButtonInvisible(buttonRect);
        }

        private void ShowSocialExpAnimation(int amount)
        {
            lastExpAmount = amount;
            socialExpAnimStartTime = Time.time;
        }

        private void HandleInputEvents()
        {
            Event current = Event.current;
            if (!IsSubmitKeyPressed(current) || !IsDialogueInputFocused() || IsImeComposing())
            {
                return;
            }

            if (current.alt)
            {
                inputText += "\n";
                current.Use();
                return;
            }

            if (!CanSendFromKeyboard())
            {
                return;
            }

            current.Use();
            SendMessage();
        }

        private static bool IsSubmitKeyPressed(Event current)
        {
            return current != null &&
                current.type == EventType.KeyDown &&
                (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter);
        }

        private static bool IsImeComposing()
        {
            return !string.IsNullOrEmpty(Input.compositionString);
        }

        private static bool IsDialogueInputFocused()
        {
            return GUI.GetNameOfFocusedControl() == DialogueInputControlName;
        }

        private bool CanSendFromKeyboard()
        {
            return !string.IsNullOrWhiteSpace(inputText) && CanSendMessageNow();
        }

        private float CalculateMessageHeight(DialogueMessageData msg, float width)
        {
            string displayText = GetDisplayText(msg);

            if (msg.IsSystemMessage())
            {
                float systemTextWidth = Mathf.Min(width - 8f, 600f);
                float systemTextHeight = Text.CalcHeight(displayText, systemTextWidth);
                return Mathf.Max(16f, systemTextHeight + 8f);
            }
            
            // 精确计算text高度: based ondynamicoutput的字符重新计算
            float contentWidth = width - 32f;
            float textHeight = Text.CalcHeight(displayText, contentWidth);
            
            // 总高度 = 上内边距(12f) + 头高度(18f) + 间距(2f) + contents高度 + 下内边距(16f) = 48f + textHeight
            float totalHeight = 48f + textHeight;
            return Mathf.Max(50f, totalHeight);
        }

        private float CalculateBubbleWidth(DialogueMessageData msg, float maxWidth)
        {
            // Use the full message for width calculation, so horizontal size remains fixed
            float textWidth = Text.CalcSize(msg.message).x;
            
            if (msg.IsSystemMessage())
            {
                return Mathf.Min(textWidth + 40f, maxWidth);
            }
            
            // Get头部名字和日期的自然宽度
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Tiny;
            float headerWidth = Text.CalcSize(msg.sender).x + Text.CalcSize(GetTimestampString(msg)).x + 25f;
            Text.Font = oldFont;
            
            float maxContentWidth = Mathf.Max(textWidth, headerWidth);
            float estimatedWidth = Mathf.Min(maxContentWidth + 32f, maxWidth);
            
            return Mathf.Clamp(estimatedWidth, 140f, maxWidth);
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
                return "RimChat_JustNow".Translate();
            }
            else if (minutes < 60f)
            {
                return "RimChat_MinutesAgo".Translate(Mathf.RoundToInt(minutes));
            }
            else if (hours < 24f)
            {
                return "RimChat_HoursAgo".Translate(Mathf.RoundToInt(hours));
            }
            else
            {
                return "RimChat_DaysAgo".Translate(Mathf.RoundToInt(days));
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
            if (goodwill >= 80) return "RimChat_RelationAllyShort".Translate();
            if (goodwill >= 40) return "RimChat_RelationFriendShort".Translate();
            if (goodwill >= 0) return "RimChat_RelationNeutralShort".Translate();
            if (goodwill >= -40) return "RimChat_RelationHostileShort".Translate();
            return "RimChat_RelationEnemyShort".Translate();
        }

        private void SendMessage()
        {
            if (string.IsNullOrWhiteSpace(inputText) || session.isWaitingForResponse || session == null || !CanSendMessageNow())
                return;

            string playerMessage = inputText.Trim();
            if (string.IsNullOrEmpty(playerMessage))
                return;

            inputText = "";
            SendPreparedMessage(playerMessage, true);
        }

        private void SendPreparedMessage(string playerMessage, bool clearStrategies)
        {
            if (string.IsNullOrWhiteSpace(playerMessage) || session == null || session.isWaitingForResponse || !CanSendMessageNow())
            {
                return;
            }

            if (clearStrategies)
            {
                ClearPendingStrategySuggestions(session);
            }

            session.AddMessage("RimChat_You".Translate(), playerMessage, true);

            if (!AIChatServiceAsync.Instance.IsConfigured())
            {
                Log.Message("[RimChat] AI not configured, using fallback response");
                AddFallbackResponse(playerMessage);
                return;
            }

            var chatMessages = BuildChatMessages(playerMessage);

            // 捕获 session 对象以在回调中使用, 避免依赖 Window 实例
            var currentSession = session;
            var currentFaction = faction;

            bool queued = conversationController.TrySendDialogueRequest(
                currentSession,
                currentFaction,
                chatMessages,
                onSuccess: response =>
                {
                    AddAIResponseToSession(response, currentSession, currentFaction, playerMessage);
                },
                onError: error =>
                {
                    Log.Warning($"[RimChat] AI request failed: {error}");
                    AddFallbackResponseToSession(playerMessage, currentSession, currentFaction);
                },
                onProgress: null);

            if (!queued)
            {
                Log.Warning("[RimChat] Failed to queue diplomacy AI request; using fallback response.");
                AddFallbackResponseToSession(playerMessage, currentSession, currentFaction);
            }
        }

        private List<ChatMessageData> BuildChatMessages(string playerMessage)
        {
            var chatMessages = new List<ChatMessageData>();

            string systemPrompt = BuildSystemPrompt();
            chatMessages.Add(new ChatMessageData { role = "system", content = systemPrompt });

            if (HasStrategyUsesRemaining(session))
            {
                string strategyContext = BuildStrategyPlayerContextPrompt();
                if (!string.IsNullOrWhiteSpace(strategyContext))
                {
                    chatMessages.Add(new ChatMessageData { role = "system", content = strategyContext });
                }
            }

            int historyCount = Math.Max(0, session.messages.Count - 1);
            List<DialogueMessageData> history = session.messages
                .Take(historyCount)
                .ToList();
            List<ChatMessageData> compressedHistory = DialogueContextCompressionService.BuildFromDialogueMessages(history);
            chatMessages.AddRange(compressedHistory);

            chatMessages.Add(new ChatMessageData { role = "user", content = playerMessage });

            Log.Message(
                $"[RimChat] Built chat messages: packed={chatMessages.Count}, raw_history={historyCount}, " +
                $"last={playerMessage.Substring(0, Math.Min(50, playerMessage.Length))}...");
            return chatMessages;
        }

        private string BuildSystemPrompt()
        {
            PromptPersistenceService.Instance.Initialize();
            var settings = RimChatMod.Settings;
            var tags = ParseSceneTagsCsv(settings?.DiplomacyManualSceneTagsCsv);
            return PromptPersistenceService.Instance.BuildFullSystemPrompt(
                faction,
                PromptPersistenceService.Instance.LoadConfig(),
                false,
                tags);
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


        private void AddAIResponseToSession(string response, FactionDialogueSession currentSession, Faction currentFaction, string playerMessage = null)
        {
            // 解析 AI response
            var parsedResponse = AIResponseParser.ParseResponse(response, currentFaction);

            // Getdialoguetext
            string dialogueText = parsedResponse.DialogueText;

            // 如果没有dialoguetext但有 action, 生成默认回复
            if (string.IsNullOrWhiteSpace(dialogueText) && parsedResponse.Actions.Count > 0)
            {
                dialogueText = GenerateResponseFromActions(parsedResponse.Actions);
            }

            if (string.IsNullOrWhiteSpace(dialogueText))
            {
                dialogueText = "RimChat_AIResponseDefault".Translate();
            }

            // 添加dialoguemessage
            string senderName = GetSenderName(currentFaction);
            currentSession.AddMessage(senderName, dialogueText, false);

            // 移除不必要的system音效播放以减少打断感 (现由打字音效替代)


            // 执行 AI 动作
            bool hasPresenceAction = parsedResponse.Actions.Any(a => IsPresenceActionType(a?.ActionType));
            if (parsedResponse.Actions.Count > 0)
            {
                ExecuteAIActions(parsedResponse.Actions, currentSession, currentFaction);
            }

            if (!hasPresenceAction)
            {
                TryAutoApplyPresenceFallback(dialogueText, currentSession, currentFaction);
            }

            TryGenerateDialogueKeywordSocialPost(playerMessage, dialogueText, parsedResponse.Actions, currentFaction, currentSession);
            ApplyStrategySuggestions(currentSession, parsedResponse.StrategySuggestions);

            // Dialogue结束后savememory
            SaveFactionMemory(currentSession, currentFaction);
        }

        private void AddFallbackResponse(string playerMessage)
        {
            AddFallbackResponseToSession(playerMessage, session, faction);
        }

        private void AddFallbackResponseToSession(string playerMessage, FactionDialogueSession currentSession, Faction currentFaction)
        {
            string senderName = GetSenderName(currentFaction);
            string response = GenerateSimulatedResponse(playerMessage, currentFaction);
            currentSession.AddMessage(senderName, response, false);
            
            // 移除global音效播放


            // Savememory
            SaveFactionMemory(currentSession, currentFaction);
        }

        /// <summary>/// 更新逐字output效果
 ///</summary>
        private void UpdateTypewriterEffect()
        {
            if (session == null || session.messages == null) return;

            float deltaTime = Time.realtimeSinceStartup - lastTypewriterUpdate;
            lastTypewriterUpdate = Time.realtimeSinceStartup;

            foreach (var msg in session.messages)
            {
                if (msg.isPlayer || msg.IsSystemMessage()) continue;

                if (!typewriterStates.TryGetValue(msg, out TypewriterState state))
                {
                    state = new TypewriterState
                    {
                        FullText = msg.message,
                        VisibleCharCount = 0,
                        AccumulatedTime = 0f,
                        IsComplete = false
                    };
                    typewriterStates[msg] = state;
                }

                if (!state.IsComplete)
                {
                    state.AccumulatedTime += deltaTime;
                    // 每秒 30 个字符
                    int targetCount = Mathf.FloorToInt(state.AccumulatedTime * 30f);
                    if (targetCount > state.VisibleCharCount)
                    {
                        // 播放打字音效
                        if (targetCount % 3 == 0)
                        {
                            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        }
                        
                        state.VisibleCharCount = Math.Min(targetCount, state.FullText.Length);
                        state.DisplayText = state.FullText.Substring(0, state.VisibleCharCount);
                        
                        if (state.VisibleCharCount >= state.FullText.Length)
                        {
                            state.IsComplete = true;
                        }
                    }
                }
            }
        }

        private string GetDisplayText(DialogueMessageData msg)
        {
            if (msg.isPlayer || msg.IsSystemMessage()) return msg.message;

            if (typewriterStates.TryGetValue(msg, out TypewriterState state))
            {
                return state.DisplayText;
            }
            return msg.message;
        }

        private string GetSenderName(Faction f)
        {
            if (f.leader != null && f.leader.Name != null)
            {
                return f.leader.Name.ToString();
            }
            return f.Name ?? "Unknown";
        }

        private string GenerateSimulatedResponse(string playerMessage, Faction f)
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
                if (f.PlayerGoodwill >= 80)
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

        /// <summary>/// 根据动作生成responsetext
 ///</summary>
        private string GenerateResponseFromActions(List<AIAction> actions)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var action in actions)
            {
                switch (action.ActionType)
                {
                    case AIActionNames.AdjustGoodwill:
                        if (action.Parameters.TryGetValue("amount", out object amount) && amount is int amt)
                        {
                            sb.AppendLine(amt > 0
                                ? "I appreciate your words. Our relations have improved."
                                : "Your words concern me. Our relations have suffered.");
                        }
                        break;
                    case AIActionNames.SendGift:
                        sb.AppendLine("I accept your gift. Let this strengthen our bond.");
                        break;
                    case AIActionNames.RequestAid:
                        sb.AppendLine("As allies, we shall assist you.");
                        break;
                    case AIActionNames.DeclareWar:
                        sb.AppendLine("You leave me no choice. Prepare for conflict!");
                        break;
                    case AIActionNames.MakePeace:
                        sb.AppendLine("Let us end this conflict. Peace is preferable.");
                        break;
                    case AIActionNames.RequestCaravan:
                        sb.AppendLine("Our traders will visit you soon.");
                        break;
                    case AIActionNames.RejectRequest:
                        string reason = action.Parameters.TryGetValue("reason", out object r)
                            ? r?.ToString()
                            : "I cannot fulfill this request.";
                        sb.AppendLine(reason);
                        break;
                }
            }
            return sb.ToString().Trim();
        }

        /// <summary>/// 执行 AI 动作
 ///</summary>
        private void ExecuteAIActions(List<AIAction> actions, FactionDialogueSession currentSession, Faction currentFaction)
        {
            var executor = new AIActionExecutor(currentFaction, applyDialogueApiGoodwillCost: true);

            foreach (var action in actions)
            {
                if (TryHandlePresenceAction(action, currentSession, currentFaction))
                {
                    continue;
                }

                if (TryHandleSocialCircleAction(action, currentSession, currentFaction))
                {
                    continue;
                }

                Log.Message($"[RimChat] Executing AI action: {action.ActionType}");
                var result = executor.ExecuteAction(action);

                if (result.IsSuccess)
                {
                    Log.Message($"[RimChat] Action executed successfully: {result.Message}");
                    
                    // Record重要event到memory
                    RecordSignificantEventForAction(action, currentFaction, result);
                }
                else
                {
                    Log.Warning($"[RimChat] Action failed: {result.Message}");
                    // 如果动作执行失败, 添加一条systemmessage
                    currentSession.AddMessage("System", $"无法执行动作 '{action.ActionType}': {result.Message}", false, DialogueMessageType.System);
                }
            }
        }

        /// <summary>/// 为执行的 AI 动作record重要event (只更新内存)
 ///</summary>
        private void RecordSignificantEventForAction(AIAction action, Faction currentFaction, ActionResult result)
        {
            SignificantEventType? eventType = action.ActionType switch
            {
                AIActionNames.AdjustGoodwill => SignificantEventType.GoodwillChanged,
                AIActionNames.SendGift => SignificantEventType.GiftSent,
                AIActionNames.RequestAid => SignificantEventType.AidRequested,
                AIActionNames.DeclareWar => SignificantEventType.WarDeclared,
                AIActionNames.MakePeace => SignificantEventType.PeaceMade,
                AIActionNames.RequestCaravan => SignificantEventType.TradeCaravan,
                AIActionNames.CreateQuest => SignificantEventType.QuestIssued,
                AIActionNames.RejectRequest => null,
                _ => null
            };

            if (eventType.HasValue)
            {
                string description = BuildSignificantEventDescription(action, result);
                // 只更新内存, 不save到file
                LeaderMemoryManager.Instance.RecordSignificantEvent(currentFaction, eventType.Value, Faction.OfPlayer, description);
            }
        }

        private static string BuildSignificantEventDescription(AIAction action, ActionResult result)
        {
            var details = result?.Data as ActionExecutionDetails;
            string fixedCost = BuildFixedCostText(details?.DialogueCost);

            return action.ActionType switch
            {
                AIActionNames.AdjustGoodwill => $"Dialogue context changed goodwill by {ReadInt(action, "amount", 0)}. Reason: {ReadText(action, "reason", action?.Reason, "Diplomatic dialogue")}.",
                AIActionNames.SendGift => $"Sent a gift of {ReadInt(action, "silver", 500)} silver with requested goodwill gain {ReadInt(action, "goodwill_gain", 5)}.",
                AIActionNames.RequestAid => $"Requested {ReadText(action, "type", null, "Military")} aid through dialogue.{fixedCost}",
                AIActionNames.RequestCaravan => $"Requested a {ReadText(action, "type", ReadText(action, "goods", null, null), "General")} caravan through dialogue.{fixedCost}",
                AIActionNames.CreateQuest => $"Issued quest template {ReadText(action, "questDefName", null, "UnknownQuest")} through dialogue.{fixedCost}",
                AIActionNames.DeclareWar => $"Declared war through dialogue. Reason: {ReadText(action, "reason", action?.Reason, "Diplomatic conflict")}.",
                AIActionNames.MakePeace => $"Proposed peace through dialogue. Cost: {ReadInt(action, "cost", 0)} silver.",
                _ => $"Executed {action?.ActionType ?? "unknown"}."
            };
        }

        private static string BuildFixedCostText(GameAIInterface.DialogueApiGoodwillCostResult cost)
        {
            if (cost == null)
            {
                return string.Empty;
            }

            return $" Fixed goodwill cost applied: base {cost.BaseCost}, actual {cost.ActualChange}.";
        }

        private static int ReadInt(AIAction action, string key, int fallback)
        {
            if (action?.Parameters != null && action.Parameters.TryGetValue(key, out object value) && value != null)
            {
                if (value is int intValue)
                {
                    return intValue;
                }

                if (int.TryParse(value.ToString(), out int parsed))
                {
                    return parsed;
                }
            }

            return fallback;
        }

        private static string ReadText(AIAction action, string key, string fallbackA, string fallbackB)
        {
            if (action?.Parameters != null && action.Parameters.TryGetValue(key, out object value) && value != null)
            {
                string text = value.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            if (!string.IsNullOrWhiteSpace(fallbackA))
            {
                return fallbackA;
            }

            return fallbackB ?? string.Empty;
        }

        private void SaveFactionMemory(FactionDialogueSession currentSession, Faction currentFaction)
        {
            if (currentSession == null || currentSession.messages == null) return;

            // 只更新内存中的memory, 不save到file
            // Filesave由存档save时统一processing
            LeaderMemoryManager.Instance.UpdateFromDialogue(currentFaction, currentSession.messages);
        }
    }
}


