using RimWorld;
using Verse;

namespace RimChat.Core
{
    /// <summary>
    /// Dependencies: RimWorld pawn/job defs and Verse pawn runtime state.
    /// Responsibility: provide a unified combat-or-drafted gate for pawn dialogue entry points.
    /// </summary>
    internal static class PawnCombatStateUtility
    {
        internal static bool IsEitherPawnInCombatOrDrafted(Pawn first, Pawn second)
        {
            return IsPawnInCombatOrDrafted(first) || IsPawnInCombatOrDrafted(second);
        }

        internal static bool IsPawnInCombatOrDrafted(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed)
            {
                return false;
            }

            if (pawn.Drafted)
            {
                return true;
            }

            return IsCombatJob(pawn.CurJob?.def);
        }

        private static bool IsCombatJob(JobDef jobDef)
        {
            return jobDef == JobDefOf.Wait_Combat ||
                   jobDef == JobDefOf.AttackMelee ||
                   jobDef == JobDefOf.AttackStatic ||
                   jobDef == JobDefOf.UseVerbOnThing;
        }
    }
}
