# RimChat AI API 文档（v0.9.87）

## 空投定价回退到市场价系统（v0.9.88）

- `RimChat.DiplomacySystem.GameAIInterface.ItemAirdrop`
  - `PrepareItemAirdropCandidates(...)` 移除贸易买入价覆盖路径，候选单价统一回退为 `ThingDefRecord.MarketValue`（最小值 `0.01`）。
- `RimChat.DiplomacySystem.GameAIInterface.ItemAirdrop.Barter`
  - `BuildPaymentPlan(...)` 保持“市场价 + 既有倍率规则（无 tradeTags x10、ExoticMisc x2）”预算派生，不再依赖贸易上下文。
  - `ResolveAirdropPaymentUnitPrice(...)` 缺失 def 的失败码改为市场价语义 `market_value_def_missing`。
- `RimChat.UI.Dialog_ItemAirdropTradeCard`
  - 需求侧参考价改为直接使用市场价，不再解析贸易买入价。
- Prompt 契约同步
  - `request_item_airdrop` 预算说明改为“按市场价（含既有倍率）求和后 Floor 派生；`budget_silver` 仅审计”。

## 空投交易规则统一与贸易价格口径根修（v0.9.87）

- `RimChat.DiplomacySystem.ItemAirdropTradePolicy`
  - 新增统一规则入口 `ResolveRuleSnapshot(Faction)`，输出 `AirdropTradeRuleSnapshot`：
    - `TradersGuild + Ally`: `shipping=150`, `tradeLimit=12000`
    - `TradersGuild + 非 Ally`: `shipping=200`, `tradeLimit=800`
    - `非 TradersGuild + Ally`: `shipping=200`, `tradeLimit=8000`
    - 其他派系：`shipping=250`, `tradeLimit=max(500, 500 + floor(goodwill/5)*300)`
  - 新增贸易买入价解析 `TryResolvePlayerBuyPrice(...)`，要求存在有效玩家谈判者与交易上下文（商队/轨道商）。
- `RimChat.DiplomacySystem.GameAIInterface.ItemAirdrop.Barter`
  - `BuildPaymentPlan(...)` 改为按贸易买入价计算 `payment_items` 预算，不再使用 `MarketValue/BaseMarketValue`。
  - `PrepareItemAirdropTradeForMap(...)` 新增交易上限 fail-fast：`paymentTotalSilver > tradeLimit` 时返回 `trade_limit_exceeded`。
- `RimChat.DiplomacySystem.GameAIInterface.ItemAirdrop`
  - `RequestItemAirdrop(...)` 现在会先解析可用玩家谈判者；缺失时 fail-fast `player_negotiator_required`。
  - 候选包价格统一注入贸易买入价覆盖，`max_legal_count` 与候选 `unit` 保持同一价格口径。
- `RimChat.Persistence.PromptPersistenceService`
  - `AppendAirdropTradeRules(...)` 改为读取统一规则快照并注入动态运费/限额文案。
  - `request_item_airdrop` 契约文本更新为“预算由贸易买入价派生（受社交影响）”。

## Google API 模型加载与配置校验根修（v0.9.79）

- `RimChat.Config.RimChatSettings`
  - `ParseGoogleModelsFromResponse(string json)`
    - Google 模型列表解析改为 typed JSON 优先，空结果时回退扫描 JSON 中的 `name` 字段。
    - 输出继续统一为去掉 `models/` 前缀后的模型 ID，保持与 OpenAI-compatible chat 请求的 `model` 字段契约一致。
  - `TestConnectionSync()`
    - 快速连通性测试不再复用 `ApiConfig.IsValid()` 的“可聊天配置”定义。
    - 云端快速探测现在只要求：存在已启用配置 + API Key 已填写；模型选择改由深度可用性链路负责。
  - `ResolvePrimaryCloudConfigForConnectivity()` / `TryValidateCloudConfigForConnectivity(...)`
    - 成为 API 设置页快速连通性按钮的单一配置入口。
- `RimChat.Config.ApiUsabilityDiagnosticService`
  - `ValidateCloudConfig(...)`
    - 配置校验阶段对缺 API Key / 缺模型返回本地化细节文本。
    - Google provider 保持“Base URL 可空，走 provider 内置 endpoint”语义，不额外引入 URL 必填门槛。
- `RimChat.Config.RimChatSettings_ApiUsability`
  - `BuildUsabilitySummaryText(...)`
    - 当失败发生在 `ConfigValidation` 阶段时，摘要直接附带精确配置错误文本，减少“摘要泛化、细节藏在技术详情里”的误导。

## 对话结构化主协议与思维链主链退出（v0.9.71）

- `RimChat.Dialogue.DialogueResponseEnvelope`
  - 统一 stage-A 对话结果对象。
  - 主字段改为 `VisibleDialogue`、`ActionsJson`、`ProtocolKind`、`FailureReason`、`IsValid`。
  - `DialogueText` 保留为 `VisibleDialogue` 的兼容访问器。
- `RimChat.Dialogue.DialogueResponseEnvelopeParser`
  - `Parse(string response, DialogueUsageChannel usageChannel)`
    - 结构化主协议解析入口。
    - 优先接受顶层 JSON：`visible_dialogue` + 可选 `actions/meta/debug`。
    - 旧单文本仅作为过渡输入适配，不再扩展异常兼容。
- `RimChat.AI.AIChatServiceAsync`
  - 在 `DiplomacyDialogue / RpgDialogue / NpcPush / PawnRpgPush` 上新增 envelope fail-fast 校验。
  - `ShouldUseStructuredDialogueEnvelope(...)`
    - 仅在真实对话链路启用结构化主协议，不影响 `StrategySuggestion / SocialNews` 等非对话 JSON 通道。
- `RimChat.AI.AIResponseParser`
  - 新增 `ParseResponse(DialogueResponseEnvelope envelope, Faction faction)`。
  - 外交通道从 envelope 的 `VisibleDialogue / ActionsJson` 构建 `ParsedResponse`，不再自由切 raw 文本。
- `RimChat.AI.ModelOutputSanitizer`
  - 新增 `SplitVisibleAndTrailingActions(...)`
  - 新增 `ComposeVisibleAndTrailingActions(...)`
  - 成为“对白/动作 JSON 切分”的单一真相源。
- `RimChat.AI.ImmersionOutputGuard`
  - 新增 `ReasoningLeakage` 违规类型。
  - 新增 `ValidateVisibleDialogueParts(...)`，只校验结构化后的 `visible_dialogue`。
- `RimChat.AI.TextIntegrityGuard`
  - 新增 `ValidateVisibleDialogueParts(...)`，只校验结构化后的 `visible_dialogue`。
- `RimChat.AI.DiplomacyResponseContractGuard`
  - 新增 `ValidateVisibleDialogueParts(...)`。
  - 明确约束“可见对白中的执行承诺”必须和 `actions` 同轮匹配。
- `RimChat.AI.RpgResponseContractGuard`
  - 新增 `ValidateVisibleDialogueParts(...)`。
  - 明确校验 `visible_dialogue` 单行、占位动作负载与 RPG 输出合同。

## 外交历史记录面板高保真重做（v0.9.70）

- `RimChat.UI.Dialog_DiplomacyHistory`
  - 改为单面板结构，不再提供 `当前派系 / 玩家总历史` 视图切换。
  - 历史记录按“当前会话 + 历史会话组”展示。
  - 行交互改为：单击选中、双击编辑、选中后右侧显示删除符号。
- `RimChat.Memory.LeaderMemoryManager`
  - `GetDialogueHistorySessionGroups(Faction faction)`
    - 返回当前派系的会话分组历史，包含当前活会话与切段后的持久化历史会话。
  - `TryUpdateDialogueHistoryRow(Faction faction, DiplomacyHistoryRow row, string newMessage, out string error)`
    - 同时回写当前 `FactionDialogueSession.messages` 与持久化 `DialogueHistory`。
  - `TryDeleteDialogueHistoryRow(Faction faction, DiplomacyHistoryRow row, out string error)`
    - 同时删除当前 `FactionDialogueSession.messages` 与持久化 `DialogueHistory` 对应记录。

## 外交历史记录管理窗口（v0.9.69）

- `RimChat.UI.Dialog_DiplomacyDialogue`
  - `DrawDialogueMainTabs(...)`
    - 顶部 action-tab 新增 `RimChat_DialogueMainTabHistory`，点击后打开独立历史记录窗口。
- `RimChat.UI.Dialog_DiplomacyHistory`
  - 提供 `当前派系 / 玩家总历史` 两种视图。
  - `玩家总历史` 只做聚合展示，不创建新的持久化表。
- `RimChat.Memory.LeaderMemoryManager`
  - `GetDialogueHistoryRows(Faction faction)`
    - 读取当前派系 `DialogueHistory` 并转换为 UI 行模型。
  - `GetAggregatedDialogueHistoryRows()`
    - 聚合全部非玩家派系 `DialogueHistory`，按 `GameTick` 倒序返回。
  - `TryUpdateDialogueHistoryMessage(string factionId, int recordIndex, string newMessage, out string error)`
    - 仅更新目标 `DialogueRecord.Message`，随后立即规范化并持久化。
  - `TryDeleteDialogueHistoryRecord(string factionId, int recordIndex, out string error)`
    - 删除单条 `DialogueHistory` 记录，随后立即规范化并持久化。
- 限制
  - 本次历史记录管理只开放 `DialogueHistory`。
  - 不允许编辑 `IsPlayer`、`GameTick`、所属派系、关系快照、重大事件、外交摘要、RPG 摘要。

## 外交发送入口改名为“快速行动 / Actions”（v0.9.67）

- `RimChat_SendInfoEntry`
  - 中文显示从 `+发送信息` 改为 `快速行动`。
  - 英文显示从 `+Send Info` 改为 `Actions`。
  - 仅改 UI 可见文案，不改入口行为和后续动作链。

## 空投交易 mod 物品兼容根修（v0.9.66）

- `RimChat.DiplomacySystem.ThingDefCatalog`
  - `GetRecords()`
    - mod 物品候选入池规则扩大到“明确可交易 / 明确可用 / 具备有效 item 分类信号”的 def，不再过度依赖原版风格元数据。
  - `TryGetRecordByDefName(string defName, out ThingDefRecord record)`
    - 交易卡绑定解析新增 direct def fallback；缓存 catalog 未收录时，仍可对合法 item def 建立记录。
  - `GetTradeablePaymentRecords()`
    - 提供付款解析的全局可交易 def 视图，用于区分“物品不可解析”和“全局可解析但当前信标没货”。
- `RimChat.DiplomacySystem.ItemAirdropSafetyPolicy`
  - `IsResourceCandidate(ThingDefRecord record)`
    - 资源判定从单一 `stuffProps / 价值 / tradeability` 组合，升级为多信号稳定判定：强资源信号、结构化交易信号、元数据资源信号。
- `RimChat.DiplomacySystem.GameAIInterface.ItemAirdrop.Barter`
  - `BuildPaymentPlan(...)`
    - `payment_items.item` 解析现在优先限定在“当前轨道信标实际库存”。
    - 若某个物品在全局可交易 defs 中能唯一解析、但当前信标无库存，返回 `payment_item_insufficient` 而不是模糊 `payment_item_unresolved`。
  - `TryResolvePaymentThingDef(...)`
    - 改为接收候选记录集，由调用方决定解析范围，避免把库存外相似 mod 物品误判为当前支付目标。
- `RimChat.DiplomacySystem.GameAIInterface.ItemAirdrop.BoundNeed`
  - `TryResolveBoundNeedInfo(...)`
    - 交易卡 `need_def` 绑定改为“先查 catalog，再 direct fallback”，保持强绑定，不允许静默换货。

## `+发送信息` 挑衅 / 请求商队入口（v0.9.64）

- `RimChat.UI.Dialog_DiplomacyDialogue`
  - `OpenSendInfoMenu()`
    - 新增 `Taunt` 与 `Request Caravan` 玩家入口。
  - `BuildChatMessages(...)`
    - 当最后一条会话消息是与当前驱动文本相同的系统消息时，跳过该条历史，避免系统驱动请求重复注入。
- `RimChat.UI.Dialog_DiplomacyDialogue.SendInfoActions`
  - 新增“挑衅”独立窗口，提供 3 个选项：
    - 普通袭击
    - 持续袭击
    - 联合袭击
  - 联合袭击增加二次确认。
  - 提交后不直接强绑动作，只写系统消息并触发现有 AI 回复与动作解析链。
- `RimChat.DiplomacySystem.ApiActionEligibilityService`
  - 移除 `request_caravan`、`request_aid`、`create_quest` 的 projected goodwill floor 校验。
  - 保留关系、冷却、任务模板等真实业务约束。
- `RimChat.Persistence.PromptPersistenceService`
  - 取消提示词层对 projected goodwill floor 的镜像隐藏规则，确保提示词暴露与运行时校验一致。
- 对外接口/本地化
  - 新增中英文键：
    - 发送信息菜单项
    - 挑衅窗口标题与 3 个选项说明
    - 联合袭击确认窗
    - “挑衅”与“请求商队”系统消息模板

## 社交圈原生渲染兼容 fail-fast（v0.9.58）

- `RimChat.Prompting.RimTalkNativeRpgPromptRenderer`
  - `TryRenderWithNativeScriban(...)`
    - 原生 `ScribanParser.Render` 绑定改为多签名探测并缓存命中结果。
    - 若未发现兼容签名，返回结构化兼容失败（不再伪装成功渲染）。
  - `RimTalkNativeRenderDiagnostic`
    - 新增 `BoundMethodVariant`、`IsCompatibilityFailure`、`FailureStage`，用于跨环境定位绑定失败点。
- `RimChat.Persistence.PromptPersistenceService`
  - `BuildUnifiedChannelSystemPrompt(...)`
    - 对 `social_circle_post` 通道启用 fail-fast：原生渲染兼容失败时直接抛出 `RimTalkPromptRenderCompatibilityException`，阻断请求入队。
- `RimChat.DiplomacySystem.GameComponent_DiplomacyManager`
  - `TryQueueNewsSeed(...)`
    - 捕获兼容异常并落盘失败状态，不再发送 AI 请求。
  - `OnSocialNewsRequestSuccess(...)`
    - parse 失败日志新增 `requestId/debugSource/stage/response_preview` 结构化字段。
- `RimChat.AI.AIChatServiceAsync`
  - `ProcessRequestCoroutine(...)`
    - 对 `AIRequestDebugSource.SocialNews` 精确分流：跳过外交对话 Guard（二次沉浸/契约处理），保持严格 JSON 输出链路。
- 对外接口/配置
  - 无新增 public API。
  - 无新增用户配置项。
  - 新增社交圈失败原因键：`RimChat_SocialFailureReason_prompt_render_incompatible`（中/英）。

## Comms Toggle Icon 热路径收敛（v0.9.57）

- `RimChat.Patches.PlaySettingsPatch_CommsToggleIcon`
  - `Postfix(WidgetRow row, bool worldView)`
    - 先执行 fail-fast 门禁，再在单次调用内缓存 `WindowStack` 与已打开窗口引用。
    - 图标绘制与状态提交共用同一份窗口判定结果，避免同帧重复窗口扫描。
  - `DrawToggleButton(...)`
    - 改为接收已判定状态，不再在内部重复读取窗口栈。
  - `ApplyToggleAndPersist(...)`
    - 改为接收调用方传入的 `WindowStack` 与已打开窗口，保持单次提交语义。
  - `GetToggleTooltip(bool enabled)`
    - 新增轻量 tooltip 缓存，仅在开关状态变化时重建 `Translate(...)` 文本。
- 对外接口变更
  - 无新增 public API。
  - 无配置项变更。
  - 无存档结构变更。

## 解析链 fail-fast 根修（v0.9.52）

- `RimChat.AI.AIJsonContentExtractor`
  - `TryExtractPrimaryText(string json)` 返回类型从布尔+`out string`升级为 `PrimaryTextExtractionResult`。
  - `PrimaryTextExtractionResult` 字段：
    - `IsSuccess`：是否提取成功
    - `Content`：提取后的可见文本
    - `ReasonTag`：失败/成功原因标签（如 `ok`、`empty_primary_text`、`no_extractable_text`）
    - `MatchedPath`：命中的解析路径（如 `content[].text`）
  - 新增 `content[]` 片段文本提取能力，覆盖本地模型常见 content-part 回包。

- `RimChat.AI.AIChatServiceAsync`
  - 解析失败分流更新：
    - 仅当 `ReasonTag=empty_primary_text` 时允许一次重试；
    - 其他解析失败直接 fail-fast 触发本地化解析错误回调。
  - 解析失败后不再把 `RimChat_ImmersionFallback_*` 固定句写入会话历史。
  - 重试注入消息新增 `PARSE_MATCH_PATH` 字段，用于提示模型修正输出形态。

- `RimChat.Util.DebugLogger`
  - 新增 `LogParseExtraction(string context, PrimaryTextExtractionResult result)`，用于输出解析取证信息。

## 批量囚犯赎金谈判（v0.9.48）

- `RimChat.UI.Dialog_DiplomacyDialogue.PrisonerRansomSelection`
  - `Dialog_PrisonerRansomTargetSelector`
    - 囚犯选择弹窗由单选改为多选，新增 `全选/全不选/确认`，默认全不选。
  - `BuildRansomBatchExecutionPlan(...)`
    - 当会话存在批量引用且本轮输出 `pay_prisoner_ransom` 时，执行前先做 fail-fast 预校验：
      - 每条动作都必须有 `target_pawn_load_id` + `offer_silver`
      - 目标集合必须与已勾选集合完全一致（无缺失/重复/越权）
      - `offer_silver` 总和必须落在批量总区间内
    - 预校验失败时整批拒绝执行，并返回可读系统错误。
  - `HandleBatchRansomPaymentSuccess(...)`
    - 批量模式按目标粒度消耗待支付集合；批次全部完成后才清空赎金绑定状态。
    - 串行执行中首个失败会中止本轮后续动作，已成功动作不回滚。
- `RimChat.Memory.FactionDialogueSession`
  - 新增批量赎金运行态字段：
    - `hasPendingRansomBatchSelection`
    - `pendingRansomBatchGroupId`
    - `pendingRansomBatchTargetPawnLoadIds`
    - `pendingRansomBatchTotalCurrentAskSilver`
    - `pendingRansomBatchTotalMinOfferSilver`
    - `pendingRansomBatchTotalMaxOfferSilver`
  - 新增方法：
    - `SetPendingRansomBatchSelection(...)`
    - `TryGetPendingRansomBatchSelection(...)`
    - `TryBuildPendingRansomBatchReference(...)`
    - `ConsumePendingRansomBatchTarget(...)`
    - `ClearPendingRansomBatchSelection()`
- `RimChat.DiplomacySystem.GameAIInterface.PrisonerRansom`
  - `PreparePrisonerRansom(...)` 新增读取批量上下文字段（`batch_group_id`/`batch_target_count`）并写入预处理数据。
  - `BuildContract(...)` 新增批量合同元数据落盘：
    - `IsBatchRansom`
    - `BatchGroupId`
    - `BatchTargetCount`
  - 批量合同放人时限固定放宽至 `1.5x`（中度宽松）。
- `RimChat.DiplomacySystem.RansomContractManager`
  - 批量合同惩罚分支：
    - 普通跌价/超时惩罚按 `0.7x` 缩放
    - major/severe 跌价阈值上调
  - 器官新增缺失惩罚保持单人强度（不应用批量缩放系数）。

## 通讯台替换直开回归根修（v0.9.47）

- `RimChat.Patches.CommsConsolePatch`
  - `GetFloatMenuOptionsPostfix(...)`
    - 接管判定更新：不再依赖原版 action 的声明类型/程序集名匹配。
    - 新判定链路：`菜单项非空 -> action 非空 -> 可解析有效派系 -> 替换 action 为 RimChat 直开`。
    - fail-fast 跳过原因标准化：`NullOption / NullAction / InvalidFaction`。
- 对外接口变更
  - 无新增 public API。
  - 无配置项变更。
  - 无存档结构变更。

## 外交主动节流加强与恢复不补发（v0.9.46）

- `RimChat.NpcDialogue.GameComponent_NpcDialoguePushManager`
  - `CancelQueuedTriggersForFaction(Faction faction, string reason = "manual")`
    - 返回值：`int`（实际清理条数）。
    - 行为：用于在在线恢复等场景下清理派系历史主动队列，并可携带日志 reason。
  - 节流参数读取改为配置化：
    - `NpcGlobalDeliveryCooldownHours`（默认 `6`）
    - `NpcFactionCooldownMinDays`（默认 `3`）
    - `NpcFactionCooldownMaxDays`（默认 `7`）
    - `NpcQueueMaxPerFaction`（默认 `3`）
    - `NpcQueueExpireHours`（默认 `12`）
  - 轮询候选优化：
    - 新增活跃候选缓存 + 低频会话同步 + 低频清理，`EvaluateRegularTriggers(...)` 不再每次重建全量候选集。
  - 调试日志：
    - 新增 `EnableNpcPushThrottleDebugLog` 门控下的节流命中日志（全局冷却/派系冷却/队列清理）。

- `RimChat.DiplomacySystem.GameComponent_DiplomacyManager`
  - `ForcePresenceOnlineForNpcInitiated(Faction faction)`
    - 行为：若状态发生 `Unavailable -> Online`，触发外交主动历史队列清理。
  - `RefreshPresenceOnDialogueOpen(Faction faction)`
    - 行为：同样在 `Unavailable -> Online` 边沿恢复时触发清队列，保证“恢复在线不补发历史触发”。

- `RimChat.Config.RimChatSettings`
  - 新增持久化字段：
    - `NpcGlobalDeliveryCooldownHours`
    - `NpcFactionCooldownMinDays`
    - `NpcFactionCooldownMaxDays`
    - `EnableNpcPushThrottleDebugLog`
    - `NpcPushThrottleProfileVersion`
  - 迁移策略：
    - 旧存档首次加载会强制迁移到节流默认档位（`6h / 3~7d / 3 / 12h`）。

## 玩家手动社交圈发帖 + 派系强制主动回应（v0.9.44）

- `RimChat.DiplomacySystem.GameComponent_DiplomacyManager`
  - `TryPublishManualPlayerSocialPost(string title, string body)`
    - 直接创建玩家原文公开帖子，不经过 AI 新闻生成。
    - fail-fast 校验标题/正文为空和长度超限。
    - 返回 `ManualSocialPostResult`：
      - `Success`
      - `PostId`
      - `TriggeredFactionCount`
      - `FailureReason`
  - `GetManualSocialPostFailureReasonLabel(ManualSocialPostFailureReason reason)`
    - 统一返回面向 UI 的本地化失败原因文本。
- `RimChat.DiplomacySystem.SocialEnums`
  - 新增 `SocialNewsOriginType.PlayerManual`
    - 标记玩家手动发布的社交圈帖子，区别于 AI 生成新闻。
  - 新增 `ManualSocialPostFailureReason`
    - 失败原因：`Disabled / MissingTitle / MissingBody / TitleTooLong / BodyTooLong / Unknown`。
  - 新增 `ManualSocialPostResult`
    - 供 UI 读取发帖结果与实际触发派系数。
- `RimChat.NpcDialogue.GameComponent_NpcDialoguePushManager`
  - `manual_social_post` 自定义触发上下文会在主动对话生成阶段注入：
    - 帖子标题
    - 帖子正文
    - “这是玩家公开社交圈发帖”的语境说明
  - 目标是让主动消息直接回应帖文内容，而不是回退成普通闲聊。
- 交付行为
  - 手动帖子不会进入 AI 新闻请求队列。
  - 手动帖子不会再额外发送一封“社交圈世界新闻”来信。
  - 相关派系的主动回应继续复用原有 `ChoiceLetter_NpcInitiatedDialogue` 和会话写入链路。

## 联合袭击专属音效全链路移除（v0.9.42）

- `RimChat.AI.AIActionExecutor`
  - `ExecuteRequestRaidCallEveryone(...)`
    - 联合袭击调度成功后不再播放专属音效，只保留动作成功本身的文本/系统反馈。
- `1.6/Defs/SoundDefs/Diplomacy_Sounds.xml`
  - 删除 `RimChat_RequestRaidCallEveryone`，不再保留联合袭击专属 `SoundDef`。
- `build.ps1`
  - 移除 `sound_request_raid_call_everyone` 的构建期 fail-fast 音频校验，不再要求该资源存在。
- 资源
  - 删除 `1.6/Sounds/sound_request_raid_call_everyone.wav`。

## 轨道商订单任务禁用根修（v0.9.42）

- `RimChat.DiplomacySystem.ApiActionEligibilityService`
  - 新增轨道商会话识别：优先读取动作参数中的显式上下文字段，缺失时回退到当前地图 `TradeShip` 检测。
  - `TradeRequest` 在轨道商上下文下改为 fail-fast 阻断，返回 `orbital_trader_trade_request_disabled`，并统一使用本地化提示“轨道商无法发布地面据点履约订单，请改用空投交易”。
  - `GetQuestEligibilityReport(...)` / `GetAvailableQuestDefsForFaction(...)` / `ValidateCreateQuest(...)` 统一接入同一判定，确保提示词可用列表、执行校验与失败回显一致。
- `RimChat.Persistence.PromptPersistenceService`
  - `AppendDynamicQuestGuidance(...)` 在轨道商通信时追加专属上下文说明，并从可用任务列表层面移除据点交货类订单任务。
  - `AppendQuestSelectionHardRules(...)` 与 `AppendOutputSpecificationAuthorityRules(...)` 新增轨道商硬约束：禁止承诺把指定物资带入地面据点完成订单，相关需求只能解释限制并引导到 `request_item_airdrop`。
- `RimChat.AI.AIActionExecutor`
  - `create_quest` 校验失败时改用同一上下文的可用任务列表，避免轨道商场景下失败提示又把 `TradeRequest` 列回 allowed quest。
- `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
  - 默认外交提示词补充轨道商规则：轨道商涉及具体物资交换时只允许引导或执行 `request_item_airdrop`，禁止生成需要地面据点履约的订单/交货任务。
- 本地化
  - 新增 `RimChat_OrbitalTraderTradeRequestBlocked` 中英语言键，用于前台 fail-fast 提示与统一文案。

## 联合袭击动作目录语义澄清 + 默认提示层去 blocked 化（v0.9.41）

- `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
  - `request_raid_call_everyone`
    - 动作语义改为“公开摇人总攻”，明确这是跨派系联合袭击，不是普通袭击的别名。
    - 明确 `call everyone / 联合袭击 / 都叫来 / 全都叫来 / everyone attack / all in` 属于玩家主动要求发动联合总攻的口令。
  - `request_raid_waves`
    - 文案改为“持续施压的多波次进攻”，不再把它描述成联合袭击不可用时的默认替代。
- `RimChat.Config.PromptTextConstants`
  - `RequestRaidCallEveryoneActionDescription`
  - `RequestRaidCallEveryoneActionRequirement`
  - `RequestRaidWavesActionDescription`
  - `RequestRaidWavesActionRequirement`
    - 统一默认 JSON 与运行时补齐文案，避免动作目录语义漂移。
- `RimChat.Persistence.PromptPersistenceService`
  - `AppendBlockedActionHints(...)`
    - 提示词拼装阶段隐藏 `call_everyone_requires_post_raid_escalation`，不再把它作为默认 blocked action 暴露给模型。
    - 全局冷却、无可用派系等硬阻断仍继续进入 blocked actions。
- 兼容说明
  - `ApiActionEligibilityService.ValidateRaidCallEveryoneAvailability(...)` 的真实资格判定未放开；本次仅调整提示词呈现层，不改变执行层规则。

## 联合袭击动作说明补全 + 别名映射 + 成功音效（v0.9.40）

- `RimChat.AI.AIResponseParser`
  - `NormalizeActionName(...)`
    - 新增 `联合袭击 / 一起上 / 都叫来 / 全都叫来 / everyone_attack / all_in` 到 `request_raid_call_everyone` 的归一化映射。
- `RimChat.AI.AIActionExecutor`
  - `ExecuteRequestRaidCallEveryone(...)`
    - 当时曾在 `DiplomacyEventManager.ScheduleRaidCallEveryone(...)` 成功后播放联合袭击专属音效；该音效链已在 `v0.9.42` 全链路移除。
- `RimChat.Config.PromptTextConstants`
  - 补全 `request_raid_call_everyone` 的动作解释，明确使用时机、用途和玩家口语别名。
- `RimChat.Persistence.PromptPersistenceService.DomainStorage`
  - 运行时补齐动作目录时，同步写入新的 `request_raid_call_everyone` 说明与挑战短语解释。

## 提示词去重 + 种族强制注入 + 事件双层压缩（v0.9.39）

- `PromptPersistenceService.WorkbenchComposer.ComposePromptWorkspace(...)`
  - 运行时新增 `mandatory_race_profile` 补充块注入（diplomacy/rpg/proactive）。
- `PromptPersistenceService.WorkbenchComposer.ValidateRuntimePromptComposition(...)`
  - 新增 `mandatory_race_profile` 必需校验，缺失时 `PromptRenderException` fail-fast。
- `PromptPersistenceService.WorkbenchComposer.BuildPromptNodePlacementsForCompose(...)`
  - 外交通道在 `instruction_stack.faction_characteristics` 存在时抑制 `diplomacy_fallback_role` 重复节点输出。
- `PromptPersistenceService.TemplateVariables`
  - `world.environment_params` 输出改为紧凑快照文本（引用 `<environment>` 作为详情权威块）。
  - `world.recent_world_events` 输出改为紧凑摘要，调用 `BuildRecentWorldEventIntelCompactDigest(...)`。
- `PromptPersistenceService.AppendRecentWorldEventIntel(...)`
  - 事件注入由“仅截断”升级为“原文 + EventDigest 摘要”双层输出（预算溢出时追加摘要）。

## 外交通道固定情报注入与交易常识收口（v0.9.37）

- `PromptPersistenceService.AppendOutputSpecificationAuthorityRules(...)`
  - 外交通道运行时强制注入交易常识：
    - 即时物资交换只允许 `request_item_airdrop`；
    - 单次空投交易只允许一种换一种（`need` 对应一组 `payment_items`）；
    - 商队（`request_caravan`）是延时交易，派系无法控制其最终携带的物资种类与数量；
    - 玩家准确命中已知交易事实时，允许在成本边界内让价/打折。
- `PromptPersistenceService.Hierarchical.ResolveFactionPromptText(...)`
  - 派系提示词渲染后固定追加 `FIXED_FACTION_INTEL` 结构化块，且不依赖工作台模板内容。
  - 注入范围：`diplomacy_dialogue`、`proactive_diplomacy_dialogue`、`diplomacy_strategy`。
- `PromptPersistenceService.Hierarchical.BuildDiplomacyStrategySystemPromptHierarchical(...)`
  - 策略通道新增 `instruction_stack.faction_characteristics` 节点，确保策略链路稳定包含派系提示词与固定情报块。
- `DiplomacyFactionFixedIntelBuilder.Build(...)`
  - 固定字段：
    - `FactionDescription`
    - `FactionTechLevel`
    - `HasFactionCaravanDispatchedNow`
    - `HasFactionQuestPublishedNow`
    - `HasFactionRaidScheduledNow`
    - `HasPlayerExpeditionNow`
    - `FactionSettlementDestroyedHistory`
    - `FactionRaidImpactOnPlayerLatest`
    - `FactionRaidImpactOnPlayerTotal`
    - `FactionRaidCasualtiesLatest`
    - `FactionRaidCasualtiesTotal`
    - `PlayerFactionTechLevel`
- `GameComponent_DiplomacyManager.EventQueries`
  - 新增只读判定接口：
    - `HasCaravanDispatchedNow(Faction faction)`
    - `HasRaidScheduledNow(Faction faction)`
- `GameAIInterface.QuestTracking`
  - 新增 RimChat 任务发布追踪持久化：
    - `ExposeQuestPublicationData()`
    - `TryTrackCreateQuestResult(...)`
    - `HasActiveRimChatQuestForFaction(Faction faction)`
- `FactionIntelLedgerComponent`
  - 新增据点摧毁历史与袭击破坏账本持久化：
    - `RecordSettlementDestroyed(WorldObject worldObject)`
    - `NotifyBuildingDestroyed(Thing building, DamageInfo? dinfo)`
    - `GetSettlementDestructionRecords(Faction ownerFaction)`
    - `GetRaidDamageRecordsForAttacker(Faction attackerFaction)`

## 空投确认数量丢失根修（v0.9.29）

- `Dialog_DiplomacyDialogue.ItemAirdropConfirmation.TryInjectPendingAirdropCountFromLatestPlayerMessage(...)`
  - 注入优先级改为：
    - 1) `FactionDialogueSession.pendingAirdropTradeCardRequestedCount`
    - 2) 最新玩家消息文本解析（仅当前者缺失时）
  - 行为约束：
    - 当 action 参数已包含 `count/quantity` 时，不覆盖显式数量。
  - 预期：
    - 玩家在确认环节仅输入“同意/确认”时，执行数量仍保持交易卡绑定数量，不再回退默认家族数量。

## 空投确认重入卡死根修（v0.9.28）

- `GameAIInterface.ItemAirdrop.Async.TryBuildAirdropAsyncContext(...)`
  - 新增异步上下文强制选择参数透传：
    - 从 action 参数读取 `selected_def`。
    - 回填 `ItemAirdropAsyncPrepareContext.ForcedSelectedDef`，供异步选择阶段优先执行 `TryBuildForcedSelection(...)`。
  - 新增绑定语义同步：
    - `HasBoundNeed`：`__airdrop_bound_need_def` 是否存在。
    - `HadForcedSelectionConflict`：`selected_def` 与 `bound_need_def` 不一致时置位，最终以 `bound_need_def` 为准。
  - 预期：`selection_manual_choice` 不再在确认自动回填后重复重入。
- `ItemAirdropSafetyPolicy.IsResourceCandidate(...)`
  - 诊断日志由“逐条无门槛输出”改为：
    - 非 `Prefs.DevMode` 不输出；
    - DevMode 下按窗口限频输出（防日志洪泛）。
  - 预期：候选扫描阶段不再因日志刷写导致 `Player.log` 快速膨胀并放大卡顿。

## 空投绑定需求仲裁回归根修（v0.9.27）

- `ItemAirdropSafetyPolicy.IsResourceCandidate(...)`
  - 恢复“强资源信号优先”顺序：
    - `ThingCategory.Item + stuffProps != null + 非食物/药物/服装` 时直接判定为资源候选。
    - 然后再应用包含 `IsWeapon` 在内的通用排除逻辑。
  - 预期：`WoodLog` 等原材料不再因噪声 `IsWeapon` 元数据触发资源家族误判。
- `GameAIInterface.TryApplyBoundNeedArbitration(...)`
  - 绑定优先策略收口：
    - 当 `bound_need_def` 可解析时，若文本 `intent.Family` 与绑定物资不一致，不再返回 `bound_need_family_conflict` 阻断。
    - 系统记录 `bound_need_family_conflict_overridden` 审计码并继续按绑定物资执行。
  - fail-fast 边界保持不变：
    - 仅 `bound_need_def` 无法解析时，继续以 `bound_need_unresolved` 阻断交易。
- 本地化键新增：
  - `RimChat_ItemAirdropBoundNeedFamilyConflictOverrideAudit`（EN/ZH）

## `call_everyone/waves` 战斗主动消息直通（v0.9.26）

## `request_raid_call_everyone` 友中立援军根修（v0.9.30）

- `DiplomacyEventManager.ScheduleRaidCallEveryone(...)`
  - 到达时机统一为 16-36 小时随机窗口：
    - Hostile -> `CallEveryoneActionKind.Raid`
    - Friendly/Neutral -> `CallEveryoneActionKind.MilitaryAidCustom`
  - 取消友中立“立即执行”分支。
- `DelayedDiplomacyEvent.ExecuteRaidCallEveryoneEvent(...)`
  - 新增 `MilitaryAidCustom` 执行链路：
    - fail-fast 校验 map/faction/Combat 生成模板；
    - 生成 combat pawn 组并进图；
    - 使用 `LordJob_AssistColony` 组建援军行为。
  - 不再依赖 `FriendlyRaid/RaidFriendly` incident 可执行性。
- `GameComponent_DiplomacyManager.ProcessDelayedEvents(...)`
  - `RaidCallEveryone` 失败策略改为 no-retry：失败后直接丢弃并记录日志。
- 旧存档迁移（`PostLoadInit`）
  - 未执行 `RaidCallEveryone` 自动重排到 16-36 小时窗口；
  - 友中立动作迁移到 `MilitaryAidCustom`；
  - 清空历史重试状态（`MaxRetryCount/RetryCount/NextRetryTick`）。

## `request_raid_call_everyone` 社交圈强制双发（v0.9.33）

- `DiplomacyEventManager.ScheduleRaidCallEveryone(...)`
  - 联合袭击调度成功后，立即强制发起派系发布一篇社交圈（军事分类，负向情绪）。
  - 同时排入一个 `RaidCallEveryoneSocialPost` 延迟事件，执行时间为 36 小时后。
- `DelayedDiplomacyEvent`
  - 新增事件类型 `RaidCallEveryoneSocialPost`。
  - 执行时调用 `TryEnqueueRaidCallEveryoneSocialPost(..., isFollowup:true)` 发布 36 小时跟进社交圈。
- 本地化键（中英）：
  - `RimChat_RaidCallEveryoneSocialPostImmediate`
  - `RimChat_RaidCallEveryoneSocialPostFollowup`

## `request_raid_call_everyone` 窗口/参与裁剪/事件跳转增强（v0.9.32）

- `DiplomacyEventManager.ScheduleRaidCallEveryone(...)`
  - 联合袭击到达窗口由 `16-36h` 调整为 `16-30h`。
  - 新增参与裁剪：当敌对派系数量 `<=` 友好/中立数量时，按 `PlayerGoodwill` 升序逐个剔除友中立，直到敌对数量 `>` 友好/中立数量。
  - 调度通知 `detail` 改为 `hostile|friendly|16|30`。
- `GameComponent_DiplomacyManager.MigrateLegacyRaidCallEveryoneEvents(...)`
  - 旧事件重排窗口同步改为 `16-30h`。
- `DiplomacyEventManager.SendAidLetter(...)`
  - 信件 `lookTargets` 从 `null` 改为玩家家园地图中心 `LookTargets`，援军到达信件可使用原版“转到事件发送地点”按钮。
- `PromptTextConstants.RequestRaidCallEveryoneActionDescription`
  - 动作说明同步到 `16-30h` + “按好感度裁剪友中立参与者”。

## `request_raid_call_everyone` 援军上图坐标根修（v0.9.31）

- `DiplomacyEventManager.TryArriveCallEveryoneAidPawns(...)`
  - 移除 `arrivalMode.Worker.Arrive(...)` 的隐式落点路径，避免出现 `IntVec3.Invalid (-1000,-1000,-1000)` 越界坐标。
  - 新流程：
    - 先用 `CellFinder.TryFindRandomEdgeCellWith(...)` 找合法入场边缘格；
    - 失败时回退 `DropCellFinder.TradeDropSpot(map)`；
    - 对每个 pawn 使用 `CellFinder.TryFindRandomSpawnCellForPawnNear(...)` + `GenSpawn.Spawn(...)` 显式上图。
  - 上图后创建 `LordJob_AssistColony`；若最终 0 人上图，fail-fast 返回并记录 `entry/attempted/spawnFailed` 审计信息。

## `call_everyone/waves` 战斗主动消息直通（v0.9.26）

- `DiplomacyEventManager.ScheduleRaidCallEveryone(...)`
  - 调度阶段按关系写入执行意图：
    - Hostile -> `CallEveryoneActionKind.Raid`
    - Friendly/Neutral -> `CallEveryoneActionKind.MilitaryAidVanilla`
- `DelayedDiplomacyEvent.ExecuteRaidCallEveryoneEvent(...)`
  - 按执行意图执行：
    - `Raid` -> `TriggerRaidEvent(...)`
    - `MilitaryAidVanilla` -> `TriggerMilitaryAidEvent(...)` (vanilla `FriendlyRaid`)
  - 成功后立即触发 arrival 主动消息，并排入 departure 监控事件。
- `DelayedDiplomacyEvent` 战斗会话字段
  - 新增 `ParticipantPawnThingIds`、`TriggerWaveEndAfterDeparture`、`CallEveryoneActionKindInt`。
  - `RaidDepartureMessage` 改为“参战 pawn 仍在玩家家园地图上则重试”，直到真实离场/结束后才发送 departure 消息。
  - `RaidWaveEndMessage` 改为由最终波 departure 成功后触发，不再使用固定延迟估算。
- `NpcDialogueTriggerContext` / `QueuedNpcDialogueTrigger`
  - 新增并持久化 bypass 字段：
    - `BypassRateLimit`
    - `BypassCategoryGate`
    - `BypassPlayerBusyGate`
- `GameComponent_NpcDialoguePushManager`
  - `HandleTriggerContext(...)` 对 `WarningThreat` 的过滤改为“默认拦截 + bypass 放行”。
  - faction/global cooldown、reinitiate cooldown、busy gate 对 `BypassRateLimit` 触发器不生效。
  - 战斗消息 AI 生成失败或空输出时，fail-fast 回退到上下文 `Reason` 文案立即投递。

## `raid_call_everyone` 延迟事件根修（v0.9.25）

- `GameComponent_DiplomacyManager.ProcessDelayedEvents()`
  - 延迟事件处理从“直接遍历原列表”改为“快照遍历 + 延迟合并”。
  - 新增 tick 级防重入，避免同 tick 重复进入处理链路。
  - `AddDelayedEvent(...)` 在处理期间改为写入待合并队列，处理结束后统一落回主队列。
- `DelayedDiplomacyEvent.ExecuteRaidCallEveryoneEvent()`
  - 执行语义收敛为 raid-only：所有目标派系统一走 `DiplomacyEventManager.TriggerRaidEvent(...)`。
  - `raid_call_everyone` 路径不再调用军事援助执行分支，避免 `FriendlyRaid` 缺失时的无效 Def 查找噪音。

## 通讯台原版来源判据收敛（v0.9.24）

- `CommsConsolePatch.GetFloatMenuOptionsPostfix(...)`
  - 拦截前置为 fail-fast：
    - `option == null` 或 `option.action == null` 直接放行；
    - `IsVanillaCommsAction(option.action)` 不成立直接放行；
    - 仅对“原版通讯 action 来源 + 有效派系”执行 action 替换。
- `CommsConsolePatch.IsVanillaCommsAction(Action action)`
  - 通过 `Method/DeclaringType/Assembly` 静态识别 `Assembly-CSharp` 下 `Building_CommsConsole` 来源，避免误接管第三方 mod 菜单项。
- `CommsConsolePatch.ExtractFactionFromOption(...)`
  - 只保留原版通讯上下文可控链路：
    - action 闭包提取 `Faction`；
    - `console.GetCommTargets(myPawn)` + label 匹配。
  - 移除全派系列表 label 模糊回退，降低跨 mod 误命中概率。
- 诊断日志：
  - 新增放行原因日志（带节流）：`Comms option bypassed: reason=NullOption|NullAction|NonVanillaAction|InvalidFaction`。

## 空投聊天卡配色回调与截断修复（v0.9.23）

- `Dialog_DiplomacyDialogue.ImageRendering`
  - 灰色内容层文字颜色改为固定高对比方案，不再复用会在绿色外层气泡里显得发闷的颜色。
  - 物资名、`defName`、指标行的高度预算进一步放宽，并同步抬升指标区高度。

## 空投聊天卡原气泡回调与灰色内容层（v0.9.22）

- `Dialog_DiplomacyDialogue.ImageRendering`
  - 空投卡外层恢复 `PlayerBubbleColor / AIBubbleColor`。
  - 需求卡与出价卡内容层单独绘制灰色底板，不再整张卡统一灰底。
  - 物资名和指标行文本高度预算上调，以修复过度紧凑带来的裁切。

## 空投聊天卡标题层回调（v0.9.21）

- `Dialog_DiplomacyDialogue.ImageRendering`
  - 空投卡标题行高度与上下留白略微上调，确保极简灰底布局下标题仍然稳定可见。
  - 其余极简灰底布局保持不变。

## 空投聊天卡极简灰底收敛（v0.9.20）

- `Dialog_DiplomacyDialogue.ImageRendering`
  - 空投卡气泡底色统一为单层灰底，不再按玩家/AI 套用彩色底板。
  - 标题带、卡内块底与底栏强调块移除，改为文本 + 分隔线式布局。
  - 缩略图、标题区、指标区常量再次收紧，进一步降低聊天卡总高度。

## 空投聊天卡紧凑化与入口冷却阻断（v0.9.19）

- `Dialog_DiplomacyDialogue.ImageRendering`
  - 空投卡高度预算改为基于文本真实占用、缩略图尺寸和底部指标区动态计算，避免长名称/长 `defName` 压住指标块。
  - `defName` 现在固定按独立单行裁剪显示，名称区和指标区不再互相覆盖。
- `Dialog_DiplomacyDialogue.OpenSendInfoMenu()`
  - 现在预先调用 `ApiActionEligibilityService.ValidateActionExecution(faction, "request_item_airdrop", null)` 构造空投菜单项。
  - 命中 `airdrop_cooldown` 时返回禁用菜单项，并直接复用现有本地化剩余游戏内时间文案。
- `Dialog_DiplomacyDialogue.TryStartManualAirdropTradeSend()`
  - 在真正开卡前再次复用同一份 eligibility 校验，避免 UI 入口与动作阶段冷却判断分叉。

## 空投卡视觉与 Presence 状态重置修正（v0.9.18）

- `Dialog_DiplomacyDialogue.AddAIResponseToSession(...)`
  - 移除 `hadPendingAirdropTradeCardReference && !hasAirdropAction` 分支里的 `RimChat_AirdropTradeCardIgnoredSystem` 系统消息注入。
  - 保留原有空投绑定引用、动作执行、失败处理与 presence fallback 流程不变。
- `Dialog_DiplomacyDialogue.ImageRendering`
  - `CalculateAirdropTradeCardBubbleHeight(...)` 改为按需求卡/出价卡内容动态计算高度。
  - `DrawAirdropTradeCardBubble(...)` / `DrawAirdropItemCard(...)` 重画为沉浸式终端单据样式，未改动消息数据契约。
- `FactionPresenceState`
  - 新增持久化字段 `doNotDisturbUntilTick`，默认 `0`，旧存档自动兼容。
- `GameComponent_DiplomacyManager.ApplyPresenceAction(...)`
  - `go_offline` 仍沿用离线到期逻辑，并会清除 DND 到期。
  - `set_dnd` 现写入独立 `doNotDisturbUntilTick = currentTick + 3 * GenDate.TicksPerDay`。
- `GameComponent_DiplomacyManager.RefreshPresenceOnDialogueOpen(...)`
  - 优先检查 `forcedOfflineUntilTick` 与 `doNotDisturbUntilTick` 是否仍有效。
  - 两者到期后清空对应运行态字段，并重新走 `EvaluateScheduledPresence(...)` 排班重算。
- `Dialog_DiplomacyDialogue.ActionHint`
  - `airdrop_cooldown` 专用显示改为把 `RemainingSeconds` 还原为游戏 ticks，再输出游戏内剩余时长文案。

## 空投重报价沉浸式解析升级（v0.9.17）

- `TryCaptureAndCacheAirdropCounteroffer(...)` 现在支持三种输入：
  - 旧版固定模板：`重报价: item=... count=... silver=... reason=...`
  - 中文自然句：例如“这批原木我最多给你 50 单位，价码是 400 银，因为库存紧。”
  - 英文自然句：例如“WoodLog, we can spare 50 units for 400 silver because stock is tight.”
- 解析回退规则：
  - 若自然句中缺少 `item`，优先从当前交易卡绑定 `NeedDefName` 回填。
  - `reason` 改为从 `因为/原因/due to/because/since` 等自然提示语中提取。
- `Dialog_ItemAirdropTradeCard.ApplyCounterofferDefaults()` 现在同时回填：
  - `requestedCountText <- lastAirdropCounterofferCount`
  - `offerCountText <- lastAirdropCounterofferSilver`

## 空投确认数量来源纠偏（v0.9.16）

- `TryInjectPendingAirdropCountFromLatestPlayerMessage(...)` 现在采用如下优先级：
  - 1. `FactionDialogueSession.pendingAirdropTradeCardRequestedCount`
  - 2. 文本中的结构化“需求 xN / need xN”
  - 3. 旧的纯数字回退提取
- 预期效果：
  - 交易卡文本如“需求 原木 x50，出价 白银 x400”时，执行层注入的 `count` 固定为 `50`，不再错误取 `400`。

## 原木资源资格误判修复（v0.9.15）

- `ItemAirdropSafetyPolicy.IsResourceCandidate(...)` 的资源资格判定顺序调整：
  - 若 `ThingDef` 具备明确资源信号（当前为 `stuffProps != null`，且非食物/药物/服装），优先判定为资源。
  - 然后才应用通用 `IsWeapon` 排除。
- 预期效果：
  - `WoodLog` 这类带有噪声 `IsWeapon` 标记的原材料，仍可在 `ItemAirdropNeedFamily.Resource` 下通过绑定需求仲裁。

## 空投交易卡绑定物品状态贯通根修（v0.9.14）

- `request_item_airdrop` 的内部执行约束新增：
  - 若当前对话仍持有空投交易卡绑定的 `need_def`，则所有后续确认、延迟意图映射和最终执行都必须带上同一组 bound need 元数据。
  - 若玩家显式进行候选改选，系统会先清除交易卡绑定，再允许新的 `selected_def` 生效。
  - 若异步准备得到的 `preparedTrade.SelectedDefName` 与 bound need 不一致，接口直接 fail-fast，返回 `bound_need_prepared_mismatch`。
- 新增本地化失败文案键：
  - `RimChat_ItemAirdropBoundNeedStateLostSystem`
  - `RimChat_ItemAirdropBoundNeedPreparedMismatchSystem`

## 空投绑定需求仲裁根修（v0.9.13）

- 新增内部参数键：
  - `__airdrop_bound_need_def`
  - `__airdrop_bound_need_label`
  - `__airdrop_bound_need_search_text`
  - `__airdrop_bound_need_source`
  - `__airdrop_bound_need_conflict_code`
  - `__airdrop_bound_need_conflict_message`
- `FactionDialogueSession.SetPendingAirdropTradeCardReference(...)` 现在同时持有：
  - 原始 `need`
  - 绑定 `NeedDefName`
  - 绑定 `NeedLabel`
  - 绑定 `NeedSearchText`
- `[AirdropTradeCardReference]` 内部参考块新增字段：
  - `need_def`
  - `need_label`
  - `need_search_text`
- 执行语义收口：
  - 若存在绑定需求元数据，执行层在准备阶段先做绑定需求仲裁。
  - 候选池与绑定需求冲突时，系统把绑定物资注入候选池并重建选择。
  - 绑定需求无法解析或与需求家族冲突时，直接 fail-fast，不进入确认弹窗。

## 空投请求 UI/超时/匹配链路重构（v0.9.12）

- `ItemAirdropTradeCardPayload` 新增结构化价格字段：
  - `NeedUnitPrice`
  - `NeedReferenceTotalPrice`
  - `OfferUnitPrice`
  - `OfferTotalPrice`
- 空投卡提交契约收口：
  - `NeedDefName` 现在是空投卡提交必需字段。
  - `GetNeedReferenceText()` 优先输出 `NeedDefName`，用于注入内部参考块，避免自由文本漂移。
- `DialogueMessageData` 的空投卡消息结构新增：
  - `airdropNeedUnitPrice`
  - `airdropNeedReferenceTotalPrice`
  - `airdropOfferUnitPrice`
  - `airdropOfferTotalPrice`
- 二阶段超时语义调整：
  - 仅 `selection_timeout/queue_timeout` 允许自动取 `Options[0]`。
  - 自动取 Top1 后直接继续准备最终确认，不再缓存 `pendingDelayedActionIntent`，也不再要求玩家回复 `1/2/3/...`。
  - 非超时类 pending 仍返回明确失败，不做自动成交。
- 统一匹配入口：
  - 新增 `ThingDefMatchEngine.cs`，提供 `ThingDefMatchRequest / ThingDefMatchCandidate / ThingDefMatchResult`。
  - 搜索建议、支付解析、候选默认排序统一使用 exact `defName` > exact `label` > normalized exact > alias > token full cover > search text > semantic tokens > near match 的评分顺序。

## 空投请求卡重构 - 结构化选品 + 结构化报价（v0.9.10）

- `ItemAirdropTradeCardPayload` 新增需求侧精确字段：`NeedDefName`、`NeedLabel`、`NeedSearchText`。
- `Dialog_ItemAirdropTradeCard` 引入本地搜索状态 `SearchStateManager`，管理防抖（180ms）、候选计算（默认6个）和精确绑定。
- 搜索流程：
  - 输入后本地防抖 180ms。
  - 仅在查询文本归一化后发生变化时重算候选。
  - 候选排序优先 exact `defName` / exact `label` / 强匹配 token，再走现有 resolver。
  - 选中建议后立即建立结构化绑定，清空建议列表。
  - 玩家改词且不再精确命中绑定时，立即清空绑定。
- 二次回填逻辑：
  - 优先按 `lastAirdropCounterofferDefName` 解析成 `ThingDef`，建立结构化需求物资卡。
  - `count` 回填到需求数量，`silver` 回填到出价数量。
  - 只有在能精确解析 `ThingDef` 时才给搜索框显示规范名；解析失败时只保留数量回填。
- 信息卡布局（左右布局）：
  - 顶部：需求物资搜索框。
  - 搜索框下方：建议下拉区，仅在有候选时展示。
  - 中部：左侧需求物资卡（缩略图、名称、`defName`、市场价、堆叠上限），右侧出价物资卡。
  - 底部：`需求物资数量` / `出价物资数量` / 提交/取消按钮。
- 结构化提交链路：
  - `ItemAirdropTradeCardPayload` 携带原始需求文本和精确需求字段。
  - `FactionDialogueSession.SetPendingAirdropTradeCardReference` / `TryBuildPendingAirdropTradeCardReference` 扩展为同时注入原始需求文本和精确需求字段。
  - 保留现有 `need/count/payment_items/scenario`，新增需求精确字段时不破坏旧链路。

## 空投信息卡可用性修复（v0.9.9）

- UI 语义收口为“需求输入 + 以物易物库存选择”，移除候选/刷新/搜索三类易误导交互。
- 提交载荷保持参考报价语义：`need` + `count` + `payment_items[{item,count}]`，其中 `payment_items` 来源于信标库存选择。
- 新增空状态文案：无可交易信标库存时直接提示，不再展示空白可点列表。

## 空投信息卡“AI主导议价”落地（v0.9.8）

- `Dialog_DiplomacyDialogue.OnAirdropTradeCardSubmitted` 改为发送自然语言摘要，不再把结构化交易块直接写入聊天记录。
- 空投信息卡结构字段改为通过 `FactionDialogueSession` 运行态上下文内部注入（`TryBuildPendingAirdropTradeCardReference`），仅供本轮 AI 参考，不作为强制执行源。
- `Dialog_DiplomacyDialogue.AddAIResponseToSession` 新增固定句式重报价解析并写入会话缓存（`CacheAirdropCounteroffer`）。
- 当信息卡发起轮次中 AI 未返回 `request_item_airdrop` 动作时，系统注入提示：`RimChat_AirdropTradeCardIgnoredSystem`，且不自动补动作执行。
- `Dialog_ItemAirdropTradeCard` 右侧库存改为“通电轨道信标可达物资+数量”；开卡时优先读取最近一次 AI 重报价做默认回填。
- `Dialog_DiplomacyDialogue.ActionHint` 受限原因映射改为：code 本地化优先 -> `validation.Message` -> 通用文案；所有 `*_cooldown` 统一时间格式化。
- Prompt 契约更新：信息卡字段为参考报价，AI 可拒绝/重报价/改参数执行；重报价固定句式：
  - `重报价: item=<defName> count=<int> silver=<int> reason=<text>`
  - `Counteroffer: item=<defName> count=<int> silver=<int> reason=<text>`

## 启动期 Harmony 补丁参数规范与自检（v0.9.6）

- 影响范围：启动期关键补丁注入链路（`RimChatMod` -> `Harmony.PatchAll`）。
- 参数规范：
  - 关键补丁统一采用位置参数风格（`__0/__1/...`）或严格与原方法参数名对齐。
  - `Translator.TryTranslate` 回退补丁已切换为 `__0/__1`。
- 启动期自检：
  - 新增 `HarmonyPatchStartupSelfCheck.Run()`，在 `PatchAll` 前校验关键补丁签名并输出最小日志：
    - 通过：`[RimChat][HarmonySelfCheck] Startup patch checks passed`
    - 失败：`[RimChat][HarmonySelfCheck] ... failed` + 失败项明细

## 非中英语言键英文回退根修（v0.9.5）

- 影响范围：全局 `RimChat_*` 本地化键解析链路。
- 机制：
  - 对 `Translator.TryTranslate(string, out TaggedString)` 增加后置补丁。
  - 仅当原始翻译失败且键前缀为 `RimChat_` 时，从 `1.6/Languages/English/Keyed/RimChat_Keys.xml` 回退解析。
- 约束：
  - 非 `RimChat_*` 键不参与该补丁。
  - 不改变原版语言系统、不会覆盖其他 Mod 的翻译行为。
- 观测：
  - 首次回退会写入告警日志，标记当前活动语言缺失 RimChat keyed 条目。

## 版本日志语言目录直读与动态语言列表（v0.9.4）

- 影响范围：`RimChatSettings_APIHeader.UX` 的 API 页头版本日志读取链路。
- 语言目录解析契约：
  - 优先按 `LanguageDatabase.activeLanguage.folderName` 直读 `1.6/Languages/<folderName>`。
  - 未命中时走归一化匹配（去空白/分隔符/大小写差异）与别名映射匹配。
  - 仍未命中时 fail-fast 回退 `English`，并输出明确 `Log.Warning`。
- 可用语言来源：
  - `AvailableLanguages` 不再硬编码中英，改为扫描 `1.6/Languages` 一级子目录动态生成。
- 版本日志文件候选顺序：
  - `VersionLog_<languageFolder>.txt`
  - `VersionLog.txt`
  - `VersionLog_en.txt`
- 异常语义：
  - 当目标语言文件不存在时立即回退 English 文件，保留现有“缺失/空文件/读取失败”的 UI 提示链路。

## 空投信息卡与3天空投冷却（v0.9.7）

- `RimChat.DiplomacySystem.GameAIInterface` 冷却键新增 `RequestItemAirdrop`，配置项 `ItemAirdropCooldownTicks`（默认 180000 ticks = 3 天），存档读写与 UI 滑条已添加。
- `ApiActionEligibilityService.ValidateActionExecution("request_item_airdrop", ...)` 在参数为空的提示阶段即接入冷却校验（`ValidateCooldown(faction, "RequestItemAirdrop", "airdrop_cooldown")`），冷却期内拒绝请求并返回 `RemainingSeconds`。
- `GameAIInterface.ItemAirdrop.Barter.CommitPreparedItemAirdropTrade` 成功后调用 `SetCooldown(faction, "RequestItemAirdrop")`，仅成功 commit 触发冷却，取消/失败不触发。
- `+发送信息` 菜单新增"发送空投交易请求"入口（`RimChat_SendInfoMenuAirdropTrade`），打开 `Dialog_ItemAirdropTradeCard` 信息卡窗口（双列表：推荐候选 + 殖民地库存），玩家填写数量与银币出价后提交，自动生成结构化消息块并触发 AI 请求。
- `Dialog_ItemAirdropTradeCard` 提交载荷固定包含字段：`need`、`selected_def`、`count`、`payment_items=[{"item":"Silver","count":N}]`、`scenario=trade`。
- `Dialog_DiplomacyDialogue.ActionHint` 升级：全部外交动作 `[?]` 受限时显示"状态 + 本地化受限原因"（`BuildActionHintLine` 接入 `ActionValidationResult`，冷却类调用 `FormatCooldownReason(remainingSeconds)` 格式化为天/小时/分钟）。
- 新增 Keyed 文本：`RimChat_SendInfoMenuAirdropTrade`、`RimChat_AirdropTradeCard_*`（标题/标签/按钮）、`RimChat_ActionsHint_CooldownDays/Hours/Minutes`、`RimChat_ActionsHint_Reason_*`（各类受限原因映射）。

## 空投二阶段移除与手动改选确认（v0.9.3）

- 二阶段选择链路：
  - `request_item_airdrop` 不再发起二阶段 AI 选择请求。
  - 改为直接使用候选池进入确认流程，默认自动选 Top1。
- 确认交互：
  - 确认弹窗新增低可视度按钮 `RimChat_ItemAirdropAlternativeLowVisibility`。
  - 点击后可在前5候选内手动改选，再进入确认执行。
- 数量参数兼容：
  - 数量提取支持 `count` 与 `quantity` 双键。
  - 仍与 `need` 显式数量合并并执行合法窗口校验。

## 空投二阶段超时根修与数量窗口调整（v0.9.2）

- 二阶段超时根修：
  - `AirdropSelection` 通道禁用本地连接超时重试，避免单次 `timeout` 后自动再等一轮请求超时。
  - 二阶段新增独立队列超时配置：`ItemAirdropSecondPassQueueTimeoutSeconds`（默认 `15`，范围 `3..120`）。
  - `ItemAirdropSecondPassTimeoutSeconds` 保持为“单次请求超时”配置（默认 `25`，范围 `3..30`）。
- 二阶段诊断增强：
  - `selection_async_success/timeout/error` 诊断追加字段：
    - `firstByteMs`（从 dispatch 到收到首字节）
    - `attempts`（请求尝试次数）
    - `payloadBytes`（请求体字节数）
    - `http`（最近一次 HTTP 状态码）
    - `endpoint`（endpoint host:port）
- 空投数量窗口调整：
  - `hardMax` 不再使用 `ItemAirdropMaxTotalItemsPerDrop` 做固定总量截断。
  - 新逻辑为 `hardMax=min(maxByBudget, maxByStacks)`，其中 `maxByStacks = ItemAirdropMaxStacksPerDrop * def.stackLimit`。
  - 结果语义：数量按需求+预算决策，同时受掉落堆叠物理约束限制。

## 空投链路诊断与修复（v0.9.1）

- `request_item_airdrop` 同轮执行策略
  - 执行器改为“同轮仅接受首条成功空投请求”，后续同轮空投动作返回拒绝结果，不再“全部拒绝”。
- 二阶段候选回复前置直连
  - 在发送新 AI 请求前，若会话存在空投候选待选状态，玩家输入命中 `1/2/3/defName/label` 时直接映射 `selected_def` 并进入空投准备链路。
  - 命中前置直连后不再发起新的二阶段 AI 选择请求。
- 数量来源优先级
  - 新增数量决策：`parameters.count` 与 `need` 文本显式数量并存时取较大值。
  - 当决策数量超过 `hardMax` 时，按策略自动截断为 `hardMax`，并在审计中记录 `original->hardMax`。
- 二阶段诊断增强
  - 增加二阶段阶段审计日志：`selection_async_dispatch`、`selection_async_success`、`selection_async_timeout`、`selection_async_parse_failed`、`selection_async_error`。
  - 日志包含 requestId、timeout、候选数、queueMs、processingMs、endToEndMs、failureReason，用于区分 queue_timeout / request timeout / parse failure。

## 囚犯信息卡器官对账与读档报价刷新（v0.8.20）

- 器官快照与持久化扩展：
  - `RansomContractRecord` 新增字段：
    - `BaselineCoreOrganMissingSnapshot`
    - `ExitCoreOrganMissingSnapshot`
    - `NewlyMissingCoreOrgans`
    - `OrganFailureScheduled`
    - `OrganFailureDueTick`
    - `OrganFailurePenaltyApplied`
  - 兼容策略：新增字段均在 `ExposeData` 指定默认值，旧存档可直接加载。
- 信息卡报价强刷：
  - `CalculatePrisonerRansomQuote(...)` 新增可选参数 `forceRefresh=false`。
  - 囚犯信息卡发布链路固定 `forceRefresh=true`，确保每次发卡按当前状态重算参考赎金。
- 运行态缓存治理：
  - `GameAIInterface.ResetPrisonerRansomRuntimeState()` 清空赎金议价/信息卡快照运行态。
  - 在 `GameComponent_DiplomacyManager.StartedNewGame/LoadedGame` 调用该方法，避免静态单例跨局残留。
- 离图失败判定：
  - 判定规则：离图时仅检查“相对信息卡基准的新增核心器官缺失”。
  - 核心器官范围：`Heart/Liver/Lung/Kidney/Eye`（按实例计数）。
  - 命中后调度 `dueTick = exitTick + Rand[12500, 25000]`，到期按超时级惩罚执行（扣好感+袭击）。
  - 命中器官失败时不再叠加即时 `drop_penalty`，避免双重处罚。

## 赎金合约健康离图回执与超时谴责（v0.8.19）

- 赎金合约持久化扩展：
  - `RansomContractRecord` 新增字段：
    - `TargetPawnLabelSnapshot`
    - `ReleasedTick`
    - `HealthyExitReplyScheduled`
    - `HealthyExitReplyDueTick`
    - `HealthyExitReplySent`
  - 兼容策略：全部字段在 `ExposeData` 里带默认值，旧存档自动回填。
- 健康离图延迟回执：
  - 触发条件：囚犯离图时满足严格健康（`SummaryHealth >= 85%`、`Consciousness >= 85%`、且非 `Downed`）。
  - 调度规则：`dueTick = exitTick + Rand[12500, 25000]`（5-10 游戏小时）。
  - 投递行为：到期后向该派系会话写入 NPC 消息，并发送 `ChoiceLetter_NpcInitiatedDialogue` 主动来信。
- 超时未离图增强：
  - 保留原有 `ApplyRansomPenaltyAndRaid` 与 `RimChat_PrisonerRansomTimeout*` 信件逻辑。
  - 同步追加：
    - 会话内超时警告消息与来信提醒。
    - `EnqueuePublicPost(...)` 负向社交圈事件，驱动“派系首领谴责”AI 出稿。
- 本地化键新增：
  - `RimChat_PrisonerRansomHealthyExitReplyMessage`
  - `RimChat_PrisonerRansomHealthyExitLetterTitle`
  - `RimChat_PrisonerRansomTimeoutWarningMessage`
  - `RimChat_PrisonerRansomTimeoutWarningLetterTitle`
  - `RimChat_PrisonerRansomTimeoutCondemnSummary`

## 赎金 request_info 去重与超时冷却稳定化（v0.8.14）

- `request_info(info_type=prisoner)` 行为加固：
  - 当会话已绑定有效赎金目标时，`request_info(prisoner)` 直接返回成功语义，不再重复触发选人窗口。
  - 当会话正在等待选人时，重复触发会被去重短路，不再重入选人流程。
- 自动回复超时冷却：
  - 囚犯信息卡自动回复链路新增 90 秒冷却门禁。
  - 命中超时分类（`queue_timeout` / `network_timeout` / `drop_timeout`）后，在冷却窗口内不再自动重发同链路请求。
  - 玩家手动发送路径不受该冷却门禁影响。
- 可观测性：
  - 新增日志：`request_info(prisoner) dedup hit`
  - 新增日志：`ransom auto-reply timeout classified=... cooldown=90s`
- 契约与兼容：
  - `request_info` 与 `pay_prisoner_ransom` 动作结构、参数名、返回语义不变。
  - 不新增存档持久化字段；新增运行态冷却字段不写入 `ExposeData`。

## 赎金承诺动作一致性（MUST）强化（v0.8.13）

- 赎金语义硬约束：
  - 若自然语言出现“已提交/已支付/钱货两清/已放人离开”等完成态，**同条响应必须包含** `pay_prisoner_ransom` 动作。
  - 若当轮不含 `pay_prisoner_ransom`，自然语言必须回退为待确认/待提交语气。
- 通信语境硬约束：
  - 当前场景固定为通信终端在线聊天，禁止线下完成态叙述（到场、当面交接、带人离开）。
- 同步范围：
  - 默认提示词、系统默认配置、迁移补丁与压缩响应合同均已对齐。

## 通信终端语境与赎金承诺动作一致性（v0.8.12）

- 终端语境约束：
  - 对话统一视为通信终端在线聊天，不是线下会面。
  - 自然语言不得表达“我已到场/当面处理/线下交接”等线下完成态。
- 赎金承诺一致性约束：
  - 若自然语言声明“赎金已提交/已支付”，同条响应必须包含 `pay_prisoner_ransom` 动作。
  - 若当轮未输出 `pay_prisoner_ransom`，自然语言必须改为未提交语气（澄清或待确认）。
- 同步范围：
  - 已同步到默认提示词、系统默认配置、运行时迁移规则与动作规则文本。

## 赎金单次支付提交（v0.8.11）

- 执行契约调整：
  - `pay_prisoner_ransom` 仍使用原参数：`target_pawn_load_id`、`offer_silver`、`payment_mode?`。
  - 成功状态码改为 `paid_submitted`（语义：付款已到账并登记合约，未自动放人）。
  - 不再返回 `counter_offer/rejected_floor_not_met` 作为代码议价流程。
- 执行链路调整：
  - 执行层只做参数、目标资格、报价区间校验；通过后直接空投银币并登记合约。
  - 移除释放前置校验（`warden/exit cell`）与 `ReleasePrisoner` job 下发。
  - 缺失或失效 `target_pawn_load_id` 时回退到 `request_info(prisoner)` 选人语义。
- 失败反馈：
  - 失败消息统一为简洁系统原因（参数错误、目标无效、区间越界、模式错误、系统不可用）。
- 兼容边界：
  - 不新增存档字段，沿用现有会话字段与合约惩罚机制。

## 赎金非终态结果可视化（v0.8.10）

- 新增 UI 反馈：
  - `pay_prisoner_ransom` 返回 `counter_offer` 时，系统消息显示：目标、被拒报价、当前还价、轮次。
  - 返回 `rejected_floor_not_met` 时，系统消息显示：目标、最后报价、底价，并提示提高报价重试。
- 数据来源：
  - 从 `PrisonerRansomResultData` 读取 `StatusCode/OfferedSilver/CurrentAskSilver/FloorSilver/RoundIndex/MaxRounds/TargetPawnLoadId`。
- 契约保持：
  - 动作协议与参数不变，不新增存档字段。

## 赎金报价回归区间校验（v0.8.9）

- 执行规则调整：
  - `pay_prisoner_ransom` 不再要求 `offer_silver == currentAsk`。
  - 只要 `offer_silver` 落在当前有效报价窗口（min/max）内，即允许进入议价状态机。
- 提示规则调整：
  - 当前叫价仍是建议参考值，但不是执行层硬门禁。
  - 默认提示词、系统默认配置、迁移补丁已同步为“区间内有效”语义。
- 契约保持：
  - `request_info/pay_prisoner_ransom` 动作结构与参数名不变。
  - 不新增存档字段。

## 赎金当前叫价执行层硬校验（v0.8.8）

- 新增执行门禁：
  - 当 `PrisonerRansomNegotiationState.CurrentAskSilver > 0` 时，`offer_silver` 必须等于 `CurrentAskSilver`。
  - 不命中当前叫价直接返回 fail-fast（`offer_must_match_current_ask`）。
- 失败提示：
  - 使用 `RimChat_RansomOfferMustMatchCurrentAskSystem`（中英本地化），明确返回 `offered/currentAsk/min/max`。
- 契约保持：
  - `request_info/pay_prisoner_ransom` 动作结构与参数名不变。
  - 不新增存档字段。

## 赎金终态成功清理规则（v0.8.7）

- 执行状态机修复：
  - `pay_prisoner_ransom` 成功后仅在 `accepted_and_released` 清理赎金会话绑定状态。
  - `counter_offer`、`rejected_floor_not_met` 属于非终态成功/协商态，必须保留绑定目标用于后续议价。
- 状态判定来源：
  - 优先读取执行结果消息状态码（`result.Message`）。
  - 兼容读取结果数据状态码（`PrisonerRansomResultData.StatusCode`）。
- 契约保持：
  - 不修改 `request_info/pay_prisoner_ransom` 动作结构与参数约束。
  - 不新增存档字段。

## 赎金 request_info 条件触发（v0.8.6）

- 动作语义调整：
  - `request_info(info_type=prisoner)` 不再是 `pay_prisoner_ransom` 的强制前置。
  - 仅在缺少有效 `target_pawn_load_id` 时，用于触发选人并补充目标信息。
- 执行链路调整：
  - `pay_prisoner_ransom` 在目标信息已明确有效时可直接执行。
  - 若目标信息缺失/无效，执行层会触发选人补信息提示并拒绝本次支付动作（fail-fast）。
- 契约边界保持：
  - 不新增存档字段，不修改 `request_info/pay_prisoner_ransom` 的动作结构。
  - `offer_silver` 报价窗口约束与 `payment_mode` 规则保持不变。

## 囚犯信息卡作为玩家消息并自动触发回复（v0.8.5）

- 消息语义调整：
  - 囚犯信息卡改为玩家消息：`AddImageMessage(..., isPlayer=true, ...)`。
  - 发送者使用当前我方谈判者名称（`ResolvePlayerSenderName`）。
- 自动回复：
  - 囚犯信息卡发送后，立即复用外交请求链路发起一次 AI 回复请求。
  - 复用既有：`BuildChatMessages`、上下文解析/校验、`conversationController.TrySendDialogueRequest(...)`。
- 边界：
  - 仍受 `CanSendMessageNow()` 门控。
  - 不新增存档字段，不修改 `request_info/pay_prisoner_ransom` 契约。

## 外交发送区“+发送信息”与囚犯信息卡重构（v0.8.4）

- UI 入口：
  - 外交输入区新增纯文本入口 `RimChat_SendInfoEntry`（`+发送信息` / `+Send Info`）。
  - 点击后弹出轻量 `FloatMenu`，当前仅一项 `RimChat_SendInfoMenuPrisoner`。
  - 入口可用性与发送按钮一致，复用 `SendGateState.CanSendNow` 门控。
- 囚犯信息手动触发：
  - 新增手动入口方法（外交窗口内部）：`TryStartManualPrisonerInfoSend()`。
  - 复用既有囚犯选择弹窗：`Dialog_PrisonerRansomTargetSelector`。
  - `StartRansomTargetSelection(...)` 增加参数：`emitSelectionPromptMessage = true`。
    - AI 动作链路保持默认（会写“先选囚犯”系统提示）。
    - 手动入口传 `false`，直接弹窗，不写该提示。
- 囚犯信息卡视觉归属：
  - 语义不变：消息仍是系统语义（`isPlayer=false`，不进入玩家输入语义链）。
  - 视觉改造：命中 `imageSourceUrl == \"rimchat://ransom-proof\"` 时，按我方视觉渲染（右侧、我方头像、我方气泡配色）。
  - 新增视觉判定：
    - `IsOutboundPrisonerInfoMessage(msg)`
    - `IsPlayerVisualMessage(msg)`
- 囚犯卡布局：
  - 囚犯信息卡单独使用横向紧凑布局（左图右文）。
  - 缩略图改为 `ScaleAndCrop`，减少留白；气泡高度与宽度策略下调，降低 UI 占用。

## 赎金报价窗口约束与可视化反馈（v0.8.4）

- 赎金报价窗口约束保持不变：
  - `offer_silver` 必须落在当前窗口 `[negotiationBase*0.60, negotiationBase*1.40]`。
- 前置可视化：
  - `request_info(info_type=prisoner)` 完成后，系统消息会追加当前可报价区间（min/max）与 `currentAsk`。
- 越界失败反馈：
  - `pay_prisoner_ransom` 越界时返回可读消息（含 `offered/min/max/currentAsk`），用于引导下一轮修正报价。
## 赎金前置 request_info 链路稳定化（v0.8.3）

- 新增动作契约：
  - `request_info(info_type)`
  - 首版仅支持 `info_type=prisoner`，用于赎金前置信息请求。
- 赎金动作前置门禁：
  - `pay_prisoner_ransom` 执行前必须已成功完成 `request_info(info_type=prisoner)`。
  - 未完成前置或无有效绑定囚犯目标时，执行链路 fail-fast 返回系统拒绝消息。
- 解析层归一化：
  - 当模型返回 `pay_prisoner_ransom` 但参数缺失/非法时，`AIResponseParser` 将动作归一化为 `request_info(prisoner)`。
- 会话运行态新增字段（不入存档）：
  - `FactionDialogueSession.hasCompletedRansomInfoRequest`
  - 结合已有字段 `isWaitingForRansomTargetSelection / boundRansomTargetPawnLoadId / boundRansomTargetFactionId` 形成完整前置状态机。
- 支付后状态回收：
  - `pay_prisoner_ransom` 成功后重置赎金前置状态与绑定目标。
  - 失败场景不重置，保留上下文供继续议价。
- 关键日志点：
  - request_info 接收、候选数量、弹窗启动、选择完成/取消、支付成功后状态重置。


## 空投显式数量优先根修（v0.7.106）

- 适用动作：`request_item_airdrop(need, payment_items, scenario?, constraints?, budget_silver?(audit only), selected_def?(follow-up))`
- 行为变更（数量决策）：
  - 当 `need` 含显式数量（如 `50个干肉饼` / `50 pemmican`）时，执行阶段强制使用该数量作为 `count`，不再受二阶段 LLM 返回 `count` 影响。
  - 显式数量仍受统一合法窗口约束：`count <= max_legal_count(hardMax)`；超量继续 fail-fast（`selection_count_out_of_range`）。
- 二阶段提示词修正：
  - 移除“single-item airdrop / count=1”误导语义。
  - 明确规则：`need` 有显式数量时优先沿用；否则 `count` 必须落在 `1..max_legal_count`。
- 审计字段：
  - `RequestItemAirdrop.Stage(selection)` 的 `countSource` 统一为 `llm|fallback_explicit|fallback_default_family`，并记录最终生效数量。

## 空投二阶段异步化（v0.7.105）

- 新增内部异步入口（外交空投准备链路）：
  - `GameAIInterface.BeginPrepareItemAirdropTradeAsync(...)`
  - 语义：发起异步准备流程（支付校验 -> 候选构建 -> 别名扩展(可选) -> 二阶段选择），立即返回排队结果或即时失败/成功结果。
- 新增内部取消入口：
  - `GameAIInterface.CancelItemAirdropAsyncRequest(requestId, cancelReason, error)`
  - 语义：窗口关闭/上下文失效时主动取消空投异步请求。
- 空投动作外部契约不变：
  - `request_item_airdrop(need, payment_items, scenario?, constraints?, budget_silver?(audit only), selected_def?(follow-up))`
- 行为变更（链路级）：
  - 旧同步二阶段 `Task.Wait(timeout)` 已移除，不再阻塞主线程。
  - 二阶段与别名扩展超时通过 `AIChatServiceAsync.SendChatRequestAsync(...requestTimeoutSecondsOverride, queueTimeoutSecondsOverride)` 按空投设置覆盖。
  - 二阶段 timeout/queue_timeout 仍返回 `ItemAirdropPendingSelectionData` 并进入玩家候选确认链路。

## 空投支付解析语义匹配修复（v0.7.104）

- 修复点：`ItemAirdropPaymentResolver` 新增“语义分词全包含”匹配层，支持 CamelCase 与标签词序差异。
- 目标问题：`payment_item_unresolved` 在 `MealPackaged` 等输入下误报。
- 兼容规则：并列最高分仍返回 `payment_item_ambiguous`（Top3 候选），保持 fail-fast。

## 空投预算派生与提示可见性根修（v0.7.103）

- `request_item_airdrop` 契约更新：
  - 必填：`need`, `payment_items`
  - 可选：`scenario`, `constraints`
  - 可选审计字段：`budget_silver`（输入可带，但运行时忽略）
- 预算规则：
  - 运行时预算由 `payment_items` 市场价总和 `Floor` 派生。
  - 派生预算用于后续候选筛选、合法数量计算、确认窗显示与执行审计。
  - 若传入 `budget_silver` 与派生预算不一致，仅记录审计（`RequestItemAirdrop.BudgetMismatch`），不参与执行判定。
- 交互可见性：
  - 二阶段 `selection_timeout` 下，外交链路始终追加系统候选提示（TopN + 回复指引），不再受 NPC 可见台词是否为空影响。

## 空投二阶段超时根修与语义细分（v0.7.102）

- 二阶段选择链路升级为结构化响应（`AIChatClientResponse`）：
  - 可观测字段新增透传：`httpStatusCode/promptTokens/completionTokens/totalTokens/isEstimatedTokens/failureReason`。
- 二阶段失败语义细分：
  - `selection_timeout`：本地等待窗口超时或服务 timeout。
  - `selection_queue_timeout`：队列超时语义（沿用“待玩家确认候选”分支）。
  - `selection_service_error`：非 timeout 的服务错误（fail-fast）。
- 二阶段提示词压缩：候选行缩减为 `def/label/unit/max_legal_count`，并限制展示前 20 条。
- 配置默认值调整：`ItemAirdropSecondPassTimeoutSeconds` 默认 `25`（范围 `3..30`）。

## 空投支付解析根修与超时待确认（v0.7.101）

- 支付品解析链路升级（`request_item_airdrop.payment_items[].item`）：
  - 解析顺序固定为：`defName 精确` -> `label 精确` -> `归一化强匹配` -> `近似匹配`。
  - 并列最高分时 fail-fast：返回 `payment_item_ambiguous`，并在错误消息中附带 Top3 候选（`defName(label)`）。
  - `payment_item_unresolved` 仅在无可用匹配时返回。
- `selection_timeout` 语义变更：
  - 二阶段超时不再自动 Top1 成交。
  - 现在返回待确认数据 `ItemAirdropPendingSelectionData`（Top3 候选），由外交 UI 等待玩家指定候选后重提动作。
- 新增内部返回模型（向后兼容）：
  - `ItemAirdropPendingSelectionData`
    - `needText`
    - `budgetSilver`
    - `failureCode`（`selection_timeout` 或 `selection_queue_timeout`）
    - `failureReason`
    - `options[]`（`index/defName/label/unitPrice/maxLegalCount`）
- 动作参数增强（向后兼容）：
  - `request_item_airdrop` 可接受可选参数 `selected_def`，用于玩家在超时待确认后明确指定候选。
  - 仍保持原必填：`need`、`budget_silver`、`payment_items`。
- 提示词契约更新：
  - `payment_items.item` 约束为“优先使用 `defName`，`label` 仅在唯一可解析时备用”。

## 空投缺参阻断与动作契约修正（v0.7.99）

- 解析期 fail-fast（`AIResponseParser.AddActionIfValid`）：
  - 对 `request_item_airdrop` 增加参数结构校验；
  - 缺失或非法时直接丢弃动作，不进入执行链路。
- `request_item_airdrop` 契约（紧凑动作目录）修正为：
  - `request_item_airdrop(need, budget_silver, payment_items, scenario?, constraints?)`
  - `need`：string，必填
  - `budget_silver`：int，必填，且 `> 0`
  - `payment_items`：array，必填；每项包含 `item`(string) + `count`(int>0)
  - 支付约束：`payment_items` 总价必须 `>= budget_silver`，且超付 `<= 5%`
- 说明：该修正与 `SystemPromptConfig` 的动作定义保持一致，消除“目录提示是旧合同、执行器按新合同校验”的链路分叉。

## AI 空投以物易物 + 最终确认弹窗（v0.7.98）

- 动作契约升级：`request_item_airdrop(need, budget_silver, scenario?, constraints?, payment_items)`
  - `need`：string，必填
  - `budget_silver`：int，必填，且 `> 0`
  - `payment_items`：array，必填；每项必须包含：
    - `item`：string（支持 defName / label / 别名）
    - `count`：int（`> 0`）
  - `scenario`：可选，`general|trade|ransom`
  - `constraints`：可选，文本约束
- 执行语义：
  - 外交对话链路改为“Prepare -> Confirm -> Commit”。
  - Prepare 阶段只生成交易单并校验，不执行扣货和空投。
  - Confirm 阶段由玩家弹窗确认；确认后才提交扣货与空投。
  - Cancel 阶段终止本次动作并写系统消息，不扣货不空投。
- 付款与预算规则：
  - `budget_silver` 为预算权威值。
  - `payment_items` 折算总价必须 `>= budget_silver`。
  - 超付上限固定为 `5%`（超过即 fail-fast：`payment_overpay_too_high`）。
  - 扣货来源限定为“通电轨道信标覆盖范围”的真实可交易物资。
- fail-fast 失败码（新增/强化）：
  - `budget_required`
  - `payment_items_missing`
  - `payment_items_invalid`
  - `payment_item_unresolved`
  - `payment_item_ambiguous`
  - `payment_item_insufficient`
  - `payment_overpay_too_high`
  - `beacon_source_unavailable`
  - `player_negotiator_required`（外交对话准备阶段）
- UI 行为：
  - 同一轮对话若出现多条 `request_item_airdrop`，全部拒绝并返回失败提示。
  - 新增确认弹窗多语言键，所有 UI 文本均走语言键，不硬编码。

## RPG 首轮延迟治理与通道化思维链（v0.7.97）

- 目标：消除 RPG 新会话首轮长等待，避免提示词构建链路的同步重任务阻塞。
- 关键接口变更：
  - `IPromptPersistenceService.BuildRPGFullSystemPrompt(...)` 新增参数：
    - `allowMemoryCompressionScheduling`（默认 `true`）
    - `allowMemoryColdLoad`（默认 `true`）
  - `RpgNpcDialogueArchiveManager.BuildPromptMemoryBlock(...)` 新增参数：
    - `allowCompressionScheduling`（默认 `true`）
    - `allowCacheLoad`（默认 `true`）
  - `RpgNpcDialogueArchiveManager.HasPromptMemory(...)` 新增参数：
    - `allowCacheLoad`（默认 `true`）
- 新增运行时能力：
  - `RpgNpcDialogueArchiveManager.BeginPromptMemoryWarmup(...)`：开窗异步预热归档缓存。
  - `RpgPromptTurnContextScope` 扩展：
    - `AllowMemoryCompressionScheduling`
    - `AllowMemoryColdLoad`
- 行为约束：
  - RPG 新会话 opening turn 固定 `allowMemoryCompressionScheduling=false` 且 `allowMemoryColdLoad=false`。
  - 压缩调度由暖启动待处理队列延迟到主线程安全点执行，避免后台线程直接触发请求发送。

## 外交意图到动作双层根治（v0.7.96）

- 新增外交输出契约守卫：`DiplomacyResponseContractGuard`
  - 规则：可见对白出现明确执行承诺（如“我会安排/我已提交/这就派出”）但未附带 `{"actions":[...]}` 时判定违约。
  - 流程：首轮违约 -> 自动追加重试提示；重试后仍违约 -> 降级为角色内澄清追问。
- `AIChatServiceAsync` 外交通道新增契约重试链路：
  - 触发提示：`DIPLOMACY_CONTRACT_VIOLATION=...`
  - 观测字段：`contractValidationStatus/contractRetryCount/contractFailureReason`。
- 外交主链新增意图映射策略（`Dialog_DiplomacyDialogue.ActionPolicies`）：
  - 覆盖延迟动作：`request_item_airdrop/request_caravan/request_aid/request_raid/trigger_incident/create_quest`。
  - 模糊催单（如“再发一次/发送请求/还是没收到”）先追问确认，不直接执行动作。
  - 仅在收到肯定确认（如“确认/下单/yes/confirm”）后补发动作。
  - 缺必填参数时持续追问，不允许“口头已安排”。
- 新增短窗口防重：
  - 同动作同参数在 2 个助手回合内阻断重复执行。
- 新增外交运行态（不入存档）：
  - `FactionDialogueSession.pendingDelayedActionIntent`
  - `FactionDialogueSession.lastDelayedActionIntent`
  - `FactionDialogueSession.lastDelayedActionExecutionSignature`
  - `FactionDialogueSession.lastDelayedActionExecutionAssistantRound`

## 外交界面空投成功系统提示（v0.7.95）

- 外部动作契约不变：`request_item_airdrop(need, budget_silver?, scenario?, constraints?)`。
- 外交会话新增成功系统提示：
  - 仅在外交对话链路且动作 `request_item_airdrop` 执行成功时注入。
  - 系统消息模板：`成功触发空投({0} x{1}@{2}银)`（中文），英文同步本地化键。
- 数据来源改为结构化 payload（不解析自然语言）：
  - `ItemAirdropResultData.ResolvedLabel`
  - `ItemAirdropResultData.Quantity`
  - `ItemAirdropResultData.BudgetUsed`
- 执行结果透传：
  - `Dialog_DiplomacyDialogue` 内部 `ActionExecutionOutcome` 增加 `Data` 承载动作返回数据，供 UI 层系统消息组装使用。
- 共存行为：
  - 保留原有“空投到达”信件（`RimChat_ItemAirdropArrivedTitle/Body`），系统消息仅新增，不替代信件。

## request_item_airdrop 数量合法性单一真相源（v0.7.94）

- 外部动作契约不变：`request_item_airdrop(need, budget_silver?, scenario?, constraints?)`。
- 单物品语义约束：
  - `need` 出现多个显式数字时直接 fail-fast：`need_count_ambiguous`。
  - 不再做“多数字猜测”或静默夹紧。
- 数量上限统一计算：
  - 新增统一窗口函数：`ComputeLegalCountWindow(...)`。
  - `ValidateAirdropSelection`、超时回退、二阶段提示词上限展示全部复用该函数。
- 二阶段超时回退规则（Top1）：
  - 显式数量且 `requested > hardMax`：`selection_count_out_of_range`。
  - 无显式数量：按族群默认值（Food=25，Medicine=10，Weapon=1，Apparel=1，Unknown=5），再 `min(baseCount, hardMax)`。
- 二阶段提示词收口：
  - 明确写入 `BudgetSilver` 与每个候选 `max_legal_count`。
  - 规则固定为：`count must be 1..max_legal_count for selected_def`。
- 可观测性增强（`RequestItemAirdrop.Stage` 的 `selection` 阶段）：
  - 新增：`countSource=llm|fallback_explicit|fallback_default_family`、`hardMax`、`maxByBudget`。

## 物资空投候选目录过滤修复（v0.7.92）

- 目录构建入口：`ThingDefCatalog.IsSpawnableItemDef(...)`
- 修复内容：
  - 不再按 `scatterableOnMapGen/mineable` 全局排除物资 Def。
  - 新增 `def.IsCorpse` 目录级排除，避免尸体 Def 主导候选与 near-miss。
- 影响：
  - `request_item_airdrop` 的 prepare 阶段 `recordsScanned` 与族群候选召回恢复正常。
  - 失败诊断中 `nearMisses` 不应再以 `Corpse_*` 为主。

## 物资空投候选召回与诊断增强（v0.7.91）

- 外部动作契约保持不变：`request_item_airdrop(need, budget_silver?, scenario?, constraints?)`。
- prepare 阶段增强：
  - 先本地同义词扩展，再 AI 别名扩展（AI 仍为二阶段最终选品者）。
  - 输入 token 支持混写切分与噪声清洗，提升 `steel10个` 等表达的可检索性。
- 可观测字段增强（内部审计）：
  - `recordsScanned`
  - `rejectedByBlacklist`
  - `rejectedByBlockedCategory`
  - `rejectedByFamily`
  - `rejectedByMatchScore`
  - `nearMisses`
- 失败审计：`no_candidates` 与 `need_family_unknown` 失败会附带 prepare 诊断摘要，用于快速定位是“输入问题”还是“过滤策略问题”。

## Persona Bootstrap 请求链路移除（v0.7.90）

- 范围：仅移除 `persona_bootstrap` 外发请求，不删除现有 persona 数据结构与调试枚举定义。
- 运行时行为：
  - `GameComponent_RPGManager.PersonaBootstrap.StartNpcPersonaGeneration(...)` 不再调用 `AIChatServiceAsync.SendChatRequestAsync(...)`。
  - NPC 人格补全仅保留 RimTalk 同步/复制路径。
  - 无 RimTalk 时，bootstrap/runtime 扫描链路 fail-fast 收敛，不再产生请求。

## 物资空投 API（v0.7.89）

- 动作：`request_item_airdrop`（外部契约不变）
- 执行链路：`PrepareCandidates -> InternalSelectionLLM -> ValidateSelection -> ExecuteDrop`
- 强制策略：
  - 两阶段选择默认强制开启（无两阶段开关）
  - 保留 `EnableAIItemAirdrop` 动作总开关
  - 首轮候选为空时自动触发一次 AI CN/EN 别名扩展，再进行候选重建
  - 已识别族群且候选为空时，允许一次同族群放宽重试（不跨族群）
  - 需求无法归类且重试后仍无候选时 fail-fast（`need_family_unknown`）
- 二阶段选择输出 schema（严格）：
  - `selected_def`（string）
  - `count`（int）
  - `reason`（string）
- 失败码（新增）：
  - `need_count_ambiguous`
  - `need_family_unknown`
  - `selection_timeout`
  - `selection_json_missing`
  - `selection_selected_def_missing`
  - `selection_count_missing`
  - `selection_reason_missing`
  - `selection_out_of_candidates`
  - `selection_count_out_of_range`
- 审计分段：
  - `prepare`：候选构建摘要
  - `selection`：二阶段模型选择结果
  - `execute/failed`：投放结果或失败码
- 调试观测：
  - 新来源：`AIRequestDebugSource.AirdropSelection`
  - 二阶段请求标签：`channel:airdrop_selection`

## 物资空投 API（v0.7.86）

- 新动作：`request_item_airdrop`
- 执行入口：`RimChat.DiplomacySystem.GameAIInterface.RequestItemAirdrop(Faction faction, Dictionary<string, object> parameters)`
- 参数：
  - `need`（string，必填）
  - `budget_silver`（int，可选，优先级最高）
  - `scenario`（string，可选：`trade|ransom|general`）
  - `constraints`（string，可选，当前按文本约束处理）
- 预算规则：
  - `budget_silver`（若提供） > `scenario=ransom` 时 `colony_wealth * 1%` > `ItemAirdropDefaultAIBudgetSilver`
  - 最终预算会被 `[ItemAirdropMinBudgetSilver, ItemAirdropMaxBudgetSilver]` 夹紧
- 返回数据（成功）：
  - `selectedDefName`
  - `resolvedLabel`
  - `budgetUsed`
  - `quantity`
  - `dropCell`
- 失败语义（Fail Fast）：
  - 缺失需求、预算无效、命中黑名单、无匹配 Def、数量为 0、无合法落点均直接失败并返回失败码

## 外交过期回包与可观测队列（v0.7.84）

- `RimChat.AI.AIRequestState`
  - 新增状态：`Queued`、`Cancelled`。
- `RimChat.AI.AIRequestResult`
  - 新增运行态字段：
    - `Source`
    - `Priority`
    - `EnqueuedAtUtc`
    - `QueueDeadlineUtc`
    - `StartedProcessingAtUtc`
    - `QueuePosition`
    - `AllowCallbacks`
    - `CancelReason`
    - `FailureReason`
- `RimChat.AI.AIRequestPriority`
  - 新增内部优先级枚举：`Background`、`Interactive`。
- `RimChat.AI.AIChatServiceAsync`
  - `SendChatRequestAsync(...)`
    - 发送时会按 `AIRequestDebugSource` 自动写入请求优先级与调度元数据。
  - `CancelRequest(...)`
    - 新增可选参数：`string cancelReason`、`string error`，默认保持旧行为兼容。
    - 取消后请求进入 `Cancelled`，并禁止后续 UI 回调。
  - 本地单飞队列语义升级：
    - 保持并发上限 `1`。
    - 前台交互请求优先于后台请求。
    - 同优先级内保持 FIFO。
    - 排队超过 `60s` 自动失败，错误键为 `RimChat_ErrorQueueTimeout`。
  - `ProcessRequestCoroutine(...)`
    - 网络飞行期间会响应取消并立即 `Abort()`。
    - 回调派发前新增 `AllowCallbacks` / `Cancelled` 门禁，已取消和已失效请求不会再把 stale callback 投递到 UI。
- `RimChat.DiplomacySystem.DiplomacyConversationController`
  - `CancelPendingRequest(...)` 与同会话顶替链路现在会传递明确的取消原因（关闭窗口 / superseded）。
- `RimChat.Dialogue.DialogueDropPolicy`
  - 新增掉包原因分类器，统一判定哪些 dropped reason 只保留内部日志，不再给玩家显示。
- `RimChat.UI.Dialog_DiplomacyDialogue`
  - 外交窗口读取共享请求状态，区分“排队中”和“生成中”。
  - 真实失败改为可见错误提示，不再注入 `RimChat_DialogueResponseDropped` 系统消息。

## Prompt Workspace 首开预览增量构建（v0.7.76）

- `RimChat.Persistence.PromptPersistenceService`
  - 新增：
    - `CreatePromptWorkspaceIncrementalPreviewBuild(RimTalkPromptChannel rootChannel, string promptChannel)`
    - `StepPromptWorkspaceIncrementalPreviewBuild(PromptWorkspaceIncrementalPreviewBuildState state)`
  - 作用：
    - 仅用于 deterministic preview 的工作台预览链路；
    - 按阶段增量构建 `PromptWorkspaceStructuredPreview`（Init/Sections/Nodes/Finalize）；
    - 发生模板异常时 fail-fast 进入 `Failed`，保留已完成块并写入错误诊断块。

- `RimChat.Persistence.PromptWorkspaceStructuredPreview`
  - 新增状态字段：
    - `IsBuilding`
    - `IsFailed`
    - `Completed` / `Total`
    - `CompletedSections` / `TotalSections`
    - `CompletedNodes` / `TotalNodes`
    - `Stage`（`PromptWorkspacePreviewBuildStage`）
    - `ErrorDiagnostic`（template/channel/errorCode/line/column/message）

- `RimChat.Config.RimChatSettings`（Prompt Workspace）
  - `DrawPromptSectionWorkspace(...)`
    - 每帧驱动预览增量构建，预算固定 2ms。
  - `GetPromptWorkspaceStructuredPreview(...)`
    - 移除首次打开时同步全量 `BuildPromptWorkspaceStructuredLayoutPreview(...)` 路径；
    - 改为返回增量缓存快照（自动重建）。
  - `InvalidatePromptWorkspacePreviewCache(...)`
    - 额外清理增量构建状态，防止跨频道残留。

- `RimChat.UI.PromptWorkspaceStructuredPreviewRenderer`
  - 顶部新增进度条与计数显示（总进度 + section/node 子进度）。
  - 新增 `Error` 区块类型渲染（红色标题区）。
  - 仍保持 `Signature` 驱动布局缓存刷新。

## 通讯台派系识别根修（v0.7.72）

- `RimChat.Patches.CommsConsolePatch`
  - `GetFloatMenuOptionsPostfix(...)`
    - 拦截条件改为“可解析到有效派系目标”。
    - 不再依赖标签关键词（`call/contact/呼叫/联系`）触发拦截。
  - `ExtractFactionFromOption(...)`
    - 优先从 `FloatMenuOption.action` 闭包反射提取 `Faction`。
    - 次优先从 `console.GetCommTargets(myPawn)` + label 匹配提取。
    - 兜底遍历 `Find.FactionManager.AllFactionsListForReading` 做 label 匹配。
    - 不再读取 `Find.Selector.SingleSelectedThing`。
- 新增调试日志：
  - `Comms option intercepted: pawn=..., faction=...`
  - `Comms menu patch found no faction options: ...`

## 外交开窗拒绝日志与入口阻断（v0.7.71）

- 入口层统一行为（外交开窗）：
  - 先调用：`DialogueWindowCoordinator.TryOpen(...)`
  - 若返回 `false`：记录 `reason` 并执行入口级直接开窗阻断（`new Dialog_DiplomacyDialogue(...)`）。
- 已接入入口：
  - `RimChat.Patches.FactionDialogRimChatBridgePatch`
  - `RimChat.Patches.CommsConsolePatch.CommsConsoleCallback`
  - `RimChat.UI.Dialog_SelectFactionForDialogue`
  - `RimChat.UI.MainTabWindow_RimChat`
  - `RimChat.NpcDialogue.ChoiceLetter_NpcInitiatedDialogue`
  - `RimChat.UI.Dialog_DiplomacyDialogue`（派系切换入口）
- 调试日志关键字：
  - `Bridge dialogue open rejected`
  - `MainTab dialogue open rejected`
  - `Select-faction dialogue open rejected`
  - `NPC letter dialogue open rejected`
  - `Comms dialogue open rejected`
  - `Applying direct diplomacy open fallback`

## 对话生命周期统一模型（v0.7.70）

- 新增类型：
  - `RimChat.Dialogue.DialogueRuntimeContext`
  - `RimChat.Dialogue.DialogueContextResolver`
  - `RimChat.Dialogue.DialogueContextValidator`
  - `RimChat.Dialogue.DialogueRequestLease`
  - `RimChat.Dialogue.DialogueResponseEnvelope`
  - `RimChat.Dialogue.DialogueOpenIntent`
  - `RimChat.Dialogue.DialogueWindowCoordinator`
- 新增控制器：
  - `RimChat.Rpg.RpgDialogueConversationController`
    - `TrySend(...)`
    - `Cancel(...)`
    - `CloseLease(...)`
    - `TryApplyResponseEnvelope(...)`
- 外交控制器升级：
  - `RimChat.DiplomacySystem.DiplomacyConversationController.TrySendDialogueRequest(...)` 新增参数：
    - `DialogueRuntimeContext runtimeContext`
    - `string ownerWindowId`
    - `Action<string> onDropped`
  - 新增 `CloseLease(FactionDialogueSession session)`。
- AI 服务新增：
  - `RimChat.AI.AIChatServiceAsync.GetCurrentContextVersionSnapshot()`。
- RPG 管理器持久层升级：
  - `GameComponent_RPGManager` 新增持久化字段：
    - `pawnDialogueCooldownUntilTickById: Dictionary<string,int>`
    - `pawnPersonaPromptsById: Dictionary<string,string>`
  - 旧 `pawnDialogueCooldownUntilTick` / `pawnPersonaPrompts` 仅读档迁移，不再写入。

## RPG 动作合同注入与自动记忆门控（v0.7.67）

- `RimChat.Persistence.PromptPersistenceService.WorkbenchComposer`
  - `InjectRuntimeNodeBodies(...)`
    - RPG 通道新增 `response_contract_node_template` 正文注入：
      - 输出变量：`dialogue.response_contract_body`
      - 正文来源：`BuildRpgApiContractText(...)`
  - `GetRequiredRuntimeNodeIds(...)`
    - RPG 运行时必需节点新增：`response_contract_node_template`（fail-fast）。
  - `BuildPromptNodePlacementsForCompose(...)`
    - 新增 allowed-node 自动补全：当用户自定义布局缺少允许节点时，自动回填默认布局节点。

- `RimChat.Persistence.PromptPersistenceService.Hierarchical`
  - `ResolveRpgNodePlacements(...)`
    - 新增 `response_contract_node_template` 分支，统一 RPG 节点渲染与运行时行为。

- `RimChat.UI.Dialog_RPGPawnDialogue.RequestContext`
  - `BuildRpgSystemPromptForRequest(...)`
    - 新增动作合同存在性检测（仅 `EnableRPGAPI=true`）。
    - 合同缺失时：写告警日志并关闭“本轮自动记忆兜底”。

- `RimChat.UI.Dialog_RPGPawnDialogue.ActionPolicies`
  - `EnsureRpgActionFallbacks(...)`
    - 行为调整：退出类兜底始终保留；自动记忆映射与记忆兜底可被本轮门控关闭。
  - 自动记忆单次门控：
    - 自动来源（协作映射/轮次兜底/无动作连击兜底）单会话最多触发一次。
    - 显式模型动作 `TryGainMemory` 不计入该门控。
  - 协作意图词表收紧：
    - 移除高歧义短词（如 `okay` 及单字确认词），改为明确承诺短语触发。

## NPC 记忆存档隔离修复（v0.7.61）

- `RimChat.Memory.RpgNpcDialogueArchiveManager`
  - 写盘 fail-fast：
    - `OnBeforeGameSave(...)`
    - `RecordTurn(...)`
    - `FinalizeSession(...)`
    - `RecordDiplomacySummary(...)`
    - 行为：存档标识不可解析时直接阻断写盘，并输出错误日志；不再允许写入共享 `Default` 桶。
  - 存档名解析链路：
    - `ResolveCurrentSaveKey()`
    - `GetCurrentSaveName()`
    - 解析顺序：`name/Name/fileName/FileName` -> 任意字符串成员启发式 -> `ScribeMetaHeaderUtility.loadedGameName`。
  - legacy 迁移链路：
    - `TryMigrateLegacyArchives(...)`
    - 首次进入目标存档时，先备份 legacy JSON 到 `Prompt/NPC/_migration_backup/...`，再迁移到当前存档目录并写一次性迁移标记。
  - 档案隔离字段：
    - `RpgNpcDialogueArchive.SaveKey`（JSON 字段：`saveKey`）
    - 读取时仅接纳“当前存档 saveKey”或 legacy 无 `saveKey` 档案。

## 对话链路收敛与日志分级（v0.7.59）

- `RimChat.UI.Dialog_DiplomacyDialogue`
  - 动作执行失败分级：
    - `ExpectedDenied`（cooldown / blocked / validation 等预期拒绝）：默认写 `Info`，不视为异常。
    - `UnexpectedFailure`：写 `Warning`。
  - 当存在成功动作时，预期拒绝不再追加系统失败消息，避免“任务已成功但又提示失败”的双状态噪声。
  - 对话失败总结仅统计 `UnexpectedFailure`，不再被预期拒绝污染。

- `RimChat.AI.AIChatServiceAsync`
  - `BuildRejectedInputFallbackMessages(...)` 重试提示词改为精简合同，减少系统腔和机械化冗长输出放大。

## API 可用性链路误判修复（v0.7.58）

- `RimChat.Config.RimChatSettings`（`RimChatSettings_ApiUsability.cs`）
  - UI 入口改造：
    - `测试连通性` + `测试可用性` 同行双列按钮（50/50）。
  - 成功摘要增强：
    - 新增耗时速度评级映射（`<500 极快`、`500-1499 快`、`1500-2999 正常`、`3000-5999 慢`、`>=6000 极慢`）。
    - 仅成功结果显示速度评级；失败摘要保持原格式。
    - 速度为 `极慢` 时追加“连接质量较差，建议更换服务商”提示。

- `RimChat.Config.ApiUsabilityDiagnosticService`
  - `RunLocalDiagnosticCoroutine(...)` 流程调整：
    - 由本地 5 步改为 4 步（移除本地模型可用性阻断步骤）。
    - 本地链路不再因为模型列表未命中直接失败，最终以聊天探测与响应契约校验判定可用性。
  - 云端 6 步流程保持不变（仍含模型可用性校验）。

## API 设置页双测试与可用性诊断 API（v0.7.57）

- `RimChat.Config.RimChatSettings`（`RimChatSettings_ApiUsability.cs`）
  - 新增 UI/调度方法：
    - `DrawQuickConnectivityTestButton(...)`
    - `DrawUsabilityTestButton(...)`
    - `DrawUsabilityTestResult(...)`
    - `StartUsabilityTest()` / `RunUsabilityTestCoroutine()`
  - 行为：
    - 保留 `测试连通性` 作为快速探测。
    - 新增 `测试可用性` 执行分阶段 fail-fast 深度测试。
    - 深度测试失败后提供建议列表、技术细节折叠、日志观测跳转。

- `RimChat.Config.ApiUsabilityDiagnosticService`
  - 新增核心入口：
    - `RunCloudDiagnosticCoroutine(ApiConfig, Action<ApiUsabilityProgress>, Action<ApiUsabilityDiagnosticResult>)`
    - `RunLocalDiagnosticCoroutine(LocalModelConfig, Action<ApiUsabilityProgress>, Action<ApiUsabilityDiagnosticResult>)`
  - 诊断输出模型：
    - `ApiUsabilityDiagnosticResult`
    - `ApiUsabilityStepResult`
    - `ApiUsabilityStep`
    - `ApiUsabilityErrorCode`
  - 错误码覆盖：
    - `AUTH_INVALID`
    - `ENDPOINT_NOT_FOUND`
    - `MODEL_NOT_FOUND`
    - `TIMEOUT`
    - `RATE_LIMIT`
    - `TLS_OR_CERT`
    - `DNS_OR_NETWORK`
    - `RESPONSE_SCHEMA_INVALID`
    - `LOCAL_SERVICE_DOWN`
    - `UNKNOWN`

- API 观测联动：
  - `RimChat.AI.AIRequestDebugSource` 新增 `ApiUsabilityTest`。
  - `Dialog_ApiDebugObservability.GetSourceLabel(...)` 增加可用性测试来源展示。

## 提示词单真源 API 收敛（v0.7.55）

- `RimChat.Config.RimChatSettings` (`RimChatSettings_RimTalkCompat.cs`)
  - `SetPromptSectionCatalog(...)`
    - 语义变更：迁移专用 fail-fast 入口，正式编辑链路禁止调用。
  - `ImportLegacySectionCatalogToUnifiedCatalog(RimTalkPromptEntryDefaultsConfig sections, string sourceId, bool persistToFiles = true)`
    - 新增：legacy section -> unified 单向导入 API。
  - `SetPromptSectionText(string promptChannel, string sectionId, string content, bool persistToFiles = true)`
    - 新增：工作台 section 编辑统一写入 unified。
  - `SetPromptUnifiedCatalog(PromptUnifiedCatalog catalog, bool persistToFiles = true)`
    - 新增 `persistToFiles` 控制内存态/落盘态。
  - `SetPromptNodeText(...)` / `SetPromptNodeLayout(...)` / `SavePromptNodeLayouts(...)`
    - 新增 `persistToFiles` 参数，支持“仅内存编辑，显式保存落盘”。
  - `PersistUnifiedPromptCatalogToCustom()` / `HasPendingUnifiedPromptCatalogChanges()`
    - 新增：统一 catalog 脏状态与显式落盘接口。

- `RimChat.Config.PromptPresetChannelPayloads`
  - 变更：移除正式字段 `PromptSectionCatalog`，payload 正式真源仅保留 `UnifiedPromptCatalog`。
  - 兼容：legacy payload 中的 section 字段仍可导入，但不再回写到正式 payload。

- `RimChat.Config.RpgPromptCustomConfig`
  - 变更：移除正式字段 `PromptSectionCatalog`。
  - 兼容：通过 `RpgPromptCustomStore.LoadLegacyPromptSectionCatalogSnapshot()` 做一次性 legacy section 读取导入。

- `RimChat.Persistence.IPromptPersistenceService` / `PromptPersistenceService`
  - 新增：`LoadConfigReadOnly()`
  - 新增：`RepairAndRewritePromptDomains()`
  - 约束：工作台预览、UI 刷新、提示词拼装预览链路应走 `LoadConfigReadOnly()`。

## 提示词工作台预设与编辑器增强（v0.7.54）

- `RimChat.Config.PromptPresetStoreConfig`
  - `SchemaVersion` 升级为 `2`。
  - 新增 `DefaultPresetId`，用于稳定标识只读默认预设（不再依赖名称推断）。
- `RimChat.Config.IPromptPresetService`
  - 新增：
    - `bool IsDefaultPreset(PromptPresetStoreConfig store, string presetId)`
    - `bool EnsureEditablePresetForMutation(RimChatSettings settings, PromptPresetStoreConfig store, string selectedPresetId, string forkNamePrefix, out PromptPresetConfig editablePreset, out bool forked, out string error)`
- `RimChat.Config.PromptPresetService`
  - 归一化链路新增默认预设回填规则：
    - 优先匹配 canonical default payload；
    - 多候选取最早创建；
    - 无候选取最早预设；
    - 全程不按名称猜测默认预设。
  - 自动分叉命名：`Custom yyyyMMdd-HHmmss`（重名自动后缀）。
- `RimChat.Config.RimChatSettings`（Prompt Workspace）
  - 工具栏动作替换为 `Undo/Redo/Save/Reset`。
  - Undo/Redo 实现为按 `preset + channel + mode(section|node) + targetId` 维度隔离的文本历史栈。
  - `Save` 走强制 `PersistPromptWorkspaceBufferNow()`；`Reset` 仅作用当前编辑对象（分段或节点）。
  - `PersistPromptWorkspaceBufferNow(..., persistToDisk:true)` 仅在实质文本变更时同步 preset payload；无实质改动保存静默成功。
  - 切换保护：分段/通道/节点/预设切换前统一执行 `PersistPromptWorkspaceBufferNow(force: true)`；返回失败时中止切换（fail-fast），避免未落盘文本被旧 payload 覆盖。
  - 预设同步失败语义：`PersistPromptWorkspaceBufferNow(...)` 在 preset payload 同步失败时返回 `false` 并保留 pending 状态，调用方必须阻断后续切换。
  - 预设列表支持行内复制/删除、双击重命名；默认预设重命名意图触发自动分叉后再重命名。

## RPG PromptContext Pawn 根绑定修复（v0.7.53）

- `RimChat.Prompting.RimTalkNativeRpgPromptRenderer`
  - `TryRenderRpgPrompt(string promptText, string promptChannel, DialogueScenarioContext scenarioContext, out string rendered, out RimTalkNativeRenderDiagnostic diagnostic)`
    - 新增 `promptChannel` 入参，通道语义不再仅靠 tags 推断。
    - 统一构建 `PromptContext` 的 pawn 根绑定：`CurrentPawn / Pawns / AllPawns / ScopedPawnIndex`。
- `RimChat.Prompting.RimTalkNativeRenderDiagnostic`
  - 新增字段：
    - `PromptChannel`
    - `CurrentPawnLabel`
    - `PawnCount`
    - `AllPawnCount`
    - `ScopedPawnIndex`
    - `RemainingTokensPreview`
- `RimChat.Memory.RpgNpcDialogueArchiveManager` (`Sessions` partial)
  - `BuildSessionSummaryRequestMessages(...)`
    - 输入契约改为“尽量提供真实 NPC/interlocutor pawn”。
    - 场景构建改为 `CreateRpg(interlocutorPawn, npcPawn, false, ...)`，禁止 `CreateRpg(null, null, ...)`。

## RPG 原生 RimTalk 变量收口（v0.7.52）

- `RimChat.Prompting.RimTalkNativeRpgPromptRenderer`
  - `TryRenderRpgPrompt(string promptText, string promptChannel, DialogueScenarioContext scenarioContext, out string rendered, out RimTalkNativeRenderDiagnostic diagnostic)`
    - 在 RPG 运行时最终文本阶段调用 RimTalk 原生 `ScribanParser.Render(...)`。
    - 负责构建原生 `PromptContext`、注入 `VariableStore` / `ChatHistory` / `PawnContext` / `DialoguePrompt`，并输出结构化诊断。
- `RimChat.Prompting.RimTalkNativeRenderDiagnostic`
  - 字段：
    - `BoundMethod`
    - `PromptChannel`
    - `CurrentPawnLabel`
    - `PawnCount`
    - `AllPawnCount`
    - `ScopedPawnIndex`
    - `ContextBuilt`
    - `ErrorMessage`
    - `RemainingTokenCount`
    - `RemainingTokensPreview`
- `RimChat.Persistence.PromptPersistenceService`
  - `BuildUnifiedChannelSystemPrompt(...)`
    - RPG 根通道在 runtime 且非 preview 时，追加 RimTalk 原生二次渲染阶段。
  - `RenderRawModVariablesSection(...)`
    - 对 RimTalk token 改为"保留/归一化为 raw token"，不再在该阶段消费为本地模拟值。
  - `IsDiplomacyNativeVariablePassthroughSection(RimTalkPromptChannel rootChannel, string promptChannel, string templateId)`
    - 判定 diplomacy 对话通道（diplomacy_dialogue / proactive_diplomacy_dialogue）中需要直通处理的 section（非 `mod_variables`）。
    - 仅对根通道为 Diplomacy 的对话通道生效，不扩展到其他通道类型。
  - `ShouldPassthroughRimTalkNativeToken(string normalizedToken)`
    - 识别 RimTalk 原生变量 token。检测 `.rimtalk.` 命名空间路径或 legacy 映射到 `.rimtalk.` 的 token。
  - `ExtractSectionIdFromTemplateId(string templateId)`
    - 从 templateId 提取最后一段 section 标识。
  - `PreprocessDiplomacyNativeVariables(string template)`
    - 对外交通道目标 section 中的模板文本做预处理，将识别的原生变量 token 替换为 RimTalk raw token 文本。
    - 无法解析的原生变量保留原始 token 文本（WYSIWYG 预览一致性）。
  - `RenderUnifiedTemplate(...)`
    - 对 diplomacy 对话通道，在统一 Scriban 渲染前先调用 `PreprocessDiplomacyNativeVariables` 预处理原生变量。

## RimTalk 自定义变量快照链路修复（v0.7.51）

- `RimChat.Prompting.PromptRuntimeVariableBridge`
  - `RefreshRimTalkCustomVariableSnapshot(bool force = false)`
    - 读取 `GetAllCustomVariables()` 并执行节流刷新（默认冷却 1000ms）。
    - 输出快照遥测日志：`raw_count` / `parsed_count` / `duplicate_count` / `force`。
    - 当 `raw_count > 0` 且 `parsed_count == 0` 时，按 fail-fast 阻断 Bridge 链路并记录明确错误。
  - `GetCustomVariables()`
    - 改为“读取前刷新尝试 + 返回快照”，避免首轮快照冻结。
  - `ParseCustomVariable(object item)`
    - 支持 tuple 字段与命名字段双协议读取：
      - 名称：`Item1` / `VariableName` / `Name` / `LegacyName` / `Key`
      - ModId：`Item2` / `SourceModId` / `ModId` / `SourceId`
      - 描述：`Item3` / `Description` / `Desc` / `Tooltip`
      - 类型：`Item4` / `Kind` / `VariableKind` / `Type` / `Scope`
- `RimChat.Config.RimChatSettings`
- `RimChat.Config.RimChatSettings (RimTalkVariableBrowser partial)`
  - `EnsurePromptVariableSnapshotCacheFresh()`
    - 浏览器重建前先刷新 RimTalk 快照，保持展示链路与手动插入链路一致。

## RimChat ↔ RimTalk Bridge API Changes（v0.7.50）

- `RimChat.Prompting.PromptRuntimeVariableBridge`
  - `InitializeBridgeChain()`
    - Strict bridge startup orchestration with fail-fast signature validation.
  - `ValidateRimTalkBridgeSignaturesOrFail()`
    - Required signatures:
      - `RimTalk.API.ContextHookRegistry.RegisterContextVariable(...)`
      - `RimTalk.API.ContextHookRegistry.GetAllCustomVariables()`
      - `RimTalk.API.ContextHookRegistry.UnregisterMod(string)`
      - `RimTalk.API.ContextHookRegistry.TryGetContextVariable(...)`
      - `RimTalk.Prompt.PromptContext`
      - `RimTalk.Prompt.VariableStore`
  - `RegisterRimChatSummaryVariable()`
    - Registers `rimchat_summary` via RimTalk context-variable API.
  - `BuildRimChatSummaryAggregateText()`
    - Exports a lightweight cross-channel summary block (1200-char budget).
  - `StrictLegacyCleanup()`
    - Removes legacy bridge artifacts, including old runtime keys and old preset mod entries.
## Default `mod_variables` Manual-Only Flow（v0.9.72）

- `RimChat.Config.RimChatSettings`
  - `LoadRpgPromptTextsFromCustom()` no longer auto-populates blank `mod_variables` sections during settings load.
  - `BuildCanonicalSectionEntry(...)` no longer injects generated raw-token content into blank `mod_variables` entries while rebuilding canonical prompt-entry coverage.
- `RimChat.Config.RimChatSettings_PromptSectionWorkspace`
  - Prompt Workbench no longer substitutes blank RPG `mod_variables` editor text with generated variable-list content.
- `RimChat.Persistence.PromptPersistenceService`
  - Workbench compose / incremental preview no longer inject generated `mod_variables` content when the section template is blank.
- `RimChat.Prompting.PromptRuntimeVariableBridge`
  - `BuildModVariablesSectionContent()` remains available as a manual tooling helper for browser insertion flows; it is no longer part of default-preset auto-fill semantics.

  - `GetRimTalkCustomVariablesSnapshot()` / `RefreshRimTalkCustomVariableSnapshot()`
    - Snapshot APIs for RimTalk custom-variable synchronization.
  - `BuildModVariablesSectionContent()`
    - Produces raw-token list for manual browser/tool insertion flows.
  - `ResolveRawToken(string variablePath)`
    - Resolves browser insertion token to RimTalk raw token when applicable.

- `RimChat.Prompting.PromptVariableDisplayEntry`
  - Added fields:
    - `RawToken`
    - `NamespacedToken`
    - `DefaultInsertToken`
  - Contract:
    - Variable browser displays both token tracks; insertion uses `DefaultInsertToken` (raw-first policy).

## create_quest Fail-Fast + RPG Profile Extension（v0.7.48）

- `AIActionExecutor.ExecuteCreateQuest(...)`
  - Validation failure now returns: original denial reason + current-faction allowed `questDefName` list.
  - Behavior policy: strict fail-fast only, no alias remap, no fallback quest generation.
- `PromptTextConstants.QuestGuidanceNodeLiteralDefault`
  - Default node template switched to `{{ dialogue.quest_guidance_body }}` to guarantee dynamic quest-availability injection.
- `PromptPersistenceService.TryMigrateLegacyNodeBodyLiteralTemplates(...)`
  - Added migration pattern for legacy Chinese hardcoded quest-guidance literals to runtime-body placeholder template.
- New runtime prompt variable:
  - `pawn.relation.social_summary`
  - Contract: bilateral social summary for the active pawn pair, including opinion A->B / B->A, direct relations, kinship/romance, and faction-goodwill hints.
- Expanded variable output contract (RPG channel):
  - `pawn.target.profile`
  - `pawn.initiator.profile`
  - Added fields: `Recent Job State`, `Needs` (switch-gated), `Visible Conditions` (switch-gated), `Recent Memories` (switch-gated).

## Prompt Variables + Persona Resolution（v0.7.47）

- New variable:
  - `world.faction.description`
  - Runtime source: `FactionPromptManager.GetPrompt(currentFactionDefName)`.
  - Value contract: effective faction prompt text (default templates + custom overrides).
- Updated variable resolution:
  - `pawn.personality` now resolves through `GameComponent_RPGManager.ResolveEffectivePawnPersonalityPrompt(...)`.
  - Resolution order:
    1. RimTalk persona (when available and readable)
    2. Stored RimChat persona
    3. No external persona-bootstrap request is issued
- Prompt Workbench quick actions:
  - `Faction Prompt` quick button now opens faction template editor entries (`Dialog_FactionPromptEditor`).
  - `Persona Prompt` quick-save flow now auto-attempts insertion of `{{ pawn.personality }}` into current-channel `character_persona` (idempotent).

## RPG Relationship Profile Dedup + Remove kinship=no Restriction（v0.7.44）

- `RimChat.Persistence.PromptPersistenceService` (`Hierarchical` partial)
  - `ResolveRpgNodePlacements(...)`
    - `rpg_kinship_boundary` placement is now layout-compatible only and no longer emits standalone output text.
  - `BuildRpgKinshipBoundaryGuidanceText(...)`
    - returns `string.Empty` when `kinship == no`;
    - renders boundary rule text only when `kinship == yes`.
- Default template chain update:
  - `Prompt/Default/PawnDialoguePrompt_Default.json`
  - `Prompt/Default/PromptUnifiedCatalog_Default.json`
  - `RimChat/Config/RpgPromptDefaultsConfig.cs`
  - `RimChat/Config/PromptUnifiedDefaults.cs`
  - `rpg_relationship_profile` now uses conditional guidance line rendering:
    - show `Guidance` line only when `dialogue.guidance` is non-empty.
- Unified catalog auto-migration:
  - `RimChat/Config/PromptUnifiedCatalog.cs`
  - During node normalization, legacy template patterns:
    - `引导：{{ dialogue.guidance }}`
    - `Guidance: {{ dialogue.guidance }}`
    are migrated to conditional guidance form in an idempotent way.
- Runtime behavior contract:
  - `kinship=no`: no kinship boundary guidance injected, and no guidance line rendered in profile.
  - `kinship=yes`: kinship boundary guidance remains active, but appears only once (inside relationship profile).

## Observability Window + 30m Token Trend + RPG Prompt-Memory Cache（v0.7.43）

- `RimChat.UI.Dialog_ApiDebugObservability`
  - Added header action:
    - `TryOpenRimChatSettingsWindow()`
  - Behavior:
    - Opens `Dialog_ModSettings` for `RimChatMod`.
    - Fails fast with localized error message when mod instance is unavailable.
- `RimChat.AI.AIChatServiceAsync` (`DebugTelemetry` partial)
  - Updated debug telemetry window constants:
    - `DebugWindowMinutes = 30`
    - `DebugBucketMinutes = 1`
    - `DebugRetentionMinutes = 35`
  - `BuildRequestDebugSnapshot(DateTime nowUtc)` now performs single-pass aggregation:
    - clones records in-window
    - builds per-minute buckets
    - computes summary metrics in the same pass
- `RimChat.Memory.RpgNpcDialogueArchiveManager`
  - Added lightweight memory probe:
    - `HasPromptMemory(Pawn targetNpc, Pawn currentInterlocutor = null)`
  - `BuildPromptMemoryBlock(...)` now uses version-based in-memory cache keyed by:
    - target pawn id
    - interlocutor pawn id
    - summary turn limit
    - summary char budget
  - Added prompt-memory cache invalidation on archive mutation paths:
    - turn write
    - session finalize
    - diplomacy summary write
    - archive reload
    - compression success/failure
- Compatibility contract:
  - No save schema/API wire contract changes.
  - No Def/Harmony target behavior changes.

## Think Tag Dual-Stage Filtering（v0.7.42）

- New sanitizer:
  - `RimChat.AI.ModelOutputSanitizer`
  - `StripReasoningTags(string text)`
  - Contract: remove full hidden-reasoning blocks (`<think>...</think>`, `<thinking>...</thinking>`), trim dangling open blocks, and remove stray closing tags.
- Service-stage ingress filtering:
  - `RimChat.AI.AIJsonContentExtractor.TryExtractPrimaryText(...)`
  - Behavior change: candidate text is sanitized before it can be returned to chat services; empty-after-sanitize candidates are discarded.
- Display-stage filtering:
  - `RimChat.AI.ImmersionOutputGuard.ValidateVisibleDialogue(...)`
  - Behavior change: same sanitizer runs before visible/actions split, so UI rendering paths cannot leak think blocks even when content bypasses normal parse flow.
- Diplomacy parser alignment:
  - `RimChat.AI.AIResponseParser.NormalizeDialogueText(...)`
  - Behavior change: think-tag stripping is now an explicit first step before strategy-section trimming and immersion validation.
- Compatibility:
  - No action schema change.
  - No save format change.
  - No new external config switch.

## Diplomacy Bubble Avatar + Speaker Backfill（v0.7.41）

- `RimChat.Memory.FactionDialogueSession`
  - `AddMessage(string sender, string message, bool isPlayer, DialogueMessageType messageType = DialogueMessageType.Normal, Pawn speakerPawn = null)`
  - `AddImageMessage(string sender, string caption, bool isPlayer, string imageLocalPath, string imageSourceUrl, Pawn speakerPawn = null)`
- `RimChat.Memory.DialogueMessageData`
  - New fields:
    - `string speakerPawnThingId`
    - `Pawn speakerPawn` (serialized reference)
  - New APIs:
    - `void SetSpeakerPawn(Pawn pawn)`
    - `Pawn ResolveSpeakerPawn()`
- `RimChat.UI.Dialog_DiplomacyDialogue` (speaker/avatar behavior)
  - On window open, legacy messages are backfilled with speaker data.
  - Player fallback speaker: best colony pawn by Social skill when negotiator is unavailable.
  - Faction fallback speaker: leader first, otherwise a fixed random speaker per session.
  - Bubble layout now reserves avatar lanes and uses max width = 85% of usable track.

## Pawn↔Pawn Combat Gate（v0.7.40）

- `RimChat.Core.PawnCombatStateUtility`
  - `IsEitherPawnInCombatOrDrafted(Pawn first, Pawn second)`
  - `IsPawnInCombatOrDrafted(Pawn pawn)`
  - 统一判定：`pawn.Drafted == true` 或 `pawn.CurJob?.def` 命中
    - `JobDefOf.Wait_Combat`
    - `JobDefOf.AttackMelee`
    - `JobDefOf.AttackStatic`
    - `JobDefOf.UseVerbOnThing`
- `RimChat.Comp.CompPawnDialogue`
  - `CanShowRpgDialogueOption(...)` 新增战斗态双向门控；任一方命中则不返回右键对话入口。
- `RimChat.AI.JobDriver_RPGPawnDialogue`
  - `MakeNewToils(...)` 的 `openDialogue` 初始化阶段新增 fail-fast 二次判定；命中后直接终止窗口打开。

## Prompt Workbench Node Layout Header Cleanup（v0.7.32）

- `RimChat.Config.RimChatSettings_PromptSectionWorkspace`
  - `DrawPromptWorkspaceNodeLayoutList(...)` 移除顶部固定 `Body/正文` 列表头行，仅保留可编辑节点项列表。
  - 该调整仅影响节点编排面板显示，不改变节点排序与保存规则。

## Prompt Workbench Body/ThoughtChain Terminal Ordering（v0.7.31）

- `RimChat.Persistence.PromptPersistenceService.WorkbenchComposer`
  - `ComposePromptWorkspace(...)` 预览块组装顺序统一为：
    - 非思维链节点（`metadata_after/main_chain_before/main_chain_after/dynamic_data_after/contract_before_end`，保持原相对顺序）
    - 正文聚合块（`SectionAggregate/Body`）
    - 思维链节点（`thought_chain_node_template`）
    - Footer（`</prompt_context>`）
- `RimChat.Persistence.PromptPersistenceService.SectionAggregates`
  - 新增 `AddPromptWorkspaceThoughtChainBlocks(...)`，专门负责末尾追加思维链块。
  - `AddPromptWorkspaceNodeBlocks(...)` 增加 `includeThoughtChain` 过滤维度，普通节点渲染与思维链渲染完全分离。
  - 将思维链识别逻辑统一为 `IsThoughtChainPlacement(...)`，不再依赖固定 slot。
- `RimChat.Persistence.PromptPersistenceService.Hierarchical`
  - `ApplyResolvedNodePlacements(...)` 的“后置判定”同步切换为 `IsThoughtChainPlacement(...)`，与 Workbench 识别规则保持一致。

## Prompt Workbench Label + Body Order Cleanup（v0.7.30）

- `RimChat.Config.PromptUnifiedNodeSchemaCatalog`
  - 三个节点显示名统一为业务语义标签：
    - `api_limits_node_template -> API Limits`
    - `quest_guidance_node_template -> Quest Rules`
    - `response_contract_node_template -> Response Contract`
- `RimChat.Persistence.PromptPersistenceService`
  - 预览与运行时都通过同一条 placement 规则，把 `thought_chain_node_template` 安排到正文聚合块之后。
- `RimChat.UI.PromptWorkspaceStructuredPreviewRenderer`
  - 预览节点标题不再附加 slot 标签；正文块标题收口为 `Body`，分段副标题只显示显示名。

## Node Template + Runtime Body 回归（v0.7.28）

- `RimChat.Config.PromptTextConstants`
  - `ApiLimitsNodeLiteralDefault` / `QuestGuidanceNodeLiteralDefault` / `ResponseContractNodeLiteralDefault` 从说明文硬文本回退为“多行节点模板 + Scriban变量正文”：
    - `{{ dialogue.api_limits_body }}`
    - `{{ dialogue.quest_guidance_body }}`
    - `{{ dialogue.response_contract_body }}`
- `RimChat.Persistence.PromptPersistenceService.Hierarchical`
  - `ResolveDiplomacyNodePlacements(...)` 继续沿用旧值来源：
    - `AppendApiLimits(...)`
    - `AppendDynamicQuestGuidance(...) + AppendQuestSelectionHardRules(...)`
    - `AppendAdvancedConfig(...) / AppendSimpleConfig(...)`
  - `RenderPromptNodeTemplate(...)` 新增 fail-fast：三段运行时 body 为空时抛 `PromptRenderException(TemplateMissing)`，不再静默输出空节点。
- `RimChat.Persistence.PromptPersistenceService`
  - `EnsurePromptTemplateDefaults(...)` 增加一次性自动迁移：识别三段旧硬文本模板并重写为新 Scriban 节点模板。
  - 迁移命中会写 `Player.log`，便于排查旧配置是否被自动修复。

## Social News JSON Contract Alignment（v0.7.27）

- `Prompt/Default/PromptUnifiedCatalog_Default.json`
  - `social_news_style`：改为完整社交新闻风格模板（含 category/source/credibility/game language 变量）。
  - `social_news_json_contract`：强制输出完整结构，必填键为 `headline/lead/cause/process/outlook`，可选 `quote/quote_attribution`。
  - `social_news_fact`：改为结构化 fact seed 模板（包含 `origin_type/source_faction/target_faction/summary/intent_hint/facts`）。
- `RimChat.Config.PromptUnifiedDefaults`
  - `social_news_*` 回退节点不再使用简化硬编码，改为引用 `PromptTextConstants` 的社交默认模板常量，确保回退与默认资产一致。

## Strict Workbench WYSIWYG（v0.7.26）

- `RimChat.Persistence.PromptPersistenceService`
  - `BuildFullSystemPrompt(...)`：改为统一调用 `BuildUnifiedChannelSystemPrompt(...)`（diplomacy runtime channel）。
  - `BuildRPGFullSystemPrompt(...)`：改为统一调用 `BuildUnifiedChannelSystemPrompt(...)`（rpg runtime channel）。
  - `BuildDiplomacyStrategySystemPrompt(...)`：改为统一调用 `BuildUnifiedChannelSystemPrompt(...)`（`diplomacy_strategy`）。
- `RimChat.Persistence.PromptPersistenceService.WorkbenchComposer`
  - `BuildUnifiedChannelSystemPrompt(...)`：组装模式改为 deterministic（`deterministicPreview=true`），停用运行时环境/动态数据注入差异。
- `RimChat.Config.RimChatSettings_PromptSectionWorkspace`
  - `GetPromptWorkspaceStructuredPreview()`：预览统一为完整布局视图 `BuildPromptWorkspaceStructuredLayoutPreview(...)`。
- `RimChat.AI.AIChatServiceAsync`
  - `NormalizeRequestMessagesForProvider(...)`：不再在 system-only 请求自动补 `user` 消息。
  - `ProcessRequestCoroutine(...)`：移除 HTTP 400 rejected-input 的“降载重写重试”链路，保持发送 payload 不被二次改写。

## Prompt Unified Catalog Lifecycle Consistency（v0.7.25）

- `RimChat.Config.PromptUnifiedNodeSchemaCatalog`
  - 新增严格节点通道校验入口：
    - `NormalizeStrictChannelOrThrow(promptChannel)`
    - `GetAllowedNodesStrict(promptChannel)`
    - `EnsureNodeAllowedForChannelOrThrow(promptChannel, nodeId, operation)`
  - 保留 `GetAllowedNodes(...)` 兼容入口，不改全局 `NormalizeLoose(...)` 行为。
- `RimChat.Config.PromptUnifiedCatalog`
  - 行为契约升级（fail-fast）：
    - `ResolveNode(...)`：非法 `channel/nodeId` -> 抛 `InvalidOperationException`。
    - `ResolveNodeLayout(...)`：非法 `channel/nodeId` -> 抛 `InvalidOperationException`。
    - `SetNode(...)` / `SetNodeLayout(...)`：非法 `channel/nodeId` -> 抛 `InvalidOperationException`。
  - 新增：
    - `NormalizeWithReport(fallback)` -> `PromptUnifiedCatalogNormalizeReport`
    - `ValidateInvariantsOrThrow()`
  - 归一化报告字段：
    - `RemovedNodeCount`
    - `RemovedLayoutCount`
    - `FilledDefaultLayoutCount`
    - `UnknownChannelCount`
    - `HasStructuralChange`
- `RimChat.Config.RimChatSettings_RimTalkCompat`
  - `EnsureUnifiedCatalogReady()` 保存判定改为：
    - `legacyMigrationChanged || migrationVersionChanged || normalizeReport.HasStructuralChange`
  - 删除旧的 `requiresLayoutSave` 数量判定。
  - 日志分层：不变量阻断错误 `Error` + throw；清洗摘要 `Warning`；布局补全/迁移成功 `Info`。

## Request Message Normalization + Faction Goodwill Safety（v0.7.24）

- `RimChat.AI.AIChatServiceAsync`
  - `SendChatRequestAsync(...)`
    - 发送前新增 `NormalizeRequestMessagesForProvider(...)`：
      - role 只允许 `system/user/assistant`（非法值归一到 `user`）。
      - 当请求存在消息但没有有效 `user` 消息时，自动补最小 `user` 指令。
  - `BuildRejectedInputFallbackMessages(...)`
    - fallback 结果回收进统一标准化流程，保证重试请求同样满足 provider 消息契约。
- `RimChat.AI.AIChatService`
  - `SendChatRequest(...)`
    - 同步请求链路新增相同消息标准化逻辑，避免旧入口出现 system-only 请求。
- `RimChat.Memory.DialogueSummaryService`
  - `TryQueueLlmFallback(...)`
    - `messages` 从单 system 改为 system+user。
    - `usageChannel` 从 `Unknown` 改为按 root 通道映射到 `DialogueUsageChannel.Diplomacy/Rpg`。
- `RimChat.Memory.RpgNpcDialogueArchiveManager.Sessions`
  - `BuildSessionSummaryRequestMessages(...)`
    - `rpg_archive_compression` 请求改为 system+user。
- `RimChat.Persistence.PromptPersistenceService.TemplateVariables`
  - `BuildCurrentFactionProfileVariableText(...)`
    - 好感读取改为 `TryGetGoodwillTowardPlayer(...)` 安全路径：玩家派系或自派系返回 `N/A`，异常时仅记录 warning 不抛出。

## Prompt Node Channel Guard + Fail-Fast Normalization（v0.7.23）

- `RimChat.Config.PromptUnifiedNodeSchemaCatalog`
  - 新增通道白名单接口：
    - `GetAllowedNodes(promptChannel)`
    - `IsNodeAllowedForChannel(promptChannel, nodeId)`
  - 节点编辑与运行时注入统一以通道白名单为单一事实源。
- `RimChat.Config.PromptUnifiedCatalog`
  - `ResolveNode(...)` 与 `ResolveNodeLayout(...)` 增加通道合法性校验，禁止读取不属于该通道的节点。
  - `NormalizeNodes(...)` / `NormalizeNodeLayout(...)` 增加通道约束清理：
    - 非法节点与布局会被移除并输出错误日志（fail-fast diagnostics）。
- `RimChat.Persistence.PromptPersistenceService.Hierarchical`
  - `GetOrderedNodeLayouts(promptChannel)` 改为按通道白名单过滤并补齐默认布局，运行时不再接受跨通道节点布局。
- `RimChat.Persistence.PromptPersistenceService.WorkbenchComposer`
  - 无用户布局时，默认节点布局改为按通道白名单生成，避免预览链路跨通道节点污染。
- `RimChat.Config.RimChatSettings_PromptSectionWorkspace`
  - Node 选择器与 Node 模式切换按当前通道白名单过滤；
  - 无节点通道自动回退到 Section 模式，阻断无效编辑入口。
- `RimChat.Config.RimChatSettings_RimTalkCompat`
  - `requiresLayoutSave` 判定从“全节点数”改为“通道允许节点数”，避免通道切换导致持续误判迁移。

## Workbench WYSIWYG Composer Merge（v0.7.22）

- `RimChat.Persistence.PromptPersistenceService.WorkbenchComposer`
  - 新增统一拼装接口：
    - `BuildUnifiedChannelSystemPrompt(rootChannel, promptChannel, scenarioContext, environmentConfig, additionalValues, payloadTag, payloadText)`
  - 统一返回单条 system prompt 成品，包含：
    - 固定 envelope（`<prompt_context>`）
    - main-chain sections 聚合渲染
    - node layout 落位渲染
    - payload 注入（可选）
- `RimChat.Persistence.PromptPersistenceService.SectionAggregates`
  - `BuildPromptWorkspaceStructuredSectionPreview(...)` 与 `BuildPromptWorkspaceStructuredLayoutPreview(...)` 改为调用统一 composer。
  - Workbench 预览语义改为确定性占位渲染，签名稳定可复现。
- `RimChat.Config.PromptSectionSchemaCatalog`
  - 新增通道归一化与归属接口：
    - `GetAllWorkspaceChannels()`
    - `NormalizeWorkspaceChannel(...)`
    - `NormalizeRuntimePromptChannel(...)`
    - `DoesChannelBelongToRoot(...)`
    - `ResolveRootChannel(...)`
- `RimChat.Config.RimChatSettings_RimTalkCompat`
  - Unified 迁移门控升级为 `MigrationVersion=2`。
  - 新增一次迁移入口：导入 legacy RPG 自定义文本与 legacy 图像模板到 Unified Catalog（含 image alias）。
- 旁路调用方改造为单 system：
  - `RimChat.DiplomacySystem.Social.SocialNewsPromptBuilder`
  - `RimChat.DiplomacySystem.GameComponent_RPGManager.PersonaBootstrap`
  - `RimChat.Memory.DialogueSummaryService`
  - `RimChat.Memory.RpgNpcDialogueArchiveManager.Sessions`
- 图像链路：
  - `RimChat.UI.Dialog_DiplomacyDialogue.ImageAction`
  - `RimChat.DiplomacySystem.ApiActionEligibilityService`
  - 模板解析统一走 `ResolvePromptTemplateAlias(...) / ResolvePreferredPromptTemplateAlias(...)`。

## Prompt Workbench Runtime-Aligned Validation + Structured Preview（v0.7.21）

- `RimChat.Persistence.TemplateVariableValidationContext`
  - 新增运行时一致校验上下文模型，统一管理“运行时已知变量 + 节点注入变量”。
  - 工作台可按 `rootChannel / promptChannel / sectionId|nodeId` 构造上下文，确保校验与运行时一致。
- `RimChat.Persistence.PromptPersistenceService.TemplateVariables`
  - 新增内部重载：
    - `ValidateTemplateVariables(string templateText, TemplateVariableValidationContext validationContext)`
  - 旧公开重载保持兼容并转发到上下文实现。
- `RimChat.Persistence.PromptWorkspacePreviewModels`
  - 新增 `PromptWorkspaceStructuredPreview`、`PromptWorkspacePreviewBlock`、`PromptWorkspacePreviewBlockKind`、`PromptWorkspacePreviewSubsection`。
- `RimChat.Persistence.PromptPersistenceService.SectionAggregates`
  - 新增：
    - `BuildPromptWorkspaceStructuredSectionPreview(rootChannel, promptChannel)`
    - `BuildPromptWorkspaceStructuredLayoutPreview(rootChannel, promptChannel, out placements)`
  - 预览块顺序固定：`Context -> Slot Nodes -> Main Sections -> Footer`。
  - 主链分段块会同时输出 section 级子分段列表，供右侧预览渲染次级标题。
- `RimChat.UI.PromptWorkspaceStructuredPreviewRenderer`
  - 新增轻量结构化预览渲染器，按 `signature + width` 缓存布局高度。
  - `SectionAggregate` 块支持按 section 渲染次级标题条与对应正文。
- `RimChat.Config.RimChatSettings_PromptSectionWorkspace`
  - 新增工作台编辑防抖持久化：500ms 空闲自动落盘；切换频道/分段/节点/模式与窗口关闭时强制落盘。
- `RimChat.UI.Dialog_PromptWorkbenchLarge`
  - 新增 `PreClose()`，关闭前强制调用工作台 flush。

## Prompt Node Layout + Slot Injector（v0.7.19）

- `RimChat.Config.PromptUnifiedCatalog`
  - `SchemaVersion` 升级到 `2`。
  - `PromptUnifiedChannelConfig` 新增 `NodeLayout`（`List<PromptUnifiedNodeLayoutConfig>`）。
  - 新增节点布局读写接口：
    - `ResolveNodeLayout(promptChannel, nodeId)`
    - `SetNodeLayout(promptChannel, nodeId, slot, order, enabled)`
    - `GetOrderedNodeLayouts(promptChannel)`
- `RimChat.Config.PromptUnifiedNodeLayoutConfig`
  - 字段：`NodeId`、`Slot`、`Order`、`Enabled`。
  - `Slot` 支持固定 5 槽位（字符串序列化值）：
    - `metadata_after`
    - `main_chain_before`
    - `main_chain_after`
    - `dynamic_data_after`
    - `contract_before_end`
- `RimChat.Config.RimChatSettings_RimTalkCompat`
  - 新增统一节点布局访问接口：
    - `GetPromptNodeLayouts(promptChannel)`
    - `ResolvePromptNodeLayout(promptChannel, nodeId)`
    - `SetPromptNodeLayout(promptChannel, nodeId, slot, order, enabled)`
    - `SavePromptNodeLayouts(promptChannel, layouts)`
- `RimChat.Persistence.PromptPersistenceService.Hierarchical`
  - 外交 / RPG / 策略三条主链从固定顺序改为“骨架 + 槽位注入”渲染。
  - 注入顺序固定：`slot -> order -> nodeId`。
  - 新增运行时节点落点模型：`ResolvedPromptNodePlacement`。
- `RimChat.Persistence.PromptPersistenceService.SectionAggregates`
  - 新增 `BuildPromptWorkspaceLayoutPreview(rootChannel, promptChannel, out placements)`，用于工作台完整预览与节点落点展示。

## Prompt Unified Catalog（v0.7.18）

- 新增统一提示词存储模型：
  - `PromptUnifiedCatalog`（`Channels + Sections + Nodes + SchemaVersion + MigrationVersion`）
- 新增节点 schema：
  - `PromptUnifiedNodeSchemaCatalog`
  - 运行时节点（如 `fact_grounding`、`decision_policy`、`turn_objective`、`strategy_output_contract`、`social_news_*`）统一从 node 解析。
- 新增统一存储 provider：
  - `PromptUnifiedCatalogProvider.LoadMerged()`
  - `PromptUnifiedCatalogProvider.SaveCustom(...)`
- 存储路径：
  - 默认：`Prompt/Default/PromptUnifiedCatalog_Default.json`
  - 自定义：`Prompt/Custom/PromptUnifiedCatalog_Custom.json`
- 兼容迁移：
  - 首次加载自动把 legacy `PromptSectionCatalog + PromptTemplates` 映射到 unified catalog，并写 `legacyMigrated` 标记。
- 对外构建签名保持不变：
  - `BuildFullSystemPrompt(...)`
  - `BuildDiplomacyStrategySystemPrompt(...)`
  - `BuildRPGFullSystemPrompt(...)`

## Prompt Workbench Quick Persona Actions（v0.7.17）

- `RimChat.Config.RimChatSettings_PromptSectionWorkspace`
  - Prompt Workbench 头部新增 `派系提示词 / 人设提示词` 快捷入口。
  - 仅在 `Current.Game != null && Current.ProgramState == Playing` 时可用；游戏外按钮禁用并显示提示。
- `RimChat.Config.RimChatSettings_PromptQuickActions`
  - 负责枚举当前存档真实派系，以及玩家殖民者/驯化动物/机械体实例。
  - 负责快捷入口冲突菜单、轻量编辑器打开、保存成功后的 `character_persona` 分段聚焦与结果提示。
- `RimChat.UI.Dialog_QuickPromptVariableRuleEditor`
  - 轻量编辑单个快捷规则，不暴露完整用户变量编辑器。
  - 保存时只更新规则，不自动把 token 写入当前 section 正文。
- `RimChat.Prompting.UserDefinedPromptVariableService.QuickActions`
  - 新增固定快捷变量槽位：
    - `system.custom.quick_faction_persona`
    - `system.custom.quick_pawn_persona`
  - 继续复用统一 `TrySaveEdit(...)` 校验与持久化链。
  - 固定路径已被现有用户变量占用时，支持 `ReuseExisting / TakeOver` 两种处理模式。
- `RimChat.Prompting.UserDefinedPromptVariableRuleMatcher`
  - `NameExact` 额外兼容 `thingid:*` 格式，允许 Pawn 快捷规则按真实实例 `ThingID` 精确命中。

### 行为约束

- 快捷入口只负责“创建/更新变量规则 + 展示 token + 跳转建议分段”，不会自动写入正文。
- 派系快捷入口写入 `Faction Rule`；Pawn 快捷入口写入 `Pawn Rule`。
- 运行时优先级保持不变：`pawn exact -> pawn conditional -> faction -> default -> empty`。

## Unified User Variable Rule Set + Safe Personality Export (v0.7.16)

- `RimChat.Config.UserDefinedPromptVariableConfig`
  - 用户变量根配置模型升级为 `Id / Key / DisplayName / Description / DefaultTemplateText / Enabled`。
  - 继续兼容旧 `templateText` 序列化字段，加载时自动迁入 `DefaultTemplateText`。
- `RimChat.Config.FactionPromptVariableRuleConfig`
  - 新增统一派系规则模型，持久化 `Id / VariableKey / FactionDefName / Priority / TemplateText / Enabled / Order`。
- `RimChat.Config.PawnPromptVariableRuleConfig`
  - 新增统一 Pawn 规则模型，持久化 `NameExact / FactionDefName / RaceDefName / Gender / AgeStage / TraitsAny / TraitsAll / XenotypeDefName / PlayerControlled / Priority / TemplateText / Enabled / Order`。
- `RimChat.Config.FactionScopedPromptVariableOverrideConfig`
  - 保留为 load-only 旧配置兼容模型；读取后自动迁移到新 faction rule 列表，保存不再写回旧字段。
- `RimChat.Prompting.UserDefinedPromptVariableService`
  - 负责统一规则迁移、归一化、保存校验、循环依赖检查、引用扫描、官方示例变量入口以及运行时规则解析。
  - 规则命中顺序固定为：`pawn exact -> pawn conditional -> faction -> default -> empty`。
  - 同层排序固定为：`Priority desc -> Specificity desc -> Order asc`。
- `RimChat.Prompting.UserDefinedPromptVariableRuleMatcher`
  - 负责 Pawn/Faction 规则匹配、具体度评分、命中层级标签与条件摘要生成。
- `RimChat.Prompting.UserDefinedVariableProvider`
  - 继续作为 `IPromptRuntimeVariableProvider` 接入 `PromptRuntimeVariableRegistry`，但运行时解析改为委托统一规则服务。
  - 在所有 `system.custom.*` 变量渲染完成后，追加 `pawn.personality` 的 effective export 覆盖链。
- `RimChat.Persistence.PromptPersistenceService`
  - 主 prompt 渲染路径继续通过 provider 流水线拿到有效 `pawn.personality` 导出值。
- `RimChat.DiplomacySystem.GameComponent_RPGManager`
  - RimTalk persona copy 模板渲染路径也接入统一自定义变量服务，确保 `pawn.personality` 能看到 effective personality。
- `RimChat.UI.Dialog_UserDefinedPromptVariableEditor`
  - 编辑器升级为“基础信息 / 默认模板 / 规则列表”结构，规则列表拆分为 `Faction Rules` 与 `Pawn Rules` 两个页签。
- `RimChat.Config.RimChatSettings_RimTalkVariableBrowser`
  - 变量浏览器新增“空白变量 + 官方示例变量”创建入口。

## Diplomacy Prompt Runtime Consolidation + XML-like Section Envelope（v0.7.14）

- `RimChat.Persistence.PromptPersistenceService`
  - 外交最终 system prompt 的唯一正式入口仍为 `BuildFullSystemPrompt(...)`，但运行时不再读取 `GlobalSystemPrompt / GlobalDialoguePrompt` 作为正式拼装源。
  - 新增 `BuildDiplomacyStrategySystemPrompt(...)`，用于策略建议链路的独立单条 system prompt。
  - `LoadConfig()` / `SaveConfig()` 现在会把外交旧字段回写为 compatibility mirror，并在 section catalog 仍为默认值时尝试一次性导入 legacy 外交 prompt。
- `RimChat.Prompting.Builders.DiplomacyPromptBuilder`
  - 继续负责普通外交 system prompt 组装调度，输出语义改为“唯一合法外交运行时入口”。
- `RimChat.Prompting.Builders.DiplomacyStrategyPromptBuilder`
  - 新增策略建议专用 builder，独立输出策略 JSON 合同、谈判者上下文、fact pack、scenario dossier 与 `diplomacy_strategy` section 主链。
- `RimChat.Persistence.DiplomacyStrategyPromptContext`
  - 新增 DTO，用于承载策略请求专属的谈判者上下文、事实包与场景 dossier 文本块。
- `RimChat.Persistence.PromptPersistenceService.Hierarchical`
  - 外交/RPG 运行时不再把 `GlobalSystemPrompt / GlobalDialoguePrompt` 节点塞进最终 prompt。
  - `main_prompt_sections` 改为真实 XML-like section 子节点，而不是 `[SECTION: ...]` 文本块。
  - 运行时成品 prompt 不再输出 `[CODE]` / `[FILE]` 源标签。
- `RimChat.Prompting.PromptHierarchyRenderer`
  - 层级渲染固定收口为 XML-like 输出；`UseHierarchicalPromptFormat` 仅保留为旧字段兼容镜像。
- `RimChat.AI.AIChatServiceAsync`
  - 删除发送前 `Think step by step.` / `Review your rules.` 追加逻辑，网络层改为只透传 builder 产物。
- `RimChat.UI.Dialog_DiplomacyDialogue`
  - 普通外交请求不再追加 `PLAYER NEGOTIATOR CONTEXT` 第二条 system。
- `RimChat.UI.Dialog_DiplomacyDialogue.Strategy`
  - 策略建议请求改为只发送 1 条独立 strategy system prompt，再附历史消息与最终 user 触发语。
- 默认资产：
  - `Prompt/Default/PromptSectionCatalog_Default.json`
    - 成为外交/策略长规则文本的正式主源。
  - `Prompt/Default/SystemPrompt_Default.json`
    - `GlobalSystemPrompt` 改为 compatibility mirror 提示文本，不再承载正式运行时规则包。

## Default Prompt Variable-System Consolidation + Social Circle Workspace Channel（v0.7.13）

- `RimChat.Config.PromptSectionSchemaCatalog`
  - 外交 root 下的 section 工作台子通道新增 `social_circle_post`，现在会参与 section 编辑与 aggregate 预览。
- `RimChat.Config.SocialCirclePromptDefaultsProvider`
  - 新增默认社交圈模板提供器，统一从 `Prompt/Default/SocialCirclePrompt_Default.json` 读取社交圈新闻模板与 `publish_public_post` 默认动作定义。
- `RimChat.Config.PromptTextConstants`
  - `publish_public_post` 描述/参数/要求与社交圈新闻模板默认值不再以代码常量作为长期来源，改为委托默认配置文件。
- `RimChat.Prompting.PromptEntryStaticTextCatalog`
  - 外交主链默认段落不再重复内嵌一份 fallback 文案，改为优先解析 `PromptSectionCatalog` / 默认 section catalog。
- `doc/PromptVariableGapReport.md`
  - 新增只读变量缺口清单，记录旧 default prompt 中尚未进入现有 namespaced variable system 的语义位点。

## Prompt Editor Invalid-Namespace Validation Hardening（v0.7.12）

- `RimChat.Persistence.PromptPersistenceService.ValidateTemplateVariables(...)`
  - 编辑态只把 `ctx / pawn / world / dialogue / system` 五个命名空间下的变量路径送入 validation context。
  - 非法命名空间或手动输入过程中的半成品变量不再触发页面级异常，而是作为未知变量留在诊断结果里。

## Prompt Workspace Variable Insert Routing Fix（v0.7.11）

- `RimChat.Config.RimChatSettings_PromptSectionWorkspace`
  - 工作台右侧变量面板的插入回调现在直接调用 `TryInsertVariableTokenToPromptWorkspace(...)`，目标固定为当前 section 文本。
  - `TryInsertVariableTokenToPromptWorkspace(...)` 的可插入判断改为基于当前 workspace 的有效 `promptChannel + sectionId`，不再依赖旧页签索引状态。

## Prompt Workbench Preview Cache Tightening（v0.7.10）

- `RimChat.UI.PromptWorkbenchChipEditor`
  - `DrawReadOnly(...)` 现在缓存只读分段布局结果，缓存键为“源文本 + 视口宽度”。
  - 缓存内容包含：分段块、每段高度与总内容高度，避免滚动/悬停阶段重复 `ParseSegments(...)` 与 `CalcHeight(...)`。
- `RimChat.Config.RimChatSettings_PromptSectionWorkspace`
  - 当前通道的 aggregate 预览文本现在在工作台层缓存。
  - 失效条件：section 文本变更、根通道变更、prompt channel 变更、整份 `PromptSectionCatalog` 被替换。

## Prompt Workbench Preview Variable Standalone-Line Rendering（v0.7.9）

- `RimChat.UI.PromptWorkbenchChipEditor`
  - `DrawReadOnly(...)` 改为只读分段渲染路径，不再复用编辑态的“整块 TextArea + 分片覆盖高亮”方案。
  - 普通文本段继续按当前视口宽度自然换行。
  - 命中的 `{{ namespace.path }}` 变量段会被提升为独立渲染块，强制前后断开，并保持 tooltip 能力。
  - 连续变量按“一颗变量一行”显示；中间编辑器 `Draw(...)` 行为不变。

## Safe RimTalk Baseline Scriban Coverage（v0.7.6）

- `RimChat.Prompting.RimChatCoreVariableProvider`
  - 新增安全变量目录：`world.time.hour/day/quadrum/year/season/date`、`world.weather`、`world.temperature`、`pawn.recipient`、`pawn.recipient.name`。
- `RimChat.Persistence.PromptPersistenceService`
  - `ResolveTemplateVariableValue(...)` 增加原生时间/季节/天气/温度/收件者别名解析，全部从当前 `Map`、`TickManager`、`DialogueScenarioContext.Target` 直接读取。
- 兼容边界：
  - 保持 strict 命名空间合同，不恢复 RimTalk 的 `Find`、`settings`、静态类、工具函数与大小写宽松访问。
  - 现有 `dialogue.rimtalk.*` / `pawn.rimtalk.context` 兼容变量继续保留，旧行为不变。

## Prompt Tab Large Window Routing（v0.7.5）

- `RimChat.Config.RimChatSettings_PromptAdvancedFramework`
  - `OpenPromptWorkbenchWindow(...)` 从“设置页切 tab”改为“打开大尺寸独立窗口”。
  - 打开前会检测 `Dialog_PromptWorkbenchLarge` 是否已存在，避免重复叠加同类窗口。
- `RimChat.UI.Dialog_PromptWorkbenchLarge`
  - 新增大尺寸 Prompt 工作区弹窗，窗口初始尺寸按屏幕 `90%` 自适应并做上下限约束。
  - 弹窗内容复用 `RimChatSettings.DrawTab_PromptSettingsDirect(...)`，保持现有提示词分段工作区渲染链不变。
- `RimChat.Config.RimChatSettings`
  - `DrawTab_PromptSettingsDirect(Rect rect)` 访问级别提升为 `internal`，用于被独立弹窗安全复用。

## Prompt Compat Final Closure（v0.7.0）

- `RimChat.Config.RimChatSettings`
  - 正式 live 字段只保留 `PromptSectionCatalog`、`RimTalkSummaryHistoryLimit`、`RimTalkPersonaCopyTemplate`、`RimTalkAutoPushSessionSummary`、`RimTalkAutoInjectCompatPreset`。
  - 旧 `EnableRimTalkPromptCompat / RimTalkCompatTemplate / RimTalkDiplomacy / RimTalkRpg / RimTalkChannelSplitMigrated` 改为加载时临时 legacy payload，仅用于导入，不再参与正式保存与稳定 UI。
- `RimChat.Config.PromptLegacyCompatMigration`
  - 升级为唯一 legacy 导入入口，统一处理 settings / preset / bundle / custom store 四类旧 payload。
  - 新增 `LegacyPromptMigrationReport`、`LegacyPromptMigrationEntry`，并把最新导入结果覆盖写入 `Prompt/Reports/LegacyPromptMigrationReport.json`。
- `RimChat.Config.PromptSectionSchemaCatalog`
  - 统一声明主链固定 8 段 schema，以及 Prompt 页稳定工作区允许编辑的主链 prompt channel。
- `RimChat.Prompting.PromptSectionAggregateBuilder`
  - 新增 canonical aggregate builder，直接按 `PromptSectionCatalog` 生成当前 prompt channel 的 section aggregate，供 runtime 与 UI 预览共用。
- `RimChat.Persistence.PromptPersistenceService.SectionAggregates`
  - 外交与 RPG 主链现在统一渲染一份 canonical section aggregate，并在 hierarchical builder 中只插入一次。
- `RimChat.Config.RimChatSettings_PromptSectionWorkspace`
  - Prompt 页稳定入口改为 `root channel -> prompt channel -> sectionId` 工作区。
  - 仅支持按 section / 当前 prompt channel 恢复默认，变量插入仅写当前焦点 section 文本，预览显示当前通道 canonical aggregate。
- `RimChat.Config.RimChatSettings_RimTalkBridgePage`
  - RimTalk 页只保留 `Bridge / Variables / Summary & Persona`。
- `RimChat.Prompting.PromptVariableDisplayEntry`
  - 变量 UI 统一改为中性显示模型：`path / scope / sourceId / sourceLabel / availability / description`。
- 默认资产：
  - 主入口：`Prompt/Default/PromptSectionCatalog_Default.json`
  - 单版本兼容回退：`Prompt/Default/RimTalkPromptEntries_Default.json`

## RimTalk 变量桥接 Provider（v0.6.35）

- `RimChat.Prompting.IPromptRuntimeVariableProvider`
  - 新增职责：除 `GetDefinitions()`、`PopulateValues(...)` 外，还支持 `TryMapLegacyToken(...)`，用于把旧 token 重写到 RimChat 命名空间。
- `RimChat.Prompting.RimTalkVariableProvider`
  - 提供首批旧语义桥接变量：
    - `pawn.rimtalk.context`
    - `dialogue.rimtalk.prompt`
    - `dialogue.rimtalk.history`
    - `dialogue.rimtalk.history_simplified`
  - 若 RimTalk API 注册了 context/pawn/environment 变量，会自动映射到：
    - `dialogue.rimtalk.*`
    - `pawn.rimtalk.*`
    - `world.rimtalk.*`
- `RimChat.Prompting.RimTalkMemoryPatchVariableProvider`
  - 负责承接 `modId/name` 命中 `memorypatch` 的注册变量，并映射到同一命名空间规则。
- 迁移规则：
  - `{{context}} -> {{pawn.rimtalk.context}}`
  - `{{prompt}} -> {{dialogue.rimtalk.prompt}}`
  - `{{chat.history}} -> {{dialogue.rimtalk.history}}`
  - `{{chat.history_simplified}} -> {{dialogue.rimtalk.history_simplified}}`
  - `{{json.format}} ->` 迁移为当次解析得到的 JSON 指令正文
- UI 规则：
  - 变量选择器与变量浏览器现在显示 `SourceLabel` 与依赖状态；
  - provider 当前不可用时，变量仍可显示，但标记为“运行时依赖缺失”。

## Prompt Section Catalog Native Migration（v0.6.34）

- `RimChat.Config.RimTalkPromptEntryDefaultsConfig`
  - 现在兼任原生 section 配置模型，新增 clone / `IExposable` / `SetContent(...)` 能力，作为 `PromptSectionCatalog` 的统一数据载体。
- `RimChat.Config.PromptLegacyCompatMigration`
  - 新增 `NormalizePromptSections(...)`、`CreateLegacyAdapterFromPromptSections(...)`、`ApplyLegacyAdapterToPromptSections(...)`。
  - legacy `PromptEntries` / `CompatTemplate` 现在只允许导入到 section catalog；若模板含未知裸变量或污染型已渲染 prompt，会拒绝迁移并回退默认 section，同时写 `Player.log`。
- `RimChat.Config.RimChatSettings`
  - 新增 `PromptSectionCatalog` 持久化字段，以及 `GetPromptSectionCatalogClone()`、`SetPromptSectionCatalog(...)`、`ResolvePromptSectionText(...)`。
  - `GetRimTalkChannelConfig(...)` / `SetRimTalkChannelConfig(...)` 改为 legacy adapter，面向 UI/导入适配，不再代表正式运行时状态。
- `RimChat.Prompting.PromptEntryStaticTextCatalog`
  - `DiplomacyDialogueRequest.*` 由 section catalog 解析，并在迁移阶段直接内联进模板；`dialogue.diplomacy_dialogue.*` 不再作为运行时 Scriban 变量暴露。
- `RimChat.Config.PromptPresetService` / `RimChat.Persistence.PromptBundleConfig` / `RimChat.Config.RpgPromptCustomConfig`
  - preset、bundle、RPG custom store 均新增 `PromptSectionCatalog` 同步持久化入口，保存/导入时先归一化原生 section，再清空 compat 结构。

## Prompt Compatibility Runtime Removal（v0.6.33）

- `RimChat.Persistence.PromptPersistenceService`
  - `BuildFullSystemPromptHierarchical(...)` 与 `BuildRpgSystemPromptHierarchical(...)` 现在固定走 RimChat 原生层级构建，不再调用 RimTalk entry-driven 兼容入口。
- `RimChat.Prompting`
  - 新增 `IPromptRuntimeVariableProvider`、`PromptRuntimeVariableRegistry`、`RimChatCoreVariableProvider`、`RimTalkVariableProvider`、`RimTalkMemoryPatchVariableProvider`。
  - `PromptVariableCatalog` 从静态常量集合改为基于 provider registry 聚合。
- `RimChat.Config` / `RimChat.Persistence`
  - legacy `RimTalkChannelCompatConfig` 只保留迁移/导入用途，不再属于正式运行时 prompt 装配接口。

## Prompt Workbench Preset Activation Persistence Fix（v0.6.32）

- `RimChat.Config.PromptPresetService`
  - 调整 `Activate(...)`：激活预设后不再立即把刷新后的编辑器状态再次回写到磁盘，避免刚应用的新 payload 被旧文件重载结果覆盖。
  - 调整 `ApplyPayloadToSettings(...)`：
    - `persistToFiles = true` 时，除 `Prompt/Custom/SystemPrompt_Custom.json`、`DiplomacyDialoguePrompt_Custom.json`、`SocialCirclePrompt_Custom.json`、`FactionPrompts_Custom.json` 外，
    - 还会把 `RpgPromptCustomConfig` 相关字段按当前 payload 重建后写回 `Prompt/Custom/PawnDialoguePrompt_Custom.json`，确保 RPG/RimTalk 兼容层与预设显式 payload 一致。
  - 调整 `WriteIfNotNull(...)`：当 payload 为空白时会删除旧 custom 文件，而不是静默保留旧文件残留。
- 结果：
  - `Default -> Migrated -> Default` 切换时，中间 “条目内容（Scriban）” 与右侧 “预览” 会回到各自预设的真实内容；
  - 迁移生成的 `Migrated` 不再把 `Default` 的运行态覆盖成同一份内容；
  - Prompt Workbench 预设激活链对 `Prompt/Custom/*` 的写盘结果与内存态重新收敛。

## Prompt Workbench Canonical Default Preset Bootstrap（v0.6.26）

- `RimChat.Config.PromptPresetService`
  - 新增 canonical `Default` 预设引导逻辑：首次创建预设仓库时，不再从当前 legacy/runtime 状态捕获 `Default`。
  - canonical payload 来源：
    - `Prompt/Default/SystemPrompt_Default.json`
    - `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
    - `Prompt/Default/PawnDialoguePrompt_Default.json`
    - `Prompt/Default/SocialCirclePrompt_Default.json`
    - `Prompt/Default/FactionPrompts_Default.json`
    - `RimChatSettings.CreateCanonicalDefaultRimTalkChannelConfig(...)`
  - 升级兼容：
    - 若当前 legacy payload 与 canonical payload 存在实质差异，会追加 `Migrated` 预设保存旧提示词；
    - `Default` 永远保留为 canonical 默认内容。
- `IPromptPresetService.ApplyPayloadToSettings(...)`
  - 输入：`RimChatSettings`、预设 payload、`persistToFiles`。
  - 行为：
    - `persistToFiles = true`：沿用原有激活路径，写回 `Prompt/Custom/*` 并刷新编辑器状态；
    - `persistToFiles = false`：仅把 active preset 同步到当前设置对象，不覆写现有自定义文件。
- `RimChatSettings_PromptAdvancedFramework.EnsurePresetStoreReady()`
  - 新行为：首次加载 preset store 后，先把 active preset 同步到当前 workbench 编辑态，再设置选中预设 ID。
  - 结果：Prompt Workbench 首次打开时，左侧预设选择与右侧条目内容不再分叉。

## Prompt Workbench Fixed Body Editor + Vertical Scroll Contract（v0.6.25）

- `RimChat.UI.PromptWorkbenchChipEditor`
  - 输入：固定 `Rect`、当前文本、滚动状态。
  - 输出：编辑后的原始模板文本（仍写回 `RimTalkPromptEntryConfig.Content`）。
  - 行为：
    - Workbench 正文区固定高度显示，不再随文本内容视觉扩张；
    - 文本区改为自动换行；
    - 仅保留纵向滚动为主交互，滚动状态会在切换条目时重置；
    - 变量 token 胶囊高亮与 tooltip 结构保持不变。
- `RimChatSettings_RimTalkTab.DrawRimTalkPromptEntryEditor(..., bool useChipEditor = false)`
  - Workbench 正文区继续使用既有 `contentRect` 与校验状态栏布局。
- `RimChatSettings_RimTalkTab.DrawLegacyPromptEntryTextArea(...)`
  - Workbench fallback 文本框与 chip editor 对齐为“固定高度 + 自动换行 + 纵向滚动”，避免软限制回退后交互突变。

## Prompt Workbench Variable Chip Editor + Unified Tooltip Contract（v0.6.24）

- `RimChat.UI.PromptWorkbenchChipEditor`
  - 输入：`Rect`、当前文本、滚动状态。
  - 输出：编辑后的原始模板文本（仍写回 `RimTalkPromptEntryConfig.Content`）。
  - 行为：
    - 仅识别 `PromptVariableCatalog` 白名单内的完整变量 token；
    - 有效 token 绘制为胶囊底色；
    - 单击选中胶囊，双击进入 token 原文编辑，Backspace/Delete 删除整 token。
- `PromptVariableTokenScanner.ParseSegments(...)`
  - 严格匹配：`{{ namespace.path }}`（必须命中变量白名单）。
  - 输出：`PromptTokenSegment`（`Text`/`VariableToken`）供 UI 渲染层使用。
- `PromptVariableTooltipCatalog.Resolve(...)`
  - 输出：`PromptVariableTooltipInfo`（`name/dataType/description/typicalValues` 静态信息）。
  - 用途：统一工作台变量侧栏与编辑器胶囊悬浮信息内容结构。
  - 规则：优先返回变量专属说明与显式典型值；若缺少专属元数据，再按通用规则推断。
- `RimChatSettings_RimTalkTab.DrawRimTalkPromptEntryEditor(..., bool useChipEditor = false)`
  - 新增参数：`useChipEditor`，用于将胶囊编辑器限定在 Prompt Workbench 路径。

兼容保证：持久化字段与渲染主链不变，`entry.Content` 仍保存原始模板文本。

## 全通道默认条目内容源 + 严格变量种子（v0.6.23）

- 默认条目内容源：
  - 新增 `Prompt/Default/RimTalkPromptEntries_Default.json`，按 `PromptChannel + SectionId` 提供默认正文。
  - 新增 `RimTalkPromptEntryDefaultsProvider.ResolveContent(promptChannel, sectionId)` 供工作台默认条目重建读取。
- 条目重建行为：
  - `RimChatSettings.BuildCanonicalSectionEntry(...)` 在条目内容为空时，优先从默认 JSON 填充对应段落内容。
  - `RimChatSettings_RimTalkTab.TryRestoreDefaultEntriesForScopedChannel(...)` 改为恢复“结构 + 默认正文”，不再仅恢复结构。
- Strict Scriban 变量种子：
  - `PromptPersistenceService.BuildSharedPromptTemplateVariables(...)` 先按 `PromptVariableCatalog.GetAll()` 预置全量命名空间变量为空字符串，再覆盖当前上下文值。
  - 目标：避免 entry-driven 渲染因“存在白名单变量但未赋值”导致的 strict 失败。
- 兼容策略变更（破坏式）：
  - 停用条目到旧字段的保存回写入口：
    - `RimChatSettings.SaveRpgPromptTextsToCustom(...)` 不再调用 `SyncLegacyPromptFieldsFromEntryChannels()`。
    - `RimChatSettings_Prompt.SaveSystemPromptConfig()` 不再调用 `SyncLegacyPromptFieldsFromEntryChannels()`。
  - 说明：旧字段读取链保留，但保存阶段不再保证与新条目系统双向同步。

## Prompt Workbench Scoped Channel Contract（v0.6.22）

- `RimChatSettings_RimTalkTab.DrawRimTalkPromptEntryList(...)`
  - Entry list now operates on a scoped channel subset when workbench mode is active.
  - Add/duplicate/delete/reorder are constrained to scoped visible entries only.
- `RimChatSettings_RimTalkTab.DrawRimTalkPromptEntryEditor(...)`
  - Editor selection is normalized to scoped channel visibility before editing.
- `RimChatSettings_RimTalkTab.EnsureRimTalkEditableEntry(...)`
  - New entry default channel now resolves from active scoped channel in workbench mode.
- Behavior guarantee:
  - Channel-scoped editing does not mutate entry ordering/content in other channels.

## Prompt Workbench Canonical Section Schema（v0.6.21）

- `RimTalkPromptEntryConfig`
  - Added persistent field: `SectionId` (string, default empty).
  - Purpose: stable section identity independent of localized name text.
- `RimChatSettings.EnsurePromptEntrySeedForChannel(...)`
  - Extended coverage flow: after seed synchronization, each selectable channel is normalized to a canonical 8-section layout.
  - Canonical section names:
    - `System Rules`
    - `Character Persona`
    - `Memory System`
    - `Environment Perception`
    - `Context`
    - `Action Rules`
    - `Repetition Reinforcement`
    - `Output Specification`
- Runtime behavior contract:
  - Legacy layouts are rebuilt into canonical sections once per channel shape detection.
  - Missing sections are auto-created.
  - Non-canonical extra sections are removed during normalization.
  - Canonical section names are enforced in English; manual list reordering remains allowed.

## Scriban Engine Contract（Step 2 Breaking Update）

- 主渲染入口：
  - `RimChat.Prompting.IScribanPromptEngine.RenderOrThrow(templateId, channel, templateText, context)`
- 运行时契约：
  - 仅允许命名空间变量：`ctx.* / pawn.* / world.* / dialogue.* / system.*`
  - Parse/Render/Unknown variable/Null object access 必须抛出 `PromptRenderException`
  - 禁止 prompt 渲染失败后回退原文或空串透传
- 迁移契约：
  - Schema 升级由 `PromptTemplateAutoRewriter` 执行一次性重写与验证
  - 失败模板标记 `Blocked` 并抛 `PromptRenderException(ErrorCode=1200)`
  - 缺失必需模板文本时抛 `PromptRenderException(ErrorCode=1201, TemplateMissing)`
- 场景模板链路：
  - `PromptPersistenceService.RenderTemplateVariables(...)` 已切到 Scriban 严格渲染，不再执行旧正则替换渲染
- 运行时桥接状态：
  - `RimTalkCompatBridge` 已从 RimChat 代码库物理移除
- 可观测性：
  - `ScribanPromptEngine` 内置 LRU 编译缓存（固定容量）与渲染遥测
  - `Dialog_ApiDebugObservability` 展示缓存命中率、命中/未命中/淘汰计数、平均编译耗时、平均渲染耗时

> 注意：本文档下方旧版本历史条目若出现 “fallback” 描述，仅代表历史行为；v0.6.15+ 运行时以本节 strict 契约为准。

## RimTalk 条目通道接管 + 工作台失效区修复（v0.6.18）

- `RimTalkPromptEntryConfig`
  - 新增持久化字段：`PromptChannel`（字符串，默认 `any`，写盘前归一化）。
- `RimTalkPromptEntryChannelCatalog`
  - 新增通道目录 API：`GetSelectableChannels(...)`、`NormalizeForRoot(...)`、`MatchesRuntimeChannel(...)`、`GetSeedDefinitions(...)`。
  - 通道覆盖：`外交对话 / RPG对话 / 外交策略 / 主动外交 / 主动RPG / 社交圈推文 / 人格初始化 / 摘要生成 / RPG归档压缩 / 图像生成`。
- `RimChatSettings.LoadRpgPromptTextsFromCustom(...)` / `EnsurePromptEntrySeedForChannel(...)`
  - 旧字段迁移后会自动补齐缺失通道条目，并按种子策略设置默认启用状态，保持旧存档兼容。
- `PromptPersistenceService.TryBuildEntryDrivenChannelPrompt(...)`
  - 运行时新增严格前置：当 `EnablePromptCompat == false` 时直接退出条目注入链路，回退标准分层 Prompt。
  - 条目筛选新增通道匹配：仅注入 `Enabled && Content 非空 && PromptChannel 匹配当前运行通道/模式` 的条目。
- `RimChatSettings_RimTalkTab.DrawRimTalkPromptEntryEditor(...)`
  - 编辑器从 `Role/Position` 入口切换为 `PromptChannel` 入口，修复“控件可点但运行不生效”的错配问题。
- `RimChatSettings_PromptAdvancedFramework.GetWorkbenchEditingChannelConfig(...)`
  - 工作台引入编辑态配置缓存，避免每帧 clone/set 导致的文本区输入回滚。

## Persona Strict Chain + RimTalk Diagnostics Closure（v0.6.17）

- `GameComponent_RPGManager.PersonaBootstrap.BuildPersonaBootstrapPrompt(...)` / `RenderPersonaBootstrapTemplate(...)`
  - 从字符串替换链切换为 `PromptTemplateRenderer.RenderOrThrow(...)` strict Scriban 渲染。
- `GameComponent_RPGManager.PersonaBootstrap.RenderPersonaCopyTemplateOrThrow(...)`
  - 人格 copy 渲染失败或空结果改为直接抛 `PromptRenderException` 并中断链路（无 silent fallback）。
- `RimChatSettings_RimTalkTab.DrawRimTalkPromptEntryEditor(...)`
  - 条目内容编辑器新增实时 Scriban 诊断状态（错误码 + 行列 + 未知变量）。
- `RimChatSettings_PromptAdvancedFramework.DrawWorkbenchMainPanel(...)`
  - 提示词工作台中的“条目内容（Scriban）”保留固定高度多行编辑框与框内纵向滚动；变量 token 继续提供胶囊高亮与 tooltip，但样式回收为无描边的原始观感，同时保持不覆盖相邻字符。
- `RimChatSettings_RimTalkTab.TryInsertVariableIntoFocusedEditor(...)`
  - 当工作台向已聚焦的条目内容编辑框插入完整变量 token 时，会自动补齐 token 前后缺失空格，减少胶囊与相邻文本粘连。
- `PromptWorkbenchChipEditor.DrawChipLabel(...)`
  - 胶囊文字层使用 `new Color(184f/255f, 230f/255f, 184f/255f, 1f)` 作为字体颜色，与变量胶囊的绿色视觉一致。
- `RimChatSettings_RimTalkTab.DrawRimTalkChannelTemplateTextArea(...)`
  - 通道模板文本区新增实时 Scriban 诊断状态。
- `RimChatSettings_RimTalkTab.DrawRimTalkPersonaCopyTemplateEditor(...)`
  - Persona copy 模板编辑区新增实时 Scriban 诊断状态。

## Prompt Workbench Hit-Area Reliability Fix（v0.6.14）

- `RimChatSettings_RimTalkTab.DrawRimTalkPromptEntryList(...)`
  - Expanded interactive hit area to full-row selection body.
  - Restored top duplicate shortcut (`⧉`) and duplicate naming collision handling.
- `RimChatSettings_RimTalkTab.DrawRimTalkPromptEntryEditor(...)`
  - Added responsive fallback: when horizontal space is insufficient, Role/Position actions switch to stacked vertical layout.
- `RimChatSettings_RimTalkVariableBrowser.DrawRimTalkWorkbenchVariableRow(...)`
  - Added explicit row-level `Insert` button in workbench variable panel while keeping click-to-insert on row body.
- Compatibility notes:
  - No breaking save/schema migration.
  - Legacy prompt files and old fields remain readable.

## Prompt Workbench Button Response Fix（v0.6.13）

- `RimChatSettings_PromptAdvancedFramework.OpenPromptWorkbenchWindowForRpg(...)`
  - Added RPG-specific workbench opening path to avoid channel-reset behavior from RPG entry points.
- `RimChatSettings_AI.RpgDialogue.DrawRpgNonPromptSettings(...)`
  - RPG runtime settings now open Prompt Workbench via RPG-channel entry.
- `RimChatSettings_RimTalkTab.DrawTab_RimTalk(...)`
  - RimTalk migration tab now opens Prompt Workbench via RPG-channel entry.
- `RimChatSettings_PromptAdvancedFramework.TryActivatePresetById(...)`
  - Added centralized preset activation flow with explicit failure handling and localized failure message.
- `RimChatSettings_PromptAdvancedFramework.ShowImportPresetDialog(...)` / `ShowExportPresetDialog(...)`
  - Added localized success feedback for import/export actions.
- `RimChatSettings_PromptAdvancedFramework.DrawPresetActions(...)` / `DrawPresetBottomActions(...)`
  - Added localized success feedback for create/duplicate/rename/delete actions.
- Compatibility notes:
  - No breaking save/schema migration.
  - Legacy prompt files and old fields remain readable.

## Prompt Workbench Interaction Fix + RimTalk Variable UI Port（v0.6.12）

- `RimChatSettings_PromptAdvancedFramework.DrawWorkbenchVariables(...)`
  - Workbench variable side panel now uses a dedicated Rect-driven renderer instead of nested `Listing_Standard`, avoiding hit-area mismatch and dead-click zones.
- `RimChatSettings_RimTalkVariableBrowser.DrawRimTalkWorkbenchVariableBrowser(...)`
  - Added RimTalk-style variable panel flow: search, grouped variable sections, full-row click insertion, tooltip metadata.
- `RimChatSettings_RimTalkTab.DrawRimTalkPromptEntryList(...)`
  - Entry list interaction aligned with RimTalk: inline enable checkbox, inline delete button, and up/down reorder controls.
- `RimChatSettings_PromptAdvancedFramework.DrawPresetList(...)`
  - Selecting a preset row now activates/applies that preset immediately so editor content switches with selection.
- Compatibility notes:
  - No breaking save/schema migration.
  - Legacy prompt files and old fields remain readable.

## Prompt Workbench RimTalk Fidelity Alignment（v0.6.11）

- `RimChatSettings_PromptAdvancedFramework.DrawWorkbenchBody(...)`
  - Rebalanced workbench geometry to RimTalk-like proportions: narrow left rail + right workspace split into editor and side panel.
- `RimChatSettings_PromptAdvancedFramework.DrawWorkbenchPresetPanel(...)`
  - Reorganized left rail into compact preset/entry workflow and removed generic prompt action buttons that were not part of RimTalk workbench UX.
- `RimTalkPromptEntryConfig`
  - Added `CustomRole` field for explicit custom-role persistence in entry-level editing.
- `RimChatSettings_RimTalkTab.DrawRimTalkPromptEntryEditor(...)`
  - Fixed `Custom Role` textbox binding: now writes to `entry.CustomRole` rather than overriding `entry.Role`.
- Compatibility notes:
  - No breaking save/schema migration.
  - Legacy prompt files missing `CustomRole` stay valid and fallback to empty value.

## Mod Settings Icon Namespace Isolation（v0.6.10）

- `About/About.xml`
  - `modIconPath` is now `UI/RimChat/Logo` instead of generic `UI/Logo`.
- `1.6/Textures/UI/RimChat/Logo.png`
  - Added namespaced logo asset for mod settings/mod list icon resolution.
- Compatibility notes:
  - Legacy `1.6/Textures/UI/Logo.png` is preserved.
  - No save schema changes.
  - No prompt-file schema changes.

## Comms Toggle Icon Namespace Isolation（v0.6.9）

- `PlaySettingsPatch_CommsToggleIcon.ResolveCommsToggleIcon()`
  - Icon loading now prefers unique resource path `UI/RimChat/CommsToggleIcon` and falls back to legacy `UI/CommsToggleIcon`.
- `1.6/Textures/UI/RimChat/CommsToggleIcon.png`
  - Added dedicated runtime icon asset under a namespaced path to avoid cross-mod texture path collisions.
- Compatibility notes:
  - No save schema changes.
  - No prompt-file schema changes.
  - Legacy icon path remains supported for older distributions.

## RimTalk Entry List Interaction Polish（v0.6.8）

- `RimChatSettings_RimTalkTab.DrawRimTalkPromptEntryList(...)`
  - 条目列表改为双行渲染（名称 + 角色/位置），并对长文本做安全截断与 tooltip 完整显示。
- `RimChatSettings_RimTalkTab.DrawRimTalkPromptEntryEditor(...)`
  - 启用/角色/位置控件布局改为自适应宽度，避免窄宽度下按钮重叠与不可点击区域。
- `1.6/Languages/*/Keyed/RimChat_Keys.xml`
  - 补充 `RimChat_Import` 与 `RimChat_Export` 语言键，修复工作台头部按钮 key 回退导致的文本截断。
- 兼容说明：
  - 本次仅 UI 交互与本地化键补全，不改存档结构与提示词文件 schema。

## Prompt Workbench Variable Browser UX + Perf Cache（v0.6.7）

- `RimChatSettings_RimTalkVariableBrowser.DrawRimTalkTabVariableBrowser(...)`
  - 变量浏览器改为“可选择列表 + 选中详情”结构，支持行选中高亮并保留插入动作。
- `RimChatSettings_RimTalkVariableBrowser.GetFilteredRimTalkVariables(...)`
  - 新增变量快照节流缓存（1.2 秒）与搜索结果缓存，减少每帧反射抓取与重复排序。
- `RimChatSettings_RimTalkVariableBrowser.DrawRimTalkVariableDetails(...)`
  - 新增变量详情区，展示完整 token、分组和描述，降低长文本截断带来的误判。
- 架构调整：
  - 变量浏览器逻辑从 `RimChatSettings_RimTalkTab.cs` 抽离到 `RimChatSettings_RimTalkVariableBrowser.cs`（partial）。
- 兼容说明：
  - 本次仅 UI 交互和渲染性能优化；不改存档字段、不改提示词文件 schema，保持旧版本兼容。

## Prompt Workbench Variable Insert & Seed Split（v0.6.6）

- `RimChatSettings_PromptAdvancedFramework.DrawWorkbenchVariables(...)`
  - 工作台右侧变量面板改为复用 RimTalk 变量浏览器渲染路径。
- `RimChatSettings_RimTalkTab.AppendVariableToCurrentRimTalkTemplate(...)`
  - 变量插入策略改为“光标优先插入，失焦回退追加”。
- `RimChatSettings_PromptEntrySeedImport`
  - 新增 legacy 拼接文本拆分器，按段标题生成多条种子条目，服务 `BuildLegacyPromptEntries(...)`。
- 兼容说明：
  - 仅在“无有效条目”迁移路径下触发 seed 拆分，不覆盖已有用户条目。

## Prompt Workbench Prototype Refresh + Single Tab Entry（v0.6.5）

- 设置页导航接口变更（`RimChatSettings`）：
  - 顶层Tab调整为 `API / ModOptions / PromptWorkbench / ImageApi`。
  - 原 `Prompts / RPG / RimTalk` 顶层入口不再暴露。
- Prompt Workbench 入口行为：
  - 点击 `PromptWorkbench` Tab 直接调用 `OpenPromptWorkbenchWindow()` 弹出独立窗口；
  - 入口动作不强制修改当前设置页内容上下文。
- Prompt Workbench UI交互变更（`RimChatSettings_PromptAdvancedFramework`）：
  - 主通道固定为 `Diplomacy` / `RPG`；
  - RPG 通道新增二级切换：`Common Entries` / `Pawn Persona`；
  - 右侧工具区改为面板切换模式：`Preview` / `Variables` / `Help`；
  - `Variables` 面板复用 RimTalk 变量浏览器（搜索、分组、插入到当前条目）。
- 变量插入行为对齐 RimTalk（`RimChatSettings_RimTalkTab`）：
  - 优先按当前编辑器光标位置插入变量；
  - 若编辑器未聚焦，回退为末尾追加插入，保持兼容。
- legacy 条目种子导入补全（`RimChatSettings_PromptEntrySeedImport`）：
  - 当旧配置仅有拼接文本时，按段标题（如 `[Section]`、`=== Section ===`）自动拆分成多条 `PromptEntries`。
- 非提示词RPG设置迁移（`RimChatSettings_AI.RpgDialogue.cs`）：
  - 新增 ModOptions 分组 `RPG Runtime Settings`；
  - 承接运行时开关：`EnableRPGDialogue`、`EnableRPGAPI`、注入开关、`RpgManualSceneTagsCsv`。
- 兼容基线：
  - `PromptPresetChannelPayloads` 结构不变；
  - RimTalk 兼容字段与读写路径不变，仅隐藏可视通道入口。

## Prompt Entry Unified Channels（v0.6.4）

- Prompt Workbench 通道行为变更：
  - `Diplomacy` 与 `RPG` 现直接复用 RimTalk 条目编辑工作流，不再走旧“分区编辑器”中间层。
  - 条目编辑结构保持一致：`Name / Enabled / Role / Position / InChatDepth / Content`。
- 运行时组装入口变更：
  - `PromptPersistenceService.BuildFullSystemPromptHierarchical(...)`
  - `PromptPersistenceService.BuildRpgSystemPromptHierarchical(...)`
  - 新逻辑优先按条目顺序拼接“已启用条目”；当检测到仅旧字段（无有效条目）时，按旧字段生成临时回退条目以保证升级兼容。
- Scriban 渲染链路：
  - 条目内容通过 `PromptTemplateRenderer.RenderOrThrow(...)` -> `IScribanPromptEngine.RenderOrThrow(...)` 渲染。
  - 条目渲染不再依赖 RimTalk bridge 运行时方法。
- 旧字段回写策略：
  - 保存时会从条目系统回写旧字段（外交：`GlobalSystemPrompt/GlobalDialoguePrompt`，RPG：`RoleSetting/DialogueStyle` 等），并落盘到旧路径 JSON，保证旧版本读取不崩。

## RimTalk 通道变量条目编辑器（v0.6.3）

- `RimTalkChannelCompatConfig` 新增字段：
  - `PromptEntries: List<RimTalkPromptEntryConfig>`
- 新增条目数据契约：`RimTalkPromptEntryConfig`
  - `Id`、`Name`、`Role`、`Position`、`InChatDepth`、`Enabled`、`Content`
- 兼容策略：
  - 旧配置仅含 `CompatTemplate` 时，加载阶段自动迁移为单条默认条目；
  - 条目列表会自动合成为 `CompatTemplate`，保持旧链路可读。
- RimTalk 通道 UI 交互升级：
  - 条目列表：新增、复制、删除、上移、下移；
  - 条目编辑：名称、启用状态、角色、位置、InChat 深度、内容；
  - 变量插入：优先写入当前选中条目内容。

## Prompt Workbench + Preset Framework（v0.6.2）

- 新增预设数据契约：
  - `PromptPresetStoreConfig`：`SchemaVersion`、`ActivePresetId`、`Presets`。
  - `PromptPresetConfig`：`Id`、`Name`、`IsActive`、`CreatedAtUtc`、`UpdatedAtUtc`、`ChannelPayloads`。
  - `PromptPresetChannelPayloads`：`Diplomacy`、`Rpg`、`RimTalkDiplomacy`、`RimTalkRpg` 与 RimTalk 限制字段。
- 新增服务接口：`IPromptPresetService`（`LoadAll/SaveAll/CreateFromLegacy/Duplicate/Activate/ImportPreset/ExportPreset/BuildSummaries`）。
- Prompt 页高级模式新增工作台：
  - 通道导航：`Diplomacy`、`RPG`、`RimTalk-Diplomacy`、`RimTalk-RPG`。
  - 预设管理：新建、复制、激活、删除、重命名、导入、导出。
- 兼容策略：
  - 首次加载若无预设文件，自动从旧 `Prompt/Custom/*` 配置迁移创建默认预设。
  - 激活预设时回写旧 `Prompt/Custom/*` 文件，并保持 RimTalk 双通道兼容字段同步。
- 旧 RimTalk Tab 调整为迁移入口：引导用户跳转到 Prompt 工作台对应通道。

## RimTalk 严格隔离开关（v0.6.1）

- 新增隔离配置项（`RimChatSettings` / `RpgPromptCustomConfig`）：
  - `RimTalkAutoPushSessionSummary`（默认 `false`）
  - `RimTalkAutoInjectCompatPreset`（默认 `false`）
- 兼容链路行为（Scriban strict 主链）：
  - `PushSessionSummary` 仅在 `RimTalkAutoPushSessionSummary == true` 时执行全局变量写入；
  - 兼容预设条目 `RimChat Compat Variables` 仅在 `RimTalkAutoInjectCompatPreset == true` 时自动创建/更新；
  - 当 `RimTalkAutoInjectCompatPreset == false` 时，若存在该条目会被强制禁用，避免 RimTalk 普通聊天链路读取 RimChat 摘要。
- 变量注册方式保持不变：
  - 仍通过 Context 变量注册链路提供 `rimchat_*` 变量；
  - 推荐仅在模板中显式 `{{variable}}` 引用，不做隐式自动注入。
- 兼容性：
  - 旧存档 / 旧自定义提示词 JSON 缺失新字段时自动回退为 `false`；
  - 不破坏既有字段结构与读取路径。

## 通讯台外交隐藏派系显示控制（v0.5.29）

- 存档级新状态（`GameComponent_DiplomacyManager`）：
  - `HashSet<Faction> manuallyVisibleHiddenFactions`
  - 序列化键：`manuallyVisibleHiddenFactions`
  - 读档兼容：旧存档无该字段时自动回退为空集合，并清理失效引用。
- 新增接口（供 UI 调用）：
  - `List<Faction> GetManuallyVisibleHiddenFactions()`
  - `bool IsHiddenFactionManuallyVisible(Faction faction)`
  - `void SetManuallyVisibleHiddenFactions(IEnumerable<Faction> factions)`
- 通讯台外交派系列表过滤规则（`Dialog_DiplomacyDialogue.GetAvailableFactions`）：
  - 基础资格：`!IsPlayer && !defeated`
  - 默认可见：`!Hidden`
  - 额外可见：`Hidden && manuallyVisibleHiddenFactions.Contains(faction)`
- UI 新增：
  - 派系标题右侧齿轮按钮：打开隐藏派系多选弹窗。
  - 弹窗操作：全选、清空、确定、取消（仅确定写入存档状态）。

## 图片 API 三模式收敛与 ComfyUI 异步链路（v0.5.22）

- 图片生成执行模式（`DiplomacyImageApiConfig` / `DiplomacyImageGenerationService`）：
  - `sync_url`：同步请求，解析 URL 并下载。
  - `sync_payload`：同步请求，优先解析 URL，回退解析 Base64 载荷。
  - `async_job`：异步任务模式（提交 -> 轮询 -> 拉图）。
- 新增可持久化配置字段（旧存档缺失时自动回退默认值）：
  - `Mode`、`SchemaPreset`、`AuthMode`
  - `ApiKeyHeaderName`、`ApiKeyQueryName`
  - `ResponseUrlPath`、`ResponseB64Path`
  - `AsyncSubmitPath`、`AsyncStatusPathTemplate`、`AsyncImageFetchPath`
  - `PollIntervalMs`、`PollMaxAttempts`
- 鉴权模式：
  - `bearer`（默认）
  - `api_key_header`（使用 `ApiKeyHeaderName`）
  - `query_key`（使用 `ApiKeyQueryName`）
  - `none`
- ComfyUI 兼容（`SchemaPreset=comfyui`）：
  - 自动切换到 `async_job`；
  - 提交流程：`/prompt`；
  - 轮询流程：`/history/{job_id}`；
  - 拉图流程：`/view?filename=...&subfolder=...&type=...`。
- 图片 API 设置页连通性测试（`RimChatSettings_ImageApi`）：
  - 提供与主 API 页同风格的 `Test Connection` 按钮与状态色反馈；
  - `sync_*` 模式执行一次最小发图请求探测；
  - `async_job` 模式执行提交探测（ComfyUI 读取 `prompt_id`）。
- 图片 API 设置页 Provider 预设：
  - 预设项：`Volcengine ARK`、`OpenAI Compatible`、`SiliconFlow`、`ComfyUI Local`、`Custom`。
  - 非 `Custom` 预设自动填充模式/协议/鉴权默认值，普通用户仅需配置 endpoint/apiKey/model。
  - `Custom` 预设可展开高级选项（模式、鉴权、响应路径、异步路径、轮询参数）。
- 兼容性：
  - 不改动 `send_image` 动作契约；
  - 不改动既有提示词文件结构；
  - 旧存档与旧配置可直接读取。

## RPG 对话会话历史面板与行为时间线（v0.5.21）

- RPG 手动对话窗口新增会话历史面板（`Dialog_RPGPawnDialogue.HistoryPanel`）：
  - 左下角新增入口按钮 `RimChat_RPGHistoryButton`；
  - 面板显示范围固定为“当前会话”；
  - 记录顺序为时间正序（旧 -> 新）；
  - 点击面板外只关闭面板，不关闭 RPG 对话窗口。
- 会话内历史记录模型（运行时，仅 UI）：
  - 对话条目：`speaker + text`（玩家/NPC）；
  - 行为条目：挂在对应 NPC 对话条目下，包含 `actionName + result(success/failure/error) + reason`。
- 动作执行链路对接：
  - `NotifyActionSuccess/NotifyActionFailure/NotifyActionError` 现在会同步写入历史行为记录；
  - 保持原有动作执行逻辑和 toast 行为不变。
- 本地化新增键：
  - `RimChat_RPGHistoryButton`
  - `RimChat_RPGHistoryPanelTitle`
  - `RimChat_RPGHistoryEmpty`
  - `RimChat_RPGHistoryActionPrefix`
  - `RimChat_RPGHistoryActionResultSuccess/Failure/Error`
  - `RimChat_RPGHistoryReasonPrefix`

## 外交发图 Caption 策略与输入锁定占位隐藏（v0.5.20）

- 输入锁定渲染调整（`Dialog_DiplomacyDialogue`）：
  - 锁定输入框期间保留只读行为，但 `DrawLockedInputPreview(...)` 不再绘制等待文案。
  - 底部 typing 状态层与结束态优先级逻辑保持不变。
- `send_image` caption 处理（`Dialog_DiplomacyDialogue.ImageAction`）：
  - 仍读取 `parameters.caption`；
  - 若为空，不再回退模板名，改为本地模板兜底；
  - 兜底占位符：`{leader}`、`{faction}`、`{template_name}`；
  - 渲染后若仍为空，回退 `RimChat_SendImageDefaultCaption`。
- 设置持久化新增字段（`RimChatSettings`）：
  - `SendImageCaptionStylePrompt`（默认：`PromptTextConstants.SendImageCaptionStylePromptDefault`）
  - `SendImageCaptionFallbackTemplate`（默认：`PromptTextConstants.SendImageCaptionFallbackTemplateDefault`）
  - 旧存档缺失字段时自动按默认值回退，不影响反序列化。
- 图片 API 设置页（`RimChatSettings_ImageApi`）：
  - 新增两个多行编辑项：caption 风格提示词、caption 本地兜底模板。
- 提示词构建（`PromptPersistenceService.AppendSendImageTemplateGuidance`）：
  - `SEND_IMAGE TEMPLATE RULE` 新增 caption 指引，要求优先填写 `parameters.caption`；
  - caption 风格读取 `SendImageCaptionStylePrompt`；
  - 明确 caption 语言需匹配当前游戏语言。

## 外交相册缩略图与自拍注入开关（v0.5.19）

- `AlbumImageEntry` 新增可选字段：`sourceType`（`chat/selfie/unknown`）。
  - 旧存档缺失该字段时自动回退 `unknown`，不影响反序列化。
- 相册窗口 `Dialog_DiplomacyAlbum` 升级为缩略图网格卡片视图：
  - 缩略图缓存（软上限 + 回收）；
  - 来源徽标（聊天图/自拍图）；
  - 右键菜单新增 `复制图片路径`，保留 `打开图片保存目录`。
- 聊天内联图右键保存修复（`Dialog_DiplomacyDialogue.ImageRendering`）：
  - 触发改为 `ContextClick + MouseDown(button=1)` 双兜底；
  - 命中区域改为图片真实可视矩形（aspect-fit）而非整块容器。
- 自拍参数窗口 `Dialog_DiplomacySelfieConfig` 新增注入开关：
  - `服饰/体型/发型/武器/植入物/状态`（默认全开）；
  - 通过 `SelfiePromptInjectionBuilder` 在发送前隐藏拼接到最终 prompt；
  - 不改写用户手动输入的 prompt 文本框。
- 自拍预览手动入册时元数据会标记 `sourceType=selfie`；聊天右键入册标记 `sourceType=chat`。

## 外交相册与自拍工作流（v0.5.18）

- 新增存档持久化类型：`AlbumImageEntry`
  - 字段：`id`、`savedTick`、`sourcePath`、`albumPath`、`caption`、`factionId`、`negotiatorId`、`size`。
- `GameComponent_DiplomacyManager` 新增相册接口：
  - `bool AddAlbumEntry(AlbumImageEntry entry)`
  - `List<AlbumImageEntry> GetAlbumEntries()`
  - `int PruneMissingAlbumFiles()`
- 新增相册服务：`DiplomacyAlbumService`
  - `SaveToAlbum(sourcePath, metadata, out savedEntry, out error)`：复制文件到存档维度相册目录并自动防重名。
  - `OpenImageDirectory(item, out error)`：打开所选图片实际保存目录。
- 外交窗口新增 UI 行为：
  - 主标签栏新增按钮：`Album`、`Selfie`。
  - 聊天内联图片支持右键菜单：`Save to Album`。
  - 自拍流程改为：参数窗口 -> 生成 -> 预览窗口 -> 用户手动保存到相册（非自动入册）。
- 兼容性：
  - 新增 `albumEntries` 存档字段，旧存档缺失字段自动初始化为空。
  - 不改动既有 `send_image` 动作契约和提示词文件结构。

## 手动RPG血缘/浪漫关系画像注入（v0.5.17）

- RPG Prompt 默认/自定义配置新增字段：
  - `RelationshipProfileTemplate`
  - `KinshipBoundaryRuleTemplate`
- 组装行为：
  - 在 `PromptPersistenceService.BuildRpgSystemPromptHierarchical(...)` 中，`isProactive == false` 时新增节点 `relationship_profile`。
  - 节点输出字段：`Kinship`（yes/no）、`RomanceState`（spouse/fiance/lover/ex-or-none/none）、`Guidance`（边界规则模板渲染结果）。
- 关系判定边界：
  - 血缘关系仅输出布尔存在性（不细分类别）。
  - 浪漫状态优先级：`spouse -> fiance -> lover -> ex-or-none -> none`。
- 兼容性：
  - 不新增存档字段；
  - 旧 `Prompt/Custom/PawnDialoguePrompt_Custom.json` 缺失新字段时走默认回退；
  - 不修改主动RPG场景标签链路（`AppendRpgScenarioTags/HasIntimateRelation` 语义不变）。

## 派系提示词模板增删与默认模板保护（v0.5.16）

- `FactionPromptManager` 新增接口：
  - `bool TryAddTemplateForFaction(string factionDefName, string displayName, out string status)`
  - `bool TryRemoveTemplate(string factionDefName, out string reason)`
  - `bool IsDefaultTemplate(string factionDefName)`
  - `bool IsFactionMissing(string factionDefName)`
- 默认模板目录来源：
  - 启动时优先从 `Prompt/Default/FactionPrompts_Default.json` 构建默认模板目录（`FactionDefName` 集合 + 默认配置克隆源）。
  - 默认模板条目不可删除（`TryRemoveTemplate` 返回 `default_protected`）。
- 自动补齐规则调整：
  - 仅对默认模板目录中的 `FactionDefName` 做补齐；
  - 自定义新增模板被删除后不会在加载时自动补回。
- 导入行为：
  - `ImportConfigsFromJson(...)` 导入后会调用默认模板补齐，确保默认模板保护规则持续生效。
- 兼容性：
  - 未改动 `FactionPrompts_Custom.json` 的 JSON 结构；
  - 旧版本提示词文件和旧存档可直接读取。

## 外交发图等待门控与结束态优先级（v0.5.15）

- 运行态新增（不写存档）：`FactionDialogueSession.pendingImageRequests`。
  - 配套方法：`BeginImageRequest()`、`EndImageRequest()`、`HasPendingImageRequests()`。
  - 作用：统一表示外交 `send_image` 异步请求是否仍在处理中。
- `send_image` 生命周期行为：
  - 发起图片生成前调用 `BeginImageRequest()`。
  - 回调（成功/失败）均调用 `EndImageRequest()`；计数做非负保护。
- 输入门控规则统一为：
  - `session.isWaitingForResponse == true`，或
  - `session.HasPendingImageRequests() == true`，或
  - NPC 逐字渲染仍未完成。
- 会话结束态优先级：
  - 当 `session.isConversationEndedByNpc == true` 时，输入区状态显示优先输出“会话结束原因/冷却提示”，不显示 typing 状态。
  - 即使会话已结束，`send_image` 的晚到回调仍允许追加历史消息（图片卡片或失败系统消息）。
- 兼容性：不新增 Scribe 字段，不更改提示词文件结构，旧存档可直接兼容。

## 外交发图尺寸阈值对齐（v0.5.14）

- `send_image` 的 `size` 参数校验下限调整为 `>= 3,686,400` 像素（与图片接口最新要求一致）。
- 当 action 参数或旧配置提供低尺寸（如 `1024x1024`）时，会自动归一化为默认尺寸 `2560x1440` 后再发请求。
- 尺寸别名映射更新：
  - `small` / `landscape` -> `2560x1440`
  - `portrait` -> `1440x2560`
  - `medium` -> `3072x1728`
  - `large` -> `3840x2160`
- 兼容性：不新增存档字段，不改提示词文件结构，旧存档继续可读。

## 外交发图接口（v0.5.11）

- 新增动作：`send_image`。
  - 参数契约：`template_id`（必填）、`extra_prompt`（可选）、`caption`（可选）、`size`（可选）、`watermark`（可选）。
  - 资格校验：图片 API 已配置可用、模板存在且启用、`template_id` 非空。
- 请求执行链：
  - 外交窗口动作执行阶段新增 `TryHandleSendImageAction(...)` 拦截（与 presence/social 同层）。
  - 每轮最多执行 1 次 `send_image`，超出部分直接系统提示并忽略。
- ARK REST 请求契约（固定字段）：
  - Header: `Content-Type: application/json`、`Authorization: Bearer <ImageApiKey>`。
  - Body: `model`、`prompt`、`sequential_image_generation="disabled"`、`response_format="url"`、`stream=false`、`size`、`watermark`。
  - 说明：`size/watermark` 默认取独立图片配置，可被 action 参数覆盖；其余固定字段不允许 action 改写。
- Prompt 组装规则：
  - `模板正文 + extra_prompt + LeaderProfile`。
  - `LeaderProfile` 包含：首领身份（姓名/称谓/种族/性别）、外形（体型/发型/胡须/可见服饰）、派系信息（类型/科技/关系/背景）。
  - 缺失首领 Pawn 时自动回退为派系级背景描述，不阻断发图。
- 响应处理：
  - 当前仅支持 `response_format=url` 分支。
  - 先解析 URL，再下载图片字节并落地到存档维度缓存目录，最后回写为聊天内联图片卡片。
  - 下载或生成失败时，不影响文本回复，仅追加系统失败提示。
- 存档兼容：
  - `DialogueMessageType` 新增 `Image`。
  - `DialogueMessageData` 新增 `imageLocalPath`、`imageSourceUrl`（均有默认值，旧存档可直接读取）。
## NPC 主动对话分离开关（v0.5.8）

- 新增配置字段：
  - `RimChatSettings.EnablePawnRpgInitiatedDialogue`（默认 `true`，Scribe key: `EnablePawnRpgInitiatedDialogue`）。
- 既有字段语义保持：
  - `RimChatSettings.EnableNpcInitiatedDialogue` 继续用于外交主动对话门控。
- 主动链路门控更新：
  - 外交主动：`GameComponent_NpcDialoguePushManager` 仍读取 `EnableNpcInitiatedDialogue`。
  - PawnRPG 主动：`GameComponent_PawnRpgDialoguePushManager.IsFeatureEnabled()` 改为读取 `EnablePawnRpgInitiatedDialogue && EnableRPGDialogue`。
- 旧存档迁移策略：
  - 在 `RimChatSettings.ExposeData_AI()` 加载阶段，若存档节点中缺失 `EnablePawnRpgInitiatedDialogue`，则自动将其赋值为旧 `EnableNpcInitiatedDialogue`，保持旧配置行为一致。

## 概述

`GameAIInterface` 是 RimChat 模组中用于 AI 与游戏交互的核心接口类。它提供了一系列 API 方法，允许 AI 根据对话内容动态调整游戏状态，实现智能外交交互。

## API 调试观测窗口（v0.5.7）

- `AIChatServiceAsync.SendChatRequestAsync(...)` 接口扩展（向后兼容）：
  - 新增可选参数：`AIRequestDebugSource debugSource = AIRequestDebugSource.Other`。
  - 旧调用方不传该参数时保持原行为。
- 新增来源分类枚举：`AIRequestDebugSource`。
  - 枚举值：`DiplomacyDialogue`、`RpgDialogue`、`NpcPush`、`PawnRpgPush`、`SocialNews`、`StrategySuggestion`、`PersonaBootstrap`、`MemorySummary`、`ArchiveCompression`、`Other`。
- 新增调试观测模型（只读）：
  - `AIRequestDebugRecord`：单条请求记录（时间戳、来源、渠道、模型、状态、耗时、HTTP、token、完整 request/response）。
  - `AIRequestDebugSummary`：窗口汇总统计（总 token、请求数、成功率、平均耗时、外交/RPG token 占比）。
  - `AIRequestDebugBucket`：5 分钟桶统计（最近 60 分钟共 12 桶）。
  - `AIRequestDebugSnapshot`：窗口快照（summary + buckets + records）。
- 新增只读查询接口：
  - `AIChatServiceAsync.TryGetRequestDebugSnapshot(out AIRequestDebugSnapshot snapshot)`
  - `AIChatServiceAsync.GetRequestDebugSnapshot()`
- 采集策略：
  - 覆盖所有 `SendChatRequestAsync` 请求来源，不依赖 `EnableDebugLogging`。
  - 内存环形保留：最多 2000 条；自动清理 65 分钟前数据；窗口展示固定最近 60 分钟。
  - 不新增 Scribe 字段，不写入存档，保持旧存档兼容。

## 袭击执行可靠性修复（v0.5.5）

- `DiplomacyEventManager.TriggerRaidEvent(...)`：
  - 当策略/入场模式归一化后仍无法通过预检或执行失败时，会强制追加一次“原版自动策略 + 自动入场”的兜底执行。
  - 目标：避免 `request_raid` 因特定策略不可执行而整体失败。
- `GameComponent_DiplomacyManager.ProcessDelayedEvents()`：
  - 改为“执行成功后再移除事件”。
  - 失败事件不再立即丢弃，转为延迟重试（最多 3 次）。
- `DelayedDiplomacyEvent` 存档兼容扩展：
  - 新增 `raidStrategyDefName` / `arrivalModeDefName` 字段。
  - 当 `Scribe_Defs` 无法还原 Def 引用时，允许按名称回填 Def，兼容旧存档和模组变动场景。

## 提示词包选择性导入导出 + RimTalk 通道化（v0.5.4）

- Prompt bundle 数据结构升级：
  - `PromptBundleConfig.BundleVersion` 升级到 `v2`。
  - 新增 `IncludedModules`（模块白名单）。
  - 新增 RimTalk 通道字段：`RimTalkDiplomacy`、`RimTalkRpg`（共享 `RimTalkSummaryHistoryLimit`）。
- `PromptPersistenceService` 新增能力：
  - `ExportConfig(string filePath, IEnumerable<PromptBundleModule> selectedModules)`（模块选择导出）。
  - `ImportConfig(string filePath, IEnumerable<PromptBundleModule> selectedModules)`（模块选择导入）。
  - `TryGetImportPreview(string filePath, out PromptBundleImportPreview preview)`（导入预览）。
- 兼容策略：
  - `v1` 旧文件可继续导入；缺失 `IncludedModules` 时默认映射为全模块。
  - 旧 RimTalk 单通道字段会在导入/加载时自动迁移为外交+RPG 双通道配置。
- RimTalk 运行时兼容桥扩展：
  - `RimTalkCompatBridge.GetRuntimeStatus()` 提供启用状态、运行时可用性和失败原因。
  - `RenderCompatTemplate(...)` / `RenderActivePresetModEntries(...)` 改为按 `channel` 使用通道化配置。
  - 上下文变量注册支持更宽松的反射签名匹配，优先 PromptAPI，回退 ContextHookRegistry。
  - 反射参数装配优先使用目标方法默认值，降低不同 RimTalk 版本签名差异导致的注入失败风险。
- 导入导出稳健性补充：
  - `ExportConfig(...)` 在服务层统一做路径空值与目录创建校验（不依赖 UI 层）。
  - `ImportConfig(...)` 增加空路径、空文件、无交集模块选择的提前拦截与日志提示。
- UI 路径收敛：
  - 旧 RPG 页内 RimTalk 兼容工具路径已收敛，仅保留独立 RimTalk 顶级页，避免双入口分叉维护。

## 非言语 Pawn RPG 对话兼容（v0.5.0）

- 手动 RPG 对话入口不再限制 `Human/Humanlike`，可面向全部 Pawn 目标发起。
- 非言语类别判定固定为：`Animal` 或 `Baby` 或 `Mechanoid`。
- 对命中非言语类别的目标回复，显示层会强制为：`叫声 + （内心想法）`（中文）或 `sound + (inner thought)`（其他语言）。
- 若模型已输出合法“叫声 + 括号想法”结构，系统保留其叫声与想法，仅做括号规范化。
- 若模型未输出合法结构，系统会回退为本地化默认叫声 + 括号包裹原始意图文本。
- RPG actions 的 JSON 解析与执行链路保持不变。

## HAR 种族 Def 注入加固（v0.5.1）

- XML Patch 的 Def 选择器已从固定 `ThingDef` 扩展为通配 Def 节点，兼容 Humanoid Alien Races 2.0 的自定义 Def 标签。
- 新增运行时 Def 注入器，在 Def 加载完成后按解析结果补齐 `CompPawnDialogue`，覆盖继承链导致的静态 XML 漏注入场景。

## XML 误命中修复（v0.5.2）

- 回退 XML 注入范围到保守 `ThingDef[defName="Human"]`，避免通配 XML 误命中 `PawnKindDef` 导致 `<comps>` 字段报错。
- HAR/异种族覆盖继续由运行时 `PawnDialogueCompDefInjector` 负责。

## RPG 输出契约加固（v0.4.12）

- RPG `actions[]` 解析兼容字段：
  - 支持 `params`（历史形态）
  - 支持 `parameters`（OpenAI 兼容常见形态）
- RPG 请求链路约束：
  - 常规请求会追加严格输出契约提醒，不再仅依赖 `HTTP 400` 重试时的补充提醒。
- RPG JSON 结构建议：
  - 可见文本先输出自然语言。
  - 仅在需要游戏效果时追加一个 `{"actions":[...]}` 对象。
  - `action` 使用允许动作名（示例：`TryGainMemory`），参数优先使用 `defName` / `amount` / `reason`。

## Custom URL 安全映射与模式化解析（v0.4.9）

- `ApiConfig` 新增可序列化字段：
  - `CustomUrlMode`：`BaseUrl` / `FullEndpoint`。
  - 旧配置加载时执行一次性自动判定：包含 `/chat/completions` 归为 `FullEndpoint`，否则归为 `BaseUrl`。
- Custom provider 运行时 URL 解析规则：
  - 仅映射 `cloud.siliconflow.*` 主机到 `api.siliconflow.cn`。
  - `FullEndpoint`：保留原路径/查询参数，不做端点重写。
  - `BaseUrl`：仅对空路径、`/`、`/v1` 自动补全到 `/v1/chat/completions`。
  - 非标准路径保持原值（并返回提示标记），避免误改兼容网关地址。
- 模型列表与连通性测试：
  - `Custom FullEndpoint` 测试链路为“先 `/models`，失败后回退 chat endpoint 探测”。
  - 连接状态文本会追加映射/可疑路径/回退命中提示，便于定位配置行为。

## 模型列表拉取兜底（v0.4.7）

- DeepSeek 模型列表地址对齐 RimTalk，使用 `/models` 端点。
- 模型列表请求会自动去除 API Key 前后空白字符。
- OpenAI 兼容模型列表解析在返回空列表时会尝试从 JSON 中抽取 `id` 作为兜底。

## 通讯台覆盖默认值调整（v0.4.5）

- 设置默认值变更：
  - `RimChatSettings.ReplaceCommsConsole` 默认值改为 `false`。
  - `Scribe_Values.Look(ref ReplaceCommsConsole, "ReplaceCommsConsole", false)` 作为缺省回填值。
- UI 默认重置变更：
  - `ResetUISettingsToDefault()` 现在将 `ReplaceCommsConsole` 重置为 `false`。
- 兼容说明：
  - 不涉及存档结构变更。
  - 仅影响“缺省值/恢复默认”路径；用户手动配置优先级不变。

## 原版派系联络桥接入口（v0.4.4）

- UI 桥接补丁：
  - `FactionDialogRimChatBridgePatch`（`HarmonyPatch` 到 `FactionDialogMaker.FactionDialogFor` 的 `Postfix`）。
- 触发条件：
  - `RimChatMod.Settings != null`
  - `ReplaceCommsConsole = false`
  - 当前为有效非玩家派系联络根节点
- 注入行为：
  - 在 `DiaNode.options` 增加本地化入口 `RimChat_UseRimChatContact`。
  - 入口动作：`Find.WindowStack.Add(new Dialog_DiplomacyDialogue(faction, negotiator))`。
  - 入口设置 `resolveTree = true`，用于在点击时关闭原版联络树窗口。
  - 插入位置优先在“退出/挂断”前（`resolveTree=true && link == null && linkLateBind == null`），否则追加到末尾。
- 兼容说明：
  - 不新增存档字段，不改变对外 API。
  - `ReplaceCommsConsole = true` 时该桥接入口不生效，原有通讯台覆盖流程保持不变。

## 外交 Prompt 动态上下文补全（v0.4.3）

- 外交通道新增动态注入节点（`dynamic_data`）：
  - `player_pawn_profile`
  - `player_royalty_summary`
  - `faction_settlement_summary`
    - 输出该派系“全量据点列表”（不再仅输出关键据点）
- 玩家小人来源策略：
  - 优先使用外交窗口显式 `negotiator`
  - 缺失时回退到“社交最高殖民者”
  - 主动外交推送同样复用回退策略
- 帝国软约束注入（Prompt 层）：
  - 读取玩家侧帝国荣誉点（`Pawn_RoyaltyTracker.GetFavor(faction)`）
  - 读取当前派系头衔（`GetCurrentTitleInFaction(faction)`）
  - 汇总许可可用性（`AllFactionPermits`）
  - 输出 `create_quest/request_aid` 软约束提示（执行层资格校验仍为最终权威）
- 新增服务重载（签名新增，不破坏旧调用）：
  - `BuildFullSystemPrompt(Faction faction, SystemPromptConfig config, bool isProactive, IEnumerable<string> additionalSceneTags, Pawn playerNegotiator)`
- 模板变量扩展：
  - `player_pawn_profile`
  - `player_royalty_summary`
  - `faction_settlement_summary`

## 好感度分段和平策略（v0.3.164）

- 生效链路：
  - 执行层：`ApiActionEligibilityService.ValidateActionExecution(...)`、`TryValidateQuestTemplateForFaction(...)`
  - 提示词层：`PromptPersistenceService.AppendCompactDiplomacyResponseContract(...)` 动态注入
- `make_peace` 分段规则：
  - `< -50`：禁止直接议和（返回 `peace_goodwill_too_low`）。
  - `[-50,-21]`：禁止 `make_peace`，要求改走和平会谈任务（返回 `peace_talk_required`）。
  - `[-20,0]`：允许 `make_peace`（仍需满足战时与冷却等既有条件）。
  - `> 0`：保持既有规则不变。
- `create_quest` 分段规则：
  - 在 `[-50,-21]` 区间，若指定 `questDefName`，仅允许 `OpportunitySite_PeaceTalks`（`peace_talk_only_range`）。
  - 任务模板校验层同步限制该区间只允许 `OpportunitySite_PeaceTalks`。
- 提示词动态注入：
  - 在外交 response contract 中新增 `DYNAMIC PEACE POLICY (GOODWILL-BASED)` 区块。
  - 根据当前 goodwill 输出禁用原因/替代路径/可用路径，和执行层规则保持一致。

## 模型超时统一（v0.3.158）

- 超时策略统一为 `40s`（本地/云端一致）。
- 覆盖实现：
  - `AIChatServiceAsync`
  - `AIChatService`
  - `AIChatClient`

## 本地超时恢复（v0.3.157）

- 生效范围：仅本地模型模式（`UseCloudProviders = false`）。
- `AIChatServiceAsync` 调整：
  - 本地请求 timeout 从 `60s` 提升到 `180s`（云端保持 `60s`）。
  - `ConnectionError` 分支新增 timeout 语义识别：timeout 类错误返回 `RimChat_ErrorTimeout`。
  - 本地连接瞬态错误（timeout/reset 等）增加有限重试（2 次尝试，短退避 + 抖动）。
- 观测：内部日志新增 `local_conn_retry` 记录重试决策（受 `LogInternals` 控制）。

## 本地模型 500 容错与诊断（v0.3.154）

- 生效范围：仅 `UseCloudProviders = false`（本地模型模式）。
- `AIChatServiceAsync` 请求生命周期新增：
  - 本地请求单飞队列（并发上限 `1`）：本地请求按 `enqueue -> wait turn -> execute -> release` 串行执行。
  - 本地 5xx 自动重试：仅对 `500/502/503/504` 触发，最多 `3` 次请求尝试（首发 + 2 次重试）。
  - 退避策略：第一次重试短退避，第二次重试长退避，均带轻微抖动。
  - 保持既有 `HTTP 400 user input rejected` 降级重试逻辑，不与 5xx 重试互斥。
- 新增诊断日志（受现有 Debug Internals 开关控制，无新增 UI 配置）：
  - 每次请求尝试输出结构化指纹：`requestId/attempt/channel/model/host/messageCount/jsonBytes/elapsedMs/httpCode`。
  - 本地 5xx 重试决策输出：`attempt -> nextAttempt`、`backoffMs`、`responseSummary`。
- 兼容性：
  - 云端 provider 的并发和重试行为不变。
  - 不新增用户可见设置项，不改变 API 配置页面字段。

## DeepSeek 官方地址强制（v0.4.6）

- DeepSeek 提供商强制使用官方地址：`https://api.deepseek.com/v1`。
- 读取配置时若检测到非官方 `BaseUrl`，会自动归一化为官方地址并写回配置。
- 模型列表与连接测试链路对 DeepSeek 不再使用自定义 `BaseUrl`。

## API URL 归一化加固（v0.3.151）

- 修复云厂商默认 URL 常量中的空白字符：
  - `AIProviderRegistry.Defs[*].EndpointUrl`
  - `AIProviderRegistry.Defs[*].ListModelsUrl`
- 修复本地默认地址：
  - `LocalModelConfig.BaseUrl` 默认值改为 `http://localhost:11434`。
- 新增 URL 归一化接口：
  - `ApiConfig.NormalizeUrl(string value)`
  - `ApiConfig.ToModelsEndpoint(string value)`
  - `ApiConfig.EnsureChatCompletionsEndpoint(string baseUrl)`
- 运行时调用链改造：
  - `ApiConfig.GetEffectiveEndpoint()` 统一返回归一化 URL。
  - `AIChatService` / `AIChatServiceAsync` / `AIChatClient` 在本地模式下统一按归一化 `BaseUrl` 生成 chat-completions endpoint。
  - 设置页模型拉取与连接测试链路改为使用归一化 URL（云/自定义/本地）。
- 兼容性与行为：
  - 不改变既有接口语义，仅修复“配置值带空白导致无法通过 URL 校验/请求失败”的异常路径。

## 社交圈世界新闻接口（v0.3.143）

- 运行时链路改为：`真实事件/公开声明 -> SocialNewsSeed -> LLM 严格 JSON -> PublicSocialPost`。
- `GameComponent_DiplomacyManager.ForceGeneratePublicPost(DebugGenerateReason reason = DebugGenerateReason.ManualButton)`
  - 语义变更为”提交下一条合格世界新闻生成请求”。
  - 若当前没有可报道事件、AI 未配置、或 JSON 生成失败，则不会写入半成品帖子。
  - 入队约束：不再要求候选 `SocialNewsSeed` 必须含可解析派系；只要通过基础有效性/去重/请求可用性检查即可入队。
  - 调度重试策略：自动调度对 `Failed` 来源采用 2 天冷却后重试；手动按钮可立即重试失败来源。
- `GameComponent_DiplomacyManager.TryForceGeneratePublicPost(DebugGenerateReason reason, out SocialForceGenerateFailureReason failureReason)`
  - 新增带失败原因输出的强制生成方法，用于精确诊断失败原因。
  - 失败原因枚举：`Disabled`（系统关闭）、`AiUnavailable`（AI 不可用）、`QueueFull`（请求队列已满）、`NoAvailableSeed`（无可用事件）、`Unknown`（未知错误）。
  - 即时采集重试：首次选 seed 失败时，立即触发 `WorldEventLedgerComponent.CollectNow()` 采集 Letter 栈与战报，然后二次选 seed 重试。
  - 二次仍无 seed 时返回 `NoAvailableSeed`，不生成兜底虚构新闻。
- `WorldEventLedgerComponent.CollectNow()`
  - 新增手动强制采集方法，立即执行 `PollLetterStackEvents` 与 `UpdateRaidBattleStates`。
  - 用于强制生成前同步采集最新世界事件与战报，确保即时可用性。
- `GameComponent_DiplomacyManager.EnqueuePublicPost(...)`
  - 仍作为 `publish_public_post` 的运行时入口，但内部不再直接拼装模板文案，而是改为提交带事实摘要的对话新闻种子。
- 新内部类型：
  - `SocialNewsSeed`：统一世界事件、战报、领袖记忆、外交摘要、公开声明的事实输入。
  - `SocialNewsOriginType`：标记新闻来源类型。
  - `SocialNewsGenerationState`：标记来源去重/生成结果状态。
  - `SocialForceGenerateFailureReason`：标记强制生成失败原因（v0.3.144）。
  - `SocialNewsJsonParser`：校验 `headline / lead / cause / process / outlook / quote / quote_attribution` 严格 JSON 合同。
- `PublicSocialPost` 新持久化字段：
  - `OriginType`, `OriginKey`, `Headline`, `Lead`, `Cause`, `Process`, `Outlook`, `Quote`, `QuoteAttribution`, `SourceLabel`, `CredibilityLabel`, `CredibilityValue`, `GenerationState`。
  - 读档清理约束：不会因 `SourceFaction/TargetFaction` 同时缺失而删除历史帖子；双空帖子仅由 UI 决定是否显示演员行。
  - UI 演员行约束（v0.3.144）：双边派系显示 `A → B`；仅单边派系时显示单派系行（`RimChat_SocialNewsSingleFactionLine`）；双边缺失时不渲染演员行。玩家派系视为有效单边。
- 社交圈 Prompt 分仓新增：
  - `SocialCircleNewsStyleTemplate`
  - `SocialCircleNewsJsonContractTemplate`
  - `SocialCircleNewsFactTemplate`
- RimChat 袭击链路防御（v0.3.144）：
  - `DiplomacyEventManager.TriggerRaidEvent` 增加策略/到达模式归一化与可执行性预检。
  - 策略/到达模式为空或不可执行时，自动选择可执行默认值（优先 `ImmediateAttack` / `EdgeWalkIn`）。
  - 失败时有明确日志，避免空集合 RandomElement 异常。

## 当前 Prompt 文件系统（v0.3.137）

- 默认提示词现已按领域拆分为 5 个文件：
  - `Prompt/Default/SystemPrompt_Default.json`
  - `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
  - `Prompt/Default/PawnDialoguePrompt_Default.json`
  - `Prompt/Default/FactionPrompts_Default.json`
  - `Prompt/Default/SocialCirclePrompt_Default.json`
- 运行时自定义提示词按相同领域写入 `Prompt/Custom/*_Custom.json`，不再使用 `system_prompt_config.json` 或 `RpgPrompts_Custom.json`。
- `PromptPersistenceService.LoadConfig/SaveConfig/ExportConfig/ImportConfig` 现在负责组装/拆分系统、外交、社交圈三类聚合配置；pawn/RPG 文件改由 `PawnDialoguePrompt_*` 链路读取与保存。

## 当前 Prompt 合同（v0.3.120）

- 外交通道默认输出合同已统一为：先输出角色台词；如需 gameplay effect，再追加一个原始 JSON 对象：`{"actions":[...]}`。
- 外交通道不再接受旧的单 `action / parameters / response` 输出模板；只接受 `{"actions":[...]}` 协议。
- 外交默认文本与模板改由 `Prompt/Default/DiplomacyDialoguePrompt_Default.json` 提供。
- RPG 角色设定、格式约束、动作可靠性、开场目标与 topic shift 默认值改由 `Prompt/Default/PawnDialoguePrompt_Default.json` 提供。
- `reject_request` 仅用于“明确的玩家请求被正式拒绝”；普通口头拒绝应直接用角色台词表达。
- `publish_public_post` 属于高影响的公开世界动作，只应用于面向全派系的公开声明，不应用于例行聊天或私下讨价还价。

## 外交 prompt 动作裁剪（v0.3.142）

- 默认外交 prompt 已移除 `send_gift`，不再向 LLM 暴露该动作。
- 旧存档/旧自定义 prompt 配置中的 `send_gift` 会在 prompt 配置自修复阶段被移除。
- `GameAIInterface.SendGift(...)` 仍保留为旧逻辑 API 参考；本次改动仅影响外交 prompt 的动作暴露。

## 外交对话固定消耗（v0.3.116）

- 外交对话中的固定行为成本不再由 LLM 通过 `adjust_goodwill` 间接表达，而是由系统在 API 成功后自动追加。
- `request_caravan`：仅当 `parameters.apply_goodwill_cost=true` 时，成功后固定基础消耗 `-15` 好感度（默认 `false`）。
- `request_aid`：仅当 `parameters.apply_goodwill_cost=true` 时，成功后固定基础消耗 `-25` 好感度（默认 `false`）；`Military` / `Medical` / `Resources` 统一按 `-25` 处理。
- `create_quest`：成功后固定基础消耗 `-10` 好感度。
- `send_gift`：保留旧逻辑实现，但默认外交 prompt 不再注入该动作。
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

## Prompt Policy V4 接口变更（v0.3.163）

### 配置模型

- `PromptTemplateTextConfig` 新增字段：
  - `DecisionPolicyTemplate`
  - `TurnObjectiveTemplate`
  - `TopicShiftRuleTemplate`
- `SystemPromptConfig` 新增字段：
  - `PromptPolicySchemaVersion`（当前默认：`4`）
  - `PromptPolicy`
- `PromptPolicyConfig` 当前公开配置：
  - `Enabled`
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

上述接口签名保持不变，仅内部保留策略层组装（不再执行 prompt token 预算裁剪）：
- 新节点：`decision_policy`、`turn_objective`、`topic_shift_rule`；
- RPG 首轮额外节点：`opening_objective`；
- 外交通道 API 限制（阈值/冷却/上限）与 `api_limits` 提示内容不变。

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

## RPG 对话行为更新（v0.3.137）

- `ExitDialogueCooldown`：冷却时长为 `60000` ticks（1 天）。
- 记忆回退：RPG 对话达到 `5` 轮后执行一次 `80%` 概率判定，成功时自动追加 `TryGainMemory`。
- `TryGainMemory`：默认记忆池已切换为 28 条 RimChat 分层记忆 Def；旧 token（如 `Chitchat` / `DeepTalk` / `Slighted` 和旧 3 条自定义 DefName）会自动重映射到新 Def。
- 自动补记忆：普通兜底只从正向递进池中选取（轻度 -> 中度 -> 深度正向记忆），不会自动落入第四档哲思/核心记忆。
- 系统反馈：应用/补记忆时展示本地化后的记忆标签，而不是原始 `defName`。
- 单行对白：RPG NPC 可见对白会在落地时折叠换行/制表/连续空白，统一为单行文本。
- 超长分页：当 RPG 对话正文超出对话框文本区域时，会在打字结束后启用消息内分页；历史回看同样支持分页。
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
- 会话冷却门控：对话被 NPC 结束且仍在重联冷却中时，主动触发延迟到冷却结束（仍受队列过期影响）。
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
- `PawnRpgProtagonistEntry`
  - PawnRPG 主动目标主角名单条目（`Pawn` 引用 + `pawnThingId` 兜底）。

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

### 主角名单接口（v0.5.6）

- `GetRpgProactiveProtagonists()`：获取当前存档的 PawnRPG 主角列表（仅返回可解析 Pawn）。
- `ContainsRpgProactiveProtagonist(Pawn pawn)`：判断 Pawn 是否在主角名单。
- `TryAddRpgProactiveProtagonist(Pawn pawn)`：尝试添加主角；达到上限时返回 `false`。
- `RemoveRpgProactiveProtagonist(Pawn pawn)`：从主角名单移除指定 Pawn。
- `ClearRpgProactiveProtagonists()`：清空主角名单。
- `GetRpgProactiveProtagonistCap()` / `SetRpgProactiveProtagonistCap(int)`：获取/设置主角人数上限（默认 `20`）。
- `GetEligibleRpgProactiveTargetsOnMap(Map map)`：获取当前地图内、名单中且运行时可用的 Pawn 候选。

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
- 主角名单门控：仅在“手动主角名单”内选目标；评分规则保持原有亲密关系/好感优先逻辑。
- 空名单行为：主角名单为空时，PawnRPG 主动链路严格不投递（含调试强制触发），并写入可读日志。

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

> 说明：`SendGift` 运行时 API 仍保留，但默认外交 prompt 已不再向 LLM 暴露该动作。

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
- 派系必须具备可用的 `Combat` 类型 `pawnGroupMaker`（对 HAR/种族派系同样生效）；否则会返回明确失败原因并拒绝排程。
- 当 `points <= 0` 时，自动点数改为使用原版 `RaidEnemy` 默认参数基线（`StorytellerUtility.DefaultParmsNow`），不再使用 `0.5x DefaultThreatPointsNow`。
- 自动点数会叠加配置调节：
  - 全局：`RaidPointsMultiplier`、`MinRaidPoints`
  - 按派系 Def 覆盖：`RaidPointsFactionOverrides`（`FactionDefName + RaidPointsMultiplier + MinRaidPoints`）
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
将公开声明写入社交圈新闻请求队列：
- 生成对话来源的 `SocialNewsSeed` 并提交到 LLM 严格 JSON 新闻链路。
- 对话来源新闻可应用轻量外交后果（好感变化 + 社交意图分值），不再触发额外世界 incident。
- 新闻卡片保存为结构化字段（标题/导语/起因/过程/研判/引述），不再使用旧版固定模板正文。

#### GameComponent_DiplomacyManager.ForceGeneratePublicPost
调试入口，立即扫描并提交下一条可用事实新闻请求，并重排下一次自动扫描时间。

#### GameComponent_DiplomacyManager.GetSocialPosts / GetUnreadSocialPostCount / MarkSocialPostsRead
提供社交圈 UI 所需的 feed 与未读状态接口。

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

### 袭击点数调节
- `RaidPointsMultiplier`: 全局袭击点数倍率 (0.1-5.0)
- `MinRaidPoints`: 全局最小袭击点数 (0-1000)
- `RaidPointsFactionOverrides`: 按派系 DefName 覆盖（每项包含 `FactionDefName`、`RaidPointsMultiplier`、`MinRaidPoints`）

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
- Min goodwill for aid: {当前设置值}
- Max goodwill for war declaration: {当前设置值}
- Max peace cost: {当前设置值}
- Peace goodwill reset: {当前设置值}

ENABLED FEATURES:
- Goodwill adjustment: {YES/NO}
- War declaration: {YES/NO}
- Peace making: {YES/NO}
- Trade caravan: {YES/NO}
- Aid request: {YES/NO}

ACTIONS:
1. adjust_goodwill - Change faction relations
   Parameters: amount (int, -{当前单次上限} to {当前单次上限}), reason (string)
   Daily limit remaining: {当前每日上限} total per day
2. request_aid - Request military/medical aid (requires ally)
   Parameters: type (string: Military/Medical/Resources)
   Requirement: goodwill >= {当前最低要求}
3. declare_war - Declare war
   Parameters: reason (string)
   Requirement: goodwill <= {当前宣战阈值}
4. make_peace - Offer peace treaty (requires war)
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

LLM 可以通过包含一个尾随 JSON 对象触发游戏 API 调用。**唯一有效动作协议**为 `actions` 数组：

```json
{
  "actions": [
    {
      "action": "adjust_goodwill",
      "parameters": {
        "amount": 10,
        "reason": "Successful trade negotiation"
      }
    }
  ],
  "strategy_suggestions": [
    {
      "strategy_name": "以势压人",
      "reason": "[F1] 财富压制，先用威慑抢占主导",
      "content": "你若继续拖延，我们会把谈判变成最后通牒。"
    },
    {
      "strategy_name": "缓和周旋",
      "reason": "[F2] 社交较高，先争取缓和与让步空间",
      "content": "我们愿意先降一阶条件，只要你给出可验证承诺。"
    },
    {
      "strategy_name": "极端威慑",
      "reason": "[F3] 激进特质下需快速施压迫使表态",
      "content": "再无结果，我们将按敌对预案执行，不再二次警告。"
    }
  ]
}
```

#### 字段说明

| 字段 | 类型 | 必需 | 说明 |
|------|------|------|------|
| actions | array | 否 | 动作数组；有 gameplay 效果时必须使用该字段 |
| actions[].action | string | 是（当 actions 存在） | 要执行的动作类型 |
| actions[].parameters | object | 否 | 动作参数，根据 action 类型变化 |
| strategy_suggestions | array | 否 | 降好感场景可选返回的策略按钮数据，必须为 3 项 |

#### `strategy_suggestions` 子字段说明

| 子字段 | 类型 | 必需 | 说明 |
|--------|------|------|------|
| strategy_name | string | 是 | 按钮短标题（建议 <= 6 中文字符） |
| reason | string | 是 | 触发依据，建议包含事实标签（如 `[F1]`） |
| content | string | 是 | 完整回复草稿（按钮点击即发送） |

**输出约束：**
- 策略能力可用时优先输出 `strategy_suggestions`（会话内由社交等级和剩余次数决定可用性）。
- 若输出该字段，必须严格返回 3 项；否则客户端会丢弃整个字段。
- 客户端在“净降好感且字段缺失/异常”时会发起一次仅请求 `strategy_suggestions` 的补充请求，不影响本轮普通对话文本与动作执行。
- 若补充请求返回自然语言而非 JSON，客户端会尝试从叙述文本中提取 3 条策略句并回填按钮（兜底逻辑）。
- 至少 2 条建议应明确基于玩家属性/上下文（社交、特质、殖民地财富、近期交互语气）。
- UI 侧在 `EnableDiplomacyStrategyToggle=false` 时会将策略状态区折叠为极简入口，并继续阻断策略补请求与按钮展示/自动发送；该折叠仅改变外交窗口布局，不改变协议字段。
- 禁止旧单对象协议：`{"action":"...","parameters":{...},"response":"..."}`。

#### 有效动作类型

| 动作 | 说明 | 必需参数 | 可选参数 |
|------|------|----------|----------|
| adjust_goodwill | 调整好感度 | amount (int) | reason (string) |
| request_aid | 请求援助 | - | type (string), apply_goodwill_cost (bool, default=false) |
| declare_war | 宣战 | - | reason (string) |
| make_peace | 议和 | - | cost (int) |
| request_caravan | 请求商队 | - | goods (string), apply_goodwill_cost (bool, default=false) |
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

### v0.9.11
- 添加 LLM JSON 响应支持
- 实现 AI 动作解析器
- 添加 AI 动作执行器
- 扩展系统提示词，包含 API 调用说明
- 实现接受/拒绝逻辑

### v0.9.10
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

## 在线状态强制时长更新（v0.9.88）
- `GameComponent_DiplomacyManager.GetPresenceForcedOfflineTicks()`
  - 现固定返回 `2 * GenDate.TicksPerHour`。
- `GameComponent_DiplomacyManager.GetPresenceDoNotDisturbTicks()`
  - 现固定返回 `4 * GenDate.TicksPerHour`。
- `GameComponent_DiplomacyManager.RefreshPresenceOnDialogueOpen(Faction faction)`
  - 强制离线或免打扰到期时，立即恢复 `Online`，并同步清空 `forcedOfflineUntilTick` / `doNotDisturbUntilTick`。
  - 到期恢复不再先走排班重算，避免“到期仍不可发言”的阻塞。
- `GameComponent_DiplomacyManager.EnforcePresenceForcedDurationCaps(...)`
  - 新增旧存档长计时截断逻辑：刷新时将剩余强制态上限裁剪为“当前 tick + 新规则时长”。
- `PresenceForcedOfflineHours`（设置项）
  - 保留存档字段与 UI 滑条，不再驱动 `go_offline/set_dnd` 强制时长运行态。

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
    - `He/She is a [core temperament] person who tends to [emotional pattern], usually handles situations by [behavioral strategy], because deep down they seek [core motivation], but this also makes them [defense/weakness], often leading to [personality cost].`
  - 示例：
    - `He is a calm and analytical person who rarely shows his emotions and tends to approach problems through careful observation and planning, because deep down he seeks control and security, but this also makes him distant and slow to trust others.`
  - 输出长度约束：保持单行输出、人格聚焦、短句表达；运行时会按 Pawn 性别统一 pronoun（`He/She/They`）。
  - 无效输出会重试；重试失败写入模板化兜底文本，保证字段可用。
- RimTalk 人格自动复制（v0.5.10）：
  - 在 AI 生成人格前，先尝试通过 RimTalk 模板渲染复制人格。
  - 目标过滤：仅殖民地人类 Pawn（`pawn.Faction == Faction.OfPlayer`）。
  - 覆盖策略：仅填空，不覆盖已有 `GetPawnPersonaPrompt` 非空值。
  - 模板来源：`RimChatSettings.RimTalkPersonaCopyTemplate`（strict Scriban，仅支持 `{{ pawn.personality }}` 语法）。
  - 渲染失败/空结果：直接抛出结构化异常并中断本次链路（无 silent fallback）。
  - 设置页支持查看最近一次模板迁移结果（成功/失败清单 + 阻断原因）。
  - 手动全量同步：`GameComponent_RPGManager.TrySyncAllColonyPawnPersonasFromRimTalk(out int updated, out int cleared, out int unchanged, out int skipped)`，用于在设置页一键把殖民地 Pawn 的 RimTalk 人格同步到 RimChat（包含更新/清空/跳过统计）。

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

环境场景条目 `SceneEntries[].Content` 仅支持命名空间变量语法 `{{ namespace.path }}`，运行时走 Scriban strict。  
当前内置变量：

- `{{ world.scene_tags }}`
- `{{ world.environment_params }}`
- `{{ world.recent_world_events }}`
- `{{ world.colony_status }}`
- `{{ world.colony_factions }}`
- `{{ world.current_faction_profile }}`
- `{{ pawn.target.profile }}`
- `{{ pawn.initiator.profile }}`

说明：
- 未识别变量、解析错误、空对象访问都会抛 `PromptRenderException`，不会原文透传。
- 提示词设置页提供实时 Scriban 编译诊断（错误码+行列）与手动校验按钮。

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

## RimTalk Compatibility API (v0.4.11 archived, strict runtime mainline)
- Settings:
  - `RimChatSettings.EnableRimTalkPromptCompat` (default `true`)
  - `RimChatSettings.RimTalkSummaryHistoryLimit` (default `10`, clamped to `1..30`)
  - `RimChatSettings.RimTalkPresetInjectionMaxEntries` (default `0`, clamped to `0..200`, `0 = unlimited`)
  - `RimChatSettings.RimTalkPresetInjectionMaxChars` (default `0`, clamped to `0..200000`, `0 = unlimited`)
  - `RimChatSettings.RimTalkCompatTemplate` (Scriban template used by both diplomacy and RPG prompts)
  - `RimChatSettings.RimTalkPersonaCopyTemplate` (default `{{ pawn.personality }}`, used by RPG persona auto-copy)
- Runtime note:
  - Active prompt rendering pipeline is unified to internal strict Scriban (`PromptTemplateRenderer.RenderOrThrow(...)`).
  - `RimChat.Compat.RimTalkCompatBridge*` files are removed from current codebase (historical docs only).
  - Settings variable browser uses local `PromptVariableCatalog` namespaced snapshot.
- Summary push keys (RimTalk global variable store):
  - `rimchat_last_session_summary`
  - `rimchat_last_diplomacy_summary`
  - `rimchat_last_rpg_summary`
  - `rimchat_recent_session_summaries`
- Prompt pipeline integration:
  - Diplomacy: compatibility block appended at `instruction_stack` tail.
  - RPG: compatibility block appended at `role_stack` tail, plus active RimTalk preset mod-entry render block (`rimtalk_preset_mod_entries`).
  - Active-preset mod-entry injection limits are now settings-driven (entries/chars) and default to unlimited.
  - Render failure policy: strict exception (`PromptRenderException`), no raw-template passthrough fallback.
- Session-end integration:
  - Diplomacy close summary: pushed after summary record creation.
  - RPG close summary (manual close included): built from existing chat history rules (no extra AI call), then pushed.
- `GameComponent_RPGManager` startup/load/finalize path performs compatibility warmup for delayed registration.

## Prompt Domain Runtime Isolation + Self-Heal API (v0.7.24)

- `RimChat.Persistence.PromptPersistenceService.WorkbenchComposer`
  - `BuildUnifiedChannelSystemPrompt(..., deterministicPreview=false)`
    - Runtime path now explicitly uses `deterministicPreview=false`.
    - Workbench structured preview remains deterministic (`true`) via section/workspace preview APIs.
  - Runtime composer now validates required node outputs by channel and throws `PromptRenderException` on empty critical nodes.

- `RimChat.Persistence.PromptPersistenceService`
  - `BuildFullSystemPrompt(...)`
  - `BuildDiplomacyStrategySystemPrompt(...)`
  - `BuildRPGFullSystemPrompt(...)`
    - All runtime entry points are fixed to non-preview rendering.
  - `BuildDiplomacyStrategySystemPrompt(...)` now requires non-empty strategy runtime payload (`negotiator_context/fact_pack/scenario_dossier`) and fail-fast on missing segments.
  - `LoadConfig()`
    - Adds semantic domain validation, startup auto-heal, custom-backup writeback, and migration summary logging.
    - Invalid custom domain data falls back to default-only load only when semantic validation passes.
    - If default-only semantic validation also fails: keep cached config and block writeback; if no cache exists, throw fail-fast `PromptRenderException`.
  - `CreateDefaultConfig()`
    - Strict default-only load path (does not read custom prompt files).
    - Legacy minimal-default fallback via `SystemPromptConfig.InitializeDefaults()` is removed.
  - `AppendDiplomacyResponseFormatSection(...)`
    - Throws on empty `ResponseFormat.JsonTemplate` (runtime fail-fast).

- `RimChat.Persistence.PromptPersistenceService.DomainStorage`
  - `TryLoadPromptDomains(bool includeCustom, out SystemPromptConfig, out int loadedDomainSchemaVersion, out List<string> validationErrors)`
    - New diagnostic overload with semantic validation details.
    - `includeCustom=false` excludes all custom sources, including `PawnDialoguePrompt_Custom.json`.
    - When default-only direct compose fails semantic checks, loader rehydrates from aggregate default-only domain JSON and validates again.
  - Domain semantic validation requirements:
    - `ApiActions` must include full diplomacy default action set.
    - `ResponseFormat.JsonTemplate` must be non-empty.
    - `PromptTemplates.ApiLimitsNodeTemplate / QuestGuidanceNodeTemplate / ResponseContractNodeTemplate` must be non-empty.
  - Action source normalized:
    - `ApiActions` now come only from diplomacy domain.
    - Social domain keeps template text only.

- `RimChat.Persistence.PromptDomainFileCatalog`
  - `ResolveModRoot()` now normalizes loaded-mod roots and only accepts directories containing `Prompt/Default`.
  - If loaded root points to version subfolder (for example `.../RimChat/1.6`), path resolution auto-promotes to parent mod root.

- `RimChat.Persistence.PromptDomainJsonUtility`
  - `LoadSingle<T>()` and `TryDeserialize<T>()` now use `ReflectionJsonFieldDeserializer` first, then fallback to `JsonUtility`.
  - Goal: avoid silent empty-object reads on split-domain JSON payloads.

- `RimChat.Persistence.SystemPromptDomainConfig`
  - Added field: `PromptDomainSchemaVersion` (single-anchor schema marker for domain migration traceability and idempotence).

## Text Integrity Guard API (v0.7.48)

- `RimChat.AI.TextIntegrityGuard`
  - `ValidateVisibleDialogue(string rawOutput)`
    - Scope: diplomacy/RPG visible dialogue only.
    - Behavior: split visible text and trailing `{"actions":[...]}` JSON, sanitize visible text, detect mojibake/fragment corruption.
  - `SanitizeSummaryText(string text, int maxChars = 280)`
    - Scope: summary persistence path.
  - `SanitizeKeyFact(string text, int maxChars = 100)`
    - Scope: summary key-facts persistence path.
  - `TryDetectCorruption(string text, out TextIntegrityIssue issue, out string reasonTag)`
    - Rule tags: `replacement_char`, `control_noise`, `low_printable_ratio`, `fragmented_text`.

- `RimChat.AI.AIChatServiceAsync`
  - `ProcessRequestCoroutine(...)`
    - Added text-integrity retry stage for `DialogueUsageChannel.Diplomacy` and `DialogueUsageChannel.Rpg`.
    - Retry budget: 1.
    - On retry failure: fallback to localized immersion-safe local line.

- `RimChat.Memory.LeaderMemoryManager`
  - `UpsertSummaryInternal(...)`
    - Added pre-upsert sanitization and corruption gate.
  - `TryQueueSummaryRepair(...)` (internal helper in partial)
    - On corrupted summary: queue one repair request.
    - Repair failure or still-corrupt result: drop and log structured warning.

## Diplomacy Action Catalog Injection Fix (v0.7.49)

- Problem
  - `request_raid_call_everyone` and `request_raid_waves` were missing from runtime diplomacy action catalog in some saves/configs.

- Root cause
  - `Prompt/Default/DiplomacyDialoguePrompt_Default.json` did not include those two actions.
  - In custom-domain override scenarios, missing entries in `ApiActions` array propagated to prompt runtime.

- Changes
  - `RimChat.Persistence.PromptPersistenceService.DomainStorage`
    - `BuildApiActions(...)` now enforces required raid-variant actions by appending missing entries:
      - `request_raid_call_everyone`
      - `request_raid_waves`
    - Existing user-configured entries are preserved; only missing fields are backfilled.
  - `RimChat.Persistence.PromptPersistenceService`
    - `BuildCompactActionParameterHint(...)` adds `request_raid_waves -> waves(2-6)`.
  - `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
    - Added default definitions for both actions above.




## 固定种族画像注入（v0.9.38）

- 新增固定注入节点：`mandatory_race_profile`（外交 + RPG 通道，注入位置：`environment` 之后、主链之前）。
- 新增模板字段：`PromptTemplateTextConfig.MandatoryRaceInjectionTemplate`。
- 新增变量：`dialogue.mandatory_race_profile_body`。
- 外交通道来源：`Leader + Negotiator`；RPG 通道来源：`Target + Initiator`。
- 每个角色固定字段：`Role`、`Name`、`RaceKind`、`RaceDef`、`Xenotype`。
- 缺失数据策略：字段值输出 `N/A`，不阻断请求。

## 日志观测本局统计与分页（v0.9.51）

- 模型扩展：
  - `RimChat.AI.AIRequestDebugSnapshot`
  - 新增字段：`SessionSummary: AIRequestDebugSessionSummary`
  - `AIRequestDebugSessionSummary` 字段：
    - `SessionElapsedMinutes`
    - `TotalRequestCount`
    - `TotalTokens`
    - `AverageRequestsPerMinute`
    - `AverageTokensPerMinute`
    - `AverageTokensPerRequest`
- 遥测快照：
  - `RimChat.AI.AIChatServiceAsync.BuildRequestDebugSnapshot(DateTime nowUtc)`
  - 输出口径调整：
    - `Records`：本次游戏进程内完整记录（不做 30 分钟裁剪）
    - `Buckets` / `Summary`：仍为最近 30 分钟口径
    - `SessionSummary`：本局累计聚合
- 对外接口兼容：
  - `AIChatServiceAsync.TryGetRequestDebugSnapshot(out AIRequestDebugSnapshot snapshot)` 签名不变，仅返回模型增强。
