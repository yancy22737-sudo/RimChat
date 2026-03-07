using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;
using RimDiplomacy.UI;
using RimDiplomacy.Core;

namespace RimDiplomacy.Comp
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
                yield return new FloatMenuOption("CannotReach".Translate() + ": " + "RimDiplomacy_Unreachable".Translate(), null);
                yield break;
            }

            if (RimDiplomacyMod.Settings == null || !RimDiplomacyMod.Settings.EnableRPGDialogue)
                yield break;

            if (!CanShowRpgDialogueOption(selPawn, targetPawn))
                yield break;

            string label = "RimDiplomacy_RPGDialogue_Dialogue".Translate();
            yield return new FloatMenuOption(label, () =>
            {
                var rpgManager = Current.Game?.GetComponent<RimDiplomacy.DiplomacySystem.GameComponent_RPGManager>();
                if (rpgManager != null && rpgManager.IsRpgDialogueOnCooldown(targetPawn, out int remainingTicks))
                {
                    Messages.Message(
                        "RimDiplomacy_RPGDialogue_CooldownRejected".Translate(),
                        MessageTypeDefOf.RejectInput,
                        false);
                    return;
                }

                JobDef dialogueJobDef = DefDatabase<JobDef>.GetNamedSilentFail("RimDiplomacy_RPGDialogue");
                if (dialogueJobDef == null)
                {
                    Log.Warning("[RimDiplomacy] Missing JobDef RimDiplomacy_RPGDialogue, fallback to direct dialogue open.");
                    Find.WindowStack.Add(new Dialog_RPGPawnDialogue(selPawn, targetPawn));
                    return;
                }

                Job dialogueJob = JobMaker.MakeJob(dialogueJobDef, targetPawn);
                dialogueJob.playerForced = true;
                selPawn.jobs.TryTakeOrderedJob(dialogueJob, JobTag.Misc);
            }, MenuOptionPriority.Default);
        }

        private bool CanShowRpgDialogueOption(Pawn initiator, Pawn target)
        {
            if (initiator == null || target == null)
                return false;

            if (!target.RaceProps.Humanlike || target.Dead || target.Destroyed || target.Downed)
                return false;

            if (!initiator.Spawned || !target.Spawned || initiator.Map != target.Map)
                return false;

            var rpgManager = Current.Game?.GetComponent<RimDiplomacy.DiplomacySystem.GameComponent_RPGManager>();
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
