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
        private const float SessionHistoryPanelMinWidth = 760f;
        private const float SessionHistoryPanelMaxWidth = 1020f;
        private const float SessionHistoryPanelMinHeight = 260f;
        private const float SessionHistoryPanelMaxHeight = 600f;

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
            float panelWidth = Mathf.Clamp(topArea.width * 0.72f, SessionHistoryPanelMinWidth, SessionHistoryPanelMaxWidth);
            float panelHeight = Mathf.Clamp(topArea.height * 0.82f, SessionHistoryPanelMinHeight, SessionHistoryPanelMaxHeight);
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
            Rect titleRect = new Rect(panelRect.x + 16f, panelRect.y + 10f, panelRect.width - 72f, 30f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Medium;
            Widgets.Label(titleRect, "RimChat_RPGHistoryPanelTitle".Translate().ToString());
            Text.Anchor = TextAnchor.UpperLeft;

            Rect closeRect = new Rect(panelRect.xMax - 44f, panelRect.y + 10f, 28f, 28f);
            if (Widgets.ButtonText(closeRect, "×"))
            {
                isSessionHistoryPanelOpen = false;
                hasSessionHistoryPanelRect = false;
            }
        }

        private void DrawSessionHistoryPanelBody(Rect panelRect)
        {
            Rect bodyRect = new Rect(panelRect.x + 16f, panelRect.y + 48f, panelRect.width - 32f, panelRect.height - 62f);
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

        private float CalculateSessionHistoryContentHeight(float width)
        {
            float total = 8f;
            for (int i = 0; i < sessionHistoryRecords.Count; i++)
            {
                total += MeasureSessionHistoryRecordHeight(sessionHistoryRecords[i], width) + 10f;
            }

            return Mathf.Max(total, 40f);
        }

        private void DrawSessionHistoryRecords(Rect viewRect)
        {
            float currentY = 6f;
            for (int i = 0; i < sessionHistoryRecords.Count; i++)
            {
                SessionDialogueRecord record = sessionHistoryRecords[i];
                float height = MeasureSessionHistoryRecordHeight(record, viewRect.width);
                Rect recordRect = new Rect(0f, currentY, viewRect.width, height);
                DrawSessionHistoryRecord(record, recordRect);
                currentY += height + 10f;
            }
        }

        private float MeasureSessionHistoryRecordHeight(SessionDialogueRecord record, float width)
        {
            if (record == null)
            {
                return 42f;
            }

            float contentWidth = Math.Max(120f, width - 18f);
            float textHeight = Text.CalcHeight(record.Text ?? string.Empty, contentWidth);
            float total = 34f + textHeight;

            if (record.Actions != null && record.Actions.Count > 0)
            {
                total += 26f;
                for (int i = 0; i < record.Actions.Count; i++)
                {
                    string actionLine = BuildSessionActionLine(record.Actions[i]);
                    total += Text.CalcHeight(actionLine, contentWidth - 18f) + 4f;
                }
            }

            return Math.Max(58f, total + 8f);
        }

        private void DrawSessionHistoryRecord(SessionDialogueRecord record, Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.04f));
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;

            Rect innerRect = rect.ContractedBy(8f);
            Rect speakerRect = new Rect(innerRect.x, innerRect.y, innerRect.width, 24f);
            GUI.color = SessionHistorySpeakerText;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;
            Widgets.Label(speakerRect, record?.SpeakerName ?? string.Empty);
            GUI.color = Color.white;

            Rect textRect = new Rect(innerRect.x, speakerRect.yMax + 2f, innerRect.width, Text.CalcHeight(record?.Text ?? string.Empty, innerRect.width));
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(textRect, record?.Text ?? string.Empty);

            if (record?.Actions == null || record.Actions.Count == 0)
            {
                return;
            }

            Rect actionTitleRect = new Rect(innerRect.x, textRect.yMax + 4f, innerRect.width, 20f);
            GUI.color = SessionHistoryActionText;
            Widgets.Label(actionTitleRect, "RimChat_RPGHistoryActionPrefix".Translate().ToString());

            float actionY = actionTitleRect.yMax + 2f;
            for (int i = 0; i < record.Actions.Count; i++)
            {
                string actionLine = BuildSessionActionLine(record.Actions[i]);
                float actionHeight = Text.CalcHeight(actionLine, innerRect.width - 18f);
                Rect actionRect = new Rect(innerRect.x + 12f, actionY, innerRect.width - 12f, actionHeight);
                Widgets.Label(actionRect, actionLine);
                actionY += actionHeight + 4f;
            }

            GUI.color = Color.white;
        }

        private string BuildSessionActionLine(SessionActionRecord action)
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
            return $"• {actionLabel} ({result}){reason}";
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
