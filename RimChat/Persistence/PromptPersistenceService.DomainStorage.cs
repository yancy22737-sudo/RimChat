using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimChat.Config;
using RimChat.Core;
using Verse;

namespace RimChat.Persistence
{
    public partial class PromptPersistenceService
    {
        private static readonly string[] CustomPromptDomainFiles =
        {
            PromptDomainFileCatalog.SystemPromptCustomFileName,
            PromptDomainFileCatalog.DiplomacyPromptCustomFileName,
            PromptDomainFileCatalog.PawnPromptCustomFileName,
            PromptDomainFileCatalog.SocialCirclePromptCustomFileName
        };

        private bool TryLoadPromptDomains(out SystemPromptConfig config)
        {
            string json = BuildAggregateConfigJsonFromDomainFiles();
            config = string.IsNullOrWhiteSpace(json)
                ? null
                : ParseJsonToConfigInternal(json, "domain_aggregate");
            if (config == null)
            {
                return false;
            }

            ApplyPawnPromptTemplates(config, RpgPromptCustomStore.LoadOrDefault());
            return IsDomainConfigUsable(config);
        }

        private string BuildAggregateConfigJsonFromDomainFiles()
        {
            string systemDefault = ReadDomainJson(PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.SystemPromptDefaultFileName));
            string diplomacyDefault = ReadDomainJson(PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.DiplomacyPromptDefaultFileName));
            string socialDefault = ReadDomainJson(PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.SocialCirclePromptDefaultFileName));
            if (string.IsNullOrWhiteSpace(systemDefault) || string.IsNullOrWhiteSpace(diplomacyDefault) || string.IsNullOrWhiteSpace(socialDefault))
            {
                return string.Empty;
            }

            string systemCustom = ReadDomainJson(PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SystemPromptCustomFileName));
            string diplomacyCustom = ReadDomainJson(PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.DiplomacyPromptCustomFileName));
            string socialCustom = ReadDomainJson(PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SocialCirclePromptCustomFileName));
            RpgPromptCustomConfig pawnPrompt = RpgPromptCustomStore.LoadOrDefault();

            string configName = SelectStringField(systemCustom, systemDefault, "ConfigName", "Default");
            string globalSystemPrompt = SelectStringField(systemCustom, systemDefault, "GlobalSystemPrompt", string.Empty);
            string globalDialoguePrompt = SelectStringField(diplomacyCustom, diplomacyDefault, "GlobalDialoguePrompt", string.Empty);
            string useAdvancedMode = SelectValueField(systemCustom, systemDefault, "UseAdvancedMode", "false");
            string useHierarchical = SelectValueField(systemCustom, systemDefault, "UseHierarchicalPromptFormat", "true");
            string enabled = SelectValueField(systemCustom, systemDefault, "Enabled", "true");
            string promptSchemaVersion = SelectValueField(systemCustom, systemDefault, "PromptSchemaVersion", SystemPromptConfig.CurrentPromptSchemaVersion.ToString());
            string schemaVersion = SelectValueField(systemCustom, systemDefault, "PromptPolicySchemaVersion", SystemPromptConfig.CurrentPromptPolicySchemaVersion.ToString());
            string apiActions = MergeApiActionArray(diplomacyCustom, diplomacyDefault, socialCustom, socialDefault);
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

        private string MergeApiActionArray(string diplomacyCustom, string diplomacyDefault, string socialCustom, string socialDefault)
        {
            string diplomacyActions = SelectArraySection(diplomacyCustom, diplomacyDefault, "ApiActions", "[]");
            string socialAction = SelectObjectSection(socialCustom, socialDefault, "PublishPublicPostAction", "{}");
            string diplomacyInner = diplomacyActions.Length >= 2 ? diplomacyActions.Substring(1, diplomacyActions.Length - 2).Trim() : string.Empty;
            string socialInner = socialAction.Length >= 2 ? socialAction.Substring(1, socialAction.Length - 2).Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(diplomacyInner))
            {
                return "[" + socialAction + "]";
            }

            if (string.IsNullOrWhiteSpace(socialInner))
            {
                return diplomacyActions;
            }

            return "[" + diplomacyInner + "," + socialAction + "]";
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
            string apiLimits = SelectStringField(diplomacyCustom, diplomacyDefault, "ApiLimitsNodeTemplate", "{{ dialogue.api_limits_body }}");
            string questGuidance = SelectStringField(diplomacyCustom, diplomacyDefault, "QuestGuidanceNodeTemplate", "{{ dialogue.quest_guidance_body }}");
            string responseContract = SelectStringField(diplomacyCustom, diplomacyDefault, "ResponseContractNodeTemplate", "{{ dialogue.response_contract_body }}");

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
                + $"\"ResponseContractNodeTemplate\":\"{EscapeJson(responseContract)}\""
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
                ApiActions = BuildApiActions(diplomacyPrompt, socialPrompt),
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

        private static bool IsDomainConfigUsable(SystemPromptConfig config)
        {
            return config != null &&
                !string.IsNullOrWhiteSpace(config.GlobalSystemPrompt) &&
                config.ResponseFormat != null &&
                config.PromptTemplates != null &&
                config.PromptPolicy != null;
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
                ApiLimitsNodeTemplate = diplomacyPrompt?.ApiLimitsNodeTemplate ?? "{{ dialogue.api_limits_body }}",
                QuestGuidanceNodeTemplate = diplomacyPrompt?.QuestGuidanceNodeTemplate ?? "{{ dialogue.quest_guidance_body }}",
                ResponseContractNodeTemplate = diplomacyPrompt?.ResponseContractNodeTemplate ?? "{{ dialogue.response_contract_body }}"
            };
        }

        private static List<ApiActionConfig> BuildApiActions(
            DiplomacyDialoguePromptDomainConfig diplomacyPrompt,
            SocialCirclePromptDomainConfig socialPrompt)
        {
            var actions = CloneApiActions(diplomacyPrompt?.ApiActions);
            ApiActionConfig socialAction = socialPrompt?.PublishPublicPostAction?.Clone();
            if (socialAction != null)
            {
                int existingIndex = actions.FindIndex(item =>
                    string.Equals(item?.ActionName, socialAction.ActionName, StringComparison.OrdinalIgnoreCase));
                if (existingIndex >= 0)
                {
                    actions[existingIndex] = socialAction;
                }
                else
                {
                    actions.Add(socialAction);
                }
            }

            return actions;
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
                ApiLimitsNodeTemplate = config?.PromptTemplates?.ApiLimitsNodeTemplate ?? "{{ dialogue.api_limits_body }}",
                QuestGuidanceNodeTemplate = config?.PromptTemplates?.QuestGuidanceNodeTemplate ?? "{{ dialogue.quest_guidance_body }}",
                ResponseContractNodeTemplate = config?.PromptTemplates?.ResponseContractNodeTemplate ?? "{{ dialogue.response_contract_body }}"
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
                PublishPublicPostAction = FindPublishPublicPostAction(config?.ApiActions)
            };
        }

        private static ApiActionConfig FindPublishPublicPostAction(IEnumerable<ApiActionConfig> actions)
        {
            ApiActionConfig action = actions?.FirstOrDefault(item =>
                string.Equals(item?.ActionName, "publish_public_post", StringComparison.OrdinalIgnoreCase));
            return action?.Clone() ?? new ApiActionConfig(
                "publish_public_post",
                PromptTextConstants.PublishPublicPostActionDescription,
                PromptTextConstants.PublishPublicPostActionParameters,
                PromptTextConstants.PublishPublicPostActionRequirement);
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
            yield return PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.SocialCirclePromptDefaultFileName);
            yield return PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SystemPromptCustomFileName);
            yield return PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.DiplomacyPromptCustomFileName);
            yield return PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SocialCirclePromptCustomFileName);
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
                RimTalkDiplomacy = settings?.GetRimTalkChannelConfigClone(RimTalkPromptChannel.Diplomacy) ?? RimTalkChannelCompatConfig.CreateDefault(),
                RimTalkDiplomacyJson = string.Empty,
                RimTalkRpg = settings?.GetRimTalkChannelConfigClone(RimTalkPromptChannel.Rpg) ?? RimTalkChannelCompatConfig.CreateDefault(),
                RimTalkRpgJson = string.Empty
            };

            if (!selected.Contains(PromptBundleModule.RimTalkDiplomacy))
            {
                bundle.RimTalkDiplomacy = null;
            }

            if (!selected.Contains(PromptBundleModule.RimTalkRpg))
            {
                bundle.RimTalkRpg = null;
            }

            if (!selected.Contains(PromptBundleModule.RimTalkDiplomacy) &&
                !selected.Contains(PromptBundleModule.RimTalkRpg))
            {
                bundle.RimTalkSummaryHistoryLimit = 0;
            }

            bundle.SystemPromptJson = SerializeBundleSection(bundle.SystemPrompt);
            bundle.DiplomacyDialoguePromptJson = SerializeBundleSection(bundle.DiplomacyDialoguePrompt);
            bundle.PawnDialoguePromptJson = SerializeBundleSection(bundle.PawnDialoguePrompt);
            bundle.SocialCirclePromptJson = SerializeBundleSection(bundle.SocialCirclePrompt);
            bundle.RimTalkDiplomacyJson = SerializeBundleSection(bundle.RimTalkDiplomacy);
            bundle.RimTalkRpgJson = SerializeBundleSection(bundle.RimTalkRpg);

            return bundle;
        }

        private bool TryParsePromptBundle(string json, out PromptBundleConfig bundle)
        {
            return TryParsePromptBundle(json, out bundle, out _);
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

            if (bundle.BundleVersion <= 1 || bundle.IncludedModules == null || bundle.IncludedModules.Count == 0)
            {
                includedModules = NormalizeModuleSelection(PromptBundleModuleCatalog.All, includeAllWhenEmpty: true);
                HydrateRimTalkFromLegacyBundle(bundle);
            }
            else
            {
                foreach (string token in bundle.IncludedModules)
                {
                    if (PromptBundleModuleCatalog.TryParseStorageToken(token, out PromptBundleModule module))
                    {
                        includedModules.Add(module);
                    }
                }

                if (includedModules.Count == 0)
                {
                    includedModules = NormalizeModuleSelection(PromptBundleModuleCatalog.All, includeAllWhenEmpty: true);
                }
            }

            if (includedModules.Contains(PromptBundleModule.RimTalkDiplomacy))
            {
                bundle.RimTalkDiplomacy ??= BuildLegacyRimTalkChannel(bundle.PawnDialoguePrompt);
            }

            if (includedModules.Contains(PromptBundleModule.RimTalkRpg))
            {
                bundle.RimTalkRpg ??= BuildLegacyRimTalkChannel(bundle.PawnDialoguePrompt);
            }

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
            TryDeserializeBundleSection(bundle.RimTalkDiplomacyJson, ref bundle.RimTalkDiplomacy);
            TryDeserializeBundleSection(bundle.RimTalkRpgJson, ref bundle.RimTalkRpg);
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

        private static void HydrateRimTalkFromLegacyBundle(PromptBundleConfig bundle)
        {
            if (bundle == null)
            {
                return;
            }

            bundle.RimTalkDiplomacy ??= BuildLegacyRimTalkChannel(bundle.PawnDialoguePrompt);
            bundle.RimTalkRpg ??= BuildLegacyRimTalkChannel(bundle.PawnDialoguePrompt);
            if (bundle.RimTalkSummaryHistoryLimit <= 0)
            {
                bundle.RimTalkSummaryHistoryLimit = bundle.PawnDialoguePrompt?.RimTalkSummaryHistoryLimit ?? 10;
            }
        }

        private static RimTalkChannelCompatConfig BuildLegacyRimTalkChannel(RpgPromptCustomConfig source)
        {
            var config = new RimTalkChannelCompatConfig
            {
                EnablePromptCompat = source?.EnableRimTalkPromptCompat ?? true,
                PresetInjectionMaxEntries = source?.RimTalkPresetInjectionMaxEntries ?? RimChatSettings.RimTalkPresetInjectionLimitUnlimited,
                PresetInjectionMaxChars = source?.RimTalkPresetInjectionMaxChars ?? RimChatSettings.RimTalkPresetInjectionLimitUnlimited,
                CompatTemplate = source?.RimTalkCompatTemplate ?? RimChatSettings.DefaultRimTalkCompatTemplate
            };
            config.NormalizeWith(RimTalkChannelCompatConfig.CreateDefault());
            return config;
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
            preview = null;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                if (!TryParsePromptBundle(json, out PromptBundleConfig bundle, out HashSet<PromptBundleModule> includedModules))
                {
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
                Log.Warning($"[RimChat] Failed to build prompt-bundle import preview: {ex.Message}");
                preview = null;
                return false;
            }
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
                case PromptBundleModule.RimTalkDiplomacy:
                {
                    string diplomacyEnabled = bundle?.RimTalkDiplomacy?.EnablePromptCompat == true
                        ? "RimChat_CommsToggleStatusOn".Translate().ToString()
                        : "RimChat_CommsToggleStatusOff".Translate().ToString();
                    return bundle?.RimTalkDiplomacy == null
                        ? "RimChat_PromptBundleSummary_Unavailable".Translate().ToString()
                        : "RimChat_PromptBundleSummary_RimTalk".Translate(
                            diplomacyEnabled,
                            (bundle.RimTalkDiplomacy.CompatTemplate ?? string.Empty).Length).ToString();
                }
                case PromptBundleModule.RimTalkRpg:
                {
                    string rpgEnabled = bundle?.RimTalkRpg?.EnablePromptCompat == true
                        ? "RimChat_CommsToggleStatusOn".Translate().ToString()
                        : "RimChat_CommsToggleStatusOff".Translate().ToString();
                    return bundle?.RimTalkRpg == null
                        ? "RimChat_PromptBundleSummary_Unavailable".Translate().ToString()
                        : "RimChat_PromptBundleSummary_RimTalk".Translate(
                            rpgEnabled,
                            (bundle.RimTalkRpg.CompatTemplate ?? string.Empty).Length).ToString();
                }
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
                if (!selected.Contains(PromptBundleModule.RimTalkDiplomacy))
                {
                    mergedRpg.RimTalkDiplomacy = currentRpg.RimTalkDiplomacy?.Clone() ?? BuildLegacyRimTalkChannel(currentRpg);
                }

                if (!selected.Contains(PromptBundleModule.RimTalkRpg))
                {
                    mergedRpg.RimTalkRpg = currentRpg.RimTalkRpg?.Clone() ?? BuildLegacyRimTalkChannel(currentRpg);
                }

                if (!selected.Contains(PromptBundleModule.RimTalkDiplomacy) &&
                    !selected.Contains(PromptBundleModule.RimTalkRpg))
                {
                    mergedRpg.RimTalkSummaryHistoryLimit = currentRpg.RimTalkSummaryHistoryLimit;
                }

                shouldSaveRpg = true;
            }

            if (selected.Contains(PromptBundleModule.RimTalkDiplomacy) ||
                selected.Contains(PromptBundleModule.RimTalkRpg))
            {
                if (selected.Contains(PromptBundleModule.RimTalkDiplomacy))
                {
                    mergedRpg.RimTalkDiplomacy = bundle.RimTalkDiplomacy?.Clone() ?? BuildLegacyRimTalkChannel(bundle.PawnDialoguePrompt);
                }

                if (selected.Contains(PromptBundleModule.RimTalkRpg))
                {
                    mergedRpg.RimTalkRpg = bundle.RimTalkRpg?.Clone() ?? BuildLegacyRimTalkChannel(bundle.PawnDialoguePrompt);
                }

                if (bundle.RimTalkSummaryHistoryLimit > 0)
                {
                    mergedRpg.RimTalkSummaryHistoryLimit = bundle.RimTalkSummaryHistoryLimit;
                }

                mergedRpg.RimTalkChannelSplitMigrated = true;
                shouldSaveRpg = true;
            }

            if (shouldSaveRpg)
            {
                mergedRpg.RimTalkDiplomacy ??= BuildLegacyRimTalkChannel(mergedRpg);
                mergedRpg.RimTalkRpg ??= BuildLegacyRimTalkChannel(mergedRpg);
                RimTalkChannelCompatConfig rpg = mergedRpg.RimTalkRpg ?? RimTalkChannelCompatConfig.CreateDefault();
                mergedRpg.EnableRimTalkPromptCompat = rpg.EnablePromptCompat;
                mergedRpg.RimTalkPresetInjectionMaxEntries = rpg.PresetInjectionMaxEntries;
                mergedRpg.RimTalkPresetInjectionMaxChars = rpg.PresetInjectionMaxChars;
                mergedRpg.RimTalkCompatTemplate = rpg.CompatTemplate ?? RimChatSettings.DefaultRimTalkCompatTemplate;
                RpgPromptCustomStore.Save(mergedRpg);
            }

            if (selected.Contains(PromptBundleModule.FactionPrompts) &&
                !string.IsNullOrWhiteSpace(bundle.FactionPromptsJson))
            {
                FactionPromptManager.Instance.ImportConfigsFromJson(bundle.FactionPromptsJson);
            }
        }
    }
}
