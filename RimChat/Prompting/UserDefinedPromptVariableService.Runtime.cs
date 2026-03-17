using System;
using System.Collections.Generic;
using RimChat.Config;
using RimChat.Persistence;
using Verse;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: prompt rendering services and runtime prompt contexts.
    /// Responsibility: render `system.custom.*` values and apply the effective pawn personality export chain.
    /// </summary>
    internal static partial class UserDefinedPromptVariableService
    {
        public static void PopulateRuntimeValues(IDictionary<string, object> values, PromptRuntimeVariableContext context)
        {
            if (values == null)
            {
                return;
            }

            Dictionary<string, string> cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Stack<string> resolving = new Stack<string>();
            foreach (UserDefinedPromptVariableConfig item in GetVariables())
            {
                string path = BuildPath(item?.Key);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                values[path] = ResolveVariableValue(path, values, context, cache, resolving);
            }

            ApplyEffectivePawnPersonality(values, context, cache, resolving);
        }

        private static string ResolveVariableValue(
            string path,
            IDictionary<string, object> values,
            PromptRuntimeVariableContext context,
            IDictionary<string, string> cache,
            Stack<string> resolving)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            if (cache.TryGetValue(path, out string cached))
            {
                return cached ?? string.Empty;
            }

            if (resolving.Contains(path))
            {
                Log.Warning($"[RimChat] Detected recursive custom variable render cycle at {path}.");
                cache[path] = string.Empty;
                return string.Empty;
            }

            resolving.Push(path);
            string rendered = string.Empty;
            try
            {
                UserDefinedPromptVariableConfig variable = FindVariableByPath(path);
                if (variable == null || !variable.Enabled)
                {
                    rendered = string.Empty;
                }
                else
                {
                    UserDefinedPromptVariableRuleMatcher.ResolvedRule rule = UserDefinedPromptVariableRuleMatcher.ResolveRule(
                        variable,
                        GetFactionRulesForKey(variable.Key),
                        GetPawnRulesForKey(variable.Key),
                        context);
                    string template = rule?.TemplateText ?? variable.DefaultTemplateText ?? string.Empty;
                    rendered = RenderTemplate(template, values, context, cache, resolving);
                }
            }
            catch (PromptRenderException ex)
            {
                Log.Warning($"[RimChat] Failed to render custom variable {path}: {ex.Message}");
                rendered = string.Empty;
            }
            finally
            {
                resolving.Pop();
            }

            cache[path] = rendered ?? string.Empty;
            return rendered ?? string.Empty;
        }

        private static string RenderTemplate(
            string templateText,
            IDictionary<string, object> values,
            PromptRuntimeVariableContext context,
            IDictionary<string, string> cache,
            Stack<string> resolving)
        {
            if (string.IsNullOrWhiteSpace(templateText))
            {
                return string.Empty;
            }

            TemplateVariableValidationResult validation = PromptPersistenceService.Instance.ValidateTemplateVariables(templateText);
            foreach (string dependency in validation.UsedVariables)
            {
                if (!IsUserDefinedPath(dependency))
                {
                    continue;
                }

                values[dependency] = ResolveVariableValue(dependency, values, context, cache, resolving);
            }

            PromptRenderContext renderContext = PromptRenderContext.Create(
                context?.TemplateId ?? "custom.variable",
                context?.Channel ?? "runtime");
            renderContext.SetValues(new Dictionary<string, object>(values, StringComparer.OrdinalIgnoreCase));
            return PromptTemplateRenderer.RenderOrThrow(
                context?.TemplateId ?? "custom.variable",
                context?.Channel ?? "runtime",
                templateText,
                renderContext);
        }

        private static void ApplyEffectivePawnPersonality(
            IDictionary<string, object> values,
            PromptRuntimeVariableContext context,
            IDictionary<string, string> cache,
            Stack<string> resolving)
        {
            if (values == null)
            {
                return;
            }

            string raw = values.TryGetValue("pawn.personality", out object rawValue)
                ? rawValue?.ToString() ?? string.Empty
                : string.Empty;
            string overridePath = BuildPath("pawn_personality_override");
            string appendPath = BuildPath("pawn_personality_append");
            string overrideText = ResolveOptionalVariableValue(overridePath, values, context, cache, resolving);
            string appendText = ResolveOptionalVariableValue(appendPath, values, context, cache, resolving);

            string effective = string.IsNullOrWhiteSpace(overrideText) ? raw : overrideText.Trim();
            if (!string.IsNullOrWhiteSpace(appendText))
            {
                effective = string.IsNullOrWhiteSpace(effective)
                    ? appendText.Trim()
                    : effective + "\n" + appendText.Trim();
            }

            values["pawn.personality"] = effective ?? string.Empty;
        }

        private static string ResolveOptionalVariableValue(
            string path,
            IDictionary<string, object> values,
            PromptRuntimeVariableContext context,
            IDictionary<string, string> cache,
            Stack<string> resolving)
        {
            if (!IsUserDefinedPath(path))
            {
                return string.Empty;
            }

            UserDefinedPromptVariableConfig variable = FindVariableByPath(path);
            if (variable == null || !variable.Enabled)
            {
                return string.Empty;
            }

            string rendered = ResolveVariableValue(path, values, context, cache, resolving);
            values[path] = rendered;
            return rendered;
        }
    }
}
