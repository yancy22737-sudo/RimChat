using HarmonyLib;
using RimChat.Memory;
using RimWorld;
using Verse;

namespace RimChat.Patches
{
    /// <summary>
    /// Dependencies: Verse.Pawn.ExitMap(bool, Rot4).
    /// Responsibility: generate RPG departure summary into faction memory when qualified NPC exits a player map.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExitMap), new[] { typeof(bool), typeof(Rot4) })]
    public static class PawnExitMapPatch_RpgMemory
    {
        private static void Prefix(Pawn __instance)
        {
            if (__instance == null || __instance.Dead || __instance.Destroyed)
            {
                return;
            }

            if (__instance.Faction == null || __instance.Faction.IsPlayer || __instance.Faction.defeated)
            {
                return;
            }

            if (!(__instance.RaceProps?.Humanlike ?? false))
            {
                return;
            }

            Map map = __instance.Map;
            if (map == null || !map.IsPlayerHome)
            {
                return;
            }

            if (!RpgDialogueTraceTracker.TryConsumeRecentForExit(__instance, out RpgDialogueTraceSnapshot trace))
            {
                return;
            }

            DialogueSummaryService.TryRecordRpgDepartSummary(__instance, trace);
        }
    }
}
