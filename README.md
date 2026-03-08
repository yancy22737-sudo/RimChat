# RimChat - AI Driven Faction Diplomacy

## Action Hint UI Module (v0.3.45)

### Module Map
- `RimChat/UI/Dialog_DiplomacyDialogue.ActionHint.cs`
  - Added low-visibility `?` hint near diplomacy send button.
  - Tooltip now lists faction-scoped potential diplomacy actions with availability (`Available/Blocked`).
- `RimChat/UI/Dialog_RPGPawnDialogue.ActionHint.cs`
  - Added low-visibility `?` hint near RPG send button.
  - Tooltip now lists potential RPG API actions.
- `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - Integrated action-hint rendering into input area next to send button.
- `RimChat/UI/Dialog_RPGPawnDialogue.cs`
  - Integrated action-hint rendering into RPG input area and switched send label to keyed translation (`RimChat_SendButton`).
- `1.6/Languages/*/Keyed/RimChat_Keys.xml`
  - Added CN/EN keys for action hint titles, availability tags, and diplomacy action labels.

## Prompt Output Language Module (v0.3.44)

### Module Map
- `RimChat/Config/RimChatSettings.cs`
  - Added settings: `PromptLanguageFollowSystem`, `PromptLanguageOverride`.
  - Added API tab UI: output language selector (`follow system` / `custom`).
- `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - Added `output_language` guidance injection for diplomacy and RPG prompt roots.
- `1.6/Languages/*/Keyed/RimChat_Keys.xml`
  - Added CN/EN language keys for output-language controls and hint text.

## RimTalk Compatibility Module (v0.3.47)

### Module Map
- `RimChat/Compat/RimTalkCompatBridge.cs`
  - Reflection bridge for optional RimTalk runtime compatibility.
  - Binds `RimTalk.API.RimTalkPromptAPI` and `RimTalk.Prompt.ScribanParser` at runtime.
  - Provides:
    - `RenderCompatTemplate(...)` for Scriban rendering with RimTalk context.
    - `PushSessionSummary(...)` for global summary variable sync.
- `RimChat/Compat/RimTalkCompatBridge.Reflection.cs`
  - Reflection helpers for context-variable registration and active-preset mod-entry filtering/rendering.
- `RimChat/Compat/RimTalkCompatBridge.PromptEntries.cs`
  - Prompt-entry creation/insertion bridge (`CreatePromptEntry`/`AddPromptEntry`/`InsertPromptEntryAfterName`) and built-in RimChat variable snapshot integration.
- `RimChat/Compat/RimTalkCompatBridge.EntryReflection.cs`
  - Shared reflection conversion utilities for setting `PromptEntry` fields/properties across RimTalk versions.
- `RimChat/Compat/RimTalkCompatBridge.Models.cs`
  - Shared models: `RimTalkPromptEntryWriteResult`, `RimTalkRegisteredVariable`.
- `RimChat/Memory/DialogueSummaryService.cs`
  - Diplomacy close summaries now push to RimTalk global variables.
  - RPG close summary builder (no extra AI request) now pushes to RimTalk.
- `RimChat/UI/Dialog_RPGPawnDialogue.cs`
  - Manual window close now commits RPG session summary push.
- `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - Appends RimTalk compatibility template at instruction/role stack tail for diplomacy and RPG.
  - RPG channel also appends active RimTalk preset mod-entry render block (`rimtalk_preset_mod_entries`) so plugin prompt entries can affect RPG prompt.
  - Render failures safely fallback to raw template text.
- `RimChat/Config/RimChatSettings_RimTalkCompat.cs`
  - Added settings:
    - `EnableRimTalkPromptCompat`
    - `RimTalkSummaryHistoryLimit`
    - `RimTalkCompatTemplate`
- `RimChat/Config/RimChatSettings_RPG.cs` + `RimChat/Config/RimChatSettings_RPG.RimTalkCompatUI.cs`
  - Added RimTalk compatibility controls in RPG dynamic injection section (applies to both channels).
  - Added RimTalk variable browser (including plugin/custom variables) and one-click variable insertion to compat template.
  - Added prompt-entry add/update UI (name/anchor/role/position/depth/content) to write entries into active RimTalk preset.
- `RimChat/DiplomacySystem/GameComponent_RPGManager.cs`
  - Added late-lifecycle warmup calls so RimTalk variables and compat preset entry registration run after save load/new game init.
- `1.6/Languages/*/Keyed/RimChat_Keys.xml`
  - Added CN/EN keys for RimTalk compatibility UI, variable browser, and entry writer feedback.

### RimTalk Global Keys
- `rimchat_last_session_summary`
- `rimchat_last_diplomacy_summary`
- `rimchat_last_rpg_summary`
- `rimchat_recent_session_summaries`

为 RimWorld 带来 AI 驱动的派系外交系统！

## 功能特性

- **AI 控制派系智能对话系统**: 与 AI 派系领袖进行外交对话，请求商队，触发袭击，接取任务
- **RPG风格人物对话**: 与 AI NPC进行沉浸式对话，触发事件，谈情说爱
- **NPC 主动对话系统**: 在线状态下派系可主动发信；支持忙碌延迟队列与因果触发
- **对话 Token 用量可视化**: API 设置页底部显示最近一次外交/RPG对话 token 使用量与负载分档（低/中/高），并在服务端 usage 异常时自动回退估算
- **RPG-外交双向记忆链路**: 离图摘要写入派系记忆、外交会话摘要反哺 RPG 提示词，提升长期世界状态感知

## RPG Dialogue Tuning (v0.3.35)

- `ExitDialogueCooldown` now blocks re-chat for `1` in-game day (`60000` ticks).
- RPG memory fallback now uses a single `80%` roll once NPC dialogue reaches `5` rounds; success appends `TryGainMemory`.
- Recruit auto-completion fallback is removed; only model-originated Recruit actions are executed.
- RPG action/system feedback uses a translucent panel and surfaces cooldown remaining time and memory-roll details.

## Cross-Channel Memory Module (v0.3.29)

### Module Map
- `RimChat/Memory/CrossChannelSummaryRecord.cs`
  - Responsibility: 统一的跨通道摘要数据模型（来源、相关 Pawn/Faction、摘要正文、关键事实、tick、置信度、hash）。
- `RimChat/Memory/DialogueSummaryService.cs`
  - Responsibility: 规则优先摘要生成、低置信 LLM 回退、RPG 动态记忆块拼装（20/6/2200 预算）。
  - Interface: `TryRecordDiplomacySessionSummary(...)`, `TryRecordRpgDepartSummary(...)`, `BuildRpgDynamicFactionMemoryBlock(...)`。
- `RimChat/Memory/RpgDialogueTraceTracker.cs`
  - Responsibility: 追踪最近 RPG 对话轮次与最后互动 tick，供离图触发过滤使用。
  - Interface: `RegisterTurn(...)`, `TryConsumeRecentForExit(...)`。
- `RimChat/Patches/PawnExitMapPatch_RpgMemory.cs`
  - Responsibility: Patch `Pawn.ExitMap(bool, Rot4)`，在严格条件下触发离图摘要写入。
- `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - Responsibility: 对话窗口关闭时按“新增消息基线”触发外交会话摘要（仅一次）。
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Responsibility: RPG 系统提示词注入 Dynamic Faction Memory Block（人格提示词后、API规则前）；外交提示词可读跨通道摘要。
- `RimChat/Memory/LeaderMemoryManager.cs` + `RimChat/Memory/LeaderMemoryJsonCodec.cs`
  - Responsibility: 跨通道摘要持久化、上限裁剪、旧字段兼容解析与 JSON 字段映射修正；存档接管时会话历史回填与记忆基线快照初始化。
  - Interface: `OnNewGame()` 初始化新档基线记忆，`OnAfterGameLoad(IEnumerable<FactionDialogueSession>)` 回填读档前会话历史并补齐基础记忆。
- `RimChat/Memory/RpgNpcDialogueArchive.cs` + `RimChat/Memory/RpgNpcDialogueArchiveJsonCodec.cs` + `RimChat/Memory/RpgNpcDialogueArchiveManager.cs`
  - Responsibility: RPG 对话按 NPC 独立外部文件持久化（每 NPC 一份），并在读档后回填到 RPG 运行态（人格 Prompt、关系值、冷却截止 tick）。
  - Storage: `save_data/<saveName>/rpg_npc_dialogues/npc_<pawnId>.json`。

### Persistence Notes (v0.3.31)
- `LeaderMemory` JSON 现已补齐核心字段：`lastDecayCheckTick`、`playerRelationValues`、`FactionMemoryEntry.firstContactTick/lastMentionedTick/relationHistory`。
- 领袖记忆写盘不再额外裁切到 50 条；仅受内存上限控制（当前 200 条）。

## Environment Prompt Module (v0.3.25)

### Module Map
- `RimChat/Config/SystemPromptConfig.cs`
  - Responsibility: environment prompt root data model and default seed (`Worldview`, `EnvironmentContextSwitches`, `SceneSystem`, `SceneEntries`, `RpgSceneParamSwitches`, `EventIntelPrompt`).
  - Interface: runtime config persisted in user config path (`Config/RimChat/Prompt/Custom/system_prompt_config.json`), default seed loaded from bundled `Prompt/Default/SystemPrompt_Default.json`.
- `RimChat/Config/EventIntelPromptConfig.cs`
  - Responsibility: event memory injection switches and limits (`DaysWindow`, `MaxStoredRecords`, `MaxInjectedItems`, `MaxInjectedChars`, channel toggles).
- `RimChat/WorldState/WorldEventLedgerComponent.cs`
  - Responsibility: persistent world-event ledger, letter polling capture, raid casualty aggregation and faction-knowledge filtering.
  - Interface: `GetRecentWorldEvents(...)`, `GetRecentRaidBattleReports(...)`, `NotifyPawnKilled(...)`.
- `RimChat/Patches/PawnKillPatch_WorldEventLedger.cs`
  - Responsibility: hook `Pawn.Kill` and feed raid casualty aggregation.
- `RimChat/Persistence/DialogueScenarioContext.cs`
  - Responsibility: unified channel/source/scenario context container for scene prompt matching.
  - Interface: `CreateDiplomacy(...)`, `CreateRpg(...)`.
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Responsibility: environment prompt assembly, event intel injection, adaptive scene matching with hard length caps.
  - Interface: `BuildEnvironmentPromptBlocks(...)`, `AppendRecentWorldEventIntel(...)`, `BuildFullSystemPrompt(..., bool isProactive, IEnumerable<string> additionalSceneTags)` / `BuildRPGFullSystemPrompt(..., bool isProactive, IEnumerable<string> additionalSceneTags)`.
- `RimChat/Config/RimChatSettings_Prompt*.cs`
  - Responsibility: Prompts tab environment section UI (worldview, environment parameter toggles, event memory switches, scene CRUD, channel toggles, RPG deep-param switches, preview).
  - Interface: section key `RimChat_EnvironmentPromptsSection`.

### Behavior
- Injection order: `Worldview -> Environment Parameters -> Recent World Events & Battle Intel -> Scene Prompt Layers -> Existing Prompt System`.
- Environment parameters: time/date/season/weather/location+temperature/terrain/beauty/cleanliness/surroundings/wealth are switchable per item.
- Event memory: home-map public events from `LetterStack` + direct-known raid casualty reports (attacker/defender deaths + defender downed peak), filtered by faction visibility.
- RPG player-faction context: optional colony inventory summary and home-world alerts reflected from active vanilla alerts.
- RPG player-faction context: optional recent job state and pawn attribute levels are injectable via `RpgSceneParamSwitches`.
- Fact grounding: diplomacy/RPG prompts append explicit evidence constraints; unsupported claims must be questioned in-character.
- Scene matching: ALL tags must match; all matched entries are appended by descending priority.
- Length control: per-scene cap + total-cap enforced before append.
- Channel coverage: diplomacy manual/proactive + RPG manual/proactive all use the same environment system.
- Manual channel tags: diplomacy and RPG manual dialogue can inject `additionalSceneTags` from settings CSV (`DiplomacyManualSceneTagsCsv`, `RpgManualSceneTagsCsv`).

## 构建说明

### 前置要求

- .NET Framework 4.8 SDK
- RimWorld 1.6
- Harmony Mod
- 使用 `build.ps1` 构建时，会先执行编码守卫（扫描 UI 字符串中的典型乱码模式）；命中后会直接中止构建并给出文件/行号。

## 版本历史

### v1.0.0
- 初始版本
- AI 派系控制
- 基础外交对话
- 世界新闻系统


## API Filter Module (v0.3.5)

### Module Map
- `RimChat/DiplomacySystem/ApiActionEligibilityService.cs`
  - Responsibility: centralized action eligibility + quest template validation.
  - Dependencies: `GameAIInterface`, `RimChatMod.InstanceSettings`, `DLCCompatibility`, RimWorld `Faction/QuestScriptDef`.
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Responsibility: dynamic prompt action filtering and quest availability injection.
  - Interface: builds prompt with faction-scoped ACTIONS and blocked-action hints.
- `RimChat/Prompting/RpgApiPromptTextBuilder.cs`
  - Responsibility: shared RPG API action-definition text assembly for runtime prompt injection and settings preview.
  - Interface: `AppendActionDefinitions(StringBuilder)`.
- `RimChat/AI/AIActionExecutor.cs`
  - Responsibility: runtime precheck before executing parsed AI actions.
  - Interface: denies invalid actions with explicit failure messages.
- `RimChat/DiplomacySystem/GameAIInterface.cs`
  - Responsibility: strict `CreateQuest` validation and quest generation.
  - Interface: no redirect fallback for rule mismatch or technical generation errors.

### Public Interfaces Added
- `ApiActionEligibilityService.GetAllowedActions(Faction faction)`
- `ApiActionEligibilityService.ValidateActionExecution(Faction faction, string actionType, Dictionary<string, object> parameters)`
- `ApiActionEligibilityService.ValidateCreateQuest(Faction faction, string questDefName, Dictionary<string, object> parameters)`
- `ApiActionEligibilityService.GetQuestEligibilityReport(Faction faction)`

## Presence Module (v0.3.6)

### Module Map
- `RimChat/Memory/FactionPresenceState.cs`
  - Responsibility: faction presence state data model (`Online/Offline/DoNotDisturb`) and cache metadata persistence.
  - Dependencies: `RimWorld.Faction`, `Verse.Scribe`.
- `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.cs`
  - Responsibility: presence schedule evaluation, 8-hour cache lock, forced offline duration, and AI presence action application.
  - Interface: `RefreshPresenceOnDialogueOpen`, `LockPresenceCacheOnDialogueClose`, `ApplyPresenceAction`, `GetPresenceStatus`, `CanSendMessage`.
- `RimChat/UI/Dialog_DiplomacyDialogue.Presence.cs`
  - Responsibility: dialogue-window presence badge rendering, input gate (read-only), and reinitiate flow after `exit_dialogue`.
  - Interface: handles AI actions `exit_dialogue`, `go_offline`, `set_dnd` inside dialogue execution flow.
- `RimChat/Config/RimChatSettings*.cs`
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
- `RimChat/DiplomacySystem/GameComponent_RPGManager.cs`
  - Responsibility: per-save persistence for pawn-specific RPG persona prompts.
  - Interface: `GetPawnPersonaPrompt`, `SetPawnPersonaPrompt`.
- `RimChat/Config/RimChatSettings_RPG.cs`
  - Responsibility: RPG settings tab UI for selecting colony pawns, editing independent persona prompts, and forcing PawnRPG proactive debug trigger in RPG Pawn persona section.
  - Dependencies: `PawnsFinder`, `GameComponent_RPGManager`, keyed language strings.
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Responsibility: inject pawn persona override block into RPG system prompt assembly when configured.
  - Dependencies: `GameComponent_RPGManager`, target pawn context.

### Public Interfaces Added
- `GameComponent_RPGManager.GetPawnPersonaPrompt(Pawn pawn)`
- `GameComponent_RPGManager.SetPawnPersonaPrompt(Pawn pawn, string prompt)`

## Social Circle Module (v0.3.14)

### Module Map
- `RimChat/DiplomacySystem/Social/*.cs`
  - Responsibility: social post data model, leader-aware post text, impact model (goodwill/settlement/incident), like logic, intent-action resolver.
  - Dependencies: `RimWorld.Faction`, `RimWorld.Planet`, `Verse.Scribe`, `AIActionExecutor`, `RimChatSettings`.
- `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.SocialCircle.cs`
  - Responsibility: scheduler, enqueue pipeline, unread tracking, manual force-generate, dialogue keyword ingress, like interaction.
  - Interface: `EnqueuePublicPost`, `ForceGeneratePublicPost`, `GetSocialPosts`, `GetUnreadSocialPostCount`, `MarkSocialPostsRead`, `TryLikeSocialPost`.
- `RimChat/UI/Dialog_DiplomacyDialogue.SocialCircle.cs`
  - Responsibility: explicit AI action `publish_public_post` handling and keyword fallback post creation.
- `RimChat/UI/Dialog_DiplomacyDialogue.SocialCircleView.cs`
  - Responsibility: diplomacy-window social tab UI (filters, feed, like button, unread mark-as-read, toast feedback).
- `RimChat/Config/RimChatSettings_SocialCircle.cs`
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
- `RimChat/AI/AIResponseParser.cs`
  - Responsibility: parse optional `strategy_suggestions` from LLM output and sanitize to strict 3-item payload.
  - Interface: `ParsedResponse.StrategySuggestions`.
- `RimChat/Memory/FactionDialogueSession.cs`
  - Responsibility: runtime-only cache for pending strategy suggestions (not serialized).
  - Interface: `pendingStrategySuggestions`.
- `RimChat/UI/Dialog_DiplomacyDialogue.Strategy.cs`
  - Responsibility: strategy bar rendering, one-click send hidden reply, player-context soft injection, and follow-up strategy request when goodwill dropped but first payload omitted suggestions.
  - Dependencies: negotiator skills/traits, colony wealth, recent messages.
- `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - Responsibility: net-goodwill-drop trigger wiring + fixed layout slots for icon/strategy/input + compact reinitiate button.
  - Interface: strategy suggestions shown only for next send cycle after net goodwill decrease.
- `RimChat/UI/FiveDimensionBar.cs`
  - Responsibility: compact icon anchor + floating compact list overlay for five-dimension values.
  - Interface: `DrawCompactIcon`, `DrawCompactOverlay`, `GetCompactAnchorHeight`.
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Responsibility: runtime prompt guidance for optional `strategy_suggestions` output contract.

### Public Interfaces Added
- LLM JSON optional field:
  - `strategy_suggestions` (exact 3 items)
  - item fields: `short_label`, `trigger_basis`, `strategy_keywords`, `hidden_reply`
- Parsed response field:
  - `ParsedResponse.StrategySuggestions`
- Runtime behavior:
  - Strategy ability is gated per session by negotiator Social skill: `<5 locked`, `5-9 => 1 use`, `10-14 => 2 uses`, `>=15 => 3 uses`.
  - If `strategy_suggestions` is missing/invalid while ability is available, client sends one additional strategy-only LLM request (non-blocking).
  - If the follow-up response is still non-JSON, client-side narrative fallback extracts strategy sentences and backfills 3 buttons.
  - Reinitiate button after `exit_dialogue` is delayed by 1 in-game hour cooldown.

## NPC Proactive Dialogue Module (v0.3.9)

### Module Map
- `RimChat/NpcDialogue/NpcDialogueModels.cs`
  - Responsibility: proactive trigger types/categories + queue item + per-faction push state persistence models.
  - Dependencies: `RimWorld.Faction`, `Verse.Scribe`.
- `RimChat/NpcDialogue/GameComponent_NpcDialoguePushManager.cs`
  - Responsibility: end-to-end proactive pipeline (`collect -> gate -> queue/generate -> deliver`), tick scheduling, busy detection, queue lifecycle, LLM retry/drop policy.
  - Dependencies: `AIChatServiceAsync`, `GameComponent_DiplomacyManager`, `PromptPersistenceService`, `LeaderMemoryManager`.
- `RimChat/NpcDialogue/ChoiceLetter_NpcInitiatedDialogue.cs`
  - Responsibility: right-side choice letter delivery and one-click open diplomacy dialogue interaction.
  - Dependencies: `Verse.ChoiceLetter`, `RimChat.UI.Dialog_DiplomacyDialogue`.
- `RimChat/Patches/TradeDealPatch_NpcDialogue.cs`
  - Responsibility: causal trigger from low-quality weapon sales (`Poor` and below) after trade execution.
  - Dependencies: `RimWorld.TradeDeal`, `RimWorld.TradeSession`.
- `RimChat/Patches/FactionGoodwillPatch_NpcDialogue.cs`
  - Responsibility: causal trigger from significant goodwill shifts (`|delta| >= 10`) and hostile-warning proxy tagging.
  - Dependencies: `RimWorld.Faction.TryAffectGoodwillWith`.
- `RimChat/Patches/UIRootPlayPatch_NpcDialogue.cs`
  - Responsibility: left-click cadence sampling for "busy by click-rate" detection.
  - Dependencies: `RimWorld.UIRoot_Play`, `UnityEngine.Event`.
- `RimChat/Config/NpcPushFrequencyMode.cs`
  - Responsibility: proactive frequency strategy enum (`Low/Medium/High`).
- `RimChat/Config/RimChatSettings_NpcPush.cs`
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

## PawnRPG Proactive Channel Module (v0.3.19)

### Module Map
- `RimChat/PawnRpgPush/PawnRpgPushModels.cs`
  - Responsibility: PawnRPG proactive trigger context, delayed queue model, per-NPC cooldown anchor, per-faction threat-edge state persistence.
  - Dependencies: `RimWorld.Faction`, `Verse.Pawn`, `Verse.Scribe`, `NpcDialogueTriggerType/NpcDialogueCategory`.
- `RimChat/PawnRpgPush/GameComponent_PawnRpgDialoguePushManager.cs`
  - Responsibility: independent PawnRPG proactive scheduler (`intake -> gate -> queue -> generate -> deliver`), regular/causal trigger intake, quest deadline conditional trigger, threat edge scan.
  - Dependencies: `AIChatServiceAsync`, `RimChatSettings`, `PromptPersistenceService`, `LeaderMemoryManager`, `Verse.GameComponent`.
- `RimChat/PawnRpgPush/GameComponent_PawnRpgDialoguePushManager.Candidates.cs`
  - Responsibility: NPC/player candidate pairing, relation thresholds, intimate bypass, busy triple-gate, sleep/downed/working availability checks.
  - Dependencies: `RimWorld.Map/Pawn/Quest`, `Verse.AI.JobDefOf`, `UnityEngine.Mathf`.
- `RimChat/PawnRpgPush/GameComponent_PawnRpgDialoguePushManager.Generation.cs`
  - Responsibility: LLM request assembly, one retry policy, model output sanitize, PawnRPG letter delivery and cooldown stamping on successful delivery only.
  - Dependencies: `AIChatServiceAsync`, `PromptPersistenceService`, `ChoiceLetter_PawnRpgInitiatedDialogue`.
- `RimChat/PawnRpgPush/ChoiceLetter_PawnRpgInitiatedDialogue.cs`
  - Responsibility: right-side PawnRPG proactive letter with one-click open `Dialog_RPGPawnDialogue`.
  - Dependencies: `Verse.ChoiceLetter`, `RimChat.UI.Dialog_RPGPawnDialogue`.
- `RimChat/Patches/TradeDealPatch_NpcDialogue.cs`
  - Responsibility change: keeps legacy low-quality weapon trigger; additionally reports trade completion trigger into PawnRPG proactive channel.
- `RimChat/Patches/FactionGoodwillPatch_NpcDialogue.cs`
  - Responsibility change: keeps legacy goodwill trigger; additionally reports goodwill-shift trigger into PawnRPG proactive channel.
- `RimChat/Patches/UIRootPlayPatch_NpcDialogue.cs`
  - Responsibility change: left-click cadence now reports to both legacy and PawnRPG proactive channels.
- `RimChat/Config/RimChatSettings_NpcPush.cs`
  - Responsibility change: keeps legacy debug trigger button and adds PawnRPG debug force-trigger button.

### Public Interfaces Added
- `GameComponent_PawnRpgDialoguePushManager.RegisterTradeCompletedTrigger(Faction faction, int soldCount, int boughtCount)`
- `GameComponent_PawnRpgDialoguePushManager.RegisterGoodwillShiftTrigger(Faction faction, int goodwillDelta, string reason, bool likelyHostile)`
- `GameComponent_PawnRpgDialoguePushManager.RegisterThreatStateTrigger(Faction faction, bool hasHive, bool hasHostiles)`
- `GameComponent_PawnRpgDialoguePushManager.RegisterPlayerLeftClick()`
- `GameComponent_PawnRpgDialoguePushManager.DebugForcePawnRpgProactiveDialogue()`
- `ChoiceLetter_PawnRpgInitiatedDialogue.Setup(Pawn npcPawn, Pawn playerPawn, TaggedString labelText, TaggedString bodyText, LetterDef letterDef)`
- `ChoiceLetter_PawnRpgInitiatedDialogue.IsDialogueAlreadyOpen(Pawn playerPawn, Pawn npcPawn)`

### Runtime Rules
- Scope isolation: only applies to PawnRPG proactive channel; legacy faction proactive diplomacy channel remains unchanged. PawnRPG now supports both non-player faction -> player pawn and player-faction internal pawn -> pawn proactive dialogues.
- Per-NPC cooldown: one successful delivery per NPC every `150000` ticks (`6` in-game days).
- Colony global cooldown: non-warning categories can be successfully delivered at most once every `75000` ticks (`3` in-game days).
- Warning exception: warning category bypasses only colony global cooldown, but still respects per-NPC cooldown.
- Thresholds: intimate relation (`Spouse/Fiance/Lover`) bypasses; otherwise `Opinion >= 35`. Conditional low mood trigger uses `Mood <= 0.30`.
- Busy and availability gates: drafted/hostile/click-rate busy gate; sleeping/downed/working NPC or player pawn causes delayed queue entry.
- Queue policy: `3` max per faction, `12` in-game hour expiry (settings default); overdue items discarded automatically.
- LLM policy: all proactive content is LLM-generated; retry once on failure, then drop. Cooldowns update only after successful letter delivery.
- Letter-open behavior: opening a PawnRPG proactive letter seeds the RPG dialogue with that proactive line as the first assistant message and does not regenerate a new opener.







## Prompt Authoring Updates (v0.3.34)
- Prompt section navigation now preserves scroll position.
- API action list and description editor use independent scroll states.
- Added prompt variable picker with click-to-insert `{{variable}}` tokens.
- Added per-section variable validation and environment scene render diagnostics in preview.
