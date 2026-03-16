using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.DiplomacySystem;
using RimChat.Persistence;
using RimChat.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: PromptPersistenceService validation API, RPG manager persona sync API, and settings UI widgets.
    /// Responsibility: render dedicated RimTalk tab with per-channel settings, strict template diagnostics, persona copy controls, and variable insertion tools.
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
        private string _rimTalkEntryContentBuffer = string.Empty;
        private string _rimTalkEntryContentBufferEntryId = string.Empty;
        private string _rimTalkEntryContentSnapshot = string.Empty;
        private PromptWorkbenchChipEditor _workbenchChipEditor;
        private bool _workbenchChipEditorDisabledForSession;
        private const int ChipEditorContentLengthSoftLimit = 24000;
        private const int ChipEditorTokenCountSoftLimit = 320;
        private static readonly string[] RimTalkEntryRoles = { "System", "User", "Assistant" };
        private static readonly string[] RimTalkEntryPositions = { "Relative", "InChat" };

        private void DrawTab_RimTalk(Rect rect)
        {
            DrawRimTalkBridgePage(rect);
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
            GUI.color = Color.gray;
            listing.Label("RimChat_RimTalkCompatEnableHint".Translate());
            GUI.color = Color.white;

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
            DrawRimTalkPromptEntryEditor(rightRect.ContractedBy(6f), config, useChipEditor: false);
        }

        private void DrawRimTalkPromptEntryList(Rect rect, RimTalkChannelCompatConfig config)
        {
            const float buttonSize = 22f;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width - (buttonSize * 3f + 12f), 22f), "RimChat_RimTalkEntryListTitle".Translate());
            Rect duplicateRect = new Rect(rect.xMax - buttonSize, rect.y, buttonSize, buttonSize);
            Rect addRect = new Rect(duplicateRect.x - buttonSize - 4f, rect.y, buttonSize, buttonSize);
            Rect restoreRect = new Rect(addRect.x - buttonSize - 4f, rect.y, buttonSize, buttonSize);
            string scopedPromptChannel = GetScopedPromptChannelOrEmpty();
            List<int> visibleIndices = CollectVisiblePromptEntryIndices(config, scopedPromptChannel);
            EnsureSelectedEntryInVisibleScope(config, visibleIndices);
            bool dirty = false;
            if (Widgets.ButtonText(restoreRect, "↺"))
            {
                if (TryRestoreDefaultEntriesForScopedChannel(config, scopedPromptChannel))
                {
                    visibleIndices = CollectVisiblePromptEntryIndices(config, scopedPromptChannel);
                    EnsureSelectedEntryInVisibleScope(config, visibleIndices);
                    dirty = true;
                }
            }

            TooltipHandler.TipRegion(restoreRect, "RimChat_RimTalkEntryRestoreDefaultsTooltip".Translate());
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
                    PromptChannel = ResolveEntryCreationPromptChannel(scopedPromptChannel),
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
                else if (visibleIndices.Count > 0)
                {
                    insertIndex = visibleIndices[visibleIndices.Count - 1] + 1;
                }

                config.PromptEntries.Insert(insertIndex, created);
                _rimTalkSelectedEntryId = created.Id;
                _rimTalkDepthBuffer = created.InChatDepth.ToString();
                visibleIndices = CollectVisiblePromptEntryIndices(config, scopedPromptChannel);
                int visibleInsertIndex = FindVisibleIndexByEntryId(config, visibleIndices, created.Id);
                _rimTalkEntryListScroll = new Vector2(0f, Mathf.Max(0f, visibleInsertIndex * 25f - 40f));
                dirty = true;
            }

            TooltipHandler.TipRegion(addRect, "RimChat_RimTalkEntryAddTooltip".Translate());
            int selectedFullIndex = config.PromptEntries.FindIndex(entry =>
                entry != null && string.Equals(entry.Id, _rimTalkSelectedEntryId, StringComparison.Ordinal));
            bool hasVisibleSelection = selectedFullIndex >= 0 && visibleIndices.Contains(selectedFullIndex);
            RimTalkPromptEntryConfig selectedForDuplicate = hasVisibleSelection ? config.PromptEntries[selectedFullIndex] : null;
            if (hasVisibleSelection && selectedForDuplicate != null && Widgets.ButtonText(duplicateRect, "C"))
            {
                RimTalkPromptEntryConfig duplicated = selectedForDuplicate.Clone();
                duplicated.Id = Guid.NewGuid().ToString("N");
                duplicated.SectionId = string.Empty;
                duplicated.Name = NextPromptEntryName(config, selectedForDuplicate.Name);
                duplicated.PromptChannel = ResolveEntryCreationPromptChannel(scopedPromptChannel);
                int selectedEntryIndex = selectedFullIndex;
                int insertIndex = selectedEntryIndex >= 0 ? selectedEntryIndex + 1 : config.PromptEntries.Count;
                config.PromptEntries.Insert(insertIndex, duplicated);
                _rimTalkSelectedEntryId = duplicated.Id;
                _rimTalkDepthBuffer = duplicated.InChatDepth.ToString();
                visibleIndices = CollectVisiblePromptEntryIndices(config, scopedPromptChannel);
                int visibleInsertIndex = FindVisibleIndexByEntryId(config, visibleIndices, duplicated.Id);
                _rimTalkEntryListScroll = new Vector2(0f, Mathf.Max(0f, visibleInsertIndex * 25f - 40f));
                dirty = true;
            }

            TooltipHandler.TipRegion(duplicateRect, "RimChat_RimTalkEntryDuplicateTooltip".Translate());
            const float rowHeight = 25f;
            const float rowStep = 26f;
            Rect listRect = new Rect(rect.x, rect.y + 24f, rect.width, rect.height - 52f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, Mathf.Max(listRect.height, visibleIndices.Count * rowStep));
            float rowButtonX = viewRect.width - buttonSize - 2f;
            Widgets.BeginScrollView(listRect, ref _rimTalkEntryListScroll, viewRect);
            float rowY = 0f;
            for (int i = 0; i < visibleIndices.Count; i++)
            {
                int entryIndex = visibleIndices[i];
                RimTalkPromptEntryConfig entry = config.PromptEntries[entryIndex];
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
                string channelLabel = GetRimTalkPromptChannelLabel(entry.PromptChannel);
                string rowText = $"{title} [{channelLabel}]";
                bool oldWordWrap = Text.WordWrap;
                Text.WordWrap = false;
                Rect titleRect = new Rect(24f, rowY + 1f, viewRect.width - 24f - buttonSize - 8f, rowHeight - 2f);
                Widgets.Label(titleRect, rowText.Truncate(titleRect.width));
                Text.WordWrap = oldWordWrap;

                Rect deleteRect = new Rect(rowButtonX, rowY + 2f, buttonSize, buttonSize);
                bool canDeleteEntry = !IsDefaultPromptEntry(entry);
                if (canDeleteEntry)
                {
                    GUI.color = new Color(1f, 0.4f, 0.4f);
                    if (Widgets.ButtonText(deleteRect, "×"))
                    {
                        bool deletingSelected = string.Equals(_rimTalkSelectedEntryId, entry.Id, StringComparison.Ordinal);
                        config.PromptEntries.RemoveAt(entryIndex);
                        visibleIndices = CollectVisiblePromptEntryIndices(config, scopedPromptChannel);
                        if (deletingSelected)
                        {
                            EnsureSelectedEntryInVisibleScope(config, visibleIndices);
                        }

                        dirty = true;
                        GUI.color = Color.white;
                        continue;
                    }
                }

                GUI.color = Color.white;
                string tip = title + "\n" + channelLabel;
                TooltipHandler.TipRegion(rowRect, tip);
                rowY += rowStep;
            }

            Widgets.EndScrollView();

            selectedFullIndex = config.PromptEntries.FindIndex(entry =>
                entry != null && string.Equals(entry.Id, _rimTalkSelectedEntryId, StringComparison.Ordinal));
            int selectedVisibleIndex = selectedFullIndex >= 0 ? visibleIndices.IndexOf(selectedFullIndex) : -1;
            float buttonWidth = (rect.width - 4f) * 0.5f;
            Rect upRect = new Rect(rect.x, rect.yMax - 24f, buttonWidth, 24f);
            Rect downRect = new Rect(upRect.xMax + 4f, rect.yMax - 24f, buttonWidth, 24f);
            if (selectedVisibleIndex > 0)
            {
                if (Widgets.ButtonText(upRect, "▲"))
                {
                    int currentIndex = visibleIndices[selectedVisibleIndex];
                    int targetIndex = visibleIndices[selectedVisibleIndex - 1];
                    RimTalkPromptEntryConfig item = config.PromptEntries[currentIndex];
                    config.PromptEntries.RemoveAt(currentIndex);
                    if (targetIndex > currentIndex)
                    {
                        targetIndex--;
                    }

                    config.PromptEntries.Insert(targetIndex, item);
                    dirty = true;
                }
            }
            else
            {
                GUI.enabled = false;
                Widgets.ButtonText(upRect, "▲");
                GUI.enabled = true;
            }

            if (selectedVisibleIndex >= 0 && selectedVisibleIndex < visibleIndices.Count - 1)
            {
                if (Widgets.ButtonText(downRect, "▼"))
                {
                    int currentIndex = visibleIndices[selectedVisibleIndex];
                    int targetIndex = visibleIndices[selectedVisibleIndex + 1];
                    RimTalkPromptEntryConfig item = config.PromptEntries[currentIndex];
                    config.PromptEntries.RemoveAt(currentIndex);
                    if (targetIndex > currentIndex)
                    {
                        targetIndex--;
                    }

                    int insertIndex = Mathf.Min(config.PromptEntries.Count, targetIndex + 1);
                    config.PromptEntries.Insert(insertIndex, item);
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

        private static int FindVisibleIndexByEntryId(
            RimTalkChannelCompatConfig config,
            IReadOnlyList<int> visibleIndices,
            string entryId)
        {
            if (config?.PromptEntries == null || visibleIndices == null || string.IsNullOrWhiteSpace(entryId))
            {
                return 0;
            }

            for (int i = 0; i < visibleIndices.Count; i++)
            {
                int index = visibleIndices[i];
                RimTalkPromptEntryConfig entry = config.PromptEntries[index];
                if (entry != null && string.Equals(entry.Id, entryId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return 0;
        }

        private string GetScopedPromptChannelOrEmpty()
        {
            if (!IsEntryDrivenWorkbenchChannelActive())
            {
                return string.Empty;
            }

            string selected = EnsureWorkbenchPromptChannelSelection();
            return RimTalkPromptEntryChannelCatalog.NormalizeForRoot(selected, _rimTalkEditorChannel);
        }

        private List<int> CollectVisiblePromptEntryIndices(RimTalkChannelCompatConfig config, string scopedPromptChannel)
        {
            var result = new List<int>();
            if (config?.PromptEntries == null)
            {
                return result;
            }

            bool scoped = !string.IsNullOrWhiteSpace(scopedPromptChannel);
            for (int i = 0; i < config.PromptEntries.Count; i++)
            {
                RimTalkPromptEntryConfig entry = config.PromptEntries[i];
                if (entry == null)
                {
                    continue;
                }

                if (scoped)
                {
                    string normalizedEntryChannel = RimTalkPromptEntryChannelCatalog.NormalizeForRoot(entry.PromptChannel, _rimTalkEditorChannel);
                    if (!string.Equals(normalizedEntryChannel, scopedPromptChannel, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                result.Add(i);
            }

            return result;
        }

        private void EnsureSelectedEntryInVisibleScope(RimTalkChannelCompatConfig config, IReadOnlyList<int> visibleIndices)
        {
            if (config?.PromptEntries == null)
            {
                _rimTalkSelectedEntryId = string.Empty;
                _rimTalkDepthBuffer = string.Empty;
                return;
            }

            if (!string.IsNullOrWhiteSpace(_rimTalkSelectedEntryId))
            {
                for (int i = 0; i < visibleIndices.Count; i++)
                {
                    RimTalkPromptEntryConfig current = config.PromptEntries[visibleIndices[i]];
                    if (current != null && string.Equals(current.Id, _rimTalkSelectedEntryId, StringComparison.Ordinal))
                    {
                        return;
                    }
                }
            }

            if (visibleIndices.Count == 0)
            {
                _rimTalkSelectedEntryId = string.Empty;
                _rimTalkDepthBuffer = string.Empty;
                return;
            }

            RimTalkPromptEntryConfig first = config.PromptEntries[visibleIndices[0]];
            _rimTalkSelectedEntryId = first?.Id ?? string.Empty;
            _rimTalkDepthBuffer = first?.InChatDepth.ToString() ?? string.Empty;
        }

        private string ResolveEntryCreationPromptChannel(string scopedPromptChannel)
        {
            if (!string.IsNullOrWhiteSpace(scopedPromptChannel))
            {
                return scopedPromptChannel;
            }

            return RimTalkPromptEntryChannelCatalog.GetDefaultChannel(_rimTalkEditorChannel);
        }

        private void DrawRimTalkPromptEntryEditor(Rect rect, RimTalkChannelCompatConfig config, bool useChipEditor = false)
        {
            EnsureSelectedEntryInVisibleScope(config, CollectVisiblePromptEntryIndices(config, GetScopedPromptChannelOrEmpty()));
            RimTalkPromptEntryConfig entry = EnsureRimTalkEditableEntry(config);
            if (entry == null)
            {
                ResetRimTalkEntryContentBuffer();
                Widgets.Label(rect, "RimChat_RimTalkEntryNone".Translate());
                return;
            }

            SyncRimTalkEntryContentBuffer(entry);
            bool dirty = false;
            string normalizedPromptChannel = RimTalkPromptEntryChannelCatalog.NormalizeForRoot(entry.PromptChannel, _rimTalkEditorChannel);
            if (!string.Equals(normalizedPromptChannel, entry.PromptChannel, StringComparison.OrdinalIgnoreCase))
            {
                entry.PromptChannel = normalizedPromptChannel;
                dirty = true;
            }
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
            const float validationStatusHeight = 24f;
            const float validationGap = 2f;
            float contentAreaHeight = Mathf.Max(24f, rect.yMax - y - validationStatusHeight - validationGap);
            Rect contentRect = new Rect(rect.x, y, rect.width, contentAreaHeight);
            string bufferedContent = _rimTalkEntryContentBuffer ?? string.Empty;
            string editedContent = DrawPromptEntryContentEditor(contentRect, bufferedContent, useChipEditor);
            Rect validationRect = new Rect(rect.x, contentRect.yMax + validationGap, rect.width, validationStatusHeight);
            DrawRimTalkTemplateValidationStatus(validationRect, editedContent);
            if (!string.Equals(editedContent, bufferedContent, StringComparison.Ordinal))
            {
                _rimTalkEntryContentBuffer = editedContent;
            }

            if (!string.Equals(_rimTalkEntryContentBuffer, entry.Content ?? string.Empty, StringComparison.Ordinal))
            {
                entry.Content = _rimTalkEntryContentBuffer;
                dirty = true;
            }

            if (dirty)
            {
                SetRimTalkChannelConfig(_rimTalkEditorChannel, config);
            }

            _rimTalkEntryContentSnapshot = entry.Content ?? string.Empty;
        }

        private string DrawPromptEntryContentEditor(Rect contentRect, string text, bool useChipEditor)
        {
            if (!useChipEditor || _workbenchChipEditorDisabledForSession || ExceedsChipEditorSoftLimits(text))
            {
                return DrawLegacyPromptEntryTextArea(contentRect, text);
            }

            try
            {
                _workbenchChipEditor ??= new PromptWorkbenchChipEditor("RimChat_WorkbenchPromptEntryContentEditor");
                return _workbenchChipEditor.Draw(contentRect, text, ref _rimTalkEntryContentScroll);
            }
            catch (Exception ex)
            {
                _workbenchChipEditorDisabledForSession = true;
                Log.Warning($"[RimChat] Prompt workbench chip editor fallback activated: {ex.GetType().Name}: {ex.Message}");
                return DrawLegacyPromptEntryTextArea(contentRect, text);
            }
        }

        private static bool ExceedsChipEditorSoftLimits(string text)
        {
            string content = text ?? string.Empty;
            if (content.Length > ChipEditorContentLengthSoftLimit)
            {
                return true;
            }

            int markers = CountTokenMarkers(content);
            return markers > ChipEditorTokenCountSoftLimit;
        }

        private static int CountTokenMarkers(string text)
        {
            int count = 0;
            for (int i = 0; i < text.Length - 1; i++)
            {
                if (text[i] == '{' && text[i + 1] == '{')
                {
                    count++;
                    i++;
                }
            }

            return count;
        }

        private string DrawLegacyPromptEntryTextArea(Rect contentRect, string text)
        {
            string source = text ?? string.Empty;
            var textAreaStyle = new GUIStyle(GUI.skin.textArea)
            {
                wordWrap = true,
                richText = false
            };
            float contentWidth = Mathf.Max(1f, contentRect.width - 16f);
            float contentHeight = Mathf.Max(contentRect.height, textAreaStyle.CalcHeight(new GUIContent(source), contentWidth) + 4f);
            Rect viewRect = new Rect(0f, 0f, contentWidth, contentHeight);
            _rimTalkEntryContentScroll = new Vector2(
                0f,
                Mathf.Clamp(_rimTalkEntryContentScroll.y, 0f, Mathf.Max(0f, viewRect.height - contentRect.height)));
            _rimTalkEntryContentScroll = GUI.BeginScrollView(contentRect, _rimTalkEntryContentScroll, viewRect, false, true);
            string editedContent = GUI.TextArea(new Rect(0f, 0f, contentWidth, contentHeight), source, textAreaStyle);
            GUI.EndScrollView();
            return editedContent;
        }

        private RimTalkPromptEntryConfig EnsureRimTalkEditableEntry(RimTalkChannelCompatConfig config)
        {
            RimTalkPromptEntryConfig entry = GetSelectedRimTalkPromptEntry(config);
            if (entry != null)
            {
                return entry;
            }

            if (config == null)
            {
                return null;
            }

            config.PromptEntries ??= new List<RimTalkPromptEntryConfig>();
            var created = new RimTalkPromptEntryConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "RimChat_RimTalkEntryDefaultName".Translate(),
                Role = "System",
                CustomRole = string.Empty,
                Position = "Relative",
                InChatDepth = 0,
                Enabled = true,
                PromptChannel = ResolveEntryCreationPromptChannel(GetScopedPromptChannelOrEmpty()),
                Content = string.Empty
            };
            config.PromptEntries.Add(created);
            _rimTalkSelectedEntryId = created.Id;
            _rimTalkDepthBuffer = created.InChatDepth.ToString();
            SetRimTalkChannelConfig(_rimTalkEditorChannel, config);
            return created;
        }

        private void SyncRimTalkEntryContentBuffer(RimTalkPromptEntryConfig entry)
        {
            string entryId = entry?.Id ?? string.Empty;
            string entryContent = entry?.Content ?? string.Empty;
            bool switchedEntry = !string.Equals(_rimTalkEntryContentBufferEntryId, entryId, StringComparison.Ordinal);
            bool externallyUpdated = !switchedEntry &&
                                     !string.Equals(_rimTalkEntryContentSnapshot, entryContent, StringComparison.Ordinal) &&
                                     !string.Equals(_rimTalkEntryContentBuffer, entryContent, StringComparison.Ordinal);
            if (switchedEntry || externallyUpdated)
            {
                _rimTalkEntryContentBufferEntryId = entryId;
                _rimTalkEntryContentBuffer = entryContent;
                if (switchedEntry)
                {
                    _rimTalkEntryContentScroll = Vector2.zero;
                }
            }

            _rimTalkEntryContentSnapshot = entryContent;
        }

        private void ResetRimTalkEntryContentBuffer()
        {
            _rimTalkEntryContentBuffer = string.Empty;
            _rimTalkEntryContentBufferEntryId = string.Empty;
            _rimTalkEntryContentSnapshot = string.Empty;
            _rimTalkEntryContentScroll = Vector2.zero;
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

        private void ShowRimTalkPromptChannelMenu(RimTalkPromptChannel channel, string entryId)
        {
            IReadOnlyList<string> selectableChannels = RimTalkPromptEntryChannelCatalog.GetSelectableChannels(channel);
            List<FloatMenuOption> options = selectableChannels
                .Select(channelId => new FloatMenuOption(
                    GetRimTalkPromptChannelLabel(channelId),
                    () =>
                {
                    TryUpdatePromptEntryById(channel, entryId, selected =>
                    {
                        selected.PromptChannel = channelId;
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

        private static bool IsDefaultPromptEntry(RimTalkPromptEntryConfig entry)
        {
            if (entry == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(entry.SectionId);
        }

        private bool TryRestoreDefaultEntriesForScopedChannel(RimTalkChannelCompatConfig config, string scopedPromptChannel)
        {
            if (config == null)
            {
                return false;
            }

            string normalizedPromptChannel = string.IsNullOrWhiteSpace(scopedPromptChannel)
                ? RimTalkPromptEntryChannelCatalog.GetDefaultChannel(_rimTalkEditorChannel)
                : RimTalkPromptEntryChannelCatalog.NormalizeForRoot(scopedPromptChannel, _rimTalkEditorChannel);
            config.PromptEntries ??= new List<RimTalkPromptEntryConfig>();

            List<RimTalkPromptEntryConfig> restored = BuildDefaultSectionEntriesForChannel(normalizedPromptChannel);
            if (restored == null || restored.Count == 0)
            {
                return false;
            }

            ReplacePromptChannelEntries(config.PromptEntries, normalizedPromptChannel, restored);
            RimTalkPromptEntryConfig first = restored[0];
            _rimTalkSelectedEntryId = first?.Id ?? string.Empty;
            _rimTalkDepthBuffer = first?.InChatDepth.ToString() ?? string.Empty;
            _rimTalkEntryListScroll = Vector2.zero;
            ResetRimTalkEntryContentBuffer();
            Messages.Message(
                "RimChat_RimTalkEntryRestoreDefaultsSuccess".Translate(GetRimTalkPromptChannelLabel(normalizedPromptChannel)),
                MessageTypeDefOf.NeutralEvent,
                false);
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

        private static string GetRimTalkPromptChannelLabel(string channelId)
        {
            return RimTalkPromptEntryChannelCatalog.GetLabel(channelId);
        }

        private void DrawRimTalkChannelTemplateTextArea(Rect rect, RimTalkChannelCompatConfig config)
        {
            string current = config?.CompatTemplate ?? string.Empty;
            const float validationStatusHeight = 24f;
            const float validationGap = 2f;
            Rect contentRect = new Rect(rect.x, rect.y, rect.width, Mathf.Max(24f, rect.height - validationStatusHeight - validationGap));
            float contentHeight = Mathf.Max(contentRect.height, Text.CalcHeight(current, contentRect.width - 16f) + 10f);
            Rect viewRect = new Rect(0f, 0f, contentRect.width - 16f, contentHeight);
            _rimTalkCompatTemplateScroll = GUI.BeginScrollView(contentRect, _rimTalkCompatTemplateScroll, viewRect);
            string edited = GUI.TextArea(viewRect, current);
            GUI.EndScrollView();
            Rect validationRect = new Rect(rect.x, contentRect.yMax + validationGap, rect.width, validationStatusHeight);
            DrawRimTalkTemplateValidationStatus(validationRect, edited);

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
            const float validationStatusHeight = 24f;
            const float validationGap = 2f;
            Rect rect = listing.GetRect(116f);
            Rect contentRect = new Rect(rect.x, rect.y, rect.width, Mathf.Max(24f, rect.height - validationStatusHeight - validationGap));
            float contentHeight = Mathf.Max(contentRect.height, Text.CalcHeight(current, contentRect.width - 16f) + 10f);
            Rect viewRect = new Rect(0f, 0f, contentRect.width - 16f, contentHeight);
            _rimTalkPersonaCopyTemplateScroll = GUI.BeginScrollView(contentRect, _rimTalkPersonaCopyTemplateScroll, viewRect);
            string edited = GUI.TextArea(viewRect, current);
            GUI.EndScrollView();
            Rect validationRect = new Rect(rect.x, contentRect.yMax + validationGap, rect.width, validationStatusHeight);
            DrawRimTalkTemplateValidationStatus(validationRect, edited);

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

        private static void DrawRimTalkTemplateValidationStatus(Rect rect, string templateText)
        {
            TemplateVariableValidationResult result = string.IsNullOrWhiteSpace(templateText)
                ? new TemplateVariableValidationResult()
                : PromptPersistenceService.Instance.ValidateTemplateVariables(templateText);
            string statusText = BuildLiveValidationStatusText(result, templateText);
            Color oldColor = GUI.color;
            GUI.color = ResolveLiveValidationStatusColor(result, templateText);
            Widgets.Label(rect, statusText);
            GUI.color = oldColor;
        }

        private void AppendVariableToCurrentRimTalkTemplate(string variableName)
        {
            string normalizedName = variableName?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return;
            }

            if (TryInsertVariableTokenToPromptWorkspace("{{ " + normalizedName + " }}"))
            {
                return;
            }

            RimTalkChannelCompatConfig config = IsEntryDrivenWorkbenchChannelActive()
                ? GetWorkbenchEditingChannelConfig()
                : GetRimTalkChannelConfigClone(_rimTalkEditorChannel);
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
                _rimTalkEntryContentBufferEntryId = entry.Id ?? string.Empty;
                _rimTalkEntryContentBuffer = currentEntry;
                _rimTalkEntryContentSnapshot = currentEntry;
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
            else
            {
                insert = AddTokenSpacing(text, cursor, insert);
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

        private static string AddTokenSpacing(string text, int cursor, string token)
        {
            string insert = token ?? string.Empty;
            if (NeedsLeadingSpace(text, cursor))
            {
                insert = " " + insert;
            }

            if (NeedsTrailingSpace(text, cursor))
            {
                insert += " ";
            }

            return insert;
        }

        private static bool NeedsLeadingSpace(string text, int cursor)
        {
            return cursor > 0 && !char.IsWhiteSpace(text[cursor - 1]);
        }

        private static bool NeedsTrailingSpace(string text, int cursor)
        {
            return cursor < (text?.Length ?? 0) && !char.IsWhiteSpace(text[cursor]);
        }

        private string GetCurrentChannelToken()
        {
            return _rimTalkEditorChannel == RimTalkPromptChannel.Diplomacy ? "diplomacy" : "rpg";
        }
    }
}
