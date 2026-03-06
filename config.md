# RimDiplomacy 外部配置说明（v0.3.9）

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
- `RimDiplomacy_NpcPush_DebugForceTrigger`（UI 调试按钮）
  - 立即强制触发一条随机主动对话（用于联调；仍依赖 AI 配置可用）。

### 运行策略（非配置项）

- 常规触发评估间隔：`6000` ticks。
- 队列处理间隔：`600` ticks。
- 同派系主动发言冷却：成功后随机 `1~3` 天。
- 在线门控：仅 `Online` 可直接投递，离线/勿扰入队等待。
- LLM 失败策略：每条触发仅重试 `1` 次，失败后丢弃并写日志。`r`n`r`n## 在线状态系统

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

## RPG 独立人格 Prompt

### 配置入口（Mod 设置 -> RPG 对话 -> 殖民地 Pawn 独立人格）

- 支持为每个殖民地人类 Pawn（殖民者/囚犯/奴隶）设置独立人格 Prompt。
- 该 Prompt 仅在该 Pawn 作为 RPG 对话目标时注入系统提示词。
- 清空文本即删除该 Pawn 的独立人格 Prompt。
- 配置按存档持久化（不会污染其他存档）。



