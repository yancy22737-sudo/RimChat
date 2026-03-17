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
    /// Dependencies: RimChat settings, prompt validation service, and RimWorld defs.
    /// Responsibility: own normalized CRUD, validation, migration, runtime rendering, and editor metadata for user-defined prompt variables.
    /// </summary>
    internal static partial class UserDefinedPromptVariableService
    {
        public const string NamespaceRoot = "system.custom";
        private const string SourceId = "rimchat.user";
        private const string SourceLabel = "User Variable";

        private static readonly string[] SuggestedKeys =
        {
            "pawn_personality_override",
            "pawn_personality_append",
            "faction_tone",
            "faction_attitude_text",
            "pawn_speaking_style",
            "relationship_flavor"
        };

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

        public static IReadOnlyList<FactionPromptVariableRuleConfig> GetFactionRules(RimChat.Config.RimChatSettings settings = null)
        {
            RimChat.Config.RimChatSettings resolved = settings ?? RimChatMod.Settings;
            return resolved?.UserDefinedPromptVariableFactionRules != null
                ? resolved.UserDefinedPromptVariableFactionRules
                : (IReadOnlyList<FactionPromptVariableRuleConfig>)Array.Empty<FactionPromptVariableRuleConfig>();
        }

        public static IReadOnlyList<PawnPromptVariableRuleConfig> GetPawnRules(RimChat.Config.RimChatSettings settings = null)
        {
            RimChat.Config.RimChatSettings resolved = settings ?? RimChatMod.Settings;
            return resolved?.UserDefinedPromptVariablePawnRules != null
                ? resolved.UserDefinedPromptVariablePawnRules
                : (IReadOnlyList<PawnPromptVariableRuleConfig>)Array.Empty<PawnPromptVariableRuleConfig>();
        }

        public static IReadOnlyList<FactionScopedPromptVariableOverrideConfig> GetLegacyOverrides(RimChat.Config.RimChatSettings settings = null)
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
                item != null &&
                string.Equals(NormalizeKey(item.Key), normalized, StringComparison.Ordinal));
        }

        public static List<FactionPromptVariableRuleConfig> GetFactionRulesForKey(string key, RimChat.Config.RimChatSettings settings = null)
        {
            string normalized = NormalizeKey(key);
            return GetFactionRules(settings)
                .Where(item => item != null && string.Equals(NormalizeKey(item.VariableKey), normalized, StringComparison.Ordinal))
                .Select(item => item.Clone())
                .ToList();
        }

        public static List<PawnPromptVariableRuleConfig> GetPawnRulesForKey(string key, RimChat.Config.RimChatSettings settings = null)
        {
            string normalized = NormalizeKey(key);
            return GetPawnRules(settings)
                .Where(item => item != null && string.Equals(NormalizeKey(item.VariableKey), normalized, StringComparison.Ordinal))
                .Select(item => item.Clone())
                .ToList();
        }

        public static PromptRuntimeVariableDefinition BuildDefinition(UserDefinedPromptVariableConfig config)
        {
            string path = BuildPath(config?.Key);
            string description = BuildDefinitionDescription(
                config,
                GetFactionRulesForKey(config?.Key),
                GetPawnRulesForKey(config?.Key));
            return new PromptRuntimeVariableDefinition(path, SourceId, SourceLabel, description, true);
        }

        public static string BuildDefinitionDescription(
            UserDefinedPromptVariableConfig config,
            IReadOnlyCollection<FactionPromptVariableRuleConfig> factionRules,
            IReadOnlyCollection<PawnPromptVariableRuleConfig> pawnRules)
        {
            if (config == null)
            {
                return string.Empty;
            }

            string displayName = string.IsNullOrWhiteSpace(config.DisplayName)
                ? BuildPath(config.Key)
                : config.DisplayName.Trim();
            string stateText = config.Enabled ? "enabled" : "disabled";
            string summary = string.IsNullOrWhiteSpace(config.Description) ? displayName : config.Description.Trim();
            int enabledFactionRules = factionRules?.Count(item => item != null && item.Enabled) ?? 0;
            int enabledPawnRules = pawnRules?.Count(item => item != null && item.Enabled) ?? 0;
            return $"{summary} ({stateText}, faction rules: {enabledFactionRules}, pawn rules: {enabledPawnRules})";
        }

        public static PromptVariableTooltipInfo BuildTooltipInfo(string path)
        {
            UserDefinedPromptVariableConfig config = FindVariableByPath(path);
            if (config == null)
            {
                return null;
            }

            List<FactionPromptVariableRuleConfig> factionRules = GetFactionRulesForKey(config.Key);
            List<PawnPromptVariableRuleConfig> pawnRules = GetPawnRulesForKey(config.Key);
            List<string> typicalValues = new List<string>();
            if (!string.IsNullOrWhiteSpace(config.DefaultTemplateText))
            {
                typicalValues.Add(config.DefaultTemplateText.Trim());
            }

            foreach (FactionPromptVariableRuleConfig rule in factionRules.Where(item => item != null && item.Enabled))
            {
                if (string.IsNullOrWhiteSpace(rule.TemplateText))
                {
                    continue;
                }

                typicalValues.Add($"{UserDefinedPromptVariableRuleMatcher.BuildLayerLabel(UserDefinedPromptVariableRuleMatcher.RuleLayer.Faction)}: {rule.FactionDefName} -> {UserDefinedPromptVariableRuleMatcher.BuildTemplateSummary(rule.TemplateText)}");
                if (typicalValues.Count >= 4)
                {
                    break;
                }
            }

            foreach (PawnPromptVariableRuleConfig rule in pawnRules.Where(item => item != null && item.Enabled))
            {
                if (string.IsNullOrWhiteSpace(rule.TemplateText))
                {
                    continue;
                }

                UserDefinedPromptVariableRuleMatcher.RuleLayer layer = string.IsNullOrWhiteSpace(rule.NameExact)
                    ? UserDefinedPromptVariableRuleMatcher.RuleLayer.PawnConditional
                    : UserDefinedPromptVariableRuleMatcher.RuleLayer.PawnExact;
                typicalValues.Add($"{UserDefinedPromptVariableRuleMatcher.BuildLayerLabel(layer)}: {UserDefinedPromptVariableRuleMatcher.BuildTemplateSummary(rule.TemplateText)}");
                if (typicalValues.Count >= 4)
                {
                    break;
                }
            }

            string name = string.IsNullOrWhiteSpace(config.DisplayName)
                ? BuildPath(config.Key)
                : config.DisplayName.Trim();
            string description = BuildDefinitionDescription(config, factionRules, pawnRules);
            return new PromptVariableTooltipInfo(name, "Scriban text", description, typicalValues);
        }

        public static void NormalizeSettingsCollections(RimChat.Config.RimChatSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.UserDefinedPromptVariables ??= new List<UserDefinedPromptVariableConfig>();
            settings.UserDefinedPromptVariableFactionRules ??= new List<FactionPromptVariableRuleConfig>();
            settings.UserDefinedPromptVariablePawnRules ??= new List<PawnPromptVariableRuleConfig>();
            settings.FactionScopedPromptVariableOverrides ??= new List<FactionScopedPromptVariableOverrideConfig>();

            NormalizeVariables(settings);
            MigrateLegacyOverrides(settings);
            NormalizeFactionRules(settings);
            NormalizePawnRules(settings);
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
            settings.UserDefinedPromptVariableFactionRules.RemoveAll(item =>
                item != null && string.Equals(NormalizeKey(item.VariableKey), normalizedKey, StringComparison.OrdinalIgnoreCase));
            settings.UserDefinedPromptVariablePawnRules.RemoveAll(item =>
                item != null && string.Equals(NormalizeKey(item.VariableKey), normalizedKey, StringComparison.OrdinalIgnoreCase));
            NormalizeSettingsCollections(settings);
            return true;
        }

        public static IEnumerable<string> GetSuggestedKeys()
        {
            return SuggestedKeys;
        }

        public static UserDefinedPromptVariableEditModel CreateSuggestedModel(string key)
        {
            string normalized = NormalizeKey(key);
            var model = new UserDefinedPromptVariableEditModel();
            model.Variable.Key = normalized;
            model.Variable.DisplayName = BuildPath(normalized);
            model.Variable.Description = BuildSuggestedDescription(normalized);
            model.Variable.DefaultTemplateText = BuildSuggestedTemplate(normalized);
            return model;
        }

        private static void NormalizeVariables(RimChat.Config.RimChatSettings settings)
        {
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
                item.DefaultTemplateText = item.DefaultTemplateText ?? string.Empty;
                if (string.IsNullOrWhiteSpace(item.Key) || !seenVariableKeys.Add(item.Key))
                {
                    continue;
                }

                normalizedVariables.Add(item);
            }

            settings.UserDefinedPromptVariables = normalizedVariables;
        }

        private static void MigrateLegacyOverrides(RimChat.Config.RimChatSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.UserDefinedPromptVariableFactionRules ??= new List<FactionPromptVariableRuleConfig>();
            foreach (FactionScopedPromptVariableOverrideConfig legacy in GetLegacyOverrides(settings))
            {
                if (legacy == null)
                {
                    continue;
                }

                string variableKey = NormalizeKey(legacy.VariableKey);
                if (string.IsNullOrWhiteSpace(variableKey) ||
                    FindVariableByKey(variableKey, settings) == null ||
                    string.IsNullOrWhiteSpace(legacy.FactionDefName))
                {
                    continue;
                }

                bool exists = settings.UserDefinedPromptVariableFactionRules.Any(item =>
                    item != null &&
                    string.Equals(NormalizeKey(item.VariableKey), variableKey, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.FactionDefName, legacy.FactionDefName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.TemplateText ?? string.Empty, legacy.TemplateText ?? string.Empty, StringComparison.Ordinal) &&
                    item.Priority == 0);
                if (exists)
                {
                    continue;
                }

                settings.UserDefinedPromptVariableFactionRules.Add(new FactionPromptVariableRuleConfig
                {
                    Id = string.IsNullOrWhiteSpace(legacy.Id) ? Guid.NewGuid().ToString("N") : legacy.Id.Trim(),
                    VariableKey = variableKey,
                    FactionDefName = legacy.FactionDefName?.Trim() ?? string.Empty,
                    Priority = 0,
                    TemplateText = legacy.TemplateText ?? string.Empty,
                    Enabled = legacy.Enabled,
                    Order = settings.UserDefinedPromptVariableFactionRules.Count
                });
            }

            settings.FactionScopedPromptVariableOverrides = new List<FactionScopedPromptVariableOverrideConfig>();
        }

        private static void NormalizeFactionRules(RimChat.Config.RimChatSettings settings)
        {
            var normalizedRules = new List<FactionPromptVariableRuleConfig>();
            int order = 0;
            foreach (FactionPromptVariableRuleConfig item in settings.UserDefinedPromptVariableFactionRules)
            {
                if (item == null)
                {
                    continue;
                }

                item.Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id.Trim();
                item.VariableKey = NormalizeKey(item.VariableKey);
                item.FactionDefName = item.FactionDefName?.Trim() ?? string.Empty;
                item.TemplateText = item.TemplateText ?? string.Empty;
                item.Order = item.Order >= 0 ? item.Order : order;
                if (string.IsNullOrWhiteSpace(item.VariableKey) ||
                    string.IsNullOrWhiteSpace(item.FactionDefName) ||
                    FindVariableByKey(item.VariableKey, settings) == null)
                {
                    continue;
                }

                normalizedRules.Add(item);
                order++;
            }

            settings.UserDefinedPromptVariableFactionRules = normalizedRules
                .OrderBy(item => item.Order)
                .ToList();
        }

        private static void NormalizePawnRules(RimChat.Config.RimChatSettings settings)
        {
            var normalizedRules = new List<PawnPromptVariableRuleConfig>();
            int order = 0;
            foreach (PawnPromptVariableRuleConfig item in settings.UserDefinedPromptVariablePawnRules)
            {
                if (item == null)
                {
                    continue;
                }

                item.Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id.Trim();
                item.VariableKey = NormalizeKey(item.VariableKey);
                item.NameExact = UserDefinedPromptVariableRuleMatcher.NormalizePawnName(item.NameExact);
                item.FactionDefName = item.FactionDefName?.Trim() ?? string.Empty;
                item.RaceDefName = item.RaceDefName?.Trim() ?? string.Empty;
                item.Gender = item.Gender?.Trim() ?? string.Empty;
                item.AgeStage = item.AgeStage?.Trim() ?? string.Empty;
                item.TraitsAny = UserDefinedPromptVariableRuleMatcher.NormalizeValues(item.TraitsAny);
                item.TraitsAll = UserDefinedPromptVariableRuleMatcher.NormalizeValues(item.TraitsAll);
                item.XenotypeDefName = item.XenotypeDefName?.Trim() ?? string.Empty;
                item.PlayerControlled = NormalizeBoolToken(item.PlayerControlled);
                item.TemplateText = item.TemplateText ?? string.Empty;
                item.Order = item.Order >= 0 ? item.Order : order;
                if (string.IsNullOrWhiteSpace(item.VariableKey) ||
                    FindVariableByKey(item.VariableKey, settings) == null)
                {
                    continue;
                }

                normalizedRules.Add(item);
                order++;
            }

            settings.UserDefinedPromptVariablePawnRules = normalizedRules
                .OrderBy(item => item.Order)
                .ToList();
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
                    variable?.DefaultTemplateText ?? string.Empty);

                foreach (FactionPromptVariableRuleConfig rule in GetFactionRulesForKey(key, settings))
                {
                    yield return new PromptTemplateReferenceCandidate(
                        $"custom:{key}:faction:{rule.Order}",
                        $"Custom Variable / {key} / faction:{rule.FactionDefName}",
                        rule.TemplateText ?? string.Empty);
                }

                foreach (PawnPromptVariableRuleConfig rule in GetPawnRulesForKey(key, settings))
                {
                    yield return new PromptTemplateReferenceCandidate(
                        $"custom:{key}:pawn:{rule.Order}",
                        $"Custom Variable / {key} / pawn",
                        rule.TemplateText ?? string.Empty);
                }
            }
        }

        private static string NormalizeBoolToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value.Trim().ToLowerInvariant();
            return normalized == "true" || normalized == "false"
                ? normalized
                : string.Empty;
        }

        private static string BuildSuggestedDescription(string key)
        {
            switch (NormalizeKey(key))
            {
                case "pawn_personality_override":
                    return "RimChat_CustomVariableSuggestedDescription_PawnPersonalityOverride".Translate().ToString();
                case "pawn_personality_append":
                    return "RimChat_CustomVariableSuggestedDescription_PawnPersonalityAppend".Translate().ToString();
                case "faction_tone":
                    return "RimChat_CustomVariableSuggestedDescription_FactionTone".Translate().ToString();
                case "faction_attitude_text":
                    return "RimChat_CustomVariableSuggestedDescription_FactionAttitude".Translate().ToString();
                case "pawn_speaking_style":
                    return "RimChat_CustomVariableSuggestedDescription_PawnSpeakingStyle".Translate().ToString();
                case "relationship_flavor":
                    return "RimChat_CustomVariableSuggestedDescription_RelationshipFlavor".Translate().ToString();
                default:
                    return string.Empty;
            }
        }

        private static string BuildSuggestedTemplate(string key)
        {
            switch (NormalizeKey(key))
            {
                case "pawn_personality_override":
                    return "{{ pawn.personality }}";
                case "pawn_personality_append":
                    return string.Empty;
                case "faction_tone":
                    return "{{ world.faction.name }} should sound measured, goal-oriented, and consistent with faction culture.";
                case "faction_attitude_text":
                    return "Attitude toward the player should reflect current diplomacy, recent actions, and strategic needs.";
                case "pawn_speaking_style":
                    return "Keep wording short, natural, and aligned with the pawn's current personality.";
                case "relationship_flavor":
                    return "Reflect relationship warmth, distance, trust, or tension through tone and word choice.";
                default:
                    return string.Empty;
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
