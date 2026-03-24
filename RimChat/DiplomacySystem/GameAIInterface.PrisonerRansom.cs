using System;
using System.Collections.Generic;
using RimChat.Config;
using RimChat.Core;
using RimChat.WorldState;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: PrisonerRansomService, RansomContractManager, DropPodUtility, vanilla ReleasePrisoner job.
    /// Responsibility: orchestrate pay_prisoner_ransom prepare/commit flow and punishment raid channel.
    /// </summary>
    public partial class GameAIInterface
    {
        private readonly PrisonerRansomService prisonerRansomService = new PrisonerRansomService();
        private const string RansomPaymentModeSilver = "silver";

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
                return FailFastRansom("invalid_target_pawn_load_id", "pay_prisoner_ransom requires positive int parameter target_pawn_load_id.");
            }

            if (!TryReadIntParameter(parameters, "offer_silver", out int offeredSilver) || offeredSilver <= 0)
            {
                return FailFastRansom("invalid_offer_silver", "pay_prisoner_ransom requires positive int parameter offer_silver.");
            }

            string paymentMode = NormalizeRansomPaymentMode(ReadString(parameters, "payment_mode"), settings);
            if (!string.Equals(paymentMode, RansomPaymentModeSilver, StringComparison.Ordinal))
            {
                return FailFastRansom("failed_invalid_mode", "Only payment_mode=silver is supported in current build.");
            }

            if (!PrisonerRansomService.TryResolvePawnByLoadId(pawnLoadId, out Pawn targetPawn))
            {
                return FailFastRansom("target_pawn_not_found", $"No pawn found for target_pawn_load_id={pawnLoadId}.");
            }

            if (!PrisonerRansomService.IsRansomEligibleTarget(targetPawn, faction, out string eligibilityReason))
            {
                return FailFastRansom("target_not_eligible", $"Target pawn is not eligible for ransom: {eligibilityReason}.");
            }

            if (!prisonerRansomService.TryGetOrCreateNegotiationState(
                faction,
                targetPawn,
                settings,
                out PrisonerRansomNegotiationState state,
                out string negotiationError))
            {
                return FailFastRansom("quote_unavailable", $"Failed to prepare ransom quote: {negotiationError}.");
            }

            if (!prisonerRansomService.TryValidateOfferWindow(state, offeredSilver, out string offerWindowError))
            {
                return FailFastRansom("offer_out_of_window", $"offer_silver is outside allowed range: {offerWindowError}.");
            }

            PrisonerRansomResultData offerResult = prisonerRansomService.EvaluateOffer(state, offeredSilver);
            if (string.Equals(offerResult.StatusCode, "counter_offer", StringComparison.Ordinal))
            {
                return APIResult.SuccessResult("counter_offer", offerResult);
            }

            if (string.Equals(offerResult.StatusCode, "rejected_floor_not_met", StringComparison.Ordinal))
            {
                prisonerRansomService.ClearState(faction.GetUniqueLoadID(), targetPawn.thingIDNumber);
                return APIResult.SuccessResult("rejected_floor_not_met", offerResult);
            }

            if (!PrisonerRansomService.TryPrecheckRelease(targetPawn, out Pawn warden, out string precheckReason))
            {
                return FailFastRansom("failed_precheck", $"Release precheck failed: {precheckReason}.");
            }

            var preparedData = new PrisonerRansomPrepareData
            {
                Faction = faction,
                TargetPawn = targetPawn,
                AssignedWarden = warden,
                OfferedSilver = offeredSilver,
                AcceptedSilver = offerResult.AcceptedSilver,
                State = state
            };
            return APIResult.SuccessResult("accepted_prepared", preparedData);
        }

        internal APIResult CommitPrisonerRansomAndRelease(Faction faction, PrisonerRansomPrepareData preparedData)
        {
            if (faction == null || preparedData?.TargetPawn == null || preparedData.State?.Snapshot == null)
            {
                return FailFastRansom("prepared_data_invalid", "Prepared ransom payload is invalid.");
            }

            if (!PrisonerRansomService.IsRansomEligibleTarget(preparedData.TargetPawn, faction, out string eligibilityReason))
            {
                return FailFastRansom("failed_precheck", $"Target eligibility changed before commit: {eligibilityReason}.");
            }

            if (!PrisonerRansomService.TryPrecheckRelease(preparedData.TargetPawn, out Pawn warden, out string precheckReason))
            {
                return FailFastRansom("failed_precheck", $"Release precheck failed: {precheckReason}.");
            }

            RimChatSettings settings = RimChatMod.Instance?.InstanceSettings;
            if (settings == null)
            {
                return FailFastRansom("settings_unavailable", "Settings not initialized.");
            }

            Map map = preparedData.TargetPawn.MapHeld;
            if (map == null)
            {
                return FailFastRansom("failed_precheck", "Target pawn is not on a valid map.");
            }

            if (!TryDeliverRansomSilver(map, preparedData.AcceptedSilver, settings, out IntVec3 dropCell))
            {
                return FailFastRansom("payment_delivery_failed", "Failed to deliver ransom silver via drop pod.");
            }

            IssueReleaseJob(warden, preparedData.TargetPawn);
            int paidTick = Find.TickManager?.TicksGame ?? 0;
            RansomContractRecord contract = BuildContract(preparedData, paidTick, settings);
            RansomContractManager.Instance?.RegisterContract(contract);
            prisonerRansomService.ClearState(faction.GetUniqueLoadID(), preparedData.TargetPawn.thingIDNumber);

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
                StatusCode = "accepted_and_released",
                TargetPawnLoadId = preparedData.TargetPawn.thingIDNumber,
                OfferedSilver = preparedData.OfferedSilver,
                AcceptedSilver = preparedData.AcceptedSilver,
                CurrentAskSilver = preparedData.State.CurrentAskSilver,
                FloorSilver = preparedData.State.Snapshot.FloorSilver,
                RoundIndex = preparedData.State.CurrentRound,
                MaxRounds = preparedData.State.MaxRounds,
                NegotiationBaseSnapshot = preparedData.State.Snapshot.NegotiationBase,
                DeadlineTick = contract.DeadlineTick,
                ContractId = contract.ContractId
            };
            return APIResult.SuccessResult("accepted_and_released", result);
        }

        public APIResult CalculatePrisonerRansomQuote(Faction faction, Pawn targetPawn)
        {
            RimChatSettings settings = RimChatMod.Instance?.InstanceSettings;
            if (faction == null || targetPawn == null || settings == null)
            {
                return FailFastRansom("quote_context_invalid", "Cannot calculate ransom quote with invalid context.");
            }

            if (!prisonerRansomService.TryGetOrCreateNegotiationState(
                faction,
                targetPawn,
                settings,
                out PrisonerRansomNegotiationState state,
                out string error))
            {
                return FailFastRansom("quote_unavailable", $"Failed to calculate quote: {error}.");
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

            int safePenalty = Math.Abs(goodwillPenalty);
            if (safePenalty > 0)
            {
                faction.TryAffectGoodwillWith(Faction.OfPlayer, -safePenalty, false, true, null);
            }

            bool raidTriggered = false;
            if (triggerRaid)
            {
                EnsureHostileBeforePenaltyRaid(faction);
                raidTriggered = DiplomacyEventManager.TriggerRaidEvent(faction, -1f, null, null);
                if (raidTriggered)
                {
                    WorldEventLedgerComponent.Instance?.RecordRaidIntent(faction, delayed: false, "ransom_penalty", "auto");
                }
            }

            RecordAPICall(
                "RansomPenalty",
                true,
                $"faction={faction.Name},penalty={safePenalty},raid={raidTriggered},reason={reasonTag},targetPawn={targetPawn?.thingIDNumber ?? -1}");

            return APIResult.SuccessResult(
                "Ransom penalty applied.",
                new
                {
                    Faction = faction.Name,
                    GoodwillPenalty = safePenalty,
                    RaidTriggered = raidTriggered,
                    Reason = reasonTag ?? string.Empty
                });
        }

        private static void IssueReleaseJob(Pawn warden, Pawn targetPawn)
        {
            Job releaseJob = JobMaker.MakeJob(JobDefOf.ReleasePrisoner, targetPawn);
            releaseJob.playerForced = true;
            warden.jobs?.TryTakeOrderedJob(releaseJob, JobTag.Misc);
        }

        private static RansomContractRecord BuildContract(
            PrisonerRansomPrepareData preparedData,
            int paidTick,
            RimChatSettings settings)
        {
            return new RansomContractRecord
            {
                ContractId = Guid.NewGuid().ToString("N"),
                FactionId = preparedData.Faction?.GetUniqueLoadID() ?? string.Empty,
                TargetPawnLoadId = preparedData.TargetPawn.thingIDNumber,
                NegotiatedValueSnapshot = preparedData.State.Snapshot.NegotiationBase,
                WealthFactorSnapshot = preparedData.State.Snapshot.WealthFactorSnapshot,
                PaidTick = paidTick,
                DeadlineTick = paidTick + Math.Max(1, settings.RansomReleaseTimeoutTicks),
                Status = RansomContractStatus.PendingRelease
            };
        }

        private static string NormalizeRansomPaymentMode(string paymentMode, RimChatSettings settings)
        {
            string mode = string.IsNullOrWhiteSpace(paymentMode)
                ? settings?.RansomPaymentModeDefault ?? RansomPaymentModeSilver
                : paymentMode;
            return mode.Trim().ToLowerInvariant();
        }

        private static APIResult FailFastRansom(string code, string message)
        {
            return APIResult.FailureResult($"[{code}] {message}");
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
                failure = FailFastRansom("settings_unavailable", "Settings not initialized.");
                return false;
            }

            if (faction == null)
            {
                failure = FailFastRansom("invalid_faction", "Faction cannot be null.");
                return false;
            }

            if (parameters == null)
            {
                failure = FailFastRansom("missing_parameters", "pay_prisoner_ransom requires parameters.");
                return false;
            }

            return true;
        }

        private static void EnsureHostileBeforePenaltyRaid(Faction faction)
        {
            if (faction?.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile)
            {
                return;
            }

            int currentGoodwill = faction?.PlayerGoodwill ?? 0;
            int goodwillDelta = Math.Min(-80, currentGoodwill) - currentGoodwill;
            if (goodwillDelta != 0)
            {
                faction?.TryAffectGoodwillWith(Faction.OfPlayer, goodwillDelta, false, true, null);
            }

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
