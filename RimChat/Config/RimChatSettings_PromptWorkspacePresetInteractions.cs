using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: prompt preset service, workbench selection state, and RimWorld widgets/messages.
    /// Responsibility: handle workspace preset panel interactions, inline rename, and default-preset auto-fork gating.
    /// </summary>
    public partial class RimChatSettings : ModSettings
    {
        private const int PromptWorkspacePresetDoubleClickMs = 360;
        private string _promptWorkspaceRenamingPresetId = string.Empty;
        private string _promptWorkspacePresetRenameBuffer = string.Empty;
        private bool _promptWorkspacePresetRenameFocusRequested;
        private bool _promptWorkspacePresetRenameHadFocus;
        private string _promptWorkspaceLastClickedPresetId = string.Empty;
        private DateTime _promptWorkspaceLastPresetClickUtc = DateTime.MinValue;

        private void DrawPromptWorkspacePresetActions(Rect rect)
        {
            float w = (rect.width - 6f) * 0.5f;
            if (Widgets.ButtonText(new Rect(rect.x, rect.y, w, rect.height), "RimChat_PromptPreset_Create".Translate()))
            {
                try
                {
                    PromptPresetConfig created = _promptPresetService.CreateFromLegacy(this, NextPresetName("Preset"));
                    _promptPresetStore.Presets.Add(created);
                    _selectedPromptPresetId = created.Id;
                    _presetRenameBuffer = created.Name;
                    CancelPromptWorkspaceInlineRename();
                    Log.Message($"[RimChat][PresetDiag] Workspace create clicked. add_id={created.Id}, count={_promptPresetStore.Presets.Count}");
                    if (!TryActivatePresetById(created.Id, showSuccessMessage: false))
                    {
                        _promptPresetService.SaveAll(_promptPresetStore);
                    }
                    Messages.Message("RimChat_PromptPreset_CreateSuccess".Translate(created.Name), MessageTypeDefOf.NeutralEvent, false);
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimChat][PresetDiag] Workspace create failed: {ex}");
                    Messages.Message("RimChat_PromptPreset_ActivateFailed".Translate(ex.Message), MessageTypeDefOf.RejectInput, false);
                }
            }

            PromptPresetConfig selected = GetSelectedPreset();
            if (selected != null &&
                Widgets.ButtonText(new Rect(rect.x + w + 6f, rect.y, w, rect.height), "RimChat_PromptPreset_Duplicate".Translate()))
            {
                try
                {
                    PromptPresetConfig duplicated = _promptPresetService.Duplicate(this, selected, NextPresetName(selected.Name));
                    _promptPresetStore.Presets.Add(duplicated);
                    _selectedPromptPresetId = duplicated.Id;
                    _presetRenameBuffer = duplicated.Name;
                    CancelPromptWorkspaceInlineRename();
                    Log.Message($"[RimChat][PresetDiag] Workspace duplicate clicked. add_id={duplicated.Id}, count={_promptPresetStore.Presets.Count}");
                    if (!TryActivatePresetById(duplicated.Id, showSuccessMessage: false))
                    {
                        _promptPresetService.SaveAll(_promptPresetStore);
                    }
                    Messages.Message("RimChat_PromptPreset_DuplicateSuccess".Translate(duplicated.Name), MessageTypeDefOf.NeutralEvent, false);
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimChat][PresetDiag] Workspace duplicate failed: {ex}");
                    Messages.Message("RimChat_PromptPreset_ActivateFailed".Translate(ex.Message), MessageTypeDefOf.RejectInput, false);
                }
            }
        }

        private void DrawPromptWorkspacePresetList(Rect rect)
        {
            List<PromptPresetSummary> rows = _promptPresetService.BuildSummaries(_promptPresetStore);
            const float rowStep = 26f;
            Rect view = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(rect.height, rows.Count * rowStep));
            Widgets.BeginScrollView(rect, ref _promptPresetScroll, view);
            for (int i = 0; i < rows.Count; i++)
            {
                DrawPromptWorkspacePresetRow(rows[i], new Rect(0f, i * rowStep, view.width, rowStep - 2f));
            }

            Widgets.EndScrollView();
        }

        private void DrawPromptWorkspacePresetRow(PromptPresetSummary row, Rect rowRect)
        {
            bool selected = string.Equals(row.Id, _selectedPromptPresetId, StringComparison.Ordinal);
            if (selected)
            {
                Widgets.DrawBoxSolid(rowRect, new Color(0.27f, 0.38f, 0.56f));
            }
            else if (Mouse.IsOver(rowRect))
            {
                Widgets.DrawBoxSolid(rowRect, new Color(0.18f, 0.18f, 0.20f));
            }

            float iconW = 20f;
            float iconGap = 4f;
            Rect deleteRect = new Rect(rowRect.xMax - iconW, rowRect.y + 2f, iconW, rowRect.height - 4f);
            Rect duplicateRect = new Rect(deleteRect.x - iconGap - iconW, rowRect.y + 2f, iconW, rowRect.height - 4f);
            Rect labelRect = new Rect(rowRect.x + 20f, rowRect.y + 2f, duplicateRect.x - rowRect.x - 24f, rowRect.height - 4f);
            Rect clickRect = new Rect(rowRect.x, rowRect.y, labelRect.xMax - rowRect.x, rowRect.height);

            if (row.IsActive)
            {
                GUI.color = Color.green;
                Widgets.Label(new Rect(rowRect.x + 4f, rowRect.y, 14f, rowRect.height), "▶");
                GUI.color = Color.white;
            }

            DrawPromptWorkspacePresetRowLabel(row, labelRect);
            HandlePromptWorkspacePresetRowClicks(row, clickRect);
            DrawPromptWorkspacePresetRowActions(row, duplicateRect, deleteRect);
        }

        private void DrawPromptWorkspacePresetRowLabel(PromptPresetSummary row, Rect rect)
        {
            if (string.Equals(_promptWorkspaceRenamingPresetId, row.Id, StringComparison.Ordinal))
            {
                DrawPromptWorkspaceInlineRenameField(row, rect);
                return;
            }

            bool oldWrap = Text.WordWrap;
            Text.WordWrap = false;
            string title = row.Name ?? string.Empty;
            if (row.IsDefault)
            {
                title = $"{title}  {("RimChat_PromptPreset_DefaultReadonlyTag".Translate())}";
            }

            Widgets.Label(rect, title.Truncate(rect.width));
            Text.WordWrap = oldWrap;
        }

        private void HandlePromptWorkspacePresetRowClicks(PromptPresetSummary row, Rect clickRect)
        {
            if (!Widgets.ButtonInvisible(clickRect))
            {
                return;
            }

            bool shouldRename = IsPromptWorkspacePresetDoubleClick(row.Id);
            SchedulePromptWorkspaceNavigation(() =>
            {
                if (!PersistPromptWorkspaceBufferNow(force: true))
                {
                    return;
                }

                bool changedSelection = !string.Equals(_selectedPromptPresetId, row.Id, StringComparison.Ordinal);
                _selectedPromptPresetId = row.Id;
                _presetRenameBuffer = row.Name;
                if (changedSelection && !row.IsActive)
                {
                    TryActivatePresetById(row.Id, showSuccessMessage: false);
                }

                if (!shouldRename)
                {
                    return;
                }

                BeginPromptWorkspaceInlineRename(row);
            });
        }

        private void DrawPromptWorkspacePresetRowActions(PromptPresetSummary row, Rect duplicateRect, Rect deleteRect)
        {
            if (Widgets.ButtonText(duplicateRect, "D"))
            {
                DuplicatePromptWorkspacePreset(row.Id);
            }

            TooltipHandler.TipRegion(duplicateRect, "RimChat_PromptPreset_RowDuplicateTip".Translate());

            bool canDelete = !row.IsDefault && _promptPresetStore?.Presets?.Count > 1;
            bool oldEnabled = GUI.enabled;
            if (!canDelete)
            {
                GUI.enabled = false;
            }

            if (Widgets.ButtonText(deleteRect, "X"))
            {
                DeletePromptWorkspacePreset(row.Id);
            }

            GUI.enabled = oldEnabled;
            TooltipHandler.TipRegion(
                deleteRect,
                canDelete
                    ? "RimChat_PromptPreset_RowDeleteTip".Translate()
                    : "RimChat_PromptPreset_RowDeleteDefaultBlocked".Translate());
        }

        private void DuplicatePromptWorkspacePreset(string sourcePresetId)
        {
            PromptPresetConfig source = _promptPresetStore?.Presets?.FirstOrDefault(p => string.Equals(p.Id, sourcePresetId, StringComparison.Ordinal));
            if (source == null)
            {
                return;
            }

            PromptPresetConfig duplicated = _promptPresetService.Duplicate(this, source, NextPresetName(source.Name));
            _promptPresetStore.Presets.Add(duplicated);
            _selectedPromptPresetId = duplicated.Id;
            _presetRenameBuffer = duplicated.Name;
            CancelPromptWorkspaceInlineRename();
            if (!TryActivatePresetById(duplicated.Id, showSuccessMessage: false))
            {
                _promptPresetService.SaveAll(_promptPresetStore);
            }
            Messages.Message("RimChat_PromptPreset_DuplicateSuccess".Translate(duplicated.Name), MessageTypeDefOf.NeutralEvent, false);
        }

        private void DeletePromptWorkspacePreset(string presetId)
        {
            PromptPresetConfig selected = _promptPresetStore?.Presets?.FirstOrDefault(p => string.Equals(p.Id, presetId, StringComparison.Ordinal));
            if (selected == null || _promptPresetService.IsDefaultPreset(_promptPresetStore, selected.Id))
            {
                return;
            }

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

            if (string.Equals(_promptWorkspaceRenamingPresetId, presetId, StringComparison.Ordinal))
            {
                CancelPromptWorkspaceInlineRename();
            }

            Messages.Message("RimChat_PromptPreset_DeleteSuccess".Translate(deletedName), MessageTypeDefOf.NeutralEvent, false);
        }

        private void BeginPromptWorkspaceInlineRename(PromptPresetSummary row)
        {
            if (row == null)
            {
                return;
            }

            _selectedPromptPresetId = row.Id;
            if (!row.IsActive)
            {
                TryActivatePresetById(row.Id, showSuccessMessage: false);
            }

            if (row.IsDefault && !EnsurePromptWorkspaceEditablePresetForMutation("preset.rename"))
            {
                return;
            }

            PromptPresetConfig target = GetSelectedPreset();
            if (target == null)
            {
                return;
            }

            _promptWorkspaceRenamingPresetId = target.Id;
            _promptWorkspacePresetRenameBuffer = target.Name ?? string.Empty;
            _promptWorkspacePresetRenameFocusRequested = true;
            _promptWorkspacePresetRenameHadFocus = false;
        }

        private void DrawPromptWorkspaceInlineRenameField(PromptPresetSummary row, Rect rect)
        {
            const string controlName = "RimChat_PromptWorkspacePresetInlineRename";
            GUI.SetNextControlName(controlName);
            _promptWorkspacePresetRenameBuffer = Widgets.TextField(rect, _promptWorkspacePresetRenameBuffer ?? string.Empty);
            if (_promptWorkspacePresetRenameFocusRequested)
            {
                GUI.FocusControl(controlName);
                _promptWorkspacePresetRenameFocusRequested = false;
            }

            bool focused = string.Equals(GUI.GetNameOfFocusedControl(), controlName, StringComparison.Ordinal);
            Event evt = Event.current;
            if (focused)
            {
                _promptWorkspacePresetRenameHadFocus = true;
                if (evt != null && evt.type == EventType.KeyDown)
                {
                    if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    {
                        CommitPromptWorkspaceInlineRename();
                        evt.Use();
                        return;
                    }

                    if (evt.keyCode == KeyCode.Escape)
                    {
                        CancelPromptWorkspaceInlineRename();
                        evt.Use();
                        return;
                    }
                }
            }
            else if (_promptWorkspacePresetRenameHadFocus)
            {
                CommitPromptWorkspaceInlineRename();
            }

            TooltipHandler.TipRegion(rect, "RimChat_PromptPreset_InlineRenameHint".Translate());
        }

        private void CommitPromptWorkspaceInlineRename()
        {
            PromptPresetConfig target = _promptPresetStore?.Presets?.FirstOrDefault(p =>
                string.Equals(p.Id, _promptWorkspaceRenamingPresetId, StringComparison.Ordinal));
            if (target == null)
            {
                CancelPromptWorkspaceInlineRename();
                return;
            }

            string beforeName = target.Name ?? string.Empty;
            string next = (_promptWorkspacePresetRenameBuffer ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(next))
            {
                target.Name = next;
                target.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
                _presetRenameBuffer = target.Name;
                _promptPresetService.SaveAll(_promptPresetStore);
                if (!string.Equals(beforeName, target.Name, StringComparison.Ordinal))
                {
                    Messages.Message("RimChat_PromptPreset_RenameSuccess".Translate(target.Name), MessageTypeDefOf.NeutralEvent, false);
                }
            }

            CancelPromptWorkspaceInlineRename();
        }

        private void CancelPromptWorkspaceInlineRename()
        {
            _promptWorkspaceRenamingPresetId = string.Empty;
            _promptWorkspacePresetRenameBuffer = string.Empty;
            _promptWorkspacePresetRenameFocusRequested = false;
            _promptWorkspacePresetRenameHadFocus = false;
        }

        private bool IsPromptWorkspacePresetDoubleClick(string presetId)
        {
            DateTime nowUtc = DateTime.UtcNow;
            bool doubled = string.Equals(_promptWorkspaceLastClickedPresetId, presetId, StringComparison.Ordinal) &&
                           _promptWorkspaceLastPresetClickUtc != DateTime.MinValue &&
                           (nowUtc - _promptWorkspaceLastPresetClickUtc).TotalMilliseconds <= PromptWorkspacePresetDoubleClickMs;
            _promptWorkspaceLastClickedPresetId = presetId ?? string.Empty;
            _promptWorkspaceLastPresetClickUtc = nowUtc;
            return doubled;
        }

        private bool EnsurePromptWorkspaceEditablePresetForMutation(string mutationReason)
        {
            if (_promptPresetService == null)
            {
                EnsurePresetStoreReady();
            }

            if (_promptPresetService == null || _promptPresetStore == null)
            {
                Messages.Message("RimChat_PromptPreset_AutoForkFailed".Translate(mutationReason ?? string.Empty), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            if (!_promptPresetService.EnsureEditablePresetForMutation(
                    this,
                    _promptPresetStore,
                    _selectedPromptPresetId,
                    "Custom",
                    out PromptPresetConfig editablePreset,
                    out bool forked,
                    out string error))
            {
                Messages.Message("RimChat_PromptPreset_AutoForkFailed".Translate(error ?? string.Empty), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            if (editablePreset != null)
            {
                _selectedPromptPresetId = editablePreset.Id;
                _presetRenameBuffer = editablePreset.Name;
            }

            if (!forked)
            {
                return true;
            }

            InvalidateWorkbenchEditingChannelConfig();
            ResetRimTalkEntryContentBuffer();
            InvalidatePromptWorkspaceNodeUiCaches();
            InvalidatePromptWorkspacePreviewCache();
            EnsurePromptWorkspaceBuffer();
            Messages.Message("RimChat_PromptPreset_AutoForked".Translate(editablePreset?.Name ?? string.Empty), MessageTypeDefOf.NeutralEvent, false);
            return true;
        }
    }
}
