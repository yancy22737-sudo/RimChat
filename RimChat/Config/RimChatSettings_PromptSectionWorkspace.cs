using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Persistence;
using RimChat.Prompting;
using RimChat.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: prompt preset service, prompt section catalog, aggregate preview builder, shared variable browser, and chip editor.
    /// Responsibility: render the stable section-driven prompt workspace for the native PromptSectionCatalog.
    /// </summary>
    public partial class RimChatSettings : ModSettings
    {
        private string _promptWorkspaceSelectedSectionId = "system_rules";
        private string _promptWorkspaceSelectedNodeId = "fact_grounding";
        private bool _promptWorkspaceEditNodeMode;
        private string _promptWorkspaceBufferedChannel = string.Empty;
        private string _promptWorkspaceBufferedSectionId = string.Empty;
        private string _promptWorkspaceBufferedNodeId = string.Empty;
        private bool _promptWorkspaceBufferedNodeMode;
        private string _promptWorkspaceEditorBuffer = string.Empty;
        private Vector2 _promptWorkspaceSectionScroll = Vector2.zero;
        private Vector2 _promptWorkspaceNodeScroll = Vector2.zero;
        private Vector2 _promptWorkspaceEditorScroll = Vector2.zero;
        private Vector2 _promptWorkspacePreviewScroll = Vector2.zero;
        private Vector2 _promptWorkspaceReportScroll = Vector2.zero;
        private PromptWorkbenchChannel _promptWorkspacePreviewCachedRoot;
        private string _promptWorkspacePreviewCachedChannel = string.Empty;
        private string _promptWorkspacePreviewCachedSignature = string.Empty;
        private PromptWorkspaceStructuredPreview _promptWorkspacePreviewCachedData;
        private bool _promptWorkspacePreviewCacheValid;
        private bool _promptWorkspaceHasPendingPersist;
        private DateTime _promptWorkspaceLastEditUtc = DateTime.MinValue;
        private const int PromptWorkspaceAutoSaveDebounceMs = 500;
        private TemplateVariableValidationResult _promptWorkspaceValidationResult = new TemplateVariableValidationResult();
        private string _promptWorkspaceValidationSignature = string.Empty;
        private int _promptWorkspaceValidationCooldown;
        private const int PromptWorkspaceValidationRefreshTicks = 15;
        private PromptWorkbenchChipEditor _promptWorkspaceChipEditor;
        private PromptWorkspaceStructuredPreviewRenderer _promptWorkspacePreviewRenderer;
        private bool _promptWorkspaceChipEditorDisabledForSession;
        private string _promptWorkspaceDraggingNodeId = string.Empty;
        private string _promptWorkspaceDropTargetNodeId = string.Empty;

        private void DrawPromptSectionWorkspace(Rect root)
        {
            EnsurePresetStoreReady();
            EnsurePromptWorkspaceSelection();
            TryAutoSavePromptWorkspaceBuffer();

            Widgets.DrawBoxSolid(root, new Color(0.08f, 0.09f, 0.11f));
            Rect frame = root.ContractedBy(8f);
            Rect headerRect = new Rect(frame.x, frame.y, frame.width, 74f);
            Rect bodyRect = new Rect(frame.x, headerRect.yMax + 6f, frame.width, frame.height - headerRect.height - 6f);

            DrawPromptWorkspaceHeader(headerRect);
            DrawPromptWorkspaceBody(bodyRect);
        }

        private void DrawPromptWorkspaceHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.07f, 0.08f, 0.10f));
            Rect inner = rect.ContractedBy(8f);

            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(0.95f, 0.74f, 0.26f);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width * 0.42f, 28f), "RimChat_Tab_PromptWorkbench".Translate());
            GUI.color = Color.white;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;

            float top = inner.y + 34f;
            DrawPromptWorkspaceRootButtons(new Rect(inner.x, top, 250f, 30f));
            DrawPromptWorkspaceChannelDropdown(new Rect(inner.x + 260f, top, 300f, 30f));
            DrawPromptWorkspaceQuickActions(new Rect(inner.x + 570f, top, Mathf.Max(220f, inner.xMax - (inner.x + 570f) - 196f), 30f));

            Rect importRect = new Rect(inner.xMax - 180f, top, 84f, 30f);
            Rect exportRect = new Rect(inner.xMax - 90f, top, 84f, 30f);
            if (Widgets.ButtonText(importRect, "RimChat_Import".Translate()))
            {
                PersistPromptWorkspaceBufferNow();
                ShowImportPresetDialog();
            }

            if (Widgets.ButtonText(exportRect, "RimChat_Export".Translate()))
            {
                PersistPromptWorkspaceBufferNow();
                ShowExportPresetDialog();
            }
        }

        private void DrawPromptWorkspaceRootButtons(Rect rect)
        {
            float width = (rect.width - 6f) * 0.5f;
            Rect diplomacyRect = new Rect(rect.x, rect.y, width, rect.height);
            Rect rpgRect = new Rect(diplomacyRect.xMax + 6f, rect.y, width, rect.height);
            DrawPromptWorkspaceRootButton(diplomacyRect, PromptWorkbenchChannel.Diplomacy, "RimChat_PromptWorkbench_ChannelDiplomacy");
            DrawPromptWorkspaceRootButton(rpgRect, PromptWorkbenchChannel.Rpg, "RimChat_PromptWorkbench_ChannelRpg");
        }

        private void DrawPromptWorkspaceRootButton(Rect rect, PromptWorkbenchChannel channel, string key)
        {
            bool selected = _workbenchChannel == channel;
            Widgets.DrawBoxSolid(rect, selected ? new Color(0.45f, 0.33f, 0.15f) : new Color(0.19f, 0.15f, 0.10f));
            Widgets.DrawBox(rect, 1);
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = selected ? new Color(1f, 0.88f, 0.55f) : Color.white;
            Widgets.Label(rect, key.Translate());
            GUI.color = Color.white;
            Text.Anchor = oldAnchor;
            if (Widgets.ButtonInvisible(rect))
            {
                SetPromptWorkspaceRoot(channel);
            }
        }

        private void DrawPromptWorkspaceChannelDropdown(Rect rect)
        {
            string currentChannel = EnsurePromptWorkspaceSelection();
            Widgets.DrawBoxSolid(rect, new Color(0.25f, 0.18f, 0.08f));
            Widgets.DrawBox(rect, 1);
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(1f, 0.88f, 0.55f);
            Widgets.Label(new Rect(rect.x + 8f, rect.y, rect.width - 30f, rect.height), RimTalkPromptEntryChannelCatalog.GetLabel(currentChannel));
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(rect.xMax - 22f, rect.y, 18f, rect.height), "▼");
            GUI.color = Color.white;
            Text.Anchor = oldAnchor;
            if (Widgets.ButtonInvisible(rect))
            {
                List<FloatMenuOption> options = GetPromptWorkspaceChannels()
                    .Select(channelId => new FloatMenuOption(
                        RimTalkPromptEntryChannelCatalog.GetLabel(channelId),
                        () => SetPromptWorkspaceChannel(channelId)))
                    .ToList();
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private void DrawPromptWorkspaceBody(Rect rect)
        {
            float gap = 8f;
            float leftWidth = Mathf.Clamp(rect.width * 0.24f, 240f, 300f);
            float remainingWidth = Mathf.Max(1f, rect.width - leftWidth - gap * 2f);
            float middleWidth = Mathf.Clamp(remainingWidth * 0.60f, 360f, Mathf.Max(360f, remainingWidth - 300f));
            float rightWidth = Mathf.Max(260f, remainingWidth - middleWidth);
            if (middleWidth + rightWidth > remainingWidth)
            {
                rightWidth = Mathf.Max(220f, remainingWidth - middleWidth);
            }

            Rect leftRect = new Rect(rect.x, rect.y, leftWidth, rect.height);
            Rect middleRect = new Rect(leftRect.xMax + gap, rect.y, middleWidth, rect.height);
            Rect rightRect = new Rect(middleRect.xMax + gap, rect.y, Mathf.Max(1f, rect.xMax - (middleRect.xMax + gap)), rect.height);

            DrawPromptWorkspacePresetPanel(leftRect);
            DrawPromptWorkspaceEditorPanel(middleRect);
            DrawPromptWorkspaceSidePanel(rightRect);
        }

        private void DrawPromptWorkspacePresetPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.09f, 0.10f, 0.12f));
            Rect inner = rect.ContractedBy(8f);
            float y = inner.y;
            Rect bottomActionsRect = new Rect(inner.x, inner.yMax - 80f, inner.width, 80f);
            Widgets.Label(new Rect(inner.x, y, inner.width, 22f), "RimChat_PromptWorkbench_PresetHeader".Translate());
            y += 24f;
            DrawPresetActions(new Rect(inner.x, y, inner.width, 24f));
            y += 28f;
            float presetListHeight = ResolvePromptWorkspacePresetListHeight(y, bottomActionsRect.y, inner.height);
            DrawPresetList(new Rect(inner.x, y, inner.width, presetListHeight));
            y += presetListHeight + 8f;

            Widgets.Label(
                new Rect(inner.x, y, inner.width, 22f),
                (_promptWorkspaceEditNodeMode
                    ? "RimChat_PromptWorkspaceNodeLayoutHeader"
                    : "RimChat_PromptWorkspaceSectionHeader").Translate());
            y += 24f;
            float sectionHeight = Mathf.Max(72f, bottomActionsRect.y - y - 6f);
            if (_promptWorkspaceEditNodeMode)
            {
                DrawPromptWorkspaceNodeLayoutList(new Rect(inner.x, y, inner.width, sectionHeight));
            }
            else
            {
                DrawPromptWorkspaceSectionList(new Rect(inner.x, y, inner.width, sectionHeight));
            }
            DrawPresetBottomActions(bottomActionsRect);
        }

        private void DrawPromptWorkspaceEditorPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.06f, 0.07f, 0.09f));
            Rect inner = rect.ContractedBy(8f);
            float y = inner.y;
            const float validationHeight = 24f;

            DrawWorkbenchPresetNameRow(inner, ref y);
            DrawPromptWorkspaceToolbar(new Rect(inner.x, y, inner.width, 26f));
            y += 32f;
            DrawPromptWorkspaceEditModeSwitch(new Rect(inner.x, y, inner.width, 24f));
            y += 26f;
            if (_promptWorkspaceEditNodeMode)
            {
                DrawPromptWorkspaceNodeSelector(new Rect(inner.x, y, inner.width, 24f));
                y += 26f;
            }
            else
            {
                PromptSectionSchemaCatalog.TryGetSection(_promptWorkspaceSelectedSectionId, out PromptSectionSchemaItem section);
                Widgets.Label(new Rect(inner.x, y, inner.width, 22f), section.GetDisplayLabel());
                y += 24f;
            }

            float editorHeight = Mathf.Max(24f, inner.yMax - y - validationHeight - 4f);
            Rect editorRect = new Rect(inner.x, y, inner.width, editorHeight);
            string edited = DrawPromptWorkspaceEditor(editorRect, GetPromptWorkspaceCurrentEditorText());
            DrawPromptWorkspaceValidationStatus(
                new Rect(inner.x, editorRect.yMax + 4f, inner.width, validationHeight),
                edited);

            if (!string.Equals(edited, _promptWorkspaceEditorBuffer, StringComparison.Ordinal))
            {
                SetPromptWorkspaceCurrentEditorText(edited);
            }
        }

        private void DrawPromptWorkspaceValidationStatus(Rect rect, string templateText)
        {
            TemplateVariableValidationContext validationContext = BuildPromptWorkspaceValidationContext();
            UpdatePromptWorkspaceValidationState(templateText, validationContext);
            string statusText = BuildLiveValidationStatusText(_promptWorkspaceValidationResult, templateText);
            Color oldColor = GUI.color;
            GUI.color = ResolveLiveValidationStatusColor(_promptWorkspaceValidationResult, templateText);
            Widgets.Label(rect, statusText);
            GUI.color = oldColor;
        }

        private TemplateVariableValidationContext BuildPromptWorkspaceValidationContext()
        {
            return _promptWorkspaceEditNodeMode
                ? TemplateVariableValidationContext.ForPromptWorkspaceNode(
                    GetPromptWorkspaceRootChannel(),
                    _workbenchPromptChannel,
                    _promptWorkspaceSelectedNodeId)
                : TemplateVariableValidationContext.ForPromptWorkspaceSection(
                    GetPromptWorkspaceRootChannel(),
                    _workbenchPromptChannel,
                    _promptWorkspaceSelectedSectionId);
        }

        private void UpdatePromptWorkspaceValidationState(
            string templateText,
            TemplateVariableValidationContext validationContext)
        {
            string contextSignature = validationContext?.Signature ?? "runtime.default";
            string signature = contextSignature + "\n" + (templateText ?? string.Empty);
            _promptWorkspaceValidationCooldown = Math.Max(0, _promptWorkspaceValidationCooldown - 1);
            if (_promptWorkspaceValidationCooldown > 0 &&
                string.Equals(signature, _promptWorkspaceValidationSignature, StringComparison.Ordinal))
            {
                return;
            }

            _promptWorkspaceValidationSignature = signature;
            _promptWorkspaceValidationCooldown = PromptWorkspaceValidationRefreshTicks;
            _promptWorkspaceValidationResult = string.IsNullOrWhiteSpace(templateText)
                ? new TemplateVariableValidationResult()
                : PromptPersistenceService.Instance.ValidateTemplateVariables(templateText, validationContext);
        }

        private void DrawPromptWorkspaceToolbar(Rect rect)
        {
            float buttonWidth = (rect.width - 6f) * 0.5f;
            Rect restoreSectionRect = new Rect(rect.x, rect.y, buttonWidth, rect.height);
            Rect restoreChannelRect = new Rect(restoreSectionRect.xMax + 6f, rect.y, buttonWidth, rect.height);

            if (Widgets.ButtonText(restoreSectionRect, "RimChat_PromptSectionRestoreSection".Translate()))
            {
                RestorePromptWorkspaceCurrentEntry();
            }

            if (Widgets.ButtonText(restoreChannelRect, "RimChat_PromptSectionRestoreChannel".Translate()))
            {
                RestorePromptWorkspaceCurrentChannel();
            }
        }

        private void DrawPromptWorkspaceEditModeSwitch(Rect rect)
        {
            float buttonWidth = (rect.width - 6f) * 0.5f;
            Rect sectionRect = new Rect(rect.x, rect.y, buttonWidth, rect.height);
            Rect nodeRect = new Rect(sectionRect.xMax + 6f, rect.y, buttonWidth, rect.height);
            DrawPromptWorkspaceModeButton(sectionRect, false, "RimChat_PromptWorkspaceMode_Sections".Translate().ToString());
            DrawPromptWorkspaceModeButton(nodeRect, true, "RimChat_PromptWorkspaceMode_Nodes".Translate().ToString());
        }

        private void DrawPromptWorkspaceModeButton(Rect rect, bool nodeMode, string label)
        {
            bool selected = _promptWorkspaceEditNodeMode == nodeMode;
            Widgets.DrawBoxSolid(rect, selected ? new Color(0.24f, 0.35f, 0.55f) : new Color(0.13f, 0.15f, 0.18f));
            Widgets.DrawBox(rect, 1);
            TextAnchor old = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label);
            Text.Anchor = old;
            if (Widgets.ButtonInvisible(rect) && _promptWorkspaceEditNodeMode != nodeMode)
            {
                PersistPromptWorkspaceBufferNow();
                _promptWorkspaceEditNodeMode = nodeMode;
                EnsurePromptWorkspaceBuffer();
                InvalidatePromptWorkspacePreviewCache();
            }
        }

        private void DrawPromptWorkspaceNodeSelector(Rect rect)
        {
            string current = PromptUnifiedNodeSchemaCatalog.GetDisplayLabel(_promptWorkspaceSelectedNodeId);
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.14f, 0.18f));
            Widgets.DrawBox(rect, 1);
            Widgets.Label(new Rect(rect.x + 8f, rect.y, rect.width - 28f, rect.height), current);
            TextAnchor old = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(rect.xMax - 22f, rect.y, 18f, rect.height), "▼");
            Text.Anchor = old;
            if (!Widgets.ButtonInvisible(rect))
            {
                return;
            }

            List<FloatMenuOption> options = PromptUnifiedNodeSchemaCatalog.GetAll()
                .Select(node => new FloatMenuOption(PromptUnifiedNodeSchemaCatalog.GetDisplayLabel(node.Id), () =>
                {
                    PersistPromptWorkspaceBufferNow();
                    _promptWorkspaceSelectedNodeId = node.Id;
                    EnsurePromptWorkspaceBuffer();
                    InvalidatePromptWorkspacePreviewCache();
                }))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void DrawPromptWorkspaceSectionList(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.03f, 0.03f, 0.04f));
            Rect inner = rect.ContractedBy(6f);
            IReadOnlyList<PromptSectionSchemaItem> sections = PromptSectionSchemaCatalog.GetMainChainSections();
            float rowHeight = 30f;
            Rect viewRect = new Rect(0f, 0f, inner.width - 16f, Mathf.Max(inner.height, sections.Count * rowHeight));
            Widgets.BeginScrollView(inner, ref _promptWorkspaceSectionScroll, viewRect);

            for (int i = 0; i < sections.Count; i++)
            {
                PromptSectionSchemaItem section = sections[i];
                Rect rowRect = new Rect(0f, i * rowHeight, viewRect.width, rowHeight - 2f);
                bool selected = string.Equals(_promptWorkspaceSelectedSectionId, section.Id, StringComparison.OrdinalIgnoreCase);
                if (selected)
                {
                    Widgets.DrawBoxSolid(rowRect, new Color(0.24f, 0.35f, 0.55f));
                }
                else if (Mouse.IsOver(rowRect))
                {
                    Widgets.DrawBoxSolid(rowRect, new Color(0.18f, 0.18f, 0.20f));
                }

                if (Widgets.ButtonInvisible(rowRect))
                {
                    SelectPromptWorkspaceSection(section.Id);
                }

                Widgets.Label(new Rect(rowRect.x + 8f, rowRect.y + 4f, rowRect.width - 16f, rowRect.height - 8f), section.GetDisplayLabel());
            }

            Widgets.EndScrollView();
        }

        private void DrawPromptWorkspaceNodeLayoutList(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.03f, 0.03f, 0.04f));
            Rect inner = rect.ContractedBy(6f);
            List<PromptUnifiedNodeLayoutConfig> layouts = GetPromptWorkspaceNodeLayouts();
            float rowHeight = 28f;
            float headerHeight = 20f;
            float totalRows = layouts.Count + 5;
            Rect viewRect = new Rect(0f, 0f, inner.width - 16f, Mathf.Max(inner.height, totalRows * rowHeight + 5f * headerHeight));
            Widgets.BeginScrollView(inner, ref _promptWorkspaceNodeScroll, viewRect);

            float y = 0f;
            foreach (PromptUnifiedNodeSlot slot in Enum.GetValues(typeof(PromptUnifiedNodeSlot)))
            {
                List<PromptUnifiedNodeLayoutConfig> slotItems = layouts
                    .Where(item => item.GetSlot() == slot)
                    .OrderBy(item => item.Order)
                    .ThenBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                Rect headerRect = new Rect(0f, y, viewRect.width, headerHeight);
                Widgets.DrawBoxSolid(headerRect, new Color(0.12f, 0.14f, 0.18f));
                Widgets.Label(new Rect(headerRect.x + 6f, headerRect.y + 1f, headerRect.width - 12f, headerRect.height - 2f), GetPromptNodeSlotLabel(slot));
                y += headerHeight;

                for (int i = 0; i < slotItems.Count; i++)
                {
                    PromptUnifiedNodeLayoutConfig item = slotItems[i];
                    Rect rowRect = new Rect(0f, y, viewRect.width, rowHeight - 2f);
                    bool selected = string.Equals(_promptWorkspaceSelectedNodeId, item.NodeId, StringComparison.OrdinalIgnoreCase);
                    if (selected)
                    {
                        Widgets.DrawBoxSolid(rowRect, new Color(0.24f, 0.35f, 0.55f));
                    }
                    else if (Mouse.IsOver(rowRect))
                    {
                        Widgets.DrawBoxSolid(rowRect, new Color(0.18f, 0.18f, 0.20f));
                    }

                    Rect toggleRect = new Rect(rowRect.x + 4f, rowRect.y + 4f, 18f, rowRect.height - 8f);
                    bool enabled = item.Enabled;
                    Widgets.Checkbox(toggleRect.position, ref enabled, rowRect.height - 8f, false);
                    if (enabled != item.Enabled)
                    {
                        item.Enabled = enabled;
                        SavePromptWorkspaceNodeLayouts(layouts);
                    }

                    Rect labelRect = new Rect(toggleRect.xMax + 4f, rowRect.y + 4f, rowRect.width - 130f, rowRect.height - 8f);
                    Widgets.Label(labelRect, PromptUnifiedNodeSchemaCatalog.GetDisplayLabel(item.NodeId));
                    if (Widgets.ButtonInvisible(labelRect))
                    {
                        PersistPromptWorkspaceBufferNow();
                        _promptWorkspaceSelectedNodeId = item.NodeId;
                        EnsurePromptWorkspaceBuffer();
                        InvalidatePromptWorkspacePreviewCache();
                    }

                    DrawNodeLayoutRowButtons(layouts, item, rowRect);
                    HandleNodeLayoutDrag(layouts, item, rowRect);
                    y += rowHeight;
                }
            }

            Widgets.EndScrollView();
        }

        private void DrawNodeLayoutRowButtons(
            List<PromptUnifiedNodeLayoutConfig> layouts,
            PromptUnifiedNodeLayoutConfig item,
            Rect rowRect)
        {
            Rect upRect = new Rect(rowRect.xMax - 72f, rowRect.y + 3f, 20f, rowRect.height - 6f);
            Rect downRect = new Rect(rowRect.xMax - 50f, rowRect.y + 3f, 20f, rowRect.height - 6f);
            Rect slotRect = new Rect(rowRect.xMax - 28f, rowRect.y + 3f, 24f, rowRect.height - 6f);
            if (Widgets.ButtonText(upRect, "▲"))
            {
                MovePromptNodeLayout(layouts, item.NodeId, -1);
            }

            if (Widgets.ButtonText(downRect, "▼"))
            {
                MovePromptNodeLayout(layouts, item.NodeId, 1);
            }

            if (Widgets.ButtonText(slotRect, "↕"))
            {
                ShowPromptNodeSlotMenu(layouts, item);
            }
        }

        private void HandleNodeLayoutDrag(
            List<PromptUnifiedNodeLayoutConfig> layouts,
            PromptUnifiedNodeLayoutConfig item,
            Rect rowRect)
        {
            Event evt = Event.current;
            if (evt == null)
            {
                return;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0 && rowRect.Contains(evt.mousePosition))
            {
                _promptWorkspaceDraggingNodeId = item.NodeId;
            }

            if (evt.type == EventType.MouseDrag &&
                !string.IsNullOrWhiteSpace(_promptWorkspaceDraggingNodeId) &&
                rowRect.Contains(evt.mousePosition))
            {
                _promptWorkspaceDropTargetNodeId = item.NodeId;
            }

            if (evt.type == EventType.MouseUp && evt.button == 0)
            {
                if (!string.IsNullOrWhiteSpace(_promptWorkspaceDraggingNodeId) &&
                    !string.IsNullOrWhiteSpace(_promptWorkspaceDropTargetNodeId) &&
                    !string.Equals(_promptWorkspaceDraggingNodeId, _promptWorkspaceDropTargetNodeId, StringComparison.OrdinalIgnoreCase))
                {
                    MovePromptNodeLayoutToTarget(layouts, _promptWorkspaceDraggingNodeId, _promptWorkspaceDropTargetNodeId);
                }

                _promptWorkspaceDraggingNodeId = string.Empty;
                _promptWorkspaceDropTargetNodeId = string.Empty;
            }
        }

        private List<PromptUnifiedNodeLayoutConfig> GetPromptWorkspaceNodeLayouts()
        {
            return GetPromptNodeLayouts(_workbenchPromptChannel)
                .Select(item => item.Clone())
                .OrderBy(item => item.GetSlot())
                .ThenBy(item => item.Order)
                .ThenBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void SavePromptWorkspaceNodeLayouts(List<PromptUnifiedNodeLayoutConfig> layouts)
        {
            SavePromptNodeLayouts(_workbenchPromptChannel, layouts);
            InvalidatePromptWorkspacePreviewCache();
            EnsurePromptWorkspaceBuffer();
        }

        private void MovePromptNodeLayout(List<PromptUnifiedNodeLayoutConfig> layouts, string nodeId, int direction)
        {
            PromptUnifiedNodeLayoutConfig current = layouts.FirstOrDefault(item =>
                string.Equals(item.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));
            if (current == null)
            {
                return;
            }

            PromptUnifiedNodeSlot slot = current.GetSlot();
            List<PromptUnifiedNodeLayoutConfig> slotItems = layouts
                .Where(item => item.GetSlot() == slot)
                .OrderBy(item => item.Order)
                .ThenBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            int index = slotItems.FindIndex(item => string.Equals(item.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));
            int target = index + direction;
            if (index < 0 || target < 0 || target >= slotItems.Count)
            {
                return;
            }

            PromptUnifiedNodeLayoutConfig source = slotItems[index];
            PromptUnifiedNodeLayoutConfig destination = slotItems[target];
            int tempOrder = source.Order;
            source.Order = destination.Order;
            destination.Order = tempOrder;
            SavePromptWorkspaceNodeLayouts(layouts);
        }

        private void MovePromptNodeLayoutToTarget(List<PromptUnifiedNodeLayoutConfig> layouts, string dragNodeId, string targetNodeId)
        {
            PromptUnifiedNodeLayoutConfig drag = layouts.FirstOrDefault(item =>
                string.Equals(item.NodeId, dragNodeId, StringComparison.OrdinalIgnoreCase));
            PromptUnifiedNodeLayoutConfig target = layouts.FirstOrDefault(item =>
                string.Equals(item.NodeId, targetNodeId, StringComparison.OrdinalIgnoreCase));
            if (drag == null || target == null)
            {
                return;
            }

            drag.Slot = target.Slot;
            drag.Order = target.Order;
            MovePromptNodeLayout(layouts, drag.NodeId, 1);
            SavePromptWorkspaceNodeLayouts(layouts);
        }

        private void ShowPromptNodeSlotMenu(List<PromptUnifiedNodeLayoutConfig> layouts, PromptUnifiedNodeLayoutConfig node)
        {
            List<FloatMenuOption> options = Enum.GetValues(typeof(PromptUnifiedNodeSlot))
                .Cast<PromptUnifiedNodeSlot>()
                .Select(slot => new FloatMenuOption(GetPromptNodeSlotLabel(slot), () =>
                {
                    node.Slot = slot.ToSerializedValue();
                    SavePromptWorkspaceNodeLayouts(layouts);
                }))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static string GetPromptNodeSlotLabel(PromptUnifiedNodeSlot slot)
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

        private string DrawPromptWorkspaceEditor(Rect rect, string text)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.03f, 0.03f, 0.04f));
            Rect inner = rect.ContractedBy(6f);
            if (_promptWorkspaceChipEditorDisabledForSession || ExceedsChipEditorSoftLimits(text))
            {
                return DrawPromptWorkspaceLegacyTextArea(inner, text);
            }

            try
            {
                _promptWorkspaceChipEditor ??= new PromptWorkbenchChipEditor("RimChat_PromptWorkspaceSectionEditor");
                return _promptWorkspaceChipEditor.Draw(inner, text, ref _promptWorkspaceEditorScroll);
            }
            catch (Exception ex)
            {
                _promptWorkspaceChipEditorDisabledForSession = true;
                Log.Warning($"[RimChat] Prompt workspace chip editor fallback activated: {ex.GetType().Name}: {ex.Message}");
                return DrawPromptWorkspaceLegacyTextArea(inner, text);
            }
        }

        private void DrawPromptWorkspaceSidePanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.09f, 0.10f, 0.12f));
            Rect inner = rect.ContractedBy(8f);
            float buttonWidth = (inner.width - 12f) / 3f;
            Rect previewRect = new Rect(inner.x, inner.y, buttonWidth, 24f);
            Rect varsRect = new Rect(previewRect.xMax + 6f, inner.y, buttonWidth, 24f);
            Rect reportRect = new Rect(varsRect.xMax + 6f, inner.y, buttonWidth, 24f);

            DrawWorkbenchSideButton(previewRect, PromptWorkbenchInfoPanel.Preview, "RimChat_PreviewTitleShort");
            DrawWorkbenchSideButton(varsRect, PromptWorkbenchInfoPanel.Variables, "RimChat_PromptWorkbench_VariablesTab");
            DrawWorkbenchSideButton(reportRect, PromptWorkbenchInfoPanel.Help, "RimChat_PromptWorkbench_GuideTab");

            Rect contentRect = new Rect(inner.x, previewRect.yMax + 6f, inner.width, inner.height - 30f);
            switch (_workbenchSidePanelTab)
            {
                case PromptWorkbenchInfoPanel.Preview:
                    DrawPromptWorkspacePreview(contentRect);
                    break;
                case PromptWorkbenchInfoPanel.Variables:
                    DrawPromptWorkspaceVariables(contentRect);
                    break;
                default:
                    DrawPromptWorkspaceReport(contentRect);
                    break;
            }
        }

        private void DrawPromptWorkspacePreview(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.03f, 0.03f, 0.04f));
            Rect inner = rect.ContractedBy(6f);
            PromptWorkspaceStructuredPreview preview = GetPromptWorkspaceStructuredPreview();
            DrawPromptWorkspaceStructuredPreview(inner, preview);
        }

        private void DrawPromptWorkspaceVariables(Rect rect)
        {
            DrawPromptVariableBrowser(
                rect,
                _promptWorkspaceEditorBuffer,
                entry =>
                {
                    string token = "{{ " + (entry?.Path ?? string.Empty).Trim() + " }}";
                    return TryInsertVariableTokenToPromptWorkspace(token);
                },
                showCustomCrud: true);
        }

        private void DrawPromptWorkspaceReport(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.03f, 0.03f, 0.04f));
            Rect inner = rect.ContractedBy(6f);
            LegacyPromptMigrationReport report = PromptLegacyCompatMigration.GetLatestReport();
            string summary =
                $"Source: {report.SourceId}\n" +
                $"Imported: {report.ImportedCount}\n" +
                $"Rewritten: {report.RewrittenCount}\n" +
                $"Rejected: {report.RejectedCount}\n" +
                $"Defaulted: {report.DefaultedCount}\n\n";
            List<string> lines = report.Entries
                .Select(entry =>
                    $"- [{entry.Status}] {entry.PromptChannel}/{entry.SectionId} :: {entry.Detail}" +
                    (entry.FallbackApplied ? " (defaulted)" : string.Empty))
                .ToList();
            string body = summary + (lines.Count == 0 ? "No legacy import activity recorded in this session." : string.Join("\n", lines));
            float contentHeight = Mathf.Max(inner.height, Text.CalcHeight(body, inner.width - 16f) + 12f);
            Rect viewRect = new Rect(0f, 0f, inner.width - 16f, contentHeight);
            _promptWorkspaceReportScroll = GUI.BeginScrollView(inner, _promptWorkspaceReportScroll, viewRect);
            Widgets.Label(new Rect(0f, 0f, viewRect.width, contentHeight), body);
            GUI.EndScrollView();
        }

        private void SetPromptWorkspaceRoot(PromptWorkbenchChannel root)
        {
            PersistPromptWorkspaceBufferNow();
            _workbenchChannel = root;
            _workbenchPromptChannel = string.Empty;
            InvalidatePromptWorkspacePreviewCache();
            EnsurePromptWorkspaceSelection();
        }

        private RimTalkPromptChannel GetPromptWorkspaceRootChannel()
        {
            return _workbenchChannel == PromptWorkbenchChannel.Rpg
                ? RimTalkPromptChannel.Rpg
                : RimTalkPromptChannel.Diplomacy;
        }

        private IReadOnlyList<string> GetPromptWorkspaceChannels()
        {
            return PromptSectionSchemaCatalog.GetWorkspaceChannels(GetPromptWorkspaceRootChannel());
        }

        private string EnsurePromptWorkspaceSelection()
        {
            IReadOnlyList<string> channels = GetPromptWorkspaceChannels();
            string normalizedChannel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(_workbenchPromptChannel);
            if (!channels.Contains(normalizedChannel, StringComparer.Ordinal))
            {
                normalizedChannel = PromptSectionSchemaCatalog.GetDefaultWorkspaceChannel(GetPromptWorkspaceRootChannel());
            }

            _workbenchPromptChannel = normalizedChannel;
            if (!PromptSectionSchemaCatalog.TryGetSection(_promptWorkspaceSelectedSectionId, out PromptSectionSchemaItem _))
            {
                _promptWorkspaceSelectedSectionId = PromptSectionSchemaCatalog.GetMainChainSections()[0].Id;
            }

            EnsurePromptWorkspaceBuffer();
            return _workbenchPromptChannel;
        }

        private void SetPromptWorkspaceChannel(string channelId)
        {
            PersistPromptWorkspaceBufferNow();
            _workbenchPromptChannel = RimTalkPromptEntryChannelCatalog.NormalizeForRoot(channelId, GetPromptWorkspaceRootChannel());
            InvalidatePromptWorkspacePreviewCache();
            EnsurePromptWorkspaceSelection();
        }

        private void SelectPromptWorkspaceSection(string sectionId)
        {
            PersistPromptWorkspaceBufferNow();
            _promptWorkspaceSelectedSectionId = PromptSectionSchemaCatalog.NormalizeSectionId(sectionId);
            EnsurePromptWorkspaceBuffer();
        }

        private void EnsurePromptWorkspaceBuffer()
        {
            string targetId = _promptWorkspaceEditNodeMode ? _promptWorkspaceSelectedNodeId : _promptWorkspaceSelectedSectionId;
            if (string.Equals(_promptWorkspaceBufferedChannel, _workbenchPromptChannel, StringComparison.Ordinal) &&
                _promptWorkspaceBufferedNodeMode == _promptWorkspaceEditNodeMode &&
                string.Equals(_promptWorkspaceEditNodeMode ? _promptWorkspaceBufferedNodeId : _promptWorkspaceBufferedSectionId, targetId, StringComparison.Ordinal))
            {
                return;
            }

            _promptWorkspaceBufferedChannel = _workbenchPromptChannel ?? string.Empty;
            _promptWorkspaceBufferedNodeMode = _promptWorkspaceEditNodeMode;
            _promptWorkspaceBufferedSectionId = _promptWorkspaceSelectedSectionId ?? string.Empty;
            _promptWorkspaceBufferedNodeId = _promptWorkspaceSelectedNodeId ?? string.Empty;
            _promptWorkspaceEditorBuffer = _promptWorkspaceEditNodeMode
                ? GetPromptWorkspaceNodeText(_promptWorkspaceBufferedChannel, _promptWorkspaceBufferedNodeId)
                : GetPromptWorkspaceSectionText(_promptWorkspaceBufferedChannel, _promptWorkspaceBufferedSectionId);
        }

        private string GetPromptWorkspaceCurrentEditorText()
        {
            EnsurePromptWorkspaceBuffer();
            return _promptWorkspaceEditorBuffer ?? string.Empty;
        }

        private string GetPromptWorkspaceSectionText(string promptChannel, string sectionId)
        {
            RimTalkPromptEntryDefaultsConfig catalog = GetPromptSectionCatalogClone();
            return catalog.ResolveContent(promptChannel, sectionId) ?? string.Empty;
        }

        private string GetPromptWorkspaceNodeText(string promptChannel, string nodeId)
        {
            return ResolvePromptNodeText(promptChannel, nodeId);
        }

        private void SetPromptWorkspaceCurrentEditorText(string text)
        {
            _promptWorkspaceEditorBuffer = text ?? string.Empty;
            _promptWorkspaceBufferedChannel = _workbenchPromptChannel ?? string.Empty;
            _promptWorkspaceBufferedNodeMode = _promptWorkspaceEditNodeMode;
            _promptWorkspaceBufferedSectionId = _promptWorkspaceSelectedSectionId ?? string.Empty;
            _promptWorkspaceBufferedNodeId = _promptWorkspaceSelectedNodeId ?? string.Empty;
            MarkPromptWorkspaceDirty();
        }

        private void MarkPromptWorkspaceDirty()
        {
            _promptWorkspaceHasPendingPersist = true;
            _promptWorkspaceLastEditUtc = DateTime.UtcNow;
        }

        private void TryAutoSavePromptWorkspaceBuffer()
        {
            if (!_promptWorkspaceHasPendingPersist)
            {
                return;
            }

            if (_promptWorkspaceLastEditUtc == DateTime.MinValue)
            {
                PersistPromptWorkspaceBufferNow();
                return;
            }

            double elapsedMs = (DateTime.UtcNow - _promptWorkspaceLastEditUtc).TotalMilliseconds;
            if (elapsedMs >= PromptWorkspaceAutoSaveDebounceMs)
            {
                PersistPromptWorkspaceBufferNow();
            }
        }

        private void PersistPromptWorkspaceBufferNow()
        {
            if (!_promptWorkspaceHasPendingPersist)
            {
                return;
            }

            string bufferedChannel = _promptWorkspaceBufferedChannel ?? string.Empty;
            if (string.IsNullOrWhiteSpace(bufferedChannel))
            {
                _promptWorkspaceHasPendingPersist = false;
                _promptWorkspaceLastEditUtc = DateTime.MinValue;
                return;
            }

            string bufferedText = _promptWorkspaceEditorBuffer ?? string.Empty;
            if (_promptWorkspaceBufferedNodeMode)
            {
                if (!string.IsNullOrWhiteSpace(_promptWorkspaceBufferedNodeId))
                {
                    SetPromptNodeText(bufferedChannel, _promptWorkspaceBufferedNodeId, bufferedText);
                }
            }
            else if (!string.IsNullOrWhiteSpace(_promptWorkspaceBufferedSectionId))
            {
                RimTalkPromptEntryDefaultsConfig catalog = GetPromptSectionCatalogClone();
                catalog.SetContent(bufferedChannel, _promptWorkspaceBufferedSectionId, bufferedText);
                SetPromptSectionCatalog(catalog);
                _promptWorkspaceBufferedChannel = bufferedChannel;
                _promptWorkspaceBufferedNodeMode = false;
                _promptWorkspaceBufferedSectionId = _promptWorkspaceSelectedSectionId ?? string.Empty;
                _promptWorkspaceBufferedNodeId = _promptWorkspaceSelectedNodeId ?? string.Empty;
            }

            _promptWorkspaceHasPendingPersist = false;
            _promptWorkspaceLastEditUtc = DateTime.MinValue;
            InvalidatePromptWorkspacePreviewCache();
        }

        internal void FlushPromptWorkspaceEdits()
        {
            PersistPromptWorkspaceBufferNow();
        }

        private float ResolvePromptWorkspacePresetListHeight(float startY, float bottomY, float panelHeight)
        {
            float available = Mathf.Max(96f, bottomY - startY - 140f);
            float preferred = Mathf.Clamp(panelHeight * 0.28f, 96f, 220f);
            return Mathf.Clamp(preferred, 96f, available);
        }

        private string DrawPromptWorkspaceLegacyTextArea(Rect rect, string text)
        {
            string source = text ?? string.Empty;
            GUIStyle style = new GUIStyle(GUI.skin.textArea)
            {
                wordWrap = true,
                richText = false
            };
            float contentWidth = Mathf.Max(1f, rect.width - 16f);
            float contentHeight = Mathf.Max(rect.height, style.CalcHeight(new GUIContent(source), contentWidth) + 4f);
            Rect viewRect = new Rect(0f, 0f, contentWidth, contentHeight);
            _promptWorkspaceEditorScroll = new Vector2(
                0f,
                Mathf.Clamp(_promptWorkspaceEditorScroll.y, 0f, Mathf.Max(0f, viewRect.height - rect.height)));
            _promptWorkspaceEditorScroll = GUI.BeginScrollView(rect, _promptWorkspaceEditorScroll, viewRect, false, true);
            string edited = GUI.TextArea(new Rect(0f, 0f, contentWidth, contentHeight), source, style);
            GUI.EndScrollView();
            return edited;
        }

        private void DrawPromptWorkspaceStructuredPreview(Rect rect, PromptWorkspaceStructuredPreview preview)
        {
            _promptWorkspacePreviewRenderer ??= new PromptWorkspaceStructuredPreviewRenderer();
            _promptWorkspacePreviewRenderer.Draw(rect, preview, ref _promptWorkspacePreviewScroll);
        }

        private void RestorePromptWorkspaceCurrentEntry()
        {
            if (_promptWorkspaceEditNodeMode)
            {
                string fallbackNode = PromptUnifiedCatalog.CreateFallback().ResolveNode(_workbenchPromptChannel, _promptWorkspaceSelectedNodeId);
                SetPromptWorkspaceCurrentEditorText(fallbackNode);
                PersistPromptWorkspaceBufferNow();
                return;
            }

            string fallback = RimTalkPromptEntryDefaultsProvider.ResolveContent(_workbenchPromptChannel, _promptWorkspaceSelectedSectionId);
            SetPromptWorkspaceCurrentEditorText(fallback);
            PersistPromptWorkspaceBufferNow();
        }

        private void RestorePromptWorkspaceCurrentChannel()
        {
            RimTalkPromptEntryDefaultsConfig catalog = GetPromptSectionCatalogClone();
            foreach (PromptSectionSchemaItem section in PromptSectionSchemaCatalog.GetMainChainSections())
            {
                catalog.SetContent(
                    _workbenchPromptChannel,
                    section.Id,
                    RimTalkPromptEntryDefaultsProvider.ResolveContent(_workbenchPromptChannel, section.Id));
            }

            SetPromptSectionCatalog(catalog);
            if (_promptWorkspaceEditNodeMode)
            {
                PromptUnifiedCatalog fallback = PromptUnifiedCatalog.CreateFallback();
                var resetLayouts = new List<PromptUnifiedNodeLayoutConfig>();
                foreach (PromptUnifiedNodeSchemaItem node in PromptUnifiedNodeSchemaCatalog.GetAll())
                {
                    SetPromptNodeText(_workbenchPromptChannel, node.Id, fallback.ResolveNode(_workbenchPromptChannel, node.Id));
                    resetLayouts.Add(PromptUnifiedNodeLayoutDefaults.BuildDefaultLayout(_workbenchPromptChannel, node.Id));
                }

                SavePromptNodeLayouts(_workbenchPromptChannel, resetLayouts);
            }

            EnsurePromptWorkspaceBuffer();
            InvalidatePromptWorkspacePreviewCache();
        }

        private PromptWorkspaceStructuredPreview GetPromptWorkspaceStructuredPreview()
        {
            if (_promptWorkspacePreviewCacheValid &&
                _promptWorkspacePreviewCachedRoot == _workbenchChannel &&
                string.Equals(_promptWorkspacePreviewCachedChannel, _workbenchPromptChannel, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(_promptWorkspacePreviewCachedSignature))
            {
                return _promptWorkspacePreviewCachedData ?? new PromptWorkspaceStructuredPreview();
            }

            PromptWorkspaceStructuredPreview preview;
            if (_promptWorkspaceEditNodeMode)
            {
                preview = PromptPersistenceService.Instance.BuildPromptWorkspaceStructuredLayoutPreview(
                    GetPromptWorkspaceRootChannel(),
                    _workbenchPromptChannel,
                    out List<ResolvedPromptNodePlacement> _);
            }
            else
            {
                preview = PromptPersistenceService.Instance.BuildPromptWorkspaceStructuredSectionPreview(
                    GetPromptWorkspaceRootChannel(),
                    _workbenchPromptChannel);
            }

            _promptWorkspacePreviewCachedRoot = _workbenchChannel;
            _promptWorkspacePreviewCachedChannel = _workbenchPromptChannel ?? string.Empty;
            _promptWorkspacePreviewCachedData = preview;
            _promptWorkspacePreviewCachedSignature = preview?.Signature ?? string.Empty;
            _promptWorkspacePreviewCacheValid = true;
            return preview ?? new PromptWorkspaceStructuredPreview();
        }

        private void InvalidatePromptWorkspacePreviewCache()
        {
            _promptWorkspacePreviewCacheValid = false;
            _promptWorkspacePreviewCachedChannel = string.Empty;
            _promptWorkspacePreviewCachedSignature = string.Empty;
            _promptWorkspacePreviewCachedData = null;
        }

        private bool TryInsertVariableTokenToPromptWorkspace(string token)
        {
            if (!CanInsertVariableTokenToPromptWorkspace())
            {
                return false;
            }

            string normalized = NormalizeVariableNameToken(token);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            string current = GetPromptWorkspaceCurrentEditorText();
            if (ContainsVariableToken(current, normalized))
            {
                Messages.Message("RimChat_RimTalkVariableAlreadyInTemplate".Translate(), MessageTypeDefOf.NeutralEvent, false);
                return true;
            }

            string wrapped = "{{ " + normalized + " }}";
            string updated = string.IsNullOrWhiteSpace(current)
                ? wrapped
                : current.TrimEnd() + "\n" + wrapped;
            SetPromptWorkspaceCurrentEditorText(updated);
            Messages.Message("RimChat_RimTalkVariableInserted".Translate(wrapped), MessageTypeDefOf.NeutralEvent, false);
            return true;
        }

        private bool CanInsertVariableTokenToPromptWorkspace()
        {
            EnsurePromptWorkspaceSelection();
            if (string.IsNullOrWhiteSpace(_workbenchPromptChannel))
            {
                return false;
            }

            if (_promptWorkspaceEditNodeMode)
            {
                return PromptUnifiedNodeSchemaCatalog.TryGet(_promptWorkspaceSelectedNodeId, out PromptUnifiedNodeSchemaItem _);
            }

            return PromptSectionSchemaCatalog.TryGetSection(_promptWorkspaceSelectedSectionId, out PromptSectionSchemaItem _);
        }
    }
}
