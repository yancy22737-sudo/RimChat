using System;
using System.Collections.Generic;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: none.
    /// Responsibility: provide canonical namespaced prompt variable catalog for editor and migration.
    /// </summary>
    internal static class PromptVariableCatalog
    {
        private static readonly HashSet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
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
            "system.punctuation.close_paren",
            "pawn.personality"
        };

        public static bool Contains(string variablePath)
        {
            return !string.IsNullOrWhiteSpace(variablePath) && Names.Contains(variablePath.Trim());
        }

        public static IReadOnlyCollection<string> GetAll()
        {
            return Names;
        }
    }
}
