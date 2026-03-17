using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Config;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: user-defined prompt variable service, prompt renderer, and current runtime value dictionary.
    /// Responsibility: expose `system.custom.*` variables and delegate unified rule resolution to the shared service.
    /// </summary>
    internal sealed class UserDefinedVariableProvider : IPromptRuntimeVariableProvider
    {
        public UserDefinedVariableProvider(Func<string, PromptRuntimeVariableContext, object> resolver)
        {
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
            UserDefinedPromptVariableService.PopulateRuntimeValues(values, context);
        }

        public bool TryMapLegacyToken(string token, out string namespacedPath)
        {
            namespacedPath = string.Empty;
            return false;
        }

    }
}
