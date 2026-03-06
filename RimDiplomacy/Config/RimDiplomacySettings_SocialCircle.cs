using RimDiplomacy.DiplomacySystem;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimDiplomacy.Config
{
    /// <summary>
    /// Dependencies: AI settings tab, GameComponent_DiplomacyManager.
    /// Responsibility: social circle settings UI and debug controls.
    /// </summary>
    public partial class RimDiplomacySettings : ModSettings
    {
        private void DrawSocialCircleSettings(Listing_Standard listing)
        {
            DrawSectionHeader(
                listing,
                "RimDiplomacy_SocialCircleSettings".Translate(),
                ResetSocialCircleSettingsToDefault,
                new Color(0.8f, 1f, 0.95f));

            listing.CheckboxLabeled("RimDiplomacy_EnableSocialCircle".Translate(), ref EnableSocialCircle);
            listing.CheckboxLabeled("RimDiplomacy_EnablePlayerInfluenceNews".Translate(), ref EnablePlayerInfluenceNews);
            listing.CheckboxLabeled("RimDiplomacy_EnableAISimulationNews".Translate(), ref EnableAISimulationNews);
            listing.CheckboxLabeled("RimDiplomacy_EnableSocialCircleAutoActions".Translate(), ref EnableSocialCircleAutoActions);

            listing.Label("RimDiplomacy_SocialIntervalMinDays".Translate(SocialPostIntervalMinDays));
            SocialPostIntervalMinDays = Mathf.RoundToInt(listing.Slider(SocialPostIntervalMinDays, 1f, 30f));

            listing.Label("RimDiplomacy_SocialIntervalMaxDays".Translate(SocialPostIntervalMaxDays));
            SocialPostIntervalMaxDays = Mathf.RoundToInt(listing.Slider(SocialPostIntervalMaxDays, SocialPostIntervalMinDays, 45f));

            Rect buttonRect = listing.GetRect(30f);
            bool canForceGenerate = EnableSocialCircle && Current.ProgramState == ProgramState.Playing && Current.Game != null;
            if (Widgets.ButtonText(buttonRect, "RimDiplomacy_SocialForceGenerateButton".Translate(), active: canForceGenerate))
            {
                bool success = GameComponent_DiplomacyManager.Instance?.ForceGeneratePublicPost(DebugGenerateReason.ManualButton) ?? false;
                MessageTypeDef messageType = success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput;
                string message = success
                    ? "RimDiplomacy_SocialForceGenerateSuccess".Translate()
                    : "RimDiplomacy_SocialForceGenerateFailed".Translate();
                Messages.Message(message, messageType, false);
                SoundDefOf.Click.PlayOneShotOnCamera(null);
            }

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            if (!EnableSocialCircle)
            {
                listing.Label("RimDiplomacy_SocialForceGenerateDisabledHint".Translate());
            }
            else if (!canForceGenerate)
            {
                listing.Label("RimDiplomacy_SocialForceGenerateGameHint".Translate());
            }
            else
            {
                listing.Label("RimDiplomacy_SocialForceGenerateHint".Translate());
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


