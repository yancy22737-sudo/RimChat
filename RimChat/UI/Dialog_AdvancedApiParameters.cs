using RimChat.Core;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    public sealed class Dialog_AdvancedApiParameters : Window
    {
        public override Vector2 InitialSize => new Vector2(420f, 180f);

        public Dialog_AdvancedApiParameters()
        {
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            doCloseX = true;
            doCloseButton = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            RimChatSettings settings = RimChatMod.Settings;
            if (settings == null)
            {
                Widgets.Label(inRect, "RimChat_SettingsUnavailable".Translate());
                return;
            }

            Rect rect = inRect.ContractedBy(12f);
            float y = rect.y;
            float lineHeight = 28f;

            // Title
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, y, rect.width, lineHeight), "RimChat_AdvancedApiParameters".Translate());
            y += lineHeight + 8f;
            Text.Font = GameFont.Small;

            // Thinking mode toggle
            Rect thinkingRect = new Rect(rect.x, y, rect.width, lineHeight);
            Widgets.CheckboxLabeled(thinkingRect, "RimChat_ThinkingMode".Translate(), ref settings.ThinkingEnabled);
            TooltipHandler.TipRegion(thinkingRect, "RimChat_ThinkingModeTooltip".Translate());
            y += lineHeight + 4f;

            // Reasoning effort selector (only when thinking enabled)
            if (settings.ThinkingEnabled)
            {
                Rect effortLabelRect = new Rect(rect.x, y, 140f, lineHeight);
                Widgets.Label(effortLabelRect, "RimChat_ReasoningEffort".Translate());

                Rect effortSelectRect = new Rect(effortLabelRect.xMax + 4f, y, 180f, lineHeight);
                string currentLabel = Config.RimChatSettings.GetReasoningEffortLabel(settings.ReasoningEffort);
                if (Widgets.ButtonText(effortSelectRect, currentLabel))
                {
                    var options = new System.Collections.Generic.List<FloatMenuOption>();
                    foreach (string level in new[] { "low", "medium", "high", "xhigh" })
                    {
                        string captured = level;
                        options.Add(new FloatMenuOption(
                            Config.RimChatSettings.GetReasoningEffortLabel(captured),
                            () => settings.ReasoningEffort = captured));
                    }
                    Find.WindowStack.Add(new FloatMenu(options));
                }
                TooltipHandler.TipRegion(effortSelectRect, "RimChat_ReasoningEffortTooltip".Translate());
            }
        }
    }
}
