using System;
using System.Collections.Generic;
using RimChat.Config;
using RimChat.Persistence;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: structured preview model, node schema labels, and Verse GUI APIs.
    /// Responsibility: render lightweight structured prompt preview blocks with layout caching.
    /// </summary>
    internal sealed class PromptWorkspaceStructuredPreviewRenderer
    {
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
        private GUIStyle _headerStyle;
        private GUIStyle _subHeaderStyle;
        private GUIStyle _bodyStyle;

        internal void Draw(
            Rect rect,
            PromptWorkspaceStructuredPreview preview,
            ref Vector2 scroll)
        {
            EnsureStyles();
            List<PromptWorkspacePreviewBlock> blocks = preview?.Blocks ?? new List<PromptWorkspacePreviewBlock>();
            if (blocks.Count == 0)
            {
                Widgets.Label(rect, "RimChat_PromptWorkbench_PreviewEmpty".Translate());
                return;
            }

            float width = Mathf.Max(1f, rect.width - 16f);
            string signature = preview?.Signature ?? string.Empty;
            EnsureLayoutCache(signature, blocks, width);

            Rect viewRect = new Rect(0f, 0f, width, _cachedContentHeight);
            float maxScrollY = Mathf.Max(0f, viewRect.height - rect.height);
            scroll = new Vector2(0f, Mathf.Clamp(scroll.y, 0f, maxScrollY));
            scroll = GUI.BeginScrollView(rect, scroll, viewRect, false, true);

            float y = 0f;
            for (int i = 0; i < blocks.Count; i++)
            {
                PromptWorkspacePreviewBlock block = blocks[i];
                float headerHeight = i < _cachedHeaderHeights.Count
                    ? _cachedHeaderHeights[i]
                    : ResolveHeaderHeight(ResolveHeaderText(block), width);
                float bodyHeight = i < _cachedBodyHeights.Count
                    ? _cachedBodyHeights[i]
                    : ResolveBodyHeight(block, width);

                Rect headerRect = new Rect(0f, y, width, headerHeight);
                Widgets.DrawBoxSolid(headerRect, ResolveHeaderColor(block));
                GUI.Label(new Rect(
                    headerRect.x + HeaderPadding,
                    headerRect.y + 1f,
                    headerRect.width - HeaderPadding * 2f,
                    headerRect.height - 2f),
                    ResolveHeaderText(block),
                    _headerStyle);
                y += headerHeight;

                Rect bodyRect = new Rect(0f, y, width, bodyHeight);
                DrawBodyContent(bodyRect, block);
                y += bodyHeight + BlockGap;
            }

            GUI.EndScrollView();
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

            float contentHeight = 0f;
            for (int i = 0; i < blocks.Count; i++)
            {
                PromptWorkspacePreviewBlock block = blocks[i];
                string headerText = ResolveHeaderText(block);
                float headerHeight = ResolveHeaderHeight(headerText, width);
                float bodyHeight = ResolveBodyHeight(block, width);
                _cachedHeaderHeights.Add(headerHeight);
                _cachedBodyHeights.Add(bodyHeight);
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

        private void DrawBodyContent(Rect bodyRect, PromptWorkspacePreviewBlock block)
        {
            if (HasSubsections(block))
            {
                DrawSubsectionBody(bodyRect, block);
                return;
            }

            DrawPlainBody(bodyRect, block?.Content ?? string.Empty);
        }

        private void DrawSubsectionBody(Rect bodyRect, PromptWorkspacePreviewBlock block)
        {
            float headerX = bodyRect.x + BodyPadding;
            float headerWidth = Mathf.Max(1f, bodyRect.width - BodyPadding * 2f);
            float contentX = headerX + SubsectionIndent;
            float contentWidth = Mathf.Max(1f, headerWidth - SubsectionIndent);
            float y = bodyRect.y;
            bool hasRenderableSubsection = false;

            foreach (PromptWorkspacePreviewSubsection subsection in block.Subsections ?? new List<PromptWorkspacePreviewSubsection>())
            {
                if (subsection == null || string.IsNullOrWhiteSpace(subsection.Content))
                {
                    continue;
                }

                string subtitle = ResolveSubsectionTitle(subsection);
                float subtitleHeight = ResolveSubsectionHeaderHeight(subtitle, headerWidth);
                Rect subtitleRect = new Rect(headerX, y, headerWidth, subtitleHeight);
                Widgets.DrawBoxSolid(subtitleRect, new Color(0.16f, 0.18f, 0.13f));
                GUI.Label(new Rect(
                    subtitleRect.x + SubsectionHeaderPadding,
                    subtitleRect.y + 1f,
                    subtitleRect.width - SubsectionHeaderPadding * 2f,
                    subtitleRect.height - 2f),
                    subtitle,
                    _subHeaderStyle);
                y += subtitleHeight;

                float contentHeight = Mathf.Max(
                    16f,
                    _bodyStyle.CalcHeight(new GUIContent(subsection.Content), contentWidth));
                GUI.Label(new Rect(contentX, y, contentWidth, contentHeight), subsection.Content, _bodyStyle);
                y += contentHeight + SubsectionGap;
                hasRenderableSubsection = true;
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
                    return new Color(0.22f, 0.30f, 0.40f);
                case PromptWorkspacePreviewBlockKind.SectionAggregate:
                    return new Color(0.25f, 0.28f, 0.18f);
                case PromptWorkspacePreviewBlockKind.Footer:
                    return new Color(0.22f, 0.22f, 0.22f);
                default:
                    return new Color(0.20f, 0.24f, 0.30f);
            }
        }
    }
}
