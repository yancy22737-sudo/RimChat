using System.Collections.Generic;
using HarmonyLib;
using RimChat.Comp;
using RimChat.Dialogue;
using RimChat.UI;
using RimChat.Core;
using RimChat.WorldState;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimChat.Patches
{
    /// <summary>
    /// Dependencies: Harmony patch, Verse.Pawn, RimWorld.FloatMenuOption.
    /// Responsibility: Provide a global fallback for RPG dialogue float menu options.
    /// This patch ensures dialogue options appear even for pawns that don't have CompPawnDialogue injected.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetFloatMenuOptions))]
    internal static class Pawn_FloatMenuOptions_FallbackPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, Pawn selPawn, ref IEnumerable<FloatMenuOption> __result)
        {
            if (selPawn == null || __instance == null)
                return;

            Pawn targetPawn = __instance;

            if (targetPawn == selPawn)
                return;

            if (targetPawn.Dead || targetPawn.Destroyed)
                return;

            if (!selPawn.Spawned || !targetPawn.Spawned || selPawn.Map != targetPawn.Map)
                return;

            if (targetPawn.GetComp<CompPawnDialogue>() != null)
                return;

            if (!PawnDialogueCompDefInjector.TryInjectForPawn(targetPawn))
            {
                string raceReason = PawnDialogueRoutingPolicy.GetIneligibleRaceReason(targetPawn);
                if (raceReason != null)
                    return;
            }

            var options = new List<FloatMenuOption>(__result);
            foreach (var option in GenerateDialogueOptions(selPawn, targetPawn))
            {
                options.Add(option);
            }
            __result = options;
        }

        private static IEnumerable<FloatMenuOption> GenerateDialogueOptions(Pawn selPawn, Pawn targetPawn)
        {
            RimChatTrackedEntityRegistry.TrackPawn(selPawn);
            RimChatTrackedEntityRegistry.TrackPawn(targetPawn);

            string raceReason = PawnDialogueRoutingPolicy.GetIneligibleRaceReason(targetPawn);
            if (raceReason != null)
            {
                yield return DisabledOption(raceReason);
                yield break;
            }

            if (RimChatMod.Settings == null || !RimChatMod.Settings.EnableRPGDialogue)
            {
                yield return DisabledOption("RimChat_Converse_Disabled_RpgOff");
                yield break;
            }

            if (!selPawn.CanReach(targetPawn, PathEndMode.InteractionCell, Danger.Deadly))
            {
                yield return DisabledOption("RimChat_Converse_Disabled_Unreachable");
                yield break;
            }

            if (PawnCombatStateUtility.IsEitherPawnInCombat(selPawn, targetPawn))
                yield break;

            if (PawnCombatStateUtility.IsEitherPawnDrafted(selPawn, targetPawn))
            {
                yield return DisabledOption("RimChat_Converse_Disabled_Drafted");
                yield break;
            }

            if (!RestUtility.Awake(targetPawn) || targetPawn.Downed)
            {
                if (targetPawn.Downed && !RestUtility.Awake(targetPawn))
                    yield return DisabledOption("RimChat_Converse_Disabled_DownedAsleep");
                else if (targetPawn.Downed)
                    yield return DisabledOption("RimChat_Converse_Disabled_Downed");
                else
                    yield return DisabledOption("RimChat_Converse_Disabled_Asleep");
                yield break;
            }

            var rpgManager = Current.Game?.GetComponent<RimChat.DiplomacySystem.GameComponent_RPGManager>();
            if (rpgManager != null && rpgManager.IsRpgDialogueOnCooldown(targetPawn, out int remainingTicks))
            {
                float remainingHours = System.Math.Max(0f, remainingTicks / 2500f);
                string cooldownLabel = "RimChat_Converse_Disabled_Cooldown".Translate(remainingHours.ToString("F1"));
                yield return new FloatMenuOption(cooldownLabel, null);
                yield break;
            }

            string label = "RimChat_Converse".Translate();
            yield return new FloatMenuOption(label, () =>
            {
                RimChatTrackedEntityRegistry.TrackPawn(selPawn);
                RimChatTrackedEntityRegistry.TrackPawn(targetPawn);

                JobDef dialogueJobDef = DefDatabase<JobDef>.GetNamedSilentFail("RimChat_RPGDialogue");
                if (dialogueJobDef == null)
                {
                    Log.Warning("[RimChat] Missing JobDef RimChat_RPGDialogue, fallback to direct dialogue open.");
                    DialogueWindowCoordinator.TryOpen(
                        DialogueOpenIntent.CreateRpg(selPawn, targetPawn, selPawn.Map),
                        out _);
                    return;
                }

                Job dialogueJob = JobMaker.MakeJob(dialogueJobDef, targetPawn);
                dialogueJob.playerForced = true;

                if (selPawn.jobs?.curJob != null)
                {
                    selPawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }

                selPawn.jobs.TryTakeOrderedJob(dialogueJob, JobTag.Misc);
            }, MenuOptionPriority.Low);
        }

        private static FloatMenuOption DisabledOption(string reasonKey)
        {
            string label = "RimChat_Converse_Disabled".Translate(reasonKey.Translate());
            return new FloatMenuOption(label, null);
        }
    }
}
