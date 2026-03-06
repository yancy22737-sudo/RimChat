# NPC 行为触发级 API 文档 (RimWorld 1.6)

本文档列出了 RimWorld 1.6 中用于控制和触发 NPC（Pawn）行为的核心 API。这些 API 允许开发者直接修改 NPC 的心理状态、社交关系、囚犯状态以及下达即时战斗指令。

---

## 1. 心情与心理状态 (Mood & Thoughts)

### TryGainMemory
向 Pawn 添加一个记忆（Thought_Memory）。这是提升或降低心情的最常用方式。

- **类名**: `RimWorld.ThoughtHandler` (通过 `pawn.needs.mood.thoughts` 访问)
- **方法签名**: `void TryGainMemory(ThoughtDef def, Pawn otherPawn = null, Precept precept = null)`
- **功能说明**: 为 Pawn 添加一个基于特定 Def 的心情记忆。

**代码示例:**
```csharp
// 为 Pawn 添加一个“感到愉快”的心情
ThoughtDef moodDef = ThoughtDefOf.JoyFilled; // 或自定义 Def
pawn.needs.mood.thoughts.memories.TryGainMemory(moodDef);

// 添加与另一个 Pawn 相关的记忆
pawn.needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.PawnWithGoodOpinionDied, otherPawn);
```

---

## 2. 社交关系与好感度 (Social Relations)

### TryAffectSocialGoodwill
直接调整两个 Pawn 之间的个人好感度（Social Goodwill）。

- **类名**: `RimWorld.Pawn_RelationsTracker` (通过 `pawn.relations` 访问)
- **方法签名**: `bool TryAffectSocialGoodwill(Pawn otherPawn, int amount, bool canMessage = true, string reason = null)`
- **功能说明**: 增加或减少 Pawn 对目标 Pawn 的个人看法值。

**代码示例:**
```csharp
// 提升 selPawn 对 targetPawn 的个人好感度 10 点
selPawn.relations.TryAffectSocialGoodwill(targetPawn, 10, true, "Diplomatic interaction");
```

### TryInteractWith
触发两个 Pawn 之间的社交互动。

- **类名**: `RimWorld.Pawn_InteractionsTracker` (通过 `pawn.interactions` 访问)
- **方法签名**: `bool TryInteractWith(Pawn recipient, InteractionDef interaction)`
- **功能说明**: 强制触发一次社交互动（如聊天、侮辱、招募尝试）。

**代码示例:**
```csharp
// 强制 selPawn 与 targetPawn 进行一次“亲切交谈”
selPawn.interactions.TryInteractWith(targetPawn, InteractionDefOf.Chitchat);
```

---

## 3. 囚犯管理 (Prisoner Interactions)

### Resistance & Will (抵抗度与意志)
直接修改囚犯的数值。

- **类名**: `RimWorld.Pawn_GuestTracker` (通过 `pawn.guest` 访问)
- **属性**: `float resistance`, `float will`
- **功能说明**: 直接读写囚犯的抵抗度（用于招募）和意志（用于奴役/转换）。

**代码示例:**
```csharp
if (pawn.IsPrisoner)
{
    // 降低抵抗度
    pawn.guest.resistance = Math.Max(0, pawn.guest.resistance - 5f);
    
    // 降低意志
    pawn.guest.will = Math.Max(0, pawn.guest.will - 3f);
}
```

---

## 4. 招募与加入殖民地 (Joining Colony)

### RecruitUtility.Recruit
强制让一个 Pawn 加入指定派系。

- **类名**: `RimWorld.RecruitUtility`
- **方法签名**: `static void Recruit(Pawn pawn, Faction faction, Pawn recruiter = null)`
- **功能说明**: 最直接的招募 API，会处理所有加入逻辑（如状态清除、归属变更）。

**代码示例:**
```csharp
// 让目标 Pawn 立即加入玩家派系
RecruitUtility.Recruit(targetPawn, Faction.OfPlayer);
```

---

## 5. 战斗与即时指令 (Combat & Jobs)

### TryTakeOrderedJob
给 Pawn 下达一个即时指令（Job）。

- **类名**: `Verse.AI.Pawn_JobTracker` (通过 `pawn.jobs` 访问)
- **方法签名**: `bool TryTakeOrderedJob(Job job, JobTag? tag = null)`
- **功能说明**: 强制 Pawn 停止当前活动并开始执行新任务。

**代码示例 (攻击指令):**
```csharp
// 让 selPawn 攻击 targetPawn
Job attackJob = JobMaker.MakeJob(JobDefOf.AttackMelee, targetPawn);
selPawn.jobs.TryTakeOrderedJob(attackJob, JobTag.Misc);
```

**代码示例 (强制移动):**
```csharp
// 让 Pawn 移动到指定坐标
Job gotoJob = JobMaker.MakeJob(JobDefOf.Goto, new IntVec3(10, 0, 10));
pawn.jobs.TryTakeOrderedJob(gotoJob, JobTag.Misc);
```

---

## 总结表

| 功能 | API 路径 | 核心方法/字段 |
| :--- | :--- | :--- |
| **提升心情** | `pawn.needs.mood.thoughts.memories` | `TryGainMemory(ThoughtDef)` |
| **提升好感度** | `pawn.relations` | `TryAffectSocialGoodwill(other, amount)` |
| **降低抵抗度** | `pawn.guest` | `resistance -= amount` |
| **加入殖民地** | `RecruitUtility` | `Recruit(pawn, faction)` |
| **下达攻击指令** | `pawn.jobs` | `TryTakeOrderedJob(JobMaker.MakeJob(...))` |
