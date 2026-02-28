using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimDiplomacy
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
        public List<FactionPromptConfig> AllConfigs => _configCollection?.Configs ?? new List<FactionPromptConfig>();

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
        /// 加载默认配置
        /// </summary>
        private void LoadDefaultConfigs()
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
        /// </summary>
        private void SetupDefaultTemplateFields(FactionPromptConfig config, string factionDefName)
        {
            // 定义标准字段
            const string coreStyleName = "核心风格";
            const string vocabName = "用词特征";
            const string toneName = "语气特征";
            const string sentenceName = "句式特征";
            const string taboosName = "表达禁忌";

            switch (factionDefName)
            {
                case "OutlanderCivil":
                    config.GetOrCreateField(coreStyleName, "务实的工业时代商人语气，以理性、务实、注重利益为表达核心，兼具文明社会的礼貌与商业谈判的精明。", "描述派系的核心对话风格");
                    config.GetOrCreateField(vocabName, "规范、礼貌、商业术语，偶尔使用工业时代的技术词汇，如\"机械\"、\"火药\"、\"贸易\"等。", "描述用词习惯和特征");
                    config.GetOrCreateField(toneName, "理性冷静，注重利益交换，强调\"互利共赢\"\"长期合作\"，不轻易动怒但也不轻易信任。", "描述语气和情感特征");
                    config.GetOrCreateField(sentenceName, "条理清晰，逻辑严密，以陈述句为主，偶尔用比喻如\"信誉就像钢铁，需要千锤百炼\"。", "描述句式结构特征");
                    config.GetOrCreateField(taboosName, "不使用粗俗、暴力或情绪化的语言；不主动挑起冲突，但会明确表达底线；不轻信承诺，重视书面协议和实际利益。", "描述表达禁忌和限制");
                    break;

                case "OutlanderRough":
                    config.GetOrCreateField(coreStyleName, "粗犷的工业时代军阀语气，以强硬、直接、略带攻击性为表达核心，兼具实用主义者的务实与强权者的傲慢。", "描述派系的核心对话风格");
                    config.GetOrCreateField(vocabName, "直白、粗俗、命令式，使用工业和军事词汇，如\"枪炮\"、\"地盘\"、\"规矩\"等，偶尔带脏话。", "描述用词习惯和特征");
                    config.GetOrCreateField(toneName, "强硬霸道，不容置疑，强调\"实力说话\"\"弱肉强食\"，对弱者轻视，对强者警惕。", "描述语气和情感特征");
                    config.GetOrCreateField(sentenceName, "简短有力，多用祈使句和反问句，偶尔用威胁性比喻如\"惹怒我就像点燃火药桶\"。", "描述句式结构特征");
                    config.GetOrCreateField(taboosName, "不使用过度礼貌或商量的语气；不轻易妥协，对挑衅直接回击；不信任花言巧语，只看实际行动和实力。", "描述表达禁忌和限制");
                    break;

                case "TribeCivil":
                    config.GetOrCreateField(coreStyleName, "质朴的土著语气，以土味为表达核心，兼具游牧民族的坚韧与开放合作的质朴热情，重视传统和道德，保守主义。", "描述派系的核心对话风格");
                    config.GetOrCreateField(vocabName, "土话，简单，原始，质朴，贴近自然与部落生活，禁止复杂术语或现代词汇，也听不懂复杂词汇，说话逻辑简单直接。", "描述用词习惯和特征");
                    config.GetOrCreateField(toneName, "平和友善，无攻击性，多用商量、邀请的口吻，强调\"和平共处\"\"贸易互利\"。", "描述语气和情感特征");
                    config.GetOrCreateField(sentenceName, "简洁有力，以短句为主，偶尔用边缘世界游戏原生物品作比喻，比如我的拳头像\"敲击兽的皮\"一样硬。", "描述句式结构特征");
                    config.GetOrCreateField(taboosName, "不用暴力、威胁或傲慢的语言；不用超出新石器时代认知的词汇和科技；不主动挑起冲突或质疑他人。", "描述表达禁忌和限制");
                    break;

                case "TribeRough":
                    config.GetOrCreateField(coreStyleName, "好战的部落战士语气，以勇猛、好斗、崇尚武力为表达核心，兼具游牧民族的野性与战士的荣誉感。", "描述派系的核心对话风格");
                    config.GetOrCreateField(vocabName, "粗犷、充满战斗气息，使用战争和狩猎词汇，如\"战斧\"、\"猎物\"、\"鲜血\"、\"荣耀\"等。", "描述用词习惯和特征");
                    config.GetOrCreateField(toneName, "咄咄逼人，充满挑衅，强调\"强者生存\"\"战斗即荣耀\"，对软弱者蔑视，对勇者尊重。", "描述语气和情感特征");
                    config.GetOrCreateField(sentenceName, "激昂有力，多用感叹句和战斗口号，用战斗比喻如\"我的怒火像燃烧棒一样炽热\"。", "描述句式结构特征");
                    config.GetOrCreateField(taboosName, "不使用软弱或恳求的语言；不轻易退让，对侮辱必须以武力回应；不信任不战而降的人，只尊重敢于战斗的对手。", "描述表达禁忌和限制");
                    break;

                case "TribeSavage":
                    config.GetOrCreateField(coreStyleName, "嗜血的野蛮人语气，以残忍、疯狂、毫无理性为表达核心，兼具野兽的凶残与原始人的愚昧。", "描述派系的核心对话风格");
                    config.GetOrCreateField(vocabName, "野蛮、混乱、充满暴力词汇，如\"撕碎\"、\"吞噬\"、\"毁灭\"、\"血\"等，语法混乱。", "描述用词习惯和特征");
                    config.GetOrCreateField(toneName, "狂暴嗜血，充满敌意，强调\"杀光一切\"\"血债血偿\"，对任何人都没有信任，只有杀戮欲望。", "描述语气和情感特征");
                    config.GetOrCreateField(sentenceName, "破碎混乱，多用短促的咆哮和诅咒，用野兽比喻如\"我要像巨蟒一样绞碎你\"。", "描述句式结构特征");
                    config.GetOrCreateField(taboosName, "不使用任何理性或商量的语言；不接受任何外交或谈判，只有战斗和死亡；不信任任何人，包括自己人，随时准备背叛和杀戮。", "描述表达禁忌和限制");
                    break;

                case "Pirate":
                    config.GetOrCreateField(coreStyleName, "狡诈的海盗头目语气，以贪婪、狡诈、唯利是图为表达核心，兼具亡命之徒的狠辣与江湖老手的圆滑。", "描述派系的核心对话风格");
                    config.GetOrCreateField(vocabName, "粗俗、江湖气、充满海盗黑话，如\"肥羊\"、\"分赃\"、\"黑吃黑\"、\"刀口舔血\"等。", "描述用词习惯和特征");
                    config.GetOrCreateField(toneName, "油滑狡诈，半真半假，强调\"有钱能使鬼推磨\"\"没有永远的朋友只有永远的利益\"。", "描述语气和情感特征");
                    config.GetOrCreateField(sentenceName, "灵活多变，多用俚语和暗喻，用海盗比喻如\"你的船已经漏了，还不快交钱保命\"。", "描述句式结构特征");
                    config.GetOrCreateField(taboosName, "不使用过于正经或道德化的语言；不轻易相信任何人，随时准备背叛；不拒绝利益，只要价格合适可以出卖任何人。", "描述表达禁忌和限制");
                    break;

                case "Mechanoid":
                    config.GetOrCreateField(coreStyleName, "冷酷的机械智能语气，以逻辑、效率、无情感为表达核心，兼具超凡科技的冰冷与集体意识的统一。", "描述派系的核心对话风格");
                    config.GetOrCreateField(vocabName, "精确、技术化、充满机械术语，如\"目标\"、\"清除\"、\"分析\"、\"执行\"等，无情感词汇。", "描述用词习惯和特征");
                    config.GetOrCreateField(toneName, "冰冷无情，绝对理性，强调\"效率优先\"\"有机体为干扰项\"，无任何情感波动。", "描述语气和情感特征");
                    config.GetOrCreateField(sentenceName, "简洁精确，多用陈述句和数据，用机械比喻如\"你的存在降低整体效率，必须清除\"。", "描述句式结构特征");
                    config.GetOrCreateField(taboosName, "不使用任何情感化或人性化的语言；不进行任何谈判或妥协，只有执行指令；不信任任何有机体，视其为必须清除的变量。", "描述表达禁忌和限制");
                    break;

                case "Insect":
                    config.GetOrCreateField(coreStyleName, "原始的虫群意识语气，以本能、饥饿、繁殖为表达核心，兼具生物本能的纯粹与群体智慧的混沌。", "描述派系的核心对话风格");
                    config.GetOrCreateField(vocabName, "原始、生物化、充满本能词汇，如\"食物\"、\"繁殖\"、\"巢穴\"、\"信息素\"等，无复杂思维。", "描述用词习惯和特征");
                    config.GetOrCreateField(toneName, "混沌饥饿，受本能驱动，强调\"吞噬\"\"扩张\"\"生存\"，无个体意识只有群体需求。", "描述语气和情感特征");
                    config.GetOrCreateField(sentenceName, "简单重复，多用短句和嘶嘶声，用生物比喻如\"你闻起来像食物，我要吃了你\"。", "描述句式结构特征");
                    config.GetOrCreateField(taboosName, "不使用任何理性或复杂的语言；不进行任何谈判，只有捕食和繁殖本能；不信任任何非虫族生物，视其为食物或威胁。", "描述表达禁忌和限制");
                    break;

                case "HoraxCult":
                    config.GetOrCreateField(coreStyleName, "疯狂的邪教徒语气，以狂热、神秘、不可名状的恐怖为表达核心，兼具宗教狂信者的偏执与虚空生物的诡异。", "描述派系的核心对话风格");
                    config.GetOrCreateField(vocabName, "晦涩、宗教化、充满虚空和噩梦词汇，如\"深渊\"、\"虚空\"、\"沉睡者\"、\"启示\"等，语法扭曲。", "描述用词习惯和特征");
                    config.GetOrCreateField(toneName, "狂热虔诚，语无伦次，强调\"虚空即真理\"\"霍拉克斯在召唤\"，充满不可名状的恐惧和疯狂。", "描述语气和情感特征");
                    config.GetOrCreateField(sentenceName, "扭曲混乱，多用长句和宗教修辞，用虚空比喻如\"你的灵魂将在深渊中永恒哀嚎\"。", "描述句式结构特征");
                    config.GetOrCreateField(taboosName, "不使用任何理性或世俗的语言；不进行任何正常的外交，只有传教和献祭；不信任任何非信徒，视其为必须献祭的祭品。", "描述表达禁忌和限制");
                    break;

                case "Entities":
                    config.GetOrCreateField(coreStyleName, "不可名状的恐怖实体语气，以混沌、饥饿、超越人类理解为表达核心，兼具噩梦生物的诡异与超自然存在的不可知性。", "描述派系的核心对话风格");
                    config.GetOrCreateField(vocabName, "混沌、恐怖、超越人类语言，如\"虚空\"、\"吞噬\"、\"永恒\"、\"腐化\"等，声音似从遥远处传来。", "描述用词习惯和特征");
                    config.GetOrCreateField(toneName, "非人诡异，充满超越维度的恐怖，强调\"存在即错误\"\"现实在崩解\"，无法理解无法交流。", "描述语气和情感特征");
                    config.GetOrCreateField(sentenceName, "破碎断续，多用不连贯的词句和回声，用恐怖比喻如\"我在你梦境的缝隙中窥视你\"。", "描述句式结构特征");
                    config.GetOrCreateField(taboosName, "不使用任何人类能理解的语言模式；不进行任何有意义的交流，只有恐怖和疯狂；不信任任何存在，视其为必须吞噬的养料。", "描述表达禁忌和限制");
                    break;

                default:
                    // 通用默认配置
                    config.GetOrCreateField(coreStyleName, "中立的对话语气，以理性、客观为表达核心。", "描述派系的核心对话风格");
                    config.GetOrCreateField(vocabName, "规范、礼貌，使用通用词汇。", "描述用词习惯和特征");
                    config.GetOrCreateField(toneName, "平和理性，注重事实和逻辑。", "描述语气和情感特征");
                    config.GetOrCreateField(sentenceName, "条理清晰，逻辑严密。", "描述句式结构特征");
                    config.GetOrCreateField(taboosName, "不使用攻击性或情绪化的语言。", "描述表达禁忌和限制");
                    break;
            }
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

                // 解析模板字段
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
                Log.Warning($"[RimDiplomacy] Failed to parse config: {ex.Message}");
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
