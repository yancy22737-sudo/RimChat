using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RimChat.UI.UGui.Panels
{
    /// <summary>
    /// Dependencies: UGuiCanvasHost, TMPro, Unity UI Button/Image/Layout.
    /// Responsibility: UGUI-based rendering of the prompt workspace header bar
    /// (title, channel root buttons, channel dropdown, quick actions, import/export).
    /// Replaces IMGUI DrawPromptWorkspaceHeader with retained-mode UGUI elements
    /// rendered once to a RenderTexture and displayed via GUI.DrawTexture.
    /// </summary>
    internal sealed class WorkspaceHeaderPanel : UGuiPanelBase
    {
        // Sub-elements
        private TMP_Text _titleLabel;
        private Button _diplomacyBtn;
        private TMP_Text _diplomacyLabel;
        private Button _rpgBtn;
        private TMP_Text _rpgLabel;
        private Image _channelDropdownBg;
        private TMP_Text _channelDropdownLabel;
        private TMP_Text _channelDropdownArrow;
        private Button _channelDropdownBtn;
        private Button _quickFactionBtn;
        private TMP_Text _quickFactionLabel;
        private Button _quickPawnBtn;
        private TMP_Text _quickPawnLabel;
        private Button _importBtn;
        private TMP_Text _importLabel;
        private Button _exportBtn;
        private TMP_Text _exportLabel;

        // Color constants matching IMGUI version
        private static readonly Color HeaderBg = new Color(0.07f, 0.08f, 0.10f);
        private static readonly Color AccentGold = new Color(0.95f, 0.74f, 0.26f);
        private static readonly Color AccentLightGold = new Color(1f, 0.88f, 0.55f);
        private static readonly Color ButtonSelectedBg = new Color(0.45f, 0.33f, 0.15f);
        private static readonly Color ButtonNormalBg = new Color(0.19f, 0.15f, 0.10f);
        private static readonly Color DropdownBgColor = new Color(0.25f, 0.18f, 0.08f);

        private const float TitleFontSize = 18f;
        private const float ButtonFontSize = 12f;
        private const float SmallFontSize = 11f;

        // Current data
        private HeaderData _data;

        internal struct HeaderData
        {
            public string Title;
            public string DiplomacyLabel;
            public string RpgLabel;
            public bool IsDiplomacySelected;
            public string ChannelLabel;
            public string QuickFactionLabel;
            public string QuickPawnLabel;
            public string ImportLabel;
            public string ExportLabel;
        }

        // Callbacks - set by the host before rendering
        internal Action OnDiplomacyClicked;
        internal Action OnRpgClicked;
        internal Action OnChannelDropdownClicked;
        internal Action OnQuickFactionClicked;
        internal Action OnQuickPawnClicked;
        internal Action OnImportClicked;
        internal Action OnExportClicked;

        internal void SetData(HeaderData data)
        {
            string sig = $"{data.Title}|{data.DiplomacyLabel}|{data.RpgLabel}|{data.IsDiplomacySelected}|{data.ChannelLabel}|{data.QuickFactionLabel}|{data.QuickPawnLabel}|{data.ImportLabel}|{data.ExportLabel}";
            _data = data;
            MarkDirtyIfChanged(sig);
        }

        protected override void BuildUI(RectTransform parent)
        {
            // Background
            Image bg = parent.gameObject.AddComponent<Image>();
            bg.color = HeaderBg;
            bg.raycastTarget = false;

            // Title label (top-left, medium font)
            GameObject titleGo = CreateChildObject("Title", parent);
            RectTransform titleRect = titleGo.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(0.42f, 1f);
            titleRect.offsetMin = new Vector2(8f, -36f);
            titleRect.offsetMax = new Vector2(-8f, -8f);

            _titleLabel = titleGo.AddComponent<TextMeshProUGUI>();
            _titleLabel.fontSize = TitleFontSize;
            _titleLabel.color = AccentGold;
            _titleLabel.alignment = TextAlignmentOptions.MidlineLeft;
            _titleLabel.raycastTarget = false;
            UGuiFontManager.ApplyFont(_titleLabel);

            // Root buttons row (Diplomacy + RPG)
            BuildRootButtons(parent);

            // Channel dropdown
            BuildChannelDropdown(parent);

            // Quick action buttons
            BuildQuickActions(parent);

            // Import / Export buttons
            BuildImportExport(parent);
        }

        private void BuildRootButtons(RectTransform parent)
        {
            // Diplomacy button
            GameObject dipGo = CreateChildObject("DiplomacyBtn", parent);
            RectTransform dipRect = dipGo.GetComponent<RectTransform>();
            dipRect.anchorMin = new Vector2(0f, 0f);
            dipRect.anchorMax = new Vector2(0f, 0f);
            dipRect.offsetMin = new Vector2(8f, 10f);
            dipRect.offsetMax = new Vector2(133f, 40f);

            Image dipBg = dipGo.AddComponent<Image>();
            dipBg.raycastTarget = true;
            _diplomacyBtn = dipGo.AddComponent<Button>();
            _diplomacyBtn.targetGraphic = dipBg;
            _diplomacyBtn.onClick.AddListener(() => OnDiplomacyClicked?.Invoke());

            GameObject dipLabelGo = CreateChildObject("Label", dipGo.transform);
            RectTransform dlRect = dipLabelGo.GetComponent<RectTransform>();
            dlRect.anchorMin = Vector2.zero;
            dlRect.anchorMax = Vector2.one;
            dlRect.offsetMin = Vector2.zero;
            dlRect.offsetMax = Vector2.zero;
            _diplomacyLabel = dipLabelGo.AddComponent<TextMeshProUGUI>();
            _diplomacyLabel.fontSize = ButtonFontSize;
            _diplomacyLabel.alignment = TextAlignmentOptions.Center;
            _diplomacyLabel.raycastTarget = false;
            UGuiFontManager.ApplyFont(_diplomacyLabel);

            // RPG button
            GameObject rpgGo = CreateChildObject("RpgBtn", parent);
            RectTransform rpgRect = rpgGo.GetComponent<RectTransform>();
            rpgRect.anchorMin = new Vector2(0f, 0f);
            rpgRect.anchorMax = new Vector2(0f, 0f);
            rpgRect.offsetMin = new Vector2(139f, 10f);
            rpgRect.offsetMax = new Vector2(264f, 40f);

            Image rpgBg = rpgGo.AddComponent<Image>();
            rpgBg.raycastTarget = true;
            _rpgBtn = rpgGo.AddComponent<Button>();
            _rpgBtn.targetGraphic = rpgBg;
            _rpgBtn.onClick.AddListener(() => OnRpgClicked?.Invoke());

            GameObject rpgLabelGo = CreateChildObject("Label", rpgGo.transform);
            RectTransform rlRect = rpgLabelGo.GetComponent<RectTransform>();
            rlRect.anchorMin = Vector2.zero;
            rlRect.anchorMax = Vector2.one;
            rlRect.offsetMin = Vector2.zero;
            rlRect.offsetMax = Vector2.zero;
            _rpgLabel = rpgLabelGo.AddComponent<TextMeshProUGUI>();
            _rpgLabel.fontSize = ButtonFontSize;
            _rpgLabel.alignment = TextAlignmentOptions.Center;
            _rpgLabel.raycastTarget = false;
            UGuiFontManager.ApplyFont(_rpgLabel);
        }

        private void BuildChannelDropdown(RectTransform parent)
        {
            GameObject ddGo = CreateChildObject("ChannelDropdown", parent);
            RectTransform ddRect = ddGo.GetComponent<RectTransform>();
            ddRect.anchorMin = new Vector2(0f, 0f);
            ddRect.anchorMax = new Vector2(0f, 0f);
            ddRect.offsetMin = new Vector2(270f, 10f);
            ddRect.offsetMax = new Vector2(570f, 40f);

            _channelDropdownBg = ddGo.AddComponent<Image>();
            _channelDropdownBg.color = DropdownBgColor;
            _channelDropdownBg.raycastTarget = true;

            _channelDropdownBtn = ddGo.AddComponent<Button>();
            _channelDropdownBtn.targetGraphic = _channelDropdownBg;
            _channelDropdownBtn.onClick.AddListener(() => OnChannelDropdownClicked?.Invoke());

            // Channel label
            GameObject chLabelGo = CreateChildObject("ChannelLabel", ddGo.transform);
            RectTransform clRect = chLabelGo.GetComponent<RectTransform>();
            clRect.anchorMin = Vector2.zero;
            clRect.anchorMax = new Vector2(0.9f, 1f);
            clRect.offsetMin = new Vector2(8f, 0f);
            clRect.offsetMax = new Vector2(-8f, 0f);
            _channelDropdownLabel = chLabelGo.AddComponent<TextMeshProUGUI>();
            _channelDropdownLabel.fontSize = ButtonFontSize;
            _channelDropdownLabel.color = AccentLightGold;
            _channelDropdownLabel.alignment = TextAlignmentOptions.MidlineLeft;
            _channelDropdownLabel.raycastTarget = false;
            UGuiFontManager.ApplyFont(_channelDropdownLabel);

            // Arrow
            GameObject arrowGo = CreateChildObject("Arrow", ddGo.transform);
            RectTransform arRect = arrowGo.GetComponent<RectTransform>();
            arRect.anchorMin = new Vector2(0.9f, 0f);
            arRect.anchorMax = Vector2.one;
            arRect.offsetMin = Vector2.zero;
            arRect.offsetMax = Vector2.zero;
            _channelDropdownArrow = arrowGo.AddComponent<TextMeshProUGUI>();
            _channelDropdownArrow.fontSize = SmallFontSize;
            _channelDropdownArrow.alignment = TextAlignmentOptions.Center;
            _channelDropdownArrow.raycastTarget = false;
            _channelDropdownArrow.text = "\u25BC";
        }

        private void BuildQuickActions(RectTransform parent)
        {
            // Quick Faction button
            GameObject qfGo = CreateChildObject("QuickFactionBtn", parent);
            RectTransform qfRect = qfGo.GetComponent<RectTransform>();
            qfRect.anchorMin = new Vector2(0f, 0f);
            qfRect.anchorMax = new Vector2(0f, 0f);
            qfRect.offsetMin = new Vector2(576f, 10f);
            qfRect.offsetMax = new Vector2(680f, 40f);

            Image qfBg = qfGo.AddComponent<Image>();
            qfBg.color = new Color(0.16f, 0.16f, 0.18f);
            qfBg.raycastTarget = true;
            _quickFactionBtn = qfGo.AddComponent<Button>();
            _quickFactionBtn.targetGraphic = qfBg;
            _quickFactionBtn.onClick.AddListener(() => OnQuickFactionClicked?.Invoke());

            GameObject qflGo = CreateChildObject("Label", qfGo.transform);
            RectTransform qflRect = qflGo.GetComponent<RectTransform>();
            qflRect.anchorMin = Vector2.zero;
            qflRect.anchorMax = Vector2.one;
            qflRect.offsetMin = Vector2.zero;
            qflRect.offsetMax = Vector2.zero;
            _quickFactionLabel = qflGo.AddComponent<TextMeshProUGUI>();
            _quickFactionLabel.fontSize = SmallFontSize;
            _quickFactionLabel.alignment = TextAlignmentOptions.Center;
            _quickFactionLabel.raycastTarget = false;
            UGuiFontManager.ApplyFont(_quickFactionLabel);

            // Quick Pawn button
            GameObject qpGo = CreateChildObject("QuickPawnBtn", parent);
            RectTransform qpRect = qpGo.GetComponent<RectTransform>();
            qpRect.anchorMin = new Vector2(0f, 0f);
            qpRect.anchorMax = new Vector2(0f, 0f);
            qpRect.offsetMin = new Vector2(686f, 10f);
            qpRect.offsetMax = new Vector2(790f, 40f);

            Image qpBg = qpGo.AddComponent<Image>();
            qpBg.color = new Color(0.16f, 0.16f, 0.18f);
            qpBg.raycastTarget = true;
            _quickPawnBtn = qpGo.AddComponent<Button>();
            _quickPawnBtn.targetGraphic = qpBg;
            _quickPawnBtn.onClick.AddListener(() => OnQuickPawnClicked?.Invoke());

            GameObject qplGo = CreateChildObject("Label", qpGo.transform);
            RectTransform qplRect = qplGo.GetComponent<RectTransform>();
            qplRect.anchorMin = Vector2.zero;
            qplRect.anchorMax = Vector2.one;
            qplRect.offsetMin = Vector2.zero;
            qplRect.offsetMax = Vector2.zero;
            _quickPawnLabel = qplGo.AddComponent<TextMeshProUGUI>();
            _quickPawnLabel.fontSize = SmallFontSize;
            _quickPawnLabel.alignment = TextAlignmentOptions.Center;
            _quickPawnLabel.raycastTarget = false;
            UGuiFontManager.ApplyFont(_quickPawnLabel);
        }

        private void BuildImportExport(RectTransform parent)
        {
            // Import button
            GameObject impGo = CreateChildObject("ImportBtn", parent);
            RectTransform impRect = impGo.GetComponent<RectTransform>();
            impRect.anchorMin = new Vector2(1f, 0f);
            impRect.anchorMax = new Vector2(1f, 0f);
            impRect.offsetMin = new Vector2(-180f, 10f);
            impRect.offsetMax = new Vector2(-96f, 40f);

            Image impBg = impGo.AddComponent<Image>();
            impBg.color = new Color(0.16f, 0.16f, 0.18f);
            impBg.raycastTarget = true;
            _importBtn = impGo.AddComponent<Button>();
            _importBtn.targetGraphic = impBg;
            _importBtn.onClick.AddListener(() => OnImportClicked?.Invoke());

            GameObject impLGo = CreateChildObject("Label", impGo.transform);
            RectTransform ilRect = impLGo.GetComponent<RectTransform>();
            ilRect.anchorMin = Vector2.zero;
            ilRect.anchorMax = Vector2.one;
            ilRect.offsetMin = Vector2.zero;
            ilRect.offsetMax = Vector2.zero;
            _importLabel = impLGo.AddComponent<TextMeshProUGUI>();
            _importLabel.fontSize = SmallFontSize;
            _importLabel.alignment = TextAlignmentOptions.Center;
            _importLabel.raycastTarget = false;
            UGuiFontManager.ApplyFont(_importLabel);

            // Export button
            GameObject expGo = CreateChildObject("ExportBtn", parent);
            RectTransform expRect = expGo.GetComponent<RectTransform>();
            expRect.anchorMin = new Vector2(1f, 0f);
            expRect.anchorMax = new Vector2(1f, 0f);
            expRect.offsetMin = new Vector2(-90f, 10f);
            expRect.offsetMax = new Vector2(-6f, 40f);

            Image expBg = expGo.AddComponent<Image>();
            expBg.color = new Color(0.16f, 0.16f, 0.18f);
            expBg.raycastTarget = true;
            _exportBtn = expGo.AddComponent<Button>();
            _exportBtn.targetGraphic = expBg;
            _exportBtn.onClick.AddListener(() => OnExportClicked?.Invoke());

            GameObject expLGo = CreateChildObject("Label", expGo.transform);
            RectTransform elRect = expLGo.GetComponent<RectTransform>();
            elRect.anchorMin = Vector2.zero;
            elRect.anchorMax = Vector2.one;
            elRect.offsetMin = Vector2.zero;
            elRect.offsetMax = Vector2.zero;
            _exportLabel = expLGo.AddComponent<TextMeshProUGUI>();
            _exportLabel.fontSize = SmallFontSize;
            _exportLabel.alignment = TextAlignmentOptions.Center;
            _exportLabel.raycastTarget = false;
            UGuiFontManager.ApplyFont(_exportLabel);
        }

        protected override void RefreshData()
        {
            if (_titleLabel != null) _titleLabel.text = _data.Title ?? string.Empty;

            // Root buttons
            if (_diplomacyLabel != null) _diplomacyLabel.text = _data.DiplomacyLabel ?? string.Empty;
            if (_rpgLabel != null) _rpgLabel.text = _data.RpgLabel ?? string.Empty;

            Image dipImg = _diplomacyBtn?.targetGraphic as Image;
            Image rpgImg = _rpgBtn?.targetGraphic as Image;
            if (dipImg != null) dipImg.color = _data.IsDiplomacySelected ? ButtonSelectedBg : ButtonNormalBg;
            if (rpgImg != null) rpgImg.color = !_data.IsDiplomacySelected ? ButtonSelectedBg : ButtonNormalBg;

            if (_diplomacyLabel != null) _diplomacyLabel.color = _data.IsDiplomacySelected ? AccentLightGold : Color.white;
            if (_rpgLabel != null) _rpgLabel.color = !_data.IsDiplomacySelected ? AccentLightGold : Color.white;

            // Channel dropdown
            if (_channelDropdownLabel != null) _channelDropdownLabel.text = _data.ChannelLabel ?? string.Empty;

            // Quick actions
            if (_quickFactionLabel != null) _quickFactionLabel.text = _data.QuickFactionLabel ?? string.Empty;
            if (_quickPawnLabel != null) _quickPawnLabel.text = _data.QuickPawnLabel ?? string.Empty;

            // Import/Export
            if (_importLabel != null) _importLabel.text = _data.ImportLabel ?? string.Empty;
            if (_exportLabel != null) _exportLabel.text = _data.ExportLabel ?? string.Empty;
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
