using System;
using System.Collections.Generic;
using RimChat.Config;
using RimChat.Persistence;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: structured preview model, node schema labels, Verse GUI APIs, and CachedRenderTexture.
    /// Responsibility: render lightweight structured prompt preview blocks with layout caching
    /// and RenderTexture offscreen caching to reduce per-frame DrawCall count.
    /// </summary>
    internal sealed class PromptWorkspaceStructuredPreviewRenderer : IDisposable
    {
        private const float StatusHeight = 30f;
        private const float StatusGap = 6f;
        private const float MinContentHeight = 24f;
        private const float HeaderPadding = 4f;
        private const float BodyPadding = 6f;
        private const float BlockGap = 6f;
        private const float SubsectionHeaderPadding = 4f;
        private const float SubsectionIndent = 8f;
        private const float SubsectionGap = 6f;

        private string _cachedSignature = string.Empty;
        private float _cachedWidth = -1f;
        private readonly List<float> _cachedBodyHeights = new List<float>();
        private readonly List<float> _cachedHeaderHeights = new List<float>();
        private float _cachedContentHeight = MinContentHeight;
        internal float CachedContentHeight => _cachedContentHeight;
        private GUIStyle _headerStyle;
        private GUIStyle _subHeaderStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _indicatorStyle;
        // Subsection-level height cache: block index -> list of (headerHeight, contentHeight)
        private readonly List<List<SubsectionLayoutEntry>> _cachedSubsectionLayouts = new List<List<SubsectionLayoutEntry>>();

        // RenderTexture cache for offscreen rendering (reduces DrawCalls from N to 1)
        private readonly CachedRenderTexture _rtCache = new CachedRenderTexture();

        private struct SubsectionLayoutEntry
        {
            public float HeaderHeight;
            public float ContentHeight;
        }

        // Static color constants to avoid per-frame allocations
        private static readonly Color StatusBg = new Color(0.10f, 0.12f, 0.15f);
        private static readonly Color ProgressBarBg = new Color(0.20f, 0.22f, 0.24f);
        private static readonly Color ProgressError = new Color(0.72f, 0.20f, 0.20f);
        private static readonly Color ProgressSuccess = new Color(0.28f, 0.62f, 0.35f);
        private static readonly Color SubtitleBg = new Color(0.16f, 0.18f, 0.13f);
        private static readonly Color SnapshotLiveBg = new Color(0.12f, 0.18f, 0.12f);
        private static readonly Color SnapshotLiveText = new Color(0.45f, 0.80f, 0.45f);
        private static readonly Color SnapshotPlaceholderBg = new Color(0.14f, 0.14f, 0.16f);
        private static readonly Color SnapshotPlaceholderText = new Color(0.55f, 0.55f, 0.55f);
        private static readonly Color BlockBgSystemRules = new Color(0.22f, 0.30f, 0.40f);
        private static readonly Color BlockBgCharacter = new Color(0.25f, 0.28f, 0.18f);
        private static readonly Color BlockBgGeneric = new Color(0.22f, 0.22f, 0.22f);
        private static readonly Color BlockBgActionRules = new Color(0.40f, 0.18f, 0.18f);
        private static readonly Color BlockBgOutputSpec = new Color(0.20f, 0.24f, 0.30f);

        /// <summary>
        /// Mark the RenderTexture cache as dirty so it will be redrawn on next frame.
        /// Call this when preview data or scroll position changes.
        /// </summary>
        internal void MarkDirty()
        {
            _rtCache.MarkDirty();
        }

        internal void Draw(
            Rect rect,
            PromptWorkspaceStructuredPreview preview,
            ref Vector2 scroll)
        {
            // Direct IMGUI rendering — skips RenderTexture cache so scrolling works
            DrawDirect(rect, preview, ref scroll);
        }

        internal void DrawDirect(
            Rect rect,
            PromptWorkspaceStructuredPreview preview,
            ref Vector2 scroll)
        {
            string signature = BuildRenderSignature(preview, rect);
            _rtCache.MarkDirtyIfChanged(signature);
            DrawContent(rect, preview, ref scroll, _rtCache.Dirty);
        }

        private string BuildRenderSignature(PromptWorkspaceStructuredPreview preview, Rect rect)
        {
            string previewSig = preview?.Signature ?? string.Empty;
            string stageSig = preview?.Stage.ToString() ?? "null";
            string progressSig = $"{preview?.Completed ?? 0}/{preview?.Total ?? 0}";
            return $"{previewSig}|{stageSig}|{progressSig}|{rect.width:F0}|{rect.height:F0}";
        }

        private void DrawContent(
            Rect rect,
            PromptWorkspaceStructuredPreview preview,
            ref Vector2 scroll,
            bool needsLayoutRecalc)
        {
            EnsureStyles();
            List<PromptWorkspacePreviewBlock> blocks = preview?.Blocks ?? new List<PromptWorkspacePreviewBlock>();
            float width = Mathf.Max(1f, rect.width - 16f);

            // Repaint: full rendering with layout calc. Layout: only ScrollView shell.
            if (Event.current.type != EventType.Repaint)
            {
                scroll = GUI.BeginScrollView(rect, scroll,
                    new Rect(0f, 0f, width, Mathf.Max(1f, _cachedContentHeight)), false, true);
                GUI.EndScrollView();
                return;
            }

            string signature = preview?.Signature ?? string.Empty;
            if (needsLayoutRecalc) EnsureLayoutCache(signature, blocks, width);

            Rect contentRect = ResolveContentRectWithStatus(rect, preview);
            Rect viewRect = new Rect(0f, 0f, width, _cachedContentHeight);
            float maxScrollY = Mathf.Max(0f, viewRect.height - contentRect.height);
            scroll = new Vector2(0f, Mathf.Clamp(scroll.y, 0f, maxScrollY));
            scroll = GUI.BeginScrollView(contentRect, scroll, viewRect, false, true);

            if (blocks.Count > 0)
            {
                float effectiveY = 0f;
                if (preview != null && preview.Stage == PromptWorkspacePreviewBuildStage.Completed)
                {
                    DrawSnapshotIndicator(new Rect(0f, effectiveY, width, 20f), preview.UsesSnapshotData);
                    effectiveY += 22f;
                }

                for (int i = 0; i < blocks.Count; i++)
                {
                    PromptWorkspacePreviewBlock block = blocks[i];
                    float headerHeight = i < _cachedHeaderHeights.Count
                        ? _cachedHeaderHeights[i]
                        : ResolveHeaderHeight(ResolveHeaderText(block), width);
                    float bodyHeight = i < _cachedBodyHeights.Count
                        ? _cachedBodyHeights[i]
                        : ResolveBodyHeight(block, width);

                    Rect headerRect = new Rect(0f, effectiveY, width, headerHeight);
                    Widgets.DrawBoxSolid(headerRect, ResolveHeaderColor(block));
                    GUI.Label(new Rect(headerRect.x + HeaderPadding, headerRect.y + 1f,
                        headerRect.width - HeaderPadding * 2f, headerRect.height - 2f),
                        ResolveHeaderText(block), _headerStyle);
                    effectiveY += headerHeight;

                    Rect bodyRect = new Rect(0f, effectiveY, width, bodyHeight);
                    DrawBodyContent(bodyRect, block, i);
                    effectiveY += bodyHeight + BlockGap;
                }
            }
            else
            {
                Widgets.Label(new Rect(0f, 0f, width, 24f), "RimChat_PromptWorkbench_PreviewEmpty".Translate());
            }

            GUI.EndScrollView();
        }

        private Rect ResolveContentRectWithStatus(Rect rect, PromptWorkspaceStructuredPreview preview)
        {
            if (!ShouldDrawStatus(preview))
            {
                return rect;
            }

            Rect statusRect = new Rect(rect.x, rect.y, rect.width, StatusHeight);
            Widgets.DrawBoxSolid(statusRect, StatusBg);
            float progress = ResolveProgress(preview);
            Rect barRect = new Rect(statusRect.x + 8f, statusRect.y + 6f, Mathf.Max(1f, statusRect.width - 16f), 8f);
            Widgets.DrawBoxSolid(barRect, ProgressBarBg);
            Widgets.DrawBoxSolid(
                new Rect(barRect.x, barRect.y, barRect.width * progress, barRect.height),
                preview?.IsFailed == true ? ProgressError : ProgressSuccess);
            GUI.Label(
                new Rect(statusRect.x + 8f, barRect.yMax + 1f, statusRect.width - 16f, statusRect.height - 16f),
                ResolveStatusText(preview),
                _subHeaderStyle);
            return new Rect(rect.x, statusRect.yMax + StatusGap, rect.width, Mathf.Max(1f, rect.height - StatusHeight - StatusGap));
        }

        private static bool ShouldDrawStatus(PromptWorkspaceStructuredPreview preview)
        {
            return preview != null &&
                (preview.IsBuilding || preview.IsFailed || preview.Total > 0 || preview.Stage == PromptWorkspacePreviewBuildStage.Completed);
        }

        private static float ResolveProgress(PromptWorkspaceStructuredPreview preview)
        {
            if (preview == null)
            {
                return 0f;
            }

            if (preview.Total <= 0)
            {
                return preview.IsBuilding ? 0f : 1f;
            }

            return Mathf.Clamp01((float)preview.Completed / preview.Total);
        }

        private static string ResolveStatusText(PromptWorkspaceStructuredPreview preview)
        {
            if (preview == null)
            {
                return string.Empty;
            }

            string stage = ResolveStageLabel(preview.Stage);
            if (preview.IsFailed)
            {
                return "RimChat_PromptWorkspacePreviewBuild_StatusFailed"
                    .Translate(stage, preview.Completed, preview.Total)
                    .ToString();
            }

            if (preview.IsBuilding)
            {
                return "RimChat_PromptWorkspacePreviewBuild_StatusBuilding"
                    .Translate(
                        stage,
                        preview.Completed,
                        preview.Total,
                        preview.CompletedSections,
                        preview.TotalSections,
                        preview.CompletedNodes,
                        preview.TotalNodes)
                    .ToString();
            }

            return "RimChat_PromptWorkspacePreviewBuild_StatusCompleted"
                .Translate(preview.Completed, preview.Total)
                .ToString();
        }

        private static string ResolveStageLabel(PromptWorkspacePreviewBuildStage stage)
        {
            switch (stage)
            {
                case PromptWorkspacePreviewBuildStage.Init:
                    return "RimChat_PromptWorkspacePreviewBuild_StageInit".Translate().ToString();
                case PromptWorkspacePreviewBuildStage.Sections:
                    return "RimChat_PromptWorkspacePreviewBuild_StageSections".Translate().ToString();
                case PromptWorkspacePreviewBuildStage.Nodes:
                    return "RimChat_PromptWorkspacePreviewBuild_StageNodes".Translate().ToString();
                case PromptWorkspacePreviewBuildStage.Finalize:
                    return "RimChat_PromptWorkspacePreviewBuild_StageFinalize".Translate().ToString();
                case PromptWorkspacePreviewBuildStage.Failed:
                    return "RimChat_PromptWorkspacePreviewBuild_StageFailed".Translate().ToString();
                default:
                    return "RimChat_PromptWorkspacePreviewBuild_StageCompleted".Translate().ToString();
            }
        }

        private void EnsureLayoutCache(
            string signature,
            IReadOnlyList<PromptWorkspacePreviewBlock> blocks,
            float width)
        {
            if (string.Equals(_cachedSignature, signature, StringComparison.Ordinal) &&
                Mathf.Abs(_cachedWidth - width) < 0.5f)
            {
                return;
            }

            _cachedSignature = signature ?? string.Empty;
            _cachedWidth = width;
            _cachedHeaderHeights.Clear();
            _cachedBodyHeights.Clear();
            _cachedSubsectionLayouts.Clear();

            float contentHeight = 0f;
            for (int i = 0; i < blocks.Count; i++)
            {
                PromptWorkspacePreviewBlock block = blocks[i];
                string headerText = ResolveHeaderText(block);
                float headerHeight = ResolveHeaderHeight(headerText, width);
                float bodyHeight;
                List<SubsectionLayoutEntry> subsectionEntries = null;
                if (HasSubsections(block))
                {
                    subsectionEntries = new List<SubsectionLayoutEntry>();
                    bodyHeight = ResolveSubsectionBodyHeightCached(block, width, subsectionEntries);
                }
                else
                {
                    bodyHeight = ResolveBodyHeight(block, width);
                }

                _cachedHeaderHeights.Add(headerHeight);
                _cachedBodyHeights.Add(bodyHeight);
                _cachedSubsectionLayouts.Add(subsectionEntries);
                contentHeight += headerHeight + bodyHeight + BlockGap;
            }

            _cachedContentHeight = Mathf.Max(MinContentHeight, contentHeight + 4f);
        }

        private float ResolveHeaderHeight(string text, float width)
        {
            return Mathf.Max(20f, _headerStyle.CalcHeight(new GUIContent(text ?? string.Empty), Mathf.Max(1f, width)));
        }

        private float ResolveBodyHeight(PromptWorkspacePreviewBlock block, float width)
        {
            if (HasSubsections(block))
            {
                return ResolveSubsectionBodyHeight(block, width);
            }

            string text = block?.Content ?? string.Empty;
            return Mathf.Max(16f, _bodyStyle.CalcHeight(new GUIContent(text), Mathf.Max(1f, width - BodyPadding * 2f)));
        }

        private float ResolveSubsectionBodyHeightCached(
            PromptWorkspacePreviewBlock block,
            float width,
            List<SubsectionLayoutEntry> entries)
        {
            float subsectionWidth = Mathf.Max(1f, width - BodyPadding * 2f);
            float subsectionContentWidth = Mathf.Max(1f, subsectionWidth - SubsectionIndent);
            float totalHeight = 0f;
            int subsectionCount = 0;
            foreach (PromptWorkspacePreviewSubsection subsection in block.Subsections ?? new List<PromptWorkspacePreviewSubsection>())
            {
                if (subsection == null || string.IsNullOrWhiteSpace(subsection.Content))
                {
                    continue;
                }

                float headerHeight = ResolveSubsectionHeaderHeight(ResolveSubsectionTitle(subsection), subsectionWidth);
                float contentHeight = Mathf.Max(
                    16f,
                    _bodyStyle.CalcHeight(new GUIContent(subsection.Content), subsectionContentWidth));
                entries.Add(new SubsectionLayoutEntry { HeaderHeight = headerHeight, ContentHeight = contentHeight });
                totalHeight += headerHeight + contentHeight + SubsectionGap;
                subsectionCount++;
            }

            if (subsectionCount == 0)
            {
                return Mathf.Max(
                    16f,
                    _bodyStyle.CalcHeight(new GUIContent(block?.Content ?? string.Empty), Mathf.Max(1f, width - BodyPadding * 2f)));
            }

            return Mathf.Max(16f, totalHeight);
        }

        private float ResolveSubsectionBodyHeight(PromptWorkspacePreviewBlock block, float width)
        {
            float subsectionWidth = Mathf.Max(1f, width - BodyPadding * 2f);
            float subsectionContentWidth = Mathf.Max(1f, subsectionWidth - SubsectionIndent);
            float totalHeight = 0f;
            int subsectionCount = 0;
            foreach (PromptWorkspacePreviewSubsection subsection in block.Subsections ?? new List<PromptWorkspacePreviewSubsection>())
            {
                if (subsection == null || string.IsNullOrWhiteSpace(subsection.Content))
                {
                    continue;
                }

                float headerHeight = ResolveSubsectionHeaderHeight(ResolveSubsectionTitle(subsection), subsectionWidth);
                float contentHeight = Mathf.Max(
                    16f,
                    _bodyStyle.CalcHeight(new GUIContent(subsection.Content), subsectionContentWidth));
                totalHeight += headerHeight + contentHeight + SubsectionGap;
                subsectionCount++;
            }

            if (subsectionCount == 0)
            {
                return Mathf.Max(
                    16f,
                    _bodyStyle.CalcHeight(new GUIContent(block?.Content ?? string.Empty), Mathf.Max(1f, width - BodyPadding * 2f)));
            }

            return Mathf.Max(16f, totalHeight);
        }

        private float ResolveSubsectionHeaderHeight(string text, float width)
        {
            return Mathf.Max(18f, _subHeaderStyle.CalcHeight(new GUIContent(text ?? string.Empty), Mathf.Max(1f, width)));
        }

        private void DrawBodyContent(Rect bodyRect, PromptWorkspacePreviewBlock block, int blockIndex)
        {
            if (HasSubsections(block))
            {
                DrawSubsectionBody(bodyRect, block, blockIndex);
                return;
            }

            DrawPlainBody(bodyRect, block?.Content ?? string.Empty);
        }

        private void DrawSubsectionBody(Rect bodyRect, PromptWorkspacePreviewBlock block, int blockIndex)
        {
            float headerX = bodyRect.x + BodyPadding;
            float headerWidth = Mathf.Max(1f, bodyRect.width - BodyPadding * 2f);
            float contentX = headerX + SubsectionIndent;
            float contentWidth = Mathf.Max(1f, headerWidth - SubsectionIndent);
            float y = bodyRect.y;
            bool hasRenderableSubsection = false;

            List<SubsectionLayoutEntry> cachedEntries = blockIndex >= 0 && blockIndex < _cachedSubsectionLayouts.Count
                ? _cachedSubsectionLayouts[blockIndex]
                : null;

            int subsectionIdx = 0;
            foreach (PromptWorkspacePreviewSubsection subsection in block.Subsections ?? new List<PromptWorkspacePreviewSubsection>())
            {
                if (subsection == null || string.IsNullOrWhiteSpace(subsection.Content))
                {
                    continue;
                }

                string subtitle = ResolveSubsectionTitle(subsection);

                // Use cached height when available, otherwise compute
                float subtitleHeight, contentHeight;
                if (cachedEntries != null && subsectionIdx < cachedEntries.Count)
                {
                    subtitleHeight = cachedEntries[subsectionIdx].HeaderHeight;
                    contentHeight = cachedEntries[subsectionIdx].ContentHeight;
                }
                else
                {
                    subtitleHeight = ResolveSubsectionHeaderHeight(subtitle, headerWidth);
                    contentHeight = Mathf.Max(16f, _bodyStyle.CalcHeight(new GUIContent(subsection.Content), contentWidth));
                }

                Rect subtitleRect = new Rect(headerX, y, headerWidth, subtitleHeight);
                Widgets.DrawBoxSolid(subtitleRect, SubtitleBg);
                GUI.Label(new Rect(
                    subtitleRect.x + SubsectionHeaderPadding,
                    subtitleRect.y + 1f,
                    subtitleRect.width - SubsectionHeaderPadding * 2f,
                    subtitleRect.height - 2f),
                    subtitle,
                    _subHeaderStyle);
                y += subtitleHeight;

                GUI.Label(new Rect(contentX, y, contentWidth, contentHeight), subsection.Content, _bodyStyle);
                y += contentHeight + SubsectionGap;
                hasRenderableSubsection = true;
                subsectionIdx++;
            }

            if (!hasRenderableSubsection)
            {
                DrawPlainBody(bodyRect, block?.Content ?? string.Empty);
            }
        }

        private void DrawPlainBody(Rect bodyRect, string content)
        {
            GUI.Label(new Rect(
                bodyRect.x + BodyPadding,
                bodyRect.y,
                bodyRect.width - BodyPadding * 2f,
                bodyRect.height),
                content ?? string.Empty,
                _bodyStyle);
        }

        private string ResolveSubsectionTitle(PromptWorkspacePreviewSubsection subsection)
        {
            string sectionId = PromptSectionSchemaCatalog.NormalizeSectionId(subsection?.SectionId);
            if (PromptSectionSchemaCatalog.TryGetSection(sectionId, out PromptSectionSchemaItem section))
            {
                return "RimChat_PromptWorkspacePreviewBlock_SubSection".Translate(section.GetDisplayLabel(), section.Id).ToString();
            }

            string fallbackId = string.IsNullOrWhiteSpace(sectionId)
                ? PromptWorkspacePreviewBlockKind.SectionAggregate.ToString().ToLowerInvariant()
                : sectionId;
            return "RimChat_PromptWorkspacePreviewBlock_SubSection".Translate(fallbackId, fallbackId).ToString();
        }

        private static bool HasSubsections(PromptWorkspacePreviewBlock block)
        {
            return block != null &&
                   block.Kind == PromptWorkspacePreviewBlockKind.SectionAggregate &&
                   block.Subsections != null &&
                   block.Subsections.Count > 0;
        }

        private void EnsureStyles()
        {
            _instance = this;
            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold,
                    wordWrap = true,
                    richText = false
                };
            }

            if (_subHeaderStyle == null)
            {
                _subHeaderStyle = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold,
                    wordWrap = true,
                    richText = false
                };
            }

            if (_bodyStyle == null)
            {
                _bodyStyle = new GUIStyle(GUI.skin.label)
                {
                    wordWrap = true,
                    richText = false
                };
            }

            if (_indicatorStyle == null)
            {
                _indicatorStyle = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold,
                    wordWrap = false
                };
                SnapshotIndicatorStyle = _indicatorStyle;
            }
        }

        private string ResolveHeaderText(PromptWorkspacePreviewBlock block)
        {
            PromptWorkspacePreviewBlockKind kind = block?.Kind ?? PromptWorkspacePreviewBlockKind.Node;
            switch (kind)
            {
                case PromptWorkspacePreviewBlockKind.Context:
                    return "RimChat_PromptWorkspacePreviewBlock_Context".Translate(block?.PromptChannel ?? string.Empty).ToString();
                case PromptWorkspacePreviewBlockKind.SectionAggregate:
                    return "RimChat_PromptWorkspacePreviewBlock_MainSections".Translate().ToString();
                case PromptWorkspacePreviewBlockKind.Footer:
                    return "RimChat_PromptWorkspacePreviewBlock_Footer".Translate().ToString();
                case PromptWorkspacePreviewBlockKind.Error:
                    return "RimChat_PromptWorkspacePreviewBlock_Error".Translate().ToString();
                default:
                    string nodeLabel = PromptUnifiedNodeSchemaCatalog.GetDisplayLabel(block?.NodeId ?? string.Empty);
                    return "RimChat_PromptWorkspacePreviewBlock_Node".Translate(block?.Order ?? 0, nodeLabel).ToString();
            }
        }

        private static Color ResolveHeaderColor(PromptWorkspacePreviewBlock block)
        {
            PromptWorkspacePreviewBlockKind kind = block?.Kind ?? PromptWorkspacePreviewBlockKind.Node;
            switch (kind)
            {
                case PromptWorkspacePreviewBlockKind.Context:
                    return BlockBgSystemRules;
                case PromptWorkspacePreviewBlockKind.SectionAggregate:
                    return BlockBgCharacter;
                case PromptWorkspacePreviewBlockKind.Footer:
                    return BlockBgGeneric;
                case PromptWorkspacePreviewBlockKind.Error:
                    return BlockBgActionRules;
                default:
                    return BlockBgOutputSpec;
            }
        }

        private static void DrawSnapshotIndicator(Rect rect, bool usesSnapshot)
        {
            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;

            // Reuse the shared static indicator style (cached per-instance via EnsureStyles)
            GUIStyle indicatorStyle = _instance?._indicatorStyle ?? SnapshotIndicatorStyle;

            if (usesSnapshot)
            {
                // Green indicator: runtime snapshot data
                Widgets.DrawBoxSolid(rect, SnapshotLiveBg);
                GUI.color = SnapshotLiveText;
                GUI.Label(new Rect(rect.x + 6f, rect.y, rect.width - 12f, rect.height),
                    "RimChat_PreviewSnapshotIndicator_Live".Translate().ToString(),
                    indicatorStyle);
            }
            else
            {
                // Gray indicator: placeholder data
                Widgets.DrawBoxSolid(rect, SnapshotPlaceholderBg);
                GUI.color = SnapshotPlaceholderText;
                GUI.Label(new Rect(rect.x + 6f, rect.y, rect.width - 12f, rect.height),
                    "RimChat_PreviewSnapshotIndicator_Placeholder".Translate().ToString(),
                    indicatorStyle);
            }

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
        }

        private static GUIStyle SnapshotIndicatorStyle;
        private static PromptWorkspaceStructuredPreviewRenderer _instance;

        public void Dispose()
        {
            _rtCache.Dispose();
        }
    }
}
