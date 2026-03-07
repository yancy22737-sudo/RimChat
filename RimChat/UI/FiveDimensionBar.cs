using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using RimChat.Relation;
using RimChat.DiplomacySystem;

namespace RimChat.UI
{
    /// <summary>
    /// 五维关系属性UI组件
    /// 显示派系对玩家的五个维度关系值：信任、亲密、互惠、尊重、影响
    /// </summary>
    public class FiveDimensionBar
    {
        // 维度定义
        private readonly DimensionInfo[] dimensions;

        // 动画相关
        private readonly Dictionary<RelationDimension, float> animatedValues = new();
        private readonly Dictionary<RelationDimension, float> targetValues = new();
        private readonly Dictionary<RelationDimension, float> changeAnimations = new();
        private const float ANIMATION_SPEED = 5f;
        private const float CHANGE_ANIMATION_DURATION = 2f;

        // 样式常量
        private const float BAR_HEIGHT = 8f;
        private const float LABEL_WIDTH = 50f;
        private const float VALUE_WIDTH = 40f;
        private const float SPACING = 6f;
        private const float ICON_SIZE = 16f;
        private const float HEADER_HEIGHT = 25f;
        private const float COLLAPSED_HEIGHT = 30f;

        // 颜色配置 - RimWorld原生风格
        private static readonly Color BackgroundColor = new Color(0.12f, 0.12f, 0.14f);
        private static readonly Color BorderColor = new Color(0.25f, 0.25f, 0.30f);
        private static readonly Color BarBackgroundColor = new Color(0.08f, 0.08f, 0.10f);
        private static readonly Color TextPrimary = new Color(0.90f, 0.90f, 0.92f);
        private static readonly Color TextSecondary = new Color(0.65f, 0.65f, 0.70f);
        private static readonly Color PositiveChangeColor = new Color(0.35f, 0.85f, 0.45f);
        private static readonly Color NegativeChangeColor = new Color(0.95f, 0.45f, 0.35f);

        // 维度颜色 - 每个维度有独特的颜色
        private static readonly Dictionary<RelationDimension, Color> DimensionColors = new()
        {
            { RelationDimension.Trust, new Color(0.35f, 0.75f, 0.95f) },      // 蓝色 - 信任
            { RelationDimension.Intimacy, new Color(0.95f, 0.55f, 0.75f) },   // 粉色 - 亲密
            { RelationDimension.Reciprocity, new Color(0.55f, 0.95f, 0.65f) }, // 绿色 - 互惠
            { RelationDimension.Respect, new Color(0.95f, 0.85f, 0.35f) },    // 金色 - 尊重
            { RelationDimension.Influence, new Color(0.85f, 0.55f, 0.95f) }   // 紫色 - 影响
        };

        // 维度图标字符
        private static readonly Dictionary<RelationDimension, string> DimensionIcons = new()
        {
            { RelationDimension.Trust, "◆" },
            { RelationDimension.Intimacy, "♥" },
            { RelationDimension.Reciprocity, "⇄" },
            { RelationDimension.Respect, "★" },
            { RelationDimension.Influence, "⚡" }
        };

        private Faction currentFaction;
        private FactionRelationValues currentValues;
        private bool isInitialized;

        // 折叠状态（默认折叠）
        private bool isCollapsed = true;
        private bool isOverlayExpanded = false;
        private Rect overlayRect = Rect.zero;
        private Rect anchorRect = Rect.zero;
        private float compactOverlayAnimProgress = 0f;

        private const float COMPACT_ANCHOR_HEIGHT = 28f;
        private const float COMPACT_BUTTON_SIZE = 22f;
        private const float COMPACT_OVERLAY_WIDTH = 360f;
        private const float COMPACT_OVERLAY_PADDING = 10f;
        private const float COMPACT_ROW_HEIGHT = 20f;
        private const float COMPACT_HEADER_HEIGHT = 24f;
        private const float COMPACT_OVERLAY_ANIM_SPEED = 7f;
        private const float COMPACT_OVERLAY_INTRO_OFFSET = 10f;
        
        public FiveDimensionBar()
        {
            dimensions = new[]
            {
                new DimensionInfo(RelationDimension.Trust, "RimChat_Trust"),
                new DimensionInfo(RelationDimension.Intimacy, "RimChat_Intimacy"),
                new DimensionInfo(RelationDimension.Reciprocity, "RimChat_Reciprocity"),
                new DimensionInfo(RelationDimension.Respect, "RimChat_Respect"),
                new DimensionInfo(RelationDimension.Influence, "RimChat_Influence")
            };
            
            foreach (var dim in dimensions)
            {
                animatedValues[dim.Dimension] = 0f;
                targetValues[dim.Dimension] = 0f;
                changeAnimations[dim.Dimension] = 0f;
            }
        }
        
        /// <summary>
        /// 更新当前显示的派系数据
        /// </summary>
        public void UpdateFaction(Faction faction)
        {
            if (faction == null) return;
            
            currentFaction = faction;
            currentValues = GameComponent_DiplomacyManager.Instance?.GetOrCreateRelationValues(faction);
            
            if (currentValues != null)
            {
                foreach (var dim in dimensions)
                {
                    float newValue = currentValues.GetValue(dim.Dimension);
                    float oldTarget = targetValues[dim.Dimension];
                    
                    // 检测值变化，触发变化动画
                    if (isInitialized && Mathf.Abs(newValue - oldTarget) > 0.1f)
                    {
                        float change = newValue - oldTarget;
                        changeAnimations[dim.Dimension] = change;
                    }
                    
                    targetValues[dim.Dimension] = newValue;
                }
                
                isInitialized = true;
            }
        }
        
        /// <summary>
        /// 绘制五维属性栏
        /// </summary>
        public void Draw(Rect rect)
        {
            if (currentValues == null) return;

            // 更新动画值
            UpdateAnimations();

            // 绘制背景面板
            Widgets.DrawBoxSolid(rect, BackgroundColor);
            GUI.color = BorderColor;
            Widgets.DrawBox(rect);
            GUI.color = Color.white;

            Rect innerRect = rect.ContractedBy(8f);

            // 绘制标题（带折叠按钮）
            Rect titleRect = new Rect(innerRect.x, innerRect.y, innerRect.width, HEADER_HEIGHT);
            DrawTitleWithCollapseButton(titleRect);

            // 如果折叠了，只显示标题和简要信息
            if (isCollapsed)
            {
                DrawCollapsedSummary(new Rect(innerRect.x, innerRect.y + HEADER_HEIGHT, innerRect.width, COLLAPSED_HEIGHT - HEADER_HEIGHT));
                return;
            }

            // 绘制五个维度条
            float rowHeight = (innerRect.height - HEADER_HEIGHT) / 5f;
            float curY = innerRect.y + HEADER_HEIGHT;

            foreach (var dim in dimensions)
            {
                Rect rowRect = new Rect(innerRect.x, curY, innerRect.width, rowHeight - SPACING);
                DrawDimensionRow(rowRect, dim);
                curY += rowHeight;
            }
        }

        /// <summary>
        /// 绘制最小化图标入口（固定高度，不参与展开布局）
        /// </summary>
        public void DrawCompactIcon(Rect rect)
        {
            if (currentValues == null)
            {
                return;
            }

            UpdateAnimations();

            Rect buttonRect = new Rect(
                rect.x + 4f,
                rect.y + (rect.height - COMPACT_BUTTON_SIZE) / 2f,
                COMPACT_BUTTON_SIZE,
                COMPACT_BUTTON_SIZE);
            anchorRect = buttonRect;

            float avgValue = currentValues.GetAverageValue();
            Color glow = GetValueColor(avgValue);

            Widgets.DrawBoxSolid(buttonRect, new Color(0.16f, 0.16f, 0.2f));
            GUI.color = new Color(glow.r, glow.g, glow.b, 0.75f);
            Widgets.DrawBox(buttonRect);
            GUI.color = Color.white;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(buttonRect, "◈");
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            if (Widgets.ButtonInvisible(buttonRect))
            {
                isOverlayExpanded = !isOverlayExpanded;
            }

            string tip = "RimChat_FiveDimensionsTitle".Translate() + "\n" + GetSummaryText(avgValue);
            TooltipHandler.TipRegion(buttonRect, tip);
        }

        /// <summary>
        /// 绘制紧凑浮层（覆盖绘制，不影响主布局）
        /// </summary>
        public void DrawCompactOverlay(Rect hostRect)
        {
            if (currentValues == null)
            {
                return;
            }

            float target = isOverlayExpanded ? 1f : 0f;
            compactOverlayAnimProgress = Mathf.MoveTowards(compactOverlayAnimProgress, target, Time.deltaTime * COMPACT_OVERLAY_ANIM_SPEED);
            if (compactOverlayAnimProgress <= 0.01f)
            {
                return;
            }

            float progress = Mathf.SmoothStep(0f, 1f, compactOverlayAnimProgress);
            float width = Mathf.Min(COMPACT_OVERLAY_WIDTH, hostRect.width - 20f);
            float height = COMPACT_HEADER_HEIGHT + COMPACT_OVERLAY_PADDING * 2f + dimensions.Length * COMPACT_ROW_HEIGHT;
            float x = Mathf.Clamp(anchorRect.xMin, hostRect.x + 8f, hostRect.xMax - width - 8f);
            float y = anchorRect.yMin - height - 6f + (1f - progress) * COMPACT_OVERLAY_INTRO_OFFSET;
            if (y < hostRect.y + 8f)
            {
                y = hostRect.y + 8f;
            }

            overlayRect = new Rect(x, y, width, height);

            if (isOverlayExpanded)
            {
                HandleOverlayDismiss();
            }

            Widgets.DrawBoxSolid(overlayRect, new Color(0.1f, 0.1f, 0.14f, 0.96f * progress));
            GUI.color = new Color(BorderColor.r, BorderColor.g, BorderColor.b, progress);
            Widgets.DrawBox(overlayRect);
            GUI.color = new Color(1f, 1f, 1f, progress);

            DrawCompactOverlayHeader(new Rect(overlayRect.x + COMPACT_OVERLAY_PADDING, overlayRect.y + 4f, overlayRect.width - COMPACT_OVERLAY_PADDING * 2f, COMPACT_HEADER_HEIGHT));

            float rowY = overlayRect.y + COMPACT_HEADER_HEIGHT + COMPACT_OVERLAY_PADDING;
            for (int i = 0; i < dimensions.Length; i++)
            {
                Rect rowRect = new Rect(
                    overlayRect.x + COMPACT_OVERLAY_PADDING,
                    rowY,
                    overlayRect.width - COMPACT_OVERLAY_PADDING * 2f,
                    COMPACT_ROW_HEIGHT);
                DrawCompactDimensionRow(rowRect, dimensions[i]);
                rowY += COMPACT_ROW_HEIGHT;
            }

            GUI.color = Color.white;
        }

        public void CollapseCompactOverlay()
        {
            isOverlayExpanded = false;
        }

        public static float GetCompactAnchorHeight()
        {
            return COMPACT_ANCHOR_HEIGHT;
        }

        private void HandleOverlayDismiss()
        {
            Event current = Event.current;
            if (current.type != EventType.MouseDown || current.button != 0)
            {
                return;
            }

            Vector2 mousePos = current.mousePosition;
            if (!overlayRect.Contains(mousePos) && !anchorRect.Contains(mousePos))
            {
                isOverlayExpanded = false;
            }
        }

        private void DrawCompactOverlayHeader(Rect rect)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = TextPrimary;
            Widgets.Label(new Rect(rect.x, rect.y + 2f, rect.width - 24f, rect.height), "RimChat_FiveDimensionsTitle".Translate());

            Rect closeRect = new Rect(rect.xMax - 20f, rect.y, 20f, 18f);
            GUI.color = TextSecondary;
            if (Widgets.ButtonText(closeRect, "×"))
            {
                isOverlayExpanded = false;
            }
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawCompactDimensionRow(Rect rect, DimensionInfo dim)
        {
            float x = rect.x;
            GUI.color = DimensionColors[dim.Dimension];
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(x, rect.y + 2f, 16f, 16f), DimensionIcons[dim.Dimension]);
            x += 18f;

            GUI.color = TextPrimary;
            Widgets.Label(new Rect(x, rect.y + 2f, 56f, 16f), dim.LabelKey.Translate());
            x += 58f;

            float value = animatedValues[dim.Dimension];
            float barWidth = Mathf.Max(50f, rect.width - 148f);
            Rect barRect = new Rect(x, rect.y + 6f, barWidth, 6f);
            Widgets.DrawBoxSolid(barRect, BarBackgroundColor);

            float centerX = barRect.x + barRect.width / 2f;
            float fillWidth = barRect.width * Mathf.Abs(value) / 200f;
            if (fillWidth > 0.5f)
            {
                Rect fillRect = value < 0
                    ? new Rect(centerX - fillWidth, barRect.y, fillWidth, barRect.height)
                    : new Rect(centerX, barRect.y, fillWidth, barRect.height);
                Widgets.DrawBoxSolid(fillRect, DimensionColors[dim.Dimension]);
            }

            Widgets.DrawBoxSolid(new Rect(centerX - 0.5f, barRect.y - 1f, 1f, barRect.height + 2f), new Color(0.5f, 0.5f, 0.55f, 0.5f));

            GUI.color = GetValueColor(value);
            Widgets.Label(new Rect(barRect.xMax + 8f, rect.y + 2f, 40f, 16f), value.ToString("F0"));
            GUI.color = Color.white;

            if (Mouse.IsOver(rect))
            {
                DrawTooltip(dim, value);
            }

            Text.Font = GameFont.Small;
        }
        
        /// <summary>
        /// 绘制标题（带折叠按钮）
        /// </summary>
        private void DrawTitleWithCollapseButton(Rect rect)
        {
            float buttonSize = 20f;
            Rect buttonRect = new Rect(rect.x, rect.y + 2f, buttonSize, buttonSize);

            // 绘制折叠/展开按钮
            GUI.color = new Color(0.3f, 0.3f, 0.35f);
            Widgets.DrawBoxSolid(buttonRect, new Color(0.2f, 0.2f, 0.25f));
            GUI.color = Color.white;

            // 绘制箭头图标
            Text.Font = GameFont.Small;
            GUI.color = TextSecondary;
            string arrow = isCollapsed ? "▶" : "▼";
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(buttonRect, arrow);
            Text.Anchor = TextAnchor.UpperLeft;

            // 点击处理
            if (Widgets.ButtonInvisible(buttonRect))
            {
                isCollapsed = !isCollapsed;
            }

            // 绘制标题文本
            Rect labelRect = new Rect(rect.x + buttonSize + 8f, rect.y, rect.width - buttonSize - 8f, 20f);
            Widgets.Label(labelRect, "RimChat_FiveDimensionsTitle".Translate());
            GUI.color = Color.white;
        }

        /// <summary>
        /// 绘制折叠状态的简要信息
        /// </summary>
        private void DrawCollapsedSummary(Rect rect)
        {
            if (currentValues == null) return;

            float avgValue = currentValues.GetAverageValue();
            string summaryText = GetSummaryText(avgValue);

            Text.Font = GameFont.Tiny;
            GUI.color = GetValueColor(avgValue);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(rect, summaryText);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        /// <summary>
        /// 获取关系概要文本
        /// </summary>
        private string GetSummaryText(float avgValue)
        {
            if (avgValue >= 50f) return "RimChat_FiveDimensionsSummaryExcellent".Translate();
            if (avgValue >= 20f) return "RimChat_FiveDimensionsSummaryGood".Translate();
            if (avgValue >= -20f) return "RimChat_FiveDimensionsSummaryNeutral".Translate();
            if (avgValue >= -50f) return "RimChat_FiveDimensionsSummaryTense".Translate();
            return "RimChat_FiveDimensionsSummaryBad".Translate();
        }
        
        /// <summary>
        /// 绘制单个维度行
        /// </summary>
        private void DrawDimensionRow(Rect rect, DimensionInfo dim)
        {
            float x = rect.x;
            float y = rect.y + (rect.height - BAR_HEIGHT) / 2f;
            
            // 图标
            GUI.color = DimensionColors[dim.Dimension];
            Text.Font = GameFont.Small;
            Rect iconRect = new Rect(x, y - 4f, ICON_SIZE, ICON_SIZE);
            Widgets.Label(iconRect, DimensionIcons[dim.Dimension]);
            x += ICON_SIZE + 4f;
            
            // 标签
            GUI.color = TextPrimary;
            Text.Font = GameFont.Tiny;
            Rect labelRect = new Rect(x, y - 2f, LABEL_WIDTH, 16f);
            Widgets.Label(labelRect, dim.LabelKey.Translate());
            
            // 计算进度条区域
            float barX = x + LABEL_WIDTH + 8f;
            float barWidth = rect.width - LABEL_WIDTH - VALUE_WIDTH - ICON_SIZE - 20f;
            Rect barBgRect = new Rect(barX, y, barWidth, BAR_HEIGHT);
            
            // 绘制进度条背景
            Widgets.DrawBoxSolid(barBgRect, BarBackgroundColor);

            // 获取当前值 (-100 到 100)
            float currentValue = animatedValues[dim.Dimension];

            // 绘制进度条填充
            float centerX = barBgRect.x + barWidth / 2f;
            Color barColor = DimensionColors[dim.Dimension];

            if (Mathf.Abs(currentValue) > 0.5f)
            {
                // 计算填充宽度：值范围是 -100 到 100，所以除以 200 得到半宽的比例
                float fillRatio = Mathf.Abs(currentValue) / 200f; // 0-0.5 范围
                float fillWidth = barWidth * fillRatio;

                Rect fillRect;
                if (currentValue < 0)
                {
                    // 负值：从中心向左绘制
                    fillRect = new Rect(centerX - fillWidth, barBgRect.y, fillWidth, BAR_HEIGHT);
                }
                else
                {
                    // 正值：从中心向右绘制
                    fillRect = new Rect(centerX, barBgRect.y, fillWidth, BAR_HEIGHT);
                }

                Widgets.DrawBoxSolid(fillRect, barColor);
            }
            
            // 绘制中心线（0点）
            float centerLineX = barBgRect.x + barWidth / 2f;
            Widgets.DrawBoxSolid(new Rect(centerLineX - 0.5f, barBgRect.y - 2f, 1f, BAR_HEIGHT + 4f), 
                new Color(0.5f, 0.5f, 0.55f, 0.5f));
            
            // 数值显示
            float valueX = barX + barWidth + 8f;
            Rect valueRect = new Rect(valueX, y - 2f, VALUE_WIDTH, 16f);
            
            // 如果有变化动画，显示变化数值
            float changeAnim = changeAnimations[dim.Dimension];
            if (Mathf.Abs(changeAnim) > 0.1f)
            {
                GUI.color = changeAnim > 0 ? PositiveChangeColor : NegativeChangeColor;
                string changeText = $"{(changeAnim > 0 ? "+" : "")}{changeAnim:F0}";
                Widgets.Label(valueRect, changeText);
            }
            else
            {
                GUI.color = GetValueColor(currentValue);
                Widgets.Label(valueRect, $"{currentValue:F0}");
            }
            
            // Tooltip
            if (Mouse.IsOver(rect))
            {
                DrawTooltip(dim, currentValue);
            }
            
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }
        
        /// <summary>
        /// 根据数值获取颜色
        /// </summary>
        private Color GetValueColor(float value)
        {
            if (value >= 50f) return new Color(0.4f, 0.9f, 0.5f);
            if (value >= 20f) return new Color(0.6f, 0.9f, 0.6f);
            if (value >= -20f) return TextPrimary;
            if (value >= -50f) return new Color(0.9f, 0.6f, 0.5f);
            return new Color(0.95f, 0.45f, 0.4f);
        }
        
        /// <summary>
        /// 绘制Tooltip
        /// </summary>
        private void DrawTooltip(DimensionInfo dim, float value)
        {
            string tooltip = $"{dim.LabelKey.Translate()}: {value:F1}/100\n";
            tooltip += GetDimensionDescription(dim.Dimension, value);
            TooltipHandler.TipRegion(new Rect(Event.current.mousePosition.x - 10f, Event.current.mousePosition.y - 10f, 20f, 20f), tooltip);
        }
        
        /// <summary>
        /// 获取维度描述
        /// </summary>
        private string GetDimensionDescription(RelationDimension dimension, float value)
        {
            return dimension switch
            {
                RelationDimension.Trust => GetTrustDescription(value),
                RelationDimension.Intimacy => GetIntimacyDescription(value),
                RelationDimension.Reciprocity => GetReciprocityDescription(value),
                RelationDimension.Respect => GetRespectDescription(value),
                RelationDimension.Influence => GetInfluenceDescription(value),
                _ => string.Empty
            };
        }
        
        private string GetTrustDescription(float value)
        {
            return value switch
            {
                >= 60 => "高度信任 - 相信承诺，愿意承担风险",
                >= 20 => "初步信任 - 持谨慎乐观态度",
                >= -20 => "中立观望 - 需要更多证据",
                >= -60 => "深度怀疑 - 对动机持怀疑态度",
                _ => "完全不信任 - 需要严格约束条件"
            };
        }
        
        private string GetIntimacyDescription(float value)
        {
            return value switch
            {
                >= 60 => "非常亲近 - 像老朋友一样交谈",
                >= 20 => "初步友好 - 关系融洽",
                >= -20 => "保持中立 - 基本礼节",
                >= -60 => "冷淡疏远 - 避免非必要交流",
                _ => "充满敌意 - 难以建设性对话"
            };
        }
        
        private string GetReciprocityDescription(float value)
        {
            return value switch
            {
                >= 60 => "欠人情 - 愿意做出重大让步",
                >= 20 => "略有亏欠 - 愿意回报",
                >= -20 => "基本平衡 - 互利往来",
                >= -60 => "对方亏欠 - 要求补偿",
                _ => "对方极度亏欠 - 要求立即补偿"
            };
        }
        
        private string GetRespectDescription(float value)
        {
            return value switch
            {
                >= 60 => "高度尊重 - 认真听取意见",
                >= 20 => "基本尊重 - 认可平等地位",
                >= -20 => "中立评估 - 持中立态度",
                >= -60 => "明显轻视 - 怀疑能力",
                _ => "完全蔑视 - 不值得认真对待"
            };
        }
        
        private string GetInfluenceDescription(float value)
        {
            return value switch
            {
                >= 60 => "高度影响 - 容易接受观点",
                >= 20 => "轻微影响 - 认真考虑建议",
                >= -20 => "基本独立 - 独立判断",
                >= -60 => "明显抵触 - 倾向于反对",
                _ => "完全抗拒 - 坚持己见"
            };
        }
        
        /// <summary>
        /// 更新动画
        /// </summary>
        private void UpdateAnimations()
        {
            float deltaTime = Time.deltaTime;
            
            foreach (var dim in dimensions)
            {
                // 值动画 - 平滑过渡到目标值
                float current = animatedValues[dim.Dimension];
                float target = targetValues[dim.Dimension];
                
                if (Mathf.Abs(target - current) > 0.01f)
                {
                    animatedValues[dim.Dimension] = Mathf.Lerp(current, target, deltaTime * ANIMATION_SPEED);
                }
                else
                {
                    animatedValues[dim.Dimension] = target;
                }
                
                // 变化动画衰减
                float changeAnim = changeAnimations[dim.Dimension];
                if (Mathf.Abs(changeAnim) > 0.1f)
                {
                    changeAnimations[dim.Dimension] = Mathf.Lerp(changeAnim, 0f, deltaTime / CHANGE_ANIMATION_DURATION);
                }
                else
                {
                    changeAnimations[dim.Dimension] = 0f;
                }
            }
        }
        
        /// <summary>
        /// 获取五维属性栏的首选高度
        /// </summary>
        public float GetPreferredHeight()
        {
            return COMPACT_ANCHOR_HEIGHT;
        }

        /// <summary>
        /// 获取五维属性栏的展开高度
        /// </summary>
        public static float GetExpandedHeight()
        {
            return 140f;
        }

        /// <summary>
        /// 获取五维属性栏的折叠高度
        /// </summary>
        public static float GetCollapsedHeight()
        {
            return COMPACT_ANCHOR_HEIGHT;
        }
        
        /// <summary>
        /// 维度信息结构
        /// </summary>
        private class DimensionInfo
        {
            public RelationDimension Dimension { get; }
            public string LabelKey { get; }
            
            public DimensionInfo(RelationDimension dimension, string labelKey)
            {
                Dimension = dimension;
                LabelKey = labelKey;
            }
        }
    }
}
