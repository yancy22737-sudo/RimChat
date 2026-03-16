namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: none.
    /// Responsibility: provide one UI-facing neutral variable row shared by browsers and pickers.
    /// </summary>
    public sealed class PromptVariableDisplayEntry
    {
        public string Path { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
        public string SourceLabel { get; set; } = string.Empty;
        public string Availability { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public bool IsAvailable => string.Equals(Availability, "available", System.StringComparison.OrdinalIgnoreCase);

        public string Token => "{{" + (Path ?? string.Empty) + "}}";
    }
}
