using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;
using RimChat.UI;
using RimChat.Dialogue;
using RimChat.DiplomacySystem;
using RimChat.Core;

namespace RimChat.AI
{
    // Responsibility: drive pawn-to-pawn RPG dialogue approach/open flow safely.
    // Dependencies: Verse.AI JobDriver/Toils, RimWorld Messages, RimChat RPG UI and cooldown manager.
    public class JobDriver_RPGPawnDialogue : JobDriver
    {
        private const float InteractionDistanceThreshold = 1.5f;
        private const float InteractionDistanceLenient = 2.8f;
        private const int RepathCheckIntervalTicks = 3;
        private const int MaxRepathCount = 5;

        private int _lastRepathTick = -9999;
        private int _repathCount;
        private IntVec3 _lastTrackedTargetPos = IntVec3.Invalid;

        protected Pawn TargetPawn => ResolveTargetPawn();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn target = TargetPawn;
            if (pawn == null || target == null)
            {
                return false;
            }

            if (!PawnDialogueRoutingPolicy.ShouldUseRpgDialogue(pawn, target, out _))
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
                return target == null ||
                       target.Downed ||
                       !PawnDialogueRoutingPolicy.ShouldUseRpgDialogue(pawn, target, out _) ||
                       !pawn.CanReach(target, PathEndMode.Touch, Danger.Deadly);
            });

            // Follow the target; use Touch mode for closest approach.
            Toil gotoTarget = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
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

                // After too many repaths, stop chasing and open from current distance
                if (_repathCount >= MaxRepathCount)
                {
                    return pawn.Position.DistanceTo(target.Position) > InteractionDistanceLenient;
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
                    if (!RestUtility.Awake(target) || target.Downed)
                    {
                        return;
                    }

                    if (PawnCombatStateUtility.IsEitherPawnInCombat(initiator, target) ||
                        PawnCombatStateUtility.IsEitherPawnDrafted(initiator, target))
                    {
                        return;
                    }

                    float dist = initiator.Position.DistanceTo(target.Position);
                    float threshold = _repathCount >= MaxRepathCount
                        ? InteractionDistanceLenient
                        : InteractionDistanceThreshold;
                    if (dist > threshold)
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

                    DialogueWindowCoordinator.TryOpen(
                        DialogueOpenIntent.CreateRpg(initiator, target, initiator.Map),
                        out _);
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
            _repathCount++;
            actor.pather?.StartPath(target, PathEndMode.Touch);
        }

        private Pawn ResolveTargetPawn()
        {
            Job currentJob = job ?? pawn?.jobs?.curJob;
            return currentJob?.GetTarget(TargetIndex.A).Thing as Pawn;
        }
    }
}



