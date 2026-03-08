using RimChat.DiplomacySystem;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimChat.Config
{
    /// <summary>/// Dependencies: AI settings tab, GameComponent_DiplomacyManager.
 /// Responsibility: social circle settings UI and debug controls.
 ///</summary>
    public partial class RimChatSettings : ModSettings
    {
        private void DrawSocialCircleSettings(Listing_Standard listing)
        {
            listing.CheckboxLabeled("RimChat_EnableSocialCircle".Translate(), ref EnableSocialCircle);
            listing.CheckboxLabeled("RimChat_EnablePlayerInfluenceNews".Translate(), ref EnablePlayerInfluenceNews);
            listing.CheckboxLabeled("RimChat_EnableAISimulationNews".Translate(), ref EnableAISimulationNews);
            listing.CheckboxLabeled("RimChat_EnableSocialCircleAutoActions".Translate(), ref EnableSocialCircleAutoActions);

            listing.Label("RimChat_SocialIntervalMinDays".Translate(SocialPostIntervalMinDays));
            SocialPostIntervalMinDays = Mathf.RoundToInt(listing.Slider(SocialPostIntervalMinDays, 1f, 30f));

            listing.Label("RimChat_SocialIntervalMaxDays".Translate(SocialPostIntervalMaxDays));
            SocialPostIntervalMaxDays = Mathf.RoundToInt(listing.Slider(SocialPostIntervalMaxDays, SocialPostIntervalMinDays, 45f));

            Rect buttonRect = listing.GetRect(30f);
            bool canForceGenerate = EnableSocialCircle && Current.ProgramState == ProgramState.Playing && Current.Game != null;
            if (Widgets.ButtonText(buttonRect, "RimChat_SocialForceGenerateButton".Translate(), active: canForceGenerate))
            {
                bool success = GameComponent_DiplomacyManager.Instance?.ForceGeneratePublicPost(DebugGenerateReason.ManualButton) ?? false;
                MessageTypeDef messageType = success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput;
                string message = success
                    ? "RimChat_SocialForceGenerateSuccess".Translate()
                    : "RimChat_SocialForceGenerateFailed".Translate();
                Messages.Message(message, messageType, false);
                SoundDefOf.Click.PlayOneShotOnCamera(null);
            }

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            if (!EnableSocialCircle)
            {
                listing.Label("RimChat_SocialForceGenerateDisabledHint".Translate());
            }
            else if (!canForceGenerate)
            {
                listing.Label("RimChat_SocialForceGenerateGameHint".Translate());
            }
            else
            {
                listing.Label("RimChat_SocialForceGenerateHint".Translate());
            }
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void ResetSocialCircleSettingsToDefault()
        {
            EnableSocialCircle = true;
            SocialPostIntervalMinDays = 5;
            SocialPostIntervalMaxDays = 7;
            EnablePlayerInfluenceNews = true;
            EnableAISimulationNews = true;
            EnableSocialCircleAutoActions = false;
        }
    }
}


