using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using RimDiplomacy.Core;

namespace RimDiplomacy.Config
{
    /// <summary>
    /// 派系Prompt管理器
    /// 负责从外部文件加载和管理各派系的Prompt配置
    /// </summary>
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

        /// <summary>
        /// Prompt配置文件名
        /// </summary>
        public const string ConfigFileName = "FactionPrompts.json";

        /// <summary>
        /// 默认Prompt资源路径
        /// </summary>
        public const string DefaultPromptsResourcePath = "RimDiplomacy/DefaultFactionPrompts";

        /// <summary>
        /// 默认Prompt配置文件名（在Mod目录中）
        /// </summary>
        public const string DefaultConfigFileName = "FactionPrompts_Default.json";

        #endregion

        #region 字段

        /// <summary>
        /// 配置集合
        /// </summary>
        private FactionPromptConfigCollection _configCollection;

        /// <summary>
        /// 是否已初始化
        /// </summary>
        private bool _initialized;

        /// <summary>
        /// 配置文件完整路径
        /// </summary>
        private string _configFilePath;

        #endregion

        #region 属性

        /// <summary>
        /// 获取所有配置
        /// </summary>
        public List<FactionPromptConfig> AllConfigs
        {
            get
            {
                if (!_initialized) Initialize();
                return _configCollection?.Configs ?? new List<FactionPromptConfig>();
            }
        }

        /// <summary>
        /// 配置文件路径
        /// </summary>
        public string ConfigFilePath
        {
            get
            {
                if (string.IsNullOrEmpty(_configFilePath))
                {
                    _configFilePath = Path.Combine(RimDiplomacyMod.Instance?.GetSettingsFolderPath() ?? "", ConfigFileName);
                }
                return _configFilePath;
            }
        }

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化管理器
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;

            try
            {
                // 加载或创建配置
                LoadConfigs();

                // 确保所有派系都有配置
                EnsureAllFactionsHaveConfigs();

                _initialized = true;
                Log.Message($"[RimDiplomacy] FactionPromptManager initialized with {_configCollection.Configs.Count} faction prompts");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimDiplomacy] Failed to initialize FactionPromptManager: {ex}");
                // 创建空配置集合作为后备
                _configCollection = new FactionPromptConfigCollection();
            }
        }

        #endregion

        #region 配置加载与保存

        /// <summary>
        /// 加载配置
        /// </summary>
        private void LoadConfigs()
        {
            if (File.Exists(ConfigFilePath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    _configCollection = FactionPromptJsonUtility.FromJson(json);
                    Log.Message($"[RimDiplomacy] Loaded faction prompts from {ConfigFilePath}");
                    
                    // 如果配置文件为空，从默认配置加载
                    if (_configCollection == null || _configCollection.Configs.Count == 0)
                    {
                        Log.Warning($"[RimDiplomacy] Config file exists but contains no configs, loading defaults");
                        LoadDefaultConfigs();
                        SaveConfigs(); // 保存默认配置到文件
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimDiplomacy] Failed to load prompts from file: {ex}. Using defaults.");
                    LoadDefaultConfigs();
                }
            }
            else
            {
                Log.Message($"[RimDiplomacy] Prompt config file not found, loading defaults");
                LoadDefaultConfigs();
                SaveConfigs(); // 保存默认配置到文件
            }

            if (_configCollection == null)
            {
                _configCollection = new FactionPromptConfigCollection();
            }
        }

        /// <summary>
        /// 保存配置
        /// </summary>
        public void SaveConfigs()
        {
            try
            {
                if (_configCollection == null) return;

                // 确保目录存在
                string directory = Path.GetDirectoryName(ConfigFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = FactionPromptJsonUtility.ToJson(_configCollection, true);
                File.WriteAllText(ConfigFilePath, json);
                Log.Message($"[RimDiplomacy] Saved faction prompts to {ConfigFilePath}");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimDiplomacy] Failed to save prompts: {ex}");
            }
        }

        /// <summary>
        /// 加载默认配置（从 FactionPrompts_Default.json 文件）
        /// </summary>
        private void LoadDefaultConfigs()
        {
            _configCollection = new FactionPromptConfigCollection();

            // 尝试从 Mod 目录读取默认配置文件
            string defaultConfigPath = GetDefaultConfigFilePath();
            Log.Message($"[RimDiplomacy] Looking for default config at: {defaultConfigPath}");
            
            if (File.Exists(defaultConfigPath))
            {
                try
                {
                    string json = File.ReadAllText(defaultConfigPath);
                    Log.Message($"[RimDiplomacy] Read {json.Length} characters from default config file");
                    _configCollection = FactionPromptJsonUtility.FromJson(json);
                    Log.Message($"[RimDiplomacy] Loaded {_configCollection.Configs.Count} faction prompts from {defaultConfigPath}");
                    if (_configCollection.Configs.Count > 0)
                    {
                        return;
                    }
                    Log.Warning($"[RimDiplomacy] Config file parsed but contains 0 configs");
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimDiplomacy] Failed to load default prompts from file: {ex}. Using fallback.");
                }
            }
            else
            {
                Log.Warning($"[RimDiplomacy] Default config file not found at {defaultConfigPath}");
            }

            // 如果文件读取失败，使用硬编码后备
            Log.Message($"[RimDiplomacy] Using hardcoded fallback for faction prompts");
            LoadHardcodedDefaultConfigs();
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
        /// 获取默认配置文件路径（Mod目录下的Prompt/Default文件夹）
        /// </summary>
        private string GetDefaultConfigFilePath()
        {
            // 尝试从当前Mod的路径获取
            try
            {
                var mod = LoadedModManager.GetMod<RimDiplomacyMod>();
                if (mod?.Content != null)
                {
                    string defaultDir = Path.Combine(mod.Content.RootDir, PromptFolderName, DefaultSubFolderName);
                    string path = Path.Combine(defaultDir, DefaultConfigFileName);
                    Log.Message($"[RimDiplomacy] Default config path from mod: {path}");
                    return path;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimDiplomacy] Failed to get mod path: {ex.Message}");
            }

            // 后备1：使用程序集所在目录的上级目录（通常在 1.6/Assemblies 中）
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
                    Log.Message($"[RimDiplomacy] Default config path from assembly parent: {path}");
                    return path;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimDiplomacy] Failed to get assembly path: {ex.Message}");
            }

            // 后备2：使用已知路径
            string fallbackPath = Path.Combine("E:\\SteamLibrary\\steamapps\\common\\RimWorld\\Mods\\RimDiplomacy", PromptFolderName, DefaultSubFolderName, DefaultConfigFileName);
            Log.Message($"[RimDiplomacy] Default config path fallback: {fallbackPath}");
            return fallbackPath;
        }

        /// <summary>
        /// 获取自定义配置文件路径（Mod目录下的Prompt/Custom文件夹）
        /// </summary>
        public string GetCustomConfigFilePath()
        {
            // 尝试从当前Mod的路径获取
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
                    return Path.Combine(customDir, ConfigFileName);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimDiplomacy] Failed to get custom config path: {ex.Message}");
            }

            // 后备：使用用户配置目录
            return ConfigFilePath;
        }

        /// <summary>
        /// 加载硬编码默认配置（后备方案）
        /// </summary>
        private void LoadHardcodedDefaultConfigs()
        {
            _configCollection = new FactionPromptConfigCollection();

            // 创建所有派系的默认配置
            foreach (var factionDef in GetSupportedFactionDefs())
            {
                var config = CreateDefaultConfig(factionDef);
                _configCollection.Configs.Add(config);
            }
        }

        /// <summary>
        /// 确保所有派系都有配置
        /// </summary>
        private void EnsureAllFactionsHaveConfigs()
        {
            var supportedFactions = GetSupportedFactionDefs();
            bool addedNew = false;

            foreach (var factionDef in supportedFactions)
            {
                if (_configCollection.GetConfig(factionDef.defName) == null)
                {
                    var config = CreateDefaultConfig(factionDef);
                    _configCollection.Configs.Add(config);
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

        /// <summary>
        /// 获取支持的派系Def列表
        /// </summary>
        private List<FactionDef> GetSupportedFactionDefs()
        {
            var supportedDefs = new List<FactionDef>();

            // 主要派系
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

        /// <summary>
        /// 如果存在则添加派系Def
        /// </summary>
        private void AddFactionDefIfExists(List<FactionDef> list, string defName)
        {
            var def = DefDatabase<FactionDef>.GetNamedSilentFail(defName);
            if (def != null)
            {
                list.Add(def);
            }
        }

        /// <summary>
        /// 创建默认配置
        /// </summary>
        private FactionPromptConfig CreateDefaultConfig(FactionDef factionDef)
        {
            var config = new FactionPromptConfig(factionDef.defName, factionDef.label);

            // 根据派系类型设置默认 Prompt 模板
            SetupDefaultTemplateFields(config, factionDef.defName);

            return config;
        }

        /// <summary>
        /// 为派系设置默认模板字段
        /// 注意：默认配置应从 FactionPrompts_Default.json 文件读取
        /// 此方法仅在文件读取失败时作为后备使用，创建最小化配置
        /// </summary>
        private void SetupDefaultTemplateFields(FactionPromptConfig config, string factionDefName)
        {
            // 定义标准字段
            const string coreStyleName = "核心风格";
            const string vocabName = "用词特征";
            const string toneName = "语气特征";
            const string sentenceName = "句式特征";
            const string taboosName = "表达禁忌";

            // 创建最小化默认配置（提示用户需要从文件加载完整配置）
            config.GetOrCreateField(coreStyleName, $"请从 {DefaultConfigFileName} 文件加载 {factionDefName} 的默认配置，或手动编辑此模板。", "描述派系的核心对话风格");
            config.GetOrCreateField(vocabName, "请配置用词特征。", "描述用词习惯和特征");
            config.GetOrCreateField(toneName, "请配置语气特征。", "描述语气和情感特征");
            config.GetOrCreateField(sentenceName, "请配置句式特征。", "描述句式结构特征");
            config.GetOrCreateField(taboosName, "请配置表达禁忌。", "描述表达禁忌和限制");
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 获取派系Prompt配置
        /// </summary>
        public FactionPromptConfig GetConfig(string factionDefName)
        {
            if (!_initialized) Initialize();
            return _configCollection?.GetConfig(factionDefName);
        }

        /// <summary>
        /// 获取派系Prompt内容
        /// </summary>
        public string GetPrompt(string factionDefName)
        {
            var config = GetConfig(factionDefName);
            return config?.GetEffectivePrompt() ?? "";
        }

        /// <summary>
        /// 更新配置
        /// </summary>
        public void UpdateConfig(FactionPromptConfig config)
        {
            if (!_initialized) Initialize();
            if (config == null) return;

            _configCollection.SetConfig(config);
            SaveConfigs();
        }

        /// <summary>
        /// 重置配置为默认
        /// </summary>
        public void ResetConfig(string factionDefName)
        {
            var config = GetConfig(factionDefName);
            if (config != null)
            {
                config.ResetToDefault();
                SaveConfigs();
            }
        }

        /// <summary>
        /// 重置所有配置为默认
        /// </summary>
        public void ResetAllConfigs()
        {
            if (!_initialized) Initialize();

            foreach (var config in _configCollection.Configs)
            {
                config.ResetToDefault();
            }
            SaveConfigs();
        }

        /// <summary>
        /// 应用自定义Prompt
        /// </summary>
        public void ApplyCustomPrompt(string factionDefName, string customPrompt)
        {
            var config = GetConfig(factionDefName);
            if (config != null)
            {
                config.ApplyCustomPrompt(customPrompt);
                SaveConfigs();
            }
        }

        /// <summary>
        /// 导出配置到文件
        /// </summary>
        public bool ExportConfigs(string filePath)
        {
            try
            {
                if (_configCollection == null) return false;

                string json = FactionPromptJsonUtility.ToJson(_configCollection, true);
                File.WriteAllText(filePath, json);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimDiplomacy] Failed to export configs: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 从文件导入配置
        /// </summary>
        public bool ImportConfigs(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;

                string json = File.ReadAllText(filePath);
                var imported = FactionPromptJsonUtility.FromJson(json);

                if (imported?.Configs != null && imported.Configs.Count > 0)
                {
                    _configCollection = imported;
                    SaveConfigs();
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimDiplomacy] Failed to import configs: {ex}");
                return false;
            }
        }

        #endregion
    }

    /// <summary>
    /// 简单的JSON工具类 - 使用手动序列化
    /// </summary>
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
                
                // 序列化模板字段
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
                Log.Warning($"[RimDiplomacy] Failed to parse JSON: {ex.Message}");
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
                    // 跳过字符串内容
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

                // 解析模板字段（支持两种格式）
                if (json.Contains("\"TemplateFields\":"))
                {
                    // 新格式：包含 TemplateFields 数组
                    ParseTemplateFields(json, config);
                }
                else
                {
                    // 旧格式/默认文件格式：扁平字段
                    ParseLegacyFields(json, config);
                }

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
                Log.Warning($"[RimDiplomacy] Failed to parse config: {ex.Message}");
                return null;
            }

            return config;
        }

        /// <summary>
        /// 解析旧格式/默认文件格式的扁平字段
        /// </summary>
        private static void ParseLegacyFields(string json, FactionPromptConfig config)
        {
            // 核心风格
            string coreStyle = ExtractString(json, "CoreStyle");
            if (!string.IsNullOrEmpty(coreStyle))
            {
                config.GetOrCreateField("核心风格", coreStyle, "描述派系的核心对话风格");
            }

            // 用词特征
            string vocabFeatures = ExtractString(json, "VocabularyFeatures");
            if (!string.IsNullOrEmpty(vocabFeatures))
            {
                config.GetOrCreateField("用词特征", vocabFeatures, "描述用词习惯和特征");
            }

            // 语气特征
            string toneFeatures = ExtractString(json, "ToneFeatures");
            if (!string.IsNullOrEmpty(toneFeatures))
            {
                config.GetOrCreateField("语气特征", toneFeatures, "描述语气和情感特征");
            }

            // 句式特征
            string sentenceFeatures = ExtractString(json, "SentenceFeatures");
            if (!string.IsNullOrEmpty(sentenceFeatures))
            {
                config.GetOrCreateField("句式特征", sentenceFeatures, "描述句式结构特征");
            }

            // 表达禁忌
            string taboos = ExtractString(json, "Taboos");
            if (!string.IsNullOrEmpty(taboos))
            {
                config.GetOrCreateField("表达禁忌", taboos, "描述表达禁忌和限制");
            }
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
                Log.Warning($"[RimDiplomacy] Failed to parse template field: {ex.Message}");
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
