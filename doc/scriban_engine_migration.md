# RimChat Scriban Engine Migration (Step 1)

## Breaking Baseline

- This migration is **breaking by design**.
- Prompt rendering is now unified to `IScribanPromptEngine.RenderOrThrow(...)`.
- Runtime fallback to raw template text is forbidden for prompt rendering failures.
- Legacy bridge runtime rendering path is removed from active runtime call chain.

## Variable Namespace Rules

- Bare variables are forbidden.
- Every variable must be under one of:
  - `ctx.*`
  - `pawn.*`
  - `world.*`
  - `dialogue.*`
  - `system.*`
- Unknown variables are rejected before render.
- Null object access during render is treated as hard error.
- `pawn.*` is projected via `PromptRenderProjection` into nested `ScriptObject`.
- `PromptRenderContext.SetValue(...)` enforces namespace path (`ctx/pawn/world/dialogue/system` only).
- Null pawn/object access in template expressions is not tolerated; strict runtime throws immediately.

## Canonical Variable Catalog

The current namespaced catalog is:

`ctx.channel`, `ctx.mode`

`pawn.initiator`, `pawn.initiator.name`, `pawn.initiator.profile`, `pawn.target`, `pawn.target.name`, `pawn.target.profile`, `pawn.player.profile`, `pawn.player.royalty_summary`, `pawn.profile`, `pawn.personality`, `pawn.relation.kinship`, `pawn.relation.romance_state`, `pawn.pronouns.subject`, `pawn.pronouns.object`, `pawn.pronouns.possessive`, `pawn.pronouns.subject_lower`, `pawn.pronouns.be_verb`, `pawn.pronouns.seek_verb`, `pawn.speaker.kind`, `pawn.speaker.default_sound`, `pawn.speaker.animal_sound`, `pawn.speaker.baby_sound`, `pawn.speaker.mechanoid_sound`

`world.faction.name`, `world.scene_tags`, `world.environment_params`, `world.recent_world_events`, `world.colony_status`, `world.colony_factions`, `world.current_faction_profile`, `world.faction_settlement_summary`, `world.social.origin_type`, `world.social.category`, `world.social.source_faction`, `world.social.target_faction`, `world.social.source_label`, `world.social.credibility_label`, `world.social.credibility_value`, `world.social.fact_lines`

`dialogue.summary`, `dialogue.intent_hint`, `dialogue.primary_objective`, `dialogue.optional_followup`, `dialogue.latest_unresolved_intent`, `dialogue.topic_shift_rule`, `dialogue.api_limits_body`, `dialogue.quest_guidance_body`, `dialogue.response_contract_body`, `dialogue.guidance`, `dialogue.template_line`, `dialogue.example_line`, `dialogue.examples`, `dialogue.action_names`

`system.target_language`, `system.game_language`, `system.punctuation.open_paren`, `system.punctuation.close_paren`

## Legacy-to-Namespace Migration

Auto-rewriter maps legacy placeholders to namespaced variables.

Examples:

- `scene_tags` -> `world.scene_tags`
- `environment_params` -> `world.environment_params`
- `channel` -> `ctx.channel`
- `target_language` -> `system.target_language`
- `initiator_name` -> `pawn.initiator.name`
- `target_name` -> `pawn.target.name`

Post-rewrite validation is mandatory:

1. Rewrite template text with fixed mapping table.
2. Compile/validate with Scriban immediately.
3. If validation fails, mark template as `Blocked`.
4. Throw `PromptRenderException` with structured diagnostics.

## Error Code Table

| Error Code | Enum | Meaning |
| --- | --- | --- |
| 1000 | `ParseError` | Scriban parse failure |
| 1100 | `RuntimeError` | Generic runtime render failure |
| 1101 | `UnknownVariable` | Variable not in allowed namespaced catalog |
| 1102 | `NullObjectAccess` | Runtime null member access |
| 1200 | `TemplateBlocked` | Migration validation failed and template blocked |
| 1201 | `TemplateMissing` | Required template text missing in strict mode |

## Exception Contract

All prompt render failures must throw `PromptRenderException`.

Required diagnostic payload:

- `TemplateID`
- `Channel`
- `Line`
- `Column`
- `ErrorCode`

No silent fallback, no raw-template passthrough.

## Runtime Notes

- Scene-layer templates now render via Scriban strict engine (`RenderOrThrow`) instead of regex replacement.
- Image caption template no longer falls back to built-in default text; missing/empty template is `TemplateMissing(1201)`.
- Active settings variable browser now reads from local namespaced variable catalog (no runtime bridge variable scan).
- Prompt settings editor now shows live Scriban compile diagnostics and keeps manual validate as explicit action.
- Prompt settings provides migration-result view (rewritten/blocked template list + blocked reason).
- `RimTalkCompatBridge` source/runtime path is physically removed from RimChat.
- Scriban engine now includes LRU compiled-template cache and periodic telemetry log output.
- API debug observability window now displays Scriban cache hit-rate and average parse/render latency.

## Acceptance Loop Record (2026-03-15, UTC+08:00)

| Scope | Method | Result | Evidence |
| --- | --- | --- | --- |
| Build gate | Run `build.ps1` (compile + deploy) | PASS | Build output: `0 errors`, `1 warning` (obsolete `Translate` API in `DiplomacyNotificationManager.cs`) |
| Persona strict render chain | Static regression grep on persona bootstrap path | PASS | `RenderPersonaCopyTemplateOrThrow` and `throw BuildPersonaCopyRenderException(...)` present; persona-copy render path no longer catches `PromptRenderException` silently |
| RimTalk realtime diagnostics | Static regression grep on RimTalk editors | PASS | `DrawRimTalkTemplateValidationStatus(...)` is wired in entry-content editor, channel-template editor, and persona-copy template editor |
| Historical template migration regression | Validate migration entrypoints still present | PASS | `NormalizePersonaCopyTemplateToStrictScriban(...)`, `GetLastSchemaRewriteResult()`, and `Dialog_PromptMigrationResult` are all present and still wired from settings UI |
| In-game four-channel smoke | Attempted automated launch `RimWorldWin64.exe -batchmode -nographics -quicktest` | BLOCKED (automation) | Process timed out in non-interactive environment; no deterministic four-channel interaction evidence can be collected headlessly |
