using System;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: Verse Scribe serialization.
    /// Responsibility: persistent album index item for manually saved diplomacy/selfie images.
    /// </summary>
    public sealed class AlbumImageEntry : IExposable
    {
        public const string SourceUnknown = "unknown";
        public const string SourceChat = "chat";
        public const string SourceSelfie = "selfie";

        public string Id = string.Empty;
        public int SavedTick;
        public string SourcePath = string.Empty;
        public string AlbumPath = string.Empty;
        public string Caption = string.Empty;
        public string FactionId = string.Empty;
        public string NegotiatorId = string.Empty;
        public string Size = string.Empty;
        public string SourceType = SourceUnknown;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id", string.Empty);
            Scribe_Values.Look(ref SavedTick, "savedTick", 0);
            Scribe_Values.Look(ref SourcePath, "sourcePath", string.Empty);
            Scribe_Values.Look(ref AlbumPath, "albumPath", string.Empty);
            Scribe_Values.Look(ref Caption, "caption", string.Empty);
            Scribe_Values.Look(ref FactionId, "factionId", string.Empty);
            Scribe_Values.Look(ref NegotiatorId, "negotiatorId", string.Empty);
            Scribe_Values.Look(ref Size, "size", string.Empty);
            Scribe_Values.Look(ref SourceType, "sourceType", SourceUnknown);

            if (string.IsNullOrWhiteSpace(Id))
            {
                Id = Guid.NewGuid().ToString("N");
            }

            SourceType = NormalizeSourceType(SourceType);
        }

        public static string NormalizeSourceType(string sourceType)
        {
            if (string.IsNullOrWhiteSpace(sourceType))
            {
                return SourceUnknown;
            }

            string normalized = sourceType.Trim().ToLowerInvariant();
            if (normalized == SourceChat || normalized == SourceSelfie)
            {
                return normalized;
            }

            return SourceUnknown;
        }
    }
}
