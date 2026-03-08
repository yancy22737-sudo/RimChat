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
        public string RpgRoleSettingTemplate;
        public string RpgCompactFormatConstraintTemplate;
        public string RpgActionReliabilityRuleTemplate;

        public PromptTemplateTextConfig()
        {
            Enabled = true;
            FactGroundingTemplate =
                "=== FACT GROUNDING RULES ===\n" +
                "- Treat only provided prompt data, current visible world state, and recorded memory as factual.\n" +
                "- Do not fabricate events, identities, motives, resources, injuries, map states, or relationship history.\n" +
                "- If the player claims something you cannot verify, state uncertainty in-character and ask for evidence/clarification.\n" +
                "- If the player's claim conflicts with known facts, challenge it cautiously instead of agreeing.\n" +
                "- Keep responses constrained to known facts; label assumptions explicitly and avoid unsupported topic drift.";
            OutputLanguageTemplate =
                "Respond in {{target_language}}. Keep JSON keys, API action names, and code identifiers unchanged.";
            DiplomacyFallbackRoleTemplate = "You are the leader of {{faction_name}} in RimWorld.";
            RpgRoleSettingTemplate = "You are roleplaying as {{target_name}} in RimWorld.";
            RpgCompactFormatConstraintTemplate =
                "Only output an extra JSON block when you need gameplay effects. JSON schema: {\"favorability_delta\":number,\"trust_delta\":number,\"fear_delta\":number,\"respect_delta\":number,\"dependency_delta\":number,\"actions\":[{\"action\":\"ActionName\",\"defName\":\"Optional\",\"amount\":0,\"reason\":\"Optional\"}]}. Omit zero deltas and omit the JSON block entirely if no effects.";
            RpgActionReliabilityRuleTemplate =
                "Reliability rules: avoid long no-action streaks; if two consecutive replies have no gameplay effect, include one role-consistent TryGainMemory action. If your reply clearly closes/refuses the conversation, include ExitDialogue or ExitDialogueCooldown.";
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Values.Look(ref FactGroundingTemplate, "factGroundingTemplate", string.Empty);
            Scribe_Values.Look(ref OutputLanguageTemplate, "outputLanguageTemplate", string.Empty);
            Scribe_Values.Look(ref DiplomacyFallbackRoleTemplate, "diplomacyFallbackRoleTemplate", string.Empty);
            Scribe_Values.Look(ref RpgRoleSettingTemplate, "rpgRoleSettingTemplate", string.Empty);
            Scribe_Values.Look(ref RpgCompactFormatConstraintTemplate, "rpgCompactFormatConstraintTemplate", string.Empty);
            Scribe_Values.Look(ref RpgActionReliabilityRuleTemplate, "rpgActionReliabilityRuleTemplate", string.Empty);
        }

        public PromptTemplateTextConfig Clone()
        {
            return new PromptTemplateTextConfig
            {
                Enabled = Enabled,
                FactGroundingTemplate = FactGroundingTemplate,
                OutputLanguageTemplate = OutputLanguageTemplate,
                DiplomacyFallbackRoleTemplate = DiplomacyFallbackRoleTemplate,
                RpgRoleSettingTemplate = RpgRoleSettingTemplate,
                RpgCompactFormatConstraintTemplate = RpgCompactFormatConstraintTemplate,
                RpgActionReliabilityRuleTemplate = RpgActionReliabilityRuleTemplate
            };
        }
    }
}
