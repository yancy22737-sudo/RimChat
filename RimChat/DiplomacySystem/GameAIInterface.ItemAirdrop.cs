using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RimChat.AI;
using RimChat.Config;
using RimChat.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: ThingDefResolver, ItemAirdropSelectionParser, AIChatClient, DropPodUtility, DropCellFinder.
    /// Responsibility: two-phase item airdrop orchestration for request_item_airdrop.
    /// </summary>
    public partial class GameAIInterface
    {
        public APIResult RequestItemAirdrop(Faction faction, Dictionary<string, object> parameters)
        {
            ClearStaleBoundNeedParameters(parameters);

            Map map = Find.AnyPlayerHomeMap;
            Pawn negotiator = ItemAirdropTradePolicy.ResolveBestNegotiator(null);
            if (negotiator == null)
            {
                return FailFastAirdrop(
                    "player_negotiator_required",
                    "Preparing a barter airdrop requires a valid player negotiator on a map.",
                    faction,
                    parameters);
            }

            APIResult prepareResult = PrepareItemAirdropTradeForMap(faction, parameters, map, false, negotiator);
            if (!prepareResult.Success)
            {
                return prepareResult;
            }

            if (prepareResult.Data is ItemAirdropPendingSelectionData)
            {
                return prepareResult;
            }

            if (!(prepareResult.Data is ItemAirdropPreparedTradeData preparedTrade))
            {
                return FailFastAirdrop("prepare_trade_failed", "Airdrop trade payload is missing.", faction, parameters);
            }

            return CommitPreparedItemAirdropTrade(faction, preparedTrade);
        }

        private APIResult PrepareItemAirdropCandidates(
            ItemAirdropIntent intent,
            int budget,
            RimChatSettings settings,
            out ItemAirdropCandidatePack candidatePack)
        {
            candidatePack = null;
            HashSet<string> blacklist = ParseCsv(settings.ItemAirdropBlacklistDefNamesCsv);
            HashSet<string> blockedCategories = ItemAirdropSafetyPolicy.ParseBlockedCategories(settings.ItemAirdropBlockedCategoriesCsv);
            int topN = Mathf.Clamp(settings.ItemAirdropSelectionCandidateLimit, 1, 100);
            ItemAirdropCandidatePack strictPack = ThingDefResolver.BuildCandidates(intent, topN, blacklist, blockedCategories);
            if (strictPack.Candidates.Count > 0 ||
                intent == null ||
                intent.Family == ItemAirdropNeedFamily.Unknown ||
                !settings.EnableAirdropSameFamilyRelaxedRetry)
            {
                candidatePack = strictPack;
                return APIResult.SuccessResult("Candidate market prices resolved.");
            }

            // Relax blocked-category filtering once, while keeping the same family boundary.
            HashSet<string> relaxedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ItemAirdropCandidatePack retryPack = ThingDefResolver.BuildCandidates(intent, topN, blacklist, relaxedCategories);
            retryPack.UsedFallbackPool = true;
            candidatePack = retryPack;
            return APIResult.SuccessResult("Candidate market prices resolved.");
        }

        private static string BuildPrepareAuditSummary(
            ItemAirdropIntent intent,
            int budget,
            ItemAirdropCandidatePack candidatePack,
            List<string> localAliases,
            List<string> aiAliases,
            string needType = "missing",
            string needRawPreview = "none")
        {
            string tokenSummary = intent?.Tokens == null || intent.Tokens.Count == 0
                ? "none"
                : string.Join("|", intent.Tokens.Take(8));
            string localAliasSummary = localAliases == null || localAliases.Count == 0
                ? "none"
                : string.Join("|", localAliases.Take(6));
            string aiAliasSummary = aiAliases == null || aiAliases.Count == 0
                ? "none"
                : string.Join("|", aiAliases.Take(6));
            string diagnostics = candidatePack?.BuildDiagnosticsSummary() ?? "records=0,blacklist=0,blockedCategory=0,familyReject=0,matchReject=0,nearMiss=none";
            string topSummary = candidatePack?.BuildSummary() ?? "none";
            string boundNeedDetails = string.IsNullOrWhiteSpace(candidatePack?.BoundNeedConflictDetails)
                ? "none"
                : candidatePack.BoundNeedConflictDetails;
            return $"budget={budget},family={intent?.Family ?? ItemAirdropNeedFamily.Unknown},needType={needType},needRawPreview={needRawPreview},tokens={tokenSummary},localAliases={localAliasSummary},aiAliases={aiAliasSummary},candidates={candidatePack?.Candidates?.Count ?? 0},fallback={candidatePack?.UsedFallbackPool ?? false},{diagnostics},boundNeedDetails={boundNeedDetails},top={topSummary}";
        }

        private static bool ShouldRequireNeedClarification(ItemAirdropIntent intent, ItemAirdropCandidatePack candidatePack)
        {
            return intent?.Family == ItemAirdropNeedFamily.Unknown &&
                   !ThingDefResolver.HasStrongNeedRelevance(intent, candidatePack, 5);
        }

        private static string BuildNeedClarificationReason()
        {
            return "need_relevance_insufficient";
        }

        private List<string> ExpandNeedAliasesWithAi(string need, string constraints, RimChatSettings settings)
        {
            _ = need;
            _ = constraints;
            _ = settings;
            // AI alias expansion is handled by BeginPrepareItemAirdropTradeAsync.
            return new List<string>();
        }

        private static string BuildAliasExpansionPrompt(string need, string constraints, int maxCount)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Need: {(need ?? string.Empty).Trim()}");
            sb.AppendLine($"Constraints: {(constraints ?? string.Empty).Trim()}");
            sb.AppendLine($"MaxAliases: {maxCount}");
            sb.AppendLine("Output strictly JSON with field aliases only.");
            return sb.ToString().Trim();
        }

        private static List<string> ParseAliases(string rawText, int maxCount)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return result;
            }

            MatchCollection matches = Regex.Matches(rawText, "\"([^\"]+)\"");
            for (int i = 0; i < matches.Count; i++)
            {
                string value = matches[i].Groups[1].Value?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(value) ||
                    string.Equals(value, "aliases", StringComparison.OrdinalIgnoreCase) ||
                    value.Length > 36)
                {
                    continue;
                }

                result.Add(value);
                if (result.Count >= maxCount)
                {
                    break;
                }
            }

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private APIResult ExecuteItemAirdropSelection(
            ItemAirdropIntent intent,
            ItemAirdropCandidatePack candidatePack,
            int budget,
            RimChatSettings settings,
            Dictionary<string, object> parameters,
            string forcedSelectedDefName = "")
        {
            string boundNeedDefName = ReadString(parameters, ItemAirdropParameterKeys.BoundNeedDefName);
            string effectiveForcedSelectedDefName = forcedSelectedDefName;
            bool hasBoundNeed = !string.IsNullOrWhiteSpace(boundNeedDefName);
            bool hadForcedSelectionConflict = false;
            if (hasBoundNeed)
            {
                if (string.IsNullOrWhiteSpace(effectiveForcedSelectedDefName))
                {
                    effectiveForcedSelectedDefName = boundNeedDefName;
                }
                else if (!string.Equals(effectiveForcedSelectedDefName, boundNeedDefName, StringComparison.OrdinalIgnoreCase))
                {
                    effectiveForcedSelectedDefName = boundNeedDefName;
                    hadForcedSelectionConflict = true;
                }
            }

            RequestedCountExtraction requestedCount = ExtractRequestedCount(intent?.NeedText);
            requestedCount = MergeRequestedCountWithParameters(requestedCount, parameters);
            if (requestedCount.HasMultipleCounts)
            {
                return BuildSelectionFailure(
                    "need_count_ambiguous",
                    "need contains multiple explicit counts; request_item_airdrop supports single-item count only.");
            }

            if (!string.IsNullOrWhiteSpace(effectiveForcedSelectedDefName))
            {
                APIResult forcedResult = TryBuildForcedSelection(
                    effectiveForcedSelectedDefName,
                    intent,
                    candidatePack,
                    budget,
                    settings,
                    requestedCount,
                    out ItemAirdropSelection forcedSelection,
                    out string forcedCountSource,
                    out int forcedHardMax,
                    out int forcedMaxByBudget);
                if (!forcedResult.Success || forcedSelection == null)
                {
                    return forcedResult;
                }

                if (hasBoundNeed)
                {
                    forcedSelection.Reason = hadForcedSelectionConflict
                        ? "bound_need_conflict_rebuilt"
                        : "bound_need_selected";
                }

                string forcedDetails = BuildSelectionAuditDetails(
                    forcedSelection,
                    candidatePack,
                    budget,
                    settings,
                    forcedCountSource,
                    forcedMaxByBudget,
                    forcedHardMax);
                RecordStageAudit("selection", null, null, forcedDetails);
                return APIResult.SuccessResult("Selection resolved from bound need / selected_def.", forcedSelection);
            }

            const string pendingReason = "Second-pass LLM selection moved to async pipeline.";
            APIResult pendingResult = BuildTimeoutPendingSelection(intent, candidatePack, budget, settings, "selection_timeout", pendingReason);
            if (pendingResult.Data is ItemAirdropPendingSelectionData pendingData)
            {
                RecordStageAudit("selection", null, null, BuildPendingSelectionAuditDetails(pendingData));
            }

            return pendingResult;
        }

        private static RequestedCountExtraction ExtractRequestedCount(string needText)
        {
            var result = new RequestedCountExtraction();
            if (string.IsNullOrWhiteSpace(needText))
            {
                return result;
            }

            MatchCollection matches = Regex.Matches(needText, "\\d+");
            if (matches.Count <= 0)
            {
                return result;
            }

            if (matches.Count > 1)
            {
                result.HasMultipleCounts = true;
                return result;
            }

            if (!int.TryParse(matches[0].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                return result;
            }

            result.HasExplicitCount = true;
            result.RequestedCount = Mathf.Clamp(parsed, 1, 5000);
            return result;
        }

        private APIResult ValidateAirdropSelection(
            ItemAirdropSelection selection,
            ItemAirdropCandidatePack candidatePack,
            int budget,
            RimChatSettings settings,
            RequestedCountExtraction requestedCount,
            string defaultCountSource,
            out ThingDefRecord selectedRecord,
            out int validatedCount,
            out string resolvedCountSource,
            out int requestedOriginalCount,
            out int maxByBudget,
            out int maxBySystem,
            out int hardMax)
        {
            selectedRecord = null;
            validatedCount = 0;
            resolvedCountSource = string.IsNullOrWhiteSpace(defaultCountSource) ? "llm" : defaultCountSource;
            requestedOriginalCount = 0;
            maxByBudget = 0;
            maxBySystem = 0;
            hardMax = 0;
            if (selection == null)
            {
                return BuildSelectionFailure("selection_null", "Selection payload is null.");
            }

            selectedRecord = candidatePack.Candidates
                .Select(c => c.Record)
                .FirstOrDefault(r => string.Equals(r.DefName, selection.SelectedDefName, StringComparison.OrdinalIgnoreCase));
            if (selectedRecord?.Def == null)
            {
                return BuildSelectionFailure("selection_out_of_candidates", $"selected_def '{selection.SelectedDefName}' is not in candidate list.");
            }

            if (requestedCount.HasMultipleCounts)
            {
                return BuildSelectionFailure(
                    "need_count_ambiguous",
                    "need contains multiple explicit counts; request_item_airdrop supports single-item count only.");
            }

            int targetCount = selection.Count;
            if (requestedCount.HasExplicitCount)
            {
                targetCount = requestedCount.RequestedCount;
                resolvedCountSource = "fallback_explicit";
            }
            else if (requestedCount.HasParameterCount)
            {
                targetCount = requestedCount.ParameterCount;
                resolvedCountSource = "fallback_parameter";
            }

            if (requestedCount.HasExplicitCount && requestedCount.HasParameterCount)
            {
                int needCount = requestedCount.RequestedCount;
                int parameterCount = requestedCount.ParameterCount;
                targetCount = Math.Max(needCount, parameterCount);
                resolvedCountSource = needCount == parameterCount
                    ? "fallback_explicit_parameter_consistent"
                    : "fallback_max_conflict";
            }

            if (targetCount <= 0)
            {
                return BuildSelectionFailure("selection_count_invalid", "count must be greater than 0.");
            }

            requestedOriginalCount = targetCount;
            ComputeLegalCountWindow(budget, selectedRecord, candidatePack, settings, out maxByBudget, out maxBySystem, out hardMax);
            if (hardMax <= 0)
            {
                string message = $"Budget {budget} is too low for {selectedRecord.DefName}. maxByBudget={maxByBudget},maxBySystem={maxBySystem},hardMax={hardMax}.";
                return BuildSelectionFailure("budget_too_low", message);
            }

            if (targetCount > hardMax)
            {
                int originalCount = targetCount;
                targetCount = hardMax;
                resolvedCountSource = $"{resolvedCountSource}_clamped({originalCount}->{hardMax})";
            }

            validatedCount = targetCount;
            return APIResult.SuccessResult("Selection validated.");
        }

        private static RequestedCountExtraction MergeRequestedCountWithParameters(
            RequestedCountExtraction requestedCount,
            Dictionary<string, object> parameters)
        {
            int explicitNeedCount = 0;
            bool hasExplicitNeedCount = TryReadIntParameter(parameters, "__airdrop_explicit_need_count", out explicitNeedCount);
            if (hasExplicitNeedCount && explicitNeedCount > 0)
            {
                requestedCount.HasExplicitCount = true;
                requestedCount.RequestedCount = Mathf.Clamp(explicitNeedCount, 1, 5000);
            }

            int parameterCount = 0;
            bool hasCount = TryReadIntParameter(parameters, "count", out parameterCount);
            if (!hasCount)
            {
                hasCount = TryReadIntParameter(parameters, "quantity", out parameterCount);
            }

            if (!hasCount || parameterCount <= 0)
            {
                return requestedCount;
            }

            requestedCount.HasParameterCount = true;
            requestedCount.ParameterCount = Mathf.Clamp(parameterCount, 1, 5000);
            return requestedCount;
        }

        private APIResult ExecuteAirdropDrop(
            Faction faction,
            Dictionary<string, object> parameters,
            Map map,
            int budget,
            ThingDefRecord selectedRecord,
            int validatedCount,
            string selectionReason,
            ItemAirdropCandidatePack candidatePack)
        {
            List<Thing> stacks = BuildStacks(selectedRecord.Def, validatedCount, RimChatMod.Instance.InstanceSettings.ItemAirdropMaxStacksPerDrop);
            if (stacks.Count == 0)
            {
                return FailFastAirdrop("stack_build_failed", "Could not create item stacks for airdrop.", faction, parameters);
            }

            if (!TryFindAirdropCell(map, out IntVec3 dropCell))
            {
                if (MapUtility.IsOrbitalBaseMap(map))
                {
                    return FailFastAirdrop("orbital_drop_unavailable", "You are on an orbital base and cannot receive supply drops.", faction, parameters);
                }
                return FailFastAirdrop("dropcell_not_found", "No legal drop cell found near colony center.", faction, parameters);
            }

            DropPodUtility.DropThingsNear(
                dropCell,
                map,
                stacks,
                110,
                canInstaDropDuringInit: false,
                leaveSlag: false,
                canRoofPunch: false);

            int deliveredCount = stacks.Sum(t => t.stackCount);
            string stage3 = $"def={selectedRecord.DefName},count={deliveredCount},budget={budget},reason={selectionReason},drop={dropCell}";
            RecordStageAudit("execute", faction, parameters, stage3);
            RecordAPICall("RequestItemAirdrop", true, stage3);

            string playerTitle = "RimChat_ItemAirdropArrivedTitle".Translate();
            string playerBody = "RimChat_ItemAirdropArrivedBody".Translate(
                faction.Name,
                selectedRecord.Label.CapitalizeFirst(),
                deliveredCount,
                budget);
            Find.LetterStack.ReceiveLetter(playerTitle, playerBody, LetterDefOf.PositiveEvent, new TargetInfo(dropCell, map), faction);

            var payload = new ItemAirdropResultData
            {
                SelectedDefName = selectedRecord.DefName,
                ResolvedLabel = selectedRecord.Label,
                BudgetUsed = budget,
                Quantity = deliveredCount,
                DropCell = dropCell.ToString(),
                FailureCode = string.Empty
            };
            return APIResult.SuccessResult($"Airdrop delivered: {selectedRecord.DefName} x{deliveredCount} (budget {budget})", payload);
        }

        private APIResult BuildSelectionFailure(string code, string message)
        {
            return new APIResult
            {
                Success = false,
                Message = $"[{code}] {message}",
                Data = new ItemAirdropResultData { FailureCode = code }
            };
        }

        private static string NormalizeSelectionFailureReason(string rawReason)
        {
            string normalized = (rawReason ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "service_error";
            }

            if (normalized.Contains("queue_timeout"))
            {
                return "queue_timeout";
            }

            if (normalized.Contains("timeout"))
            {
                return "timeout";
            }

            if (normalized.StartsWith("http_", StringComparison.Ordinal) ||
                normalized.Contains("connection") ||
                normalized.Contains("data_processing"))
            {
                return normalized;
            }

            return "service_error";
        }

        private static bool IsTimeoutLikeSelectionFailure(string failureReason)
        {
            return string.Equals(failureReason, "timeout", StringComparison.Ordinal) ||
                   string.Equals(failureReason, "queue_timeout", StringComparison.Ordinal);
        }

        private static string BuildSelectionServiceErrorMessage(AIChatClientResponse response, string failureReason)
        {
            if (response == null)
            {
                return "selection request failed: unknown error.";
            }

            string reason = string.IsNullOrWhiteSpace(failureReason) ? "service_error" : failureReason;
            string message = string.IsNullOrWhiteSpace(response.ErrorText)
                ? "selection request failed."
                : response.ErrorText.Trim();
            return $"{message} failureReason={reason},http={response.HttpStatusCode}";
        }

        private void RecordSelectionDebugRecord(
            string requestText,
            string responseText,
            string errorText,
            AIRequestDebugStatus status,
            long durationMs,
            long httpStatusCode,
            DateTime startedAtUtc,
            int promptTokens = 0,
            int completionTokens = 0,
            int totalTokens = 0,
            bool isEstimatedTokens = true)
        {
            AIChatServiceAsync.RecordExternalDebugRecord(
                AIRequestDebugSource.AirdropSelection,
                DialogueUsageChannel.Diplomacy,
                "airdrop_selection",
                status,
                durationMs,
                httpStatusCode,
                promptTokens,
                completionTokens,
                totalTokens,
                isEstimatedTokens,
                requestText,
                responseText,
                errorText,
                startedAtUtc);
        }

        private static string BuildSelectionPrompt(
            ItemAirdropIntent intent,
            ItemAirdropCandidatePack candidatePack,
            int budget,
            RimChatSettings settings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("channel:airdrop_selection");
            sb.AppendLine("Task: choose exactly one candidate and legal count for item airdrop.");
            sb.AppendLine("IMPORTANT: If Need has an explicit quantity (e.g., '50个干肉饼' or '50 pemmican'), preserve that quantity in count.");
            sb.AppendLine("IMPORTANT: If Need directly matches a candidate, keep the explicit quantity from Need instead of forcing count=1.");
            sb.AppendLine("Output JSON only:");
            sb.AppendLine("{\"selected_def\":\"<defName>\",\"count\":<int>,\"reason\":\"<short reason>\"}");
            sb.AppendLine($"Need: {intent.NeedText}");
            sb.AppendLine($"Constraints: {intent.ConstraintsText}");
            sb.AppendLine($"Family: {intent.Family}");
            sb.AppendLine($"BudgetSilver: {budget}");
            sb.AppendLine("Rule: If Need has explicit quantity, use it. Otherwise count must be 1..max_legal_count for selected_def.");
            sb.AppendLine("Candidates:");

            int promptCandidateLimit = Math.Min(candidatePack.Candidates.Count, 20);
            for (int i = 0; i < promptCandidateLimit; i++)
            {
                ItemAirdropCandidate candidate = candidatePack.Candidates[i];
                ComputeLegalCountWindow(budget, candidate.Record, candidatePack, settings, out _, out _, out int hardMax);
                sb.AppendLine(
                    $"{i + 1}. def={candidate.Record.DefName},label={candidate.Record.Label},unit={candidate.Price:F1},max_legal_count={hardMax}");
            }

            int omitted = candidatePack.Candidates.Count - promptCandidateLimit;
            if (omitted > 0)
            {
                sb.AppendLine($"... omitted_candidates={omitted}");
            }

            return sb.ToString().Trim();
        }

        private static void ComputeLegalCountWindow(
            int budget,
            ThingDefRecord record,
            RimChatSettings settings,
            out int maxByBudget,
            out int maxBySystem,
            out int hardMax)
        {
            ComputeLegalCountWindow(budget, record, null, settings, out maxByBudget, out maxBySystem, out hardMax);
        }

        private static void ComputeLegalCountWindow(
            int budget,
            ThingDefRecord record,
            ItemAirdropCandidatePack candidatePack,
            RimChatSettings settings,
            out int maxByBudget,
            out int maxBySystem,
            out int hardMax)
        {
            if (record == null)
            {
                maxByBudget = 0;
                maxBySystem = 0;
                hardMax = 0;
                return;
            }

            float safePrice = candidatePack?.ResolveUnitPrice(record) ?? Math.Max(0.01f, record.MarketValue);
            maxByBudget = Mathf.FloorToInt(Math.Max(0, budget) / safePrice);
            maxBySystem = ComputeMaxDeliverableByStacks(record.Def, settings);
            hardMax = Math.Max(0, maxByBudget);
        }

        private static int ComputeMaxDeliverableByStacks(ThingDef def, RimChatSettings settings)
        {
            int maxStacks = Math.Max(1, settings?.ItemAirdropMaxStacksPerDrop ?? 1);
            int stackLimit = Math.Max(1, def?.stackLimit ?? 1);
            return maxStacks * stackLimit;
        }

        private static int ResolveFamilyDefaultCount(ItemAirdropNeedFamily family)
        {
            return family switch
            {
                ItemAirdropNeedFamily.Food => 25,
                ItemAirdropNeedFamily.Medicine => 10,
                ItemAirdropNeedFamily.Weapon => 1,
                ItemAirdropNeedFamily.Apparel => 1,
                ItemAirdropNeedFamily.Resource => 75,
                _ => 5
            };
        }

        private string BuildSelectionAuditDetails(
            ItemAirdropSelection selection,
            ItemAirdropCandidatePack candidatePack,
            int budget,
            RimChatSettings settings,
            string countSource,
            int? explicitMaxByBudget,
            int? explicitHardMax)
        {
            int maxByBudget = explicitMaxByBudget ?? -1;
            int hardMax = explicitHardMax ?? -1;
            if (!explicitMaxByBudget.HasValue || !explicitHardMax.HasValue)
            {
                if (TryResolveSelectedRecord(selection, candidatePack, out ThingDefRecord selectedRecord))
                {
                    ComputeLegalCountWindow(budget, selectedRecord, candidatePack, settings, out maxByBudget, out _, out hardMax);
                }
            }

            string maxByBudgetText = maxByBudget >= 0
                ? maxByBudget.ToString(CultureInfo.InvariantCulture)
                : "na";
            string hardMaxText = hardMax >= 0
                ? hardMax.ToString(CultureInfo.InvariantCulture)
                : "na";
            return $"selected={selection?.SelectedDefName ?? "unknown"},count={selection?.Count ?? 0},reason={selection?.Reason ?? "none"},countSource={countSource},hardMax={hardMaxText},maxByBudget={maxByBudgetText}";
        }

        private static bool TryResolveSelectedRecord(
            ItemAirdropSelection selection,
            ItemAirdropCandidatePack candidatePack,
            out ThingDefRecord selectedRecord)
        {
            selectedRecord = null;
            if (selection == null || candidatePack?.Candidates == null)
            {
                return false;
            }

            selectedRecord = candidatePack.Candidates
                .Select(c => c.Record)
                .FirstOrDefault(r => string.Equals(r.DefName, selection.SelectedDefName, StringComparison.OrdinalIgnoreCase));
            return selectedRecord?.Def != null;
        }

        private APIResult FailFastAirdrop(
            string failureCode,
            string message,
            Faction faction,
            Dictionary<string, object> parameters,
            string diagnostics = "")
        {
            string details = string.IsNullOrWhiteSpace(diagnostics)
                ? $"code={failureCode},msg={message}"
                : $"code={failureCode},msg={message},diag={diagnostics}";
            RecordStageAudit("failed", faction, parameters, details);
            string auditText = $"faction={faction?.Name ?? "unknown"}, code={failureCode}, msg={message}, params={SerializeParameterSummary(parameters)}";
            if (!string.IsNullOrWhiteSpace(diagnostics))
            {
                auditText = $"{auditText}, diag={diagnostics}";
            }

            RecordAPICall("RequestItemAirdrop", false, auditText, message);

            string playerTitle = "RimChat_ItemAirdropFailedTitle".Translate();
            string playerBody = "RimChat_ItemAirdropFailedBody".Translate(failureCode, message);
            Find.LetterStack.ReceiveLetter(playerTitle, playerBody, LetterDefOf.NeutralEvent);
            return APIResult.FailureResult($"[{failureCode}] {message}");
        }

        private void RecordStageAudit(string stage, Faction faction, Dictionary<string, object> parameters, string details)
        {
            string text = $"stage={stage},faction={faction?.Name ?? "unknown"},params={SerializeParameterSummary(parameters)},details={details}";
            RecordAPICall("RequestItemAirdrop.Stage", true, text);
        }

        private static int ResolveBudget(Dictionary<string, object> parameters, string scenario, RimChatSettings settings, Map map)
        {
            if (TryReadIntParameter(parameters, "budget_silver", out int directBudget))
            {
                return Mathf.Clamp(directBudget, settings.ItemAirdropMinBudgetSilver, settings.ItemAirdropMaxBudgetSilver);
            }

            if (scenario == "ransom")
            {
                float wealth = map?.wealthWatcher?.WealthTotal ?? 0f;
                int ransomBudget = Mathf.RoundToInt(wealth * settings.ItemAirdropRansomBudgetPercent);
                return Mathf.Clamp(ransomBudget, settings.ItemAirdropMinBudgetSilver, settings.ItemAirdropMaxBudgetSilver);
            }

            int aiBudget = settings.ItemAirdropDefaultAIBudgetSilver;
            return Mathf.Clamp(aiBudget, settings.ItemAirdropMinBudgetSilver, settings.ItemAirdropMaxBudgetSilver);
        }

        private static bool TryFindAirdropCell(Map map, out IntVec3 dropCell)
        {
            IntVec3 vanillaTradeDropSpot = DropCellFinder.TradeDropSpot(map);
            if (vanillaTradeDropSpot.IsValid &&
                vanillaTradeDropSpot.InBounds(map) &&
                vanillaTradeDropSpot.Standable(map) &&
                DropCellFinder.CanPhysicallyDropInto(vanillaTradeDropSpot, map, canRoofPunch: false))
            {
                dropCell = vanillaTradeDropSpot;
                return true;
            }

            IntVec3 center = map.Center;
            return CellFinder.TryFindRandomCellNear(
                center,
                map,
                18,
                c => c.InBounds(map) &&
                     c.Walkable(map) &&
                     c.Standable(map) &&
                     DropCellFinder.CanPhysicallyDropInto(c, map, canRoofPunch: false),
                out dropCell);
        }

        private static List<Thing> BuildStacks(ThingDef def, int totalCount, int maxStacks)
        {
            var result = new List<Thing>();
            int stackLimit = Math.Max(1, def.stackLimit);
            int remaining = Math.Max(0, totalCount);

            // Resolve stuff for MadeFromStuff defs (e.g., apparel, weapons)
            ThingDef stuff = null;
            if (def.MadeFromStuff)
            {
                stuff = def.defaultStuff;
            }

            while (remaining > 0 && result.Count < maxStacks)
            {
                Thing thing = ThingMaker.MakeThing(def, stuff);
                int stack = Math.Min(stackLimit, remaining);
                thing.stackCount = stack;
                result.Add(thing);
                remaining -= stack;
            }

            return result;
        }

        private static HashSet<string> ParseCsv(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return new HashSet<string>(
                csv.Split(new[] { ',', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);
        }

        private static string ReadString(Dictionary<string, object> parameters, string key)
        {
            if (parameters == null || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            if (!parameters.TryGetValue(key, out object value) || value == null)
            {
                return string.Empty;
            }

            return value.ToString()?.Trim() ?? string.Empty;
        }

        private static bool TryReadRequiredStringParameter(
            Dictionary<string, object> parameters,
            string key,
            out string value,
            out string valueType,
            out string rawPreview)
        {
            value = string.Empty;
            valueType = "missing";
            rawPreview = "none";
            if (parameters == null || string.IsNullOrWhiteSpace(key) || !parameters.TryGetValue(key, out object raw) || raw == null)
            {
                return false;
            }

            valueType = DescribeParameterType(raw);
            rawPreview = BuildParameterPreview(raw);
            if (!(raw is string text))
            {
                return false;
            }

            value = text.Trim();
            return !string.IsNullOrWhiteSpace(value);
        }

        private static string DescribeParameterType(object value)
        {
            if (value == null)
            {
                return "null";
            }

            return value is string ? "string" : value.GetType().Name;
        }

        private static string BuildParameterPreview(object value)
        {
            string text = value?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return "empty";
            }

            string singleLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return singleLine.Length <= 48 ? singleLine : $"{singleLine.Substring(0, 48)}...";
        }

        private static bool TryReadIntParameter(Dictionary<string, object> parameters, string key, out int value)
        {
            value = 0;
            if (parameters == null || !parameters.TryGetValue(key, out object raw) || raw == null)
            {
                return false;
            }

            if (raw is int intValue)
            {
                value = intValue;
                return true;
            }

            if (raw is long longValue && longValue <= int.MaxValue && longValue >= int.MinValue)
            {
                value = (int)longValue;
                return true;
            }

            return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static string NormalizeScenario(string scenario)
        {
            string normalized = (scenario ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "trade" => "trade",
                "ransom" => "ransom",
                _ => "general"
            };
        }

        private static string SerializeParameterSummary(Dictionary<string, object> parameters)
        {
            if (parameters == null || parameters.Count == 0)
            {
                return "none";
            }

            return string.Join(",", parameters.Select(kv => $"{kv.Key}={kv.Value}"));
        }
    }

    public sealed class ItemAirdropResultData
    {
        public string SelectedDefName { get; set; }
        public string ResolvedLabel { get; set; }
        public int BudgetUsed { get; set; }
        public int Quantity { get; set; }
        public string DropCell { get; set; }
        public string FailureCode { get; set; }
    }

    internal struct RequestedCountExtraction
    {
        public bool HasExplicitCount { get; set; }
        public bool HasMultipleCounts { get; set; }
        public int RequestedCount { get; set; }
        public bool HasParameterCount { get; set; }
        public int ParameterCount { get; set; }
    }
}
