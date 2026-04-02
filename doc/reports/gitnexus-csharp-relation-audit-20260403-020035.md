# GitNexus C# Relation Audit

- Repository: RimChat
- RunAt: 2026-04-03 02:01:06
- Samples: 8

## Summary

- CALLS edges found: 8 / 8
- CALLS missing: 0
- Expected IMPORTS missing: 2
- Same-namespace no-import edges: 6
- Process zero coverage: 7
- Global STEP_IN_PROCESS edges: 3699
- Source evidence missing: 0
- Strict gate: FAIL

## Strict Gate Checks

- [PASS] CALLS sample coverage: missing=0 (expected 0)
- [PASS] SocialNewsPromptBuilder Edge/using: value=0 (threshold<=3)
- [FAIL] Process zero sample count: value=7 (threshold<=2)
- [PASS] Global STEP_IN_PROCESS edges: value=3699 (threshold>=2201)

## Sample Matrix

| Id | Caller | Callee | EvidenceHits | CALLS | IMPORTS | PROCESS | Verdict |
| --- | --- | --- | --- | --- | --- | --- | --- |
| S1 | RimChat/DiplomacySystem/ItemAirdropPaymentResolver.cs::Resolve | RimChat/DiplomacySystem/ThingDefResolver.cs::BuildMatchRequest | 1 | 1 | 0 | 0 | SAME_NAMESPACE_NO_IMPORT_EDGE |
| S2 | RimChat/DiplomacySystem/ItemAirdropPaymentResolver.cs::Resolve | RimChat/DiplomacySystem/ThingDefMatchEngine.cs::ResolveSingle | 1 | 1 | 0 | 0 | SAME_NAMESPACE_NO_IMPORT_EDGE |
| S3 | RimChat/DiplomacySystem/Social/SocialNewsPromptBuilder.cs::BuildVariables | RimChat/DiplomacySystem/Social/SocialCircleService.cs::GetCategoryLabel | 2 | 1 | 0 | 0 | SAME_NAMESPACE_NO_IMPORT_EDGE |
| S4 | RimChat/DiplomacySystem/Social/SocialNewsPromptBuilder.cs::BuildPromptInputPayload | RimChat/DiplomacySystem/Social/SocialCircleService.cs::ResolveDisplayLabel | 3 | 1 | 0 | 0 | SAME_NAMESPACE_NO_IMPORT_EDGE |
| S5 | RimChat/DiplomacySystem/Social/SocialNewsPromptBuilder.cs::BuildMessages | RimChat/Persistence/DialogueScenarioContext.cs::CreateDiplomacy | 1 | 1 | 0 | 0 | IMPORT_EDGE_MISSING |
| S6 | RimChat/DiplomacySystem/Social/SocialNewsPromptBuilder.cs::BuildMessages | RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs::BuildUnifiedChannelSystemPrompt | 1 | 1 | 0 | 0 | IMPORT_EDGE_MISSING |
| S7 | RimChat/Prompting/ScribanPromptEngine.cs::RenderOrThrow | RimChat/Prompting/PromptTemplateBlockRegistry.cs::TryGetReason | 1 | 1 | 0 | 0 | SAME_NAMESPACE_NO_IMPORT_EDGE |
| S8 | RimChat/DiplomacySystem/ThingDefResolver.cs::BuildMatchRequest | RimChat/DiplomacySystem/ThingDefMatchEngine.cs::ExtractSemanticTokens | 1 | 1 | 0 | 2 | SAME_NAMESPACE_NO_IMPORT_EDGE |

## Import Overlink Check

| File | RimChat using count | IMPORT edges | Edge/using | Suspect overlink |
| --- | --- | --- | --- | --- |
| RimChat/DiplomacySystem/Social/SocialNewsPromptBuilder.cs | 4 | 0 | 0 | False |
| RimChat/DiplomacySystem/ItemAirdropPaymentResolver.cs | 0 | 0 | 0 | False |

## Rule Of Thumb

- Treat CALLS as primary truth for C# relation checks.
- Treat IMPORTS as auxiliary only; same-namespace direct references often have no import edge.
- If IMPORTS is high relative to using RimChat.*, namespace-wide overlink is likely.
- If a symbol has PROCESS=0, do not infer that the call chain is absent.
