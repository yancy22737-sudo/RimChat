using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: prompt legacy migration service and prompt section catalog defaults.
    /// Responsibility: persist native prompt section state while treating legacy compat fields as load-only payload.
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
        private const int UnifiedCatalogMigrationTargetVersion = 2;

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
                UnifiedPromptCatalog = PromptUnifiedCatalogProvider.LoadMerged();
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
                    literalDefaultsChanged;
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

                if (requiresSave)
                {
                    PromptUnifiedCatalogProvider.SaveCustom(UnifiedPromptCatalog);
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
        }

        private static void ApplyLegacyRpgPromptMigration(PromptUnifiedCatalog catalog)
        {
            RpgPromptCustomConfig legacy = RpgPromptCustomStore.LoadOrDefault();
            if (legacy == null)
            {
                return;
            }

            CopySectionIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.RpgDialogue, "character_persona", legacy.RoleSetting);
            CopySectionIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue, "character_persona", legacy.RoleSetting);
            CopySectionIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.RpgDialogue, "output_specification", legacy.DialogueStyle);
            CopySectionIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue, "output_specification", legacy.DialogueStyle);
            CopySectionIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.RpgDialogue, "action_rules", legacy.FormatConstraint);
            CopySectionIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue, "action_rules", legacy.FormatConstraint);
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
            string[] channels =
            {
                RimTalkPromptEntryChannelCatalog.Any,
                RimTalkPromptEntryChannelCatalog.DiplomacyDialogue,
                RimTalkPromptEntryChannelCatalog.ProactiveDiplomacyDialogue
            };

            foreach (string channel in channels)
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

            PromptUnifiedCatalogProvider.SaveCustom(UnifiedPromptCatalog);
            PromptLegacyCompatMigration.ResetLegacyFields(this);
            _promptWorkspaceBufferedChannel = string.Empty;
            _promptWorkspaceBufferedSectionId = string.Empty;
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

        internal void SetPromptUnifiedCatalog(PromptUnifiedCatalog catalog)
        {
            EnsurePromptSectionCatalogReady();
            UnifiedPromptCatalog = catalog?.Clone() ?? PromptUnifiedCatalog.CreateFallback();
            UnifiedPromptCatalog.NormalizeWith(PromptUnifiedCatalog.CreateFallback());
            PromptUnifiedCatalogProvider.SaveCustom(UnifiedPromptCatalog);
            PromptSectionCatalog = UnifiedPromptCatalog.ToSectionCatalog();
            PromptLegacyCompatMigration.ResetLegacyFields(this);
            _promptWorkspaceBufferedChannel = string.Empty;
            _promptWorkspaceBufferedSectionId = string.Empty;
            InvalidatePromptWorkspacePreviewCache();
        }

        internal void SetPromptNodeText(string promptChannel, string nodeId, string content)
        {
            EnsurePromptSectionCatalogReady();
            UnifiedPromptCatalog.SetNode(promptChannel, nodeId, content ?? string.Empty);
            PromptUnifiedCatalogProvider.SaveCustom(UnifiedPromptCatalog);
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

        internal PromptUnifiedNodeLayoutConfig ResolvePromptNodeLayout(string promptChannel, string nodeId)
        {
            EnsurePromptSectionCatalogReady();
            return UnifiedPromptCatalog.ResolveNodeLayout(promptChannel, nodeId);
        }

        internal void SetPromptNodeLayout(string promptChannel, string nodeId, PromptUnifiedNodeSlot slot, int order, bool enabled)
        {
            EnsurePromptSectionCatalogReady();
            UnifiedPromptCatalog.SetNodeLayout(promptChannel, nodeId, slot, order, enabled);
            PromptUnifiedCatalogProvider.SaveCustom(UnifiedPromptCatalog);
            InvalidatePromptWorkspacePreviewCache();
        }

        internal void SavePromptNodeLayouts(string promptChannel, IEnumerable<PromptUnifiedNodeLayoutConfig> layouts)
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

            PromptUnifiedCatalogProvider.SaveCustom(UnifiedPromptCatalog);
            InvalidatePromptWorkspacePreviewCache();
        }
    }
}
