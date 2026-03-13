using System;
using System.Collections.Generic;
using System.IO;
using RimChat.DiplomacySystem;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: diplomacy manager album index, image file IO, and Unity texture rendering.
    /// Responsibility: render album entries as thumbnail cards and provide per-item context actions.
    /// </summary>
    public sealed class Dialog_DiplomacyAlbum : Window
    {
        private const int ThumbnailCacheSoftLimit = 72;
        private static readonly Dictionary<string, Texture2D> ThumbnailCache =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        private Vector2 scrollPos = Vector2.zero;
        private List<AlbumImageEntry> items = new List<AlbumImageEntry>();

        public override Vector2 InitialSize => new Vector2(960f, 620f);

        public Dialog_DiplomacyAlbum()
        {
            draggable = true;
            doCloseX = true;
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            RefreshItems();
        }

        public override void PostClose()
        {
            base.PostClose();
            ClearThumbnailCache();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "RimChat_AlbumWindowTitle".Translate());

            Rect listRect = new Rect(inRect.x, inRect.y + 34f, inRect.width, inRect.height - 74f);
            DrawList(listRect);

            Rect refreshRect = new Rect(inRect.x, inRect.yMax - 32f, 120f, 28f);
            if (Widgets.ButtonText(refreshRect, "RimChat_AlbumRefresh".Translate()))
            {
                RefreshItems();
            }

            Rect closeRect = new Rect(inRect.xMax - 120f, inRect.yMax - 32f, 120f, 28f);
            if (Widgets.ButtonText(closeRect, "RimChat_CloseButton".Translate()))
            {
                Close();
            }
        }

        private void DrawList(Rect rect)
        {
            if (items.Count == 0)
            {
                Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.12f));
                GUI.color = new Color(0.82f, 0.82f, 0.86f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, "RimChat_AlbumEmpty".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            const float gap = 10f;
            const float cardHeight = 214f;
            float cardWidth = 220f;
            int columns = Mathf.Max(1, Mathf.FloorToInt((rect.width + gap) / (cardWidth + gap)));
            cardWidth = Mathf.Max(180f, (rect.width - (columns - 1) * gap - 16f) / columns);
            int rows = Mathf.CeilToInt(items.Count / (float)columns);

            float contentHeight = rows * (cardHeight + gap);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(rect.height, contentHeight));
            Widgets.BeginScrollView(rect, ref scrollPos, viewRect);

            for (int i = 0; i < items.Count; i++)
            {
                int row = i / columns;
                int col = i % columns;
                float x = col * (cardWidth + gap);
                float y = row * (cardHeight + gap);
                DrawCard(new Rect(x, y, cardWidth, cardHeight), items[i]);
            }

            Widgets.EndScrollView();
        }

        private void DrawCard(Rect rect, AlbumImageEntry item)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.15f, 0.96f));
            GUI.color = new Color(0.27f, 0.27f, 0.32f, 0.98f);
            Widgets.DrawBox(rect);
            GUI.color = Color.white;

            Rect thumbRect = new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, 122f);
            DrawThumbnail(thumbRect, item);
            DrawBadges(new Rect(rect.x + 8f, thumbRect.yMax + 4f, rect.width - 16f, 20f), item);

            string title = string.IsNullOrWhiteSpace(item?.Caption)
                ? Path.GetFileName(item?.AlbumPath ?? string.Empty)
                : item.Caption;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x + 8f, thumbRect.yMax + 26f, rect.width - 16f, 24f), title);

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.78f, 0.78f, 0.84f);
            string meta = BuildRowMeta(item);
            Widgets.Label(new Rect(rect.x + 8f, thumbRect.yMax + 48f, rect.width - 16f, 16f), meta);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Rect openRect = new Rect(rect.x + 8f, rect.yMax - 28f, rect.width - 16f, 22f);
            if (Widgets.ButtonText(openRect, "RimChat_AlbumOpenDir".Translate()))
            {
                TryOpenDirectory(item);
            }

            TryHandleCardContextMenu(rect, item);
        }

        private void DrawThumbnail(Rect rect, AlbumImageEntry item)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.08f, 0.1f, 0.95f));
            if (TryGetThumbnail(item?.AlbumPath, out Texture2D texture) && texture != null)
            {
                Rect drawRect = GetAspectFitRect(rect, texture);
                GUI.DrawTexture(drawRect, texture, ScaleMode.ScaleToFit, true);
                Widgets.DrawBox(rect);
                return;
            }

            GUI.color = new Color(0.82f, 0.84f, 0.9f, 0.9f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, "RimChat_SendImageMissingPreview".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawBadges(Rect rect, AlbumImageEntry item)
        {
            string source = ResolveSourceLabel(item);
            string size = string.IsNullOrWhiteSpace(item?.Size)
                ? "RimChat_AlbumBadgeSizeUnknown".Translate().ToString()
                : item.Size;
            DrawBadge(new Rect(rect.x, rect.y, rect.width * 0.5f - 4f, rect.height), source, new Color(0.22f, 0.36f, 0.62f, 0.9f));
            DrawBadge(new Rect(rect.x + rect.width * 0.5f + 4f, rect.y, rect.width * 0.5f - 4f, rect.height), size, new Color(0.3f, 0.3f, 0.34f, 0.9f));
        }

        private static void DrawBadge(Rect rect, string label, Color color)
        {
            Widgets.DrawBoxSolid(rect, color);
            GUI.color = new Color(0.1f, 0.1f, 0.12f, 0.8f);
            Widgets.DrawBox(rect);
            GUI.color = Color.white;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private string ResolveSourceLabel(AlbumImageEntry item)
        {
            string source = AlbumImageEntry.NormalizeSourceType(item?.SourceType);
            if (source == AlbumImageEntry.SourceSelfie)
            {
                return "RimChat_AlbumBadgeSourceSelfie".Translate().ToString();
            }

            if (source == AlbumImageEntry.SourceChat || source == AlbumImageEntry.SourceUnknown)
            {
                return "RimChat_AlbumBadgeSourceChat".Translate().ToString();
            }

            return "RimChat_AlbumBadgeSourceUnknown".Translate().ToString();
        }

        private static string BuildRowMeta(AlbumImageEntry item)
        {
            string file = Path.GetFileName(item?.AlbumPath ?? string.Empty);
            return "RimChat_AlbumRowMeta".Translate(item?.Size ?? "?", file).ToString();
        }

        private void TryHandleCardContextMenu(Rect rect, AlbumImageEntry item)
        {
            Event current = Event.current;
            if (current == null || current.type != EventType.ContextClick || !Mouse.IsOver(rect))
            {
                return;
            }

            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("RimChat_AlbumOpenDirAction".Translate(), () => TryOpenDirectory(item)),
                new FloatMenuOption("RimChat_AlbumCopyPathAction".Translate(), () => CopyAlbumPath(item))
            };
            Find.WindowStack.Add(new FloatMenu(options));
            current.Use();
        }

        private void CopyAlbumPath(AlbumImageEntry item)
        {
            if (string.IsNullOrWhiteSpace(item?.AlbumPath))
            {
                Messages.Message("RimChat_AlbumCopyPathFailed".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            GUIUtility.systemCopyBuffer = item.AlbumPath;
            Messages.Message("RimChat_AlbumCopyPathSuccess".Translate(), MessageTypeDefOf.PositiveEvent, false);
        }

        private void TryOpenDirectory(AlbumImageEntry item)
        {
            if (DiplomacyAlbumService.OpenImageDirectory(item, out string error))
            {
                return;
            }

            Messages.Message("RimChat_AlbumOpenDirFailed".Translate(error), MessageTypeDefOf.RejectInput, false);
        }

        private void RefreshItems()
        {
            GameComponent_DiplomacyManager manager = GameComponent_DiplomacyManager.Instance;
            manager?.PruneMissingAlbumFiles();
            items = manager?.GetAlbumEntries() ?? new List<AlbumImageEntry>();
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

        private static bool TryGetThumbnail(string path, out Texture2D texture)
        {
            texture = null;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            if (ThumbnailCache.TryGetValue(path, out Texture2D cached) && cached != null)
            {
                texture = cached;
                return true;
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
                if (ThumbnailCache.Count >= ThumbnailCacheSoftLimit)
                {
                    ClearThumbnailCache();
                }

                ThumbnailCache[path] = loaded;
                texture = loaded;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ClearThumbnailCache()
        {
            foreach (KeyValuePair<string, Texture2D> pair in ThumbnailCache)
            {
                if (pair.Value != null)
                {
                    UnityEngine.Object.Destroy(pair.Value);
                }
            }

            ThumbnailCache.Clear();
        }
    }
}
