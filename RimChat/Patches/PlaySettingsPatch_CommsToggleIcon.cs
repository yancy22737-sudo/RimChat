using HarmonyLib;
using RimChat.Core;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimChat.Patches
{
    /// <summary>
    /// Dependencies: RimWorld.PlaySettings, Verse.WidgetRow, RimChat.Core.RimChatMod.
    /// Responsibility: add a map-view bottom-right icon that toggles ReplaceCommsConsole.
    /// </summary>
    [HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
    public static class PlaySettingsPatch_CommsToggleIcon
    {
        private static Texture2D cachedIcon;

        private static Texture2D CommsToggleIcon =>
            cachedIcon ?? (cachedIcon = ContentFinder<Texture2D>.Get("UI/CommsToggleIcon", false) ?? BaseContent.BadTex);

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
            bool currentValue = RimChatMod.Settings.ReplaceCommsConsole;
            bool originalValue = currentValue;
            string tip = "RimChat_CommsToggleIconTooltip".Translate(GetStatusLabel(currentValue).Translate());
            row.ToggleableIcon(
                ref currentValue,
                CommsToggleIcon,
                tip,
                SoundDefOf.Mouseover_ButtonToggle,
                null);
            toggledValue = currentValue;
            return currentValue != originalValue;
        }

        private static string GetStatusLabel(bool enabled)
        {
            return enabled ? "RimChat_CommsToggleStatusOn" : "RimChat_CommsToggleStatusOff";
        }

        private static void ApplyToggleAndPersist(bool toggledValue)
        {
            RimChatMod.Settings.ReplaceCommsConsole = toggledValue;
            RimChatMod mod = RimChatMod.Instance ?? LoadedModManager.GetMod<RimChatMod>();
            mod?.WriteSettings();
            string messageKey = RimChatMod.Settings.ReplaceCommsConsole
                ? "RimChat_CommsToggleEnabledMessage"
                : "RimChat_CommsToggleDisabledMessage";
            Messages.Message(messageKey.Translate(), MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
