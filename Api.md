# RimDiplomacy AI API 文档

## 概述

`GameAIInterface` 是 RimDiplomacy 模组中用于 AI 与游戏交互的核心接口类。它提供了一系列 API 方法，允许 AI 根据对话内容动态调整游戏状态，实现智能外交交互。

## 核心特性

- **安全限制**: 好感度调整有单次上限和每日累计上限
- **频率控制**: 每个 API 方法都有独立的冷却时间
- **详细日志**: 完整的 API 调用记录和错误追踪
- **可配置**: 所有限制阈值都可在 Mod 选项中调整
- **主动对话**: NPC 可在在线状态下主动发起对话（右侧信件/直接入会话）

---

## 环境提示词接口（v0.3.23）

环境层统一由 `PromptPersistenceService` 组装并前置注入到外交/RPG系统提示词中。

### 核心入口

- `BuildFullSystemPrompt(Faction faction, SystemPromptConfig config, bool isProactive, IEnumerable<string> additionalSceneTags)`
- `BuildRPGFullSystemPrompt(Pawn initiator, Pawn target, bool isProactive, IEnumerable<string> additionalSceneTags)`
- `BuildEnvironmentPromptBlocks(SystemPromptConfig config, DialogueScenarioContext context)`（内部组装入口）
- `AppendRecentWorldEventIntel(StringBuilder sb, EnvironmentPromptConfig env, DialogueScenarioContext context)`（内部注入块）

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
- 冷却时间：默认 2 天

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
- 冷却时间：默认 1.5 天

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
- 延迟时间：
  - EdgeWalkIn/Siege: 6~8 小时
  - DropPods: 1~2 小时

---

#### CreateQuest
使用原版任务模板创建并向玩家发布一个任务。

**参数:**
| 参数名 | 类型 | 说明 |
|--------|------|------|
| questDefName | string | **必需**。原版任务模板的 DefName。必须从推荐清单中选择。 |
| askerFaction | string | 可选。任务发起派系的名字。默认为当前派系。 |
| points | int | 可选。任务的威胁点数。若不提供，系统将根据玩家当前实力自动计算。 |

---

### 4. 社交圈公开公告（v0.3.14）

#### publish_public_post（AI 动作协议）
用于将当前外交内容转为“全派系可见”的公开公告，进入社交圈 feed。

**参数（建议通过 `parameters` 对象提供）：**
| 参数名 | 类型 | 说明 |
|--------|------|------|
| category | string | 公告类别：`Military/Economic/Diplomatic/Anomaly` |
| sentiment | int | 情绪方向，范围 `-2..2` |
| summary | string | 公告正文（可选，未提供则用规则模板） |
| targetFaction | string | 被提及派系名或 defName（可选） |
| intentHint | string | 行动意图提示（可选） |

#### GameComponent_DiplomacyManager.EnqueuePublicPost
将公告写入社交圈并应用关联影响链：
- 软影响：发帖/被提及派系对玩家好感同步变化（单次钳制 `[-4,4]`）。
- 扩展影响：按帖子类型尝试触发 `新增定居点 / 丢失定居点 / 寒潮 / 枯萎病` 之一（受世界状态约束）。
- 帖子正文会注入发帖派系领袖信息，影响描述与执行结果保持关联。

#### GameComponent_DiplomacyManager.ForceGeneratePublicPost
调试入口，立即按规则强制生成一条公告，并重排下一次自动生成时间。

#### GameComponent_DiplomacyManager.GetSocialPosts / GetUnreadSocialPostCount / MarkSocialPostsRead
提供社交圈 UI 所需的 feed 与未读状态接口。

#### GameComponent_DiplomacyManager.TryLikeSocialPost
外交对话窗口社交圈页签中的点赞接口：
- 记录玩家点赞状态（单帖一次）。
- 按派系定居点规模影响默认点赞基数。
- 低概率给予玩家 `+1~2` 对该发帖派系好感度奖励。

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
| RequestAid | 2 天 | 1-7 天 |
| DeclareWar | 1 天 | 1-7 天 |
| MakePeace | 1 天 | 1-7 天 |
| RequestTradeCaravan | 1.5 天 | 0.5-5 天 |

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

RimDiplomacy 支持 LLM（大语言模型）通过 JSON 格式响应来调用游戏 API。这使得 AI 能够根据对话内容动态调整游戏状态，实现智能外交交互。

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
- Max gift silver: {当前设置值}
- Max gift goodwill gain: {当前设置值}
- Min goodwill for aid: {当前设置值}
- Max goodwill for war declaration: {当前设置值}
- Max peace cost: {当前设置值}
- Peace goodwill reset: {当前设置值}

ENABLED FEATURES:
- Goodwill adjustment: {YES/NO}
- Gift sending: {YES/NO}
- War declaration: {YES/NO}
- Peace making: {YES/NO}
- Trade caravan: {YES/NO}
- Aid request: {YES/NO}

ACTIONS:
1. adjust_goodwill - Change faction relations
   Parameters: amount (int, -{当前单次上限} to {当前单次上限}), reason (string)
   Daily limit remaining: {当前每日上限} total per day
2. send_gift - Send silver to improve relations
   Parameters: silver (int, max {当前最大白银}), goodwill_gain (int, 1-{当前最大收益})
3. request_aid - Request military/medical aid (requires ally)
   Parameters: type (string: Military/Medical/Resources)
   Requirement: goodwill >= {当前最低要求}
4. declare_war - Declare war
   Parameters: reason (string)
   Requirement: goodwill <= {当前宣战阈值}
5. make_peace - Offer peace treaty (requires war)
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
Respond with your in-character dialogue first, then optionally include a JSON block:

```json
{
  "action": "action_name",
  "parameters": {
    "param1": value,
    "param2": value
  },
  "response": "Your in-character response here"
}
```

IMPORTANT RULES:
1. NEVER exceed the max values shown above
2. ONLY use enabled features
3. ALWAYS check requirements before using an action
4. If a feature is disabled, you cannot use it - explain this to the player

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
- 至少 2 条建议应明确基于玩家属性/上下文（社交、特质、殖民地财富、近期交互语气）。

#### 有效动作类型

| 动作 | 说明 | 必需参数 | 可选参数 |
|------|------|----------|----------|
| adjust_goodwill | 调整好感度 | amount (int) | reason (string) |
| send_gift | 发送礼物 | - | silver (int), goodwill_gain (int) |
| request_aid | 请求援助 | - | type (string) |
| declare_war | 宣战 | - | reason (string) |
| make_peace | 议和 | - | cost (int) |
| request_caravan | 请求商队 | - | goods (string) |
| request_raid | 攻击玩家殖民地（袭击） | strategy (string) | arrival (string) |
| create_quest | 创建自定义任务 | title (string), description (string) | rewardDescription (string), callbackId (string), durationDays (int) |
| reject_request | 拒绝请求 | - | reason (string) |
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
```json
{
  "action": "reject_request",
  "parameters": {
    "reason": "Our relations are too strained for trade at this time."
  },
  "response": "I cannot agree to this. Your colony has caused us much trouble. Improve our relations first, then we may talk of trade."
}
```

#### 示例 2：玩家请求援助

**玩家**："We are under attack! Can you send military aid?"

**LLM 响应**（盟友，高好感度）：
```json
{
  "action": "request_aid",
  "parameters": {
    "type": "Military"
  },
  "response": "As allies, we shall not abandon you in your time of need. Reinforcements are being dispatched immediately!"
}
```

**LLM 响应**（中立派系）：
```json
{
  "action": "reject_request",
  "parameters": {
    "reason": "We are not yet close enough allies for such assistance."
  },
  "response": "I sympathize with your plight, but we are not yet bound by alliance. Strengthen our ties, and perhaps we can discuss mutual defense."
}
```

#### 示例 3：调整好感度

**玩家**："Thank you for your generous gift. We appreciate our friendship."

**LLM 响应**：
```json
{
  "action": "adjust_goodwill",
  "parameters": {
    "amount": 8,
    "reason": "Player expressed gratitude for gift"
  },
  "response": "Your words warm my heart. It pleases me to see our friendship grows stronger with each passing day."
}
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
  - 兼容旧入口：`BuildFullSystemPrompt(Faction faction, SystemPromptConfig config)`
  - 新入口：`BuildFullSystemPrompt(Faction faction, SystemPromptConfig config, bool isProactive, IEnumerable<string> additionalSceneTags)`
- RPG 通道：
  - 兼容旧入口：`BuildRPGFullSystemPrompt(Pawn initiator, Pawn target)`
  - 新入口：`BuildRPGFullSystemPrompt(Pawn initiator, Pawn target, bool isProactive, IEnumerable<string> additionalSceneTags)`

### 环境层注入规则
- 注入顺序：`Worldview -> Environment Parameters -> Recent World Events & Battle Intel -> Scene Layers -> Existing Prompt Stack`。
- 匹配规则：`SceneEntries.MatchTags` 全量命中（ALL）才注入。
- 命中策略：全部命中条目按 `Priority` 降序注入。
- 长度控制：先按 `MaxSceneChars` 裁剪单条，再按 `MaxTotalChars` 裁剪总量。
- 事件记忆控制：`MaxInjectedItems` 与 `MaxInjectedChars` 双限流，按派系可知边界过滤。
- 事实约束块：统一追加 `FACT GROUNDING RULES`，要求仅基于已知信息回复；无依据说法需明确不确定并质疑。









