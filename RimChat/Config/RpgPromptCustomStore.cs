using System;
using System.Collections.Generic;
using System.IO;
using RimChat.Core;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>/// Dependencies: JSON file I/O, RimWorld mod path APIs.
 /// Responsibility: persist RPG prompt overrides under Prompt/Custom only.
 ///</summary>
    [Serializable]
    internal sealed class RpgPromptCustomConfig
    {
        public string RoleSetting;
        public string DialogueStyle;
        public string FormatConstraint;
        public string RoleSettingFallbackTemplate;
        public string FormatConstraintHeader;
        public string CompactFormatFallback;
        public string ActionReliabilityFallback;
        public string ActionReliabilityMarker;
        public RpgApiActionPromptConfig ApiActionPrompt;
        public bool EnableRimTalkPromptCompat;
        public int RimTalkSummaryHistoryLimit;
        public string RimTalkCompatTemplate;
    }

    /// <summary>/// Dependencies: RpgPromptDefaultsProvider, Unity JsonUtility.
 /// Responsibility: load/save RPG prompt custom overrides from Prompt/Custom/RpgPrompts_Custom.json.
 ///</summary>
    internal static class RpgPromptCustomStore
    {
        private const string PromptFolderName = "Prompt";
        private const string CustomSubFolderName = "Custom";
        private const string CustomConfigFileName = "RpgPrompts_Custom.json";
        private static string loggedCustomPath = string.Empty;

        public static RpgPromptCustomConfig LoadOrDefault()
        {
            RpgPromptDefaultsConfig defaults = RpgPromptDefaultsProvider.GetDefaults();
            RpgPromptCustomConfig config = BuildDefaultConfig(defaults);
            string path = GetCustomConfigPath();
            if (!File.Exists(path))
            {
                LogResolvedCustomPayload(config, exists: false);
                return config;
            }

            try
            {
                string json = File.ReadAllText(path);
                RpgPromptCustomConfig custom = JsonUtility.FromJson<RpgPromptCustomConfig>(json);
                MergeCustomIntoBase(config, custom);
                LogResolvedCustomPayload(config, exists: true);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to load RPG custom prompts from {path}: {ex.Message}");
            }

            return config;
        }

        public static void Save(RpgPromptCustomConfig config)
        {
            if (config == null)
            {
                return;
            }

            string path = GetCustomConfigPath();
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonUtility.ToJson(config, true);
            File.WriteAllText(path, json);
        }

        public static bool CustomConfigExists()
        {
            return File.Exists(GetCustomConfigPath());
        }

        private static RpgPromptCustomConfig BuildDefaultConfig(RpgPromptDefaultsConfig defaults)
        {
            return new RpgPromptCustomConfig
            {
                RoleSetting = defaults?.RoleSettingDefault ?? string.Empty,
                DialogueStyle = defaults?.DialogueStyleDefault ?? string.Empty,
                FormatConstraint = defaults?.FormatConstraintDefault ?? string.Empty,
                RoleSettingFallbackTemplate = defaults?.RoleSettingFallbackTemplate ?? string.Empty,
                FormatConstraintHeader = defaults?.FormatConstraintHeader ?? string.Empty,
                CompactFormatFallback = defaults?.CompactFormatFallback ?? string.Empty,
                ActionReliabilityFallback = defaults?.ActionReliabilityFallback ?? string.Empty,
                ActionReliabilityMarker = defaults?.ActionReliabilityMarker ?? string.Empty,
                ApiActionPrompt = defaults?.ApiActionPrompt?.Clone() ?? RpgApiActionPromptConfig.CreateFallback(),
                EnableRimTalkPromptCompat = true,
                RimTalkSummaryHistoryLimit = 10,
                RimTalkCompatTemplate = RimChatSettings.DefaultRimTalkCompatTemplate
            };
        }

        private static void MergeCustomIntoBase(RpgPromptCustomConfig target, RpgPromptCustomConfig custom)
        {
            if (target == null || custom == null)
            {
                return;
            }

            if (custom.RoleSetting != null)
            {
                target.RoleSetting = custom.RoleSetting;
            }

            if (custom.DialogueStyle != null)
            {
                target.DialogueStyle = custom.DialogueStyle;
            }

            if (custom.FormatConstraint != null)
            {
                target.FormatConstraint = custom.FormatConstraint;
            }

            if (custom.RoleSettingFallbackTemplate != null)
            {
                target.RoleSettingFallbackTemplate = custom.RoleSettingFallbackTemplate;
            }

            if (custom.FormatConstraintHeader != null)
            {
                target.FormatConstraintHeader = custom.FormatConstraintHeader;
            }

            if (custom.CompactFormatFallback != null)
            {
                target.CompactFormatFallback = custom.CompactFormatFallback;
            }

            if (custom.ActionReliabilityFallback != null)
            {
                target.ActionReliabilityFallback = custom.ActionReliabilityFallback;
            }

            if (custom.ActionReliabilityMarker != null)
            {
                target.ActionReliabilityMarker = custom.ActionReliabilityMarker;
            }

            MergeApiActionPrompt(target.ApiActionPrompt, custom.ApiActionPrompt);

            bool hasRimTalkPayload =
                custom.RimTalkCompatTemplate != null ||
                custom.RimTalkSummaryHistoryLimit != 0;
            if (!hasRimTalkPayload)
            {
                return;
            }

            target.EnableRimTalkPromptCompat = custom.EnableRimTalkPromptCompat;
            if (custom.RimTalkSummaryHistoryLimit != 0)
            {
                target.RimTalkSummaryHistoryLimit = custom.RimTalkSummaryHistoryLimit;
            }

            if (custom.RimTalkCompatTemplate != null)
            {
                target.RimTalkCompatTemplate = custom.RimTalkCompatTemplate;
            }
        }

        private static void MergeApiActionPrompt(RpgApiActionPromptConfig target, RpgApiActionPromptConfig custom)
        {
            if (target == null || custom == null)
            {
                return;
            }

            if (custom.FullHeader != null)
            {
                target.FullHeader = custom.FullHeader;
            }

            if (custom.FullIntro != null)
            {
                target.FullIntro = custom.FullIntro;
            }

            if (custom.FullActionObjectHint != null)
            {
                target.FullActionObjectHint = custom.FullActionObjectHint;
            }

            if (custom.FullActionReliabilityGuidance != null)
            {
                target.FullActionReliabilityGuidance = custom.FullActionReliabilityGuidance;
            }

            if (custom.FullClosureReliabilityGuidance != null)
            {
                target.FullClosureReliabilityGuidance = custom.FullClosureReliabilityGuidance;
            }

            if (custom.FullTryGainMemoryLineTemplate != null)
            {
                target.FullTryGainMemoryLineTemplate = custom.FullTryGainMemoryLineTemplate;
            }

            if (custom.SharedActionLines != null)
            {
                target.SharedActionLines = new List<string>(custom.SharedActionLines);
            }

            if (custom.CompactHeader != null)
            {
                target.CompactHeader = custom.CompactHeader;
            }

            if (custom.CompactIntro != null)
            {
                target.CompactIntro = custom.CompactIntro;
            }

            if (custom.CompactAllowedActionsTemplate != null)
            {
                target.CompactAllowedActionsTemplate = custom.CompactAllowedActionsTemplate;
            }

            if (custom.CompactTryGainMemoryTemplate != null)
            {
                target.CompactTryGainMemoryTemplate = custom.CompactTryGainMemoryTemplate;
            }

            if (custom.CompactActionFieldsHint != null)
            {
                target.CompactActionFieldsHint = custom.CompactActionFieldsHint;
            }

            if (custom.CompactClosureGuidance != null)
            {
                target.CompactClosureGuidance = custom.CompactClosureGuidance;
            }

            if (custom.CompactActionNames != null)
            {
                target.CompactActionNames = new List<string>(custom.CompactActionNames);
            }
        }

        private static string GetCustomConfigPath()
        {
            string assemblyPath = ResolveFromAssemblyPath();
            if (!string.IsNullOrWhiteSpace(assemblyPath))
            {
                LogResolvedCustomPath(assemblyPath, "assembly");
                return assemblyPath;
            }

            string modPath = ResolveFromModPath();
            if (!string.IsNullOrWhiteSpace(modPath))
            {
                LogResolvedCustomPath(modPath, "mod-root");
                return modPath;
            }

            string fallbackDir = Path.Combine(GenFilePaths.ConfigFolderPath, "RimChat", PromptFolderName, CustomSubFolderName);
            string fallbackPath = Path.Combine(fallbackDir, CustomConfigFileName);
            LogResolvedCustomPath(fallbackPath, "appdata-fallback");
            return fallbackPath;
        }

        private static string ResolveFromModPath()
        {
            try
            {
                var mod = LoadedModManager.GetMod<RimChatMod>();
                if (mod?.Content == null)
                {
                    return string.Empty;
                }

                string dir = Path.Combine(mod.Content.RootDir, PromptFolderName, CustomSubFolderName);
                return Path.Combine(dir, CustomConfigFileName);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ResolveFromAssemblyPath()
        {
            try
            {
                string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string assemblyDir = Path.GetDirectoryName(assemblyPath);
                string modDir = Directory.GetParent(assemblyDir)?.Parent?.FullName;
                if (string.IsNullOrWhiteSpace(modDir))
                {
                    return string.Empty;
                }

                string dir = Path.Combine(modDir, PromptFolderName, CustomSubFolderName);
                return Path.Combine(dir, CustomConfigFileName);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void LogResolvedCustomPath(string path, string source)
        {
            if (string.IsNullOrWhiteSpace(path) || string.Equals(loggedCustomPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            loggedCustomPath = path;
            Log.Message($"[RimChat] RPG custom prompt path ({source}): {path}");
        }

        private static void LogResolvedCustomPayload(RpgPromptCustomConfig config, bool exists)
        {
            if (config == null)
            {
                return;
            }

            string fullHeader = config.ApiActionPrompt?.FullHeader ?? "<null>";
            string compactHeader = config.ApiActionPrompt?.CompactHeader ?? "<null>";
            string reliability = config.ActionReliabilityFallback ?? "<null>";
            string tryGainMemory = config.ApiActionPrompt?.FullTryGainMemoryLineTemplate ?? "<null>";
            Log.Message(
                $"[RimChat] RPG custom prompt payload (exists={exists}): FullHeader='{fullHeader}', CompactHeader='{compactHeader}', ActionReliabilityFallback='{reliability}', FullTryGainMemoryLineTemplate='{tryGainMemory}'");
        }
    }
}
