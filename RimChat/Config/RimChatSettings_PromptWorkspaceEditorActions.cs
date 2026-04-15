using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: prompt workspace buffer state and preset mutability guard.
    /// Responsibility: provide workspace editor toolbar actions (undo/redo/save/reset) with per-target text history.
    /// </summary>
    public partial class RimChatSettings : ModSettings
    {
        private const int PromptWorkspaceHistoryLimit = 64;

        private sealed class PromptWorkspaceTextHistory
        {
            public readonly List<string> Undo = new List<string>();
            public readonly List<string> Redo = new List<string>();
        }

        private readonly Dictionary<string, PromptWorkspaceTextHistory> _promptWorkspaceTextHistories =
            new Dictionary<string, PromptWorkspaceTextHistory>(StringComparer.Ordinal);

        private void DrawPromptWorkspaceToolbar(Rect rect)
        {
            float width = (rect.width - 18f) / 4f;
            Rect undoRect = new Rect(rect.x, rect.y, width, rect.height);
            Rect redoRect = new Rect(undoRect.xMax + 6f, rect.y, width, rect.height);
            Rect saveRect = new Rect(redoRect.xMax + 6f, rect.y, width, rect.height);
            Rect resetRect = new Rect(saveRect.xMax + 6f, rect.y, width, rect.height);

            DrawPromptWorkspaceToolbarButton(
                undoRect,
                "RimChat_PromptWorkspaceToolbar_Undo",
                CanUndoPromptWorkspaceText(),
                TryUndoPromptWorkspaceText);
            DrawPromptWorkspaceToolbarButton(
                redoRect,
                "RimChat_PromptWorkspaceToolbar_Redo",
                CanRedoPromptWorkspaceText(),
                TryRedoPromptWorkspaceText);
            DrawPromptWorkspaceToolbarButton(
                saveRect,
                "RimChat_PromptWorkspaceToolbar_Save",
                true,
                TrySavePromptWorkspaceNow);
            DrawPromptWorkspaceToolbarButton(
                resetRect,
                "RimChat_PromptWorkspaceToolbar_Reset",
                true,
                TryResetPromptWorkspaceCurrentEntry);
        }

        private static void DrawPromptWorkspaceToolbarButton(Rect rect, string key, bool enabled, Action action)
        {
            bool oldEnabled = GUI.enabled;
            if (!enabled)
            {
                GUI.enabled = false;
            }

            if (Widgets.ButtonText(rect, key.Translate()))
            {
                action?.Invoke();
            }

            GUI.enabled = oldEnabled;
        }

        private bool CanUndoPromptWorkspaceText()
        {
            string key = BuildPromptWorkspaceHistoryKey();
            return !string.IsNullOrWhiteSpace(key) &&
                   _promptWorkspaceTextHistories.TryGetValue(key, out PromptWorkspaceTextHistory history) &&
                   history.Undo.Count > 0;
        }

        private bool CanRedoPromptWorkspaceText()
        {
            string key = BuildPromptWorkspaceHistoryKey();
            return !string.IsNullOrWhiteSpace(key) &&
                   _promptWorkspaceTextHistories.TryGetValue(key, out PromptWorkspaceTextHistory history) &&
                   history.Redo.Count > 0;
        }

        private void TryUndoPromptWorkspaceText()
        {
            if (!EnsurePromptWorkspaceEditablePresetForMutation("workspace.undo"))
            {
                return;
            }

            string key = BuildPromptWorkspaceHistoryKey();
            if (string.IsNullOrWhiteSpace(key) ||
                !_promptWorkspaceTextHistories.TryGetValue(key, out PromptWorkspaceTextHistory history) ||
                history.Undo.Count == 0)
            {
                return;
            }

            string current = _promptWorkspaceEditorBuffer ?? string.Empty;
            string previous = PopFromHistory(history.Undo);
            PushToHistory(history.Redo, current);
            SetPromptWorkspaceCurrentEditorTextWithoutHistory(previous);
        }

        private void TryRedoPromptWorkspaceText()
        {
            if (!EnsurePromptWorkspaceEditablePresetForMutation("workspace.redo"))
            {
                return;
            }

            string key = BuildPromptWorkspaceHistoryKey();
            if (string.IsNullOrWhiteSpace(key) ||
                !_promptWorkspaceTextHistories.TryGetValue(key, out PromptWorkspaceTextHistory history) ||
                history.Redo.Count == 0)
            {
                return;
            }

            string current = _promptWorkspaceEditorBuffer ?? string.Empty;
            string next = PopFromHistory(history.Redo);
            PushToHistory(history.Undo, current);
            SetPromptWorkspaceCurrentEditorTextWithoutHistory(next);
        }

        private void TrySavePromptWorkspaceNow()
        {
            TryScheduleValidation(immediate: true);
            ForcePromptWorkspaceValidationNow();
            if (PersistPromptWorkspaceBufferNow(force: true, persistToDisk: true))
            {
                if (_promptWorkspaceLastPersistHadMaterialChange)
                {
                    Messages.Message("RimChat_PromptWorkspace_SaveDone".Translate(), MessageTypeDefOf.NeutralEvent, false);
                }
            }
        }

        private void TryResetPromptWorkspaceCurrentEntry()
        {
            if (!EnsurePromptWorkspaceEditablePresetForMutation("workspace.reset_entry"))
            {
                return;
            }

            string current = GetPromptWorkspaceCurrentEditorText();
            string fallback = _promptWorkspaceEditNodeMode
                ? PromptUnifiedCatalog.CreateFallback().ResolveNode(_workbenchPromptChannel, _promptWorkspaceSelectedNodeId)
                : RimTalkPromptEntryDefaultsProvider.ResolveContent(_workbenchPromptChannel, _promptWorkspaceSelectedSectionId);
            string next = fallback ?? string.Empty;
            if (string.Equals(current ?? string.Empty, next, StringComparison.Ordinal))
            {
                return;
            }

            RecordPromptWorkspaceTextHistoryBeforeMutation(current ?? string.Empty);
            SetPromptWorkspaceCurrentEditorTextWithoutHistory(next);
            PersistPromptWorkspaceBufferNow();
        }

        private void HandlePromptWorkspaceKeyboardShortcuts()
        {
            Event evt = Event.current;
            if (evt == null || evt.type != EventType.KeyDown || !evt.control)
            {
                return;
            }

            if (evt.keyCode == KeyCode.S)
            {
                TrySavePromptWorkspaceNow();
                evt.Use();
                return;
            }

            if (evt.keyCode == KeyCode.R)
            {
                TryResetPromptWorkspaceCurrentEntry();
                evt.Use();
                return;
            }

            if (evt.keyCode == KeyCode.Z && !evt.shift)
            {
                TryUndoPromptWorkspaceText();
                evt.Use();
                return;
            }

            if (evt.keyCode == KeyCode.Y || (evt.keyCode == KeyCode.Z && evt.shift))
            {
                TryRedoPromptWorkspaceText();
                evt.Use();
            }
        }

        private void CapturePromptWorkspaceLiveEditorText()
        {
            if (!string.Equals(GUI.GetNameOfFocusedControl(), PromptWorkspaceEditorControlName, StringComparison.Ordinal))
            {
                return;
            }

            TextEditor editor = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
            if (editor == null)
            {
                return;
            }

            string liveText = editor.text ?? string.Empty;
            if (string.Equals(liveText, _promptWorkspaceEditorBuffer ?? string.Empty, StringComparison.Ordinal))
            {
                return;
            }

            _promptWorkspaceEditorBuffer = liveText;
            _promptWorkspaceBufferedChannel = _workbenchPromptChannel ?? string.Empty;
            _promptWorkspaceBufferedNodeMode = _promptWorkspaceEditNodeMode;
            _promptWorkspaceBufferedSectionId = _promptWorkspaceSelectedSectionId ?? string.Empty;
            _promptWorkspaceBufferedNodeId = _promptWorkspaceSelectedNodeId ?? string.Empty;
            _promptWorkspaceHasPendingPersist = true;
            NotifyPromptWorkspaceEditorTextChanged();
        }

        private void SetPromptWorkspaceCurrentEditorTextWithoutHistory(string text)
        {
            string next = text ?? string.Empty;
            if (string.Equals(next, _promptWorkspaceEditorBuffer ?? string.Empty, StringComparison.Ordinal))
            {
                return;
            }

            _promptWorkspaceEditorBuffer = next;
            _promptWorkspaceBufferedChannel = _workbenchPromptChannel ?? string.Empty;
            _promptWorkspaceBufferedNodeMode = _promptWorkspaceEditNodeMode;
            _promptWorkspaceBufferedSectionId = _promptWorkspaceSelectedSectionId ?? string.Empty;
            _promptWorkspaceBufferedNodeId = _promptWorkspaceSelectedNodeId ?? string.Empty;
            MarkPromptWorkspaceDirty();
        }

        private void RecordPromptWorkspaceTextHistoryBeforeMutation(string oldText)
        {
            string key = BuildPromptWorkspaceHistoryKey();
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            PromptWorkspaceTextHistory history = GetOrCreatePromptWorkspaceTextHistory(key);
            PushToHistory(history.Undo, oldText ?? string.Empty);
            history.Redo.Clear();
        }

        private string BuildPromptWorkspaceHistoryKey()
        {
            string presetId = _selectedPromptPresetId ?? string.Empty;
            string channel = string.IsNullOrWhiteSpace(_workbenchPromptChannel)
                ? EnsurePromptWorkspaceSelection()
                : _workbenchPromptChannel;
            string mode = _promptWorkspaceEditNodeMode ? "node" : "section";
            string target = _promptWorkspaceEditNodeMode
                ? (_promptWorkspaceSelectedNodeId ?? string.Empty)
                : (_promptWorkspaceSelectedSectionId ?? string.Empty);
            if (string.IsNullOrWhiteSpace(presetId) ||
                string.IsNullOrWhiteSpace(channel) ||
                string.IsNullOrWhiteSpace(target))
            {
                return string.Empty;
            }

            return $"{presetId}|{channel}|{mode}|{target}";
        }

        private PromptWorkspaceTextHistory GetOrCreatePromptWorkspaceTextHistory(string key)
        {
            if (!_promptWorkspaceTextHistories.TryGetValue(key, out PromptWorkspaceTextHistory history))
            {
                history = new PromptWorkspaceTextHistory();
                _promptWorkspaceTextHistories[key] = history;
            }

            return history;
        }

        private static void PushToHistory(List<string> stack, string value)
        {
            if (stack == null)
            {
                return;
            }

            string normalized = value ?? string.Empty;
            if (stack.Count > 0 && string.Equals(stack[stack.Count - 1], normalized, StringComparison.Ordinal))
            {
                return;
            }

            stack.Add(normalized);
            if (stack.Count <= PromptWorkspaceHistoryLimit)
            {
                return;
            }

            stack.RemoveAt(0);
        }

        private static string PopFromHistory(List<string> stack)
        {
            if (stack == null || stack.Count == 0)
            {
                return string.Empty;
            }

            int index = stack.Count - 1;
            string value = stack[index];
            stack.RemoveAt(index);
            return value ?? string.Empty;
        }
    }
}
