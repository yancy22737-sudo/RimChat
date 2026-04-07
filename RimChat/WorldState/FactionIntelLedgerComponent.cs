using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimChat.WorldState
{
    /// <summary>/// Dependencies: Verse.GameComponent, map pawn state, and world object lifecycle.
 /// Responsibility: persist faction settlement destruction history and raid damage intel for fixed prompt injection.
 ///</summary>
    public class FactionIntelLedgerComponent : GameComponent
    {
        private const int RaidScanInterval = 250;
        private const int BuildingDedupeLimit = 4096;

        public class OngoingRaidIntelState : IExposable
        {
            public int StartTick;
            public int LastUpdatedTick;
            public int MapId;
            public string MapLabel;
            public string AttackerFactionId;
            public string AttackerFactionName;
            public int AttackerDeaths;
            public int PlayerDeaths;
            public int PlayerDownedPeak;
            public int PlayerBuildingsDestroyed;

            public OngoingRaidIntelState()
            {
                MapLabel = string.Empty;
                AttackerFactionId = string.Empty;
                AttackerFactionName = string.Empty;
            }

            public void ExposeData()
            {
                Scribe_Values.Look(ref StartTick, "startTick", 0);
                Scribe_Values.Look(ref LastUpdatedTick, "lastUpdatedTick", 0);
                Scribe_Values.Look(ref MapId, "mapId", -1);
                Scribe_Values.Look(ref MapLabel, "mapLabel", string.Empty);
                Scribe_Values.Look(ref AttackerFactionId, "attackerFactionId", string.Empty);
                Scribe_Values.Look(ref AttackerFactionName, "attackerFactionName", string.Empty);
                Scribe_Values.Look(ref AttackerDeaths, "attackerDeaths", 0);
                Scribe_Values.Look(ref PlayerDeaths, "playerDeaths", 0);
                Scribe_Values.Look(ref PlayerDownedPeak, "playerDownedPeak", 0);
                Scribe_Values.Look(ref PlayerBuildingsDestroyed, "playerBuildingsDestroyed", 0);
            }
        }

        public static FactionIntelLedgerComponent Instance => Current.Game?.GetComponent<FactionIntelLedgerComponent>();

        private List<OngoingRaidIntelState> ongoingRaidStates = new List<OngoingRaidIntelState>();
        private List<FactionRaidDamageRecord> raidDamageRecords = new List<FactionRaidDamageRecord>();
        private List<FactionSettlementDestructionRecord> settlementDestructionRecords = new List<FactionSettlementDestructionRecord>();
        private readonly HashSet<int> processedDestroyedBuildingThingIds = new HashSet<int>();
        private int lastRaidScanTick = -RaidScanInterval;

        public FactionIntelLedgerComponent(Game game) : base()
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref ongoingRaidStates, "ongoingRaidStates", LookMode.Deep);
            Scribe_Collections.Look(ref raidDamageRecords, "raidDamageRecords", LookMode.Deep);
            Scribe_Collections.Look(ref settlementDestructionRecords, "settlementDestructionRecords", LookMode.Deep);
            Scribe_Values.Look(ref lastRaidScanTick, "lastRaidScanTick", -RaidScanInterval);
            if (Scribe.mode != LoadSaveMode.PostLoadInit)
            {
                return;
            }

            ongoingRaidStates ??= new List<OngoingRaidIntelState>();
            raidDamageRecords ??= new List<FactionRaidDamageRecord>();
            settlementDestructionRecords ??= new List<FactionSettlementDestructionRecord>();
            CleanupLoadedData();
            processedDestroyedBuildingThingIds.Clear();
            RimChatTrackedEntityRegistry.PrimeFromCurrentGame();
        }

        public override void GameComponentTick()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.TickManager == null)
            {
                return;
            }

            int tick = Find.TickManager.TicksGame;
            if (tick - lastRaidScanTick < RaidScanInterval)
            {
                return;
            }

            lastRaidScanTick = tick;
            UpdateRaidStates(tick);
        }

        public void NotifyPawnKilled(Pawn victim, DamageInfo? dinfo)
        {
            Map map = victim?.MapHeld;
            if (map == null || !map.IsPlayerHome || victim?.Faction == null)
            {
                return;
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            Faction attackerByDamage = dinfo?.Instigator?.Faction;
            OngoingRaidIntelState state = ResolveStateForPawnKill(map, victim.Faction, attackerByDamage, tick);
            if (state == null)
            {
                return;
            }

            if (victim.Faction == Faction.OfPlayer)
            {
                state.PlayerDeaths++;
            }
            else if (string.Equals(GetFactionId(victim.Faction), state.AttackerFactionId, StringComparison.Ordinal))
            {
                state.AttackerDeaths++;
            }

            state.LastUpdatedTick = tick;
        }

        public void NotifyBuildingDestroyed(Thing building, DamageInfo? dinfo)
        {
            if (!IsPlayerBuildingLoss(building))
            {
                return;
            }

            if (!processedDestroyedBuildingThingIds.Add(building.thingIDNumber))
            {
                return;
            }

            if (processedDestroyedBuildingThingIds.Count > BuildingDedupeLimit)
            {
                processedDestroyedBuildingThingIds.Clear();
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            Map map = building.MapHeld;
            Faction attackerFaction = dinfo?.Instigator?.Faction;
            OngoingRaidIntelState state = ResolveStateForBuildingLoss(map, attackerFaction, tick);
            if (state == null)
            {
                return;
            }

            state.PlayerBuildingsDestroyed++;
            state.LastUpdatedTick = tick;
        }

        public void RecordSettlementDestroyed(WorldObject worldObject)
        {
            if (!IsFactionSettlementLike(worldObject))
            {
                return;
            }

            Faction ownerFaction = worldObject.Faction;
            if (ownerFaction == null || ownerFaction.IsPlayer)
            {
                return;
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            ResolveSettlementDestroyer(worldObject, ownerFaction, out string destroyerId, out string destroyerName);
            var record = new FactionSettlementDestructionRecord
            {
                OccurredTick = tick,
                OwnerFactionId = GetFactionId(ownerFaction),
                OwnerFactionName = ownerFaction.Name ?? string.Empty,
                SettlementLabel = worldObject.LabelCap ?? worldObject.def?.label ?? "UnknownSettlement",
                Tile = worldObject.Tile,
                DestroyedByFactionId = destroyerId,
                DestroyedByFactionName = destroyerName
            };

            settlementDestructionRecords.Add(record);
        }

        public List<FactionSettlementDestructionRecord> GetSettlementDestructionRecords(Faction ownerFaction)
        {
            string ownerId = GetFactionId(ownerFaction);
            return settlementDestructionRecords
                .Where(record => record != null && string.Equals(record.OwnerFactionId, ownerId, StringComparison.Ordinal))
                .OrderBy(record => record.OccurredTick)
                .ToList();
        }

        public List<FactionRaidDamageRecord> GetRaidDamageRecordsForAttacker(Faction attackerFaction)
        {
            string attackerId = GetFactionId(attackerFaction);
            return raidDamageRecords
                .Where(record => record != null && string.Equals(record.AttackerFactionId, attackerId, StringComparison.Ordinal))
                .OrderByDescending(record => record.BattleEndTick)
                .ToList();
        }

        private void UpdateRaidStates(int tick)
        {
            List<Map> homeMaps = Find.Maps?.Where(map => map != null && map.IsPlayerHome).ToList();
            if (homeMaps == null || homeMaps.Count == 0)
            {
                return;
            }

            var activeKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < homeMaps.Count; i++)
            {
                Map map = homeMaps[i];
                List<Faction> hostileFactions = GetActiveHostileFactions(map);
                for (int j = 0; j < hostileFactions.Count; j++)
                {
                    Faction attacker = hostileFactions[j];
                    OngoingRaidIntelState state = GetOrCreateState(map, attacker, tick);
                    state.PlayerDownedPeak = Math.Max(state.PlayerDownedPeak, CountPlayerDowned(map));
                    state.LastUpdatedTick = tick;
                    activeKeys.Add(GetStateKey(map.uniqueID, state.AttackerFactionId));
                }
            }

            FinalizeEndedStates(activeKeys, tick);
        }

        private void FinalizeEndedStates(HashSet<string> activeKeys, int tick)
        {
            if (ongoingRaidStates == null || ongoingRaidStates.Count == 0)
            {
                return;
            }

            for (int i = ongoingRaidStates.Count - 1; i >= 0; i--)
            {
                OngoingRaidIntelState state = ongoingRaidStates[i];
                if (state == null)
                {
                    ongoingRaidStates.RemoveAt(i);
                    continue;
                }

                string key = GetStateKey(state.MapId, state.AttackerFactionId);
                if (activeKeys.Contains(key))
                {
                    continue;
                }

                AddRaidDamageRecord(state, tick);
                ongoingRaidStates.RemoveAt(i);
            }
        }

        private void AddRaidDamageRecord(OngoingRaidIntelState state, int battleEndTick)
        {
            var record = new FactionRaidDamageRecord
            {
                BattleStartTick = state.StartTick,
                BattleEndTick = battleEndTick,
                MapLabel = state.MapLabel,
                AttackerFactionId = state.AttackerFactionId,
                AttackerFactionName = state.AttackerFactionName,
                PlayerDeaths = state.PlayerDeaths,
                PlayerDownedPeak = state.PlayerDownedPeak,
                PlayerBuildingsDestroyed = state.PlayerBuildingsDestroyed,
                AttackerDeaths = state.AttackerDeaths
            };

            raidDamageRecords.Add(record);
        }

        private OngoingRaidIntelState ResolveStateForPawnKill(Map map, Faction victimFaction, Faction attackerByDamage, int tick)
        {
            if (victimFaction == Faction.OfPlayer)
            {
                OngoingRaidIntelState byAttacker = GetState(map.uniqueID, attackerByDamage);
                if (byAttacker != null)
                {
                    return byAttacker;
                }

                return GetLatestStateOnMap(map.uniqueID);
            }

            OngoingRaidIntelState byVictimFaction = GetState(map.uniqueID, victimFaction);
            if (byVictimFaction != null)
            {
                return byVictimFaction;
            }

            return victimFaction.HostileTo(Faction.OfPlayer)
                ? GetOrCreateState(map, victimFaction, tick)
                : null;
        }

        private OngoingRaidIntelState ResolveStateForBuildingLoss(Map map, Faction attackerFaction, int tick)
        {
            if (map == null)
            {
                return null;
            }

            OngoingRaidIntelState byAttacker = GetState(map.uniqueID, attackerFaction);
            if (byAttacker != null)
            {
                return byAttacker;
            }

            if (attackerFaction != null && attackerFaction.HostileTo(Faction.OfPlayer))
            {
                return GetOrCreateState(map, attackerFaction, tick);
            }

            return GetLatestStateOnMap(map.uniqueID);
        }

        private OngoingRaidIntelState GetState(int mapId, Faction attackerFaction)
        {
            if (attackerFaction == null)
            {
                return null;
            }

            string attackerId = GetFactionId(attackerFaction);
            return ongoingRaidStates?.FirstOrDefault(state =>
                state != null &&
                state.MapId == mapId &&
                string.Equals(state.AttackerFactionId, attackerId, StringComparison.Ordinal));
        }

        private OngoingRaidIntelState GetLatestStateOnMap(int mapId)
        {
            return ongoingRaidStates?
                .Where(state => state != null && state.MapId == mapId)
                .OrderByDescending(state => state.LastUpdatedTick)
                .FirstOrDefault();
        }

        private OngoingRaidIntelState GetOrCreateState(Map map, Faction attackerFaction, int tick)
        {
            OngoingRaidIntelState existing = GetState(map.uniqueID, attackerFaction);
            if (existing != null)
            {
                return existing;
            }

            var state = new OngoingRaidIntelState
            {
                StartTick = tick,
                LastUpdatedTick = tick,
                MapId = map.uniqueID,
                MapLabel = map.Parent?.LabelCap ?? map.Biome?.LabelCap ?? $"Map#{map.uniqueID}",
                AttackerFactionId = GetFactionId(attackerFaction),
                AttackerFactionName = attackerFaction?.Name ?? "UnknownFaction",
                AttackerDeaths = 0,
                PlayerDeaths = 0,
                PlayerDownedPeak = CountPlayerDowned(map),
                PlayerBuildingsDestroyed = 0
            };

            ongoingRaidStates.Add(state);
            return state;
        }

        private static List<Faction> GetActiveHostileFactions(Map map)
        {
            IEnumerable<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
            if (pawns == null)
            {
                return new List<Faction>();
            }

            return pawns
                .Where(pawn => pawn != null && !pawn.Dead && pawn.Faction != null && pawn.Faction != Faction.OfPlayer)
                .Where(pawn => pawn.Faction.HostileTo(Faction.OfPlayer))
                .Select(pawn => pawn.Faction)
                .Distinct()
                .ToList();
        }

        private static int CountPlayerDowned(Map map)
        {
            IEnumerable<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
            if (pawns == null)
            {
                return 0;
            }

            return pawns.Count(pawn =>
                pawn != null &&
                pawn.Faction == Faction.OfPlayer &&
                pawn.Downed &&
                !pawn.Dead);
        }

        private static bool IsPlayerBuildingLoss(Thing building)
        {
            Map map = building?.MapHeld;
            if (building == null || map == null || !map.IsPlayerHome)
            {
                return false;
            }

            if (building is Pawn)
            {
                return false;
            }

            if (building.def?.category != ThingCategory.Building)
            {
                return false;
            }

            Faction owner = building.Faction;
            return owner == Faction.OfPlayer || owner?.IsPlayer == true;
        }

        private static bool IsFactionSettlementLike(WorldObject worldObject)
        {
            if (worldObject == null || worldObject.Destroyed)
            {
                return false;
            }

            if (worldObject is Settlement)
            {
                return true;
            }

            if (!(worldObject is MapParent mapParent))
            {
                return false;
            }

            return mapParent.Faction != null && !mapParent.Faction.IsPlayer;
        }

        private static void ResolveSettlementDestroyer(
            WorldObject worldObject,
            Faction ownerFaction,
            out string destroyerId,
            out string destroyerName)
        {
            destroyerId = "unknown";
            destroyerName = "Unknown";
            if (worldObject == null)
            {
                return;
            }

            Caravan playerCaravan = Find.WorldObjects?.Caravans?
                .FirstOrDefault(caravan =>
                    caravan != null &&
                    caravan.Tile == worldObject.Tile &&
                    caravan.Faction == Faction.OfPlayer);
            if (playerCaravan != null)
            {
                destroyerId = GetFactionId(Faction.OfPlayer);
                destroyerName = Faction.OfPlayer?.Name ?? "PlayerFaction";
                return;
            }

            Caravan anyCaravan = Find.WorldObjects?.Caravans?
                .FirstOrDefault(caravan =>
                    caravan != null &&
                    caravan.Tile == worldObject.Tile &&
                    caravan.Faction != null &&
                    caravan.Faction != ownerFaction);
            if (anyCaravan?.Faction != null)
            {
                destroyerId = GetFactionId(anyCaravan.Faction);
                destroyerName = anyCaravan.Faction.Name ?? "Unknown";
            }
        }

        private static string GetFactionId(Faction faction)
        {
            return faction?.GetUniqueLoadID() ?? string.Empty;
        }

        private static string GetStateKey(int mapId, string attackerFactionId)
        {
            return $"{mapId}|{attackerFactionId ?? string.Empty}";
        }

        private void CleanupLoadedData()
        {
            ongoingRaidStates = (ongoingRaidStates ?? new List<OngoingRaidIntelState>())
                .Where(state => state != null && state.MapId >= 0 && !string.IsNullOrWhiteSpace(state.AttackerFactionId))
                .ToList();

            raidDamageRecords = (raidDamageRecords ?? new List<FactionRaidDamageRecord>())
                .Where(record => record != null && !string.IsNullOrWhiteSpace(record.AttackerFactionId))
                .ToList();

            settlementDestructionRecords = (settlementDestructionRecords ?? new List<FactionSettlementDestructionRecord>())
                .Where(record => record != null && !string.IsNullOrWhiteSpace(record.OwnerFactionId))
                .ToList();
        }
    }
}
