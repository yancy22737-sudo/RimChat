using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>/// Dependencies: portrait layout helpers, Verse widgets, and localized feedback text producers.
 /// Responsibility: queue and render portrait-anchored RPG floating subtitles with gentle rise/fade motion.
 ///</summary>
    public partial class Dialog_RPGPawnDialogue
    {
        private struct ActionFeedbackEntry
        {
            public string Text;
            public Color Color;
            public float CreatedAt;
            public float Duration;
        }

        private static Texture2D subtitleCornerTexture;
        private static Texture2D SubtitleCornerTexture => subtitleCornerTexture ?? (subtitleCornerTexture = CreateSubtitleCornerTexture());

        private readonly List<ActionFeedbackEntry> actionFeedbackEntries = new List<ActionFeedbackEntry>();
        private static readonly Color ActionSuccessColor = new Color(0.45f, 0.9f, 0.55f, 1f);
        private static readonly Color ActionFailureColor = new Color(0.95f, 0.6f, 0.45f, 1f);
        private static readonly Color ActionErrorColor = new Color(0.95f, 0.4f, 0.4f, 1f);
        private static readonly Color ActionInfoColor = new Color(0.55f, 0.78f, 0.98f, 1f);
        private static readonly Vector2 ActionFeedbackShadowOffset = new Vector2(1.5f, 2f);
        private const float ActionFeedbackDefaultDuration = 10f;
        private const float ActionFeedbackFadeOutDuration = 1f;
        private const float ActionFeedbackHorizontalOffset = 20f;
        private const float ActionFeedbackVerticalInset = 60f;
        private const float ActionFeedbackWidth = 300f;
        private const float ActionFeedbackSpacing = 8f;
        private const float ActionFeedbackStackRunway = 26f;
        private const float ActionFeedbackBaseRiseDistance = 14f;
        private const float ActionFeedbackFadeRiseDistance = 12f;
        private const float ActionFeedbackHorizontalPadding = 16f;
        private const float ActionFeedbackVerticalPadding = 9f;
        private const float ActionFeedbackAccentWidth = 3f;
        private const float ActionFeedbackCornerRadius = 8f;
        private const float ActionFeedbackMinHeight = 34f;
        private const float ActionFeedbackShadowVerticalOffset = 3f;
        private const float ActionFeedbackShadowHorizontalOffset = 1f;
        private const int ActionFeedbackMaxCount = 8;

        private void AddActionFeedback(string text, Color color, float duration = ActionFeedbackDefaultDuration)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            actionFeedbackEntries.Add(new ActionFeedbackEntry
            {
                Text = text,
                Color = color,
                Duration = ActionFeedbackDefaultDuration,
                CreatedAt = Time.realtimeSinceStartup
            });

            if (actionFeedbackEntries.Count > ActionFeedbackMaxCount)
            {
                actionFeedbackEntries.RemoveAt(0);
            }
        }

        private void AddSystemFeedback(string text, float duration = ActionFeedbackDefaultDuration)
        {
            AddActionFeedback(text, ActionInfoColor, duration);
        }

        private void DrawActionFeedback(Rect inRect)
        {
            RemoveExpiredActionFeedback();
            if (actionFeedbackEntries.Count == 0 || !TryGetActionFeedbackAnchorRect(inRect, out Rect anchorRect))
            {
                return;
            }

            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            Color oldColor = GUI.color;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            DrawActionFeedbackEntries(anchorRect);
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            GUI.color = oldColor;
        }

        private bool TryGetActionFeedbackAnchorRect(Rect inRect, out Rect anchorRect)
        {
            float visibility = GetActionFeedbackVisibility();
            if (visibility <= 0.01f)
            {
                anchorRect = Rect.zero;
                return false;
            }

            Rect targetPortraitRect = GetTargetPortraitRect(inRect);
            anchorRect = new Rect(
                targetPortraitRect.xMax + ActionFeedbackHorizontalOffset,
                targetPortraitRect.y + ActionFeedbackVerticalInset,
                ActionFeedbackWidth,
                PortraitHeight - ActionFeedbackVerticalInset * 2f);
            return true;
        }

        private void DrawActionFeedbackEntries(Rect anchorRect)
        {
            float currentY = anchorRect.yMin;
            for (int index = actionFeedbackEntries.Count - 1; index >= 0; index--)
            {
                ActionFeedbackEntry entry = actionFeedbackEntries[index];
                float subtitleHeight = CalculateActionFeedbackPanelHeight(entry.Text);
                Rect subtitleRect = BuildActionFeedbackRect(anchorRect, currentY, subtitleHeight, entry);
                if (subtitleRect.yMax > anchorRect.yMax)
                {
                    break;
                }

                DrawActionFeedbackEntry(entry, subtitleRect);
                currentY += subtitleHeight + ActionFeedbackSpacing + ActionFeedbackStackRunway;
            }
        }

        private Rect BuildActionFeedbackRect(Rect anchorRect, float baseY, float height, ActionFeedbackEntry entry)
        {
            float riseOffset = GetActionFeedbackRiseOffset(entry);
            return new Rect(anchorRect.x, baseY - riseOffset, anchorRect.width, height);
        }

        private float CalculateActionFeedbackPanelHeight(string text)
        {
            float textWidth = GetActionFeedbackTextWidth();
            float textHeight = Text.CalcHeight(text ?? string.Empty, textWidth);
            return Mathf.Max(ActionFeedbackMinHeight, textHeight + ActionFeedbackVerticalPadding * 2f);
        }

        private float GetActionFeedbackTextWidth()
        {
            return ActionFeedbackWidth - ActionFeedbackHorizontalPadding * 2f - ActionFeedbackAccentWidth - 8f;
        }

        private void DrawActionFeedbackEntry(ActionFeedbackEntry entry, Rect subtitleRect)
        {
            float alpha = GetActionFeedbackAlpha(entry) * GetActionFeedbackVisibility();
            if (alpha <= 0.01f)
            {
                return;
            }

            DrawActionFeedbackBackground(subtitleRect, alpha);
            DrawActionFeedbackAccent(entry, subtitleRect, alpha);
            DrawActionFeedbackText(entry, subtitleRect, alpha);
        }

        private void DrawActionFeedbackBackground(Rect subtitleRect, float alpha)
        {
            Rect shadowRect = new Rect(
                subtitleRect.x + ActionFeedbackShadowHorizontalOffset,
                subtitleRect.y + ActionFeedbackShadowVerticalOffset,
                subtitleRect.width,
                subtitleRect.height);
            DrawRoundedRect(shadowRect, new Color(0f, 0f, 0f, 0.12f * alpha), ActionFeedbackCornerRadius);
            DrawRoundedRect(subtitleRect, new Color(0.04f, 0.05f, 0.07f, 0.22f * alpha), ActionFeedbackCornerRadius);
        }

        private void DrawActionFeedbackAccent(ActionFeedbackEntry entry, Rect subtitleRect, float alpha)
        {
            float glowHeight = Mathf.Max(12f, subtitleRect.height - 12f);
            float glowY = subtitleRect.y + (subtitleRect.height - glowHeight) * 0.5f;
            Rect glowRect = new Rect(subtitleRect.x + 3f, glowY, ActionFeedbackAccentWidth + 6f, glowHeight);
            Color accentGlow = new Color(entry.Color.r, entry.Color.g, entry.Color.b, 0.14f * alpha);
            DrawRoundedRect(glowRect, accentGlow, ActionFeedbackCornerRadius);

            Rect accentRect = new Rect(subtitleRect.x + 8f, glowY + 2f, ActionFeedbackAccentWidth, glowHeight - 4f);
            GUI.color = new Color(entry.Color.r, entry.Color.g, entry.Color.b, 0.72f * alpha);
            GUI.DrawTexture(accentRect, BaseContent.WhiteTex);
            GUI.color = Color.white;
        }

        private void DrawActionFeedbackText(ActionFeedbackEntry entry, Rect subtitleRect, float alpha)
        {
            Rect textRect = GetActionFeedbackTextRect(subtitleRect);
            Rect shadowRect = new Rect(
                textRect.x + ActionFeedbackShadowOffset.x,
                textRect.y + ActionFeedbackShadowOffset.y,
                textRect.width,
                textRect.height);
            GUI.color = new Color(0f, 0f, 0f, 0.5f * alpha);
            Widgets.Label(shadowRect, entry.Text);
            GUI.color = GetActionFeedbackTextColor(entry.Color, alpha);
            Widgets.Label(textRect, entry.Text);
        }

        private Rect GetActionFeedbackTextRect(Rect subtitleRect)
        {
            float x = subtitleRect.x + ActionFeedbackHorizontalPadding + ActionFeedbackAccentWidth + 6f;
            float width = subtitleRect.width - (x - subtitleRect.x) - ActionFeedbackHorizontalPadding;
            return new Rect(x, subtitleRect.y + ActionFeedbackVerticalPadding, width, subtitleRect.height - ActionFeedbackVerticalPadding * 2f);
        }

        private Color GetActionFeedbackTextColor(Color sourceColor, float alpha)
        {
            Color blendedColor = Color.Lerp(sourceColor, Color.white, 0.42f);
            blendedColor.a = 0.96f * alpha;
            return blendedColor;
        }

        private float GetActionFeedbackRiseOffset(ActionFeedbackEntry entry)
        {
            float age = Time.realtimeSinceStartup - entry.CreatedAt;
            float sustainDuration = Mathf.Max(0.01f, entry.Duration - ActionFeedbackFadeOutDuration);
            float sustainProgress = Mathf.Clamp01(age / sustainDuration);
            float baseRise = Mathf.SmoothStep(0f, ActionFeedbackBaseRiseDistance, sustainProgress);
            if (age <= sustainDuration)
            {
                return baseRise;
            }

            float fadeProgress = Mathf.Clamp01((age - sustainDuration) / ActionFeedbackFadeOutDuration);
            return baseRise + Mathf.SmoothStep(0f, ActionFeedbackFadeRiseDistance, fadeProgress);
        }

        private float GetActionFeedbackAlpha(ActionFeedbackEntry entry)
        {
            float age = Time.realtimeSinceStartup - entry.CreatedAt;
            float fadeStart = entry.Duration - ActionFeedbackFadeOutDuration;
            if (age <= fadeStart)
            {
                return 1f;
            }

            return Mathf.Clamp01((entry.Duration - age) / ActionFeedbackFadeOutDuration);
        }

        private float GetActionFeedbackVisibility()
        {
            return Mathf.Clamp01(globalFadeAlpha * targetFadeAlpha);
        }

        private void RemoveExpiredActionFeedback()
        {
            float now = Time.realtimeSinceStartup;
            actionFeedbackEntries.RemoveAll(entry => now - entry.CreatedAt > entry.Duration);
        }

        private void DrawRoundedRect(Rect rect, Color color, float radius)
        {
            GUI.color = color;
            float cornerRadius = Mathf.Min(radius, rect.width / 2f, rect.height / 2f);
            GUI.DrawTexture(new Rect(rect.x + cornerRadius, rect.y, rect.width - cornerRadius * 2f, rect.height), BaseContent.WhiteTex);
            GUI.DrawTexture(new Rect(rect.x, rect.y + cornerRadius, rect.width, rect.height - cornerRadius * 2f), BaseContent.WhiteTex);
            GUI.DrawTextureWithTexCoords(new Rect(rect.x, rect.y, cornerRadius, cornerRadius), SubtitleCornerTexture, new Rect(0f, 0.5f, 0.5f, 0.5f));
            GUI.DrawTextureWithTexCoords(new Rect(rect.xMax - cornerRadius, rect.y, cornerRadius, cornerRadius), SubtitleCornerTexture, new Rect(0.5f, 0.5f, 0.5f, 0.5f));
            GUI.DrawTextureWithTexCoords(new Rect(rect.x, rect.yMax - cornerRadius, cornerRadius, cornerRadius), SubtitleCornerTexture, new Rect(0f, 0f, 0.5f, 0.5f));
            GUI.DrawTextureWithTexCoords(new Rect(rect.xMax - cornerRadius, rect.yMax - cornerRadius, cornerRadius, cornerRadius), SubtitleCornerTexture, new Rect(0.5f, 0f, 0.5f, 0.5f));
            GUI.color = Color.white;
        }

        private static Texture2D CreateSubtitleCornerTexture()
        {
            const int radius = 32;
            int size = radius * 2;
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2(radius, radius);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float alpha = Mathf.Clamp01(radius - distance + 0.5f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
