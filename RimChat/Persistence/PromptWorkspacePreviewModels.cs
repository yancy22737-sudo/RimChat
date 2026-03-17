using System.Collections.Generic;
using RimChat.Config;

namespace RimChat.Persistence
{
    /// <summary>
    /// Dependencies: unified node slot enum.
    /// Responsibility: represent structured prompt-workspace preview blocks in final assembly order.
    /// </summary>
    internal enum PromptWorkspacePreviewBlockKind
    {
        Context = 0,
        Node = 1,
        SectionAggregate = 2,
        Footer = 3
    }

    internal sealed class PromptWorkspacePreviewBlock
    {
        public PromptWorkspacePreviewBlockKind Kind = PromptWorkspacePreviewBlockKind.Node;
        public string PromptChannel = string.Empty;
        public string NodeId = string.Empty;
        public PromptUnifiedNodeSlot Slot = PromptUnifiedNodeSlot.MainChainAfter;
        public int Order = 0;
        public string Content = string.Empty;
    }

    internal sealed class PromptWorkspaceStructuredPreview
    {
        public string Signature = string.Empty;
        public List<PromptWorkspacePreviewBlock> Blocks = new List<PromptWorkspacePreviewBlock>();
    }
}
