using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>/// Dependencies: Unity Time/GUI, Verse localization, shared rounded-draw helpers in Dialog_DiplomacyDialogue.
 /// Responsibility: render immersive diplomacy waiting status with rotating phrases and subtle dynamic effects.
 ///</summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private const float TypingStatusRotateSeconds = 2.5f;
        private const float TypingStatusPulseSpeed = 3.1f;
        private const float TypingStatusSweepCycleSeconds = 1.8f;
        private static readonly string[] DiplomacyTypingStatusKeys = new[]
        {
            "RimChat_DiplomacyTypingStatus_01",
            "RimChat_DiplomacyTypingStatus_02",
            "RimChat_DiplomacyTypingStatus_03",
            "RimChat_DiplomacyTypingStatus_04",
            "RimChat_DiplomacyTypingStatus_05",
            "RimChat_DiplomacyTypingStatus_06"
        };

        private void DrawDiplomacyTypingStatus(Rect rect)
        {
            Rect panelRect = BuildTypingStatusPanelRect(rect);
            DrawTypingStatusPanel(panelRect);
            DrawTypingStatusText(panelRect, ResolveDiplomacyTypingStatusText());
            DrawTypingStatusDots(panelRect);
            DrawTypingStatusSweep(panelRect);
            ResetTypingStatusStyle();
        }

        private static Rect BuildTypingStatusPanelRect(Rect rect)
        {
            float width = Mathf.Max(180f, rect.width);
            return new Rect(rect.x, rect.y, width, Mathf.Max(18f, rect.height));
        }

        private void DrawTypingStatusPanel(Rect panelRect)
        {
            DrawRoundedRect(panelRect, new Color(0.12f, 0.2f, 0.29f, 0.76f), 7f);
            float outlineAlpha = 0.24f + 0.14f * Mathf.Sin(Time.realtimeSinceStartup * 1.9f);
            GUI.color = new Color(0.47f, 0.79f, 1f, outlineAlpha);
            Widgets.DrawBox(panelRect);
            GUI.color = Color.white;
        }

        private string ResolveDiplomacyTypingStatusText()
        {
            string fallback = "RimChat_AIIsTyping".Translate();
            if (DiplomacyTypingStatusKeys.Length == 0)
            {
                return fallback;
            }

            int index = (int)(Time.realtimeSinceStartup / TypingStatusRotateSeconds);
            string key = DiplomacyTypingStatusKeys[index % DiplomacyTypingStatusKeys.Length];
            TaggedString translated = key.Translate();
            return translated.RawText == key ? fallback : translated.RawText;
        }

        private void DrawTypingStatusText(Rect panelRect, string statusText)
        {
            Rect textRect = new Rect(panelRect.x + 8f, panelRect.y, panelRect.width - 56f, panelRect.height - 3f);
            GUI.color = new Color(0.84f, 0.93f, 1f, 0.95f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            DrawSingleLineClippedLabel(textRect, statusText);
            GUI.color = Color.white;
        }

        private void DrawTypingStatusDots(Rect panelRect)
        {
            float phase = Time.realtimeSinceStartup * TypingStatusPulseSpeed;
            float dotSize = 4f;
            float dotY = panelRect.y + panelRect.height * 0.5f - dotSize * 0.5f - 1f;
            float startX = panelRect.xMax - 25f;
            for (int i = 0; i < 3; i++)
            {
                DrawTypingStatusDot(startX + i * 6f, dotY, dotSize, phase - i * 0.85f);
            }

            GUI.color = Color.white;
        }

        private void DrawTypingStatusDot(float x, float y, float size, float phase)
        {
            float alpha = 0.22f + 0.72f * (0.5f + 0.5f * Mathf.Sin(phase));
            GUI.color = new Color(0.63f, 0.89f, 1f, alpha);
            GUI.DrawTexture(new Rect(x, y, size, size), CircleTexture);
        }

        private void DrawTypingStatusSweep(Rect panelRect)
        {
            Rect trackRect = new Rect(panelRect.x + 8f, panelRect.yMax - 3f, panelRect.width - 16f, 1.5f);
            Widgets.DrawBoxSolid(trackRect, new Color(0.38f, 0.53f, 0.63f, 0.28f));
            float progress = (Time.realtimeSinceStartup % TypingStatusSweepCycleSeconds) / TypingStatusSweepCycleSeconds;
            float sweepWidth = Mathf.Max(32f, trackRect.width * 0.28f);
            float sweepX = Mathf.Lerp(trackRect.x - sweepWidth, trackRect.xMax, progress);
            DrawTypingStatusSweepSegment(trackRect, sweepX, sweepWidth);
        }

        private static void DrawTypingStatusSweepSegment(Rect trackRect, float sweepX, float sweepWidth)
        {
            float segmentStart = Mathf.Max(trackRect.x, sweepX);
            float segmentEnd = Mathf.Min(trackRect.xMax, sweepX + sweepWidth);
            if (segmentEnd <= segmentStart)
            {
                return;
            }

            GUI.color = new Color(0.68f, 0.91f, 1f, 0.82f);
            GUI.DrawTexture(new Rect(segmentStart, trackRect.y, segmentEnd - segmentStart, trackRect.height), WhiteTexture);
            GUI.color = Color.white;
        }

        private static void ResetTypingStatusStyle()
        {
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }
    }
}
