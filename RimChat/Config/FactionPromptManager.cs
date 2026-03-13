using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using RimChat.Core;

namespace RimChat.Config
{
    /// <summary>/// factionPromptmanager
 /// 负责从外部fileload和管理各faction的Promptconfiguration
 ///</summary>
    public class FactionPromptManager
    {
        #region 单例模式

        private static FactionPromptManager _instance;
        public static FactionPromptManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new FactionPromptManager();
                }
                return _instance;
            }
        }

        #endregion

        #region 常量

        /// <summary>/// Promptconfigurationfile名
 ///</summary>
        public const string ConfigFileName = "FactionPrompts_Custom.json";

        /// <summary>/// 默认Prompt资源path
 ///</summary>
        public const string DefaultPromptsResourcePath = "RimChat/DefaultFactionPrompts";

        /// <summary>/// 默认Promptconfigurationfile名 (在Mod目录中)
 ///</summary>
        public const string DefaultConfigFileName = "FactionPrompts_Default.json";

        #endregion

        #region 字段

        /// <summary>/// configuration集合
 ///</summary>
        private FactionPromptConfigCollection _configCollection;

        /// <summary>/// whether已initialize
 ///</summary>
        private bool _initialized;

        /// <summary>/// configurationfile完整path
 ///</summary>
        private string _configFilePath;

        private HashSet<string> _defaultFactionDefNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, FactionPromptConfig> _defaultConfigLookup =
            new Dictionary<string, FactionPromptConfig>(StringComparer.OrdinalIgnoreCase);
        #endregion

        #region 属性

        /// <summary>/// get所有configuration
 ///</summary>
        public List<FactionPromptConfig> AllConfigs
        {
            get
            {
                if (!_initialized) Initialize();
                return _configCollection?.Configs ?? new List<FactionPromptConfig>();
            }
        }

        /// <summary>/// configurationfilepath
 ///</summary>
        public string ConfigFilePath
        {
            get
            {
                if (string.IsNullOrEmpty(_configFilePath))
                {
                    _configFilePath = GetCustomConfigFilePathInternal();
                }
                return _configFilePath;
            }
        }

        #endregion

        #region 初始化

        /// <summary>/// initializemanager
 ///</summary>
        public void Initialize()
        {
            if (_initialized) return;

            try
            {
                BuildDefaultConfigCatalog();

                // Load或创建configuration
                LoadConfigs();

                // 确保所有faction都有configuration
                EnsureAllFactionsHaveConfigs();

                _initialized = true;
                Log.Message($"[RimChat] FactionPromptManager initialized with {_configCollection.Configs.Count} faction prompts");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Failed to initialize FactionPromptManager: {ex}");
                // 创建空configuration集合作为后备
                _configCollection = new FactionPromptConfigCollection();
            }
        }

        #endregion

        #region 配置加载与保存

        /// <summary>/// loadconfiguration
 ///</summary>
        private void LoadConfigs()
        {
            string sourcePath = ConfigFilePath;
            if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
            {
                try
                {
                    string json = File.ReadAllText(sourcePath);
                    _configCollection = FactionPromptJsonUtility.FromJson(json);
                    Log.Message($"[RimChat] Loaded faction prompts from {sourcePath}");
                    
                    // 如果configurationfileempty, 从默认configurationload
                    if (_configCollection == null || _configCollection.Configs.Count == 0)
                    {
                        Log.Warning($"[RimChat] Config file exists but contains no configs, loading defaults");
                        LoadDefaultConfigs();
                        SaveConfigs(); // Save默认configuration到file
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimChat] Failed to load prompts from file: {ex}. Using defaults.");
                    LoadDefaultConfigs();
                }
            }
            else
            {
                Log.Message($"[RimChat] Prompt config file not found, loading defaults");
                LoadDefaultConfigs();
                SaveConfigs(); // Save默认configuration到file
            }

            if (_configCollection == null)
            {
                _configCollection = new FactionPromptConfigCollection();
            }
        }

        /// <summary>/// saveconfiguration
 ///</summary>
        public void SaveConfigs()
        {
            try
            {
                if (_configCollection == null) return;

                // 确保目录presence
                string directory = Path.GetDirectoryName(ConfigFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = FactionPromptJsonUtility.ToJson(_configCollection, true);
                File.WriteAllText(ConfigFilePath, json);
                Log.Message($"[RimChat] Saved faction prompts to {ConfigFilePath}");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Failed to save prompts: {ex}");
            }
        }

        /// <summary>/// load默认configuration (从 FactionPrompts_Default.json file)
 ///</summary>
        private void LoadDefaultConfigs()
        {
            _configCollection = new FactionPromptConfigCollection();
            foreach (var defName in _defaultFactionDefNames.OrderBy(name => name))
            {
                if (_defaultConfigLookup.TryGetValue(defName, out FactionPromptConfig config))
                {
                    _configCollection.Configs.Add(config.Clone());
                }
            }

            if (_configCollection.Configs.Count == 0)
            {
                Log.Warning("[RimChat] Default faction prompt catalog is empty. Using hardcoded fallback.");
                LoadHardcodedDefaultConfigs();
                BuildDefaultConfigCatalog();
            }
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

        /// <summary>/// get默认configurationfilepath (Mod目录下的Prompt/Defaultfolder)
 ///</summary>
        private string GetDefaultConfigFilePath()
        {
            // 尝试从当前Mod的pathget
            try
            {
                var mod = LoadedModManager.GetMod<RimChatMod>();
                if (mod?.Content != null)
                {
                    string defaultDir = Path.Combine(mod.Content.RootDir, PromptFolderName, DefaultSubFolderName);
                    string path = Path.Combine(defaultDir, DefaultConfigFileName);
                    Log.Message($"[RimChat] Default config path from mod: {path}");
                    return path;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to get mod path: {ex.Message}");
            }

            // 后备1: 使用程序集所在目录的上级目录 (通常在 1.6/Assemblies 中)
            try
            {
                string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string assemblyDir = Path.GetDirectoryName(assemblyPath);
                // 尝试从 Assemblies 目录向上找到 Mod 根目录
                string modDir = Directory.GetParent(assemblyDir)?.Parent?.FullName;
                if (!string.IsNullOrEmpty(modDir))
                {
                    string defaultDir = Path.Combine(modDir, PromptFolderName, DefaultSubFolderName);
                    string path = Path.Combine(defaultDir, DefaultConfigFileName);
                    Log.Message($"[RimChat] Default config path from assembly parent: {path}");
                    return path;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to get assembly path: {ex.Message}");
            }

            // 后备2: 使用已知path
            string fallbackPath = Path.Combine("E:\\SteamLibrary\\steamapps\\common\\RimWorld\\Mods\\RimChat", PromptFolderName, DefaultSubFolderName, DefaultConfigFileName);
            Log.Message($"[RimChat] Default config path fallback: {fallbackPath}");
            return fallbackPath;
        }

        /// <summary>/// get自定义configurationfilepath (Mod目录下的Prompt/Customfolder)
 ///</summary>
        public string GetCustomConfigFilePath()
        {
            return ConfigFilePath;
        }

        private string GetCustomConfigFilePathInternal()
        {
            // 尝试从当前Mod的pathget
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
                    return Path.Combine(customDir, ConfigFileName);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to get custom config path: {ex.Message}");
            }

            // 后备: 使用userconfiguration目录
            return Path.Combine(RimChatMod.Instance?.GetSettingsFolderPath() ?? "", ConfigFileName);
        }

        /// <summary>/// load硬编码默认configuration (后备方案)
 ///</summary>
        private void LoadHardcodedDefaultConfigs()
        {
            _configCollection = new FactionPromptConfigCollection();

            // 创建所有faction的默认configuration
            foreach (var factionDef in GetSupportedFactionDefs())
            {
                var config = CreateDefaultConfig(factionDef);
                _configCollection.Configs.Add(config);
            }
        }

        /// <summary>/// 确保所有faction都有configuration
 ///</summary>
        private void EnsureAllFactionsHaveConfigs()
        {
            bool addedNew = false;

            foreach (var defName in _defaultFactionDefNames)
            {
                if (_configCollection.GetConfig(defName) != null)
                {
                    continue;
                }

                if (_defaultConfigLookup.TryGetValue(defName, out FactionPromptConfig defaultConfig))
                {
                    _configCollection.Configs.Add(defaultConfig.Clone());
                    addedNew = true;
                }
            }

            if (addedNew)
            {
                SaveConfigs();
            }
        }

        #endregion

        #region 默认配置创建

        /// <summary>/// get支持的factionDef列表
 ///</summary>
        private List<FactionDef> GetSupportedFactionDefs()
        {
            var supportedDefs = new List<FactionDef>();

            // 主要faction
            AddFactionDefIfExists(supportedDefs, "OutlanderCivil");
            AddFactionDefIfExists(supportedDefs, "OutlanderRough");
            AddFactionDefIfExists(supportedDefs, "TribeCivil");
            AddFactionDefIfExists(supportedDefs, "TribeRough");
            AddFactionDefIfExists(supportedDefs, "TribeSavage");
            AddFactionDefIfExists(supportedDefs, "Pirate");
            AddFactionDefIfExists(supportedDefs, "Mechanoid");
            AddFactionDefIfExists(supportedDefs, "Insect");
            AddFactionDefIfExists(supportedDefs, "HoraxCult");
            AddFactionDefIfExists(supportedDefs, "Entities");

            return supportedDefs;
        }

        /// <summary>/// 如果presence则添加factionDef
 ///</summary>
        private void AddFactionDefIfExists(List<FactionDef> list, string defName)
        {
            var def = DefDatabase<FactionDef>.GetNamedSilentFail(defName);
            if (def != null)
            {
                list.Add(def);
            }
        }

        /// <summary>/// 创建默认configuration
 ///</summary>
        private FactionPromptConfig CreateDefaultConfig(FactionDef factionDef)
        {
            var config = new FactionPromptConfig(factionDef.defName, factionDef.label);

            // 根据faction类型settings默认 Prompt template
            SetupDefaultTemplateFields(config, factionDef.defName);

            return config;
        }

        /// <summary>/// 为factionsettings默认template字段
 /// 注意: 默认configuration应从 FactionPrompts_Default.json file读取
 /// 此method仅在file读取失败时作为后备使用, 创建最小化configuration
 ///</summary>
        private void SetupDefaultTemplateFields(FactionPromptConfig config, string factionDefName)
        {
            // 定义标准字段
            const string coreStyleName = "核心风格";
            const string vocabName = "用词特征";
            const string toneName = "语气特征";
            const string sentenceName = "句式特征";
            const string taboosName = "表达禁忌";

            // 创建最小化默认configuration (提示user需要从fileload完整configuration)
            config.GetOrCreateField(coreStyleName, $"请从 {DefaultConfigFileName} 文件加载 {factionDefName} 的默认配置，或手动编辑此模板。", "描述派系的核心对话风格");
            config.GetOrCreateField(vocabName, "请配置用词特征。", "描述用词习惯和特征");
            config.GetOrCreateField(toneName, "请配置语气特征。", "描述语气和情感特征");
            config.GetOrCreateField(sentenceName, "请配置句式特征。", "描述句式结构特征");
            config.GetOrCreateField(taboosName, "请配置表达禁忌。", "描述表达禁忌和限制");
        }

        #endregion

        #region 公共方法

        /// <summary>/// getfactionPromptconfiguration
 ///</summary>
        public FactionPromptConfig GetConfig(string factionDefName)
        {
            if (!_initialized) Initialize();
            return _configCollection?.GetConfig(factionDefName);
        }

        /// <summary>/// getfactionPromptcontents
 ///</summary>
        public string GetPrompt(string factionDefName)
        {
            var config = GetConfig(factionDefName);
            return config?.GetEffectivePrompt() ?? "";
        }

        /// <summary>/// 更新configuration
 ///</summary>
        public void UpdateConfig(FactionPromptConfig config)
        {
            if (!_initialized) Initialize();
            if (config == null) return;

            _configCollection.SetConfig(config);
            SaveConfigs();
        }

        public bool TryAddTemplateForFaction(string factionDefName, string displayName, out string status)
        {
            if (!_initialized) Initialize();

            status = "invalid";
            string normalized = factionDefName?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            if (_configCollection.GetConfig(normalized) != null)
            {
                status = "existing";
                return false;
            }

            FactionPromptConfig config;
            if (_defaultConfigLookup.TryGetValue(normalized, out FactionPromptConfig defaultConfig))
            {
                config = defaultConfig.Clone();
            }
            else
            {
                FactionDef factionDef = DefDatabase<FactionDef>.GetNamedSilentFail(normalized);
                string resolvedDisplayName = !string.IsNullOrWhiteSpace(displayName)
                    ? displayName
                    : factionDef?.label ?? normalized;
                config = new FactionPromptConfig(normalized, resolvedDisplayName);
                SetupDefaultTemplateFields(config, normalized);
            }

            _configCollection.SetConfig(config);
            SaveConfigs();
            status = "created";
            return true;
        }

        public bool TryRemoveTemplate(string factionDefName, out string reason)
        {
            if (!_initialized) Initialize();

            reason = "invalid";
            string normalized = factionDefName?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            if (IsDefaultTemplate(normalized))
            {
                reason = "default_protected";
                return false;
            }

            FactionPromptConfig existing = _configCollection.GetConfig(normalized);
            if (existing == null)
            {
                reason = "not_found";
                return false;
            }

            _configCollection.Configs.Remove(existing);
            SaveConfigs();
            reason = "removed";
            return true;
        }

        public bool IsDefaultTemplate(string factionDefName)
        {
            if (!_initialized) Initialize();
            if (string.IsNullOrWhiteSpace(factionDefName))
            {
                return false;
            }

            return _defaultFactionDefNames.Contains(factionDefName.Trim());
        }

        public bool IsFactionMissing(string factionDefName)
        {
            if (string.IsNullOrWhiteSpace(factionDefName))
            {
                return true;
            }

            return DefDatabase<FactionDef>.GetNamedSilentFail(factionDefName.Trim()) == null;
        }

        /// <summary>/// 重置configuration为默认
 ///</summary>
        public void ResetConfig(string factionDefName)
        {
            var config = GetConfig(factionDefName);
            if (config != null)
            {
                config.ResetToDefault();
                SaveConfigs();
            }
        }

        /// <summary>/// 重置所有configuration为默认
 ///</summary>
        public void ResetAllConfigs()
        {
            if (!_initialized) Initialize();

            foreach (var config in _configCollection.Configs)
            {
                config.ResetToDefault();
            }
            SaveConfigs();
        }

        /// <summary>/// apply自定义Prompt
 ///</summary>
        public void ApplyCustomPrompt(string factionDefName, string customPrompt)
        {
            var config = GetConfig(factionDefName);
            if (config != null)
            {
                config.ApplyCustomPrompt(customPrompt);
                SaveConfigs();
            }
        }

        /// <summary>/// 导出configuration到file
 ///</summary>
        public bool ExportConfigs(string filePath)
        {
            try
            {
                if (_configCollection == null) return false;

                string json = ExportConfigsToJson(prettyPrint: true);
                File.WriteAllText(filePath, json);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Failed to export configs: {ex}");
                return false;
            }
        }

        /// <summary>/// 从file导入configuration
 ///</summary>
        public bool ImportConfigs(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;

                string json = File.ReadAllText(filePath);
                return ImportConfigsFromJson(json);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Failed to import configs: {ex}");
                return false;
            }
        }

        public string ExportConfigsToJson(bool prettyPrint = false)
        {
            if (!_initialized) Initialize();
            if (_configCollection == null)
            {
                return string.Empty;
            }

            return FactionPromptJsonUtility.ToJson(_configCollection, prettyPrint);
        }

        public bool ImportConfigsFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            var imported = FactionPromptJsonUtility.FromJson(json);
            if (imported?.Configs == null || imported.Configs.Count == 0)
            {
                return false;
            }

            _configCollection = imported;
            EnsureAllFactionsHaveConfigs();
            SaveConfigs();
            return true;
        }

        private void BuildDefaultConfigCatalog()
        {
            _defaultFactionDefNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _defaultConfigLookup = new Dictionary<string, FactionPromptConfig>(StringComparer.OrdinalIgnoreCase);

            string defaultConfigPath = GetDefaultConfigFilePath();
            Log.Message($"[RimChat] Looking for default config at: {defaultConfigPath}");
            if (!string.IsNullOrWhiteSpace(defaultConfigPath) && File.Exists(defaultConfigPath))
            {
                try
                {
                    string json = File.ReadAllText(defaultConfigPath);
                    FactionPromptConfigCollection collection = FactionPromptJsonUtility.FromJson(json);
                    if (TryPopulateDefaultCatalog(collection))
                    {
                        Log.Message($"[RimChat] Loaded default faction prompt catalog ({_defaultFactionDefNames.Count})");
                        return;
                    }

                    Log.Warning("[RimChat] Default prompt file parsed but contains no valid configs.");
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimChat] Failed to parse default prompt file: {ex}");
                }
            }

            BuildHardcodedDefaultCatalog();
        }

        private bool TryPopulateDefaultCatalog(FactionPromptConfigCollection collection)
        {
            if (collection?.Configs == null || collection.Configs.Count == 0)
            {
                return false;
            }

            foreach (FactionPromptConfig config in collection.Configs)
            {
                string defName = config?.FactionDefName?.Trim();
                if (string.IsNullOrWhiteSpace(defName))
                {
                    continue;
                }

                _defaultFactionDefNames.Add(defName);
                _defaultConfigLookup[defName] = config.Clone();
            }

            return _defaultFactionDefNames.Count > 0;
        }

        private void BuildHardcodedDefaultCatalog()
        {
            Log.Message("[RimChat] Using hardcoded fallback for faction prompt default catalog.");

            foreach (FactionDef factionDef in GetSupportedFactionDefs())
            {
                if (factionDef == null || string.IsNullOrWhiteSpace(factionDef.defName))
                {
                    continue;
                }

                FactionPromptConfig config = CreateDefaultConfig(factionDef);
                _defaultFactionDefNames.Add(factionDef.defName);
                _defaultConfigLookup[factionDef.defName] = config;
            }
        }

        #endregion
    }

    /// <summary>/// 简单的JSON工具类 - 使用手动序列化
 ///</summary>
    public static class FactionPromptJsonUtility
    {
        public static string ToJson(FactionPromptConfigCollection collection, bool prettyPrint = false)
        {
            if (collection == null || collection.Configs == null)
                return "{\"Configs\":[]}";

            var sb = new StringBuilder();
            if (prettyPrint)
            {
                sb.AppendLine("{");
                sb.AppendLine("  \"Configs\": [");
            }
            else
            {
                sb.Append("{\"Configs\":[");
            }

            for (int i = 0; i < collection.Configs.Count; i++)
            {
                var config = collection.Configs[i];
                if (prettyPrint) sb.Append("    ");
                sb.Append("{");

                sb.Append($"\"FactionDefName\":\"{EscapeJson(config.FactionDefName)}\",");
                sb.Append($"\"DisplayName\":\"{EscapeJson(config.DisplayName)}\",");
                
                // 序列化template字段
                sb.Append("\"TemplateFields\":[");
                for (int j = 0; j < config.TemplateFields.Count; j++)
                {
                    var field = config.TemplateFields[j];
                    if (prettyPrint) sb.Append("\n      ");
                    sb.Append("{");
                    sb.Append($"\"FieldName\":\"{EscapeJson(field.FieldName)}\",");
                    sb.Append($"\"FieldValue\":\"{EscapeJson(field.FieldValue)}\",");
                    sb.Append($"\"FieldDescription\":\"{EscapeJson(field.FieldDescription)}\",");
                    sb.Append($"\"IsEnabled\":{field.IsEnabled.ToString().ToLower()}");
                    sb.Append("}");
                    if (j < config.TemplateFields.Count - 1)
                    {
                        sb.Append(",");
                    }
                }
                if (prettyPrint) sb.Append("\n    ");
                sb.Append("],");

                sb.Append($"\"UseCustomPrompt\":{config.UseCustomPrompt.ToString().ToLower()},");
                sb.Append($"\"CustomPrompt\":\"{EscapeJson(config.CustomPrompt)}\",");
                sb.Append($"\"LastModifiedTicks\":{config.LastModifiedTicks}");

                sb.Append("}");
                if (i < collection.Configs.Count - 1)
                {
                    sb.Append(",");
                }
                if (prettyPrint) sb.AppendLine();
            }

            if (prettyPrint)
            {
                sb.AppendLine("  ]");
                sb.Append("}");
            }
            else
            {
                sb.Append("]}");
            }

            return sb.ToString();
        }

        public static FactionPromptConfigCollection FromJson(string json)
        {
            var collection = new FactionPromptConfigCollection();

            if (string.IsNullOrEmpty(json))
                return collection;

            try
            {
                // 解析 Configs 数组
                int configsStart = json.IndexOf("\"Configs\":");
                if (configsStart < 0) return collection;

                int arrayStart = json.IndexOf("[", configsStart);
                if (arrayStart < 0) return collection;

                int arrayEnd = json.LastIndexOf("]");
                if (arrayEnd < 0) return collection;

                string arrayContent = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);

                // 分割对象
                var objects = SplitJsonObjects(arrayContent);

                foreach (var objStr in objects)
                {
                    var config = ParseConfig(objStr);
                    if (config != null)
                    {
                        collection.Configs.Add(config);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to parse JSON: {ex.Message}");
            }

            return collection;
        }

        private static List<string> SplitJsonObjects(string arrayContent)
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
                    // 跳过字符串contents
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

        private static FactionPromptConfig ParseConfig(string json)
        {
            var config = new FactionPromptConfig();

            try
            {
                config.FactionDefName = ExtractString(json, "FactionDefName");
                config.DisplayName = ExtractString(json, "DisplayName");
                config.CustomPrompt = ExtractString(json, "CustomPrompt");

                ParseTemplateFields(json, config);

                string useCustomStr = ExtractValue(json, "UseCustomPrompt");
                if (bool.TryParse(useCustomStr, out bool useCustom))
                {
                    config.UseCustomPrompt = useCustom;
                }

                string ticksStr = ExtractValue(json, "LastModifiedTicks");
                if (long.TryParse(ticksStr, out long ticks))
                {
                    config.LastModifiedTicks = ticks;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to parse config: {ex.Message}");
                return null;
            }

            return config;
        }

        private static void ParseTemplateFields(string json, FactionPromptConfig config)
        {
            int fieldsStart = json.IndexOf("\"TemplateFields\":");
            if (fieldsStart < 0) return;

            int arrayStart = json.IndexOf("[", fieldsStart);
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
            var fieldObjects = SplitJsonObjects(arrayContent);

            foreach (var fieldStr in fieldObjects)
            {
                var field = ParseTemplateField(fieldStr);
                if (field != null)
                {
                    config.TemplateFields.Add(field);
                }
            }
        }

        private static PromptTemplateField ParseTemplateField(string json)
        {
            var field = new PromptTemplateField();

            try
            {
                field.FieldName = ExtractString(json, "FieldName");
                field.FieldValue = ExtractString(json, "FieldValue");
                field.FieldDescription = ExtractString(json, "FieldDescription");

                string enabledStr = ExtractValue(json, "IsEnabled");
                if (bool.TryParse(enabledStr, out bool enabled))
                {
                    field.IsEnabled = enabled;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to parse template field: {ex.Message}");
                return null;
            }

            return field;
        }

        private static string ExtractString(string json, string key)
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

        private static string ExtractValue(string json, string key)
        {
            string pattern = $"\"{key}\":";
            int index = json.IndexOf(pattern);
            if (index < 0) return "";

            int start = index + pattern.Length;
            int end = json.IndexOfAny(new[] { ',', '}' }, start);
            if (end < 0) end = json.Length;

            return json.Substring(start, end - start).Trim();
        }

        private static string EscapeJson(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }
}
