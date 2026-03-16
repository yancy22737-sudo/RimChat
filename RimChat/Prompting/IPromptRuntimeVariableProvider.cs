using System.Collections.Generic;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: prompt runtime variable metadata and resolution context.
    /// Responsibility: expose one prompt-variable source as metadata + optional runtime values.
    /// </summary>
    internal interface IPromptRuntimeVariableProvider
    {
        string SourceId { get; }
        string SourceLabel { get; }

        bool IsAvailable(PromptRuntimeVariableContext context);

        IReadOnlyList<PromptRuntimeVariableDefinition> GetDefinitions();

        void PopulateValues(
            IDictionary<string, object> values,
            PromptRuntimeVariableContext context);

        bool TryMapLegacyToken(string token, out string namespacedPath);
    }
}
