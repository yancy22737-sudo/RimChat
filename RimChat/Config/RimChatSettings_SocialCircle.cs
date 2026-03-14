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
            DrawScheduledNewsFrequencySelector(listing);

            Rect buttonRect = listing.GetRect(30f);
            bool canForceGenerate = EnableSocialCircle && Current.ProgramState == ProgramState.Playing && Current.Game != null;
            if (Widgets.ButtonText(buttonRect, "RimChat_SocialForceGenerateButton".Translate(), active: canForceGenerate))
            {
                SocialForceGenerateFailureReason failureReason = SocialForceGenerateFailureReason.Unknown;
                bool success = GameComponent_DiplomacyManager.Instance?.TryForceGeneratePublicPost(
                    DebugGenerateReason.ManualButton,
                    out failureReason) ?? false;

                MessageTypeDef messageType = success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput;
                string message;
                if (success)
                {
                    message = "RimChat_SocialForceGenerateSuccess".Translate();
                }
                else
                {
                    message = GetFailureMessage(failureReason);
                }

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

        private string GetFailureMessage(SocialForceGenerateFailureReason reason)
        {
            switch (reason)
            {
                case SocialForceGenerateFailureReason.Disabled:
                    return "RimChat_SocialForceGenerateFailedDisabled".Translate();
                case SocialForceGenerateFailureReason.AiUnavailable:
                    return "RimChat_SocialForceGenerateFailedAiUnavailable".Translate();
                case SocialForceGenerateFailureReason.QueueFull:
                    return "RimChat_SocialForceGenerateFailedQueueFull".Translate();
                case SocialForceGenerateFailureReason.NoAvailableSeed:
                    return "RimChat_SocialForceGenerateFailedNoSeed".Translate();
                default:
                    return "RimChat_SocialForceGenerateFailed".Translate();
            }
        }

        private void ResetSocialCircleSettingsToDefault()
        {
            EnableSocialCircle = true;
            ScheduledNewsFrequencyLevel = global::RimChat.Config.ScheduledNewsFrequencyLevel.Medium;
            SocialPostIntervalMinDays = 5;
            SocialPostIntervalMaxDays = 7;
            EnablePlayerInfluenceNews = true;
            EnableAISimulationNews = true;
            EnableSocialCircleAutoActions = false;
        }

        private void DrawScheduledNewsFrequencySelector(Listing_Standard listing)
        {
            listing.Label("RimChat_ScheduledNewsFrequency".Translate());
            Rect rowRect = listing.GetRect(30f);
            float buttonWidth = (rowRect.width - 20f) / 3f;
            DrawScheduledNewsFrequencyButton(
                new Rect(rowRect.x, rowRect.y, buttonWidth, 30f),
                global::RimChat.Config.ScheduledNewsFrequencyLevel.Low,
                "RimChat_ScheduledNewsFrequencyLow".Translate());
            DrawScheduledNewsFrequencyButton(
                new Rect(rowRect.x + buttonWidth + 10f, rowRect.y, buttonWidth, 30f),
                global::RimChat.Config.ScheduledNewsFrequencyLevel.Medium,
                "RimChat_ScheduledNewsFrequencyMedium".Translate());
            DrawScheduledNewsFrequencyButton(
                new Rect(rowRect.x + (buttonWidth + 10f) * 2f, rowRect.y, buttonWidth, 30f),
                global::RimChat.Config.ScheduledNewsFrequencyLevel.High,
                "RimChat_ScheduledNewsFrequencyHigh".Translate());
        }

        private void DrawScheduledNewsFrequencyButton(
            Rect rect,
            global::RimChat.Config.ScheduledNewsFrequencyLevel mode,
            string label)
        {
            Color oldColor = GUI.color;
            if (ScheduledNewsFrequencyLevel == mode)
            {
                GUI.color = new Color(0.35f, 0.55f, 0.85f, 0.9f);
            }

            if (Widgets.ButtonText(rect, label))
            {
                ScheduledNewsFrequencyLevel = mode;
            }

            GUI.color = oldColor;
        }
    }
}


