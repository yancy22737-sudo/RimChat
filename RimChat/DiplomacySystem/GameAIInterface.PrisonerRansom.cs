using System;
using System.Collections.Generic;
using RimChat.Config;
using RimChat.Core;
using RimChat.WorldState;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: PrisonerRansomService, RansomContractManager, and DropPodUtility.
    /// Responsibility: orchestrate pay_prisoner_ransom single-submit flow and punishment raid channel.
    /// </summary>
    public partial class GameAIInterface
    {
        private readonly PrisonerRansomService prisonerRansomService = new PrisonerRansomService();
        private const string RansomPaymentModeSilver = "silver";
        private const float BatchRansomReleaseTimeoutMultiplier = 1.5f;

        public void ResetPrisonerRansomRuntimeState()
        {
            prisonerRansomService.ClearAllRuntimeState();
        }

        public void CapturePrisonerInfoCardCoreOrganSnapshot(Faction faction, Pawn targetPawn)
        {
            prisonerRansomService.CaptureInfoCardCoreOrganSnapshot(faction, targetPawn);
        }

        public APIResult PayPrisonerRansom(Faction faction, Dictionary<string, object> parameters)
        {
            APIResult prepareResult = PreparePrisonerRansom(faction, parameters);
            if (!prepareResult.Success)
            {
                return prepareResult;
            }

            if (!(prepareResult.Data is PrisonerRansomPrepareData preparedData))
            {
                return prepareResult;
            }

            return CommitPrisonerRansomAndRelease(faction, preparedData);
        }

        public APIResult PreparePrisonerRansom(Faction faction, Dictionary<string, object> parameters)
        {
            if (!TryValidatePrepareContext(faction, parameters, out RimChatSettings settings, out APIResult validationFailure))
            {
                return validationFailure;
            }

            if (!TryReadIntParameter(parameters, "target_pawn_load_id", out int pawnLoadId) || pawnLoadId <= 0)
            {
                return FailFastRansom(
                    "request_info_required",
                    "RimChat_RansomNeedPrisonerSelectionSystem".Translate().ToString(),
                    "missing or invalid target_pawn_load_id");
            }

            if (!TryReadIntParameter(parameters, "offer_silver", out int offeredSilver) || offeredSilver <= 0)
            {
                return FailFastRansom(
                    "invalid_offer_silver",
                    "RimChat_RansomInvalidOfferSystem".Translate().ToString(),
                    $"invalid offer_silver={ReadString(parameters, "offer_silver")}");
            }

            string paymentModeRaw = ReadString(parameters, "payment_mode");
            string paymentMode = NormalizeRansomPaymentMode(paymentModeRaw, settings);
            if (!string.Equals(paymentMode, RansomPaymentModeSilver, StringComparison.Ordinal))
            {
                string rawDisplay = string.IsNullOrWhiteSpace(paymentModeRaw)
                    ? "unknown"
                    : paymentModeRaw.Trim();
                return FailFastRansom(
                    "failed_invalid_mode",
                    "RimChat_RansomInvalidPaymentModeSystem".Translate(rawDisplay).ToString(),
                    $"invalid payment_mode raw={rawDisplay}, normalized={paymentMode}");
            }

            if (!PrisonerRansomService.TryResolvePawnByLoadId(pawnLoadId, out Pawn targetPawn))
            {
                return FailFastRansom(
                    "request_info_required",
                    "RimChat_RansomNeedPrisonerSelectionSystem".Translate().ToString(),
                    $"target not found: pawn_load_id={pawnLoadId}");
            }

            if (!PrisonerRansomService.IsRansomEligibleTarget(targetPawn, faction, out string eligibilityReason))
            {
                return FailFastRansom(
                    "request_info_required",
                    "RimChat_RansomNeedPrisonerSelectionSystem".Translate().ToString(),
                    $"target not eligible: pawn_load_id={pawnLoadId}, reason={eligibilityReason}");
            }

            string factionId = faction.GetUniqueLoadID() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(factionId) &&
                RansomContractManager.Instance?.HasPendingReleaseContractForTarget(factionId, pawnLoadId) == true)
            {
                return FailFastRansom(
                    "target_already_paid_pending_release",
                    "RimChat_RansomAlreadyPaidPendingReleaseSystem".Translate(targetPawn.LabelShortCap).ToString(),
                    $"duplicate payment blocked: faction_id={factionId}, pawn_load_id={pawnLoadId}");
            }

            if (!prisonerRansomService.TryGetOrCreateNegotiationState(
                faction,
                targetPawn,
                settings,
                out PrisonerRansomNegotiationState state,
                out string negotiationError))
            {
                return FailFastRansom(
                    "quote_unavailable",
                    "RimChat_RansomQuoteUnavailableSystem".Translate().ToString(),
                    $"quote unavailable: {negotiationError}");
            }

            if (!prisonerRansomService.TryValidateOfferWindow(state, offeredSilver, out string offerWindowError))
            {
                if (prisonerRansomService.TryGetOfferWindow(state, out int minOffer, out int maxOffer, out _))
                {
                    return FailFastRansom(
                        "offer_out_of_window",
                        "RimChat_RansomOfferOutOfWindowSystem".Translate(
                            offeredSilver,
                            minOffer,
                            maxOffer,
                            state.CurrentAskSilver).ToString(),
                        $"offer out of window: offered={offeredSilver}, min={minOffer}, max={maxOffer}, current_ask={state.CurrentAskSilver}, detail={offerWindowError}");
                }

                return FailFastRansom(
                    "offer_out_of_window",
                    "RimChat_RansomOfferOutOfWindowSimpleSystem".Translate(offeredSilver).ToString(),
                    $"offer window unavailable: offered={offeredSilver}, detail={offerWindowError}");
            }

            bool hasBatchTargetCount = TryReadIntParameter(parameters, "batch_target_count", out int batchTargetCount);
            bool isBatchRansom = hasBatchTargetCount && batchTargetCount > 1;
            var preparedData = new PrisonerRansomPrepareData
            {
                Faction = faction,
                TargetPawn = targetPawn,
                OfferedSilver = offeredSilver,
                AcceptedSilver = offeredSilver,
                State = state,
                IsBatchRansom = isBatchRansom,
                BatchGroupId = ReadString(parameters, "batch_group_id"),
                BatchTargetCount = Math.Max(0, batchTargetCount)
            };
            return APIResult.SuccessResult("paid_prepared", preparedData);
        }

        internal APIResult CommitPrisonerRansomAndRelease(Faction faction, PrisonerRansomPrepareData preparedData)
        {
            if (faction == null || preparedData?.TargetPawn == null || preparedData.State?.Snapshot == null)
            {
                return FailFastRansom("prepared_data_invalid", "RimChat_RansomSystemUnavailableSystem".Translate().ToString(), "prepared payload invalid");
            }

            if (!PrisonerRansomService.IsRansomEligibleTarget(preparedData.TargetPawn, faction, out string eligibilityReason))
            {
                return FailFastRansom("target_not_eligible", "RimChat_RansomNeedPrisonerSelectionSystem".Translate().ToString(), $"target became invalid before commit: {eligibilityReason}");
            }

            RimChatSettings settings = RimChatMod.Instance?.InstanceSettings;
            if (settings == null)
            {
                return FailFastRansom("settings_unavailable", "RimChat_RansomSystemUnavailableSystem".Translate().ToString(), "settings unavailable");
            }

            Map map = preparedData.TargetPawn.MapHeld;
            if (map == null)
            {
                return FailFastRansom("target_map_invalid", "RimChat_RansomTargetUnavailableSystem".Translate().ToString(), "target pawn map invalid");
            }

            if (!TryDeliverRansomSilver(map, preparedData.AcceptedSilver, settings, out IntVec3 dropCell))
            {
                return FailFastRansom("payment_delivery_failed", "RimChat_RansomPaymentDeliveryFailedSystem".Translate().ToString(), "drop pod delivery failed");
            }

            int paidTick = Find.TickManager?.TicksGame ?? 0;
            RansomContractRecord contract = BuildContract(preparedData, paidTick, settings);
            RansomContractManager.Instance?.RegisterContract(contract);
            prisonerRansomService.ClearState(faction.GetUniqueLoadID(), preparedData.TargetPawn.thingIDNumber);
            prisonerRansomService.ClearInfoCardCoreOrganSnapshot(faction.GetUniqueLoadID(), preparedData.TargetPawn.thingIDNumber);

            string title = "RimChat_PrisonerRansomPaidTitle".Translate();
            string body = "RimChat_PrisonerRansomPaidBody".Translate(
                faction.Name,
                preparedData.TargetPawn.LabelShortCap,
                preparedData.AcceptedSilver,
                dropCell.ToString());
            Find.LetterStack.ReceiveLetter(title, body, LetterDefOf.PositiveEvent, new TargetInfo(dropCell, map), faction);

            RecordAPICall(
                "PayPrisonerRansom",
                true,
                $"faction={faction.Name},targetPawnLoadId={preparedData.TargetPawn.thingIDNumber},acceptedSilver={preparedData.AcceptedSilver},contractId={contract.ContractId}");

            var result = new PrisonerRansomResultData
            {
                StatusCode = "paid_submitted",
                TargetPawnLoadId = preparedData.TargetPawn.thingIDNumber,
                OfferedSilver = preparedData.OfferedSilver,
                AcceptedSilver = preparedData.AcceptedSilver,
                CurrentAskSilver = preparedData.State.CurrentAskSilver,
                FloorSilver = preparedData.State.Snapshot.FloorSilver,
                RoundIndex = 1,
                MaxRounds = 1,
                NegotiationBaseSnapshot = preparedData.State.Snapshot.NegotiationBase,
                DeadlineTick = contract.DeadlineTick,
                ContractId = contract.ContractId
            };
            return APIResult.SuccessResult("paid_submitted", result);
        }

        public APIResult CalculatePrisonerRansomQuote(Faction faction, Pawn targetPawn, bool forceRefresh = false)
        {
            RimChatSettings settings = RimChatMod.Instance?.InstanceSettings;
            if (faction == null || targetPawn == null || settings == null)
            {
                return FailFastRansom("quote_context_invalid", "RimChat_RansomQuoteUnavailableSystem".Translate().ToString(), "quote context invalid");
            }

            if (!prisonerRansomService.TryGetOrCreateNegotiationState(
                faction,
                targetPawn,
                settings,
                out PrisonerRansomNegotiationState state,
                out string error,
                forceRefresh))
            {
                return FailFastRansom("quote_unavailable", "RimChat_RansomQuoteUnavailableSystem".Translate().ToString(), $"quote unavailable: {error}");
            }

            var result = new PrisonerRansomResultData
            {
                StatusCode = "quote_ready",
                TargetPawnLoadId = targetPawn.thingIDNumber,
                CurrentAskSilver = state.CurrentAskSilver,
                FloorSilver = state.Snapshot.FloorSilver,
                RoundIndex = state.CurrentRound,
                MaxRounds = state.MaxRounds,
                NegotiationBaseSnapshot = state.Snapshot.NegotiationBase
            };
            return APIResult.SuccessResult("quote_ready", result);
        }

        public APIResult ApplyRansomPenaltyAndRaid(
            Faction faction,
            int goodwillPenalty,
            bool triggerRaid,
            string reasonTag,
            Pawn targetPawn = null)
        {
            if (faction == null)
            {
                return APIResult.FailureResult("Faction cannot be null.");
            }

            int safePenalty = ResolveSoftenedRansomPenalty(goodwillPenalty, triggerRaid, reasonTag);
            bool forceHostile = triggerRaid;
            int goodwillApplied = ApplyRansomRelationPenalty(faction, safePenalty, forceHostile);

            bool raidTriggered = false;
            if (triggerRaid)
            {
                raidTriggered = DiplomacyEventManager.TriggerRaidEvent(faction, -1f, null, null);
                if (raidTriggered)
                {
                    WorldEventLedgerComponent.Instance?.RecordRaidIntent(faction, delayed: false, "ransom_penalty", "auto");
                }
            }

            RecordAPICall(
                "RansomPenalty",
                true,
                $"faction={faction.Name},penalty={goodwillApplied},raid={raidTriggered},reason={reasonTag},targetPawn={targetPawn?.thingIDNumber ?? -1}");

            return APIResult.SuccessResult(
                "Ransom penalty applied.",
                new
                {
                    Faction = faction.Name,
                    GoodwillPenalty = goodwillApplied,
                    RaidTriggered = raidTriggered,
                    Reason = reasonTag ?? string.Empty
                });
        }

        private RansomContractRecord BuildContract(
            PrisonerRansomPrepareData preparedData,
            int paidTick,
            RimChatSettings settings)
        {
            List<RansomCoreOrganSnapshotEntry> baselineSnapshot = prisonerRansomService
                .ResolveBaselineCoreOrganSnapshot(preparedData.Faction, preparedData.TargetPawn);
            return new RansomContractRecord
            {
                ContractId = Guid.NewGuid().ToString("N"),
                FactionId = preparedData.Faction?.GetUniqueLoadID() ?? string.Empty,
                TargetPawnLoadId = preparedData.TargetPawn.thingIDNumber,
                TargetPawnLabelSnapshot = preparedData.TargetPawn?.LabelShortCap ?? string.Empty,
                NegotiatedValueSnapshot = preparedData.State.Snapshot.NegotiationBase,
                WealthFactorSnapshot = preparedData.State.Snapshot.WealthFactorSnapshot,
                PaidTick = paidTick,
                DeadlineTick = paidTick + ResolveContractTimeoutTicks(preparedData, settings),
                Status = RansomContractStatus.PendingRelease,
                BaselineCoreOrganMissingSnapshot = baselineSnapshot ?? new List<RansomCoreOrganSnapshotEntry>(),
                IsBatchRansom = preparedData.IsBatchRansom,
                BatchGroupId = preparedData.BatchGroupId ?? string.Empty,
                BatchTargetCount = Math.Max(0, preparedData.BatchTargetCount)
            };
        }

        private static int ResolveContractTimeoutTicks(PrisonerRansomPrepareData preparedData, RimChatSettings settings)
        {
            int baseTimeout = Math.Max(1, settings?.RansomReleaseTimeoutTicks ?? 1);
            if (preparedData == null || !preparedData.IsBatchRansom)
            {
                return baseTimeout;
            }

            return Math.Max(baseTimeout, Mathf.CeilToInt(baseTimeout * BatchRansomReleaseTimeoutMultiplier));
        }

        private static string NormalizeRansomPaymentMode(string paymentMode, RimChatSettings settings)
        {
            string mode = string.IsNullOrWhiteSpace(paymentMode)
                ? settings?.RansomPaymentModeDefault ?? RansomPaymentModeSilver
                : paymentMode;
            return mode.Trim().ToLowerInvariant();
        }

        private static APIResult FailFastRansom(string code, string playerMessage, string debugDetail)
        {
            Log.Warning($"[RimChat] pay_prisoner_ransom failed: code={code}, detail={debugDetail ?? "n/a"}");
            return APIResult.FailureResult(playerMessage);
        }

        private static bool TryValidatePrepareContext(
            Faction faction,
            Dictionary<string, object> parameters,
            out RimChatSettings settings,
            out APIResult failure)
        {
            settings = RimChatMod.Instance?.InstanceSettings;
            failure = null;
            if (settings == null)
            {
                failure = FailFastRansom("settings_unavailable", "RimChat_RansomSystemUnavailableSystem".Translate().ToString(), "settings unavailable in prepare");
                return false;
            }

            if (faction == null)
            {
                failure = FailFastRansom("invalid_faction", "RimChat_RansomSystemUnavailableSystem".Translate().ToString(), "faction null");
                return false;
            }

            if (parameters == null)
            {
                failure = FailFastRansom("missing_parameters", "RimChat_RansomNeedPrisonerSelectionSystem".Translate().ToString(), "parameters null");
                return false;
            }

            return true;
        }

        private static int ResolveSoftenedRansomPenalty(int goodwillPenalty, bool triggerRaid, string reasonTag)
        {
            int absolutePenalty = Math.Abs(goodwillPenalty);
            if (absolutePenalty <= 0)
            {
                return 0;
            }

            if (triggerRaid)
            {
                return Math.Max(1, Mathf.RoundToInt(absolutePenalty * 0.25f));
            }

            if (string.Equals(reasonTag, "drop_penalty", StringComparison.Ordinal))
            {
                return Mathf.RoundToInt(absolutePenalty * 0.15f);
            }

            return Math.Max(1, Mathf.RoundToInt(absolutePenalty * 0.20f));
        }

        private static int ApplyRansomRelationPenalty(Faction faction, int goodwillPenalty, bool forceHostile)
        {
            if (faction == null)
            {
                return 0;
            }

            int appliedPenalty = Math.Max(0, goodwillPenalty);
            if (appliedPenalty > 0)
            {
                faction.TryAffectGoodwillWith(Faction.OfPlayer, -appliedPenalty, false, true, null);
            }

            if (forceHostile)
            {
                EnsureHostileRelation(faction);
            }

            return appliedPenalty;
        }

        private static void EnsureHostileRelation(Faction faction)
        {
            if (faction?.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile)
            {
                return;
            }

            try
            {
                faction?.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to force hostile relation for ransom penalty raid: {ex.Message}");
            }
        }

        private static bool TryDeliverRansomSilver(Map map, int silverAmount, RimChatSettings settings, out IntVec3 dropCell)
        {
            dropCell = IntVec3.Invalid;
            if (map == null || silverAmount <= 0)
            {
                return false;
            }

            if (!TryFindAirdropCell(map, out dropCell))
            {
                return false;
            }

            int maxStacks = Mathf.Clamp(settings?.ItemAirdropMaxStacksPerDrop ?? 8, 1, 100);
            List<Thing> silverStacks = BuildStacks(ThingDefOf.Silver, silverAmount, maxStacks);
            if (silverStacks.Count == 0)
            {
                return false;
            }

            DropPodUtility.DropThingsNear(
                dropCell,
                map,
                silverStacks,
                110,
                canInstaDropDuringInit: false,
                leaveSlag: false,
                canRoofPunch: false);
            return true;
        }
    }
}
