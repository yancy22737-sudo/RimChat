using RimChat.Config;
using RimChat.Persistence;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: prompt persistence variable picker models and runtime prompt contexts.
    /// Responsibility: describe one canonical namespaced prompt variable and its provider source.
    /// </summary>
    internal sealed class PromptRuntimeVariableDefinition
    {
        public PromptRuntimeVariableDefinition(
            string path,
            string sourceId,
            string sourceLabel,
            string descriptionKey)
        {
            Path = path ?? string.Empty;
            SourceId = sourceId ?? string.Empty;
            SourceLabel = sourceLabel ?? string.Empty;
            DescriptionKey = descriptionKey ?? string.Empty;
        }

        public string Path { get; }
        public string SourceId { get; }
        public string SourceLabel { get; }
        public string DescriptionKey { get; }

        public PromptTemplateVariableDefinition ToTemplateDefinition()
        {
            return new PromptTemplateVariableDefinition(Path, DescriptionKey);
        }
    }

    /// <summary>
    /// Dependencies: scenario and environment prompt config models.
    /// Responsibility: provide one immutable runtime context to prompt variable providers.
    /// </summary>
    internal sealed class PromptRuntimeVariableContext
    {
        public PromptRuntimeVariableContext(
            string templateId,
            string channel,
            DialogueScenarioContext scenarioContext,
            EnvironmentPromptConfig environmentConfig)
        {
            TemplateId = templateId ?? string.Empty;
            Channel = channel ?? string.Empty;
            ScenarioContext = scenarioContext;
            EnvironmentConfig = environmentConfig;
        }

        public string TemplateId { get; }
        public string Channel { get; }
        public DialogueScenarioContext ScenarioContext { get; }
        public EnvironmentPromptConfig EnvironmentConfig { get; }
    }
}
