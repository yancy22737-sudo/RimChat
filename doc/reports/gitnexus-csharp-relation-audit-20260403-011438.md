# GitNexus C# Relation Audit

- Repository: RimChat
- RunAt: 2026-04-03 01:15:09
- Samples: 8

## Summary

- CALLS edges found: 8 / 8
- CALLS missing: 0
- Expected IMPORTS missing: 0
- Same-namespace no-import edges: 6
- Process zero coverage: 6
- Source evidence missing: 0

## Sample Matrix

| Id | Caller | Callee | EvidenceHits | CALLS | IMPORTS | PROCESS | Verdict |
| --- | --- | --- | --- | --- | --- | --- | --- |
| S1 | RimChat/DiplomacySystem/ItemAirdropPaymentResolver.cs::Resolve | RimChat/DiplomacySystem/ThingDefResolver.cs::BuildMatchRequest | 1 | 1 | 0 | 0 | SAME_NAMESPACE_NO_IMPORT_EDGE |
| S2 | RimChat/DiplomacySystem/ItemAirdropPaymentResolver.cs::Resolve | RimChat/DiplomacySystem/ThingDefMatchEngine.cs::ResolveSingle | 1 | 1 | 0 | 0 | SAME_NAMESPACE_NO_IMPORT_EDGE |
| S3 | RimChat/DiplomacySystem/Social/SocialNewsPromptBuilder.cs::BuildVariables | RimChat/DiplomacySystem/Social/SocialCircleService.cs::GetCategoryLabel | 2 | 1 | 0 | 0 | SAME_NAMESPACE_NO_IMPORT_EDGE |
| S4 | RimChat/DiplomacySystem/Social/SocialNewsPromptBuilder.cs::BuildPromptInputPayload | RimChat/DiplomacySystem/Social/SocialCircleService.cs::ResolveDisplayLabel | 3 | 1 | 0 | 0 | SAME_NAMESPACE_NO_IMPORT_EDGE |
| S5 | RimChat/DiplomacySystem/Social/SocialNewsPromptBuilder.cs::BuildMessages | RimChat/Persistence/DialogueScenarioContext.cs::CreateDiplomacy | 1 | 1 | 1 | 0 | OK |
| S6 | RimChat/DiplomacySystem/Social/SocialNewsPromptBuilder.cs::BuildMessages | RimChat/Persistence/PromptPersistenceService.WorkbenchComposer.cs::BuildUnifiedChannelSystemPrompt | 1 | 1 | 1 | 0 | OK |
| S7 | RimChat/Prompting/ScribanPromptEngine.cs::RenderOrThrow | RimChat/Prompting/PromptTemplateBlockRegistry.cs::TryGetReason | 1 | 1 | 0 | 1 | SAME_NAMESPACE_NO_IMPORT_EDGE |
| S8 | RimChat/DiplomacySystem/ThingDefResolver.cs::BuildMatchRequest | RimChat/DiplomacySystem/ThingDefMatchEngine.cs::ExtractSemanticTokens | 1 | 1 | 0 | 2 | SAME_NAMESPACE_NO_IMPORT_EDGE |

## Import Overlink Check

| File | RimChat using count | IMPORT edges | Edge/using | Suspect overlink |
| --- | --- | --- | --- | --- |
| RimChat/DiplomacySystem/Social/SocialNewsPromptBuilder.cs | 4 | 117 | 29.25 | True |
| RimChat/DiplomacySystem/ItemAirdropPaymentResolver.cs | 0 | 0 | 0 | False |

## Rule Of Thumb

- Treat CALLS as primary truth for C# relation checks.
- Treat IMPORTS as auxiliary only; same-namespace direct references often have no import edge.
- If IMPORTS is high relative to using RimChat.*, namespace-wide overlink is likely.
- If a symbol has PROCESS=0, do not infer that the call chain is absent.
