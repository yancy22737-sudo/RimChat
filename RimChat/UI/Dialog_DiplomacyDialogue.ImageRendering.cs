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
        private static readonly Dictionary<string, Texture2D> InlineImageTextureCache =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        private void DrawImageMessageBubble(DialogueMessageData msg, Rect rect)
        {
            Color bubbleColor = msg.isPlayer ? PlayerBubbleColor : AIBubbleColor;
            Color textColor = msg.isPlayer ? new Color(0.1f, 0.1f, 0.1f) : new Color(0.95f, 0.95f, 0.97f);
            Color senderColor = msg.isPlayer ? new Color(0.2f, 0.3f, 0.15f) : new Color(0.75f, 0.8f, 0.9f);

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
            Widgets.Label(new Rect(contentX, contentY, contentWidth * 0.7f, headerHeight), msg.sender ?? string.Empty);

            string timeStr = GetTimestampString(msg);
            float timeWidth = Text.CalcSize(timeStr).x + 5f;
            Rect timeRect = new Rect(rect.xMax - timeWidth - padding, contentY, timeWidth, headerHeight);
            GUI.color = new Color(senderColor.r, senderColor.g, senderColor.b, 0.65f);
            Widgets.Label(timeRect, timeStr);
            contentY += headerHeight + 2f;

            float imageHeight = ResolveInlineImageHeight(msg.imageLocalPath, contentWidth);
            Rect imageRect = new Rect(contentX, contentY, contentWidth, imageHeight);
            DrawInlineImageContent(msg.imageLocalPath, imageRect);
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

        private float CalculateImageMessageHeight(DialogueMessageData msg, float width)
        {
            float contentWidth = width - 32f;
            float imageHeight = ResolveInlineImageHeight(msg?.imageLocalPath, contentWidth);
            string caption = GetDisplayText(msg);
            float captionHeight = string.IsNullOrWhiteSpace(caption)
                ? 0f
                : Text.CalcHeight(caption, contentWidth);
            float bodyHeight = 48f + imageHeight + 8f + captionHeight + 10f;
            return Mathf.Max(170f, bodyHeight);
        }

        private void DrawInlineImageContent(string imageLocalPath, Rect imageRect)
        {
            if (TryGetInlineImageTexture(imageLocalPath, out Texture2D texture))
            {
                GUI.DrawTexture(imageRect, texture, ScaleMode.ScaleToFit, true);
                Widgets.DrawBox(imageRect);
                return;
            }

            DrawRoundedRect(imageRect, new Color(0.08f, 0.08f, 0.1f, 0.85f), 8f);
            GUI.color = new Color(0.82f, 0.84f, 0.88f, 0.92f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(imageRect, "RimChat_SendImageMissingPreview".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
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
