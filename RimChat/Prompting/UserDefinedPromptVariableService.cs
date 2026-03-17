using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimChat.Config;
using RimChat.Core;
using RimChat.Persistence;
using RimWorld;
using Verse;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: RimChat settings, prompt validation service, and RimWorld faction defs.
    /// Responsibility: provide canonical read/write helpers, validation, and reference scanning for user-defined prompt variables.
    /// </summary>
    internal static class UserDefinedPromptVariableService
    {
        public const string NamespaceRoot = "system.custom";
        private const string SourceId = "rimchat.user";
        private const string SourceLabel = "User Variable";

        public static bool IsUserDefinedPath(string path)
        {
            string normalized = (path ?? string.Empty).Trim();
            return normalized.StartsWith(NamespaceRoot + ".", StringComparison.OrdinalIgnoreCase);
        }

        public static string NormalizeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(key.Length);
            string trimmed = key.Trim();
            for (int i = 0; i < trimmed.Length; i++)
            {
                char current = trimmed[i];
                if (char.IsLetterOrDigit(current))
                {
                    builder.Append(char.ToLowerInvariant(current));
                }
                else if (current == '_')
                {
                    builder.Append('_');
                }
            }

            return builder.ToString();
        }

        public static bool IsValidKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            string normalized = key.Trim();
            for (int i = 0; i < normalized.Length; i++)
            {
                char current = normalized[i];
                if (!(char.IsLower(current) || char.IsDigit(current) || current == '_'))
                {
                    return false;
                }
            }

            return true;
        }

        public static string BuildPath(string key)
        {
            string normalized = NormalizeKey(key);
            return string.IsNullOrWhiteSpace(normalized)
                ? string.Empty
                : NamespaceRoot + "." + normalized;
        }

        public static string ExtractKeyFromPath(string path)
        {
            if (!IsUserDefinedPath(path))
            {
                return string.Empty;
            }

            string normalized = path.Trim();
            return normalized.Substring(NamespaceRoot.Length + 1).Trim().ToLowerInvariant();
        }

        public static string GetSourceId()
        {
            return SourceId;
        }

        public static string GetSourceLabel()
        {
            return SourceLabel;
        }

        public static IReadOnlyList<UserDefinedPromptVariableConfig> GetVariables(RimChat.Config.RimChatSettings settings = null)
        {
            RimChat.Config.RimChatSettings resolved = settings ?? RimChatMod.Settings;
            return resolved?.UserDefinedPromptVariables != null
                ? resolved.UserDefinedPromptVariables
                : (IReadOnlyList<UserDefinedPromptVariableConfig>)Array.Empty<UserDefinedPromptVariableConfig>();
        }

        public static IReadOnlyList<FactionScopedPromptVariableOverrideConfig> GetOverrides(RimChat.Config.RimChatSettings settings = null)
        {
            RimChat.Config.RimChatSettings resolved = settings ?? RimChatMod.Settings;
            return resolved?.FactionScopedPromptVariableOverrides != null
                ? resolved.FactionScopedPromptVariableOverrides
                : (IReadOnlyList<FactionScopedPromptVariableOverrideConfig>)Array.Empty<FactionScopedPromptVariableOverrideConfig>();
        }

        public static UserDefinedPromptVariableConfig FindVariableByPath(string path, RimChat.Config.RimChatSettings settings = null)
        {
            string key = ExtractKeyFromPath(path);
            return FindVariableByKey(key, settings);
        }

        public static UserDefinedPromptVariableConfig FindVariableByKey(string key, RimChat.Config.RimChatSettings settings = null)
        {
            string normalized = NormalizeKey(key);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            return GetVariables(settings).FirstOrDefault(item =>
                item != null && string.Equals(NormalizeKey(item.Key), normalized, StringComparison.Ordinal));
        }

        public static List<FactionScopedPromptVariableOverrideConfig> GetOverridesForKey(string key, RimChat.Config.RimChatSettings settings = null)
        {
            string normalized = NormalizeKey(key);
            return GetOverrides(settings)
                .Where(item => item != null && string.Equals(NormalizeKey(item.VariableKey), normalized, StringComparison.Ordinal))
                .Select(item => item.Clone())
                .ToList();
        }

        public static PromptRuntimeVariableDefinition BuildDefinition(UserDefinedPromptVariableConfig config)
        {
            string path = BuildPath(config?.Key);
            string description = BuildDefinitionDescription(config, GetOverridesForKey(config?.Key));
            return new PromptRuntimeVariableDefinition(path, SourceId, SourceLabel, description, true);
        }

        public static string BuildDefinitionDescription(
            UserDefinedPromptVariableConfig config,
            IReadOnlyCollection<FactionScopedPromptVariableOverrideConfig> overrides)
        {
            if (config == null)
            {
                return string.Empty;
            }

            string displayName = string.IsNullOrWhiteSpace(config.DisplayName)
                ? BuildPath(config.Key)
                : config.DisplayName.Trim();
            int enabledOverrides = overrides?.Count(item => item != null && item.Enabled) ?? 0;
            string stateText = config.Enabled ? "enabled" : "disabled";
            string summary = string.IsNullOrWhiteSpace(config.Description) ? displayName : config.Description.Trim();
            return $"{summary} ({stateText}, overrides: {enabledOverrides})";
        }

        public static PromptVariableTooltipInfo BuildTooltipInfo(string path)
        {
            UserDefinedPromptVariableConfig config = FindVariableByPath(path);
            if (config == null)
            {
                return null;
            }

            List<FactionScopedPromptVariableOverrideConfig> overrides = GetOverridesForKey(config.Key);
            List<string> typicalValues = new List<string>();
            if (!string.IsNullOrWhiteSpace(config.TemplateText))
            {
                typicalValues.Add(config.TemplateText.Trim());
            }

            foreach (FactionScopedPromptVariableOverrideConfig entry in overrides.Where(item => item != null && item.Enabled))
            {
                if (string.IsNullOrWhiteSpace(entry.TemplateText))
                {
                    continue;
                }

                typicalValues.Add($"{entry.FactionDefName}: {entry.TemplateText.Trim()}");
                if (typicalValues.Count >= 3)
                {
                    break;
                }
            }

            string name = string.IsNullOrWhiteSpace(config.DisplayName)
                ? BuildPath(config.Key)
                : config.DisplayName.Trim();
            string description = BuildDefinitionDescription(config, overrides);
            return new PromptVariableTooltipInfo(name, "Scriban text", description, typicalValues);
        }

        public static void NormalizeSettingsCollections(RimChat.Config.RimChatSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.UserDefinedPromptVariables ??= new List<UserDefinedPromptVariableConfig>();
            settings.FactionScopedPromptVariableOverrides ??= new List<FactionScopedPromptVariableOverrideConfig>();

            var normalizedVariables = new List<UserDefinedPromptVariableConfig>();
            var seenVariableKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UserDefinedPromptVariableConfig item in settings.UserDefinedPromptVariables)
            {
                if (item == null)
                {
                    continue;
                }

                item.Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id.Trim();
                item.Key = NormalizeKey(item.Key);
                item.DisplayName = item.DisplayName?.Trim() ?? string.Empty;
                item.Description = item.Description?.Trim() ?? string.Empty;
                item.TemplateText = item.TemplateText ?? string.Empty;
                if (string.IsNullOrWhiteSpace(item.Key) || !seenVariableKeys.Add(item.Key))
                {
                    continue;
                }

                normalizedVariables.Add(item);
            }

            settings.UserDefinedPromptVariables = normalizedVariables;

            var normalizedOverrides = new List<FactionScopedPromptVariableOverrideConfig>();
            var seenOverridePairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FactionScopedPromptVariableOverrideConfig item in settings.FactionScopedPromptVariableOverrides)
            {
                if (item == null)
                {
                    continue;
                }

                item.Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id.Trim();
                item.VariableKey = NormalizeKey(item.VariableKey);
                item.FactionDefName = item.FactionDefName?.Trim() ?? string.Empty;
                item.TemplateText = item.TemplateText ?? string.Empty;
                if (string.IsNullOrWhiteSpace(item.VariableKey) ||
                    string.IsNullOrWhiteSpace(item.FactionDefName) ||
                    FindVariableByKey(item.VariableKey, settings) == null)
                {
                    continue;
                }

                string pairKey = item.VariableKey + "::" + item.FactionDefName;
                if (!seenOverridePairs.Add(pairKey))
                {
                    continue;
                }

                normalizedOverrides.Add(item);
            }

            settings.FactionScopedPromptVariableOverrides = normalizedOverrides;
        }

        public static UserDefinedPromptVariableValidationResult ValidateEdit(
            RimChat.Config.RimChatSettings settings,
            UserDefinedPromptVariableEditModel editModel,
            UserDefinedPromptVariableConfig originalVariable = null)
        {
            var result = new UserDefinedPromptVariableValidationResult();
            if (settings == null)
            {
                result.Errors.Add("Settings unavailable.");
                return result;
            }

            UserDefinedPromptVariableConfig variable = editModel?.Variable ?? new UserDefinedPromptVariableConfig();
            string normalizedKey = NormalizeKey(variable.Key);
            string path = BuildPath(normalizedKey);
            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                result.Errors.Add("RimChat_CustomVariableValidation_InvalidKey".Translate().ToString());
                return result;
            }

            if (!IsValidKey(normalizedKey))
            {
                result.Errors.Add("RimChat_CustomVariableValidation_InvalidKey".Translate().ToString());
            }

            bool keyTaken = GetVariables(settings).Any(item =>
                item != null &&
                !string.Equals(item.Id ?? string.Empty, originalVariable?.Id ?? string.Empty, StringComparison.Ordinal) &&
                string.Equals(NormalizeKey(item.Key), normalizedKey, StringComparison.Ordinal));
            if (keyTaken)
            {
                result.Errors.Add("RimChat_CustomVariableValidation_DuplicateKey".Translate(normalizedKey).ToString());
            }

            bool pathConflict = PromptRuntimeVariableRegistry.ContainsReservedPath(path, originalVariable == null ? string.Empty : BuildPath(originalVariable.Key));
            if (pathConflict)
            {
                result.Errors.Add("RimChat_CustomVariableValidation_PathConflict".Translate(path).ToString());
            }

            ValidateTemplate(
                result,
                settings,
                "default",
                variable.TemplateText,
                path,
                editModel?.Overrides,
                originalVariable);

            HashSet<string> overrideFactions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FactionScopedPromptVariableOverrideConfig entry in editModel?.Overrides ?? Enumerable.Empty<FactionScopedPromptVariableOverrideConfig>())
            {
                if (entry == null)
                {
                    continue;
                }

                entry.VariableKey = normalizedKey;
                if (string.IsNullOrWhiteSpace(entry.FactionDefName) ||
                    DefDatabase<FactionDef>.GetNamedSilentFail(entry.FactionDefName) == null)
                {
                    result.Errors.Add("RimChat_CustomVariableValidation_InvalidFaction".Translate(entry.FactionDefName ?? string.Empty).ToString());
                }
                else if (!overrideFactions.Add(entry.FactionDefName))
                {
                    result.Errors.Add("RimChat_CustomVariableValidation_DuplicateFaction".Translate(entry.FactionDefName).ToString());
                }

                ValidateTemplate(
                    result,
                    settings,
                    $"override:{entry.FactionDefName}",
                    entry.TemplateText,
                    path,
                    editModel?.Overrides,
                    originalVariable);
            }

            DetectCycleErrors(result, settings, editModel, originalVariable);
            return result;
        }

        public static bool TrySaveEdit(
            RimChat.Config.RimChatSettings settings,
            UserDefinedPromptVariableEditModel editModel,
            UserDefinedPromptVariableConfig originalVariable,
            out UserDefinedPromptVariableValidationResult validationResult)
        {
            validationResult = ValidateEdit(settings, editModel, originalVariable);
            if (!validationResult.IsValid)
            {
                return false;
            }

            string originalId = originalVariable?.Id ?? string.Empty;
            UserDefinedPromptVariableConfig target = settings.UserDefinedPromptVariables.FirstOrDefault(item =>
                item != null && string.Equals(item.Id ?? string.Empty, originalId, StringComparison.Ordinal));
            if (target == null)
            {
                target = new UserDefinedPromptVariableConfig();
                settings.UserDefinedPromptVariables.Add(target);
            }

            ApplyVariable(target, editModel.Variable);

            settings.FactionScopedPromptVariableOverrides.RemoveAll(item =>
                item != null && string.Equals(NormalizeKey(item.VariableKey), NormalizeKey(target.Key), StringComparison.OrdinalIgnoreCase));
            foreach (FactionScopedPromptVariableOverrideConfig entry in editModel.Overrides.Where(item => item != null))
            {
                FactionScopedPromptVariableOverrideConfig clone = entry.Clone();
                clone.VariableKey = target.Key;
                settings.FactionScopedPromptVariableOverrides.Add(clone);
            }

            NormalizeSettingsCollections(settings);
            return true;
        }

        public static List<UserDefinedPromptVariableReferenceLocation> FindReferences(string path, RimChat.Config.RimChatSettings settings = null)
        {
            RimChat.Config.RimChatSettings resolved = settings ?? RimChatMod.Settings;
            var matches = new List<UserDefinedPromptVariableReferenceLocation>();
            if (resolved == null || string.IsNullOrWhiteSpace(path))
            {
                return matches;
            }

            string normalized = path.Trim();
            foreach (PromptTemplateReferenceCandidate candidate in EnumerateReferenceCandidates(resolved))
            {
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.TemplateText))
                {
                    continue;
                }

                TemplateVariableValidationResult validation = PromptPersistenceService.Instance.ValidateTemplateVariables(candidate.TemplateText);
                if (validation.UsedVariables.Any(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    matches.Add(new UserDefinedPromptVariableReferenceLocation
                    {
                        LocationId = candidate.LocationId,
                        DisplayText = candidate.DisplayText
                    });
                }
            }

            return matches
                .GroupBy(item => item.LocationId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        public static bool TryDeleteVariable(
            RimChat.Config.RimChatSettings settings,
            string path,
            out List<UserDefinedPromptVariableReferenceLocation> references)
        {
            references = FindReferences(path, settings);
            if (references.Count > 0)
            {
                return false;
            }

            UserDefinedPromptVariableConfig config = FindVariableByPath(path, settings);
            if (config == null)
            {
                return true;
            }

            string normalizedKey = NormalizeKey(config.Key);
            settings.UserDefinedPromptVariables.RemoveAll(item =>
                item != null && string.Equals(NormalizeKey(item.Key), normalizedKey, StringComparison.OrdinalIgnoreCase));
            settings.FactionScopedPromptVariableOverrides.RemoveAll(item =>
                item != null && string.Equals(NormalizeKey(item.VariableKey), normalizedKey, StringComparison.OrdinalIgnoreCase));
            NormalizeSettingsCollections(settings);
            return true;
        }

        private static void ApplyVariable(UserDefinedPromptVariableConfig target, UserDefinedPromptVariableConfig source)
        {
            target.Id = string.IsNullOrWhiteSpace(source?.Id) ? Guid.NewGuid().ToString("N") : source.Id.Trim();
            target.Key = NormalizeKey(source?.Key);
            target.DisplayName = source?.DisplayName?.Trim() ?? string.Empty;
            target.Description = source?.Description?.Trim() ?? string.Empty;
            target.TemplateText = source?.TemplateText ?? string.Empty;
            target.Enabled = source?.Enabled ?? true;
        }

        private static void ValidateTemplate(
            UserDefinedPromptVariableValidationResult result,
            RimChat.Config.RimChatSettings settings,
            string templateId,
            string templateText,
            string currentPath,
            IEnumerable<FactionScopedPromptVariableOverrideConfig> overrides,
            UserDefinedPromptVariableConfig originalVariable)
        {
            TemplateVariableValidationResult validation = PromptPersistenceService.Instance.ValidateTemplateVariables(
                templateText ?? string.Empty,
                BuildAdditionalKnownPaths(settings, currentPath, originalVariable));
            result.TemplateResults[templateId] = validation;

            if (validation.HasScribanError)
            {
                result.Errors.Add("RimChat_CustomVariableValidation_TemplateCompile".Translate(templateId, validation.ScribanErrorMessage).ToString());
            }

            if (validation.UnknownVariables.Count > 0)
            {
                result.Errors.Add("RimChat_CustomVariableValidation_UnknownVariables".Translate(templateId, string.Join(", ", validation.UnknownVariables)).ToString());
            }
        }

        private static IEnumerable<string> BuildAdditionalKnownPaths(
            RimChat.Config.RimChatSettings settings,
            string currentPath,
            UserDefinedPromptVariableConfig originalVariable)
        {
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                currentPath
            };

            if (originalVariable != null)
            {
                string originalPath = BuildPath(originalVariable.Key);
                if (!string.IsNullOrWhiteSpace(originalPath))
                {
                    paths.Add(originalPath);
                }
            }

            foreach (UserDefinedPromptVariableConfig item in GetVariables(settings))
            {
                string path = BuildPath(item?.Key);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    paths.Add(path);
                }
            }

            return paths;
        }

        private static void DetectCycleErrors(
            UserDefinedPromptVariableValidationResult result,
            RimChat.Config.RimChatSettings settings,
            UserDefinedPromptVariableEditModel editModel,
            UserDefinedPromptVariableConfig originalVariable)
        {
            string draftKey = NormalizeKey(editModel?.Variable?.Key);
            Dictionary<string, HashSet<string>> graph = BuildDependencyGraph(settings, editModel, originalVariable);
            if (string.IsNullOrWhiteSpace(draftKey) || !graph.ContainsKey(draftKey))
            {
                return;
            }

            List<string> path = new List<string>();
            HashSet<string> visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (TryFindCycle(draftKey, graph, visiting, visited, path, out List<string> cycle))
            {
                result.Errors.Add("RimChat_CustomVariableValidation_Cycle".Translate(string.Join(" -> ", cycle)).ToString());
            }
        }

        private static Dictionary<string, HashSet<string>> BuildDependencyGraph(
            RimChat.Config.RimChatSettings settings,
            UserDefinedPromptVariableEditModel editModel,
            UserDefinedPromptVariableConfig originalVariable)
        {
            var graph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (UserDefinedPromptVariableConfig item in GetVariables(settings))
            {
                if (item == null)
                {
                    continue;
                }

                bool isCurrent = originalVariable != null &&
                                 string.Equals(item.Id ?? string.Empty, originalVariable.Id ?? string.Empty, StringComparison.Ordinal);
                if (isCurrent)
                {
                    continue;
                }

                AddDependencies(graph, NormalizeKey(item.Key), item.TemplateText);
                foreach (FactionScopedPromptVariableOverrideConfig entry in GetOverridesForKey(item.Key, settings))
                {
                    AddDependencies(graph, NormalizeKey(item.Key), entry.TemplateText);
                }
            }

            if (editModel?.Variable != null)
            {
                AddDependencies(graph, NormalizeKey(editModel.Variable.Key), editModel.Variable.TemplateText);
                foreach (FactionScopedPromptVariableOverrideConfig entry in editModel.Overrides ?? Enumerable.Empty<FactionScopedPromptVariableOverrideConfig>())
                {
                    AddDependencies(graph, NormalizeKey(editModel.Variable.Key), entry?.TemplateText);
                }
            }

            return graph;
        }

        private static void AddDependencies(Dictionary<string, HashSet<string>> graph, string key, string templateText)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (!graph.TryGetValue(key, out HashSet<string> deps))
            {
                deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                graph[key] = deps;
            }

            TemplateVariableValidationResult validation = PromptPersistenceService.Instance.ValidateTemplateVariables(templateText ?? string.Empty);
            foreach (string used in validation.UsedVariables)
            {
                if (!IsUserDefinedPath(used))
                {
                    continue;
                }

                string dependencyKey = ExtractKeyFromPath(used);
                if (!string.IsNullOrWhiteSpace(dependencyKey))
                {
                    deps.Add(dependencyKey);
                }
            }
        }

        private static bool TryFindCycle(
            string current,
            Dictionary<string, HashSet<string>> graph,
            HashSet<string> visiting,
            HashSet<string> visited,
            List<string> path,
            out List<string> cycle)
        {
            cycle = null;
            if (visiting.Contains(current))
            {
                int start = path.FindIndex(item => string.Equals(item, current, StringComparison.OrdinalIgnoreCase));
                if (start >= 0)
                {
                    cycle = path.Skip(start).Concat(new[] { current }).ToList();
                    return true;
                }

                cycle = new List<string> { current, current };
                return true;
            }

            if (!visited.Add(current))
            {
                return false;
            }

            visiting.Add(current);
            path.Add(current);
            if (graph.TryGetValue(current, out HashSet<string> deps))
            {
                foreach (string dependency in deps)
                {
                    if (TryFindCycle(dependency, graph, visiting, visited, path, out cycle))
                    {
                        return true;
                    }
                }
            }

            visiting.Remove(current);
            path.RemoveAt(path.Count - 1);
            return false;
        }

        private static IEnumerable<PromptTemplateReferenceCandidate> EnumerateReferenceCandidates(RimChat.Config.RimChatSettings settings)
        {
            foreach (RimTalkPromptChannel channel in Enum.GetValues(typeof(RimTalkPromptChannel)).Cast<RimTalkPromptChannel>())
            {
                RimTalkChannelCompatConfig compat = settings.GetRimTalkChannelConfigClone(channel);
                yield return new PromptTemplateReferenceCandidate(
                    $"compat:{channel}",
                    $"Compat Template / {channel}",
                    compat?.CompatTemplate ?? string.Empty);
            }

            yield return new PromptTemplateReferenceCandidate(
                "persona_copy",
                "RimTalk Persona Copy",
                settings.GetRimTalkPersonaCopyTemplateOrDefault());

            RimTalkPromptEntryDefaultsConfig catalog = settings.GetPromptSectionCatalogClone();
            foreach (RimTalkPromptChannelDefaultsConfig channelConfig in catalog?.Channels ?? Enumerable.Empty<RimTalkPromptChannelDefaultsConfig>())
            {
                foreach (RimTalkPromptSectionDefaultConfig section in channelConfig?.Sections ?? Enumerable.Empty<RimTalkPromptSectionDefaultConfig>())
                {
                    yield return new PromptTemplateReferenceCandidate(
                        $"section:{channelConfig.PromptChannel}:{section.SectionId}",
                        $"Prompt Section / {channelConfig.PromptChannel} / {section.SectionId}",
                        section.Content ?? string.Empty);
                }
            }

            foreach (UserDefinedPromptVariableConfig variable in GetVariables(settings))
            {
                string key = NormalizeKey(variable?.Key);
                yield return new PromptTemplateReferenceCandidate(
                    $"custom:{key}:default",
                    $"Custom Variable / {key} / default",
                    variable?.TemplateText ?? string.Empty);

                foreach (FactionScopedPromptVariableOverrideConfig entry in GetOverridesForKey(key, settings))
                {
                    yield return new PromptTemplateReferenceCandidate(
                        $"custom:{key}:override:{entry.FactionDefName}",
                        $"Custom Variable / {key} / {entry.FactionDefName}",
                        entry.TemplateText ?? string.Empty);
                }
            }
        }

        private sealed class PromptTemplateReferenceCandidate
        {
            public PromptTemplateReferenceCandidate(string locationId, string displayText, string templateText)
            {
                LocationId = locationId ?? string.Empty;
                DisplayText = displayText ?? string.Empty;
                TemplateText = templateText ?? string.Empty;
            }

            public string LocationId { get; }
            public string DisplayText { get; }
            public string TemplateText { get; }
        }
    }
}
