using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RimChat.UI.UGui.Panels
{
    /// <summary>
    /// Dependencies: UGuiCanvasHost, TMPro, Unity UI Button/Image/Layout/ScrollRect/Toggle.
    /// Responsibility: UGUI-based rendering of the workspace left panel
    /// (preset actions, preset list, module list header, module list with checkboxes).
    /// Interactive elements (buttons, checkboxes) use UGUI native components
    /// with input bridge from IMGUI.
    /// </summary>
    internal sealed class WorkspacePresetPanel : UGuiPanelBase
    {
        // Header section
        private TMP_Text _presetHeaderLabel;
        private Button _createBtn;
        private TMP_Text _createLabel;
        private Button _duplicateBtn;
        private TMP_Text _duplicateLabel;

        // Preset list
        private ScrollRect _presetScrollRect;
        private RectTransform _presetContentRect;
        private readonly List<PresetRowElements> _presetRows = new List<PresetRowElements>();

        // Module section
        private TMP_Text _moduleHeaderLabel;
        private ScrollRect _moduleScrollRect;
        private RectTransform _moduleContentRect;
        private readonly List<ModuleRowElements> _moduleRows = new List<ModuleRowElements>();

        // Color constants
        private static readonly Color PanelBg = new Color(0.09f, 0.10f, 0.12f);
        private static readonly Color RowSelectedBg = new Color(0.27f, 0.38f, 0.56f);
        private static readonly Color RowHoverBg = new Color(0.18f, 0.18f, 0.20f);
        private static readonly Color RowNormalBg = new Color(0f, 0f, 0f, 0f);
        private static readonly Color BtnBg = new Color(0.16f, 0.16f, 0.18f);

        private const float LabelFontSize = 12f;
        private const float SmallFontSize = 11f;
        private const float RowHeight = 25f;
        private const float RowStep = 26f;

        private PresetPanelData _data;

        internal struct PresetRowData
        {
            public string Id;
            public string Name;
            public bool IsSelected;
            public bool IsActive;
            public bool IsDefault;
        }

        internal struct ModuleRowData
        {
            public string Id;
            public string Label;
            public string KindTag;
            public bool IsSelected;
            public bool IsEnabled;
        }

        internal struct PresetPanelData
        {
            public string PresetHeaderLabel;
            public string CreateLabel;
            public string DuplicateLabel;
            public List<PresetRowData> Presets;
            public string ModuleHeaderLabel;
            public List<ModuleRowData> Modules;
        }

        internal System.Action OnCreateClicked;
        internal System.Action OnDuplicateClicked;
        internal System.Action<string> OnPresetClicked;
        internal System.Action<string> OnModuleClicked;
        internal System.Action<string, bool> OnModuleToggled;

        internal void SetData(PresetPanelData data)
        {
            // Quick signature — include count + selected states
            int selPreset = -1;
            for (int i = 0; i < (data.Presets?.Count ?? 0); i++)
            {
                if (data.Presets[i].IsSelected) { selPreset = i; break; }
            }
            int selModule = -1;
            for (int i = 0; i < (data.Modules?.Count ?? 0); i++)
            {
                if (data.Modules[i].IsSelected) { selModule = i; break; }
            }
            string sig = $"{data.PresetHeaderLabel}|{data.CreateLabel}|{data.ModuleHeaderLabel}|{data.Presets?.Count ?? 0}|{selPreset}|{data.Modules?.Count ?? 0}|{selModule}|{data.Presets?.Count ?? 0}";
            _data = data;
            MarkDirtyIfChanged(sig);
        }

        protected override void BuildUI(RectTransform parent)
        {
            // Background
            Image bg = parent.gameObject.AddComponent<Image>();
            bg.color = PanelBg;
            bg.raycastTarget = false;

            VerticalLayoutGroup layout = parent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 4f;
            layout.padding = new RectOffset(8, 8, 8, 8);

            // Preset header label
            GameObject phGo = CreateChildObject("PresetHeader", parent);
            LayoutElement phLayout = phGo.AddComponent<LayoutElement>();
            phLayout.minHeight = 22f;
            phLayout.preferredHeight = 22f;
            _presetHeaderLabel = phGo.AddComponent<TextMeshProUGUI>();
            _presetHeaderLabel.fontSize = LabelFontSize;
            _presetHeaderLabel.color = Color.white;
            _presetHeaderLabel.alignment = TextAlignmentOptions.MidlineLeft;
            _presetHeaderLabel.raycastTarget = false;
            UGuiFontManager.ApplyFont(_presetHeaderLabel);

            // Preset action buttons row
            GameObject actionsGo = CreateChildObject("PresetActions", parent);
            LayoutElement actionsLayout = actionsGo.AddComponent<LayoutElement>();
            actionsLayout.minHeight = 24f;
            actionsLayout.preferredHeight = 24f;
            BuildPresetActions(actionsGo.transform);

            // Preset list (scrollable)
            GameObject presetListGo = CreateChildObject("PresetList", parent);
            LayoutElement plLayout = presetListGo.AddComponent<LayoutElement>();
            plLayout.minHeight = 80f;
            plLayout.preferredHeight = 180f;
            plLayout.flexibleHeight = 1f;
            BuildPresetScrollList(presetListGo);

            // Module header label
            GameObject mhGo = CreateChildObject("ModuleHeader", parent);
            LayoutElement mhLayout = mhGo.AddComponent<LayoutElement>();
            mhLayout.minHeight = 22f;
            mhLayout.preferredHeight = 22f;
            _moduleHeaderLabel = mhGo.AddComponent<TextMeshProUGUI>();
            _moduleHeaderLabel.fontSize = LabelFontSize;
            _moduleHeaderLabel.color = Color.white;
            _moduleHeaderLabel.alignment = TextAlignmentOptions.MidlineLeft;
            _moduleHeaderLabel.raycastTarget = false;
            UGuiFontManager.ApplyFont(_moduleHeaderLabel);

            // Module list (scrollable)
            GameObject moduleListGo = CreateChildObject("ModuleList", parent);
            LayoutElement mlLayout = moduleListGo.AddComponent<LayoutElement>();
            mlLayout.minHeight = 72f;
            mlLayout.preferredHeight = 200f;
            mlLayout.flexibleHeight = 1f;
            BuildModuleScrollList(moduleListGo);
        }

        private void BuildPresetActions(Transform parent)
        {
            HorizontalLayoutGroup hLayout = parent.gameObject.AddComponent<HorizontalLayoutGroup>();
            hLayout.childControlWidth = true;
            hLayout.childControlHeight = true;
            hLayout.childForceExpandWidth = true;
            hLayout.childForceExpandHeight = true;
            hLayout.spacing = 6f;

            // Create button
            GameObject createGo = CreateChildObject("CreateBtn", parent);
            Image createBg = createGo.AddComponent<Image>();
            createBg.color = BtnBg;
            createBg.raycastTarget = true;
            _createBtn = createGo.AddComponent<Button>();
            _createBtn.targetGraphic = createBg;
            _createBtn.onClick.AddListener(() => OnCreateClicked?.Invoke());

            GameObject clGo = CreateChildObject("Label", createGo.transform);
            _createLabel = clGo.AddComponent<TextMeshProUGUI>();
            _createLabel.fontSize = SmallFontSize;
            _createLabel.alignment = TextAlignmentOptions.Center;
            _createLabel.raycastTarget = false;
            UGuiFontManager.ApplyFont(_createLabel);

            // Duplicate button
            GameObject dupGo = CreateChildObject("DuplicateBtn", parent);
            Image dupBg = dupGo.AddComponent<Image>();
            dupBg.color = BtnBg;
            dupBg.raycastTarget = true;
            _duplicateBtn = dupGo.AddComponent<Button>();
            _duplicateBtn.targetGraphic = dupBg;
            _duplicateBtn.onClick.AddListener(() => OnDuplicateClicked?.Invoke());

            GameObject dlGo = CreateChildObject("Label", dupGo.transform);
            _duplicateLabel = dlGo.AddComponent<TextMeshProUGUI>();
            _duplicateLabel.fontSize = SmallFontSize;
            _duplicateLabel.alignment = TextAlignmentOptions.Center;
            _duplicateLabel.raycastTarget = false;
            UGuiFontManager.ApplyFont(_duplicateLabel);
        }

        private void BuildPresetScrollList(GameObject container)
        {
            Image bg = container.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.06f);
            bg.raycastTarget = false;

            ScrollRect scroll = container.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;

            // Viewport
            GameObject viewportGo = CreateChildObject("Viewport", container.transform);
            Image maskImg = viewportGo.AddComponent<Image>();
            maskImg.color = Color.clear;
            viewportGo.AddComponent<Mask>().showMaskGraphic = false;

            // Content
            GameObject contentGo = CreateChildObject("Content", viewportGo.transform);
            _presetContentRect = contentGo.GetComponent<RectTransform>();
            _presetContentRect.anchorMin = new Vector2(0f, 1f);
            _presetContentRect.anchorMax = new Vector2(1f, 1f);
            _presetContentRect.pivot = new Vector2(0.5f, 1f);

            VerticalLayoutGroup vLayout = contentGo.AddComponent<VerticalLayoutGroup>();
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = false;
            vLayout.childForceExpandWidth = true;
            vLayout.childForceExpandHeight = false;
            vLayout.spacing = 2f;

            ContentSizeFitter fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewportGo.GetComponent<RectTransform>();
            scroll.content = _presetContentRect;
            _presetScrollRect = scroll;
        }

        private void BuildModuleScrollList(GameObject container)
        {
            Image bg = container.AddComponent<Image>();
            bg.color = new Color(0.03f, 0.03f, 0.04f);
            bg.raycastTarget = false;

            ScrollRect scroll = container.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;

            GameObject viewportGo = CreateChildObject("Viewport", container.transform);
            Image maskImg = viewportGo.AddComponent<Image>();
            maskImg.color = Color.clear;
            viewportGo.AddComponent<Mask>().showMaskGraphic = false;

            GameObject contentGo = CreateChildObject("Content", viewportGo.transform);
            _moduleContentRect = contentGo.GetComponent<RectTransform>();
            _moduleContentRect.anchorMin = new Vector2(0f, 1f);
            _moduleContentRect.anchorMax = new Vector2(1f, 1f);
            _moduleContentRect.pivot = new Vector2(0.5f, 1f);

            VerticalLayoutGroup vLayout = contentGo.AddComponent<VerticalLayoutGroup>();
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = false;
            vLayout.childForceExpandWidth = true;
            vLayout.childForceExpandHeight = false;
            vLayout.spacing = 1f;

            ContentSizeFitter fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewportGo.GetComponent<RectTransform>();
            scroll.content = _moduleContentRect;
            _moduleScrollRect = scroll;
        }

        protected override void RefreshData()
        {
            // Headers
            if (_presetHeaderLabel != null) _presetHeaderLabel.text = _data.PresetHeaderLabel ?? string.Empty;
            if (_createLabel != null) _createLabel.text = _data.CreateLabel ?? string.Empty;
            if (_duplicateLabel != null) _duplicateLabel.text = _data.DuplicateLabel ?? string.Empty;
            if (_moduleHeaderLabel != null) _moduleHeaderLabel.text = _data.ModuleHeaderLabel ?? string.Empty;

            // Preset rows
            RefreshPresetRows();

            // Module rows
            RefreshModuleRows();
        }

        private void RefreshPresetRows()
        {
            List<PresetRowData> presets = _data.Presets ?? new List<PresetRowData>();

            // Ensure row count
            while (_presetRows.Count < presets.Count)
            {
                PresetRowElements row = CreatePresetRow(_presetContentRect);
                _presetRows.Add(row);
            }

            for (int i = 0; i < presets.Count; i++)
            {
                PresetRowData data = presets[i];
                PresetRowElements elem = _presetRows[i];

                elem.Root.SetActive(true);
                elem.Background.color = data.IsSelected ? RowSelectedBg : RowNormalBg;
                elem.Label.text = data.IsActive ? "\u25B6 " + (data.Name ?? string.Empty) : (data.Name ?? string.Empty);
                elem.Label.color = data.IsActive ? Color.green : Color.white;
                elem.Button.onClick.RemoveAllListeners();
                string presetId = data.Id;
                elem.Button.onClick.AddListener(() => OnPresetClicked?.Invoke(presetId));
            }

            for (int i = presets.Count; i < _presetRows.Count; i++)
            {
                _presetRows[i].Root.SetActive(false);
            }
        }

        private void RefreshModuleRows()
        {
            List<ModuleRowData> modules = _data.Modules ?? new List<ModuleRowData>();

            while (_moduleRows.Count < modules.Count)
            {
                ModuleRowElements row = CreateModuleRow(_moduleContentRect);
                _moduleRows.Add(row);
            }

            for (int i = 0; i < modules.Count; i++)
            {
                ModuleRowData data = modules[i];
                ModuleRowElements elem = _moduleRows[i];

                elem.Root.SetActive(true);
                elem.Background.color = data.IsSelected ? RowSelectedBg : RowNormalBg;
                elem.Label.text = $"{data.Label} [{data.KindTag}]";
                elem.Label.color = data.IsSelected ? Color.white : new Color(0.80f, 0.80f, 0.85f);
                elem.Toggle.isOn = data.IsEnabled;

                elem.Button.onClick.RemoveAllListeners();
                string moduleId = data.Id;
                elem.Button.onClick.AddListener(() => OnModuleClicked?.Invoke(moduleId));

                elem.Toggle.onValueChanged.RemoveAllListeners();
                elem.Toggle.onValueChanged.AddListener(val => OnModuleToggled?.Invoke(moduleId, val));
            }

            for (int i = modules.Count; i < _moduleRows.Count; i++)
            {
                _moduleRows[i].Root.SetActive(false);
            }
        }

        private PresetRowElements CreatePresetRow(Transform parent)
        {
            PresetRowElements elem = new PresetRowElements();

            GameObject go = CreateChildObject("PresetRow", parent);
            LayoutElement layout = go.AddComponent<LayoutElement>();
            layout.minHeight = RowHeight;
            layout.preferredHeight = RowHeight;

            elem.Root = go;
            elem.Background = go.AddComponent<Image>();
            elem.Background.raycastTarget = true;

            elem.Button = go.AddComponent<Button>();
            elem.Button.targetGraphic = elem.Background;

            GameObject labelGo = CreateChildObject("Label", go.transform);
            RectTransform lRect = labelGo.GetComponent<RectTransform>();
            lRect.anchorMin = new Vector2(0f, 0f);
            lRect.anchorMax = new Vector2(1f, 1f);
            lRect.offsetMin = new Vector2(24f, 0f);
            lRect.offsetMax = new Vector2(-8f, 0f);

            elem.Label = labelGo.AddComponent<TextMeshProUGUI>();
            elem.Label.fontSize = SmallFontSize;
            elem.Label.alignment = TextAlignmentOptions.MidlineLeft;
            elem.Label.raycastTarget = false;
            elem.Label.enableWordWrapping = false;
            UGuiFontManager.ApplyFont(elem.Label);

            return elem;
        }

        private ModuleRowElements CreateModuleRow(Transform parent)
        {
            ModuleRowElements elem = new ModuleRowElements();

            GameObject go = CreateChildObject("ModuleRow", parent);
            LayoutElement layout = go.AddComponent<LayoutElement>();
            layout.minHeight = RowHeight;
            layout.preferredHeight = RowHeight;

            elem.Root = go;
            elem.Background = go.AddComponent<Image>();
            elem.Background.raycastTarget = true;

            elem.Button = go.AddComponent<Button>();
            elem.Button.targetGraphic = elem.Background;

            // Checkbox
            GameObject toggleGo = CreateChildObject("Toggle", go.transform);
            RectTransform tRect = toggleGo.GetComponent<RectTransform>();
            tRect.anchorMin = new Vector2(0f, 0.5f);
            tRect.anchorMax = new Vector2(0f, 0.5f);
            tRect.sizeDelta = new Vector2(16f, 16f);
            tRect.anchoredPosition = new Vector2(12f, 0f);

            Image checkBg = toggleGo.AddComponent<Image>();
            checkBg.color = Color.white;
            checkBg.raycastTarget = true;

            GameObject markGo = CreateChildObject("Checkmark", toggleGo.transform);
            Image checkmark = markGo.AddComponent<Image>();
            checkmark.color = new Color(0.24f, 0.35f, 0.55f);

            elem.Toggle = toggleGo.AddComponent<Toggle>();
            elem.Toggle.targetGraphic = checkBg;
            elem.Toggle.graphic = checkmark;

            // Label
            GameObject labelGo = CreateChildObject("Label", go.transform);
            RectTransform lRect = labelGo.GetComponent<RectTransform>();
            lRect.anchorMin = new Vector2(0f, 0f);
            lRect.anchorMax = new Vector2(1f, 1f);
            lRect.offsetMin = new Vector2(28f, 0f);
            lRect.offsetMax = new Vector2(-4f, 0f);

            elem.Label = labelGo.AddComponent<TextMeshProUGUI>();
            elem.Label.fontSize = SmallFontSize;
            elem.Label.alignment = TextAlignmentOptions.MidlineLeft;
            elem.Label.raycastTarget = false;
            elem.Label.enableWordWrapping = false;
            UGuiFontManager.ApplyFont(elem.Label);

            return elem;
        }

        private struct PresetRowElements
        {
            public GameObject Root;
            public Image Background;
            public Button Button;
            public TMP_Text Label;
        }

        private struct ModuleRowElements
        {
            public GameObject Root;
            public Image Background;
            public Button Button;
            public Toggle Toggle;
            public TMP_Text Label;
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
