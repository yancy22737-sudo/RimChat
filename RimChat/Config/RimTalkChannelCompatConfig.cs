using System;
using UnityEngine;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: RimChat RimTalk compatibility constants.
    /// Responsibility: represent and clamp one prompt-channel RimTalk compatibility payload.
    /// </summary>
    [Serializable]
    internal sealed class RimTalkChannelCompatConfig
    {
        public bool EnablePromptCompat = true;
        public int PresetInjectionMaxEntries = RimChatSettings.RimTalkPresetInjectionLimitUnlimited;
        public int PresetInjectionMaxChars = RimChatSettings.RimTalkPresetInjectionLimitUnlimited;
        public string CompatTemplate = RimChatSettings.DefaultRimTalkCompatTemplate;

        public static RimTalkChannelCompatConfig CreateDefault()
        {
            return new RimTalkChannelCompatConfig();
        }

        public RimTalkChannelCompatConfig Clone()
        {
            return new RimTalkChannelCompatConfig
            {
                EnablePromptCompat = EnablePromptCompat,
                PresetInjectionMaxEntries = PresetInjectionMaxEntries,
                PresetInjectionMaxChars = PresetInjectionMaxChars,
                CompatTemplate = CompatTemplate
            };
        }

        public void NormalizeWith(RimTalkChannelCompatConfig fallback)
        {
            fallback ??= CreateDefault();

            if (CompatTemplate == null)
            {
                CompatTemplate = fallback.CompatTemplate;
            }

            PresetInjectionMaxEntries = Mathf.Clamp(
                PresetInjectionMaxEntries,
                RimChatSettings.RimTalkPresetInjectionMaxEntriesMin,
                RimChatSettings.RimTalkPresetInjectionMaxEntriesMax);
            PresetInjectionMaxChars = Mathf.Clamp(
                PresetInjectionMaxChars,
                RimChatSettings.RimTalkPresetInjectionMaxCharsMin,
                RimChatSettings.RimTalkPresetInjectionMaxCharsMax);

            if (string.IsNullOrWhiteSpace(CompatTemplate))
            {
                CompatTemplate = fallback.CompatTemplate;
            }

            if (!string.IsNullOrWhiteSpace(CompatTemplate) &&
                CompatTemplate.Length > RimChatSettings.RimTalkCompatTemplateMaxLength)
            {
                CompatTemplate = CompatTemplate.Substring(0, RimChatSettings.RimTalkCompatTemplateMaxLength);
            }
        }
    }

    internal enum RimTalkPromptChannel
    {
        Diplomacy = 0,
        Rpg = 1
    }
}
