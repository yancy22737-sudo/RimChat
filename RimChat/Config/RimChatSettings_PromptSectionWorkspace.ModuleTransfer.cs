using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Persistence;
using RimChat.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: PromptModuleTransferModels, Dialog_PromptModuleCreate, Dialog_PromptModuleMultiExport,
    /// Dialog_PromptModuleImportPreview, PromptDomainJsonUtility, PromptUnifiedNodeSchemaCatalog.
    /// Responsibility: module-level export/import/create operations for the prompt workbench module list.
    /// </summary>
    public partial class RimChatSettings : ModSettings
    {
        private void DrawPromptWorkspaceModuleHeaderActions(Rect rect)
        {
            float btnW = 60f;
            float gap = 4f;
            float plusW = 26f;

            Rect exportRect = new Rect(rect.xMax - btnW, rect.y, btnW, rect.height);
            Rect importRect = new Rect(exportRect.xMin - gap - btnW, rect.y, btnW, rect.height);
            Rect newRect = new Rect(importRect.xMin - gap - plusW, rect.y, plusW, rect.height);

            if (Widgets.ButtonText(newRect, "+"))
            {
                Find.WindowStack.Add(new Dialog_PromptModuleCreate(HandleModuleCreate));
            }

            if (Widgets.ButtonText(importRect, "RimChat_ModuleImportBtn".Translate()))
            {
                ShowModuleImportDialog();
            }

            if (Widgets.ButtonText(exportRect, "RimChat_ModuleExportBtn".Translate()))
            {
                ShowModuleExportDialog();
            }
        }

        private void HandleModuleCreate(string nodeId, string displayName, PromptUnifiedNodeSlot slot)
        {
            if (!EnsurePromptWorkspaceEditablePresetForMutation("workspace.module_create"))
            {
                return;
            }

            PromptUnifiedNodeSchemaCatalog.RegisterCustomNode(nodeId, displayName);
            AddCustomNodeRegistrationToCatalog(nodeId, displayName);

            List<PromptUnifiedNodeLayoutConfig> layouts = GetPromptWorkspaceNodeLayouts();
            int maxOrder = layouts
                .Where(l => l != null && l.GetSlot() == slot)
                .Select(l => l.Order)
                .DefaultIfEmpty(0)
                .Max();
            layouts.Add(PromptUnifiedNodeLayoutConfig.Create(nodeId, slot, maxOrder + 1, true));
            SavePromptWorkspaceNodeLayouts(layouts);

            SetPromptNodeText(_workbenchPromptChannel, nodeId, string.Empty, persistToFiles: false);
            ApplyUnifiedCatalogPersistence(persistToFiles: true);

            SchedulePromptWorkspaceNavigation(() =>
            {
                if (!PersistPromptWorkspaceBufferNow(force: true)) return;
                _promptWorkspaceEditNodeMode = true;
                _promptWorkspaceSelectedNodeId = nodeId;
                EnsurePromptWorkspaceBuffer();
                InvalidatePromptWorkspacePreviewCache();
            });

            Messages.Message("RimChat_ModuleCreateSuccess".Translate(displayName), MessageTypeDefOf.PositiveEvent, false);
        }

        private void ShowModuleExportDialog()
        {
            PersistPromptWorkspaceBufferNow(force: true);
            List<PromptWorkbenchModuleItem> modules = GetCachedPromptWorkspaceModules();
            if (modules.Count == 0)
            {
                Messages.Message("RimChat_ModuleExportNoSelection".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Find.WindowStack.Add(new Dialog_PromptModuleMultiExport(modules, HandleMultiExport));
        }

        private void HandleMultiExport(string filePath, List<PromptWorkbenchModuleItem> selectedModules)
        {
            string channel = _workbenchPromptChannel;
            List<PromptUnifiedNodeLayoutConfig> layouts = GetPromptWorkspaceNodeLayouts();
            var bundle = new PromptModuleExportBundle();

            foreach (PromptWorkbenchModuleItem module in selectedModules)
            {
                string content = module.Kind == ModuleKind.Node
                    ? UnifiedPromptCatalog.ResolveNode(channel, module.Id)
                    : string.Empty;

                PromptUnifiedNodeLayoutConfig layout = layouts.FirstOrDefault(l =>
                    l != null && string.Equals(l.NodeId, module.Id, StringComparison.OrdinalIgnoreCase));

                bundle.Modules.Add(new PromptModuleExportPayload
                {
                    FormatVersion = 1,
                    NodeId = module.Id,
                    DisplayName = module.Label,
                    Slot = layout?.Slot ?? PromptUnifiedNodeSlot.MainChainAfter.ToSerializedValue(),
                    Order = layout?.Order ?? module.DisplayOrder,
                    Enabled = module.Enabled,
                    Content = content
                });
            }

            PromptDomainJsonUtility.WriteToFile(filePath, bundle, prettyPrint: true);
            Messages.Message("RimChat_ModuleExportSuccess".Translate(filePath), MessageTypeDefOf.PositiveEvent, false);
        }

        private void ShowModuleImportDialog()
        {
            string defaultDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            Find.WindowStack.Add(new Dialog_LoadFile(defaultDir, path =>
            {
                // Try multi-module bundle first
                PromptModuleExportBundle bundle = PromptDomainJsonUtility.LoadSingle<PromptModuleExportBundle>(path);
                if (bundle?.Modules != null && bundle.Modules.Count > 0 &&
                    bundle.Modules.Any(m => m != null && !string.IsNullOrWhiteSpace(m.NodeId)))
                {
                    Find.WindowStack.Add(new Dialog_PromptModuleImportPreview(bundle.Modules, HandleMultiImport));
                    return;
                }

                // Fallback: try single-module payload (backward compatibility)
                PromptModuleExportPayload single = PromptDomainJsonUtility.LoadSingle<PromptModuleExportPayload>(path);
                if (single != null && !string.IsNullOrWhiteSpace(single.NodeId))
                {
                    Find.WindowStack.Add(new Dialog_PromptModuleImportPreview(
                        new List<PromptModuleExportPayload> { single }, HandleMultiImport));
                    return;
                }

                Messages.Message("RimChat_ModuleImportInvalidFile".Translate(), MessageTypeDefOf.RejectInput, false);
            }));
        }

        private void HandleMultiImport(List<PromptModuleExportPayload> modules)
        {
            if (!EnsurePromptWorkspaceEditablePresetForMutation("workspace.module_import"))
            {
                return;
            }

            int count = 0;
            foreach (PromptModuleExportPayload payload in modules)
            {
                if (payload == null || string.IsNullOrWhiteSpace(payload.NodeId))
                {
                    continue;
                }

                ImportSingleModule(payload);
                count++;
            }

            ApplyUnifiedCatalogPersistence(persistToFiles: true);

            InvalidatePromptWorkspaceNodeUiCaches();
            InvalidatePromptWorkspacePreviewCache();
            EnsurePromptWorkspaceBuffer();

            Messages.Message("RimChat_ModuleImportMultiSuccess".Translate(count), MessageTypeDefOf.PositiveEvent, false);
        }

        private void ImportSingleModule(PromptModuleExportPayload payload)
        {
            string nodeId = payload.NodeId;
            PromptUnifiedNodeSchemaCatalog.RegisterCustomNode(nodeId, payload.DisplayName);
            AddCustomNodeRegistrationToCatalog(nodeId, payload.DisplayName);

            PromptUnifiedNodeSlot slot = PromptUnifiedNodeSlotExtensions.ToPromptUnifiedNodeSlot(payload.Slot);
            List<PromptUnifiedNodeLayoutConfig> layouts = GetPromptWorkspaceNodeLayouts();
            PromptUnifiedNodeLayoutConfig existing = layouts.FirstOrDefault(l =>
                l != null && string.Equals(l.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Slot = slot.ToSerializedValue();
                existing.Order = payload.Order;
                existing.Enabled = payload.Enabled;
            }
            else
            {
                layouts.Add(PromptUnifiedNodeLayoutConfig.Create(nodeId, slot, payload.Order, payload.Enabled));
            }

            SavePromptWorkspaceNodeLayouts(layouts);
            SetPromptNodeText(_workbenchPromptChannel, nodeId, payload.Content ?? string.Empty, persistToFiles: false);
        }

        private void AddCustomNodeRegistrationToCatalog(string nodeId, string displayName)
        {
            PromptUnifiedChannelConfig channelConfig = UnifiedPromptCatalog
                .Channels?.FirstOrDefault(c =>
                    c != null && string.Equals(c.PromptChannel, _workbenchPromptChannel, StringComparison.OrdinalIgnoreCase));
            if (channelConfig == null)
            {
                return;
            }

            channelConfig.CustomNodes ??= new List<PromptUnifiedNodeRegistration>();
            PromptUnifiedNodeRegistration existing = channelConfig.CustomNodes.FirstOrDefault(r =>
                r != null && string.Equals(r.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.DisplayName = displayName;
            }
            else
            {
                channelConfig.CustomNodes.Add(new PromptUnifiedNodeRegistration
                {
                    NodeId = nodeId,
                    DisplayName = displayName
                });
            }
        }

        private void DeleteCustomModule(string nodeId)
        {
            if (!EnsurePromptWorkspaceEditablePresetForMutation("workspace.module_delete"))
            {
                return;
            }

            string channel = _workbenchPromptChannel;
            string displayName = PromptUnifiedNodeSchemaCatalog.GetDisplayLabel(nodeId);

            // Remove node layout
            List<PromptUnifiedNodeLayoutConfig> layouts = GetPromptWorkspaceNodeLayouts();
            layouts.RemoveAll(l => l != null && string.Equals(l.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));
            SavePromptWorkspaceNodeLayouts(layouts);

            // Remove node content
            UnifiedPromptCatalog.SetNode(channel, nodeId, string.Empty);

            // Remove custom node registration from catalog
            PromptUnifiedChannelConfig channelConfig = UnifiedPromptCatalog
                .Channels?.FirstOrDefault(c =>
                    c != null && string.Equals(c.PromptChannel, channel, StringComparison.OrdinalIgnoreCase));
            if (channelConfig?.CustomNodes != null)
            {
                channelConfig.CustomNodes.RemoveAll(r =>
                    r != null && string.Equals(r.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));
            }

            // Unregister from schema catalog
            PromptUnifiedNodeSchemaCatalog.UnregisterCustomNode(nodeId);

            ApplyUnifiedCatalogPersistence(persistToFiles: true);

            // Reset selection to first section
            _promptWorkspaceEditNodeMode = false;
            _promptWorkspaceSelectedNodeId = string.Empty;
            EnsurePromptWorkspaceBuffer();
            InvalidatePromptWorkspaceNodeUiCaches();
            InvalidatePromptWorkspacePreviewCache();

            Messages.Message("RimChat_ModuleDeleteSuccess".Translate(displayName), MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
