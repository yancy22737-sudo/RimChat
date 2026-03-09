using System.Collections.Generic;
using System.Text;
using RimChat.Config;
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
            sb.AppendLine(RenderTemplate(config.FullTryGainMemoryLineTemplate, examples, string.Empty));

            for (int i = 0; i < config.SharedActionLines.Count; i++)
            {
                sb.AppendLine(config.SharedActionLines[i]);
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
            string actionNames = string.Join(", ", config.CompactActionNames);
            string examples = BuildTryGainMemoryExamples();

            sb.AppendLine(config.CompactHeader);
            sb.AppendLine(config.CompactIntro);
            sb.AppendLine(RenderTemplate(config.CompactAllowedActionsTemplate, string.Empty, actionNames));
            sb.AppendLine(RenderTemplate(config.CompactTryGainMemoryTemplate, examples, actionNames));
            sb.AppendLine(config.CompactActionFieldsHint);
            sb.AppendLine(config.CompactClosureGuidance);
            sb.AppendLine();
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

        private static string RenderTemplate(string template, string examples, string actionNames)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return string.Empty;
            }

            return template
                .Replace("{{examples}}", examples ?? string.Empty)
                .Replace("{{action_names}}", actionNames ?? string.Empty);
        }

        private static string BuildTryGainMemoryExamples()
        {
            string[] preferred =
            {
                "Chitchat", "DeepTalk", "KindWords", "Slighted", "Insulted", "AteWithoutTable",
                "SleepDisturbed", "SleptOutside", "SleptInCold", "SleptInHeat", "GotSomeLovin", "Catharsis"
            };

            var names = new List<string>();
            for (int i = 0; i < preferred.Length; i++)
            {
                string defName = preferred[i];
                if (DefDatabase<ThoughtDef>.GetNamedSilentFail(defName) != null)
                {
                    names.Add(defName);
                }
            }

            return names.Count > 0 ? string.Join(", ", names) : "Chitchat, DeepTalk, KindWords, Insulted";
        }
    }
}
