using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RimChat.UI.UGui.Panels
{
    /// <summary>
    /// Dependencies: UGuiCanvasHost, TMPro, Unity UI Button/Image/Layout.
    /// Responsibility: UGUI-based rendering of the workspace editor panel header area
    /// (preset name, metadata row, toolbar, validation status). The main text editor
    /// area remains IMGUI due to deep TextEditor coupling, but the surrounding chrome
    /// (metadata, toolbar buttons, validation label) can be rendered via UGUI.
    /// </summary>
    internal sealed class WorkspaceEditorPanel : UGuiPanelBase
    {
        // Metadata row
        private TMP_Text _kindTagLabel;
        private TMP_Text _moduleLabel;
        private TMP_Text _enabledLabel;
        private Toggle _enabledToggle;

        // Toolbar buttons
        private Button _undoBtn;
        private TMP_Text _undoLabel;
        private Button _redoBtn;
        private TMP_Text _redoLabel;
        private Button _saveBtn;
        private TMP_Text _saveLabel;
        private Button _resetBtn;
        private TMP_Text _resetLabel;

        // Validation status
        private TMP_Text _validationLabel;

        // Color constants
        private static readonly Color EditorBg = new Color(0.06f, 0.07f, 0.09f);
        private static readonly Color MetadataTagColor = new Color(0.70f, 0.70f, 0.70f);
        private static readonly Color AccentBrightGold = new Color(0.95f, 0.88f, 0.55f);
        private static readonly Color ValidationOkColor = new Color(0.28f, 0.62f, 0.35f);
        private static readonly Color ValidationWarnColor = new Color(0.95f, 0.74f, 0.26f);
        private static readonly Color ValidationErrorColor = new Color(0.72f, 0.20f, 0.20f);
        private static readonly Color ToolbarBtnBg = new Color(0.16f, 0.16f, 0.18f);
        private static readonly Color ToolbarBtnDisabledBg = new Color(0.10f, 0.10f, 0.10f);

        private const float LabelFontSize = 12f;
        private const float SmallFontSize = 11f;

        private EditorData _data;

        internal struct EditorData
        {
            public string KindTag;
            public string ModuleLabel;
            public string EnabledLabel;
            public bool IsEnabled;
            public string UndoLabel;
            public string RedoLabel;
            public string SaveLabel;
            public string ResetLabel;
            public bool CanUndo;
            public bool CanRedo;
            public string ValidationText;
            public ValidationState Validation;
        }

        internal enum ValidationState
        {
            Ok,
            Warning,
            Error
        }

        internal Action OnEnabledToggled;
        internal Action OnUndoClicked;
        internal Action OnRedoClicked;
        internal Action OnSaveClicked;
        internal Action OnResetClicked;

        internal void SetData(EditorData data)
        {
            string sig = $"{data.KindTag}|{data.ModuleLabel}|{data.IsEnabled}|{data.EnabledLabel}|{data.CanUndo}|{data.CanRedo}|{data.ValidationText}|{(int)data.Validation}";
            _data = data;
            MarkDirtyIfChanged(sig);
        }

        protected override void BuildUI(RectTransform parent)
        {
            // Background
            Image bg = parent.gameObject.AddComponent<Image>();
            bg.color = EditorBg;
            bg.raycastTarget = false;

            float y = 0f;

            // Metadata row (y: 0-24)
            BuildMetadataRow(parent, y);
            y += 28f;

            // Toolbar row (y: 28-54)
            BuildToolbarRow(parent, y);
            y += 30f;

            // Validation label at bottom
            BuildValidationLabel(parent);
        }

        private void BuildMetadataRow(RectTransform parent, float y)
        {
            // Kind tag [Section] or [Node]
            GameObject kindGo = CreateChildObject("KindTag", parent);
            RectTransform kindRect = kindGo.GetComponent<RectTransform>();
            kindRect.anchorMin = new Vector2(0f, 1f);
            kindRect.anchorMax = new Vector2(0f, 1f);
            kindRect.offsetMin = new Vector2(8f, -(y + 20f));
            kindRect.offsetMax = new Vector2(68f, -y);

            _kindTagLabel = kindGo.AddComponent<TextMeshProUGUI>();
            _kindTagLabel.fontSize = LabelFontSize;
            _kindTagLabel.color = MetadataTagColor;
            _kindTagLabel.alignment = TextAlignmentOptions.MidlineLeft;
            _kindTagLabel.raycastTarget = false;
            UGuiFontManager.ApplyFont(_kindTagLabel);

            // Module label
            GameObject modGo = CreateChildObject("ModuleLabel", parent);
            RectTransform modRect = modGo.GetComponent<RectTransform>();
            modRect.anchorMin = new Vector2(0f, 1f);
            modRect.anchorMax = new Vector2(0.72f, 1f);
            modRect.offsetMin = new Vector2(70f, -(y + 20f));
            modRect.offsetMax = new Vector2(-8f, -y);

            _moduleLabel = modGo.AddComponent<TextMeshProUGUI>();
            _moduleLabel.fontSize = LabelFontSize;
            _moduleLabel.color = AccentBrightGold;
            _moduleLabel.alignment = TextAlignmentOptions.MidlineLeft;
            _moduleLabel.raycastTarget = false;
            _moduleLabel.enableWordWrapping = false;
            UGuiFontManager.ApplyFont(_moduleLabel);

            // Enabled toggle + label
            GameObject enGo = CreateChildObject("EnabledToggle", parent);
            RectTransform enRect = enGo.GetComponent<RectTransform>();
            enRect.anchorMin = new Vector2(1f, 1f);
            enRect.anchorMax = new Vector2(1f, 1f);
            enRect.offsetMin = new Vector2(-180f, -(y + 20f));
            enRect.offsetMax = new Vector2(-8f, -y);

            _enabledLabel = enGo.AddComponent<TextMeshProUGUI>();
            _enabledLabel.fontSize = SmallFontSize;
            _enabledLabel.alignment = TextAlignmentOptions.Right;
            _enabledLabel.raycastTarget = false;
            UGuiFontManager.ApplyFont(_enabledLabel);

            // Toggle
            GameObject toggleGo = CreateChildObject("Toggle", enGo.transform);
            RectTransform tRect = toggleGo.GetComponent<RectTransform>();
            tRect.anchorMin = new Vector2(0f, 0.5f);
            tRect.anchorMax = new Vector2(0f, 0.5f);
            tRect.sizeDelta = new Vector2(16f, 16f);
            tRect.anchoredPosition = new Vector2(8f, 0f);

            Image checkBg = toggleGo.AddComponent<Image>();
            checkBg.color = Color.white;
            checkBg.raycastTarget = true;

            GameObject checkMarkGo = CreateChildObject("Checkmark", toggleGo.transform);
            Image checkmark = checkMarkGo.AddComponent<Image>();
            checkmark.color = new Color(0.24f, 0.35f, 0.55f);

            _enabledToggle = toggleGo.AddComponent<Toggle>();
            _enabledToggle.targetGraphic = checkBg;
            _enabledToggle.graphic = checkmark;
            _enabledToggle.onValueChanged.AddListener(val => OnEnabledToggled?.Invoke());
        }

        private void BuildToolbarRow(RectTransform parent, float y)
        {
            float btnWidth = 0.23f;
            float gap = 6f / 800f;

            BuildToolbarButton("UndoBtn", parent, 0f, btnWidth - gap, y, out _undoBtn, out _undoLabel);
            _undoBtn.onClick.AddListener(() => OnUndoClicked?.Invoke());

            BuildToolbarButton("RedoBtn", parent, btnWidth, btnWidth - gap, y, out _redoBtn, out _redoLabel);
            _redoBtn.onClick.AddListener(() => OnRedoClicked?.Invoke());

            BuildToolbarButton("SaveBtn", parent, btnWidth * 2f, btnWidth - gap, y, out _saveBtn, out _saveLabel);
            _saveBtn.onClick.AddListener(() => OnSaveClicked?.Invoke());

            BuildToolbarButton("ResetBtn", parent, btnWidth * 3f, btnWidth, y, out _resetBtn, out _resetLabel);
            _resetBtn.onClick.AddListener(() => OnResetClicked?.Invoke());
        }

        private void BuildToolbarButton(string name, RectTransform parent, float anchorX, float anchorWidth, float y,
            out Button btn, out TMP_Text label)
        {
            GameObject go = CreateChildObject(name, parent);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(anchorX, 1f);
            rect.anchorMax = new Vector2(anchorX + anchorWidth, 1f);
            rect.offsetMin = new Vector2(8f, -(y + 22f));
            rect.offsetMax = new Vector2(-8f, -y);

            Image bg = go.AddComponent<Image>();
            bg.color = ToolbarBtnBg;
            bg.raycastTarget = true;

            btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;

            GameObject lGo = CreateChildObject("Label", go.transform);
            RectTransform lRect = lGo.GetComponent<RectTransform>();
            lRect.anchorMin = Vector2.zero;
            lRect.anchorMax = Vector2.one;
            lRect.offsetMin = Vector2.zero;
            lRect.offsetMax = Vector2.zero;

            label = lGo.AddComponent<TextMeshProUGUI>();
            label.fontSize = SmallFontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            UGuiFontManager.ApplyFont(label);
        }

        private void BuildValidationLabel(RectTransform parent)
        {
            GameObject vGo = CreateChildObject("Validation", parent);
            RectTransform vRect = vGo.GetComponent<RectTransform>();
            vRect.anchorMin = new Vector2(0f, 0f);
            vRect.anchorMax = new Vector2(1f, 0f);
            vRect.offsetMin = new Vector2(8f, 4f);
            vRect.offsetMax = new Vector2(-8f, 24f);

            _validationLabel = vGo.AddComponent<TextMeshProUGUI>();
            _validationLabel.fontSize = SmallFontSize;
            _validationLabel.alignment = TextAlignmentOptions.MidlineLeft;
            _validationLabel.raycastTarget = false;
            UGuiFontManager.ApplyFont(_validationLabel);
        }

        protected override void RefreshData()
        {
            // Metadata
            if (_kindTagLabel != null) _kindTagLabel.text = _data.KindTag ?? string.Empty;
            if (_moduleLabel != null) _moduleLabel.text = _data.ModuleLabel ?? string.Empty;
            if (_enabledLabel != null) _enabledLabel.text = _data.EnabledLabel ?? string.Empty;
            if (_enabledToggle != null) _enabledToggle.isOn = _data.IsEnabled;

            // Toolbar
            if (_undoLabel != null) _undoLabel.text = _data.UndoLabel ?? string.Empty;
            if (_redoLabel != null) _redoLabel.text = _data.RedoLabel ?? string.Empty;
            if (_saveLabel != null) _saveLabel.text = _data.SaveLabel ?? string.Empty;
            if (_resetLabel != null) _resetLabel.text = _data.ResetLabel ?? string.Empty;

            // Toolbar enabled state
            if (_undoBtn != null)
            {
                Image bg = _undoBtn.targetGraphic as Image;
                if (bg != null) bg.color = _data.CanUndo ? ToolbarBtnBg : ToolbarBtnDisabledBg;
                _undoBtn.interactable = _data.CanUndo;
            }

            if (_redoBtn != null)
            {
                Image bg = _redoBtn.targetGraphic as Image;
                if (bg != null) bg.color = _data.CanRedo ? ToolbarBtnBg : ToolbarBtnDisabledBg;
                _redoBtn.interactable = _data.CanRedo;
            }

            // Validation
            if (_validationLabel != null)
            {
                _validationLabel.text = _data.ValidationText ?? string.Empty;
                switch (_data.Validation)
                {
                    case ValidationState.Warning:
                        _validationLabel.color = ValidationWarnColor;
                        break;
                    case ValidationState.Error:
                        _validationLabel.color = ValidationErrorColor;
                        break;
                    default:
                        _validationLabel.color = ValidationOkColor;
                        break;
                }
            }
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
