using RimChat.DiplomacySystem;
using RimChat.Memory;
using RimWorld;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: diplomacy manager album APIs and image album service.
    /// Responsibility: bridge diplomacy window actions (album open/selfie open/save image to album).
    /// </summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private void OpenAlbumWindow()
        {
            Find.WindowStack.Add(new Dialog_DiplomacyAlbum());
        }

        private void OpenSelfieWindow()
        {
            if (negotiator == null)
            {
                Messages.Message("RimChat_SelfieUnavailableNoNegotiator".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Find.WindowStack.Add(new Dialog_DiplomacySelfieConfig(faction, negotiator));
        }

        private void SaveMessageImageToAlbum(DialogueMessageData message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.imageLocalPath))
            {
                Messages.Message("RimChat_AlbumSaveFailed".Translate("invalid image"), MessageTypeDefOf.RejectInput, false);
                return;
            }

            AlbumImageEntry metadata = BuildAlbumMetadata(message.imageLocalPath, GetDisplayText(message));
            if (!DiplomacyAlbumService.SaveToAlbum(message.imageLocalPath, metadata, out AlbumImageEntry savedEntry, out string error))
            {
                Messages.Message("RimChat_AlbumSaveFailed".Translate(error), MessageTypeDefOf.RejectInput, false);
                return;
            }

            GameComponent_DiplomacyManager.Instance?.AddAlbumEntry(savedEntry);
            Messages.Message("RimChat_AlbumSaveSuccess".Translate(), MessageTypeDefOf.PositiveEvent, false);
        }

        internal static bool SaveImageToAlbum(string sourcePath, AlbumImageEntry metadata, out string error)
        {
            error = string.Empty;
            if (!DiplomacyAlbumService.SaveToAlbum(sourcePath, metadata, out AlbumImageEntry savedEntry, out error))
            {
                return false;
            }

            GameComponent_DiplomacyManager.Instance?.AddAlbumEntry(savedEntry);
            return true;
        }

        internal AlbumImageEntry BuildAlbumMetadata(string sourcePath, string caption)
        {
            string resolvedSize = string.Empty;
            if (TryGetInlineImageTexture(sourcePath, out UnityEngine.Texture2D texture) && texture != null)
            {
                resolvedSize = $"{texture.width}x{texture.height}";
            }

            return new AlbumImageEntry
            {
                SavedTick = Find.TickManager?.TicksGame ?? 0,
                SourcePath = sourcePath ?? string.Empty,
                Caption = caption ?? string.Empty,
                FactionId = faction?.Name ?? string.Empty,
                NegotiatorId = negotiator?.ThingID ?? string.Empty,
                Size = resolvedSize,
                SourceType = AlbumImageEntry.SourceChat
            };
        }
    }
}
