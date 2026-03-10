using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimChat.Config;
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
            config = string.IsNullOrWhiteSpace(json) ? null : ParseJsonToConfigInternal(json);
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
            string apiLimits = SelectStringField(diplomacyCustom, diplomacyDefault, "ApiLimitsNodeTemplate", "{{api_limits_body}}");
            string questGuidance = SelectStringField(diplomacyCustom, diplomacyDefault, "QuestGuidanceNodeTemplate", "{{quest_guidance_body}}");
            string responseContract = SelectStringField(diplomacyCustom, diplomacyDefault, "ResponseContractNodeTemplate", "{{response_contract_body}}");

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
                ApiLimitsNodeTemplate = diplomacyPrompt?.ApiLimitsNodeTemplate ?? "{{api_limits_body}}",
                QuestGuidanceNodeTemplate = diplomacyPrompt?.QuestGuidanceNodeTemplate ?? "{{quest_guidance_body}}",
                ResponseContractNodeTemplate = diplomacyPrompt?.ResponseContractNodeTemplate ?? "{{response_contract_body}}"
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
                ApiLimitsNodeTemplate = config?.PromptTemplates?.ApiLimitsNodeTemplate ?? "{{api_limits_body}}",
                QuestGuidanceNodeTemplate = config?.PromptTemplates?.QuestGuidanceNodeTemplate ?? "{{quest_guidance_body}}",
                ResponseContractNodeTemplate = config?.PromptTemplates?.ResponseContractNodeTemplate ?? "{{response_contract_body}}"
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
            return new PromptBundleConfig
            {
                BundleVersion = 1,
                SystemPrompt = BuildSystemPromptDomain(config),
                DiplomacyDialoguePrompt = BuildDiplomacyPromptDomain(config),
                PawnDialoguePrompt = RpgPromptCustomStore.LoadOrDefault(),
                SocialCirclePrompt = BuildSocialCirclePromptDomain(config),
                FactionPromptsJson = FactionPromptManager.Instance.ExportConfigsToJson(prettyPrint: true)
            };
        }

        private bool TryParsePromptBundle(string json, out PromptBundleConfig bundle)
        {
            if (!PromptDomainJsonUtility.TryDeserialize(json, out bundle) || bundle == null)
            {
                return false;
            }

            bundle.SystemPrompt ??= new SystemPromptDomainConfig();
            bundle.DiplomacyDialoguePrompt ??= new DiplomacyDialoguePromptDomainConfig();
            bundle.PawnDialoguePrompt ??= new RpgPromptCustomConfig();
            bundle.SocialCirclePrompt ??= new SocialCirclePromptDomainConfig();
            return true;
        }

        private void SavePromptBundle(PromptBundleConfig bundle)
        {
            PromptDomainFileCatalog.EnsureCustomDirectoryExists();
            PromptDomainJsonUtility.WriteToFile(
                PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SystemPromptCustomFileName),
                bundle.SystemPrompt ?? new SystemPromptDomainConfig());
            PromptDomainJsonUtility.WriteToFile(
                PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.DiplomacyPromptCustomFileName),
                bundle.DiplomacyDialoguePrompt ?? new DiplomacyDialoguePromptDomainConfig());
            PromptDomainJsonUtility.WriteToFile(
                PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SocialCirclePromptCustomFileName),
                bundle.SocialCirclePrompt ?? new SocialCirclePromptDomainConfig());
            RpgPromptCustomStore.Save(bundle.PawnDialoguePrompt ?? new RpgPromptCustomConfig());
            FactionPromptManager.Instance.ImportConfigsFromJson(bundle.FactionPromptsJson);
        }
    }
}
