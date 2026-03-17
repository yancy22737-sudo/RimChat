# Prompt Variable Gap Report

## Scope

- Reviewed defaults:
  - `Prompt/Default/PromptSectionCatalog_Default.json`
  - `Prompt/Default/SystemPrompt_Default.json`
  - `Prompt/Default/DiplomacyDialoguePrompt_Default.json`
  - `Prompt/Default/SocialCirclePrompt_Default.json`
- Policy:
  - Existing namespaced variables were reused directly.
  - Missing variables are recorded here only and were not implemented in code.

## Gaps

| Source prompt | Original semantic | Suggested variable path | Suggested provider | Why not migrated now |
|---|---|---|---|---|
| `SystemPrompt_Default.json` `GlobalSystemPrompt` | Faction relation band such as Ally/Friend/Neutral/Hostile | `world.faction.relation_band` | `RimChatCoreVariableProvider` | Runtime currently exposes faction names, profiles, and API limits, but not a structured relation-band token. |
| `SystemPrompt_Default.json` `GlobalSystemPrompt` | Leader traits used for diplomacy decisions | `pawn.target.traits_summary` or `world.faction.leader_traits` | `RimChatCoreVariableProvider` | Current provider exposes profile text, not a dedicated trait summary field that templates can reliably compose. |
| `SystemPrompt_Default.json` `GlobalSystemPrompt` | Faction ideology summary | `world.faction.ideology_summary` | `RimChatCoreVariableProvider` | No stable ideology variable is registered in the current prompt variable catalog. |
| `SystemPrompt_Default.json` `GlobalSystemPrompt` | Faction technology level / lore tech tier | `world.faction.tech_level` | `RimChatCoreVariableProvider` | The prompt system has no structured tech-level variable even though the old prose references it. |
| `SocialCirclePrompt_Default.json` `SocialCircleActionRuleTemplate` | Current diplomacy stance as a structured token | `world.social.diplomacy_stance` | `RimChatCoreVariableProvider` | Social templates currently have category, source, credibility, and fact lines, but not a dedicated stance field. |

## Notes

- `world.social.*`, `dialogue.summary`, `dialogue.intent_hint`, `system.game_language`, `ctx.*`, `pawn.*`, and existing `world.*` variables were kept on the current provider system.
- These gaps were intentionally left as documentation-only reminders to avoid introducing guessed provider semantics in this migration.
