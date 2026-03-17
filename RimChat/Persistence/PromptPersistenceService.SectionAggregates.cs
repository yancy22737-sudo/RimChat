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
        private PromptHierarchyNode BuildMainChainPromptSectionNode(
            RimTalkPromptChannel rootChannel,
            SystemPromptConfig config,
            DialogueScenarioContext context,
            EnvironmentPromptConfig environmentConfig)
        {
            string promptChannel = PromptSectionSchemaCatalog.ResolveRuntimePromptChannel(
                rootChannel,
                context?.IsProactive == true);
            return BuildPromptSectionAggregateNode(config, promptChannel, context, environmentConfig);
        }

        private PromptHierarchyNode BuildPromptSectionAggregateNode(
            SystemPromptConfig config,
            string promptChannel,
            DialogueScenarioContext context,
            EnvironmentPromptConfig environmentConfig)
        {
            RimTalkPromptEntryDefaultsConfig catalog = GetRuntimePromptSectionCatalog(config);
            PromptSectionAggregate aggregate = PromptSectionAggregateBuilder.Build(
                catalog,
                promptChannel,
                (sectionId, template) => RenderPromptSectionAggregateSection(promptChannel, sectionId, template, context, environmentConfig));

            var root = new PromptHierarchyNode("main_prompt_sections");
            for (int i = 0; i < aggregate.Sections.Count; i++)
            {
                PromptSectionAggregateSection section = aggregate.Sections[i];
                if (section == null || string.IsNullOrWhiteSpace(section.Content))
                {
                    continue;
                }

                root.AddChild(section.SectionId, section.Content.Trim());
            }

            return root.Children.Count > 0 ? root : null;
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
            var root = new PromptHierarchyNode("main_prompt_sections");
            for (int i = 0; i < aggregate.Sections.Count; i++)
            {
                PromptSectionAggregateSection section = aggregate.Sections[i];
                if (section == null || string.IsNullOrWhiteSpace(section.Content))
                {
                    continue;
                }

                root.AddChild(section.SectionId, section.Content.Trim());
            }

            return PromptHierarchyRenderer.Render(root);
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

        private RimTalkPromptEntryDefaultsConfig GetRuntimePromptSectionCatalog(SystemPromptConfig config)
        {
            return RimChatMod.Settings?.GetPromptSectionCatalogClone()
                ?? RimTalkPromptEntryDefaultsProvider.GetDefaultsSnapshot();
        }


        private bool SyncLegacyPromptMirrorsFromSections(SystemPromptConfig config)
        {
            if (config == null)
            {
                return false;
            }

            string systemMirror = BuildLegacyPromptMirrorText(
                RimTalkPromptEntryChannelCatalog.DiplomacyDialogue,
                "system_rules",
                "action_rules",
                "output_specification");
            string dialogueMirror = BuildLegacyPromptMirrorText(
                RimTalkPromptEntryChannelCatalog.DiplomacyDialogue,
                "character_persona",
                "memory_system",
                "environment_perception",
                "context",
                "repetition_reinforcement");

            bool changed = false;
            if (!string.Equals(config.GlobalSystemPrompt ?? string.Empty, systemMirror, StringComparison.Ordinal))
            {
                config.GlobalSystemPrompt = systemMirror;
                changed = true;
            }

            if (!string.Equals(config.GlobalDialoguePrompt ?? string.Empty, dialogueMirror, StringComparison.Ordinal))
            {
                config.GlobalDialoguePrompt = dialogueMirror;
                changed = true;
            }

            config.UseHierarchicalPromptFormat = true;
            return changed;
        }

        private string BuildLegacyPromptMirrorText(string promptChannel, params string[] sectionIds)
        {
            RimTalkPromptEntryDefaultsConfig catalog = RimChatMod.Settings?.GetPromptSectionCatalogClone()
                                                   ?? RimTalkPromptEntryDefaultsProvider.GetDefaultsSnapshot();
            var parts = new List<string>();
            for (int i = 0; i < sectionIds.Length; i++)
            {
                string text = catalog.ResolveContent(promptChannel, sectionIds[i])?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(text);
                }
            }

            return string.Join("\n\n", parts).Trim();
        }
    }
}
