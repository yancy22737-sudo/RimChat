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
    /// Dependencies: prompt preset service, unified prompt catalog projection helpers, aggregate preview builder, shared variable browser, and chip editor.
    /// Responsibility: render the unified-only prompt workspace and keep editor mutations in memory until explicit save.
    /// </summary>
    public partial class RimChatSettings : ModSettings
    {
        private string _promptWorkspaceSelectedSectionId = "system_rules";
        private string _promptWorkspaceSelectedNodeId = "fact_grounding";
        private bool _promptWorkspaceEditNodeMode;
        private string _promptWorkspaceBufferedChannel = string.Empty;
        private string _promptWorkspaceBufferedSectionId = string.Empty;
        private string _promptWorkspaceBufferedNodeId = string.Empty;
        private bool _promptWorkspaceBufferedNodeMode;
        private string _promptWorkspaceEditorBuffer = string.Empty;
        private string _promptWorkspaceLastRenderedEditorTarget = string.Empty;
        private string _promptWorkspaceLastRenderedEditorText = string.Empty;
        private Vector2 _promptWorkspaceSectionScroll = Vector2.zero;
        private Vector2 _promptWorkspaceNodeScroll = Vector2.zero;
        private Vector2 _promptWorkspaceEditorScroll = Vector2.zero;
        private Vector2 _promptWorkspacePreviewScroll = Vector2.zero;
        private Vector2 _promptWorkspaceReportScroll = Vector2.zero;
        private PromptWorkbenchChannel _promptWorkspacePreviewCachedRoot;
        private string _promptWorkspacePreviewCachedChannel = string.Empty;
        private string _promptWorkspacePreviewCachedSignature = string.Empty;
        private PromptWorkspaceStructuredPreview _promptWorkspacePreviewCachedData;
        private bool _promptWorkspacePreviewCacheValid;
        private PromptWorkspaceIncrementalPreviewBuildState _promptWorkspacePreviewBuildState;
        private bool _promptWorkspaceHasPendingPersist;
        private bool _promptWorkspaceLastPersistHadMaterialChange;
        private DateTime _promptWorkspaceLastEditUtc = DateTime.MinValue;
        private TemplateVariableValidationResult _promptWorkspaceValidationResult = new TemplateVariableValidationResult();
        private string _promptWorkspaceValidationSignature = string.Empty;
        private int _promptWorkspaceValidationCooldown;
        private const int PromptWorkspaceValidationRefreshTicks = 15;
        private const float PromptWorkspacePreviewFrameBudgetSeconds = 0.002f;
        private const string PromptWorkspaceEditorControlName = "RimChat_PromptWorkspaceSectionEditor";
        private PromptWorkbenchChipEditor _promptWorkspaceChipEditor;
        private PromptWorkspaceStructuredPreviewRenderer _promptWorkspacePreviewRenderer;
        private bool _promptWorkspaceChipEditorDisabledForSession;
        private string _promptWorkspaceDraggingNodeId = string.Empty;
        private string _promptWorkspaceDropTargetNodeId = string.Empty;
        private string _promptWorkspaceNodeListCacheChannel = string.Empty;
        private List<PromptUnifiedNodeSchemaItem> _promptWorkspaceNodeListCache = new List<PromptUnifiedNodeSchemaItem>();
        private string _promptWorkspaceNodeLayoutCacheChannel = string.Empty;
        private List<PromptUnifiedNodeLayoutConfig> _promptWorkspaceNodeLayoutCache = new List<PromptUnifiedNodeLayoutConfig>();
        private Action _promptWorkspaceDeferredNavigationAction;

        private void DrawPromptSectionWorkspace(Rect root)
        {
            EnsurePresetStoreReady();
            EnsurePromptWorkspaceSelection();
            TryRunDeferredPromptWorkspaceNavigation();
            TryAutoSavePromptWorkspaceBuffer();
            TickPromptWorkspacePreviewBuild(PromptWorkspacePreviewFrameBudgetSeconds);

            Widgets.DrawBoxSolid(root, new Color(0.08f, 0.09f, 0.11f));
            Rect frame = root.ContractedBy(8f);
            Rect headerRect = new Rect(frame.x, frame.y, frame.width, 74f);
            Rect bodyRect = new Rect(frame.x, headerRect.yMax + 6f, frame.width, frame.height - headerRect.height - 6f);

            DrawPromptWorkspaceHeader(headerRect);
            DrawPromptWorkspaceBody(bodyRect);
        }

        private void SchedulePromptWorkspaceNavigation(Action action)
        {
            if (action == null)
            {
                return;
            }

            _promptWorkspaceDeferredNavigationAction = action;
            GUI.FocusControl(string.Empty);
        }

        private void TryRunDeferredPromptWorkspaceNavigation()
        {
            if (_promptWorkspaceDeferredNavigationAction == null)
            {
                return;
            }

            Action action = _promptWorkspaceDeferredNavigationAction;
            _promptWorkspaceDeferredNavigationAction = null;
            action.Invoke();
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
            DrawPromptWorkspaceQuickActions(new Rect(inner.x + 570f, top, Mathf.Max(220f, inner.xMax - (inner.x + 570f) - 196f), 30f));

            Rect importRect = new Rect(inner.xMax - 180f, top, 84f, 30f);
            Rect exportRect = new Rect(inner.xMax - 90f, top, 84f, 30f);
            if (Widgets.ButtonText(importRect, "RimChat_Import".Translate()))
            {
                if (!PersistPromptWorkspaceBufferNow(force: true))
                {
                    return;
                }

                ShowImportPresetDialog();
            }

            if (Widgets.ButtonText(exportRect, "RimChat_Export".Translate()))
            {
                if (!PersistPromptWorkspaceBufferNow(force: true))
                {
                    return;
                }

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
                SchedulePromptWorkspaceNavigation(() => SetPromptWorkspaceRoot(channel));
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
                        () => SchedulePromptWorkspaceNavigation(() => SetPromptWorkspaceChannel(channelId))))
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

            DrawPromptWorkspaceEditorPanel(middleRect);
            DrawPromptWorkspacePresetPanel(leftRect);
            DrawPromptWorkspaceSidePanel(rightRect);
        }

        private void DrawPromptWorkspacePresetPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.09f, 0.10f, 0.12f));
            Rect inner = rect.ContractedBy(8f);
            float y = inner.y;
            Widgets.Label(new Rect(inner.x, y, inner.width, 22f), "RimChat_PromptWorkbench_PresetHeader".Translate());
            y += 24f;
            DrawPromptWorkspacePresetActions(new Rect(inner.x, y, inner.width, 24f));
            y += 28f;
            float presetListHeight = ResolvePromptWorkspacePresetListHeight(y, inner.yMax, inner.height);
            DrawPromptWorkspacePresetList(new Rect(inner.x, y, inner.width, presetListHeight));
            y += presetListHeight + 8f;

            Widgets.Label(
                new Rect(inner.x, y, inner.width, 22f),
                (_promptWorkspaceEditNodeMode
                    ? "RimChat_PromptWorkspaceNodeLayoutHeader"
                    : "RimChat_PromptWorkspaceSectionHeader").Translate());
            y += 24f;
            float sectionHeight = Mathf.Max(72f, inner.yMax - y - 6f);
            if (_promptWorkspaceEditNodeMode)
            {
                DrawPromptWorkspaceNodeLayoutList(new Rect(inner.x, y, inner.width, sectionHeight));
            }
            else
            {
                DrawPromptWorkspaceSectionList(new Rect(inner.x, y, inner.width, sectionHeight));
            }
        }

        private void DrawPromptWorkspaceEditorPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.06f, 0.07f, 0.09f));
            Rect inner = rect.ContractedBy(8f);
            float y = inner.y;
            const float validationHeight = 24f;

            DrawWorkbenchPresetNameRow(inner, ref y);
            HandlePromptWorkspaceKeyboardShortcuts();
            Rect toolbarRect = new Rect(inner.x, y, inner.width, 26f);
            y += 32f;
            Rect modeRect = new Rect(inner.x, y, inner.width, 24f);
            y += 26f;
            Rect selectorOrLabelRect = new Rect(inner.x, y, inner.width, _promptWorkspaceEditNodeMode ? 24f : 22f);
            y += _promptWorkspaceEditNodeMode ? 26f : 24f;

            float editorHeight = Mathf.Max(24f, inner.yMax - y - validationHeight - 4f);
            Rect editorRect = new Rect(inner.x, y, inner.width, editorHeight);
            bool dynamicModVariablesSection = IsPromptWorkspaceDynamicModVariablesSection();
            string sourceText = GetPromptWorkspaceCurrentEditorText();
            if (dynamicModVariablesSection && string.IsNullOrWhiteSpace(sourceText))
            {
                sourceText = BuildPromptWorkspaceDynamicModVariablesText();
            }

            string edited = DrawPromptWorkspaceEditor(editorRect, sourceText);
            CachePromptWorkspaceRenderedEditorText(edited);
            CapturePromptWorkspaceLiveEditorText();
            DrawPromptWorkspaceValidationStatus(
                new Rect(inner.x, editorRect.yMax + 4f, inner.width, validationHeight),
                edited);

            if (!string.Equals(edited, _promptWorkspaceEditorBuffer, StringComparison.Ordinal))
            {
                SetPromptWorkspaceCurrentEditorText(edited);
            }

            DrawPromptWorkspaceToolbar(toolbarRect);
            DrawPromptWorkspaceEditModeSwitch(modeRect);
            if (_promptWorkspaceEditNodeMode)
            {
                DrawPromptWorkspaceNodeSelector(selectorOrLabelRect);
            }
            else
            {
                PromptSectionSchemaCatalog.TryGetSection(_promptWorkspaceSelectedSectionId, out PromptSectionSchemaItem section);
                Widgets.Label(selectorOrLabelRect, section.GetDisplayLabel());
            }

            HandlePromptWorkspaceKeyboardShortcuts();
        }

        private void DrawPromptWorkspaceValidationStatus(Rect rect, string templateText)
        {
            if (IsPromptWorkspaceDynamicModVariablesSection())
            {
                _promptWorkspaceValidationResult = new TemplateVariableValidationResult();
                _promptWorkspaceValidationSignature = "mod_variables.dynamic.skip";
                _promptWorkspaceValidationCooldown = 0;
                Color skippedColor = GUI.color;
                GUI.color = Color.gray;
                Widgets.Label(rect, "RimChat_PromptLiveValidationOk".Translate(0));
                GUI.color = skippedColor;
                return;
            }

            TemplateVariableValidationContext validationContext = BuildPromptWorkspaceValidationContext();
            UpdatePromptWorkspaceValidationState(templateText, validationContext);
            string statusText = BuildLiveValidationStatusText(_promptWorkspaceValidationResult, templateText);
            Color oldColor = GUI.color;
            GUI.color = ResolveLiveValidationStatusColor(_promptWorkspaceValidationResult, templateText);
            Widgets.Label(rect, statusText);
            GUI.color = oldColor;
        }

        private TemplateVariableValidationContext BuildPromptWorkspaceValidationContext()
        {
            return _promptWorkspaceEditNodeMode
                ? TemplateVariableValidationContext.ForPromptWorkspaceNode(
                    GetPromptWorkspaceRootChannel(),
                    _workbenchPromptChannel,
                    _promptWorkspaceSelectedNodeId)
                : TemplateVariableValidationContext.ForPromptWorkspaceSection(
                    GetPromptWorkspaceRootChannel(),
                    _workbenchPromptChannel,
                    _promptWorkspaceSelectedSectionId);
        }

        private void UpdatePromptWorkspaceValidationState(
            string templateText,
            TemplateVariableValidationContext validationContext)
        {
            string contextSignature = validationContext?.Signature ?? "runtime.default";
            string signature = contextSignature + "\n" + (templateText ?? string.Empty);
            _promptWorkspaceValidationCooldown = Math.Max(0, _promptWorkspaceValidationCooldown - 1);
            if (_promptWorkspaceValidationCooldown > 0 &&
                string.Equals(signature, _promptWorkspaceValidationSignature, StringComparison.Ordinal))
            {
                return;
            }

            _promptWorkspaceValidationSignature = signature;
            _promptWorkspaceValidationCooldown = PromptWorkspaceValidationRefreshTicks;
            _promptWorkspaceValidationResult = string.IsNullOrWhiteSpace(templateText)
                ? new TemplateVariableValidationResult()
                : PromptPersistenceService.Instance.ValidateTemplateVariables(templateText, validationContext);
        }

        private void DrawPromptWorkspaceEditModeSwitch(Rect rect)
        {
            float buttonWidth = (rect.width - 6f) * 0.5f;
            Rect sectionRect = new Rect(rect.x, rect.y, buttonWidth, rect.height);
            Rect nodeRect = new Rect(sectionRect.xMax + 6f, rect.y, buttonWidth, rect.height);
            bool hasEditableNodes = GetPromptWorkspaceEditableNodes().Count > 0;
            DrawPromptWorkspaceModeButton(sectionRect, false, "RimChat_PromptWorkspaceMode_Sections".Translate().ToString(), true);
            DrawPromptWorkspaceModeButton(nodeRect, true, "RimChat_PromptWorkspaceMode_Nodes".Translate().ToString(), hasEditableNodes);
        }

        private void DrawPromptWorkspaceModeButton(Rect rect, bool nodeMode, string label, bool active)
        {
            bool selected = _promptWorkspaceEditNodeMode == nodeMode;
            Color selectedColor = active ? new Color(0.24f, 0.35f, 0.55f) : new Color(0.16f, 0.16f, 0.16f);
            Color normalColor = active ? new Color(0.13f, 0.15f, 0.18f) : new Color(0.10f, 0.10f, 0.10f);
            Widgets.DrawBoxSolid(rect, selected ? selectedColor : normalColor);
            Widgets.DrawBox(rect, 1);
            TextAnchor old = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Color oldColor = GUI.color;
            GUI.color = active ? Color.white : new Color(0.60f, 0.60f, 0.60f);
            Widgets.Label(rect, label);
            GUI.color = oldColor;
            Text.Anchor = old;
            if (active && Widgets.ButtonInvisible(rect) && _promptWorkspaceEditNodeMode != nodeMode)
            {
                SchedulePromptWorkspaceNavigation(() =>
                {
                    if (!PersistPromptWorkspaceBufferNow(force: true))
                    {
                        return;
                    }

                    _promptWorkspaceEditNodeMode = nodeMode;
                    if (_promptWorkspaceEditNodeMode)
                    {
                        EnsurePromptWorkspaceNodeLayoutCoverage(_workbenchPromptChannel, GetPromptWorkspaceEditableNodes());
                    }

                    EnsurePromptWorkspaceBuffer();
                    InvalidatePromptWorkspacePreviewCache();
                });
            }
        }

        private void DrawPromptWorkspaceNodeSelector(Rect rect)
        {
            List<PromptUnifiedNodeSchemaItem> editableNodes = GetPromptWorkspaceEditableNodes();
            if (editableNodes.Count == 0)
            {
                _promptWorkspaceEditNodeMode = false;
                EnsurePromptWorkspaceBuffer();
                return;
            }

            string current = PromptUnifiedNodeSchemaCatalog.GetDisplayLabel(_promptWorkspaceSelectedNodeId);
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.14f, 0.18f));
            Widgets.DrawBox(rect, 1);
            Widgets.Label(new Rect(rect.x + 8f, rect.y, rect.width - 28f, rect.height), current);
            TextAnchor old = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(rect.xMax - 22f, rect.y, 18f, rect.height), "▼");
            Text.Anchor = old;
            if (!Widgets.ButtonInvisible(rect))
            {
                return;
            }

            List<FloatMenuOption> options = editableNodes
                .Select(node => new FloatMenuOption(PromptUnifiedNodeSchemaCatalog.GetDisplayLabel(node.Id), () =>
                {
                    SchedulePromptWorkspaceNavigation(() =>
                    {
                        if (!PersistPromptWorkspaceBufferNow(force: true))
                        {
                            return;
                        }

                        _promptWorkspaceSelectedNodeId = node.Id;
                        EnsurePromptWorkspaceBuffer();
                        InvalidatePromptWorkspacePreviewCache();
                    });
                }))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
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
                _promptWorkspaceChipEditor ??= new PromptWorkbenchChipEditor(PromptWorkspaceEditorControlName);
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
            DrawWorkbenchSideButton(reportRect, PromptWorkbenchInfoPanel.Help, "RimChat_PromptWorkbench_GuideTab");

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
            PromptWorkspaceStructuredPreview preview = GetPromptWorkspaceStructuredPreview();
            DrawPromptWorkspaceStructuredPreview(inner, preview);
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
                },
                showCustomCrud: true);
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
            if (!PersistPromptWorkspaceBufferNow(force: true))
            {
                return;
            }

            _workbenchChannel = root;
            _workbenchPromptChannel = string.Empty;
            InvalidatePromptWorkspaceNodeUiCaches();
            InvalidatePromptWorkspacePreviewCache();
            EnsurePromptWorkspaceSelection();
            EnsurePromptWorkspaceNodeLayoutCoverage(_workbenchPromptChannel, GetPromptWorkspaceEditableNodes());
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

            List<PromptUnifiedNodeSchemaItem> allowedNodes = PromptUnifiedNodeSchemaCatalog
                .GetAllowedNodes(_workbenchPromptChannel)
                .ToList();
            if (allowedNodes.Count == 0)
            {
                _promptWorkspaceSelectedNodeId = string.Empty;
                _promptWorkspaceEditNodeMode = false;
            }
            else if (!allowedNodes.Any(item =>
                         string.Equals(item.Id, _promptWorkspaceSelectedNodeId, StringComparison.OrdinalIgnoreCase)))
            {
                _promptWorkspaceSelectedNodeId = allowedNodes[0].Id;
            }

            EnsurePromptWorkspaceBuffer();
            return _workbenchPromptChannel;
        }

        private void EnsurePromptWorkspaceNodeLayoutCoverage(
            string channel,
            IReadOnlyList<PromptUnifiedNodeSchemaItem> allowedNodes)
        {
            if (string.IsNullOrWhiteSpace(channel) || allowedNodes == null || allowedNodes.Count == 0)
            {
                return;
            }

            List<PromptUnifiedNodeLayoutConfig> layouts = GetPromptNodeLayouts(channel)
                .Select(item => item.Clone())
                .ToList();
            var allowedSet = new HashSet<string>(
                allowedNodes.Select(item => item.Id),
                StringComparer.OrdinalIgnoreCase);
            var existingSet = new HashSet<string>(
                layouts.Where(item => item != null && !string.IsNullOrWhiteSpace(item.NodeId))
                    .Select(item => item.NodeId),
                StringComparer.OrdinalIgnoreCase);
            bool changed = false;
            foreach (PromptUnifiedNodeSchemaItem node in allowedNodes)
            {
                if (existingSet.Contains(node.Id))
                {
                    continue;
                }

                layouts.Add(PromptUnifiedNodeLayoutDefaults.BuildDefaultLayout(channel, node.Id));
                existingSet.Add(node.Id);
                changed = true;
            }

            for (int i = layouts.Count - 1; i >= 0; i--)
            {
                PromptUnifiedNodeLayoutConfig layout = layouts[i];
                if (layout == null || string.IsNullOrWhiteSpace(layout.NodeId) || !allowedSet.Contains(layout.NodeId))
                {
                    layouts.RemoveAt(i);
                    changed = true;
                }
            }

            if (!changed)
            {
                return;
            }

            SavePromptNodeLayouts(channel, layouts, persistToFiles: false);
            InvalidatePromptWorkspaceNodeUiCaches();
            InvalidatePromptWorkspacePreviewCache();
        }

        private void SetPromptWorkspaceChannel(string channelId)
        {
            if (!PersistPromptWorkspaceBufferNow(force: true))
            {
                return;
            }

            _workbenchPromptChannel = RimTalkPromptEntryChannelCatalog.NormalizeForRoot(channelId, GetPromptWorkspaceRootChannel());
            InvalidatePromptWorkspaceNodeUiCaches();
            InvalidatePromptWorkspacePreviewCache();
            EnsurePromptWorkspaceSelection();
            EnsurePromptWorkspaceNodeLayoutCoverage(_workbenchPromptChannel, GetPromptWorkspaceEditableNodes());
        }

        private void SelectPromptWorkspaceSection(string sectionId)
        {
            if (!PersistPromptWorkspaceBufferNow(force: true))
            {
                return;
            }

            _promptWorkspaceSelectedSectionId = PromptSectionSchemaCatalog.NormalizeSectionId(sectionId);
            EnsurePromptWorkspaceBuffer();
        }

        private void EnsurePromptWorkspaceBuffer()
        {
            string targetId = _promptWorkspaceEditNodeMode ? _promptWorkspaceSelectedNodeId : _promptWorkspaceSelectedSectionId;
            if (string.Equals(_promptWorkspaceBufferedChannel, _workbenchPromptChannel, StringComparison.Ordinal) &&
                _promptWorkspaceBufferedNodeMode == _promptWorkspaceEditNodeMode &&
                string.Equals(_promptWorkspaceEditNodeMode ? _promptWorkspaceBufferedNodeId : _promptWorkspaceBufferedSectionId, targetId, StringComparison.Ordinal))
            {
                return;
            }

            _promptWorkspaceBufferedChannel = _workbenchPromptChannel ?? string.Empty;
            _promptWorkspaceBufferedNodeMode = _promptWorkspaceEditNodeMode;
            _promptWorkspaceBufferedSectionId = _promptWorkspaceSelectedSectionId ?? string.Empty;
            _promptWorkspaceBufferedNodeId = _promptWorkspaceSelectedNodeId ?? string.Empty;
            _promptWorkspaceEditorBuffer = _promptWorkspaceEditNodeMode
                ? GetPromptWorkspaceNodeText(_promptWorkspaceBufferedChannel, _promptWorkspaceBufferedNodeId)
                : GetPromptWorkspaceSectionText(_promptWorkspaceBufferedChannel, _promptWorkspaceBufferedSectionId);
        }

        private string GetPromptWorkspaceCurrentEditorText()
        {
            EnsurePromptWorkspaceBuffer();
            return _promptWorkspaceEditorBuffer ?? string.Empty;
        }

        private string GetPromptWorkspaceSectionText(string promptChannel, string sectionId)
        {
            RimTalkPromptEntryDefaultsConfig catalog = GetPromptSectionCatalogClone();
            return catalog.ResolveContent(promptChannel, sectionId) ?? string.Empty;
        }

        private string GetPromptWorkspaceNodeText(string promptChannel, string nodeId)
        {
            string text = ResolvePromptNodeText(promptChannel, nodeId);
            if (!string.Equals(nodeId, "thought_chain_node_template", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            // Self-heal: if thought-chain node is unexpectedly empty, restore from default merged catalog.
            PromptUnifiedCatalog merged = PromptUnifiedCatalogProvider.LoadMerged();
            string recovered = merged?.ResolveNode(promptChannel, nodeId) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(recovered))
            {
                return text;
            }

            SetPromptNodeText(promptChannel, nodeId, recovered, persistToFiles: false);
            return recovered;
        }

        private void SetPromptWorkspaceCurrentEditorText(string text)
        {
            string next = text ?? string.Empty;
            if (string.Equals(next, _promptWorkspaceEditorBuffer ?? string.Empty, StringComparison.Ordinal))
            {
                return;
            }

            RecordPromptWorkspaceTextHistoryBeforeMutation(_promptWorkspaceEditorBuffer ?? string.Empty);
            SetPromptWorkspaceCurrentEditorTextWithoutHistory(next);
        }

        private void MarkPromptWorkspaceDirty()
        {
            _promptWorkspaceHasPendingPersist = true;
            _promptWorkspaceLastEditUtc = DateTime.UtcNow;
        }

        private void TryAutoSavePromptWorkspaceBuffer()
        {
            // Unified-only workspace persists to disk only via explicit Save.
        }

        private bool PersistPromptWorkspaceBufferNow(bool force = false, bool persistToDisk = false)
        {
            if (force)
            {
                EnsurePromptWorkspaceSelection();
            }

            string targetChannel = force ? (_workbenchPromptChannel ?? string.Empty) : (_promptWorkspaceBufferedChannel ?? string.Empty);
            bool targetNodeMode = force ? _promptWorkspaceEditNodeMode : _promptWorkspaceBufferedNodeMode;
            string targetSectionId = force ? (_promptWorkspaceSelectedSectionId ?? string.Empty) : (_promptWorkspaceBufferedSectionId ?? string.Empty);
            string targetNodeId = force ? (_promptWorkspaceSelectedNodeId ?? string.Empty) : (_promptWorkspaceBufferedNodeId ?? string.Empty);
            ApplyRenderedEditorSnapshotToPromptWorkspaceBuffer(
                targetChannel,
                targetNodeMode,
                targetSectionId,
                targetNodeId);
            CapturePromptWorkspaceLiveEditorText();
            _promptWorkspaceLastPersistHadMaterialChange = false;

            if (!_promptWorkspaceHasPendingPersist)
            {
                if (persistToDisk && HasPendingUnifiedPromptCatalogChanges())
                {
                    PersistUnifiedPromptCatalogToCustom();
                }

                return true;
            }

            if (string.IsNullOrWhiteSpace(targetChannel))
            {
                _promptWorkspaceHasPendingPersist = false;
                _promptWorkspaceLastEditUtc = DateTime.MinValue;
                return false;
            }

            string bufferedText = _promptWorkspaceEditorBuffer ?? string.Empty;
            bool changed = false;
            if (targetNodeMode)
            {
                if (!string.IsNullOrWhiteSpace(targetNodeId))
                {
                    string current = GetPromptWorkspaceNodeText(targetChannel, targetNodeId);
                    if (!string.Equals(current ?? string.Empty, bufferedText, StringComparison.Ordinal))
                    {
                        if (!EnsurePromptWorkspaceEditablePresetForMutation("workspace.persist_node"))
                        {
                            return false;
                        }

                        SetPromptNodeText(targetChannel, targetNodeId, bufferedText, persistToDisk);
                        changed = true;
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(targetSectionId))
            {
                string current = GetPromptWorkspaceSectionText(targetChannel, targetSectionId);
                if (!string.Equals(current ?? string.Empty, bufferedText, StringComparison.Ordinal))
                {
                    if (!EnsurePromptWorkspaceEditablePresetForMutation("workspace.persist_section"))
                    {
                        return false;
                    }

                    SetPromptSectionText(targetChannel, targetSectionId, bufferedText, persistToDisk);
                    changed = true;
                }

                _promptWorkspaceBufferedChannel = targetChannel;
                _promptWorkspaceBufferedNodeMode = false;
                _promptWorkspaceBufferedSectionId = targetSectionId;
                _promptWorkspaceBufferedNodeId = targetNodeId;
            }

            _promptWorkspaceHasPendingPersist = false;
            _promptWorkspaceLastEditUtc = DateTime.MinValue;
            if (changed)
            {
                if (_promptPresetService != null && _promptPresetStore != null)
                {
                    string syncError = string.Empty;
                    bool syncOk = _promptPresetService.SyncPresetPayloadFromSettings(
                        this,
                        _promptPresetStore,
                        _selectedPromptPresetId,
                        out syncError);
                    if (syncOk)
                    {
                        if (persistToDisk)
                        {
                            _promptPresetService.SaveAll(_promptPresetStore);
                        }
                    }
                    else
                    {
                        Log.Warning($"[RimChat] Prompt workspace preset payload sync failed: {syncError}");
                        Messages.Message(
                            "RimChat_PromptPreset_AutoForkFailed".Translate(syncError ?? "workspace.sync_payload"),
                            MessageTypeDefOf.RejectInput,
                            false);
                        _promptWorkspaceLastPersistHadMaterialChange = false;
                        _promptWorkspaceHasPendingPersist = true;
                        _promptWorkspaceLastEditUtc = DateTime.MinValue;
                        return false;
                    }
                }

                _promptWorkspaceLastPersistHadMaterialChange = true;
                InvalidatePromptWorkspaceNodeUiCaches();
                InvalidatePromptWorkspacePreviewCache();
            }

            if (persistToDisk && HasPendingUnifiedPromptCatalogChanges())
            {
                PersistUnifiedPromptCatalogToCustom();
            }

            return true;
        }

        private void CachePromptWorkspaceRenderedEditorText(string text)
        {
            _promptWorkspaceLastRenderedEditorTarget = BuildPromptWorkspaceEditorTargetSignature(
                _workbenchPromptChannel,
                _promptWorkspaceEditNodeMode,
                _promptWorkspaceSelectedSectionId,
                _promptWorkspaceSelectedNodeId);
            _promptWorkspaceLastRenderedEditorText = text ?? string.Empty;
        }

        private void ApplyRenderedEditorSnapshotToPromptWorkspaceBuffer(
            string promptChannel,
            bool nodeMode,
            string sectionId,
            string nodeId)
        {
            string target = BuildPromptWorkspaceEditorTargetSignature(promptChannel, nodeMode, sectionId, nodeId);
            if (!string.Equals(target, _promptWorkspaceLastRenderedEditorTarget, StringComparison.Ordinal))
            {
                return;
            }

            string renderedText = _promptWorkspaceLastRenderedEditorText ?? string.Empty;
            if (string.Equals(renderedText, _promptWorkspaceEditorBuffer ?? string.Empty, StringComparison.Ordinal))
            {
                return;
            }

            _promptWorkspaceEditorBuffer = renderedText;
            _promptWorkspaceBufferedChannel = promptChannel ?? string.Empty;
            _promptWorkspaceBufferedNodeMode = nodeMode;
            _promptWorkspaceBufferedSectionId = sectionId ?? string.Empty;
            _promptWorkspaceBufferedNodeId = nodeId ?? string.Empty;
            _promptWorkspaceHasPendingPersist = true;
            _promptWorkspaceLastEditUtc = DateTime.UtcNow;
        }

        private static string BuildPromptWorkspaceEditorTargetSignature(
            string promptChannel,
            bool nodeMode,
            string sectionId,
            string nodeId)
        {
            string targetId = nodeMode ? (nodeId ?? string.Empty) : (sectionId ?? string.Empty);
            return $"{promptChannel ?? string.Empty}|{(nodeMode ? "node" : "section")}|{targetId}";
        }


        internal void FlushPromptWorkspaceEdits(bool persistToDisk = false)
        {
            PersistPromptWorkspaceBufferNow(force: false, persistToDisk: persistToDisk);
        }

        private float ResolvePromptWorkspacePresetListHeight(float startY, float bottomY, float panelHeight)
        {
            float available = Mathf.Max(96f, bottomY - startY - 170f);
            float preferred = Mathf.Clamp(panelHeight * 0.32f, 96f, 280f);
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
            GUI.SetNextControlName(PromptWorkspaceEditorControlName);
            string edited = GUI.TextArea(new Rect(0f, 0f, contentWidth, contentHeight), source, style);
            GUI.EndScrollView();
            return edited;
        }

        private bool IsPromptWorkspaceDynamicModVariablesSection()
        {
            if (_promptWorkspaceEditNodeMode || _workbenchChannel != PromptWorkbenchChannel.Rpg)
            {
                return false;
            }

            if (!string.Equals(
                    PromptSectionSchemaCatalog.NormalizeSectionId(_promptWorkspaceSelectedSectionId),
                    "mod_variables",
                    StringComparison.Ordinal))
            {
                return false;
            }

            string normalizedChannel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(_workbenchPromptChannel);
            return normalizedChannel == RimTalkPromptEntryChannelCatalog.RpgDialogue ||
                   normalizedChannel == RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue ||
                   normalizedChannel == RimTalkPromptEntryChannelCatalog.PersonaBootstrap ||
                   normalizedChannel == RimTalkPromptEntryChannelCatalog.RpgArchiveCompression;
        }

        private static string BuildPromptWorkspaceDynamicModVariablesText()
        {
            PromptRuntimeVariableBridge.RefreshRimTalkCustomVariableSnapshot();
            return PromptRuntimeVariableBridge.BuildModVariablesSectionContent();
        }

        private void DrawPromptWorkspaceStructuredPreview(Rect rect, PromptWorkspaceStructuredPreview preview)
        {
            _promptWorkspacePreviewRenderer ??= new PromptWorkspaceStructuredPreviewRenderer();
            _promptWorkspacePreviewRenderer.Draw(rect, preview, ref _promptWorkspacePreviewScroll);
        }

        private PromptWorkspaceStructuredPreview GetPromptWorkspaceStructuredPreview()
        {
            TickPromptWorkspacePreviewBuild(PromptWorkspacePreviewFrameBudgetSeconds);
            if (_promptWorkspacePreviewCachedRoot != _workbenchChannel)
            {
                InvalidatePromptWorkspacePreviewCache();
                TickPromptWorkspacePreviewBuild(PromptWorkspacePreviewFrameBudgetSeconds);
            }

            if (!string.Equals(_promptWorkspacePreviewCachedChannel, _workbenchPromptChannel ?? string.Empty, StringComparison.Ordinal))
            {
                InvalidatePromptWorkspacePreviewCache();
                TickPromptWorkspacePreviewBuild(PromptWorkspacePreviewFrameBudgetSeconds);
            }

            return _promptWorkspacePreviewCachedData ?? new PromptWorkspaceStructuredPreview();
        }

        private void TickPromptWorkspacePreviewBuild(float frameBudgetSeconds)
        {
            EnsurePromptWorkspaceSelection();
            EnsurePromptWorkspacePreviewBuildState();
            if (_promptWorkspacePreviewBuildState == null)
            {
                return;
            }

            float start = Time.realtimeSinceStartup;
            do
            {
                PromptPersistenceService.Instance.StepPromptWorkspaceIncrementalPreviewBuild(_promptWorkspacePreviewBuildState);
                SyncPromptWorkspacePreviewCacheFromBuildState();
                if (_promptWorkspacePreviewBuildState == null)
                {
                    return;
                }
            }
            while (Time.realtimeSinceStartup - start < frameBudgetSeconds);
        }

        private void EnsurePromptWorkspacePreviewBuildState()
        {
            if (_promptWorkspacePreviewCacheValid)
            {
                return;
            }

            if (_promptWorkspacePreviewBuildState != null)
            {
                return;
            }

            _promptWorkspacePreviewBuildState = PromptPersistenceService.Instance.CreatePromptWorkspaceIncrementalPreviewBuild(
                GetPromptWorkspaceRootChannel(),
                _workbenchPromptChannel);
            _promptWorkspacePreviewCachedRoot = _workbenchChannel;
            _promptWorkspacePreviewCachedChannel = _workbenchPromptChannel ?? string.Empty;
            _promptWorkspacePreviewCachedData = _promptWorkspacePreviewBuildState?.Preview ?? new PromptWorkspaceStructuredPreview();
            _promptWorkspacePreviewCachedSignature = _promptWorkspacePreviewCachedData?.Signature ?? string.Empty;
        }

        private void SyncPromptWorkspacePreviewCacheFromBuildState()
        {
            if (_promptWorkspacePreviewBuildState == null)
            {
                return;
            }

            PromptWorkspaceStructuredPreview preview = _promptWorkspacePreviewBuildState.Preview ?? new PromptWorkspaceStructuredPreview();
            _promptWorkspacePreviewCachedRoot = _workbenchChannel;
            _promptWorkspacePreviewCachedChannel = _workbenchPromptChannel ?? string.Empty;
            _promptWorkspacePreviewCachedData = preview;
            _promptWorkspacePreviewCachedSignature = preview.Signature ?? string.Empty;
            if (preview.Stage == PromptWorkspacePreviewBuildStage.Completed ||
                preview.Stage == PromptWorkspacePreviewBuildStage.Failed)
            {
                _promptWorkspacePreviewCacheValid = true;
                _promptWorkspacePreviewBuildState = null;
            }
        }

        private void InvalidatePromptWorkspacePreviewCache()
        {
            _promptWorkspacePreviewCacheValid = false;
            _promptWorkspacePreviewCachedChannel = string.Empty;
            _promptWorkspacePreviewCachedSignature = string.Empty;
            _promptWorkspacePreviewCachedData = null;
            _promptWorkspacePreviewBuildState = null;
        }

        private void InvalidatePromptWorkspaceNodeUiCaches()
        {
            _promptWorkspaceNodeListCacheChannel = string.Empty;
            _promptWorkspaceNodeListCache.Clear();
            _promptWorkspaceNodeLayoutCacheChannel = string.Empty;
            _promptWorkspaceNodeLayoutCache.Clear();
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

            string current = GetPromptWorkspaceCurrentEditorText();
            if (ContainsVariableToken(current, normalized))
            {
                Messages.Message("RimChat_RimTalkVariableAlreadyInTemplate".Translate(), MessageTypeDefOf.NeutralEvent, false);
                return true;
            }

            string wrapped = "{{ " + normalized + " }}";
            string updated = string.IsNullOrWhiteSpace(current)
                ? wrapped
                : current.TrimEnd() + "\n" + wrapped;
            SetPromptWorkspaceCurrentEditorText(updated);
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

            if (_promptWorkspaceEditNodeMode)
            {
                return PromptUnifiedNodeSchemaCatalog.TryGet(_promptWorkspaceSelectedNodeId, out PromptUnifiedNodeSchemaItem _);
            }

            return PromptSectionSchemaCatalog.TryGetSection(_promptWorkspaceSelectedSectionId, out PromptSectionSchemaItem _);
        }
    }
}
