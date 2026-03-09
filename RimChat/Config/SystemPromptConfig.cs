using System;
using System.Collections.Generic;
using Verse;
using RimChat.Persistence;
using RimChat.Core;

namespace RimChat.Config
{
    [Serializable]
    public class ApiActionConfig : IExposable
    {
        public string ActionName;
        public string Description;
        public string Parameters;
        public string Requirement;
        public bool IsEnabled;

        public ApiActionConfig()
        {
            IsEnabled = true;
        }

        public ApiActionConfig(string actionName, string description, string parameters = "", string requirement = "")
        {
            ActionName = actionName;
            Description = description;
            Parameters = parameters;
            Requirement = requirement;
            IsEnabled = true;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref ActionName, "actionName", "");
            Scribe_Values.Look(ref Description, "description", "");
            Scribe_Values.Look(ref Parameters, "parameters", "");
            Scribe_Values.Look(ref Requirement, "requirement", "");
            Scribe_Values.Look(ref IsEnabled, "isEnabled", true);
        }

        public ApiActionConfig Clone()
        {
            return new ApiActionConfig
            {
                ActionName = this.ActionName,
                Description = this.Description,
                Parameters = this.Parameters,
                Requirement = this.Requirement,
                IsEnabled = this.IsEnabled
            };
        }
    }

    [Serializable]
    public class ResponseFormatConfig : IExposable
    {
        public string JsonTemplate;
        public string ImportantRules;

        public void ExposeData()
        {
            Scribe_Values.Look(ref JsonTemplate, "jsonTemplate", "");
            Scribe_Values.Look(ref ImportantRules, "importantRules", "");
        }

        public ResponseFormatConfig Clone()
        {
            return new ResponseFormatConfig
            {
                JsonTemplate = this.JsonTemplate,
                ImportantRules = this.ImportantRules
            };
        }
    }

    [Serializable]
    public class DecisionRuleConfig : IExposable
    {
        public string RuleName;
        public string RuleContent;
        public bool IsEnabled;

        public DecisionRuleConfig()
        {
            IsEnabled = true;
        }

        public DecisionRuleConfig(string ruleName, string ruleContent)
        {
            RuleName = ruleName;
            RuleContent = ruleContent;
            IsEnabled = true;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref RuleName, "ruleName", "");
            Scribe_Values.Look(ref RuleContent, "ruleContent", "");
            Scribe_Values.Look(ref IsEnabled, "isEnabled", true);
        }

        public DecisionRuleConfig Clone()
        {
            return new DecisionRuleConfig
            {
                RuleName = this.RuleName,
                RuleContent = this.RuleContent,
                IsEnabled = this.IsEnabled
            };
        }
    }

    [Serializable]
    public class DynamicDataInjectionConfig : IExposable
    {
        public bool InjectMemoryData;
        public bool InjectFactionInfo;
        public string CustomInjectionHeader;

        public DynamicDataInjectionConfig()
        {
            InjectMemoryData = true;
            InjectFactionInfo = true;
            CustomInjectionHeader = "";
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref InjectMemoryData, "injectMemoryData", true);
            Scribe_Values.Look(ref InjectFactionInfo, "injectFactionInfo", true);
            Scribe_Values.Look(ref CustomInjectionHeader, "customInjectionHeader", "");
        }

        public DynamicDataInjectionConfig Clone()
        {
            return new DynamicDataInjectionConfig
            {
                InjectMemoryData = this.InjectMemoryData,
                InjectFactionInfo = this.InjectFactionInfo,
                CustomInjectionHeader = this.CustomInjectionHeader
            };
        }
    }

    [Serializable]
    public class WorldviewPromptConfig : IExposable
    {
        public bool Enabled;
        public string Content;

        public WorldviewPromptConfig()
        {
            Enabled = true;
            Content = string.Empty;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Values.Look(ref Content, "content", string.Empty);
        }

        public WorldviewPromptConfig Clone()
        {
            return new WorldviewPromptConfig
            {
                Enabled = this.Enabled,
                Content = this.Content
            };
        }
    }

    [Serializable]
    public class SceneSystemPromptConfig : IExposable
    {
        public bool Enabled;
        public int MaxSceneChars;
        public int MaxTotalChars;
        public bool PresetTagsEnabled;

        public SceneSystemPromptConfig()
        {
            Enabled = true;
            MaxSceneChars = 1200;
            MaxTotalChars = 4000;
            PresetTagsEnabled = true;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Values.Look(ref MaxSceneChars, "maxSceneChars", 1200);
            Scribe_Values.Look(ref MaxTotalChars, "maxTotalChars", 4000);
            Scribe_Values.Look(ref PresetTagsEnabled, "presetTagsEnabled", true);
        }

        public SceneSystemPromptConfig Clone()
        {
            return new SceneSystemPromptConfig
            {
                Enabled = this.Enabled,
                MaxSceneChars = this.MaxSceneChars,
                MaxTotalChars = this.MaxTotalChars,
                PresetTagsEnabled = this.PresetTagsEnabled
            };
        }
    }

    [Serializable]
    public class ScenePromptEntryConfig : IExposable
    {
        public string Id;
        public string Name;
        public bool Enabled;
        public bool ApplyToDiplomacy;
        public bool ApplyToRPG;
        public int Priority;
        public List<string> MatchTags;
        public string Content;

        public ScenePromptEntryConfig()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = string.Empty;
            Enabled = true;
            ApplyToDiplomacy = true;
            ApplyToRPG = true;
            Priority = 0;
            MatchTags = new List<string>();
            Content = string.Empty;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id", string.Empty);
            Scribe_Values.Look(ref Name, "name", string.Empty);
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Values.Look(ref ApplyToDiplomacy, "applyToDiplomacy", true);
            Scribe_Values.Look(ref ApplyToRPG, "applyToRPG", true);
            Scribe_Values.Look(ref Priority, "priority", 0);
            Scribe_Collections.Look(ref MatchTags, "matchTags", LookMode.Value);
            Scribe_Values.Look(ref Content, "content", string.Empty);
            if (MatchTags == null)
            {
                MatchTags = new List<string>();
            }
            if (string.IsNullOrEmpty(Id))
            {
                Id = Guid.NewGuid().ToString("N");
            }
        }

        public ScenePromptEntryConfig Clone()
        {
            return new ScenePromptEntryConfig
            {
                Id = this.Id,
                Name = this.Name,
                Enabled = this.Enabled,
                ApplyToDiplomacy = this.ApplyToDiplomacy,
                ApplyToRPG = this.ApplyToRPG,
                Priority = this.Priority,
                MatchTags = this.MatchTags != null ? new List<string>(this.MatchTags) : new List<string>(),
                Content = this.Content
            };
        }
    }

    [Serializable]
    public class RpgSceneParamSwitchesConfig : IExposable
    {
        public bool IncludeSkills;
        public bool IncludeEquipment;
        public bool IncludeGenes;
        public bool IncludeNeeds;
        public bool IncludeHediffs;
        public bool IncludeRecentEvents;
        public bool IncludeColonyInventorySummary;
        public bool IncludeHomeAlerts;
        public bool IncludeRecentJobState;
        public bool IncludeAttributeLevels;

        public RpgSceneParamSwitchesConfig()
        {
            IncludeSkills = true;
            IncludeEquipment = true;
            IncludeGenes = true;
            IncludeNeeds = true;
            IncludeHediffs = true;
            IncludeRecentEvents = true;
            IncludeColonyInventorySummary = true;
            IncludeHomeAlerts = true;
            IncludeRecentJobState = true;
            IncludeAttributeLevels = true;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref IncludeSkills, "includeSkills", true);
            Scribe_Values.Look(ref IncludeEquipment, "includeEquipment", true);
            Scribe_Values.Look(ref IncludeGenes, "includeGenes", true);
            Scribe_Values.Look(ref IncludeNeeds, "includeNeeds", true);
            Scribe_Values.Look(ref IncludeHediffs, "includeHediffs", true);
            Scribe_Values.Look(ref IncludeRecentEvents, "includeRecentEvents", true);
            Scribe_Values.Look(ref IncludeColonyInventorySummary, "includeColonyInventorySummary", true);
            Scribe_Values.Look(ref IncludeHomeAlerts, "includeHomeAlerts", true);
            Scribe_Values.Look(ref IncludeRecentJobState, "includeRecentJobState", true);
            Scribe_Values.Look(ref IncludeAttributeLevels, "includeAttributeLevels", true);
        }

        public RpgSceneParamSwitchesConfig Clone()
        {
            return new RpgSceneParamSwitchesConfig
            {
                IncludeSkills = this.IncludeSkills,
                IncludeEquipment = this.IncludeEquipment,
                IncludeGenes = this.IncludeGenes,
                IncludeNeeds = this.IncludeNeeds,
                IncludeHediffs = this.IncludeHediffs,
                IncludeRecentEvents = this.IncludeRecentEvents,
                IncludeColonyInventorySummary = this.IncludeColonyInventorySummary,
                IncludeHomeAlerts = this.IncludeHomeAlerts,
                IncludeRecentJobState = this.IncludeRecentJobState,
                IncludeAttributeLevels = this.IncludeAttributeLevels
            };
        }
    }

    [Serializable]
    public class EnvironmentContextSwitchesConfig : IExposable
    {
        public bool Enabled;
        public bool IncludeTime;
        public bool IncludeDate;
        public bool IncludeSeason;
        public bool IncludeWeather;
        public bool IncludeLocationAndTemperature;
        public bool IncludeTerrain;
        public bool IncludeBeauty;
        public bool IncludeCleanliness;
        public bool IncludeSurroundings;
        public bool IncludeWealth;

        public EnvironmentContextSwitchesConfig()
        {
            Enabled = true;
            IncludeTime = true;
            IncludeDate = false;
            IncludeSeason = true;
            IncludeWeather = true;
            IncludeLocationAndTemperature = true;
            IncludeTerrain = false;
            IncludeBeauty = false;
            IncludeCleanliness = false;
            IncludeSurroundings = false;
            IncludeWealth = false;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Values.Look(ref IncludeTime, "includeTime", true);
            Scribe_Values.Look(ref IncludeDate, "includeDate", false);
            Scribe_Values.Look(ref IncludeSeason, "includeSeason", true);
            Scribe_Values.Look(ref IncludeWeather, "includeWeather", true);
            Scribe_Values.Look(ref IncludeLocationAndTemperature, "includeLocationAndTemperature", true);
            Scribe_Values.Look(ref IncludeTerrain, "includeTerrain", false);
            Scribe_Values.Look(ref IncludeBeauty, "includeBeauty", false);
            Scribe_Values.Look(ref IncludeCleanliness, "includeCleanliness", false);
            Scribe_Values.Look(ref IncludeSurroundings, "includeSurroundings", false);
            Scribe_Values.Look(ref IncludeWealth, "includeWealth", false);
        }

        public EnvironmentContextSwitchesConfig Clone()
        {
            return new EnvironmentContextSwitchesConfig
            {
                Enabled = this.Enabled,
                IncludeTime = this.IncludeTime,
                IncludeDate = this.IncludeDate,
                IncludeSeason = this.IncludeSeason,
                IncludeWeather = this.IncludeWeather,
                IncludeLocationAndTemperature = this.IncludeLocationAndTemperature,
                IncludeTerrain = this.IncludeTerrain,
                IncludeBeauty = this.IncludeBeauty,
                IncludeCleanliness = this.IncludeCleanliness,
                IncludeSurroundings = this.IncludeSurroundings,
                IncludeWealth = this.IncludeWealth
            };
        }
    }

    [Serializable]
    public class EnvironmentPromptConfig : IExposable
    {
        public WorldviewPromptConfig Worldview;
        public SceneSystemPromptConfig SceneSystem;
        public List<ScenePromptEntryConfig> SceneEntries;
        public EnvironmentContextSwitchesConfig EnvironmentContextSwitches;
        public RpgSceneParamSwitchesConfig RpgSceneParamSwitches;
        public EventIntelPromptConfig EventIntelPrompt;

        public EnvironmentPromptConfig()
        {
            Worldview = new WorldviewPromptConfig();
            SceneSystem = new SceneSystemPromptConfig();
            SceneEntries = new List<ScenePromptEntryConfig>();
            EnvironmentContextSwitches = new EnvironmentContextSwitchesConfig();
            RpgSceneParamSwitches = new RpgSceneParamSwitchesConfig();
            EventIntelPrompt = new EventIntelPromptConfig();
        }

        public void ExposeData()
        {
            Scribe_Deep.Look(ref Worldview, "worldview");
            Scribe_Deep.Look(ref SceneSystem, "sceneSystem");
            Scribe_Collections.Look(ref SceneEntries, "sceneEntries", LookMode.Deep);
            Scribe_Deep.Look(ref EnvironmentContextSwitches, "environmentContextSwitches");
            Scribe_Deep.Look(ref RpgSceneParamSwitches, "rpgSceneParamSwitches");
            Scribe_Deep.Look(ref EventIntelPrompt, "eventIntelPrompt");

            if (Worldview == null) Worldview = new WorldviewPromptConfig();
            if (SceneSystem == null) SceneSystem = new SceneSystemPromptConfig();
            if (SceneEntries == null) SceneEntries = new List<ScenePromptEntryConfig>();
            if (EnvironmentContextSwitches == null) EnvironmentContextSwitches = new EnvironmentContextSwitchesConfig();
            if (RpgSceneParamSwitches == null) RpgSceneParamSwitches = new RpgSceneParamSwitchesConfig();
            if (EventIntelPrompt == null) EventIntelPrompt = new EventIntelPromptConfig();
        }

        public EnvironmentPromptConfig Clone()
        {
            var clone = new EnvironmentPromptConfig
            {
                Worldview = this.Worldview?.Clone() ?? new WorldviewPromptConfig(),
                SceneSystem = this.SceneSystem?.Clone() ?? new SceneSystemPromptConfig(),
                EnvironmentContextSwitches = this.EnvironmentContextSwitches?.Clone() ?? new EnvironmentContextSwitchesConfig(),
                RpgSceneParamSwitches = this.RpgSceneParamSwitches?.Clone() ?? new RpgSceneParamSwitchesConfig(),
                EventIntelPrompt = this.EventIntelPrompt?.Clone() ?? new EventIntelPromptConfig(),
                SceneEntries = new List<ScenePromptEntryConfig>()
            };

            if (this.SceneEntries != null)
            {
                foreach (var entry in this.SceneEntries)
                {
                    if (entry != null)
                    {
                        clone.SceneEntries.Add(entry.Clone());
                    }
                }
            }

            return clone;
        }

        public static EnvironmentPromptConfig CreateDefaultSeed()
        {
            var config = new EnvironmentPromptConfig();
            config.Worldview.Enabled = true;
            config.Worldview.Content = "你处于 RimWorld 的真实世界观中。请以当前世界事件、资源压力、派系立场和生存逻辑来组织回应。";
            config.SceneSystem.Enabled = true;
            config.SceneSystem.MaxSceneChars = 1200;
            config.SceneSystem.MaxTotalChars = 4000;
            config.SceneSystem.PresetTagsEnabled = true;
            config.EventIntelPrompt = new EventIntelPromptConfig
            {
                Enabled = true,
                ApplyToDiplomacy = true,
                ApplyToRpg = true,
                IncludeMapEvents = true,
                IncludeRaidBattleReports = true,
                DaysWindow = 15,
                MaxStoredRecords = 50,
                MaxInjectedItems = 8,
                MaxInjectedChars = 1200
            };

            config.SceneEntries = new List<ScenePromptEntryConfig>
            {
                CreateSeedEntry(
                    "外交-社交接触",
                    30,
                    true,
                    false,
                    "派系初次接触或常规寒暄，强调礼节、信息交换和试探边界。",
                    "channel:diplomacy",
                    "scene:social"),
                CreateSeedEntry(
                    "外交-任务协商",
                    60,
                    true,
                    false,
                    "聚焦任务目标、条件、风险与回报，避免空泛承诺。",
                    "channel:diplomacy",
                    "scene:task"),
                CreateSeedEntry(
                    "外交-威胁对抗",
                    90,
                    true,
                    false,
                    "进入高压谈判与威慑博弈，语言应更强硬并保持立场一致。",
                    "channel:diplomacy",
                    "scene:threat"),
                CreateSeedEntry(
                    "RPG-日常互动",
                    30,
                    false,
                    true,
                    "进行生活化、角色化交流，注意口语感与个体性。",
                    "channel:rpg",
                    "scene:daily"),
                CreateSeedEntry(
                    "RPG-亲密关系",
                    70,
                    false,
                    true,
                    "突出情感张力、信任和依赖变化，避免机械化表达。",
                    "channel:rpg",
                    "scene:intimacy"),
                CreateSeedEntry(
                    "RPG-冲突对话",
                    85,
                    false,
                    true,
                    "处理争执、挑衅或拒绝情境，保持角色动机和后果一致。",
                    "channel:rpg",
                    "scene:conflict")
            };

            return config;
        }

        private static ScenePromptEntryConfig CreateSeedEntry(
            string name,
            int priority,
            bool diplomacy,
            bool rpg,
            string content,
            params string[] tags)
        {
            return new ScenePromptEntryConfig
            {
                Name = name,
                Priority = priority,
                ApplyToDiplomacy = diplomacy,
                ApplyToRPG = rpg,
                Content = content,
                MatchTags = tags != null ? new List<string>(tags) : new List<string>()
            };
        }
    }

    [Serializable]
    public class SystemPromptConfig : IExposable
    {
        public const int CurrentPromptPolicySchemaVersion = 3;

        public string ConfigName;
        public string GlobalSystemPrompt;
        public string GlobalDialoguePrompt;
        public bool UseAdvancedMode;
        public bool UseHierarchicalPromptFormat;

        public List<ApiActionConfig> ApiActions;
        public ResponseFormatConfig ResponseFormat;
        public List<DecisionRuleConfig> DecisionRules;
        public EnvironmentPromptConfig EnvironmentPrompt;
        public DynamicDataInjectionConfig DynamicDataInjection;
        public PromptTemplateTextConfig PromptTemplates;
        public int PromptPolicySchemaVersion;
        public PromptPolicyConfig PromptPolicy;

        public bool Enabled;

        public SystemPromptConfig()
        {
            ConfigName = "Default";
            GlobalSystemPrompt = "";
            GlobalDialoguePrompt = "";
            UseAdvancedMode = false;
            UseHierarchicalPromptFormat = true;
            Enabled = true;
            ApiActions = new List<ApiActionConfig>();
            ResponseFormat = new ResponseFormatConfig();
            DecisionRules = new List<DecisionRuleConfig>();
            EnvironmentPrompt = new EnvironmentPromptConfig();
            DynamicDataInjection = new DynamicDataInjectionConfig();
            PromptTemplates = new PromptTemplateTextConfig();
            PromptPolicySchemaVersion = CurrentPromptPolicySchemaVersion;
            PromptPolicy = PromptPolicyConfig.CreateDefault();
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref ConfigName, "configName", "Default");
            Scribe_Values.Look(ref GlobalSystemPrompt, "globalSystemPrompt", "");
            Scribe_Values.Look(ref GlobalDialoguePrompt, "globalDialoguePrompt", "");
            Scribe_Values.Look(ref UseAdvancedMode, "useAdvancedMode", false);
            Scribe_Values.Look(ref UseHierarchicalPromptFormat, "useHierarchicalPromptFormat", true);
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Collections.Look(ref ApiActions, "apiActions", LookMode.Deep);
            Scribe_Deep.Look(ref ResponseFormat, "responseFormat");
            Scribe_Collections.Look(ref DecisionRules, "decisionRules", LookMode.Deep);
            Scribe_Deep.Look(ref EnvironmentPrompt, "environmentPrompt");
            Scribe_Deep.Look(ref DynamicDataInjection, "dynamicDataInjection");
            Scribe_Deep.Look(ref PromptTemplates, "promptTemplates");
            Scribe_Values.Look(ref PromptPolicySchemaVersion, "promptPolicySchemaVersion", CurrentPromptPolicySchemaVersion);
            Scribe_Deep.Look(ref PromptPolicy, "promptPolicy");
            if (EnvironmentPrompt == null)
            {
                EnvironmentPrompt = new EnvironmentPromptConfig();
            }

            if (PromptTemplates == null)
            {
                PromptTemplates = new PromptTemplateTextConfig();
            }

            if (PromptPolicy == null)
            {
                PromptPolicy = PromptPolicyConfig.CreateDefault();
            }

            if (PromptPolicySchemaVersion <= 0)
            {
                PromptPolicySchemaVersion = CurrentPromptPolicySchemaVersion;
            }
        }

        public SystemPromptConfig Clone()
        {
            var clone = new SystemPromptConfig
            {
                ConfigName = this.ConfigName,
                GlobalSystemPrompt = this.GlobalSystemPrompt,
                GlobalDialoguePrompt = this.GlobalDialoguePrompt,
                UseAdvancedMode = this.UseAdvancedMode,
                UseHierarchicalPromptFormat = this.UseHierarchicalPromptFormat,
                Enabled = this.Enabled,
                ResponseFormat = this.ResponseFormat?.Clone() ?? new ResponseFormatConfig(),
                EnvironmentPrompt = this.EnvironmentPrompt?.Clone() ?? new EnvironmentPromptConfig(),
                DynamicDataInjection = this.DynamicDataInjection?.Clone() ?? new DynamicDataInjectionConfig(),
                PromptTemplates = this.PromptTemplates?.Clone() ?? new PromptTemplateTextConfig(),
                PromptPolicySchemaVersion = this.PromptPolicySchemaVersion,
                PromptPolicy = this.PromptPolicy?.Clone() ?? PromptPolicyConfig.CreateDefault()
            };

            foreach (var action in ApiActions)
            {
                clone.ApiActions.Add(action.Clone());
            }

            foreach (var rule in DecisionRules)
            {
                clone.DecisionRules.Add(rule.Clone());
            }

            return clone;
        }

        public void InitializeDefaults()
        {
            // 尝试从默认configurationfileload
            var defaultConfig = LoadDefaultConfigFromFile();
            if (IsDefaultConfigUsable(defaultConfig))
            {
                CopyFrom(defaultConfig);
                return;
            }

            if (defaultConfig != null)
            {
                Log.Warning("[RimChat] Default system prompt file parsed but critical sections are missing; fallback to minimal defaults.");
            }

            // 如果fileload失败, 使用最小化默认configuration
            InitializeMinimalDefaults();
        }

        /// <summary>/// 从 SystemPrompt_Default.json fileload默认configuration
 ///</summary>
        private SystemPromptConfig LoadDefaultConfigFromFile()
        {
            try
            {
                string defaultConfigPath = GetDefaultConfigPath();
                if (System.IO.File.Exists(defaultConfigPath))
                {
                    string json = System.IO.File.ReadAllText(defaultConfigPath);
                    // 使用 PromptPersistenceService 的解析method
                    var config = PromptPersistenceService.Instance?.ParseJsonToConfigInternal(json);
                    if (config != null)
                    {
                        Log.Message($"[RimChat] Loaded default system prompt from {defaultConfigPath}");
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to load default system prompt from file: {ex.Message}");
            }
            return null;
        }

        private static bool IsDefaultConfigUsable(SystemPromptConfig config)
        {
            if (config == null)
            {
                return false;
            }

            bool hasActions = config.ApiActions != null && config.ApiActions.Count > 0;
            bool hasDecisionRules = config.DecisionRules != null && config.DecisionRules.Count > 0;
            bool hasResponseFormat = config.ResponseFormat != null;
            bool hasJsonTemplate = !string.IsNullOrWhiteSpace(config.ResponseFormat?.JsonTemplate);
            bool hasImportantRules = !string.IsNullOrWhiteSpace(config.ResponseFormat?.ImportantRules);
            bool hasPromptTemplates = config.PromptTemplates != null;
            bool hasPromptPolicy = config.PromptPolicy != null;
            return hasActions && hasDecisionRules && hasResponseFormat && hasJsonTemplate && hasImportantRules && hasPromptTemplates && hasPromptPolicy;
        }

        /// <summary>/// Promptfoldername
 ///</summary>
        public const string PromptFolderName = "Prompt";

        /// <summary>/// 默认configuration子foldername
 ///</summary>
        public const string DefaultSubFolderName = "Default";

        /// <summary>/// 自定义configuration子foldername
 ///</summary>
        public const string CustomSubFolderName = "Custom";

        /// <summary>/// 默认systempromptconfigurationfile名
 ///</summary>
        public const string DefaultConfigFileName = "SystemPrompt_Default.json";

        /// <summary>/// get默认configurationfilepath (Mod目录下的Prompt/Defaultfolder)
 ///</summary>
        private string GetDefaultConfigPath()
        {
            // 尝试从 Mod pathget
            try
            {
                var mod = LoadedModManager.GetMod<RimChatMod>();
                if (mod?.Content != null)
                {
                    string defaultDir = System.IO.Path.Combine(mod.Content.RootDir, PromptFolderName, DefaultSubFolderName);
                    return System.IO.Path.Combine(defaultDir, DefaultConfigFileName);
                }
            }
            catch { }

            // 后备: 使用程序集path
            try
            {
                string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string assemblyDir = System.IO.Path.GetDirectoryName(assemblyPath);
                string modDir = System.IO.Directory.GetParent(assemblyDir)?.Parent?.FullName;
                if (!string.IsNullOrEmpty(modDir))
                {
                    string defaultDir = System.IO.Path.Combine(modDir, PromptFolderName, DefaultSubFolderName);
                    return System.IO.Path.Combine(defaultDir, DefaultConfigFileName);
                }
            }
            catch { }

            // 最终后备
            return System.IO.Path.Combine("E:\\SteamLibrary\\steamapps\\common\\RimWorld\\Mods\\RimChat", PromptFolderName, DefaultSubFolderName, DefaultConfigFileName);
        }

        /// <summary>/// 从另一个configuration复制数据
 ///</summary>
        private void CopyFrom(SystemPromptConfig source)
        {
            ConfigName = source.ConfigName;
            GlobalSystemPrompt = source.GlobalSystemPrompt;
            GlobalDialoguePrompt = source.GlobalDialoguePrompt;
            UseAdvancedMode = source.UseAdvancedMode;
            UseHierarchicalPromptFormat = source.UseHierarchicalPromptFormat;
            Enabled = source.Enabled;
            PromptPolicySchemaVersion = source.PromptPolicySchemaVersion;

            ApiActions.Clear();
            foreach (var action in source.ApiActions)
            {
                ApiActions.Add(action.Clone());
            }

            ResponseFormat = source.ResponseFormat?.Clone() ?? new ResponseFormatConfig();
            EnvironmentPrompt = source.EnvironmentPrompt?.Clone() ?? new EnvironmentPromptConfig();

            DecisionRules.Clear();
            foreach (var rule in source.DecisionRules)
            {
                DecisionRules.Add(rule.Clone());
            }

            DynamicDataInjection = source.DynamicDataInjection?.Clone() ?? new DynamicDataInjectionConfig();
            PromptTemplates = source.PromptTemplates?.Clone() ?? new PromptTemplateTextConfig();
            PromptPolicy = source.PromptPolicy?.Clone() ?? PromptPolicyConfig.CreateDefault();
        }

        /// <summary>/// initialize最小化默认configuration (fileload失败时使用)
 ///</summary>
        private void InitializeMinimalDefaults()
        {
            GlobalSystemPrompt = "请从 SystemPrompt_Default.json 文件加载默认系统提示词配置。";

            ApiActions = new List<ApiActionConfig>
            {
                new ApiActionConfig("adjust_goodwill", "Change faction relations", "amount (int), reason (string)", ""),
                new ApiActionConfig("send_gift", "Send silver to improve relations", "silver (int), goodwill_gain (int)", ""),
                new ApiActionConfig("request_aid", "Request military/medical aid", "type (string)", ""),
                new ApiActionConfig("declare_war", "Declare war", "reason (string)", "Only when relations are already hostile enough for war declaration."),
                new ApiActionConfig("make_peace", "Offer peace treaty", "cost (int, silver)", ""),
                new ApiActionConfig("request_caravan", "Request trade caravan", "goods (string, optional)", "Only when relations are not hostile."),
                new ApiActionConfig("request_raid", PromptTextConstants.RequestRaidActionDescription, PromptTextConstants.RequestRaidActionParametersLegacy, PromptTextConstants.RequestRaidActionRequirement),
                new ApiActionConfig("trigger_incident", "Trigger a game event (incident)", "defName (string), amount (int, optional points)", ""),
                new ApiActionConfig("create_quest", "Create a mission/quest for the player using a native template.", "questDefName (string, REQUIRED: exact name from the dynamic list provided below), askerFaction (string, optional: defaults to current faction), points (int, optional: threat points for the mission)", "You MUST provide a valid questDefName from the approved list exactly as written. Custom quests are NOT allowed."),
                new ApiActionConfig("exit_dialogue", "End the current dialogue session while keeping current presence status", "reason (string, optional)", ""),
                new ApiActionConfig("go_offline", PromptTextConstants.GoOfflineActionDescription, "reason (string, optional)", ""),
                new ApiActionConfig("set_dnd", PromptTextConstants.SetDndActionDescription, "reason (string, optional)", ""),
                new ApiActionConfig("publish_public_post", PromptTextConstants.PublishPublicPostActionDescription, PromptTextConstants.PublishPublicPostActionParameters, PromptTextConstants.PublishPublicPostActionRequirement),
                new ApiActionConfig("reject_request", "Reject player's request", "reason (string)", "Use when you are explicitly declining a concrete player request that should be recorded as a refusal. Do not use for casual disagreement.")
            };

            ApiActions[2].Requirement = "Only when relations are strong enough for aid and the current goodwill meets the aid threshold shown in API limits.";

            ResponseFormat = new ResponseFormatConfig
            {
                JsonTemplate = "{\n  \"actions\": [\n    {\n      \"action\": \"snake_case_action\",\n      \"parameters\": {\n        \"param1\": \"value\"\n      }\n    }\n  ]\n}",
                ImportantRules = "1. Match the user's game language while keeping JSON keys and action names unchanged.\n2. Use only enabled actions and obey requirements, cooldowns, and limits.\n3. Keep natural-language dialogue fully in character; do not mention AI identity or game mechanics. Structured JSON is parser-facing and allowed only when needed.\n4. Mirror the player's brevity when helpful, but keep enough clarity and tone to stay coherent.\n5. If intent is ambiguous, probe cautiously before escalating to suspicion or hostility.\n6. If no gameplay effect is intended, omit JSON entirely."
            };

            DecisionRules = new List<DecisionRuleConfig>
            {
                new DecisionRuleConfig("GoodwillGuideline", "Consider current goodwill level when making decisions"),
                new DecisionRuleConfig("LeaderTraits", "Consider your leader's traits when making decisions")
            };

            EnvironmentPrompt = EnvironmentPromptConfig.CreateDefaultSeed();
            PromptTemplates = new PromptTemplateTextConfig();
            PromptPolicySchemaVersion = CurrentPromptPolicySchemaVersion;
            PromptPolicy = PromptPolicyConfig.CreateDefault();
        }
    }
}
