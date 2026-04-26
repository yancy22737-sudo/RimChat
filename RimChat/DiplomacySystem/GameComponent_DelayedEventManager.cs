using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: DiplomacyManager delayedEvents list, RimWorld faction/map APIs.
    /// Responsibility: own tick cadence for delayed diplomacy events (raids, caravans, aid)
    /// and cached event queries, removing them from DiplomacyManager's per-tick path.
    /// </summary>
    public class GameComponent_DelayedEventManager : GameComponent
    {
        public static GameComponent_DelayedEventManager Instance;

        // Event query cache
        private readonly Dictionary<Faction, bool> cachedCaravanPresence = new Dictionary<Faction, bool>();
        private readonly Dictionary<Faction, bool> cachedRaidPresence = new Dictionary<Faction, bool>();
        private int eventQueryCacheTick = -1;
        private const int EventQueryCacheIntervalTicks = 2000;

        public GameComponent_DelayedEventManager(Game game)
        {
            Instance = this;
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            Instance = this;
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            Instance = this;
        }

        public override void GameComponentTick()
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick <= 0)
                return;

            if (currentTick % 250 == 0)
                GameComponent_DiplomacyManager.Instance?.ProcessDelayedEvents();
        }

        public bool HasCaravanDispatchedNow(Faction faction)
        {
            if (faction == null) return false;
            RefreshEventQueryCacheIfNeeded();
            if (cachedCaravanPresence.TryGetValue(faction, out bool cached))
                return cached;
            bool result = HasPendingDelayedEvent(faction, IsCaravanEvent) || HasArrivedTradeCaravanOnPlayerMap(faction);
            cachedCaravanPresence[faction] = result;
            return result;
        }

        public bool HasRaidScheduledNow(Faction faction)
        {
            if (faction == null) return false;
            RefreshEventQueryCacheIfNeeded();
            if (cachedRaidPresence.TryGetValue(faction, out bool cached))
                return cached;
            bool result = HasPendingDelayedEvent(faction, IsRaidSchedulingEvent) || HasOngoingRaidOnPlayerMap(faction);
            cachedRaidPresence[faction] = result;
            return result;
        }

        private void RefreshEventQueryCacheIfNeeded()
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick - eventQueryCacheTick < EventQueryCacheIntervalTicks && eventQueryCacheTick >= 0)
                return;
            eventQueryCacheTick = currentTick;
            cachedCaravanPresence.Clear();
            cachedRaidPresence.Clear();
        }

        private bool HasPendingDelayedEvent(Faction faction, Func<DelayedDiplomacyEvent, bool> predicate)
        {
            if (faction == null || predicate == null)
                return false;

            var dm = GameComponent_DiplomacyManager.Instance;
            if (dm == null) return false;

            var events = dm.GetDelayedEvents();
            if (events != null)
            {
                for (int i = 0; i < events.Count; i++)
                {
                    DelayedDiplomacyEvent evt = events[i];
                    if (evt != null && evt.Faction == faction && !evt.Faction.defeated && predicate(evt))
                        return true;
                }
            }

            return false;
        }

        private static bool IsCaravanEvent(DelayedDiplomacyEvent evt)
        {
            return evt.EventType == DelayedEventType.Caravan;
        }

        private static bool IsRaidSchedulingEvent(DelayedDiplomacyEvent evt)
        {
            DelayedEventType type = evt.EventType;
            return type == DelayedEventType.Raid ||
                   type == DelayedEventType.RaidWave ||
                   type == DelayedEventType.RaidCallEveryone ||
                   type == DelayedEventType.RaidCallEveryoneAnnounce;
        }

        private static bool HasArrivedTradeCaravanOnPlayerMap(Faction faction)
        {
            List<Map> maps = Find.Maps;
            if (maps == null) return false;

            for (int m = 0; m < maps.Count; m++)
            {
                Map map = maps[m];
                if (map == null || !map.IsPlayerHome) continue;
                IEnumerable<Pawn> pawns = map.mapPawns?.AllPawnsSpawned;
                if (pawns == null) continue;

                foreach (Pawn pawn in pawns)
                {
                    if (pawn == null || pawn.Dead || pawn.Faction != faction || pawn.Faction == Faction.OfPlayer)
                        continue;
                    if (IsTradeCaravanPawn(pawn))
                        return true;
                }
            }

            return false;
        }

        private static bool IsTradeCaravanPawn(Pawn pawn)
        {
            Lord lord = pawn.GetLord();
            string lordJobName = lord?.LordJob?.GetType().Name ?? string.Empty;
            if (lordJobName.IndexOf("TradeWithColony", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            string dutyName = pawn.mindState?.duty?.def?.defName ?? string.Empty;
            return dutyName.IndexOf("TradeWithColony", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   dutyName.IndexOf("Trader", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasOngoingRaidOnPlayerMap(Faction faction)
        {
            List<Map> maps = Find.Maps;
            if (maps == null) return false;

            for (int m = 0; m < maps.Count; m++)
            {
                Map map = maps[m];
                if (map == null || !map.IsPlayerHome) continue;
                IEnumerable<Pawn> pawns = map.mapPawns?.AllPawnsSpawned;
                if (pawns == null) continue;

                foreach (Pawn pawn in pawns)
                {
                    if (pawn != null && !pawn.Dead && pawn.Faction == faction && faction.HostileTo(Faction.OfPlayer))
                        return true;
                }
            }

            return false;
        }
    }
}
