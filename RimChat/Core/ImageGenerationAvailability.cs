using RimChat.Config;
using Verse;

namespace RimChat.Core
{
    /// <summary>
    /// Dependencies: Verse translation service and RimChat settings.
    /// Responsibility: centralize image-generation availability policy and user-facing message text.
    /// </summary>
    internal static class ImageGenerationAvailability
    {
        internal const string InDevelopmentKey = "RimChat_ImageGenerationInDevelopment";

        internal static bool IsBlocked()
        {
            RimChatSettings settings = RimChatMod.Settings;
            return settings?.DiplomacyImageApi == null || !settings.DiplomacyImageApi.IsEnabled;
        }

        internal static string GetBlockedMessage()
        {
            if (RimChatMod.Settings?.DiplomacyImageApi == null || !RimChatMod.Settings.DiplomacyImageApi.IsEnabled)
            {
                return "RimChat_SelfieConfigInvalid".Translate().ToString();
            }

            return InDevelopmentKey.Translate().ToString();
        }
    }
}
