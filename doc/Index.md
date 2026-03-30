# RimChat 模块索引（v0.9.64）

## `+发送信息` 新增挑衅 / 请求商队入口（v0.9.64）
- 目标：在外交窗口 `+发送信息` 中新增“挑衅”和“请求商队”快捷入口，统一走系统消息驱动 AI 回复链，并移除已经失真的 projected goodwill floor 阻断。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.SendInfoActions.cs`
  - `RimChat/DiplomacySystem/ApiActionEligibilityService.cs`
  - `RimChat/Persistence/PromptPersistenceService.cs`
- 链路变化：
  - `OpenSendInfoMenu()` 新增“挑衅”“请求商队”入口；“挑衅”打开独立窗口，展示普通袭击、持续袭击、联合袭击 3 个选项及解释。
  - 联合袭击增加二次确认；确认后写系统消息 `玩家挑衅了对方，将引发“xx袭击”。`，随后立即入队 AI 对话请求。
  - 请求商队入口写系统消息 `玩家向对方请求派遣商队交易。`，随后立即入队 AI 对话请求。
  - `BuildChatMessages(...)` 对“刚写入、且与当前驱动文本相同”的系统消息做去重，避免历史与当前 user message 双重注入。
  - 全局取消 `request_caravan / request_aid / create_quest` 的 projected goodwill floor 阻断，并同步移除提示词层镜像隐藏规则，保持 UI / 提示词 / 运行时一致。

## 空投确认状态机收口根修（v0.9.63）
- 目标：彻底根除空投确认窗口已拿到正确 `preparedTrade` 后，仍被旧 `selection_manual_choice` pending 状态反向重入导致的失败与假死。
- 关键模块：
  - `RimChat/Memory/FactionDialogueSession.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ItemAirdropState.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ItemAirdropConfirmation.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ItemAirdropAsync.cs`
- 链路变化：
  - 新增空投运行态阶段枚举 `Idle/SelectingCandidate/PreparedAwaitingConfirm/Committing/Completed/Failed/Cancelled`，统一记录确认链路阶段。
  - 打开确认窗前统一失效旧的 `pendingDelayedActionIntent/lastDelayedActionIntent` 空投待选负载、异步 request 状态和交易卡运行态，避免旧状态再次映射回 `selection_manual_choice`。
  - 确认提交前新增 fail-fast：若当前阶段不是 `PreparedAwaitingConfirm`，或会话里仍残留空投待选状态，直接阻断提交并写诊断日志。
  - 提交成功、失败、取消、异步回调失败路径统一释放空投临时态，并补齐 `AirdropStateTransition / AirdropConfirmOpen / AirdropConfirmCommitStart / AirdropConfirmCommitResult / AirdropPendingIntentInvalidated / AirdropStalePendingBlocked` 日志。

## 提示词工作台预览缓存上次请求快照（v0.9.60）
- 目标：提示词工作台预览默认使用上次请求的 Scriban 变量快照进行渲染，与 RimTalk 行为一致。
- 关键模块：
  - `RimChat/Prompting/PromptRequestSnapshotCache.cs`
  - `RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs`
- 链路变化：
  - 新增 `PromptRequestSnapshotCache` 按频道缓存 `Dictionary<string, object>` 快照。
  - `BuildRuntimeComposeValues` 在构建后自动调用 `RecordSnapshot` 记录快照。
  - `BuildDeterministicComposeValues` 优先使用快照值，无快照时回退到占位符预览。
  - 快照按频道独立缓存，2小时过期，首次使用或过期后自动回退。

## 社交圈自关系报错根修（v0.9.59）
- 目标：彻底根除社交圈生成中 `PlayerColony -> PlayerColony` 自关系调用导致的关系查询报错。
- 关键模块：
  - `RimChat/DiplomacySystem/Social/SocialCircleService.cs`
  - `RimChat/DiplomacySystem/Social/SocialCircleActionResolver.cs`
  - `RimChat/Patches/FactionGoodwillPatch_NpcDialogue.cs`
- 链路变化：
  - `TryAffectPlayerGoodwill(...)` 新增 fail-fast：玩家派系直接阻断，不再进入 `TryAffectGoodwillWith(Faction.OfPlayer, ...)`。
  - `ApplySoftImpact(...)` 统一改为“去重后的非玩家派系集合”应用软影响，避免 Source/Target 重复与玩家误入。
  - `AddIntentScore(...)` 在意图写入阶段屏蔽玩家派系；`CanAttemptExecution(...)` 在执行阶段再次屏蔽，兼容旧存档残留意图。
  - `FactionGoodwillPatch_NpcDialogue.Prefix(...)` 增加 `__instance == other` 保护，阻断补丁层自关系读取。
  - 所有阻断仅在 DevMode 输出一次性 warning（去重键），正式游玩不刷屏。

## 社交圈原生渲染兼容 fail-fast（v0.9.58）
- 目标：根治“非本机缺失 `ScribanParser.Render` 导致社交圈新闻生成异常”的跨环境兼容问题。
- 关键模块：
  - `RimChat/Prompting/RimTalkNativeRpgPromptRenderer.cs`
  - `RimChat/Prompting/RimTalkPromptRenderCompatibilityException.cs`
  - `RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs`
  - `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.SocialCircle.NewsRequests.cs`
  - `RimChat/AI/AIChatServiceAsync.cs`
  - `RimChat/DiplomacySystem/Social/SocialEnums.cs`
- 链路变化：
  - 原生渲染绑定从单签名改为多签名探测并缓存；兼容失败返回结构化诊断。
  - `social_circle_post` 通道在兼容失败时 fail-fast 阻断，不再发送降级 prompt。
  - `SocialNews` 请求源绕过外交对话 Guard，避免严格 JSON 输出被对话后处理污染。
  - 社交圈失败原因扩展 `PromptRenderIncompatible`，并补齐中英文语言键。
  - parse 失败日志新增 `requestId/debugSource/stage/response_preview` 关联字段。

## 地图栏通讯切换图标热路径优化（v0.9.57）
- 目标：降低 `PlaySettingsPatch_CommsToggleIcon.Postfix(...)` 每帧重复计算导致的 CPU/GC 抖动，同时保持现有交互语义不变。
- 关键模块：
  - `RimChat/Patches/PlaySettingsPatch_CommsToggleIcon.cs`
- 链路变化：
  - `Postfix(...)` 在单次调用内缓存 `WindowStack` 与已打开窗口，避免一次绘制流程内二次窗口扫描。
  - 图标状态判定与点击提交共用同一状态快照，保持“窗口存在即开关状态真值”。
  - tooltip 文案改为状态变化时才重建，减少每帧翻译字符串构造与分配。

## 解析链 fail-fast 根修（v0.9.52）
- 目标：根治赎金外交“解析失败后固定兜底句写入历史导致复读”。
- 关键模块：
  - `RimChat/AI/AIJsonContentExtractor.cs`
  - `RimChat/AI/AIChatServiceAsync.cs`
  - `RimChat/AI/AIChatService.cs`
  - `RimChat/Util/DebugLogger.cs`
- 链路变化：
  - `TryExtractPrimaryText(...)` 改为返回 `PrimaryTextExtractionResult`，输出 `ReasonTag/MatchedPath`。
  - 解析器补充 `content[].text` 提取，合并现有字符串键候选统一评分。
  - `AIChatServiceAsync` 仅对 `empty_primary_text` 重试一次，其他解析失败 fail-fast 返回本地化错误。
  - 移除“解析失败后把 `RimChat_ImmersionFallback_*` 固定句作为 assistant 历史落盘”的路径，阻断复读污染。
  - 新增解析取证日志：记录解析状态、原因、命中路径、内容长度。

## 批量赎金估算价格下调（v0.9.50）
- 目标：批量赎金模式下将“估算赎金价格”统一下调 20%，并同步到批量卡片与批量会话提示中的当前总叫价。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.PrisonerRansomSelection.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.PrisonerRansomBatchRuntime.cs`
  - `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - `1.6/Languages/English/Keyed/RimChat_Keys.xml`
- 链路变化：
  - 新增批量估算系数 `0.8`，批量卡片中的单囚犯估算叫价改为 `~ {estimate}`。
  - 批量卡片改版为“囚犯存活证明”，展示字段改为：所属派系、已选囚犯、估算叫价、健康度、器官状态。
  - 批量会话刷新时的“当前总叫价”同步使用折后估算值；批量总报价区间校验仍沿用原有窗口规则（fail-fast 不变）。

## 批量囚犯赎金覆盖校验根修（v0.9.49）
- 目标：修复批量囚犯赎金在同轮多目标提交时被解析层误去重，导致执行层报“覆盖不完整”的问题。
- 关键模块：
  - `RimChat/AI/AIResponseParser.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.PrisonerRansomBatchRuntime.cs`
- 链路变化：
  - `pay_prisoner_ransom` 解析去重改为按 `target_pawn_load_id` 去重；同一轮不同 target 全部保留，不再按 `action+reason` 折叠。
  - 保持 fail-fast：同 target 重复提交仍被拦截，批量执行仍要求“覆盖全部已选目标且总报价落在区间内”。
  - 增强可观测性：解析层新增结构化汇总日志（保留目标/丢弃重复目标/无目标条目计数）；批量校验失败日志补充 `expected_targets` 与 `actual_targets` 明细。

## 批量囚犯赎金谈判（v0.9.48）
- 目标：把赎金选人与谈判从“单囚犯单笔提交”升级为“多选囚犯批量谈判”，并保持器官丢失惩罚严格执行。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.PrisonerRansomSelection.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - `RimChat/Memory/FactionDialogueSession.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.PrisonerRansom.cs`
  - `RimChat/DiplomacySystem/PrisonerRansomModels.cs`
  - `RimChat/DiplomacySystem/RansomContractManager.cs`
  - `RimChat/AI/AIChatServiceAsync.cs`
  - `RimChat/Config/SystemPromptConfig.cs`
  - `RimChat/action_rules.txt`
  - `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - `1.6/Languages/English/Keyed/RimChat_Keys.xml`
- 链路变化：
  - 囚犯选择弹窗升级为多选，新增 `全选/全不选/确认`，默认全不选。
  - 选中多人后生成批量囚犯情报卡（逐人列表 + 总叫价 + 总报价区间），并写入会话批量运行态。
  - 构建 AI 用户消息时追加 `[RansomBatchSelection]` 引用块，约束本轮多条 `pay_prisoner_ransom` 的覆盖性与总价区间。
  - 执行层新增批量预校验：缺目标、重复目标、未勾选目标、总价越界均 fail-fast；合法后串行执行并首错即停。
  - 串行成功后按目标粒度从待支付集合中扣减；批次完成才清空赎金绑定状态。
  - 批量惩罚中度宽松（时限+50%、普通罚值*0.7、跌价阈值放宽）；器官丢失惩罚保持单人强度，不降级。

## 通讯台替换直开回归根修（v0.9.47）
- 目标：修复“勾选替换通讯台后无法直接进入 RimChat，必须先关闭原版通讯树”的回归。
- 关键模块：
  - `RimChat/Patches/CommsConsolePatch.cs`
- 链路变化：
  - `GetFloatMenuOptionsPostfix(...)` 的接管门禁由“原版 action 声明类型/程序集匹配”改为“可解析到有效派系即可接管”。
  - fail-fast 跳过原因收口为 `NullOption / NullAction / InvalidFaction`，用于稳定输出日志并快速定位未接管原因。
  - `ReplaceCommsConsole=true` 时，通讯台联络项恢复直开 RimChat 外交通讯窗口；`ReplaceCommsConsole=false` 的桥接入口策略保持不变。

## 外交主动根修：恢复在线即清历史队列 + 三层节流强化（v0.9.46）
- 目标：彻底解决“长期未使用通讯台后恢复时主动对话补发/堆积”问题，并降低主动轮询的性能开销。
- 关键模块：
  - `RimChat/NpcDialogue/GameComponent_NpcDialoguePushManager.cs`
  - `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.cs`
  - `RimChat/Config/RimChatSettings_NpcPush.cs`
  - `RimChat/Config/RimChatSettings_AI.cs`
  - `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - `1.6/Languages/English/Keyed/RimChat_Keys.xml`
- 链路变化：
  - Presence 从 `Unavailable -> Online` 发生边沿恢复时，外交主动历史队列按派系立刻清空，不再补发旧触发。
  - 常规外交主动节流升级为配置化默认：全局冷却 `6h`、派系冷却 `3~7天`、队列 `3` 条上限与 `12h` 过期。
  - `GameComponent_NpcDialoguePushManager` 新增活跃候选派系缓存，常规评估读取增量快照并配合低频同步/清理，减少全量重建和临时分配。
  - 新增可控节流调试日志开关，支持定位“全局/派系节流命中”和“恢复清队列”行为。

## 敌对派系入站消息自动重开会话（v0.9.45）
- 目标：根治“派系持续来信但玩家无法回复”的状态断链；同派系只要出现新的普通/系统入站消息，就自动解除会话结束态并恢复可回复状态。
- 关键模块：
  - `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.cs`
  - `RimChat/NpcDialogue/GameComponent_NpcDialoguePushManager.cs`
  - `RimChat/DiplomacySystem/RansomContractManager.cs`
  - `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.SocialCircle.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.Presence.cs`
- 链路变化：
  - 新增统一入口 `HandleInboundFactionMessage(...)`，将“入站消息触发会话重开 + 写消息 + 未读标记 + 记忆同步”收敛到外交管理层单点。
  - `NpcPush`、`Ransom`、`SocialCircle` 三条入站链路全部改为调用统一入口，移除分散的本地重开逻辑，避免遗漏。
  - 外交 UI 发送门控保留 fail-fast，但不再渲染“重新发起对话”按钮；恢复动作完全由入站消息驱动。

## 玩家手动社交圈发帖 + 派系强制主动回应（v0.9.44）
- 目标：允许玩家在外交窗口的 `社交圈` 页内手动发布公开帖子，并在发帖后强制触发 1-3 个相关派系走现有主动对话链路。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.SocialCircleView.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ManualSocialPost.cs`
  - `RimChat/UI/Dialog_ManualSocialPost.cs`
  - `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.SocialCircle.ManualPost.cs`
  - `RimChat/NpcDialogue/GameComponent_NpcDialoguePushManager.ManualSocialPost.cs`
  - `RimChat/DiplomacySystem/Social/SocialEnums.cs`
  - `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - `1.6/Languages/English/Keyed/RimChat_Keys.xml`
- 链路变化：
  - `DrawSocialToolbar(...)` 新增 `发帖/Post` 按钮，打开最小化手动发帖窗口。
  - `Dialog_ManualSocialPost` 只采集标题和正文，并在 UI 层对空值和超长输入执行 fail-fast。
  - `TryPublishManualPlayerSocialPost(...)` 直接创建 `PublicSocialPost`，使用新的 `SocialNewsOriginType.PlayerManual` 写入社交圈，不经过 AI 新闻草稿生成。
  - 手动帖子发布后，系统按标题/正文相关性和派系关系随机挑选 1-3 个有效派系，逐个注入 `RegisterCustomTrigger(...)`。
  - `BuildGenerationMessages(...)` 在 `manual_social_post` 上下文下会追加帖子标题、正文和公开语境，确保派系主动来信明确回应帖文内容。

## 外交策略提示行全局开关（v0.9.43）
- 目标：让外交对话窗口中的策略提示行支持整行点击切换全局策略开关，并在策略按钮可见时自动隐藏该提示行。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.Strategy.cs`
  - `RimChat/Config/RimChatSettings.cs`
  - `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - `1.6/Languages/English/Keyed/RimChat_Keys.xml`
- 链路变化：
  - 新增 `EnableDiplomacyStrategyToggle` 全局持久化配置，默认开启，关闭后跨窗口/跨派系/重开游戏保持状态。
  - `DrawStrategyStatusHint(...)` 改为整行可点击；开启时尾部显示“点击关闭”，关闭时显示“点击开启”。
  - 策略按钮栏显示优先级高于提示行：只要三条策略按钮已可展示，就直接隐藏提示行文字。
  - 外交窗口内的策略 follow-up 请求、策略按钮展示与按钮点击自动发送统一走同一全局门控；关闭时只阻断策略 UI 链路，不改主对话链路。

## 联合袭击专属音效全链路移除（v0.9.42）
- 目标：彻底移除 `sound_request_raid_call_everyone` 专属音效链路，避免继续维护独立资源、播放调用和构建约束。
- 关键模块：
  - `RimChat/AI/AIActionExecutor.cs`
  - `1.6/Defs/SoundDefs/Diplomacy_Sounds.xml`
  - `build.ps1`
  - `doc/Api.md`
  - `doc/config.md`
- 链路变化：
  - `ExecuteRequestRaidCallEveryone(...)` 成功后不再播放 `RimChat_RequestRaidCallEveryone`，联合袭击只保留文本/系统反馈。
  - 删除 `RimChat_RequestRaidCallEveryone` `SoundDef` 与 `1.6/Sounds/sound_request_raid_call_everyone.wav` 资源，不再保留专属音频入口。
  - `build.ps1` 移除联合袭击音频 fail-fast 校验，构建链不再依赖该资源存在。

## 轨道商订单任务禁用根修（v0.9.42）
- 目标：彻底禁止轨道商生成要求殖民者携带指定物资进入地面据点履约的订单任务，并把相关需求统一收口到“解释限制 + 引导空投交易”。
- 关键模块：
  - `RimChat/DiplomacySystem/ApiActionEligibilityService.cs`
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `RimChat/AI/AIActionExecutor.cs`
  - `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
  - `RimChat/action_rules.txt`
  - `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - `1.6/Languages/English/Keyed/RimChat_Keys.xml`
- 链路变化：
  - 轨道商上下文统一由资格层判定，并同时驱动任务 eligibility、动态任务提示和 create_quest 失败回显。
  - `TradeRequest` 在轨道商场景下不再进入 allowed quest 列表；执行期若模型仍输出该任务，会被 fail-fast 拦截。
  - 输出契约与动作附加规则同步明确：轨道商没有地面据点履约链路，具体物资交换只能改走 `request_item_airdrop`。

## 联合袭击动作目录语义澄清 + 默认提示层去 blocked 化（v0.9.41）
- 目标：将 `request_raid_call_everyone` 在提示词动作目录中的定位改为“公开摇人总攻”，并在提示词层默认按普通高强度动作展示，不再把 post-raid 前置当成默认 blocked 提示暴露给模型。
- 关键模块：
  - `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
  - `RimChat/Config/PromptTextConstants.cs`
  - `RimChat/Persistence/PromptPersistenceService.DomainStorage.cs`
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `doc/Api.md`
- 链路变化：
  - `request_raid_call_everyone` 的默认说明与运行时补齐说明统一改写为“跨派系联合总攻”，并明确 `call everyone / 联合袭击 / 都叫来 / 全都叫来 / everyone attack / all in` 属于主动发起总攻的口令。
  - `AppendBlockedActionHints(...)` 在提示词层隐藏 `call_everyone_requires_post_raid_escalation`，避免模型默认把联合袭击视为 blocked 动作；真实资格校验与 UI blocked 原因保持不变。
  - `request_raid_waves` 文案同步改为“持续施压的多波次进攻”，不再沿用“联合袭击不可用时的兜底”叙事。

## 联合袭击动作说明补全 + 别名映射 + 成功音效（v0.9.40）
- 目标：补全 `request_raid_call_everyone` 的用途、触发时机与语义解释，支持“联合袭击”等口语别名，并在动作真正调度成功后播放专属音效。
- 关键模块：
  - `RimChat/AI/AIResponseParser.cs`
  - `RimChat/AI/AIActionExecutor.cs`
  - `RimChat/Config/PromptTextConstants.cs`
  - `RimChat/Persistence/PromptPersistenceService.DomainStorage.cs`
  - `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
  - `1.6/Defs/SoundDefs/Diplomacy_Sounds.xml`
- 链路变化：
  - `NormalizeActionName(...)` 新增 `联合袭击 / 一起上 / 都叫来 / 全都叫来 / everyone_attack / all_in` 到 `request_raid_call_everyone` 的归一化映射。
  - `ExecuteRequestRaidCallEveryone(...)` 仅在 `ScheduleRaidCallEveryone(...)` 成功后播放 `RimChat_RequestRaidCallEveryone`，避免失败时误播。
  - 默认动作说明与运行时补齐说明明确：`call everyone` 用于把普通袭击升级为联合袭击；玩家明确说出相关挑战短语时可视为有效触发意图。

## 提示词去重 + 种族强制注入 + 事件双层压缩（v0.9.39）
- 目标：降低重复注入 token，强制补齐种族画像块，并在事件过载时输出“原文 + 摘要”双层信息。
- 关键模块：
  - `RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs`
  - `RimChat/Persistence/PromptPersistenceService.TemplateVariables.cs`
  - `RimChat/Persistence/PromptPersistenceService.cs`
- 链路变化：
  - 运行时补充块新增 `mandatory_race_profile`（外交/RPG/proactive），并加入运行时必需校验，缺失 fail-fast。
  - 运行时节点去重：已有 `instruction_stack.faction_characteristics` 时抑制 `diplomacy_fallback_role` 重复输出。
  - `world.environment_params` 与 `world.recent_world_events` 改为紧凑快照/摘要口径，完整事件详情只保留在 `<environment>`。
  - `AppendRecentWorldEventIntel(...)` 升级为双层输出：预算溢出时追加 `EventDigest`（类型分布、主题聚类、时序趋势）。

## 外交/RPG 固定种族画像注入（v0.9.38）
- 目标：在外交与 RPG 两条主对话链路强制注入种族画像，不再依赖工作台模板是否显式引用 `pawn.*.profile`。
- 关键模块：
  - `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - `RimChat/Persistence/PromptPersistenceService.TemplateVariables.cs`
  - `RimChat/Config/PromptTemplateTextConfig.cs`
  - `RimChat/Config/RimChatSettings_PromptTemplates.cs`
  - `RimChat/Persistence/PromptDomainPayloads.cs`
  - `RimChat/Persistence/PromptPersistenceService.DomainStorage.cs`
  - `RimChat/Prompting/PromptRuntimeVariableProviders.cs`
  - `RimChat/Prompting/PromptRuntimeVariableBridge.cs`
  - `RimChat/Prompting/PromptVariableTooltipCatalog.cs`
  - `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
- 链路变化：
  - `BuildFullSystemPromptHierarchical(...)` 与 `BuildRpgSystemPromptHierarchical(...)` 在 `environment` 后固定追加 `mandatory_race_profile`。
  - 新增 `BuildMandatoryRaceProfileBlock(...)` + `BuildMandatoryRaceProfileBody(...)`，外交来源为 `Leader + Negotiator`，RPG 来源为 `Target + Initiator`。
  - 固定输出三项：`RaceKind`、`RaceDef`、`Xenotype`；缺失按 `N/A` 输出，不中断请求。
  - 新增模板字段 `MandatoryRaceInjectionTemplate` 与变量 `dialogue.mandatory_race_profile_body`，并接入配置编辑器与本地化。

## 外交通道固定情报注入与交易常识收口（v0.9.37）
- 目标：在运行时强制收口外交交易常识，并在派系提示词后固定注入“当前态 + 历史态”结构化情报，避免被工作台模板覆盖。
- 关键模块：
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - `RimChat/Persistence/DiplomacyFactionFixedIntelBuilder.cs`
  - `RimChat/WorldState/FactionIntelLedgerComponent.cs`
  - `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.EventQueries.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.QuestTracking.cs`
  - `RimChat/Patches/GameAIInterface_QuestTrackingPatch.cs`
  - `RimChat/Patches/ThingTakeDamagePatch_FactionIntelLedger.cs`
  - `RimChat/Patches/WorldObjectDestroyPatch_FactionIntelLedger.cs`
  - `RimChat/Patches/PawnKillPatch_WorldEventLedger.cs`
- 链路变化：
  - `AppendOutputSpecificationAuthorityRules(...)` 新增空投/商队硬约束：即时交换仅 `request_item_airdrop`，单次仅一种换一种，商队延时且不可控货，命中既有事实可让步打折。
  - `ResolveFactionPromptText(...)` 在派系提示词末尾固定追加 `FIXED_FACTION_INTEL`，并随外交、主动外交、策略三通道统一生效。
  - `BuildDiplomacyStrategySystemPromptHierarchical(...)` 新增 `instruction_stack.faction_characteristics` 节点，确保策略链路也稳定注入派系固定情报。
  - `FactionIntelLedgerComponent` 新增据点摧毁历史和袭击破坏聚合账本，持久化并在运行时汇总“最近一次 + 总计”。
  - `GameAIInterface` 通过 `create_quest` 前后置补丁持久化任务发布记录，支持“仅 RimChat 任务发布态”精确判定。

## 同派系按 Pawn 粒度记忆注入隔离根修（v0.9.36）
- 目标：根除同派系内 NPC A 的个人叙事被 NPC B 复用的问题（跨 pawn 摘要污染）。
- 关键模块：
  - `RimChat/Memory/DialogueSummaryService.cs`
- 链路变化：
  - `BuildRpgDynamicFactionMemoryBlock(...)` 增加 fail-fast 约束：`targetPawn == null` 或 `targetPawn.thingIDNumber <= 0` 时不注入动态派系记忆。
  - `CollectSortedSummaries(...)` 改为严格 `PawnLoadId == targetPawnId` 过滤，只允许目标 pawn 自己的跨通道摘要进入 `dynamic_faction_memory`。
  - `dynamic_faction_memory` 标头改为 `TARGET-PAWN SCOPED`，明确该块不再是同派系共享摘要池。

## 归档压缩与持久化记忆隔离根修（v0.9.35）
- 目标：根除 `rpg_archive_compression` 与 `leader_memories` 因存档键算法分叉导致的跨存档/跨对象记忆污染。
- 关键模块：
  - `RimChat/Memory/SaveScopeKeyResolver.cs`
  - `RimChat/Memory/RpgNpcDialogueArchiveManager.cs`
  - `RimChat/Memory/RpgNpcDialogueArchiveManager.Sessions.cs`
  - `RimChat/Memory/LeaderMemoryManager.cs`
  - `RimChat/Memory/LeaderMemoryManager.PersistenceHelpers.cs`
- 链路变化：
  - 新增统一存档键解析器 `SaveScopeKeyResolver`，固定优先级为：`SaveContextTracker -> Current.Game.Info -> ScribeMetaHeader -> persistent slot`，解析失败 fail-fast 拒绝写入。
  - `RpgNpcDialogueArchiveManager` 的 `CurrentSaveKey` 改为统一委托解析器；归档压缩异步回调新增 `request_save_key == current_save_key` 强校验，不一致直接丢弃回调写盘。
  - `LeaderMemoryManager` 移除旧分叉解析路径，写入链路统一走严格存档键；`OnBeforeGameSave/SaveAllMemories/SaveMemory` 增加持久化上下文校验。
  - 新增 `leader_memories` 自动迁移：从 legacy/default 桶迁移到当前严格 saveKey，写入 `.migration_complete_<saveKey>.marker`，遇到重名文件不覆盖并统计 `skipped_existing`。

## 多波次袭击首波社交圈与调度信件（v0.9.34）
- 目标：`request_raid_waves` 在调度成功时立即发信件；第一波成功到达时自动生成 1 条社交圈军事动态。
- 关键模块：
  - `RimChat/DiplomacySystem/DiplomacyEventManager.cs`
  - `RimChat/DiplomacySystem/DelayedDiplomacyEvent.cs`
  - `RimChat/DiplomacySystem/DiplomacyNotificationManager.cs`
  - `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - `1.6/Languages/English/Keyed/RimChat_Keys.xml`
- 链路变化：
  - `ScheduleRaidWaves(...)` 调度成功后追加 `DelayedEventType.RaidWave` 的预警信件通知，包含 `waves|firstMin|firstMax|finalMin|finalMax` 详情。
  - `SendDelayedEventScheduledNotification(...)` 新增 `RaidWave` 分支，渲染“首波/末波窗口”信件文案。
  - `ExecuteRaidOrWaveEvent(...)` 在 `RaidWave && WaveIndex==0` 且事件成功时，调用 `TryEnqueueRaidWavesFirstArrivalSocialPost(...)` 入队军事社交圈。
  - 新增本地化键：`RimChat_DelayedRaidWavesScheduledTitle`、`RimChat_DelayedRaidWavesScheduledDesc`、`RimChat_RaidWavesFirstArrivalSocialPost`（中英）。

## 联合袭击社交圈强制双发（v0.9.33）
- 目标：联合袭击触发后强制由发起派系发布 1 篇社交圈；并在 36 小时后再发布 1 篇跟进社交圈。
- 关键模块：
  - `RimChat/DiplomacySystem/DiplomacyEventManager.cs`
  - `RimChat/DiplomacySystem/DelayedDiplomacyEvent.cs`
  - `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - `1.6/Languages/English/Keyed/RimChat_Keys.xml`
- 链路变化：
  - `ScheduleRaidCallEveryone(...)` 在调度成功后立即调用 `TryEnqueueRaidCallEveryoneSocialPost(..., isFollowup:false)` 强制发帖。
  - 同时新增 `RaidCallEveryoneSocialPost` 延迟事件，执行时间为触发后 `36*2500` tick。
  - `DelayedDiplomacyEvent.Execute(...)` 新增 `RaidCallEveryoneSocialPost` 分支，执行 follow-up 发帖。
  - 新增本地化摘要键：`RimChat_RaidCallEveryoneSocialPostImmediate`、`RimChat_RaidCallEveryoneSocialPostFollowup`（中英）。

## 联合袭击窗口/参与裁剪/事件跳转增强（v0.9.32）
- 目标：将联合袭击窗口收敛到 16-30 小时，并在敌对数量不占优势时按好感度剔除友中立参与者，同时为援军到达信件提供原版“转到事件地点”跳转入口。
- 关键模块：
  - `RimChat/DiplomacySystem/DiplomacyEventManager.cs`
  - `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.cs`
  - `RimChat/AI/AIActionExecutor.cs`
  - `RimChat/Config/PromptTextConstants.cs`
- 链路变化：
  - `ScheduleRaidCallEveryone(...)` 窗口由 16-36h 改为 16-30h（`detail` 同步为 `16|30`）。
  - 新增 `BalanceCallEveryoneParticipants(...)`：当敌对数 `<=` 友中立数时，按 `PlayerGoodwill` 升序逐个剔除友中立，直到敌对数 `>` 友中立数或无可剔除派系。
  - `SendAidLetter(...)` 第 4 参改为有效 `LookTargets`（玩家家园地图中心），援军到达信件可直接“跳转到事件地点”。
  - 旧存档迁移窗口同步改为 16-30h（`MigrateLegacyRaidCallEveryoneEvents(...)`）。

## 联合袭击援军上图坐标根修（v0.9.31）
- 目标：根除 `request_raid_call_everyone` 友中立援军在到达阶段出现 `(-1000,-1000,-1000)` 越界坐标导致“已触发但无援军生效”问题。
- 关键模块：
  - `RimChat/DiplomacySystem/DiplomacyEventManager.cs`
- 链路变化：
  - `TryArriveCallEveryoneAidPawns(...)` 移除 `arrivalMode.Worker.Arrive(...)` 的隐式落点路径。
  - 改为显式流程：先找合法入场点（边缘格，失败回退 `TradeDropSpot`），再按 pawn 逐个在入场点附近可站立格 `GenSpawn.Spawn(...)` 上图。
  - 上图后统一创建 `LordJob_AssistColony`；若最终 0 人上图，按 fail-fast 直接失败并记录详细原因（入口格、尝试数、失败数）。

## 联合袭击友中立支援根修（v0.9.30）
- 目标：根除 `request_raid_call_everyone` 在友好/中立派系上的支援触发失败，并统一到达时机为 16-36 小时随机窗口。
- 关键模块：
  - `RimChat/DiplomacySystem/DiplomacyEventManager.cs`
  - `RimChat/DiplomacySystem/DelayedDiplomacyEvent.cs`
  - `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.cs`
  - `RimChat/AI/AIActionExecutor.cs`
- 链路变化：
  - `ScheduleRaidCallEveryone(...)` 移除友中立“立即执行”分支，敌友全部改为 16-36 小时窗口随机调度。
  - `CallEveryone` 友中立分支新增 `MilitaryAidCustom` 执行路径，使用 RimChat 自建 combat 生成/到达/AssistColony 领主链路，不再依赖 `FriendlyRaid/RaidFriendly` 可执行性。
  - `ProcessDelayedEvents(...)` 对 `RaidCallEveryone` 启用 no-retry 策略，失败即丢弃并记录 fail-fast 日志。
  - 读档 `PostLoadInit` 增加旧队列迁移：未执行的 `RaidCallEveryone` 统一重排到 16-36 小时窗口，友中立动作迁移到 `MilitaryAidCustom`，并清空重试状态。
  - `request_raid_call_everyone` 成功返回文本改为“hostile/friendly-neutral 均为 16|36h 到达窗口”。

## 空投确认数量丢失根修（v0.9.29）
- 目标：根除空投交易在“同意/确认”短回复下丢失请求数量并回退默认数量的问题。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.ItemAirdropConfirmation.cs`
  - `RimChat/Memory/FactionDialogueSession.cs`
- 链路变化：
  - `TryInjectPendingAirdropCountFromLatestPlayerMessage(...)` 现在优先读取 `pendingAirdropTradeCardRequestedCount` 注入 `count`。
  - 只有当会话绑定数量缺失时，才回退到“最新玩家消息文本解析”路径。
  - `count/quantity` 已显式存在时保持原值，不做覆盖。

## 空投确认重入卡死根修（v0.9.28）
- 目标：根除 `request_item_airdrop` 在确认后重复重入 `selection_manual_choice` 导致的卡死/假死。
- 关键模块：
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.Async.cs`
  - `RimChat/DiplomacySystem/ItemAirdropSafetyPolicy.cs`
- 链路变化：
  - `TryBuildAirdropAsyncContext(...)` 新增 `selected_def` 显式透传到 `context.ForcedSelectedDef`，并同步回填 `HasBoundNeed/HadForcedSelectionConflict`，异步链路与同步选择语义对齐。
  - `ContinueAirdropSelectionStageAsync(...)` 在 `ForcedSelectedDef` 存在时可直接走强制选定，不再反复落入手动选择分支。
  - `IsResourceCandidate(...)` 诊断日志改为 DevMode + 窗口限频，避免候选扫描时日志洪泛放大主线程卡顿。

## 空投绑定需求仲裁回归根修（v0.9.27）
- 目标：根除 `request_item_airdrop` 在交易卡绑定 `WoodLog` 时被 `bound_need_family_conflict` 误阻断的回归。
- 关键模块：
  - `RimChat/DiplomacySystem/ItemAirdropSafetyPolicy.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.BoundNeed.cs`
  - `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - `1.6/Languages/English/Keyed/RimChat_Keys.xml`
- 链路变化：
  - `IsResourceCandidate(...)` 恢复“强资源信号优先”：`stuffProps` 资源先判通过，再应用通用排除，避免 `IsWeapon` 噪声误杀原材料。
  - `TryApplyBoundNeedArbitration(...)` 改为绑定优先：`bound_need_def` 可解析时，文本家族冲突改为审计记录（`bound_need_family_conflict_overridden`）而非 fail-fast 阻断。
  - 绑定物资注入候选的 `Family` 改为由绑定物资反推，避免延续漂移文本的家族标签。

## `CallEveryone/Waves` 战斗主动消息强制直通与离场闭环（v0.9.26）
- 目标：实现 `call_everyone` 友中立原版军事支援 + 敌对袭击分流，并保证战斗主动消息不受主动对话频率限制。
- 关键模块：
  - `RimChat/DiplomacySystem/DiplomacyEventManager.cs`
  - `RimChat/DiplomacySystem/DelayedDiplomacyEvent.cs`
  - `RimChat/NpcDialogue/NpcDialogueModels.cs`
  - `RimChat/NpcDialogue/GameComponent_NpcDialoguePushManager.cs`
- 链路变化：
  - `ScheduleRaidCallEveryone(...)` 在调度时写入执行意图：敌对 `Raid`，友中立 `MilitaryAidVanilla(FriendlyRaid)`。
  - `DelayedDiplomacyEvent.ExecuteRaidCallEveryoneEvent(...)` 按执行意图触发 raid/aid，不再把友中立走袭击链路。
  - 战斗主动消息上下文新增 bypass 标记，`WarningThreat` 在 bypass 条件下不再被入口拦截。
  - 战斗消息不再受 faction/global 冷却与玩家忙碌门控；AI 生成失败时 fail-fast 投递 fallback 文案。
  - `Raid/Wave/CallEveryone` 到达消息改为“事件成功后立即触发”。
  - 新增基于参战 pawn 追踪的离场监控；`RaidWaveEndMessage` 改为“最终波次离场后”触发。

## `raid_call_everyone` 日志循环与无效 Def 报错根修（v0.9.25）
- 目标：根除 `ProcessDelayedEvents` 在执行期间新增延迟事件导致的集合枚举修改异常，并去除 `raid_call_everyone` 链路对 `FriendlyRaid` 的执行依赖。
- 关键模块：
  - `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.cs`
  - `RimChat/DiplomacySystem/DelayedDiplomacyEvent.cs`
- 链路变化：
  - `ProcessDelayedEvents(...)` 改为快照遍历，新增事件通过 `AddDelayedEvent(...)` 先进入待合并队列，处理循环结束再统一合并回 `delayedEvents`。
  - 增加同 tick 防重入保护，避免异常路径重复进入造成日志刷屏。
  - `ExecuteRaidCallEveryoneEvent(...)` 统一执行 raid 触发，不再在该链路切入 military aid 执行分支。

## 通讯台原版来源判据收敛（v0.9.24）
- 目标：根除通讯台菜单“派系可解析即拦截”导致的第三方选项误接管问题（如星环通讯扩展）。
- 关键模块：
  - `RimChat/Patches/CommsConsolePatch.cs`
- 链路变化：
  - `GetFloatMenuOptionsPostfix(...)` 现采用 fail-fast 顺序：
    1) `option/action` 为空直接放行；
    2) `IsVanillaCommsAction(action)` 不成立直接放行；
    3) 仅在原版来源成立后再解析派系并决定是否替换 action。
  - 新增 `IsVanillaCommsAction(...)`：基于 `action.Method/DeclaringType/Assembly` 静态识别原版 `Building_CommsConsole` 链路。
  - `ExtractFactionFromOption(...)` 移除“全派系列表 + label 模糊匹配”回退，只保留：
    - action 闭包反射提取 `Faction`；
    - `console.GetCommTargets(myPawn)` + label 匹配。
  - 新增放行原因日志（节流）：`NullOption`、`NullAction`、`NonVanillaAction`、`InvalidFaction`。

## 空投聊天卡配色回调与截断修复（v0.9.23）
- 目标：修正当前灰色内容层与外层气泡的配色协调，同时继续降低物资名、`defName` 与指标行的截断概率。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.ImageRendering.cs`
- 链路变化：
  - 中间灰色内容层改为更适合白字的灰阶配色，提升标题、名称、`defName` 与指标的对比度。
  - 名称区、`defName` 区与指标区高度预算继续放宽，优先保证文本完整可读。

## 空投聊天卡原气泡回调与灰色内容层（v0.9.22）
- 目标：恢复原来的外层气泡配色，同时只在中间需求/出价内容层加灰底，并修复当前过度紧缩导致的文字裁切。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.ImageRendering.cs`
- 链路变化：
  - 外层气泡恢复玩家/AI 原配色，不再整张卡统一灰底。
  - 仅需求物资 / 出价物资中间内容层保留灰色背景。
  - 名称和底部指标标签的高度预算回调，降低文本被截断的概率。

## 空投聊天卡标题层回调（v0.9.21）
- 目标：在保持单层灰底和紧凑布局的前提下，把空投聊天卡标题重新明确展示出来。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.ImageRendering.cs`
- 链路变化：
  - 标题行高度与上下留白略微回调，保证“【空投物资请求】”在极简样式下仍然清晰可读。
  - 不恢复多层底色或装饰块，仍保持单层灰底。

## 空投聊天卡极简灰底收敛（v0.9.20）
- 目标：把聊天区空投卡进一步收敛为“单层灰底 + 极简分隔线”样式，去掉多层色块和装饰感，继续压缩整体高度。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.ImageRendering.cs`
- 链路变化：
  - 空投卡外层统一改为单层灰底，不再区分玩家/AI 的彩色气泡底。
  - 头部标题带、卡内块状底、底栏强调块统一移除，改为轻量文本 + 细分隔线。
  - 缩略图、标题区、指标区再次压缩，卡片更接近紧凑清单布局。

## 空投聊天卡紧凑化与入口冷却阻断（v0.9.19）
- 目标：压缩聊天区空投交易卡高度并根修缩略图/文字遮挡，同时把空投冷却阻断前移到 `+发送信息` 菜单入口，避免玩家在冷却期白填卡。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.ImageRendering.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ActionHint.cs`
- 链路变化：
  - 聊天气泡里的空投卡缩略图、标题带、卡内留白和指标块统一收紧，保留 `label / defName / 数量 / 单价 / 总价`，并按文本真实高度重算卡片高度。
  - `defName` 改为独立单行裁剪显示，不再和物资名、底部指标区互相挤压。
  - `+发送信息 -> 发送空投交易请求` 现在会预先复用 `ApiActionEligibilityService.ValidateActionExecution(... request_item_airdrop ...)`；若命中 `airdrop_cooldown`，菜单项直接以不可点击态显示剩余游戏内时间。
  - 手动打开空投信息卡前会再次做同一份资格校验，确保入口阻断与 `[?]` 提示共用同一条冷却真相源。

## 空投卡视觉与 Presence 状态重置修正（v0.9.18）
- 目标：移除空投卡被忽略时的冗余系统提示，重绘聊天内空投卡视觉，并修正 Presence 中 `DoNotDisturb` 的到期语义。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ImageRendering.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ActionHint.cs`
  - `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.cs`
  - `RimChat/Memory/FactionPresenceState.cs`
  - `1.6/Languages/*/Keyed/RimChat_Keys.xml`
- 链路变化：
  - 空投交易卡发起轮次中，若 AI 未执行 `request_item_airdrop`，前台不再追加“空投信息被忽略，无交易行为”系统消息。
  - 聊天气泡内的空投卡改为沉浸式终端单据风格：顶部标题带、双物资卡分层、独立价格指标区、参考价底栏。
  - `FactionPresenceState` 新增独立 `doNotDisturbUntilTick`，`set_dnd` 现固定维持 3 个游戏日；到期后回到排班态重算，不再复用 `cacheUntilTick` 模糊表示 DND 生命周期。
  - `request_item_airdrop` 冷却提示改为按 RimWorld 游戏内时间展示“剩余 X 天 / X 时”，不再把 `RemainingSeconds` 直接当现实时间显示。

## 空投重报价沉浸式解析升级（v0.9.17）
- 目标：去掉 AI 重报价对“硬编码句式”的依赖，让可见文本恢复角色内对白，同时保持系统对 `item/count/silver/reason` 的稳定提取。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.ItemAirdropCounteroffer.cs`
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `RimChat/UI/Dialog_ItemAirdropTradeCard.cs`
- 链路变化：
  - 重报价解析从“只接受固定模板”升级为“固定模板 + 中文自然句 + 英文自然句”三路兼容。
  - 当自然对白里未显式重复物资名时，解析器会优先回退到当前交易卡绑定的 `NeedDefName`。
  - 二次打开空投交易卡时，默认回填最近一次重报价里的需求数量和银币价格。

## 空投确认数量来源纠偏（v0.9.16）
- 目标：修复交易卡消息同时包含“需求数量 + 付款银币”时，系统把银币数误注入 `request_item_airdrop.count`，导致最终确认数量与谈判数量脱轨的问题。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.ItemAirdropConfirmation.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ActionPolicies.AirdropPending.cs`
- 链路变化：
  - 空投确认前补 `count` 时，优先使用交易卡结构化保存的 `RequestedCount`，不再从整句玩家消息里盲取最大数字。
  - 文本回退解析新增“需求 xN / need xN”优先模式，避免把付款金额识别成需求数量。

## 原木资源资格误判修复（v0.9.15）
- 目标：修复 `WoodLog` 等原材料在资源家族判定中被 `IsWeapon` 噪声标记误杀，导致 `bound_need_family_conflict` 错误阻断的问题。
- 关键模块：
  - `RimChat/DiplomacySystem/ItemAirdropSafetyPolicy.cs`
- 链路变化：
  - 资源资格判定改为“强资源信号优先”。当 `ThingDef` 具备 `stuffProps` 且不属于食物/药物/服装时，先按资源处理，再决定是否排除武器类。
  - `WoodLog`、布料、皮革、矿物等原材料不再因为噪声性的 `IsWeapon` 标记触发错误家族冲突。

## 空投交易卡绑定物品状态贯通根修（v0.9.14）
- 目标：根除“交易卡精确绑定物品在跨回合确认、延迟意图和最终确认弹窗中丢失”的状态断链问题。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ActionPolicies.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ActionPolicies.AirdropPending.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ItemAirdropBoundNeed.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ItemAirdropConfirmation.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ItemAirdropPreSend.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.Async.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.Barter.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.BoundNeed.cs`
- 链路变化：
  - 空投交易卡绑定的 `NeedDefName/NeedLabel/NeedSearchText` 不再在普通确认消息发送前或 AI 回包结束后被无条件清空。
  - 所有 `request_item_airdrop` 动作在进入 delayed intent、跨回合映射和最终确认前，统一补齐 bound need 元数据。
  - 显式人工改选候选物资时，会主动解除交易卡绑定，允许玩家真正切换目标物品。
  - 异步准备完成后新增最终一致性校验；若 `preparedTrade.SelectedDefName` 与交易卡绑定物品不一致，系统直接 fail-fast 阻断，不再弹出错误确认窗。

## 空投绑定需求仲裁根修（v0.9.13）
- 目标：根除“交易卡已精确绑定需求物资，但候选池/确认窗仍显示错误物资”的链路漂移问题。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ItemAirdropBoundNeed.cs`
  - `RimChat/Memory/FactionDialogueSession.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.BoundNeed.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.Barter.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.Async.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.cs`
  - `RimChat/DiplomacySystem/ItemAirdropModels.cs`
- 链路变化：
  - 空投交易卡提交后，会话运行态除 `need/count/payment_items/scenario` 外，额外保存 `NeedDefName/NeedLabel/NeedSearchText`。
  - AI 回包中的 `request_item_airdrop` 动作在执行前统一注入绑定需求元数据，后续异步准备、待确认和最终确认都共享同一份绑定事实。
  - 若候选池不包含绑定需求物资，执行层会记录冲突审计并把绑定物资注入候选池头部，按绑定物资重建交易。
  - 若绑定需求物资无法解析，或与需求家族冲突，系统直接 fail-fast 阻断交易并给出前台可见失败信息。

## 空投请求 UI/超时/匹配链路重构（v0.9.12）
- 目标：统一物资空投请求的搜索绑定、参考价格、气泡展示和二阶段超时语义，彻底移除“超时后等待玩家手动选候选”的旧链路。
- 关键模块：
  - `RimChat/UI/Dialog_ItemAirdropTradeCard.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ImageRendering.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ItemAirdropConfirmation.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ItemAirdropAsync.cs`
  - `RimChat/UI/ItemAirdropTradeCardPayload.cs`
  - `RimChat/DiplomacySystem/ThingDefMatchEngine.cs`
  - `RimChat/DiplomacySystem/ThingDefResolver.cs`
  - `RimChat/DiplomacySystem/ItemAirdropPaymentResolver.cs`
  - `RimChat/Memory/FactionDialogueSession.cs`
- 链路变化：
  - 空投卡顶部改为“精确搜索 + 绑定状态”，中部为“需求卡 / 出价卡”，底部显示需求数量、出价数量与参考价格。
  - 提交前必须精确绑定 `NeedDefName`，不再允许仅凭自由文本 `need` 裸提交。
  - 参考价格定义固定为“需求物资市场价 x 需求数量”，并进入消息结构，在聊天气泡中所见即所得展示。
  - 二次开窗只回填出价区；需求侧不再从上一次重报价或上一条玩家消息自动回灌。
  - `selection_timeout/queue_timeout` 仅保留“默认 Top1 + 最终确认窗”，移除候选系统消息与待玩家二次回复状态。
  - `ThingDefMatchEngine` 成为搜索建议、支付物资解析与候选排序的共享评分入口，统一 exact/normalized/alias/token/semantic/near-match 优先级。

## 空投请求卡重构 - 结构化选品 + 结构化报价（v0.9.10）
- 目标：重做空投卡为"结构化选品 + 结构化报价"流程，不再把需求物资当纯文本处理。
- 关键模块：
  - `RimChat/UI/Dialog_ItemAirdropTradeCard.cs`（重构）
  - `RimChat/UI/SearchStateManager.cs`（新增）
  - `RimChat/UI/ItemAirdropTradeCardPayload.cs`（重构自 Dialog_ItemAirdropTradeCard.cs）
  - `RimChat/DiplomacySystem/ThingDefCatalog.cs`
  - `RimChat/DiplomacySystem/ThingDefResolver.cs`
  - `RimChat/Memory/FactionDialogueSession.cs`
  - `1.6/Languages/*/Keyed/RimChat_Keys.xml`
- 链路变化：
  - 需求物资输入改为"搜索框 + 本地建议列表 + 精确绑定 ThingDef"。
  - 搜索建议完全走本地 `ThingDefCatalog/ThingDefResolver`，不走 AI。
  - 候选数默认 6 个，优先 exact `defName` / exact `label` / 强匹配 token，再走现有 resolver 排序。
  - 选中建议后立即建立结构化绑定：保存 `NeedDefName`、`NeedLabel`、`NeedSearchText`、缩略图来源 `ThingDef`。
  - 玩家继续改动搜索词且不再精确命中当前绑定时，立即清空绑定。
  - 二次回填改为字段级回填：需求物资卡、需求数量、出价数量分别恢复，不再把上一轮结果粗暴写回搜索框。
  - 信息卡界面增加"需求物资卡 / 出价物资卡"，两张卡都显示缩略图、名称、`defName`、数量、市场价/堆叠上限。
  - 底部两个输入标签改为"需求物资数量" / "出价物资数量"。

## 空投信息卡可用性修复（v0.9.9）
- 目标：解决空投信息卡“不可用、语义冲突、遮挡”问题。
- 关键模块：
  - `RimChat/UI/Dialog_ItemAirdropTradeCard.cs`
  - `RimChat/Memory/FactionDialogueSession.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - `1.6/Languages/*/Keyed/RimChat_Keys.xml`
- 链路变化：
  - 卡片交互改为“需求输入 + 以物易物库存选择 + 数量输入”。
  - 以物易物库存来源限定为通电轨道信标覆盖范围，行内显示可用数量与单价。
  - 底部控件改为响应式布局，避免输入框与按钮遮挡。

## 空投信息卡“AI主导议价”落地（v0.9.8）
- 目标：将空投信息卡调整为“参考报价输入”，保留 AI 最终决策权（可拒绝、可重报价、可改参数执行）。
- 关键模块：
  - `RimChat/Memory/FactionDialogueSession.cs`
  - `RimChat/UI/Dialog_ItemAirdropTradeCard.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ItemAirdropCounteroffer.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ActionHint.cs`
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
  - `1.6/Languages/*/Keyed/RimChat_Keys.xml`
- 链路变化：
  - 信息卡提交仅发送自然语言摘要；结构化参数通过会话运行态隐式注入到 AI 请求，不显示在聊天记录。
  - AI 若仅给重报价文本且不返回 `request_item_airdrop`，系统提示“空投信息被忽略，无交易行为”。
  - 新增固定句式重报价解析并缓存到会话，下次开卡自动回填 `item/count/silver`。
  - 库存列表改为“通电轨道信标可达物资 + 可用数量”。
  - `[?]` 受限原因展示改为 code 本地化优先，未命中回退 message，再回退通用文案。

## 启动期 Harmony 补丁自检与参数规范化（v0.9.6）
- 目标：避免 Harmony 按参数名绑定漂移导致的启动级崩溃。
- 关键模块：
  - `RimChat/Patches/TranslatorPatch_RimChatEnglishFallback.cs`
  - `RimChat/Patches/HarmonyPatchStartupSelfCheck.cs`
  - `RimChat/Core/RimChatMod.cs`
- 链路变化：
  - 关键补丁参数改为 `__0/__1` 位置参数风格，规避原方法参数命名变化风险。
  - `RimChatMod` 在 `PatchAll` 前执行最小自检，日志输出通过/失败与失败明细。

## 非中英语言键英文回退根修（v0.9.5）
- 目标：根除切换中英以外语言时 RimChat 键名直出/乱码问题。
- 关键模块：
  - `RimChat/Patches/TranslatorPatch_RimChatEnglishFallback.cs`
  - `doc/Api.md`
  - `doc/config.md`
  - `doc/VersionLog.txt`
  - `doc/VersionLog_en.txt`
- 链路变化：
  - 对 `Translator.TryTranslate` 增加严格后置补丁，仅在 `RimChat_*` 翻译失败时启用英文键表回退。
  - 首次命中回退时输出一次明确告警日志，便于在 `Player.log` 定位语言包缺失。
  - 不影响非 RimChat 翻译键，避免污染游戏本体多语言行为。

## 版本日志语言目录直读与动态语言列表（v0.9.4）
- 目标：移除“中文/其他”硬编码分流，改为按当前活动语言目录直读并在缺失时 fail-fast 回退 English。
- 关键模块：
  - `RimChat/Config/RimChatSettings_APIHeader.UX.cs`
  - `doc/Api.md`
  - `doc/config.md`
  - `doc/VersionLog.txt`
  - `doc/VersionLog_en.txt`
- 链路变化：
  - `AvailableLanguages` 改为动态扫描 `1.6/Languages` 一级子目录。
  - 语言目录解析顺序：直读 `activeLanguage.folderName` -> 归一化匹配 -> 别名匹配 -> fail-fast 回退 `English`。
  - 版本日志文件候选顺序：`VersionLog_<languageFolder>.txt` -> `VersionLog.txt` -> `VersionLog_en.txt`。
  - 缺失目录/文件时统一打印明确 `Log.Warning`，包含活动语言、尝试路径与最终回退路径。

## 空投信息卡与3天空投冷却（v0.9.7）
- 目标：在外交窗口 `+发送信息` 下新增"空投交易信息卡"，玩家手动开卡后填写目标物资、数量、银币出价，提交后自动触发 AI 请求；新增按派系维度的 3 天空投冷却（仅成功提交后触发）；升级全部外交动作 `[?]` 提示，显示本地化受限原因（冷却类显示剩余时间）。
- 关键模块：
  - `RimChat/UI/Dialog_ItemAirdropTradeCard.cs`（新增）
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`（`OpenSendInfoMenu` 扩展）
  - `RimChat/UI/Dialog_DiplomacyDialogue.ActionHint.cs`（受限原因本地化）
  - `RimChat/Config/RimChatSettings.cs`（新增 `ItemAirdropCooldownTicks`）
  - `RimChat/DiplomacySystem/GameAIInterface.cs`（冷却键注册）
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.Barter.cs`（成功后设置冷却）
  - `RimChat/DiplomacySystem/ApiActionEligibilityService.cs`（冷却校验接入）
  - `1.6/Languages/*/Keyed/RimChat_Keys.xml`（新增 Keyed 文本）
- 链路变化：
  - `+发送信息` 菜单新增"发送空投交易请求"，打开信息卡窗口（双列表：推荐候选+库存）。
  - 提交后生成结构化消息块（含 `need/selected_def/count/payment_items`）并立即触发 AI 请求。
  - `CommitPreparedItemAirdropTrade` 成功后 `SetCooldown(faction, "RequestItemAirdrop")`。
  - 冷却期内 `request_item_airdrop` 在 eligibility 与 `[?]` 均显示受限+剩余时间。
  - `[?]` 全部外交动作受限展示本地化原因，冷却时间按天/小时/分钟格式化。

## 空投二阶段移除与手动改选确认（v0.9.3）
- 目标：移除二阶段 AI 选择阻塞点，改为“默认 Top1 + 可手动 Top5 改选”确认流，并修复 `quantity` 数量参数未生效。
- 关键模块：
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.Async.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.SelectionPending.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ItemAirdropConfirmation.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ItemAirdropAsync.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ActionPolicies.AirdropPending.cs`
- 链路变化：
  - 二阶段选择改为本地直出 pending 候选，不再请求 AI 第二跳。
  - 确认窗默认 Top1，并提供低可视度“不是我想要的物品”按钮切换 Top5。
  - 数量参数新增 `quantity` 兼容，避免回退到 `fallback_default_family` 导致低数量。

## 空投二阶段超时根修与数量窗口调整（v0.9.2）
- 目标：根除空投二阶段“固定双轮超时等待”，并移除固定总量截断导致的需求偏移。
- 关键模块：
  - `RimChat/AI/AIChatServiceAsync.cs`
  - `RimChat/AI/AIChatServiceAsync.LocalControl.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.Async.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.cs`
  - `RimChat/Config/RimChatSettings.cs`
  - `RimChat/Config/RimChatSettings_AI.cs`
- 链路变化：
  - `AirdropSelection` 通道禁用本地连接超时重试，超时后直接进入手动候选确认，不再额外叠加一次完整请求超时。
  - 二阶段请求超时与排队超时解耦：新增 `ItemAirdropSecondPassQueueTimeoutSeconds`，并在 dispatch 审计输出 `timeout/queueTimeout`。
  - 二阶段传输审计新增 `firstByteMs/attempts/payloadBytes/http/endpoint`，可直接判定“无首包”链路位置。
  - `hardMax` 由 `预算上限+固定总量上限` 改为 `预算上限+堆叠上限`，数量决策回归“按需求+预算”，同时保留掉落堆叠约束。

## 空投链路诊断与修复（v0.9.1）
- 目标：修复空投同轮多动作全拒、二阶段候选回复重复超时，以及数量来源退化问题；补充可定位的二阶段诊断日志。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ItemAirdropPreSend.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.Async.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.SelectionPending.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.Barter.cs`
- 链路变化：
  - 同轮多条 `request_item_airdrop` 从“全部拒绝”改为“首条成功执行，其余拒绝”。
  - 新增空投候选回复前置直连：发送前命中候选输入即本地映射 `selected_def`，直接走空投确认链路，不再发起新一轮二阶段 AI 请求。
  - 数量决策新增参数来源融合：`parameters.count` 与 `need` 显式数量并存时取较大值；超过 `hardMax` 自动截断并写入审计来源。
  - 二阶段新增阶段诊断日志，支持在 `Player.log` 区分 `queue_timeout`、请求超时、解析失败。

## 囚犯信息卡器官对账与读档报价刷新（v0.8.20）
- 目标：记录囚犯信息卡核心器官快照，离图后按“新增缺失”延迟判定离图失败；同时根修读档后赎金预估不刷新。
- 关键模块：
  - `RimChat/DiplomacySystem/PrisonerRansomService.cs`
  - `RimChat/DiplomacySystem/PrisonerRansomModels.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.PrisonerRansom.cs`
  - `RimChat/DiplomacySystem/RansomContractManager.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.PrisonerRansomSelection.cs`
  - `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.cs`
  - `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - `1.6/Languages/English/Keyed/RimChat_Keys.xml`
- 链路变化：
  - 信息卡发送时写入核心器官快照（心/肝/肺/肾/眼，多重计数），同囚犯后发覆盖前发。
  - `CalculatePrisonerRansomQuote` 支持 `forceRefresh`，信息卡发布链路固定强刷报价。
  - 新增赎金运行态清空入口，并在 `StartedNewGame/LoadedGame` 时清空，消除读档后报价缓存残留。
  - 合约持久化新增器官快照/新增缺失/延迟失败调度字段，旧档默认值兼容。
  - 离图命中“新增核心器官缺失”时，跳过即时价值缩水处罚，改为 5-10 小时后按超时级重罚并在社交圈谴责文本中带器官缺失事实。

## 赎金合约健康离图回执与超时谴责增强（v0.8.19）
- 目标：在不移除原惩罚链路的前提下，补全赎金“履约回执”和“超时舆情后果”。
- 关键模块：
  - `RimChat/DiplomacySystem/PrisonerRansomModels.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.PrisonerRansom.cs`
  - `RimChat/DiplomacySystem/RansomContractManager.cs`
  - `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - `1.6/Languages/English/Keyed/RimChat_Keys.xml`
- 链路变化：
  - `RansomContractRecord` 新增目标名称快照与健康离图延迟回执状态（调度/到期/已发送）字段，并持久化兼容旧档默认值。
  - 囚犯离图时启用严格健康判定（健康 >= 85%、意识 >= 85%、非倒地）；命中后写入延迟回执任务（5-10 游戏小时）。
  - 合约 Tick 循环新增延迟回执扫描：到期后向外交会话注入 NPC 主动回复，并发 `ChoiceLetter_NpcInitiatedDialogue` 来信提醒。
  - 超时分支保持原有重罚与报复袭击，同时新增会话警告消息 + 来信，并通过 `EnqueuePublicPost(...)` 触发派系首领社交圈谴责（AI 生成）。

## 赎金证据卡二次放大与空白收敛（v0.8.18）
- 目标：进一步拉近 pawn 贴框效果，同时扩大文字区并减少证据卡内部留白。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.ImageRendering.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
- 链路变化：
  - 证据卡缩略图 zoom 从 `1.40` 提升至 `1.75`，并使用略下偏移的中心裁切（`pivot=(0.5, 0.58)`）强化主体占比。
  - 证据卡内容留白收敛：内边距、头部间距、图文间距、底部 padding 同步下调。
  - 仅在 `IsOutboundPrisonerInfoMessage(msg)` 分支加宽气泡（`0.72` 比例 + 更高 min/max），其余消息气泡策略不变。

## 赎金证据卡 Pawn 放大与 ID 字段替换（v0.8.17）
- 目标：在不调整气泡宽度和文字区宽度的前提下放大证据卡 Pawn 可见主体，并将“当前叫价”字段改为可追踪的 Pawn 唯一 ID。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.ImageRendering.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.PrisonerRansomSelection.cs`
  - `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - `1.6/Languages/English/Keyed/RimChat_Keys.xml`
- 链路变化：
  - `DrawOutboundPrisonerInfoBubble(...)` 改为中心裁切放大绘制（固定 zoom），仅影响 `rimchat://ransom-proof` 证据卡缩略图呈现。
  - `BuildRansomProofCaption(...)` 第 `{5}` 字段从 `currentAsk` 改为 `pawn.GetUniqueLoadID()` 原样字符串（空值回退 `RimChat_Unknown`）。
  - 囚犯信息卡字段顺序校验从“当前叫价/Current ask”同步到“ID”，并保留 legacy 顺序兼容历史文本。

## 赎金超时重入与重复选人稳定化（v0.8.14）
- 目标：根除“超时后自动回复链路重入”与“已有绑定目标时重复 request_info 弹窗”两类抖动。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.PrisonerRansomSelection.cs`
  - `RimChat/Memory/FactionDialogueSession.cs`
- 链路变化：
  - `TryHandleRequestInfoActionForPrisoner(...)` 增加去重短路：若会话已绑定有效赎金目标，直接返回成功语义并跳过选人弹窗。
  - `StartRansomTargetSelection(...)` 增加重入门禁：选人进行中拒绝重复启动；已有有效绑定目标时复用目标并跳过重选。
  - `TryQueueReplyForPlayerPrisonerInfoCard(...)` 增加超时冷却门禁：命中超时分类后进入 90 秒冷却，冷却期抑制自动重发，手动发送保持可用。
  - 新增超时分类日志（`queue_timeout/network_timeout/drop_timeout`）与去重命中日志，便于 Player.log 快速定位。
  - 新增字段仅为运行态：`ransomAutoReplyCooldownUntilRealtime`、`ransomAutoReplyCooldownCategory`，不写入存档。

## 赎金承诺动作一致性（MUST）强化（v0.8.13）
- 目标：进一步压缩“文本已付款但无动作提交”的概率，覆盖更多完成态措辞。
- 关键模块：
  - `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
  - `RimChat/Config/SystemPromptConfig.cs`
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `RimChat/action_rules.txt`
- 链路变化：
  - 将赎金承诺约束提升为 MUST，覆盖“已提交/已支付/钱货两清/已放人离开”等文本。
  - 通信终端语境约束补充“禁止带人离开/到场处理”等线下完成态叙述。
  - 压缩响应合同（短上下文）同步注入同一硬规则，避免规则在裁剪模式下丢失。

## 通信终端与赎金承诺动作一致性强化（v0.8.12）
- 目标：约束模型始终按“通信终端在线聊天”语境输出，并保证赎金承诺与动作提交同轮一致。
- 关键模块：
  - `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
  - `RimChat/Config/SystemPromptConfig.cs`
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `RimChat/action_rules.txt`
- 链路变化：
  - 默认/系统/迁移规则统一注入“非线下会面”约束，禁止线下到场式表述。
  - 新增赎金强规则：若文本承诺“已提交/已支付赎金”，同条回复必须包含 `pay_prisoner_ransom` 动作。

## 赎金单次支付提交重构（v0.8.11）
- 目标：将 `pay_prisoner_ransom` 从“代码议价+自动放人”重构为“单次支付+玩家手动放人”。
- 关键模块：
  - `RimChat/DiplomacySystem/GameAIInterface.PrisonerRansom.cs`
  - `RimChat/DiplomacySystem/PrisonerRansomService.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `RimChat/Config/SystemPromptConfig.cs`
  - `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
- 链路变化：
  - 移除 `counter_offer/rejected_floor_not_met` 议价状态分支与自动 `ReleasePrisoner` 链路。
  - `pay_prisoner_ransom` 成功路径变为：参数/目标/区间校验通过 -> 空投银币 -> 登记合约 -> 返回 `paid_submitted`。
  - 成功后立即清理 `request_info(prisoner)` 绑定状态；放人由玩家手动操作。
  - 缺失或失效目标时回退到 `request_info(prisoner)` 选人语义。

## 赎金非终态反馈可视化（v0.8.10）
- 目标：解决 `pay_prisoner_ransom` 执行后返回 `counter_offer/rejected_floor_not_met` 但界面缺少明确反馈的问题。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - `1.6/Languages/English/Keyed/RimChat_Keys.xml`
- 链路变化：
  - 在成功动作系统消息追加链路中新增赎金结果分支。
  - `counter_offer` 显示“报价被拒 + 当前还价 + 轮次”。
  - `rejected_floor_not_met` 显示“谈判已结束 + 底价要求”。

## 赎金报价回归区间校验（v0.8.9）
- 目标：满足“报价在有效区间内即可”的谈判规则，不再强制命中当前叫价。
- 关键模块：
  - `RimChat/DiplomacySystem/GameAIInterface.PrisonerRansom.cs`
  - `RimChat/Config/SystemPromptConfig.cs`
  - `RimChat/AI/AIChatServiceAsync.cs`
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
- 链路变化：
  - 移除执行层 `offer_must_match_current_ask` 失败分支，保留报价窗口校验（min/max）作为唯一硬门禁。
  - 默认提示词与系统/迁移规则不再注入“必须等于当前叫价”约束，避免运行时回灌旧规则。

## 赎金当前叫价硬校验（v0.8.8）
- 目标：根除“文本声称已付款但实际未成交”的误导链路。
- 关键模块：
  - `RimChat/DiplomacySystem/GameAIInterface.PrisonerRansom.cs`
  - `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - `1.6/Languages/English/Keyed/RimChat_Keys.xml`
- 链路变化：
  - `pay_prisoner_ransom` 在执行层新增硬门禁：若存在 `currentAsk`，`offer_silver` 必须等于 `currentAsk`。
  - 报价未命中当前叫价时直接 fail-fast，并返回带 `offered/currentAsk/min/max` 的系统提示。

## 赎金终态清理修复（v0.8.7）
- 目标：根除“赎金支付成功后误清理目标绑定导致重复选人”的死循环。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.PrisonerRansomSelection.cs`
- 链路变化：
  - `pay_prisoner_ransom` 仅在终态成功 `accepted_and_released` 时清理赎金绑定状态。
  - `counter_offer` 与 `rejected_floor_not_met` 保留 `boundRansomTargetPawnLoadId / boundRansomTargetFactionId / hasCompletedRansomInfoRequest`，允许同目标继续议价。
  - 新增赎金成功状态分流日志：终态成功清理、非终态成功保留绑定。

## 赎金 request_info 条件触发化（v0.8.6）
- 目标：将赎金 `request_info` 从“强制前置”改为“缺信息时才触发”，允许已知目标时直接支付。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.PrisonerRansomSelection.cs`
  - `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
  - `RimChat/Config/SystemPromptConfig.cs`
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `RimChat/AI/AIChatServiceAsync.cs`
  - `RimChat/action_rules.txt`
- 链路变化：
  - 移除 `pay_prisoner_ransom` 对 `hasCompletedRansomInfoRequest` 的硬门禁。
  - `pay_prisoner_ransom` 缺少有效 `target_pawn_load_id` 时才触发选人补信息提示。
  - 默认提示词与迁移补丁从“必须先 request_info”改为“缺目标信息才 request_info”。
  - 不改存档结构，继续复用 `isWaitingForRansomTargetSelection / boundRansomTargetPawnLoadId / boundRansomTargetFactionId / hasCompletedRansomInfoRequest` 运行态字段。

## 囚犯信息卡玩家消息化与自动回复（v0.8.5）
- 目标：将囚犯信息卡从系统消息调整为玩家消息，并在发送后自动触发 AI 回复。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.PrisonerRansomSelection.cs`
- 链路变化：
  - `PublishRansomProofCard(...)` 中囚犯信息卡改为 `isPlayer=true`，发送者绑定我方谈判者。
  - 新增 `TryQueueReplyForPlayerPrisonerInfoCard(...)`，在卡片发布后复用外交请求链路自动发起 AI 回复。
  - 保持赎金状态机与存档结构不变。

## 外交“+发送信息”入口与囚犯信息卡重构（v0.8.4）
- 目标：在外交发送区增加“+发送信息”纯文字入口，并将囚犯信息卡改为我方视觉发送与紧凑布局。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.PrisonerRansomSelection.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.Speakers.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ImageRendering.cs`
- 链路变化：
  - 发送区新增 `+发送信息` 文字入口（无按钮样式），可用性与 `SendGateState` 一致，点击弹出轻量菜单。
  - 菜单当前仅提供“发送囚犯信息”，复用既有 `Dialog_PrisonerRansomTargetSelector`，并支持手动入口不插入“先选囚犯”系统提示。
  - 囚犯存活证明卡保持系统语义（`isPlayer=false`），但视觉归属我方：右侧气泡、我方头像、我方配色。
  - 囚犯卡改为横向紧凑布局（左图右文），缩略图使用 `ScaleAndCrop` 降低留白，占用高度显著下降。

## 赎金报价窗口显式注入与越界反馈修复（v0.8.4）
- 目标：根除“报价越界后用户只看到内部错误串、模型继续盲猜报价”的链路问题。
- 关键模块：
  - `RimChat/DiplomacySystem/PrisonerRansomService.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.PrisonerRansom.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.PrisonerRansomSelection.cs`
  - `RimChat/Config/SystemPromptConfig.cs`
  - `RimChat/Persistence/PromptPersistenceService.cs`
- 链路变化：
  - 统一提供报价窗口计算入口（min/max），执行层与 UI 前置提示复用同一口径。
  - `request_info(prisoner)` 成功选人后新增“当前可报价区间”系统消息，注入到谈判上下文。
  - 越界报价失败改为可读提示：展示 `offered/min/max/currentAsk`，并保留会话状态用于重试。
## 赎金前置 request_info 链路稳定化（v0.8.3）
- 目标：根除“赎金意图未稳定触发选人”的链路缺口，强制执行 `request_info(prisoner) -> pay_prisoner_ransom`。
- 关键模块：
  - `RimChat/AI/AIActionNames.cs`
  - `RimChat/DiplomacySystem/ApiActionEligibilityService.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.PrisonerRansomSelection.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - `RimChat/Memory/FactionDialogueSession.cs`
  - `RimChat/AI/AIResponseParser.cs`
  - `RimChat/AI/AIChatServiceAsync.cs`
  - `RimChat/Config/SystemPromptConfig.cs`
  - `RimChat/Persistence/PromptPersistenceService.cs`
- 链路变化：
  - 新增 `request_info` 动作契约（仅支持 `info_type=prisoner`），由外交执行层触发稳定选人弹窗。
  - `pay_prisoner_ransom` 增加会话级前置门禁：未完成 request_info 或无合法绑定目标时直接 fail-fast。
  - `AIResponseParser` 将缺参赎金动作统一归一化为 `request_info(prisoner)`，避免动作链路断点。
  - 赎金支付成功后重置会话赎金状态（waiting/bound/preflight），防止跨轮状态污染。

## 发送区提示按图定位（v0.7.110）
- 目标：将 `[?]` 移动到发送按钮右下侧（按 UI 示意图红框位置）。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.ActionHint.cs`
  - `RimChat/UI/Dialog_RPGPawnDialogue.ActionHint.cs`
- 链路变化：
  - 提示锚点改为 `sendRect.xMax - 16, sendRect.yMax + 2`。
  - 提示框尺寸改为 `24x18`，保持可见且不压住输入框。


## 空投显式数量优先根修（v0.7.106）
- 目标：根除 `request_item_airdrop` 在“需求含显式数量”场景下被二阶段模型错误压成 `count=1` 的链路问题。
- 关键模块：
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.Async.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.Barter.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.SelectionPending.cs`
- 链路变化：
  - `ValidateAirdropSelection(...)` 新增显式数量优先策略：`need` 抽取到明确数量时，直接覆盖二阶段返回 `count` 并统一做合法窗口校验。
  - 二阶段提示词移除“single-item/count=1”误导语句，改为“显式数量优先，模型仅负责选品”。
  - 异步链路的 `selection` 审计改为记录最终生效数量，避免“日志是模型数量、执行是修正数量”的偏差。
  - `countSource` 统一为 `llm/fallback_explicit/fallback_default_family`，与文档约定一致。

## 空投二阶段异步化与主线程阻塞根修（v0.7.105）
- 目标：根除 `request_item_airdrop` 二阶段 `Task.Wait(timeout)` 主线程阻塞，并保持超时后候选确认链路。
- 关键模块：
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.Async.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ItemAirdropConfirmation.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ItemAirdropAsync.cs`
  - `RimChat/AI/AIChatServiceAsync.cs`
  - `RimChat/AI/AIChatServiceAsync.RequestScheduling.cs`
  - `RimChat/Memory/FactionDialogueSession.cs`
- 链路变化：
  - 外交空投准备改为异步入口 `BeginPrepareItemAirdropTradeAsync`，别名扩展与二阶段选择统一走 `AIChatServiceAsync`。
  - 空投链路移除同步 `Task.Wait` 阻塞，二阶段与别名超时改为请求级超时配置驱动（按空投设置覆盖）。
  - 对话层新增空投异步 requestId/lease 运行态，窗口关闭时立即取消并清理状态，禁止跨窗口回调落地。
  - 输入区新增“空投二阶段匹配中”非阻塞状态条；不锁输入，不冻结窗口。
  - 超时仍进入 Top3 候选确认（`1/2/3` 或 defName/名称）原链路，兼容旧交互。

## 空投支付语义匹配修复（v0.7.104）
- 目标：修复 `payment_item_unresolved` 在“词序变化/插词”的支付品输入下误拒绝问题。
- 关键模块：
  - `RimChat/DiplomacySystem/ItemAirdropPaymentResolver.cs`
- 链路变化：
  - 支付解析新增语义分词匹配层（CamelCase/label 拆词 + 全包含匹配打分）。
  - `MealPackaged` 这类输入可稳定匹配到 `MealSurvivalPackaged` 候选，不再直接 unresolved。
  - 并列候选仍走 `payment_item_ambiguous` fail-fast，不放宽安全边界。

## 空投预算单一真相源 + 超时提示强制可见（v0.7.103）
- 目标：根除 `budget_silver` 与 `payment_items` 双字段分歧，并消除 `selection_timeout` 被叙事文本覆盖后的“无提示”体验。
- 关键模块：
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.Barter.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ActionPolicies.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ActionPolicies.AirdropPending.cs`
  - `RimChat/AI/AIResponseParser.cs`
  - `RimChat/DiplomacySystem/ApiActionEligibilityService.cs`
  - `RimChat/Config/SystemPromptConfig.cs`
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
  - `RimChat/action_rules.txt`
- 链路变化：
  - 预算改为由 `payment_items` 市场价汇总后 `Floor` 派生；`budget_silver` 若存在仅用于审计，不参与执行。
  - 空投准备链路改为先做支付解析/预算派生，再进入候选选择与数量合法校验。
  - `selection_timeout` 场景下外交 UI 强制追加系统候选提示，不再依赖“台词为空才显示”。
  - 单金额跟进映射改为仅补 `payment_items`（`Silver x amount`），不再补 `budget_silver`。

## 空投二阶段超时根修 + 可观测增强（v0.7.102）
- 目标：降低 `request_item_airdrop` 二阶段稳定 `~12s` 超时，并补齐失败语义与调试观测。
- 关键模块：
  - `RimChat/AI/AIChatClient.cs`
  - `RimChat/AI/AIChatServiceAsync.DebugTelemetry.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.SelectionPending.cs`
  - `RimChat/Config/RimChatSettings.cs`
  - `RimChat/Config/RimChatSettings_AI.cs`
- 链路变化：
  - 二阶段请求改用结构化响应对象（含 `failureReason/http/tokens`），不再只有裸文本。
  - 失败语义细分：`selection_timeout / selection_queue_timeout / selection_service_error`。
  - `selection_timeout/selection_queue_timeout` 仍走“待玩家确认候选（Top3）”；`selection_service_error` 保持 fail-fast。
  - 二阶段提示词候选压缩为关键字段并限制 Top20，减少 token 压力。
  - `ItemAirdropSecondPassTimeoutSeconds` 默认值从 `12` 调整为 `25`（约束保持 `3..30`）。

## 空投支付解析根修 + 超时待确认（v0.7.101）
- 目标：根除 `payment_item_unresolved/payment_item_ambiguous` 的高频误匹配，并移除二阶段超时自动成交风险。
- 关键模块：
  - `RimChat/DiplomacySystem/ItemAirdropPaymentResolver.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.Barter.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.cs`
  - `RimChat/DiplomacySystem/ItemAirdropModels.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ItemAirdropConfirmation.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ActionPolicies.cs`
  - `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
  - `RimChat/Config/SystemPromptConfig.cs`
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `RimChat/action_rules.txt`
- 链路变化：
  - 支付解析独立模块化：统一“`defName` 精确 -> `label` 精确 -> 归一化强匹配 -> 近似匹配”。
  - 并列最高分改为 fail-fast：返回 `payment_item_ambiguous` + Top3 候选，禁止自动扣货。
  - 二阶段 `selection_timeout` 改为“待玩家确认候选（Top3）”，不再自动 Top1 成交。
  - 外交 UI 新增待确认提示与候选回复映射：玩家回复编号/名称后回填 `selected_def` 并重提同一空投动作。
  - 契约收口：`payment_items.item` 提示更新为“defName 优先，label 仅备用（需唯一可解析）”。

## 外交空投金额简写映射根修（v0.7.100）
- 目标：修复外交跟进语句仅给金额时的预算/支付错配，防止沿用旧失败文案造成“文本误报”。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.ActionPolicies.cs`
  - `RimChat/Memory/FactionDialogueSession.cs`（复用既有运行态意图，不新增持久化字段）
  - `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - `1.6/Languages/English/Keyed/RimChat_Keys.xml`
- 链路变化：
  - 新增空投金额简写映射：当存在上一条空投意图且玩家输入匹配单金额（如 `2100银` / `2100 silver`）时，自动补齐 `budget_silver=amount` 与 `payment_items=[{item:Silver,count:amount}]` 并直接入执行链路。
  - 多金额表达（如“预算2100支付2000”）不会触发该映射，继续走原有确认/澄清路径。
  - 映射命中后覆盖可见回复为确定性提示（中英语言键），避免沿用模型中的过期失败文本。

## 空投缺参阻断 + 契约收口（v0.7.99）
- 目标：根除 `request_item_airdrop` 因缺失 `payment_items` 导致的稳定失败链路，避免进入运行时后段才抛错。
- 关键模块：
  - `RimChat/AI/AIResponseParser.cs`
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `RimChat/action_rules.txt`
- 链路变化：
  - 解析期 fail-fast：`AIResponseParser.AddActionIfValid` 对 `request_item_airdrop` 增加参数结构校验（`need`、`budget_silver`、`payment_items[]`，每项 `item/count`）。
  - 缺参动作直接丢弃并记录日志，不再进入执行链路触发 `payment_items_missing`。
  - 紧凑动作目录契约修正：`request_item_airdrop` 明确 `budget_silver/payment_items` 必填，并写明支付价值约束（`>= budget`、超付 `<= 5%`）。
  - `action_rules` 示例同步更新为包含 `payment_items` 的结构化清单，消除旧示例误导。

## AI 空投以物易物 + 最终确认弹窗（v0.7.98）
- 目标：将 `request_item_airdrop` 升级为“真实物资折银交易 + 玩家最终确认”链路，彻底避免未确认即扣货/发货。
- 关键模块：
  - `RimChat/AI/AIResponseParser.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.Barter.cs`
  - `RimChat/DiplomacySystem/ApiActionEligibilityService.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ItemAirdropConfirmation.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ActionPolicies.cs`
  - `RimChat/Config/SystemPromptConfig.cs`
  - `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
- 链路变化：
  - `request_item_airdrop` 契约升级：`budget_silver` 与 `payment_items` 改为必填。
  - 交易准备阶段新增：`payment_items` 解析、`item`（defName/label/别名）解析、预算/超付校验、轨道信标覆盖库存校验、扣货计划生成。
  - 外交 UI 新增最终确认弹窗：确认后立即执行“扣货 + 空投”，取消则终止并写系统消息。
  - 同轮多条空投动作 fail-fast 拒绝，避免并发确认窗和重复扣货。
  - `AIResponseParser` 参数解析升级为通用 JSON 值解析，支持 object/array/bool/null，消除数组参数退化为字符串的问题。

## RPG 首轮延迟治理与思维链通道化（v0.7.97）
- 目标：根治 RPG 新会话首轮“长时间思考中”。
- 关键模块：
  - `RimChat/UI/Dialog_RPGPawnDialogue.cs`
  - `RimChat/UI/Dialog_RPGPawnDialogue.RequestContext.cs`
  - `RimChat/Memory/RpgNpcDialogueArchiveManager.cs`
  - `RimChat/Memory/RpgNpcDialogueArchiveManager.Warmup.cs`
  - `RimChat/Persistence/RpgPromptTurnContextRuntime.cs`
  - `RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs`
  - `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - `RimChat/Prompting/RimTalkNativeRpgPromptRenderer.cs`
  - `RimChat/Prompting/PromptRuntimeVariableBridge.cs`
  - `RimChat/Config/RimChatSettings.cs`
  - `RimChat/Config/RimChatSettings_AI.cs`
  - `RimChat/Config/PromptChannelToggleConfig.cs`
- 链路变化：
  - RPG 窗口首轮改为单次构建缓存复用，移除同窗口首轮双重系统提示词构建。
  - 开窗即异步预热归档缓存；首轮提示词构建禁用冷加载与压缩调度。
  - 归档压缩调度改为主线程安全检查点触发，避免后台线程直接触发请求链路。
  - 思维链开关从全局布尔升级为按通道矩阵，`rpg_dialogue/proactive_rpg_dialogue` 默认关闭。

## 外交意图到动作双层根治（v0.7.96）
- 目标：根治“识别到意图但未触发动作”与“口头承诺已提交但无实际 action”问题。
- 关键模块：
  - `RimChat/AI/DiplomacyResponseContractGuard.cs`
  - `RimChat/AI/AIChatServiceAsync.cs`
  - `RimChat/UI/Dialog_DiplomacyDialogue.ActionPolicies.cs`
  - `RimChat/Memory/FactionDialogueSession.cs`
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `Prompt/Default/PromptUnifiedCatalog_Default.json`
- 链路变化：
  - 新增外交契约 fail-fast：检测“强承诺 + 无 actions JSON”，自动重试一次；重试后仍违规时回退为角色内澄清追问。
  - 外交主链新增意图映射：对模糊催单先追问，收到确认后补发延迟动作；缺必填参数时持续追问，不执行动作。
  - 覆盖延迟动作：`request_item_airdrop/request_caravan/request_aid/request_raid/trigger_incident/create_quest`。
  - 新增短窗口防重：同动作同参数在 2 个助手回合内不重复执行。
  - 运行态新增 `PendingDelayedActionIntent`（不入档）用于跨轮确认与去重。

## 外交界面空投成功系统提示（v0.7.95）
- 目标：在外交对话窗口内，为 `request_item_airdrop` 成功执行提供可见系统反馈，且与原有信件并存。
- 关键模块：
  - `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.cs`（读取返回 payload，不改外部契约）
  - `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - `1.6/Languages/English/Keyed/RimChat_Keys.xml`
- 链路变化：
  - `ActionExecutionOutcome` 新增 `Data` 承载，动作执行成功时透传 API 返回数据。
  - 新增空投成功系统消息注入分支：仅匹配 `request_item_airdrop` 且 `ActionExecutionOutcome.IsSuccess=true`。
  - 系统消息从 `ItemAirdropResultData` 读取 `ResolvedLabel/Quantity/BudgetUsed`，避免自然语言反解析误差。
  - 本地化新增键：`RimChat_ItemAirdropTriggeredSystem`（中英双端）。

## request_item_airdrop 单物品数量真相源根修（v0.7.94）
- 目标：统一数量合法性真相源，根除“超时回退默认 25 -> 校验再打回”的链路冲突。
- 关键模块：
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.cs`
- 链路变化：
  - 新增统一数量窗口函数 `ComputeLegalCountWindow(...)`，回退、提示词、验证三处共享同一计算。
  - `ExtractRequestedCount` 改为三态语义（无数字 / 单数字 / 多数字）。
  - 单物品硬约束：`need` 含多数字直接 fail-safe，返回 `need_count_ambiguous`。
  - 二阶段超时回退改为“先取 Top1 + 先算 hardMax 再决策”，显式超量直接 `selection_count_out_of_range`。
  - 二阶段提示词新增预算与候选 `max_legal_count`，并明确 `count must be 1..max_legal_count` 规则。
  - `selection` 阶段审计新增 `countSource`、`hardMax`、`maxByBudget`，便于区分模型问题与回退策略问题。

## request_item_airdrop 候选目录过滤根修（v0.7.92）
- 目标：修复候选目录过度过滤导致的“全量 familyReject + 尸体 near-miss”异常。
- 关键模块：
  - `RimChat/DiplomacySystem/ThingDefCatalog.cs`
- 链路变化：
  - 取消 `scatterableOnMapGen/mineable` 的目录级硬排除，恢复常见资源进入目录。
  - 新增 `def.IsCorpse` 目录级排除，防止尸体 Def 污染候选池。
  - 与 `ItemAirdropSafetyPolicy` 的族群约束配合后，候选召回恢复到“先可见物资，再安全筛选”。

## request_item_airdrop 候选召回根修与诊断增强（v0.7.91）
- 目标：在不改变 AI 二阶段选品契约的前提下，根治 `no_candidates` 高概率误空集，并提升失败可定位性。
- 关键模块：
  - `RimChat/DiplomacySystem/ItemAirdropIntentParser.cs`
  - `RimChat/DiplomacySystem/ThingDefResolver.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.cs`
  - `RimChat/DiplomacySystem/ItemAirdropModels.cs`
- 链路变化：
  - 输入归一化增强：支持 `| / \ 、，。；：` 边界分隔与中英数混写拆分（`steel10个` -> `steel|10|个`）。
  - 噪声清洗：过滤数量/单位 token，减少无意义检索词污染。
  - 候选召回增强：新增本地同义词扩展（先本地再 AI），并在评分前增加强匹配通道（def/label 归一化包含关系加权）。
  - 诊断可观测：候选包新增拒绝计数与 near-miss，`prepare/failed` 阶段审计输出完整定位摘要。

## 人格引导请求移除与 RimTalk-only 同步（v0.7.90）
- 目标：彻底移除 `persona_bootstrap` 外发请求，仅保留 RimTalk 人格复制/同步。
- 关键文件：
  - `RimChat/DiplomacySystem/GameComponent_RPGManager.PersonaBootstrap.cs`
- 链路变化：
  - `StartNpcPersonaGeneration(...)` 不再向 AI 发送 persona 引导请求。
  - `ProcessNpcPersonaBootstrapTick(...)` 在无 RimTalk 时 fail-fast 完成 bootstrap，不再进入请求链。
  - `ProcessNpcPersonaRuntimeTick(...)` 在无 RimTalk 时关闭 runtime 扫描，避免无效循环。
  - `RetryOrFallbackPersonaPrompt(...)` 不再重试 AI 生成人格，也不再写入本地 fallback 文本。

## request_item_airdrop 两阶段链路（v0.7.89）
- 目标：把单阶段 Def 解析改为“候选构建 + 二次选择 + 严格校验 + 执行投放”。
- 关键模块：
  - `RimChat/DiplomacySystem/ItemAirdropModels.cs`
  - `RimChat/DiplomacySystem/ItemAirdropIntentParser.cs`
  - `RimChat/DiplomacySystem/ItemAirdropSafetyPolicy.cs`
  - `RimChat/DiplomacySystem/ItemAirdropSelectionParser.cs`
  - `RimChat/DiplomacySystem/ThingDefResolver.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.cs`
- 链路变化：
  - 阶段1：按自然语言 need 构建候选；首轮候选为空时，自动触发一次 AI 中英文别名扩展并重试。
  - 阶段1.5：若已识别族群且候选仍为空，执行一次同族群放宽重试（仅放宽类别黑名单，不跨族群）。
  - 阶段2：`channel:airdrop_selection` 请求进行候选内选择，严格解析 `selected_def/count/reason`。
  - 阶段3：执行前做候选归属、预算与数量越界校验；失败即 fail-fast，不做降级回退。
  - 排序优化：`defName/label` 精确命中优先，提高 `steel` 等明确物资词的命中稳定性。
  - fail-fast：经过别名扩展与重试后仍无法归类或无候选时，返回 `need_family_unknown/no_candidates`。
  - 审计：统一写入 `RequestItemAirdrop.Stage`（prepare/selection/execute/failed）与最终 `RequestItemAirdrop` 结果。
  - 可观测性：`AIRequestDebugSource` 新增 `AirdropSelection`，API Debug 窗口可见。

## 真实物资检索与空投链路（v0.7.86）
- 目标：让 AI 按需求检索真实 ThingDef（可来自原版与已加载 Mod），并通过原版空投发送到殖民地。
- 关键文件：
  - `RimChat/DiplomacySystem/ThingDefCatalog.cs`
  - `RimChat/DiplomacySystem/ThingDefResolver.cs`
  - `RimChat/DiplomacySystem/GameAIInterface.ItemAirdrop.cs`
  - `RimChat/AI/AIActionExecutor.ItemAirdrop.cs`
- 链路变化：
  - 新增动作 `request_item_airdrop`，从解析、资格校验、执行器到 API 层全链路打通。
  - 检索策略为“规则先筛 + Top1 选择”，命中黑名单/预算不足/无落点/无匹配 Def 时 Fail Fast。
  - 预算优先级：`budget_silver` > `scenario=ransom`（财富 1%）> AI 默认预算，并统一受配置上下限夹紧。
  - 执行使用 `DropPodUtility.DropThingsNear(...)`，落点为殖民地中心附近随机合法格。
  - 新增玩家可见成功/失败信件与开发审计日志（请求参数、候选集、最终 Def、数量、失败码）。

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
  - 顺序：RimTalk 人格 -> RimChat 已存人格（不再触发 persona_bootstrap 外发请求）。
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

## 外交动作注入修复（v0.7.49）
- 症状与根因：
  - `request_raid_call_everyone` 与 `request_raid_waves` 在运行期动作目录缺失。
  - 根因是默认外交域文件 `Prompt/Default/DiplomacyDialoguePrompt_Default.json` 未包含这两个动作，且用户 custom 域可能覆盖默认数组。
- 修复点：
  - `RimChat/Persistence/PromptPersistenceService.DomainStorage.cs`
  - 在 `BuildApiActions(...)` 增加必需袭击变体动作补齐逻辑（仅补齐缺失项，不覆盖已有配置）。
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `BuildCompactActionParameterHint(...)` 增加 `request_raid_waves` 参数签名 `waves(2-6)`，避免动作目录参数语义丢失。
  - `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
  - 默认外交域补充 `request_raid_call_everyone` / `request_raid_waves` 动作定义。

## 日志观测本局统计与分页（v0.9.51）
- 目标：
  - 在日志观测趋势区右侧新增“本局统计”面板，仅显示 3 个均值指标。
  - 记录列表改为“本次游戏进程完整记录 + 分页浏览”，不再按最近 30 分钟裁剪列表数据。
- 关键实现：
  - `RimChat/AI/AIRequestDebugModels.cs`
  - `AIRequestDebugSnapshot` 新增 `SessionSummary`（`AIRequestDebugSessionSummary`）。
  - `RimChat/AI/AIChatServiceAsync.DebugTelemetry.cs`
  - `BuildRequestDebugSnapshot(...)` 返回全量 session `Records`；趋势桶与顶部汇总仍按最近 30 分钟。
  - 新增 session 聚合：`AverageRequestsPerMinute`、`AverageTokensPerMinute`、`AverageTokensPerRequest`。
  - `RimChat/UI/Dialog_ApiDebugObservability.cs`
  - 趋势区拆分为左侧趋势图 + 右侧本局统计面板；列表新增分页控件（首页/上一页/下一页/末页）。
  - 每页条数按可视区域动态计算（`floor(listHeight / RowHeight)`，最小 1）。
- 本地化：
  - 新增中英文语言键：本局统计 3 项、分页按钮、页码信息。

