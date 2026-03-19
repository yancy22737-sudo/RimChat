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
                    + "Voice: neutral news bulletin with light immersion and one optional attributed quote.\n"
                    + "Grounding: use only the supplied facts; minimal connective phrasing is allowed, but do not invent new events, actors, or outcomes.\n"
                    + "Goal: make the player immediately understand what happened, why it happened, how it spread, and what may happen next.\n"
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
                    + "- quote, quote_attribution.\n"
                    + "Each value must be a JSON string.\n"
                    + "If quote is empty, quote_attribution must also be empty.",
                SocialCircleNewsFactTemplate =
                    "Build one social-circle world-news card from this fact seed.\n"
                    + "origin_type={{ world.social.origin_type }}\n"
                    + "source_faction={{ world.social.source_faction }}\n"
                    + "target_faction={{ world.social.target_faction }}\n"
                    + "summary={{ dialogue.summary }}\n"
                    + "intent_hint={{ dialogue.intent_hint }}\n"
                    + "facts:\n"
                    + "{{ world.social.fact_lines }}\n"
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
