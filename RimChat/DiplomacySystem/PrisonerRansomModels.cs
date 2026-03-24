using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: Verse serialization, Faction/Pawn lookup.
    /// Responsibility: shared models and lookup helpers for prisoner-ransom action flow.
    /// </summary>
    public enum RansomContractStatus
    {
        PendingRelease = 0,
        Released = 1,
        TimeoutPunished = 2,
        Completed = 3
    }

    public sealed class PrisonerRansomResultData
    {
        public string StatusCode { get; set; } = string.Empty;
        public string FailureCode { get; set; } = string.Empty;
        public int TargetPawnLoadId { get; set; }
        public int OfferedSilver { get; set; }
        public int AcceptedSilver { get; set; }
        public int CurrentAskSilver { get; set; }
        public int FloorSilver { get; set; }
        public int RoundIndex { get; set; }
        public int MaxRounds { get; set; }
        public float NegotiationBaseSnapshot { get; set; }
        public int DeadlineTick { get; set; }
        public string ContractId { get; set; } = string.Empty;
    }

    internal sealed class PrisonerRansomQuoteSnapshot
    {
        public string FactionId = string.Empty;
        public int TargetPawnLoadId;
        public float NegotiationBase;
        public float WealthFactorSnapshot;
        public float HealthFactorSnapshot;
        public bool LowGoodwillDiscountApplied;
        public int GoodwillSnapshot;
        public int StartAskSilver;
        public int FloorSilver;
    }

    internal sealed class PrisonerRansomNegotiationState
    {
        public PrisonerRansomQuoteSnapshot Snapshot = new PrisonerRansomQuoteSnapshot();
        public int CurrentRound = 1;
        public int CurrentAskSilver;
        public int MaxRounds = 3;
    }

    internal sealed class PrisonerRansomPrepareData
    {
        public Faction Faction;
        public Pawn TargetPawn;
        public Pawn AssignedWarden;
        public int OfferedSilver;
        public int AcceptedSilver;
        public PrisonerRansomNegotiationState State;
    }

    public sealed class RansomContractRecord : IExposable
    {
        public string ContractId = string.Empty;
        public string FactionId = string.Empty;
        public int TargetPawnLoadId;
        public float NegotiatedValueSnapshot;
        public float WealthFactorSnapshot;
        public int PaidTick;
        public int DeadlineTick;
        public RansomContractStatus Status = RansomContractStatus.PendingRelease;
        public float ExitValueSnapshot;
        public float DropRate;
        public int AppliedGoodwillPenalty;

        public void ExposeData()
        {
            Scribe_Values.Look(ref ContractId, "contractId", string.Empty);
            Scribe_Values.Look(ref FactionId, "factionId", string.Empty);
            Scribe_Values.Look(ref TargetPawnLoadId, "targetPawnLoadId", 0);
            Scribe_Values.Look(ref NegotiatedValueSnapshot, "negotiatedValueSnapshot", 0f);
            Scribe_Values.Look(ref WealthFactorSnapshot, "wealthFactorSnapshot", 0f);
            Scribe_Values.Look(ref PaidTick, "paidTick", 0);
            Scribe_Values.Look(ref DeadlineTick, "deadlineTick", 0);
            int status = (int)Status;
            Scribe_Values.Look(ref status, "status", 0);
            Status = (RansomContractStatus)Math.Max(0, status);
            Scribe_Values.Look(ref ExitValueSnapshot, "exitValueSnapshot", 0f);
            Scribe_Values.Look(ref DropRate, "dropRate", 0f);
            Scribe_Values.Look(ref AppliedGoodwillPenalty, "appliedGoodwillPenalty", 0);
        }
    }

    internal static class PrisonerRansomLookupUtility
    {
        public static bool TryFindPawnByLoadId(int pawnLoadId, out Pawn pawn)
        {
            pawn = null;
            if (pawnLoadId <= 0)
            {
                return false;
            }

            pawn = FindPawnOnMaps(pawnLoadId) ?? FindPawnInWorld(pawnLoadId);
            return pawn != null;
        }

        public static Faction FindFactionByLoadId(string factionId)
        {
            if (string.IsNullOrWhiteSpace(factionId))
            {
                return null;
            }

            return Find.FactionManager?.AllFactions?
                .FirstOrDefault(faction =>
                    faction != null &&
                    string.Equals(faction.GetUniqueLoadID(), factionId, StringComparison.Ordinal));
        }

        private static Pawn FindPawnOnMaps(int pawnLoadId)
        {
            IEnumerable<Pawn> pawns = Find.Maps?
                .SelectMany(map => map?.mapPawns?.AllPawnsSpawned ?? Enumerable.Empty<Pawn>());
            return pawns?.FirstOrDefault(pawn => pawn != null && pawn.thingIDNumber == pawnLoadId);
        }

        private static Pawn FindPawnInWorld(int pawnLoadId)
        {
            IEnumerable<Pawn> worldPawns = Find.WorldPawns?.AllPawnsAliveOrDead ?? Enumerable.Empty<Pawn>();
            return worldPawns.FirstOrDefault(pawn => pawn != null && pawn.thingIDNumber == pawnLoadId);
        }
    }
}
