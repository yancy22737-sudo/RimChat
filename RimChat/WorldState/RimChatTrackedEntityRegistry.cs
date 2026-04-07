using System;
using System.Collections.Generic;
using Verse;
using RimWorld.Planet;

namespace RimChat.WorldState
{
    /// <summary>
    /// Dependencies: Verse current game objects and lightweight runtime hash sets.
    /// Responsibility: keep an explicit registry of RimChat-tracked pawns/buildings/world objects so high-frequency patches only process relevant targets.
    /// </summary>
    internal static class RimChatTrackedEntityRegistry
    {
        private static readonly HashSet<int> TrackedPawnIds = new HashSet<int>();
        private static readonly HashSet<int> TrackedThingIds = new HashSet<int>();
        private static readonly HashSet<int> TrackedWorldObjectIds = new HashSet<int>();

        internal static void Reset()
        {
            TrackedPawnIds.Clear();
            TrackedThingIds.Clear();
            TrackedWorldObjectIds.Clear();
        }

        internal static void TrackPawn(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            if (pawn.thingIDNumber > 0)
            {
                TrackedPawnIds.Add(pawn.thingIDNumber);
            }
        }

        internal static void TrackThing(Thing thing)
        {
            if (thing == null)
            {
                return;
            }

            if (thing.thingIDNumber > 0)
            {
                TrackedThingIds.Add(thing.thingIDNumber);
            }
        }

        internal static void TrackWorldObject(WorldObject worldObject)
        {
            if (worldObject == null)
            {
                return;
            }

            if (worldObject.ID >= 0)
            {
                TrackedWorldObjectIds.Add(worldObject.ID);
            }
        }

        internal static bool IsPawnTracked(Pawn pawn)
        {
            return pawn != null && pawn.thingIDNumber > 0 && TrackedPawnIds.Contains(pawn.thingIDNumber);
        }

        internal static bool IsThingTracked(Thing thing)
        {
            return thing != null && thing.thingIDNumber > 0 && TrackedThingIds.Contains(thing.thingIDNumber);
        }

        internal static bool IsWorldObjectTracked(WorldObject worldObject)
        {
            return worldObject != null && worldObject.ID >= 0 && TrackedWorldObjectIds.Contains(worldObject.ID);
        }

        internal static void PrimeFromCurrentGame()
        {
            if (Current.Game == null)
            {
                return;
            }

            foreach (Map map in Find.Maps)
            {
                if (map == null || !map.IsPlayerHome)
                {
                    continue;
                }

                if (map.mapPawns?.AllPawnsSpawned != null)
                {
                    foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                    {
                        if (pawn?.Faction != null)
                        {
                            TrackPawn(pawn);
                        }
                    }
                }

                if (map.listerBuildings?.allBuildingsColonist != null)
                {
                    foreach (Building building in map.listerBuildings.allBuildingsColonist)
                    {
                        TrackThing(building);
                    }
                }
            }

            foreach (WorldObject worldObject in Find.WorldObjects?.AllWorldObjects ?? new List<WorldObject>())
            {
                if (worldObject?.Faction != null && !worldObject.Faction.IsPlayer)
                {
                    TrackWorldObject(worldObject);
                }
            }
        }
    }
}
