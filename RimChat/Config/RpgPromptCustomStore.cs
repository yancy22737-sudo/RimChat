using System;
using System.Collections.Generic;
using System.IO;
using RimChat.Core;
using RimChat.Prompting;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>/// Dependencies: JSON file I/O, RimWorld mod path APIs.
 /// Responsibility: persist pawn dialogue prompt overrides and prompt section catalog state under Prompt/Custom only.
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
        public string RelationshipProfileTemplate;
        public string KinshipBoundaryRuleTemplate;
        public string PersonaBootstrapSystemPrompt;
        public string PersonaBootstrapUserPromptTemplate;
        public string PersonaBootstrapOutputTemplate;
        public string PersonaBootstrapExample;
        public RpgApiActionPromptConfig ApiActionPrompt;
        public int RimTalkSummaryHistoryLimit;
        public string RimTalkPersonaCopyTemplate;
        public bool RimTalkAutoPushSessionSummary;
        public bool RimTalkAutoInjectCompatPreset;
        public RimTalkPromptEntryDefaultsConfig PromptSectionCatalog;
    }

    /// <summary>/// Dependencies: RpgPromptDefaultsProvider, Unity JsonUtility.
 /// Responsibility: load/save pawn dialogue prompt custom overrides from Prompt/Custom/PawnDialoguePrompt_Custom.json.
 ///</summary>
    internal static class RpgPromptCustomStore
    {
        private const string PromptFolderName = "Prompt";
        private const string CustomSubFolderName = "Custom";
        private const string CustomConfigFileName = "PawnDialoguePrompt_Custom.json";
        private static readonly object CacheLock = new object();
        private static string loggedCustomPath = string.Empty;
        private static string loggedPayloadSignature = string.Empty;
        private static RpgPromptCustomConfig cachedConfig;
        private static DateTime cachedWriteTimeUtc = DateTime.MinValue;
        private static bool cachedConfigExists;

        public static RpgPromptCustomConfig LoadOrDefault()
        {
            string path = GetCustomConfigPath();
            bool exists = File.Exists(path);
            DateTime writeTimeUtc = exists ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            lock (CacheLock)
            {
                if (cachedConfig != null &&
                    cachedConfigExists == exists &&
                    cachedWriteTimeUtc == writeTimeUtc)
                {
                    return cachedConfig;
                }

                RpgPromptDefaultsConfig defaults = RpgPromptDefaultsProvider.GetDefaults();
                RpgPromptCustomConfig config = BuildDefaultConfig(defaults);
                if (exists)
                {
                    try
                    {
                        string json = File.ReadAllText(path);
                        RpgPromptCustomConfig custom = JsonUtility.FromJson<RpgPromptCustomConfig>(json);
                        MergeCustomIntoBase(config, custom);
                        config.PromptSectionCatalog = PromptLegacyCompatMigration.ApplyLegacyPayloadToPromptSections(
                            config.PromptSectionCatalog,
                            json,
                            "custom_store");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[RimChat] Failed to load RPG custom prompts from {path}: {ex.Message}");
                    }
                }

                ApplyPromptTemplateRewrite(config);
                cachedConfig = config;
                cachedWriteTimeUtc = writeTimeUtc;
                cachedConfigExists = exists;
                LogResolvedCustomPayload(config, exists);
                return cachedConfig;
            }
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
            lock (CacheLock)
            {
                cachedConfig = CloneConfig(config);
                cachedWriteTimeUtc = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                cachedConfigExists = File.Exists(path);
            }
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

            lock (CacheLock)
            {
                cachedConfig = null;
                cachedWriteTimeUtc = DateTime.MinValue;
                cachedConfigExists = false;
                loggedPayloadSignature = string.Empty;
            }
        }

        private static RpgPromptCustomConfig CloneConfig(RpgPromptCustomConfig source)
        {
            if (source == null)
            {
                return null;
            }

            string json = JsonUtility.ToJson(source);
            return JsonUtility.FromJson<RpgPromptCustomConfig>(json);
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
                RelationshipProfileTemplate = defaults?.RelationshipProfileTemplate ?? string.Empty,
                KinshipBoundaryRuleTemplate = defaults?.KinshipBoundaryRuleTemplate ?? string.Empty,
                PersonaBootstrapSystemPrompt = defaults?.PersonaBootstrapSystemPrompt ?? string.Empty,
                PersonaBootstrapUserPromptTemplate = defaults?.PersonaBootstrapUserPromptTemplate ?? string.Empty,
                PersonaBootstrapOutputTemplate = defaults?.PersonaBootstrapOutputTemplate ?? string.Empty,
                PersonaBootstrapExample = defaults?.PersonaBootstrapExample ?? string.Empty,
                ApiActionPrompt = defaults?.ApiActionPrompt?.Clone() ?? RpgApiActionPromptConfig.CreateFallback(),
                RimTalkSummaryHistoryLimit = defaults?.RimTalkSummaryHistoryLimit ?? 10,
                RimTalkPersonaCopyTemplate = defaults?.RimTalkPersonaCopyTemplate ?? RimChatSettings.DefaultRimTalkPersonaCopyTemplate,
                RimTalkAutoPushSessionSummary = defaults?.RimTalkAutoPushSessionSummary ?? false,
                RimTalkAutoInjectCompatPreset = defaults?.RimTalkAutoInjectCompatPreset ?? false,
                PromptSectionCatalog = RimTalkPromptEntryDefaultsProvider.GetDefaultsSnapshot()
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

            if (custom.RelationshipProfileTemplate != null)
            {
                target.RelationshipProfileTemplate = custom.RelationshipProfileTemplate;
            }

            if (custom.KinshipBoundaryRuleTemplate != null)
            {
                target.KinshipBoundaryRuleTemplate = custom.KinshipBoundaryRuleTemplate;
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
            if (custom.PromptSectionCatalog != null)
            {
                target.PromptSectionCatalog = PromptLegacyCompatMigration.NormalizePromptSections(custom.PromptSectionCatalog);
            }
            target.RimTalkAutoPushSessionSummary = custom.RimTalkAutoPushSessionSummary;
            target.RimTalkAutoInjectCompatPreset = custom.RimTalkAutoInjectCompatPreset;

            MergeApiActionPrompt(target.ApiActionPrompt, custom.ApiActionPrompt);

            if (custom.RimTalkSummaryHistoryLimit != 0)
            {
                target.RimTalkSummaryHistoryLimit = custom.RimTalkSummaryHistoryLimit;
            }
        }

        private static void SyncLegacyRimTalkFieldsFromRpgChannel(RpgPromptCustomConfig target)
        {
            target.PromptSectionCatalog = PromptLegacyCompatMigration.NormalizePromptSections(target.PromptSectionCatalog);
            if (target != null && string.IsNullOrWhiteSpace(target.RimTalkPersonaCopyTemplate))
            {
                target.RimTalkPersonaCopyTemplate = RimChatSettings.DefaultRimTalkPersonaCopyTemplate;
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

            string fullHeader = config.ApiActionPrompt?.FullHeader ?? string.Empty;
            string compactHeader = config.ApiActionPrompt?.CompactHeader ?? string.Empty;
            string reliability = config.ActionReliabilityFallback ?? string.Empty;
            string tryGainMemory = config.ApiActionPrompt?.FullTryGainMemoryLineTemplate ?? string.Empty;
            string signature = string.Concat(
                exists ? "1" : "0",
                "|", fullHeader.Length.ToString(),
                "|", compactHeader.Length.ToString(),
                "|", reliability.Length.ToString(),
                "|", tryGainMemory.Length.ToString(),
                "|", ComputeStableHash(fullHeader).ToString(),
                "|", ComputeStableHash(compactHeader).ToString(),
                "|", ComputeStableHash(reliability).ToString(),
                "|", ComputeStableHash(tryGainMemory).ToString());
            if (string.Equals(signature, loggedPayloadSignature, StringComparison.Ordinal))
            {
                return;
            }

            loggedPayloadSignature = signature;
            Log.Message(
                $"[RimChat] RPG custom prompt payload updated (exists={exists}): FullHeaderLen={fullHeader.Length}, CompactHeaderLen={compactHeader.Length}, ActionReliabilityLen={reliability.Length}, FullTryGainMemoryTemplateLen={tryGainMemory.Length}");
        }

        private static int ComputeStableHash(string text)
        {
            unchecked
            {
                const int fnvOffset = unchecked((int)2166136261);
                const int fnvPrime = 16777619;
                int hash = fnvOffset;
                string source = text ?? string.Empty;
                for (int i = 0; i < source.Length; i++)
                {
                    hash ^= source[i];
                    hash *= fnvPrime;
                }

                return hash;
            }
        }

        private static void ApplyPromptTemplateRewrite(RpgPromptCustomConfig config)
        {
            if (config == null)
            {
                return;
            }

            SyncLegacyRimTalkFieldsFromRpgChannel(config);
        }
    }
}
