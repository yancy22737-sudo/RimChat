using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Persistence;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: Verse scribing.
    /// Responsibility: persist one user-defined prompt variable root definition.
    /// </summary>
    public sealed class UserDefinedPromptVariableConfig : IExposable
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Key = string.Empty;
        public string DisplayName = string.Empty;
        public string Description = string.Empty;
        public string TemplateText = string.Empty;
        public bool Enabled = true;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id", string.Empty);
            Scribe_Values.Look(ref Key, "key", string.Empty);
            Scribe_Values.Look(ref DisplayName, "displayName", string.Empty);
            Scribe_Values.Look(ref Description, "description", string.Empty);
            Scribe_Values.Look(ref TemplateText, "templateText", string.Empty);
            Scribe_Values.Look(ref Enabled, "enabled", true);
        }

        public UserDefinedPromptVariableConfig Clone()
        {
            return new UserDefinedPromptVariableConfig
            {
                Id = Id ?? string.Empty,
                Key = Key ?? string.Empty,
                DisplayName = DisplayName ?? string.Empty,
                Description = Description ?? string.Empty,
                TemplateText = TemplateText ?? string.Empty,
                Enabled = Enabled
            };
        }
    }

    /// <summary>
    /// Dependencies: Verse scribing.
    /// Responsibility: persist one faction-specific override for a user-defined prompt variable.
    /// </summary>
    public sealed class FactionScopedPromptVariableOverrideConfig : IExposable
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string VariableKey = string.Empty;
        public string FactionDefName = string.Empty;
        public string TemplateText = string.Empty;
        public bool Enabled = true;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id", string.Empty);
            Scribe_Values.Look(ref VariableKey, "variableKey", string.Empty);
            Scribe_Values.Look(ref FactionDefName, "factionDefName", string.Empty);
            Scribe_Values.Look(ref TemplateText, "templateText", string.Empty);
            Scribe_Values.Look(ref Enabled, "enabled", true);
        }

        public FactionScopedPromptVariableOverrideConfig Clone()
        {
            return new FactionScopedPromptVariableOverrideConfig
            {
                Id = Id ?? string.Empty,
                VariableKey = VariableKey ?? string.Empty,
                FactionDefName = FactionDefName ?? string.Empty,
                TemplateText = TemplateText ?? string.Empty,
                Enabled = Enabled
            };
        }
    }

    /// <summary>
    /// Dependencies: none.
    /// Responsibility: report validation and delete-safety diagnostics for one user-defined prompt variable edit.
    /// </summary>
    public sealed class UserDefinedPromptVariableValidationResult
    {
        public List<string> Errors { get; } = new List<string>();
        public Dictionary<string, TemplateVariableValidationResult> TemplateResults { get; } = new Dictionary<string, TemplateVariableValidationResult>(StringComparer.OrdinalIgnoreCase);
        public bool IsValid => Errors.Count == 0;
    }

    /// <summary>
    /// Dependencies: none.
    /// Responsibility: describe one blocking reference when deleting a user-defined prompt variable.
    /// </summary>
    public sealed class UserDefinedPromptVariableReferenceLocation
    {
        public string LocationId { get; set; } = string.Empty;
        public string DisplayText { get; set; } = string.Empty;
    }

    /// <summary>
    /// Dependencies: none.
    /// Responsibility: package one variable definition together with its faction overrides for editor flows.
    /// </summary>
    public sealed class UserDefinedPromptVariableEditModel
    {
        public UserDefinedPromptVariableConfig Variable { get; set; } = new UserDefinedPromptVariableConfig();
        public List<FactionScopedPromptVariableOverrideConfig> Overrides { get; set; } = new List<FactionScopedPromptVariableOverrideConfig>();

        public UserDefinedPromptVariableEditModel Clone()
        {
            return new UserDefinedPromptVariableEditModel
            {
                Variable = Variable?.Clone() ?? new UserDefinedPromptVariableConfig(),
                Overrides = Overrides?.Select(item => item?.Clone()).Where(item => item != null).ToList()
                    ?? new List<FactionScopedPromptVariableOverrideConfig>()
            };
        }
    }
}
