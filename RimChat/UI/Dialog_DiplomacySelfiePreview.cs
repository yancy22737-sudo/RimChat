using System.IO;
using RimChat.DiplomacySystem;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: local image bytes and album save bridge.
    /// Responsibility: preview generated selfie and allow manual save-to-album.
    /// </summary>
    public sealed class Dialog_DiplomacySelfiePreview : Window
    {
        private readonly string imagePath;
        private readonly string caption;
        private readonly AlbumImageEntry metadata;
        private Texture2D texture;
        private string loadError = string.Empty;
        private bool saved;

        public override Vector2 InitialSize => new Vector2(760f, 620f);

        public Dialog_DiplomacySelfiePreview(string imagePath, string caption, AlbumImageEntry metadata)
        {
            this.imagePath = imagePath ?? string.Empty;
            this.caption = caption ?? string.Empty;
            this.metadata = metadata;
            doCloseX = true;
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            LoadTexture();
        }

        public override void PostClose()
        {
            base.PostClose();
            if (texture != null)
            {
                Object.Destroy(texture);
                texture = null;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "RimChat_SelfiePreviewTitle".Translate());

            Rect imageRect = new Rect(inRect.x, inRect.y + 34f, inRect.width, inRect.height - 118f);
            DrawImage(imageRect);

            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inRect.x, imageRect.yMax + 4f, inRect.width, 18f), caption);
            Text.Font = GameFont.Small;

            Rect saveRect = new Rect(inRect.x, inRect.yMax - 32f, 180f, 28f);
            GUI.enabled = !saved && texture != null;
            if (Widgets.ButtonText(saveRect, "RimChat_AlbumSaveAction".Translate()))
            {
                SaveToAlbum();
            }

            GUI.enabled = true;

            Rect closeRect = new Rect(inRect.xMax - 120f, inRect.yMax - 32f, 120f, 28f);
            if (Widgets.ButtonText(closeRect, "RimChat_CloseButton".Translate()))
            {
                Close();
            }
        }

        private void DrawImage(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.08f, 0.1f, 0.96f));
            if (texture != null)
            {
                GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
                Widgets.DrawBox(rect);
                return;
            }

            GUI.color = new Color(0.82f, 0.82f, 0.88f);
            Text.Anchor = TextAnchor.MiddleCenter;
            string fallback = "RimChat_SendImageMissingPreview".Translate().ToString();
            Widgets.Label(rect, string.IsNullOrWhiteSpace(loadError) ? fallback : loadError);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void SaveToAlbum()
        {
            if (saved)
            {
                return;
            }

            if (!Dialog_DiplomacyDialogue.SaveImageToAlbum(imagePath, metadata, out string error))
            {
                Messages.Message("RimChat_AlbumSaveFailed".Translate(error), MessageTypeDefOf.RejectInput, false);
                return;
            }

            saved = true;
            Messages.Message("RimChat_AlbumSaveSuccess".Translate(), MessageTypeDefOf.PositiveEvent, false);
        }

        private void LoadTexture()
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                loadError = "RimChat_SelfiePreviewMissing".Translate();
                return;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(imagePath);
                if (bytes == null || bytes.Length == 0)
                {
                    loadError = "RimChat_SelfiePreviewMissing".Translate();
                    return;
                }

                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, bytes))
                {
                    loadError = "RimChat_SelfiePreviewMissing".Translate();
                    Object.Destroy(texture);
                    texture = null;
                    return;
                }
            }
            catch
            {
                loadError = "RimChat_SelfiePreviewMissing".Translate();
            }
        }
    }
}
