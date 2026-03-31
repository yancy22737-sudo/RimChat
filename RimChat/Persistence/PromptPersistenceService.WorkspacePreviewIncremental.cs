using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Config;
using RimChat.Core;
using RimChat.Prompting;
using Verse;

namespace RimChat.Persistence
{
    public partial class PromptPersistenceService
    {
        internal PromptWorkspaceIncrementalPreviewBuildState CreatePromptWorkspaceIncrementalPreviewBuild(
            RimTalkPromptChannel rootChannel,
            string promptChannel)
        {
            string normalizedChannel = PromptSectionSchemaCatalog.NormalizeWorkspaceChannel(promptChannel, rootChannel);
            var state = new PromptWorkspaceIncrementalPreviewBuildState
            {
                RootChannel = rootChannel,
                PromptChannel = normalizedChannel,
                IncludeNodes = !IsSectionOnlyChannel(normalizedChannel)
            };
            state.Sections.AddRange(PromptSectionSchemaCatalog.GetMainChainSections());
            if (state.IncludeNodes)
            {
                state.NodeLayouts.AddRange(GetOrderedNodeLayoutsForPreview(normalizedChannel));
            }

            state.Preview.Stage = PromptWorkspacePreviewBuildStage.Init;
            UpdateBuildProgress(state);
            UpdateBuildSignature(state);
            return state;
        }

        internal void StepPromptWorkspaceIncrementalPreviewBuild(PromptWorkspaceIncrementalPreviewBuildState state)
        {
            if (state == null)
            {
                return;
            }

            if (state.Preview.Stage == PromptWorkspacePreviewBuildStage.Completed ||
                state.Preview.Stage == PromptWorkspacePreviewBuildStage.Failed)
            {
                return;
            }

            try
            {
                StepBuildStateCore(state);
            }
            catch (PromptRenderException ex)
            {
                MarkBuildFailed(state, BuildErrorDiagnostic(ex));
            }
            catch (Exception ex)
            {
                MarkBuildFailed(state, BuildErrorDiagnostic(ex, state.PromptChannel));
            }
        }

        private void StepBuildStateCore(PromptWorkspaceIncrementalPreviewBuildState state)
        {
            switch (state.Preview.Stage)
            {
                case PromptWorkspacePreviewBuildStage.Init:
                    StepInitStage(state);
                    return;
                case PromptWorkspacePreviewBuildStage.Sections:
                    StepSectionStage(state);
                    return;
                case PromptWorkspacePreviewBuildStage.Nodes:
                    StepNodeStage(state);
                    return;
                case PromptWorkspacePreviewBuildStage.Finalize:
                    StepFinalizeStage(state);
                    return;
            }
        }

        private void StepInitStage(PromptWorkspaceIncrementalPreviewBuildState state)
        {
            state.Preview.Blocks.Add(new PromptWorkspacePreviewBlock
            {
                Kind = PromptWorkspacePreviewBlockKind.Context,
                PromptChannel = state.PromptChannel,
                Content = BuildPromptWorkspaceContextBlock(
                    state.PromptChannel,
                    "manual",
                    "{{ runtime.environment }}")
            });
            state.Preview.Stage = state.Sections.Count > 0
                ? PromptWorkspacePreviewBuildStage.Sections
                : state.IncludeNodes && state.NodeLayouts.Count > 0
                    ? PromptWorkspacePreviewBuildStage.Nodes
                    : PromptWorkspacePreviewBuildStage.Finalize;
            UpdateBuildProgress(state);
            UpdateBuildSignature(state);
        }

        private void StepSectionStage(PromptWorkspaceIncrementalPreviewBuildState state)
        {
            if (state.SectionCursor >= state.Sections.Count)
            {
                state.Preview.Stage = state.IncludeNodes && state.NodeLayouts.Count > 0
                    ? PromptWorkspacePreviewBuildStage.Nodes
                    : PromptWorkspacePreviewBuildStage.Finalize;
                UpdateBuildProgress(state);
                UpdateBuildSignature(state);
                return;
            }

            PromptSectionSchemaItem section = state.Sections[state.SectionCursor];
            string rendered = RenderPreviewSectionStep(state.RootChannel, state.PromptChannel, section.Id);
            if (!string.IsNullOrWhiteSpace(rendered))
            {
                state.RenderedSections.Add(new PromptSectionAggregateSection
                {
                    SectionId = section.Id,
                    SectionLabel = section.EnglishName,
                    Content = rendered.Trim()
                });
            }

            state.SectionCursor++;
            UpdateSectionAggregatePreviewBlock(state);
            state.Preview.Stage = state.SectionCursor >= state.Sections.Count
                ? state.IncludeNodes && state.NodeLayouts.Count > 0
                    ? PromptWorkspacePreviewBuildStage.Nodes
                    : PromptWorkspacePreviewBuildStage.Finalize
                : PromptWorkspacePreviewBuildStage.Sections;
            UpdateBuildProgress(state);
            UpdateBuildSignature(state);
        }

        private void StepNodeStage(PromptWorkspaceIncrementalPreviewBuildState state)
        {
            if (state.NodeCursor >= state.NodeLayouts.Count)
            {
                state.Preview.Stage = PromptWorkspacePreviewBuildStage.Finalize;
                UpdateBuildProgress(state);
                UpdateBuildSignature(state);
                return;
            }

            PromptUnifiedNodeLayoutConfig layout = state.NodeLayouts[state.NodeCursor];
            PromptWorkspacePreviewBlock nodeBlock = RenderPreviewNodeStep(
                state.RootChannel,
                state.PromptChannel,
                layout);
            if (nodeBlock != null)
            {
                state.Preview.Blocks.Add(nodeBlock);
            }

            state.NodeCursor++;
            state.Preview.Stage = state.NodeCursor >= state.NodeLayouts.Count
                ? PromptWorkspacePreviewBuildStage.Finalize
                : PromptWorkspacePreviewBuildStage.Nodes;
            UpdateBuildProgress(state);
            UpdateBuildSignature(state);
        }

        private void StepFinalizeStage(PromptWorkspaceIncrementalPreviewBuildState state)
        {
            EnsureFooterBlock(state.Preview.Blocks, state.PromptChannel);
            state.Preview.Blocks = ReorderWorkspacePreviewBlocks(state.Preview.Blocks);
            state.Preview.Stage = PromptWorkspacePreviewBuildStage.Completed;
            UpdateBuildProgress(state);
            UpdateBuildSignature(state);
        }

        private string RenderPreviewSectionStep(
            RimTalkPromptChannel rootChannel,
            string promptChannel,
            string sectionId)
        {
            string template = RimChatMod.Settings?.ResolvePromptSectionText(promptChannel, sectionId) ?? string.Empty;
            bool rawModVariablesSection = IsRpgModVariablesRawOutputSection(rootChannel, promptChannel, sectionId);
            return rawModVariablesSection
                ? RenderRawModVariablesSection(
                    template,
                    rootChannel,
                    promptChannel,
                    deterministicPreview: true,
                    scenarioContext: null,
                    environmentConfig: null,
                    additionalValues: null)
                : RenderUnifiedTemplate(
                    $"prompt_sections.{promptChannel}.{sectionId}",
                    promptChannel,
                    template,
                    rootChannel,
                    deterministicPreview: true,
                    scenarioContext: null,
                    environmentConfig: null,
                    additionalValues: null);
        }

        private PromptWorkspacePreviewBlock RenderPreviewNodeStep(
            RimTalkPromptChannel rootChannel,
            string promptChannel,
            PromptUnifiedNodeLayoutConfig layout)
        {
            if (layout == null)
            {
                return null;
            }

            string nodeId = layout.NodeId ?? string.Empty;
            string template = RimChatMod.Settings?.ResolvePromptNodeText(promptChannel, nodeId) ?? string.Empty;
            string rendered = RenderUnifiedTemplate(
                $"prompt_nodes.{promptChannel}.{nodeId}",
                promptChannel,
                template,
                rootChannel,
                deterministicPreview: true,
                scenarioContext: null,
                environmentConfig: null,
                additionalValues: null);
            if (!layout.Enabled || string.IsNullOrWhiteSpace(rendered))
            {
                return null;
            }

            bool thoughtChain = string.Equals(
                PromptUnifiedNodeSchemaCatalog.NormalizeId(nodeId),
                "thought_chain_node_template",
                StringComparison.OrdinalIgnoreCase);
            if (thoughtChain && RimChatMod.Settings?.IsThoughtChainEnabledForPromptChannel(promptChannel) != true)
            {
                return null;
            }

            return new PromptWorkspacePreviewBlock
            {
                Kind = PromptWorkspacePreviewBlockKind.Node,
                PromptChannel = promptChannel,
                NodeId = nodeId,
                Slot = layout.GetSlot(),
                Order = layout.Order,
                Content = rendered.Trim()
            };
        }

        private static void EnsureFooterBlock(ICollection<PromptWorkspacePreviewBlock> blocks, string promptChannel)
        {
            if (blocks == null || blocks.Any(block => block?.Kind == PromptWorkspacePreviewBlockKind.Footer))
            {
                return;
            }

            blocks.Add(new PromptWorkspacePreviewBlock
            {
                Kind = PromptWorkspacePreviewBlockKind.Footer,
                PromptChannel = promptChannel,
                Content = "</prompt_context>"
            });
        }

        private void UpdateSectionAggregatePreviewBlock(PromptWorkspaceIncrementalPreviewBuildState state)
        {
            PromptSectionAggregate aggregate = BuildSectionAggregateSnapshot(state.PromptChannel, state.RenderedSections);
            string content = aggregate?.RenderedText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            PromptWorkspacePreviewBlock block = BuildSectionAggregateBlock(state.PromptChannel, content, aggregate);
            List<PromptWorkspacePreviewBlock> blocks = state.Preview.Blocks;
            int index = blocks.FindIndex(item => item?.Kind == PromptWorkspacePreviewBlockKind.SectionAggregate);
            if (index >= 0)
            {
                blocks[index] = block;
                return;
            }

            blocks.Add(block);
        }

        private static PromptSectionAggregate BuildSectionAggregateSnapshot(
            string promptChannel,
            IEnumerable<PromptSectionAggregateSection> sections)
        {
            var aggregate = new PromptSectionAggregate
            {
                PromptChannel = promptChannel ?? string.Empty
            };
            aggregate.Sections.AddRange(sections ?? Enumerable.Empty<PromptSectionAggregateSection>());
            aggregate.RenderedText = PromptHierarchyRenderer.Render(
                BuildMainPromptSectionNodeForAggregate(aggregate.Sections));
            return aggregate;
        }

        private List<PromptUnifiedNodeLayoutConfig> GetOrderedNodeLayoutsForPreview(string promptChannel)
        {
            List<PromptUnifiedNodeLayoutConfig> layouts =
                RimChatMod.Settings?.GetPromptNodeLayouts(promptChannel) ??
                PromptUnifiedNodeSchemaCatalog.GetAllowedNodes(promptChannel)
                    .Select(item => PromptUnifiedNodeLayoutDefaults.BuildDefaultLayout(promptChannel, item.Id))
                    .ToList();
            EnsureLayoutsContainAllowedNodes(promptChannel, layouts);
            return layouts
                .Where(item => item != null)
                .OrderBy(item => item.GetSlot())
                .ThenBy(item => item.Order)
                .ThenBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void MarkBuildFailed(
            PromptWorkspaceIncrementalPreviewBuildState state,
            PromptWorkspacePreviewErrorDiagnostic diagnostic)
        {
            state.Preview.ErrorDiagnostic = diagnostic;
            state.Preview.Stage = PromptWorkspacePreviewBuildStage.Failed;
            state.Preview.Blocks.Add(new PromptWorkspacePreviewBlock
            {
                Kind = PromptWorkspacePreviewBlockKind.Error,
                PromptChannel = state.PromptChannel,
                Content = "RimChat_PromptWorkspacePreviewBuild_ErrorBody".Translate(
                    diagnostic?.TemplateId ?? string.Empty,
                    diagnostic?.Channel ?? string.Empty,
                    diagnostic?.ErrorLine ?? 0,
                    diagnostic?.ErrorColumn ?? 0,
                    diagnostic?.Message ?? string.Empty).ToString()
            });
            EnsureFooterBlock(state.Preview.Blocks, state.PromptChannel);
            state.Preview.Blocks = ReorderWorkspacePreviewBlocks(state.Preview.Blocks);
            UpdateBuildProgress(state);
            UpdateBuildSignature(state);
        }

        private static PromptWorkspacePreviewErrorDiagnostic BuildErrorDiagnostic(PromptRenderException ex)
        {
            return new PromptWorkspacePreviewErrorDiagnostic
            {
                TemplateId = ex?.TemplateId ?? string.Empty,
                Channel = ex?.Channel ?? string.Empty,
                ErrorCode = (int)(ex?.ErrorCode ?? PromptRenderErrorCode.RuntimeError),
                ErrorLine = ex?.ErrorLine ?? 0,
                ErrorColumn = ex?.ErrorColumn ?? 0,
                Message = ex?.Message ?? string.Empty
            };
        }

        private static PromptWorkspacePreviewErrorDiagnostic BuildErrorDiagnostic(Exception ex, string channel)
        {
            return new PromptWorkspacePreviewErrorDiagnostic
            {
                TemplateId = "prompt_workspace.preview",
                Channel = channel ?? string.Empty,
                ErrorCode = 0,
                ErrorLine = 0,
                ErrorColumn = 0,
                Message = ex?.Message ?? "unknown_error"
            };
        }

        private static void UpdateBuildProgress(PromptWorkspaceIncrementalPreviewBuildState state)
        {
            PromptWorkspaceStructuredPreview preview = state.Preview;
            preview.TotalSections = state.Sections.Count;
            preview.CompletedSections = Math.Min(state.SectionCursor, preview.TotalSections);
            preview.TotalNodes = state.NodeLayouts.Count;
            preview.CompletedNodes = Math.Min(state.NodeCursor, preview.TotalNodes);
            preview.Total = preview.TotalSections + preview.TotalNodes;
            preview.Completed = preview.CompletedSections + preview.CompletedNodes;
            preview.IsFailed = preview.Stage == PromptWorkspacePreviewBuildStage.Failed;
            preview.IsBuilding = preview.Stage != PromptWorkspacePreviewBuildStage.Completed && !preview.IsFailed;
        }

        private static void UpdateBuildSignature(PromptWorkspaceIncrementalPreviewBuildState state)
        {
            PromptWorkspaceStructuredPreview preview = state.Preview;
            string baseSignature = BuildPreviewSignature(state.PromptChannel, preview.Blocks);
            string progress = "|build:" + (int)preview.Stage +
                ":" + preview.Completed + "/" + preview.Total +
                ":s" + preview.CompletedSections + "/" + preview.TotalSections +
                ":n" + preview.CompletedNodes + "/" + preview.TotalNodes +
                ":failed=" + (preview.IsFailed ? 1 : 0);
            PromptWorkspacePreviewErrorDiagnostic error = preview.ErrorDiagnostic;
            if (error != null)
            {
                progress += ":err=" + error.ErrorCode + ":" + error.ErrorLine + ":" + error.ErrorColumn;
            }

            preview.Signature = baseSignature + progress;
        }
    }
}
