using System.Collections.Generic;

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
    }
}
