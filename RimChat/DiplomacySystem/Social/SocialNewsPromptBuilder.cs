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
        private static readonly string[] NarrativeModes =
        {
            "scene_report",
            "rumor_wire",
            "war_dispatch",
            "personal_chronicle"
        };

        public static List<ChatMessageData> BuildMessages(SocialNewsSeed seed)
        {
            var variables = new Dictionary<string, object>(System.StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, object> entry in BuildVariables(seed))
            {
                variables[entry.Key] = entry.Value ?? string.Empty;
            }

            variables["dialogue.primary_objective"] = "Generate one social-circle post from the input fact seed.";
            variables["dialogue.optional_followup"] = "Keep it vivid, compact, world-grounded, and structurally varied.";
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
            string sourceFaction = BuildFactionPromptDescriptor(seed?.SourceFaction, "None");
            string targetFaction = BuildFactionPromptDescriptor(seed?.TargetFaction, "None");
            return new Dictionary<string, object>
            {
                ["world.social.origin_type"] = seed?.OriginType.ToString() ?? "Unknown",
                ["world.social.category"] = SocialCircleService.GetCategoryLabel(seed?.Category ?? SocialPostCategory.Diplomatic),
                ["world.social.source_faction"] = sourceFaction,
                ["world.social.target_faction"] = targetFaction,
                ["dialogue.summary"] = seed?.Summary ?? string.Empty,
                ["dialogue.intent_hint"] = seed?.IntentHint ?? string.Empty,
                ["world.social.source_label"] = SocialCircleService.ResolveDisplayLabel(seed?.SourceLabel),
                ["world.social.credibility_label"] = SocialCircleService.ResolveDisplayLabel(seed?.CredibilityLabel),
                ["world.social.credibility_value"] = (seed?.CredibilityValue ?? 0.6f).ToString("F2", CultureInfo.InvariantCulture),
                ["world.social.fact_lines"] = BuildFactLines(seed),
                ["world.social.primary_claim"] = seed?.PrimaryClaim ?? string.Empty,
                ["world.social.quote_attribution_hint"] = seed?.QuoteAttributionHint ?? string.Empty,
                ["world.social.narrative_mode"] = PickNarrativeMode(seed),
                ["world.social.style_constraints"] = BuildStyleConstraints(),
                ["world.social.quote_guardrails"] = BuildQuoteGuardrails(seed),
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

        internal static string BuildPromptInputPayloadForDebug(SocialNewsSeed seed)
        {
            return BuildPromptInputPayload(seed);
        }

        private static string BuildPromptInputPayload(SocialNewsSeed seed)
        {
            string origin = seed?.OriginType.ToString() ?? "Unknown";
            string category = SocialCircleService.GetCategoryLabel(seed?.Category ?? SocialPostCategory.Diplomatic);
            string summary = seed?.Summary ?? string.Empty;
            string intent = seed?.IntentHint ?? string.Empty;
            string source = BuildFactionPromptDescriptor(seed?.SourceFaction, "None");
            string target = BuildFactionPromptDescriptor(seed?.TargetFaction, "None");
            string credibility = SocialCircleService.ResolveDisplayLabel(seed?.CredibilityLabel);
            string facts = BuildFactLines(seed);
            string narrativeMode = PickNarrativeMode(seed);
            string primaryClaim = seed?.PrimaryClaim ?? string.Empty;
            string quoteAttributionHint = seed?.QuoteAttributionHint ?? string.Empty;
            return "origin=" + origin + "\n"
                + "category=" + category + "\n"
                + "source_faction=" + source + "\n"
                + "target_faction=" + target + "\n"
                + "credibility=" + credibility + "\n"
                + "narrative_mode=" + narrativeMode + "\n"
                + "primary_claim=" + primaryClaim + "\n"
                + "quote_attribution_hint=" + quoteAttributionHint + "\n"
                + "quote_guardrails=" + BuildQuoteGuardrails(seed) + "\n"
                + "summary=" + summary + "\n"
                + "intent_hint=" + intent + "\n"
                + "facts:\n" + facts;
        }

        private static string PickNarrativeMode(SocialNewsSeed seed)
        {
            if (seed == null)
            {
                return NarrativeModes[0];
            }

            int hash = GenText.StableStringHash(seed.OriginKey ?? string.Empty);
            int tickBucket = seed.OccurredTick / 60000;
            int index = System.Math.Abs(hash + tickBucket) % NarrativeModes.Length;
            return NarrativeModes[index];
        }

        private static string BuildStyleConstraints()
        {
            return "Avoid formulaic transitions and repeated sentence openings. "
                + "Prefer concrete sensory or situational detail over abstract summary. "
                + "Allow light hearsay, witness angle, or field-observer texture without adding new facts. "
                + "Let each section carry new information instead of restating the previous one. "
                + "When a faction is named, naturally reflect its technological level, political temperament, or social identity from the provided facts. "
                + "When a concrete stronghold or settlement is provided, mention it in the body as part of the lived scene rather than dropping it as a detached label.";
        }

        private static string BuildQuoteGuardrails(SocialNewsSeed seed)
        {
            string sourceLabel = SocialCircleService.ResolveDisplayLabel(seed?.SourceLabel);
            return string.IsNullOrWhiteSpace(sourceLabel)
                ? "Quote must sound like an in-world speaker. Never paste source labels, channel names, metadata tags, or attribution boilerplate into quote text."
                : "Quote must sound like an in-world speaker. Never paste source labels, channel names, metadata tags, or attribution boilerplate into quote text. Keep source metadata separate from the quote. Current source label: " + sourceLabel;
        }

        private static string BuildFactionPromptDescriptor(Faction faction, string fallback)
        {
            if (faction == null)
            {
                return fallback;
            }

            string name = string.IsNullOrWhiteSpace(faction.Name) ? fallback : faction.Name;
            string kind = faction.def?.label ?? faction.def?.defName ?? string.Empty;
            string tech = faction.def == null ? string.Empty : faction.def.techLevel.ToString();
            if (string.IsNullOrWhiteSpace(kind) && string.IsNullOrWhiteSpace(tech))
            {
                return name;
            }

            if (string.IsNullOrWhiteSpace(kind))
            {
                return name + " (tech level: " + tech + ")";
            }

            if (string.IsNullOrWhiteSpace(tech))
            {
                return name + " (" + kind + ")";
            }

            return name + " (" + kind + ", tech level: " + tech + ")";
        }
    }
}
