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
    /// Dependencies: prisoner-ransom portrait export, batch selection state, and batch execution validation.
    /// Responsibility: ransom portrait capture, batch selection state management, and batch execution guardrails.
    /// </summary>
    public partial class Dialog_DiplomacyDialogue
    {
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

        private static string ResolveRansomPendingMessage(FactionDialogueSession currentSession)
        {
            if (!TryGetPendingRansomBatchSelection(currentSession, out PendingRansomBatchSelection pendingBatch))
            {
                return "RimChat_RansomNeedPrisonerSelectionSystem".Translate().ToString();
            }

            return "RimChat_RansomBatchNeedOfferSystem".Translate(
                pendingBatch.TargetPawnLoadIds.Count,
                pendingBatch.TotalMinOfferSilver,
                pendingBatch.TotalMaxOfferSilver,
                pendingBatch.TotalCurrentAskSilver).ToString();
        }

        private static bool HasPendingRansomBatchSelection(FactionDialogueSession currentSession)
        {
            return TryGetPendingRansomBatchSelection(currentSession, out _);
        }

        private static void ClearPendingRansomBatchSelection(FactionDialogueSession currentSession)
        {
            currentSession?.ClearPendingRansomBatchSelection();
        }

        private static void ClearPendingRansomOfferReference(FactionDialogueSession currentSession)
        {
            currentSession?.ClearPendingRansomOfferReference();
        }

        private static bool TryGetPendingRansomBatchSelection(
            FactionDialogueSession currentSession,
            out PendingRansomBatchSelection pendingBatch)
        {
            pendingBatch = null;
            if (currentSession == null ||
                !currentSession.TryGetPendingRansomBatchSelection(
                    out string batchGroupId,
                    out List<int> targetPawnLoadIds,
                    out int totalCurrentAskSilver,
                    out int totalMinOfferSilver,
                    out int totalMaxOfferSilver))
            {
                return false;
            }

            pendingBatch = new PendingRansomBatchSelection(
                batchGroupId,
                targetPawnLoadIds,
                totalCurrentAskSilver,
                totalMinOfferSilver,
                totalMaxOfferSilver);
            return pendingBatch.TargetPawnLoadIds.Count > 0;
        }

        private BatchRansomExecutionPlan BuildBatchRansomExecutionPlan(
            List<AIAction> actions,
            FactionDialogueSession currentSession,
            Faction currentFaction)
        {
            if (!TryGetPendingRansomBatchSelection(currentSession, out PendingRansomBatchSelection pendingBatch))
            {
                return BatchRansomExecutionPlan.Inactive();
            }

            List<AIAction> ransomActions = actions?
                .Where(IsPayPrisonerRansomAction)
                .ToList() ?? new List<AIAction>();
            if (ransomActions.Count <= 0)
            {
                return BatchRansomExecutionPlan.Inactive();
            }

            if (!TryRefreshPendingRansomBatchOfferWindow(currentSession, currentFaction, out pendingBatch, out string refreshError))
            {
                ClearPendingRansomBatchSelection(currentSession);
                MarkRansomInfoRequestIncomplete(currentSession);
                return BatchRansomExecutionPlan.Invalid(ransomActions, refreshError);
            }

            var expectedTargetIds = new HashSet<int>(pendingBatch.TargetPawnLoadIds);
            var actionTargetIds = new Dictionary<AIAction, int>();
            var actionOfferSilver = new Dictionary<AIAction, int>();
            var actualTargetIds = new HashSet<int>();
            int totalOfferSilver = 0;
            foreach (AIAction action in ransomActions)
            {
                if (action?.Parameters == null ||
                    !TryReadPositiveInt(action.Parameters, "target_pawn_load_id", out int targetPawnLoadId))
                {
                    Log.Warning($"[RimChat] pay_prisoner_ransom batch validation failed: missing target_pawn_load_id. expected_targets={FormatRansomBatchTargetIds(expectedTargetIds)}");
                    return BatchRansomExecutionPlan.Invalid(
                        ransomActions,
                        "RimChat_RansomBatchActionMissingTargetSystem".Translate().ToString());
                }

                if (!TryReadPositiveInt(action.Parameters, "offer_silver", out int offerSilver))
                {
                    Log.Warning($"[RimChat] pay_prisoner_ransom batch validation failed: missing offer_silver for target={targetPawnLoadId}. expected_targets={FormatRansomBatchTargetIds(expectedTargetIds)}");
                    return BatchRansomExecutionPlan.Invalid(
                        ransomActions,
                        "RimChat_RansomBatchActionMissingOfferSystem".Translate(targetPawnLoadId).ToString());
                }

                if (!expectedTargetIds.Contains(targetPawnLoadId))
                {
                    Log.Warning($"[RimChat] pay_prisoner_ransom batch validation failed: unexpected target={targetPawnLoadId}. expected_targets={FormatRansomBatchTargetIds(expectedTargetIds)}");
                    return BatchRansomExecutionPlan.Invalid(
                        ransomActions,
                        "RimChat_RansomBatchUnexpectedTargetSystem".Translate(targetPawnLoadId).ToString());
                }

                if (!actualTargetIds.Add(targetPawnLoadId))
                {
                    Log.Warning($"[RimChat] pay_prisoner_ransom batch validation failed: duplicate target={targetPawnLoadId}. expected_targets={FormatRansomBatchTargetIds(expectedTargetIds)}, actual_targets={FormatRansomBatchTargetIds(actualTargetIds)}");
                    return BatchRansomExecutionPlan.Invalid(
                        ransomActions,
                        "RimChat_RansomBatchDuplicateTargetSystem".Translate(targetPawnLoadId).ToString());
                }

                actionTargetIds[action] = targetPawnLoadId;
                actionOfferSilver[action] = offerSilver;
                totalOfferSilver += offerSilver;
            }

            if (!expectedTargetIds.SetEquals(actualTargetIds))
            {
                var missingTargetIds = expectedTargetIds.Except(actualTargetIds);
                var extraTargetIds = actualTargetIds.Except(expectedTargetIds);
                Log.Warning(
                    $"[RimChat] pay_prisoner_ransom batch validation failed: coverage mismatch. " +
                    $"expected_targets={FormatRansomBatchTargetIds(expectedTargetIds)}, " +
                    $"actual_targets={FormatRansomBatchTargetIds(actualTargetIds)}, " +
                    $"missing_targets={FormatRansomBatchTargetIds(missingTargetIds)}, " +
                    $"extra_targets={FormatRansomBatchTargetIds(extraTargetIds)}.");
                return BatchRansomExecutionPlan.Invalid(
                    ransomActions,
                    "RimChat_RansomBatchCoverageMismatchSystem".Translate(
                        expectedTargetIds.Count,
                        actualTargetIds.Count,
                        FormatRansomBatchTargetIds(missingTargetIds),
                        FormatRansomBatchTargetIds(extraTargetIds)).ToString());
            }

            if (!TryNormalizeBatchOfferTotals(
                    ransomActions,
                    actionOfferSilver,
                    pendingBatch,
                    totalOfferSilver,
                    out int normalizedTotalOfferSilver,
                    out string normalizeFailureMessage))
            {
                Log.Warning(
                    "[RimChat] pay_prisoner_ransom batch normalization failed. " +
                    $"total_offer={totalOfferSilver}, window={pendingBatch.TotalMinOfferSilver}-{pendingBatch.TotalMaxOfferSilver}, " +
                    $"expected_targets={FormatRansomBatchTargetIds(expectedTargetIds)}, actual_targets={FormatRansomBatchTargetIds(actualTargetIds)}");
                return BatchRansomExecutionPlan.Invalid(
                    ransomActions,
                    normalizeFailureMessage);
            }

            foreach (AIAction action in ransomActions)
            {
                action.Parameters ??= new Dictionary<string, object>(StringComparer.Ordinal);
                action.Parameters[BatchGroupIdParameterKey] = pendingBatch.BatchGroupId;
                action.Parameters[BatchTargetCountParameterKey] = expectedTargetIds.Count;
                action.Parameters[BatchTotalOfferSilverParameterKey] = normalizedTotalOfferSilver;
            }

            return BatchRansomExecutionPlan.Valid(ransomActions, actionTargetIds, pendingBatch);
        }

        private static bool TryNormalizeBatchOfferTotals(
            List<AIAction> ransomActions,
            Dictionary<AIAction, int> actionOfferSilver,
            PendingRansomBatchSelection pendingBatch,
            int totalOfferSilver,
            out int normalizedTotalOfferSilver,
            out string failureMessage)
        {
            normalizedTotalOfferSilver = Math.Max(0, totalOfferSilver);
            failureMessage = string.Empty;
            if (ransomActions == null || actionOfferSilver == null || pendingBatch == null)
            {
                failureMessage = "RimChat_RansomSystemUnavailableSystem".Translate().ToString();
                return false;
            }

            int targetTotalOfferSilver = Mathf.Clamp(
                totalOfferSilver,
                pendingBatch.TotalMinOfferSilver,
                pendingBatch.TotalMaxOfferSilver);
            normalizedTotalOfferSilver = targetTotalOfferSilver;
            if (targetTotalOfferSilver == totalOfferSilver)
            {
                return true;
            }

            if (!TryBuildNormalizedBatchOfferMap(ransomActions, actionOfferSilver, targetTotalOfferSilver, out Dictionary<AIAction, int> normalizedOffers))
            {
                failureMessage = "RimChat_RansomBatchTotalOutOfWindowSystem".Translate(
                    totalOfferSilver,
                    pendingBatch.TotalMinOfferSilver,
                    pendingBatch.TotalMaxOfferSilver,
                    pendingBatch.TotalCurrentAskSilver).ToString();
                return false;
            }

            foreach (AIAction action in ransomActions)
            {
                action.Parameters ??= new Dictionary<string, object>(StringComparer.Ordinal);
                action.Parameters["offer_silver"] = normalizedOffers[action];
                actionOfferSilver[action] = normalizedOffers[action];
            }

            Log.Message(
                "[RimChat] pay_prisoner_ransom batch total normalized. " +
                $"original_total={totalOfferSilver}, normalized_total={targetTotalOfferSilver}, " +
                $"window={pendingBatch.TotalMinOfferSilver}-{pendingBatch.TotalMaxOfferSilver}, " +
                $"targets={ransomActions.Count}");
            return true;
        }

        private static bool TryBuildNormalizedBatchOfferMap(
            List<AIAction> ransomActions,
            Dictionary<AIAction, int> actionOfferSilver,
            int targetTotalOfferSilver,
            out Dictionary<AIAction, int> normalizedOffers)
        {
            normalizedOffers = new Dictionary<AIAction, int>();
            if (ransomActions == null || actionOfferSilver == null || ransomActions.Count <= 0 || targetTotalOfferSilver <= 0)
            {
                return false;
            }

            int targetCount = ransomActions.Count;
            if (targetTotalOfferSilver < targetCount)
            {
                return false;
            }

            int weightSum = ransomActions.Sum(action => actionOfferSilver.TryGetValue(action, out int offer) ? Math.Max(1, offer) : 1);
            if (weightSum <= 0)
            {
                return false;
            }

            int remainingPool = targetTotalOfferSilver - targetCount;
            int allocated = targetCount;
            var candidates = new List<BatchOfferScaleCandidate>(targetCount);
            for (int i = 0; i < targetCount; i++)
            {
                AIAction action = ransomActions[i];
                int weight = actionOfferSilver.TryGetValue(action, out int offer) ? Math.Max(1, offer) : 1;
                double rawExtra = remainingPool * (double)weight / weightSum;
                int floorExtra = Math.Max(0, (int)Math.Floor(rawExtra));
                int normalized = 1 + floorExtra;
                allocated += floorExtra;
                candidates.Add(new BatchOfferScaleCandidate(action, i, weight, normalized, rawExtra - floorExtra));
            }

            int residual = targetTotalOfferSilver - allocated;
            if (residual < 0)
            {
                return false;
            }

            foreach (BatchOfferScaleCandidate candidate in candidates
                .OrderByDescending(item => item.FractionRemainder)
                .ThenByDescending(item => item.Weight)
                .ThenBy(item => item.Index)
                .Take(residual))
            {
                candidate.NormalizedOffer += 1;
            }

            int finalTotal = 0;
            foreach (BatchOfferScaleCandidate candidate in candidates)
            {
                int safeOffer = Math.Max(1, candidate.NormalizedOffer);
                normalizedOffers[candidate.Action] = safeOffer;
                finalTotal += safeOffer;
            }

            return finalTotal == targetTotalOfferSilver;
        }

        private void HandleBatchRansomPaymentSuccess(
            BatchRansomExecutionPlan plan,
            AIAction action,
            ActionResult result,
            FactionDialogueSession currentSession,
            Faction currentFaction)
        {
            if (currentSession == null || !ShouldResetRansomSelectionStateAfterSuccess(result))
            {
                return;
            }

            if (!plan.TryGetTargetPawnLoadId(action, out int targetPawnLoadId))
            {
                return;
            }

            if (!currentSession.ConsumePendingRansomBatchTarget(targetPawnLoadId))
            {
                return;
            }

            if (!HasPendingRansomBatchSelection(currentSession))
            {
                Log.Message("[RimChat] pay_prisoner_ransom batch completed. Cleared request_info(prisoner) state.");
                ResetRansomSelectionStateAfterPayment(currentSession);
                return;
            }

            if (!TryRefreshPendingRansomBatchOfferWindow(currentSession, currentFaction, out PendingRansomBatchSelection pendingBatch, out string refreshError))
            {
                currentSession.AddMessage("System", refreshError, false, DialogueMessageType.System);
                return;
            }

            currentSession.AddMessage(
                "System",
                "RimChat_RansomBatchRemainingSystem".Translate(
                    pendingBatch.TargetPawnLoadIds.Count,
                    pendingBatch.TotalMinOfferSilver,
                    pendingBatch.TotalMaxOfferSilver,
                    pendingBatch.TotalCurrentAskSilver).ToString(),
                false,
                DialogueMessageType.System);
        }

        private bool TryRefreshPendingRansomBatchOfferWindow(
            FactionDialogueSession currentSession,
            Faction currentFaction,
            out PendingRansomBatchSelection refreshedBatch,
            out string failureMessage)
        {
            refreshedBatch = null;
            failureMessage = "RimChat_RansomQuoteUnavailableSystem".Translate().ToString();
            if (!TryGetPendingRansomBatchSelection(currentSession, out PendingRansomBatchSelection pendingBatch))
            {
                return false;
            }

            if (currentFaction == null)
            {
                failureMessage = "RimChat_RansomSystemUnavailableSystem".Translate().ToString();
                return false;
            }

            int totalCurrentAskSilver = 0;
            int totalMinOfferSilver = 0;
            int totalMaxOfferSilver = 0;
            foreach (int targetPawnLoadId in pendingBatch.TargetPawnLoadIds)
            {
                if (!PrisonerRansomService.TryResolvePawnByLoadId(targetPawnLoadId, out Pawn targetPawn) ||
                    !PrisonerRansomService.IsRansomEligibleTarget(targetPawn, currentFaction, out _))
                {
                    failureMessage = "RimChat_RansomBatchTargetUnavailableSystem".Translate(targetPawnLoadId).ToString();
                    return false;
                }

                GameAIInterface.APIResult quoteResult = GameAIInterface.Instance.CalculatePrisonerRansomQuote(
                    currentFaction,
                    targetPawn,
                    forceRefresh: true);
                if (!quoteResult.Success || !(quoteResult.Data is PrisonerRansomResultData quoteData) || quoteData.CurrentAskSilver <= 0)
                {
                    failureMessage = "RimChat_RansomReferenceAskUnavailableSystem".Translate(targetPawn.LabelShortCap).ToString();
                    return false;
                }

                if (!TryGetRansomOfferWindow(quoteData, out int minOfferSilver, out int maxOfferSilver))
                {
                    failureMessage = "RimChat_RansomOfferOutOfWindowSimpleSystem".Translate(quoteData.CurrentAskSilver).ToString();
                    return false;
                }

                totalCurrentAskSilver += ResolveBatchEstimatedAskSilver(quoteData.CurrentAskSilver);
                totalMinOfferSilver += minOfferSilver;
                totalMaxOfferSilver += maxOfferSilver;
            }

            currentSession.SetPendingRansomBatchSelection(
                pendingBatch.BatchGroupId,
                pendingBatch.TargetPawnLoadIds,
                totalCurrentAskSilver,
                totalMinOfferSilver,
                totalMaxOfferSilver);
            refreshedBatch = new PendingRansomBatchSelection(
                pendingBatch.BatchGroupId,
                pendingBatch.TargetPawnLoadIds,
                totalCurrentAskSilver,
                totalMinOfferSilver,
                totalMaxOfferSilver);
            return true;
        }

        private static string FormatRansomBatchTargetIds(IEnumerable<int> targetPawnLoadIds)
        {
            if (targetPawnLoadIds == null)
            {
                return "none";
            }

            List<int> normalized = targetPawnLoadIds
                .Where(id => id > 0)
                .Distinct()
                .ToList();
            return normalized.Count <= 0
                ? "none"
                : string.Join(",", normalized);
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
            currentSession.ClearPendingRansomBatchSelection();
            currentSession.ClearPendingRansomOfferReference();
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

        private sealed class RansomBatchQuoteEntry
        {
            public RansomBatchQuoteEntry(Pawn targetPawn, int currentAskSilver, int minOfferSilver, int maxOfferSilver)
            {
                TargetPawn = targetPawn;
                CurrentAskSilver = Math.Max(1, currentAskSilver);
                MinOfferSilver = Math.Max(1, minOfferSilver);
                MaxOfferSilver = Math.Max(MinOfferSilver, maxOfferSilver);
            }

            public Pawn TargetPawn { get; }
            public int CurrentAskSilver { get; }
            public int MinOfferSilver { get; }
            public int MaxOfferSilver { get; }
        }

        private sealed class PendingRansomBatchSelection
        {
            public PendingRansomBatchSelection(
                string batchGroupId,
                IEnumerable<int> targetPawnLoadIds,
                int totalCurrentAskSilver,
                int totalMinOfferSilver,
                int totalMaxOfferSilver)
            {
                BatchGroupId = batchGroupId ?? string.Empty;
                TargetPawnLoadIds = (targetPawnLoadIds ?? Enumerable.Empty<int>())
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList();
                TotalCurrentAskSilver = Math.Max(1, totalCurrentAskSilver);
                TotalMinOfferSilver = Math.Max(1, totalMinOfferSilver);
                TotalMaxOfferSilver = Math.Max(TotalMinOfferSilver, totalMaxOfferSilver);
            }

            public string BatchGroupId { get; }
            public List<int> TargetPawnLoadIds { get; }
            public int TotalCurrentAskSilver { get; }
            public int TotalMinOfferSilver { get; }
            public int TotalMaxOfferSilver { get; }
        }

        private sealed class BatchRansomExecutionPlan
        {
            private BatchRansomExecutionPlan()
            {
            }

            public bool IsActive { get; private set; }
            public bool IsValid { get; private set; }
            public string ValidationMessage { get; private set; } = string.Empty;
            public List<AIAction> RansomActions { get; private set; } = new List<AIAction>();
            private Dictionary<AIAction, int> actionTargetIds = new Dictionary<AIAction, int>();
            public PendingRansomBatchSelection BatchSelection { get; private set; }

            public static BatchRansomExecutionPlan Inactive()
            {
                return new BatchRansomExecutionPlan
                {
                    IsActive = false,
                    IsValid = true
                };
            }

            public static BatchRansomExecutionPlan Invalid(List<AIAction> ransomActions, string message)
            {
                return new BatchRansomExecutionPlan
                {
                    IsActive = true,
                    IsValid = false,
                    ValidationMessage = string.IsNullOrWhiteSpace(message)
                        ? "RimChat_RansomSystemUnavailableSystem".Translate().ToString()
                        : message,
                    RansomActions = ransomActions ?? new List<AIAction>()
                };
            }

            public static BatchRansomExecutionPlan Valid(
                List<AIAction> ransomActions,
                Dictionary<AIAction, int> actionTargetIds,
                PendingRansomBatchSelection batchSelection)
            {
                return new BatchRansomExecutionPlan
                {
                    IsActive = true,
                    IsValid = true,
                    RansomActions = ransomActions ?? new List<AIAction>(),
                    actionTargetIds = actionTargetIds ?? new Dictionary<AIAction, int>(),
                    BatchSelection = batchSelection
                };
            }

            public bool TryGetTargetPawnLoadId(AIAction action, out int targetPawnLoadId)
            {
                targetPawnLoadId = 0;
                return action != null &&
                    actionTargetIds != null &&
                    actionTargetIds.TryGetValue(action, out targetPawnLoadId) &&
                    targetPawnLoadId > 0;
            }
        }

        private sealed class BatchOfferScaleCandidate
        {
            public BatchOfferScaleCandidate(
                AIAction action,
                int index,
                int weight,
                int normalizedOffer,
                double fractionRemainder)
            {
                Action = action;
                Index = Math.Max(0, index);
                Weight = Math.Max(1, weight);
                NormalizedOffer = Math.Max(1, normalizedOffer);
                FractionRemainder = Math.Max(0d, fractionRemainder);
            }

            public AIAction Action { get; }
            public int Index { get; }
            public int Weight { get; }
            public int NormalizedOffer { get; set; }
            public double FractionRemainder { get; }
        }

    }
}
