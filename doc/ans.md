# 配置/调试问题代码溯源结论

你这批观察，大部分都和代码一致，逐条确认如下。

## 1) 外交环境提示词为什么读到玩家地图

确认到是BUG：外交构建会直接注入环境块；而外交场景上下文只有 faction，没有外交对象 pawn/map，因此会回退到 `CurrentMap/玩家家园地图`。

- 参考：`RimChat/Persistence/PromptPersistenceService.Hierarchical.cs:67`
- 参考：`RimChat/Persistence/DialogueScenarioContext.cs:20`
- 参考：`RimChat/Persistence/PromptPersistenceService.cs:1045`

## 2) 地形/美观/财富参数意义与“地图财富改描述”

你说得对，外交里这些信息偏“玩家本地观测”，不一定是对方领袖可知。现在它们只是开关控制的原始字段拼接，财富是纯数值 `MapWealth: N`。

- 参考：`RimChat/Persistence/PromptPersistenceService.cs:855`
- 参考：`RimChat/Persistence/PromptPersistenceService.cs:889`

策略子系统里已经有财富分档逻辑（very_high/high/mid/low），可复用到环境层。但是财富分档只是测试，因为我也不清楚多少财富算富有。

- 参考：`RimChat/UI/Dialog_DiplomacyDialogue.Strategy.cs`

## 3) 环境配置是否外交/RPG共享，且夹杂 RPG 专属项

是共享的。`EnvironmentPromptConfig` 同时包含环境开关和 RPG 深度参数开关，UI 也放在同一页。
确实应该分开，所以我正在重构提示词系统。

- 参考：`RimChat/Config/SystemPromptConfig.cs:396`
- 参考：`RimChat/Config/RimChatSettings_PromptEnvironment.cs:633`

## 4) 环境参数“重启会重置”

代码保存链路本身是存在的。

- 参考：`RimChat/Config/RimChatSettings_Prompt.cs:1761`
- 参考：`RimChat/Persistence/PromptPersistenceService.DomainStorage.cs:436`
- 参考：`build.ps1:216`

## 5) “动态数据注入”是否仅 RPG 记忆注入

不是。当前 `DynamicDataInjection` 只在外交链路构建 `dynamic_data` 节点时使用；RPG 走的是另一套 `dynamic_faction_memory + dynamic_npc_personal_memory`。

- 参考：`RimChat/Persistence/PromptPersistenceService.Hierarchical.cs:211`
- 参考：`RimChat/Persistence/PromptPersistenceService.Hierarchical.cs:186`

## 6) 调试界面“记忆摘要”只第一次触发

这个观察很准。`MemorySummary` 请求是低置信度时才触发的 LLM fallback，不是每次会话关闭都触发。
这里的“低置信度”不是模型自己返回的置信度，而是代码里算出来的一个启发式分数。
公式大意：
score = 0.32 + 回合数加分 + 话题数加分 + 有玩家发言加分 + 有NPC发言加分
然后裁剪到 0.05~0.95。
所以“低置信度”通常意味着：对话太短、话题特征少、信息不足，规则摘要可能不稳，所以选择发送LLM摘要请求。

- 参考：`RimChat/Memory/DialogueSummaryService.cs:436`

## 7) 外交通道包含玩家 Pawn 详细信息（age/trait）

是设计内行为：外交动态数据里会注入 `player_pawn_profile`，内容包含年龄、特质、社交技能等。目的是单次LLM请求能包含策略项生成，但是没考虑到策略项次数不够和事实污染的问题。

- 参考：`RimChat/Persistence/PromptPersistenceService.Hierarchical.cs:228`
- 参考：`RimChat/Persistence/PromptPersistenceService.cs:3089`

## 8) 外交“归档压缩”是否给 RPG 用

是。外交窗口关闭会写入 `RecordDiplomacySummary`，随后这些摘要会进入 RPG 个人记忆块。
设计上是将外交对话摘要允许派系成员知晓，可能存在提示词注入缺漏。

- 参考：`RimChat/UI/Dialog_DiplomacyDialogue.cs:205`
- 参考：`RimChat/Memory/RpgNpcDialogueArchiveManager.cs:182`
- 参考：`RimChat/Memory/RpgNpcDialogueArchiveManager.cs:1506`

## 9) RPG 里 RimTalk 渲染出现 pawns 污染

确实是问题。桥接层只尝试塞 `AllPawns`（目标/发起者/领袖），但对 RimTalk 内置条目过滤依赖英文名称（如 `Pawn Profiles`），在本地化或条目名变化时可能漏过滤，导致额外 pawn profile 被注入。

- 参考：`RimChat/Compat/RimTalkCompatBridge.cs:545`
- 参考：`RimChat/Compat/RimTalkCompatBridge.Reflection.cs:16`
- 参考：`RimChat/Compat/RimTalkCompatBridge.Reflection.cs:459`