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
        public bool EnableRimTalkPromptCompat = true;
        public int RimTalkSummaryHistoryLimit = 10;
        public int RimTalkPresetInjectionMaxEntries = 0;
        public int RimTalkPresetInjectionMaxChars = 0;
        public string RimTalkCompatTemplate = DefaultRimTalkCompatTemplate;

        public const int RimTalkSummaryHistoryMin = 1;
        public const int RimTalkSummaryHistoryMax = 30;
        public const int RimTalkPresetInjectionLimitUnlimited = 0;
        public const int RimTalkPresetInjectionMaxEntriesMin = 0;
        public const int RimTalkPresetInjectionMaxEntriesMax = 200;
        public const int RimTalkPresetInjectionMaxCharsMin = 0;
        public const int RimTalkPresetInjectionMaxCharsMax = 200000;
        public const int RimTalkCompatTemplateMaxLength = 6000;

        public const string DefaultRimTalkCompatTemplate =
@"=== RIMTALK SCRIBAN COMPAT (RIMCHAT) ===
You may reference RimTalk variables/plugins directly in this section.";

        internal void ExposeData_RimTalkCompat()
        {
            ClampRimTalkCompatSettings();
        }

        public int GetRimTalkSummaryHistoryLimitClamped()
        {
            return Mathf.Clamp(RimTalkSummaryHistoryLimit, RimTalkSummaryHistoryMin, RimTalkSummaryHistoryMax);
        }

        public int GetRimTalkPresetInjectionMaxEntriesClamped()
        {
            return Mathf.Clamp(
                RimTalkPresetInjectionMaxEntries,
                RimTalkPresetInjectionMaxEntriesMin,
                RimTalkPresetInjectionMaxEntriesMax);
        }

        public int GetRimTalkPresetInjectionMaxCharsClamped()
        {
            return Mathf.Clamp(
                RimTalkPresetInjectionMaxChars,
                RimTalkPresetInjectionMaxCharsMin,
                RimTalkPresetInjectionMaxCharsMax);
        }

        public string GetRimTalkCompatTemplateOrDefault()
        {
            ClampRimTalkCompatSettings();
            return RimTalkCompatTemplate;
        }

        private void ClampRimTalkCompatSettings()
        {
            RimTalkSummaryHistoryLimit = Mathf.Clamp(
                RimTalkSummaryHistoryLimit,
                RimTalkSummaryHistoryMin,
                RimTalkSummaryHistoryMax);
            RimTalkPresetInjectionMaxEntries = Mathf.Clamp(
                RimTalkPresetInjectionMaxEntries,
                RimTalkPresetInjectionMaxEntriesMin,
                RimTalkPresetInjectionMaxEntriesMax);
            RimTalkPresetInjectionMaxChars = Mathf.Clamp(
                RimTalkPresetInjectionMaxChars,
                RimTalkPresetInjectionMaxCharsMin,
                RimTalkPresetInjectionMaxCharsMax);

            if (string.IsNullOrWhiteSpace(RimTalkCompatTemplate))
            {
                RimTalkCompatTemplate = DefaultRimTalkCompatTemplate;
                return;
            }

            if (RimTalkCompatTemplate.Length > RimTalkCompatTemplateMaxLength)
            {
                RimTalkCompatTemplate = RimTalkCompatTemplate.Substring(0, RimTalkCompatTemplateMaxLength);
            }
        }
    }
}
