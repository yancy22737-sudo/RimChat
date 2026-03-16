using System;
using System.Collections.Generic;
using RimChat.Config;
using RimChat.Core;
using RimChat.Prompting;
using Verse;

namespace RimChat.Persistence
{
    /// <summary>
    /// Dependencies: prompt section aggregate builder, prompt render pipeline, and runtime prompt variable context.
    /// Responsibility: render canonical PromptSectionCatalog aggregates for diplomacy and RPG main-chain prompts.
    /// </summary>
    public partial class PromptPersistenceService
    {
        private string BuildMainChainPromptSectionAggregate(
            RimTalkPromptChannel rootChannel,
            DialogueScenarioContext context,
            EnvironmentPromptConfig environmentConfig)
        {
            string promptChannel = PromptSectionSchemaCatalog.ResolveRuntimePromptChannel(
                rootChannel,
                context?.IsProactive == true);
            RimTalkPromptEntryDefaultsConfig catalog = RimChatMod.Settings?.GetPromptSectionCatalogClone()
                                                   ?? RimTalkPromptEntryDefaultsProvider.GetDefaultsSnapshot();
            PromptSectionAggregate aggregate = PromptSectionAggregateBuilder.Build(
                catalog,
                promptChannel,
                (sectionId, template) => RenderPromptSectionAggregateSection(promptChannel, sectionId, template, context, environmentConfig));
            return aggregate.RenderedText ?? string.Empty;
        }

        internal string BuildPromptSectionAggregatePreview(RimTalkPromptChannel rootChannel, string promptChannel)
        {
            string normalizedChannel = RimTalkPromptEntryChannelCatalog.NormalizeForRoot(promptChannel, rootChannel);
            RimTalkPromptEntryDefaultsConfig catalog = RimChatMod.Settings?.GetPromptSectionCatalogClone()
                                                   ?? RimTalkPromptEntryDefaultsProvider.GetDefaultsSnapshot();
            PromptSectionAggregate aggregate = PromptSectionAggregateBuilder.Build(
                catalog,
                normalizedChannel,
                (_, template) => template);
            return aggregate.RenderedText ?? string.Empty;
        }

        private string RenderPromptSectionAggregateSection(
            string promptChannel,
            string sectionId,
            string templateText,
            DialogueScenarioContext context,
            EnvironmentPromptConfig environmentConfig)
        {
            string normalized = templateText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            string renderChannel = context?.IsRpg == true ? "rpg" : "diplomacy";
            string templateId = $"prompt_sections.{promptChannel}.{sectionId}";
            Dictionary<string, object> values = BuildTemplateVariableValues(
                templateId,
                renderChannel,
                context,
                environmentConfig);
            PromptRenderContext renderContext = PromptRenderContext.Create(templateId, renderChannel);
            renderContext.SetValues(values);
            return PromptTemplateRenderer.RenderOrThrow(templateId, renderChannel, normalized, renderContext).Trim();
        }
    }
}
