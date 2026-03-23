# RimChat 模块索引（v0.7.85）

## 主动外交 Warning 触发移除（v0.7.85）
- 目标：仅在 `NpcDialogue` 主动外交链路中，彻底移除 `WarningThreat` 类型触发，不影响 `PawnRpgPush`。
- 关键文件：
  - `RimChat/Patches/FactionGoodwillPatch_NpcDialogue.cs`
  - `RimChat/Patches/TradeDealPatch_NpcDialogue.cs`
  - `RimChat/NpcDialogue/GameComponent_NpcDialoguePushManager.cs`
- 链路变化：
  - `FactionGoodwillPatch_NpcDialogue` 仅在 `goodwillChange > 0` 时向 `NpcDialogue` 注入主动触发，负向好感不再进入主动外交 Warning 链路。
  - `TradeDealPatch_NpcDialogue` 不再调用 `RegisterLowQualityTradeTrigger(...)`，低质量交易不再触发主动外交 Warning。
  - `HandleTriggerContext(...)` 新增 fail-fast：`WarningThreat` 触发上下文立即丢弃。
  - `BuildRegularTrigger(...)` 在 `goodwill <= -40` 时直接返回 `null`，不再生成 Warning 类定期触发。
  - `DebugForceRandomProactiveDialogue(...)` 随机类别收敛为 `Social` / `DiplomacyTask`，排除 `WarningThreat`。
  - `CleanupInvalidState(...)` 读档后会清理历史 `queuedTriggers` 中的 `WarningThreat` 条目，避免旧存档残留触发。
- 兼容约束：
  - 保留 `NpcDialogueCategory.WarningThreat` 枚举定义，避免旧存档反序列化兼容性风险。

## 外交过期回包与跨派系假加载根修（v0.7.84）
- 目标：根除“已丢弃过期对话回包”在外交关闭/切换后的可见提示，并修复一个派系卡住时其他派系窗口长期假加载的问题。
- 关键文件：
  - `RimChat/AI/AIChatServiceAsync.cs`
  - `RimChat/AI/AIChatServiceAsync.RequestScheduling.cs`
  - `RimChat/AI/AIChatServiceAsync.LocalControl.cs`
  - `RimChat/DiplomacySystem/DiplomacyConversationController.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.RequestFeedback.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.InputLifecycle.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.TypingStatus.cs`
  - `RimChat/UI/Dialog_RPGPawnDialogue.Lifecycle.cs`
  - `RimChat/Dialogue/DialogueDropPolicy.cs`
- 链路变化：
  - `AIRequestState` 新增 `Queued` / `Cancelled`，`AIRequestResult` 新增优先级、队列位置、排队截止、取消原因、失败原因、回调允许标记等运行态元数据。
  - 本地单飞队列保持并发上限 `1`，但改为“前台交互优先、同优先级 FIFO、超过 `60s` 排队失败”的可观测队列。
  - `CancelRequest(...)`、窗口关闭、派系切换、同会话新请求顶替旧请求时，统一走“取消并禁止回调”语义；飞行中的 `UnityWebRequest` 会立刻 `Abort()`。
  - 主线程回调门禁新增 `AllowCallbacks` 校验，已取消/已失效请求的 `onSuccess/onError/onProgress` 不再落到外交或 RPG UI。
  - 外交窗口的掉包提示改为内部日志；玩家只会看到真实失败（如 queue timeout / timeout / service error）的可见错误提示。
  - 外交输入状态新增“排队中”文案，区分 queued 与 processing，不再把排队态伪装成正常“对方正在输入”。
- 行为约束：
  - 保留本地模型单飞串行，不做真并发。
  - 手动外交 / RPG / 策略补请求优先于后台人格生成、社交圈新闻、摘要压缩等后台任务。
  - 旧回包在窗口关闭、切换派系、请求被顶替后即使晚到，也不会污染聊天记录。

## 外交输入宿主生命周期根修（v0.7.83）
- 目标：根修外交窗口在 AI 主回复结束后、策略三选项补请求尚未完成时继续输入触发的 Unity / Windows IME 闪退。
- 关键文件：
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.InputLifecycle.cs`
- 链路变化：
  - `ShouldRenderInputAsReadOnly(...)`：统一将 `IsHardBlocked` 与 `IsSoftBlocked` 纳入只读输入门控，AI 软阻断期间不再创建可编辑 `Widgets.TextArea(...)`。
  - `IsAiTurnInputHostOwned(...)`：将主回复请求、图片请求、NPC 逐字机、策略三选项补请求统一视为 AI 持有输入宿主的阶段。
  - `InputHostReactivationStabilizationSeconds`：AI 链路结束后增加短暂重激活稳定期，仅在稳定期结束后允许 IME 重新绑定输入宿主。
- 根因分析：
  - 既有外交输入锁只对 `IsHardBlocked` 生效；
  - 策略三选项补请求属于 `IsSoftBlocked`，输入框会在补请求完成前提前恢复为可编辑状态；
  - Windows `textinputframework` / `MSCTF` 在 Unity 文本宿主重建阶段重新接管焦点，导致原生层崩溃。
- 行为约束：
  - 三选项策略补请求完成前，外交输入框保持只读；
  - 三选项就绪后仍需经过短稳定期，才能重新输入；
  - 不改动策略建议生成逻辑，不阉割功能。

## 压缩通道 runtime 占位符根修（v0.7.82）
- 目标：修复 `rpg_archive_compression` / `summary_generation` 通道在运行期出现 `Scriban Render Error: Object runtime is null` 的高频日志错误。
- 关键文件：
  - `RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs`
- 链路变化：
  - `ResolveWorkspaceContextEnvironmentText(...)`：当通道为 `RpgArchiveCompression` 或 `SummaryGeneration` 时，不再返回 `{{ runtime.environment }}` 占位符，而是返回固定文本 `No environment context.`。
- 原因分析：
  - 这两个通道属于压缩/摘要链路，不构建完整 runtime 环境对象；
  - 占位符进入 RimTalk 原生 Scriban 渲染后会访问 `runtime.environment`，触发 null 成员访问。
- 行为约束：
  - 仅调整压缩/摘要通道的环境块输出；
  - RPG 对话主通道（`rpg_dialogue` / `proactive_rpg_dialogue` / `persona_bootstrap`）仍按原逻辑构建环境文本，保持既有行为不变。

## 外交通道原生变量受控直通（v0.7.78）
- 目标：解决外交通道（diplomacy_dialogue / proactive_diplomacy_dialogue）non-mod_variables 区域中原生变量（如 `{{ pawn.ABM }}`、`{{ knowledge_* }}`、`{{ rimchat_summary }}`）不渲染的问题。
- 关键文件：
  - `RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs`
- 链路变化：
  - `IsDiplomacyNativeVariablePassthroughSection(...)`：判定 diplomacy 对话通道中需要直通处理的 section（非 `mod_variables`）。
  - `ShouldPassthroughRimTalkNativeToken(...)`：识别 RimTalk 原生变量 token（检测 `.rimtalk.` 命名空间路径或 legacy 映射）。
  - `ExtractSectionIdFromTemplateId(...)`：从 templateId 提取 section 标识。
  - `PreprocessDiplomacyNativeVariables(...)`：对外交通道目标 section 中的原生变量做预处理，在 Scriban 统一渲染前解析为原始文本。
  - `RenderUnifiedTemplate(...)`：在 diplomacy 对话通道中，先走原生变量预处理，再走统一 Scriban 渲染。
- 行为约束：
  - `mod_variables` section 行为不变，仍走 `RenderRawModVariablesSection` 全量 Raw 处理。
  - 预览时 RimTalk 桥未完整初始化，原生变量 token 保留原样（WYSIWYG）。
  - 普通模板变量仍走统一 Scriban 渲染，Fail Fast 行为不变。
  - 不扩展到 diplomacy_strategy / social_circle / summary / image 等非对话通道。

## Pawn 右键对话入口排序后移（v0.7.77）
- 目标：将 Pawn 右键菜单中的 RimChat 对话入口从默认优先级后移，降低其长期位于第一项的概率。
- 关键文件：
  - `RimChat/Comp/CompPawnDialogue.cs`
- 链路变化：
  - `CompPawnDialogue.CompFloatMenuOptions(...)` 中 `RimChat_RPGDialogue_Dialogue` 入口的 `MenuOptionPriority` 从 `Default` 调整为 `Low`。
- 保持不变：
  - 对话显示门控、冷却校验、`JobDriver_RPGPawnDialogue` 派发、缺失 `JobDef` 的开窗回退链路均保持原样。

## Prompt Workspace 预览增量构建（v0.7.76）
- 目标：消除 `ComposePromptWorkspace` 在工作台首开预览上的同步全量渲染阻塞。
- 关键文件：
  - `RimChat/Persistence/PromptWorkspacePreviewModels.cs`
  - `RimChat/Persistence/PromptPersistenceService.WorkspacePreviewIncremental.cs`
  - `RimChat/Config/RimChatSettings_PromptSectionWorkspace.cs`
  - `RimChat/UI/PromptWorkspaceStructuredPreviewRenderer.cs`
  - `1.6/Languages/English/Keyed/RimChat_Keys.xml`
  - `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
- 链路变化：
  - 工作台预览从“同步一次性构建”改为“自动增量分阶段构建”。
  - 每帧预算固定 2ms，阶段为 `Init -> Sections -> Nodes -> Finalize`。
  - 失败策略为 fail-fast：停止后续阶段，但保留已完成块并展示错误诊断。
- 兼容边界：
  - 运行时 prompt 主链路（`BuildUnifiedChannelSystemPrompt -> ComposePromptWorkspace`）不变。

## 通讯台派系识别根修（v0.7.72）
- 关键修复：
  - `RimChat/Patches/CommsConsolePatch.cs`
  - `GetFloatMenuOptionsPostfix(...)` 不再依赖标签关键词（call/contact/呼叫/联系）判断。
  - `ExtractFactionFromOption(...)` 识别链改为：
    1) action 闭包反射提取 `Faction`
    2) `console.GetCommTargets(myPawn)` 标签匹配
    3) 全派系标签匹配回退
  - 移除 `Find.Selector.SingleSelectedThing` 依赖，避免通讯台场景下选中对象不是 pawn 时识别失败。
- 新增诊断日志：
  - `Comms option intercepted`
  - `Comms menu patch found no faction options`

## 外交开窗拒绝可观测化与入口阻断（v0.7.71）
- 入口拒绝原因日志统一接入：
  - `RimChat/Patches/FactionDialogRimChatBridgePatch.cs`
  - `RimChat/Patches/CommsConsolePatch.cs`（`CommsConsoleCallback`）
  - `RimChat/UI/Dialog_SelectFactionForDialogue.cs`
  - `RimChat/UI/MainTabWindow_RimChat.cs`
  - `RimChat/NpcDialogue/ChoiceLetter_NpcInitiatedDialogue.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`（派系列表切换）
- 当 `DialogueWindowCoordinator.TryOpen(...)` 返回拒绝时，入口层统一追加“直接开窗”短路阻断，优先恢复外交窗口可达性。
- 统一日志标识：`Applying direct diplomacy open fallback`，便于在 `Player.log` 直接筛选链路。

## 对话生命周期统一收口（v0.7.70）
- 新增生命周期核心：
  - `RimChat/Dialogue/DialogueRuntimeContext.cs`
  - `RimChat/Dialogue/DialogueContextResolver.cs`
  - `RimChat/Dialogue/DialogueContextValidator.cs`
  - `RimChat/Dialogue/DialogueRequestLease.cs`
  - `RimChat/Dialogue/DialogueResponseEnvelope.cs`
  - `RimChat/Dialogue/DialogueOpenIntent.cs`
  - `RimChat/Dialogue/DialogueWindowCoordinator.cs`
- 新增 RPG 请求控制器：
  - `RimChat/Rpg/RpgDialogueConversationController.cs`
- 外交与 RPG 开窗入口统一接管（拒绝重复窗口）：
  - `CommsConsolePatch`、`CompPawnDialogue`、`JobDriver_RPGPawnDialogue`、`ChoiceLetter_*`、`MainTabWindow_RimChat`、`Dialog_SelectFactionForDialogue`、`FactionDialogRimChatBridgePatch`、`Dialog_DiplomacyDialogue`（派系列表切换）。
- 请求生命周期改造：
  - `DiplomacyConversationController` 升级为 lease 驱动；
  - `Dialog_RPGPawnDialogue` 改为控制器发送 + 回包 Envelope 两阶段落地；
  - 回包失效统一写系统提示并中止动作。
- RPG 持久层 ID 化迁移：
  - `GameComponent_RPGManager` 从 `Dictionary<Pawn,...>` 升级为 `Dictionary<string,...>`（`GetUniqueLoadID()`），仅在读档 `PostLoadInit` 一次性迁移旧字段。
- 多语言键新增：
  - `RimChat_DialogueResponseDropped`（中英）。

## 外交 NPC 主动重开会话止血补丁（v0.7.68）
- 主动投递入口前置复位：
  - `RimChat/NpcDialogue/GameComponent_NpcDialoguePushManager.cs`
  - `AddMessageToSession(...)` 新增会话结束态判定：当 `isConversationEndedByNpc=true` 时，按顺序执行 `ReinitiateConversation()` -> 强制在线恢复 -> 追加系统提示 -> 追加 NPC 主动消息。
- Presence 状态强制在线封装：
  - `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.cs`
  - 新增 `ForcePresenceOnlineForNpcInitiated(Faction faction)`，统一封装主动投递场景下的在线恢复与缓存清理（`forcedOfflineUntilTick/cacheUntilTick/lastReason`）。
- 语言键补充：
  - `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - `1.6/Languages/English/Keyed/RimChat_Keys.xml`
  - 新增 `RimChat_ConversationReinitiatedByNpc`，用于区分“玩家手动重开”与“NPC 主动重开”。

## RPG 动作合同注入与自动记忆门控修复（v0.7.67）
- RPG `response_contract` 注入恢复：
  - `RimChat/Config/PromptUnifiedNodeSchemaCatalog.cs`
  - `rpg_dialogue` 与 `proactive_rpg_dialogue` 允许节点新增 `response_contract_node_template`。
  - `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - `ResolveRpgNodePlacements(...)` 新增 `response_contract_node_template` 渲染分支，`dialogue.response_contract_body` 由 `BuildRpgApiContractText(...)` 注入。
  - `RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs`
  - `InjectRuntimeNodeBodies(...)` 补齐 RPG 通道 `response_contract_node_template` 正文注入，避免运行时只剩“引用说明”。
- 运行时 fail-fast 与布局补全：
  - `RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs`
  - RPG 运行时必需节点新增 `response_contract_node_template` 校验。
  - `BuildPromptNodePlacementsForCompose(...)` 增加 allowed node 自动补全，旧自定义布局缺项时自动回填默认布局节点。
- 自动记忆连发治理：
  - `RimChat/UI/Dialog_RPGPawnDialogue.ActionPolicies.cs`
  - 新增会话级自动记忆单次门控（仅自动来源计数）；显式 `TryGainMemory` 不受限制。
  - 缺失动作合同时仅保留退出类兜底，自动记忆映射与兜底整体关闭。
  - 协作意图关键词收紧，移除高歧义短词，改为明确承诺短语触发。
- 请求期短期阻断：
  - `RimChat/UI/Dialog_RPGPawnDialogue.RequestContext.cs`
  - `BuildRpgSystemPromptForRequest(...)` 新增动作合同存在性检测；缺失时记录告警并关闭本轮自动记忆兜底。

## 构建告警清零与依赖安全修复（v0.7.64）
- 通知翻译 API 过时警告修复：
  - `RimChat/DiplomacySystem/DiplomacyNotificationManager.cs`
  - `SendNotification(...)` 改为“翻译模板 + `string.Format`”路径，移除过时 `Translate(params object[])` 调用，构建不再触发 `CS0618`。
- Scriban 依赖漏洞修复：
  - `RimChat/RimChat.csproj`
  - `Scriban` 从 `5.10.0` 升级到 `6.6.0`，清除 `NU1902/NU1903` 告警。
- 验证链路：
  - `build.ps1` 构建与部署通过（`0 warnings / 0 errors`）；`dotnet list package --vulnerable --include-transitive` 返回无漏洞包。

## NPC 记忆按存档强隔离修复（v0.7.61）
- 存档标识 fail-fast 阻断：
  - `RimChat/Memory/RpgNpcDialogueArchiveManager.cs`
  - `OnBeforeGameSave/RecordTurn/FinalizeSession/RecordDiplomacySummary` 在写盘前强校验当前存档标识；无法解析时直接阻断写入并输出错误日志，不再落入共享 `Default` 桶。
- 存档名解析链路加固：
  - `RimChat/Memory/RpgNpcDialogueArchiveManager.cs`
  - `ResolveCurrentSaveKey/GetCurrentSaveName` 增加反射兜底链：`name/Name/fileName/FileName` -> 任意字符串成员启发式 -> `ScribeMetaHeaderUtility.loadedGameName`。
- 旧数据自动迁移（含备份）：
  - `RimChat/Memory/RpgNpcDialogueArchiveManager.cs`
  - 首次加载目标存档时自动扫描 legacy `Prompt/NPC/Save_*_Default/rpg_npc_dialogues` 与根目录旧结构，先备份到 `Prompt/NPC/_migration_backup/...`，再迁移到当前存档目录，并写一次性迁移标记。
- 档案写盘所有权字段：
  - `RimChat/Memory/RpgNpcDialogueArchive.cs`
  - `RimChat/Memory/RpgNpcDialogueArchiveJsonCodec.cs`
  - 新增 `saveKey` 字段，写盘时写入当前存档键；读取时仅接纳当前存档 `saveKey` 或 legacy 无键档案，阻断跨存档串读。

## 解析失败根因修复（v0.7.60）
- 请求解析重试收口：
  - `RimChat/AI/AIChatServiceAsync.cs`
  - `ProcessRequestCoroutine(...)` 在 `HTTP 200` 且无可解析可见文本时，新增一次 parse 重试（附带 `PARSE_RETRY_REASON` 标签），避免直接终止请求链路。
- 二次失败后的统一回退：
  - `RimChat/AI/AIChatServiceAsync.cs`
  - 外交/RPG 通道在 parse 重试后仍无可解析文本时，统一返回本地沉浸台词，阻断主动推送重复报错掉落。
- 主动推送通道显式绑定：
  - `RimChat/NpcDialogue/GameComponent_NpcDialoguePushManager.cs`
  - `RimChat/PawnRpgPush/GameComponent_PawnRpgDialoguePushManager.Generation.cs`
  - 主动外交与主动RPG请求统一传入 `usageChannel`（`Diplomacy/Rpg`），不再走 `Unknown` 分支。
- 多模型响应兼容扩展：
  - `RimChat/AI/AIJsonContentExtractor.cs`
  - `TryExtractPrimaryText(...)` 扩展候选键：`generated_text`、`answer`、`reasoning_content`，提升对不同服务端响应字段的提取成功率。

## 对话体验链路修复（v0.7.59）
- 提示词与风格收敛：
  - `RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs`
  - 运行时主提示词新增风格优先级注入（按 `DialogueStyleMode`），并对 `response_contract/output_specification` 权威重复行做去重，降低机械冗长。
- 重试链路降噪：
  - `RimChat/AI/AIChatServiceAsync.cs`
  - 重试合同文本改为精简指令，保持 `actions` 契约不变但减少规则口水文。
- 主动推送截断修复：
  - `RimChat/NpcDialogue/GameComponent_NpcDialoguePushManager.cs`
  - `RimChat/PawnRpgPush/GameComponent_PawnRpgDialoguePushManager.Generation.cs`
  - 移除固定 260 字硬截断，改为 `ProactiveMessageHardLimit` 配置化（默认 0 不截断）。
- 动作日志分级与误报抑制：
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.Strategy.cs`
  - 预期拒绝从 Warning 下调为可配置等级（默认 Info）；策略跟进失败回落路径改 Message，降低“任务报错”噪声。
- 聊天布局错位修复：
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.Speakers.cs`
  - 气泡宽度估算改为与换行高度一致的口径；输入区底部状态文案宽度改自适应；新增一次性 UI 轨道断言日志用于回归定位。

## API 可用性链路误判修复与速度评级（v0.7.58）
- UI 入口一致性：
  - `RimChat/Config/RimChatSettings.cs`
  - `RimChat/Config/RimChatSettings_ApiUsability.cs`
  - `测试连通性` 与 `测试可用性` 改为同一行 50/50 等宽按钮；任一测试执行中双按钮统一禁用。
- 本地可用性链路收敛：
  - `RimChat/Config/ApiUsabilityDiagnosticService.cs`
  - 本地链路改为 4 步：配置校验 -> 本地服务探测 -> 最小 chat 实测 -> 响应契约校验。
  - 移除本地 `模型可用性校验` 阻断，避免“模型列表未命中但实际可调用”导致误判失败。
- 结果文案增强：
  - `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - `1.6/Languages/English/Keyed/RimChat_Keys.xml`
  - 可用性成功摘要新增速度评级（极快/快/正常/慢/极慢）；当评级为“极慢”时追加连接质量差提示并建议更换服务商。

## API 双测试按钮与深度可用性诊断（v0.7.57）
- UI 入口分层（快速连通性 / 深度可用性）：
  - `RimChat/Config/RimChatSettings.cs`
  - `RimChat/Config/RimChatSettings_ApiUsability.cs`
  - `DrawConnectionTestButton(...)` 改为薄入口，分别接入 `测试连通性` 与 `测试可用性` 两条链路；深度测试执行中双按钮统一禁用。
- 深度诊断引擎：
  - `RimChat/Config/ApiUsabilityDiagnosticService.cs`
  - 云端链路：配置校验 -> 运行时端点解析 -> `/models` 探测 -> 模型存在性 -> 最小 chat 实测 -> 响应契约校验。
  - 本地链路：配置校验 -> 本地服务探测（Ollama/OpenAI 兼容）-> 模型存在性 -> 最小 chat 实测 -> 响应契约校验。
- 结构化诊断模型：
  - `ApiUsabilityDiagnosticResult` / `ApiUsabilityStepResult` / `ApiUsabilityErrorCode` / `ApiUsabilityStep`
  - 输出字段覆盖 `Step/ErrorCode/Hint/TechDetail/HTTP/Endpoint/Elapsed`，设置页按 fail-fast 单点失败即时回传。
- 观测联动：
  - `RimChat/AI/AIRequestDebugModels.cs`
  - `RimChat/UI/Dialog_ApiDebugObservability.cs`
  - 新增 source：`ApiUsabilityTest`，深度测试结果会写入现有日志观测窗口（含 request/response 摘要）。

## 外交主动对话重发链路修复（v0.7.56）
- 重发覆盖旧请求（同派系会话单活跃请求）：
  - `RimChat/DiplomacySystem/DiplomacyConversationController.cs`
  - `TrySendDialogueRequest(...)` 在入队新请求前，先取消并清理 `pendingRequestId` 对应旧请求，避免“旧请求残留锁死”阻断玩家继续发起。
- 同目标防抖（2 秒，外交会话级）：
  - `RimChat/Memory/FactionDialogueSession.cs`
  - `RimChat/DiplomacySystem/DiplomacyConversationController.cs`
  - 新增运行态字段 `lastDiplomacyRequestQueuedTick`；`CanStartRequest(...)` 与 `IsRequestDebounced(...)` 统一按 tick 做短防抖门控。
- 输入门控收口（允许等待回复期间再次发起）：
  - `RimChat/UI/Dialog_DiplomacyDialogue.Presence.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - `CanSendMessageNow()` 不再被 `isWaitingForResponse` 硬阻断；`IsInputLockedByAiTurn(...)` 取消 AI 回合输入硬锁，仅保留状态展示。
- 失败释放与兜底收敛：
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - 发送未入队时，若命中防抖或仍处等待态，不再误走本地 fallback 回复，避免产生“未真正请求却出现兜底 AI 回复”的链路偏差。

## 提示词工作台单真源收敛（v0.7.55）
- Unified-only 主链：
  - `RimChat/Config/RimChatSettings_RimTalkCompat.cs`
  - `SetPromptSectionCatalog(...)` 已降级为迁移专用 fail-fast；正式编辑链路新增 `SetPromptSectionText(...)` / `SetPromptNodeText(..., persistToFiles)` / `PersistUnifiedPromptCatalogToCustom()`。
- 工作台保存语义：
  - `RimChat/Config/RimChatSettings_PromptSectionWorkspace.cs`
  - `RimChat/Config/RimChatSettings_PromptWorkspaceEditorActions.cs`
  - 编辑过程只改内存 unified，`Save` 才执行统一落盘；切换分段/节点/通道/预设不再自动写盘。
- 预设单向化：
  - `RimChat/Config/PromptPresets/PromptPresetModels.cs`
  - `RimChat/Config/PromptPresets/PromptPresetService.cs`
  - payload 正式字段移除 `PromptSectionCatalog`，激活只应用 `UnifiedPromptCatalog`；legacy section 仅导入迁移。
- RPG custom store 去 section 化：
  - `RimChat/Config/RpgPromptCustomStore.cs`
  - `RimChat/Config/RimChatSettings.cs`
  - `PawnDialoguePrompt_Custom.json` 不再保存 section catalog；旧字段仅通过 `LoadLegacyPromptSectionCatalogSnapshot()` 做一次性导入。
- 读写职责拆分：
  - `RimChat/Persistence/IPromptPersistenceService.cs`
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs`
  - 新增 `LoadConfigReadOnly()` / `RepairAndRewritePromptDomains()`；工作台预览链路改走只读加载，避免读取副作用。

## 提示词工作台预设交互增强（v0.7.54）
- 保存失败阻断切换（fail-fast）：
  - `RimChat/Config/RimChatSettings_PromptSectionWorkspace.cs`
  - `RimChat/Config/RimChatSettings_PromptSectionWorkspace.NodeLayout.cs`
  - `RimChat/Config/RimChatSettings_PromptWorkspacePresetInteractions.cs`
  - 分段/通道/节点/预设切换前统一检查 `PersistPromptWorkspaceBufferNow(force: true)`，保存未成功时中止切换，避免回滚到旧文本。
- 主工作台预设交互（仅 `DrawPromptSectionWorkspace` 路径）：
  - `RimChat/Config/RimChatSettings_PromptWorkspacePresetInteractions.cs`
  - 预设列表行内快捷操作（复制/删除）、双击行内重命名（Enter/失焦保存、Esc 取消）、默认预设删除禁用。
- 默认预设只读 + 自动分叉：
  - `RimChat/Config/PromptPresets/PromptPresetModels.cs`
  - `RimChat/Config/PromptPresets/IPromptPresetService.cs`
  - `RimChat/Config/PromptPresets/PromptPresetService.cs`
  - `RimChat/Config/PromptPresets/PromptPresetService.DefaultPreset.cs`
  - 新增 `DefaultPresetId` 与 schema `2`；工作台写入意图统一经“可编辑预设保障”入口，默认预设首次编辑自动分叉为 `Custom yyyyMMdd-HHmmss`。
- 主编辑器动作栏替换：
  - `RimChat/Config/RimChatSettings_PromptWorkspaceEditorActions.cs`
  - 工具栏改为 `Undo / Redo / Save / Reset`；Undo/Redo 按 `preset+channel+section|node` 独立历史栈隔离。
- 分拆与文件阈值治理（< 800 行）：
  - `RimChat/Config/RimChatSettings_PromptSectionWorkspace.cs`
  - `RimChat/Config/RimChatSettings_PromptSectionWorkspace.NodeLayout.cs`
  - 节点编排列表与 reset 链路拆分，主文件压缩到阈值内。

## 外交主动推送全局共享冷却池（v0.7.47）
- 触发编排与全局冷却落点：
  - `RimChat/NpcDialogue/GameComponent_NpcDialoguePushManager.cs`
  - 新增全局门控常量 `GlobalDeliveryCooldownTicks = TickPerHour` 与持久化锚点 `lastGlobalDeliveredTick`（Scribe key: `npcPushLastGlobalDeliveredTick`）。
- 触发链路统一：
  - `HandleTriggerContext(...)` 在原有派系冷却/重启冷却/忙碌门控后，新增全局冷却门控。
  - `ProcessQueuedTriggers(...)` 在出队执行前再次校验全局冷却，命中则仅延后 `dueTick`，不丢事件。
  - `DebugForceRandomProactiveDialogue()` 改为走 `HandleTriggerContext(...)`，与正式链路一致。
- 生效语义：
  - 外交主动推送全阵营共享：每 1 游戏小时最多投递 1 条。
  - 威胁高优先级不再绕过该全局池，统一限流。

## RPG PromptContext Pawn 根绑定修复（v0.7.53）
- 通道显式传递到原生渲染器：
  - `RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs`
  - `BuildUnifiedChannelSystemPrompt(...)` 调用 `TryRenderRpgPrompt(...)` 时显式传入 `promptChannel`。
- 原生 Pawn 绑定收口：
  - `RimChat/Prompting/RimTalkNativeRpgPromptRenderer.cs`
  - `TryRenderRpgPrompt(...)` 通过统一绑定器构建 `CurrentPawn / Pawns / AllPawns / ScopedPawnIndex`，并记录绑定快照诊断字段。
- Archive 压缩场景真实 pawn 输入：
  - `RimChat/Memory/RpgNpcDialogueArchiveManager.Sessions.cs`
  - `BuildSessionSummaryRequestMessages(...)` 改为 `CreateRpg(interlocutorPawn, npcPawn, ...)`，不再使用 `CreateRpg(null, null, ...)`。
  - interlocutor 解析顺序：`session.InterlocutorPawnLoadId` -> `archive.LastInterlocutorPawnLoadId`；缺失时仅绑定 NPC 并强告警。
- 观测增强（非兜底）：
  - `RimTalkNativeRenderDiagnostic` 新增 `PromptChannel / CurrentPawnLabel / PawnCount / AllPawnCount / ScopedPawnIndex / RemainingTokensPreview`。
  - 当 prompt 含 `{{ pawn.` 且 `CurrentPawn` 为空时写明确错误日志，但按既定策略继续渲染流程。

## RPG 原生 RimTalk 变量收口（v0.7.52）
- RPG 统一运行时出口：
  - `RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs`
  - `BuildUnifiedChannelSystemPrompt(...)` 在 RPG runtime、非 preview 分支调用原生 RimTalk 二次渲染。
- 原生渲染适配层：
  - `RimChat/Prompting/RimTalkNativeRpgPromptRenderer.cs`
  - 职责：构建 RimTalk `PromptContext`、执行 `ScribanParser.Render(...)`、记录 structured diagnostic。
- RPG raw token 保留策略：
  - `RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs`
  - `RenderRawModVariablesSection(...)` 遇到 `*.rimtalk.*` 或 legacy RimTalk token 时，不再走本地 provider 替换，统一保留为 raw token。
- raw token 目录来源：
  - `RimChat/Prompting/PromptRuntimeVariableBridge.cs`
  - 自定义变量 raw token 现在使用 legacy token 形态（Pawn 为 `pawn.xxx`，其余为 `xxx`），供变量浏览器和 `mod_variables` section 共用。

## RimTalk 自定义变量系统性修复（v0.7.51）
- 自定义变量快照刷新主链：
  - `RimChat/Prompting/PromptRuntimeVariableBridge.cs`
  - `RefreshRimTalkCustomVariableSnapshot(bool force = false)` 改为节流刷新；`GetCustomVariables()` 每次读取前都会触发刷新尝试（受冷却时间控制）。
- 自定义变量解析兼容：
  - `RimChat/Prompting/PromptRuntimeVariableBridge.cs`
  - `ParseCustomVariable(...)` 支持 tuple 字段（`Item1..Item4`）和命名字段（`VariableName/Name/SourceModId/Kind...`）双协议读取。
- fail-fast 与可观测性：
  - `RimChat/Prompting/PromptRuntimeVariableBridge.cs`
  - 当 raw 枚举数量 > 0 且解析数量 = 0 时，Bridge 链路阻断并输出明确错误日志。
  - 增加快照日志：`raw_count / parsed_count / duplicate_count / force`。
- 时序修复（工作台 + 浏览器）：
  - `RimChat/Config/RimChatSettings.cs`
  - `AutoPopulatePromptSectionCatalogModVariables()` 在自动填充前强制刷新快照。
  - `RimChat/Config/RimChatSettings_RimTalkVariableBrowser.cs`
  - `EnsurePromptVariableSnapshotCacheFresh()` 在构建展示快照前同步刷新 RimTalk 变量。

## RimChat ↔ RimTalk 变量桥接重构（v0.7.50）
- Bridge 启动主链（fail-fast，仅阻断桥接）：
  - `RimChat/Core/RimChatMod.cs`
  - `RimChat/Prompting/PromptRuntimeVariableBridge.cs`
  - 顺序：`StrictLegacyCleanup` -> `ValidateRimTalkBridgeSignaturesOrFail` -> `RegisterRimChatSummaryVariable` -> `RefreshRimTalkCustomVariableSnapshot`。
- 对外导出变量：
  - `rimchat_summary`（raw token：`{{ rimchat_summary }}`）
  - 聚合实现：`BuildRimChatSummaryAggregateText()`，预算 1200 字符。
- 旧痕迹清理范围：
  - `ContextHookRegistry.UnregisterMod(...)`
  - 历史 `rimchat_*` runtime/context key
  - 旧 `SourceModId` 注入条目 + `DeletedModEntryIds` 残留（`contains("rimchat")`）。
- 工作台全通道 section：
  - `mod_variables` 已加入主链 schema 与默认目录结构。
  - 仅当 section 为空且检测到 RimTalk 自定义变量时，自动填充 raw token 列表。
- 变量浏览器双轨显示：
  - `PromptVariableDisplayEntry` 新增 `RawToken` / `NamespacedToken` / `DefaultInsertToken`。
  - UI 展示 raw + namespaced；插入默认走 raw token。

## create_quest 与 RPG 上下文修复（v0.7.48）
- 任务链路：
  - `RimChat/AI/AIActionExecutor.cs`
  - `ExecuteCreateQuest(...)` 在 fail-fast 失败分支统一回传 `questDefName` 可用列表，阻断非法模板名反复盲猜。
- quest_guidance 根因修复：
  - `RimChat/Config/PromptTextConstants.cs`：`QuestGuidanceNodeLiteralDefault` 改为 `{{ dialogue.quest_guidance_body }}`。
  - `RimChat/Persistence/PromptPersistenceService.cs`：模板迁移补齐中文旧字面量标记识别，旧配置可自动升级到占位注入。
- RPG 上下文增强（手动 + 主动）：
  - `RimChat/Persistence/PromptPersistenceService.TemplateVariables.cs`
  - `RimChat/Persistence/PromptPersistenceService.RpgProfileVariables.cs`
  - 新增变量 `pawn.relation.social_summary`，并扩展 `pawn.target.profile` / `pawn.initiator.profile` 输出：Job、Needs/Hediffs、Recent Memories（受 `RpgSceneParamSwitches` 控制）。
- 默认开关与迁移：
  - `Prompt/Default/SystemPrompt_Default.json`：`IncludeNeeds`、`IncludeRecentJobState` 默认开启。
  - `RimChat/Persistence/PromptPersistenceService.cs`：仅命中历史默认签名时自动升级旧开关，避免覆盖用户自定义值。
- 关系画像模板同步：
  - `Prompt/Default/PawnDialoguePrompt_Default.json`
  - `Prompt/Default/PromptUnifiedCatalog_Default.json`
  - `RimChat/Config/PromptUnifiedDefaults.cs`
  - `RimChat/Config/RpgPromptDefaultsConfig.cs`
  - 在 `rpg_relationship_profile` 注入 `pawn.relation.social_summary`。

## 提示词工作台派系描述/人设变量链路（当前变更）
- 新增变量：`world.faction.description`
  - 取值链路：`PromptPersistenceService.TemplateVariables.ResolveTemplateVariableValue(...)` -> `BuildFactionDescriptionVariableText(...)` -> `FactionPromptManager.GetPrompt(faction.def.defName)`。
  - 数据源：`Prompt/Default/FactionPrompts_Default.json`（默认） + `Prompt/Custom/FactionPrompts_Custom.json`（覆盖）。
- 收敛 `pawn.personality` 运行时取值：
  - 入口：`PromptPersistenceService.TemplateVariables.BuildPawnPersonalityVariableText(...)`。
  - 解析：`GameComponent_RPGManager.ResolveEffectivePawnPersonalityPrompt(...)`。
  - 顺序：RimTalk 人格 -> RimChat 已存人格 -> 人格引导即时生成并持久化。
- 工作台快捷区：
  - `RimChatSettings_PromptQuickActions.DrawPromptWorkspaceQuickActions(...)` 的“派系提示词”改为派系模板编辑菜单（`Dialog_FactionPromptEditor`）。
  - “人设提示词”保存后自动尝试把 `{{ pawn.personality }}` 注入当前通道 `character_persona`（幂等）。
- 背景迁移：
  - 默认资产：`Prompt/Default/PromptSectionCatalog_Default.json`、`Prompt/Default/PromptUnifiedCatalog_Default.json`、`Prompt/Default/RimTalkPromptEntries_Default.json` 的 `any/system_rules` 均追加背景段落。
  - 运行时迁移：`RimChatSettings_RimTalkCompat.ApplyUnifiedCatalogOneTimeMigration(...)` 新增背景补入步骤（仅缺失时追加，不覆盖）。

## 提示词工作台沉浸感约束（当前变更）
- 全局 `system_rules` 节点新增括号使用约束（允许括号叙事，禁止括号内规则/系统/元信息说明）：
  - `Prompt/Default/PromptSectionCatalog_Default.json`
  - `Prompt/Default/RimTalkPromptEntries_Default.json`
  - `Prompt/Default/PromptUnifiedCatalog_Default.json`
- 生效方式：
  - 注入点为 `any -> system_rules`，由工作台主链继承到各通道默认提示词。

## RPG关系画像去重 + 移除 kinship=no 限制（v0.7.44）
- RPG 关系画像去重（停止独立边界节点输出）：
  - `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - `ResolveRpgNodePlacements(...)` 中 `rpg_kinship_boundary` 节点保留布局兼容，但不再单独渲染正文，避免与 `relationship_profile` 重复。
- 亲缘规则语义收口：
  - `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - `BuildRpgKinshipBoundaryGuidanceText(...)` 改为 `kinship=no` 直接返回空字符串，`kinship=yes` 才注入 RomanceAttempt/Date/MarriageProposal 边界限制。
- 关系画像模板改为条件引导行（no 时整行隐藏）：
  - `Prompt/Default/PawnDialoguePrompt_Default.json`
  - `Prompt/Default/PromptUnifiedCatalog_Default.json`
  - `RimChat/Config/RpgPromptDefaultsConfig.cs`
  - `RimChat/Config/PromptUnifiedDefaults.cs`
- 历史模板自动迁移（幂等）：
  - `RimChat/Config/PromptUnifiedCatalog.cs`
  - 在 unified catalog 归一化阶段自动把旧写法 `引导：{{ dialogue.guidance }}` / `Guidance: {{ dialogue.guidance }}` 升级为条件渲染写法，覆盖 RPG/主动RPG/any 通道，防止旧自定义模板复发。

## 日志观测入口 + 30分钟趋势 + RPG开窗卡顿治理（v0.7.43）
- 日志观测窗口新增设置入口：
  - `RimChat/UI/Dialog_ApiDebugObservability.cs`
  - 头部新增 `RimChat_ApiDebugOpenSettingsButton`，点击后打开 `Dialog_ModSettings(RimChatMod)`。
  - 缺失 Mod 实例时 fail-fast 提示 `RimChat_ApiDebugOpenSettingsFailed`，不做静默降级。
- Token 趋势窗口收口为最近 30 分钟、1 分钟粒度：
  - `RimChat/AI/AIChatServiceAsync.DebugTelemetry.cs`
  - 常量改为 `DebugWindowMinutes=30`、`DebugBucketMinutes=1`、`DebugRetentionMinutes=35`。
  - 快照构建改为单次遍历：同轮完成 records 克隆、summary 聚合、bucket 聚合。
- RPG 对话开窗卡顿治理（短期阻断 + 根治）：
  - `RimChat/UI/Dialog_RPGPawnDialogue.cs`
  - 构造期不再调用 `BuildPromptMemoryBlock(...)` 做存在性探测，改为 `HasPromptMemory(...)`，避免开窗瞬间重复重算。
  - `RimChat/Memory/RpgNpcDialogueArchiveManager.PromptCache.cs`
  - 新增 prompt memory 版本戳缓存（target/interlocutor/summary 参数维度）。
  - `RimChat/Memory/RpgNpcDialogueArchiveManager.cs`
  - `RimChat/Memory/RpgNpcDialogueArchiveManager.Sessions.cs`
  - 在 turn 记录、session finalize、外交摘要、读档重载、压缩成功/失败等路径统一触发缓存失效，保证一致性并避免脏读。
- 本地化同步：
  - `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - `1.6/Languages/English/Keyed/RimChat_Keys.xml`
  - 更新观测窗口趋势文案到 30 分钟/1 分钟，并新增设置按钮相关语言键。

## Think 标签双层过滤收口（v0.7.42）
- 统一新增模型输出清洗器：
  - `RimChat/AI/ModelOutputSanitizer.cs`
  - 职责：删除 `<think>...</think>` / `<thinking>...</thinking>` 整段内容，并处理未闭合起始标签与残留闭合标签。
- 服务层入口前置清洗（入业务链前）：
  - `RimChat/AI/AIJsonContentExtractor.cs`
  - `TryExtractPrimaryText(...)` 在命中文本字段后先执行 `ModelOutputSanitizer.StripReasoningTags(...)`，清洗后为空则继续尝试下一个候选字段。
- UI 显示层前置清洗（最终渲染前）：
  - `RimChat/AI/ImmersionOutputGuard.cs`
  - `ValidateVisibleDialogue(...)` 在拆分可见文本/尾部 actions JSON 前先执行同一清洗器，阻断旁路文本直达 UI。
- 外交解析层同步清洗：
  - `RimChat/AI/AIResponseParser.cs`
  - `NormalizeDialogueText(...)` 先执行 think 标签剥离，再执行原有策略段落裁剪与沉浸校验。
- 影响范围说明：
  - 通过 `ImmersionOutputGuard` 的外交对话、RPG 对话、NPC 主动推送、PawnRPG 主动推送共享该防线。
  - 不改动作协议、不改存档结构、不改 Def 与 Patch 链路。

## 外交对话头像与说话者补齐（v0.7.41）
- 外交气泡头像渲染与布局扩展：
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ImageRendering.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.Speakers.cs`
  - 普通/图片消息新增 24px 外角头像；系统消息保持无头像。
  - 气泡宽度上限提升至可用区 85%，并改为“头像通道 + 气泡通道”排版。
- 会话说话者解析与补齐：
  - `RimChat/UI/Dialog_DiplomacyDialogue.Speakers.cs`
  - 打开外交窗口时自动补齐历史消息说话者与显示名。
  - 玩家侧缺失说话者回退到“社交最高殖民者”。
  - 对方侧优先派系领袖；领袖缺失时会话内固定随机发言人。
- 消息模型持久化扩展（向后兼容）：
  - `RimChat/Memory/FactionDialogueSession.cs`
  - `DialogueMessageData` 新增 `speakerPawnThingId` 与 `speakerPawn` 引用，支持存档恢复头像来源。
  - `AddMessage(...)` / `AddImageMessage(...)` 增加可选 `speakerPawn` 参数。
- 写入链路同步：
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ImageAction.cs`
  - `RimChat/NpcDialogue/GameComponent_NpcDialoguePushManager.cs`
  - 外交发送、AI 回复、fallback、图片消息、NPC 主动推送统一写入说话者 Pawn。

## Pawn↔Pawn 右键对话战斗态拦截（v0.7.40）
- 新增统一判定工具，避免菜单层与执行层规则漂移：
  - `RimChat/Core/PawnCombatStateUtility.cs`
  - 规则：`Drafted` 或当前 JobDef 属于 `Wait_Combat / AttackMelee / AttackStatic / UseVerbOnThing`
- 右键入口改为双向战斗态门控：
  - `RimChat/Comp/CompPawnDialogue.cs`
  - 当前 pawn 或目标 pawn 任一处于战斗态时，不显示 RimChat 对话选项。
- 执行层新增 fail-fast 二次拦截：
  - `RimChat/AI/JobDriver_RPGPawnDialogue.cs`
  - Job 执行到窗口打开前再次判定，阻断时序绕过。

## RimTalk 污染隔离与 legacy 变量清理（v0.7.39）
- RimTalk 桥接 provider 改为显式启用门控；未启用时不注册 runtime provider：
  - `RimChat/Prompting/PromptRuntimeVariableRegistry.cs`
  - `RimChat/Prompting/PromptRuntimeVariableProviders.cs`
- 启动时尝试清理 RimTalk 中旧版 `rimchat_*` 遗留上下文变量：
  - `RimChat/Prompting/PromptRuntimeVariableBridge.cs`
  - `RimChat/Core/RimChatMod.cs`
- `dialogue.rimtalk.history*` 移除 RimChat 跨通道摘要回退，避免历史污染：
  - `RimChat/Prompting/PromptRuntimeVariableBridge.cs`

## 生图封闭补强（v0.7.38）
- 图片生成设置页封板（开发中提示，禁止交互）：
  - `RimChat/Config/RimChatSettings_ImageApi.cs`
- 外交提示词动作目录构建阶段强制排除 `send_image`：
  - `RimChat/Persistence/PromptPersistenceService.cs`
- 提示词工作台 `ApiActions` 编辑列表排除 `send_image`：
  - `RimChat/Config/RimChatSettings_Prompt.cs`

## 生图功能开发中封闭（v0.7.37）
- 生图功能统一封闭并返回“开发中”提示（UI + 业务执行双层拦截）：
  - `RimChat/Core/ImageGenerationAvailability.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ImageAction.cs`
  - `RimChat/DiplomacySystem/DiplomacyImageGenerationService.cs`
  - `RimChat/DiplomacySystem/ApiActionEligibilityService.cs`
- 外交窗口生图相关入口改为禁用（相册/自拍）：
  - `RimChat/UI/Dialog_DiplomacyDialogue.SocialCircleView.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.AlbumSelfieActions.cs`
- 移除生图提示词注入与自拍隐藏注入拼接：
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `RimChat/UI/Dialog_DiplomacySelfieConfig.cs`
  - `RimChat/AI/AIResponseParser.cs`
- 本地化键新增：
  - `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - `1.6/Languages/English/Keyed/RimChat_Keys.xml`

## 输出协议单一权威收敛（v0.7.36）
- 响应协议正文统一收敛到 `response_contract` 运行时正文（`dialogue.response_contract_body`），`RESPONSE FORMAT` 与 `CRITICAL ACTION RULES` 改为引用式提示：
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `RimChat/Config/PromptTextConstants.cs`
- 默认 Prompt 各通道 `output_specification` 段统一改为“引用语句”，不再内嵌协议正文：
  - `RimChat/Config/RimTalkPromptEntryDefaultsConfig.cs`
  - `Prompt/Default/PromptSectionCatalog_Default.json`
  - `Prompt/Default/RimTalkPromptEntries_Default.json`
  - `Prompt/Default/PromptUnifiedCatalog_Default.json`
- 开发文档移除旧 `{\"action\":...}` 单对象协议，统一声明 `{\"actions\":[...]}`：
  - `doc/Api.md`
  - `doc/config.md`
  - `doc/VersionLog.txt`
  - `doc/VersionLog_en.txt`

## 响应契约节点占位符收口（v0.7.35）
- 响应契约节点默认模板统一为纯运行时占位符 `{{ dialogue.response_contract_body }}`：
  - `RimChat/Config/PromptTextConstants.cs`
  - `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
  - `Prompt/Default/PromptUnifiedCatalog_Default.json`

## 任务规则节点变量->文本收口（v0.7.34）
- 任务规则节点渲染链路改为文本优先，并兼容 legacy token 自动解引用：
  - `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - `ResolveDiplomacyNodePlacements(...)` 的 `quest_guidance_node_template` 分支改为 `ResolveQuestGuidanceNodeText(...)`
- 任务规则节点默认值改为纯文本常量：
  - `RimChat/Config/PromptTextConstants.cs`
  - `QuestGuidanceNodeLiteralDefault`
- 节点变量校验上下文移除任务规则注入变量：
  - `RimChat/Persistence/TemplateVariableValidationContext.cs`

## Prompt Workbench 节点编排表头清理（v0.7.32）
- 移除“节点编排”列表顶部固定 `正文` 表头，避免正文标签固定占据首行：
  - `RimChat/Config/RimChatSettings_PromptSectionWorkspace.cs`
- 该改动仅影响 UI 呈现，不改变节点布局存储与排序算法。

## Prompt Workbench 正文/思维链末尾排序修复（v0.7.31）
- 结构化预览与导出文本组装顺序统一为“其它节点 -> 正文 -> 思维链 -> 结束标签”，保证思维链固定在正文底部：
  - `RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs`
  - `RimChat/Persistence/PromptPersistenceService.SectionAggregates.cs`
- 思维链识别改为统一函数 `IsThoughtChainPlacement(...)`，不再依赖固定 slot，跨频道一致：
  - `RimChat/Persistence/PromptPersistenceService.SectionAggregates.cs`
  - `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
- 其它节点保持原相对顺序，不引入新的兜底排序逻辑。

## Prompt Workbench 节点命名与正文预览统一（v0.7.30）
- 节点显示名统一改为可读业务名，不再把内部模板 id 直接暴露到节点编辑列表：
  - `RimChat/Config/PromptUnifiedNodeSchemaCatalog.cs`
  - `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - `1.6/Languages/English/Keyed/RimChat_Keys.xml`
- 预览中的正文块与思维链顺序统一到同一条组装规则：
  - `RimChat/Persistence/PromptPersistenceService.SectionAggregates.cs`
  - `RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs`
  - `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - `thought_chain_node_template` 统一后置到正文之后
- 预览标题去技术附加标签：
  - `RimChat/UI/PromptWorkspaceStructuredPreviewRenderer.cs`
  - 节点块不再显示槽位前缀；正文分段标题不再附加 `&lt;section_id&gt;`

## 节点模板动态正文回归（v0.7.28）
- 默认模板事实源收敛到 Scriban 变量正文（不再维护三段说明文硬文本）：
  - `RimChat/Config/PromptTextConstants.cs`
  - `ApiLimitsNodeLiteralDefault` / `QuestGuidanceNodeLiteralDefault` / `ResponseContractNodeLiteralDefault`
- 运行时正文继续由旧业务链路实时生成：
  - `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - `ResolveDiplomacyNodePlacements(...)` 内部 `AppendApiLimits(...)`、`AppendDynamicQuestGuidance(...) + AppendQuestSelectionHardRules(...)`、`AppendAdvancedConfig(...) / AppendSimpleConfig(...)`
- 三段节点新增严格空正文拦截（fail-fast）：
  - `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - `RenderPromptNodeTemplate(...)` 在正文为空时抛 `PromptRenderException(TemplateMissing)`。
- 旧配置一次性迁移入口：
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `EnsurePromptTemplateDefaults(...)` 新增三段旧硬文本识别并自动重写为 Scriban 模板，命中写日志。

## Social News JSON 合同统一（v0.7.27）
- 默认统一目录与社交链路合同收敛为同一事实源，避免运行时输出合同漂移：
  - `Prompt/Default/PromptUnifiedCatalog_Default.json`
  - `social_news_style` / `social_news_json_contract` / `social_news_fact` 改为与 `SocialCirclePrompt_Default.json` 一致的完整模板。
- 代码级回退节点改为引用社交默认模板常量，避免再次出现“默认资产与回退文案不一致”：
  - `RimChat/Config/PromptUnifiedDefaults.cs`
  - `PromptTextConstants.SocialCircleNewsStyleTemplateDefault`
  - `PromptTextConstants.SocialCircleNewsJsonContractTemplateDefault`
  - `PromptTextConstants.SocialCircleNewsFactTemplateDefault`

## Strict Workbench WYSIWYG（v0.7.26）
- 统一运行时主链提示词入口到 Workbench composer（deterministic）：
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `BuildFullSystemPrompt(...)` / `BuildRPGFullSystemPrompt(...)` / `BuildDiplomacyStrategySystemPrompt(...)` 全部改为走 `BuildUnifiedChannelSystemPrompt(...)`。
- 运行时 composer 改为 deterministic 组装，停用环境/动态变量注入差异：
  - `RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs`
  - `BuildUnifiedChannelSystemPrompt(...)` 内部 `deterministicPreview=true`。
- 工作台预览改为始终展示完整布局拼接（不再区分 section-only 预览）：
  - `RimChat/Config/RimChatSettings_PromptSectionWorkspace.cs`
  - `GetPromptWorkspaceStructuredPreview()` 始终走 `BuildPromptWorkspaceStructuredLayoutPreview(...)`。
- 移除请求链路中的“工作台外追加文本”：
  - `RimChat/NpcDialogue/GameComponent_NpcDialoguePushManager.cs`
  - `RimChat/PawnRpgPush/GameComponent_PawnRpgDialoguePushManager.Generation.cs`
  - `RimChat/UI/Dialog_RPGPawnDialogue.cs`
  - `RimChat/UI/Dialog_RPGPawnDialogue.RequestContext.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.Strategy.cs`
  - `RimChat/Memory/DialogueSummaryService.cs`
  - `RimChat/Memory/RpgNpcDialogueArchiveManager.Sessions.cs`
- 发送层禁用隐式消息改写/重写重试，确保发包文本不偏离所见：
  - `RimChat/AI/AIChatServiceAsync.cs`
  - 取消 system-only 自动补 user；取消 HTTP 400 rejected-input 的降载重写重试。

## Prompt Unified Catalog 生命周期一致性修复（v0.7.25）
- 节点严格校验入口（仅节点链路启用）：
  - `RimChat/Config/PromptUnifiedNodeSchemaCatalog.cs`
  - 新增 `NormalizeStrictChannelOrThrow(...)`、`GetAllowedNodesStrict(...)`、`EnsureNodeAllowedForChannelOrThrow(...)`，未知/空 channel 不再回退到 `any`。
- 节点读写 fail-fast：
  - `RimChat/Config/PromptUnifiedCatalog.cs`
  - `ResolveNode(...)` / `ResolveNodeLayout(...)` / `SetNode(...)` / `SetNodeLayout(...)` 对非法 `channel/nodeId` 立即抛 `InvalidOperationException`。
- 不变量与归一化报告：
  - `RimChat/Config/PromptUnifiedCatalog.cs`
  - 新增 `ValidateInvariantsOrThrow()` 与 `NormalizeWithReport(...)`（含 `RemovedNodeCount/RemovedLayoutCount/FilledDefaultLayoutCount/UnknownChannelCount/HasStructuralChange`）。
- 兼容初始化与保存判定收敛：
  - `RimChat/Config/RimChatSettings_RimTalkCompat.cs`
  - `EnsureUnifiedCatalogReady()` 改为基于 `normalizeReport.HasStructuralChange` 判定保存，移除旧 `requiresLayoutSave` 数量推断。
- 日志分层：
  - 阻断性不变量错误使用 `Error`（抛异常前）。
  - 自动清洗使用一次性摘要 `Warning`。
  - 默认布局补全与迁移完成使用 `Info`。

## 请求消息规范化与派系好感空引用修复（v0.7.24）
- 全局请求入口 fail-fast 规范化：
  - `RimChat/AI/AIChatServiceAsync.cs`
  - `RimChat/AI/AIChatService.cs`
  - 发送前统一标准化 `role`（仅保留 `system/user/assistant`），并在仅 system 场景自动补最小 `user` 指令，阻断 `HTTP 400 Param Incorrect`。
- 摘要链路显式 user 消息：
  - `RimChat/Memory/DialogueSummaryService.cs`
  - `RimChat/Memory/RpgNpcDialogueArchiveManager.Sessions.cs`
  - `summary_generation`、`rpg_archive_compression` 请求改为 system+user 双消息，避免 provider 参数拒绝。
- 摘要通道归一化：
  - `RimChat/Memory/DialogueSummaryService.cs`
  - `TryQueueLlmFallback(...)` 的 `usageChannel` 按 root 通道映射到 `Diplomacy/Rpg`，统一重试与调试归类。
- 派系档案变量安全读取：
  - `RimChat/Persistence/PromptPersistenceService.TemplateVariables.cs`
  - `BuildCurrentFactionProfileVariableText(...)` 改为安全读取好感；玩家派系/自派系返回 `Goodwill: N/A`，不再触发 `Faction.PlayerGoodwill` 空引用。

## Prompt Node 通道强约束与 Fail-Fast 清理（v0.7.23）
- 节点通道白名单（单一事实源）：
  - `RimChat/Config/PromptUnifiedNodeSchemaCatalog.cs`
  - 新增 `GetAllowedNodes(...)` / `IsNodeAllowedForChannel(...)`，按通道限定可编辑与可注入节点集合。
- 统一目录规范化（加载即清理）：
  - `RimChat/Config/PromptUnifiedCatalog.cs`
  - `NormalizeNodes(...)` 与 `NormalizeNodeLayout(...)` 改为按通道白名单裁剪；发现非法节点/布局立即报错日志并移除。
- 运行时构建强约束：
  - `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - `GetOrderedNodeLayouts(...)` 只保留当前通道允许节点，并对越界布局输出错误日志，阻断串线注入。
- Workbench 编辑面强约束：
  - `RimChat/Config/RimChatSettings_PromptSectionWorkspace.cs`
  - 节点模式选择器、节点下拉、整通道重置均改为“仅当前通道允许节点”，无节点通道自动回退到 section 模式。
- 统一预览链路收敛：
  - `RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs`
  - 组合器在无用户布局时仅为允许节点生成默认布局，避免跨通道节点进入预览。
- 兼容初始化修正：
  - `RimChat/Config/RimChatSettings_RimTalkCompat.cs`
  - `requiresLayoutSave` 改为按“通道允许节点数”判定，避免通道切换后持续误判需要迁移。

## Workbench 所见即所得并轨（v0.7.22）
- 统一 composer：
  - `RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs`
  - 新增共享拼装入口 `BuildUnifiedChannelSystemPrompt(...)`，统一 section 聚合、node 编排、变量注入与 payload 合并。
- 预览与运行时并轨：
  - `RimChat/Persistence/PromptPersistenceService.SectionAggregates.cs`
  - `BuildPromptWorkspaceStructuredLayoutPreview(...)` / `BuildPromptWorkspaceStructuredSectionPreview(...)` 均改为走同一 composer。
- 通道映射单一事实源：
  - `RimChat/Config/PromptSectionSchemaCatalog.cs`
  - `RimChat/Config/RimChatSettings_PromptAdvancedFramework.cs`
  - root 归属、共享通道与运行时归一化全部收敛到 schema 目录。
- 旁路链路统一成单 system 消息：
  - `RimChat/DiplomacySystem/Social/SocialNewsPromptBuilder.cs`
  - `RimChat/DiplomacySystem/GameComponent_RPGManager.PersonaBootstrap.cs`
  - `RimChat/Memory/DialogueSummaryService.cs`
  - `RimChat/Memory/RpgNpcDialogueArchiveManager.Sessions.cs`
- 图像链路 Unified 化：
  - `RimChat/UI/Dialog_DiplomacyDialogue.ImageAction.cs`
  - `RimChat/DiplomacySystem/ApiActionEligibilityService.cs`
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `template_id` 解析与校验改走 Unified alias，保留历史模板 ID 兼容。
- 旧配置一次迁移：
  - `RimChat/Config/RimChatSettings_RimTalkCompat.cs`
  - 升级 `PromptUnifiedCatalog.MigrationVersion` 门控，首载导入 legacy RPG/图像模板到 Unified，迁移后运行时只读 Unified。

## Prompt Workbench 运行时一致校验与结构化预览（v0.7.21）
- 校验上下文模型：
  - `RimChat/Persistence/TemplateVariableValidationContext.cs`
  - 负责“运行时变量目录 + 节点注入变量”合并，工作台按上下文进行严格校验。
- 模板校验入口重载：
  - `RimChat/Persistence/PromptPersistenceService.TemplateVariables.cs`
  - 新增 `ValidateTemplateVariables(templateText, validationContext)`（内部重载）。
- 结构化预览模型与构建：
  - `RimChat/Persistence/PromptWorkspacePreviewModels.cs`
  - `RimChat/Persistence/PromptPersistenceService.SectionAggregates.cs`
  - 提供分段模式与节点编排模式的最终顺序预览块。
  - 主链分段块新增 `PromptWorkspacePreviewSubsection`，支持次级标题渲染。
- 轻量预览渲染器：
  - `RimChat/UI/PromptWorkspaceStructuredPreviewRenderer.cs`
  - 取代工作台右侧只读 chip 预览路径，缓存布局高度减少重绘开销。
  - 主链分段聚合支持 section 级二级标题条与正文分段展示。
- 防抖保存与关闭前强制落盘：
  - `RimChat/Config/RimChatSettings_PromptSectionWorkspace.cs`
  - `RimChat/UI/Dialog_PromptWorkbenchLarge.cs`
  - 编辑态采用内存缓冲 + 500ms 防抖；切换上下文与关闭窗口强制落盘。

## Prompt Node 编排（v0.7.19）
- 节点布局模型：
  - `RimChat/Config/PromptUnifiedCatalog.cs`
  - `PromptUnifiedNodeLayoutConfig`（`NodeId/Slot/Order/Enabled`）
  - `PromptUnifiedNodeSlot`（5 固定槽位）
- 节点 schema 与显示名：
  - `RimChat/Config/PromptUnifiedNodeSchemaCatalog.cs`
- 运行时槽位注入器：
  - `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - 外交 / RPG / 策略三链统一按布局注入节点。
- 工作台节点编排 UI：
  - `RimChat/Config/RimChatSettings_PromptSectionWorkspace.cs`
  - 槽位分组列表、拖拽重排、槽位切换、完整预览落点摘要。
- 预览与落点模型：
  - `RimChat/Persistence/PromptNodePlacementModels.cs`
  - `RimChat/Persistence/PromptPersistenceService.SectionAggregates.cs`

## Prompt Unified 主干
- 统一存储：
  - `RimChat/Config/PromptUnifiedCatalog.cs`
  - `RimChat/Config/PromptUnifiedCatalogProvider.cs`
  - `Prompt/Default/PromptUnifiedCatalog_Default.json`
- 节点 schema：
  - `RimChat/Config/PromptUnifiedNodeSchemaCatalog.cs`
- 统一默认节点兜底：
  - `RimChat/Config/PromptUnifiedDefaults.cs`

## 运行时拼装
- 外交/RPG/策略主拼装入口：
  - `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - `RimChat/Persistence/PromptPersistenceService.SectionAggregates.cs`
- 对外入口（签名保持）：
  - `BuildFullSystemPrompt(...)`
  - `BuildDiplomacyStrategySystemPrompt(...)`
  - `BuildRPGFullSystemPrompt(...)`

## 工作台与编辑
- Prompt 工作台（Section + Node）：
  - `RimChat/Config/RimChatSettings_PromptSectionWorkspace.cs`
  - `PersistPromptWorkspaceBufferNow(..., persistToDisk:true)` 仅在实质文本变更时同步 preset payload；无改动保存静默成功。
- 通道/分段 schema：
  - `RimChat/Config/PromptSectionSchemaCatalog.cs`
  - `RimChat/Config/RimTalkPromptEntryChannelCatalog.cs`

## 兼容迁移
- 旧配置桥接与映射：
  - `RimChat/Config/RimChatSettings_RimTalkCompat.cs`
  - `RimChat/Config/PromptLegacyCompatMigration.cs`
- 迁移策略：
  - 首次加载将 legacy prompt section/template 映射到 unified catalog；
  - 迁移完成后运行时只读 unified catalog。

## 社交链路
- 社交新闻提示词构建：
  - `RimChat/DiplomacySystem/Social/SocialNewsPromptBuilder.cs`
  - 统一从 unified node 读取 `social_news_*` 模板。

## Prompt 域链路隔离与自愈（v0.7.24）
- 运行时与预览构建隔离：
  - `RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs`
  - `BuildUnifiedChannelSystemPrompt(...)` 新增 `deterministicPreview` 参数，运行时入口固定 `false`，工作台预览保持 `true`。
- 域装配语义校验升级：
  - `RimChat/Persistence/PromptPersistenceService.DomainStorage.cs`
  - `TryLoadPromptDomains(...)` 新增语义校验：外交动作全集、`ResponseFormat.JsonTemplate`、关键节点模板（`api_limits/quest_guidance/response_contract`）缺失即判坏。
  - default-only 路径新增“聚合重建”回路：直接分域装配失败时，改走 default-only 聚合 JSON 重建并再次校验。
- 动作源单一化：
  - `RimChat/Persistence/PromptPersistenceService.DomainStorage.cs`
  - `ApiActions` 仅来自外交域，不再合并社交 `PublishPublicPostAction`。
- 默认回退路径净化：
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `CreateDefaultConfig()` 改为严格 default-only 读取（不读 custom）；移除 legacy `InitializeDefaults()` 最小配置回退。
  - `RimChat/Persistence/PromptDomainFileCatalog.cs`
  - 路径解析新增根目录归一化：若 `LoadedMod` 根落在 `.../1.6` 子目录，将自动回退到真实 mod 根目录（含 `Prompt/Default` 的目录）。
  - `RimChat/Persistence/PromptDomainJsonUtility.cs`
  - default domain JSON 读取改为反射反序列化优先（`ReflectionJsonFieldDeserializer`），避免 `JsonUtility` 静默吞字段。
- 启动自愈与迁移追踪：
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - 检测坏 custom 且 default-only 可用时：先备份 `Prompt/Custom/_backup/<timestamp>`，再用默认配置重建并写回；日志记录回退来源与修复摘要。
  - 若 default-only 语义也失败：存在缓存则保留缓存并阻断 auto-heal 写回；无缓存则 fail-fast 抛 `PromptRenderException`。
- 新增域版本锚点：
  - `RimChat/Persistence/PromptDomainPayloads.cs`
  - `SystemPromptDomainConfig.PromptDomainSchemaVersion`（单锚点，当前 `1`）。
- 运行期 fail-fast：
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs`
  - 关键 runtime 节点为空或 `ResponseFormat.JsonTemplate` 为空时抛 `PromptRenderException`，阻断请求，禁止静默降级。

## 文本完整性治理（v0.7.48）
- 可见对白乱码/碎片检测（外交 + RPG）：
  - `RimChat/AI/TextIntegrityGuard.cs`
  - `RimChat/AI/AIChatServiceAsync.cs`
  - 请求链在 `ImmersionOutputGuard` 之后追加文本完整性检测；命中后同链路重试 1 次，仍失败则回退本地沉浸兜底文本。
- 摘要入库净化与修复：
  - `RimChat/Memory/LeaderMemoryManager.SummaryIntegrity.cs`
  - `RimChat/Memory/LeaderMemoryManager.cs`
  - 所有 summary upsert 在入库前统一净化与异常判定；异常触发一次修复请求，修复失败则丢弃并记录结构化告警日志。
- Faction 风格变量注入：
  - `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - Faction Prompt 文本检测到 Scriban 占位符时按 strict 模式渲染，并注入 `world.faction_settlement_summary` 与 settlement 子变量（派系长期据点/基地视图，而非临时事件地图）。
