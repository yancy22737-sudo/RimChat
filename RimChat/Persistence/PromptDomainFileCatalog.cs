using System;
using System.IO;
using RimChat.Core;
using Verse;

namespace RimChat.Persistence
{
    /// <summary>
    /// Dependencies: RimWorld mod path APIs and System.IO.
    /// Responsibility: resolve prompt-domain default/custom file paths.
    /// </summary>
    internal static class PromptDomainFileCatalog
    {
        public const string PromptFolderName = "Prompt";
        public const string DefaultSubFolderName = "Default";
        public const string CustomSubFolderName = "Custom";
        public const string SystemPromptDefaultFileName = "SystemPrompt_Default.json";
        public const string SystemPromptCustomFileName = "SystemPrompt_Custom.json";
        public const string DiplomacyPromptDefaultFileName = "DiplomacyDialoguePrompt_Default.json";
        public const string DiplomacyPromptCustomFileName = "DiplomacyDialoguePrompt_Custom.json";
        public const string PawnPromptDefaultFileName = "PawnDialoguePrompt_Default.json";
        public const string PawnPromptCustomFileName = "PawnDialoguePrompt_Custom.json";
        public const string SocialCirclePromptDefaultFileName = "SocialCirclePrompt_Default.json";
        public const string SocialCirclePromptCustomFileName = "SocialCirclePrompt_Custom.json";
        public const string FactionPromptCustomFileName = "FactionPrompts_Custom.json";

        private const string FallbackRoot = "E:\\SteamLibrary\\steamapps\\common\\RimWorld\\Mods\\RimChat";

        public static string GetDefaultPath(string fileName)
        {
            string root = ResolveModRoot();
            if (string.IsNullOrWhiteSpace(root))
            {
                root = FallbackRoot;
            }

            return Path.Combine(root, PromptFolderName, DefaultSubFolderName, fileName);
        }

        public static string GetCustomPath(string fileName)
        {
            string root = ResolveModRoot();
            if (!string.IsNullOrWhiteSpace(root))
            {
                string dir = Path.Combine(root, PromptFolderName, CustomSubFolderName);
                return Path.Combine(dir, fileName);
            }

            string fallbackDir = Path.Combine(GenFilePaths.ConfigFolderPath, "RimChat", PromptFolderName, CustomSubFolderName);
            return Path.Combine(fallbackDir, fileName);
        }

        public static void EnsureCustomDirectoryExists()
        {
            string path = GetCustomPath(SystemPromptCustomFileName);
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static string ResolveModRoot()
        {
            string modRoot = ResolveFromLoadedMod();
            if (!string.IsNullOrWhiteSpace(modRoot))
            {
                return modRoot;
            }

            return ResolveFromAssembly();
        }

        private static string ResolveFromLoadedMod()
        {
            try
            {
                return LoadedModManager.GetMod<RimChatMod>()?.Content?.RootDir ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ResolveFromAssembly()
        {
            try
            {
                string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string assemblyDir = Path.GetDirectoryName(assemblyPath);
                return Directory.GetParent(assemblyDir)?.Parent?.FullName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
