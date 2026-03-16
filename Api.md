# RimChat AI API 文档

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
  - 输出：`PromptVariableTooltipInfo`（`name/scope/description/example` 静态信息）。
  - 用途：统一工作台变量侧栏与编辑器胶囊悬浮信息内容结构。
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
- `request_caravan`：成功后固定基础消耗 `-15` 好感度。
- `request_aid`：成功后固定基础消耗 `-25` 好感度；`Military` / `Medical` / `Resources` 统一按 `-25` 处理。
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

