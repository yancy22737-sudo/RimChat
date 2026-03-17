using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Config;
using RimChat.Persistence;
using Verse;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: user-defined prompt variable service, prompt renderer, and current runtime value dictionary.
    /// Responsibility: expose `system.custom.*` variables and resolve their values with faction-scoped overrides.
    /// </summary>
    internal sealed class UserDefinedVariableProvider : IPromptRuntimeVariableProvider
    {
        private readonly Func<string, PromptRuntimeVariableContext, object> _resolver;

        public UserDefinedVariableProvider(Func<string, PromptRuntimeVariableContext, object> resolver)
        {
            _resolver = resolver;
        }

        public string SourceId => UserDefinedPromptVariableService.GetSourceId();
        public string SourceLabel => UserDefinedPromptVariableService.GetSourceLabel();

        public bool IsAvailable(PromptRuntimeVariableContext context)
        {
            return true;
        }

        public IReadOnlyList<PromptRuntimeVariableDefinition> GetDefinitions()
        {
            return UserDefinedPromptVariableService.GetVariables()
                .Where(item => item != null)
                .Select(UserDefinedPromptVariableService.BuildDefinition)
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Path))
                .ToList();
        }

        public void PopulateValues(IDictionary<string, object> values, PromptRuntimeVariableContext context)
        {
            if (values == null)
            {
                return;
            }

            Dictionary<string, string> cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (UserDefinedPromptVariableConfig item in UserDefinedPromptVariableService.GetVariables())
            {
                string path = UserDefinedPromptVariableService.BuildPath(item?.Key);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                values[path] = ResolveVariableValue(path, values, context, cache, new Stack<string>());
            }
        }

        public bool TryMapLegacyToken(string token, out string namespacedPath)
        {
            namespacedPath = string.Empty;
            return false;
        }

        private string ResolveVariableValue(
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
                UserDefinedPromptVariableConfig variable = UserDefinedPromptVariableService.FindVariableByPath(path);
                if (variable == null || !variable.Enabled)
                {
                    rendered = string.Empty;
                }
                else
                {
                    string factionDefName = context?.ScenarioContext?.Faction?.def?.defName ?? string.Empty;
                    FactionScopedPromptVariableOverrideConfig match = UserDefinedPromptVariableService.GetOverridesForKey(variable.Key)
                        .FirstOrDefault(item =>
                            item != null &&
                            item.Enabled &&
                            string.Equals(item.FactionDefName, factionDefName, StringComparison.OrdinalIgnoreCase));
                    string template = match?.TemplateText ?? variable.TemplateText ?? string.Empty;
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

        private string RenderTemplate(
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
                if (!UserDefinedPromptVariableService.IsUserDefinedPath(dependency))
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
    }
}
