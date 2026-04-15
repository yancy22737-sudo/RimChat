using System.Collections.Generic;
using System.Text;
using RimChat.Config;
using RimChat.Persistence;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Verse;

namespace RimChat.UI.UGui.Panels
{
    /// <summary>
    /// Dependencies: PromptWorkspaceStructuredPreview data model, TMPro TextMeshProUGUI, Unity LayoutGroup/Image.
    /// Responsibility: UGUI-based rendering of structured prompt preview blocks.
    /// Replaces IMGUI DrawCall-heavy rendering with UGUI retained-mode elements
    /// that are rendered once to a RenderTexture and displayed via GUI.DrawTexture.
    /// </summary>
    internal sealed class PromptPreviewPanel : UGuiPanelBase
    {
        private GameObject _contentContainer;
        private readonly List<BlockElementGroup> _blockElements = new List<BlockElementGroup>();
        private PromptWorkspaceStructuredPreview _currentData;

        // Status bar elements
        private GameObject _statusBar;
        private Image _progressBarBg;
        private Image _progressBarFill;
        private TMP_Text _statusLabel;
        private GameObject _snapshotIndicator;
        private TMP_Text _snapshotLabel;
        private Image _snapshotBg;

        // Color constants matching IMGUI version
        private static readonly Color StatusBg = new Color(0.10f, 0.12f, 0.15f);
        private static readonly Color ProgressBarBgColor = new Color(0.20f, 0.22f, 0.24f);
        private static readonly Color ProgressErrorColor = new Color(0.72f, 0.20f, 0.20f);
        private static readonly Color ProgressSuccessColor = new Color(0.28f, 0.62f, 0.35f);
        private static readonly Color BlockBgSystemRules = new Color(0.22f, 0.30f, 0.40f);
        private static readonly Color BlockBgCharacter = new Color(0.25f, 0.28f, 0.18f);
        private static readonly Color BlockBgGeneric = new Color(0.22f, 0.22f, 0.22f);
        private static readonly Color BlockBgActionRules = new Color(0.40f, 0.18f, 0.18f);
        private static readonly Color BlockBgOutputSpec = new Color(0.20f, 0.24f, 0.30f);
        private static readonly Color SubtitleBgColor = new Color(0.16f, 0.18f, 0.13f);
        private static readonly Color SnapshotLiveBgColor = new Color(0.12f, 0.18f, 0.12f);
        private static readonly Color SnapshotLiveTextColor = new Color(0.45f, 0.80f, 0.45f);
        private static readonly Color SnapshotPlaceholderBgColor = new Color(0.14f, 0.14f, 0.16f);
        private static readonly Color SnapshotPlaceholderTextColor = new Color(0.55f, 0.55f, 0.55f);
        private static readonly Color HeaderTextColor = Color.white;
        private static readonly Color BodyTextColor = new Color(0.85f, 0.85f, 0.85f);

        // Font sizes matching RimWorld's IMGUI style
        private const float HeaderFontSize = 14f;
        private const float SubHeaderFontSize = 13f;
        private const float BodyFontSize = 12f;
        private const float StatusFontSize = 11f;
        private const float BlockGap = 6f;
        private const float HeaderPadding = 4f;
        private const float BodyPadding = 6f;
        private const float SubsectionIndent = 8f;

        private struct BlockElementGroup
        {
            public GameObject Root;
            public Image HeaderBg;
            public TMP_Text HeaderText;
            public Image BodyBg;
            public TMP_Text BodyText;
            public List<SubsectionElementGroup> Subsections;
        }

        private struct SubsectionElementGroup
        {
            public Image HeaderBg;
            public TMP_Text HeaderText;
            public TMP_Text ContentText;
        }

        /// <summary>
        /// Set the preview data and mark the panel as dirty.
        /// </summary>
        internal void SetData(PromptWorkspaceStructuredPreview preview)
        {
            _currentData = preview;
            string sig = BuildSignature(preview);
            MarkDirtyIfChanged(sig);
        }

        protected override void BuildUI(RectTransform parent)
        {
            // Main vertical layout container
            _contentContainer = CreateChildObject("Content", parent);
            RectTransform contentRect = _contentContainer.GetComponent<RectTransform>();
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;

            VerticalLayoutGroup layout = _contentContainer.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = BlockGap;
            layout.padding = new RectOffset(4, 4, 4, 4);

            ContentSizeFitter fitter = _contentContainer.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Status bar (initially hidden)
            BuildStatusBar(_contentContainer);

            // Snapshot indicator (initially hidden)
            BuildSnapshotIndicator(_contentContainer);
        }

        protected override void RefreshData()
        {
            if (_currentData == null)
            {
                return;
            }

            // Update status bar
            RefreshStatusBar();

            // Update snapshot indicator
            RefreshSnapshotIndicator();

            // Update preview blocks
            RefreshBlocks();
        }

        protected override void OnBeforeDispose()
        {
            _blockElements.Clear();
            _currentData = null;
        }

        private void BuildStatusBar(GameObject parent)
        {
            _statusBar = CreateChildObject("StatusBar", parent.transform);
            _statusBar.SetActive(false);

            Image bg = _statusBar.AddComponent<Image>();
            bg.color = StatusBg;
            bg.raycastTarget = false;

            LayoutElement layoutElem = _statusBar.AddComponent<LayoutElement>();
            layoutElem.minHeight = 30f;
            layoutElem.preferredHeight = 30f;

            // Progress bar background
            GameObject progressGo = CreateChildObject("ProgressBg", _statusBar.transform);
            RectTransform progressRect = progressGo.GetComponent<RectTransform>();
            progressRect.anchorMin = new Vector2(0f, 0.5f);
            progressRect.anchorMax = new Vector2(1f, 0.5f);
            progressRect.offsetMin = new Vector2(8f, -4f);
            progressRect.offsetMax = new Vector2(-8f, 4f);

            _progressBarBg = progressGo.AddComponent<Image>();
            _progressBarBg.color = ProgressBarBgColor;
            _progressBarBg.raycastTarget = false;

            // Progress bar fill
            GameObject fillGo = CreateChildObject("ProgressFill", progressGo.transform);
            RectTransform fillRect = fillGo.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            _progressBarFill = fillGo.AddComponent<Image>();
            _progressBarFill.color = ProgressSuccessColor;
            _progressBarFill.raycastTarget = false;

            // Status label
            GameObject labelGo = CreateChildObject("StatusLabel", _statusBar.transform);
            RectTransform labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0.5f);
            labelRect.offsetMin = new Vector2(8f, 0f);
            labelRect.offsetMax = new Vector2(-8f, 0f);

            _statusLabel = labelGo.AddComponent<TextMeshProUGUI>();
            _statusLabel.fontSize = StatusFontSize;
            _statusLabel.color = Color.white;
            _statusLabel.raycastTarget = false;
            _statusLabel.alignment = TextAlignmentOptions.MidlineLeft;
            UGuiFontManager.ApplyFont(_statusLabel);
        }

        private void BuildSnapshotIndicator(GameObject parent)
        {
            _snapshotIndicator = CreateChildObject("SnapshotIndicator", parent.transform);
            _snapshotIndicator.SetActive(false);

            _snapshotBg = _snapshotIndicator.AddComponent<Image>();
            _snapshotBg.raycastTarget = false;

            LayoutElement layoutElem = _snapshotIndicator.AddComponent<LayoutElement>();
            layoutElem.minHeight = 20f;
            layoutElem.preferredHeight = 20f;

            GameObject labelGo = CreateChildObject("SnapshotLabel", _snapshotIndicator.transform);
            RectTransform labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(6f, 0f);
            labelRect.offsetMax = new Vector2(-6f, 0f);

            _snapshotLabel = labelGo.AddComponent<TextMeshProUGUI>();
            _snapshotLabel.fontSize = StatusFontSize;
            _snapshotLabel.raycastTarget = false;
            _snapshotLabel.alignment = TextAlignmentOptions.MidlineLeft;
            UGuiFontManager.ApplyFont(_snapshotLabel);
        }

        private void RefreshStatusBar()
        {
            bool shouldShow = ShouldDrawStatus(_currentData);
            _statusBar.SetActive(shouldShow);
            if (!shouldShow)
            {
                return;
            }

            float progress = ResolveProgress(_currentData);
            _progressBarFill.fillAmount = progress;
            _progressBarFill.color = _currentData.IsFailed ? ProgressErrorColor : ProgressSuccessColor;
            _statusLabel.text = ResolveStatusText(_currentData);
        }

        private void RefreshSnapshotIndicator()
        {
            bool shouldShow = _currentData != null
                && _currentData.Stage == PromptWorkspacePreviewBuildStage.Completed;
            _snapshotIndicator.SetActive(shouldShow);
            if (!shouldShow)
            {
                return;
            }

            bool usesSnapshot = _currentData.UsesSnapshotData;
            _snapshotBg.color = usesSnapshot ? SnapshotLiveBgColor : SnapshotPlaceholderBgColor;
            _snapshotLabel.color = usesSnapshot ? SnapshotLiveTextColor : SnapshotPlaceholderTextColor;
            _snapshotLabel.text = usesSnapshot
                ? "RimChat_PreviewSnapshotIndicator_Live".Translate().ToString()
                : "RimChat_PreviewSnapshotIndicator_Placeholder".Translate().ToString();
        }

        private void RefreshBlocks()
        {
            List<PromptWorkspacePreviewBlock> blocks = _currentData?.Blocks
                ?? new List<PromptWorkspacePreviewBlock>();

            // Reuse existing block elements or create new ones as needed
            EnsureBlockCount(blocks.Count);

            for (int i = 0; i < blocks.Count; i++)
            {
                PromptWorkspacePreviewBlock block = blocks[i];
                BlockElementGroup elem = _blockElements[i];

                // Header
                string headerText = ResolveHeaderText(block);
                elem.HeaderText.text = headerText;
                elem.HeaderBg.color = ResolveHeaderColor(block);

                // Body
                if (HasSubsections(block))
                {
                    elem.BodyText.gameObject.SetActive(false);
                    RefreshSubsections(elem, block);
                }
                else
                {
                    elem.BodyText.gameObject.SetActive(true);
                    elem.BodyText.text = block.Content ?? string.Empty;
                    HideSubsections(elem);
                }
            }

            // Hide unused blocks
            for (int i = blocks.Count; i < _blockElements.Count; i++)
            {
                _blockElements[i].Root.SetActive(false);
            }
        }

        private void EnsureBlockCount(int count)
        {
            while (_blockElements.Count < count)
            {
                BlockElementGroup block = CreateBlockElement(_contentContainer.transform);
                _blockElements.Add(block);
            }
        }

        private BlockElementGroup CreateBlockElement(Transform parent)
        {
            BlockElementGroup group = new BlockElementGroup();

            // Root container
            group.Root = CreateChildObject("Block", parent);
            LayoutElement rootLayout = group.Root.AddComponent<LayoutElement>();
            rootLayout.minHeight = 20f;

            // Header
            GameObject headerGo = CreateChildObject("Header", group.Root.transform);
            RectTransform headerRect = headerGo.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);

            group.HeaderBg = headerGo.AddComponent<Image>();
            group.HeaderBg.raycastTarget = false;

            LayoutElement headerLayout = headerGo.AddComponent<LayoutElement>();
            headerLayout.minHeight = 20f;
            headerLayout.preferredHeight = 20f;

            // Header text
            GameObject headerTextGo = CreateChildObject("HeaderText", headerGo.transform);
            RectTransform htRect = headerTextGo.GetComponent<RectTransform>();
            htRect.anchorMin = Vector2.zero;
            htRect.anchorMax = Vector2.one;
            htRect.offsetMin = new Vector2(HeaderPadding, 1f);
            htRect.offsetMax = new Vector2(-HeaderPadding, -1f);

            group.HeaderText = headerTextGo.AddComponent<TextMeshProUGUI>();
            group.HeaderText.fontSize = HeaderFontSize;
            group.HeaderText.color = HeaderTextColor;
            group.HeaderText.fontStyle = FontStyles.Bold;
            group.HeaderText.raycastTarget = false;
            group.HeaderText.enableWordWrapping = true;
            group.HeaderText.alignment = TextAlignmentOptions.MidlineLeft;
            UGuiFontManager.ApplyFont(group.HeaderText);

            // Body
            GameObject bodyGo = CreateChildObject("Body", group.Root.transform);
            group.BodyBg = bodyGo.AddComponent<Image>();
            group.BodyBg.color = Color.clear;
            group.BodyBg.raycastTarget = false;

            LayoutElement bodyLayout = bodyGo.AddComponent<LayoutElement>();
            bodyLayout.minHeight = 16f;

            // Body text
            GameObject bodyTextGo = CreateChildObject("BodyText", bodyGo.transform);
            RectTransform btRect = bodyTextGo.GetComponent<RectTransform>();
            btRect.anchorMin = Vector2.zero;
            btRect.anchorMax = Vector2.one;
            btRect.offsetMin = new Vector2(BodyPadding, 0f);
            btRect.offsetMax = new Vector2(-BodyPadding, 0f);

            group.BodyText = bodyTextGo.AddComponent<TextMeshProUGUI>();
            group.BodyText.fontSize = BodyFontSize;
            group.BodyText.color = BodyTextColor;
            group.BodyText.raycastTarget = false;
            group.BodyText.enableWordWrapping = true;
            UGuiFontManager.ApplyFont(group.BodyText);

            group.Subsections = new List<SubsectionElementGroup>();

            return group;
        }

        private void RefreshSubsections(BlockElementGroup block, PromptWorkspacePreviewBlock data)
        {
            List<PromptWorkspacePreviewSubsection> subs = data.Subsections
                ?? new List<PromptWorkspacePreviewSubsection>();

            // Ensure enough subsection elements
            while (block.Subsections.Count < subs.Count)
            {
                SubsectionElementGroup sub = CreateSubsectionElement(block.BodyBg.transform);
                block.Subsections.Add(sub);
            }

            int validIdx = 0;
            for (int i = 0; i < subs.Count; i++)
            {
                if (subs[i] == null || string.IsNullOrWhiteSpace(subs[i].Content))
                {
                    continue;
                }

                SubsectionElementGroup subElem = block.Subsections[validIdx];
                subElem.HeaderBg.gameObject.SetActive(true);
                subElem.ContentText.gameObject.SetActive(true);

                string subtitle = ResolveSubsectionTitle(subs[i]);
                subElem.HeaderText.text = subtitle;
                subElem.ContentText.text = subs[i].Content ?? string.Empty;

                validIdx++;
            }

            // Hide unused subsections
            for (int i = validIdx; i < block.Subsections.Count; i++)
            {
                block.Subsections[i].HeaderBg.gameObject.SetActive(false);
                block.Subsections[i].ContentText.gameObject.SetActive(false);
            }
        }

        private void HideSubsections(BlockElementGroup block)
        {
            foreach (SubsectionElementGroup sub in block.Subsections)
            {
                sub.HeaderBg.gameObject.SetActive(false);
                sub.ContentText.gameObject.SetActive(false);
            }
        }

        private SubsectionElementGroup CreateSubsectionElement(Transform parent)
        {
            SubsectionElementGroup group = new SubsectionElementGroup();

            // Sub-header
            GameObject subHeaderGo = CreateChildObject("SubHeader", parent);
            group.HeaderBg = subHeaderGo.AddComponent<Image>();
            group.HeaderBg.color = SubtitleBgColor;
            group.HeaderBg.raycastTarget = false;

            LayoutElement subHeaderLayout = subHeaderGo.AddComponent<LayoutElement>();
            subHeaderLayout.minHeight = 18f;
            subHeaderLayout.preferredHeight = 18f;

            // Sub-header text
            GameObject subHeaderTextGo = CreateChildObject("SubHeaderText", subHeaderGo.transform);
            RectTransform shtRect = subHeaderTextGo.GetComponent<RectTransform>();
            shtRect.anchorMin = Vector2.zero;
            shtRect.anchorMax = Vector2.one;
            shtRect.offsetMin = new Vector2(SubsectionHeaderPadding, 1f);
            shtRect.offsetMax = new Vector2(-SubsectionHeaderPadding, -1f);

            group.HeaderText = subHeaderTextGo.AddComponent<TextMeshProUGUI>();
            group.HeaderText.fontSize = SubHeaderFontSize;
            group.HeaderText.color = HeaderTextColor;
            group.HeaderText.fontStyle = FontStyles.Bold;
            group.HeaderText.raycastTarget = false;
            group.HeaderText.enableWordWrapping = true;
            UGuiFontManager.ApplyFont(group.HeaderText);

            // Content
            GameObject contentGo = CreateChildObject("SubContent", parent);
            RectTransform scRect = contentGo.GetComponent<RectTransform>();
            scRect.offsetMin = new Vector2(SubsectionIndent, 0f);

            LayoutElement contentLayout = contentGo.AddComponent<LayoutElement>();
            contentLayout.minHeight = 16f;

            group.ContentText = contentGo.AddComponent<TextMeshProUGUI>();
            group.ContentText.fontSize = BodyFontSize;
            group.ContentText.color = BodyTextColor;
            group.ContentText.raycastTarget = false;
            group.ContentText.enableWordWrapping = true;
            UGuiFontManager.ApplyFont(group.ContentText);

            return group;
        }

        private string BuildSignature(PromptWorkspaceStructuredPreview preview)
        {
            if (preview == null)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(preview.Signature ?? string.Empty);
            sb.Append('|');
            sb.Append(preview.Stage.ToString());
            sb.Append('|');
            sb.Append(preview.Completed);
            sb.Append('/');
            sb.Append(preview.Total);
            sb.Append('|');
            sb.Append(preview.UsesSnapshotData ? "1" : "0");
            return sb.ToString();
        }

        private static bool ShouldDrawStatus(PromptWorkspaceStructuredPreview preview)
        {
            return preview != null &&
                (preview.IsBuilding || preview.IsFailed || preview.Total > 0
                    || preview.Stage == PromptWorkspacePreviewBuildStage.Completed);
        }

        private static float ResolveProgress(PromptWorkspaceStructuredPreview preview)
        {
            if (preview == null) return 0f;
            if (preview.Total <= 0) return preview.IsBuilding ? 0f : 1f;
            return Mathf.Clamp01((float)preview.Completed / preview.Total);
        }

        private static string ResolveStatusText(PromptWorkspaceStructuredPreview preview)
        {
            if (preview == null) return string.Empty;
            string stage = ResolveStageLabel(preview.Stage);
            if (preview.IsFailed)
            {
                return "RimChat_PromptWorkspacePreviewBuild_StatusFailed"
                    .Translate(stage, preview.Completed, preview.Total).ToString();
            }

            if (preview.IsBuilding)
            {
                return "RimChat_PromptWorkspacePreviewBuild_StatusBuilding"
                    .Translate(stage, preview.Completed, preview.Total,
                        preview.CompletedSections, preview.TotalSections,
                        preview.CompletedNodes, preview.TotalNodes).ToString();
            }

            return "RimChat_PromptWorkspacePreviewBuild_StatusCompleted"
                .Translate(preview.Completed, preview.Total).ToString();
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

        private static string ResolveHeaderText(PromptWorkspacePreviewBlock block)
        {
            PromptWorkspacePreviewBlockKind kind = block?.Kind ?? PromptWorkspacePreviewBlockKind.Node;
            switch (kind)
            {
                case PromptWorkspacePreviewBlockKind.Context:
                    return "RimChat_PromptWorkspacePreviewBlock_Context"
                        .Translate(block?.PromptChannel ?? string.Empty).ToString();
                case PromptWorkspacePreviewBlockKind.SectionAggregate:
                    return "RimChat_PromptWorkspacePreviewBlock_MainSections".Translate().ToString();
                case PromptWorkspacePreviewBlockKind.Footer:
                    return "RimChat_PromptWorkspacePreviewBlock_Footer".Translate().ToString();
                case PromptWorkspacePreviewBlockKind.Error:
                    return "RimChat_PromptWorkspacePreviewBlock_Error".Translate().ToString();
                default:
                    string nodeLabel = PromptUnifiedNodeSchemaCatalog
                        .GetDisplayLabel(block?.NodeId ?? string.Empty);
                    return "RimChat_PromptWorkspacePreviewBlock_Node"
                        .Translate(block?.Order ?? 0, nodeLabel).ToString();
            }
        }

        private static Color ResolveHeaderColor(PromptWorkspacePreviewBlock block)
        {
            PromptWorkspacePreviewBlockKind kind = block?.Kind ?? PromptWorkspacePreviewBlockKind.Node;
            switch (kind)
            {
                case PromptWorkspacePreviewBlockKind.Context: return BlockBgSystemRules;
                case PromptWorkspacePreviewBlockKind.SectionAggregate: return BlockBgCharacter;
                case PromptWorkspacePreviewBlockKind.Footer: return BlockBgGeneric;
                case PromptWorkspacePreviewBlockKind.Error: return BlockBgActionRules;
                default: return BlockBgOutputSpec;
            }
        }

        private static string ResolveSubsectionTitle(PromptWorkspacePreviewSubsection subsection)
        {
            string sectionId = PromptSectionSchemaCatalog.NormalizeSectionId(subsection?.SectionId);
            if (PromptSectionSchemaCatalog.TryGetSection(sectionId, out PromptSectionSchemaItem section))
            {
                return "RimChat_PromptWorkspacePreviewBlock_SubSection"
                    .Translate(section.GetDisplayLabel(), section.Id).ToString();
            }

            string fallbackId = string.IsNullOrWhiteSpace(sectionId)
                ? PromptWorkspacePreviewBlockKind.SectionAggregate.ToString().ToLowerInvariant()
                : sectionId;
            return "RimChat_PromptWorkspacePreviewBlock_SubSection"
                .Translate(fallbackId, fallbackId).ToString();
        }

        private static bool HasSubsections(PromptWorkspacePreviewBlock block)
        {
            return block != null
                && block.Kind == PromptWorkspacePreviewBlockKind.SectionAggregate
                && block.Subsections != null
                && block.Subsections.Count > 0;
        }

        private const float SubsectionHeaderPadding = 4f;

        private static GameObject CreateChildObject(string name, Transform parent)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.NotEditable;
            go.layer = parent.gameObject.layer;
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return go;
        }
    }
}
