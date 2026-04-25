# RimChat 性能优化报告：DiplomacyPromptSnapshotCache

## 1. 问题背景

### 1.1 用户反馈
用户观察到 RimChat 在游戏运行期间存在持续的性能下降，表现为游戏卡顿（FPS 下降），且该问题从安装开始一直存在。

### 1.2 初步假设
用户怀疑 RimChat 后台在持续计算世界关系，导致性能下降。

---

## 2. 代码检索过程

### 2.1 检索范围
针对以下模块进行了深度代码审查：
- `DiplomacySystem/GameComponent_DiplomacyManager.cs` — 外交管理器主循环
- `Persistence/DiplomacyPromptSnapshotCache.cs` — 外交提示快照缓存
- `DiplomacySystem/SocialCircleService.cs` — 社交圈服务
- `Memory/RpgNpcDialogueArchiveManager.cs` — RPG 对话档案管理
- `AI/AIChatServiceAsync.cs` — AI 异步服务
- `WorldState/FactionIntelLedgerComponent.cs` — 派系情报组件

### 2.2 关键发现

#### 发现 1：GameComponentTick 调度频率
`GameComponent_DiplomacyManager.GameComponentTick()` 每游戏 tick 执行以下任务：

| 频率 | 任务 | 代码位置 |
|------|------|----------|
| 每 tick | `DiplomacyPromptSnapshotCache.Instance.Tick(maxBuildsPerTick: 1)` | L516 |
| 每 60 ticks | `ProcessDeferredSocialNewsSeeds()` + `ProcessDelayedEvents()` | L518-522 |
| 每 2000 ticks | `ProcessAIDecisions()` + `ProcessSocialCircleTick()` | L524-528 |
| 每 1500 ticks | `ProcessPeriodicDiplomacySnapshots()` | L530-534 |
| 每 60000 ticks | `DailyReset()` + `FactionSpecialItemsManager.Tick()` | L536-545 |

#### 发现 2：每 tick 磁盘 IO（高优先级问题）
`DiplomacyPromptSnapshotCache.RefreshGlobalInvalidationSignals()` 每 tick 调用 `ComputePromptFilesStampUtcTicks()`，该方法遍历 10 个提示文件并执行 `File.Exists()` + `File.GetLastWriteTimeUtc()`。

```csharp
// DiplomacyPromptSnapshotCache.cs L370-388
private static long ComputePromptFilesStampUtcTicks()
{
    long maxTicks = 0L;
    foreach (string path in EnumeratePromptFilePaths())  // 10 files
    {
        if (File.Exists(path))  // Disk IO every tick!
        {
            long ticks = File.GetLastWriteTimeUtc(path).Ticks;
            if (ticks > maxTicks) maxTicks = ticks;
        }
    }
    return maxTicks;
}
```

#### 发现 3：快照缓存过于敏感（高优先级问题）
`IsSnapshotFresh()` 执行 6 项全量比对，任一失败即整缓存失效：

```csharp
// DiplomacyPromptSnapshotCache.cs L268-312
private bool IsSnapshotFresh(Faction faction, DiplomacyPromptRuntimeSnapshot snapshot)
{
    // 6 checks, any failure = full rebuild
    if (snapshot.PlayerGoodwill != faction.PlayerGoodwill) return false;
    if (snapshot.PlayerRelationKind != faction.RelationKindWith(Faction.OfPlayer)) return false;
    if (snapshot.MemoryRevision != LeaderMemoryManager.Instance.GetFactionMemoryRevision(faction)) return false;
    if (snapshot.WorldEventRevision != ResolveWorldEventRevision()) return false;
    if (snapshot.PromptFilesStampUtcTicks != ComputePromptFilesStampUtcTicks()) return false;
    if (snapshot.SettingsSignature != ComputeSettingsSignature()) return false;
    return true;
}
```

#### 发现 4：世界事件版本计算开销
`ResolveWorldEventRevision()` 每次查询最近 120 天的世界事件和战斗报告，然后计算哈希：

```csharp
// DiplomacyPromptSnapshotCache.cs L314-348
List<WorldEventRecord> worldEvents = ledger.GetRecentWorldEvents(null, daysWindow: 120, ...);
List<RaidBattleReportRecord> raidReports = ledger.GetRecentRaidBattleReports(null, daysWindow: 120, ...);
// ... hash computation
```

#### 发现 5：RPG 档案高频文件写入
`RpgNpcDialogueArchiveManager.RecordTurn()` 每次对话回合都触发 `SaveArchiveToFile()`，即 JSON 序列化 + 磁盘写入。

---

## 3. 根因分析

### 3.1 问题 1：每 tick 磁盘文件时间戳检查

**根本原因**：`ComputePromptFilesStampUtcTicks()` 没有任何缓存机制，每 tick 直接查询 10 个文件的磁盘状态。

**影响量化**：
- 假设游戏 60 FPS，每秒 60 ticks
- 每秒磁盘查询次数：60 ticks × 10 files = **600 次 File.Exists + 600 次 GetLastWriteTimeUtc**
- 每分钟：36,000 次磁盘调用

**为什么这会导致卡顿**：
- 虽然单次 `File.Exists` 很快（微秒级），但高频调用会：
  1. 占用系统调用开销
  2. 触发磁盘缓存抖动（机械硬盘尤为明显）
  3. 在 Unity 单线程环境中累积帧时间

### 3.2 问题 2：快照缓存过于敏感，频繁重建

**根本原因**：`IsSnapshotFresh()` 采用"一票否决"策略，6 项检查中任何一项变化都导致整缓存失效并重新构建快照。

**敏感度分析**：

| 检查项 | 变化频率 | 影响 |
|--------|----------|------|
| `FactionLoadId` | 极低（派系消失/重建） | 合理失效 |
| `PlayerGoodwill` | 中（外交行动、事件） | 过于敏感 |
| `PlayerRelationKind` | 中（好感度阈值跨越） | 过于敏感 |
| `MemoryRevision` | 高（每次对话都变） | **过于敏感** |
| `WorldEventRevision` | 高（每次 raid/事件） | **过于敏感** |
| `PromptFilesStampUtcTicks` | 极低（手动改提示文件） | 合理失效 |
| `SettingsSignature` | 极低（改设置） | 合理失效 |

**问题**：`MemoryRevision` 和 `WorldEventRevision` 在游戏进行中频繁变化，导致快照几乎无法命中缓存，每次 `TryGetSnapshot()` 都触发重建。

### 3.3 附加问题：世界事件版本计算开销

**根本原因**：`ResolveWorldEventRevision()` 不维护增量版本号，每次都要遍历 120 天窗口的数据做哈希计算。

---

## 4. 修复方案

### 4.1 方案 1：文件时间戳缓存（解决磁盘 IO）

**设计思路**：引入时间戳缓存层，将每 tick 磁盘查询降为每 5 秒一次。

#### 新增文件：`PromptFileStampCache.cs`

```csharp
using System;
using System.IO;
using System.Linq;

namespace RimChat.Persistence
{
    /// <summary>
    /// Responsibility: cache prompt file last-write timestamps to avoid per-tick disk IO.
    /// </summary>
    public sealed class PromptFileStampCache
    {
        private const int CacheValidityTicks = 250; // ~5 seconds at 60fps

        private long cachedStamp = -1;
        private int cachedAtTick = -1;
        private readonly object syncRoot = new object();

        public long GetStamp(int currentTick)
        {
            lock (syncRoot)
            {
                if (cachedAtTick > 0 && currentTick - cachedAtTick < CacheValidityTicks)
                {
                    return cachedStamp;
                }

                cachedStamp = ComputePromptFilesStampUtcTicks();
                cachedAtTick = currentTick;
                return cachedStamp;
            }
        }

        public void Invalidate()
        {
            lock (syncRoot)
            {
                cachedAtTick = -1;
            }
        }

        private static long ComputePromptFilesStampUtcTicks()
        {
            long maxTicks = 0L;
            foreach (string path in EnumeratePromptFilePaths())
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    continue;

                long ticks = File.GetLastWriteTimeUtc(path).Ticks;
                if (ticks > maxTicks)
                    maxTicks = ticks;
            }
            return maxTicks;
        }

        private static System.Collections.Generic.IEnumerable<string> EnumeratePromptFilePaths()
        {
            yield return PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.SystemPromptDefaultFileName);
            yield return PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.DiplomacyPromptDefaultFileName);
            yield return PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.SocialCirclePromptDefaultFileName);
            yield return PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.FactionPromptDefaultFileName);
            yield return PromptDomainFileCatalog.GetDefaultPath(PromptDomainFileCatalog.PromptUnifiedDefaultFileName);
            yield return PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SystemPromptCustomFileName);
            yield return PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.DiplomacyPromptCustomFileName);
            yield return PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.SocialCirclePromptCustomFileName);
            yield return PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.FactionPromptCustomFileName);
            yield return PromptDomainFileCatalog.GetCustomPath(PromptDomainFileCatalog.PromptUnifiedCustomFileName);
        }
    }
}
```

#### 修改：`DiplomacyPromptSnapshotCache.cs`

**变更点 1**：注入缓存实例
```csharp
public sealed class DiplomacyPromptSnapshotCache : IDiplomacyPromptSnapshotCache
{
    private readonly PromptFileStampCache _fileStampCache = new PromptFileStampCache();
    // ... existing fields
}
```

**变更点 2**：替换所有 `ComputePromptFilesStampUtcTicks()` 调用为 `_fileStampCache.GetStamp(currentTick)`

**变更点 3**：提供外部失效接口
```csharp
public void InvalidateFileStampCache()
{
    _fileStampCache.Invalidate();
}
```

**预期效果**：
- 磁盘查询从每 tick 600 次降至每 5 秒 10 次
- 减少 99.7% 的文件系统调用

---

### 4.2 方案 2：分层降级验证（解决缓存敏感）

**设计思路**：将 6 项检查按影响程度分层，非关键变化不立即失效缓存，而是标记为"需刷新"并延迟重建。

#### 分层策略

| 层级 | 检查项 | 变化行为 | 处理方式 |
|------|--------|----------|----------|
| **L1 关键层** | `FactionLoadId` | 派系身份变化 | 立即失效 |
| **L2 数据层** | `PlayerGoodwill`, `PlayerRelationKind`, `MemoryRevision` | 游戏数据变化 | 标记刷新，旧快照仍可用 |
| **L3 配置层** | `WorldEventRevision`, `PromptFilesStamp`, `SettingsSignature` | 环境/配置变化 | 延迟失效，下次 Tick 重建 |

#### 修改：`CacheEntry` 结构

```csharp
private sealed class CacheEntry
{
    public DiplomacyPromptRuntimeSnapshot Snapshot;
    public int NextRetryTick;
    public int BuiltTick;
    public bool NeedsRefresh;      // L2/L3 changed, needs rebuild
    public int LastValidatedTick;  // Last full validation tick
}
```

#### 修改：`IsSnapshotFresh` → `ValidateSnapshot`

```csharp
private bool ValidateSnapshot(Faction faction, CacheEntry entry, int currentTick)
{
    var snapshot = entry.Snapshot;
    if (snapshot == null || faction == null)
        return false;

    // L1: Critical layer - immediate invalidation
    string currentFactionId = faction.GetUniqueLoadID() ?? string.Empty;
    if (!string.Equals(snapshot.FactionLoadId, currentFactionId, StringComparison.Ordinal))
        return false;

    // Throttle full validation to every 150 ticks (~2.5s)
    if (currentTick - entry.LastValidatedTick < 150)
        return true;

    entry.LastValidatedTick = currentTick;

    // L2: Data layer - mark refresh but don't invalidate
    bool l2Changed = snapshot.PlayerGoodwill != faction.PlayerGoodwill
                  || snapshot.PlayerRelationKind != faction.RelationKindWith(Faction.OfPlayer)
                  || snapshot.MemoryRevision != LeaderMemoryManager.Instance.GetFactionMemoryRevision(faction);

    // L3: Config layer - delayed invalidation
    bool l3Changed = snapshot.WorldEventRevision != ResolveWorldEventRevision()
                  || snapshot.PromptFilesStampUtcTicks != _fileStampCache.GetStamp(currentTick)
                  || snapshot.SettingsSignature != ComputeSettingsSignature();

    if (l2Changed || l3Changed)
    {
        entry.NeedsRefresh = true;
        RequestWarmup(faction, l2Changed ? "l2_data_changed" : "l3_config_changed");
    }

    return true; // Old snapshot remains usable
}
```

#### 修改：`Tick()` 优先重建策略

```csharp
public void Tick(int currentTick, int maxBuildsPerTick = 1)
{
    if (currentTick <= 0) return;

    RefreshGlobalInvalidationSignals();

    int budget = Math.Max(1, maxBuildsPerTick);

    // Priority 1: Rebuild entries marked as NeedsRefresh
    var refreshTargets = cacheEntries
        .Where(kvp => kvp.Value.NeedsRefresh && kvp.Value.NextRetryTick <= currentTick)
        .Select(kvp => FindFactionByLoadId(kvp.Key))
        .Where(f => f != null)
        .Take(budget)
        .ToList();

    foreach (var faction in refreshTargets)
    {
        TryBuildSnapshot(faction, currentTick);
        budget--;
    }

    // Priority 2: Normal warmup queue
    for (int i = 0; i < budget; i++)
    {
        if (!TryDequeueNextBuildTarget(currentTick, out Faction faction))
            break;
        TryBuildSnapshot(faction, currentTick);
    }
}
```

**预期效果**：
- 好感度/关系/记忆变化不再导致快照立即失效
- 旧快照继续可用，用户体验无感知
- 后台异步刷新，不阻塞主线程

---

### 4.3 方案 3：世界事件增量版本号（附加优化）

**设计思路**：用单调递增的版本号替代全量数据哈希。

#### 修改：`WorldEventLedgerComponent.cs`

```csharp
public class WorldEventLedgerComponent : GameComponent
{
    private static int _globalEventRevision = 1;
    public static int GlobalEventRevision => _globalEventRevision;

    public void NotifyEventAdded()
    {
        _globalEventRevision++;
    }

    // Call this when adding any world event
    public void RecordWorldEvent(WorldEventRecord record)
    {
        // ... existing logic
        NotifyEventAdded();
    }
}
```

#### 修改：`DiplomacyPromptSnapshotCache.ResolveWorldEventRevision()`

```csharp
private static int ResolveWorldEventRevision()
{
    return WorldEventLedgerComponent.GlobalEventRevision;
}
```

**预期效果**：
- 版本号计算从 O(n) 列表遍历降为 O(1) 直接读取
- 消除 120 天窗口的数据查询开销

---

## 5. 文件变更清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `RimChat/Persistence/PromptFileStampCache.cs` | **新增** | 文件时间戳缓存服务 |
| `RimChat/Persistence/DiplomacyPromptSnapshotCache.cs` | **修改** | 接入分层验证、文件缓存、增量版本号 |
| `RimChat/WorldState/WorldEventLedgerComponent.cs` | **修改** | 添加全局事件版本号及递增逻辑 |
| `RimChat/Persistence/DiplomacyPromptRuntimeSnapshot.cs` | **不修改** | 快照结构不变，保持向后兼容 |

---

## 6. 验证与回归路径

| 测试路径 | 预期行为 | 验证方式 |
|----------|----------|----------|
| 正常游戏运行 5 分钟 | 文件 IO 次数从 ~18,000 次降至 ~60 次 | 日志计数或性能分析器 |
| 打开外交对话框 | 快照仍正确命中，响应时间 < 50ms | 手动测试 + 日志 |
| 与派系对话后好感度变化 | L2 标记刷新，旧快照可用，下次 Tick 重建 | 调试日志观察 `NeedsRefresh` |
| 保存提示词模板文件 | 外部 Invalidate 触发，5 秒内缓存更新 | 修改文件后观察重建 |
| Raid 事件发生后 | 世界事件版本号 +1，L3 标记刷新 | 调试日志 |
| 加载旧存档 | 缓存正确初始化，无异常 | 存档加载测试 |
| 长时间运行（1小时） | 无内存泄漏，缓存大小稳定 | 内存分析器 |

---

## 7. 版本与日志

### 版本号更新
- 格式：`x.y.z`（主.次.修订）
- 本次变更：修订号 +1

### VersionLog.txt / VersionLog_en.txt 条目

```
[优化] 减少外交提示快照缓存的磁盘 IO 频率（引入 PromptFileStampCache，每 5 秒检查一次文件时间戳）
[优化] 快照缓存改为分层降级验证（L1 立即失效 / L2 标记刷新 / L3 延迟失效，避免频繁重建）
[优化] 世界事件版本号改为增量机制，消除 120 天窗口遍历开销
```

---

## 8. 附录：原始代码引用

### A.1 GameComponentTick 调度
```csharp
// GameComponent_DiplomacyManager.cs L513-546
public override void GameComponentTick()
{
    int currentTick = Find.TickManager.TicksGame;
    DiplomacyPromptSnapshotCache.Instance.Tick(currentTick, maxBuildsPerTick: 1);

    if (currentTick % 60 == 0)
    {
        ProcessDeferredSocialNewsSeeds(currentTick);
        ProcessDelayedEvents();
    }

    if (currentTick % 2000 == 0)
    {
        ProcessAIDecisions();
        ProcessSocialCircleTick();
    }

    if (currentTick - lastPeriodicSnapshotTick >= PeriodicSnapshotIntervalTicks)
    {
        ProcessPeriodicDiplomacySnapshots();
        lastPeriodicSnapshotTick = currentTick;
    }

    if (currentTick - lastDailyResetTick >= 60000)
    {
        DailyReset();
        lastDailyResetTick = currentTick;
    }

    if (currentTick % 60000 == 0)
    {
        FactionSpecialItemsManager.Instance.Tick();
    }
}
```

### A.2 原始 ComputePromptFilesStampUtcTicks
```csharp
// DiplomacyPromptSnapshotCache.cs L370-388
private static long ComputePromptFilesStampUtcTicks()
{
    long maxTicks = 0L;
    foreach (string path in EnumeratePromptFilePaths())
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            continue;
        }

        long ticks = File.GetLastWriteTimeUtc(path).Ticks;
        if (ticks > maxTicks)
        {
            maxTicks = ticks;
        }
    }

    return maxTicks;
}
```

### A.3 原始 IsSnapshotFresh
```csharp
// DiplomacyPromptSnapshotCache.cs L268-312
private bool IsSnapshotFresh(Faction faction, DiplomacyPromptRuntimeSnapshot snapshot)
{
    if (snapshot == null || faction == null) return false;

    string currentFactionId = faction.GetUniqueLoadID() ?? string.Empty;
    if (!string.Equals(snapshot.FactionLoadId, currentFactionId, StringComparison.Ordinal))
        return false;

    if (snapshot.PlayerGoodwill != faction.PlayerGoodwill) return false;
    if (snapshot.PlayerRelationKind != faction.RelationKindWith(Faction.OfPlayer)) return false;
    if (snapshot.MemoryRevision != LeaderMemoryManager.Instance.GetFactionMemoryRevision(faction)) return false;
    if (snapshot.WorldEventRevision != ResolveWorldEventRevision()) return false;
    if (snapshot.PromptFilesStampUtcTicks != ComputePromptFilesStampUtcTicks()) return false;
    if (snapshot.SettingsSignature != ComputeSettingsSignature()) return false;

    return true;
}
```

### A.4 原始 ResolveWorldEventRevision
```csharp
// DiplomacyPromptSnapshotCache.cs L314-348
private static int ResolveWorldEventRevision()
{
    WorldEventLedgerComponent ledger = WorldEventLedgerComponent.Instance;
    if (ledger == null) return 0;

    List<WorldEventRecord> worldEvents = ledger.GetRecentWorldEvents(null, daysWindow: 120, includePublic: true, includeDirect: true);
    List<RaidBattleReportRecord> raidReports = ledger.GetRecentRaidBattleReports(null, daysWindow: 120, includeDirect: true);

    int latestWorldEventTick = worldEvents?.Count > 0 ? worldEvents[0]?.OccurredTick ?? 0 : 0;
    int latestRaidTick = raidReports?.Count > 0 ? raidReports[0]?.BattleEndTick ?? 0 : 0;

    unchecked
    {
        int hash = 17;
        hash = hash * 31 + (worldEvents?.Count ?? 0);
        hash = hash * 31 + (raidReports?.Count ?? 0);
        hash = hash * 31 + latestWorldEventTick;
        hash = hash * 31 + latestRaidTick;
        return hash;
    }
}
```

---

## 9. 评审反馈与方案完善

### 9.1 分层验证策略的风险与打磨（评审补充）

**风险识别**：L2 变化只标记刷新、旧快照仍可用的策略存在**数据不一致导致的玩家感知割裂**风险。

**具体场景**：假设派系好感从 +50 变为 -10，L2 标记了刷新，但旧快照（+50 好感）仍在接下来的若干秒内被用于生成外交文本。玩家可能看到"我们曾是朋友"这类与当前敌对状态矛盾的内容。

**完善措施**：

| 措施 | 实现方式 |
|------|----------|
| **宽限期上限** | `CacheEntry` 增加 `NeedsRefreshSinceTick` 字段，若超过 30 秒（1500 ticks）仍未重建，强制失效 |
| **过期使用日志** | 在 `ValidateSnapshot` 中记录 `Log.Warning` 当使用过期快照超过 5 秒时 |
| **好感度剧变快速通道** | `PlayerRelationKind` 变化（如 Ally → Hostile）时，跳过宽限期直接标记为 L1 失效 |

**完善后的 L2 处理逻辑**：

```csharp
private bool ValidateSnapshot(Faction faction, CacheEntry entry, int currentTick)
{
    // ... L1 检查不变 ...

    // L2: 数据层 - 标记刷新但保留旧快照
    bool l2Changed = snapshot.PlayerGoodwill != faction.PlayerGoodwill
                  || snapshot.MemoryRevision != LeaderMemoryManager.Instance.GetFactionMemoryRevision(faction);

    // 关系类型变化属于剧变，直接走 L1 失效
    bool relationKindChanged = snapshot.PlayerRelationKind != faction.RelationKindWith(Faction.OfPlayer);
    if (relationKindChanged)
        return false;

    if (l2Changed)
    {
        entry.NeedsRefresh = true;
        if (entry.NeedsRefreshSinceTick <= 0)
            entry.NeedsRefreshSinceTick = currentTick;

        // 宽限期上限：30 秒强制失效
        if (currentTick - entry.NeedsRefreshSinceTick > 1500)
        {
            Log.Warning($"[RimChat] Snapshot for {faction.Name} expired after 30s grace period, forcing rebuild.");
            return false;
        }
    }

    // ... L3 检查不变 ...
    return true;
}
```

### 9.2 文件时间戳缓存的首次命中优化（评审补充）

**问题**：`PromptFileStampCache` 初始化时 `cachedAtTick = -1`，首次调用必然执行磁盘 IO，可能导致首个外交对话框打开时的微小卡顿。

**完善措施**：提供 `Prime(int currentTick)` 方法，在游戏加载的非关键路径上提前触发。

```csharp
public sealed class PromptFileStampCache
{
    // ... existing fields ...

    public void Prime(int currentTick)
    {
        lock (syncRoot)
        {
            if (cachedAtTick > 0) return; // Already primed
            cachedStamp = ComputePromptFilesStampUtcTicks();
            cachedAtTick = currentTick;
        }
    }
}
```

**调用时机**：在 `DiplomacyPromptSnapshotCache.WarmupOnLoad()` 中：

```csharp
public void WarmupOnLoad()
{
    // ... existing warmup logic ...
    int currentTick = Find.TickManager?.TicksGame ?? 0;
    _fileStampCache.Prime(currentTick);
}
```

### 9.3 世界事件增量版本号的线程安全与持久化（评审补充）

**问题 1：线程安全**
`_globalEventRevision` 通过 `++` 递增，若世界事件产生和版本号读取在不同线程（如异步任务），存在竞态条件。

**完善措施**：使用 `Interlocked.Increment`。

```csharp
using System.Threading;

public class WorldEventLedgerComponent : GameComponent
{
    private static int _globalEventRevision = 1;
    public static int GlobalEventRevision => _globalEventRevision;

    public void NotifyEventAdded()
    {
        Interlocked.Increment(ref _globalEventRevision);
    }
}
```

**问题 2：存档同步**
加载存档后，若 `_globalEventRevision` 只是内存中的纯增量，首次快照判断会因版本号突变而全部失效重建。

**完善措施**：将版本号持久化到存档中。

```csharp
public class WorldEventLedgerComponent : GameComponent
{
    private static int _globalEventRevision = 1;
    public static int GlobalEventRevision => _globalEventRevision;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref _globalEventRevision, "worldEventRevision", 1);
    }

    public void NotifyEventAdded()
    {
        Interlocked.Increment(ref _globalEventRevision);
    }
}
```

### 9.4 实施路径（评审建议）

**第一阶段（低风险、高收益）**：
- 实施文件时间戳缓存（方案 1）
- 实施世界事件增量版本号（方案 3）
- 这两个改动纯属性能优化，不改变业务逻辑，对玩家体验无副作用

**第二阶段（核心改造）**：
- 实施分层降级验证（方案 2）
- 保留 `IsSnapshotFresh` 原始逻辑作为回退选项（通过配置开关 `EnableLayeredCacheValidation`）
- 灰度验证后移除回退

### 9.5 补充测试路径（评审建议）

| 测试路径 | 目的 |
|----------|------|
| **低帧率压力测试** | 将 FPS 降至 15 以下，验证 250 ticks 缓存窗口在低速下的合理性（此时 250 ticks > 5 秒） |
| **RPG 对话连续触发** | 连续多次对话，确认 `MemoryRevision` 频繁变化不会导致重建队列过度堆积 |
| **好感度剧变测试** | 通过 dev mode 快速改变派系关系，验证 L2 宽限期和 L1 快速通道行为 |
| **存档加载一致性** | 保存游戏 → 触发世界事件 → 加载存档，验证版本号恢复正确 |

---

*文档生成时间：2026-04-24*
*最后更新：2026-04-24（纳入评审反馈）*
*关联模块：DiplomacySystem, Persistence, WorldState*
*影响范围：DiplomacyPromptSnapshotCache, WorldEventLedgerComponent*
