using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Config;
using RimChat.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: GameComponent tick loop, GameAIInterface penalties, Pawn.ExitMap callback.
    /// Responsibility: persist ransom contracts, evaluate timeout/exit risk control, and apply penalties.
    /// </summary>
    public sealed class RansomContractManager : GameComponent
    {
        private const int TimeoutScanIntervalTicks = 250;
        private List<RansomContractRecord> contracts = new List<RansomContractRecord>();
        private int lastTimeoutScanTick;

        public RansomContractManager(Game game) : base()
        {
        }

        public static RansomContractManager Instance => Current.Game?.GetComponent<RansomContractManager>();

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref contracts, "ransomContracts", LookMode.Deep);
            contracts ??= new List<RansomContractRecord>();
            Scribe_Values.Look(ref lastTimeoutScanTick, "ransomLastTimeoutScanTick", 0);
        }

        public override void GameComponentTick()
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick <= 0 || currentTick - lastTimeoutScanTick < TimeoutScanIntervalTicks)
            {
                return;
            }

            lastTimeoutScanTick = currentTick;
            ProcessTimeoutContracts(currentTick);
            CleanupFinishedContracts(currentTick);
        }

        public void RegisterContract(RansomContractRecord contract)
        {
            if (contract == null || string.IsNullOrWhiteSpace(contract.ContractId))
            {
                return;
            }

            contracts.RemoveAll(existing => string.Equals(existing.ContractId, contract.ContractId, StringComparison.Ordinal));
            contracts.Add(contract);
        }

        public void HandlePawnExit(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed)
            {
                return;
            }

            RansomContractRecord contract = FindPendingContractByPawn(pawn.thingIDNumber);
            if (contract == null)
            {
                return;
            }

            RimChatSettings settings = RimChatMod.Instance?.InstanceSettings;
            if (settings == null)
            {
                return;
            }

            contract.Status = RansomContractStatus.Released;
            contract.ExitValueSnapshot = PrisonerRansomService.CalculateExitValueSnapshot(pawn, contract.WealthFactorSnapshot);
            contract.DropRate = ComputeDropRate(contract.NegotiatedValueSnapshot, contract.ExitValueSnapshot);
            ApplyExitPenalties(contract, pawn, settings);
            contract.Status = RansomContractStatus.Completed;
        }

        private static float ComputeDropRate(float negotiatedValue, float exitValue)
        {
            if (negotiatedValue <= 0f)
            {
                return 0f;
            }

            float ratio = Mathf.Clamp01(exitValue / negotiatedValue);
            return Mathf.Clamp01(1f - ratio);
        }

        private void ApplyExitPenalties(RansomContractRecord contract, Pawn targetPawn, RimChatSettings settings)
        {
            Faction faction = PrisonerRansomLookupUtility.FindFactionByLoadId(contract.FactionId);
            if (faction == null)
            {
                return;
            }

            int totalPenalty = 0;
            bool triggerRaid = false;
            if (contract.DropRate >= settings.RansomValueDropMajorThreshold)
            {
                totalPenalty += Math.Abs(settings.RansomPenaltyMajor);
                SendLetter("RimChat_PrisonerRansomPenaltyTitle", "RimChat_PrisonerRansomPenaltyMajorBody", faction.Name, targetPawn?.LabelShortCap ?? "Unknown", Mathf.RoundToInt(contract.DropRate * 100f));
            }

            if (contract.DropRate >= settings.RansomValueDropSevereThreshold)
            {
                totalPenalty += Math.Abs(settings.RansomPenaltySevere);
                triggerRaid = true;
                SendLetter("RimChat_PrisonerRansomPenaltyTitle", "RimChat_PrisonerRansomPenaltySevereBody", faction.Name, targetPawn?.LabelShortCap ?? "Unknown", Mathf.RoundToInt(contract.DropRate * 100f));
            }

            if (totalPenalty <= 0)
            {
                return;
            }

            GameAIInterface.APIResult result = GameAIInterface.Instance.ApplyRansomPenaltyAndRaid(
                faction,
                totalPenalty,
                triggerRaid,
                "drop_penalty",
                targetPawn);
            contract.AppliedGoodwillPenalty += Math.Abs(totalPenalty);
            if (triggerRaid && result.Success)
            {
                SendLetter("RimChat_PrisonerRansomRaidTitle", "RimChat_PrisonerRansomRaidBody", faction.Name);
            }
        }

        private void ProcessTimeoutContracts(int currentTick)
        {
            RimChatSettings settings = RimChatMod.Instance?.InstanceSettings;
            if (settings == null)
            {
                return;
            }

            List<RansomContractRecord> timeoutContracts = contracts
                .Where(contract => contract != null)
                .Where(contract => contract.Status == RansomContractStatus.PendingRelease)
                .Where(contract => currentTick > contract.DeadlineTick)
                .ToList();
            foreach (RansomContractRecord contract in timeoutContracts)
            {
                ApplyTimeoutPenalty(contract, settings);
            }
        }

        private void ApplyTimeoutPenalty(RansomContractRecord contract, RimChatSettings settings)
        {
            Faction faction = PrisonerRansomLookupUtility.FindFactionByLoadId(contract.FactionId);
            if (faction == null)
            {
                contract.Status = RansomContractStatus.TimeoutPunished;
                return;
            }

            int timeoutPenalty = Math.Abs(settings.RansomPenaltyTimeout);
            GameAIInterface.APIResult result = GameAIInterface.Instance.ApplyRansomPenaltyAndRaid(
                faction,
                timeoutPenalty,
                triggerRaid: true,
                reasonTag: "timeout_penalty");
            contract.AppliedGoodwillPenalty += timeoutPenalty;
            contract.Status = RansomContractStatus.TimeoutPunished;
            SendLetter("RimChat_PrisonerRansomTimeoutTitle", "RimChat_PrisonerRansomTimeoutBody", faction.Name);
            if (result.Success)
            {
                SendLetter("RimChat_PrisonerRansomRaidTitle", "RimChat_PrisonerRansomRaidBody", faction.Name);
            }
        }

        private static void SendLetter(string titleKey, string bodyKey, params object[] args)
        {
            Find.LetterStack.ReceiveLetter(
                titleKey.Translate(),
                bodyKey.Translate(args),
                LetterDefOf.NegativeEvent);
        }

        private RansomContractRecord FindPendingContractByPawn(int targetPawnLoadId)
        {
            return contracts
                .Where(contract => contract != null)
                .FirstOrDefault(contract =>
                    contract.TargetPawnLoadId == targetPawnLoadId &&
                    contract.Status == RansomContractStatus.PendingRelease);
        }

        private void CleanupFinishedContracts(int currentTick)
        {
            contracts.RemoveAll(contract =>
                contract == null ||
                (contract.Status != RansomContractStatus.PendingRelease &&
                 currentTick - contract.PaidTick > 60000));
        }
    }
}
