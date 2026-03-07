using HarmonyLib;
using RimDiplomacy.NpcDialogue;
using RimDiplomacy.PawnRpgPush;
using RimWorld;

namespace RimDiplomacy.Patches
{
    /// <summary>
    /// Dependencies: RimWorld.Faction.TryAffectGoodwillWith.
    /// Responsibility: Translate significant goodwill shifts into proactive causal triggers.
    /// </summary>
    [HarmonyPatch(typeof(Faction), nameof(Faction.TryAffectGoodwillWith))]
    public static class FactionGoodwillPatch_NpcDialogue
    {
        private static void Postfix(
            Faction __instance,
            Faction other,
            int goodwillChange,
            bool __result,
            HistoryEventDef reason)
        {
            if (!__result || __instance == null || other != Faction.OfPlayer || __instance.IsPlayer)
            {
                return;
            }

            if (System.Math.Abs(goodwillChange) < 10)
            {
                return;
            }

            bool likelyHostile =
                goodwillChange <= -18 ||
                __instance.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile;
            string reasonTag = reason?.defName ?? string.Empty;

            GameComponent_NpcDialoguePushManager.Instance?.RegisterGoodwillShiftTrigger(
                __instance,
                goodwillChange,
                reasonTag,
                likelyHostile);

            GameComponent_PawnRpgDialoguePushManager.Instance?.RegisterGoodwillShiftTrigger(
                __instance,
                goodwillChange,
                reasonTag,
                likelyHostile);
        }
    }
}
