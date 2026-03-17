using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Persistence;
using RimChat.Prompting;
using RimChat.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: prompt preset service, prompt section catalog, aggregate preview builder, shared variable browser, and chip editor.
    /// Responsibility: render the stable section-driven prompt workspace for the native PromptSectionCatalog.
    /// </summary>
    public partial class RimChatSettings : ModSettings
    {
        private string _promptWorkspaceSelectedSectionId = "system_rules";
        private string _promptWorkspaceBufferedChannel = string.Empty;
        private string _promptWorkspaceBufferedSectionId = string.Empty;
        private string _promptWorkspaceEditorBuffer = string.Empty;
        private Vector2 _promptWorkspaceSectionScroll = Vector2.zero;
        private Vector2 _promptWorkspaceEditorScroll = Vector2.zero;
        private Vector2 _promptWorkspacePreviewScroll = Vector2.zero;
        private Vector2 _promptWorkspaceReportScroll = Vector2.zero;
        private PromptWorkbenchChannel _promptWorkspacePreviewCachedRoot;
        private string _promptWorkspacePreviewCachedChannel = string.Empty;
        private string _promptWorkspacePreviewCachedText = string.Empty;
        private bool _promptWorkspacePreviewCacheValid;
        private PromptWorkbenchChipEditor _promptWorkspaceChipEditor;
        private PromptWorkbenchChipEditor _promptWorkspacePreviewChipViewer;
        private bool _promptWorkspaceChipEditorDisabledForSession;
        private bool _promptWorkspacePreviewChipViewerDisabledForSession;

        private void DrawPromptSectionWorkspace(Rect root)
        {
            EnsurePresetStoreReady();
            EnsurePromptWorkspaceSelection();

            Widgets.DrawBoxSolid(root, new Color(0.08f, 0.09f, 0.11f));
            Rect frame = root.ContractedBy(8f);
            Rect headerRect = new Rect(frame.x, frame.y, frame.width, 74f);
            Rect bodyRect = new Rect(frame.x, headerRect.yMax + 6f, frame.width, frame.height - headerRect.height - 6f);

            DrawPromptWorkspaceHeader(headerRect);
            DrawPromptWorkspaceBody(bodyRect);
        }

        private void DrawPromptWorkspaceHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.07f, 0.08f, 0.10f));
            Rect inner = rect.ContractedBy(8f);

            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(0.95f, 0.74f, 0.26f);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width * 0.42f, 28f), "RimChat_Tab_PromptWorkbench".Translate());
            GUI.color = Color.white;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;

            float top = inner.y + 34f;
            DrawPromptWorkspaceRootButtons(new Rect(inner.x, top, 250f, 30f));
            DrawPromptWorkspaceChannelDropdown(new Rect(inner.x + 260f, top, 300f, 30f));

            Rect importRect = new Rect(inner.xMax - 180f, top, 84f, 30f);
            Rect exportRect = new Rect(inner.xMax - 90f, top, 84f, 30f);
            if (Widgets.ButtonText(importRect, "RimChat_Import".Translate()))
            {
                ShowImportPresetDialog();
            }

            if (Widgets.ButtonText(exportRect, "RimChat_Export".Translate()))
            {
                ShowExportPresetDialog();
            }
        }

        private void DrawPromptWorkspaceRootButtons(Rect rect)
        {
            float width = (rect.width - 6f) * 0.5f;
            Rect diplomacyRect = new Rect(rect.x, rect.y, width, rect.height);
            Rect rpgRect = new Rect(diplomacyRect.xMax + 6f, rect.y, width, rect.height);
            DrawPromptWorkspaceRootButton(diplomacyRect, PromptWorkbenchChannel.Diplomacy, "RimChat_PromptWorkbench_ChannelDiplomacy");
            DrawPromptWorkspaceRootButton(rpgRect, PromptWorkbenchChannel.Rpg, "RimChat_PromptWorkbench_ChannelRpg");
        }

        private void DrawPromptWorkspaceRootButton(Rect rect, PromptWorkbenchChannel channel, string key)
        {
            bool selected = _workbenchChannel == channel;
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
                SetPromptWorkspaceRoot(channel);
            }
        }

        private void DrawPromptWorkspaceChannelDropdown(Rect rect)
        {
            string currentChannel = EnsurePromptWorkspaceSelection();
            Widgets.DrawBoxSolid(rect, new Color(0.25f, 0.18f, 0.08f));
            Widgets.DrawBox(rect, 1);
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(1f, 0.88f, 0.55f);
            Widgets.Label(new Rect(rect.x + 8f, rect.y, rect.width - 30f, rect.height), RimTalkPromptEntryChannelCatalog.GetLabel(currentChannel));
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(rect.xMax - 22f, rect.y, 18f, rect.height), "▼");
            GUI.color = Color.white;
            Text.Anchor = oldAnchor;
            if (Widgets.ButtonInvisible(rect))
            {
                List<FloatMenuOption> options = GetPromptWorkspaceChannels()
                    .Select(channelId => new FloatMenuOption(
                        RimTalkPromptEntryChannelCatalog.GetLabel(channelId),
                        () => SetPromptWorkspaceChannel(channelId)))
                    .ToList();
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private void DrawPromptWorkspaceBody(Rect rect)
        {
            float gap = 8f;
            float leftWidth = Mathf.Clamp(rect.width * 0.24f, 240f, 300f);
            float remainingWidth = Mathf.Max(1f, rect.width - leftWidth - gap * 2f);
            float middleWidth = Mathf.Clamp(remainingWidth * 0.60f, 360f, Mathf.Max(360f, remainingWidth - 300f));
            float rightWidth = Mathf.Max(260f, remainingWidth - middleWidth);
            if (middleWidth + rightWidth > remainingWidth)
            {
                rightWidth = Mathf.Max(220f, remainingWidth - middleWidth);
            }

            Rect leftRect = new Rect(rect.x, rect.y, leftWidth, rect.height);
            Rect middleRect = new Rect(leftRect.xMax + gap, rect.y, middleWidth, rect.height);
            Rect rightRect = new Rect(middleRect.xMax + gap, rect.y, Mathf.Max(1f, rect.xMax - (middleRect.xMax + gap)), rect.height);

            DrawPromptWorkspacePresetPanel(leftRect);
            DrawPromptWorkspaceEditorPanel(middleRect);
            DrawPromptWorkspaceSidePanel(rightRect);
        }

        private void DrawPromptWorkspacePresetPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.09f, 0.10f, 0.12f));
            Rect inner = rect.ContractedBy(8f);
            float y = inner.y;
            Rect bottomActionsRect = new Rect(inner.x, inner.yMax - 80f, inner.width, 80f);
            Widgets.Label(new Rect(inner.x, y, inner.width, 22f), "RimChat_PromptWorkbench_PresetHeader".Translate());
            y += 24f;
            DrawPresetActions(new Rect(inner.x, y, inner.width, 24f));
            y += 28f;
            float presetListHeight = ResolvePromptWorkspacePresetListHeight(y, bottomActionsRect.y, inner.height);
            DrawPresetList(new Rect(inner.x, y, inner.width, presetListHeight));
            y += presetListHeight + 8f;

            Widgets.Label(new Rect(inner.x, y, inner.width, 22f), "RimChat_PromptWorkspaceSectionHeader".Translate());
            y += 24f;
            float sectionHeight = Mathf.Max(72f, bottomActionsRect.y - y - 6f);
            DrawPromptWorkspaceSectionList(new Rect(inner.x, y, inner.width, sectionHeight));
            DrawPresetBottomActions(bottomActionsRect);
        }

        private void DrawPromptWorkspaceEditorPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.06f, 0.07f, 0.09f));
            Rect inner = rect.ContractedBy(8f);
            float y = inner.y;
            const float validationHeight = 24f;

            DrawWorkbenchPresetNameRow(inner, ref y);
            DrawPromptWorkspaceToolbar(new Rect(inner.x, y, inner.width, 26f));
            y += 32f;
            PromptSectionSchemaCatalog.TryGetSection(_promptWorkspaceSelectedSectionId, out PromptSectionSchemaItem section);
            Widgets.Label(new Rect(inner.x, y, inner.width, 22f), section.EnglishName);
            y += 24f;

            float editorHeight = Mathf.Max(24f, inner.yMax - y - validationHeight - 4f);
            Rect editorRect = new Rect(inner.x, y, inner.width, editorHeight);
            string edited = DrawPromptWorkspaceEditor(editorRect, GetPromptWorkspaceCurrentSectionText());
            DrawRimTalkTemplateValidationStatus(
                new Rect(inner.x, editorRect.yMax + 4f, inner.width, validationHeight),
                edited);

            if (!string.Equals(edited, _promptWorkspaceEditorBuffer, StringComparison.Ordinal))
            {
                SetPromptWorkspaceCurrentSectionText(edited);
            }
        }

        private void DrawPromptWorkspaceToolbar(Rect rect)
        {
            float buttonWidth = (rect.width - 12f) / 3f;
            Rect restoreSectionRect = new Rect(rect.x, rect.y, buttonWidth, rect.height);
            Rect restoreChannelRect = new Rect(restoreSectionRect.xMax + 6f, rect.y, buttonWidth, rect.height);
            Rect openReportRect = new Rect(restoreChannelRect.xMax + 6f, rect.y, buttonWidth, rect.height);

            if (Widgets.ButtonText(restoreSectionRect, "RimChat_PromptSectionRestoreSection".Translate()))
            {
                RestorePromptWorkspaceCurrentSection();
            }

            if (Widgets.ButtonText(restoreChannelRect, "RimChat_PromptSectionRestoreChannel".Translate()))
            {
                RestorePromptWorkspaceCurrentChannel();
            }

            if (Widgets.ButtonText(openReportRect, "RimChat_PromptMigrationResultButton".Translate()))
            {
                _workbenchSidePanelTab = PromptWorkbenchInfoPanel.Help;
            }
        }

        private void DrawPromptWorkspaceSectionList(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.03f, 0.03f, 0.04f));
            Rect inner = rect.ContractedBy(6f);
            IReadOnlyList<PromptSectionSchemaItem> sections = PromptSectionSchemaCatalog.GetMainChainSections();
            float rowHeight = 30f;
            Rect viewRect = new Rect(0f, 0f, inner.width - 16f, Mathf.Max(inner.height, sections.Count * rowHeight));
            Widgets.BeginScrollView(inner, ref _promptWorkspaceSectionScroll, viewRect);

            for (int i = 0; i < sections.Count; i++)
            {
                PromptSectionSchemaItem section = sections[i];
                Rect rowRect = new Rect(0f, i * rowHeight, viewRect.width, rowHeight - 2f);
                bool selected = string.Equals(_promptWorkspaceSelectedSectionId, section.Id, StringComparison.OrdinalIgnoreCase);
                if (selected)
                {
                    Widgets.DrawBoxSolid(rowRect, new Color(0.24f, 0.35f, 0.55f));
                }
                else if (Mouse.IsOver(rowRect))
                {
                    Widgets.DrawBoxSolid(rowRect, new Color(0.18f, 0.18f, 0.20f));
                }

                if (Widgets.ButtonInvisible(rowRect))
                {
                    SelectPromptWorkspaceSection(section.Id);
                }

                Widgets.Label(new Rect(rowRect.x + 8f, rowRect.y + 4f, rowRect.width - 16f, rowRect.height - 8f), section.EnglishName);
            }

            Widgets.EndScrollView();
        }

        private string DrawPromptWorkspaceEditor(Rect rect, string text)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.03f, 0.03f, 0.04f));
            Rect inner = rect.ContractedBy(6f);
            if (_promptWorkspaceChipEditorDisabledForSession || ExceedsChipEditorSoftLimits(text))
            {
                return DrawPromptWorkspaceLegacyTextArea(inner, text);
            }

            try
            {
                _promptWorkspaceChipEditor ??= new PromptWorkbenchChipEditor("RimChat_PromptWorkspaceSectionEditor");
                return _promptWorkspaceChipEditor.Draw(inner, text, ref _promptWorkspaceEditorScroll);
            }
            catch (Exception ex)
            {
                _promptWorkspaceChipEditorDisabledForSession = true;
                Log.Warning($"[RimChat] Prompt workspace chip editor fallback activated: {ex.GetType().Name}: {ex.Message}");
                return DrawPromptWorkspaceLegacyTextArea(inner, text);
            }
        }

        private void DrawPromptWorkspaceSidePanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.09f, 0.10f, 0.12f));
            Rect inner = rect.ContractedBy(8f);
            float buttonWidth = (inner.width - 12f) / 3f;
            Rect previewRect = new Rect(inner.x, inner.y, buttonWidth, 24f);
            Rect varsRect = new Rect(previewRect.xMax + 6f, inner.y, buttonWidth, 24f);
            Rect reportRect = new Rect(varsRect.xMax + 6f, inner.y, buttonWidth, 24f);

            DrawWorkbenchSideButton(previewRect, PromptWorkbenchInfoPanel.Preview, "RimChat_PreviewTitleShort");
            DrawWorkbenchSideButton(varsRect, PromptWorkbenchInfoPanel.Variables, "RimChat_PromptWorkbench_VariablesTab");
            DrawWorkbenchSideButton(reportRect, PromptWorkbenchInfoPanel.Help, "RimChat_PromptMigrationResultButton");

            Rect contentRect = new Rect(inner.x, previewRect.yMax + 6f, inner.width, inner.height - 30f);
            switch (_workbenchSidePanelTab)
            {
                case PromptWorkbenchInfoPanel.Preview:
                    DrawPromptWorkspacePreview(contentRect);
                    break;
                case PromptWorkbenchInfoPanel.Variables:
                    DrawPromptWorkspaceVariables(contentRect);
                    break;
                default:
                    DrawPromptWorkspaceReport(contentRect);
                    break;
            }
        }

        private void DrawPromptWorkspacePreview(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.03f, 0.03f, 0.04f));
            Rect inner = rect.ContractedBy(6f);
            string preview = GetPromptWorkspacePreviewText();
            string previewText = string.IsNullOrWhiteSpace(preview)
                ? "RimChat_PromptWorkbench_PreviewEmpty".Translate().ToString()
                : preview;
            DrawPromptWorkspacePreviewContent(inner, previewText);
        }

        private void DrawPromptWorkspaceVariables(Rect rect)
        {
            DrawPromptVariableBrowser(
                rect,
                _promptWorkspaceEditorBuffer,
                entry =>
                {
                    string token = "{{ " + (entry?.Path ?? string.Empty).Trim() + " }}";
                    return TryInsertVariableTokenToPromptWorkspace(token);
                });
        }

        private void DrawPromptWorkspaceReport(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.03f, 0.03f, 0.04f));
            Rect inner = rect.ContractedBy(6f);
            LegacyPromptMigrationReport report = PromptLegacyCompatMigration.GetLatestReport();
            string summary =
                $"Source: {report.SourceId}\n" +
                $"Imported: {report.ImportedCount}\n" +
                $"Rewritten: {report.RewrittenCount}\n" +
                $"Rejected: {report.RejectedCount}\n" +
                $"Defaulted: {report.DefaultedCount}\n\n";
            List<string> lines = report.Entries
                .Select(entry =>
                    $"- [{entry.Status}] {entry.PromptChannel}/{entry.SectionId} :: {entry.Detail}" +
                    (entry.FallbackApplied ? " (defaulted)" : string.Empty))
                .ToList();
            string body = summary + (lines.Count == 0 ? "No legacy import activity recorded in this session." : string.Join("\n", lines));
            float contentHeight = Mathf.Max(inner.height, Text.CalcHeight(body, inner.width - 16f) + 12f);
            Rect viewRect = new Rect(0f, 0f, inner.width - 16f, contentHeight);
            _promptWorkspaceReportScroll = GUI.BeginScrollView(inner, _promptWorkspaceReportScroll, viewRect);
            Widgets.Label(new Rect(0f, 0f, viewRect.width, contentHeight), body);
            GUI.EndScrollView();
        }

        private void SetPromptWorkspaceRoot(PromptWorkbenchChannel root)
        {
            _workbenchChannel = root;
            _workbenchPromptChannel = string.Empty;
            InvalidatePromptWorkspacePreviewCache();
            EnsurePromptWorkspaceSelection();
        }

        private RimTalkPromptChannel GetPromptWorkspaceRootChannel()
        {
            return _workbenchChannel == PromptWorkbenchChannel.Rpg
                ? RimTalkPromptChannel.Rpg
                : RimTalkPromptChannel.Diplomacy;
        }

        private IReadOnlyList<string> GetPromptWorkspaceChannels()
        {
            return PromptSectionSchemaCatalog.GetWorkspaceChannels(GetPromptWorkspaceRootChannel());
        }

        private string EnsurePromptWorkspaceSelection()
        {
            IReadOnlyList<string> channels = GetPromptWorkspaceChannels();
            string normalizedChannel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(_workbenchPromptChannel);
            if (!channels.Contains(normalizedChannel, StringComparer.Ordinal))
            {
                normalizedChannel = PromptSectionSchemaCatalog.GetDefaultWorkspaceChannel(GetPromptWorkspaceRootChannel());
            }

            _workbenchPromptChannel = normalizedChannel;
            if (!PromptSectionSchemaCatalog.TryGetSection(_promptWorkspaceSelectedSectionId, out PromptSectionSchemaItem _))
            {
                _promptWorkspaceSelectedSectionId = PromptSectionSchemaCatalog.GetMainChainSections()[0].Id;
            }

            EnsurePromptWorkspaceBuffer();
            return _workbenchPromptChannel;
        }

        private void SetPromptWorkspaceChannel(string channelId)
        {
            _workbenchPromptChannel = RimTalkPromptEntryChannelCatalog.NormalizeForRoot(channelId, GetPromptWorkspaceRootChannel());
            InvalidatePromptWorkspacePreviewCache();
            EnsurePromptWorkspaceSelection();
        }

        private void SelectPromptWorkspaceSection(string sectionId)
        {
            _promptWorkspaceSelectedSectionId = PromptSectionSchemaCatalog.NormalizeSectionId(sectionId);
            EnsurePromptWorkspaceBuffer();
        }

        private void EnsurePromptWorkspaceBuffer()
        {
            if (string.Equals(_promptWorkspaceBufferedChannel, _workbenchPromptChannel, StringComparison.Ordinal) &&
                string.Equals(_promptWorkspaceBufferedSectionId, _promptWorkspaceSelectedSectionId, StringComparison.Ordinal))
            {
                return;
            }

            _promptWorkspaceBufferedChannel = _workbenchPromptChannel ?? string.Empty;
            _promptWorkspaceBufferedSectionId = _promptWorkspaceSelectedSectionId ?? string.Empty;
            _promptWorkspaceEditorBuffer = GetPromptWorkspaceSectionText(_promptWorkspaceBufferedChannel, _promptWorkspaceBufferedSectionId);
        }

        private string GetPromptWorkspaceCurrentSectionText()
        {
            EnsurePromptWorkspaceBuffer();
            return _promptWorkspaceEditorBuffer ?? string.Empty;
        }

        private string GetPromptWorkspaceSectionText(string promptChannel, string sectionId)
        {
            RimTalkPromptEntryDefaultsConfig catalog = GetPromptSectionCatalogClone();
            return catalog.ResolveContent(promptChannel, sectionId) ?? string.Empty;
        }

        private void SetPromptWorkspaceCurrentSectionText(string text)
        {
            RimTalkPromptEntryDefaultsConfig catalog = GetPromptSectionCatalogClone();
            catalog.SetContent(_workbenchPromptChannel, _promptWorkspaceSelectedSectionId, text ?? string.Empty);
            SetPromptSectionCatalog(catalog);
            _promptWorkspaceEditorBuffer = text ?? string.Empty;
            _promptWorkspaceBufferedChannel = _workbenchPromptChannel ?? string.Empty;
            _promptWorkspaceBufferedSectionId = _promptWorkspaceSelectedSectionId ?? string.Empty;
            InvalidatePromptWorkspacePreviewCache();
        }

        private float ResolvePromptWorkspacePresetListHeight(float startY, float bottomY, float panelHeight)
        {
            float available = Mathf.Max(96f, bottomY - startY - 140f);
            float preferred = Mathf.Clamp(panelHeight * 0.28f, 96f, 220f);
            return Mathf.Clamp(preferred, 96f, available);
        }

        private string DrawPromptWorkspaceLegacyTextArea(Rect rect, string text)
        {
            string source = text ?? string.Empty;
            GUIStyle style = new GUIStyle(GUI.skin.textArea)
            {
                wordWrap = true,
                richText = false
            };
            float contentWidth = Mathf.Max(1f, rect.width - 16f);
            float contentHeight = Mathf.Max(rect.height, style.CalcHeight(new GUIContent(source), contentWidth) + 4f);
            Rect viewRect = new Rect(0f, 0f, contentWidth, contentHeight);
            _promptWorkspaceEditorScroll = new Vector2(
                0f,
                Mathf.Clamp(_promptWorkspaceEditorScroll.y, 0f, Mathf.Max(0f, viewRect.height - rect.height)));
            _promptWorkspaceEditorScroll = GUI.BeginScrollView(rect, _promptWorkspaceEditorScroll, viewRect, false, true);
            string edited = GUI.TextArea(new Rect(0f, 0f, contentWidth, contentHeight), source, style);
            GUI.EndScrollView();
            return edited;
        }

        private void DrawPromptWorkspacePreviewContent(Rect rect, string text)
        {
            if (_promptWorkspacePreviewChipViewerDisabledForSession || ExceedsChipEditorSoftLimits(text))
            {
                DrawPromptWorkspacePreviewFallback(rect, text);
                return;
            }

            try
            {
                _promptWorkspacePreviewChipViewer ??= new PromptWorkbenchChipEditor("RimChat_PromptWorkspacePreviewViewer");
                _promptWorkspacePreviewChipViewer.DrawReadOnly(rect, text, ref _promptWorkspacePreviewScroll);
            }
            catch (Exception ex)
            {
                _promptWorkspacePreviewChipViewerDisabledForSession = true;
                Log.Warning($"[RimChat] Prompt workspace preview chip viewer fallback activated: {ex.GetType().Name}: {ex.Message}");
                DrawPromptWorkspacePreviewFallback(rect, text);
            }
        }

        private void DrawPromptWorkspacePreviewFallback(Rect rect, string text)
        {
            float contentHeight = Mathf.Max(rect.height, Text.CalcHeight(text, rect.width - 16f) + 12f);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, contentHeight);
            _promptWorkspacePreviewScroll = GUI.BeginScrollView(rect, _promptWorkspacePreviewScroll, viewRect);
            Widgets.Label(new Rect(0f, 0f, viewRect.width, contentHeight), text);
            GUI.EndScrollView();
        }

        private void RestorePromptWorkspaceCurrentSection()
        {
            string fallback = RimTalkPromptEntryDefaultsProvider.ResolveContent(_workbenchPromptChannel, _promptWorkspaceSelectedSectionId);
            SetPromptWorkspaceCurrentSectionText(fallback);
        }

        private void RestorePromptWorkspaceCurrentChannel()
        {
            RimTalkPromptEntryDefaultsConfig catalog = GetPromptSectionCatalogClone();
            foreach (PromptSectionSchemaItem section in PromptSectionSchemaCatalog.GetMainChainSections())
            {
                catalog.SetContent(
                    _workbenchPromptChannel,
                    section.Id,
                    RimTalkPromptEntryDefaultsProvider.ResolveContent(_workbenchPromptChannel, section.Id));
            }

            SetPromptSectionCatalog(catalog);
            EnsurePromptWorkspaceBuffer();
            InvalidatePromptWorkspacePreviewCache();
        }

        private string GetPromptWorkspacePreviewText()
        {
            if (_promptWorkspacePreviewCacheValid &&
                _promptWorkspacePreviewCachedRoot == _workbenchChannel &&
                string.Equals(_promptWorkspacePreviewCachedChannel, _workbenchPromptChannel, StringComparison.Ordinal))
            {
                return _promptWorkspacePreviewCachedText ?? string.Empty;
            }

            _promptWorkspacePreviewCachedRoot = _workbenchChannel;
            _promptWorkspacePreviewCachedChannel = _workbenchPromptChannel ?? string.Empty;
            _promptWorkspacePreviewCachedText = PromptPersistenceService.Instance.BuildPromptSectionAggregatePreview(
                GetPromptWorkspaceRootChannel(),
                _workbenchPromptChannel);
            _promptWorkspacePreviewCacheValid = true;
            return _promptWorkspacePreviewCachedText ?? string.Empty;
        }

        private void InvalidatePromptWorkspacePreviewCache()
        {
            _promptWorkspacePreviewCacheValid = false;
            _promptWorkspacePreviewCachedChannel = string.Empty;
            _promptWorkspacePreviewCachedText = string.Empty;
        }

        private bool TryInsertVariableTokenToPromptWorkspace(string token)
        {
            if (!CanInsertVariableTokenToPromptWorkspace())
            {
                return false;
            }

            string normalized = NormalizeVariableNameToken(token);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            string current = GetPromptWorkspaceCurrentSectionText();
            if (ContainsVariableToken(current, normalized))
            {
                Messages.Message("RimChat_RimTalkVariableAlreadyInTemplate".Translate(), MessageTypeDefOf.NeutralEvent, false);
                return true;
            }

            string wrapped = "{{ " + normalized + " }}";
            string updated = string.IsNullOrWhiteSpace(current)
                ? wrapped
                : current.TrimEnd() + "\n" + wrapped;
            SetPromptWorkspaceCurrentSectionText(updated);
            Messages.Message("RimChat_RimTalkVariableInserted".Translate(wrapped), MessageTypeDefOf.NeutralEvent, false);
            return true;
        }

        private bool CanInsertVariableTokenToPromptWorkspace()
        {
            EnsurePromptWorkspaceSelection();
            if (string.IsNullOrWhiteSpace(_workbenchPromptChannel))
            {
                return false;
            }

            return PromptSectionSchemaCatalog.TryGetSection(_promptWorkspaceSelectedSectionId, out PromptSectionSchemaItem _);
        }
    }
}
