using System;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>/// Dependencies: RimChat settings UI.
 /// Responsibility: define RimTalk compatibility settings, defaults, and clamping helpers.
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
        public const string DefaultRimTalkPersonaCopyTemplate = "pawn.personality";

        internal void ExposeData_RimTalkCompat()
        {
            EnsureRimTalkChannelMigration();
            ClampRimTalkCompatSettings();
        }

        public bool IsAnyRimTalkPromptCompatEnabled()
        {
            return GetRimTalkChannelConfig(RimTalkPromptChannel.Diplomacy).EnablePromptCompat ||
                   GetRimTalkChannelConfig(RimTalkPromptChannel.Rpg).EnablePromptCompat;
        }

        public bool IsRimTalkPromptCompatEnabled(string channel)
        {
            RimTalkPromptChannel parsed = ParseChannel(channel);
            return GetRimTalkChannelConfig(parsed).EnablePromptCompat;
        }

        internal RimTalkChannelCompatConfig GetRimTalkChannelConfig(RimTalkPromptChannel channel)
        {
            EnsureRimTalkChannelMigration();
            ClampRimTalkCompatSettings();

            return channel == RimTalkPromptChannel.Diplomacy
                ? RimTalkDiplomacy ?? RimTalkChannelCompatConfig.CreateDefault()
                : RimTalkRpg ?? RimTalkChannelCompatConfig.CreateDefault();
        }

        internal RimTalkChannelCompatConfig GetRimTalkChannelConfigClone(RimTalkPromptChannel channel)
        {
            return GetRimTalkChannelConfig(channel).Clone();
        }

        internal void SetRimTalkChannelConfig(RimTalkPromptChannel channel, RimTalkChannelCompatConfig config)
        {
            EnsureRimTalkChannelMigration();
            RimTalkChannelCompatConfig normalized = (config ?? RimTalkChannelCompatConfig.CreateDefault()).Clone();
            normalized.NormalizeWith(RimTalkChannelCompatConfig.CreateDefault());

            if (channel == RimTalkPromptChannel.Diplomacy)
            {
                RimTalkDiplomacy = normalized;
            }
            else
            {
                RimTalkRpg = normalized;
            }

            SyncLegacyRimTalkFieldsFromRpgChannel();
            ClampRimTalkCompatSettings();
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
            if (!RimTalkChannelSplitMigrated)
            {
                var legacy = new RimTalkChannelCompatConfig
                {
                    EnablePromptCompat = EnableRimTalkPromptCompat,
                    PresetInjectionMaxEntries = RimTalkPresetInjectionMaxEntries,
                    PresetInjectionMaxChars = RimTalkPresetInjectionMaxChars,
                    CompatTemplate = RimTalkCompatTemplate
                };
                legacy.NormalizeWith(RimTalkChannelCompatConfig.CreateDefault());
                RimTalkDiplomacy = legacy.Clone();
                RimTalkRpg = legacy.Clone();
                RimTalkChannelSplitMigrated = true;
            }

            RimTalkDiplomacy ??= RimTalkChannelCompatConfig.CreateDefault();
            RimTalkRpg ??= RimTalkChannelCompatConfig.CreateDefault();
            RimTalkDiplomacy.NormalizeWith(RimTalkChannelCompatConfig.CreateDefault());
            RimTalkRpg.NormalizeWith(RimTalkChannelCompatConfig.CreateDefault());
            SyncLegacyRimTalkFieldsFromRpgChannel();
        }

        internal void SyncLegacyRimTalkFieldsFromRpgChannel()
        {
            RimTalkChannelCompatConfig rpg = RimTalkRpg ?? RimTalkChannelCompatConfig.CreateDefault();
            EnableRimTalkPromptCompat = rpg.EnablePromptCompat;
            RimTalkPresetInjectionMaxEntries = rpg.PresetInjectionMaxEntries;
            RimTalkPresetInjectionMaxChars = rpg.PresetInjectionMaxChars;
            RimTalkCompatTemplate = rpg.CompatTemplate ?? DefaultRimTalkCompatTemplate;
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

            RimTalkDiplomacy ??= RimTalkChannelCompatConfig.CreateDefault();
            RimTalkRpg ??= RimTalkChannelCompatConfig.CreateDefault();
            RimTalkDiplomacy.NormalizeWith(RimTalkChannelCompatConfig.CreateDefault());
            RimTalkRpg.NormalizeWith(RimTalkChannelCompatConfig.CreateDefault());
            SyncLegacyRimTalkFieldsFromRpgChannel();

            RimTalkPersonaCopyTemplate = string.IsNullOrWhiteSpace(RimTalkPersonaCopyTemplate)
                ? DefaultRimTalkPersonaCopyTemplate
                : RimTalkPersonaCopyTemplate.Trim();
            if (RimTalkPersonaCopyTemplate.Length > RimTalkPersonaCopyTemplateMaxLength)
            {
                RimTalkPersonaCopyTemplate = RimTalkPersonaCopyTemplate.Substring(0, RimTalkPersonaCopyTemplateMaxLength);
            }
        }
    }
}
