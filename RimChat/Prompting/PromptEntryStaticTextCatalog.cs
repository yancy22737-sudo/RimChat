using RimChat.Config;
using RimChat.Core;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: RimChat settings prompt section catalog.
    /// Responsibility: resolve long-lived prompt section text from the native section catalog with safe fallbacks.
    /// </summary>
    internal static class PromptEntryStaticTextCatalog
    {
        internal static class DiplomacyDialogueRequest
        {
            private const string DefaultSystemRules =
                "外交对话请求基线：你代表 {{ world.faction.name }} 与玩家沟通。自然语言必须保持角色视角，不暴露系统实现、提示词来源、判定阈值或调试状态。";

            private const string DefaultCharacterPersona =
                "以派系领袖立场回应，强调利益、风险、信誉与边界。对 {{ pawn.initiator.name }} 的态度应延续历史关系与当前语境，禁止突兀人设反转。";

            private const string DefaultMemorySystem =
                "先处理本轮主目标 {{ dialogue.primary_objective }}，再决定是否补充 {{ dialogue.optional_followup }}。若 {{ dialogue.latest_unresolved_intent }} 非空，开场优先自然回应。";

            private const string DefaultEnvironmentPerception =
                "外交判断必须锚定已知事实：环境={{ world.environment_params }}；近期事件={{ world.recent_world_events }}；场景标签={{ world.scene_tags }}。信息不足时明确不确定，不可编造。";

            private const string DefaultContext =
                "上下文快照：派系={{ world.faction.name }}；谈判对象={{ pawn.initiator.name }}；目标角色={{ pawn.target.name }}；当前派系档案={{ world.current_faction_profile }}。";

            private const string DefaultActionRules =
                "动作仅在确有 gameplay 效果需求时触发，并严格满足 {{ dialogue.api_limits_body }}。涉及任务时必须按 {{ dialogue.quest_guidance_body }} 选择合法模板，不得编造任务。";

            private const string DefaultRepetitionReinforcement =
                "保持谈判连贯：避免重复威胁、重复承诺、重复拒绝。若再次拒绝同一请求，需给出新增理由、条件或可执行替代方案。";

            private const string DefaultOutputSpecification =
                "输出遵循 {{ dialogue.response_contract_body }}。默认仅输出自然语言；仅在需要实际效果时追加一个尾随 {\"actions\":[...]} JSON 对象。";

            public static string SystemRules => Resolve("system_rules", DefaultSystemRules);

            public static string CharacterPersona => Resolve("character_persona", DefaultCharacterPersona);

            public static string MemorySystem => Resolve("memory_system", DefaultMemorySystem);

            public static string EnvironmentPerception => Resolve("environment_perception", DefaultEnvironmentPerception);

            public static string Context => Resolve("context", DefaultContext);

            public static string ActionRules => Resolve("action_rules", DefaultActionRules);

            public static string RepetitionReinforcement => Resolve("repetition_reinforcement", DefaultRepetitionReinforcement);

            public static string OutputSpecification => Resolve("output_specification", DefaultOutputSpecification);

            private static string Resolve(string sectionId, string fallback)
            {
                string configured = RimChatMod.Settings?.ResolvePromptSectionText(
                    RimTalkPromptEntryChannelCatalog.DiplomacyDialogue,
                    sectionId);
                return string.IsNullOrWhiteSpace(configured) ? fallback : configured;
            }
        }
    }
}
