# RimDiplomacy - AI Driven Faction Diplomacy

为 RimWorld 带来 AI 驱动的派系外交系统！

## 功能特性

- **AI 控制派系**: 让派系由 AI 控制，实现动态外交
- **智能对话系统**: 与 AI 派系领袖进行外交对话
- **动态世界新闻**: 每 2-3 天播报世界动态
- **智能事件触发**: 作为原版 Storyteller 的补充
- **NPC 主动对话系统**: 在线状态下派系可主动发信；支持忙碌延迟队列与因果触发

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

## Presence Module (v0.3.6)

### Module Map
- `RimDiplomacy/Memory/FactionPresenceState.cs`
  - Responsibility: faction presence state data model (`Online/Offline/DoNotDisturb`) and cache metadata persistence.
  - Dependencies: `RimWorld.Faction`, `Verse.Scribe`.
- `RimDiplomacy/DiplomacySystem/GameComponent_DiplomacyManager.cs`
  - Responsibility: presence schedule evaluation, 8-hour cache lock, forced offline duration, and AI presence action application.
  - Interface: `RefreshPresenceOnDialogueOpen`, `LockPresenceCacheOnDialogueClose`, `ApplyPresenceAction`, `GetPresenceStatus`, `CanSendMessage`.
- `RimDiplomacy/UI/Dialog_DiplomacyDialogue.Presence.cs`
  - Responsibility: dialogue-window presence badge rendering, input gate (read-only), and reinitiate flow after `exit_dialogue`.
  - Interface: handles AI actions `exit_dialogue`, `go_offline`, `set_dnd` inside dialogue execution flow.
- `RimDiplomacy/Config/RimDiplomacySettings*.cs`
  - Responsibility: player-configurable presence parameters (basic + advanced tech-level profiles).
  - Interface: UI sliders/toggles + save/load via `ExposeData_AI`.

### Public Interfaces Added
- AI action protocol:
  - `exit_dialogue`
  - `go_offline`
  - `set_dnd`
- Settings keys (runtime):
  - `EnableFactionPresenceStatus`
  - `PresenceCacheHours`
  - `PresenceForcedOfflineHours`
  - `PresenceNightBiasEnabled`
  - `PresenceNightStartHour`, `PresenceNightEndHour`, `PresenceNightOfflineBias`
  - `PresenceUseAdvancedProfiles` + per-tech start/duration fields

## RPG Persona Module (v0.3.7)

### Module Map
- `RimDiplomacy/DiplomacySystem/GameComponent_RPGManager.cs`
  - Responsibility: per-save persistence for pawn-specific RPG persona prompts.
  - Interface: `GetPawnPersonaPrompt`, `SetPawnPersonaPrompt`.
- `RimDiplomacy/Config/RimDiplomacySettings_RPG.cs`
  - Responsibility: RPG settings tab UI for selecting colony pawns and editing independent persona prompts.
  - Dependencies: `PawnsFinder`, `GameComponent_RPGManager`, keyed language strings.
- `RimDiplomacy/Persistence/PromptPersistenceService.cs`
  - Responsibility: inject pawn persona override block into RPG system prompt assembly when configured.
  - Dependencies: `GameComponent_RPGManager`, target pawn context.

### Public Interfaces Added
- `GameComponent_RPGManager.GetPawnPersonaPrompt(Pawn pawn)`
- `GameComponent_RPGManager.SetPawnPersonaPrompt(Pawn pawn, string prompt)`

## NPC Proactive Dialogue Module (v0.3.9)

### Module Map
- `RimDiplomacy/NpcDialogue/NpcDialogueModels.cs`
  - Responsibility: proactive trigger types/categories + queue item + per-faction push state persistence models.
  - Dependencies: `RimWorld.Faction`, `Verse.Scribe`.
- `RimDiplomacy/NpcDialogue/GameComponent_NpcDialoguePushManager.cs`
  - Responsibility: end-to-end proactive pipeline (`collect -> gate -> queue/generate -> deliver`), tick scheduling, busy detection, queue lifecycle, LLM retry/drop policy.
  - Dependencies: `AIChatServiceAsync`, `GameComponent_DiplomacyManager`, `PromptPersistenceService`, `LeaderMemoryManager`.
- `RimDiplomacy/NpcDialogue/ChoiceLetter_NpcInitiatedDialogue.cs`
  - Responsibility: right-side choice letter delivery and one-click open diplomacy dialogue interaction.
  - Dependencies: `Verse.ChoiceLetter`, `RimDiplomacy.UI.Dialog_DiplomacyDialogue`.
- `RimDiplomacy/Patches/TradeDealPatch_NpcDialogue.cs`
  - Responsibility: causal trigger from low-quality weapon sales (`Poor` and below) after trade execution.
  - Dependencies: `RimWorld.TradeDeal`, `RimWorld.TradeSession`.
- `RimDiplomacy/Patches/FactionGoodwillPatch_NpcDialogue.cs`
  - Responsibility: causal trigger from significant goodwill shifts (`|delta| >= 10`) and hostile-warning proxy tagging.
  - Dependencies: `RimWorld.Faction.TryAffectGoodwillWith`.
- `RimDiplomacy/Patches/UIRootPlayPatch_NpcDialogue.cs`
  - Responsibility: left-click cadence sampling for "busy by click-rate" detection.
  - Dependencies: `RimWorld.UIRoot_Play`, `UnityEngine.Event`.
- `RimDiplomacy/Config/NpcPushFrequencyMode.cs`
  - Responsibility: proactive frequency strategy enum (`Low/Medium/High`).
- `RimDiplomacy/Config/RimDiplomacySettings_NpcPush.cs`
  - Responsibility: proactive settings fields + AI settings tab UI section.

### Public Interfaces Added
- `GameComponent_NpcDialoguePushManager.RegisterLowQualityTradeTrigger(Faction faction, int lowQualityCount, QualityCategory worstQuality)`
- `GameComponent_NpcDialoguePushManager.RegisterGoodwillShiftTrigger(Faction faction, int goodwillDelta, string reason, bool likelyHostile)`
- `GameComponent_NpcDialoguePushManager.RegisterPlayerLeftClick()`
- `ChoiceLetter_NpcInitiatedDialogue.Setup(Faction faction, TaggedString labelText, TaggedString bodyText, LetterDef letterDef)`
- `GameComponent_NpcDialoguePushManager.DebugForceRandomProactiveDialogue()` (debug force-trigger entry)

### Runtime Rules
- Tick intervals: regular evaluation every `6000` ticks; queue processing every `600` ticks.
- Cooldown: same faction proactive push cooldown random `1-3` in-game days after successful delivery.
- Queue policy: per-faction cap default `3`, expiry default `12` in-game hours.
- Busy gate: drafted colonist / hostile pawns on player home map / left-click >= `12` within `6` seconds.
- Presence gate: only delivers/generates when faction is `Online`; otherwise queued.
- LLM policy: every proactive message must be generated by LLM; one retry on failure then discard and log.



