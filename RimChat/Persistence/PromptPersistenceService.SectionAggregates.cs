using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimChat.Config;
using RimChat.Core;
using RimChat.Prompting;
using Verse;

namespace RimChat.Persistence
{
    /// <summary>
    /// Dependencies: prompt section aggregate builder, prompt render pipeline, and runtime prompt variable context.
    /// Responsibility: render canonical PromptSectionCatalog aggregates for diplomacy and RPG main-chain prompts.
    /// </summary>
    public partial class PromptPersistenceService
    {
        private PromptHierarchyNode BuildMainChainPromptSectionNode(
            RimTalkPromptChannel rootChannel,
            SystemPromptConfig config,
            DialogueScenarioContext context,
            EnvironmentPromptConfig environmentConfig)
        {
            string promptChannel = PromptSectionSchemaCatalog.ResolveRuntimePromptChannel(
                rootChannel,
                context?.IsProactive == true);
            return BuildPromptSectionAggregateNode(config, promptChannel, context, environmentConfig);
        }

        private PromptHierarchyNode BuildPromptSectionAggregateNode(
            SystemPromptConfig config,
            string promptChannel,
            DialogueScenarioContext context,
            EnvironmentPromptConfig environmentConfig)
        {
            RimTalkPromptEntryDefaultsConfig catalog = GetRuntimePromptSectionCatalog(config);
            PromptSectionAggregate aggregate = PromptSectionAggregateBuilder.Build(
                catalog,
                promptChannel,
                (sectionId, template) => RenderPromptSectionAggregateSection(promptChannel, sectionId, template, context, environmentConfig));

            var root = new PromptHierarchyNode("main_prompt_sections");
            for (int i = 0; i < aggregate.Sections.Count; i++)
            {
                PromptSectionAggregateSection section = aggregate.Sections[i];
                if (section == null || string.IsNullOrWhiteSpace(section.Content))
                {
                    continue;
                }

                root.AddChild(section.SectionId, section.Content.Trim());
            }

            return root.Children.Count > 0 ? root : null;
        }

        internal string BuildPromptSectionAggregatePreview(RimTalkPromptChannel rootChannel, string promptChannel)
        {
            PromptSectionAggregate aggregate = BuildPromptSectionAggregateForPreview(rootChannel, promptChannel);
            return aggregate?.RenderedText?.Trim() ?? string.Empty;
        }

        internal PromptWorkspaceStructuredPreview BuildPromptWorkspaceStructuredSectionPreview(
            RimTalkPromptChannel rootChannel,
            string promptChannel)
        {
            string normalizedChannel = RimTalkPromptEntryChannelCatalog.NormalizeForRoot(promptChannel, rootChannel);
            PromptSectionAggregate aggregate = BuildPromptSectionAggregateForPreview(rootChannel, normalizedChannel);
            string sectionPreview = aggregate?.RenderedText?.Trim() ?? string.Empty;
            var preview = new PromptWorkspaceStructuredPreview();
            preview.Blocks.Add(new PromptWorkspacePreviewBlock
            {
                Kind = PromptWorkspacePreviewBlockKind.Context,
                PromptChannel = normalizedChannel,
                Content = BuildPromptWorkspaceContextBlock(normalizedChannel)
            });
            if (!string.IsNullOrWhiteSpace(sectionPreview))
            {
                preview.Blocks.Add(BuildSectionAggregateBlock(normalizedChannel, sectionPreview, aggregate));
            }

            preview.Blocks.Add(new PromptWorkspacePreviewBlock
            {
                Kind = PromptWorkspacePreviewBlockKind.Footer,
                PromptChannel = normalizedChannel,
                Content = "</prompt_context>"
            });
            preview.Signature = BuildPreviewSignature(normalizedChannel, preview.Blocks);
            return preview;
        }

        internal PromptWorkspaceStructuredPreview BuildPromptWorkspaceStructuredLayoutPreview(
            RimTalkPromptChannel rootChannel,
            string promptChannel,
            out List<ResolvedPromptNodePlacement> placements)
        {
            string normalizedChannel = RimTalkPromptEntryChannelCatalog.NormalizeForRoot(promptChannel, rootChannel);
            List<PromptUnifiedNodeLayoutConfig> layoutConfigs =
                RimChatMod.Settings?.GetPromptNodeLayouts(normalizedChannel) ??
                PromptUnifiedNodeSchemaCatalog.GetAll()
                    .Select(node => PromptUnifiedNodeLayoutDefaults.BuildDefaultLayout(normalizedChannel, node.Id))
                    .ToList();
            placements = layoutConfigs
                .Where(item => item != null)
                .OrderBy(item => item.GetSlot())
                .ThenBy(item => item.Order)
                .ThenBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase)
                .Select(item => new ResolvedPromptNodePlacement
                {
                    PromptChannel = normalizedChannel,
                    NodeId = item.NodeId,
                    OutputTag = item.NodeId,
                    Slot = item.GetSlot(),
                    Order = item.Order,
                    Enabled = item.Enabled,
                    Applied = item.Enabled,
                    Content = RimChatMod.Settings?.ResolvePromptNodeText(normalizedChannel, item.NodeId) ?? string.Empty
                })
                .ToList();

            PromptSectionAggregate aggregate = BuildPromptSectionAggregateForPreview(rootChannel, normalizedChannel);
            string sectionPreview = aggregate?.RenderedText?.Trim() ?? string.Empty;
            var preview = new PromptWorkspaceStructuredPreview();
            preview.Blocks.Add(new PromptWorkspacePreviewBlock
            {
                Kind = PromptWorkspacePreviewBlockKind.Context,
                PromptChannel = normalizedChannel,
                Content = BuildPromptWorkspaceContextBlock(normalizedChannel)
            });

            AddPromptWorkspaceNodeBlocks(preview.Blocks, placements, PromptUnifiedNodeSlot.MetadataAfter);
            AddPromptWorkspaceNodeBlocks(preview.Blocks, placements, PromptUnifiedNodeSlot.MainChainBefore);
            if (!string.IsNullOrWhiteSpace(sectionPreview))
            {
                preview.Blocks.Add(BuildSectionAggregateBlock(normalizedChannel, sectionPreview, aggregate));
            }

            AddPromptWorkspaceNodeBlocks(preview.Blocks, placements, PromptUnifiedNodeSlot.MainChainAfter);
            AddPromptWorkspaceNodeBlocks(preview.Blocks, placements, PromptUnifiedNodeSlot.DynamicDataAfter);
            AddPromptWorkspaceNodeBlocks(preview.Blocks, placements, PromptUnifiedNodeSlot.ContractBeforeEnd);
            preview.Blocks.Add(new PromptWorkspacePreviewBlock
            {
                Kind = PromptWorkspacePreviewBlockKind.Footer,
                PromptChannel = normalizedChannel,
                Content = "</prompt_context>"
            });
            preview.Signature = BuildPreviewSignature(normalizedChannel, preview.Blocks);
            return preview;
        }

        internal string BuildPromptWorkspaceLayoutPreview(
            RimTalkPromptChannel rootChannel,
            string promptChannel,
            out List<ResolvedPromptNodePlacement> placements)
        {
            PromptWorkspaceStructuredPreview preview = BuildPromptWorkspaceStructuredLayoutPreview(
                rootChannel,
                promptChannel,
                out placements);
            return RenderStructuredPreviewAsText(preview);
        }

        private static string BuildPromptWorkspaceContextBlock(string normalizedChannel)
        {
            return "<prompt_context>\n"
                + "  <channel>" + normalizedChannel + "</channel>\n"
                + "  <mode>manual</mode>\n"
                + "  <environment>{{ runtime.environment }}</environment>";
        }

        private PromptSectionAggregate BuildPromptSectionAggregateForPreview(
            RimTalkPromptChannel rootChannel,
            string promptChannel)
        {
            string normalizedChannel = RimTalkPromptEntryChannelCatalog.NormalizeForRoot(promptChannel, rootChannel);
            RimTalkPromptEntryDefaultsConfig catalog = RimChatMod.Settings?.GetPromptSectionCatalogClone()
                                                   ?? RimTalkPromptEntryDefaultsProvider.GetDefaultsSnapshot();
            return PromptSectionAggregateBuilder.Build(
                catalog,
                normalizedChannel,
                (_, template) => template);
        }

        private static PromptWorkspacePreviewBlock BuildSectionAggregateBlock(
            string promptChannel,
            string content,
            PromptSectionAggregate aggregate)
        {
            var block = new PromptWorkspacePreviewBlock
            {
                Kind = PromptWorkspacePreviewBlockKind.SectionAggregate,
                PromptChannel = promptChannel,
                Content = content ?? string.Empty
            };
            block.Subsections.AddRange(BuildSectionAggregateSubsections(aggregate));
            return block;
        }

        private static IEnumerable<PromptWorkspacePreviewSubsection> BuildSectionAggregateSubsections(
            PromptSectionAggregate aggregate)
        {
            foreach (PromptSectionAggregateSection section in aggregate?.Sections ?? Enumerable.Empty<PromptSectionAggregateSection>())
            {
                if (section == null || string.IsNullOrWhiteSpace(section.Content))
                {
                    continue;
                }

                yield return new PromptWorkspacePreviewSubsection
                {
                    SectionId = section.SectionId ?? string.Empty,
                    Content = section.Content.Trim()
                };
            }
        }

        private static void AddPromptWorkspaceNodeBlocks(
            ICollection<PromptWorkspacePreviewBlock> blocks,
            IEnumerable<ResolvedPromptNodePlacement> placements,
            PromptUnifiedNodeSlot slot)
        {
            foreach (ResolvedPromptNodePlacement placement in placements ?? Enumerable.Empty<ResolvedPromptNodePlacement>())
            {
                if (placement == null || placement.Slot != slot || !placement.Enabled)
                {
                    continue;
                }

                string nodeContent = placement.Content?.Trim() ?? string.Empty;
                if (nodeContent.Length == 0)
                {
                    continue;
                }

                blocks.Add(new PromptWorkspacePreviewBlock
                {
                    Kind = PromptWorkspacePreviewBlockKind.Node,
                    PromptChannel = placement.PromptChannel,
                    NodeId = placement.NodeId,
                    Slot = placement.Slot,
                    Order = placement.Order,
                    Content = nodeContent
                });
            }
        }

        private static string BuildPreviewSignature(
            string normalizedChannel,
            IEnumerable<PromptWorkspacePreviewBlock> blocks)
        {
            var sb = new StringBuilder();
            sb.Append("channel=").Append(normalizedChannel ?? string.Empty).Append('|');
            foreach (PromptWorkspacePreviewBlock block in blocks ?? Enumerable.Empty<PromptWorkspacePreviewBlock>())
            {
                if (block == null)
                {
                    continue;
                }

                sb.Append((int)block.Kind).Append(':')
                  .Append(block.NodeId ?? string.Empty).Append(':')
                  .Append(block.Slot.ToSerializedValue()).Append(':')
                  .Append(block.Order).Append(':')
                  .Append(BuildTextSignature(block.Content));

                foreach (PromptWorkspacePreviewSubsection subsection in block.Subsections ?? Enumerable.Empty<PromptWorkspacePreviewSubsection>())
                {
                    if (subsection == null)
                    {
                        continue;
                    }

                    sb.Append(":sub(")
                      .Append(subsection.SectionId ?? string.Empty)
                      .Append(',')
                      .Append(BuildTextSignature(subsection.Content))
                      .Append(')');
                }

                sb
                  .Append('|');
            }

            return sb.ToString();
        }

        private static string BuildTextSignature(string text)
        {
            string normalized = text ?? string.Empty;
            return normalized.Length + ":" + normalized.GetHashCode().ToString("X8");
        }

        private static string RenderStructuredPreviewAsText(PromptWorkspaceStructuredPreview preview)
        {
            if (preview?.Blocks == null || preview.Blocks.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (PromptWorkspacePreviewBlock block in preview.Blocks)
            {
                if (block == null || string.IsNullOrWhiteSpace(block.Content))
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine();
                }

                sb.Append(block.Content.Trim());
            }

            return sb.ToString();
        }

        private string RenderPromptSectionAggregateSection(
            string promptChannel,
            string sectionId,
            string templateText,
            DialogueScenarioContext context,
            EnvironmentPromptConfig environmentConfig)
        {
            string normalized = templateText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            string renderChannel = context?.IsRpg == true ? "rpg" : "diplomacy";
            string templateId = $"prompt_sections.{promptChannel}.{sectionId}";
            Dictionary<string, object> values = BuildTemplateVariableValues(
                templateId,
                renderChannel,
                context,
                environmentConfig);
            PromptRenderContext renderContext = PromptRenderContext.Create(templateId, renderChannel);
            renderContext.SetValues(values);
            return PromptTemplateRenderer.RenderOrThrow(templateId, renderChannel, normalized, renderContext).Trim();
        }

        private RimTalkPromptEntryDefaultsConfig GetRuntimePromptSectionCatalog(SystemPromptConfig config)
        {
            return RimChatMod.Settings?.GetPromptSectionCatalogClone()
                ?? RimTalkPromptEntryDefaultsProvider.GetDefaultsSnapshot();
        }


        private bool SyncLegacyPromptMirrorsFromSections(SystemPromptConfig config)
        {
            if (config == null)
            {
                return false;
            }

            string systemMirror = BuildLegacyPromptMirrorText(
                RimTalkPromptEntryChannelCatalog.DiplomacyDialogue,
                "system_rules",
                "action_rules",
                "output_specification");
            string dialogueMirror = BuildLegacyPromptMirrorText(
                RimTalkPromptEntryChannelCatalog.DiplomacyDialogue,
                "character_persona",
                "memory_system",
                "environment_perception",
                "context",
                "repetition_reinforcement");

            bool changed = false;
            if (!string.Equals(config.GlobalSystemPrompt ?? string.Empty, systemMirror, StringComparison.Ordinal))
            {
                config.GlobalSystemPrompt = systemMirror;
                changed = true;
            }

            if (!string.Equals(config.GlobalDialoguePrompt ?? string.Empty, dialogueMirror, StringComparison.Ordinal))
            {
                config.GlobalDialoguePrompt = dialogueMirror;
                changed = true;
            }

            config.UseHierarchicalPromptFormat = true;
            return changed;
        }

        private string BuildLegacyPromptMirrorText(string promptChannel, params string[] sectionIds)
        {
            RimTalkPromptEntryDefaultsConfig catalog = RimChatMod.Settings?.GetPromptSectionCatalogClone()
                                                   ?? RimTalkPromptEntryDefaultsProvider.GetDefaultsSnapshot();
            var parts = new List<string>();
            for (int i = 0; i < sectionIds.Length; i++)
            {
                string text = catalog.ResolveContent(promptChannel, sectionIds[i])?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(text);
                }
            }

            return string.Join("\n\n", parts).Trim();
        }
    }
}
