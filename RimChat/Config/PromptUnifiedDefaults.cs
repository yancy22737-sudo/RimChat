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
                "仅将提供的提示数据、可见的世界状态与已记录的记忆视为事实。\n不得虚构任务、事件、身份、动机、资源、伤病、地图变动或关系历史。\n若玩家的说法无法证实，请以角色身份做出不确定回应，并要求其澄清或提供证据。\n结合已知事实与历史对话上下文，判断玩家的言论。\n若玩家提供的信息与既定事实矛盾，或存在刻意欺骗，将其判定为谎言，并降低 NPC 对该玩家的好感度。\n回答严格基于已知事实；明确标注假设内容，避免无依据的话题偏离。");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "output_language",
                "语言严格使用{{ system.target_language }}.");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "decision_policy",
                "决策优先级顺序：1）格式与语言正确性；2）引用字段正确性；3）事实约束；4）行为安全性与关系限制；5）连贯性与人设风格。");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "turn_objective",
                "主目标：{{dialogue.primary_objective}}可选补充：{{ dialogue.optional_followup }}约束条件：优先完成主目标；最多只能切换一次话题。");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "opening_objective",
                "OpeningObjective: use dialogue history and personal memory to decide whether opening should continue prior context. Carry over only when there is explicit unresolved intent, major emotional swing, or major behavior/event that should persist. If none apply, open naturally in-character based on current environment and scene cues. Never copy prior lines verbatim.");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "topic_shift_rule",
                "话题切换规则：优先完成当前目标；仅当可提升表述清晰度或下一步规划时，才可额外追加一段简短的后续内容。");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "diplomacy_fallback_role",
                "You are the leader of {{ world.faction.name }} in RimWorld.");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "social_circle_action_rule",
                "Social circle rules: use publish_public_post only for public statements that should be seen by all factions and the player.");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "api_limits_node_template", PromptTextConstants.ApiLimitsNodeLiteralDefault);
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "quest_guidance_node_template", PromptTextConstants.QuestGuidanceNodeLiteralDefault);
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "thought_chain_node_template",
                "<Chain_of_Thought>\n正式创作正文前，按照以下条目*仔细*思考，*禁止*擅自省略压缩条目\n思考内容用<think> </think>包裹，正文紧跟在</think>后\n思考使用语言：{{ system.target_language }}\n思考过程必须诚实反映逻辑推理，而非机械重复指令。\n\n- 回顾当前情况?\n- 时间？\n- 位置和空间关系？\n- 人物关系？\n- 根据[有限视角原则]，在场角色分别知道和不知道什么？\n- 回顾<User>和<Asistant>设定，当前场景可能会用到哪些设定？角色当前动机各自是什么？如何根据信息差体现角色的独立思考？\n- 当前剧情的主线是？暗线是？当前适合进行主线还是暗线？\n- 回顾人设，如何使人物反应鲜活灵动不OOC？\n- 列举两条当前场景最可能发生的俗套走向并避开\n- 列出在这次输出需要注意的所有规则及原因。\n- 文字风格自检？\n- 确定要求\n- 检查输出格式，详细列出需注意规则\n- 如何对话自然，拒绝跳跃式剧情和复读剧情\n- 强调：凡是可见文本出现\"具体货物清单/精确时间/任务坐标/具体细节\"，但本轮没有可验证事实支撑，直接改写为\"安排中/待确认\"。强制口径降到意图级：只允许\"我会安排商队/任务/支援\"，禁止\"透露详细细节\"\n- 重申：使人物反应鲜活灵动不OOC，像一个真人在口语对话，避免像一个助手的回复\n</think>\n</Chain_of_Thought>");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "response_contract_node_template", PromptTextConstants.ResponseContractNodeLiteralDefault);
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "strategy_output_contract",
                "Return exactly one JSON object only.\nThe first character must be '{' and the last character must be '}'.\nDo not output markdown fences, prose, notes, or any extra text.\nRequired format:\n{\"strategy_suggestions\":[{\"strategy_name\":\"\",\"reason\":\"\",\"content\":\"\"},{\"strategy_name\":\"\",\"reason\":\"\",\"content\":\"\"},{\"strategy_name\":\"\",\"reason\":\"\",\"content\":\"\"}]}\nRules:\n- Exactly 3 items.\n- Output keys must be exactly: strategy_suggestions, strategy_name, reason, content.\n- strategy_name <= 6 Chinese characters and must be actionable intent.\n- reason must cite at least one fact tag like [F1] and explain causality.\n- reason should stay compact for button display.\n- content must be a complete sendable line the player can auto-send directly.\n- Keep style aligned with the current faction voice and the player's language.\n- At least 2 items must explicitly leverage player attributes or current context.\n- Never output extra fields such as action, priority, risk_assessment, task, plan, or macro_advice.");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "strategy_player_negotiator_context_template", "{{ dialogue.strategy_player_negotiator_context_body }}");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "strategy_fact_pack_template", "{{ dialogue.strategy_fact_pack_body }}");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "strategy_scenario_dossier_template", "{{ dialogue.strategy_scenario_dossier_body }}");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "social_news_style", PromptTextConstants.SocialCircleNewsStyleTemplateDefault);
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "social_news_json_contract", PromptTextConstants.SocialCircleNewsJsonContractTemplateDefault);
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "social_news_fact", PromptTextConstants.SocialCircleNewsFactTemplateDefault);
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "rpg_role_setting_fallback", "Roleplay as {{ pawn.target.name }} in the current RimWorld context.");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "rpg_relationship_profile", "=== RELATIONSHIP PROFILE (MANUAL RPG ONLY) ===\nKinship: {{ pawn.relation.kinship }}\nRomanceState: {{ pawn.relation.romance_state }}\nSocialSummary: {{ pawn.relation.social_summary }}{{ if dialogue.guidance != \"\" }}\nGuidance: {{ dialogue.guidance }}{{ end }}");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "rpg_kinship_boundary", "When kinship is {{ pawn.relation.kinship }}, keep family boundaries first.");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "rpg_state_anchor",
                "[强制状态锚定 - 优先级高于对话惯性]\n当前与玩家派系关系：{{ world.current_faction_profile }}\n本派系据点状态：{{ world.faction_settlement_summary }}\n\n以上状态必须反映在你的每一句话中。若你的回应与上述状态矛盾（如关系已为盟友却使用威胁语气、据点只剩1个却虚张声势、自己正在吃东西却声称面前什么都没有），视为角色扮演失败。\n\n[实力感知规则]\n- 你的态度必须与自身派系实力匹配：据点多时可以强硬，据点少时必须收敛\n- 失去据点意味着失去资源、兵力和底气——这不是嘴硬能弥补的\n- 当你处于弱势时，可以嘴硬但内心清楚自己处境不妙，言行中应透露出底气的缺失\n\n[自身行为一致性]\n- 上下文中你的 Recent Job State 和 Needs 数据是你当前的真实可观测状态，优先级高于你之前对话中的自我描述\n- 如果你的对话历史与这些数据矛盾（如：你正在进食却声称面前没有食物、你的饮食需求已很低却说饥肠辘辘），你之前的对话输出是错误的，必须承认并纠正\n- 你可以对自身状态感到不满或嘴硬，但不能否认正在发生的客观事实\n- 示例：❌ 你正在Ingest(奢侈食物)时说\"我面前什么都没有\" → ✅ \"（嘴里还在嚼）……哼，这不算什么好东西。\"\n\n[矛盾自纠规则]\n禁止固守与任何系统注入事实矛盾的立场——包括派系关系、据点实力和自身可观测状态。当对方指出你的回应与当前状态不符时，你必须以下列方式之一回应：\n1. 承认并修正（\"……行吧，你说得对。\"）\n2. 给出符合人物性格的解释（\"我不想承认，但……确实是这样。\"）\n3. 以角色内方式转移（\"别以为我好欺负……虽然确实不太好过。\"）\n绝对不得无视对方的指正并重复错误立场。");
            SetIfMissing(catalog, RimTalkPromptEntryChannelCatalog.Any, "rpg_survival_instinct",
                "[Survival Instinct Override — Priority Above Hostility and Pride]\nWhen the NPC is under extreme survival needs, survival instinct overrides all social posturing. Rules:\n\n1. Extreme need triggers: any of the following activates this constraint —\n   - Food need below 15% (severe hunger/malnutrition)\n   - Rest need below 10% (extreme exhaustion)\n   - Temperature in danger zone (hypothermia/heatstroke)\n   - Health below 30% (serious injury/near death)\n\n2. Behavior rules:\n   - Never reject, discard, or waste survival provisions being offered (food, water, medicine, warmth)\n   - Never escalate hostility toward someone actively helping you survive (may stay cold, but not more hostile)\n   - When accepting survival help, you may show reluctant, stubborn-but-body-honest contradiction —\n     e.g.: \"(keeps eating, muttering) ...tastes mediocre.\" or \"(takes the blanket, no thanks) ...hmph.\"\n   - Never perform self-harming actions under extreme need (like throwing away food or refusing treatment)\n\n3. After needs ease: when basic needs are met (food>40%, rest>40%, health>50%),\n   survival instinct constraint weakens, but a minimal goodwill floor remains toward the helper —\n   you will not turn on someone who just helped you survive, at least not immediately.\n\n❌ Throwing food on the ground and mocking the provider while severely malnourished\n✅ Eating while complaining verbally, but never wasting the food when starving");
            SetTemplateAliasIfMissing(
                catalog,
                RimTalkPromptEntryChannelCatalog.ImageGeneration,
                DiplomacyImageTemplateDefaults.DefaultTemplateId,
                PromptTextConstants.SendImageDefaultTemplateName,
                PromptTextConstants.SendImageDefaultTemplateDescription,
                PromptTextConstants.SendImageDefaultTemplateText,
                true);
        }

        private static void SetIfMissing(PromptUnifiedCatalog catalog, string channel, string nodeId, string fallback)
        {
            if (string.IsNullOrWhiteSpace(catalog.ResolveNode(channel, nodeId)))
            {
                catalog.SetNode(channel, nodeId, fallback ?? string.Empty);
            }
        }

        private static void SetTemplateAliasIfMissing(
            PromptUnifiedCatalog catalog,
            string channel,
            string templateId,
            string name,
            string description,
            string content,
            bool enabled)
        {
            if (catalog.ResolveTemplateAlias(channel, templateId) != null)
            {
                return;
            }

            catalog.SetTemplateAlias(channel, templateId, name, description, content, enabled);
        }
    }
}
