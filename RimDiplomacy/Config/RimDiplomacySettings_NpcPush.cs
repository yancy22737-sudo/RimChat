using RimDiplomacy.NpcDialogue;
using RimDiplomacy.PawnRpgPush;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimDiplomacy.Config
{
    /// <summary>
    /// Dependencies: RimWorld.Listing_Standard, RimDiplomacySettings_AI UI pipeline.
    /// Responsibility: NPC proactive dialogue settings fields and UI rendering.
    /// </summary>
    public partial class RimDiplomacySettings : ModSettings
    {
        public bool EnableNpcInitiatedDialogue = true;
        public NpcPushFrequencyMode NpcPushFrequencyMode = global::RimDiplomacy.Config.NpcPushFrequencyMode.Low;
        public int NpcQueueMaxPerFaction = 3;
        public float NpcQueueExpireHours = 12f;
        public bool EnableBusyByDrafted = true;
        public bool EnableBusyByHostiles = true;
        public bool EnableBusyByClickRate = true;

        private void DrawNpcInitiatedDialogueSettings(Listing_Standard listing)
        {
            DrawSectionHeader(
                listing,
                "RimDiplomacy_NpcPushSettings".Translate(),
                ResetNpcInitiatedDialogueSettings,
                new Color(0.85f, 0.9f, 1f));

            listing.CheckboxLabeled("RimDiplomacy_EnableNpcInitiatedDialogue".Translate(), ref EnableNpcInitiatedDialogue);
            if (!EnableNpcInitiatedDialogue)
            {
                return;
            }

            DrawNpcPushFrequencySelector(listing);
            listing.Label("RimDiplomacy_NpcQueueMaxPerFaction".Translate(NpcQueueMaxPerFaction));
            NpcQueueMaxPerFaction = Mathf.RoundToInt(listing.Slider(NpcQueueMaxPerFaction, 1f, 10f));
            listing.Label("RimDiplomacy_NpcQueueExpireHours".Translate(NpcQueueExpireHours.ToString("F1")));
            NpcQueueExpireHours = listing.Slider(NpcQueueExpireHours, 1f, 48f);
            listing.CheckboxLabeled("RimDiplomacy_EnableBusyByDrafted".Translate(), ref EnableBusyByDrafted);
            listing.CheckboxLabeled("RimDiplomacy_EnableBusyByHostiles".Translate(), ref EnableBusyByHostiles);
            listing.CheckboxLabeled("RimDiplomacy_EnableBusyByClickRate".Translate(), ref EnableBusyByClickRate);
            DrawDebugForceTriggerButton(listing);
        }

        private void DrawDebugForceTriggerButton(Listing_Standard listing)
        {
            listing.Gap(4f);
            Rect buttonRect = listing.GetRect(30f);
            float leftWidth = (buttonRect.width - 8f) * 0.5f;
            Rect oldButtonRect = new Rect(buttonRect.x, buttonRect.y, leftWidth, buttonRect.height);
            Rect newButtonRect = new Rect(buttonRect.x + leftWidth + 8f, buttonRect.y, leftWidth, buttonRect.height);

            if (Widgets.ButtonText(oldButtonRect, "RimDiplomacy_NpcPush_DebugForceTrigger".Translate()))
            {
                bool ok = GameComponent_NpcDialoguePushManager.Instance?.DebugForceRandomProactiveDialogue() == true;
                MessageTypeDef messageType = ok ? MessageTypeDefOf.TaskCompletion : MessageTypeDefOf.RejectInput;
                string key = ok
                    ? "RimDiplomacy_NpcPush_DebugTriggerSuccess"
                    : "RimDiplomacy_NpcPush_DebugTriggerFailed";
                Messages.Message(key.Translate(), messageType, false);
            }

            if (Widgets.ButtonText(newButtonRect, "RimDiplomacy_PawnRpgPush_DebugForceTrigger".Translate()))
            {
                bool ok = GameComponent_PawnRpgDialoguePushManager.Instance?.DebugForcePawnRpgProactiveDialogue() == true;
                MessageTypeDef messageType = ok ? MessageTypeDefOf.TaskCompletion : MessageTypeDefOf.RejectInput;
                string key = ok
                    ? "RimDiplomacy_PawnRpgPush_DebugTriggerSuccess"
                    : "RimDiplomacy_PawnRpgPush_DebugTriggerFailed";
                Messages.Message(key.Translate(), messageType, false);
            }
        }

        private void DrawNpcPushFrequencySelector(Listing_Standard listing)
        {
            listing.Label("RimDiplomacy_NpcPushFrequency".Translate());
            Rect rowRect = listing.GetRect(30f);
            float buttonWidth = (rowRect.width - 20f) / 3f;
            DrawFrequencyButton(
                new Rect(rowRect.x, rowRect.y, buttonWidth, 30f),
                global::RimDiplomacy.Config.NpcPushFrequencyMode.Low,
                "RimDiplomacy_NpcPushFrequencyLow".Translate());
            DrawFrequencyButton(
                new Rect(rowRect.x + buttonWidth + 10f, rowRect.y, buttonWidth, 30f),
                global::RimDiplomacy.Config.NpcPushFrequencyMode.Medium,
                "RimDiplomacy_NpcPushFrequencyMedium".Translate());
            DrawFrequencyButton(
                new Rect(rowRect.x + (buttonWidth + 10f) * 2f, rowRect.y, buttonWidth, 30f),
                global::RimDiplomacy.Config.NpcPushFrequencyMode.High,
                "RimDiplomacy_NpcPushFrequencyHigh".Translate());
        }

        private void DrawFrequencyButton(Rect rect, NpcPushFrequencyMode mode, string label)
        {
            Color oldColor = GUI.color;
            if (NpcPushFrequencyMode == mode)
            {
                GUI.color = new Color(0.35f, 0.55f, 0.85f, 0.9f);
            }

            if (Widgets.ButtonText(rect, label))
            {
                NpcPushFrequencyMode = mode;
            }

            GUI.color = oldColor;
        }

        private void ResetNpcInitiatedDialogueSettings()
        {
            EnableNpcInitiatedDialogue = true;
            NpcPushFrequencyMode = global::RimDiplomacy.Config.NpcPushFrequencyMode.Low;
            NpcQueueMaxPerFaction = 3;
            NpcQueueExpireHours = 12f;
            EnableBusyByDrafted = true;
            EnableBusyByHostiles = true;
            EnableBusyByClickRate = true;
        }
    }
}
