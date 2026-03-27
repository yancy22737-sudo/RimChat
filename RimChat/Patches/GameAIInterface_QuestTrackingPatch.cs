using System.Collections.Generic;
using HarmonyLib;
using RimChat.DiplomacySystem;

namespace RimChat.Patches
{
    /// <summary>/// Dependencies: GameAIInterface.CreateQuest/ExposeData.
 /// Responsibility: persist and update RimChat create_quest publication tracking records.
 ///</summary>
    [HarmonyPatch]
    public static class GameAIInterface_QuestTrackingPatch
    {
        [HarmonyPatch(typeof(GameAIInterface), nameof(GameAIInterface.ExposeData))]
        [HarmonyPostfix]
        private static void ExposeDataPostfix(GameAIInterface __instance)
        {
            __instance?.ExposeQuestPublicationData();
        }

        [HarmonyPatch(typeof(GameAIInterface), nameof(GameAIInterface.CreateQuest))]
        [HarmonyPrefix]
        private static void CreateQuestPrefix(ref HashSet<int> __state)
        {
            __state = GameAIInterface.CaptureCurrentQuestIdsForTracking();
        }

        [HarmonyPatch(typeof(GameAIInterface), nameof(GameAIInterface.CreateQuest))]
        [HarmonyPostfix]
        private static void CreateQuestPostfix(
            GameAIInterface __instance,
            string questDefName,
            Dictionary<string, object> parameters,
            GameAIInterface.APIResult __result,
            HashSet<int> __state)
        {
            __instance?.TryTrackCreateQuestResult(questDefName, parameters, __result, __state);
        }
    }
}
