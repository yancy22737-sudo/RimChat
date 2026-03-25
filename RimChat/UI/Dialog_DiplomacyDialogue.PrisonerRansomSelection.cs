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
        private const float RansomAutoReplyTimeoutCooldownSeconds = 90f;

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
                string pendingMessage = "RimChat_RansomNeedPrisonerSelectionSystem".Translate().ToString();
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
            if (TryUseBoundRansomTarget(currentSession, currentFaction, out int boundTargetId, out Pawn boundPawn))
            {
                action.Parameters["target_pawn_load_id"] = boundTargetId;
                targetPawn = boundPawn;
                return true;
            }

            if (allowSelectionPrompt && currentSession != null && !currentSession.isWaitingForRansomTargetSelection)
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

            List<Pawn> candidates = CollectEligibleRansomTargets(currentFaction);
            candidateCount = candidates.Count;
            if (candidates.Count == 0)
            {
                ClearRansomTargetBinding(currentSession);
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
                selected => HandleRansomTargetSelected(currentSession, currentFaction, selected),
                () => HandleRansomTargetSelectionCanceled(currentSession)));
            return true;
        }

        private void HandleRansomTargetSelected(FactionDialogueSession currentSession, Faction currentFaction, Pawn selectedPawn)
        {
            if (currentSession == null)
            {
                return;
            }

            currentSession.isWaitingForRansomTargetSelection = false;
            if (selectedPawn == null || currentFaction == null)
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
                MarkRansomInfoRequestIncomplete(currentSession);
                return;
            }

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
            MarkRansomInfoRequestIncomplete(currentSession);
            Log.Message("[RimChat] request_info(prisoner) canceled by player.");
            currentSession.AddMessage(
                "System",
                "RimChat_RansomSelectionCancelledSystem".Translate().ToString(),
                false,
                DialogueMessageType.System);
        }

        private void PublishRansomProofCard(FactionDialogueSession currentSession, Faction currentFaction, Pawn selectedPawn)
        {
            GameAIInterface.APIResult quoteResult = GameAIInterface.Instance.CalculatePrisonerRansomQuote(currentFaction, selectedPawn);
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
            int healthPct = Mathf.RoundToInt(Mathf.Clamp01(pawn?.health?.summaryHealth?.SummaryHealthPercent ?? 0f) * 100f);
            int consciousnessPct = Mathf.RoundToInt(Mathf.Clamp01(ReadCapacitySafe(pawn, PawnCapacityDefOf.Consciousness)) * 100f);
            int age = pawn?.ageTracker?.AgeBiologicalYears ?? 0;
            string sourceFactionName = faction?.Name ?? pawn?.Faction?.Name ?? "Unknown";
            string askDisplay = string.IsNullOrWhiteSpace(currentAskDisplay)
                ? "RimChat_Unknown".Translate().ToString()
                : currentAskDisplay.Trim();
            string quote = ResolveRansomProofQuote(pawn);

            return "RimChat_RansomProofCardBody".Translate(
                pawn?.LabelShortCap ?? "Unknown",
                age,
                healthPct,
                consciousnessPct,
                sourceFactionName,
                askDisplay,
                quote).ToString();
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

        private static bool TryExportRansomProofPortrait(Pawn pawn, out string imagePath)
        {
            imagePath = string.Empty;
            if (pawn == null)
            {
                return false;
            }

            Texture portrait;
            try
            {
                portrait = PortraitsCache.Get(
                    pawn,
                    new Vector2(RansomProofPortraitSize, RansomProofPortraitSize),
                    Rot4.South,
                    Vector3.zero,
                    1f);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to capture ransom portrait: {ex.Message}");
                return false;
            }

            if (!TryConvertPortraitToPngBytes(portrait, out byte[] pngBytes) || pngBytes == null || pngBytes.Length == 0)
            {
                return false;
            }

            try
            {
                string folder = Path.Combine(GenFilePaths.SaveDataFolderPath, "RimChat", "Temp", "RansomProof");
                Directory.CreateDirectory(folder);
                int tick = Find.TickManager?.TicksGame ?? 0;
                imagePath = Path.Combine(folder, $"ransom_proof_{pawn.thingIDNumber}_{tick}.png");
                File.WriteAllBytes(imagePath, pngBytes);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to persist ransom portrait: {ex.Message}");
                imagePath = string.Empty;
                return false;
            }
        }

        private static bool TryConvertPortraitToPngBytes(Texture portrait, out byte[] pngBytes)
        {
            pngBytes = null;
            if (portrait == null)
            {
                return false;
            }

            if (portrait is Texture2D texture2D)
            {
                try
                {
                    pngBytes = texture2D.EncodeToPNG();
                    return pngBytes != null && pngBytes.Length > 0;
                }
                catch
                {
                    return false;
                }
            }

            if (!(portrait is RenderTexture renderTexture))
            {
                return false;
            }

            RenderTexture previous = RenderTexture.active;
            Texture2D readable = null;
            try
            {
                RenderTexture.active = renderTexture;
                readable = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
                readable.Apply();
                pngBytes = readable.EncodeToPNG();
                return pngBytes != null && pngBytes.Length > 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                if (readable != null)
                {
                    UnityEngine.Object.Destroy(readable);
                }
            }
        }

        private static List<Pawn> CollectEligibleRansomTargets(Faction sourceFaction)
        {
            if (sourceFaction == null)
            {
                return new List<Pawn>();
            }

            var result = new List<Pawn>();
            var seenIds = new HashSet<int>();
            IEnumerable<Pawn> candidates = (Find.Maps ?? new List<Map>())
                .SelectMany(map => map?.mapPawns?.AllPawnsSpawned ?? Enumerable.Empty<Pawn>());
            foreach (Pawn pawn in candidates)
            {
                if (pawn == null || pawn.thingIDNumber <= 0 || !seenIds.Add(pawn.thingIDNumber))
                {
                    continue;
                }

                if (!PrisonerRansomService.IsRansomEligibleTarget(pawn, sourceFaction, out _))
                {
                    continue;
                }

                result.Add(pawn);
            }

            return result
                .OrderByDescending(p => p.health?.summaryHealth?.SummaryHealthPercent ?? 0f)
                .ThenBy(p => p.LabelShortCap)
                .ToList();
        }

        private static bool TryUseBoundRansomTarget(
            FactionDialogueSession currentSession,
            Faction currentFaction,
            out int targetPawnLoadId,
            out Pawn targetPawn)
        {
            targetPawnLoadId = 0;
            targetPawn = null;
            if (currentSession == null || currentFaction == null)
            {
                return false;
            }

            string factionId = currentFaction.GetUniqueLoadID() ?? string.Empty;
            if (currentSession.boundRansomTargetPawnLoadId <= 0 ||
                !string.Equals(currentSession.boundRansomTargetFactionId ?? string.Empty, factionId, StringComparison.Ordinal))
            {
                return false;
            }

            int boundId = currentSession.boundRansomTargetPawnLoadId;
            if (!PrisonerRansomService.TryResolvePawnByLoadId(boundId, out Pawn boundPawn) ||
                !PrisonerRansomService.IsRansomEligibleTarget(boundPawn, currentFaction, out _))
            {
                ClearRansomTargetBinding(currentSession);
                return false;
            }

            targetPawnLoadId = boundId;
            targetPawn = boundPawn;
            return true;
        }

        private static void BindRansomTarget(FactionDialogueSession currentSession, Faction currentFaction, int pawnLoadId)
        {
            if (currentSession == null || currentFaction == null)
            {
                return;
            }

            currentSession.boundRansomTargetPawnLoadId = Math.Max(0, pawnLoadId);
            currentSession.boundRansomTargetFactionId = currentFaction.GetUniqueLoadID() ?? string.Empty;
        }

        private static void ClearRansomTargetBinding(FactionDialogueSession currentSession)
        {
            if (currentSession == null)
            {
                return;
            }

            currentSession.boundRansomTargetPawnLoadId = 0;
            currentSession.boundRansomTargetFactionId = string.Empty;
        }

        private static void MarkRansomInfoRequestCompleted(FactionDialogueSession currentSession, Faction currentFaction, int selectedPawnLoadId)
        {
            if (currentSession == null || currentFaction == null || selectedPawnLoadId <= 0)
            {
                return;
            }

            currentSession.hasCompletedRansomInfoRequest = true;
            currentSession.boundRansomTargetPawnLoadId = selectedPawnLoadId;
            currentSession.boundRansomTargetFactionId = currentFaction.GetUniqueLoadID() ?? string.Empty;
        }

        private static void MarkRansomInfoRequestIncomplete(FactionDialogueSession currentSession)
        {
            if (currentSession == null)
            {
                return;
            }

            currentSession.hasCompletedRansomInfoRequest = false;
        }

        private static bool HasCompletedRansomInfoRequestForFaction(FactionDialogueSession currentSession, Faction currentFaction)
        {
            if (currentSession == null || currentFaction == null || !currentSession.hasCompletedRansomInfoRequest)
            {
                return false;
            }

            string factionId = currentFaction.GetUniqueLoadID() ?? string.Empty;
            return currentSession.boundRansomTargetPawnLoadId > 0 &&
                string.Equals(currentSession.boundRansomTargetFactionId ?? string.Empty, factionId, StringComparison.Ordinal);
        }

        private static bool IsRequestInfoPrisonerAction(AIAction action)
        {
            return action != null &&
                string.Equals(action.ActionType, AIActionNames.RequestInfo, StringComparison.Ordinal);
        }

        private static void ResetRansomSelectionStateAfterPayment(FactionDialogueSession currentSession)
        {
            if (currentSession == null)
            {
                return;
            }

            currentSession.isWaitingForRansomTargetSelection = false;
            currentSession.hasCompletedRansomInfoRequest = false;
            currentSession.boundRansomTargetPawnLoadId = 0;
            currentSession.boundRansomTargetFactionId = string.Empty;
            Log.Message("[RimChat] pay_prisoner_ransom succeeded. Cleared request_info(prisoner) state.");
        }

        private static bool IsPayPrisonerRansomAction(AIAction action)
        {
            return action != null &&
                   string.Equals(action.ActionType, AIActionNames.PayPrisonerRansom, StringComparison.Ordinal);
        }

        private static bool TryReadPositiveInt(Dictionary<string, object> values, string key, out int parsed)
        {
            parsed = 0;
            if (values == null || string.IsNullOrWhiteSpace(key) || !values.TryGetValue(key, out object raw) || raw == null)
            {
                return false;
            }

            if (raw is int intValue)
            {
                parsed = intValue;
                return parsed > 0;
            }

            if (raw is long longValue)
            {
                if (longValue <= 0 || longValue > int.MaxValue)
                {
                    return false;
                }

                parsed = (int)longValue;
                return true;
            }

            string normalized = (raw.ToString() ?? string.Empty)
                .Trim()
                .Replace(",", string.Empty)
                .Replace("，", string.Empty);

            if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out int directParsed) && directParsed > 0)
            {
                parsed = directParsed;
                return true;
            }

            string digits = new string(normalized.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int recovered) && recovered > 0)
            {
                parsed = recovered;
                return true;
            }

            return false;
        }

        private static bool IsRansomAutoReplyCoolingDown(FactionDialogueSession currentSession, out float remainingSeconds)
        {
            remainingSeconds = 0f;
            if (currentSession == null || currentSession.ransomAutoReplyCooldownUntilRealtime <= 0f)
            {
                return false;
            }

            remainingSeconds = currentSession.ransomAutoReplyCooldownUntilRealtime - Time.realtimeSinceStartup;
            if (remainingSeconds > 0f)
            {
                return true;
            }

            currentSession.ransomAutoReplyCooldownUntilRealtime = -1f;
            currentSession.ransomAutoReplyCooldownCategory = string.Empty;
            remainingSeconds = 0f;
            return false;
        }

        private static bool TryClassifyRansomAutoReplyTimeout(string detail, out string timeoutClass)
        {
            timeoutClass = string.Empty;
            string text = (detail ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (IsQueueTimeoutText(text)) { timeoutClass = "queue_timeout"; return true; }
            if (IsNetworkTimeoutText(text)) { timeoutClass = "network_timeout"; return true; }
            if (IsDropTimeoutText(text)) { timeoutClass = "drop_timeout"; return true; }
            return false;
        }

        private static bool IsQueueTimeoutText(string text)
        {
            return ContainsTimeoutToken(text, "queue") ||
                   ContainsTimeoutToken(text, "排队");
        }

        private static bool IsNetworkTimeoutText(string text)
        {
            return ContainsTimeoutToken(text, "curl error 28") ||
                   ContainsTimeoutToken(text, "request timeout") ||
                   ContainsTimeoutToken(text, "timed out") ||
                   ContainsTimeoutToken(text, "timeout") ||
                   ContainsTimeoutToken(text, "超时");
        }

        private static bool IsDropTimeoutText(string text)
        {
            return ContainsTimeoutToken(text, "dropped") ||
                   ContainsTimeoutToken(text, "pending_request_mismatch") ||
                   ContainsTimeoutToken(text, "request_lease_invalid") ||
                   ContainsTimeoutToken(text, "queue_timeout");
        }

        private static bool ContainsTimeoutToken(string source, string token)
        {
            return !string.IsNullOrWhiteSpace(source) &&
                   !string.IsNullOrWhiteSpace(token) &&
                   source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ArmRansomAutoReplyTimeoutCooldown(
            FactionDialogueSession currentSession,
            string timeoutClass,
            string detail)
        {
            if (currentSession == null)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            float nextDeadline = now + RansomAutoReplyTimeoutCooldownSeconds;
            currentSession.ransomAutoReplyCooldownUntilRealtime =
                Math.Max(currentSession.ransomAutoReplyCooldownUntilRealtime, nextDeadline);
            currentSession.ransomAutoReplyCooldownCategory = timeoutClass ?? string.Empty;

            string summary = (detail ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (summary.Length > 160)
            {
                summary = summary.Substring(0, 160) + "...";
            }

            Log.Warning($"[RimChat] ransom auto-reply timeout classified={timeoutClass} cooldown=90s detail={summary}");
        }

    }

    /// <summary>
    /// Dependencies: Window/Widgets and prisoner candidate projection.
    /// Responsibility: blocking popup to choose one ransom target prisoner from eligible candidates.
    /// </summary>
    internal sealed class Dialog_PrisonerRansomTargetSelector : Window
    {
        private readonly Faction sourceFaction;
        private readonly List<Pawn> candidates;
        private readonly Action<Pawn> onSelect;
        private readonly Action onCancel;
        private Vector2 scrollPosition = Vector2.zero;
        private bool committed;

        public override Vector2 InitialSize => new Vector2(640f, 520f);

        public Dialog_PrisonerRansomTargetSelector(
            Faction sourceFaction,
            List<Pawn> candidates,
            Action<Pawn> onSelect,
            Action onCancel)
        {
            this.sourceFaction = sourceFaction;
            this.candidates = candidates ?? new List<Pawn>();
            this.onSelect = onSelect;
            this.onCancel = onCancel;

            doCloseX = true;
            closeOnCancel = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            forcePause = true;
            onlyOneOfTypeAllowed = true;
            draggable = true;
        }

        public override void PreClose()
        {
            base.PreClose();
            if (!committed)
            {
                onCancel?.Invoke();
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "RimChat_RansomSelectorTitle".Translate());

            Text.Font = GameFont.Small;
            string factionName = sourceFaction?.Name ?? "Unknown";
            Widgets.Label(new Rect(inRect.x, inRect.y + 34f, inRect.width, 24f), "RimChat_RansomSelectorSubtitle".Translate(factionName));

            Rect listRect = new Rect(inRect.x, inRect.y + 62f, inRect.width, inRect.height - 112f);
            DrawList(listRect);

            Rect cancelRect = new Rect(inRect.x + inRect.width - 160f, inRect.yMax - 38f, 160f, 32f);
            if (Widgets.ButtonText(cancelRect, "RimChat_RansomSelectorCancel".Translate()))
            {
                Close();
            }
        }

        private void DrawList(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(6f);
            if (candidates == null || candidates.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.gray;
                Widgets.Label(inner, "RimChat_RansomNoEligiblePrisonerSystem".Translate());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            const float rowHeight = 58f;
            float totalHeight = Mathf.Max(inner.height, candidates.Count * rowHeight);
            Rect viewRect = new Rect(0f, 0f, inner.width - 16f, totalHeight);
            Widgets.BeginScrollView(inner, ref scrollPosition, viewRect);
            float y = 0f;
            foreach (Pawn pawn in candidates)
            {
                DrawCandidateRow(new Rect(0f, y, viewRect.width, rowHeight - 2f), pawn);
                y += rowHeight;
            }

            Widgets.EndScrollView();
        }

        private void DrawCandidateRow(Rect rect, Pawn pawn)
        {
            Widgets.DrawHighlightIfMouseover(rect);
            Widgets.DrawBox(rect, 1);

            string title = pawn?.LabelShortCap ?? "Unknown";
            string health = BuildHealthLine(pawn);
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 126f, 20f), title);

            GUI.color = Color.gray;
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 28f, rect.width - 126f, 20f), health);
            GUI.color = Color.white;

            Rect chooseRect = new Rect(rect.xMax - 108f, rect.y + 12f, 100f, 30f);
            if (!Widgets.ButtonText(chooseRect, "RimChat_RansomSelectorChoose".Translate()))
            {
                return;
            }

            committed = true;
            onSelect?.Invoke(pawn);
            Close();
        }

        private static string BuildHealthLine(Pawn pawn)
        {
            int healthPct = Mathf.RoundToInt(Mathf.Clamp01(pawn?.health?.summaryHealth?.SummaryHealthPercent ?? 0f) * 100f);
            int consciousnessPct = Mathf.RoundToInt(Mathf.Clamp01(ReadCapacitySafe(pawn, PawnCapacityDefOf.Consciousness)) * 100f);
            return "RimChat_RansomSelectorHealthLine".Translate(healthPct, consciousnessPct).ToString();
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
