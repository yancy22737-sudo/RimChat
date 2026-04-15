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
            state.Sections.AddRange(GetOrderedSectionsForPreview(normalizedChannel));
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
            MarkBlockDirty(state, state.Preview.Blocks.Count - 1);
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

            EnsureBuildStateComposeValues(state);
            PromptSectionSchemaItem section = state.Sections[state.SectionCursor];
            string rendered = RenderPreviewSectionStep(state.RootChannel, state.PromptChannel, section.Id, state.CachedComposeValues);
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
            int aggregateBlockIndex = UpdateSectionAggregatePreviewBlock(state);
            if (aggregateBlockIndex >= 0)
            {
                MarkBlockDirty(state, aggregateBlockIndex);
            }
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

            EnsureBuildStateComposeValues(state);
            PromptUnifiedNodeLayoutConfig layout = state.NodeLayouts[state.NodeCursor];
            PromptWorkspacePreviewBlock nodeBlock = RenderPreviewNodeStep(
                state.RootChannel,
                state.PromptChannel,
                layout,
                state.CachedComposeValues);
            if (nodeBlock != null)
            {
                state.Preview.Blocks.Add(nodeBlock);
                MarkBlockDirty(state, state.Preview.Blocks.Count - 1);
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
            InvalidateIncrementalSignatureCache(state);
            state.Preview.UsesSnapshotData = PromptRequestSnapshotCache.HasSnapshotForChannel(state.PromptChannel);
            state.Preview.Stage = PromptWorkspacePreviewBuildStage.Completed;
            UpdateBuildProgress(state);
            UpdateBuildSignature(state);
        }

        private string RenderPreviewSectionStep(
            RimTalkPromptChannel rootChannel,
            string promptChannel,
            string sectionId,
            Dictionary<string, object> cachedComposeValues)
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
                    additionalValues: null,
                    cachedComposeValues: cachedComposeValues)
                : RenderUnifiedTemplate(
                    $"prompt_sections.{promptChannel}.{sectionId}",
                    promptChannel,
                    template,
                    rootChannel,
                    deterministicPreview: true,
                    scenarioContext: null,
                    environmentConfig: null,
                    additionalValues: null,
                    cachedComposeValues: cachedComposeValues);
        }

        private PromptWorkspacePreviewBlock RenderPreviewNodeStep(
            RimTalkPromptChannel rootChannel,
            string promptChannel,
            PromptUnifiedNodeLayoutConfig layout,
            Dictionary<string, object> cachedComposeValues)
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
                additionalValues: null,
                cachedComposeValues: cachedComposeValues);
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

        private static void EnsureBuildStateComposeValues(PromptWorkspaceIncrementalPreviewBuildState state)
        {
            if (state.ComposeValuesInitialized)
            {
                return;
            }

            state.CachedComposeValues = BuildDeterministicComposeValues(
                state.PromptChannel,
                scenarioContext: null,
                additionalValues: null);
            state.ComposeValuesInitialized = true;
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

        private int UpdateSectionAggregatePreviewBlock(PromptWorkspaceIncrementalPreviewBuildState state)
        {
            PromptSectionAggregate aggregate = BuildSectionAggregateSnapshot(state.PromptChannel, state.RenderedSections);
            string content = aggregate?.RenderedText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(content))
            {
                return -1;
            }

            PromptWorkspacePreviewBlock block = BuildSectionAggregateBlock(state.PromptChannel, content, aggregate);
            List<PromptWorkspacePreviewBlock> blocks = state.Preview.Blocks;
            int index = blocks.FindIndex(item => item?.Kind == PromptWorkspacePreviewBlockKind.SectionAggregate);
            if (index >= 0)
            {
                blocks[index] = block;
                return index;
            }

            blocks.Add(block);
            return blocks.Count - 1;
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

        private IReadOnlyList<PromptSectionSchemaItem> GetOrderedSectionsForPreview(string promptChannel)
        {
            List<PromptSectionLayoutConfig> sectionLayouts =
                RimChatMod.Settings?.GetPromptSectionLayouts(promptChannel) ?? new List<PromptSectionLayoutConfig>();
            return PromptSectionSchemaCatalog.GetOrderedMainChainSections(sectionLayouts, enabledOnly: true);
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
            InvalidateIncrementalSignatureCache(state);
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
            string baseSignature = UpdatePreviewSignatureIncremental(state);
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

        private static string UpdatePreviewSignatureIncremental(PromptWorkspaceIncrementalPreviewBuildState state)
        {
            PromptWorkspaceStructuredPreview preview = state.Preview;
            if (!state.SignatureCacheInitialized)
            {
                state.DirtyBlockIndices.Clear();
                for (int i = 0; i < preview.Blocks.Count; i++)
                {
                    state.DirtyBlockIndices.Add(i);
                }

                state.SignatureCacheInitialized = true;
            }

            EnsureBlockSignatureCacheCapacity(state, preview.Blocks.Count);
            foreach (int dirtyIndex in state.DirtyBlockIndices.ToList())
            {
                if (dirtyIndex < 0 || dirtyIndex >= preview.Blocks.Count)
                {
                    continue;
                }

                PromptWorkspacePreviewBlock block = preview.Blocks[dirtyIndex];
                state.BlockSignatureHashes[dirtyIndex] = ComputePreviewBlockSignatureHash(state, dirtyIndex, block);
            }

            state.DirtyBlockIndices.Clear();
            int aggregateHash = ComputePreviewAggregateHash(state.PromptChannel, state.BlockSignatureHashes, preview.Blocks.Count);
            return "channel=" + (state.PromptChannel ?? string.Empty) +
                "|agg=" + aggregateHash.ToString("X8") +
                "|blocks=" + preview.Blocks.Count;
        }

        private static void EnsureBlockSignatureCacheCapacity(PromptWorkspaceIncrementalPreviewBuildState state, int count)
        {
            count = Math.Max(0, count);
            while (state.BlockSignatureHashes.Count < count)
            {
                state.BlockSignatureHashes.Add(0);
            }

            if (state.BlockSignatureHashes.Count > count)
            {
                state.BlockSignatureHashes.RemoveRange(count, state.BlockSignatureHashes.Count - count);
            }

            if (state.SubsectionSignatureHashesByBlock.Count == 0)
            {
                return;
            }

            var staleKeys = state.SubsectionSignatureHashesByBlock.Keys
                .Where(key => key < 0 || key >= count)
                .ToList();
            for (int i = 0; i < staleKeys.Count; i++)
            {
                state.SubsectionSignatureHashesByBlock.Remove(staleKeys[i]);
            }
        }

        private static void InvalidateIncrementalSignatureCache(PromptWorkspaceIncrementalPreviewBuildState state)
        {
            state.SignatureCacheInitialized = false;
            state.DirtyBlockIndices.Clear();
            state.SubsectionSignatureHashesByBlock.Clear();
        }

        private static void MarkBlockDirty(PromptWorkspaceIncrementalPreviewBuildState state, int index)
        {
            if (state == null || index < 0)
            {
                return;
            }

            state.DirtyBlockIndices.Add(index);
        }

        private static int ComputePreviewBlockSignatureHash(
            PromptWorkspaceIncrementalPreviewBuildState state,
            int blockIndex,
            PromptWorkspacePreviewBlock block)
        {
            if (block == null)
            {
                return 0;
            }

            int hash = BeginHash();
            hash = MixHash(hash, (int)block.Kind);
            hash = MixHash(hash, ComputeStableSignatureHash(block.PromptChannel));
            hash = MixHash(hash, ComputeStableSignatureHash(block.NodeId));
            hash = MixHash(hash, (int)block.Slot);
            hash = MixHash(hash, block.Order);
            hash = MixHash(hash, ComputeStableSignatureHash(block.Content));

            List<PromptWorkspacePreviewSubsection> subsections = block.Subsections ?? new List<PromptWorkspacePreviewSubsection>();
            List<int> subsectionCache;
            if (!state.SubsectionSignatureHashesByBlock.TryGetValue(blockIndex, out subsectionCache))
            {
                subsectionCache = new List<int>();
                state.SubsectionSignatureHashesByBlock[blockIndex] = subsectionCache;
            }

            while (subsectionCache.Count < subsections.Count)
            {
                subsectionCache.Add(0);
            }

            if (subsectionCache.Count > subsections.Count)
            {
                subsectionCache.RemoveRange(subsections.Count, subsectionCache.Count - subsections.Count);
            }

            hash = MixHash(hash, subsections.Count);
            for (int i = 0; i < subsections.Count; i++)
            {
                PromptWorkspacePreviewSubsection subsection = subsections[i];
                int subsectionHash = ComputePreviewSubsectionSignatureHash(subsection);
                subsectionCache[i] = subsectionHash;
                hash = MixHash(hash, subsectionHash);
            }

            return hash;
        }

        private static int ComputePreviewSubsectionSignatureHash(PromptWorkspacePreviewSubsection subsection)
        {
            if (subsection == null)
            {
                return 0;
            }

            int hash = BeginHash();
            hash = MixHash(hash, ComputeStableSignatureHash(subsection.SectionId));
            hash = MixHash(hash, ComputeStableSignatureHash(subsection.Content));
            return hash;
        }

        private static int ComputePreviewAggregateHash(string channel, List<int> blockHashes, int count)
        {
            int hash = BeginHash();
            hash = MixHash(hash, ComputeStableSignatureHash(channel));
            hash = MixHash(hash, count);
            for (int i = 0; i < count; i++)
            {
                hash = MixHash(hash, i);
                hash = MixHash(hash, blockHashes[i]);
            }

            return hash;
        }

        private static int BeginHash()
        {
            unchecked
            {
                return (int)2166136261;
            }
        }

        private static int MixHash(int hash, int value)
        {
            unchecked
            {
                hash ^= value;
                hash *= 16777619;
                return hash;
            }
        }

        private static int ComputeStableSignatureHash(string text)
        {
            unchecked
            {
                int hash = BeginHash();
                string source = text ?? string.Empty;
                for (int i = 0; i < source.Length; i++)
                {
                    hash = MixHash(hash, source[i]);
                }

                return hash;
            }
        }
    }
}
