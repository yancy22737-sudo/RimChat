# GitNexus C# Relation Audit

- Repository: RimChat
- RunAt: 2026-04-03 02:25:47
- Samples: 8

## Summary

- CALLS edges found: 8 / 8
- CALLS missing: 0
- Expected IMPORTS missing: 0
- Same-namespace no-import edges: 0
- Process zero coverage: 0
- Global STEP_IN_PROCESS edges: 46310
- Source evidence missing: 0
- Strict gate: PASS

## Strict Gate Checks

- [PASS] CALLS sample coverage: missing=0 (expected 0)
- [PASS] SocialNewsPromptBuilder Edge/using: value=1 (threshold<=3)
- [PASS] Process zero sample count: value=0 (threshold<=2)
- [PASS] Global STEP_IN_PROCESS edges: value=46310 (threshold>=2201)

## Sample Matrix

| Id | Caller | Callee | EvidenceHits | CALLS | IMPORTS | PROCESS | Verdict |
| --- | --- | --- | --- | --- | --- | --- | --- |
| S1 | RimChat/DiplomacySystem/ItemAirdropPaymentResolver.cs::Resolve | RimChat/DiplomacySystem/ThingDefResolver.cs::BuildMatchRequest | 1 | 1 | 1 | 16 | OK |
| S2 | RimChat/DiplomacySystem/ItemAirdropPaymentResolver.cs::Resolve | RimChat/DiplomacySystem/ThingDefMatchEngine.cs::ResolveSingle | 1 | 1 | 1 | 16 | OK |
| S3 | RimChat/DiplomacySystem/Social/SocialNewsPromptBuilder.cs::BuildVariables | RimChat/DiplomacySystem/Social/SocialCircleService.cs::GetCategoryLabel | 2 | 1 | 1 | 3 | OK |
| S4 | RimChat/DiplomacySystem/Social/SocialNewsPromptBuilder.cs::BuildPromptInputPayload | RimChat/DiplomacySystem/Social/SocialCircleService.cs::ResolveDisplayLabel | 3 | 1 | 1 | 16 | OK |
| S5 | RimChat/DiplomacySystem/Social/SocialNewsPromptBuilder.cs::BuildMessages | RimChat/Persistence/DialogueScenarioContext.cs::CreateDiplomacy | 1 | 1 | 1 | 16 | OK |
| S6 | RimChat/DiplomacySystem/Social/SocialNewsPromptBuilder.cs::BuildMessages | RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs::BuildUnifiedChannelSystemPrompt | 1 | 1 | 1 | 16 | OK |
| S7 | RimChat/Prompting/ScribanPromptEngine.cs::RenderOrThrow | RimChat/Prompting/PromptTemplateBlockRegistry.cs::TryGetReason | 1 | 1 | 1 | 64 | OK |
| S8 | RimChat/DiplomacySystem/ThingDefResolver.cs::BuildMatchRequest | RimChat/DiplomacySystem/ThingDefMatchEngine.cs::ExtractSemanticTokens | 1 | 1 | 1 | 2 | OK |

## Import Overlink Check

| File | RimChat using count | IMPORT edges | Edge/using | Suspect overlink |
| --- | --- | --- | --- | --- |
| RimChat/DiplomacySystem/Social/SocialNewsPromptBuilder.cs | 4 | 4 | 1 | False |
| RimChat/DiplomacySystem/ItemAirdropPaymentResolver.cs | 0 | 3 | 0 | False |

## Rule Of Thumb

- Treat CALLS as primary truth for C# relation checks.
- Treat IMPORTS as auxiliary only; same-namespace direct references often have no import edge.
- If IMPORTS is high relative to using RimChat.*, namespace-wide overlink is likely.
- If a symbol has PROCESS=0, do not infer that the call chain is absent.
