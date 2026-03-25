using System;
using System.Collections.Generic;
using System.IO;
using RimChat.Memory;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>/// Dependencies: dialogue message model image fields and Unity texture loading APIs.
 /// Responsibility: render inline diplomacy image cards and compute stable bubble layout heights.
 ///</summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private const int InlineImageCacheSoftLimit = 48;
        private const float OutboundPrisonerThumbMinSize = 140f;
        private const float OutboundPrisonerThumbMaxSize = 190f;
        private const float OutboundPrisonerCardPadding = 10f;
        private const float OutboundPrisonerHeaderHeight = 16f;
        private const float OutboundPrisonerHeaderTopPadding = 8f;
        private const float OutboundPrisonerHeaderGap = 2f;
        private const float OutboundPrisonerImageTextGap = 8f;
        private const float OutboundPrisonerBottomPadding = 8f;
        private const float OutboundPrisonerMinBubbleHeight = 110f;
        private static readonly string[] OutboundPrisonerFieldOrderZh =
            { "姓名：", "年龄：", "健康：", "意识：", "所属派系：", "证词：" };
        private static readonly string[] OutboundPrisonerFieldOrderEn =
            { "Name:", "Age:", "Health:", "Consciousness:", "Source faction:", "Quote:" };
        private static readonly HashSet<string> OutboundPrisonerCaptionWarningKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Texture2D> InlineImageTextureCache =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        private void DrawImageMessageBubble(DialogueMessageData msg, Rect rect)
        {
            if (IsOutboundPrisonerInfoMessage(msg))
            {
                DrawOutboundPrisonerInfoBubble(msg, rect);
                return;
            }

            bool playerVisual = IsPlayerVisualMessage(msg);
            Color bubbleColor = playerVisual ? PlayerBubbleColor : AIBubbleColor;
            Color textColor = playerVisual ? new Color(0.1f, 0.1f, 0.1f) : new Color(0.95f, 0.95f, 0.97f);
            Color senderColor = playerVisual ? new Color(0.2f, 0.3f, 0.15f) : new Color(0.75f, 0.8f, 0.9f);

            Rect shadowRect = new Rect(rect.x + 1f, rect.y + 3f, rect.width, rect.height);
            DrawRoundedRect(shadowRect, new Color(0f, 0f, 0f, 0.12f), BUBBLE_CORNER_RADIUS);
            DrawRoundedRect(rect, bubbleColor, BUBBLE_CORNER_RADIUS);

            float padding = 16f;
            float headerHeight = 18f;
            float contentX = rect.x + padding;
            float contentY = rect.y + 12f;
            float contentWidth = rect.width - padding * 2f;

            Text.Font = GameFont.Tiny;
            GUI.color = senderColor;
            Widgets.Label(new Rect(contentX, contentY, contentWidth * 0.7f, headerHeight), GetDisplaySenderName(msg));

            string timeStr = GetTimestampString(msg);
            float timeWidth = Text.CalcSize(timeStr).x + 5f;
            Rect timeRect = new Rect(rect.xMax - timeWidth - padding, contentY, timeWidth, headerHeight);
            GUI.color = new Color(senderColor.r, senderColor.g, senderColor.b, 0.65f);
            Widgets.Label(timeRect, timeStr);
            contentY += headerHeight + 2f;

            float imageHeight = ResolveInlineImageHeight(msg.imageLocalPath, contentWidth);
            Rect imageRect = new Rect(contentX, contentY, contentWidth, imageHeight);
            Rect hitRect = DrawInlineImageContent(msg.imageLocalPath, imageRect);
            TryHandleImageContextMenu(msg, hitRect);
            contentY += imageHeight + 8f;

            string caption = GetDisplayText(msg);
            if (!string.IsNullOrWhiteSpace(caption))
            {
                Text.Font = GameFont.Small;
                GUI.color = textColor;
                Widgets.Label(new Rect(contentX, contentY, contentWidth, rect.yMax - contentY - 10f), caption);
            }

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawOutboundPrisonerInfoBubble(DialogueMessageData msg, Rect rect)
        {
            Rect shadowRect = new Rect(rect.x + 1f, rect.y + 3f, rect.width, rect.height);
            DrawRoundedRect(shadowRect, new Color(0f, 0f, 0f, 0.12f), BUBBLE_CORNER_RADIUS);
            DrawRoundedRect(rect, PlayerBubbleColor, BUBBLE_CORNER_RADIUS);

            string displayText = GetOutboundPrisonerProofCaption(msg);
            float contentX = rect.x + OutboundPrisonerCardPadding;
            float contentY = rect.y + OutboundPrisonerHeaderTopPadding;
            float contentWidth = rect.width - OutboundPrisonerCardPadding * 2f;

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.2f, 0.3f, 0.15f);
            Widgets.Label(
                new Rect(contentX, contentY, contentWidth * 0.7f, OutboundPrisonerHeaderHeight),
                GetDisplaySenderName(msg));

            string timeStr = GetTimestampString(msg);
            float timeWidth = Text.CalcSize(timeStr).x + 5f;
            Rect timeRect = new Rect(
                rect.xMax - timeWidth - OutboundPrisonerCardPadding,
                contentY,
                timeWidth,
                OutboundPrisonerHeaderHeight);
            GUI.color = new Color(0.2f, 0.3f, 0.15f, 0.7f);
            Widgets.Label(timeRect, timeStr);

            float cardTop = contentY + OutboundPrisonerHeaderHeight + OutboundPrisonerHeaderGap;
            float thumbSize = ResolveOutboundPrisonerThumbSize(contentWidth);
            Rect imageRect = new Rect(contentX, cardTop, thumbSize, thumbSize);
            GUI.color = Color.white;
            Rect hitRect = DrawInlineImageContentFill(msg.imageLocalPath, imageRect, false);
            TryHandleImageContextMenu(msg, hitRect);

            float textX = imageRect.xMax + OutboundPrisonerImageTextGap;
            float textWidth = ResolveOutboundPrisonerCaptionWidth(contentWidth, thumbSize);
            float textHeight = MeasureOutboundPrisonerCaptionHeight(displayText, textWidth);
            Rect captionRect = new Rect(textX, cardTop, textWidth, textHeight);
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.1f, 0.1f, 0.1f);
            Widgets.Label(captionRect, displayText);

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void TryHandleImageContextMenu(DialogueMessageData msg, Rect imageRect)
        {
            Event current = Event.current;
            if (current == null || msg == null || string.IsNullOrWhiteSpace(msg.imageLocalPath))
            {
                return;
            }

            bool rightClick = current.type == EventType.ContextClick ||
                              (current.type == EventType.MouseDown && current.button == 1);
            if (!rightClick || !Mouse.IsOver(imageRect))
            {
                return;
            }

            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("RimChat_AlbumSaveAction".Translate(), () => SaveMessageImageToAlbum(msg))
            };
            Find.WindowStack.Add(new FloatMenu(options));
            current.Use();
        }

        private float CalculateImageMessageHeight(DialogueMessageData msg, float width)
        {
            if (IsOutboundPrisonerInfoMessage(msg))
            {
                float proofContentWidth = Mathf.Max(1f, width - OutboundPrisonerCardPadding * 2f);
                string displayText = GetOutboundPrisonerProofCaption(msg);
                float proofBodyHeight = CalculateOutboundPrisonerBodyHeight(proofContentWidth, displayText);
                float totalHeight = OutboundPrisonerHeaderTopPadding
                    + OutboundPrisonerHeaderHeight
                    + OutboundPrisonerHeaderGap
                    + proofBodyHeight
                    + OutboundPrisonerBottomPadding;
                return Mathf.Max(OutboundPrisonerMinBubbleHeight, totalHeight);
            }

            float contentWidth = width - 32f;
            float imageHeight = ResolveInlineImageHeight(msg?.imageLocalPath, contentWidth);
            string caption = GetDisplayText(msg);
            float captionHeight = string.IsNullOrWhiteSpace(caption)
                ? 0f
                : Text.CalcHeight(caption, contentWidth);
            float bodyHeight = 48f + imageHeight + 8f + captionHeight + 10f;
            return Mathf.Max(170f, bodyHeight);
        }

        private Rect DrawInlineImageContent(string imageLocalPath, Rect imageRect, bool drawBorder = true)
        {
            if (TryGetInlineImageTexture(imageLocalPath, out Texture2D texture))
            {
                Rect drawRect = GetAspectFitRect(imageRect, texture);
                GUI.DrawTexture(drawRect, texture, ScaleMode.ScaleToFit, true);
                if (drawBorder)
                {
                    Widgets.DrawBox(imageRect);
                }

                return drawRect;
            }

            DrawRoundedRect(imageRect, new Color(0.08f, 0.08f, 0.1f, 0.85f), 8f);
            GUI.color = new Color(0.82f, 0.84f, 0.88f, 0.92f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(imageRect, "RimChat_SendImageMissingPreview".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            return imageRect;
        }

        private Rect DrawInlineImageContentFill(string imageLocalPath, Rect imageRect, bool drawBorder = true)
        {
            if (TryGetInlineImageTexture(imageLocalPath, out Texture2D texture))
            {
                GUI.DrawTexture(imageRect, texture, ScaleMode.ScaleAndCrop, true);
                if (drawBorder)
                {
                    Widgets.DrawBox(imageRect);
                }

                return imageRect;
            }

            return DrawInlineImageContent(imageLocalPath, imageRect, drawBorder);
        }

        private static float ResolveOutboundPrisonerThumbSize(float contentWidth)
        {
            return Mathf.Clamp(contentWidth * 0.30f, OutboundPrisonerThumbMinSize, OutboundPrisonerThumbMaxSize);
        }

        private static float ResolveOutboundPrisonerCaptionWidth(float contentWidth, float thumbSize)
        {
            return Mathf.Max(110f, contentWidth - thumbSize - OutboundPrisonerImageTextGap);
        }

        private static float CalculateOutboundPrisonerBodyHeight(float contentWidth, string caption)
        {
            float thumbSize = ResolveOutboundPrisonerThumbSize(contentWidth);
            float textWidth = ResolveOutboundPrisonerCaptionWidth(contentWidth, thumbSize);
            float textHeight = MeasureOutboundPrisonerCaptionHeight(caption, textWidth);
            return Mathf.Max(thumbSize, textHeight);
        }

        private static float MeasureOutboundPrisonerCaptionHeight(string caption, float textWidth)
        {
            if (string.IsNullOrWhiteSpace(caption))
            {
                return 0f;
            }

            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Small;
            float height = Text.CalcHeight(caption, Mathf.Max(1f, textWidth));
            Text.Font = previousFont;
            return Mathf.Max(18f, height);
        }

        private string GetOutboundPrisonerProofCaption(DialogueMessageData msg)
        {
            string normalized = NormalizeOutboundPrisonerProofCaption(GetDisplayText(msg));
            ValidateOutboundPrisonerProofCaption(normalized, msg);
            return normalized;
        }

        private static string NormalizeOutboundPrisonerProofCaption(string caption)
        {
            string normalized = caption ?? string.Empty;
            normalized = normalized.Replace("\\r\\n", "\n")
                .Replace("\\n", "\n")
                .Replace("\\r", "\n")
                .Replace("\\t", " ")
                .Replace("\\\"", "\"")
                .Replace("\\\\", string.Empty)
                .Replace("\r\n", "\n");
            if (normalized.IndexOf('\\') >= 0)
            {
                normalized = normalized.Replace("\\", string.Empty);
            }

            return normalized.Trim();
        }

        private static void ValidateOutboundPrisonerProofCaption(string caption, DialogueMessageData msg)
        {
            if (string.IsNullOrWhiteSpace(caption))
            {
                WarnOutboundPrisonerCaptionOnce(msg, "empty", "[RimChat][UI_ASSERT] ransom proof caption is empty.");
                return;
            }

            if (caption.IndexOf("行走", StringComparison.Ordinal) >= 0)
            {
                WarnOutboundPrisonerCaptionOnce(
                    msg,
                    "walk_field",
                    "[RimChat][UI_ASSERT] ransom proof caption still contains removed walk field.");
            }

            if (caption.IndexOf('\\') >= 0)
            {
                WarnOutboundPrisonerCaptionOnce(
                    msg,
                    "backslash",
                    "[RimChat][UI_ASSERT] ransom proof caption still contains a backslash after normalization.");
            }

            bool hasKnownOrder = HasOrderedFields(caption, OutboundPrisonerFieldOrderZh)
                || HasOrderedFields(caption, OutboundPrisonerFieldOrderEn);
            if (!hasKnownOrder)
            {
                WarnOutboundPrisonerCaptionOnce(
                    msg,
                    "field_order",
                    "[RimChat][UI_ASSERT] ransom proof field order is unexpected.");
            }
        }

        private static bool HasOrderedFields(string text, string[] fieldOrder)
        {
            int cursor = -1;
            for (int i = 0; i < fieldOrder.Length; i++)
            {
                int next = text.IndexOf(fieldOrder[i], StringComparison.Ordinal);
                if (next <= cursor)
                {
                    return false;
                }

                cursor = next;
            }

            return true;
        }

        private static void WarnOutboundPrisonerCaptionOnce(DialogueMessageData msg, string suffix, string warning)
        {
            int tick = msg?.GetGameTick() ?? -1;
            string key = tick + ":" + suffix;
            if (OutboundPrisonerCaptionWarningKeys.Add(key))
            {
                Log.Warning(warning);
            }
        }

        private static Rect GetAspectFitRect(Rect container, Texture2D texture)
        {
            if (texture == null || texture.width <= 0 || texture.height <= 0 || container.width <= 0f || container.height <= 0f)
            {
                return container;
            }

            float scale = Mathf.Min(container.width / texture.width, container.height / texture.height);
            float width = texture.width * scale;
            float height = texture.height * scale;
            float x = container.x + (container.width - width) * 0.5f;
            float y = container.y + (container.height - height) * 0.5f;
            return new Rect(x, y, width, height);
        }

        private float ResolveInlineImageHeight(string imageLocalPath, float width)
        {
            const float fallbackHeight = 180f;
            if (!TryGetInlineImageTexture(imageLocalPath, out Texture2D texture) || texture == null || texture.width <= 0 || texture.height <= 0)
            {
                return fallbackHeight;
            }

            float ratio = texture.height / (float)Math.Max(1, texture.width);
            float resolved = width * ratio;
            return Mathf.Clamp(resolved, 120f, 340f);
        }

        private static bool TryGetInlineImageTexture(string path, out Texture2D texture)
        {
            texture = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (InlineImageTextureCache.TryGetValue(path, out Texture2D cached) && cached != null)
            {
                texture = cached;
                return true;
            }

            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes == null || bytes.Length == 0)
                {
                    return false;
                }

                Texture2D loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(loaded, bytes))
                {
                    UnityEngine.Object.Destroy(loaded);
                    return false;
                }

                loaded.wrapMode = TextureWrapMode.Clamp;
                loaded.filterMode = FilterMode.Bilinear;
                if (InlineImageTextureCache.Count >= InlineImageCacheSoftLimit)
                {
                    ClearInlineImageTextureCache();
                }

                InlineImageTextureCache[path] = loaded;
                texture = loaded;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ClearInlineImageTextureCache()
        {
            foreach (KeyValuePair<string, Texture2D> pair in InlineImageTextureCache)
            {
                if (pair.Value != null)
                {
                    UnityEngine.Object.Destroy(pair.Value);
                }
            }

            InlineImageTextureCache.Clear();
        }
    }
}
