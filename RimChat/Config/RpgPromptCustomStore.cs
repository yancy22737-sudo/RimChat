using System;
using System.Collections.Generic;
using System.IO;
using RimChat.Core;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>/// Dependencies: JSON file I/O, RimWorld mod path APIs.
 /// Responsibility: persist pawn dialogue prompt overrides under Prompt/Custom only.
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
        public string RpgRoleSettingTemplate;
        public string RpgCompactFormatConstraintTemplate;
        public string RpgActionReliabilityRuleTemplate;
        public string DecisionPolicyTemplate;
        public string TurnObjectiveTemplate;
        public string OpeningObjectiveTemplate;
        public string TopicShiftRuleTemplate;
        public string PersonaBootstrapSystemPrompt;
        public string PersonaBootstrapUserPromptTemplate;
        public string PersonaBootstrapOutputTemplate;
        public string PersonaBootstrapExample;
        public RpgApiActionPromptConfig ApiActionPrompt;
        public bool EnableRimTalkPromptCompat;
        public int RimTalkSummaryHistoryLimit;
        public int RimTalkPresetInjectionMaxEntries;
        public int RimTalkPresetInjectionMaxChars;
        public string RimTalkCompatTemplate;
        public string RimTalkPersonaCopyTemplate;
        public RimTalkChannelCompatConfig RimTalkDiplomacy;
        public RimTalkChannelCompatConfig RimTalkRpg;
        public bool RimTalkChannelSplitMigrated;
    }

    /// <summary>/// Dependencies: RpgPromptDefaultsProvider, Unity JsonUtility.
 /// Responsibility: load/save pawn dialogue prompt custom overrides from Prompt/Custom/PawnDialoguePrompt_Custom.json.
 ///</summary>
    internal static class RpgPromptCustomStore
    {
        private const string PromptFolderName = "Prompt";
        private const string CustomSubFolderName = "Custom";
        private const string CustomConfigFileName = "PawnDialoguePrompt_Custom.json";
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

        public static void DeleteCustomConfig()
        {
            string path = GetCustomConfigPath();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static RpgPromptCustomConfig BuildDefaultConfig(RpgPromptDefaultsConfig defaults)
        {
            return new RpgPromptCustomConfig
            {
                RoleSetting = defaults?.RoleSetting ?? string.Empty,
                DialogueStyle = defaults?.DialogueStyle ?? string.Empty,
                FormatConstraint = defaults?.FormatConstraint ?? string.Empty,
                RoleSettingFallbackTemplate = defaults?.RoleSettingFallbackTemplate ?? string.Empty,
                FormatConstraintHeader = defaults?.FormatConstraintHeader ?? string.Empty,
                CompactFormatFallback = defaults?.CompactFormatFallback ?? string.Empty,
                ActionReliabilityFallback = defaults?.ActionReliabilityFallback ?? string.Empty,
                ActionReliabilityMarker = defaults?.ActionReliabilityMarker ?? string.Empty,
                RpgRoleSettingTemplate = defaults?.RpgRoleSettingTemplate ?? string.Empty,
                RpgCompactFormatConstraintTemplate = defaults?.RpgCompactFormatConstraintTemplate ?? string.Empty,
                RpgActionReliabilityRuleTemplate = defaults?.RpgActionReliabilityRuleTemplate ?? string.Empty,
                DecisionPolicyTemplate = defaults?.DecisionPolicyTemplate ?? string.Empty,
                TurnObjectiveTemplate = defaults?.TurnObjectiveTemplate ?? string.Empty,
                OpeningObjectiveTemplate = defaults?.OpeningObjectiveTemplate ?? string.Empty,
                TopicShiftRuleTemplate = defaults?.TopicShiftRuleTemplate ?? string.Empty,
                PersonaBootstrapSystemPrompt = defaults?.PersonaBootstrapSystemPrompt ?? string.Empty,
                PersonaBootstrapUserPromptTemplate = defaults?.PersonaBootstrapUserPromptTemplate ?? string.Empty,
                PersonaBootstrapOutputTemplate = defaults?.PersonaBootstrapOutputTemplate ?? string.Empty,
                PersonaBootstrapExample = defaults?.PersonaBootstrapExample ?? string.Empty,
                ApiActionPrompt = defaults?.ApiActionPrompt?.Clone() ?? RpgApiActionPromptConfig.CreateFallback(),
                EnableRimTalkPromptCompat = defaults?.EnableRimTalkPromptCompat ?? true,
                RimTalkSummaryHistoryLimit = defaults?.RimTalkSummaryHistoryLimit ?? 10,
                RimTalkPresetInjectionMaxEntries = defaults?.RimTalkPresetInjectionMaxEntries ?? RimChatSettings.RimTalkPresetInjectionLimitUnlimited,
                RimTalkPresetInjectionMaxChars = defaults?.RimTalkPresetInjectionMaxChars ?? RimChatSettings.RimTalkPresetInjectionLimitUnlimited,
                RimTalkCompatTemplate = defaults?.RimTalkCompatTemplate ?? RimChatSettings.DefaultRimTalkCompatTemplate,
                RimTalkPersonaCopyTemplate = defaults?.RimTalkPersonaCopyTemplate ?? RimChatSettings.DefaultRimTalkPersonaCopyTemplate,
                RimTalkDiplomacy = defaults?.RimTalkDiplomacy?.Clone() ?? RimTalkChannelCompatConfig.CreateDefault(),
                RimTalkRpg = defaults?.RimTalkRpg?.Clone() ?? RimTalkChannelCompatConfig.CreateDefault(),
                RimTalkChannelSplitMigrated = defaults?.RimTalkChannelSplitMigrated ?? true
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

            if (custom.RpgRoleSettingTemplate != null)
            {
                target.RpgRoleSettingTemplate = custom.RpgRoleSettingTemplate;
            }

            if (custom.RpgCompactFormatConstraintTemplate != null)
            {
                target.RpgCompactFormatConstraintTemplate = custom.RpgCompactFormatConstraintTemplate;
            }

            if (custom.RpgActionReliabilityRuleTemplate != null)
            {
                target.RpgActionReliabilityRuleTemplate = custom.RpgActionReliabilityRuleTemplate;
            }

            if (custom.DecisionPolicyTemplate != null)
            {
                target.DecisionPolicyTemplate = custom.DecisionPolicyTemplate;
            }

            if (custom.TurnObjectiveTemplate != null)
            {
                target.TurnObjectiveTemplate = custom.TurnObjectiveTemplate;
            }

            if (custom.OpeningObjectiveTemplate != null)
            {
                target.OpeningObjectiveTemplate = custom.OpeningObjectiveTemplate;
            }

            if (custom.TopicShiftRuleTemplate != null)
            {
                target.TopicShiftRuleTemplate = custom.TopicShiftRuleTemplate;
            }

            if (custom.PersonaBootstrapSystemPrompt != null)
            {
                target.PersonaBootstrapSystemPrompt = custom.PersonaBootstrapSystemPrompt;
            }

            if (custom.PersonaBootstrapUserPromptTemplate != null)
            {
                target.PersonaBootstrapUserPromptTemplate = custom.PersonaBootstrapUserPromptTemplate;
            }

            if (custom.PersonaBootstrapOutputTemplate != null)
            {
                target.PersonaBootstrapOutputTemplate = custom.PersonaBootstrapOutputTemplate;
            }

            if (custom.PersonaBootstrapExample != null)
            {
                target.PersonaBootstrapExample = custom.PersonaBootstrapExample;
            }

            if (custom.RimTalkPersonaCopyTemplate != null)
            {
                target.RimTalkPersonaCopyTemplate = custom.RimTalkPersonaCopyTemplate;
            }

            MergeApiActionPrompt(target.ApiActionPrompt, custom.ApiActionPrompt);

            bool hasChannelPayload =
                custom.RimTalkDiplomacy != null ||
                custom.RimTalkRpg != null ||
                custom.RimTalkChannelSplitMigrated;

            if (custom.RimTalkSummaryHistoryLimit != 0)
            {
                target.RimTalkSummaryHistoryLimit = custom.RimTalkSummaryHistoryLimit;
            }

            if (hasChannelPayload)
            {
                if (custom.RimTalkDiplomacy != null)
                {
                    target.RimTalkDiplomacy = custom.RimTalkDiplomacy.Clone();
                }

                if (custom.RimTalkRpg != null)
                {
                    target.RimTalkRpg = custom.RimTalkRpg.Clone();
                }

                target.RimTalkDiplomacy ??= BuildLegacyRimTalkChannelConfig(custom, target.RimTalkDiplomacy);
                target.RimTalkRpg ??= BuildLegacyRimTalkChannelConfig(custom, target.RimTalkRpg);
                target.RimTalkDiplomacy.NormalizeWith(RimTalkChannelCompatConfig.CreateDefault());
                target.RimTalkRpg.NormalizeWith(RimTalkChannelCompatConfig.CreateDefault());
                target.RimTalkChannelSplitMigrated = custom.RimTalkChannelSplitMigrated || target.RimTalkChannelSplitMigrated;
                SyncLegacyRimTalkFieldsFromRpgChannel(target);
                return;
            }

            bool hasLegacyPayload =
                custom.RimTalkCompatTemplate != null ||
                custom.RimTalkSummaryHistoryLimit != 0 ||
                custom.RimTalkPresetInjectionMaxEntries != RimChatSettings.RimTalkPresetInjectionLimitUnlimited ||
                custom.RimTalkPresetInjectionMaxChars != RimChatSettings.RimTalkPresetInjectionLimitUnlimited;
            if (!hasLegacyPayload)
            {
                return;
            }

            target.EnableRimTalkPromptCompat = custom.EnableRimTalkPromptCompat;
            if (custom.RimTalkCompatTemplate != null)
            {
                target.RimTalkCompatTemplate = custom.RimTalkCompatTemplate;
            }

            target.RimTalkPresetInjectionMaxEntries = custom.RimTalkPresetInjectionMaxEntries;
            target.RimTalkPresetInjectionMaxChars = custom.RimTalkPresetInjectionMaxChars;
            RimTalkChannelCompatConfig legacy = BuildLegacyRimTalkChannelConfig(custom, target.RimTalkRpg);
            target.RimTalkDiplomacy = legacy.Clone();
            target.RimTalkRpg = legacy.Clone();
            target.RimTalkChannelSplitMigrated = true;
            SyncLegacyRimTalkFieldsFromRpgChannel(target);
        }

        private static RimTalkChannelCompatConfig BuildLegacyRimTalkChannelConfig(
            RpgPromptCustomConfig source,
            RimTalkChannelCompatConfig fallback)
        {
            RimTalkChannelCompatConfig config = fallback?.Clone() ?? RimTalkChannelCompatConfig.CreateDefault();
            if (source == null)
            {
                return config;
            }

            config.EnablePromptCompat = source.EnableRimTalkPromptCompat;
            config.PresetInjectionMaxEntries = source.RimTalkPresetInjectionMaxEntries;
            config.PresetInjectionMaxChars = source.RimTalkPresetInjectionMaxChars;
            if (source.RimTalkCompatTemplate != null)
            {
                config.CompatTemplate = source.RimTalkCompatTemplate;
            }

            config.NormalizeWith(RimTalkChannelCompatConfig.CreateDefault());
            return config;
        }

        private static void SyncLegacyRimTalkFieldsFromRpgChannel(RpgPromptCustomConfig target)
        {
            if (target == null)
            {
                return;
            }

            RimTalkChannelCompatConfig rpg = target.RimTalkRpg ?? target.RimTalkDiplomacy ?? RimTalkChannelCompatConfig.CreateDefault();
            target.EnableRimTalkPromptCompat = rpg.EnablePromptCompat;
            target.RimTalkPresetInjectionMaxEntries = rpg.PresetInjectionMaxEntries;
            target.RimTalkPresetInjectionMaxChars = rpg.PresetInjectionMaxChars;
            target.RimTalkCompatTemplate = rpg.CompatTemplate ?? RimChatSettings.DefaultRimTalkCompatTemplate;
            target.RimTalkPersonaCopyTemplate = string.IsNullOrWhiteSpace(target.RimTalkPersonaCopyTemplate)
                ? RimChatSettings.DefaultRimTalkPersonaCopyTemplate
                : target.RimTalkPersonaCopyTemplate;
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
