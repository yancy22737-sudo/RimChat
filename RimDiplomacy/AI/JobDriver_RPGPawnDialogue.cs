using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;
using RimDiplomacy.UI;
using RimDiplomacy.DiplomacySystem;

namespace RimDiplomacy.AI
{
    public class JobDriver_RPGPawnDialogue : JobDriver
    {
        protected Pawn TargetPawn => (Pawn)job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // Fail if target is gone or downed
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOnDowned(TargetIndex.A);
            this.FailOn(() => TargetPawn == null || !pawn.CanReach(TargetPawn, PathEndMode.InteractionCell, Danger.Deadly));

            // Keep following moving target pawn until actual interaction distance is reached.
            Toil gotoTarget = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            yield return gotoTarget;

            Toil refreshTargetAlignment = Toils_Jump.JumpIf(gotoTarget, () =>
            {
                Pawn target = TargetPawn;
                if (target == null || !pawn.Spawned || !target.Spawned || pawn.Map != target.Map)
                {
                    return false;
                }

                return pawn.Position.DistanceTo(target.Position) > 1.9f;
            });
            yield return refreshTargetAlignment;

            // Open the dialogue window
            Toil openDialogue = new Toil();
            openDialogue.initAction = () =>
            {
                Pawn initiator = pawn;
                Pawn target = TargetPawn;
                if (initiator != null && target != null && initiator.Spawned && target.Spawned && initiator.Map == target.Map)
                {
                    if (initiator.Position.DistanceTo(target.Position) > 1.9f)
                    {
                        return;
                    }

                    var rpgManager = Current.Game?.GetComponent<GameComponent_RPGManager>();
                    if (rpgManager != null && rpgManager.IsRpgDialogueOnCooldown(target, out _))
                    {
                        Messages.Message(
                            "RimDiplomacy_RPGDialogue_CooldownRejected".Translate(),
                            MessageTypeDefOf.RejectInput,
                            false);
                        return;
                    }

                    Find.WindowStack.Add(new Dialog_RPGPawnDialogue(initiator, target));
                }
            };
            openDialogue.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return openDialogue;
        }
    }
}
