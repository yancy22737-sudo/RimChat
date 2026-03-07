# 任务系统身份一致性修复文档

## 问题描述
在 `RimChat` 的 AI 任务生成过程中，玩家经常发现无论与哪个派系对话，生成的任务（尤其是原版 `Royalty` 任务，如 `ThreatReward_Raid_MiscReward`）最终都变成了“帝国任务”。

### 核心表现：
1. **身份被篡改**：任务描述显示由“帝国总督”下达，而非对话派系的领袖。
2. **奖励错位**：任务完成后奖励“帝国声望”（Royal Favor），而非对话派系的好感度。
3. **变量覆盖**：系统日志显示 `asker` 和 `faction` 变量在生成过程中被原版脚本强制覆盖。

## 修复方案：核心变量锁定机制

为了解决原版任务脚本（特别是具有强帝国偏向性的脚本）对身份的篡改，我们实施了多层次的保护机制。

### 1. Slate 变量锁定 (QuestGenPatch.cs)
这是最高优先级的修复。我们通过 Harmony 拦截了 `RimWorld.QuestGen.Slate.Set` 方法。
- **锁定逻辑**：当 `LockSlateVariables` 标志为 `true` 时，禁止任何试图修改核心身份变量（`asker`, `faction`, `askerFaction`, `giverFaction`, `enemyFaction`）的操作。
- **范围控制**：锁定仅在 `GameAIInterface.CreateQuest` 的生成周期内生效，不影响游戏其他部分的正常运行。

### 2. 强制奖励重定向 (QuestGenPatch.cs)
针对 `QuestNode_GiveRewards` 节点进行了专门补丁。
- **多字段注入**：利用反射遍历 `giverFaction`、`faction`、`askerFaction` 等多个可能的字段。
- **强制引用**：将这些字段的 `sli`（Slate 变量名）强制设置为 `$faction`。
- **结果**：这确保了任务奖励始终发放给发起任务的派系，彻底切断了与帝国的意外关联，即使原版脚本使用了非标准的字段名或默认值。

### 3. 增强的上下文解析 (GameAIInterface.cs)
- **多别名预设**：在任务生成前，同时注入 `faction`、`askerFaction` 和 `giverFaction` 三个变量，以兼容各种编写风格的原版脚本。
- **强制对象传递**：在 `AIActionExecutor.cs` 中，直接传递 `Faction` 对象而非依赖名称字符串解析，消除了由于翻译或特殊字符导致的解析失效。

### 4. 健壮的发起者回退 (GameAIInterface.cs)
- 如果当前派系没有明确的领袖，系统会自动在该派系活跃成员中随机挑选一名作为 `asker`，防止原版脚本因找不到人而触发“全图寻找皇室成员”的回退逻辑。

## 相关文件参考
- [QuestGenPatch.cs](file:///c:/Users/Administrator/source/repos/RimChat/RimChat/Patches/QuestGenPatch.cs)：变量锁定与节点补丁实现。
- [GameAIInterface.cs](file:///c:/Users/Administrator/source/repos/RimChat/RimChat/DiplomacySystem/GameAIInterface.cs)：任务生成流程与上下文注入。
- [AIActionExecutor.cs](file:///c:/Users/Administrator/source/repos/RimChat/RimChat/AI/AIActionExecutor.cs)：AI 指令到游戏逻辑的转换保护。
