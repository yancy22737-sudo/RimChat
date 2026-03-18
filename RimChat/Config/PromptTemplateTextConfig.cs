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
        public string SocialCircleNewsStyleTemplate;
        public string SocialCircleNewsJsonContractTemplate;
        public string SocialCircleNewsFactTemplate;
        public string RpgRoleSettingTemplate;
        public string RpgCompactFormatConstraintTemplate;
        public string RpgActionReliabilityRuleTemplate;
        public string DecisionPolicyTemplate;
        public string TurnObjectiveTemplate;
        public string OpeningObjectiveTemplate;
        public string TopicShiftRuleTemplate;
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
            SocialCircleNewsStyleTemplate = string.Empty;
            SocialCircleNewsJsonContractTemplate = string.Empty;
            SocialCircleNewsFactTemplate = string.Empty;
            RpgRoleSettingTemplate = string.Empty;
            RpgCompactFormatConstraintTemplate = string.Empty;
            RpgActionReliabilityRuleTemplate = string.Empty;
            DecisionPolicyTemplate = string.Empty;
            TurnObjectiveTemplate = string.Empty;
            OpeningObjectiveTemplate = string.Empty;
            TopicShiftRuleTemplate = string.Empty;
            ApiLimitsNodeTemplate = PromptTextConstants.ApiLimitsNodeLiteralDefault;
            QuestGuidanceNodeTemplate = PromptTextConstants.QuestGuidanceNodeLiteralDefault;
            ResponseContractNodeTemplate = PromptTextConstants.ResponseContractNodeLiteralDefault;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Values.Look(ref FactGroundingTemplate, "factGroundingTemplate", string.Empty);
            Scribe_Values.Look(ref OutputLanguageTemplate, "outputLanguageTemplate", string.Empty);
            Scribe_Values.Look(ref DiplomacyFallbackRoleTemplate, "diplomacyFallbackRoleTemplate", string.Empty);
            Scribe_Values.Look(ref SocialCircleActionRuleTemplate, "socialCircleActionRuleTemplate", string.Empty);
            Scribe_Values.Look(ref SocialCircleNewsStyleTemplate, "socialCircleNewsStyleTemplate", string.Empty);
            Scribe_Values.Look(ref SocialCircleNewsJsonContractTemplate, "socialCircleNewsJsonContractTemplate", string.Empty);
            Scribe_Values.Look(ref SocialCircleNewsFactTemplate, "socialCircleNewsFactTemplate", string.Empty);
            Scribe_Values.Look(ref RpgRoleSettingTemplate, "rpgRoleSettingTemplate", string.Empty);
            Scribe_Values.Look(ref RpgCompactFormatConstraintTemplate, "rpgCompactFormatConstraintTemplate", string.Empty);
            Scribe_Values.Look(ref RpgActionReliabilityRuleTemplate, "rpgActionReliabilityRuleTemplate", string.Empty);
            Scribe_Values.Look(ref DecisionPolicyTemplate, "decisionPolicyTemplate", string.Empty);
            Scribe_Values.Look(ref TurnObjectiveTemplate, "turnObjectiveTemplate", string.Empty);
            Scribe_Values.Look(ref OpeningObjectiveTemplate, "openingObjectiveTemplate", string.Empty);
            Scribe_Values.Look(ref TopicShiftRuleTemplate, "topicShiftRuleTemplate", string.Empty);
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
                SocialCircleNewsStyleTemplate = SocialCircleNewsStyleTemplate,
                SocialCircleNewsJsonContractTemplate = SocialCircleNewsJsonContractTemplate,
                SocialCircleNewsFactTemplate = SocialCircleNewsFactTemplate,
                RpgRoleSettingTemplate = RpgRoleSettingTemplate,
                RpgCompactFormatConstraintTemplate = RpgCompactFormatConstraintTemplate,
                RpgActionReliabilityRuleTemplate = RpgActionReliabilityRuleTemplate,
                DecisionPolicyTemplate = DecisionPolicyTemplate,
                TurnObjectiveTemplate = TurnObjectiveTemplate,
                OpeningObjectiveTemplate = OpeningObjectiveTemplate,
                TopicShiftRuleTemplate = TopicShiftRuleTemplate,
                ApiLimitsNodeTemplate = ApiLimitsNodeTemplate,
                QuestGuidanceNodeTemplate = QuestGuidanceNodeTemplate,
                ResponseContractNodeTemplate = ResponseContractNodeTemplate
            };
        }
    }
}
