using System;
using System.Collections.Generic;
using Verse;

namespace RimChat.Relation
{
    /// <summary>/// relation阈valuesbehavior限制system
 /// 根据5维relationvalues的组合决定AIfactionleader可执行的behavior
 ///</summary>
    public static class RelationThresholdBehavior
    {
        // ========== 阈values常量定义 ==========
        
        /// <summary>/// 高relation阈values
 ///</summary>
        public const float HighThreshold = 60f;
        
        /// <summary>/// 中高relation阈values
 ///</summary>
        public const float MediumHighThreshold = 40f;
        
        /// <summary>/// 中等relation阈values
 ///</summary>
        public const float MediumThreshold = 20f;
        
        /// <summary>/// 低relation阈values
 ///</summary>
        public const float LowThreshold = -20f;
        
        /// <summary>/// 极低relation阈values
 ///</summary>
        public const float VeryLowThreshold = -60f;

        // ========== behavior类型枚举 ==========
        
        /// <summary>/// 检查指定behaviorwhether被允许
 ///</summary>
        public static bool IsBehaviorAllowed(FactionRelationValues relations, RelationBehaviorType behaviorType)
        {
            return behaviorType switch
            {
                RelationBehaviorType.FormAlliance => CanFormAlliance(relations),
                RelationBehaviorType.TradeAgreement => CanTradeAgreement(relations),
                RelationBehaviorType.MilitaryAid => CanRequestMilitaryAid(relations),
                RelationBehaviorType.ShareIntel => CanShareIntel(relations),
                RelationBehaviorType.GiftResources => CanGiftResources(relations),
                RelationBehaviorType.PeaceNegotiation => CanPeaceNegotiation(relations),
                RelationBehaviorType.HostileAction => CanHostileAction(relations),
                RelationBehaviorType.BetrayPlayer => CanBetrayPlayer(relations),
                RelationBehaviorType.IgnorePlayer => CanIgnorePlayer(relations),
                RelationBehaviorType.FriendlyChat => CanFriendlyChat(relations),
                RelationBehaviorType.SharePersonalInfo => CanSharePersonalInfo(relations),
                RelationBehaviorType.AcceptDemands => CanAcceptDemands(relations),
                RelationBehaviorType.MakeDemands => CanMakeDemands(relations),
                RelationBehaviorType.VisitColony => CanVisitColony(relations),
                RelationBehaviorType.SendCaravan => CanSendCaravan(relations),
                _ => true
            };
        }

        // ========== 具体behavior判断逻辑 ==========

        /// <summary>/// whether可以结盟
 /// 需要: 信任≥60 且 尊重≥40 且 互惠≥20
 ///</summary>
        private static bool CanFormAlliance(FactionRelationValues relations)
        {
            return relations.Trust >= HighThreshold &&
                   relations.Respect >= MediumHighThreshold &&
                   relations.Reciprocity >= MediumThreshold;
        }

        /// <summary>/// whether可以达成贸易协议
 /// 需要: 信任≥20 或 互惠≥40
 ///</summary>
        private static bool CanTradeAgreement(FactionRelationValues relations)
        {
            return relations.Trust >= MediumThreshold ||
                   relations.Reciprocity >= MediumHighThreshold;
        }

        /// <summary>/// whether可以request军事援助
 /// 需要: 信任≥40 且 亲密度≥20
 /// 或: 互惠≥60 (欠人情)
 ///</summary>
        private static bool CanRequestMilitaryAid(FactionRelationValues relations)
        {
            return (relations.Trust >= MediumHighThreshold && relations.Intimacy >= MediumThreshold) ||
                   relations.Reciprocity >= HighThreshold;
        }

        /// <summary>/// whether可以分享情报
 /// 需要: 信任≥40 且 影响≥20
 /// 或: 亲密度≥60
 ///</summary>
        private static bool CanShareIntel(FactionRelationValues relations)
        {
            return (relations.Trust >= MediumHighThreshold && relations.Influence >= MediumThreshold) ||
                   relations.Intimacy >= HighThreshold;
        }

        /// <summary>/// whether可以赠送资源
 /// 需要: 互惠≥20 (欠人情) 或 亲密度≥40
 ///</summary>
        private static bool CanGiftResources(FactionRelationValues relations)
        {
            return relations.Reciprocity >= MediumThreshold ||
                   relations.Intimacy >= MediumHighThreshold;
        }

        /// <summary>/// whether可以和平谈判
 /// 基础behavior, 总是允许, 但条件影响谈判立场
 ///</summary>
        private static bool CanPeaceNegotiation(FactionRelationValues relations)
        {
            return true;
        }

        /// <summary>/// whether可以采取敌对行动
 /// 当信任≤-40 或 尊重≤-20 时允许
 ///</summary>
        private static bool CanHostileAction(FactionRelationValues relations)
        {
            return relations.Trust <= LowThreshold ||
                   relations.Respect <= LowThreshold;
        }

        /// <summary>/// whether可以背叛玩家
 /// 需要: 信任≤-60 且 影响≤-40
 ///</summary>
        private static bool CanBetrayPlayer(FactionRelationValues relations)
        {
            return relations.Trust <= VeryLowThreshold &&
                   relations.Influence <= MediumHighThreshold * -1;
        }

        /// <summary>/// whether可以无视玩家
 /// 当亲密度≤-40 或 影响≤-60 时
 ///</summary>
        private static bool CanIgnorePlayer(FactionRelationValues relations)
        {
            return relations.Intimacy <= LowThreshold ||
                   relations.Influence <= VeryLowThreshold;
        }

        /// <summary>/// whether可以友好闲聊
 /// 需要: 亲密度≥-20
 ///</summary>
        private static bool CanFriendlyChat(FactionRelationValues relations)
        {
            return relations.Intimacy >= LowThreshold;
        }

        /// <summary>/// whether可以分享个人信息
 /// 需要: 亲密度≥60
 ///</summary>
        private static bool CanSharePersonalInfo(FactionRelationValues relations)
        {
            return relations.Intimacy >= HighThreshold;
        }

        /// <summary>/// whether可以接受玩家要求
 /// 需要: 影响≥40 或 互惠≥20 (欠人情)
 ///</summary>
        private static bool CanAcceptDemands(FactionRelationValues relations)
        {
            return relations.Influence >= MediumHighThreshold ||
                   relations.Reciprocity >= MediumThreshold;
        }

        /// <summary>/// whether可以向玩家提出要求
 /// 需要: 尊重≥40 或 互惠≤-20 (玩家欠人情)
 ///</summary>
        private static bool CanMakeDemands(FactionRelationValues relations)
        {
            return relations.Respect >= MediumHighThreshold ||
                   relations.Reciprocity <= LowThreshold;
        }

        /// <summary>/// whether可以访问colony
 /// 需要: 信任≥20 且 亲密度≥-20
 ///</summary>
        private static bool CanVisitColony(FactionRelationValues relations)
        {
            return relations.Trust >= MediumThreshold &&
                   relations.Intimacy >= LowThreshold;
        }

        /// <summary>/// whether可以派遣商队
 /// 需要: 信任≥10 且 互惠≥-20
 ///</summary>
        private static bool CanSendCaravan(FactionRelationValues relations)
        {
            return relations.Trust >= 10 &&
                   relations.Reciprocity >= LowThreshold;
        }

        // ========== getbehavior限制信息 ==========

        /// <summary>/// getbehavior限制描述
 ///</summary>
        public static string GetBehaviorRestrictionDescription(FactionRelationValues relations, RelationBehaviorType behaviorType)
        {
            bool allowed = IsBehaviorAllowed(relations, behaviorType);
            var requirements = GetBehaviorRequirements(behaviorType);
            
            if (allowed)
            {
                return $"{behaviorType.GetLabel()} - 允许执行";
            }
            else
            {
                return $"{behaviorType.GetLabel()} - 受限\n需要: {requirements}";
            }
        }

        /// <summary>/// getbehavior的需求描述
 ///</summary>
        private static string GetBehaviorRequirements(RelationBehaviorType behaviorType)
        {
            return behaviorType switch
            {
                RelationBehaviorType.FormAlliance => "信任≥60, 尊重≥40, 互惠≥20",
                RelationBehaviorType.TradeAgreement => "信任≥20 或 互惠≥40",
                RelationBehaviorType.MilitaryAid => "(信任≥40 且 亲密≥20) 或 互惠≥60",
                RelationBehaviorType.ShareIntel => "(信任≥40 且 影响≥20) 或 亲密≥60",
                RelationBehaviorType.GiftResources => "互惠≥20 或 亲密≥40",
                RelationBehaviorType.HostileAction => "信任≤-40 或 尊重≤-20",
                RelationBehaviorType.BetrayPlayer => "信任≤-60 且 影响≤-40",
                RelationBehaviorType.IgnorePlayer => "亲密≤-40 或 影响≤-60",
                RelationBehaviorType.FriendlyChat => "亲密≥-20",
                RelationBehaviorType.SharePersonalInfo => "亲密≥60",
                RelationBehaviorType.AcceptDemands => "影响≥40 或 互惠≥20",
                RelationBehaviorType.MakeDemands => "尊重≥40 或 互惠≤-20",
                RelationBehaviorType.VisitColony => "信任≥20 且 亲密≥-20",
                RelationBehaviorType.SendCaravan => "信任≥10 且 互惠≥-20",
                _ => "无特殊要求"
            };
        }

        /// <summary>/// get所有可用behavior列表
 ///</summary>
        public static List<RelationBehaviorType> GetAvailableBehaviors(FactionRelationValues relations)
        {
            var available = new List<RelationBehaviorType>();
            
            foreach (RelationBehaviorType behavior in Enum.GetValues(typeof(RelationBehaviorType)))
            {
                if (IsBehaviorAllowed(relations, behavior))
                {
                    available.Add(behavior);
                }
            }
            
            return available;
        }

        /// <summary>/// getrelationstate综合评估
 ///</summary>
        public static string GetOverallRelationStatus(FactionRelationValues relations)
        {
            float avgValue = relations.GetAverageValue();
            
            return avgValue switch
            {
                >= 60 => "亲密盟友",
                >= 40 => "友好合作",
                >= 20 => "良好往来",
                >= -20 => "中立观望",
                >= -40 => "冷淡疏远",
                >= -60 => "敌对警惕",
                _ => "势不两立"
            };
        }
    }

    /// <summary>/// relationbehavior类型枚举
 ///</summary>
    public enum RelationBehaviorType
    {
        FormAlliance,       // 结盟
        TradeAgreement,     // 贸易协议
        MilitaryAid,        // 军事援助
        ShareIntel,         // 分享情报
        GiftResources,      // 赠送资源
        PeaceNegotiation,   // 和平谈判
        HostileAction,      // 敌对行动
        BetrayPlayer,       // 背叛玩家
        IgnorePlayer,       // 无视玩家
        FriendlyChat,       // 友好闲聊
        SharePersonalInfo,  // 分享个人信息
        AcceptDemands,      // 接受要求
        MakeDemands,        // 提出要求
        VisitColony,        // 访问colony
        SendCaravan         // 派遣商队
    }

    /// <summary>/// relationbehavior类型扩展method
 ///</summary>
    public static class RelationBehaviorTypeExtensions
    {
        /// <summary>/// getbehavior类型的displaylabel
 ///</summary>
        public static string GetLabel(this RelationBehaviorType behavior)
        {
            return behavior switch
            {
                RelationBehaviorType.FormAlliance => "结盟",
                RelationBehaviorType.TradeAgreement => "贸易协议",
                RelationBehaviorType.MilitaryAid => "军事援助",
                RelationBehaviorType.ShareIntel => "分享情报",
                RelationBehaviorType.GiftResources => "赠送资源",
                RelationBehaviorType.PeaceNegotiation => "和平谈判",
                RelationBehaviorType.HostileAction => "敌对行动",
                RelationBehaviorType.BetrayPlayer => "背叛",
                RelationBehaviorType.IgnorePlayer => "无视",
                RelationBehaviorType.FriendlyChat => "友好闲聊",
                RelationBehaviorType.SharePersonalInfo => "分享个人信息",
                RelationBehaviorType.AcceptDemands => "接受要求",
                RelationBehaviorType.MakeDemands => "提出要求",
                RelationBehaviorType.VisitColony => "访问殖民地",
                RelationBehaviorType.SendCaravan => "派遣商队",
                _ => behavior.ToString()
            };
        }

        /// <summary>/// getbehavior类型的描述
 ///</summary>
        public static string GetDescription(this RelationBehaviorType behavior)
        {
            return behavior switch
            {
                RelationBehaviorType.FormAlliance => "与玩家建立正式同盟关系",
                RelationBehaviorType.TradeAgreement => "与玩家达成长期贸易协议",
                RelationBehaviorType.MilitaryAid => "在冲突中向玩家提供军事支持",
                RelationBehaviorType.ShareIntel => "与玩家分享重要情报信息",
                RelationBehaviorType.GiftResources => "主动向玩家赠送资源",
                RelationBehaviorType.PeaceNegotiation => "与玩家进行和平谈判",
                RelationBehaviorType.HostileAction => "对玩家采取敌对行动",
                RelationBehaviorType.BetrayPlayer => "背叛与玩家的协议",
                RelationBehaviorType.IgnorePlayer => "无视玩家的请求和存在",
                RelationBehaviorType.FriendlyChat => "与玩家进行非正式友好交流",
                RelationBehaviorType.SharePersonalInfo => "与玩家分享个人想法和派系内部信息",
                RelationBehaviorType.AcceptDemands => "接受玩家提出的要求",
                RelationBehaviorType.MakeDemands => "向玩家提出要求",
                RelationBehaviorType.VisitColony => "派遣代表访问玩家殖民地",
                RelationBehaviorType.SendCaravan => "向玩家殖民地派遣商队",
                _ => behavior.ToString()
            };
        }
    }
}
