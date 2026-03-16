using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RimChat.Config
{
    [Serializable]
    internal sealed class RimTalkPromptEntryConfig
    {
        public string Id = string.Empty;
        public string SectionId = string.Empty;
        public string Name = "Entry";
        public string Role = "System";
        public string CustomRole = string.Empty;
        public string Position = "Relative";
        public int InChatDepth = 0;
        public bool Enabled = true;
        public string PromptChannel = RimTalkPromptEntryChannelCatalog.Any;
        public string Content = string.Empty;

        public RimTalkPromptEntryConfig Clone()
        {
            return new RimTalkPromptEntryConfig
            {
                Id = Id ?? string.Empty,
                SectionId = SectionId ?? string.Empty,
                Name = Name ?? "Entry",
                Role = Role ?? "System",
                CustomRole = CustomRole ?? string.Empty,
                Position = Position ?? "Relative",
                InChatDepth = InChatDepth,
                Enabled = Enabled,
                PromptChannel = PromptChannel ?? RimTalkPromptEntryChannelCatalog.Any,
                Content = Content ?? string.Empty
            };
        }

        public void NormalizeWith(RimTalkPromptEntryConfig fallback)
        {
            fallback ??= new RimTalkPromptEntryConfig();
            if (string.IsNullOrWhiteSpace(Id))
            {
                Id = Guid.NewGuid().ToString("N");
            }

            SectionId = string.IsNullOrWhiteSpace(SectionId) ? string.Empty : SectionId.Trim();
            Name = string.IsNullOrWhiteSpace(Name) ? fallback.Name : Name.Trim();
            Role = string.IsNullOrWhiteSpace(Role) ? fallback.Role : Role.Trim();
            CustomRole = string.IsNullOrWhiteSpace(CustomRole) ? string.Empty : CustomRole.Trim();
            Position = string.IsNullOrWhiteSpace(Position) ? fallback.Position : Position.Trim();
            Content ??= fallback.Content ?? string.Empty;
            InChatDepth = Mathf.Clamp(InChatDepth, 0, 32);
            PromptChannel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(
                string.IsNullOrWhiteSpace(PromptChannel) ? fallback.PromptChannel : PromptChannel);
        }
    }

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
        public List<RimTalkPromptEntryConfig> PromptEntries = new List<RimTalkPromptEntryConfig>();

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
                CompatTemplate = CompatTemplate,
                PromptEntries = PromptEntries?.Select(entry => entry?.Clone()).Where(entry => entry != null).ToList()
                               ?? new List<RimTalkPromptEntryConfig>()
            };
        }

        public void NormalizeWith(RimTalkChannelCompatConfig fallback)
        {
            fallback ??= CreateDefault();

            if (CompatTemplate == null)
            {
                CompatTemplate = fallback.CompatTemplate;
            }

            PromptEntries ??= new List<RimTalkPromptEntryConfig>();

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

            NormalizePromptEntries();

            if (!string.IsNullOrWhiteSpace(CompatTemplate) &&
                CompatTemplate.Length > RimChatSettings.RimTalkCompatTemplateMaxLength)
            {
                CompatTemplate = CompatTemplate.Substring(0, RimChatSettings.RimTalkCompatTemplateMaxLength);
            }
        }

        private void NormalizePromptEntries()
        {
            PromptEntries = PromptEntries
                .Where(entry => entry != null)
                .Select(entry =>
                {
                    entry.NormalizeWith(new RimTalkPromptEntryConfig());
                    return entry;
                })
                .ToList();
        }
    }

    internal enum RimTalkPromptChannel
    {
        Diplomacy = 0,
        Rpg = 1
    }
}
