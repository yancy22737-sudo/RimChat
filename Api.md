# RimChat AI API 文档

## 概述

`GameAIInterface` 是 RimChat 模组中用于 AI 与游戏交互的核心接口类。它提供了一系列 API 方法，允许 AI 根据对话内容动态调整游戏状态，实现智能外交交互。

## 当前 Prompt 合同（v0.3.120）

- 外交通道默认输出合同已统一为：先输出角色台词；如需 gameplay effect，再追加一个原始 JSON 对象：`{"actions":[...]}`。
- 外交通道不再使用旧的单 `action / parameters / response` 输出模板作为主协议。
- `SystemPrompt_Default.json` 现在只承载外交侧模板与策略。
- RPG 角色设定、格式约束、动作可靠性、开场目标与 topic shift 默认值改由 `Prompt/Default/RpgPrompts_Default.json` 提供。
- `reject_request` 仅用于“明确的玩家请求被正式拒绝”；普通口头拒绝应直接用角色台词表达。
- `publish_public_post` 属于高影响的公开世界动作，只应用于面向全派系的公开声明，不应用于例行聊天或私下讨价还价。

## 外交对话固定消耗（v0.3.116）

- 外交对话中的固定行为成本不再由 LLM 通过 `adjust_goodwill` 间接表达，而是由系统在 API 成功后自动追加。
- `request_caravan`：成功后固定基础消耗 `-15` 好感度。
- `request_aid`：成功后固定基础消耗 `-25` 好感度；`Military` / `Medical` / `Resources` 统一按 `-25` 处理。
- `create_quest`：成功后固定基础消耗 `-10` 好感度。
- `send_gift`：保持现有逻辑，不受本次固定消耗改动影响。
- 只有 `adjust_goodwill` 用于表达“语境导致的额外好感度变化”；不得再用它重复表示上述固定系统成本。

## 动态注入与原版冷却（v0.3.117）

- prompt 动态动作注入会对 `request_caravan`、`request_aid`、`create_quest` 先按固定成本做预判。
- 若当前好感度在执行该动作后会低于 `0`，该动作不会出现在注入给 LLM 的可用动作列表里。
- `request_aid` 冷却改为 `1` 天，`request_caravan` 冷却改为 `4` 天，以对齐原版。

## 核心特性

- **安全限制**: 好感度调整有单次上限和每日累计上限
- **频率控制**: 每个 API 方法都有独立的冷却时间
- **详细日志**: 完整的 API 调用记录和错误追踪
- **可配置**: 所有限制阈值都可在 Mod 选项中调整
- **主动对话**: NPC 可在在线状态下主动发起对话（右侧信件/直接入会话）

## 响应解析与 UI 生命周期修复（v0.3.114）

### AI 响应解析

- 新增：`AIJsonContentExtractor`
  - 提供 `IsErrorPayload(string json)` 与 `TryExtractPrimaryText(string json, out string content)`。
  - 用途：兼容空白、换行、转义字符等格式波动，降低 `"content"` 提取失败概率。
- `AIChatService.ParseResponse(...)` / `AIChatServiceAsync.ParseResponse(...)`
  - 统一改为调用 `AIJsonContentExtractor`，移除硬编码 `IndexOf` 字符串切片解析。

### MainTab 事件生命周期

- `MainTabWindow_RimChat`
  - 新增 `EnsureGoodwillEventSubscription()` / `ClearGoodwillEventSubscription()`。
  - 在 `PreOpen` 保证订阅、`PreClose` 取消订阅，修复窗口二次打开后好感度动画不再播放的问题。

### 存档名解析兜底

- `LeaderMemoryManager.GetCurrentSaveName()`
  - 在 `Current.Game?.Info` 不可用时，增加 `ScribeMetaHeaderUtility.loadedGameName` 反射读取兜底。
  - 新增字符串成员启发式扫描，降低引擎内部成员变更导致解析失败的风险。

## 异步请求生命周期加固（v0.3.113）

### AIChatServiceAsync 新增/调整

- `NotifyGameContextChanged(string reason)`
  - 用途：读档/新开局时通知异步服务切换上下文，取消旧上下文挂起请求。
- `CancelAllPendingRequests(string reason = "...")`
  - 用途：批量取消 Pending/Processing 请求。
- `CleanupCompletedRequests()`（行为增强）
  - 现由服务内部定时调度（10 秒）并附带终态请求数量裁剪，避免历史请求无限堆积。

### 外交通道新增控制器

- `DiplomacyConversationController.TrySendDialogueRequest(...)`
  - 统一发送外交 AI 请求，内部绑定 `FactionDialogueSession` 生命周期校验。
- `DiplomacyConversationController.CancelPendingRequest(FactionDialogueSession session)`
  - 对话窗口关闭时取消挂起请求，避免回调写入失效窗口上下文。

### GameComponent 接入点

- `GameComponent_DiplomacyManager.StartedNewGame()` / `LoadedGame()`
  - 现在会调用 `AIChatServiceAsync.NotifyGameContextChanged(...)`，确保跨存档请求不会污染新会话状态。

## Prompt Policy V3 接口变更（v0.3.120 / 基于 v0.3.110 扩展）

### 配置模型

- `PromptTemplateTextConfig` 新增字段：
  - `DecisionPolicyTemplate`
  - `TurnObjectiveTemplate`
  - `TopicShiftRuleTemplate`
- `SystemPromptConfig` 新增字段：
  - `PromptPolicySchemaVersion`（当前默认：`3`）
  - `PromptPolicy`
- `PromptPolicyConfig` 新增公开配置：
  - `Enabled`
  - `GlobalPromptCharBudget`
  - `NodeBudgets`（`PromptNodeBudgetConfig { NodeId, MaxChars }`）
  - `TrimPriorityNodeIds`
  - `EnableIntentDrivenActionMapping`
  - `IntentActionCooldownTurns`
  - `IntentMinAssistantRoundsForMemory`
  - `IntentNoActionStreakThreshold`
  - `ResetPromptCustomOnSchemaUpgrade`
  - `SummaryTimelineTurnLimit`
  - `SummaryCharBudget`

### Prompt 组装入口（签名不变）

- `BuildFullSystemPrompt(Faction faction, SystemPromptConfig config, bool isProactive, IEnumerable<string> additionalSceneTags)`
- `BuildRPGFullSystemPrompt(Pawn initiator, Pawn target, bool isProactive, IEnumerable<string> additionalSceneTags)`

上述接口签名保持不变，仅内部升级为策略层 + 双层预算组装：
- 新节点：`decision_policy`、`turn_objective`、`topic_shift_rule`；
- RPG 首轮额外节点：`opening_objective`；
- 预算：先节点预算，再全局预算，`fact_grounding`、`turn_objective`、`output_language`、`quest_guidance`、`response_contract` 优先保留。

### RPG 补充接口

- `RpgNpcDialogueArchiveManager.BuildPromptMemoryBlock(Pawn targetNpc, Pawn currentInterlocutor = null, int summaryTurnLimit = 8, int summaryCharBudget = 1200)`
  - 现支持摘要预算参数，输出“历史会话摘要优先 + 最近完整会话少量原句”。
- `RpgNpcDialogueArchiveManager.BuildUnresolvedIntentSummary(Pawn targetNpc, Pawn currentInterlocutor = null)`
  - 未决意图仅从“最近保留全文会话”提炼，供 `turn_objective` 节点组装使用。

## 最近一次对话 Token 用量（v0.3.28）

- UI 位置：`Mod 设置 -> API 配置` 页底部。
- 显示格式：`最近一次对话Token使用量：xxxx（低/中/高）`。
- 统计范围：仅外交对话窗口与 RPG 对话窗口发起的请求。
- 统计口径：
  - 优先读取响应 `usage` 对象中的 token 字段（兼容 `prompt_tokens/input_tokens/promptTokenCount`、`completion_tokens/output_tokens/candidatesTokenCount`、`total_tokens/totalTokenCount`）。
  - 当 usage 缺失，或 usage 与本地估算差异过大时，按“请求+响应文本（4字符≈1token）”回退估算并标记估算状态。
- 分档阈值：
  - 低：`<=1200`
  - 中：`1201~3000`
- 高：`>3000`

---

## RPG 对话行为更新（v0.3.35）

- `ExitDialogueCooldown`：冷却时长为 `60000` ticks（1 天）。
- 记忆回退：RPG 对话达到 `5` 轮后执行一次 `80%` 概率判定，成功时自动追加 `TryGainMemory`。
- Recruit 执行策略：不再做自动补全，仅执行模型原始输出的 Recruit 动作。
- 系统提示可视化：RPG 对话中系统信息改为半透明面板，并显示冷却剩余时长与记忆判定结果。

---

## RPG-外交双向记忆链路（v0.3.29）

### 核心模型

- `CrossChannelSummaryRecord`
  - 字段：`Source`、`FactionId`、`PawnLoadId`、`PawnName`、`SummaryText`、`KeyFacts`、`GameTick`、`Confidence`、`ContentHash`、`IsLlmFallback`、`CreatedTimestamp`。

### 核心服务

- `DialogueSummaryService.TryRecordDiplomacySessionSummary(Faction faction, List<DialogueMessageData> allMessages, int baselineMessageCount)`
  - 外交窗口关闭时调用：仅在有新增消息时生成并写入 1 条外交会话摘要。
- `DialogueSummaryService.TryRecordRpgDepartSummary(Pawn pawn, RpgDialogueTraceSnapshot trace)`
  - 非玩家派系 Pawn 执行 `ExitMap` 且满足过滤条件时调用：生成离图摘要并写入派系记忆。
- `DialogueSummaryService.BuildRpgDynamicFactionMemoryBlock(Faction faction, Pawn targetPawn)`
  - RPG 开聊时构建派系共享动态记忆块（运行时拼接，不覆盖 Persona 持久化字段）。

### 触发链路

- RPG -> 外交：
  - `PawnExitMapPatch_RpgMemory` Patch `Pawn.ExitMap(bool, Rot4)`。
  - 过滤条件：`非玩家派系 + 人形 + 玩家家园地图 + 最近有 RPG 对话痕迹`。
  - 痕迹来源：`RpgDialogueTraceTracker.RegisterTurn(...)`（RPG 开场、玩家发言、NPC回合）。
- 外交 -> RPG：
  - `Dialog_DiplomacyDialogue` 在打开窗口时记录消息基线；
  - `PreClose` 时若 `session.messages.Count > baseline`，触发会话摘要。

### 摘要策略与预算

- 策略：规则优先；低置信（<0.65）触发 LLM 回退；AI 不可用则保留规则摘要。
- 预算：
  - 摘要池上限：`20`（离图池 20、外交池 20）
  - RPG 注入条数：`6`
  - 注入总长上限：`2200` 字符

### 持久化兼容

- `LeaderMemoryJsonCodec` 修正了读写字段映射不一致问题（如 `ownerFactionId/leaderName` 与旧字段兼容）。
- 新增 `rpgDepartSummaries` / `diplomacySessionSummaries` JSON 字段。
- 旧存档缺失新字段时自动回退为空列表，不报错。

### 存档接管初始化（v0.3.30）

- `LeaderMemoryManager.OnNewGame()`
  - 新存档开局即为全部非玩家派系建立记忆基线快照，写入当前 goodwill/关系类型，避免首次对话时记忆为空。
- `LeaderMemoryManager.OnAfterGameLoad(IEnumerable<FactionDialogueSession> loadedSessions)`
  - 读档后加载 JSON 记忆后，回填存档中已存在的 `FactionDialogueSession.messages` 到派系记忆。
  - 对缺少初始化基线的派系补写一次 `init-snapshot` 事件与 5 维关系初值（由当前 goodwill 同步）。
  - 回填采用去重逻辑：仅导入比当前记忆更“新”的会话消息。

### RPG 按 NPC 独立持久化（v0.3.31）

- 新增管理器：`RpgNpcDialogueArchiveManager`
  - `RecordTurn(Pawn initiator, Pawn targetNpc, bool isPlayerSpeaker, string text, int tick, string sessionId = null)`
  - `FinalizeSession(Pawn initiator, Pawn targetNpc, string sessionId, List<ChatMessageData> chatHistory)`
  - `OnBeforeGameSave()`
  - `OnAfterGameLoad()`
- 存储路径：`Prompt/NPC/<saveName>/rpg_npc_dialogues/npc_<pawnId>.json`（旧 `save_data/<saveName>/rpg_npc_dialogues` 自动迁移）
- NPC 档案字段：
  - `PawnLoadId`、`PawnName`、`FactionId`、`FactionName`
  - `LastInteractionTick`、`CooldownUntilTick`
  - `PersonaPrompt`
  - `Sessions[]`（`SessionId/StartedTick/EndedTick/TurnCount/IsFinalized/Interlocutor*/SummaryText/SummaryState/LastSummaryAttemptTick/Turns[]`）
  - 压缩策略：仅保留最近 `TurnCount>=2` 会话全文；其余“已结束会话（IsFinalized=true）”走严格 LLM 一句摘要，失败标记 `summary_failed` 且保留原文。

- `GameComponent_RPGManager` 新增运行态接口（供档案回填）：
  - `TryGetRelation(Pawn pawn, out RPGRelationValues relation)`
  - `SetRelationValues(Pawn pawn, RPGRelationValues relationValues)`
  - `GetDialogueCooldownUntilTick(Pawn pawn)`
  - `SetDialogueCooldownUntilTick(Pawn pawn, int untilTick)`

---

## 环境提示词接口（v0.3.23）

环境层统一由 `PromptPersistenceService` 组装并前置注入到外交/RPG系统提示词中。

### 核心入口

- `BuildFullSystemPrompt(Faction faction, SystemPromptConfig config, bool isProactive, IEnumerable<string> additionalSceneTags)`
- `BuildRPGFullSystemPrompt(Pawn initiator, Pawn target, bool isProactive, IEnumerable<string> additionalSceneTags)`
- `BuildEnvironmentPromptBlocks(SystemPromptConfig config, DialogueScenarioContext context)`（内部组装入口）
- `AppendRecentWorldEventIntel(StringBuilder sb, EnvironmentPromptConfig env, DialogueScenarioContext context)`（内部注入块）

### Prompt 构建编排拆分（v0.3.61）

- 外交组装入口仍为：
  - `PromptPersistenceService.BuildFullSystemPrompt(...)`
  - 现委托给 `RimChat.Prompting.Builders.DiplomacyPromptBuilder.Build(...)`，再进入层级核心构建。
- RPG 组装入口仍为：
  - `PromptPersistenceService.BuildRPGFullSystemPrompt(...)`
  - 现委托给 `RimChat.Prompting.Builders.RpgPromptBuilder.Build(...)`，再进入层级核心构建。
- 层级核心组装（内部）：
  - `PromptPersistenceService.BuildFullSystemPromptHierarchicalCore(...)`
  - `PromptPersistenceService.BuildRpgSystemPromptHierarchicalCore(...)`
- Prompt 配置文件 IO（内部）：
  - `RimChat.Persistence.PromptConfigStore.Exists()`
  - `RimChat.Persistence.PromptConfigStore.ReadAllText()`
  - `RimChat.Persistence.PromptConfigStore.WriteAllText(string content)`

说明：本次拆分只调整编排层结构，不改变外交/RPG 最终提示词内容和执行行为。

### Prompt JSON 编解码（v0.3.62）

- 新增内部编码器：`RimChat.Persistence.PromptConfigJsonCodec`
  - `TrySerialize(SystemPromptConfig config, bool prettyPrint, out string json)`
  - `TryDeserialize(string json, out SystemPromptConfig config, out string error)`
- `PromptPersistenceService` 调整：
  - `SerializeConfigToJson(...)`：先走 typed codec，失败才回退旧版字符串拼接序列化。
  - `ParseJsonToConfigInternal(...)`：先走 typed codec，失败才回退旧版字符串解析。

说明：该改动优先提高配置读写鲁棒性，同时保留旧解析链作为兼容兜底。

### Prompt JSON 运行时修复（v0.3.63）

- `PromptConfigJsonCodec` 调整：
  - 序列化：`UnityEngine.JsonUtility.ToJson(...)`
  - 反序列化：`UnityEngine.JsonUtility.FromJson<SystemPromptConfig>(...)`
- 模型兼容性：
  - 为 `SystemPromptConfig` 相关配置类与 `EventIntelPromptConfig` 增加 `[Serializable]`。
- 项目依赖调整：
  - 删除 `System.Web.Extensions`，新增 `UnityEngine.JSONSerializeModule`。

修复目标：消除 RimWorld 运行时 `TypeLoadException`（`System.Web.Script.Serialization.JavaScriptSerializer` 不可解析）并保持配置读写兼容回退链不变。

### Prompt 文本模板外置（v0.3.64）

- 新增配置模型：`PromptTemplateTextConfig`
  - `Enabled`
  - `FactGroundingTemplate`
  - `OutputLanguageTemplate`
- `SystemPromptConfig` 根节点新增：
  - `PromptTemplates`
- 模板渲染器：
  - `PromptTemplateRenderer.Render(string templateText, IReadOnlyDictionary<string, string> variables)`
  - 语法：`{{variable_name}}`
  - 未匹配变量保持原样（便于排错）
- 分层构建接入：
  - `fact_grounding` 节点：优先渲染 `PromptTemplates.FactGroundingTemplate`
  - `output_language` 节点：优先渲染 `PromptTemplates.OutputLanguageTemplate`
  - 当模板为空或未启用时，回退旧逻辑
- 可用变量（共享）：
  - `{{channel}}`（`diplomacy` / `rpg`）
  - `{{mode}}`（`manual` / `proactive`）
  - `{{target_language}}`
  - `{{faction_name}}`
  - `{{initiator_name}}`
  - `{{target_name}}`

### Prompt 文本模板外置扩展（v0.3.65）

- `PromptTemplateTextConfig` 新增字段：
  - `DiplomacyFallbackRoleTemplate`
- 分层构建接入新增：
  - 外交 `faction_characteristics` 在无派系专属 Prompt 时，优先渲染 `DiplomacyFallbackRoleTemplate`。
  - RPG `role_setting`、格式约束、可靠性、开场目标与 topic shift 现在改由 `Prompt/Default/RpgPrompts_Default.json` 提供，不再从外交 `PromptTemplates` 读取。

### 社交圈动作规则模板（v0.3.105）

- `PromptTemplateTextConfig` 新增字段：
  - `SocialCircleActionRuleTemplate`
- 分层构建接入：
  - 外交通道 `instruction_stack` 新增 `social_circle_action_rule` 节点；
  - 当 `PromptTemplates.Enabled == true` 且模板非空时，渲染 `SocialCircleActionRuleTemplate`；
  - 模板为空时回退到内置最小规则文本。
- 持久化链路：
  - 默认值来自 `Prompt/Default/SystemPrompt_Default.json`；
  - 运行时编辑保存到 `Prompt/Custom/system_prompt_config.json`；
  - 旧配置缺失该字段时，加载阶段自动从默认模板回填。

### Prompt 节点包装模板（v0.3.66）

- `PromptTemplateTextConfig` 新增字段：
  - `ApiLimitsNodeTemplate`
  - `QuestGuidanceNodeTemplate`
  - `ResponseContractNodeTemplate`
- 分层构建新增包装渲染阶段（外交通道）：
  - `api_limits`：先构建动态正文，再由模板包装
  - `quest_guidance`：先构建动态正文，再由模板包装
  - `response_contract`：先构建动态正文，再由模板包装
- 默认占位符：
  - `{{api_limits_body}}`
  - `{{quest_guidance_body}}`
  - `{{response_contract_body}}`

### Prompt 文本去重清理（v0.3.67）

- 模板字段默认文本来源调整：
  - 长文本默认值不再在构造函数中硬编码重复维护。
  - `Prompt/Default/SystemPrompt_Default.json` 作为长模板文本唯一默认源。
- 构建 fallback 调整：
  - 模板缺失时返回精简兜底提示，不再复制默认模板全文。

### Prompt 常量单源（v0.3.68）

- 新增：`PromptTextConstants`
  - 统一承载重复提示词文本常量（RPG 默认提示词、部分 API 动作提示词描述与参数）。
- 调整：
  - `RimChatSettings` 默认 RPG 提示词读取改为常量引用（初始化、Scribe 默认、迁移兜底）。
  - `SystemPromptConfig` 与 `PromptPersistenceService` 中重复 API 动作提示词描述改为常量引用。

### Prompt 段落常量收敛（v0.3.69）

- `PromptTextConstants` 新增回复合约段落标题常量：
  - `ACTIONS`
  - `DECISION GUIDELINES`
  - `RESPONSE FORMAT`
  - 以及 relation/important/no-action 等通用提示行
- `AppendSimpleConfig` / `AppendAdvancedConfig` 改为统一引用上述常量，避免同段提示词重复维护。

### Prompt 模板回填修复（v0.3.70）

- 新增迁移策略：
  - 当运行配置中的 `PromptTemplates` 某字段为空时，加载阶段自动从默认模板配置回填该字段。
- 回填来源：
  - `Prompt/Default/SystemPrompt_Default.json`
- 行为：
  - 仅回填“缺失值”，不会覆盖用户已填写模板。
  - 回填发生后自动保存配置，避免后续再次空白。

### 注入顺序

- `Worldview -> Environment Parameters -> Recent World Events & Battle Intel -> Scene Prompt Layers -> Existing Prompt Stack`

### EnvironmentContextSwitches（新）

- `Enabled`
- `IncludeTime`
- `IncludeDate`
- `IncludeSeason`
- `IncludeWeather`
- `IncludeLocationAndTemperature`
- `IncludeTerrain`
- `IncludeBeauty`
- `IncludeCleanliness`
- `IncludeSurroundings`
- `IncludeWealth`

以上开关控制环境参数层按项注入；若配置缺失会自动回退默认值（兼容旧配置）。

### EventIntelPrompt（新）

- `Enabled`
- `ApplyToDiplomacy`
- `ApplyToRpg`
- `IncludeMapEvents`
- `IncludeRaidBattleReports`
- `DaysWindow`
- `MaxStoredRecords`
- `MaxInjectedItems`
- `MaxInjectedChars`

### 世界事件账本接口（新）

- `WorldEventLedgerComponent : GameComponent`
- `WorldEventRecord`
- `RaidBattleReportRecord`
- `GetRecentWorldEvents(Faction observerFaction, int daysWindow, bool includePublic, bool includeDirect)`
- `GetRecentRaidBattleReports(Faction observerFaction, int daysWindow, bool includeDirect)`

可知性规则：
- `PublicKnown`（公开地图事件）按 `IsPublic=true` 注入摘要。
- `DirectKnown`（派系直接参与事件）按 `KnownFactionIds` 过滤，支持袭击战报完整伤亡摘要。

---

## NPC 主动对话接口（v0.3.9）

主动对话由 `GameComponent_NpcDialoguePushManager` 统一调度，外部 Patch 通过入口方法上报触发事件。

### 类型定义

- `NpcDialogueTriggerType`
  - `Ambient` / `Conditional` / `Causal`
- `NpcDialogueCategory`
  - `Social` / `DiplomacyTask` / `WarningThreat`
- `NpcDialogueTriggerContext`
  - 运行时触发上下文（派系、触发类型、原因、严重度、好感变化等）
- `QueuedNpcDialogueTrigger`
  - 延迟队列持久化项（含 `dueTick/expireTick`）
- `FactionNpcPushState`
  - 派系推送状态（冷却、上次互动、上次负向激增）

### Patch 上报入口

```csharp
// 交易后置：玩家卖出 Poor 及以下武器
GameComponent_NpcDialoguePushManager.Instance?.RegisterLowQualityTradeTrigger(
    faction,
    lowQualityCount,
    worstQuality
);

// 好感变动后置：单次绝对变化 >= 10
GameComponent_NpcDialoguePushManager.Instance?.RegisterGoodwillShiftTrigger(
    faction,
    goodwillDelta,
    reasonTag,
    likelyHostile
);

// UI 帧内鼠标左键采样（忙碌判定）
GameComponent_NpcDialoguePushManager.Instance?.RegisterPlayerLeftClick();
```

### 调试入口

```csharp
// 强制触发一条随机主动对话（调试按钮调用）
bool ok = GameComponent_NpcDialoguePushManager.Instance?.DebugForceRandomProactiveDialogue() == true;
```

### 投递接口

- `ChoiceLetter_NpcInitiatedDialogue`
  - `Setup(Faction faction, TaggedString labelText, TaggedString bodyText, LetterDef letterDef)`
  - `IsDialogueAlreadyOpen(Faction faction)`
  - 信件选项包含“打开外交对话”，可直接拉起 `Dialog_DiplomacyDialogue`

### 运行规则（固定策略）

- 评估频率：每 `6000` ticks 一次常规评估；每 `600` ticks 处理队列。
- 冷却：同派系主动发言成功后进入 `1~3` 天随机冷却。
- 忙碌判定（三重）：`Drafted` / 敌对单位在玩家家园地图 / `6` 秒内左键点击 `>=12`。
- 在线门控：仅 `Online` 直接发起，`Offline/DoNotDisturb` 入队等待。
- 队列：每派系上限默认 `3`，默认 `12` 小时过期。
- LLM：每条主动消息都走 LLM；失败重试 `1` 次，仍失败即丢弃并写日志。

---

## PawnRPG 主动通道接口（v0.3.19）

`GameComponent_PawnRpgDialoguePushManager` 是独立于旧派系主动通道的 PawnRPG 主动对话调度器。旧通道保持原行为不变；PawnRPG 通道支持非玩家派系对玩家 Pawn，以及玩家派系内部 Pawn 对 Pawn 主动对话。

### 类型定义

- `PawnRpgTriggerContext`
  - 运行时触发上下文（派系、触发类型、分类、原因、严重度、元数据）。
- `QueuedPawnRpgTrigger`
  - PawnRPG 延迟队列持久化项（`enqueuedTick/dueTick/expireTick`）。
- `PawnRpgNpcPushState`
  - 按 NPC 记录成功投递时间锚（`lastNpcEvaluateTick`）。
- `PawnRpgThreatState`
  - 按派系记录威胁边沿状态（避免虫巢/敌对持续状态重复刷警告）。

### Patch 上报入口

```csharp
// 交易完成后置
GameComponent_PawnRpgDialoguePushManager.Instance?.RegisterTradeCompletedTrigger(
    faction,
    soldCount,
    boughtCount
);

// 好感大幅变动后置（|delta| >= 10）
GameComponent_PawnRpgDialoguePushManager.Instance?.RegisterGoodwillShiftTrigger(
    faction,
    goodwillDelta,
    reasonTag,
    likelyHostile
);

// UI 帧内鼠标左键采样（忙碌判定）
GameComponent_PawnRpgDialoguePushManager.Instance?.RegisterPlayerLeftClick();
```

### 调试入口

```csharp
// 强制触发一条 PawnRPG 主动对话（调试按钮调用）
bool ok = GameComponent_PawnRpgDialoguePushManager.Instance?.DebugForcePawnRpgProactiveDialogue() == true;
```

### 投递接口

- `ChoiceLetter_PawnRpgInitiatedDialogue`
  - `Setup(Pawn npcPawn, Pawn playerPawn, TaggedString labelText, TaggedString bodyText, LetterDef letterDef)`
  - `IsDialogueAlreadyOpen(Pawn playerPawn, Pawn npcPawn)`
  - 信件选项包含“打开 PawnRPG 对话”，可直接拉起 `Dialog_RPGPawnDialogue(playerPawn, npcPawn)`。

### 运行规则（固定策略）

- 评估频率：常规评估每 `6000` ticks；队列处理每 `600` ticks。
- 6天单NPC节流：同一 NPC 成功投递后 `150000` ticks 内不再评估/发起。
- 3天全局节流：非警告类成功投递后 `75000` ticks 内全殖民地不再成功投递 PawnRPG 主动消息。
- 警告例外：`WarningThreat` 仅绕过 3 天全局节流，不绕过 6 天单 NPC 节流。
- 关系阈值：亲密关系（配偶/未婚/恋人）直通，否则 `Opinion >= 35`。
- 低心情阈值：`Mood <= 0.30` 才触发条件类。
- 忙碌三重判定：`Drafted` / 敌对单位 / `6` 秒内左键 `>=12`。
- 可用性门控：NPC 与玩家 Pawn 睡觉/昏迷/工作中时入队等待。
- 队列：每派系上限默认 `3`，默认 `12` 小时过期。
- LLM：失败重试 `1` 次后丢弃；冷却计数仅按“成功投递”更新。
- 信件打开：从 PawnRPG 主动信件进入 `Dialog_RPGPawnDialogue` 时，主动消息会作为首条 NPC 发言注入，不会重新请求开场。

---

## 快速开始

### 获取接口实例

```csharp
// 获取单例实例
GameAIInterface aiInterface = GameAIInterface.Instance;
```

### 基本调用示例

```csharp
// 获取派系信息
var result = aiInterface.GetFactionInfo(someFaction);
if (result.Success)
{
    Log.Message(result.Message);
    // 使用 result.Data 获取详细数据
}

// 调整好感度
var adjustResult = aiInterface.AdjustGoodwill(targetFaction, 10, "Diplomatic dialogue");
if (!adjustResult.Success)
{
    Log.Warning($"Failed to adjust goodwill: {adjustResult.Message}");
}
```

---

## API 方法详解

### 1. 好感度管理

#### AdjustGoodwill
调整目标派系的好感度。

**参数:**
| 参数名 | 类型 | 说明 |
|--------|------|------|
| faction | Faction | 目标派系 |
| amount | int | 调整值（正数增加，负数减少） |
| reason | string | 调整原因（用于日志） |

**返回值:** `APIResult`
- `Success`: 是否成功
- `Message`: 操作结果描述
- `Data`: 包含旧/新好感度值的对象

**限制:**
- 单次调整上限：默认 15（可在设置中调整）
- 每日累计上限：默认 30
- 冷却时间：默认 1 小时

**示例:**
```csharp
var result = GameAIInterface.Instance.AdjustGoodwill(
    targetFaction, 
    10, 
    "Successful trade negotiation"
);

if (result.Success)
{
    var data = result.Data as dynamic;
    Log.Message($"Goodwill changed from {data.OldGoodwill} to {data.NewGoodwill}");
}
```

---

#### GetCurrentGoodwill
获取当前与指定派系的好感度。

**参数:**
| 参数名 | 类型 | 说明 |
|--------|------|------|
| faction | Faction | 目标派系 |

**返回值数据:**
```csharp
{
    FactionName: string,
    Goodwill: int,
    RelationKind: string,  // "Hostile", "Neutral", "Ally"
    IsHostile: bool,
    IsAlly: bool
}
```

---

### 2. 外交操作

#### SendGift
向派系发送礼物以提升好感度。

**参数:**
| 参数名 | 类型 | 说明 |
|--------|------|------|
| faction | Faction | 目标派系 |
| silverAmount | int | 白银数量 |
| goodwillGain | int | 预期获得的好感度 |

**限制:**
- 最大白银：默认 1000
- 最大好感度收益：默认 10
- 冷却时间：默认 1 天

---

#### RequestAid
请求派系提供援助。

**参数:**
| 参数名 | 类型 | 说明 |
|--------|------|------|
| faction | Faction | 目标派系 |
| aidType | string | 援助类型（"Military", "Medical", "Resources"） |

**限制:**
- 仅可向盟友请求
- 最低好感度要求：默认 40
- 冷却时间：默认 1 天（对齐原版军事援助）
- 外交对话中成功后会自动追加固定基础消耗 `-25`

---

#### DeclareWar
向派系宣战。

**参数:**
| 参数名 | 类型 | 说明 |
|--------|------|------|
| faction | Faction | 目标派系 |
| reason | string | 宣战原因 |

**限制:**
- 好感度必须低于阈值：默认 -50
- 冷却时间：默认 1 天
- 不能对已是敌对的派系宣战

---

#### MakePeace
与派系签订和平条约。

**参数:**
| 参数名 | 类型 | 说明 |
|--------|------|------|
| faction | Faction | 目标派系 |
| peaceCost | int | 和平代价（白银） |

**限制:**
- 仅可与敌对派系议和
- 最大和平代价：默认 5000
- 冷却时间：默认 1 天

---

### 3. 贸易与商队

#### RequestTradeCaravan
请求派系派遣贸易商队。

**参数:**
| 参数名 | 类型 | 说明 |
|--------|------|------|
| faction | Faction | 目标派系 |
| requestedGoods | string | 请求的商品类型（可选） |

**限制:**
- 不能向敌对派系请求
- 冷却时间：默认 4 天（对齐原版商队请求）
- 外交对话中成功后会自动追加固定基础消耗 `-15`

---

#### RequestRaid
请求派系发动袭击。

**参数:**
| 参数名 | 类型 | 说明 |
|--------|------|------|
| faction | Faction | 目标派系 |
| points | float | 袭击点数 (默认 -1 表示自动计算) |
| strategyDefName | string | 袭击策略 DefName (如 "ImmediateAttack", "Siege") |
| arrivalModeDefName | string | 入场方式 DefName (如 "EdgeWalkIn", "CenterDrop") |
| delayed | bool | 是否延迟执行 (默认 true) |

**限制:**
- 冷却时间：默认 3 天
- 延迟时间：
  - EdgeWalkIn/Siege: 6~8 小时
  - DropPods: 1~2 小时

---

#### CreateQuest
使用原版任务模板创建并向玩家发布一个任务。

**参数:**
| 参数名 | 类型 | 说明 |
|--------|------|------|
| questDefName | string | **必需**。原版任务模板的 DefName。必须从当前 prompt 动态注入的 Available 列表中选择。 |
| askerFaction | string | 可选。任务发起派系的名字。默认为当前派系。 |
| points | int | 可选。任务的威胁点数。若不提供，系统将根据玩家当前实力自动计算。 |

---

### 4. 社交圈公开公告（v0.3.14）

#### publish_public_post（AI 动作协议）
用于将当前外交内容转为“全派系可见”的公开公告，进入社交圈 feed。应谨慎使用，不应用于例行聊天或私下协商。

**参数（建议通过 `parameters` 对象提供）：**
| 参数名 | 类型 | 说明 |
|--------|------|------|
| category | string | 公告类别：`Military/Economic/Diplomatic/Anomaly` |
| sentiment | int | 情绪方向，范围 `-2..2` |
| summary | string | 公告正文（可选，未提供则用规则模板） |
| targetFaction | string | 被提及派系名或 defName（可选） |
| intentHint | string | 行动意图提示（可选） |

#### GameComponent_DiplomacyManager.EnqueuePublicPost
将公告写入社交圈并应用关联影响链：
- 软影响：发帖/被提及派系对玩家好感同步变化（单次钳制 `[-4,4]`）。
- 扩展影响：按帖子类型尝试触发 `新增定居点 / 丢失定居点 / 寒潮 / 作物枯萎 / 热浪 / 太阳耀斑 / 雷暴` 之一（均为原版 Core 事件，受世界状态约束）。
- 帖子正文会注入发帖派系领袖信息，影响描述与执行结果保持关联。

#### GameComponent_DiplomacyManager.ForceGeneratePublicPost
调试入口，立即按规则强制生成一条公告，并重排下一次自动生成时间。

#### GameComponent_DiplomacyManager.GetSocialPosts / GetUnreadSocialPostCount / MarkSocialPostsRead
提供社交圈 UI 所需的 feed 与未读状态接口。

#### GameComponent_DiplomacyManager.TryLikeSocialPost
外交对话窗口社交圈页签中的点赞接口：
- 记录玩家点赞状态（单帖一次）。
- 按派系定居点规模影响默认点赞基数。
- 低概率给予玩家 `+1~2` 对该发帖派系好感度奖励。

**推荐任务模板清单:**
- `ThreatReward_Raid_MiscReward`: 抵御袭击并获得奖励
- `Mission_BanditCamp`: 摧毁敌对营地
- `OpportunitySite_PeaceTalks`: 和平会谈
- `TradeRequest`: 交付特定物资
- `Hospitality_Refugee`: 接纳难民
- `PawnLend`: 租借殖民者
- `AncientComplex_Mission`: 探索古代遗迹
- `SurveySite`: 实地考察考察

**示例:**
```json
{
  "action": "create_quest",
  "parameters": {
    "questDefName": "Mission_BanditCamp",
    "points": 1000
  }
}
```

**限制:**
- 必须提供 `questDefName`。不再支持无模板的纯自定义任务。
- 系统会自动补全 `map` 等环境参数。

---

### 4. 状态查询

#### GetFactionInfo
获取派系的详细信息。

**返回值数据:**
```csharp
{
    Name: string,
    DefName: string,
    Goodwill: int,
    RelationKind: string,
    IsPlayer: bool,
    IsDefeated: bool,
    IsHidden: bool,
    LeaderName: string,
    SettlementCount: int,
    TodayAdjustment: int  // 今日已调整的好感度
}
```

---

#### GetAllFactions
获取所有可用派系的列表。

**返回值数据:**
```csharp
List<{
    Name: string,
    Goodwill: int,
    RelationKind: string,
    IsAIControlled: bool
}>
```

---

#### GetColonyStatus
获取殖民地当前状态。

**返回值数据:**
```csharp
{
    ColonyName: string,
    MapCount: int,
    TotalColonists: int,
    TotalWealth: float,
    GameDate: string,
    ThreatLevel: float
}
```

---

## 安全机制

### 冷却系统

每个 API 方法都有独立的冷却时间，防止 AI 过度调用：

| 方法 | 默认冷却 | 可配置范围 |
|------|----------|------------|
| AdjustGoodwill | 0 小时（无冷却） | 0-24 小时 |
| SendGift | 1 天 | 0.5-5 天 |
| RequestAid | 1 天 | 1-7 天 |
| DeclareWar | 1 天 | 1-7 天 |
| MakePeace | 1 天 | 1-7 天 |
| RequestTradeCaravan | 4 天 | 0.5-5 天 |

**查询剩余冷却时间:**
```csharp
int remainingSeconds = GameAIInterface.Instance.GetRemainingCooldownSeconds("AdjustGoodwill");
```

### 好感度调整限制

1. **单次调整上限**: 默认 15 点（范围：0-50）
2. **每日累计上限**: 默认 30 点（范围：0-100）
3. **自动截断**: 超出限制的请求会被自动截断到允许范围

### 权限验证

```csharp
// 验证 AI 是否有权限操作指定派系
bool hasPermission = GameAIInterface.Instance.ValidateAIPermission(targetFaction);
```

---

## 配置选项

所有限制都可在 Mod 设置中调整：

### 好感度设置
- `MaxGoodwillAdjustmentPerCall`: 单次调整上限 (1-50)
- `MaxDailyGoodwillAdjustment`: 每日累计上限 (10-100)
- `GoodwillCooldownTicks`: 冷却时间 (0.5-24 小时)

### 礼物设置
- `MaxGiftSilverAmount`: 最大白银数量 (100-5000)
- `MaxGiftGoodwillGain`: 最大好感度收益 (1-25)
- `GiftCooldownTicks`: 冷却时间 (0.5-5 天)

### 战争与和平
- `MaxGoodwillForWarDeclaration`: 宣战最大好感度 (-100-0)
- `MaxPeaceCost`: 最大和平代价 (0-10000)
- `PeaceGoodwillReset`: 议和后好感度重置值 (-100-0)

---

## 错误处理

所有 API 方法都返回 `APIResult` 对象：

```csharp
public class APIResult
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public object Data { get; set; }
}
```

**常见错误信息:**
- `"Settings not initialized"` - 设置未初始化
- `"Faction cannot be null"` - 派系参数为空
- `"Method X is on cooldown"` - 方法处于冷却中
- `"Daily goodwill adjustment limit reached"` - 超出每日调整上限
- `"Can only request aid from allied factions"` - 关系不满足要求

---

## 调试与日志

### 启用 API 调用日志

在 Mod 设置的 AI Control 选项卡中启用 `Enable API Call Logging`。

### 获取调用历史

```csharp
// 获取最近的 50 条调用记录
var history = GameAIInterface.Instance.GetAPICallHistory(maxRecords: 50);

// 获取特定方法的调用记录
var goodwillHistory = GameAIInterface.Instance.GetAPICallHistory("AdjustGoodwill");

foreach (var record in history)
{
    Log.Message($"[{record.TickCalled}] {record.MethodName}: {record.Parameters} - {(record.Success ? "Success" : "Failed")}");
}
```

---

## 最佳实践

### 1. 检查冷却时间

在调用可能处于冷却的方法前，先检查剩余时间：

```csharp
int cooldown = GameAIInterface.Instance.GetRemainingCooldownSeconds("AdjustGoodwill");
if (cooldown > 0)
{
    Log.Message($"Please wait {cooldown} seconds before adjusting goodwill again");
    return;
}
```

### 2. 验证权限

在执行敏感操作前验证 AI 权限：

```csharp
if (!GameAIInterface.Instance.ValidateAIPermission(faction))
{
    Log.Warning("AI does not have permission to interact with this faction");
    return;
}
```

### 3. 处理失败结果

始终检查 API 调用结果：

```csharp
var result = GameAIInterface.Instance.AdjustGoodwill(faction, amount, reason);
if (!result.Success)
{
    // 根据错误类型采取不同措施
    if (result.Message.Contains("cooldown"))
    {
        // 等待冷却结束
    }
    else if (result.Message.Contains("limit"))
    {
        // 调整策略
    }
}
```

---

## 与 LLM 集成示例

以下是一个与 LLM API 集成的完整示例：

```csharp
public class AIDiplomacyService
{
    private GameAIInterface _interface;
    
    public AIDiplomacyService()
    {
        _interface = GameAIInterface.Instance;
    }
    
    public async Task ProcessDialogue(Faction faction, string playerMessage)
    {
        // 1. 获取当前状态
        var statusResult = _interface.GetColonyStatus();
        var factionResult = _interface.GetFactionInfo(faction);
        
        // 2. 构建 LLM 提示
        var prompt = BuildPrompt(faction, playerMessage, statusResult.Data, factionResult.Data);
        
        // 3. 调用 LLM
        var llmResponse = await CallLLM(prompt);
        
        // 4. 解析 LLM 的 API 调用意图
        var intendedAction = ParseAction(llmResponse);
        
        // 5. 执行游戏 API 调用
        switch (intendedAction.Type)
        {
            case "adjust_goodwill":
                var result = _interface.AdjustGoodwill(
                    faction, 
                    intendedAction.Amount, 
                    intendedAction.Reason
                );
                
                if (!result.Success)
                {
                    // 通知 LLM 调用失败，请求调整策略
                    await NotifyFailure(llmResponse, result.Message);
                }
                break;
                
            case "declare_war":
                _interface.DeclareWar(faction, intendedAction.Reason);
                break;
                
            // ... 其他操作
        }
    }
}
```

---

## LLM 集成指南

### 概述

RimChat 支持 LLM（大语言模型）通过 JSON 格式响应来调用游戏 API。这使得 AI 能够根据对话内容动态调整游戏状态，实现智能外交交互。

### 系统提示词

当玩家与 AI 派系对话时，LLM 会收到包含以下信息的系统提示词。系统提示词会**动态包含当前的 Mod 设置参数**，确保 LLM 了解实时的 API 限制。

```
=== FACTION INFO ===
Name: {派系名称}
Type: {派系类型}
Current Goodwill: {好感度}
Relation: {关系状态}
Leader: {领袖名称}
Leader Traits: {特质列表}
Ideology: {意识形态}

=== AVAILABLE ACTIONS ===
You can perform diplomatic actions by including a JSON block in your response.

=== CURRENT API LIMITS (MUST FOLLOW) ===
- Max goodwill adjustment per call: {当前设置值} (range: 0 to {当前设置值})
- Max daily goodwill adjustment: {当前设置值}
- Goodwill cooldown: {当前设置值} hours
- Max gift silver: {当前设置值}
- Max gift goodwill gain: {当前设置值}
- Min goodwill for aid: {当前设置值}
- Max goodwill for war declaration: {当前设置值}
- Max peace cost: {当前设置值}
- Peace goodwill reset: {当前设置值}

ENABLED FEATURES:
- Goodwill adjustment: {YES/NO}
- Gift sending: {YES/NO}
- War declaration: {YES/NO}
- Peace making: {YES/NO}
- Trade caravan: {YES/NO}
- Aid request: {YES/NO}

ACTIONS:
1. adjust_goodwill - Change faction relations
   Parameters: amount (int, -{当前单次上限} to {当前单次上限}), reason (string)
   Daily limit remaining: {当前每日上限} total per day
2. send_gift - Send silver to improve relations
   Parameters: silver (int, max {当前最大白银}), goodwill_gain (int, 1-{当前最大收益})
3. request_aid - Request military/medical aid (requires ally)
   Parameters: type (string: Military/Medical/Resources)
   Requirement: goodwill >= {当前最低要求}
4. declare_war - Declare war
   Parameters: reason (string)
   Requirement: goodwill <= {当前宣战阈值}
5. make_peace - Offer peace treaty (requires war)
   Parameters: cost (int, max {当前最大代价} silver)
   Result: goodwill reset to {当前重置值}
6. request_caravan - Request trade caravan
   Parameters: goods (string, optional)
   Requirement: not hostile
7. reject_request - Reject player's request
   Parameters: reason (string)

DECISION GUIDELINES:
- Current goodwill {value}: {行为建议}
- Consider your leader's traits and ideology when making decisions
- You can accept or reject player requests based on current relations
- Small goodwill changes (1-{当前单次上限/3}) for minor interactions
- Medium changes ({当前单次上限/3}-{当前单次上限*2/3}) for moderate events
- Large changes ({当前单次上限*2/3}-{当前单次上限}) for significant diplomatic events

RESPONSE FORMAT:
Respond with your in-character dialogue first. If gameplay effects are needed, append one raw JSON object using the `actions` array contract:

```json
{
  "actions": [
    {
      "action": "snake_case_action",
      "parameters": {
        "param1": "value"
      }
    }
  ]
}
```

IMPORTANT RULES:
1. NEVER exceed the max values shown above
2. ONLY use enabled features
3. ALWAYS check requirements before using an action
4. If an action is unavailable, refuse through an in-world reason instead of exposing system state

If no action is needed, respond normally without JSON.
```

**注意**: 系统提示词中的 `{当前设置值}` 会根据玩家在 Mod 设置中的配置动态变化。这意味着：
- 如果玩家将好感度调整上限设为 0，LLM 会知道它不能调整好感度
- 如果玩家禁用了某个功能，LLM 会知道它不能使用该功能
- LLM 始终了解实时的 API 限制，确保不会超出边界

### JSON 响应格式

LLM 可以通过包含 JSON 块来触发游戏 API 调用：

```json
{
  "action": "adjust_goodwill",
  "parameters": {
    "amount": 10,
    "reason": "Successful trade negotiation"
  },
  "response": "Your trade proposal is most welcome. I believe this is the start of a fruitful partnership.",
  "strategy_suggestions": [
    {
      "short_label": "以势压人",
      "trigger_basis": "财富压制",
      "strategy_keywords": ["威慑", "实力", "底线"],
      "hidden_reply": "（供按钮一键发送的完整回复内容，玩家不可见）"
    },
    {
      "short_label": "缓和周旋",
      "trigger_basis": "社交说服",
      "strategy_keywords": ["转圜", "共赢", "拖延"],
      "hidden_reply": "（供按钮一键发送的完整回复内容，玩家不可见）"
    },
    {
      "short_label": "极端威慑",
      "trigger_basis": "激进特质",
      "strategy_keywords": ["威胁", "震慑", "恐惧"],
      "hidden_reply": "（供按钮一键发送的完整回复内容，玩家不可见）"
    }
  ]
}
```

#### 字段说明

| 字段 | 类型 | 必需 | 说明 |
|------|------|------|------|
| action | string | 是 | 要执行的动作类型 |
| parameters | object | 否 | 动作参数，根据 action 类型变化 |
| response | string | 否 | AI 的角色扮演回复文本 |
| strategy_suggestions | array | 否 | 降好感场景可选返回的策略按钮数据，必须为 3 项 |

#### `strategy_suggestions` 子字段说明

| 子字段 | 类型 | 必需 | 说明 |
|--------|------|------|------|
| short_label | string | 是 | 按钮短标题（建议 <= 8 中文字符） |
| trigger_basis | string | 是 | 触发依据短语，UI 显示在标题后 |
| strategy_keywords | array[string] | 否 | 策略关键词，用于 tooltip |
| hidden_reply | string | 是 | 完整回复草稿（按钮点击即发送，玩家界面不直接显示） |

**输出约束：**
- 策略能力可用时优先输出 `strategy_suggestions`（会话内由社交等级和剩余次数决定可用性）。
- 若输出该字段，必须严格返回 3 项；否则客户端会丢弃整个字段。
- 客户端在“净降好感且字段缺失/异常”时会发起一次仅请求 `strategy_suggestions` 的补充请求，不影响本轮普通对话文本与动作执行。
- 若补充请求返回自然语言而非 JSON，客户端会尝试从叙述文本中提取 3 条策略句并回填按钮（兜底逻辑）。
- 至少 2 条建议应明确基于玩家属性/上下文（社交、特质、殖民地财富、近期交互语气）。

#### 有效动作类型

| 动作 | 说明 | 必需参数 | 可选参数 |
|------|------|----------|----------|
| adjust_goodwill | 调整好感度 | amount (int) | reason (string) |
| send_gift | 发送礼物 | - | silver (int), goodwill_gain (int) |
| request_aid | 请求援助 | - | type (string) |
| declare_war | 宣战 | - | reason (string) |
| make_peace | 议和 | - | cost (int) |
| request_caravan | 请求商队 | - | goods (string) |
| request_raid | 攻击玩家殖民地（袭击） | strategy (string) | arrival (string) |
| create_quest | 创建原生模板任务 | questDefName (string) | points (int), askerFaction (string) |
| reject_request | 正式拒绝明确请求 | - | reason (string) |
| none | 无动作 | - | - |

### 决策指南

#### 基于好感度的行为建议

| 好感度范围 | 关系 | 行为建议 |
|------------|------|----------|
| 80-100 | 盟友 | 非常友好， likely to accept most requests |
| 40-79 | 朋友 | 友好， open to trade and cooperation |
| 0-39 | 中立 | 谨慎， but willing to negotiate |
| -39-(-1) | 敌对 | 不太可能合作， may threaten |
| -100-(-40) | 敌人 | 敌对， may declare war |

#### 接受/拒绝逻辑

LLM 应该基于以下因素决定接受或拒绝玩家请求：

1. **当前好感度**：高好感度更容易接受请求
2. **领袖特质**：影响决策风格（如"聪明"的领袖更谨慎）
3. **意识形态**：影响外交倾向
4. **关系状态**：
   - 盟友可以请求援助
   - 敌对不能请求商队
   - 战争状态才能议和
   - 好感度低于-50才能宣战

### 使用示例

#### 示例 1：玩家请求商队

**玩家**："Can you send a trade caravan to our colony?"

**LLM 响应**（友好派系）：
```json
{
  "action": "request_caravan",
  "parameters": {
    "goods": "general"
  },
  "response": "Of course! Our traders would be delighted to visit your colony. Expect them within a few days."
}
```

**LLM 响应**（敌对派系）：
I cannot agree to this. Your colony has caused us much trouble. Improve our relations first, then we may talk of trade.
```json
{"actions":[{"action":"reject_request","parameters":{"reason":"Our relations are too strained for trade at this time."}}]}
```

#### 示例 2：玩家请求援助

**玩家**："We are under attack! Can you send military aid?"

**LLM 响应**（盟友，高好感度）：
As allies, we shall not abandon you in your time of need. Reinforcements are being prepared and will move as soon as they can.
```json
{"actions":[{"action":"request_aid","parameters":{"type":"Military"}}]}
```

**LLM 响应**（中立派系）：
I sympathize with your plight, but we are not yet bound by alliance. Strengthen our ties, and perhaps we can discuss mutual defense.
```json
{"actions":[{"action":"reject_request","parameters":{"reason":"We are not yet close enough allies for such assistance."}}]}
```

#### 示例 3：调整好感度

**玩家**："Thank you for your generous gift. We appreciate our friendship."

**LLM 响应**：
Your words warm my heart. It pleases me to see our friendship grows stronger with each passing day.
```json
{"actions":[{"action":"adjust_goodwill","parameters":{"amount":8,"reason":"Player expressed gratitude for gift"}}]}
```

#### 示例 4：纯对话（无动作）

**玩家**："Tell me about your faction's history."

**LLM 响应**（纯文本，无 JSON）：
"Our people have wandered these lands for generations, forging alliances and overcoming challenges. We value strength, wisdom, and above all, loyalty to our friends."

### 错误处理

如果 LLM 返回的 JSON 格式无效或动作执行失败：

1. **解析错误**：系统会将整个响应作为纯文本显示
2. **动作执行失败**：系统会记录错误日志，并在对话中显示失败原因
3. **冷却中**：如果动作处于冷却期，会提示剩余时间
4. **权限不足**：如果 AI 没有权限操作该派系，会拒绝执行

### 最佳实践

#### 对 LLM 开发者的建议

1. **渐进式好感度调整**：
   - 小互动：±5
   - 中等互动：±10
   - 重大事件：±15

2. **合理的接受/拒绝**：
   - 不要无条件接受所有请求
   - 考虑当前关系和派系特性
   - 给出合理的拒绝理由

3. **角色扮演**：
   - 保持角色一致性
   - 考虑领袖特质（如"暴躁"的领袖更容易宣战）
   - 反映意识形态差异

4. **JSON 格式**：
   - 确保 JSON 格式正确
   - 参数类型匹配（整数 vs 字符串）
   - 如果不确定，优先使用纯文本响应

---

## 更新日志

### v0.3.6
- Added `exit_dialogue`, `go_offline`, `set_dnd` AI actions for faction presence control.
- Added faction presence state system (`Online/Offline/DoNotDisturb`) with per-faction cache and forced-offline support.
- Added dialogue input gate: offline/DND are read-only and cannot send messages.
- Added reinitiate flow after `exit_dialogue`.

### v1.1.0
- 添加 LLM JSON 响应支持
- 实现 AI 动作解析器
- 添加 AI 动作执行器
- 扩展系统提示词，包含 API 调用说明
- 实现接受/拒绝逻辑

### v1.0.0
- 初始版本发布
- 实现核心 API 方法
- 添加安全限制和冷却机制
- 支持 Mod 设置配置

---

## 在线状态动作（v0.3.6）

### Action: exit_dialogue
结束当前对话轮次，不改变在线状态。

**JSON 示例:**
```json
{
  "action": "exit_dialogue",
  "parameters": {
    "reason": "I need to review your proposal first."
  }
}
```

**效果:**
- 当前窗口进入只读状态。
- 玩家可点击“重新发起对话”继续同派系会话（若该派系当前为在线）。

### Action: go_offline
结束当前对话并切换到离线状态。

**JSON 示例:**
```json
{
  "action": "go_offline",
  "parameters": {
    "reason": "Communications terminal shutting down."
  }
}
```

**效果:**
- 当前窗口只读。
- 在线状态变为 `Offline`。
- 在“强制离线时长”内不可发送消息。

### Action: set_dnd
切换到请勿打扰状态并停止消息交互。

**JSON 示例:**
```json
{
  "action": "set_dnd",
  "parameters": {
    "reason": "We are in emergency council."
  }
}
```

**效果:**
- 当前窗口只读。
- 在线状态变为 `DoNotDisturb`。
- 不允许发送消息。

### 相关设置项
- `EnableFactionPresenceStatus`
- `PresenceCacheHours`（默认 8 小时）
- `PresenceForcedOfflineHours`（默认 24 小时）
- `PresenceNightBiasEnabled`
- `PresenceNightStartHour` / `PresenceNightEndHour`
- `PresenceNightOfflineBias`
- `PresenceUseAdvancedProfiles` 与各 TechLevel 在线模板（起始小时/在线时长）

## RPG Pawn Persona Prompt 接口

### `GameComponent_RPGManager.GetPawnPersonaPrompt(Pawn pawn)`
- 作用：读取指定 Pawn 的独立人格 Prompt。
- 返回：未配置时返回空字符串。

### `GameComponent_RPGManager.SetPawnPersonaPrompt(Pawn pawn, string prompt)`
- 作用：写入或清空指定 Pawn 的独立人格 Prompt。
- 行为：`prompt` 为空或仅空白时会删除该 Pawn 的配置。

### RPG Prompt 注入行为
- 组装入口：`PromptPersistenceService.BuildRPGFullSystemPrompt(Pawn initiator, Pawn target)`。
- 注入位置：`ROLE SETTING` 之后、`DIALOGUE STYLE` 之前。
- 注入条件：目标 Pawn 存在非空独立人格 Prompt。

### 首次加载旧存档 NPC 人格画像（v0.3.109）
- 组件：`GameComponent_RPGManager`
  - 存档字段：`npcPersonaBootstrapCompleted`、`npcPersonaBootstrapVersion`（按引导版本一次性执行标记）。
  - 运行入口：`GameComponentTick()` -> 人格画像异步队列处理。
  - 目标集合：已存在的人形 Pawn（地图已生成 Pawn + 可见派系领袖）。
  - 写入接口：复用 `SetPawnPersonaPrompt(Pawn pawn, string prompt)`。
- 上下文构建：
  - `PromptPersistenceService.BuildPawnPersonaBootstrapProfile(Pawn pawn)`
  - 使用人格专用精简档案（背景/特质/核心技能/派系角色/意识形态）。
  - 显式排除健康/需求/心情/伤病/装备/基因/临时事件等非人格信号。
- 生成协议：
  - 输出模板固定：
    - `You are a person who ___. On a daily basis, you ___. When getting along with others, you ___. When facing pressure or conflict, you ___. You value ___ the most, so you will instinctively ___.`
  - 输出长度约束：每段短语 4-12 词，总体尽量保持精简（目标 <90 词）。
  - 无效输出会重试；重试失败写入模板化兜底文本，保证字段可用。

## 环境提示词系统接口（v0.3.25）

### 新增配置结构
- `SystemPromptConfig.EnvironmentPrompt`
  - `Worldview.Enabled` / `Worldview.Content`
  - `SceneSystem.Enabled` / `MaxSceneChars` / `MaxTotalChars` / `PresetTagsEnabled`
  - `SceneEntries[]`
    - `Id`, `Name`, `Enabled`, `ApplyToDiplomacy`, `ApplyToRPG`, `Priority`, `MatchTags[]`, `Content`
  - `RpgSceneParamSwitches`
    - `IncludeSkills`, `IncludeEquipment`, `IncludeGenes`, `IncludeNeeds`, `IncludeHediffs`, `IncludeRecentEvents`, `IncludeColonyInventorySummary`, `IncludeHomeAlerts`, `IncludeRecentJobState`, `IncludeAttributeLevels`
  - `EventIntelPrompt`
    - `Enabled`, `ApplyToDiplomacy`, `ApplyToRpg`
    - `IncludeMapEvents`, `IncludeRaidBattleReports`
    - `DaysWindow`, `MaxStoredRecords`, `MaxInjectedItems`, `MaxInjectedChars`

### 新增上下文类型
- `DialogueScenarioContext`
  - `CreateDiplomacy(Faction faction, bool isProactive, IEnumerable<string> additionalTags = null)`
  - `CreateRpg(Pawn initiator, Pawn target, bool isProactive, IEnumerable<string> additionalTags = null)`

### Prompt 组装入口扩展
- 外交通道：
  - `BuildFullSystemPrompt(Faction faction, SystemPromptConfig config, bool isProactive, IEnumerable<string> additionalSceneTags)`
- RPG 通道：
- `BuildRPGFullSystemPrompt(Pawn initiator, Pawn target, bool isProactive, IEnumerable<string> additionalSceneTags)`
### 场景模板变量（v0.3.34）

环境场景条目 `SceneEntries[].Content` 现支持 `{{variable}}` 语法，运行时在提示词组装阶段替换。  
当前内置变量：

- `{{scene_tags}}`
- `{{environment_params}}`
- `{{recent_world_events}}`
- `{{colony_status}}`
- `{{colony_factions}}`
- `{{current_faction_profile}}`
- `{{rpg_target_profile}}`
- `{{rpg_initiator_profile}}`

说明：
- 未识别变量将保留原文本（不会被静默删除），并在预览诊断中提示。
- 提示词设置页新增变量参考与当前分区变量校验按钮。

### 环境层注入规则
- 注入顺序：`Worldview -> Environment Parameters -> Recent World Events & Battle Intel -> Scene Layers -> Existing Prompt Stack`。
- 匹配规则：`SceneEntries.MatchTags` 全量命中（ALL）才注入。
- 命中策略：全部命中条目按 `Priority` 降序注入。
- 长度控制：先按 `MaxSceneChars` 裁剪单条，再按 `MaxTotalChars` 裁剪总量。
- 事件记忆控制：`MaxInjectedItems` 与 `MaxInjectedChars` 双限流，按派系可知边界过滤。
- 事实约束块：统一追加 `FACT GROUNDING RULES`，要求仅基于已知信息回复；无依据说法需明确不确定并质疑。










## Prompt Output Language API (v0.3.44)
- Settings: RimChatSettings.PromptLanguageFollowSystem, RimChatSettings.PromptLanguageOverride.
- Runtime resolver: RimChatSettings.GetEffectivePromptLanguage().
- Prompt injection: diplomacy/RPG hierarchical builders append an output_language guidance node.
- Contract: language guidance applies to natural-language response only; JSON keys/action IDs remain unchanged.

## RimTalk Compatibility API (v0.3.47)
- Settings:
  - `RimChatSettings.EnableRimTalkPromptCompat` (default `true`)
  - `RimChatSettings.RimTalkSummaryHistoryLimit` (default `10`, clamped to `1..30`)
  - `RimChatSettings.RimTalkCompatTemplate` (Scriban template used by both diplomacy and RPG prompts)
- Runtime bridge:
  - `RimChat.Compat.RimTalkCompatBridge.IsRuntimeAvailable()`
  - `RimChat.Compat.RimTalkCompatBridge.GetRegisteredVariablesSnapshot()`
  - `RimChat.Compat.RimTalkCompatBridge.TryAddOrUpdateUserPromptEntry(string entryName, string content, string roleName, string positionName, int inChatDepth, string afterEntryName)`
  - `RimChat.Compat.RimTalkCompatBridge.RenderCompatTemplate(string templateText, Pawn initiator, Pawn target, Faction faction, string channel)`
  - `RimChat.Compat.RimTalkCompatBridge.RenderActivePresetModEntries(Pawn initiator, Pawn target, Faction faction, string channel)`
  - `RimChat.Compat.RimTalkCompatBridge.PushSessionSummary(string summaryText, RimTalkSummaryChannel channel)`
- Bridge models:
  - `RimChat.Compat.RimTalkPromptEntryWriteResult`
  - `RimChat.Compat.RimTalkRegisteredVariable`
- Summary push keys (RimTalk global variable store):
  - `rimchat_last_session_summary`
  - `rimchat_last_diplomacy_summary`
  - `rimchat_last_rpg_summary`
  - `rimchat_recent_session_summaries`
- Prompt pipeline integration:
  - Diplomacy: compatibility block appended at `instruction_stack` tail.
  - RPG: compatibility block appended at `role_stack` tail, plus active RimTalk preset mod-entry render block (`rimtalk_preset_mod_entries`).
  - Render failure fallback: raw template text is appended; request flow continues.
- Session-end integration:
  - Diplomacy close summary: pushed after summary record creation.
  - RPG close summary (manual close included): built from existing chat history rules (no extra AI call), then pushed.
  - `GameComponent_RPGManager` startup/load/finalize path performs bridge warmup for delayed runtime registration.
  - RimTalk absent or reflection bind failure: silent downgrade, debug-log only.
