using System;
using System.Collections.Generic;
using Verse;
using RimDiplomacy.Persistence;
using RimDiplomacy.Core;

namespace RimDiplomacy.Config
{
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

    public class ResponseFormatConfig : IExposable
    {
        public string JsonTemplate;
        public string RelationChangesTemplate;
        public string ImportantRules;
        public bool IncludeRelationChanges;

        public ResponseFormatConfig()
        {
            IncludeRelationChanges = true;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref JsonTemplate, "jsonTemplate", "");
            Scribe_Values.Look(ref RelationChangesTemplate, "relationChangesTemplate", "");
            Scribe_Values.Look(ref ImportantRules, "importantRules", "");
            Scribe_Values.Look(ref IncludeRelationChanges, "includeRelationChanges", true);
        }

        public ResponseFormatConfig Clone()
        {
            return new ResponseFormatConfig
            {
                JsonTemplate = this.JsonTemplate,
                RelationChangesTemplate = this.RelationChangesTemplate,
                ImportantRules = this.ImportantRules,
                IncludeRelationChanges = this.IncludeRelationChanges
            };
        }
    }

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

    public class DynamicDataInjectionConfig : IExposable
    {
        public bool InjectRelationContext;
        public bool InjectMemoryData;
        public bool InjectFiveDimensionData;
        public bool InjectFactionInfo;
        public string CustomInjectionHeader;

        public DynamicDataInjectionConfig()
        {
            InjectRelationContext = true;
            InjectMemoryData = true;
            InjectFiveDimensionData = true;
            InjectFactionInfo = true;
            CustomInjectionHeader = "";
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref InjectRelationContext, "injectRelationContext", true);
            Scribe_Values.Look(ref InjectMemoryData, "injectMemoryData", true);
            Scribe_Values.Look(ref InjectFiveDimensionData, "injectFiveDimensionData", true);
            Scribe_Values.Look(ref InjectFactionInfo, "injectFactionInfo", true);
            Scribe_Values.Look(ref CustomInjectionHeader, "customInjectionHeader", "");
        }

        public DynamicDataInjectionConfig Clone()
        {
            return new DynamicDataInjectionConfig
            {
                InjectRelationContext = this.InjectRelationContext,
                InjectMemoryData = this.InjectMemoryData,
                InjectFiveDimensionData = this.InjectFiveDimensionData,
                InjectFactionInfo = this.InjectFactionInfo,
                CustomInjectionHeader = this.CustomInjectionHeader
            };
        }
    }

    public class SystemPromptConfig : IExposable
    {
        public string ConfigName;
        public string GlobalSystemPrompt;
        public string GlobalDialoguePrompt;
        public bool UseAdvancedMode;

        public List<ApiActionConfig> ApiActions;
        public ResponseFormatConfig ResponseFormat;
        public List<DecisionRuleConfig> DecisionRules;
        public DynamicDataInjectionConfig DynamicDataInjection;

        public bool Enabled;

        public SystemPromptConfig()
        {
            ConfigName = "Default";
            GlobalSystemPrompt = "";
            GlobalDialoguePrompt = "";
            UseAdvancedMode = false;
            Enabled = true;
            ApiActions = new List<ApiActionConfig>();
            ResponseFormat = new ResponseFormatConfig();
            DecisionRules = new List<DecisionRuleConfig>();
            DynamicDataInjection = new DynamicDataInjectionConfig();
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref ConfigName, "configName", "Default");
            Scribe_Values.Look(ref GlobalSystemPrompt, "globalSystemPrompt", "");
            Scribe_Values.Look(ref GlobalDialoguePrompt, "globalDialoguePrompt", "");
            Scribe_Values.Look(ref UseAdvancedMode, "useAdvancedMode", false);
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Collections.Look(ref ApiActions, "apiActions", LookMode.Deep);
            Scribe_Deep.Look(ref ResponseFormat, "responseFormat");
            Scribe_Collections.Look(ref DecisionRules, "decisionRules", LookMode.Deep);
            Scribe_Deep.Look(ref DynamicDataInjection, "dynamicDataInjection");
        }

        public SystemPromptConfig Clone()
        {
            var clone = new SystemPromptConfig
            {
                ConfigName = this.ConfigName,
                GlobalSystemPrompt = this.GlobalSystemPrompt,
                GlobalDialoguePrompt = this.GlobalDialoguePrompt,
                UseAdvancedMode = this.UseAdvancedMode,
                Enabled = this.Enabled,
                ResponseFormat = this.ResponseFormat?.Clone() ?? new ResponseFormatConfig(),
                DynamicDataInjection = this.DynamicDataInjection?.Clone() ?? new DynamicDataInjectionConfig()
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
            // 尝试从默认配置文件加载
            var defaultConfig = LoadDefaultConfigFromFile();
            if (defaultConfig != null)
            {
                CopyFrom(defaultConfig);
                return;
            }

            // 如果文件加载失败，使用最小化默认配置
            InitializeMinimalDefaults();
        }

        /// <summary>
        /// 从 SystemPrompt_Default.json 文件加载默认配置
        /// </summary>
        private SystemPromptConfig LoadDefaultConfigFromFile()
        {
            try
            {
                string defaultConfigPath = GetDefaultConfigPath();
                if (System.IO.File.Exists(defaultConfigPath))
                {
                    string json = System.IO.File.ReadAllText(defaultConfigPath);
                    // 使用 PromptPersistenceService 的解析方法
                    var config = PromptPersistenceService.Instance?.ParseJsonToConfigInternal(json);
                    if (config != null)
                    {
                        Log.Message($"[RimDiplomacy] Loaded default system prompt from {defaultConfigPath}");
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimDiplomacy] Failed to load default system prompt from file: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Prompt文件夹名称
        /// </summary>
        public const string PromptFolderName = "Prompt";

        /// <summary>
        /// 默认配置子文件夹名称
        /// </summary>
        public const string DefaultSubFolderName = "Default";

        /// <summary>
        /// 自定义配置子文件夹名称
        /// </summary>
        public const string CustomSubFolderName = "Custom";

        /// <summary>
        /// 默认系统提示词配置文件名
        /// </summary>
        public const string DefaultConfigFileName = "SystemPrompt_Default.json";

        /// <summary>
        /// 获取默认配置文件路径（Mod目录下的Prompt/Default文件夹）
        /// </summary>
        private string GetDefaultConfigPath()
        {
            // 尝试从 Mod 路径获取
            try
            {
                var mod = LoadedModManager.GetMod<RimDiplomacyMod>();
                if (mod?.Content != null)
                {
                    string defaultDir = System.IO.Path.Combine(mod.Content.RootDir, PromptFolderName, DefaultSubFolderName);
                    return System.IO.Path.Combine(defaultDir, DefaultConfigFileName);
                }
            }
            catch { }

            // 后备：使用程序集路径
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
            return System.IO.Path.Combine("E:\\SteamLibrary\\steamapps\\common\\RimWorld\\Mods\\RimDiplomacy", PromptFolderName, DefaultSubFolderName, DefaultConfigFileName);
        }

        /// <summary>
        /// 从另一个配置复制数据
        /// </summary>
        private void CopyFrom(SystemPromptConfig source)
        {
            ConfigName = source.ConfigName;
            GlobalSystemPrompt = source.GlobalSystemPrompt;
            GlobalDialoguePrompt = source.GlobalDialoguePrompt;
            UseAdvancedMode = source.UseAdvancedMode;
            Enabled = source.Enabled;

            ApiActions.Clear();
            foreach (var action in source.ApiActions)
            {
                ApiActions.Add(action.Clone());
            }

            ResponseFormat = source.ResponseFormat?.Clone() ?? new ResponseFormatConfig();

            DecisionRules.Clear();
            foreach (var rule in source.DecisionRules)
            {
                DecisionRules.Add(rule.Clone());
            }

            DynamicDataInjection = source.DynamicDataInjection?.Clone() ?? new DynamicDataInjectionConfig();
        }

        /// <summary>
        /// 初始化最小化默认配置（文件加载失败时使用）
        /// </summary>
        private void InitializeMinimalDefaults()
        {
            GlobalSystemPrompt = "请从 SystemPrompt_Default.json 文件加载默认系统提示词配置。";

            ApiActions = new List<ApiActionConfig>
            {
                new ApiActionConfig("adjust_goodwill", "Change faction relations", "amount (int), reason (string)", ""),
                new ApiActionConfig("send_gift", "Send silver to improve relations", "silver (int), goodwill_gain (int)", ""),
                new ApiActionConfig("request_aid", "Request military/medical aid", "type (string)", ""),
                new ApiActionConfig("declare_war", "Declare war", "reason (string)", ""),
                new ApiActionConfig("make_peace", "Offer peace treaty", "cost (int, silver)", ""),
                new ApiActionConfig("request_caravan", "Request trade caravan", "goods (string, optional)", "not hostile"),
                new ApiActionConfig("request_raid", "Launch a raid against the player (delayed arrival). Use this when insulted, threatened, or as a tactical decision during hostilities.", "strategy (string: 'ImmediateAttack' or 'Siege'), arrival (string: 'EdgeWalkIn' or 'CenterDrop')", "faction is hostile to player"),
                new ApiActionConfig("trigger_incident", "Trigger a game event (incident)", "defName (string), amount (int, optional points)", ""),
                new ApiActionConfig("create_quest", "Create a mission/quest for the player using a native template.", "questDefName (string, REQUIRED: e.g. 'ThreatReward_Raid_MiscReward'), askerFaction (string, optional: defaults to current faction), points (int, optional: threat points for the mission)", "You MUST provide a valid questDefName from the approved list. Custom quests without a template are not allowed."),
                new ApiActionConfig("reject_request", "Reject player's request", "reason (string)", "")
            };

            ResponseFormat = new ResponseFormatConfig
            {
                JsonTemplate = "{\n  \"action\": \"action_name\",\n  \"parameters\": {},\n  \"response\": \"Your response here\"\n}",
                IncludeRelationChanges = true
            };

            DecisionRules = new List<DecisionRuleConfig>
            {
                new DecisionRuleConfig("GoodwillGuideline", "Consider current goodwill level when making decisions"),
                new DecisionRuleConfig("LeaderTraits", "Consider your leader's traits when making decisions")
            };
        }
    }
}
