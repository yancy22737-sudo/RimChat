using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;
using RimChat.UI;
using RimChat.DiplomacySystem;
using RimChat.Core;

namespace RimChat.AI
{
    // Responsibility: drive pawn-to-pawn RPG dialogue approach/open flow safely.
    // Dependencies: Verse.AI JobDriver/Toils, RimWorld Messages, RimChat RPG UI and cooldown manager.
    public class JobDriver_RPGPawnDialogue : JobDriver
    {
        private const float InteractionDistanceThreshold = 1.9f;
        private const int RepathCheckIntervalTicks = 15;

        private int _lastRepathTick = -9999;
        private IntVec3 _lastTrackedTargetPos = IntVec3.Invalid;

        protected Pawn TargetPawn => ResolveTargetPawn();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn target = TargetPawn;
            if (pawn == null || target == null)
            {
                return false;
            }

            return pawn.Reserve(target, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // Fail if target is gone
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() =>
            {
                Pawn target = TargetPawn;
                return target == null || !pawn.CanReach(target, PathEndMode.InteractionCell, Danger.Deadly);
            });

            // Keep following moving target pawn until actual interaction distance is reached.
            Toil gotoTarget = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            Action originalInitAction = gotoTarget.initAction;
            gotoTarget.initAction = () =>
            {
                originalInitAction?.Invoke();
                _lastRepathTick = Find.TickManager?.TicksGame ?? 0;
                Pawn target = TargetPawn;
                _lastTrackedTargetPos = target?.Position ?? IntVec3.Invalid;
            };

            Action originalTickAction = gotoTarget.tickAction;
            gotoTarget.tickAction = () =>
            {
                originalTickAction?.Invoke();
                TryRefreshPathToMovingTarget(gotoTarget.actor);
            };
            yield return gotoTarget;

            Toil refreshTargetAlignment = Toils_Jump.JumpIf(gotoTarget, () =>
            {
                Pawn target = TargetPawn;
                if (target == null || !pawn.Spawned || !target.Spawned || pawn.Map != target.Map)
                {
                    return false;
                }

                return pawn.Position.DistanceTo(target.Position) > InteractionDistanceThreshold;
            });
            yield return refreshTargetAlignment;

            // Open the dialogue window
            Toil openDialogue = new Toil();
            openDialogue.initAction = () =>
            {
                Pawn initiator = pawn;
                Pawn target = TargetPawn;
                if (initiator != null && target != null && initiator.Spawned && target.Spawned && initiator.Map == target.Map)
                {
                    if (!RestUtility.Awake(target))
                    {
                        return;
                    }

                    if (PawnCombatStateUtility.IsEitherPawnInCombatOrDrafted(initiator, target))
                    {
                        return;
                    }

                    if (initiator.Position.DistanceTo(target.Position) > InteractionDistanceThreshold)
                    {
                        return;
                    }

                    var rpgManager = Current.Game?.GetComponent<GameComponent_RPGManager>();
                    if (rpgManager != null && rpgManager.IsRpgDialogueOnCooldown(target, out _))
                    {
                        Messages.Message(
                            "RimChat_RPGDialogue_CooldownRejected".Translate(),
                            MessageTypeDefOf.RejectInput,
                            false);
                        return;
                    }

                    Find.WindowStack.Add(new Dialog_RPGPawnDialogue(initiator, target));
                }
            };
            openDialogue.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return openDialogue;
        }

        private void TryRefreshPathToMovingTarget(Pawn actor)
        {
            Pawn target = TargetPawn;
            if (actor == null || target == null || !actor.Spawned || !target.Spawned || actor.Map != target.Map)
            {
                return;
            }

            if (actor.Position.DistanceTo(target.Position) <= InteractionDistanceThreshold)
            {
                return;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick - _lastRepathTick < RepathCheckIntervalTicks)
            {
                return;
            }

            if (_lastTrackedTargetPos == target.Position)
            {
                return;
            }

            _lastTrackedTargetPos = target.Position;
            _lastRepathTick = currentTick;
            actor.pather?.StartPath(target, PathEndMode.InteractionCell);
        }

        private Pawn ResolveTargetPawn()
        {
            Job currentJob = job ?? pawn?.jobs?.curJob;
            return currentJob?.GetTarget(TargetIndex.A).Thing as Pawn;
        }
    }
}



