using System;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Prompt configuration - used to store global and faction-specific prompts
    /// </summary>
    [Serializable]
    public class PromptConfig : IExposable
    {
        /// <summary>Configuration name (for display)</summary>
        public string Name = "";

        /// <summary>System prompt</summary>
        public string SystemPrompt = "";

        /// <summary>Dialogue prompt template</summary>
        public string DialoguePrompt = "";

        /// <summary>Whether enabled</summary>
        public bool Enabled = true;

        /// <summary>Faction ID (empty for global configuration)</summary>
        public string FactionId = "";

        public void ExposeData()
        {
            Scribe_Values.Look(ref Name, "name", "");
            Scribe_Values.Look(ref SystemPrompt, "systemPrompt", "");
            Scribe_Values.Look(ref DialoguePrompt, "dialoguePrompt", "");
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Values.Look(ref FactionId, "factionId", "");
        }

        public bool IsGlobal => string.IsNullOrEmpty(FactionId);

        public PromptConfig Clone()
        {
            return new PromptConfig
            {
                Name = this.Name,
                SystemPrompt = this.SystemPrompt,
                DialoguePrompt = this.DialoguePrompt,
                Enabled = this.Enabled,
                FactionId = this.FactionId
            };
        }
    }
}
