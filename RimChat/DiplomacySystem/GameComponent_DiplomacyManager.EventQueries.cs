using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimChat.DiplomacySystem
{
    public partial class GameComponent_DiplomacyManager
    {
        public bool HasCaravanDispatchedNow(Faction faction)
        {
            return faction != null &&
                   (HasPendingDelayedEvent(faction, IsCaravanEvent) || HasArrivedTradeCaravanOnPlayerMap(faction));
        }

        public bool HasRaidScheduledNow(Faction faction)
        {
            return faction != null &&
                   (HasPendingDelayedEvent(faction, IsRaidSchedulingEvent) || HasOngoingRaidOnPlayerMap(faction));
        }

        private bool HasPendingDelayedEvent(Faction faction, Func<DelayedDiplomacyEvent, bool> predicate)
        {
            if (faction == null || predicate == null)
            {
                return false;
            }

            return EnumerateDelayedEventsSnapshot().Any(evt =>
                evt != null &&
                evt.Faction == faction &&
                !evt.Faction.defeated &&
                predicate(evt));
        }

        private IEnumerable<DelayedDiplomacyEvent> EnumerateDelayedEventsSnapshot()
        {
            if (delayedEvents != null)
            {
                foreach (DelayedDiplomacyEvent evt in delayedEvents)
                {
                    yield return evt;
                }
            }

            if (delayedEventsPendingAdd == null)
            {
                yield break;
            }

            foreach (DelayedDiplomacyEvent evt in delayedEventsPendingAdd)
            {
                yield return evt;
            }
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
            IEnumerable<Map> maps = Find.Maps?.Where(map => map != null && map.IsPlayerHome);
            if (maps == null)
            {
                return false;
            }

            foreach (Map map in maps)
            {
                IEnumerable<Pawn> pawns = map.mapPawns?.AllPawnsSpawned;
                if (pawns == null)
                {
                    continue;
                }

                foreach (Pawn pawn in pawns)
                {
                    if (pawn == null || pawn.Dead || pawn.Faction != faction || pawn.Faction == Faction.OfPlayer)
                    {
                        continue;
                    }

                    if (IsTradeCaravanPawn(pawn))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsTradeCaravanPawn(Pawn pawn)
        {
            Lord lord = pawn.GetLord();
            string lordJobName = lord?.LordJob?.GetType().Name ?? string.Empty;
            if (lordJobName.IndexOf("TradeWithColony", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string dutyName = pawn.mindState?.duty?.def?.defName ?? string.Empty;
            return dutyName.IndexOf("TradeWithColony", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   dutyName.IndexOf("Trader", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasOngoingRaidOnPlayerMap(Faction faction)
        {
            IEnumerable<Map> maps = Find.Maps?.Where(map => map != null && map.IsPlayerHome);
            if (maps == null)
            {
                return false;
            }

            foreach (Map map in maps)
            {
                IEnumerable<Pawn> pawns = map.mapPawns?.AllPawnsSpawned;
                if (pawns == null)
                {
                    continue;
                }

                if (pawns.Any(pawn =>
                        pawn != null &&
                        !pawn.Dead &&
                        pawn.Faction == faction &&
                        faction.HostileTo(Faction.OfPlayer)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
