using System;
using System.Collections.Generic;
using Verse;

namespace RimDiplomacy.Relation
{
    /// <summary>
    /// LLM对话行为好感度消耗配置
    /// 定义各种外交行为的基础消耗和关系值修正系数
    /// </summary>
    public static class DialogueGoodwillCost
    {
        // ========== 基础消耗值 ==========
        
        /// <summary>
        /// 请求商队基础消耗
        /// </summary>
        public const int BaseCost_RequestCaravan = -8;
        
        /// <summary>
        /// 请求军事援助基础消耗
        /// </summary>
        public const int BaseCost_RequestMilitaryAid = -15;
        
        /// <summary>
        /// 请求医疗援助基础消耗
        /// </summary>
        public const int BaseCost_RequestMedicalAid = -10;
        
        /// <summary>
        /// 请求资源援助基础消耗
        /// </summary>
        public const int BaseCost_RequestResourceAid = -12;
        
        /// <summary>
        /// 要求派系离开基础消耗
        /// </summary>
        public const int BaseCost_DemandLeave = -20;
        
        /// <summary>
        /// 要求支付赎金/赔偿基础消耗
        /// </summary>
        public const int BaseCost_DemandPayment = -15;
        
        /// <summary>
        /// 分享情报基础收益
        /// </summary>
        public const int BaseGain_ShareIntel = 5;
        
        /// <summary>
        /// 赠送礼物基础收益
        /// </summary>
        public const int BaseGain_SendGift = 8;
        
        /// <summary>
        /// 履行承诺基础收益
        /// </summary>
        public const int BaseGain_FulfillPromise = 10;
        
        /// <summary>
        /// 接受要求基础收益
        /// </summary>
        public const int BaseGain_AcceptDemand = 5;
        
        /// <summary>
        /// 道歉基础收益
        /// </summary>
        public const int BaseGain_Apologize = 3;

        // ========== 关系值修正系数 ==========
        
        /// <summary>
        /// 信任值修正系数（高信任减少消耗）
        /// 每10点信任减少消耗的比例
        /// </summary>
        public const float TrustModifier = 0.05f;
        
        /// <summary>
        /// 亲密度修正系数（高亲密减少消耗）
        /// </summary>
        public const float IntimacyModifier = 0.03f;
        
        /// <summary>
        /// 互惠值修正系数（正互惠减少消耗，负互惠增加消耗）
        /// </summary>
        public const float ReciprocityModifier = 0.04f;
        
        /// <summary>
        /// 尊重值修正系数（高尊重减少消耗）
        /// </summary>
        public const float RespectModifier = 0.02f;
        
        /// <summary>
        /// 影响值修正系数（高影响减少消耗）
        /// </summary>
        public const float InfluenceModifier = 0.03f;

        // ========== 限制常量 ==========
        
        /// <summary>
        /// 单次消耗最大限制（防止过度消耗）
        /// </summary>
        public const int MaxSingleCost = -25;
        
        /// <summary>
        /// 单次收益最大限制
        /// </summary>
        public const int MaxSingleGain = 15;
        
        /// <summary>
        /// 每日消耗上限
        /// </summary>
        public const int DailyCostLimit = -50;
        
        /// <summary>
        /// 每日收益上限
        /// </summary>
        public const int DailyGainLimit = 30;

        // ========== 行为类型枚举 ==========
        
        /// <summary>
        /// 对话行为类型
        /// </summary>
        public enum DialogueActionType
        {
            RequestCaravan,      // 请求商队
            RequestMilitaryAid,  // 请求军事援助
            RequestMedicalAid,   // 请求医疗援助
            RequestResourceAid,  // 请求资源援助
            DemandLeave,         // 要求离开
            DemandPayment,       // 要求支付
            ShareIntel,          // 分享情报
            SendGift,            // 赠送礼物
            FulfillPromise,      // 履行承诺
            AcceptDemand,        // 接受要求
            Apologize,           // 道歉
            FriendlyChat,        // 友好闲聊
            Threaten,            // 威胁
            Insult,              // 侮辱
            Compliment,          // 赞美
            MakePromise,         // 做出承诺
        }

        // ========== 获取基础值 ==========
        
        /// <summary>
        /// 获取行为的基础好感度变化值
        /// </summary>
        public static int GetBaseValue(DialogueActionType actionType)
        {
            return actionType switch
            {
                DialogueActionType.RequestCaravan => BaseCost_RequestCaravan,
                DialogueActionType.RequestMilitaryAid => BaseCost_RequestMilitaryAid,
                DialogueActionType.RequestMedicalAid => BaseCost_RequestMedicalAid,
                DialogueActionType.RequestResourceAid => BaseCost_RequestResourceAid,
                DialogueActionType.DemandLeave => BaseCost_DemandLeave,
                DialogueActionType.DemandPayment => BaseCost_DemandPayment,
                DialogueActionType.ShareIntel => BaseGain_ShareIntel,
                DialogueActionType.SendGift => BaseGain_SendGift,
                DialogueActionType.FulfillPromise => BaseGain_FulfillPromise,
                DialogueActionType.AcceptDemand => BaseGain_AcceptDemand,
                DialogueActionType.Apologize => BaseGain_Apologize,
                DialogueActionType.FriendlyChat => 0,
                DialogueActionType.Threaten => -10,
                DialogueActionType.Insult => -8,
                DialogueActionType.Compliment => 3,
                DialogueActionType.MakePromise => 2,
                _ => 0
            };
        }

        /// <summary>
        /// 判断行为是消耗型还是收益型
        /// </summary>
        public static bool IsCostAction(DialogueActionType actionType)
        {
            int baseValue = GetBaseValue(actionType);
            return baseValue < 0;
        }

        /// <summary>
        /// 判断行为是否受关系值影响
        /// </summary>
        public static bool IsRelationModified(DialogueActionType actionType)
        {
            // 闲聊、侮辱、赞美等简单对话不受关系值修正
            return actionType switch
            {
                DialogueActionType.FriendlyChat => false,
                DialogueActionType.Insult => false,
                DialogueActionType.Compliment => false,
                _ => true
            };
        }

        /// <summary>
        /// 获取行为的冷却时间（tick）
        /// </summary>
        public static int GetCooldownTicks(DialogueActionType actionType)
        {
            return actionType switch
            {
                DialogueActionType.RequestCaravan => 60000,      // 1天
                DialogueActionType.RequestMilitaryAid => 180000, // 3天
                DialogueActionType.RequestMedicalAid => 120000,  // 2天
                DialogueActionType.RequestResourceAid => 120000, // 2天
                DialogueActionType.DemandLeave => 90000,         // 1.5天
                DialogueActionType.DemandPayment => 60000,       // 1天
                DialogueActionType.ShareIntel => 30000,          // 0.5天
                DialogueActionType.SendGift => 60000,            // 1天
                DialogueActionType.FulfillPromise => 0,          // 无冷却
                DialogueActionType.AcceptDemand => 0,            // 无冷却
                DialogueActionType.Apologize => 30000,           // 0.5天
                DialogueActionType.FriendlyChat => 0,            // 无冷却
                DialogueActionType.Threaten => 60000,            // 1天
                DialogueActionType.Insult => 30000,              // 0.5天
                DialogueActionType.Compliment => 0,              // 无冷却
                DialogueActionType.MakePromise => 0,             // 无冷却
                _ => 60000
            };
        }

        /// <summary>
        /// 获取行为的显示名称
        /// </summary>
        public static string GetActionLabel(DialogueActionType actionType)
        {
            return actionType switch
            {
                DialogueActionType.RequestCaravan => "请求商队",
                DialogueActionType.RequestMilitaryAid => "请求军事援助",
                DialogueActionType.RequestMedicalAid => "请求医疗援助",
                DialogueActionType.RequestResourceAid => "请求资源援助",
                DialogueActionType.DemandLeave => "要求离开",
                DialogueActionType.DemandPayment => "要求支付",
                DialogueActionType.ShareIntel => "分享情报",
                DialogueActionType.SendGift => "赠送礼物",
                DialogueActionType.FulfillPromise => "履行承诺",
                DialogueActionType.AcceptDemand => "接受要求",
                DialogueActionType.Apologize => "道歉",
                DialogueActionType.FriendlyChat => "友好闲聊",
                DialogueActionType.Threaten => "威胁",
                DialogueActionType.Insult => "侮辱",
                DialogueActionType.Compliment => "赞美",
                DialogueActionType.MakePromise => "做出承诺",
                _ => actionType.ToString()
            };
        }

        /// <summary>
        /// 获取行为的描述
        /// </summary>
        public static string GetActionDescription(DialogueActionType actionType)
        {
            return actionType switch
            {
                DialogueActionType.RequestCaravan => "请求派系派遣商队访问你的殖民地",
                DialogueActionType.RequestMilitaryAid => "请求派系派遣军事援助",
                DialogueActionType.RequestMedicalAid => "请求派系提供医疗援助",
                DialogueActionType.RequestResourceAid => "请求派系提供资源援助",
                DialogueActionType.DemandLeave => "要求派系人员离开你的领地",
                DialogueActionType.DemandPayment => "要求派系支付赔偿或赎金",
                DialogueActionType.ShareIntel => "与派系分享有价值的情报",
                DialogueActionType.SendGift => "向派系赠送礼物",
                DialogueActionType.FulfillPromise => "履行之前做出的承诺",
                DialogueActionType.AcceptDemand => "接受派系提出的要求",
                DialogueActionType.Apologize => "为之前的冒犯道歉",
                DialogueActionType.FriendlyChat => "进行友好的闲聊",
                DialogueActionType.Threaten => "对派系进行威胁",
                DialogueActionType.Insult => "侮辱派系",
                DialogueActionType.Compliment => "赞美派系",
                DialogueActionType.MakePromise => "向派系做出承诺",
                _ => actionType.ToString()
            };
        }
    }

    /// <summary>
    /// 行为消耗记录（用于每日限制）
    /// </summary>
    public class DialogueActionRecord : IExposable
    {
        public DialogueGoodwillCost.DialogueActionType ActionType;
        public int GoodwillChange;
        public int Tick;
        public string FactionName;

        public void ExposeData()
        {
            Scribe_Values.Look(ref ActionType, "actionType");
            Scribe_Values.Look(ref GoodwillChange, "goodwillChange", 0);
            Scribe_Values.Look(ref Tick, "tick", 0);
            Scribe_Values.Look(ref FactionName, "factionName", "");
        }
    }
}
