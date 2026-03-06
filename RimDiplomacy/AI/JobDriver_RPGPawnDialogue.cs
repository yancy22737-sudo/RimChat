using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;
using RimDiplomacy.UI;

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

            // Go to the target
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);

            // Open the dialogue window
            Toil openDialogue = new Toil();
            openDialogue.initAction = () =>
            {
                Pawn initiator = pawn;
                Pawn target = TargetPawn;
                if (initiator != null && target != null)
                {
                    Find.WindowStack.Add(new Dialog_RPGPawnDialogue(initiator, target));
                }
            };
            openDialogue.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return openDialogue;
        }
    }
}