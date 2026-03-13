using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Persistence;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: prompt-bundle preview model and RimWorld modal UI widgets.
    /// Responsibility: display selectable modules from import file before applying.
    /// </summary>
    public sealed class Dialog_PromptBundleImportPreview : Window
    {
        private readonly PromptBundleImportPreview _preview;
        private readonly Action<List<PromptBundleModule>> _onImport;
        private readonly Dictionary<PromptBundleModule, bool> _selected = new Dictionary<PromptBundleModule, bool>();
        private Vector2 _scroll = Vector2.zero;

        public override Vector2 InitialSize => new Vector2(760f, 520f);

        internal Dialog_PromptBundleImportPreview(
            PromptBundleImportPreview preview,
            Action<List<PromptBundleModule>> onImport)
        {
            _preview = preview ?? new PromptBundleImportPreview();
            _onImport = onImport;
            doCloseButton = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;

            foreach (PromptBundleModule module in _preview.AvailableModules)
            {
                _selected[module] = true;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "RimChat_PromptBundleImportTitle".Translate());
            Text.Font = GameFont.Small;

            float y = inRect.y + 36f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "RimChat_PromptBundleImportFile".Translate(_preview.FilePath ?? string.Empty));
            y += 24f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "RimChat_PromptBundleImportVersion".Translate(_preview.BundleVersion));
            y += 30f;

            Rect listRect = new Rect(inRect.x, y, inRect.width, inRect.height - 120f);
            DrawModuleList(listRect);

            Rect importRect = new Rect(inRect.xMax - 220f, inRect.yMax - 34f, 100f, 30f);
            Rect cancelRect = new Rect(inRect.xMax - 110f, inRect.yMax - 34f, 100f, 30f);
            if (Widgets.ButtonText(importRect, "RimChat_LoadButton".Translate()))
            {
                List<PromptBundleModule> modules = _selected
                    .Where(item => item.Value)
                    .Select(item => item.Key)
                    .ToList();
                if (modules.Count == 0)
                {
                    Messages.Message("RimChat_PromptBundleNoModuleSelected".Translate(), MessageTypeDefOf.RejectInput, false);
                    return;
                }

                _onImport?.Invoke(modules);
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
            float rowHeight = 44f;
            float viewHeight = Mathf.Max(inner.height, _preview.AvailableModules.Count * rowHeight + 4f);
            Rect view = new Rect(0f, 0f, inner.width - 16f, viewHeight);
            _scroll = GUI.BeginScrollView(inner, _scroll, view);

            float y = 0f;
            for (int i = 0; i < _preview.AvailableModules.Count; i++)
            {
                PromptBundleModule module = _preview.AvailableModules[i];
                bool enabled = _selected[module];
                Rect row = new Rect(0f, y, view.width, rowHeight - 2f);
                Widgets.DrawHighlightIfMouseover(row);
                Widgets.CheckboxLabeled(new Rect(row.x, row.y + 2f, row.width, 22f), GetModuleLabel(module), ref enabled);
                _selected[module] = enabled;
                string summary = _preview.ModuleSummaries.TryGetValue(module, out string text) ? text : string.Empty;
                GUI.color = Color.gray;
                Widgets.Label(new Rect(row.x + 24f, row.y + 22f, row.width - 24f, 20f), summary);
                GUI.color = Color.white;
                y += rowHeight;
            }

            GUI.EndScrollView();
        }

        private static string GetModuleLabel(PromptBundleModule module)
        {
            return ("RimChat_PromptBundleModule_" + module).Translate();
        }
    }
}
