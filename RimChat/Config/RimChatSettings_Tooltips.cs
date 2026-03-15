using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: RimWorld tooltip system and settings section identifiers.
    /// Responsibility: centralize tooltip key mapping and hover registration for settings UI.
    /// </summary>
    public partial class RimChatSettings : ModSettings
    {
        private static readonly Dictionary<int, string> TabTooltipKeys = new Dictionary<int, string>
        {
            { 0, "RimChat_Tab_API_Tooltip" },
            { 1, "RimChat_Tab_ModOptions_Tooltip" },
            { 2, "RimChat_Tab_PromptWorkbench_Tooltip" },
            { 3, "RimChat_Tab_ImageApi_Tooltip" }
        };

        private static readonly Dictionary<AIControlSection, string> AISectionTooltipKeys = new Dictionary<AIControlSection, string>
        {
            { AIControlSection.UISettings, "RimChat_UISettingsTooltip" },
            { AIControlSection.PresenceSettings, "RimChat_PresenceSettingsTooltip" },
            { AIControlSection.NpcPushSettings, "RimChat_NpcPushSettingsTooltip" },
            { AIControlSection.RpgDialogueSettings, "RimChat_RpgDialogueSettingsModOptionsTooltip" },
            { AIControlSection.AIBehaviorSettings, "RimChat_AIBehaviorSettingsTooltip" },
            { AIControlSection.RaidSettings, "RimChat_RaidSettingsTooltip" },
            { AIControlSection.GoodwillSettings, "RimChat_GoodwillSettingsTooltip" },
            { AIControlSection.GiftSettings, "RimChat_GiftSettingsTooltip" },
            { AIControlSection.AidSettings, "RimChat_AidSettingsTooltip" },
            { AIControlSection.WarPeaceSettings, "RimChat_WarPeaceSettingsTooltip" },
            { AIControlSection.CaravanSettings, "RimChat_CaravanSettingsTooltip" },
            { AIControlSection.QuestSettings, "RimChat_QuestSettingsTooltip" },
            { AIControlSection.SocialCircleSettings, "RimChat_SocialCircleSettingsTooltip" },
            { AIControlSection.SecuritySettings, "RimChat_SecuritySettingsTooltip" }
        };

        private static readonly Dictionary<string, string> PromptSectionTooltipKeys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "GlobalPrompt", "RimChat_PromptSection_GlobalPrompt_Tooltip" },
            { "EnvironmentPrompts", "RimChat_PromptSection_EnvironmentPrompts_Tooltip" },
            { "FactionPrompts", "RimChat_PromptSection_FactionPrompts_Tooltip" },
            { "ApiActions", "RimChat_PromptSection_ApiActions_Tooltip" },
            { "JsonTemplate", "RimChat_PromptSection_JsonTemplate_Tooltip" },
            { "ImportantRules", "RimChat_PromptSection_ImportantRules_Tooltip" },
            { "PromptTemplates", "RimChat_PromptSection_PromptTemplates_Tooltip" },
            { "SocialCirclePrompts", "RimChat_PromptSection_SocialCirclePrompts_Tooltip" },
            { "DecisionRules", "RimChat_PromptSection_DecisionRules_Tooltip" },
            { "DynamicData", "RimChat_PromptSection_DynamicData_Tooltip" }
        };

        private static readonly Dictionary<string, string> RpgSectionTooltipKeys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "RPGRoleSetting", "RimChat_RPGSection_RoleSetting_Tooltip" },
            { "RPGDialogueStyle", "RimChat_RPGSection_DialogueStyle_Tooltip" },
            { "RPGDynamicInjection", "RimChat_RPGSection_DynamicInjection_Tooltip" },
            { "RPGRimTalkCompatTools", "RimChat_RPGSection_RimTalkCompatTools_Tooltip" },
            { "RPGPawnPersonaPrompts", "RimChat_RPGSection_PawnPersonaPrompts_Tooltip" },
            { "RPGFormatConstraint", "RimChat_RPGSection_FormatConstraint_Tooltip" },
            { "RPGFallbackTemplates", "RimChat_RPGSection_FallbackTemplates_Tooltip" },
            { "RPGApiPromptTemplates", "RimChat_RPGSection_ApiPromptTemplates_Tooltip" }
        };

        private static readonly Dictionary<string, string> RpgFieldTooltipKeys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "RimChat_RPGRoleSettingLabel", "RimChat_RPGRoleSettingTooltip" },
            { "RimChat_RPGDialogueStyleLabel", "RimChat_RPGDialogueStyleTooltip" },
            { "RimChat_RPGFormatConstraintLabel", "RimChat_RPGFormatConstraintTooltip" }
        };

        private static void RegisterTooltip(Rect rect, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (Mouse.IsOver(rect))
            {
                TooltipHandler.TipRegion(rect, key.Translate());
            }
        }

        private static string GetSettingsTabTooltipKey(int tabIndex)
        {
            return TabTooltipKeys.TryGetValue(tabIndex, out string key) ? key : string.Empty;
        }

        private static string GetAISectionTooltipKey(AIControlSection section)
        {
            return AISectionTooltipKeys.TryGetValue(section, out string key) ? key : string.Empty;
        }

        private static string GetPromptSectionTooltipKey(string sectionName)
        {
            return PromptSectionTooltipKeys.TryGetValue(sectionName ?? string.Empty, out string key) ? key : string.Empty;
        }

        private static string GetRpgSectionTooltipKey(string sectionName)
        {
            return RpgSectionTooltipKeys.TryGetValue(sectionName ?? string.Empty, out string key) ? key : string.Empty;
        }

        private static string GetRpgFieldTooltipKey(string labelKey)
        {
            return RpgFieldTooltipKeys.TryGetValue(labelKey ?? string.Empty, out string key) ? key : string.Empty;
        }
    }
}
