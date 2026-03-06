# RimDiplomacy - AI Driven Faction Diplomacy

为 RimWorld 带来 AI 驱动的派系外交系统！

## 功能特性

- **AI 控制派系智能对话系统**: 与 AI 派系领袖进行外交对话，请求商队，触发袭击，接取任务
- **RPG风格人物对话**: 与 AI NPC进行沉浸式对话，触发事件，谈情说爱
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

## Social Circle Module (v0.3.14)

### Module Map
- `RimDiplomacy/DiplomacySystem/Social/*.cs`
  - Responsibility: social post data model, leader-aware post text, impact model (goodwill/settlement/incident), like logic, intent-action resolver.
  - Dependencies: `RimWorld.Faction`, `RimWorld.Planet`, `Verse.Scribe`, `AIActionExecutor`, `RimDiplomacySettings`.
- `RimDiplomacy/DiplomacySystem/GameComponent_DiplomacyManager.SocialCircle.cs`
  - Responsibility: scheduler, enqueue pipeline, unread tracking, manual force-generate, dialogue keyword ingress, like interaction.
  - Interface: `EnqueuePublicPost`, `ForceGeneratePublicPost`, `GetSocialPosts`, `GetUnreadSocialPostCount`, `MarkSocialPostsRead`, `TryLikeSocialPost`.
- `RimDiplomacy/UI/Dialog_DiplomacyDialogue.SocialCircle.cs`
  - Responsibility: explicit AI action `publish_public_post` handling and keyword fallback post creation.
- `RimDiplomacy/UI/Dialog_DiplomacyDialogue.SocialCircleView.cs`
  - Responsibility: diplomacy-window social tab UI (filters, feed, like button, unread mark-as-read, toast feedback).
- `RimDiplomacy/Config/RimDiplomacySettings_SocialCircle.cs`
  - Responsibility: social circle settings UI and always-visible debug button for manual generation.
- `1.6/Defs/MainButtonDefs.xml` (removed)
  - Responsibility change: bottom main-tab button is removed; social circle entry moved to diplomacy dialogue window.

### Public Interfaces Added
- AI action protocol:
  - `publish_public_post`
- Runtime APIs:
  - `GameComponent_DiplomacyManager.EnqueuePublicPost(...)`
  - `GameComponent_DiplomacyManager.ForceGeneratePublicPost(DebugGenerateReason reason = DebugGenerateReason.ManualButton)`
  - `GameComponent_DiplomacyManager.GetSocialPosts(int maxCount = 200)`
  - `GameComponent_DiplomacyManager.GetUnreadSocialPostCount()`
  - `GameComponent_DiplomacyManager.MarkSocialPostsRead()`
  - `GameComponent_DiplomacyManager.TryLikeSocialPost(string postId, out int goodwillBonus)`

### Defaults
- Social post interval: `5-7` days (`SocialPostIntervalMinDays` / `SocialPostIntervalMaxDays`)
- Auto action execution from intent: off by default (`EnableSocialCircleAutoActions = false`)

## Strategy Suggestion + FiveDim Overlay Module (v0.3.12)

### Module Map
- `RimDiplomacy/AI/AIResponseParser.cs`
  - Responsibility: parse optional `strategy_suggestions` from LLM JSON output and sanitize to strict 3-item payload.
  - Interface: `ParsedResponse.StrategySuggestions`.
- `RimDiplomacy/Memory/FactionDialogueSession.cs`
  - Responsibility: runtime-only cache for pending strategy suggestions (not serialized).
  - Interface: `pendingStrategySuggestions`.
- `RimDiplomacy/UI/Dialog_DiplomacyDialogue.Strategy.cs`
  - Responsibility: strategy bar rendering, one-click send hidden reply, player-context soft injection, and follow-up strategy request when goodwill dropped but first payload omitted suggestions.
  - Dependencies: negotiator skills/traits, colony wealth, recent messages.
- `RimDiplomacy/UI/Dialog_DiplomacyDialogue.cs`
  - Responsibility: net-goodwill-drop trigger wiring + fixed layout slots for icon/strategy/input + compact reinitiate button.
  - Interface: strategy suggestions shown only for next send cycle after net goodwill decrease.
- `RimDiplomacy/UI/FiveDimensionBar.cs`
  - Responsibility: compact icon anchor + floating compact list overlay for five-dimension values.
  - Interface: `DrawCompactIcon`, `DrawCompactOverlay`, `GetCompactAnchorHeight`.
- `RimDiplomacy/Persistence/PromptPersistenceService.cs`
  - Responsibility: runtime prompt guidance for optional `strategy_suggestions` output contract.

### Public Interfaces Added
- LLM JSON optional field:
  - `strategy_suggestions` (exact 3 items)
  - item fields: `short_label`, `trigger_basis`, `strategy_keywords`, `hidden_reply`
- Parsed response field:
  - `ParsedResponse.StrategySuggestions`
- Runtime behavior:
  - Strategy ability is gated per session by negotiator Social skill: `<5 locked`, `5-9 => 1 use`, `10-14 => 2 uses`, `>=15 => 3 uses`.
  - If `strategy_suggestions` is missing/invalid while ability is available, client sends one additional strategy-only LLM request (non-blocking) and only accepts exactly 3 valid items.
  - Reinitiate button after `exit_dialogue` is delayed by 1 in-game hour cooldown.

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


