using System;
using System.Collections.Generic;
using RimChat.Config;

namespace RimChat.Persistence
{
    /// <summary>
    /// Dependencies: prompt config DTOs in RimChat.Config.
    /// Responsibility: define disk payloads for split prompt-domain storage and bundle transport.
    /// </summary>
    [Serializable]
    internal sealed class SystemPromptDomainConfig
    {
        public string ConfigName = "Default";
        public string GlobalSystemPrompt = string.Empty;
        public bool UseAdvancedMode;
        public bool UseHierarchicalPromptFormat = true;
        public bool Enabled = true;
        public int PromptPolicySchemaVersion = SystemPromptConfig.CurrentPromptPolicySchemaVersion;
        public EnvironmentPromptConfig EnvironmentPrompt = new EnvironmentPromptConfig();
        public DynamicDataInjectionConfig DynamicDataInjection = new DynamicDataInjectionConfig();
        public PromptPolicyConfig PromptPolicy = PromptPolicyConfig.CreateDefault();
    }

    [Serializable]
    internal sealed class DiplomacyDialoguePromptDomainConfig
    {
        public string GlobalDialoguePrompt = string.Empty;
        public bool PromptTemplatesEnabled = true;
        public ResponseFormatConfig ResponseFormat = new ResponseFormatConfig();
        public List<DecisionRuleConfig> DecisionRules = new List<DecisionRuleConfig>();
        public List<ApiActionConfig> ApiActions = new List<ApiActionConfig>();
        public string FactGroundingTemplate = string.Empty;
        public string OutputLanguageTemplate = string.Empty;
        public string DiplomacyFallbackRoleTemplate = string.Empty;
        public string DecisionPolicyTemplate = string.Empty;
        public string TurnObjectiveTemplate = string.Empty;
        public string TopicShiftRuleTemplate = string.Empty;
        public string ApiLimitsNodeTemplate = "{{api_limits_body}}";
        public string QuestGuidanceNodeTemplate = "{{quest_guidance_body}}";
        public string ResponseContractNodeTemplate = "{{response_contract_body}}";
    }

    [Serializable]
    internal sealed class SocialCirclePromptDomainConfig
    {
        public string SocialCircleActionRuleTemplate = string.Empty;
        public string SocialCircleNewsStyleTemplate = string.Empty;
        public string SocialCircleNewsJsonContractTemplate = string.Empty;
        public string SocialCircleNewsFactTemplate = string.Empty;
        public ApiActionConfig PublishPublicPostAction = new ApiActionConfig(
            "publish_public_post",
            string.Empty,
            string.Empty,
            string.Empty);
    }

    [Serializable]
    internal sealed class PromptBundleConfig
    {
        public int BundleVersion = 1;
        public SystemPromptDomainConfig SystemPrompt = new SystemPromptDomainConfig();
        public DiplomacyDialoguePromptDomainConfig DiplomacyDialoguePrompt = new DiplomacyDialoguePromptDomainConfig();
        public RpgPromptCustomConfig PawnDialoguePrompt = new RpgPromptCustomConfig();
        public SocialCirclePromptDomainConfig SocialCirclePrompt = new SocialCirclePromptDomainConfig();
        public string FactionPromptsJson = string.Empty;
    }
}
