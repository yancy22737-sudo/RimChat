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
        private RimTalkPromptChannel _rimTalkEditorChannel = RimTalkPromptChannel.Rpg;
        private Vector2 _rimTalkPersonaCopyTemplateScroll = Vector2.zero;
        private Vector2 _rimTalkEntryListScroll = Vector2.zero;
        private Vector2 _rimTalkEntryContentScroll = Vector2.zero;
        private string _rimTalkSelectedEntryId = string.Empty;
        private string _rimTalkDepthBuffer = string.Empty;

        private static readonly string[] RimTalkEntryRoles = { "System", "User", "Assistant" };
        private static readonly string[] RimTalkEntryPositions = { "Relative", "InChat" };

        private void DrawTab_RimTalk(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.12f));
            Rect inner = rect.ContractedBy(12f);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 28f), "RimChat_RimTalkTabMigratedTitle".Translate());
            GUI.color = Color.gray;
            Widgets.Label(new Rect(inner.x, inner.y + 34f, inner.width, 84f), "RimChat_RimTalkTabMigratedHint".Translate());
            GUI.color = Color.white;

            Rect openRect = new Rect(inner.x, inner.y + 124f, 260f, 30f);
            if (Widgets.ButtonText(openRect, "RimChat_RimTalkTabOpenPromptWorkbench".Translate()))
            {
                OpenPromptWorkbenchWindowForRpg();
            }

            Rect disableRect = new Rect(inner.x, openRect.yMax + 8f, 260f, 30f);
            if (Widgets.ButtonText(disableRect, "RimChat_RimTalkTabUseStablePromptPage".Translate()))
            {
                SetPromptWorkbenchExperimentalEnabled(false);
                selectedTab = 2;
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

            DrawRimTalkPromptEntryWorkbench(listing, config);
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

        private void DrawRimTalkPromptEntryWorkbench(Listing_Standard listing, RimTalkChannelCompatConfig config)
        {
            if (config == null)
            {
                return;
            }

            config.NormalizeWith(RimTalkChannelCompatConfig.CreateDefault());
            EnsureRimTalkEntrySelection(config);
            listing.Label("RimChat_RimTalkCompatTemplate".Translate());
            Rect workRect = listing.GetRect(312f);
            float leftWidth = Mathf.Clamp(workRect.width * 0.38f, 250f, 340f);
            Rect leftRect = new Rect(workRect.x, workRect.y, leftWidth, workRect.height);
            Rect rightRect = new Rect(leftRect.xMax + 8f, workRect.y, workRect.width - leftWidth - 8f, workRect.height);
            Widgets.DrawBoxSolid(leftRect, new Color(0.12f, 0.12f, 0.14f));
            Widgets.DrawBoxSolid(rightRect, new Color(0.10f, 0.10f, 0.12f));
            DrawRimTalkPromptEntryList(leftRect.ContractedBy(6f), config);
            DrawRimTalkPromptEntryEditor(rightRect.ContractedBy(6f), config);
        }

        private void DrawRimTalkPromptEntryList(Rect rect, RimTalkChannelCompatConfig config)
        {
            const float buttonSize = 22f;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width - (buttonSize * 2f + 8f), 22f), "RimChat_RimTalkEntryListTitle".Translate());
            Rect duplicateRect = new Rect(rect.xMax - buttonSize, rect.y, buttonSize, buttonSize);
            Rect addRect = new Rect(duplicateRect.x - buttonSize - 4f, rect.y, buttonSize, buttonSize);
            bool dirty = false;
            if (Widgets.ButtonText(addRect, "+"))
            {
                var created = new RimTalkPromptEntryConfig
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "RimChat_RimTalkEntryDefaultName".Translate(),
                    Role = "System",
                    CustomRole = string.Empty,
                    Position = "Relative",
                    InChatDepth = 0,
                    Enabled = true,
                    Content = string.Empty
                };

                int insertIndex = config.PromptEntries.Count;
                if (!string.IsNullOrWhiteSpace(_rimTalkSelectedEntryId))
                {
                    int currentIndex = config.PromptEntries.FindIndex(entry =>
                        entry != null && string.Equals(entry.Id, _rimTalkSelectedEntryId, StringComparison.Ordinal));
                    if (currentIndex >= 0)
                    {
                        insertIndex = currentIndex + 1;
                    }
                }

                config.PromptEntries.Insert(insertIndex, created);
                _rimTalkSelectedEntryId = created.Id;
                _rimTalkDepthBuffer = created.InChatDepth.ToString();
                _rimTalkEntryListScroll = new Vector2(0f, Mathf.Max(0f, insertIndex * 25f - 40f));
                dirty = true;
            }

            RimTalkPromptEntryConfig selectedForDuplicate = GetSelectedRimTalkPromptEntry(config);
            if (selectedForDuplicate != null && Widgets.ButtonText(duplicateRect, "⧉"))
            {
                RimTalkPromptEntryConfig duplicated = selectedForDuplicate.Clone();
                duplicated.Id = Guid.NewGuid().ToString("N");
                duplicated.Name = NextPromptEntryName(config, selectedForDuplicate.Name);
                int selectedEntryIndex = config.PromptEntries.FindIndex(entry =>
                    entry != null && string.Equals(entry.Id, selectedForDuplicate.Id, StringComparison.Ordinal));
                int insertIndex = selectedEntryIndex >= 0 ? selectedEntryIndex + 1 : config.PromptEntries.Count;
                config.PromptEntries.Insert(insertIndex, duplicated);
                _rimTalkSelectedEntryId = duplicated.Id;
                _rimTalkDepthBuffer = duplicated.InChatDepth.ToString();
                _rimTalkEntryListScroll = new Vector2(0f, Mathf.Max(0f, insertIndex * 25f - 40f));
                dirty = true;
            }

            const float rowHeight = 25f;
            const float rowStep = 26f;
            Rect listRect = new Rect(rect.x, rect.y + 24f, rect.width, rect.height - 52f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, Mathf.Max(listRect.height, config.PromptEntries.Count * rowStep));
            float rowButtonX = viewRect.width - buttonSize - 2f;
            Widgets.BeginScrollView(listRect, ref _rimTalkEntryListScroll, viewRect);
            float rowY = 0f;
            for (int i = 0; i < config.PromptEntries.Count; i++)
            {
                RimTalkPromptEntryConfig entry = config.PromptEntries[i];
                if (entry == null)
                {
                    continue;
                }

                Rect rowRect = new Rect(0f, rowY, viewRect.width, rowHeight);
                bool isSelected = string.Equals(entry.Id, _rimTalkSelectedEntryId, StringComparison.Ordinal);
                if (isSelected)
                {
                    Widgets.DrawHighlight(rowRect);
                }
                else if (Mouse.IsOver(rowRect))
                {
                    Widgets.DrawBoxSolid(rowRect, new Color(0.18f, 0.18f, 0.2f));
                }

                bool enabled = entry.Enabled;
                Widgets.Checkbox(new Vector2(4f, rowY + 4f), ref enabled, 16f);
                if (enabled != entry.Enabled)
                {
                    entry.Enabled = enabled;
                    dirty = true;
                }

                Rect selectRect = new Rect(24f, rowY, viewRect.width - 24f - buttonSize - 6f, rowHeight);
                if (Widgets.ButtonInvisible(selectRect))
                {
                    _rimTalkSelectedEntryId = entry.Id;
                    _rimTalkDepthBuffer = entry.InChatDepth.ToString();
                }

                string title = string.IsNullOrWhiteSpace(entry.Name)
                    ? "RimChat_RimTalkEntryDefaultName".Translate().ToString()
                    : entry.Name;
                bool oldWordWrap = Text.WordWrap;
                Text.WordWrap = false;
                Rect titleRect = new Rect(24f, rowY + 1f, viewRect.width - 24f - buttonSize - 8f, rowHeight - 2f);
                Widgets.Label(titleRect, title.Truncate(titleRect.width));
                Text.WordWrap = oldWordWrap;

                Rect deleteRect = new Rect(rowButtonX, rowY + 2f, buttonSize, buttonSize);
                GUI.color = new Color(1f, 0.4f, 0.4f);
                if (Widgets.ButtonText(deleteRect, "×"))
                {
                    config.PromptEntries.RemoveAt(i);
                    i--;
                    rowY -= rowStep;
                    if (string.Equals(_rimTalkSelectedEntryId, entry.Id, StringComparison.Ordinal))
                    {
                        _rimTalkSelectedEntryId = config.PromptEntries.FirstOrDefault()?.Id ?? string.Empty;
                    }

                    dirty = true;
                    GUI.color = Color.white;
                    continue;
                }

                GUI.color = Color.white;
                string tip = title + "\n" + GetRimTalkRoleLabel(entry.Role) + " / " + GetRimTalkPositionLabel(entry.Position);
                TooltipHandler.TipRegion(rowRect, tip);
                rowY += rowStep;
            }

            Widgets.EndScrollView();

            RimTalkPromptEntryConfig selected = GetSelectedRimTalkPromptEntry(config);
            int selectedIndex = selected == null
                ? -1
                : config.PromptEntries.FindIndex(entry => entry != null && string.Equals(entry.Id, selected.Id, StringComparison.Ordinal));
            float buttonWidth = (rect.width - 4f) * 0.5f;
            Rect upRect = new Rect(rect.x, rect.yMax - 24f, buttonWidth, 24f);
            Rect downRect = new Rect(upRect.xMax + 4f, rect.yMax - 24f, buttonWidth, 24f);
            if (selectedIndex > 0)
            {
                if (Widgets.ButtonText(upRect, "▲"))
                {
                    RimTalkPromptEntryConfig item = config.PromptEntries[selectedIndex];
                    config.PromptEntries.RemoveAt(selectedIndex);
                    config.PromptEntries.Insert(selectedIndex - 1, item);
                    dirty = true;
                }
            }
            else
            {
                GUI.enabled = false;
                Widgets.ButtonText(upRect, "▲");
                GUI.enabled = true;
            }

            if (selectedIndex >= 0 && selectedIndex < config.PromptEntries.Count - 1)
            {
                if (Widgets.ButtonText(downRect, "▼"))
                {
                    RimTalkPromptEntryConfig item = config.PromptEntries[selectedIndex];
                    config.PromptEntries.RemoveAt(selectedIndex);
                    config.PromptEntries.Insert(selectedIndex + 1, item);
                    dirty = true;
                }
            }
            else
            {
                GUI.enabled = false;
                Widgets.ButtonText(downRect, "▼");
                GUI.enabled = true;
            }

            if (dirty)
            {
                SetRimTalkChannelConfig(_rimTalkEditorChannel, config);
                EnsureRimTalkEntrySelection(config);
            }
        }

        private void DrawRimTalkPromptEntryEditor(Rect rect, RimTalkChannelCompatConfig config)
        {
            RimTalkPromptEntryConfig entry = GetSelectedRimTalkPromptEntry(config);
            if (entry == null)
            {
                Widgets.Label(rect, "RimChat_RimTalkEntryNone".Translate());
                return;
            }

            bool dirty = false;
            float y = rect.y;
            float nameLabelWidth = Mathf.Clamp(Text.CalcSize("RimChat_RimTalkEntryName".Translate()).x + 8f, 72f, 140f);
            Widgets.Label(new Rect(rect.x, y, nameLabelWidth, 24f), "RimChat_RimTalkEntryName".Translate());
            string editedName = Widgets.TextField(new Rect(rect.x + nameLabelWidth + 4f, y, rect.width - nameLabelWidth - 4f, 24f), entry.Name ?? string.Empty);
            if (!string.Equals(editedName, entry.Name, StringComparison.Ordinal))
            {
                entry.Name = editedName;
                dirty = true;
            }

            y += 28f;
            bool enabled = entry.Enabled;
            float enabledWidth = Mathf.Clamp(rect.width * 0.34f, 140f, 180f);
            Widgets.CheckboxLabeled(new Rect(rect.x, y, enabledWidth, 24f), "RimChat_RimTalkCompatEnable".Translate(), ref enabled);
            if (enabled != entry.Enabled)
            {
                entry.Enabled = enabled;
                dirty = true;
            }

            float actionStart = rect.x + enabledWidth + 6f;
            float actionWidth = rect.xMax - actionStart;
            Rect roleRect;
            Rect positionRect;
            if (actionWidth >= 140f)
            {
                float roleWidth = Mathf.Max(58f, (actionWidth - 6f) * 0.5f);
                roleRect = new Rect(actionStart, y, roleWidth, 24f);
                positionRect = new Rect(roleRect.xMax + 6f, y, Mathf.Max(56f, rect.xMax - (roleRect.xMax + 6f)), 24f);
            }
            else
            {
                y += 28f;
                roleRect = new Rect(rect.x, y, rect.width, 24f);
                y += 28f;
                positionRect = new Rect(rect.x, y, rect.width, 24f);
            }

            if (Widgets.ButtonText(roleRect, "RimChat_RimTalkEntryRole".Translate() + ": " + GetRimTalkRoleLabel(entry.Role)))
            {
                ShowRimTalkRoleMenu(_rimTalkEditorChannel, entry.Id);
            }

            if (Widgets.ButtonText(positionRect, "RimChat_RimTalkEntryPosition".Translate() + ": " + GetRimTalkPositionLabel(entry.Position)))
            {
                ShowRimTalkPositionMenu(_rimTalkEditorChannel, entry.Id);
            }

            y += 28f;
            float customRoleLabelWidth = Mathf.Clamp(Text.CalcSize("RimChat_RimTalkEntryCustomRole".Translate()).x + 8f, 72f, 160f);
            Widgets.Label(new Rect(rect.x, y, customRoleLabelWidth, 24f), "RimChat_RimTalkEntryCustomRole".Translate());
            string customRole = Widgets.TextField(new Rect(rect.x + customRoleLabelWidth + 4f, y, rect.width - customRoleLabelWidth - 4f, 24f), entry.CustomRole ?? string.Empty);
            if (!string.Equals(customRole, entry.CustomRole, StringComparison.Ordinal))
            {
                entry.CustomRole = string.IsNullOrWhiteSpace(customRole) ? string.Empty : customRole.Trim();
                dirty = true;
            }

            y += 28f;
            if (string.Equals(entry.Position, "InChat", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(_rimTalkDepthBuffer))
                {
                    _rimTalkDepthBuffer = entry.InChatDepth.ToString();
                }

                float depthLabelWidth = Mathf.Clamp(Text.CalcSize("RimChat_RimTalkEntryDepth".Translate()).x + 8f, 72f, 140f);
                Widgets.Label(new Rect(rect.x, y, depthLabelWidth, 24f), "RimChat_RimTalkEntryDepth".Translate());
                _rimTalkDepthBuffer = Widgets.TextField(new Rect(rect.x + depthLabelWidth + 4f, y, 64f, 24f), _rimTalkDepthBuffer ?? "0");
                if (int.TryParse(_rimTalkDepthBuffer, out int depth))
                {
                    int clamped = Mathf.Clamp(depth, 0, 32);
                    if (clamped != entry.InChatDepth)
                    {
                        entry.InChatDepth = clamped;
                        dirty = true;
                    }
                }

                y += 28f;
            }

            Widgets.Label(new Rect(rect.x, y, rect.width, 22f), "RimChat_RimTalkEntryContent".Translate());
            y += 22f;
            Rect contentRect = new Rect(rect.x, y, rect.width, rect.yMax - y);
            float contentHeight = Mathf.Max(contentRect.height, Text.CalcHeight(entry.Content ?? string.Empty, contentRect.width - 16f) + 10f);
            Rect viewRect = new Rect(0f, 0f, contentRect.width - 16f, contentHeight);
            _rimTalkEntryContentScroll = GUI.BeginScrollView(contentRect, _rimTalkEntryContentScroll, viewRect);
            string editedContent = GUI.TextArea(viewRect, entry.Content ?? string.Empty);
            GUI.EndScrollView();
            if (!string.Equals(editedContent, entry.Content, StringComparison.Ordinal))
            {
                entry.Content = editedContent;
                dirty = true;
            }

            if (dirty)
            {
                SetRimTalkChannelConfig(_rimTalkEditorChannel, config);
            }
        }

        private static string NextPromptEntryName(RimTalkChannelCompatConfig config, string baseName)
        {
            string stem = string.IsNullOrWhiteSpace(baseName)
                ? "RimChat_RimTalkEntryDefaultName".Translate().ToString()
                : baseName.Trim();
            int suffix = 2;
            string candidate = stem + " Copy";
            while (config?.PromptEntries?.Any(entry =>
                       entry != null && string.Equals(entry.Name, candidate, StringComparison.OrdinalIgnoreCase)) == true)
            {
                candidate = $"{stem} Copy {suffix}";
                suffix++;
            }

            return candidate;
        }

        private void EnsureRimTalkEntrySelection(RimTalkChannelCompatConfig config)
        {
            config.NormalizeWith(RimTalkChannelCompatConfig.CreateDefault());
            if (config.PromptEntries == null || config.PromptEntries.Count == 0)
            {
                _rimTalkSelectedEntryId = string.Empty;
                _rimTalkDepthBuffer = string.Empty;
                return;
            }

            if (config.PromptEntries.Any(entry => string.Equals(entry?.Id, _rimTalkSelectedEntryId, StringComparison.Ordinal)))
            {
                return;
            }

            RimTalkPromptEntryConfig first = config.PromptEntries.FirstOrDefault(entry => entry != null);
            _rimTalkSelectedEntryId = first?.Id ?? string.Empty;
            _rimTalkDepthBuffer = first?.InChatDepth.ToString() ?? string.Empty;
        }

        private RimTalkPromptEntryConfig GetSelectedRimTalkPromptEntry(RimTalkChannelCompatConfig config)
        {
            if (config?.PromptEntries == null)
            {
                return null;
            }

            EnsureRimTalkEntrySelection(config);
            return config.PromptEntries.FirstOrDefault(entry =>
                entry != null && string.Equals(entry.Id, _rimTalkSelectedEntryId, StringComparison.Ordinal));
        }

        private void ShowRimTalkRoleMenu(RimTalkPromptChannel channel, string entryId)
        {
            List<FloatMenuOption> options = RimTalkEntryRoles
                .Select(role => new FloatMenuOption(GetRimTalkRoleLabel(role), () =>
                {
                    TryUpdatePromptEntryById(channel, entryId, selected => selected.Role = role);
                }))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ShowRimTalkPositionMenu(RimTalkPromptChannel channel, string entryId)
        {
            List<FloatMenuOption> options = RimTalkEntryPositions
                .Select(position => new FloatMenuOption(GetRimTalkPositionLabel(position), () =>
                {
                    TryUpdatePromptEntryById(channel, entryId, selected =>
                    {
                        selected.Position = position;
                        if (!string.Equals(position, "InChat", StringComparison.OrdinalIgnoreCase))
                        {
                            selected.InChatDepth = 0;
                        }
                    });
                }))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private bool TryUpdatePromptEntryById(
            RimTalkPromptChannel channel,
            string entryId,
            Action<RimTalkPromptEntryConfig> updateAction)
        {
            if (string.IsNullOrWhiteSpace(entryId))
            {
                return false;
            }

            RimTalkChannelCompatConfig config = GetRimTalkChannelConfigClone(channel);
            RimTalkPromptEntryConfig selected = config?.PromptEntries?.FirstOrDefault(entry =>
                entry != null && string.Equals(entry.Id, entryId, StringComparison.Ordinal));
            if (selected == null)
            {
                return false;
            }

            updateAction?.Invoke(selected);
            SetRimTalkChannelConfig(channel, config);

            if (channel == _rimTalkEditorChannel)
            {
                _rimTalkSelectedEntryId = selected.Id;
                _rimTalkDepthBuffer = selected.InChatDepth.ToString();
            }

            return true;
        }

        private static string GetRimTalkRoleLabel(string role)
        {
            if (string.Equals(role, "User", StringComparison.OrdinalIgnoreCase))
            {
                return "RimChat_RimTalkEntryRoleUser".Translate();
            }

            if (string.Equals(role, "Assistant", StringComparison.OrdinalIgnoreCase))
            {
                return "RimChat_RimTalkEntryRoleAssistant".Translate();
            }

            if (!string.IsNullOrWhiteSpace(role) &&
                !string.Equals(role, "System", StringComparison.OrdinalIgnoreCase))
            {
                return role.Trim();
            }

            return "RimChat_RimTalkEntryRoleSystem".Translate();
        }

        private static string GetRimTalkPositionLabel(string position)
        {
            return string.Equals(position, "InChat", StringComparison.OrdinalIgnoreCase)
                ? "RimChat_RimTalkEntryPositionInChat".Translate()
                : "RimChat_RimTalkEntryPositionRelative".Translate();
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

        private void AppendVariableToCurrentRimTalkTemplate(string variableName)
        {
            string normalizedName = variableName?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return;
            }

            RimTalkChannelCompatConfig config = GetRimTalkChannelConfigClone(_rimTalkEditorChannel);
            string token = "{{ " + normalizedName + " }}";
            RimTalkPromptEntryConfig entry = GetSelectedRimTalkPromptEntry(config);
            if (entry != null)
            {
                string currentEntry = entry.Content ?? string.Empty;
                if (ContainsVariableToken(currentEntry, normalizedName))
                {
                    Messages.Message("RimChat_RimTalkVariableAlreadyInTemplate".Translate(), MessageTypeDefOf.NeutralEvent, false);
                    return;
                }

                if (!TryInsertVariableIntoFocusedEditor(ref currentEntry, normalizedName))
                {
                    currentEntry = string.IsNullOrWhiteSpace(currentEntry)
                        ? token
                        : currentEntry.TrimEnd() + "\n" + token;
                }

                entry.Content = currentEntry;
                SetRimTalkChannelConfig(_rimTalkEditorChannel, config);
                Messages.Message("RimChat_RimTalkVariableInserted".Translate(token), MessageTypeDefOf.NeutralEvent, false);
                return;
            }

            string current = config.CompatTemplate ?? string.Empty;
            if (ContainsVariableToken(current, normalizedName))
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

        private static bool ContainsVariableToken(string text, string variableName)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(variableName))
            {
                return false;
            }

            string normalized = variableName.Trim();
            string[] patterns =
            {
                "{{" + normalized + "}}",
                "{{ " + normalized + "}}",
                "{{" + normalized + " }}",
                "{{ " + normalized + " }}"
            };

            for (int i = 0; i < patterns.Length; i++)
            {
                if (text.IndexOf(patterns[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryInsertVariableIntoFocusedEditor(ref string content, string variableName)
        {
            TextEditor editor = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
            string text = content ?? string.Empty;
            if (editor == null || editor.cursorIndex < 0 || editor.cursorIndex > text.Length)
            {
                return false;
            }

            int cursor = editor.cursorIndex;
            int prefixStart = cursor - 1;
            while (prefixStart >= 0 && (char.IsLetterOrDigit(text[prefixStart]) || text[prefixStart] == '.' || text[prefixStart] == '_'))
            {
                prefixStart--;
            }

            prefixStart++;
            if (prefixStart < cursor)
            {
                string prefix = text.Substring(prefixStart, cursor - prefixStart);
                if (variableName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    text = text.Remove(prefixStart, cursor - prefixStart);
                    cursor = prefixStart;
                }
            }

            int left = cursor - 1;
            while (left >= 0 && char.IsWhiteSpace(text[left]))
            {
                left--;
            }

            bool insideOpenToken = left >= 1 && text[left] == '{' && text[left - 1] == '{';
            string insert = insideOpenToken ? variableName : "{{ " + variableName + " }}";
            if (insideOpenToken)
            {
                if (cursor > 0 && text[cursor - 1] == '{')
                {
                    insert = " " + insert;
                }

                int right = cursor;
                while (right < text.Length && char.IsWhiteSpace(text[right]))
                {
                    right++;
                }

                bool hasClosing = right < text.Length - 1 && text[right] == '}' && text[right + 1] == '}';
                if (!hasClosing)
                {
                    insert += " }}";
                }
            }

            text = text.Insert(cursor, insert);
            int newCursor = cursor + insert.Length;
            if (insideOpenToken)
            {
                int close = text.IndexOf("}}", cursor, StringComparison.Ordinal);
                newCursor = close >= 0 ? close + 2 : newCursor;
            }

            editor.text = text;
            editor.cursorIndex = newCursor;
            editor.selectIndex = newCursor;
            content = text;
            return true;
        }

        private string GetCurrentChannelToken()
        {
            return _rimTalkEditorChannel == RimTalkPromptChannel.Diplomacy ? "diplomacy" : "rpg";
        }
    }
}
