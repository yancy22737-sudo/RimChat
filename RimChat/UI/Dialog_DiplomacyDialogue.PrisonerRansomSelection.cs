using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using RimChat.AI;
using RimChat.DiplomacySystem;
using RimChat.Dialogue;
using RimChat.Memory;
using RimChat.Prompting;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: pay_prisoner_ransom action flow, PrisonerRansomService eligibility, and diplomacy message image rendering.
    /// Responsibility: enforce prisoner-target selection before ransom execution and publish hostage proof card + reference quote.
    /// </summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private const int RansomProofPortraitSize = 160;
        private const string RansomProofImageSourceUrl = "rimchat://ransom-proof";
        private const string RequestInfoTypePrisoner = "prisoner";
        private const float RansomOfferWindowMinMultiplier = 0.60f;
        private const float RansomOfferWindowMaxMultiplier = 1.40f;
        private const float BatchRansomEstimateMultiplier = 0.80f;
        private const float RansomAutoReplyTimeoutCooldownSeconds = 90f;
        private const string BatchGroupIdParameterKey = "batch_group_id";
        private const string BatchTargetCountParameterKey = "batch_target_count";
        private const string BatchTotalOfferSilverParameterKey = "batch_total_offer_silver";

        private bool TryHandleRequestInfoActionForPrisoner(
            AIAction action,
            FactionDialogueSession currentSession,
            Faction currentFaction,
            out ActionExecutionOutcome outcome)
        {
            outcome = null;
            if (!IsRequestInfoPrisonerAction(action))
            {
                return false;
            }

            if (action.Parameters == null)
            {
                action.Parameters = new Dictionary<string, object>(StringComparer.Ordinal);
            }

            string infoType = ReadStringParameter(action.Parameters, "info_type").Trim().ToLowerInvariant();
            if (!string.Equals(infoType, RequestInfoTypePrisoner, StringComparison.Ordinal))
            {
                Log.Warning($"[RimChat] request_info rejected: unsupported info_type={infoType}");
                outcome = ActionExecutionOutcome.Failure(action, "RimChat_RequestInfoInvalidTypeSystem".Translate().ToString());
                return true;
            }

            action.Parameters["info_type"] = RequestInfoTypePrisoner;
            if (currentSession != null && currentSession.isWaitingForRansomTargetSelection)
            {
                Log.Message("[RimChat] request_info(prisoner) dedup hit: selection already in progress.");
                outcome = ActionExecutionOutcome.Success(action, "RimChat_RansomNeedPrisonerSelectionSystem".Translate().ToString());
                return true;
            }

            if (TryGetPendingRansomBatchSelection(currentSession, out PendingRansomBatchSelection pendingBatch))
            {
                outcome = ActionExecutionOutcome.Success(
                    action,
                    "RimChat_RansomBatchNeedOfferSystem".Translate(
                        pendingBatch.TargetPawnLoadIds.Count,
                        pendingBatch.TotalMinOfferSilver,
                        pendingBatch.TotalMaxOfferSilver,
                        pendingBatch.TotalCurrentAskSilver).ToString());
                return true;
            }

            if (TryUseBoundRansomTarget(currentSession, currentFaction, out int boundTargetId, out Pawn boundTargetPawn))
            {
                string targetLabel = boundTargetPawn?.LabelShortCap ?? "RimChat_Unknown".Translate().ToString();
                Log.Message($"[RimChat] request_info(prisoner) dedup hit: target={boundTargetId}, skipping selection popup.");
                outcome = ActionExecutionOutcome.Success(
                    action,
                    "RimChat_RansomNeedOfferSystem".Translate(targetLabel).ToString());
                return true;
            }

            Log.Message("[RimChat] request_info(prisoner) received.");
            bool started = StartRansomTargetSelection(currentSession, currentFaction, out int candidateCount);
            Log.Message($"[RimChat] request_info(prisoner) candidate_count={candidateCount}, selection_started={started}.");

            if (!started)
            {
                outcome = ActionExecutionOutcome.Failure(action, "RimChat_RansomNoEligiblePrisonerSystem".Translate().ToString());
                return true;
            }

            outcome = ActionExecutionOutcome.Success(action, "RimChat_RansomNeedPrisonerSelectionSystem".Translate().ToString());
            return true;
        }

        private void TryStartManualPrisonerInfoSend()
        {
            if (!CanSendMessageNow() || session == null || faction == null)
            {
                return;
            }

            bool started = StartRansomTargetSelection(session, faction, out int candidateCount, false);
            Log.Message($"[RimChat] manual prisoner info send. candidate_count={candidateCount}, selection_started={started}.");
        }

        private bool TryHandlePrisonerRansomActionWithSelection(
            AIAction action,
            FactionDialogueSession currentSession,
            Faction currentFaction,
            out ActionExecutionOutcome outcome)
        {
            outcome = null;
            if (!IsPayPrisonerRansomAction(action))
            {
                return false;
            }

            if (action.Parameters == null)
            {
                action.Parameters = new Dictionary<string, object>(StringComparer.Ordinal);
            }

            if (!TryEnsureRansomTargetParameter(action, currentSession, currentFaction, out Pawn resolvedTarget, allowSelectionPrompt: true))
            {
                string pendingMessage = ResolveRansomPendingMessage(currentSession);
                currentSession?.AddMessage("System", pendingMessage, false, DialogueMessageType.System);
                Log.Warning("[RimChat] pay_prisoner_ransom pending: missing valid target_pawn_load_id, selection requested.");
                outcome = ActionExecutionOutcome.Failure(action, pendingMessage);
                return true;
            }

            if (!TryReadPositiveInt(action.Parameters, "offer_silver", out _))
            {
                currentSession?.AddMessage(
                    "System",
                    "RimChat_RansomNeedOfferSystem".Translate(resolvedTarget?.LabelShortCap ?? "Unknown").ToString(),
                    false,
                    DialogueMessageType.System);
                outcome = ActionExecutionOutcome.Failure(action, "RimChat_RansomNeedOfferSystem".Translate(resolvedTarget?.LabelShortCap ?? "Unknown").ToString());
                return true;
            }

            string paymentMode = ReadStringParameter(action.Parameters, "payment_mode").Trim();
            if (string.IsNullOrWhiteSpace(paymentMode))
            {
                action.Parameters["payment_mode"] = "silver";
            }

            return false;
        }

        private bool TryEnsureRansomTargetParameter(
            AIAction action,
            FactionDialogueSession currentSession,
            Faction currentFaction,
            out Pawn targetPawn,
            bool allowSelectionPrompt)
        {
            targetPawn = null;
            if (action?.Parameters == null || currentFaction == null)
            {
                return false;
            }

            if (TryReadPositiveInt(action.Parameters, "target_pawn_load_id", out int explicitTargetId) &&
                PrisonerRansomService.TryResolvePawnByLoadId(explicitTargetId, out Pawn explicitTarget) &&
                PrisonerRansomService.IsRansomEligibleTarget(explicitTarget, currentFaction, out _))
            {
                action.Parameters["target_pawn_load_id"] = explicitTargetId;
                BindRansomTarget(currentSession, currentFaction, explicitTargetId);
                targetPawn = explicitTarget;
                return true;
            }

            action.Parameters.Remove("target_pawn_load_id");
            if (HasPendingRansomBatchSelection(currentSession))
            {
                return false;
            }

            if (TryUseBoundRansomTarget(currentSession, currentFaction, out int boundTargetId, out Pawn boundPawn))
            {
                action.Parameters["target_pawn_load_id"] = boundTargetId;
                targetPawn = boundPawn;
                return true;
            }

            if (allowSelectionPrompt &&
                currentSession != null &&
                !currentSession.isWaitingForRansomTargetSelection &&
                !HasPendingRansomBatchSelection(currentSession))
            {
                StartRansomTargetSelection(currentSession, currentFaction, out _);
            }

            return false;
        }

        private bool StartRansomTargetSelection(
            FactionDialogueSession currentSession,
            Faction currentFaction,
            out int candidateCount,
            bool emitSelectionPromptMessage = true)
        {
            candidateCount = 0;
            if (currentSession == null || currentFaction == null)
            {
                return false;
            }

            if (currentSession.isWaitingForRansomTargetSelection)
            {
                Log.Message("[RimChat] request_info(prisoner) dedup hit: selection already in progress.");
                return false;
            }

            if (emitSelectionPromptMessage &&
                TryUseBoundRansomTarget(currentSession, currentFaction, out int boundTargetId, out _))
            {
                candidateCount = 1;
                Log.Message($"[RimChat] request_info(prisoner) dedup hit: reuse bound target={boundTargetId}, skip reselection.");
                return true;
            }

            if (emitSelectionPromptMessage && HasPendingRansomBatchSelection(currentSession))
            {
                if (TryGetPendingRansomBatchSelection(currentSession, out PendingRansomBatchSelection pendingBatch))
                {
                    candidateCount = pendingBatch.TargetPawnLoadIds.Count;
                }

                Log.Message("[RimChat] request_info(prisoner) dedup hit: reuse pending ransom batch selection.");
                return true;
            }

            List<Pawn> candidates = CollectEligibleRansomTargets(currentFaction);
            candidateCount = candidates.Count;
            if (candidates.Count == 0)
            {
                ClearRansomTargetBinding(currentSession);
                ClearPendingRansomBatchSelection(currentSession);
                currentSession?.AddMessage(
                    "System",
                    "RimChat_RansomNoEligiblePrisonerSystem".Translate().ToString(),
                    false,
                    DialogueMessageType.System);
                MarkRansomInfoRequestIncomplete(currentSession);
                return false;
            }

            currentSession.isWaitingForRansomTargetSelection = true;
            MarkRansomInfoRequestIncomplete(currentSession);
            if (emitSelectionPromptMessage)
            {
                currentSession.AddMessage(
                    "System",
                    "RimChat_RansomNeedPrisonerSelectionSystem".Translate().ToString(),
                    false,
                    DialogueMessageType.System);
            }

            Find.WindowStack.Add(new Dialog_PrisonerRansomTargetSelector(
                currentFaction,
                candidates,
                selected => HandleRansomTargetsSelected(currentSession, currentFaction, selected),
                () => HandleRansomTargetSelectionCanceled(currentSession)));
            return true;
        }

        private void HandleRansomTargetsSelected(FactionDialogueSession currentSession, Faction currentFaction, List<Pawn> selectedPawns)
        {
            if (currentSession == null)
            {
                return;
            }

            currentSession.isWaitingForRansomTargetSelection = false;
            if (selectedPawns == null || selectedPawns.Count <= 0 || currentFaction == null)
            {
                return;
            }

            if (selectedPawns.Count == 1)
            {
                HandleRansomTargetSelectedSingle(currentSession, currentFaction, selectedPawns[0]);
                return;
            }

            HandleRansomBatchTargetsSelected(currentSession, currentFaction, selectedPawns);
        }

        private void HandleRansomTargetSelectedSingle(FactionDialogueSession currentSession, Faction currentFaction, Pawn selectedPawn)
        {
            if (currentSession == null || selectedPawn == null || currentFaction == null)
            {
                return;
            }

            if (!PrisonerRansomService.IsRansomEligibleTarget(selectedPawn, currentFaction, out _))
            {
                currentSession.AddMessage(
                    "System",
                    "RimChat_RansomSelectedPrisonerInvalidSystem".Translate().ToString(),
                    false,
                    DialogueMessageType.System);
                ClearRansomTargetBinding(currentSession);
                ClearPendingRansomBatchSelection(currentSession);
                MarkRansomInfoRequestIncomplete(currentSession);
                return;
            }

            ClearPendingRansomBatchSelection(currentSession);
            BindRansomTarget(currentSession, currentFaction, selectedPawn.thingIDNumber);
            MarkRansomInfoRequestCompleted(currentSession, currentFaction, selectedPawn.thingIDNumber);
            Log.Message($"[RimChat] request_info(prisoner) completed. selected_target={selectedPawn.thingIDNumber}.");
            PublishRansomProofCard(currentSession, currentFaction, selectedPawn);
        }

        private void HandleRansomTargetSelectionCanceled(FactionDialogueSession currentSession)
        {
            if (currentSession == null)
            {
                return;
            }

            currentSession.isWaitingForRansomTargetSelection = false;
            ClearRansomTargetBinding(currentSession);
            ClearPendingRansomBatchSelection(currentSession);
            MarkRansomInfoRequestIncomplete(currentSession);
            Log.Message("[RimChat] request_info(prisoner) canceled by player.");
            currentSession.AddMessage(
                "System",
                "RimChat_RansomSelectionCancelledSystem".Translate().ToString(),
                false,
                DialogueMessageType.System);
        }

        private void HandleRansomBatchTargetsSelected(
            FactionDialogueSession currentSession,
            Faction currentFaction,
            List<Pawn> selectedPawns)
        {
            if (currentSession == null || currentFaction == null || selectedPawns == null || selectedPawns.Count <= 1)
            {
                return;
            }

            if (!TryBuildRansomBatchQuoteEntries(currentFaction, selectedPawns, out List<RansomBatchQuoteEntry> entries, out string failureMessage))
            {
                currentSession.AddMessage("System", failureMessage, false, DialogueMessageType.System);
                ClearPendingRansomBatchSelection(currentSession);
                MarkRansomInfoRequestIncomplete(currentSession);
                return;
            }

            int totalCurrentAskSilver = entries.Sum(entry => entry.CurrentAskSilver);
            int totalMinOfferSilver = entries.Sum(entry => entry.MinOfferSilver);
            int totalMaxOfferSilver = entries.Sum(entry => entry.MaxOfferSilver);
            string batchGroupId = Guid.NewGuid().ToString("N");
            List<int> targetIds = entries.Select(entry => entry.TargetPawn.thingIDNumber).ToList();
            currentSession.SetPendingRansomBatchSelection(
                batchGroupId,
                targetIds,
                totalCurrentAskSilver,
                totalMinOfferSilver,
                totalMaxOfferSilver);
            MarkRansomInfoRequestIncomplete(currentSession);
            ClearRansomTargetBinding(currentSession);
            PublishRansomBatchInfoCard(currentSession, currentFaction, entries, totalCurrentAskSilver, totalMinOfferSilver, totalMaxOfferSilver);
        }

        private bool TryBuildRansomBatchQuoteEntries(
            Faction currentFaction,
            List<Pawn> selectedPawns,
            out List<RansomBatchQuoteEntry> entries,
            out string failureMessage)
        {
            entries = new List<RansomBatchQuoteEntry>();
            failureMessage = "RimChat_RansomQuoteUnavailableSystem".Translate().ToString();
            if (currentFaction == null || selectedPawns == null || selectedPawns.Count <= 0)
            {
                return false;
            }

            foreach (Pawn selectedPawn in selectedPawns
                .Where(pawn => pawn != null)
                .GroupBy(pawn => pawn.thingIDNumber)
                .Select(group => group.First()))
            {
                if (!PrisonerRansomService.IsRansomEligibleTarget(selectedPawn, currentFaction, out _))
                {
                    failureMessage = "RimChat_RansomSelectedPrisonerInvalidSystem".Translate().ToString();
                    return false;
                }

                GameAIInterface.Instance.CapturePrisonerInfoCardCoreOrganSnapshot(currentFaction, selectedPawn);
                GameAIInterface.APIResult quoteResult = GameAIInterface.Instance.CalculatePrisonerRansomQuote(
                    currentFaction,
                    selectedPawn,
                    forceRefresh: true);
                if (!quoteResult.Success || !(quoteResult.Data is PrisonerRansomResultData quoteData) || quoteData.CurrentAskSilver <= 0)
                {
                    failureMessage = "RimChat_RansomReferenceAskUnavailableSystem".Translate(selectedPawn.LabelShortCap).ToString();
                    return false;
                }

                if (!TryGetRansomOfferWindow(quoteData, out int minOfferSilver, out int maxOfferSilver))
                {
                    failureMessage = "RimChat_RansomOfferOutOfWindowSimpleSystem".Translate(quoteData.CurrentAskSilver).ToString();
                    return false;
                }

                entries.Add(new RansomBatchQuoteEntry(
                    selectedPawn,
                    ResolveBatchEstimatedAskSilver(quoteData.CurrentAskSilver),
                    minOfferSilver,
                    maxOfferSilver));
            }

            return entries.Count > 1;
        }

        private void PublishRansomBatchInfoCard(
            FactionDialogueSession currentSession,
            Faction currentFaction,
            List<RansomBatchQuoteEntry> entries,
            int totalCurrentAskSilver,
            int totalMinOfferSilver,
            int totalMaxOfferSilver)
        {
            if (currentSession == null || currentFaction == null || entries == null || entries.Count <= 1)
            {
                return;
            }

            string body = BuildRansomBatchInfoCardBody(currentFaction, entries);
            Pawn playerSpeaker = ResolvePlayerSpeakerPawn();
            currentSession.AddMessage(
                ResolvePlayerSenderName(playerSpeaker),
                body,
                true,
                DialogueMessageType.Normal,
                playerSpeaker);

            currentSession.AddMessage(
                "System",
                "RimChat_RansomBatchNeedOfferSystem".Translate(
                    entries.Count,
                    totalMinOfferSilver,
                    totalMaxOfferSilver,
                    totalCurrentAskSilver).ToString(),
                false,
                DialogueMessageType.System);

            TryQueueReplyForPlayerPrisonerInfoCard(body, currentSession, currentFaction);
        }

        private static string BuildRansomBatchInfoCardBody(
            Faction currentFaction,
            List<RansomBatchQuoteEntry> entries)
        {
            if (entries == null || entries.Count <= 0)
            {
                return "RimChat_RansomNoEligiblePrisonerSystem".Translate().ToString();
            }

            string lines = string.Join(
                "\n",
                entries.Select((entry, index) =>
                    "RimChat_RansomBatchListLine".Translate(
                        index + 1,
                        entry.TargetPawn?.LabelShortCap ?? "Unknown",
                        entry.TargetPawn?.thingIDNumber ?? 0,
                        entry.CurrentAskSilver,
                        ResolveBatchHealthPercent(entry.TargetPawn),
                        BuildRansomProofCoreOrganSummary(entry.TargetPawn)).ToString()));
            string factionName = currentFaction?.Name ?? "Unknown";
            return "RimChat_RansomBatchCardBody".Translate(
                factionName,
                entries.Count,
                lines).ToString();
        }

        private static int ResolveBatchHealthPercent(Pawn pawn)
        {
            return Mathf.RoundToInt(Mathf.Clamp01(pawn?.health?.summaryHealth?.SummaryHealthPercent ?? 0f) * 100f);
        }

        private static int ResolveBatchEstimatedAskSilver(int currentAskSilver)
        {
            int normalized = Math.Max(1, currentAskSilver);
            return Math.Max(1, Mathf.RoundToInt(normalized * BatchRansomEstimateMultiplier));
        }

        private void PublishRansomProofCard(FactionDialogueSession currentSession, Faction currentFaction, Pawn selectedPawn)
        {
            GameAIInterface.Instance.CapturePrisonerInfoCardCoreOrganSnapshot(currentFaction, selectedPawn);
            GameAIInterface.APIResult quoteResult = GameAIInterface.Instance.CalculatePrisonerRansomQuote(
                currentFaction,
                selectedPawn,
                forceRefresh: true);
            string currentAskDisplay = ResolveRansomProofCurrentAskDisplay(quoteResult);
            string caption = BuildRansomProofCaption(selectedPawn, currentFaction, currentAskDisplay);
            Pawn playerSpeaker = ResolvePlayerSpeakerPawn();
            bool shouldQueueAutoReply = false;
            if (TryExportRansomProofPortrait(selectedPawn, out string imagePath))
            {
                currentSession.AddImageMessage(
                    ResolvePlayerSenderName(playerSpeaker),
                    caption,
                    true,
                    imagePath,
                    RansomProofImageSourceUrl,
                    playerSpeaker);

                shouldQueueAutoReply = true;
            }
            else
            {
                currentSession.AddMessage("System", caption, false, DialogueMessageType.System);
            }

            if (quoteResult.Success && quoteResult.Data is PrisonerRansomResultData quoteData && quoteData.CurrentAskSilver > 0)
            {
                currentSession.AddMessage(
                    "System",
                    "RimChat_RansomReferenceAskSystem".Translate(selectedPawn.LabelShortCap, quoteData.CurrentAskSilver).ToString(),
                    false,
                    DialogueMessageType.System);

                if (TryGetRansomOfferWindow(quoteData, out int minOffer, out int maxOffer))
                {
                    currentSession.AddMessage(
                        "System",
                        "RimChat_RansomOfferWindowSystem".Translate(
                            selectedPawn.LabelShortCap,
                            minOffer,
                            maxOffer,
                            quoteData.CurrentAskSilver).ToString(),
                        false,
                        DialogueMessageType.System);
                }

                if (shouldQueueAutoReply)
                {
                    TryQueueReplyForPlayerPrisonerInfoCard(caption, currentSession, currentFaction);
                }

                return;
            }

            currentSession.AddMessage(
                "System",
                "RimChat_RansomReferenceAskUnavailableSystem".Translate(selectedPawn.LabelShortCap).ToString(),
                false,
                DialogueMessageType.System);

            if (shouldQueueAutoReply)
            {
                TryQueueReplyForPlayerPrisonerInfoCard(caption, currentSession, currentFaction);
            }
        }

        private void TryQueueReplyForPlayerPrisonerInfoCard(
            string playerMessage,
            FactionDialogueSession currentSession,
            Faction currentFaction)
        {
            if (string.IsNullOrWhiteSpace(playerMessage) || currentSession == null || currentFaction == null)
            {
                return;
            }

            if (IsRansomAutoReplyCoolingDown(currentSession, out float cooldownRemaining))
            {
                Log.Message($"[RimChat] Skipped auto-reply for prisoner info card due to active timeout cooldown. remaining={cooldownRemaining:F1}s.");
                return;
            }

            if (!CanSendMessageNow())
            {
                Log.Warning("[RimChat] Skipped auto-reply for prisoner info card because send gate is blocked.");
                return;
            }

            ClearPendingStrategySuggestions(currentSession);
            if (!AIChatServiceAsync.Instance.IsConfigured())
            {
                AddFallbackResponse(playerMessage);
                return;
            }

            List<ChatMessageData> chatMessages;
            try
            {
                chatMessages = BuildChatMessages(playerMessage);
            }
            catch (PromptRenderException ex)
            {
                HandlePromptRenderFailure(ex);
                return;
            }

            DialogueRuntimeContext requestContext = runtimeContext.WithCurrentRuntimeMarkers();
            bool resolved = DialogueContextResolver.TryResolveLiveContext(
                requestContext,
                out DialogueLiveContext liveContext,
                out string resolveReason);
            string validateReason = string.Empty;
            bool validated = resolved && DialogueContextValidator.ValidateRequestSend(requestContext, liveContext, out validateReason);
            if (!resolved || !validated)
            {
                HandleDroppedRequest(resolveReason, validateReason);
                return;
            }

            bool queued = conversationController.TrySendDialogueRequest(
                currentSession,
                currentFaction,
                chatMessages,
                requestContext,
                windowInstanceId,
                onSuccess: response =>
                {
                    AddAIResponseToSession(response, currentSession, currentFaction, playerMessage);
                },
                onError: error =>
                {
                    if (TryClassifyRansomAutoReplyTimeout(error, out string timeoutClass))
                    {
                        ArmRansomAutoReplyTimeoutCooldown(currentSession, timeoutClass, error);
                    }

                    Log.Warning($"[RimChat] Auto-reply request for prisoner info card failed: {error}");
                    ShowDialogueRequestError(error);
                },
                onProgress: null,
                onDropped: reason =>
                {
                    if (TryClassifyRansomAutoReplyTimeout(reason, out string timeoutClass))
                    {
                        ArmRansomAutoReplyTimeoutCooldown(currentSession, timeoutClass, reason);
                    }

                    HandleDroppedRequest(reason);
                });

            if (!queued)
            {
                if (conversationController.IsRequestDebounced(currentSession) || currentSession.isWaitingForResponse)
                {
                    return;
                }

                Log.Warning("[RimChat] Failed to queue auto-reply request for prisoner info card.");
                ShowDialogueRequestError(currentSession?.aiError);
            }
        }

        private static string BuildRansomProofCaption(Pawn pawn, Faction faction, string currentAskDisplay)
        {
            _ = currentAskDisplay;
            int healthPct = Mathf.RoundToInt(Mathf.Clamp01(pawn?.health?.summaryHealth?.SummaryHealthPercent ?? 0f) * 100f);
            int consciousnessPct = Mathf.RoundToInt(Mathf.Clamp01(ReadCapacitySafe(pawn, PawnCapacityDefOf.Consciousness)) * 100f);
            int age = pawn?.ageTracker?.AgeBiologicalYears ?? 0;
            string sourceFactionName = faction?.Name ?? pawn?.Faction?.Name ?? "Unknown";
            string idDisplay = string.IsNullOrWhiteSpace(pawn?.GetUniqueLoadID())
                ? "RimChat_Unknown".Translate().ToString()
                : pawn.GetUniqueLoadID().Trim();
            string coreOrganSummary = BuildRansomProofCoreOrganSummary(pawn);
            string quote = ResolveRansomProofQuote(pawn);

            return "RimChat_RansomProofCardBody".Translate(
                pawn?.LabelShortCap ?? "Unknown",
                age,
                healthPct,
                consciousnessPct,
                sourceFactionName,
                idDisplay,
                coreOrganSummary,
                quote).ToString();
        }

        private static string BuildRansomProofCoreOrganSummary(Pawn pawn)
        {
            List<RansomCoreOrganSnapshotEntry> snapshot = PrisonerRansomService.CaptureCoreOrganMissingSnapshot(pawn);
            string summary = PrisonerRansomService.FormatCoreOrganMissingSummary(snapshot);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                return summary;
            }

            return "RimChat_RansomCoreOrgansIntact".Translate().ToString();
        }

        private static string ResolveRansomProofCurrentAskDisplay(GameAIInterface.APIResult quoteResult)
        {
            if (quoteResult != null &&
                quoteResult.Success &&
                quoteResult.Data is PrisonerRansomResultData quoteData &&
                quoteData.CurrentAskSilver > 0)
            {
                return quoteData.CurrentAskSilver.ToString(CultureInfo.InvariantCulture);
            }

            return "RimChat_Unknown".Translate().ToString();
        }

        private static bool TryGetRansomOfferWindow(
            PrisonerRansomResultData quoteData,
            out int minOffer,
            out int maxOffer)
        {
            minOffer = 0;
            maxOffer = 0;
            if (quoteData == null || quoteData.NegotiationBaseSnapshot <= 0f)
            {
                return false;
            }

            float baseValue = Math.Max(1f, quoteData.NegotiationBaseSnapshot);
            minOffer = Math.Max(1, Mathf.FloorToInt(baseValue * RansomOfferWindowMinMultiplier));
            maxOffer = Math.Max(minOffer, Mathf.CeilToInt(baseValue * RansomOfferWindowMaxMultiplier));
            return true;
        }

        private static string ResolveRansomProofQuote(Pawn pawn)
        {
            float health = Mathf.Clamp01(pawn?.health?.summaryHealth?.SummaryHealthPercent ?? 0f);
            float consciousness = Mathf.Clamp01(ReadCapacitySafe(pawn, PawnCapacityDefOf.Consciousness));
            if (health <= 0.25f || consciousness <= 0.20f)
            {
                return "RimChat_RansomProofQuoteCritical".Translate().ToString();
            }

            if (health <= 0.55f || consciousness <= 0.50f)
            {
                return "RimChat_RansomProofQuoteInjured".Translate().ToString();
            }

            return "RimChat_RansomProofQuoteHealthy".Translate().ToString();
        }

        private static float ReadCapacitySafe(Pawn pawn, PawnCapacityDef capacity)
        {
            if (pawn?.health?.capacities == null || capacity == null)
            {
                return 0f;
            }

            try
            {
                return pawn.health.capacities.GetLevel(capacity);
            }
            catch
            {
                return 0f;
            }
        }

    }
}
