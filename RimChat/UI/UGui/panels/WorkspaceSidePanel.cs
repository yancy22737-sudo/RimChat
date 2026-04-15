using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RimChat.UI.UGui.Panels
{
    /// <summary>
    /// Dependencies: UGuiCanvasHost, TMPro, Unity UI Button/Image/Layout.
    /// Responsibility: UGUI-based rendering of the workspace side panel tabs
    /// (Preview, FullPreview, Variables tab buttons) plus content area placeholder.
    /// The actual preview content is handled by PromptPreviewPanel on a shared CanvasHost;
    /// this panel only renders the tab bar to save DrawCalls.
    /// </summary>
    internal sealed class WorkspaceSidePanel : UGuiPanelBase
    {
        private Button _previewTabBtn;
        private Image _previewTabBg;
        private TMP_Text _previewTabLabel;
        private Button _fullPreviewTabBtn;
        private Image _fullPreviewTabBg;
        private TMP_Text _fullPreviewTabLabel;
        private Button _variablesTabBtn;
        private Image _variablesTabBg;
        private TMP_Text _variablesTabLabel;

        // Color constants matching IMGUI version
        private static readonly Color TabSelectedBg = new Color(0.45f, 0.33f, 0.15f);
        private static readonly Color TabNormalBg = new Color(0.19f, 0.15f, 0.10f);
        private static readonly Color TabSelectedText = new Color(1f, 0.88f, 0.55f);
        private static readonly Color PanelBg = new Color(0.09f, 0.10f, 0.12f);

        private const float TabFontSize = 12f;
        private const float TabHeight = 24f;

        private SidePanelData _data;

        internal enum SidePanelTab
        {
            Preview,
            FullPreview,
            Variables
        }

        internal struct SidePanelData
        {
            public SidePanelTab ActiveTab;
            public string PreviewTabLabel;
            public string FullPreviewTabLabel;
            public string VariablesTabLabel;
        }

        internal Action<SidePanelTab> OnTabChanged;

        internal void SetData(SidePanelData data)
        {
            string sig = $"{(int)data.ActiveTab}|{data.PreviewTabLabel}|{data.FullPreviewTabLabel}|{data.VariablesTabLabel}";
            _data = data;
            MarkDirtyIfChanged(sig);
        }

        protected override void BuildUI(RectTransform parent)
        {
            // Background
            Image bg = parent.gameObject.AddComponent<Image>();
            bg.color = PanelBg;
            bg.raycastTarget = false;

            // Tab bar container at top
            GameObject tabBarGo = CreateChildObject("TabBar", parent);
            RectTransform tabBarRect = tabBarGo.GetComponent<RectTransform>();
            tabBarRect.anchorMin = new Vector2(0f, 1f);
            tabBarRect.anchorMax = new Vector2(1f, 1f);
            tabBarRect.offsetMin = new Vector2(8f, -24f);
            tabBarRect.offsetMax = new Vector2(-8f, -0f);

            float tabWidth = (1f / 3f);
            float gap = 6f / 600f; // normalized gap

            // Preview tab
            BuildTabButton("PreviewTab", tabBarGo.transform, 0f, tabWidth - gap, out _previewTabBtn, out _previewTabBg, out _previewTabLabel);
            _previewTabBtn.onClick.AddListener(() => OnTabChanged?.Invoke(SidePanelTab.Preview));

            // FullPreview tab
            BuildTabButton("FullPreviewTab", tabBarGo.transform, tabWidth, tabWidth - gap, out _fullPreviewTabBtn, out _fullPreviewTabBg, out _fullPreviewTabLabel);
            _fullPreviewTabBtn.onClick.AddListener(() => OnTabChanged?.Invoke(SidePanelTab.FullPreview));

            // Variables tab
            BuildTabButton("VariablesTab", tabBarGo.transform, tabWidth * 2f, tabWidth, out _variablesTabBtn, out _variablesTabBg, out _variablesTabLabel);
            _variablesTabBtn.onClick.AddListener(() => OnTabChanged?.Invoke(SidePanelTab.Variables));
        }

        private void BuildTabButton(string name, Transform parent, float anchorX, float anchorWidth,
            out Button btn, out Image bg, out TMP_Text label)
        {
            GameObject go = CreateChildObject(name, parent);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(anchorX, 0f);
            rect.anchorMax = new Vector2(anchorX + anchorWidth, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            bg = go.AddComponent<Image>();
            bg.raycastTarget = true;

            btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;

            GameObject labelGo = CreateChildObject("Label", go.transform);
            RectTransform lRect = labelGo.GetComponent<RectTransform>();
            lRect.anchorMin = Vector2.zero;
            lRect.anchorMax = Vector2.one;
            lRect.offsetMin = Vector2.zero;
            lRect.offsetMax = Vector2.zero;

            label = labelGo.AddComponent<TextMeshProUGUI>();
            label.fontSize = TabFontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            UGuiFontManager.ApplyFont(label);
        }

        protected override void RefreshData()
        {
            // Update tab visuals based on active tab
            bool isPreview = _data.ActiveTab == SidePanelTab.Preview;
            bool isFull = _data.ActiveTab == SidePanelTab.FullPreview;
            bool isVars = _data.ActiveTab == SidePanelTab.Variables;

            if (_previewTabBg != null) _previewTabBg.color = isPreview ? TabSelectedBg : TabNormalBg;
            if (_previewTabLabel != null) { _previewTabLabel.text = _data.PreviewTabLabel ?? string.Empty; _previewTabLabel.color = isPreview ? TabSelectedText : Color.white; }

            if (_fullPreviewTabBg != null) _fullPreviewTabBg.color = isFull ? TabSelectedBg : TabNormalBg;
            if (_fullPreviewTabLabel != null) { _fullPreviewTabLabel.text = _data.FullPreviewTabLabel ?? string.Empty; _fullPreviewTabLabel.color = isFull ? TabSelectedText : Color.white; }

            if (_variablesTabBg != null) _variablesTabBg.color = isVars ? TabSelectedBg : TabNormalBg;
            if (_variablesTabLabel != null) { _variablesTabLabel.text = _data.VariablesTabLabel ?? string.Empty; _variablesTabLabel.color = isVars ? TabSelectedText : Color.white; }
        }

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
