using System;

namespace RimChat.Prompting
{
    internal enum PromptTokenSegmentKind
    {
        Text = 0,
        VariableToken = 1
    }

    /// <summary>
    /// Dependencies: none.
    /// Responsibility: represent one parsed text segment from workbench prompt content.
    /// </summary>
    internal sealed class PromptTokenSegment
    {
        public PromptTokenSegmentKind Kind { get; set; }
        public string Text { get; set; } = string.Empty;
        public string VariableName { get; set; } = string.Empty;
        public int StartIndex { get; set; }
        public int Length { get; set; }
        public int EndIndex => StartIndex + Math.Max(0, Length);
    }
}
