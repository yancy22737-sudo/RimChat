using HarmonyLib;
using RimChat.WorldState;
using RimWorld.Planet;

namespace RimChat.Patches
{
    /// <summary>/// Dependencies: RimWorld.Planet.WorldObject.Destroy.
 /// Responsibility: record faction settlement destruction history for fixed intel injection.
 ///</summary>
    [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.Destroy))]
    public static class WorldObjectDestroyPatch_FactionIntelLedger
    {
        private static void Postfix(WorldObject __instance)
        {
            if (!RimChatTrackedEntityRegistry.IsWorldObjectTracked(__instance))
            {
                return;
            }

            FactionIntelLedgerComponent.Instance?.RecordSettlementDestroyed(__instance);
        }
    }
}
