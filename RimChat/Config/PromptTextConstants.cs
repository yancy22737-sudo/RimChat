namespace RimChat.Config
{
    /// <summary>/// Dependencies: none.
 /// Responsibility: provide a single source of truth for repeated prompt default text literals.
 ///</summary>
    internal static class PromptTextConstants
    {
        public const string RpgRoleSettingDefault =
            "You are an AI-controlled NPC in RimWorld. Your goal is to engage in immersive, character-driven dialogue with the player.";

        public const string RpgDialogueStyleDefault =
            "Keep your responses concise, oral, and immersive. Avoid robotic or overly formal language.";

        public const string RpgFormatConstraintDefault =
            "Please output a JSON code block after your text if you want to trigger any game effects or relationship changes.\n\nFormat:\n```json\n{\n  \"favorability_delta\": 0.0,\n  \"trust_delta\": 0.0, \n  \"fear_delta\": 0.0,\n  \"respect_delta\": 0.0,\n  \"dependency_delta\": 0.0,\n  \"actions\": [\n    { \"action\": \"TryGainMemory\", \"defName\": \"Chitchat\" },\n    { \"action\": \"RomanceAttempt\" },\n    { \"action\": \"Date\" },\n    { \"action\": \"MarriageProposal\" },\n    { \"action\": \"Breakup\" },\n    { \"action\": \"Divorce\" },\n    { \"action\": \"GrantInspiration\", \"defName\": \"Inspired_Creativity\" },\n    { \"action\": \"TriggerIncident\", \"defName\": \"RaidEnemy\", \"amount\": 500 },\n    { \"action\": \"ExitDialogue\", \"reason\": \"Let's pause here.\" },\n    { \"action\": \"ExitDialogueCooldown\", \"reason\": \"Do not contact me again for now.\" }\n  ]\n}\n```\nIMPORTANT: Use EXACTLY the format above. 'actions' must be an array of objects. Only include fields that have non-zero changes. If no effects occur, you may omit the JSON block.";

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

        public const string ActionsHeader = "ACTIONS:";
        public const string DecisionGuidelinesHeader = "DECISION GUIDELINES:";
        public const string ResponseFormatHeader = "RESPONSE FORMAT:";
        public const string ResponseFormatIntro =
            "Respond with your in-character dialogue first, then optionally include a JSON block:";
        public const string JsonFence = "```json";
        public const string ImportantRulesHeader = "IMPORTANT RULES:";
        public const string NoActionResponseHint = "If no action is needed, respond normally without JSON.";
    }
}
