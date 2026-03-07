# RimChat 外部配置说明（v0.3.29）

## API 页最近对话 Token 用量（v0.3.29）

### 配置入口（Mod 设置 -> API 配置）

- 该项显示在 API 页面底部：`最近一次对话Token使用量：xxxx（低/中/高）`。
- 统计范围：仅统计外交对话与 RPG 对话窗口发起的请求。
- 数据来源：
  - 优先读取响应 `usage.prompt_tokens / usage.completion_tokens / usage.total_tokens`。
  - 缺失 usage 时按“请求+响应文本（4字符≈1token）”回退估算，并显示“估算”标记。
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

## 五维图标浮层（外交窗口）

### 行为说明（当前版本无可配置开关）

- 五维关系区默认最小化为单图标入口。
- 点击图标后显示紧凑浮层列表；点击浮层外部自动收起。
- 浮层为覆盖绘制，不改变底部策略按钮与输入框的布局坐标。

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
