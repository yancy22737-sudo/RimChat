using System.Collections.Generic;

namespace RimChat.Persistence
{
    /// <summary>/// Responsibility: describes a supported {{template_variable}} token for prompt editing UIs.
 ///</summary>
    public sealed class PromptTemplateVariableDefinition
    {
        public string Name { get; }
        public string DescriptionKey { get; }
        public string SourceId { get; }
        public string SourceLabel { get; }
        public bool IsAvailable { get; }

        public string Token => "{{" + Name + "}}";

        public PromptTemplateVariableDefinition(
            string name,
            string descriptionKey,
            string sourceId = "",
            string sourceLabel = "",
            bool isAvailable = true)
        {
            Name = name ?? string.Empty;
            DescriptionKey = descriptionKey ?? string.Empty;
            SourceId = sourceId ?? string.Empty;
            SourceLabel = sourceLabel ?? string.Empty;
            IsAvailable = isAvailable;
        }
    }

    /// <summary>/// Responsibility: variable validation result for one prompt text.
 ///</summary>
    public sealed class TemplateVariableValidationResult
    {
        public List<string> UsedVariables { get; } = new List<string>();
        public List<string> UnknownVariables { get; } = new List<string>();
        public int ScribanErrorCode { get; set; }
        public int ScribanErrorLine { get; set; }
        public int ScribanErrorColumn { get; set; }
        public string ScribanErrorMessage { get; set; } = string.Empty;
        public bool HasScribanError => ScribanErrorCode != 0;
    }

    /// <summary>/// Responsibility: scene-layer build diagnostics for preview explain mode.
 ///</summary>
    public sealed class EnvironmentPromptBuildDiagnostics
    {
        public List<string> ScenarioTags { get; } = new List<string>();
        public List<EnvironmentSceneEntryDiagnostic> SceneEntries { get; } = new List<EnvironmentSceneEntryDiagnostic>();
    }

    /// <summary>/// Responsibility: one scene entry's match/apply diagnostics.
 ///</summary>
    public sealed class EnvironmentSceneEntryDiagnostic
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Priority { get; set; }
        public bool Included { get; set; }
        public bool ChannelMatched { get; set; }
        public bool TagsMatched { get; set; }
        public bool TruncatedByPerSceneLimit { get; set; }
        public bool TruncatedByTotalLimit { get; set; }
        public int OriginalChars { get; set; }
        public int AppliedChars { get; set; }
        public string SkipReason { get; set; } = string.Empty;
        public List<string> UsedVariables { get; } = new List<string>();
        public List<string> UnknownVariables { get; } = new List<string>();
    }
}
