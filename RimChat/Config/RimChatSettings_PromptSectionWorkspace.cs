using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Persistence;
using RimChat.Prompting;
using RimChat.UI;
using RimChat.UI.UGui;
using RimChat.UI.UGui.Bridge;
using RimChat.UI.UGui.Panels;
using RimWorld;
using UnityEngine;
using Verse;

// ReSharper disable InconsistentlySynchronizedField

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
        private bool _promptWorkspacePreviewFrozen;
        private DateTime _promptWorkspaceLastEditUtc = DateTime.MinValue;
        private TemplateVariableValidationResult _promptWorkspaceValidationResult = new TemplateVariableValidationResult();
        private readonly PromptWorkspacePerformanceState _promptWorkspacePerformance = new PromptWorkspacePerformanceState();
        private const double PromptWorkspaceValidationDebounceSeconds = 0.30d;
        private const int PromptWorkspacePreviewStartDelayFrames = 1;
        private const float PromptWorkspacePreviewFrameBudgetSeconds = 0.004f;
        private const string PromptWorkspaceEditorControlName = "RimChat_PromptWorkspaceSectionEditor";
        private PromptWorkbenchChipEditor _promptWorkspaceChipEditor;
        private PromptWorkspaceStructuredPreviewRenderer _promptWorkspacePreviewRenderer;
        private bool _promptWorkspaceChipEditorDisabledForSession;
        private bool _promptWorkspaceForceImguiFallback;
        private bool _promptWorkspaceFallbackWarningSent;

        // UGUI panel data caches (avoid per-frame DTO reconstruction)
        private string _promptWorkspaceHeaderDataSignature = string.Empty;
        private WorkspaceHeaderPanel.HeaderData _promptWorkspaceHeaderDataCache;
        private string _promptWorkspacePresetPanelDataSignature = string.Empty;
        private WorkspacePresetPanel.PresetPanelData _promptWorkspacePresetPanelDataCache;
        private string _promptWorkspaceSidePanelDataSignature = string.Empty;
        private WorkspaceSidePanel.SidePanelData _promptWorkspaceSidePanelDataCache;
        private string _promptWorkspaceEditorPanelDataSignature = string.Empty;
        private WorkspaceEditorPanel.EditorData _promptWorkspaceEditorPanelDataCache;

        // RenderTexture caches for offscreen rendering (reduces DrawCalls from N to 1 per panel)
        private CachedRenderTexture _sidePanelContentRtCache;

        // UGUI rendering infrastructure for structured preview panel
        private UGuiCanvasHost _uguiPreviewHost;
        private PromptPreviewPanel _uguiPreviewPanel;
        private ImguiToUguiInputBridge _uguiInputBridge;
        private bool _uguiPreviewInitialized;
        private int _uguiLastRtWidth;
        private int _uguiLastRtHeight;

        // UGUI rendering infrastructure for workspace header panel
        private UGuiCanvasHost _uguiHeaderHost;
        private WorkspaceHeaderPanel _uguiHeaderPanel;
        private ImguiToUguiInputBridge _uguiHeaderInputBridge;
        private bool _uguiHeaderInitialized;

        // UGUI rendering infrastructure for workspace preset panel
        private UGuiCanvasHost _uguiPresetHost;
        private WorkspacePresetPanel _uguiPresetPanel;
        private ImguiToUguiInputBridge _uguiPresetInputBridge;
        private bool _uguiPresetInitialized;

        // UGUI rendering infrastructure for workspace side panel tabs
        private UGuiCanvasHost _uguiSidePanelHost;
        private WorkspaceSidePanel _uguiSidePanel;
        private ImguiToUguiInputBridge _uguiSidePanelInputBridge;
        private bool _uguiSidePanelInitialized;

        // UGUI rendering infrastructure for workspace editor chrome
        private UGuiCanvasHost _uguiEditorHost;
        private WorkspaceEditorPanel _uguiEditorPanel;
        private ImguiToUguiInputBridge _uguiEditorInputBridge;
        private bool _uguiEditorInitialized;
        private string _promptWorkspaceDraggingNodeId = string.Empty;
        private string _promptWorkspaceDropTargetNodeId = string.Empty;
        private string _promptWorkspaceNodeListCacheChannel = string.Empty;
        private List<PromptUnifiedNodeSchemaItem> _promptWorkspaceNodeListCache = new List<PromptUnifiedNodeSchemaItem>();
        private string _promptWorkspaceNodeLayoutCacheChannel = string.Empty;
        private List<PromptUnifiedNodeLayoutConfig> _promptWorkspaceNodeLayoutCache = new List<PromptUnifiedNodeLayoutConfig>();
        private string _promptWorkspaceSectionLayoutCacheChannel = string.Empty;
        private List<PromptSectionLayoutConfig> _promptWorkspaceSectionLayoutCache = new List<PromptSectionLayoutConfig>();
        private Action _promptWorkspaceDeferredNavigationAction;

        // Dirty-flag infrastructure for cache-invalidation signaling across partial classes.
        // Used by InvalidatePromptWorkspaceNodeUiCaches, InvalidatePromptWorkspacePreviewCache, etc.
        // NOT used for frame-throttle (IMGUI requires rendering every frame to avoid flicker).
        private int _workspaceDirtyFlags;
        private const int WorkspaceDirtyPresetPanel = 1 << 0;
        private const int WorkspaceDirtyModuleList   = 1 << 1;
        private const int WorkspaceDirtySidePanel    = 1 << 2;
        private const int WorkspaceDirtyHeader       = 1 << 3;
        private const int WorkspaceDirtyAll = WorkspaceDirtyPresetPanel | WorkspaceDirtyModuleList | WorkspaceDirtySidePanel | WorkspaceDirtyHeader;

        private void MarkWorkspaceDirty(int flags)
        {
            _workspaceDirtyFlags |= flags;
            if (flags == 0)
            {
                return;
            }

            _promptWorkspacePerformance.LayoutVersion++;
            InvalidatePromptWorkspacePanelDataCaches();
        }
        private bool IsWorkspaceDirty(int flag)      { return (_workspaceDirtyFlags & flag) != 0; }
        private void ClearWorkspaceDirty(int flags)  { _workspaceDirtyFlags &= ~flags; }
        private void MarkWorkspaceAllDirty()
        {
            _workspaceDirtyFlags = WorkspaceDirtyAll;
            _promptWorkspacePerformance.LayoutVersion++;
            InvalidatePromptWorkspacePanelDataCaches();
        }

        // Cached module list to avoid per-frame rebuilds (A+B+C optimization)
        private string _promptWorkspaceModuleCacheChannel = string.Empty;
        private List<PromptWorkbenchModuleItem> _promptWorkspaceModuleCache = new List<PromptWorkbenchModuleItem>();

        // Static color constants to avoid per-frame allocations (D optimization)
        private static readonly Color WorkspaceBackground = new Color(0.08f, 0.09f, 0.11f);

        // Frame skipping for preview build to reduce CPU usage (A optimization)
        private int _promptWorkspacePreviewFrameCounter;
        private const int PromptWorkspacePreviewFrameSkip = 2; // Build every 3rd frame (20fps at 60fps game)
        private static readonly Color WorkspaceHeaderBg = new Color(0.07f, 0.08f, 0.10f);
        private static readonly Color WorkspaceAccentGold = new Color(0.95f, 0.74f, 0.26f);
        private static readonly Color WorkspaceAccentLightGold = new Color(1f, 0.88f, 0.55f);
        private static readonly Color WorkspaceAccentBrightGold = new Color(0.95f, 0.88f, 0.55f);
        private static readonly Color ModuleListBg = new Color(0.03f, 0.03f, 0.04f);
        private static readonly Color EditorPanelBg = new Color(0.06f, 0.07f, 0.09f);
        private static readonly Color RowHoverBg = new Color(0.18f, 0.18f, 0.20f);
        private static readonly Color RowSelectedBg = new Color(0.24f, 0.35f, 0.55f);
        private static readonly Color ModeSelectedActiveBg = new Color(0.24f, 0.35f, 0.55f);
        private static readonly Color ModeSelectedInactiveBg = new Color(0.16f, 0.16f, 0.16f);
        private static readonly Color ModeNormalActiveBg = new Color(0.13f, 0.15f, 0.18f);
        private static readonly Color ModeNormalInactiveBg = new Color(0.10f, 0.10f, 0.10f);
        private static readonly Color InactiveText = new Color(0.60f, 0.60f, 0.60f);
        private static readonly Color MetadataTagText = new Color(0.70f, 0.70f, 0.70f);
        private static readonly Color DropdownBg = new Color(0.25f, 0.18f, 0.08f);
        private static readonly Color ButtonSelectedBg = new Color(0.45f, 0.33f, 0.15f);
        private static readonly Color ButtonNormalBg = new Color(0.19f, 0.15f, 0.10f);
        private static readonly Color PresetPanelBg = new Color(0.09f, 0.10f, 0.12f);
        private static readonly Color VariablePanelBg = new Color(0.12f, 0.14f, 0.18f);
        private static readonly Color NodeInfoText = new Color(0.70f, 0.80f, 0.95f);
        private static readonly Color DimmedText = new Color(0.75f, 0.80f, 0.85f);

        private void DrawPromptSectionWorkspace(Rect root)
        {
            EvaluatePromptWorkspaceUguiCapability();
            EnsurePresetStoreReady();
            EnsurePromptWorkspaceSelection();
            TryRunDeferredPromptWorkspaceNavigation();
            TryAutoSavePromptWorkspaceBuffer();

            // Freeze full preview while editor has focus to reduce per-frame rebuild cost
            bool editorHasFocus = GUI.GetNameOfFocusedControl() == PromptWorkspaceEditorControlName;
            if (editorHasFocus && !_promptWorkspacePreviewFrozen)
            {
                _promptWorkspacePreviewFrozen = true;
            }
            else if (!editorHasFocus && _promptWorkspacePreviewFrozen)
            {
                _promptWorkspacePreviewFrozen = false;
                // Mark cache stale so next Tick rebuilds, but don't discard existing partial data.
                // This avoids a full rebuild blast on every focus loss — the incremental builder
                // will reuse any completed blocks and only re-render changed steps.
                _promptWorkspacePreviewCacheValid = false;
                // Invalidate RT caches since preview data will change
                _sidePanelContentRtCache?.MarkDirty();
                _promptWorkspacePreviewRenderer?.MarkDirty();
            }

            if (!_promptWorkspacePreviewFrozen || _workbenchSidePanelTab == PromptWorkbenchInfoPanel.Preview)
            {
                bool canRunPreviewBuild = TryRunDeferredPreviewBuild();
                // Frame skipping: only check build every N frames to reduce CPU usage (A optimization)
                _promptWorkspacePreviewFrameCounter++;
                if (_promptWorkspacePreviewFrameCounter >= PromptWorkspacePreviewFrameSkip)
                {
                    _promptWorkspacePreviewFrameCounter = 0;
                }

                // Skip Tick when cache is valid and no build is in progress, or when frame skipping
                if (canRunPreviewBuild &&
                    (_promptWorkspacePreviewFrameCounter == 0 || _promptWorkspacePreviewBuildState != null) &&
                    (!_promptWorkspacePreviewCacheValid || _promptWorkspacePreviewBuildState != null))
                {
                    TickPromptWorkspacePreviewBuild(PromptWorkspacePreviewFrameBudgetSeconds);
                }
            }

            // IMGUI requires all panels to render every frame — skipping Draw calls causes flicker.
            // Dirty flags are kept for cache-invalidation signaling only, not for frame-throttle.
            Widgets.DrawBoxSolid(root, WorkspaceBackground);
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
            // UGUI rendering path for header
            if (CanUsePromptWorkspaceUguiHeader())
            {
                DrawPromptWorkspaceHeaderUgui(rect);
                return;
            }

            // IMGUI fallback path
            Widgets.DrawBoxSolid(rect, WorkspaceHeaderBg);
            Rect inner = rect.ContractedBy(8f);

            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = WorkspaceAccentGold;
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
            Widgets.DrawBoxSolid(rect, selected ? ButtonSelectedBg : ButtonNormalBg);
            Widgets.DrawBox(rect, 1);
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = selected ? WorkspaceAccentLightGold : Color.white;
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
            Widgets.DrawBoxSolid(rect, DropdownBg);
            Widgets.DrawBox(rect, 1);
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = WorkspaceAccentLightGold;
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
            // UGUI rendering path for preset panel
            if (CanUsePromptWorkspaceUguiPresetPanel())
            {
                DrawPromptWorkspacePresetPanelUgui(rect);
                return;
            }

            // IMGUI fallback path
            Widgets.DrawBoxSolid(rect, PresetPanelBg);
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
                "RimChat_PromptWorkspaceModuleHeader".Translate());
            y += 24f;
            float moduleListHeight = Mathf.Max(72f, inner.yMax - y - 6f);

            // Module list has interactive elements (checkbox, click) — keep direct IMGUI rendering.
            // RenderTexture caching only applies to read-only panels.
            DrawPromptWorkspaceModuleList(new Rect(inner.x, y, inner.width, moduleListHeight));
        }

        private void DrawPromptWorkspaceEditorPanel(Rect rect)
        {
            // UGUI rendering path for editor chrome (metadata, toolbar, validation)
            if (CanUsePromptWorkspaceUguiEditorPanel())
            {
                DrawPromptWorkspaceEditorPanelUgui(rect);
                return;
            }

            // IMGUI fallback path
            Widgets.DrawBoxSolid(rect, EditorPanelBg);
            Rect inner = rect.ContractedBy(8f);
            float y = inner.y;
            const float validationHeight = 24f;

            DrawWorkbenchPresetNameRow(inner, ref y);
            HandlePromptWorkspaceKeyboardShortcuts();
            Rect toolbarRect = new Rect(inner.x, y, inner.width, 26f);
            y += 32f;

            // RimTalk-style metadata row: module label + enabled checkbox + slot selector
            DrawPromptWorkspaceModuleMetadataRow(new Rect(inner.x, y, inner.width, 24f));
            y += 28f;

            // Second metadata row for nodes: slot selector
            if (_promptWorkspaceEditNodeMode)
            {
                DrawPromptWorkspaceNodeSlotRow(new Rect(inner.x, y, inner.width, 24f));
                y += 28f;
            }

            float editorHeight = Mathf.Max(24f, inner.yMax - y - validationHeight - 4f);
            Rect editorRect = new Rect(inner.x, y, inner.width, editorHeight);
            string sourceText = GetPromptWorkspaceCurrentEditorText();

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
        }

        private void DrawPromptWorkspaceModuleMetadataRow(Rect rect)
        {
            string kindTag = _promptWorkspaceEditNodeMode
                ? "RimChat_PromptWorkspaceKind_Node".Translate().ToString()
                : "RimChat_PromptWorkspaceKind_Section".Translate().ToString();
            string label = _promptWorkspaceEditNodeMode
                ? PromptUnifiedNodeSchemaCatalog.GetDisplayLabel(_promptWorkspaceSelectedNodeId)
                : (PromptSectionSchemaCatalog.TryGetSection(_promptWorkspaceSelectedSectionId, out PromptSectionSchemaItem section)
                    ? section.GetDisplayLabel()
                    : _promptWorkspaceSelectedSectionId);

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GUI.color = MetadataTagText;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x, rect.y, 60f, rect.height), $"[{kindTag}]");
            GUI.color = WorkspaceAccentBrightGold;
            bool oldWrap = Text.WordWrap;
            Text.WordWrap = false;
            Widgets.Label(new Rect(rect.x + 62f, rect.y, rect.width - 200f, rect.height),
                label.Truncate(rect.width - 200f));
            Text.WordWrap = oldWrap;
            GUI.color = oldColor;
            Text.Anchor = oldAnchor;

            // Enabled checkbox (RimTalk-style)
            List<PromptSectionLayoutConfig> sectionLayouts = GetPromptWorkspaceSectionLayouts();
            List<PromptUnifiedNodeLayoutConfig> nodeLayouts = GetPromptWorkspaceNodeLayouts();
            bool isEnabled;
            if (_promptWorkspaceEditNodeMode)
            {
                PromptUnifiedNodeLayoutConfig layout = nodeLayouts.FirstOrDefault(item =>
                    string.Equals(item.NodeId, _promptWorkspaceSelectedNodeId, StringComparison.OrdinalIgnoreCase));
                isEnabled = layout?.Enabled ?? true;
            }
            else
            {
                PromptSectionLayoutConfig layout = sectionLayouts.FirstOrDefault(item =>
                    string.Equals(item.SectionId, _promptWorkspaceSelectedSectionId, StringComparison.OrdinalIgnoreCase));
                isEnabled = layout?.Enabled ?? true;
            }

            float enabledWidth = Mathf.Clamp(rect.width * 0.28f, 100f, 180f);
            Rect enabledRect = new Rect(rect.xMax - enabledWidth, rect.y, enabledWidth, rect.height);
            bool toggled = isEnabled;
            Widgets.CheckboxLabeled(enabledRect, "RimChat_RimTalkCompatEnable".Translate(), ref toggled);
            if (toggled != isEnabled)
            {
                if (EnsurePromptWorkspaceEditablePresetForMutation("workspace.module_toggle"))
                {
                    if (_promptWorkspaceEditNodeMode)
                    {
                        PromptUnifiedNodeLayoutConfig layout = nodeLayouts.FirstOrDefault(item =>
                            string.Equals(item.NodeId, _promptWorkspaceSelectedNodeId, StringComparison.OrdinalIgnoreCase));
                        if (layout != null)
                        {
                            layout.Enabled = toggled;
                            SavePromptWorkspaceNodeLayouts(nodeLayouts);
                        }
                    }
                    else
                    {
                        PromptSectionLayoutConfig layout = sectionLayouts.FirstOrDefault(item =>
                            string.Equals(item.SectionId, _promptWorkspaceSelectedSectionId, StringComparison.OrdinalIgnoreCase));
                        if (layout != null)
                        {
                            layout.Enabled = toggled;
                            SavePromptWorkspaceSectionLayouts(sectionLayouts);
                        }
                    }
                }
            }
        }

        private void DrawPromptWorkspaceNodeSlotRow(Rect rect)
        {
            List<PromptUnifiedNodeLayoutConfig> nodeLayouts = GetPromptWorkspaceNodeLayouts();
            PromptUnifiedNodeLayoutConfig layout = nodeLayouts.FirstOrDefault(item =>
                string.Equals(item.NodeId, _promptWorkspaceSelectedNodeId, StringComparison.OrdinalIgnoreCase));
            if (layout == null)
            {
                return;
            }

            PromptUnifiedNodeSlot currentSlot = layout.GetSlot();
            string slotLabel = GetPromptNodeSlotLabel(currentSlot);

            float slotButtonWidth = Mathf.Clamp(rect.width * 0.45f, 140f, 240f);
            Rect slotRect = new Rect(rect.x, rect.y, slotButtonWidth, rect.height);
            if (Widgets.ButtonText(slotRect, "RimChat_PromptNodeSlot".Translate() + ": " + slotLabel))
            {
                ShowPromptNodeSlotMenu(nodeLayouts, layout);
            }

            // Order display
            int order = layout.Order;
            float orderLabelWidth = Mathf.Clamp(rect.width * 0.30f, 100f, 160f);
            Rect orderRect = new Rect(slotRect.xMax + 8f, rect.y, orderLabelWidth, rect.height);
            Widgets.Label(orderRect, "RimChat_PromptNodeOrder".Translate() + ": " + order);
        }

        private void DrawPromptWorkspaceCurrentModuleLabel(Rect rect)
        {
            string label = _promptWorkspaceEditNodeMode
                ? PromptUnifiedNodeSchemaCatalog.GetDisplayLabel(_promptWorkspaceSelectedNodeId)
                : (PromptSectionSchemaCatalog.TryGetSection(_promptWorkspaceSelectedSectionId, out PromptSectionSchemaItem section)
                    ? section.GetDisplayLabel()
                    : _promptWorkspaceSelectedSectionId);

            string kindTag = _promptWorkspaceEditNodeMode
                ? "RimChat_PromptWorkspaceKind_Node".Translate().ToString()
                : "RimChat_PromptWorkspaceKind_Section".Translate().ToString();

            Color oldColor = GUI.color;
            GUI.color = MetadataTagText;
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x, rect.y, 60f, rect.height), $"[{kindTag}]");
            GUI.color = WorkspaceAccentBrightGold;
            Widgets.Label(new Rect(rect.x + 62f, rect.y, rect.width - 62f, rect.height), label);
            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
        }

        private void DrawPromptWorkspaceValidationStatus(Rect rect, string templateText)
        {
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

        private void ForcePromptWorkspaceValidationNow()
        {
            string editorText = _promptWorkspaceEditorBuffer ?? string.Empty;
            TemplateVariableValidationContext validationContext = BuildPromptWorkspaceValidationContext();
            UpdatePromptWorkspaceValidationState(editorText, validationContext, force: true);
        }

        private void UpdatePromptWorkspaceValidationState(
            string templateText,
            TemplateVariableValidationContext validationContext,
            bool force = false)
        {
            string contextSignature = validationContext?.Signature ?? "runtime.default";
            long textVersion = _promptWorkspacePerformance.EditorTextVersion;
            bool contextChanged = !string.Equals(
                contextSignature,
                _promptWorkspacePerformance.LastValidatedContextSignature,
                StringComparison.Ordinal);
            bool textChanged = _promptWorkspacePerformance.LastValidatedTextVersion != textVersion;
            bool hasPendingRequest = _promptWorkspacePerformance.ValidationPending;

            if (!force && !contextChanged && !textChanged && !hasPendingRequest)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            bool needsDebounce = (textChanged || hasPendingRequest) && !contextChanged && !force;
            if (needsDebounce && now < _promptWorkspacePerformance.ValidationEarliestRunUtc)
            {
                return;
            }

            _promptWorkspaceValidationResult = string.IsNullOrWhiteSpace(templateText)
                ? new TemplateVariableValidationResult()
                : PromptPersistenceService.Instance.ValidateTemplateVariables(templateText, validationContext);
            _promptWorkspacePerformance.LastValidatedContextSignature = contextSignature;
            _promptWorkspacePerformance.LastValidatedTextVersion = textVersion;
            _promptWorkspacePerformance.ValidationPending = false;
            _promptWorkspacePerformance.ValidationResultVersion++;
            _promptWorkspaceEditorPanelDataSignature = string.Empty;
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
            Color selectedColor = active ? ModeSelectedActiveBg : ModeSelectedInactiveBg;
            Color normalColor = active ? ModeNormalActiveBg : ModeNormalInactiveBg;
            Widgets.DrawBoxSolid(rect, selected ? selectedColor : normalColor);
            Widgets.DrawBox(rect, 1);
            TextAnchor old = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Color oldColor = GUI.color;
            GUI.color = active ? Color.white : InactiveText;
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
                    MarkWorkspaceDirty(WorkspaceDirtyModuleList | WorkspaceDirtySidePanel);
                    // Note: no need to InvalidatePromptWorkspacePreviewCache here.
                    // Switching edit mode only changes which module is shown in the
                    // editor; the full preview (all sections + nodes) is unchanged.
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
            Widgets.DrawBoxSolid(rect, VariablePanelBg);
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
                        MarkWorkspaceDirty(WorkspaceDirtyModuleList | WorkspaceDirtySidePanel);
                        // Note: no need to InvalidatePromptWorkspacePreviewCache here.
                        // Selecting a different node only changes which module is shown
                        // in the editor; the full preview (all sections + nodes) is unchanged.
                    });
                }))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private string DrawPromptWorkspaceEditor(Rect rect, string text)
        {
            Widgets.DrawBoxSolid(rect, ModuleListBg);
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
            // UGUI rendering path for side panel tab bar
            if (CanUsePromptWorkspaceUguiSidePanel())
            {
                DrawPromptWorkspaceSidePanelUgui(rect);
                return;
            }

            // IMGUI fallback path
            Widgets.DrawBoxSolid(rect, PresetPanelBg);
            Rect inner = rect.ContractedBy(8f);
            float buttonWidth = (inner.width - 12f) / 3f;
            Rect previewRect = new Rect(inner.x, inner.y, buttonWidth, 24f);
            Rect fullPreviewRect = new Rect(previewRect.xMax + 6f, inner.y, buttonWidth, 24f);
            Rect varsRect = new Rect(fullPreviewRect.xMax + 6f, inner.y, buttonWidth, 24f);

            DrawWorkbenchSideButton(previewRect, PromptWorkbenchInfoPanel.Preview, "RimChat_PreviewTitleShort");
            DrawWorkbenchSideButton(fullPreviewRect, PromptWorkbenchInfoPanel.FullPreview, "RimChat_PromptWorkbench_FullPreviewTab");
            DrawWorkbenchSideButton(varsRect, PromptWorkbenchInfoPanel.Variables, "RimChat_PromptWorkbench_VariablesTab");

            Rect contentRect = new Rect(inner.x, previewRect.yMax + 6f, inner.width, inner.height - 30f);

            // Variables tab has interactive elements — use direct IMGUI rendering.
            // Preview and FullPreview tabs are read-only — use RenderTexture cache (1 DrawCall).
            if (_workbenchSidePanelTab == PromptWorkbenchInfoPanel.Variables)
            {
                DrawPromptWorkspaceVariables(contentRect);
                return;
            }

            _sidePanelContentRtCache ??= new CachedRenderTexture();
            string sidePanelSig = BuildSidePanelRenderSignature();
            _sidePanelContentRtCache.MarkDirtyIfChanged(sidePanelSig);

            _sidePanelContentRtCache.DrawWithCacheTracking(contentRect, localRect =>
            {
                switch (_workbenchSidePanelTab)
                {
                    case PromptWorkbenchInfoPanel.FullPreview:
                        DrawPromptWorkspaceFullPreview(localRect);
                        break;
                    default:
                        DrawPromptWorkspacePreview(localRect);
                        break;
                }
            });
        }

        private string BuildSidePanelRenderSignature()
        {
            PromptWorkspaceStructuredPreview preview = GetPromptWorkspaceStructuredPreview();
            string previewSig = preview?.Signature ?? "null";
            string editMode = _promptWorkspaceEditNodeMode ? "node" : "section";
            string selectedSection = _promptWorkspaceSelectedSectionId ?? string.Empty;
            string selectedNode = _promptWorkspaceSelectedNodeId ?? string.Empty;
            string tab = _workbenchSidePanelTab.ToString();
            long editorVersion = _promptWorkspacePerformance.EditorTextVersion;
            return $"{tab}|{previewSig}|{editMode}|{selectedSection}|{selectedNode}|v{editorVersion}|{_promptWorkspacePreviewScroll.y:F0}";
        }

        private void DrawPromptWorkspaceFullPreview(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, ModuleListBg);
            Rect inner = rect.ContractedBy(6f);
            PromptWorkspaceStructuredPreview preview = GetPromptWorkspaceStructuredPreview();
            DrawPromptWorkspaceStructuredPreview(inner, preview);
        }

        private void DrawPromptWorkspacePreview(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, ModuleListBg);
            Rect inner = rect.ContractedBy(6f);

            // Single-node preview: show only the selected module's content
            if (_promptWorkspaceEditNodeMode && !string.IsNullOrWhiteSpace(_promptWorkspaceSelectedNodeId))
            {
                DrawPromptWorkspaceSingleNodePreview(inner);
            }
            else if (!_promptWorkspaceEditNodeMode && !string.IsNullOrWhiteSpace(_promptWorkspaceSelectedSectionId))
            {
                DrawPromptWorkspaceSingleSectionPreview(inner);
            }
            else
            {
                PromptWorkspaceStructuredPreview preview = GetPromptWorkspaceStructuredPreview();
                DrawPromptWorkspaceStructuredPreview(inner, preview);
            }
        }

        private void DrawPromptWorkspaceSingleNodePreview(Rect rect)
        {
            string label = PromptUnifiedNodeSchemaCatalog.GetDisplayLabel(_promptWorkspaceSelectedNodeId);
            string text = ResolvePromptWorkspaceNodePreviewContent(_promptWorkspaceSelectedNodeId);

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;

            // Header
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = NodeInfoText;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 20f),
                "RimChat_PromptWorkspaceKind_Node".Translate() + ": " + label);
            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;

            // Content
            Rect contentRect = new Rect(rect.x, rect.y + 24f, rect.width, Mathf.Max(24f, rect.height - 24f));
            DrawPromptWorkspacePreviewContentScroll(contentRect, text);
        }

        private void DrawPromptWorkspaceSingleSectionPreview(Rect rect)
        {
            string label = PromptSectionSchemaCatalog.TryGetSection(_promptWorkspaceSelectedSectionId, out PromptSectionSchemaItem section)
                ? section.GetDisplayLabel()
                : _promptWorkspaceSelectedSectionId;
            string text = ResolvePromptWorkspaceSectionPreviewContent(_promptWorkspaceSelectedSectionId);

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;

            // Header
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = WorkspaceAccentBrightGold;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 20f),
                "RimChat_PromptWorkspaceKind_Section".Translate() + ": " + label);
            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;

            // Content
            Rect contentRect = new Rect(rect.x, rect.y + 24f, rect.width, Mathf.Max(24f, rect.height - 24f));
            DrawPromptWorkspacePreviewContentScroll(contentRect, text);
        }

        /// <summary>
        /// Extract rendered node content from the already-built StructuredPreview blocks.
        /// Falls back to raw template text only if the preview has no matching block yet.
        /// </summary>
        private string ResolvePromptWorkspaceNodePreviewContent(string nodeId)
        {
            PromptWorkspaceStructuredPreview preview = GetPromptWorkspaceStructuredPreview();
            if (preview?.Blocks != null)
            {
                string normalizedId = PromptUnifiedNodeSchemaCatalog.NormalizeId(nodeId);
                foreach (PromptWorkspacePreviewBlock block in preview.Blocks)
                {
                    if (block?.Kind != PromptWorkspacePreviewBlockKind.Node)
                    {
                        continue;
                    }

                    if (string.Equals(
                        PromptUnifiedNodeSchemaCatalog.NormalizeId(block.NodeId),
                        normalizedId,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return block.Content ?? string.Empty;
                    }
                }
            }

            // Fallback: preview not yet built for this node — return raw template
            return GetPromptWorkspaceNodeText(_workbenchPromptChannel, nodeId);
        }

        /// <summary>
        /// Extract rendered section content from the already-built StructuredPreview subsections.
        /// Falls back to raw template text only if the preview has no matching subsection yet.
        /// </summary>
        private string ResolvePromptWorkspaceSectionPreviewContent(string sectionId)
        {
            PromptWorkspaceStructuredPreview preview = GetPromptWorkspaceStructuredPreview();
            if (preview?.Blocks != null)
            {
                string normalizedId = PromptSectionSchemaCatalog.NormalizeSectionId(sectionId);
                foreach (PromptWorkspacePreviewBlock block in preview.Blocks)
                {
                    // Only SectionAggregate blocks carry Subsections with section-level content
                    if (block?.Kind != PromptWorkspacePreviewBlockKind.SectionAggregate)
                    {
                        continue;
                    }

                    if (block.Subsections == null)
                    {
                        continue;
                    }

                    foreach (PromptWorkspacePreviewSubsection subsection in block.Subsections)
                    {
                        if (subsection == null)
                        {
                            continue;
                        }

                        if (string.Equals(
                            PromptSectionSchemaCatalog.NormalizeSectionId(subsection.SectionId),
                            normalizedId,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            return subsection.Content ?? string.Empty;
                        }
                    }
                }
            }

            // Fallback: preview not yet built for this section — return raw template
            return GetPromptWorkspaceSectionText(_workbenchPromptChannel, sectionId);
        }

        private GUIStyle _previewContentScrollStyle;
        private string _previewContentScrollCachedText = string.Empty;
        private float _previewContentScrollCachedWidth = -1f;
        private float _previewContentScrollCachedHeight;

        private void DrawPromptWorkspacePreviewContentScroll(Rect rect, string text)
        {
            string source = text ?? string.Empty;
            if (_previewContentScrollStyle == null)
            {
                _previewContentScrollStyle = new GUIStyle(GUI.skin.label)
                {
                    wordWrap = true,
                    richText = false
                };
            }

            float contentWidth = Mathf.Max(1f, rect.width - 16f);
            float contentHeight;
            if (string.Equals(source, _previewContentScrollCachedText, StringComparison.Ordinal) &&
                Mathf.Abs(contentWidth - _previewContentScrollCachedWidth) < 0.5f)
            {
                contentHeight = _previewContentScrollCachedHeight;
            }
            else
            {
                contentHeight = Mathf.Max(rect.height, _previewContentScrollStyle.CalcHeight(new GUIContent(source), contentWidth) + 4f);
                _previewContentScrollCachedText = source;
                _previewContentScrollCachedWidth = contentWidth;
                _previewContentScrollCachedHeight = contentHeight;
            }

            Rect viewRect = new Rect(0f, 0f, contentWidth, contentHeight);
            _promptWorkspacePreviewScroll = new Vector2(
                0f,
                Mathf.Clamp(_promptWorkspacePreviewScroll.y, 0f, Mathf.Max(0f, viewRect.height - rect.height)));
            _promptWorkspacePreviewScroll = GUI.BeginScrollView(rect, _promptWorkspacePreviewScroll, viewRect, false, true);

            Color oldColor = GUI.color;
            GUI.color = DimmedText;
            Widgets.Label(new Rect(0f, 0f, contentWidth, contentHeight), source);
            GUI.color = oldColor;

            GUI.EndScrollView();
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

            _promptWorkspaceEditNodeMode = false;
            _promptWorkspaceSelectedSectionId = PromptSectionSchemaCatalog.NormalizeSectionId(sectionId);
            EnsurePromptWorkspaceBuffer();
            MarkWorkspaceDirty(WorkspaceDirtyModuleList);
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
            string nextBuffer = _promptWorkspaceEditNodeMode
                ? GetPromptWorkspaceNodeText(_promptWorkspaceBufferedChannel, _promptWorkspaceBufferedNodeId)
                : GetPromptWorkspaceSectionText(_promptWorkspaceBufferedChannel, _promptWorkspaceBufferedSectionId);
            if (!string.Equals(nextBuffer ?? string.Empty, _promptWorkspaceEditorBuffer ?? string.Empty, StringComparison.Ordinal))
            {
                _promptWorkspaceEditorBuffer = nextBuffer ?? string.Empty;
                NotifyPromptWorkspaceEditorBufferRebound();
            }
            else
            {
                _promptWorkspaceEditorBuffer = nextBuffer ?? string.Empty;
            }
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
            NotifyPromptWorkspaceEditorTextChanged();
        }

        private void NotifyPromptWorkspaceEditorTextChanged()
        {
            DateTime now = DateTime.UtcNow;
            _promptWorkspaceLastEditUtc = now;
            _promptWorkspacePerformance.LastEditorTextChangedUtc = now;
            _promptWorkspacePerformance.EditorTextVersion++;
            _promptWorkspaceEditorPanelDataSignature = string.Empty;
            TryScheduleValidation(immediate: false);
        }

        private void NotifyPromptWorkspaceEditorBufferRebound()
        {
            DateTime now = DateTime.UtcNow;
            _promptWorkspacePerformance.LastEditorTextChangedUtc = now;
            _promptWorkspacePerformance.EditorTextVersion++;
            _promptWorkspaceEditorPanelDataSignature = string.Empty;
            TryScheduleValidation(immediate: true);
        }

        private void TryScheduleValidation(bool immediate)
        {
            _promptWorkspacePerformance.ValidationPending = true;
            _promptWorkspacePerformance.ValidationEarliestRunUtc = immediate
                ? DateTime.UtcNow
                : DateTime.UtcNow.AddSeconds(PromptWorkspaceValidationDebounceSeconds);
        }

        private bool TryRunDeferredPreviewBuild()
        {
            if (_promptWorkspacePreviewBuildState != null)
            {
                return true;
            }

            if (_promptWorkspacePreviewCacheValid)
            {
                _promptWorkspacePerformance.PreviewStartPending = false;
                _promptWorkspacePerformance.PreviewStartDelayFrames = 0;
                return false;
            }

            if (!_promptWorkspacePerformance.PreviewStartPending)
            {
                return true;
            }

            if (_promptWorkspacePerformance.PreviewStartDelayFrames > 0)
            {
                _promptWorkspacePerformance.PreviewStartDelayFrames--;
                return false;
            }

            _promptWorkspacePerformance.PreviewStartPending = false;
            return true;
        }

        private void InvalidatePromptWorkspacePanelDataCaches()
        {
            _promptWorkspaceHeaderDataSignature = string.Empty;
            _promptWorkspacePresetPanelDataSignature = string.Empty;
            _promptWorkspaceSidePanelDataSignature = string.Empty;
            _promptWorkspaceEditorPanelDataSignature = string.Empty;
        }

        private bool CanUsePromptWorkspaceUguiHeader()
        {
            return !_promptWorkspaceForceImguiFallback && UGuiFeatureFlags.UseUguiHeaderPanel;
        }

        private bool CanUsePromptWorkspaceUguiPresetPanel()
        {
            return !_promptWorkspaceForceImguiFallback && UGuiFeatureFlags.UseUguiPresetPanel;
        }

        private bool CanUsePromptWorkspaceUguiSidePanel()
        {
            return !_promptWorkspaceForceImguiFallback && UGuiFeatureFlags.UseUguiSidePanel;
        }

        private bool CanUsePromptWorkspaceUguiEditorPanel()
        {
            return !_promptWorkspaceForceImguiFallback && UGuiFeatureFlags.UseUguiEditorPanel;
        }

        private bool CanUsePromptWorkspaceUguiPreviewPanel()
        {
            return !_promptWorkspaceForceImguiFallback && UGuiFeatureFlags.UseUguiPreviewPanel;
        }

        private void EvaluatePromptWorkspaceUguiCapability()
        {
            if (_promptWorkspaceForceImguiFallback || !UGuiFeatureFlags.UseUguiRendering)
            {
                return;
            }

            string folder = LanguageDatabase.activeLanguage?.folderName ?? string.Empty;
            bool cjkLanguage = folder.IndexOf("Chinese", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               folder.IndexOf("Japanese", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               folder.IndexOf("Korean", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!cjkLanguage)
            {
                return;
            }

            UGuiFontManager.EnsureInitialized();
            if (UGuiFontManager.SupportsCjk)
            {
                return;
            }

            _promptWorkspaceForceImguiFallback = true;
            DisposePromptWorkspaceRenderTextures();
            if (_promptWorkspaceFallbackWarningSent)
            {
                return;
            }

            _promptWorkspaceFallbackWarningSent = true;
            string activeLanguage = string.IsNullOrWhiteSpace(folder) ? "unknown" : folder;
            string fontName = UGuiFontManager.DefaultFont != null ? UGuiFontManager.DefaultFont.name : "null";
            Log.Warning(
                "[RimChat] Prompt workspace switched to IMGUI compatibility mode. " +
                $"language={activeLanguage}, font={fontName}, supportsCjk={UGuiFontManager.SupportsCjk}, " +
                $"uguiMaster={UGuiFeatureFlags.UseUguiRendering}, uguiHeader={UGuiFeatureFlags.UseUguiHeaderPanel}, " +
                $"uguiPreset={UGuiFeatureFlags.UseUguiPresetPanel}, uguiEditor={UGuiFeatureFlags.UseUguiEditorPanel}, " +
                $"uguiSide={UGuiFeatureFlags.UseUguiSidePanel}, uguiPreview={UGuiFeatureFlags.UseUguiPreviewPanel}.");
            Messages.Message("RimChat_PromptRenderCompatibilityMode".Translate(), MessageTypeDefOf.NeutralEvent, false);
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
                TryScheduleValidation(immediate: true);
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
            NotifyPromptWorkspaceEditorTextChanged();
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

        /// <summary>
        /// Release all RenderTexture GPU resources used by the prompt workspace panels.
        /// Called when the workbench window closes to prevent memory leaks.
        /// </summary>
        internal void DisposePromptWorkspaceRenderTextures()
        {
            _sidePanelContentRtCache?.Dispose();
            _sidePanelContentRtCache = null;
            _promptWorkspacePreviewRenderer?.Dispose();

            // Dispose UGUI rendering resources - preview panel
            _uguiInputBridge?.Dispose();
            _uguiInputBridge = null;
            _uguiPreviewPanel?.Dispose();
            _uguiPreviewPanel = null;
            _uguiPreviewHost?.Dispose();
            _uguiPreviewHost = null;
            _uguiPreviewInitialized = false;

            // Dispose UGUI rendering resources - header panel
            _uguiHeaderInputBridge?.Dispose();
            _uguiHeaderInputBridge = null;
            _uguiHeaderPanel?.Dispose();
            _uguiHeaderPanel = null;
            _uguiHeaderHost?.Dispose();
            _uguiHeaderHost = null;
            _uguiHeaderInitialized = false;

            // Dispose UGUI rendering resources - preset panel
            _uguiPresetInputBridge?.Dispose();
            _uguiPresetInputBridge = null;
            _uguiPresetPanel?.Dispose();
            _uguiPresetPanel = null;
            _uguiPresetHost?.Dispose();
            _uguiPresetHost = null;
            _uguiPresetInitialized = false;

            // Dispose UGUI rendering resources - side panel
            _uguiSidePanelInputBridge?.Dispose();
            _uguiSidePanelInputBridge = null;
            _uguiSidePanel?.Dispose();
            _uguiSidePanel = null;
            _uguiSidePanelHost?.Dispose();
            _uguiSidePanelHost = null;
            _uguiSidePanelInitialized = false;

            // Dispose UGUI rendering resources - editor panel
            _uguiEditorInputBridge?.Dispose();
            _uguiEditorInputBridge = null;
            _uguiEditorPanel?.Dispose();
            _uguiEditorPanel = null;
            _uguiEditorHost?.Dispose();
            _uguiEditorHost = null;
            _uguiEditorInitialized = false;
        }

        private float ResolvePromptWorkspacePresetListHeight(float startY, float bottomY, float panelHeight)
        {
            float available = Mathf.Max(96f, bottomY - startY - 170f);
            float preferred = Mathf.Clamp(panelHeight * 0.32f, 96f, 280f);
            return Mathf.Clamp(preferred, 96f, available);
        }

        private GUIStyle _legacyTextAreaStyle;
        private string _legacyTextAreaCachedText = string.Empty;
        private float _legacyTextAreaCachedWidth = -1f;
        private float _legacyTextAreaCachedHeight;

        private string DrawPromptWorkspaceLegacyTextArea(Rect rect, string text)
        {
            string source = text ?? string.Empty;
            if (_legacyTextAreaStyle == null)
            {
                _legacyTextAreaStyle = new GUIStyle(GUI.skin.textArea)
                {
                    wordWrap = true,
                    richText = false
                };
            }

            float contentWidth = Mathf.Max(1f, rect.width - 16f);
            float contentHeight;
            if (string.Equals(source, _legacyTextAreaCachedText, StringComparison.Ordinal) &&
                Mathf.Abs(contentWidth - _legacyTextAreaCachedWidth) < 0.5f)
            {
                contentHeight = _legacyTextAreaCachedHeight;
            }
            else
            {
                contentHeight = Mathf.Max(rect.height, _legacyTextAreaStyle.CalcHeight(new GUIContent(source), contentWidth) + 4f);
                _legacyTextAreaCachedText = source;
                _legacyTextAreaCachedWidth = contentWidth;
                _legacyTextAreaCachedHeight = contentHeight;
            }

            Rect viewRect = new Rect(0f, 0f, contentWidth, contentHeight);
            _promptWorkspaceEditorScroll = new Vector2(
                0f,
                Mathf.Clamp(_promptWorkspaceEditorScroll.y, 0f, Mathf.Max(0f, viewRect.height - rect.height)));
            _promptWorkspaceEditorScroll = GUI.BeginScrollView(rect, _promptWorkspaceEditorScroll, viewRect, false, true);
            GUI.SetNextControlName(PromptWorkspaceEditorControlName);
            string edited = GUI.TextArea(new Rect(0f, 0f, contentWidth, contentHeight), source, _legacyTextAreaStyle);
            GUI.EndScrollView();
            return edited;
        }

        private void DrawPromptWorkspaceStructuredPreview(Rect rect, PromptWorkspaceStructuredPreview preview)
        {
            // UGUI rendering path: Canvas → RenderTexture → GUI.DrawTexture
            if (CanUsePromptWorkspaceUguiPreviewPanel())
            {
                DrawPromptWorkspaceStructuredPreviewUgui(rect, preview);
                return;
            }

            // IMGUI fallback path: original renderer
            _promptWorkspacePreviewRenderer ??= new PromptWorkspaceStructuredPreviewRenderer();
            _promptWorkspacePreviewRenderer.Draw(rect, preview, ref _promptWorkspacePreviewScroll);
        }

        private void DrawPromptWorkspaceStructuredPreviewUgui(Rect rect, PromptWorkspaceStructuredPreview preview)
        {
            EnsureUguiPreviewInitialized(rect);
            ResizeUguiPreviewIfNeeded(rect);

            // Update data and check if panel needs refresh
            _uguiPreviewPanel.SetData(preview);
            bool needsRender = _uguiPreviewPanel.IsDirty;

            // Refresh UGUI elements if dirty (clears IsDirty flag)
            _uguiPreviewPanel.RefreshIfDirty();

            // Re-render the Camera when content changed
            if (needsRender)
            {
                _uguiPreviewHost.Render();
            }

            // Bridge input events (scroll, click) from IMGUI to UGUI
            if (_uguiInputBridge != null && ShouldProcessUguiInput(rect))
            {
                _uguiInputBridge.ProcessEvent(rect);
            }

            // Draw the RenderTexture into the IMGUI rect (single DrawCall)
            _uguiPreviewHost.DrawToImgui(rect);
        }

        private void EnsureUguiPreviewInitialized(Rect initialRect)
        {
            if (_uguiPreviewInitialized)
            {
                return;
            }

            int w = Mathf.Max(1, (int)initialRect.width);
            int h = Mathf.Max(1, (int)initialRect.height);

            _uguiPreviewHost = new UGuiCanvasHost();
            _uguiPreviewHost.Initialize(w, h, "RimChat_PreviewCanvas");
            _uguiLastRtWidth = w;
            _uguiLastRtHeight = h;

            _uguiPreviewPanel = new PromptPreviewPanel();
            _uguiPreviewPanel.Initialize(_uguiPreviewHost, "PreviewPanel");

            _uguiInputBridge = new ImguiToUguiInputBridge(_uguiPreviewHost);

            _uguiPreviewInitialized = true;
        }

        private void ResizeUguiPreviewIfNeeded(Rect rect)
        {
            int newW = Mathf.Max(1, (int)rect.width);
            int newH = Mathf.Max(1, (int)rect.height);
            if (newW != _uguiLastRtWidth || newH != _uguiLastRtHeight)
            {
                _uguiPreviewHost.Resize(newW, newH);
                _uguiLastRtWidth = newW;
                _uguiLastRtHeight = newH;
            }
        }

        #region UGUI Header Panel

        private void DrawPromptWorkspaceHeaderUgui(Rect rect)
        {
            EnsureUguiHeaderInitialized(rect);
            ResizeUguiHostIfNeeded(_uguiHeaderHost, rect, ref _uguiHeaderInitialized);

            _uguiHeaderPanel.SetData(BuildHeaderData());
            bool needsRender = _uguiHeaderPanel.IsDirty;
            _uguiHeaderPanel.RefreshIfDirty();
            if (needsRender) _uguiHeaderHost.Render();

            if (_uguiHeaderInputBridge != null && ShouldProcessUguiInput(rect))
            {
                _uguiHeaderInputBridge.ProcessEvent(rect);
            }

            _uguiHeaderHost.DrawToImgui(rect);
        }

        private void EnsureUguiHeaderInitialized(Rect initialRect)
        {
            if (_uguiHeaderInitialized) return;

            int w = Mathf.Max(1, (int)initialRect.width);
            int h = Mathf.Max(1, (int)initialRect.height);

            _uguiHeaderHost = new UGuiCanvasHost();
            _uguiHeaderHost.Initialize(w, h, "RimChat_HeaderCanvas");

            _uguiHeaderPanel = new WorkspaceHeaderPanel();
            _uguiHeaderPanel.Initialize(_uguiHeaderHost, "HeaderPanel");

            // Wire callbacks
            _uguiHeaderPanel.OnDiplomacyClicked = () => SchedulePromptWorkspaceNavigation(() => SetPromptWorkspaceRoot(PromptWorkbenchChannel.Diplomacy));
            _uguiHeaderPanel.OnRpgClicked = () => SchedulePromptWorkspaceNavigation(() => SetPromptWorkspaceRoot(PromptWorkbenchChannel.Rpg));
            _uguiHeaderPanel.OnChannelDropdownClicked = () =>
            {
                List<FloatMenuOption> options = GetPromptWorkspaceChannels()
                    .Select(channelId => new FloatMenuOption(
                        RimTalkPromptEntryChannelCatalog.GetLabel(channelId),
                        () => SchedulePromptWorkspaceNavigation(() => SetPromptWorkspaceChannel(channelId))))
                    .ToList();
                Find.WindowStack.Add(new FloatMenu(options));
            };
            _uguiHeaderPanel.OnQuickFactionClicked = () => OpenPromptWorkspaceFactionTemplateMenu();
            _uguiHeaderPanel.OnQuickPawnClicked = () => OpenPromptWorkspaceQuickPawnMenu();
            _uguiHeaderPanel.OnImportClicked = () =>
            {
                if (!PersistPromptWorkspaceBufferNow(force: true)) return;
                ShowImportPresetDialog();
            };
            _uguiHeaderPanel.OnExportClicked = () =>
            {
                if (!PersistPromptWorkspaceBufferNow(force: true)) return;
                ShowExportPresetDialog();
            };

            _uguiHeaderInputBridge = new ImguiToUguiInputBridge(_uguiHeaderHost);
            _uguiHeaderInitialized = true;
        }

        private WorkspaceHeaderPanel.HeaderData BuildHeaderData()
        {
            string selectedChannel = EnsurePromptWorkspaceSelection();
            string signature = (_workbenchChannel == PromptWorkbenchChannel.Diplomacy ? "d" : "r") +
                "|" + selectedChannel;
            if (string.Equals(signature, _promptWorkspaceHeaderDataSignature, StringComparison.Ordinal))
            {
                return _promptWorkspaceHeaderDataCache;
            }

            _promptWorkspaceHeaderDataSignature = signature;
            _promptWorkspaceHeaderDataCache = new WorkspaceHeaderPanel.HeaderData
            {
                Title = "RimChat_Tab_PromptWorkbench".Translate().ToString(),
                DiplomacyLabel = "RimChat_PromptWorkbench_ChannelDiplomacy".Translate().ToString(),
                RpgLabel = "RimChat_PromptWorkbench_ChannelRpg".Translate().ToString(),
                IsDiplomacySelected = _workbenchChannel == PromptWorkbenchChannel.Diplomacy,
                ChannelLabel = RimTalkPromptEntryChannelCatalog.GetLabel(selectedChannel),
                QuickFactionLabel = "RimChat_PromptWorkbench_QuickFaction".Translate().ToString(),
                QuickPawnLabel = "RimChat_PromptWorkbench_QuickPawn".Translate().ToString(),
                ImportLabel = "RimChat_Import".Translate().ToString(),
                ExportLabel = "RimChat_Export".Translate().ToString()
            };
            return _promptWorkspaceHeaderDataCache;
        }

        #endregion

        #region UGUI Preset Panel

        private void DrawPromptWorkspacePresetPanelUgui(Rect rect)
        {
            EnsureUguiPresetInitialized(rect);
            ResizeUguiHostIfNeeded(_uguiPresetHost, rect, ref _uguiPresetInitialized);

            _uguiPresetPanel.SetData(BuildPresetPanelData());
            bool needsRender = _uguiPresetPanel.IsDirty;
            _uguiPresetPanel.RefreshIfDirty();
            if (needsRender) _uguiPresetHost.Render();

            if (_uguiPresetInputBridge != null && ShouldProcessUguiInput(rect))
            {
                _uguiPresetInputBridge.ProcessEvent(rect);
            }

            _uguiPresetHost.DrawToImgui(rect);
        }

        private void EnsureUguiPresetInitialized(Rect initialRect)
        {
            if (_uguiPresetInitialized) return;

            int w = Mathf.Max(1, (int)initialRect.width);
            int h = Mathf.Max(1, (int)initialRect.height);

            _uguiPresetHost = new UGuiCanvasHost();
            _uguiPresetHost.Initialize(w, h, "RimChat_PresetCanvas");

            _uguiPresetPanel = new WorkspacePresetPanel();
            _uguiPresetPanel.Initialize(_uguiPresetHost, "PresetPanel");

            // Wire callbacks
            _uguiPresetPanel.OnCreateClicked = () =>
            {
                try
                {
                    PromptPresetConfig created = _promptPresetService.CreateFromLegacy(this, NextPresetName("Preset"));
                    _promptPresetStore.Presets.Add(created);
                    InvalidatePresetSummariesCache();
                    _selectedPromptPresetId = created.Id;
                    _presetRenameBuffer = created.Name;
                    if (!TryActivatePresetById(created.Id, showSuccessMessage: false))
                    {
                        _promptPresetService.SaveAll(_promptPresetStore);
                    }
                    Messages.Message("RimChat_PromptPreset_CreateSuccess".Translate(created.Name), MessageTypeDefOf.NeutralEvent, false);
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimChat] Preset create failed: {ex}");
                }
            };
            _uguiPresetPanel.OnDuplicateClicked = () =>
            {
                PromptPresetConfig selected = GetSelectedPreset();
                if (selected == null) return;
                try
                {
                    PromptPresetConfig duplicated = _promptPresetService.Duplicate(this, selected, NextPresetName(selected.Name));
                    _promptPresetStore.Presets.Add(duplicated);
                    InvalidatePresetSummariesCache();
                    _selectedPromptPresetId = duplicated.Id;
                    _presetRenameBuffer = duplicated.Name;
                    if (!TryActivatePresetById(duplicated.Id, showSuccessMessage: false))
                    {
                        _promptPresetService.SaveAll(_promptPresetStore);
                    }
                    Messages.Message("RimChat_PromptPreset_DuplicateSuccess".Translate(duplicated.Name), MessageTypeDefOf.NeutralEvent, false);
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimChat] Preset duplicate failed: {ex}");
                }
            };
            _uguiPresetPanel.OnPresetClicked = presetId =>
            {
                SchedulePromptWorkspaceNavigation(() =>
                {
                    if (!PersistPromptWorkspaceBufferNow(force: true)) return;
                    _selectedPromptPresetId = presetId;
                    PromptPresetSummary row = GetCachedPresetSummaries().FirstOrDefault(p => string.Equals(p.Id, presetId, StringComparison.Ordinal));
                    if (row != null)
                    {
                        _presetRenameBuffer = row.Name;
                        if (!row.IsActive) TryActivatePresetById(row.Id, showSuccessMessage: false);
                    }
                });
            };
            _uguiPresetPanel.OnModuleClicked = moduleId =>
            {
                // Determine if it's a section or node by checking the module list
                List<PromptWorkbenchModuleItem> modules = GetCachedPromptWorkspaceModules();
                PromptWorkbenchModuleItem target = modules.FirstOrDefault(m => string.Equals(m.Id, moduleId, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(target.Id)) return;

                if (target.Kind == ModuleKind.Section)
                {
                    SchedulePromptWorkspaceNavigation(() => SelectPromptWorkspaceSection(moduleId));
                }
                else
                {
                    SchedulePromptWorkspaceNavigation(() =>
                    {
                        if (!PersistPromptWorkspaceBufferNow(force: true)) return;
                        _promptWorkspaceEditNodeMode = true;
                        _promptWorkspaceSelectedNodeId = moduleId;
                        EnsurePromptWorkspaceBuffer();
                        InvalidatePromptWorkspacePreviewCache();
                    });
                }
            };
            _uguiPresetPanel.OnModuleToggled = (moduleId, enabled) =>
            {
                if (!EnsurePromptWorkspaceEditablePresetForMutation("workspace.module_toggle")) return;
                List<PromptSectionLayoutConfig> sectionLayouts = GetPromptWorkspaceSectionLayouts();
                List<PromptUnifiedNodeLayoutConfig> nodeLayouts = GetPromptWorkspaceNodeLayouts();
                List<PromptWorkbenchModuleItem> modules = GetCachedPromptWorkspaceModules();
                PromptWorkbenchModuleItem target = modules.FirstOrDefault(m => string.Equals(m.Id, moduleId, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(target.Id)) return;

                if (target.Kind == ModuleKind.Section)
                {
                    PromptSectionLayoutConfig layout = sectionLayouts.FirstOrDefault(item => string.Equals(item.SectionId, moduleId, StringComparison.OrdinalIgnoreCase));
                    if (layout != null) { layout.Enabled = enabled; SavePromptWorkspaceSectionLayouts(sectionLayouts); }
                }
                else
                {
                    PromptUnifiedNodeLayoutConfig layout = nodeLayouts.FirstOrDefault(item => string.Equals(item.NodeId, moduleId, StringComparison.OrdinalIgnoreCase));
                    if (layout != null) { layout.Enabled = enabled; SavePromptWorkspaceNodeLayouts(nodeLayouts); }
                }
            };

            _uguiPresetInputBridge = new ImguiToUguiInputBridge(_uguiPresetHost);
            _uguiPresetInitialized = true;
        }

        private WorkspacePresetPanel.PresetPanelData BuildPresetPanelData()
        {
            List<PromptPresetSummary> presets = GetCachedPresetSummaries();
            List<PromptWorkbenchModuleItem> modules = GetCachedPromptWorkspaceModules();

            int presetHash = 17;
            for (int i = 0; i < presets.Count; i++)
            {
                PromptPresetSummary p = presets[i];
                presetHash = presetHash * 31 + ((p.Id ?? string.Empty).GetHashCode());
                presetHash = presetHash * 31 + ((p.Name ?? string.Empty).GetHashCode());
                presetHash = presetHash * 31 + (p.IsActive ? 1 : 0);
                presetHash = presetHash * 31 + (p.IsDefault ? 1 : 0);
            }

            int moduleHash = 23;
            for (int i = 0; i < modules.Count; i++)
            {
                PromptWorkbenchModuleItem m = modules[i];
                moduleHash = moduleHash * 31 + ((m.Id ?? string.Empty).GetHashCode());
                moduleHash = moduleHash * 31 + (m.Enabled ? 1 : 0);
                moduleHash = moduleHash * 31 + (int)m.Kind;
            }

            string signature = _promptWorkspacePerformance.LayoutVersion +
                "|presetHash=" + presetHash +
                "|moduleHash=" + moduleHash +
                "|selPreset=" + (_selectedPromptPresetId ?? string.Empty) +
                "|mode=" + (_promptWorkspaceEditNodeMode ? "node" : "section") +
                "|selSection=" + (_promptWorkspaceSelectedSectionId ?? string.Empty) +
                "|selNode=" + (_promptWorkspaceSelectedNodeId ?? string.Empty);
            if (string.Equals(signature, _promptWorkspacePresetPanelDataSignature, StringComparison.Ordinal))
            {
                return _promptWorkspacePresetPanelDataCache;
            }

            var presetRows = new List<WorkspacePresetPanel.PresetRowData>();
            foreach (PromptPresetSummary p in presets)
            {
                presetRows.Add(new WorkspacePresetPanel.PresetRowData
                {
                    Id = p.Id ?? string.Empty,
                    Name = p.Name ?? string.Empty,
                    IsSelected = string.Equals(p.Id, _selectedPromptPresetId, StringComparison.Ordinal),
                    IsActive = p.IsActive,
                    IsDefault = p.IsDefault
                });
            }

            var moduleRows = new List<WorkspacePresetPanel.ModuleRowData>();
            foreach (PromptWorkbenchModuleItem m in modules)
            {
                moduleRows.Add(new WorkspacePresetPanel.ModuleRowData
                {
                    Id = m.Id ?? string.Empty,
                    Label = m.Label ?? string.Empty,
                    KindTag = m.Kind == ModuleKind.Section
                        ? "RimChat_PromptWorkspaceKind_Section".Translate().ToString()
                        : "RimChat_PromptWorkspaceKind_Node".Translate().ToString(),
                    IsSelected = m.Kind == ModuleKind.Section
                        ? (!_promptWorkspaceEditNodeMode && string.Equals(_promptWorkspaceSelectedSectionId, m.Id, StringComparison.OrdinalIgnoreCase))
                        : (_promptWorkspaceEditNodeMode && string.Equals(_promptWorkspaceSelectedNodeId, m.Id, StringComparison.OrdinalIgnoreCase)),
                    IsEnabled = m.Enabled
                });
            }

            _promptWorkspacePresetPanelDataSignature = signature;
            _promptWorkspacePresetPanelDataCache = new WorkspacePresetPanel.PresetPanelData
            {
                PresetHeaderLabel = "RimChat_PromptWorkbench_PresetHeader".Translate().ToString(),
                CreateLabel = "RimChat_PromptPreset_Create".Translate().ToString(),
                DuplicateLabel = "RimChat_PromptPreset_Duplicate".Translate().ToString(),
                Presets = presetRows,
                ModuleHeaderLabel = "RimChat_PromptWorkspaceModuleHeader".Translate().ToString(),
                Modules = moduleRows
            };
            return _promptWorkspacePresetPanelDataCache;
        }

        #endregion

        #region UGUI Side Panel

        private void DrawPromptWorkspaceSidePanelUgui(Rect rect)
        {
            EnsureUguiSidePanelInitialized(rect);
            ResizeUguiHostIfNeeded(_uguiSidePanelHost, rect, ref _uguiSidePanelInitialized);

            _uguiSidePanel.SetData(BuildSidePanelTabData());
            bool needsRender = _uguiSidePanel.IsDirty;
            _uguiSidePanel.RefreshIfDirty();
            if (needsRender) _uguiSidePanelHost.Render();

            if (_uguiSidePanelInputBridge != null && ShouldProcessUguiInput(rect))
            {
                _uguiSidePanelInputBridge.ProcessEvent(rect);
            }

            _uguiSidePanelHost.DrawToImgui(rect);
        }

        private void EnsureUguiSidePanelInitialized(Rect initialRect)
        {
            if (_uguiSidePanelInitialized) return;

            int w = Mathf.Max(1, (int)initialRect.width);
            int h = Mathf.Max(1, (int)initialRect.height);

            _uguiSidePanelHost = new UGuiCanvasHost();
            _uguiSidePanelHost.Initialize(w, h, "RimChat_SidePanelCanvas");

            _uguiSidePanel = new WorkspaceSidePanel();
            _uguiSidePanel.Initialize(_uguiSidePanelHost, "SidePanel");

            _uguiSidePanel.OnTabChanged = tab =>
            {
                switch (tab)
                {
                    case WorkspaceSidePanel.SidePanelTab.Preview:
                        _workbenchSidePanelTab = PromptWorkbenchInfoPanel.Preview;
                        break;
                    case WorkspaceSidePanel.SidePanelTab.FullPreview:
                        _workbenchSidePanelTab = PromptWorkbenchInfoPanel.FullPreview;
                        break;
                    case WorkspaceSidePanel.SidePanelTab.Variables:
                        _workbenchSidePanelTab = PromptWorkbenchInfoPanel.Variables;
                        break;
                }
                MarkWorkspaceDirty(WorkspaceDirtySidePanel);
            };

            _uguiSidePanelInputBridge = new ImguiToUguiInputBridge(_uguiSidePanelHost);
            _uguiSidePanelInitialized = true;
        }

        private WorkspaceSidePanel.SidePanelData BuildSidePanelTabData()
        {
            WorkspaceSidePanel.SidePanelTab activeTab;
            switch (_workbenchSidePanelTab)
            {
                case PromptWorkbenchInfoPanel.FullPreview:
                    activeTab = WorkspaceSidePanel.SidePanelTab.FullPreview;
                    break;
                case PromptWorkbenchInfoPanel.Variables:
                    activeTab = WorkspaceSidePanel.SidePanelTab.Variables;
                    break;
                default:
                    activeTab = WorkspaceSidePanel.SidePanelTab.Preview;
                    break;
            }

            string signature = "tab=" + (int)activeTab;
            if (string.Equals(signature, _promptWorkspaceSidePanelDataSignature, StringComparison.Ordinal))
            {
                return _promptWorkspaceSidePanelDataCache;
            }

            _promptWorkspaceSidePanelDataSignature = signature;
            _promptWorkspaceSidePanelDataCache = new WorkspaceSidePanel.SidePanelData
            {
                ActiveTab = activeTab,
                PreviewTabLabel = "RimChat_PreviewTitleShort".Translate().ToString(),
                FullPreviewTabLabel = "RimChat_PromptWorkbench_FullPreviewTab".Translate().ToString(),
                VariablesTabLabel = "RimChat_PromptWorkbench_VariablesTab".Translate().ToString()
            };
            return _promptWorkspaceSidePanelDataCache;
        }

        #endregion

        #region UGUI Editor Panel

        private void DrawPromptWorkspaceEditorPanelUgui(Rect rect)
        {
            EnsureUguiEditorInitialized(rect);
            ResizeUguiHostIfNeeded(_uguiEditorHost, rect, ref _uguiEditorInitialized);

            _uguiEditorPanel.SetData(BuildEditorChromeData());
            bool needsRender = _uguiEditorPanel.IsDirty;
            _uguiEditorPanel.RefreshIfDirty();
            if (needsRender) _uguiEditorHost.Render();

            if (_uguiEditorInputBridge != null && ShouldProcessUguiInput(rect))
            {
                _uguiEditorInputBridge.ProcessEvent(rect);
            }

            _uguiEditorHost.DrawToImgui(rect);
        }

        private void EnsureUguiEditorInitialized(Rect initialRect)
        {
            if (_uguiEditorInitialized) return;

            int w = Mathf.Max(1, (int)initialRect.width);
            int h = Mathf.Max(1, (int)initialRect.height);

            _uguiEditorHost = new UGuiCanvasHost();
            _uguiEditorHost.Initialize(w, h, "RimChat_EditorCanvas");

            _uguiEditorPanel = new WorkspaceEditorPanel();
            _uguiEditorPanel.Initialize(_uguiEditorHost, "EditorPanel");

            _uguiEditorPanel.OnEnabledToggled = () =>
            {
                if (!EnsurePromptWorkspaceEditablePresetForMutation("workspace.module_toggle")) return;
                List<PromptSectionLayoutConfig> sectionLayouts = GetPromptWorkspaceSectionLayouts();
                List<PromptUnifiedNodeLayoutConfig> nodeLayouts = GetPromptWorkspaceNodeLayouts();
                if (_promptWorkspaceEditNodeMode)
                {
                    PromptUnifiedNodeLayoutConfig layout = nodeLayouts.FirstOrDefault(item =>
                        string.Equals(item.NodeId, _promptWorkspaceSelectedNodeId, StringComparison.OrdinalIgnoreCase));
                    if (layout != null) { layout.Enabled = !layout.Enabled; SavePromptWorkspaceNodeLayouts(nodeLayouts); }
                }
                else
                {
                    PromptSectionLayoutConfig layout = sectionLayouts.FirstOrDefault(item =>
                        string.Equals(item.SectionId, _promptWorkspaceSelectedSectionId, StringComparison.OrdinalIgnoreCase));
                    if (layout != null) { layout.Enabled = !layout.Enabled; SavePromptWorkspaceSectionLayouts(sectionLayouts); }
                }
            };
            _uguiEditorPanel.OnUndoClicked = TryUndoPromptWorkspaceText;
            _uguiEditorPanel.OnRedoClicked = TryRedoPromptWorkspaceText;
            _uguiEditorPanel.OnSaveClicked = TrySavePromptWorkspaceNow;
            _uguiEditorPanel.OnResetClicked = TryResetPromptWorkspaceCurrentEntry;

            _uguiEditorInputBridge = new ImguiToUguiInputBridge(_uguiEditorHost);
            _uguiEditorInitialized = true;
        }

        private WorkspaceEditorPanel.EditorData BuildEditorChromeData()
        {
            string kindTag = _promptWorkspaceEditNodeMode
                ? "RimChat_PromptWorkspaceKind_Node".Translate().ToString()
                : "RimChat_PromptWorkspaceKind_Section".Translate().ToString();
            string label = _promptWorkspaceEditNodeMode
                ? PromptUnifiedNodeSchemaCatalog.GetDisplayLabel(_promptWorkspaceSelectedNodeId)
                : (PromptSectionSchemaCatalog.TryGetSection(_promptWorkspaceSelectedSectionId, out PromptSectionSchemaItem section)
                    ? section.GetDisplayLabel()
                    : _promptWorkspaceSelectedSectionId);

            // Resolve enabled state
            List<PromptSectionLayoutConfig> sectionLayouts = GetPromptWorkspaceSectionLayouts();
            List<PromptUnifiedNodeLayoutConfig> nodeLayouts = GetPromptWorkspaceNodeLayouts();
            bool isEnabled;
            if (_promptWorkspaceEditNodeMode)
            {
                PromptUnifiedNodeLayoutConfig layout = nodeLayouts.FirstOrDefault(item =>
                    string.Equals(item.NodeId, _promptWorkspaceSelectedNodeId, StringComparison.OrdinalIgnoreCase));
                isEnabled = layout?.Enabled ?? true;
            }
            else
            {
                PromptSectionLayoutConfig layout = sectionLayouts.FirstOrDefault(item =>
                    string.Equals(item.SectionId, _promptWorkspaceSelectedSectionId, StringComparison.OrdinalIgnoreCase));
                isEnabled = layout?.Enabled ?? true;
            }

            // Validation
            string editorText = _promptWorkspaceEditorBuffer ?? string.Empty;
            TemplateVariableValidationContext validationContext = BuildPromptWorkspaceValidationContext();
            UpdatePromptWorkspaceValidationState(editorText, validationContext);
            string validationText = BuildLiveValidationStatusText(_promptWorkspaceValidationResult, editorText);
            WorkspaceEditorPanel.ValidationState validationState;
            if (_promptWorkspaceValidationResult.HasScribanError)
            {
                validationState = WorkspaceEditorPanel.ValidationState.Error;
            }
            else if (_promptWorkspaceValidationResult.UnknownVariables.Count > 0)
            {
                validationState = WorkspaceEditorPanel.ValidationState.Warning;
            }
            else
            {
                validationState = WorkspaceEditorPanel.ValidationState.Ok;
            }

            bool canUndo = CanUndoPromptWorkspaceText();
            bool canRedo = CanRedoPromptWorkspaceText();
            string signature =
                "layout=" + _promptWorkspacePerformance.LayoutVersion +
                "|editor=" + _promptWorkspacePerformance.EditorTextVersion +
                "|validation=" + _promptWorkspacePerformance.ValidationResultVersion +
                "|mode=" + (_promptWorkspaceEditNodeMode ? "node" : "section") +
                "|section=" + (_promptWorkspaceSelectedSectionId ?? string.Empty) +
                "|node=" + (_promptWorkspaceSelectedNodeId ?? string.Empty) +
                "|enabled=" + (isEnabled ? "1" : "0") +
                "|undo=" + (canUndo ? "1" : "0") +
                "|redo=" + (canRedo ? "1" : "0") +
                "|state=" + (int)validationState +
                "|text=" + validationText;
            if (string.Equals(signature, _promptWorkspaceEditorPanelDataSignature, StringComparison.Ordinal))
            {
                return _promptWorkspaceEditorPanelDataCache;
            }

            _promptWorkspaceEditorPanelDataSignature = signature;
            _promptWorkspaceEditorPanelDataCache = new WorkspaceEditorPanel.EditorData
            {
                KindTag = $"[{kindTag}]",
                ModuleLabel = label,
                EnabledLabel = "RimChat_RimTalkCompatEnable".Translate().ToString(),
                IsEnabled = isEnabled,
                UndoLabel = "RimChat_PromptWorkspaceToolbar_Undo".Translate().ToString(),
                RedoLabel = "RimChat_PromptWorkspaceToolbar_Redo".Translate().ToString(),
                SaveLabel = "RimChat_PromptWorkspaceToolbar_Save".Translate().ToString(),
                ResetLabel = "RimChat_PromptWorkspaceToolbar_Reset".Translate().ToString(),
                CanUndo = canUndo,
                CanRedo = canRedo,
                ValidationText = validationText,
                Validation = validationState
            };
            return _promptWorkspaceEditorPanelDataCache;
        }

        #endregion

        /// <summary>
        /// Shared utility: resize a UGuiCanvasHost if the IMGUI rect dimensions changed.
        /// </summary>
        private static void ResizeUguiHostIfNeeded(UGuiCanvasHost host, Rect rect, ref bool initialized)
        {
            if (host == null || !initialized) return;
            int newW = Mathf.Max(1, (int)rect.width);
            int newH = Mathf.Max(1, (int)rect.height);
            host.Resize(newW, newH);
        }

        private static bool ShouldProcessUguiInput(Rect rect)
        {
            Event evt = Event.current;
            if (evt == null)
            {
                return false;
            }

            switch (evt.type)
            {
                case EventType.MouseDown:
                case EventType.MouseUp:
                case EventType.MouseDrag:
                case EventType.ScrollWheel:
                case EventType.MouseMove:
                    return rect.Contains(evt.mousePosition);
                case EventType.KeyDown:
                case EventType.KeyUp:
                    return true;
                default:
                    return false;
            }
        }

        private PromptWorkspaceStructuredPreview GetPromptWorkspaceStructuredPreview()
        {
            // Pure cache read — Tick is already called once per frame in DrawPromptSectionWorkspace.
            // If channel changed, invalidate so next Tick creates a fresh build state.
            if (_promptWorkspacePreviewCachedRoot != _workbenchChannel ||
                !string.Equals(_promptWorkspacePreviewCachedChannel, _workbenchPromptChannel ?? string.Empty, StringComparison.Ordinal))
            {
                InvalidatePromptWorkspacePreviewCache();
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
            _promptWorkspacePerformance.PreviewStartPending = false;
            _promptWorkspacePerformance.PreviewStartDelayFrames = 0;
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
            _promptWorkspacePerformance.PreviewStartPending = true;
            _promptWorkspacePerformance.PreviewStartDelayFrames = PromptWorkspacePreviewStartDelayFrames;
            MarkWorkspaceDirty(WorkspaceDirtySidePanel);
            _sidePanelContentRtCache?.MarkDirty();
            _promptWorkspacePreviewRenderer?.MarkDirty();
            _uguiPreviewPanel?.MarkDirty();
            _uguiSidePanel?.MarkDirty();
            _uguiEditorPanel?.MarkDirty();
        }

        private void InvalidatePromptWorkspaceNodeUiCaches()
        {
            _promptWorkspaceNodeListCacheChannel = string.Empty;
            _promptWorkspaceNodeListCache.Clear();
            _promptWorkspaceNodeLayoutCacheChannel = string.Empty;
            _promptWorkspaceNodeLayoutCache.Clear();
            _promptWorkspaceSectionLayoutCacheChannel = string.Empty;
            _promptWorkspaceSectionLayoutCache.Clear();
            _promptWorkspaceModuleCacheChannel = string.Empty;
            _promptWorkspaceModuleCache.Clear();
            MarkWorkspaceDirty(WorkspaceDirtyPresetPanel | WorkspaceDirtyModuleList | WorkspaceDirtySidePanel);
            _sidePanelContentRtCache?.MarkDirty();
            _uguiPresetPanel?.MarkDirty();
            _uguiEditorPanel?.MarkDirty();
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
