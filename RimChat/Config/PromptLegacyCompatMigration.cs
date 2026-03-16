using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Prompting;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: RimTalk channel compat models and prompt template rewrite service.
    /// Responsibility: migrate legacy RimTalk prompt payloads into sanitized import-only channel configs.
    /// </summary>
    internal static class PromptLegacyCompatMigration
    {
        public static RimTalkChannelCompatConfig NormalizeChannelConfig(
            RimTalkChannelCompatConfig config,
            string channel,
            string idPrefix)
        {
            RimTalkChannelCompatConfig normalized = (config ?? RimTalkChannelCompatConfig.CreateDefault()).Clone();
            normalized.NormalizeWith(RimTalkChannelCompatConfig.CreateDefault());
            if (string.IsNullOrWhiteSpace(normalized.CompatTemplate))
            {
                normalized.CompatTemplate = ComposeTemplateFromEntries(normalized.PromptEntries);
            }

            normalized.CompatTemplate = string.IsNullOrWhiteSpace(normalized.CompatTemplate)
                ? RimChatSettings.DefaultRimTalkCompatTemplate
                : normalized.CompatTemplate.Trim();
            PromptTemplateAutoRewriter.RewriteRimTalkChannelConfig(
                normalized,
                channel,
                ScribanPromptEngine.Instance,
                string.IsNullOrWhiteSpace(idPrefix) ? "legacy" : idPrefix);
            return normalized;
        }

        public static RimTalkChannelCompatConfig BuildFromLegacyFields(
            bool enablePromptCompat,
            int presetInjectionMaxEntries,
            int presetInjectionMaxChars,
            string compatTemplate,
            RimTalkChannelCompatConfig fallback,
            string channel,
            string idPrefix)
        {
            RimTalkChannelCompatConfig config = fallback?.Clone() ?? RimTalkChannelCompatConfig.CreateDefault();
            config.EnablePromptCompat = enablePromptCompat;
            config.PresetInjectionMaxEntries = presetInjectionMaxEntries;
            config.PresetInjectionMaxChars = presetInjectionMaxChars;
            if (!string.IsNullOrWhiteSpace(compatTemplate))
            {
                config.CompatTemplate = compatTemplate.Trim();
            }

            return NormalizeChannelConfig(config, channel, idPrefix);
        }

        public static void ResetLegacyFields(RpgPromptCustomConfig config)
        {
            if (config == null)
            {
                return;
            }

            config.EnableRimTalkPromptCompat = false;
            config.RimTalkPresetInjectionMaxEntries = RimChatSettings.RimTalkPresetInjectionLimitUnlimited;
            config.RimTalkPresetInjectionMaxChars = RimChatSettings.RimTalkPresetInjectionLimitUnlimited;
            config.RimTalkCompatTemplate = string.Empty;
            config.RimTalkChannelSplitMigrated = true;
        }

        public static void ResetLegacyFields(RimChatSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.EnableRimTalkPromptCompat = false;
            settings.RimTalkPresetInjectionMaxEntries = RimChatSettings.RimTalkPresetInjectionLimitUnlimited;
            settings.RimTalkPresetInjectionMaxChars = RimChatSettings.RimTalkPresetInjectionLimitUnlimited;
            settings.RimTalkCompatTemplate = string.Empty;
            settings.RimTalkChannelSplitMigrated = true;
        }

        private static string ComposeTemplateFromEntries(IEnumerable<RimTalkPromptEntryConfig> entries)
        {
            if (entries == null)
            {
                return string.Empty;
            }

            IEnumerable<string> enabled = entries
                .Where(entry => entry != null && entry.Enabled && !string.IsNullOrWhiteSpace(entry.Content))
                .Select(entry => entry.Content.Trim());
            string combined = string.Join("\n\n", enabled);
            if (!string.IsNullOrWhiteSpace(combined))
            {
                return combined;
            }

            IEnumerable<string> all = entries
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.Content))
                .Select(entry => entry.Content.Trim());
            return string.Join("\n\n", all).Trim();
        }
    }
}
