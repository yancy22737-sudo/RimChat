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

            if (!targetPawn.RaceProps.Humanlike)
                yield break;

            string label = "RimDiplomacy_RPGDialogue_Dialogue".Translate();
            yield return new FloatMenuOption(label, () =>
            {
                Find.WindowStack.Add(new Dialog_RPGPawnDialogue(selPawn, targetPawn));
            }, MenuOptionPriority.Default);
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
