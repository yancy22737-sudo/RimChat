using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimChat.Persistence;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: RimWorld window/widgets and selective bundle module models.
    /// Responsibility: collect path and module selection for prompt-bundle export.
    /// </summary>
    public sealed class Dialog_PromptBundleExport : Window
    {
        private const string DefaultExportFileName = "RimChat_PromptBundle.json";
        private readonly Action<string, List<PromptBundleModule>> _onExport;
        private readonly Dictionary<PromptBundleModule, bool> _moduleEnabled = new Dictionary<PromptBundleModule, bool>();
        private string _filePath;
        private bool _partialMode;

        public override Vector2 InitialSize => new Vector2(680f, 460f);

        internal Dialog_PromptBundleExport(
            string defaultPath,
            Action<string, List<PromptBundleModule>> onExport)
        {
            _filePath = string.IsNullOrWhiteSpace(defaultPath)
                ? BuildDesktopPath(DefaultExportFileName)
                : defaultPath;
            _onExport = onExport;
            doCloseButton = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;

            foreach (PromptBundleModule module in PromptBundleModuleCatalog.All)
            {
                _moduleEnabled[module] = true;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(
                new Rect(inRect.x, inRect.y, inRect.width, 30f),
                "RimChat_PromptBundleExportTitle".Translate());
            Text.Font = GameFont.Small;

            float y = inRect.y + 36f;
            Widgets.Label(new Rect(inRect.x, y, 90f, 24f), "RimChat_FilePathLabel".Translate());
            _filePath = Widgets.TextField(new Rect(inRect.x + 95f, y, inRect.width - 95f, 24f), _filePath);
            y += 34f;

            DrawQuickPathButtons(new Rect(inRect.x, y, inRect.width, 24f));
            y += 30f;

            Rect fullModeRect = new Rect(inRect.x, y, 220f, 24f);
            Rect partialModeRect = new Rect(fullModeRect.xMax + 10f, y, 260f, 24f);
            DrawModeToggle(fullModeRect, partialModeRect);
            y += 32f;

            if (_partialMode)
            {
                DrawModuleCheckboxList(new Rect(inRect.x, y, inRect.width, inRect.height - 120f));
            }

            Rect saveRect = new Rect(inRect.xMax - 220f, inRect.yMax - 34f, 100f, 30f);
            Rect cancelRect = new Rect(inRect.xMax - 110f, inRect.yMax - 34f, 100f, 30f);

            if (Widgets.ButtonText(saveRect, "RimChat_SaveButton".Translate()))
            {
                if (!TryBuildExportPath(_filePath, out string outputPath))
                {
                    return;
                }

                List<PromptBundleModule> modules = null;
                if (_partialMode)
                {
                    modules = _moduleEnabled.Where(item => item.Value).Select(item => item.Key).ToList();
                    if (modules.Count == 0)
                    {
                        Messages.Message("RimChat_PromptBundleNoModuleSelected".Translate(), MessageTypeDefOf.RejectInput, false);
                        return;
                    }
                }

                _onExport?.Invoke(outputPath, modules);
                Close();
            }

            if (Widgets.ButtonText(cancelRect, "RimChat_CancelButton".Translate()))
            {
                Close();
            }
        }

        private void DrawModeToggle(Rect fullModeRect, Rect partialModeRect)
        {
            if (Widgets.RadioButtonLabeled(
                fullModeRect,
                "RimChat_PromptBundleExportModeFull".Translate(),
                !_partialMode))
            {
                _partialMode = false;
            }

            if (Widgets.RadioButtonLabeled(
                partialModeRect,
                "RimChat_PromptBundleExportModePartial".Translate(),
                _partialMode))
            {
                _partialMode = true;
            }
        }

        private void DrawQuickPathButtons(Rect rect)
        {
            Rect labelRect = new Rect(rect.x, rect.y, 110f, rect.height);
            Rect desktopRect = new Rect(labelRect.xMax + 6f, rect.y, 120f, rect.height);
            Rect configRect = new Rect(desktopRect.xMax + 6f, rect.y, 180f, rect.height);

            Widgets.Label(labelRect, "RimChat_PromptBundleQuickPath".Translate());
            if (Widgets.ButtonText(desktopRect, "RimChat_PromptBundleQuickPathDesktop".Translate()))
            {
                ApplyQuickPath(BuildDesktopPath(GetCurrentFileName()));
            }

            if (Widgets.ButtonText(configRect, "RimChat_PromptBundleQuickPathConfig".Translate()))
            {
                string directory = Path.Combine(GenFilePaths.ConfigFolderPath, "RimChat", "Exports");
                ApplyQuickPath(Path.Combine(directory, GetCurrentFileName()));
            }
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

        private void ApplyQuickPath(string fullPath)
        {
            if (!string.IsNullOrWhiteSpace(fullPath))
            {
                _filePath = fullPath;
            }
        }

        private static string BuildDesktopPath(string fileName)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            return Path.Combine(desktop, string.IsNullOrWhiteSpace(fileName) ? DefaultExportFileName : fileName);
        }

        private void DrawModuleCheckboxList(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(8f);
            float y = inner.y;

            Widgets.Label(new Rect(inner.x, y, inner.width, 22f), "RimChat_PromptBundleModuleSelectHint".Translate());
            y += 26f;

            foreach (PromptBundleModule module in PromptBundleModuleCatalog.All)
            {
                bool enabled = _moduleEnabled[module];
                Rect row = new Rect(inner.x, y, inner.width, 24f);
                Widgets.CheckboxLabeled(row, GetModuleLabel(module), ref enabled);
                _moduleEnabled[module] = enabled;
                y += 26f;
            }
        }

        private static string GetModuleLabel(PromptBundleModule module)
        {
            return ("RimChat_PromptBundleModule_" + module).Translate();
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

                outputPath = EnsureUniquePath(outputPath);
                return true;
            }
            catch (Exception ex)
            {
                Messages.Message("RimChat_InvalidPath".Translate(ex.Message), MessageTypeDefOf.RejectInput, false);
                return false;
            }
        }

        private static string EnsureUniquePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return path;
            }

            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            string fileName = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            int suffix = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(directory, fileName + "_" + suffix + extension);
                suffix++;
            } while (File.Exists(candidate));

            return candidate;
        }
    }
}
