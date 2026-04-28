using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimChat.AI;
using RimChat.Dialogue;
using RimChat.DiplomacySystem;
using RimChat.Memory;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: GameAIInterface prepared airdrop trade API and dialogue session runtime state.
    /// Responsibility: show final confirmation dialog for barter airdrop and commit/cancel the prepared trade.
    /// </summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private const string AirdropPendingCandidatesKey = "__airdrop_pending_candidates";
        private const string AirdropPendingFailureCodeKey = "__airdrop_pending_failure_code";
        private enum AirdropPendingResolution
        {
            AutoPickTop1 = 0,
            ShowFailureMessage = 1
        }

        private static bool IsRequestItemAirdropAction(AIAction action)
        {
            return action != null &&
                   string.Equals(action.ActionType, AIActionNames.RequestItemAirdrop, StringComparison.Ordinal);
        }

        private bool TryHandleAirdropActionWithConfirmation(
            AIAction action,
            FactionDialogueSession currentSession,
            Faction currentFaction,
            out ActionExecutionOutcome outcome)
        {
            outcome = null;
            if (!IsRequestItemAirdropAction(action))
            {
                return false;
            }

            if (IsAirdropAsyncRequestPending(currentSession))
            {
                outcome = ActionExecutionOutcome.Success(
                    action,
                    BuildAirdropSelectionInProgressSystemText(),
                    new ItemAirdropAsyncQueuedData());
                return true;
            }

            AIAction actionSnapshot = new AIAction
            {
                ActionType = action.ActionType,
                Parameters = CloneParameters(action.Parameters),
                Reason = action.Reason
            };
            TryInjectPendingAirdropCountFromLatestPlayerMessage(actionSnapshot, currentSession);

            DialogueRuntimeContext requestContext = runtimeContext.WithCurrentRuntimeMarkers();
            string validateReason = string.Empty;
            bool resolved = DialogueContextResolver.TryResolveLiveContext(
                requestContext,
                out DialogueLiveContext liveContext,
                out string resolveReason);
            bool validated = resolved && DialogueContextValidator.ValidateRequestSend(requestContext, liveContext, out validateReason);
            if (!resolved || !validated)
            {
                string fallbackReason = string.IsNullOrWhiteSpace(validateReason) ? resolveReason : validateReason;
                Log.Warning($"[RimChat] Airdrop context validation failed: resolved={resolved}, validated={validated}, resolveReason={resolveReason}, validateReason={validateReason}, faction={currentFaction?.Name ?? "null"}, defName={currentFaction?.def?.defName ?? "null"}");
                outcome = ActionExecutionOutcome.Failure(action, fallbackReason ?? "RimChat_DialogueRequestUnavailable".Translate().ToString());
                return true;
            }

            Log.Message($"[RimChat] Airdrop context validation passed: faction={currentFaction?.Name}, defName={currentFaction?.def?.defName}, need={actionSnapshot.Parameters?["need"] ?? "null"}");

            var lease = new DialogueRequestLease(
                requestContext.DialogueSessionId,
                windowInstanceId,
                requestContext.ContextVersion);
            var prepareResult = GameAIInterface.Instance.BeginPrepareItemAirdropTradeAsync(
                currentFaction,
                actionSnapshot.Parameters,
                negotiator,
                completedResult => HandleAirdropAsyncPrepareCompleted(
                    currentSession,
                    currentFaction,
                    lease,
                    requestContext,
                    actionSnapshot,
                    currentSession?.airdropRequestGeneration ?? -1,
                    completedResult),
                (requestId, timeoutSeconds) => BindAirdropAsyncRequest(currentSession, lease, requestId, timeoutSeconds));
            if (!prepareResult.Success)
            {
                lease.Dispose();
                ResetAirdropConfirmationRuntime(currentSession, "prepare_start_failed", true, true);
                TransitionAirdropExecutionStage(currentSession, AirdropExecutionStage.Failed, prepareResult?.Message ?? "prepare_start_failed");
                string failureMessage = string.IsNullOrWhiteSpace(prepareResult?.Message)
                    ? "RimChat_Unknown".Translate().ToString()
                    : prepareResult.Message;
                outcome = ActionExecutionOutcome.Failure(action, failureMessage);
                return true;
            }

            if (prepareResult.Data is ItemAirdropAsyncQueuedData)
            {
                outcome = ActionExecutionOutcome.Success(
                    action,
                    BuildAirdropSelectionInProgressSystemText(),
                    prepareResult.Data);
                return true;
            }

            if (prepareResult.Data is ItemAirdropPendingSelectionData pendingSelection)
            {
                lease.Dispose();
                TransitionAirdropExecutionStage(currentSession, AirdropExecutionStage.SelectingCandidate, pendingSelection.FailureCode ?? "selection_pending");
                if (DeterminePendingSelectionResolution(pendingSelection) == AirdropPendingResolution.AutoPickTop1 &&
                    TryAutoPickPendingAirdropSelection(actionSnapshot, pendingSelection, currentSession, currentFaction, out outcome))
                {
                    return true;
                }

                CacheAirdropPendingSelectionIntent(currentSession, actionSnapshot, pendingSelection);
                outcome = ActionExecutionOutcome.Failure(
                    action,
                    BuildAirdropPendingSelectionSystemText(pendingSelection));
                return true;
            }

            if (!(prepareResult.Data is ItemAirdropPreparedTradeData preparedTrade))
            {
                lease.Dispose();
                ResetAirdropConfirmationRuntime(currentSession, "prepared_trade_missing", true);
                TransitionAirdropExecutionStage(currentSession, AirdropExecutionStage.Failed, "prepared_trade_missing");
                outcome = ActionExecutionOutcome.Failure(action, "RimChat_Unknown".Translate().ToString());
                return true;
            }

            lease.Dispose();
            ResetAirdropConfirmationRuntime(currentSession, "prepared_trade_ready", true, true);
            TransitionAirdropExecutionStage(currentSession, AirdropExecutionStage.PreparedAwaitingConfirm, preparedTrade.SelectedDefName ?? "prepared_trade");
            List<PendingAirdropSelectionCandidate> pendingCandidates = null;
            Dictionary<string, object> baseParameters = CloneParameters(actionSnapshot.Parameters);
            if (!TryReadPendingAirdropCandidates(baseParameters, out pendingCandidates))
            {
                pendingCandidates = new List<PendingAirdropSelectionCandidate>();
            }

            Log.Message(
                $"[RimChat] AirdropConfirmOpen: def={preparedTrade.SelectedDefName},count={preparedTrade.Quantity},requested={preparedTrade.RequestedQuantity},hardMax={preparedTrade.HardMax},adjustment={preparedTrade.CountAdjustmentReason},payment={preparedTrade.PaymentTotalSilver},candidateCount={pendingCandidates.Count}");
            ShowAirdropTradeConfirmationDialog(currentSession, currentFaction, preparedTrade, baseParameters, pendingCandidates);
            outcome = ActionExecutionOutcome.Success(
                action,
                "RimChat_ItemAirdropAwaitingConfirmSystem".Translate().ToString(),
                preparedTrade);
            return true;
        }

        private static string BuildAirdropSelectionInProgressSystemText()
        {
            return "RimChat_ItemAirdropSelectionInProgressSystem".Translate().ToString();
        }

        private static void TryInjectPendingAirdropCountFromLatestPlayerMessage(AIAction actionSnapshot, FactionDialogueSession currentSession)
        {
            if (actionSnapshot == null)
            {
                return;
            }

            if (actionSnapshot.Parameters == null)
            {
                actionSnapshot.Parameters = new Dictionary<string, object>(StringComparer.Ordinal);
            }

            if (HasAirdropExplicitCountParameter(actionSnapshot.Parameters))
            {
                return;
            }

            int pendingCardCount = Math.Max(0, currentSession?.pendingAirdropTradeCardRequestedCount ?? 0);
            if (pendingCardCount > 0)
            {
                actionSnapshot.Parameters["count"] = pendingCardCount;
                Log.Message($"[RimChat] Injected pending airdrop count from session trade-card reference: count={pendingCardCount}");
                return;
            }

            string latestPlayerText = currentSession?.messages?
                .LastOrDefault(message => message != null && message.isPlayer && !message.IsSystemMessage())?
                .message ?? string.Empty;
            if (!TryExtractAirdropRequestedCount(latestPlayerText, out int requestedCount))
            {
                return;
            }

            actionSnapshot.Parameters["count"] = requestedCount;
            Log.Message($"[RimChat] Injected pending airdrop count from latest player message: count={requestedCount}");
        }

        private static bool HasAirdropExplicitCountParameter(Dictionary<string, object> parameters)
        {
            if (parameters == null)
            {
                return false;
            }

            return parameters.ContainsKey("count") || parameters.ContainsKey("quantity");
        }

        private static bool IsTimeoutPendingSelection(ItemAirdropPendingSelectionData pendingSelection)
        {
            string code = pendingSelection?.FailureCode ?? string.Empty;
            return code.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsManualChoicePending(ItemAirdropPendingSelectionData pendingSelection)
        {
            string code = pendingSelection?.FailureCode ?? string.Empty;
            return code.IndexOf("selection_manual_choice", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static AirdropPendingResolution DeterminePendingSelectionResolution(ItemAirdropPendingSelectionData pendingSelection)
        {
            if (IsTimeoutPendingSelection(pendingSelection) || IsManualChoicePending(pendingSelection))
            {
                return AirdropPendingResolution.AutoPickTop1;
            }

            return AirdropPendingResolution.ShowFailureMessage;
        }

        private bool TryAutoPickPendingAirdropSelection(
            AIAction action,
            ItemAirdropPendingSelectionData pendingSelection,
            FactionDialogueSession currentSession,
            Faction currentFaction,
            out ActionExecutionOutcome outcome)
        {
            outcome = null;
            if (pendingSelection?.Options == null || pendingSelection.Options.Count == 0)
            {
                return false;
            }

            Dictionary<string, object> autoParameters = CloneParameters(action?.Parameters);
            autoParameters[AirdropPendingFailureCodeKey] = pendingSelection.FailureCode ?? "selection_manual_choice";
            autoParameters[AirdropPendingCandidatesKey] = pendingSelection.Options
                .OrderBy(option => option.Index)
                .Take(5)
                .Select(option => new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["index"] = option.Index,
                    ["defName"] = option.DefName ?? string.Empty,
                    ["label"] = option.Label ?? option.DefName ?? string.Empty,
                    ["unitPrice"] = option.UnitPrice,
                    ["max_legal_count"] = option.MaxLegalCount
                })
                .Cast<object>()
                .ToList();

            ItemAirdropPendingSelectionOption topOption = pendingSelection.Options
                .OrderBy(option => option.Index)
                .FirstOrDefault();
            if (topOption == null || string.IsNullOrWhiteSpace(topOption.DefName))
            {
                return false;
            }

            autoParameters["selected_def"] = topOption.DefName;
            var autoAction = new AIAction
            {
                ActionType = AIActionNames.RequestItemAirdrop,
                Parameters = autoParameters,
                Reason = "selection_timeout_autopick_top1"
            };
            return TryHandleAirdropActionWithConfirmation(autoAction, currentSession, currentFaction, out outcome);
        }

        private static void CacheAirdropPendingSelectionIntent(
            FactionDialogueSession currentSession,
            AIAction action,
            ItemAirdropPendingSelectionData pendingSelection)
        {
            if (currentSession == null || action == null || pendingSelection?.Options == null || pendingSelection.Options.Count == 0)
            {
                return;
            }

            Dictionary<string, object> parameters = CloneParameters(action.Parameters);
            parameters.Remove("selected_def");
            parameters[AirdropPendingFailureCodeKey] = pendingSelection.FailureCode ?? "selection_timeout";
            parameters[AirdropPendingCandidatesKey] = pendingSelection.Options
                .Select(option => new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["index"] = option.Index,
                    ["defName"] = option.DefName ?? string.Empty,
                    ["label"] = option.Label ?? option.DefName ?? string.Empty,
                    ["unitPrice"] = option.UnitPrice,
                    ["max_legal_count"] = option.MaxLegalCount
                })
                .Cast<object>()
                .ToList();

            var pendingAction = new AIAction
            {
                ActionType = AIActionNames.RequestItemAirdrop,
                Parameters = parameters,
                Reason = "selection_timeout_pending_confirmation"
            };

            int assistantRound = GetAssistantDialogueRound(currentSession) + 1;
            PendingDelayedActionIntent intent = CreatePendingDelayedIntent(
                pendingAction,
                assistantRound,
                true,
                "selected_def");
            if (intent == null)
            {
                return;
            }

            currentSession.pendingDelayedActionIntent = intent;
            currentSession.lastDelayedActionIntent = intent.Clone();
            Log.Message($"[RimChat] CacheAirdropPendingSelectionIntent: cached pendingDelayedActionIntent for RequestItemAirdrop, failureCode={pendingSelection.FailureCode}, optionsCount={pendingSelection.Options.Count}");
        }

        private static string BuildPendingSelectionCandidateLine(PendingAirdropSelectionCandidate candidate)
        {
            if (candidate == null)
            {
                return string.Empty;
            }

            return "RimChat_ItemAirdropSelectionPendingLine".Translate(
                candidate.Index,
                candidate.Label ?? candidate.DefName ?? "RimChat_Unknown".Translate().ToString(),
                candidate.DefName ?? "RimChat_Unknown".Translate().ToString(),
                candidate.UnitPrice.ToString("F1", CultureInfo.InvariantCulture),
                Math.Max(0, candidate.MaxLegalCount)).ToString();
        }

        private static string BuildAirdropPendingSelectionSystemText(ItemAirdropPendingSelectionData pendingSelection)
        {
            if (pendingSelection?.Options == null || pendingSelection.Options.Count == 0)
            {
                if (string.Equals(pendingSelection?.FailureCode, "need_relevance_insufficient", StringComparison.Ordinal))
                {
                    return "RimChat_ItemAirdropNeedClarifySystem".Translate().ToString();
                }

                return "RimChat_ItemAirdropAwaitingConfirmSystem".Translate().ToString();
            }

            string lines = string.Join(
                "\n",
                pendingSelection.Options
                    .OrderBy(option => option.Index)
                    .Select(option => "RimChat_ItemAirdropSelectionPendingLine".Translate(
                        option.Index,
                        option.Label ?? option.DefName ?? "RimChat_Unknown".Translate().ToString(),
                        option.DefName ?? "RimChat_Unknown".Translate().ToString(),
                        option.UnitPrice.ToString("F1", CultureInfo.InvariantCulture),
                        option.MaxLegalCount).ToString()));
            return "RimChat_ItemAirdropSelectionPendingSystem".Translate(lines).ToString();
        }

        private void ShowAirdropTradeConfirmationDialog(
            FactionDialogueSession currentSession,
            Faction currentFaction,
            ItemAirdropPreparedTradeData preparedTrade,
            Dictionary<string, object> baseParameters,
            List<PendingAirdropSelectionCandidate> pendingCandidates)
        {
            List<PendingAirdropSelectionCandidate> availableCandidates = pendingCandidates?
                .OrderBy(candidate => candidate.Index)
                .Take(5)
                .ToList() ?? new List<PendingAirdropSelectionCandidate>();
            ClearPendingAirdropDialogState("reschedule_confirmation", false);
            pendingAirdropDialogState = new PendingAirdropDialogState
            {
                Session = currentSession,
                Faction = currentFaction,
                PreparedTrade = preparedTrade,
                BaseParameters = CloneParameters(baseParameters),
                PendingCandidates = ClonePendingAirdropCandidates(availableCandidates)
            };
            Log.Message(
                $"[RimChat] AirdropConfirmScheduled: def={preparedTrade?.SelectedDefName ?? "unknown"},count={preparedTrade?.Quantity ?? 0},candidateCount={availableCandidates.Count}");
        }

        private void OpenQueuedAirdropTradeConfirmationDialog(PendingAirdropDialogState state)
        {
            if (state == null || state.PreparedTrade == null)
            {
                return;
            }

            ItemAirdropPreparedTradeData trade = state.PreparedTrade;
            string tradeLabel = string.IsNullOrWhiteSpace(trade?.ResolvedLabel)
                ? (trade?.SelectedDefName ?? "")
                : trade.ResolvedLabel;
            int quantity = trade?.Quantity ?? 1;
            int requestedQuantity = trade?.RequestedQuantity ?? quantity;
            int paymentTotal = trade?.PaymentTotalSilver ?? 0;
            float unitPrice = trade?.NeedQuotedUnitSilver ?? 0f;
            string priceTag = BuildPriceSemanticTag(trade?.NeedPriceSemantic);
            int shippingCost = Math.Max(0, trade?.ShippingCostSilver ?? 0);
            int shippingPods = Math.Max(0, trade?.ShippingPodCount ?? 0);
            string adjustmentReason = trade?.CountAdjustmentReason ?? string.Empty;

            List<PendingAirdropSelectionCandidate> availableCandidates = state.PendingCandidates?
                .OrderBy(candidate => candidate.Index)
                .Take(5)
                .ToList() ?? new List<PendingAirdropSelectionCandidate>();
            bool hasManualAlternative = availableCandidates.Count > 1;

            var confirmationDialog = new Dialog_AirdropTradeConfirmWithAlternative(
                tradeLabel,
                quantity,
                requestedQuantity,
                paymentTotal,
                unitPrice,
                priceTag,
                shippingCost,
                shippingPods,
                adjustmentReason,
                hasManualAlternative,
                () => CommitConfirmedAirdropTrade(state.Session, state.Faction, state.PreparedTrade),
                () =>
                {
                    // Pre-fill trade card from current prepared trade parameters.
                    ItemAirdropPreparedTradeData trade = state.PreparedTrade;
                    if (trade != null && state.Session != null &&
                        !string.IsNullOrWhiteSpace(trade.SelectedDefName) && trade.Quantity > 0)
                    {
                        state.Session.CacheAirdropCounteroffer(
                            trade.SelectedDefName,
                            trade.Quantity,
                            trade.PaymentTotalSilver,
                            string.Empty);
                    }

                    CancelConfirmedAirdropTrade(state.Session, state.Faction, skipSystemMessage: true);
                    if (state.Session != null && state.Faction != null)
                    {
                        Find.WindowStack.Add(new Dialog_ItemAirdropTradeCard(
                            state.Session,
                            state.Faction,
                            OnAirdropTradeCardSubmitted));
                    }
                },
                () => OpenAirdropAlternativeSelection(state.Session, state.Faction, state.BaseParameters, availableCandidates));
            Find.WindowStack.Add(confirmationDialog);
        }

        private static List<PendingAirdropSelectionCandidate> ClonePendingAirdropCandidates(
            List<PendingAirdropSelectionCandidate> candidates)
        {
            if (candidates == null || candidates.Count <= 0)
            {
                return new List<PendingAirdropSelectionCandidate>();
            }

            return candidates
                .Where(candidate => candidate != null)
                .Select(candidate => new PendingAirdropSelectionCandidate
                {
                    Index = candidate.Index,
                    DefName = candidate.DefName ?? string.Empty,
                    Label = candidate.Label ?? string.Empty,
                    UnitPrice = candidate.UnitPrice,
                    MaxLegalCount = candidate.MaxLegalCount
                })
                .ToList();
        }

        private void OpenAirdropAlternativeSelection(
            FactionDialogueSession currentSession,
            Faction currentFaction,
            Dictionary<string, object> baseParameters,
            List<PendingAirdropSelectionCandidate> availableCandidates)
        {
            if (currentFaction == null || currentSession == null || availableCandidates == null || availableCandidates.Count <= 1)
            {
                return;
            }

            Dictionary<string, object> safeParams = baseParameters ?? new Dictionary<string, object>();

            var options = new List<FloatMenuOption>();
            foreach (PendingAirdropSelectionCandidate candidate in availableCandidates)
            {
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.DefName))
                {
                    continue;
                }

                string optionText = "RimChat_ItemAirdropSelectionPendingLine".Translate(
                    candidate.Index,
                    candidate.Label ?? candidate.DefName,
                    candidate.DefName,
                    candidate.UnitPrice.ToString("F1", CultureInfo.InvariantCulture),
                    Math.Max(0, candidate.MaxLegalCount)).ToString();
                options.Add(new FloatMenuOption(optionText, () =>
                {
                    Dictionary<string, object> mappedParameters = CloneParameters(safeParams);
                    mappedParameters["selected_def"] = candidate.DefName;
                    var mappedAction = new AIAction
                    {
                        ActionType = AIActionNames.RequestItemAirdrop,
                        Parameters = mappedParameters,
                        Reason = "selection_manual_alternative"
                    };

                    if (!TryHandleAirdropActionWithConfirmation(mappedAction, currentSession, currentFaction, out _))
                    {
                        currentSession?.AddMessage(
                            "System",
                            "RimChat_ItemAirdropCommitFailedSystem".Translate("manual_selection_failed"),
                            false,
                            DialogueMessageType.System);
                    }
                }));
            }

            if (options.Count <= 0)
            {
                return;
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private sealed class Dialog_AirdropTradeConfirmWithAlternative : Window
        {
            private readonly string tradeLabel;
            private readonly int quantity;
            private readonly int requestedQuantity;
            private readonly int paymentTotal;
            private readonly float unitPrice;
            private readonly string priceTag;
            private readonly int shippingCost;
            private readonly int shippingPods;
            private readonly string adjustmentReason;
            private readonly Action onConfirm;
            private readonly Action onCancel;
            private readonly Action onAlternative;
            private readonly bool optionalAlternativeVisible;

            public override Vector2 InitialSize => new Vector2(500f, 320f);

            public Dialog_AirdropTradeConfirmWithAlternative(
                string tradeLabel,
                int quantity,
                int requestedQuantity,
                int paymentTotal,
                float unitPrice,
                string priceTag,
                int shippingCost,
                int shippingPods,
                string adjustmentReason,
                bool hasAlternative,
                Action onConfirm,
                Action onCancel,
                Action onAlternative)
            {
                this.tradeLabel = tradeLabel ?? string.Empty;
                this.quantity = Math.Max(1, quantity);
                this.requestedQuantity = Math.Max(1, requestedQuantity);
                this.paymentTotal = paymentTotal;
                this.unitPrice = unitPrice;
                this.priceTag = priceTag ?? string.Empty;
                this.shippingCost = shippingCost;
                this.shippingPods = shippingPods;
                this.adjustmentReason = adjustmentReason ?? string.Empty;
                this.onConfirm = onConfirm;
                this.onCancel = onCancel;
                this.onAlternative = onAlternative;
                this.optionalAlternativeVisible = hasAlternative;
                forcePause = true;
                doCloseX = false;
                absorbInputAroundWindow = true;
                closeOnClickedOutside = false;
                closeOnCancel = false;
                closeOnAccept = false;
            }

            public override void DoWindowContents(Rect inRect)
            {
                float y = inRect.y;

                // Title
                Text.Font = GameFont.Medium;
                string title = "RimChat_ItemAirdropConfirmTitle".Translate();
                float titleHeight = Text.CalcHeight(title, inRect.width);
                Widgets.Label(new Rect(inRect.x, y, inRect.width, titleHeight), title);
                y += titleHeight + 12f;

                // Key info — large
                Text.Font = GameFont.Medium;
                string mainLine = "RimChat_ItemAirdropConfirmMainLine".Translate(tradeLabel, quantity, paymentTotal);
                float mainHeight = Text.CalcHeight(mainLine, inRect.width - 20f);
                Widgets.Label(new Rect(inRect.x + 10f, y, inRect.width - 20f, mainHeight), mainLine);
                y += mainHeight + 6f;

                // Quantity adjustment note
                Text.Font = GameFont.Tiny;
                Color dimGray = new Color(0.55f, 0.55f, 0.55f);
                Color prevColor = GUI.color;

                if (quantity != requestedQuantity && requestedQuantity > 0)
                {
                    string adjStr = string.IsNullOrWhiteSpace(adjustmentReason)
                        ? "RimChat_ItemAirdropConfirmAdjusted".Translate(requestedQuantity, quantity)
                        : "RimChat_ItemAirdropConfirmAdjustedReason".Translate(requestedQuantity, quantity, adjustmentReason);
                    GUI.color = new Color(0.9f, 0.6f, 0.2f); // amber warning
                    float adjH = Text.CalcHeight(adjStr, inRect.width - 20f);
                    Widgets.Label(new Rect(inRect.x + 10f, y, inRect.width - 20f, adjH), adjStr);
                    y += adjH + 2f;
                }

                // Secondary details
                if (unitPrice > 0f)
                {
                    string unitStr = string.IsNullOrWhiteSpace(priceTag)
                        ? "RimChat_ItemAirdropConfirmUnitPrice".Translate(unitPrice.ToString("F1"))
                        : "RimChat_ItemAirdropConfirmUnitPriceTagged".Translate(unitPrice.ToString("F1"), priceTag);
                    GUI.color = dimGray;
                    float unitH = Text.CalcHeight(unitStr, inRect.width - 20f);
                    Widgets.Label(new Rect(inRect.x + 10f, y, inRect.width - 20f, unitH), unitStr);
                    y += unitH + 2f;
                }

                if (shippingCost > 0)
                {
                    string shipStr = "RimChat_ItemAirdropConfirmShippingShort".Translate(shippingCost, shippingPods);
                    GUI.color = dimGray;
                    float shipH = Text.CalcHeight(shipStr, inRect.width - 20f);
                    Widgets.Label(new Rect(inRect.x + 10f, y, inRect.width - 20f, shipH), shipStr);
                    y += shipH + 2f;
                }

                GUI.color = prevColor;

                // Buttons
                float buttonTop = inRect.yMax - 48f;
                float buttonWidth = (inRect.width - 20f) / 2f;
                Rect confirmRect = new Rect(inRect.x + 5f, buttonTop, buttonWidth, 42f);
                Rect cancelRect = new Rect(confirmRect.xMax + 10f, buttonTop, buttonWidth, 42f);

                Text.Font = GameFont.Medium;
                if (Widgets.ButtonText(confirmRect, "RimChat_ItemAirdropConfirmAccept".Translate()))
                {
                    onConfirm?.Invoke();
                    Close();
                    return;
                }

                if (Widgets.ButtonText(cancelRect, "RimChat_ItemAirdropConfirmCancel".Translate()))
                {
                    onCancel?.Invoke();
                    Close();
                    return;
                }

                if (!optionalAlternativeVisible)
                {
                    return;
                }

                // Low-visibility alternative link — simple label-based button, no Anchor manipulation
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.45f, 0.45f, 0.45f);
                float altY = cancelRect.y - 24f;
                Rect alternativeRect = new Rect(inRect.x, altY, inRect.width, 20f);
                if (Widgets.ButtonText(alternativeRect, "RimChat_ItemAirdropAlternativeLowVisibility".Translate()))
                {
                    onAlternative?.Invoke();
                    Close();
                }
                GUI.color = prevColor;
            }
        }

        private static string BuildPriceSemanticTag(string semantic)
        {
            if (string.IsNullOrWhiteSpace(semantic))
            {
                return string.Empty;
            }

            string lower = semantic.ToLowerInvariant().Trim();
            if (lower.StartsWith("special_item_discount"))
                return "RimChat_ItemAirdropPriceSemanticDiscount".Translate().ToString();
            if (lower.StartsWith("special_item_scarce"))
                return "RimChat_ItemAirdropPriceSemanticScarce".Translate().ToString();
            if (lower.StartsWith("market_value") || lower.StartsWith("market_value_x"))
                return "RimChat_ItemAirdropPriceSemanticMarket".Translate().ToString();
            if (lower.StartsWith("untradeable_x") || lower.StartsWith("black_market"))
                return "RimChat_ItemAirdropPriceSemanticScarce".Translate().ToString();

            return string.Empty;
        }

        private void CommitConfirmedAirdropTrade(
            FactionDialogueSession currentSession,
            Faction currentFaction,
            ItemAirdropPreparedTradeData preparedTrade)
        {
            if (currentSession != null)
            {
                if (currentSession.airdropExecutionStage != AirdropExecutionStage.PreparedAwaitingConfirm)
                {
                    Log.Warning($"[RimChat] AirdropStalePendingBlocked: commit rejected because stage={currentSession.airdropExecutionStage},expected={AirdropExecutionStage.PreparedAwaitingConfirm}");
                    ResetAirdropConfirmationRuntime(currentSession, "commit_rejected_wrong_stage", true, true);
                    TransitionAirdropExecutionStage(currentSession, AirdropExecutionStage.Failed, "commit_rejected_wrong_stage");
                    currentSession.AddMessage(
                        "System",
                        BuildAirdropFailureSystemMessage("selection_manual_choice"),
                        false,
                        DialogueMessageType.System);
                    SaveFactionMemory(currentSession, currentFaction);
                    return;
                }

                if (HasStalePendingAirdropSelection(currentSession, out string staleDetails))
                {
                    Log.Warning($"[RimChat] AirdropStalePendingBlocked: commit rejected because stale pending state survived until confirm. {staleDetails}");
                    ResetAirdropConfirmationRuntime(currentSession, "commit_rejected_stale_pending", true, true);
                    TransitionAirdropExecutionStage(currentSession, AirdropExecutionStage.Failed, staleDetails);
                    currentSession.AddMessage(
                        "System",
                        BuildAirdropFailureSystemMessage("selection_manual_choice"),
                        false,
                        DialogueMessageType.System);
                    SaveFactionMemory(currentSession, currentFaction);
                    return;
                }

                TransitionAirdropExecutionStage(currentSession, AirdropExecutionStage.Committing, preparedTrade?.SelectedDefName ?? "prepared_trade");
            }

            Log.Message($"[RimChat] AirdropConfirmCommitStart: def={preparedTrade?.SelectedDefName ?? "unknown"},count={preparedTrade?.Quantity ?? 0},budget={preparedTrade?.BudgetSilver ?? 0}");
            var commitResult = GameAIInterface.Instance.CommitPreparedItemAirdropTrade(currentFaction, preparedTrade);
            if (commitResult.Success)
            {
                ResetAirdropConfirmationRuntime(currentSession, "commit_success", true, true);
                TransitionAirdropExecutionStage(currentSession, AirdropExecutionStage.Completed, preparedTrade?.SelectedDefName ?? "commit_success");
                var payload = commitResult.Data as ItemAirdropResultData;
                string text = payload != null
                    ? BuildAirdropSuccessSystemMessage(payload)
                    : "RimChat_ItemAirdropCommitSuccessSystem".Translate().ToString();
                currentSession?.AddMessage("System", text, false, DialogueMessageType.System);
                Log.Message($"[RimChat] AirdropConfirmCommitResult: success=True,def={payload?.SelectedDefName ?? preparedTrade?.SelectedDefName ?? "unknown"},count={payload?.Quantity ?? preparedTrade?.Quantity ?? 0},failureCode=none");
            }
            else
            {
                ResetAirdropConfirmationRuntime(currentSession, "commit_failed", true, true);
                string transitionReason = commitResult?.Message ?? "commit_failed";
                var payload = commitResult.Data as ItemAirdropResultData;
                if (!string.IsNullOrWhiteSpace(payload?.FailureCode))
                {
                    transitionReason = payload.FailureCode;
                }

                TransitionAirdropExecutionStage(currentSession, AirdropExecutionStage.Failed, transitionReason);
                if (payload != null && !string.IsNullOrWhiteSpace(payload.FailureCode))
                {
                    currentSession?.AddMessage(
                        "System",
                        BuildAirdropFailureSystemMessage(payload.FailureCode),
                        false,
                        DialogueMessageType.System);
                }
                else
                {
                    string reason = string.IsNullOrWhiteSpace(commitResult?.Message)
                        ? "RimChat_Unknown".Translate().ToString()
                        : commitResult.Message;
                    currentSession?.AddMessage(
                        "System",
                        "RimChat_ItemAirdropCommitFailedSystem".Translate(reason),
                        false,
                        DialogueMessageType.System);
                }

                Log.Message($"[RimChat] AirdropConfirmCommitResult: success=False,def={preparedTrade?.SelectedDefName ?? "unknown"},count={preparedTrade?.Quantity ?? 0},failureCode={payload?.FailureCode ?? "none"},message={commitResult?.Message ?? "none"}");
            }

            SaveFactionMemory(currentSession, currentFaction);
        }

        private static bool IsAirdropDelayedIntent(PendingDelayedActionIntent intent)
        {
            return intent != null &&
                   string.Equals(intent.ActionType, AIActionNames.RequestItemAirdrop, StringComparison.Ordinal);
        }

        private static bool ClearAirdropDelayedIntentRuntime(FactionDialogueSession currentSession)
        {
            if (currentSession == null)
            {
                return false;
            }

            bool cleared = false;
            if (IsAirdropDelayedIntent(currentSession.pendingDelayedActionIntent))
            {
                currentSession.pendingDelayedActionIntent = null;
                cleared = true;
            }

            if (IsAirdropDelayedIntent(currentSession.lastDelayedActionIntent))
            {
                currentSession.lastDelayedActionIntent = null;
                cleared = true;
            }

            return cleared;
        }

        private void CancelConfirmedAirdropTrade(FactionDialogueSession currentSession, Faction currentFaction, bool skipSystemMessage = false)
        {
            bool clearedDelayedIntent = ClearAirdropDelayedIntentRuntime(currentSession);
            ResetAirdropConfirmationRuntime(currentSession, "commit_cancelled", true, true, true);
            TransitionAirdropExecutionStage(currentSession, AirdropExecutionStage.Idle, "player_cancelled_confirmation");
            Log.Message($"[RimChat] AirdropConfirmExplicitCancel: stage={currentSession?.airdropExecutionStage.ToString() ?? "null"},faction={currentFaction?.Name ?? "null"},clearedDelayedIntent={clearedDelayedIntent}");

            if (!skipSystemMessage)
            {
                currentSession?.AddMessage(
                    "System",
                    "RimChat_ItemAirdropCancelledSystem".Translate(),
                    false,
                    DialogueMessageType.System);
            }

            SaveFactionMemory(currentSession, currentFaction);
        }
    }
}
