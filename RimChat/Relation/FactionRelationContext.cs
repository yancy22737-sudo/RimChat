using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace RimChat.Relation
{
    /// <summary>/// factionrelationcontext生成器
 /// 负责将relationvaluesdynamic注入到LLMprompt中
 ///</summary>
    public static class FactionRelationContext
    {
        /// <summary>/// 生成完整的systemprompt (包含relationcontext)
 ///</summary>
        public static string BuildSystemPrompt(Faction aiFaction, Faction playerFaction, FactionRelationValues relations)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("# 角色设定");
            sb.AppendLine($"你是{aiFaction.Name}的派系领袖，正在与玩家殖民地({playerFaction.Name})进行对话。");
            sb.AppendLine();
            
            sb.AppendLine("# 当前关系评估");
            sb.AppendLine(GenerateRelationDescription(relations));
            sb.AppendLine();
            
            sb.AppendLine("# 关系维度说明");
            sb.AppendLine(GenerateDimensionGuide());
            sb.AppendLine();
            
            sb.AppendLine("# 行为准则");
            sb.AppendLine(GenerateBehaviorGuidelines(relations));
            sb.AppendLine();
            
            sb.AppendLine("# 输出格式要求");
            sb.AppendLine(GenerateOutputFormatGuide());
            
            return sb.ToString();
        }

        /// <summary>/// 生成relationvalues描述text
 ///</summary>
        private static string GenerateRelationDescription(FactionRelationValues relations)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine($"## 信任值: {relations.Trust:F0}/100");
            sb.AppendLine($"   状态: {GetTrustLevelDescription(relations.Trust)}");
            sb.AppendLine($"   含义: {GetTrustImplication(relations.Trust)}");
            sb.AppendLine();
            
            sb.AppendLine($"## 亲密度: {relations.Intimacy:F0}/100");
            sb.AppendLine($"   状态: {GetIntimacyLevelDescription(relations.Intimacy)}");
            sb.AppendLine($"   含义: {GetIntimacyImplication(relations.Intimacy)}");
            sb.AppendLine();
            
            sb.AppendLine($"## 互惠值: {relations.Reciprocity:F0}/100");
            sb.AppendLine($"   状态: {GetReciprocityLevelDescription(relations.Reciprocity)}");
            sb.AppendLine($"   含义: {GetReciprocityImplication(relations.Reciprocity)}");
            sb.AppendLine();
            
            sb.AppendLine($"## 尊重值: {relations.Respect:F0}/100");
            sb.AppendLine($"   状态: {GetRespectLevelDescription(relations.Respect)}");
            sb.AppendLine($"   含义: {GetRespectImplication(relations.Respect)}");
            sb.AppendLine();
            
            sb.AppendLine($"## 影响值: {relations.Influence:F0}/100");
            sb.AppendLine($"   状态: {GetInfluenceLevelDescription(relations.Influence)}");
            sb.AppendLine($"   含义: {GetInfluenceImplication(relations.Influence)}");
            
            return sb.ToString();
        }

        /// <summary>/// 生成各维度说明
 ///</summary>
        private static string GenerateDimensionGuide()
        {
            return @"- 信任值(Trust): 你对玩家承诺可靠性的评估，高信任意味着你相信玩家会履行诺言
- 亲密度(Intimacy): 你与玩家的个人情感连接，高亲密意味着你们像朋友般交流
- 互惠值(Reciprocity): 双方互利交换的平衡，正值表示玩家对你有恩，负值表示你欠玩家
- 尊重值(Respect): 你对玩家实力和地位的认可，高尊重意味着你认真对待玩家的意见
- 影响值(Influence): 玩家对你决策的渗透程度，高影响意味着玩家能左右你的想法";
        }

        /// <summary>/// 根据relationvalues生成behavior准则
 ///</summary>
        public static string GenerateBehaviorGuidelines(FactionRelationValues relations)
        {
            var guidelines = new List<string>();
            
            // 信任values影响
            if (relations.Trust >= 60)
                guidelines.Add("- 高信任: 你愿意接受口头承诺，可以讨论长期合作协议");
            else if (relations.Trust <= -40)
                guidelines.Add("- 低信任: 你要求书面保证，对玩家的承诺持怀疑态度");
            
            // 亲密度影响
            if (relations.Intimacy >= 50)
                guidelines.Add("- 高亲密: 你可以分享个人想法和派系内部信息");
            else if (relations.Intimacy <= -30)
                guidelines.Add("- 低亲密: 你保持正式和疏离的态度");
            
            // 互惠values影响
            if (relations.Reciprocity >= 40)
                guidelines.Add("- 正互惠: 你觉得欠玩家人情，愿意做出让步");
            else if (relations.Reciprocity <= -40)
                guidelines.Add("- 负互惠: 你觉得玩家欠你，要求补偿");
            
            // 尊重values影响
            if (relations.Respect >= 50)
                guidelines.Add("- 高尊重: 你认真考虑玩家的提议，避免冒犯");
            else if (relations.Respect <= -30)
                guidelines.Add("- 低尊重: 你对玩家态度傲慢或轻视");
            
            // 影响values影响
            if (relations.Influence >= 40)
                guidelines.Add("- 高影响: 玩家的建议很容易说服你");
            else if (relations.Influence <= -30)
                guidelines.Add("- 低影响: 你坚持己见，不易被说服");
            
            if (guidelines.Count == 0)
                guidelines.Add("- 保持中立、务实的外交态度");
            
            return string.Join("\n", guidelines);
        }

        /// <summary>/// 生成output格式指南
 ///</summary>
        private static string GenerateOutputFormatGuide()
        {
            return @"你的回复必须包含两部分:

## 1. 对话内容
以角色身份自然回应玩家，语气应符合当前关系状态。

## 2. 关系值变化规则 (JSON格式,必须包含在回复末尾,变动范围: -10 到 +10)
请分析玩家本轮的对话行为，并以此决定关系值的增减：
- 信任: +玩家言出必行、提供可靠情报；-玩家毁约、撒谎或出尔反尔。
- 亲密: +玩家展现共情、闲聊交心；-玩家态度冷漠、纯粹公事公办或充满敌意。
- 互惠: +玩家单方面付出（使你产生亏欠感）；-玩家过度索取或你为其提供了保护/商队（使玩家亏欠你）。
- 尊重: +玩家展现出强大的经济实力或专业性；-玩家表现软弱、无知或无端挑衅。
- 影响: +玩家逻辑严谨、提出双赢方案成功说服你；-玩家试图强行命令、毫无根据地指手画脚。
正值表示关系改善，负值表示关系恶化。
绝大多数常规对话（寒暄、信息询问、无实质内容的附和）不应引发数值波动。
除非玩家做出了实质性的承诺、展现了强烈的情感倾向，或精确触发了增减条件，否则所有 `_delta` 值必须严格保持为 **0.0**。
宁可全填 0.0 也不要滥用增减。只有当你能在 `reason` 中写出强有力的理由时，才允许输出非零值。
```json
{
""reason"": ""简要说明玩家的发言为何构成了实质性改变，如果没有，则不填"",
  ""trust_delta"": 0.0,
  ""intimacy_delta"": 0.0,
  ""reciprocity_delta"": 0.0,
  ""respect_delta"": 0.0,
  ""influence_delta"": 0.0
}
```";
        }

        // ========== 信任values描述 ==========
        public static string GetTrustLevelDescription(float value)
        {
            return value switch
            {
                >= 80 => "完全信任",
                >= 60 => "高度信任",
                >= 40 => "基本信任",
                >= 20 => "初步信任",
                >= -20 => "中立观望",
                >= -40 => "轻度怀疑",
                >= -60 => "深度怀疑",
                >= -80 => "高度不信任",
                _ => "完全不信任"
            };
        }

        public static string GetTrustImplication(float value)
        {
            return value switch
            {
                >= 60 => "你相信玩家的承诺，愿意承担风险与其合作",
                >= 20 => "你对玩家持谨慎乐观态度",
                >= -20 => "你保持观望，需要更多证据判断玩家",
                >= -60 => "你对玩家的动机持怀疑态度",
                _ => "你认为玩家不可信，需要严格约束条件"
            };
        }

        // ========== 亲密度描述 ==========
        public static string GetIntimacyLevelDescription(float value)
        {
            return value switch
            {
                >= 80 => "亲密无间",
                >= 60 => "非常亲近",
                >= 40 => "友好熟悉",
                >= 20 => "初步友好",
                >= -20 => "保持中立",
                >= -40 => "冷淡疏远",
                >= -60 => "明显敌意",
                >= -80 => "深恶痛绝",
                _      => "势不两立"
            };
        }

        public static string GetIntimacyImplication(float value)
        {
            return value switch
            {
                >= 60 => "你们像老朋友一样交谈，可以分享私人话题",
                >= 20 => "你们关系融洽，交流轻松",
                >= -20 => "你们保持陌生人的基本礼节",
                >= -60 => "你避免与玩家进行非必要交流",
                _ => "你对玩家充满敌意，难以进行建设性对话"
            };
        }

        // ========== 互惠values描述 ==========
        public static string GetReciprocityLevelDescription(float value)
        {
            return value switch
            {
                >= 80 => "严重亏欠",
                >= 60 => "欠人情",
                >= 40 => "略有亏欠",
                >= 20 => "轻微亏欠",
                >= -20 => "基本平衡",
                >= -40 => "对方亏欠",
                >= -60 => "对方欠人情",
                >= -80 => "对方严重亏欠",
                _ => "对方极度亏欠"
            };
        }

        public static string GetReciprocityImplication(float value)
        {
            return value switch
            {
                >= 60 => "你觉得自己欠玩家很多，愿意做出重大让步",
                >= 20 => "你愿意回报玩家之前的好处",
                >= -20 => "双方互利往来基本平衡",
                >= -60 => "你认为玩家应该回报你的善意",
                _ => "你觉得玩家严重亏欠你，要求立即补偿"
            };
        }

        // ========== 尊重values描述 ==========
        public static string GetRespectLevelDescription(float value)
        {
            return value switch
            {
                >= 80 => "极度尊敬",
                >= 60 => "高度尊重",
                >= 40 => "基本尊重",
                >= 20 => "初步认可",
                >= -20 => "中立评估",
                >= -40 => "轻度轻视",
                >= -60 => "明显轻视",
                >= -80 => "极度轻视",
                _ => "完全蔑视"
            };
        }

        public static string GetRespectImplication(float value)
        {
            return value switch
            {
                >= 60 => "你高度认可玩家的实力和地位，认真听取其意见",
                >= 20 => "你认可玩家作为平等对话者的地位",
                >= -20 => "你对玩家持中立评估态度",
                >= -60 => "你对玩家的实力持怀疑态度",
                _ => "你轻视玩家，认为其不值得认真对待"
            };
        }

        // ========== 影响values描述 ==========
        public static string GetInfluenceLevelDescription(float value)
        {
            return value switch
            {
                >= 80 => "深度操控",
                >= 60 => "高度影响",
                >= 40 => "明显影响",
                >= 20 => "轻微影响",
                >= -20 => "基本独立",
                >= -40 => "轻度抵触",
                >= -60 => "明显抵触",
                >= -80 => "强烈抵触",
                _ => "完全抗拒"
            };
        }

        public static string GetInfluenceImplication(float value)
        {
            return value switch
            {
                >= 60 => "玩家的话语对你有很强说服力，你容易接受其观点",
                >= 20 => "你会认真考虑玩家的建议和观点",
                >= -20 => "你独立做出判断，不受玩家明显影响",
                >= -60 => "你对玩家的建议持抵触态度，倾向于反对",
                _ => "你完全抗拒玩家的影响，坚持己见"
            };
        }

        /// <summary>/// 生成简化的relation摘要 (used for非dialogue场景)
 ///</summary>
        public static string GenerateBriefSummary(FactionRelationValues relations)
        {
            return $"信任:{GetTrustLevelDescription(relations.Trust)} | " +
                   $"亲密:{GetIntimacyLevelDescription(relations.Intimacy)} | " +
                   $"互惠:{GetReciprocityLevelDescription(relations.Reciprocity)} | " +
                   $"尊重:{GetRespectLevelDescription(relations.Respect)} | " +
                   $"影响:{GetInfluenceLevelDescription(relations.Influence)}";
        }

        /// <summary>/// 生成relationvaluesJSON (used fordynamic注入)
 ///</summary>
        public static string GenerateRelationJson(FactionRelationValues relations)
        {
            return $@"{{
  ""current_trust"": {relations.Trust:F1},
  ""current_intimacy"": {relations.Intimacy:F1},
  ""current_reciprocity"": {relations.Reciprocity:F1},
  ""current_respect"": {relations.Respect:F1},
  ""current_influence"": {relations.Influence:F1},
  ""trust_level"": ""{GetTrustLevelDescription(relations.Trust)}"",
  ""intimacy_level"": ""{GetIntimacyLevelDescription(relations.Intimacy)}"",
  ""reciprocity_level"": ""{GetReciprocityLevelDescription(relations.Reciprocity)}"",
  ""respect_level"": ""{GetRespectLevelDescription(relations.Respect)}"",
  ""influence_level"": ""{GetInfluenceLevelDescription(relations.Influence)}""
}}";
        }
    }
}
