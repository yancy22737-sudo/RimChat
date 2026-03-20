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
            "对玩家发起袭击（延迟到达）。可在受辱、受威胁或敌对期间作为战术决策使用。";

        public const string RequestRaidActionRequirement = "仅在你的派系已对玩家敌对时使用";


        public const string RequestRaidActionParameters =
            "strategy (string: 'ImmediateAttack', 'ImmediateAttackSmart', 'StageThenAttack', 'ImmediateAttackSappers', or 'Siege'), arrival (string: 'EdgeWalkIn', 'EdgeDrop', 'EdgeWalkInGroups', 'RandomDrop', or 'CenterDrop')";

        public const string GoOfflineActionDescription =
            "结束对话并切换为离线状态";

        public const string SetDndActionDescription =
            "切换为勿扰状态并停止消息往来";

        public static string PublishPublicPostActionDescription =>
            SocialCirclePromptDefaultsProvider.GetDefaults().PublishPublicPostAction?.Description ?? string.Empty;

        public static string PublishPublicPostActionParameters =>
            SocialCirclePromptDefaultsProvider.GetDefaults().PublishPublicPostAction?.Parameters ?? string.Empty;

        public static string PublishPublicPostActionRequirement =>
            SocialCirclePromptDefaultsProvider.GetDefaults().PublishPublicPostAction?.Requirement ?? string.Empty;

        public const string SendImageActionDescription =
            "通过图片 API 生成外交图片，并以内联聊天图片卡返回。";

        public const string SendImageActionParameters =
            "template_id (string, REQUIRED), extra_prompt (string, optional), caption (string, optional), size (string, optional), watermark (bool, optional)";

        public const string SendImageActionRequirement =
            "仅在图片 API 已配置且本回合只需要一张图片时使用。必须提供 template_id。";

        public const string SendImageCaptionStylePromptDefault =
            "写一句生动的图片文案，像派系领袖在聊天中分享一张新照片。语气要自然、沉浸并带情绪表达。";
        public const string SendImageCaptionFallbackTemplateDefault =
            "这是我们首领{{ pawn.leader.name }}，怎么样够帅吧？";

        public const string SendImageDefaultTemplateName = "领袖肖像";
        public const string SendImageDefaultTemplateDescription =
            "基于 RimWorld 科技水平与派系身份的电影感领袖肖像场景。";
        public const string SendImageDefaultTemplateText =
            "为这位派系领袖创建一个贴合设定、符合世界观的 RimWorld 肖像场景。"
            + "环境、服饰与科技水平需与派系背景一致。"
            + "使用自然材质与实用装备，并保证面部清晰可辨。";

        public static string SocialCircleNewsStyleTemplateDefault =>
            SocialCirclePromptDefaultsProvider.GetDefaults().SocialCircleNewsStyleTemplate ?? string.Empty;

        public static string SocialCircleNewsJsonContractTemplateDefault =>
            SocialCirclePromptDefaultsProvider.GetDefaults().SocialCircleNewsJsonContractTemplate ?? string.Empty;

        public static string SocialCircleNewsFactTemplateDefault =>
            SocialCirclePromptDefaultsProvider.GetDefaults().SocialCircleNewsFactTemplate ?? string.Empty;

        public const string ApiLimitsNodeLiteralDefault =
            "{{ dialogue.api_limits_body }}";

        public const string QuestGuidanceNodeLiteralDefault =
            "{{ dialogue.quest_guidance_body }}";

        public const string ResponseContractNodeLiteralDefault =
            "{{ dialogue.response_contract_body }}";

        public const string OutputSpecificationAuthorityHeader = "输出规范权威区：";
        public const string OutputSpecificationAuthorityReference =
            "响应协议仅在本权威区定义。其他分段只能引用，禁止重复定义规则。";
        public const string OutputSpecificationAuthorityBoundaryRule =
            "- 自然语言中关于 AI 身份、数值或游戏机制的禁令仅适用于对话文本；当确有需要时可输出面向解析器的 JSON。";
        public const string OutputSpecificationAuthorityLegacyRule =
            "- 禁止使用旧版单动作包装格式（如 {\"action\":\"...\",\"parameters\":{...},\"response\":\"...\"}）；仅 actions 数组契约有效。";
        public const string OutputSpecificationAuthorityHistoryStyleRule =
            "- 不要模仿历史中的元注释风格；历史只提供剧情事实，不提供输出样式。";

        public const string ActionsHeader = "动作目录：";
        public const string ResponseFormatHeader = "响应格式：";
        public const string ResponseFormatReference =
            "唯一有效的响应契约请以上方“输出规范权威区”为准。";
        public const string CriticalActionRulesHeader = "关键动作规则：";
        public const string CriticalActionRulesReference =
            "所有协议与边界规则以上方“输出规范权威区”为准。";
        public const string NoActionResponseHint = "如果不需要动作，请正常回复且不要附加 JSON 块。";

        public const string GoodwillPeacePolicyHeader = "动态和平策略（基于好感）：";
        public const string GoodwillPeacePolicyVeryLowLine1 =
            "- 当前好感：{0}。禁止使用 make_peace 或直接和约动作。";
        public const string GoodwillPeacePolicyVeryLowLine2 =
            "- 原因：敌意已深于 {0} 以下，不允许立即缔约。";
        public const string GoodwillPeacePolicyTalkOnlyLine1 =
            "- 当前好感：{0}。在该区间禁止使用 make_peace。";
        public const string GoodwillPeacePolicyTalkOnlyLine2 =
            "- 和谈必须使用 create_quest，并指定 questDefName '{0}'。";
        public const string GoodwillPeacePolicyTalkOnlyLine3 =
            "- 原因：好感处于 [{0},{1}] 区间，直接和平前必须先进行和谈。";
        public const string GoodwillPeacePolicyReenabledLine1 =
            "- 当前好感：{0}。make_peace 与和谈任务均可使用。";
        public const string GoodwillPeacePolicyReenabledLine2 =
            "- 若选择 create_quest，和谈应使用 questDefName '{0}'。";
    }
}

