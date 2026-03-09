using System;
using Verse;

namespace RimChat.Config
{
    /// <summary>/// Dependencies: Verse.Scribe for config persistence.
 /// Responsibility: store reusable prompt text templates for shared prompt sections.
 ///</summary>
    [Serializable]
    public class PromptTemplateTextConfig : IExposable
    {
        public bool Enabled;
        public string FactGroundingTemplate;
        public string OutputLanguageTemplate;
        public string DiplomacyFallbackRoleTemplate;
        public string SocialCircleActionRuleTemplate;
        public string RpgRoleSettingTemplate;
        public string RpgCompactFormatConstraintTemplate;
        public string RpgActionReliabilityRuleTemplate;
        public string ApiLimitsNodeTemplate;
        public string QuestGuidanceNodeTemplate;
        public string ResponseContractNodeTemplate;

        public PromptTemplateTextConfig()
        {
            Enabled = true;
            FactGroundingTemplate = string.Empty;
            OutputLanguageTemplate = string.Empty;
            DiplomacyFallbackRoleTemplate = string.Empty;
            SocialCircleActionRuleTemplate = string.Empty;
            RpgRoleSettingTemplate = string.Empty;
            RpgCompactFormatConstraintTemplate = string.Empty;
            RpgActionReliabilityRuleTemplate = string.Empty;
            ApiLimitsNodeTemplate = "{{api_limits_body}}";
            QuestGuidanceNodeTemplate = "{{quest_guidance_body}}";
            ResponseContractNodeTemplate = "{{response_contract_body}}";
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Values.Look(ref FactGroundingTemplate, "factGroundingTemplate", string.Empty);
            Scribe_Values.Look(ref OutputLanguageTemplate, "outputLanguageTemplate", string.Empty);
            Scribe_Values.Look(ref DiplomacyFallbackRoleTemplate, "diplomacyFallbackRoleTemplate", string.Empty);
            Scribe_Values.Look(ref SocialCircleActionRuleTemplate, "socialCircleActionRuleTemplate", string.Empty);
            Scribe_Values.Look(ref RpgRoleSettingTemplate, "rpgRoleSettingTemplate", string.Empty);
            Scribe_Values.Look(ref RpgCompactFormatConstraintTemplate, "rpgCompactFormatConstraintTemplate", string.Empty);
            Scribe_Values.Look(ref RpgActionReliabilityRuleTemplate, "rpgActionReliabilityRuleTemplate", string.Empty);
            Scribe_Values.Look(ref ApiLimitsNodeTemplate, "apiLimitsNodeTemplate", string.Empty);
            Scribe_Values.Look(ref QuestGuidanceNodeTemplate, "questGuidanceNodeTemplate", string.Empty);
            Scribe_Values.Look(ref ResponseContractNodeTemplate, "responseContractNodeTemplate", string.Empty);
        }

        public PromptTemplateTextConfig Clone()
        {
            return new PromptTemplateTextConfig
            {
                Enabled = Enabled,
                FactGroundingTemplate = FactGroundingTemplate,
                OutputLanguageTemplate = OutputLanguageTemplate,
                DiplomacyFallbackRoleTemplate = DiplomacyFallbackRoleTemplate,
                SocialCircleActionRuleTemplate = SocialCircleActionRuleTemplate,
                RpgRoleSettingTemplate = RpgRoleSettingTemplate,
                RpgCompactFormatConstraintTemplate = RpgCompactFormatConstraintTemplate,
                RpgActionReliabilityRuleTemplate = RpgActionReliabilityRuleTemplate,
                ApiLimitsNodeTemplate = ApiLimitsNodeTemplate,
                QuestGuidanceNodeTemplate = QuestGuidanceNodeTemplate,
                ResponseContractNodeTemplate = ResponseContractNodeTemplate
            };
        }
    }
}
