using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Prompting;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: prompt section/node schema catalogs and unified node layout persistence.
    /// Responsibility: build a flat, ordered projection of Section + Node items for the unified module list,
    /// sorted by Slot then Order to match the actual prompt assembly order (WYSIWYG).
    /// </summary>
    internal readonly struct PromptWorkbenchModuleItem
    {
        internal readonly string Id;
        internal readonly ModuleKind Kind;
        internal readonly string Label;
        internal readonly bool Enabled;
        internal readonly int DisplayOrder;
        internal readonly PromptUnifiedNodeSlot Slot;

        internal PromptWorkbenchModuleItem(
            string id,
            ModuleKind kind,
            string label,
            bool enabled,
            int displayOrder,
            PromptUnifiedNodeSlot slot)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Label = label ?? string.Empty;
            Enabled = enabled;
            DisplayOrder = displayOrder;
            Slot = slot;
        }
    }

    internal enum ModuleKind
    {
        Section,
        Node
    }

    /// <summary>
    /// Builds a flat, unified projection of Sections and Nodes sorted by Slot+Order
    /// so the left panel displays items in the same order as the preview assembly (WYSIWYG).
    /// Sections are mapped to the MainChainBefore slot by default,
    /// and all items are interleaved by Slot then Order.
    /// </summary>
    internal static class PromptWorkbenchModuleProjection
    {
        internal static List<PromptWorkbenchModuleItem> BuildModules(
            string promptChannel,
            IReadOnlyList<PromptSectionLayoutConfig> sectionLayouts,
            IReadOnlyList<PromptUnifiedNodeLayoutConfig> nodeLayouts)
        {
            var modules = new List<PromptWorkbenchModuleItem>();

            // Sections ordered by persisted section layouts (fallback to canonical order)
            IReadOnlyList<PromptSectionSchemaItem> sections = PromptSectionSchemaCatalog.GetOrderedMainChainSections(
                sectionLayouts as List<PromptSectionLayoutConfig>);
            for (int i = 0; i < sections.Count; i++)
            {
                PromptSectionSchemaItem section = sections[i];
                int order = ResolveSectionOrder(section.Id, sectionLayouts, i);
                bool enabled = ResolveSectionEnabled(section.Id, sectionLayouts);
                modules.Add(new PromptWorkbenchModuleItem(
                    section.Id,
                    ModuleKind.Section,
                    section.GetDisplayLabel(),
                    enabled: enabled,
                    displayOrder: order,
                    slot: PromptUnifiedNodeSlot.MainChainBefore));
            }

            // Nodes with enabled/slot/order from layout
            if (nodeLayouts != null && nodeLayouts.Count > 0)
            {
                for (int i = 0; i < nodeLayouts.Count; i++)
                {
                    PromptUnifiedNodeLayoutConfig layout = nodeLayouts[i];
                    if (layout == null || string.IsNullOrWhiteSpace(layout.NodeId))
                    {
                        continue;
                    }

                    string nodeLabel = PromptUnifiedNodeSchemaCatalog.GetDisplayLabel(layout.NodeId);
                    PromptUnifiedNodeSlot slot = layout.GetSlot();
                    modules.Add(new PromptWorkbenchModuleItem(
                        layout.NodeId,
                        ModuleKind.Node,
                        nodeLabel,
                        layout.Enabled,
                        layout.Order,
                        slot));
                }
            }

            // Sort by Slot then Order to match preview assembly order (WYSIWYG)
            return modules
                .OrderBy(m => m.Slot)
                .ThenBy(m => m.DisplayOrder)
                .ThenBy(m => m.Kind == ModuleKind.Section ? 0 : 1)
                .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static int ResolveSectionOrder(
            string sectionId,
            IReadOnlyList<PromptSectionLayoutConfig> layouts,
            int fallbackIndex)
        {
            if (layouts != null)
            {
                for (int i = 0; i < layouts.Count; i++)
                {
                    if (layouts[i] != null && string.Equals(layouts[i].SectionId, sectionId, StringComparison.OrdinalIgnoreCase))
                    {
                        return layouts[i].Order;
                    }
                }
            }

            return fallbackIndex * 10;
        }

        private static bool ResolveSectionEnabled(
            string sectionId,
            IReadOnlyList<PromptSectionLayoutConfig> layouts)
        {
            if (layouts != null)
            {
                for (int i = 0; i < layouts.Count; i++)
                {
                    if (layouts[i] != null && string.Equals(layouts[i].SectionId, sectionId, StringComparison.OrdinalIgnoreCase))
                    {
                        return layouts[i].Enabled;
                    }
                }
            }

            return true;
        }
    }
}
