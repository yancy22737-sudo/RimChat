# API Filter Design (RimChat)

## 1. 目标
- 对不符合任务生成条件的派系进行动态 API 注入过滤，避免派系拿到不合适的 `create_quest` prompt。
- 对不合适任务执行进行严格拦截，直接失败，不做重定向回退。
- 冷却中的 `create_quest` 不注入到可用 action，并在 prompt 注入剩余冷却时间。
- 模型强行调用被禁动作时，执行层返回失败消息（会进入对话系统消息链路）并写入日志。

## 2. 双层防护架构
- 注入层（Prompt）：
  - 入口：`PromptPersistenceService.BuildFullSystemPrompt`
  - 机制：按 `ApiActionEligibilityService.GetAllowedActions(faction)` 动态裁剪 `ApiActions`
  - 结果：不可用 action（含冷却中的 `create_quest`）不会出现在 ACTIONS 段落
- 执行层（Runtime）：
  - 入口：`AIActionExecutor.ExecuteAction` 前置校验
  - 机制：`ApiActionEligibilityService.ValidateActionExecution(...)` + `ValidateCreateQuest(...)`
  - 结果：任何越权/不满足条件的动作直接 `ActionResult.Failure`

## 3. 原版任务限制检索依据（关键证据）
以下依据来自原版 Def/XML 实际节点约束：

1. `Mission_BanditCamp`（Royalty `Scripts_Missions.xml`）
- `factionsToDrawLeaderFrom`: `Empire`, `OutlanderCivil`, `OutlanderRough`
- 结论：任务对发起派系有明确限定，不满足时应拦截。

2. `ThreatReward_Raid_MiscReward`（Royalty `Scripts_RewardRaid.xml`）
- 包含 `mustHaveRoyalTitleInCurrentFaction=true` 路径
- 结论：皇家头衔路径约束强，跨派系误用风险高，需严格过滤。

3. `PawnLend`（Royalty `Script_PawnLend.xml`）
- `minTechLevel=Industrial`
- 另有 `mustHaveRoyalTitleInCurrentFaction` 分支，且 `allowPermanentEnemyFaction=false`
- 结论：低科技/永久敌对派系不应发起。

4. `OpportunitySite_PeaceTalks`（Core `Script_PeaceTalks.xml`）
- `allowPermanentEnemy=false`
- `mustHaveGoodwillRewardsEnabled=true`
- 结论：永久敌对派系应拦截。

5. `TradeRequest`（Core `Script_TradeRequest.xml`）
- 依赖附近 settlement（`QuestNode_GetNearbySettlement`）
- 与敌对关系流程不兼容
- 结论：敌对派系或无据点派系应拦截。

6. `Hospitality_Refugee`（Royalty `Script_Hospitality_Refugee.xml`）
- Royalty 任务链
- 结论：需在 Royalty 前提下并按整合策略限制。

7. `AncientComplex_Mission`（Ideology `Script_Missions.xml`）
- Ideology 任务链（`QuestNode_Root_Mission_AncientComplex`）
- 结论：无 Ideology 时拦截。

8. `OpportunitySite_ItemStash`（Core `Script_ItemStash.xml`）
- 站点生成链有参数与站点部件要求
- 结论：不满足上下文可能触发生成错误，应在执行层失败并回报。

## 4. 动态规则引擎（所有可能派系）
实现位置：`RimChat/DiplomacySystem/ApiActionEligibilityService.cs`

### 4.1 Action 级过滤
- 支持动作集：
  - `adjust_goodwill`, `send_gift`, `request_aid`, `declare_war`, `make_peace`, `request_caravan`, `request_raid`, `trigger_incident`, `create_quest`, `reject_request`
- 过滤条件：
  - 模组开关（settings）
  - 关系条件（如援助需盟友、袭击需敌对）
  - 冷却条件（含 `create_quest`）
  - `create_quest` 还需“当前至少有一个合格任务模板”

### 4.2 Quest 模板校验
- 方法：`ValidateCreateQuest(faction, questDefName, parameters)`
- 返回：`Allowed/Denied + Code + Message + RemainingSeconds`
- 严格模式：
  - 不再把规则失败重定向到 `RimChat_AIQuest`
  - 直接返回失败信息，交由对话系统消息显示

## 5. 规则矩阵（当前内置模板）
- `OpportunitySite_ItemStash`
  - 当前策略：按稳定性优先暂时禁用（`quest_template_high_risk_disabled`）
  - 原因：日志中多派系稳定复现 `sitePartsParams=null / Could not resolve site parts`
  - 注入/执行约束：不在可用任务清单注入；模型强行调用时执行层直接拒绝
- `AncientComplex_Mission`
  - 当前策略：按稳定性优先暂时禁用（`quest_template_high_risk_disabled`）
  - 原因：日志中出现 Grammar unresolvable（`colonistCount=-1`）的技术异常痕迹
  - 注入/执行约束：不在可用任务清单注入；模型强行调用时执行层直接拒绝
- `TradeRequest`
  - 需要：非敌对 + 至少一个 settlement
- `OpportunitySite_PeaceTalks`
  - 需要：非 `permanentEnemy` + 有有效领袖上下文
- `AncientComplex_Mission`
  - 需要：Ideology DLC + 有有效领袖上下文
- `Mission_BanditCamp`
  - 需要：Royalty DLC + faction def 在 `{Empire, OutlanderCivil, OutlanderRough}`
- `PawnLend`
  - 需要：Royalty DLC + `techLevel >= Industrial`
- `ThreatReward_Raid_MiscReward`
  - 需要：Royalty DLC + 当前整合策略限制为 Empire
- `Hospitality_Refugee`
  - 需要：Royalty DLC + 当前整合策略限制为 Empire
- `BestowingCeremony`
  - 需要：Royalty DLC + 当前整合策略限制为 Empire

## 6. 错误码与回报链路
- 动作前置拦截：`AIActionExecutor.ExecuteAction` 返回 `ActionResult.Failure`
- 对话显示：`Dialog_DiplomacyDialogue.ExecuteAIActions` 会将失败写入系统消息
- 日志：
  - 规则失败：`Log.Warning`（`CreateQuest blocked ...`）
  - 生成异常：`Log.Error`（`Error creating quest ...`）
- 典型错误码：
  - `quest_cooldown`, `no_eligible_quests`, `quest_def_required`, `quest_template_missing`
  - `banditcamp_faction_not_supported`, `pawnlend_tech_too_low`, `ideology_required`, `royalty_required`, `empire_only`

## 7. 关键行为变更
1. 冷却中的 `create_quest`：
- 不注入 ACTIONS
- 仍在 API LIMITS 段落注入剩余冷却时间
- 强行调用时执行层拒绝并回报剩余秒数

2. 任务生成技术异常：
- 不再 fallback `RimChat_AIQuest`
- 直接失败 -> 系统消息 + `Log.Error`

3. 规则不匹配：
- 不再重定向到其他任务模板
- 直接失败 -> 系统消息 + `Log.Warning`

4. 旧静态任务推荐清单：
- 迁移为“仅动态任务清单有效”
- 即使全局系统提示里存在历史静态清单，也会在运行时追加 `QUEST TEMPLATE STRICT OVERRIDE` 硬覆盖

## 8. 测试模板
- [ ] 用例 1：遍历可通信派系，检查 prompt ACTIONS 是否动态裁剪正确
- [ ] 用例 2：人为制造 `CreateQuest` 冷却，确认 `create_quest` 不注入且冷却时间在 prompt 可见
- [ ] 用例 3：模型强行输出被禁模板（如非限定派系 `Mission_BanditCamp`），确认被拒绝并显示失败消息
- [ ] 用例 4：构造任务生成技术异常（站点参数异常），确认无 fallback、直接失败并记录 Error
- [ ] 用例 5：合规模板仍可成功创建，不影响正常任务流程

## 9. 文件落点
- 核心规则服务：`RimChat/DiplomacySystem/ApiActionEligibilityService.cs`
- 执行层拦截：`RimChat/AI/AIActionExecutor.cs`
- 任务生成严格校验：`RimChat/DiplomacySystem/GameAIInterface.cs`
- 注入层动态过滤：`RimChat/Persistence/PromptPersistenceService.cs`
