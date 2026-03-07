using System.Collections.Generic;
using System.Linq;
using RimDiplomacy.Core;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimDiplomacy.PawnRpgPush
{
    /// <summary>
    /// Dependencies: RimWorld pawn/map/job systems, RimDiplomacy settings, Verse utility APIs.
    /// Responsibility: Candidate discovery, relationship/availability gating, and busy-state checks for PawnRPG proactive flow.
    /// </summary>
    public partial class GameComponent_PawnRpgDialoguePushManager
    {
        private IEnumerable<Pawn> GetFactionNpcCandidates(Faction faction)
        {
            if (!IsValidTargetFaction(faction) || Find.Maps == null)
            {
                yield break;
            }

            foreach (Map map in Find.Maps)
            {
                if (map?.mapPawns?.AllPawnsSpawned == null || !map.IsPlayerHome)
                {
                    continue;
                }

                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (IsEligibleNpcPawn(pawn) && pawn.Faction == faction)
                    {
                        yield return pawn;
                    }
                }
            }
        }

        private List<Faction> GetActiveCandidateFactionsOnPlayerMaps()
        {
            var factions = new HashSet<Faction>();
            if (Find.Maps == null)
            {
                return factions.ToList();
            }

            foreach (Map map in Find.Maps)
            {
                if (map?.mapPawns?.AllPawnsSpawned == null || !map.IsPlayerHome)
                {
                    continue;
                }

                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (IsEligibleNpcPawn(pawn) && IsValidTargetFaction(pawn.Faction))
                    {
                        factions.Add(pawn.Faction);
                    }
                }
            }

            return factions.ToList();
        }

        private bool IsEligibleNpcPawn(Pawn pawn)
        {
            return pawn != null &&
                   pawn.Spawned &&
                   pawn.RaceProps?.Humanlike == true &&
                   pawn.Faction != null &&
                   !pawn.Dead &&
                   !pawn.Destroyed;
        }

        private bool TryResolvePairForFaction(Faction faction, int currentTick, bool bypassAvailability, bool bypassCooldown, bool bypassRelation, out Pawn npcPawn, out Pawn playerPawn)
        {
            npcPawn = null;
            playerPawn = null;
            if (!IsValidTargetFaction(faction))
            {
                return false;
            }

            foreach (Pawn candidate in GetFactionNpcCandidates(faction))
            {
                if (!bypassCooldown && IsNpcOnCooldown(candidate, currentTick))
                {
                    continue;
                }

                if (!TrySelectPlayerPawn(candidate, bypassAvailability, bypassRelation, out Pawn receiver))
                {
                    continue;
                }

                if (!bypassAvailability && IsPawnUnavailable(candidate))
                {
                    continue;
                }

                npcPawn = candidate;
                playerPawn = receiver;
                return true;
            }

            return false;
        }

        private bool TrySelectPlayerPawn(Pawn npcPawn, bool bypassAvailability, bool bypassRelation, out Pawn playerPawn)
        {
            playerPawn = null;
            if (npcPawn?.Map?.mapPawns?.AllPawnsSpawned == null)
            {
                return false;
            }

            Pawn best = null;
            int bestScore = int.MinValue;
            foreach (Pawn colonist in GetPlayerDialogueTargets(npcPawn.Map))
            {
                if (colonist == npcPawn || !IsEligiblePlayerPawn(colonist))
                {
                    continue;
                }

                bool intimate = HasIntimateRelation(npcPawn, colonist);
                int opinion = GetOpinion(npcPawn, colonist);
                if (!bypassRelation && !intimate && opinion < 35)
                {
                    continue;
                }

                if (!bypassAvailability && IsPawnUnavailable(colonist))
                {
                    continue;
                }

                int score = intimate ? 1000 + opinion : opinion;
                if (score > bestScore)
                {
                    best = colonist;
                    bestScore = score;
                }
            }

            if (best == null)
            {
                return false;
            }

            playerPawn = best;
            return true;
        }

        private IEnumerable<Pawn> GetPlayerDialogueTargets(Map map)
        {
            if (map?.mapPawns?.AllPawnsSpawned == null)
            {
                yield break;
            }

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (IsEligiblePlayerPawn(pawn))
                {
                    yield return pawn;
                }
            }
        }

        private bool IsEligiblePlayerPawn(Pawn pawn)
        {
            return pawn != null &&
                   pawn.Spawned &&
                   pawn.RaceProps?.Humanlike == true &&
                   pawn.Faction == Faction.OfPlayer &&
                   !pawn.Dead &&
                   !pawn.Destroyed;
        }

        private bool HasQualifiedPlayerRelation(Pawn npcPawn)
        {
            if (npcPawn?.Map?.mapPawns?.AllPawnsSpawned == null)
            {
                return false;
            }

            foreach (Pawn colonist in GetPlayerDialogueTargets(npcPawn.Map))
            {
                if (colonist == npcPawn || !IsEligiblePlayerPawn(colonist))
                {
                    continue;
                }

                if (HasIntimateRelation(npcPawn, colonist) || GetOpinion(npcPawn, colonist) >= 35)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasIntimateRelation(Pawn npcPawn, Pawn playerPawn)
        {
            if (npcPawn?.relations == null || playerPawn == null)
            {
                return false;
            }

            return HasDirectRelation(npcPawn, playerPawn, PawnRelationDefOf.Spouse) ||
                   HasDirectRelation(npcPawn, playerPawn, PawnRelationDefOf.Fiance) ||
                   HasDirectRelation(npcPawn, playerPawn, PawnRelationDefOf.Lover);
        }

        private bool HasDirectRelation(Pawn npcPawn, Pawn playerPawn, PawnRelationDef relationDef)
        {
            return relationDef != null && npcPawn.relations.DirectRelationExists(relationDef, playerPawn);
        }

        private int GetOpinion(Pawn npcPawn, Pawn playerPawn)
        {
            return npcPawn?.relations == null || playerPawn == null ? 0 : npcPawn.relations.OpinionOf(playerPawn);
        }

        private int GetFactionNpcReadyTick(Faction faction, int currentTick)
        {
            int earliest = int.MaxValue;
            bool foundNpc = false;
            foreach (Pawn npc in GetFactionNpcCandidates(faction))
            {
                foundNpc = true;
                int readyTick = GetNpcReadyTick(npc);
                if (readyTick < earliest)
                {
                    earliest = readyTick;
                }
            }

            if (!foundNpc)
            {
                return currentTick + BlockedRetryTicks;
            }

            return Mathf.Max(currentTick, earliest);
        }

        private bool IsNpcOnCooldown(Pawn pawn, int currentTick)
        {
            return GetNpcReadyTick(pawn) > currentTick;
        }

        private int GetNpcReadyTick(Pawn pawn)
        {
            if (pawn == null)
            {
                return int.MaxValue;
            }

            PawnRpgNpcPushState state = GetOrCreateNpcState(pawn);
            if (state.lastNpcEvaluateTick <= 0)
            {
                return 0;
            }

            return state.lastNpcEvaluateTick + NpcEvaluateCooldownTicks;
        }

        private PawnRpgNpcPushState GetOrCreateNpcState(Pawn pawn)
        {
            PawnRpgNpcPushState state = npcPushStates.FirstOrDefault(s => s?.pawn == pawn);
            if (state != null)
            {
                return state;
            }

            state = new PawnRpgNpcPushState { pawn = pawn };
            npcPushStates.Add(state);
            return state;
        }

        private bool IsPlayerBusy()
        {
            var settings = RimDiplomacyMod.Instance?.InstanceSettings;
            if (settings == null)
            {
                return false;
            }

            if (settings.EnableBusyByDrafted && IsBusyByDrafted())
            {
                return true;
            }

            if (settings.EnableBusyByHostiles && IsBusyByHostiles())
            {
                return true;
            }

            return settings.EnableBusyByClickRate && clickTicks.Count >= ClickBusyThreshold;
        }

        private bool IsBusyByDrafted()
        {
            if (Find.Maps == null)
            {
                return false;
            }

            foreach (Map map in Find.Maps)
            {
                if (map?.mapPawns?.FreeColonistsSpawned == null)
                {
                    continue;
                }

                if (map.mapPawns.FreeColonistsSpawned.Any(p => p != null && p.Drafted))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsBusyByHostiles()
        {
            if (Find.Maps == null)
            {
                return false;
            }

            foreach (Map map in Find.Maps)
            {
                if (map == null || !map.IsPlayerHome || map.mapPawns?.AllPawnsSpawned == null)
                {
                    continue;
                }

                if (map.mapPawns.AllPawnsSpawned.Any(p => p != null && p.HostileTo(Faction.OfPlayer)))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasNearbyHiveThreat()
        {
            ThingDef hiveDef = DefDatabase<ThingDef>.GetNamedSilentFail("Hive");
            if (hiveDef == null || Find.Maps == null)
            {
                return false;
            }

            foreach (Map map in Find.Maps)
            {
                if (map == null || !map.IsPlayerHome || map.listerThings == null)
                {
                    continue;
                }

                List<Thing> hives = map.listerThings.ThingsOfDef(hiveDef);
                if (hives != null && hives.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void TrackClickSignal(int currentTick)
        {
            var settings = RimDiplomacyMod.Instance?.InstanceSettings;
            if (settings?.EnableBusyByClickRate != true)
            {
                clickTicks.Clear();
                return;
            }

            while (clickTicks.Count > 0 && currentTick - clickTicks.Peek() > ClickWindowTicks)
            {
                clickTicks.Dequeue();
            }
        }

        private bool IsPawnUnavailable(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed || !pawn.Spawned || pawn.Downed)
            {
                return true;
            }

            if (!RestUtility.Awake(pawn))
            {
                return true;
            }

            return IsPawnWorking(pawn);
        }

        private bool IsPawnWorking(Pawn pawn)
        {
            Job currentJob = pawn?.CurJob;
            JobDef jobDef = currentJob?.def;
            if (jobDef == null)
            {
                return false;
            }

            if (jobDef == JobDefOf.LayDown || jobDef == JobDefOf.Wait || jobDef == JobDefOf.Wait_Combat)
            {
                return false;
            }

            return jobDef.joyKind == null;
        }

        private bool TryGetMoodPercent(Pawn pawn, out float mood)
        {
            mood = 1f;
            if (pawn?.needs?.mood == null)
            {
                return false;
            }

            mood = pawn.needs.mood.CurLevelPercentage;
            return true;
        }
    }
}


