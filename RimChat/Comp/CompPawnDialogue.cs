using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;
using RimChat.Dialogue;
using RimChat.UI;
using RimChat.Core;
using RimChat.WorldState;

namespace RimChat.Comp
{
    public class CompPawnDialogue : ThingComp
    {
        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            if (selPawn == null)
                yield break;

            Pawn targetPawn = this.parent as Pawn;

            // Hide: self-to-self
            if (targetPawn == null || targetPawn == selPawn)
                yield break;

            RimChatTrackedEntityRegistry.TrackPawn(selPawn);
            RimChatTrackedEntityRegistry.TrackPawn(targetPawn);

            // Hide: trade caravan should use vanilla trade
            if (PawnDialogueRoutingPolicy.IsTradeCaravanPawn(targetPawn))
                yield break;

            // Hide: target dead/destroyed or not on same map
            if (targetPawn.RaceProps == null || targetPawn.Dead || targetPawn.Destroyed)
                yield break;

            if (!selPawn.Spawned || !targetPawn.Spawned || selPawn.Map != targetPawn.Map)
                yield break;

            // --- Below this point, always show the button (enabled or disabled) ---

            // Check race eligibility — show disabled if incompatible
            string raceReason = PawnDialogueRoutingPolicy.GetIneligibleRaceReason(targetPawn);
            if (raceReason != null)
            {
                yield return DisabledOption(raceReason);
                yield break;
            }

            // Check RPG dialogue enabled — show disabled if off
            if (RimChatMod.Settings == null || !RimChatMod.Settings.EnableRPGDialogue)
            {
                yield return DisabledOption("RimChat_Converse_Disabled_RpgOff");
                yield break;
            }

            // Check reachability — show disabled if unreachable
            if (!selPawn.CanReach(targetPawn, PathEndMode.InteractionCell, Danger.Deadly))
            {
                yield return DisabledOption("RimChat_Converse_Disabled_Unreachable");
                yield break;
            }

            // Hide: either pawn in combat (matches vanilla style — hide instead of disable)
            if (PawnCombatStateUtility.IsEitherPawnInCombat(selPawn, targetPawn))
                yield break;

            // Disable: either pawn drafted (show disabled button with reason)
            if (PawnCombatStateUtility.IsEitherPawnDrafted(selPawn, targetPawn))
            {
                yield return DisabledOption("RimChat_Converse_Disabled_Drafted");
                yield break;
            }

            // Check asleep/downed — show disabled with fine-grained reason
            if (!RestUtility.Awake(targetPawn) || targetPawn.Downed)
            {
                if (targetPawn.Downed && !RestUtility.Awake(targetPawn))
                    yield return DisabledOption("RimChat_Converse_Disabled_DownedAsleep");
                else if (targetPawn.Downed)
                    yield return DisabledOption("RimChat_Converse_Disabled_Downed");
                else
                    yield return DisabledOption("RimChat_Converse_Disabled_Asleep");
                yield break;
            }

            // Check cooldown — show disabled with remaining time
            var rpgManager = Current.Game?.GetComponent<RimChat.DiplomacySystem.GameComponent_RPGManager>();
            if (rpgManager != null && rpgManager.IsRpgDialogueOnCooldown(targetPawn, out int remainingTicks))
            {
                float remainingHours = System.Math.Max(0f, remainingTicks / 2500f);
                string cooldownLabel = "RimChat_Converse_Disabled_Cooldown".Translate(remainingHours.ToString("F1"));
                yield return new FloatMenuOption(cooldownLabel, null);
                yield break;
            }

            // All checks passed — show enabled button
            string label = "RimChat_Converse".Translate();
            yield return new FloatMenuOption(label, () =>
            {
                RimChatTrackedEntityRegistry.TrackPawn(selPawn);
                RimChatTrackedEntityRegistry.TrackPawn(targetPawn);

                JobDef dialogueJobDef = DefDatabase<JobDef>.GetNamedSilentFail("RimChat_RPGDialogue");
                if (dialogueJobDef == null)
                {
                    Log.Warning("[RimChat] Missing JobDef RimChat_RPGDialogue, fallback to direct dialogue open.");
                    DialogueWindowCoordinator.TryOpen(
                        DialogueOpenIntent.CreateRpg(selPawn, targetPawn, selPawn.Map),
                        out _);
                    return;
                }

                Job dialogueJob = JobMaker.MakeJob(dialogueJobDef, targetPawn);
                dialogueJob.playerForced = true;

                if (selPawn.jobs?.curJob != null)
                {
                    selPawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }

                selPawn.jobs.TryTakeOrderedJob(dialogueJob, JobTag.Misc);
            }, MenuOptionPriority.Low);
        }

        /// <summary>
        /// Create a disabled (greyed-out) float menu option with a reason.
        /// </summary>
        private static FloatMenuOption DisabledOption(string reasonKey)
        {
            string label = "RimChat_Converse_Disabled".Translate(reasonKey.Translate());
            return new FloatMenuOption(label, null);
        }
    }

    public class CompProperties_PawnDialogue : CompProperties
    {
        public CompProperties_PawnDialogue()
        {
            this.compClass = typeof(CompPawnDialogue);
        }
    }
}
