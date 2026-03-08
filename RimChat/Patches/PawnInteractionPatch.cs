// Using System;
// Using System.Collections.Generic;
// Using System.Linq;
// Using HarmonyLib;
// Using RimWorld;
// Using UnityEngine;
// Using Verse;
// Using RimChat.UI;
// Using RimChat.Core;
////namespace RimChat.Patches
// {
//// NOTE: This patch is disabled for RimWorld 1.6 compatibility
//// FloatMenuMakerMap.AddHumanlikeOrders was removed in 1.6
//// Alternative: Use FloatMenuOptionProvider system or other interaction methods
///* // [HarmonyPatch(typeof(FloatMenuMakerMap), "AddHumanlikeOrders")]
// Public static class PawnInteractionPatch
// {
// [HarmonyPostfix]
// Public static void Postfix(Vector3 clickPos, Pawn pawn, List<FloatMenuOption> opts)
// {
// If (!RimChatMod.Settings.EnableRPGDialogue) return;
//// // Only allow player pawns to initiate
// If (pawn.Faction != Faction.OfPlayer) return;
//// // Find what was clicked on
// IntVec3 c = IntVec3.FromVector3(clickPos);
// Foreach (Thing t in c.GetThingList(pawn.Map))
// {
// If (t is Pawn targetPawn && targetPawn != pawn && targetPawn.RaceProps.Humanlike)
// {
//// Add "Dialogue" option
// String label = "RimChat_RPGDialogue_Dialogue".Translate();
//// Action action = () =>
// {
//// Check distance or just open UI?
//// For now, let's just open the UI if within range, or move to target.
// If (pawn.CanReach(targetPawn, Verse.AI.PathEndMode.Touch, Danger.Deadly))
// {
// Find.WindowStack.Add(new Dialog_RPGPawnDialogue(pawn, targetPawn));
// }
// Else
// {
// Messages.Message("CannotReach".Translate(), targetPawn, MessageTypeDefOf.RejectInput, false);
// }
// };
//// opts.Add(new FloatMenuOption(label, action, MenuOptionPriority.Default, null, targetPawn));
// }
// }
// }
// }
// */
// }
