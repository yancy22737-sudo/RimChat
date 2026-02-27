using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimDiplomacy.UI
{
    /// <summary>
    /// 好感度变化动画数据
    /// </summary>
    public class GoodwillChangeAnimation
    {
        public Faction TargetFaction { get; set; }
        public int ChangeAmount { get; set; }
        public float StartTime { get; set; }
        public Vector2 StartPosition { get; set; }
        public float Duration { get; set; }
        public bool IsComplete => Time.time - StartTime >= Duration;

        public GoodwillChangeAnimation(Faction faction, int changeAmount, Vector2 startPosition, float duration = 1.5f)
        {
            TargetFaction = faction;
            ChangeAmount = changeAmount;
            StartPosition = startPosition;
            StartTime = Time.time;
            Duration = duration;
        }

        /// <summary>
        /// 获取当前动画进度 (0-1)
        /// </summary>
        public float GetProgress()
        {
            return Mathf.Clamp01((Time.time - StartTime) / Duration);
        }

        /// <summary>
        /// 获取当前位置（向上浮动）
        /// </summary>
        public Vector2 GetCurrentPosition()
        {
            float progress = GetProgress();
            float floatDistance = 50f;
            return new Vector2(StartPosition.x, StartPosition.y - progress * floatDistance);
        }

        /// <summary>
        /// 获取当前透明度
        /// </summary>
        public float GetCurrentAlpha()
        {
            float progress = GetProgress();
            // 前30%保持完全不透明，后70%逐渐淡出
            if (progress < 0.3f)
                return 1f;
            return Mathf.Lerp(1f, 0f, (progress - 0.3f) / 0.7f);
        }

        /// <summary>
        /// 获取颜色（增加绿色，减少红色）
        /// </summary>
        public Color GetColor()
        {
            if (ChangeAmount >= 0)
            {
                return new Color(0.3f, 0.9f, 0.4f); // 绿色
            }
            else
            {
                return new Color(0.95f, 0.35f, 0.35f); // 红色
            }
        }

        /// <summary>
        /// 获取显示文本
        /// </summary>
        public string GetDisplayText()
        {
            return ChangeAmount >= 0 ? $"+{ChangeAmount}" : ChangeAmount.ToString();
        }
    }

    /// <summary>
    /// 好感度变化动画管理器
    /// 管理所有浮动数值动画的创建、更新和渲染
    /// </summary>
    public static class GoodwillChangeAnimator
    {
        private static readonly List<GoodwillChangeAnimation> activeAnimations = new List<GoodwillChangeAnimation>();
        private static readonly Dictionary<Faction, int> lastKnownGoodwill = new Dictionary<Faction, int>();

        // 动画配置
        private const float ANIMATION_DURATION = 1.8f;
        private const float FLOAT_DISTANCE = 50f;
        private const float TEXT_SCALE = 1.2f;

        /// <summary>
        /// 检查并记录好感度变化
        /// 应在UI更新时调用
        /// </summary>
        public static void CheckGoodwillChanges(List<Faction> factions)
        {
            if (factions == null) return;

            foreach (var faction in factions)
            {
                if (faction == null) continue;

                int currentGoodwill = faction.PlayerGoodwill;

                if (lastKnownGoodwill.TryGetValue(faction, out int lastGoodwill))
                {
                    int change = currentGoodwill - lastGoodwill;
                    if (change != 0)
                    {
                        // 好感度发生变化，触发事件
                        TriggerGoodwillChangeEvent(faction, change);
                    }
                }

                // 更新记录
                lastKnownGoodwill[faction] = currentGoodwill;
            }

            // 清理已不存在的派系记录
            var factionsToRemove = new List<Faction>();
            foreach (var recordedFaction in lastKnownGoodwill.Keys)
            {
                if (!factions.Contains(recordedFaction))
                {
                    factionsToRemove.Add(recordedFaction);
                }
            }
            foreach (var faction in factionsToRemove)
            {
                lastKnownGoodwill.Remove(faction);
            }
        }

        /// <summary>
        /// 手动触发好感度变化动画
        /// </summary>
        public static void TriggerGoodwillChangeEvent(Faction faction, int changeAmount)
        {
            if (faction == null || changeAmount == 0) return;

            // 事件触发，由UI层处理显示位置
            OnGoodwillChanged?.Invoke(faction, changeAmount);
        }

        /// <summary>
        /// 创建动画实例（由UI层调用）
        /// </summary>
        public static void CreateAnimation(Faction faction, int changeAmount, Vector2 screenPosition)
        {
            if (faction == null || changeAmount == 0) return;

            var animation = new GoodwillChangeAnimation(faction, changeAmount, screenPosition, ANIMATION_DURATION);
            activeAnimations.Add(animation);
        }

        /// <summary>
        /// 更新并绘制所有活动动画
        /// 应在OnGUI中调用
        /// </summary>
        public static void UpdateAndDrawAnimations()
        {
            // 移除已完成的动画
            activeAnimations.RemoveAll(a => a.IsComplete);

            // 绘制活动动画
            foreach (var animation in activeAnimations)
            {
                DrawAnimation(animation);
            }
        }

        /// <summary>
        /// 绘制单个动画
        /// </summary>
        private static void DrawAnimation(GoodwillChangeAnimation animation)
        {
            Vector2 position = animation.GetCurrentPosition();
            float alpha = animation.GetCurrentAlpha();
            Color color = animation.GetColor();
            color.a = alpha;

            string text = animation.GetDisplayText();

            // 计算文本大小
            Text.Font = GameFont.Medium;
            Vector2 textSize = Text.CalcSize(text);

            // 添加阴影效果
            Rect shadowRect = new Rect(position.x + 2f, position.y + 2f, textSize.x, textSize.y);
            GUI.color = new Color(0f, 0f, 0f, alpha * 0.5f);
            Widgets.Label(shadowRect, text);

            // 绘制主文本
            Rect textRect = new Rect(position.x, position.y, textSize.x, textSize.y);
            GUI.color = color;
            Widgets.Label(textRect, text);

            // 恢复默认设置
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        /// <summary>
        /// 清理所有动画和记录
        /// </summary>
        public static void ClearAll()
        {
            activeAnimations.Clear();
            lastKnownGoodwill.Clear();
        }

        /// <summary>
        /// 好感度变化事件
        /// 参数：派系，变化值
        /// </summary>
        public static event Action<Faction, int> OnGoodwillChanged;
    }
}
