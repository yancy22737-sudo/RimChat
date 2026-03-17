using System;
using System.Collections.Generic;
using System.Linq;

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
            new PromptUnifiedNodeSchemaItem("fact_grounding", "Fact Grounding"),
            new PromptUnifiedNodeSchemaItem("output_language", "Output Language"),
            new PromptUnifiedNodeSchemaItem("decision_policy", "Decision Policy"),
            new PromptUnifiedNodeSchemaItem("turn_objective", "Turn Objective"),
            new PromptUnifiedNodeSchemaItem("opening_objective", "Opening Objective"),
            new PromptUnifiedNodeSchemaItem("topic_shift_rule", "Topic Shift Rule"),
            new PromptUnifiedNodeSchemaItem("diplomacy_fallback_role", "Diplomacy Role Fallback"),
            new PromptUnifiedNodeSchemaItem("social_circle_action_rule", "Social Action Rule"),
            new PromptUnifiedNodeSchemaItem("api_limits_node_template", "API Limits Node"),
            new PromptUnifiedNodeSchemaItem("quest_guidance_node_template", "Quest Guidance Node"),
            new PromptUnifiedNodeSchemaItem("response_contract_node_template", "Response Contract Node"),
            new PromptUnifiedNodeSchemaItem("strategy_output_contract", "Strategy Output Contract"),
            new PromptUnifiedNodeSchemaItem("strategy_player_negotiator_context_template", "Strategy Negotiator Context"),
            new PromptUnifiedNodeSchemaItem("strategy_fact_pack_template", "Strategy Fact Pack"),
            new PromptUnifiedNodeSchemaItem("strategy_scenario_dossier_template", "Strategy Scenario Dossier"),
            new PromptUnifiedNodeSchemaItem("social_news_style", "Social News Style"),
            new PromptUnifiedNodeSchemaItem("social_news_json_contract", "Social News JSON Contract"),
            new PromptUnifiedNodeSchemaItem("social_news_fact", "Social News Fact"),
            new PromptUnifiedNodeSchemaItem("rpg_role_setting_fallback", "RPG Role Fallback"),
            new PromptUnifiedNodeSchemaItem("rpg_relationship_profile", "RPG Relationship Profile"),
            new PromptUnifiedNodeSchemaItem("rpg_kinship_boundary", "RPG Kinship Boundary")
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
    }

    internal readonly struct PromptUnifiedNodeSchemaItem
    {
        internal readonly string Id;
        internal readonly string Label;

        internal PromptUnifiedNodeSchemaItem(string id, string label)
        {
            Id = id ?? string.Empty;
            Label = label ?? string.Empty;
        }
    }
}
