# RimChat 模块索引（v0.7.26）

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
