# RimChat 模块索引（v0.7.41）

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
- 动作源单一化：
  - `RimChat/Persistence/PromptPersistenceService.DomainStorage.cs`
  - `ApiActions` 仅来自外交域，不再合并社交 `PublishPublicPostAction`。
- 默认回退路径净化：
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `CreateDefaultConfig()` 改为 default-only 读取（不读 custom）。
- 启动自愈与迁移追踪：
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - 检测坏 custom 时先备份 `Prompt/Custom/_backup/<timestamp>`，再用默认配置重建并写回；日志记录回退来源与修复摘要。
- 新增域版本锚点：
  - `RimChat/Persistence/PromptDomainPayloads.cs`
  - `SystemPromptDomainConfig.PromptDomainSchemaVersion`（单锚点，当前 `1`）。
- 运行期 fail-fast：
  - `RimChat/Persistence/PromptPersistenceService.cs`
  - `RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs`
  - 关键 runtime 节点为空或 `ResponseFormat.JsonTemplate` 为空时抛 `PromptRenderException`，阻断请求，禁止静默降级。
