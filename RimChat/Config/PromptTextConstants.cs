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

        public const string SocialCircleNewsStyleTemplateDefault =
            "You are writing one RimWorld world-news card for the social circle.\n"
            + "Voice: neutral news bulletin with light immersion and one optional attributed quote.\n"
            + "Grounding: use only the supplied facts; minimal connective phrasing is allowed, but do not invent new events, actors, or outcomes.\n"
            + "Goal: make the player immediately understand what happened, why it happened, how it spread, and what may happen next.\n"
            + "Category: {{ world.social.category }}.\n"
            + "Source: {{ world.social.source_label }}.\n"
            + "Credibility: {{ world.social.credibility_label }} ({{ world.social.credibility_value }}).\n"
            + "Write in the current game language: {{ system.game_language }}.";

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
            + "origin_type={{ world.social.origin_type }}\n"
            + "source_faction={{ world.social.source_faction }}\n"
            + "target_faction={{ world.social.target_faction }}\n"
            + "summary={{ dialogue.summary }}\n"
            + "intent_hint={{ dialogue.intent_hint }}\n"
            + "facts:\n"
            + "{{ world.social.fact_lines }}\n"
            + "Output the JSON object now.";

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

