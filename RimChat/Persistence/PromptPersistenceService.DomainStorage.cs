using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimChat.Config;
using RimChat.Core;
using Verse;

namespace RimChat.Persistence
{
    internal enum PromptBundleImportFailure
    {
        None = 0,
        EmptyPath,
        FileNotFound,
        EmptyFile,
        InvalidJson,
        PresetFileDetected,
        NotPromptBundle,
        InvalidBundlePayload,
        NoModuleOverlap,
        UnexpectedException
    }

    internal static class PromptBundleImportErrorCodes
    {
        public const string EmptyPath = "PBIMP_001_EMPTY_PATH";
        public const string FileNotFound = "PBIMP_002_FILE_NOT_FOUND";
        public const string EmptyFile = "PBIMP_003_EMPTY_FILE";
        public const string InvalidJson = "PBIMP_004_INVALID_JSON";
        public const string PresetFileDetected = "PBIMP_005_PRESET_FILE";
        public const string NotPromptBundle = "PBIMP_006_NOT_BUNDLE";
        public const string InvalidBundlePayload = "PBIMP_007_INVALID_BUNDLE_PAYLOAD";
        public const string NoModuleOverlap = "PBIMP_008_NO_MODULE_OVERLAP";
        public const string UnexpectedException = "PBIMP_999_UNEXPECTED";
    }

    public partial class PromptPersistenceService
    {
        private static readonly string[] PromptBundlePayloadMarkers =
        {
            "SystemPrompt",
            "SystemPromptJson",
            "DiplomacyDialoguePrompt",
            "DiplomacyDialoguePromptJson",
            "PawnDialoguePrompt",
            "PawnDialoguePromptJson",
            "SocialCirclePrompt",
            "SocialCirclePromptJson",
            "FactionPromptsJson",
            "PromptSectionCatalog",
            "PromptSectionCatalogJson",
            "UnifiedPromptCatalog",
            "UnifiedPromptCatalogJson"
        };

        private static readonly string[] PromptPresetFeatureKeys =
        {
            "Presets",
            "ChannelPayloads",
            "UnifiedPromptCatalog"
        };

        private static readonly string[] CustomPromptDomainFiles =
        {
            PromptDomainFileCatalog.SystemPromptCustomFileName,
            PromptDomainFileCatalog.DiplomacyPromptCustomFileName,
            PromptDomainFileCatalog.PawnPromptCustomFileName,
            PromptDomainFileCatalog.SocialCirclePromptCustomFileName
        };

        private bool TryLoadPromptDomains(out SystemPromptConfig config)
        {
            return TryLoadPromptDomains(
                includeCustom: true,
                out config,
                out _,
                out _);
        }

        private bool TryLoadPromptDomains(
            bool includeCustom,
            out SystemPromptConfig config,
            out int loadedDomainSchemaVersion,
            out List<string> validationErrors)
        {
            SystemPromptDomainConfig systemPrompt = LoadSystemPromptDomain(includeCustom);
            DiplomacyDialoguePromptDomainConfig diplomacyPrompt = LoadDiplomacyPromptDomain(includeCustom);
            SocialCirclePromptDomainConfig socialPrompt = LoadSocialCirclePromptDomain(includeCustom);
            RpgPromptCustomConfig pawnPrompt = includeCustom
                ? RpgPromptCustomStore.LoadOrDefault()
                : RpgPromptCustomStore.LoadDefaultsOnly();
            loadedDomainSchemaVersion = systemPrompt?.PromptDomainSchemaVersion ?? 0;
            config = ComposeConfigFromDomains(systemPrompt, diplomacyPrompt, pawnPrompt, socialPrompt);
            validationErrors = ValidateDomainConfigSemantics(config);

            if (!includeCustom && validationErrors.Count > 0)
            {
                if (TryRehydrateFromAggregateDomainJson(includeCustom: false, out SystemPromptConfig reparsedConfig, out List<string> reparsedErrors))
                {
                    config = reparsedConfig;
                    validationErrors = reparsedErrors;
                }
            }

            return validationErrors.Count == 0;
        }

        private bool TryRehydrateFromAggregateDomainJson(
            bool includeCustom,
            out SystemPromptConfig config,
            out List<string> validationErrors)
        {
            config = null;
            validationErrors = new List<string>();
            string aggregateJson = BuildAggregateConfigJsonFromDomainFiles(includeCustom);
            if (string.IsNullOrWhiteSpace(aggregateJson))
            {
                return false;
            }

            config = ParseJsonToConfigInternal(
                aggregateJson,
                includeCustom ? "aggregate_domains_custom" : "aggregate_domains_default_only");
            validationErrors = ValidateDomainConfigSemantics(config);
            return validationErrors.Count == 0;
        }

        private string BuildAggregateConfigJsonFromDomainFiles(bool includeCustom)
        {
            string systemDefault = ReadDomainJson(PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.SystemPromptDefaultFileName));
            string diplomacyDefault = ReadDomainJson(PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.DiplomacyPromptDefaultFileName));
            string socialDefault = ReadDomainJson(PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.SocialCirclePromptDefaultFileName));
            if (string.IsNullOrWhiteSpace(systemDefault) || string.IsNullOrWhiteSpace(diplomacyDefault) || string.IsNullOrWhiteSpace(socialDefault))
            {
                return string.Empty;
            }

            string systemCustom = includeCustom
                ? ReadDomainJson(PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SystemPromptCustomFileName))
                : string.Empty;
            string diplomacyCustom = includeCustom
                ? ReadDomainJson(PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.DiplomacyPromptCustomFileName))
                : string.Empty;
            string socialCustom = includeCustom
                ? ReadDomainJson(PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SocialCirclePromptCustomFileName))
                : string.Empty;
            RpgPromptCustomConfig pawnPrompt = includeCustom
                ? RpgPromptCustomStore.LoadOrDefault()
                : RpgPromptCustomStore.LoadDefaultsOnly();

            string configName = SelectStringField(systemCustom, systemDefault, "ConfigName", "Default");
            string globalSystemPrompt = SelectStringField(systemCustom, systemDefault, "GlobalSystemPrompt", string.Empty);
            string globalDialoguePrompt = SelectStringField(diplomacyCustom, diplomacyDefault, "GlobalDialoguePrompt", string.Empty);
            string useAdvancedMode = SelectValueField(systemCustom, systemDefault, "UseAdvancedMode", "false");
            string useHierarchical = SelectValueField(systemCustom, systemDefault, "UseHierarchicalPromptFormat", "true");
            string enabled = SelectValueField(systemCustom, systemDefault, "Enabled", "true");
            string promptDomainSchemaVersion = SelectValueField(systemCustom, systemDefault, "PromptDomainSchemaVersion", CurrentPromptDomainSchemaVersion.ToString());
            string promptSchemaVersion = SelectValueField(systemCustom, systemDefault, "PromptSchemaVersion", SystemPromptConfig.CurrentPromptSchemaVersion.ToString());
            string schemaVersion = SelectValueField(systemCustom, systemDefault, "PromptPolicySchemaVersion", SystemPromptConfig.CurrentPromptPolicySchemaVersion.ToString());
            string apiActions = SelectArraySection(diplomacyCustom, diplomacyDefault, "ApiActions", "[]");
            string responseFormat = SelectObjectSection(diplomacyCustom, diplomacyDefault, "ResponseFormat", "{}");
            string decisionRules = SelectArraySection(diplomacyCustom, diplomacyDefault, "DecisionRules", "[]");
            string environmentPrompt = SelectObjectSection(systemCustom, systemDefault, "EnvironmentPrompt", "{}");
            string promptTemplates = BuildPromptTemplatesJson(diplomacyCustom, diplomacyDefault, socialCustom, socialDefault, pawnPrompt);
            string promptPolicy = SelectObjectSection(systemCustom, systemDefault, "PromptPolicy", "{}");
            string dynamicData = SelectObjectSection(systemCustom, systemDefault, "DynamicDataInjection", "{}");

            return "{"
                + $"\"ConfigName\":\"{EscapeJson(configName)}\","
                + $"\"GlobalSystemPrompt\":\"{EscapeJson(globalSystemPrompt)}\","
                + $"\"GlobalDialoguePrompt\":\"{EscapeJson(globalDialoguePrompt)}\","
                + $"\"UseAdvancedMode\":{useAdvancedMode},"
                + $"\"UseHierarchicalPromptFormat\":{useHierarchical},"
                + $"\"PromptDomainSchemaVersion\":{promptDomainSchemaVersion},"
                + $"\"PromptSchemaVersion\":{promptSchemaVersion},"
                + $"\"PromptPolicySchemaVersion\":{schemaVersion},"
                + $"\"Enabled\":{enabled},"
                + $"\"ApiActions\":{apiActions},"
                + $"\"ResponseFormat\":{responseFormat},"
                + $"\"DecisionRules\":{decisionRules},"
                + $"\"EnvironmentPrompt\":{environmentPrompt},"
                + $"\"PromptTemplates\":{promptTemplates},"
                + $"\"PromptPolicy\":{promptPolicy},"
                + $"\"DynamicDataInjection\":{dynamicData}"
                + "}";
        }

        private static string ReadDomainJson(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return string.Empty;
            }

            return File.ReadAllText(path);
        }

        private string SelectStringField(string customJson, string defaultJson, string key, string fallback)
        {
            if (ContainsJsonKey(customJson, key))
            {
                return ExtractString(customJson, key);
            }

            if (ContainsJsonKey(defaultJson, key))
            {
                return ExtractString(defaultJson, key);
            }

            return fallback ?? string.Empty;
        }

        private string SelectValueField(string customJson, string defaultJson, string key, string fallback)
        {
            if (ContainsJsonKey(customJson, key))
            {
                return ExtractValue(customJson, key);
            }

            if (ContainsJsonKey(defaultJson, key))
            {
                return ExtractValue(defaultJson, key);
            }

            return fallback;
        }

        private string SelectObjectSection(string customJson, string defaultJson, string key, string fallback)
        {
            if (TryExtractJsonObject(customJson, key, out string customSection))
            {
                return "{" + customSection + "}";
            }

            if (TryExtractJsonObject(defaultJson, key, out string defaultSection))
            {
                return "{" + defaultSection + "}";
            }

            return fallback;
        }

        private string SelectArraySection(string customJson, string defaultJson, string key, string fallback)
        {
            if (TryExtractJsonArray(customJson, key, out string customSection))
            {
                return "[" + customSection + "]";
            }

            if (TryExtractJsonArray(defaultJson, key, out string defaultSection))
            {
                return "[" + defaultSection + "]";
            }

            return fallback;
        }

        private string BuildPromptTemplatesJson(
            string diplomacyCustom,
            string diplomacyDefault,
            string socialCustom,
            string socialDefault,
            RpgPromptCustomConfig pawnPrompt)
        {
            string enabled = SelectValueField(diplomacyCustom, diplomacyDefault, "PromptTemplatesEnabled", "true");
            string factGrounding = SelectStringField(diplomacyCustom, diplomacyDefault, "FactGroundingTemplate", string.Empty);
            string outputLanguage = SelectStringField(diplomacyCustom, diplomacyDefault, "OutputLanguageTemplate", string.Empty);
            string diplomacyFallback = SelectStringField(diplomacyCustom, diplomacyDefault, "DiplomacyFallbackRoleTemplate", string.Empty);
            string socialCircle = SelectStringField(socialCustom, socialDefault, "SocialCircleActionRuleTemplate", string.Empty);
            string socialNewsStyle = SelectStringField(socialCustom, socialDefault, "SocialCircleNewsStyleTemplate", string.Empty);
            string socialNewsContract = SelectStringField(socialCustom, socialDefault, "SocialCircleNewsJsonContractTemplate", string.Empty);
            string socialNewsFact = SelectStringField(socialCustom, socialDefault, "SocialCircleNewsFactTemplate", string.Empty);
            string decisionPolicy = SelectStringField(diplomacyCustom, diplomacyDefault, "DecisionPolicyTemplate", pawnPrompt?.DecisionPolicyTemplate ?? string.Empty);
            string turnObjective = SelectStringField(diplomacyCustom, diplomacyDefault, "TurnObjectiveTemplate", pawnPrompt?.TurnObjectiveTemplate ?? string.Empty);
            string openingObjective = pawnPrompt?.OpeningObjectiveTemplate ?? string.Empty;
            string topicShift = SelectStringField(diplomacyCustom, diplomacyDefault, "TopicShiftRuleTemplate", pawnPrompt?.TopicShiftRuleTemplate ?? string.Empty);
            string apiLimits = SelectStringField(diplomacyCustom, diplomacyDefault, "ApiLimitsNodeTemplate", PromptTextConstants.ApiLimitsNodeLiteralDefault);
            string questGuidance = SelectStringField(diplomacyCustom, diplomacyDefault, "QuestGuidanceNodeTemplate", PromptTextConstants.QuestGuidanceNodeLiteralDefault);
            string responseContract = SelectStringField(diplomacyCustom, diplomacyDefault, "ResponseContractNodeTemplate", PromptTextConstants.ResponseContractNodeLiteralDefault);
            string mandatoryRaceInjection = SelectStringField(diplomacyCustom, diplomacyDefault, "MandatoryRaceInjectionTemplate", string.Empty);

            return "{"
                + $"\"Enabled\":{enabled},"
                + $"\"FactGroundingTemplate\":\"{EscapeJson(factGrounding)}\","
                + $"\"OutputLanguageTemplate\":\"{EscapeJson(outputLanguage)}\","
                + $"\"DiplomacyFallbackRoleTemplate\":\"{EscapeJson(diplomacyFallback)}\","
                + $"\"SocialCircleActionRuleTemplate\":\"{EscapeJson(socialCircle)}\","
                + $"\"SocialCircleNewsStyleTemplate\":\"{EscapeJson(socialNewsStyle)}\","
                + $"\"SocialCircleNewsJsonContractTemplate\":\"{EscapeJson(socialNewsContract)}\","
                + $"\"SocialCircleNewsFactTemplate\":\"{EscapeJson(socialNewsFact)}\","
                + $"\"RpgRoleSettingTemplate\":\"{EscapeJson(pawnPrompt?.RpgRoleSettingTemplate ?? string.Empty)}\","
                + $"\"RpgCompactFormatConstraintTemplate\":\"{EscapeJson(pawnPrompt?.RpgCompactFormatConstraintTemplate ?? string.Empty)}\","
                + $"\"RpgActionReliabilityRuleTemplate\":\"{EscapeJson(pawnPrompt?.RpgActionReliabilityRuleTemplate ?? string.Empty)}\","
                + $"\"DecisionPolicyTemplate\":\"{EscapeJson(decisionPolicy)}\","
                + $"\"TurnObjectiveTemplate\":\"{EscapeJson(turnObjective)}\","
                + $"\"OpeningObjectiveTemplate\":\"{EscapeJson(openingObjective)}\","
                + $"\"TopicShiftRuleTemplate\":\"{EscapeJson(topicShift)}\","
                + $"\"ApiLimitsNodeTemplate\":\"{EscapeJson(apiLimits)}\","
                + $"\"QuestGuidanceNodeTemplate\":\"{EscapeJson(questGuidance)}\","
                + $"\"ResponseContractNodeTemplate\":\"{EscapeJson(responseContract)}\","
                + $"\"MandatoryRaceInjectionTemplate\":\"{EscapeJson(mandatoryRaceInjection)}\""
                + "}";
        }

        private static void ApplyPawnPromptTemplates(SystemPromptConfig config, RpgPromptCustomConfig pawnPrompt)
        {
            if (config?.PromptTemplates == null || pawnPrompt == null)
            {
                return;
            }

            config.PromptTemplates.RpgRoleSettingTemplate = pawnPrompt.RpgRoleSettingTemplate ?? string.Empty;
            config.PromptTemplates.RpgCompactFormatConstraintTemplate = pawnPrompt.RpgCompactFormatConstraintTemplate ?? string.Empty;
            config.PromptTemplates.RpgActionReliabilityRuleTemplate = pawnPrompt.RpgActionReliabilityRuleTemplate ?? string.Empty;
            config.PromptTemplates.OpeningObjectiveTemplate = pawnPrompt.OpeningObjectiveTemplate ?? string.Empty;
        }

        private SystemPromptConfig ComposeConfigFromDomains(
            SystemPromptDomainConfig systemPrompt,
            DiplomacyDialoguePromptDomainConfig diplomacyPrompt,
            RpgPromptCustomConfig pawnPrompt,
            SocialCirclePromptDomainConfig socialPrompt)
        {
            var config = new SystemPromptConfig
            {
                ConfigName = systemPrompt?.ConfigName ?? "Default",
                GlobalSystemPrompt = systemPrompt?.GlobalSystemPrompt ?? string.Empty,
                GlobalDialoguePrompt = diplomacyPrompt?.GlobalDialoguePrompt ?? string.Empty,
                UseAdvancedMode = systemPrompt?.UseAdvancedMode ?? false,
                UseHierarchicalPromptFormat = systemPrompt?.UseHierarchicalPromptFormat ?? true,
                Enabled = systemPrompt?.Enabled ?? true,
                PromptSchemaVersion = systemPrompt?.PromptSchemaVersion ?? SystemPromptConfig.CurrentPromptSchemaVersion,
                PromptPolicySchemaVersion = systemPrompt?.PromptPolicySchemaVersion ?? SystemPromptConfig.CurrentPromptPolicySchemaVersion,
                ResponseFormat = diplomacyPrompt?.ResponseFormat?.Clone() ?? new ResponseFormatConfig(),
                EnvironmentPrompt = systemPrompt?.EnvironmentPrompt?.Clone() ?? new EnvironmentPromptConfig(),
                DynamicDataInjection = systemPrompt?.DynamicDataInjection?.Clone() ?? new DynamicDataInjectionConfig(),
                PromptTemplates = BuildPromptTemplates(diplomacyPrompt, pawnPrompt, socialPrompt),
                PromptPolicy = systemPrompt?.PromptPolicy?.Clone() ?? PromptPolicyConfig.CreateDefault(),
                ApiActions = BuildApiActions(diplomacyPrompt),
                DecisionRules = CloneDecisionRules(diplomacyPrompt?.DecisionRules)
            };

            return config;
        }

        private SystemPromptDomainConfig LoadSystemPromptDomain(bool includeCustom)
        {
            string customPath = includeCustom
                ? PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SystemPromptCustomFileName)
                : string.Empty;
            return PromptDomainJsonUtility.LoadMerged<SystemPromptDomainConfig>(
                PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.SystemPromptDefaultFileName),
                customPath);
        }

        private DiplomacyDialoguePromptDomainConfig LoadDiplomacyPromptDomain(bool includeCustom)
        {
            string customPath = includeCustom
                ? PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.DiplomacyPromptCustomFileName)
                : string.Empty;
            return PromptDomainJsonUtility.LoadMerged<DiplomacyDialoguePromptDomainConfig>(
                PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.DiplomacyPromptDefaultFileName),
                customPath);
        }

        private SocialCirclePromptDomainConfig LoadSocialCirclePromptDomain(bool includeCustom)
        {
            string customPath = includeCustom
                ? PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SocialCirclePromptCustomFileName)
                : string.Empty;
            return PromptDomainJsonUtility.LoadMerged<SocialCirclePromptDomainConfig>(
                PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.SocialCirclePromptDefaultFileName),
                customPath);
        }

        private List<string> ValidateDomainConfigSemantics(SystemPromptConfig config)
        {
            var errors = new List<string>();
            if (config == null)
            {
                errors.Add("ConfigMissing");
                return errors;
            }

            if (string.IsNullOrWhiteSpace(config.GlobalSystemPrompt))
            {
                errors.Add("GlobalSystemPromptMissing");
            }

            var validActions = (config.ApiActions ?? new List<ApiActionConfig>())
                .Where(action => action != null && !string.IsNullOrWhiteSpace(action.ActionName))
                .Select(action => action.ActionName.Trim())
                .ToList();
            if (validActions.Count == 0)
            {
                errors.Add("ApiActionsEmpty");
            }

            HashSet<string> requiredActions = ResolveDiplomacyCoreActionNamesFromDefault();
            if (requiredActions.Count == 0)
            {
                errors.Add("CoreApiActionsDefaultMissing");
            }

            foreach (string required in requiredActions)
            {
                bool exists = validActions.Any(actionName =>
                    string.Equals(actionName, required, StringComparison.OrdinalIgnoreCase));
                if (!exists)
                {
                    errors.Add("MissingApiAction:" + required);
                }
            }

            if (string.IsNullOrWhiteSpace(config.ResponseFormat?.JsonTemplate))
            {
                errors.Add("ResponseFormat.JsonTemplateMissing");
            }

            if (config.PromptTemplates == null)
            {
                errors.Add("PromptTemplatesMissing");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(config.PromptTemplates.ApiLimitsNodeTemplate))
                {
                    errors.Add("PromptTemplates.ApiLimitsNodeTemplateMissing");
                }

                if (string.IsNullOrWhiteSpace(config.PromptTemplates.QuestGuidanceNodeTemplate))
                {
                    errors.Add("PromptTemplates.QuestGuidanceNodeTemplateMissing");
                }

                if (string.IsNullOrWhiteSpace(config.PromptTemplates.ResponseContractNodeTemplate))
                {
                    errors.Add("PromptTemplates.ResponseContractNodeTemplateMissing");
                }
            }

            if (config.PromptPolicy == null)
            {
                errors.Add("PromptPolicyMissing");
            }

            return errors;
        }

        private HashSet<string> ResolveDiplomacyCoreActionNamesFromDefault()
        {
            DiplomacyDialoguePromptDomainConfig defaults = LoadDiplomacyPromptDomain(includeCustom: false);
            var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ApiActionConfig action in defaults?.ApiActions ?? Enumerable.Empty<ApiActionConfig>())
            {
                string name = action?.ActionName?.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    required.Add(name);
                }
            }

            return required;
        }

        private static PromptTemplateTextConfig BuildPromptTemplates(
            DiplomacyDialoguePromptDomainConfig diplomacyPrompt,
            RpgPromptCustomConfig pawnPrompt,
            SocialCirclePromptDomainConfig socialPrompt)
        {
            return new PromptTemplateTextConfig
            {
                Enabled = diplomacyPrompt?.PromptTemplatesEnabled ?? true,
                FactGroundingTemplate = diplomacyPrompt?.FactGroundingTemplate ?? string.Empty,
                OutputLanguageTemplate = diplomacyPrompt?.OutputLanguageTemplate ?? string.Empty,
                DiplomacyFallbackRoleTemplate = diplomacyPrompt?.DiplomacyFallbackRoleTemplate ?? string.Empty,
                SocialCircleActionRuleTemplate = socialPrompt?.SocialCircleActionRuleTemplate ?? string.Empty,
                SocialCircleNewsStyleTemplate = socialPrompt?.SocialCircleNewsStyleTemplate ?? string.Empty,
                SocialCircleNewsJsonContractTemplate = socialPrompt?.SocialCircleNewsJsonContractTemplate ?? string.Empty,
                SocialCircleNewsFactTemplate = socialPrompt?.SocialCircleNewsFactTemplate ?? string.Empty,
                RpgRoleSettingTemplate = pawnPrompt?.RpgRoleSettingTemplate ?? string.Empty,
                RpgCompactFormatConstraintTemplate = pawnPrompt?.RpgCompactFormatConstraintTemplate ?? string.Empty,
                RpgActionReliabilityRuleTemplate = pawnPrompt?.RpgActionReliabilityRuleTemplate ?? string.Empty,
                DecisionPolicyTemplate = !string.IsNullOrWhiteSpace(diplomacyPrompt?.DecisionPolicyTemplate)
                    ? diplomacyPrompt.DecisionPolicyTemplate
                    : pawnPrompt?.DecisionPolicyTemplate ?? string.Empty,
                TurnObjectiveTemplate = !string.IsNullOrWhiteSpace(diplomacyPrompt?.TurnObjectiveTemplate)
                    ? diplomacyPrompt.TurnObjectiveTemplate
                    : pawnPrompt?.TurnObjectiveTemplate ?? string.Empty,
                OpeningObjectiveTemplate = pawnPrompt?.OpeningObjectiveTemplate ?? string.Empty,
                TopicShiftRuleTemplate = !string.IsNullOrWhiteSpace(diplomacyPrompt?.TopicShiftRuleTemplate)
                    ? diplomacyPrompt.TopicShiftRuleTemplate
                    : pawnPrompt?.TopicShiftRuleTemplate ?? string.Empty,
                ApiLimitsNodeTemplate = diplomacyPrompt?.ApiLimitsNodeTemplate ?? PromptTextConstants.ApiLimitsNodeLiteralDefault,
                QuestGuidanceNodeTemplate = diplomacyPrompt?.QuestGuidanceNodeTemplate ?? PromptTextConstants.QuestGuidanceNodeLiteralDefault,
                ResponseContractNodeTemplate = diplomacyPrompt?.ResponseContractNodeTemplate ?? PromptTextConstants.ResponseContractNodeLiteralDefault,
                MandatoryRaceInjectionTemplate = diplomacyPrompt?.MandatoryRaceInjectionTemplate ?? string.Empty
            };
        }

        private static List<ApiActionConfig> BuildApiActions(
            DiplomacyDialoguePromptDomainConfig diplomacyPrompt)
        {
            List<ApiActionConfig> actions = CloneApiActions(diplomacyPrompt?.ApiActions);
            EnsureRequiredRaidVariantActions(actions);
            return actions;
        }

        private static void EnsureRequiredRaidVariantActions(List<ApiActionConfig> actions)
        {
            if (actions == null)
            {
                return;
            }

            EnsureAction(
                actions,
                "request_raid_call_everyone",
                PromptTextConstants.RequestRaidCallEveryoneActionDescription,
                string.Empty,
                PromptTextConstants.RequestRaidCallEveryoneActionRequirement);

            EnsureAction(
                actions,
                "request_raid_waves",
                PromptTextConstants.RequestRaidWavesActionDescription,
                PromptTextConstants.RequestRaidWavesActionParameters,
                PromptTextConstants.RequestRaidWavesActionRequirement);
        }

        private static void EnsureAction(
            List<ApiActionConfig> actions,
            string actionName,
            string description,
            string parameters,
            string requirement)
        {
            ApiActionConfig existing = actions.FirstOrDefault(item =>
                string.Equals(item?.ActionName, actionName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (string.IsNullOrWhiteSpace(existing.Description)) existing.Description = description;
                if (string.IsNullOrWhiteSpace(existing.Parameters)) existing.Parameters = parameters;
                if (string.IsNullOrWhiteSpace(existing.Requirement)) existing.Requirement = requirement;
                return;
            }

            actions.Add(new ApiActionConfig(actionName, description, parameters, requirement));
        }

        private static List<ApiActionConfig> CloneApiActions(IEnumerable<ApiActionConfig> actions)
        {
            return actions?.Where(item => item != null).Select(item => item.Clone()).ToList()
                ?? new List<ApiActionConfig>();
        }

        private static List<DecisionRuleConfig> CloneDecisionRules(IEnumerable<DecisionRuleConfig> rules)
        {
            return rules?.Where(item => item != null).Select(item => item.Clone()).ToList()
                ?? new List<DecisionRuleConfig>();
        }

        private SystemPromptDomainConfig BuildSystemPromptDomain(SystemPromptConfig config)
        {
            return new SystemPromptDomainConfig
            {
                ConfigName = config?.ConfigName ?? "Default",
                GlobalSystemPrompt = config?.GlobalSystemPrompt ?? string.Empty,
                UseAdvancedMode = config?.UseAdvancedMode ?? false,
                UseHierarchicalPromptFormat = config?.UseHierarchicalPromptFormat ?? true,
                Enabled = config?.Enabled ?? true,
                PromptDomainSchemaVersion = CurrentPromptDomainSchemaVersion,
                PromptSchemaVersion = config?.PromptSchemaVersion ?? SystemPromptConfig.CurrentPromptSchemaVersion,
                PromptPolicySchemaVersion = config?.PromptPolicySchemaVersion ?? SystemPromptConfig.CurrentPromptPolicySchemaVersion,
                EnvironmentPrompt = config?.EnvironmentPrompt?.Clone() ?? new EnvironmentPromptConfig(),
                DynamicDataInjection = config?.DynamicDataInjection?.Clone() ?? new DynamicDataInjectionConfig(),
                PromptPolicy = config?.PromptPolicy?.Clone() ?? PromptPolicyConfig.CreateDefault()
            };
        }

        private DiplomacyDialoguePromptDomainConfig BuildDiplomacyPromptDomain(SystemPromptConfig config)
        {
            return new DiplomacyDialoguePromptDomainConfig
            {
                GlobalDialoguePrompt = config?.GlobalDialoguePrompt ?? string.Empty,
                PromptTemplatesEnabled = config?.PromptTemplates?.Enabled ?? true,
                ResponseFormat = config?.ResponseFormat?.Clone() ?? new ResponseFormatConfig(),
                DecisionRules = CloneDecisionRules(config?.DecisionRules),
                ApiActions = CloneDiplomacyActions(config?.ApiActions),
                FactGroundingTemplate = config?.PromptTemplates?.FactGroundingTemplate ?? string.Empty,
                OutputLanguageTemplate = config?.PromptTemplates?.OutputLanguageTemplate ?? string.Empty,
                DiplomacyFallbackRoleTemplate = config?.PromptTemplates?.DiplomacyFallbackRoleTemplate ?? string.Empty,
                DecisionPolicyTemplate = config?.PromptTemplates?.DecisionPolicyTemplate ?? string.Empty,
                TurnObjectiveTemplate = config?.PromptTemplates?.TurnObjectiveTemplate ?? string.Empty,
                TopicShiftRuleTemplate = config?.PromptTemplates?.TopicShiftRuleTemplate ?? string.Empty,
                ApiLimitsNodeTemplate = config?.PromptTemplates?.ApiLimitsNodeTemplate ?? PromptTextConstants.ApiLimitsNodeLiteralDefault,
                QuestGuidanceNodeTemplate = config?.PromptTemplates?.QuestGuidanceNodeTemplate ?? PromptTextConstants.QuestGuidanceNodeLiteralDefault,
                ResponseContractNodeTemplate = config?.PromptTemplates?.ResponseContractNodeTemplate ?? PromptTextConstants.ResponseContractNodeLiteralDefault,
                MandatoryRaceInjectionTemplate = config?.PromptTemplates?.MandatoryRaceInjectionTemplate ?? string.Empty
            };
        }

        private static List<ApiActionConfig> CloneDiplomacyActions(IEnumerable<ApiActionConfig> actions)
        {
            return actions?
                .Where(item => item != null &&
                    !string.Equals(item.ActionName, "publish_public_post", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Clone())
                .ToList()
                ?? new List<ApiActionConfig>();
        }

        private SocialCirclePromptDomainConfig BuildSocialCirclePromptDomain(SystemPromptConfig config)
        {
            return new SocialCirclePromptDomainConfig
            {
                SocialCircleActionRuleTemplate = config?.PromptTemplates?.SocialCircleActionRuleTemplate ?? string.Empty,
                SocialCircleNewsStyleTemplate = config?.PromptTemplates?.SocialCircleNewsStyleTemplate ?? string.Empty,
                SocialCircleNewsJsonContractTemplate = config?.PromptTemplates?.SocialCircleNewsJsonContractTemplate ?? string.Empty,
                SocialCircleNewsFactTemplate = config?.PromptTemplates?.SocialCircleNewsFactTemplate ?? string.Empty,
                PublishPublicPostAction = new ApiActionConfig(
                    "publish_public_post",
                    PromptTextConstants.PublishPublicPostActionDescription,
                    PromptTextConstants.PublishPublicPostActionParameters,
                    PromptTextConstants.PublishPublicPostActionRequirement)
            };
        }

        private void SavePromptDomainFiles(SystemPromptConfig config)
        {
            PromptDomainFileCatalog.EnsureCustomDirectoryExists();
            PromptDomainJsonUtility.WriteToFile(
                PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SystemPromptCustomFileName),
                BuildSystemPromptDomain(config));
            PromptDomainJsonUtility.WriteToFile(
                PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.DiplomacyPromptCustomFileName),
                BuildDiplomacyPromptDomain(config));
            PromptDomainJsonUtility.WriteToFile(
                PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SocialCirclePromptCustomFileName),
                BuildSocialCirclePromptDomain(config));
        }

        private void DeletePromptDomainCustomFiles()
        {
            foreach (string fileName in CustomPromptDomainFiles)
            {
                DeleteCustomPromptFile(fileName);
            }
        }

        private static void DeleteCustomPromptFile(string fileName)
        {
            string path = PromptDomainFileCatalog.GetCustomPath(fileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private bool HasAnyCustomDomainFile()
        {
            return CustomPromptDomainFiles.Any(fileName =>
                File.Exists(PromptDomainFileCatalog.GetCustomPath(fileName)));
        }

        private bool TryGetDomainConfigLastWriteTimeUtc(out DateTime writeTimeUtc)
        {
            writeTimeUtc = DateTime.MinValue;
            bool found = false;
            foreach (string path in GetTrackedPromptPaths())
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                DateTime current = File.GetLastWriteTimeUtc(path);
                if (!found || current > writeTimeUtc)
                {
                    writeTimeUtc = current;
                    found = true;
                }
            }

            return found;
        }

        private IEnumerable<string> GetTrackedPromptPaths()
        {
            yield return PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.SystemPromptDefaultFileName);
            yield return PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.DiplomacyPromptDefaultFileName);
            yield return PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.PawnPromptDefaultFileName);
            yield return PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.SocialCirclePromptDefaultFileName);
            yield return PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.PromptUnifiedDefaultFileName);
            yield return PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SystemPromptCustomFileName);
            yield return PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.DiplomacyPromptCustomFileName);
            yield return PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.PawnPromptCustomFileName);
            yield return PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SocialCirclePromptCustomFileName);
            yield return PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.PromptUnifiedCustomFileName);
        }

        private PromptBundleConfig CreatePromptBundle(SystemPromptConfig config)
        {
            return CreatePromptBundle(config, PromptBundleModuleCatalog.All);
        }

        private PromptBundleConfig CreatePromptBundle(
            SystemPromptConfig config,
            IEnumerable<PromptBundleModule> includedModules)
        {
            HashSet<PromptBundleModule> selected = NormalizeModuleSelection(includedModules, includeAllWhenEmpty: true);
            RimChatSettings settings = RimChatMod.Settings;

            var bundle = new PromptBundleConfig
            {
                BundleVersion = 2,
                IncludedModules = PromptBundleModuleCatalog.ToStorageTokens(selected),
                SystemPrompt = selected.Contains(PromptBundleModule.SystemPrompt)
                    ? BuildSystemPromptDomain(config)
                    : null,
                SystemPromptJson = string.Empty,
                DiplomacyDialoguePrompt = selected.Contains(PromptBundleModule.DiplomacyPrompt)
                    ? BuildDiplomacyPromptDomain(config)
                    : null,
                DiplomacyDialoguePromptJson = string.Empty,
                PawnDialoguePrompt = selected.Contains(PromptBundleModule.RpgPrompt)
                    ? RpgPromptCustomStore.LoadOrDefault()
                    : null,
                PawnDialoguePromptJson = string.Empty,
                SocialCirclePrompt = selected.Contains(PromptBundleModule.SocialCirclePrompt)
                    ? BuildSocialCirclePromptDomain(config)
                    : null,
                SocialCirclePromptJson = string.Empty,
                FactionPromptsJson = selected.Contains(PromptBundleModule.FactionPrompts)
                    ? FactionPromptManager.Instance.ExportConfigsToJson(prettyPrint: true)
                    : string.Empty,
                RimTalkSummaryHistoryLimit = settings?.GetRimTalkSummaryHistoryLimitClamped() ?? 10,
                PromptSectionCatalog = settings?.GetPromptSectionCatalogClone() ?? RimTalkPromptEntryDefaultsProvider.GetDefaultsSnapshot(),
                PromptSectionCatalogJson = string.Empty,
                HasUnifiedPromptCatalogPayload = true,
                UnifiedPromptCatalog = settings?.GetPromptUnifiedCatalogClone() ?? PromptUnifiedCatalogProvider.LoadMerged(),
                UnifiedPromptCatalogJson = string.Empty
            };

            bundle.SystemPromptJson = SerializeBundleSection(bundle.SystemPrompt);
            bundle.DiplomacyDialoguePromptJson = SerializeBundleSection(bundle.DiplomacyDialoguePrompt);
            bundle.PawnDialoguePromptJson = SerializeBundleSection(bundle.PawnDialoguePrompt);
            bundle.SocialCirclePromptJson = SerializeBundleSection(bundle.SocialCirclePrompt);
            bundle.PromptSectionCatalogJson = SerializeBundleSection(bundle.PromptSectionCatalog);
            bundle.UnifiedPromptCatalogJson = SerializeBundleSection(bundle.UnifiedPromptCatalog);

            return bundle;
        }

        private bool TryParsePromptBundle(string json, out PromptBundleConfig bundle)
        {
            return TryParsePromptBundle(json, out bundle, out _);
        }

        private static bool TryValidatePromptBundleImportEnvelope(
            string json,
            out PromptBundleImportFailure failure,
            out string errorCode)
        {
            failure = PromptBundleImportFailure.None;
            errorCode = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                failure = PromptBundleImportFailure.EmptyFile;
                errorCode = PromptBundleImportErrorCodes.EmptyFile;
                return false;
            }

            if (!LooksLikeJsonObject(json))
            {
                failure = PromptBundleImportFailure.InvalidJson;
                errorCode = PromptBundleImportErrorCodes.InvalidJson;
                return false;
            }

            if (ContainsAnyJsonKey(json, PromptPresetFeatureKeys))
            {
                failure = PromptBundleImportFailure.PresetFileDetected;
                errorCode = PromptBundleImportErrorCodes.PresetFileDetected;
                return false;
            }

            bool hasVersion = ContainsJsonKey(json, "BundleVersion");
            bool hasModules = ContainsJsonKey(json, "IncludedModules");
            bool hasPayload = ContainsAnyJsonKey(json, PromptBundlePayloadMarkers);
            if (!hasVersion || !hasModules || !hasPayload)
            {
                failure = PromptBundleImportFailure.NotPromptBundle;
                errorCode = PromptBundleImportErrorCodes.NotPromptBundle;
                return false;
            }

            return true;
        }

        private bool TryParsePromptBundle(
            string json,
            out PromptBundleConfig bundle,
            out HashSet<PromptBundleModule> includedModules)
        {
            includedModules = new HashSet<PromptBundleModule>();
            if (!PromptDomainJsonUtility.TryDeserialize(json, out bundle) || bundle == null)
            {
                return false;
            }

            HydratePromptBundleSectionsFromRawJson(bundle);
            bundle.SystemPrompt ??= new SystemPromptDomainConfig();
            bundle.DiplomacyDialoguePrompt ??= new DiplomacyDialoguePromptDomainConfig();
            bundle.PawnDialoguePrompt ??= new RpgPromptCustomConfig();
            bundle.SocialCirclePrompt ??= new SocialCirclePromptDomainConfig();
            bundle.PromptSectionCatalog ??= RimTalkPromptEntryDefaultsProvider.GetDefaultsSnapshot();
            bundle.UnifiedPromptCatalog ??= PromptUnifiedCatalog.CreateFallback();
            bundle.UnifiedPromptCatalog.NormalizeWith(PromptUnifiedCatalog.CreateFallback());

            if (bundle.BundleVersion <= 1 || bundle.IncludedModules == null || bundle.IncludedModules.Count == 0)
            {
                includedModules = NormalizeModuleSelection(PromptBundleModuleCatalog.All, includeAllWhenEmpty: true);
            }
            else
            {
                foreach (string token in bundle.IncludedModules)
                {
                    if (PromptBundleModuleCatalog.TryParseStorageToken(token, out PromptBundleModule module))
                    {
                        includedModules.Add(module);
                    }
                    else if (string.Equals(token, "rimtalk_diplomacy", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(token, "rimtalk_rpg", StringComparison.OrdinalIgnoreCase))
                    {
                        includedModules.Add(PromptBundleModule.RpgPrompt);
                    }
                }

                if (includedModules.Count == 0)
                {
                    includedModules = NormalizeModuleSelection(PromptBundleModuleCatalog.All, includeAllWhenEmpty: true);
                }
            }
            bundle.PromptSectionCatalog = PromptLegacyCompatMigration.ApplyLegacyPayloadToPromptSections(
                bundle.PromptSectionCatalog,
                json,
                "bundle");
            bundle.HasUnifiedPromptCatalogPayload =
                ContainsJsonKey(json, "UnifiedPromptCatalog") ||
                ContainsJsonKey(json, "UnifiedPromptCatalogJson");

            if (bundle.RimTalkSummaryHistoryLimit <= 0)
            {
                bundle.RimTalkSummaryHistoryLimit = bundle.PawnDialoguePrompt?.RimTalkSummaryHistoryLimit ?? 10;
            }

            bundle.IncludedModules = PromptBundleModuleCatalog.ToStorageTokens(includedModules);
            bundle.BundleVersion = Math.Max(bundle.BundleVersion, 1);
            return true;
        }

        private static string SerializeBundleSection<TPayload>(TPayload payload) where TPayload : class
        {
            if (payload == null)
            {
                return string.Empty;
            }

            try
            {
                return PromptDomainJsonUtility.Serialize(payload, prettyPrint: false) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void HydratePromptBundleSectionsFromRawJson(PromptBundleConfig bundle)
        {
            if (bundle == null)
            {
                return;
            }

            TryDeserializeBundleSection(bundle.SystemPromptJson, ref bundle.SystemPrompt);
            TryDeserializeBundleSection(bundle.DiplomacyDialoguePromptJson, ref bundle.DiplomacyDialoguePrompt);
            TryDeserializeBundleSection(bundle.PawnDialoguePromptJson, ref bundle.PawnDialoguePrompt);
            TryDeserializeBundleSection(bundle.SocialCirclePromptJson, ref bundle.SocialCirclePrompt);
            TryDeserializeBundleSection(bundle.PromptSectionCatalogJson, ref bundle.PromptSectionCatalog);
            TryDeserializeBundleSection(bundle.UnifiedPromptCatalogJson, ref bundle.UnifiedPromptCatalog);
        }

        private static void TryDeserializeBundleSection<TPayload>(string json, ref TPayload target)
            where TPayload : class, new()
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            if (PromptDomainJsonUtility.TryDeserialize(json, out TPayload payload) && payload != null)
            {
                target = payload;
            }
        }

        private static HashSet<PromptBundleModule> NormalizeModuleSelection(
            IEnumerable<PromptBundleModule> modules,
            bool includeAllWhenEmpty)
        {
            var set = modules != null
                ? new HashSet<PromptBundleModule>(modules)
                : new HashSet<PromptBundleModule>();
            if (set.Count == 0 && includeAllWhenEmpty)
            {
                foreach (PromptBundleModule module in PromptBundleModuleCatalog.All)
                {
                    set.Add(module);
                }
            }

            return set;
        }

        internal bool TryBuildPromptBundleImportPreview(string filePath, out PromptBundleImportPreview preview)
        {
            ResetPromptBundleImportFailure();
            preview = null;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                SetPromptBundleImportFailure(PromptBundleImportFailure.EmptyPath, PromptBundleImportErrorCodes.EmptyPath);
                return false;
            }

            if (!File.Exists(filePath))
            {
                SetPromptBundleImportFailure(PromptBundleImportFailure.FileNotFound, PromptBundleImportErrorCodes.FileNotFound);
                return false;
            }

            try
            {
                string json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    SetPromptBundleImportFailure(PromptBundleImportFailure.EmptyFile, PromptBundleImportErrorCodes.EmptyFile);
                    return false;
                }

                if (!TryValidatePromptBundleImportEnvelope(json, out PromptBundleImportFailure envelopeFailure, out string envelopeErrorCode))
                {
                    SetPromptBundleImportFailure(envelopeFailure, envelopeErrorCode);
                    Log.Warning($"[RimChat][{envelopeErrorCode}] Import preview rejected non-bundle file: {filePath}");
                    return false;
                }

                if (!TryParsePromptBundle(json, out PromptBundleConfig bundle, out HashSet<PromptBundleModule> includedModules))
                {
                    SetPromptBundleImportFailure(PromptBundleImportFailure.InvalidBundlePayload, PromptBundleImportErrorCodes.InvalidBundlePayload);
                    Log.Warning($"[RimChat][{PromptBundleImportErrorCodes.InvalidBundlePayload}] Import preview failed to parse bundle payload: {filePath}");
                    return false;
                }

                preview = new PromptBundleImportPreview
                {
                    FilePath = filePath,
                    BundleVersion = bundle.BundleVersion,
                    AvailableModules = includedModules.OrderBy(item => (int)item).ToList()
                };

                foreach (PromptBundleModule module in preview.AvailableModules)
                {
                    try
                    {
                        preview.ModuleSummaries[module] = BuildModuleSummary(bundle, module);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[RimChat] Failed to build import-preview summary for module {module}: {ex.Message}");
                        preview.ModuleSummaries[module] = "RimChat_PromptBundleSummary_Unavailable".Translate().ToString();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                SetPromptBundleImportFailure(PromptBundleImportFailure.UnexpectedException, PromptBundleImportErrorCodes.UnexpectedException);
                Log.Warning($"[RimChat][{PromptBundleImportErrorCodes.UnexpectedException}] Failed to build prompt-bundle import preview: {ex.Message}");
                preview = null;
                return false;
            }
        }

        private static bool LooksLikeJsonObject(string json)
        {
            string trimmed = json?.Trim();
            return !string.IsNullOrWhiteSpace(trimmed) &&
                   trimmed.StartsWith("{", StringComparison.Ordinal) &&
                   trimmed.EndsWith("}", StringComparison.Ordinal);
        }

        private static bool ContainsAnyJsonKey(string json, IEnumerable<string> keys)
        {
            if (keys == null)
            {
                return false;
            }

            foreach (string key in keys)
            {
                if (ContainsJsonKey(json, key))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildModuleSummary(PromptBundleConfig bundle, PromptBundleModule module)
        {
            switch (module)
            {
                case PromptBundleModule.SystemPrompt:
                    return bundle?.SystemPrompt == null
                        ? "RimChat_PromptBundleSummary_Unavailable".Translate().ToString()
                        : "RimChat_PromptBundleSummary_System".Translate(bundle.SystemPrompt.ConfigName ?? "Default").ToString();
                case PromptBundleModule.DiplomacyPrompt:
                    return bundle?.DiplomacyDialoguePrompt == null
                        ? "RimChat_PromptBundleSummary_Unavailable".Translate().ToString()
                        : "RimChat_PromptBundleSummary_Diplomacy".Translate(
                            bundle.DiplomacyDialoguePrompt.ApiActions?.Count ?? 0,
                            bundle.DiplomacyDialoguePrompt.DecisionRules?.Count ?? 0).ToString();
                case PromptBundleModule.RpgPrompt:
                    return bundle?.PawnDialoguePrompt == null
                        ? "RimChat_PromptBundleSummary_Unavailable".Translate().ToString()
                        : "RimChat_PromptBundleSummary_Rpg".Translate(
                            (bundle.PawnDialoguePrompt.RoleSetting ?? string.Empty).Length,
                            (bundle.PawnDialoguePrompt.DialogueStyle ?? string.Empty).Length).ToString();
                case PromptBundleModule.SocialCirclePrompt:
                    return bundle?.SocialCirclePrompt == null
                        ? "RimChat_PromptBundleSummary_Unavailable".Translate().ToString()
                        : "RimChat_PromptBundleSummary_Social".Translate(
                            bundle.SocialCirclePrompt.PublishPublicPostAction?.ActionName ?? "publish_public_post").ToString();
                case PromptBundleModule.FactionPrompts:
                    int count = FactionPromptJsonUtility.FromJson(bundle?.FactionPromptsJson ?? string.Empty)?.Configs?.Count ?? 0;
                    return "RimChat_PromptBundleSummary_Faction".Translate(count).ToString();
                default:
                    return "RimChat_PromptBundleSummary_Unavailable".Translate().ToString();
            }
        }

        private void SavePromptBundle(PromptBundleConfig bundle)
        {
            HashSet<PromptBundleModule> included = NormalizeModuleSelection(PromptBundleModuleCatalog.All, includeAllWhenEmpty: true);
            if (bundle?.IncludedModules != null && bundle.IncludedModules.Count > 0)
            {
                included.Clear();
                foreach (string token in bundle.IncludedModules)
                {
                    if (PromptBundleModuleCatalog.TryParseStorageToken(token, out PromptBundleModule parsed))
                    {
                        included.Add(parsed);
                    }
                }

                if (included.Count == 0)
                {
                    included = NormalizeModuleSelection(PromptBundleModuleCatalog.All, includeAllWhenEmpty: true);
                }
            }

            SavePromptBundle(bundle, included);
        }

        private void SavePromptBundle(PromptBundleConfig bundle, IEnumerable<PromptBundleModule> selectedModules)
        {
            if (bundle == null)
            {
                return;
            }

            HashSet<PromptBundleModule> selected = NormalizeModuleSelection(selectedModules, includeAllWhenEmpty: false);
            if (selected.Count == 0)
            {
                return;
            }

            PromptDomainFileCatalog.EnsureCustomDirectoryExists();

            if (selected.Contains(PromptBundleModule.SystemPrompt))
            {
                PromptDomainJsonUtility.WriteToFile(
                    PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SystemPromptCustomFileName),
                    bundle.SystemPrompt ?? new SystemPromptDomainConfig());
            }

            if (selected.Contains(PromptBundleModule.DiplomacyPrompt))
            {
                PromptDomainJsonUtility.WriteToFile(
                    PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.DiplomacyPromptCustomFileName),
                    bundle.DiplomacyDialoguePrompt ?? new DiplomacyDialoguePromptDomainConfig());
            }

            if (selected.Contains(PromptBundleModule.SocialCirclePrompt))
            {
                PromptDomainJsonUtility.WriteToFile(
                    PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SocialCirclePromptCustomFileName),
                    bundle.SocialCirclePrompt ?? new SocialCirclePromptDomainConfig());
            }

            RpgPromptCustomConfig currentRpg = RpgPromptCustomStore.LoadOrDefault() ?? new RpgPromptCustomConfig();
            RpgPromptCustomConfig mergedRpg = PromptDomainJsonUtility.Clone(currentRpg);
            bool shouldSaveRpg = false;

            if (selected.Contains(PromptBundleModule.RpgPrompt))
            {
                mergedRpg = PromptDomainJsonUtility.Clone(bundle.PawnDialoguePrompt ?? new RpgPromptCustomConfig());
                mergedRpg ??= new RpgPromptCustomConfig();
                mergedRpg.RimTalkSummaryHistoryLimit = bundle.RimTalkSummaryHistoryLimit > 0
                    ? bundle.RimTalkSummaryHistoryLimit
                    : currentRpg.RimTalkSummaryHistoryLimit;
                if (bundle.PromptSectionCatalog != null)
                {
                    RimChatMod.Settings?.ImportLegacySectionCatalogToUnifiedCatalog(
                        bundle.PromptSectionCatalog,
                        "bundle.import",
                        persistToFiles: true);
                }

                shouldSaveRpg = true;
            }

            if (shouldSaveRpg)
            {
                RpgPromptCustomStore.Save(mergedRpg);
            }

            if (selected.Contains(PromptBundleModule.FactionPrompts) &&
                !string.IsNullOrWhiteSpace(bundle.FactionPromptsJson))
            {
                FactionPromptManager.Instance.ImportConfigsFromJson(bundle.FactionPromptsJson);
            }

            bool shouldApplyUnified = selected.Contains(PromptBundleModule.SystemPrompt) ||
                                      selected.Contains(PromptBundleModule.DiplomacyPrompt) ||
                                      selected.Contains(PromptBundleModule.RpgPrompt) ||
                                      selected.Contains(PromptBundleModule.SocialCirclePrompt);
            if (shouldApplyUnified && bundle.HasUnifiedPromptCatalogPayload)
            {
                PromptUnifiedCatalog unified = bundle.UnifiedPromptCatalog?.Clone() ?? PromptUnifiedCatalog.CreateFallback();
                unified.NormalizeWith(PromptUnifiedCatalog.CreateFallback());
                PromptUnifiedCatalogProvider.SaveCustom(unified);
            }
        }
    }
}
