# RimChat 外部配置说明（v0.3.29）

## 响应解析与生命周期修复（v0.3.114，无新增用户配置）

本版本无新增开关，行为为内建修复：

- AI 响应解析改为容错提取器，提升不同提供商返回格式的兼容性。
- 主页面签窗口重复打开时会自动恢复好感度动画事件订阅。
- 存档名解析增加反射兜底链，降低异常回退到 `Default` 的概率。

## 异步稳定性机制（v0.3.113，无新增用户配置）

本版本的请求生命周期加固为内建行为，无需用户手动配置：

- 跨存档隔离：新开局/读档时自动取消旧上下文挂起请求。
- 回调防护：旧上下文请求即使晚到，也不会继续写入当前会话。
- 内存清理：异步请求结果会定时清理并做数量上限裁剪。
- 外交窗口关闭保护：关闭窗口会取消该窗口的挂起主回复与策略补充请求。
- 记忆加载策略：`LeaderMemoryManager` 在加载期预热缓存，运行期不再按需阻塞单文件读取。

## Prompt Policy V2（v0.3.110）

### 配置入口

- `Mod 设置 -> 提示词 -> 高级 -> Prompt Policy`

### 新增可配置项

- `Enabled`：启用策略层预算与动作映射。
- `GlobalPromptCharBudget`：全局提示词字符预算。
- `NodeBudgets`：节点级预算（`environment`/`dynamic_npc_personal_memory`/`actor_state`/`api_contract` 等）。
- `TrimPriorityNodeIds`：超预算时全局裁剪优先顺序（每行一个节点 ID）。
- `EnableIntentDrivenActionMapping`：启用 RPG 意图驱动动作映射层。
- `IntentActionCooldownTurns`：意图映射动作冷却回合。
- `IntentMinAssistantRoundsForMemory`：协作意图触发 `TryGainMemory` 的最小助手轮数。
- `IntentNoActionStreakThreshold`：no-action 连击兜底阈值。
- `ResetPromptCustomOnSchemaUpgrade`：schema 升级时重置旧 Prompt 自定义覆盖并重建 V2 默认。
- `SummaryTimelineTurnLimit`：RPG 记忆摘要最多回合数。
- `SummaryCharBudget`：RPG 记忆摘要字符预算。

### Prompt 模板新增字段（PromptTemplates）

- `DecisionPolicyTemplate`
- `TurnObjectiveTemplate`
- `TopicShiftRuleTemplate`

### 默认文件与持久化

- 默认值来源：`Prompt/Default/SystemPrompt_Default.json`
  - `PromptPolicySchemaVersion = 3`
  - `PromptPolicy` 对象（预算/映射默认参数）
- 自定义持久化：`Prompt/Custom/system_prompt_config.json`
- 升级行为：检测到旧 schema 且 `ResetPromptCustomOnSchemaUpgrade=true` 时，清空旧覆盖并重建 V3 默认模板。

## 提示词设置全挂载（v0.3.103）

### RPG 提示词（Mod 设置 -> RPG 对话）

- 新增分区：
  - `RPG 兜底模板`
  - `RPG API 模板`
- 可编辑项已覆盖 `Prompt/Default/RpgPrompts_Default.json` 对应字段：
  - `RoleSettingFallbackTemplate`
  - `FormatConstraintHeader`
  - `CompactFormatFallback`
  - `ActionReliabilityFallback`
  - `ActionReliabilityMarker`
  - `DecisionPolicyTemplate`
  - `TurnObjectiveTemplate`
  - `OpeningObjectiveTemplate`
  - `TopicShiftRuleTemplate`
  - `ApiActionPrompt` 全字段（full/compact 模板、动作行、动作名列表）
- 持久化路径：
  - 读取：`Prompt/Custom/RpgPrompts_Custom.json`（存在时）-> `Prompt/Default/RpgPrompts_Default.json`
  - 保存：仅写入 `Prompt/Custom/RpgPrompts_Custom.json`

### 系统提示词模板（Mod 设置 -> 提示词 -> 高级）

- `PromptTemplates` 分区现在只负责外交模板，支持直接编辑以下字段：
  - `FactGroundingTemplate`
  - `OutputLanguageTemplate`
  - `DiplomacyFallbackRoleTemplate`
  - `DecisionPolicyTemplate`
  - `TurnObjectiveTemplate`
  - `TopicShiftRuleTemplate`
  - `ApiLimitsNodeTemplate`
  - `QuestGuidanceNodeTemplate`
  - `ResponseContractNodeTemplate`
- 持久化路径：`Prompt/Custom/system_prompt_config.json`

### 社交圈 Prompt（Mod 设置 -> 提示词 -> 高级 -> 社交圈 Prompt，v0.3.106）

- 专用分区统一编辑：
  - `PromptTemplates.SocialCircleActionRuleTemplate`
  - `ApiActions.publish_public_post.Description`
  - `ApiActions.publish_public_post.Parameters`
  - `ApiActions.publish_public_post.Requirement`
  - `ApiActions.publish_public_post.IsEnabled`
- 持久化路径：
  - 默认读取：`Prompt/Default/SystemPrompt_Default.json`
  - 修改保存：`Prompt/Custom/system_prompt_config.json`
- `PromptTemplates` 通用分区不再重复展示 `SocialCircleActionRuleTemplate`，避免入口分散。

## API 页最近对话 Token 用量（v0.3.29）

### 配置入口（Mod 设置 -> API 配置）

- 该项显示在 API 页面底部：`最近一次对话Token使用量：xxxx（低/中/高）`。
- 统计范围：仅统计外交对话与 RPG 对话窗口发起的请求。
- 数据来源：
  - 优先读取响应 `usage` 对象中的 token 字段（兼容 `prompt_tokens/input_tokens/promptTokenCount`、`completion_tokens/output_tokens/candidatesTokenCount`、`total_tokens/totalTokenCount`）。
  - 缺失 usage，或 usage 与本地估算差异过大时，按“请求+响应文本（4字符≈1token）”回退估算，并显示“估算”标记。
- 分档阈值：低 `<=1200`，中 `1201~3000`，高 `>3000`。

## RPG-外交双向记忆链路（v0.3.29）

### 行为口径（固定策略）

- 离图触发仅认 `Pawn.ExitMap(bool, Rot4)`，不包含 `DeSpawn/死亡`。
- 外交会话结束定义为：外交窗口关闭且本次会话存在新增消息。
- 摘要策略：规则优先；低置信触发 LLM 回退；AI 不可用时回退规则摘要且不中断流程。

### 预算与注入限制（默认固定）

- 离图摘要池上限：`20`
- 外交会话摘要池上限：`20`
- RPG 动态注入条数：最近 `6` 条（按相关性排序）
- RPG 动态注入总长上限：`2200` 字符

### 提示词注入语义

- RPG 注入为运行时临时拼接，不调用 `SetPawnPersonaPrompt`，不会覆盖 Pawn Persona 持久化配置。
- 注入位置：原人格提示词后、API 规则前。
- 注入来源：同派系共享记忆（外交摘要 + 离图摘要 + 关键事件），保证同派系 Pawn 开聊可读一致长期记忆。

## 社交圈公开公告系统

### 入口变更（v0.3.14）

- 社交圈展示入口已从主窗口迁移到 外交对话窗口 内页签。
- 底部主窗口按钮已隐藏（不再提供主窗口入口）。

### 配置入口（Mod 设置 -> AI 控制 -> 社交圈设置）

- `EnableSocialCircle`
  - 总开关。关闭后不会自动生成、也不会处理公开公告动作。
- `SocialPostIntervalMinDays` / `SocialPostIntervalMaxDays`
  - 自动公告最小/最大间隔天数。
  - 默认：`5` / `7`。
- `EnablePlayerInfluenceNews`
  - 是否允许“玩家对话影响”进入社交圈（显式动作 + 关键词兜底）。
- `EnableAISimulationNews`
  - 是否启用系统按周期自动生成 AI 推演公告。
- `EnableSocialCircleAutoActions`
  - 是否启用“公告意图 -> 自动行动执行”。
  - 默认关闭，开启后仅在意图分达到阈值时尝试执行。
- 社交圈扩展影响事件池（原版 Core，中低威胁）
  - `ColdSnap` / `CropBlight` / `HeatWave` / `SolarFlare` / `Flashstorm`
  - 事件由帖子类别与情绪分布概率触发，并受世界状态与当前地图约束。

### 调试按钮

- `RimChat_SocialForceGenerateButton`（UI 按钮）
  - 始终显示在社交圈设置分区。
  - 点击后强制生成一条公告，并重排下一次自动生成时间。
  - 当社交圈总开关关闭或未进入存档时，按钮不可执行并显示提示。

## NPC 主动对话系统

### 配置入口（Mod 设置 -> AI 控制 -> UI 设置 -> NPC 主动对话设置）

- `EnableNpcInitiatedDialogue`
  - 总开关。关闭后不会再产生新的主动触发评估与投递。
- `NpcPushFrequencyMode`
  - 主动消息频率档位：`Low` / `Medium` / `High`。
  - 影响每次周期评估中的触发概率。
- `NpcQueueMaxPerFaction`
  - 每个派系的延迟队列上限。
  - 超限时按最早入队项淘汰。
- `NpcQueueExpireHours`
  - 队列消息过期时长（小时）。
  - 超时消息会在队列处理时自动丢弃。
- `EnableBusyByDrafted`
  - 忙碌判定：任一殖民者处于征召状态。
- `EnableBusyByHostiles`
  - 忙碌判定：玩家家园地图存在敌对已生成单位。
- `EnableBusyByClickRate`
  - 忙碌判定：6 秒窗口内左键点击次数达到阈值（默认 `>=12`）。
- `RimChat_NpcPush_DebugForceTrigger`（UI 调试按钮）
  - 立即强制触发一条随机主动对话（用于联调；仍依赖 AI 配置可用）。

### PawnRPG 主动通道（v0.3.19）

- 与旧派系主动通道并行、互不替换；旧通道规则保持不变。
- 支持玩家派系内部主动对话（玩家派系 Pawn 可主动对其他玩家派系 Pawn 发起）。
- 复用当前 `NPC 主动对话设置` 配置分区（总开关、频率档、队列参数、忙碌判定开关）。
- 新增调试按钮：`RimChat_PawnRpgPush_DebugForceTrigger`
  - 强制触发一条 PawnRPG 主动对话用于联调。
  - 保留旧调试按钮，两者互不影响。

### PawnRPG 固定运行策略（非配置项）

- 6 天单 NPC 节流：每个 NPC 成功投递后 `150000` ticks 内不再评估/发起。
- 3 天全局节流：非警告类成功投递后 `75000` ticks 内不再成功投递 PawnRPG 主动消息。
- 警告类例外：仅绕过 3 天全局节流，不绕过 6 天单 NPC 节流。
- 阈值调整：非亲密关系需 `Opinion >= 35`；条件触发低心情阈值为 `Mood <= 0.30`。
- 忙碌三重判定持续生效：`Drafted` / 敌对单位 / 高频点击。
- NPC 与玩家 Pawn 睡觉/昏迷/工作中时，触发入延迟队列等待。
- 队列策略保持：每派系默认上限 `3` 条，默认 `12` 小时过期。
- LLM 失败策略保持：每条消息重试 `1` 次，连续失败后丢弃。
- 冷却口径：仅“成功投递”才更新单 NPC 与全局冷却计数。
- 从 PawnRPG 主动信件打开对话时，会直接以该主动消息作为会话首句，不会再次请求 LLM 重新生成开场。

### 运行策略（非配置项）

- 本节描述旧派系主动通道固定策略（PawnRPG 通道规则见上节）。

- 常规触发评估间隔：`6000` ticks。
- 队列处理间隔：`600` ticks。
- 同派系主动发言冷却：成功后随机 `1~3` 天。
- 在线门控：仅 `Online` 可直接投递，离线/勿扰入队等待。
- LLM 失败策略：每条触发仅重试 `1` 次，失败后丢弃并写日志。

## 降好感策略按钮（外交窗口）

### 行为说明（当前版本无可配置开关）

- 仅在 **本轮 AI 回复导致净好感度下降** 时生效。
- LLM 可选返回 `strategy_suggestions`（必须 3 项）：
  - `short_label`
  - `trigger_basis`
  - `strategy_keywords`
  - `hidden_reply`
- 玩家只看到按钮策略，不直接看到 `hidden_reply`。
- 点击按钮会立即发送 `hidden_reply`；手动发送同样会清空本轮策略。
- 策略按钮仅对下一次发送有效，发送后立即失效。
- 若模型未返回合法 JSON 策略字段，客户端会发起一次补充请求；若补充请求仍是自然语言，将从叙述中提取策略句进行 3 按钮兜底。

## 在线状态系统

### 基础配置（Mod 设置 -> AI 控制 -> UI 设置 -> 在线状态设置）

- `EnableFactionPresenceStatus`
  - 是否启用派系在线状态系统。
- `PresenceCacheHours`
  - 关闭外交对话窗口后，按派系缓存在线状态的时长（小时）。
  - 默认：`2`。
- `PresenceForcedOfflineHours`
  - AI 执行 `go_offline` 后，保持强制离线的时长（小时）。
  - 默认：`24`。
- `PresenceNightBiasEnabled`
  - 是否启用夜间偏离线规则。
- `PresenceNightStartHour` / `PresenceNightEndHour`
  - 夜间判定时段（0-23）。
  - 默认：`22` 到 `6`。
- `PresenceNightOfflineBias`
  - 夜间在线时转离线的附加概率系数（0.0-1.0）。
  - 默认：`0.65`。

### 高级配置（展开“科技等级在线模板”）

- `PresenceUseAdvancedProfiles`
  - 是否启用按科技等级自定义在线模板。
  - 默认：`true`（默认启用科技等级影响在线时长）。
- 每个模板包含：
  - `PresenceOnlineStart_*`：在线起始小时。
  - `PresenceOnlineDuration_*`：在线总时长（小时）。

可配置模板：
- `Default`
- `Neolithic`
- `Medieval`
- `Industrial`
- `Spacer`
- `Ultra`
- `Archotech`

## 状态语义

- `Online`：可发送消息。
- `Offline`：窗口只读，不可发送消息。
- `DoNotDisturb`：窗口只读，不可发送消息。

状态颜色固定：
- 在线：绿
- 离线：灰
- 请勿打扰：红

## AI 动作协议

- `exit_dialogue`：结束当前对话轮次，保持当前在线状态。
- `go_offline`：结束对话并切换为离线。
- `set_dnd`：切换为请勿打扰并停止交互。

## RPG 对话固定策略（v0.3.35，非配置项）

- `ExitDialogueCooldown` 冷却固定为 `60000` ticks（1 天）。
- 当 RPG 对话达到 `5` 轮后，会执行一次 `80%` 记忆判定；成功时自动追加 `TryGainMemory`。
- 不会自动补全 Recruit；仅执行模型原始输出的 Recruit 动作。
- 对话窗口系统提示改为半透明面板，展示冷却剩余时长与记忆判定结果。

## RPG 独立人格 Prompt

### 配置入口（Mod 设置 -> RPG 对话 -> 殖民地 Pawn 独立人格）

- 支持为每个殖民地人类 Pawn（殖民者/囚犯/奴隶）设置独立人格 Prompt。
- 该 Prompt 仅在该 Pawn 作为 RPG 对话目标时注入系统提示词。
- 清空文本即删除该 Pawn 的独立人格 Prompt。
- 配置按存档持久化（不会污染其他存档）。
- 该分区新增调试按钮：`RimChat_PawnRpgPush_DebugForceTrigger`，可直接强制触发一条 PawnRPG 主动对话用于联调。

### 首次加载旧存档自动 NPC 画像（v0.3.109）

- 触发时机：
  - 仅在“旧存档首次加载且尚未完成画像标记”时触发一次。
  - 新开档默认标记为已完成，不触发该流程。
  - 当引导版本升级时，会自动失效旧完成标记并重跑一次。
- 覆盖目标：
  - 当前地图中已存在的人形 Pawn（含玩家派系）。
  - 已知可见派系的领袖 Pawn。
- 生成链路：
  - 使用 `PromptPersistenceService.BuildPawnPersonaBootstrapProfile(Pawn)` 组装人格专用精简上下文。
  - 仅包含背景、特质、核心技能、派系角色与意识形态。
  - 显式排除健康/需求/心情/伤病/装备/基因/临时事件等非人格信息。
  - 异步串行调用 LLM，按 `Prompt/Default/RpgPrompts_Default.json` 中的人格模板配置生成人格文本。
  - 模板格式固定：
    - `He/She is a [core temperament] person who tends to [emotional pattern], usually handles situations by [behavioral strategy], because deep down they seek [core motivation], but this also makes them [defense/weakness], often leading to [personality cost].`
  - 示例参考：
    - `He is a calm and analytical person who rarely shows his emotions and tends to approach problems through careful observation and planning, because deep down he seeks control and security, but this also makes him distant and slow to trust others.`
  - 输出目标：保持单行、人格导向、短句表达，并统一使用 Pawn 对应的人称代词。
- 写入策略：
  - 复用现有 `SetPawnPersonaPrompt` 持久化字段（与手动编辑同源）。
  - 仅对“当前为空”的 Pawn 独立人格字段写入，不覆盖已有自定义文本。
  - 失败会重试；重试失败后写入模板化兜底人格文本，避免留空。

## 环境提示词系统（v0.3.23）

### 配置入口（Mod 设置 -> Prompts -> 环境提示词）

- `Worldview.Enabled` / `Worldview.Content`
  - 世界观全局提示词层。启用后在外交与 RPG 两条系统提示词链路前置注入。
- `SceneSystem.Enabled`
  - 场景提示词总开关。
- `SceneSystem.MaxSceneChars`
  - 单条场景内容硬上限（默认 `1200` 字符）。
- `SceneSystem.MaxTotalChars`
  - 场景层总字符硬上限（默认 `4000` 字符）。
- `SceneSystem.PresetTagsEnabled`
  - 是否启用系统自动标签（`channel:*` / `source:*` / `scene:*` 及关系、心情、健康等标签）。
- `EventIntelPrompt.Enabled`
  - 事件记忆注入总开关（默认开启）。
- `EventIntelPrompt.ApplyToDiplomacy` / `EventIntelPrompt.ApplyToRpg`
  - 事件记忆应用通道开关。
- `EventIntelPrompt.IncludeMapEvents`
  - 是否注入公开地图事件（如寒潮、热浪、枯萎病、袭击、殖民者死亡等信件事件）。
- `EventIntelPrompt.IncludeRaidBattleReports`
  - 是否注入袭击聚合战报（攻击方/守方死亡 + 守方倒地峰值）。
- `EventIntelPrompt.DaysWindow`
  - 注入读取窗口天数（默认 `15`）。
- `EventIntelPrompt.MaxStoredRecords`
  - 事件账本总存储上限（默认 `50`，FIFO 裁剪）。
- `EventIntelPrompt.MaxInjectedItems`
  - 单次注入最多条目数（默认 `8`）。
- `EventIntelPrompt.MaxInjectedChars`
  - 事件记忆块最大字符数（默认 `1200`）。

### 共享提示词模板（PromptTemplates，v0.3.64）

- `PromptTemplates.Enabled`
  - 模板渲染总开关。关闭后回退旧版硬编码拼接文本。
- `PromptTemplates.FactGroundingTemplate`
  - 作用于 `fact_grounding` 节点（外交/RPG 共用）。
- `PromptTemplates.OutputLanguageTemplate`
  - 作用于 `output_language` 节点（外交/RPG 共用）。
- `PromptTemplates.DiplomacyFallbackRoleTemplate`（v0.3.65）
  - 在无派系专属 Prompt 时，作为外交通道角色兜底文本。
- `PromptTemplates.SocialCircleActionRuleTemplate`（v0.3.105）
  - 注入外交分层 prompt 的 `social_circle_action_rule` 节点，用于约束 `publish_public_post` 的使用场景与语义一致性。
  - v0.3.106 起，编辑入口迁移到独立“社交圈 Prompt”分区。
- `Prompt/Default/RpgPrompts_Default.json`（v0.3.120）
  - RPG 角色设定、格式约束、动作可靠性、开场目标与 topic shift 默认文本改由该文件统一提供，不再从外交 `PromptTemplates` 读取。
- `PromptTemplates.ApiLimitsNodeTemplate`（v0.3.66）
  - 包装 `api_limits` 节点动态正文，默认 `{{api_limits_body}}`。
- `PromptTemplates.QuestGuidanceNodeTemplate`（v0.3.66）
  - 包装 `quest_guidance` 节点动态正文，默认 `{{quest_guidance_body}}`。
- `PromptTemplates.ResponseContractNodeTemplate`（v0.3.66）
  - 包装 `response_contract` 节点动态正文，默认 `{{response_contract_body}}`。

支持占位符（`{{variable}}`）：
- `{{channel}}`：`diplomacy` 或 `rpg`
- `{{mode}}`：`manual` 或 `proactive`
- `{{target_language}}`：当前输出语言
- `{{faction_name}}`、`{{initiator_name}}`、`{{target_name}}`

说明：
- 未识别占位符会保留原文，便于排错。
- 建议将这两段视为“可本地化模板文本”，按语言维护不同配置文件。
- `v0.3.67` 起，长模板默认文本以 `Prompt/Default/SystemPrompt_Default.json` 为准，不再在代码构造函数重复维护。
- `v0.3.68` 起，RPG 默认提示词与部分动作描述在代码端由 `PromptTextConstants` 统一提供，避免同文案多处硬编码。
- `v0.3.69` 起，回复合约段落标题文本也由 `PromptTextConstants` 统一提供，减少 `AppendSimpleConfig/AppendAdvancedConfig` 重复文案。
- `v0.3.70` 起，若旧配置中模板字段为空，系统会在加载时从默认模板文件自动回填并保存。
- `v0.3.98` 起，RPG 默认提示词主文案改为以 `Prompt/Default/RpgPrompts_Default.json` 为准，`PromptTextConstants` 与 RPG API 动作说明文本统一从该文件读取（含层级构建 fallback 文案）。
- `v0.3.101` 起，RPG 提示词覆盖值仅持久化到 `Prompt/Custom/RpgPrompts_Custom.json`，不再走 `ModSettings` 配置文件；默认读取链路固定为 `Prompt/Custom`（存在时）→ `Prompt/Default/RpgPrompts_Default.json`。
- `v0.3.102` 起，启动时会一次性清理 `Config/Mod_*.xml` 中遗留的 prompt 文本字段（并写入 marker），防止旧字段继续污染运行配置来源。

### 环境参数开关（EnvironmentContextSwitches）

- `Enabled`
  - 环境参数层总开关。
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

以上项按开关决定是否注入对应环境上下文信息（外交与 RPG 共用同一套环境参数层）。

### 场景条目配置（SceneEntries[]）

每条场景包含：
- `Enabled`：条目开关
- `ApplyToDiplomacy`：是否作用于外交通道
- `ApplyToRPG`：是否作用于 RPG 通道
- `Priority`：优先级（高到低注入）
- `MatchTags[]`：匹配标签（必须 ALL 命中）
- `Content`：场景提示词文本

### RPG 深度 Pawn 参数开关（RpgSceneParamSwitches）

- `IncludeSkills`
- `IncludeEquipment`
- `IncludeGenes`
- `IncludeNeeds`
- `IncludeHediffs`
- `IncludeRecentEvents`
- `IncludeColonyInventorySummary`
- `IncludeHomeAlerts`
- `IncludeRecentJobState`
- `IncludeAttributeLevels`

这些开关控制 RPG 动态注入里是否包含对应深度参数项。

### 事件记忆可知边界（Event Intel Visibility）

- `PublicKnown`：公开地图事件可注入摘要（不暴露不可知细节）。
- `DirectKnown`：派系直接参与事件（如袭击与战报）可注入完整聚合伤亡摘要。
- 过滤依据：`KnownFactionIds` + `IsPublic`。

### 运行规则

- 注入顺序固定：`世界观 -> 环境参数层 -> 近期世界事件与战报 -> 场景层 -> 旧提示词体系`。
- 场景命中策略：全部命中条目都注入，按优先级降序。
- 长度策略：先裁剪单条，再按总量上限裁剪。
- 通道覆盖：外交手动/外交主动/RPG手动/RPG主动全部共用同一场景库。
- 事件账本：独立 `GameComponent` 持久化，默认跟随存档保存/读档恢复。
- 事实约束：系统会追加事实约束块；AI 仅可基于已知信息回复，对无依据说法需明确不确定并提出质疑。

### 双通道场景标签与预览一致性（v0.3.33）

- `DiplomacyManualSceneTagsCsv`
  - 外交通道（手动对话）追加场景标签 CSV（支持 `, ; |` 分隔），在构建最终系统提示词时作为 `additionalSceneTags` 注入。
- `RpgManualSceneTagsCsv`
  - RPG 通道（手动对话）追加场景标签 CSV，构建最终系统提示词时同样作为 `additionalSceneTags` 注入。
- 提示词设置页与 RPG 设置页的预览现在都走最终组装入口（`PromptPersistenceService.BuildFullSystemPrompt/BuildRPGFullSystemPrompt`），不再使用手工拼接预览。
- 预览支持独立设置：
  - `PromptPreviewUseProactiveContext` / `PromptPreviewSceneTagsCsv`
  - `RpgPromptPreviewUseProactiveContext` / `RpgPromptPreviewSceneTagsCsv`

### Prompt 持久化路径（v0.3.89）

- 统一目录结构：`Mods/RimChat/Prompt/{Default,Custom,NPC}`。
- `system_prompt_config.json` 持久化路径：`Mods/RimChat/Prompt/Custom/system_prompt_config.json`（不可写时回退到配置目录）。
- `FactionPrompts.json` 持久化路径：`Mods/RimChat/Prompt/Custom/FactionPrompts.json`（旧 `Config/RimChat/FactionPrompts.json` 自动迁移）。
- NPC 记忆路径：
  - `Prompt/NPC/<saveName>/rpg_npc_dialogues/npc_<pawnId>.json`
    - RPG NPC 档案写盘结构为 `sessions`（会话级）；旧版顶层 `turns` 仅用于兼容读取并增量迁移。
    - 保留策略：仅最近一段 `turnCount>=2` 会话保留全文，其他已结束会话压缩为一句摘要。
    - 压缩策略：严格 LLM 请求；失败时不压缩、保留原文，并在后续触达/读取/存档时按冷却重试。
  - `Prompt/NPC/<saveName>/leader_memories/<factionSafeName>_<factionLoadId>.json`
  - 旧 `save_data/<saveName>/...` 目录会在读取时自动迁移到新路径。
- `build.ps1` 部署阶段会备份并还原目标目录下 `Prompt/Custom` 与 `Prompt/NPC`，防止历史数据被清空。



## 场景模板变量（v0.3.34）
- SceneEntries[].Content 支持 `{{variable}}` 运行时替换。
- 内置变量：`scene_tags`、`environment_params`、`recent_world_events`、`colony_status`、`colony_factions`、`current_faction_profile`、`rpg_target_profile`、`rpg_initiator_profile`。
- 未识别变量会在预览诊断中以 `unknown_vars` 提示。
- 提示词预览新增诊断段：展示场景命中/跳过原因与截断标记。

## Prompt Output Language Settings (v0.3.44)
- PromptLanguageFollowSystem (default true).
- PromptLanguageOverride (default empty).
- UI path: Mod Settings -> API -> Output Language.
- Behavior: injects output-language requirement for diplomacy and RPG prompts; JSON keys/action IDs remain unchanged.

## RimTalk Compatibility Settings (v0.3.47)
- `EnableRimTalkPromptCompat`
  - Default: `true`
  - Master switch for RimTalk compatibility bridge (prompt rendering + summary push).
- `RimTalkSummaryHistoryLimit`
  - Default: `10`
  - Clamp range: `1..30`
  - Controls rolling size of `rimchat_recent_session_summaries`.
- `RimTalkCompatTemplate`
  - Default: built-in minimal Scriban-safe template with latest/recent summary variables.
  - Used by both diplomacy and RPG prompt pipelines.
  - Supports RimTalk Scriban syntax and plugin variables.
  - On render failure, runtime falls back to raw template text (request flow continues).
- 持久化路径（v0.3.106）：
  - 读取：`Prompt/Custom/RpgPrompts_Custom.json`（存在时）-> `Prompt/Default/RpgPrompts_Default.json`
  - 保存：仅写入 `Prompt/Custom/RpgPrompts_Custom.json`
  - 兼容迁移：当 Custom 文件不存在时，旧 ModSettings 中 RimTalk 相关值会一次性迁移到 Custom 文件。

### UI Entry
- Mod Settings -> RPG Dialogue -> RPG Dynamic Injection -> RimTalk Prompt Compatibility.
- This section is explicitly shared by diplomacy and RPG channels.

### Variable Browser & Entry Writer
- RimTalk Variable Browser:
  - Shows RimChat built-in summary variables and RimTalk-registered custom/plugin variables.
  - Supports one-click insertion of selected variable token into `RimTalkCompatTemplate`.
- RimTalk Prompt Entry Creator:
  - Allows writing/updating active RimTalk preset entries from RimChat settings.
  - Editable fields: `EntryName`, `AfterEntryName`, `Role`, `Position`, `InChatDepth`, `Content`.
  - Write result is returned via `RimTalkPromptEntryWriteResult` mapped to UI messages.

### Runtime Summary Keys (RimTalk Global Variables)
- `rimchat_last_session_summary`
- `rimchat_last_diplomacy_summary`
- `rimchat_last_rpg_summary`
- `rimchat_recent_session_summaries`
