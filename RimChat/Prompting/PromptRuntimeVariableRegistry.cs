using System;
using System.Collections.Generic;
using System.Linq;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: prompt runtime variable providers.
    /// Responsibility: aggregate prompt variable metadata across core and external providers.
    /// </summary>
    internal static class PromptRuntimeVariableRegistry
    {
        private static readonly Func<Func<string, PromptRuntimeVariableContext, object>, IPromptRuntimeVariableProvider>[] ProviderFactories =
        {
            resolver => new RimChatCoreVariableProvider(resolver),
            _ => new RimTalkVariableProvider(),
            _ => new RimTalkMemoryPatchVariableProvider(),
            resolver => new UserDefinedVariableProvider(resolver)
        };

        public static IReadOnlyList<PromptRuntimeVariableDefinition> GetDefinitions()
        {
            return BuildDefinitions();
        }

        public static IReadOnlyCollection<string> GetPaths()
        {
            return BuildDefinitions()
                .Select(item => item.Path)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static bool Contains(string path)
        {
            return Resolve(path) != null;
        }

        public static bool ContainsReservedPath(string path, string exemptPath = "")
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string normalized = path.Trim();
            string exempt = string.IsNullOrWhiteSpace(exemptPath) ? string.Empty : exemptPath.Trim();
            foreach (PromptRuntimeVariableDefinition definition in BuildDefinitions())
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.Path))
                {
                    continue;
                }

                if (string.Equals(definition.Path, exempt, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(definition.Path, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static PromptRuntimeVariableDefinition Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            IReadOnlyDictionary<string, PromptRuntimeVariableDefinition> definitionMap = BuildDefinitions()
                .ToDictionary(item => item.Path, item => item, StringComparer.OrdinalIgnoreCase);
            definitionMap.TryGetValue(path.Trim(), out PromptRuntimeVariableDefinition definition);
            return definition;
        }

        public static string ResolveLegacyToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return string.Empty;
            }

            List<IPromptRuntimeVariableProvider> providers = CreateRuntimeProviders(null);
            for (int i = 0; i < providers.Count; i++)
            {
                IPromptRuntimeVariableProvider provider = providers[i];
                if (provider != null &&
                    provider.TryMapLegacyToken(token.Trim(), out string namespacedPath) &&
                    !string.IsNullOrWhiteSpace(namespacedPath))
                {
                    return namespacedPath;
                }
            }

            return string.Empty;
        }

        public static List<IPromptRuntimeVariableProvider> CreateRuntimeProviders(
            Func<string, PromptRuntimeVariableContext, object> coreResolver)
        {
            var providers = new List<IPromptRuntimeVariableProvider>(ProviderFactories.Length);
            for (int i = 0; i < ProviderFactories.Length; i++)
            {
                Func<Func<string, PromptRuntimeVariableContext, object>, IPromptRuntimeVariableProvider> factory = ProviderFactories[i];
                IPromptRuntimeVariableProvider provider = factory?.Invoke(coreResolver);
                if (provider != null)
                {
                    providers.Add(provider);
                }
            }

            return providers;
        }

        private static IReadOnlyList<PromptRuntimeVariableDefinition> BuildDefinitions()
        {
            PromptRuntimeVariableContext metadataContext = new PromptRuntimeVariableContext("catalog", "editor", null, null);
            List<IPromptRuntimeVariableProvider> providers = CreateRuntimeProviders(null);
            var ordered = new List<PromptRuntimeVariableDefinition>();
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < providers.Count; i++)
            {
                IPromptRuntimeVariableProvider provider = providers[i];
                if (provider == null)
                {
                    continue;
                }

                bool isAvailable = provider.IsAvailable(metadataContext);
                AddDefinitions(ordered, unique, provider.GetDefinitions(), isAvailable);
            }

            return ordered;
        }

        private static void AddDefinitions(
            ICollection<PromptRuntimeVariableDefinition> target,
            ISet<string> unique,
            IEnumerable<PromptRuntimeVariableDefinition> definitions,
            bool isAvailable)
        {
            if (target == null || unique == null || definitions == null)
            {
                return;
            }

            foreach (PromptRuntimeVariableDefinition definition in definitions)
            {
                if (definition == null ||
                    string.IsNullOrWhiteSpace(definition.Path) ||
                    !unique.Add(definition.Path))
                {
                    continue;
                }

                target.Add(definition.WithAvailability(isAvailable));
            }
        }
    }
}
