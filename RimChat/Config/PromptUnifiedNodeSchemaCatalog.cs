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
            new PromptUnifiedNodeSchemaItem("api_limits_node_template", "RimChat_PromptNode_ApiLimitsNode", "API Limits"),
            new PromptUnifiedNodeSchemaItem("quest_guidance_node_template", "RimChat_PromptNode_QuestGuidanceNode", "Quest Rules"),
            new PromptUnifiedNodeSchemaItem("response_contract_node_template", "RimChat_PromptNode_ResponseContractNode", "Response Contract"),
            new PromptUnifiedNodeSchemaItem("strategy_output_contract", "RimChat_PromptNode_StrategyOutputContract", "Strategy Output Contract"),
            new PromptUnifiedNodeSchemaItem("strategy_player_negotiator_context_template", "RimChat_PromptNode_StrategyNegotiatorContext", "Strategy Negotiator Context"),
            new PromptUnifiedNodeSchemaItem("strategy_fact_pack_template", "RimChat_PromptNode_StrategyFactPack", "Strategy Fact Pack"),
            new PromptUnifiedNodeSchemaItem("strategy_scenario_dossier_template", "RimChat_PromptNode_StrategyScenarioDossier", "Strategy Scenario Dossier"),
            new PromptUnifiedNodeSchemaItem("social_news_style", "RimChat_PromptNode_SocialNewsStyle", "Social News Style"),
            new PromptUnifiedNodeSchemaItem("social_news_json_contract", "RimChat_PromptNode_SocialNewsJsonContract", "Social News JSON Contract"),
            new PromptUnifiedNodeSchemaItem("social_news_fact", "RimChat_PromptNode_SocialNewsFact", "Social News Fact"),
            new PromptUnifiedNodeSchemaItem("rpg_role_setting_fallback", "RimChat_PromptNode_RpgRoleFallback", "RPG Role Fallback"),
            new PromptUnifiedNodeSchemaItem("rpg_relationship_profile", "RimChat_PromptNode_RpgRelationshipProfile", "RPG Relationship Profile"),
            new PromptUnifiedNodeSchemaItem("rpg_kinship_boundary", "RimChat_PromptNode_RpgKinshipBoundary", "RPG Kinship Boundary"),
            new PromptUnifiedNodeSchemaItem("thought_chain_node_template", "RimChat_PromptNode_ThoughtChainNode", "Thought Chain Node")
        };

        private static readonly Dictionary<string, string[]> AllowedNodesByChannel =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [RimTalkPromptEntryChannelCatalog.Any] = NodeItems.Select(item => item.Id).ToArray(),
                [RimTalkPromptEntryChannelCatalog.DiplomacyDialogue] = new[]
                {
                    "fact_grounding",
                    "output_language",
                    "decision_policy",
                    "turn_objective",
                    "topic_shift_rule",
                    "diplomacy_fallback_role",
                    "social_circle_action_rule",
                    "api_limits_node_template",
                    "quest_guidance_node_template",
                    "response_contract_node_template",
                    "thought_chain_node_template"
                },
                [RimTalkPromptEntryChannelCatalog.ProactiveDiplomacyDialogue] = new[]
                {
                    "fact_grounding",
                    "output_language",
                    "decision_policy",
                    "turn_objective",
                    "topic_shift_rule",
                    "diplomacy_fallback_role",
                    "social_circle_action_rule",
                    "api_limits_node_template",
                    "quest_guidance_node_template",
                    "response_contract_node_template",
                    "thought_chain_node_template"
                },
                [RimTalkPromptEntryChannelCatalog.RpgDialogue] = new[]
                {
                    "fact_grounding",
                    "output_language",
                    "decision_policy",
                    "turn_objective",
                    "topic_shift_rule",
                    "opening_objective",
                    "rpg_role_setting_fallback",
                    "rpg_relationship_profile",
                    "rpg_kinship_boundary",
                    "response_contract_node_template",
                    "thought_chain_node_template"
                },
                [RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue] = new[]
                {
                    "fact_grounding",
                    "output_language",
                    "decision_policy",
                    "turn_objective",
                    "topic_shift_rule",
                    "opening_objective",
                    "rpg_role_setting_fallback",
                    "rpg_relationship_profile",
                    "rpg_kinship_boundary",
                    "response_contract_node_template",
                    "thought_chain_node_template"
                },
                [RimTalkPromptEntryChannelCatalog.DiplomacyStrategy] = new[]
                {
                    "fact_grounding",
                    "output_language",
                    "decision_policy",
                    "turn_objective",
                    "strategy_output_contract",
                    "strategy_player_negotiator_context_template",
                    "strategy_fact_pack_template",
                    "strategy_scenario_dossier_template",
                    "thought_chain_node_template"
                },
                [RimTalkPromptEntryChannelCatalog.SocialCirclePost] = new[]
                {
                    "fact_grounding",
                    "output_language",
                    "decision_policy",
                    "turn_objective",
                    "topic_shift_rule",
                    "social_circle_action_rule",
                    "social_news_style",
                    "social_news_json_contract",
                    "social_news_fact",
                    "thought_chain_node_template"
                },
                [RimTalkPromptEntryChannelCatalog.PersonaBootstrap] = Array.Empty<string>(),
                [RimTalkPromptEntryChannelCatalog.SummaryGeneration] = Array.Empty<string>(),
                [RimTalkPromptEntryChannelCatalog.RpgArchiveCompression] = Array.Empty<string>(),
                [RimTalkPromptEntryChannelCatalog.ImageGeneration] = Array.Empty<string>()
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

        internal static IReadOnlyList<PromptUnifiedNodeSchemaItem> GetAllowedNodes(string promptChannel)
        {
            string normalizedChannel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            if (!AllowedNodesByChannel.TryGetValue(normalizedChannel, out string[] allowedNodeIds))
            {
                allowedNodeIds = AllowedNodesByChannel[RimTalkPromptEntryChannelCatalog.Any];
            }

            return BuildAllowedNodes(allowedNodeIds);
        }

        internal static IReadOnlyList<PromptUnifiedNodeSchemaItem> GetAllowedNodesStrict(string promptChannel)
        {
            string normalizedChannel = NormalizeStrictChannelOrThrow(promptChannel);
            if (!AllowedNodesByChannel.TryGetValue(normalizedChannel, out string[] allowedNodeIds))
            {
                throw new InvalidOperationException(
                    $"[RimChat] Unknown prompt channel '{promptChannel ?? string.Empty}' in strict node schema lookup.");
            }

            return BuildAllowedNodes(allowedNodeIds);
        }

        internal static string NormalizeStrictChannelOrThrow(string promptChannel)
        {
            if (string.IsNullOrWhiteSpace(promptChannel))
            {
                throw new InvalidOperationException("[RimChat] Prompt channel cannot be empty for strict node operations.");
            }

            string normalized = promptChannel.Trim().ToLowerInvariant();
            if (!AllowedNodesByChannel.ContainsKey(normalized))
            {
                throw new InvalidOperationException(
                    $"[RimChat] Unknown prompt channel '{promptChannel}' for strict node operations.");
            }

            return normalized;
        }

        internal static void EnsureNodeAllowedForChannelOrThrow(string promptChannel, string nodeId, string operation)
        {
            string channel = NormalizeStrictChannelOrThrow(promptChannel);
            string normalizedNode = NormalizeId(nodeId);
            if (string.IsNullOrWhiteSpace(normalizedNode))
            {
                throw new InvalidOperationException(
                    $"[RimChat] {operation ?? "Node operation"} requires a non-empty nodeId.");
            }

            if (!GetAllowedNodesStrict(channel).Any(item =>
                    string.Equals(item.Id, normalizedNode, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"[RimChat] {operation ?? "Node operation"} rejected node '{normalizedNode}' for channel '{channel}'.");
            }
        }

        internal static bool IsNodeAllowedForChannel(string promptChannel, string nodeId)
        {
            string normalizedNode = NormalizeId(nodeId);
            if (string.IsNullOrWhiteSpace(normalizedNode))
            {
                return false;
            }

            return GetAllowedNodes(promptChannel).Any(item =>
                string.Equals(item.Id, normalizedNode, StringComparison.OrdinalIgnoreCase));
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

        private static IReadOnlyList<PromptUnifiedNodeSchemaItem> BuildAllowedNodes(string[] allowedNodeIds)
        {
            if (allowedNodeIds == null || allowedNodeIds.Length == 0)
            {
                return Array.Empty<PromptUnifiedNodeSchemaItem>();
            }

            var results = new List<PromptUnifiedNodeSchemaItem>(allowedNodeIds.Length);
            for (int i = 0; i < allowedNodeIds.Length; i++)
            {
                if (TryGet(allowedNodeIds[i], out PromptUnifiedNodeSchemaItem item))
                {
                    results.Add(item);
                }
            }

            return results;
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
