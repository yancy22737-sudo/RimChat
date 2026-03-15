using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: RPG runtime settings fields and prompt workbench window entry.
    /// Responsibility: expose non-prompt RPG controls inside Mod Options accordion.
    /// </summary>
    public partial class RimChatSettings : ModSettings
    {
        private void DrawRpgNonPromptSettings(Listing_Standard listing)
        {
            Rect enableDialogueRect = listing.GetRect(24f);
            Widgets.CheckboxLabeled(enableDialogueRect, "RimChat_EnableRPGDialogue".Translate(), ref EnableRPGDialogue);
            RegisterTooltip(enableDialogueRect, "RimChat_EnableRPGDialogueTooltip");

            Rect enableApiRect = listing.GetRect(24f);
            Widgets.CheckboxLabeled(enableApiRect, "RimChat_EnableRPGAPI".Translate(), ref EnableRPGAPI);
            RegisterTooltip(enableApiRect, "RimChat_EnableRPGAPITooltip");

            Rect selfStatusRect = listing.GetRect(24f);
            Widgets.CheckboxLabeled(selfStatusRect, "RimChat_RPGInjectSelfStatus".Translate(), ref RPGInjectSelfStatus);
            RegisterTooltip(selfStatusRect, "RimChat_RPGInjectSelfStatusTooltip");

            Rect interlocutorStatusRect = listing.GetRect(24f);
            Widgets.CheckboxLabeled(interlocutorStatusRect, "RimChat_RPGInjectInterlocutorStatus".Translate(), ref RPGInjectInterlocutorStatus);
            RegisterTooltip(interlocutorStatusRect, "RimChat_RPGInjectInterlocutorStatusTooltip");

            Rect factionBackgroundRect = listing.GetRect(24f);
            Widgets.CheckboxLabeled(factionBackgroundRect, "RimChat_RPGInjectFactionBackground".Translate(), ref RPGInjectFactionBackground);
            RegisterTooltip(factionBackgroundRect, "RimChat_RPGInjectFactionBackgroundTooltip");

            Rect nonVerbalRect = listing.GetRect(24f);
            Widgets.CheckboxLabeled(nonVerbalRect, "RimChat_EnableRPGNonVerbalPawnSpeech".Translate(), ref EnableRPGNonVerbalPawnSpeech);
            RegisterTooltip(nonVerbalRect, "RimChat_EnableRPGNonVerbalPawnSpeechTooltip");

            Rect sceneTagsRow = listing.GetRect(24f);
            Rect sceneTagsLabel = new Rect(sceneTagsRow.x, sceneTagsRow.y, 150f, sceneTagsRow.height);
            Rect sceneTagsInput = new Rect(sceneTagsLabel.xMax + 6f, sceneTagsRow.y, sceneTagsRow.width - sceneTagsLabel.width - 6f, sceneTagsRow.height);
            Widgets.Label(sceneTagsLabel, "RimChat_RpgSceneTags".Translate());
            RegisterTooltip(sceneTagsLabel, "RimChat_RpgSceneTagsTooltip");
            string sceneTags = RpgManualSceneTagsCsv ?? string.Empty;
            string editedTags = Widgets.TextField(sceneTagsInput, sceneTags);
            RegisterTooltip(sceneTagsInput, "RimChat_RpgSceneTagsTooltip");
            if (!string.Equals(editedTags, sceneTags, System.StringComparison.Ordinal))
            {
                RpgManualSceneTagsCsv = editedTags;
                _rpgPreviewUpdateCooldown = 0;
            }

            listing.Gap(4f);
            Rect openWorkbenchRect = listing.GetRect(28f);
            if (Widgets.ButtonText(openWorkbenchRect, "RimChat_RpgOpenPromptWorkbench".Translate()))
            {
                OpenPromptWorkbenchWindow();
            }

            RegisterTooltip(openWorkbenchRect, "RimChat_RpgOpenPromptWorkbenchTooltip");
        }

        private void ResetRpgNonPromptSettingsToDefault()
        {
            EnableRPGDialogue = true;
            EnableRPGAPI = true;
            RPGInjectSelfStatus = true;
            RPGInjectInterlocutorStatus = true;
            RPGInjectFactionBackground = true;
            EnableRPGNonVerbalPawnSpeech = true;
            RpgManualSceneTagsCsv = "scene:daily";
        }
    }
}
