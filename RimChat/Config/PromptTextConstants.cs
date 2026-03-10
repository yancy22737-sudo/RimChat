namespace RimChat.Config
{
    /// <summary>/// Dependencies: none.
 /// Responsibility: provide a single source of truth for repeated prompt default text literals.
 ///</summary>
    internal static class PromptTextConstants
    {
        public static string RpgRoleSettingDefault =>
            RpgPromptDefaultsProvider.GetDefaults().RoleSetting;

        public static string RpgDialogueStyleDefault =>
            RpgPromptDefaultsProvider.GetDefaults().DialogueStyle;

        public static string RpgFormatConstraintDefault =>
            RpgPromptDefaultsProvider.GetDefaults().FormatConstraint;

        public const string RequestRaidActionDescription =
            "Launch a raid against the player (delayed arrival). Use this when insulted, threatened, or as a tactical decision during hostilities.";

        public const string RequestRaidActionRequirement = "Only when your faction is already hostile to the player";


        public const string RequestRaidActionParameters =
            "strategy (string: 'ImmediateAttack', 'ImmediateAttackSmart', 'StageThenAttack', 'ImmediateAttackSappers', or 'Siege'), arrival (string: 'EdgeWalkIn', 'EdgeDrop', 'EdgeWalkInGroups', 'RandomDrop', or 'CenterDrop')";

        public const string GoOfflineActionDescription =
            "End dialogue and switch to offline presence state";

        public const string SetDndActionDescription =
            "Switch to do-not-disturb presence state and stop message exchange";

        public const string PublishPublicPostActionDescription =
            "Publish a public social-circle announcement visible to all factions and the player";

        public const string PublishPublicPostActionParameters =
            "category (string: Military/Economic/Diplomatic/Anomaly), sentiment (int: -2..2), summary (string, optional), targetFaction (string, optional), intentHint (string, optional)";

        public const string PublishPublicPostActionRequirement =
            "Only use when the statement should become public, affect world-facing diplomacy, and is not routine private bargaining. Use sparingly.";

        public const string SocialCircleNewsStyleTemplateDefault =
            "You are writing one RimWorld world-news card for the social circle.\n"
            + "Voice: neutral news bulletin with light immersion and one optional attributed quote.\n"
            + "Grounding: use only the supplied facts; minimal connective phrasing is allowed, but do not invent new events, actors, or outcomes.\n"
            + "Goal: make the player immediately understand what happened, why it happened, how it spread, and what may happen next.\n"
            + "Category: {{category}}.\n"
            + "Source: {{source_label}}.\n"
            + "Credibility: {{credibility_label}} ({{credibility_value}}).\n"
            + "Write in the current game language: {{game_language}}.";

        public const string SocialCircleNewsJsonContractTemplateDefault =
            "Return exactly one JSON object only.\n"
            + "The first character must be '{' and the last character must be '}'.\n"
            + "Do not output markdown fences, prose, notes, or extra keys.\n"
            + "Required keys: headline, lead, cause, process, outlook.\n"
            + "Optional keys: quote, quote_attribution.\n"
            + "Each value must be a JSON string.\n"
            + "If quote is empty, quote_attribution must also be empty.";

        public const string SocialCircleNewsFactTemplateDefault =
            "Build one social-circle world-news card from this fact seed.\n"
            + "origin_type={{origin_type}}\n"
            + "source_faction={{source_faction}}\n"
            + "target_faction={{target_faction}}\n"
            + "summary={{summary}}\n"
            + "intent_hint={{intent_hint}}\n"
            + "facts:\n"
            + "{{fact_lines}}\n"
            + "Output the JSON object now.";

        public const string ActionsHeader = "ACTIONS:";
        public const string ResponseFormatHeader = "RESPONSE FORMAT:";
        public const string ResponseFormatIntro =
            "Reply in-character. If you choose gameplay actions, append exactly one raw JSON object after the dialogue (no code fences):";
        public const string CriticalActionRulesHeader = "CRITICAL ACTION RULES:";
        public const string NoActionResponseHint = "If no action is needed, reply normally with no JSON block.";
    }
}
