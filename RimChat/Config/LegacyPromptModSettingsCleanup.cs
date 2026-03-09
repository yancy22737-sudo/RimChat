using System;
using System.IO;
using System.Xml;
using RimChat.Core;
using Verse;

namespace RimChat.Config
{
    /// <summary>/// Dependencies: XML file I/O, RimWorld config paths.
 /// Responsibility: one-time cleanup of legacy prompt fields from Mod_*.xml settings files.
 ///</summary>
    internal static class LegacyPromptModSettingsCleanup
    {
        private const string PromptFolderName = "Prompt";
        private const string CustomSubFolderName = "Custom";
        private const string MarkerFileName = "legacy_prompt_modsettings_cleanup_v1.done";

        private static readonly string[] LegacyPromptFieldNames =
        {
            "GlobalSystemPrompt",
            "GlobalDialoguePrompt",
            "RPGRoleSetting",
            "RPGDialogueStyle",
            "RPGApiGuidelines",
            "RPGFormatConstraint",
            "RPGSystemPrompt",
            "RPGDialoguePrompt",
            "RPGApiFormatPrompt"
        };

        public static void RunOnce()
        {
            try
            {
                string markerPath = GetMarkerPath();
                if (File.Exists(markerPath))
                {
                    return;
                }

                int changedFiles = CleanupModSettingsFiles();
                WriteMarker(markerPath, changedFiles);
                if (changedFiles > 0)
                {
                    Log.Message($"[RimChat] Legacy prompt fields cleaned from {changedFiles} ModSettings file(s).");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Legacy prompt settings cleanup failed: {ex.Message}");
            }
        }

        private static int CleanupModSettingsFiles()
        {
            string configDir = GenFilePaths.ConfigFolderPath;
            if (string.IsNullOrWhiteSpace(configDir) || !Directory.Exists(configDir))
            {
                return 0;
            }

            int changedFiles = 0;
            string[] files = Directory.GetFiles(configDir, "Mod_*.xml", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                if (CleanupSingleFile(files[i]))
                {
                    changedFiles++;
                }
            }

            return changedFiles;
        }

        private static bool CleanupSingleFile(string path)
        {
            var doc = new XmlDocument();
            doc.Load(path);
            XmlNodeList modSettingsNodes = doc.SelectNodes("/SettingsBlock/ModSettings");
            if (modSettingsNodes == null || modSettingsNodes.Count == 0)
            {
                return false;
            }

            bool changed = false;
            for (int i = 0; i < modSettingsNodes.Count; i++)
            {
                changed |= RemoveLegacyPromptFields(modSettingsNodes[i]);
            }

            if (!changed)
            {
                return false;
            }

            doc.Save(path);
            return true;
        }

        private static bool RemoveLegacyPromptFields(XmlNode modSettingsNode)
        {
            if (modSettingsNode == null)
            {
                return false;
            }

            bool changed = false;
            for (int i = 0; i < LegacyPromptFieldNames.Length; i++)
            {
                XmlNodeList nodes = modSettingsNode.SelectNodes(LegacyPromptFieldNames[i]);
                for (int j = nodes?.Count - 1 ?? -1; j >= 0; j--)
                {
                    modSettingsNode.RemoveChild(nodes[j]);
                    changed = true;
                }
            }

            return changed;
        }

        private static void WriteMarker(string markerPath, int changedFiles)
        {
            string directory = Path.GetDirectoryName(markerPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string content = $"changed_files={changedFiles}\nutc={DateTime.UtcNow:O}";
            File.WriteAllText(markerPath, content);
        }

        private static string GetMarkerPath()
        {
            string customDir = ResolveCustomDirFromMod();
            if (string.IsNullOrWhiteSpace(customDir))
            {
                customDir = Path.Combine(GenFilePaths.ConfigFolderPath, "RimChat", PromptFolderName, CustomSubFolderName);
            }

            return Path.Combine(customDir, MarkerFileName);
        }

        private static string ResolveCustomDirFromMod()
        {
            try
            {
                var mod = LoadedModManager.GetMod<RimChatMod>();
                if (mod?.Content == null)
                {
                    return string.Empty;
                }

                return Path.Combine(mod.Content.RootDir, PromptFolderName, CustomSubFolderName);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
