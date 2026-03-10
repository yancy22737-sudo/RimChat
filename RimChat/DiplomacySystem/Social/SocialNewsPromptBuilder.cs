using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimChat.AI;
using RimChat.Config;
using RimChat.Persistence;
using RimChat.Prompting;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>/// Dependencies: prompt persistence, prompt renderer, AI chat message DTOs.
 /// Responsibility: build LLM prompts for one fact-grounded social-circle news seed.
 ///</summary>
    internal static class SocialNewsPromptBuilder
    {
        public static List<ChatMessageData> BuildMessages(SocialNewsSeed seed)
        {
            PromptTemplateTextConfig templates = PromptPersistenceService.Instance
                .LoadConfig()
                ?.PromptTemplates ?? new PromptTemplateTextConfig();
            IReadOnlyDictionary<string, string> variables = BuildVariables(seed);

            return new List<ChatMessageData>
            {
                new ChatMessageData
                {
                    role = "system",
                    content = RenderTemplate(
                        templates.SocialCircleNewsStyleTemplate,
                        PromptTextConstants.SocialCircleNewsStyleTemplateDefault,
                        variables)
                },
                new ChatMessageData
                {
                    role = "system",
                    content = RenderTemplate(
                        templates.SocialCircleNewsJsonContractTemplate,
                        PromptTextConstants.SocialCircleNewsJsonContractTemplateDefault,
                        variables)
                },
                new ChatMessageData
                {
                    role = "user",
                    content = RenderTemplate(
                        templates.SocialCircleNewsFactTemplate,
                        PromptTextConstants.SocialCircleNewsFactTemplateDefault,
                        variables)
                }
            };
        }

        private static IReadOnlyDictionary<string, string> BuildVariables(SocialNewsSeed seed)
        {
            return new Dictionary<string, string>
            {
                ["origin_type"] = seed?.OriginType.ToString() ?? "Unknown",
                ["category"] = SocialCircleService.GetCategoryLabel(seed?.Category ?? SocialPostCategory.Diplomatic),
                ["source_faction"] = seed?.SourceFaction?.Name ?? "None",
                ["target_faction"] = seed?.TargetFaction?.Name ?? "None",
                ["summary"] = seed?.Summary ?? string.Empty,
                ["intent_hint"] = seed?.IntentHint ?? string.Empty,
                ["source_label"] = SocialCircleService.ResolveDisplayLabel(seed?.SourceLabel),
                ["credibility_label"] = SocialCircleService.ResolveDisplayLabel(seed?.CredibilityLabel),
                ["credibility_value"] = (seed?.CredibilityValue ?? 0.6f).ToString("F2", CultureInfo.InvariantCulture),
                ["fact_lines"] = BuildFactLines(seed),
                ["game_language"] = LanguageDatabase.activeLanguage?.FriendlyNameNative ?? "English"
            };
        }

        private static string BuildFactLines(SocialNewsSeed seed)
        {
            List<string> facts = seed?.Facts ?? new List<string>();
            if (facts.Count == 0)
            {
                return "- none";
            }

            return string.Join("\n", facts
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => "- " + item.Trim()));
        }

        private static string RenderTemplate(
            string template,
            string fallback,
            IReadOnlyDictionary<string, string> variables)
        {
            string resolved = string.IsNullOrWhiteSpace(template) ? fallback : template;
            return PromptTemplateRenderer.Render(resolved, variables);
        }
    }
}
