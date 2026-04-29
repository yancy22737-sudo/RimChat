using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: prompt legacy migration service and unified prompt catalog provider.
    /// Responsibility: keep PromptUnifiedCatalog as the single editable source and expose legacy section import as one-way migration only.
    /// </summary>
    public partial class RimChatSettings : ModSettings
    {
        public int RimTalkSummaryHistoryLimit = 10;
        public string RimTalkPersonaCopyTemplate = DefaultRimTalkPersonaCopyTemplate;
        public bool RimTalkAutoPushSessionSummary;
        public bool RimTalkAutoInjectCompatPreset;
        internal RimTalkPromptEntryDefaultsConfig PromptSectionCatalog = RimTalkPromptEntryDefaultsProvider.GetDefaultsSnapshot();
        internal PromptUnifiedCatalog UnifiedPromptCatalog = PromptUnifiedCatalog.CreateFallback();

        private bool _legacyEnableRimTalkPromptCompat = true;
        private int _legacyRimTalkPresetInjectionMaxEntries;
        private int _legacyRimTalkPresetInjectionMaxChars;
        private string _legacyRimTalkCompatTemplate = string.Empty;
        private bool _legacyRimTalkChannelSplitMigrated;
        private RimTalkChannelCompatConfig _legacyRimTalkDiplomacy = RimTalkChannelCompatConfig.CreateDefault();
        private RimTalkChannelCompatConfig _legacyRimTalkRpg = RimTalkChannelCompatConfig.CreateDefault();
        private bool _legacyPromptCompatImported;
        private bool _isEnsuringPromptCatalog;
        private bool _isEnsuringUnifiedPromptCatalog;
        private bool _promptUnifiedCatalogLoaded;
        private bool _promptUnifiedCatalogDirty;
        private const int UnifiedCatalogMigrationTargetVersion = 7;
        private const string RimWorldBackgroundNarrativeLead = "背景：破碎的人类文明散落在已知宇宙边缘。";
        private const string RimWorldBackgroundNarrativeText =
            "背景：破碎的人类文明散落在已知宇宙边缘。远离中央权威的边缘世界普遍无序，辽阔而危险的星球迫使幸存者自力更生。由于缺乏超光速航行与通信，各世界长期隔绝且发展失衡，原始部落、工业社会、高科技派系与近神级机器得以并存。整体基调是硬科幻与边境生存的结合，聚焦普通人在破碎世界中求生并书写自己的故事；";
        private const string RpgOutputSpecificationReferenceText = "输出规范唯一权威：见独立 `response_contract` 节点（`dialogue.response_contract_body`）。本段只做引用，不重复定义规则。";
        private const string RpgArchiveCompressionSystemRulesText =
            "RPG 归档压缩模式：你是离线归档压缩器。仅基于提供的会话文本提取事实，不增删事件，不重写因果，不加入角色扮演语气。";
        private const string RpgArchiveCompressionOutputSpecificationText =
            "输出规范：仅输出单句纯文本摘要。禁止 JSON、列表、换行、额外说明或引号包裹。";
        private static readonly string[] RpgArchiveCompressionRequiredSectionIds =
        {
            "system_rules",
            "character_persona",
            "memory_system",
            "environment_perception",
            "context",
            "mod_variables",
            "action_rules",
            "repetition_reinforcement",
            "output_specification"
        };

        public const int RimTalkSummaryHistoryMin = 1;
        public const int RimTalkSummaryHistoryMax = 30;
        public const int RimTalkPresetInjectionLimitUnlimited = 0;
        public const int RimTalkPresetInjectionMaxEntriesMin = 0;
        public const int RimTalkPresetInjectionMaxEntriesMax = 200;
        public const int RimTalkPresetInjectionMaxCharsMin = 0;
        public const int RimTalkPresetInjectionMaxCharsMax = 200000;
        public const int RimTalkCompatTemplateMaxLength = 6000;
        public const int RimTalkPersonaCopyTemplateMaxLength = 2000;

        public const string DefaultRimTalkCompatTemplate =
@"=== RIMTALK SCRIBAN COMPAT (RIMCHAT) ===
You may reference RimTalk variables/plugins directly in this section.";
        public const string DefaultRimTalkPersonaCopyTemplate = "{{ pawn.personality }}";

        internal void ExposeData_RimTalkCompat()
        {
            Scribe_Deep.Look(ref PromptSectionCatalog, "PromptSectionCatalog");
            Scribe_Deep.Look(ref UnifiedPromptCatalog, "PromptUnifiedCatalog");
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Scribe_Values.Look(ref _legacyEnableRimTalkPromptCompat, "EnableRimTalkPromptCompat", true);
                Scribe_Values.Look(ref _legacyRimTalkPresetInjectionMaxEntries, "RimTalkPresetInjectionMaxEntries", RimTalkPresetInjectionLimitUnlimited);
                Scribe_Values.Look(ref _legacyRimTalkPresetInjectionMaxChars, "RimTalkPresetInjectionMaxChars", RimTalkPresetInjectionLimitUnlimited);
                Scribe_Values.Look(ref _legacyRimTalkCompatTemplate, "RimTalkCompatTemplate", string.Empty);
                Scribe_Values.Look(ref _legacyRimTalkChannelSplitMigrated, "RimTalkChannelSplitMigrated", false);
                Scribe_Deep.Look(ref _legacyRimTalkDiplomacy, "RimTalkDiplomacy");
                Scribe_Deep.Look(ref _legacyRimTalkRpg, "RimTalkRpg");
            }

            EnsurePromptSectionCatalogReady();
            ClampRimTalkCompatSettings();
        }

        public bool IsAnyRimTalkPromptCompatEnabled()
        {
            return false;
        }

        public bool IsRimTalkPromptCompatEnabled(string channel)
        {
            return false;
        }

        internal RimTalkChannelCompatConfig GetRimTalkChannelConfig(RimTalkPromptChannel channel)
        {
            EnsurePromptSectionCatalogReady();
            return PromptLegacyCompatMigration.CreateLegacyAdapterFromPromptSections(PromptSectionCatalog, channel);
        }

        internal RimTalkChannelCompatConfig GetRimTalkChannelConfigClone(RimTalkPromptChannel channel)
        {
            return GetRimTalkChannelConfig(channel).Clone();
        }

        internal void SetRimTalkChannelConfig(RimTalkPromptChannel channel, RimTalkChannelCompatConfig config)
        {
            EnsurePromptSectionCatalogReady();
            string sourceId = channel == RimTalkPromptChannel.Diplomacy ? "settings.diplomacy" : "settings.rpg";
            PromptSectionCatalog = PromptLegacyCompatMigration.ApplyLegacyAdapterToPromptSections(
                PromptSectionCatalog,
                config,
                channel,
                sourceId);
            ClampRimTalkCompatSettings();
            SyncWorkbenchEditingChannelConfig(channel, GetRimTalkChannelConfig(channel));
        }

        public int GetRimTalkSummaryHistoryLimitClamped()
        {
            return Mathf.Clamp(RimTalkSummaryHistoryLimit, RimTalkSummaryHistoryMin, RimTalkSummaryHistoryMax);
        }

        public int GetRimTalkPresetInjectionMaxEntriesClamped(string channel)
        {
            RimTalkChannelCompatConfig config = GetRimTalkChannelConfig(ParseChannel(channel));
            return Mathf.Clamp(
                config.PresetInjectionMaxEntries,
                RimTalkPresetInjectionMaxEntriesMin,
                RimTalkPresetInjectionMaxEntriesMax);
        }

        public int GetRimTalkPresetInjectionMaxEntriesClamped()
        {
            return GetRimTalkPresetInjectionMaxEntriesClamped("rpg");
        }

        public int GetRimTalkPresetInjectionMaxCharsClamped(string channel)
        {
            RimTalkChannelCompatConfig config = GetRimTalkChannelConfig(ParseChannel(channel));
            return Mathf.Clamp(
                config.PresetInjectionMaxChars,
                RimTalkPresetInjectionMaxCharsMin,
                RimTalkPresetInjectionMaxCharsMax);
        }

        public int GetRimTalkPresetInjectionMaxCharsClamped()
        {
            return GetRimTalkPresetInjectionMaxCharsClamped("rpg");
        }

        public string GetRimTalkCompatTemplateOrDefault(string channel)
        {
            RimTalkChannelCompatConfig config = GetRimTalkChannelConfig(ParseChannel(channel));
            return config.CompatTemplate;
        }

        public string GetRimTalkCompatTemplateOrDefault()
        {
            return GetRimTalkCompatTemplateOrDefault("rpg");
        }

        public string GetRimTalkPersonaCopyTemplateOrDefault()
        {
            ClampRimTalkCompatSettings();
            return RimTalkPersonaCopyTemplate;
        }

        public bool IsRimTalkSummaryPushEnabled()
        {
            return RimTalkAutoPushSessionSummary;
        }

        public bool IsRimTalkAutoPresetSyncEnabled()
        {
            return RimTalkAutoInjectCompatPreset;
        }

        internal void EnsureRimTalkChannelMigration()
        {
            EnsurePromptSectionCatalogReady();
        }

        internal void SyncLegacyRimTalkFieldsFromRpgChannel()
        {
            PromptLegacyCompatMigration.ResetLegacyFields(this);
        }

        internal void ResetLegacyCompatLoadPayload()
        {
            _legacyEnableRimTalkPromptCompat = false;
            _legacyRimTalkPresetInjectionMaxEntries = RimTalkPresetInjectionLimitUnlimited;
            _legacyRimTalkPresetInjectionMaxChars = RimTalkPresetInjectionLimitUnlimited;
            _legacyRimTalkCompatTemplate = string.Empty;
            _legacyRimTalkChannelSplitMigrated = true;
            _legacyRimTalkDiplomacy = RimTalkChannelCompatConfig.CreateDefault();
            _legacyRimTalkRpg = RimTalkChannelCompatConfig.CreateDefault();
        }

        private void EnsurePromptSectionCatalogReady()
        {
            if (_isEnsuringPromptCatalog)
            {
                return;
            }

            _isEnsuringPromptCatalog = true;
            try
            {
                if (!_promptUnifiedCatalogLoaded || UnifiedPromptCatalog == null)
                {
                    UnifiedPromptCatalog = PromptUnifiedCatalogProvider.LoadMerged();
                    _promptUnifiedCatalogLoaded = true;
                }

                PromptSectionCatalog = PromptLegacyCompatMigration.NormalizePromptSections(PromptSectionCatalog);
                RimTalkPromptEntryDefaultsConfig.TryUpgradeLegacyAnyDefaults(PromptSectionCatalog);
                if (_legacyPromptCompatImported)
                {
                    EnsureUnifiedCatalogReady();
                    return;
                }

                PromptSectionCatalog = PromptLegacyCompatMigration.ApplyLegacyPayloadToPromptSections(
                    PromptSectionCatalog,
                    _legacyEnableRimTalkPromptCompat,
                    _legacyRimTalkPresetInjectionMaxEntries,
                    _legacyRimTalkPresetInjectionMaxChars,
                    _legacyRimTalkCompatTemplate,
                    _legacyRimTalkDiplomacy,
                    _legacyRimTalkRpg,
                    "settings");
                PromptLegacyCompatMigration.ResetLegacyFields(this);
                _legacyPromptCompatImported = true;
                EnsureUnifiedCatalogReady();
            }
            finally
            {
                _isEnsuringPromptCatalog = false;
            }
        }

        private void EnsureUnifiedCatalogReady()
        {
            if (_isEnsuringUnifiedPromptCatalog)
            {
                return;
            }

            _isEnsuringUnifiedPromptCatalog = true;
            try
            {
                bool legacyMigratedChanged = false;
                bool migrationVersionChanged = false;
                UnifiedPromptCatalog = UnifiedPromptCatalog?.Clone() ?? PromptUnifiedCatalogProvider.LoadMerged();
                if (UnifiedPromptCatalog == null)
                {
                    UnifiedPromptCatalog = PromptUnifiedCatalog.CreateFallback();
                }

                if (!UnifiedPromptCatalog.LegacyMigrated)
                {
                    // Avoid recursive settings->loadConfig->settings loops during workbench initialization.
                    PromptTemplateTextConfig templates = _systemPromptConfig?.PromptTemplates ?? new PromptTemplateTextConfig();
                    UnifiedPromptCatalog = PromptUnifiedCatalog.FromLegacy(PromptSectionCatalog, templates);
                    UnifiedPromptCatalog.LegacyMigrated = true;
                    legacyMigratedChanged = true;
                }

                PromptUnifiedCatalogNormalizeReport normalizeReport =
                    UnifiedPromptCatalog.NormalizeWithReport(PromptUnifiedCatalog.CreateFallback());
                if (UnifiedPromptCatalog.MigrationVersion < UnifiedCatalogMigrationTargetVersion)
                {
                    ApplyUnifiedCatalogOneTimeMigration(UnifiedPromptCatalog);
                    UnifiedPromptCatalog.MigrationVersion = UnifiedCatalogMigrationTargetVersion;
                    migrationVersionChanged = true;
                    normalizeReport.Merge(UnifiedPromptCatalog.NormalizeWithReport(PromptUnifiedCatalog.CreateFallback()));
                }
                bool literalDefaultsChanged = ApplyStaticLiteralNodeDefaults(UnifiedPromptCatalog);
                bool archiveCompressionSectionChanged = EnsureRpgArchiveCompressionSectionContract(UnifiedPromptCatalog);

                try
                {
                    UnifiedPromptCatalog.ValidateInvariantsOrThrow();
                }
                catch (InvalidOperationException ex)
                {
                    Log.Error($"[RimChat] Unified prompt catalog invariant violation: {ex.Message}");
                    throw;
                }

                bool requiresSave = legacyMigratedChanged ||
                    migrationVersionChanged ||
                    normalizeReport.HasStructuralChange ||
                    literalDefaultsChanged ||
                    archiveCompressionSectionChanged;
                bool hasCleanup = normalizeReport.UnknownChannelCount > 0 ||
                    normalizeReport.RemovedNodeCount > 0 ||
                    normalizeReport.RemovedLayoutCount > 0;
                if (hasCleanup)
                {
                    Log.Warning(
                        $"[RimChat] Unified prompt catalog cleanup applied: " +
                        $"unknownChannels={normalizeReport.UnknownChannelCount}, " +
                        $"removedNodes={normalizeReport.RemovedNodeCount}, " +
                        $"removedLayouts={normalizeReport.RemovedLayoutCount}.");
                }

                if (normalizeReport.FilledDefaultLayoutCount > 0)
                {
                    Log.Message(
                        $"[RimChat] Unified prompt catalog filled {normalizeReport.FilledDefaultLayoutCount} missing node layouts.");
                }

                if (legacyMigratedChanged || migrationVersionChanged)
                {
                    Log.Message(
                        $"[RimChat] Unified prompt catalog migration applied " +
                        $"(legacyMigrated={legacyMigratedChanged}, migrationVersionUpdated={migrationVersionChanged}).");
                }
                if (literalDefaultsChanged)
                {
                    Log.Message("[RimChat] Unified prompt catalog applied static literal node defaults.");
                }
                if (archiveCompressionSectionChanged)
                {
                    Log.Message("[RimChat] Unified prompt catalog repaired rpg_archive_compression section contract.");
                }

                if (requiresSave)
                {
                    PromptUnifiedCatalogProvider.SaveCustom(UnifiedPromptCatalog);
                    _promptUnifiedCatalogDirty = false;
                }

                PromptSectionCatalog = UnifiedPromptCatalog.ToSectionCatalog();
            }
            finally
            {
                _isEnsuringUnifiedPromptCatalog = false;
            }
        }

        private void ApplyUnifiedCatalogOneTimeMigration(PromptUnifiedCatalog catalog)
        {
            if (catalog == null)
            {
                return;
            }

            ApplyLegacyRpgPromptMigration(catalog);
            ApplyLegacyImageTemplateMigration(catalog);
            ApplyAnySystemRulesBackgroundMigration(catalog);
            ApplyRpgOutputProtocolMigration(catalog);
            ApplyCharacterPersonaStateAnchorMigration(catalog);
            ApplyRpgStateAnchorSelfActionMigration(catalog);
            EnsureRpgArchiveCompressionSectionContract(catalog);
        }

        internal bool EnsureRpgArchiveCompressionContractReady()
        {
            EnsurePromptSectionCatalogReady();
            if (UnifiedPromptCatalog == null)
            {
                return false;
            }

            bool changed = EnsureRpgArchiveCompressionSectionContract(UnifiedPromptCatalog);
            if (changed)
            {
                PromptSectionCatalog = UnifiedPromptCatalog.ToSectionCatalog();
                ApplyUnifiedCatalogPersistence(persistToFiles: true);
            }

            string outputSpec = UnifiedPromptCatalog.ResolveSection(
                RimTalkPromptEntryChannelCatalog.RpgArchiveCompression,
                "output_specification");
            return !IsRpgArchiveCompressionOutputSpecificationInvalid(outputSpec);
        }

        private static void ApplyAnySystemRulesBackgroundMigration(PromptUnifiedCatalog catalog)
        {
            if (catalog == null)
            {
                return;
            }

            string current = catalog.ResolveSection(RimTalkPromptEntryChannelCatalog.Any, "system_rules") ?? string.Empty;
            if (current.IndexOf(RimWorldBackgroundNarrativeLead, StringComparison.Ordinal) >= 0)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(current))
            {
                catalog.SetSection(RimTalkPromptEntryChannelCatalog.Any, "system_rules", RimWorldBackgroundNarrativeText);
                return;
            }

            string separator = current.EndsWith("\n", StringComparison.Ordinal) ? string.Empty : "\n\n";
            catalog.SetSection(
                RimTalkPromptEntryChannelCatalog.Any,
                "system_rules",
                current + separator + RimWorldBackgroundNarrativeText);
        }

        private static readonly string LegacyCharacterPersona =
            "人格基线：优先参考 {{ world.faction.name }} 与 {{ pawn.target.name }} 的关系语境。保持语气稳定、立场连续，不在单轮内突然人设反转。";
        private static readonly string UpdatedCharacterPersona =
            "人格基线：优先参考 {{ world.faction.name }} 与 {{ pawn.target.name }} 的关系语境。保持角色核心性格稳定，但态度必须根据关系变化和客观事实及时调整；当关系/实力/处境已变，继续使用旧语气视为角色扮演失败。";

        private static void ApplyCharacterPersonaStateAnchorMigration(PromptUnifiedCatalog catalog)
        {
            if (catalog == null)
            {
                return;
            }

            string[] channelsToMigrate =
            {
                RimTalkPromptEntryChannelCatalog.Any,
                RimTalkPromptEntryChannelCatalog.DiplomacyDialogue,
                RimTalkPromptEntryChannelCatalog.ProactiveDiplomacyDialogue,
                RimTalkPromptEntryChannelCatalog.RpgDialogue,
                RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue
            };

            foreach (string channel in channelsToMigrate)
            {
                string current = (catalog.ResolveSection(channel, "character_persona") ?? string.Empty).Trim();
                if (string.Equals(current, LegacyCharacterPersona.Trim(), StringComparison.Ordinal))
                {
                    catalog.SetSection(channel, "character_persona", UpdatedCharacterPersona);
                }
            }
        }

        private static readonly string LegacyRpgStateAnchorOld =
            "[强制状态锚定 - 优先级高于对话惯性]\n当前与玩家派系关系：{{ world.current_faction_profile }}\n本派系据点状态：{{ world.faction_settlement_summary }}\n\n以上状态必须反映在你的每一句话中。若你的回应与上述状态矛盾（如关系已为盟友却使用威胁语气、据点只剩1个却虚张声势），视为角色扮演失败。\n\n[实力感知规则]\n- 你的态度必须与自身派系实力匹配：据点多时可以强硬，据点少时必须收敛\n- 失去据点意味着失去资源、兵力和底气——这不是嘴硬能弥补的\n- 当你处于弱势时，可以嘴硬但内心清楚自己处境不妙，言行中应透露出底气的缺失\n\n[矛盾自纠规则]\n禁止固守与事实矛盾的立场。当对方指出你的回应与当前状态不符时，你必须以下列方式之一回应：\n1. 承认并修正（\"……行吧，你说得对。\"）\n2. 给出符合人物性格的解释（\"我不想承认，但……确实是这样。\"）\n3. 以角色内方式转移（\"别以为我好欺负……虽然确实不太好过。\"）\n绝对不得无视对方的指正并重复错误立场。";

        private static readonly string UpdatedRpgStateAnchorSelfAction =
            "[强制状态锚定 - 优先级高于对话惯性]\n当前与玩家派系关系：{{ world.current_faction_profile }}\n本派系据点状态：{{ world.faction_settlement_summary }}\n\n以上状态必须反映在你的每一句话中。若你的回应与上述状态矛盾（如关系已为盟友却使用威胁语气、据点只剩1个却虚张声势、自己正在吃东西却声称面前什么都没有），视为角色扮演失败。\n\n[实力感知规则]\n- 你的态度必须与自身派系实力匹配：据点多时可以强硬，据点少时必须收敛\n- 失去据点意味着失去资源、兵力和底气——这不是嘴硬能弥补的\n- 当你处于弱势时，可以嘴硬但内心清楚自己处境不妙，言行中应透露出底气的缺失\n\n[自身行为一致性]\n- 上下文中你的 Recent Job State 和 Needs 数据是你当前的真实可观测状态，优先级高于你之前对话中的自我描述\n- 如果你的对话历史与这些数据矛盾（如：你正在进食却声称面前没有食物、你的饮食需求已很低却说饥肠辘辘），你之前的对话输出是错误的，必须承认并纠正\n- 你可以对自身状态感到不满或嘴硬，但不能否认正在发生的客观事实\n- 示例：❌ 你正在Ingest(奢侈食物)时说\"我面前什么都没有\" → ✅ \"（嘴里还在嚼）……哼，这不算什么好东西。\"\n\n[矛盾自纠规则]\n禁止固守与任何系统注入事实矛盾的立场——包括派系关系、据点实力和自身可观测状态。当对方指出你的回应与当前状态不符时，你必须以下列方式之一回应：\n1. 承认并修正（\"……行吧，你说得对。\"）\n2. 给出符合人物性格的解释（\"我不想承认，但……确实是这样。\"）\n3. 以角色内方式转移（\"别以为我好欺负……虽然确实不太好过。\"）\n绝对不得无视对方的指正并重复错误立场。";

        private static void ApplyRpgStateAnchorSelfActionMigration(PromptUnifiedCatalog catalog)
        {
            if (catalog == null)
            {
                return;
            }

            string[] channelsToMigrate =
            {
                RimTalkPromptEntryChannelCatalog.Any,
                RimTalkPromptEntryChannelCatalog.RpgDialogue,
                RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue
            };

            foreach (string channel in channelsToMigrate)
            {
                string currentNode = (catalog.ResolveNode(channel, "rpg_state_anchor") ?? string.Empty).Trim();
                if (string.Equals(currentNode, LegacyRpgStateAnchorOld.Trim(), StringComparison.Ordinal))
                {
                    catalog.SetNode(channel, "rpg_state_anchor", UpdatedRpgStateAnchorSelfAction);
                }
            }
        }

        private static void ApplyLegacyRpgPromptMigration(PromptUnifiedCatalog catalog)
        {
            RpgPromptCustomConfig legacy = RpgPromptCustomStore.LoadOrDefault();
            if (legacy == null)
            {
                return;
            }

            RimTalkPromptEntryDefaultsConfig legacySections = RpgPromptCustomStore.LoadLegacyPromptSectionCatalogSnapshot();
            CopyLegacySectionsToUnifiedCatalog(catalog, legacySections);

            CopySectionIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.RpgDialogue, "character_persona", legacy.RoleSetting);
            CopySectionIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue, "character_persona", legacy.RoleSetting);
            catalog.SetSection(RimTalkPromptEntryChannelCatalog.RpgDialogue, "output_specification", RpgOutputSpecificationReferenceText);
            catalog.SetSection(RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue, "output_specification", RpgOutputSpecificationReferenceText);
            CopySectionIfNotEmpty(
                catalog,
                RimTalkPromptEntryChannelCatalog.RpgDialogue,
                "action_rules",
                SanitizeLegacyRpgActionRulesText(legacy.FormatConstraint));
            CopySectionIfNotEmpty(
                catalog,
                RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue,
                "action_rules",
                SanitizeLegacyRpgActionRulesText(legacy.FormatConstraint));
            CopySectionIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.PersonaBootstrap, "system_rules", legacy.PersonaBootstrapSystemPrompt);
            CopySectionIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.PersonaBootstrap, "context", legacy.PersonaBootstrapUserPromptTemplate);
            CopySectionIfNotEmpty(
                catalog,
                RimTalkPromptEntryChannelCatalog.PersonaBootstrap,
                "output_specification",
                BuildPersonaBootstrapOutputSection(legacy.PersonaBootstrapOutputTemplate, legacy.PersonaBootstrapExample));
            CopyNodeIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.RpgDialogue, "rpg_role_setting_fallback", legacy.RoleSettingFallbackTemplate);
            CopyNodeIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue, "rpg_role_setting_fallback", legacy.RoleSettingFallbackTemplate);
            CopyNodeIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.RpgDialogue, "rpg_relationship_profile", legacy.RelationshipProfileTemplate);
            CopyNodeIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue, "rpg_relationship_profile", legacy.RelationshipProfileTemplate);
            CopyNodeIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.RpgDialogue, "rpg_kinship_boundary", legacy.KinshipBoundaryRuleTemplate);
            CopyNodeIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue, "rpg_kinship_boundary", legacy.KinshipBoundaryRuleTemplate);
        }

        private static void CopyLegacySectionsToUnifiedCatalog(
            PromptUnifiedCatalog catalog,
            RimTalkPromptEntryDefaultsConfig legacySections)
        {
            if (catalog == null)
            {
                return;
            }

            RimTalkPromptEntryDefaultsConfig normalized = PromptLegacyCompatMigration.NormalizePromptSections(legacySections);
            foreach (RimTalkPromptChannelDefaultsConfig channel in normalized.Channels ?? new List<RimTalkPromptChannelDefaultsConfig>())
            {
                if (channel == null || string.IsNullOrWhiteSpace(channel.PromptChannel))
                {
                    continue;
                }

                foreach (RimTalkPromptSectionDefaultConfig section in channel.Sections ?? new List<RimTalkPromptSectionDefaultConfig>())
                {
                    if (section == null || string.IsNullOrWhiteSpace(section.SectionId))
                    {
                        continue;
                    }

                    catalog.SetSection(channel.PromptChannel, section.SectionId, section.Content ?? string.Empty);
                }
            }
        }

        private static string SanitizeLegacyRpgActionRulesText(string candidate)
        {
            string normalized = candidate?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
            {
                return normalized;
            }

            if (LooksLikeLegacyRpgProtocolText(normalized) || ContainsPlaceholderActionPayload(normalized))
            {
                return PromptUnifiedCatalog.CreateFallback().ResolveSection(
                    RimTalkPromptEntryChannelCatalog.RpgDialogue,
                    "action_rules");
            }

            return normalized;
        }

        private static void ApplyRpgOutputProtocolMigration(PromptUnifiedCatalog catalog)
        {
            ApplyRpgOutputProtocolMigrationForChannel(catalog, RimTalkPromptEntryChannelCatalog.RpgDialogue);
            ApplyRpgOutputProtocolMigrationForChannel(catalog, RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue);
        }

        private static void ApplyRpgOutputProtocolMigrationForChannel(PromptUnifiedCatalog catalog, string promptChannel)
        {
            if (catalog == null || string.IsNullOrWhiteSpace(promptChannel))
            {
                return;
            }

            string outputSpec = catalog.ResolveSection(promptChannel, "output_specification") ?? string.Empty;
            if (LooksLikeLegacyRpgProtocolText(outputSpec))
            {
                catalog.SetSection(promptChannel, "output_specification", RpgOutputSpecificationReferenceText);
            }

            string actionRules = catalog.ResolveSection(promptChannel, "action_rules") ?? string.Empty;
            if (ContainsPlaceholderActionPayload(actionRules))
            {
                catalog.SetSection(
                    promptChannel,
                    "action_rules",
                    PromptUnifiedCatalog.CreateFallback().ResolveSection(promptChannel, "action_rules"));
            }
        }

        private static bool LooksLikeLegacyRpgProtocolText(string text)
        {
            string normalized = text?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
            {
                return false;
            }

            if (normalized.StartsWith("{\"dialogue\"", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return normalized.IndexOf("{\"dialogue\":\"\",\"actions\":", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("OptionalDef", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("\"amount\":0", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsPlaceholderActionPayload(string text)
        {
            string normalized = text?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
            {
                return false;
            }

            return normalized.IndexOf("OptionalDef", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("OptionalReason", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("\"amount\":0", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool EnsureRpgArchiveCompressionSectionContract(PromptUnifiedCatalog catalog)
        {
            if (catalog == null)
            {
                return false;
            }

            bool changed = false;
            for (int i = 0; i < RpgArchiveCompressionRequiredSectionIds.Length; i++)
            {
                string sectionId = RpgArchiveCompressionRequiredSectionIds[i];
                string expected = GetRpgArchiveCompressionSectionDefault(sectionId);
                string current = catalog.ResolveSection(RimTalkPromptEntryChannelCatalog.RpgArchiveCompression, sectionId);
                string any = catalog.ResolveSection(RimTalkPromptEntryChannelCatalog.Any, sectionId);
                if (!ShouldRepairRpgArchiveCompressionSection(sectionId, current, any, expected))
                {
                    continue;
                }

                catalog.SetSection(RimTalkPromptEntryChannelCatalog.RpgArchiveCompression, sectionId, expected);
                changed = true;
            }

            return changed;
        }

        private static bool ShouldRepairRpgArchiveCompressionSection(
            string sectionId,
            string current,
            string any,
            string expected)
        {
            string normalizedSectionId = PromptSectionSchemaCatalog.NormalizeSectionId(sectionId);
            string currentText = (current ?? string.Empty).Trim();
            string anyText = (any ?? string.Empty).Trim();
            string expectedText = (expected ?? string.Empty).Trim();

            if (currentText.Length == 0)
            {
                // No content — only fill if we have a specific expected value
                return expectedText.Length > 0;
            }

            // For sections without a specific default (expected is empty),
            // inheriting from the "any" channel is the correct and intended behavior.
            // Do NOT flag this as needing repair to avoid infinite fix-save-reload loops.
            if (expectedText.Length == 0)
            {
                return false;
            }

            // Current already matches expected — no repair needed
            if (string.Equals(currentText, expectedText, StringComparison.Ordinal))
            {
                return false;
            }

            // Current equals "any" channel but expected differs — override needed
            if (string.Equals(currentText, anyText, StringComparison.Ordinal))
            {
                return true;
            }

            // output_specification: special validation for invalid legacy content
            if (string.Equals(normalizedSectionId, "output_specification", StringComparison.Ordinal))
            {
                return IsRpgArchiveCompressionOutputSpecificationInvalid(currentText);
            }

            // system_rules: special validation for legacy placeholder patterns
            if (string.Equals(normalizedSectionId, "system_rules", StringComparison.Ordinal))
            {
                return currentText.IndexOf("只保留世界内", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    currentText.IndexOf("{{ ctx.channel }}", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    currentText.IndexOf("角色内表达", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return false;
        }

        private static bool IsRpgArchiveCompressionOutputSpecificationInvalid(string text)
        {
            string normalized = (text ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                return true;
            }

            return normalized.IndexOf("response_contract", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("JSON", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("compressed_summary", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetRpgArchiveCompressionSectionDefault(string sectionId)
        {
            string normalized = PromptSectionSchemaCatalog.NormalizeSectionId(sectionId);
            switch (normalized)
            {
                case "system_rules":
                    return RpgArchiveCompressionSystemRulesText;
                case "output_specification":
                    return RpgArchiveCompressionOutputSpecificationText;
                default:
                    return string.Empty;
            }
        }

        private void ApplyLegacyImageTemplateMigration(PromptUnifiedCatalog catalog)
        {
            DiplomacyImagePromptTemplates ??= new List<DiplomacyImagePromptTemplate>();
            DiplomacyImageTemplateDefaults.EnsureDefaults(DiplomacyImagePromptTemplates);
            foreach (DiplomacyImagePromptTemplate template in DiplomacyImagePromptTemplates.Where(item => item != null))
            {
                string id = PromptUnifiedTemplateAliasConfig.NormalizeTemplateId(template.Id);
                if (id.Length == 0)
                {
                    continue;
                }

                catalog.SetTemplateAlias(
                    RimTalkPromptEntryChannelCatalog.ImageGeneration,
                    id,
                    template.Name,
                    template.Description,
                    template.Text,
                    template.Enabled);
            }

            MirrorImageAlias(catalog, "diplomacy_scene", DiplomacyImageTemplateDefaults.DefaultTemplateId);
            MirrorImageAlias(catalog, "diplomacyscene", DiplomacyImageTemplateDefaults.DefaultTemplateId);
            MirrorImageAlias(catalog, "diplomacy_image", DiplomacyImageTemplateDefaults.DefaultTemplateId);
            MirrorImageAlias(catalog, "diplomacyimage", DiplomacyImageTemplateDefaults.DefaultTemplateId);
            MirrorImageAlias(catalog, "leader_portrait", DiplomacyImageTemplateDefaults.DefaultTemplateId);
        }

        private static void MirrorImageAlias(PromptUnifiedCatalog catalog, string aliasId, string targetTemplateId)
        {
            if (catalog == null)
            {
                return;
            }

            PromptUnifiedTemplateAliasConfig target = catalog.ResolveTemplateAlias(
                RimTalkPromptEntryChannelCatalog.ImageGeneration,
                targetTemplateId);
            if (target == null || string.IsNullOrWhiteSpace(target.Content))
            {
                return;
            }

            catalog.SetTemplateAlias(
                RimTalkPromptEntryChannelCatalog.ImageGeneration,
                aliasId,
                target.Name,
                target.Description,
                target.Content,
                target.Enabled);
        }

        private static bool ApplyStaticLiteralNodeDefaults(PromptUnifiedCatalog catalog)
        {
            if (catalog == null)
            {
                return false;
            }

            bool changed = false;
            string[] diplomacyChannels =
            {
                RimTalkPromptEntryChannelCatalog.Any,
                RimTalkPromptEntryChannelCatalog.DiplomacyDialogue,
                RimTalkPromptEntryChannelCatalog.ProactiveDiplomacyDialogue
            };

            foreach (string channel in diplomacyChannels)
            {
                changed |= SetNodeIfDifferent(
                    catalog,
                    channel,
                    "api_limits_node_template",
                    PromptTextConstants.ApiLimitsNodeLiteralDefault);
                changed |= SetNodeIfDifferent(
                    catalog,
                    channel,
                    "quest_guidance_node_template",
                    PromptTextConstants.QuestGuidanceNodeLiteralDefault);
                changed |= SetNodeIfDifferent(
                    catalog,
                    channel,
                    "response_contract_node_template",
                    PromptTextConstants.ResponseContractNodeLiteralDefault);
            }

            return changed;
        }

        private static bool SetNodeIfDifferent(
            PromptUnifiedCatalog catalog,
            string channel,
            string nodeId,
            string targetText)
        {
            string current = (catalog.ResolveNode(channel, nodeId) ?? string.Empty).Trim();
            string target = (targetText ?? string.Empty).Trim();
            if (string.Equals(current, target, StringComparison.Ordinal))
            {
                return false;
            }

            catalog.SetNode(channel, nodeId, target);
            return true;
        }


        private static string BuildPersonaBootstrapOutputSection(string templateLine, string exampleLine)
        {
            string template = (templateLine ?? string.Empty).Trim();
            string example = (exampleLine ?? string.Empty).Trim();
            if (template.Length == 0 && example.Length == 0)
            {
                return string.Empty;
            }

            // A strict JSON template must stay untouched so the workbench section remains parser-safe.
            if ((template.StartsWith("{", StringComparison.Ordinal) && template.EndsWith("}", StringComparison.Ordinal)) ||
                (template.StartsWith("[", StringComparison.Ordinal) && template.EndsWith("]", StringComparison.Ordinal)))
            {
                return template;
            }

            if (example.Length == 0)
            {
                return template;
            }

            if (template.Length == 0)
            {
                return "Example:\n" + example;
            }

            return template + "\n\nExample:\n" + example;
        }

        private static void CopySectionIfNotEmpty(PromptUnifiedCatalog catalog, string channel, string sectionId, string content)
        {
            string text = content?.Trim() ?? string.Empty;
            if (text.Length == 0 || catalog == null)
            {
                return;
            }

            catalog.SetSection(channel, sectionId, text);
        }

        private static void CopyNodeIfNotEmpty(PromptUnifiedCatalog catalog, string channel, string nodeId, string content)
        {
            string text = content?.Trim() ?? string.Empty;
            if (text.Length == 0 || catalog == null)
            {
                return;
            }

            catalog.SetNode(channel, nodeId, text);
        }

        private static RimTalkPromptChannel ParseChannel(string channel)
        {
            if (string.Equals(channel, "diplomacy", StringComparison.OrdinalIgnoreCase))
            {
                return RimTalkPromptChannel.Diplomacy;
            }

            return RimTalkPromptChannel.Rpg;
        }

        private void ClampRimTalkCompatSettings()
        {
            EnsurePromptSectionCatalogReady();
            RimTalkSummaryHistoryLimit = Mathf.Clamp(
                RimTalkSummaryHistoryLimit,
                RimTalkSummaryHistoryMin,
                RimTalkSummaryHistoryMax);
            PromptSectionCatalog = PromptLegacyCompatMigration.NormalizePromptSections(PromptSectionCatalog);
            RimTalkPersonaCopyTemplate = NormalizePersonaCopyTemplateToStrictScriban(RimTalkPersonaCopyTemplate);
            if (RimTalkPersonaCopyTemplate.Length > RimTalkPersonaCopyTemplateMaxLength)
            {
                RimTalkPersonaCopyTemplate = RimTalkPersonaCopyTemplate.Substring(0, RimTalkPersonaCopyTemplateMaxLength);
            }
        }

        private static string NormalizePersonaCopyTemplateToStrictScriban(string template)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return DefaultRimTalkPersonaCopyTemplate;
            }

            string trimmed = template.Trim();
            if (string.Equals(trimmed, "pawn.personality", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "{{pawn.personality}}", StringComparison.OrdinalIgnoreCase))
            {
                return DefaultRimTalkPersonaCopyTemplate;
            }

            return trimmed;
        }

        internal RimTalkPromptEntryDefaultsConfig GetPromptSectionCatalogClone()
        {
            EnsurePromptSectionCatalogReady();
            return UnifiedPromptCatalog?.ToSectionCatalog() ?? PromptLegacyCompatMigration.NormalizePromptSections(PromptSectionCatalog);
        }

        internal void SetPromptSectionCatalog(RimTalkPromptEntryDefaultsConfig sections)
        {
            throw new InvalidOperationException(
                "SetPromptSectionCatalog is migration-only and cannot be used in the editable workflow. " +
                "Use ImportLegacySectionCatalogToUnifiedCatalog instead.");
        }

        internal void ImportLegacySectionCatalogToUnifiedCatalog(RimTalkPromptEntryDefaultsConfig sections, string sourceId, bool persistToFiles = true)
        {
            PromptSectionCatalog = PromptLegacyCompatMigration.NormalizePromptSections(sections);
            EnsureUnifiedCatalogReady();
            foreach (RimTalkPromptChannelDefaultsConfig channel in PromptSectionCatalog.Channels ?? new System.Collections.Generic.List<RimTalkPromptChannelDefaultsConfig>())
            {
                if (channel == null)
                {
                    continue;
                }

                foreach (RimTalkPromptSectionDefaultConfig section in channel.Sections ?? new System.Collections.Generic.List<RimTalkPromptSectionDefaultConfig>())
                {
                    if (section == null)
                    {
                        continue;
                    }

                    UnifiedPromptCatalog.SetSection(channel.PromptChannel, section.SectionId, section.Content);
                }
            }

            ApplyUnifiedCatalogPersistence(persistToFiles);
            PromptLegacyCompatMigration.ResetLegacyFields(this);
            _promptWorkspaceBufferedChannel = string.Empty;
            _promptWorkspaceBufferedSectionId = string.Empty;
            InvalidatePromptWorkspacePreviewCache();
        }

        internal void SetPromptSectionText(string promptChannel, string sectionId, string content, bool persistToFiles = true)
        {
            EnsurePromptSectionCatalogReady();
            UnifiedPromptCatalog.SetSection(promptChannel, sectionId, content ?? string.Empty);
            PromptSectionCatalog = UnifiedPromptCatalog.ToSectionCatalog();
            ApplyUnifiedCatalogPersistence(persistToFiles);
            InvalidatePromptWorkspacePreviewCache();
        }

        internal string ResolvePromptSectionText(string promptChannel, string sectionId)
        {
            EnsurePromptSectionCatalogReady();
            return UnifiedPromptCatalog?.ResolveSection(promptChannel, sectionId) ?? string.Empty;
        }

        internal string ResolvePromptNodeText(string promptChannel, string nodeId)
        {
            EnsurePromptSectionCatalogReady();
            return UnifiedPromptCatalog?.ResolveNode(promptChannel, nodeId) ?? string.Empty;
        }

        internal PromptUnifiedTemplateAliasConfig ResolvePromptTemplateAlias(string promptChannel, string templateId)
        {
            EnsurePromptSectionCatalogReady();
            return UnifiedPromptCatalog?.ResolveTemplateAlias(promptChannel, templateId);
        }

        internal PromptUnifiedTemplateAliasConfig ResolvePreferredPromptTemplateAlias(string promptChannel, string preferredTemplateId)
        {
            EnsurePromptSectionCatalogReady();
            return UnifiedPromptCatalog?.ResolvePreferredTemplateAlias(promptChannel, preferredTemplateId);
        }

        internal List<PromptUnifiedTemplateAliasConfig> GetPromptTemplateAliases(string promptChannel)
        {
            EnsurePromptSectionCatalogReady();
            return UnifiedPromptCatalog?.GetTemplateAliases(promptChannel) ?? new List<PromptUnifiedTemplateAliasConfig>();
        }

        internal PromptUnifiedCatalog GetPromptUnifiedCatalogClone()
        {
            EnsurePromptSectionCatalogReady();
            return UnifiedPromptCatalog?.Clone() ?? PromptUnifiedCatalog.CreateFallback();
        }

        internal void SetPromptUnifiedCatalog(PromptUnifiedCatalog catalog, bool persistToFiles = true)
        {
            EnsurePromptSectionCatalogReady();
            UnifiedPromptCatalog = catalog?.Clone() ?? PromptUnifiedCatalog.CreateFallback();
            UnifiedPromptCatalog.NormalizeWith(PromptUnifiedCatalog.CreateFallback());
            // Treat applied unified payload as modern source-of-truth and skip legacy backfill overwrite.
            UnifiedPromptCatalog.LegacyMigrated = true;
            if (persistToFiles)
            {
                ApplyUnifiedCatalogPersistence(persistToFiles: true);
            }
            else
            {
                _promptUnifiedCatalogLoaded = true;
                _promptUnifiedCatalogDirty = false;
            }

            PromptSectionCatalog = UnifiedPromptCatalog.ToSectionCatalog();
            PromptLegacyCompatMigration.ResetLegacyFields(this);
            _promptWorkspaceBufferedChannel = string.Empty;
            _promptWorkspaceBufferedSectionId = string.Empty;
            InvalidatePromptWorkspacePreviewCache();
        }

        internal void EnsurePawnPersonalityTokenForRpgChannelsSafe()
        {
            try
            {
                EnsurePromptSectionCatalogReady();
                string[] channels =
                {
                    RimTalkPromptEntryChannelCatalog.RpgDialogue,
                    RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue
                };

                bool changed = false;
                const string sectionId = "character_persona";
                foreach (string channel in channels)
                {
                    if (string.IsNullOrWhiteSpace(channel))
                    {
                        continue;
                    }

                    string current = UnifiedPromptCatalog.ResolveSection(channel, sectionId) ?? string.Empty;
                    const string variableName = "pawn.personality";
                    if (ContainsVariableToken(current, variableName))
                    {
                        continue;
                    }

                    const string token = "{{ pawn.personality }}";
                    string updated = string.IsNullOrWhiteSpace(current)
                        ? token
                        : current.TrimEnd() + "\n" + token;
                    UnifiedPromptCatalog.SetSection(channel, sectionId, updated);
                    changed = true;
                }

                if (changed)
                {
                    ApplyUnifiedCatalogPersistence(persistToFiles: true);
                    PromptSectionCatalog = UnifiedPromptCatalog.ToSectionCatalog();
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to ensure RPG persona token coverage: {ex.Message}");
            }
        }

        internal void SetPromptNodeText(string promptChannel, string nodeId, string content, bool persistToFiles = true)
        {
            EnsurePromptSectionCatalogReady();
            UnifiedPromptCatalog.SetNode(promptChannel, nodeId, content ?? string.Empty);
            ApplyUnifiedCatalogPersistence(persistToFiles);
            InvalidatePromptWorkspacePreviewCache();
        }

        internal List<PromptUnifiedNodeLayoutConfig> GetPromptNodeLayouts(string promptChannel)
        {
            EnsurePromptSectionCatalogReady();
            return UnifiedPromptCatalog
                .GetOrderedNodeLayouts(promptChannel)
                .Select(item => item.Clone())
                .ToList();
        }

        internal List<PromptSectionLayoutConfig> GetPromptSectionLayouts(string promptChannel)
        {
            EnsurePromptSectionCatalogReady();
            return UnifiedPromptCatalog
                .GetOrderedSectionLayouts(promptChannel)
                .Select(item => item.Clone())
                .ToList();
        }

        internal void SavePromptSectionLayouts(string promptChannel, IEnumerable<PromptSectionLayoutConfig> layouts, bool persistToFiles = true)
        {
            EnsurePromptSectionCatalogReady();
            string channel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            List<PromptSectionLayoutConfig> ordered = (layouts ?? Enumerable.Empty<PromptSectionLayoutConfig>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.SectionId))
                .Select(item => item.Clone())
                .OrderBy(item => item.Order)
                .ThenBy(item => item.SectionId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int nextOrder = 0;
            foreach (PromptSectionLayoutConfig item in ordered)
            {
                UnifiedPromptCatalog.SetSectionLayout(channel, item.SectionId, nextOrder);
                nextOrder++;
            }

            ApplyUnifiedCatalogPersistence(persistToFiles);
            InvalidatePromptWorkspacePreviewCache();
        }

        internal PromptUnifiedNodeLayoutConfig ResolvePromptNodeLayout(string promptChannel, string nodeId)
        {
            EnsurePromptSectionCatalogReady();
            return UnifiedPromptCatalog.ResolveNodeLayout(promptChannel, nodeId);
        }

        internal void SetPromptNodeLayout(string promptChannel, string nodeId, PromptUnifiedNodeSlot slot, int order, bool enabled, bool persistToFiles = true)
        {
            EnsurePromptSectionCatalogReady();
            UnifiedPromptCatalog.SetNodeLayout(promptChannel, nodeId, slot, order, enabled);
            ApplyUnifiedCatalogPersistence(persistToFiles);
            InvalidatePromptWorkspacePreviewCache();
        }

        internal void SavePromptNodeLayouts(string promptChannel, IEnumerable<PromptUnifiedNodeLayoutConfig> layouts, bool persistToFiles = true)
        {
            EnsurePromptSectionCatalogReady();
            string channel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            List<PromptUnifiedNodeLayoutConfig> ordered = (layouts ?? Enumerable.Empty<PromptUnifiedNodeLayoutConfig>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.NodeId))
                .Select(item => item.Clone())
                .OrderBy(item => item.GetSlot())
                .ThenBy(item => item.Order)
                .ThenBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var nextOrderBySlot = new Dictionary<PromptUnifiedNodeSlot, int>();
            foreach (PromptUnifiedNodeLayoutConfig item in ordered)
            {
                PromptUnifiedNodeSlot slot = item.GetSlot();
                if (!nextOrderBySlot.TryGetValue(slot, out int nextOrder))
                {
                    nextOrder = 0;
                }

                UnifiedPromptCatalog.SetNodeLayout(channel, item.NodeId, slot, nextOrder, item.Enabled);
                nextOrderBySlot[slot] = nextOrder + 1;
            }

            ApplyUnifiedCatalogPersistence(persistToFiles);
            InvalidatePromptWorkspacePreviewCache();
        }

        internal bool HasPendingUnifiedPromptCatalogChanges()
        {
            EnsurePromptSectionCatalogReady();
            return _promptUnifiedCatalogDirty;
        }

        internal void PersistUnifiedPromptCatalogToCustom()
        {
            EnsurePromptSectionCatalogReady();
            PromptUnifiedCatalogProvider.SaveCustom(UnifiedPromptCatalog);
            _promptUnifiedCatalogDirty = false;
        }

        internal void ReloadPromptUnifiedCatalogFromStorage()
        {
            UnifiedPromptCatalog = PromptUnifiedCatalogProvider.LoadMerged() ?? PromptUnifiedCatalog.CreateFallback();
            UnifiedPromptCatalog.NormalizeWith(PromptUnifiedCatalog.CreateFallback());
            PromptSectionCatalog = UnifiedPromptCatalog.ToSectionCatalog();
            _promptUnifiedCatalogLoaded = true;
            _promptUnifiedCatalogDirty = false;
            InvalidatePromptWorkspacePreviewCache();
        }

        private void ApplyUnifiedCatalogPersistence(bool persistToFiles)
        {
            _promptUnifiedCatalogLoaded = true;
            if (persistToFiles)
            {
                PromptUnifiedCatalogProvider.SaveCustom(UnifiedPromptCatalog);
                _promptUnifiedCatalogDirty = false;
                return;
            }

            _promptUnifiedCatalogDirty = true;
        }
    }
}
