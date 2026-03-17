using RimChat.Config;

namespace RimChat.Persistence
{
    /// <summary>
    /// Dependencies: unified node layout slot enum.
    /// Responsibility: represent one runtime node placement for workbench preview and diagnostics.
    /// </summary>
    internal sealed class ResolvedPromptNodePlacement
    {
        public string PromptChannel = string.Empty;
        public string NodeId = string.Empty;
        public string OutputTag = string.Empty;
        public PromptUnifiedNodeSlot Slot = PromptUnifiedNodeSlot.MainChainAfter;
        public int Order = int.MaxValue;
        public bool Enabled = true;
        public bool Applied;
        public string Content = string.Empty;
    }
}
