using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using RimChat.Config;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: prompt config models and Scriban prompt engine.
    /// Responsibility: one-shot rewrite from legacy placeholders to strong namespaces.
    /// </summary>
    internal sealed class PromptTemplateRewriteDiagnostic
    {
        public string TemplateId { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public bool Rewritten { get; set; }
        public bool Blocked { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Dependencies: PromptTemplateRewriteDiagnostic.
    /// Responsibility: hold migration outcomes for each template.
    /// </summary>
    internal sealed class PromptTemplateAutoRewriteResult
    {
        public bool Changed { get; set; }
        public bool HasBlockedTemplates => BlockedTemplateIds.Count > 0;
        public List<string> BlockedTemplateIds { get; } = new List<string>();
        public List<PromptTemplateRewriteDiagnostic> TemplateDiagnostics { get; } = new List<PromptTemplateRewriteDiagnostic>();

        public void RecordDiagnostic(PromptTemplateRewriteDiagnostic diagnostic)
        {
            if (diagnostic == null)
            {
                return;
            }

            TemplateDiagnostics.Add(diagnostic);
            if (!diagnostic.Blocked || string.IsNullOrWhiteSpace(diagnostic.TemplateId))
            {
                return;
            }

            if (!BlockedTemplateIds.Contains(diagnostic.TemplateId))
            {
                BlockedTemplateIds.Add(diagnostic.TemplateId);
            }
        }
    }

    /// <summary>
    /// Dependencies: PromptTemplateBlockRegistry and IScribanPromptEngine.
    /// Responsibility: rewrite and validate templates, blocking invalid migrated templates.
    /// </summary>
    internal static class PromptTemplateAutoRewriter
    {
        private static readonly Regex PlaceholderRegex =
            new Regex(@"\{\{\s*([a-zA-Z0-9_.]+)\s*\}\}", RegexOptions.Compiled);

        private static readonly Dictionary<string, string> LegacyVariableMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["scene_tags"] = "world.scene_tags",
                ["environment_params"] = "world.environment_params",
                ["recent_world_events"] = "world.recent_world_events",
                ["colony_status"] = "world.colony_status",
                ["colony_factions"] = "world.colony_factions",
                ["current_faction_profile"] = "world.current_faction_profile",
                ["rpg_target_profile"] = "pawn.target.profile",
                ["rpg_initiator_profile"] = "pawn.initiator.profile",
                ["player_pawn_profile"] = "pawn.player.profile",
                ["player_royalty_summary"] = "pawn.player.royalty_summary",
                ["faction_settlement_summary"] = "world.faction_settlement_summary",
                ["channel"] = "ctx.channel",
                ["mode"] = "ctx.mode",
                ["target_language"] = "system.target_language",
                ["game_language"] = "system.game_language",
                ["faction_name"] = "world.faction.name",
                ["initiator_name"] = "pawn.initiator.name",
                ["target_name"] = "pawn.target.name",
                ["primary_objective"] = "dialogue.primary_objective",
                ["optional_followup"] = "dialogue.optional_followup",
                ["latest_unresolved_intent"] = "dialogue.latest_unresolved_intent",
                ["topic_shift_rule"] = "dialogue.topic_shift_rule",
                ["api_limits_body"] = "dialogue.api_limits_body",
                ["quest_guidance_body"] = "dialogue.quest_guidance_body",
                ["response_contract_body"] = "dialogue.response_contract_body",
                ["kinship"] = "pawn.relation.kinship",
                ["romance_state"] = "pawn.relation.romance_state",
                ["guidance"] = "dialogue.guidance",
                ["origin_type"] = "world.social.origin_type",
                ["category"] = "world.social.category",
                ["source_faction"] = "world.social.source_faction",
                ["target_faction"] = "world.social.target_faction",
                ["summary"] = "dialogue.summary",
                ["intent_hint"] = "dialogue.intent_hint",
                ["source_label"] = "world.social.source_label",
                ["credibility_label"] = "world.social.credibility_label",
                ["credibility_value"] = "world.social.credibility_value",
                ["fact_lines"] = "world.social.fact_lines",
                ["speaker_kind"] = "pawn.speaker.kind",
                ["default_sound"] = "pawn.speaker.default_sound",
                ["animal_sound"] = "pawn.speaker.animal_sound",
                ["baby_sound"] = "pawn.speaker.baby_sound",
                ["mechanoid_sound"] = "pawn.speaker.mechanoid_sound",
                ["open_paren"] = "system.punctuation.open_paren",
                ["close_paren"] = "system.punctuation.close_paren",
                ["template_line"] = "dialogue.template_line",
                ["example_line"] = "dialogue.example_line",
                ["subject_pronoun"] = "pawn.pronouns.subject",
                ["object_pronoun"] = "pawn.pronouns.object",
                ["possessive_pronoun"] = "pawn.pronouns.possessive",
                ["profile"] = "pawn.profile",
                ["subject_pronoun_lower"] = "pawn.pronouns.subject_lower",
                ["be_verb"] = "pawn.pronouns.be_verb",
                ["seek_verb"] = "pawn.pronouns.seek_verb",
                ["examples"] = "dialogue.examples",
                ["action_names"] = "dialogue.action_names"
            };

        public static PromptTemplateAutoRewriteResult RewriteSystemPromptConfig(
            SystemPromptConfig config,
            IScribanPromptEngine engine)
        {
            var result = new PromptTemplateAutoRewriteResult();
            if (config?.PromptTemplates == null || engine == null)
            {
                return result;
            }

            RewritePromptTemplate(ref config.PromptTemplates.FactGroundingTemplate, "prompt_templates.fact_grounding", "system", engine, result);
            RewritePromptTemplate(ref config.PromptTemplates.OutputLanguageTemplate, "prompt_templates.output_language", "system", engine, result);
            RewritePromptTemplate(ref config.PromptTemplates.DiplomacyFallbackRoleTemplate, "prompt_templates.diplomacy_fallback_role", "diplomacy", engine, result);
            RewritePromptTemplate(ref config.PromptTemplates.SocialCircleActionRuleTemplate, "prompt_templates.social_circle_action_rule", "diplomacy", engine, result);
            RewritePromptTemplate(ref config.PromptTemplates.SocialCircleNewsStyleTemplate, "prompt_templates.social_news_style", "social", engine, result);
            RewritePromptTemplate(ref config.PromptTemplates.SocialCircleNewsJsonContractTemplate, "prompt_templates.social_news_json_contract", "social", engine, result);
            RewritePromptTemplate(ref config.PromptTemplates.SocialCircleNewsFactTemplate, "prompt_templates.social_news_fact", "social", engine, result);
            RewritePromptTemplate(ref config.PromptTemplates.RpgRoleSettingTemplate, "prompt_templates.rpg_role_setting", "rpg", engine, result);
            RewritePromptTemplate(ref config.PromptTemplates.RpgCompactFormatConstraintTemplate, "prompt_templates.rpg_compact_constraint", "rpg", engine, result);
            RewritePromptTemplate(ref config.PromptTemplates.RpgActionReliabilityRuleTemplate, "prompt_templates.rpg_action_reliability", "rpg", engine, result);
            RewritePromptTemplate(ref config.PromptTemplates.DecisionPolicyTemplate, "prompt_templates.decision_policy", "system", engine, result);
            RewritePromptTemplate(ref config.PromptTemplates.TurnObjectiveTemplate, "prompt_templates.turn_objective", "system", engine, result);
            RewritePromptTemplate(ref config.PromptTemplates.OpeningObjectiveTemplate, "prompt_templates.opening_objective", "rpg", engine, result);
            RewritePromptTemplate(ref config.PromptTemplates.TopicShiftRuleTemplate, "prompt_templates.topic_shift_rule", "system", engine, result);
            RewritePromptTemplate(ref config.PromptTemplates.ApiLimitsNodeTemplate, "prompt_templates.api_limits_node", "system", engine, result);
            RewritePromptTemplate(ref config.PromptTemplates.QuestGuidanceNodeTemplate, "prompt_templates.quest_guidance_node", "system", engine, result);
            RewritePromptTemplate(ref config.PromptTemplates.ResponseContractNodeTemplate, "prompt_templates.response_contract_node", "system", engine, result);
            RewriteSceneEntries(config, engine, result);
            return result;
        }

        public static PromptTemplateAutoRewriteResult RewriteRimTalkChannelConfig(
            RimTalkChannelCompatConfig config,
            string channel,
            IScribanPromptEngine engine,
            string idPrefix)
        {
            var result = new PromptTemplateAutoRewriteResult();
            if (config == null || engine == null)
            {
                return result;
            }

            string normalizedPrefix = string.IsNullOrWhiteSpace(idPrefix) ? "compat" : idPrefix.Trim();
            RewritePromptTemplate(ref config.CompatTemplate, $"{normalizedPrefix}.template", channel, engine, result);
            if (config.PromptEntries == null)
            {
                return result;
            }

            for (int i = 0; i < config.PromptEntries.Count; i++)
            {
                RimTalkPromptEntryConfig entry = config.PromptEntries[i];
                if (entry == null)
                {
                    continue;
                }

                string entryId = string.IsNullOrWhiteSpace(entry.Id) ? $"entry_{i}" : entry.Id.Trim();
                RewritePromptTemplate(ref entry.Content, $"{normalizedPrefix}.entry.{entryId}", channel, engine, result);
            }

            return result;
        }

        public static bool TryRewriteLegacyTemplate(
            string templateId,
            string channel,
            string templateText,
            IScribanPromptEngine engine,
            out string rewrittenText,
            out string failureReason)
        {
            rewrittenText = templateText ?? string.Empty;
            failureReason = string.Empty;
            if (engine == null)
            {
                failureReason = "Prompt engine is unavailable.";
                return false;
            }

            string rewritten = RewriteTemplateText(templateText, out _);
            try
            {
                EnsureNoBareVariablesOrThrow(templateId, channel, rewritten);
                engine.ValidateOrThrow(
                    templateId,
                    channel,
                    rewritten,
                    engine.BuildValidationContext(templateId, channel, EnumerateVariablePaths(rewritten)));
                rewrittenText = rewritten;
                return true;
            }
            catch (PromptRenderException ex)
            {
                failureReason = ex.Message ?? string.Empty;
                return false;
            }
        }

        private static void RewriteSceneEntries(
            SystemPromptConfig config,
            IScribanPromptEngine engine,
            PromptTemplateAutoRewriteResult result)
        {
            if (config?.EnvironmentPrompt?.SceneEntries == null)
            {
                return;
            }

            for (int i = 0; i < config.EnvironmentPrompt.SceneEntries.Count; i++)
            {
                ScenePromptEntryConfig entry = config.EnvironmentPrompt.SceneEntries[i];
                if (entry == null)
                {
                    continue;
                }

                string id = string.IsNullOrWhiteSpace(entry.Id) ? $"scene_entry_{i}" : entry.Id.Trim();
                RewritePromptTemplate(ref entry.Content, $"scene_entries.{id}", "system", engine, result);
            }
        }

        private static void RewritePromptTemplate(
            ref string templateText,
            string templateId,
            string channel,
            IScribanPromptEngine engine,
            PromptTemplateAutoRewriteResult result)
        {
            if (templateText == null)
            {
                return;
            }

            bool replaced;
            string rewritten = RewriteTemplateText(templateText, out replaced);
            bool rewrittenChanged = replaced && !string.Equals(templateText, rewritten, StringComparison.Ordinal);
            try
            {
                EnsureNoBareVariablesOrThrow(templateId, channel, rewritten);
                engine.ValidateOrThrow(templateId, channel, rewritten, engine.BuildValidationContext(templateId, channel, EnumerateVariablePaths(rewritten)));
                PromptTemplateBlockRegistry.Clear(templateId, channel);
                result.RecordDiagnostic(new PromptTemplateRewriteDiagnostic
                {
                    TemplateId = templateId,
                    Channel = channel,
                    Rewritten = rewrittenChanged,
                    Blocked = false
                });
            }
            catch (PromptRenderException ex)
            {
                PromptTemplateBlockRegistry.MarkBlocked(templateId, channel, ex.Message);
                result.RecordDiagnostic(new PromptTemplateRewriteDiagnostic
                {
                    TemplateId = templateId,
                    Channel = channel,
                    Rewritten = rewrittenChanged,
                    Blocked = true,
                    Reason = ex.Message ?? string.Empty
                });
                return;
            }

            if (!rewrittenChanged)
            {
                return;
            }

            templateText = rewritten;
            result.Changed = true;
        }

        private static IEnumerable<string> EnumerateVariablePaths(string templateText)
        {
            if (string.IsNullOrWhiteSpace(templateText))
            {
                yield break;
            }

            MatchCollection matches = PlaceholderRegex.Matches(templateText);
            for (int i = 0; i < matches.Count; i++)
            {
                string token = matches[i].Groups[1].Value?.Trim() ?? string.Empty;
                if (token.IndexOf('.') <= 0)
                {
                    continue;
                }

                yield return token;
            }
        }

        private static string RewriteTemplateText(string templateText, out bool replaced)
        {
            bool wasReplaced = false;
            if (string.IsNullOrWhiteSpace(templateText))
            {
                replaced = false;
                return templateText ?? string.Empty;
            }

            string rewritten = PlaceholderRegex.Replace(templateText, match =>
            {
                string token = match.Groups[1].Value?.Trim() ?? string.Empty;
                string mapped;
                if (!LegacyVariableMap.TryGetValue(token, out mapped))
                {
                    return match.Value;
                }

                wasReplaced = true;
                return "{{ " + mapped + " }}";
            });
            replaced = wasReplaced;
            return rewritten;
        }

        private static void EnsureNoBareVariablesOrThrow(string templateId, string channel, string templateText)
        {
            MatchCollection matches = PlaceholderRegex.Matches(templateText ?? string.Empty);
            for (int i = 0; i < matches.Count; i++)
            {
                string token = matches[i].Groups[1].Value?.Trim() ?? string.Empty;
                if (token.IndexOf('.') >= 0)
                {
                    continue;
                }

                throw new PromptRenderException(
                    templateId,
                    channel,
                    new PromptRenderDiagnostic
                    {
                        ErrorCode = PromptRenderErrorCode.UnknownVariable,
                        Message = $"Bare variable is forbidden after migration: {token}"
                    });
            }
        }
    }
}
