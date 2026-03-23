using HarmonyLib;
using RimChat.DiplomacySystem;
using RimChat.NpcDialogue;
using RimChat.PawnRpgPush;
using RimWorld;
using Verse;

namespace RimChat.Patches
{
    /// <summary>/// Dependencies: RimWorld.Faction.TryAffectGoodwillWith.
 /// Responsibility: Translate significant goodwill shifts into proactive causal triggers while filtering passive natural goodwill decreases.
 ///</summary>
    [HarmonyPatch(typeof(Faction), nameof(Faction.TryAffectGoodwillWith))]
    public static class FactionGoodwillPatch_NpcDialogue
    {
        private static bool IsNaturalGoodwillDecrease(int goodwillChange, string reasonTag)
        {
            if (goodwillChange >= 0 || string.IsNullOrWhiteSpace(reasonTag))
            {
                return false;
            }

            string normalized = reasonTag.Trim().ToLowerInvariant();
            return normalized.Contains("natural") ||
                   normalized.Contains("decay") ||
                   normalized.Contains("drift") ||
                   normalized.Contains("baseline") ||
                   normalized.Contains("goodwillnatur");
        }

        private static void Prefix(Faction __instance, Faction other, out FactionRelationKind __state)
        {
            __state = FactionRelationKind.Neutral;
            if (__instance == null || other == null)
            {
                return;
            }

            __state = __instance.RelationKindWith(other);
        }

        private static void Postfix(
            Faction __instance,
            Faction other,
            int goodwillChange,
            bool __result,
            HistoryEventDef reason,
            FactionRelationKind __state)
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
            if (IsNaturalGoodwillDecrease(goodwillChange, reasonTag))
            {
                return;
            }

            if (goodwillChange > 0)
            {
                GameComponent_NpcDialoguePushManager.Instance?.RegisterGoodwillShiftTrigger(
                    __instance,
                    goodwillChange,
                    reasonTag,
                    likelyHostile);
            }

            GameComponent_PawnRpgDialoguePushManager.Instance?.RegisterGoodwillShiftTrigger(
                __instance,
                goodwillChange,
                reasonTag,
                likelyHostile);

            int tick = Find.TickManager?.TicksGame ?? 0;
            GameComponent_DiplomacyManager.Instance?.RecordScheduledSocialEvent(
                ScheduledSocialEventType.GoodwillShift,
                __instance,
                other,
                $"{__instance.Name} goodwill changed by {goodwillChange}.",
                $"reason={reasonTag}",
                goodwillChange,
                $"goodwill:{__instance.GetUniqueLoadID()}:{tick}:{goodwillChange}:{reasonTag}");

            FactionRelationKind currentRelation = __instance.RelationKindWith(other);
            if (__state == currentRelation)
            {
                return;
            }

            bool isTrackedShift =
                currentRelation == FactionRelationKind.Hostile ||
                currentRelation == FactionRelationKind.Ally;
            if (!isTrackedShift)
            {
                return;
            }

            int relationValue = currentRelation == FactionRelationKind.Hostile ? -1 : 1;
            GameComponent_DiplomacyManager.Instance?.RecordScheduledSocialEvent(
                ScheduledSocialEventType.RelationShift,
                __instance,
                other,
                $"{__instance.Name} relation shifted: {__state} -> {currentRelation}.",
                $"goodwillChange={goodwillChange}, reason={reasonTag}",
                relationValue,
                $"relation:{__instance.GetUniqueLoadID()}:{tick}:{__state}:{currentRelation}:{reasonTag}");
        }
    }
}
