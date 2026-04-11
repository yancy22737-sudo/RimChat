using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimChat.Dialogue
{
    public sealed class DialogueLiveContext
    {
        public Pawn Initiator { get; set; }
        public Pawn Target { get; set; }
        public Pawn Negotiator { get; set; }
        public Faction Faction { get; set; }
        public Map Map { get; set; }
    }

    /// <summary>
    /// Resolves live objects from stable runtime ids.
    /// </summary>
    public static class DialogueContextResolver
    {
        public static bool TryResolveLiveContext(DialogueRuntimeContext runtimeContext, out DialogueLiveContext live, out string reason)
        {
            live = null;
            reason = string.Empty;
            if (runtimeContext == null)
            {
                reason = "runtime_context_null";
                return false;
            }

            live = new DialogueLiveContext();
            Faction faction = null;
            Pawn negotiator = null;
            Pawn initiator = null;
            Pawn target = null;
            Map map = null;

            if (runtimeContext.Channel == DialogueChannel.Diplomacy)
            {
                if (runtimeContext.FactionLoadId > 0)
                {
                    if (!TryResolveFaction(runtimeContext.FactionLoadId, out faction))
                    {
                        reason = "faction_unresolvable";
                        return false;
                    }
                }
                else
                {
                    Faction directFaction = DialogueRuntimeContext.GetDiplomacyFactionFromSessionId(runtimeContext.DialogueSessionId);
                    if (directFaction != null && !directFaction.defeated)
                    {
                        faction = directFaction;
                    }
                    else if (Find.FactionManager != null)
                    {
                        faction = Find.FactionManager.AllFactionsListForReading?
                            .FirstOrDefault(f => f != null && !f.defeated && f.loadID > 0);
                    }

                    if (faction == null)
                    {
                        reason = "faction_invalid";
                        return false;
                    }
                }

                live.Faction = faction;
                if (!string.IsNullOrWhiteSpace(runtimeContext.NegotiatorPawnId) &&
                    !TryResolvePawn(runtimeContext.NegotiatorPawnId, out negotiator))
                {
                    reason = "negotiator_unresolvable";
                    return false;
                }

                live.Negotiator = negotiator;
            }
            else
            {
                if (!TryResolvePawn(runtimeContext.InitiatorPawnId, out initiator))
                {
                    reason = "initiator_unresolvable";
                    return false;
                }

                if (!TryResolvePawn(runtimeContext.TargetPawnId, out target))
                {
                    reason = "target_unresolvable";
                    return false;
                }

                live.Initiator = initiator;
                live.Target = target;
            }

            if (runtimeContext.MapUniqueId > 0 && !TryResolveMap(runtimeContext.MapUniqueId, out map))
            {
                reason = "map_unresolvable";
                return false;
            }

            if (map == null)
            {
                map = live.Initiator?.Map ?? live.Target?.Map ?? live.Negotiator?.Map;
            }

            live.Map = map;
            return true;
        }

        public static bool TryResolvePawn(string uniqueLoadId, out Pawn pawn)
        {
            pawn = null;
            if (string.IsNullOrWhiteSpace(uniqueLoadId))
            {
                return false;
            }

            pawn = Find.WorldPawns?.AllPawnsAliveOrDead?
                .FirstOrDefault(candidate => IsPawnValid(candidate) &&
                    string.Equals(candidate.GetUniqueLoadID(), uniqueLoadId, StringComparison.Ordinal));
            if (pawn != null)
            {
                return true;
            }

            IEnumerable<Map> maps = Find.Maps ?? Enumerable.Empty<Map>();
            foreach (Map map in maps)
            {
                pawn = map?.mapPawns?.AllPawnsSpawned?
                    .FirstOrDefault(candidate => IsPawnValid(candidate) &&
                        string.Equals(candidate.GetUniqueLoadID(), uniqueLoadId, StringComparison.Ordinal));
                if (pawn != null)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryResolveFaction(int factionLoadId, out Faction faction)
        {
            faction = null;
            if (factionLoadId <= 0 || Find.FactionManager == null)
            {
                return false;
            }

            foreach (Faction candidate in Find.FactionManager.AllFactionsListForReading ?? new List<Faction>())
            {
                if (candidate != null && candidate.loadID == factionLoadId)
                {
                    faction = candidate;
                    return true;
                }
            }

            return false;
        }

        public static bool TryResolveMap(int mapUniqueId, out Map map)
        {
            map = null;
            if (mapUniqueId <= 0)
            {
                return false;
            }

            foreach (Map candidate in Find.Maps ?? Enumerable.Empty<Map>())
            {
                if (candidate != null && candidate.uniqueID == mapUniqueId)
                {
                    map = candidate;
                    return true;
                }
            }

            return false;
        }

        public static bool IsPawnValid(Pawn pawn)
        {
            return pawn != null && !pawn.Destroyed && !pawn.Dead && !pawn.Downed;
        }
    }
}
