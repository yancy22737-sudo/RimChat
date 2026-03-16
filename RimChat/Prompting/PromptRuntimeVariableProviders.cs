using System;
using System.Collections.Generic;
using RimChat.Config;
using RimWorld;
using Verse;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: prompt variable registry and runtime resolver delegate.
    /// Responsibility: expose RimChat-owned namespaced variables as the canonical prompt source.
    /// </summary>
    internal sealed class RimChatCoreVariableProvider : IPromptRuntimeVariableProvider
    {
        private const string Source = "rimchat.core";
        private static readonly IReadOnlyList<PromptRuntimeVariableDefinition> Definitions = BuildDefinitions();
        private readonly Func<string, PromptRuntimeVariableContext, object> _resolver;

        public RimChatCoreVariableProvider(Func<string, PromptRuntimeVariableContext, object> resolver)
        {
            _resolver = resolver;
        }

        public string SourceId => Source;

        public string SourceLabel => "RimChat Core";

        public bool IsAvailable(PromptRuntimeVariableContext context)
        {
            return true;
        }

        public IReadOnlyList<PromptRuntimeVariableDefinition> GetDefinitions()
        {
            return Definitions;
        }

        public void PopulateValues(
            IDictionary<string, object> values,
            PromptRuntimeVariableContext context)
        {
            if (values == null || _resolver == null)
            {
                return;
            }

            for (int i = 0; i < Definitions.Count; i++)
            {
                PromptRuntimeVariableDefinition definition = Definitions[i];
                object value = _resolver(definition.Path, context);
                if (value != null)
                {
                    values[definition.Path] = value;
                }
            }
        }

        private static IReadOnlyList<PromptRuntimeVariableDefinition> BuildDefinitions()
        {
            var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ctx.channel"] = "RimChat_TemplateVar_ctx_channel_Desc",
                ["ctx.mode"] = "RimChat_TemplateVar_ctx_mode_Desc",
                ["system.target_language"] = "RimChat_TemplateVar_system_target_language_Desc",
                ["world.faction.name"] = "RimChat_TemplateVar_world_faction_name_Desc",
                ["pawn.initiator.name"] = "RimChat_TemplateVar_pawn_initiator_name_Desc",
                ["pawn.target.name"] = "RimChat_TemplateVar_pawn_target_name_Desc",
                ["world.scene_tags"] = "RimChat_TemplateVar_scene_tags_Desc",
                ["world.environment_params"] = "RimChat_TemplateVar_environment_params_Desc",
                ["world.recent_world_events"] = "RimChat_TemplateVar_recent_world_events_Desc",
                ["world.colony_status"] = "RimChat_TemplateVar_colony_status_Desc",
                ["world.colony_factions"] = "RimChat_TemplateVar_colony_factions_Desc",
                ["world.current_faction_profile"] = "RimChat_TemplateVar_current_faction_profile_Desc",
                ["pawn.target.profile"] = "RimChat_TemplateVar_rpg_target_profile_Desc",
                ["pawn.initiator.profile"] = "RimChat_TemplateVar_rpg_initiator_profile_Desc",
                ["pawn.player.profile"] = "RimChat_TemplateVar_player_pawn_profile_Desc",
                ["pawn.player.royalty_summary"] = "RimChat_TemplateVar_player_royalty_summary_Desc",
                ["world.faction_settlement_summary"] = "RimChat_TemplateVar_faction_settlement_summary_Desc",
                ["dialogue.primary_objective"] = "RimChat_TemplateVar_dialogue_primary_objective_Desc",
                ["dialogue.optional_followup"] = "RimChat_TemplateVar_dialogue_optional_followup_Desc",
                ["dialogue.latest_unresolved_intent"] = "RimChat_TemplateVar_dialogue_latest_unresolved_intent_Desc",
                ["dialogue.topic_shift_rule"] = "RimChat_TemplateVar_dialogue_topic_shift_rule_Desc",
                ["dialogue.api_limits_body"] = "RimChat_TemplateVar_dialogue_api_limits_body_Desc",
                ["dialogue.quest_guidance_body"] = "RimChat_TemplateVar_dialogue_quest_guidance_body_Desc",
                ["dialogue.response_contract_body"] = "RimChat_TemplateVar_dialogue_response_contract_body_Desc"
            };

            string[] paths =
            {
                "ctx.channel",
                "ctx.mode",
                "system.target_language",
                "system.game_language",
                "world.faction.name",
                "world.scene_tags",
                "world.environment_params",
                "world.recent_world_events",
                "world.colony_status",
                "world.colony_factions",
                "world.current_faction_profile",
                "world.faction_settlement_summary",
                "world.social.origin_type",
                "world.social.category",
                "world.social.source_faction",
                "world.social.target_faction",
                "world.social.source_label",
                "world.social.credibility_label",
                "world.social.credibility_value",
                "world.social.fact_lines",
                "pawn.initiator",
                "pawn.target",
                "pawn.initiator.name",
                "pawn.target.name",
                "pawn.target.profile",
                "pawn.initiator.profile",
                "pawn.player.profile",
                "pawn.player.royalty_summary",
                "pawn.relation.kinship",
                "pawn.relation.romance_state",
                "pawn.speaker.kind",
                "pawn.speaker.default_sound",
                "pawn.speaker.animal_sound",
                "pawn.speaker.baby_sound",
                "pawn.speaker.mechanoid_sound",
                "pawn.pronouns.subject",
                "pawn.pronouns.object",
                "pawn.pronouns.possessive",
                "pawn.pronouns.subject_lower",
                "pawn.pronouns.be_verb",
                "pawn.pronouns.seek_verb",
                "pawn.profile",
                "pawn.personality",
                "dialogue.summary",
                "dialogue.intent_hint",
                "dialogue.primary_objective",
                "dialogue.optional_followup",
                "dialogue.latest_unresolved_intent",
                "dialogue.topic_shift_rule",
                "dialogue.api_limits_body",
                "dialogue.quest_guidance_body",
                "dialogue.response_contract_body",
                "dialogue.guidance",
                "dialogue.template_line",
                "dialogue.example_line",
                "dialogue.examples",
                "dialogue.action_names",
                "dialogue.diplomacy_dialogue.system_rules",
                "dialogue.diplomacy_dialogue.character_persona",
                "dialogue.diplomacy_dialogue.memory_system",
                "dialogue.diplomacy_dialogue.environment_perception",
                "dialogue.diplomacy_dialogue.context",
                "dialogue.diplomacy_dialogue.action_rules",
                "dialogue.diplomacy_dialogue.repetition_reinforcement",
                "dialogue.diplomacy_dialogue.output_specification",
                "system.punctuation.open_paren",
                "system.punctuation.close_paren"
            };

            var items = new List<PromptRuntimeVariableDefinition>(paths.Length);
            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                descriptions.TryGetValue(path, out string descriptionKey);
                items.Add(new PromptRuntimeVariableDefinition(path, Source, "RimChat Core", descriptionKey));
            }

            return items;
        }
    }

    /// <summary>
    /// Dependencies: RimWorld loaded-mod inspection APIs.
    /// Responsibility: reserve a clean provider boundary for RimTalk-derived runtime variables.
    /// </summary>
    internal sealed class RimTalkVariableProvider : IPromptRuntimeVariableProvider
    {
        public string SourceId => "rimtalk.bridge";

        public string SourceLabel => "RimTalk Bridge";

        public bool IsAvailable(PromptRuntimeVariableContext context)
        {
            return LoadedModManager.RunningModsListForReading != null &&
                   LoadedModManager.RunningModsListForReading.Exists(mod =>
                       mod != null &&
                       (ContainsText(mod.PackageIdPlayerFacing, "rimtalk") ||
                        ContainsText(mod.Name, "rimtalk")));
        }

        public IReadOnlyList<PromptRuntimeVariableDefinition> GetDefinitions()
        {
            return Array.Empty<PromptRuntimeVariableDefinition>();
        }

        public void PopulateValues(
            IDictionary<string, object> values,
            PromptRuntimeVariableContext context)
        {
        }

        private static bool ContainsText(string value, string token)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// <summary>
    /// Dependencies: RimWorld loaded-mod inspection APIs.
    /// Responsibility: reserve a clean provider boundary for MemoryPatch-derived runtime variables.
    /// </summary>
    internal sealed class RimTalkMemoryPatchVariableProvider : IPromptRuntimeVariableProvider
    {
        public string SourceId => "rimtalk.memorypatch";

        public string SourceLabel => "MemoryPatch Bridge";

        public bool IsAvailable(PromptRuntimeVariableContext context)
        {
            return LoadedModManager.RunningModsListForReading != null &&
                   LoadedModManager.RunningModsListForReading.Exists(mod =>
                       mod != null &&
                       (ContainsText(mod.PackageIdPlayerFacing, "memorypatch") ||
                        ContainsText(mod.Name, "memorypatch")));
        }

        public IReadOnlyList<PromptRuntimeVariableDefinition> GetDefinitions()
        {
            return Array.Empty<PromptRuntimeVariableDefinition>();
        }

        public void PopulateValues(
            IDictionary<string, object> values,
            PromptRuntimeVariableContext context)
        {
        }

        private static bool ContainsText(string value, string token)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
