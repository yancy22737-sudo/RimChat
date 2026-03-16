using System;
using System.Collections.Generic;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: PromptVariableCatalog canonical list.
    /// Responsibility: provide stable static metadata used by workbench hover tooltips.
    /// </summary>
    internal static class PromptVariableTooltipCatalog
    {
        private static readonly Dictionary<string, string> ScopeDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ctx"] = "Request context and channel metadata.",
            ["system"] = "System-level language and output constraints.",
            ["world"] = "Faction, world, and social environment context.",
            ["pawn"] = "Speaker, target, and pawn state context.",
            ["dialogue"] = "Dialogue control, intent, and policy hints."
        };

        public static PromptVariableTooltipInfo Resolve(string variableName)
        {
            string normalized = (variableName ?? string.Empty).Trim();
            string scope = ResolveScope(normalized);
            string description = ResolveDescription(normalized, scope);
            string example = ResolveExample(normalized, scope);
            return new PromptVariableTooltipInfo(normalized, scope, description, example);
        }

        private static string ResolveScope(string variableName)
        {
            int dot = variableName.IndexOf('.');
            if (dot <= 0)
            {
                return "general";
            }

            return variableName.Substring(0, dot).Trim().ToLowerInvariant();
        }

        private static string ResolveDescription(string variableName, string scope)
        {
            if (string.IsNullOrWhiteSpace(variableName))
            {
                return string.Empty;
            }

            if (!PromptVariableCatalog.Contains(variableName))
            {
                return "Unknown or custom variable token.";
            }

            if (ScopeDescriptions.TryGetValue(scope, out string description))
            {
                return description;
            }

            return "Prompt variable metadata.";
        }

        private static string ResolveExample(string variableName, string scope)
        {
            if (string.IsNullOrWhiteSpace(variableName))
            {
                return string.Empty;
            }

            if (variableName.EndsWith(".name", StringComparison.OrdinalIgnoreCase))
            {
                return "Example: The Southern Empire";
            }

            if (variableName.EndsWith(".profile", StringComparison.OrdinalIgnoreCase))
            {
                return "Example: concise character profile text";
            }

            if (variableName.IndexOf("language", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Example: English";
            }

            switch (scope)
            {
                case "world":
                    return "Example: world context snapshot";
                case "pawn":
                    return "Example: pawn state snapshot";
                case "dialogue":
                    return "Example: dialogue guidance text";
                default:
                    return "Example: runtime-resolved value";
            }
        }
    }

    internal sealed class PromptVariableTooltipInfo
    {
        public PromptVariableTooltipInfo(string name, string scope, string description, string example)
        {
            Name = name ?? string.Empty;
            Scope = scope ?? string.Empty;
            Description = description ?? string.Empty;
            Example = example ?? string.Empty;
        }

        public string Name { get; }
        public string Scope { get; }
        public string Description { get; }
        public string Example { get; }
    }
}
