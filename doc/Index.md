# RimChat 模块索引（v0.7.23）

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
