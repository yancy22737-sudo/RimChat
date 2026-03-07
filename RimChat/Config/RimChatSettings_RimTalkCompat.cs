using System;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: RimWorld Scribe, RimChat settings UI.
    /// Responsibility: define RimTalk compatibility settings, defaults, and clamping helpers.
    /// </summary>
    public partial class RimChatSettings : ModSettings
    {
        public bool EnableRimTalkPromptCompat = true;
        public int RimTalkSummaryHistoryLimit = 10;
        public string RimTalkCompatTemplate = DefaultRimTalkCompatTemplate;

        public const int RimTalkSummaryHistoryMin = 1;
        public const int RimTalkSummaryHistoryMax = 30;
        public const int RimTalkCompatTemplateMaxLength = 6000;

        public const string DefaultRimTalkCompatTemplate =
@"=== RIMTALK SCRIBAN COMPAT (RIMCHAT) ===
You may reference RimTalk variables/plugins directly in this section.
Latest RimChat session summary: {{rimchat_last_session_summary}}
Recent RimChat summaries: {{rimchat_recent_session_summaries}}";

        internal void ExposeData_RimTalkCompat()
        {
            Scribe_Values.Look(ref EnableRimTalkPromptCompat, "EnableRimTalkPromptCompat", true);
            Scribe_Values.Look(ref RimTalkSummaryHistoryLimit, "RimTalkSummaryHistoryLimit", 10);
            Scribe_Values.Look(ref RimTalkCompatTemplate, "RimTalkCompatTemplate", DefaultRimTalkCompatTemplate);
            ClampRimTalkCompatSettings();
        }

        public int GetRimTalkSummaryHistoryLimitClamped()
        {
            return Mathf.Clamp(RimTalkSummaryHistoryLimit, RimTalkSummaryHistoryMin, RimTalkSummaryHistoryMax);
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
