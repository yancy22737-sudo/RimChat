using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using RimChat.Config;
using RimChat.Memory;
using RimChat.DiplomacySystem;
using RimChat.Core;
using RimChat.Relation;
using RimChat.Util;
using RimChat.WorldState;
using RimChat.Prompting;
using RimChat.Prompting.Builders;

namespace RimChat.Persistence
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

        private const string CONFIG_FILE_NAME = "SystemPrompt_Custom.json";

        private SystemPromptConfig _cachedConfig;
        private DateTime _cachedConfigWriteTimeUtc = DateTime.MinValue;
        private readonly object _typedParseWarningLock = new object();
        private readonly HashSet<int> _typedParseIncompleteWarningHashes = new HashSet<int>();
        private readonly HashSet<int> _typedParseFailureWarningHashes = new HashSet<int>();
        private readonly HashSet<int> _typedParseRecoveredInfoHashes = new HashSet<int>();
        private bool _isInitialized;
        private readonly PromptConfigStore _configStore;
        private readonly PromptConfigJsonCodec _configJsonCodec;
        private readonly DiplomacyPromptBuilder _diplomacyPromptBuilder;
        private readonly RpgPromptBuilder _rpgPromptBuilder;

        private PromptPersistenceService()
        {
            _configStore = new PromptConfigStore(() => ConfigFilePath, EnsureDirectoryExists);
            _configJsonCodec = new PromptConfigJsonCodec();
            _diplomacyPromptBuilder = new DiplomacyPromptBuilder(this);
            _rpgPromptBuilder = new RpgPromptBuilder(this);
        }

        /// <summary>/// Promptfoldername
 ///</summary>
        public const string PromptFolderName = "Prompt";

        /// <summary>/// 自定义configuration子foldername
 ///</summary>
        public const string CustomSubFolderName = "Custom";

        /// <summary>/// get自定义configuration基础path (Mod目录下的Prompt/Customfolder)
 ///</summary>
        public string BasePath
        {
            get
            {
                // 优先使用Mod目录下的Prompt/Customfolder
                try
                {
                    var mod = LoadedModManager.GetMod<RimChatMod>();
                    if (mod?.Content != null)
                    {
                        string customDir = Path.Combine(mod.Content.RootDir, PromptFolderName, CustomSubFolderName);
                        // 确保目录presence
                        if (!Directory.Exists(customDir))
                        {
                            Directory.CreateDirectory(customDir);
                        }
                        return customDir;
                    }
                }
                catch { }

                // Fallback: keep prompt custom config under user config path.
                string fallbackDir = Path.Combine(GenFilePaths.ConfigFolderPath, "RimChat", PromptFolderName, CustomSubFolderName);
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
                Log.Message($"[RimChat] PromptPersistenceService initialized, config path: {ConfigFilePath}");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Failed to initialize PromptPersistenceService: {ex}");
                _cachedConfig = CreateDefaultConfig();
            }
        }

        public string GetConfigFilePath()
        {
            return ConfigFilePath;
        }

                public bool ConfigExists()
        {
            return HasAnyCustomDomainFile();
        }


                private bool TryGetConfigLastWriteTimeUtc(out DateTime writeTimeUtc)
        {
            try
            {
                return TryGetDomainConfigLastWriteTimeUtc(out writeTimeUtc);
            }
            catch
            {
                writeTimeUtc = DateTime.MinValue;
                return false;
            }
        }


        private bool IsConfigCacheFresh()
        {
            if (_cachedConfig == null)
            {
                return false;
            }

            if (!TryGetConfigLastWriteTimeUtc(out DateTime writeTimeUtc))
            {
                return false;
            }

            return writeTimeUtc == _cachedConfigWriteTimeUtc;
        }

        private static bool IsPlaceholderGlobalSystemPrompt(SystemPromptConfig config)
        {
            return config != null &&
                string.Equals(
                    config.GlobalSystemPrompt?.Trim() ?? string.Empty,
                    SystemPromptConfig.PlaceholderGlobalSystemPrompt,
                    StringComparison.Ordinal);
        }

        private bool TryRepairPlaceholderGlobalSystemPrompt(ref SystemPromptConfig config, string sourceLabel)
        {
            if (!IsPlaceholderGlobalSystemPrompt(config))
            {
                return false;
            }

            Log.Error($"[RimChat] Detected placeholder GlobalSystemPrompt in {sourceLabel}; rebuilding from default file.");
            SystemPromptConfig rebuilt = CreateDefaultConfig();
            if (rebuilt == null ||
                IsPlaceholderGlobalSystemPrompt(rebuilt) ||
                string.IsNullOrWhiteSpace(rebuilt.GlobalSystemPrompt))
            {
                Log.Error("[RimChat] Failed to rebuild placeholder GlobalSystemPrompt from Prompt/Default/SystemPrompt_Default.json.");
                return false;
            }

            config = rebuilt;
            return true;
        }

                public SystemPromptConfig LoadConfig()
        {
            try
            {
                EnsureDirectoryExists();
                if (IsConfigCacheFresh() && !IsPlaceholderGlobalSystemPrompt(_cachedConfig))
                {
                    return _cachedConfig;
                }

                TryGetConfigLastWriteTimeUtc(out DateTime domainWriteTimeUtc);
                SystemPromptConfig resolvedConfig = TryLoadPromptDomains(out SystemPromptConfig loadedConfig)
                    ? loadedConfig
                    : CreateDefaultConfig();

                bool needsDomainSave = TryRepairPlaceholderGlobalSystemPrompt(ref resolvedConfig, "prompt domain config");
                needsDomainSave |= TryApplyPromptPolicySchemaUpgrade(ref resolvedConfig);
                needsDomainSave |= EnsurePresenceActionExists(
                    resolvedConfig,
                    "publish_public_post",
                    PromptTextConstants.PublishPublicPostActionDescription,
                    PromptTextConstants.PublishPublicPostActionParameters,
                    PromptTextConstants.PublishPublicPostActionRequirement);
                needsDomainSave |= EnsurePresenceActionExists(
                    resolvedConfig,
                    AI.AIActionNames.SendImage,
                    PromptTextConstants.SendImageActionDescription,
                    PromptTextConstants.SendImageActionParameters,
                    PromptTextConstants.SendImageActionRequirement);
                needsDomainSave |= MigratePresenceBehaviorGuidance(resolvedConfig);
                needsDomainSave |= EnsureConfigDefaults(resolvedConfig);

                _cachedConfig = resolvedConfig;
                _cachedConfigWriteTimeUtc = domainWriteTimeUtc;
                if (needsDomainSave && HasAnyCustomDomainFile())
                {
                    SaveConfig(resolvedConfig);
                    Log.Message("[RimChat] Prompt domain migration completed and saved.");
                }

                Log.Message("[RimChat] Loaded SystemPromptConfig from prompt domain files.");
                return resolvedConfig;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Failed to load config: {ex}");
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
                    Log.Warning("[RimChat] Attempted to save null config");
                    return;
                }

                if (TryRepairPlaceholderGlobalSystemPrompt(ref config, "config save request"))
                {
                    Log.Message("[RimChat] Rebuilt placeholder GlobalSystemPrompt before saving custom config.");
                }

                if (IsPlaceholderGlobalSystemPrompt(config))
                {
                    Log.Error("[RimChat] Refusing to save placeholder GlobalSystemPrompt into prompt domain files.");
                    return;
                }

                EnsureConfigDefaults(config);
                SavePromptDomainFiles(config);
                _cachedConfig = config;
                if (!TryGetConfigLastWriteTimeUtc(out _cachedConfigWriteTimeUtc))
                {
                    _cachedConfigWriteTimeUtc = DateTime.MinValue;
                }

                Log.Message($"[RimChat] Saved SystemPromptConfig to: {ConfigFilePath}");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Failed to save config: {ex}");
            }
        }


                public void ResetToDefault()
        {
            try
            {
                DeletePromptDomainCustomFiles();
                RpgPromptCustomStore.DeleteCustomConfig();
                FactionPromptManager.Instance.ResetAllConfigs();
                _cachedConfig = CreateDefaultConfig();
                _cachedConfigWriteTimeUtc = DateTime.MinValue;
                Log.Message("[RimChat] Reset SystemPromptConfig to default");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Failed to reset config: {ex}");
            }
        }
        public bool ExportConfig(string filePath)
        {
            return ExportConfig(filePath, PromptBundleModuleCatalog.All);
        }

        internal bool ExportConfig(string filePath, IEnumerable<PromptBundleModule> selectedModules)
        {
            try
            {
                if (!TryPrepareExportPath(filePath, out string normalizedPath))
                {
                    return false;
                }

                if (_cachedConfig == null)
                {
                    _cachedConfig = LoadConfig();
                }

                PromptBundleConfig bundle = CreatePromptBundle(_cachedConfig, selectedModules);
                string json = PromptDomainJsonUtility.Serialize(bundle, prettyPrint: true);
                File.WriteAllText(normalizedPath, json, Encoding.UTF8);
                Log.Message($"[RimChat] Exported config to: {normalizedPath}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Failed to export config: {ex}");
                return false;
            }
        }

        public bool ImportConfig(string filePath)
        {
            return ImportConfig(filePath, null);
        }

        internal bool ImportConfig(string filePath, IEnumerable<PromptBundleModule> selectedModules)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    Log.Warning("[RimChat] Import path is empty.");
                    return false;
                }

                if (!File.Exists(filePath))
                {
                    Log.Warning($"[RimChat] Import file not found: {filePath}");
                    return false;
                }

                string json = File.ReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    Log.Warning($"[RimChat] Import file is empty: {filePath}");
                    return false;
                }

                if (!TryParsePromptBundle(json, out PromptBundleConfig bundle, out HashSet<PromptBundleModule> includedModules))
                {
                    return false;
                }

                HashSet<PromptBundleModule> modulesToApply = ResolveImportSelection(selectedModules, includedModules);
                if (modulesToApply.Count == 0)
                {
                    Log.Warning("[RimChat] Import skipped because no overlapping module was selected.");
                    return false;
                }

                SavePromptBundle(bundle, modulesToApply);
                _cachedConfig = null;
                _cachedConfigWriteTimeUtc = DateTime.MinValue;
                Log.Message($"[RimChat] Imported config from: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Failed to import config: {ex}");
                return false;
            }
        }

        internal bool TryGetImportPreview(string filePath, out PromptBundleImportPreview preview)
        {
            return TryBuildPromptBundleImportPreview(filePath, out preview);
        }

        private static bool TryPrepareExportPath(string filePath, out string normalizedPath)
        {
            normalizedPath = filePath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                Log.Warning("[RimChat] Export path is empty.");
                return false;
            }

            try
            {
                string directory = Path.GetDirectoryName(normalizedPath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Invalid export path '{normalizedPath}': {ex.Message}");
                return false;
            }
        }

        private static HashSet<PromptBundleModule> ResolveImportSelection(
            IEnumerable<PromptBundleModule> selectedModules,
            HashSet<PromptBundleModule> includedModules)
        {
            if (includedModules == null || includedModules.Count == 0)
            {
                return new HashSet<PromptBundleModule>();
            }

            if (selectedModules == null)
            {
                return new HashSet<PromptBundleModule>(includedModules);
            }

            return new HashSet<PromptBundleModule>(
                selectedModules.Where(item => includedModules.Contains(item)));
        }


        public string BuildFullSystemPrompt(Faction faction, SystemPromptConfig config, bool isProactive, IEnumerable<string> additionalSceneTags)
        {
            return BuildFullSystemPrompt(faction, config, isProactive, additionalSceneTags, null);
        }

        public string BuildFullSystemPrompt(
            Faction faction,
            SystemPromptConfig config,
            bool isProactive,
            IEnumerable<string> additionalSceneTags,
            Pawn playerNegotiator)
        {
            config ??= LoadConfig() ?? CreateDefaultConfig();
            return _diplomacyPromptBuilder.Build(faction, config, isProactive, additionalSceneTags, playerNegotiator);
        }

        public string BuildRPGFullSystemPrompt(Pawn initiator, Pawn target, bool isProactive, IEnumerable<string> additionalSceneTags)
        {
            return _rpgPromptBuilder.Build(initiator, target, isProactive, additionalSceneTags);
        }

        internal string BuildEnvironmentPromptBlocks(SystemPromptConfig config, DialogueScenarioContext context)
        {
            return BuildEnvironmentPromptBlocksInternal(config, context, null);
        }

        internal string BuildEnvironmentPromptBlocksWithDiagnostics(
            SystemPromptConfig config,
            DialogueScenarioContext context,
            out EnvironmentPromptBuildDiagnostics diagnostics)
        {
            diagnostics = new EnvironmentPromptBuildDiagnostics();
            return BuildEnvironmentPromptBlocksInternal(config, context, diagnostics);
        }

        private string BuildEnvironmentPromptBlocksInternal(
            SystemPromptConfig config,
            DialogueScenarioContext context,
            EnvironmentPromptBuildDiagnostics diagnostics)
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
            if (diagnostics != null)
            {
                diagnostics.ScenarioTags.AddRange(tags.OrderBy(tag => tag));
            }

            int maxPerScene = env.SceneSystem.MaxSceneChars > 0 ? env.SceneSystem.MaxSceneChars : int.MaxValue;
            int maxTotalSceneChars = env.SceneSystem.MaxTotalChars > 0 ? env.SceneSystem.MaxTotalChars : int.MaxValue;
            int totalSceneChars = 0;
            int appendedCount = 0;

            var orderedEntries = env.SceneEntries
                .Where(entry => entry != null)
                .OrderByDescending(entry => entry.Priority)
                .ThenBy(entry => entry.Name ?? string.Empty)
                .ToList();

            foreach (ScenePromptEntryConfig entry in orderedEntries)
            {
                EnvironmentSceneEntryDiagnostic sceneDiag = null;
                if (diagnostics != null)
                {
                    sceneDiag = new EnvironmentSceneEntryDiagnostic
                    {
                        Id = entry.Id ?? string.Empty,
                        Name = string.IsNullOrWhiteSpace(entry.Name) ? "UnnamedScene" : entry.Name.Trim(),
                        Priority = entry.Priority
                    };
                    diagnostics.SceneEntries.Add(sceneDiag);
                }

                if (!entry.Enabled)
                {
                    if (sceneDiag != null)
                    {
                        sceneDiag.SkipReason = "disabled";
                    }
                    continue;
                }

                bool channelMatched = context.IsRpg ? entry.ApplyToRPG : entry.ApplyToDiplomacy;
                if (sceneDiag != null)
                {
                    sceneDiag.ChannelMatched = channelMatched;
                }

                if (!channelMatched)
                {
                    if (sceneDiag != null)
                    {
                        sceneDiag.SkipReason = "channel_filtered";
                    }
                    continue;
                }

                bool tagsMatched = EntryMatchesTags(entry, tags);
                if (sceneDiag != null)
                {
                    sceneDiag.TagsMatched = tagsMatched;
                }

                if (!tagsMatched)
                {
                    if (sceneDiag != null)
                    {
                        sceneDiag.SkipReason = "tag_filtered";
                    }
                    continue;
                }

                string content = entry.Content?.Trim() ?? string.Empty;
                if (content.Length == 0)
                {
                    if (sceneDiag != null)
                    {
                        sceneDiag.SkipReason = "empty";
                    }
                    continue;
                }

                content = RenderTemplateVariables(content, context, env, out List<string> usedVariables, out List<string> unknownVariables);
                if (sceneDiag != null)
                {
                    sceneDiag.UsedVariables.AddRange(usedVariables);
                    sceneDiag.UnknownVariables.AddRange(unknownVariables);
                }

                if (content.Length == 0)
                {
                    if (sceneDiag != null)
                    {
                        sceneDiag.SkipReason = "empty_after_render";
                    }
                    continue;
                }

                int originalChars = content.Length;
                if (content.Length > maxPerScene)
                {
                    content = content.Substring(0, maxPerScene);
                    if (sceneDiag != null)
                    {
                        sceneDiag.TruncatedByPerSceneLimit = true;
                    }
                }

                int remain = maxTotalSceneChars - totalSceneChars;
                if (remain <= 0)
                {
                    if (sceneDiag != null)
                    {
                        sceneDiag.SkipReason = "total_limit_exceeded";
                    }
                    if (diagnostics == null)
                    {
                        break;
                    }
                    continue;
                }

                if (content.Length > remain)
                {
                    content = content.Substring(0, remain);
                    if (sceneDiag != null)
                    {
                        sceneDiag.TruncatedByTotalLimit = true;
                    }
                }

                if (content.Length == 0)
                {
                    if (sceneDiag != null)
                    {
                        sceneDiag.SkipReason = "empty_after_limit";
                    }
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

                if (sceneDiag != null)
                {
                    sceneDiag.Included = true;
                    sceneDiag.OriginalChars = originalChars;
                    sceneDiag.AppliedChars = content.Length;
                    sceneDiag.SkipReason = string.Empty;
                }
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
                    Log.Message($"[RimChat] Created prompt directory: {BasePath}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Failed to create directory: {ex}");
            }
        }

                private SystemPromptConfig CreateDefaultConfig()
        {
            if (TryLoadPromptDomains(out SystemPromptConfig domainConfig))
            {
                return domainConfig;
            }

            var legacyConfig = new SystemPromptConfig();
            legacyConfig.InitializeDefaults();
            return legacyConfig;
        }


        private string SerializeConfigToJson(SystemPromptConfig config, bool prettyPrint = false)
        {
            if (_configJsonCodec.TrySerialize(config, prettyPrint, out string typedJson)
                && IsTypedJsonComplete(typedJson, config))
            {
                return typedJson;
            }

            var sb = new StringBuilder();

            if (prettyPrint)
            {
                sb.AppendLine("{");
                sb.AppendLine($"  \"ConfigName\": \"{EscapeJson(config.ConfigName)}\",");
                sb.AppendLine($"  \"GlobalSystemPrompt\": \"{EscapeJson(config.GlobalSystemPrompt)}\",");
                sb.AppendLine($"  \"GlobalDialoguePrompt\": \"{EscapeJson(config.GlobalDialoguePrompt)}\",");
                sb.AppendLine($"  \"UseAdvancedMode\": {config.UseAdvancedMode.ToString().ToLower()},");
                sb.AppendLine($"  \"UseHierarchicalPromptFormat\": {config.UseHierarchicalPromptFormat.ToString().ToLower()},");
                sb.AppendLine($"  \"PromptPolicySchemaVersion\": {config.PromptPolicySchemaVersion},");
                sb.AppendLine($"  \"Enabled\": {config.Enabled.ToString().ToLower()},");
            }
            else
            {
                sb.Append("{");
                sb.Append($"\"ConfigName\":\"{EscapeJson(config.ConfigName)}\",");
                sb.Append($"\"GlobalSystemPrompt\":\"{EscapeJson(config.GlobalSystemPrompt)}\",");
                sb.Append($"\"GlobalDialoguePrompt\":\"{EscapeJson(config.GlobalDialoguePrompt)}\",");
                sb.Append($"\"UseAdvancedMode\":{config.UseAdvancedMode.ToString().ToLower()},");
                sb.Append($"\"UseHierarchicalPromptFormat\":{config.UseHierarchicalPromptFormat.ToString().ToLower()},");
                sb.Append($"\"PromptPolicySchemaVersion\":{config.PromptPolicySchemaVersion},");
                sb.Append($"\"Enabled\":{config.Enabled.ToString().ToLower()},");
            }

            SerializeApiActions(sb, config.ApiActions, prettyPrint);
            SerializeResponseFormat(sb, config.ResponseFormat, prettyPrint);
            SerializeDecisionRules(sb, config.DecisionRules, prettyPrint);
            SerializeEnvironmentPrompt(sb, config.EnvironmentPrompt, prettyPrint);
            SerializePromptTemplates(sb, config.PromptTemplates, prettyPrint);
            SerializePromptPolicy(sb, config.PromptPolicy, prettyPrint);
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

        private static bool IsTypedJsonComplete(string json, SystemPromptConfig config)
        {
            if (string.IsNullOrWhiteSpace(json) || config == null)
            {
                return false;
            }

            if (config.ApiActions != null && config.ApiActions.Count > 0
                && json.IndexOf("\"ApiActions\"", StringComparison.Ordinal) < 0)
            {
                return false;
            }

            if (config.ResponseFormat != null
                && json.IndexOf("\"ResponseFormat\"", StringComparison.Ordinal) < 0)
            {
                return false;
            }

            if (config.PromptTemplates != null
                && json.IndexOf("\"PromptTemplates\"", StringComparison.Ordinal) < 0)
            {
                return false;
            }

            if (config.PromptPolicy != null
                && json.IndexOf("\"PromptPolicy\"", StringComparison.Ordinal) < 0)
            {
                return false;
            }

            return true;
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
                sb.AppendLine($"    \"ImportantRules\": \"{EscapeJson(format.ImportantRules)}\"");
                sb.Append("  },");
            }
            else
            {
                sb.Append(",\"ResponseFormat\":{");
                sb.Append($"\"JsonTemplate\":\"{EscapeJson(format.JsonTemplate)}\",");
                sb.Append($"\"ImportantRules\":\"{EscapeJson(format.ImportantRules)}\"");
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
                sb.AppendLine($"    \"InjectMemoryData\": {config.InjectMemoryData.ToString().ToLower()},");
                sb.AppendLine($"    \"InjectFactionInfo\": {config.InjectFactionInfo.ToString().ToLower()},");
                sb.AppendLine($"    \"CustomInjectionHeader\": \"{EscapeJson(config.CustomInjectionHeader)}\"");
                sb.Append("  }");
            }
            else
            {
                sb.Append(",\"DynamicDataInjection\":{");
                sb.Append($"\"InjectMemoryData\":{config.InjectMemoryData.ToString().ToLower()},");
                sb.Append($"\"InjectFactionInfo\":{config.InjectFactionInfo.ToString().ToLower()},");
                sb.Append($"\"CustomInjectionHeader\":\"{EscapeJson(config.CustomInjectionHeader)}\"");
                sb.Append("}");
            }
        }

        private void SerializePromptTemplates(StringBuilder sb, PromptTemplateTextConfig templates, bool prettyPrint)
        {
            if (templates == null)
            {
                return;
            }

            if (prettyPrint)
            {
                sb.AppendLine();
                sb.AppendLine("  \"PromptTemplates\": {");
                sb.AppendLine($"    \"Enabled\": {templates.Enabled.ToString().ToLower()},");
                sb.AppendLine($"    \"FactGroundingTemplate\": \"{EscapeJson(templates.FactGroundingTemplate)}\",");
                sb.AppendLine($"    \"OutputLanguageTemplate\": \"{EscapeJson(templates.OutputLanguageTemplate)}\",");
                sb.AppendLine($"    \"DiplomacyFallbackRoleTemplate\": \"{EscapeJson(templates.DiplomacyFallbackRoleTemplate)}\",");
                sb.AppendLine($"    \"SocialCircleActionRuleTemplate\": \"{EscapeJson(templates.SocialCircleActionRuleTemplate)}\",");
                sb.AppendLine($"    \"SocialCircleNewsStyleTemplate\": \"{EscapeJson(templates.SocialCircleNewsStyleTemplate)}\",");
                sb.AppendLine($"    \"SocialCircleNewsJsonContractTemplate\": \"{EscapeJson(templates.SocialCircleNewsJsonContractTemplate)}\",");
                sb.AppendLine($"    \"SocialCircleNewsFactTemplate\": \"{EscapeJson(templates.SocialCircleNewsFactTemplate)}\",");
                sb.AppendLine($"    \"DecisionPolicyTemplate\": \"{EscapeJson(templates.DecisionPolicyTemplate)}\",");
                sb.AppendLine($"    \"TurnObjectiveTemplate\": \"{EscapeJson(templates.TurnObjectiveTemplate)}\",");
                sb.AppendLine($"    \"TopicShiftRuleTemplate\": \"{EscapeJson(templates.TopicShiftRuleTemplate)}\",");
                sb.AppendLine($"    \"ApiLimitsNodeTemplate\": \"{EscapeJson(templates.ApiLimitsNodeTemplate)}\",");
                sb.AppendLine($"    \"QuestGuidanceNodeTemplate\": \"{EscapeJson(templates.QuestGuidanceNodeTemplate)}\",");
                sb.AppendLine($"    \"ResponseContractNodeTemplate\": \"{EscapeJson(templates.ResponseContractNodeTemplate)}\"");
                sb.Append("  },");
            }
            else
            {
                sb.Append(",\"PromptTemplates\":{");
                sb.Append($"\"Enabled\":{templates.Enabled.ToString().ToLower()},");
                sb.Append($"\"FactGroundingTemplate\":\"{EscapeJson(templates.FactGroundingTemplate)}\",");
                sb.Append($"\"OutputLanguageTemplate\":\"{EscapeJson(templates.OutputLanguageTemplate)}\",");
                sb.Append($"\"DiplomacyFallbackRoleTemplate\":\"{EscapeJson(templates.DiplomacyFallbackRoleTemplate)}\",");
                sb.Append($"\"SocialCircleActionRuleTemplate\":\"{EscapeJson(templates.SocialCircleActionRuleTemplate)}\",");
                sb.Append($"\"SocialCircleNewsStyleTemplate\":\"{EscapeJson(templates.SocialCircleNewsStyleTemplate)}\",");
                sb.Append($"\"SocialCircleNewsJsonContractTemplate\":\"{EscapeJson(templates.SocialCircleNewsJsonContractTemplate)}\",");
                sb.Append($"\"SocialCircleNewsFactTemplate\":\"{EscapeJson(templates.SocialCircleNewsFactTemplate)}\",");
                sb.Append($"\"DecisionPolicyTemplate\":\"{EscapeJson(templates.DecisionPolicyTemplate)}\",");
                sb.Append($"\"TurnObjectiveTemplate\":\"{EscapeJson(templates.TurnObjectiveTemplate)}\",");
                sb.Append($"\"TopicShiftRuleTemplate\":\"{EscapeJson(templates.TopicShiftRuleTemplate)}\",");
                sb.Append($"\"ApiLimitsNodeTemplate\":\"{EscapeJson(templates.ApiLimitsNodeTemplate)}\",");
                sb.Append($"\"QuestGuidanceNodeTemplate\":\"{EscapeJson(templates.QuestGuidanceNodeTemplate)}\",");
                sb.Append($"\"ResponseContractNodeTemplate\":\"{EscapeJson(templates.ResponseContractNodeTemplate)}\"");
                sb.Append("},");
            }
        }

        private void SerializePromptPolicy(StringBuilder sb, PromptPolicyConfig policy, bool prettyPrint)
        {
            policy ??= PromptPolicyConfig.CreateDefault();

            if (prettyPrint)
            {
                sb.AppendLine();
                sb.AppendLine("  \"PromptPolicy\": {");
                sb.AppendLine($"    \"Enabled\": {policy.Enabled.ToString().ToLower()},");
                sb.AppendLine($"    \"EnableIntentDrivenActionMapping\": {policy.EnableIntentDrivenActionMapping.ToString().ToLower()},");
                sb.AppendLine($"    \"IntentActionCooldownTurns\": {policy.IntentActionCooldownTurns},");
                sb.AppendLine($"    \"IntentMinAssistantRoundsForMemory\": {policy.IntentMinAssistantRoundsForMemory},");
                sb.AppendLine($"    \"IntentNoActionStreakThreshold\": {policy.IntentNoActionStreakThreshold},");
                sb.AppendLine($"    \"ResetPromptCustomOnSchemaUpgrade\": {policy.ResetPromptCustomOnSchemaUpgrade.ToString().ToLower()},");
                sb.AppendLine($"    \"SummaryTimelineTurnLimit\": {policy.SummaryTimelineTurnLimit},");
                sb.AppendLine($"    \"SummaryCharBudget\": {policy.SummaryCharBudget}");
                sb.Append("  },");
            }
            else
            {
                sb.Append(",\"PromptPolicy\":{");
                sb.Append($"\"Enabled\":{policy.Enabled.ToString().ToLower()},");
                sb.Append($"\"EnableIntentDrivenActionMapping\":{policy.EnableIntentDrivenActionMapping.ToString().ToLower()},");
                sb.Append($"\"IntentActionCooldownTurns\":{policy.IntentActionCooldownTurns},");
                sb.Append($"\"IntentMinAssistantRoundsForMemory\":{policy.IntentMinAssistantRoundsForMemory},");
                sb.Append($"\"IntentNoActionStreakThreshold\":{policy.IntentNoActionStreakThreshold},");
                sb.Append($"\"ResetPromptCustomOnSchemaUpgrade\":{policy.ResetPromptCustomOnSchemaUpgrade.ToString().ToLower()},");
                sb.Append($"\"SummaryTimelineTurnLimit\":{policy.SummaryTimelineTurnLimit},");
                sb.Append($"\"SummaryCharBudget\":{policy.SummaryCharBudget}");
                sb.Append("},");
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

        /// <summary>/// 解析 JSON 字符串为 SystemPromptConfig (内部使用)
 ///</summary>
        internal SystemPromptConfig ParseJsonToConfigInternal(string json, string sourceContext = "unknown")
        {
            string source = string.IsNullOrWhiteSpace(sourceContext) ? "unknown" : sourceContext.Trim();
            bool hasApiActionsKey = ContainsJsonKey(json, "ApiActions");
            bool hasResponseFormatKey = ContainsJsonKey(json, "ResponseFormat");
            bool hasDecisionRulesKey = ContainsJsonKey(json, "DecisionRules");
            bool hasPromptTemplatesKey = ContainsJsonKey(json, "PromptTemplates");
            bool hasSchemaAnchors =
                hasApiActionsKey ||
                hasResponseFormatKey ||
                hasDecisionRulesKey ||
                hasPromptTemplatesKey ||
                ContainsJsonKey(json, "ConfigName") ||
                ContainsJsonKey(json, "GlobalSystemPrompt") ||
                ContainsJsonKey(json, "GlobalDialoguePrompt");

            if (_configJsonCodec.TryDeserialize(json, out SystemPromptConfig typedConfig, out string typedError))
            {
                if (IsConfigStructurallyComplete(
                    typedConfig,
                    hasSchemaAnchors,
                    hasApiActionsKey,
                    hasResponseFormatKey,
                    hasDecisionRulesKey,
                    hasPromptTemplatesKey))
                {
                    return typedConfig;
                }

                LogTypedParseIncompleteWarningOnce(
                    hasSchemaAnchors,
                    hasApiActionsKey,
                    hasResponseFormatKey,
                    hasDecisionRulesKey,
                    hasPromptTemplatesKey,
                    typedConfig,
                    source);
            }

            bool hasTypedError = !string.IsNullOrWhiteSpace(typedError);

            SystemPromptConfig fallbackConfig = TryParseCurrentSchemaTextFallback(
                json,
                hasSchemaAnchors,
                hasApiActionsKey,
                hasResponseFormatKey,
                hasDecisionRulesKey,
                hasPromptTemplatesKey);
            if (fallbackConfig != null)
            {
                if (hasTypedError)
                {
                    LogTypedParseRecoveredInfoOnce(source, typedError);
                }
                else
                {
                    Log.Message($"[RimChat] Typed JSON parse was incomplete at source={source}; recovered config using current-schema text fallback.");
                }
            }
            else if (hasTypedError)
            {
                LogTypedParseFailureWarningOnce(source, typedError);
            }

            return fallbackConfig;
        }

        private SystemPromptConfig TryParseCurrentSchemaTextFallback(
            string json,
            bool hasSchemaAnchors,
            bool hasApiActionsKey,
            bool hasResponseFormatKey,
            bool hasDecisionRulesKey,
            bool hasPromptTemplatesKey)
        {
            if (string.IsNullOrWhiteSpace(json) || !hasSchemaAnchors)
            {
                return null;
            }

            try
            {
                var config = new SystemPromptConfig
                {
                    ConfigName = ExtractString(json, "ConfigName"),
                    GlobalSystemPrompt = ExtractString(json, "GlobalSystemPrompt"),
                    GlobalDialoguePrompt = ExtractString(json, "GlobalDialoguePrompt")
                };

                string useAdvancedStr = ExtractValue(json, "UseAdvancedMode");
                if (bool.TryParse(useAdvancedStr, out bool useAdvanced))
                {
                    config.UseAdvancedMode = useAdvanced;
                }

                string useHierarchicalFormatStr = ExtractValue(json, "UseHierarchicalPromptFormat");
                if (bool.TryParse(useHierarchicalFormatStr, out bool useHierarchicalFormat))
                {
                    config.UseHierarchicalPromptFormat = useHierarchicalFormat;
                }

                string enabledStr = ExtractValue(json, "Enabled");
                if (bool.TryParse(enabledStr, out bool enabled))
                {
                    config.Enabled = enabled;
                }

                string schemaVersionStr = ExtractValue(json, "PromptPolicySchemaVersion");
                if (int.TryParse(schemaVersionStr, out int schemaVersion))
                {
                    config.PromptPolicySchemaVersion = schemaVersion;
                }

                ParseApiActions(json, config);
                ParseResponseFormat(json, config);
                ParseDecisionRules(json, config);
                ParseEnvironmentPrompt(json, config);
                ParsePromptTemplates(json, config);
                ParsePromptPolicy(json, config);
                ParseDynamicDataInjection(json, config);

                return IsConfigStructurallyComplete(
                    config,
                    hasSchemaAnchors,
                    hasApiActionsKey,
                    hasResponseFormatKey,
                    hasDecisionRulesKey,
                    hasPromptTemplatesKey)
                    ? config
                    : null;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Current-schema text fallback parse failed: {ex.Message}");
                return null;
            }
        }

        private static bool IsConfigStructurallyComplete(
            SystemPromptConfig config,
            bool hasSchemaAnchors,
            bool hasApiActionsKey,
            bool hasResponseFormatKey,
            bool hasDecisionRulesKey,
            bool hasPromptTemplatesKey)
        {
            if (config == null || !hasSchemaAnchors)
            {
                return false;
            }

            if (hasApiActionsKey && (config.ApiActions == null || config.ApiActions.Count == 0))
            {
                return false;
            }

            if (hasResponseFormatKey && config.ResponseFormat == null)
            {
                return false;
            }

            if (hasResponseFormatKey &&
                (string.IsNullOrWhiteSpace(config.ResponseFormat?.JsonTemplate) ||
                 string.IsNullOrWhiteSpace(config.ResponseFormat?.ImportantRules)))
            {
                return false;
            }

            if (hasDecisionRulesKey && (config.DecisionRules == null || config.DecisionRules.Count == 0))
            {
                return false;
            }

            if (hasPromptTemplatesKey && config.PromptTemplates == null)
            {
                return false;
            }

            return true;
        }

        private static bool ContainsJsonKey(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            return json.IndexOf($"\"{key}\"", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void LogTypedParseIncompleteWarningOnce(
            bool hasSchemaAnchors,
            bool hasApiActionsKey,
            bool hasResponseFormatKey,
            bool hasDecisionRulesKey,
            bool hasPromptTemplatesKey,
            SystemPromptConfig config,
            string sourceContext)
        {
            var missing = new List<string>();
            if (!hasSchemaAnchors)
            {
                missing.Add("schema_anchor");
            }
            if (hasApiActionsKey && (config?.ApiActions == null || config.ApiActions.Count == 0))
            {
                missing.Add("ApiActions");
            }
            if (hasResponseFormatKey && config?.ResponseFormat == null)
            {
                missing.Add("ResponseFormat");
            }
            if (hasResponseFormatKey && string.IsNullOrWhiteSpace(config?.ResponseFormat?.JsonTemplate))
            {
                missing.Add("ResponseFormat.JsonTemplate");
            }
            if (hasResponseFormatKey && string.IsNullOrWhiteSpace(config?.ResponseFormat?.ImportantRules))
            {
                missing.Add("ResponseFormat.ImportantRules");
            }
            if (hasDecisionRulesKey && (config?.DecisionRules == null || config.DecisionRules.Count == 0))
            {
                missing.Add("DecisionRules");
            }
            if (hasPromptTemplatesKey && config?.PromptTemplates == null)
            {
                missing.Add("PromptTemplates");
            }

            string detail = missing.Count > 0 ? string.Join(",", missing) : "unknown";
            string source = string.IsNullOrWhiteSpace(sourceContext) ? "unknown" : sourceContext.Trim();
            string signature = $"typed_incomplete::{source}::{detail}";
            LogTypedParseWarningOnce(
                _typedParseIncompleteWarningHashes,
                signature,
                $"[RimChat] Typed JSON parse produced incomplete config at source={source} (missing: {detail}); config load rejected.");
        }

        private void LogTypedParseFailureWarningOnce(string sourceContext, string typedError)
        {
            string source = string.IsNullOrWhiteSpace(sourceContext) ? "unknown" : sourceContext.Trim();
            string normalizedError = typedError ?? string.Empty;
            string signature = $"typed_fail::{source}::{normalizedError}";
            LogTypedParseWarningOnce(
                _typedParseFailureWarningHashes,
                signature,
                $"[RimChat] Typed JSON parse failed at source={source}; config load rejected: {normalizedError}");
        }

        private void LogTypedParseRecoveredInfoOnce(string sourceContext, string typedError)
        {
            string source = string.IsNullOrWhiteSpace(sourceContext) ? "unknown" : sourceContext.Trim();
            string normalizedError = typedError ?? string.Empty;
            string signature = $"typed_recovered::{source}::{normalizedError}";
            LogTypedParseWarningOnce(
                _typedParseRecoveredInfoHashes,
                signature,
                $"[RimChat] Typed JSON parse failed at source={source}, but fallback recovery succeeded: {normalizedError}",
                logAsWarning: false);
        }

        private void LogTypedParseWarningOnce(
            HashSet<int> warningHashes,
            string signature,
            string message,
            bool logAsWarning = true)
        {
            if (warningHashes == null || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            int hash = (signature ?? string.Empty).GetHashCode();
            bool shouldLog;
            lock (_typedParseWarningLock)
            {
                if (warningHashes.Count > 128)
                {
                    warningHashes.Clear();
                }

                shouldLog = warningHashes.Add(hash);
            }

            if (shouldLog)
            {
                if (logAsWarning)
                {
                    Log.Warning(message);
                }
                else
                {
                    Log.Message(message);
                }
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
                ImportantRules = ExtractString(objContent, "ImportantRules")
            };
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

            string injectMemoryStr = ExtractValue(objContent, "InjectMemoryData");
            if (bool.TryParse(injectMemoryStr, out bool injectMemory))
            {
                config.DynamicDataInjection.InjectMemoryData = injectMemory;
            }

            string injectFactionStr = ExtractValue(objContent, "InjectFactionInfo");
            if (bool.TryParse(injectFactionStr, out bool injectFaction))
            {
                config.DynamicDataInjection.InjectFactionInfo = injectFaction;
            }
        }

        private void ParsePromptTemplates(string json, SystemPromptConfig config)
        {
            if (!TryExtractJsonObject(json, "PromptTemplates", out string templatesContent))
            {
                if (config.PromptTemplates == null)
                {
                    config.PromptTemplates = new PromptTemplateTextConfig();
                }

                return;
            }

            config.PromptTemplates = new PromptTemplateTextConfig
            {
                FactGroundingTemplate = ExtractString(templatesContent, "FactGroundingTemplate"),
                OutputLanguageTemplate = ExtractString(templatesContent, "OutputLanguageTemplate"),
                DiplomacyFallbackRoleTemplate = ExtractString(templatesContent, "DiplomacyFallbackRoleTemplate"),
                SocialCircleActionRuleTemplate = ExtractString(templatesContent, "SocialCircleActionRuleTemplate"),
                SocialCircleNewsStyleTemplate = ExtractString(templatesContent, "SocialCircleNewsStyleTemplate"),
                SocialCircleNewsJsonContractTemplate = ExtractString(templatesContent, "SocialCircleNewsJsonContractTemplate"),
                SocialCircleNewsFactTemplate = ExtractString(templatesContent, "SocialCircleNewsFactTemplate"),
                RpgRoleSettingTemplate = ExtractString(templatesContent, "RpgRoleSettingTemplate"),
                RpgCompactFormatConstraintTemplate = ExtractString(templatesContent, "RpgCompactFormatConstraintTemplate"),
                RpgActionReliabilityRuleTemplate = ExtractString(templatesContent, "RpgActionReliabilityRuleTemplate"),
                DecisionPolicyTemplate = ExtractString(templatesContent, "DecisionPolicyTemplate"),
                TurnObjectiveTemplate = ExtractString(templatesContent, "TurnObjectiveTemplate"),
                OpeningObjectiveTemplate = ExtractString(templatesContent, "OpeningObjectiveTemplate"),
                TopicShiftRuleTemplate = ExtractString(templatesContent, "TopicShiftRuleTemplate"),
                ApiLimitsNodeTemplate = ExtractString(templatesContent, "ApiLimitsNodeTemplate"),
                QuestGuidanceNodeTemplate = ExtractString(templatesContent, "QuestGuidanceNodeTemplate"),
                ResponseContractNodeTemplate = ExtractString(templatesContent, "ResponseContractNodeTemplate")
            };

            string enabledStr = ExtractValue(templatesContent, "Enabled");
            if (bool.TryParse(enabledStr, out bool enabled))
            {
                config.PromptTemplates.Enabled = enabled;
            }
        }

        private void ParsePromptPolicy(string json, SystemPromptConfig config)
        {
            if (!TryExtractJsonObject(json, "PromptPolicy", out string policyContent))
            {
                config.PromptPolicy ??= PromptPolicyConfig.CreateDefault();
                return;
            }

            var policy = PromptPolicyConfig.CreateDefault();
            string enabledStr = ExtractValue(policyContent, "Enabled");
            if (bool.TryParse(enabledStr, out bool enabled))
            {
                policy.Enabled = enabled;
            }

            string intentMappingStr = ExtractValue(policyContent, "EnableIntentDrivenActionMapping");
            if (bool.TryParse(intentMappingStr, out bool intentMapping))
            {
                policy.EnableIntentDrivenActionMapping = intentMapping;
            }

            string cooldownStr = ExtractValue(policyContent, "IntentActionCooldownTurns");
            if (int.TryParse(cooldownStr, out int cooldown))
            {
                policy.IntentActionCooldownTurns = cooldown;
            }

            string minRoundsStr = ExtractValue(policyContent, "IntentMinAssistantRoundsForMemory");
            if (int.TryParse(minRoundsStr, out int minRounds))
            {
                policy.IntentMinAssistantRoundsForMemory = minRounds;
            }

            string streakStr = ExtractValue(policyContent, "IntentNoActionStreakThreshold");
            if (int.TryParse(streakStr, out int streakThreshold))
            {
                policy.IntentNoActionStreakThreshold = streakThreshold;
            }

            string resetStr = ExtractValue(policyContent, "ResetPromptCustomOnSchemaUpgrade");
            if (bool.TryParse(resetStr, out bool reset))
            {
                policy.ResetPromptCustomOnSchemaUpgrade = reset;
            }

            string summaryLimitStr = ExtractValue(policyContent, "SummaryTimelineTurnLimit");
            if (int.TryParse(summaryLimitStr, out int summaryLimit))
            {
                policy.SummaryTimelineTurnLimit = summaryLimit;
            }

            string summaryBudgetStr = ExtractValue(policyContent, "SummaryCharBudget");
            if (int.TryParse(summaryBudgetStr, out int summaryBudget))
            {
                policy.SummaryCharBudget = summaryBudget;
            }

            config.PromptPolicy = policy;
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
                Log.Warning($"[RimChat] 注入记忆数据失败：{ex.Message}");
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

        internal Pawn ResolveBestPlayerNegotiator(Pawn preferredNegotiator)
        {
            if (IsEligiblePlayerNegotiator(preferredNegotiator))
            {
                return preferredNegotiator;
            }

            var candidates = new List<Pawn>();
            IEnumerable<Map> maps = Find.Maps ?? Enumerable.Empty<Map>();
            foreach (Map map in maps.Where(m => m != null && m.IsPlayerHome))
            {
                if (map.mapPawns?.FreeColonistsSpawned == null)
                {
                    continue;
                }

                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    if (IsEligiblePlayerNegotiator(pawn) && !candidates.Contains(pawn))
                    {
                        candidates.Add(pawn);
                    }
                }
            }

            if (candidates.Count == 0)
            {
                foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
                {
                    if (IsEligiblePlayerNegotiator(pawn) && !candidates.Contains(pawn))
                    {
                        candidates.Add(pawn);
                    }
                }
            }

            return candidates
                .OrderByDescending(GetPawnSocialSkillLevel)
                .ThenBy(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShortCap)
                .FirstOrDefault();
        }

        internal string BuildPlayerPawnContextForPrompt(Faction faction, Pawn preferredNegotiator, int maxChars = 900)
        {
            Pawn selected = ResolveBestPlayerNegotiator(preferredNegotiator);
            if (selected == null)
            {
                return string.Empty;
            }

            bool explicitNegotiator = ReferenceEquals(selected, preferredNegotiator);
            string source = explicitNegotiator ? "explicit_negotiator" : "fallback_highest_social_colonist";
            int social = GetPawnSocialSkillLevel(selected);
            string traits = selected.story?.traits?.allTraits == null
                ? "none"
                : string.Join(", ", selected.story.traits.allTraits.Select(t => t.Label).Take(6));

            var sb = new StringBuilder();
            sb.AppendLine("=== PLAYER PAWN PROFILE (REFERENCE ONLY) ===");
            sb.AppendLine($"Source: {source}");
            sb.AppendLine($"Name: {selected.Name?.ToStringFull ?? selected.LabelShort}");
            sb.AppendLine($"Kind: {selected.KindLabel}");
            sb.AppendLine($"Gender: {selected.gender}");
            sb.AppendLine($"Age: {selected.ageTracker?.AgeBiologicalYears}");
            sb.AppendLine($"SocialSkill: {social}");
            sb.AppendLine($"Traits: {traits}");
            if (faction != null && !faction.IsPlayer)
            {
                sb.AppendLine($"TargetFactionRelation: goodwill={faction.PlayerGoodwill} ({GetRelationLabel(faction.PlayerGoodwill)})");
            }
            sb.AppendLine("Usage: treat this as player-side capability context only. Never switch identity.");
            return ClampPromptBlock(sb.ToString(), maxChars);
        }

        internal string BuildPlayerRoyaltySummaryForPrompt(Faction faction, Pawn preferredNegotiator, int maxChars = 1400)
        {
            if (!ModsConfig.RoyaltyActive || faction == null || faction.def != FactionDefOf.Empire)
            {
                return string.Empty;
            }

            Pawn selected = ResolveBestPlayerNegotiator(preferredNegotiator);
            if (selected == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.AppendLine("=== PLAYER EMPIRE ROYALTY CONSTRAINTS ===");
            sb.AppendLine($"ReferencePawn: {selected.Name?.ToStringFull ?? selected.LabelShort}");

            if (selected.royalty == null)
            {
                sb.AppendLine("Honor: unavailable (no royalty tracker)");
                sb.AppendLine("HighestTitle: none");
                sb.AppendLine("Permits: none");
                sb.AppendLine("UnavailableReasons: no empire title context on the player side.");
                sb.AppendLine("EmpireActionGuidance: avoid empire-sensitive create_quest/request_aid; decline in-character and suggest goodwill-building alternatives.");
                return ClampPromptBlock(sb.ToString(), maxChars);
            }

            int honor = selected.royalty.GetFavor(faction);
            RoyalTitle highestTitle = selected.royalty.GetCurrentTitleInFaction(faction);
            if (highestTitle == null && selected.royalty.AllTitlesInEffectForReading != null)
            {
                highestTitle = selected.royalty.AllTitlesInEffectForReading
                    .Where(title => title != null && title.faction == faction)
                    .OrderByDescending(title => title.def?.seniority ?? int.MinValue)
                    .FirstOrDefault();
            }

            string titleLabel = highestTitle?.Label ?? "none";
            List<FactionPermit> permits = selected.royalty.AllFactionPermits == null
                ? new List<FactionPermit>()
                : selected.royalty.AllFactionPermits
                    .Where(permit => permit != null && permit.Faction == faction)
                    .ToList();
            int readyPermits = permits.Count(permit => !permit.OnCooldown);
            int cooldownPermits = permits.Count - readyPermits;

            var blockedReasons = new List<string>();
            if (titleLabel == "none")
            {
                blockedReasons.Add("no active empire title in the current faction");
            }
            if (readyPermits == 0)
            {
                blockedReasons.Add("no ready empire permit");
            }

            sb.AppendLine($"Honor: {honor}");
            sb.AppendLine($"HighestTitle: {titleLabel}");
            sb.AppendLine($"Permits: total={permits.Count}, ready={readyPermits}, cooldown={cooldownPermits}");
            sb.AppendLine($"PermitSamples: {BuildPermitSummaryText(permits)}");
            if (blockedReasons.Count > 0)
            {
                sb.AppendLine($"UnavailableReasons: {string.Join(" | ", blockedReasons)}");
            }
            sb.AppendLine("EmpireActionGuidance:");
            sb.AppendLine("- For empire-sensitive create_quest templates (bestowing/royal paths), require matching title and honor context.");
            sb.AppendLine("- If no suitable title/honor context exists, refuse create_quest in-character and offer alternatives.");
            sb.AppendLine("- If permits are missing or on cooldown, avoid request_aid and explain authority/logistics constraints.");
            sb.AppendLine("- Runtime eligibility remains authoritative; this block is a soft prompt policy.");
            return ClampPromptBlock(sb.ToString(), maxChars);
        }

        internal string BuildFactionSettlementSummaryForPrompt(Faction faction, int maxChars = 0)
        {
            if (faction == null)
            {
                return string.Empty;
            }

            List<Settlement> settlements = Find.WorldObjects?.Settlements?
                .Where(settlement => settlement != null && settlement.Faction == faction)
                .ToList() ?? new List<Settlement>();

            var sb = new StringBuilder();
            sb.AppendLine("=== FACTION SETTLEMENT SUMMARY ===");
            sb.AppendLine($"Faction: {faction.Name}");
            sb.AppendLine($"SettlementCount: {settlements.Count}");

            if (settlements.Count == 0)
            {
                sb.AppendLine("AllSettlements: none");
                sb.AppendLine("SettlementActionGuidance: avoid settlement-dependent trade/quest actions and explain constraints in-character.");
                return ClampPromptBlock(sb.ToString(), maxChars);
            }

            Map homeMap = Find.AnyPlayerHomeMap ?? Find.Maps?.FirstOrDefault(map => map != null && map.IsPlayerHome);
            Settlement nearest = null;
            if (homeMap != null && Find.WorldGrid != null)
            {
                nearest = settlements
                    .OrderBy(settlement => Find.WorldGrid.ApproxDistanceInTiles(homeMap.Tile, settlement.Tile))
                    .FirstOrDefault();
            }

            if (nearest != null && homeMap != null && Find.WorldGrid != null)
            {
                int distance = Mathf.RoundToInt(Find.WorldGrid.ApproxDistanceInTiles(homeMap.Tile, nearest.Tile));
                sb.AppendLine($"NearestToPlayerHome: {nearest.LabelCap} ({distance} tiles)");
            }

            IEnumerable<Settlement> orderedSettlements;
            if (homeMap != null && Find.WorldGrid != null)
            {
                orderedSettlements = settlements
                    .OrderBy(settlement => Find.WorldGrid.ApproxDistanceInTiles(homeMap.Tile, settlement.Tile))
                    .ThenBy(settlement => settlement.LabelCap);
            }
            else
            {
                orderedSettlements = settlements.OrderBy(settlement => settlement.LabelCap);
            }

            sb.AppendLine($"AllSettlements: {string.Join(", ", orderedSettlements.Select(settlement => settlement.LabelCap))}");
            sb.AppendLine("SettlementActionGuidance: settlement-backed actions are allowed only when this summary indicates viable settlement presence.");
            return ClampPromptBlock(sb.ToString(), maxChars);
        }

        private static bool IsEligiblePlayerNegotiator(Pawn pawn)
        {
            return pawn != null
                && pawn.Faction == Faction.OfPlayer
                && pawn.RaceProps?.Humanlike == true
                && !pawn.Dead
                && !pawn.Destroyed;
        }

        private static int GetPawnSocialSkillLevel(Pawn pawn)
        {
            return pawn?.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
        }

        private static string BuildPermitSummaryText(List<FactionPermit> permits)
        {
            if (permits == null || permits.Count == 0)
            {
                return "none";
            }

            return string.Join(", ", permits
                .Take(4)
                .Select(FormatPermitSummaryItem));
        }

        private static string FormatPermitSummaryItem(FactionPermit permit)
        {
            if (permit?.Permit == null)
            {
                return "unknown";
            }

            string state = permit.OnCooldown ? "cooldown" : "ready";
            string title = permit.Title?.label ?? "no_title";
            return $"{permit.Permit.LabelCap} [{state}] (title:{title})";
        }

        private static string ClampPromptBlock(string text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string trimmed = text.Trim();
            if (maxChars <= 0 || trimmed.Length <= maxChars)
            {
                return trimmed;
            }

            return trimmed.Substring(0, maxChars).TrimEnd() + "...";
        }

        public string BuildPawnPersonaBootstrapProfile(Pawn pawn)
        {
            if (pawn == null)
            {
                return "No pawn context.";
            }

            var sb = new StringBuilder();
            sb.AppendLine("=== PERSONA PROFILE (PERSONALITY ONLY) ===");
            sb.AppendLine($"Name: {pawn.Name?.ToStringFull ?? pawn.LabelShort}");
            sb.AppendLine($"Kind: {pawn.KindLabel}");
            sb.AppendLine($"Gender: {pawn.gender}");
            sb.AppendLine($"Age: {pawn.ageTracker?.AgeBiologicalYears}");
            AppendPersonaBackstory(sb, pawn);
            AppendPersonaTraits(sb, pawn);
            AppendPersonaCoreSkills(sb, pawn);
            AppendPersonaFactionContext(sb, pawn);
            sb.AppendLine("Excluded Signals: Health, needs, mood, wounds, equipment, genes, temporary events.");

            return sb.ToString().Trim();
        }

        private static void AppendPersonaBackstory(StringBuilder sb, Pawn pawn)
        {
            if (sb == null || pawn?.story == null)
            {
                return;
            }

            string childhood = pawn.story.Childhood?.title;
            string adulthood = pawn.story.Adulthood?.title;
            if (!string.IsNullOrWhiteSpace(childhood))
            {
                sb.AppendLine($"Backstory (Child): {childhood}");
            }

            if (!string.IsNullOrWhiteSpace(adulthood))
            {
                sb.AppendLine($"Backstory (Adult): {adulthood}");
            }
        }

        private static void AppendPersonaTraits(StringBuilder sb, Pawn pawn)
        {
            if (sb == null || pawn?.story?.traits?.allTraits == null)
            {
                return;
            }

            List<string> traits = pawn.story.traits.allTraits
                .Select(t => t?.Label)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Take(6)
                .ToList();
            if (traits.Count > 0)
            {
                sb.AppendLine($"Traits: {string.Join(", ", traits)}");
            }
        }

        private static void AppendPersonaCoreSkills(StringBuilder sb, Pawn pawn)
        {
            if (sb == null || pawn?.skills?.skills == null)
            {
                return;
            }

            List<string> topSkills = pawn.skills.skills
                .Where(skill => skill?.def != null)
                .OrderByDescending(skill => skill.Level)
                .Take(4)
                .Select(FormatPersonaSkill)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
            if (topSkills.Count > 0)
            {
                sb.AppendLine($"Core Skills: {string.Join(", ", topSkills)}");
            }
        }

        private static string FormatPersonaSkill(SkillRecord skill)
        {
            if (skill?.def == null)
            {
                return string.Empty;
            }

            string passion = string.Empty;
            if (skill.passion == Passion.Major)
            {
                passion = " (major passion)";
            }
            else if (skill.passion == Passion.Minor)
            {
                passion = " (minor passion)";
            }

            return $"{skill.def.skillLabel}:{skill.Level}{passion}";
        }

        private void AppendPersonaFactionContext(StringBuilder sb, Pawn pawn)
        {
            if (sb == null || pawn?.Faction == null)
            {
                return;
            }

            Faction faction = pawn.Faction;
            if (faction.IsPlayer)
            {
                sb.AppendLine("Faction: Player Colony");
            }
            else
            {
                sb.AppendLine($"Faction: {faction.Name} ({faction.def?.label})");
                sb.AppendLine($"Faction Relation with Player: {faction.PlayerGoodwill} ({GetRelationLabel(faction.PlayerGoodwill)})");
            }

            if (faction.leader == pawn)
            {
                sb.AppendLine("Faction Role: Leader");
            }

            if (faction.ideos?.PrimaryIdeo != null)
            {
                sb.AppendLine($"Primary Ideology: {faction.ideos.PrimaryIdeo.name}");
            }
        }

        private void AppendRPGPawnInfo(
            StringBuilder sb,
            Pawn pawn,
            bool isTarget,
            RpgSceneParamSwitchesConfig switches,
            bool includePlayerSharedColonyContext = true,
            bool includeStaticProfileDetails = true)
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
            
            if (includeStaticProfileDetails && pawn.story != null)
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

            if (includeStaticProfileDetails && effectiveSwitches.IncludeSkills)
            {
                AppendRpgSkills(sb, pawn);
            }

            if (includeStaticProfileDetails && effectiveSwitches.IncludeEquipment)
            {
                AppendRpgEquipment(sb, pawn);
            }

            if (includeStaticProfileDetails && effectiveSwitches.IncludeGenes)
            {
                AppendRpgGenes(sb, pawn);
            }

            if (includeStaticProfileDetails && effectiveSwitches.IncludeRecentEvents)
            {
                AppendRpgRecentMemories(sb, pawn);
            }

            if (includePlayerSharedColonyContext)
            {
                AppendPlayerColonyContextIfEnabled(sb, pawn, effectiveSwitches);
            }
            
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
                .Select(memory => memory.LabelCap)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Distinct()
                .Take(5)
                .ToList();

            if (values.Count > 0)
            {
                sb.AppendLine($"Recent Memories: {string.Join(", ", values)}");
            }
        }

        private void AppendRPGFactionContext(StringBuilder sb, Pawn pawn)
        {
            if (pawn.Faction == null) return;
            bool isTarget = pawn.IsColonist || pawn.IsPrisoner || pawn.IsSlave; // Roughly
            sb.AppendLine(isTarget ? "=== YOUR FACTION CONTEXT ===" : "=== INTERLOCUTOR FACTION CONTEXT ===");
            if (pawn.Faction.IsPlayer)
            {
                sb.AppendLine("Faction: Player Colony (Your own people)");
            }
            else
            {
                sb.AppendLine($"Faction: {pawn.Faction.Name} ({pawn.Faction.def?.label})");
                sb.AppendLine($"Faction Relations with Player: {pawn.Faction.PlayerGoodwill} ({GetRelationLabel(pawn.Faction.PlayerGoodwill)})");
            }
            
            if (pawn.Faction.ideos?.PrimaryIdeo != null)
            {
                sb.AppendLine($"Primary Ideology: {pawn.Faction.ideos.PrimaryIdeo.name}");
            }
            sb.AppendLine();
        }

        private void AppendApiLimits(StringBuilder sb, Faction faction = null)
        {
            var settings = RimChatMod.Settings ?? RimChatMod.Instance?.InstanceSettings;
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
            sb.AppendLine($"- Min goodwill for aid request: {settings.MinGoodwillForAid}");
            sb.AppendLine($"- Max goodwill for war declaration: {settings.MaxGoodwillForWarDeclaration}");
            sb.AppendLine($"- Max peace cost: {settings.MaxPeaceCost}");
            sb.AppendLine($"- Peace goodwill reset: {settings.PeaceGoodwillReset}");
            sb.AppendLine($"- Create quest cooldown: {settings.MinQuestCooldownDays} to {settings.MaxQuestCooldownDays} days");
            sb.AppendLine();
            sb.AppendLine("ENABLED FEATURES:");
            sb.AppendLine($"- Goodwill adjustment: {(settings.EnableAIGoodwillAdjustment ? "YES" : "NO")}");
            sb.AppendLine($"- War declaration: {(settings.EnableAIWarDeclaration ? "YES" : "NO")}");
            sb.AppendLine($"- Peace making: {(settings.EnableAIPeaceMaking ? "YES" : "NO")}");
            sb.AppendLine($"- Trade caravan: {(settings.EnableAITradeCaravan ? "YES" : "NO")}");
            sb.AppendLine($"- Aid request: {(settings.EnableAIAidRequest ? "YES" : "NO")}");
            sb.AppendLine("- Quest creation: YES");
            sb.AppendLine();
        }

        /// <summary>/// Build dynamic quest availability from centralized eligibility service.
 ///</summary>
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

            string sectionContent = LoadPresenceBehaviorGuidanceSection();
            if (string.IsNullOrWhiteSpace(sectionContent))
            {
                Log.Warning("[RimChat] Presence behavior guidance template missing or empty; migration skipped.");
                return false;
            }

            int banIndex = config.GlobalSystemPrompt.IndexOf("【重要禁令】", StringComparison.Ordinal);
            if (banIndex >= 0)
            {
                config.GlobalSystemPrompt = config.GlobalSystemPrompt.Insert(banIndex, sectionContent);
            }
            else
            {
                config.GlobalSystemPrompt += "\n\n" + sectionContent;
            }

            Log.Message("[RimChat] Migrating config: Added presence behavior guidance.");
            return true;
        }

        private string LoadPresenceBehaviorGuidanceSection()
        {
            const string sectionTitle = "【在线状态策略】";
            string defaultPrompt = CreateDefaultConfig()?.GlobalSystemPrompt;
            if (string.IsNullOrWhiteSpace(defaultPrompt))
            {
                return string.Empty;
            }

            string normalizedText = defaultPrompt.Replace("\r\n", "\n");
            string[] lines = normalizedText.Split('\n');
            var sectionLines = new List<string>();
            bool inSection = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                if (!inSection)
                {
                    if (!string.Equals(trimmed, sectionTitle, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    inSection = true;
                    sectionLines.Add(trimmed);
                    continue;
                }

                if (trimmed.StartsWith("-", StringComparison.Ordinal))
                {
                    sectionLines.Add(trimmed);
                    continue;
                }

                break;
            }

            if (sectionLines.Count <= 1)
            {
                return string.Empty;
            }

            return string.Join("\n", sectionLines) + "\n\n";
        }

        private bool TryApplyPromptPolicySchemaUpgrade(ref SystemPromptConfig config)
        {
            if (config == null)
            {
                return false;
            }

            int current = SystemPromptConfig.CurrentPromptPolicySchemaVersion;
            int loaded = config.PromptPolicySchemaVersion;
            config.PromptPolicy ??= PromptPolicyConfig.CreateDefault();
            if (loaded >= current)
            {
                return false;
            }

            if (config.PromptPolicy.ResetPromptCustomOnSchemaUpgrade)
            {
                Log.Warning(
                    $"[RimChat] Prompt policy schema upgrade detected ({loaded} -> {current}). " +
                    "Resetting prompt custom overrides to new defaults.");
                config = CreateDefaultConfig();
                config.PromptPolicySchemaVersion = current;
                config.PromptPolicy ??= PromptPolicyConfig.CreateDefault();
                return true;
            }

            config.PromptPolicySchemaVersion = current;
            if (config.PromptPolicy == null)
            {
                config.PromptPolicy = PromptPolicyConfig.CreateDefault();
            }

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
            Log.Message($"[RimChat] Migrating config: Adding {actionName} action...");
            return true;
        }

        private bool EnsureConfigDefaults(SystemPromptConfig config)
        {
            if (config == null)
            {
                return false;
            }

            SystemPromptConfig defaults = CreateDefaultConfig();
            if (defaults == null)
            {
                return false;
            }

            bool changed = false;
            changed |= EnsureApiActionDefaults(config, defaults);
            changed |= EnsureResponseFormatDefaults(config, defaults);
            changed |= EnsureDecisionRuleDefaults(config, defaults);
            changed |= EnsureEnvironmentPromptDefaults(config, defaults);
            changed |= EnsureDynamicInjectionDefaults(config, defaults);
            changed |= EnsurePromptTemplateDefaults(config, defaults);
            changed |= EnsurePromptPolicyDefaults(config, defaults);
            return changed;
        }

        private bool EnsureApiActionDefaults(SystemPromptConfig config, SystemPromptConfig defaults)
        {
            if (defaults?.ApiActions == null || defaults.ApiActions.Count == 0)
            {
                return false;
            }

            config.ApiActions ??= new List<ApiActionConfig>();
            bool changed = false;
            changed |= RemoveDeprecatedPromptAction(config, "send_gift");
            foreach (ApiActionConfig defAction in defaults.ApiActions)
            {
                ApiActionConfig target = config.ApiActions.FirstOrDefault(
                    a => string.Equals(a.ActionName, defAction.ActionName, StringComparison.Ordinal));
                if (target == null)
                {
                    config.ApiActions.Add(defAction.Clone());
                    changed = true;
                    continue;
                }

                changed |= AssignIfMissing(ref target.Description, defAction.Description);
                changed |= AssignIfMissing(ref target.Parameters, defAction.Parameters);
                changed |= AssignIfMissing(ref target.Requirement, defAction.Requirement);
                changed |= TryUpgradeLegacyMakePeaceAction(target, defAction);
            }

            return changed;
        }

        private static bool TryUpgradeLegacyMakePeaceAction(ApiActionConfig target, ApiActionConfig defAction)
        {
            if (target == null || !string.Equals(target.ActionName, "make_peace", StringComparison.Ordinal))
            {
                return false;
            }

            bool changed = false;
            if (IsLegacyMakePeaceDescription(target.Description) && !string.IsNullOrWhiteSpace(defAction?.Description))
            {
                target.Description = defAction.Description;
                changed = true;
            }

            if (IsLegacyMakePeaceRequirement(target.Requirement) && !string.IsNullOrWhiteSpace(defAction?.Requirement))
            {
                target.Requirement = defAction.Requirement;
                changed = true;
            }

            return changed;
        }

        private static bool IsLegacyMakePeaceDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return true;
            }

            string value = description.Trim();
            return string.Equals(value, "Offer peace treaty", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Offer peace treaty (requires war)", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "offer peace", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLegacyMakePeaceRequirement(string requirement)
        {
            if (string.IsNullOrWhiteSpace(requirement))
            {
                return true;
            }

            string value = requirement.Trim().ToLowerInvariant();
            return value == "already at war"
                || value == "requires war"
                || value.Contains("already at war") && !ContainsSincerityConstraint(requirement);
        }

        private static bool RemoveDeprecatedPromptAction(SystemPromptConfig config, string actionName)
        {
            if (config?.ApiActions == null || string.IsNullOrWhiteSpace(actionName))
            {
                return false;
            }

            int removedCount = config.ApiActions.RemoveAll(action =>
                string.Equals(action?.ActionName, actionName, StringComparison.Ordinal));
            if (removedCount <= 0)
            {
                return false;
            }

            Log.Message($"[RimChat] Migrating config: Removing deprecated prompt action '{actionName}'.");
            return true;
        }

        private bool EnsureResponseFormatDefaults(SystemPromptConfig config, SystemPromptConfig defaults)
        {
            if (defaults?.ResponseFormat == null)
            {
                return false;
            }

            if (config.ResponseFormat == null)
            {
                config.ResponseFormat = defaults.ResponseFormat.Clone();
                return true;
            }

            bool changed = false;
            changed |= AssignIfMissing(ref config.ResponseFormat.JsonTemplate, defaults.ResponseFormat.JsonTemplate);
            changed |= AssignIfMissing(ref config.ResponseFormat.ImportantRules, defaults.ResponseFormat.ImportantRules);
            return changed;
        }

        private bool EnsureDecisionRuleDefaults(SystemPromptConfig config, SystemPromptConfig defaults)
        {
            if (defaults?.DecisionRules == null || defaults.DecisionRules.Count == 0)
            {
                return false;
            }

            config.DecisionRules ??= new List<DecisionRuleConfig>();
            bool changed = false;
            foreach (DecisionRuleConfig defRule in defaults.DecisionRules)
            {
                DecisionRuleConfig target = config.DecisionRules.FirstOrDefault(
                    r => string.Equals(r.RuleName, defRule.RuleName, StringComparison.Ordinal));
                if (target == null)
                {
                    config.DecisionRules.Add(defRule.Clone());
                    changed = true;
                    continue;
                }

                changed |= AssignIfMissing(ref target.RuleContent, defRule.RuleContent);
            }

            return changed;
        }

        private bool EnsureEnvironmentPromptDefaults(SystemPromptConfig config, SystemPromptConfig defaults)
        {
            if (defaults?.EnvironmentPrompt == null)
            {
                return false;
            }

            if (config.EnvironmentPrompt == null)
            {
                config.EnvironmentPrompt = defaults.EnvironmentPrompt.Clone();
                return true;
            }

            bool changed = false;
            changed |= EnsureWorldviewDefaults(config.EnvironmentPrompt, defaults.EnvironmentPrompt);
            changed |= EnsureSceneDefaults(config.EnvironmentPrompt, defaults.EnvironmentPrompt);
            changed |= EnsureEnvSwitchDefaults(config.EnvironmentPrompt, defaults.EnvironmentPrompt);
            changed |= EnsureRpgSwitchDefaults(config.EnvironmentPrompt, defaults.EnvironmentPrompt);
            changed |= EnsureEventIntelDefaults(config.EnvironmentPrompt, defaults.EnvironmentPrompt);
            return changed;
        }

        private bool EnsureWorldviewDefaults(EnvironmentPromptConfig target, EnvironmentPromptConfig defaults)
        {
            if (defaults?.Worldview == null)
            {
                return false;
            }

            if (target.Worldview == null)
            {
                target.Worldview = defaults.Worldview.Clone();
                return true;
            }

            return AssignIfMissing(ref target.Worldview.Content, defaults.Worldview.Content);
        }

        private bool EnsureSceneDefaults(EnvironmentPromptConfig target, EnvironmentPromptConfig defaults)
        {
            if (defaults?.SceneEntries == null || defaults.SceneEntries.Count == 0)
            {
                return false;
            }

            target.SceneEntries ??= new List<ScenePromptEntryConfig>();
            if (target.SceneEntries.Count > 0)
            {
                return false;
            }

            target.SceneEntries = defaults.SceneEntries.Select(entry => entry.Clone()).ToList();
            return true;
        }

        private bool EnsureEnvSwitchDefaults(EnvironmentPromptConfig target, EnvironmentPromptConfig defaults)
        {
            if (defaults?.EnvironmentContextSwitches == null || target.EnvironmentContextSwitches != null)
            {
                return false;
            }

            target.EnvironmentContextSwitches = defaults.EnvironmentContextSwitches.Clone();
            return true;
        }

        private bool EnsureRpgSwitchDefaults(EnvironmentPromptConfig target, EnvironmentPromptConfig defaults)
        {
            if (defaults?.RpgSceneParamSwitches == null || target.RpgSceneParamSwitches != null)
            {
                return false;
            }

            target.RpgSceneParamSwitches = defaults.RpgSceneParamSwitches.Clone();
            return true;
        }

        private bool EnsureEventIntelDefaults(EnvironmentPromptConfig target, EnvironmentPromptConfig defaults)
        {
            if (defaults?.EventIntelPrompt == null || target.EventIntelPrompt != null)
            {
                return false;
            }

            target.EventIntelPrompt = defaults.EventIntelPrompt.Clone();
            return true;
        }

        private bool EnsureDynamicInjectionDefaults(SystemPromptConfig config, SystemPromptConfig defaults)
        {
            if (defaults?.DynamicDataInjection == null)
            {
                return false;
            }

            if (config.DynamicDataInjection == null)
            {
                config.DynamicDataInjection = defaults.DynamicDataInjection.Clone();
                return true;
            }

            return AssignIfMissing(
                ref config.DynamicDataInjection.CustomInjectionHeader,
                defaults.DynamicDataInjection.CustomInjectionHeader);
        }

        private bool EnsurePromptTemplateDefaults(SystemPromptConfig config, SystemPromptConfig defaults)
        {
            if (config == null)
            {
                return false;
            }

            PromptTemplateTextConfig templateDefaults = defaults?.PromptTemplates;
            if (templateDefaults == null)
            {
                return false;
            }

            config.PromptTemplates ??= new PromptTemplateTextConfig();
            PromptTemplateTextConfig target = config.PromptTemplates;
            bool changed = false;

            changed |= AssignIfMissing(ref target.FactGroundingTemplate, templateDefaults.FactGroundingTemplate);
            changed |= AssignIfMissing(ref target.OutputLanguageTemplate, templateDefaults.OutputLanguageTemplate);
            changed |= AssignIfMissing(ref target.DiplomacyFallbackRoleTemplate, templateDefaults.DiplomacyFallbackRoleTemplate);
            changed |= AssignIfMissing(ref target.SocialCircleActionRuleTemplate, templateDefaults.SocialCircleActionRuleTemplate);
            changed |= AssignIfMissing(ref target.SocialCircleNewsStyleTemplate, templateDefaults.SocialCircleNewsStyleTemplate);
            changed |= AssignIfMissing(ref target.SocialCircleNewsJsonContractTemplate, templateDefaults.SocialCircleNewsJsonContractTemplate);
            changed |= AssignIfMissing(ref target.SocialCircleNewsFactTemplate, templateDefaults.SocialCircleNewsFactTemplate);
            changed |= AssignIfMissing(ref target.DecisionPolicyTemplate, templateDefaults.DecisionPolicyTemplate);
            changed |= AssignIfMissing(ref target.TurnObjectiveTemplate, templateDefaults.TurnObjectiveTemplate);
            changed |= AssignIfMissing(ref target.TopicShiftRuleTemplate, templateDefaults.TopicShiftRuleTemplate);
            changed |= AssignIfMissing(ref target.ApiLimitsNodeTemplate, templateDefaults.ApiLimitsNodeTemplate);
            changed |= AssignIfMissing(ref target.QuestGuidanceNodeTemplate, templateDefaults.QuestGuidanceNodeTemplate);
            changed |= AssignIfMissing(ref target.ResponseContractNodeTemplate, templateDefaults.ResponseContractNodeTemplate);

            if (changed)
            {
                Log.Message("[RimChat] Migrating config: Filled missing PromptTemplates fields from default template file.");
            }

            return changed;
        }

        private bool EnsurePromptPolicyDefaults(SystemPromptConfig config, SystemPromptConfig defaults)
        {
            if (config == null)
            {
                return false;
            }

            PromptPolicyConfig defaultPolicy = defaults?.PromptPolicy ?? PromptPolicyConfig.CreateDefault();
            if (defaultPolicy == null)
            {
                return false;
            }

            bool changed = false;
            if (config.PromptPolicy == null)
            {
                config.PromptPolicy = defaultPolicy.Clone();
                changed = true;
            }

            PromptPolicyConfig target = config.PromptPolicy;
            changed |= AssignIfLessOrEqualZero(ref target.IntentActionCooldownTurns, defaultPolicy.IntentActionCooldownTurns);
            changed |= AssignIfLessOrEqualZero(ref target.IntentMinAssistantRoundsForMemory, defaultPolicy.IntentMinAssistantRoundsForMemory);
            changed |= AssignIfLessOrEqualZero(ref target.IntentNoActionStreakThreshold, defaultPolicy.IntentNoActionStreakThreshold);
            changed |= AssignIfLessOrEqualZero(ref target.SummaryTimelineTurnLimit, defaultPolicy.SummaryTimelineTurnLimit);
            changed |= AssignIfLessOrEqualZero(ref target.SummaryCharBudget, defaultPolicy.SummaryCharBudget);

            int schemaVersion = config.PromptPolicySchemaVersion;
            if (schemaVersion <= 0)
            {
                config.PromptPolicySchemaVersion = SystemPromptConfig.CurrentPromptPolicySchemaVersion;
                changed = true;
            }

            return changed;
        }

        private static bool AssignIfMissing(ref string target, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(fallback))
            {
                return false;
            }

            target = fallback;
            return true;
        }

        private static bool AssignIfLessOrEqualZero(ref int target, int fallback)
        {
            if (target > 0 || fallback <= 0)
            {
                return false;
            }

            target = fallback;
            return true;
        }

        private void AppendSimpleConfig(StringBuilder sb, SystemPromptConfig config, Faction faction)
        {
            AppendCompactDiplomacyResponseContract(sb, config, faction);
        }

        private void AppendAdvancedConfig(StringBuilder sb, SystemPromptConfig config, Faction faction)
        {
            AppendCompactDiplomacyResponseContract(sb, config, faction);
        }

        private void AppendCompactDiplomacyResponseContract(StringBuilder sb, SystemPromptConfig config, Faction faction)
        {
            List<ApiActionConfig> availableActions = GetAvailableActionsForFaction(config, faction);
            AppendDiplomacyResponseFormatSection(sb, config);
            AppendDiplomacyCriticalActionRules(sb);
            AppendCompactActionCatalog(sb, availableActions);
            AppendBlockedActionHints(sb, config, faction);
            AppendGoodwillPeacePolicyHints(sb, faction);
            AppendPresenceActionGuidance(sb, availableActions);
            AppendStrategySuggestionGuidance(sb);
            sb.AppendLine(PromptTextConstants.NoActionResponseHint);
        }

        private void AppendDiplomacyResponseFormatSection(StringBuilder sb, SystemPromptConfig config)
        {
            sb.AppendLine(PromptTextConstants.ResponseFormatHeader);
            sb.AppendLine(PromptTextConstants.ResponseFormatIntro);
            sb.AppendLine(config?.ResponseFormat?.JsonTemplate ?? string.Empty);
            sb.AppendLine();
        }

        private void AppendDiplomacyCriticalActionRules(StringBuilder sb)
        {
            sb.AppendLine(PromptTextConstants.CriticalActionRulesHeader);
            sb.AppendLine("- If you use gameplay actions, append exactly one raw JSON object after the dialogue using the {\"actions\":[...]} contract.");
            sb.AppendLine("- Do not use legacy single-action wrappers such as {\"action\":\"...\",\"parameters\":{...},\"response\":\"...\"}; only the actions array contract is valid.");
            sb.AppendLine("- The natural-language ban on AI identity, stats, or game mechanics applies only to dialogue text. Parser-facing JSON is allowed when needed.");
            sb.AppendLine("- Never narrate a gameplay effect as already executed unless the same reply includes the matching JSON action.");
            sb.AppendLine("- request_caravan/request_aid/request_raid/create_quest/trigger_incident are delayed or system-mediated; speak as intent or scheduling, not completed arrival/results.");
            sb.AppendLine("- Only adjust_goodwill may change goodwill from dialogue tone or context.");
            sb.AppendLine("- request_caravan and request_aid already apply fixed system goodwill costs on success; do not add adjust_goodwill just to represent those costs.");
            sb.AppendLine("- create_quest already applies a fixed -10 goodwill cost on success; do not add adjust_goodwill just to represent task issuance cost.");
            sb.AppendLine("- Do not invent exact arrival times, coordinates, frequencies, cargo manifests, or confirmations unless they are present in prompt facts.");
            sb.AppendLine("- Use reject_request only for explicit player requests that you are formally declining. For ordinary disagreement or caution, refuse naturally in character without the action.");
            sb.AppendLine("- publish_public_post is a high-impact world-facing action; use it sparingly, not for routine chat or private bargaining.");
            sb.AppendLine("- A brief low-information reply does not require a presence action by itself. Escalate closure gradually unless there is clear harassment, boundary crossing, or strong hostility.");
            sb.AppendLine();
        }

        private void AppendCompactActionCatalog(StringBuilder sb, List<ApiActionConfig> availableActions)
        {
            if (availableActions == null || availableActions.Count == 0)
            {
                return;
            }

            sb.AppendLine(PromptTextConstants.ActionsHeader);
            foreach (ApiActionConfig action in availableActions)
            {
                string line = BuildCompactActionLine(action);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    sb.AppendLine(line);
                }
            }
            sb.AppendLine();
        }

        private static string BuildCompactActionLine(ApiActionConfig action)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.ActionName))
            {
                return string.Empty;
            }

            string parameters = BuildCompactActionParameterHint(action.ActionName);
            string requirement = BuildCompactActionRequirementHint(action);
            string description = BuildCompactActionDescriptionHint(action);
            string signature = string.IsNullOrWhiteSpace(parameters)
                ? action.ActionName
                : $"{action.ActionName}({parameters})";
            string gate = string.IsNullOrWhiteSpace(requirement)
                ? string.Empty
                : $" [{requirement}]";
            return $"- {signature}{gate}: {description}";
        }

        private static string BuildCompactActionParameterHint(string actionName)
        {
            switch (actionName)
            {
                case "adjust_goodwill":
                    return "amount, reason";
                case "request_aid":
                    return "type";
                case "declare_war":
                    return "reason";
                case "make_peace":
                    return "cost?";
                case "request_caravan":
                    return "goods?";
                case "request_raid":
                    return "strategy?(ImmediateAttack/ImmediateAttackSmart/StageThenAttack/ImmediateAttackSappers/Siege), arrival?(EdgeWalkIn/EdgeDrop/EdgeWalkInGroups/RandomDrop/CenterDrop)";
                case "trigger_incident":
                    return "defName, amount?";
                case "create_quest":
                    return "questDefName, points?";
                case "send_image":
                    return "template_id, extra_prompt?, caption?, size?, watermark?";
                case "reject_request":
                case "exit_dialogue":
                case "go_offline":
                case "set_dnd":
                    return "reason?";
                case "publish_public_post":
                    return "category, sentiment, summary?";
                default:
                    return string.Empty;
            }
        }

        private static string BuildCompactActionRequirementHint(ApiActionConfig action)
        {
            string actionName = action?.ActionName;
            if (string.IsNullOrWhiteSpace(actionName))
            {
                return string.Empty;
            }

            string configured = NormalizeCompactActionText(action.Requirement, 120);
            if (actionName == "make_peace")
            {
                return MergeMakePeaceRequirement(configured);
            }

            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            switch (actionName)
            {
                case "request_aid":
                    return "aid threshold met";
                case "declare_war":
                    return "war threshold met";
                case "make_peace":
                    return "already at war";
                case "request_caravan":
                    return "not hostile";
                case "request_raid":
                    return "hostile only";
                case "create_quest":
                    return "exact available questDefName only";
                case "send_image":
                    return "image API configured + template_id required + one image per turn";
                case "publish_public_post":
                    return "public, world-facing, and sparing";
                case "reject_request":
                    return "explicit request refusal only";
                default:
                    return string.Empty;
            }
        }

        private static string BuildCompactActionDescriptionHint(ApiActionConfig action)
        {
            string actionName = action?.ActionName;
            if (string.IsNullOrWhiteSpace(actionName))
            {
                return string.Empty;
            }

            string configured = NormalizeCompactActionText(action.Description, 180);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            switch (actionName)
            {
                case "adjust_goodwill":
                    return "change faction relation";
                case "request_aid":
                    return "schedule aid (fixed goodwill cost on success)";
                case "declare_war":
                    return "switch to war";
                case "make_peace":
                    return "offer peace only when the player shows very high sincerity";
                case "request_caravan":
                    return "schedule a trade caravan (fixed goodwill cost on success)";
                case "request_raid":
                    return "schedule a raid";
                case "trigger_incident":
                    return "trigger a game incident";
                case "create_quest":
                    return "start a native quest (fixed -10 goodwill on success)";
                case "send_image":
                    return "generate and return one diplomacy image card via image API";
                case "reject_request":
                    return "formally refuse an explicit player request";
                case "publish_public_post":
                    return "publish a high-impact public social post";
                case "exit_dialogue":
                    return "end the current topic";
                case "go_offline":
                    return "leave and go offline";
                case "set_dnd":
                    return "stop further contact";
                default:
                    return actionName;
            }
        }

        private static string NormalizeCompactActionText(string text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string normalized = Regex.Replace(text, "\\s+", " ").Trim();
            if (normalized.Length <= maxChars)
            {
                return normalized;
            }

            return normalized.Substring(0, maxChars).TrimEnd() + "...";
        }

        private static string MergeMakePeaceRequirement(string configured)
        {
            const string hardRule = "already at war + very high sincerity only";
            if (string.IsNullOrWhiteSpace(configured))
            {
                return hardRule;
            }

            if (ContainsSincerityConstraint(configured))
            {
                return configured;
            }

            return configured + "; very high sincerity only";
        }

        private static bool ContainsSincerityConstraint(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string lower = text.ToLowerInvariant();
            return lower.Contains("sincer") || text.Contains("真诚") || text.Contains("诚意");
        }

        private void AppendStrategySuggestionGuidance(StringBuilder sb)
        {
            sb.AppendLine("STRATEGY SUGGESTIONS (OPTIONAL):");
            sb.AppendLine("- Only when strategy ability is available, you may add strategy_suggestions with EXACTLY 3 items.");
            sb.AppendLine("- Each item must be {\"strategy_name\":\"\",\"reason\":\"\",\"content\":\"\"}.");
            sb.AppendLine("- Keep them fact-grounded, compact, and hidden in JSON only.");
            sb.AppendLine("- Never print visible strategy bullet lists in dialogue text.");
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
            sb.AppendLine("- Brief low-information replies can receive a short in-character answer without ending the conversation.");
            sb.AppendLine("- exit_dialogue: use for natural closure or repeated low-information replies after you have already responded.");
            sb.AppendLine("- go_offline / set_dnd: use for harassment, repeated boundary crossing, serious offense, or when you are clearly ending contact.");
            sb.AppendLine("- If you use a presence action, include a short in-character reason.");
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
                .Where(a => !ShouldHideActionFromPromptByProjectedGoodwill(faction, a.ActionName))
                .Where(a => !eligibility.ContainsKey(a.ActionName) || eligibility[a.ActionName].Allowed)
                .ToList();
        }

        private void AppendBlockedActionHints(StringBuilder sb, SystemPromptConfig config, Faction faction)
        {
            if (config?.ApiActions == null || faction == null) return;

            var eligibility = ApiActionEligibilityService.Instance.GetAllowedActions(faction);
            var blocked = config.ApiActions
                .Where(a => a.IsEnabled)
                .Select(a => new
                {
                    a.ActionName,
                    ProjectedGoodwillReason = GetProjectedGoodwillBlockReason(faction, a.ActionName),
                    Eligibility = eligibility.ContainsKey(a.ActionName) ? eligibility[a.ActionName] : null
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.ProjectedGoodwillReason) || (item.Eligibility != null && !item.Eligibility.Allowed))
                .ToList();

            if (!blocked.Any()) return;

            sb.AppendLine("BLOCKED ACTIONS FOR CURRENT FACTION:");
            foreach (var item in blocked)
            {
                if (!string.IsNullOrWhiteSpace(item.ProjectedGoodwillReason))
                {
                    sb.AppendLine($"- {item.ActionName}: {item.ProjectedGoodwillReason}");
                }
                else if (item.Eligibility.RemainingSeconds > 0)
                {
                    float remainingDays = item.Eligibility.RemainingSeconds / 1000f;
                    sb.AppendLine($"- {item.ActionName}: {item.Eligibility.Message} (Remaining: {remainingDays:F1} days)");
                }
                else
                {
                    sb.AppendLine($"- {item.ActionName}: {item.Eligibility.Message}");
                }
            }
            sb.AppendLine();
        }

        private static void AppendGoodwillPeacePolicyHints(StringBuilder sb, Faction faction)
        {
            if (sb == null || faction == null)
            {
                return;
            }

            int goodwill = faction.PlayerGoodwill;
            if (goodwill > 0)
            {
                return;
            }

            const int peaceTalkOnlyMin = -50;
            const int makePeaceReenabledMin = -20;
            const string peaceTalkQuest = "OpportunitySite_PeaceTalks";

            sb.AppendLine(PromptTextConstants.GoodwillPeacePolicyHeader);
            if (goodwill < peaceTalkOnlyMin)
            {
                AppendVeryLowGoodwillPeacePolicy(sb, goodwill, peaceTalkOnlyMin);
            }
            else if (goodwill < makePeaceReenabledMin)
            {
                AppendPeaceTalkOnlyPolicy(sb, goodwill, peaceTalkOnlyMin, makePeaceReenabledMin, peaceTalkQuest);
            }
            else
            {
                AppendMakePeaceReenabledPolicy(sb, goodwill, peaceTalkQuest);
            }
            sb.AppendLine();
        }

        private static void AppendVeryLowGoodwillPeacePolicy(StringBuilder sb, int goodwill, int peaceTalkOnlyMin)
        {
            sb.AppendLine(string.Format(PromptTextConstants.GoodwillPeacePolicyVeryLowLine1, goodwill));
            sb.AppendLine(string.Format(PromptTextConstants.GoodwillPeacePolicyVeryLowLine2, peaceTalkOnlyMin));
        }

        private static void AppendPeaceTalkOnlyPolicy(
            StringBuilder sb,
            int goodwill,
            int peaceTalkOnlyMin,
            int makePeaceReenabledMin,
            string peaceTalkQuest)
        {
            sb.AppendLine(string.Format(PromptTextConstants.GoodwillPeacePolicyTalkOnlyLine1, goodwill));
            sb.AppendLine(string.Format(PromptTextConstants.GoodwillPeacePolicyTalkOnlyLine2, peaceTalkQuest));
            sb.AppendLine(string.Format(
                PromptTextConstants.GoodwillPeacePolicyTalkOnlyLine3,
                peaceTalkOnlyMin,
                makePeaceReenabledMin - 1));
        }

        private static void AppendMakePeaceReenabledPolicy(StringBuilder sb, int goodwill, string peaceTalkQuest)
        {
            sb.AppendLine(string.Format(PromptTextConstants.GoodwillPeacePolicyReenabledLine1, goodwill));
            sb.AppendLine(string.Format(PromptTextConstants.GoodwillPeacePolicyReenabledLine2, peaceTalkQuest));
        }

        private static bool ShouldHideActionFromPromptByProjectedGoodwill(Faction faction, string actionName)
        {
            return !string.IsNullOrWhiteSpace(GetProjectedGoodwillBlockReason(faction, actionName));
        }

        private static string GetProjectedGoodwillBlockReason(Faction faction, string actionName)
        {
            if (faction == null || string.IsNullOrWhiteSpace(actionName))
            {
                return string.Empty;
            }

            DialogueGoodwillCost.DialogueActionType? actionType = actionName switch
            {
                "request_caravan" => DialogueGoodwillCost.DialogueActionType.RequestCaravan,
                "request_aid" => DialogueGoodwillCost.DialogueActionType.RequestMilitaryAid,
                "create_quest" => DialogueGoodwillCost.DialogueActionType.CreateQuest,
                _ => null
            };

            if (!actionType.HasValue)
            {
                return string.Empty;
            }

            int fixedCost = DialogueGoodwillCost.GetBaseValue(actionType.Value);
            int projectedGoodwill = faction.PlayerGoodwill + fixedCost;
            if (projectedGoodwill >= 0)
            {
                return string.Empty;
            }

            return $"Blocked because fixed goodwill cost {fixedCost} would reduce goodwill from {faction.PlayerGoodwill} to {projectedGoodwill}, below 0.";
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
                SignificantEventType.QuestIssued => "📜",
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
                SignificantEventType.QuestIssued => "发布任务",
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



