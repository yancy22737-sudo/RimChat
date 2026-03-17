using System;
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
                    PromptUnifiedCatalogProvider.SaveCustom(UnifiedPromptCatalog);
                }

                UnifiedPromptCatalog.NormalizeWith(PromptUnifiedCatalog.CreateFallback());
            }
            finally
            {
                _isEnsuringUnifiedPromptCatalog = false;
            }
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
    }
}
