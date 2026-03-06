这是一个关于 RimWorld 袭击事件生成机制的详细调查报告。基于对底层代码 ( IncidentWorker , StorytellerComp ) 和 XML 定义 ( StorytellerDef , FactionDef ) 的分析。

### RimWorld 袭击机制调查报告 1. 袭击事件生成机制 (Raid Generation Mechanism)
袭击（Raid）本质上是一种 事件 (Incident) ，其生成流程如下：

1. 触发源 (Trigger) ：
   - 叙事者 (Storyteller) ：通过其组件 ( StorytellerComp ) 周期性检查是否应该触发“威胁 (Threat)”类事件。
   - 任务/脚本 ：通过 Quest 或其他脚本直接触发。
2. 事件选择 (Selection) ：
   - 如果叙事者决定触发 ThreatBig （大威胁）或 ThreatSmall （小威胁），它会从数据库中根据 权重 (Commonality) 选择一个 IncidentDef 。
   - RaidEnemy 是典型的 ThreatBig 事件。
3. 执行 (Execution) ：
   - 实例化对应的 IncidentWorker 类（如 IncidentWorker_RaidEnemy ）。
   - 调用 TryExecute(IncidentParms parms) 方法。
   - 派系选择 ：Worker 会选择一个敌对派系 ( Faction )。
   - 点数计算 ：如果 parms.points 未指定，则根据当前地图财富和难度计算（见下文）。
   - 生成单位 ：调用 PawnGroupMakerUtility.GeneratePawns() 根据点数生成具体的袭击者（Pawn）。
   - 入场方式 ：决定是边缘出现 ( EdgeWalkIn )、空投 ( DropPods ) 还是围攻 ( Siege )。 2. 故事叙述者决策逻辑 (Storyteller Decision Logic)
叙事者（如卡桑德拉、兰迪）通过 StorytellerDef 定义的组件 ( comps ) 来决定何时袭击。

- 核心组件 ( StorytellerComp ) ：
  - StorytellerComp_OnOffCycle (卡桑德拉/菲比常用)：定义“开启”和“关闭”周期。在开启期（如 4.6 天），事件触发概率极高；关闭期则平稳。
  - StorytellerComp_RandomMain (兰迪常用)：纯随机，使用 MTB (Mean Time Between) 算法，例如“平均每 1.0 天触发一次”。
  - StorytellerComp_ThreatCycle ：专门负责生成威胁事件（袭击、虫灾等）。
- 决策因素 ：
  - minDaysPassed ：游戏开始多少天后才允许触发。
  - mtbDays ：平均触发间隔天数。
  - minSpacingDays ：同类事件的最小间隔，防止连续高强度袭击。
  - threatPointsFactor ：不同难度会调整此系数。 3. 袭击点数计算 (Raid Points Calculation)
袭击点数 ( Raid Points ) 决定了袭击的规模和强度。计算公式极其复杂，主要由 StorytellerUtility.DefaultParmsNow 及其调用的方法处理。

核心公式逻辑：
 [ o bj ec tO bj ec t ] FinalPoints = ( BasePoints + RampUp ) × Difficulty × Adaptation × StorytellerFactor
- 基础点数 (Base Points) ：
  - 财富 (Wealth) ：主要来源。包括物品、建筑、生物的总价值。财富越高，点数越高（呈曲线增长）。
  - 殖民者 (Pawns) ：每个殖民者和动物都贡献点数，且战斗能力越强（如仿生体）贡献越多。
- 修正因子 (Factors) ：
  - 适应度 (Adaptation/AdaptDays) ： StorytellerDef 中定义。如果玩家最近损失惨重（死人、倒地），适应度降低，点数减少（给玩家喘息机会）；如果长期无损，点数会增加。
    - 参考 XML ： <adaptDaysLossFromColonistLostByPostPopulation>
  - 时间增长 (RampUp) ： <pointsFactorFromDaysPassed> ，随着天数增加，点数会有额外加成。
  - 难度 (Difficulty) ：游戏难度设置直接通过乘法修正点数（如“无情”难度可能是 100% 或更高，“和平”则极低）。
- 限制 ：
  - 通常有硬性上限（如 10000 点或 20000 点），防止后期崩盘（尽管有些 MOD 会突破此上限）。 4. 可复用于 AI 调用的 API (API for AI)
如果您希望 AI（如您的 RimDiplomacy AI）能直接触发袭击或控制袭击参数，可以使用以下 C# 接口。

场景 A：立即强制触发特定袭击 (Direct Execution) 这是最直接的方法，绕过叙事者的随机判定，直接执行逻辑。

```
using RimWorld;
using Verse;

public static void TriggerRaidByAI(Map targetMap, float 
points, Faction specificFaction = null)
{
    // 1. 构建事件参数
    IncidentParms parms = new IncidentParms
    {
        target = targetMap,
        points = points, // 指定点数，-1 让系统自动计算
        faction = specificFaction, // 指定派系，null 让系统随机选
        敌对派系
        forced = true, // 标记为强制事件，无视某些条件
        raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn // 
        可选：指定入场方式
    };

    // 2. 获取袭击事件定义 (RaidEnemy)
    IncidentDef raidDef = IncidentDefOf.RaidEnemy;

    // 3. 执行事件 Worker
    if (raidDef.Worker.TryExecute(parms))
    {
        Log.Message($"AI 成功触发袭击！点数: {parms.points}");
    }
    else
    {
        Log.Warning("AI 触发袭击失败（可能是条件不满足，如无敌对派
        系）。");
    }
}
```
场景 B：计算合适的点数 (Get Standard Points) 如果 AI 想知道“当前通过正常逻辑应该生成多少点数的袭击”，可以使用工具类：

```
float standardPoints = StorytellerUtility.
DefaultThreatPointsNow(targetMap);
```
场景 C：让叙事者排队 (Queue Incident) 如果 AI 只是想“建议”叙事者在未来某时触发，而不是立即执行：

```
Find.Storyteller.incidentQueue.Add(
    IncidentDefOf.RaidEnemy, 
    Find.TickManager.TicksGame + 60000, // 1天后触发
    new IncidentParms { target = targetMap }
);
```
### 总结
- 袭击生成 ：由 IncidentWorker_RaidEnemy 执行。
- 决策 ：由 StorytellerComp 控制频率。
- 点数 ：基于财富曲线 + 适应度修正。
- AI 接口 ：推荐直接实例化 IncidentParms 并调用 IncidentDefOf.RaidEnemy.Worker.TryExecute(parms) 。这完全符合原生逻辑，且兼容性最好。