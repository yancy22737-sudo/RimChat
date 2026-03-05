using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using RimDiplomacy.Config;
using RimDiplomacy.Memory;
using RimDiplomacy.Relation;
using RimDiplomacy.DiplomacySystem;
using RimDiplomacy.Core;
using RimDiplomacy.Util;

namespace RimDiplomacy.Persistence
{
    public class PromptPersistenceService : IPromptPersistenceService
    {
        private static PromptPersistenceService _instance;
        public static PromptPersistenceService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new PromptPersistenceService();
                }
                return _instance;
            }
        }

        private const string CONFIG_FILE_NAME = "system_prompt_config.json";

        private SystemPromptConfig _cachedConfig;
        private bool _isInitialized;

        /// <summary>
        /// Prompt文件夹名称
        /// </summary>
        public const string PromptFolderName = "Prompt";

        /// <summary>
        /// 自定义配置子文件夹名称
        /// </summary>
        public const string CustomSubFolderName = "Custom";

        /// <summary>
        /// 获取自定义配置基础路径（Mod目录下的Prompt/Custom文件夹）
        /// </summary>
        public string BasePath
        {
            get
            {
                // 优先使用Mod目录下的Prompt/Custom文件夹
                try
                {
                    var mod = LoadedModManager.GetMod<RimDiplomacyMod>();
                    if (mod?.Content != null)
                    {
                        string customDir = Path.Combine(mod.Content.RootDir, PromptFolderName, CustomSubFolderName);
                        // 确保目录存在
                        if (!Directory.Exists(customDir))
                        {
                            Directory.CreateDirectory(customDir);
                        }
                        return customDir;
                    }
                }
                catch { }

                // 后备：使用用户数据目录
                string fallbackDir = Path.Combine(GenFilePaths.SaveDataFolderPath, "RimDiplomacy", "prompts");
                if (!Directory.Exists(fallbackDir))
                {
                    Directory.CreateDirectory(fallbackDir);
                }
                return fallbackDir;
            }
        }

        public string ConfigFilePath
        {
            get
            {
                return Path.Combine(BasePath, CONFIG_FILE_NAME);
            }
        }

        public void Initialize()
        {
            if (_isInitialized) return;

            try
            {
                EnsureDirectoryExists();
                _cachedConfig = LoadConfig();
                _isInitialized = true;
                Log.Message($"[RimDiplomacy] PromptPersistenceService initialized, config path: {ConfigFilePath}");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimDiplomacy] Failed to initialize PromptPersistenceService: {ex}");
                _cachedConfig = CreateDefaultConfig();
            }
        }

        public string GetConfigFilePath()
        {
            return ConfigFilePath;
        }

        public bool ConfigExists()
        {
            return File.Exists(ConfigFilePath);
        }

        public SystemPromptConfig LoadConfig()
        {
            try
            {
                EnsureDirectoryExists();

                if (File.Exists(ConfigFilePath))
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    var config = ParseJsonToConfigInternal(json);

                    if (config != null)
                    {
                        _cachedConfig = config;
                        
                        // 迁移逻辑：确保包含 request_raid 且描述最新
                        if (config.ApiActions == null)
                        {
                            config.ApiActions = new List<ApiActionConfig>();
                        }

                        bool needsSave = false;
                        var raidAction = config.ApiActions.FirstOrDefault(a => a.ActionName == "request_raid");
                        
                        if (raidAction == null)
                        {
                            Log.Message("[RimDiplomacy] Migrating config: Adding request_raid action...");
                            int insertIndex = config.ApiActions.FindIndex(a => a.ActionName == "reject_request");
                            if (insertIndex == -1) insertIndex = config.ApiActions.Count;

                            config.ApiActions.Insert(insertIndex, new ApiActionConfig(
                                "request_raid", 
                                "Launch a raid against the player (delayed arrival). Use this when insulted, threatened, or as a tactical decision during hostilities.", 
                                "strategy (string: 'ImmediateAttack', 'ImmediateAttackSmart', 'StageThenAttack', 'ImmediateAttackSappers', or 'Siege'), arrival (string: 'EdgeWalkIn', 'EdgeDrop', 'EdgeWalkInGroups', 'RandomDrop', or 'CenterDrop')", 
                                "faction is hostile to player"
                            ));
                            needsSave = true;
                        }
                        else if (string.IsNullOrEmpty(raidAction.Requirement) || raidAction.Parameters.Contains("'ImmediateAttack' or 'Siege'"))
                        {
                            Log.Message("[RimDiplomacy] Migrating config: Updating request_raid metadata...");
                            raidAction.Description = "Launch a raid against the player (delayed arrival). Use this when insulted, threatened, or as a tactical decision during hostilities.";
                            raidAction.Parameters = "strategy (string: 'ImmediateAttack', 'ImmediateAttackSmart', 'StageThenAttack', 'ImmediateAttackSappers', or 'Siege'), arrival (string: 'EdgeWalkIn', 'EdgeDrop', 'EdgeWalkInGroups', 'RandomDrop', or 'CenterDrop')";
                            raidAction.Requirement = "faction is hostile to player";
                            needsSave = true;
                        }

                        // 确保 request_caravan 也有 Requirement
                        var caravanAction = config.ApiActions.FirstOrDefault(a => a.ActionName == "request_caravan");
                        if (caravanAction != null && string.IsNullOrEmpty(caravanAction.Requirement))
                        {
                            caravanAction.Requirement = "not hostile";
                            needsSave = true;
                        }

                        // 新增：迁移 trigger_incident 和 create_quest
                        if (config.ApiActions.All(a => a.ActionName != "trigger_incident"))
                        {
                            Log.Message("[RimDiplomacy] Migrating config: Adding trigger_incident action...");
                            int insertIndex = config.ApiActions.FindIndex(a => a.ActionName == "reject_request");
                            if (insertIndex == -1) insertIndex = config.ApiActions.Count;
                            config.ApiActions.Insert(insertIndex, new ApiActionConfig("trigger_incident", "Trigger a game event (incident)", "defName (string), amount (int, optional points)", ""));
                            needsSave = true;
                        }

                        if (config.ApiActions.All(a => a.ActionName != "create_quest"))
                        {
                            Log.Message("[RimDiplomacy] Migrating config: Adding create_quest action...");
                            int insertIndex = config.ApiActions.FindIndex(a => a.ActionName == "reject_request");
                            if (insertIndex == -1) insertIndex = config.ApiActions.Count;
                            config.ApiActions.Insert(insertIndex, new ApiActionConfig("create_quest", "Offer a custom mission/quest to the player", "title (string), description (string), rewardDescription (string), callbackId (string)", ""));
                            needsSave = true;
                        }

                        var createQuestAction = config.ApiActions.FirstOrDefault(a => a.ActionName == "create_quest");
                        if (createQuestAction != null && (string.IsNullOrEmpty(createQuestAction.Requirement) || !createQuestAction.Parameters.Contains("REQUIRED")))
                        {
                            Log.Message("[RimDiplomacy] Migrating config: Updating create_quest to strict template mode...");
                            createQuestAction.Description = "Create a mission/quest for the player using a native template.";
                            createQuestAction.Parameters = "questDefName (string, REQUIRED: e.g. 'ThreatReward_Raid_MiscReward'), askerFaction (string, optional: defaults to current faction), points (int, optional: threat points for the mission)";
                            createQuestAction.Requirement = "You MUST provide a valid questDefName from the approved list. Custom quests without a template are not allowed.";
                            needsSave = true;
                        }

                        if (needsSave)
                        {
                            SaveConfig(config); 
                            Log.Message("[RimDiplomacy] Config migration completed and saved.");
                        }

                        Log.Message($"[RimDiplomacy] Loaded SystemPromptConfig from file");
                        return config;
                    }
                }

                Log.Message($"[RimDiplomacy] Config file not found, creating default config");
                _cachedConfig = CreateDefaultConfig();
                SaveConfig(_cachedConfig);
                return _cachedConfig;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimDiplomacy] Failed to load config: {ex}");
                return _cachedConfig ?? CreateDefaultConfig();
            }
        }

        public void SaveConfig(SystemPromptConfig config)
        {
            try
            {
                EnsureDirectoryExists();

                if (config == null)
                {
                    Log.Warning("[RimDiplomacy] Attempted to save null config");
                    return;
                }

                string json = SerializeConfigToJson(config);
                File.WriteAllText(ConfigFilePath, json);
                _cachedConfig = config;

                Log.Message($"[RimDiplomacy] Saved SystemPromptConfig to: {ConfigFilePath}");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimDiplomacy] Failed to save config: {ex}");
            }
        }

        public void ResetToDefault()
        {
            try
            {
                _cachedConfig = CreateDefaultConfig();
                SaveConfig(_cachedConfig);
                Log.Message("[RimDiplomacy] Reset SystemPromptConfig to default");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimDiplomacy] Failed to reset config: {ex}");
            }
        }

        public bool ExportConfig(string filePath)
        {
            try
            {
                if (_cachedConfig == null)
                {
                    _cachedConfig = LoadConfig();
                }

                string json = SerializeConfigToJson(_cachedConfig, true);
                File.WriteAllText(filePath, json);
                Log.Message($"[RimDiplomacy] Exported config to: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimDiplomacy] Failed to export config: {ex}");
                return false;
            }
        }

        public bool ImportConfig(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Log.Warning($"[RimDiplomacy] Import file not found: {filePath}");
                    return false;
                }

                string json = File.ReadAllText(filePath);
                var config = ParseJsonToConfigInternal(json);

                if (config != null)
                {
                    SaveConfig(config);
                    Log.Message($"[RimDiplomacy] Imported config from: {filePath}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimDiplomacy] Failed to import config: {ex}");
                return false;
            }
        }

        public string BuildFullSystemPrompt(Faction faction, SystemPromptConfig config)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(config.GlobalSystemPrompt))
            {
                sb.AppendLine("=== GLOBAL SYSTEM PROMPT ===");
                sb.AppendLine(config.GlobalSystemPrompt);
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(config.GlobalDialoguePrompt))
            {
                sb.AppendLine("=== DIALOGUE PROMPT ===");
                sb.AppendLine(config.GlobalDialoguePrompt);
                sb.AppendLine();
            }

            string factionPrompt = FactionPromptManager.Instance.GetPrompt(faction.def?.defName);
            if (!string.IsNullOrEmpty(factionPrompt))
            {
                sb.AppendLine("=== FACTION CHARACTERISTICS ===");
                sb.AppendLine(factionPrompt);
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("You are the leader of a faction in RimWorld.");
                sb.AppendLine();
            }

            if (config.DynamicDataInjection != null)
            {
                // Prevent duplicate relation data injection: AppendRelationContext outputs 5-dim data which overlaps with AppendFiveDimensionData
                if (config.DynamicDataInjection.InjectRelationContext && !config.DynamicDataInjection.InjectFiveDimensionData)
                {
                    AppendRelationContext(sb, faction);
                }

                if (config.DynamicDataInjection.InjectMemoryData)
                {
                    AppendMemoryData(sb, faction);
                }

                if (config.DynamicDataInjection.InjectFiveDimensionData)
                {
                    AppendFiveDimensionData(sb, faction);
                }

                if (config.DynamicDataInjection.InjectFactionInfo)
                {
                    AppendFactionInfo(sb, faction);
                }
            }

            AppendApiLimits(sb, faction);

            if (config.UseAdvancedMode)
            {
                AppendAdvancedConfig(sb, config);
            }
            else
            {
                AppendSimpleConfig(sb, config);
            }

            return sb.ToString();
        }

        public string BuildRPGFullSystemPrompt(Pawn initiator, Pawn target)
        {
            var sb = new StringBuilder();
            var settings = RimDiplomacyMod.Settings;

            // 1. RPG Role Setting (AI Persona)
            if (!string.IsNullOrEmpty(settings.RPGRoleSetting))
            {
                sb.AppendLine("=== ROLE SETTING ===");
                sb.AppendLine(settings.RPGRoleSetting);
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine($"You are roleplaying as {target.LabelShort} in RimWorld.");
            }

            // 2. RPG Dialogue Style
            if (!string.IsNullOrEmpty(settings.RPGDialogueStyle))
            {
                sb.AppendLine("=== DIALOGUE STYLE ===");
                sb.AppendLine(settings.RPGDialogueStyle);
                sb.AppendLine();
            }

            // 3. Dynamic Data Injection
            if (settings.RPGInjectSelfStatus)
            {
                AppendRPGPawnInfo(sb, target, true); // YOU (AI)
            }
            
            if (settings.RPGInjectInterlocutorStatus)
            {
                AppendRPGPawnInfo(sb, initiator, false); // INTERLOCUTOR (Player)
            }

            if (settings.RPGInjectPsychologicalAssessment)
            {
                AppendRPGRelationData(sb, initiator, target);
            }

            if (settings.RPGInjectFactionBackground)
            {
                AppendRPGFactionContext(sb, target);
                if (initiator.Faction != target.Faction)
                {
                    AppendRPGFactionContext(sb, initiator);
                }
            }

            // 4. API Actions and Format
            if (settings.EnableRPGAPI)
            {
                // API Guidelines (Actions definitions)
                if (!string.IsNullOrEmpty(settings.RPGApiGuidelines))
                {
                    sb.AppendLine("=== API GUIDELINES ===");
                    sb.AppendLine(settings.RPGApiGuidelines);
                    sb.AppendLine();
                }
                else
                {
                    AppendRPGApiDefinitions(sb);
                }
                
                // Format Constraint (JSON output requirements)
                if (!string.IsNullOrEmpty(settings.RPGFormatConstraint))
                {
                    sb.AppendLine("=== FORMAT CONSTRAINT (REQUIRED) ===");
                    sb.AppendLine(settings.RPGFormatConstraint);
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        private void EnsureDirectoryExists()
        {
            try
            {
                if (!Directory.Exists(BasePath))
                {
                    Directory.CreateDirectory(BasePath);
                    Log.Message($"[RimDiplomacy] Created prompt directory: {BasePath}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimDiplomacy] Failed to create directory: {ex}");
            }
        }

        private SystemPromptConfig CreateDefaultConfig()
        {
            var config = new SystemPromptConfig();
            config.InitializeDefaults();
            return config;
        }

        private string SerializeConfigToJson(SystemPromptConfig config, bool prettyPrint = false)
        {
            var sb = new StringBuilder();

            if (prettyPrint)
            {
                sb.AppendLine("{");
                sb.AppendLine($"  \"ConfigName\": \"{EscapeJson(config.ConfigName)}\",");
                sb.AppendLine($"  \"GlobalSystemPrompt\": \"{EscapeJson(config.GlobalSystemPrompt)}\",");
                sb.AppendLine($"  \"GlobalDialoguePrompt\": \"{EscapeJson(config.GlobalDialoguePrompt)}\",");
                sb.AppendLine($"  \"UseAdvancedMode\": {config.UseAdvancedMode.ToString().ToLower()},");
                sb.AppendLine($"  \"Enabled\": {config.Enabled.ToString().ToLower()},");
            }
            else
            {
                sb.Append("{");
                sb.Append($"\"ConfigName\":\"{EscapeJson(config.ConfigName)}\",");
                sb.Append($"\"GlobalSystemPrompt\":\"{EscapeJson(config.GlobalSystemPrompt)}\",");
                sb.Append($"\"GlobalDialoguePrompt\":\"{EscapeJson(config.GlobalDialoguePrompt)}\",");
                sb.Append($"\"UseAdvancedMode\":{config.UseAdvancedMode.ToString().ToLower()},");
                sb.Append($"\"Enabled\":{config.Enabled.ToString().ToLower()},");
            }

            SerializeApiActions(sb, config.ApiActions, prettyPrint);
            SerializeResponseFormat(sb, config.ResponseFormat, prettyPrint);
            SerializeDecisionRules(sb, config.DecisionRules, prettyPrint);
            SerializeDynamicDataInjection(sb, config.DynamicDataInjection, prettyPrint);

            if (prettyPrint)
            {
                sb.AppendLine();
                sb.Append("}");
            }
            else
            {
                sb.Append("}");
            }

            return sb.ToString();
        }

        private void SerializeApiActions(StringBuilder sb, List<ApiActionConfig> actions, bool prettyPrint)
        {
            if (prettyPrint)
            {
                sb.AppendLine();
                sb.AppendLine("  \"ApiActions\": [");
                for (int i = 0; i < actions.Count; i++)
                {
                    var action = actions[i];
                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"ActionName\": \"{EscapeJson(action.ActionName)}\",");
                    sb.AppendLine($"      \"Description\": \"{EscapeJson(action.Description)}\",");
                    sb.AppendLine($"      \"Parameters\": \"{EscapeJson(action.Parameters)}\",");
                    sb.AppendLine($"      \"Requirement\": \"{EscapeJson(action.Requirement)}\",");
                    sb.AppendLine($"      \"IsEnabled\": {action.IsEnabled.ToString().ToLower()}");
                    sb.Append(i < actions.Count - 1 ? "    }," : "    }");
                    sb.AppendLine();
                }
                sb.Append("  ],");
            }
            else
            {
                sb.Append(",\"ApiActions\":[");
                for (int i = 0; i < actions.Count; i++)
                {
                    var action = actions[i];
                    sb.Append("{");
                    sb.Append($"\"ActionName\":\"{EscapeJson(action.ActionName)}\",");
                    sb.Append($"\"Description\":\"{EscapeJson(action.Description)}\",");
                    sb.Append($"\"Parameters\":\"{EscapeJson(action.Parameters)}\",");
                    sb.Append($"\"Requirement\":\"{EscapeJson(action.Requirement)}\",");
                    sb.Append($"\"IsEnabled\":{action.IsEnabled.ToString().ToLower()}");
                    sb.Append(i < actions.Count - 1 ? "}," : "}");
                }
                sb.Append("],");
            }
        }

        private void SerializeResponseFormat(StringBuilder sb, ResponseFormatConfig format, bool prettyPrint)
        {
            if (format == null) return;

            if (prettyPrint)
            {
                sb.AppendLine();
                sb.AppendLine("  \"ResponseFormat\": {");
                sb.AppendLine($"    \"JsonTemplate\": \"{EscapeJson(format.JsonTemplate)}\",");
                sb.AppendLine($"    \"RelationChangesTemplate\": \"{EscapeJson(format.RelationChangesTemplate)}\",");
                sb.AppendLine($"    \"ImportantRules\": \"{EscapeJson(format.ImportantRules)}\",");
                sb.AppendLine($"    \"IncludeRelationChanges\": {format.IncludeRelationChanges.ToString().ToLower()}");
                sb.Append("  },");
            }
            else
            {
                sb.Append(",\"ResponseFormat\":{");
                sb.Append($"\"JsonTemplate\":\"{EscapeJson(format.JsonTemplate)}\",");
                sb.Append($"\"RelationChangesTemplate\":\"{EscapeJson(format.RelationChangesTemplate)}\",");
                sb.Append($"\"ImportantRules\":\"{EscapeJson(format.ImportantRules)}\",");
                sb.Append($"\"IncludeRelationChanges\":{format.IncludeRelationChanges.ToString().ToLower()}");
                sb.Append("},");
            }
        }

        private void SerializeDecisionRules(StringBuilder sb, List<DecisionRuleConfig> rules, bool prettyPrint)
        {
            if (prettyPrint)
            {
                sb.AppendLine();
                sb.AppendLine("  \"DecisionRules\": [");
                for (int i = 0; i < rules.Count; i++)
                {
                    var rule = rules[i];
                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"RuleName\": \"{EscapeJson(rule.RuleName)}\",");
                    sb.AppendLine($"      \"RuleContent\": \"{EscapeJson(rule.RuleContent)}\",");
                    sb.AppendLine($"      \"IsEnabled\": {rule.IsEnabled.ToString().ToLower()}");
                    sb.Append(i < rules.Count - 1 ? "    }," : "    }");
                    sb.AppendLine();
                }
                sb.Append("  ],");
            }
            else
            {
                sb.Append(",\"DecisionRules\":[");
                for (int i = 0; i < rules.Count; i++)
                {
                    var rule = rules[i];
                    sb.Append("{");
                    sb.Append($"\"RuleName\":\"{EscapeJson(rule.RuleName)}\",");
                    sb.Append($"\"RuleContent\":\"{EscapeJson(rule.RuleContent)}\",");
                    sb.Append($"\"IsEnabled\":{rule.IsEnabled.ToString().ToLower()}");
                    sb.Append(i < rules.Count - 1 ? "}," : "}");
                }
                sb.Append("],");
            }
        }

        private void SerializeDynamicDataInjection(StringBuilder sb, DynamicDataInjectionConfig config, bool prettyPrint)
        {
            if (config == null) return;

            if (prettyPrint)
            {
                sb.AppendLine();
                sb.AppendLine("  \"DynamicDataInjection\": {");
                sb.AppendLine($"    \"InjectRelationContext\": {config.InjectRelationContext.ToString().ToLower()},");
                sb.AppendLine($"    \"InjectMemoryData\": {config.InjectMemoryData.ToString().ToLower()},");
                sb.AppendLine($"    \"InjectFiveDimensionData\": {config.InjectFiveDimensionData.ToString().ToLower()},");
                sb.AppendLine($"    \"InjectFactionInfo\": {config.InjectFactionInfo.ToString().ToLower()},");
                sb.AppendLine($"    \"CustomInjectionHeader\": \"{EscapeJson(config.CustomInjectionHeader)}\"");
                sb.Append("  }");
            }
            else
            {
                sb.Append(",\"DynamicDataInjection\":{");
                sb.Append($"\"InjectRelationContext\":{config.InjectRelationContext.ToString().ToLower()},");
                sb.Append($"\"InjectMemoryData\":{config.InjectMemoryData.ToString().ToLower()},");
                sb.Append($"\"InjectFiveDimensionData\":{config.InjectFiveDimensionData.ToString().ToLower()},");
                sb.Append($"\"InjectFactionInfo\":{config.InjectFactionInfo.ToString().ToLower()},");
                sb.Append($"\"CustomInjectionHeader\":\"{EscapeJson(config.CustomInjectionHeader)}\"");
                sb.Append("}");
            }
        }

        /// <summary>
        /// 解析 JSON 字符串为 SystemPromptConfig（内部使用）
        /// </summary>
        internal SystemPromptConfig ParseJsonToConfigInternal(string json)
        {
            var config = new SystemPromptConfig();

            try
            {
                config.ConfigName = ExtractString(json, "ConfigName");
                config.GlobalSystemPrompt = ExtractString(json, "GlobalSystemPrompt");
                config.GlobalDialoguePrompt = ExtractString(json, "GlobalDialoguePrompt");

                string useAdvancedStr = ExtractValue(json, "UseAdvancedMode");
                if (bool.TryParse(useAdvancedStr, out bool useAdvanced))
                {
                    config.UseAdvancedMode = useAdvanced;
                }

                string enabledStr = ExtractValue(json, "Enabled");
                if (bool.TryParse(enabledStr, out bool enabled))
                {
                    config.Enabled = enabled;
                }

                ParseApiActions(json, config);
                ParseResponseFormat(json, config);
                ParseDecisionRules(json, config);
                ParseDynamicDataInjection(json, config);

                return config;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimDiplomacy] Failed to parse config JSON: {ex.Message}");
                return null;
            }
        }

        private void ParseApiActions(string json, SystemPromptConfig config)
        {
            int actionsStart = json.IndexOf("\"ApiActions\":");
            if (actionsStart < 0) return;

            int arrayStart = json.IndexOf("[", actionsStart);
            if (arrayStart < 0) return;

            int depth = 1;
            int arrayEnd = arrayStart + 1;
            while (arrayEnd < json.Length && depth > 0)
            {
                if (json[arrayEnd] == '[') depth++;
                else if (json[arrayEnd] == ']') depth--;
                arrayEnd++;
            }

            string arrayContent = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 2);
            var objects = SplitJsonObjects(arrayContent);

            foreach (var objStr in objects)
            {
                var action = new ApiActionConfig
                {
                    ActionName = ExtractString(objStr, "ActionName"),
                    Description = ExtractString(objStr, "Description"),
                    Parameters = ExtractString(objStr, "Parameters"),
                    Requirement = ExtractString(objStr, "Requirement")
                };

                string enabledStr = ExtractValue(objStr, "IsEnabled");
                if (bool.TryParse(enabledStr, out bool isEnabled))
                {
                    action.IsEnabled = isEnabled;
                }

                config.ApiActions.Add(action);
            }
        }

        private void ParseResponseFormat(string json, SystemPromptConfig config)
        {
            int formatStart = json.IndexOf("\"ResponseFormat\":");
            if (formatStart < 0) return;

            int objStart = json.IndexOf("{", formatStart);
            if (objStart < 0) return;

            int depth = 1;
            int objEnd = objStart + 1;
            while (objEnd < json.Length && depth > 0)
            {
                if (json[objEnd] == '{') depth++;
                else if (json[objEnd] == '}') depth--;
                objEnd++;
            }

            string objContent = json.Substring(objStart, objEnd - objStart);

            config.ResponseFormat = new ResponseFormatConfig
            {
                JsonTemplate = ExtractString(objContent, "JsonTemplate"),
                RelationChangesTemplate = ExtractString(objContent, "RelationChangesTemplate"),
                ImportantRules = ExtractString(objContent, "ImportantRules")
            };

            string includeStr = ExtractValue(objContent, "IncludeRelationChanges");
            if (bool.TryParse(includeStr, out bool include))
            {
                config.ResponseFormat.IncludeRelationChanges = include;
            }
        }

        private void ParseDecisionRules(string json, SystemPromptConfig config)
        {
            int rulesStart = json.IndexOf("\"DecisionRules\":");
            if (rulesStart < 0) return;

            int arrayStart = json.IndexOf("[", rulesStart);
            if (arrayStart < 0) return;

            int depth = 1;
            int arrayEnd = arrayStart + 1;
            while (arrayEnd < json.Length && depth > 0)
            {
                if (json[arrayEnd] == '[') depth++;
                else if (json[arrayEnd] == ']') depth--;
                arrayEnd++;
            }

            string arrayContent = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 2);
            var objects = SplitJsonObjects(arrayContent);

            foreach (var objStr in objects)
            {
                var rule = new DecisionRuleConfig
                {
                    RuleName = ExtractString(objStr, "RuleName"),
                    RuleContent = ExtractString(objStr, "RuleContent")
                };

                string enabledStr = ExtractValue(objStr, "IsEnabled");
                if (bool.TryParse(enabledStr, out bool isEnabled))
                {
                    rule.IsEnabled = isEnabled;
                }

                config.DecisionRules.Add(rule);
            }
        }

        private void ParseDynamicDataInjection(string json, SystemPromptConfig config)
        {
            int injectionStart = json.IndexOf("\"DynamicDataInjection\":");
            if (injectionStart < 0) return;

            int objStart = json.IndexOf("{", injectionStart);
            if (objStart < 0) return;

            int depth = 1;
            int objEnd = objStart + 1;
            while (objEnd < json.Length && depth > 0)
            {
                if (json[objEnd] == '{') depth++;
                else if (json[objEnd] == '}') depth--;
                objEnd++;
            }

            string objContent = json.Substring(objStart, objEnd - objStart);

            config.DynamicDataInjection = new DynamicDataInjectionConfig
            {
                CustomInjectionHeader = ExtractString(objContent, "CustomInjectionHeader")
            };

            string injectRelationStr = ExtractValue(objContent, "InjectRelationContext");
            if (bool.TryParse(injectRelationStr, out bool injectRelation))
            {
                config.DynamicDataInjection.InjectRelationContext = injectRelation;
            }

            string injectMemoryStr = ExtractValue(objContent, "InjectMemoryData");
            if (bool.TryParse(injectMemoryStr, out bool injectMemory))
            {
                config.DynamicDataInjection.InjectMemoryData = injectMemory;
            }

            string injectFiveDimStr = ExtractValue(objContent, "InjectFiveDimensionData");
            if (bool.TryParse(injectFiveDimStr, out bool injectFiveDim))
            {
                config.DynamicDataInjection.InjectFiveDimensionData = injectFiveDim;
            }

            string injectFactionStr = ExtractValue(objContent, "InjectFactionInfo");
            if (bool.TryParse(injectFactionStr, out bool injectFaction))
            {
                config.DynamicDataInjection.InjectFactionInfo = injectFaction;
            }
        }

        private List<string> SplitJsonObjects(string arrayContent)
        {
            var objects = new List<string>();
            int depth = 0;
            int start = 0;

            for (int i = 0; i < arrayContent.Length; i++)
            {
                char c = arrayContent[i];

                if (c == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        objects.Add(arrayContent.Substring(start, i - start + 1));
                    }
                }
                else if (c == '"')
                {
                    i++;
                    while (i < arrayContent.Length && arrayContent[i] != '"')
                    {
                        if (arrayContent[i] == '\\' && i + 1 < arrayContent.Length)
                        {
                            i += 2;
                        }
                        else
                        {
                            i++;
                        }
                    }
                }
            }

            return objects;
        }

        private string ExtractString(string json, string key)
        {
            string pattern = $"\"{key}\":";
            int index = json.IndexOf(pattern);
            if (index < 0) return "";

            int start = json.IndexOf("\"", index + pattern.Length);
            if (start < 0) return "";

            start++;
            var sb = new StringBuilder();

            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"')
                {
                    break;
                }
                else if (c == '\\' && i + 1 < json.Length)
                {
                    char next = json[i + 1];
                    switch (next)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(next); break;
                    }
                    i++;
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private string ExtractValue(string json, string key)
        {
            string pattern = $"\"{key}\":";
            int index = json.IndexOf(pattern);
            if (index < 0) return "";

            int start = index + pattern.Length;
            int end = json.IndexOfAny(new[] { ',', '}', ']' }, start);
            if (end < 0) end = json.Length;

            return json.Substring(start, end - start).Trim();
        }

        private string EscapeJson(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        private void AppendRelationContext(StringBuilder sb, Faction faction)
        {
            try
            {
                var memoryManager = LeaderMemoryManager.Instance;
                if (memoryManager == null) return;

                var leaderMemory = memoryManager.GetMemory(faction);
                if (leaderMemory == null) return;

                var relations = leaderMemory.GetOrCreatePlayerRelations();

                sb.AppendLine("=== RELATIONSHIP VALUES (5-DIMENSION ASSESSMENT) ===");
                sb.AppendLine("These values represent how you feel about the player faction:");
                sb.AppendLine();

                sb.AppendLine($"1. TRUST: {relations.Trust:F0}/100");
                sb.AppendLine($"   Level: {FactionRelationContext.GetTrustLevelDescription(relations.Trust)}");
                sb.AppendLine($"   Meaning: {FactionRelationContext.GetTrustImplication(relations.Trust)}");
                sb.AppendLine();

                sb.AppendLine($"2. INTIMACY: {relations.Intimacy:F0}/100");
                sb.AppendLine($"   Level: {FactionRelationContext.GetIntimacyLevelDescription(relations.Intimacy)}");
                sb.AppendLine($"   Meaning: {FactionRelationContext.GetIntimacyImplication(relations.Intimacy)}");
                sb.AppendLine();

                sb.AppendLine($"3. RECIPROCITY: {relations.Reciprocity:F0}/100");
                sb.AppendLine($"   Level: {FactionRelationContext.GetReciprocityLevelDescription(relations.Reciprocity)}");
                sb.AppendLine($"   Meaning: {FactionRelationContext.GetReciprocityImplication(relations.Reciprocity)}");
                sb.AppendLine();

                sb.AppendLine($"4. RESPECT: {relations.Respect:F0}/100");
                sb.AppendLine($"   Level: {FactionRelationContext.GetRespectLevelDescription(relations.Respect)}");
                sb.AppendLine($"   Meaning: {FactionRelationContext.GetRespectImplication(relations.Respect)}");
                sb.AppendLine();

                sb.AppendLine($"5. INFLUENCE: {relations.Influence:F0}/100");
                sb.AppendLine($"   Level: {FactionRelationContext.GetInfluenceLevelDescription(relations.Influence)}");
                sb.AppendLine($"   Meaning: {FactionRelationContext.GetInfluenceImplication(relations.Influence)}");
                sb.AppendLine();

                sb.AppendLine("BEHAVIOR GUIDELINES based on these values:");
                sb.AppendLine(FactionRelationContext.GenerateBehaviorGuidelines(relations));
                sb.AppendLine();
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimDiplomacy] Failed to append relation context: {ex.Message}");
            }
        }

        private void AppendMemoryData(StringBuilder sb, Faction faction)
        {
            try
            {
                var memoryManager = LeaderMemoryManager.Instance;
                if (memoryManager == null) return;

                var leaderMemory = memoryManager.GetMemory(faction);
                if (leaderMemory == null) return;

                sb.AppendLine();
                sb.AppendLine("=== 记忆与历史数据（动态注入）===");
                sb.AppendLine("以下是你对其他派系的记忆和交互历史，请基于这些信息形成你的态度和决策：");
                sb.AppendLine();

                if (leaderMemory.SignificantEvents != null && leaderMemory.SignificantEvents.Count > 0)
                {
                    sb.AppendLine("【重大事件记忆】");
                    sb.AppendLine("这些事件深刻影响了你对其他派系的看法：");

                    var recentEvents = leaderMemory.SignificantEvents
                        .OrderByDescending(e => e.OccurredTick)
                        .Take(5)
                        .ToList();

                    foreach (var evt in recentEvents)
                    {
                        string eventIcon = GetEventIcon(evt.EventType);
                        sb.AppendLine($"  {eventIcon} [{GetEventTypeName(evt.EventType)}] 对 {evt.InvolvedFactionName}: {evt.Description}");
                    }
                    sb.AppendLine();
                }

                if (leaderMemory.FactionMemories != null && leaderMemory.FactionMemories.Count > 0)
                {
                    sb.AppendLine("【派系关系认知】");
                    sb.AppendLine("基于长期交互，你对以下派系形成了印象：");

                    foreach (var memory in leaderMemory.FactionMemories)
                    {
                        if (memory.PositiveInteractions == 0 && memory.NegativeInteractions == 0) continue;

                        string impression = GetRelationImpression(memory);
                        sb.AppendLine($"  • {memory.FactionName}: {impression}");
                        sb.AppendLine($"    交互记录：{memory.PositiveInteractions} 次正面，{memory.NegativeInteractions} 次负面");

                        if (memory.RelationHistory != null && memory.RelationHistory.Count > 0)
                        {
                            var trend = GetRelationTrend(memory.RelationHistory);
                            if (!string.IsNullOrEmpty(trend))
                            {
                                sb.AppendLine($"    关系趋势：{trend}");
                            }
                        }
                    }
                    sb.AppendLine();
                }

                sb.AppendLine("【记忆使用指导】");
                sb.AppendLine("- 对有过负面交互的派系保持警惕和怀疑");
                sb.AppendLine("- 对有过正面交互的派系更加友好和信任");
                sb.AppendLine("- 重大事件（如宣战、背叛）应该深刻影响你的态度");
                sb.AppendLine("- 基于历史形成连贯一致的外交策略");
                sb.AppendLine();
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimDiplomacy] 注入记忆数据失败：{ex.Message}");
            }
        }

        private void AppendFiveDimensionData(StringBuilder sb, Faction faction)
        {
            try
            {
                var relations = GameComponent_DiplomacyManager.Instance?.GetOrCreateRelationValues(faction);
                if (relations == null)
                {
                    sb.AppendLine();
                    sb.AppendLine("=== 五维关系评估 ===");
                    sb.AppendLine("关系数据暂时不可用。");
                    return;
                }

                sb.AppendLine();
                sb.AppendLine("=== 五维关系评估 (Five-Dimension Relations) ===");
                sb.AppendLine("这些数值代表我对你的态度评估，会影响我的决策和行为：");
                sb.AppendLine();

                sb.AppendLine($"1. 信任值 (Trust): {relations.Trust:F0}/100");
                sb.AppendLine($"   状态: {FactionRelationContext.GetTrustLevelDescription(relations.Trust)}");
                sb.AppendLine($"   含义: {FactionRelationContext.GetTrustImplication(relations.Trust)}");
                sb.AppendLine();

                sb.AppendLine($"2. 亲密度 (Intimacy): {relations.Intimacy:F0}/100");
                sb.AppendLine($"   状态: {FactionRelationContext.GetIntimacyLevelDescription(relations.Intimacy)}");
                sb.AppendLine($"   含义: {FactionRelationContext.GetIntimacyImplication(relations.Intimacy)}");
                sb.AppendLine();

                sb.AppendLine($"3. 互惠值 (Reciprocity): {relations.Reciprocity:F0}/100");
                sb.AppendLine($"   状态: {FactionRelationContext.GetReciprocityLevelDescription(relations.Reciprocity)}");
                sb.AppendLine($"   含义: {FactionRelationContext.GetReciprocityImplication(relations.Reciprocity)}");
                sb.AppendLine();

                sb.AppendLine($"4. 尊重值 (Respect): {relations.Respect:F0}/100");
                sb.AppendLine($"   状态: {FactionRelationContext.GetRespectLevelDescription(relations.Respect)}");
                sb.AppendLine($"   含义: {FactionRelationContext.GetRespectImplication(relations.Respect)}");
                sb.AppendLine();

                sb.AppendLine($"5. 影响值 (Influence): {relations.Influence:F0}/100");
                sb.AppendLine($"   状态: {FactionRelationContext.GetInfluenceLevelDescription(relations.Influence)}");
                sb.AppendLine($"   含义: {FactionRelationContext.GetInfluenceImplication(relations.Influence)}");
                sb.AppendLine();

                sb.AppendLine("=== 基于当前关系的行为准则 ===");
                sb.AppendLine(FactionRelationContext.GenerateBehaviorGuidelines(relations));
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimDiplomacy] 添加五维关系数据失败: {ex.Message}");
            }
        }

        private void AppendFactionInfo(StringBuilder sb, Faction faction)
        {
            sb.AppendLine();
            sb.AppendLine($"=== FACTION INFO ===");
            sb.AppendLine($"Name: {faction.Name}");
            sb.AppendLine($"Type: {faction.def?.label ?? "Unknown"}");
            if (!faction.IsPlayer)
            {
                sb.AppendLine($"Current Goodwill: {faction.PlayerGoodwill}");
                sb.AppendLine($"Relation: {GetRelationLabel(faction.PlayerGoodwill)}");
            }
            else
            {
                sb.AppendLine("Current Faction: Player Colony (Self)");
            }

            if (faction.leader != null)
            {
                sb.AppendLine($"Leader: {faction.leader.Name?.ToStringFull ?? "Unknown"}");

                if (faction.leader.story?.traits?.allTraits != null)
                {
                    var traits = faction.leader.story.traits.allTraits;
                    if (traits.Count > 0)
                    {
                        sb.AppendLine($"Leader Traits: {string.Join(", ", traits.Select(t => t.Label))}");
                    }
                }
            }

            if (faction.ideos?.PrimaryIdeo != null)
            {
                sb.AppendLine($"Ideology: {faction.ideos.PrimaryIdeo.name}");
            }
        }

        private void AppendRPGPawnInfo(StringBuilder sb, Pawn pawn, bool isTarget)
        {
            sb.AppendLine(isTarget ? "=== CHARACTER STATUS (YOU) ===" : "=== CHARACTER STATUS (INTERLOCUTOR) ===");
            sb.AppendLine($"Name: {pawn.Name?.ToStringFull ?? pawn.LabelShort}");
            sb.AppendLine($"Kind: {pawn.KindLabel}");
            sb.AppendLine($"Gender: {pawn.gender}");
            sb.AppendLine($"Age: {pawn.ageTracker?.AgeBiologicalYears}");
            
            if (pawn.story != null)
            {
                sb.AppendLine($"Backstory (Child): {pawn.story.Childhood?.title}");
                sb.AppendLine($"Backstory (Adult): {pawn.story.Adulthood?.title}");
                if (pawn.story.traits?.allTraits != null)
                {
                    sb.AppendLine($"Traits: {string.Join(", ", pawn.story.traits.allTraits.Select(t => t.Label))}");
                }
            }

            if (pawn.needs?.mood != null)
            {
                sb.AppendLine($"Current Mood: {pawn.needs.mood.CurLevelPercentage:P0}");
            }

            if (pawn.health != null)
            {
                sb.AppendLine($"Health Summary: {pawn.health.summaryHealth.SummaryHealthPercent:P0}");
            }
            
            sb.AppendLine();
        }

        private void AppendRPGRelationData(StringBuilder sb, Pawn initiator, Pawn target)
        {
            var rpgManager = GameComponent_RPGManager.Instance;
            if (rpgManager == null) return;

            var relations = rpgManager.GetOrCreateRelation(target);
            sb.AppendLine("=== YOUR FEELINGS TOWARDS THE INTERLOCUTOR ===");
            sb.AppendLine($"Interlocutor: {initiator.LabelShort}");
            sb.AppendLine($"Favorability: {relations.Favorability:F1}/100 (Positivity of your attitude)");
            sb.AppendLine($"Trust: {relations.Trust:F1}/100 (Credibility/Dependability)");
            sb.AppendLine($"Fear: {relations.Fear:F1}/100 (Power dynamics/Vulnerability)");
            sb.AppendLine($"Respect: {relations.Respect:F1}/100 (Status/Authority)");
            sb.AppendLine($"Dependency: {relations.Dependency:F1}/100 (Need for the other)");
            sb.AppendLine();
        }

        private void AppendRPGFactionContext(StringBuilder sb, Pawn pawn)
        {
            if (pawn.Faction == null) return;
            bool isTarget = pawn.IsColonist || pawn.IsPrisoner || pawn.IsSlave; // Roughly
            sb.AppendLine(isTarget ? "=== YOUR FACTION CONTEXT ===" : "=== INTERLOCUTOR FACTION CONTEXT ===");
            sb.AppendLine($"Faction: {pawn.Faction.Name} ({pawn.Faction.def?.label})");
            if (!pawn.Faction.IsPlayer)
            {
                sb.AppendLine($"Faction Relations with Player: {pawn.Faction.PlayerGoodwill} ({GetRelationLabel(pawn.Faction.PlayerGoodwill)})");
            }
            else
            {
                sb.AppendLine("Faction: Player Colony (Your own people)");
            }
            
            if (pawn.Faction.ideos?.PrimaryIdeo != null)
            {
                sb.AppendLine($"Primary Ideology: {pawn.Faction.ideos.PrimaryIdeo.name}");
            }
            sb.AppendLine();
        }

        private void AppendRPGApiDefinitions(StringBuilder sb)
        {
            sb.AppendLine("=== AVAILABLE NPC ACTIONS (API) ===");
            sb.AppendLine("You can trigger game effects by including them in the 'actions' array of your JSON output.");
            sb.AppendLine("Each action should be an object: { \"action\": \"ActionName\", \"defName\": \"OptionalDef\", \"amount\": 0 }");
            sb.AppendLine();
            sb.AppendLine("- TryGainMemory: Add a thought memory to yourself. Required 'defName'. Examples: 'JoyFilled', 'Insulted'.");
            sb.AppendLine("- TryAffectSocialGoodwill: Change goodwill between your faction and player. Required 'amount' (int).");
            sb.AppendLine("- ReduceResistance: If you are a prisoner, reduce your recruitment resistance. Required 'amount' (float/int).");
            sb.AppendLine("- ReduceWill: If you are a prisoner, reduce your enslavement will. Required 'amount' (float/int).");
            sb.AppendLine("- Recruit: Immediately join the player's faction (no parameters).");
            sb.AppendLine("- TryTakeOrderedJob: Execute a job. Use 'defName': 'AttackMelee' to attack the interlocutor.");
            sb.AppendLine("- TriggerIncident: Trigger a game event (incident). Required 'defName'. Optional 'amount' for incident points. Examples: 'RaidEnemy', 'TraderCaravanArrival', 'TravelerGroup'.");
            sb.AppendLine();
        }

        private void AppendApiLimits(StringBuilder sb, Faction faction = null)
        {
            var settings = RimDiplomacyMod.Instance?.InstanceSettings;
            if (settings == null) return;

            sb.AppendLine();
            sb.AppendLine($"=== CURRENT API LIMITS (MUST FOLLOW) ===");

            // Check current cooldown for specific faction
            if (faction != null)
            {
                int questCooldownSec = GameAIInterface.Instance.GetRemainingCooldownSeconds(faction, "CreateQuest");
                if (questCooldownSec > 0)
                {
                    // GameAIInterface.GetRemainingCooldownSeconds returns total remaining seconds (ticks/60)
                    // One RimWorld day is 60,000 ticks = 1000 seconds.
                    float remainingDays = questCooldownSec / 1000f;
                    sb.AppendLine($"- [CRITICAL] Create quest is CURRENTLY ON COOLDOWN for {faction.Name}. Remaining: {remainingDays:F1} days. You MUST NOT create any quests or missions until this cooldown expires. If the player requests a mission/quest, you MUST decline it and explain the reason (e.g., preparation, resource replenishment, or previous promises still being fulfilled).");
                }
            }

            sb.AppendLine($"- Max goodwill adjustment per call: {settings.MaxGoodwillAdjustmentPerCall} (range: 0 to {settings.MaxGoodwillAdjustmentPerCall})");
            sb.AppendLine($"- Max daily goodwill adjustment: {settings.MaxDailyGoodwillAdjustment}");
            sb.AppendLine($"- Goodwill cooldown: {settings.GoodwillCooldownTicks / 2500f:F1} hours");
            sb.AppendLine($"- Max gift silver: {settings.MaxGiftSilverAmount}");
            sb.AppendLine($"- Max gift goodwill gain: {settings.MaxGiftGoodwillGain}");
            sb.AppendLine($"- Max goodwill for war declaration: {settings.MaxGoodwillForWarDeclaration}");
            sb.AppendLine($"- Max peace cost: {settings.MaxPeaceCost}");
            sb.AppendLine($"- Peace goodwill reset: {settings.PeaceGoodwillReset}");
            sb.AppendLine($"- Create quest cooldown: {settings.MinQuestCooldownDays} to {settings.MaxQuestCooldownDays} days");
            sb.AppendLine();
            sb.AppendLine("ENABLED FEATURES:");
            sb.AppendLine($"- Goodwill adjustment: {(settings.EnableAIGoodwillAdjustment ? "YES" : "NO")}");
            sb.AppendLine($"- Gift sending: {(settings.EnableAIGiftSending ? "YES" : "NO")}");
            sb.AppendLine($"- War declaration: {(settings.EnableAIWarDeclaration ? "YES" : "NO")}");
            sb.AppendLine($"- Peace making: {(settings.EnableAIPeaceMaking ? "YES" : "NO")}");
            sb.AppendLine($"- Trade caravan: {(settings.EnableAITradeCaravan ? "YES" : "NO")}");
            sb.AppendLine($"- Aid request: {(settings.EnableAIAidRequest ? "YES" : "NO")}");
            sb.AppendLine();
        }

        private void AppendSimpleConfig(StringBuilder sb, SystemPromptConfig config)
        {
            var settings = RimDiplomacyMod.Instance?.InstanceSettings;

            sb.AppendLine("ACTIONS:");
            foreach (var action in config.ApiActions.Where(a => a.IsEnabled))
            {
                sb.AppendLine($"- {action.ActionName}: {action.Description}");
                if (!string.IsNullOrEmpty(action.Parameters))
                {
                    sb.AppendLine($"  Parameters: {action.Parameters}");
                }
                if (!string.IsNullOrEmpty(action.Requirement))
                {
                    sb.AppendLine($"  Requirement: {action.Requirement}");
                }
            }
            sb.AppendLine();

            if (config.DecisionRules != null && config.DecisionRules.Any(r => r.IsEnabled))
            {
                sb.AppendLine("DECISION GUIDELINES:");
                foreach (var rule in config.DecisionRules.Where(r => r.IsEnabled))
                {
                    sb.AppendLine($"- {rule.RuleName}: {rule.RuleContent}");
                }
                sb.AppendLine();
            }

            AppendRelationRulesConfig(sb);

            sb.AppendLine("RESPONSE FORMAT:");
            sb.AppendLine("Respond with your in-character dialogue first, then optionally include a JSON block:");
            sb.AppendLine();
            sb.AppendLine("```json");
            sb.AppendLine(config.ResponseFormat?.JsonTemplate ?? "");
            sb.AppendLine("```");
            sb.AppendLine();

            if (config.ResponseFormat?.IncludeRelationChanges == true)
            {
                sb.AppendLine("RELATION CHANGES (in relation_changes):");
                sb.AppendLine(config.ResponseFormat?.RelationChangesTemplate ?? "");
                sb.AppendLine();
            }

            sb.AppendLine("IMPORTANT RULES:");
            sb.AppendLine(config.ResponseFormat?.ImportantRules ?? "");
            sb.AppendLine();

            sb.AppendLine("If no action is needed, respond normally without JSON.");
        }

        private void AppendAdvancedConfig(StringBuilder sb, SystemPromptConfig config)
        {
            var settings = RimDiplomacyMod.Instance?.InstanceSettings;

            sb.AppendLine("ACTIONS:");
            int actionIndex = 1;
            foreach (var action in config.ApiActions.Where(a => a.IsEnabled))
            {
                sb.AppendLine($"{actionIndex}. {action.ActionName} - {action.Description}");
                if (!string.IsNullOrEmpty(action.Parameters))
                {
                    sb.AppendLine($"   Parameters: {action.Parameters}");
                }
                if (!string.IsNullOrEmpty(action.Requirement))
                {
                    sb.AppendLine($"   Requirement: {action.Requirement}");
                }
                actionIndex++;
            }
            sb.AppendLine();

            sb.AppendLine("DECISION GUIDELINES:");
            foreach (var rule in config.DecisionRules.Where(r => r.IsEnabled))
            {
                sb.AppendLine($"- {rule.RuleName}: {rule.RuleContent}");
            }
            sb.AppendLine();

            AppendRelationRulesConfig(sb);

            sb.AppendLine("RESPONSE FORMAT:");
            sb.AppendLine("Respond with your in-character dialogue first, then optionally include a JSON block:");
            sb.AppendLine();
            sb.AppendLine("```json");
            sb.AppendLine(config.ResponseFormat?.JsonTemplate ?? "");
            sb.AppendLine("```");
            sb.AppendLine();

            if (config.ResponseFormat?.IncludeRelationChanges == true)
            {
                sb.AppendLine("RELATION CHANGES (in relation_changes):");
                sb.AppendLine(config.ResponseFormat?.RelationChangesTemplate ?? "");
                sb.AppendLine();
            }

            sb.AppendLine("IMPORTANT RULES:");
            sb.AppendLine(config.ResponseFormat?.ImportantRules ?? "");
            sb.AppendLine();

            sb.AppendLine("If no action is needed, respond normally without JSON.");
        }

        private string GetRelationLabel(int goodwill)
        {
            if (goodwill >= 80) return "Ally";
            if (goodwill >= 40) return "Friend";
            if (goodwill >= 0) return "Neutral";
            if (goodwill >= -40) return "Hostile";
            return "Enemy";
        }

        private string GetEventIcon(SignificantEventType eventType)
        {
            return eventType switch
            {
                SignificantEventType.WarDeclared => "⚔️",
                SignificantEventType.PeaceMade => "🕊️",
                SignificantEventType.TradeCaravan => "📦",
                SignificantEventType.GiftSent => "🎁",
                SignificantEventType.AidRequested => "🆘",
                SignificantEventType.GoodwillChanged => "📊",
                SignificantEventType.AllianceFormed => "🤝",
                SignificantEventType.Betrayal => "🗡️",
                _ => "📌"
            };
        }

        private string GetEventTypeName(SignificantEventType eventType)
        {
            return eventType switch
            {
                SignificantEventType.WarDeclared => "宣战",
                SignificantEventType.PeaceMade => "议和",
                SignificantEventType.TradeCaravan => "贸易商队",
                SignificantEventType.GiftSent => "赠送礼物",
                SignificantEventType.AidRequested => "请求援助",
                SignificantEventType.GoodwillChanged => "好感度变化",
                SignificantEventType.AllianceFormed => "结盟",
                SignificantEventType.Betrayal => "背叛",
                _ => "事件"
            };
        }

        private string GetRelationImpression(FactionMemoryEntry memory)
        {
            if (memory.NegativeInteractions > memory.PositiveInteractions * 2)
            {
                return "危险的敌人，多次敌对行为让我们充满警惕";
            }
            else if (memory.NegativeInteractions > memory.PositiveInteractions)
            {
                return "关系紧张，存在较多冲突";
            }
            else if (memory.PositiveInteractions > memory.NegativeInteractions * 2)
            {
                return "可靠的盟友，长期友好合作建立了信任";
            }
            else if (memory.PositiveInteractions > memory.NegativeInteractions)
            {
                return "友好的派系，互动以合作为主";
            }
            else
            {
                return "关系复杂，既有合作也有冲突";
            }
        }

        private void AppendRelationRulesConfig(StringBuilder sb)
        {
            try
            {
                RelationRules.Instance.Initialize();
                var config = RelationRules.Instance.GetConfig();

                if (config == null || !config.IsEnabled)
                {
                    return;
                }

                string rulesPrompt = RelationRules.Instance.BuildRulesPrompt(config);
                if (!string.IsNullOrEmpty(rulesPrompt))
                {
                    sb.AppendLine(rulesPrompt);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimDiplomacy] Failed to append relation rules config: {ex.Message}");
            }
        }

        private string GetRelationTrend(List<RelationSnapshot> history)
        {
            if (history.Count < 2) return string.Empty;

            var recent = history.Skip(Math.Max(0, history.Count - 3)).ToList();
            if (recent.Count < 2) return string.Empty;

            int firstGoodwill = recent.First().Goodwill;
            int lastGoodwill = recent.Last().Goodwill;
            int change = lastGoodwill - firstGoodwill;

            if (change > 10) return "关系显著改善 ↑";
            else if (change > 0) return "关系缓慢改善 ↑";
            else if (change < -10) return "关系急剧恶化 ↓";
            else if (change < 0) return "关系缓慢恶化 ↓";
            else return "关系稳定 →";
        }
    }
}
