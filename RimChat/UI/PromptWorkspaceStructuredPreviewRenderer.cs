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

        private string _cachedSignature = string.Empty;
        private float _cachedWidth = -1f;
        private readonly List<float> _cachedBodyHeights = new List<float>();
        private readonly List<float> _cachedHeaderHeights = new List<float>();
        private float _cachedContentHeight = MinContentHeight;
        private GUIStyle _headerStyle;
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
                    : ResolveBodyHeight(block?.Content ?? string.Empty, width);

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
                GUI.Label(new Rect(
                    bodyRect.x + BodyPadding,
                    bodyRect.y,
                    bodyRect.width - BodyPadding * 2f,
                    bodyRect.height),
                    block?.Content ?? string.Empty,
                    _bodyStyle);
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
                string content = block?.Content ?? string.Empty;
                float headerHeight = ResolveHeaderHeight(headerText, width);
                float bodyHeight = ResolveBodyHeight(content, width);
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

        private float ResolveBodyHeight(string text, float width)
        {
            return Mathf.Max(16f, _bodyStyle.CalcHeight(new GUIContent(text ?? string.Empty), Mathf.Max(1f, width - BodyPadding * 2f)));
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
                    string slotLabel = ResolveSlotLabel(block?.Slot ?? PromptUnifiedNodeSlot.MainChainAfter);
                    string nodeLabel = PromptUnifiedNodeSchemaCatalog.GetDisplayLabel(block?.NodeId ?? string.Empty);
                    return "RimChat_PromptWorkspacePreviewBlock_Node".Translate(block?.Order ?? 0, slotLabel, nodeLabel).ToString();
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

        private static string ResolveSlotLabel(PromptUnifiedNodeSlot slot)
        {
            switch (slot)
            {
                case PromptUnifiedNodeSlot.MetadataAfter:
                    return "RimChat_PromptNodeSlot_MetadataAfter".Translate().ToString();
                case PromptUnifiedNodeSlot.MainChainBefore:
                    return "RimChat_PromptNodeSlot_MainChainBefore".Translate().ToString();
                case PromptUnifiedNodeSlot.MainChainAfter:
                    return "RimChat_PromptNodeSlot_MainChainAfter".Translate().ToString();
                case PromptUnifiedNodeSlot.DynamicDataAfter:
                    return "RimChat_PromptNodeSlot_DynamicDataAfter".Translate().ToString();
                case PromptUnifiedNodeSlot.ContractBeforeEnd:
                    return "RimChat_PromptNodeSlot_ContractBeforeEnd".Translate().ToString();
                default:
                    return slot.ToSerializedValue();
            }
        }
    }
}
