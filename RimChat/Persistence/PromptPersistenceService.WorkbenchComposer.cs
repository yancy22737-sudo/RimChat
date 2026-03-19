using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Config;
using RimChat.Core;
using RimChat.Prompting;

namespace RimChat.Persistence
{
    /// <summary>
    /// Dependencies: unified prompt catalog, prompt template renderer, and workspace preview models.
    /// Responsibility: provide one shared composer for workbench preview and side-channel runtime prompts.
    /// </summary>
    public partial class PromptPersistenceService
    {
        internal string BuildUnifiedChannelSystemPrompt(
            RimTalkPromptChannel rootChannel,
            string promptChannel,
            DialogueScenarioContext scenarioContext,
            EnvironmentPromptConfig environmentConfig,
            IReadOnlyDictionary<string, object> additionalValues = null,
            string payloadTag = "",
            string payloadText = "",
            bool deterministicPreview = false)
        {
            PromptWorkspaceComposeResult composed = ComposePromptWorkspace(
                rootChannel,
                promptChannel,
                includeNodes: !IsSectionOnlyChannel(promptChannel),
                deterministicPreview,
                scenarioContext,
                environmentConfig,
                additionalValues);
            if (!deterministicPreview)
            {
                ValidateRuntimePromptComposition(composed);
            }

            string prompt = RenderStructuredPreviewAsText(composed.Preview);
            if (string.IsNullOrWhiteSpace(payloadTag) || string.IsNullOrWhiteSpace(payloadText))
            {
                return prompt;
            }

            return InjectPromptPayloadBlock(prompt, payloadTag, payloadText);
        }

        private PromptWorkspaceComposeResult ComposePromptWorkspace(
            RimTalkPromptChannel rootChannel,
            string promptChannel,
            bool includeNodes,
            bool deterministicPreview,
            DialogueScenarioContext scenarioContext,
            EnvironmentPromptConfig environmentConfig,
            IReadOnlyDictionary<string, object> additionalValues)
        {
            string normalizedChannel = PromptSectionSchemaCatalog.NormalizeWorkspaceChannel(promptChannel, rootChannel);
            bool effectiveIncludeNodes = includeNodes && !IsSectionOnlyChannel(normalizedChannel);
            PromptSectionAggregate aggregate = BuildPromptSectionAggregateForCompose(
                rootChannel,
                normalizedChannel,
                deterministicPreview,
                scenarioContext,
                environmentConfig,
                additionalValues);
            List<ResolvedPromptNodePlacement> placements = effectiveIncludeNodes
                ? BuildPromptNodePlacementsForCompose(
                    rootChannel,
                    normalizedChannel,
                    deterministicPreview,
                    scenarioContext,
                    environmentConfig,
                    additionalValues)
                : new List<ResolvedPromptNodePlacement>();

            string mode = ResolvePromptModeForCompose(scenarioContext, normalizedChannel);
            var preview = new PromptWorkspaceStructuredPreview();
            preview.Blocks.Add(new PromptWorkspacePreviewBlock
            {
                Kind = PromptWorkspacePreviewBlockKind.Context,
                PromptChannel = normalizedChannel,
                Content = BuildPromptWorkspaceContextBlock(normalizedChannel, mode, "{{ runtime.environment }}")
            });
            if (effectiveIncludeNodes)
            {
                AddPromptWorkspaceNodeBlocks(preview.Blocks, placements, PromptUnifiedNodeSlot.MetadataAfter);
                AddPromptWorkspaceNodeBlocks(preview.Blocks, placements, PromptUnifiedNodeSlot.MainChainBefore);
                AddPromptWorkspaceNodeBlocks(preview.Blocks, placements, PromptUnifiedNodeSlot.MainChainAfter);
                AddPromptWorkspaceNodeBlocks(preview.Blocks, placements, PromptUnifiedNodeSlot.DynamicDataAfter);
                AddPromptWorkspaceNodeBlocks(preview.Blocks, placements, PromptUnifiedNodeSlot.ContractBeforeEnd);
            }

            string sectionPreview = aggregate?.RenderedText?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(sectionPreview))
            {
                preview.Blocks.Add(BuildSectionAggregateBlock(normalizedChannel, sectionPreview, aggregate));
            }

            if (effectiveIncludeNodes)
            {
                AddPromptWorkspaceThoughtChainBlocks(preview.Blocks, placements);
            }

            preview.Blocks.Add(new PromptWorkspacePreviewBlock
            {
                Kind = PromptWorkspacePreviewBlockKind.Footer,
                PromptChannel = normalizedChannel,
                Content = "</prompt_context>"
            });
            preview.Blocks = ReorderWorkspacePreviewBlocks(preview.Blocks);
            preview.Signature = BuildPreviewSignature(normalizedChannel, preview.Blocks);
            return new PromptWorkspaceComposeResult
            {
                PromptChannel = normalizedChannel,
                Aggregate = aggregate,
                Placements = placements,
                Preview = preview
            };
        }

        private PromptSectionAggregate BuildPromptSectionAggregateForCompose(
            RimTalkPromptChannel rootChannel,
            string promptChannel,
            bool deterministicPreview,
            DialogueScenarioContext scenarioContext,
            EnvironmentPromptConfig environmentConfig,
            IReadOnlyDictionary<string, object> additionalValues)
        {
            string normalizedChannel = PromptSectionSchemaCatalog.NormalizeWorkspaceChannel(promptChannel, rootChannel);
            var aggregate = new PromptSectionAggregate
            {
                PromptChannel = normalizedChannel
            };

            foreach (PromptSectionSchemaItem section in PromptSectionSchemaCatalog.GetMainChainSections())
            {
                string template = RimChatMod.Settings?.ResolvePromptSectionText(normalizedChannel, section.Id) ?? string.Empty;
                string rendered = RenderUnifiedTemplate(
                    $"prompt_sections.{normalizedChannel}.{section.Id}",
                    normalizedChannel,
                    template,
                    rootChannel,
                    deterministicPreview,
                    scenarioContext,
                    environmentConfig,
                    additionalValues);
                if (string.IsNullOrWhiteSpace(rendered))
                {
                    continue;
                }

                aggregate.Sections.Add(new PromptSectionAggregateSection
                {
                    SectionId = section.Id,
                    SectionLabel = section.EnglishName,
                    Content = rendered.Trim()
                });
            }

            aggregate.RenderedText = PromptHierarchyRenderer.Render(
                BuildMainPromptSectionNodeForAggregate(aggregate.Sections));
            return aggregate;
        }

        private List<ResolvedPromptNodePlacement> BuildPromptNodePlacementsForCompose(
            RimTalkPromptChannel rootChannel,
            string promptChannel,
            bool deterministicPreview,
            DialogueScenarioContext scenarioContext,
            EnvironmentPromptConfig environmentConfig,
            IReadOnlyDictionary<string, object> additionalValues)
        {
            string normalizedChannel = PromptSectionSchemaCatalog.NormalizeWorkspaceChannel(promptChannel, rootChannel);
            if (TryBuildRuntimeAlignedPreviewNodePlacements(
                    rootChannel,
                    normalizedChannel,
                    deterministicPreview,
                    scenarioContext,
                    out List<ResolvedPromptNodePlacement> runtimePlacements))
            {
                return runtimePlacements;
            }

            List<PromptUnifiedNodeLayoutConfig> layouts =
                RimChatMod.Settings?.GetPromptNodeLayouts(normalizedChannel) ??
                PromptUnifiedNodeSchemaCatalog.GetAllowedNodes(normalizedChannel)
                    .Select(node => PromptUnifiedNodeLayoutDefaults.BuildDefaultLayout(normalizedChannel, node.Id))
                    .ToList();

            var placements = new List<ResolvedPromptNodePlacement>();
            foreach (PromptUnifiedNodeLayoutConfig layout in layouts
                         .Where(item => item != null)
                         .OrderBy(item => item.GetSlot())
                         .ThenBy(item => item.Order)
                         .ThenBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase))
            {
                string nodeId = layout.NodeId ?? string.Empty;
                string template = RimChatMod.Settings?.ResolvePromptNodeText(normalizedChannel, nodeId) ?? string.Empty;
                string rendered = RenderUnifiedTemplate(
                    $"prompt_nodes.{normalizedChannel}.{nodeId}",
                    normalizedChannel,
                    template,
                    rootChannel,
                    deterministicPreview,
                    scenarioContext,
                    environmentConfig,
                    additionalValues);
                placements.Add(new ResolvedPromptNodePlacement
                {
                    PromptChannel = normalizedChannel,
                    NodeId = nodeId,
                    OutputTag = nodeId,
                    Slot = layout.GetSlot(),
                    Order = layout.Order,
                    Enabled = layout.Enabled,
                    Applied = layout.Enabled,
                    Content = rendered
                });
            }

            return placements;
        }

        private bool TryBuildRuntimeAlignedPreviewNodePlacements(
            RimTalkPromptChannel rootChannel,
            string promptChannel,
            bool deterministicPreview,
            DialogueScenarioContext scenarioContext,
            out List<ResolvedPromptNodePlacement> placements)
        {
            placements = null;
            if (!deterministicPreview)
            {
                return false;
            }

            string normalized = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            if (!IsRuntimeMainChainChannel(normalized))
            {
                return false;
            }

            DialogueScenarioContext previewContext = scenarioContext
                ?? CreateDeterministicPreviewScenarioContext(rootChannel, normalized);
            SystemPromptConfig config = LoadConfig() ?? CreateDefaultConfig();
            if (normalized == RimTalkPromptEntryChannelCatalog.DiplomacyStrategy)
            {
                placements = ResolveStrategyNodePlacements(
                    normalized,
                    config,
                    previewContext,
                    new DiplomacyStrategyPromptContext
                    {
                        NegotiatorContextText = "preview_negotiator_context",
                        StrategyFactPackText = "preview_fact_pack",
                        ScenarioDossierText = "preview_scenario_dossier"
                    });
                return true;
            }

            if (normalized == RimTalkPromptEntryChannelCatalog.DiplomacyDialogue ||
                normalized == RimTalkPromptEntryChannelCatalog.ProactiveDiplomacyDialogue)
            {
                placements = ResolveDiplomacyNodePlacements(
                    normalized,
                    config,
                    previewContext,
                    previewContext?.Faction,
                    null);
                return true;
            }

            if (normalized == RimTalkPromptEntryChannelCatalog.RpgDialogue ||
                normalized == RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue)
            {
                placements = ResolveRpgNodePlacements(
                    normalized,
                    RimChatMod.Settings,
                    config,
                    previewContext,
                    null,
                    null,
                    string.Empty,
                    IsOpeningTurnContext(previewContext));
                return true;
            }

            return false;
        }

        private static bool IsRuntimeMainChainChannel(string promptChannel)
        {
            string normalized = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            return normalized == RimTalkPromptEntryChannelCatalog.DiplomacyDialogue
                || normalized == RimTalkPromptEntryChannelCatalog.ProactiveDiplomacyDialogue
                || normalized == RimTalkPromptEntryChannelCatalog.DiplomacyStrategy
                || normalized == RimTalkPromptEntryChannelCatalog.RpgDialogue
                || normalized == RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue;
        }

        private static DialogueScenarioContext CreateDeterministicPreviewScenarioContext(
            RimTalkPromptChannel rootChannel,
            string promptChannel)
        {
            bool proactive = IsProactivePromptChannel(promptChannel);
            if (rootChannel == RimTalkPromptChannel.Rpg ||
                promptChannel == RimTalkPromptEntryChannelCatalog.RpgDialogue ||
                promptChannel == RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue)
            {
                return DialogueScenarioContext.CreateRpg(
                    null,
                    null,
                    proactive,
                    new[] { "channel:" + promptChannel, "mode:preview" });
            }

            return DialogueScenarioContext.CreateDiplomacy(
                null,
                proactive,
                new[] { "channel:" + promptChannel, "mode:preview" });
        }

        private static bool IsProactivePromptChannel(string promptChannel)
        {
            string normalized = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            return normalized.IndexOf("proactive", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsSectionOnlyChannel(string promptChannel)
        {
            string normalized = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            return normalized == RimTalkPromptEntryChannelCatalog.PersonaBootstrap
                || normalized == RimTalkPromptEntryChannelCatalog.SummaryGeneration
                || normalized == RimTalkPromptEntryChannelCatalog.RpgArchiveCompression
                || normalized == RimTalkPromptEntryChannelCatalog.ImageGeneration;
        }

        private string RenderUnifiedTemplate(
            string templateId,
            string promptChannel,
            string templateText,
            RimTalkPromptChannel rootChannel,
            bool deterministicPreview,
            DialogueScenarioContext scenarioContext,
            EnvironmentPromptConfig environmentConfig,
            IReadOnlyDictionary<string, object> additionalValues)
        {
            string template = templateText?.Trim() ?? string.Empty;
            if (template.Length == 0)
            {
                return string.Empty;
            }

            string renderChannel = ResolveTemplateRenderChannel(promptChannel, rootChannel, scenarioContext);
            Dictionary<string, object> values = deterministicPreview
                ? BuildDeterministicComposeValues(promptChannel, scenarioContext, additionalValues)
                : BuildRuntimeComposeValues(templateId, renderChannel, promptChannel, scenarioContext, environmentConfig, additionalValues);
            PromptRenderContext renderContext = PromptRenderContext.Create(templateId, renderChannel);
            renderContext.SetValues(values);
            return PromptTemplateRenderer.RenderOrThrow(templateId, renderChannel, template, renderContext).Trim();
        }

        private Dictionary<string, object> BuildRuntimeComposeValues(
            string templateId,
            string renderChannel,
            string promptChannel,
            DialogueScenarioContext scenarioContext,
            EnvironmentPromptConfig environmentConfig,
            IReadOnlyDictionary<string, object> additionalValues)
        {
            Dictionary<string, object> values = BuildTemplateVariableValues(
                templateId,
                renderChannel,
                scenarioContext,
                environmentConfig);
            InjectRuntimeNodeBodies(values, templateId, promptChannel, scenarioContext);
            values["ctx.channel"] = promptChannel ?? string.Empty;
            values["ctx.mode"] = ResolvePromptModeForCompose(scenarioContext, promptChannel);
            MergeAdditionalValues(values, additionalValues);
            return values;
        }

        private void InjectRuntimeNodeBodies(
            IDictionary<string, object> values,
            string templateId,
            string promptChannel,
            DialogueScenarioContext scenarioContext)
        {
            if (values == null)
            {
                return;
            }

            string normalized = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            if (normalized != RimTalkPromptEntryChannelCatalog.DiplomacyDialogue &&
                normalized != RimTalkPromptEntryChannelCatalog.ProactiveDiplomacyDialogue)
            {
                return;
            }

            var faction = scenarioContext?.Faction;
            string normalizedTemplateId = (templateId ?? string.Empty).Trim();
            if (normalizedTemplateId.EndsWith(".api_limits_node_template", StringComparison.OrdinalIgnoreCase))
            {
                values["dialogue.api_limits_body"] = BuildTextBlock(sb => AppendApiLimits(sb, faction));
                return;
            }

            if (normalizedTemplateId.EndsWith(".quest_guidance_node_template", StringComparison.OrdinalIgnoreCase))
            {
                values["dialogue.quest_guidance_body"] = BuildTextBlock(sb =>
                {
                    AppendDynamicQuestGuidance(sb, faction);
                    AppendQuestSelectionHardRules(sb);
                });
                return;
            }

            if (!normalizedTemplateId.EndsWith(".response_contract_node_template", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SystemPromptConfig config = _cachedConfig ?? LoadConfig() ?? CreateDefaultConfig();
            values["dialogue.response_contract_body"] = BuildTextBlock(sb =>
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
        }

        private static void ValidateRuntimePromptComposition(PromptWorkspaceComposeResult composed)
        {
            if (composed == null)
            {
                throw new PromptRenderException(
                    "prompt_runtime.compose",
                    "unknown",
                    new PromptRenderDiagnostic
                    {
                        ErrorCode = PromptRenderErrorCode.TemplateMissing,
                        Message = "Runtime prompt composition result is null."
                    });
            }

            string channel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(composed.PromptChannel);
            foreach (string nodeId in GetRequiredRuntimeNodeIds(channel))
            {
                string content = FindEnabledNodeContent(composed.Placements, nodeId);
                if (string.IsNullOrWhiteSpace(content))
                {
                    throw new PromptRenderException(
                        "prompt_nodes." + nodeId,
                        channel,
                        new PromptRenderDiagnostic
                        {
                            ErrorCode = PromptRenderErrorCode.TemplateMissing,
                            Message = "Runtime required node is empty or disabled: " + nodeId
                        });
                }
            }
        }

        private static IReadOnlyList<string> GetRequiredRuntimeNodeIds(string promptChannel)
        {
            if (promptChannel == RimTalkPromptEntryChannelCatalog.DiplomacyDialogue ||
                promptChannel == RimTalkPromptEntryChannelCatalog.ProactiveDiplomacyDialogue)
            {
                return new[]
                {
                    "api_limits_node_template",
                    "quest_guidance_node_template",
                    "response_contract_node_template"
                };
            }

            if (promptChannel == RimTalkPromptEntryChannelCatalog.DiplomacyStrategy)
            {
                return new[]
                {
                    "strategy_output_contract",
                    "strategy_player_negotiator_context_template",
                    "strategy_fact_pack_template",
                    "strategy_scenario_dossier_template"
                };
            }

            if (promptChannel == RimTalkPromptEntryChannelCatalog.RpgDialogue ||
                promptChannel == RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue)
            {
                return new[]
                {
                    "fact_grounding",
                    "output_language",
                    "decision_policy",
                    "turn_objective",
                    "rpg_role_setting_fallback"
                };
            }

            return Array.Empty<string>();
        }

        private static string FindEnabledNodeContent(
            IEnumerable<ResolvedPromptNodePlacement> placements,
            string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return string.Empty;
            }

            string targetId = PromptUnifiedNodeSchemaCatalog.NormalizeId(nodeId);
            foreach (ResolvedPromptNodePlacement placement in placements ?? Enumerable.Empty<ResolvedPromptNodePlacement>())
            {
                if (placement == null || !placement.Enabled)
                {
                    continue;
                }

                string candidate = PromptUnifiedNodeSchemaCatalog.NormalizeId(placement.NodeId);
                if (!string.Equals(candidate, targetId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return placement.Content?.Trim() ?? string.Empty;
            }

            return string.Empty;
        }

        private static Dictionary<string, object> BuildDeterministicComposeValues(
            string promptChannel,
            DialogueScenarioContext scenarioContext,
            IReadOnlyDictionary<string, object> additionalValues)
        {
            Dictionary<string, object> values = CreatePromptVariableSeed();
            values["ctx.channel"] = promptChannel ?? string.Empty;
            values["ctx.mode"] = ResolvePromptModeForCompose(scenarioContext, promptChannel);
            values["system.target_language"] = "English";
            values["system.game_language"] = "English";
            values["world.faction.name"] = "PreviewFaction";
            values["world.faction.description"] = "preview_faction_description";
            values["pawn.initiator.name"] = "PreviewInitiator";
            values["pawn.target.name"] = "PreviewTarget";
            values["world.faction"] = CreatePreviewFactionPlaceholder("PreviewFaction");
            values["pawn.initiator"] = CreatePreviewPawnPlaceholder("PreviewInitiator");
            values["pawn.target"] = CreatePreviewPawnPlaceholder("PreviewTarget");
            values["world.scene_tags"] = "scene:preview";
            values["world.environment_params"] = "preview_environment";
            values["world.recent_world_events"] = "preview_events";
            values["dialogue.primary_objective"] = "preview_objective";
            values["dialogue.optional_followup"] = "preview_followup";
            values["dialogue.latest_unresolved_intent"] = string.Empty;
            values["dialogue.api_limits_body"] = "preview_api_limits";
            values["dialogue.quest_guidance_body"] = "preview_quest_guidance";
            values["dialogue.response_contract_body"] = "preview_response_contract";
            MergeAdditionalValues(values, additionalValues);
            return values;
        }

        private static void MergeAdditionalValues(
            IDictionary<string, object> target,
            IReadOnlyDictionary<string, object> additionalValues)
        {
            if (target == null || additionalValues == null)
            {
                return;
            }

            foreach (KeyValuePair<string, object> entry in additionalValues)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    continue;
                }

                target[entry.Key] = entry.Value ?? string.Empty;
            }
        }

        private static PromptHierarchyNode BuildMainPromptSectionNodeForAggregate(
            IEnumerable<PromptSectionAggregateSection> sections)
        {
            var node = new PromptHierarchyNode("main_prompt_sections");
            foreach (PromptSectionAggregateSection section in sections ?? Enumerable.Empty<PromptSectionAggregateSection>())
            {
                if (section == null || string.IsNullOrWhiteSpace(section.Content))
                {
                    continue;
                }

                node.AddChild(section.SectionId, section.Content.Trim());
            }

            return node;
        }

        private static string ResolveTemplateRenderChannel(
            string promptChannel,
            RimTalkPromptChannel rootChannel,
            DialogueScenarioContext scenarioContext)
        {
            if (scenarioContext?.IsRpg == true)
            {
                return "rpg";
            }

            string normalized = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            if (normalized.IndexOf("rpg", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized == RimTalkPromptEntryChannelCatalog.PersonaBootstrap ||
                normalized == RimTalkPromptEntryChannelCatalog.RpgArchiveCompression)
            {
                return "rpg";
            }

            if (normalized == RimTalkPromptEntryChannelCatalog.ImageGeneration)
            {
                return "image";
            }

            if (normalized == RimTalkPromptEntryChannelCatalog.SocialCirclePost)
            {
                return "social";
            }

            if (normalized == RimTalkPromptEntryChannelCatalog.SummaryGeneration)
            {
                return rootChannel == RimTalkPromptChannel.Rpg ? "rpg" : "diplomacy";
            }

            return rootChannel == RimTalkPromptChannel.Rpg ? "rpg" : "diplomacy";
        }

        private static string ResolvePromptModeForCompose(DialogueScenarioContext scenarioContext, string promptChannel)
        {
            if (scenarioContext?.IsProactive == true)
            {
                return "proactive";
            }

            string normalized = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            return normalized.IndexOf("proactive", StringComparison.OrdinalIgnoreCase) >= 0
                ? "proactive"
                : "manual";
        }

        private static string InjectPromptPayloadBlock(string promptText, string payloadTag, string payloadText)
        {
            string tag = SanitizePayloadTag(payloadTag);
            string text = EscapePromptXml(payloadText);
            if (tag.Length == 0 || text.Length == 0)
            {
                return promptText ?? string.Empty;
            }

            string block = "  <" + tag + ">\n    "
                + text.Replace("\r", string.Empty).Replace("\n", "\n    ")
                + "\n  </" + tag + ">";
            string normalized = promptText ?? string.Empty;
            const string footer = "</prompt_context>";
            int index = normalized.LastIndexOf(footer, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return normalized + "\n" + block;
            }

            return normalized.Insert(index, block + "\n\n");
        }

        private static string SanitizePayloadTag(string payloadTag)
        {
            string raw = (payloadTag ?? string.Empty).Trim().ToLowerInvariant();
            if (raw.Length == 0)
            {
                return string.Empty;
            }

            var chars = raw.Where(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-');
            string result = new string(chars.ToArray());
            return result.Length == 0 ? string.Empty : result;
        }

        private static string EscapePromptXml(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }
    }

    internal sealed class PromptWorkspaceComposeResult
    {
        public string PromptChannel = string.Empty;
        public PromptSectionAggregate Aggregate;
        public List<ResolvedPromptNodePlacement> Placements = new List<ResolvedPromptNodePlacement>();
        public PromptWorkspaceStructuredPreview Preview = new PromptWorkspaceStructuredPreview();
    }
}
