using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.DiplomacySystem;
using RimWorld;
using Verse;

namespace RimChat.Memory
{
    /// <summary>
    /// Dependencies: diplomacy runtime sessions, leader-memory cache, and dialogue-history persistence.
    /// Responsibility: provide session-grouped diplomacy history with two-way sync between live sessions and persistent dialogue history.
    /// </summary>
    public partial class LeaderMemoryManager
    {
        private const int DialogueHistorySessionSplitTicks = 2500 * 3;

        public sealed class DiplomacyHistoryRow
        {
            public string FactionId { get; set; } = string.Empty;
            public string FactionName { get; set; } = string.Empty;
            public bool IsPlayer { get; set; }
            public int GameTick { get; set; }
            public string Message { get; set; } = string.Empty;
            public bool IsCurrentSession { get; set; }
            public int SessionOrdinal { get; set; } = -1;
            public int SessionRowOrdinal { get; set; } = -1;
            public int LiveMessageIndex { get; set; } = -1;
            public int HistoryRecordIndex { get; set; } = -1;
            public string SenderLabel { get; set; } = string.Empty;
        }

        public sealed class DiplomacyHistorySessionGroup
        {
            public bool IsCurrentSession { get; set; }
            public int SessionOrdinal { get; set; }
            public int StartTick { get; set; }
            public int EndTick { get; set; }
            public List<DiplomacyHistoryRow> Rows { get; set; } = new List<DiplomacyHistoryRow>();
        }

        public List<DiplomacyHistorySessionGroup> GetDialogueHistorySessionGroups(Faction faction)
        {
            if (faction == null)
            {
                return new List<DiplomacyHistorySessionGroup>();
            }

            EnsureCacheLoaded();
            string factionId = GetUniqueFactionId(faction);
            string factionName = faction.Name ?? factionId;
            FactionDialogueSession session = GameComponent_DiplomacyManager.Instance?.GetSession(faction);
            FactionLeaderMemory memory = GetMemory(faction);

            List<DiplomacyHistoryRow> currentRows = BuildCurrentSessionRows(session, memory, factionId, factionName);
            List<DiplomacyHistoryRow> historyRows = BuildPersistentHistoryRows(memory, factionId, factionName, currentRows);
            List<DiplomacyHistorySessionGroup> groups = BuildHistoricalGroups(historyRows);
            if (currentRows.Count > 0)
            {
                groups.Insert(0, BuildCurrentSessionGroup(currentRows));
            }

            return groups;
        }

        public bool TryUpdateDialogueHistoryRow(Faction faction, DiplomacyHistoryRow row, string newMessage, out string error)
        {
            error = string.Empty;
            if (!TryValidateHistoryRowMutation(faction, row, newMessage, requireNonEmptyMessage: true, out error))
            {
                return false;
            }

            string trimmed = newMessage.Trim();
            bool changed = false;
            bool affectsCurrentSession = false;
            bool affectsPersistentHistory = false;
            FactionDialogueSession session = GameComponent_DiplomacyManager.Instance?.GetSession(faction);
            if (TryResolveLiveMessage(session, row.LiveMessageIndex, out DialogueMessageData liveMessage))
            {
                liveMessage.message = trimmed;
                changed = true;
                affectsCurrentSession = true;
            }

            FactionLeaderMemory memory = GetMemory(faction);
            if (TryResolveDialogueRecord(memory, row.HistoryRecordIndex, out DialogueRecord record, out _))
            {
                record.Message = trimmed;
                changed = true;
                affectsPersistentHistory = true;
            }

            if (!changed)
            {
                error = "history_record_missing";
                return false;
            }

            NormalizeAndPersistDialogueHistory(faction, memory);
            PublishDiplomacyMemoryChanged(
                faction,
                affectsCurrentSession,
                affectsPersistentHistory,
                affectsAiPrompt: true);
            return true;
        }

        public bool TryDeleteDialogueHistoryRow(Faction faction, DiplomacyHistoryRow row, out string error)
        {
            error = string.Empty;
            if (!TryValidateHistoryRowMutation(faction, row, string.Empty, requireNonEmptyMessage: false, out error))
            {
                return false;
            }

            bool changed = false;
            bool affectsCurrentSession = false;
            bool affectsPersistentHistory = false;
            FactionDialogueSession session = GameComponent_DiplomacyManager.Instance?.GetSession(faction);
            if (TryRemoveLiveMessage(session, row.LiveMessageIndex))
            {
                changed = true;
                affectsCurrentSession = true;
            }

            FactionLeaderMemory memory = GetMemory(faction);
            if (TryRemoveDialogueRecord(memory, row.HistoryRecordIndex))
            {
                changed = true;
                affectsPersistentHistory = true;
            }

            if (!changed)
            {
                error = "history_record_missing";
                return false;
            }

            NormalizeAndPersistDialogueHistory(faction, memory);
            PublishDiplomacyMemoryChanged(
                faction,
                affectsCurrentSession,
                affectsPersistentHistory,
                affectsAiPrompt: true);
            return true;
        }

        private static DiplomacyHistorySessionGroup BuildCurrentSessionGroup(List<DiplomacyHistoryRow> currentRows)
        {
            return new DiplomacyHistorySessionGroup
            {
                IsCurrentSession = true,
                SessionOrdinal = 0,
                StartTick = currentRows.Min(row => row.GameTick),
                EndTick = currentRows.Max(row => row.GameTick),
                Rows = currentRows
            };
        }

        private List<DiplomacyHistoryRow> BuildCurrentSessionRows(
            FactionDialogueSession session,
            FactionLeaderMemory memory,
            string factionId,
            string factionName)
        {
            var rows = new List<DiplomacyHistoryRow>();
            List<DialogueMessageData> messages = session?.messages ?? new List<DialogueMessageData>();
            List<DialogueRecord> history = memory?.DialogueHistory ?? new List<DialogueRecord>();
            var consumedHistoryIndexes = new HashSet<int>();

            for (int i = 0; i < messages.Count; i++)
            {
                DialogueMessageData message = messages[i];
                if (message == null || string.IsNullOrWhiteSpace(message.message))
                {
                    continue;
                }

                int historyIndex = FindMatchingHistoryRecordIndex(history, message, consumedHistoryIndexes);
                if (historyIndex >= 0)
                {
                    consumedHistoryIndexes.Add(historyIndex);
                }

                rows.Add(new DiplomacyHistoryRow
                {
                    FactionId = factionId,
                    FactionName = factionName,
                    IsPlayer = message.isPlayer,
                    GameTick = message.GetGameTick(),
                    Message = message.message,
                    IsCurrentSession = true,
                    SessionOrdinal = 0,
                    SessionRowOrdinal = rows.Count,
                    LiveMessageIndex = i,
                    HistoryRecordIndex = historyIndex,
                    SenderLabel = message.sender ?? string.Empty
                });
            }

            return rows;
        }

        private List<DiplomacyHistoryRow> BuildPersistentHistoryRows(
            FactionLeaderMemory memory,
            string factionId,
            string factionName,
            List<DiplomacyHistoryRow> currentRows)
        {
            var rows = new List<DiplomacyHistoryRow>();
            List<DialogueRecord> history = memory?.DialogueHistory ?? new List<DialogueRecord>();
            Dictionary<string, int> liveSignatureCounts = BuildLiveSignatureCounts(currentRows);

            for (int i = 0; i < history.Count; i++)
            {
                DialogueRecord record = history[i];
                if (record == null || string.IsNullOrWhiteSpace(record.Message))
                {
                    continue;
                }

                string signature = BuildHistorySignature(record.GameTick, record.IsPlayer, record.Message);
                if (liveSignatureCounts.TryGetValue(signature, out int remaining) && remaining > 0)
                {
                    liveSignatureCounts[signature] = remaining - 1;
                    continue;
                }

                rows.Add(new DiplomacyHistoryRow
                {
                    FactionId = factionId,
                    FactionName = factionName,
                    IsPlayer = record.IsPlayer,
                    GameTick = record.GameTick,
                    Message = record.Message,
                    IsCurrentSession = false,
                    HistoryRecordIndex = i
                });
            }

            rows.Sort((a, b) => a.GameTick.CompareTo(b.GameTick));
            return rows;
        }

        private static Dictionary<string, int> BuildLiveSignatureCounts(List<DiplomacyHistoryRow> currentRows)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (DiplomacyHistoryRow row in currentRows ?? new List<DiplomacyHistoryRow>())
            {
                string signature = BuildHistorySignature(row.GameTick, row.IsPlayer, row.Message);
                counts.TryGetValue(signature, out int existing);
                counts[signature] = existing + 1;
            }

            return counts;
        }

        private List<DiplomacyHistorySessionGroup> BuildHistoricalGroups(List<DiplomacyHistoryRow> rows)
        {
            var groupsAscending = new List<DiplomacyHistorySessionGroup>();
            DiplomacyHistorySessionGroup current = null;

            foreach (DiplomacyHistoryRow row in rows ?? new List<DiplomacyHistoryRow>())
            {
                if (current == null || ShouldSplitHistorySession(current, row))
                {
                    current = new DiplomacyHistorySessionGroup
                    {
                        IsCurrentSession = false,
                        StartTick = row.GameTick,
                        EndTick = row.GameTick
                    };
                    groupsAscending.Add(current);
                }

                current.Rows.Add(row);
                current.EndTick = Math.Max(current.EndTick, row.GameTick);
            }

            groupsAscending.Reverse();
            for (int i = 0; i < groupsAscending.Count; i++)
            {
                DiplomacyHistorySessionGroup group = groupsAscending[i];
                group.SessionOrdinal = i + 1;
                for (int rowIndex = 0; rowIndex < group.Rows.Count; rowIndex++)
                {
                    group.Rows[rowIndex].SessionOrdinal = group.SessionOrdinal;
                    group.Rows[rowIndex].SessionRowOrdinal = rowIndex;
                }
            }

            return groupsAscending;
        }

        private static bool ShouldSplitHistorySession(DiplomacyHistorySessionGroup current, DiplomacyHistoryRow incoming)
        {
            if (current == null || incoming == null || current.Rows.Count == 0)
            {
                return true;
            }

            DiplomacyHistoryRow last = current.Rows[current.Rows.Count - 1];
            return incoming.GameTick - last.GameTick > DialogueHistorySessionSplitTicks;
        }

        private static int FindMatchingHistoryRecordIndex(
            List<DialogueRecord> history,
            DialogueMessageData message,
            HashSet<int> consumedIndexes)
        {
            if (history == null || message == null)
            {
                return -1;
            }

            string normalized = NormalizeMessage(message.message);
            for (int i = 0; i < history.Count; i++)
            {
                if (consumedIndexes.Contains(i))
                {
                    continue;
                }

                DialogueRecord record = history[i];
                if (record == null)
                {
                    continue;
                }

                if (record.GameTick == message.GetGameTick() &&
                    record.IsPlayer == message.isPlayer &&
                    string.Equals(NormalizeMessage(record.Message), normalized, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string BuildHistorySignature(int tick, bool isPlayer, string message)
        {
            return $"{tick}|{isPlayer}|{NormalizeMessage(message)}";
        }

        private static string NormalizeMessage(string message)
        {
            return (message ?? string.Empty).Trim();
        }

        private static bool TryResolveLiveMessage(FactionDialogueSession session, int liveMessageIndex, out DialogueMessageData message)
        {
            message = null;
            List<DialogueMessageData> messages = session?.messages;
            if (messages == null || liveMessageIndex < 0 || liveMessageIndex >= messages.Count)
            {
                return false;
            }

            message = messages[liveMessageIndex];
            return message != null;
        }

        private static bool TryRemoveLiveMessage(FactionDialogueSession session, int liveMessageIndex)
        {
            List<DialogueMessageData> messages = session?.messages;
            if (messages == null || liveMessageIndex < 0 || liveMessageIndex >= messages.Count)
            {
                return false;
            }

            messages.RemoveAt(liveMessageIndex);
            return true;
        }

        private static bool TryRemoveDialogueRecord(FactionLeaderMemory memory, int recordIndex)
        {
            List<DialogueRecord> history = memory?.DialogueHistory;
            if (history == null || recordIndex < 0 || recordIndex >= history.Count)
            {
                return false;
            }

            history.RemoveAt(recordIndex);
            return true;
        }

        private bool TryValidateHistoryRowMutation(Faction faction, DiplomacyHistoryRow row, string message, bool requireNonEmptyMessage, out string error)
        {
            error = string.Empty;
            if (faction == null)
            {
                error = "history_faction_missing";
                return false;
            }

            if (row == null)
            {
                error = "history_record_missing";
                return false;
            }

            if (requireNonEmptyMessage && string.IsNullOrWhiteSpace(message))
            {
                error = "history_message_empty";
                return false;
            }

            if (row.LiveMessageIndex < 0 && row.HistoryRecordIndex < 0)
            {
                error = "history_record_missing";
                return false;
            }

            return true;
        }

        private static bool TryResolveDialogueRecord(FactionLeaderMemory memory, int recordIndex, out DialogueRecord record, out string error)
        {
            record = null;
            error = string.Empty;
            List<DialogueRecord> history = memory?.DialogueHistory;
            if (history == null || recordIndex < 0 || recordIndex >= history.Count)
            {
                error = "history_record_missing";
                return false;
            }

            record = history[recordIndex];
            if (record == null)
            {
                error = "history_record_missing";
                return false;
            }

            return true;
        }

        private void NormalizeAndPersistDialogueHistory(Faction faction, FactionLeaderMemory memory)
        {
            if (faction == null || memory == null)
            {
                return;
            }

            NormalizeMemoryData(memory);
            memory.LastUpdatedTick = Find.TickManager?.TicksGame ?? memory.LastUpdatedTick;
            SaveMemory(faction);
        }
    }
}
