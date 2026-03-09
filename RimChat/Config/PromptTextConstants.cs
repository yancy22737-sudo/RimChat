namespace RimChat.Config
{
    /// <summary>/// Dependencies: none.
 /// Responsibility: provide a single source of truth for repeated prompt default text literals.
 ///</summary>
    internal static class PromptTextConstants
    {
        public static string RpgRoleSettingDefault =>
            RpgPromptDefaultsProvider.GetDefaults().RoleSettingDefault;

        public static string RpgDialogueStyleDefault =>
            RpgPromptDefaultsProvider.GetDefaults().DialogueStyleDefault;

        public static string RpgFormatConstraintDefault =>
            RpgPromptDefaultsProvider.GetDefaults().FormatConstraintDefault;

        public const string RequestRaidActionDescription =
            "Launch a raid against the player (delayed arrival). Use this when insulted, threatened, or as a tactical decision during hostilities.";

        public const string RequestRaidActionRequirement = "faction is hostile to player";

        public const string RequestRaidActionParametersLegacy =
            "strategy (string: 'ImmediateAttack' or 'Siege'), arrival (string: 'EdgeWalkIn' or 'CenterDrop')";

        public const string RequestRaidActionParametersCurrent =
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
            "Only use when communication should become public and have world-facing consequences";

        public const string ActionsHeader = "ACTIONS:";
        public const string ResponseFormatHeader = "RESPONSE FORMAT:";
        public const string ResponseFormatIntro =
            "Reply in-character. If you choose a gameplay action, append exactly one JSON block after the dialogue:";
        public const string JsonFence = "```json";
        public const string CriticalActionRulesHeader = "CRITICAL ACTION RULES:";
        public const string NoActionResponseHint = "If no action is needed, reply normally with no JSON block.";
    }
}
