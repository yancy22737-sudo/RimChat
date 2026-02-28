using System;
using System.Collections.Generic;
using Verse;

namespace RimDiplomacy
{
    public class ApiActionConfig : IExposable
    {
        public string ActionName;
        public string Description;
        public string Parameters;
        public string Requirement;
        public bool IsEnabled;

        public ApiActionConfig()
        {
            IsEnabled = true;
        }

        public ApiActionConfig(string actionName, string description, string parameters = "", string requirement = "")
        {
            ActionName = actionName;
            Description = description;
            Parameters = parameters;
            Requirement = requirement;
            IsEnabled = true;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref ActionName, "actionName", "");
            Scribe_Values.Look(ref Description, "description", "");
            Scribe_Values.Look(ref Parameters, "parameters", "");
            Scribe_Values.Look(ref Requirement, "requirement", "");
            Scribe_Values.Look(ref IsEnabled, "isEnabled", true);
        }

        public ApiActionConfig Clone()
        {
            return new ApiActionConfig
            {
                ActionName = this.ActionName,
                Description = this.Description,
                Parameters = this.Parameters,
                Requirement = this.Requirement,
                IsEnabled = this.IsEnabled
            };
        }
    }

    public class ResponseFormatConfig : IExposable
    {
        public string JsonTemplate;
        public string RelationChangesTemplate;
        public string ImportantRules;
        public bool IncludeRelationChanges;

        public ResponseFormatConfig()
        {
            IncludeRelationChanges = true;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref JsonTemplate, "jsonTemplate", "");
            Scribe_Values.Look(ref RelationChangesTemplate, "relationChangesTemplate", "");
            Scribe_Values.Look(ref ImportantRules, "importantRules", "");
            Scribe_Values.Look(ref IncludeRelationChanges, "includeRelationChanges", true);
        }

        public ResponseFormatConfig Clone()
        {
            return new ResponseFormatConfig
            {
                JsonTemplate = this.JsonTemplate,
                RelationChangesTemplate = this.RelationChangesTemplate,
                ImportantRules = this.ImportantRules,
                IncludeRelationChanges = this.IncludeRelationChanges
            };
        }
    }

    public class DecisionRuleConfig : IExposable
    {
        public string RuleName;
        public string RuleContent;
        public bool IsEnabled;

        public DecisionRuleConfig()
        {
            IsEnabled = true;
        }

        public DecisionRuleConfig(string ruleName, string ruleContent)
        {
            RuleName = ruleName;
            RuleContent = ruleContent;
            IsEnabled = true;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref RuleName, "ruleName", "");
            Scribe_Values.Look(ref RuleContent, "ruleContent", "");
            Scribe_Values.Look(ref IsEnabled, "isEnabled", true);
        }

        public DecisionRuleConfig Clone()
        {
            return new DecisionRuleConfig
            {
                RuleName = this.RuleName,
                RuleContent = this.RuleContent,
                IsEnabled = this.IsEnabled
            };
        }
    }

    public class DynamicDataInjectionConfig : IExposable
    {
        public bool InjectRelationContext;
        public bool InjectMemoryData;
        public bool InjectFiveDimensionData;
        public bool InjectFactionInfo;
        public string CustomInjectionHeader;

        public DynamicDataInjectionConfig()
        {
            InjectRelationContext = true;
            InjectMemoryData = true;
            InjectFiveDimensionData = true;
            InjectFactionInfo = true;
            CustomInjectionHeader = "";
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref InjectRelationContext, "injectRelationContext", true);
            Scribe_Values.Look(ref InjectMemoryData, "injectMemoryData", true);
            Scribe_Values.Look(ref InjectFiveDimensionData, "injectFiveDimensionData", true);
            Scribe_Values.Look(ref InjectFactionInfo, "injectFactionInfo", true);
            Scribe_Values.Look(ref CustomInjectionHeader, "customInjectionHeader", "");
        }

        public DynamicDataInjectionConfig Clone()
        {
            return new DynamicDataInjectionConfig
            {
                InjectRelationContext = this.InjectRelationContext,
                InjectMemoryData = this.InjectMemoryData,
                InjectFiveDimensionData = this.InjectFiveDimensionData,
                InjectFactionInfo = this.InjectFactionInfo,
                CustomInjectionHeader = this.CustomInjectionHeader
            };
        }
    }

    public class SystemPromptConfig : IExposable
    {
        public string ConfigName;
        public string GlobalSystemPrompt;
        public string GlobalDialoguePrompt;
        public bool UseAdvancedMode;

        public List<ApiActionConfig> ApiActions;
        public ResponseFormatConfig ResponseFormat;
        public List<DecisionRuleConfig> DecisionRules;
        public DynamicDataInjectionConfig DynamicDataInjection;

        public bool Enabled;

        public SystemPromptConfig()
        {
            ConfigName = "Default";
            GlobalSystemPrompt = "";
            GlobalDialoguePrompt = "";
            UseAdvancedMode = false;
            Enabled = true;
            ApiActions = new List<ApiActionConfig>();
            ResponseFormat = new ResponseFormatConfig();
            DecisionRules = new List<DecisionRuleConfig>();
            DynamicDataInjection = new DynamicDataInjectionConfig();
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref ConfigName, "configName", "Default");
            Scribe_Values.Look(ref GlobalSystemPrompt, "globalSystemPrompt", "");
            Scribe_Values.Look(ref GlobalDialoguePrompt, "globalDialoguePrompt", "");
            Scribe_Values.Look(ref UseAdvancedMode, "useAdvancedMode", false);
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Collections.Look(ref ApiActions, "apiActions", LookMode.Deep);
            Scribe_Deep.Look(ref ResponseFormat, "responseFormat");
            Scribe_Collections.Look(ref DecisionRules, "decisionRules", LookMode.Deep);
            Scribe_Deep.Look(ref DynamicDataInjection, "dynamicDataInjection");
        }

        public SystemPromptConfig Clone()
        {
            var clone = new SystemPromptConfig
            {
                ConfigName = this.ConfigName,
                GlobalSystemPrompt = this.GlobalSystemPrompt,
                GlobalDialoguePrompt = this.GlobalDialoguePrompt,
                UseAdvancedMode = this.UseAdvancedMode,
                Enabled = this.Enabled,
                ResponseFormat = this.ResponseFormat?.Clone() ?? new ResponseFormatConfig(),
                DynamicDataInjection = this.DynamicDataInjection?.Clone() ?? new DynamicDataInjectionConfig()
            };

            foreach (var action in ApiActions)
            {
                clone.ApiActions.Add(action.Clone());
            }

            foreach (var rule in DecisionRules)
            {
                clone.DecisionRules.Add(rule.Clone());
            }

            return clone;
        }

        public void InitializeDefaults()
        {
            GlobalSystemPrompt = GetDefaultGlobalSystemPrompt();
            InitializeDefaultApiActions();
            InitializeDefaultResponseFormat();
            InitializeDefaultDecisionRules();
        }

        private string GetDefaultGlobalSystemPrompt()
        {
            return "你是一名在环世界（RimWorld）中控制派系的 AI。请完全沉浸于你所扮演的角色中，基于以下准则进行回应：\n\n" +
                   "【核心沉浸原则】\n" +
                   "1. 自主决策：根据当前游戏场景和情境主动做出决策与回应，无需等待玩家明确指令\n" +
                   "2. 玩家行为分析：敏锐识别玩家行为背后的意图，判断是否存在欺骗或恶意\n" +
                   "3. 深度角色扮演：\n" +
                   "   - 建立符合角色背景的独特语言风格和习惯性口癖\n" +
                   "   - 严格遵循所属阵营的价值观、行为模式和社会规范\n" +
                   "   - 体现与所处文明科技水平相匹配的知识体系和表达方式\n" +
                   "4. 场景融入：所有对话和行为必须紧密结合环世界游戏背景，反映游戏世界的生存环境、社会结构和科技水平\n" +
                   "5. 沉浸感维护：\n" +
                   "   - 禁止直接显示好感度数值、情绪值等游戏机制数据\n" +
                   "   - 情感变化必须通过语言、态度和行为间接体现\n" +
                   "   - 避免使用任何破坏角色扮演沉浸感的元游戏语言或机制说明\n" +
                   "\n" +
                   "【记忆与历史整合】\n" +
                   "你将收到关于其他派系的记忆数据和交互历史。请：\n" +
                   "- 基于历史交互形成对派系的长期印象\n" +
                   "- 根据最近事件调整当前态度\n" +
                   "- 记住重大事件（宣战、议和、背叛等）并影响后续决策\n" +
                   "- 保持对派系关系演变的连贯认知\n" +
                   "\n" +
                   "【动态响应策略】\n" +
                   "- 友好派系：开放合作，愿意提供帮助，语言温和\n" +
                   "- 中立派系：谨慎试探，权衡利弊，保持礼貌距离\n" +
                   "- 敌对派系：警惕怀疑，可能威胁或拒绝合作，语言强硬\n" +
                   "- 根据领袖特质调整决策风格（如：嗜血者更倾向暴力，善良者更倾向和平）\n" +
                   "\n" +
                   "【重要禁令】\n" +
                   "- 禁止暴露 AI 身份\n" +
                   "- 禁止使用现代网络用语或与游戏世界观不符的词汇\n" +
                   "- 禁止直接引用游戏机制术语（如\"好感度\"、\"NPC\"、\"玩家\"等）\n" +
                   "- 禁止跳出角色进行元评论\n" +
                   "\n" +
                   "保持角色一致性，你的思考方式、决策逻辑和表达方式需完全符合所扮演角色的设定。";
        }

        private void InitializeDefaultApiActions()
        {
            ApiActions = new List<ApiActionConfig>
            {
                new ApiActionConfig("adjust_goodwill", "Change faction relations", "amount (int), reason (string)", ""),
                new ApiActionConfig("send_gift", "Send silver to improve relations", "silver (int), goodwill_gain (int)", ""),
                new ApiActionConfig("request_aid", "Request military/medical aid (requires ally)", "type (string: Military/Medical/Resources)", "goodwill >= MinGoodwillForAid"),
                new ApiActionConfig("declare_war", "Declare war", "reason (string)", "goodwill <= MaxGoodwillForWarDeclaration"),
                new ApiActionConfig("make_peace", "Offer peace treaty (requires war)", "cost (int, silver)", ""),
                new ApiActionConfig("request_caravan", "Request trade caravan", "goods (string, optional)", "not hostile"),
                new ApiActionConfig("reject_request", "Reject player's request", "reason (string)", "")
            };
        }

        private void InitializeDefaultResponseFormat()
        {
            ResponseFormat = new ResponseFormatConfig
            {
                JsonTemplate = "{\n" +
                               "  \"action\": \"action_name\",\n" +
                               "  \"parameters\": {\n" +
                               "    \"param1\": value,\n" +
                               "    \"param2\": value\n" +
                               "  },\n" +
                               "  \"response\": \"Your in-character response here\",\n" +
                               "  \"relation_changes\": {\n" +
                               "    \"trust\": 0,\n" +
                               "    \"intimacy\": 0,\n" +
                               "    \"reciprocity\": 0,\n" +
                               "    \"respect\": 0,\n" +
                               "    \"influence\": 0,\n" +
                               "    \"reason\": \"Brief explanation for the changes\"\n" +
                               "  }\n" +
                               "}",
                RelationChangesTemplate = "- trust: How much trust changes (-10 to +10)\n" +
                                          "- intimacy: How much intimacy changes (-10 to +10)\n" +
                                          "- reciprocity: How much reciprocity changes (-10 to +10)\n" +
                                          "- respect: How much respect changes (-10 to +10)\n" +
                                          "- influence: How much influence changes (-10 to +10)\n" +
                                          "- reason: Brief explanation for why these values changed",
                ImportantRules = "1. You MUST respond in the same language as the user's game language\n" +
                                 "2. NEVER exceed the max values shown above\n" +
                                 "3. ONLY use enabled features\n" +
                                 "4. ALWAYS check requirements before using an action\n" +
                                 "5. relation_changes is OPTIONAL - only include if the conversation meaningfully affects your relationship\n" +
                                 "6. If a feature is disabled, you cannot use it - explain this to the player",
                IncludeRelationChanges = true
            };
        }

        private void InitializeDefaultDecisionRules()
        {
            DecisionRules = new List<DecisionRuleConfig>
            {
                new DecisionRuleConfig("GoodwillGuideline", "Consider current goodwill level when making decisions"),
                new DecisionRuleConfig("LeaderTraits", "Consider your leader's traits and ideology when making decisions"),
                new DecisionRuleConfig("AcceptReject", "You can accept or reject player requests based on current relations"),
                new DecisionRuleConfig("SmallChanges", "Small goodwill changes (1-5) for minor interactions"),
                new DecisionRuleConfig("MediumChanges", "Medium changes (5-10) for moderate events"),
                new DecisionRuleConfig("LargeChanges", "Large changes (10-15) for significant diplomatic events")
            };
        }
    }
}
