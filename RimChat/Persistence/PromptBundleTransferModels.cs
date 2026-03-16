using System;
using System.Collections.Generic;
using System.Linq;

namespace RimChat.Persistence
{
    /// <summary>
    /// Dependencies: prompt bundle payload serialization and settings import/export UI.
    /// Responsibility: define module identifiers and transfer-preview models for selective bundle import/export.
    /// </summary>
    internal enum PromptBundleModule
    {
        SystemPrompt = 0,
        DiplomacyPrompt = 1,
        RpgPrompt = 2,
        SocialCirclePrompt = 3,
        FactionPrompts = 4
    }

    internal static class PromptBundleModuleCatalog
    {
        private static readonly PromptBundleModule[] OrderedModules =
        {
            PromptBundleModule.SystemPrompt,
            PromptBundleModule.DiplomacyPrompt,
            PromptBundleModule.RpgPrompt,
            PromptBundleModule.SocialCirclePrompt,
            PromptBundleModule.FactionPrompts
        };

        private static readonly PromptBundleModule[] ExportableModules =
        {
            PromptBundleModule.SystemPrompt,
            PromptBundleModule.DiplomacyPrompt,
            PromptBundleModule.RpgPrompt,
            PromptBundleModule.SocialCirclePrompt,
            PromptBundleModule.FactionPrompts
        };

        public static IReadOnlyList<PromptBundleModule> All => ExportableModules;

        public static string ToStorageToken(PromptBundleModule module)
        {
            switch (module)
            {
                case PromptBundleModule.SystemPrompt:
                    return "system_prompt";
                case PromptBundleModule.DiplomacyPrompt:
                    return "diplomacy_prompt";
                case PromptBundleModule.RpgPrompt:
                    return "rpg_prompt";
                case PromptBundleModule.SocialCirclePrompt:
                    return "social_circle_prompt";
                case PromptBundleModule.FactionPrompts:
                    return "faction_prompts";
                default:
                    return "unknown";
            }
        }

        public static bool TryParseStorageToken(string token, out PromptBundleModule module)
        {
            module = PromptBundleModule.SystemPrompt;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            string normalized = token.Trim().ToLowerInvariant();
            foreach (PromptBundleModule item in OrderedModules)
            {
                if (string.Equals(ToStorageToken(item), normalized, StringComparison.Ordinal))
                {
                    module = item;
                    return true;
                }
            }

            return false;
        }

        public static List<string> ToStorageTokens(IEnumerable<PromptBundleModule> modules)
        {
            if (modules == null)
            {
                return new List<string>();
            }

            return modules
                .Distinct()
                .Select(ToStorageToken)
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .ToList();
        }
    }

    internal sealed class PromptBundleImportPreview
    {
        public string FilePath = string.Empty;
        public int BundleVersion = 1;
        public List<PromptBundleModule> AvailableModules = new List<PromptBundleModule>();
        public Dictionary<PromptBundleModule, string> ModuleSummaries = new Dictionary<PromptBundleModule, string>();
    }
}
