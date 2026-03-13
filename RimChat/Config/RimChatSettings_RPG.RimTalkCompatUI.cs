using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: RimChat settings localization keys.
    /// Responsibility: share tiny helper methods reused by RPG and RimTalk tabs.
    /// </summary>
    public partial class RimChatSettings : ModSettings
    {
        private static string FormatUnlimitedAwareLimit(int value)
        {
            return value <= RimTalkPresetInjectionLimitUnlimited
                ? "RimChat_Unlimited".Translate().ToString()
                : value.ToString();
        }
    }
}
