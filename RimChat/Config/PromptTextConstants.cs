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

        public static string PublishPublicPostActionDescription =>
            SocialCirclePromptDefaultsProvider.GetDefaults().PublishPublicPostAction?.Description ?? string.Empty;

        public static string PublishPublicPostActionParameters =>
            SocialCirclePromptDefaultsProvider.GetDefaults().PublishPublicPostAction?.Parameters ?? string.Empty;

        public static string PublishPublicPostActionRequirement =>
            SocialCirclePromptDefaultsProvider.GetDefaults().PublishPublicPostAction?.Requirement ?? string.Empty;

        public const string SendImageActionDescription =
            "Generate a diplomacy image through the image API and return it as an inline chat image card.";

        public const string SendImageActionParameters =
            "template_id (string, REQUIRED), extra_prompt (string, optional), caption (string, optional), size (string, optional), watermark (bool, optional)";

        public const string SendImageActionRequirement =
            "Use only when image API is configured and only one image is needed this turn. Always provide template_id.";

        public const string SendImageCaptionStylePromptDefault =
            "Write a vivid one-sentence caption like a faction leader sharing a fresh photo in chat. Keep it natural, immersive, and emotionally expressive.";
        public const string SendImageCaptionFallbackTemplateDefault =
            "这是我们首领{{ pawn.leader.name }}，怎么样够帅吧？";

        public const string SendImageDefaultTemplateName = "Leader Portrait";
        public const string SendImageDefaultTemplateDescription =
            "Cinematic faction-leader portrait scene grounded in RimWorld technology and faction identity.";
        public const string SendImageDefaultTemplateText =
            "Create a grounded, lore-consistent RimWorld portrait scene of this faction leader. "
            + "Keep environment, clothing, and technology level consistent with faction background. "
            + "Use natural materials, practical gear, and clear facial readability.";

        public static string SocialCircleNewsStyleTemplateDefault =>
            SocialCirclePromptDefaultsProvider.GetDefaults().SocialCircleNewsStyleTemplate ?? string.Empty;

        public static string SocialCircleNewsJsonContractTemplateDefault =>
            SocialCirclePromptDefaultsProvider.GetDefaults().SocialCircleNewsJsonContractTemplate ?? string.Empty;

        public static string SocialCircleNewsFactTemplateDefault =>
            SocialCirclePromptDefaultsProvider.GetDefaults().SocialCircleNewsFactTemplate ?? string.Empty;

        public const string ApiLimitsNodeLiteralDefault =
            "{{ dialogue.api_limits_body }}";

        public const string QuestGuidanceNodeLiteralDefault =
            "=== QUEST TEMPLATE STRICT OVERRIDE ===\n"
            + "Use only quest templates currently available for the active faction.\n"
            + "Do not use blocked templates.\n"
            + "If a template is blocked by safety policy, refuse in-character and explain the constraint.\n"
            + "Never use static or recalled quest recommendations from other sections.";

        public const string ResponseContractNodeLiteralDefault =
            "{{ dialogue.response_contract_body }}";

        public const string ActionsHeader = "ACTIONS:";
        public const string ResponseFormatHeader = "RESPONSE FORMAT:";
        public const string ResponseFormatIntro =
            "Reply in-character. If you choose gameplay actions, append exactly one raw JSON object after the dialogue (no code fences):";
        public const string CriticalActionRulesHeader = "CRITICAL ACTION RULES:";
        public const string NoActionResponseHint = "If no action is needed, reply normally with no JSON block.";

        public const string GoodwillPeacePolicyHeader = "DYNAMIC PEACE POLICY (GOODWILL-BASED):";
        public const string GoodwillPeacePolicyVeryLowLine1 =
            "- Current goodwill: {0}. Do NOT use make_peace or direct peace treaty actions.";
        public const string GoodwillPeacePolicyVeryLowLine2 =
            "- Reason: hostility is too deep below {0}; immediate treaty is disallowed.";
        public const string GoodwillPeacePolicyTalkOnlyLine1 =
            "- Current goodwill: {0}. Do NOT use make_peace in this range.";
        public const string GoodwillPeacePolicyTalkOnlyLine2 =
            "- You MUST use create_quest with questDefName '{0}' for peace talks.";
        public const string GoodwillPeacePolicyTalkOnlyLine3 =
            "- Reason: goodwill is in [{0},{1}], peace talks are required before direct peace.";
        public const string GoodwillPeacePolicyReenabledLine1 =
            "- Current goodwill: {0}. Both make_peace and peace-talk quest are allowed.";
        public const string GoodwillPeacePolicyReenabledLine2 =
            "- If you choose create_quest, peace talks should use questDefName '{0}'.";
    }
}

