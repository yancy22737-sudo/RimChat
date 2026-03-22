using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using RimChat.Dialogue;
using RimChat.DiplomacySystem;
using RimChat.Relation;
using RimChat.Config;

namespace RimChat.UI
{
    public partial class MainTabWindow_RimChat : MainTabWindow
    {
        public override Vector2 InitialSize => new Vector2(1000f, 750f);

        private Vector2 factionListScrollPosition = Vector2.zero;
        private Vector2 detailScrollPosition = Vector2.zero;
        private Faction selectedFaction;
        private List<Faction> allFactions = new List<Faction>();

        // 颜色主题
        private static readonly Color BackgroundColor = new Color(0.08f, 0.08f, 0.10f);
        private static readonly Color PanelColor = new Color(0.12f, 0.12f, 0.15f);
        private static readonly Color HeaderColor = new Color(0.15f, 0.15f, 0.18f);
        private static readonly Color AccentColor = new Color(0.25f, 0.55f, 0.95f);
        private static readonly Color TextPrimary = new Color(0.95f, 0.95f, 0.97f);
        private static readonly Color TextSecondary = new Color(0.65f, 0.65f, 0.70f);
        private static readonly Color BorderColor = new Color(0.20f, 0.20f, 0.25f);

        // Faction位置映射 (used for动画定位)
        private readonly Dictionary<Faction, Rect> factionRowRects = new Dictionary<Faction, Rect>();
        private bool goodwillEventSubscribed;

        public MainTabWindow_RimChat()
        {
            closeOnClickedOutside = false;
            EnsureGoodwillEventSubscription();
        }

        public override void PreClose()
        {
            base.PreClose();
            ClearGoodwillEventSubscription();
        }

        /// <summary>/// goodwill变化eventprocessing
 ///</summary>
        private void OnGoodwillChanged(Faction faction, int changeAmount)
        {
            if (faction == null) return;

            // Lookupfaction在列表中的位置
            if (factionRowRects.TryGetValue(faction, out Rect rowRect))
            {
                // 计算动画起始位置 (在goodwill数values附近)
                Vector2 startPos = new Vector2(
                    rowRect.x + 82f,
                    rowRect.y + 36f
                );

                // 创建动画
                GoodwillChangeAnimator.CreateAnimation(faction, changeAmount, startPos);
            }
        }

        public override void PreOpen()
        {
            base.PreOpen();
            EnsureGoodwillEventSubscription();
            RefreshFactionList();
        }

        private void EnsureGoodwillEventSubscription()
        {
            if (goodwillEventSubscribed)
            {
                return;
            }

            GoodwillChangeAnimator.OnGoodwillChanged -= OnGoodwillChanged;
            GoodwillChangeAnimator.OnGoodwillChanged += OnGoodwillChanged;
            goodwillEventSubscribed = true;
        }

        private void ClearGoodwillEventSubscription()
        {
            if (!goodwillEventSubscribed)
            {
                return;
            }

            GoodwillChangeAnimator.OnGoodwillChanged -= OnGoodwillChanged;
            goodwillEventSubscribed = false;
        }

        private void RefreshFactionList()
        {
            allFactions.Clear();
            
            if (Find.FactionManager?.AllFactions != null)
            {
                foreach (var faction in Find.FactionManager.AllFactions)
                {
                    if (faction != null && !faction.IsPlayer && !faction.defeated && !faction.Hidden)
                    {
                        allFactions.Add(faction);
                    }
                }
            }

            // 按goodwill排序
            allFactions = allFactions.OrderByDescending(f => f.PlayerGoodwill).ToList();

            if (selectedFaction == null || !allFactions.Contains(selectedFaction))
            {
                selectedFaction = allFactions.FirstOrDefault();
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            // 绘制背景
            Widgets.DrawBoxSolid(inRect, BackgroundColor);

            // 标题栏
            DrawHeader(new Rect(inRect.x, inRect.y, inRect.width, 74f));

            float contentY = inRect.y + 79f;
            float contentHeight = inRect.height - 84f;

            // 左侧faction列表
            float listWidth = 280f;
            Rect listRect = new Rect(inRect.x + 5f, contentY, listWidth, contentHeight);
            DrawFactionList(listRect);

            // 右侧详情区域
            Rect detailRect = new Rect(inRect.x + listWidth + 15f, contentY,
                inRect.width - listWidth - 25f, contentHeight);
            DrawFactionDetail(detailRect);

            // 绘制goodwill变化动画 (在所有UI之上)
            GoodwillChangeAnimator.UpdateAndDrawAnimations();
        }

        private void DrawHeader(Rect rect)
        {
            // 标题背景
            Widgets.DrawBoxSolid(rect, HeaderColor);
            
            // 标题文字
            Text.Font = GameFont.Medium;
            GUI.color = TextPrimary;
            string title = "RimChat_WindowTitle".Translate();
            Widgets.Label(new Rect(rect.x + 15f, rect.y + 12f, rect.width - 200f, 30f), title);
            
            // 副标题
            Text.Font = GameFont.Tiny;
            GUI.color = TextSecondary;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x + 15f, rect.y + 30f, rect.width - 200f, 20f),
                "RimChat_FactionsAvailable".Translate(allFactions.Count));
            Text.Anchor = TextAnchor.UpperLeft;

            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            // 刷新button
            Rect refreshRect = new Rect(rect.xMax - 100f, rect.y + 10f, 85f, 30f);
            DrawModernButton(refreshRect, "RimChat_Refresh".Translate(), () => RefreshFactionList());
        }

        private void DrawFactionList(Rect rect)
        {
            // 清空位置映射 (将在绘制时重新填充)
            factionRowRects.Clear();

            // 面板背景
            Widgets.DrawBoxSolid(rect, PanelColor);
            GUI.color = BorderColor;
            Widgets.DrawBox(rect);
            GUI.color = Color.white;

            // 列表面板标题
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, 35f);
            Widgets.DrawBoxSolid(headerRect, new Color(0.14f, 0.14f, 0.17f));
            
            Text.Font = GameFont.Small;
            GUI.color = TextSecondary;
            Widgets.Label(new Rect(headerRect.x + 12f, headerRect.y + 8f, headerRect.width - 20f, 20f),
                "RimChat_FactionsHeader".Translate());
            GUI.color = Color.white;

            Rect innerRect = new Rect(rect.x, rect.y + 35f, rect.width, rect.height - 35f);

            // 计算contents高度
            float rowHeight = 75f;
            float contentHeight = allFactions.Count * (rowHeight + 4f);
            
            Rect viewRect = new Rect(0f, 0f, innerRect.width - 16f, Mathf.Max(contentHeight, innerRect.height));
            
            factionListScrollPosition = GUI.BeginScrollView(innerRect, factionListScrollPosition, viewRect);

            float curY = 5f;
            foreach (var faction in allFactions)
            {
                Rect rowRect = new Rect(8f, curY, viewRect.width - 16f, rowHeight);
                DrawModernFactionListItem(faction, rowRect);

                // Recordfaction位置 (转换为屏幕坐标used for动画)
                Rect screenRect = new Rect(
                    rect.x + rowRect.x,
                    rect.y + 35f + rowRect.y - factionListScrollPosition.y,
                    rowRect.width,
                    rowRect.height
                );
                factionRowRects[faction] = screenRect;

                curY += rowHeight + 4f;
            }

            GUI.EndScrollView();

            // 检查goodwill变化
            GoodwillChangeAnimator.CheckGoodwillChanges(allFactions);
        }

        private void DrawModernFactionListItem(Faction faction, Rect rect)
        {
            bool isSelected = faction == selectedFaction;
            bool hasDialogue = GameComponent_DiplomacyManager.Instance?.GetSession(faction)?.messages.Count > 0;
            bool hasUnread = GameComponent_DiplomacyManager.Instance?.HasUnreadMessages(faction) ?? false;
            
            // 背景
            if (isSelected)
            {
                Widgets.DrawBoxSolid(rect, new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.25f));
                GUI.color = AccentColor;
                Widgets.DrawBox(rect);
                GUI.color = Color.white;
            }
            else
            {
                Widgets.DrawBoxSolid(rect, new Color(0.10f, 0.10f, 0.13f));
                if (Mouse.IsOver(rect))
                {
                    Widgets.DrawBoxSolid(rect, new Color(0.14f, 0.14f, 0.18f));
                }
            }

            float x = rect.x + 12f;
            float y = rect.y + 10f;

            // Faction图标背景
            Rect iconBgRect = new Rect(x, y, 55f, 55f);
            Widgets.DrawBoxSolid(iconBgRect, new Color(0.08f, 0.08f, 0.10f));
            GUI.color = BorderColor;
            Widgets.DrawBox(iconBgRect);
            GUI.color = Color.white;
            
            // Faction图标
            Rect iconRect = new Rect(x + 2f, y + 2f, 51f, 51f);
            if (faction.def != null)
            {
                Texture2D factionIcon = faction.def.FactionIcon;
                if (factionIcon != null && factionIcon != BaseContent.BadTex)
                {
                    GUI.DrawTexture(iconRect, factionIcon);
                }
                else
                {
                    DrawDefaultFactionIcon(iconRect, faction);
                }
            }
            x += 70f;

            // AI控制标记 (右上角)
            bool isAIControlled = GameComponent_DiplomacyManager.Instance?.IsAIControlled(faction) ?? false;
            if (isAIControlled)
            {
                Rect aiBadgeRect = new Rect(rect.xMax - 40f, rect.y + 7f, 32f, 20f);
                Widgets.DrawBoxSolid(aiBadgeRect, new Color(0.2f, 0.6f, 0.9f, 0.8f));
                Text.Font = GameFont.Tiny;
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(aiBadgeRect, "RimChat_AIBadge".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
            }

            // Factionname
            Text.Font = GameFont.Small;
            GUI.color = isSelected ? Color.white : TextPrimary;
            Rect nameRect = new Rect(x, y, rect.width - x + rect.x - 45f, 22f);
            Widgets.Label(nameRect, faction.Name ?? "RimChat_Unknown".Translate());

            // 未读message指示
            if (hasUnread && !isSelected)
            {
                Rect unreadRect = new Rect(rect.xMax - 12f, rect.y + 28f, 8f, 8f);
                Widgets.DrawBoxSolid(unreadRect, new Color(0.3f, 0.8f, 1f));
            }

            y += 26f;

            // Goodwill条
            int goodwill = faction.PlayerGoodwill;
            Color goodwillColor = GetGoodwillColor(goodwill);
            
            // Goodwill数values
            GUI.color = goodwillColor;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(x, y, 45f, 20f), goodwill.ToString());

            // Goodwillprogress条背景
            Rect barBgRect = new Rect(x + 50f, y + 4f, 100f, 12f);
            Widgets.DrawBoxSolid(barBgRect, new Color(0.08f, 0.08f, 0.10f));
            
            // Goodwillprogress条
            float goodwillPercent = Mathf.InverseLerp(-100f, 100f, goodwill);
            Rect barFillRect = new Rect(barBgRect.x, barBgRect.y, barBgRect.width * goodwillPercent, barBgRect.height);
            Widgets.DrawBoxSolid(barFillRect, goodwillColor);

            // Relationlabel
            string relationLabel = GetRelationLabelShort(goodwill);
            GUI.color = goodwillColor * 0.9f;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(x + 155f, y - 1f, 76f, 22f), relationLabel.Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            // 点击打开dialogueinterface
            if (Widgets.ButtonInvisible(rect))
            {
                selectedFaction = faction;
                // 标记为已读
                var session = GameComponent_DiplomacyManager.Instance?.GetSession(faction);
                session?.MarkAsRead();
                // 直接打开dialogueinterface
                OpenDialogueWindow();
            }
        }

        private void DrawFactionDetail(Rect rect)
        {
            // 面板背景
            Widgets.DrawBoxSolid(rect, PanelColor);
            GUI.color = BorderColor;
            Widgets.DrawBox(rect);
            GUI.color = Color.white;

            if (selectedFaction == null)
            {
                DrawEmptyState(rect);
                return;
            }

            Rect innerRect = rect.ContractedBy(15f);

            // 计算contents高度
            float contentHeight = 800f;
            Rect viewRect = new Rect(0f, 0f, innerRect.width - 16f, contentHeight);
            
            detailScrollPosition = GUI.BeginScrollView(innerRect, detailScrollPosition, viewRect);

            float curY = 0f;
            float width = viewRect.width;

            // Faction标题卡片
            Rect headerRect = new Rect(0f, curY, width, 100f);
            DrawModernFactionHeader(selectedFaction, headerRect);
            curY += 115f;

            // Relationstate卡片
            Rect relationRect = new Rect(0f, curY, width, 80f);
            DrawRelationCard(selectedFaction, relationRect);
            curY += 95f;

            // 信息网格
            Rect infoRect = new Rect(0f, curY, width, 200f);
            DrawInfoGrid(selectedFaction, infoRect);
            curY += 215f;

            // 操作button区
            Rect actionRect = new Rect(0f, curY, width, 60f);
            DrawModernActionButtons(actionRect);
            curY += 75f;

            // AIstate区
            Rect aiRect = new Rect(0f, curY, width, 70f);
            DrawModernAIStatus(selectedFaction, aiRect);

            GUI.EndScrollView();
        }

        private void DrawEmptyState(Rect rect)
        {
            GUI.color = new Color(0.3f, 0.3f, 0.35f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Medium;
            Widgets.Label(rect, "RimChat_SelectFactionPrompt".Translate());
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawModernFactionHeader(Faction faction, Rect rect)
        {
            // 卡片背景
            Widgets.DrawBoxSolid(rect, new Color(0.10f, 0.10f, 0.13f));
            GUI.color = BorderColor;
            Widgets.DrawBox(rect);
            GUI.color = Color.white;

            float x = rect.x + 15f;
            float y = rect.y + 15f;

            // 大图标
            Rect iconRect = new Rect(x, y, 70f, 70f);
            Widgets.DrawBoxSolid(iconRect, new Color(0.08f, 0.08f, 0.10f));
            GUI.color = BorderColor;
            Widgets.DrawBox(iconRect);
            GUI.color = Color.white;
            
            Rect iconInnerRect = new Rect(x + 3f, y + 3f, 64f, 64f);
            if (faction.def != null)
            {
                Texture2D factionIcon = faction.def.FactionIcon;
                if (factionIcon != null && factionIcon != BaseContent.BadTex)
                {
                    GUI.DrawTexture(iconInnerRect, factionIcon);
                }
                else
                {
                    DrawDefaultFactionIcon(iconInnerRect, faction);
                }
            }
            x += 90f;

            // Factionname
            Text.Font = GameFont.Medium;
            GUI.color = TextPrimary;
            Widgets.Label(new Rect(x, y, rect.width - x + rect.x - 20f, 30f),
                faction.Name ?? "RimChat_Unknown".Translate());

            // Ilabel信息卡button
            Rect infoButtonRect = new Rect(rect.xMax - 40f, y, 28f, 28f);
            DrawInfoButton(infoButtonRect, () => OpenFactionDefInfoCard(faction));

            // Faction类型label
            y += 32f;
            Rect typeBadgeRect = new Rect(x, y, 120f, 22f);
            Widgets.DrawBoxSolid(typeBadgeRect, new Color(0.20f, 0.20f, 0.25f));
            Text.Font = GameFont.Tiny;
            GUI.color = TextSecondary;
            Widgets.Label(typeBadgeRect, faction.def?.label?.CapitalizeFirst() ?? "RimChat_Unknown".Translate());

            // AI控制label
            bool isAIControlled = GameComponent_DiplomacyManager.Instance?.IsAIControlled(faction) ?? false;
            if (isAIControlled)
            {
                Rect aiBadgeRect = new Rect(x + 130f, y, 60f, 22f);
                Widgets.DrawBoxSolid(aiBadgeRect, new Color(0.2f, 0.6f, 0.9f, 0.6f));
                GUI.color = Color.white;
                Widgets.Label(aiBadgeRect, "RimChat_AIControl".Translate());
            }

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        private void DrawRelationCard(Faction faction, Rect rect)
        {
            // 卡片背景
            int goodwill = faction.PlayerGoodwill;
            Color relationColor = GetGoodwillColor(goodwill);
            
            Widgets.DrawBoxSolid(rect, new Color(0.10f, 0.10f, 0.13f));
            GUI.color = new Color(relationColor.r, relationColor.g, relationColor.b, 0.5f);
            Widgets.DrawBox(rect);
            GUI.color = Color.white;

            float x = rect.x + 15f;
            float y = rect.y + 15f;

            // Relationlabel
            string relationLabel = GetRelationLabel(goodwill);
            Text.Font = GameFont.Medium;
            GUI.color = relationColor;
            Widgets.Label(new Rect(x, y, 200f, 28f), relationLabel);

            // Goodwill大数字
            Text.Font = GameFont.Medium;
            GUI.color = relationColor;
            string goodwillText = $"{goodwill}";
            float goodwillWidth = Text.CalcSize(goodwillText).x;
            Widgets.Label(new Rect(rect.xMax - goodwillWidth - 20f, y, goodwillWidth + 10f, 28f), goodwillText);

            y += 35f;

            // Goodwill条
            Rect barBgRect = new Rect(x, y, rect.width - 30f, 10f);
            Widgets.DrawBoxSolid(barBgRect, new Color(0.08f, 0.08f, 0.10f));
            
            float goodwillPercent = Mathf.InverseLerp(-100f, 100f, goodwill);
            Rect barFillRect = new Rect(barBgRect.x, barBgRect.y, barBgRect.width * goodwillPercent, barBgRect.height);
            Widgets.DrawBoxSolid(barFillRect, relationColor);

            // 刻度标记
            GUI.color = new Color(0.3f, 0.3f, 0.35f);
            for (int i = -100; i <= 100; i += 50)
            {
                float markX = barBgRect.x + barBgRect.width * Mathf.InverseLerp(-100f, 100f, i);
                Widgets.DrawBoxSolid(new Rect(markX, barBgRect.y - 3f, 1f, 16f), new Color(0.3f, 0.3f, 0.35f));
            }

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        private void DrawInfoGrid(Faction faction, Rect rect)
        {
            float cardWidth = (rect.width - 15f) / 2f;
            float cardHeight = 90f;

            // Leader信息卡片
            Rect leaderRect = new Rect(rect.x, rect.y, cardWidth, cardHeight);
            string leaderName = faction.leader?.Name?.ToStringFull ?? "RimChat_None".Translate();
            string leaderTraits = faction.leader?.story?.traits?.allTraits?.Count > 0
                ? string.Join(", ", faction.leader.story.traits.allTraits.Select(t => t.Label))
                : "RimChat_NoTraits".Translate();
            DrawInfoCard(leaderRect, "RimChat_LeaderCard".Translate(), leaderName, leaderTraits);

            // 科技等级卡片
            Rect techRect = new Rect(rect.x + cardWidth + 15f, rect.y, cardWidth, cardHeight);
            DrawInfoCard(techRect, "RimChat_TechLevelCard".Translate(),
                faction.def?.techLevel.ToString() ?? "RimChat_Unknown".Translate(),
                "RimChat_TechLevelDesc".Translate());

            // 据点数量卡片
            int settlementCount = 0;
            if (Find.WorldObjects?.SettlementBases != null)
            {
                settlementCount = Find.WorldObjects.SettlementBases.Count(s => s.Faction == faction);
            }
            Rect settlementRect = new Rect(rect.x, rect.y + cardHeight + 10f, cardWidth, cardHeight);
            DrawInfoCard(settlementRect, "RimChat_SettlementsCard".Translate(), settlementCount.ToString(),
                "RimChat_SettlementsDesc".Translate());

            // 意识形态卡片
            Rect ideoRect = new Rect(rect.x + cardWidth + 15f, rect.y + cardHeight + 10f, cardWidth, cardHeight);
            string ideoName = faction.ideos?.PrimaryIdeo?.name ?? "RimChat_None".Translate();
            DrawInfoCard(ideoRect, "RimChat_IdeologyCard".Translate(), ideoName,
                "RimChat_IdeologyDesc".Translate());
        }

        private void DrawInfoCard(Rect rect, string label, string value, string subtext)
        {
            // 卡片背景
            Widgets.DrawBoxSolid(rect, new Color(0.10f, 0.10f, 0.13f));
            GUI.color = BorderColor;
            Widgets.DrawBox(rect);
            GUI.color = Color.white;

            float x = rect.x + 12f;
            float y = rect.y + 10f;

            // Label
            Text.Font = GameFont.Tiny;
            GUI.color = TextSecondary;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(x, y - 1f, rect.width - 20f, 20f), label.ToUpper().Translate());

            // 数values
            y += 18f;
            Text.Font = GameFont.Small;
            GUI.color = TextPrimary;
            Widgets.Label(new Rect(x, y, rect.width - 20f, 22f), value);

            // 副text
            y += 24f;
            Text.Font = GameFont.Tiny;
            GUI.color = TextSecondary * 0.8f;
            Widgets.Label(new Rect(x, y - 1f, rect.width - 20f, 20f), subtext);
            Text.Anchor = TextAnchor.UpperLeft;

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        private void DrawModernActionButtons(Rect rect)
        {
            float buttonWidth = 140f;
            float x = rect.x;

            // Dialoguebutton
            Rect dialogueRect = new Rect(x, rect.y + 10f, buttonWidth, 40f);
            DrawModernButton(dialogueRect, "RimChat_DialogueButton".Translate(), () => OpenDialogueWindow(), AccentColor);
        }

        private void DrawModernAIStatus(Faction faction, Rect rect)
        {
            bool isAIControlled = GameComponent_DiplomacyManager.Instance?.IsAIControlled(faction) ?? false;
            
            // 背景
            Color statusColor = isAIControlled 
                ? new Color(0.2f, 0.6f, 0.9f, 0.15f)
                : new Color(0.3f, 0.3f, 0.35f, 0.15f);
            Widgets.DrawBoxSolid(rect, statusColor);
            GUI.color = isAIControlled 
                ? new Color(0.2f, 0.6f, 0.9f, 0.5f)
                : BorderColor;
            Widgets.DrawBox(rect);
            GUI.color = Color.white;

            float x = rect.x + 15f;
            float y = rect.y + 12f;

            // State图标
            string icon = isAIControlled ? "[AI]" : "[Std]";
            Text.Font = GameFont.Medium;
            GUI.color = isAIControlled ? new Color(0.4f, 0.8f, 1f) : TextSecondary;
            Widgets.Label(new Rect(x, y, 30f, 30f), icon);

            x += 40f;

            // State标题
            Text.Font = GameFont.Small;
            GUI.color = TextPrimary;
            string statusTitle = isAIControlled ? "RimChat_AIControlledStatus".Translate() : "RimChat_StandardBehaviorStatus".Translate();
            Widgets.Label(new Rect(x, y, rect.width - x + rect.x - 20f, 22f), statusTitle);

            // State描述
            y += 22f;
            Text.Font = GameFont.Tiny;
            GUI.color = TextSecondary;
            string statusDesc = isAIControlled
                ? "RimChat_AIControlledDesc".Translate()
                : "RimChat_StandardBehaviorDesc".Translate();
            Widgets.Label(new Rect(x, y, rect.width - x + rect.x - 20f, 20f), statusDesc);

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        private void DrawModernButton(Rect rect, string label, Action onClick, Color? color = null, bool enabled = true)
        {
            Color buttonColor = color ?? AccentColor;
            
            if (!enabled)
            {
                buttonColor = new Color(0.2f, 0.2f, 0.25f);
                GUI.color = new Color(0.5f, 0.5f, 0.55f);
            }

            // Button背景
            Widgets.DrawBoxSolid(rect, buttonColor * (Mouse.IsOver(rect) && enabled ? 1.2f : 1f));
            
            // Button文字
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = enabled ? Color.white : new Color(0.5f, 0.5f, 0.55f);
            Widgets.Label(rect, label);
            Text.Anchor = oldAnchor;
            GUI.color = Color.white;

            // 点击processing
            if (enabled && Widgets.ButtonInvisible(rect))
            {
                onClick?.Invoke();
            }
        }

        private void DrawInfoButton(Rect rect, Action onClick)
        {
            Color buttonColor = new Color(0.35f, 0.58f, 0.92f);
            bool isMouseOver = Mouse.IsOver(rect);
            
            if (isMouseOver)
            {
                Widgets.DrawBoxSolid(rect, buttonColor * 1.3f);
            }
            else
            {
                Widgets.DrawBoxSolid(rect, buttonColor);
            }
            
            GUI.color = BorderColor;
            Widgets.DrawBox(rect);
            GUI.color = Color.white;

            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.white;
            Text.Font = GameFont.Medium;
            Widgets.Label(rect, "i");
            Text.Anchor = oldAnchor;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            if (Widgets.ButtonInvisible(rect))
            {
                onClick?.Invoke();
            }
        }

        private void OpenFactionDefInfoCard(Faction faction)
        {
            if (faction?.def == null) return;

            try
            {
                Type dialogInfoCardType = typeof(Window).Assembly.GetType("RimWorld.Dialog_InfoCard");
                if (dialogInfoCardType != null)
                {
                    ConstructorInfo constructor = dialogInfoCardType.GetConstructor(new Type[] { typeof(object) });
                    if (constructor != null)
                    {
                        object dialog = constructor.Invoke(new object[] { faction.def });
                        Find.WindowStack.Add((Window)dialog);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to open faction info card: {ex.Message}");
            }

            ShowFallbackFactionInfo(faction);
        }

        private void ShowFallbackFactionInfo(Faction faction)
        {
            Log.Message($"[RimChat] Faction Info: {faction.def?.label ?? "Unknown"} - Tech: {faction.def?.techLevel}");
        }

        private void DrawDefaultFactionIcon(Rect rect, Faction faction)
        {
            Color factionColor = GetFactionColor(faction);
            Widgets.DrawBoxSolid(rect, factionColor * 0.3f);
            
            Text.Font = GameFont.Medium;
            GUI.color = factionColor;
            
            string initial = faction.Name?.Length > 0 ? faction.Name[0].ToString().ToUpper() : "?";
            
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, initial);
            Text.Anchor = oldAnchor;
            
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        private Color GetFactionColor(Faction faction)
        {
            if (faction?.def == null) return new Color(0.5f, 0.5f, 0.5f);
            
            if (faction.PlayerRelationKind == FactionRelationKind.Hostile)
                return new Color(0.95f, 0.35f, 0.35f);
            if (faction.PlayerRelationKind == FactionRelationKind.Neutral)
                return new Color(0.95f, 0.85f, 0.3f);
            if (faction.PlayerRelationKind == FactionRelationKind.Ally)
                return new Color(0.3f, 0.85f, 0.4f);
            
            return new Color(0.4f, 0.6f, 0.9f);
        }

        private string GetRelationLabel(int goodwill)
        {
            if (goodwill >= 80) return "RimChat_RelationAlly".Translate();
            if (goodwill >= 40) return "RimChat_RelationFriend".Translate();
            if (goodwill >= 0) return "RimChat_RelationNeutral".Translate();
            if (goodwill >= -40) return "RimChat_RelationHostile".Translate();
            return "RimChat_RelationEnemy".Translate();
        }

        private string GetRelationLabelShort(int goodwill)
        {
            if (goodwill >= 80) return "RimChat_RelationAllyShort";
            if (goodwill >= 40) return "RimChat_RelationFriendShort";
            if (goodwill >= 0) return "RimChat_RelationNeutralShort";
            if (goodwill >= -40) return "RimChat_RelationHostileShort";
            return "RimChat_RelationEnemyShort";
        }

        private Color GetGoodwillColor(int goodwill)
        {
            if (goodwill >= 80) return new Color(0.3f, 0.85f, 0.4f);   // 绿色
            if (goodwill >= 40) return new Color(0.7f, 0.9f, 0.3f);    // 黄绿
            if (goodwill >= 0) return new Color(0.95f, 0.85f, 0.3f);   // 黄色
            if (goodwill >= -40) return new Color(0.95f, 0.6f, 0.25f); // 橙色
            return new Color(0.95f, 0.35f, 0.35f);                      // 红色
        }

        private void OpenDialogueWindow()
        {
            if (selectedFaction != null)
            {
                Close();
                if (DialogueWindowCoordinator.TryOpen(
                    DialogueOpenIntent.CreateDiplomacy(selectedFaction, null, null, false),
                    out string reason))
                {
                    return;
                }

                Log.Warning($"[RimChat] MainTab dialogue open rejected: faction={selectedFaction.Name}, reason={reason ?? "unknown"}");
                Log.Warning($"[RimChat] Applying direct diplomacy open fallback: source=main_tab, faction={selectedFaction.Name}");
                Find.WindowStack?.Add(new Dialog_DiplomacyDialogue(selectedFaction, null));
            }
        }
    }
}


