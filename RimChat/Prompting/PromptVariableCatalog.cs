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
                    string namespacedToken = BuildWrappedToken(definition.Path);
                    string rawToken = PromptRuntimeVariableBridge.ResolveRawToken(definition.Path);
                    if (string.IsNullOrWhiteSpace(rawToken))
                    {
                        rawToken = namespacedToken;
                    }

                    return new PromptVariableDisplayEntry
                    {
                        Path = definition.Path,
                        RawToken = rawToken,
                        NamespacedToken = namespacedToken,
                        DefaultInsertToken = rawToken,
                        Scope = ResolveScope(definition.Path),
                        SourceId = definition.SourceId,
                        SourceLabel = definition.SourceLabel,
                        Availability = definition.IsAvailable ? "available" : "unavailable",
                        Description = ResolveDisplayDescription(definition, info),
                        DetailSummary = info?.Description ?? string.Empty,
                        IsEditable = UserDefinedPromptVariableService.IsUserDefinedPath(definition.Path)
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

        private static string BuildWrappedToken(string variableName)
        {
            return "{{ " + (variableName ?? string.Empty) + " }}";
        }

        public static IReadOnlyList<string> GetClosestSuggestions(string partialPath, int maxResults = 3)
        {
            if (string.IsNullOrWhiteSpace(partialPath))
            {
                return Array.Empty<string>();
            }

            string normalized = partialPath.Trim().ToLowerInvariant();
            IReadOnlyCollection<string> allPaths = GetAll();
            if (allPaths.Count == 0)
            {
                return Array.Empty<string>();
            }

            // Prefer same-namespace matches, then fall back to global distance
            var scored = new List<(string path, int score)>();
            int dotIndex = normalized.LastIndexOf('.');
            string prefix = dotIndex > 0 ? normalized.Substring(0, dotIndex + 1) : string.Empty;

            foreach (string candidate in allPaths)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                string candidateLower = candidate.ToLowerInvariant();
                if (candidateLower == normalized)
                {
                    continue; // exact match, not a suggestion
                }

                int score = ComputeSuggestionScore(normalized, candidateLower, prefix);
                if (score >= 0)
                {
                    scored.Add((candidate, score));
                }
            }

            return scored
                .OrderBy(item => item.score)
                .ThenBy(item => item.path, StringComparer.OrdinalIgnoreCase)
                .Take(maxResults)
                .Select(item => item.path)
                .ToList();
        }

        private static int ComputeSuggestionScore(string query, string candidate, string prefix)
        {
            // Namespace prefix bonus: paths with same prefix are much closer
            int score = 0;
            if (prefix.Length > 0 && candidate.StartsWith(prefix, StringComparison.Ordinal))
            {
                score -= 100;
            }
            else if (prefix.Length > 0)
            {
                score += 50; // penalty for different namespace
            }

            // Edit distance (simple Levenshtein-like)
            int distance = ComputeEditDistance(query, candidate);
            score += distance;

            return score;
        }

        private static int ComputeEditDistance(string a, string b)
        {
            int n = a.Length;
            int m = b.Length;
            if (n == 0) return m;
            if (m == 0) return n;

            var prev = new int[m + 1];
            var curr = new int[m + 1];
            for (int j = 0; j <= m; j++)
            {
                prev[j] = j;
            }

            for (int i = 1; i <= n; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                }

                var tmp = prev;
                prev = curr;
                curr = tmp;
            }

            return prev[m];
        }
    }
}
