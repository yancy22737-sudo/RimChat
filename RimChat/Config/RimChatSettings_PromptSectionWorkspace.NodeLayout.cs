using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: prompt section/node schema catalogs and unified node layout persistence.
    /// Responsibility: render workspace section/node lists and apply in-memory unified resets until explicit save.
    /// </summary>
    public partial class RimChatSettings : ModSettings
    {
        private void DrawPromptWorkspaceSectionList(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, ModuleListBg);
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
                    Widgets.DrawBoxSolid(rowRect, RowSelectedBg);
                }
                else if (Mouse.IsOver(rowRect))
                {
                    Widgets.DrawBoxSolid(rowRect, RowHoverBg);
                }

                if (Widgets.ButtonInvisible(rowRect))
                {
                    SchedulePromptWorkspaceNavigation(() => SelectPromptWorkspaceSection(section.Id));
                }

                Widgets.Label(new Rect(rowRect.x + 8f, rowRect.y + 4f, rowRect.width - 16f, rowRect.height - 8f), section.GetDisplayLabel());
            }

            Widgets.EndScrollView();
        }

        private void DrawPromptWorkspaceNodeLayoutList(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, ModuleListBg);
            Rect inner = rect.ContractedBy(6f);
            List<PromptUnifiedNodeLayoutConfig> layouts = GetPromptWorkspaceNodeLayouts();
            float rowHeight = 28f;
            float totalRows = layouts.Count;
            Rect viewRect = new Rect(0f, 0f, inner.width - 16f, Mathf.Max(inner.height, totalRows * rowHeight));
            Widgets.BeginScrollView(inner, ref _promptWorkspaceNodeScroll, viewRect);

            float y = 0f;

            List<PromptUnifiedNodeLayoutConfig> orderedItems = layouts
                .OrderBy(item => item.Order)
                .ThenBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            for (int i = 0; i < orderedItems.Count; i++)
            {
                PromptUnifiedNodeLayoutConfig item = orderedItems[i];
                Rect rowRect = new Rect(0f, y, viewRect.width, rowHeight - 2f);
                bool selected = string.Equals(_promptWorkspaceSelectedNodeId, item.NodeId, StringComparison.OrdinalIgnoreCase);
                if (selected)
                {
                    Widgets.DrawBoxSolid(rowRect, RowSelectedBg);
                }
                else if (Mouse.IsOver(rowRect))
                {
                    Widgets.DrawBoxSolid(rowRect, RowHoverBg);
                }

                Rect toggleRect = new Rect(rowRect.x + 4f, rowRect.y + 4f, 18f, rowRect.height - 8f);
                bool enabled = item.Enabled;
                Widgets.Checkbox(toggleRect.position, ref enabled, rowRect.height - 8f, false);
                if (enabled != item.Enabled)
                {
                    if (!EnsurePromptWorkspaceEditablePresetForMutation("workspace.node_toggle"))
                    {
                        enabled = item.Enabled;
                        continue;
                    }

                    item.Enabled = enabled;
                    SavePromptWorkspaceNodeLayouts(layouts);
                }

                Rect labelRect = new Rect(toggleRect.xMax + 4f, rowRect.y + 4f, rowRect.width - 106f, rowRect.height - 8f);
                Widgets.Label(labelRect, PromptUnifiedNodeSchemaCatalog.GetDisplayLabel(item.NodeId));
                if (Widgets.ButtonInvisible(labelRect))
                {
                    SchedulePromptWorkspaceNavigation(() =>
                    {
                        if (!PersistPromptWorkspaceBufferNow(force: true))
                        {
                            return;
                        }

                        _promptWorkspaceSelectedNodeId = item.NodeId;
                        EnsurePromptWorkspaceBuffer();
                        InvalidatePromptWorkspacePreviewCache();
                    });
                }

                DrawNodeLayoutRowButtons(layouts, item, rowRect);
                HandleNodeLayoutDrag(layouts, item, rowRect);
                y += rowHeight;
            }

            Widgets.EndScrollView();
        }

        private void DrawNodeLayoutRowButtons(
            List<PromptUnifiedNodeLayoutConfig> layouts,
            PromptUnifiedNodeLayoutConfig item,
            Rect rowRect)
        {
            Rect upRect = new Rect(rowRect.xMax - 50f, rowRect.y + 3f, 20f, rowRect.height - 6f);
            Rect downRect = new Rect(rowRect.xMax - 28f, rowRect.y + 3f, 20f, rowRect.height - 6f);
            if (Widgets.ButtonText(upRect, "▲"))
            {
                MovePromptNodeLayout(layouts, item.NodeId, -1);
            }

            if (Widgets.ButtonText(downRect, "▼"))
            {
                MovePromptNodeLayout(layouts, item.NodeId, 1);
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
            string channel = string.IsNullOrWhiteSpace(_workbenchPromptChannel)
                ? EnsurePromptWorkspaceSelection()
                : _workbenchPromptChannel;
            if (string.IsNullOrWhiteSpace(channel))
            {
                return new List<PromptUnifiedNodeLayoutConfig>();
            }

            if (string.Equals(_promptWorkspaceNodeLayoutCacheChannel, channel, StringComparison.Ordinal) &&
                _promptWorkspaceNodeLayoutCache != null &&
                _promptWorkspaceNodeLayoutCache.Count > 0)
            {
                return _promptWorkspaceNodeLayoutCache;
            }

            _promptWorkspaceNodeLayoutCacheChannel = channel;
            _promptWorkspaceNodeLayoutCache = GetPromptNodeLayouts(channel)
                .Select(item => item.Clone())
                .OrderBy(item => item.Order)
                .ThenBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return _promptWorkspaceNodeLayoutCache;
        }

        private List<PromptUnifiedNodeSchemaItem> GetPromptWorkspaceEditableNodes()
        {
            string channel = string.IsNullOrWhiteSpace(_workbenchPromptChannel)
                ? EnsurePromptWorkspaceSelection()
                : _workbenchPromptChannel;
            if (string.IsNullOrWhiteSpace(channel))
            {
                return new List<PromptUnifiedNodeSchemaItem>();
            }

            if (string.Equals(_promptWorkspaceNodeListCacheChannel, channel, StringComparison.Ordinal) &&
                _promptWorkspaceNodeListCache != null &&
                _promptWorkspaceNodeListCache.Count > 0)
            {
                return _promptWorkspaceNodeListCache;
            }

            List<PromptUnifiedNodeSchemaItem> allowedNodes = PromptUnifiedNodeSchemaCatalog.GetAllowedNodes(channel).ToList();
            List<PromptUnifiedNodeLayoutConfig> layouts = GetPromptWorkspaceNodeLayouts();
            if (layouts.Count > 0)
            {
                var byId = allowedNodes.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
                var ordered = new List<PromptUnifiedNodeSchemaItem>();
                foreach (PromptUnifiedNodeLayoutConfig layout in layouts)
                {
                    if (layout == null || string.IsNullOrWhiteSpace(layout.NodeId))
                    {
                        continue;
                    }

                    if (byId.TryGetValue(layout.NodeId, out PromptUnifiedNodeSchemaItem matched))
                    {
                        ordered.Add(matched);
                    }
                }

                if (ordered.Count > 0)
                {
                    allowedNodes = ordered;
                }
            }

            _promptWorkspaceNodeListCacheChannel = channel;
            _promptWorkspaceNodeListCache = allowedNodes;
            return _promptWorkspaceNodeListCache;
        }

        private void SavePromptWorkspaceNodeLayouts(List<PromptUnifiedNodeLayoutConfig> layouts)
        {
            SavePromptNodeLayouts(_workbenchPromptChannel, layouts, persistToFiles: false);
            InvalidatePromptWorkspaceNodeUiCaches();
            InvalidatePromptWorkspacePreviewCache();
            EnsurePromptWorkspaceBuffer();
        }

        private void MovePromptNodeLayout(List<PromptUnifiedNodeLayoutConfig> layouts, string nodeId, int direction)
        {
            if (!EnsurePromptWorkspaceEditablePresetForMutation("workspace.node_order"))
            {
                return;
            }

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
            if (!EnsurePromptWorkspaceEditablePresetForMutation("workspace.node_drag"))
            {
                return;
            }

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
        }

        private void ShowPromptNodeSlotMenu(List<PromptUnifiedNodeLayoutConfig> layouts, PromptUnifiedNodeLayoutConfig node)
        {
            List<FloatMenuOption> options = Enum.GetValues(typeof(PromptUnifiedNodeSlot))
                .Cast<PromptUnifiedNodeSlot>()
                .Select(slot => new FloatMenuOption(GetPromptNodeSlotLabel(slot), () =>
                {
                    if (!EnsurePromptWorkspaceEditablePresetForMutation("workspace.node_slot"))
                    {
                        return;
                    }

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

        private void RestorePromptWorkspaceCurrentEntry()
        {
            TryResetPromptWorkspaceCurrentEntry();
        }

        private void RestorePromptWorkspaceCurrentChannel()
        {
            if (!EnsurePromptWorkspaceEditablePresetForMutation("workspace.reset_channel"))
            {
                return;
            }

            foreach (PromptSectionSchemaItem section in PromptSectionSchemaCatalog.GetMainChainSections())
            {
                SetPromptSectionText(
                    _workbenchPromptChannel,
                    section.Id,
                    RimTalkPromptEntryDefaultsProvider.ResolveContent(_workbenchPromptChannel, section.Id),
                    persistToFiles: false);
            }
            if (_promptWorkspaceEditNodeMode)
            {
                PromptUnifiedCatalog fallback = PromptUnifiedCatalog.CreateFallback();
                var resetLayouts = new List<PromptUnifiedNodeLayoutConfig>();
                foreach (PromptUnifiedNodeSchemaItem node in PromptUnifiedNodeSchemaCatalog.GetAllowedNodes(_workbenchPromptChannel))
                {
                    SetPromptNodeText(_workbenchPromptChannel, node.Id, fallback.ResolveNode(_workbenchPromptChannel, node.Id), persistToFiles: false);
                    resetLayouts.Add(PromptUnifiedNodeLayoutDefaults.BuildDefaultLayout(_workbenchPromptChannel, node.Id));
                }

                SavePromptNodeLayouts(_workbenchPromptChannel, resetLayouts, persistToFiles: false);
            }

            EnsurePromptWorkspaceBuffer();
            InvalidatePromptWorkspaceNodeUiCaches();
            InvalidatePromptWorkspacePreviewCache();
        }

        private List<PromptWorkbenchModuleItem> GetCachedPromptWorkspaceModules()
        {
            string channel = string.IsNullOrWhiteSpace(_workbenchPromptChannel)
                ? EnsurePromptWorkspaceSelection()
                : _workbenchPromptChannel;

            if (string.Equals(_promptWorkspaceModuleCacheChannel, channel, StringComparison.Ordinal) &&
                _promptWorkspaceModuleCache != null &&
                _promptWorkspaceModuleCache.Count >= 0)
            {
                return _promptWorkspaceModuleCache;
            }

            List<PromptSectionLayoutConfig> sectionLayouts = GetPromptWorkspaceSectionLayouts();
            List<PromptUnifiedNodeLayoutConfig> nodeLayouts = GetPromptWorkspaceNodeLayouts();
            _promptWorkspaceModuleCacheChannel = channel;
            _promptWorkspaceModuleCache = PromptWorkbenchModuleProjection.BuildModules(channel, sectionLayouts, nodeLayouts);
            return _promptWorkspaceModuleCache;
        }

        private void DrawPromptWorkspaceModuleList(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, ModuleListBg);
            Rect inner = rect.ContractedBy(6f);

            List<PromptWorkbenchModuleItem> modules = GetCachedPromptWorkspaceModules();
            List<PromptSectionLayoutConfig> sectionLayouts = GetPromptWorkspaceSectionLayouts();
            List<PromptUnifiedNodeLayoutConfig> nodeLayouts = GetPromptWorkspaceNodeLayouts();

            const float rowHeight = 25f;
            const float rowStep = 26f;
            const float buttonSize = 22f;
            float totalHeight = modules.Count * rowStep;

            Rect viewRect = new Rect(0f, 0f, inner.width - 16f, Mathf.Max(inner.height, totalHeight));
            Widgets.BeginScrollView(inner, ref _promptWorkspaceSectionScroll, viewRect);

            float rowY = 0f;
            for (int i = 0; i < modules.Count; i++)
            {
                PromptWorkbenchModuleItem module = modules[i];
                Rect rowRect = new Rect(0f, rowY, viewRect.width, rowHeight);

                bool selected = module.Kind == ModuleKind.Section
                    ? (!_promptWorkspaceEditNodeMode && string.Equals(_promptWorkspaceSelectedSectionId, module.Id, StringComparison.OrdinalIgnoreCase))
                    : (_promptWorkspaceEditNodeMode && string.Equals(_promptWorkspaceSelectedNodeId, module.Id, StringComparison.OrdinalIgnoreCase));

                // RimTalk-style selection highlight
                if (selected)
                {
                    Widgets.DrawHighlight(rowRect);
                }
                else if (Mouse.IsOver(rowRect))
                {
                    Widgets.DrawBoxSolid(rowRect, RowHoverBg);
                }

                // Checkbox (both Section and Node)
                bool enabled = module.Enabled;
                Widgets.Checkbox(new Vector2(4f, rowY + 4f), ref enabled, 16f);
                if (enabled != module.Enabled)
                {
                    if (!EnsurePromptWorkspaceEditablePresetForMutation("workspace.module_toggle"))
                    {
                        enabled = module.Enabled;
                    }
                    else if (module.Kind == ModuleKind.Section)
                    {
                        PromptSectionLayoutConfig layoutItem = sectionLayouts.FirstOrDefault(item =>
                            string.Equals(item.SectionId, module.Id, StringComparison.OrdinalIgnoreCase));
                        if (layoutItem != null)
                        {
                            layoutItem.Enabled = enabled;
                            SavePromptWorkspaceSectionLayouts(sectionLayouts);
                        }
                    }
                    else
                    {
                        PromptUnifiedNodeLayoutConfig layoutItem = nodeLayouts.FirstOrDefault(item =>
                            string.Equals(item.NodeId, module.Id, StringComparison.OrdinalIgnoreCase));
                        if (layoutItem != null)
                        {
                            layoutItem.Enabled = enabled;
                            SavePromptWorkspaceNodeLayouts(nodeLayouts);
                        }
                    }
                }

                // Label (clickable)
                float labelX = 24f;
                float labelWidth = viewRect.width - labelX - buttonSize - 6f;
                Rect labelRect = new Rect(labelX, rowY + 1f, labelWidth, rowHeight - 2f);
                string kindTag = module.Kind == ModuleKind.Section
                    ? "RimChat_PromptWorkspaceKind_Section".Translate().ToString()
                    : "RimChat_PromptWorkspaceKind_Node".Translate().ToString();
                string displayText = $"{module.Label} [{kindTag}]";
                bool oldWordWrap = Text.WordWrap;
                Text.WordWrap = false;
                Widgets.Label(labelRect, displayText.Truncate(labelRect.width));
                Text.WordWrap = oldWordWrap;

                if (Widgets.ButtonInvisible(labelRect))
                {
                    if (module.Kind == ModuleKind.Section)
                    {
                        SchedulePromptWorkspaceNavigation(() => SelectPromptWorkspaceSection(module.Id));
                    }
                    else
                    {
                        SchedulePromptWorkspaceNavigation(() =>
                        {
                            if (!PersistPromptWorkspaceBufferNow(force: true))
                            {
                                return;
                            }

                            _promptWorkspaceEditNodeMode = true;
                            _promptWorkspaceSelectedNodeId = module.Id;
                            EnsurePromptWorkspaceBuffer();
                            InvalidatePromptWorkspacePreviewCache();
                        });
                    }
                }

                // Move up/down buttons (RimTalk style, bottom of list)
                rowY += rowStep;
            }

            Widgets.EndScrollView();

            // Bottom bar with ▲▼ sort buttons (RimTalk style)
            if (modules.Count > 0)
            {
                PromptWorkbenchModuleItem selectedModule = default;
                int selectedIndex = -1;
                for (int i = 0; i < modules.Count; i++)
                {
                    PromptWorkbenchModuleItem m = modules[i];
                    bool isSel = m.Kind == ModuleKind.Section
                        ? (!_promptWorkspaceEditNodeMode && string.Equals(_promptWorkspaceSelectedSectionId, m.Id, StringComparison.OrdinalIgnoreCase))
                        : (_promptWorkspaceEditNodeMode && string.Equals(_promptWorkspaceSelectedNodeId, m.Id, StringComparison.OrdinalIgnoreCase));
                    if (isSel)
                    {
                        selectedModule = m;
                        selectedIndex = i;
                        break;
                    }
                }

                float buttonWidth = (inner.width - 4f) * 0.5f;
                Rect upRect = new Rect(inner.x, inner.yMax - 24f, buttonWidth, 24f);
                Rect downRect = new Rect(upRect.xMax + 4f, inner.yMax - 24f, buttonWidth, 24f);

                if (selectedIndex > 0)
                {
                    if (Widgets.ButtonText(upRect, "▲"))
                    {
                        MovePromptWorkspaceModule(modules, selectedIndex, -1, sectionLayouts, nodeLayouts);
                    }
                }
                else
                {
                    GUI.enabled = false;
                    Widgets.ButtonText(upRect, "▲");
                    GUI.enabled = true;
                }

                if (selectedIndex >= 0 && selectedIndex < modules.Count - 1)
                {
                    if (Widgets.ButtonText(downRect, "▼"))
                    {
                        MovePromptWorkspaceModule(modules, selectedIndex, 1, sectionLayouts, nodeLayouts);
                    }
                }
                else
                {
                    GUI.enabled = false;
                    Widgets.ButtonText(downRect, "▼");
                    GUI.enabled = true;
                }
            }
        }

        private void MovePromptWorkspaceModule(
            List<PromptWorkbenchModuleItem> modules,
            int selectedIndex,
            int direction,
            List<PromptSectionLayoutConfig> sectionLayouts,
            List<PromptUnifiedNodeLayoutConfig> nodeLayouts)
        {
            if (!EnsurePromptWorkspaceEditablePresetForMutation("workspace.module_order"))
            {
                return;
            }

            PromptWorkbenchModuleItem current = modules[selectedIndex];
            PromptWorkbenchModuleItem target = modules[selectedIndex + direction];

            // Only allow swapping within the same Kind or same Slot
            if (current.Kind == ModuleKind.Section && target.Kind == ModuleKind.Section)
            {
                MovePromptSectionLayout(sectionLayouts, current.Id, direction);
            }
            else if (current.Kind == ModuleKind.Node && target.Kind == ModuleKind.Node)
            {
                // Same slot swap
                if (current.Slot == target.Slot)
                {
                    MovePromptNodeLayout(nodeLayouts, current.Id, direction);
                }
                else
                {
                    // Cross-slot: move to target slot
                    PromptUnifiedNodeLayoutConfig layout = nodeLayouts.FirstOrDefault(item =>
                        string.Equals(item.NodeId, current.Id, StringComparison.OrdinalIgnoreCase));
                    if (layout != null)
                    {
                        layout.Slot = target.Slot.ToSerializedValue();
                        layout.Order = target.DisplayOrder;
                        SavePromptWorkspaceNodeLayouts(nodeLayouts);
                    }
                }
            }
            // Cross-kind swaps are not allowed (Section and Node belong to different conceptual groups)
        }

        private List<PromptSectionLayoutConfig> GetPromptWorkspaceSectionLayouts()
        {
            string channel = string.IsNullOrWhiteSpace(_workbenchPromptChannel)
                ? EnsurePromptWorkspaceSelection()
                : _workbenchPromptChannel;
            if (string.IsNullOrWhiteSpace(channel))
            {
                return new List<PromptSectionLayoutConfig>();
            }

            if (string.Equals(_promptWorkspaceSectionLayoutCacheChannel, channel, StringComparison.Ordinal) &&
                _promptWorkspaceSectionLayoutCache != null &&
                _promptWorkspaceSectionLayoutCache.Count > 0)
            {
                return _promptWorkspaceSectionLayoutCache;
            }

            _promptWorkspaceSectionLayoutCacheChannel = channel;
            _promptWorkspaceSectionLayoutCache = GetPromptSectionLayouts(channel)
                .Select(item => item.Clone())
                .ToList();
            return _promptWorkspaceSectionLayoutCache;
        }

        private void SavePromptWorkspaceSectionLayouts(List<PromptSectionLayoutConfig> layouts)
        {
            SavePromptSectionLayouts(_workbenchPromptChannel, layouts, persistToFiles: false);
            InvalidatePromptWorkspaceNodeUiCaches();
            InvalidatePromptWorkspacePreviewCache();
            EnsurePromptWorkspaceBuffer();
        }

        private void DrawSectionLayoutRowButtons(
            List<PromptSectionLayoutConfig> layouts,
            string sectionId,
            Rect rowRect)
        {
            Rect upRect = new Rect(rowRect.xMax - 50f, rowRect.y + 3f, 20f, rowRect.height - 6f);
            Rect downRect = new Rect(rowRect.xMax - 28f, rowRect.y + 3f, 20f, rowRect.height - 6f);
            if (Widgets.ButtonText(upRect, "▲"))
            {
                MovePromptSectionLayout(layouts, sectionId, -1);
            }

            if (Widgets.ButtonText(downRect, "▼"))
            {
                MovePromptSectionLayout(layouts, sectionId, 1);
            }
        }

        private void MovePromptSectionLayout(List<PromptSectionLayoutConfig> layouts, string sectionId, int direction)
        {
            if (!EnsurePromptWorkspaceEditablePresetForMutation("workspace.section_order"))
            {
                return;
            }

            PromptSectionLayoutConfig current = layouts.FirstOrDefault(item =>
                string.Equals(item.SectionId, sectionId, StringComparison.OrdinalIgnoreCase));
            if (current == null)
            {
                return;
            }

            List<PromptSectionLayoutConfig> ordered = layouts
                .OrderBy(item => item.Order)
                .ThenBy(item => item.SectionId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            int index = ordered.FindIndex(item => string.Equals(item.SectionId, sectionId, StringComparison.OrdinalIgnoreCase));
            int target = index + direction;
            if (index < 0 || target < 0 || target >= ordered.Count)
            {
                return;
            }

            PromptSectionLayoutConfig source = ordered[index];
            PromptSectionLayoutConfig destination = ordered[target];
            int tempOrder = source.Order;
            source.Order = destination.Order;
            destination.Order = tempOrder;
            SavePromptWorkspaceSectionLayouts(layouts);
        }
    }
}
