using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimDiplomacy.Memory
{
    /// <summary>
    /// Dependencies: RimWorld.Pawn/Faction, Verse.TickManager.
    /// Responsibility: keep a lightweight rolling trace of recent RPG dialogue turns per NPC pawn.
    /// </summary>
    public static class RpgDialogueTraceTracker
    {
        public const int MaxTurnsPerTrace = 12;
        public const int RecentTraceWindowTicks = 90000;

        private sealed class TraceEntry
        {
            public Pawn Pawn;
            public Pawn Initiator;
            public Faction Faction;
            public List<RpgDialogueTurn> Turns = new List<RpgDialogueTurn>();
            public int LastInteractionTick;
            public int LastConsumedExitTick;
        }

        private static readonly Dictionary<int, TraceEntry> traces = new Dictionary<int, TraceEntry>();
        private static readonly object syncRoot = new object();

        public static void RegisterTurn(Pawn initiator, Pawn targetNpc, bool isPlayerSpeaker, string text)
        {
            if (targetNpc == null || targetNpc.Dead || targetNpc.Destroyed || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            int key = targetNpc.thingIDNumber;
            string normalized = text.Trim();

            lock (syncRoot)
            {
                CleanupStaleEntries(tick);

                if (!traces.TryGetValue(key, out TraceEntry entry) || entry == null)
                {
                    entry = new TraceEntry();
                    traces[key] = entry;
                }

                entry.Pawn = targetNpc;
                entry.Initiator = initiator;
                entry.Faction = targetNpc.Faction;
                entry.LastInteractionTick = tick;
                entry.Turns.Add(new RpgDialogueTurn
                {
                    IsPlayer = isPlayerSpeaker,
                    Text = normalized,
                    GameTick = tick
                });

                if (entry.Turns.Count > MaxTurnsPerTrace)
                {
                    entry.Turns.RemoveRange(0, entry.Turns.Count - MaxTurnsPerTrace);
                }
            }
        }

        public static bool TryConsumeRecentForExit(Pawn pawn, out RpgDialogueTraceSnapshot snapshot)
        {
            snapshot = null;
            if (pawn == null)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            int key = pawn.thingIDNumber;
            lock (syncRoot)
            {
                CleanupStaleEntries(currentTick);

                if (!traces.TryGetValue(key, out TraceEntry entry) || entry == null)
                {
                    return false;
                }

                if (!IsEntryEligible(entry, currentTick))
                {
                    return false;
                }

                if (entry.LastConsumedExitTick == currentTick)
                {
                    return false;
                }

                entry.LastConsumedExitTick = currentTick;
                snapshot = BuildSnapshot(entry);
                return snapshot != null;
            }
        }

        private static bool IsEntryEligible(TraceEntry entry, int currentTick)
        {
            if (entry.Pawn == null || entry.Pawn.Dead || entry.Pawn.Destroyed || entry.Faction == null)
            {
                return false;
            }

            if (entry.Faction.IsPlayer || entry.Faction.defeated)
            {
                return false;
            }

            if (entry.Turns == null || entry.Turns.Count == 0)
            {
                return false;
            }

            return entry.LastInteractionTick > 0 && currentTick - entry.LastInteractionTick <= RecentTraceWindowTicks;
        }

        private static RpgDialogueTraceSnapshot BuildSnapshot(TraceEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            return new RpgDialogueTraceSnapshot
            {
                Pawn = entry.Pawn,
                Initiator = entry.Initiator,
                Faction = entry.Faction,
                LastInteractionTick = entry.LastInteractionTick,
                Turns = entry.Turns.Select(t => t.Clone()).ToList()
            };
        }

        private static void CleanupStaleEntries(int currentTick)
        {
            if (traces.Count == 0)
            {
                return;
            }

            var toRemove = new List<int>();
            foreach (KeyValuePair<int, TraceEntry> pair in traces)
            {
                TraceEntry entry = pair.Value;
                if (entry == null || entry.Pawn == null || entry.Pawn.Dead || entry.Pawn.Destroyed)
                {
                    toRemove.Add(pair.Key);
                    continue;
                }

                if (entry.LastInteractionTick <= 0 || currentTick - entry.LastInteractionTick > RecentTraceWindowTicks * 2)
                {
                    toRemove.Add(pair.Key);
                }
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                traces.Remove(toRemove[i]);
            }
        }
    }

    public sealed class RpgDialogueTurn
    {
        public bool IsPlayer;
        public string Text = string.Empty;
        public int GameTick;

        public RpgDialogueTurn Clone()
        {
            return new RpgDialogueTurn
            {
                IsPlayer = IsPlayer,
                Text = Text ?? string.Empty,
                GameTick = GameTick
            };
        }
    }

    public sealed class RpgDialogueTraceSnapshot
    {
        public Pawn Pawn;
        public Pawn Initiator;
        public Faction Faction;
        public int LastInteractionTick;
        public List<RpgDialogueTurn> Turns = new List<RpgDialogueTurn>();
    }
}
