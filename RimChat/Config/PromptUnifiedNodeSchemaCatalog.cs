using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: prompt channel catalog.
    /// Responsibility: define editable unified prompt node ids for workbench/runtime lookups.
    /// </summary>
    internal static class PromptUnifiedNodeSchemaCatalog
    {
        private static readonly PromptUnifiedNodeSchemaItem[] NodeItems =
        {
            new PromptUnifiedNodeSchemaItem("fact_grounding", "RimChat_PromptNode_FactGrounding", "Fact Grounding"),
            new PromptUnifiedNodeSchemaItem("output_language", "RimChat_PromptNode_OutputLanguage", "Output Language"),
            new PromptUnifiedNodeSchemaItem("decision_policy", "RimChat_PromptNode_DecisionPolicy", "Decision Policy"),
            new PromptUnifiedNodeSchemaItem("turn_objective", "RimChat_PromptNode_TurnObjective", "Turn Objective"),
            new PromptUnifiedNodeSchemaItem("opening_objective", "RimChat_PromptNode_OpeningObjective", "Opening Objective"),
            new PromptUnifiedNodeSchemaItem("topic_shift_rule", "RimChat_PromptNode_TopicShiftRule", "Topic Shift Rule"),
            new PromptUnifiedNodeSchemaItem("diplomacy_fallback_role", "RimChat_PromptNode_DiplomacyRoleFallback", "Diplomacy Role Fallback"),
            new PromptUnifiedNodeSchemaItem("social_circle_action_rule", "RimChat_PromptNode_SocialActionRule", "Social Action Rule"),
            new PromptUnifiedNodeSchemaItem("api_limits_node_template", "RimChat_PromptNode_ApiLimitsNode", "API Limits Node"),
            new PromptUnifiedNodeSchemaItem("quest_guidance_node_template", "RimChat_PromptNode_QuestGuidanceNode", "Quest Guidance Node"),
            new PromptUnifiedNodeSchemaItem("response_contract_node_template", "RimChat_PromptNode_ResponseContractNode", "Response Contract Node"),
            new PromptUnifiedNodeSchemaItem("strategy_output_contract", "RimChat_PromptNode_StrategyOutputContract", "Strategy Output Contract"),
            new PromptUnifiedNodeSchemaItem("strategy_player_negotiator_context_template", "RimChat_PromptNode_StrategyNegotiatorContext", "Strategy Negotiator Context"),
            new PromptUnifiedNodeSchemaItem("strategy_fact_pack_template", "RimChat_PromptNode_StrategyFactPack", "Strategy Fact Pack"),
            new PromptUnifiedNodeSchemaItem("strategy_scenario_dossier_template", "RimChat_PromptNode_StrategyScenarioDossier", "Strategy Scenario Dossier"),
            new PromptUnifiedNodeSchemaItem("social_news_style", "RimChat_PromptNode_SocialNewsStyle", "Social News Style"),
            new PromptUnifiedNodeSchemaItem("social_news_json_contract", "RimChat_PromptNode_SocialNewsJsonContract", "Social News JSON Contract"),
            new PromptUnifiedNodeSchemaItem("social_news_fact", "RimChat_PromptNode_SocialNewsFact", "Social News Fact"),
            new PromptUnifiedNodeSchemaItem("rpg_role_setting_fallback", "RimChat_PromptNode_RpgRoleFallback", "RPG Role Fallback"),
            new PromptUnifiedNodeSchemaItem("rpg_relationship_profile", "RimChat_PromptNode_RpgRelationshipProfile", "RPG Relationship Profile"),
            new PromptUnifiedNodeSchemaItem("rpg_kinship_boundary", "RimChat_PromptNode_RpgKinshipBoundary", "RPG Kinship Boundary")
        };

        internal static IReadOnlyList<PromptUnifiedNodeSchemaItem> GetAll()
        {
            return NodeItems;
        }

        internal static bool TryGet(string nodeId, out PromptUnifiedNodeSchemaItem item)
        {
            string normalized = NormalizeId(nodeId);
            item = NodeItems.FirstOrDefault(i => string.Equals(i.Id, normalized, StringComparison.OrdinalIgnoreCase));
            return !string.IsNullOrWhiteSpace(item.Id);
        }

        internal static string NormalizeId(string nodeId)
        {
            return string.IsNullOrWhiteSpace(nodeId)
                ? string.Empty
                : nodeId.Trim().ToLowerInvariant();
        }

        internal static string GetDisplayLabel(string nodeId)
        {
            if (!TryGet(nodeId, out PromptUnifiedNodeSchemaItem item))
            {
                return NormalizeId(nodeId);
            }

            if (LanguageDatabase.activeLanguage != null &&
                Translator.TryTranslate(item.LabelKey, out TaggedString localized))
            {
                return localized.ToString();
            }

            return item.DefaultLabel;
        }
    }

    internal readonly struct PromptUnifiedNodeSchemaItem
    {
        internal readonly string Id;
        internal readonly string LabelKey;
        internal readonly string DefaultLabel;

        internal PromptUnifiedNodeSchemaItem(string id, string labelKey, string defaultLabel)
        {
            Id = id ?? string.Empty;
            LabelKey = labelKey ?? string.Empty;
            DefaultLabel = defaultLabel ?? string.Empty;
        }
    }
}
