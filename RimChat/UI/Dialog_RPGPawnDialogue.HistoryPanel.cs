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
            public string Detail = string.Empty;
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
        private static readonly Color SessionHistoryBodyText = new Color(0.9f, 0.9f, 0.92f, 0.98f);
        private static readonly Color SessionHistoryRecordOddBg = new Color(1f, 1f, 1f, 0.025f);
        private static readonly Color SessionHistoryRecordEvenBg = new Color(1f, 1f, 1f, 0.055f);
        private const int SessionHistoryRecordCap = 180;
        private const float SessionHistoryPanelMinWidth = 760f;
        private const float SessionHistoryPanelMaxWidth = 900f;
        private const float SessionHistoryPanelMinHeight = 240f;
        private const float SessionHistoryPanelMaxHeight = 600f;

        private void DrawHistoryToggleButton(Rect boxRect)
        {
            Rect buttonRect = new Rect(boxRect.x + 14f, boxRect.yMax - 33f, 100f, 26f);
            float alpha = isSessionHistoryPanelOpen
                ? 1f
                : (Mouse.IsOver(buttonRect) ? 0.9f : 0.45f);
            Color previousColor = GUI.color;
            GUI.color = new Color(previousColor.r, previousColor.g, previousColor.b, alpha);
            if (Widgets.ButtonText(buttonRect, "RimChat_RPGHistoryButton".Translate().ToString()))
            {
                isSessionHistoryPanelOpen = !isSessionHistoryPanelOpen;
                if (isSessionHistoryPanelOpen)
                {
                    isViewingHistory = false;
                    ResetDialogueTextPaging();
                }
            }
            GUI.color = previousColor;
        }

        private void DrawSessionHistoryPanel(Rect inRect)
        {
            if (!isSessionHistoryPanelOpen)
            {
                hasSessionHistoryPanelRect = false;
                return;
            }

            float topPadding = 62f;
            float sidePadding = Mathf.Clamp(inRect.width * 0.18f, 200f, 340f);
            float bottomPadding = DialogueBoxHeight + 30f;
            Rect topArea = new Rect(
                sidePadding,
                topPadding,
                Mathf.Max(220f, inRect.width - sidePadding * 2f),
                Mathf.Max(120f, inRect.height - topPadding - bottomPadding));

            float panelWidth = Mathf.Clamp(topArea.width * 0.8f, SessionHistoryPanelMinWidth, SessionHistoryPanelMaxWidth);
            float panelHeight = Mathf.Clamp(topArea.height * 0.96f, SessionHistoryPanelMinHeight, SessionHistoryPanelMaxHeight);
            float panelX = topArea.x + (topArea.width - panelWidth) * 0.46f;
            Rect panelRect = new Rect(
                Mathf.Clamp(panelX, topArea.x, topArea.xMax - panelWidth),
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
            Rect bodyRect = new Rect(panelRect.x + 12f, panelRect.y + 44f, panelRect.width - 24f, panelRect.height - 54f);
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
            float total = 4f;
            for (int i = 0; i < sessionHistoryRecords.Count; i++)
            {
                total += MeasureSessionHistoryRecordHeight(sessionHistoryRecords[i], width) + 2f;
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
                DrawSessionHistoryRecord(record, recordRect, i);
                currentY += height + 1f;
            }
        }

        private float MeasureSessionHistoryRecordHeight(SessionDialogueRecord record, float width)
        {
            if (record == null)
            {
                return 36f;
            }

            float contentWidth = Math.Max(120f, width - 10f);
            float textHeight = CalcHeightWithFont(record.Text ?? string.Empty, contentWidth, GameFont.Tiny);
            float total = 18f + textHeight;

            SessionActionRecord finalSuccessAction = GetFinalSuccessfulAction(record);
            if (finalSuccessAction != null)
            {
                string actionLine = BuildSessionActionLine(finalSuccessAction);
                total += 5f + CalcHeightWithFont(actionLine, contentWidth - 4f, GameFont.Tiny);
            }

            return Math.Max(32f, total + 2f);
        }

        private void DrawSessionHistoryRecord(SessionDialogueRecord record, Rect rect, int index)
        {
            Widgets.DrawBoxSolid(rect, index % 2 == 0 ? SessionHistoryRecordEvenBg : SessionHistoryRecordOddBg);
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;

            Rect innerRect = rect.ContractedBy(4f);
            Rect speakerRect = new Rect(innerRect.x, innerRect.y, innerRect.width, 16f);
            GUI.color = SessionHistorySpeakerText;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Tiny;
            Widgets.Label(speakerRect, BuildSpeakerLine(record?.SpeakerName));
            GUI.color = Color.white;

            Text.Font = GameFont.Tiny;
            GUI.color = SessionHistoryBodyText;
            Rect textRect = new Rect(innerRect.x, speakerRect.yMax + 1f, innerRect.width, Text.CalcHeight(record?.Text ?? string.Empty, innerRect.width));
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(textRect, record?.Text ?? string.Empty);
            GUI.color = Color.white;

            SessionActionRecord finalSuccessAction = GetFinalSuccessfulAction(record);
            if (finalSuccessAction == null)
            {
                return;
            }

            Text.Font = GameFont.Tiny;
            GUI.color = SessionHistoryActionText;
            string actionLine = BuildSessionActionLine(finalSuccessAction);
            float actionHeight = Text.CalcHeight(actionLine, innerRect.width - 4f);
            Rect actionRect = new Rect(innerRect.x + 4f, textRect.yMax, innerRect.width - 4f, actionHeight);
            Widgets.Label(actionRect, actionLine);

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        private static float CalcHeightWithFont(string text, float width, GameFont font)
        {
            GameFont previous = Text.Font;
            Text.Font = font;
            float height = Text.CalcHeight(text ?? string.Empty, width);
            Text.Font = previous;
            return height;
        }

        private string BuildSessionActionLine(SessionActionRecord action)
        {
            if (action == null)
            {
                return string.Empty;
            }

            string actionLabel = GetRpgActionLabel(action.ActionName ?? string.Empty);
            string detail = (action.Detail ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(detail))
            {
                detail = (action.Reason ?? string.Empty).Trim();
            }
            if (!string.IsNullOrWhiteSpace(detail))
            {
                return "RimChat_RPGHistorySystemMessageWithDetailFormat".Translate(actionLabel, detail).ToString();
            }

            return "RimChat_RPGHistorySystemMessageFormat".Translate(actionLabel).ToString();
        }

        private static SessionActionRecord GetFinalSuccessfulAction(SessionDialogueRecord record)
        {
            if (record?.Actions == null || record.Actions.Count == 0)
            {
                return null;
            }

            for (int i = record.Actions.Count - 1; i >= 0; i--)
            {
                SessionActionRecord action = record.Actions[i];
                if (action != null && action.Outcome == SessionActionOutcome.Success)
                {
                    return action;
                }
            }

            return null;
        }

        private static string BuildSpeakerLine(string speakerName)
        {
            string normalizedSpeaker = string.IsNullOrWhiteSpace(speakerName) ? "NPC" : speakerName.Trim();
            return "RimChat_RPGHistorySpeakerSays".Translate(normalizedSpeaker).ToString();
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

        private void RecordSessionActionOutcome(string actionName, SessionActionOutcome outcome, string reason, string detail = "")
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
                Reason = reason?.Trim() ?? string.Empty,
                Detail = detail?.Trim() ?? string.Empty
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
