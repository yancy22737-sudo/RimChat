using Verse;

namespace RimDiplomacy.Config
{
    /// <summary>
    /// 提示词配置 - 用于存储全局和派系特定的提示词
    /// </summary>
    public class PromptConfig : IExposable
    {
        /// <summary>配置名称（用于显示）</summary>
        public string Name = "";

        /// <summary>系统提示词</summary>
        public string SystemPrompt = "";

        /// <summary>对话提示词模板</summary>
        public string DialoguePrompt = "";

        /// <summary>是否启用</summary>
        public bool Enabled = true;

        /// <summary>派系ID（全局配置为空）</summary>
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
