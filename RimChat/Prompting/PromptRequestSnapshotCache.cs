using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Config;

namespace RimChat.Prompting
{
    internal sealed class PromptRequestSnapshotRecord
    {
        public string PromptChannel { get; set; } = string.Empty;
        public Dictionary<string, object> Values { get; set; } = new Dictionary<string, object>();
        public DateTime RecordedAtUtc { get; set; } = DateTime.MinValue;
        public string ScenarioSignature { get; set; } = string.Empty;
    }

    internal static class PromptRequestSnapshotCache
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, PromptRequestSnapshotRecord> SnapshotsByChannel =
            new Dictionary<string, PromptRequestSnapshotRecord>(StringComparer.OrdinalIgnoreCase);
        private const int MaxSnapshotsPerChannel = 1;
        private const int MaxSnapshotAgeHours = 2;

        public static void RecordSnapshot(
            string promptChannel,
            IReadOnlyDictionary<string, object> values,
            string scenarioSignature = null)
        {
            if (string.IsNullOrWhiteSpace(promptChannel) || values == null)
            {
                return;
            }

            string normalizedChannel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            if (string.IsNullOrWhiteSpace(normalizedChannel))
            {
                return;
            }

            var snapshot = new PromptRequestSnapshotRecord
            {
                PromptChannel = normalizedChannel,
                Values = CloneValues(values),
                RecordedAtUtc = DateTime.UtcNow,
                ScenarioSignature = scenarioSignature ?? string.Empty
            };

            lock (SyncRoot)
            {
                SnapshotsByChannel[normalizedChannel] = snapshot;
            }
        }

        public static bool TryGetSnapshot(string promptChannel, out PromptRequestSnapshotRecord snapshot)
        {
            snapshot = null;
            if (string.IsNullOrWhiteSpace(promptChannel))
            {
                return false;
            }

            string normalizedChannel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            if (string.IsNullOrWhiteSpace(normalizedChannel))
            {
                return false;
            }

            lock (SyncRoot)
            {
                if (!SnapshotsByChannel.TryGetValue(normalizedChannel, out snapshot))
                {
                    return false;
                }

                if (snapshot == null || snapshot.Values == null)
                {
                    SnapshotsByChannel.Remove(normalizedChannel);
                    return false;
                }

                if (DateTime.UtcNow - snapshot.RecordedAtUtc > TimeSpan.FromHours(MaxSnapshotAgeHours))
                {
                    SnapshotsByChannel.Remove(normalizedChannel);
                    return false;
                }
            }

            return true;
        }

        public static Dictionary<string, object> CloneSnapshotValues(string promptChannel)
        {
            if (!TryGetSnapshot(promptChannel, out PromptRequestSnapshotRecord snapshot))
            {
                return null;
            }

            return CloneValues(snapshot.Values);
        }

        public static void ClearSnapshot(string promptChannel)
        {
            if (string.IsNullOrWhiteSpace(promptChannel))
            {
                return;
            }

            string normalizedChannel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            lock (SyncRoot)
            {
                SnapshotsByChannel.Remove(normalizedChannel);
            }
        }

        public static void ClearAllSnapshots()
        {
            lock (SyncRoot)
            {
                SnapshotsByChannel.Clear();
            }
        }

        public static IReadOnlyList<string> GetCachedChannels()
        {
            lock (SyncRoot)
            {
                return SnapshotsByChannel.Keys.ToList();
            }
        }

        public static bool HasSnapshotForChannel(string promptChannel)
        {
            return TryGetSnapshot(promptChannel, out _);
        }

        private static Dictionary<string, object> CloneValues(IReadOnlyDictionary<string, object> source)
        {
            if (source == null)
            {
                return new Dictionary<string, object>();
            }

            var result = new Dictionary<string, object>(source.Count, StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, object> pair in source)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                result[pair.Key] = CloneValue(pair.Value);
            }

            return result;
        }

        private static Dictionary<string, object> CloneValuesFromIDict(IDictionary<string, object> source)
        {
            if (source == null)
            {
                return new Dictionary<string, object>();
            }

            var result = new Dictionary<string, object>(source.Count, StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, object> pair in source)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                result[pair.Key] = CloneValue(pair.Value);
            }

            return result;
        }

        private static object CloneValue(object value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is string text)
            {
                return text;
            }

            if (value is int || value is float || value is double || value is bool || value is long)
            {
                return value;
            }

            if (value is IDictionary<string, object> dict)
            {
                return CloneValuesFromIDict(dict);
            }

            return value;
        }
    }
}
