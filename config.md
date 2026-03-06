# RimDiplomacy 外部配置说明（v0.3.7）

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

## RPG 独立人格 Prompt

### 配置入口（Mod 设置 -> RPG 对话 -> 殖民地 Pawn 独立人格）

- 支持为每个殖民地人类 Pawn（殖民者/囚犯/奴隶）设置独立人格 Prompt。
- 该 Prompt 仅在该 Pawn 作为 RPG 对话目标时注入系统提示词。
- 清空文本即删除该 Pawn 的独立人格 Prompt。
- 配置按存档持久化（不会污染其他存档）。

