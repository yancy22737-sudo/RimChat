using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Compat;
using RimChat.DiplomacySystem;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: RimTalk compatibility bridge, RPG manager persona sync API, and settings UI widgets.
    /// Responsibility: render dedicated RimTalk tab with per-channel settings, persona copy controls, and variable insertion tools.
    /// </summary>
    public partial class RimChatSettings : ModSettings
    {
        private Vector2 _rimTalkTabScroll = Vector2.zero;
        private string _rimTalkVariableSearch = string.Empty;
        private RimTalkPromptChannel _rimTalkEditorChannel = RimTalkPromptChannel.Rpg;
        private Vector2 _rimTalkPersonaCopyTemplateScroll = Vector2.zero;

        private void DrawTab_RimTalk(Rect rect)
        {
            EnsureRpgPromptTextsLoaded();
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.12f));
            Rect inner = rect.ContractedBy(10f);

            Rect scrollRect = new Rect(inner.x, inner.y, inner.width, inner.height - 42f);
            float contentHeight = Mathf.Max(scrollRect.height, 980f);
            Rect viewRect = new Rect(0f, 0f, scrollRect.width - 16f, contentHeight);
            _rimTalkTabScroll = GUI.BeginScrollView(scrollRect, _rimTalkTabScroll, viewRect);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(new Rect(0f, 0f, viewRect.width, viewRect.height));
            DrawRimTalkRuntimeStatus(listing);
            DrawRimTalkChannelSelector(listing);
            DrawRimTalkChannelEditor(listing);
            DrawRimTalkTabVariableBrowser(listing);
            listing.End();

            GUI.EndScrollView();

            Rect saveRect = new Rect(inner.xMax - 120f, inner.yMax - 30f, 120f, 28f);
            if (Widgets.ButtonText(saveRect, "RimChat_SaveRPGPrompt".Translate()))
            {
                SaveRpgPromptTextsToCustom();
                Messages.Message("RimChat_RPGPromptSaved".Translate(), MessageTypeDefOf.NeutralEvent, false);
            }
        }

        private static void DrawRimTalkRuntimeStatus(Listing_Standard listing)
        {
            RimTalkRuntimeStatus status = RimTalkCompatBridge.GetRuntimeStatus();
            bool available = status.RuntimeAvailable;
            string key = available
                ? "RimChat_RimTalkRuntimeAvailable"
                : "RimChat_RimTalkRuntimeUnavailable";
            GUI.color = available ? new Color(0.6f, 0.9f, 0.6f) : new Color(1f, 0.7f, 0.5f);
            listing.Label(key.Translate());
            if (!available && status.PromptCompatEnabled && !string.IsNullOrWhiteSpace(status.Reason))
            {
                GUI.color = Color.gray;
                listing.Label("RimChat_RimTalkRuntimeReason".Translate(status.Reason));
            }

            GUI.color = Color.white;
            listing.GapLine();
        }

        private void DrawRimTalkChannelSelector(Listing_Standard listing)
        {
            listing.Label("RimChat_RimTalkChannelTitle".Translate());
            Rect row = listing.GetRect(26f);
            float half = (row.width - 8f) * 0.5f;
            Rect diplomacyRect = new Rect(row.x, row.y, half, row.height);
            Rect rpgRect = new Rect(diplomacyRect.xMax + 8f, row.y, half, row.height);

            bool diplomacySelected = _rimTalkEditorChannel == RimTalkPromptChannel.Diplomacy;
            bool rpgSelected = _rimTalkEditorChannel == RimTalkPromptChannel.Rpg;
            Widgets.DrawBoxSolid(diplomacyRect, diplomacySelected ? new Color(0.25f, 0.35f, 0.55f) : new Color(0.18f, 0.18f, 0.2f));
            Widgets.DrawBoxSolid(rpgRect, rpgSelected ? new Color(0.25f, 0.35f, 0.55f) : new Color(0.18f, 0.18f, 0.2f));
            Widgets.Label(diplomacyRect, "RimChat_RimTalkChannelDiplomacy".Translate());
            Widgets.Label(rpgRect, "RimChat_RimTalkChannelRpg".Translate());

            if (Widgets.ButtonInvisible(diplomacyRect))
            {
                _rimTalkEditorChannel = RimTalkPromptChannel.Diplomacy;
            }

            if (Widgets.ButtonInvisible(rpgRect))
            {
                _rimTalkEditorChannel = RimTalkPromptChannel.Rpg;
            }

            listing.Gap(6f);
        }

        private void DrawRimTalkChannelEditor(Listing_Standard listing)
        {
            RimTalkChannelCompatConfig config = GetRimTalkChannelConfigClone(_rimTalkEditorChannel);
            bool enabled = config.EnablePromptCompat;
            listing.CheckboxLabeled("RimChat_RimTalkCompatEnable".Translate(), ref enabled);
            if (enabled != config.EnablePromptCompat)
            {
                config.EnablePromptCompat = enabled;
                SetRimTalkChannelConfig(_rimTalkEditorChannel, config);
            }

            bool autoPushSummary = RimTalkAutoPushSessionSummary;
            listing.CheckboxLabeled("RimChat_RimTalkAutoPushSummary".Translate(), ref autoPushSummary);
            if (autoPushSummary != RimTalkAutoPushSessionSummary)
            {
                RimTalkAutoPushSessionSummary = autoPushSummary;
            }

            bool autoInjectPreset = RimTalkAutoInjectCompatPreset;
            listing.CheckboxLabeled("RimChat_RimTalkAutoInjectPreset".Translate(), ref autoInjectPreset);
            if (autoInjectPreset != RimTalkAutoInjectCompatPreset)
            {
                RimTalkAutoInjectCompatPreset = autoInjectPreset;
            }

            listing.Label("RimChat_RimTalkSummaryHistoryLimit".Translate(GetRimTalkSummaryHistoryLimitClamped()));
            string editedHistory = listing.TextEntry(RimTalkSummaryHistoryLimit.ToString());
            if (int.TryParse(editedHistory, out int parsedHistory))
            {
                RimTalkSummaryHistoryLimit = Mathf.Clamp(parsedHistory, RimTalkSummaryHistoryMin, RimTalkSummaryHistoryMax);
            }

            int currentEntries = config.PresetInjectionMaxEntries;
            string entriesValue = FormatUnlimitedAwareLimit(GetRimTalkPresetInjectionMaxEntriesClamped(GetCurrentChannelToken()));
            listing.Label("RimChat_RimTalkPresetInjectionMaxEntries".Translate(entriesValue));
            string editedEntries = listing.TextEntry(currentEntries.ToString());
            if (int.TryParse(editedEntries, out int parsedEntries))
            {
                config.PresetInjectionMaxEntries = Mathf.Clamp(
                    parsedEntries,
                    RimTalkPresetInjectionMaxEntriesMin,
                    RimTalkPresetInjectionMaxEntriesMax);
                SetRimTalkChannelConfig(_rimTalkEditorChannel, config);
            }

            int currentChars = config.PresetInjectionMaxChars;
            string charsValue = FormatUnlimitedAwareLimit(GetRimTalkPresetInjectionMaxCharsClamped(GetCurrentChannelToken()));
            listing.Label("RimChat_RimTalkPresetInjectionMaxChars".Translate(charsValue));
            string editedChars = listing.TextEntry(currentChars.ToString());
            if (int.TryParse(editedChars, out int parsedChars))
            {
                config.PresetInjectionMaxChars = Mathf.Clamp(
                    parsedChars,
                    RimTalkPresetInjectionMaxCharsMin,
                    RimTalkPresetInjectionMaxCharsMax);
                SetRimTalkChannelConfig(_rimTalkEditorChannel, config);
            }

            listing.Label("RimChat_RimTalkCompatTemplate".Translate());
            DrawRimTalkChannelTemplateTextArea(listing.GetRect(150f), config);
            if (_rimTalkEditorChannel == RimTalkPromptChannel.Rpg)
            {
                DrawRimTalkPersonaCopyTemplateEditor(listing);
            }

            GUI.color = Color.gray;
            listing.Label("RimChat_RimTalkCompatTemplateHint".Translate());
            listing.Label("RimChat_RimTalkPresetInjectionLimitHint".Translate());
            listing.Label("RimChat_RimTalkIsolationHint".Translate());
            if (_rimTalkEditorChannel == RimTalkPromptChannel.Rpg)
            {
                listing.Label("RimChat_RimTalkPersonaCopyTemplateHint".Translate());
            }
            GUI.color = Color.white;
            listing.GapLine();
        }

        private void DrawRimTalkChannelTemplateTextArea(Rect rect, RimTalkChannelCompatConfig config)
        {
            string current = config?.CompatTemplate ?? string.Empty;
            float contentHeight = Mathf.Max(rect.height, Text.CalcHeight(current, rect.width - 16f) + 10f);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, contentHeight);
            _rimTalkCompatTemplateScroll = GUI.BeginScrollView(rect, _rimTalkCompatTemplateScroll, viewRect);
            string edited = GUI.TextArea(viewRect, current);
            GUI.EndScrollView();

            if (!string.Equals(edited, current, StringComparison.Ordinal))
            {
                RimTalkChannelCompatConfig changed = config?.Clone() ?? RimTalkChannelCompatConfig.CreateDefault();
                changed.CompatTemplate = edited;
                SetRimTalkChannelConfig(_rimTalkEditorChannel, changed);
            }
        }

        private void DrawRimTalkPersonaCopyTemplateEditor(Listing_Standard listing)
        {
            listing.Gap(4f);
            listing.Label("RimChat_RimTalkPersonaCopyTemplate".Translate());
            string current = RimTalkPersonaCopyTemplate ?? DefaultRimTalkPersonaCopyTemplate;
            Rect rect = listing.GetRect(90f);
            float contentHeight = Mathf.Max(rect.height, Text.CalcHeight(current, rect.width - 16f) + 10f);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, contentHeight);
            _rimTalkPersonaCopyTemplateScroll = GUI.BeginScrollView(rect, _rimTalkPersonaCopyTemplateScroll, viewRect);
            string edited = GUI.TextArea(viewRect, current);
            GUI.EndScrollView();

            if (!string.Equals(edited, current, StringComparison.Ordinal))
            {
                RimTalkPersonaCopyTemplate = edited;
            }

            DrawRimTalkManualPersonaCopyButton(listing);
        }

        private static void DrawRimTalkManualPersonaCopyButton(Listing_Standard listing)
        {
            listing.Gap(4f);
            Rect buttonRect = listing.GetRect(28f);
            if (!Widgets.ButtonText(buttonRect, "RimChat_RimTalkPersonaManualCopyButton".Translate()))
            {
                return;
            }

            GameComponent_RPGManager manager = Current.Game?.GetComponent<GameComponent_RPGManager>();
            if (manager == null)
            {
                Messages.Message("RimChat_RPGPawnPersonaNeedGame".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            bool changed = manager.TrySyncAllColonyPawnPersonasFromRimTalk(
                out int updated,
                out int cleared,
                out int unchanged,
                out int skipped);
            MessageTypeDef messageType = changed ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NeutralEvent;
            Messages.Message(
                "RimChat_RimTalkPersonaManualCopySummary".Translate(updated, cleared, unchanged, skipped),
                messageType,
                false);
        }

        private void DrawRimTalkTabVariableBrowser(Listing_Standard listing)
        {
            listing.Label("RimChat_RimTalkVariableBrowserTitle".Translate());
            listing.Label("RimChat_RimTalkVariableBrowserHint".Translate());
            Rect searchRow = listing.GetRect(24f);
            Rect searchLabel = new Rect(searchRow.x, searchRow.y, 120f, searchRow.height);
            Rect searchInput = new Rect(searchLabel.xMax + 6f, searchRow.y, searchRow.width - searchLabel.width - 6f, searchRow.height);
            Widgets.Label(searchLabel, "RimChat_RimTalkVariableSearch".Translate());
            _rimTalkVariableSearch = Widgets.TextField(searchInput, _rimTalkVariableSearch ?? string.Empty);

            List<RimTalkRegisteredVariable> variables = RimTalkCompatBridge.GetRegisteredVariablesSnapshot() ?? new List<RimTalkRegisteredVariable>();
            if (!string.IsNullOrWhiteSpace(_rimTalkVariableSearch))
            {
                string term = _rimTalkVariableSearch.Trim();
                variables = variables.Where(item =>
                        ContainsTerm(item?.Name, term) ||
                        ContainsTerm(item?.Type, term) ||
                        ContainsTerm(item?.ModId, term) ||
                        ContainsTerm(item?.Description, term))
                    .ToList();
            }

            variables = variables
                .OrderBy(item => item?.Type ?? string.Empty)
                .ThenBy(item => item?.ModId ?? string.Empty)
                .ThenBy(item => item?.Name ?? string.Empty)
                .ToList();

            Rect listRect = listing.GetRect(220f);
            float rowHeight = 24f;
            float headerHeight = 22f;
            float viewHeight = 4f;
            string lastGroup = string.Empty;
            for (int i = 0; i < variables.Count; i++)
            {
                string currentGroup = BuildVariableGroupKey(variables[i]);
                if (!string.Equals(lastGroup, currentGroup, StringComparison.Ordinal))
                {
                    viewHeight += headerHeight;
                    lastGroup = currentGroup;
                }

                viewHeight += rowHeight;
            }

            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, Mathf.Max(listRect.height, viewHeight));
            _rimTalkCompatVariableScroll = GUI.BeginScrollView(listRect, _rimTalkCompatVariableScroll, viewRect);
            float y = 2f;
            lastGroup = string.Empty;
            for (int i = 0; i < variables.Count; i++)
            {
                RimTalkRegisteredVariable variable = variables[i];
                string group = BuildVariableGroupKey(variable);
                if (!string.Equals(lastGroup, group, StringComparison.Ordinal))
                {
                    GUI.color = Color.gray;
                    Widgets.Label(new Rect(0f, y, viewRect.width, 20f), group);
                    GUI.color = Color.white;
                    y += headerHeight;
                    lastGroup = group;
                }

                DrawRimTalkTabVariableRow(new Rect(0f, y, viewRect.width, rowHeight), variable);
                y += rowHeight;
            }

            GUI.EndScrollView();
        }

        private static bool ContainsTerm(string value, string term)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildVariableGroupKey(RimTalkRegisteredVariable variable)
        {
            string type = string.IsNullOrWhiteSpace(variable?.Type) ? "Unknown" : variable.Type;
            string mod = string.IsNullOrWhiteSpace(variable?.ModId) ? "UnknownMod" : variable.ModId;
            return $"[{type}] {mod}";
        }

        private void DrawRimTalkTabVariableRow(Rect rect, RimTalkRegisteredVariable variable)
        {
            if (variable == null)
            {
                return;
            }

            Widgets.DrawHighlightIfMouseover(rect);
            string token = "{{" + (variable.Name ?? string.Empty) + "}}";
            Widgets.Label(new Rect(rect.x + 4f, rect.y, rect.width - 110f, rect.height), token);
            Rect insertRect = new Rect(rect.xMax - 104f, rect.y, 104f, rect.height);
            if (Widgets.ButtonText(insertRect, "RimChat_RimTalkInsertToTemplate".Translate()))
            {
                AppendVariableToCurrentRimTalkTemplate(variable.Name);
            }

            string tip = $"[{variable.Type}] {variable.Name}\n{variable.Description}\n{variable.ModId}";
            TooltipHandler.TipRegion(rect, tip);
        }

        private void AppendVariableToCurrentRimTalkTemplate(string variableName)
        {
            if (string.IsNullOrWhiteSpace(variableName))
            {
                return;
            }

            RimTalkChannelCompatConfig config = GetRimTalkChannelConfigClone(_rimTalkEditorChannel);
            string token = "{{" + variableName.Trim() + "}}";
            string current = config.CompatTemplate ?? string.Empty;
            if (current.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Messages.Message("RimChat_RimTalkVariableAlreadyInTemplate".Translate(), MessageTypeDefOf.NeutralEvent, false);
                return;
            }

            config.CompatTemplate = string.IsNullOrWhiteSpace(current)
                ? token
                : current.TrimEnd() + "\n" + token;
            SetRimTalkChannelConfig(_rimTalkEditorChannel, config);
            Messages.Message("RimChat_RimTalkVariableInserted".Translate(token), MessageTypeDefOf.NeutralEvent, false);
        }

        private string GetCurrentChannelToken()
        {
            return _rimTalkEditorChannel == RimTalkPromptChannel.Diplomacy ? "diplomacy" : "rpg";
        }
    }
}
