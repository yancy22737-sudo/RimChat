using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Config;
using RimChat.Persistence;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: PromptModuleExportBundle, PromptUnifiedNodeSlot enum.
    /// Responsibility: display selectable modules from import file before applying.
    /// </summary>
    public sealed class Dialog_PromptModuleImportPreview : Window
    {
        private readonly List<PromptModuleExportPayload> _modules;
        private readonly Dictionary<int, bool> _selected = new Dictionary<int, bool>();
        private readonly Action<List<PromptModuleExportPayload>> _onImport;
        private Vector2 _scroll = Vector2.zero;

        public override Vector2 InitialSize => new Vector2(680f, 520f);

        internal Dialog_PromptModuleImportPreview(
            List<PromptModuleExportPayload> modules,
            Action<List<PromptModuleExportPayload>> onImport)
        {
            _modules = modules ?? new List<PromptModuleExportPayload>();
            _onImport = onImport;
            doCloseButton = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;

            for (int i = 0; i < _modules.Count; i++)
            {
                _selected[i] = true;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "RimChat_ModuleImportPreviewTitle".Translate());
            Text.Font = GameFont.Small;
            float y = inRect.y + 36f;

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 22f),
                "RimChat_ModuleImportCount".Translate(_modules.Count));
            y += 26f;

            // Select all / none
            Rect selectAll = new Rect(inRect.x, y, 100f, 22f);
            Rect selectNone = new Rect(inRect.x + 106f, y, 100f, 22f);
            if (Widgets.ButtonText(selectAll, "RimChat_ModuleSelectAll".Translate()))
            {
                for (int i = 0; i < _modules.Count; i++) _selected[i] = true;
            }
            if (Widgets.ButtonText(selectNone, "RimChat_ModuleSelectNone".Translate()))
            {
                for (int i = 0; i < _modules.Count; i++) _selected[i] = false;
            }
            y += 28f;

            // Module list
            Rect listRect = new Rect(inRect.x, y, inRect.width, inRect.yMax - y - 44f);
            DrawModuleList(listRect);

            // Buttons
            Rect importRect = new Rect(inRect.xMax - 220f, inRect.yMax - 34f, 100f, 30f);
            Rect cancelRect = new Rect(inRect.xMax - 110f, inRect.yMax - 34f, 100f, 30f);

            if (Widgets.ButtonText(importRect, "RimChat_LoadButton".Translate()))
            {
                List<PromptModuleExportPayload> selected = _modules
                    .Where((m, idx) => _selected.TryGetValue(idx, out bool v) && v)
                    .ToList();
                if (selected.Count == 0)
                {
                    Messages.Message("RimChat_PromptBundleNoModuleSelected".Translate(), MessageTypeDefOf.RejectInput, false);
                    return;
                }

                _onImport?.Invoke(selected);
                Close();
            }

            if (Widgets.ButtonText(cancelRect, "RimChat_CancelButton".Translate()))
            {
                Close();
            }
        }

        private void DrawModuleList(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(8f);
            float rowHeight = 48f;
            float viewHeight = Mathf.Max(inner.height, _modules.Count * rowHeight + 4f);
            Rect view = new Rect(0f, 0f, inner.width - 16f, viewHeight);
            _scroll = GUI.BeginScrollView(inner, _scroll, view);

            float yPos = 0f;
            for (int i = 0; i < _modules.Count; i++)
            {
                PromptModuleExportPayload module = _modules[i];
                bool enabled = _selected[i];
                Rect row = new Rect(0f, yPos, view.width, rowHeight - 2f);
                Widgets.DrawHighlightIfMouseover(row);

                string slotDisplay = ResolveSlotLabel(module.Slot);
                string label = $"{module.DisplayName} [{slotDisplay}]";
                Widgets.CheckboxLabeled(new Rect(row.x, row.y + 2f, row.width, 22f), label, ref enabled);
                _selected[i] = enabled;

                string preview = module.Content ?? string.Empty;
                if (preview.Length > 100)
                {
                    preview = preview.Substring(0, 100) + "...";
                }

                GUI.color = Color.gray;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(row.x + 24f, row.y + 24f, row.width - 24f, 20f),
                    $"ID: {module.NodeId}  |  {preview}");
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                yPos += rowHeight;
            }

            GUI.EndScrollView();
        }

        private static string ResolveSlotLabel(string slotValue)
        {
            PromptUnifiedNodeSlot slot = PromptUnifiedNodeSlotExtensions.ToPromptUnifiedNodeSlot(slotValue);
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
                    return slotValue ?? string.Empty;
            }
        }
    }
}
