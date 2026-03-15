using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Persistence;
using RimChat.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    public partial class RimChatSettings
    {
        private enum PromptWorkbenchChannel
        {
            Diplomacy = 0,
            Rpg = 1
        }

        private enum PromptWorkbenchInfoPanel
        {
            Preview = 0,
            Variables = 1,
            Help = 2
        }

        private IPromptPresetService _promptPresetService;
        private PromptPresetStoreConfig _promptPresetStore;
        private string _selectedPromptPresetId = string.Empty;
        private string _presetRenameBuffer = string.Empty;
        private Vector2 _promptPresetScroll = Vector2.zero;
        private PromptWorkbenchChannel _workbenchChannel = PromptWorkbenchChannel.Diplomacy;
        private PromptWorkbenchInfoPanel _workbenchSidePanelTab = PromptWorkbenchInfoPanel.Preview;
        private string _workbenchVariableInsertToken = string.Empty;
        private string _workbenchHintSearch = string.Empty;
        private int _workbenchRpgSubTab;

        internal void FlushPromptEditorsToStorageForPreset()
        {
            SyncBuffersToData();
            SaveSystemPromptConfig();
            SaveRpgPromptTextsToCustom();
        }

        internal void RefreshPromptEditorStateFromStorage()
        {
            _systemPromptConfig = PromptPersistenceService.Instance.LoadConfig();
            EnsureRpgPromptTextsLoaded();
            SyncBuffersToData();
            _previewUpdateCooldown = 0;
            _rpgPreviewUpdateCooldown = 0;
        }

        private void DrawAdvancedPromptWorkbench(Listing_Standard listing)
        {
            DrawAdvancedPromptWorkbench(listing.GetRect(620f));
        }

        private void DrawAdvancedPromptWorkbench(Rect root)
        {
            EnsurePresetStoreReady();
            InitBuffers();
            EnsureRpgPromptTextsLoaded();
            ApplyWorkbenchEntryChannelSelection(_workbenchChannel);

            Widgets.DrawBoxSolid(root, new Color(0.08f, 0.09f, 0.11f));
            Rect frame = root.ContractedBy(8f);
            Rect headerRect = new Rect(frame.x, frame.y, frame.width, 78f);
            Rect bodyRect = new Rect(frame.x, headerRect.yMax + 6f, frame.width, frame.height - headerRect.height - 6f);

            DrawWorkbenchHeader(headerRect);
            DrawWorkbenchBody(bodyRect);
        }

        private void DrawWorkbenchHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.07f, 0.08f, 0.10f));
            Rect inner = rect.ContractedBy(8f);

            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(0.95f, 0.74f, 0.26f);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width * 0.45f, 28f), "RimChat_Tab_PromptWorkbench".Translate());
            GUI.color = Color.white;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;

            float tabsY = inner.y + 34f;
            float tabWidth = 160f;
            Rect diplomacyTab = new Rect(inner.x, tabsY, tabWidth, 30f);
            Rect rpgTab = new Rect(diplomacyTab.xMax + 6f, tabsY, tabWidth, 30f);
            DrawWorkbenchChannelButton(diplomacyTab, PromptWorkbenchChannel.Diplomacy, "RimChat_PromptWorkbench_ChannelDiplomacy");
            DrawWorkbenchChannelButton(rpgTab, PromptWorkbenchChannel.Rpg, "RimChat_PromptWorkbench_ChannelRpg");

            Rect importRect = new Rect(inner.xMax - 180f, tabsY, 84f, 30f);
            Rect exportRect = new Rect(inner.xMax - 90f, tabsY, 84f, 30f);
            if (Widgets.ButtonText(importRect, "RimChat_Import".Translate()))
            {
                ShowImportPresetDialog();
            }

            if (Widgets.ButtonText(exportRect, "RimChat_Export".Translate()))
            {
                ShowExportPresetDialog();
            }
        }

        private void DrawWorkbenchChannelButton(Rect rect, PromptWorkbenchChannel channel, string labelKey)
        {
            bool selected = _workbenchChannel == channel;
            Color baseColor = selected ? new Color(0.45f, 0.33f, 0.15f) : new Color(0.25f, 0.18f, 0.08f);
            Widgets.DrawBoxSolid(rect, baseColor);
            Widgets.DrawBox(rect, 1);
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = selected ? new Color(1f, 0.88f, 0.55f) : Color.white;
            Widgets.Label(rect, labelKey.Translate());
            GUI.color = Color.white;
            Text.Anchor = oldAnchor;

            if (Widgets.ButtonInvisible(rect))
            {
                if (_workbenchChannel != channel)
                {
                    _workbenchChannel = channel;
                    _workbenchRpgSubTab = 0;
                    _rimTalkSelectedEntryId = string.Empty;
                    ApplyWorkbenchEntryChannelSelection(_workbenchChannel);
                }
            }
        }

        private void DrawWorkbenchBody(Rect rect)
        {
            float gap = 8f;
            float leftWidth = Mathf.Clamp(rect.width * 0.27f, 280f, 340f);
            float rightWidth = Mathf.Clamp(rect.width * 0.24f, 240f, 320f);
            float centerWidth = rect.width - leftWidth - rightWidth - gap * 2f;
            Rect leftRect = new Rect(rect.x, rect.y, leftWidth, rect.height);
            Rect centerRect = new Rect(leftRect.xMax + gap, rect.y, centerWidth, rect.height);
            Rect rightRect = new Rect(centerRect.xMax + gap, rect.y, rightWidth, rect.height);

            DrawWorkbenchPresetPanel(leftRect);
            DrawWorkbenchMainPanel(centerRect);
            DrawWorkbenchSidePanelContainer(rightRect);
        }

        private void DrawWorkbenchPresetPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.09f, 0.10f, 0.12f));
            Rect inner = rect.ContractedBy(8f);
            float topHeight = Mathf.Clamp(inner.height * 0.42f, 250f, 320f);
            Rect presetRect = new Rect(inner.x, inner.y, inner.width, topHeight);
            Rect lowerRect = new Rect(inner.x, presetRect.yMax + 8f, inner.width, inner.height - topHeight - 8f);

            Widgets.Label(new Rect(presetRect.x, presetRect.y, presetRect.width, 22f), "RimChat_PromptWorkbench_PresetHeader".Translate());
            float y = presetRect.y + 24f;
            DrawPresetActions(new Rect(presetRect.x, y, presetRect.width, 24f));
            y += 28f;
            DrawPresetList(new Rect(presetRect.x, y, presetRect.width, 160f));
            y += 166f;
            DrawPresetBottomActions(new Rect(presetRect.x, y, presetRect.width, 54f));

            float actionHeight = 116f;
            float entryHeight = Mathf.Max(120f, lowerRect.height - actionHeight - 8f);
            Rect entryRect = new Rect(lowerRect.x, lowerRect.y, lowerRect.width, entryHeight);
            Rect actionRect = new Rect(lowerRect.x, entryRect.yMax + 8f, lowerRect.width, actionHeight);

            ApplyWorkbenchEntryChannelSelection(_workbenchChannel);
            RimTalkChannelCompatConfig config = GetRimTalkChannelConfigClone(_rimTalkEditorChannel);
            DrawRimTalkPromptEntryList(entryRect, config);
            DrawPromptActionButtonsVertical(actionRect);
        }

        private void DrawWorkbenchMainPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.06f, 0.07f, 0.09f));
            Rect inner = rect.ContractedBy(8f);
            float y = inner.y;

            DrawWorkbenchPresetNameRow(inner, ref y);

            if (_workbenchChannel == PromptWorkbenchChannel.Rpg)
            {
                DrawRpgWorkbenchSubTabs(inner, ref y);
            }

            Rect contentRect = new Rect(inner.x, y, inner.width, inner.yMax - y);
            if (_workbenchChannel == PromptWorkbenchChannel.Rpg && _workbenchRpgSubTab == 1)
            {
                DrawRPGPawnPersonaEditor(contentRect);
                return;
            }

            ApplyWorkbenchEntryChannelSelection(_workbenchChannel);
            RimTalkChannelCompatConfig config = GetRimTalkChannelConfigClone(_rimTalkEditorChannel);
            DrawRimTalkPromptEntryEditor(contentRect, config);
        }

        private void DrawWorkbenchPresetNameRow(Rect inner, ref float y)
        {
            Rect row = new Rect(inner.x, y, inner.width, 24f);
            float labelWidth = 86f;
            Rect labelRect = new Rect(row.x, row.y, labelWidth, row.height);
            Rect valueRect = new Rect(labelRect.xMax + 4f, row.y, row.width - labelWidth - 4f, row.height);
            Widgets.Label(labelRect, "RimChat_PromptWorkbench_SelectedPresetName".Translate());
            Widgets.DrawBoxSolid(valueRect, new Color(0.03f, 0.03f, 0.04f));
            Widgets.DrawBox(valueRect, 1);
            PromptPresetConfig selected = GetSelectedPreset();
            string name = selected?.Name ?? "RimChat_PromptPreset_NoSelection".Translate().ToString();
            Widgets.Label(new Rect(valueRect.x + 6f, valueRect.y + 2f, valueRect.width - 12f, valueRect.height), name);
            y += 30f;
        }

        private void DrawRpgWorkbenchSubTabs(Rect inner, ref float y)
        {
            Rect row = new Rect(inner.x, y, inner.width, 26f);
            float width = (row.width - 6f) * 0.5f;
            Rect entriesRect = new Rect(row.x, row.y, width, row.height);
            Rect personaRect = new Rect(entriesRect.xMax + 6f, row.y, width, row.height);
            DrawWorkbenchSubTab(entriesRect, 0, "RimChat_PromptWorkbench_RpgSubEntries");
            DrawWorkbenchSubTab(personaRect, 1, "RimChat_PromptWorkbench_RpgSubPersona");
            y += 30f;
        }

        private void DrawWorkbenchSubTab(Rect rect, int index, string key)
        {
            bool selected = _workbenchRpgSubTab == index;
            Color color = selected ? new Color(0.45f, 0.33f, 0.15f) : new Color(0.19f, 0.15f, 0.10f);
            Widgets.DrawBoxSolid(rect, color);
            Widgets.DrawBox(rect, 1);
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = selected ? new Color(1f, 0.88f, 0.55f) : Color.white;
            Widgets.Label(rect, key.Translate());
            GUI.color = Color.white;
            Text.Anchor = oldAnchor;
            if (Widgets.ButtonInvisible(rect))
            {
                _workbenchRpgSubTab = index;
            }
        }

        private void DrawWorkbenchSidePanelContainer(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.09f, 0.10f, 0.12f));
            Rect inner = rect.ContractedBy(8f);
            float buttonWidth = (inner.width - 12f) / 3f;
            Rect previewRect = new Rect(inner.x, inner.y, buttonWidth, 24f);
            Rect varsRect = new Rect(previewRect.xMax + 6f, inner.y, buttonWidth, 24f);
            Rect helpRect = new Rect(varsRect.xMax + 6f, inner.y, buttonWidth, 24f);

            DrawWorkbenchSideButton(previewRect, PromptWorkbenchInfoPanel.Preview, "RimChat_PreviewTitleShort");
            DrawWorkbenchSideButton(varsRect, PromptWorkbenchInfoPanel.Variables, "RimChat_PromptWorkbench_VariablesTab");
            DrawWorkbenchSideButton(helpRect, PromptWorkbenchInfoPanel.Help, "RimChat_PromptWorkbench_HelpTab");

            Rect contentRect = new Rect(inner.x, previewRect.yMax + 6f, inner.width, inner.height - 30f);
            switch (_workbenchSidePanelTab)
            {
                case PromptWorkbenchInfoPanel.Preview:
                    DrawWorkbenchPreview(contentRect);
                    break;
                case PromptWorkbenchInfoPanel.Variables:
                    DrawWorkbenchVariables(contentRect);
                    break;
                default:
                    DrawWorkbenchHelp(contentRect);
                    break;
            }
        }

        private void DrawWorkbenchSideButton(Rect rect, PromptWorkbenchInfoPanel panel, string key)
        {
            bool selected = _workbenchSidePanelTab == panel;
            Widgets.DrawBoxSolid(rect, selected ? new Color(0.45f, 0.33f, 0.15f) : new Color(0.19f, 0.15f, 0.10f));
            Widgets.DrawBox(rect, 1);
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = selected ? new Color(1f, 0.88f, 0.55f) : Color.white;
            Widgets.Label(rect, key.Translate());
            GUI.color = Color.white;
            Text.Anchor = oldAnchor;
            if (Widgets.ButtonInvisible(rect))
            {
                _workbenchSidePanelTab = panel;
            }
        }

        private void DrawWorkbenchPreview(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.03f, 0.03f, 0.04f));
            Rect inner = rect.ContractedBy(6f);
            string preview = BuildWorkbenchPreviewText();
            float contentHeight = Mathf.Max(inner.height, Text.CalcHeight(preview, inner.width - 16f) + 12f);
            Rect view = new Rect(0f, 0f, inner.width - 16f, contentHeight);
            _previewScroll = GUI.BeginScrollView(inner, _previewScroll, view);
            GUI.color = Color.white;
            Widgets.Label(new Rect(0f, 0f, view.width, contentHeight), preview);
            GUI.EndScrollView();
        }

        private string BuildWorkbenchPreviewText()
        {
            if (_workbenchChannel == PromptWorkbenchChannel.Rpg && _workbenchRpgSubTab == 1)
            {
                return "RimChat_PromptWorkbench_PersonaPreviewHint".Translate();
            }

            ApplyWorkbenchEntryChannelSelection(_workbenchChannel);
            RimTalkChannelCompatConfig config = GetRimTalkChannelConfigClone(_rimTalkEditorChannel);
            string merged = ComposePromptEntryTextByRole(config?.PromptEntries, includeSystemRole: true, includeNonSystemRole: true);
            if (string.IsNullOrWhiteSpace(merged))
            {
                return "RimChat_PromptWorkbench_PreviewEmpty".Translate();
            }

            return merged;
        }

        private void DrawPresetActions(Rect rect)
        {
            float w = (rect.width - 6f) / 2f;
            if (Widgets.ButtonText(new Rect(rect.x, rect.y, w, rect.height), "RimChat_PromptPreset_Create".Translate()))
            {
                PromptPresetConfig created = _promptPresetService.CreateFromLegacy(this, NextPresetName("Preset"));
                _promptPresetStore.Presets.Add(created);
                _selectedPromptPresetId = created.Id;
                _presetRenameBuffer = created.Name;
                _promptPresetService.SaveAll(_promptPresetStore);
            }

            PromptPresetConfig selected = GetSelectedPreset();
            if (selected != null && Widgets.ButtonText(new Rect(rect.x + w + 6f, rect.y, w, rect.height), "RimChat_PromptPreset_Duplicate".Translate()))
            {
                PromptPresetConfig duplicated = _promptPresetService.Duplicate(this, selected, NextPresetName(selected.Name));
                _promptPresetStore.Presets.Add(duplicated);
                _selectedPromptPresetId = duplicated.Id;
                _presetRenameBuffer = duplicated.Name;
                _promptPresetService.SaveAll(_promptPresetStore);
            }
        }

        private void DrawPresetList(Rect rect)
        {
            List<PromptPresetSummary> rows = _promptPresetService.BuildSummaries(_promptPresetStore);
            float contentHeight = Mathf.Max(rect.height, rows.Count * 32f);
            Rect view = new Rect(0f, 0f, rect.width - 16f, contentHeight);
            _promptPresetScroll = GUI.BeginScrollView(rect, _promptPresetScroll, view);
            for (int i = 0; i < rows.Count; i++)
            {
                PromptPresetSummary row = rows[i];
                Rect r = new Rect(0f, i * 32f, view.width, 30f);
                bool selected = string.Equals(row.Id, _selectedPromptPresetId, StringComparison.Ordinal);
                if (selected)
                {
                    Widgets.DrawBoxSolid(r, new Color(0.27f, 0.38f, 0.56f));
                }
                else if (Mouse.IsOver(r))
                {
                    Widgets.DrawBoxSolid(r, new Color(0.18f, 0.18f, 0.20f));
                }

                Widgets.Label(new Rect(r.x + 6f, r.y + 1f, r.width - 12f, 16f), row.Name + (row.IsActive ? " [ACTIVE]" : string.Empty));
                GUI.color = Color.gray;
                Widgets.Label(new Rect(r.x + 6f, r.y + 15f, r.width - 12f, 14f), $"D:{row.DiplomacyChars} R:{row.RpgChars}");
                GUI.color = Color.white;
                if (Widgets.ButtonInvisible(r))
                {
                    _selectedPromptPresetId = row.Id;
                    _presetRenameBuffer = row.Name;
                }
            }

            GUI.EndScrollView();
        }

        private void DrawPresetBottomActions(Rect rect)
        {
            PromptPresetConfig selected = GetSelectedPreset();
            _presetRenameBuffer = Widgets.TextField(new Rect(rect.x, rect.y, rect.width, 24f), _presetRenameBuffer ?? string.Empty);
            float w = (rect.width - 12f) / 3f;
            if (selected != null && Widgets.ButtonText(new Rect(rect.x, rect.y + 28f, w, 24f), "RimChat_PromptPreset_Activate".Translate()))
            {
                _promptPresetService.Activate(this, _promptPresetStore, selected.Id, out _);
                _promptPresetService.SaveAll(_promptPresetStore);
            }

            if (selected != null && _promptPresetStore.Presets.Count > 1 && Widgets.ButtonText(new Rect(rect.x + w + 6f, rect.y + 28f, w, 24f), "RimChat_PromptPreset_Delete".Translate()))
            {
                _promptPresetStore.Presets.RemoveAll(p => string.Equals(p.Id, selected.Id, StringComparison.Ordinal));
                _selectedPromptPresetId = _promptPresetStore.Presets.FirstOrDefault()?.Id ?? string.Empty;
                _promptPresetService.SaveAll(_promptPresetStore);
            }

            if (selected != null && Widgets.ButtonText(new Rect(rect.x + (w + 6f) * 2f, rect.y + 28f, w, 24f), "RimChat_PromptPreset_Rename".Translate()))
            {
                selected.Name = string.IsNullOrWhiteSpace(_presetRenameBuffer) ? selected.Name : _presetRenameBuffer.Trim();
                selected.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
                _promptPresetService.SaveAll(_promptPresetStore);
            }
        }

        private void ApplyWorkbenchEntryChannelSelection(PromptWorkbenchChannel channel)
        {
            _rimTalkEditorChannel = channel == PromptWorkbenchChannel.Diplomacy
                ? RimTalkPromptChannel.Diplomacy
                : RimTalkPromptChannel.Rpg;
            EnsurePromptEntrySeedForChannel(_rimTalkEditorChannel);
        }

        private bool IsEntryDrivenWorkbenchChannelActive()
        {
            return _promptWorkbenchExperimentalEnabled &&
                   (_workbenchChannel == PromptWorkbenchChannel.Diplomacy ||
                    _workbenchChannel == PromptWorkbenchChannel.Rpg);
        }

        private bool TryInsertVariableTokenToEntryChannel(string token)
        {
            if (!IsEntryDrivenWorkbenchChannelActive())
            {
                return false;
            }

            string variableName = NormalizeVariableNameToken(token);
            if (string.IsNullOrWhiteSpace(variableName))
            {
                return false;
            }

            if (_workbenchChannel == PromptWorkbenchChannel.Rpg && _workbenchRpgSubTab == 1)
            {
                return false;
            }

            ApplyWorkbenchEntryChannelSelection(_workbenchChannel);
            AppendVariableToCurrentRimTalkTemplate(variableName);
            return true;
        }

        private static string NormalizeVariableNameToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return string.Empty;
            }

            string normalized = token.Trim();
            if (normalized.StartsWith("{{", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(2);
            }

            if (normalized.EndsWith("}}", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(0, normalized.Length - 2);
            }

            return normalized.Trim().Trim('{', '}', ' ');
        }

        private void DrawWorkbenchVariables(Rect rect)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 22f), "RimChat_PromptWorkbench_VariablesTitle".Translate());
            Rect contentRect = new Rect(rect.x, rect.y + 24f, rect.width, rect.height - 24f);

            var listing = new Listing_Standard();
            listing.Begin(contentRect);
            DrawRimTalkTabVariableBrowser(listing);
            listing.End();
        }

        private void DrawWorkbenchHelp(Rect rect)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 22f), "RimChat_PromptWorkbench_HelpTitle".Translate());
            GUI.color = Color.gray;
            Widgets.Label(new Rect(rect.x, rect.y + 24f, rect.width, rect.height - 24f), "RimChat_PromptWorkbench_HelpBody".Translate());
            GUI.color = Color.white;
        }

        private string[] GetCurrentSourceHints()
        {
            if (_workbenchChannel == PromptWorkbenchChannel.Diplomacy)
            {
                return new[] { "DiplomacyDialogue", "SocialNews", "SendImage", "StrategySuggestion" };
            }

            return new[] { "RpgDialogue", "NpcPush", "PawnRpgPush", "PersonaBootstrap", "MemorySummary", "ArchiveCompression" };
        }

        private void EnsurePresetStoreReady()
        {
            _promptPresetService ??= new PromptPresetService();
            if (_promptPresetStore != null)
            {
                return;
            }

            _promptPresetStore = _promptPresetService.LoadAll(this);
            PromptPresetConfig active = _promptPresetStore.Presets.FirstOrDefault(p => p.IsActive)
                                      ?? _promptPresetStore.Presets.FirstOrDefault();
            _selectedPromptPresetId = active?.Id ?? string.Empty;
            _presetRenameBuffer = active?.Name ?? string.Empty;
        }

        private PromptPresetConfig GetSelectedPreset()
        {
            return _promptPresetStore?.Presets?.FirstOrDefault(p => string.Equals(p.Id, _selectedPromptPresetId, StringComparison.Ordinal));
        }

        private string NextPresetName(string baseName)
        {
            string stem = string.IsNullOrWhiteSpace(baseName) ? "Preset" : baseName.Trim();
            int n = 1;
            string candidate = stem;
            while (_promptPresetStore.Presets.Any(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                n++;
                candidate = $"{stem} {n}";
            }

            return candidate;
        }

        internal void DrawPromptWorkbenchWindow(Rect rect)
        {
            try
            {
                DrawAdvancedPromptWorkbench(rect);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Prompt workbench window render failed: {ex}");
                Widgets.Label(rect, "RimChat_PromptRenderFailed".Translate());
            }
        }

        internal void OpenPromptWorkbenchWindow()
        {
            _advancedPromptMode = true;
            SetPromptWorkbenchExperimentalEnabled(true);
            _workbenchChannel = PromptWorkbenchChannel.Diplomacy;
            _workbenchSidePanelTab = PromptWorkbenchInfoPanel.Preview;
            _workbenchRpgSubTab = 0;
            ApplyWorkbenchEntryChannelSelection(_workbenchChannel);
            Find.WindowStack.Add(new UI.Dialog_PromptWorkbench(this));
        }

        internal void SetWorkbenchChannelRimTalkRpg()
        {
            _workbenchChannel = PromptWorkbenchChannel.Rpg;
            _workbenchRpgSubTab = 0;
        }

        private void ShowImportPresetDialog()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            Find.WindowStack.Add(new Dialog_LoadFile(System.IO.Path.Combine(desktop, "RimChatPromptPreset.json"), path =>
            {
                if (_promptPresetService.ImportPreset(path, _promptPresetStore, out PromptPresetConfig imported, out string error))
                {
                    _promptPresetStore.Presets.Add(imported);
                    _selectedPromptPresetId = imported.Id;
                    _presetRenameBuffer = imported.Name;
                    _promptPresetService.SaveAll(_promptPresetStore);
                }
                else
                {
                    Messages.Message("RimChat_PromptPreset_ImportFailed".Translate(error), MessageTypeDefOf.RejectInput, false);
                }
            }));
        }

        private void ShowExportPresetDialog()
        {
            PromptPresetConfig selected = GetSelectedPreset();
            if (selected == null)
            {
                Messages.Message("RimChat_PromptPreset_NoSelection".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            Find.WindowStack.Add(new Dialog_SaveFile(System.IO.Path.Combine(desktop, $"RimChatPromptPreset_{selected.Name}.json"), path =>
            {
                if (!_promptPresetService.ExportPreset(path, selected, out string error))
                {
                    Messages.Message("RimChat_PromptPreset_ExportFailed".Translate(error), MessageTypeDefOf.RejectInput, false);
                }
            }));
        }
    }
}
