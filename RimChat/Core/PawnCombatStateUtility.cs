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
        internal static bool IsEitherPawnInCombat(Pawn first, Pawn second)
        {
            return IsPawnInCombat(first) || IsPawnInCombat(second);
        }

        internal static bool IsEitherPawnDrafted(Pawn first, Pawn second)
        {
            return IsPawnDrafted(first) || IsPawnDrafted(second);
        }

        internal static bool IsPawnInCombat(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed)
                return false;

            return IsCombatJob(pawn.CurJob?.def);
        }

        internal static bool IsPawnDrafted(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed)
                return false;

            return pawn.Drafted;
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
