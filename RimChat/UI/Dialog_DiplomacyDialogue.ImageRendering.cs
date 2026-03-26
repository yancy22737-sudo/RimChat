using System;
using System.Collections.Generic;
using System.Globalization;
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
        private const float OutboundPrisonerCardPadding = 8f;
        private const float OutboundPrisonerHeaderHeight = 16f;
        private const float OutboundPrisonerHeaderTopPadding = 6f;
        private const float OutboundPrisonerHeaderGap = 1f;
        private const float OutboundPrisonerImageTextGap = 6f;
        private const float OutboundPrisonerBottomPadding = 6f;
        private const float OutboundPrisonerMinBubbleHeight = 110f;
        private const float OutboundPrisonerThumbnailZoomFactor = 1.75f;
        private static readonly Vector2 OutboundPrisonerThumbnailPivot = new Vector2(0.5f, 0.58f);
        private static readonly string[] OutboundPrisonerFieldOrderZh =
            { "姓名：", "年龄：", "健康：", "意识：", "所属派系：", "ID：", "证词：" };
        private static readonly string[] OutboundPrisonerFieldOrderEn =
            { "Name:", "Age:", "Health:", "Consciousness:", "Source faction:", "ID:", "Quote:" };
        private static readonly string[] LegacyOutboundPrisonerFieldOrderZh =
            { "姓名：", "年龄：", "健康：", "意识：", "所属派系：", "证词：" };
        private static readonly string[] LegacyOutboundPrisonerFieldOrderEn =
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
            Rect hitRect = DrawInlineImageContentFillZoomed(
                msg.imageLocalPath,
                imageRect,
                OutboundPrisonerThumbnailZoomFactor,
                OutboundPrisonerThumbnailPivot,
                false);
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

        private Rect DrawInlineImageContentFillZoomed(
            string imageLocalPath,
            Rect imageRect,
            float zoomFactor,
            Vector2 pivot,
            bool drawBorder = true)
        {
            if (!TryGetInlineImageTexture(imageLocalPath, out Texture2D texture))
            {
                return DrawInlineImageContent(imageLocalPath, imageRect, drawBorder);
            }

            float safeZoom = Mathf.Max(1f, zoomFactor);
            float viewSize = 1f / safeZoom;
            float pivotX = Mathf.Clamp01(pivot.x);
            float pivotY = Mathf.Clamp01(pivot.y);
            float uvX = Mathf.Clamp(pivotX - viewSize * 0.5f, 0f, 1f - viewSize);
            float uvY = Mathf.Clamp(pivotY - viewSize * 0.5f, 0f, 1f - viewSize);
            Rect uvRect = new Rect(uvX, uvY, viewSize, viewSize);
            GUI.DrawTextureWithTexCoords(imageRect, texture, uvRect, true);

            if (drawBorder)
            {
                Widgets.DrawBox(imageRect);
            }

            return imageRect;
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
                || HasOrderedFields(caption, OutboundPrisonerFieldOrderEn)
                || HasOrderedFields(caption, LegacyOutboundPrisonerFieldOrderZh)
                || HasOrderedFields(caption, LegacyOutboundPrisonerFieldOrderEn);
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

        private const float AirdropCardThumbSize = 40f;
        private const float AirdropCardPadding = 10f;
        private const float AirdropCardHeaderHeight = 14f;
        private const float AirdropCardTitleBandHeight = 24f;
        private const float AirdropCardFooterHeight = 22f;
        private const float AirdropCardRowGap = 6f;
        private const float AirdropCardMetricGap = 4f;
        private const float AirdropCardMetricHeight = 30f;
        private const float AirdropCardMinRowHeight = 96f;

        private float CalculateAirdropTradeCardBubbleHeight(DialogueMessageData msg, float width)
        {
            float contentWidth = Mathf.Max(220f, width - AirdropCardPadding * 2f);
            float needHeight = CalculateAirdropItemCardHeight(contentWidth, msg?.airdropNeedLabel, msg?.airdropNeedDefName);
            float offerHeight = CalculateAirdropItemCardHeight(contentWidth, msg?.airdropOfferLabel, msg?.airdropOfferDefName);
            float totalHeight = 14f
                + AirdropCardHeaderHeight
                + 4f
                + AirdropCardTitleBandHeight
                + 8f
                + needHeight
                + AirdropCardRowGap
                + offerHeight
                + 6f
                + AirdropCardFooterHeight
                + 8f;
            return Mathf.Max(258f, totalHeight);
        }

        private void DrawAirdropTradeCardBubble(DialogueMessageData msg, Rect rect)
        {
            bool playerVisual = IsPlayerVisualMessage(msg);
            Color bubbleColor = playerVisual ? PlayerBubbleColor : AIBubbleColor;
            Color senderColor = playerVisual ? new Color(0.12f, 0.16f, 0.10f, 0.95f) : new Color(0.16f, 0.19f, 0.23f, 0.95f);
            Color secondaryTextColor = playerVisual ? new Color(0.14f, 0.18f, 0.12f, 0.78f) : new Color(0.18f, 0.21f, 0.24f, 0.82f);
            Color dividerColor = new Color(0f, 0f, 0f, 0.18f);
            Color contentPanelColor = new Color(1f, 1f, 1f, 0.06f);
            Color contentPrimaryTextColor = new Color(0.10f, 0.12f, 0.11f, 0.98f);
            Color contentSecondaryTextColor = new Color(0.18f, 0.20f, 0.19f, 0.88f);
            Color metricLabelColor = new Color(0.20f, 0.22f, 0.21f, 0.84f);
            Color metricValueColor = new Color(0.09f, 0.11f, 0.10f, 0.98f);

            Rect shadowRect = new Rect(rect.x + 1f, rect.y + 3f, rect.width, rect.height);
            DrawRoundedRect(shadowRect, new Color(0f, 0f, 0f, 0.12f), BUBBLE_CORNER_RADIUS);
            DrawRoundedRect(rect, bubbleColor, BUBBLE_CORNER_RADIUS);

            float contentX = rect.x + AirdropCardPadding;
            float contentY = rect.y + 8f;
            float contentWidth = rect.width - AirdropCardPadding * 2f;

            Text.Font = GameFont.Tiny;
            GUI.color = senderColor;
            DrawSingleLineClippedLabel(new Rect(contentX, contentY, contentWidth * 0.7f, AirdropCardHeaderHeight), GetDisplaySenderName(msg));

            string timeStr = GetTimestampString(msg);
            float timeWidth = Text.CalcSize(timeStr).x + 5f;
            Rect timeRect = new Rect(rect.xMax - timeWidth - AirdropCardPadding, contentY, timeWidth, AirdropCardHeaderHeight);
            GUI.color = secondaryTextColor;
            DrawSingleLineClippedLabel(timeRect, timeStr);

            contentY += AirdropCardHeaderHeight + 4f;
            Widgets.DrawBoxSolid(new Rect(contentX, contentY, contentWidth, 1f), dividerColor);
            contentY += 4f;

            Text.Font = GameFont.Small;
            GUI.color = new Color(0.09f, 0.11f, 0.10f, 1f);
            DrawSingleLineClippedLabel(new Rect(contentX, contentY, contentWidth, AirdropCardTitleBandHeight), "RimChat_AirdropTradeCard_BubbleTitle".Translate());
            GUI.color = Color.white;

            contentY += AirdropCardTitleBandHeight + 4f;
            Widgets.DrawBoxSolid(new Rect(contentX, contentY, contentWidth, 1f), dividerColor);
            contentY += 3f;

            float needHeight = CalculateAirdropItemCardHeight(contentWidth, msg.airdropNeedLabel, msg.airdropNeedDefName);
            Rect needRect = new Rect(contentX, contentY, contentWidth, needHeight);
            DrawAirdropItemCard(
                needRect,
                msg.airdropNeedLabel,
                msg.airdropNeedDefName,
                msg.airdropRequestedCount,
                msg.airdropNeedUnitPrice,
                msg.airdropNeedReferenceTotalPrice,
                "RimChat_AirdropTradeCard_NeedItemCard".Translate().ToString(),
                true,
                contentPanelColor,
                dividerColor,
                contentPrimaryTextColor,
                contentSecondaryTextColor,
                metricLabelColor,
                metricValueColor);

            contentY += needHeight + AirdropCardRowGap;
            Widgets.DrawBoxSolid(new Rect(contentX, contentY - 1f, contentWidth, 1f), dividerColor);

            float offerHeight = CalculateAirdropItemCardHeight(contentWidth, msg.airdropOfferLabel, msg.airdropOfferDefName);
            Rect offerRect = new Rect(contentX, contentY + 2f, contentWidth, offerHeight);
            DrawAirdropItemCard(
                offerRect,
                msg.airdropOfferLabel,
                msg.airdropOfferDefName,
                msg.airdropOfferCount,
                msg.airdropOfferUnitPrice,
                msg.airdropOfferTotalPrice,
                "RimChat_AirdropTradeCard_OfferItemCard".Translate().ToString(),
                false,
                contentPanelColor,
                dividerColor,
                contentPrimaryTextColor,
                contentSecondaryTextColor,
                metricLabelColor,
                metricValueColor);

            contentY += offerHeight + 4f;
            Widgets.DrawBoxSolid(new Rect(contentX, contentY, contentWidth, 1f), dividerColor);
            contentY += 3f;
            Rect footerRect = new Rect(contentX, contentY, contentWidth, AirdropCardFooterHeight);
            Text.Font = GameFont.Tiny;
            GUI.color = senderColor;
            string footerText = "RimChat_AirdropTradeCard_ReferencePriceBubble".Translate(
                msg.airdropNeedReferenceTotalPrice.ToString("F1", CultureInfo.InvariantCulture)).ToString();
            DrawSingleLineClippedLabel(
                new Rect(footerRect.x, footerRect.y + 3f, footerRect.width, footerRect.height - 6f),
                footerText);

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private float CalculateAirdropItemCardHeight(float width, string label, string defName)
        {
            float iconPanelSize = AirdropCardThumbSize + 8f;
            float textWidth = Mathf.Max(124f, width - (AirdropCardPadding * 2f + iconPanelSize + 10f + 12f));
            string displayLabel = string.IsNullOrWhiteSpace(label) ? (defName ?? "?") : label;
            float labelHeight = MeasureWrappedTextHeight(displayLabel, textWidth, GameFont.Small, float.MaxValue);
            float defHeight = string.IsNullOrWhiteSpace(defName) ? 0f : 14f;
            float textBlockHeight = 12f + 4f + 1f + 4f + labelHeight + (defHeight > 0f ? 4f + defHeight : 0f) + 6f;
            float iconBlockHeight = iconPanelSize;
            float topBlockHeight = Mathf.Max(iconBlockHeight, textBlockHeight);
            float contentHeight = AirdropCardPadding + topBlockHeight + 8f + AirdropCardMetricHeight + 6f;
            return Mathf.Max(AirdropCardMinRowHeight, contentHeight);
        }

        private void DrawAirdropItemCard(
            Rect rect,
            string label,
            string defName,
            int count,
            float unitPrice,
            float totalPrice,
            string categoryLabel,
            bool emphasizeReference,
            Color contentPanelColor,
            Color dividerColor,
            Color primaryTextColor,
            Color secondaryTextColor,
            Color metricLabelColor,
            Color metricValueColor)
        {
            DrawRoundedRect(rect, contentPanelColor, 9f);
            GUI.color = new Color(0f, 0f, 0f, 0.24f);
            Widgets.DrawBox(rect);
            GUI.color = Color.white;
            float iconPanelSize = AirdropCardThumbSize + 8f;
            Rect iconPanelRect = new Rect(rect.x + 8f, rect.y + 8f, iconPanelSize, iconPanelSize);
            Rect iconRect = new Rect(iconPanelRect.x + 4f, iconPanelRect.y + 4f, AirdropCardThumbSize, AirdropCardThumbSize);
            DrawAirdropThingThumbnail(iconRect, defName);

            float textX = iconPanelRect.xMax + 10f;
            float textWidth = rect.width - (textX - rect.x) - 8f;
            Text.Font = GameFont.Tiny;
            GUI.color = secondaryTextColor;
            DrawSingleLineClippedLabel(new Rect(textX, rect.y + 8f, textWidth, 12f), categoryLabel);
            Widgets.DrawBoxSolid(new Rect(textX, rect.y + 22f, textWidth, 1f), dividerColor);

            Text.Font = GameFont.Small;
            GUI.color = primaryTextColor;
            string displayLabel = string.IsNullOrWhiteSpace(label) ? (defName ?? "?") : label;
            float labelY = rect.y + 27f;
            float labelHeight = MeasureWrappedTextHeight(displayLabel, textWidth, GameFont.Small, float.MaxValue);
            Widgets.Label(new Rect(textX, labelY, textWidth, labelHeight), displayLabel);

            float defHeight = string.IsNullOrWhiteSpace(defName) ? 0f : 14f;
            float textBlockHeight = 12f + 4f + 1f + 4f + labelHeight + (defHeight > 0f ? 4f + defHeight : 0f) + 6f;
            float topBlockHeight = Mathf.Max(iconPanelSize, textBlockHeight);
            float metricsY = rect.y + AirdropCardPadding + topBlockHeight + 8f;

            Text.Font = GameFont.Tiny;
            GUI.color = secondaryTextColor;
            if (!string.IsNullOrWhiteSpace(defName))
            {
                float defY = labelY + labelHeight + 4f;
                DrawSingleLineClippedLabel(new Rect(textX, defY, textWidth, 14f), defName);
            }

            float metricWidth = (textWidth - AirdropCardMetricGap * 2f) / 3f;
            DrawAirdropMetricCell(
                new Rect(textX, metricsY, metricWidth, AirdropCardMetricHeight),
                "RimChat_AirdropTradeCard_CountLabel".Translate().ToString(),
                count.ToString(CultureInfo.InvariantCulture),
                dividerColor,
                metricLabelColor,
                metricValueColor);
            DrawAirdropMetricCell(
                new Rect(textX + metricWidth + AirdropCardMetricGap, metricsY, metricWidth, AirdropCardMetricHeight),
                "RimChat_Price".Translate().ToString(),
                unitPrice.ToString("F1", CultureInfo.InvariantCulture),
                dividerColor,
                metricLabelColor,
                metricValueColor);
            DrawAirdropMetricCell(
                new Rect(textX + (metricWidth + AirdropCardMetricGap) * 2f, metricsY, metricWidth, AirdropCardMetricHeight),
                "RimChat_AirdropTradeCard_TotalPriceLabel".Translate().ToString(),
                totalPrice.ToString("F1", CultureInfo.InvariantCulture),
                dividerColor,
                metricLabelColor,
                metricValueColor);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawAirdropMetricCell(Rect rect, string label, string value, Color dividerColor, Color labelColor, Color valueColor)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 1f), dividerColor);
            Text.Font = GameFont.Tiny;
            GUI.color = labelColor;
            DrawSingleLineClippedLabel(new Rect(rect.x, rect.y + 3f, rect.width, 14f), label);
            GUI.color = valueColor;
            DrawSingleLineClippedLabel(new Rect(rect.x, rect.y + 16f, rect.width, 14f), value);
            GUI.color = Color.white;
        }

        private float MeasureWrappedTextHeight(string text, float width, GameFont font, float maxHeight)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0f;
            }

            GameFont previousFont = Text.Font;
            Text.Font = font;
            float height = Text.CalcHeight(text, Mathf.Max(1f, width));
            Text.Font = previousFont;
            return Mathf.Min(maxHeight, Mathf.Max(14f, height));
        }

        private void DrawAirdropThingThumbnail(Rect iconRect, string defName)
        {
            if (string.IsNullOrWhiteSpace(defName))
            {
                Widgets.DrawBoxSolid(iconRect, new Color(0.15f, 0.15f, 0.18f));
                GUI.color = new Color(0.5f, 0.55f, 0.6f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(iconRect, "?");
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (thingDef?.uiIcon != null)
            {
                GUI.color = thingDef.uiIconColor;
                GUI.DrawTexture(iconRect.ContractedBy(2f), thingDef.uiIcon, ScaleMode.ScaleToFit, true);
            }
            else
            {
                Widgets.DrawBoxSolid(iconRect, new Color(0.15f, 0.15f, 0.18f));
            }

            GUI.color = new Color(0.35f, 0.35f, 0.4f, 0.9f);
            Widgets.DrawBox(iconRect);
            GUI.color = Color.white;
        }
    }
}
