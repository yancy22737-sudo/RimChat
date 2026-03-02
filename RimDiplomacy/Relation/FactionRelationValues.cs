using System;
using Verse;
using RimWorld;

namespace RimDiplomacy.Relation
{
    /// <summary>
    /// 派系关系值数据结构
    /// 记录AI派系领袖对玩家派系的5个维度关系评估
    /// 数值范围: -100 ~ 100
    /// </summary>
    public class FactionRelationValues : IExposable
    {
        // ========== 核心关系字段 ==========
        
        /// <summary>
        /// 信任值 (Trust)
        /// 衡量领袖对玩家承诺和行为的信赖程度
        /// 影响因素: 履约记录、诚实度、历史行为一致性
        /// </summary>
        private float _trust;
        public float Trust
        {
            get => _trust;
            set => _trust = ClampValue(value);
        }
        
        /// <summary>
        /// 亲密度 (Intimacy)
        /// 衡量领袖与玩家之间的个人情感亲近程度
        /// 影响因素: 对话频率、共同经历、情感交流深度
        /// </summary>
        private float _intimacy;
        public float Intimacy
        {
            get => _intimacy;
            set => _intimacy = ClampValue(value);
        }
        
        /// <summary>
        /// 互惠值 (Reciprocity)
        /// 衡量双方的互利交换平衡程度
        /// 影响因素: 贸易往来、援助互惠、礼物交换
        /// </summary>
        private float _reciprocity;
        public float Reciprocity
        {
            get => _reciprocity;
            set => _reciprocity = ClampValue(value);
        }
        
        /// <summary>
        /// 尊重值 (Respect)
        /// 衡量领袖对玩家实力、地位和决策的认可程度
        /// 影响因素: 军事实力、殖民地发展、外交礼仪
        /// </summary>
        private float _respect;
        public float Respect
        {
            get => _respect;
            set => _respect = ClampValue(value);
        }
        
        /// <summary>
        /// 影响值 (Influence)
        /// 衡量玩家对该领袖决策的渗透和影响能力
        /// 影响因素: 外交手腕、情报掌握、政治操作
        /// </summary>
        private float _influence;
        public float Influence
        {
            get => _influence;
            set => _influence = ClampValue(value);
        }

        // ========== 元数据字段 ==========
        
        /// <summary>
        /// 最后更新时间 tick
        /// </summary>
        public int LastUpdatedTick;
        
        /// <summary>
        /// 最后对话时间 tick（用于衰减计算）
        /// </summary>
        public int LastDialogueTick;
        
        /// <summary>
        /// 更新次数计数
        /// </summary>
        public int UpdateCount;

        // ========== 常量定义 ==========
        
        public const float MinValue = -100f;
        public const float MaxValue = 100f;
        public const float DefaultValue = 0f;
        
        /// <summary>
        /// 衰减检查间隔（每多少tick检查一次）
        /// </summary>
        public const int DecayCheckInterval = 60000; // 约1游戏日
        
        /// <summary>
        /// 衰减阈值（超过多少tick无互动开始衰减）
        /// </summary>
        public const int DecayThresholdTicks = 180000; // 约3游戏日
        
        /// <summary>
        /// 每次衰减量
        /// </summary>
        public const float DecayAmount = 2f;

        // ========== 构造函数 ==========
        
        public FactionRelationValues()
        {
            ResetToDefault();
        }

        // ========== 核心方法 ==========
        
        /// <summary>
        /// 重置所有值为默认值
        /// </summary>
        public void ResetToDefault()
        {
            _trust = DefaultValue;
            _intimacy = DefaultValue;
            _reciprocity = DefaultValue;
            _respect = DefaultValue;
            _influence = DefaultValue;
            LastUpdatedTick = Find.TickManager?.TicksGame ?? 0;
            LastDialogueTick = LastUpdatedTick;
            UpdateCount = 0;
        }

        /// <summary>
        /// 数值钳制到有效范围
        /// </summary>
        private float ClampValue(float value)
        {
            return Math.Max(MinValue, Math.Min(MaxValue, value));
        }

        /// <summary>
        /// 批量更新关系值（来自LLM响应）
        /// </summary>
        public void UpdateFromLLMResponse(float trustDelta, float intimacyDelta, float reciprocityDelta, 
            float respectDelta, float influenceDelta)
        {
            Trust += trustDelta;
            Intimacy += intimacyDelta;
            Reciprocity += reciprocityDelta;
            Respect += respectDelta;
            Influence += influenceDelta;
            
            LastUpdatedTick = Find.TickManager.TicksGame;
            UpdateCount++;
        }

        /// <summary>
        /// 执行时间衰减
        /// </summary>
        public void ApplyDecay()
        {
            int currentTick = Find.TickManager.TicksGame;
            int ticksSinceLastDialogue = currentTick - LastDialogueTick;
            
            if (ticksSinceLastDialogue < DecayThresholdTicks)
                return;
            
            // 计算衰减次数
            int decayCycles = (ticksSinceLastDialogue - DecayThresholdTicks) / DecayCheckInterval;
            float totalDecay = DecayAmount * decayCycles;
            
            // 亲密度和信任值衰减较快
            Intimacy = MoveTowardDefault(Intimacy, totalDecay * 1.5f);
            Trust = MoveTowardDefault(Trust, totalDecay);
            
            // 互惠值衰减较慢
            Reciprocity = MoveTowardDefault(Reciprocity, totalDecay * 0.5f);
            
            // 尊重值基本不衰减（基于实力认知）
            // 影响值轻微衰减
            Influence = MoveTowardDefault(Influence, totalDecay * 0.3f);
            
            LastUpdatedTick = currentTick;
        }

        /// <summary>
        /// 将值向默认值移动
        /// </summary>
        private float MoveTowardDefault(float current, float amount)
        {
            if (current > DefaultValue)
            {
                return Math.Max(DefaultValue, current - amount);
            }
            else if (current < DefaultValue)
            {
                return Math.Min(DefaultValue, current + amount);
            }
            return current;
        }

        /// <summary>
        /// 记录对话互动（重置衰减计时）
        /// </summary>
        public void RecordDialogue()
        {
            LastDialogueTick = Find.TickManager.TicksGame;
        }

        /// <summary>
        /// 获取关系值摘要（用于调试显示）
        /// </summary>
        public string GetSummary()
        {
            return $"信任:{Trust:F1} 亲密:{Intimacy:F1} 互惠:{Reciprocity:F1} 尊重:{Respect:F1} 影响:{Influence:F1}";
        }

        /// <summary>
        /// 序列化/反序列化
        /// </summary>
        public void ExposeData()
        {
            float trustValue = _trust;
            float intimacyValue = _intimacy;
            float reciprocityValue = _reciprocity;
            float respectValue = _respect;
            float influenceValue = _influence;
            
            Scribe_Values.Look(ref trustValue, "trust", DefaultValue);
            Scribe_Values.Look(ref intimacyValue, "intimacy", DefaultValue);
            Scribe_Values.Look(ref reciprocityValue, "reciprocity", DefaultValue);
            Scribe_Values.Look(ref respectValue, "respect", DefaultValue);
            Scribe_Values.Look(ref influenceValue, "influence", DefaultValue);
            
            _trust = trustValue;
            _intimacy = intimacyValue;
            _reciprocity = reciprocityValue;
            _respect = respectValue;
            _influence = influenceValue;
            
            Scribe_Values.Look(ref LastUpdatedTick, "lastUpdatedTick", 0);
            Scribe_Values.Look(ref LastDialogueTick, "lastDialogueTick", 0);
            Scribe_Values.Look(ref UpdateCount, "updateCount", 0);
        }

        // ========== 便捷方法 ==========
        
        /// <summary>
        /// 获取平均关系值
        /// </summary>
        public float GetAverageValue()
        {
            return (Trust + Intimacy + Reciprocity + Respect + Influence) / 5f;
        }

        /// <summary>
        /// 检查是否达到指定阈值
        /// </summary>
        public bool IsAboveThreshold(RelationDimension dimension, float threshold)
        {
            return GetValue(dimension) >= threshold;
        }

        /// <summary>
        /// 检查是否低于指定阈值
        /// </summary>
        public bool IsBelowThreshold(RelationDimension dimension, float threshold)
        {
            return GetValue(dimension) <= threshold;
        }

        /// <summary>
        /// 获取指定维度的值
        /// </summary>
        public float GetValue(RelationDimension dimension)
        {
            return dimension switch
            {
                RelationDimension.Trust => Trust,
                RelationDimension.Intimacy => Intimacy,
                RelationDimension.Reciprocity => Reciprocity,
                RelationDimension.Respect => Respect,
                RelationDimension.Influence => Influence,
                _ => DefaultValue
            };
        }

        /// <summary>
        /// 设置指定维度的值
        /// </summary>
        public void SetValue(RelationDimension dimension, float value)
        {
            switch (dimension)
            {
                case RelationDimension.Trust: Trust = value; break;
                case RelationDimension.Intimacy: Intimacy = value; break;
                case RelationDimension.Reciprocity: Reciprocity = value; break;
                case RelationDimension.Respect: Respect = value; break;
                case RelationDimension.Influence: Influence = value; break;
            }
        }
    }

    /// <summary>
    /// 关系维度枚举
    /// </summary>
    public enum RelationDimension
    {
        Trust,      // 信任值
        Intimacy,   // 亲密度
        Reciprocity,// 互惠值
        Respect,    // 尊重值
        Influence   // 影响值
    }
}
