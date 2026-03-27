using HarmonyLib;
using RimChat.WorldState;
using Verse;

namespace RimChat.Patches
{
    /// <summary>/// Dependencies: Verse.Thing.TakeDamage.
 /// Responsibility: feed player-building loss intel for raid damage aggregation.
 ///</summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class ThingTakeDamagePatch_FactionIntelLedger
    {
        private static void Postfix(Thing __instance, DamageInfo dinfo)
        {
            if (__instance == null || !__instance.Destroyed)
            {
                return;
            }

            FactionIntelLedgerComponent.Instance?.NotifyBuildingDestroyed(__instance, dinfo);
        }
    }
}
