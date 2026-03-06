# RimDiplomacy - AI Driven Faction Diplomacy

为 RimWorld 带来 AI 驱动的派系外交系统！

## 功能特性

- **AI 控制派系**: 让派系由 AI 控制，实现动态外交
- **智能对话系统**: 与 AI 派系领袖进行外交对话
- **动态世界新闻**: 每 2-3 天播报世界动态
- **智能事件触发**: 作为原版 Storyteller 的补充

## 构建说明

### 前置要求

- .NET Framework 4.8 SDK
- RimWorld 1.6
- Harmony Mod

## 版本历史

### v1.0.0
- 初始版本
- AI 派系控制
- 基础外交对话
- 世界新闻系统


## API Filter Module (v0.3.5)

### Module Map
- `RimDiplomacy/DiplomacySystem/ApiActionEligibilityService.cs`
  - Responsibility: centralized action eligibility + quest template validation.
  - Dependencies: `GameAIInterface`, `RimDiplomacyMod.InstanceSettings`, `DLCCompatibility`, RimWorld `Faction/QuestScriptDef`.
- `RimDiplomacy/Persistence/PromptPersistenceService.cs`
  - Responsibility: dynamic prompt action filtering and quest availability injection.
  - Interface: builds prompt with faction-scoped ACTIONS and blocked-action hints.
- `RimDiplomacy/AI/AIActionExecutor.cs`
  - Responsibility: runtime precheck before executing parsed AI actions.
  - Interface: denies invalid actions with explicit failure messages.
- `RimDiplomacy/DiplomacySystem/GameAIInterface.cs`
  - Responsibility: strict `CreateQuest` validation and quest generation.
  - Interface: no redirect fallback for rule mismatch or technical generation errors.

### Public Interfaces Added
- `ApiActionEligibilityService.GetAllowedActions(Faction faction)`
- `ApiActionEligibilityService.ValidateActionExecution(Faction faction, string actionType, Dictionary<string, object> parameters)`
- `ApiActionEligibilityService.ValidateCreateQuest(Faction faction, string questDefName, Dictionary<string, object> parameters)`
- `ApiActionEligibilityService.GetQuestEligibilityReport(Faction faction)`
