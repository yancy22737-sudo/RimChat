using System;
using System.IO;
using RimChat.Persistence;
using UnityEngine;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: social-circle prompt domain DTOs, prompt-domain file catalog, and Unity JSON loader.
    /// Responsibility: load default social-circle templates and action metadata from Prompt/Default/SocialCirclePrompt_Default.json.
    /// </summary>
    internal static class SocialCirclePromptDefaultsProvider
    {
        private static readonly object SyncRoot = new object();
        private static string _cachedPath = string.Empty;
        private static DateTime _cachedWriteTimeUtc = DateTime.MinValue;
        private static SocialCirclePromptDomainConfig _cachedConfig;

        internal static SocialCirclePromptDomainConfig GetDefaults()
        {
            lock (SyncRoot)
            {
                string path = PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.SocialCirclePromptDefaultFileName);
                if (TryGetCached(path, out SocialCirclePromptDomainConfig cached))
                {
                    return Clone(cached);
                }

                SocialCirclePromptDomainConfig loaded = Load(path) ?? CreateFallback();
                _cachedPath = path;
                _cachedWriteTimeUtc = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                _cachedConfig = loaded;
                return Clone(loaded);
            }
        }

        private static bool TryGetCached(string path, out SocialCirclePromptDomainConfig config)
        {
            config = null;
            if (_cachedConfig == null || !string.Equals(_cachedPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!File.Exists(path))
            {
                config = _cachedConfig;
                return true;
            }

            DateTime writeTime = File.GetLastWriteTimeUtc(path);
            if (writeTime != _cachedWriteTimeUtc)
            {
                return false;
            }

            config = _cachedConfig;
            return true;
        }

        private static SocialCirclePromptDomainConfig Load(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return null;
                }

                string json = File.ReadAllText(path);
                SocialCirclePromptDomainConfig loaded = JsonUtility.FromJson<SocialCirclePromptDomainConfig>(json);
                return Normalize(loaded);
            }
            catch
            {
                return null;
            }
        }

        private static SocialCirclePromptDomainConfig Normalize(SocialCirclePromptDomainConfig config)
        {
            config ??= new SocialCirclePromptDomainConfig();
            config.SocialCircleActionRuleTemplate = config.SocialCircleActionRuleTemplate ?? string.Empty;
            config.SocialCircleNewsStyleTemplate = config.SocialCircleNewsStyleTemplate ?? string.Empty;
            config.SocialCircleNewsJsonContractTemplate = config.SocialCircleNewsJsonContractTemplate ?? string.Empty;
            config.SocialCircleNewsFactTemplate = config.SocialCircleNewsFactTemplate ?? string.Empty;
            config.PublishPublicPostAction ??= new ApiActionConfig("publish_public_post", string.Empty, string.Empty, string.Empty);
            return config;
        }

        private static SocialCirclePromptDomainConfig Clone(SocialCirclePromptDomainConfig config)
        {
            SocialCirclePromptDomainConfig normalized = Normalize(config);
            return new SocialCirclePromptDomainConfig
            {
                SocialCircleActionRuleTemplate = normalized.SocialCircleActionRuleTemplate,
                SocialCircleNewsStyleTemplate = normalized.SocialCircleNewsStyleTemplate,
                SocialCircleNewsJsonContractTemplate = normalized.SocialCircleNewsJsonContractTemplate,
                SocialCircleNewsFactTemplate = normalized.SocialCircleNewsFactTemplate,
                PublishPublicPostAction = normalized.PublishPublicPostAction?.Clone()
                    ?? new ApiActionConfig("publish_public_post", string.Empty, string.Empty, string.Empty)
            };
        }

        private static SocialCirclePromptDomainConfig CreateFallback()
        {
            return new SocialCirclePromptDomainConfig
            {
                SocialCircleActionRuleTemplate =
                    "Social circle rules: use publish_public_post only for public statements that should be seen by all factions and the player; keep category and sentiment consistent with the current diplomacy stance; do not use it for private negotiation details or routine chatter; use it sparingly.",
                SocialCircleNewsStyleTemplate =
                    "You are writing one RimWorld world-news card for the social circle.\n"
                    + "Voice: a circulated world-event short piece rather than a bullet-point report. Let it read as continuous narrative with scene, tension, aftermath, and an observer's voice; it may carry the texture of a historical note, battlefield account, tavern rumor, lordly bulletin, or a private record later made public.\n"
                    + "Grounding: use only the supplied facts. You may adjust order, emphasis, tone, and connective phrasing, and add restrained expansion, but do not invent new events, participants, motives, outcomes, or numbers.\n"
                    + "Writing rule: avoid template voice, summary voice, and explanatory voice. Do not restate the same idea in slightly different wording. Keep third-person exposition a little leaner, and let quotes, witness-like lines, or public statements carry more immediacy and first-person flavor. Environmental detail may be strengthened, especially at openings and turns, with small touches of season, weather, light, camp, settlement, or battlefield detail, but it must not overpower the event itself.\n"
                    + "Narrative mode: {{ world.social.narrative_mode }}. You may reference {{ world.social.style_constraints }}.\n"
                    + "The player should feel that this is not a system notice, but an event already circulating through the world and leaving a wake behind it.\n"
                    + "Category: {{ world.social.category }}.\n"
                    + "Source: {{ world.social.source_label }}.\n"
                    + "Credibility: {{ world.social.credibility_label }} ({{ world.social.credibility_value }}).\n"
                    + "Write in the current game language: {{ system.game_language }}.",
                SocialCircleNewsJsonContractTemplate =
                    "Return exactly one JSON object only.\n"
                    + "The first character must be '{' and the last character must be '}'.\n"
                    + "Do not output markdown fences, prose, notes, or extra keys.\n"
                    + "Required keys:\n"
                    + "- headline, lead, cause, process, outlook.\n"
                    + "Optional keys:\n"
                    + "- quote, quote_attribution, narrative_mode, location_name.\n"
                    + "Each value must be a JSON string.\n"
                    + "headline: a world-event title that is short, precise, and has a hook without sounding bureaucratic.\n"
                    + "lead: the opening of the body; bring the reader directly into the scene or shift in situation.\n"
                    + "cause: continue the body naturally; do not write it as 'Cause:'. Explain the spark, background, or pressure behind the event.\n"
                    + "process: continue the body and show how the news spread, how the situation fermented, how onlookers discussed it, or how parties reacted.\n"
                    + "outlook: close the body with the aftermath, suspense, retaliation, fear, expectation, or next development.\n"
                    + "The four body fields must read as one connected short article rather than four parallel notes.\n"
                    + "quote: strongly encouraged, and it should usually run 2-3 sentences. Prefer using it to carry first-person feeling, stance, emotion, and witness-like immediacy. When the facts are thin, it may sound a little more human and lightly emotional, but it must not introduce a new conclusion.\n"
                    + "quote_attribution: if quote is not empty, this must be filled; otherwise it must be empty.\n"
                    + "narrative_mode: report the narrative mode actually used; suggested values include scene_report, rumor_wire, war_dispatch, or personal_chronicle.\n"
                    + "location_name: return a structured place name only when a concrete location is explicitly present in the facts.",
                SocialCircleNewsFactTemplate =
                    "Build one social-circle world-news card from this fact seed.\n"
                    + "origin_type={{ world.social.origin_type }}\n"
                    + "source_faction={{ world.social.source_faction }}\n"
                    + "target_faction={{ world.social.target_faction }}\n"
                    + "summary={{ dialogue.summary }}\n"
                    + "intent_hint={{ dialogue.intent_hint }}\n"
                    + "narrative_mode={{ world.social.narrative_mode }}\n"
                    + "facts:\n"
                    + "{{ world.social.fact_lines }}\n"
                    + "First decide what channel this news is traveling through, then write it as one continuous world-event short piece. lead/cause/process/outlook are returned as separate fields, but they must read in sequence like continuous paragraphs of the same article. Do not turn them into mechanical labeled sections.\n"
                    + "Prefer putting stronger first-person flavor, stance, or witness-like immediacy into quote. The quote should usually be 2-3 sentences, while the main body stays more restrained and continuous.\n"
                    + "When facts are sparse, the quote may feel a little more human and lightly emotional, but it must still stay inside the supplied facts and must not add a new outcome or judgment.\n"
                    + "You may add small touches of season, weather, light, camp, settlement, or battlefield detail at the opening and turning points to strengthen scene presence, but do not let environmental description dominate the factual line.\n"
                    + "If the facts clearly contain a concrete place, you may also return location_name; otherwise do not guess it.\n"
                    + "Output the JSON object now.",
                PublishPublicPostAction = new ApiActionConfig(
                    "publish_public_post",
                    "Publish a public social-circle announcement visible to all factions and the player",
                    "category (string: Military/Economic/Diplomatic/Anomaly), sentiment (int: -2..2), summary (string, optional), targetFaction (string, optional), intentHint (string, optional)",
                    "Only use when the statement should become public, affect world-facing diplomacy, and is not routine private bargaining. Use sparingly.")
                {
                    IsEnabled = true
                }
            };
        }
    }
}
