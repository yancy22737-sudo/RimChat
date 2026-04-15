using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimChat.WorldState
{
    /// <summary>
    /// Dependencies: Verse map/pawn runtime state.
    /// Responsibility: provide a shared, incremental raid threat snapshot for world-state components.
    /// </summary>
    internal interface IRaidSnapshotProvider
    {
        bool TryGetSnapshot(int currentTick, out RaidThreatSnapshot snapshot);
        bool TryForceRefreshNow(int currentTick, out RaidThreatSnapshot snapshot);
        int ResolvePlayerDownedCount(Map map, int currentTick);
        void Reset();
    }

    internal sealed class RaidThreatMapSnapshot
    {
        public RaidThreatMapSnapshot(Map map, int playerDownedCount, List<Faction> hostileFactions)
        {
            Map = map;
            MapId = map?.uniqueID ?? -1;
            PlayerDownedCount = playerDownedCount;
            HostileFactions = hostileFactions ?? new List<Faction>();
        }

        public Map Map { get; }
        public int MapId { get; }
        public int PlayerDownedCount { get; }
        public IReadOnlyList<Faction> HostileFactions { get; }
    }

    internal sealed class RaidThreatSnapshot
    {
        public RaidThreatSnapshot(int tick, List<RaidThreatMapSnapshot> maps)
        {
            Tick = tick;
            Maps = maps ?? new List<RaidThreatMapSnapshot>();
        }

        public int Tick { get; }
        public IReadOnlyList<RaidThreatMapSnapshot> Maps { get; }
    }

    internal sealed class RaidThreatSnapshotProvider : IRaidSnapshotProvider
    {
        private const int SnapshotIntervalTicks = 250;
        private const int MaxMapsPerPass = 2;
        private const int MaxPawnsPerPass = 900;
        private const int ForceRefreshMapBudget = 999;
        private const int ForceRefreshPawnBudget = 200000;

        private readonly List<PendingMapScanState> pendingMapScans = new List<PendingMapScanState>();
        private RaidThreatSnapshot latestSnapshot = new RaidThreatSnapshot(0, new List<RaidThreatMapSnapshot>());
        private int latestSnapshotTick = int.MinValue;
        private int pendingMapIndex;
        private bool scanInProgress;

        public static RaidThreatSnapshotProvider Instance { get; } = new RaidThreatSnapshotProvider();

        private RaidThreatSnapshotProvider()
        {
        }

        public bool TryGetSnapshot(int currentTick, out RaidThreatSnapshot snapshot)
        {
            TryAdvanceScan(currentTick, forceRefresh: false);
            snapshot = latestSnapshot;
            return snapshot != null;
        }

        public bool TryForceRefreshNow(int currentTick, out RaidThreatSnapshot snapshot)
        {
            TryAdvanceScan(currentTick, forceRefresh: true);
            snapshot = latestSnapshot;
            return snapshot != null;
        }

        public int ResolvePlayerDownedCount(Map map, int currentTick)
        {
            if (map == null)
            {
                return 0;
            }

            TryGetSnapshot(currentTick, out RaidThreatSnapshot snapshot);
            if (snapshot?.Maps != null)
            {
                for (int i = 0; i < snapshot.Maps.Count; i++)
                {
                    RaidThreatMapSnapshot mapSnapshot = snapshot.Maps[i];
                    if (mapSnapshot != null && mapSnapshot.MapId == map.uniqueID)
                    {
                        return mapSnapshot.PlayerDownedCount;
                    }
                }
            }

            return CountPlayerDownedFallback(map);
        }

        public void Reset()
        {
            pendingMapScans.Clear();
            pendingMapIndex = 0;
            scanInProgress = false;
            latestSnapshotTick = int.MinValue;
            latestSnapshot = new RaidThreatSnapshot(0, new List<RaidThreatMapSnapshot>());
        }

        private void TryAdvanceScan(int currentTick, bool forceRefresh)
        {
            if (!ShouldScan(currentTick, forceRefresh))
            {
                return;
            }

            if (!scanInProgress)
            {
                InitializeScan();
            }

            int mapBudget = forceRefresh ? ForceRefreshMapBudget : MaxMapsPerPass;
            int pawnBudget = forceRefresh ? ForceRefreshPawnBudget : MaxPawnsPerPass;
            StepScan(mapBudget, pawnBudget);
            if (scanInProgress)
            {
                return;
            }

            FinalizeSnapshot(currentTick);
        }

        private bool ShouldScan(int currentTick, bool forceRefresh)
        {
            if (currentTick <= 0)
            {
                return false;
            }

            if (forceRefresh)
            {
                return true;
            }

            if (scanInProgress)
            {
                return true;
            }

            return currentTick - latestSnapshotTick >= SnapshotIntervalTicks;
        }

        private void InitializeScan()
        {
            pendingMapScans.Clear();
            pendingMapIndex = 0;
            scanInProgress = true;

            IList<Map> maps = Find.Maps;
            if (maps == null || maps.Count == 0)
            {
                scanInProgress = false;
                return;
            }

            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map == null || !map.IsPlayerHome || map.mapPawns?.AllPawnsSpawned == null)
                {
                    continue;
                }

                pendingMapScans.Add(new PendingMapScanState(map, map.mapPawns.AllPawnsSpawned));
            }

            if (pendingMapScans.Count == 0)
            {
                scanInProgress = false;
            }
        }

        private void StepScan(int maxMapsPerPass, int maxPawnsPerPass)
        {
            int mapBudget = maxMapsPerPass;
            int pawnBudget = maxPawnsPerPass;
            while (scanInProgress && mapBudget > 0 && pawnBudget > 0)
            {
                if (pendingMapIndex >= pendingMapScans.Count)
                {
                    scanInProgress = false;
                    break;
                }

                PendingMapScanState mapState = pendingMapScans[pendingMapIndex];
                bool completed = StepSingleMap(mapState, ref pawnBudget);
                if (completed)
                {
                    pendingMapIndex++;
                }

                mapBudget--;
            }
        }

        private static bool StepSingleMap(PendingMapScanState mapState, ref int pawnBudget)
        {
            if (mapState == null || mapState.Pawns == null)
            {
                return true;
            }

            while (mapState.Cursor < mapState.Pawns.Count && pawnBudget > 0)
            {
                Pawn pawn = mapState.Pawns[mapState.Cursor];
                mapState.Cursor++;
                pawnBudget--;
                CollectPawnSnapshot(mapState, pawn);
            }

            return mapState.Cursor >= mapState.Pawns.Count;
        }

        private static void CollectPawnSnapshot(PendingMapScanState mapState, Pawn pawn)
        {
            if (pawn == null || pawn.Dead)
            {
                return;
            }

            Faction faction = pawn.Faction;
            if (faction == null)
            {
                return;
            }

            if (faction == Faction.OfPlayer)
            {
                if (pawn.Downed)
                {
                    mapState.PlayerDownedCount++;
                }

                return;
            }

            if (faction.HostileTo(Faction.OfPlayer))
            {
                mapState.HostileFactions.Add(faction);
            }
        }

        private void FinalizeSnapshot(int currentTick)
        {
            var maps = new List<RaidThreatMapSnapshot>(pendingMapScans.Count);
            for (int i = 0; i < pendingMapScans.Count; i++)
            {
                PendingMapScanState pending = pendingMapScans[i];
                if (pending?.Map == null)
                {
                    continue;
                }

                maps.Add(new RaidThreatMapSnapshot(
                    pending.Map,
                    pending.PlayerDownedCount,
                    pending.GetHostilesAsList()));
            }

            latestSnapshot = new RaidThreatSnapshot(currentTick, maps);
            latestSnapshotTick = currentTick;
            pendingMapScans.Clear();
            pendingMapIndex = 0;
            scanInProgress = false;
        }

        private static int CountPlayerDownedFallback(Map map)
        {
            IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
            if (pawns == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn != null &&
                    pawn.Faction == Faction.OfPlayer &&
                    pawn.Downed &&
                    !pawn.Dead)
                {
                    count++;
                }
            }

            return count;
        }

        private sealed class PendingMapScanState
        {
            public PendingMapScanState(Map map, IReadOnlyList<Pawn> pawns)
            {
                Map = map;
                Pawns = pawns;
            }

            public Map Map { get; }
            public IReadOnlyList<Pawn> Pawns { get; }
            public int Cursor { get; set; }
            public int PlayerDownedCount { get; set; }
            public HashSet<Faction> HostileFactions { get; } = new HashSet<Faction>();

            public List<Faction> GetHostilesAsList()
            {
                var list = new List<Faction>(HostileFactions.Count);
                foreach (Faction hostileFaction in HostileFactions)
                {
                    if (hostileFaction != null)
                    {
                        list.Add(hostileFaction);
                    }
                }

                return list;
            }
        }
    }
}
