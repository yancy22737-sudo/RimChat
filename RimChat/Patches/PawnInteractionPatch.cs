//using System;
//using System.Collections.Generic;
//using System.Linq;
//using HarmonyLib;
//using RimWorld;
//using UnityEngine;
//using Verse;
//using RimChat.UI;
//using RimChat.Core;
//
//namespace RimChat.Patches
//{
//    // NOTE: This patch is disabled for RimWorld 1.6 compatibility
//    // FloatMenuMakerMap.AddHumanlikeOrders was removed in 1.6
//    // Alternative: Use FloatMenuOptionProvider system or other interaction methods
//    /*
//    [HarmonyPatch(typeof(FloatMenuMakerMap), "AddHumanlikeOrders")]
//    public static class PawnInteractionPatch
//    {
//        [HarmonyPostfix]
//        public static void Postfix(Vector3 clickPos, Pawn pawn, List<FloatMenuOption> opts)
//        {
//            if (!RimChatMod.Settings.EnableRPGDialogue) return;
//
//            // Only allow player pawns to initiate
//            if (pawn.Faction != Faction.OfPlayer) return;
//
//            // Find what was clicked on
//            IntVec3 c = IntVec3.FromVector3(clickPos);
//            foreach (Thing t in c.GetThingList(pawn.Map))
//            {
//                if (t is Pawn targetPawn && targetPawn != pawn && targetPawn.RaceProps.Humanlike)
//                {
//                    // Add "Dialogue" option
//                    string label = "RimChat_RPGDialogue_Dialogue".Translate();
//                    
//                    Action action = () =>
//                    {
//                        // Check distance or just open UI? 
//                        // For now, let's just open the UI if within range, or move to target.
//                        if (pawn.CanReach(targetPawn, Verse.AI.PathEndMode.Touch, Danger.Deadly))
//                        {
//                            Find.WindowStack.Add(new Dialog_RPGPawnDialogue(pawn, targetPawn));
//                        }
//                        else
//                        {
//                            Messages.Message("CannotReach".Translate(), targetPawn, MessageTypeDefOf.RejectInput, false);
//                        }
//                    };
//
//                    opts.Add(new FloatMenuOption(label, action, MenuOptionPriority.Default, null, targetPawn));
//                }
//            }
//        }
//    }
//    */
//}
