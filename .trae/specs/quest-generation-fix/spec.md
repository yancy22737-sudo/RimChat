# Quest 生成错误修复 Spec

## Why

AI 对话系统在调用 `CreateQuest` API 时，多个原版任务模板生成失败，导致红字报错和任务无法正常创建。主要问题包括：
1. `OpportunitySite_ItemStash` 任务因 `sitePartsParams=null` 导致 NullReferenceException
2. `Mission_BanditCamp` 任务因 `requiredPawnCount=-1` 无效参数报错
3. Grammar 解析失败（中文翻译缺失关键字段）

## What Changes

- **修复 `OpportunitySite_ItemStash` 任务生成**：确保 `sitePartsParams` 正确初始化，添加必要的派系和站点参数
- **修复 `Mission_BanditCamp` 任务生成**：确保 `requiredPawnCount` 基于玩家人口正确计算
- **增强错误处理**：当原版任务参数不完整时，自动回退到通用任务 `RimDiplomacy_AIQuest`
- **改进参数验证**：在任务生成前预检查关键参数，提前拦截不合理的任务请求

## Impact

- Affected specs: CreateQuest API, AIActionExecutor
- Affected code: 
  - `RimDiplomacy/DiplomacySystem/GameAIInterface.cs` (CreateQuest 方法)
  - `RimDiplomacy/AI/AIActionExecutor.cs` (ExecuteCreateQuest 方法)

## ADDED Requirements

### Requirement: Quest Parameter Validation

系统应在调用原版任务生成前验证关键参数的完整性和有效性。

#### Scenario: OpportunitySite_ItemStash 参数验证
- **WHEN** AI 请求创建 `OpportunitySite_ItemStash` 任务
- **THEN** 系统应确保：
  - `points` >= 200（最小威胁点数）
  - `siteFaction` 存在且非永久敌对
  - 如果没有 `asker`，设置 `askerIsNull=true`

#### Scenario: Mission_BanditCamp 参数验证
- **WHEN** AI 请求创建 `Mission_BanditCamp` 任务
- **THEN** 系统应确保：
  - `requiredPawnCount` 基于玩家自由殖民者数量计算（2-5人）
  - `enemyFaction` 存在且为敌对派系
  - `enemiesLabel` 已设置

### Requirement: Graceful Fallback

当原版任务生成失败时，系统应自动回退到通用任务模板。

#### Scenario: 站点生成失败回退
- **WHEN** 原版任务生成抛出 `NullReferenceException` 或站点生成错误
- **THEN** 系统应自动使用 `RimDiplomacy_AIQuest` 模板创建任务
- **AND** 记录警告日志说明回退原因

#### Scenario: 派系不兼容回退
- **WHEN** 任务模板要求特定派系类型（如 Empire）但当前派系不匹配
- **THEN** 系统应自动回退到兼容的任务模板

## MODIFIED Requirements

### Requirement: CreateQuest Method

原 `CreateQuest` 方法需要增强参数预处理逻辑：

**修改前**：直接传递参数到 QuestGen，依赖原版脚本处理缺失参数
**修改后**：在调用 QuestGen 前自动补全必要参数，验证参数有效性

关键修改点：
1. `OpportunitySite_ItemStash` 需要确保 `sitePartsParams` 不为 null
2. `Mission_BanditCamp` 需要确保 `requiredPawnCount` > 0
3. 所有任务需要确保 `points` 有合理默认值

## Root Cause Analysis

### 问题1: sitePartsParams = null

```
Slate vars:
sitePartsParams=null
```

**原因**：`QuestNode_GetSitePartDefsByTagsAndFaction` 节点未能正确生成站点部件定义，导致后续 `QuestNode_GetDefaultSitePartsParams` 无法生成参数。

**解决方案**：在调用原版脚本前，预设置 `siteFaction`，确保派系上下文完整。

### 问题2: requiredPawnCount = -1

```
Mission 'Quest4.BanditCamp' of type 'QuestNode_Root_Mission_BanditCamp' and def 'Mission_BanditCamp' has invalid required pawn count (-1) or population (3).
```

**原因**：`Mission_BanditCamp` 的 `QuestNode_Root_Mission_BanditCamp` 节点内部计算 `requiredPawnCount` 时使用了玩家人口，但传入的参数覆盖了计算逻辑。

**解决方案**：不传递 `requiredPawnCount` 参数，让原版脚本自行计算；或确保传入值 >= 2。

### 问题3: Grammar 解析失败

```
Grammar unresolvable. Root 'questDescription'
```

**原因**：中文翻译缺少 `allSitePartsDescriptionsExceptFirst` 等关键字段的翻译规则。

**解决方案**：确保站点部件正确生成，这样 Grammar 系统才能生成描述文本。

## Technical Notes

1. `QuestNode_GetSitePartDefsByTagsAndFaction` 需要有效的派系上下文才能选择合适的站点部件
2. `Mission_BanditCamp` 是 Royalty DLC 任务，需要检查 DLC 是否激活
3. 永久敌对派系（如海盗）不应发起外交类任务
