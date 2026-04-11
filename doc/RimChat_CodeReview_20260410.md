# RimChat 项目全面审查报告

> 审查日期：2026-04-10
> 项目版本：0.9.93
> 审查范围：架构、逻辑正确性、数据持久化、UI/UX、本地化、Patch 安全性、代码规范

---

## 1. 总结

### 总体评价：**可改进**

RimChat 是一个功能丰富的 RimWorld 1.6 Mod，将 AI 驱动的外交系统引入游戏。项目在功能完整性、守卫层设计、构建系统、存档隔离等方面表现出色，但在代码体量控制、UI/数据分离、本地化一致性、Patch 安全性等方面存在显著改进空间。

### 最大风险

**QuestGenPatch 的深度反射侵入**——直接读写 `SlateRef.sli`、`QuestNode` 内部字段等 RimWorld 私有实现细节，RimWorld 任何任务系统内部重构都会导致运行时崩溃。

---

## 2. 发现列表（按优先级）

### P0 — 阻断级（会崩溃/刷红字/数据损坏/严重性能问题）

| # | 问题 | 位置 | 影响 |
|---|------|------|------|
| P0-1 | **FactionLeaderMemory 内部类型未实现 IExposable**，但使用 `LookMode.Deep` | `FactionLeaderMemory.cs:L321-L325` | 若 ExposeData 被调用将导致存档损坏（当前走外部 JSON 路径暂未触发，属定时炸弹） |
| P0-2 | **Thread.Sleep 在 while 循环中阻塞主线程**等待 UnityWebRequest | `AIChatClient.cs:L159`, `AIChatService.cs:L267`, `RimChatSettings.cs` 多处 | 网络异常时游戏长时间冻结无响应 |
| P0-3 | **QuestGenPatch 反射无异常保护** | `QuestGenPatch.cs` 全文 | 反射查找失败直接抛未处理异常，任务生成流程中断 |
| P0-4 | **EscapeJson 缺少 Unicode 控制字符转义** | `AIChatService.cs`, `AIChatClient.cs`, `AIChatServiceAsync.cs`, `LeaderMemoryJsonCodec.cs:L595`, `RpgNpcDialogueArchiveJsonCodec.cs:L455` | LLM 输出含 `\b`/`\f`/`\0` 等字符时生成无效 JSON，解析失败 |
| P0-5 | **.ToUpper().Translate() Bug** | `MainTabWindow_RimChat.cs:L578` | 英文环境下信息卡片标题显示大写翻译键名而非翻译后文本 |

### P1 — 重要级（逻辑隐患/耦合过高/难维护/易回归）

| # | 问题 | 位置 | 影响 |
|---|------|------|------|
| P1-1 | **三套 AI 服务并存**（AIChatService/AIChatClient/AIChatServiceAsync），大量代码重复 | `AI/` 目录全局 | 维护成本高，修复需同步三处；旧版有线程安全问题 |
| P1-2 | **40+ 文件超过 800 行硬阈值**，最严重：PromptPersistenceService.cs(6350行)、Dialog_DiplomacyDialogue.cs(4051行)、RimChatSettings.cs(3513行) | 多模块 | 修改困难，回归风险高 |
| P1-3 | **ProcessRequestCoroutine 700+ 行**，嵌套和分支超出规范 | `AIChatServiceAsync.cs:L389-L1097` | 难以理解和维护 |
| P1-4 | **AIResponseParser 2159 行**，混合 JSON 解析/动作验证/参数规范化/策略建议解析 | `AIResponseParser.cs` | 职责不清，修改易回归 |
| P1-5 | **RecordTurn 每次对话轮次同步写磁盘** | `RpgNpcDialogueArchiveManager.cs:L152` | 频繁 I/O 导致帧率下降 |
| P1-6 | **LeaderMemoryManager 缺少线程同步** | `LeaderMemoryManager.cs` 全文 | 潜在的缓存竞态条件 |
| P1-7 | **UI 逻辑与业务逻辑严重耦合**——RimChatSettings partial 文件混合字段声明+UI渲染+重置逻辑 | `Config/` 目录 | 无法独立测试，违反"分离 UI 表现与业务逻辑"规则 |
| P1-8 | **默认值三处重复定义**（字段声明/ExposeData/Reset 方法） | `RimChatSettings_AI.cs` 等多处 | 修改默认值需同步三处，极易遗漏 |
| P1-9 | **RpgResponseContractGuard 调用时 ActionsJson 传了两次** | `AIChatServiceAsync.cs:L998-L1001` | 疑似复制粘贴错误，占位符检测源不正确 |
| P1-10 | **Dialog_DiplomacyDialogue.ActionPolicies 硬编码中文对话文本** | `Dialog_DiplomacyDialogue.ActionPolicies.cs:L36-L48,L427,L867-L882` | 英文环境下显示中文，严重影响非中文用户 |
| P1-11 | **SystemPromptConfig/RpgPromptDefaultsConfig 硬编码中文提示词** | `SystemPromptConfig.cs:L462-L527`, `RpgPromptDefaultsConfig.cs:L79-L84` | 英文模式下 AI 用中文思考和回复 |
| P1-12 | **RimChatSettings_ImageApi 硬编码中文 UI 标签** | `RimChatSettings_ImageApi.cs:L65,L92,L96` | 非中文用户看到中文配置说明 |
| P1-13 | **93 处 "Unknown" 硬编码** | 全项目 | UI 一致性问题 |
| P1-14 | **AIChatClient 的 TaskCompletionSource + LongEventHandler 反模式** | `AIChatClient.cs:L106-L127` | 可能死锁；游戏退出时 tcs.Task 永不完成 |
| P1-15 | **Google/其他提供商 Authorization 分支完全相同**（死代码） | `AIChatServiceAsync.cs:L596-L603`, `AIChatService.cs:L238-L247` | 误导性代码 |
| P1-16 | **ChatMessageData 和 ChatMessage 两个重复类** | `AIChatService.cs:L15`, `AIChatClient.cs:L16` | 概念冗余 |

### P2 — 优化级（可读性、命名、重复代码、体验改进）

| # | 问题 | 位置 | 影响 |
|---|------|------|------|
| P2-1 | **中英文语言键数量差异（54 键）** | `Languages/` 目录 | 本地化完整性 |
| P2-2 | **固定窗口尺寸未做分辨率自适应**，特别是 Dialog_ApiDebugObservability(1400x860) | 多个 Dialog 文件 | 低分辨率下窗口超出屏幕 |
| P2-3 | **MainTabWindow_RimChat 硬编码 contentHeight=800** | `MainTabWindow_RimChat.cs:L367` | 窗口缩放适配差 |
| P2-4 | **ParseSceneTagsCsv 和 ParseSceneTagsCsvForRpg 重复** | `RimChatSettings_Prompt.cs:L1858`, `RimChatSettings_RPG.cs:L656` | 应提取为共享工具方法 |
| P2-5 | **乱码注释** | `RimChatSettings_AI.cs` 多处, `RimChatSettings.cs:L20` | 代码可读性差 |
| P2-6 | **DiplomacyResponseContractGuard.BuildFallbackClarification() 硬编码中文** | `DiplomacyResponseContractGuard.cs:L78` | 非中文用户看到中文 |
| P2-7 | **SocialNewsJsonParser 中文词表硬编码** | `SocialNewsJsonParser.cs:L174-L216,L370-L379` | 社交系统非中文适配 |
| P2-8 | **LLMRpgApiResponse 中文动作名硬编码** | `LLMRpgApiResponse.cs:L508-L545` | RPG 动作解析非中文适配 |
| P2-9 | **QuestPart_AICallback 硬编码中文** | `QuestPart_AICallback.cs:L38` | 违反项目规范 |
| P2-10 | **"[AI]"/"[Std]" 硬编码** | `MainTabWindow_RimChat.cs:L626` | 非中文 UI |
| P2-11 | **RimChatSettings partial 内部通过 RimChatMod.Settings 访问自身实例** | `RimChatSettings_Prompt.cs:L1250`, `RimChatSettings_RPG.cs:L614` | 不必要的间接耦合 |
| P2-12 | **Text.Anchor 直接重置模式** | `Dialog_DiplomacyDialogue.cs` 多处 | 异常路径下潜在对齐问题 |
| P2-13 | **Regex 解析 JSON 脆弱**，未复用已有的 JsonParser | `LeaderMemoryJsonCodec.cs`, `RpgNpcDialogueArchiveJsonCodec.cs` | 键名重复或格式异常时解析错误 |
| P2-14 | **SaveAllMemories 的 O(N*M) 派系查找** | `LeaderMemoryManager.cs:L249-L256` | 派系多时保存延迟 |
| P2-15 | **ReflectionJsonFieldSerializer 仅序列化公共字段** | `ReflectionJsonFieldSerializer.cs:L110-L114` | 使用属性的模型类数据丢失 |
| P2-16 | **PromptPersistenceService 缓存无线程保护** | `PromptPersistenceService.cs:L42-L43` | 理论上的竞态条件 |
| P2-17 | **AIProvider.None.GetEndpointUrl() 返回空字符串而非报错** | `AIProvider.cs` | 可能造成静默失败 |
| P2-18 | **GetUrlHostPort 裸 catch 吞异常** | `AIChatServiceAsync.LocalControl.cs:L383` | 调试困难 |
| P2-19 | **CommsConsolePatch 闭包反射提取 Faction** | `CommsConsolePatch.cs:L214-L249` | 依赖编译器实现细节，C# 版本更新可能导致失效 |
| P2-20 | **AIResponseParser.ParseResponse 异常降级为 Success=true 但 Actions 为空** | `AIResponseParser.cs:L65-L75` | 调用方无法区分"纯文本响应"和"解析出错被降级" |

---

## 3. 超过 800 行硬阈值的文件清单

> 项目规范要求：单文件 < 800 行

| 文件 | 行数 | 模块 |
|------|------|------|
| PromptPersistenceService.cs | 6350 | Persistence |
| Dialog_DiplomacyDialogue.cs | 4051 | UI |
| RimChatSettings.cs | 3513 | Config |
| RpgNpcDialogueArchiveManager.cs | 2582 | Memory |
| AIChatServiceAsync.cs | 2304 | AI |
| PromptPersistenceService.Hierarchical.cs | 2276 | Persistence |
| AIResponseParser.cs | 2160 | AI |
| RimChatSettings_Prompt.cs | 2048 | Config |
| GameAIInterface.cs | 1976 | DiplomacySystem |
| PromptRuntimeVariableBridge.cs | 1591 | Prompting |
| PromptPersistenceService.WorkbenchComposer.cs | 1671 | Persistence |
| PromptPresetService.cs | 1655 | Config |
| DiplomacyEventManager.cs | 1502 | DiplomacySystem |
| Dialog_DiplomacyDialogue.Strategy.cs | 1470 | UI |
| GameComponent_NpcDialoguePushManager.cs | 1372 | NpcDialogue |
| PromptUnifiedCatalog.cs | 1363 | Config |
| RimChatSettings_AI.cs | 1250 | Config |
| RimChatSettings_RimTalkTab.cs | 1243 | Config |
| FactionPromptManager.cs | 1112 | Config |
| RimChatSettings_RimTalkCompat.cs | 1119 | Config |
| Dialog_DiplomacyDialogue.ActionPolicies.cs | 1005 | UI |
| GameAIInterface.ItemAirdrop.Barter.cs | 1020 | DiplomacySystem |
| ApiActionEligibilityService.cs | 1142 | DiplomacySystem |
| PromptPersistenceService.DomainStorage.cs | 1161 | Persistence |
| Dialog_ItemAirdropTradeCard.cs | 1130 | UI |
| GameComponent_DiplomacyManager.cs | 1031 | DiplomacySystem |
| GameComponent_RPGManager.PersonaBootstrap.cs | 1088 | DiplomacySystem |
| SocialNewsSeedFactory.cs | 1057 | DiplomacySystem |
| RimChatSettings_PromptSectionWorkspace.cs | 1051 | Config |
| RimTalkNativeRpgPromptRenderer.cs | 1004 | Prompting |
| Dialog_ApiDebugObservability.cs | 986 | UI |
| Dialog_RPGPawnDialogue.cs | 953 | UI |
| WorldEventLedgerComponent.cs | 954 | WorldState |
| AIActionExecutor.cs | 949 | AI |
| SystemPromptConfig.cs | 934 | Config |
| ApiUsabilityDiagnosticService.cs | 918 | Config |
| GameAIInterface.ItemAirdrop.cs | 913 | DiplomacySystem |
| RimChatSettings_PromptAdvancedFramework.cs | 910 | Config |
| FactionDialogueSession.cs | 969 | Memory |
| DialogueSummaryService.cs | 791 | Memory |
| MainTabWindow_RimChat.cs | 824 | UI |
| Dialog_DiplomacyDialogue.ImageRendering.cs | 841 | UI |
| GameComponent_PawnRpgDialoguePushManager.cs | 815 | PawnRpgPush |

---

## 4. 硬编码中文/英文字符串清单

### 4.1 用户可见的硬编码中文（P1 优先级）

| 文件 | 行号 | 内容 | 建议替换 |
|------|------|------|----------|
| RimChatSettings_ImageApi.cs | L65 | "这里应填写支持图生图的图片 API..." | 翻译键 |
| RimChatSettings_ImageApi.cs | L92 | "ComfyUI 图生图加载节点名" | 翻译键 |
| RimChatSettings_ImageApi.cs | L96 | "填写你本地 ComfyUI..." | 翻译键 |
| Dialog_DiplomacyDialogue.ActionPolicies.cs | L36-L48 | "再发一次"/"确认"/"取消" 等词表 | 翻译键 + 本地化词表 |
| Dialog_DiplomacyDialogue.ActionPolicies.cs | L427 | "好，这次请求先取消。" | 翻译键 |
| Dialog_DiplomacyDialogue.ActionPolicies.cs | L867-L882 | "你这次要我空投什么物资？" 等 | 翻译键 |
| DiplomacyResponseContractGuard.cs | L78 | 降级提示文本 | 翻译键 |
| QuestPart_AICallback.cs | L38 | $"任务 '{quest.name}' 已{state}" | 翻译键 |
| Dialog_DiplomacyDialogue.cs | L2948 | $"无法执行动作 '{actionName}': {reason}" | 翻译键 |
| Dialog_DiplomacyDialogue.cs | L3841-L3844 | "联合袭击"/"都叫来" 等 | 翻译键 + 本地化词表 |

### 4.2 AI 提示词中的硬编码中文（P1 优先级）

| 文件 | 行号 | 内容 | 影响 |
|------|------|------|------|
| SystemPromptConfig.cs | L462-L527 | 频道名、描述、世界观 | 英文模式下 AI 用中文思考 |
| RpgPromptDefaultsConfig.cs | L79-L84 | 决策优先级等默认提示词 | 英文模式下 RPG 行为异常 |
| RimChatSettings_RimTalkCompat.cs | 常量 | "背景：破碎的人类文明..." 等 | 提示词不可本地化 |

### 4.3 解析逻辑中的硬编码中文（P2 优先级）

| 文件 | 行号 | 内容 | 影响 |
|------|------|------|------|
| SocialNewsJsonParser.cs | L174-L216 | "波澜"/"发酵"/"争论" 等词表 | 社交系统非中文适配 |
| SocialNewsJsonParser.cs | L370-L379 | "消息源："/"公开社交圈转述" | 社交新闻非中文适配 |
| LLMRpgApiResponse.cs | L508-L545 | "恋爱"/"结婚"/"分手" 等动作名 | RPG 动作解析非中文适配 |

### 4.4 硬编码英文字符串

| 文件 | 内容 | 出现次数 |
|------|------|----------|
| 全项目 | "Unknown" | 93 处 |
| MainTabWindow_RimChat.cs:L626 | "[AI]"/"[Std]" | 2 处 |

---

## 5. 架构层面问题详解

### 5.1 三套 AI 服务并存

```
AIChatService（旧版同步）──┐
                           ├── 大量重复代码
AIChatClient（旧版异步）───┤     GetFirstValidConfig()
                           │     BuildChatCompletionJson()
AIChatServiceAsync（主力）─┘     EscapeJson()
                                 ParseResponse()
```

**问题**：
- `AIChatService` 和 `AIChatClient` 使用 `Thread.Sleep` 轮询 `UnityWebRequest`，阻塞后台线程
- `AIChatClient` 的 `TaskCompletionSource + LongEventHandler` 模式可能死锁
- 旧版未标记 `[Obsolete]`，没有注释说明状态
- `EscapeJson` 在三处各实现一份，均缺少 Unicode 控制字符转义

**建议**：将 `AIChatService` 和 `AIChatClient` 标记为 `[Obsolete]`，逐步迁移调用方到 `AIChatServiceAsync`，提取共享逻辑到 `AIRequestHelper` 工具类。

### 5.2 Partial Class 滥用

`Dialog_DiplomacyDialogue`（20+ partial 分片）和 `RimChatSettings`（30 partial 分片）虽然物理拆分了文件，但：

- 逻辑耦合并未真正解耦——所有 partial 共享同一个类的私有字段
- 部分分片仍超 800 行（Strategy.cs 1470行、ActionPolicies.cs 1005行）
- 隐式依赖：分片 A 可能依赖分片 B 的字段初始化顺序

**建议**：将 partial class 拆分改为组合模式——提取独立的 Renderer/Controller/State 类，通过依赖注入或构造函数传入主窗口引用。

### 5.3 Settings 层 UI/数据耦合

```
RimChatSettings (partial, 30 files)
  ├── 字段声明 + ExposeData() ── 数据层
  ├── Draw*() 方法 ──────────── UI 层
  ├── Reset*() 方法 ─────────── 业务层
  └── 直接访问 GameComponent ── 跨层耦合
```

**问题**：
- 无法对 Settings 进行单元测试（UI 逻辑依赖 RimWorld 的 IMGUI API）
- 修改 UI 布局需要修改数据类
- `RimChatSettings_NpcPush.cs` 直接访问 `GameComponent_PawnRpgDialoguePushManager.Instance`
- `RimChatSettings_ImageApi.cs` 直接使用 `UnityWebRequest` 进行连接测试

**建议**：提取 `Draw*()` 方法到独立的 `SettingsRenderer` 类，Settings 类只保留字段和 `ExposeData()`。

### 5.4 循环依赖

```
RimChatSettings ←→ RimChatMod.Settings
     ↓
GameComponent_*.Instance
```

在 partial class 内部通过 `RimChatMod.Settings` 访问自身实例，而 `RimChatMod` 又持有 `RimChatSettings` 实例，形成编译期概念上的循环引用。

---

## 6. Patch 安全性评估

### 6.1 风险矩阵

| Patch 文件 | 风险等级 | 主要风险点 |
|-----------|---------|-----------|
| QuestGenPatch.cs | **极高** | 12+ 处反射访问内部字段，无异常保护 |
| CommsConsolePatch.cs | **高** | 闭包反射提取 Faction |
| PawnExitMapPatch_RpgMemory.cs | **中** | 基类 Patch，但有过滤 |
| PawnKillPatch_WorldEventLedger.cs | **中** | 基类 Patch，但有过滤 |
| ThingTakeDamagePatch_FactionIntelLedger.cs | **中** | 极高频方法 + 基类 Patch |
| 其他 10 个 Patch | **低** | 标准 Prefix/Postfix，无反射 |

### 6.2 QuestGenPatch 关键反射点

| 反射目标 | 用途 | 断裂概率 |
|---------|------|---------|
| `SlateRef.sli` 字段 | 修改 Slate 引用值 | 极高——内部私有字段 |
| `QuestNode.giverFaction` 字段 | 强制任务发起派系 | 高——可能重命名 |
| `QuestNode.pawn/node/elseNode` 字段 | 修改 HasRoyalTitle 逻辑 | 高——内部结构 |
| `GiveRewards.parms` 子字段 | 禁止非帝国赏赐 | 中——参数可能变更 |
| `BanditCamp.factionsToDrawLeaderFrom` | 允许任意派系匪营 | 中 |

### 6.3 正面发现

- **无 Transpiler Patch**——这是安全性的一大优势
- **无 `___` 三下划线私有字段访问**
- **HarmonyPatchStartupSelfCheck** 启动自检机制是良好实践
- **所有基类 Patch 都有过滤**（`RimChatTrackedEntityRegistry`）

---

## 7. 性能热点

| 热点 | 位置 | 影响 | 建议 |
|------|------|------|------|
| Thread.Sleep 轮询 UnityWebRequest | AIChatService.cs:L267, AIChatClient.cs:L159 | 主线程冻结 | 改用协程（AIChatServiceAsync 已正确实现） |
| RecordTurn 每轮次同步写磁盘 | RpgNpcDialogueArchiveManager.cs:L152 | 帧率下降 | 改为脏标记 + 延迟批量写入 |
| SaveAllMemories O(N*M) 派系查找 | LeaderMemoryManager.cs:L249-L256 | 保存延迟 | 构建 factionId → Faction 索引字典 |
| Thing.TakeDamage Postfix 高频调用 | ThingTakeDamagePatch_FactionIntelLedger.cs | 每帧数百次 | 确认 IsThingTracked 为 O(1) |
| API 模型列表探测 Thread.Sleep | RimChatSettings.cs:L2118,L2629,L2713 | 设置页卡顿 | 改用协程异步探测 |
| LeaderMemoryManager 全量文件 I/O | LeaderMemoryManager.cs:L494,L571 | 存档加载/保存延迟 | 懒加载 + 增量写入 |

---

## 8. 建议与落地

### 8.1 最小改动修复（对应 P0）

| 问题 | 修复方案 | 涉及文件 |
|------|---------|---------|
| P0-1 FactionLeaderMemory 内部类型未实现 IExposable | 为 `FactionMemoryEntry`/`SignificantEventMemory`/`DialogueRecord`/`RelationSnapshot` 添加 `IExposable` 实现和 `ExposeData()` | `FactionLeaderMemory.cs` |
| P0-2 Thread.Sleep 阻塞主线程 | 将 `AIChatService`/`AIChatClient` 的调用方迁移到 `AIChatServiceAsync`，旧版标记 `[Obsolete]` | `AIChatService.cs`, `AIChatClient.cs` 及调用方 |
| P0-3 QuestGenPatch 反射无异常保护 | 为所有 `AccessTools.Field` + `GetValue`/`SetValue` 调用添加 try-catch，失败时 `Log.Warning` 并跳过 | `QuestGenPatch.cs` |
| P0-4 EscapeJson 不完整 | 统一提取为 `JsonEscapeHelper.EscapeString()` 方法，处理所有控制字符 U+0000-U+001F | 新建 `JsonEscapeHelper.cs`，替换三处旧实现 |
| P0-5 .ToUpper().Translate() Bug | 改为 `.Translate().ToUpper()` | `MainTabWindow_RimChat.cs:L578` |

### 8.2 结构性重构（按模块拆分与替换顺序）

**第一阶段：基础设施**

1. **提取 `RimChatSettingsDefaults` 常量类**——集中管理所有默认值，消除三处重复
2. **提取 `JsonEscapeHelper` 工具类**——统一 JSON 转义逻辑
3. **提取 `AIRequestHelper` 工具类**——统一 `GetFirstValidConfig`/`BuildChatCompletionJson`/`EscapeJson`

**第二阶段：AI 层整合**

4. **标记 `AIChatService`/`AIChatClient` 为 `[Obsolete]`**——逐步迁移调用方到 `AIChatServiceAsync`
5. **拆分 `AIResponseParser`（2159行）**——按职责拆为 `AIResponseJsonParser`/`AIActionValidator`/`AIActionNormalizer`
6. **拆分 `ProcessRequestCoroutine`（700+行）**——提取重试策略为独立的 `AIRetryPolicy` 类

**第三阶段：UI/数据分离**

7. **提取 Settings UI 渲染层**——将 `Draw*()` 方法移到 `SettingsRenderer` 类
8. **拆分 `RimChatSettings.cs`（3513行）主文件**——将 `ExposeData()` 按功能域拆到各 partial 文件
9. **将 `Dialog_DiplomacyDialogue` 的 partial 模式改为组合模式**——提取 `DiplomacyMessageRenderer`/`DiplomacyActionController`/`DiplomacyStateManager`

**第四阶段：本地化修复**

10. **修复所有硬编码中文 UI 文本**——替换为翻译键
11. **修复提示词本地化**——将中文提示词移到配置文件，按语言加载
12. **同步中英文语言键**——编写脚本比对差异，补齐缺失键

**第五阶段：性能优化**

13. **RpgNpcDialogueArchiveManager 延迟写入**——脏标记 + 每帧最多写一次
14. **LeaderMemoryManager 线程安全**——为 `EnsureCacheLoaded`/`GetMemory`/`SaveMemory` 添加锁
15. **LeaderMemoryManager 索引优化**——构建 factionId → Faction 字典

### 8.3 回归清单

完成修改后，需验证以下用户路径与边界条件：

1. **外交对话完整流程**：打开通讯台 → 选择派系 → 发送消息 → 收到 AI 回复 → 触发动作（好感度/空投/赎金）
2. **RPG 对话完整流程**：点击 Pawn → 发起 RPG 对话 → 收到回复 → 触发 RPG 动作
3. **存档兼容性**：加载旧版存档 → 确认记忆/归档正确迁移 → 保存 → 重新加载确认数据完整
4. **多语言切换**：英文环境下所有 UI 文本正确显示 → 中文环境同样正确
5. **网络异常处理**：API 超时/401/500 → 游戏不冻结 → 错误提示正确显示
6. **任务生成**：AI 发起任务 → QuestGenPatch 正确注入派系 → 任务完成回调
7. **低分辨率适配**：1280x720 下所有窗口可正常显示和操作
8. **NPC 主动推送**：NPC 发起对话 → ChoiceLetter 显示 → 玩家响应 → 对话正常进行

---

## 9. 架构亮点（值得保持的设计）

| 设计 | 位置 | 说明 |
|------|------|------|
| 多层输出守卫管线 | `ImmersionOutputGuard` → `TextIntegrityGuard` → `DiplomacyResponseContractGuard` → `RpgResponseContractGuard` | 每层有限重试和降级策略，设计精良 |
| Scriban 模板引擎 | `Prompting/` 模块 | 提示词可维护性好，支持变量注入和运行时渲染 |
| 存档隔离机制 | `SaveScopeKeyResolver` + 声明式迁移 | FNV-1a 哈希 + 多存档数据隔离，兼容旧存档 |
| PromptPresetService | `Config/PromptPresets/` | 原子写入 + 损坏隔离 + 自动恢复 + Schema 版本控制 |
| 构建系统 | `build.ps1` | 编码守卫 + 备份恢复 + NPC 提示词规范化 |
| DialogueWindowCoordinator | `Dialogue/` | 统一管理对话窗口打开，避免冲突 |
| HarmonyPatchStartupSelfCheck | `Patches/` | 启动时自检 Patch 完整性 |
| RaidFallback 多层回退 | `DiplomacyEventManager.RaidFallback.cs` | 详尽的 PawnGroupMaker 注入和点数升级策略 |
| 对话响应结构化协议 | `DialogueResponseEnvelope` | 完善的 fail-fast 校验链 |
| DLC 兼容性 | `DLCCompatibility.cs` | 专门的 DLC 检测和兼容处理 |

---

## 10. P0 修复记录（2026-04-10 已完成）

| P0 编号 | 修复内容 | 修改文件 |
|---------|---------|---------|
| P0-1 | FactionMemoryEntry/RelationSnapshot/SignificantEventMemory/DialogueRecord 添加 IExposable 实现，属性改为公共字段 | `Memory/FactionLeaderMemory.cs` |
| P0-2 | AIChatService/AIChatClient/AIChatClientResponse 标记 `[Obsolete]` | `AI/AIChatService.cs`, `AI/AIChatClient.cs` |
| P0-3 | QuestGenPatch 所有反射调用添加 try-catch 保护 | `Patches/QuestGenPatch.cs` |
| P0-4 | 新建 `JsonEscapeHelper.EscapeString()`，补全 \b/\f/U+0000-U+001F 转义，替换 6 处旧实现 | `Util/JsonEscapeHelper.cs`(新), `AI/AIChatService.cs`, `AI/AIChatClient.cs`, `AI/AIChatServiceAsync.cs`, `Memory/LeaderMemoryJsonCodec.cs`, `Memory/RpgNpcDialogueArchiveJsonCodec.cs` |
| P0-5 | `label.ToUpper().Translate()` → `label.Translate().RawText.ToUpper()` | `UI/MainTabWindow_RimChat.cs` |

---

## 11. P1 完整修复方案

### P1-1: 三套 AI 服务并存

**现状**：`AIChatService`（同步）、`AIChatClient`（Task+LongEventHandler）、`AIChatServiceAsync`（协程，主力）三套并存，大量重复代码。

**修复方案**：

1. **已标记 `[Obsolete]`**（P0-2 已完成），编译器警告已确认出现
2. **逐步迁移调用方**：
   - `Dialog_DiplomacyDialogue.cs:321` 使用了 `AIChatService` → 迁移到 `AIChatServiceAsync`
   - `GameAIInterface.ItemAirdrop.cs:502` 使用了 `AIChatClientResponse` → 迁移到 `AIChatServiceAsync` 的响应模型
3. **提取共享逻辑到 `AIRequestHelper`**：
   ```
   AIRequestHelper (新建)
   ├── GetFirstValidConfig()    ← 从三处合并
   ├── BuildChatCompletionJson() ← 从三处合并
   └── IsConfigured()           ← 从三处合并
   ```
4. **删除 `ChatMessageData` 和 `ChatMessage` 重复类** → 统一为 `AIChatMessage`
5. **最终删除 `AIChatService.cs` 和 `AIChatClient.cs`**（所有调用方迁移完成后）

**风险**：中等。需逐一确认调用方的线程模型是否兼容协程。

---

### P1-2: 40+ 文件超过 800 行硬阈值

**现状**：最严重的三个文件：PromptPersistenceService.cs(6350行)、Dialog_DiplomacyDialogue.cs(4051行)、RimChatSettings.cs(3513行)。

**修复方案**（按优先级排序）：

| 文件 | 当前行数 | 拆分策略 |
|------|---------|---------|
| PromptPersistenceService.cs | 6350 | 按职责域拆为独立服务类：`PromptDomainStorageService`、`PromptWorkspaceService`、`PromptTemplateService`、`PromptVariableService`，主类仅做门面调度 |
| Dialog_DiplomacyDialogue.cs | 4051 | partial → 组合模式：提取 `DiplomacyMessageRenderer`、`DiplomacyActionController`、`DiplomacyStateManager`、`DiplomacyImageController` |
| RimChatSettings.cs | 3513 | 将 ExposeData 拆到各 partial 文件；提取 `Draw*()` 到 `SettingsRenderer` 类 |
| AIChatServiceAsync.cs | 2304 | 拆分 `ProcessRequestCoroutine` 为多个子协程 + `AIRetryPolicy` 类 |
| AIResponseParser.cs | 2160 | 拆为 `AIResponseJsonParser`、`AIActionValidator`、`AIActionNormalizer` |
| RpgNpcDialogueArchiveManager.cs | 2582 | 拆为 `RpgArchivePersistenceService`、`RpgArchiveSessionService`、`RpgArchiveWarmupService` |

**原则**：partial class 只是物理拆分，逻辑耦合并未解耦。应改为组合模式——提取独立类，通过构造函数/属性注入主窗口引用。

---

### P1-3: ProcessRequestCoroutine 700+ 行

**现状**：`AIChatServiceAsync.cs:L389-L1097`，包含上下文版本检查、配置获取、HTTP请求、5层重试（5xx/连接/解析/沉浸感/合约）、Token记录。

**修复方案**：

```csharp
// 拆分为子协程 + 策略类
private IEnumerator ProcessRequestCoroutine(AsyncRequestContext ctx)
{
    yield return StartCoroutine(ValidateAndPrepareCoroutine(ctx));
    if (ctx.Failed) yield break;

    yield return StartCoroutine(SendRequestCoroutine(ctx));
    if (ctx.Failed) yield break;

    yield return StartCoroutine(ParseAndValidateCoroutine(ctx));
    if (ctx.Failed) yield break;

    FinalizeRequest(ctx);
}

// 重试策略提取为独立类
public class AIRetryPolicy
{
    public int MaxRetries { get; }
    public float BaseDelayMs { get; }
    public bool ShouldRetry(AsyncRequestContext ctx, int attempt);
    public float GetDelay(int attempt);
}

// 守卫重试提取为通用方法
private IEnumerator RunGuardWithRetry<TGuard>(
    AsyncRequestContext ctx, TGuard guard, int maxRetries) where TGuard : IResponseGuard
{
    for (int i = 0; i < maxRetries; i++)
    {
        var result = guard.Validate(ctx.Response);
        if (result.IsValid) yield break;
        yield return StartCoroutine(ReparseWithHintCoroutine(ctx, result.Hint));
    }
}
```

---

### P1-4: AIResponseParser 2159 行

**现状**：混合 JSON 解析、动作验证、参数规范化、策略建议解析。

**修复方案**：

```
AIResponseParser (精简为调度器, <200行)
├── AIResponseJsonParser        ← JSON 提取 + 信封解析
├── AIActionValidator           ← 动作合法性校验
├── AIActionNormalizer          ← 参数规范化（RaidDefName、ThingDef 解析等）
└── AIStrategySuggestionParser  ← 策略建议解析
```

每个子类 < 500 行，职责单一，可独立测试。

---

### P1-5: RecordTurn 每次对话轮次同步写磁盘

**现状**：`RpgNpcDialogueArchiveManager.cs:L152` 每次对话轮次都调用 `SaveArchiveToFile`。

**修复方案**：

```csharp
// 脏标记 + 延迟批量写入
private readonly HashSet<int> _dirtyArchives = new HashSet<int>();
private int _lastFlushTick = 0;
private const int FLUSH_INTERVAL_TICKS = 60; // ~1秒

public void RecordTurn(int pawnLoadId, ...)
{
    // ... 记录逻辑 ...
    _dirtyArchives.Add(pawnLoadId);
}

// 在 GameComponent.Tick() 中调用
public void Tick()
{
    if (_dirtyArchives.Count > 0 && Find.TickManager.TicksGame - _lastFlushTick >= FLUSH_INTERVAL_TICKS)
    {
        FlushDirtyArchives();
    }
}

private void FlushDirtyArchives()
{
    foreach (var pawnLoadId in _dirtyArchives)
    {
        if (_cache.TryGetValue(pawnLoadId, out var archive))
        {
            SaveArchiveToFile(archive);
        }
    }
    _dirtyArchives.Clear();
    _lastFlushTick = Find.TickManager.TicksGame;
}

// 存档保存时强制 flush
public void OnBeforeGameSave()
{
    FlushDirtyArchives();
}
```

---

### P1-6: LeaderMemoryManager 缺少线程同步

**现状**：`EnsureCacheLoaded()`、`GetMemory()`、`SaveMemory()` 无锁保护，异步预热可能并发。

**修复方案**：

```csharp
private readonly object _cacheSyncRoot = new object();

public FactionLeaderMemory GetMemory(string factionId)
{
    lock (_cacheSyncRoot)
    {
        EnsureCacheLoaded();
        // ... 原有逻辑 ...
    }
}

public void SaveMemory(FactionLeaderMemory memory)
{
    lock (_cacheSyncRoot)
    {
        // ... 原有逻辑 ...
    }
}

// 异步预热合并时也加锁
public void MergeWarmupResults(Dictionary<string, FactionLeaderMemory> warmupCache)
{
    lock (_cacheSyncRoot)
    {
        foreach (var kvp in warmupCache)
        {
            if (!_memoryCache.ContainsKey(kvp.Key))
            {
                _memoryCache[kvp.Key] = kvp.Value;
            }
        }
    }
}
```

---

### P1-7: UI 逻辑与业务逻辑严重耦合

**现状**：RimChatSettings partial 文件混合字段声明+UI渲染+重置逻辑。

**修复方案**（分阶段）：

**阶段一**：将 `ExposeData()` 拆到各 partial 文件
```csharp
// RimChatSettings.cs (主文件)
public override void ExposeData()
{
    ExposeData_AI();
    ExposeData_Prompt();
    ExposeData_RPG();
    ExposeData_NpcPush();
    ExposeData_ImageApi();
    ExposeData_SocialCircle();
}

// RimChatSettings_AI.cs
partial void ExposeData_AI()
{
    Scribe_Values.Look(ref MaxGoodwillAdjustmentPerCall, ...);
    // ...
}
```

**阶段二**：提取 UI 渲染层
```csharp
// 新建 SettingsRenderer 类
public class AISettingsRenderer
{
    private readonly RimChatSettings _settings;
    public AISettingsRenderer(RimChatSettings settings) { _settings = settings; }
    public void DrawAISettings(Listing_Standard listing) { ... }
}

// RimChatSettings_AI.cs 调用
private static readonly AISettingsRenderer _aiRenderer = new AISettingsRenderer(this);
_aiRenderer.DrawAISettings(listing);
```

---

### P1-8: 默认值三处重复定义

**现状**：同一默认值在字段声明、ExposeData、Reset 方法中各出现一次。

**修复方案**：

```csharp
// 新建 RimChatSettingsDefaults.cs
public static class RimChatSettingsDefaults
{
    public const int MaxGoodwillAdjustmentPerCall = 15;
    public const int MaxDailyGoodwillAdjustment = 30;
    public const int GoodwillAdjustmentCooldownTicks = 60000;
    // ... 所有默认值集中声明 ...
}

// 字段声明
public int MaxGoodwillAdjustmentPerCall = RimChatSettingsDefaults.MaxGoodwillAdjustmentPerCall;

// ExposeData
Scribe_Values.Look(ref MaxGoodwillAdjustmentPerCall, "MaxGoodwillAdjustmentPerCall",
    RimChatSettingsDefaults.MaxGoodwillAdjustmentPerCall);

// Reset
MaxGoodwillAdjustmentPerCall = RimChatSettingsDefaults.MaxGoodwillAdjustmentPerCall;
```

---

### P1-9: RpgResponseContractGuard 调用时 ActionsJson 传了两次

**现状**：`AIChatServiceAsync.cs:L998-L1001` 中 `ValidateVisibleDialogueParts` 的 `placeholderSource` 参数也传了 `ActionsJson`，疑似复制粘贴错误。

**修复方案**：

需确认 `ValidateVisibleDialogueParts` 的 `placeholderSource` 参数的语义。如果它应该传入的是"用于检测占位符的源文本"，则应传入 `visibleDialogue` 而非 `ActionsJson`：

```csharp
// 修复前
result = guard.ValidateVisibleDialogueParts(
    visibleDialogue, trailingActionsJson, parsedEnvelope.ActionsJson);

// 修复后（需确认语义）
result = guard.ValidateVisibleDialogueParts(
    visibleDialogue, trailingActionsJson, visibleDialogue);
```

**注意**：此修复需要先阅读 `RpgResponseContractGuard.ValidateVisibleDialogueParts` 的实现确认第三个参数的语义后再决定。

---

### P1-10 ~ P1-12: 硬编码中文 UI 文本

**修复方案**（统一策略）：

1. **在 `RimChat_Keys.xml` 中添加翻译键**：
```xml
<RimChat_ImageApi_ImageToImageHint>Fill in an image-to-image API here; selfie sends rendered pawn portrait as input. Text-to-image only APIs may not work for selfies.</RimChat_ImageApi_ImageToImageHint>
<RimChat_ImageApi_ComfyUiNodeName>ComfyUI img2img load node name</RimChat_ImageApi_ComfyUiNodeName>
<RimChat_ImageApi_ComfyUiNodeHint>Enter your local ComfyUI node class_type for receiving base64 input, e.g. LoadImageBase64. Only change the node name, not other workflow structure.</RimChat_ImageApi_ComfyUiNodeHint>
<RimChat_Action_Cancelled>OK, this request is cancelled.</RimChat_Action_Cancelled>
<RimChat_Action_WhatToAirdrop>What supplies do you want me to airdrop?</RimChat_Action_WhatToAirdrop>
```

2. **替换 C# 中的硬编码**：
```csharp
// 修复前
listing.Label("这里应填写支持图生图的图片 API...");
// 修复后
listing.Label("RimChat_ImageApi_ImageToImageHint".Translate());
```

3. **ActionPolicies 中的词表**（确认/取消/重发）需要做本地化词表：
```csharp
// 修复前
string[] confirmWords = { "确认", "是的", "是", "好", "行" };
// 修复后
string[] confirmWords = "RimChat_ConfirmWords".Translate().Split(',');
```

4. **SystemPromptConfig 中的中文提示词** → 移到 `Prompt/Default/` 配置文件中，按 `PromptLanguage` 加载不同版本。

---

### P1-13: 93 处 "Unknown" 硬编码

**修复方案**：

1. 在 `RimChat_Keys.xml` 中添加：`<RimChat_Unknown>Unknown</RimChat_Unknown>`
2. 全局替换 `"Unknown"` → `"RimChat_Unknown".Translate()`
3. 注意区分：仅替换 UI 显示用的 `"Unknown"`，不替换枚举值或内部标识符

---

### P1-14: AIChatClient 的 TaskCompletionSource + LongEventHandler 反模式

**现状**：`AIChatClient.cs:L106-L127` 使用 `TaskCompletionSource` + `LongEventHandler`，可能死锁。

**修复方案**：

此问题随 P1-1 的迁移方案一并解决——将 `AIChatClient` 的调用方迁移到 `AIChatServiceAsync` 后，删除 `AIChatClient`。在迁移完成前，添加超时保护：

```csharp
// 临时保护：添加超时
var tcs = new TaskCompletionSource<AIChatClientResponse>();
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

cts.Token.Register(() => tcs.TrySetResult(new AIChatClientResponse
{
    Success = false,
    Error = "Request timed out after 120 seconds"
}));

LongEventHandler.QueueLongEvent(() =>
{
    try
    {
        AIChatClientResponse result = SendRequestDetailedSync(...);
        tcs.TrySetResult(result);
    }
    catch (Exception ex)
    {
        tcs.TrySetResult(new AIChatClientResponse { Success = false, Error = ex.Message });
    }
}, "RimChat_SendingAIRequest".Translate(), false, null);

return await tcs.Task;
```

---

### P1-15: Google/其他提供商 Authorization 分支完全相同

**修复方案**：

```csharp
// 修复前
if (config.Provider == AIProvider.Google)
{
    request.SetRequestHeader("Authorization", $"Bearer {trimmedApiKey}");
}
else
{
    request.SetRequestHeader("Authorization", $"Bearer {trimmedApiKey}");
}

// 修复后
request.SetRequestHeader("Authorization", $"Bearer {trimmedApiKey}");
```

如果未来 Google 需要不同的认证方式（如 API Key in URL），再添加条件分支。

---

### P1-16: ChatMessageData 和 ChatMessage 两个重复类

**修复方案**：

1. 新建统一消息类 `AIChatMessage`：
```csharp
public class AIChatMessage
{
    public string Role;
    public string Content;
}
```
2. 全局替换 `ChatMessageData` → `AIChatMessage`，`ChatMessage` → `AIChatMessage`
3. 删除 `AIChatService.cs` 中的 `ChatMessageData` 和 `AIChatClient.cs` 中的 `ChatMessage`
4. 此修复随 P1-1 迁移方案一并执行
