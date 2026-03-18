using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimChat.AI;
using RimChat.Config;
using RimChat.Core;
using RimChat.Persistence;
using RimWorld;
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
            var variables = new Dictionary<string, object>(System.StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, object> entry in BuildVariables(seed))
            {
                variables[entry.Key] = entry.Value ?? string.Empty;
            }

            variables["dialogue.primary_objective"] = "Generate one social-circle post from the input fact seed.";
            variables["dialogue.optional_followup"] = "Keep output compact and world-grounded.";
            variables["dialogue.latest_unresolved_intent"] = string.Empty;
            Faction faction = seed?.SourceFaction ?? seed?.TargetFaction;
            DialogueScenarioContext context = DialogueScenarioContext.CreateDiplomacy(
                faction,
                false,
                new[] { "channel:social_circle_post", "scene:social" });
            string systemPrompt = PromptPersistenceService.Instance.BuildUnifiedChannelSystemPrompt(
                RimTalkPromptChannel.Diplomacy,
                RimTalkPromptEntryChannelCatalog.SocialCirclePost,
                context,
                null,
                variables,
                "social_news_input",
                BuildPromptInputPayload(seed));
            return new List<ChatMessageData>
            {
                new ChatMessageData
                {
                    role = "system",
                    content = systemPrompt
                }
            };
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

        private static string BuildPromptInputPayload(SocialNewsSeed seed)
        {
            string origin = seed?.OriginType.ToString() ?? "Unknown";
            string category = SocialCircleService.GetCategoryLabel(seed?.Category ?? SocialPostCategory.Diplomatic);
            string summary = seed?.Summary ?? string.Empty;
            string intent = seed?.IntentHint ?? string.Empty;
            string source = seed?.SourceFaction?.Name ?? "None";
            string target = seed?.TargetFaction?.Name ?? "None";
            string credibility = SocialCircleService.ResolveDisplayLabel(seed?.CredibilityLabel);
            string facts = BuildFactLines(seed);
            return "origin=" + origin + "\n"
                + "category=" + category + "\n"
                + "source_faction=" + source + "\n"
                + "target_faction=" + target + "\n"
                + "credibility=" + credibility + "\n"
                + "summary=" + summary + "\n"
                + "intent_hint=" + intent + "\n"
                + "facts:\n" + facts;
        }
    }
}
