using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Persistence;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: Verse scribing.
    /// Responsibility: persist one user-defined prompt variable root definition and its default template.
    /// </summary>
    public sealed class UserDefinedPromptVariableConfig : IExposable
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Key = string.Empty;
        public string DisplayName = string.Empty;
        public string Description = string.Empty;
        public string DefaultTemplateText = string.Empty;
        public bool Enabled = true;

        public void ExposeData()
        {
            string legacyTemplateText = string.Empty;
            Scribe_Values.Look(ref Id, "id", string.Empty);
            Scribe_Values.Look(ref Key, "key", string.Empty);
            Scribe_Values.Look(ref DisplayName, "displayName", string.Empty);
            Scribe_Values.Look(ref Description, "description", string.Empty);
            Scribe_Values.Look(ref DefaultTemplateText, "defaultTemplateText", string.Empty);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Scribe_Values.Look(ref legacyTemplateText, "templateText", string.Empty);
                if (string.IsNullOrWhiteSpace(DefaultTemplateText))
                {
                    DefaultTemplateText = legacyTemplateText ?? string.Empty;
                }
            }

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
                DefaultTemplateText = DefaultTemplateText ?? string.Empty,
                Enabled = Enabled
            };
        }
    }

    /// <summary>
    /// Dependencies: Verse scribing.
    /// Responsibility: load legacy faction override payloads for migration into the unified rule model.
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
    /// Dependencies: Verse scribing.
    /// Responsibility: persist one faction-scoped text rule for a user-defined prompt variable.
    /// </summary>
    public sealed class FactionPromptVariableRuleConfig : IExposable
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string VariableKey = string.Empty;
        public string FactionDefName = string.Empty;
        public int Priority;
        public string TemplateText = string.Empty;
        public bool Enabled = true;
        public int Order = -1;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id", string.Empty);
            Scribe_Values.Look(ref VariableKey, "variableKey", string.Empty);
            Scribe_Values.Look(ref FactionDefName, "factionDefName", string.Empty);
            Scribe_Values.Look(ref Priority, "priority", 0);
            Scribe_Values.Look(ref TemplateText, "templateText", string.Empty);
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Values.Look(ref Order, "order", -1);
        }

        public FactionPromptVariableRuleConfig Clone()
        {
            return new FactionPromptVariableRuleConfig
            {
                Id = Id ?? string.Empty,
                VariableKey = VariableKey ?? string.Empty,
                FactionDefName = FactionDefName ?? string.Empty,
                Priority = Priority,
                TemplateText = TemplateText ?? string.Empty,
                Enabled = Enabled,
                Order = Order
            };
        }
    }

    /// <summary>
    /// Dependencies: Verse scribing.
    /// Responsibility: persist one pawn-scoped text rule for a user-defined prompt variable.
    /// </summary>
    public sealed class PawnPromptVariableRuleConfig : IExposable
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string VariableKey = string.Empty;
        public string NameExact = string.Empty;
        public string FactionDefName = string.Empty;
        public string RaceDefName = string.Empty;
        public string Gender = string.Empty;
        public string AgeStage = string.Empty;
        public List<string> TraitsAny = new List<string>();
        public List<string> TraitsAll = new List<string>();
        public string XenotypeDefName = string.Empty;
        public string PlayerControlled = string.Empty;
        public int Priority;
        public string TemplateText = string.Empty;
        public bool Enabled = true;
        public int Order = -1;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id", string.Empty);
            Scribe_Values.Look(ref VariableKey, "variableKey", string.Empty);
            Scribe_Values.Look(ref NameExact, "nameExact", string.Empty);
            Scribe_Values.Look(ref FactionDefName, "factionDefName", string.Empty);
            Scribe_Values.Look(ref RaceDefName, "raceDefName", string.Empty);
            Scribe_Values.Look(ref Gender, "gender", string.Empty);
            Scribe_Values.Look(ref AgeStage, "ageStage", string.Empty);
            Scribe_Collections.Look(ref TraitsAny, "traitsAny", LookMode.Value);
            Scribe_Collections.Look(ref TraitsAll, "traitsAll", LookMode.Value);
            Scribe_Values.Look(ref XenotypeDefName, "xenotypeDefName", string.Empty);
            Scribe_Values.Look(ref PlayerControlled, "playerControlled", string.Empty);
            Scribe_Values.Look(ref Priority, "priority", 0);
            Scribe_Values.Look(ref TemplateText, "templateText", string.Empty);
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Values.Look(ref Order, "order", -1);
        }

        public PawnPromptVariableRuleConfig Clone()
        {
            return new PawnPromptVariableRuleConfig
            {
                Id = Id ?? string.Empty,
                VariableKey = VariableKey ?? string.Empty,
                NameExact = NameExact ?? string.Empty,
                FactionDefName = FactionDefName ?? string.Empty,
                RaceDefName = RaceDefName ?? string.Empty,
                Gender = Gender ?? string.Empty,
                AgeStage = AgeStage ?? string.Empty,
                TraitsAny = TraitsAny?.Where(item => !string.IsNullOrWhiteSpace(item)).ToList() ?? new List<string>(),
                TraitsAll = TraitsAll?.Where(item => !string.IsNullOrWhiteSpace(item)).ToList() ?? new List<string>(),
                XenotypeDefName = XenotypeDefName ?? string.Empty,
                PlayerControlled = PlayerControlled ?? string.Empty,
                Priority = Priority,
                TemplateText = TemplateText ?? string.Empty,
                Enabled = Enabled,
                Order = Order
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
    /// Responsibility: package one variable definition together with its unified rule set for editor flows.
    /// </summary>
    public sealed class UserDefinedPromptVariableEditModel
    {
        public UserDefinedPromptVariableConfig Variable { get; set; } = new UserDefinedPromptVariableConfig();
        public List<FactionPromptVariableRuleConfig> FactionRules { get; set; } = new List<FactionPromptVariableRuleConfig>();
        public List<PawnPromptVariableRuleConfig> PawnRules { get; set; } = new List<PawnPromptVariableRuleConfig>();

        public UserDefinedPromptVariableEditModel Clone()
        {
            return new UserDefinedPromptVariableEditModel
            {
                Variable = Variable?.Clone() ?? new UserDefinedPromptVariableConfig(),
                FactionRules = FactionRules?.Select(item => item?.Clone()).Where(item => item != null).ToList()
                    ?? new List<FactionPromptVariableRuleConfig>(),
                PawnRules = PawnRules?.Select(item => item?.Clone()).Where(item => item != null).ToList()
                    ?? new List<PawnPromptVariableRuleConfig>()
            };
        }
    }
}
