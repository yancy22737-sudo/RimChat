using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: PromptRuntimeVariableRegistry metadata.
    /// Responsibility: provide canonical namespaced prompt variable catalog for editor, migration, and runtime validation.
    /// </summary>
    internal static class PromptVariableCatalog
    {
        public static bool Contains(string variablePath)
        {
            return PromptRuntimeVariableRegistry.Contains(variablePath);
        }

        public static IReadOnlyCollection<string> GetAll()
        {
            return PromptRuntimeVariableRegistry.GetPaths();
        }

        public static IReadOnlyList<PromptRuntimeVariableDefinition> GetDefinitions()
        {
            return PromptRuntimeVariableRegistry.GetDefinitions();
        }

        public static IReadOnlyList<PromptVariableDisplayEntry> GetDisplayEntries()
        {
            return GetDefinitions()
                .Where(item => item != null)
                .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .Select(definition =>
                {
                    PromptVariableTooltipInfo info = PromptVariableTooltipCatalog.Resolve(definition.Path);
                    return new PromptVariableDisplayEntry
                    {
                        Path = definition.Path,
                        Scope = ResolveScope(definition.Path),
                        SourceId = definition.SourceId,
                        SourceLabel = definition.SourceLabel,
                        Availability = definition.IsAvailable ? "available" : "unavailable",
                        Description = ResolveDisplayDescription(definition, info)
                    };
                })
                .ToList();
        }

        private static string ResolveDisplayDescription(
            PromptRuntimeVariableDefinition definition,
            PromptVariableTooltipInfo tooltipInfo)
        {
            if (definition != null)
            {
                string description = ResolveLocalizedDescription(definition.DescriptionKey);
                if (!string.IsNullOrWhiteSpace(description))
                {
                    return description;
                }
            }

            return tooltipInfo?.Description ?? string.Empty;
        }

        private static string ResolveLocalizedDescription(string descriptionKey)
        {
            if (string.IsNullOrWhiteSpace(descriptionKey))
            {
                return string.Empty;
            }

            string key = descriptionKey.Trim();
            string translated = key.Translate().ToString().Trim();
            if (string.IsNullOrWhiteSpace(translated))
            {
                return string.Empty;
            }

            // Built-in metadata keys should fall back to generic scope hints if localization is missing.
            if (translated == key && key.StartsWith("RimChat_", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return translated;
        }

        private static string ResolveScope(string variablePath)
        {
            if (string.IsNullOrWhiteSpace(variablePath))
            {
                return "unknown";
            }

            int separator = variablePath.IndexOf('.');
            return separator <= 0
                ? variablePath.Trim().ToLowerInvariant()
                : variablePath.Substring(0, separator).Trim().ToLowerInvariant();
        }
    }
}
