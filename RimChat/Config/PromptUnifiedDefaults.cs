namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: unified prompt catalog.
    /// Responsibility: provide minimal fallback node defaults for unified prompt runtime.
    /// </summary>
    internal static class PromptUnifiedDefaults
    {
        internal static void ApplyFallbackNodes(PromptUnifiedCatalog catalog)
        {
            if (catalog == null)
            {
                return;
            }

            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "fact_grounding",
                "=== FACT GROUNDING RULES ===\n- Only treat provided prompt data, visible world state, and recorded memory as facts.\n- Do not invent events, identities, motives, resources, injuries, map changes, or relationship history.\n- If a player claim is unverified, respond in-character with uncertainty and ask for clarification or evidence.\n- Evaluate the user's statements using the known facts and previous dialogue context.\n- If the user provides information that contradicts established facts or appears intentionally deceptive, consider it a lie and reduce the NPC's favorability toward the user.\n- Stay grounded in known facts; mark assumptions clearly and avoid unsupported topic drift.");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "output_language",
                "Respond in {{ system.target_language }}. Keep JSON keys and action names unchanged.");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "decision_policy",
                "Decision priority order: 1) format + language correctness; 2) current objective; 3) fact grounding; 4) action safety and relationship constraints; 5) continuity + persona style.");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "turn_objective",
                "PrimaryObjective: {{ dialogue.primary_objective }}\nOptionalFollowup: {{ dialogue.optional_followup }}\nConstraint: complete PrimaryObjective first; at most one topic shift.");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "opening_objective",
                "OpeningObjective: if unresolved intent exists ({{ dialogue.latest_unresolved_intent }}), acknowledge it naturally in the opening line; otherwise open in-character.");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "topic_shift_rule",
                "TopicShiftRule: finish the current objective first; only add one short follow-up if it improves clarity or next-step planning.");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "diplomacy_fallback_role",
                "You are the leader of {{ world.faction.name }} in RimWorld.");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "social_circle_action_rule",
                "Social circle rules: use publish_public_post only for public statements that should be seen by all factions and the player.");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "api_limits_node_template", "{{ dialogue.api_limits_body }}");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "quest_guidance_node_template", "{{ dialogue.quest_guidance_body }}");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "response_contract_node_template", "{{ dialogue.response_contract_body }}");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "strategy_output_contract",
                "Return exactly one JSON object only.\nThe first character must be '{' and the last character must be '}'.\nDo not output markdown fences, prose, notes, or any extra text.\nRequired format:\n{\"strategy_suggestions\":[{\"strategy_name\":\"\",\"reason\":\"\",\"content\":\"\"},{\"strategy_name\":\"\",\"reason\":\"\",\"content\":\"\"},{\"strategy_name\":\"\",\"reason\":\"\",\"content\":\"\"}]}\nRules:\n- Exactly 3 items.\n- Output keys must be exactly: strategy_suggestions, strategy_name, reason, content.\n- strategy_name <= 6 Chinese characters and must be actionable intent.\n- reason must cite at least one fact tag like [F1] and explain causality.\n- reason should stay compact for button display.\n- content must be a complete sendable line the player can auto-send directly.\n- Keep style aligned with the current faction voice and the player's language.\n- At least 2 items must explicitly leverage player attributes or current context.\n- Never output extra fields such as action, priority, risk_assessment, task, plan, or macro_advice.");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "strategy_player_negotiator_context_template", "{{ dialogue.strategy_player_negotiator_context_body }}");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "strategy_fact_pack_template", "{{ dialogue.strategy_fact_pack_body }}");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "strategy_scenario_dossier_template", "{{ dialogue.strategy_scenario_dossier_body }}");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "social_news_style", "You are writing one RimWorld world-news card for the social circle.");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "social_news_json_contract", "Return exactly one JSON object only.");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "social_news_fact", "Build one social-circle world-news card from the supplied fact seed.");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "rpg_role_setting_fallback", "Roleplay as {{ pawn.target.name }} in the current RimWorld context.");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "rpg_relationship_profile", "=== RELATIONSHIP PROFILE (MANUAL RPG ONLY) ===\nKinship: {{ pawn.relation.kinship }}\nRomanceState: {{ pawn.relation.romance_state }}\nGuidance: {{ dialogue.guidance }}");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "rpg_kinship_boundary", "When kinship is {{ pawn.relation.kinship }}, keep family boundaries first.");
        }

        private static void SetIfMissing(PromptUnifiedCatalog catalog, string channel, string nodeId, string fallback)
        {
            if (string.IsNullOrWhiteSpace(catalog.ResolveNode(channel, nodeId)))
            {
                catalog.SetNode(channel, nodeId, fallback ?? string.Empty);
            }
        }
    }
}
