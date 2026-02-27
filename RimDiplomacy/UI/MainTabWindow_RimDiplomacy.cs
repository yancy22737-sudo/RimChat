using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimDiplomacy.UI
{
    public class MainTabWindow_RimDiplomacy : MainTabWindow
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

        // 派系位置映射（用于动画定位）
        private readonly Dictionary<Faction, Rect> factionRowRects = new Dictionary<Faction, Rect>();

        public MainTabWindow_RimDiplomacy()
        {
            closeOnClickedOutside = false;
            // 订阅好感度变化事件
            GoodwillChangeAnimator.OnGoodwillChanged += OnGoodwillChanged;
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
        private void OnGoodwillChanged(Faction faction, int changeAmount)
        {
            if (faction == null) return;

            // 查找派系在列表中的位置
            if (factionRowRects.TryGetValue(faction, out Rect rowRect))
            {
                // 计算动画起始位置（在好感度数值附近）
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
            RefreshFactionList();
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

            // 按好感度排序
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
            DrawHeader(new Rect(inRect.x, inRect.y, inRect.width, 50f));

            float contentY = inRect.y + 55f;
            float contentHeight = inRect.height - 60f;

            // 左侧派系列表
            float listWidth = 280f;
            Rect listRect = new Rect(inRect.x + 5f, contentY, listWidth, contentHeight);
            DrawFactionList(listRect);

            // 右侧详情区域
            Rect detailRect = new Rect(inRect.x + listWidth + 15f, contentY, 
                inRect.width - listWidth - 25f, contentHeight);
            DrawFactionDetail(detailRect);

            // 绘制好感度变化动画（在所有UI之上）
            GoodwillChangeAnimator.UpdateAndDrawAnimations();
        }

        private void DrawHeader(Rect rect)
        {
            // 标题背景
            Widgets.DrawBoxSolid(rect, HeaderColor);
            
            // 标题文字
            Text.Font = GameFont.Medium;
            GUI.color = TextPrimary;
            string title = "RimDiplomacy_WindowTitle".Translate();
            Widgets.Label(new Rect(rect.x + 15f, rect.y + 12f, rect.width - 200f, 30f), title);
            
            // 副标题
            Text.Font = GameFont.Tiny;
            GUI.color = TextSecondary;
            Widgets.Label(new Rect(rect.x + 15f, rect.y + 32f, rect.width - 200f, 16f),
                "RimDiplomacy_FactionsAvailable".Translate(allFactions.Count));

            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            // 刷新按钮
            Rect refreshRect = new Rect(rect.xMax - 100f, rect.y + 10f, 85f, 30f);
            DrawModernButton(refreshRect, "RimDiplomacy_Refresh".Translate(), () => RefreshFactionList());
        }

        private void DrawFactionList(Rect rect)
        {
            // 清空位置映射（将在绘制时重新填充）
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
                "RimDiplomacy_FactionsHeader".Translate());
            GUI.color = Color.white;

            Rect innerRect = new Rect(rect.x, rect.y + 35f, rect.width, rect.height - 35f);

            // 计算内容高度
            float rowHeight = 75f;
            float contentHeight = allFactions.Count * (rowHeight + 4f);
            
            Rect viewRect = new Rect(0f, 0f, innerRect.width - 16f, Mathf.Max(contentHeight, innerRect.height));
            
            factionListScrollPosition = GUI.BeginScrollView(innerRect, factionListScrollPosition, viewRect);

            float curY = 5f;
            foreach (var faction in allFactions)
            {
                Rect rowRect = new Rect(8f, curY, viewRect.width - 16f, rowHeight);
                DrawModernFactionListItem(faction, rowRect);

                // 记录派系位置（转换为屏幕坐标用于动画）
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

            // 检查好感度变化
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

            // 派系图标背景
            Rect iconBgRect = new Rect(x, y, 55f, 55f);
            Widgets.DrawBoxSolid(iconBgRect, new Color(0.08f, 0.08f, 0.10f));
            GUI.color = BorderColor;
            Widgets.DrawBox(iconBgRect);
            GUI.color = Color.white;
            
            // 派系图标
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

            // AI控制标记（右上角）
            bool isAIControlled = GameComponent_DiplomacyManager.Instance?.IsAIControlled(faction) ?? false;
            if (isAIControlled)
            {
                Rect aiBadgeRect = new Rect(rect.xMax - 35f, rect.y + 8f, 28f, 18f);
                Widgets.DrawBoxSolid(aiBadgeRect, new Color(0.2f, 0.6f, 0.9f, 0.8f));
                Text.Font = GameFont.Tiny;
                GUI.color = Color.white;
                Widgets.Label(aiBadgeRect, "RimDiplomacy_AIBadge".Translate());
                Text.Font = GameFont.Small;
            }

            // 派系名称
            Text.Font = GameFont.Small;
            GUI.color = isSelected ? Color.white : TextPrimary;
            Rect nameRect = new Rect(x, y, rect.width - x + rect.x - 45f, 22f);
            Widgets.Label(nameRect, faction.Name ?? "RimDiplomacy_Unknown".Translate());

            // 未读消息指示
            if (hasUnread && !isSelected)
            {
                Rect unreadRect = new Rect(rect.xMax - 12f, rect.y + 28f, 8f, 8f);
                Widgets.DrawBoxSolid(unreadRect, new Color(0.3f, 0.8f, 1f));
            }

            y += 26f;

            // 好感度条
            int goodwill = faction.PlayerGoodwill;
            Color goodwillColor = GetGoodwillColor(goodwill);
            
            // 好感度数值
            GUI.color = goodwillColor;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(x, y, 45f, 20f), goodwill.ToString());

            // 好感度进度条背景
            Rect barBgRect = new Rect(x + 50f, y + 4f, 100f, 12f);
            Widgets.DrawBoxSolid(barBgRect, new Color(0.08f, 0.08f, 0.10f));
            
            // 好感度进度条
            float goodwillPercent = Mathf.InverseLerp(-100f, 100f, goodwill);
            Rect barFillRect = new Rect(barBgRect.x, barBgRect.y, barBgRect.width * goodwillPercent, barBgRect.height);
            Widgets.DrawBoxSolid(barFillRect, goodwillColor);

            // 关系标签
            string relationLabel = GetRelationLabelShort(goodwill);
            GUI.color = goodwillColor * 0.9f;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(x + 155f, y, 70f, 20f), relationLabel.Translate());
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            // 点击选择
            if (Widgets.ButtonInvisible(rect))
            {
                selectedFaction = faction;
                // 标记为已读
                var session = GameComponent_DiplomacyManager.Instance?.GetSession(faction);
                session?.MarkAsRead();
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

            // 计算内容高度
            float contentHeight = 800f;
            Rect viewRect = new Rect(0f, 0f, innerRect.width - 16f, contentHeight);
            
            detailScrollPosition = GUI.BeginScrollView(innerRect, detailScrollPosition, viewRect);

            float curY = 0f;
            float width = viewRect.width;

            // 派系标题卡片
            Rect headerRect = new Rect(0f, curY, width, 100f);
            DrawModernFactionHeader(selectedFaction, headerRect);
            curY += 115f;

            // 关系状态卡片
            Rect relationRect = new Rect(0f, curY, width, 80f);
            DrawRelationCard(selectedFaction, relationRect);
            curY += 95f;

            // 信息网格
            Rect infoRect = new Rect(0f, curY, width, 200f);
            DrawInfoGrid(selectedFaction, infoRect);
            curY += 215f;

            // 操作按钮区
            Rect actionRect = new Rect(0f, curY, width, 60f);
            DrawModernActionButtons(actionRect);
            curY += 75f;

            // AI状态区
            Rect aiRect = new Rect(0f, curY, width, 70f);
            DrawModernAIStatus(selectedFaction, aiRect);

            GUI.EndScrollView();
        }

        private void DrawEmptyState(Rect rect)
        {
            GUI.color = new Color(0.3f, 0.3f, 0.35f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Medium;
            Widgets.Label(rect, "RimDiplomacy_SelectFactionPrompt".Translate());
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

            // 派系名称
            Text.Font = GameFont.Medium;
            GUI.color = TextPrimary;
            Widgets.Label(new Rect(x, y, rect.width - x + rect.x - 20f, 30f),
                faction.Name ?? "RimDiplomacy_Unknown".Translate());

            // i标签信息卡按钮
            Rect infoButtonRect = new Rect(rect.xMax - 40f, y, 28f, 28f);
            DrawInfoButton(infoButtonRect, () => OpenFactionDefInfoCard(faction));

            // 派系类型标签
            y += 32f;
            Rect typeBadgeRect = new Rect(x, y, 120f, 22f);
            Widgets.DrawBoxSolid(typeBadgeRect, new Color(0.20f, 0.20f, 0.25f));
            Text.Font = GameFont.Tiny;
            GUI.color = TextSecondary;
            Widgets.Label(typeBadgeRect, faction.def?.label?.CapitalizeFirst() ?? "RimDiplomacy_Unknown".Translate());

            // AI控制标签
            bool isAIControlled = GameComponent_DiplomacyManager.Instance?.IsAIControlled(faction) ?? false;
            if (isAIControlled)
            {
                Rect aiBadgeRect = new Rect(x + 130f, y, 60f, 22f);
                Widgets.DrawBoxSolid(aiBadgeRect, new Color(0.2f, 0.6f, 0.9f, 0.6f));
                GUI.color = Color.white;
                Widgets.Label(aiBadgeRect, "RimDiplomacy_AIControl".Translate());
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

            // 关系标签
            string relationLabel = GetRelationLabel(goodwill);
            Text.Font = GameFont.Medium;
            GUI.color = relationColor;
            Widgets.Label(new Rect(x, y, 200f, 28f), relationLabel);

            // 好感度大数字
            Text.Font = GameFont.Medium;
            GUI.color = relationColor;
            string goodwillText = $"{goodwill}";
            float goodwillWidth = Text.CalcSize(goodwillText).x;
            Widgets.Label(new Rect(rect.xMax - goodwillWidth - 20f, y, goodwillWidth + 10f, 28f), goodwillText);

            y += 35f;

            // 好感度条
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

            // 领袖信息卡片
            Rect leaderRect = new Rect(rect.x, rect.y, cardWidth, cardHeight);
            string leaderName = faction.leader?.Name?.ToStringFull ?? "RimDiplomacy_None".Translate();
            string leaderTraits = faction.leader?.story?.traits?.allTraits?.Count > 0
                ? string.Join(", ", faction.leader.story.traits.allTraits.Select(t => t.Label))
                : "RimDiplomacy_NoTraits".Translate();
            DrawInfoCard(leaderRect, "RimDiplomacy_LeaderCard".Translate(), leaderName, leaderTraits);

            // 科技等级卡片
            Rect techRect = new Rect(rect.x + cardWidth + 15f, rect.y, cardWidth, cardHeight);
            DrawInfoCard(techRect, "RimDiplomacy_TechLevelCard".Translate(),
                faction.def?.techLevel.ToString() ?? "RimDiplomacy_Unknown".Translate(),
                "RimDiplomacy_TechLevelDesc".Translate());

            // 据点数量卡片
            int settlementCount = 0;
            if (Find.WorldObjects?.SettlementBases != null)
            {
                settlementCount = Find.WorldObjects.SettlementBases.Count(s => s.Faction == faction);
            }
            Rect settlementRect = new Rect(rect.x, rect.y + cardHeight + 10f, cardWidth, cardHeight);
            DrawInfoCard(settlementRect, "RimDiplomacy_SettlementsCard".Translate(), settlementCount.ToString(),
                "RimDiplomacy_SettlementsDesc".Translate());

            // 意识形态卡片
            Rect ideoRect = new Rect(rect.x + cardWidth + 15f, rect.y + cardHeight + 10f, cardWidth, cardHeight);
            string ideoName = faction.ideos?.PrimaryIdeo?.name ?? "RimDiplomacy_None".Translate();
            DrawInfoCard(ideoRect, "RimDiplomacy_IdeologyCard".Translate(), ideoName,
                "RimDiplomacy_IdeologyDesc".Translate());
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

            // 标签
            Text.Font = GameFont.Tiny;
            GUI.color = TextSecondary;
            Widgets.Label(new Rect(x, y, rect.width - 20f, 16f), label.ToUpper().Translate());

            // 数值
            y += 18f;
            Text.Font = GameFont.Small;
            GUI.color = TextPrimary;
            Widgets.Label(new Rect(x, y, rect.width - 20f, 22f), value);

            // 副文本
            y += 24f;
            Text.Font = GameFont.Tiny;
            GUI.color = TextSecondary * 0.8f;
            Widgets.Label(new Rect(x, y, rect.width - 20f, 16f), subtext);

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        private void DrawModernActionButtons(Rect rect)
        {
            float buttonWidth = 140f;
            float x = rect.x;

            // 对话按钮
            Rect dialogueRect = new Rect(x, rect.y + 10f, buttonWidth, 40f);
            DrawModernButton(dialogueRect, "RimDiplomacy_DialogueButton".Translate(), () => OpenDialogueWindow(), AccentColor);
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

            // 状态图标
            string icon = isAIControlled ? "[AI]" : "[Std]";
            Text.Font = GameFont.Medium;
            GUI.color = isAIControlled ? new Color(0.4f, 0.8f, 1f) : TextSecondary;
            Widgets.Label(new Rect(x, y, 30f, 30f), icon);

            x += 40f;

            // 状态标题
            Text.Font = GameFont.Small;
            GUI.color = TextPrimary;
            string statusTitle = isAIControlled ? "RimDiplomacy_AIControlledStatus".Translate() : "RimDiplomacy_StandardBehaviorStatus".Translate();
            Widgets.Label(new Rect(x, y, rect.width - x + rect.x - 20f, 22f), statusTitle);

            // 状态描述
            y += 22f;
            Text.Font = GameFont.Tiny;
            GUI.color = TextSecondary;
            string statusDesc = isAIControlled
                ? "RimDiplomacy_AIControlledDesc".Translate()
                : "RimDiplomacy_StandardBehaviorDesc".Translate();
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

            // 按钮背景
            Widgets.DrawBoxSolid(rect, buttonColor * (Mouse.IsOver(rect) && enabled ? 1.2f : 1f));
            
            // 按钮文字
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = enabled ? Color.white : new Color(0.5f, 0.5f, 0.55f);
            Widgets.Label(rect, label);
            Text.Anchor = oldAnchor;
            GUI.color = Color.white;

            // 点击处理
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
                Log.Warning($"[RimDiplomacy] Failed to open faction info card: {ex.Message}");
            }

            ShowFallbackFactionInfo(faction);
        }

        private void ShowFallbackFactionInfo(Faction faction)
        {
            Log.Message($"[RimDiplomacy] Faction Info: {faction.def?.label ?? "Unknown"} - Tech: {faction.def?.techLevel}");
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
            if (goodwill >= 80) return "RimDiplomacy_RelationAlly".Translate();
            if (goodwill >= 40) return "RimDiplomacy_RelationFriend".Translate();
            if (goodwill >= 0) return "RimDiplomacy_RelationNeutral".Translate();
            if (goodwill >= -40) return "RimDiplomacy_RelationHostile".Translate();
            return "RimDiplomacy_RelationEnemy".Translate();
        }

        private string GetRelationLabelShort(int goodwill)
        {
            if (goodwill >= 80) return "RimDiplomacy_RelationAllyShort";
            if (goodwill >= 40) return "RimDiplomacy_RelationFriendShort";
            if (goodwill >= 0) return "RimDiplomacy_RelationNeutralShort";
            if (goodwill >= -40) return "RimDiplomacy_RelationHostileShort";
            return "RimDiplomacy_RelationEnemyShort";
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
                Find.WindowStack.Add(new Dialog_DiplomacyDialogue(selectedFaction));
            }
        }
    }
}
