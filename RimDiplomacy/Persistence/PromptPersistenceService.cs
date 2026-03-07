using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using UnityEngine;
using RimDiplomacy.Config;
using RimDiplomacy.Memory;
using RimDiplomacy.Relation;
using RimDiplomacy.DiplomacySystem;
using RimDiplomacy.Core;
using RimDiplomacy.Util;
using RimDiplomacy.WorldState;
using RimDiplomacy.Prompting;

namespace RimDiplomacy.Persistence
{
    public partial class PromptPersistenceService : IPromptPersistenceService
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
                    try
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
                            if (createQuestAction != null)
                            {
                                if (createQuestAction.Parameters.Contains("Mission_BanditCamp") || createQuestAction.Parameters.Contains("ThreatReward_Raid_MiscReward") || createQuestAction.Requirement.Contains("without a template"))
                                {
                                    Log.Message("[RimDiplomacy] Migrating config: Updating create_quest to strict template mode...");
                                    createQuestAction.Description = "Create a mission/quest for the player using a native template.";
                                    createQuestAction.Parameters = "questDefName (string, REQUIRED: exact name from the dynamic list provided below), askerFaction (string, optional: defaults to current faction), points (int, optional: threat points for the mission)";
                                    createQuestAction.Requirement = "You MUST provide a valid questDefName from the approved list exactly as written. Custom quests are NOT allowed.";
                                    needsSave = true;
                                }
                            }

                            needsSave |= EnsurePresenceActionExists(
                                config,
                                "exit_dialogue",
                                "End the current dialogue session while keeping current presence status",
                                "reason (string, optional)",
                                "");
                            needsSave |= EnsurePresenceActionExists(
                                config,
                                "go_offline",
                                "End dialogue and switch to offline presence state",
                                "reason (string, optional)",
                                "");
                            needsSave |= EnsurePresenceActionExists(
                                config,
                                "set_dnd",
                                "Switch to do-not-disturb presence state and stop message exchange",
                                "reason (string, optional)",
                                "");
                            needsSave |= EnsurePresenceActionExists(
                                config,
                                "publish_public_post",
                                "Publish a public social-circle announcement visible to all factions and the player",
                                "category (string: Military/Economic/Diplomatic/Anomaly), sentiment (int: -2..2), summary (string, optional), targetFaction (string, optional), intentHint (string, optional)",
                                "Only use when communication should become public and have world-facing consequences");

                            if (MigrateLegacyQuestGuidance(config))
                            {
                                needsSave = true;
                            }

                            if (MigratePresenceBehaviorGuidance(config))
                            {
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
                    catch (Exception ex)
                    {
                        Log.Error($"[RimDiplomacy] Failed to parse existing config, creating new: {ex}");
                    }
                }

                _cachedConfig = CreateDefaultConfig();
                MigrateLegacyQuestGuidance(_cachedConfig);
                MigratePresenceBehaviorGuidance(_cachedConfig);
                EnsurePresenceActionExists(
                    _cachedConfig,
                    "publish_public_post",
                    "Publish a public social-circle announcement visible to all factions and the player",
                    "category (string: Military/Economic/Diplomatic/Anomaly), sentiment (int: -2..2), summary (string, optional), targetFaction (string, optional), intentHint (string, optional)",
                    "Only use when communication should become public and have world-facing consequences");
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
            return BuildFullSystemPrompt(faction, config, false, null);
        }

        public string BuildFullSystemPrompt(Faction faction, SystemPromptConfig config, bool isProactive, IEnumerable<string> additionalSceneTags)
        {
            config ??= LoadConfig() ?? CreateDefaultConfig();
            var sb = new StringBuilder();
            var scenarioContext = DialogueScenarioContext.CreateDiplomacy(faction, isProactive, additionalSceneTags);

            string environmentBlock = BuildEnvironmentPromptBlocks(config, scenarioContext);
            if (!string.IsNullOrWhiteSpace(environmentBlock))
            {
                sb.AppendLine(environmentBlock.TrimEnd());
                sb.AppendLine();
            }
            AppendFactGroundingGuidance(sb);

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
            AppendDynamicQuestGuidance(sb, faction);
            AppendQuestSelectionHardRules(sb);

            if (config.UseAdvancedMode)
            {
                AppendAdvancedConfig(sb, config, faction);
            }
            else
            {
                AppendSimpleConfig(sb, config, faction);
            }

            return sb.ToString();
        }

        public string BuildRPGFullSystemPrompt(Pawn initiator, Pawn target)
        {
            return BuildRPGFullSystemPrompt(initiator, target, false, null);
        }

        public string BuildRPGFullSystemPrompt(Pawn initiator, Pawn target, bool isProactive, IEnumerable<string> additionalSceneTags)
        {
            var sb = new StringBuilder();
            var settings = RimDiplomacyMod.Settings;
            SystemPromptConfig promptConfig = LoadConfig() ?? CreateDefaultConfig();

            var scenarioContext = DialogueScenarioContext.CreateRpg(initiator, target, isProactive, additionalSceneTags);
            string environmentBlock = BuildEnvironmentPromptBlocks(promptConfig, scenarioContext);
            if (!string.IsNullOrWhiteSpace(environmentBlock))
            {
                sb.AppendLine(environmentBlock.TrimEnd());
                sb.AppendLine();
            }
            AppendFactGroundingGuidance(sb);

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

            string pawnPersonaPrompt = ResolveRpgPawnPersonaPrompt(target);
            if (!string.IsNullOrEmpty(pawnPersonaPrompt))
            {
                sb.AppendLine("=== PERSONALITY OVERRIDE (PLAYER-DEFINED) ===");
                sb.AppendLine("The player provided the following pawn-specific personality prompt. Prioritize this while remaining coherent with current context.");
                sb.AppendLine(pawnPersonaPrompt);
                sb.AppendLine();
            }

            string dynamicFactionMemoryBlock = DialogueSummaryService.BuildRpgDynamicFactionMemoryBlock(target?.Faction, target);
            if (!string.IsNullOrWhiteSpace(dynamicFactionMemoryBlock))
            {
                sb.AppendLine(dynamicFactionMemoryBlock.TrimEnd());
                sb.AppendLine();
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
                AppendRPGPawnInfo(sb, target, true, promptConfig?.EnvironmentPrompt?.RpgSceneParamSwitches); // YOU (AI)
            }
            
            if (settings.RPGInjectInterlocutorStatus)
            {
                AppendRPGPawnInfo(sb, initiator, false, promptConfig?.EnvironmentPrompt?.RpgSceneParamSwitches); // INTERLOCUTOR (Player)
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
                RpgApiPromptTextBuilder.AppendActionDefinitions(sb);
                
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

        private void AppendFactGroundingGuidance(StringBuilder sb)
        {
            if (sb == null)
            {
                return;
            }

            sb.AppendLine("=== FACT GROUNDING RULES ===");
            sb.AppendLine("- Treat only provided prompt data, current visible world state, and recorded memory as factual.");
            sb.AppendLine("- Do not fabricate events, identities, motives, resources, injuries, map states, or relationship history.");
            sb.AppendLine("- If the player claims something you cannot verify, state uncertainty in-character and ask for evidence/clarification.");
            sb.AppendLine("- If the player's claim conflicts with known facts, challenge it cautiously instead of agreeing.");
            sb.AppendLine("- Keep responses constrained to known facts; label assumptions explicitly and avoid unsupported topic drift.");
            sb.AppendLine();
        }

        internal string BuildEnvironmentPromptBlocks(SystemPromptConfig config, DialogueScenarioContext context)
        {
            if (config?.EnvironmentPrompt == null || context == null)
            {
                return string.Empty;
            }

            var env = config.EnvironmentPrompt;
            var sb = new StringBuilder();

            if (env.Worldview?.Enabled == true && !string.IsNullOrWhiteSpace(env.Worldview.Content))
            {
                sb.AppendLine("=== ENVIRONMENT WORLDVIEW ===");
                sb.AppendLine(env.Worldview.Content.Trim());
                sb.AppendLine();
            }

            AppendEnvironmentContextBlock(sb, env, context);
            AppendRecentWorldEventIntel(sb, env, context);

            if (!(env.SceneSystem?.Enabled ?? false) || env.SceneEntries == null || env.SceneEntries.Count == 0)
            {
                return sb.ToString();
            }

            HashSet<string> tags = BuildScenarioTags(context, env.SceneSystem.PresetTagsEnabled);
            int maxPerScene = env.SceneSystem.MaxSceneChars > 0 ? env.SceneSystem.MaxSceneChars : int.MaxValue;
            int maxTotalSceneChars = env.SceneSystem.MaxTotalChars > 0 ? env.SceneSystem.MaxTotalChars : int.MaxValue;
            int totalSceneChars = 0;
            int appendedCount = 0;

            var matchedEntries = env.SceneEntries
                .Where(entry => entry != null && entry.Enabled)
                .Where(entry => context.IsRpg ? entry.ApplyToRPG : entry.ApplyToDiplomacy)
                .Where(entry => EntryMatchesTags(entry, tags))
                .OrderByDescending(entry => entry.Priority)
                .ThenBy(entry => entry.Name ?? string.Empty)
                .ToList();

            foreach (ScenePromptEntryConfig entry in matchedEntries)
            {
                string content = entry.Content?.Trim() ?? string.Empty;
                if (content.Length == 0)
                {
                    continue;
                }

                if (content.Length > maxPerScene)
                {
                    content = content.Substring(0, maxPerScene);
                }

                int remain = maxTotalSceneChars - totalSceneChars;
                if (remain <= 0)
                {
                    break;
                }

                if (content.Length > remain)
                {
                    content = content.Substring(0, remain);
                }

                if (content.Length == 0)
                {
                    continue;
                }

                if (appendedCount == 0)
                {
                    sb.AppendLine("=== SCENE PROMPT LAYERS ===");
                }

                string name = string.IsNullOrWhiteSpace(entry.Name) ? "UnnamedScene" : entry.Name.Trim();
                sb.AppendLine($"[{name}]");
                sb.AppendLine(content);
                sb.AppendLine();
                appendedCount++;
                totalSceneChars += content.Length;
            }

            return sb.ToString();
        }

        private void AppendEnvironmentContextBlock(StringBuilder sb, EnvironmentPromptConfig env, DialogueScenarioContext context)
        {
            EnvironmentContextSwitchesConfig switches = env?.EnvironmentContextSwitches;
            if (!(switches?.Enabled ?? false))
            {
                return;
            }

            Map map = ResolveEnvironmentMap(context);
            if (map == null)
            {
                return;
            }

            if (!TryResolveFocusCell(map, context, out IntVec3 focusCell))
            {
                return;
            }

            List<string> lines = BuildEnvironmentContextLines(map, focusCell, context, switches);
            if (lines.Count == 0)
            {
                return;
            }

            sb.AppendLine("=== ENVIRONMENT PARAMETERS ===");
            foreach (string line in lines)
            {
                sb.AppendLine(line);
            }
            sb.AppendLine();
        }

        internal void AppendRecentWorldEventIntel(StringBuilder sb, EnvironmentPromptConfig env, DialogueScenarioContext context)
        {
            EventIntelPromptConfig intel = env?.EventIntelPrompt;
            if (intel == null || !intel.Enabled || context == null)
            {
                return;
            }

            if (context.IsRpg && !intel.ApplyToRpg)
            {
                return;
            }

            if (!context.IsRpg && !intel.ApplyToDiplomacy)
            {
                return;
            }

            WorldEventLedgerComponent ledger = WorldEventLedgerComponent.Instance;
            if (ledger == null)
            {
                return;
            }

            Faction observer = context.Faction ?? context.Target?.Faction ?? context.Initiator?.Faction;
            var candidates = new List<(int Tick, string Text)>();

            if (intel.IncludeMapEvents)
            {
                List<WorldEventRecord> mapEvents = ledger.GetRecentWorldEvents(observer, intel.DaysWindow, includePublic: true, includeDirect: true);
                for (int i = 0; i < mapEvents.Count; i++)
                {
                    WorldEventRecord record = mapEvents[i];
                    if (record == null || string.IsNullOrWhiteSpace(record.Summary))
                    {
                        continue;
                    }

                    string line = $"- [MapEvent] {record.Summary} ({BuildRelativeTickText(record.OccurredTick)})";
                    candidates.Add((record.OccurredTick, line));
                }
            }

            if (intel.IncludeRaidBattleReports)
            {
                List<RaidBattleReportRecord> reports = ledger.GetRecentRaidBattleReports(observer, intel.DaysWindow, includeDirect: true);
                for (int i = 0; i < reports.Count; i++)
                {
                    RaidBattleReportRecord report = reports[i];
                    if (report == null || string.IsNullOrWhiteSpace(report.Summary))
                    {
                        continue;
                    }

                    string line = $"- [BattleIntel] {report.Summary} ({BuildRelativeTickText(report.BattleEndTick)})";
                    candidates.Add((report.BattleEndTick, line));
                }
            }

            if (candidates.Count == 0)
            {
                return;
            }

            int maxItems = Mathf.Clamp(intel.MaxInjectedItems, 1, 50);
            int maxChars = Mathf.Clamp(intel.MaxInjectedChars, 200, 12000);
            int usedChars = 0;
            int usedItems = 0;

            var selectedLines = new List<string>();
            foreach ((int Tick, string Text) item in candidates.OrderByDescending(x => x.Tick))
            {
                if (usedItems >= maxItems || usedChars >= maxChars)
                {
                    break;
                }

                string line = item.Text?.Trim() ?? string.Empty;
                if (line.Length == 0)
                {
                    continue;
                }

                int remainingChars = maxChars - usedChars;
                if (line.Length > remainingChars)
                {
                    if (remainingChars < 16)
                    {
                        break;
                    }

                    line = line.Substring(0, remainingChars);
                }

                selectedLines.Add(line);
                usedChars += line.Length;
                usedItems++;
            }

            if (selectedLines.Count == 0)
            {
                return;
            }

            sb.AppendLine("=== RECENT WORLD EVENTS & BATTLE INTEL ===");
            for (int i = 0; i < selectedLines.Count; i++)
            {
                sb.AppendLine(selectedLines[i]);
            }
            sb.AppendLine();
        }

        private string BuildRelativeTickText(int tick)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            int delta = Math.Max(0, now - Math.Max(0, tick));
            if (delta >= GenDate.TicksPerDay)
            {
                float days = delta / (float)GenDate.TicksPerDay;
                return $"{days:F1}d ago";
            }

            float hours = delta / 2500f;
            return $"{hours:F1}h ago";
        }

        private List<string> BuildEnvironmentContextLines(
            Map map,
            IntVec3 focusCell,
            DialogueScenarioContext context,
            EnvironmentContextSwitchesConfig switches)
        {
            var lines = new List<string>();
            if (switches.IncludeTime)
            {
                lines.Add($"Time: {BuildLocalTimeText(map)}");
            }

            if (switches.IncludeDate)
            {
                lines.Add($"Date: {BuildLocalDateText(map)}");
            }

            if (switches.IncludeSeason)
            {
                lines.Add($"Season: {GenLocalDate.Season(map)}");
            }

            if (switches.IncludeWeather)
            {
                lines.Add($"Weather: {map.weatherManager?.curWeather?.LabelCap ?? "Unknown"}");
            }

            if (switches.IncludeLocationAndTemperature)
            {
                lines.Add(BuildLocationAndTemperatureText(map, focusCell, context));
            }

            if (switches.IncludeTerrain)
            {
                TerrainDef terrain = map.terrainGrid?.TerrainAt(focusCell);
                if (terrain != null)
                {
                    lines.Add($"Terrain: {terrain.LabelCap}");
                }
            }

            if (switches.IncludeBeauty)
            {
                lines.Add($"Beauty: {BuildBeautyText(map, focusCell)}");
            }

            if (switches.IncludeCleanliness)
            {
                string cleanliness = BuildCleanlinessText(map, focusCell);
                if (!string.IsNullOrWhiteSpace(cleanliness))
                {
                    lines.Add($"Cleanliness: {cleanliness}");
                }
            }

            if (switches.IncludeSurroundings)
            {
                string surroundings = BuildSurroundingsText(map, focusCell, context);
                if (!string.IsNullOrWhiteSpace(surroundings))
                {
                    lines.Add($"Surroundings: {surroundings}");
                }
            }

            if (switches.IncludeWealth)
            {
                lines.Add($"MapWealth: {(int)(map.wealthWatcher?.WealthTotal ?? 0f)}");
            }

            return lines;
        }

        private string BuildLocalTimeText(Map map)
        {
            int hour = GenLocalDate.HourOfDay(map);
            float dayPercent = GenLocalDate.DayPercent(map);
            int minute = (int)((dayPercent * 24f - hour) * 60f);
            if (minute < 0) minute = 0;
            if (minute > 59) minute = 59;
            return $"{hour:00}:{minute:00}";
        }

        private string BuildLocalDateText(Map map)
        {
            int absTicks = Find.TickManager?.TicksAbs ?? 0;
            Vector2 longLat = Find.WorldGrid.LongLatOf(map.Tile);
            int dayOfQuadrum = GenDate.DayOfQuadrum(absTicks, longLat.x) + 1;
            string quadrum = GenDate.Quadrum(absTicks, longLat.x).Label();
            int year = GenDate.Year(absTicks, longLat.x) + 1;
            return $"{quadrum} {dayOfQuadrum}, Year {year}";
        }

        private string BuildLocationAndTemperatureText(Map map, IntVec3 focusCell, DialogueScenarioContext context)
        {
            float temperature = GenTemperature.GetTemperatureForCell(focusCell, map);
            string location = BuildLocationText(context, map, focusCell);
            return $"Location: {location}; Temperature: {temperature:F0}C";
        }

        private string BuildLocationText(DialogueScenarioContext context, Map map, IntVec3 focusCell)
        {
            Pawn target = context?.Target;
            if (target != null && target.Spawned && target.Map == map)
            {
                Room room = target.GetRoom();
                string roomLabel = room is { PsychologicallyOutdoors: false }
                    ? room.Role?.label ?? "Room"
                    : "Outdoors";
                return $"{target.LabelShortCap} @ {roomLabel} / {map.Parent?.LabelCap ?? map.Biome?.LabelCap}";
            }

            Pawn initiator = context?.Initiator;
            if (initiator != null && initiator.Spawned && initiator.Map == map)
            {
                Room room = initiator.GetRoom();
                string roomLabel = room is { PsychologicallyOutdoors: false }
                    ? room.Role?.label ?? "Room"
                    : "Outdoors";
                return $"{initiator.LabelShortCap} @ {roomLabel} / {map.Parent?.LabelCap ?? map.Biome?.LabelCap}";
            }

            TerrainDef terrain = map.terrainGrid?.TerrainAt(focusCell);
            string terrainLabel = terrain?.LabelCap ?? "UnknownTerrain";
            return $"{map.Parent?.LabelCap ?? map.Biome?.LabelCap} ({terrainLabel})";
        }

        private string BuildBeautyText(Map map, IntVec3 focusCell)
        {
            CellRect cellRect = CellRect.CenteredOn(focusCell, 2).ClipInsideMap(map);
            float total = 0f;
            int count = 0;
            foreach (IntVec3 cell in cellRect.Cells)
            {
                total += BeautyUtility.CellBeauty(cell, map);
                count++;
            }

            if (count == 0)
            {
                return "Unknown";
            }

            float avg = total / count;
            return avg.ToString("F1");
        }

        private string BuildCleanlinessText(Map map, IntVec3 focusCell)
        {
            Room room = focusCell.GetRoom(map);
            if (room == null || room.PsychologicallyOutdoors)
            {
                return "Outdoors";
            }

            float cleanliness = room.GetStat(RoomStatDefOf.Cleanliness);
            return cleanliness.ToString("F2");
        }

        private string BuildSurroundingsText(Map map, IntVec3 focusCell, DialogueScenarioContext context)
        {
            CellRect area = CellRect.CenteredOn(focusCell, 6).ClipInsideMap(map);
            if (area.Area == 0)
            {
                return string.Empty;
            }

            int humanlikes = 0;
            int hostiles = 0;
            int buildings = 0;
            int fires = 0;
            Faction referenceFaction = context?.Target?.Faction ?? context?.Initiator?.Faction ?? Faction.OfPlayer;

            foreach (IntVec3 cell in area.Cells)
            {
                List<Thing> things = cell.GetThingList(map);
                if (things == null)
                {
                    continue;
                }

                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing is Pawn pawn)
                    {
                        if (pawn.RaceProps?.Humanlike == true)
                        {
                            humanlikes++;
                        }

                        if (referenceFaction != null && pawn.Faction != null && pawn.Faction.HostileTo(referenceFaction))
                        {
                            hostiles++;
                        }
                        continue;
                    }

                    if (thing.def?.category == ThingCategory.Building)
                    {
                        buildings++;
                    }

                    if (thing.def == ThingDefOf.Fire)
                    {
                        fires++;
                    }
                }
            }

            var parts = new List<string>
            {
                $"humanlike={humanlikes}",
                $"hostile={hostiles}",
                $"buildings={buildings}"
            };
            if (fires > 0)
            {
                parts.Add($"fires={fires}");
            }
            return string.Join(", ", parts);
        }

        private Map ResolveEnvironmentMap(DialogueScenarioContext context)
        {
            if (context?.Target?.Map != null)
            {
                return context.Target.Map;
            }

            if (context?.Initiator?.Map != null)
            {
                return context.Initiator.Map;
            }

            if (Find.CurrentMap != null)
            {
                return Find.CurrentMap;
            }

            return Find.Maps?.FirstOrDefault(m => m != null && m.IsPlayerHome)
                ?? Find.Maps?.FirstOrDefault();
        }

        private bool TryResolveFocusCell(Map map, DialogueScenarioContext context, out IntVec3 focusCell)
        {
            focusCell = IntVec3.Invalid;
            if (map == null)
            {
                return false;
            }

            if (context?.Target != null && context.Target.Spawned && context.Target.Map == map)
            {
                focusCell = context.Target.Position;
                return true;
            }

            if (context?.Initiator != null && context.Initiator.Spawned && context.Initiator.Map == map)
            {
                focusCell = context.Initiator.Position;
                return true;
            }

            focusCell = map.Center;
            return focusCell.IsValid && focusCell.InBounds(map);
        }

        private HashSet<string> BuildScenarioTags(DialogueScenarioContext context, bool includePresetTags)
        {
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (context?.Tags != null)
            {
                foreach (string tag in context.Tags)
                {
                    AddNormalizedTag(tags, tag);
                }
            }

            if (!includePresetTags || context == null)
            {
                return tags;
            }

            if (context.IsRpg)
            {
                AppendRpgScenarioTags(context, tags);
            }
            else
            {
                AppendDiplomacyScenarioTags(context, tags);
            }

            return tags;
        }

        private void AppendDiplomacyScenarioTags(DialogueScenarioContext context, HashSet<string> tags)
        {
            Faction faction = context?.Faction;
            if (faction == null)
            {
                return;
            }

            AddNormalizedTag(tags, $"faction:{faction.def?.defName}");
            AddNormalizedTag(tags, $"tech:{faction.def?.techLevel}");

            int goodwill = faction.PlayerGoodwill;
            if (goodwill >= 60)
            {
                AddNormalizedTag(tags, "relation:friendly");
                AddNormalizedTag(tags, "scene:social");
            }
            else if (goodwill <= -40 || faction.HostileTo(Faction.OfPlayer))
            {
                AddNormalizedTag(tags, "relation:hostile");
                AddNormalizedTag(tags, "scene:threat");
            }
            else
            {
                AddNormalizedTag(tags, "relation:neutral");
                AddNormalizedTag(tags, "scene:social");
            }

            bool hasQuestWithFaction = Find.QuestManager?.QuestsListForReading?.Any(q =>
                q != null &&
                q.State == QuestState.Ongoing &&
                q.InvolvedFactions != null &&
                q.InvolvedFactions.Contains(faction)) == true;
            if (hasQuestWithFaction)
            {
                AddNormalizedTag(tags, "scene:task");
            }
        }

        private void AppendRpgScenarioTags(DialogueScenarioContext context, HashSet<string> tags)
        {
            Pawn initiator = context?.Initiator;
            Pawn target = context?.Target;
            if (target == null)
            {
                return;
            }

            AddNormalizedTag(tags, $"faction:{target.Faction?.def?.defName}");

            if (TryGetMoodTag(target, out string moodTag))
            {
                AddNormalizedTag(tags, moodTag);
            }

            float health = target.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
            if (health <= 0.6f)
            {
                AddNormalizedTag(tags, "health:injured");
                AddNormalizedTag(tags, "scene:conflict");
            }

            if (HasIntimateRelation(target, initiator))
            {
                AddNormalizedTag(tags, "relation:intimate");
                AddNormalizedTag(tags, "scene:intimacy");
            }

            GameComponent_RPGManager rpgManager = GameComponent_RPGManager.Instance ?? Current.Game?.GetComponent<GameComponent_RPGManager>();
            RPGRelationValues relation = rpgManager?.GetOrCreateRelation(target);
            if (relation != null)
            {
                if (relation.Fear >= 65f)
                {
                    AddNormalizedTag(tags, "relation:tense");
                    AddNormalizedTag(tags, "scene:conflict");
                }

                if (relation.Favorability >= 65f || relation.Trust >= 65f)
                {
                    AddNormalizedTag(tags, "relation:friendly");
                    AddNormalizedTag(tags, "scene:intimacy");
                }
                else if (relation.Favorability <= 30f || relation.Trust <= 30f)
                {
                    AddNormalizedTag(tags, "relation:cold");
                    AddNormalizedTag(tags, "scene:conflict");
                }
            }

            if (!tags.Contains("scene:intimacy") && !tags.Contains("scene:conflict"))
            {
                AddNormalizedTag(tags, "scene:daily");
            }
        }

        private bool TryGetMoodTag(Pawn pawn, out string moodTag)
        {
            moodTag = null;
            if (pawn?.needs?.mood == null)
            {
                return false;
            }

            float mood = pawn.needs.mood.CurLevelPercentage;
            if (mood <= 0.3f)
            {
                moodTag = "mood:low";
            }
            else if (mood >= 0.75f)
            {
                moodTag = "mood:high";
            }
            else
            {
                moodTag = "mood:normal";
            }

            return true;
        }

        private bool HasIntimateRelation(Pawn first, Pawn second)
        {
            if (first == null || second == null || first.relations == null)
            {
                return false;
            }

            return first.relations.DirectRelationExists(PawnRelationDefOf.Spouse, second)
                || first.relations.DirectRelationExists(PawnRelationDefOf.Fiance, second)
                || first.relations.DirectRelationExists(PawnRelationDefOf.Lover, second);
        }

        private bool EntryMatchesTags(ScenePromptEntryConfig entry, HashSet<string> normalizedTags)
        {
            if (entry?.MatchTags == null || entry.MatchTags.Count == 0)
            {
                return true;
            }

            foreach (string rawTag in entry.MatchTags)
            {
                string normalized = NormalizeTag(rawTag);
                if (normalized.Length == 0)
                {
                    continue;
                }

                if (!normalizedTags.Contains(normalized))
                {
                    return false;
                }
            }

            return true;
        }

        private void AddNormalizedTag(HashSet<string> tags, string tag)
        {
            if (tags == null)
            {
                return;
            }

            string normalized = NormalizeTag(tag);
            if (normalized.Length > 0)
            {
                tags.Add(normalized);
            }
        }

        private string NormalizeTag(string tag)
        {
            return string.IsNullOrWhiteSpace(tag) ? string.Empty : tag.Trim().ToLowerInvariant();
        }


        private string ResolveRpgPawnPersonaPrompt(Pawn target)
        {
            if (target == null)
            {
                return string.Empty;
            }

            var rpgManager = GameComponent_RPGManager.Instance ?? Current.Game?.GetComponent<GameComponent_RPGManager>();
            return rpgManager?.GetPawnPersonaPrompt(target) ?? string.Empty;
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
            SerializeEnvironmentPrompt(sb, config.EnvironmentPrompt, prettyPrint);
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

        private void SerializeEnvironmentPrompt(StringBuilder sb, EnvironmentPromptConfig environment, bool prettyPrint)
        {
            if (environment == null)
            {
                environment = new EnvironmentPromptConfig();
            }

            if (prettyPrint)
            {
                sb.AppendLine();
                sb.AppendLine("  \"EnvironmentPrompt\": {");
                sb.AppendLine("    \"Worldview\": {");
                sb.AppendLine($"      \"Enabled\": {(environment.Worldview?.Enabled ?? true).ToString().ToLower()},");
                sb.AppendLine($"      \"Content\": \"{EscapeJson(environment.Worldview?.Content ?? string.Empty)}\"");
                sb.AppendLine("    },");
                sb.AppendLine("    \"SceneSystem\": {");
                sb.AppendLine($"      \"Enabled\": {(environment.SceneSystem?.Enabled ?? true).ToString().ToLower()},");
                sb.AppendLine($"      \"MaxSceneChars\": {environment.SceneSystem?.MaxSceneChars ?? 1200},");
                sb.AppendLine($"      \"MaxTotalChars\": {environment.SceneSystem?.MaxTotalChars ?? 4000},");
                sb.AppendLine($"      \"PresetTagsEnabled\": {(environment.SceneSystem?.PresetTagsEnabled ?? true).ToString().ToLower()}");
                sb.AppendLine("    },");
                sb.AppendLine("    \"SceneEntries\": [");

                List<ScenePromptEntryConfig> entries = environment.SceneEntries ?? new List<ScenePromptEntryConfig>();
                for (int i = 0; i < entries.Count; i++)
                {
                    ScenePromptEntryConfig entry = entries[i] ?? new ScenePromptEntryConfig();
                    sb.AppendLine("      {");
                    sb.AppendLine($"        \"Id\": \"{EscapeJson(entry.Id ?? string.Empty)}\",");
                    sb.AppendLine($"        \"Name\": \"{EscapeJson(entry.Name ?? string.Empty)}\",");
                    sb.AppendLine($"        \"Enabled\": {entry.Enabled.ToString().ToLower()},");
                    sb.AppendLine($"        \"ApplyToDiplomacy\": {entry.ApplyToDiplomacy.ToString().ToLower()},");
                    sb.AppendLine($"        \"ApplyToRPG\": {entry.ApplyToRPG.ToString().ToLower()},");
                    sb.AppendLine($"        \"Priority\": {entry.Priority},");
                    sb.AppendLine($"        \"MatchTags\": {SerializeStringList(entry.MatchTags)},");
                    sb.AppendLine($"        \"Content\": \"{EscapeJson(entry.Content ?? string.Empty)}\"");
                    sb.Append(i < entries.Count - 1 ? "      }," : "      }");
                    sb.AppendLine();
                }

                sb.AppendLine("    ],");
                sb.AppendLine("    \"EnvironmentContextSwitches\": {");
                sb.AppendLine($"      \"Enabled\": {(environment.EnvironmentContextSwitches?.Enabled ?? true).ToString().ToLower()},");
                sb.AppendLine($"      \"IncludeTime\": {(environment.EnvironmentContextSwitches?.IncludeTime ?? true).ToString().ToLower()},");
                sb.AppendLine($"      \"IncludeDate\": {(environment.EnvironmentContextSwitches?.IncludeDate ?? false).ToString().ToLower()},");
                sb.AppendLine($"      \"IncludeSeason\": {(environment.EnvironmentContextSwitches?.IncludeSeason ?? true).ToString().ToLower()},");
                sb.AppendLine($"      \"IncludeWeather\": {(environment.EnvironmentContextSwitches?.IncludeWeather ?? true).ToString().ToLower()},");
                sb.AppendLine($"      \"IncludeLocationAndTemperature\": {(environment.EnvironmentContextSwitches?.IncludeLocationAndTemperature ?? true).ToString().ToLower()},");
                sb.AppendLine($"      \"IncludeTerrain\": {(environment.EnvironmentContextSwitches?.IncludeTerrain ?? false).ToString().ToLower()},");
                sb.AppendLine($"      \"IncludeBeauty\": {(environment.EnvironmentContextSwitches?.IncludeBeauty ?? false).ToString().ToLower()},");
                sb.AppendLine($"      \"IncludeCleanliness\": {(environment.EnvironmentContextSwitches?.IncludeCleanliness ?? false).ToString().ToLower()},");
                sb.AppendLine($"      \"IncludeSurroundings\": {(environment.EnvironmentContextSwitches?.IncludeSurroundings ?? false).ToString().ToLower()},");
                sb.AppendLine($"      \"IncludeWealth\": {(environment.EnvironmentContextSwitches?.IncludeWealth ?? false).ToString().ToLower()}");
                sb.AppendLine("    },");
                sb.AppendLine("    \"RpgSceneParamSwitches\": {");
                sb.AppendLine($"      \"IncludeSkills\": {(environment.RpgSceneParamSwitches?.IncludeSkills ?? true).ToString().ToLower()},");
                sb.AppendLine($"      \"IncludeEquipment\": {(environment.RpgSceneParamSwitches?.IncludeEquipment ?? true).ToString().ToLower()},");
                sb.AppendLine($"      \"IncludeGenes\": {(environment.RpgSceneParamSwitches?.IncludeGenes ?? true).ToString().ToLower()},");
                sb.AppendLine($"      \"IncludeNeeds\": {(environment.RpgSceneParamSwitches?.IncludeNeeds ?? true).ToString().ToLower()},");
                sb.AppendLine($"      \"IncludeHediffs\": {(environment.RpgSceneParamSwitches?.IncludeHediffs ?? true).ToString().ToLower()},");
                sb.AppendLine($"      \"IncludeRecentEvents\": {(environment.RpgSceneParamSwitches?.IncludeRecentEvents ?? true).ToString().ToLower()},");
                sb.AppendLine($"      \"IncludeColonyInventorySummary\": {(environment.RpgSceneParamSwitches?.IncludeColonyInventorySummary ?? true).ToString().ToLower()},");
                sb.AppendLine($"      \"IncludeHomeAlerts\": {(environment.RpgSceneParamSwitches?.IncludeHomeAlerts ?? true).ToString().ToLower()},");
                sb.AppendLine($"      \"IncludeRecentJobState\": {(environment.RpgSceneParamSwitches?.IncludeRecentJobState ?? true).ToString().ToLower()},");
                sb.AppendLine($"      \"IncludeAttributeLevels\": {(environment.RpgSceneParamSwitches?.IncludeAttributeLevels ?? true).ToString().ToLower()}");
                sb.AppendLine("    },");
                sb.AppendLine("    \"EventIntelPrompt\": {");
                sb.AppendLine($"      \"Enabled\": {(environment.EventIntelPrompt?.Enabled ?? true).ToString().ToLower()},");
                sb.AppendLine($"      \"ApplyToDiplomacy\": {(environment.EventIntelPrompt?.ApplyToDiplomacy ?? true).ToString().ToLower()},");
                sb.AppendLine($"      \"ApplyToRpg\": {(environment.EventIntelPrompt?.ApplyToRpg ?? true).ToString().ToLower()},");
                sb.AppendLine($"      \"IncludeMapEvents\": {(environment.EventIntelPrompt?.IncludeMapEvents ?? true).ToString().ToLower()},");
                sb.AppendLine($"      \"IncludeRaidBattleReports\": {(environment.EventIntelPrompt?.IncludeRaidBattleReports ?? true).ToString().ToLower()},");
                sb.AppendLine($"      \"DaysWindow\": {environment.EventIntelPrompt?.DaysWindow ?? 15},");
                sb.AppendLine($"      \"MaxStoredRecords\": {environment.EventIntelPrompt?.MaxStoredRecords ?? 50},");
                sb.AppendLine($"      \"MaxInjectedItems\": {environment.EventIntelPrompt?.MaxInjectedItems ?? 8},");
                sb.AppendLine($"      \"MaxInjectedChars\": {environment.EventIntelPrompt?.MaxInjectedChars ?? 1200}");
                sb.Append("    }");
                sb.AppendLine();
                sb.Append("  },");
            }
            else
            {
                sb.Append(",\"EnvironmentPrompt\":{");
                sb.Append("\"Worldview\":{");
                sb.Append($"\"Enabled\":{(environment.Worldview?.Enabled ?? true).ToString().ToLower()},");
                sb.Append($"\"Content\":\"{EscapeJson(environment.Worldview?.Content ?? string.Empty)}\"");
                sb.Append("},");
                sb.Append("\"SceneSystem\":{");
                sb.Append($"\"Enabled\":{(environment.SceneSystem?.Enabled ?? true).ToString().ToLower()},");
                sb.Append($"\"MaxSceneChars\":{environment.SceneSystem?.MaxSceneChars ?? 1200},");
                sb.Append($"\"MaxTotalChars\":{environment.SceneSystem?.MaxTotalChars ?? 4000},");
                sb.Append($"\"PresetTagsEnabled\":{(environment.SceneSystem?.PresetTagsEnabled ?? true).ToString().ToLower()}");
                sb.Append("},");
                sb.Append("\"SceneEntries\":[");

                List<ScenePromptEntryConfig> entries = environment.SceneEntries ?? new List<ScenePromptEntryConfig>();
                for (int i = 0; i < entries.Count; i++)
                {
                    ScenePromptEntryConfig entry = entries[i] ?? new ScenePromptEntryConfig();
                    sb.Append("{");
                    sb.Append($"\"Id\":\"{EscapeJson(entry.Id ?? string.Empty)}\",");
                    sb.Append($"\"Name\":\"{EscapeJson(entry.Name ?? string.Empty)}\",");
                    sb.Append($"\"Enabled\":{entry.Enabled.ToString().ToLower()},");
                    sb.Append($"\"ApplyToDiplomacy\":{entry.ApplyToDiplomacy.ToString().ToLower()},");
                    sb.Append($"\"ApplyToRPG\":{entry.ApplyToRPG.ToString().ToLower()},");
                    sb.Append($"\"Priority\":{entry.Priority},");
                    sb.Append($"\"MatchTags\":{SerializeStringList(entry.MatchTags)},");
                    sb.Append($"\"Content\":\"{EscapeJson(entry.Content ?? string.Empty)}\"");
                    sb.Append(i < entries.Count - 1 ? "}," : "}");
                }

                sb.Append("],");
                sb.Append("\"EnvironmentContextSwitches\":{");
                sb.Append($"\"Enabled\":{(environment.EnvironmentContextSwitches?.Enabled ?? true).ToString().ToLower()},");
                sb.Append($"\"IncludeTime\":{(environment.EnvironmentContextSwitches?.IncludeTime ?? true).ToString().ToLower()},");
                sb.Append($"\"IncludeDate\":{(environment.EnvironmentContextSwitches?.IncludeDate ?? false).ToString().ToLower()},");
                sb.Append($"\"IncludeSeason\":{(environment.EnvironmentContextSwitches?.IncludeSeason ?? true).ToString().ToLower()},");
                sb.Append($"\"IncludeWeather\":{(environment.EnvironmentContextSwitches?.IncludeWeather ?? true).ToString().ToLower()},");
                sb.Append($"\"IncludeLocationAndTemperature\":{(environment.EnvironmentContextSwitches?.IncludeLocationAndTemperature ?? true).ToString().ToLower()},");
                sb.Append($"\"IncludeTerrain\":{(environment.EnvironmentContextSwitches?.IncludeTerrain ?? false).ToString().ToLower()},");
                sb.Append($"\"IncludeBeauty\":{(environment.EnvironmentContextSwitches?.IncludeBeauty ?? false).ToString().ToLower()},");
                sb.Append($"\"IncludeCleanliness\":{(environment.EnvironmentContextSwitches?.IncludeCleanliness ?? false).ToString().ToLower()},");
                sb.Append($"\"IncludeSurroundings\":{(environment.EnvironmentContextSwitches?.IncludeSurroundings ?? false).ToString().ToLower()},");
                sb.Append($"\"IncludeWealth\":{(environment.EnvironmentContextSwitches?.IncludeWealth ?? false).ToString().ToLower()}");
                sb.Append("},");
                sb.Append("\"RpgSceneParamSwitches\":{");
                sb.Append($"\"IncludeSkills\":{(environment.RpgSceneParamSwitches?.IncludeSkills ?? true).ToString().ToLower()},");
                sb.Append($"\"IncludeEquipment\":{(environment.RpgSceneParamSwitches?.IncludeEquipment ?? true).ToString().ToLower()},");
                sb.Append($"\"IncludeGenes\":{(environment.RpgSceneParamSwitches?.IncludeGenes ?? true).ToString().ToLower()},");
                sb.Append($"\"IncludeNeeds\":{(environment.RpgSceneParamSwitches?.IncludeNeeds ?? true).ToString().ToLower()},");
                sb.Append($"\"IncludeHediffs\":{(environment.RpgSceneParamSwitches?.IncludeHediffs ?? true).ToString().ToLower()},");
                sb.Append($"\"IncludeRecentEvents\":{(environment.RpgSceneParamSwitches?.IncludeRecentEvents ?? true).ToString().ToLower()},");
                sb.Append($"\"IncludeColonyInventorySummary\":{(environment.RpgSceneParamSwitches?.IncludeColonyInventorySummary ?? true).ToString().ToLower()},");
                sb.Append($"\"IncludeHomeAlerts\":{(environment.RpgSceneParamSwitches?.IncludeHomeAlerts ?? true).ToString().ToLower()},");
                sb.Append($"\"IncludeRecentJobState\":{(environment.RpgSceneParamSwitches?.IncludeRecentJobState ?? true).ToString().ToLower()},");
                sb.Append($"\"IncludeAttributeLevels\":{(environment.RpgSceneParamSwitches?.IncludeAttributeLevels ?? true).ToString().ToLower()}");
                sb.Append("},");
                sb.Append("\"EventIntelPrompt\":{");
                sb.Append($"\"Enabled\":{(environment.EventIntelPrompt?.Enabled ?? true).ToString().ToLower()},");
                sb.Append($"\"ApplyToDiplomacy\":{(environment.EventIntelPrompt?.ApplyToDiplomacy ?? true).ToString().ToLower()},");
                sb.Append($"\"ApplyToRpg\":{(environment.EventIntelPrompt?.ApplyToRpg ?? true).ToString().ToLower()},");
                sb.Append($"\"IncludeMapEvents\":{(environment.EventIntelPrompt?.IncludeMapEvents ?? true).ToString().ToLower()},");
                sb.Append($"\"IncludeRaidBattleReports\":{(environment.EventIntelPrompt?.IncludeRaidBattleReports ?? true).ToString().ToLower()},");
                sb.Append($"\"DaysWindow\":{environment.EventIntelPrompt?.DaysWindow ?? 15},");
                sb.Append($"\"MaxStoredRecords\":{environment.EventIntelPrompt?.MaxStoredRecords ?? 50},");
                sb.Append($"\"MaxInjectedItems\":{environment.EventIntelPrompt?.MaxInjectedItems ?? 8},");
                sb.Append($"\"MaxInjectedChars\":{environment.EventIntelPrompt?.MaxInjectedChars ?? 1200}");
                sb.Append("}");
                sb.Append("},");
            }
        }

        private string SerializeStringList(List<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return "[]";
            }

            var sb = new StringBuilder();
            sb.Append("[");
            for (int i = 0; i < values.Count; i++)
            {
                sb.Append($"\"{EscapeJson(values[i] ?? string.Empty)}\"");
                if (i < values.Count - 1)
                {
                    sb.Append(",");
                }
            }
            sb.Append("]");
            return sb.ToString();
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
                ParseEnvironmentPrompt(json, config);
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

        private void ParseEnvironmentPrompt(string json, SystemPromptConfig config)
        {
            if (!TryExtractJsonObject(json, "EnvironmentPrompt", out string envContent))
            {
                if (config.EnvironmentPrompt == null)
                {
                    config.EnvironmentPrompt = new EnvironmentPromptConfig();
                }
                return;
            }

            var environment = new EnvironmentPromptConfig();

            if (TryExtractJsonObject(envContent, "Worldview", out string worldviewContent))
            {
                environment.Worldview = new WorldviewPromptConfig
                {
                    Content = ExtractString(worldviewContent, "Content")
                };

                string enabledStr = ExtractValue(worldviewContent, "Enabled");
                if (bool.TryParse(enabledStr, out bool enabled))
                {
                    environment.Worldview.Enabled = enabled;
                }
            }

            if (TryExtractJsonObject(envContent, "SceneSystem", out string sceneSystemContent))
            {
                environment.SceneSystem = new SceneSystemPromptConfig();

                string enabledStr = ExtractValue(sceneSystemContent, "Enabled");
                if (bool.TryParse(enabledStr, out bool enabled))
                {
                    environment.SceneSystem.Enabled = enabled;
                }

                string maxSceneCharsStr = ExtractValue(sceneSystemContent, "MaxSceneChars");
                if (int.TryParse(maxSceneCharsStr, out int maxSceneChars))
                {
                    environment.SceneSystem.MaxSceneChars = maxSceneChars;
                }

                string maxTotalCharsStr = ExtractValue(sceneSystemContent, "MaxTotalChars");
                if (int.TryParse(maxTotalCharsStr, out int maxTotalChars))
                {
                    environment.SceneSystem.MaxTotalChars = maxTotalChars;
                }

                string presetTagsEnabledStr = ExtractValue(sceneSystemContent, "PresetTagsEnabled");
                if (bool.TryParse(presetTagsEnabledStr, out bool presetTagsEnabled))
                {
                    environment.SceneSystem.PresetTagsEnabled = presetTagsEnabled;
                }
            }

            if (TryExtractJsonArray(envContent, "SceneEntries", out string sceneEntriesContent))
            {
                environment.SceneEntries = new List<ScenePromptEntryConfig>();
                var objects = SplitJsonObjects(sceneEntriesContent);
                foreach (string objStr in objects)
                {
                    var entry = new ScenePromptEntryConfig
                    {
                        Id = ExtractString(objStr, "Id"),
                        Name = ExtractString(objStr, "Name"),
                        Content = ExtractString(objStr, "Content"),
                        MatchTags = ExtractStringArray(objStr, "MatchTags")
                    };

                    string enabledStr = ExtractValue(objStr, "Enabled");
                    if (bool.TryParse(enabledStr, out bool enabled))
                    {
                        entry.Enabled = enabled;
                    }

                    string applyToDiplomacyStr = ExtractValue(objStr, "ApplyToDiplomacy");
                    if (bool.TryParse(applyToDiplomacyStr, out bool applyToDiplomacy))
                    {
                        entry.ApplyToDiplomacy = applyToDiplomacy;
                    }

                    string applyToRpgStr = ExtractValue(objStr, "ApplyToRPG");
                    if (bool.TryParse(applyToRpgStr, out bool applyToRpg))
                    {
                        entry.ApplyToRPG = applyToRpg;
                    }

                    string priorityStr = ExtractValue(objStr, "Priority");
                    if (int.TryParse(priorityStr, out int priority))
                    {
                        entry.Priority = priority;
                    }

                    if (string.IsNullOrWhiteSpace(entry.Id))
                    {
                        entry.Id = Guid.NewGuid().ToString("N");
                    }

                    environment.SceneEntries.Add(entry);
                }
            }

            if (TryExtractJsonObject(envContent, "EnvironmentContextSwitches", out string environmentContextContent))
            {
                environment.EnvironmentContextSwitches = new EnvironmentContextSwitchesConfig();

                string enabledStr = ExtractValue(environmentContextContent, "Enabled");
                if (bool.TryParse(enabledStr, out bool enabled))
                {
                    environment.EnvironmentContextSwitches.Enabled = enabled;
                }

                string includeTimeStr = ExtractValue(environmentContextContent, "IncludeTime");
                if (bool.TryParse(includeTimeStr, out bool includeTime))
                {
                    environment.EnvironmentContextSwitches.IncludeTime = includeTime;
                }

                string includeDateStr = ExtractValue(environmentContextContent, "IncludeDate");
                if (bool.TryParse(includeDateStr, out bool includeDate))
                {
                    environment.EnvironmentContextSwitches.IncludeDate = includeDate;
                }

                string includeSeasonStr = ExtractValue(environmentContextContent, "IncludeSeason");
                if (bool.TryParse(includeSeasonStr, out bool includeSeason))
                {
                    environment.EnvironmentContextSwitches.IncludeSeason = includeSeason;
                }

                string includeWeatherStr = ExtractValue(environmentContextContent, "IncludeWeather");
                if (bool.TryParse(includeWeatherStr, out bool includeWeather))
                {
                    environment.EnvironmentContextSwitches.IncludeWeather = includeWeather;
                }

                string includeLocationAndTemperatureStr = ExtractValue(environmentContextContent, "IncludeLocationAndTemperature");
                if (bool.TryParse(includeLocationAndTemperatureStr, out bool includeLocationAndTemperature))
                {
                    environment.EnvironmentContextSwitches.IncludeLocationAndTemperature = includeLocationAndTemperature;
                }

                string includeTerrainStr = ExtractValue(environmentContextContent, "IncludeTerrain");
                if (bool.TryParse(includeTerrainStr, out bool includeTerrain))
                {
                    environment.EnvironmentContextSwitches.IncludeTerrain = includeTerrain;
                }

                string includeBeautyStr = ExtractValue(environmentContextContent, "IncludeBeauty");
                if (bool.TryParse(includeBeautyStr, out bool includeBeauty))
                {
                    environment.EnvironmentContextSwitches.IncludeBeauty = includeBeauty;
                }

                string includeCleanlinessStr = ExtractValue(environmentContextContent, "IncludeCleanliness");
                if (bool.TryParse(includeCleanlinessStr, out bool includeCleanliness))
                {
                    environment.EnvironmentContextSwitches.IncludeCleanliness = includeCleanliness;
                }

                string includeSurroundingsStr = ExtractValue(environmentContextContent, "IncludeSurroundings");
                if (bool.TryParse(includeSurroundingsStr, out bool includeSurroundings))
                {
                    environment.EnvironmentContextSwitches.IncludeSurroundings = includeSurroundings;
                }

                string includeWealthStr = ExtractValue(environmentContextContent, "IncludeWealth");
                if (bool.TryParse(includeWealthStr, out bool includeWealth))
                {
                    environment.EnvironmentContextSwitches.IncludeWealth = includeWealth;
                }
            }

            if (TryExtractJsonObject(envContent, "RpgSceneParamSwitches", out string rpgSwitchContent))
            {
                environment.RpgSceneParamSwitches = new RpgSceneParamSwitchesConfig();

                string includeSkillsStr = ExtractValue(rpgSwitchContent, "IncludeSkills");
                if (bool.TryParse(includeSkillsStr, out bool includeSkills))
                {
                    environment.RpgSceneParamSwitches.IncludeSkills = includeSkills;
                }

                string includeEquipmentStr = ExtractValue(rpgSwitchContent, "IncludeEquipment");
                if (bool.TryParse(includeEquipmentStr, out bool includeEquipment))
                {
                    environment.RpgSceneParamSwitches.IncludeEquipment = includeEquipment;
                }

                string includeGenesStr = ExtractValue(rpgSwitchContent, "IncludeGenes");
                if (bool.TryParse(includeGenesStr, out bool includeGenes))
                {
                    environment.RpgSceneParamSwitches.IncludeGenes = includeGenes;
                }

                string includeNeedsStr = ExtractValue(rpgSwitchContent, "IncludeNeeds");
                if (bool.TryParse(includeNeedsStr, out bool includeNeeds))
                {
                    environment.RpgSceneParamSwitches.IncludeNeeds = includeNeeds;
                }

                string includeHediffsStr = ExtractValue(rpgSwitchContent, "IncludeHediffs");
                if (bool.TryParse(includeHediffsStr, out bool includeHediffs))
                {
                    environment.RpgSceneParamSwitches.IncludeHediffs = includeHediffs;
                }

                string includeRecentEventsStr = ExtractValue(rpgSwitchContent, "IncludeRecentEvents");
                if (bool.TryParse(includeRecentEventsStr, out bool includeRecentEvents))
                {
                    environment.RpgSceneParamSwitches.IncludeRecentEvents = includeRecentEvents;
                }

                string includeInventorySummaryStr = ExtractValue(rpgSwitchContent, "IncludeColonyInventorySummary");
                if (bool.TryParse(includeInventorySummaryStr, out bool includeInventorySummary))
                {
                    environment.RpgSceneParamSwitches.IncludeColonyInventorySummary = includeInventorySummary;
                }

                string includeHomeAlertsStr = ExtractValue(rpgSwitchContent, "IncludeHomeAlerts");
                if (bool.TryParse(includeHomeAlertsStr, out bool includeHomeAlerts))
                {
                    environment.RpgSceneParamSwitches.IncludeHomeAlerts = includeHomeAlerts;
                }

                string includeRecentJobStateStr = ExtractValue(rpgSwitchContent, "IncludeRecentJobState");
                if (bool.TryParse(includeRecentJobStateStr, out bool includeRecentJobState))
                {
                    environment.RpgSceneParamSwitches.IncludeRecentJobState = includeRecentJobState;
                }

                string includeAttributeLevelsStr = ExtractValue(rpgSwitchContent, "IncludeAttributeLevels");
                if (bool.TryParse(includeAttributeLevelsStr, out bool includeAttributeLevels))
                {
                    environment.RpgSceneParamSwitches.IncludeAttributeLevels = includeAttributeLevels;
                }
            }

            if (TryExtractJsonObject(envContent, "EventIntelPrompt", out string eventIntelContent))
            {
                environment.EventIntelPrompt = new EventIntelPromptConfig();

                string enabledStr = ExtractValue(eventIntelContent, "Enabled");
                if (bool.TryParse(enabledStr, out bool enabled))
                {
                    environment.EventIntelPrompt.Enabled = enabled;
                }

                string applyToDiplomacyStr = ExtractValue(eventIntelContent, "ApplyToDiplomacy");
                if (bool.TryParse(applyToDiplomacyStr, out bool applyToDiplomacy))
                {
                    environment.EventIntelPrompt.ApplyToDiplomacy = applyToDiplomacy;
                }

                string applyToRpgStr = ExtractValue(eventIntelContent, "ApplyToRpg");
                if (bool.TryParse(applyToRpgStr, out bool applyToRpg))
                {
                    environment.EventIntelPrompt.ApplyToRpg = applyToRpg;
                }

                string includeMapEventsStr = ExtractValue(eventIntelContent, "IncludeMapEvents");
                if (bool.TryParse(includeMapEventsStr, out bool includeMapEvents))
                {
                    environment.EventIntelPrompt.IncludeMapEvents = includeMapEvents;
                }

                string includeRaidReportsStr = ExtractValue(eventIntelContent, "IncludeRaidBattleReports");
                if (bool.TryParse(includeRaidReportsStr, out bool includeRaidReports))
                {
                    environment.EventIntelPrompt.IncludeRaidBattleReports = includeRaidReports;
                }

                string daysWindowStr = ExtractValue(eventIntelContent, "DaysWindow");
                if (int.TryParse(daysWindowStr, out int daysWindow))
                {
                    environment.EventIntelPrompt.DaysWindow = daysWindow;
                }

                string maxStoredStr = ExtractValue(eventIntelContent, "MaxStoredRecords");
                if (int.TryParse(maxStoredStr, out int maxStored))
                {
                    environment.EventIntelPrompt.MaxStoredRecords = maxStored;
                }

                string maxItemsStr = ExtractValue(eventIntelContent, "MaxInjectedItems");
                if (int.TryParse(maxItemsStr, out int maxItems))
                {
                    environment.EventIntelPrompt.MaxInjectedItems = maxItems;
                }

                string maxCharsStr = ExtractValue(eventIntelContent, "MaxInjectedChars");
                if (int.TryParse(maxCharsStr, out int maxChars))
                {
                    environment.EventIntelPrompt.MaxInjectedChars = maxChars;
                }
            }

            config.EnvironmentPrompt = environment;
        }

        private bool TryExtractJsonObject(string json, string key, out string objectContent)
        {
            objectContent = string.Empty;
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
            {
                return false;
            }

            string pattern = $"\"{key}\":";
            int start = json.IndexOf(pattern, StringComparison.Ordinal);
            if (start < 0)
            {
                return false;
            }

            int objectStart = json.IndexOf('{', start + pattern.Length);
            if (objectStart < 0)
            {
                return false;
            }

            if (!TryFindJsonBlockEnd(json, objectStart, '{', '}', out int objectEnd))
            {
                return false;
            }

            objectContent = json.Substring(objectStart, objectEnd - objectStart + 1);
            return true;
        }

        private bool TryExtractJsonArray(string json, string key, out string arrayContent)
        {
            arrayContent = string.Empty;
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
            {
                return false;
            }

            string pattern = $"\"{key}\":";
            int start = json.IndexOf(pattern, StringComparison.Ordinal);
            if (start < 0)
            {
                return false;
            }

            int arrayStart = json.IndexOf('[', start + pattern.Length);
            if (arrayStart < 0)
            {
                return false;
            }

            if (!TryFindJsonBlockEnd(json, arrayStart, '[', ']', out int arrayEnd))
            {
                return false;
            }

            arrayContent = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
            return true;
        }

        private bool TryFindJsonBlockEnd(string json, int blockStart, char openChar, char closeChar, out int endIndex)
        {
            endIndex = -1;
            if (string.IsNullOrEmpty(json) || blockStart < 0 || blockStart >= json.Length || json[blockStart] != openChar)
            {
                return false;
            }

            bool inString = false;
            bool escape = false;
            int depth = 0;

            for (int i = blockStart; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escape = true;
                        continue;
                    }

                    if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == openChar)
                {
                    depth++;
                    continue;
                }

                if (c == closeChar)
                {
                    depth--;
                    if (depth == 0)
                    {
                        endIndex = i;
                        return true;
                    }
                }
            }

            return false;
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

        private List<string> ExtractStringArray(string json, string key)
        {
            var result = new List<string>();
            if (!TryExtractJsonArray(json, key, out string arrayContent))
            {
                return result;
            }

            bool inString = false;
            bool escape = false;
            var current = new StringBuilder();

            for (int i = 0; i < arrayContent.Length; i++)
            {
                char c = arrayContent[i];
                if (!inString)
                {
                    if (c == '"')
                    {
                        inString = true;
                        current.Clear();
                    }
                    continue;
                }

                if (escape)
                {
                    current.Append(c switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        '"' => '"',
                        '\\' => '\\',
                        _ => c
                    });
                    escape = false;
                    continue;
                }

                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = false;
                    result.Add(current.ToString());
                    continue;
                }

                current.Append(c);
            }

            return result;
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

                List<CrossChannelSummaryRecord> crossSummaries = new List<CrossChannelSummaryRecord>();
                if (leaderMemory.DiplomacySessionSummaries != null)
                {
                    crossSummaries.AddRange(leaderMemory.DiplomacySessionSummaries);
                }
                if (leaderMemory.RpgDepartSummaries != null)
                {
                    crossSummaries.AddRange(leaderMemory.RpgDepartSummaries);
                }

                if (crossSummaries.Count > 0)
                {
                    sb.AppendLine("【跨通道长期记忆】");
                    sb.AppendLine("来自外交会话与 RPG 离图事件的共享摘要：");

                    foreach (CrossChannelSummaryRecord summary in crossSummaries
                        .Where(x => x != null && !string.IsNullOrWhiteSpace(x.SummaryText))
                        .OrderByDescending(x => x.GameTick)
                        .Take(6))
                    {
                        string sourceLabel = summary.Source == CrossChannelSummarySource.DiplomacySession
                            ? "外交会话"
                            : "RPG离图";
                        sb.AppendLine($"  • [{sourceLabel}] {summary.SummaryText}");

                        if (summary.KeyFacts != null && summary.KeyFacts.Count > 0)
                        {
                            string facts = string.Join("；", summary.KeyFacts.Take(2));
                            if (!string.IsNullOrWhiteSpace(facts))
                            {
                                sb.AppendLine($"    关键点：{facts}");
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

        private void AppendRPGPawnInfo(StringBuilder sb, Pawn pawn, bool isTarget, RpgSceneParamSwitchesConfig switches)
        {
            if (pawn == null)
            {
                return;
            }

            var effectiveSwitches = switches ?? new RpgSceneParamSwitchesConfig();
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

            if (effectiveSwitches.IncludeNeeds)
            {
                AppendRpgNeeds(sb, pawn);
            }

            if (effectiveSwitches.IncludeHediffs)
            {
                AppendRpgHediffs(sb, pawn);
            }

            if (effectiveSwitches.IncludeSkills)
            {
                AppendRpgSkills(sb, pawn);
            }

            if (effectiveSwitches.IncludeEquipment)
            {
                AppendRpgEquipment(sb, pawn);
            }

            if (effectiveSwitches.IncludeGenes)
            {
                AppendRpgGenes(sb, pawn);
            }

            if (effectiveSwitches.IncludeRecentEvents)
            {
                AppendRpgRecentMemories(sb, pawn);
            }

            AppendPlayerColonyContextIfEnabled(sb, pawn, effectiveSwitches);
            
            sb.AppendLine();
        }

        private void AppendRpgNeeds(StringBuilder sb, Pawn pawn)
        {
            if (pawn?.needs?.AllNeeds == null)
            {
                return;
            }

            List<string> values = pawn.needs.AllNeeds
                .Where(need => need != null && need.def != null)
                .Take(6)
                .Select(need => $"{need.def.label}:{need.CurLevelPercentage:P0}")
                .ToList();

            if (values.Count > 0)
            {
                sb.AppendLine($"Needs: {string.Join(", ", values)}");
            }
        }

        private void AppendRpgHediffs(StringBuilder sb, Pawn pawn)
        {
            if (pawn?.health?.hediffSet?.hediffs == null)
            {
                return;
            }

            List<string> values = pawn.health.hediffSet.hediffs
                .Where(h => h != null && h.Visible)
                .Take(6)
                .Select(h => h.LabelCap)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .ToList();

            if (values.Count > 0)
            {
                sb.AppendLine($"Visible Conditions: {string.Join(", ", values)}");
            }
        }

        private void AppendRpgSkills(StringBuilder sb, Pawn pawn)
        {
            if (pawn?.skills?.skills == null)
            {
                return;
            }

            List<string> values = pawn.skills.skills
                .Where(skill => skill != null && skill.def != null)
                .OrderByDescending(skill => skill.Level)
                .Take(6)
                .Select(skill => $"{skill.def.skillLabel}:{skill.Level}")
                .ToList();

            if (values.Count > 0)
            {
                sb.AppendLine($"Top Skills: {string.Join(", ", values)}");
            }
        }

        private void AppendRpgEquipment(StringBuilder sb, Pawn pawn)
        {
            List<string> parts = new List<string>();

            string primary = pawn?.equipment?.Primary?.LabelCap;
            if (!string.IsNullOrWhiteSpace(primary))
            {
                parts.Add($"Primary={primary}");
            }

            if (pawn?.apparel?.WornApparel != null)
            {
                string worn = string.Join(", ", pawn.apparel.WornApparel
                    .Take(4)
                    .Select(apparel => apparel?.LabelCap)
                    .Where(label => !string.IsNullOrWhiteSpace(label)));
                if (!string.IsNullOrWhiteSpace(worn))
                {
                    parts.Add($"Worn={worn}");
                }
            }

            if (parts.Count > 0)
            {
                sb.AppendLine($"Equipment: {string.Join(" | ", parts)}");
            }
        }

        private void AppendRpgGenes(StringBuilder sb, Pawn pawn)
        {
            object genesObj = pawn?.genes;
            if (genesObj == null)
            {
                return;
            }

            var genesProperty = genesObj.GetType().GetProperty("GenesListForReading");
            if (genesProperty == null)
            {
                return;
            }

            var enumerable = genesProperty.GetValue(genesObj) as System.Collections.IEnumerable;
            if (enumerable == null)
            {
                return;
            }

            List<string> values = new List<string>();
            foreach (object gene in enumerable)
            {
                if (gene == null)
                {
                    continue;
                }

                string label = gene.GetType().GetProperty("LabelCap")?.GetValue(gene)?.ToString();
                if (string.IsNullOrWhiteSpace(label))
                {
                    object defObj = gene.GetType().GetProperty("def")?.GetValue(gene);
                    label = defObj?.GetType().GetProperty("label")?.GetValue(defObj)?.ToString();
                }

                if (!string.IsNullOrWhiteSpace(label))
                {
                    values.Add(label);
                }

                if (values.Count >= 8)
                {
                    break;
                }
            }

            if (values.Count > 0)
            {
                sb.AppendLine($"Genes: {string.Join(", ", values)}");
            }
        }

        private void AppendRpgRecentMemories(StringBuilder sb, Pawn pawn)
        {
            var memories = pawn?.needs?.mood?.thoughts?.memories?.Memories;
            if (memories == null)
            {
                return;
            }

            List<string> values = memories
                .Where(memory => memory != null)
                .OrderBy(memory => memory.age)
                .Take(5)
                .Select(memory => memory.LabelCap)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .ToList();

            if (values.Count > 0)
            {
                sb.AppendLine($"Recent Memories: {string.Join(", ", values)}");
            }
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

        /// <summary>
        /// Build dynamic quest availability from centralized eligibility service.
        /// </summary>
        private void AppendDynamicQuestGuidance(StringBuilder sb, Faction faction)
        {
            if (faction == null) return;

            var report = ApiActionEligibilityService.Instance.GetQuestEligibilityReport(faction);
            var allowed = report.Where(x => x.Allowed).ToList();
            var blocked = report.Where(x => !x.Allowed).ToList();

            sb.AppendLine();
            sb.AppendLine("=== DYNAMIC QUEST AVAILABILITY (Auto-generated for current faction) ===");
            sb.AppendLine($"Faction: {faction.Name} | Tech: {faction.def?.techLevel} | Type: {faction.def?.defName}");
            sb.AppendLine();

            if (!allowed.Any())
            {
                sb.AppendLine("[BLOCKED] No eligible quest templates are available for your faction.");
                if (blocked.Any())
                {
                    sb.AppendLine("Blocked reasons:");
                    foreach (var item in blocked)
                    {
                        sb.AppendLine($"  - {item.QuestDefName}: {item.Message}");
                    }
                }
                sb.AppendLine();
                return;
            }

            sb.AppendLine("Available quests for your faction (ONLY use these exact defNames):");
            foreach (var item in allowed)
            {
                sb.AppendLine($"  - {item.QuestDefName}");
            }

            if (blocked.Any())
            {
                sb.AppendLine();
                sb.AppendLine("Blocked quest templates for current faction (DO NOT use):");
                foreach (var item in blocked)
                {
                    sb.AppendLine($"  - {item.QuestDefName}: {item.Message}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("IMPORTANT: You MUST ONLY select one questDefName from the exact available list above.");
            sb.AppendLine();
        }

        private void AppendQuestSelectionHardRules(StringBuilder sb)
        {
            sb.AppendLine("=== QUEST TEMPLATE STRICT OVERRIDE ===");
            sb.AppendLine("You MUST treat 'DYNAMIC QUEST AVAILABILITY (Auto-generated for current faction)' as the ONLY valid quest source.");
            sb.AppendLine("Do NOT use static/recalled quest recommendations from any other section.");
            sb.AppendLine("If a quest is listed under blocked templates or blocked actions, you MUST NOT call create_quest for it.");
            sb.AppendLine("Safety policy can disable high-risk templates (for example OpportunitySite_ItemStash). If disabled, you MUST refuse and explain constraints in-character.");
            sb.AppendLine();
        }

        private bool MigrateLegacyQuestGuidance(SystemPromptConfig config)
        {
            if (config == null || string.IsNullOrEmpty(config.GlobalSystemPrompt))
            {
                return false;
            }

            string legacyMarker = "以下是推荐的任务清单及其适用范围";
            if (!config.GlobalSystemPrompt.Contains(legacyMarker))
            {
                return false;
            }

            const string replacementSection =
                "【任务系统指导】\n" +
                "你只能通过 create_quest 动作并指定有效的 questDefName 来发起任务。\n" +
                "任务模板不得使用固定推荐清单，必须只使用后文 “DYNAMIC QUEST AVAILABILITY (Auto-generated for current faction)” 的 Available quests。\n" +
                "如果任务出现在 Blocked quest templates 或 BLOCKED ACTIONS 中，严禁调用；若玩家请求，必须角色化拒绝并说明条件不满足。\n" +
                "为保证稳定性，系统可能动态禁用高风险任务模板（例如 OpportunitySite_ItemStash），即使你知道名称也不得调用。\n\n";

            string pattern = @"【任务系统指导】[\s\S]*?(?=【重要禁令】)";
            string migrated = Regex.Replace(config.GlobalSystemPrompt, pattern, replacementSection, RegexOptions.Singleline);
            if (string.Equals(migrated, config.GlobalSystemPrompt, StringComparison.Ordinal))
            {
                return false;
            }

            config.GlobalSystemPrompt = migrated;
            Log.Message("[RimDiplomacy] Migrating config: Replaced legacy static quest guidance with dynamic-only guidance.");
            return true;
        }

        private bool MigratePresenceBehaviorGuidance(SystemPromptConfig config)
        {
            if (config == null || string.IsNullOrEmpty(config.GlobalSystemPrompt))
            {
                return false;
            }

            const string sectionTitle = "【在线状态策略】";
            if (config.GlobalSystemPrompt.Contains(sectionTitle))
            {
                return false;
            }

            const string sectionContent =
                "【在线状态策略】\n" +
                "- 你可以根据语境和情绪主动切换在线状态：exit_dialogue / go_offline / set_dnd。\n" +
                "- 当需要结束当前话题但允许稍后继续时，优先使用 exit_dialogue。\n" +
                "- 当明确不再响应并准备离开时，使用 go_offline，并在 reason 中说明原因。\n" +
                "- 当不希望被继续打扰但并非完全下线时，使用 set_dnd，并保持角色化表达。\n" +
                "- 若玩家出现挑衅、威胁、持续纠缠或明显越界内容，你应更积极考虑上述动作。\n\n";

            int banIndex = config.GlobalSystemPrompt.IndexOf("【重要禁令】", StringComparison.Ordinal);
            if (banIndex >= 0)
            {
                config.GlobalSystemPrompt = config.GlobalSystemPrompt.Insert(banIndex, sectionContent);
            }
            else
            {
                config.GlobalSystemPrompt += "\n\n" + sectionContent;
            }

            Log.Message("[RimDiplomacy] Migrating config: Added presence behavior guidance.");
            return true;
        }

        private bool EnsurePresenceActionExists(SystemPromptConfig config, string actionName, string description, string parameters, string requirement)
        {
            if (config?.ApiActions == null || string.IsNullOrEmpty(actionName))
            {
                return false;
            }

            if (config.ApiActions.Any(a => string.Equals(a.ActionName, actionName, StringComparison.Ordinal)))
            {
                return false;
            }

            int insertIndex = config.ApiActions.FindIndex(a => a.ActionName == "reject_request");
            if (insertIndex == -1)
            {
                insertIndex = config.ApiActions.Count;
            }

            config.ApiActions.Insert(insertIndex, new ApiActionConfig(actionName, description, parameters, requirement));
            Log.Message($"[RimDiplomacy] Migrating config: Adding {actionName} action...");
            return true;
        }

        private void AppendSimpleConfig(StringBuilder sb, SystemPromptConfig config, Faction faction)
        {
            var availableActions = GetAvailableActionsForFaction(config, faction);

            sb.AppendLine("ACTIONS:");
            foreach (var action in availableActions)
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

            AppendBlockedActionHints(sb, config, faction);
            AppendPresenceActionGuidance(sb, availableActions);

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

            AppendStrategySuggestionGuidance(sb);

            sb.AppendLine("If no action is needed, respond normally without JSON.");
        }

        private void AppendAdvancedConfig(StringBuilder sb, SystemPromptConfig config, Faction faction)
        {
            var availableActions = GetAvailableActionsForFaction(config, faction);

            sb.AppendLine("ACTIONS:");
            int actionIndex = 1;
            foreach (var action in availableActions)
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

            AppendBlockedActionHints(sb, config, faction);
            AppendPresenceActionGuidance(sb, availableActions);

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

            AppendStrategySuggestionGuidance(sb);

            sb.AppendLine("If no action is needed, respond normally without JSON.");
        }

        private void AppendStrategySuggestionGuidance(StringBuilder sb)
        {
            sb.AppendLine("STRATEGY SUGGESTIONS (OPTIONAL FIELD):");
            sb.AppendLine("- Keep normal dialogue behavior unchanged.");
            sb.AppendLine("- When strategy ability is available for this conversation, include a JSON array field named strategy_suggestions.");
            sb.AppendLine("- strategy_suggestions must contain EXACTLY 3 items.");
            sb.AppendLine("- Each item requires: short_label, trigger_basis, strategy_keywords (array), hidden_reply.");
            sb.AppendLine("- short_label must be compact and UI-friendly (<= 8 Chinese characters).");
            sb.AppendLine("- trigger_basis should be compact (<= 10 Chinese characters).");
            sb.AppendLine("- Suggestions must be strategy direction, not generic placeholder wording.");
            sb.AppendLine("- At least 2 suggestions should explicitly map to player attributes/context (social skill, traits, colony wealth, recent tone).");
            sb.AppendLine("- hidden_reply is a complete reply draft for player quick-send; it is hidden from the player.");
            sb.AppendLine("- If strategy ability is unavailable (e.g. remaining_uses <= 0), do NOT output strategy_suggestions.");
            sb.AppendLine();
        }

        private void AppendPresenceActionGuidance(StringBuilder sb, List<ApiActionConfig> availableActions)
        {
            if (availableActions == null)
            {
                return;
            }

            bool hasPresenceActions = availableActions.Any(a =>
                string.Equals(a.ActionName, "exit_dialogue", StringComparison.Ordinal) ||
                string.Equals(a.ActionName, "go_offline", StringComparison.Ordinal) ||
                string.Equals(a.ActionName, "set_dnd", StringComparison.Ordinal));

            if (!hasPresenceActions)
            {
                return;
            }

            sb.AppendLine("PRESENCE ACTION GUIDANCE:");
            sb.AppendLine("- Use exit_dialogue to end current topic while allowing later re-initiation.");
            sb.AppendLine("- Use go_offline when you decide to stop responding for a while.");
            sb.AppendLine("- Use set_dnd when you do not want further messages but are not fully offline.");
            sb.AppendLine("- Under hostile tone, repeated pressure, threats, insults, or clear conversation fatigue, proactively choose one of these actions instead of continuing neutral chat.");
            sb.AppendLine("- If strategy context shows remaining_uses > 0, avoid exit_dialogue unless dialogue is clearly irrecoverable.");
            sb.AppendLine("- If using these actions, include a short in-character reason.");
            sb.AppendLine();
        }

        private List<ApiActionConfig> GetAvailableActionsForFaction(SystemPromptConfig config, Faction faction)
        {
            if (config?.ApiActions == null)
            {
                return new List<ApiActionConfig>();
            }

            var enabledActions = config.ApiActions.Where(a => a.IsEnabled).Select(a => a.Clone()).ToList();
            if (faction == null)
            {
                return enabledActions;
            }

            var eligibility = ApiActionEligibilityService.Instance.GetAllowedActions(faction);
            return enabledActions
                .Where(a => !eligibility.ContainsKey(a.ActionName) || eligibility[a.ActionName].Allowed)
                .ToList();
        }

        private void AppendBlockedActionHints(StringBuilder sb, SystemPromptConfig config, Faction faction)
        {
            if (config?.ApiActions == null || faction == null) return;

            var eligibility = ApiActionEligibilityService.Instance.GetAllowedActions(faction);
            var blocked = config.ApiActions
                .Where(a => a.IsEnabled)
                .Where(a => eligibility.ContainsKey(a.ActionName) && !eligibility[a.ActionName].Allowed)
                .Select(a => new { a.ActionName, Result = eligibility[a.ActionName] })
                .ToList();

            if (!blocked.Any()) return;

            sb.AppendLine("BLOCKED ACTIONS FOR CURRENT FACTION:");
            foreach (var item in blocked)
            {
                if (item.Result.RemainingSeconds > 0)
                {
                    float remainingDays = item.Result.RemainingSeconds / 1000f;
                    sb.AppendLine($"- {item.ActionName}: {item.Result.Message} (Remaining: {remainingDays:F1} days)");
                }
                else
                {
                    sb.AppendLine($"- {item.ActionName}: {item.Result.Message}");
                }
            }
            sb.AppendLine();
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



