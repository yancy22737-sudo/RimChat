using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimChat.Config;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: RimWorld Pawn/Faction state and RimChat settings.
    /// Responsibility: valuation formula and single-submit quote state for prisoner ransom.
    /// </summary>
    internal sealed class PrisonerRansomService
    {
        private const float StartAskMultiplier = 1.25f;
        private const float FloorMultiplier = 0.80f;
        private const float OfferWindowMinMultiplier = 0.60f;
        private const float OfferWindowMaxMultiplier = 1.40f;
        private const float PawnValueCap = 5000f;

        private readonly Dictionary<string, PrisonerRansomNegotiationState> negotiationStates =
            new Dictionary<string, PrisonerRansomNegotiationState>(StringComparer.Ordinal);

        public bool TryGetOrCreateNegotiationState(
            Faction faction,
            Pawn targetPawn,
            RimChatSettings settings,
            out PrisonerRansomNegotiationState state,
            out string error)
        {
            state = null;
            error = string.Empty;
            if (faction == null || targetPawn == null || settings == null)
            {
                error = "negotiation_context_invalid";
                return false;
            }

            string stateKey = BuildStateKey(faction.GetUniqueLoadID(), targetPawn.thingIDNumber);
            if (negotiationStates.TryGetValue(stateKey, out state))
            {
                return true;
            }

            PrisonerRansomQuoteSnapshot snapshot = BuildSnapshot(faction, targetPawn, settings);
            if (snapshot == null || snapshot.NegotiationBase <= 0f)
            {
                error = "quote_snapshot_invalid";
                return false;
            }

            state = new PrisonerRansomNegotiationState
            {
                Snapshot = snapshot,
                CurrentRound = 1,
                CurrentAskSilver = snapshot.StartAskSilver,
                MaxRounds = 1
            };
            negotiationStates[stateKey] = state;
            return true;
        }

        public static bool IsRansomEligibleTarget(Pawn targetPawn, Faction sourceFaction, out string reasonCode)
        {
            reasonCode = string.Empty;
            if (targetPawn == null || targetPawn.Dead || targetPawn.Destroyed)
            {
                reasonCode = "target_pawn_missing";
                return false;
            }

            if (!targetPawn.IsPrisonerOfColony || targetPawn.MapHeld == null || !targetPawn.MapHeld.IsPlayerHome)
            {
                reasonCode = "target_not_player_prisoner";
                return false;
            }

            if (sourceFaction == null || targetPawn.Faction == null || targetPawn.Faction != sourceFaction)
            {
                reasonCode = "target_faction_mismatch";
                return false;
            }

            bool isHumanlike = targetPawn.RaceProps?.Humanlike ?? false;
            if (!isHumanlike)
            {
                reasonCode = "target_not_humanlike";
                return false;
            }

            return true;
        }

        public bool TryValidateOfferWindow(PrisonerRansomNegotiationState state, int offerSilver, out string error)
        {
            error = string.Empty;
            if (state?.Snapshot == null || offerSilver <= 0)
            {
                error = "offer_invalid";
                return false;
            }

            if (!TryGetOfferWindow(state, out int minOffer, out int maxOffer, out string windowError))
            {
                error = windowError;
                return false;
            }

            if (offerSilver < minOffer || offerSilver > maxOffer)
            {
                error = $"offer_window_violation({offerSilver},{minOffer},{maxOffer})";
                return false;
            }

            return true;
        }

        public bool TryGetOfferWindow(
            PrisonerRansomNegotiationState state,
            out int minOffer,
            out int maxOffer,
            out string error)
        {
            minOffer = 0;
            maxOffer = 0;
            error = string.Empty;
            if (state?.Snapshot == null)
            {
                error = "offer_window_state_invalid";
                return false;
            }

            float baseValue = Math.Max(1f, state.Snapshot.NegotiationBase);
            minOffer = Math.Max(1, Mathf.FloorToInt(baseValue * OfferWindowMinMultiplier));
            maxOffer = Math.Max(minOffer, Mathf.CeilToInt(baseValue * OfferWindowMaxMultiplier));
            return true;
        }

        public void ClearState(string factionId, int targetPawnLoadId)
        {
            string stateKey = BuildStateKey(factionId, targetPawnLoadId);
            negotiationStates.Remove(stateKey);
        }

        public static bool TryResolvePawnByLoadId(int pawnLoadId, out Pawn pawn)
        {
            return PrisonerRansomLookupUtility.TryFindPawnByLoadId(pawnLoadId, out pawn);
        }

        public static float CalculateExitValueSnapshot(Pawn targetPawn, float wealthFactorSnapshot)
        {
            if (targetPawn == null || targetPawn.Dead || targetPawn.Destroyed)
            {
                return 0f;
            }

            float pawnValueRaw = Math.Max(1f, targetPawn.MarketValue);
            float pawnValue = Math.Min(pawnValueRaw, PawnValueCap);
            float baseValue = pawnValue * (1f + Math.Max(0f, wealthFactorSnapshot));
            float healthFactor = ComputeHealthFactor(targetPawn);
            return Math.Max(1f, baseValue * healthFactor);
        }

        private static string BuildStateKey(string factionId, int targetPawnLoadId)
        {
            return string.Concat(factionId ?? string.Empty, "::", targetPawnLoadId.ToString(CultureInfo.InvariantCulture));
        }

        private static PrisonerRansomQuoteSnapshot BuildSnapshot(Faction faction, Pawn targetPawn, RimChatSettings settings)
        {
            float pawnValueRaw = Math.Max(1f, targetPawn.MarketValue);
            float pawnValue = Math.Min(pawnValueRaw, PawnValueCap);
            float wealthFactor = ComputeWealthFactor(targetPawn.MapHeld);
            float baseValue = pawnValue * (1f + wealthFactor);
            float healthFactor = ComputeHealthFactor(targetPawn);
            float negotiationBase = baseValue * healthFactor;
            int goodwill = faction?.PlayerGoodwill ?? 0;
            bool applyDiscount = goodwill < settings.RansomLowGoodwillDiscountThreshold;
            if (applyDiscount)
            {
                negotiationBase *= settings.RansomLowGoodwillDiscountFactor;
            }

            float safeBase = Math.Max(1f, negotiationBase);
            return new PrisonerRansomQuoteSnapshot
            {
                FactionId = faction?.GetUniqueLoadID() ?? string.Empty,
                TargetPawnLoadId = targetPawn.thingIDNumber,
                NegotiationBase = safeBase,
                WealthFactorSnapshot = wealthFactor,
                HealthFactorSnapshot = healthFactor,
                LowGoodwillDiscountApplied = applyDiscount,
                GoodwillSnapshot = goodwill,
                StartAskSilver = Math.Max(1, Mathf.CeilToInt(safeBase * StartAskMultiplier)),
                FloorSilver = Math.Max(1, Mathf.CeilToInt(safeBase * FloorMultiplier))
            };
        }

        private static float ComputeWealthFactor(Map map)
        {
            float wealth = map?.wealthWatcher?.WealthTotal ?? 0f;
            return Math.Max(0f, (wealth / 100000f) * 0.15f);
        }

        private static float ComputeHealthFactor(Pawn pawn)
        {
            if (pawn?.health == null)
            {
                return 0.25f;
            }

            float summaryHealth = Mathf.Clamp01(pawn.health.summaryHealth.SummaryHealthPercent);
            float capacityAverage = ComputeCapacityAverage(pawn);
            float missingPenalty = Mathf.Clamp01(CountMissingParts(pawn) * 0.05f);
            float bleedPenalty = Mathf.Clamp01(pawn.health.hediffSet?.BleedRateTotal ?? 0f) * 0.35f;
            float blended = (summaryHealth * 0.55f) + (capacityAverage * 0.45f);
            float final = blended - missingPenalty - bleedPenalty;
            if (pawn.Downed)
            {
                final -= 0.1f;
            }

            return Mathf.Clamp(final, 0.25f, 1f);
        }

        private static float ComputeCapacityAverage(Pawn pawn)
        {
            if (pawn?.health?.capacities == null)
            {
                return 0.25f;
            }

            float sum = 0f;
            int count = 0;
            sum += ReadCapacity(pawn, PawnCapacityDefOf.Consciousness, ref count);
            sum += ReadCapacity(pawn, PawnCapacityDefOf.Moving, ref count);
            sum += ReadCapacity(pawn, PawnCapacityDefOf.Manipulation, ref count);
            sum += ReadCapacity(pawn, PawnCapacityDefOf.Breathing, ref count);
            sum += ReadCapacity(pawn, PawnCapacityDefOf.BloodPumping, ref count);
            return count <= 0 ? 0.25f : Mathf.Clamp01(sum / count);
        }

        private static float ReadCapacity(Pawn pawn, PawnCapacityDef capacityDef, ref int count)
        {
            if (capacityDef == null)
            {
                return 0f;
            }

            count++;
            return Mathf.Clamp01(pawn.health.capacities.GetLevel(capacityDef));
        }

        private static int CountMissingParts(Pawn pawn)
        {
            return pawn?.health?.hediffSet?.GetMissingPartsCommonAncestors()?.Count() ?? 0;
        }

    }
}
