using System.Collections.Generic;
using RimChat.Config;
using RimChat.Prompting;

namespace RimChat.Persistence
{
    internal enum PromptWorkspacePreviewBuildStage
    {
        Init = 0,
        Sections = 1,
        Nodes = 2,
        Finalize = 3,
        Completed = 4,
        Failed = 5
    }

    /// <summary>
    /// Dependencies: unified node slot enum.
    /// Responsibility: represent structured prompt-workspace preview blocks in final assembly order.
    /// </summary>
    internal enum PromptWorkspacePreviewBlockKind
    {
        Context = 0,
        Node = 1,
        SectionAggregate = 2,
        Footer = 3,
        Error = 4
    }

    internal sealed class PromptWorkspacePreviewBlock
    {
        public PromptWorkspacePreviewBlockKind Kind = PromptWorkspacePreviewBlockKind.Node;
        public string PromptChannel = string.Empty;
        public string NodeId = string.Empty;
        public PromptUnifiedNodeSlot Slot = PromptUnifiedNodeSlot.MainChainAfter;
        public int Order = 0;
        public string Content = string.Empty;
        public List<PromptWorkspacePreviewSubsection> Subsections = new List<PromptWorkspacePreviewSubsection>();
    }

    internal sealed class PromptWorkspacePreviewSubsection
    {
        public string SectionId = string.Empty;
        public string Content = string.Empty;
    }

    internal sealed class PromptWorkspaceStructuredPreview
    {
        public string Signature = string.Empty;
        public List<PromptWorkspacePreviewBlock> Blocks = new List<PromptWorkspacePreviewBlock>();
        public bool IsBuilding;
        public bool IsFailed;
        public int Completed;
        public int Total;
        public int CompletedSections;
        public int TotalSections;
        public int CompletedNodes;
        public int TotalNodes;
        public PromptWorkspacePreviewBuildStage Stage = PromptWorkspacePreviewBuildStage.Completed;
        public PromptWorkspacePreviewErrorDiagnostic ErrorDiagnostic;
    }

    internal sealed class PromptWorkspacePreviewErrorDiagnostic
    {
        public string TemplateId = string.Empty;
        public string Channel = string.Empty;
        public int ErrorCode;
        public int ErrorLine;
        public int ErrorColumn;
        public string Message = string.Empty;
    }

    internal sealed class PromptWorkspaceIncrementalPreviewBuildState
    {
        public RimTalkPromptChannel RootChannel = RimTalkPromptChannel.Diplomacy;
        public string PromptChannel = string.Empty;
        public bool IncludeNodes;
        public readonly PromptWorkspaceStructuredPreview Preview = new PromptWorkspaceStructuredPreview();
        public readonly List<PromptSectionSchemaItem> Sections = new List<PromptSectionSchemaItem>();
        public readonly List<PromptSectionAggregateSection> RenderedSections = new List<PromptSectionAggregateSection>();
        public readonly List<PromptUnifiedNodeLayoutConfig> NodeLayouts = new List<PromptUnifiedNodeLayoutConfig>();
        public int SectionCursor;
        public int NodeCursor;
    }
}
