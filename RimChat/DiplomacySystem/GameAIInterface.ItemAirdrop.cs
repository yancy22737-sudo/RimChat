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
    /// Dependencies: ThingDefResolver, ItemAirdropSelectionParser, AIChatClient, DropPodUtility.
    /// Responsibility: two-phase item airdrop orchestration for request_item_airdrop.
    /// </summary>
    public partial class GameAIInterface
    {
        public APIResult RequestItemAirdrop(Faction faction, Dictionary<string, object> parameters)
        {
            Map map = Find.AnyPlayerHomeMap;
            APIResult prepareResult = PrepareItemAirdropTradeForMap(faction, parameters, map, false);
            if (!prepareResult.Success)
            {
                return prepareResult;
            }

            if (!(prepareResult.Data is ItemAirdropPreparedTradeData preparedTrade))
            {
                return FailFastAirdrop("prepare_trade_failed", "Airdrop trade payload is missing.", faction, parameters);
            }

            return CommitPreparedItemAirdropTrade(faction, preparedTrade);
        }

        private ItemAirdropCandidatePack PrepareItemAirdropCandidates(ItemAirdropIntent intent, int budget, RimChatSettings settings)
        {
            HashSet<string> blacklist = ParseCsv(settings.ItemAirdropBlacklistDefNamesCsv);
            HashSet<string> blockedCategories = ItemAirdropSafetyPolicy.ParseBlockedCategories(settings.ItemAirdropBlockedCategoriesCsv);
            int topN = Mathf.Clamp(settings.ItemAirdropSelectionCandidateLimit, 1, 100);
            ItemAirdropCandidatePack strictPack = ThingDefResolver.BuildCandidates(intent, topN, blacklist, blockedCategories);
            if (strictPack.Candidates.Count > 0 ||
                intent == null ||
                intent.Family == ItemAirdropNeedFamily.Unknown ||
                !settings.EnableAirdropSameFamilyRelaxedRetry)
            {
                return strictPack;
            }

            // Relax blocked-category filtering once, while keeping the same family boundary.
            HashSet<string> relaxedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ItemAirdropCandidatePack retryPack = ThingDefResolver.BuildCandidates(intent, topN, blacklist, relaxedCategories);
            retryPack.UsedFallbackPool = true;
            return retryPack;
        }

        private static string BuildPrepareAuditSummary(
            ItemAirdropIntent intent,
            int budget,
            ItemAirdropCandidatePack candidatePack,
            List<string> localAliases,
            List<string> aiAliases)
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
            return $"budget={budget},family={intent?.Family ?? ItemAirdropNeedFamily.Unknown},tokens={tokenSummary},localAliases={localAliasSummary},aiAliases={aiAliasSummary},candidates={candidatePack?.Candidates?.Count ?? 0},fallback={candidatePack?.UsedFallbackPool ?? false},{diagnostics},top={topSummary}";
        }

        private List<string> ExpandNeedAliasesWithAi(string need, string constraints, RimChatSettings settings)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(need) ||
                settings == null ||
                !settings.EnableAirdropAliasExpansion ||
                !AIChatClient.Instance.IsConfigured())
            {
                return result;
            }

            int maxCount = Mathf.Clamp(settings.ItemAirdropAliasExpansionMaxCount, 2, 12);
            int timeoutSeconds = Mathf.Clamp(settings.ItemAirdropAliasExpansionTimeoutSeconds, 2, 10);
            var messages = new List<ChatMessage>
            {
                new ChatMessage
                {
                    role = "system",
                    content = "Generate only JSON: {\"aliases\":[\"...\"]}. Return up to 8 concise CN/EN aliases for a RimWorld item need. No explanation."
                },
                new ChatMessage
                {
                    role = "user",
                    content = BuildAliasExpansionPrompt(need, constraints, maxCount)
                }
            };

            try
            {
                var task = AIChatClient.Instance.SendChatRequestAsync(messages);
                if (!task.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
                {
                    return result;
                }

                return ParseAliases(task.Result, maxCount);
            }
            catch
            {
                return result;
            }
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
            RimChatSettings settings)
        {
            RequestedCountExtraction requestedCount = ExtractRequestedCount(intent?.NeedText);
            if (requestedCount.HasMultipleCounts)
            {
                return BuildSelectionFailure(
                    "need_count_ambiguous",
                    "need contains multiple explicit counts; request_item_airdrop supports single-item count only.");
            }

            string requestPrompt = BuildSelectionPrompt(intent, candidatePack, budget, settings);
            var messages = new List<ChatMessage>
            {
                new ChatMessage
                {
                    role = "system",
                    content = "Select one item candidate and return strict JSON only with fields: selected_def,count,reason."
                },
                new ChatMessage
                {
                    role = "user",
                    content = requestPrompt
                }
            };

            DateTime startedAt = DateTime.UtcNow;
            long durationMs = 0L;
            string rawText = string.Empty;
            string errorText = string.Empty;
            long httpStatusCode = 0L;
            try
            {
                int timeoutSeconds = Mathf.Clamp(settings.ItemAirdropSecondPassTimeoutSeconds, 3, 30);
                var task = AIChatClient.Instance.SendChatRequestAsync(messages);
                if (!task.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
                {
                    durationMs = Math.Max(0L, (long)(DateTime.UtcNow - startedAt).TotalMilliseconds);
                    errorText = "Second-pass LLM selection timed out.";
                    RecordSelectionDebugRecord(requestPrompt, rawText, errorText, AIRequestDebugStatus.Error, durationMs, httpStatusCode, startedAt);
                    APIResult fallbackResult = TryBuildTimeoutFallbackSelection(
                        intent,
                        candidatePack,
                        budget,
                        settings,
                        requestedCount,
                        out ItemAirdropSelection fallbackSelection,
                        out string fallbackCountSource,
                        out int fallbackHardMax,
                        out int fallbackMaxByBudget);
                    if (fallbackResult.Success && fallbackSelection != null)
                    {
                        string details = BuildSelectionAuditDetails(
                            fallbackSelection,
                            candidatePack,
                            budget,
                            settings,
                            fallbackCountSource,
                            fallbackMaxByBudget,
                            fallbackHardMax);
                        RecordStageAudit("selection", null, null, details);
                        return APIResult.SuccessResult("Selection timeout; fallback selection applied.", fallbackSelection);
                    }

                    string fallbackFailureCode = (fallbackResult.Data as ItemAirdropResultData)?.FailureCode ?? "selection_timeout";
                    string fallbackFailureDetails = $"failed=true,countSource={fallbackCountSource},hardMax={fallbackHardMax},maxByBudget={fallbackMaxByBudget},code={fallbackFailureCode},msg={fallbackResult.Message}";
                    RecordStageAudit("selection", null, null, fallbackFailureDetails);
                    return fallbackResult.Success
                        ? BuildSelectionFailure("selection_timeout", "Second-pass LLM selection timed out.")
                        : fallbackResult;
                }

                rawText = task.Result ?? string.Empty;
                durationMs = Math.Max(0L, (long)(DateTime.UtcNow - startedAt).TotalMilliseconds);
                if (!ItemAirdropSelectionParser.TryParse(rawText, out ItemAirdropSelection selection, out string failureCode, out string failureMessage))
                {
                    errorText = $"[{failureCode}] {failureMessage}";
                    RecordSelectionDebugRecord(requestPrompt, rawText, errorText, AIRequestDebugStatus.Error, durationMs, httpStatusCode, startedAt);
                    return BuildSelectionFailure(failureCode, failureMessage);
                }

                string selectionDetails = BuildSelectionAuditDetails(selection, candidatePack, budget, settings, "llm", null, null);
                RecordStageAudit("selection", null, null, selectionDetails);
                RecordSelectionDebugRecord(requestPrompt, rawText, string.Empty, AIRequestDebugStatus.Success, durationMs, httpStatusCode, startedAt);
                return APIResult.SuccessResult("Selection succeeded.", selection);
            }
            catch (Exception ex)
            {
                durationMs = Math.Max(0L, (long)(DateTime.UtcNow - startedAt).TotalMilliseconds);
                errorText = ex.Message;
                RecordSelectionDebugRecord(requestPrompt, rawText, errorText, AIRequestDebugStatus.Error, durationMs, httpStatusCode, startedAt);
                return BuildSelectionFailure("selection_exception", ex.Message);
            }
        }

        private APIResult TryBuildTimeoutFallbackSelection(
            ItemAirdropIntent intent,
            ItemAirdropCandidatePack candidatePack,
            int budget,
            RimChatSettings settings,
            RequestedCountExtraction requestedCount,
            out ItemAirdropSelection selection,
            out string countSource,
            out int hardMax,
            out int maxByBudget)
        {
            selection = null;
            countSource = "fallback_default_family";
            hardMax = 0;
            maxByBudget = 0;
            if (candidatePack?.Candidates == null || candidatePack.Candidates.Count == 0)
            {
                return BuildSelectionFailure("selection_timeout", "Second-pass LLM selection timed out.");
            }

            ItemAirdropCandidate top = candidatePack.Candidates[0];
            if (top?.Record == null || string.IsNullOrWhiteSpace(top.Record.DefName))
            {
                return BuildSelectionFailure("selection_timeout", "Second-pass LLM selection timed out.");
            }

            ComputeLegalCountWindow(budget, top.Record, settings, out maxByBudget, out int maxBySystem, out hardMax);
            if (hardMax <= 0)
            {
                string message = $"Budget {budget} is too low for {top.Record.DefName}. maxByBudget={maxByBudget},maxBySystem={maxBySystem},hardMax={hardMax}.";
                return BuildSelectionFailure("budget_too_low", message);
            }

            if (requestedCount.HasMultipleCounts)
            {
                return BuildSelectionFailure(
                    "need_count_ambiguous",
                    "need contains multiple explicit counts; request_item_airdrop supports single-item count only.");
            }

            int resolvedCount;
            if (requestedCount.HasExplicitCount)
            {
                countSource = "fallback_explicit";
                if (requestedCount.RequestedCount > hardMax)
                {
                    string message = $"count {requestedCount.RequestedCount} exceeds max legal count {hardMax} (maxByBudget={maxByBudget},maxBySystem={maxBySystem},hardMax={hardMax}).";
                    return BuildSelectionFailure("selection_count_out_of_range", message);
                }

                resolvedCount = requestedCount.RequestedCount;
            }
            else
            {
                int baseCount = ResolveFamilyDefaultCount(intent?.Family ?? ItemAirdropNeedFamily.Unknown);
                resolvedCount = Mathf.Clamp(Math.Min(baseCount, hardMax), 1, hardMax);
                countSource = "fallback_default_family";
            }

            selection = new ItemAirdropSelection
            {
                SelectedDefName = top.Record.DefName,
                Count = resolvedCount,
                Reason = "fallback_after_selection_timeout_top1_rank"
            };
            return APIResult.SuccessResult("Selection timeout fallback succeeded.", selection);
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
            out ThingDefRecord selectedRecord,
            out int validatedCount)
        {
            selectedRecord = null;
            validatedCount = 0;
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

            if (selection.Count <= 0)
            {
                return BuildSelectionFailure("selection_count_invalid", "count must be greater than 0.");
            }

            ComputeLegalCountWindow(budget, selectedRecord, settings, out int maxByBudget, out int maxBySystem, out int hardMax);
            if (hardMax <= 0)
            {
                string message = $"Budget {budget} is too low for {selectedRecord.DefName}. maxByBudget={maxByBudget},maxBySystem={maxBySystem},hardMax={hardMax}.";
                return BuildSelectionFailure("budget_too_low", message);
            }

            if (selection.Count > hardMax)
            {
                string message = $"count {selection.Count} exceeds max legal count {hardMax} (maxByBudget={maxByBudget},maxBySystem={maxBySystem},hardMax={hardMax}).";
                return BuildSelectionFailure("selection_count_out_of_range", message);
            }

            validatedCount = selection.Count;
            return APIResult.SuccessResult("Selection validated.");
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

        private void RecordSelectionDebugRecord(
            string requestText,
            string responseText,
            string errorText,
            AIRequestDebugStatus status,
            long durationMs,
            long httpStatusCode,
            DateTime startedAtUtc)
        {
            AIChatServiceAsync.RecordExternalDebugRecord(
                AIRequestDebugSource.AirdropSelection,
                DialogueUsageChannel.Diplomacy,
                "airdrop_selection",
                status,
                durationMs,
                httpStatusCode,
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
            sb.AppendLine("Task: choose exactly one candidate and legal count for single-item airdrop.");
            sb.AppendLine("Output JSON only:");
            sb.AppendLine("{\"selected_def\":\"<defName>\",\"count\":<int>,\"reason\":\"<short reason>\"}");
            sb.AppendLine($"Need: {intent.NeedText}");
            sb.AppendLine($"Constraints: {intent.ConstraintsText}");
            sb.AppendLine($"Family: {intent.Family}");
            sb.AppendLine($"BudgetSilver: {budget}");
            sb.AppendLine("Rule: count must be 1..max_legal_count for selected_def.");
            sb.AppendLine("Candidates:");

            for (int i = 0; i < candidatePack.Candidates.Count; i++)
            {
                ItemAirdropCandidate candidate = candidatePack.Candidates[i];
                ComputeLegalCountWindow(budget, candidate.Record, settings, out _, out _, out int hardMax);
                sb.AppendLine(
                    $"{i + 1}. def={candidate.Record.DefName}, label={candidate.Record.Label}, market={candidate.Price:F2}, stackLimit={candidate.Record.StackLimit}, match={candidate.MatchScore}, safety={candidate.SafetyScore}, max_legal_count={hardMax}");
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
            if (record == null)
            {
                maxByBudget = 0;
                maxBySystem = Math.Max(0, settings?.ItemAirdropMaxTotalItemsPerDrop ?? 0);
                hardMax = 0;
                return;
            }

            float safePrice = Math.Max(0.01f, record.MarketValue);
            maxByBudget = Mathf.FloorToInt(Math.Max(0, budget) / safePrice);
            maxBySystem = Math.Max(0, settings?.ItemAirdropMaxTotalItemsPerDrop ?? 0);
            hardMax = Math.Max(0, Math.Min(maxByBudget, maxBySystem));
        }

        private static int ResolveFamilyDefaultCount(ItemAirdropNeedFamily family)
        {
            return family switch
            {
                ItemAirdropNeedFamily.Food => 25,
                ItemAirdropNeedFamily.Medicine => 10,
                ItemAirdropNeedFamily.Weapon => 1,
                ItemAirdropNeedFamily.Apparel => 1,
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
                    ComputeLegalCountWindow(budget, selectedRecord, settings, out maxByBudget, out _, out hardMax);
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
            while (remaining > 0 && result.Count < maxStacks)
            {
                Thing thing = ThingMaker.MakeThing(def);
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
    }
}
