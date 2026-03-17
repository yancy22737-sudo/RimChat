using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimChat.AI;
using RimChat.Config;
using RimChat.Core;
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
            IReadOnlyDictionary<string, object> variables = BuildVariables(seed);
            string promptChannel = RimTalkPromptEntryChannelCatalog.SocialCirclePost;

            return new List<ChatMessageData>
            {
                new ChatMessageData
                {
                    role = "system",
                    content = RenderTemplate(
                        "prompt_templates.social_news_style",
                        ResolveUnifiedNode(promptChannel, "social_news_style"),
                        PromptTextConstants.SocialCircleNewsStyleTemplateDefault,
                        variables)
                },
                new ChatMessageData
                {
                    role = "system",
                    content = RenderTemplate(
                        "prompt_templates.social_news_json_contract",
                        ResolveUnifiedNode(promptChannel, "social_news_json_contract"),
                        PromptTextConstants.SocialCircleNewsJsonContractTemplateDefault,
                        variables)
                },
                new ChatMessageData
                {
                    role = "user",
                    content = RenderTemplate(
                        "prompt_templates.social_news_fact",
                        ResolveUnifiedNode(promptChannel, "social_news_fact"),
                        PromptTextConstants.SocialCircleNewsFactTemplateDefault,
                        variables)
                }
            };
        }

        private static string ResolveUnifiedNode(string promptChannel, string nodeId)
        {
            return RimChatMod.Settings?.ResolvePromptNodeText(promptChannel, nodeId) ?? string.Empty;
        }

        private static IReadOnlyDictionary<string, object> BuildVariables(SocialNewsSeed seed)
        {
            return new Dictionary<string, object>
            {
                ["world.social.origin_type"] = seed?.OriginType.ToString() ?? "Unknown",
                ["world.social.category"] = SocialCircleService.GetCategoryLabel(seed?.Category ?? SocialPostCategory.Diplomatic),
                ["world.social.source_faction"] = seed?.SourceFaction?.Name ?? "None",
                ["world.social.target_faction"] = seed?.TargetFaction?.Name ?? "None",
                ["dialogue.summary"] = seed?.Summary ?? string.Empty,
                ["dialogue.intent_hint"] = seed?.IntentHint ?? string.Empty,
                ["world.social.source_label"] = SocialCircleService.ResolveDisplayLabel(seed?.SourceLabel),
                ["world.social.credibility_label"] = SocialCircleService.ResolveDisplayLabel(seed?.CredibilityLabel),
                ["world.social.credibility_value"] = (seed?.CredibilityValue ?? 0.6f).ToString("F2", CultureInfo.InvariantCulture),
                ["world.social.fact_lines"] = BuildFactLines(seed),
                ["system.game_language"] = LanguageDatabase.activeLanguage?.FriendlyNameNative ?? "English"
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
            string templateId,
            string template,
            string fallback,
            IReadOnlyDictionary<string, object> variables)
        {
            string resolved = string.IsNullOrWhiteSpace(template) ? fallback : template;
            PromptRenderContext context = PromptRenderContext.Create(templateId, "social");
            context.SetValues(variables);
            return PromptTemplateRenderer.RenderOrThrow(templateId, "social", resolved, context);
        }
    }
}
