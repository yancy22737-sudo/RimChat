using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;
using RimChat.UI;
using RimChat.Core;

namespace RimChat.Comp
{
    public class CompPawnDialogue : ThingComp
    {
        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            if (selPawn == null)
                yield break;

            Pawn targetPawn = this.parent as Pawn;

            if (targetPawn == null || targetPawn == selPawn)
                yield break;

            if (!selPawn.CanReach(targetPawn, PathEndMode.InteractionCell, Danger.Deadly))
            {
                yield return new FloatMenuOption("CannotReach".Translate() + ": " + "RimChat_Unreachable".Translate(), null);
                yield break;
            }

            if (RimChatMod.Settings == null || !RimChatMod.Settings.EnableRPGDialogue)
                yield break;

            if (!CanShowRpgDialogueOption(selPawn, targetPawn))
                yield break;

            string label = "RimChat_RPGDialogue_Dialogue".Translate();
            yield return new FloatMenuOption(label, () =>
            {
                var rpgManager = Current.Game?.GetComponent<RimChat.DiplomacySystem.GameComponent_RPGManager>();
                if (rpgManager != null && rpgManager.IsRpgDialogueOnCooldown(targetPawn, out int remainingTicks))
                {
                    float remainingHours = System.Math.Max(0f, remainingTicks / 2500f);
                    Messages.Message(
                        "RimChat_RPGDialogue_CooldownRejectedWithHours".Translate(remainingHours.ToString("F1")),
                        MessageTypeDefOf.RejectInput,
                        false);
                    return;
                }

                JobDef dialogueJobDef = DefDatabase<JobDef>.GetNamedSilentFail("RimChat_RPGDialogue");
                if (dialogueJobDef == null)
                {
                    Log.Warning("[RimChat] Missing JobDef RimChat_RPGDialogue, fallback to direct dialogue open.");
                    Find.WindowStack.Add(new Dialog_RPGPawnDialogue(selPawn, targetPawn));
                    return;
                }

                Job dialogueJob = JobMaker.MakeJob(dialogueJobDef, targetPawn);
                dialogueJob.playerForced = true;

                // Force interrupt current work so dialogue job starts immediately.
                if (selPawn.jobs?.curJob != null)
                {
                    selPawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }

                selPawn.jobs.TryTakeOrderedJob(dialogueJob, JobTag.Misc);
            }, MenuOptionPriority.Default);
        }

        private bool CanShowRpgDialogueOption(Pawn initiator, Pawn target)
        {
            if (initiator == null || target == null)
                return false;

            if (target.RaceProps == null || target.Dead || target.Destroyed)
                return false;

            if (!initiator.Spawned || !target.Spawned || initiator.Map != target.Map)
                return false;

            if (PawnCombatStateUtility.IsEitherPawnInCombatOrDrafted(initiator, target))
                return false;

            var rpgManager = Current.Game?.GetComponent<RimChat.DiplomacySystem.GameComponent_RPGManager>();
            return rpgManager == null || !rpgManager.IsRpgDialogueOnCooldown(target, out _);
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
