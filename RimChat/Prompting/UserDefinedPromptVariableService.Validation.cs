using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Config;
using RimChat.Persistence;
using RimWorld;
using Verse;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: prompt validation service and RimWorld defs.
    /// Responsibility: validate, save, and dependency-check unified custom-variable rule edits.
    /// </summary>
    internal static partial class UserDefinedPromptVariableService
    {
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
            if (string.IsNullOrWhiteSpace(normalizedKey) || !IsValidKey(normalizedKey))
            {
                result.Errors.Add("RimChat_CustomVariableValidation_InvalidKey".Translate().ToString());
                return result;
            }

            bool keyTaken = GetVariables(settings).Any(item =>
                item != null &&
                !string.Equals(item.Id ?? string.Empty, originalVariable?.Id ?? string.Empty, StringComparison.Ordinal) &&
                string.Equals(NormalizeKey(item.Key), normalizedKey, StringComparison.Ordinal));
            if (keyTaken)
            {
                result.Errors.Add("RimChat_CustomVariableValidation_DuplicateKey".Translate(normalizedKey).ToString());
            }

            bool pathConflict = PromptRuntimeVariableRegistry.ContainsReservedPath(
                path,
                originalVariable == null ? string.Empty : BuildPath(originalVariable.Key));
            if (pathConflict)
            {
                result.Errors.Add("RimChat_CustomVariableValidation_PathConflict".Translate(path).ToString());
            }

            ValidateTemplate(result, settings, "default", variable.DefaultTemplateText, path, originalVariable);
            ValidateFactionRules(result, settings, editModel, normalizedKey, path, originalVariable);
            ValidatePawnRules(result, settings, editModel, normalizedKey, path, originalVariable);
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

            settings.UserDefinedPromptVariableFactionRules.RemoveAll(item =>
                item != null &&
                string.Equals(NormalizeKey(item.VariableKey), NormalizeKey(target.Key), StringComparison.OrdinalIgnoreCase));
            foreach (FactionPromptVariableRuleConfig rule in editModel.FactionRules.Where(item => item != null))
            {
                FactionPromptVariableRuleConfig clone = rule.Clone();
                clone.VariableKey = target.Key;
                settings.UserDefinedPromptVariableFactionRules.Add(clone);
            }

            settings.UserDefinedPromptVariablePawnRules.RemoveAll(item =>
                item != null &&
                string.Equals(NormalizeKey(item.VariableKey), NormalizeKey(target.Key), StringComparison.OrdinalIgnoreCase));
            foreach (PawnPromptVariableRuleConfig rule in editModel.PawnRules.Where(item => item != null))
            {
                PawnPromptVariableRuleConfig clone = rule.Clone();
                clone.VariableKey = target.Key;
                settings.UserDefinedPromptVariablePawnRules.Add(clone);
            }

            NormalizeSettingsCollections(settings);
            return true;
        }

        private static void ValidateFactionRules(
            UserDefinedPromptVariableValidationResult result,
            RimChat.Config.RimChatSettings settings,
            UserDefinedPromptVariableEditModel editModel,
            string normalizedKey,
            string currentPath,
            UserDefinedPromptVariableConfig originalVariable)
        {
            int fallbackOrder = 0;
            foreach (FactionPromptVariableRuleConfig rule in editModel?.FactionRules ?? Enumerable.Empty<FactionPromptVariableRuleConfig>())
            {
                if (rule == null)
                {
                    continue;
                }

                rule.VariableKey = normalizedKey;
                rule.Order = rule.Order >= 0 ? rule.Order : fallbackOrder;
                if (string.IsNullOrWhiteSpace(rule.FactionDefName) ||
                    DefDatabase<FactionDef>.GetNamedSilentFail(rule.FactionDefName) == null)
                {
                    result.Errors.Add("RimChat_CustomVariableValidation_InvalidFaction".Translate(rule.FactionDefName ?? string.Empty).ToString());
                }

                ValidateTemplate(
                    result,
                    settings,
                    $"faction:{rule.Order}:{rule.FactionDefName}",
                    rule.TemplateText,
                    currentPath,
                    originalVariable);
                fallbackOrder++;
            }
        }

        private static void ValidatePawnRules(
            UserDefinedPromptVariableValidationResult result,
            RimChat.Config.RimChatSettings settings,
            UserDefinedPromptVariableEditModel editModel,
            string normalizedKey,
            string currentPath,
            UserDefinedPromptVariableConfig originalVariable)
        {
            int fallbackOrder = 0;
            foreach (PawnPromptVariableRuleConfig rule in editModel?.PawnRules ?? Enumerable.Empty<PawnPromptVariableRuleConfig>())
            {
                if (rule == null)
                {
                    continue;
                }

                rule.VariableKey = normalizedKey;
                rule.Order = rule.Order >= 0 ? rule.Order : fallbackOrder;
                rule.NameExact = UserDefinedPromptVariableRuleMatcher.NormalizePawnName(rule.NameExact);
                rule.TraitsAny = UserDefinedPromptVariableRuleMatcher.NormalizeValues(rule.TraitsAny);
                rule.TraitsAll = UserDefinedPromptVariableRuleMatcher.NormalizeValues(rule.TraitsAll);
                rule.PlayerControlled = NormalizeBoolToken(rule.PlayerControlled);

                ValidatePawnRuleConditions(result, rule);
                ValidateTemplate(
                    result,
                    settings,
                    $"pawn:{rule.Order}",
                    rule.TemplateText,
                    currentPath,
                    originalVariable);
                fallbackOrder++;
            }
        }

        private static void ValidatePawnRuleConditions(UserDefinedPromptVariableValidationResult result, PawnPromptVariableRuleConfig rule)
        {
            if (!string.IsNullOrWhiteSpace(rule.FactionDefName) &&
                DefDatabase<FactionDef>.GetNamedSilentFail(rule.FactionDefName) == null)
            {
                result.Errors.Add("RimChat_CustomVariableValidation_InvalidFaction".Translate(rule.FactionDefName).ToString());
            }

            if (!string.IsNullOrWhiteSpace(rule.RaceDefName) &&
                DefDatabase<ThingDef>.GetNamedSilentFail(rule.RaceDefName) == null)
            {
                result.Errors.Add("RimChat_CustomVariableValidation_InvalidRace".Translate(rule.RaceDefName).ToString());
            }

            if (!string.IsNullOrWhiteSpace(rule.XenotypeDefName) &&
                DefDatabase<XenotypeDef>.GetNamedSilentFail(rule.XenotypeDefName) == null)
            {
                result.Errors.Add("RimChat_CustomVariableValidation_InvalidXenotype".Translate(rule.XenotypeDefName).ToString());
            }

            if (!string.IsNullOrWhiteSpace(rule.Gender) &&
                !Enum.GetNames(typeof(Gender)).Any(item => string.Equals(item, rule.Gender, StringComparison.OrdinalIgnoreCase)))
            {
                result.Errors.Add("RimChat_CustomVariableValidation_InvalidGender".Translate(rule.Gender).ToString());
            }

            if (!string.IsNullOrWhiteSpace(rule.AgeStage) &&
                DefDatabase<LifeStageDef>.GetNamedSilentFail(rule.AgeStage) == null)
            {
                result.Errors.Add("RimChat_CustomVariableValidation_InvalidAgeStage".Translate(rule.AgeStage).ToString());
            }

            foreach (string trait in rule.TraitsAny.Concat(rule.TraitsAll).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (DefDatabase<TraitDef>.GetNamedSilentFail(trait) == null)
                {
                    result.Errors.Add("RimChat_CustomVariableValidation_InvalidTrait".Translate(trait).ToString());
                }
            }

            if (!string.IsNullOrWhiteSpace(rule.PlayerControlled) &&
                !string.Equals(rule.PlayerControlled, "true", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(rule.PlayerControlled, "false", StringComparison.OrdinalIgnoreCase))
            {
                result.Errors.Add("RimChat_CustomVariableValidation_InvalidPlayerControlled".Translate(rule.PlayerControlled).ToString());
            }
        }

        private static void ApplyVariable(UserDefinedPromptVariableConfig target, UserDefinedPromptVariableConfig source)
        {
            target.Id = string.IsNullOrWhiteSpace(source?.Id) ? Guid.NewGuid().ToString("N") : source.Id.Trim();
            target.Key = NormalizeKey(source?.Key);
            target.DisplayName = source?.DisplayName?.Trim() ?? string.Empty;
            target.Description = source?.Description?.Trim() ?? string.Empty;
            target.DefaultTemplateText = source?.DefaultTemplateText ?? string.Empty;
            target.Enabled = source?.Enabled ?? true;
        }

        private static void ValidateTemplate(
            UserDefinedPromptVariableValidationResult result,
            RimChat.Config.RimChatSettings settings,
            string templateId,
            string templateText,
            string currentPath,
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

                string key = NormalizeKey(item.Key);
                AddDependencies(graph, key, item.DefaultTemplateText);
                foreach (FactionPromptVariableRuleConfig rule in GetFactionRulesForKey(item.Key, settings))
                {
                    AddDependencies(graph, key, rule.TemplateText);
                }

                foreach (PawnPromptVariableRuleConfig rule in GetPawnRulesForKey(item.Key, settings))
                {
                    AddDependencies(graph, key, rule.TemplateText);
                }
            }

            if (editModel?.Variable != null)
            {
                string key = NormalizeKey(editModel.Variable.Key);
                AddDependencies(graph, key, editModel.Variable.DefaultTemplateText);
                foreach (FactionPromptVariableRuleConfig rule in editModel.FactionRules ?? Enumerable.Empty<FactionPromptVariableRuleConfig>())
                {
                    AddDependencies(graph, key, rule?.TemplateText);
                }

                foreach (PawnPromptVariableRuleConfig rule in editModel.PawnRules ?? Enumerable.Empty<PawnPromptVariableRuleConfig>())
                {
                    AddDependencies(graph, key, rule?.TemplateText);
                }
            }

            return graph;
        }
    }
}
