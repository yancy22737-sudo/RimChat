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
        private static readonly IReadOnlyList<PromptRuntimeVariableDefinition> Definitions = BuildDefinitions();
        private static readonly IReadOnlyDictionary<string, PromptRuntimeVariableDefinition> DefinitionMap =
            Definitions.ToDictionary(item => item.Path, item => item, StringComparer.OrdinalIgnoreCase);
        private static readonly IReadOnlyList<string> Paths =
            Definitions.Select(item => item.Path).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList();

        public static IReadOnlyList<PromptRuntimeVariableDefinition> GetDefinitions()
        {
            return Definitions;
        }

        public static IReadOnlyCollection<string> GetPaths()
        {
            return Paths;
        }

        public static bool Contains(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && DefinitionMap.ContainsKey(path.Trim());
        }

        public static PromptRuntimeVariableDefinition Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            DefinitionMap.TryGetValue(path.Trim(), out PromptRuntimeVariableDefinition definition);
            return definition;
        }

        public static List<IPromptRuntimeVariableProvider> CreateRuntimeProviders(
            Func<string, PromptRuntimeVariableContext, object> coreResolver)
        {
            return new List<IPromptRuntimeVariableProvider>
            {
                new RimChatCoreVariableProvider(coreResolver),
                new RimTalkVariableProvider(),
                new RimTalkMemoryPatchVariableProvider()
            };
        }

        private static IReadOnlyList<PromptRuntimeVariableDefinition> BuildDefinitions()
        {
            var ordered = new List<PromptRuntimeVariableDefinition>();
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddDefinitions(ordered, unique, new RimChatCoreVariableProvider(null).GetDefinitions());
            AddDefinitions(ordered, unique, new RimTalkVariableProvider().GetDefinitions());
            AddDefinitions(ordered, unique, new RimTalkMemoryPatchVariableProvider().GetDefinitions());
            return ordered;
        }

        private static void AddDefinitions(
            ICollection<PromptRuntimeVariableDefinition> target,
            ISet<string> unique,
            IEnumerable<PromptRuntimeVariableDefinition> definitions)
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

                target.Add(definition);
            }
        }
    }
}
