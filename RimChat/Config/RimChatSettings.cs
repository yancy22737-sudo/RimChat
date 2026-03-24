using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using UnityEngine;
using UnityEngine.Networking;
using Verse;
using Verse.Sound;
using RimChat.UI;
using RimChat.AI;
using RimChat.Persistence;
using RimChat.Prompting;

namespace RimChat.Config
{
    /// <summary>/// 闁劕鐡ф潏鎾冲毉闁喎瀹冲Ο鈥崇础
 ///</summary>
    public enum TypewriterSpeedMode
    {
        Fast = 0,
        Standard = 1,
        Immersive = 2
    }

    public enum DialogueStyleMode
    {
        NaturalConcise = 0,
        Balanced = 1,
        Immersive = 2
    }

    public enum ExpectedActionDenyLogLevel
    {
        Info = 0,
        Warning = 1
    }

    public partial class RimChatSettings : ModSettings
    {
        // Provider Selection
        public bool UseCloudProviders = true;

        // Cloud API Configs
        public List<ApiConfig> CloudConfigs = new List<ApiConfig>();

        // Local Model Config
        public LocalModelConfig LocalConfig = new LocalModelConfig();

        // Diplomacy image API config (standalone from chat API)
        public DiplomacyImageApiConfig DiplomacyImageApi = new DiplomacyImageApiConfig();
        public List<DiplomacyImagePromptTemplate> DiplomacyImagePromptTemplates = new List<DiplomacyImagePromptTemplate>();
        public string SendImageCaptionStylePrompt = PromptTextConstants.SendImageCaptionStylePromptDefault;
        public string SendImageCaptionFallbackTemplate = PromptTextConstants.SendImageCaptionFallbackTemplateDefault;

        // Prompt output language settings
        public bool PromptLanguageFollowSystem = true;
        public string PromptLanguageOverride = "";
        public List<UserDefinedPromptVariableConfig> UserDefinedPromptVariables = new List<UserDefinedPromptVariableConfig>();
        public List<FactionPromptVariableRuleConfig> UserDefinedPromptVariableFactionRules = new List<FactionPromptVariableRuleConfig>();
        public List<PawnPromptVariableRuleConfig> UserDefinedPromptVariablePawnRules = new List<PawnPromptVariableRuleConfig>();
        public List<FactionScopedPromptVariableOverrideConfig> FactionScopedPromptVariableOverrides = new List<FactionScopedPromptVariableOverrideConfig>();



        // AI Behavior Limits
        public int MaxGoodwillAdjustmentPerCall = 15;
        public int MaxDailyGoodwillAdjustment = 30;
        public int GoodwillCooldownTicks = 0;
        public int MaxGiftSilverAmount = 1000;
        public int MaxGiftGoodwillGain = 10;
        public int GiftCooldownTicks = 60000;
        public int MinGoodwillForAid = 40;
        public int AidCooldownTicks = 120000;
        public int MaxGoodwillForWarDeclaration = -50;
        public int WarCooldownTicks = 60000;
        public int MaxPeaceCost = 5000;
        public int PeaceGoodwillReset = -20;
        public int PeaceCooldownTicks = 60000;
        public int CaravanCooldownTicks = 90000;
        public int AidDelayBaseTicks = 90000;
        public int CaravanDelayBaseTicks = 135000;
        public int RaidCooldownTicks = 180000;
        public float DialogueActionGoodwillCostMultiplier = 0.5f;
        public bool EnableAIGoodwillAdjustment = true;
        public bool EnableAIGiftSending = true;
        public bool EnableAIWarDeclaration = true;
        public bool EnableAIPeaceMaking = true;
        public bool EnableAITradeCaravan = true;
        public bool EnableAIRaidRequest = true;
        public bool EnableAIAidRequest = true;
        public bool EnableAIItemAirdrop = true;
        public bool EnablePrisonerRansom = true;
        public string RansomPaymentModeDefault = "silver";
        public int RansomReleaseTimeoutTicks = 30000;
        public float RansomValueDropMajorThreshold = 0.30f;
        public float RansomValueDropSevereThreshold = 0.60f;
        public int RansomLowGoodwillDiscountThreshold = 80;
        public float RansomLowGoodwillDiscountFactor = 0.8f;
        public int RansomPenaltyMajor = -15;
        public int RansomPenaltySevere = -25;
        public int RansomPenaltyTimeout = -35;

        public int ItemAirdropMinBudgetSilver = 200;
        public int ItemAirdropMaxBudgetSilver = 5000;
        public int ItemAirdropDefaultAIBudgetSilver = 800;
        public float ItemAirdropRansomBudgetPercent = 0.01f;
        public int ItemAirdropMaxStacksPerDrop = 8;
        public int ItemAirdropMaxTotalItemsPerDrop = 200;
        public string ItemAirdropBlacklistDefNamesCsv = "VanometricPowerCell,PersonaCore,ArchotechArm,ArchotechLeg";
        public int ItemAirdropSelectionCandidateLimit = 30;
        public int ItemAirdropSecondPassTimeoutSeconds = 25;
        public string ItemAirdropBlockedCategoriesCsv = "";
        public bool EnableAirdropAliasExpansion = true;
        public int ItemAirdropAliasExpansionMaxCount = 8;
        public int ItemAirdropAliasExpansionTimeoutSeconds = 4;
        public bool EnableAirdropSameFamilyRelaxedRetry = true;

        // Quest Settings
        public int MinQuestCooldownDays = 7;
        public int MaxQuestCooldownDays = 12;

        // Raid Granular Settings
        public bool EnableRaidStrategy_ImmediateAttack = true;
        public bool EnableRaidStrategy_ImmediateAttackSmart = true;
        public bool EnableRaidStrategy_StageThenAttack = true;
        public bool EnableRaidStrategy_ImmediateAttackSappers = true;
        public bool EnableRaidStrategy_Siege = true;

        public bool EnableRaidArrival_EdgeWalkIn = true;
        public bool EnableRaidArrival_EdgeDrop = true;
        public bool EnableRaidArrival_EdgeWalkInGroups = true;
        public bool EnableRaidArrival_RandomDrop = false;
        public bool EnableRaidArrival_CenterDrop = false;
        public float RaidPointsMultiplier = 1f;
        public float MinRaidPoints = 35f;
        public List<RaidPointsFactionOverride> RaidPointsFactionOverrides = new List<RaidPointsFactionOverride>();

        public bool EnableAPICallLogging = true;
        public int MaxAPICallsPerHour = 0;



        // Debug Settings
        public bool EnableDebugLogging = false;
        public bool LogAIRequests = true;
        public bool LogAIResponses = true;
        public bool LogInternals = false;
        public bool LogFullMessages = false;

        // UI Settings  
        public TypewriterSpeedMode TypewriterSpeedMode = TypewriterSpeedMode.Immersive;
        public DialogueStyleMode DialogueStyleMode = DialogueStyleMode.NaturalConcise;
        public ExpectedActionDenyLogLevel ExpectedActionDenyLogLevel = ExpectedActionDenyLogLevel.Info;
        public int ProactiveMessageHardLimit = 0;

        // Comms Console Settings
        public bool ReplaceCommsConsole = false;
        [Obsolete("Use ThoughtChainByChannel instead")]
        public bool EnableThoughtChainNode = true;
        public List<PromptChannelToggleConfig> ThoughtChainByChannel = new List<PromptChannelToggleConfig>();

        // Presence Settings
        public bool EnableFactionPresenceStatus = true;
        public float PresenceCacheHours = 2f;
        public float PresenceForcedOfflineHours = 24f;
        public bool PresenceNightBiasEnabled = true;
        public int PresenceNightStartHour = 22;
        public int PresenceNightEndHour = 6;
        public float PresenceNightOfflineBias = 0.65f;
        public bool PresenceUseAdvancedProfiles = true;
        public int PresenceOnlineStart_Default = 7;
        public int PresenceOnlineDuration_Default = 12;
        public int PresenceOnlineStart_Neolithic = 8;
        public int PresenceOnlineDuration_Neolithic = 8;
        public int PresenceOnlineStart_Medieval = 8;
        public int PresenceOnlineDuration_Medieval = 10;
        public int PresenceOnlineStart_Industrial = 7;
        public int PresenceOnlineDuration_Industrial = 14;
        public int PresenceOnlineStart_Spacer = 6;
        public int PresenceOnlineDuration_Spacer = 18;
        public int PresenceOnlineStart_Ultra = 4;
        public int PresenceOnlineDuration_Ultra = 20;
        public int PresenceOnlineStart_Archotech = 4;
        public int PresenceOnlineDuration_Archotech = 20;

        // Social Circle Settings
        public bool EnableSocialCircle = true;
        public ScheduledNewsFrequencyLevel ScheduledNewsFrequencyLevel = ScheduledNewsFrequencyLevel.High;
        public int SocialPostIntervalMinDays = 5;
        public int SocialPostIntervalMaxDays = 7;
        public bool EnablePlayerInfluenceNews = true;
        public bool EnableAISimulationNews = true;
        public bool EnableSocialCircleAutoActions = false;

        // RPG Dialogue Settings
        public bool EnableRPGDialogue = true;
        public bool EnableRPGAPI = true;
        public bool EnableRPGNonVerbalPawnSpeech = true;

        // Connection Test State
        private string connectionTestStatus = "";
        private bool isTestingConnection = false;
        private bool showPromptLanguageSettings;
        private const int DialogueTokenLowThreshold = 1200;
        private const int DialogueTokenMediumThreshold = 3000;

        // Model Cache
        private static readonly Dictionary<string, List<string>> ModelCache = new();

        // Prompt Settings -  FactionPromptManager
        private Vector2 factionListScrollPosition = Vector2.zero;
        private Vector2 promptEditorScrollPosition = Vector2.zero;
        private bool showHiddenFactions = false;
        private string selectedFactionDefName = null;
        private string editingCustomPrompt = "";
        private bool editingUseCustomPrompt = false;

        // Global Prompt Settings
        public string GlobalSystemPrompt = "";
        public string GlobalDialoguePrompt = "";
        public string RPGRoleSetting = "";
        public string RPGDialogueStyle = "";
        public string RPGApiGuidelines = "";
        public string RPGFormatConstraint = "";
        public string RPGRoleSettingFallbackTemplate = "";
        public string RPGFormatConstraintHeader = "";
        public string RPGCompactFormatFallback = "";
        public string RPGActionReliabilityFallback = "";
        public string RPGActionReliabilityMarker = "";
        internal RpgApiActionPromptConfig RPGApiActionPromptConfig = RpgApiActionPromptConfig.CreateFallback();
        [Obsolete("Use RPGRoleSetting instead")]
        public string RPGSystemPrompt = "";
        [Obsolete("Use RPGDialogueStyle instead")]
        public string RPGDialoguePrompt = "";
        [Obsolete("Use RPGApiGuidelines instead")]
        public string RPGApiFormatPrompt = "";

        public int MaxSystemPromptLength = 2000;
        public int MaxDialoguePromptLength = 2000;
        public int MaxFactionPromptLength = 4000;
        public bool EnableApiPromptEditing = false;

        // Prompt Scenario Tag Settings
        public string DiplomacyManualSceneTagsCsv = "scene:social";
        public string RpgManualSceneTagsCsv = "scene:daily";
        public bool PromptPreviewUseProactiveContext = false;
        public string PromptPreviewSceneTagsCsv = "scene:social";
        public bool RpgPromptPreviewUseProactiveContext = false;
        public string RpgPromptPreviewSceneTagsCsv = "scene:daily";

        // Dialogue Context Compression Settings
        public bool EnableDialogueContextCompression = true;
        public int DialogueCompressionKeepRecentTurns = 10;
        public int DialogueCompressionFirstPassChunkSize = 10;
        public int DialogueCompressionSecondaryTriggerTurns = 20;
        public int DialogueCompressionSecondaryWindowMinRecency = 21;
        public int DialogueCompressionSecondaryWindowMaxRecency = 25;
        public int DialogueCompressionSecondaryTierStart = 21;
        public int DialogueCompressionTertiaryTierStart = 26;
        public int DialogueCompressionMaxMark = 3;
        public int DialogueCompressionMaxEventsPerSegment = 3;
        public int DialogueCompressionSnippetMaxChars = 28;
        public int DialogueCompressionMaxSummaryLines = 3;
        public int DialogueCompressionMaxSecondaryRounds = 3;

        // RPG Dynamic Injection Settings
        public bool RPGInjectSelfStatus = true;
        public bool RPGInjectInterlocutorStatus = true;
        public bool RPGInjectFactionBackground = true;

        [Obsolete("Use RPGInjectSelfStatus instead")]
        public bool RPGInjectPawnInfo = true;
        [Obsolete("Use RPGInjectFactionBackground instead")]
        public bool RPGInjectFactionInfo = true;

        // Prompt editing state
        private string editingSystemPrompt = "";

        public void ResolveRaidPointTuning(Faction faction, out float multiplier, out float minRaidPoints)
        {
            multiplier = RaidPointsFactionOverride.ClampMultiplier(RaidPointsMultiplier);
            minRaidPoints = RaidPointsFactionOverride.ClampMinPoints(MinRaidPoints);

            if (faction?.def == null || RaidPointsFactionOverrides == null || RaidPointsFactionOverrides.Count == 0)
            {
                return;
            }

            string factionDefName = faction.def.defName;
            RaidPointsFactionOverride entry = RaidPointsFactionOverrides.FirstOrDefault(o => o?.MatchesFactionDef(factionDefName) == true);
            if (entry == null)
            {
                return;
            }

            multiplier = RaidPointsFactionOverride.ClampMultiplier(entry.RaidPointsMultiplier);
            minRaidPoints = RaidPointsFactionOverride.ClampMinPoints(entry.MinRaidPoints);
        }
        private string editingDialoguePrompt = "";
        private Vector2 globalPromptScrollPosition = Vector2.zero;

        // Enhanced TextArea components
        private EnhancedTextArea systemPromptTextArea;
        private EnhancedTextArea dialoguePromptTextArea;
        private EnhancedTextArea factionPromptTextArea;

        // Tab Settings
        private int selectedTab = 0;
        private readonly string[] tabNames = { "RimChat_Tab_API", "RimChat_Tab_ModOptions", "RimChat_Tab_PromptWorkbench", "RimChat_Tab_ImageApi" };
        private const string ModVariablesSectionId = "mod_variables";
        private static readonly PromptWorkbenchSectionDefinition[] PromptWorkbenchSections =
        {
            new PromptWorkbenchSectionDefinition("system_rules", "System Rules", "系统规则"),
            new PromptWorkbenchSectionDefinition("character_persona", "Persona", "角色人设", "Character Persona", "人物设定", "人格"),
            new PromptWorkbenchSectionDefinition("memory_system", "Memory", "记忆", "Memory System", "记忆系统"),
            new PromptWorkbenchSectionDefinition("environment_perception", "Environment", "环境感知", "Environment Perception", "环境"),
            new PromptWorkbenchSectionDefinition("context", "Context", "上下文"),
            new PromptWorkbenchSectionDefinition("mod_variables", "Mod Variables", "模组变量", "Mod Vars"),
            new PromptWorkbenchSectionDefinition("action_rules", "Action Rules", "行为规则", "行动规则"),
            new PromptWorkbenchSectionDefinition("repetition_reinforcement", "Reinforcement", "强化规则", "Repetition Reinforcement", "重复强化", "强化"),
            new PromptWorkbenchSectionDefinition("output_specification", "Output Format", "输出格式", "Output Specification", "输出规范")
        };

        private sealed class PromptWorkbenchSectionDefinition
        {
            public readonly string Id;
            public readonly string EnglishName;
            public readonly string[] Aliases;

            public PromptWorkbenchSectionDefinition(string id, string englishName, params string[] aliases)
            {
                Id = id ?? string.Empty;
                EnglishName = englishName ?? "Entry";
                Aliases = aliases ?? Array.Empty<string>();
            }
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref UseCloudProviders, "UseCloudProviders", true);
            Scribe_Collections.Look(ref CloudConfigs, "CloudConfigs", LookMode.Deep);
            Scribe_Deep.Look(ref LocalConfig, "LocalConfig");
            Scribe_Deep.Look(ref DiplomacyImageApi, "DiplomacyImageApi");
            Scribe_Collections.Look(ref DiplomacyImagePromptTemplates, "DiplomacyImagePromptTemplates", LookMode.Deep);
            Scribe_Values.Look(ref SendImageCaptionStylePrompt, "SendImageCaptionStylePrompt", PromptTextConstants.SendImageCaptionStylePromptDefault);
            Scribe_Values.Look(ref SendImageCaptionFallbackTemplate, "SendImageCaptionFallbackTemplate", PromptTextConstants.SendImageCaptionFallbackTemplateDefault);
            Scribe_Values.Look(ref PromptLanguageFollowSystem, "PromptLanguageFollowSystem", true);
            Scribe_Values.Look(ref PromptLanguageOverride, "PromptLanguageOverride", "");
            Scribe_Collections.Look(ref UserDefinedPromptVariables, "UserDefinedPromptVariables", LookMode.Deep);
            Scribe_Collections.Look(ref UserDefinedPromptVariableFactionRules, "UserDefinedPromptVariableFactionRules", LookMode.Deep);
            Scribe_Collections.Look(ref UserDefinedPromptVariablePawnRules, "UserDefinedPromptVariablePawnRules", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Scribe_Collections.Look(ref FactionScopedPromptVariableOverrides, "FactionScopedPromptVariableOverrides", LookMode.Deep);
            }

            // Debug Settings
            Scribe_Values.Look(ref EnableDebugLogging, "EnableDebugLogging", false);
            Scribe_Values.Look(ref LogAIRequests, "LogAIRequests", true);
            Scribe_Values.Look(ref LogAIResponses, "LogAIResponses", true);
            Scribe_Values.Look(ref LogInternals, "LogInternals", false);
            Scribe_Values.Look(ref LogFullMessages, "LogFullMessages", false);

            // UI Settings
            Scribe_Values.Look(ref TypewriterSpeedMode, "TypewriterSpeedMode", TypewriterSpeedMode.Standard);
            Scribe_Values.Look(ref DialogueStyleMode, "DialogueStyleMode", DialogueStyleMode.NaturalConcise);
            Scribe_Values.Look(ref ExpectedActionDenyLogLevel, "ExpectedActionDenyLogLevel", ExpectedActionDenyLogLevel.Info);
            Scribe_Values.Look(ref ProactiveMessageHardLimit, "ProactiveMessageHardLimit", 0);

            // Comms Console Settings
            Scribe_Values.Look(ref ReplaceCommsConsole, "ReplaceCommsConsole", false);
            Scribe_Collections.Look(ref ThoughtChainByChannel, "ThoughtChainByChannel", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                bool legacyEnableThoughtChainNode = true;
                Scribe_Values.Look(ref legacyEnableThoughtChainNode, "EnableThoughtChainNode", true);
                MigrateLegacyThoughtChainToggleOnce(legacyEnableThoughtChainNode);
            }
            EnsureThoughtChainChannelMapReady();

            // RPG Dialogue Settings
            Scribe_Values.Look(ref EnableRPGDialogue, "EnableRPGDialogue", true);
            Scribe_Values.Look(ref EnableRPGAPI, "EnableRPGAPI", true);
            Scribe_Values.Look(ref EnableRPGNonVerbalPawnSpeech, "EnableRPGNonVerbalPawnSpeech", true);
            
            // Refined RPG Prompt Settings
            // RPG prompt text persistence is handled by Prompt/Custom/PawnDialoguePrompt_Custom.json only.
            
            // Refined RPG Dynamic Injection Settings
            Scribe_Values.Look(ref RPGInjectSelfStatus, "RPGInjectSelfStatus", true);
            Scribe_Values.Look(ref RPGInjectInterlocutorStatus, "RPGInjectInterlocutorStatus", true);
            Scribe_Values.Look(ref RPGInjectFactionBackground, "RPGInjectFactionBackground", true);
            ExposeData_RimTalkCompat();

            // Migration from old fields
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                bool oldRPGInjectPawnInfo = true;
                bool oldRPGInjectFactionInfo = true;

                Scribe_Values.Look(ref oldRPGInjectPawnInfo, "RPGInjectPawnInfo", true);
                Scribe_Values.Look(ref oldRPGInjectFactionInfo, "RPGInjectFactionInfo", true);

                bool hasRpgCustomPromptFile = RpgPromptCustomStore.CustomConfigExists();
                LoadRpgPromptTextsFromCustom();
            }

            // Global Prompt Settings
            Scribe_Values.Look(ref MaxSystemPromptLength, "MaxSystemPromptLength", 2000);
            Scribe_Values.Look(ref MaxDialoguePromptLength, "MaxDialoguePromptLength", 2000);
            Scribe_Values.Look(ref MaxFactionPromptLength, "MaxFactionPromptLength", 4000);
            Scribe_Values.Look(ref EnableApiPromptEditing, "EnableApiPromptEditing", false);
            Scribe_Values.Look(ref DiplomacyManualSceneTagsCsv, "DiplomacyManualSceneTagsCsv", "scene:social");
            Scribe_Values.Look(ref RpgManualSceneTagsCsv, "RpgManualSceneTagsCsv", "scene:daily");
            Scribe_Values.Look(ref PromptPreviewUseProactiveContext, "PromptPreviewUseProactiveContext", false);
            Scribe_Values.Look(ref PromptPreviewSceneTagsCsv, "PromptPreviewSceneTagsCsv", "scene:social");
            Scribe_Values.Look(ref RpgPromptPreviewUseProactiveContext, "RpgPromptPreviewUseProactiveContext", false);
            Scribe_Values.Look(ref RpgPromptPreviewSceneTagsCsv, "RpgPromptPreviewSceneTagsCsv", "scene:daily");
            Scribe_Values.Look(ref EnableDialogueContextCompression, "EnableDialogueContextCompression", true);
            Scribe_Values.Look(ref DialogueCompressionKeepRecentTurns, "DialogueCompressionKeepRecentTurns", 10);
            Scribe_Values.Look(ref DialogueCompressionFirstPassChunkSize, "DialogueCompressionFirstPassChunkSize", 10);
            Scribe_Values.Look(ref DialogueCompressionSecondaryTriggerTurns, "DialogueCompressionSecondaryTriggerTurns", 20);
            Scribe_Values.Look(ref DialogueCompressionSecondaryWindowMinRecency, "DialogueCompressionSecondaryWindowMinRecency", 21);
            Scribe_Values.Look(ref DialogueCompressionSecondaryWindowMaxRecency, "DialogueCompressionSecondaryWindowMaxRecency", 25);
            Scribe_Values.Look(ref DialogueCompressionSecondaryTierStart, "DialogueCompressionSecondaryTierStart", 21);
            Scribe_Values.Look(ref DialogueCompressionTertiaryTierStart, "DialogueCompressionTertiaryTierStart", 26);
            Scribe_Values.Look(ref DialogueCompressionMaxMark, "DialogueCompressionMaxMark", 3);
            Scribe_Values.Look(ref DialogueCompressionMaxEventsPerSegment, "DialogueCompressionMaxEventsPerSegment", 3);
            Scribe_Values.Look(ref DialogueCompressionSnippetMaxChars, "DialogueCompressionSnippetMaxChars", 28);
            Scribe_Values.Look(ref DialogueCompressionMaxSummaryLines, "DialogueCompressionMaxSummaryLines", 3);
            Scribe_Values.Look(ref DialogueCompressionMaxSecondaryRounds, "DialogueCompressionMaxSecondaryRounds", 3);

            DialogueCompressionKeepRecentTurns = Math.Max(6, DialogueCompressionKeepRecentTurns);
            DialogueCompressionSecondaryTierStart = Math.Max(DialogueCompressionKeepRecentTurns + 1, DialogueCompressionSecondaryTierStart);
            DialogueCompressionTertiaryTierStart = Math.Max(DialogueCompressionSecondaryTierStart + 1, DialogueCompressionTertiaryTierStart);
            DialogueCompressionMaxMark = 3;
            DialogueCompressionMaxEventsPerSegment = Math.Max(1, Math.Min(3, DialogueCompressionMaxEventsPerSegment));
            DialogueCompressionMaxSummaryLines = Math.Max(1, Math.Min(3, DialogueCompressionMaxSummaryLines));

            // AI Control Settings
            ExposeData_AI();

            if (CloudConfigs == null) CloudConfigs = new List<ApiConfig>();
            if (LocalConfig == null) LocalConfig = new LocalModelConfig();
            if (DiplomacyImageApi == null) DiplomacyImageApi = new DiplomacyImageApiConfig();
            if (DiplomacyImagePromptTemplates == null) DiplomacyImagePromptTemplates = new List<DiplomacyImagePromptTemplate>();
            if (UserDefinedPromptVariables == null) UserDefinedPromptVariables = new List<UserDefinedPromptVariableConfig>();
            if (UserDefinedPromptVariableFactionRules == null) UserDefinedPromptVariableFactionRules = new List<FactionPromptVariableRuleConfig>();
            if (UserDefinedPromptVariablePawnRules == null) UserDefinedPromptVariablePawnRules = new List<PawnPromptVariableRuleConfig>();
            if (FactionScopedPromptVariableOverrides == null) FactionScopedPromptVariableOverrides = new List<FactionScopedPromptVariableOverrideConfig>();
            if (SendImageCaptionStylePrompt == null) SendImageCaptionStylePrompt = PromptTextConstants.SendImageCaptionStylePromptDefault;
            if (SendImageCaptionFallbackTemplate == null) SendImageCaptionFallbackTemplate = PromptTextConstants.SendImageCaptionFallbackTemplateDefault;
            ProactiveMessageHardLimit = Math.Max(0, ProactiveMessageHardLimit);
            NormalizeCloudConfigUrls();
            EnsureDiplomacyImageDefaults();
            UserDefinedPromptVariableService.NormalizeSettingsCollections(this);

            base.ExposeData();
        }

        private void NormalizeCloudConfigUrls()
        {
            if (CloudConfigs == null)
            {
                return;
            }

            foreach (var config in CloudConfigs)
            {
                NormalizeCloudConfigUrl(config);
            }
        }

        private void NormalizeCloudConfigUrl(ApiConfig config)
        {
            if (config == null || config.Provider != AIProvider.DeepSeek)
            {
                return;
            }

            config.BaseUrl = ApiConfig.DeepSeekOfficialBaseUrl;
        }

        internal void EnsureRpgPromptTextsLoaded()
        {
            LoadRpgPromptTextsFromCustom();
        }

        private void LoadRpgPromptTextsFromCustom()
        {
            RpgPromptCustomConfig config = RpgPromptCustomStore.LoadOrDefault();
            RPGRoleSetting = config?.RoleSetting ?? PromptTextConstants.RpgRoleSettingDefault;
            RPGDialogueStyle = config?.DialogueStyle ?? PromptTextConstants.RpgDialogueStyleDefault;
            RPGFormatConstraint = config?.FormatConstraint ?? PromptTextConstants.RpgFormatConstraintDefault;
            RPGRoleSettingFallbackTemplate = config?.RoleSettingFallbackTemplate ?? RpgPromptDefaultsProvider.GetDefaults().RoleSettingFallbackTemplate;
            RPGFormatConstraintHeader = config?.FormatConstraintHeader ?? RpgPromptDefaultsProvider.GetDefaults().FormatConstraintHeader;
            RPGCompactFormatFallback = config?.CompactFormatFallback ?? RpgPromptDefaultsProvider.GetDefaults().CompactFormatFallback;
            RPGActionReliabilityFallback = config?.ActionReliabilityFallback ?? RpgPromptDefaultsProvider.GetDefaults().ActionReliabilityFallback;
            RPGActionReliabilityMarker = config?.ActionReliabilityMarker ?? RpgPromptDefaultsProvider.GetDefaults().ActionReliabilityMarker;
            RPGApiActionPromptConfig = config?.ApiActionPrompt?.Clone() ?? RpgPromptDefaultsProvider.GetDefaults().ApiActionPrompt?.Clone() ?? RpgApiActionPromptConfig.CreateFallback();
            RimTalkPersonaCopyTemplate = config?.RimTalkPersonaCopyTemplate ?? DefaultRimTalkPersonaCopyTemplate;
            RimTalkAutoPushSessionSummary = config?.RimTalkAutoPushSessionSummary ?? false;
            RimTalkAutoInjectCompatPreset = config?.RimTalkAutoInjectCompatPreset ?? false;
            RimTalkSummaryHistoryLimit = config?.RimTalkSummaryHistoryLimit ?? 10;
            AutoPopulatePromptSectionCatalogModVariables();
            if (!string.IsNullOrEmpty(RPGFormatConstraint) && RPGFormatConstraint.Contains("JoyFilled"))
            {
                RPGFormatConstraint = RPGFormatConstraint.Replace("JoyFilled", "RimChat_BriefJoy");
            }

            ClampRimTalkCompatSettings();
        }

        private void SaveRpgPromptTextsToCustom()
        {
            RpgPromptCustomConfig existing = RpgPromptCustomStore.LoadOrDefault();
            var config = new RpgPromptCustomConfig
            {
                RoleSetting = RPGRoleSetting ?? string.Empty,
                DialogueStyle = RPGDialogueStyle ?? string.Empty,
                FormatConstraint = RPGFormatConstraint ?? string.Empty,
                RoleSettingFallbackTemplate = RPGRoleSettingFallbackTemplate ?? string.Empty,
                FormatConstraintHeader = RPGFormatConstraintHeader ?? string.Empty,
                CompactFormatFallback = RPGCompactFormatFallback ?? string.Empty,
                ActionReliabilityFallback = RPGActionReliabilityFallback ?? string.Empty,
                ActionReliabilityMarker = RPGActionReliabilityMarker ?? string.Empty,
                RpgRoleSettingTemplate = existing?.RpgRoleSettingTemplate ?? string.Empty,
                RpgCompactFormatConstraintTemplate = existing?.RpgCompactFormatConstraintTemplate ?? string.Empty,
                RpgActionReliabilityRuleTemplate = existing?.RpgActionReliabilityRuleTemplate ?? string.Empty,
                DecisionPolicyTemplate = existing?.DecisionPolicyTemplate ?? string.Empty,
                TurnObjectiveTemplate = existing?.TurnObjectiveTemplate ?? string.Empty,
                OpeningObjectiveTemplate = existing?.OpeningObjectiveTemplate ?? string.Empty,
                TopicShiftRuleTemplate = existing?.TopicShiftRuleTemplate ?? string.Empty,
                RelationshipProfileTemplate = existing?.RelationshipProfileTemplate ?? string.Empty,
                KinshipBoundaryRuleTemplate = existing?.KinshipBoundaryRuleTemplate ?? string.Empty,
                PersonaBootstrapSystemPrompt = existing?.PersonaBootstrapSystemPrompt ?? string.Empty,
                PersonaBootstrapUserPromptTemplate = existing?.PersonaBootstrapUserPromptTemplate ?? string.Empty,
                PersonaBootstrapOutputTemplate = existing?.PersonaBootstrapOutputTemplate ?? string.Empty,
                PersonaBootstrapExample = existing?.PersonaBootstrapExample ?? string.Empty,
                ApiActionPrompt = RPGApiActionPromptConfig?.Clone() ?? RpgApiActionPromptConfig.CreateFallback(),
                RimTalkSummaryHistoryLimit = RimTalkSummaryHistoryLimit,
                RimTalkPersonaCopyTemplate = RimTalkPersonaCopyTemplate ?? DefaultRimTalkPersonaCopyTemplate,
                RimTalkAutoPushSessionSummary = RimTalkAutoPushSessionSummary,
                RimTalkAutoInjectCompatPreset = RimTalkAutoInjectCompatPreset
            };
            RpgPromptCustomStore.Save(config);
            ApplyRpgPromptEditorStateToUnifiedCatalog(persistToFiles: true);
        }

        private void ApplyRpgPromptEditorStateToUnifiedCatalog(bool persistToFiles)
        {
            PromptUnifiedCatalog catalog = GetPromptUnifiedCatalogClone();
            ApplyRpgPromptEditorSectionToUnifiedCatalog(catalog, RimTalkPromptEntryChannelCatalog.RpgDialogue);
            ApplyRpgPromptEditorSectionToUnifiedCatalog(catalog, RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue);
            catalog.SetNode(RimTalkPromptEntryChannelCatalog.RpgDialogue, "rpg_role_setting_fallback", RPGRoleSettingFallbackTemplate ?? string.Empty);
            catalog.SetNode(RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue, "rpg_role_setting_fallback", RPGRoleSettingFallbackTemplate ?? string.Empty);
            SetPromptUnifiedCatalog(catalog, persistToFiles: persistToFiles);
        }

        private void ApplyRpgPromptEditorSectionToUnifiedCatalog(PromptUnifiedCatalog catalog, string channel)
        {
            if (catalog == null || string.IsNullOrWhiteSpace(channel))
            {
                return;
            }

            catalog.SetSection(channel, "character_persona", RPGRoleSetting ?? string.Empty);
            catalog.SetSection(channel, "style_guidance", RPGDialogueStyle ?? string.Empty);
            catalog.SetSection(channel, "output_specification", RpgOutputSpecificationReferenceText);
            catalog.SetSection(channel, "action_rules", RPGFormatConstraint ?? string.Empty);
        }

        private void EnsurePromptEntrySeedFromLegacyData(RpgPromptCustomConfig rpgConfig)
        {
            EnsurePromptEntrySeedForChannel(RimTalkPromptChannel.Diplomacy);
            EnsurePromptEntrySeedForChannel(RimTalkPromptChannel.Rpg);
        }

        private void EnsurePromptEntrySeedForChannel(RimTalkPromptChannel channel)
        {
            RimTalkChannelCompatConfig current = GetRimTalkChannelConfigClone(channel);
            bool dirty = false;
            if (!HasMeaningfulPromptEntries(current))
            {
                SystemPromptConfig systemConfig = _systemPromptConfig ?? PromptPersistenceService.Instance?.LoadConfig();
                RpgPromptCustomConfig rpgConfig = RpgPromptCustomStore.LoadOrDefault();
                dirty |= EnsurePromptEntrySeedForChannel(channel, systemConfig, rpgConfig, current);
            }

            dirty |= EnsurePromptEntryChannelCoverage(channel, current);
            if (dirty)
            {
                current.CompatTemplate = ComposePromptEntryTextByRole(
                    current.PromptEntries,
                    includeSystemRole: true,
                    includeNonSystemRole: true);
                SetRimTalkChannelConfig(channel, current);
            }
        }

        private static bool EnsurePromptEntrySeedForChannel(
            RimTalkPromptChannel channel,
            SystemPromptConfig systemConfig,
            RpgPromptCustomConfig rpgConfig,
            RimTalkChannelCompatConfig current)
        {
            if (current == null || HasMeaningfulPromptEntries(current))
            {
                return false;
            }

            List<RimTalkPromptEntryConfig> legacyEntries = BuildLegacyPromptEntries(channel, systemConfig, rpgConfig);
            if (legacyEntries.Count == 0)
            {
                return false;
            }

            current.PromptEntries = legacyEntries;
            current.EnablePromptCompat = true;
            return true;
        }

        private static bool EnsurePromptEntryChannelCoverage(
            RimTalkPromptChannel channel,
            RimTalkChannelCompatConfig config)
        {
            bool changed = RimTalkPromptEntrySeedSynchronizer.EnsureCoverage(channel, config);
            changed |= EnforcePromptWorkbenchSectionLayout(channel, config);
            return changed;
        }

        private static bool EnforcePromptWorkbenchSectionLayout(
            RimTalkPromptChannel rootChannel,
            RimTalkChannelCompatConfig config)
        {
            if (config == null)
            {
                return false;
            }

            config.PromptEntries ??= new List<RimTalkPromptEntryConfig>();
            bool changed = false;
            IReadOnlyList<string> channels = RimTalkPromptEntryChannelCatalog.GetSelectableChannels(rootChannel);
            for (int i = 0; i < channels.Count; i++)
            {
                changed |= NormalizePromptChannelEntries(config.PromptEntries, channels[i]);
            }

            return changed;
        }

        private static bool NormalizePromptChannelEntries(
            List<RimTalkPromptEntryConfig> allEntries,
            string promptChannel)
        {
            string normalizedChannel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            List<RimTalkPromptEntryConfig> current = allEntries
                .Where(entry => entry != null &&
                                string.Equals(
                                    RimTalkPromptEntryChannelCatalog.NormalizeLoose(entry.PromptChannel),
                                    normalizedChannel,
                                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            List<RimTalkPromptEntryConfig> rebuilt = BuildCanonicalPromptEntriesForChannel(current, normalizedChannel);
            if (ArePromptEntryListsEquivalent(current, rebuilt))
            {
                return false;
            }

            ReplacePromptChannelEntries(allEntries, normalizedChannel, rebuilt);
            return true;
        }

        private static List<RimTalkPromptEntryConfig> BuildCanonicalPromptEntriesForChannel(
            List<RimTalkPromptEntryConfig> sourceEntries,
            string promptChannel)
        {
            if (sourceEntries == null || sourceEntries.Count == 0)
            {
                return BuildLegacyOrderedSectionEntries(new List<RimTalkPromptEntryConfig>(), promptChannel);
            }

            bool hasSectionIdentity = sourceEntries.Any(entry => !string.IsNullOrWhiteSpace(entry?.SectionId));
            if (!hasSectionIdentity)
            {
                return BuildLegacyOrderedSectionEntries(sourceEntries, promptChannel);
            }

            bool hasKnownSection = sourceEntries.Any(entry => TryResolvePromptSectionIndex(entry, out _));
            return hasKnownSection
                ? BuildCoverageSectionEntries(sourceEntries, promptChannel)
                : BuildLegacyOrderedSectionEntries(sourceEntries, promptChannel);
        }

        internal static List<RimTalkPromptEntryConfig> BuildDefaultSectionEntriesForChannel(string promptChannel)
        {
            return BuildLegacyOrderedSectionEntries(new List<RimTalkPromptEntryConfig>(), promptChannel);
        }

        internal static RimTalkChannelCompatConfig CreateCanonicalDefaultRimTalkChannelConfig(RimTalkPromptChannel rootChannel)
        {
            return PromptLegacyCompatMigration.CreateLegacyAdapterFromPromptSections(
                RimTalkPromptEntryDefaultsProvider.GetDefaultsSnapshot(),
                rootChannel);
        }

        private static List<RimTalkPromptEntryConfig> BuildLegacyOrderedSectionEntries(
            IReadOnlyList<RimTalkPromptEntryConfig> sourceEntries,
            string promptChannel)
        {
            var result = new List<RimTalkPromptEntryConfig>(PromptWorkbenchSections.Length);
            for (int i = 0; i < PromptWorkbenchSections.Length; i++)
            {
                RimTalkPromptEntryConfig source = sourceEntries != null && i < sourceEntries.Count ? sourceEntries[i] : null;
                result.Add(BuildCanonicalSectionEntry(source, promptChannel, i));
            }

            return result;
        }

        private static List<RimTalkPromptEntryConfig> BuildCoverageSectionEntries(
            IReadOnlyList<RimTalkPromptEntryConfig> sourceEntries,
            string promptChannel)
        {
            var used = new Dictionary<int, RimTalkPromptEntryConfig>();
            var orderedIndexes = new List<int>();
            for (int i = 0; i < sourceEntries.Count; i++)
            {
                RimTalkPromptEntryConfig entry = sourceEntries[i];
                if (!TryResolvePromptSectionIndex(entry, out int index) || used.ContainsKey(index))
                {
                    continue;
                }

                used[index] = entry;
                orderedIndexes.Add(index);
            }

            for (int i = 0; i < PromptWorkbenchSections.Length; i++)
            {
                if (!used.ContainsKey(i))
                {
                    orderedIndexes.Add(i);
                }
            }

            var result = new List<RimTalkPromptEntryConfig>(PromptWorkbenchSections.Length);
            for (int i = 0; i < orderedIndexes.Count; i++)
            {
                int index = orderedIndexes[i];
                used.TryGetValue(index, out RimTalkPromptEntryConfig source);
                result.Add(BuildCanonicalSectionEntry(source, promptChannel, index));
            }

            return result;
        }

        private static RimTalkPromptEntryConfig BuildCanonicalSectionEntry(
            RimTalkPromptEntryConfig source,
            string promptChannel,
            int sectionIndex)
        {
            PromptWorkbenchSectionDefinition section = PromptWorkbenchSections[sectionIndex];
            RimTalkPromptEntryConfig target = source?.Clone() ?? new RimTalkPromptEntryConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Role = "System",
                CustomRole = string.Empty,
                Position = "Relative",
                InChatDepth = 0,
                Enabled = true,
                Content = string.Empty
            };

            target.SectionId = section.Id;
            target.Name = section.EnglishName;
            target.PromptChannel = promptChannel;
            if (ShouldResetPromptEntryContent(target.Content))
            {
                target.Content = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(target.Content))
            {
                target.Content = ResolveDefaultPromptEntryContent(promptChannel, section.Id);
                if (string.IsNullOrWhiteSpace(target.Content) &&
                    string.Equals(section.Id, ModVariablesSectionId, StringComparison.OrdinalIgnoreCase))
                {
                    target.Content = PromptRuntimeVariableBridge.BuildModVariablesSectionContent();
                }
            }

            return target;
        }

        private void AutoPopulatePromptSectionCatalogModVariables()
        {
            PromptRuntimeVariableBridge.RefreshRimTalkCustomVariableSnapshot(force: true);
            string autoContent = PromptRuntimeVariableBridge.BuildModVariablesSectionContent();
            if (string.IsNullOrWhiteSpace(autoContent))
            {
                return;
            }

            PromptUnifiedCatalog unified = GetPromptUnifiedCatalogClone();
            bool changed = false;
            List<string> channels = PromptSectionSchemaCatalog.GetAllWorkspaceChannels()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!channels.Contains(RimTalkPromptEntryChannelCatalog.Any, StringComparer.OrdinalIgnoreCase))
            {
                channels.Insert(0, RimTalkPromptEntryChannelCatalog.Any);
            }

            for (int i = 0; i < channels.Count; i++)
            {
                string channel = channels[i];
                string existing = unified.ResolveSection(channel, ModVariablesSectionId);
                if (!string.IsNullOrWhiteSpace(existing))
                {
                    continue;
                }

                unified.SetSection(channel, ModVariablesSectionId, autoContent);
                changed = true;
            }

            if (changed)
            {
                SetPromptUnifiedCatalog(unified, persistToFiles: true);
            }
        }

        private static bool ShouldResetPromptEntryContent(string content)
        {
            string value = content?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return LooksLikeRenderedStructuredPrompt(value) || LooksLikeCompiledPromptPreview(value);
        }

        private static bool LooksLikeRenderedStructuredPrompt(string content)
        {
            string value = content?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (value.IndexOf("<prompt_context>", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("</prompt_context>", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("=== PREVIEW DIAGNOSTICS ===", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string[] xmlMarkers =
            {
                "<channel>",
                "<mode>",
                "<environment>",
                "<fact_grounding>",
                "<instruction_stack>",
                "<response_contract>",
                "<dynamic_npc_personal_memory>",
                "<actor_state>"
            };
            int xmlHits = CountMarkerHits(value, xmlMarkers);
            if (xmlHits >= 3 && value.Length >= 300)
            {
                return true;
            }

            string[] blockMarkers =
            {
                "=== ENVIRONMENT PARAMETERS ===",
                "=== RECENT WORLD EVENTS & BATTLE INTEL ===",
                "=== SCENE PROMPT LAYERS ===",
                "=== FACT GROUNDING RULES ===",
                "=== CHARACTER STATUS (YOU) ==="
            };
            return CountMarkerHits(value, blockMarkers) >= 3 && value.Length >= 500;
        }

        private static bool LooksLikeCompiledPromptPreview(string content)
        {
            string value = content?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (value.IndexOf("========== FULL MESSAGE LOG ==========", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return value.IndexOf("[FILE]", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   value.IndexOf("[CODE]", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   value.IndexOf("{{", StringComparison.OrdinalIgnoreCase) < 0 &&
                   value.Length >= 500;
        }

        private static int CountMarkerHits(string value, IEnumerable<string> markers)
        {
            if (string.IsNullOrWhiteSpace(value) || markers == null)
            {
                return 0;
            }

            int hits = 0;
            foreach (string marker in markers)
            {
                if (!string.IsNullOrWhiteSpace(marker) &&
                    value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hits++;
                }
            }

            return hits;
        }

        private static string ResolveDefaultPromptEntryContent(string promptChannel, string sectionId)
        {
            return RimTalkPromptEntryDefaultsProvider.ResolveContent(promptChannel, sectionId);
        }

        private static bool TryResolvePromptSectionIndex(RimTalkPromptEntryConfig entry, out int index)
        {
            string sectionId = entry?.SectionId?.Trim();
            for (int i = 0; i < PromptWorkbenchSections.Length; i++)
            {
                PromptWorkbenchSectionDefinition section = PromptWorkbenchSections[i];
                if (!string.IsNullOrWhiteSpace(sectionId) &&
                    string.Equals(section.Id, sectionId, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    return true;
                }

                if (TokenEqualsSection(entry?.Name, section))
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        private static bool TokenEqualsSection(string name, PromptWorkbenchSectionDefinition section)
        {
            string normalized = NormalizeSectionToken(name);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            if (string.Equals(normalized, NormalizeSectionToken(section.EnglishName), StringComparison.Ordinal))
            {
                return true;
            }

            for (int i = 0; i < section.Aliases.Length; i++)
            {
                if (string.Equals(normalized, NormalizeSectionToken(section.Aliases[i]), StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeSectionToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }

            return sb.ToString();
        }

        private static bool ArePromptEntryListsEquivalent(
            IReadOnlyList<RimTalkPromptEntryConfig> left,
            IReadOnlyList<RimTalkPromptEntryConfig> right)
        {
            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!ArePromptEntriesEquivalent(left[i], right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ArePromptEntriesEquivalent(RimTalkPromptEntryConfig left, RimTalkPromptEntryConfig right)
        {
            if (left == null || right == null)
            {
                return left == right;
            }

            return string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
                   string.Equals(left.SectionId, right.SectionId, StringComparison.Ordinal) &&
                   string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
                   string.Equals(left.Role, right.Role, StringComparison.Ordinal) &&
                   string.Equals(left.CustomRole, right.CustomRole, StringComparison.Ordinal) &&
                   string.Equals(left.Position, right.Position, StringComparison.Ordinal) &&
                   left.InChatDepth == right.InChatDepth &&
                   left.Enabled == right.Enabled &&
                   string.Equals(left.PromptChannel, right.PromptChannel, StringComparison.Ordinal) &&
                   string.Equals(left.Content, right.Content, StringComparison.Ordinal);
        }

        private static void ReplacePromptChannelEntries(
            List<RimTalkPromptEntryConfig> allEntries,
            string promptChannel,
            List<RimTalkPromptEntryConfig> rebuilt)
        {
            int insertIndex = allEntries.Count;
            for (int i = 0; i < allEntries.Count; i++)
            {
                RimTalkPromptEntryConfig entry = allEntries[i];
                if (entry == null)
                {
                    continue;
                }

                if (!string.Equals(
                        RimTalkPromptEntryChannelCatalog.NormalizeLoose(entry.PromptChannel),
                        promptChannel,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                insertIndex = i;
                break;
            }

            allEntries.RemoveAll(entry =>
                entry != null &&
                string.Equals(
                    RimTalkPromptEntryChannelCatalog.NormalizeLoose(entry.PromptChannel),
                    promptChannel,
                    StringComparison.OrdinalIgnoreCase));

            allEntries.InsertRange(insertIndex, rebuilt);
        }

        private static bool HasMeaningfulPromptEntries(RimTalkChannelCompatConfig config)
        {
            if (config?.PromptEntries == null || config.PromptEntries.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < config.PromptEntries.Count; i++)
            {
                RimTalkPromptEntryConfig entry = config.PromptEntries[i];
                string text = entry?.Content?.Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (!string.Equals(text, DefaultRimTalkCompatTemplate.Trim(), StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<RimTalkPromptEntryConfig> BuildLegacyPromptEntries(
            RimTalkPromptChannel channel,
            SystemPromptConfig systemConfig,
            RpgPromptCustomConfig rpgConfig)
        {
            var entries = new List<RimTalkPromptEntryConfig>();
            if (channel == RimTalkPromptChannel.Diplomacy)
            {
                AddLegacyPromptEntry(
                    entries,
                    "Global System Prompt",
                    "System",
                    systemConfig?.GlobalSystemPrompt,
                    RimTalkPromptEntryChannelCatalog.DiplomacyDialogue);
                AddLegacyPromptEntry(
                    entries,
                    "Global Dialogue Prompt",
                    "System",
                    systemConfig?.GlobalDialoguePrompt,
                    RimTalkPromptEntryChannelCatalog.DiplomacyDialogue);
                return entries;
            }

            AddLegacyPromptEntry(
                entries,
                "Role Setting",
                "System",
                rpgConfig?.RoleSetting,
                RimTalkPromptEntryChannelCatalog.RpgDialogue);
            AddLegacyPromptEntry(
                entries,
                "Dialogue Style",
                "Assistant",
                rpgConfig?.DialogueStyle,
                RimTalkPromptEntryChannelCatalog.RpgDialogue);
            AddLegacyPromptEntry(
                entries,
                "Format Constraint",
                "System",
                rpgConfig?.FormatConstraint,
                RimTalkPromptEntryChannelCatalog.RpgDialogue);
            return entries;
        }

        private static void AddLegacyPromptEntry(
            ICollection<RimTalkPromptEntryConfig> entries,
            string name,
            string role,
            string content,
            string promptChannel)
        {
            string normalized = content?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            List<LegacyPromptEntrySeed> seeds = SplitLegacyPromptEntrySeeds(name ?? "Entry", normalized);
            if (seeds.Count == 0)
            {
                return;
            }

            for (int i = 0; i < seeds.Count; i++)
            {
                LegacyPromptEntrySeed seed = seeds[i];
                entries.Add(new RimTalkPromptEntryConfig
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = seed.Name,
                    Role = role ?? "System",
                    Position = "Relative",
                    InChatDepth = 0,
                    Enabled = true,
                    PromptChannel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel),
                    Content = seed.Content
                });
            }
        }

        private void SyncLegacyPromptFieldsFromEntryChannels()
        {
            EnsurePromptEntrySeedForChannel(RimTalkPromptChannel.Diplomacy);
            EnsurePromptEntrySeedForChannel(RimTalkPromptChannel.Rpg);

            RimTalkChannelCompatConfig diplomacy = GetRimTalkChannelConfigClone(RimTalkPromptChannel.Diplomacy);
            string diplomacySystem = ComposePromptEntryTextByRole(diplomacy?.PromptEntries, includeSystemRole: true, includeNonSystemRole: false);
            string diplomacyDialogue = ComposePromptEntryTextByRole(diplomacy?.PromptEntries, includeSystemRole: false, includeNonSystemRole: true);

            if (!string.IsNullOrWhiteSpace(diplomacySystem))
            {
                SystemPromptConfigData.GlobalSystemPrompt = diplomacySystem;
                GlobalSystemPrompt = diplomacySystem;
            }

            if (!string.IsNullOrWhiteSpace(diplomacyDialogue))
            {
                SystemPromptConfigData.GlobalDialoguePrompt = diplomacyDialogue;
                GlobalDialoguePrompt = diplomacyDialogue;
            }

            RimTalkChannelCompatConfig rpg = GetRimTalkChannelConfigClone(RimTalkPromptChannel.Rpg);
            string rpgRole = ComposePromptEntryTextByRole(rpg?.PromptEntries, includeSystemRole: true, includeNonSystemRole: false);
            string rpgDialogue = ComposePromptEntryTextByRole(rpg?.PromptEntries, includeSystemRole: false, includeNonSystemRole: true);
            if (!string.IsNullOrWhiteSpace(rpgRole))
            {
                RPGRoleSetting = rpgRole;
            }

            if (!string.IsNullOrWhiteSpace(rpgDialogue))
            {
                RPGDialogueStyle = rpgDialogue;
            }

            if (string.IsNullOrWhiteSpace(RPGFormatConstraint))
            {
                string combined = ComposePromptEntryTextByRole(rpg?.PromptEntries, includeSystemRole: true, includeNonSystemRole: true);
                if (!string.IsNullOrWhiteSpace(combined))
                {
                    RPGFormatConstraint = combined;
                }
            }
        }

        private static string ComposePromptEntryTextByRole(
            IEnumerable<RimTalkPromptEntryConfig> entries,
            bool includeSystemRole,
            bool includeNonSystemRole)
        {
            List<string> filtered = CollectPromptEntryContents(entries, enabledOnly: true, includeSystemRole, includeNonSystemRole);
            if (filtered.Count == 0)
            {
                filtered = CollectPromptEntryContents(entries, enabledOnly: true, includeSystemRole: true, includeNonSystemRole: true);
            }

            if (filtered.Count == 0)
            {
                filtered = CollectPromptEntryContents(entries, enabledOnly: false, includeSystemRole: true, includeNonSystemRole: true);
            }

            return string.Join("\n\n", filtered.Where(item => !string.IsNullOrWhiteSpace(item))).Trim();
        }

        private static List<string> CollectPromptEntryContents(
            IEnumerable<RimTalkPromptEntryConfig> entries,
            bool enabledOnly,
            bool includeSystemRole,
            bool includeNonSystemRole)
        {
            var result = new List<string>();
            if (entries == null)
            {
                return result;
            }

            foreach (RimTalkPromptEntryConfig entry in entries)
            {
                if (entry == null || (enabledOnly && !entry.Enabled))
                {
                    continue;
                }

                string text = entry.Content?.Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                bool isSystemRole = string.Equals(entry.Role, "System", StringComparison.OrdinalIgnoreCase);
                if ((isSystemRole && !includeSystemRole) || (!isSystemRole && !includeNonSystemRole))
                {
                    continue;
                }

                result.Add(text);
            }

            return result;
        }

        public void DoWindowContents(Rect inRect)
        {
            if (selectedTab < 0 || selectedTab >= tabNames.Length)
            {
                selectedTab = 0;
            }

            // Draw tabs at the top
            float tabHeight = 32f;
            Rect tabRect = new Rect(inRect.x, inRect.y, inRect.width, tabHeight);
            DrawTabs(tabRect);

            // Content area below tabs
            Rect contentRect = new Rect(inRect.x, inRect.y + tabHeight + 5f, inRect.width, inRect.height - tabHeight - 5f);

            if (selectedTab == 1)
            {
                DrawTab_AIControl(contentRect);
            }
            else if (selectedTab == 2)
            {
                DrawTab_PromptSettingsDirect(contentRect);
            }
            else if (selectedTab == 3)
            {
                DrawTab_DiplomacyImageApi(contentRect);
            }
            else
            {
                Listing_Standard listingStandard = new Listing_Standard();
                listingStandard.Begin(contentRect);

                switch (selectedTab)
                {
                    case 0:
                        DrawTab_APISettings(listingStandard);
                        break;
                }

                listingStandard.End();
            }
        }

        private void DrawTabs(Rect tabRect)
        {
            float tabWidth = tabRect.width / tabNames.Length;
            
            for (int i = 0; i < tabNames.Length; i++)
            {
                Rect singleTabRect = new Rect(tabRect.x + i * tabWidth, tabRect.y, tabWidth, tabRect.height);
                
                bool isSelected = i == selectedTab;
                
                // Tab background
                if (isSelected)
                {
                    Widgets.DrawBoxSolid(singleTabRect, new Color(0.2f, 0.4f, 0.6f));
                }
                else
                {
                    Widgets.DrawBoxSolid(singleTabRect, new Color(0.15f, 0.15f, 0.15f));
                }
                
                // Tab border
                Widgets.DrawBox(singleTabRect);
                
                // Tab label
                TextAnchor oldAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = isSelected ? Color.white : Color.gray;
                Widgets.Label(singleTabRect, tabNames[i].Translate());
                GUI.color = Color.white;
                Text.Anchor = oldAnchor;
                RegisterTooltip(singleTabRect, GetSettingsTabTooltipKey(i));
                
                // Click handling
                if (Widgets.ButtonInvisible(singleTabRect))
                {
                    if (i == 2)
                    {
                        OpenPromptWorkbenchWindow();
                    }
                    else
                    {
                        selectedTab = i;
                    }
                }
            }
        }

        private void DrawPromptWorkbenchLauncherTab(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.10f, 0.10f, 0.12f));
            Rect inner = rect.ContractedBy(12f);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 28f), "RimChat_PromptWorkbenchLauncherTitle".Translate());
            GUI.color = Color.gray;
            Widgets.Label(new Rect(inner.x, inner.y + 30f, inner.width, 50f), "RimChat_PromptWorkbenchLauncherHint".Translate());
            GUI.color = Color.white;

            Rect buttonRect = new Rect(inner.x, inner.y + 86f, 260f, 32f);
            if (Widgets.ButtonText(buttonRect, "RimChat_Tab_PromptWorkbench".Translate()))
            {
                OpenPromptWorkbenchWindow();
            }
        }

        private void DrawTab_APISettings(Listing_Standard listing)
        {
            DrawApiSettingsHeaderBar(listing);
            listing.GapLine();

            // Provider Selection
            DrawProviderSelection(listing);
            listing.Gap();

            // Draw appropriate section
            if (UseCloudProviders)
            {
                DrawCloudProvidersSection(listing);
            }
            else
            {
                DrawLocalProviderSection(listing);
            }

            listing.Gap();
            DrawConnectionTestButton(listing);

            listing.Gap();
            DrawDebugSettingsSection(listing);

            listing.Gap();
            DrawLatestDialogueTokenUsage(listing);
            listing.Gap(6f);
            DrawPromptLanguageSettings(listing);
        }

        public string GetEffectivePromptLanguage()
        {
            if (!PromptLanguageFollowSystem && !string.IsNullOrWhiteSpace(PromptLanguageOverride))
            {
                return PromptLanguageOverride.Trim();
            }

            return ResolveSystemPromptLanguage();
        }

        private void DrawDebugSettingsSection(Listing_Standard listing)
        {
            Rect headerRect = listing.GetRect(28f);
            Rect titleRect = new Rect(headerRect.x, headerRect.y, Mathf.Max(120f, headerRect.width - 170f), headerRect.height);
            Rect buttonRect = new Rect(headerRect.xMax - 160f, headerRect.y, 160f, headerRect.height);
            Widgets.Label(titleRect, "RimChat_DebugSettings".Translate());
            if (Widgets.ButtonText(buttonRect, "RimChat_OpenApiDebugWindowButton".Translate()))
            {
                Find.WindowStack.Add(new Dialog_ApiDebugObservability());
            }
            RegisterTooltip(buttonRect, "RimChat_OpenApiDebugWindowButtonTooltip");
            listing.GapLine();

            listing.CheckboxLabeled("RimChat_EnableDebugLogging".Translate(), ref EnableDebugLogging);
            if (EnableDebugLogging)
            {
                listing.CheckboxLabeled("RimChat_LogAIRequests".Translate(), ref LogAIRequests);
                listing.CheckboxLabeled("RimChat_LogAIResponses".Translate(), ref LogAIResponses);
                listing.CheckboxLabeled("RimChat_LogInternals".Translate(), ref LogInternals);
                listing.CheckboxLabeled("RimChat_LogFullMessages".Translate(), ref LogFullMessages);
            }
        }



        private Vector2 promptTabScrollPosition = Vector2.zero;
        private bool _promptWorkbenchExperimentalEnabled;

        internal void DrawTab_PromptSettingsDirect(Rect rect)
        {
            try
            {
                DrawPromptSectionWorkspace(rect);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Prompt settings page render failed: {ex}");
                Widgets.Label(rect, "RimChat_PromptRenderFailed".Translate());
            }
        }

        internal void SetPromptWorkbenchExperimentalEnabled(bool enabled)
        {
            _promptWorkbenchExperimentalEnabled = false;
        }

        private void DrawProviderSelection(Listing_Standard listing)
        {
            Rect radioRect1 = listing.GetRect(24f);
            if (Widgets.RadioButtonLabeled(radioRect1, "RimChat_CloudProviders".Translate(), UseCloudProviders))
            {
                UseCloudProviders = true;
            }
            RegisterTooltip(radioRect1, "RimChat_CloudProvidersDesc");

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect cloudDescRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(cloudDescRect, "RimChat_CloudProvidersDesc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            listing.Gap(3f);

            Rect radioRect2 = listing.GetRect(24f);
            if (Widgets.RadioButtonLabeled(radioRect2, "RimChat_LocalProvider".Translate(), !UseCloudProviders))
            {
                UseCloudProviders = false;
            }
            RegisterTooltip(radioRect2, "RimChat_LocalProviderDesc");

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect localDescRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(localDescRect, "RimChat_LocalProviderDesc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawPromptLanguageSettings(Listing_Standard listing)
        {
            string systemLanguage = ResolveSystemPromptLanguage();
            string modeText = PromptLanguageFollowSystem
                ? "RimChat_OutputLanguageFollowSystem".Translate(systemLanguage)
                : "RimChat_OutputLanguageCustom".Translate();
            string effectiveLanguage = GetEffectivePromptLanguage();

            Rect compactRow = listing.GetRect(24f);
            Rect toggleRect = new Rect(compactRow.x + compactRow.width - 24f, compactRow.y, 24f, compactRow.height);
            Rect labelRect = new Rect(compactRow.x, compactRow.y, compactRow.width - 30f, compactRow.height);
            string summaryText = "RimChat_OutputLanguage".Translate() + ": " + modeText;
            if (!PromptLanguageFollowSystem)
            {
                summaryText += " (" + effectiveLanguage + ")";
            }
            Widgets.Label(labelRect, summaryText);
            RegisterTooltip(labelRect, "RimChat_OutputLanguageTooltip");
            if (Widgets.ButtonText(toggleRect, showPromptLanguageSettings ? "^" : "v"))
            {
                showPromptLanguageSettings = !showPromptLanguageSettings;
                SoundDefOf.Click.PlayOneShotOnCamera(null);
            }

            if (!showPromptLanguageSettings)
            {
                return;
            }

            listing.Gap(2f);
            Rect followRect = listing.GetRect(24f);
            if (Widgets.RadioButtonLabeled(followRect, "RimChat_OutputLanguageFollowSystem".Translate(systemLanguage), PromptLanguageFollowSystem))
            {
                PromptLanguageFollowSystem = true;
            }
            RegisterTooltip(followRect, "RimChat_OutputLanguageFollowSystemTooltip");
            Rect customRect = listing.GetRect(24f);
            if (Widgets.RadioButtonLabeled(customRect, "RimChat_OutputLanguageCustom".Translate(), !PromptLanguageFollowSystem))
            {
                PromptLanguageFollowSystem = false;
            }
            RegisterTooltip(customRect, "RimChat_OutputLanguageCustomTooltip");
            if (!PromptLanguageFollowSystem)
            {
                Rect customLangRect = listing.GetRect(24f);
                PromptLanguageOverride = DrawTextFieldWithPlaceholder(customLangRect, PromptLanguageOverride, "RimChat_OutputLanguageCustomPlaceholder".Translate());
                RegisterTooltip(customLangRect, "RimChat_OutputLanguageCustomTooltip");
            }
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect hintRect = listing.GetRect(Text.LineHeight * 2f);
            Widgets.Label(hintRect, "RimChat_OutputLanguageHint".Translate());
            RegisterTooltip(hintRect, "RimChat_OutputLanguageTooltip");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private static string ResolveSystemPromptLanguage()
        {
            string folder = LanguageDatabase.activeLanguage?.folderName;
            if (string.IsNullOrWhiteSpace(folder))
            {
                return "English";
            }

            return folder switch
            {
                "ChineseSimplified" => "Chinese (Simplified)",
                "ChineseTraditional" => "Chinese (Traditional)",
                _ => folder.Replace('_', ' ')
            };
        }

        private void DrawCloudProvidersSection(Listing_Standard listing)
        {
            Rect headerRect = listing.GetRect(24f);

            float addBtnSize = 24f;
            Rect addButtonRect = new Rect(headerRect.x + headerRect.width - addBtnSize, headerRect.y, addBtnSize, addBtnSize);
            headerRect.width -= (addBtnSize + 5f);

            Widgets.Label(headerRect, "RimChat_CloudApiConfigurations".Translate());

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect descRect = listing.GetRect(Text.LineHeight * 2);
            descRect.width -= 35f;
            Widgets.Label(descRect, "RimChat_CloudApiConfigurationsDesc".Translate());
            GUI.color = Color.white;

            Color prevColor = GUI.color;
            GUI.color = new Color(0.3f, 0.9f, 0.3f);
            if (Widgets.ButtonText(addButtonRect, "+"))
            {
                SoundDefOf.Click.PlayOneShotOnCamera(null);
                CloudConfigs.Add(new ApiConfig());
            }
            GUI.color = prevColor;

            Text.Font = GameFont.Small;
            listing.Gap(6f);

            // Table Headers
            Rect tableHeaderRect = listing.GetRect(20f);
            float x = tableHeaderRect.x;
            float y = tableHeaderRect.y;
            float height = tableHeaderRect.height;
            float totalWidth = tableHeaderRect.width;

            float providerWidth = 90f;
            float modelWidth = 180f;
            float controlsWidth = 100f;

            Rect providerHeaderRect = new Rect(x, y, providerWidth, height);
            Widgets.Label(providerHeaderRect, "RimChat_ProviderHeader".Translate());
            RegisterTooltip(providerHeaderRect, "RimChat_ApiProviderFieldTooltip");

            float middleStartX = x + providerWidth + 5f;
            Rect apiKeyHeaderRect = new Rect(middleStartX, y, 180f, height);
            Widgets.Label(apiKeyHeaderRect, "RimChat_ApiKeyHeader".Translate());
            RegisterTooltip(apiKeyHeaderRect, "RimChat_ApiKeyFieldTooltip");

            Rect modelHeaderRect = new Rect(totalWidth - controlsWidth - modelWidth - 5f, y, modelWidth, height);
            Widgets.Label(modelHeaderRect, "RimChat_ModelHeader".Translate());
            RegisterTooltip(modelHeaderRect, "RimChat_ApiModelFieldTooltip");

            Rect enabledHeaderRect = new Rect(totalWidth - controlsWidth + 5f, y, controlsWidth, height);
            Widgets.Label(enabledHeaderRect, "RimChat_EnabledHeader".Translate());

            listing.Gap(3f);

            for (int i = 0; i < CloudConfigs.Count; i++)
            {
                if (DrawCloudConfigRow(listing, CloudConfigs[i], i))
                {
                    CloudConfigs.RemoveAt(i);
                    i--;
                }
                listing.Gap(2f);
            }

            Text.Font = GameFont.Small;
        }

        private bool DrawCloudConfigRow(Listing_Standard listing, ApiConfig config, int index)
        {
            Text.Font = GameFont.Tiny;

            Rect rowRect = listing.GetRect(24f);
            float x = rowRect.x;
            float y = rowRect.y;
            float height = rowRect.height;
            float totalWidth = rowRect.width;

            float providerWidth = 90f;
            float modelWidth = 180f;
            float controlsWidth = 100f;
            float gap = 5f;

            float middleZoneWidth = totalWidth - providerWidth - modelWidth - controlsWidth - (gap * 3);
            float middleStartX = x + providerWidth + gap;

            Color originalColor = GUI.color;
            if (!config.IsEnabled)
            {
                GUI.color = new Color(0.7f, 0.7f, 0.7f, 0.8f);
            }

            // 1. Provider Dropdown
            DrawProviderDropdown(x, y, height, providerWidth, config);

            // 2. Middle Zone (API Key or Custom URL)
            if (config.Provider == AIProvider.Custom)
            {
                float modeWidth = Mathf.Clamp(middleZoneWidth * 0.22f, 88f, 136f);
                float editableWidth = Mathf.Max(60f, middleZoneWidth - modeWidth - (gap * 2));
                float keyWidth = editableWidth * 0.38f;
                float urlWidth = editableWidth - keyWidth;

                DrawApiKeyInput(middleStartX, y, height, keyWidth, config);
                DrawBaseUrlInput(middleStartX + keyWidth + gap, y, height, urlWidth, config);
                DrawCustomUrlModeSelector(
                    middleStartX + keyWidth + gap + urlWidth + gap,
                    y,
                    height,
                    modeWidth,
                    config);
            }
            else
            {
                DrawApiKeyInput(middleStartX, y, height, middleZoneWidth, config);
            }

            // 3. Model
            float modelStartX = middleStartX + middleZoneWidth + gap;
            DrawModelSelector(modelStartX, y, height, modelWidth, config);

            GUI.color = originalColor;

            // 4. Controls (Enable + Reorder + Delete)
            float btnSize = 22f;
            float btnGap = 2f;

            float deleteX = totalWidth - btnSize;
            float downX = deleteX - btnGap - btnSize;
            float upX = downX - btnGap - btnSize;

            float controlsStartX = totalWidth - controlsWidth;
            float checkboxSpaceWidth = upX - controlsStartX;
            float checkboxX = controlsStartX + (checkboxSpaceWidth - 24f) / 2f;

            Rect toggleRect = new Rect(checkboxX, y, 24f, height);
            Widgets.Checkbox(new Vector2(toggleRect.x, toggleRect.y), ref config.IsEnabled, 20f);
            if (Mouse.IsOver(toggleRect)) TooltipHandler.TipRegion(toggleRect, "RimChat_EnableDisableTooltip".Translate());

            // Reorder buttons
            Rect upButtonRect = new Rect(upX, y, btnSize, height);
            if (Widgets.ButtonText(upButtonRect, "^") && index > 0)
            {
                SoundDefOf.Click.PlayOneShotOnCamera(null);
                (CloudConfigs[index], CloudConfigs[index - 1]) = (CloudConfigs[index - 1], CloudConfigs[index]);
            }

            Rect downButtonRect = new Rect(downX, y, btnSize, height);
            if (Widgets.ButtonText(downButtonRect, "v") && index < CloudConfigs.Count - 1)
            {
                SoundDefOf.Click.PlayOneShotOnCamera(null);
                (CloudConfigs[index], CloudConfigs[index + 1]) = (CloudConfigs[index + 1], CloudConfigs[index]);
            }

            // Delete button
            Rect deleteRect = new Rect(deleteX, y, btnSize, height);
            bool deleteClicked = false;
            bool canDelete = CloudConfigs.Count > 1;

            Color prevDeleteColor = GUI.color;
            if (canDelete)
            {
                GUI.color = new Color(1f, 0.4f, 0.4f);
            }
            else
            {
                GUI.color = Color.gray;
            }

            if (Widgets.ButtonText(deleteRect, "X", active: canDelete))
            {
                SoundDefOf.Click.PlayOneShotOnCamera(null);
                deleteClicked = true;
            }
            GUI.color = prevDeleteColor;

            Text.Font = GameFont.Tiny;
            return deleteClicked;
        }

        private void DrawProviderDropdown(float x, float y, float height, float width, ApiConfig config)
        {
            Rect providerRect = new Rect(x, y, width, height);
            RegisterTooltip(providerRect, "RimChat_ApiProviderFieldTooltip");
            if (Widgets.ButtonText(providerRect, config.Provider.GetLabel()))
            {
                List<FloatMenuOption> providerOptions = new List<FloatMenuOption>();
                foreach (AIProvider provider in Enum.GetValues(typeof(AIProvider)))
                {
                    if (provider == AIProvider.None) continue;

                    providerOptions.Add(new FloatMenuOption(provider.GetLabel(), () =>
                    {
                        config.Provider = provider;
                        if (provider == AIProvider.Custom)
                        {
                            config.SelectedModel = "Custom";
                        }
                        else
                        {
                            config.SelectedModel = "";
                        }
                        NormalizeCloudConfigUrl(config);
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(providerOptions));
            }
        }

        private void DrawApiKeyInput(float x, float y, float height, float width, ApiConfig config)
        {
            Rect apiKeyRect = new Rect(x, y, width, height);
            config.ApiKey = DrawTextFieldWithPlaceholder(apiKeyRect, config.ApiKey, "RimChat_Placeholder_ApiKey".Translate());
            RegisterTooltip(apiKeyRect, "RimChat_ApiKeyFieldTooltip");
        }

        private void DrawBaseUrlInput(float x, float y, float height, float width, ApiConfig config)
        {
            Rect baseUrlRect = new Rect(x, y, width, height);
            config.BaseUrl = ApiConfig.NormalizeUrl(DrawTextFieldWithPlaceholder(baseUrlRect, config.BaseUrl, "https:// ..."));
            RegisterTooltip(baseUrlRect, "RimChat_BaseUrlFieldTooltip");
        }

        private void DrawCustomUrlModeSelector(float x, float y, float height, float width, ApiConfig config)
        {
            Rect modeRect = new Rect(x, y, width, height);
            RegisterTooltip(modeRect, "RimChat_CustomUrlModeFieldTooltip");

            string label = config.CustomUrlMode == CustomUrlMode.FullEndpoint
                ? "RimChat_CustomUrlModeFullEndpoint".Translate()
                : "RimChat_CustomUrlModeBase".Translate();
            if (!Widgets.ButtonText(modeRect, label))
            {
                return;
            }

            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("RimChat_CustomUrlModeBase".Translate(), () =>
                {
                    config.CustomUrlMode = CustomUrlMode.BaseUrl;
                    config.MarkCustomUrlModeInitialized();
                }),
                new FloatMenuOption("RimChat_CustomUrlModeFullEndpoint".Translate(), () =>
                {
                    config.CustomUrlMode = CustomUrlMode.FullEndpoint;
                    config.MarkCustomUrlModeInitialized();
                })
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void DrawModelSelector(float x, float y, float height, float width, ApiConfig config)
        {
            Rect modelRect = new Rect(x, y, width, height);
            RegisterTooltip(modelRect, "RimChat_ApiModelFieldTooltip");

            if (config.SelectedModel == "Custom")
            {
                float xButtonWidth = 22f;
                float textFieldWidth = width - xButtonWidth - 2f;

                Rect textFieldRect = new Rect(x, y, textFieldWidth, height);
                Rect backButtonRect = new Rect(x + textFieldWidth + 2f, y, xButtonWidth, height);

                config.CustomModelName = DrawTextFieldWithPlaceholder(textFieldRect, config.CustomModelName, "Model ID");
                RegisterTooltip(textFieldRect, "RimChat_ApiModelFieldTooltip");
                RegisterTooltip(backButtonRect, "RimChat_ApiModelFieldTooltip");

                if (Widgets.ButtonText(backButtonRect, "<"))
                {
                    SoundDefOf.Click.PlayOneShotOnCamera(null);
                    config.SelectedModel = "";
                }
            }
            else
            {
                string buttonLabel = string.IsNullOrEmpty(config.SelectedModel) ? "RimChat_ChooseModel".Translate() : config.SelectedModel;
                if (Widgets.ButtonText(modelRect, buttonLabel))
                {
                    ShowModelSelectionMenu(config);
                }
            }
        }

        private string DrawTextFieldWithPlaceholder(Rect rect, string text, string placeholder)
        {
            string result = Widgets.TextField(rect, text);

            if (string.IsNullOrEmpty(result))
            {
                TextAnchor originalAnchor = Text.Anchor;
                Color originalColor = GUI.color;

                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.7f);

                Rect labelRect = new Rect(rect.x + 5f, rect.y, rect.width - 5f, rect.height);
                Widgets.Label(labelRect, placeholder);

                GUI.color = originalColor;
                Text.Anchor = originalAnchor;
            }

            return result;
        }

        private void ShowModelSelectionMenu(ApiConfig config)
        {
            bool allowBaseUrlOverride = config.Provider != AIProvider.DeepSeek;
            bool hasBaseUrlOverride = allowBaseUrlOverride && !string.IsNullOrWhiteSpace(config.BaseUrl);
            bool requiresApiKey = config.Provider != AIProvider.Custom && !hasBaseUrlOverride;
            if (requiresApiKey && string.IsNullOrWhiteSpace(config.ApiKey))
            {
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption("RimChat_EnterApiKey".Translate(), null)
                }));
                return;
            }

            string listModelsUrl = config.Provider.GetListModelsUrl();
            string providerFallbackUrl = BuildProviderModelListRequestUrl(config);
            if (hasBaseUrlOverride)
            {
                if (config.Provider == AIProvider.Custom && config.TryResolveCustomRuntimeEndpoints(out CustomUrlRuntimeResolution customResolved))
                {
                    listModelsUrl = customResolved.ModelsEndpoint;
                    providerFallbackUrl = string.Empty;
                    LogCustomUrlResolutionHint(customResolved);
                }
                else
                {
                    listModelsUrl = ApiConfig.ToModelsEndpoint(config.BaseUrl);
                }
            }

            if (string.IsNullOrEmpty(listModelsUrl))
            {
                // 婵″倹鐏塙RL娑撹櫣鈹栭敍宀€娲块幒銉︽▔缁€楦垮殰鐎规矮绠熼柅澶愩€?
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption("Custom", () => config.SelectedModel = "Custom")
                }));
                return;
            }
            
            string requestUrl = BuildModelListRequestUrl(config, listModelsUrl);
            string cacheKey = BuildModelCacheKey(config.Provider, listModelsUrl, config.ApiKey);

            void OpenMenu(List<string> models)
            {
                var options = new List<FloatMenuOption>();

                if (models != null && models.Any())
                {
                    options.AddRange(models.Select(model => new FloatMenuOption(model, () => config.SelectedModel = model)));
                }
                else
                {
                    options.Add(new FloatMenuOption("(no models found)", null));
                }

                options.Add(new FloatMenuOption("Custom", () => config.SelectedModel = "Custom"));
                Find.WindowStack.Add(new FloatMenu(options));
            }

            if (ModelCache.ContainsKey(cacheKey))
            {
                OpenMenu(ModelCache[cacheKey]);
            }
            else
            {
                // 閸忓牊妯夌粈鍝勫鏉炴垝鑵戦懣婊冨礋
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption("Loading models...", null)
                }));
                
                // 娴ｈ法鏁ら崡蹇曗柤瀵倹顒為懢宄板絿濡€崇€烽崚妤勩€?
                FetchModelsCoroutine(requestUrl, providerFallbackUrl, config.ApiKey, config.Provider, cacheKey, OpenMenu);
            }
        }

        private static void LogCustomUrlResolutionHint(CustomUrlRuntimeResolution resolution)
        {
            if (resolution.WasSiliconFlowHostMapped)
            {
                Log.Message($"[RimChat] Custom URL host mapped to API domain: {resolution.ChatEndpoint}");
            }

            if (resolution.HasSuspiciousBasePath)
            {
                Log.Warning("[RimChat] Custom BaseUrl path looks non-standard for Base URL mode. The value was kept unchanged.");
            }
        }

        private string BuildModelListRequestUrl(ApiConfig config, string baseUrl)
        {
            if (config.Provider == AIProvider.Google)
            {
                return AppendQueryParameter(baseUrl, "key", config.ApiKey);
            }

            return baseUrl;
        }

        private string BuildProviderModelListRequestUrl(ApiConfig config)
        {
            string providerUrl = config.Provider.GetListModelsUrl();
            if (string.IsNullOrWhiteSpace(providerUrl))
            {
                return string.Empty;
            }

            return BuildModelListRequestUrl(config, providerUrl);
        }

        private string BuildModelCacheKey(AIProvider provider, string baseUrl, string apiKey)
        {
            string keyFingerprint = ComputeApiKeyFingerprint(apiKey);
            return $"{provider}:{baseUrl}:{keyFingerprint}";
        }

        private static string ComputeApiKeyFingerprint(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return "nokey";
            }

            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(apiKey.Trim());
                byte[] hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash, 0, 6).Replace("-", string.Empty);
            }
        }

        private static string AppendQueryParameter(string url, string name, string value)
        {
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
            {
                return url;
            }

            if (url.IndexOf($"{name}=", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return url;
            }

            char separator = url.Contains("?") ? '&' : '?';
            return $"{url}{separator}{name}={Uri.EscapeDataString(value)}";
        }

        private static void SetModelListAuthHeader(UnityWebRequest request, AIProvider provider, string apiKey)
        {
            string trimmedKey = apiKey?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedKey))
            {
                return;
            }

            if (provider == AIProvider.Google)
            {
                request.SetRequestHeader("x-goog-api-key", trimmedKey);
                return;
            }

            request.SetRequestHeader("Authorization", $"Bearer {trimmedKey}");
        }

        private static List<string> BuildModelListRequestCandidates(string requestUrl, string providerFallbackUrl, AIProvider provider)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(requestUrl))
            {
                candidates.Add(requestUrl);
            }

            if (!string.IsNullOrWhiteSpace(providerFallbackUrl))
            {
                candidates.Add(providerFallbackUrl);
            }

            const string v1ModelsSuffix = "/v1/models";
            if (provider != AIProvider.Google
                && requestUrl != null
                && requestUrl.EndsWith(v1ModelsSuffix, StringComparison.OrdinalIgnoreCase))
            {
                string fallback = requestUrl.Substring(0, requestUrl.Length - v1ModelsSuffix.Length) + "/models";
                if (!string.Equals(fallback, requestUrl, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(fallback);
                }
            }

            return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void FetchModelsCoroutine(string url, string providerFallbackUrl, string apiKey, AIProvider provider, string cacheKey, Action<List<string>> callback)
        {
            // 绾喕绻欰IChatServiceAsync鐎圭偘绶ョ€涙ê婀?
            var service = AIChatServiceAsync.Instance;
            List<string> candidateUrls = BuildModelListRequestCandidates(url, providerFallbackUrl, provider);
            
            Task.Run(() =>
            {
                List<string> models = null;
                try
                {
                    foreach (string candidateUrl in candidateUrls)
                    {
                        using (var request = new UnityWebRequest(candidateUrl, "GET"))
                        {
                            request.downloadHandler = new DownloadHandlerBuffer();
                            SetModelListAuthHeader(request, provider, apiKey);
                            request.timeout = 10;

                            var operation = request.SendWebRequest();
                        
                            while (!operation.isDone)
                            {
                                System.Threading.Thread.Sleep(50);
                            }

                            if (request.result == UnityWebRequest.Result.Success)
                            {
                                models = ParseModelsFromResponse(request.downloadHandler.text, provider);
                                ModelCache[cacheKey] = models;
                                break;
                            }

                            string body = request.downloadHandler?.text ?? string.Empty;
                            if (body.Length > 240)
                            {
                                body = body.Substring(0, 240) + "...";
                            }

                            Log.Warning($"[RimChat] Failed to fetch models: url={candidateUrl}, HTTP {request.responseCode}, error={request.error}, body={body}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimChat] Failed to fetch models: {ex.Message}");
                }

                // Marshal callback back to Unity main thread before touching UI.
                service.ExecuteOnMainThread(() => callback(models));
            });
        }

        private List<string> ParseModelsFromResponse(string json, AIProvider provider)
        {
            try
            {
                if (provider == AIProvider.Google)
                {
                    return ParseGoogleModelsFromResponse(json);
                }

                return ParseOpenAIModelsFromResponse(json);
            }
            catch
            {
                return new List<string>();
            }
        }

        private List<string> ParseOpenAIModelsFromResponse(string json)
        {
            var response = JsonUtility.FromJson<OpenAIModelListResponse>(json);
            List<string> models = response?.data
                ?.Select(model => model?.id)
                .Where(modelId => !string.IsNullOrWhiteSpace(modelId))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(modelId => modelId, StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<string>();

            if (models.Count == 0)
            {
                models = ExtractModelIdsFromJson(json);
            }

            return models;
        }

        private List<string> ParseGoogleModelsFromResponse(string json)
        {
            var response = JsonUtility.FromJson<GoogleModelListResponse>(json);
            if (response?.models == null)
            {
                return new List<string>();
            }

            return response.models
                .Where(SupportsGenerateContent)
                .Select(model => NormalizeGoogleModelName(model.name))
                .Where(modelName => !string.IsNullOrWhiteSpace(modelName))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(modelName => modelName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool SupportsGenerateContent(GoogleModelInfo model)
        {
            if (model?.supportedGenerationMethods == null || model.supportedGenerationMethods.Length == 0)
            {
                return true;
            }

            return model.supportedGenerationMethods.Any(method =>
                string.Equals(method, "generateContent", StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeGoogleModelName(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
            {
                return string.Empty;
            }

            const string prefix = "models/";
            return modelName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? modelName.Substring(prefix.Length)
                : modelName;
        }

        private static List<string> ExtractModelIdsFromJson(string json)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return results;
            }

            if (json.IndexOf("\"data\"", StringComparison.OrdinalIgnoreCase) < 0 &&
                json.IndexOf("\"models\"", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return results;
            }

            int index = 0;
            const string token = "\"id\"";
            while ((index = json.IndexOf(token, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                index = json.IndexOf(':', index);
                if (index < 0) break;
                index++;

                while (index < json.Length && char.IsWhiteSpace(json[index])) index++;
                if (index >= json.Length || json[index] != '\"') continue;

                int start = ++index;
                int end = json.IndexOf('\"', start);
                if (end < 0) break;
                string id = json.Substring(start, end - start);
                if (!string.IsNullOrWhiteSpace(id))
                {
                    results.Add(id);
                }
                index = end + 1;
            }

            return results
                .Distinct(StringComparer.Ordinal)
                .OrderBy(modelId => modelId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        [Serializable]
        private class OpenAIModelListResponse
        {
            public OpenAIModelInfo[] data = Array.Empty<OpenAIModelInfo>();
        }

        [Serializable]
        private class OpenAIModelInfo
        {
            public string id = string.Empty;
        }

        [Serializable]
        private class GoogleModelListResponse
        {
            public GoogleModelInfo[] models = Array.Empty<GoogleModelInfo>();
        }

        [Serializable]
        private class GoogleModelInfo
        {
            public string name = string.Empty;
            public string[] supportedGenerationMethods = Array.Empty<string>();
        }

        private void DrawLocalProviderSection(Listing_Standard listing)
        {
            listing.Label("RimChat_LocalProviderConfiguration".Translate());
            listing.Gap(6f);

            Rect rowRect = listing.GetRect(24f);
            float x = rowRect.x;
            float y = rowRect.y;
            float height = rowRect.height;

            Rect baseUrlLabelRect = new Rect(x, y, 80f, height);
            Widgets.Label(baseUrlLabelRect, "RimChat_BaseUrlLabel".Translate());
            x += 85f;

            Rect urlRect = new Rect(x, y, 250f, height);
            LocalConfig.BaseUrl = ApiConfig.NormalizeUrl(Widgets.TextField(urlRect, LocalConfig.BaseUrl));
            x += 285f;

            Rect modelLabelRect = new Rect(x, y, 70f, height);
            Widgets.Label(modelLabelRect, "RimChat_ModelLabel".Translate());
            x += 75f;

            Rect modelRect = new Rect(x, y, 200f, height);
            LocalConfig.ModelName = Widgets.TextField(modelRect, LocalConfig.ModelName);
        }

        private void DrawConnectionTestButton(Listing_Standard listing)
        {
            DrawApiTestButtonsInSingleRow(listing);
            listing.Gap(2f);
            DrawConnectivityTestStatus(listing);
            listing.Gap(2f);
            DrawUsabilityTestResult(listing);
        }

        private void DrawLatestDialogueTokenUsage(Listing_Standard listing)
        {
            if (!AIChatServiceAsync.TryGetLatestDialogueTokenUsage(out DialogueTokenUsageSnapshot snapshot) || snapshot == null)
            {
                listing.Label("RimChat_LastDialogueTokenUsageNoData".Translate());
                return;
            }

            string level = GetDialogueTokenLevelLabel(snapshot.TotalTokens);
            string estimateSuffix = snapshot.IsEstimated
                ? " " + "RimChat_LastDialogueTokenUsageEstimated".Translate()
                : string.Empty;

            string text = "RimChat_LastDialogueTokenUsageLine".Translate(
                snapshot.TotalTokens.ToString(),
                level,
                estimateSuffix);
            listing.Label(text);
        }

        private string GetDialogueTokenLevelLabel(int totalTokens)
        {
            if (totalTokens <= DialogueTokenLowThreshold)
            {
                return "RimChat_TokenLevelLow".Translate();
            }

            if (totalTokens <= DialogueTokenMediumThreshold)
            {
                return "RimChat_TokenLevelMedium".Translate();
            }

            return "RimChat_TokenLevelHigh".Translate();
        }

        private Color GetStatusColor()
        {
            if (connectionTestStatus.Contains("RimChat_ConnectionSuccess".Translate().ToString()))
                return Color.green;
            if (connectionTestStatus.Contains("RimChat_ConnectionFailed".Translate().ToString()))
                return Color.red;
            return Color.yellow;
        }

        private void TestConnection()
        {
            isTestingConnection = true;
            connectionTestStatus = "RimChat_ConnectionTesting".Translate();

            LongEventHandler.QueueLongEvent(() =>
            {
                TestConnectionSync();
            }, "RimChat_TestingConnection".Translate(), false, null);
        }

        private void TestConnectionSync()
        {
            try
            {
                if (UseCloudProviders)
                {
                    var validConfig = CloudConfigs.FirstOrDefault(c => c.IsValid());
                    if (validConfig == null)
                    {
                        connectionTestStatus = "RimChat_ConnectionFailed".Translate("RimChat_NoValidConfig".Translate());
                        return;
                    }
                    TestCloudConnection(validConfig);
                }
                else
                {
                    TestLocalConnection();
                }
            }
            catch (Exception ex)
            {
                connectionTestStatus = "RimChat_ConnectionFailed".Translate(ex.Message);
            }
            finally
            {
                isTestingConnection = false;
            }
        }

        private void TestCloudConnection(ApiConfig config)
        {
            string runtimeHint = string.Empty;
            string chatFallbackUrl = string.Empty;
            bool allowChatFallback = false;
            string url = ResolveCloudModelListTestUrl(config, out runtimeHint, out chatFallbackUrl, out allowChatFallback);

            if (string.IsNullOrWhiteSpace(url))
            {
                string failed = "RimChat_ConnectionFailed".Translate("RimChat_ErrorEmptyUrl".Translate());
                connectionTestStatus = ComposeConnectionStatus(failed, runtimeHint, false);
                return;
            }

            CloudProbeResult modelsProbe = ProbeCloudEndpoint(config, url, "GET", null);
            if (modelsProbe.IsSuccess)
            {
                connectionTestStatus = ComposeConnectionStatus("RimChat_ConnectionSuccess".Translate(), runtimeHint, false);
                return;
            }

            if (modelsProbe.IsAuthError)
            {
                string failed = "RimChat_ConnectionFailed".Translate("RimChat_InvalidAPIKey".Translate());
                connectionTestStatus = ComposeConnectionStatus(failed, runtimeHint, false);
                return;
            }

            bool usedChatFallback = false;
            CloudProbeResult chatProbe = default(CloudProbeResult);
            if (allowChatFallback && !string.IsNullOrWhiteSpace(chatFallbackUrl))
            {
                chatProbe = ProbeCloudEndpoint(config, chatFallbackUrl, "POST", BuildConnectionTestChatBody(config));
                if (chatProbe.IsAuthError)
                {
                    string failed = "RimChat_ConnectionFailed".Translate("RimChat_InvalidAPIKey".Translate());
                    connectionTestStatus = ComposeConnectionStatus(failed, runtimeHint, false);
                    return;
                }

                if (chatProbe.IsChatFallbackReachable)
                {
                    usedChatFallback = true;
                    connectionTestStatus = ComposeConnectionStatus("RimChat_ConnectionSuccess".Translate(), runtimeHint, true);
                    return;
                }
            }

            CloudProbeResult failedProbe = chatProbe.HasResponseCode ? chatProbe : modelsProbe;
            string reason = failedProbe.HasResponseCode
                ? $"HTTP {failedProbe.ResponseCode}"
                : failedProbe.Error;
            if (string.IsNullOrWhiteSpace(reason))
            {
                reason = "Unknown error";
            }

            string status = "RimChat_ConnectionFailed".Translate(reason);
            connectionTestStatus = ComposeConnectionStatus(status, runtimeHint, usedChatFallback);
        }

        private string ResolveCloudModelListTestUrl(
            ApiConfig config,
            out string runtimeHint,
            out string chatFallbackUrl,
            out bool allowChatFallback)
        {
            runtimeHint = string.Empty;
            chatFallbackUrl = string.Empty;
            allowChatFallback = false;

            string url = config.Provider.GetListModelsUrl();
            bool allowBaseUrlOverride = config.Provider != AIProvider.DeepSeek;
            bool hasBaseUrlOverride = allowBaseUrlOverride && !string.IsNullOrEmpty(config.BaseUrl);
            if (!hasBaseUrlOverride)
            {
                return BuildModelListRequestUrl(config, url);
            }

            if (config.Provider == AIProvider.Custom && config.TryResolveCustomRuntimeEndpoints(out CustomUrlRuntimeResolution resolved))
            {
                runtimeHint = BuildCustomRuntimeHint(resolved);
                chatFallbackUrl = resolved.ChatEndpoint;
                allowChatFallback = config.CustomUrlMode == CustomUrlMode.FullEndpoint;
                return BuildModelListRequestUrl(config, resolved.ModelsEndpoint);
            }

            return BuildModelListRequestUrl(config, ApiConfig.ToModelsEndpoint(config.BaseUrl));
        }

        private static string BuildCustomRuntimeHint(CustomUrlRuntimeResolution resolution)
        {
            var segments = new List<string>();
            if (resolution.WasSiliconFlowHostMapped)
            {
                segments.Add("RimChat_CustomUrlMappedHint".Translate(resolution.ChatEndpoint));
            }

            if (resolution.HasSuspiciousBasePath)
            {
                segments.Add("RimChat_CustomUrlSuspiciousPathHint".Translate());
            }

            return string.Join(" ", segments.Where(segment => !string.IsNullOrWhiteSpace(segment)));
        }

        private static string ComposeConnectionStatus(string status, string runtimeHint, bool usedChatFallback)
        {
            var segments = new List<string>();
            if (!string.IsNullOrWhiteSpace(status))
            {
                segments.Add(status);
            }

            if (usedChatFallback)
            {
                segments.Add("RimChat_CustomUrlChatFallbackHint".Translate());
            }

            if (!string.IsNullOrWhiteSpace(runtimeHint))
            {
                segments.Add(runtimeHint);
            }

            return string.Join(" ", segments);
        }

        private static CloudProbeResult ProbeCloudEndpoint(ApiConfig config, string url, string method, string body)
        {
            using (var request = new UnityWebRequest(url, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 10;
                SetModelListAuthHeader(request, config.Provider, config.ApiKey);

                if (!string.IsNullOrEmpty(body))
                {
                    byte[] bodyRaw = Encoding.UTF8.GetBytes(body);
                    request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    request.SetRequestHeader("Content-Type", "application/json");
                }

                var operation = request.SendWebRequest();
                while (!operation.isDone) { System.Threading.Thread.Sleep(100); }

                return new CloudProbeResult
                {
                    Result = request.result,
                    ResponseCode = request.responseCode,
                    Error = request.error ?? string.Empty
                };
            }
        }

        private static string BuildConnectionTestChatBody(ApiConfig config)
        {
            string model = config.GetEffectiveModelName();
            if (string.IsNullOrWhiteSpace(model))
            {
                model = "test";
            }

            string escapedModel = model.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"{{\"model\":\"{escapedModel}\",\"messages\":[{{\"role\":\"user\",\"content\":\"ping\"}}]}}";
        }

        private struct CloudProbeResult
        {
            public UnityWebRequest.Result Result;
            public long ResponseCode;
            public string Error;

            public bool IsSuccess => Result == UnityWebRequest.Result.Success || ResponseCode == 200;
            public bool HasResponseCode => ResponseCode > 0;
            public bool IsAuthError => ResponseCode == 401 || ResponseCode == 403;
            public bool IsChatFallbackReachable => HasResponseCode && !IsAuthError && ResponseCode != 404;
        }

        private void TestLocalConnection()
        {
            string baseUrl = LocalConfig.GetNormalizedBaseUrl().TrimEnd('/');
            
            // Try Ollama endpoint first
            string testUrl = baseUrl + "/api/tags";
            bool success = TryTestUrl(testUrl, "GET", null);
            
            // If Ollama fails, try Player2 endpoint
            if (!success)
            {
                testUrl = baseUrl + "/v1/models";
                success = TryTestUrl(testUrl, "GET", null);
            }
            
            // If both fail, try a simple POST to chat completions endpoint
            if (!success)
            {
                testUrl = baseUrl + "/v1/chat/completions";
                success = TryTestUrl(testUrl, "POST", "{\"model\":\"test\",\"messages\":[]}");
            }
            
            if (success)
            {
                connectionTestStatus = "RimChat_ConnectionSuccess".Translate();
            }
            else
            {
                connectionTestStatus = "RimChat_ConnectionFailed".Translate("RimChat_LocalServiceNotFound".Translate());
            }
        }
        
        private bool TryTestUrl(string url, string method, string body)
        {
            try
            {
                using (var request = new UnityWebRequest(url, method))
                {
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.timeout = 5;
                    
                    if (body != null)
                    {
                        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(body);
                        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                        request.SetRequestHeader("Content-Type", "application/json");
                    }

                    var operation = request.SendWebRequest();
                    while (!operation.isDone) { System.Threading.Thread.Sleep(50); }

                    long responseCode = request.responseCode;
                    if (responseCode == 401 || responseCode == 403)
                    {
                        return false;
                    }

                    if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                    {
                        return responseCode >= 200 && responseCode < 300;
                    }

                    if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                    {
                        return responseCode > 0 && responseCode != 404;
                    }

                    return responseCode > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        #region Global Prompt Settings

        /// <summary>/// 娴犲侗rompt閺傚洣娆㈤崝鐘烘祰姒涙顓婚幓鎰仛鐠囧稄绱欐俊鍌涚亯鐠佸墽鐤嗘稉顓濊礋缁岀尨绱?
 ///</summary>
        private void LoadDefaultPromptsIfNeeded()
        {
            // 閸欘亜婀拋鍓х枂娑撹櫣鈹栭弮鏈电矤閺傚洣娆㈤崝鐘烘祰
            if (string.IsNullOrEmpty(GlobalSystemPrompt))
            {
                var promptConfig = PromptFileManager.LoadGlobalPrompt();
                if (promptConfig != null)
                {
                    if (!string.IsNullOrEmpty(promptConfig.SystemPrompt))
                    {
                        GlobalSystemPrompt = promptConfig.SystemPrompt;
                        Log.Message("[RimChat] Loaded global system prompt from file.");
                    }
                    if (!string.IsNullOrEmpty(promptConfig.DialoguePrompt))
                    {
                        GlobalDialoguePrompt = promptConfig.DialoguePrompt;
                        Log.Message("[RimChat] Loaded global dialogue prompt from file.");
                    }
                }
            }
        }

        /// <summary>/// 娣囨繂鐡ㄩ崗銊ョ湰閹绘劗銇氱拠宥呭煂閺傚洣娆?
 ///</summary>
        private void SaveGlobalPromptsToFile()
        {
            var config = new PromptConfig
            {
                Name = "Global",
                SystemPrompt = GlobalSystemPrompt,
                DialoguePrompt = GlobalDialoguePrompt,
                Enabled = true,
                FactionId = ""
            };
            PromptFileManager.SaveGlobalPrompt(config);
        }

        /// <summary>/// 缂佹ê鍩楅崗銊ョ湰閹绘劗銇氱拠宥堫啎缂冾喖灏崺? ///</summary>
        private void DrawGlobalPromptSettingsSection(Listing_Standard listing)
        {
            listing.Label("RimChat_GlobalPromptSettings".Translate());
            listing.GapLine();

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect descRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(descRect, "RimChat_GlobalPromptSettingsDesc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            listing.Gap(5f);

            // 娴犲侗rompt閺傚洣娆㈤崝鐘烘祰姒涙顓婚幓鎰仛鐠囧稄绱欐俊鍌涚亯鐠佸墽鐤嗘稉顓濊礋缁岀尨绱?
            LoadDefaultPromptsIfNeeded();

            if (string.IsNullOrEmpty(editingSystemPrompt) && !string.IsNullOrEmpty(GlobalSystemPrompt))
            {
                editingSystemPrompt = GlobalSystemPrompt;
            }
            if (string.IsNullOrEmpty(editingDialoguePrompt) && !string.IsNullOrEmpty(GlobalDialoguePrompt))
            {
                editingDialoguePrompt = GlobalDialoguePrompt;
            }

            if (systemPromptTextArea == null)
            {
                systemPromptTextArea = new EnhancedTextArea("SystemPromptTextArea", int.MaxValue);
                systemPromptTextArea.Text = editingSystemPrompt;
                systemPromptTextArea.OnTextChanged += (newText) => editingSystemPrompt = newText;
            }
            if (dialoguePromptTextArea == null)
            {
                dialoguePromptTextArea = new EnhancedTextArea("DialoguePromptTextArea", MaxDialoguePromptLength);
                dialoguePromptTextArea.Text = editingDialoguePrompt;
                dialoguePromptTextArea.OnTextChanged += (newText) => editingDialoguePrompt = newText;
            }

            systemPromptTextArea.MaxLength = int.MaxValue;
            dialoguePromptTextArea.MaxLength = MaxDialoguePromptLength;

            Rect sysLabelRect = listing.GetRect(24f);
            Widgets.Label(sysLabelRect, "RimChat_SystemPromptLabel".Translate());
            if (Mouse.IsOver(sysLabelRect))
            {
                TooltipHandler.TipRegion(sysLabelRect, "RimChat_SystemPromptDesc".Translate());
            }

            float sysTextHeight = 120f;
            Rect sysTextRect = listing.GetRect(sysTextHeight);
            systemPromptTextArea.Draw(sysTextRect);
            editingSystemPrompt = systemPromptTextArea.Text;

            listing.Gap(5f);

            Rect dlgLabelRect = listing.GetRect(24f);
            Widgets.Label(dlgLabelRect, "RimChat_DialoguePromptLabel".Translate());
            if (Mouse.IsOver(dlgLabelRect))
            {
                TooltipHandler.TipRegion(dlgLabelRect, "RimChat_DialoguePromptDesc".Translate());
            }

            float dlgTextHeight = 120f;
            Rect dlgTextRect = listing.GetRect(dlgTextHeight);
            dialoguePromptTextArea.Draw(dlgTextRect);
            editingDialoguePrompt = dialoguePromptTextArea.Text;

            listing.Gap(10f);

            // 娣囨繂鐡ㄩ幐澶愭尦
            Rect saveRect = listing.GetRect(28f);
            bool canSave = !systemPromptTextArea.HasExceededLimit && !dialoguePromptTextArea.HasExceededLimit;
            GUI.color = canSave ? new Color(0.3f, 0.8f, 0.3f) : Color.gray;
            if (Widgets.ButtonText(saveRect, "RimChat_SavePrompt".Translate()) && canSave)
            {
                GlobalSystemPrompt = editingSystemPrompt;
                GlobalDialoguePrompt = editingDialoguePrompt;
                // 閸氬本妞傛穱婵嗙摠閸掔増鏋冩禒? SaveGlobalPromptsToFile();
                Messages.Message("RimChat_PromptSaved".Translate(), MessageTypeDefOf.NeutralEvent, false);
            }
            GUI.color = Color.white;
        }

        /// <summary>/// 缂佹ê鍩楅幓鎰仛鐠囧秹鏆辨惔锕傛閸掓儼顔曠純顔煎隘閸? ///</summary>
        private void DrawPromptLengthLimitSection(Listing_Standard listing)
        {
            listing.Label("RimChat_PromptLengthLimit".Translate());
            listing.GapLine();

            listing.Label("RimChat_MaxSystemPromptLength".Translate(MaxSystemPromptLength));
            MaxSystemPromptLength = (int)listing.Slider(MaxSystemPromptLength, 500, 4000);

            listing.Label("RimChat_MaxDialoguePromptLength".Translate(MaxDialoguePromptLength));
            MaxDialoguePromptLength = (int)listing.Slider(MaxDialoguePromptLength, 500, 4000);

            listing.Label("RimChat_MaxPromptLength".Translate(MaxFactionPromptLength));
            MaxFactionPromptLength = (int)listing.Slider(MaxFactionPromptLength, 1000, 8000);

            // 鐠€锕€鎲￠幓鎰仛
            Text.Font = GameFont.Tiny;
            GUI.color = Color.yellow;
            Rect warningRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(warningRect, "RimChat_PromptLengthWarning".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        #endregion

        #region Faction Prompt Settings (New)

        /// <summary>/// 缂佹ê鍩楀ú鍓ч兇Prompt鐠佸墽鐤嗛崠鍝勭厵
 ///</summary>
        private void DrawFactionPromptSettingsSection(Listing_Standard listing)
        {
            listing.Label("RimChat_FactionPromptSettings".Translate());
            listing.GapLine();

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Rect descRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(descRect, "RimChat_FactionPromptSettingsDesc".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            listing.Gap(5f);

            // 闁板秶鐤嗛弬鍥︽鐠侯垰绶為弰鍓с仛
            string configPath = FactionPromptManager.Instance.ConfigFilePath;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            Rect pathRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(pathRect, $"Config: {configPath}");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            listing.Gap(5f);

            // 閺勫墽銇氶梾鎰濞插墽閮撮柅澶愩€?
            Rect toggleRect = listing.GetRect(24f);
            Widgets.CheckboxLabeled(toggleRect, "RimChat_ShowHiddenFactions".Translate(), ref showHiddenFactions);
            listing.Gap(10f);

            float totalHeight = 420f;
            Rect mainRect = listing.GetRect(totalHeight);

            float leftWidth = mainRect.width * 0.38f;
            float rightWidth = mainRect.width * 0.6f - 10f;

            Rect leftRect = new Rect(mainRect.x, mainRect.y, leftWidth, totalHeight);
            Rect rightRect = new Rect(mainRect.x + leftWidth + 10f, mainRect.y, rightWidth, totalHeight);

            // 缂佹ê鍩楀ú鍓ч兇閸掓銆?
            DrawFactionPromptList(leftRect);

            DrawFactionPromptEditor(rightRect);

            listing.Gap(10f);

            // 鎼存洟鍎撮幙宥勭稊閹稿鎸?
            DrawFactionPromptActionButtons(listing);
        }

        /// <summary>/// 缂佹ê鍩楀ú鍓ч兇Prompt閸掓銆?
 ///</summary>
        private void DrawFactionPromptList(Rect rect)
        {
            Rect innerRect = rect.ContractedBy(4f);

            // 閺嶅洭顣?
            Text.Font = GameFont.Small;
            Rect titleRect = new Rect(innerRect.x, innerRect.y, innerRect.width, 24f);
            Widgets.Label(titleRect, "RimChat_FactionList".Translate());

            float listY = innerRect.y + 28f;
            Rect listRect = new Rect(innerRect.x, listY, innerRect.width, innerRect.height - 28f);

            var configs = FactionPromptManager.Instance.AllConfigs;

            if (configs.Count == 0)
            {
                GUI.color = Color.gray;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(listRect, "RimChat_NoFactionConfigs".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            float rowHeight = 30f;
            float totalListHeight = Mathf.Max(configs.Count * rowHeight, listRect.height);
            Rect viewRect = new Rect(0, 0, listRect.width - 16f, totalListHeight);

            Widgets.BeginScrollView(listRect, ref factionListScrollPosition, viewRect);

            float y = 0f;
            for (int i = 0; i < configs.Count; i++)
            {
                var config = configs[i];
                if (!showHiddenFactions && IsHiddenFaction(config.FactionDefName))
                {
                    continue;
                }

                Rect rowRect = new Rect(0, y, viewRect.width, rowHeight);

                // 闁鑵戞妯瑰瘨
                if (selectedFactionDefName == config.FactionDefName)
                {
                    Widgets.DrawHighlightSelected(rowRect);
                }
                else if (i % 2 == 0)
                {
                    Widgets.DrawLightHighlight(rowRect);
                }

                // 閻愮懓鍤柅澶嬪
                if (Widgets.ButtonInvisible(rowRect))
                {
                    selectedFactionDefName = config.FactionDefName;
                    editingCustomPrompt = config.CustomPrompt ?? "";
                    editingUseCustomPrompt = config.UseCustomPrompt;
                }

                float xOffset = 4f;

                // 閼奉亜鐣炬稊澶嬪瘹缁€鍝勬珤
                if (config.UseCustomPrompt)
                {
                    Rect customRect = new Rect(xOffset, y + 8f, 14f, 14f);
                    GUI.color = new Color(0.3f, 0.8f, 0.3f);
                    Widgets.DrawBoxSolid(customRect, GUI.color);
                    GUI.color = Color.white;
                    xOffset += 20f;
                }

                // 濞插墽閮撮崥宥囆?
                Rect nameRect = new Rect(xOffset, y, viewRect.width - xOffset - 10f, rowHeight);
                Text.Anchor = TextAnchor.MiddleLeft;
                string displayName = string.IsNullOrEmpty(config.DisplayName) ? config.FactionDefName : config.DisplayName;
                Widgets.Label(nameRect, displayName.Truncate(nameRect.width));
                Text.Anchor = TextAnchor.UpperLeft;

                y += rowHeight;
            }

            Widgets.EndScrollView();
        }

        /// <summary>/// 閸掋倖鏌囬弰顖氭儊娑撴椽娈ｉ挊蹇旀烦缁? ///</summary>
        private bool IsHiddenFaction(string factionDefName)
        {
            var def = DefDatabase<FactionDef>.GetNamedSilentFail(factionDefName);
            if (def == null) return false;
            try
            {
                var hiddenField = typeof(FactionDef).GetField("hidden");
                if (hiddenField != null)
                {
                    return (bool)hiddenField.GetValue(def);
                }
            }
            catch { }
            return false;
        }

        /// <summary>/// 缂佹ê鍩楀ú鍓ч兇Prompt缂傛牞绶崳? ///</summary>
        private void DrawFactionPromptEditor(Rect rect)
        {
            Rect innerRect = rect.ContractedBy(6f);

            if (string.IsNullOrEmpty(selectedFactionDefName))
            {
                GUI.color = Color.gray;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(innerRect, "RimChat_SelectFactionForPrompt".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            var config = FactionPromptManager.Instance.GetConfig(selectedFactionDefName);
            if (config == null)
            {
                GUI.color = Color.gray;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(innerRect, "RimChat_FactionConfigNotFound".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            float y = innerRect.y;

            // 濞插墽閮撮崥宥囆為弽鍥暯
            Text.Font = GameFont.Medium;
            Rect headerRect = new Rect(innerRect.x, y, innerRect.width, 28f);
            string displayName = string.IsNullOrEmpty(config.DisplayName) ? config.FactionDefName : config.DisplayName;
            Widgets.Label(headerRect, displayName);
            Text.Font = GameFont.Small;
            y += 32f;

            // 娴ｈ法鏁ら懛顏勭暰娑斿rompt闁銆?
            Rect checkboxRect = new Rect(innerRect.x, y, innerRect.width, 24f);
            bool prevUseCustom = editingUseCustomPrompt;
            Widgets.CheckboxLabeled(checkboxRect, "RimChat_UseCustomPrompt".Translate(), ref editingUseCustomPrompt);
            if (prevUseCustom != editingUseCustomPrompt)
            {
                config.UseCustomPrompt = editingUseCustomPrompt;
                FactionPromptManager.Instance.UpdateConfig(config);
            }
            y += 28f;

            Rect lineRect = new Rect(innerRect.x, y, innerRect.width, 2f);
            Widgets.DrawBoxSolid(lineRect, new Color(0.3f, 0.3f, 0.3f, 0.5f));
            y += 8f;

            if (editingUseCustomPrompt)
            {
                // 缂傛牞绶懛顏勭暰娑斿rompt
                DrawCustomPromptEditor(innerRect, ref y, config);
            }
            else
            {
                // 閺勫墽銇氭妯款吇Prompt鐠囷附鍎?
                DrawDefaultPromptViewer(innerRect, ref y, config);
            }
        }

        /// <summary>/// 缂佹ê鍩楅懛顏勭暰娑斿rompt缂傛牞绶崳? ///</summary>
        private void DrawCustomPromptEditor(Rect innerRect, ref float y, FactionPromptConfig config)
        {
            if (factionPromptTextArea == null || factionPromptTextArea.Text != editingCustomPrompt)
            {
                factionPromptTextArea = new EnhancedTextArea($"FactionPrompt_{config.FactionDefName}", MaxFactionPromptLength);
                factionPromptTextArea.Text = editingCustomPrompt;
                factionPromptTextArea.OnTextChanged += (newText) => editingCustomPrompt = newText;
            }
            factionPromptTextArea.MaxLength = MaxFactionPromptLength;

            // 閺傚洦婀扮紓鏍帆閸栧搫鐓?
            float textHeight = innerRect.yMax - y - 70f;
            Rect textRect = new Rect(innerRect.x, y, innerRect.width, textHeight);
            factionPromptTextArea.Draw(textRect);
            editingCustomPrompt = factionPromptTextArea.Text;
            y += textHeight + 8f;

            float btnWidth = (innerRect.width - 20f) / 3;

            // 娣囨繂鐡ㄩ幐澶愭尦
            Rect saveRect = new Rect(innerRect.x, y, btnWidth, 28f);
            bool canSave = !factionPromptTextArea.HasExceededLimit;
            GUI.color = canSave ? new Color(0.3f, 0.8f, 0.3f) : Color.gray;
            if (Widgets.ButtonText(saveRect, "RimChat_SavePrompt".Translate()) && canSave)
            {
                config.ApplyCustomPrompt(editingCustomPrompt);
                FactionPromptManager.Instance.UpdateConfig(config);
                Messages.Message("RimChat_PromptSaved".Translate(), MessageTypeDefOf.NeutralEvent, false);
            }
            GUI.color = Color.white;

            Rect resetRect = new Rect(innerRect.x + btnWidth + 10f, y, btnWidth, 28f);
            if (Widgets.ButtonText(resetRect, "RimChat_ResetToDefault".Translate()))
            {
                ShowResetPromptConfirmation(config);
            }

            // 閺屻儳婀呮妯款吇閹稿鎸?
            Rect viewRect = new Rect(innerRect.x + btnWidth * 2 + 20f, y, btnWidth, 28f);
            if (Widgets.ButtonText(viewRect, "RimChat_ViewDefault".Translate()))
            {
                string defaultPrompt = config.BuildPromptFromTemplate();
                Find.WindowStack.Add(new Dialog_MessageBox(
                    defaultPrompt,
                    "OK".Translate(),
                    null,
                    null,
                    null,
                    null,
                    false,
                    null,
                    null,
                    WindowLayer.Dialog
                ));
            }
        }

        /// <summary>/// 缂佹ê鍩楁妯款吇 Prompt 閺屻儳婀呴崳? ///</summary>
        private void DrawDefaultPromptViewer(Rect innerRect, ref float y, FactionPromptConfig config)
        {
            float sectionHeight = 60f;

            // 閺嶇绺炬搴㈢壐
            DrawPromptFeature(innerRect, ref y, "RimChat_CoreStyle".Translate(), config.GetFieldValue("閺嶇绺炬搴㈢壐"), sectionHeight);

            // 閻劏鐦濋悧鐟扮窙
            DrawPromptFeature(innerRect, ref y, "RimChat_VocabularyFeatures".Translate(), config.GetFieldValue("閻劏鐦濋悧鐟扮窙"), sectionHeight);

            // 鐠囶厽鐨甸悧鐟扮窙
            DrawPromptFeature(innerRect, ref y, "RimChat_ToneFeatures".Translate(), config.GetFieldValue("鐠囶厽鐨甸悧鐟扮窙"), sectionHeight);

            // 閸欍儱绱￠悧鐟扮窙
            DrawPromptFeature(innerRect, ref y, "RimChat_SentenceFeatures".Translate(), config.GetFieldValue("閸欍儱绱￠悧鐟扮窙"), sectionHeight);

            // 鐞涖劏鎻粋浣哥箟
            DrawPromptFeature(innerRect, ref y, "RimChat_Taboos".Translate(), config.GetFieldValue("鐞涖劏鎻粋浣哥箟"), sectionHeight);

            float btnWidth = (innerRect.width - 20f) / 2;
            float btnY = innerRect.yMax - 34f;

            // 缂傛牞绶Ο鈩冩緲閹稿鎸?
            Rect editTemplateRect = new Rect(innerRect.x, btnY, btnWidth, 28f);
            if (Widgets.ButtonText(editTemplateRect, "RimChat_EditTemplate".Translate()))
            {
                Find.WindowStack.Add(new Dialog_FactionPromptEditor(config));
            }

            // 妫板嫯顫嶉幐澶愭尦
            Rect previewRect = new Rect(innerRect.x + btnWidth + 10f, btnY, btnWidth, 28f);
            if (Widgets.ButtonText(previewRect, "RimChat_PreviewPrompt".Translate()))
            {
                string fullPrompt = config.GetEffectivePrompt();
                Find.WindowStack.Add(new Dialog_MessageBox(
                    fullPrompt,
                    "OK",
                    null,
                    null,
                    null,
                    null,
                    false,
                    null,
                    null,
                    WindowLayer.Dialog
                ));
            }
        }

        /// <summary>/// 缂佹ê鍩桺rompt閻楃懓绶涙い? ///</summary>
        private void DrawPromptFeature(Rect innerRect, ref float y, string label, string content, float height)
        {
            // 閺嶅洨顒?
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Rect labelRect = new Rect(innerRect.x, y, innerRect.width, Text.LineHeight);
            Widgets.Label(labelRect, label);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += Text.LineHeight + 2f;

            Rect contentRect = new Rect(innerRect.x, y, innerRect.width, height);
            Widgets.DrawBoxSolid(contentRect, new Color(0.1f, 0.1f, 0.1f, 0.3f));

            GUI.color = new Color(0.9f, 0.9f, 0.9f);
            Text.Font = GameFont.Tiny;
            Rect textRect = contentRect.ContractedBy(4f);
            Widgets.Label(textRect, content ?? "");
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            y += height + 6f;
        }

        /// <summary>/// 閺勫墽銇氶柌宥囩枂Prompt绾喛顓荤€电鐦藉? ///</summary>
        private void ShowResetPromptConfirmation(FactionPromptConfig config)
        {
            Dialog_MessageBox dialog = Dialog_MessageBox.CreateConfirmation(
                "RimChat_ResetPromptConfirm".Translate(config.DisplayName),
                () =>
                {
                    config.ResetToDefault();
                    editingCustomPrompt = "";
                    editingUseCustomPrompt = false;
                    FactionPromptManager.Instance.UpdateConfig(config);
                    Messages.Message("RimChat_PromptReset".Translate(), MessageTypeDefOf.NeutralEvent, false);
                },
                true,
                "RimChat_ResetConfirmTitle".Translate()
            );
            Find.WindowStack.Add(dialog);
        }

        /// <summary>/// 缂佹ê鍩楀ú鍓ч兇Prompt閹垮秳缍旈幐澶愭尦
 ///</summary>
        private void DrawFactionPromptActionButtons(Listing_Standard listing)
        {
            Rect buttonRowRect = listing.GetRect(28f);
            float btnWidth = (buttonRowRect.width - 20f) / 3;

            // 鐎电厧鍤柊宥囩枂閹稿鎸?
            Rect exportRect = new Rect(buttonRowRect.x, buttonRowRect.y, btnWidth, buttonRowRect.height);
            if (Widgets.ButtonText(exportRect, "RimChat_ExportPrompts".Translate()))
            {
                ShowExportPromptsDialog();
            }

            // 鐎电厧鍙嗛柊宥囩枂閹稿鎸?
            Rect importRect = new Rect(buttonRowRect.x + btnWidth + 10f, buttonRowRect.y, btnWidth, buttonRowRect.height);
            if (Widgets.ButtonText(importRect, "RimChat_ImportPrompts".Translate()))
            {
                ShowImportPromptsDialog();
            }

            Rect resetAllRect = new Rect(buttonRowRect.x + btnWidth * 2 + 20f, buttonRowRect.y, btnWidth, buttonRowRect.height);
            GUI.color = new Color(1f, 0.6f, 0.6f);
            if (Widgets.ButtonText(resetAllRect, "RimChat_ResetAllPrompts".Translate()))
            {
                ShowResetAllPromptsConfirmation();
            }
            GUI.color = Color.white;
        }

        /// <summary>/// 閺勫墽銇氱€电厧鍤璓rompts鐎电鐦藉? ///</summary>
        private void ShowExportPromptsDialog()
        {
            string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "RimChat_Prompts.json");
            Find.WindowStack.Add(new Dialog_SaveFile(defaultPath, (path) =>
            {
                if (FactionPromptManager.Instance.ExportConfigs(path))
                {
                    Messages.Message("RimChat_ExportSuccess".Translate(path), MessageTypeDefOf.NeutralEvent, false);
                }
                else
                {
                    Messages.Message("RimChat_ExportFailed".Translate(), MessageTypeDefOf.NegativeEvent, false);
                }
            }));
        }

        /// <summary>/// 閺勫墽銇氱€电厧鍙哖rompts鐎电鐦藉? ///</summary>
        private void ShowImportPromptsDialog()
        {
            string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "RimChat_Prompts.json");
            Find.WindowStack.Add(new Dialog_LoadFile(defaultPath, (path) =>
            {
                if (FactionPromptManager.Instance.ImportConfigs(path))
                {
                    if (!string.IsNullOrEmpty(selectedFactionDefName))
                    {
                        var config = FactionPromptManager.Instance.GetConfig(selectedFactionDefName);
                        if (config != null)
                        {
                            editingCustomPrompt = config.CustomPrompt ?? "";
                            editingUseCustomPrompt = config.UseCustomPrompt;
                        }
                    }
                    Messages.Message("RimChat_ImportSuccess".Translate(), MessageTypeDefOf.NeutralEvent, false);
                }
                else
                {
                    Messages.Message("RimChat_ImportFailed".Translate(), MessageTypeDefOf.NegativeEvent, false);
                }
            }));
        }

        /// <summary>/// 閺勫墽銇氶柌宥囩枂閹碘偓閺堝rompts绾喛顓荤€电鐦藉? ///</summary>
        private void ShowResetAllPromptsConfirmation()
        {
            Dialog_MessageBox dialog = Dialog_MessageBox.CreateConfirmation(
                "RimChat_ResetAllPromptsConfirm".Translate(),
                () =>
                {
                    FactionPromptManager.Instance.ResetAllConfigs();
                    editingCustomPrompt = "";
                    editingUseCustomPrompt = false;
                    selectedFactionDefName = null;
                    Messages.Message("RimChat_AllPromptsReset".Translate(), MessageTypeDefOf.NeutralEvent, false);
                },
                true,
                "RimChat_ResetConfirmTitle".Translate()
            );
            Find.WindowStack.Add(dialog);
        }

        /// <summary>/// 娴兼壆鐣籘oken閺佷即鍣?
 ///</summary>
        private int EstimateTokenCount(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            // 缁鏆愭导鎵暬閿涙矮鑵戦懟杈ㄦ瀮濞ｅ嘲鎮庣痪?鐎涙顑?Token
            return text.Length / 4;
        }

        internal bool IsThoughtChainEnabledForPromptChannel(string promptChannel)
        {
            EnsureThoughtChainChannelMapReady();
            string normalized = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            PromptChannelToggleConfig entry = ThoughtChainByChannel?.FirstOrDefault(item =>
                item != null && string.Equals(item.PromptChannel, normalized, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
            {
                return entry.Enabled;
            }

            return GetThoughtChainDefaultForChannel(normalized);
        }

        internal void SetThoughtChainEnabledForPromptChannel(string promptChannel, bool enabled)
        {
            EnsureThoughtChainChannelMapReady();
            string normalized = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            PromptChannelToggleConfig entry = ThoughtChainByChannel?.FirstOrDefault(item =>
                item != null && string.Equals(item.PromptChannel, normalized, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                ThoughtChainByChannel ??= new List<PromptChannelToggleConfig>();
                ThoughtChainByChannel.Add(new PromptChannelToggleConfig
                {
                    PromptChannel = normalized,
                    Enabled = enabled
                });
                return;
            }

            entry.Enabled = enabled;
        }

        internal void ResetThoughtChainChannelDefaults()
        {
            ThoughtChainByChannel = GetThoughtChainDefaultEntries();
        }

        internal List<PromptChannelToggleConfig> GetThoughtChainChannelTogglesSnapshot()
        {
            EnsureThoughtChainChannelMapReady();
            return ThoughtChainByChannel
                .Where(item => item != null)
                .Select(item => item.Clone())
                .OrderBy(item => item.PromptChannel, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void EnsureThoughtChainChannelMapReady()
        {
            ThoughtChainByChannel ??= new List<PromptChannelToggleConfig>();
            if (ThoughtChainByChannel.Count == 0)
            {
                ThoughtChainByChannel = GetThoughtChainDefaultEntries();
                return;
            }

            var merged = new Dictionary<string, PromptChannelToggleConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (PromptChannelToggleConfig item in ThoughtChainByChannel)
            {
                if (item == null)
                {
                    continue;
                }

                string channel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(item.PromptChannel);
                if (!merged.ContainsKey(channel))
                {
                    merged[channel] = new PromptChannelToggleConfig
                    {
                        PromptChannel = channel,
                        Enabled = item.Enabled
                    };
                }
            }

            foreach (string channel in GetThoughtChainSupportedChannels())
            {
                if (!merged.ContainsKey(channel))
                {
                    merged[channel] = new PromptChannelToggleConfig
                    {
                        PromptChannel = channel,
                        Enabled = GetThoughtChainDefaultForChannel(channel)
                    };
                }
            }

            ThoughtChainByChannel = merged.Values
                .OrderBy(item => item.PromptChannel, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void MigrateLegacyThoughtChainToggleOnce(bool legacyEnableThoughtChainNode)
        {
            if (ThoughtChainByChannel != null && ThoughtChainByChannel.Count > 0)
            {
                return;
            }

            ThoughtChainByChannel = GetThoughtChainDefaultEntries();
            if (!legacyEnableThoughtChainNode)
            {
                for (int i = 0; i < ThoughtChainByChannel.Count; i++)
                {
                    ThoughtChainByChannel[i].Enabled = false;
                }
            }
        }

        private static List<PromptChannelToggleConfig> GetThoughtChainDefaultEntries()
        {
            var entries = new List<PromptChannelToggleConfig>();
            foreach (string channel in GetThoughtChainSupportedChannels())
            {
                entries.Add(new PromptChannelToggleConfig
                {
                    PromptChannel = channel,
                    Enabled = GetThoughtChainDefaultForChannel(channel)
                });
            }

            return entries;
        }

        private static IEnumerable<string> GetThoughtChainSupportedChannels()
        {
            yield return RimTalkPromptEntryChannelCatalog.Any;
            yield return RimTalkPromptEntryChannelCatalog.DiplomacyDialogue;
            yield return RimTalkPromptEntryChannelCatalog.ProactiveDiplomacyDialogue;
            yield return RimTalkPromptEntryChannelCatalog.RpgDialogue;
            yield return RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue;
            yield return RimTalkPromptEntryChannelCatalog.DiplomacyStrategy;
            yield return RimTalkPromptEntryChannelCatalog.SocialCirclePost;
            yield return RimTalkPromptEntryChannelCatalog.PersonaBootstrap;
            yield return RimTalkPromptEntryChannelCatalog.SummaryGeneration;
            yield return RimTalkPromptEntryChannelCatalog.RpgArchiveCompression;
            yield return RimTalkPromptEntryChannelCatalog.ImageGeneration;
        }

        private static bool GetThoughtChainDefaultForChannel(string promptChannel)
        {
            string normalized = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            return normalized != RimTalkPromptEntryChannelCatalog.RpgDialogue &&
                normalized != RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue;
        }

        #endregion


    }
}




