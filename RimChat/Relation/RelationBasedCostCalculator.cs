using System;
using RimWorld;
using Verse;

namespace RimChat.Relation
{
    /// <summary>/// based on5维relationvalues的goodwill消耗calculator
 /// 根据factionrelationdynamic计算behavior的goodwill消耗/收益
 ///</summary>
    public static class RelationBasedCostCalculator
    {
        /// <summary>/// 计算behavior的实际goodwill变化values
 ///</summary>
        /// <param name="actionType">behavior类型</param>
        /// <param name="relations">5维relationvalues</param>
        /// <param name="costInfo">output消耗信息详情</param>
        /// <returns>实际goodwill变化values</returns>
        public static int CalculateCost(
            DialogueGoodwillCost.DialogueActionType actionType, 
            FactionRelationValues relations,
            out CostCalculationInfo costInfo)
        {
            costInfo = new CostCalculationInfo();
            
            // Get基础values
            int baseValue = DialogueGoodwillCost.GetBaseValue(actionType);
            costInfo.BaseValue = baseValue;
            
            // 如果不受relationvalues影响, 直接返回基础values
            if (!DialogueGoodwillCost.IsRelationModified(actionType))
            {
                costInfo.FinalValue = baseValue;
                return baseValue;
            }
            
            // 计算relationvalues修正
            float modifier = CalculateRelationModifier(relations, baseValue < 0);
            costInfo.RelationModifier = modifier;
            
            // Apply修正
            float modifiedValue = baseValue * modifier;
            int finalValue = (int)Math.Round(modifiedValue);
            
            // Apply限制
            if (baseValue < 0)
            {
                // 消耗型behavior
                finalValue = Math.Max(finalValue, DialogueGoodwillCost.MaxSingleCost);
            }
            else if (baseValue > 0)
            {
                // 收益型behavior
                finalValue = Math.Min(finalValue, DialogueGoodwillCost.MaxSingleGain);
            }
            
            costInfo.FinalValue = finalValue;
            costInfo.ModifierBreakdown = GetModifierBreakdown(relations, baseValue < 0);
            
            return finalValue;
        }

        /// <summary>/// 简化的计算method (不需要详细信息)
 ///</summary>
        public static int CalculateCost(
            DialogueGoodwillCost.DialogueActionType actionType, 
            FactionRelationValues relations)
        {
            return CalculateCost(actionType, relations, out _);
        }

        /// <summary>/// 计算relationvalues修正系数
 ///</summary>
        /// <param name="relations">5维relationvalues</param>
        /// <param name="isCostAction">whether是消耗型behavior</param>
        /// <returns>修正系数 (1.0为无修正) </returns>
        private static float CalculateRelationModifier(FactionRelationValues relations, bool isCostAction)
        {
            float modifier = 1.0f;
            
            // 信任values修正: 高信任减少消耗/增加收益
            // 每10点信任提供5%的修正
            float trustBonus = (relations.Trust / 10f) * DialogueGoodwillCost.TrustModifier;
            modifier -= trustBonus;
            
            // 亲密度修正: 高亲密减少消耗/增加收益
            float intimacyBonus = (relations.Intimacy / 10f) * DialogueGoodwillCost.IntimacyModifier;
            modifier -= intimacyBonus;
            
            // 互惠values修正: 正互惠减少消耗, 负互惠增加消耗
            float reciprocityBonus = (relations.Reciprocity / 10f) * DialogueGoodwillCost.ReciprocityModifier;
            modifier -= reciprocityBonus;
            
            // 尊重values修正: 高尊重减少消耗
            float respectBonus = (relations.Respect / 10f) * DialogueGoodwillCost.RespectModifier;
            modifier -= respectBonus;
            
            // 影响values修正: 高影响减少消耗
            float influenceBonus = (relations.Influence / 10f) * DialogueGoodwillCost.InfluenceModifier;
            modifier -= influenceBonus;
            
            // 限制修正范围 (防止过度修正)
            // 最低0.3 (最多减少70%消耗) , 最高2.0 (最多增加100%消耗)
            modifier = Math.Max(0.3f, Math.Min(2.0f, modifier));
            
            return modifier;
        }

        /// <summary>/// get修正系数的详细分解
 ///</summary>
        private static string GetModifierBreakdown(FactionRelationValues relations, bool isCostAction)
        {
            var parts = new System.Collections.Generic.List<string>();
            
            float trustEffect = -(relations.Trust / 10f) * DialogueGoodwillCost.TrustModifier * 100;
            float intimacyEffect = -(relations.Intimacy / 10f) * DialogueGoodwillCost.IntimacyModifier * 100;
            float reciprocityEffect = -(relations.Reciprocity / 10f) * DialogueGoodwillCost.ReciprocityModifier * 100;
            float respectEffect = -(relations.Respect / 10f) * DialogueGoodwillCost.RespectModifier * 100;
            float influenceEffect = -(relations.Influence / 10f) * DialogueGoodwillCost.InfluenceModifier * 100;
            
            if (Math.Abs(trustEffect) >= 1) parts.Add($"信任:{trustEffect:F0}%");
            if (Math.Abs(intimacyEffect) >= 1) parts.Add($"亲密:{intimacyEffect:F0}%");
            if (Math.Abs(reciprocityEffect) >= 1) parts.Add($"互惠:{reciprocityEffect:F0}%");
            if (Math.Abs(respectEffect) >= 1) parts.Add($"尊重:{respectEffect:F0}%");
            if (Math.Abs(influenceEffect) >= 1) parts.Add($"影响:{influenceEffect:F0}%");
            
            return parts.Count > 0 ? string.Join(", ", parts) : "无修正";
        }

        /// <summary>/// 检查behaviorwhether可执行 (based onrelation阈values)
 ///</summary>
        public static bool CanExecuteAction(
            DialogueGoodwillCost.DialogueActionType actionType, 
            FactionRelationValues relations,
            out string reason)
        {
            reason = "";
            
            switch (actionType)
            {
                case DialogueGoodwillCost.DialogueActionType.RequestCaravan:
                    if (relations.Trust < -20)
                    {
                        reason = "信任值过低（需要≥-20）";
                        return false;
                    }
                    break;
                    
                case DialogueGoodwillCost.DialogueActionType.RequestMilitaryAid:
                    if (relations.Trust < 20)
                    {
                        reason = "信任值不足（需要≥20）";
                        return false;
                    }
                    if (relations.Intimacy < 0)
                    {
                        reason = "亲密度不足（需要≥0）";
                        return false;
                    }
                    break;
                    
                case DialogueGoodwillCost.DialogueActionType.RequestMedicalAid:
                    if (relations.Trust < 0)
                    {
                        reason = "信任值不足（需要≥0）";
                        return false;
                    }
                    break;
                    
                case DialogueGoodwillCost.DialogueActionType.RequestResourceAid:
                    if (relations.Trust < 10)
                    {
                        reason = "信任值不足（需要≥10）";
                        return false;
                    }
                    if (relations.Reciprocity < -30)
                    {
                        reason = "互惠值过低（你欠对方太多人情）";
                        return false;
                    }
                    break;
                    
                case DialogueGoodwillCost.DialogueActionType.DemandLeave:
                    if (relations.Respect < 20 && relations.Influence < 20)
                    {
                        reason = "需要尊重≥20或影响≥20才能提出此要求";
                        return false;
                    }
                    break;
                    
                case DialogueGoodwillCost.DialogueActionType.DemandPayment:
                    if (relations.Respect < 30)
                    {
                        reason = "尊重值不足（需要≥30）";
                        return false;
                    }
                    break;
                    
                case DialogueGoodwillCost.DialogueActionType.ShareIntel:
                    // 总是允许
                    break;
                    
                case DialogueGoodwillCost.DialogueActionType.SendGift:
                    // 总是允许
                    break;
                    
                case DialogueGoodwillCost.DialogueActionType.Threaten:
                    if (relations.Respect > 40)
                    {
                        reason = "对方过于尊重你，威胁会降低尊重值";
                        return true; // 仍然允许, 但会有额外后果
                    }
                    break;
            }
            
            return true;
        }

        /// <summary>/// getbehavior的预期goodwill变化描述
 ///</summary>
        public static string GetExpectedCostDescription(
            DialogueGoodwillCost.DialogueActionType actionType, 
            FactionRelationValues relations)
        {
            int cost = CalculateCost(actionType, relations, out var info);
            string actionName = DialogueGoodwillCost.GetActionLabel(actionType);
            
            if (cost < 0)
            {
                return $"{actionName}: 预计消耗 {cost} 好感度 ({info.ModifierBreakdown})";
            }
            else if (cost > 0)
            {
                return $"{actionName}: 预计增加 +{cost} 好感度 ({info.ModifierBreakdown})";
            }
            else
            {
                return $"{actionName}: 无好感度变化";
            }
        }

        /// <summary>/// 计算relationvalues对消耗的减免百分比
 ///</summary>
        public static float CalculateDiscountPercentage(FactionRelationValues relations)
        {
            float modifier = CalculateRelationModifier(relations, true);
            float discount = (1.0f - modifier) * 100f;
            return Math.Max(0, discount); // 只返回减免, 不返回增加
        }
    }

    /// <summary>/// 消耗计算详细信息
 ///</summary>
    public class CostCalculationInfo
    {
        /// <summary>/// 基础values
 ///</summary>
        public int BaseValue { get; set; }
        
        /// <summary>/// relationvalues修正系数
 ///</summary>
        public float RelationModifier { get; set; }
        
        /// <summary>/// 最终values
 ///</summary>
        public int FinalValue { get; set; }
        
        /// <summary>/// 修正分解说明
 ///</summary>
        public string ModifierBreakdown { get; set; }
        
        /// <summary>/// get详细描述
 ///</summary>
        public string GetDetailedDescription()
        {
            return $"基础值: {BaseValue}, 修正系数: {RelationModifier:F2}, 最终值: {FinalValue}\n修正详情: {ModifierBreakdown}";
        }
    }
}
