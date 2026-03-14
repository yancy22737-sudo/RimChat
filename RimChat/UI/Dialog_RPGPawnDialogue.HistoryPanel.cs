using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>/// Dependencies: Dialog_RPGPawnDialogue runtime state, Verse widgets.
    /// Responsibility: maintain per-session RPG dialogue/action history and render center history panel.
    /// </summary>
    public partial class Dialog_RPGPawnDialogue
    {
        private enum SessionActionOutcome
        {
            Success = 0,
            Failure = 1,
            Error = 2
        }

        private sealed class SessionActionRecord
        {
            public string ActionName = string.Empty;
            public SessionActionOutcome Outcome = SessionActionOutcome.Success;
            public string Reason = string.Empty;
        }

        private sealed class SessionDialogueRecord
        {
            public bool IsPlayer;
            public string SpeakerName = string.Empty;
            public string Text = string.Empty;
            public int NpcTurnSequence;
            public List<SessionActionRecord> Actions = new List<SessionActionRecord>();
        }

        private readonly List<SessionDialogueRecord> sessionHistoryRecords = new List<SessionDialogueRecord>();
        private bool isSessionHistoryPanelOpen;
        private Rect sessionHistoryPanelRect = Rect.zero;
        private bool hasSessionHistoryPanelRect;
        private Vector2 sessionHistoryScrollPosition = Vector2.zero;
        private int latestNpcTurnSequence;

        private static readonly Color SessionHistoryPanelBg = new Color(0.07f, 0.07f, 0.09f, 0.95f);
        private static readonly Color SessionHistoryBorder = new Color(0.25f, 0.25f, 0.3f, 1f);
        private static readonly Color SessionHistoryActionText = new Color(0.82f, 0.89f, 1f, 0.96f);
        private static readonly Color SessionHistorySpeakerText = new Color(0.95f, 0.95f, 0.95f, 0.98f);
        private const int SessionHistoryRecordCap = 180;
        private const float SessionHistoryPanelMinWidth = 560f;
        private const float SessionHistoryPanelMaxWidth = 860f;
        private const float SessionHistoryPanelMinHeight = 260f;
        private const float SessionHistoryPanelMaxHeight = 600f;
        private const float SessionHistoryViewportWidthRatio = 0.94f;
        private const float SessionHistoryViewportMinWidth = 520f;
        private const float SessionHistoryViewportMaxWidth = 840f;

        private void DrawHistoryToggleButton(Rect boxRect)
        {
            Rect buttonRect = new Rect(boxRect.x + 14f, boxRect.yMax - 33f, 100f, 26f);
            if (Widgets.ButtonText(buttonRect, "RimChat_RPGHistoryButton".Translate().ToString()))
            {
                isSessionHistoryPanelOpen = !isSessionHistoryPanelOpen;
                if (isSessionHistoryPanelOpen)
                {
                    isViewingHistory = false;
                    ResetDialogueTextPaging();
                }
            }
        }

        private void DrawSessionHistoryPanel(Rect inRect)
        {
            if (!isSessionHistoryPanelOpen)
            {
                hasSessionHistoryPanelRect = false;
                return;
            }

            Rect topArea = new Rect(20f, 20f, inRect.width - 40f, Math.Max(120f, inRect.height - DialogueBoxHeight - 40f));
            float panelWidth = Mathf.Clamp(topArea.width * 0.58f, SessionHistoryPanelMinWidth, SessionHistoryPanelMaxWidth);
            float panelHeight = Mathf.Clamp(topArea.height * 0.84f, SessionHistoryPanelMinHeight, SessionHistoryPanelMaxHeight);
            Rect panelRect = new Rect(
                topArea.x + (topArea.width - panelWidth) * 0.5f,
                topArea.y + (topArea.height - panelHeight) * 0.5f,
                panelWidth,
                panelHeight);

            sessionHistoryPanelRect = panelRect;
            hasSessionHistoryPanelRect = true;

            Widgets.DrawBoxSolid(panelRect, SessionHistoryPanelBg);
            GUI.color = SessionHistoryBorder;
            Widgets.DrawBox(panelRect, 2);
            GUI.color = Color.white;

            DrawSessionHistoryPanelHeader(panelRect);
            DrawSessionHistoryPanelBody(panelRect);
        }

        private void DrawSessionHistoryPanelHeader(Rect panelRect)
        {
            Rect titleRect = new Rect(panelRect.x + 12f, panelRect.y + 8f, panelRect.width - 60f, 24f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;
            Widgets.Label(titleRect, "RimChat_RPGHistoryPanelTitle".Translate().ToString());
            Text.Anchor = TextAnchor.UpperLeft;

            Rect closeRect = new Rect(panelRect.xMax - 32f, panelRect.y + 8f, 20f, 20f);
            if (Widgets.ButtonText(closeRect, "×"))
            {
                isSessionHistoryPanelOpen = false;
                hasSessionHistoryPanelRect = false;
            }
        }

        private void DrawSessionHistoryPanelBody(Rect panelRect)
        {
            Rect bodyRect = BuildSessionHistoryViewportRect(panelRect);
            if (sessionHistoryRecords.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(bodyRect, "RimChat_RPGHistoryEmpty".Translate().ToString());
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            float viewWidth = bodyRect.width - 18f;
            float viewHeight = CalculateSessionHistoryContentHeight(viewWidth);
            Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);

            sessionHistoryScrollPosition = GUI.BeginScrollView(bodyRect, sessionHistoryScrollPosition, viewRect);
            DrawSessionHistoryRecords(viewRect);
            GUI.EndScrollView();
        }

        private Rect BuildSessionHistoryViewportRect(Rect panelRect)
        {
            float contentWidth = Mathf.Clamp(panelRect.width * SessionHistoryViewportWidthRatio, SessionHistoryViewportMinWidth, SessionHistoryViewportMaxWidth);
            float x = panelRect.x + (panelRect.width - contentWidth) * 0.5f;
            float y = panelRect.y + 34f;
            float height = panelRect.height - 42f;
            return new Rect(x, y, contentWidth, height);
        }

        private float CalculateSessionHistoryContentHeight(float width)
        {
            float total = 2f;
            for (int i = 0; i < sessionHistoryRecords.Count; i++)
            {
                total += MeasureSessionHistoryRecordHeight(sessionHistoryRecords[i], width) + 4f;
            }

            return Mathf.Max(total, 40f);
        }

        private void DrawSessionHistoryRecords(Rect viewRect)
        {
            float currentY = 1f;
            for (int i = 0; i < sessionHistoryRecords.Count; i++)
            {
                SessionDialogueRecord record = sessionHistoryRecords[i];
                float height = MeasureSessionHistoryRecordHeight(record, viewRect.width);
                Rect recordRect = new Rect(0f, currentY, viewRect.width, height);
                DrawSessionHistoryRecord(record, recordRect);
                currentY += height + 4f;
            }
        }

        private float MeasureSessionHistoryRecordHeight(SessionDialogueRecord record, float width)
        {
            if (record == null)
            {
                return 42f;
            }

            float contentWidth = Math.Max(120f, width - 10f);
            string dialogueLine = BuildCompactDialogueLine(record);
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Tiny;
            float textHeight = Text.CalcHeight(dialogueLine, contentWidth);
            float total = 6f + textHeight;

            if (record.Actions != null && record.Actions.Count > 0)
            {
                for (int i = 0; i < record.Actions.Count; i++)
                {
                    string actionLine = BuildSessionActionLine(record.Actions[i], i == 0);
                    total += Text.CalcHeight(actionLine, contentWidth - 8f) + 1f;
                }
            }

            Text.Font = previousFont;
            return Math.Max(26f, total + 4f);
        }

        private void DrawSessionHistoryRecord(SessionDialogueRecord record, Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.03f));
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;

            Rect innerRect = rect.ContractedBy(4f);
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            string dialogueLine = BuildCompactDialogueLine(record);
            float dialogueHeight = Text.CalcHeight(dialogueLine, innerRect.width);
            Rect textRect = new Rect(innerRect.x, innerRect.y, innerRect.width, dialogueHeight);
            GUI.color = SessionHistorySpeakerText;
            Widgets.Label(textRect, dialogueLine);
            GUI.color = Color.white;

            if (record?.Actions == null || record.Actions.Count == 0)
            {
                Text.Font = previousFont;
                return;
            }

            float actionY = textRect.yMax + 1f;
            GUI.color = SessionHistoryActionText;
            for (int i = 0; i < record.Actions.Count; i++)
            {
                string actionLine = BuildSessionActionLine(record.Actions[i], i == 0);
                float actionHeight = Text.CalcHeight(actionLine, innerRect.width - 4f);
                Rect actionRect = new Rect(innerRect.x + 4f, actionY, innerRect.width - 4f, actionHeight);
                Widgets.Label(actionRect, actionLine);
                actionY += actionHeight + 1f;
            }

            Text.Font = previousFont;
            GUI.color = Color.white;
        }

        private static string BuildCompactDialogueLine(SessionDialogueRecord record)
        {
            if (record == null)
            {
                return string.Empty;
            }

            string speaker = string.IsNullOrWhiteSpace(record.SpeakerName) ? "NPC" : record.SpeakerName.Trim();
            string text = string.IsNullOrWhiteSpace(record.Text) ? string.Empty : record.Text.Trim();
            return $"{speaker}: {text}";
        }

        private string BuildSessionActionLine(SessionActionRecord action, bool includePrefix)
        {
            if (action == null)
            {
                return string.Empty;
            }

            string result = GetSessionActionResultLabel(action.Outcome);
            string actionLabel = GetRpgActionLabel(action.ActionName ?? string.Empty);
            string reason = string.IsNullOrWhiteSpace(action.Reason)
                ? string.Empty
                : " " + "RimChat_RPGHistoryReasonPrefix".Translate(action.Reason.Trim()).ToString();
            string prefix = includePrefix ? "RimChat_RPGHistoryActionPrefix".Translate().ToString() + " " : string.Empty;
            return $"{prefix}• {actionLabel} ({result}){reason}";
        }

        private static string GetSessionActionResultLabel(SessionActionOutcome outcome)
        {
            return outcome switch
            {
                SessionActionOutcome.Success => "RimChat_RPGHistoryActionResultSuccess".Translate().ToString(),
                SessionActionOutcome.Failure => "RimChat_RPGHistoryActionResultFailure".Translate().ToString(),
                _ => "RimChat_RPGHistoryActionResultError".Translate().ToString()
            };
        }

        private bool TryHandleHistoryPanelMouseDown(Event current)
        {
            if (!isSessionHistoryPanelOpen || current == null || current.type != EventType.MouseDown)
            {
                return false;
            }

            if (hasSessionHistoryPanelRect && sessionHistoryPanelRect.Contains(current.mousePosition))
            {
                current.Use();
                return true;
            }

            isSessionHistoryPanelOpen = false;
            hasSessionHistoryPanelRect = false;
            current.Use();
            return true;
        }

        private void RecordSessionDialogueTurn(string speakerName, string text, bool isPlayerSpeaker)
        {
            string normalizedText = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedText))
            {
                return;
            }

            var record = new SessionDialogueRecord
            {
                IsPlayer = isPlayerSpeaker,
                SpeakerName = speakerName ?? string.Empty,
                Text = normalizedText,
                NpcTurnSequence = isPlayerSpeaker ? 0 : ++latestNpcTurnSequence
            };
            sessionHistoryRecords.Add(record);

            if (sessionHistoryRecords.Count > SessionHistoryRecordCap)
            {
                sessionHistoryRecords.RemoveAt(0);
            }
        }

        private void RecordSessionActionOutcome(string actionName, SessionActionOutcome outcome, string reason)
        {
            if (string.IsNullOrWhiteSpace(actionName))
            {
                return;
            }

            SessionDialogueRecord anchor = FindLatestNpcSessionRecord();
            if (anchor == null)
            {
                anchor = new SessionDialogueRecord
                {
                    IsPlayer = false,
                    SpeakerName = target?.LabelShort ?? "NPC",
                    Text = string.Empty,
                    NpcTurnSequence = ++latestNpcTurnSequence
                };
                sessionHistoryRecords.Add(anchor);
            }

            anchor.Actions ??= new List<SessionActionRecord>();
            anchor.Actions.Add(new SessionActionRecord
            {
                ActionName = actionName.Trim(),
                Outcome = outcome,
                Reason = reason?.Trim() ?? string.Empty
            });
        }

        private SessionDialogueRecord FindLatestNpcSessionRecord()
        {
            for (int i = sessionHistoryRecords.Count - 1; i >= 0; i--)
            {
                SessionDialogueRecord record = sessionHistoryRecords[i];
                if (record != null && !record.IsPlayer)
                {
                    return record;
                }
            }

            return null;
        }
    }
}
