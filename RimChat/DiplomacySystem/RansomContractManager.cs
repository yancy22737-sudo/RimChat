using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Config;
using RimChat.Core;
using RimChat.Memory;
using RimChat.NpcDialogue;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: GameComponent tick loop, GameAIInterface penalties, Pawn.ExitMap callback, and diplomacy social/news messaging.
    /// Responsibility: persist ransom contracts, evaluate timeout/exit risk control, schedule healthy-exit acknowledgements, and apply timeout escalation.
    /// </summary>
    public sealed class RansomContractManager : GameComponent
    {
        private const int TimeoutScanIntervalTicks = 250;
        private const int HealthyExitReplyMinDelayTicks = 12500;
        private const int HealthyExitReplyMaxDelayTicks = 25000;
        private const int OrganFailureMinDelayTicks = 12500;
        private const int OrganFailureMaxDelayTicks = 25000;
        private const float StrictHealthyExitSummaryThreshold = 0.85f;
        private const float StrictHealthyExitConsciousnessThreshold = 0.85f;
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
            ProcessOrganFailurePenalties(currentTick);
            ProcessHealthyExitReplies(currentTick);
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
            contract.ReleasedTick = Find.TickManager?.TicksGame ?? 0;
            contract.TargetPawnLabelSnapshot = pawn.LabelShortCap ?? contract.TargetPawnLabelSnapshot ?? string.Empty;
            contract.ExitValueSnapshot = PrisonerRansomService.CalculateExitValueSnapshot(pawn, contract.WealthFactorSnapshot);
            contract.DropRate = ComputeDropRate(contract.NegotiatedValueSnapshot, contract.ExitValueSnapshot);
            bool organFailureScheduled = TryScheduleOrganFailurePenalty(contract, pawn, contract.ReleasedTick);
            if (!organFailureScheduled)
            {
                ApplyExitPenalties(contract, pawn, settings);
                TryScheduleHealthyExitReply(contract, pawn, contract.ReleasedTick);
            }
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
            SendTimeoutWarningMessage(contract, faction);
            TryEnqueueTimeoutCondemnation(contract, faction);
            if (result.Success)
            {
                SendLetter("RimChat_PrisonerRansomRaidTitle", "RimChat_PrisonerRansomRaidBody", faction.Name);
            }
        }

        private void ProcessHealthyExitReplies(int currentTick)
        {
            List<RansomContractRecord> dueContracts = contracts
                .Where(contract => contract != null)
                .Where(contract => contract.Status == RansomContractStatus.Completed)
                .Where(contract => contract.HealthyExitReplyScheduled && !contract.HealthyExitReplySent)
                .Where(contract => contract.HealthyExitReplyDueTick > 0 && currentTick >= contract.HealthyExitReplyDueTick)
                .ToList();
            foreach (RansomContractRecord contract in dueContracts)
            {
                bool delivered = TryDeliverHealthyExitReply(contract);
                contract.HealthyExitReplySent = delivered;
                contract.HealthyExitReplyScheduled = false;
                contract.HealthyExitReplyDueTick = 0;
            }
        }

        private void ProcessOrganFailurePenalties(int currentTick)
        {
            RimChatSettings settings = RimChatMod.Instance?.InstanceSettings;
            if (settings == null)
            {
                return;
            }

            List<RansomContractRecord> dueContracts = contracts
                .Where(contract => contract != null)
                .Where(contract => contract.Status == RansomContractStatus.Completed)
                .Where(contract => contract.OrganFailureScheduled && !contract.OrganFailurePenaltyApplied)
                .Where(contract => contract.OrganFailureDueTick > 0 && currentTick >= contract.OrganFailureDueTick)
                .ToList();
            foreach (RansomContractRecord contract in dueContracts)
            {
                ApplyOrganFailurePenalty(contract, settings);
            }
        }

        private static bool TryScheduleOrganFailurePenalty(RansomContractRecord contract, Pawn pawn, int exitTick)
        {
            if (contract == null || pawn == null)
            {
                return false;
            }

            contract.ExitCoreOrganMissingSnapshot = PrisonerRansomService.CaptureCoreOrganMissingSnapshot(pawn);
            contract.BaselineCoreOrganMissingSnapshot ??= new List<RansomCoreOrganSnapshotEntry>();
            contract.NewlyMissingCoreOrgans = PrisonerRansomService.ComputeNewlyMissingCoreOrgans(
                contract.BaselineCoreOrganMissingSnapshot,
                contract.ExitCoreOrganMissingSnapshot);

            if (contract.NewlyMissingCoreOrgans == null || contract.NewlyMissingCoreOrgans.Count <= 0)
            {
                contract.OrganFailureScheduled = false;
                contract.OrganFailureDueTick = 0;
                return false;
            }

            int delayTicks = Rand.RangeInclusive(OrganFailureMinDelayTicks, OrganFailureMaxDelayTicks);
            contract.OrganFailureDueTick = Math.Max(exitTick, 0) + delayTicks;
            contract.OrganFailureScheduled = true;
            contract.OrganFailurePenaltyApplied = false;
            return true;
        }

        private void ApplyOrganFailurePenalty(RansomContractRecord contract, RimChatSettings settings)
        {
            if (contract == null)
            {
                return;
            }

            contract.OrganFailureScheduled = false;
            contract.OrganFailureDueTick = 0;
            contract.OrganFailurePenaltyApplied = true;

            Faction faction = PrisonerRansomLookupUtility.FindFactionByLoadId(contract.FactionId);
            if (faction == null)
            {
                return;
            }

            string pawnLabel = ResolvePawnLabel(contract, null);
            string organSummary = ResolveOrganFailureSummary(contract);
            int timeoutPenalty = Math.Abs(settings.RansomPenaltyTimeout);
            GameAIInterface.APIResult result = GameAIInterface.Instance.ApplyRansomPenaltyAndRaid(
                faction,
                timeoutPenalty,
                triggerRaid: true,
                reasonTag: "organ_failure_timeout_penalty");
            contract.AppliedGoodwillPenalty += timeoutPenalty;

            SendLetter(
                "RimChat_PrisonerRansomTimeoutTitle",
                "RimChat_PrisonerRansomOrganFailureBody",
                faction.Name,
                pawnLabel,
                organSummary);
            SendOrganFailureWarningMessage(faction, pawnLabel, organSummary);
            TryEnqueueOrganFailureCondemnation(faction, pawnLabel, organSummary);
            if (result.Success)
            {
                SendLetter("RimChat_PrisonerRansomRaidTitle", "RimChat_PrisonerRansomRaidBody", faction.Name);
            }
        }

        private static void TryScheduleHealthyExitReply(RansomContractRecord contract, Pawn pawn, int exitTick)
        {
            if (contract == null || pawn == null || contract.HealthyExitReplySent || contract.HealthyExitReplyScheduled)
            {
                return;
            }

            if (!IsStrictHealthyExit(pawn))
            {
                return;
            }

            int delayTicks = Rand.RangeInclusive(HealthyExitReplyMinDelayTicks, HealthyExitReplyMaxDelayTicks);
            contract.HealthyExitReplyDueTick = Math.Max(exitTick, 0) + delayTicks;
            contract.HealthyExitReplyScheduled = true;
            contract.TargetPawnLabelSnapshot = pawn.LabelShortCap ?? contract.TargetPawnLabelSnapshot ?? string.Empty;
        }

        private static bool IsStrictHealthyExit(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed || pawn.Downed || pawn.health == null)
            {
                return false;
            }

            float summaryHealth = Mathf.Clamp01(pawn.health.summaryHealth?.SummaryHealthPercent ?? 0f);
            float consciousness = ReadCapacitySafe(pawn, PawnCapacityDefOf.Consciousness);
            return summaryHealth >= StrictHealthyExitSummaryThreshold &&
                consciousness >= StrictHealthyExitConsciousnessThreshold;
        }

        private static float ReadCapacitySafe(Pawn pawn, PawnCapacityDef capacityDef)
        {
            if (pawn?.health?.capacities == null || capacityDef == null)
            {
                return 0f;
            }

            return Mathf.Clamp01(pawn.health.capacities.GetLevel(capacityDef));
        }

        private bool TryDeliverHealthyExitReply(RansomContractRecord contract)
        {
            if (contract == null)
            {
                return false;
            }

            Faction faction = PrisonerRansomLookupUtility.FindFactionByLoadId(contract.FactionId);
            if (faction == null)
            {
                return false;
            }

            string pawnLabel = ResolvePawnLabel(contract, null);
            string message = "RimChat_PrisonerRansomHealthyExitReplyMessage".Translate(pawnLabel).ToString();
            PushNpcMessageToFactionSession(faction, message, DialogueMessageType.Normal);
            SendNpcChoiceLetter(
                faction,
                "RimChat_PrisonerRansomHealthyExitLetterTitle".Translate(faction.Name),
                message,
                LetterDefOf.PositiveEvent);
            return true;
        }

        private void SendTimeoutWarningMessage(RansomContractRecord contract, Faction faction)
        {
            if (contract == null || faction == null)
            {
                return;
            }

            string pawnLabel = ResolvePawnLabel(contract, null);
            string message = "RimChat_PrisonerRansomTimeoutWarningMessage".Translate(pawnLabel).ToString();
            PushNpcMessageToFactionSession(faction, message, DialogueMessageType.System);
            SendNpcChoiceLetter(
                faction,
                "RimChat_PrisonerRansomTimeoutWarningLetterTitle".Translate(faction.Name),
                message,
                LetterDefOf.ThreatSmall);
        }

        private static void TryEnqueueTimeoutCondemnation(RansomContractRecord contract, Faction faction)
        {
            if (contract == null || faction == null)
            {
                return;
            }

            GameComponent_DiplomacyManager manager = GameComponent_DiplomacyManager.Instance;
            if (manager == null)
            {
                return;
            }

            string pawnLabel = ResolvePawnLabel(contract, null);
            string summary = "RimChat_PrisonerRansomTimeoutCondemnSummary"
                .Translate(faction.Name, pawnLabel)
                .ToString();
            manager.EnqueuePublicPost(
                sourceFaction: faction,
                targetFaction: Faction.OfPlayer,
                category: SocialPostCategory.Diplomatic,
                sentiment: -1,
                summary: summary,
                isFromPlayerDialogue: false,
                intentHint: string.Empty,
                reason: DebugGenerateReason.DialogueExplicit);
        }

        private static void SendOrganFailureWarningMessage(Faction faction, string pawnLabel, string organSummary)
        {
            if (faction == null)
            {
                return;
            }

            string message = "RimChat_PrisonerRansomOrganFailureWarningMessage"
                .Translate(pawnLabel, organSummary)
                .ToString();
            PushNpcMessageToFactionSession(faction, message, DialogueMessageType.System);
            SendNpcChoiceLetter(
                faction,
                "RimChat_PrisonerRansomOrganFailureWarningLetterTitle".Translate(faction.Name),
                message,
                LetterDefOf.ThreatSmall);
        }

        private static void TryEnqueueOrganFailureCondemnation(Faction faction, string pawnLabel, string organSummary)
        {
            if (faction == null)
            {
                return;
            }

            GameComponent_DiplomacyManager manager = GameComponent_DiplomacyManager.Instance;
            if (manager == null)
            {
                return;
            }

            string summary = "RimChat_PrisonerRansomOrganFailureCondemnSummary"
                .Translate(faction.Name, pawnLabel, organSummary)
                .ToString();
            manager.EnqueuePublicPost(
                sourceFaction: faction,
                targetFaction: Faction.OfPlayer,
                category: SocialPostCategory.Diplomatic,
                sentiment: -1,
                summary: summary,
                isFromPlayerDialogue: false,
                intentHint: string.Empty,
                reason: DebugGenerateReason.DialogueExplicit);
        }

        private static string ResolveOrganFailureSummary(RansomContractRecord contract)
        {
            string summary = PrisonerRansomService.FormatCoreOrganMissingSummary(contract?.NewlyMissingCoreOrgans);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                return summary;
            }

            return "RimChat_Unknown".Translate().ToString();
        }

        private static string ResolvePawnLabel(RansomContractRecord contract, Pawn fallbackPawn)
        {
            if (fallbackPawn != null && !string.IsNullOrWhiteSpace(fallbackPawn.LabelShortCap))
            {
                if (contract != null)
                {
                    contract.TargetPawnLabelSnapshot = fallbackPawn.LabelShortCap;
                }
                return fallbackPawn.LabelShortCap;
            }

            if (!string.IsNullOrWhiteSpace(contract?.TargetPawnLabelSnapshot))
            {
                return contract.TargetPawnLabelSnapshot;
            }

            if (contract != null &&
                contract.TargetPawnLoadId > 0 &&
                PrisonerRansomService.TryResolvePawnByLoadId(contract.TargetPawnLoadId, out Pawn pawn) &&
                pawn != null &&
                !string.IsNullOrWhiteSpace(pawn.LabelShortCap))
            {
                contract.TargetPawnLabelSnapshot = pawn.LabelShortCap;
                return pawn.LabelShortCap;
            }

            return "Unknown";
        }

        private static void PushNpcMessageToFactionSession(Faction faction, string message, DialogueMessageType messageType)
        {
            if (faction == null || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            GameComponent_DiplomacyManager manager = GameComponent_DiplomacyManager.Instance;
            manager?.ForcePresenceOnlineForNpcInitiated(faction);
            FactionDialogueSession session = manager?.GetOrCreateSession(faction);
            if (session == null)
            {
                return;
            }

            if (session.isConversationEndedByNpc)
            {
                session.ReinitiateConversation();
                session.AddMessage(
                    "System",
                    "RimChat_ConversationReinitiatedByNpc".Translate().ToString(),
                    false,
                    DialogueMessageType.System);
            }

            string sender = faction.leader?.Name?.ToStringShort ?? faction.Name ?? "Unknown";
            session.AddMessage(sender, message, false, messageType, faction.leader);
            session.hasUnreadMessages = true;
            LeaderMemoryManager.Instance?.UpdateFromDialogue(faction, session.messages);
        }

        private static void SendNpcChoiceLetter(Faction faction, TaggedString title, string body, LetterDef letterDef)
        {
            if (faction == null || Find.LetterStack == null || string.IsNullOrWhiteSpace(body))
            {
                return;
            }

            if (ChoiceLetter_NpcInitiatedDialogue.IsDialogueAlreadyOpen(faction))
            {
                return;
            }

            var letter = new ChoiceLetter_NpcInitiatedDialogue();
            letter.Setup(faction, title, body, letterDef ?? LetterDefOf.NeutralEvent);
            Find.LetterStack.ReceiveLetter(letter, string.Empty, 0, true);
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
            {
                if (contract == null)
                {
                    return true;
                }

                if (contract.Status == RansomContractStatus.PendingRelease)
                {
                    return false;
                }

                bool hasPendingFollowups =
                    (contract.HealthyExitReplyScheduled && !contract.HealthyExitReplySent) ||
                    (contract.OrganFailureScheduled && !contract.OrganFailurePenaltyApplied);
                if (hasPendingFollowups)
                {
                    return false;
                }

                int anchorTick = Math.Max(contract.PaidTick, contract.ReleasedTick);
                return currentTick - anchorTick > 60000;
            });
        }
    }
}
