using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: Verse Scribe serialization.
    /// Responsibility: persist one prompt-channel toggle flag.
    /// </summary>
    public sealed class PromptChannelToggleConfig : IExposable
    {
        public string PromptChannel = RimTalkPromptEntryChannelCatalog.Any;
        public bool Enabled = true;

        public void ExposeData()
        {
            Scribe_Values.Look(ref PromptChannel, "promptChannel", RimTalkPromptEntryChannelCatalog.Any);
            Scribe_Values.Look(ref Enabled, "enabled", true);
            PromptChannel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(PromptChannel);
        }

        public PromptChannelToggleConfig Clone()
        {
            return new PromptChannelToggleConfig
            {
                PromptChannel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(PromptChannel),
                Enabled = Enabled
            };
        }
    }
}
