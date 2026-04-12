using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimChat.Config;
using RimChat.Memory;
using RimWorld;
using Verse;

namespace RimChat.Prompting
{
    /// <summary>/// Responsibility: build the shared RPG API action-definition prompt block for runtime injection and UI preview.
    /// Dependencies: RimWorld DefDatabase (ThoughtDef), Verse runtime.
 ///</summary>
    internal static class RpgApiPromptTextBuilder
    {
        public static void AppendActionDefinitions(StringBuilder sb, RpgApiActionPromptConfig overrideConfig = null)
        {
            if (sb == null)
            {
                return;
            }

            RpgApiActionPromptConfig config = ResolveConfig(overrideConfig);
            string examples = BuildTryGainMemoryExamples();

            sb.AppendLine(config.FullHeader);
            sb.AppendLine(config.FullIntro);
            sb.AppendLine(config.FullActionObjectHint);
            sb.AppendLine(config.FullActionReliabilityGuidance);
            sb.AppendLine(config.FullClosureReliabilityGuidance);
            sb.AppendLine();
            sb.AppendLine(RenderTemplate(
                "prompt_templates.rpg_api.full_try_gain_memory",
                config.FullTryGainMemoryLineTemplate,
                examples,
                string.Empty));

            for (int i = 0; i < config.SharedActionLines.Count; i++)
            {
                string line = config.SharedActionLines[i];
                if (IsLineExcluded(line, config.ExcludeActionNames))
                {
                    continue;
                }

                sb.AppendLine(line);
            }

            sb.AppendLine();
        }

        public static void AppendActionDefinitionsCompact(StringBuilder sb, RpgApiActionPromptConfig overrideConfig = null)
        {
            if (sb == null)
            {
                return;
            }

            RpgApiActionPromptConfig config = ResolveConfig(overrideConfig);
            List<string> filteredNames = FilterExcludedNames(config.CompactActionNames, config.ExcludeActionNames);
            string actionNames = string.Join(", ", filteredNames);
            string examples = BuildTryGainMemoryExamples();

            sb.AppendLine(config.CompactHeader);
            sb.AppendLine(config.CompactIntro);
            sb.AppendLine(RenderTemplate(
                "prompt_templates.rpg_api.compact_allowed_actions",
                config.CompactAllowedActionsTemplate,
                string.Empty,
                actionNames));
            sb.AppendLine(RenderTemplate(
                "prompt_templates.rpg_api.compact_try_gain_memory",
                config.CompactTryGainMemoryTemplate,
                examples,
                actionNames));
            sb.AppendLine(config.CompactActionFieldsHint);
            sb.AppendLine(config.CompactClosureGuidance);
            sb.AppendLine();
        }

        /// <summary>
        /// Check if a SharedActionLine starts with an excluded action name.
        /// Lines like "- ReduceResistance: ..." match when "ReduceResistance" is excluded.
        /// </summary>
        private static bool IsLineExcluded(string line, HashSet<string> excludeNames)
        {
            if (string.IsNullOrWhiteSpace(line) || excludeNames == null || excludeNames.Count == 0)
            {
                return false;
            }

            // Extract action name from lines like "- ActionName: description"
            string trimmed = line.TrimStart(' ', '-', '\t');
            int colonIndex = trimmed.IndexOf(':');
            if (colonIndex <= 0)
            {
                return false;
            }

            string actionName = trimmed.Substring(0, colonIndex).Trim();
            return excludeNames.Contains(actionName);
        }

        /// <summary>
        /// Filter out excluded action names from the compact name list.
        /// </summary>
        private static List<string> FilterExcludedNames(List<string> names, HashSet<string> excludeNames)
        {
            if (names == null || excludeNames == null || excludeNames.Count == 0)
            {
                return names ?? new List<string>();
            }

            return names.Where(name => !excludeNames.Contains(name)).ToList();
        }

        private static RpgApiActionPromptConfig ResolveConfig(RpgApiActionPromptConfig overrideConfig)
        {
            RpgApiActionPromptConfig config = overrideConfig?.Clone()
                ?? RpgPromptDefaultsProvider.GetDefaults().ApiActionPrompt?.Clone()
                ?? RpgApiActionPromptConfig.CreateFallback();

            config.SharedActionLines ??= new List<string>();
            config.CompactActionNames ??= new List<string>();
            return config;
        }

        private static string RenderTemplate(
            string templateId,
            string template,
            string examples,
            string actionNames)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return string.Empty;
            }

            PromptRenderContext context = PromptRenderContext.Create(templateId, "rpg");
            context.SetValue("dialogue.examples", examples ?? string.Empty);
            context.SetValue("dialogue.action_names", actionNames ?? string.Empty);
            return PromptTemplateRenderer.RenderOrThrow(templateId, "rpg", template, context);
        }

        private static string BuildTryGainMemoryExamples()
        {
            return RpgMemoryCatalog.BuildPromptExamplesTextWithFallback("KindWordsMood, InsultedMood");
        }
    }
}
