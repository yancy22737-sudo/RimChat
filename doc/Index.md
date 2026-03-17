# RimChat 模块索引（v0.7.18）

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
