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
        private static string cachedTooltip;
        private static bool cachedTooltipState;
        private static bool hasTooltipCache;

        private static Texture2D CommsToggleIcon =>
            cachedIcon ?? (cachedIcon = ResolveCommsToggleIcon());

        private static void Postfix(WidgetRow row, bool worldView)
        {
            if (!ShouldDrawToggle(row, worldView))
            {
                return;
            }

            WindowStack windowStack = Find.WindowStack;
            if (windowStack == null)
            {
                return;
            }

            Dialog_ApiDebugObservability openedWindow = windowStack.WindowOfType<Dialog_ApiDebugObservability>();
            bool isWindowOpen = openedWindow != null;
            if (DrawToggleButton(row, isWindowOpen, out bool toggledValue))
            {
                ApplyToggleAndPersist(windowStack, openedWindow, toggledValue);
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

        private static bool DrawToggleButton(WidgetRow row, bool isWindowOpen, out bool toggledValue)
        {
            bool currentValue = isWindowOpen;
            bool originalValue = currentValue;
            string tip = GetToggleTooltip(currentValue);
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

        private static string GetToggleTooltip(bool enabled)
        {
            if (hasTooltipCache && cachedTooltipState == enabled)
            {
                return cachedTooltip;
            }

            cachedTooltip = "RimChat_TokenStatsToggleIconTooltip".Translate(GetStatusLabel(enabled).Translate());
            cachedTooltipState = enabled;
            hasTooltipCache = true;
            return cachedTooltip;
        }

        private static void ApplyToggleAndPersist(
            WindowStack windowStack,
            Dialog_ApiDebugObservability openedWindow,
            bool toggledValue)
        {
            if (toggledValue)
            {
                if (openedWindow == null)
                {
                    windowStack.Add(new Dialog_ApiDebugObservability());
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
