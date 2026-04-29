using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimChat.Config;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: PromptWorkbenchModuleProjection.
    /// Responsibility: collect module selection and path for multi-module export.
    /// </summary>
    public sealed class Dialog_PromptModuleMultiExport : Window
    {
        private const string DefaultExportFileName = "RimChat_Modules.json";
        private readonly List<PromptWorkbenchModuleItem> _allModules;
        private readonly Dictionary<string, bool> _selected = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Action<string, List<PromptWorkbenchModuleItem>> _onExport;
        private string _filePath;
        private Vector2 _scroll = Vector2.zero;

        public override Vector2 InitialSize => new Vector2(640f, 500f);

        internal Dialog_PromptModuleMultiExport(
            List<PromptWorkbenchModuleItem> allModules,
            Action<string, List<PromptWorkbenchModuleItem>> onExport)
        {
            _allModules = allModules ?? new List<PromptWorkbenchModuleItem>();
            _onExport = onExport;
            _filePath = BuildDesktopPath(DefaultExportFileName);
            doCloseButton = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;

            foreach (PromptWorkbenchModuleItem m in _allModules)
            {
                _selected[m.Id] = true;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "RimChat_ModuleMultiExportTitle".Translate());
            Text.Font = GameFont.Small;
            float y = inRect.y + 36f;

            // Path row
            Widgets.Label(new Rect(inRect.x, y, 60f, 24f), "RimChat_FilePathLabel".Translate());
            _filePath = Widgets.TextField(new Rect(inRect.x + 65f, y, inRect.width - 65f, 24f), _filePath);
            y += 28f;

            // Quick path button
            Rect desktopBtn = new Rect(inRect.x, y, 120f, 24f);
            if (Widgets.ButtonText(desktopBtn, "RimChat_PromptBundleQuickPathDesktop".Translate()))
            {
                _filePath = BuildDesktopPath(GetCurrentFileName());
            }
            y += 30f;

            // Select all / none
            Rect selectAll = new Rect(inRect.x, y, 100f, 22f);
            Rect selectNone = new Rect(inRect.x + 106f, y, 100f, 22f);
            if (Widgets.ButtonText(selectAll, "RimChat_ModuleSelectAll".Translate()))
            {
                foreach (PromptWorkbenchModuleItem m in _allModules) _selected[m.Id] = true;
            }
            if (Widgets.ButtonText(selectNone, "RimChat_ModuleSelectNone".Translate()))
            {
                foreach (PromptWorkbenchModuleItem m in _allModules) _selected[m.Id] = false;
            }
            y += 28f;

            // Module list with checkboxes
            Rect listRect = new Rect(inRect.x, y, inRect.width, inRect.yMax - y - 44f);
            DrawModuleCheckboxList(listRect);

            // Buttons
            Rect exportRect = new Rect(inRect.xMax - 220f, inRect.yMax - 34f, 100f, 30f);
            Rect cancelRect = new Rect(inRect.xMax - 110f, inRect.yMax - 34f, 100f, 30f);

            if (Widgets.ButtonText(exportRect, "RimChat_ModuleExportBtn".Translate()))
            {
                if (!TryBuildExportPath(_filePath, out string outputPath))
                {
                    return;
                }

                List<PromptWorkbenchModuleItem> selected = _allModules
                    .Where(m => _selected.TryGetValue(m.Id, out bool v) && v)
                    .ToList();
                if (selected.Count == 0)
                {
                    Messages.Message("RimChat_PromptBundleNoModuleSelected".Translate(), MessageTypeDefOf.RejectInput, false);
                    return;
                }

                _onExport?.Invoke(outputPath, selected);
                Close();
            }

            if (Widgets.ButtonText(cancelRect, "RimChat_CancelButton".Translate()))
            {
                Close();
            }
        }

        private void DrawModuleCheckboxList(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(8f);
            float rowHeight = 26f;
            float viewHeight = Mathf.Max(inner.height, _allModules.Count * rowHeight + 4f);
            Rect view = new Rect(0f, 0f, inner.width - 16f, viewHeight);
            _scroll = GUI.BeginScrollView(inner, _scroll, view);

            string sectionTag = "RimChat_PromptWorkspaceKind_Section".Translate().ToString();
            string nodeTag = "RimChat_PromptWorkspaceKind_Node".Translate().ToString();

            float yPos = 0f;
            for (int i = 0; i < _allModules.Count; i++)
            {
                PromptWorkbenchModuleItem module = _allModules[i];
                bool enabled = _selected[module.Id];
                string kindTag = module.Kind == ModuleKind.Section ? sectionTag : nodeTag;
                string label = $"{module.Label} [{kindTag}]";
                Rect row = new Rect(0f, yPos, view.width, rowHeight - 2f);
                Widgets.CheckboxLabeled(row, label, ref enabled);
                _selected[module.Id] = enabled;
                yPos += rowHeight;
            }

            GUI.EndScrollView();
        }

        private string GetCurrentFileName()
        {
            string fileName = Path.GetFileName(_filePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return DefaultExportFileName;
            }

            if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".json";
            }

            return fileName;
        }

        private static string BuildDesktopPath(string fileName)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            return Path.Combine(desktop, string.IsNullOrWhiteSpace(fileName) ? DefaultExportFileName : fileName);
        }

        private static bool TryBuildExportPath(string rawPath, out string outputPath)
        {
            outputPath = rawPath?.Trim().Trim('"') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                Messages.Message("RimChat_FilePathEmpty".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(Path.GetExtension(outputPath)))
                {
                    outputPath += ".json";
                }
                else if (!outputPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    outputPath = Path.ChangeExtension(outputPath, ".json");
                }

                if (!Path.IsPathRooted(outputPath))
                {
                    outputPath = BuildDesktopPath(outputPath);
                }

                string directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                return true;
            }
            catch (Exception ex)
            {
                Messages.Message("RimChat_InvalidPath".Translate(ex.Message), MessageTypeDefOf.RejectInput, false);
                return false;
            }
        }
    }
}
