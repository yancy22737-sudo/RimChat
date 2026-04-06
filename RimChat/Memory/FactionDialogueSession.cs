using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Dialogue;
using RimChat.DiplomacySystem;
using RimWorld;
using Verse;

namespace RimChat.Memory
{
    public enum AirdropExecutionStage
    {
        Idle = 0,
        SelectingCandidate = 1,
        PreparedAwaitingConfirm = 2,
        Committing = 3,
        Completed = 4,
        Failed = 5,
        Cancelled = 6
    }

    /// <summary>/// store单个factiondialoguesession的数据
 ///</summary>
    public class FactionDialogueSession : IExposable
    {
        public Faction faction;
        public List<DialogueMessageData> messages = new List<DialogueMessageData>();
        public int lastInteractionTick = 0;
        public bool hasUnreadMessages = false;
        public bool isConversationEndedByNpc = false;
        public bool allowReinitiate = false;
        public string conversationEndReason = "";
        public int conversationEndedTick = 0;
        public int reinitiateAvailableTick = 0;

        // AI requeststate (不save到存档, 重启后需要重新request)
        public string pendingRequestId = null;
        public DialogueRequestLease pendingRequestLease = null;
        public bool isWaitingForResponse = false;
        public int lastDiplomacyRequestQueuedTick = int.MinValue;
        public float lastDiplomacyRequestQueuedRealtime = -1f;
        public int pendingImageRequests = 0;
        public float aiRequestProgress = 0f;
        public string aiError = null;
        public string pendingAirdropRequestId = null;
        public DialogueRequestLease pendingAirdropRequestLease = null;
        public bool isWaitingForAirdropSelection = false;
        public float pendingAirdropRequestStartedRealtime = -1f;
        public int pendingAirdropRequestTimeoutSeconds = 0;
        public int airdropRequestGeneration = 0;
        public AirdropExecutionStage airdropExecutionStage = AirdropExecutionStage.Idle;
        public bool isWaitingForRansomTargetSelection = false;
        public int boundRansomTargetPawnLoadId = 0;
        public string boundRansomTargetFactionId = string.Empty;
        public bool hasCompletedRansomInfoRequest = false;
        public float ransomAutoReplyCooldownUntilRealtime = -1f;
        public string ransomAutoReplyCooldownCategory = string.Empty;
        public bool hasPendingRansomBatchSelection = false;
        public string pendingRansomBatchGroupId = string.Empty;
        public List<int> pendingRansomBatchTargetPawnLoadIds = new List<int>();
        public int pendingRansomBatchTotalCurrentAskSilver = 0;
        public int pendingRansomBatchTotalMinOfferSilver = 0;
        public int pendingRansomBatchTotalMaxOfferSilver = 0;

        // Airdrop trade-card runtime reference (not persisted)
        public bool hasPendingAirdropTradeCardReference = false;
        public string pendingAirdropTradeCardNeed = string.Empty;
        public string pendingAirdropTradeCardNeedDefName = string.Empty;
        public string pendingAirdropTradeCardNeedLabel = string.Empty;
        public string pendingAirdropTradeCardNeedSearchText = string.Empty;
        public int pendingAirdropTradeCardRequestedCount = 0;
        public string pendingAirdropTradeCardPaymentItemDef = string.Empty;
        public string pendingAirdropTradeCardPaymentItemLabel = string.Empty;
        public int pendingAirdropTradeCardPaymentItemCount = 0;
        public string pendingAirdropTradeCardScenario = "trade";
        public int pendingAirdropTradeCardSubmittedTick = 0;
        public int pendingAirdropTradeCardShippingPodCount = 0;
        public int pendingAirdropTradeCardShippingCost = 0;

        // Last AI airdrop counteroffer cache (session-scoped)
        public string lastAirdropCounterofferDefName = string.Empty;
        public int lastAirdropCounterofferCount = 0;
        public int lastAirdropCounterofferSilver = 0;
        public string lastAirdropCounterofferReason = string.Empty;
        public int lastAirdropCounterofferTick = 0;
        
        // 策略建议运行态 (不save到存档)
        public List<PendingStrategySuggestion> pendingStrategySuggestions = new List<PendingStrategySuggestion>();
        public int strategyUsesConsumed = 0;

        // 外交延迟动作意图运行态 (不save到存档)
        public PendingDelayedActionIntent pendingDelayedActionIntent;
        public PendingDelayedActionIntent lastDelayedActionIntent;
        public string lastDelayedActionExecutionSignature = string.Empty;
        public int lastDelayedActionExecutionAssistantRound = -999;

        // Periodic snapshot tracking: last message index already summarized to RPG archive
        // Increments on each periodic snapshot, never decreases. Guards against double-summarize.
        public int lastSummarizedMessageIndex = 0;

        public FactionDialogueSession() { }

        public FactionDialogueSession(Faction faction)
        {
            this.faction = faction;
        }

        public void AddMessage(
            string sender,
            string message,
            bool isPlayer,
            DialogueMessageType messageType = DialogueMessageType.Normal,
            Pawn speakerPawn = null)
        {
            var msg = new DialogueMessageData
            {
                sender = sender,
                message = message,
                isPlayer = isPlayer,
                messageType = messageType
            };
            msg.SetSpeakerPawn(speakerPawn);
            msg.SetTimestampFromCurrentGameTick();
            messages.Add(msg);
            lastInteractionTick = Find.TickManager.TicksGame;
            if (isPlayer)
            {
                isConversationEndedByNpc = false;
                allowReinitiate = false;
                conversationEndReason = "";
                conversationEndedTick = 0;
                reinitiateAvailableTick = 0;
            }
            
            // 限制message数量, 避免存档过大
            if (messages.Count > 100)
            {
                messages.RemoveAt(0);
            }
        }

        public void AddImageMessage(
            string sender,
            string caption,
            bool isPlayer,
            string imageLocalPath,
            string imageSourceUrl,
            Pawn speakerPawn = null)
        {
            var msg = new DialogueMessageData
            {
                sender = sender,
                message = caption ?? string.Empty,
                isPlayer = isPlayer,
                messageType = DialogueMessageType.Image,
                imageLocalPath = imageLocalPath ?? string.Empty,
                imageSourceUrl = imageSourceUrl ?? string.Empty
            };
            msg.SetSpeakerPawn(speakerPawn);
            msg.SetTimestampFromCurrentGameTick();
            messages.Add(msg);
            lastInteractionTick = Find.TickManager.TicksGame;

            if (messages.Count > 100)
            {
                messages.RemoveAt(0);
            }
        }

        public void AddAirdropTradeCardMessage(
            string sender,
            string message,
            bool isPlayer,
            string needDefName,
            string needLabel,
            int requestedCount,
            float needUnitPrice,
            float needReferenceTotalPrice,
            int shippingPodCount,
            int shippingCostSilver,
            string offerDefName,
            string offerLabel,
            int offerCount,
            float offerUnitPrice,
            float offerTotalPrice,
            Pawn speakerPawn = null)
        {
            var msg = new DialogueMessageData
            {
                sender = sender,
                message = message ?? string.Empty,
                isPlayer = isPlayer,
                messageType = DialogueMessageType.AirdropTradeCard
            };
            msg.SetAirdropTradeCardData(
                needDefName,
                needLabel,
                requestedCount,
                needUnitPrice,
                needReferenceTotalPrice,
                shippingPodCount,
                shippingCostSilver,
                offerDefName,
                offerLabel,
                offerCount,
                offerUnitPrice,
                offerTotalPrice);
            msg.SetSpeakerPawn(speakerPawn);
            msg.SetTimestampFromCurrentGameTick();
            messages.Add(msg);
            lastInteractionTick = Find.TickManager.TicksGame;

            if (messages.Count > 100)
            {
                messages.RemoveAt(0);
            }
        }

        public void MarkConversationEnded(string reason, bool canReinitiate, int reinitiateCooldownTicks = 0)
        {
            isConversationEndedByNpc = true;
            conversationEndReason = reason ?? "";
            conversationEndedTick = Find.TickManager?.TicksGame ?? 0;
            if (!canReinitiate)
            {
                allowReinitiate = false;
                reinitiateAvailableTick = 0;
                return;
            }

            if (reinitiateCooldownTicks <= 0)
            {
                allowReinitiate = true;
                reinitiateAvailableTick = 0;
                return;
            }

            allowReinitiate = false;
            reinitiateAvailableTick = conversationEndedTick + reinitiateCooldownTicks;
        }

        public void ReinitiateConversation()
        {
            isConversationEndedByNpc = false;
            allowReinitiate = false;
            conversationEndReason = "";
            conversationEndedTick = 0;
            reinitiateAvailableTick = 0;
            pendingImageRequests = 0;
            strategyUsesConsumed = 0;
            pendingStrategySuggestions?.Clear();
            isWaitingForRansomTargetSelection = false;
            boundRansomTargetPawnLoadId = 0;
            boundRansomTargetFactionId = string.Empty;
            hasCompletedRansomInfoRequest = false;
            ransomAutoReplyCooldownUntilRealtime = -1f;
            ransomAutoReplyCooldownCategory = string.Empty;
            ClearPendingRansomBatchSelection();
            ClearPendingAirdropExecutionState();
            ClearPendingAirdropTradeCardReference();
        }

        public void SetPendingAirdropTradeCardReference(
            string need,
            string needDefName,
            string needLabel,
            string needSearchText,
            int requestedCount,
            string paymentItemDef,
            string paymentItemLabel,
            int paymentItemCount,
            string scenario,
            int shippingPodCount = 0,
            int shippingCostSilver = 0)
        {
            hasPendingAirdropTradeCardReference = true;
            pendingAirdropTradeCardNeed = need ?? string.Empty;
            pendingAirdropTradeCardNeedDefName = needDefName ?? string.Empty;
            pendingAirdropTradeCardNeedLabel = needLabel ?? string.Empty;
            pendingAirdropTradeCardNeedSearchText = needSearchText ?? string.Empty;
            pendingAirdropTradeCardRequestedCount = Math.Max(0, requestedCount);
            pendingAirdropTradeCardPaymentItemDef = paymentItemDef ?? string.Empty;
            pendingAirdropTradeCardPaymentItemLabel = paymentItemLabel ?? string.Empty;
            pendingAirdropTradeCardPaymentItemCount = Math.Max(0, paymentItemCount);
            pendingAirdropTradeCardScenario = string.IsNullOrWhiteSpace(scenario) ? "trade" : scenario.Trim();
            pendingAirdropTradeCardSubmittedTick = Find.TickManager?.TicksGame ?? 0;
            pendingAirdropTradeCardShippingPodCount = Math.Max(0, shippingPodCount);
            pendingAirdropTradeCardShippingCost = Math.Max(0, shippingCostSilver);
        }

        public void ClearPendingAirdropTradeCardReference()
        {
            hasPendingAirdropTradeCardReference = false;
            pendingAirdropTradeCardNeed = string.Empty;
            pendingAirdropTradeCardNeedDefName = string.Empty;
            pendingAirdropTradeCardNeedLabel = string.Empty;
            pendingAirdropTradeCardNeedSearchText = string.Empty;
            pendingAirdropTradeCardRequestedCount = 0;
            pendingAirdropTradeCardPaymentItemDef = string.Empty;
            pendingAirdropTradeCardPaymentItemLabel = string.Empty;
            pendingAirdropTradeCardPaymentItemCount = 0;
            pendingAirdropTradeCardScenario = "trade";
            pendingAirdropTradeCardSubmittedTick = 0;
            pendingAirdropTradeCardShippingPodCount = 0;
            pendingAirdropTradeCardShippingCost = 0;
        }

        public void ClearPendingAirdropExecutionState()
        {
            pendingAirdropRequestId = null;
            pendingAirdropRequestLease = null;
            isWaitingForAirdropSelection = false;
            pendingAirdropRequestStartedRealtime = -1f;
            pendingAirdropRequestTimeoutSeconds = 0;
            airdropRequestGeneration++;
            airdropExecutionStage = AirdropExecutionStage.Idle;
            ClearPendingAirdropSelectionIntentState();
        }

        public bool HasPendingAirdropSelectionIntent()
        {
            return HasPendingAirdropSelectionPayload(pendingDelayedActionIntent?.Parameters) ||
                   HasPendingAirdropSelectionPayload(lastDelayedActionIntent?.Parameters);
        }

        public bool ClearPendingAirdropSelectionIntentState()
        {
            bool cleared = false;
            if (HasPendingAirdropSelectionPayload(pendingDelayedActionIntent?.Parameters))
            {
                pendingDelayedActionIntent = null;
                cleared = true;
            }

            if (HasPendingAirdropSelectionPayload(lastDelayedActionIntent?.Parameters))
            {
                lastDelayedActionIntent = null;
                cleared = true;
            }

            return cleared;
        }

        private static bool HasPendingAirdropSelectionPayload(Dictionary<string, object> parameters)
        {
            if (parameters == null)
            {
                return false;
            }

            return parameters.ContainsKey("__airdrop_pending_candidates") ||
                   parameters.ContainsKey("__airdrop_pending_failure_code");
        }

        public bool TryBuildPendingAirdropTradeCardReference(out string referenceBlock)
        {
            referenceBlock = string.Empty;
            if (!hasPendingAirdropTradeCardReference)
            {
                return false;
            }

            string scenario = string.IsNullOrWhiteSpace(pendingAirdropTradeCardScenario)
                ? "trade"
                : pendingAirdropTradeCardScenario.Trim();
            int requestedCount = Math.Max(1, pendingAirdropTradeCardRequestedCount);
            string paymentItem = string.IsNullOrWhiteSpace(pendingAirdropTradeCardPaymentItemDef)
                ? "Silver"
                : pendingAirdropTradeCardPaymentItemDef.Trim();
            int paymentItemCount = Math.Max(1, pendingAirdropTradeCardPaymentItemCount);

            // Resolve live airdrop quote context when possible
            float needUnitValue = 0f;
            float needTotalValue = 0f;
            float offerUnitValue = 0f;
            float offerTotalValue = 0f;
            string needValueSemantic = "market_value";
            string offerValueSemantic = "market_value";
            string needDefName = pendingAirdropTradeCardNeedDefName ?? string.Empty;
            Map map = Find.AnyPlayerHomeMap ?? Find.CurrentMap;
            Pawn negotiator = ItemAirdropTradePolicy.ResolveBestNegotiator(null);
            if (!string.IsNullOrWhiteSpace(needDefName))
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(needDefName);
                if (def != null)
                {
                    if (ItemAirdropTradePolicy.TryResolveNeedUnitPrice(def, out float resolvedNeedUnit, out _))
                    {
                        needUnitValue = resolvedNeedUnit;
                        needValueSemantic = ItemAirdropTradePolicy.IsPreciousMetalFixedPrice(def)
                            ? "market_value"
                            : "market_value_x1.4";
                    }
                    else
                    {
                        needUnitValue = def.BaseMarketValue;
                    }

                    needTotalValue = needUnitValue * requestedCount;
                }
            }

            if (!string.IsNullOrWhiteSpace(paymentItem))
            {
                ThingDef offerDef = DefDatabase<ThingDef>.GetNamedSilentFail(paymentItem);
                if (offerDef != null)
                {
                    if (ItemAirdropTradePolicy.TryResolveOfferUnitPrice(offerDef, out float resolvedOfferUnit, out _))
                    {
                        offerUnitValue = resolvedOfferUnit;
                        offerValueSemantic = ItemAirdropTradePolicy.IsPreciousMetalFixedPrice(offerDef)
                            ? "market_value"
                            : "market_value_x0.6";
                    }
                    else
                    {
                        offerUnitValue = offerDef.BaseMarketValue;
                    }

                    offerTotalValue = offerUnitValue * paymentItemCount;
                }
            }

            int shippingPods = Math.Max(0, pendingAirdropTradeCardShippingPodCount);
            int shippingCost = Math.Max(0, pendingAirdropTradeCardShippingCost);

            referenceBlock =
                "[AirdropTradeCardReference]\n" +
                $"need: {pendingAirdropTradeCardNeed}\n" +
                $"need_def: {needDefName}\n" +
                $"need_label: {pendingAirdropTradeCardNeedLabel}\n" +
                $"need_search_text: {pendingAirdropTradeCardNeedSearchText}\n" +
                $"count: {requestedCount}\n" +
                $"payment_items: [{{\"item\":\"{paymentItem}\",\"count\":{paymentItemCount}}}]\n" +
                $"scenario: {scenario}\n" +
                $"shipping_pods: {shippingPods}\n" +
                $"shipping_cost_silver: {shippingCost}\n" +
                // Hidden context: aligned quote context and role reminder for AI
                "[AirdropHiddenContext]\n" +
                $"need_unit_value: {needUnitValue:F2}\n" +
                $"need_total_value: {needTotalValue:F2}\n" +
                $"need_value_semantic: {needValueSemantic}\n" +
                $"offer_unit_value: {offerUnitValue:F2}\n" +
                $"offer_total_value: {offerTotalValue:F2}\n" +
                $"offer_value_semantic: {offerValueSemantic}\n" +
                $"final_quote_with_shipping: {Math.Max(0f, needTotalValue + shippingCost):F2}\n" +
                "role_reminder: You are the faction providing the requested supplies via emergency airdrop. " +
                "The player is paying you with their offer items. " +
                "Your profit increases when the need items have higher market value. " +
                "The player loses more when they offer higher-value items. " +
                "You may accept the trade if the offer is fair or above market value (emergency premium is acceptable). " +
                "Reject or counter-offer if the player's offer is below market value.\n" +
                "[/AirdropHiddenContext]\n" +
                "[/AirdropTradeCardReference]";
            return true;
        }

        public void SetPendingRansomBatchSelection(
            string batchGroupId,
            IEnumerable<int> targetPawnLoadIds,
            int totalCurrentAskSilver,
            int totalMinOfferSilver,
            int totalMaxOfferSilver)
        {
            List<int> normalizedTargetIds = (targetPawnLoadIds ?? Enumerable.Empty<int>())
                .Where(id => id > 0)
                .Distinct()
                .ToList();
            if (normalizedTargetIds.Count <= 0)
            {
                ClearPendingRansomBatchSelection();
                return;
            }

            int safeMin = Math.Max(1, totalMinOfferSilver);
            int safeMax = Math.Max(safeMin, totalMaxOfferSilver);
            int safeAsk = Math.Max(1, totalCurrentAskSilver);

            hasPendingRansomBatchSelection = true;
            pendingRansomBatchGroupId = string.IsNullOrWhiteSpace(batchGroupId)
                ? Guid.NewGuid().ToString("N")
                : batchGroupId.Trim();
            pendingRansomBatchTargetPawnLoadIds = normalizedTargetIds;
            pendingRansomBatchTotalCurrentAskSilver = safeAsk;
            pendingRansomBatchTotalMinOfferSilver = safeMin;
            pendingRansomBatchTotalMaxOfferSilver = safeMax;
        }

        public void ClearPendingRansomBatchSelection()
        {
            hasPendingRansomBatchSelection = false;
            pendingRansomBatchGroupId = string.Empty;
            pendingRansomBatchTargetPawnLoadIds?.Clear();
            pendingRansomBatchTotalCurrentAskSilver = 0;
            pendingRansomBatchTotalMinOfferSilver = 0;
            pendingRansomBatchTotalMaxOfferSilver = 0;
        }

        public bool TryGetPendingRansomBatchSelection(
            out string batchGroupId,
            out List<int> targetPawnLoadIds,
            out int totalCurrentAskSilver,
            out int totalMinOfferSilver,
            out int totalMaxOfferSilver)
        {
            batchGroupId = pendingRansomBatchGroupId ?? string.Empty;
            targetPawnLoadIds = new List<int>();
            totalCurrentAskSilver = Math.Max(0, pendingRansomBatchTotalCurrentAskSilver);
            totalMinOfferSilver = Math.Max(0, pendingRansomBatchTotalMinOfferSilver);
            totalMaxOfferSilver = Math.Max(0, pendingRansomBatchTotalMaxOfferSilver);
            if (!hasPendingRansomBatchSelection || pendingRansomBatchTargetPawnLoadIds == null)
            {
                return false;
            }

            targetPawnLoadIds = pendingRansomBatchTargetPawnLoadIds
                .Where(id => id > 0)
                .Distinct()
                .ToList();
            if (targetPawnLoadIds.Count <= 0)
            {
                return false;
            }

            return true;
        }

        public bool TryBuildPendingRansomBatchReference(out string referenceBlock)
        {
            referenceBlock = string.Empty;
            if (!TryGetPendingRansomBatchSelection(
                    out string batchGroupId,
                    out List<int> targetPawnLoadIds,
                    out int totalCurrentAskSilver,
                    out int totalMinOfferSilver,
                    out int totalMaxOfferSilver))
            {
                return false;
            }

            string ids = string.Join(",", targetPawnLoadIds);
            referenceBlock =
                "[RansomBatchSelection]\n" +
                $"batch_group_id: {batchGroupId}\n" +
                $"target_count: {targetPawnLoadIds.Count}\n" +
                $"target_pawn_load_ids: [{ids}]\n" +
                $"total_current_ask_silver: {totalCurrentAskSilver}\n" +
                $"total_offer_window_min_silver: {totalMinOfferSilver}\n" +
                $"total_offer_window_max_silver: {totalMaxOfferSilver}\n" +
                "requirement: if any pay_prisoner_ransom action is used in this turn, output one action for EVERY listed target_pawn_load_id exactly once in the same response.\n" +
                "requirement: the sum of offer_silver across those actions must be inside [total_offer_window_min_silver, total_offer_window_max_silver].\n" +
                "[/RansomBatchSelection]";
            return true;
        }

        public bool TryGetRansomSessionState(
            string currentFactionId,
            out int currentRequestTargetPawnLoadId,
            out bool hasUnpaidRansomRequest)
        {
            currentRequestTargetPawnLoadId = 0;
            hasUnpaidRansomRequest = false;
            if (string.IsNullOrWhiteSpace(currentFactionId))
            {
                return false;
            }

            bool hasBoundTargetForFaction =
                hasCompletedRansomInfoRequest &&
                boundRansomTargetPawnLoadId > 0 &&
                string.Equals(boundRansomTargetFactionId ?? string.Empty, currentFactionId, StringComparison.Ordinal);
            if (hasBoundTargetForFaction)
            {
                currentRequestTargetPawnLoadId = boundRansomTargetPawnLoadId;
            }

            hasUnpaidRansomRequest =
                isWaitingForRansomTargetSelection ||
                hasPendingRansomBatchSelection ||
                hasBoundTargetForFaction;
            return true;
        }

        public bool ConsumePendingRansomBatchTarget(int targetPawnLoadId)
        {
            if (targetPawnLoadId <= 0 || !hasPendingRansomBatchSelection || pendingRansomBatchTargetPawnLoadIds == null)
            {
                return false;
            }

            bool removed = pendingRansomBatchTargetPawnLoadIds.Remove(targetPawnLoadId);
            if (!removed)
            {
                return false;
            }

            pendingRansomBatchTargetPawnLoadIds = pendingRansomBatchTargetPawnLoadIds
                .Where(id => id > 0)
                .Distinct()
                .ToList();
            if (pendingRansomBatchTargetPawnLoadIds.Count <= 0)
            {
                ClearPendingRansomBatchSelection();
            }

            return true;
        }

        public void CacheAirdropCounteroffer(string defName, int count, int silver, string reason)
        {
            if (string.IsNullOrWhiteSpace(defName) || count <= 0 || silver < 0)
            {
                return;
            }

            lastAirdropCounterofferDefName = defName.Trim();
            lastAirdropCounterofferCount = Math.Max(1, count);
            lastAirdropCounterofferSilver = Math.Max(0, silver);
            lastAirdropCounterofferReason = reason ?? string.Empty;
            lastAirdropCounterofferTick = Find.TickManager?.TicksGame ?? 0;
        }

        public bool HasPendingImageRequests()
        {
            return pendingImageRequests > 0;
        }

        public void BeginImageRequest()
        {
            if (pendingImageRequests < int.MaxValue)
            {
                pendingImageRequests++;
            }
        }

        public void EndImageRequest()
        {
            pendingImageRequests = Math.Max(0, pendingImageRequests - 1);
        }

        public bool IsReinitiateAvailable(int currentTick)
        {
            if (!isConversationEndedByNpc)
            {
                return false;
            }

            if (allowReinitiate)
            {
                return true;
            }

            if (reinitiateAvailableTick > 0 && currentTick >= reinitiateAvailableTick)
            {
                allowReinitiate = true;
                reinitiateAvailableTick = 0;
                return true;
            }

            return false;
        }

        public int GetReinitiateRemainingTicks(int currentTick)
        {
            if (allowReinitiate || reinitiateAvailableTick <= 0)
            {
                return 0;
            }

            return Math.Max(0, reinitiateAvailableTick - currentTick);
        }

        public void MarkAsRead()
        {
            hasUnreadMessages = false;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref faction, "faction");
            Scribe_Collections.Look(ref messages, "messages", LookMode.Deep);
            Scribe_Values.Look(ref lastInteractionTick, "lastInteractionTick", 0);
            Scribe_Values.Look(ref hasUnreadMessages, "hasUnreadMessages", false);
            Scribe_Values.Look(ref isConversationEndedByNpc, "isConversationEndedByNpc", false);
            Scribe_Values.Look(ref allowReinitiate, "allowReinitiate", false);
            Scribe_Values.Look(ref conversationEndReason, "conversationEndReason", "");
            Scribe_Values.Look(ref conversationEndedTick, "conversationEndedTick", 0);
            Scribe_Values.Look(ref reinitiateAvailableTick, "reinitiateAvailableTick", 0);
            Scribe_Values.Look(ref lastSummarizedMessageIndex, "lastSummarizedMessageIndex", 0);
            Scribe_Values.Look(ref lastAirdropCounterofferDefName, "lastAirdropCounterofferDefName", string.Empty);
            Scribe_Values.Look(ref lastAirdropCounterofferCount, "lastAirdropCounterofferCount", 0);
            Scribe_Values.Look(ref lastAirdropCounterofferSilver, "lastAirdropCounterofferSilver", 0);
            Scribe_Values.Look(ref lastAirdropCounterofferReason, "lastAirdropCounterofferReason", string.Empty);
            Scribe_Values.Look(ref lastAirdropCounterofferTick, "lastAirdropCounterofferTick", 0);
            Scribe_Values.Look(ref hasPendingRansomBatchSelection, "hasPendingRansomBatchSelection", false);
            Scribe_Values.Look(ref pendingRansomBatchGroupId, "pendingRansomBatchGroupId", string.Empty);
            Scribe_Collections.Look(ref pendingRansomBatchTargetPawnLoadIds, "pendingRansomBatchTargetPawnLoadIds", LookMode.Value);
            Scribe_Values.Look(ref pendingRansomBatchTotalCurrentAskSilver, "pendingRansomBatchTotalCurrentAskSilver", 0);
            Scribe_Values.Look(ref pendingRansomBatchTotalMinOfferSilver, "pendingRansomBatchTotalMinOfferSilver", 0);
            Scribe_Values.Look(ref pendingRansomBatchTotalMaxOfferSilver, "pendingRansomBatchTotalMaxOfferSilver", 0);
            pendingRansomBatchTargetPawnLoadIds ??= new List<int>();
        }
    }

    /// <summary>/// message类型枚举
 ///</summary>
    public enum DialogueMessageType
    {
        Normal,       // 普通message (玩家/AI dialogue)
        System,       // Systemmessage (通知, error提示等)
        Image,        // Inline image card message
        AirdropTradeCard  // 物资空投交易卡片消息
    }

    /// <summary>/// 运行态策略建议 (来自 LLM)
 ///</summary>
    public class PendingStrategySuggestion
    {
        public string StrategyName = string.Empty;
        public string FactReason = string.Empty;
        public List<string> StrategyKeywords = new List<string>();
        public string Content = string.Empty;
    }

    /// <summary>/// 外交延迟动作运行态意图（不持久化）。
    ///</summary>
    public class PendingDelayedActionIntent
    {
        public string ActionType = string.Empty;
        public Dictionary<string, object> Parameters = new Dictionary<string, object>();
        public string Signature = string.Empty;
        public string RequiredParameter = string.Empty;
        public bool AwaitingConfirmation;
        public int CreatedAssistantRound;
        public int UpdatedAssistantRound;

        public PendingDelayedActionIntent Clone()
        {
            var clone = new PendingDelayedActionIntent
            {
                ActionType = ActionType ?? string.Empty,
                Signature = Signature ?? string.Empty,
                RequiredParameter = RequiredParameter ?? string.Empty,
                AwaitingConfirmation = AwaitingConfirmation,
                CreatedAssistantRound = CreatedAssistantRound,
                UpdatedAssistantRound = UpdatedAssistantRound,
                Parameters = new Dictionary<string, object>()
            };

            if (Parameters != null)
            {
                foreach (KeyValuePair<string, object> entry in Parameters)
                {
                    clone.Parameters[entry.Key] = entry.Value;
                }
            }

            return clone;
        }
    }

    /// <summary>/// 可序列化的dialoguemessage数据
 ///</summary>
    public class DialogueMessageData : IExposable
    {
        public string sender;
        public string message;
        public bool isPlayer;
        public DateTime timestamp;
        public DialogueMessageType messageType;
        public string imageLocalPath;
        public string imageSourceUrl;
        public string speakerPawnThingId;
        private Pawn speakerPawn;

        private int gameTick;

        public string airdropNeedDefName;
        public string airdropNeedLabel;
        public int airdropRequestedCount;
        public float airdropNeedUnitPrice;
        public float airdropNeedReferenceTotalPrice;
        public int airdropShippingPodCount;
        public int airdropShippingCostSilver;
        public string airdropOfferDefName;
        public string airdropOfferLabel;
        public int airdropOfferCount;
        public float airdropOfferUnitPrice;
        public float airdropOfferTotalPrice;

        public DialogueMessageData()
        {
            messageType = DialogueMessageType.Normal;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref sender, "sender", "");
            Scribe_Values.Look(ref message, "message", "");
            Scribe_Values.Look(ref isPlayer, "isPlayer", false);
            Scribe_Values.Look(ref gameTick, "gameTick", 0);
            Scribe_Values.Look(ref messageType, "messageType", DialogueMessageType.Normal);
            Scribe_Values.Look(ref imageLocalPath, "imageLocalPath", string.Empty);
            Scribe_Values.Look(ref imageSourceUrl, "imageSourceUrl", string.Empty);
            Scribe_Values.Look(ref speakerPawnThingId, "speakerPawnThingId", string.Empty);
            Scribe_References.Look(ref speakerPawn, "speakerPawn");

            Scribe_Values.Look(ref airdropNeedDefName, "airdropNeedDefName", string.Empty);
            Scribe_Values.Look(ref airdropNeedLabel, "airdropNeedLabel", string.Empty);
            Scribe_Values.Look(ref airdropRequestedCount, "airdropRequestedCount", 0);
            Scribe_Values.Look(ref airdropNeedUnitPrice, "airdropNeedUnitPrice", 0f);
            Scribe_Values.Look(ref airdropNeedReferenceTotalPrice, "airdropNeedReferenceTotalPrice", 0f);
            Scribe_Values.Look(ref airdropShippingPodCount, "airdropShippingPodCount", 0);
            Scribe_Values.Look(ref airdropShippingCostSilver, "airdropShippingCostSilver", 0);
            Scribe_Values.Look(ref airdropOfferDefName, "airdropOfferDefName", string.Empty);
            Scribe_Values.Look(ref airdropOfferLabel, "airdropOfferLabel", string.Empty);
            Scribe_Values.Look(ref airdropOfferCount, "airdropOfferCount", 0);
            Scribe_Values.Look(ref airdropOfferUnitPrice, "airdropOfferUnitPrice", 0f);
            Scribe_Values.Look(ref airdropOfferTotalPrice, "airdropOfferTotalPrice", 0f);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                timestamp = new DateTime(gameTick);
            }
        }

        public void SetTimestampFromCurrentGameTick()
        {
            gameTick = Find.TickManager.TicksGame;
            timestamp = new DateTime(gameTick);
        }

        public int GetGameTick()
        {
            return gameTick;
        }

        public bool IsSystemMessage()
        {
            return messageType == DialogueMessageType.System;
        }

        public bool HasInlineImage()
        {
            return messageType == DialogueMessageType.Image &&
                   !string.IsNullOrWhiteSpace(imageLocalPath);
        }

        public bool IsAirdropTradeCard()
        {
            return messageType == DialogueMessageType.AirdropTradeCard &&
                   !string.IsNullOrWhiteSpace(airdropNeedDefName);
        }

        public void SetAirdropTradeCardData(
            string needDefName,
            string needLabel,
            int requestedCount,
            float needUnitPrice,
            float needReferenceTotalPrice,
            int shippingPodCount,
            int shippingCostSilver,
            string offerDefName,
            string offerLabel,
            int offerCount,
            float offerUnitPrice,
            float offerTotalPrice)
        {
            messageType = DialogueMessageType.AirdropTradeCard;
            airdropNeedDefName = needDefName ?? string.Empty;
            airdropNeedLabel = needLabel ?? string.Empty;
            airdropRequestedCount = Math.Max(0, requestedCount);
            airdropNeedUnitPrice = Math.Max(0f, needUnitPrice);
            airdropNeedReferenceTotalPrice = Math.Max(0f, needReferenceTotalPrice);
            airdropShippingPodCount = Math.Max(0, shippingPodCount);
            airdropShippingCostSilver = Math.Max(0, shippingCostSilver);
            airdropOfferDefName = offerDefName ?? string.Empty;
            airdropOfferLabel = offerLabel ?? string.Empty;
            airdropOfferCount = Math.Max(0, offerCount);
            airdropOfferUnitPrice = Math.Max(0f, offerUnitPrice);
            airdropOfferTotalPrice = Math.Max(0f, offerTotalPrice);
        }

        public void SetSpeakerPawn(Pawn pawn)
        {
            speakerPawn = pawn;
            speakerPawnThingId = pawn?.ThingID ?? string.Empty;
        }

        public Pawn ResolveSpeakerPawn()
        {
            if (IsPawnReferenceValid(speakerPawn))
            {
                if (string.IsNullOrWhiteSpace(speakerPawnThingId))
                {
                    speakerPawnThingId = speakerPawn.ThingID;
                }
                return speakerPawn;
            }

            if (string.IsNullOrWhiteSpace(speakerPawnThingId))
            {
                speakerPawn = null;
                return null;
            }

            speakerPawn = ResolvePawnByThingId(speakerPawnThingId);
            return speakerPawn;
        }

        private static Pawn ResolvePawnByThingId(string thingId)
        {
            if (string.IsNullOrWhiteSpace(thingId))
            {
                return null;
            }

            Pawn worldPawn = Find.WorldPawns?.AllPawnsAliveOrDead?
                .FirstOrDefault(pawn => string.Equals(pawn?.ThingID, thingId, StringComparison.Ordinal));
            if (IsPawnReferenceValid(worldPawn))
            {
                return worldPawn;
            }

            foreach (Map map in Find.Maps ?? Enumerable.Empty<Map>())
            {
                Pawn mapPawn = map?.mapPawns?.AllPawnsSpawned?
                    .FirstOrDefault(pawn => string.Equals(pawn?.ThingID, thingId, StringComparison.Ordinal));
                if (IsPawnReferenceValid(mapPawn))
                {
                    return mapPawn;
                }
            }

            return null;
        }

        private static bool IsPawnReferenceValid(Pawn pawn)
        {
            return pawn != null && !pawn.Destroyed && !pawn.Dead;
        }
    }
}
