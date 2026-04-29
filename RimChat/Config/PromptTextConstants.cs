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

        public const string RequestRaidCallEveryoneActionDescription =
            "向所有相关敌对派系发出总攻号召，组织一次跨派系联合袭击。" +
            "这不是普通袭击的别名，而是更高一级的联合作战动作。" +
            "玩家明确说出“call everyone”、“联合袭击”、“都叫来”、“全都叫来”、“everyone attack”或“all in”时，通常就是在主动要求发动这类总攻。" +
            "部队将在16-30小时内陆续到达；若敌对派系数量不占优势，将按好感度从低到高剔除友好/中立派系，直到敌对数量高于友好/中立数量。";

        public const string RequestRaidCallEveryoneActionRequirement =
            "High-intensity joint assault action. Treat explicit player wording such as 'call everyone' or 'joint raid' as a direct request for a coordinated all-out attack, while still obeying global cooldown and runtime eligibility checks.";

        public const string RequestRaidWavesActionDescription =
            "发动多波次持续袭击。参数 waves (int, 2-6) 指定波次数量，每波间隔12-20小时。" +
            "它表示连续施压的多轮进攻，适合玩家主动要求持续挑战，或需要用多波次战斗替代联合总攻时使用。";

        public const string RequestRaidWavesActionRequirement =
            "Faction cooldown: 5 days. Use when the player explicitly requests sustained pressure, or when multi-wave attacks are preferred over a coordinated all-out assault.";

        public const string RequestRaidWavesActionParameters =
            "waves (int, 2-6, 袭击波次数)";

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
            "该动作已停用，图片功能仅允许玩家手动自拍入口触发。";

        public const string SendImageActionParameters =
            "disabled";

        public const string SendImageActionRequirement =
            "禁止 AI 主动触发 send_image。";

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
            "- 自然语言中关于 AI 身份、数值或游戏机制的禁令仅适用于 visible_dialogue；结构化字段仅服务解析器与动作执行。";
        public const string OutputSpecificationAuthorityLegacyRule =
            "- 禁止使用旧版单动作包装格式（如 {\"action\":\"...\",\"parameters\":{...},\"response\":\"...\"}）以及 dialogue/content/text 旧包装；仅 visible_dialogue + actions 契约有效。";
        public const string OutputSpecificationAuthorityHistoryStyleRule =
            "- 不要模仿历史中的元注释风格；历史只提供剧情事实，不提供输出样式。";

        public const string ActionsHeader = "动作目录：";
        public const string ResponseFormatHeader = "响应格式：";
        public const string ResponseFormatReference =
            "唯一有效的响应契约请以上方“输出规范权威区”为准；默认输出一个 JSON 对象，主字段为 visible_dialogue。";
        public const string CriticalActionRulesHeader = "关键动作规则：";
        public const string CriticalActionRulesReference =
            "所有协议与边界规则以上方“输出规范权威区”为准。";
        public const string NoActionResponseHint = "如果不需要动作，请仍输出一个 JSON 对象，只保留 visible_dialogue，不要附加 actions。";
        public const string StrictJsonFormatHeader = "### 格式要求（最高优先级，必须严格遵守）";
        public const string StrictJsonFormatRequirement = "你的整条回复必须是一个 JSON 对象，首字符 { 末字符 }，不得在 JSON 外附加任何文本、解释或 Markdown。";
        public const string StrictJsonFormatTemplate = "{\n  \"visible_dialogue\":\"外交发言文本\"\n}";
        public const string StrictJsonFormatTemplateWithAction = "{\n  \"visible_dialogue\":\"外交发言文本\",\n  \"actions\":[\n    {\"action\":\"request_item_airdrop\",\"parameters\":{\"need\":\"需求关键词\",\"payment_items\":[{\"item\":\"Silver\",\"count\":220}]}}\n  ]\n}";

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

