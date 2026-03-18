using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimChat.Config;
using RimChat.Core;
using RimChat.Memory;
using RimChat.Prompting;
using RimWorld;
using Verse;

namespace RimChat.Persistence
{
    /// <summary>/// Dependencies: SystemPromptConfig, DialogueScenarioContext, PromptHierarchyRenderer.
 /// Responsibility: build diplomacy/RPG prompts with strict Scriban rendering and hierarchical policy pipeline.
 ///</summary>
    public partial class PromptPersistenceService
    {
        internal string BuildFullSystemPromptHierarchicalCore(
            Faction faction,
            SystemPromptConfig config,
            bool isProactive,
            IEnumerable<string> additionalSceneTags)
        {
            return BuildFullSystemPromptHierarchicalCore(
                faction,
                config,
                isProactive,
                additionalSceneTags,
                null);
        }

        internal string BuildFullSystemPromptHierarchicalCore(
            Faction faction,
            SystemPromptConfig config,
            bool isProactive,
            IEnumerable<string> additionalSceneTags,
            Pawn playerNegotiator)
        {
            return BuildFullSystemPromptHierarchical(faction, config, isProactive, additionalSceneTags, playerNegotiator);
        }

        internal string BuildRpgSystemPromptHierarchicalCore(
            Pawn initiator,
            Pawn target,
            bool isProactive,
            IEnumerable<string> additionalSceneTags)
        {
            return BuildRpgSystemPromptHierarchical(initiator, target, isProactive, additionalSceneTags);
        }

        internal string BuildDiplomacyStrategySystemPromptCore(
            Faction faction,
            SystemPromptConfig config,
            IEnumerable<string> additionalSceneTags,
            DiplomacyStrategyPromptContext strategyContext)
        {
            return BuildDiplomacyStrategySystemPromptHierarchical(
                faction,
                config,
                additionalSceneTags,
                strategyContext);
        }

        private string BuildFullSystemPromptHierarchical(
            Faction faction,
            SystemPromptConfig config,
            bool isProactive,
            IEnumerable<string> additionalSceneTags,
            Pawn playerNegotiator)
        {
            var scenarioContext = DialogueScenarioContext.CreateDiplomacy(faction, isProactive, additionalSceneTags);
            string promptChannel = ResolvePromptChannelForContext(scenarioContext);
            List<ResolvedPromptNodePlacement> placements = ResolveDiplomacyNodePlacements(
                promptChannel,
                config,
                scenarioContext,
                faction,
                playerNegotiator);
            var root = new PromptHierarchyNode("prompt_context");
            AddTextNodeIfNotEmpty(root, "channel", "diplomacy");
            AddTextNodeIfNotEmpty(root, "mode", isProactive ? "proactive" : "manual");
            AddTextNodeIfNotEmpty(root, "environment", BuildEnvironmentPromptBlocks(config, scenarioContext));
            ApplyResolvedNodePlacements(root, placements, PromptUnifiedNodeSlot.MetadataAfter);
            ApplyResolvedNodePlacements(root, placements, PromptUnifiedNodeSlot.MainChainBefore);
            AddNodeIfAnyChildren(root, BuildMainChainPromptSectionNode(
                RimTalkPromptChannel.Diplomacy,
                config,
                scenarioContext,
                config?.EnvironmentPrompt));
            ApplyResolvedNodePlacements(root, placements, PromptUnifiedNodeSlot.MainChainBefore, true);
            ApplyResolvedNodePlacements(root, placements, PromptUnifiedNodeSlot.MainChainAfter);

            var instruction = root.AddChild("instruction_stack");
            AddTextNodeIfNotEmpty(instruction, "faction_characteristics", ResolveFactionPromptText(faction, config, scenarioContext));

            PromptHierarchyNode dynamicData = BuildDiplomacyDynamicDataNode(config, faction, playerNegotiator);
            if (dynamicData != null)
            {
                root.Children.Add(dynamicData);
            }
            ApplyResolvedNodePlacements(root, placements, PromptUnifiedNodeSlot.DynamicDataAfter);
            ApplyResolvedNodePlacements(root, placements, PromptUnifiedNodeSlot.ContractBeforeEnd);
            if (instruction.Children.Count == 0)
            {
                root.Children.Remove(instruction);
            }

            return PromptHierarchyRenderer.Render(root);
        }

        private string BuildDiplomacyStrategySystemPromptHierarchical(
            Faction faction,
            SystemPromptConfig config,
            IEnumerable<string> additionalSceneTags,
            DiplomacyStrategyPromptContext strategyContext)
        {
            config ??= LoadConfig() ?? CreateDefaultConfig();
            var scenarioContext = DialogueScenarioContext.CreateDiplomacy(faction, false, additionalSceneTags);
            strategyContext ??= new DiplomacyStrategyPromptContext();
            string promptChannel = RimTalkPromptEntryChannelCatalog.DiplomacyStrategy;
            List<ResolvedPromptNodePlacement> placements = ResolveStrategyNodePlacements(
                promptChannel,
                config,
                scenarioContext,
                strategyContext);

            var root = new PromptHierarchyNode("prompt_context");
            AddTextNodeIfNotEmpty(root, "channel", RimTalkPromptEntryChannelCatalog.DiplomacyStrategy);
            AddTextNodeIfNotEmpty(root, "mode", "manual");
            AddTextNodeIfNotEmpty(root, "environment", BuildEnvironmentPromptBlocks(config, scenarioContext));
            ApplyResolvedNodePlacements(root, placements, PromptUnifiedNodeSlot.MetadataAfter);
            ApplyResolvedNodePlacements(root, placements, PromptUnifiedNodeSlot.MainChainBefore);
            AddNodeIfAnyChildren(root, BuildPromptSectionAggregateNode(
                config,
                RimTalkPromptEntryChannelCatalog.DiplomacyStrategy,
                scenarioContext,
                config?.EnvironmentPrompt));
            ApplyResolvedNodePlacements(root, placements, PromptUnifiedNodeSlot.MainChainBefore, true);
            ApplyResolvedNodePlacements(root, placements, PromptUnifiedNodeSlot.MainChainAfter);
            ApplyResolvedNodePlacements(root, placements, PromptUnifiedNodeSlot.DynamicDataAfter);
            ApplyResolvedNodePlacements(root, placements, PromptUnifiedNodeSlot.ContractBeforeEnd);
            return PromptHierarchyRenderer.Render(root);
        }

        private string BuildRpgSystemPromptHierarchical(
            Pawn initiator,
            Pawn target,
            bool isProactive,
            IEnumerable<string> additionalSceneTags)
        {
            var settings = RimChatMod.Settings;
            settings?.EnsureRpgPromptTextsLoaded();
            SystemPromptConfig config = LoadConfig() ?? CreateDefaultConfig();
            var scenarioContext = DialogueScenarioContext.CreateRpg(initiator, target, isProactive, additionalSceneTags);
            bool samePlayerFaction =
                initiator?.Faction != null &&
                initiator.Faction == target?.Faction &&
                initiator.Faction.IsPlayer;
            bool preferCompactContext = !isProactive && samePlayerFaction;
            PromptPolicyConfig promptPolicy = ResolvePromptPolicyConfig(config);
            bool includeOpeningObjective = IsOpeningTurnContext(scenarioContext);
            string unresolvedIntent = RpgNpcDialogueArchiveManager.Instance.BuildUnresolvedIntentSummary(target, initiator);
            string promptChannel = ResolvePromptChannelForContext(scenarioContext);
            List<ResolvedPromptNodePlacement> placements = ResolveRpgNodePlacements(
                promptChannel,
                settings,
                config,
                scenarioContext,
                initiator,
                target,
                unresolvedIntent,
                includeOpeningObjective);

            var root = new PromptHierarchyNode("prompt_context");
            AddTextNodeIfNotEmpty(root, "channel", "rpg");
            AddTextNodeIfNotEmpty(root, "mode", isProactive ? "proactive" : "manual");
            string environmentBlock = BuildEnvironmentPromptBlocks(config, scenarioContext);
            if (preferCompactContext)
            {
                environmentBlock = CompactRpgEnvironmentBlock(environmentBlock);
            }
            AddTextNodeIfNotEmpty(root, "environment", environmentBlock);
            ApplyResolvedNodePlacements(root, placements, PromptUnifiedNodeSlot.MetadataAfter);
            ApplyResolvedNodePlacements(root, placements, PromptUnifiedNodeSlot.MainChainBefore);
            AddNodeIfAnyChildren(root, BuildMainChainPromptSectionNode(
                RimTalkPromptChannel.Rpg,
                config,
                scenarioContext,
                config?.EnvironmentPrompt));
            ApplyResolvedNodePlacements(root, placements, PromptUnifiedNodeSlot.MainChainBefore, true);
            ApplyResolvedNodePlacements(root, placements, PromptUnifiedNodeSlot.MainChainAfter);

            var roleStack = root.AddChild("role_stack");
            AddTextNodeIfNotEmpty(roleStack, "personality_override", ResolveRpgPawnPersonaPrompt(target));
            string dialogueStyle = settings?.ResolvePromptSectionText(promptChannel, "output_specification");
            if (string.IsNullOrWhiteSpace(dialogueStyle))
            {
                dialogueStyle = PromptUnifiedCatalog.CreateFallback().ResolveSection(promptChannel, "output_specification");
            }

            AddTextNodeIfNotEmpty(roleStack, "dialogue_style", dialogueStyle, true);

            AddTextNodeIfNotEmpty(root, "dynamic_faction_memory",
                DialogueSummaryService.BuildRpgDynamicFactionMemoryBlock(target?.Faction, target));
            AddTextNodeIfNotEmpty(root, "dynamic_npc_personal_memory",
                RpgNpcDialogueArchiveManager.Instance.BuildPromptMemoryBlock(
                    target,
                    initiator,
                    promptPolicy?.SummaryTimelineTurnLimit ?? 8,
                    promptPolicy?.SummaryCharBudget ?? 1200));

            PromptHierarchyNode actorState = BuildRpgActorStateNode(
                settings,
                config,
                initiator,
                target,
                preferCompactContext);
            if (actorState != null)
            {
                root.Children.Add(actorState);
            }
            ApplyResolvedNodePlacements(root, placements, PromptUnifiedNodeSlot.DynamicDataAfter);
            ApplyResolvedNodePlacements(root, placements, PromptUnifiedNodeSlot.ContractBeforeEnd);

            bool preferCompactApiContract = preferCompactContext;
            AddTextNodeIfNotEmpty(root, "api_contract", BuildRpgApiContractText(settings, config, scenarioContext, preferCompactApiContract));
            return PromptHierarchyRenderer.Render(root);
        }

        private PromptHierarchyNode BuildDiplomacyDynamicDataNode(SystemPromptConfig config, Faction faction, Pawn playerNegotiator)
        {
            if (config?.DynamicDataInjection == null)
            {
                return null;
            }

            var node = new PromptHierarchyNode("dynamic_data");
            DynamicDataInjectionConfig dyn = config.DynamicDataInjection;
            if (dyn.InjectMemoryData)
            {
                AddTextNodeIfNotEmpty(node, "memory_data", BuildTextBlock(sb => AppendMemoryData(sb, faction)));
            }

            if (dyn.InjectFactionInfo)
            {
                AddTextNodeIfNotEmpty(node, "faction_info", BuildTextBlock(sb => AppendFactionInfo(sb, faction)));
                AddTextNodeIfNotEmpty(node, "player_pawn_profile", BuildPlayerPawnContextForPrompt(faction, playerNegotiator));
                AddTextNodeIfNotEmpty(node, "player_royalty_summary", BuildPlayerRoyaltySummaryForPrompt(faction, playerNegotiator));
                AddTextNodeIfNotEmpty(node, "faction_settlement_summary", BuildFactionSettlementSummaryForPrompt(faction));
            }

            return node.Children.Count > 0 ? node : null;
        }

        private PromptHierarchyNode BuildRpgActorStateNode(
            RimChatSettings settings,
            SystemPromptConfig config,
            Pawn initiator,
            Pawn target,
            bool preferCompactContext)
        {
            var node = new PromptHierarchyNode("actor_state");
            bool samePlayerFaction =
                initiator?.Faction != null &&
                initiator.Faction == target?.Faction &&
                initiator.Faction.IsPlayer;

            if (settings?.RPGInjectSelfStatus == true)
            {
                AddTextNodeIfNotEmpty(node, "self_status",
                    BuildTextBlock(sb => AppendRPGPawnInfo(
                        sb,
                        target,
                        true,
                        config?.EnvironmentPrompt?.RpgSceneParamSwitches,
                        includePlayerSharedColonyContext: true,
                        includeStaticProfileDetails: !preferCompactContext)));
            }

            if (settings?.RPGInjectInterlocutorStatus == true)
            {
                AddTextNodeIfNotEmpty(node, "interlocutor_status",
                    BuildTextBlock(sb => AppendRPGPawnInfo(
                        sb,
                        initiator,
                        false,
                        config?.EnvironmentPrompt?.RpgSceneParamSwitches,
                        includePlayerSharedColonyContext: !samePlayerFaction,
                        includeStaticProfileDetails: !samePlayerFaction && !preferCompactContext)));
            }

            if (settings?.RPGInjectFactionBackground == true)
            {
                AddTextNodeIfNotEmpty(node, "target_faction_context", BuildTextBlock(sb => AppendRPGFactionContext(sb, target)));
                if (initiator?.Faction != target?.Faction)
                {
                    AddTextNodeIfNotEmpty(node, "interlocutor_faction_context",
                        BuildTextBlock(sb => AppendRPGFactionContext(sb, initiator)));
                }
            }

            return node.Children.Count > 0 ? node : null;
        }

        private void ApplyResolvedNodePlacements(
            PromptHierarchyNode root,
            IEnumerable<ResolvedPromptNodePlacement> placements,
            PromptUnifiedNodeSlot slot,
            bool renderAfterSectionAggregate = false)
        {
            if (root == null || placements == null)
            {
                return;
            }

            foreach (ResolvedPromptNodePlacement placement in placements)
            {
                if (placement == null || placement.Slot != slot || !placement.Enabled)
                {
                    continue;
                }

                if (IsThoughtChainPlacement(placement) != renderAfterSectionAggregate)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(placement.Content))
                {
                    placement.Applied = false;
                    continue;
                }

                AddTextNodeIfNotEmpty(root, placement.OutputTag, placement.Content);
                placement.Applied = true;
            }
        }

        private List<PromptUnifiedNodeLayoutConfig> GetOrderedNodeLayouts(string promptChannel)
        {
            var allowedNodeIds = new HashSet<string>(
                PromptUnifiedNodeSchemaCatalog.GetAllowedNodes(promptChannel).Select(item => item.Id),
                StringComparer.OrdinalIgnoreCase);
            if (allowedNodeIds.Count == 0)
            {
                return new List<PromptUnifiedNodeLayoutConfig>();
            }

            List<PromptUnifiedNodeLayoutConfig> fromSettings = RimChatMod.Settings?.GetPromptNodeLayouts(promptChannel);
            if (fromSettings != null && fromSettings.Count > 0)
            {
                var filtered = new Dictionary<string, PromptUnifiedNodeLayoutConfig>(StringComparer.OrdinalIgnoreCase);
                foreach (PromptUnifiedNodeLayoutConfig layout in fromSettings)
                {
                    if (layout == null || string.IsNullOrWhiteSpace(layout.NodeId))
                    {
                        continue;
                    }

                    if (!allowedNodeIds.Contains(layout.NodeId))
                    {
                        Log.Error($"[RimChat] Runtime node layout '{layout.NodeId}' is not allowed for channel '{promptChannel}'. Layout ignored.");
                        continue;
                    }

                    filtered[layout.NodeId] = layout.Clone();
                }

                foreach (string nodeId in allowedNodeIds)
                {
                    if (!filtered.ContainsKey(nodeId))
                    {
                        filtered[nodeId] = PromptUnifiedNodeLayoutDefaults.BuildDefaultLayout(promptChannel, nodeId);
                    }
                }

                return filtered.Values
                    .OrderBy(item => item.GetSlot())
                    .ThenBy(item => item.Order)
                    .ThenBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return PromptUnifiedNodeSchemaCatalog.GetAllowedNodes(promptChannel)
                .Select(node => PromptUnifiedNodeLayoutDefaults.BuildDefaultLayout(promptChannel, node.Id))
                .OrderBy(item => item.GetSlot())
                .ThenBy(item => item.Order)
                .ThenBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<ResolvedPromptNodePlacement> ResolveDiplomacyNodePlacements(
            string promptChannel,
            SystemPromptConfig config,
            DialogueScenarioContext context,
            Faction faction,
            Pawn playerNegotiator)
        {
            string apiLimitsBody = BuildTextBlock(sb => AppendApiLimits(sb, faction));
            string questGuidanceBody = BuildTextBlock(sb =>
            {
                AppendDynamicQuestGuidance(sb, faction);
                AppendQuestSelectionHardRules(sb);
            });
            string responseContractBody = BuildTextBlock(sb =>
            {
                if (config.UseAdvancedMode)
                {
                    AppendAdvancedConfig(sb, config, faction);
                }
                else
                {
                    AppendSimpleConfig(sb, config, faction);
                }
            });

            var placements = new List<ResolvedPromptNodePlacement>();
            foreach (PromptUnifiedNodeLayoutConfig layout in GetOrderedNodeLayouts(promptChannel))
            {
                if (layout == null)
                {
                    continue;
                }

                string nodeId = layout.NodeId;
                var placement = new ResolvedPromptNodePlacement
                {
                    PromptChannel = promptChannel,
                    NodeId = nodeId,
                    Slot = layout.GetSlot(),
                    Order = layout.Order,
                    Enabled = layout.Enabled,
                    OutputTag = nodeId
                };

                switch (nodeId)
                {
                    case "fact_grounding":
                        placement.OutputTag = "fact_grounding";
                        placement.Content = BuildFactGroundingGuidanceText(config, context);
                        break;
                    case "output_language":
                        placement.OutputTag = "output_language";
                        placement.Content = BuildOutputLanguageGuidance(RimChatMod.Settings, config, context);
                        break;
                    case "decision_policy":
                        placement.OutputTag = "decision_policy";
                        placement.Content = BuildDecisionPolicyText(config, context);
                        break;
                    case "turn_objective":
                        placement.OutputTag = "turn_objective";
                        placement.Content = BuildTurnObjectiveText(
                            config,
                            context,
                            "Address the player's latest explicit intent from the current turn first.",
                            "After finishing the primary objective, you may add one natural follow-up extension.");
                        break;
                    case "topic_shift_rule":
                        placement.OutputTag = "topic_shift_rule";
                        placement.Content = BuildTopicShiftRuleText(config, context);
                        break;
                    case "diplomacy_fallback_role":
                        placement.OutputTag = "diplomacy_fallback_role";
                        placement.Content = ResolveFactionPromptText(faction, config, context);
                        break;
                    case "social_circle_action_rule":
                        placement.OutputTag = "social_circle_action_rule";
                        placement.Content = BuildSocialCircleActionRuleText(config, context);
                        break;
                    case "api_limits_node_template":
                        placement.OutputTag = "api_limits";
                        placement.Content = RenderPromptNodeTemplate(
                            config,
                            context,
                            ResolveUnifiedNodeTemplate(promptChannel, "api_limits_node_template", PromptTextConstants.ApiLimitsNodeLiteralDefault),
                            "api_limits_body",
                            apiLimitsBody);
                        break;
                    case "quest_guidance_node_template":
                        placement.OutputTag = "quest_guidance";
                        placement.Content = RenderPromptNodeTemplate(
                            config,
                            context,
                            ResolveUnifiedNodeTemplate(promptChannel, "quest_guidance_node_template", PromptTextConstants.QuestGuidanceNodeLiteralDefault),
                            "quest_guidance_body",
                            questGuidanceBody);
                        break;
                    case "thought_chain_node_template":
                        placement.OutputTag = "thought_chain";
                        placement.Content = ResolveUnifiedNodeTemplate(promptChannel, "thought_chain_node_template", string.Empty);
                        break;
                    case "response_contract_node_template":
                        placement.OutputTag = "response_contract";
                        placement.Content = RenderPromptNodeTemplate(
                            config,
                            context,
                            ResolveUnifiedNodeTemplate(promptChannel, "response_contract_node_template", PromptTextConstants.ResponseContractNodeLiteralDefault),
                            "response_contract_body",
                            responseContractBody);
                        break;
                    default:
                        placement.Content = string.Empty;
                        break;
                }

                placements.Add(placement);
            }

            return placements
                .OrderBy(item => item.Slot)
                .ThenBy(item => item.Order)
                .ThenBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<ResolvedPromptNodePlacement> ResolveRpgNodePlacements(
            string promptChannel,
            RimChatSettings settings,
            SystemPromptConfig config,
            DialogueScenarioContext context,
            Pawn initiator,
            Pawn target,
            string unresolvedIntent,
            bool includeOpeningObjective)
        {
            var placements = new List<ResolvedPromptNodePlacement>();
            foreach (PromptUnifiedNodeLayoutConfig layout in GetOrderedNodeLayouts(promptChannel))
            {
                if (layout == null)
                {
                    continue;
                }

                string nodeId = layout.NodeId;
                var placement = new ResolvedPromptNodePlacement
                {
                    PromptChannel = promptChannel,
                    NodeId = nodeId,
                    Slot = layout.GetSlot(),
                    Order = layout.Order,
                    Enabled = layout.Enabled,
                    OutputTag = nodeId
                };

                switch (nodeId)
                {
                    case "fact_grounding":
                        placement.OutputTag = "fact_grounding";
                        placement.Content = BuildFactGroundingGuidanceText(config, context);
                        break;
                    case "output_language":
                        placement.OutputTag = "output_language";
                        placement.Content = BuildOutputLanguageGuidance(settings, config, context);
                        break;
                    case "decision_policy":
                        placement.OutputTag = "decision_policy";
                        placement.Content = BuildDecisionPolicyText(config, context);
                        break;
                    case "turn_objective":
                        placement.OutputTag = "turn_objective";
                        placement.Content = BuildTurnObjectiveText(
                            config,
                            context,
                            BuildPrimaryObjectiveFromIntent(unresolvedIntent),
                            "After completing the primary objective, optionally add one relevant follow-up.");
                        break;
                    case "topic_shift_rule":
                        placement.OutputTag = "topic_shift_rule";
                        placement.Content = BuildTopicShiftRuleText(config, context);
                        break;
                    case "opening_objective":
                        placement.OutputTag = "opening_objective";
                        placement.Content = includeOpeningObjective
                            ? BuildOpeningObjectiveText(config, context, unresolvedIntent)
                            : string.Empty;
                        break;
                    case "rpg_role_setting_fallback":
                        placement.OutputTag = "role_setting";
                        placement.Content = BuildRpgRoleSettingText(settings, config, context, target);
                        break;
                    case "rpg_relationship_profile":
                        placement.OutputTag = "relationship_profile";
                        placement.Content = BuildRpgRelationshipProfileText(settings, initiator, target, context);
                        break;
                    case "rpg_kinship_boundary":
                        placement.OutputTag = "kinship_boundary_rule";
                        placement.Content = BuildRpgKinshipBoundaryGuidanceText(settings, initiator, target, context);
                        break;
                    case "thought_chain_node_template":
                        placement.OutputTag = "thought_chain";
                        placement.Content = ResolveUnifiedNodeTemplate(promptChannel, "thought_chain_node_template", string.Empty);
                        break;
                    default:
                        placement.Content = string.Empty;
                        break;
                }

                placements.Add(placement);
            }

            return placements
                .OrderBy(item => item.Slot)
                .ThenBy(item => item.Order)
                .ThenBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<ResolvedPromptNodePlacement> ResolveStrategyNodePlacements(
            string promptChannel,
            SystemPromptConfig config,
            DialogueScenarioContext context,
            DiplomacyStrategyPromptContext strategyContext)
        {
            var placements = new List<ResolvedPromptNodePlacement>();
            foreach (PromptUnifiedNodeLayoutConfig layout in GetOrderedNodeLayouts(promptChannel))
            {
                if (layout == null)
                {
                    continue;
                }

                string nodeId = layout.NodeId;
                var placement = new ResolvedPromptNodePlacement
                {
                    PromptChannel = promptChannel,
                    NodeId = nodeId,
                    Slot = layout.GetSlot(),
                    Order = layout.Order,
                    Enabled = layout.Enabled,
                    OutputTag = nodeId
                };

                switch (nodeId)
                {
                    case "fact_grounding":
                        placement.OutputTag = "fact_grounding";
                        placement.Content = BuildFactGroundingGuidanceText(config, context);
                        break;
                    case "output_language":
                        placement.OutputTag = "output_language";
                        placement.Content = BuildOutputLanguageGuidance(RimChatMod.Settings, config, context);
                        break;
                    case "decision_policy":
                        placement.OutputTag = "decision_policy";
                        placement.Content = BuildDiplomacyStrategyDecisionPolicyText();
                        break;
                    case "turn_objective":
                        placement.OutputTag = "turn_objective";
                        placement.Content = BuildDiplomacyStrategyTurnObjectiveText();
                        break;
                    case "strategy_output_contract":
                        placement.OutputTag = "strategy_output_contract";
                        placement.Content = BuildDiplomacyStrategyOutputContractText();
                        break;
                    case "strategy_player_negotiator_context_template":
                        placement.OutputTag = "player_negotiator_context";
                        placement.Content = RenderStrategyNodeTemplate(
                            promptChannel,
                            "strategy_player_negotiator_context_template",
                            "dialogue.strategy_player_negotiator_context_body",
                            strategyContext?.NegotiatorContextText,
                            context);
                        break;
                    case "strategy_fact_pack_template":
                        placement.OutputTag = "strategy_fact_pack";
                        placement.Content = RenderStrategyNodeTemplate(
                            promptChannel,
                            "strategy_fact_pack_template",
                            "dialogue.strategy_fact_pack_body",
                            strategyContext?.StrategyFactPackText,
                            context);
                        break;
                    case "strategy_scenario_dossier_template":
                        placement.OutputTag = "strategy_scenario_dossier";
                        placement.Content = RenderStrategyNodeTemplate(
                            promptChannel,
                            "strategy_scenario_dossier_template",
                            "dialogue.strategy_scenario_dossier_body",
                            strategyContext?.ScenarioDossierText,
                            context);
                        break;
                    case "thought_chain_node_template":
                        placement.OutputTag = "thought_chain";
                        placement.Content = ResolveUnifiedNodeTemplate(promptChannel, "thought_chain_node_template", string.Empty);
                        break;
                    default:
                        placement.Content = string.Empty;
                        break;
                }

                placements.Add(placement);
            }

            return placements
                .OrderBy(item => item.Slot)
                .ThenBy(item => item.Order)
                .ThenBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string BuildRpgKinshipBoundaryGuidanceText(
            RimChatSettings settings,
            Pawn initiator,
            Pawn target,
            DialogueScenarioContext context)
        {
            if (initiator == null || target == null)
            {
                return string.Empty;
            }

            bool kinship = HasAnyBloodRelationBetweenPair(initiator, target);
            string kinshipValue = kinship ? "yes" : "no";
            string romanceState = ResolvePairRomanceState(initiator, target);
            var variables = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["pawn.initiator.name"] = initiator.LabelShort ?? "Unknown",
                ["pawn.target.name"] = target.LabelShort ?? "Unknown",
                ["pawn.relation.kinship"] = kinshipValue,
                ["pawn.relation.romance_state"] = romanceState,
                ["pawn.initiator"] = initiator,
                ["pawn.target"] = target
            };

            string promptChannel = ResolvePromptChannelForContext(context) ?? RimTalkPromptEntryChannelCatalog.RpgDialogue;
            string template = ResolveUnifiedNodeTemplate(
                promptChannel,
                "rpg_kinship_boundary",
                ResolveRpgKinshipBoundaryRuleTemplate(settings));
            return ApplyPromptSourceTag(
                RenderTemplateOrThrow("prompt_templates.rpg_kinship_boundary", "rpg", template, variables).Trim(),
                true);
        }

        private static void AddTextNodeIfNotEmpty(PromptHierarchyNode parent, string id, string text, bool fromFile = false)
        {
            if (parent == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            parent.AddChild(id, ApplyPromptSourceTag(text.Trim(), fromFile));
        }

        private static void AddNodeIfAnyChildren(PromptHierarchyNode parent, PromptHierarchyNode child)
        {
            if (parent == null || child == null || child.Children.Count == 0)
            {
                return;
            }

            parent.Children.Add(child);
        }

        private static string ApplyPromptSourceTag(string text, bool fromFile)
        {
            return text?.Trim() ?? string.Empty;
        }

        private static string BuildTextBlock(Action<StringBuilder> appendAction)
        {
            if (appendAction == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            appendAction(sb);
            return sb.ToString().Trim();
        }

        private static string RenderTemplateOrThrow(
            string templateId,
            string channel,
            string templateText,
            IReadOnlyDictionary<string, object> variables)
        {
            string requiredTemplate = RequireTemplateText(templateId, channel, templateText);
            PromptRenderContext renderContext = PromptRenderContext.Create(templateId, channel);
            renderContext.SetValues(variables);
            return PromptTemplateRenderer.RenderOrThrow(templateId, channel, requiredTemplate, renderContext);
        }

        private static string RequireTemplateText(
            string templateId,
            string channel,
            string templateText)
        {
            if (!string.IsNullOrWhiteSpace(templateText))
            {
                return templateText;
            }

            throw new PromptRenderException(
                templateId,
                channel,
                new PromptRenderDiagnostic
                {
                    ErrorCode = PromptRenderErrorCode.TemplateMissing,
                    Message = "Template text is required in strict Scriban mode."
                });
        }

        private string BuildDecisionPolicyText(SystemPromptConfig config, DialogueScenarioContext context)
        {
            bool isRpg = context?.IsRpg == true;
            string legacyTemplate = isRpg
                ? PromptUnifiedCatalog.CreateFallback().ResolveNode(ResolvePromptChannelForContext(context), "decision_policy")
                : config?.PromptTemplates?.DecisionPolicyTemplate;
            string channel = ResolveRenderChannel(context);
            string promptChannel = ResolvePromptChannelForContext(context);
            string template = ResolveUnifiedNodeTemplate(promptChannel, "decision_policy", legacyTemplate);
            string requiredTemplate = RequireTemplateText("prompt_templates.decision_policy", channel, template);

            return ApplyPromptSourceTag(
                RenderTemplateOrThrow(
                    "prompt_templates.decision_policy",
                    channel,
                    requiredTemplate,
                    BuildPolicyTemplateVariables(context, string.Empty, string.Empty, string.Empty)),
                true);
        }

        private string BuildTurnObjectiveText(
            SystemPromptConfig config,
            DialogueScenarioContext context,
            string primaryObjective,
            string optionalFollowup)
        {
            string primary = primaryObjective?.Trim() ?? string.Empty;
            string followup = optionalFollowup?.Trim() ?? string.Empty;
            bool isRpg = context?.IsRpg == true;
            string legacyTemplate = isRpg
                ? PromptUnifiedCatalog.CreateFallback().ResolveNode(ResolvePromptChannelForContext(context), "turn_objective")
                : config?.PromptTemplates?.TurnObjectiveTemplate;
            string channel = ResolveRenderChannel(context);
            string promptChannel = ResolvePromptChannelForContext(context);
            string template = ResolveUnifiedNodeTemplate(promptChannel, "turn_objective", legacyTemplate);
            string requiredTemplate = RequireTemplateText("prompt_templates.turn_objective", channel, template);

            return ApplyPromptSourceTag(
                RenderTemplateOrThrow(
                    "prompt_templates.turn_objective",
                    channel,
                    requiredTemplate,
                    BuildPolicyTemplateVariables(context, primary, followup, string.Empty)),
                true);
        }

        private string BuildOpeningObjectiveText(
            SystemPromptConfig config,
            DialogueScenarioContext context,
            string unresolvedIntent)
        {
            string normalizedIntent = unresolvedIntent?.Trim() ?? string.Empty;
            string legacyTemplate = PromptUnifiedCatalog.CreateFallback().ResolveNode(
                ResolvePromptChannelForContext(context),
                "opening_objective");
            string channel = ResolveRenderChannel(context);
            string promptChannel = ResolvePromptChannelForContext(context);
            string template = ResolveUnifiedNodeTemplate(promptChannel, "opening_objective", legacyTemplate);
            string requiredTemplate = RequireTemplateText("prompt_templates.opening_objective", channel, template);

            return ApplyPromptSourceTag(
                RenderTemplateOrThrow(
                    "prompt_templates.opening_objective",
                    channel,
                    requiredTemplate,
                    BuildPolicyTemplateVariables(context, string.Empty, string.Empty, normalizedIntent)),
                true);
        }

        private string BuildTopicShiftRuleText(SystemPromptConfig config, DialogueScenarioContext context)
        {
            bool isRpg = context?.IsRpg == true;
            string legacyTemplate = isRpg
                ? PromptUnifiedCatalog.CreateFallback().ResolveNode(ResolvePromptChannelForContext(context), "topic_shift_rule")
                : config?.PromptTemplates?.TopicShiftRuleTemplate;
            string channel = ResolveRenderChannel(context);
            string promptChannel = ResolvePromptChannelForContext(context);
            string template = ResolveUnifiedNodeTemplate(promptChannel, "topic_shift_rule", legacyTemplate);
            string requiredTemplate = RequireTemplateText("prompt_templates.topic_shift_rule", channel, template);

            return ApplyPromptSourceTag(
                RenderTemplateOrThrow(
                    "prompt_templates.topic_shift_rule",
                    channel,
                    requiredTemplate,
                    BuildPolicyTemplateVariables(context, string.Empty, string.Empty, string.Empty)),
                true);
        }

        private static string BuildPrimaryObjectiveFromIntent(string unresolvedIntent)
        {
            if (string.IsNullOrWhiteSpace(unresolvedIntent))
            {
                return string.Empty;
            }

            return $"Acknowledge and address unresolved player intent first: {unresolvedIntent.Trim()}";
        }

        private static bool IsOpeningTurnContext(DialogueScenarioContext context)
        {
            if (context?.IsProactive == true)
            {
                return true;
            }

            if (context?.Tags == null || context.Tags.Count == 0)
            {
                return false;
            }

            return context.Tags.Contains("phase:opening")
                || context.Tags.Contains("turn:opening")
                || context.Tags.Contains("opening");
        }

        private static PromptPolicyConfig ResolvePromptPolicyConfig(SystemPromptConfig config)
        {
            return config?.PromptPolicy?.Clone() ?? PromptPolicyConfig.CreateDefault();
        }

        private static Dictionary<string, object> BuildPolicyTemplateVariables(
            DialogueScenarioContext context,
            string primaryObjective,
            string optionalFollowup,
            string unresolvedIntent)
        {
            Dictionary<string, object> variables = BuildSharedPromptTemplateVariables(context, string.Empty);
            variables["dialogue.primary_objective"] = primaryObjective ?? string.Empty;
            variables["dialogue.optional_followup"] = optionalFollowup ?? string.Empty;
            variables["dialogue.latest_unresolved_intent"] = unresolvedIntent ?? string.Empty;
            variables["dialogue.topic_shift_rule"] = "Complete the primary objective first, then allow at most one natural topic extension.";
            return variables;
        }

        private string BuildFactGroundingGuidanceText(SystemPromptConfig config, DialogueScenarioContext context)
        {
            string legacyTemplate = config?.PromptTemplates?.FactGroundingTemplate;
            string channel = ResolveRenderChannel(context);
            string promptChannel = ResolvePromptChannelForContext(context);
            string template = ResolveUnifiedNodeTemplate(promptChannel, "fact_grounding", legacyTemplate);
            string requiredTemplate = RequireTemplateText("prompt_templates.fact_grounding", channel, template);

            return ApplyPromptSourceTag(
                RenderTemplateOrThrow(
                    "prompt_templates.fact_grounding",
                    channel,
                    requiredTemplate,
                    BuildSharedPromptTemplateVariables(context, string.Empty)),
                true);
        }

        private string ResolveFactionPromptText(
            Faction faction,
            SystemPromptConfig config,
            DialogueScenarioContext context)
        {
            string factionPrompt = FactionPromptManager.Instance.GetPrompt(faction?.def?.defName);
            if (!string.IsNullOrWhiteSpace(factionPrompt))
            {
                return ApplyPromptSourceTag(factionPrompt.Trim(), true);
            }

            string legacyTemplate = config?.PromptTemplates?.DiplomacyFallbackRoleTemplate;
            string channel = ResolveRenderChannel(context);
            string promptChannel = ResolvePromptChannelForContext(context);
            string template = ResolveUnifiedNodeTemplate(promptChannel, "diplomacy_fallback_role", legacyTemplate);
            string requiredTemplate = RequireTemplateText("prompt_templates.diplomacy_fallback_role", channel, template);
            Faction resolvedFaction = faction ?? context?.Faction;
            string factionName = resolvedFaction?.Name ?? "Unknown Faction";
            Dictionary<string, object> variables = BuildSharedPromptTemplateVariables(context, string.Empty);
            variables["world.faction.name"] = factionName;
            variables["world.faction"] = resolvedFaction != null
                ? (object)resolvedFaction
                : CreatePreviewFactionPlaceholder(factionName);
            return ApplyPromptSourceTag(
                RenderTemplateOrThrow(
                    "prompt_templates.diplomacy_fallback_role",
                    channel,
                    requiredTemplate,
                    variables),
                true);
        }

        private string BuildSocialCircleActionRuleText(SystemPromptConfig config, DialogueScenarioContext context)
        {
            if (RimChatMod.Settings?.EnableSocialCircle != true)
            {
                return string.Empty;
            }

            string legacyTemplate = config?.PromptTemplates?.SocialCircleActionRuleTemplate;
            string channel = ResolveRenderChannel(context);
            string promptChannel = ResolvePromptChannelForContext(context);
            string template = ResolveUnifiedNodeTemplate(promptChannel, "social_circle_action_rule", legacyTemplate);
            string requiredTemplate = RequireTemplateText("prompt_templates.social_circle_action_rule", channel, template);
            return ApplyPromptSourceTag(
                RenderTemplateOrThrow(
                    "prompt_templates.social_circle_action_rule",
                    channel,
                    requiredTemplate,
                    BuildSharedPromptTemplateVariables(context, string.Empty)),
                true);
        }

        private string BuildRpgRoleSettingText(
            RimChatSettings settings,
            SystemPromptConfig config,
            DialogueScenarioContext context,
            Pawn target)
        {
            string promptChannel = ResolvePromptChannelForContext(context);
            string personaSection = RimChatMod.Settings?.ResolvePromptSectionText(promptChannel, "character_persona");
            if (!string.IsNullOrWhiteSpace(personaSection))
            {
                return ApplyPromptSourceTag(personaSection.Trim(), true);
            }

            Dictionary<string, object> variables = BuildSharedPromptTemplateVariables(context, string.Empty);
            variables["pawn.target.name"] = target?.LabelShort ?? "Unknown";
            variables["pawn.target"] = target;
            string channel = ResolveRenderChannel(context);
            string roleTemplate = ResolveUnifiedNodeTemplate(
                promptChannel,
                "rpg_role_setting_fallback",
                ResolveRpgRoleFallbackTemplate(settings));
            string requiredTemplate = RequireTemplateText("prompt_templates.rpg_role_setting_fallback", channel, roleTemplate);
            string roleText = RenderTemplateOrThrow(
                "prompt_templates.rpg_role_setting_fallback",
                channel,
                requiredTemplate,
                variables);
            return ApplyPromptSourceTag(roleText, true);
        }

        private string BuildRpgRelationshipProfileText(
            RimChatSettings settings,
            Pawn initiator,
            Pawn target,
            DialogueScenarioContext context)
        {
            if (initiator == null || target == null)
            {
                return string.Empty;
            }

            bool kinship = HasAnyBloodRelationBetweenPair(initiator, target);
            string kinshipValue = kinship ? "yes" : "no";
            string romanceState = ResolvePairRomanceState(initiator, target);
            var variables = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["pawn.initiator.name"] = initiator.LabelShort ?? "Unknown",
                ["pawn.target.name"] = target.LabelShort ?? "Unknown",
                ["pawn.relation.kinship"] = kinshipValue,
                ["pawn.relation.romance_state"] = romanceState,
                ["pawn.initiator"] = initiator,
                ["pawn.target"] = target
            };

            string promptChannel = ResolvePromptChannelForContext(context) ?? RimTalkPromptEntryChannelCatalog.RpgDialogue;
            string guidance = RenderTemplateOrThrow(
                "prompt_templates.rpg_kinship_boundary",
                "rpg",
                ResolveUnifiedNodeTemplate(
                    promptChannel,
                    "rpg_kinship_boundary",
                    ResolveRpgKinshipBoundaryRuleTemplate(settings)),
                variables).Trim();
            variables["dialogue.guidance"] = guidance;
            string profileText = RenderTemplateOrThrow(
                "prompt_templates.rpg_relationship_profile",
                "rpg",
                ResolveUnifiedNodeTemplate(
                    promptChannel,
                    "rpg_relationship_profile",
                    ResolveRpgRelationshipProfileTemplate(settings)),
                variables).Trim();
            return ApplyPromptSourceTag(profileText, true);
        }

        private static bool HasAnyBloodRelationBetweenPair(Pawn first, Pawn second)
        {
            return HasAnyBloodRelationOneWay(first, second) || HasAnyBloodRelationOneWay(second, first);
        }

        private static bool HasAnyBloodRelationOneWay(Pawn fromPawn, Pawn toPawn)
        {
            if (fromPawn?.relations?.DirectRelations == null || toPawn == null)
            {
                return false;
            }

            for (int i = 0; i < fromPawn.relations.DirectRelations.Count; i++)
            {
                DirectPawnRelation relation = fromPawn.relations.DirectRelations[i];
                if (relation?.otherPawn != toPawn || relation.def == null)
                {
                    continue;
                }

                if (relation.def.familyByBloodRelation)
                {
                    return true;
                }
            }

            return false;
        }

        private static string ResolvePairRomanceState(Pawn first, Pawn second)
        {
            if (HasPairRelationEitherDirection(first, second, PawnRelationDefOf.Spouse))
            {
                return "spouse";
            }

            if (HasPairRelationEitherDirection(first, second, PawnRelationDefOf.Fiance))
            {
                return "fiance";
            }

            if (HasPairRelationEitherDirection(first, second, PawnRelationDefOf.Lover))
            {
                return "lover";
            }

            if (HasPairRelationEitherDirection(first, second, PawnRelationDefOf.ExSpouse) ||
                HasPairRelationEitherDirection(first, second, PawnRelationDefOf.ExLover))
            {
                return "ex-or-none";
            }

            return "none";
        }

        private static bool HasPairRelationEitherDirection(Pawn first, Pawn second, PawnRelationDef relationDef)
        {
            if (relationDef == null || first == null || second == null)
            {
                return false;
            }

            return first.relations?.DirectRelationExists(relationDef, second) == true ||
                second.relations?.DirectRelationExists(relationDef, first) == true;
        }

        private string BuildRpgApiContractText(
            RimChatSettings settings,
            SystemPromptConfig config,
            DialogueScenarioContext context,
            bool preferCompact)
        {
            if (settings?.EnableRPGAPI != true)
            {
                return string.Empty;
            }

            return BuildTextBlock(sb =>
            {
                RpgApiActionPromptConfig apiPrompt = settings?.RPGApiActionPromptConfig?.Clone() ?? RpgApiActionPromptConfig.CreateFallback();
                if (preferCompact)
                {
                    RpgApiPromptTextBuilder.AppendActionDefinitionsCompact(sb, apiPrompt);
                }
                else
                {
                    RpgApiPromptTextBuilder.AppendActionDefinitions(sb, apiPrompt);
                }

                string formatConstraint = BuildRpgFormatConstraintText(settings, config, context, preferCompact);
                if (!string.IsNullOrWhiteSpace(formatConstraint))
                {
                    sb.AppendLine(ResolveRpgFormatConstraintHeader(settings));
                    sb.AppendLine(formatConstraint);
                    sb.AppendLine();
                }
            });
        }

        private string BuildRpgFormatConstraintText(
            RimChatSettings settings,
            SystemPromptConfig config,
            DialogueScenarioContext context,
            bool preferCompact)
        {
            string promptChannel = ResolvePromptChannelForContext(context);
            string configured = RimChatMod.Settings?.ResolvePromptSectionText(promptChannel, "output_specification")?.Trim();
            string baseConstraint;
            if (!preferCompact)
            {
                baseConstraint = ApplyPromptSourceTag(configured ?? string.Empty, true);
                return AppendRpgActionReliabilityConstraint(baseConstraint, settings, config, context);
            }

            if (!string.IsNullOrWhiteSpace(configured) && configured.Length <= 600)
            {
                baseConstraint = ApplyPromptSourceTag(configured, true);
                return AppendRpgActionReliabilityConstraint(baseConstraint, settings, config, context);
            }

            baseConstraint = ApplyPromptSourceTag(
                ResolveRpgCompactFormatFallback(settings),
                false);

            return AppendRpgActionReliabilityConstraint(baseConstraint, settings, config, context);
        }

        private string AppendRpgActionReliabilityConstraint(
            string baseConstraint,
            RimChatSettings settings,
            SystemPromptConfig config,
            DialogueScenarioContext context)
        {
            string reliabilityRule = ApplyPromptSourceTag(
                ResolveRpgActionReliabilityFallback(settings),
                false);

            if (string.IsNullOrWhiteSpace(baseConstraint))
            {
                return reliabilityRule;
            }

            string marker = ResolveRpgActionReliabilityMarker(settings);
            if (baseConstraint.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return baseConstraint;
            }

            if (baseConstraint.IndexOf(reliabilityRule, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return baseConstraint;
            }

            var sb = new StringBuilder(baseConstraint.Length + reliabilityRule.Length + 2);
            sb.Append(baseConstraint.TrimEnd());
            sb.AppendLine();
            sb.Append(reliabilityRule);
            return sb.ToString();
        }

        private static string ResolveRpgRoleFallbackTemplate(RimChatSettings settings)
        {
            string unified = settings?.ResolvePromptNodeText(
                RimTalkPromptEntryChannelCatalog.RpgDialogue,
                "rpg_role_setting_fallback");
            if (!string.IsNullOrWhiteSpace(unified))
            {
                return unified;
            }

            return PromptUnifiedCatalog.CreateFallback().ResolveNode(
                RimTalkPromptEntryChannelCatalog.RpgDialogue,
                "rpg_role_setting_fallback");
        }

        private static string ResolveRpgFormatConstraintHeader(RimChatSettings settings)
        {
            return "=== FORMAT CONSTRAINT (REQUIRED) ===";
        }

        private static string ResolveRpgCompactFormatFallback(RimChatSettings settings)
        {
            string unified = settings?.ResolvePromptSectionText(
                RimTalkPromptEntryChannelCatalog.RpgDialogue,
                "output_specification");
            if (!string.IsNullOrWhiteSpace(unified))
            {
                return unified;
            }

            return PromptUnifiedCatalog.CreateFallback().ResolveSection(
                RimTalkPromptEntryChannelCatalog.RpgDialogue,
                "output_specification");
        }

        private static string ResolveRpgActionReliabilityFallback(RimChatSettings settings)
        {
            string unified = settings?.ResolvePromptSectionText(
                RimTalkPromptEntryChannelCatalog.RpgDialogue,
                "action_rules");
            if (!string.IsNullOrWhiteSpace(unified))
            {
                return unified;
            }

            return PromptUnifiedCatalog.CreateFallback().ResolveSection(
                RimTalkPromptEntryChannelCatalog.RpgDialogue,
                "action_rules");
        }

        private static string ResolveRpgActionReliabilityMarker(RimChatSettings settings)
        {
            return "Reliability rules:";
        }

        private static string ResolveRpgRelationshipProfileTemplate(RimChatSettings settings)
        {
            string unified = settings?.ResolvePromptNodeText(
                RimTalkPromptEntryChannelCatalog.RpgDialogue,
                "rpg_relationship_profile");
            if (!string.IsNullOrWhiteSpace(unified))
            {
                return unified;
            }

            return PromptUnifiedCatalog.CreateFallback().ResolveNode(
                RimTalkPromptEntryChannelCatalog.RpgDialogue,
                "rpg_relationship_profile");
        }

        private static string ResolveRpgKinshipBoundaryRuleTemplate(RimChatSettings settings)
        {
            string unified = settings?.ResolvePromptNodeText(
                RimTalkPromptEntryChannelCatalog.RpgDialogue,
                "rpg_kinship_boundary");
            if (!string.IsNullOrWhiteSpace(unified))
            {
                return unified;
            }

            return PromptUnifiedCatalog.CreateFallback().ResolveNode(
                RimTalkPromptEntryChannelCatalog.RpgDialogue,
                "rpg_kinship_boundary");
        }

        private static string CompactRpgEnvironmentBlock(string environmentBlock)
        {
            if (string.IsNullOrWhiteSpace(environmentBlock))
            {
                return environmentBlock ?? string.Empty;
            }

            string[] lines = environmentBlock.Replace("\r", string.Empty).Split('\n');
            var sb = new StringBuilder(environmentBlock.Length);
            bool skipWorldview = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();
                if (trimmed.IndexOf("ENVIRONMENT WORLDVIEW", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    skipWorldview = true;
                    continue;
                }

                if (skipWorldview)
                {
                    if (!trimmed.StartsWith("==="))
                    {
                        continue;
                    }

                    skipWorldview = false;
                }

                sb.AppendLine(line);
            }

            return sb.ToString().Trim();
        }

        private string BuildOutputLanguageGuidance(
            RimChatSettings settings,
            SystemPromptConfig config,
            DialogueScenarioContext context)
        {
            string targetLanguage = settings?.GetEffectivePromptLanguage();
            if (string.IsNullOrWhiteSpace(targetLanguage))
            {
                return string.Empty;
            }

            string legacyTemplate = config?.PromptTemplates?.OutputLanguageTemplate;
            string channel = ResolveRenderChannel(context);
            string promptChannel = ResolvePromptChannelForContext(context);
            string template = ResolveUnifiedNodeTemplate(promptChannel, "output_language", legacyTemplate);
            string requiredTemplate = RequireTemplateText("prompt_templates.output_language", channel, template);

            return ApplyPromptSourceTag(
                RenderTemplateOrThrow(
                    "prompt_templates.output_language",
                    channel,
                    requiredTemplate,
                    BuildSharedPromptTemplateVariables(context, targetLanguage)),
                true);
        }

        private static Dictionary<string, object> BuildSharedPromptTemplateVariables(
            DialogueScenarioContext context,
            string targetLanguage)
        {
            string channel = context?.IsRpg == true ? "rpg" : "diplomacy";
            string mode = context?.IsProactive == true ? "proactive" : "manual";
            bool isPreview = IsPreviewScenario(context);
            var variables = CreatePromptVariableSeed();
            variables["ctx.channel"] = channel;
            variables["ctx.mode"] = mode;
            variables["system.target_language"] = targetLanguage ?? string.Empty;
            variables["system.game_language"] = targetLanguage ?? string.Empty;
            variables["world.faction.name"] = context?.Faction?.Name ?? "Unknown Faction";
            variables["world.scene_tags"] = context?.Tags == null ? string.Empty : string.Join(", ", context.Tags.OrderBy(item => item));
            variables["pawn.initiator.name"] = context?.Initiator?.LabelShort ?? "Unknown";
            variables["pawn.target.name"] = context?.Target?.LabelShort ?? "Unknown";
            if (context?.Initiator != null)
            {
                variables["pawn.initiator"] = context.Initiator;
            }
            else if (isPreview)
            {
                variables["pawn.initiator"] = CreatePreviewPawnPlaceholder("PreviewInitiator");
            }

            if (context?.Target != null)
            {
                variables["pawn.target"] = context.Target;
            }
            else if (isPreview)
            {
                variables["pawn.target"] = CreatePreviewPawnPlaceholder("PreviewTarget");
            }

            if (context?.Faction != null)
            {
                variables["world.faction"] = context.Faction;
            }
            else if (isPreview)
            {
                variables["world.faction"] = CreatePreviewFactionPlaceholder("PreviewFaction");
            }

            return variables;
        }

        private static Dictionary<string, object> CreatePromptVariableSeed()
        {
            var variables = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in PromptVariableCatalog.GetAll())
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                variables[path] = string.Empty;
            }

            return variables;
        }

        private static bool IsPreviewScenario(DialogueScenarioContext context)
        {
            return context?.Tags != null &&
                   (context.Tags.Contains("mode:preview") || context.Tags.Contains("scene:preview"));
        }

        private static Dictionary<string, object> CreatePreviewPawnPlaceholder(string name)
        {
            string safeName = string.IsNullOrWhiteSpace(name) ? "PreviewPawn" : name.Trim();
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = safeName,
                ["profile"] = "preview_profile",
                ["labelshort"] = safeName
            };
        }

        private static Dictionary<string, object> CreatePreviewFactionPlaceholder(string name)
        {
            string safeName = string.IsNullOrWhiteSpace(name) ? "PreviewFaction" : name.Trim();
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = safeName,
                ["profile"] = "preview_faction_profile"
            };
        }

        private string RenderPromptNodeTemplate(
            SystemPromptConfig config,
            DialogueScenarioContext context,
            string template,
            string bodyVariableName,
            string bodyText)
        {
            string normalizedBody = bodyText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedBody))
            {
                throw new PromptRenderException(
                    "prompt_templates.node." + bodyVariableName,
                    ResolveRenderChannel(context),
                    new PromptRenderDiagnostic
                    {
                        ErrorCode = PromptRenderErrorCode.TemplateMissing,
                        Message = "Runtime node body is empty for required variable: " + ResolveNodeBodyVariablePath(bodyVariableName)
                    });
            }

            Dictionary<string, object> variables = BuildSharedPromptTemplateVariables(context, string.Empty);
            string namespacedVariable = ResolveNodeBodyVariablePath(bodyVariableName);
            variables[namespacedVariable] = normalizedBody;
            string channel = ResolveRenderChannel(context);
            string templateId = $"prompt_templates.node.{bodyVariableName}";
            string requiredTemplate = RequireTemplateText(templateId, channel, template);
            return ApplyPromptSourceTag(
                RenderTemplateOrThrow(
                    templateId,
                    channel,
                    requiredTemplate,
                    variables),
                true);
        }

        private static string ResolveRenderChannel(DialogueScenarioContext context)
        {
            return context?.IsRpg == true ? "rpg" : "diplomacy";
        }

        private string BuildDiplomacyStrategyDecisionPolicyText()
        {
            const string fallback = "决策优先级顺序：1）格式与语言正确性；2）引用字段正确性；3）事实约束；4）行为安全性与关系限制；5）连贯性与人设风格。";
            return ResolveUnifiedNodeTemplate(RimTalkPromptEntryChannelCatalog.DiplomacyStrategy, "decision_policy", fallback);
        }

        private string BuildDiplomacyStrategyTurnObjectiveText()
        {
            const string fallback = "主目标：{{dialogue.primary_objective}}可选补充：{{ dialogue.optional_followup }}约束条件：优先完成主目标；最多只能切换一次话题。";
            return ResolveUnifiedNodeTemplate(RimTalkPromptEntryChannelCatalog.DiplomacyStrategy, "turn_objective", fallback);
        }

        private string BuildDiplomacyStrategyOutputContractText()
        {
            string fallback =
                "Return exactly one JSON object only.\n" +
                "The first character must be '{' and the last character must be '}'.\n" +
                "Do not output markdown fences, prose, notes, or any extra text.\n" +
                "Required format:\n" +
                "{\"strategy_suggestions\":[{\"strategy_name\":\"\",\"reason\":\"\",\"content\":\"\"},{\"strategy_name\":\"\",\"reason\":\"\",\"content\":\"\"},{\"strategy_name\":\"\",\"reason\":\"\",\"content\":\"\"}]}\n" +
                "Rules:\n" +
                "- Exactly 3 items.\n" +
                "- Output keys must be exactly: strategy_suggestions, strategy_name, reason, content.\n" +
                "- strategy_name <= 6 Chinese characters and must be actionable intent.\n" +
                "- reason must cite at least one fact tag like [F1] and explain causality.\n" +
                "- reason should stay compact for button display.\n" +
                "- content must be a complete sendable line the player can auto-send directly.\n" +
                "- Keep style aligned with the current faction voice and the player's language.\n" +
                "- At least 2 items must explicitly leverage player attributes or current context.\n" +
                "- Never output extra fields such as action, priority, risk_assessment, task, plan, or macro_advice.";
            return ResolveUnifiedNodeTemplate(
                RimTalkPromptEntryChannelCatalog.DiplomacyStrategy,
                "strategy_output_contract",
                fallback);
        }

        private string RenderStrategyNodeTemplate(
            string promptChannel,
            string nodeId,
            string bodyVariableName,
            string bodyText,
            DialogueScenarioContext context)
        {
            string normalizedBody = bodyText?.Trim() ?? string.Empty;
            if (normalizedBody.Length == 0)
            {
                return string.Empty;
            }

            string channel = ResolveRenderChannel(context);
            string template = ResolveUnifiedNodeTemplate(promptChannel, nodeId, "{{ " + bodyVariableName + " }}");
            Dictionary<string, object> variables = BuildSharedPromptTemplateVariables(context, string.Empty);
            variables[bodyVariableName] = normalizedBody;
            return ApplyPromptSourceTag(
                RenderTemplateOrThrow(
                    "prompt_nodes." + nodeId,
                    channel,
                    RequireTemplateText("prompt_nodes." + nodeId, channel, template),
                    variables),
                true);
        }

        private string ResolveUnifiedNodeTemplate(string promptChannel, string nodeId, string fallback)
        {
            string fromCatalog = RimChatMod.Settings?.ResolvePromptNodeText(promptChannel, nodeId);
            if (!string.IsNullOrWhiteSpace(fromCatalog))
            {
                return fromCatalog.Trim();
            }

            return fallback?.Trim() ?? string.Empty;
        }

        private static string ResolvePromptChannelForContext(DialogueScenarioContext context)
        {
            if (context?.IsRpg == true)
            {
                return context.IsProactive
                    ? RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue
                    : RimTalkPromptEntryChannelCatalog.RpgDialogue;
            }

            return context?.IsProactive == true
                ? RimTalkPromptEntryChannelCatalog.ProactiveDiplomacyDialogue
                : RimTalkPromptEntryChannelCatalog.DiplomacyDialogue;
        }

        private static string ResolveNodeBodyVariablePath(string bodyVariableName)
        {
            if (string.IsNullOrWhiteSpace(bodyVariableName))
            {
                return "dialogue.body";
            }

            switch (bodyVariableName.Trim().ToLowerInvariant())
            {
                case "api_limits_body":
                    return "dialogue.api_limits_body";
                case "quest_guidance_body":
                    return "dialogue.quest_guidance_body";
                case "response_contract_body":
                    return "dialogue.response_contract_body";
                default:
                    return "dialogue." + bodyVariableName.Trim().ToLowerInvariant();
            }
        }
    }
}
