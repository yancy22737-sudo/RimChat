using System;
using System.Collections.Generic;
using RimChat.Compat;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>/// Dependencies: RimTalk compatibility bridge, RimWorld settings UI widgets.
 /// Responsibility: draw RimTalk compatibility template and variable-injection tools in RPG settings.
 ///</summary>
    public partial class RimChatSettings : ModSettings
    {
        private void DrawRPGRimTalkCompatToolsEditor(Rect rect)
        {
            float contentHeight = Mathf.Max(rect.height, 900f);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, contentHeight);
            _rimTalkCompatToolsScroll = GUI.BeginScrollView(rect, _rimTalkCompatToolsScroll, viewRect);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(new Rect(0f, 0f, viewRect.width, viewRect.height));
            DrawRimTalkCompatEditor(listing);
            listing.End();

            GUI.EndScrollView();
        }

        private void DrawRimTalkCompatEditor(Listing_Standard listing)
        {
            if (listing == null)
            {
                return;
            }

            DrawRimTalkCompatBaseSettings(listing);
            DrawRimTalkCompatVariableBrowser(listing);
        }

        private void DrawRimTalkCompatBaseSettings(Listing_Standard listing)
        {
            listing.GapLine();
            listing.Label("RimChat_RimTalkCompatSection".Translate());
            GUI.color = Color.gray;
            listing.Label("RimChat_RimTalkCompatAffectsBoth".Translate());
            GUI.color = Color.white;

            listing.CheckboxLabeled("RimChat_RimTalkCompatEnable".Translate(), ref EnableRimTalkPromptCompat);
            listing.Label("RimChat_RimTalkSummaryHistoryLimit".Translate(GetRimTalkSummaryHistoryLimitClamped()));
            string editedHistory = listing.TextEntry(RimTalkSummaryHistoryLimit.ToString());
            if (int.TryParse(editedHistory, out int parsedLimit))
            {
                RimTalkSummaryHistoryLimit = Mathf.Clamp(parsedLimit, RimTalkSummaryHistoryMin, RimTalkSummaryHistoryMax);
            }

            DrawRimTalkPresetInjectionLimits(listing);
            listing.Label("RimChat_RimTalkCompatTemplate".Translate());
            DrawRimTalkCompatTemplateTextArea(listing.GetRect(120f));

            GUI.color = Color.gray;
            listing.Label("RimChat_RimTalkCompatTemplateHint".Translate());
            listing.Label("RimChat_RimTalkVariableInjectHint".Translate());
            listing.Label("RimChat_RimTalkCompatVariableHelp".Translate());
            GUI.color = Color.white;
        }

        private void DrawRimTalkPresetInjectionLimits(Listing_Standard listing)
        {
            string entriesValue = FormatUnlimitedAwareLimit(GetRimTalkPresetInjectionMaxEntriesClamped());
            listing.Label("RimChat_RimTalkPresetInjectionMaxEntries".Translate(entriesValue));
            string editedEntries = listing.TextEntry(RimTalkPresetInjectionMaxEntries.ToString());
            if (int.TryParse(editedEntries, out int parsedEntries))
            {
                int clampedEntries = Mathf.Clamp(
                    parsedEntries,
                    RimTalkPresetInjectionMaxEntriesMin,
                    RimTalkPresetInjectionMaxEntriesMax);
                if (clampedEntries != RimTalkPresetInjectionMaxEntries)
                {
                    RimTalkPresetInjectionMaxEntries = clampedEntries;
                    _rpgPreviewUpdateCooldown = 0;
                }
            }

            string charsValue = FormatUnlimitedAwareLimit(GetRimTalkPresetInjectionMaxCharsClamped());
            listing.Label("RimChat_RimTalkPresetInjectionMaxChars".Translate(charsValue));
            string editedChars = listing.TextEntry(RimTalkPresetInjectionMaxChars.ToString());
            if (int.TryParse(editedChars, out int parsedChars))
            {
                int clampedChars = Mathf.Clamp(
                    parsedChars,
                    RimTalkPresetInjectionMaxCharsMin,
                    RimTalkPresetInjectionMaxCharsMax);
                if (clampedChars != RimTalkPresetInjectionMaxChars)
                {
                    RimTalkPresetInjectionMaxChars = clampedChars;
                    _rpgPreviewUpdateCooldown = 0;
                }
            }

            GUI.color = Color.gray;
            listing.Label("RimChat_RimTalkPresetInjectionLimitHint".Translate());
            GUI.color = Color.white;
        }

        private static string FormatUnlimitedAwareLimit(int value)
        {
            return value <= RimTalkPresetInjectionLimitUnlimited
                ? "RimChat_Unlimited".Translate().ToString()
                : value.ToString();
        }

        private void DrawRimTalkCompatTemplateTextArea(Rect rect)
        {
            string template = RimTalkCompatTemplate ?? string.Empty;
            float contentHeight = Mathf.Max(rect.height, Text.CalcHeight(template, rect.width - 16f) + 10f);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, contentHeight);
            _rimTalkCompatTemplateScroll = GUI.BeginScrollView(rect, _rimTalkCompatTemplateScroll, viewRect);
            string editedTemplate = GUI.TextArea(viewRect, template);
            GUI.EndScrollView();

            if (string.Equals(editedTemplate, template, StringComparison.Ordinal))
            {
                return;
            }

            RimTalkCompatTemplate = editedTemplate;
            _rpgPreviewUpdateCooldown = 0;
        }

        private void DrawRimTalkCompatVariableBrowser(Listing_Standard listing)
        {
            listing.Gap(6f);
            listing.Label("RimChat_RimTalkVariableBrowserTitle".Translate());

            List<RimTalkRegisteredVariable> variables = RimTalkCompatBridge.GetRegisteredVariablesSnapshot();
            Rect listRect = listing.GetRect(120f);
            float viewHeight = Mathf.Max(listRect.height, variables.Count * 24f + 4f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, viewHeight);
            _rimTalkCompatVariableScroll = GUI.BeginScrollView(listRect, _rimTalkCompatVariableScroll, viewRect);

            float y = 2f;
            for (int i = 0; i < variables.Count; i++)
            {
                DrawRimTalkVariableRow(new Rect(0f, y, viewRect.width, 22f), variables[i]);
                y += 24f;
            }
            GUI.EndScrollView();

            GUI.color = Color.gray;
            listing.Label("RimChat_RimTalkVariableBrowserHint".Translate());
            GUI.color = Color.white;
        }

        private void DrawRimTalkVariableRow(Rect rect, RimTalkRegisteredVariable variable)
        {
            if (variable == null)
            {
                return;
            }

            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
            }

            string name = variable.Name ?? string.Empty;
            Widgets.Label(new Rect(rect.x + 4f, rect.y, rect.width - 110f, rect.height), $"{{{{{name}}}}}");

            Rect insertRect = new Rect(rect.xMax - 104f, rect.y, 104f, rect.height);
            if (Widgets.ButtonText(insertRect, "RimChat_RimTalkInsertToTemplate".Translate()))
            {
                AppendVariableToCompatTemplate(name);
            }

            string tip = $"[{variable.Type}] {name}\n{variable.Description}\n{variable.ModId}";
            TooltipHandler.TipRegion(rect, tip);
        }

        private void AppendVariableToCompatTemplate(string variableName)
        {
            if (string.IsNullOrWhiteSpace(variableName))
            {
                return;
            }

            string token = "{{" + variableName.Trim() + "}}";
            string current = RimTalkCompatTemplate ?? string.Empty;
            if (current.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Messages.Message("RimChat_RimTalkVariableAlreadyInTemplate".Translate(), MessageTypeDefOf.NeutralEvent, false);
                return;
            }

            RimTalkCompatTemplate = string.IsNullOrWhiteSpace(current) ? token : current.TrimEnd() + "\n" + token;
            _rpgPreviewUpdateCooldown = 0;
            Messages.Message("RimChat_RimTalkVariableInserted".Translate(token), MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
