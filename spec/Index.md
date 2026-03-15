# RimChat - AI Driven Faction Diplomacy
## RimTalk Seed Localization Boot Safety Refactor (v0.6.19)

### Module Map
- `RimChat/Config/RimTalkPromptEntrySeedSynchronizer.cs`
  - Dependencies: `RimTalkPromptEntryChannelCatalog`, `RimTalkChannelCompatConfig`, `RimTalkPromptEntryConfig`.
  - Responsibility: centralize prompt-entry channel normalization and missing-seed auto-fill for each root channel.
- `RimChat/Config/RimTalkPromptEntryChannelCatalog.cs`
  - Dependencies: RimTalk entry model + localization keys + Verse language runtime.
  - Responsibility: provide safe seed/channel label localization with active-language guard and deterministic English fallback.
- `RimChat/Config/RimChatSettings.cs`
  - Dependencies: prompt seed synchronizer + RimTalk channel config accessors.
  - Responsibility: route prompt-entry coverage checks through a dedicated synchronizer instead of inline seeding logic.

### Behavior Changes
- Fixed: startup/load path no longer emits `No active language! Cannot translate from key RimChat_RimTalkEntrySeed_*` errors when settings are loaded before language activation.
- Changed: missing default RimTalk channel entries now use guarded localization (translate only when language runtime is ready, fallback to stable English labels otherwise).
- Refactor: channel coverage + seed fill logic moved out of `RimChatSettings` into a dedicated config helper to reduce coupling and improve maintainability.

## RimTalk Entry Channel Activation + Workbench Dead-Zone Fix (v0.6.18)

### Module Map
- `RimChat/Config/RimTalkPromptEntryChannelCatalog.cs`
  - Dependencies: RimTalk entry model + localization keys.
  - Responsibility: centralize prompt-channel ids/labels, root-channel default seeds, channel normalization, and runtime channel-match policy.
- `RimChat/Config/RimChatSettings.cs`, `RimChat/Config/RimTalkChannelCompatConfig.cs`
  - Dependencies: RPG custom prompt store, legacy prompt fields, RimTalk channel config serializer.
  - Responsibility: add `PromptChannel` persistence to entries, migrate legacy entries to normalized channels, and auto-seed missing default channel entries with per-seed default enabled state.
- `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - Dependencies: entry-driven prompt renderer + RimTalk channel config.
  - Responsibility: enforce compat-layer runtime switch and inject only enabled entries matching the active runtime channel/mode.
- `RimChat/Config/RimChatSettings_RimTalkTab.cs`, `RimChat/Config/RimChatSettings_PromptAdvancedFramework.cs`
  - Dependencies: workbench channel cache, entry editor selection/cache state.
  - Responsibility: replace ineffective Role/Position editing path with prompt-channel selection and stabilize editor buffers to prevent entry content rollback/dead-click behavior.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: add localized keys for compatibility-layer runtime hint, prompt-channel labels, and seed entry names.

### Behavior Changes
- `Enable RimTalk compatibility layer` is now a true runtime gate for entry-driven prompt assembly.
- Entry editor routing is now channel-oriented (`PromptChannel`) instead of legacy Role/Position runtime illusions.
- Missing default channel entries are auto-seeded for both diplomacy and RPG channels on migration/load, without breaking old saves.

## Persona Strict Chain + RimTalk Diagnostics Closure (v0.6.17)

### Module Map
- `RimChat/DiplomacySystem/GameComponent_RPGManager.PersonaBootstrap.cs`
  - Dependencies: `PromptTemplateRenderer`, `PromptRenderException`, persona bootstrap defaults/profile builder.
  - Responsibility: switch persona-bootstrap prompt composition from string replacement to strict Scriban render chain; persona-copy render/empty output now throws structured hard-fail.
- `RimChat/Config/RimChatSettings_RimTalkTab.cs`
  - Dependencies: `PromptPersistenceService.ValidateTemplateVariables(...)`, shared live-validation status formatter.
  - Responsibility: add realtime Scriban diagnostics to RimTalk template editors (entry content / channel template / persona-copy template).
- `spec/Index.md`, `Api.md`, `doc/scriban_engine_migration.md`, `VersionLog.txt`, `VersionLog_en.txt`, `About/About.xml`
  - Responsibility: remove legacy bridge/raw-fallback wording, sync strict-runtime docs, and record acceptance evidence.

### Behavior Changes
- Persona bootstrap prompt generation now renders through strict Scriban (`RenderOrThrow`) instead of manual `.Replace(...)` chains.
- Persona copy render failure or empty render result now throws `PromptRenderException` and interrupts current chain (no silent skip).
- RimTalk template editors now show live Scriban diagnostics consistent with Prompt editor behavior.

## Scriban Mainline Breaking Switch (v0.6.16)

### Module Map
- `RimChat/Prompting/ScribanPromptEngine.cs`
  - Dependencies: Scriban parser/runtime, prompt block registry, prompt telemetry.
  - Responsibility: strict parse/render with `RenderOrThrow`, fixed-capacity LRU compile-cache usage, and telemetry logging.
- `RimChat/Prompting/PromptTemplateCache.cs`
  - Dependencies: Scriban `Template`.
  - Responsibility: provide LRU compiled-template cache and cache/render telemetry snapshot model.
- `RimChat/Persistence/PromptPersistenceService.TemplateVariables.cs`
  - Dependencies: `PromptVariableCatalog`, `PromptRenderContext`, strict render entrypoint.
  - Responsibility: validate namespaced variables and render scene templates through Scriban strict engine instead of legacy regex substitution.
- `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - Dependencies: `PromptRenderException`, `PromptTemplateRenderer`, entry-channel prompt configs.
  - Responsibility: enforce strict channel-entry rendering (enabled entries rendering empty now hard-fails), and remove runtime objective text fallback defaults.
- `RimChat/UI/Dialog_DiplomacyDialogue.ImageAction.cs`
  - Dependencies: image prompt template config, strict render entrypoint.
  - Responsibility: enforce strict image caption fallback-template requirement (missing template now throws `TemplateMissing`).
- `RimChat/Config/RimChatSettings_Prompt.cs`
  - Dependencies: prompt validation service, migration diagnostics model, prompt migration result dialog.
  - Responsibility: provide realtime Scriban compile diagnostics in editor preview controls and expose migration-result entrypoint.
- `RimChat/UI/Dialog_PromptMigrationResult.cs`
  - Dependencies: `PromptTemplateAutoRewriteResult` diagnostics model and RimWorld window widgets.
  - Responsibility: render migration success/blocked list with blocked reasons.
- `RimChat/Config/RimChatSettings_RimTalkVariableBrowser.cs`
  - Dependencies: `PromptVariableCatalog`.
  - Responsibility: build local namespaced variable snapshot for settings/workbench variable browser, removing runtime bridge-variable scan dependency.
- `RimChat/UI/Dialog_ApiDebugObservability.cs`
  - Dependencies: `ScribanPromptEngine.GetTelemetrySnapshot()`.
  - Responsibility: show Scriban cache hit-rate and average parse/render latency in observability summary.
- `doc/scriban_engine_migration.md`, `Api.md`, `config.md`, `VersionLog.txt`, `VersionLog_en.txt`, `About/About.xml`
  - Responsibility: sync breaking-contract docs and version metadata.

### Behavior Changes
- Prompt rendering mainline is unified to `IScribanPromptEngine.RenderOrThrow(...)`.
- Environment scene-template rendering no longer uses legacy regex replacement path.
- Prompt render failures are hard failures (`PromptRenderException`), with no silent fallback/raw passthrough.
- Active runtime path no longer depends on `RimTalkCompatBridge` render APIs.
- API debug window now surfaces Scriban cache/latency telemetry.
- Prompt settings editor now exposes realtime Scriban diagnostics and migration result view.
- Image caption local template chain now requires explicit Scriban template text (no built-in default fallback).

## Prompt Workbench Hit-Area Reliability Fix (v0.6.14)

### Module Map
- `RimChat/Config/RimChatSettings_RimTalkTab.cs`
  - Dependencies: RimTalk prompt-entry list/editor rendering and entry mutation handlers.
  - Responsibility: increase entry-list hit areas, restore top duplicate shortcut, and adapt role/position controls for narrow editor widths.
- `RimChat/Config/RimChatSettings_RimTalkVariableBrowser.cs`
  - Dependencies: variable browser insertion handlers and row rendering.
  - Responsibility: add explicit per-row `Insert` buttons in workbench variable panel while preserving row-click insertion.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.6.14` and sync docs.

### Behavior Changes
- Prompt-entry rows can now be selected by clicking the whole row body instead of title-only narrow regions.
- Prompt-entry list header restores a visible duplicate shortcut for faster template iteration.
- Role/Position controls no longer lose clickability on narrow editor widths due horizontal squeezing.
- Workbench variable rows now expose explicit insert buttons in addition to full-row click insertion.
- Compatibility preserved: no save schema changes and legacy prompt files remain readable.

## Prompt Workbench Button Response Fix (v0.6.13)

### Module Map
- `RimChat/Config/RimChatSettings_PromptAdvancedFramework.cs`
  - Dependencies: preset activation service, workbench channel opening flow, localized message keys.
  - Responsibility: add explicit preset action feedback, activation failure handling, and RPG-specific workbench open path.
- `RimChat/Config/RimChatSettings_AI.RpgDialogue.cs`
  - Dependencies: prompt workbench window entry.
  - Responsibility: open Prompt Workbench directly in RPG channel from RPG runtime settings.
- `RimChat/Config/RimChatSettings_RimTalkTab.cs`
  - Dependencies: prompt workbench window entry.
  - Responsibility: open Prompt Workbench directly in RPG channel from RimTalk migration tab.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: add localized success/failure keys for preset action feedback.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.6.13` and sync docs.

### Behavior Changes
- Opening Prompt Workbench from RPG-related entries now enters RPG channel explicitly.
- Preset activation now reports failure reason to the player instead of failing silently.
- Preset create/duplicate/rename/delete/import/export now provide immediate localized feedback.
- Compatibility preserved: no save schema changes and legacy prompt files remain readable.

## Prompt Workbench Interaction Fix + RimTalk Variable UI Port (v0.6.12)

### Module Map
- `RimChat/Config/RimChatSettings_PromptAdvancedFramework.cs`
  - Dependencies: workbench channel selection, RimTalk entry selection, variable browser renderer.
  - Responsibility: route workbench variable side panel to dedicated RimTalk-style renderer and activate preset on list selection.
- `RimChat/Config/RimChatSettings_RimTalkVariableBrowser.cs`
  - Dependencies: RimTalk variable snapshot cache, insertion helpers, Verse widgets.
  - Responsibility: provide RimTalk-style workbench variable UI (search, grouped rows, full-row click insert) with stable hit areas.
- `RimChat/Config/RimChatSettings_RimTalkTab.cs`
  - Dependencies: channel config clone/set path and selected-entry state.
  - Responsibility: align prompt-entry list interactions with RimTalk (inline enable/delete + up/down reorder).
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.6.12` and sync docs.

### Behavior Changes
- Workbench variable panel interaction now matches RimTalk behavior more closely and no longer exhibits dead-click zones from nested listing layout.
- Entry-list interactions are now direct and consistent with RimTalk editing flow.
- Preset row selection now applies immediately to the current editor context.
- Compatibility preserved: no save schema breakage, old prompt files remain readable.

## Prompt Workbench RimTalk Fidelity Alignment (v0.6.11)

### Module Map
- `RimChat/Config/RimChatSettings_PromptAdvancedFramework.cs`
  - Dependencies: `IPromptPresetService`, `RimTalk channel config accessors`, `Verse Widgets`.
  - Responsibility: rebalance Prompt Workbench geometry to RimTalk-like proportions and compact the left preset/entry rail layout.
- `RimChat/Config/RimTalkChannelCompatConfig.cs`
  - Dependencies: `UnityEngine.Mathf`, RimTalk entry config serialization path.
  - Responsibility: persist `CustomRole` as an explicit entry-level field with safe default fallback.
- `RimChat/Config/RimChatSettings_RimTalkTab.cs`
  - Dependencies: RimTalk channel editor rendering and entry mutation helpers.
  - Responsibility: bind `Custom Role` editor input to `CustomRole` field instead of overriding `Role`.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.6.11` and sync behavior/compatibility docs.

### Behavior Changes
- Prompt Workbench now uses a narrower left rail and a wider right editing workspace, closer to RimTalk visual hierarchy.
- Left rail no longer renders generic prompt action stack (`save/reset/import/export`) that was unrelated to RimTalk workbench editing flow.
- `Custom Role` input now has independent storage and no longer corrupts the base `Role` field.
- Compatibility preserved: no breaking save schema migration; legacy prompt files without `CustomRole` continue to load with empty fallback.

## Mod Settings Icon Namespace Isolation (v0.6.10)

### Module Map
- `About/About.xml`
  - Responsibility: switch `modIconPath` from `UI/Logo` to namespaced `UI/RimChat/Logo`.
- `1.6/Textures/UI/RimChat/Logo.png`, `1.6/Textures/UI/Logo.png`
  - Responsibility: provide collision-safe primary logo asset and legacy fallback asset.
- `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: document behavior and compatibility updates.

### Behavior Changes
- Prevents Mod Settings/mod-list icon from resolving to another mod's generic `UI/Logo` asset.
- Keeps old logo path asset file present for backward compatibility.

## Comms Toggle Icon Namespace Isolation (v0.6.9)

### Module Map
- `RimChat/Patches/PlaySettingsPatch_CommsToggleIcon.cs`
  - Dependencies: `Verse.ContentFinder`, `RimWorld.PlaySettings`, `Verse.WidgetRow`.
  - Responsibility: load map toggle icon via unique namespaced path first, with legacy fallback for backward compatibility.
- `1.6/Textures/UI/RimChat/CommsToggleIcon.png`, `1.6/Textures/UI/CommsToggleIcon.png`
  - Responsibility: provide collision-safe primary runtime icon and legacy fallback icon.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.6.9` and sync documentation.

### Behavior Changes
- Prevents the map bottom-right toggle icon from resolving to another mod's same-path texture asset.
- Keeps old icon path valid so old distributions or cached resource layouts remain compatible.

## RimTalk Entry List Interaction Polish (v0.6.8)

### Module Map
- `RimChat/Config/RimChatSettings_RimTalkTab.cs`
  - Responsibility: improve entry-list readability/selection hit area and adapt editor control widths to prevent overlap.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: add missing `RimChat_Import`/`RimChat_Export` keys used by Prompt Workbench header buttons.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.6.8` and sync docs.

### Behavior Changes
- Entry list now shows two-line row information and keeps full text accessible via tooltip.
- Entry editor controls are responsive to available width, reducing overlap and lost click regions.
- Import/Export header buttons no longer fall back to raw key text due missing localization.

## Prompt Workbench Variable Browser UX + Perf Cache (v0.6.7)

### Module Map
- `RimChat/Config/RimChatSettings_RimTalkVariableBrowser.cs`
  - Responsibility: host RimTalk variable browser rendering/caching, including selectable rows, detail panel, and throttled snapshot refresh.
- `RimChat/Config/RimChatSettings_RimTalkTab.cs`
  - Responsibility: keep RimTalk entry editor behavior while delegating variable-browser rendering to dedicated partial module.
- `RimChat/Config/RimChatSettings_PromptAdvancedFramework.cs`
  - Responsibility: continue reusing RimTalk variable browser in Prompt Workbench `Variables` side panel.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.6.7` and sync docs.

### Behavior Changes
- Variable list now supports row selection/highlight and keeps insert action available.
- Added selected-variable detail area to display full token and metadata, reducing truncated-text confusion.
- Variable snapshot and filtered results now use cache/throttle to reduce frame drops when opening Prompt Workbench.
- Save compatibility remains unchanged: no save-schema or prompt-file schema changes.

## Prompt Workbench Variable Insert + Seed Split (v0.6.6)

### Module Map
- `RimChat/Config/RimChatSettings_PromptAdvancedFramework.cs`
  - Responsibility: Prompt Workbench `Variables` panel now reuses RimTalk variable browser rendering.
- `RimChat/Config/RimChatSettings_RimTalkTab.cs`
  - Responsibility: variable insertion now prefers focused-cursor insertion; role/position menu updates are entry-id bound.
- `RimChat/Config/RimChatSettings.cs`, `RimChat/Config/RimChatSettings_PromptEntrySeedImport.cs`
  - Responsibility: legacy combined prompt text is split by section headers during seed import to improve entry completeness.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: version bump to `0.6.6` with synced behavior notes.

### Behavior Changes
- Prompt Workbench variable interactions are now consistent with RimTalk variable UX.
- Variable insertion no longer appears non-functional when editor focus exists; insertion targets the current cursor location.
- Legacy migration path now imports multi-section combined text as multiple entries instead of one oversized block.

## Prompt Workbench Prototype Refresh + Single Tab Entry (v0.6.5)

### Module Map
- `RimChat/Config/RimChatSettings.cs`
  - Responsibility: top settings tabs refactored to `API / ModOptions / Prompt Workbench / Image API`; Prompt Workbench tab now opens standalone window directly.
- `RimChat/Config/RimChatSettings_PromptAdvancedFramework.cs`
  - Responsibility: rebuilt workbench layout to prototype style, limited primary channels to `Diplomacy/RPG`, and routed right-side `Variables` panel to RimTalk variable browser workflow.
- `RimChat/Config/RimChatSettings_RimTalkTab.cs`
  - Responsibility: entry editor role/position menu callbacks are now entry-id bound; variable insertion follows RimTalk cursor-first insertion behavior with append fallback.
- `RimChat/Config/RimChatSettings_PromptEntrySeedImport.cs`
  - Responsibility: split legacy combined prompt text into multiple seed entries by section headers for entry migration completeness.
- `RimChat/Config/RimChatSettings_AI.cs`, `RimChat/Config/RimChatSettings_AI.RpgDialogue.cs`
  - Responsibility: add ModOptions `RPG Runtime Settings` accordion section for non-prompt RPG toggles and scene-tag configuration.
- `RimChat/Config/RimChatSettings_Tooltips.cs`
  - Responsibility: tab tooltip map updated for 4-tab layout and new AI section tooltip mapping.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: add EN/CN keys for new tab labels/tooltips, launcher hint, RPG runtime section, workbench sub-tabs, preview hints, and custom role label.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.6.5` and sync docs.

### Behavior Changes
- Users now access prompt editing through a single top-level `Prompt Workbench` tab button.
- Workbench visible channels are now only `Diplomacy` and `RPG`; RimTalk UI channels are hidden.
- RPG channel now has base-area sub-tabs: `Common Entries` and `Pawn Persona`.
- Workbench variable panel now supports direct RimTalk-style variable browsing and insertion.
- Legacy combined prompt blocks are auto-split into multiple entries during seed migration when no meaningful entries exist.
- Non-prompt RPG runtime settings are now available under ModOptions and no longer require a dedicated top-level RPG tab.
- RimTalk compatibility data model and legacy prompt file compatibility remain intact.

## Prompt Entry Unified Channels (v0.6.4)

### Module Map
- `RimChat/Config/RimChatSettings_PromptAdvancedFramework.cs`
  - Responsibility: route Prompt Workbench `Diplomacy/RPG` channels to the same entry editor flow used by RimTalk channels.
- `RimChat/Config/RimChatSettings.cs`
  - Responsibility: seed entry configs from legacy diplomacy/RPG prompt fields when needed, and backfill legacy fields from entries on save/export.
- `RimChat/Config/RimChatSettings_Prompt.cs`
  - Responsibility: save pipeline now includes entry->legacy backfill and RPG custom prompt persistence in one action.
- `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - Responsibility: diplomacy/RPG runtime prompt assembly now uses enabled entry concatenation only, with strict Scriban `RenderOrThrow` and no legacy fallback seeding.
- `RimChat/Compat/RimTalkCompatBridge.Models.cs`
  - Responsibility: keep legacy data-contract model types only; runtime bridge implementation files are removed.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.6.4` and sync behavior/config/api docs.

### Behavior Changes
- Prompt Workbench no longer uses the legacy middle section-editor for `Diplomacy/RPG`; both channels are now entry-driven.
- Runtime prompt generation for diplomacy/RPG concatenates only enabled entries in order.
- Entry content rendering now uses internal strict Scriban render path.
- Save/export now auto-backfills legacy prompt fields to preserve old save/prompt-file readability.

## RimTalk Variable Entry Editor (v0.6.3)

### Module Map
- `RimChat/Config/RimTalkChannelCompatConfig.cs`
  - Responsibility: introduce `PromptEntries` entry-based compatibility payload and auto-compose entries back into legacy `CompatTemplate`.
- `RimChat/Config/RimChatSettings_RimTalkTab.cs`
  - Responsibility: upgrade RimTalk sub-channel editor into entry-list + entry-detail workflow (name/role/position/depth/content) with variable insertion targeting selected entry.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: add EN/CN keys for entry-list labels, empty state, default entry naming, role and position labels.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.6.3` and sync docs.

### Behavior Changes
- RimTalk channel editing now uses variable entry workflow with list operations (add/duplicate/delete/reorder).
- Entry details support role, position, and in-chat depth similar to RimTalk advanced editor behavior.
- Variable insertion now appends into selected entry content first.
- Legacy compatibility is preserved through automatic `CompatTemplate` synchronization.

## Prompt Workbench + Preset Migration Framework (v0.6.2)

### Module Map
- `RimChat/Config/PromptPresets/IPromptPresetService.cs`, `RimChat/Config/PromptPresets/PromptPresetModels.cs`, `RimChat/Config/PromptPresets/PromptPresetService.cs`
  - Responsibility: define/store/load/activate prompt preset contracts with legacy migration and import/export support.
- `RimChat/Config/RimChatSettings_PromptAdvancedFramework.cs`, `RimChat/Config/RimChatSettings_Prompt.cs`
  - Responsibility: host Prompt advanced workbench (channel navigation + preset panel) and keep existing editors wired under unified entry.
- `RimChat/Config/RimChatSettings_RimTalkTab.cs`
  - Responsibility: keep old RimTalk tab as migration redirect entry to Prompt workbench.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: add EN/CN localized keys for workbench channels, preset actions, and RimTalk tab migration hint.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.6.2` and sync docs.

### Behavior Changes
- Added Prompt advanced workbench channel navigation (`Diplomacy`, `RPG`, `RimTalk-Diplomacy`, `RimTalk-RPG`).
- Added preset management with create/duplicate/activate/delete/rename/import/export.
- Added auto migration path: when no preset file exists, create default preset from existing legacy custom prompt files.
- Preset activation now writes legacy `Prompt/Custom/*` payloads and refreshes settings runtime state.
- RimTalk standalone tab now serves as a transition entry point to the Prompt workbench.

## RimTalk Strict Isolation Switches (v0.6.1)

### Module Map
- `RimChat/Compat/RimTalkCompatBridge.cs`
  - Responsibility: gate cross-mod summary-global write path and compat preset ensure path behind explicit isolation switches.
- `RimChat/Compat/RimTalkCompatBridge.PromptEntries.cs`
  - Responsibility: disable existing `RimChat Compat Variables` preset entry when auto preset sync is OFF.
- `RimChat/Config/RimChatSettings_RimTalkCompat.cs`, `RimChat/Config/RimChatSettings.cs`, `RimChat/Config/RimChatSettings_RimTalkTab.cs`
  - Responsibility: define/save/load/draw RimTalk isolation switches in settings.
- `RimChat/Config/RpgPromptCustomStore.cs`, `RimChat/Config/RpgPromptDefaultsConfig.cs`
  - Responsibility: persist new isolation switches with old-file-safe fallback to OFF.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: add EN/CN localized keys for isolation switches and hint text.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.6.1` and sync release/config/api notes.

### Behavior Changes
- Added two explicit isolation switches:
  - `Auto push RimChat session summaries into RimTalk global variables` (default OFF)
  - `Auto create/update RimTalk Compat Variables preset entry` (default OFF)
- RimTalk bridge no longer auto writes summary globals when summary push switch is OFF.
- RimTalk bridge no longer auto injects/updates compat preset entry when preset sync switch is OFF.
- Existing compat preset entry is force-disabled when preset sync switch is OFF.
- Compatibility baseline preserved: old saves and legacy prompt files remain readable.

## Comms Dialogue Hidden Faction Gear Multi-Select (v0.5.29)

### Module Map
- `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - Responsibility: add a factions-header gear entry and merge manually-selected hidden factions into the comms diplomacy faction list without changing original list sorting/switch flow.
- `RimChat/UI/Dialog_HiddenFactionVisibilitySelector.cs`
  - Responsibility: provide a lightweight multi-select popup for hidden factions with `Select All / Clear / Confirm / Cancel`.
- `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.HiddenFactionVisibility.cs`, `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.cs`
  - Responsibility: store save-scoped hidden-faction visibility overrides and serialize/deserialize them with old-save-safe fallback.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: add EN/CN localized keys for gear tooltip and hidden-faction selector dialog UI.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.5.29` and sync release/config/api notes.

### Behavior Changes
- Added a gear button next to the factions title in comms diplomacy dialogue.
- Added a hidden-faction visibility selector popup that supports multi-select and batch operations.
- Selected hidden factions are displayed in the same comms diplomacy list and use the same diplomacy dialogue capabilities as normal factions.
- Selection is now persisted per save (`manuallyVisibleHiddenFactions`) and remains old-save compatible when the field is missing.

## Image API 3-Mode Convergence + ComfyUI Async Flow (v0.5.22)

### Module Map
- `RimChat/Config/DiplomacyImageApiConfig.cs`
  - Responsibility: add persistent image-provider compatibility fields (`Mode/SchemaPreset/AuthMode/ResponsePaths/AsyncPaths/Polling`) with old-save-safe defaults and normalization.
- `RimChat/DiplomacySystem/DiplomacyImageGenerationService.cs`
  - Responsibility: execute 3-mode image generation pipeline (`sync_url`, `sync_payload`, `async_job`), support URL/Base64 extraction, auth-mode routing, and ComfyUI submit/poll/fetch chain.
- `RimChat/UI/Dialog_DiplomacyDialogue.ImageAction.cs`
  - Responsibility: pass image compatibility fields from settings into runtime image-generation request.
- `RimChat/Config/RimChatSettings_ImageApi.cs`
  - Responsibility: expose minimal mode/preset/auth/response/async controls in Image API settings tab.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: add EN/CN localized keys for new image compatibility controls.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.5.22` and sync behavior/config documentation.

### Behavior Changes
- Added a minimal image-provider compatibility model with 3 execution modes instead of per-provider hard forks.
- Added auth-mode abstraction (`bearer/api_key_header/query_key/none`) and custom key-name controls.
- Added response payload fallback parser that supports both URL and Base64 outputs.
- Added ComfyUI-compatible async job flow with default route templates and polling bounds.
- Aligned Image API tab controls with the API tab UX pattern (selector-first rows, placeholders, and status-colored feedback).
- Added an Image API connectivity test button with sync/async probe paths and auth failure reporting.
- Added provider-preset-first image UX (`Volcengine ARK/OpenAI Compatible/SiliconFlow/ComfyUI Local/Custom`) to reduce required user settings.
- Advanced compatibility fields are now hidden behind `Custom` preset toggle for better default usability.
- Maintained compatibility baseline: unchanged `send_image` contract, unchanged prompt-file schema, and old-save-safe field defaults.

## RPG Session History Panel + Action Timeline (v0.5.21)

### Module Map
- `RimChat/UI/Dialog_RPGPawnDialogue.cs`, `RimChat/UI/Dialog_RPGPawnDialogue.TextPaging.cs`, `RimChat/UI/Dialog_RPGPawnDialogue.Actions.cs`, `RimChat/UI/Dialog_RPGPawnDialogue.HistoryPanel.cs`
  - Responsibility: add a left-bottom `History` entry, render a centered session history panel, decouple panel-close click handling from full-window close behavior, and record/display per-turn action outcomes (`success/failure/error + reason`) under the related NPC reply.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: add EN/CN localized keys for RPG history panel labels, action-result labels, and reason formatting.
- `About/About.xml`, `Api.md`, `VersionLog.txt`, `VersionLog_en.txt`
  - Responsibility: bump version to `0.5.21` and synchronize API/change documentation.

### Behavior Changes
- Added a left-bottom `History` text button in manual RPG dialogue window.
- Added a centered session history panel (current session only, chronological order) that can be opened at any time.
- Clicking outside the history panel now closes only the panel, without closing the RPG dialogue window.
- History details now include triggered actions under the corresponding NPC turn with action name, outcome, and reason when available.
- Existing right-bottom quick history pagination remains unchanged.

## Input-Lock Placeholder Removal + Send-Image Caption Fallback (v0.5.20)

### Module Map
- `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - Responsibility: keep diplomacy input lock behavior but hide lock-preview placeholder text while preserving bottom typing status rendering.
- `RimChat/UI/Dialog_DiplomacyDialogue.ImageAction.cs`
  - Responsibility: apply send-image caption policy `AI caption first -> local template fallback -> localized default caption`, and stop using template name as caption fallback.
- `RimChat/Config/RimChatSettings.cs`, `RimChat/Config/RimChatSettings_ImageApi.cs`, `RimChat/Config/PromptTextConstants.cs`
  - Responsibility: add persistent image-caption style/fallback settings with old-save-safe defaults and expose them in Image API settings UI.
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Responsibility: append explicit send-image caption guidance (caption expected, style prompt source, and current-game-language requirement) into diplomacy prompt contract.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: add EN/CN localized keys for caption style/fallback settings labels and hints.

### Behavior Changes
- Locked diplomacy input preview no longer shows localized waiting text in the text area.
- send-image caption no longer falls back to template name when AI omits caption.
- Local fallback template now supports `{leader}`, `{faction}`, and `{template_name}` placeholders.
- Prompt contract now tells the model to provide `parameters.caption`, follow configured caption style, and match current game language.
- Save compatibility remains unchanged: new settings fields are optional with default fallback on old saves.

## Diplomacy Album Thumbnail Grid + Selfie Injection Switches (v0.5.19)

### Module Map
- `RimChat/UI/Dialog_DiplomacyAlbum.cs`
  - Responsibility: render save-scoped album as thumbnail card grid, show source/size badges, and provide item context actions (`Open dir`, `Copy path`) with lightweight texture cache cleanup.
- `RimChat/UI/Dialog_DiplomacyDialogue.ImageRendering.cs`
  - Responsibility: compute inline image visible rect (aspect-fit) and trigger right-click album menu through dual event fallback (`ContextClick` + right `MouseDown`) on image-visible area only.
- `RimChat/UI/Dialog_DiplomacySelfieConfig.cs`, `RimChat/DiplomacySystem/SelfiePromptInjectionBuilder.cs`
  - Responsibility: expose selfie injection toggles in-window and build hidden prompt-append profile blocks from negotiator runtime data (apparel/body/hair/weapon/implants/status).
- `RimChat/DiplomacySystem/AlbumImageEntry.cs`, `RimChat/DiplomacySystem/DiplomacyAlbumService.cs`, `RimChat/UI/Dialog_DiplomacyDialogue.AlbumSelfieActions.cs`
  - Responsibility: add backward-compatible `sourceType` metadata persistence and assign `chat/selfie` source tags during manual album-save paths.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: add localized keys for album badges/path-copy actions and selfie injection toggle labels.

### Behavior Changes
- Album view is now a thumbnail grid instead of plain rows.
- Inline image save menu reliably appears on right-click inside the true visible image area only.
- Selfie generation now supports per-category hidden profile injection toggles with full-detail extraction defaults.
- Save compatibility remains intact: missing `sourceType` in old saves gracefully falls back to `unknown`.

## Diplomacy Album + Selfie Workflow (v0.5.18)

### Module Map
- `RimChat/DiplomacySystem/AlbumImageEntry.cs`, `RimChat/DiplomacySystem/DiplomacyAlbumService.cs`, `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.Album.cs`, `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.cs`
  - Responsibility: define persistent album index entries, file-copy save/open-directory services, and save-compatible album index management (`AddAlbumEntry/GetAlbumEntries/PruneMissingAlbumFiles`) in diplomacy game component.
- `RimChat/UI/Dialog_DiplomacyDialogue.SocialCircleView.cs`, `RimChat/UI/Dialog_DiplomacyDialogue.ImageRendering.cs`, `RimChat/UI/Dialog_DiplomacyDialogue.AlbumSelfieActions.cs`
  - Responsibility: add top-row `Album/Selfie` entry buttons and chat-image right-click `Save to Album` action with minimal-invasive hook points.
- `RimChat/UI/Dialog_DiplomacyAlbum.cs`, `RimChat/UI/Dialog_DiplomacySelfieConfig.cs`, `RimChat/UI/Dialog_DiplomacySelfiePreview.cs`
  - Responsibility: implement manual album browsing, directory-opening context menu, selfie parameter input, and post-generation preview/save workflow.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: add EN/CN localized keys for album/selfie UI and status feedback.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.5.18` and sync behavior/documentation notes.

### Behavior Changes
- Added `Album` and `Selfie` buttons to diplomacy top tabs row.
- Album now only includes images manually saved by the player (chat-image right click or selfie preview save), not all generated images.
- Album entries are persisted per save and remain old-save compatible when the new field is missing.
- Selfie generation is isolated from existing `send_image` action chain and opens a preview dialog before user-decided album save.

## Manual RPG Kinship/Romance Relationship Profile Injection (v0.5.17)

### Module Map
- `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - Responsibility: add manual-RPG-only `relationship_profile` prompt node and render pair-level kinship/romance summary + conservative boundary guidance.
- `RimChat/Config/RpgPromptDefaultsConfig.cs`, `RimChat/Config/RpgPromptCustomStore.cs`, `Prompt/Default/PawnDialoguePrompt_Default.json`
  - Responsibility: add `RelationshipProfileTemplate` and `KinshipBoundaryRuleTemplate` to default/custom prompt text schema with fallback-safe load/merge behavior.
- `RimChat/Config/RimChatSettings.cs`
  - Responsibility: keep new RPG prompt fields preserved when writing custom prompt payloads from settings save flow.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.5.17` and synchronize release/documentation notes.

### Behavior Changes
- Manual RPG prompt assembly now injects a dedicated `relationship_profile` block.
- Pair relationship summary includes:
  - `Kinship: yes/no` (blood relation existence only)
  - `RomanceState: spouse/fiance/lover/ex-or-none/none`
  - `Guidance` rendered from conservative kinship-boundary template.
- Proactive PawnRPG generation is unchanged (no `relationship_profile` injection there).
- No save schema changes; old custom prompt files without new fields remain readable via default fallback.

## Faction Prompt Template Add/Remove + Mod Faction Binding (v0.5.16)

### Module Map
- `RimChat/Config/FactionPromptManager.cs`
  - Responsibility: build a default-template catalog from `Prompt/Default/FactionPrompts_Default.json`, enforce default-template protection, and expose `TryAddTemplateForFaction(...)` / `TryRemoveTemplate(...)` / `IsDefaultTemplate(...)` / `IsFactionMissing(...)`.
- `RimChat/Config/RimChatSettings_Prompt.cs`
  - Responsibility: extend Prompt -> Faction Prompts UI with add/remove actions, all-FactionDef picker (including mod defs), duplicate-select behavior, and default/missing status tags.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: add localized keys for new faction-template lifecycle UI and status messaging.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.5.16` and synchronize behavior docs.

### Behavior Changes
- Faction template add now supports selecting from all loaded `FactionDef` entries (including mod factions).
- Each `FactionDefName` is unique in faction prompt templates; existing entries are selected instead of duplicated.
- Only non-default templates are removable; default templates are always protected.
- Missing-mod faction templates remain persisted and are tagged as missing for compatibility visibility.
- No save schema changes and no prompt-file schema changes; legacy prompt files remain readable.

## Diplomacy Image Template Usability + Input Gate Unification (v0.5.15)

### Module Map
- `RimChat/Config/RimChatSettings_ImageApi.cs`
  - Responsibility: render diplomacy image API tab as a full-page scroll view so template editor controls remain reachable on low-height settings windows.
- `RimChat/Memory/FactionDialogueSession.cs`
  - Responsibility: add runtime-only `pendingImageRequests` state and helpers (`BeginImageRequest/EndImageRequest/HasPendingImageRequests`) without save-schema changes.
- `RimChat/UI/Dialog_DiplomacyDialogue.ImageAction.cs`
  - Responsibility: track image-generation lifecycle by incrementing/decrementing pending-image runtime state around async `GenerateImage(...)` callbacks.
- `RimChat/UI/Dialog_DiplomacyDialogue.Presence.cs`, `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - Responsibility: unify diplomacy input/send gating with image-request wait state and enforce ended-conversation status priority over typing indicator in input-area rendering.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.5.15` and sync release/documentation notes.

### Behavior Changes
- Image API settings tab now supports full-page vertical scrolling and no longer truncates template editing controls on small resolutions.
- `send_image` now locks diplomacy input while image generation is pending, and releases lock on callback for both success and failure.
- Ended-conversation status now has higher UI priority than typing indicator; typing is hidden once session is ended.
- Late image callbacks still append image/system messages to history even when conversation is already ended.
- No save-schema changes and no prompt-file format changes; old saves and existing prompt files remain compatible.

## Diplomacy Image Size Threshold Alignment (v0.5.14)

### Module Map
- `RimChat/Config/DiplomacyImageApiConfig.cs`
  - Responsibility: align image size validation threshold with provider lower bound (`>= 3,686,400` pixels), update default/fallback size and size alias mappings, and keep old-save-safe normalization behavior.
- `RimChat/DiplomacySystem/DiplomacyImageGenerationService.cs`
  - Responsibility: centralize send-image request size fallback to `DiplomacyImageApiConfig.DefaultImageSize` in both request body build and request normalization path.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.5.14` and sync release/documentation notes.

### Behavior Changes
- `send_image` size validation now follows the provider's current lower bound (`>= 3,686,400` pixels).
- Default size and fallback size are unified to `2560x1440`; low legacy values (for example `1024x1024`) are normalized automatically at runtime.
- No save schema changes and no prompt-file format changes; old saves and existing prompt files remain compatible.

## Diplomacy Input Lock Draft Clearing (v0.5.13)

### Module Map
- RimChat/UI/Dialog_DiplomacyDialogue.cs
  - Responsibility: in DrawInputArea(...), clear player draft text when input is locked by AI typing state; in DrawLockedInputPreview(...), always render localized lock hint instead of draft content.
- About/About.xml, VersionLog.txt, VersionLog_en.txt
  - Responsibility: bump version to 0.5.13 and sync release notes.

### Behavior Changes
- During diplomacy AI-turn lock (session waiting or active NPC typewriter), the input draft is now cleared immediately.
- Locked input preview no longer displays in-progress player draft text, and always shows RimChat_DiplomacyInputLockedByTyping.
- No save schema changes and no prompt-file format changes; old saves and existing prompt files remain compatible.
## Diplomacy Image Framework (v0.5.11)

### Module Map
- `RimChat/AI/AIActionNames.cs`, `RimChat/AI/AIResponseParser.cs`, `RimChat/DiplomacySystem/ApiActionEligibilityService.cs`
  - Responsibility: register `send_image` in action constants, parser whitelist/alias normalization, and eligibility checks (including required `template_id` and enabled-template validation).
- `RimChat/Config/DiplomacyImageApiConfig.cs`, `RimChat/Config/PromptTextConstants.cs`
  - Responsibility: add standalone diplomacy image API config schema and global image template model/defaults (`id/name/text/description/enabled`) with migration-safe default seeding.
- `RimChat/Config/RimChatSettings.cs`, `RimChat/Config/RimChatSettings_ImageApi.cs`, `RimChat/Config/RimChatSettings_Tooltips.cs`
  - Responsibility: add independent Image API tab and editor UI (endpoint/key/model/default size/watermark/timeout + template CRUD), and persist all fields with old-save compatibility.
- `RimChat/DiplomacySystem/DiplomacyImagePromptBuilder.cs`, `RimChat/DiplomacySystem/DiplomacyImageGenerationService.cs`
  - Responsibility: build final prompt (`template + extra_prompt + LeaderProfile`), call ARK REST image endpoint, parse URL response, download image bytes, and persist per-save image cache files.
- `RimChat/Memory/FactionDialogueSession.cs`, `RimChat/UI/Dialog_DiplomacyDialogue.ImageAction.cs`, `RimChat/UI/Dialog_DiplomacyDialogue.ImageRendering.cs`, `RimChat/UI/Dialog_DiplomacyDialogue.cs`, `RimChat/UI/Dialog_DiplomacyDialogue.ActionHint.cs`
  - Responsibility: add inline image message persistence/rendering (`DialogueMessageType.Image`), intercept `TryHandleSendImageAction(...)` in diplomacy execution chain, and expose send-image status in action-hint tooltip list.
- `Prompt/Default/DiplomacyDialoguePrompt_Default.json`, `RimChat/Config/SystemPromptConfig.cs`, `RimChat/Persistence/PromptPersistenceService.cs`
  - Responsibility: add default `send_image` action contract into prompt domains, migration defaults, and compact action catalog hints.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: add EN/CN keys for image tab labels, template editor labels, action labels, queued/failure feedback, and image-preview fallback text.
- `RimChat/RimChat.csproj`
  - Responsibility: add `UnityEngine.ImageConversionModule` reference required for runtime PNG/JPG decode into `Texture2D`.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.5.11` and sync release/documentation notes.

### Behavior Changes
- Added new diplomacy action `send_image` (one image max per turn) handled at the same interception layer as presence/social actions.
- ARK request contract is fixed: `model`, `prompt`, `sequential_image_generation="disabled"`, `response_format="url"`, `stream=false`, plus `size/watermark` (default from settings, optional action override).
- Prompt construction now always injects a structured leader profile block (identity + appearance + faction context), with faction-level fallback when leader data is missing.
- Successful URL generation is downloaded and shown as inline chat image card; failures keep text reply and append a localized system failure message.
## RimTalk Persona Auto Copy (v0.5.10)

### Module Map
- `RimChat/Compat/RimTalkCompatBridge.cs`
  - Responsibility: add pawn persona template render API (`TryRenderPawnPersonaCopyTemplate`) with token normalization support (`pawn.personality` / `{{pawn.personality}}`).
- `RimChat/DiplomacySystem/GameComponent_RPGManager.PersonaBootstrap.cs`
  - Responsibility: attempt RimTalk persona copy before AI persona bootstrap generation, including bootstrap-queue preflight and runtime-scan preflight.
- `RimChat/Config/RimChatSettings_RimTalkCompat.cs`
  - Responsibility: add persisted setting `RimTalkPersonaCopyTemplate` with default and clamping helpers.
- `RimChat/Config/RimChatSettings.cs`, `RimChat/Config/RpgPromptDefaultsConfig.cs`, `RimChat/Config/RpgPromptCustomStore.cs`, `Prompt/Default/PawnDialoguePrompt_Default.json`
  - Responsibility: wire `RimTalkPersonaCopyTemplate` through default/custom RPG prompt config load/save and migration-safe fallback.
- `RimChat/Config/RimChatSettings_RimTalkTab.cs`
  - Responsibility: expose RPG channel persona-copy template editor in RimTalk settings UI, and provide a manual one-click sync button for all colony pawns.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: add EN/CN keys for persona-copy template label/hint/failure descriptions and manual sync button/summary feedback.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.5.10` and sync release/documentation notes.

### Behavior Changes
- RPG persona bootstrap now tries RimTalk template-based persona copy first.
- Copy is restricted to colony humanlike pawns with empty RimChat persona prompts (no overwrite of existing custom persona text).
- Persona-copy template defaults to `pawn.personality`, and also accepts `{{pawn.personality}}`.
- When RimTalk render is unavailable or returns empty text, flow silently falls back to the existing AI generation/retry/fallback chain.
- Added a manual action in RimTalk settings (RPG channel): one-click sync all colony pawns from RimTalk persona into RimChat persona storage.

## Proactive Toggle Split (v0.5.8)

### Module Map
- `RimChat/Config/RimChatSettings_NpcPush.cs`
  - Responsibility: add independent settings field `EnablePawnRpgInitiatedDialogue`, split proactive settings UI into diplomacy and PawnRPG toggles, and reset defaults to enabled for both.
- `RimChat/Config/RimChatSettings_AI.cs`
  - Responsibility: persist `EnablePawnRpgInitiatedDialogue` and add old-save migration fallback (inherit from legacy `EnableNpcInitiatedDialogue` when the new node is missing).
- `RimChat/PawnRpgPush/GameComponent_PawnRpgDialoguePushManager.cs`
  - Responsibility: gate PawnRPG proactive runtime using `EnablePawnRpgInitiatedDialogue && EnableRPGDialogue`.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: add localized labels for split proactive toggles.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump to `0.5.8` and sync docs/logs.

### Behavior Changes
- Diplomacy proactive dialogue and PawnRPG proactive dialogue can now be toggled independently.
- Default for both toggles is enabled.
- Old saves without the new PawnRPG toggle field keep legacy behavior by inheriting the old shared toggle value at load time.

## API Debug Observability Window (v0.5.7)

### Module Map
- `RimChat/AI/AIRequestDebugModels.cs`
  - Responsibility: define debug source/status enums and read models (`AIRequestDebugRecord/Summary/Bucket/Snapshot`).
- `RimChat/AI/AIChatServiceAsync.DebugTelemetry.cs`
  - Responsibility: implement in-memory telemetry collection, 60-minute snapshot query, 5-minute bucket aggregation, and 2000-entry ring cleanup.
- `RimChat/AI/AIChatServiceAsync.cs`
  - Responsibility: extend `SendChatRequestAsync(...)` with optional `debugSource`, and collect request lifecycle telemetry without changing existing dialogue-token semantics.
- `RimChat/UI/Dialog_ApiDebugObservability.cs`
  - Responsibility: render observability window (summary cards + trend chart + detail table + full payload panel + JSON copy actions) with 2-second auto refresh.
- `RimChat/Config/RimChatSettings.cs`
  - Responsibility: add right-side `Token/Full Log` button in `API Settings -> Debug Settings` header to open observability window.
- `RimChat/DiplomacySystem/DiplomacyConversationController.cs`, `RimChat/UI/Dialog_RPGPawnDialogue.cs`, `RimChat/UI/Dialog_DiplomacyDialogue.Strategy.cs`, `RimChat/NpcDialogue/GameComponent_NpcDialoguePushManager.cs`, `RimChat/PawnRpgPush/GameComponent_PawnRpgDialoguePushManager.Generation.cs`, `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.SocialCircle.NewsRequests.cs`, `RimChat/DiplomacySystem/GameComponent_RPGManager.PersonaBootstrap.cs`, `RimChat/Memory/DialogueSummaryService.cs`, `RimChat/Memory/RpgNpcDialogueArchiveManager.Sessions.cs`
  - Responsibility: tag primary `SendChatRequestAsync(...)` call sites with detailed `debugSource`.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: add EN/CN keys for button text, window labels, source/status labels, empty states, and copy feedback.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.5.7` and sync docs/logs.

### Behavior Changes
- API debug telemetry now covers all AI requests with source-level categorization.
- Logs are memory-only (no save writes), with retention hard limits: max 2000 records and automatic cleanup older than 65 minutes.
- Observability UI always shows the latest real-time 60-minute window and refreshes every 2 seconds.
- Diplomacy/RPG rows are displayed with normal contrast; non-priority background sources are de-emphasized in gray.

## PawnRPG Manual Protagonist Targeting (v0.5.6)

### Module Map
- `RimChat/PawnRpgPush/PawnRpgPushModels.cs`
  - Responsibility: add `PawnRpgProtagonistEntry` persistence model for per-save protagonist list (`Pawn` reference + `pawnThingId` fallback).
- `RimChat/PawnRpgPush/GameComponent_PawnRpgDialoguePushManager.cs`, `RimChat/PawnRpgPush/GameComponent_PawnRpgDialoguePushManager.Candidates.cs`
  - Responsibility: expose protagonist-list APIs, enforce protagonist-only target candidate pool for PawnRPG proactive flow, and block proactive generation when list is empty.
- `RimChat/Config/RimChatSettings_NpcPush.cs`, `RimChat/Config/RimChatSettings_AI.cs`
  - Responsibility: add protagonist-cap setting (`PawnRpgProtagonistCap`, default `20`) and NPC proactive settings UI for add/remove/clear protagonist list management.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: add localized UI labels/messages for protagonist-list management.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.5.6` and sync docs/logs.

### Behavior Changes
- PawnRPG proactive target selection now only considers manually configured protagonists.
- Existing relation/opinion scoring logic is kept unchanged, but applied only inside protagonist candidates.
- Empty protagonist list now strictly disables PawnRPG proactive delivery (including debug force-trigger), with warning logs for diagnosis.
- Protagonist list is per-save and backward-compatible with old saves (missing fields initialize safely).

## Raid Execution Reliability Hardening (v0.5.5)

### Module Map
- `RimChat/DiplomacySystem/DiplomacyEventManager.cs`
  - Responsibility: force-run vanilla auto strategy/arrival fallback when raid strategy validation or primary execution fails, so raid events do not fail only because custom strategy resolution is unavailable.
- `RimChat/DiplomacySystem/DelayedDiplomacyEvent.cs`
  - Responsibility: persist retry state (`retryCount/maxRetryCount/nextRetryTick`) and def-name mirrors (`raidStrategyDefName/arrivalModeDefName`) for cross-save compatibility and resilient delayed raid execution.
- `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.cs`
  - Responsibility: process delayed events with success-first removal and bounded retry scheduling, preventing one-shot execution failures from permanently dropping events.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.5.5` and sync behavior/documentation notes.

### Behavior Changes
- Delayed diplomacy events are no longer removed before execution.
- Failed delayed events now retry up to 3 times with short delay windows.
- `request_raid` now performs a forced vanilla auto strategy/arrival fallback pass before final failure.
- Delayed raid strategy/arrival data can recover from missing def references using stored def names.

## Prompt Bundle Selective Transfer + RimTalk Channel Split (v0.5.4)

### Module Map
- `RimChat/Persistence/PromptBundleTransferModels.cs`
  - Responsibility: define selective import/export module identifiers and import-preview DTOs.
- `RimChat/Persistence/PromptDomainPayloads.cs`
  - Responsibility: bump bundle payload to `v2`, add `IncludedModules`, and add dedicated RimTalk diplomacy/RPG channel payload fields.
- `RimChat/Persistence/PromptPersistenceService.cs`, `RimChat/Persistence/PromptPersistenceService.DomainStorage.cs`
  - Responsibility: support selective bundle export/import, `v1` backward-compatible parsing, module preview summaries, and module-scoped apply (unselected modules remain unchanged).
- `RimChat/UI/Dialog_PromptBundleExport.cs`, `RimChat/UI/Dialog_PromptBundleImportPreview.cs`, `RimChat/UI/Dialog_LoadFile.cs`, `RimChat/UI/Dialog_SaveFile.cs`
  - Responsibility: provide full/partial export mode UI, import preview with module checkboxes, and localized path validation feedback.
- `RimChat/Config/RimTalkChannelCompatConfig.cs`, `RimChat/Config/RimChatSettings_RimTalkCompat.cs`, `RimChat/Config/RimChatSettings_RimTalkTab.cs`, `RimChat/Config/RimChatSettings.cs`, `RimChat/Config/RimChatSettings_RPG.cs`
  - Responsibility: split RimTalk compatibility settings by channel (diplomacy/rpg), migrate legacy single-channel values into both channels once, and expose a dedicated top-level RimTalk settings tab.
- `RimChat/Compat/RimTalkCompatBridge.cs`, `RimChat/Compat/RimTalkCompatBridge.Reflection.cs`, `RimChat/Compat/RimTalkCompatBridge.Models.cs`
  - Responsibility: channel-aware compatibility gating, channel-specific injection limits, runtime status diagnostics, and broader reflection signature matching for context variable registration.
- `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - Responsibility: read channel-specific RimTalk enable/template settings during diplomacy/RPG prompt assembly.
- `RimChat/Config/RpgPromptDefaultsConfig.cs`, `RimChat/Config/RpgPromptCustomStore.cs`, `Prompt/Default/PawnDialoguePrompt_Default.json`
  - Responsibility: persist/normalize RimTalk channel-split fields in default/custom RPG prompt payload chain while preserving legacy fields for backward compatibility.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: add localization keys for new RimTalk tab, selective bundle dialogs, module labels, and summary lines.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.5.4` and sync docs/logs.

### Behavior Changes
- Prompt bundle export now supports:
  - full export (all modules)
  - selective export (checked modules only, with `IncludedModules` in `v2` bundle)
- Prompt bundle import now supports:
  - file preview with module list + summary
  - module-selective apply (`explicit override` for selected modules; unselected modules preserved)
  - backward-compatible `v1` file import mapped to all modules
  - service-level guard for empty path / empty file / non-overlapping module selection
- RimTalk compatibility settings are now channel-specific:
  - diplomacy channel config
  - RPG channel config
  - shared summary-history limit remains global
- RimTalk UI moved to a dedicated top-level tab with channel switch, grouped variable search, and one-click token insertion.
- RimTalk prompt injection reads channel-specific enable/template/limit settings during prompt assembly.
- RimTalk reflection registration now prefers method default values for extra parameters, improving cross-version signature tolerance.

## Raid Point Baseline + Tuning Overrides (v0.5.3)

### Module Map
- `RimChat/DiplomacySystem/DiplomacyEventManager.cs`
  - Responsibility: compute raid points via vanilla `RaidEnemy` baseline and apply global/per-faction tuning (`multiplier + min points`) before incident execution.
- `RimChat/Config/RaidPointsFactionOverride.cs`
  - Responsibility: persist per-faction raid tuning entries (`FactionDefName`, `RaidPointsMultiplier`, `MinRaidPoints`).
- `RimChat/Config/RimChatSettings.cs`, `RimChat/Config/RimChatSettings_AI.cs`
  - Responsibility: expose/save global raid tuning settings and provide raid settings UI for per-faction overrides.
- `Api.md`, `config.md`, `VersionLog.txt`, `VersionLog_en.txt`
  - Responsibility: synchronize API/config docs and release logs for raid-size stability tuning.
- `About/About.xml`
  - Responsibility: bump mod version to `0.5.3` for this raid-size alignment patch.

### Behavior Changes
- Auto raid points for `request_raid` no longer use `0.5x DefaultThreatPointsNow`.
- Default path now follows vanilla raid parms baseline, which improves expected attacker count consistency.
- New global raid tuning (`RaidPointsMultiplier`, `MinRaidPoints`) and faction-specific overrides (`RaidPointsFactionOverrides`) can increase minimum raid scale without patching defs.

## XML Def Match Rollback + Runtime Injector Authority (v0.5.2)

### Module Map
- `1.6/Patches/CompPawnDialogue_Patch.xml`
  - Responsibility: rollback XML injection scope to conservative `ThingDef[defName="Human"]` only, avoiding false matches on non-ThingDef defs such as `PawnKindDef`.
- `RimChat/Comp/PawnDialogueCompDefInjector.cs`
  - Responsibility: remain the authoritative path for broad pawn-race comp backfill (HAR/custom races) after defs are loaded.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `spec/Index.md`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.5.2` and sync notes.

### Behavior Changes
- Eliminates startup XML errors caused by wildcard XML patch writing `<comps>` into `PawnKindDef` nodes.
- Keeps HAR race dialogue availability through runtime comp backfill instead of broad XML wildcard matching.

## HAR Race Def Injection Hardening (v0.5.1)

### Module Map
- `1.6/Patches/CompPawnDialogue_Patch.xml`
  - Dependencies: RimWorld PatchOperation runtime.
  - Responsibility: broaden XPath target from `ThingDef` to wildcard def node names, so custom pawn def tags (for example HAR `AlienRace.ThingDef_AlienRace`) are included.
- `RimChat/Comp/PawnDialogueCompDefInjector.cs`
  - Dependencies: DefDatabase resolved ThingDef graph, `CompPawnDialogue` types.
  - Responsibility: backfill `CompPawnDialogue` at runtime after defs are fully loaded, preventing missed injection due to XML inheritance/tag-shape differences.
- `RimChat/Core/RimChatMod.cs`
  - Responsibility: schedule the runtime injector through `LongEventHandler.ExecuteWhenFinished(...)`.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.5.1` and sync release/documentation notes.

### Behavior Changes
- HAR and similar custom pawn races now receive dialogue comp injection reliably even when XML node names are not plain `ThingDef` or when fields are inherited.
- Manual RPG dialogue entry availability for third-party race colonists is hardened without changing RPG action behavior.

## Non-Verbal Pawn RPG Dialogue Compatibility (v0.5.0)

### Module Map
- `1.6/Patches/CompPawnDialogue_Patch.xml`
  - Dependencies: RimWorld PatchOperation runtime.
  - Responsibility: inject `CompPawnDialogue` into all non-abstract pawn ThingDefs (`race`), auto-create missing `<comps>`, and avoid duplicate comp insertion.
- `RimChat/Comp/CompPawnDialogue.cs`
  - Dependencies: Float menu + RPG cooldown checks.
  - Responsibility: remove `Humanlike` hard gate so manual RPG dialogue can target all pawn races while preserving map/reach/liveness/cooldown checks.
- `RimChat/UI/Dialog_RPGPawnDialogue.RequestContext.cs`
  - Dependencies: RPG request prompt build, localized runtime language and settings state.
  - Responsibility: apply non-verbal target classification (animal/baby/mechanoid), enforce visible reply normalization to `sound + (inner thought)`, and append request-time non-verbal prompt constraints.
- `RimChat/Config/RimChatSettings.cs`, `RimChat/Config/RimChatSettings_RPG.cs`
  - Responsibility: add persisted setting `EnableRPGNonVerbalPawnSpeech` (default `true`) and RPG settings UI toggle.
- `RimChat/Config/RpgPromptDefaultsConfig.cs`, `Prompt/Default/PawnDialoguePrompt_Default.json`
  - Responsibility: store default non-verbal output constraint template as externalized prompt text.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: add localized toggle text, tooltip, default non-verbal sounds, and fallback thought line.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.5.0` and sync release/documentation notes.

### Behavior Changes
- Manual RPG dialogue entry is no longer limited to `Human/Humanlike` targets.
- Animal/baby/mechanoid target replies now normalize to one-line non-verbal format (`sound + parenthesized inner thought`), with language-aware parenthesis style (`（）` in Chinese, `()` otherwise).
- If model output already contains a valid `sound + (thought)` structure, the system preserves model-provided sound/thought and only normalizes parenthesis style.
- If model output misses the required structure, the system rewrites to localized default sound + wrapped thought fallback.
- RPG actions parsing/execution flow remains unchanged.

## RPG Output Contract Hardening (v0.4.12)

### Module Map
- `RimChat/AI/LLMRpgApiResponse.cs`
  - Dependencies: RPG action JSON extraction helpers (`ExtractJsonObject`, `ExtractJsonArray`, `ExtractStringField`).
  - Responsibility: accept both `params` and `parameters` action wrappers when parsing RPG `actions[]`, reducing provider-shape mismatch failures.
- `RimChat/UI/Dialog_RPGPawnDialogue.cs`
  - Dependencies: RPG request message builder and context compression pipeline.
  - Responsibility: append a strict RPG output-contract reminder on normal request construction, not only retry fallback.
- `RimChat/Config/RpgPromptDefaultsConfig.cs`, `Prompt/Default/PawnDialoguePrompt_Default.json`
  - Responsibility: align default RPG format examples with concrete allowed action naming (`TryGainMemory`) instead of placeholder `ActionName`.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: version bump and release/doc sync for the contract-hardening update.

### Behavior Changes
- RPG parser now reads `actions[i].parameters` in addition to `actions[i].params`.
- RPG request path now reinforces strict output contract per-turn during standard requests.
- Default format examples now better match executable action naming and reduce ambiguous placeholder outputs.

## RimTalk Preset Injection Limits Configurable + Unlimited by Default (v0.4.11)

### Module Map
- `RimChat/Compat/RimTalkCompatBridge.cs`
  - Dependencies: `RimChatMod.Settings`, RimTalk reflection bridge runtime.
  - Responsibility: use settings-driven limits when injecting active RimTalk preset mod entries into RPG prompt (`0 = unlimited`).
- `RimChat/Config/RimChatSettings_RimTalkCompat.cs`
  - Dependencies: RimChat settings runtime clamp helpers.
  - Responsibility: define/persist RimTalk preset injection limit settings (`RimTalkPresetInjectionMaxEntries`, `RimTalkPresetInjectionMaxChars`).
- `RimChat/Config/RimChatSettings_RPG.RimTalkCompatUI.cs`
  - Dependencies: RPG settings panel listing widgets and localization keys.
  - Responsibility: expose editable UI fields for preset-entry count/char limits and unlimited hint.
- `RimChat/Config/RimChatSettings.cs`, `RimChat/Config/RpgPromptDefaultsConfig.cs`, `RimChat/Config/RpgPromptCustomStore.cs`, `Prompt/Default/PawnDialoguePrompt_Default.json`
  - Responsibility: carry new fields through defaults/custom config load/save and default payload.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: add localized labels/hints for the new limit settings.

### Behavior Changes
- Active RimTalk preset mod-entry injection no longer uses hardcoded `12 entries` / `4200 chars`.
- New settings now control limits:
  - `RimTalkPresetInjectionMaxEntries`
  - `RimTalkPresetInjectionMaxChars`
- Both settings default to `0` (unlimited).

## RPG Pawn Dialogue Job Null-Safety Hardening (v0.4.10)

### Module Map
- `RimChat/AI/JobDriver_RPGPawnDialogue.cs`
  - Dependencies: `Verse.AI.JobDriver`, `Job/TargetIndex`, `Toils_Goto`, `Toils_Jump`, `RimWorld.Messages`, `Dialog_RPGPawnDialogue`.
  - Responsibility: safely resolve dialogue target pawn across job/toil lifecycle transitions and guard reservation/toil checks against invalid targets.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`
  - Responsibility: bump version to `0.4.10` and sync release notes.

### Behavior Changes
- `TargetPawn` resolution is now null-safe (`job` missing, invalid `TargetIndex.A`, or non-pawn targets return `null`).
- Pre-toil reservation now exits early when target is invalid, avoiding downstream broken job startup.
- Toil fail predicate now reads target once per evaluation to reduce race-like repeated dereference windows.

## Custom URL Safe Mapping + Endpoint Modes (v0.4.9)

### Module Map
- `RimChat/Config/ApiConfig.cs`
  - Dependencies: `AIProviderRegistry`, `Verse.Scribe_Values`, .NET `Uri` parser.
  - Responsibility: persist `CustomUrlMode`, infer legacy mode once on load, resolve runtime custom chat/models endpoints, map `cloud.siliconflow.*` to `api.siliconflow.cn`, and apply conservative BaseUrl completion rules.
- `RimChat/Config/RimChatSettings.cs`
  - Dependencies: cloud API settings UI, model list fetch flow, connection test flow.
  - Responsibility: expose URL mode selector for Custom rows, route model-list URLs through resolved custom runtime endpoints, and add models->chat fallback in FullEndpoint connectivity tests.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: add localization keys for custom URL mode labels/tooltips and runtime mapping/suspicious/fallback hints.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: bump version to `0.4.9` and sync behavior/documentation notes.

### Behavior Changes
- New Custom URL mode switch: `BaseUrl` vs `FullEndpoint`.
- Legacy configs auto-classify once: URLs containing `/chat/completions` become `FullEndpoint`; others become `BaseUrl`.
- Runtime mapping applies only to `cloud.siliconflow.*` hosts (Custom provider only).
- BaseUrl completion is conservative: only empty-path, `/`, and `/v1` auto-complete to `/v1/chat/completions`.
- FullEndpoint connectivity testing now falls back to chat endpoint when models probe fails, reducing false-negative checks for still-usable endpoints.

## Model List Fetch Hardening (v0.4.7)

### Module Map
- `RimChat/AI/AIProvider.cs`
  - Dependencies: provider registry defaults.
  - Responsibility: align DeepSeek model list endpoint with RimTalk (`/models`).
- `RimChat/Config/RimChatSettings.cs`
  - Dependencies: cloud API UI, model list fetch, model list parsing.
  - Responsibility: trim API keys for model list requests and add fallback model-id extraction for OpenAI-style responses.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: version bump to `0.4.7` and release/documentation sync.

### Behavior Changes
- DeepSeek model list URL now uses `/models` (RimTalk-aligned).
- Model list requests now trim API key whitespace.
- OpenAI-style model list parsing falls back to id extraction when JSON parsing yields empty results.

## DeepSeek Official URL Enforcement (v0.4.6)

### Module Map
- `RimChat/Config/ApiConfig.cs`
  - Dependencies: `AIProviderRegistry.Defs` official DeepSeek endpoints.
  - Responsibility: declare the official DeepSeek base URL constant for normalization/migration.
- `RimChat/Config/RimChatSettings.cs`
  - Dependencies: cloud config serialization, provider selection UI, model list/test request helpers.
  - Responsibility: normalize DeepSeek BaseUrl to official, ignore custom BaseUrl overrides, and ensure model list/test use official endpoints.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: version bump to `0.4.6` and release/documentation sync.

### Behavior Changes
- DeepSeek provider now enforces the official base URL (`https://api.deepseek.com/v1`) and auto-normalizes non-official BaseUrl values on load.
- Model list fetch and connection test for DeepSeek no longer use custom BaseUrl overrides.

## Default Comms Replacement Disabled (v0.4.5)

### Module Map
- `RimChat/Config/RimChatSettings.cs`
  - Dependencies: settings field defaults and `Scribe_Values.Look(...)` fallback defaults.
  - Responsibility: change `ReplaceCommsConsole` default/fallback from `true` to `false`.
- `RimChat/Config/RimChatSettings_AI.cs`
  - Dependencies: UI settings reset action.
  - Responsibility: set `ResetUISettingsToDefault()` comms replacement default to disabled.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: version bump to `0.4.5` and release/documentation sync.

### Behavior Changes
- Fresh/default RimChat settings now keep vanilla comms negotiation UI by default (`ReplaceCommsConsole = false`).
- Missing/legacy setting fallback in `ExposeData` now resolves to disabled replacement.
- UI settings "Reset to Default" now restores comms replacement to disabled.
- Players can still enable replacement manually from settings or map quick-toggle icon.

## Vanilla Negotiation Bridge Option (v0.4.4)

### Module Map
- `RimChat/Patches/FactionDialogRimChatBridgePatch.cs`
  - Dependencies: `RimWorld.FactionDialogMaker.FactionDialogFor`, `Verse.DiaNode`, `Verse.DiaOption`, `RimChatMod.Settings`, `Dialog_DiplomacyDialogue`.
  - Responsibility: inject one localized bridge option into vanilla faction negotiation root menus when comms replacement is disabled.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: provide localized label key `RimChat_UseRimChatContact`.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `Api.md`, `config.md`
  - Responsibility: version bump to `0.4.4` and release/documentation sync.

### Behavior Changes
- New bridge option appears only when `ReplaceCommsConsole = false`.
- Option is injected into vanilla faction negotiation root nodes (all vanilla faction-contact entry paths).
- Clicking bridge option closes vanilla node-tree flow (`resolveTree = true`) and opens `Dialog_DiplomacyDialogue(faction, negotiator)`.
- Insertion policy prefers placing the bridge option immediately before close/hang-up style options (`resolveTree=true` and no node links); fallback is append.
- Existing `ReplaceCommsConsole = true` replacement flow remains unchanged.

## Diplomacy Prompt Enrichment + Empire Royalty Constraints (v0.4.3)

### Module Map
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Added reusable diplomacy-context builders: player pawn profile resolver, Empire royalty summary builder, and faction settlement summary builder.
  - Added `BuildFullSystemPrompt(...)` overload with optional `playerNegotiator` input while keeping existing signature compatibility.
- `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - Diplomacy `dynamic_data` node now injects `player_pawn_profile`, `player_royalty_summary`, and `faction_settlement_summary` (under faction-info injection path).
  - Hierarchical diplomacy build core now accepts optional `playerNegotiator` and threads it into dynamic-node assembly.
- `RimChat/UI/Dialog_DiplomacyDialogue.cs`, `RimChat/UI/Dialog_DiplomacyDialogue.Strategy.cs`
  - Manual diplomacy prompt build now passes explicit window negotiator.
  - Strategy context now appends the same player-pawn and Empire-royalty summaries for consistency with the main diplomacy system prompt.
- `RimChat/Persistence/PromptPersistenceService.TemplateVariables.cs`
  - Added new template variables: `player_pawn_profile`, `player_royalty_summary`, `faction_settlement_summary`.
  - Variable resolver now supports the three new nodes and returns explicit fallback text when context is absent.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Added localized description keys for the new template variables.

### Behavior Changes
- Diplomacy prompt context now carries player capability signals and full faction settlement lists, reducing action hallucination in negotiation turns.
- Empire diplomacy sessions now include honor/title/permit availability snapshots with prompt-side soft constraints focused on `create_quest` and `request_aid`.
- Negotiator source policy is unified: explicit negotiator first; fallback to highest-social player colonist when explicit context is unavailable.
- Prompt variable picker now exposes the new dynamic fields so custom prompt templates can reference the same runtime data.

## Map Bottom-Right Comms Toggle Icon (v0.4.1)

### Module Map
- `RimChat/Patches/PlaySettingsPatch_CommsToggleIcon.cs`
  - Dependencies: `RimWorld.PlaySettings.DoPlaySettingsGlobalControls`, `Verse.WidgetRow`, `RimChatMod.Settings`.
  - Responsibility: append a map-view bottom-right icon-row button to quick-toggle `ReplaceCommsConsole`, persist setting, and show localized feedback.
- `1.6/Textures/UI/RimChat/CommsToggleIcon.png`, `1.6/Textures/UI/CommsToggleIcon.png`
  - Responsibility: namespaced runtime icon texture with legacy fallback for the bottom-right quick toggle button.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: localized tooltip/status/message keys for the quick toggle icon behavior.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`, `config.md`
  - Responsibility: version bump to `0.4.1` and release/documentation sync.

### Behavior Changes
- Added a map-view-only quick toggle entry in the vanilla bottom-right icon row.
- The icon reuses existing `ReplaceCommsConsole` (same behavior as MOD settings checkbox), updates immediately, and persists at click time.
- Icon visual state now uses vanilla check/cross overlay markers (`enabled=green check`, `disabled=red cross`) via `WidgetRow.ToggleableIcon`, with localized status tooltip and click message.

## Development Tooling - GitNexus C# Query Hotfix

### Module Map
- `tools/hotfix/apply-gitnexus-csharp-query-hotfix.ps1`
  - Responsibility: idempotently patch global GitNexus C# heritage query node names (`simple_base_type -> type`) in `tree-sitter-queries.js`.
  - Dependency: global npm GitNexus install (`%APPDATA%\\npm\\node_modules\\gitnexus` or `npm root -g`), PowerShell, UTF-8 console settings.
  - Usage: `powershell -ExecutionPolicy Bypass -File tools/hotfix/apply-gitnexus-csharp-query-hotfix.ps1`
  - Scope: developer tooling only; does not alter RimWorld mod runtime behavior.

## API Header Version/GitHub Tools + Version Log Viewer (v0.4.0)

### Module Map
- `RimChat/Config/RimChatSettings.cs`
  - Replaced API tab title-only draw with `DrawApiSettingsHeaderBar(...)` while keeping existing tab flow unchanged.
- `RimChat/Config/RimChatSettings_APIHeader.UX.cs`
  - Added API header tool rendering (`Version` + green `GitHub`) and localized version-log resolver/reader with UTF-8 load + missing/empty/read-failure fallbacks.
  - Added language mapping for log source file (`ChineseSimplified/ChineseTraditional -> VersionLog.txt`, others -> VersionLog_en.txt).
- `RimChat/UI/Dialog_VersionLogViewer.cs`
  - Added a dedicated scrollable in-game viewer window for version-log text.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Added localized keys for Version/GitHub button labels/tooltips, version-log window title, and fallback messages.
- `About/About.xml`, `VersionLog.txt`, `VersionLog_en.txt`
  - Bumped mod version to `0.4.0` and synced release notes.

### Behavior Changes
- API settings header now includes:
  - `Version: x.y.z` button (localized prefix) where `x.y.z` comes from the first non-empty line of the mapped version-log file.
  - Green `GitHub` button that directly opens `https://github.com/yancy22737-sudo/RimChat`.
- Clicking `Version` opens a scrollable viewer showing the full mapped version-log content in-game.
- When version log file is missing, empty, or unreadable, viewer shows localized fallback text instead of throwing runtime errors.

## Diplomacy Blocked Status Auto Vertical Scroll (v0.3.165)

### Module Map
- `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - Replaced blocked-status `GUI.BeginScrollView` rendering with clip-group + time-driven y-offset drawing (no visible scrollbar).
  - Added blocked-status auto-scroll state fields (`text cache`, `offset`, `direction`, `pause`, `last realtime`) and reset hooks.
  - Added wrapped-height measurement based on rendered width to trigger scrolling only when visual content height exceeds the status area.
  - Preserved existing status priority branching (`waiting > error > blocked`) and kept typing/error branch rendering unchanged.
- `VersionLog.txt`, `VersionLog_en.txt`, `About/About.xml`
  - Added release notes and bumped mod version to `0.3.165`.

### Behavior Changes
- In diplomacy input-area blocked status, overflow text now auto-scrolls vertically with no scrollbar.
- Auto-scroll motion uses ping-pong behavior at `18 px/s`, with `0.6s` pause at top and bottom edges.
- Auto-scroll state resets on text change, when blocked status is not active, and when the diplomacy window closes.

## Goodwill Segmented Peace Policy (v0.3.164)

### Module Map
- `RimChat/DiplomacySystem/ApiActionEligibilityService.cs`
  - Added goodwill-segmented peace policy validation for `make_peace` in execution eligibility.
  - Added peace-talk-only quest restriction in `[-50,-21]` for `create_quest` / quest template validation.
  - New behavior codes: `peace_goodwill_too_low`, `peace_talk_required`, `peace_talk_only_range`.
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Added dynamic response-contract injection block `DYNAMIC PEACE POLICY (GOODWILL-BASED)`.
  - Injection text now mirrors runtime execution constraints by goodwill segment.
- `RimChat/Config/PromptTextConstants.cs`
  - Added centralized constants for goodwill-segmented peace-policy prompt text to avoid hardcoded literals in assembler logic.
- `Api.md`, `config.md`
  - Synced public behavior notes for segmented peace policy and runtime prompt injection.
- `VersionLog.txt`, `VersionLog_en.txt`, `About/About.xml`
  - Added release notes and bumped mod version to `0.3.164`.

### Behavior Changes
- `make_peace`:
  - blocked when goodwill `< -50` (hostility too deep).
  - blocked in `[-50,-21]` and redirected to peace talks quest flow.
  - allowed again in `[-20,0]` (existing war/cooldown gates still apply).
- `create_quest`:
  - in `[-50,-21]`, only `OpportunitySite_PeaceTalks` is allowed.
  - outside this band, existing template-eligibility behavior is preserved.
- Diplomacy prompt contract now emits per-band policy guidance to keep LLM action choice aligned with execution eligibility.

## Prompt Token Budget Removal (v0.3.163)

### Module Map
- `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - Removed runtime prompt-budget trim execution for both diplomacy and RPG prompt assembly chains.
- `RimChat/Config/PromptPolicyConfig.cs`
  - Removed persisted budget model fields (`GlobalPromptCharBudget`, `NodeBudgets`, `TrimPriorityNodeIds`), keeping non-budget policy controls.
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Removed PromptPolicy budget field JSON read/write and budget default backfill logic.
- `RimChat/Config/RimChatSettings_Prompt.cs`
  - Removed Prompt settings `PromptPolicy` navigation entry and render branch.
- `RimChat/Config/RimChatSettings_PromptPolicy.cs`
  - Removed budget editor UI implementation file.
- `RimChat/Config/SystemPromptConfig.cs`
  - Bumped `CurrentPromptPolicySchemaVersion` to `4`.
- `Prompt/Default/SystemPrompt_Default.json`
  - Removed PromptPolicy budget defaults and updated schema version to `4`.

### Behavior Changes
- Prompt token budget trimming is fully disabled in both diplomacy and RPG channels.
- `Prompt budget trim` debug logs are no longer emitted.
- Diplomacy API limits and `api_limits` prompt content remain unchanged.

## Social Post Summary Mirror to Leader Memory (v0.3.162)

### Module Map
- `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.SocialCircle.NewsRequests.cs`
  - Responsibility: after social-circle post finalization, mirror one summary record into each active non-player faction leader memory (`DiplomacySessionSummaries`), with dedupe and anti-loop guard.
- `About/About.xml`
  - Responsibility: bumped mod version to `0.3.162`.
- `VersionLog.txt`, `VersionLog_en.txt`, `README.md`
  - Responsibility: synchronized release notes for the social-summary mirror behavior.

### Behavior Changes
- Every successful social-circle post now mirrors one summary record to all active non-player faction leader memories.
- Mirror summary text uses `Headline + Lead` first, then falls back to `Content`.
- Duplicate mirrors are prevented with a stable `ContentHash` key derived from post identity.
- Posts sourced from `DiplomacySummary` are excluded from mirroring to avoid summary self-amplification loops.

## Prompt UI Cleanup + Peace Rule Pipeline Fix (v0.3.160)

### Module Map
- `RimChat/Config/RimChatSettings_Prompt.cs`
  - Removed the diplomacy global-dialogue secondary editor block from the Global Prompt page (kept only Global System Prompt editor).
- `RimChat/Config/RimChatSettings_AI.cs`
  - Removed the Gift Settings accordion row from MOD Settings AI section display.
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Compact action catalog now prefers configured action description/requirement text instead of fixed hardcoded short copy.
  - Added make-peace requirement merge rule: always includes `very high sincerity` constraint in the compact prompt contract.
  - Added legacy make-peace action migration for old custom prompt payloads.
- `RimChat/Config/SystemPromptConfig.cs`
  - Updated minimal-default make-peace action description/requirement to include high-sincerity peace condition.
- `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
  - Updated make-peace requirement text to include high-sincerity gate.
- `About/About.xml`
  - Bumped mod version to `0.3.160`.

### Behavior Changes
- Settings UI no longer shows the two highlighted blocks: diplomacy global-dialogue secondary editor and AI gift-settings accordion row.
- Peace-rule prompt changes now flow into the actual compact action contract used at runtime prompt assembly.
- Existing custom prompt files with legacy make-peace wording are auto-upgraded to the new default wording when matched as legacy values.

## Session Switch Continuity + Input/Status Fixes (v0.3.159)

### Module Map
- `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - Added close-intent tracking (`normal` vs `switch faction`) for the diplomacy window.
  - Faction-list switching now marks switch intent before closing the current window.
  - `PreClose` now preserves in-flight session continuity on switch-close (no pending-request cancel, no close-time summary commit, no presence-cache lock).
  - Reworked `DrawSingleLineClippedLabel` to a stable single-line truncate draw path (no GUI group clipping side effects).
- `RimChat/UI/Dialog_DiplomacyDialogue.TypingStatus.cs`
  - Typing-status text draw now restores `Text.Anchor`, `Text.Font`, and `GUI.color` explicitly after rendering.
- `RimChat/UI/Dialog_RPGPawnDialogue.cs`
  - Enter-send keyboard check is now executed before `Widgets.TextField` so `TextField` cannot consume submit first.
  - Submit-key detection now supports `rawType == KeyDown` fallback for consumed-event scenarios.
- `About/About.xml`
  - Bumped mod version to `0.3.159`.

### Behavior Changes
- Diplomacy: switching faction tabs during AI waiting no longer kills the original in-flight request; returning to that session can continue and receive callback results.
- Diplomacy: waiting-status rotating text is now reliably visible in the bottom status area.
- RPG: `Enter` and `KeypadEnter` now consistently send while focused/non-empty; IME composition guard remains active.

## Unified Request Timeout (v0.3.158)

### Behavior Changes
- Model request timeout is now unified to `20s` for both local and cloud paths.
- Applied consistently across `AIChatServiceAsync`, `AIChatService`, and `AIChatClient`.

## Local Timeout Recovery (v0.3.157)

### Module Map
- `RimChat/AI/AIChatServiceAsync.cs`
  - Local request timeout raised to `180s` (cloud remains `60s`).
  - Connection-error path now maps timeout-like failures to `RimChat_ErrorTimeout` instead of always using local-disconnect copy.
  - Added local bounded connection retry branch for transient timeout/reset errors.
- `RimChat/AI/AIChatServiceAsync.LocalControl.cs`
  - Added local connection retry policy helpers (`ShouldRetryLocalConnectionError`, `GetLocalConnectionRetryDelaySeconds`) and retry decision diagnostic logs (`local_conn_retry`).

### Behavior Changes
- Local mode is now less likely to report false “cannot connect” errors for long-running generations.
- Timeout-like failures still fail fast when retries are exhausted, but user-facing error text now indicates timeout explicitly.

## Local 500 Resilience + Diagnostics (v0.3.154)

### Module Map
- `RimChat/AI/AIChatServiceAsync.cs`
  - Integrated local-model single-flight gating in the async request flow (`enqueue -> wait turn -> execute -> release`).
  - Added local-only transient 5xx retry orchestration (`500/502/503/504`) with staged backoff and preserved existing `HTTP 400 user input rejected` reduced-context retry path.
  - Added per-attempt structured fingerprint logging hooks (`requestId/attempt/channel/model/host/messageCount/jsonBytes/elapsedMs/httpCode`).
- `RimChat/AI/AIChatServiceAsync.LocalControl.cs`
  - New partial helper for local request queue coordination, local 5xx retry policy helpers, and debug-only diagnostic log payload assembly.

### Behavior Changes
- Local model mode now runs one in-flight request at a time; additional local requests are queued and executed serially.
- Local transient server errors (`500/502/503/504`) now auto-retry with short-then-long backoff (+ jitter), then fail with the original error path if retries are exhausted.
- Existing `HTTP 400 user input rejected` fallback retry remains active and independent.
- Cloud provider request concurrency and retry behavior remain unchanged.

## Diplomacy Relation + Social Visibility Fix (v0.3.153)

### Module Map
- `RimChat/DiplomacySystem/GameAIInterface.cs`
  - `DeclareWar` / `MakePeace` now use a goodwill-first relation settlement helper.
  - Fixed target goodwill policy is now explicit (`war -> -80`, `peace -> 0`), with strict post-apply validation before success/cooldown is recorded.
- `RimChat/DiplomacySystem/Social/SocialEnums.cs`
  - Added social enqueue/generation result enums and `SocialPostEnqueueResult` for observable queue outcomes.
- `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.SocialCircle.cs`
  - Added overloads for `EnqueuePublicPost` / `TryCreateKeywordDialoguePost` with detailed enqueue results.
  - Added standardized social failure label helpers and session message bridge for async generation outcomes.
- `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.SocialCircle.NewsRequests.cs`
  - Queue path now emits structured enqueue failure reasons (`ai unavailable`, `queue full`, `invalid seed`, `origin blocked`, `dispatch failed`).
  - Async success/error callbacks now push generated/failed status back into faction dialogue sessions (strict AI result, no local fallback post).
- `RimChat/UI/Dialog_DiplomacyDialogue.SocialCircle.cs`
  - Social action feedback is now split into queued/generated/failed-with-reason semantics for both explicit and keyword-triggered paths.
- `RimChat/Persistence/PromptPersistenceService.cs`, `RimChat/Persistence/PromptPersistenceService.DomainStorage.cs`, `RimChat/Config/SystemPromptConfig.cs`
  - Typed parse diagnostics now carry source context.
  - When typed parse fails but fallback succeeds, log is downgraded to info; warning remains only for unrecoverable parse failure.
- `1.6/Languages/*/Keyed/RimChat_Keys.xml`
  - Added CN/EN keys for social queue/generation statuses and normalized failure reasons.

### Behavior Changes
- `MakePeace` no longer emits goodwill-faction `SetRelationDirect` error logs in the normal path.
- Declared war/peace actions only report success when final relation state is actually achieved.
- “Social triggered but no visible content” is now diagnosable in-session: users get queue success and async generation success/failure reason messages.
- Social generation remains strict-AI: parse/AI failures do not create local fallback posts.

## Dialogue Enter-Key Send Fix (v0.3.152)

### Module Map
- `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - Explicitly set `closeOnAccept = false` and `closeOnCancel = true` in the diplomacy dialogue window constructor.
  - Added focused-input keyboard send gating for `Enter/KeypadEnter`, with IME composition guard.
  - `Alt+Enter` now inserts a newline only when the diplomacy input is focused.
- `RimChat/UI/Dialog_RPGPawnDialogue.cs`
  - Explicitly set `closeOnAccept = false` and `closeOnCancel = true` in the RPG dialogue window constructor.
  - Added focused-input keyboard send gating for `Enter/KeypadEnter`, with IME composition guard.
  - Kept RPG input single-line; `Alt+Enter` does not trigger send.
- `RimChat/RimChat.csproj`
  - Added `UnityEngine.InputLegacyModule.dll` reference so IME composition state can be read through `Input.compositionString`.

### Behavior Changes
- Pressing `Enter` no longer closes RPG or diplomacy dialogue windows through default window accept behavior.
- Enter-to-send now requires focused input and valid send conditions (`non-empty` + `send-ready`).
- During IME composition, `Enter` is reserved for candidate confirmation and does not send/close.
- `Esc` close behavior remains unchanged.

## API URL Normalization Hardening (v0.3.151)

### Module Map
- `RimChat/AI/AIProvider.cs`
  - Fixed provider endpoint/model-list URL constants (removed invalid embedded whitespace).
  - Added runtime normalization in provider URL getters as a defensive fallback.
- `RimChat/Config/ApiConfig.cs`
  - Added `NormalizeUrl`, `ToModelsEndpoint`, and `EnsureChatCompletionsEndpoint` for unified URL cleanup/composition.
  - `GetEffectiveEndpoint()` now returns normalized endpoint values.
- `RimChat/Config/LocalModelConfig.cs`
  - Local default base URL corrected to `http://localhost:11434`.
  - Added normalized base-url validation path.
- `RimChat/AI/AIChatService.cs`, `RimChat/AI/AIChatServiceAsync.cs`, `RimChat/AI/AIChatClient.cs`
  - Local provider endpoint construction now uses normalized base URL.
- `RimChat/Config/RimChatSettings.cs`
  - API settings input/model-fetch/connection-test paths now normalize custom and local URLs before request dispatch.

### Behavior Changes
- Cloud provider default URL presets no longer fail URL validation due to malformed whitespace.
- Local model default URL no longer starts from an invalid preset.
- Custom `BaseUrl` values with leading/trailing/embedded whitespace are normalized at load and at runtime usage points.
- Existing feature contracts remain unchanged (`GetEffectiveEndpoint`, model list fetch flow, connection-test flow).

## Diplomacy Typing Status Immersion (v0.3.149)

### Module Map
- `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - Waiting-state rendering now delegates to a dedicated immersive typing-status layer instead of static text plus dot suffix.
- `RimChat/UI/Dialog_DiplomacyDialogue.TypingStatus.cs`
  - Responsibility: diplomacy waiting-state UI (1.6s rotating localized phrases, rounded status capsule, three-dot pulse, and subtle indeterminate sweep bar).
  - Interface: `DrawDiplomacyTypingStatus(Rect rect)` is consumed only by the diplomacy dialogue waiting branch.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Added synchronized localization keys `RimChat_DiplomacyTypingStatus_01..06` with diplomacy-themed status phrases.

### Behavior Changes
- Diplomacy waiting text now rotates through six immersive status phrases every `1.6` seconds.
- Waiting indicator now uses a low-profile modern animation stack: status capsule, pulse dots, and indeterminate sweep bar.
- Existing state priority remains unchanged (`waiting > error > blocked`).
- If any rotating key is missing, UI falls back to `RimChat_AIIsTyping` to avoid key-leak text.

## Social Circle Force-Generate Stabilization (v0.3.144)

### Module Map
- `RimChat/DiplomacySystem/Social/SocialEnums.cs`
  - Added `SocialForceGenerateFailureReason` enum to standardize force-generate failure diagnostics.
- `RimChat/WorldState/WorldEventLedgerComponent.cs`
  - Added `CollectNow()` method for manual immediate collection of Letter stack events and raid battle states.
- `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.SocialCircle.cs`
  - Added `TryForceGeneratePublicPost(DebugGenerateReason, out SocialForceGenerateFailureReason)` with instant collection retry logic.
- `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.SocialCircle.NewsRequests.cs`
  - Added overloaded `TryQueueNextScheduledNews` and `CanGenerateSocialNews` methods with failure-reason output.
- `RimChat/Config/RimChatSettings_SocialCircle.cs`
  - Updated force-generate button to call new API and display precise failure messages.
- `RimChat/UI/Dialog_DiplomacyDialogue.SocialCircleView.cs`
  - Fixed actor line rendering to treat player faction as valid single-side and hide line when both sides are null.
- `RimChat/DiplomacySystem/DiplomacyEventManager.cs`
  - Added raid strategy/arrival-mode normalization and executability pre-checks to prevent empty-collection RandomElement crashes.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Added precise failure-reason language keys: `RimChat_SocialForceGenerateFailedDisabled`, `RimChat_SocialForceGenerateFailedAiUnavailable`, `RimChat_SocialForceGenerateFailedQueueFull`, `RimChat_SocialForceGenerateFailedNoSeed`.

### Public Interfaces Updated
- `GameComponent_DiplomacyManager.TryForceGeneratePublicPost(DebugGenerateReason reason, out SocialForceGenerateFailureReason failureReason)`
  - New method with precise failure-reason output and instant collection retry.
- `WorldEventLedgerComponent.CollectNow()`
  - New method for manual immediate event/battle-state collection.
- `SocialForceGenerateFailureReason` enum
  - `Disabled`, `AiUnavailable`, `QueueFull`, `NoAvailableSeed`, `Unknown`.

### Behavior Changes
- Force-generate now performs instant collection retry: if first seed selection fails, immediately calls `CollectNow()` and retries seed selection once.
- Force-generate failure messages are now precise and actionable (system disabled / AI unavailable / queue full / no available events).
- Social actor line rendering now correctly shows player faction as single-side and hides line when both source and target are null.
- RimChat raid triggering now normalizes null/invalid strategy and arrival-mode to executable defaults (prefers `ImmediateAttack` / `EdgeWalkIn`), preventing empty-collection crashes.

## RPG Paging Navigation Visibility Fix (v0.3.147)

### Module Map
- `RimChat/UI/Dialog_RPGPawnDialogue.TextPaging.cs`
  - Responsibility: pagination fit now uses conservative scaled text-height measurement to match large rendered dialogue text and keep navigation visible for overlong responses.

### Behavior Changes
- Fixed cases where overlong RPG dialogue overflowed into the input area but did not show page navigation after typing completed.

## RPG Paging Height Fix (v0.3.146)

### Module Map
- `RimChat/UI/Dialog_RPGPawnDialogue.TextPaging.cs`
  - Responsibility: RPG dialogue paging now measures page-fit height with the same rich-text size as runtime rendering (`<size=34>`), plus a safety padding to prevent clipping.

### Behavior Changes
- Fixed the case where page 1 looked truncated and page 2 looked abnormal because overflowing lines were hidden under the input area.

## Social Letter + Persona Trigger Stabilization (v0.3.145)

### Module Map
- `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.SocialCircle.NewsRequests.cs`
  - Responsibility: after a social-circle world-news post is finalized, immediately push a right-side RimWorld `Letter` notification with category-aware letter severity.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`, `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Responsibility: provide localized social-news letter title/body keys (`RimChat_SocialNewsLetterTitle`, `RimChat_SocialNewsLetterBody`) and avoid hardcoded UI text.
- `RimChat/DiplomacySystem/GameComponent_RPGManager.PersonaBootstrap.cs`
  - Responsibility: stabilize per-pawn persona auto-generation by adding null-safe AI availability checks and runtime missing-persona scan triggers in addition to load-time bootstrap.

### Behavior Changes
- Social circle: successful news cards now also create a right-side letter for immediate player visibility.
- RPG persona auto-generation: no longer depends only on one-time load bootstrap; runtime scans now keep filling missing persona prompts for eligible humanlike pawns.
- Stability: persona generation path no longer risks null-instance crashes when `AIChatServiceAsync.Instance` is unavailable.

## Social Circle World-News Feed (v0.3.143)

### Module Map
- `RimChat/DiplomacySystem/Social/SocialNewsSeed.cs`, `RimChat/DiplomacySystem/Social/SocialNewsSeedFactory.cs`, `RimChat/DiplomacySystem/Social/SocialNewsJsonParser.cs`, `RimChat/DiplomacySystem/Social/SocialNewsPromptBuilder.cs`
  - Responsibility: normalize world events / dialogue-public statements into fact-grounded social-news seeds, build strict LLM prompts, and parse validated JSON drafts.
- `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.SocialCircle.cs`, `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.SocialCircle.NewsRequests.cs`
  - Responsibility: schedule fact scans, queue asynchronous news generation requests, deduplicate by origin, and finalize structured social posts only after valid LLM output.
- `RimChat/DiplomacySystem/Social/PublicSocialPost.cs`, `RimChat/DiplomacySystem/Social/SocialCircleState.cs`, `RimChat/DiplomacySystem/Social/SocialCircleService.cs`
  - Responsibility: persist structured world-news cards plus processed-origin state, while limiting gameplay side effects to dialogue-sourced public statements only.
- `RimChat/UI/Dialog_DiplomacyDialogue.SocialCircleView.cs`
  - Responsibility: render the diplomacy-window social tab as headline/lead/cause/process/outlook/quote news cards and remove like-count interaction.
- `Prompt/Default/SocialCirclePrompt_Default.json`, `RimChat/Config/RimChatSettings_PromptSocialCircle.cs`
  - Responsibility: expose dedicated world-news style / JSON contract / fact-template prompt editing and persist those values in the split Social Circle prompt domain.

### Public Interfaces Updated
- `GameComponent_DiplomacyManager.ForceGeneratePublicPost(...)`
  - Now queues one fact-based world-news generation request instead of instantly fabricating a random post.
- `GameComponent_DiplomacyManager.EnqueuePublicPost(...)`
  - Now turns `publish_public_post` / keyword-triggered dialogue output into a dialogue-derived news seed and sends it through the strict LLM news pipeline.
- `PublicSocialPost`
  - Added persisted world-news fields: `OriginType`, `OriginKey`, `Headline`, `Lead`, `Cause`, `Process`, `Outlook`, `Quote`, `QuoteAttribution`, `SourceLabel`, `CredibilityLabel`, `CredibilityValue`, and `GenerationState`.

### Behavior Constraints
- Scheduled world-news seed selection no longer requires resolved actor factions; dual-null actor seeds can still be queued if they satisfy base validity and dedup checks.
- Social-circle load cleanup does not delete dual-null actor cards; actor visibility is controlled by UI rendering rules.
- Social actor line rendering now has three states: dual-faction arrow (`A → B`), single-faction line (`Related faction: X`), or hidden when neither side is available.
- Scheduled generation now allows failed origins to retry after a 2-day cooldown (manual button remains immediate retry).

## Diplomacy Prompt Gift Action Removal (v0.3.142)

### Module Map
- `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
  - Removes `send_gift` from the default diplomacy action catalog so newly rebuilt prompt configs no longer expose gift sending to the LLM.
- `RimChat/Config/SystemPromptConfig.cs`
  - Removes the code-side default `send_gift` action and switches the `request_aid` requirement assignment to action-name lookup instead of a hard-coded index.
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Migrates older prompt configs by stripping legacy `send_gift` entries and removes gift-only API limit text from the generated diplomacy prompt.
- `RimChat/action_rules.txt`
  - Removes `send_gift` guidance so the auxiliary prompt rules stay aligned with the current diplomacy action contract.

## RPG Floating Subtitle Overlay (v0.3.141)

### Module Map
- `RimChat/UI/Dialog_RPGPawnDialogue.FeedbackOverlay.cs`
  - Upgraded portrait-side feedback into lightweight RPG floating subtitles with soft rounded underlays, text shadows, and independent rise/fade motion.
- `RimChat/UI/Dialog_RPGPawnDialogue.Portraits.cs`
  - Centralized left/right portrait rect helpers so portrait rendering and feedback overlays share one layout source.
- `RimChat/UI/Dialog_RPGPawnDialogue.Actions.cs`
  - Keeps existing action/system feedback producers, which now feed the floating subtitle overlay instead of the old top-right panel.
- `RimChat/UI/Dialog_RPGPawnDialogue.cs`
  - Keeps feedback drawing in the post-dialogue overlay pass so subtitles render beside the left portrait, above the dialogue box.

## Prompt File Map (v0.3.137)

- `Prompt/Default/SystemPrompt_Default.json`
  - Owns true global system text, environment prompt blocks, dynamic injection headers, and prompt policy defaults.
- `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
  - Owns diplomacy dialogue text, diplomacy API action prompt text, response format, decision rules, and diplomacy-side templates.
- `Prompt/Default/PawnDialoguePrompt_Default.json`
  - Owns pawn/RPG dialogue defaults, persona bootstrap prompts, RPG API prompt text, fallback templates, and RimTalk compatibility prompt text.
- `Prompt/Default/FactionPrompts_Default.json`
  - Owns faction prompt defaults; semantics stay unchanged.
- `Prompt/Default/SocialCirclePrompt_Default.json`
  - Owns the social-circle rule prompt plus the `publish_public_post` action prompt text.
- Runtime custom prompt persistence now mirrors the same split under `Prompt/Custom/*_Custom.json`.

## Default Diplomacy Prompt Fallback Recovery (v0.3.134)

### Module Map
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Restored a current-schema text fallback parser for `SystemPrompt_Default.json`, so settings/runtime config recovery can still rebuild complete diplomacy prompt data when typed JSON parsing is incomplete.
- `RimChat/Config/RimChatSettings_Prompt.cs`
  - Prompt settings UI continues to bind to `SystemPromptConfigData`, which now recovers from the default diplomacy prompt file more reliably.

## Diplomacy Prompt Action Gating Fixes (v0.3.133)

### Module Map
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Diplomacy response-contract prompt now appends blocked-action hints, and API limits now explicitly state `Quest creation: YES`.
- `RimChat/DiplomacySystem/ApiActionEligibilityService.cs`
  - Added projected-goodwill hard gating for `request_aid`, `request_caravan`, and `create_quest`, so execution now rejects actions whose fixed cost would drop goodwill below 0.
- `RimChat/AI/AIActionExecutor.cs`
  - Dialogue API fixed-cost application now resolves `request_aid` cost type from the action `type` parameter instead of always treating it as military aid.

## Placeholder Prompt Self-Heal (v0.3.132)

### Module Map
- `RimChat/Config/SystemPromptConfig.cs`
  - Added a shared placeholder constant for the minimal fallback `GlobalSystemPrompt`.
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Custom prompt config loading now hard-detects the placeholder `GlobalSystemPrompt`, logs an explicit error, rebuilds from `Prompt/Default/SystemPrompt_Default.json`, and refuses to save the placeholder into `system_prompt_config.json`.

## Final Legacy Sweep (v0.3.131)

### Module Map
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Removed the old quest-guidance migration pass; prompt config now keeps only the current quest guidance model.
- `RimChat/Core/RimChatMod.cs`
  - Removed the one-time legacy ModSettings cleanup hook.
- `RimChat/Config/PromptTextConstants.cs`
  - Unified the raid-parameter constant under the current non-legacy name.
- `RimChat/Memory/FactionDialogueSession.cs`
  - Deleted unused legacy alias members from runtime strategy-suggestion data.
- `RimChat/UI/PromptTemplateFieldLocalizer.cs`
  - Trimmed field-name mapping down to the current prompt field names only.
- `RimChat/Memory/RpgNpcDialogueArchive.cs`
  - Removed the obsolete archive-session legacy flag and renamed the remaining fallback helper away from legacy wording.

## Legacy Import Removal (v0.3.130)

### Module Map
- `RimChat/Config/RimChatSettings.cs`
  - Removed the load-time branch that copied legacy RimTalk compatibility settings from old ModSettings fields into the RPG custom prompt file.
- `RimChat/Config/RimChatSettings_RimTalkCompat.cs`
  - Deleted the old `TryLoadLegacyRimTalkCompatFromModSettings` compatibility loader.
- `RimChat/Memory/LeaderMemoryManager.cs`
  - Removed legacy dialogue-session backfill into leader memories on game load; post-load now only refreshes current baseline snapshots.

## Faction Prompt TemplateFields Only (v0.3.129)

### Module Map
- `Prompt/Default/FactionPrompts_Default.json`
  - Default faction prompt JSON now uses only the `TemplateFields` schema and no longer ships the old flat field layout.
- `FactionPrompts_Default.json`
  - The root reference/default faction prompt JSON is aligned to the same `TemplateFields`-only schema.
- `RimChat/Config/FactionPromptManager.cs`
  - Faction prompt parsing now reads only `TemplateFields` and no longer falls back to the old flat prompt-field schema.

## Local JSON Strict Cleanup (v0.3.128)

### Module Map
- `RimChat/Persistence/PromptFileManager.cs`
  - Global prompt JSON now uses typed read/write only and no longer migrates old files from the deprecated save-data prompt path.
- `RimChat/Config/FactionPromptManager.cs`
  - Custom faction prompt JSON now loads only from the unified current config path instead of probing a legacy location.
- `RimChat/Memory/RpgNpcDialogueArchiveJsonCodec.cs`
  - RPG NPC archive JSON now requires the sessions-first schema and no longer converts legacy top-level `turns`.
- `RimChat/Memory/RpgNpcDialogueArchiveManager.cs`
  - RPG NPC archive loading no longer scans or copies JSON files from legacy archive directories.
- `RimChat/Memory/LeaderMemoryJsonCodec.cs`
  - Leader-memory JSON parsing now accepts only the current field names and array keys.
- `RimChat/Memory/LeaderMemoryManager.cs`
  - Leader-memory loading now reads only from the current save-data directory with no legacy JSON file migration.

## JSON Contract Audit & Strict Parsing (v0.3.127)

### Module Map
- `RimChat/AI/AIResponseParser.cs`
  - Diplomacy parser no longer reads legacy top-level `action/response/parameters` fields or alternate action keys such as `action_type/name/type`; gameplay effects now come only from the `actions` array contract.
  - Strategy suggestion payloads no longer expose legacy alias fields.
- `RimChat/AI/LLMRpgApiResponse.cs`
  - RPG parser no longer reads dialogue text from JSON `text/response` fields; visible dialogue must stay outside the parser-facing JSON block.
- `RimChat/AI/AIChatServiceAsync.cs`
  - Reduced-context retries now append strict per-channel contract reminders for both diplomacy and RPG, reducing fallback drift into old JSON shapes.
- `RimChat/UI/Dialog_RPGPawnDialogue.RequestContext.cs`
  - RPG history normalization no longer falls back to storing raw JSON as visible dialogue when the model violates the contract.
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Diplomacy critical action rules now explicitly ban the legacy single-action wrapper format.
  - Prompt config JSON loading now uses the typed codec only and rejects incomplete/failed config payloads instead of falling back to the old hand-written parser.
- `Api.md`
  - API docs now state that diplomacy no longer accepts the legacy `action / parameters / response` wrapper.

## RPG Strict Output Contract (v0.3.125)

### Module Map
- `Prompt/Default/RpgPrompts_Default.json`
  - RPG format constraint now explicitly forbids legacy top-level JSON wrappers such as `action/content/text`; only the trailing `{"actions":[...]}` block remains valid.
- `RimChat/AI/AIChatServiceAsync.cs`
  - Reduced-context RPG retries now append a strict protocol reminder so retry requests keep the same output contract instead of drifting to an older JSON shape.
- `RimChat/AI/LLMRpgApiResponse.cs`
  - Removed legacy top-level RPG action parsing; the parser now reads only the `actions` array contract for gameplay effects.

## RPG Retry & Legacy Response Compatibility (v0.3.124)

### Module Map
- `RimChat/AI/AIChatServiceAsync.cs`
  - Retryable RPG `HTTP 400 user input rejected / Param Incorrect` responses now log as retry warnings before reduced-context retry, instead of being recorded as terminal API errors.
- `RimChat/AI/LLMRpgApiResponse.cs`
  - Accepts legacy top-level `content` / `message` text fields and filters presentation-only actions such as `say`, so old payloads continue to render dialogue without noisy unknown-action logs.

## RPG Persona Template Refresh (v0.3.123)

### Module Map
- `Prompt/Default/RpgPrompts_Default.json`
  - Added configurable NPC persona bootstrap template fields, including the new RPG personality template and example text.
- `RimChat/Config/RpgPromptDefaultsConfig.cs`
  - Loads and normalizes the persona bootstrap system prompt, user template, output template, and example from the RPG default prompt config.
- `RimChat/DiplomacySystem/GameComponent_RPGManager.PersonaBootstrap.cs`
  - Persona bootstrap generation now uses the new `He/She is ...` template family, renders pronoun-aware instructions, accepts the refreshed sentence shape during normalization, and updates fallback persona text accordingly.
- `config.md`
  - Documents the new RPG persona template and example for per-pawn persona generation.
- `Api.md`
  - Updates the NPC persona bootstrap API docs to match the new output contract.

## Prompt Contract Split & Diplomacy Stabilization (v0.3.120)

### Module Map
- `Prompt/Default/SystemPrompt_Default.json`
  - Diplomacy defaults now use one response contract: natural-language reply first, then at most one raw `{"actions":[...]}` JSON object.
  - Diplomacy-specific decision policy, quest wording, graded exit rules, language guidance, and public-post limits are now isolated here.
- `Prompt/Default/RpgPrompts_Default.json`
  - RPG-only role-setting, action-reliability, compact format, opening objective, and turn-management defaults now live here instead of the diplomacy config.
- `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - Diplomacy and RPG prompt builders now resolve channel-specific policy/template sources instead of sharing one mixed template bucket.
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Diplomacy response-contract rendering now documents the raw `actions` array protocol, tighter `reject_request` boundaries, graded presence actions, and safer public-post usage.
- `RimChat/Config/SystemPromptConfig.cs`
  - Prompt policy schema bumped to `3`; diplomacy default response format, action metadata, and important rules now match the new contract.

## Prompt Action Gating & Vanilla Cooldowns (v0.3.117)

### Module Map
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Dynamic diplomacy action injection now hides `request_caravan`, `request_aid`, and `create_quest` when their fixed goodwill cost would drop projected goodwill below 0.
  - Matching hidden actions are also removed from the blocked-action hint section so they are not exposed in prompt text.
- `RimChat/DiplomacySystem/GameAIInterface.cs`
  - Request cooldowns now use vanilla-aligned values for diplomacy requests: aid = 1 day, caravan = 4 days.
- `RimChat/action_rules.txt`
  - Prompt-side action guidance now matches the updated vanilla-aligned cooldown timings.

## Diplomacy API Fixed Goodwill Costs (v0.3.116)

### Module Map
- `RimChat/AI/AIActionExecutor.cs`
  - Diplomacy-dialogue execution now applies fixed goodwill costs only after API success.
  - Keeps `send_gift` unchanged while routing `request_aid`, `request_caravan`, and `create_quest` through fixed-cost handling.
- `RimChat/DiplomacySystem/GameAIInterface.cs`
  - Added a dedicated success-only goodwill-cost applier for dialogue API actions.
  - Records the applied base/actual goodwill delta in dialogue action history.
- `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - Diplomacy dialogue now records richer memory descriptions for successful API actions, including fixed-cost text and quest issuance.
- `RimChat/Relation/DialogueGoodwillCost.cs`
  - Aligned caravan and aid fixed costs with vanilla values and added fixed quest-issuance cost support.
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Prompt rules now explicitly forbid using `adjust_goodwill` to duplicate system-managed API costs.

## Prompt Editor Localization Repair (v0.3.115)

### Module Map
- `RimChat/UI/PromptTemplateFieldLocalizer.cs`
  - Added stable prompt-field name to localization-key mapping for both legacy Chinese field names and normalized identifiers.
  - Provides safe translation fallback so unknown fields still show readable text instead of raw missing keys.
- `RimChat/UI/Dialog_FactionPromptEditor.cs`
  - Faction prompt template editor now resolves localized field labels and descriptions through the new localizer.
  - Preview rows now use localized labels and the collapsed hint is translated.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`
  - Added missing English keys for the prompt template editor field labels/descriptions and RPG prompt settings section labels/toggles.
- `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Added the missing generic `Save` key used by the prompt template editor button.

## Response Parsing & UI Lifecycle Fixes (v0.3.114)

### Module Map
- `RimChat/AI/AIJsonContentExtractor.cs`
  - Added a reusable JSON text extractor for tolerant model-response parsing.
- `RimChat/AI/AIChatService.cs`
  - Replaced brittle `IndexOf("\"content\":\"")` parsing with extractor-based parsing.
- `RimChat/AI/AIChatServiceAsync.cs`
  - Replaced brittle `IndexOf("\"content\":\"")` parsing with extractor-based parsing.
- `RimChat/UI/MainTabWindow_RimChat.cs`
  - Fixed goodwill animation event lifecycle by subscribing on `PreOpen` and unsubscribing on `PreClose`.
- `RimChat/Memory/LeaderMemoryManager.PersistenceHelpers.cs`
  - Added stronger save-name reflection fallback chain to avoid unstable `"Default"` fallback.

## Stability & Lifecycle Hardening (v0.3.113)

### Module Map
- `RimChat/AI/AIChatServiceAsync.cs`
  - Added periodic cleanup of terminal requests (10s interval + capped retention).
  - Added request context versioning and stale-callback guard.
  - Added `NotifyGameContextChanged(string reason)` entry for cross-save request invalidation.
- `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.cs`
  - On `StartedNewGame/LoadedGame`, notifies `AIChatServiceAsync` to cancel pending old-context requests.
- `RimChat/DiplomacySystem/DiplomacyConversationController.cs`
  - New controller layer between diplomacy UI and async service.
  - Owns request send/cancel and validates session/faction lifecycle before applying callback effects.
- `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - Main reply request now flows through `DiplomacyConversationController`.
  - Window close now cancels pending reply requests.
  - Extracted key layout metrics constants for top-level panel/title/faction-list layout.
- `RimChat/UI/Dialog_DiplomacyDialogue.Strategy.cs`
  - Strategy follow-up request now stores request id, validates live session context, and supports close-time cancellation.
- `RimChat/AI/AIActionExecutor.cs`
  - Replaced unsafe direct casts with safe numeric/string parameter parsing helpers.
  - Action type routing now uses centralized constants.
- `RimChat/AI/AIActionNames.cs`
  - New centralized action type constant definitions.
- `RimChat/DiplomacySystem/Social/SocialCircleService.cs`
  - Social-circle utility layer for category inference, dialogue keyword extraction, and structured post assembly.
- `RimChat/Memory/LeaderMemoryManager.cs`
  - Load-time cache warmup + runtime no-lazy-file-read behavior to avoid first-hit blocking I/O in gameplay.

## Prompt Policy V2 Unified Rollout (v0.3.110)

### Module Map
- `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - Added unified policy nodes for both diplomacy and RPG:
    - `decision_policy`
    - `turn_objective`
    - `topic_shift_rule`
    - `opening_objective` (RPG opening turn only)
  - Added dual-budget trimming engine:
    - node-level budgets (`environment/memory/actor_state/api_contract/...`)
    - global budget + trim-priority fallback
  - Added protected-node constraints so `fact_grounding` and `turn_objective` are never trimmed.
- `RimChat/Config/SystemPromptConfig.cs`
  - Added:
    - `PromptPolicySchemaVersion`
    - `PromptPolicy`
  - Wired `ExposeData/Clone/CopyFrom/InitializeMinimalDefaults`.
- `RimChat/Config/PromptPolicyConfig.cs`
  - Added Prompt Policy model:
    - global budget
    - node budgets
    - trim priority
    - summary budgets
    - intent-action mapping cooldown/threshold controls
    - schema-upgrade reset toggle
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Added legacy JSON read/write support for new `PromptTemplates` V2 fields and `PromptPolicy`.
  - Added schema-upgrade migration:
    - detects older `PromptPolicySchemaVersion`
    - resets custom prompt overrides to V2 defaults when enabled.
- `RimChat/UI/Dialog_RPGPawnDialogue.cs`
  - Removed first-turn user injection `"Initiate conversation with me."`.
  - RPG request now rebuilds system prompt per request; opening turn carries `phase:opening`.
- `RimChat/Memory/RpgNpcDialogueArchiveManager.cs`
  - Split unresolved-intent extraction from memory building.
  - Prompt memory block changed to summary-first with limited recent raw snippets.
- `RimChat/UI/Dialog_RPGPawnDialogue.Actions.cs`
  - Added intent-driven action mapping layer before fallback guard:
    - collaboration commitment
    - soft ending
    - strong reject
  - Kept existing no-action fallback as final safety layer.
- `RimChat/UI/Dialog_RPGPawnDialogue.ActionPolicies.cs`
  - Split RPG action normalization and fallback policy logic out of `Actions.cs`.
  - `TryGainMemory` now resolves through the layered RPG memory catalog and shows localized feedback labels.
- `RimChat/Memory/RpgMemoryCatalog.cs`
  - Centralizes the 28-entry RPG memory pool, legacy alias remapping, and positive-only auto-fallback selection.
- `RimChat/Config/RimChatSettings_Prompt.cs`
  - Added `PromptPolicy` editor section in Prompt Advanced settings.
- `RimChat/Config/RimChatSettings_PromptPolicy.cs`
  - Added visual editor for policy parameters:
    - budgets
    - node caps
    - trim priority list
    - intent-action mapping cooldown/threshold.
- `RimChat/Config/RimChatSettings_PromptTemplates.cs`
  - Added V2 template fields:
    - `DecisionPolicyTemplate`
    - `TurnObjectiveTemplate`
    - `TopicShiftRuleTemplate`.
- `Prompt/Default/SystemPrompt_Default.json`
  - Added V2 defaults:
    - `PromptPolicySchemaVersion = 3`
    - policy templates
    - `PromptPolicy` object.

## Existing NPC Persona Bootstrap On First Save Load (v0.3.109)

### Module Map
- `RimChat/DiplomacySystem/GameComponent_RPGManager.cs`
  - Converted to `partial` and wired bootstrap lifecycle hooks into:
    - `StartedNewGame` (mark completed for new saves),
    - `LoadedGame` (schedule bootstrap on legacy saves),
    - `ExposeData/PostLoadInit` (persist + restore bootstrap state).
- `RimChat/DiplomacySystem/GameComponent_RPGManager.PersonaBootstrap.cs`
  - Added one-time NPC persona bootstrap pipeline:
    - Collect existing humanlike pawn targets (map-spawned pawns + visible faction leaders),
    - Build personality-only compact profile context (exclude health/equipment/genes/needs),
    - Async serialized LLM generation with retry,
    - Enforce concise pronoun-aware persona output constraints,
    - Strict template normalization and fallback,
    - Writeback through existing `SetPawnPersonaPrompt` storage.
  - Added save-level state: `npcPersonaBootstrapCompleted` + `npcPersonaBootstrapVersion` (run once per save schema, with upgrade rerun support).
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Added `BuildPawnPersonaBootstrapProfile(Pawn)` to provide full RPG-style pawn/faction profile context for persona generation input.

## Social Circle Prompt Dedicated Entry + File-Only RimTalk Prompt Persistence (v0.3.106)

### Module Map
- `RimChat/Config/RimChatSettings_PromptSocialCircle.cs`
  - Added dedicated `Social Circle Prompt` editor section in Prompt advanced settings.
  - Centralizes editing of:
    - `PromptTemplates.SocialCircleActionRuleTemplate`
    - `publish_public_post` action `Description/Parameters/Requirement/IsEnabled`
  - Shows explicit persistence flow (`Prompt/Default` source + `Prompt/Custom` save target) in UI hint text.
- `RimChat/Config/RimChatSettings_Prompt.cs`
  - Added `SocialCirclePrompts` section entry to prompt navigation.
  - Wired variable insert/validation current-section routing to social-circle template text.
- `RimChat/Config/RimChatSettings_PromptTemplates.cs`
  - Removed duplicate social-circle template entry from generic `PromptTemplates` list to avoid split edit paths.
- `RimChat/Config/RimChatSettings.cs`
  - Added one-time migration flow: when `Prompt/Custom/RpgPrompts_Custom.json` is missing, legacy RimTalk prompt fields are read from ModSettings once and saved into custom prompt file.
- `RimChat/Config/RimChatSettings_RimTalkCompat.cs`
  - Removed direct ModSettings persistence for RimTalk prompt fields.
  - Added legacy one-time read helper (`TryLoadLegacyRimTalkCompatFromModSettings`).
- `RimChat/Config/RpgPromptCustomStore.cs`
  - Added RimTalk prompt compatibility fields to RPG custom prompt file schema.
  - Added `CustomConfigExists()` path helper.
- `RimChat/Config/PromptTextConstants.cs`
  - Added centralized defaults for `publish_public_post` action metadata.
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Switched `publish_public_post` migration/default backfill texts to `PromptTextConstants` constants.
- `1.6/Languages/*/Keyed/RimChat_Keys.xml`
  - Added CN/EN keys for dedicated social-circle prompt section labels and persistence guidance text.

## Social Incident Pool Expansion (v0.3.104)

### Module Map
- `RimChat/DiplomacySystem/Social/SocialEnums.cs`
  - Added social impact types for Core medium/low-threat incidents (`HeatWave`, `SolarFlare`, `Flashstorm`) while keeping legacy impact enum compatibility.
- `RimChat/DiplomacySystem/Social/SocialCircleService.cs`
  - Social extended-impact selection now includes a Core-only medium/low-threat incident pool.
  - Blight execution now maps to Core incident def `CropBlight`.
- `1.6/Languages/English/Keyed/RimChat_Keys.xml`
  - Added social impact narrative/result localization keys for `CropBlight`, `HeatWave`, `SolarFlare`, `Flashstorm`.
- `1.6/Languages/ChineseSimplified/Keyed/RimChat_Keys.xml`
  - Added Chinese localization keys for the same social impact narratives/results.

## Prompt Settings Full Exposure (v0.3.103)

### Module Map
- `RimChat/Config/RimChatSettings_RPG.cs`
  - RPG settings navigation now includes:
    - `RPG fallback templates`
    - `RPG API prompt templates`
- `RimChat/Config/RimChatSettings_RPGPromptEditors.cs`
  - Added full UI editors for RPG fallback/header/marker fields and `ApiActionPrompt` full/compact templates.
- `RimChat/Config/RpgPromptCustomStore.cs`
  - `Prompt/Custom/RpgPrompts_Custom.json` upgraded to full RPG prompt override schema.
  - Still custom-only persistence under `Prompt/Custom`.
- `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - RPG prompt assembly now reads settings-backed RPG fallback/API template values first.
- `RimChat/Config/RimChatSettings_PromptTemplates.cs`
  - Added `PromptTemplates` editor section in Prompt advanced settings.
  - Covers `FactGrounding/OutputLanguage/RoleFallback/NodeTemplate` fields.
- `RimChat/Config/RimChatSettings_Prompt.cs`
  - Global prompt section now includes `GlobalDialoguePrompt` editor.

## RPG Prompt Custom-Only Persistence (v0.3.101)

### Module Map
- `Prompt/Custom/RpgPrompts_Custom.json`
  - Added dedicated RPG prompt custom persistence file.
  - Stores only user overrides for role-setting/dialogue-style/format-constraint.
- `RimChat/Config/RimChatSettings_RPG.cs`
  - `Save RPG Prompts` now writes RPG prompt overrides into `Prompt/Custom/RpgPrompts_Custom.json`.
- `RimChat/Config/RimChatSettings.cs`
  - Removed RPG prompt text persistence from `ModSettings` (`Mod_*.xml`) Scribe fields.
  - On load, RPG prompt text now resolves strictly as:
    - `Prompt/Custom/RpgPrompts_Custom.json` (if present), else
    - `Prompt/Default/RpgPrompts_Default.json`.
- `RimChat/Config/RpgPromptCustomStore.cs`
  - Added load/save path resolver and JSON codec for RPG custom prompt file.
- `RimChat/Config/LegacyPromptModSettingsCleanup.cs` (v0.3.102)
  - Added one-time cleanup for legacy prompt fields in `Config/Mod_*.xml`.
  - Writes marker to `Prompt/Custom/legacy_prompt_modsettings_cleanup_v1.done` after cleanup.

## RPG Prompt JSON Externalization (v0.3.98)

### Module Map
- `Prompt/Default/RpgPrompts_Default.json`
  - Added authoritative default RPG prompt text bundle (role-setting/dialogue-style/format-constraint/RPG API action prompt text/fallback templates).
- `RimChat/Config/RpgPromptDefaultsConfig.cs`
  - Added structured config model + cached loader for `Prompt/Default/RpgPrompts_Default.json`.
  - Path strategy aligned with existing default prompt files (mod root -> assembly fallback -> fixed fallback path).
- `RimChat/Config/PromptTextConstants.cs`
  - `RpgRoleSettingDefault` / `RpgDialogueStyleDefault` / `RpgFormatConstraintDefault` now load from RPG default JSON.
- `RimChat/Prompting/RpgApiPromptTextBuilder.cs`
  - Full/compact RPG action-definition prompt text now rendered from RPG default JSON.
- `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - RPG fallback texts (`role setting fallback`, `format constraint header`, `compact fallback`, `reliability fallback`) now load from RPG default JSON.

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

## Prompt Template Externalization Module (v0.3.64)

### Module Map
- `RimChat/Config/PromptTemplateTextConfig.cs`
  - Added shared template config model for prompt text externalization.
  - Fields:
    - `Enabled`
    - `FactGroundingTemplate`
    - `OutputLanguageTemplate`
- `RimChat/Prompting/PromptTemplateRenderer.cs`
  - Added reusable `{{variable}}` placeholder renderer.
  - Unknown placeholders are preserved for debug visibility.
- `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - `fact_grounding` and `output_language` now render from `PromptTemplates` first.
  - Legacy hardcoded strings remain as fallback when templates are disabled/empty.
  - Supported shared variables:
    - `{{channel}}`, `{{mode}}`, `{{target_language}}`
    - `{{faction_name}}`, `{{initiator_name}}`, `{{target_name}}`
- `RimChat/Config/SystemPromptConfig.cs`
  - Added `PromptTemplates` on root system prompt config and wired `ExposeData/Clone/CopyFrom` lifecycle.
- `RimChat/Persistence/PromptConfigJsonCodec.cs`
  - Added `PromptTemplates` null-safe normalization.
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Legacy JSON serializer/parser now reads/writes `PromptTemplates` for fallback compatibility.
- `Prompt/Default/SystemPrompt_Default.json`
  - Added default `PromptTemplates` block.

## Prompt Template Externalization Expansion (v0.3.65)

### Module Map
- `RimChat/Config/PromptTemplateTextConfig.cs`
  - Expanded `PromptTemplates` with:
    - `DiplomacyFallbackRoleTemplate`
- `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - Diplomacy fallback role text now uses `DiplomacyFallbackRoleTemplate`.
  - RPG role setting, format constraint, reliability, and opening objective now resolve from `Prompt/Default/RpgPrompts_Default.json`.
  - All changes keep legacy hardcoded text as fallback for backward compatibility.
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Legacy JSON serializer/parser now reads/writes the expanded `PromptTemplates` fields.
- `Prompt/Default/SystemPrompt_Default.json`
  - Added default values for newly expanded template fields.

## Prompt Node Wrapper Templates (v0.3.66)

### Module Map
- `RimChat/Config/PromptTemplateTextConfig.cs`
  - Added wrapper-template fields for dynamic node sections:
    - `ApiLimitsNodeTemplate`
    - `QuestGuidanceNodeTemplate`
    - `ResponseContractNodeTemplate`
- `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - Added wrapper render stage for diplomacy nodes:
    - `api_limits`
    - `quest_guidance`
    - `response_contract`
  - Default placeholders:
    - `{{api_limits_body}}`
    - `{{quest_guidance_body}}`
    - `{{response_contract_body}}`
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Legacy JSON serializer/parser now reads/writes node-wrapper template fields.
- `Prompt/Default/SystemPrompt_Default.json`
  - Added default wrapper templates preserving previous output behavior.

## Social Circle Prompt Rule + RPG Pawn Scope Fix (v0.3.105)

### Module Map
- `RimChat/Config/RimChatSettings_RPG.cs`
  - `GetEditableRpgPersonaPawns()` now reads only player-home-map spawned colony pawns (`FreeColonistsSpawned`, `PrisonersOfColonySpawned`, `SlavesOfColonySpawned`).
  - Prevents world/temporary pawns from polluting RPG persona editor list.
- `RimChat/Config/PromptTemplateTextConfig.cs`
  - Added `SocialCircleActionRuleTemplate`.
- `RimChat/Config/RimChatSettings_PromptTemplates.cs`
  - Added editable UI entry for `SocialCircleActionRuleTemplate`.
- `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - Added diplomacy instruction node `social_circle_action_rule`.
  - Node renders from `PromptTemplates.SocialCircleActionRuleTemplate` when enabled.
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Legacy JSON serialize/parse/default-migration now includes `SocialCircleActionRuleTemplate`.
- `Prompt/Default/SystemPrompt_Default.json`
  - Added default `SocialCircleActionRuleTemplate` seed text.

## Prompt Text Dedup Cleanup (v0.3.67)

### Module Map
- `RimChat/Config/PromptTemplateTextConfig.cs`
  - Removed duplicated long template text defaults from constructor.
  - Kept only wrapper placeholder defaults (`{{api_limits_body}}`, `{{quest_guidance_body}}`, `{{response_contract_body}}`).
- `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - Replaced duplicated long fallback text with concise minimal fallback guidance for template-controlled sections.
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Removed deprecated `AppendFactGroundingGuidance` multiline StringBuilder block after template migration.
- `Prompt/Default/SystemPrompt_Default.json`
  - Remains the authoritative default source for long prompt template text.

## Prompt Text Constants (v0.3.68)

### Module Map
- `RimChat/Config/PromptTextConstants.cs`
  - Added a centralized single source for repeated prompt literals.
- `RimChat/Config/RimChatSettings.cs`
  - RPG default prompt strings now reference `PromptTextConstants` in all three lifecycle paths (init/Scribe fallback/migration fallback).
- `RimChat/Config/SystemPromptConfig.cs`
  - Reused centralized action prompt descriptions in minimal-default API action setup.
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Reused centralized action prompt descriptions/metadata in config migration paths.

## Prompt Section Constants (v0.3.69)

### Module Map
- `RimChat/Config/PromptTextConstants.cs`
  - Added shared constants for response-contract section labels and common prompt headers.
- `RimChat/Persistence/PromptPersistenceService.cs`
  - `AppendSimpleConfig` / `AppendAdvancedConfig` now use shared constants for duplicated section text.

## Prompt Template Backfill Fix (v0.3.70)

### Module Map
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Added `EnsurePromptTemplateDefaults(...)` to backfill blank `PromptTemplates` fields from default template config during load.
  - Added `AssignIfMissing(...)` helper and integrated migration-save path.
- `Prompt/Default/SystemPrompt_Default.json`
  - Remains the source for default template text used by runtime backfill.

## Prompt Build Decomposition Module (v0.3.61)

### Module Map
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Prompt build entry points now delegate orchestration to dedicated builder components.
  - Config read/write path now routes through `PromptConfigStore`.
- `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - Added internal core wrappers for hierarchical diplomacy/RPG build paths used by builders.
- `RimChat/Persistence/PromptConfigStore.cs`
  - Centralized prompt config file existence/read/write operations.
- `RimChat/Prompting/Builders/DiplomacyPromptBuilder.cs`
  - Dedicated diplomacy prompt build orchestrator (behavior-preserving delegation).
- `RimChat/Prompting/Builders/RpgPromptBuilder.cs`
  - Dedicated RPG prompt build orchestrator (behavior-preserving delegation).

## Prompt JSON Codec Module (v0.3.62)

### Module Map
- `RimChat/Persistence/PromptConfigJsonCodec.cs`
  - Added typed JSON encode/decode for `SystemPromptConfig` with null-safe normalization.
- `RimChat/Persistence/PromptPersistenceService.cs`
  - `SerializeConfigToJson(...)` now uses typed codec first and falls back to legacy serializer on failure.
  - `ParseJsonToConfigInternal(...)` now uses typed codec first and falls back to legacy parser on failure.

## Prompt JSON Runtime Fix (v0.3.63)

### Module Map
- `RimChat/Persistence/PromptConfigJsonCodec.cs`
  - Replaced `System.Web.Script.Serialization.JavaScriptSerializer` with `UnityEngine.JsonUtility` to avoid runtime type-load issues in RimWorld.
- `RimChat/Config/SystemPromptConfig.cs`
  - Added `[Serializable]` to prompt-config model classes for `JsonUtility` compatibility.
- `RimChat/Config/EventIntelPromptConfig.cs`
  - Added `[Serializable]` for nested environment event-intel config compatibility.
- `RimChat/RimChat.csproj`
  - Removed `System.Web.Extensions` dependency and added `UnityEngine.JSONSerializeModule` reference.

## RimTalk Compatibility Legacy Note (v0.4.11 archived)

### Module Map (Current Mainline)
- `RimChat/Persistence/PromptPersistenceService.Hierarchical.cs`
  - Owns diplomacy/RPG compatibility prompt assembly and active-entry composition.
  - Rendering is strict Scriban via `PromptTemplateRenderer.RenderOrThrow(...)`.
  - Render failures are hard-fail (`PromptRenderException`), no raw-template passthrough fallback.
- `RimChat/DiplomacySystem/GameComponent_RPGManager.PersonaBootstrap.cs`
  - Persona bootstrap/persona-copy path uses strict Scriban rendering and structured failure on render/empty output.
- `RimChat/Config/RimChatSettings_RimTalkTab.cs`
  - RimTalk template editors expose realtime Scriban diagnostics (error code + line/column/unknown variable status).
- `RimChat/Config/RimChatSettings_RimTalkCompat.cs`
  - Maintains persisted compatibility fields with old-save/old-prompt-file compatible defaults.

### Archived Note
- The historical `RimChat/Compat/RimTalkCompatBridge*` files are removed from the active codebase.
- Any old docs mentioning bridge runtime rendering behavior should be treated as historical-only and non-authoritative.

### RimTalk Global Keys
- `rimchat_last_session_summary`
- `rimchat_last_diplomacy_summary`
- `rimchat_last_rpg_summary`
- `rimchat_recent_session_summaries`

为 RimWorld 带来 AI 驱动的派系外交系统！

## 功能特性

- **AI 控制派系智能对话系统**: 与 AI 派系领袖进行外交对话，请求商队，触发袭击，接取任务
- **RPG风格人物对话**: 与 AI NPC进行沉浸式对话，触发事件，谈情说爱
- **NPC 主动对话系统**: 在线状态下派系可主动发信；支持忙碌延迟队列、因果触发与会话冷却门控
- **对话 Token 用量可视化**: API配置页底部显示最近一次外交/RPG对话 token 使用量与负载分档（低/中/高），并在服务端 usage 异常时自动回退估算
- **RPG-外交双向记忆链路**: 离图摘要写入派系记忆、外交会话摘要反哺 RPG 提示词，提升长期世界状态感知

## RPG Dialogue Tuning (v0.3.137)

- `ExitDialogueCooldown` now blocks re-chat for `1` in-game day (`60000` ticks).
- RPG memory fallback now uses a single `80%` roll once NPC dialogue reaches `5` rounds; success appends `TryGainMemory`.
- `TryGainMemory` now targets the new 28-entry RimChat memory pool; localized feedback uses the memory label, legacy tokens are remapped, and ordinary auto-fallback stays on the positive progression set instead of the rare core memories.
- Visible NPC dialogue is normalized to one line before display/history/trace persistence, so stray line breaks no longer leak into the RPG window.
- Oversized RPG dialogue text now supports per-message paging after typing completes, while history navigation keeps working independently.
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
  - Interface: `RegisterTurn(..., string sessionId = null)`, `TryConsumeRecentForExit(...)`。
- `RimChat/Patches/PawnExitMapPatch_RpgMemory.cs`
  - Responsibility: Patch `Pawn.ExitMap(bool, Rot4)`，在严格条件下触发离图摘要写入。
- `RimChat/UI/Dialog_DiplomacyDialogue.cs`
  - Responsibility: 对话窗口关闭时按“新增消息基线”触发外交会话摘要（仅一次）。
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Responsibility: RPG 系统提示词注入 Dynamic Faction Memory Block（人格提示词后、API规则前）；外交提示词可读跨通道摘要。
- `RimChat/Memory/LeaderMemoryManager.cs` + `RimChat/Memory/LeaderMemoryJsonCodec.cs`
  - Responsibility: 跨通道摘要持久化、上限裁剪、旧字段兼容解析与 JSON 字段映射修正；存档接管时会话历史回填与记忆基线快照初始化。
  - Interface: `OnNewGame()` 初始化新档基线记忆，`OnAfterGameLoad(IEnumerable<FactionDialogueSession>)` 回填读档前会话历史并补齐基础记忆。
- `RimChat/Memory/RpgNpcDialogueArchive.cs` + `RimChat/Memory/RpgNpcDialogueArchiveJsonCodec.cs` + `RimChat/Memory/RpgNpcDialogueArchiveManager*.cs`
  - Responsibility: RPG 对话按 NPC 独立外部文件持久化（每 NPC 一份），主存储改为 session 粒度；仅保留最近一段 `turnCount>=2` 会话全文，其他“已结束会话”压缩为一句摘要（严格 LLM，失败保留原文并重试）。
  - Storage: `Prompt/NPC/<saveName>/rpg_npc_dialogues/npc_<pawnId>.json`（写盘为 `sessions`；旧版顶层 `turns` 自动兼容读取并增量迁移）。

### Persistence Notes (v0.3.31)
- `LeaderMemory` JSON 现已补齐核心字段：`lastDecayCheckTick`、`playerRelationValues`、`FactionMemoryEntry.firstContactTick/lastMentionedTick/relationHistory`。
- 领袖记忆写盘不再额外裁切到 50 条；仅受内存上限控制（当前 200 条）。

## Environment Prompt Module (v0.3.25)

### Module Map
- `RimChat/Config/SystemPromptConfig.cs`
  - Responsibility: environment prompt root data model and default seed (`Worldview`, `EnvironmentContextSwitches`, `SceneSystem`, `SceneEntries`, `RpgSceneParamSwitches`, `EventIntelPrompt`).
  - Interface: runtime config persisted at `Mods/RimChat/Prompt/Custom/system_prompt_config.json` (with fallback), default seed loaded from bundled `Prompt/Default/SystemPrompt_Default.json`.
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
  - Responsibility: Diplomacy Dialogue tab environment section UI (worldview, environment parameter toggles, event memory switches, scene CRUD, channel toggles, RPG deep-param switches, preview).
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
- `RimChat/Memory/RpgMemoryCatalog.cs`
  - Responsibility: shared RPG memory catalog for prompt examples, alias compatibility, and auto-fallback selection.
  - Interface: `ResolveRequestedThoughtDef(...)`, `ResolveAutoDefName(int rounds)`, `BuildPromptExamplesText()`.
- `RimChat/UI/Dialog_RPGPawnDialogue.ActionPolicies.cs`
  - Responsibility: RPG action normalization, intent mapping, exit fallback, and memory fallback orchestration.
  - Interface: partial policy layer consumed by `Dialog_RPGPawnDialogue`.
- `RimChat/UI/Dialog_RPGPawnDialogue.FeedbackOverlay.cs`
  - Responsibility: render fixed-duration RPG floating subtitles anchored beside the target portrait.
  - Interface: partial overlay layer consumed by `Dialog_RPGPawnDialogue` and action policy feedback producers.
- `RimChat/UI/Dialog_RPGPawnDialogue.Portraits.cs`
  - Responsibility: centralize PawnRPG portrait layout rectangles for portrait drawing and overlay anchoring.
  - Interface: portrait rect helpers consumed by `Dialog_RPGPawnDialogue` partial UI layers.
- `RimChat/UI/Dialog_RPGPawnDialogue.TextPaging.cs`
  - Responsibility: paginate oversized RPG dialogue text and render message-level/history-level navigation controls.
  - Interface: partial paging layer consumed by `Dialog_RPGPawnDialogue`.
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
  - UI note: compact presence badges now reserve extra height for Tiny-font Chinese labels to avoid clipping.
  - Interface: handles AI actions `exit_dialogue`, `go_offline`, `set_dnd` inside dialogue execution flow.
- `RimChat/UI/MainTabWindow_RimChat.cs`
  - UI note: Tiny badges/subtitles/relation labels now use taller centered label rects to reduce Chinese glyph clipping in the main hub.
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
  - Responsibility: Pawn Dialogue tab UI for selecting colony pawns, editing independent persona prompts, and forcing PawnRPG proactive debug trigger in RPG Pawn persona section.
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
  - Responsibility: social post/news data model, fact-seed generation, LLM JSON parsing, and intent-action resolver.
  - Dependencies: `RimWorld.Faction`, `RimWorld.Planet`, `Verse.Scribe`, `AIActionExecutor`, `RimChatSettings`.
- `RimChat/DiplomacySystem/GameComponent_DiplomacyManager.SocialCircle.cs`
  - Responsibility: scheduler, enqueue pipeline, unread tracking, manual force-generate, and dialogue keyword ingress.
  - Interface: `EnqueuePublicPost`, `ForceGeneratePublicPost`, `GetSocialPosts`, `GetUnreadSocialPostCount`, `MarkSocialPostsRead`.
- `RimChat/UI/Dialog_DiplomacyDialogue.SocialCircle.cs`
  - Responsibility: explicit AI action `publish_public_post` handling and keyword fallback post creation.
- `RimChat/UI/Dialog_DiplomacyDialogue.SocialCircleView.cs`
  - Responsibility: diplomacy-window social tab UI (filters, structured news feed cards, unread mark-as-read, toast feedback).
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

### Defaults
- Social post interval: `5-7` days (`SocialPostIntervalMinDays` / `SocialPostIntervalMaxDays`)
- Auto action execution from intent: off by default (`EnableSocialCircleAutoActions = false`)

## Strategy Suggestion Module (v0.3.12)

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
  - Responsibility: net-goodwill-drop trigger wiring + fixed layout slots for strategy/input + compact reinitiate button.
  - Interface: strategy suggestions shown only for next send cycle after net goodwill decrease.
- `RimChat/Persistence/PromptPersistenceService.cs`
  - Responsibility: runtime prompt guidance for optional `strategy_suggestions` output contract.

### Public Interfaces Added
- LLM JSON optional field:
  - `strategy_suggestions` (exact 3 items)
  - item fields: `strategy_name`, `reason`, `content`
- Parsed response field:
  - `ParsedResponse.StrategySuggestions`
- Runtime behavior:
  - Strategy ability is gated per session by negotiator Social skill: `<5 locked`, `5-9 => 1 use`, `10-14 => 2 uses`, `>=15 => 3 uses`.
  - If `strategy_suggestions` is missing/invalid while ability is available, client sends one additional strategy-only LLM request (non-blocking).
  - If the follow-up response is still invalid, client primes a local deterministic fallback set (localized labels + fallback replies), and does not parse narrative prose into strategy buttons.
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
  - Responsibility: causal trigger from significant goodwill shifts (`|delta| >= 10`) and hostile-warning proxy tagging; natural goodwill decreases are filtered out and do not enter proactive dialogue channels.
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
  - Responsibility: PawnRPG proactive trigger context, delayed queue model, per-NPC cooldown anchor, per-faction threat-edge state persistence, protagonist list persistence entry model.
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
  - Responsibility change: keeps legacy goodwill trigger; additionally reports goodwill-shift trigger into PawnRPG proactive channel, except natural goodwill decreases (filtered).
- `RimChat/Patches/UIRootPlayPatch_NpcDialogue.cs`
  - Responsibility change: left-click cadence now reports to both legacy and PawnRPG proactive channels.
- `RimChat/Config/RimChatSettings_NpcPush.cs`
  - Responsibility change: keeps legacy debug trigger button, adds PawnRPG debug force-trigger button, and provides protagonist list management UI with cap setting.

### Public Interfaces Added
- `GameComponent_PawnRpgDialoguePushManager.RegisterTradeCompletedTrigger(Faction faction, int soldCount, int boughtCount)`
- `GameComponent_PawnRpgDialoguePushManager.RegisterGoodwillShiftTrigger(Faction faction, int goodwillDelta, string reason, bool likelyHostile)`
- `GameComponent_PawnRpgDialoguePushManager.RegisterThreatStateTrigger(Faction faction, bool hasHive, bool hasHostiles)`
- `GameComponent_PawnRpgDialoguePushManager.RegisterPlayerLeftClick()`
- `GameComponent_PawnRpgDialoguePushManager.DebugForcePawnRpgProactiveDialogue()`
- `GameComponent_PawnRpgDialoguePushManager.GetRpgProactiveProtagonists()`
- `GameComponent_PawnRpgDialoguePushManager.ContainsRpgProactiveProtagonist(Pawn pawn)`
- `GameComponent_PawnRpgDialoguePushManager.TryAddRpgProactiveProtagonist(Pawn pawn)`
- `GameComponent_PawnRpgDialoguePushManager.RemoveRpgProactiveProtagonist(Pawn pawn)`
- `GameComponent_PawnRpgDialoguePushManager.ClearRpgProactiveProtagonists()`
- `GameComponent_PawnRpgDialoguePushManager.GetRpgProactiveProtagonistCap()` / `SetRpgProactiveProtagonistCap(int value)`
- `GameComponent_PawnRpgDialoguePushManager.GetEligibleRpgProactiveTargetsOnMap(Map map)`
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
- Protagonist policy: proactive target candidates are restricted to configured protagonists only; empty list disables proactive delivery by design.







## Prompt Authoring Updates (v0.3.34)
- Prompt section navigation now preserves scroll position.
- API action list and description editor use independent scroll states.
- Added prompt variable picker with click-to-insert `{{variable}}` tokens.
- Added per-section variable validation and environment scene render diagnostics in preview.



