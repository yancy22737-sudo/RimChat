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
        private RimTalkPromptChannel? _workbenchSeededEntryChannel;
        private RimTalkChannelCompatConfig _workbenchEditingConfig;
        private RimTalkPromptChannel _workbenchEditingConfigChannel = RimTalkPromptChannel.Diplomacy;
        private bool _workbenchEditingConfigReady;
        private string _workbenchPromptChannel = string.Empty;

        internal void FlushPromptEditorsToStorageForPreset(bool persistToFiles = false)
        {
            SyncBuffersToData();
            FlushPromptWorkspaceEdits(persistToDisk: persistToFiles);
            if (!persistToFiles)
            {
                return;
            }

            SaveSystemPromptConfig();
        }

        internal void RefreshPromptEditorStateFromStorage()
        {
            _systemPromptConfig = PromptPersistenceService.Instance.LoadConfigReadOnly();
            ReloadPromptUnifiedCatalogFromStorage();
            EnsureRpgPromptTextsLoaded();
            SyncBuffersToData();
            _workbenchSeededEntryChannel = null;
            InvalidateWorkbenchEditingChannelConfig();
            ResetRimTalkEntryContentBuffer();
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
            EnsureWorkbenchPromptChannelSelection();
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
            Rect channelHeaderRect = new Rect(inner.x, tabsY + 2f, 92f, 26f);
            Widgets.Label(channelHeaderRect, "RimChat_PromptWorkbench_ChannelHeader".Translate());
            Rect channelDropdownRect = new Rect(channelHeaderRect.xMax + 4f, tabsY, 260f, 30f);
            DrawWorkbenchChannelDropdown(channelDropdownRect);

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

        private void DrawWorkbenchChannelDropdown(Rect rect)
        {
            string selectedChannel = EnsureWorkbenchPromptChannelSelection();
            string label = RimTalkPromptEntryChannelCatalog.GetLabel(selectedChannel);
            Widgets.DrawBoxSolid(rect, new Color(0.25f, 0.18f, 0.08f));
            Widgets.DrawBox(rect, 1);
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(1f, 0.88f, 0.55f);
            Widgets.Label(new Rect(rect.x + 8f, rect.y, rect.width - 30f, rect.height), label);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(rect.xMax - 22f, rect.y, 18f, rect.height), "▼");
            GUI.color = Color.white;
            Text.Anchor = oldAnchor;

            if (Widgets.ButtonInvisible(rect))
            {
                ShowWorkbenchPromptChannelMenu();
            }
        }

        private void ShowWorkbenchPromptChannelMenu()
        {
            List<FloatMenuOption> options = PromptSectionSchemaCatalog.GetAllWorkspaceChannels()
                .Select(channelId => new FloatMenuOption(
                    RimTalkPromptEntryChannelCatalog.GetLabel(channelId),
                    () => SetWorkbenchPromptChannel(channelId)))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private string EnsureWorkbenchPromptChannelSelection()
        {
            string normalized = RimTalkPromptEntryChannelCatalog.NormalizeLoose(_workbenchPromptChannel);
            if (!PromptSectionSchemaCatalog.GetAllWorkspaceChannels().Contains(normalized, StringComparer.Ordinal))
            {
                normalized = GetDefaultWorkbenchPromptChannelForRoot(_workbenchChannel);
            }

            if (!DoesPromptChannelBelongToWorkbenchRoot(normalized, _workbenchChannel))
            {
                normalized = GetDefaultWorkbenchPromptChannelForRoot(_workbenchChannel);
            }

            _workbenchPromptChannel = normalized;
            return _workbenchPromptChannel;
        }

        private static string GetDefaultWorkbenchPromptChannelForRoot(PromptWorkbenchChannel root)
        {
            return PromptSectionSchemaCatalog.GetDefaultWorkspaceChannel(ToRimTalkPromptChannel(root));
        }

        private static bool DoesPromptChannelBelongToWorkbenchRoot(string channelId, PromptWorkbenchChannel root)
        {
            return PromptSectionSchemaCatalog.DoesChannelBelongToRoot(channelId, ToRimTalkPromptChannel(root));
        }

        private void SetWorkbenchPromptChannel(string channelId)
        {
            string normalized = RimTalkPromptEntryChannelCatalog.NormalizeLoose(channelId);
            if (!PromptSectionSchemaCatalog.GetAllWorkspaceChannels().Contains(normalized, StringComparer.Ordinal))
            {
                normalized = GetDefaultWorkbenchPromptChannelForRoot(_workbenchChannel);
            }

            PromptWorkbenchChannel desiredRoot = ResolveWorkbenchRootChannel(normalized);
            bool rootChanged = _workbenchChannel != desiredRoot;
            _workbenchPromptChannel = normalized;
            _workbenchChannel = desiredRoot;
            _workbenchRpgSubTab = 0;
            _rimTalkSelectedEntryId = string.Empty;
            ApplyWorkbenchEntryChannelSelection(_workbenchChannel);
            if (rootChanged)
            {
                InvalidateWorkbenchEditingChannelConfig();
            }

            FocusWorkbenchEntryByPromptChannel(normalized);
            ResetRimTalkEntryContentBuffer();
        }

        private PromptWorkbenchChannel ResolveWorkbenchRootChannel(string channelId)
        {
            RimTalkPromptChannel root = PromptSectionSchemaCatalog.ResolveRootChannel(
                channelId,
                ToRimTalkPromptChannel(_workbenchChannel));
            return ToWorkbenchChannel(root);
        }

        private static RimTalkPromptChannel ToRimTalkPromptChannel(PromptWorkbenchChannel channel)
        {
            return channel == PromptWorkbenchChannel.Rpg
                ? RimTalkPromptChannel.Rpg
                : RimTalkPromptChannel.Diplomacy;
        }

        private static PromptWorkbenchChannel ToWorkbenchChannel(RimTalkPromptChannel channel)
        {
            return channel == RimTalkPromptChannel.Rpg
                ? PromptWorkbenchChannel.Rpg
                : PromptWorkbenchChannel.Diplomacy;
        }

        private void FocusWorkbenchEntryByPromptChannel(string channelId)
        {
            RimTalkChannelCompatConfig config = GetWorkbenchEditingChannelConfig();
            if (config?.PromptEntries == null || config.PromptEntries.Count == 0)
            {
                _rimTalkSelectedEntryId = string.Empty;
                _rimTalkDepthBuffer = string.Empty;
                return;
            }

            string normalized = RimTalkPromptEntryChannelCatalog.NormalizeForRoot(channelId, _rimTalkEditorChannel);
            RimTalkPromptEntryConfig matched = config.PromptEntries.FirstOrDefault(entry =>
                entry != null && string.Equals(
                    RimTalkPromptEntryChannelCatalog.NormalizeForRoot(entry.PromptChannel, _rimTalkEditorChannel),
                    normalized,
                    StringComparison.OrdinalIgnoreCase));

            if (matched == null)
            {
                EnsureRimTalkEntrySelection(config);
                return;
            }

            _rimTalkSelectedEntryId = matched.Id ?? string.Empty;
            _rimTalkDepthBuffer = matched.InChatDepth.ToString();
        }

        private void DrawWorkbenchBody(Rect rect)
        {
            float gap = 6f;
            float leftWidth = Mathf.Clamp(rect.width * 0.2f, 200f, 220f);
            Rect leftRect = new Rect(rect.x, rect.y, leftWidth, rect.height);
            Rect workspaceRect = new Rect(leftRect.xMax + gap, rect.y, rect.width - leftWidth - gap, rect.height);
            float sideWidth = Mathf.Clamp(workspaceRect.width * 0.36f, 260f, 380f);
            if (workspaceRect.width - sideWidth - gap < 320f)
            {
                sideWidth = Mathf.Max(220f, workspaceRect.width - 320f - gap);
            }

            Rect centerRect = new Rect(workspaceRect.x, workspaceRect.y, workspaceRect.width - sideWidth - gap, workspaceRect.height);
            Rect rightRect = new Rect(centerRect.xMax + gap, workspaceRect.y, sideWidth, workspaceRect.height);
            GetWorkbenchEditingChannelConfig();
            DrawWorkbenchPresetPanel(leftRect);
            DrawWorkbenchMainPanel(centerRect);
            DrawWorkbenchSidePanelContainer(rightRect);
        }

        private RimTalkChannelCompatConfig GetWorkbenchEditingChannelConfig()
        {
            ApplyWorkbenchEntryChannelSelection(_workbenchChannel);
            RimTalkPromptChannel channel = _rimTalkEditorChannel;
            if (_workbenchEditingConfigReady &&
                _workbenchEditingConfig != null &&
                _workbenchEditingConfigChannel == channel)
            {
                return _workbenchEditingConfig;
            }

            RimTalkChannelCompatConfig config = GetRimTalkChannelConfigClone(channel);
            config.NormalizeWith(RimTalkChannelCompatConfig.CreateDefault());
            EnsureRimTalkEntrySelection(config);
            _workbenchEditingConfig = config;
            _workbenchEditingConfigChannel = channel;
            _workbenchEditingConfigReady = true;
            return _workbenchEditingConfig;
        }

        private void SyncWorkbenchEditingChannelConfig(RimTalkPromptChannel channel, RimTalkChannelCompatConfig config)
        {
            if (config == null)
            {
                InvalidateWorkbenchEditingChannelConfig();
                return;
            }

            RimTalkChannelCompatConfig cloned = config.Clone();
            cloned.NormalizeWith(RimTalkChannelCompatConfig.CreateDefault());
            EnsureRimTalkEntrySelection(cloned);
            _workbenchEditingConfig = cloned;
            _workbenchEditingConfigChannel = channel;
            _workbenchEditingConfigReady = true;
        }

        private void InvalidateWorkbenchEditingChannelConfig()
        {
            _workbenchEditingConfig = null;
            _workbenchEditingConfigReady = false;
        }

        private void DrawWorkbenchPresetPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.09f, 0.10f, 0.12f));
            Rect inner = rect.ContractedBy(8f);
            float topHeight = Mathf.Clamp(inner.height * 0.44f, 250f, 330f);
            Rect presetRect = new Rect(inner.x, inner.y, inner.width, topHeight);
            Rect lowerRect = new Rect(inner.x, presetRect.yMax + 8f, inner.width, inner.height - topHeight - 8f);

            Widgets.Label(new Rect(presetRect.x, presetRect.y, presetRect.width, 22f), "RimChat_PromptWorkbench_PresetHeader".Translate());
            float y = presetRect.y + 24f;
            DrawPresetActions(new Rect(presetRect.x, y, presetRect.width, 24f));
            y += 28f;
            float listHeight = Mathf.Clamp(presetRect.height - 140f, 96f, 160f);
            DrawPresetList(new Rect(presetRect.x, y, presetRect.width, listHeight));
            y += listHeight + 6f;
            DrawPresetBottomActions(new Rect(presetRect.x, y, presetRect.width, presetRect.yMax - y));

            RimTalkChannelCompatConfig config = GetWorkbenchEditingChannelConfig();
            DrawRimTalkPromptEntryList(lowerRect, config);
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

            RimTalkChannelCompatConfig config = GetWorkbenchEditingChannelConfig();
            DrawRimTalkPromptEntryEditor(contentRect, config, useChipEditor: true);
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

            RimTalkChannelCompatConfig config = GetWorkbenchEditingChannelConfig();
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
                try
                {
                    PromptPresetConfig created = _promptPresetService.CreateFromLegacy(this, NextPresetName("Preset"));
                    _promptPresetStore.Presets.Add(created);
                    _selectedPromptPresetId = created.Id;
                    _presetRenameBuffer = created.Name;
                    Log.Message($"[RimChat][PresetDiag] Legacy workbench create clicked. add_id={created.Id}, count={_promptPresetStore.Presets.Count}");
                    if (!TryActivatePresetById(created.Id, showSuccessMessage: false))
                    {
                        _promptPresetService.SaveAll(_promptPresetStore);
                    }
                    Messages.Message("RimChat_PromptPreset_CreateSuccess".Translate(created.Name), MessageTypeDefOf.NeutralEvent, false);
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimChat][PresetDiag] Legacy workbench create failed: {ex}");
                    Messages.Message("RimChat_PromptPreset_ActivateFailed".Translate(ex.Message), MessageTypeDefOf.RejectInput, false);
                }
            }

            PromptPresetConfig selected = GetSelectedPreset();
            if (selected != null && Widgets.ButtonText(new Rect(rect.x + w + 6f, rect.y, w, rect.height), "RimChat_PromptPreset_Duplicate".Translate()))
            {
                try
                {
                    PromptPresetConfig duplicated = _promptPresetService.Duplicate(this, selected, NextPresetName(selected.Name));
                    _promptPresetStore.Presets.Add(duplicated);
                    _selectedPromptPresetId = duplicated.Id;
                    _presetRenameBuffer = duplicated.Name;
                    Log.Message($"[RimChat][PresetDiag] Legacy workbench duplicate clicked. add_id={duplicated.Id}, count={_promptPresetStore.Presets.Count}");
                    if (!TryActivatePresetById(duplicated.Id, showSuccessMessage: false))
                    {
                        _promptPresetService.SaveAll(_promptPresetStore);
                    }
                    Messages.Message("RimChat_PromptPreset_DuplicateSuccess".Translate(duplicated.Name), MessageTypeDefOf.NeutralEvent, false);
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimChat][PresetDiag] Legacy workbench duplicate failed: {ex}");
                    Messages.Message("RimChat_PromptPreset_ActivateFailed".Translate(ex.Message), MessageTypeDefOf.RejectInput, false);
                }
            }
        }

        private void DrawPresetList(Rect rect)
        {
            List<PromptPresetSummary> rows = _promptPresetService.BuildSummaries(_promptPresetStore);
            const float rowStep = 25f;
            float contentHeight = Mathf.Max(rect.height, rows.Count * rowStep);
            Rect view = new Rect(0f, 0f, rect.width - 16f, contentHeight);
            Widgets.BeginScrollView(rect, ref _promptPresetScroll, view);
            for (int i = 0; i < rows.Count; i++)
            {
                PromptPresetSummary row = rows[i];
                Rect r = new Rect(0f, i * rowStep, view.width, 24f);
                bool selected = string.Equals(row.Id, _selectedPromptPresetId, StringComparison.Ordinal);
                if (selected)
                {
                    Widgets.DrawBoxSolid(r, new Color(0.27f, 0.38f, 0.56f));
                }
                else if (Mouse.IsOver(r))
                {
                    Widgets.DrawBoxSolid(r, new Color(0.18f, 0.18f, 0.20f));
                }

                if (row.IsActive)
                {
                    GUI.color = Color.green;
                    Widgets.Label(new Rect(r.x + 4f, r.y, 14f, 24f), "▶");
                    GUI.color = Color.white;
                }

                bool oldWrap = Text.WordWrap;
                Text.WordWrap = false;
                string title = row.Name ?? string.Empty;
                Widgets.Label(new Rect(r.x + 20f, r.y + 2f, r.width - 24f, 20f), title.Truncate(r.width - 24f));
                Text.WordWrap = oldWrap;
                if (Widgets.ButtonInvisible(r))
                {
                    bool changedSelection = !string.Equals(_selectedPromptPresetId, row.Id, StringComparison.Ordinal);
                    _selectedPromptPresetId = row.Id;
                    _presetRenameBuffer = row.Name;
                    if (changedSelection && !row.IsActive)
                    {
                        TryActivatePresetById(row.Id, showSuccessMessage: false);
                    }
                }
            }

            Widgets.EndScrollView();
        }

        private void DrawPresetBottomActions(Rect rect)
        {
            PromptPresetConfig selected = GetSelectedPreset();
            _presetRenameBuffer = Widgets.TextField(new Rect(rect.x, rect.y, rect.width, 24f), _presetRenameBuffer ?? string.Empty);
            float w = (rect.width - 6f) / 2f;
            float topY = rect.y + 28f;
            float bottomY = topY + 28f;
            if (selected != null && Widgets.ButtonText(new Rect(rect.x, topY, w, 24f), "RimChat_PromptPreset_Activate".Translate()))
            {
                TryActivatePresetById(selected.Id, showSuccessMessage: true);
            }

            if (selected != null && Widgets.ButtonText(new Rect(rect.x + w + 6f, topY, w, 24f), "RimChat_PromptPreset_Duplicate".Translate()))
            {
                PromptPresetConfig duplicated = _promptPresetService.Duplicate(this, selected, NextPresetName(selected.Name));
                _promptPresetStore.Presets.Add(duplicated);
                _selectedPromptPresetId = duplicated.Id;
                _presetRenameBuffer = duplicated.Name;
                _promptPresetService.SaveAll(_promptPresetStore);
                Messages.Message("RimChat_PromptPreset_DuplicateSuccess".Translate(duplicated.Name), MessageTypeDefOf.NeutralEvent, false);
            }

            if (selected != null && Widgets.ButtonText(new Rect(rect.x, bottomY, w, 24f), "RimChat_PromptPreset_Rename".Translate()))
            {
                string beforeName = selected.Name ?? string.Empty;
                selected.Name = string.IsNullOrWhiteSpace(_presetRenameBuffer) ? selected.Name : _presetRenameBuffer.Trim();
                selected.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
                _promptPresetService.SaveAll(_promptPresetStore);
                if (!string.Equals(beforeName, selected.Name, StringComparison.Ordinal))
                {
                    Messages.Message("RimChat_PromptPreset_RenameSuccess".Translate(selected.Name), MessageTypeDefOf.NeutralEvent, false);
                }
            }

            if (selected != null && _promptPresetStore.Presets.Count > 1 && Widgets.ButtonText(new Rect(rect.x + w + 6f, bottomY, w, 24f), "RimChat_PromptPreset_Delete".Translate()))
            {
                string deletedName = selected.Name ?? string.Empty;
                bool deletedActive = selected.IsActive;
                _promptPresetStore.Presets.RemoveAll(p => string.Equals(p.Id, selected.Id, StringComparison.Ordinal));
                _selectedPromptPresetId = _promptPresetStore.Presets.FirstOrDefault()?.Id ?? string.Empty;
                if (deletedActive && !string.IsNullOrWhiteSpace(_selectedPromptPresetId))
                {
                    TryActivatePresetById(_selectedPromptPresetId, showSuccessMessage: false);
                }
                else
                {
                    _promptPresetStore.ActivePresetId = _selectedPromptPresetId;
                    _promptPresetService.SaveAll(_promptPresetStore);
                }

                Messages.Message("RimChat_PromptPreset_DeleteSuccess".Translate(deletedName), MessageTypeDefOf.NeutralEvent, false);
            }
        }

        private void ApplyWorkbenchEntryChannelSelection(PromptWorkbenchChannel channel)
        {
            _rimTalkEditorChannel = channel == PromptWorkbenchChannel.Diplomacy
                ? RimTalkPromptChannel.Diplomacy
                : RimTalkPromptChannel.Rpg;
            if (_workbenchSeededEntryChannel == _rimTalkEditorChannel)
            {
                return;
            }

            EnsurePromptEntrySeedForChannel(_rimTalkEditorChannel);
            _workbenchSeededEntryChannel = _rimTalkEditorChannel;
        }

        private bool IsEntryDrivenWorkbenchChannelActive()
        {
            return _promptWorkbenchExperimentalEnabled &&
                   (_workbenchChannel == PromptWorkbenchChannel.Diplomacy ||
                    _workbenchChannel == PromptWorkbenchChannel.Rpg);
        }

        private bool TryInsertVariableTokenToEntryChannel(string token)
        {
            if (TryInsertVariableTokenToPromptWorkspace(token))
            {
                return true;
            }

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
            RimTalkChannelCompatConfig config = GetWorkbenchEditingChannelConfig();
            RimTalkPromptEntryConfig selectedEntry = GetSelectedRimTalkPromptEntry(config);
            DrawRimTalkWorkbenchVariableBrowser(contentRect, selectedEntry?.Content);
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
            if (active?.ChannelPayloads != null)
            {
                _promptPresetService.ApplyPayloadToSettings(this, active.ChannelPayloads, persistToFiles: false);
            }

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

        private bool TryActivatePresetById(string presetId, bool showSuccessMessage)
        {
            if (string.IsNullOrWhiteSpace(presetId))
            {
                return false;
            }

            if (_promptPresetService.Activate(this, _promptPresetStore, presetId, out string error))
            {
                _promptPresetStore.ActivePresetId = presetId;
                _promptPresetService.SaveAll(_promptPresetStore);
                if (showSuccessMessage)
                {
                    PromptPresetConfig activated = _promptPresetStore.Presets.FirstOrDefault(p => string.Equals(p.Id, presetId, StringComparison.Ordinal));
                    Messages.Message("RimChat_PromptPreset_ActivateSuccess".Translate(activated?.Name ?? string.Empty), MessageTypeDefOf.NeutralEvent, false);
                }

                InvalidateWorkbenchEditingChannelConfig();
                ResetRimTalkEntryContentBuffer();
                return true;
            }

            Log.Warning($"[RimChat] Prompt preset activation failed. id={presetId}, error={error}");
            Messages.Message("RimChat_PromptPreset_ActivateFailed".Translate(error ?? string.Empty), MessageTypeDefOf.RejectInput, false);
            return false;
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
            OpenPromptWorkbenchWindow(PromptWorkbenchChannel.Diplomacy);
        }

        internal void OpenPromptWorkbenchWindowForRpg()
        {
            OpenPromptWorkbenchWindow(PromptWorkbenchChannel.Rpg);
        }

        private void OpenPromptWorkbenchWindow(PromptWorkbenchChannel initialChannel)
        {
            _workbenchChannel = initialChannel;
            _advancedPromptMode = false;
            SetPromptWorkbenchExperimentalEnabled(false);

            if (Find.WindowStack.WindowOfType<Dialog_PromptWorkbenchLarge>() != null)
            {
                return;
            }

            Find.WindowStack.Add(new Dialog_PromptWorkbenchLarge(this));
        }

        internal void SetWorkbenchChannelRimTalkRpg()
        {
            _workbenchChannel = PromptWorkbenchChannel.Rpg;
            SetPromptWorkbenchExperimentalEnabled(false);
            selectedTab = 2;
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
                    InvalidateWorkbenchEditingChannelConfig();
                    ResetRimTalkEntryContentBuffer();
                    Messages.Message("RimChat_PromptPreset_ImportSuccess".Translate(imported.Name), MessageTypeDefOf.NeutralEvent, false);
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
                    return;
                }

                Messages.Message("RimChat_PromptPreset_ExportSuccess".Translate(path), MessageTypeDefOf.NeutralEvent, false);
            }));
        }
    }
}
