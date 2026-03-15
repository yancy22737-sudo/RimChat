using HarmonyLib;
using RimChat.Core;
using RimChat.UI;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimChat.Patches
{
    /// <summary>
    /// Dependencies: RimWorld.PlaySettings, Verse.WidgetRow, RimChat.Core.RimChatMod.
    /// Responsibility: add a map-view bottom-right icon that toggles token-stats observability window.
    /// </summary>
    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
    public static class PlaySettingsPatch_CommsToggleIcon
    {
        private const string UniqueIconResourcePath = "UI/RimChat/CommsToggleIcon";
        private const string LegacyIconResourcePath = "UI/CommsToggleIcon";
        private static Texture2D cachedIcon;

        private static Texture2D CommsToggleIcon =>
            cachedIcon ?? (cachedIcon = ResolveCommsToggleIcon());

        private static void Postfix(WidgetRow row, bool worldView)
        {
            if (!ShouldDrawToggle(row, worldView))
            {
                return;
            }

            if (DrawToggleButton(row, out bool toggledValue))
            {
                ApplyToggleAndPersist(toggledValue);
            }
        }

        private static bool ShouldDrawToggle(WidgetRow row, bool worldView)
        {
            if (row == null || worldView)
            {
                return false;
            }

            if (Current.ProgramState != ProgramState.Playing)
            {
                return false;
            }

            return RimChatMod.Settings != null;
        }

        private static bool DrawToggleButton(WidgetRow row, out bool toggledValue)
        {
            bool currentValue = IsTokenStatsWindowOpen();
            bool originalValue = currentValue;
            string tip = "RimChat_TokenStatsToggleIconTooltip".Translate(GetStatusLabel(currentValue).Translate());
            row.ToggleableIcon(
                ref currentValue,
                CommsToggleIcon,
                tip,
                SoundDefOf.Mouseover_ButtonToggle,
                null);
            toggledValue = currentValue;
            return currentValue != originalValue;
        }

        private static Texture2D ResolveCommsToggleIcon()
        {
            Texture2D icon = ContentFinder<Texture2D>.Get(UniqueIconResourcePath, false);
            if (icon != null)
            {
                return icon;
            }

            icon = ContentFinder<Texture2D>.Get(LegacyIconResourcePath, false);
            return icon ?? BaseContent.BadTex;
        }

        private static string GetStatusLabel(bool enabled)
        {
            return enabled ? "RimChat_CommsToggleStatusOn" : "RimChat_CommsToggleStatusOff";
        }

        private static bool IsTokenStatsWindowOpen()
        {
            return Find.WindowStack != null &&
                   Find.WindowStack.WindowOfType<Dialog_ApiDebugObservability>() != null;
        }

        private static void ApplyToggleAndPersist(bool toggledValue)
        {
            Dialog_ApiDebugObservability openedWindow = Find.WindowStack != null
                ? Find.WindowStack.WindowOfType<Dialog_ApiDebugObservability>()
                : null;
            if (toggledValue)
            {
                if (openedWindow == null)
                {
                    Find.WindowStack.Add(new Dialog_ApiDebugObservability());
                }
            }
            else if (openedWindow != null)
            {
                openedWindow.Close();
            }

            string messageKey = toggledValue
                ? "RimChat_TokenStatsToggleEnabledMessage"
                : "RimChat_TokenStatsToggleDisabledMessage";
            Messages.Message(messageKey.Translate(), MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
