using HarmonyLib;
using RimChat.WorldState;
using Verse;

namespace RimChat.Patches
{
    /// <summary>
    /// Dependencies: Verse.Pawn.Kill.
    /// Responsibility: feed raid casualty aggregation in world-event ledger.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class PawnKillPatch_WorldEventLedger
    {
        private static void Postfix(Pawn __instance, DamageInfo? dinfo, Hediff exactCulprit)
        {
            WorldEventLedgerComponent.Instance?.NotifyPawnKilled(__instance, dinfo);
        }
    }
}
