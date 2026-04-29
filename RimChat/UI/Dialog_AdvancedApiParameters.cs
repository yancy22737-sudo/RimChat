using RimChat.Core;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    public sealed class Dialog_AdvancedApiParameters : Window
    {
        public override Vector2 InitialSize => new Vector2(480f, 370f);

        private const float TempMin = 0f;
        private const float TempMax = 2f;
        private static readonly int[] MaxTokensTiers = { 1024, 2048, 4096 };

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

            Rect rect = inRect.ContractedBy(14f);
            float y = rect.y;
            float w = rect.width;
            float lineH = 28f;
            float gap = 5f;

            // ── Title ──
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, y, w, lineH), "RimChat_AdvancedApiParameters".Translate());
            y += lineH + 8f;
            Text.Font = GameFont.Small;

            // ── Warning ──
            Rect warnRect = new Rect(rect.x, y, w, 44f);
            GUI.color = new Color(1f, 0.82f, 0.25f);
            Widgets.Label(warnRect, "RimChat_AdvancedApiWarning".Translate());
            GUI.color = Color.white;
            y += 48f;

            // ── Separator ──
            GUI.color = new Color(0.35f, 0.35f, 0.35f);
            Widgets.DrawLineHorizontal(rect.x, y, w);
            GUI.color = Color.white;
            y += gap + 6f;

            // ── Temperature ──
            // Row 1: label + value
            Widgets.Label(new Rect(rect.x, y, 100f, lineH), "RimChat_Temperature".Translate());
            Rect tempValRect = new Rect(rect.x + w - 50f, y, 50f, lineH);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(tempValRect, settings.Temperature.ToString("F2"));
            Text.Anchor = TextAnchor.UpperLeft;
            y += lineH;

            // Row 2: conservative label + slider + random label
            float sideW = 42f;
            Widgets.Label(new Rect(rect.x, y, sideW, lineH), "RimChat_TemperatureConservative".Translate());

            Rect sliderRect = new Rect(rect.x + sideW + 4f, y, w - sideW * 2 - 8f, lineH);
            float newTemp = GUI.HorizontalSlider(sliderRect, settings.Temperature, TempMin, TempMax);
            if (Mathf.Abs(newTemp - settings.Temperature) > 0.001f)
                settings.Temperature = Mathf.Round(newTemp * 100f) / 100f;

            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(rect.x + w - sideW, y, sideW, lineH), "RimChat_TemperatureRandom".Translate());
            Text.Anchor = TextAnchor.UpperLeft;

            TooltipHandler.TipRegion(new Rect(rect.x, y - lineH, w, lineH * 2), "RimChat_TemperatureTooltip".Translate());
            y += lineH + gap + 4f;

            // ── Separator ──
            GUI.color = new Color(0.35f, 0.35f, 0.35f);
            Widgets.DrawLineHorizontal(rect.x, y, w);
            GUI.color = Color.white;
            y += gap + 6f;

            // ── MaxTokens ──
            Widgets.Label(new Rect(rect.x, y, 120f, lineH), "RimChat_MaxTokens".Translate());
            Rect tokensBtnRect = new Rect(rect.x + 130f, y, 150f, lineH);
            if (Widgets.ButtonText(tokensBtnRect, Config.RimChatSettings.GetMaxTokensLabel(settings.MaxTokens)))
            {
                var tokenOptions = new System.Collections.Generic.List<FloatMenuOption>();
                foreach (int tier in MaxTokensTiers)
                {
                    int captured = tier;
                    tokenOptions.Add(new FloatMenuOption(
                        Config.RimChatSettings.GetMaxTokensLabel(captured),
                        () => settings.MaxTokens = captured));
                }
                Find.WindowStack.Add(new FloatMenu(tokenOptions));
            }
            TooltipHandler.TipRegion(new Rect(rect.x, y, w, lineH), "RimChat_MaxTokensTooltip".Translate());
            y += lineH + gap + 4f;

            // ── Separator ──
            GUI.color = new Color(0.35f, 0.35f, 0.35f);
            Widgets.DrawLineHorizontal(rect.x, y, w);
            GUI.color = Color.white;
            y += gap + 6f;

            // ── Thinking mode ──
            // Manual layout: checkbox on left, label on right
            Widgets.Checkbox(new Vector2(rect.x, y + 2f), ref settings.ThinkingEnabled, 24f);
            Widgets.Label(new Rect(rect.x + 28f, y, w - 28f, lineH), "RimChat_ThinkingMode".Translate());
            Rect thinkingTipRect = new Rect(rect.x, y, w, lineH);
            TooltipHandler.TipRegion(thinkingTipRect, "RimChat_ThinkingModeTooltip".Translate());
            y += lineH + 2f;

            if (settings.ThinkingEnabled)
            {
                // Indented sub-item with tree indicator
                float indent = 24f;
                Widgets.Label(new Rect(rect.x + indent, y, 14f, lineH), "▸");
                Widgets.Label(new Rect(rect.x + indent + 16f, y, 110f, lineH), "RimChat_ReasoningEffort".Translate());

                Rect effortBtnRect = new Rect(rect.x + indent + 130f, y, 150f, lineH);
                if (Widgets.ButtonText(effortBtnRect, Config.RimChatSettings.GetReasoningEffortLabel(settings.ReasoningEffort)))
                {
                    var effortOptions = new System.Collections.Generic.List<FloatMenuOption>();
                    foreach (string level in new[] { "low", "medium", "high", "xhigh" })
                    {
                        string captured = level;
                        effortOptions.Add(new FloatMenuOption(
                            Config.RimChatSettings.GetReasoningEffortLabel(captured),
                            () => settings.ReasoningEffort = captured));
                    }
                    Find.WindowStack.Add(new FloatMenu(effortOptions));
                }
                TooltipHandler.TipRegion(effortBtnRect, "RimChat_ReasoningEffortTooltip".Translate());
                y += lineH + 2f;
            }

            y += 8f;

            // ── Reset button (centered) ──
            float resetW = 140f;
            Rect resetRect = new Rect(rect.x + (w - resetW) / 2f, y, resetW, 32f);
            if (Widgets.ButtonText(resetRect, "RimChat_ResetDefaults".Translate()))
            {
                settings.Temperature = 0.7f;
                settings.MaxTokens = 2048;
                settings.ThinkingEnabled = false;
                settings.ReasoningEffort = "medium";
            }
            TooltipHandler.TipRegion(resetRect, "RimChat_ResetDefaultsTooltip".Translate());
        }
    }
}
