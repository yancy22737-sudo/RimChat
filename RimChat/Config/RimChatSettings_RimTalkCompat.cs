using System;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>/// Dependencies: RimChat settings UI.
 /// Responsibility: persist native prompt section catalog state while exposing legacy compat adapters for UI/import only.
    ///</summary>
    public partial class RimChatSettings : ModSettings
    {
        public int RimTalkSummaryHistoryLimit = 10;
        public bool EnableRimTalkPromptCompat = true;
        public int RimTalkPresetInjectionMaxEntries = 0;
        public int RimTalkPresetInjectionMaxChars = 0;
        public string RimTalkCompatTemplate = DefaultRimTalkCompatTemplate;
        public string RimTalkPersonaCopyTemplate = DefaultRimTalkPersonaCopyTemplate;
        public bool RimTalkAutoPushSessionSummary;
        public bool RimTalkAutoInjectCompatPreset;
        public bool RimTalkChannelSplitMigrated;
        internal RimTalkPromptEntryDefaultsConfig PromptSectionCatalog = RimTalkPromptEntryDefaultsProvider.GetDefaultsSnapshot();

        internal RimTalkChannelCompatConfig RimTalkDiplomacy = RimTalkChannelCompatConfig.CreateDefault();
        internal RimTalkChannelCompatConfig RimTalkRpg = RimTalkChannelCompatConfig.CreateDefault();

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
            EnsureRimTalkChannelMigration();
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
            EnsureRimTalkChannelMigration();
            ClampRimTalkCompatSettings();
            return PromptLegacyCompatMigration.CreateLegacyAdapterFromPromptSections(PromptSectionCatalog, channel);
        }

        internal RimTalkChannelCompatConfig GetRimTalkChannelConfigClone(RimTalkPromptChannel channel)
        {
            return GetRimTalkChannelConfig(channel).Clone();
        }

        internal void SetRimTalkChannelConfig(RimTalkPromptChannel channel, RimTalkChannelCompatConfig config)
        {
            EnsureRimTalkChannelMigration();
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
            PromptSectionCatalog = PromptLegacyCompatMigration.NormalizePromptSections(PromptSectionCatalog);
            if (!RimTalkChannelSplitMigrated)
            {
                RimTalkDiplomacy = PromptLegacyCompatMigration.BuildFromLegacyFields(
                    EnableRimTalkPromptCompat,
                    RimTalkPresetInjectionMaxEntries,
                    RimTalkPresetInjectionMaxChars,
                    RimTalkCompatTemplate,
                    RimTalkDiplomacy,
                    "diplomacy",
                    "settings.diplomacy");
                RimTalkRpg = PromptLegacyCompatMigration.BuildFromLegacyFields(
                    EnableRimTalkPromptCompat,
                    RimTalkPresetInjectionMaxEntries,
                    RimTalkPresetInjectionMaxChars,
                    RimTalkCompatTemplate,
                    RimTalkRpg,
                    "rpg",
                    "settings.rpg");
                RimTalkChannelSplitMigrated = true;
            }

            PromptSectionCatalog = PromptLegacyCompatMigration.ApplyLegacyAdapterToPromptSections(
                PromptSectionCatalog,
                RimTalkDiplomacy,
                RimTalkPromptChannel.Diplomacy,
                "settings.diplomacy");
            PromptSectionCatalog = PromptLegacyCompatMigration.ApplyLegacyAdapterToPromptSections(
                PromptSectionCatalog,
                RimTalkRpg,
                RimTalkPromptChannel.Rpg,
                "settings.rpg");
            PromptLegacyCompatMigration.ResetLegacyFields(this);
        }

        internal void SyncLegacyRimTalkFieldsFromRpgChannel()
        {
            PromptLegacyCompatMigration.ResetLegacyFields(this);
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
            EnsureRimTalkChannelMigration();
            RimTalkSummaryHistoryLimit = Mathf.Clamp(
                RimTalkSummaryHistoryLimit,
                RimTalkSummaryHistoryMin,
                RimTalkSummaryHistoryMax);

            PromptSectionCatalog = PromptLegacyCompatMigration.NormalizePromptSections(PromptSectionCatalog);
            PromptLegacyCompatMigration.ResetLegacyFields(this);

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
            EnsureRimTalkChannelMigration();
            return PromptLegacyCompatMigration.NormalizePromptSections(PromptSectionCatalog);
        }

        internal void SetPromptSectionCatalog(RimTalkPromptEntryDefaultsConfig sections)
        {
            PromptSectionCatalog = PromptLegacyCompatMigration.NormalizePromptSections(sections);
            PromptLegacyCompatMigration.ResetLegacyFields(this);
        }

        internal string ResolvePromptSectionText(string promptChannel, string sectionId)
        {
            EnsureRimTalkChannelMigration();
            return PromptSectionCatalog?.ResolveContent(promptChannel, sectionId) ?? string.Empty;
        }
    }
}
