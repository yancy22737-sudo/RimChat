using Verse;

namespace RimChat.Core
{
    /// <summary>
    /// Dependencies: Verse translation service.
    /// Responsibility: centralize temporary image-generation availability policy and user-facing message text.
    /// </summary>
    internal static class ImageGenerationAvailability
    {
        internal const string InDevelopmentKey = "RimChat_ImageGenerationInDevelopment";

        internal static bool IsBlocked()
        {
            return true;
        }

        internal static string GetBlockedMessage()
        {
            return InDevelopmentKey.Translate().ToString();
        }
    }
}
