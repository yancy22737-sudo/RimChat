using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.AI;
using RimChat.Config;
using RimChat.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: AIChatServiceAsync, item-airdrop prepare pipeline, selection parser.
    /// Responsibility: run airdrop alias/selection stages asynchronously without blocking the main thread.
    /// </summary>
    public partial class GameAIInterface
    {
        public APIResult BeginPrepareItemAirdropTradeAsync(
            Faction faction,
            Dictionary<string, object> parameters,
            Pawn playerNegotiator,
            Action<APIResult> onCompleted,
            Action<string, int> onRequestQueued)
        {
            APIResult contextResult = TryBuildAirdropAsyncContext(
                faction,
                parameters,
                playerNegotiator,
                out ItemAirdropAsyncPrepareContext context);
            if (!contextResult.Success)
            {
                return contextResult;
            }

            return ContinueAirdropPrepareAsync(context, onCompleted, onRequestQueued);
        }

        public bool CancelItemAirdropAsyncRequest(string requestId, string cancelReason, string error)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return false;
            }

            return AIChatServiceAsync.Instance.CancelRequest(
                requestId,
                string.IsNullOrWhiteSpace(cancelReason) ? "airdrop_async_cancelled" : cancelReason,
                string.IsNullOrWhiteSpace(error) ? "Airdrop async request cancelled." : error);
        }

        private APIResult TryBuildAirdropAsyncContext(
            Faction faction,
            Dictionary<string, object> parameters,
            Pawn playerNegotiator,
            out ItemAirdropAsyncPrepareContext context)
        {
            context = null;
            if (RimChatMod.Instance?.InstanceSettings == null)
            {
                return APIResult.FailureResult("Settings not initialized");
            }

            if (playerNegotiator == null || playerNegotiator.Map == null)
            {
                return FailFastAirdrop(
                    "player_negotiator_required",
                    "Preparing a barter airdrop requires a valid player negotiator on a map.",
                    faction,
                    parameters);
            }

            RimChatSettings settings = RimChatMod.Instance.InstanceSettings;
            if (!settings.EnableAIItemAirdrop)
            {
                return APIResult.FailureResult("request_item_airdrop is disabled in settings.");
            }

            if (faction == null)
            {
                return APIResult.FailureResult("Faction cannot be null");
            }

            if (parameters == null)
            {
                return APIResult.FailureResult("request_item_airdrop requires parameters.");
            }

            Map map = playerNegotiator.Map;
            if (map == null || !map.IsPlayerHome)
            {
                return FailFastAirdrop(
                    "map_not_player_home",
                    "Barter airdrop requires a player home map context.",
                    faction,
                    parameters);
            }

            string need = ReadString(parameters, "need");
            if (string.IsNullOrWhiteSpace(need))
            {
                return FailFastAirdrop("missing_need", "request_item_airdrop requires parameter 'need'.", faction, parameters);
            }

            string scenario = NormalizeScenario(ReadString(parameters, "scenario"));
            string constraints = ReadString(parameters, "constraints");
            bool hasProvidedBudget = TryReadIntParameter(parameters, "budget_silver", out int providedBudgetSilver);
            APIResult paymentPlanResult = BuildPaymentPlan(
                parameters,
                map,
                out List<ItemAirdropPreparedPaymentLine> paymentLines,
                out List<ItemAirdropDeductionPlanLine> deductionPlan,
                out int budget,
                out int paymentTotalSilver);
            if (!paymentPlanResult.Success)
            {
                return FailFastAirdrop(
                    (paymentPlanResult.Data as ItemAirdropResultData)?.FailureCode ?? "payment_plan_failed",
                    paymentPlanResult.Message,
                    faction,
                    parameters);
            }

            if (hasProvidedBudget && providedBudgetSilver != budget)
            {
                string mismatchAudit =
                    $"faction={faction?.Name ?? "unknown"},provided={providedBudgetSilver},derived={budget},delta={providedBudgetSilver - budget},need={need},scenario={scenario}";
                RecordAPICall("RequestItemAirdrop.BudgetMismatch", true, mismatchAudit);
            }

            ItemAirdropIntent intent = ItemAirdropIntent.Create(need, constraints, scenario);
            ItemAirdropCandidatePack candidatePack = PrepareItemAirdropCandidates(intent, budget, settings);
            var localAliases = new List<string>();
            if (candidatePack.Candidates.Count == 0)
            {
                localAliases = ThingDefResolver.ExpandLocalAliases(intent);
                if (localAliases.Count > 0)
                {
                    intent = ItemAirdropIntent.Create(need, constraints, scenario, localAliases);
                    candidatePack = PrepareItemAirdropCandidates(intent, budget, settings);
                }
            }

            context = new ItemAirdropAsyncPrepareContext
            {
                Faction = faction,
                Parameters = CloneParameterDictionary(parameters),
                Settings = settings,
                Map = map,
                Need = need,
                Scenario = scenario,
                Constraints = constraints,
                Budget = budget,
                PaymentTotalSilver = paymentTotalSilver,
                HasProvidedBudget = hasProvidedBudget,
                ProvidedBudgetSilver = providedBudgetSilver,
                PaymentLines = paymentLines,
                DeductionPlan = deductionPlan,
                Intent = intent,
                CandidatePack = candidatePack,
                LocalAliases = localAliases,
                ForcedSelectedDef = ReadString(parameters, "selected_def")
            };
            return APIResult.SuccessResult("Airdrop async context prepared.", context);
        }

        private APIResult ContinueAirdropPrepareAsync(
            ItemAirdropAsyncPrepareContext context,
            Action<APIResult> onCompleted,
            Action<string, int> onRequestQueued)
        {
            if (context == null)
            {
                return APIResult.FailureResult("Airdrop async context is null.");
            }

            if (context.CandidatePack?.Candidates?.Count > 0)
            {
                RecordAirdropPrepareStage(context);
                return ContinueAirdropSelectionStageAsync(context, onCompleted, onRequestQueued);
            }

            if (!CanQueueAirdropAliasExpansion(context))
            {
                RecordAirdropPrepareStage(context);
                return BuildNoCandidateFailure(context);
            }

            return QueueAirdropAliasExpansionAsync(context, onCompleted, onRequestQueued);
        }

        private static bool CanQueueAirdropAliasExpansion(ItemAirdropAsyncPrepareContext context)
        {
            return context?.Settings != null &&
                   context.Settings.EnableAirdropAliasExpansion &&
                   AIChatServiceAsync.Instance.IsConfigured();
        }

        private APIResult QueueAirdropAliasExpansionAsync(
            ItemAirdropAsyncPrepareContext context,
            Action<APIResult> onCompleted,
            Action<string, int> onRequestQueued)
        {
            int maxCount = Mathf.Clamp(context.Settings.ItemAirdropAliasExpansionMaxCount, 2, 12);
            int timeoutSeconds = Mathf.Clamp(context.Settings.ItemAirdropAliasExpansionTimeoutSeconds, 2, 10);
            var messages = new List<ChatMessageData>
            {
                new ChatMessageData
                {
                    role = "system",
                    content = "Generate only JSON: {\"aliases\":[\"...\"]}. Return up to 8 concise CN/EN aliases for a RimWorld item need. No explanation."
                },
                new ChatMessageData
                {
                    role = "user",
                    content = BuildAliasExpansionPrompt(context.Need, context.Constraints, maxCount)
                }
            };

            DateTime startedAt = DateTime.UtcNow;
            bool queued = TryQueueAirdropAsyncRequest(
                messages,
                timeoutSeconds,
                onRequestQueued,
                response =>
                {
                    context.AiAliases = ParseAliases(response, maxCount);
                    if (context.AiAliases.Count > 0)
                    {
                        context.Intent = ItemAirdropIntent.Create(context.Need, context.Constraints, context.Scenario, context.AiAliases);
                        context.CandidatePack = PrepareItemAirdropCandidates(context.Intent, context.Budget, context.Settings);
                    }

                    RecordAirdropPrepareStage(context);
                    if (context.CandidatePack?.Candidates?.Count <= 0)
                    {
                        onCompleted?.Invoke(BuildNoCandidateFailure(context));
                        return;
                    }

                    ContinueAirdropSelectionStageAsync(context, onCompleted, onRequestQueued);
                },
                (requestId, errorText) =>
                {
                    RecordAirdropPrepareStage(context);
                    long durationMs = Math.Max(0L, (long)(DateTime.UtcNow - startedAt).TotalMilliseconds);
                    RecordSelectionDebugRecord(
                        "channel:airdrop_alias_expansion",
                        string.Empty,
                        errorText ?? string.Empty,
                        AIRequestDebugStatus.Error,
                        durationMs,
                        0L,
                        startedAt);
                    onCompleted?.Invoke(BuildNoCandidateFailure(context));
                });
            if (!queued)
            {
                RecordAirdropPrepareStage(context);
                return BuildNoCandidateFailure(context);
            }

            return APIResult.SuccessResult("Airdrop alias expansion queued.", new ItemAirdropAsyncQueuedData());
        }

        private APIResult ContinueAirdropSelectionStageAsync(
            ItemAirdropAsyncPrepareContext context,
            Action<APIResult> onCompleted,
            Action<string, int> onRequestQueued)
        {
            RequestedCountExtraction requestedCount = ExtractRequestedCount(context.Intent?.NeedText);
            if (requestedCount.HasMultipleCounts)
            {
                return FailFastAirdrop(
                    "need_count_ambiguous",
                    "need contains multiple explicit counts; request_item_airdrop supports single-item count only.",
                    context.Faction,
                    context.Parameters);
            }

            if (!string.IsNullOrWhiteSpace(context.ForcedSelectedDef))
            {
                APIResult forcedResult = TryBuildForcedSelection(
                    context.ForcedSelectedDef,
                    context.Intent,
                    context.CandidatePack,
                    context.Budget,
                    context.Settings,
                    requestedCount,
                    out ItemAirdropSelection forcedSelection,
                    out string forcedCountSource,
                    out int forcedHardMax,
                    out int forcedMaxByBudget);
                if (!forcedResult.Success || forcedSelection == null)
                {
                    return FailFastAirdrop(
                        (forcedResult.Data as ItemAirdropResultData)?.FailureCode ?? "selection_invalid",
                        forcedResult.Message,
                        context.Faction,
                        context.Parameters);
                }

                string forcedDetails = BuildSelectionAuditDetails(
                    forcedSelection,
                    context.CandidatePack,
                    context.Budget,
                    context.Settings,
                    forcedCountSource,
                    forcedMaxByBudget,
                    forcedHardMax);
                RecordStageAudit("selection", null, null, forcedDetails);
                return BuildPreparedTradeFromSelection(context, forcedSelection);
            }

            string requestPrompt = BuildSelectionPrompt(context.Intent, context.CandidatePack, context.Budget, context.Settings);
            var messages = new List<ChatMessageData>
            {
                new ChatMessageData
                {
                    role = "system",
                    content = "Select one item candidate and return strict JSON only with fields: selected_def,count,reason."
                },
                new ChatMessageData
                {
                    role = "user",
                    content = requestPrompt
                }
            };

            int timeoutSeconds = Mathf.Clamp(context.Settings.ItemAirdropSecondPassTimeoutSeconds, 3, 30);
            DateTime startedAt = DateTime.UtcNow;
            bool queued = TryQueueAirdropAsyncRequest(
                messages,
                timeoutSeconds,
                onRequestQueued,
                response =>
                {
                    long durationMs = Math.Max(0L, (long)(DateTime.UtcNow - startedAt).TotalMilliseconds);
                    if (!ItemAirdropSelectionParser.TryParse(response, out ItemAirdropSelection selection, out string failureCode, out string failureMessage))
                    {
                        string errorText = $"[{failureCode}] {failureMessage}";
                        RecordSelectionDebugRecord(requestPrompt, response ?? string.Empty, errorText, AIRequestDebugStatus.Error, durationMs, 0L, startedAt);
                        onCompleted?.Invoke(FailFastAirdrop(failureCode, failureMessage, context.Faction, context.Parameters));
                        return;
                    }

                    string selectionDetails = BuildSelectionAuditDetails(selection, context.CandidatePack, context.Budget, context.Settings, "llm", null, null);
                    RecordStageAudit("selection", null, null, selectionDetails);
                    RecordSelectionDebugRecord(requestPrompt, response ?? string.Empty, string.Empty, AIRequestDebugStatus.Success, durationMs, 0L, startedAt);
                    onCompleted?.Invoke(BuildPreparedTradeFromSelection(context, selection));
                },
                (requestId, errorText) =>
                {
                    long durationMs = Math.Max(0L, (long)(DateTime.UtcNow - startedAt).TotalMilliseconds);
                    string failureReason = ResolveAsyncRequestFailureReason(requestId);
                    RecordSelectionDebugRecord(requestPrompt, string.Empty, errorText ?? string.Empty, AIRequestDebugStatus.Error, durationMs, 0L, startedAt);
                    if (IsTimeoutLikeAirdropFailure(failureReason, errorText))
                    {
                        string timeoutCode = string.Equals(failureReason, "queue_timeout", StringComparison.Ordinal)
                            ? "selection_queue_timeout"
                            : "selection_timeout";
                        APIResult pendingResult = BuildTimeoutPendingSelection(
                            context.Intent,
                            context.CandidatePack,
                            context.Budget,
                            context.Settings,
                            timeoutCode,
                            errorText);
                        if (pendingResult.Data is ItemAirdropPendingSelectionData pendingData)
                        {
                            RecordStageAudit("selection", null, null, BuildPendingSelectionAuditDetails(pendingData));
                        }

                        onCompleted?.Invoke(pendingResult);
                        return;
                    }

                    onCompleted?.Invoke(FailFastAirdrop(
                        string.Equals(failureReason, "queue_timeout", StringComparison.Ordinal)
                            ? "selection_queue_timeout"
                            : "selection_service_error",
                        string.IsNullOrWhiteSpace(errorText) ? "selection request failed." : errorText,
                        context.Faction,
                        context.Parameters));
                });
            return queued
                ? APIResult.SuccessResult("Airdrop selection request queued.", new ItemAirdropAsyncQueuedData())
                : FailFastAirdrop("selection_service_error", "Failed to queue airdrop selection request.", context.Faction, context.Parameters);
        }

        private APIResult BuildPreparedTradeFromSelection(ItemAirdropAsyncPrepareContext context, ItemAirdropSelection selection)
        {
            APIResult validationResult = ValidateAirdropSelection(
                selection,
                context.CandidatePack,
                context.Budget,
                context.Settings,
                out ThingDefRecord selectedRecord,
                out int validatedCount);
            if (!validationResult.Success)
            {
                return FailFastAirdrop(
                    (validationResult.Data as ItemAirdropResultData)?.FailureCode ?? "selection_invalid",
                    validationResult.Message,
                    context.Faction,
                    context.Parameters);
            }

            int overpay = Math.Max(0, context.PaymentTotalSilver - context.Budget);
            string budgetMismatchSummary = context.HasProvidedBudget
                ? $"{context.ProvidedBudgetSilver}->{context.Budget}(delta={context.ProvidedBudgetSilver - context.Budget})"
                : "none";
            string paymentSummary = $"budget={context.Budget},payment={context.PaymentTotalSilver},overpay={overpay},budgetMismatch={budgetMismatchSummary},paymentLines={context.PaymentLines.Count},deductionRows={context.DeductionPlan.Count}";
            RecordStageAudit("prepare_trade", context.Faction, context.Parameters, paymentSummary);

            var prepared = new ItemAirdropPreparedTradeData
            {
                SelectedDefName = selectedRecord.DefName,
                ResolvedLabel = selectedRecord.Label,
                Quantity = validatedCount,
                BudgetSilver = context.Budget,
                PaymentTotalSilver = context.PaymentTotalSilver,
                PaymentOverpaySilver = overpay,
                MapUniqueId = context.Map.uniqueID,
                NeedText = context.Need,
                Scenario = context.Scenario,
                SelectionReason = selection.Reason ?? string.Empty,
                PaymentLines = context.PaymentLines,
                DeductionPlan = context.DeductionPlan,
                ParametersSnapshot = CloneParameterDictionary(context.Parameters)
            };
            return APIResult.SuccessResult("Airdrop trade prepared.", prepared);
        }

        private void RecordAirdropPrepareStage(ItemAirdropAsyncPrepareContext context)
        {
            string prepareSummary = BuildPrepareAuditSummary(
                context.Intent,
                context.Budget,
                context.CandidatePack,
                context.LocalAliases,
                context.AiAliases);
            RecordStageAudit("prepare", context.Faction, context.Parameters, prepareSummary);
        }

        private APIResult BuildNoCandidateFailure(ItemAirdropAsyncPrepareContext context)
        {
            string prepareSummary = BuildPrepareAuditSummary(
                context.Intent,
                context.Budget,
                context.CandidatePack,
                context.LocalAliases,
                context.AiAliases);
            if (context.Intent?.Family == ItemAirdropNeedFamily.Unknown)
            {
                return FailFastAirdrop(
                    "need_family_unknown",
                    "Could not classify request need. Try adding multiple CN/EN aliases in need/constraints.",
                    context.Faction,
                    context.Parameters,
                    prepareSummary);
            }

            return FailFastAirdrop(
                "no_candidates",
                "No legal airdrop candidates were produced for this request.",
                context.Faction,
                context.Parameters,
                prepareSummary);
        }

        private bool TryQueueAirdropAsyncRequest(
            List<ChatMessageData> messages,
            int timeoutSeconds,
            Action<string, int> onRequestQueued,
            Action<string> onSuccess,
            Action<string, string> onError)
        {
            string requestId = null;
            requestId = AIChatServiceAsync.Instance.SendChatRequestAsync(
                messages,
                onSuccess: response => onSuccess?.Invoke(response ?? string.Empty),
                onError: error => onError?.Invoke(requestId ?? string.Empty, error ?? string.Empty),
                onProgress: null,
                usageChannel: DialogueUsageChannel.Diplomacy,
                debugSource: AIRequestDebugSource.AirdropSelection,
                requestTimeoutSecondsOverride: timeoutSeconds,
                queueTimeoutSecondsOverride: timeoutSeconds);
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return false;
            }

            onRequestQueued?.Invoke(requestId, timeoutSeconds);
            return true;
        }

        private static string ResolveAsyncRequestFailureReason(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return string.Empty;
            }

            AIRequestResult status = AIChatServiceAsync.Instance.GetRequestStatus(requestId);
            return status?.FailureReason ?? string.Empty;
        }

        private static bool IsTimeoutLikeAirdropFailure(string failureReason, string errorText)
        {
            string reason = (failureReason ?? string.Empty).Trim().ToLowerInvariant();
            if (reason.Contains("queue_timeout") || reason.Contains("timeout"))
            {
                return true;
            }

            string error = (errorText ?? string.Empty).Trim().ToLowerInvariant();
            return error.Contains("timeout") || error.Contains("timed out");
        }
    }

    public sealed class ItemAirdropAsyncQueuedData
    {
    }

    internal sealed class ItemAirdropAsyncPrepareContext
    {
        public Faction Faction { get; set; }
        public Dictionary<string, object> Parameters { get; set; }
        public RimChatSettings Settings { get; set; }
        public Map Map { get; set; }
        public string Need { get; set; }
        public string Scenario { get; set; }
        public string Constraints { get; set; }
        public int Budget { get; set; }
        public int PaymentTotalSilver { get; set; }
        public bool HasProvidedBudget { get; set; }
        public int ProvidedBudgetSilver { get; set; }
        public List<ItemAirdropPreparedPaymentLine> PaymentLines { get; set; }
        public List<ItemAirdropDeductionPlanLine> DeductionPlan { get; set; }
        public ItemAirdropIntent Intent { get; set; }
        public ItemAirdropCandidatePack CandidatePack { get; set; }
        public List<string> LocalAliases { get; set; } = new List<string>();
        public List<string> AiAliases { get; set; } = new List<string>();
        public string ForcedSelectedDef { get; set; }
    }
}
