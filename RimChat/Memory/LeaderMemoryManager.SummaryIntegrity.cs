using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.AI;
using Verse;
using RimWorld;

namespace RimChat.Memory
{
    public partial class LeaderMemoryManager
    {
        private readonly HashSet<string> _summaryRepairAttempted = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _summaryRepairInFlight = new HashSet<string>(StringComparer.Ordinal);

        private bool TryBuildSanitizedSummaryRecord(
            CrossChannelSummaryRecord source,
            out CrossChannelSummaryRecord sanitized,
            out string reasonTag)
        {
            sanitized = null;
            reasonTag = string.Empty;
            if (source == null)
            {
                reasonTag = "null_record";
                return false;
            }

            CrossChannelSummaryRecord clone = source.Clone();
            clone.SummaryText = TextIntegrityGuard.SanitizeSummaryText(clone.SummaryText, 280);
            clone.KeyFacts = (clone.KeyFacts ?? new List<string>())
                .Select(item => TextIntegrityGuard.SanitizeKeyFact(item, 100))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .Take(3)
                .ToList();

            if (string.IsNullOrWhiteSpace(clone.SummaryText))
            {
                reasonTag = "empty_after_sanitize";
                return false;
            }

            if (TextIntegrityGuard.TryDetectCorruption(clone.SummaryText, out _, out string summaryReason))
            {
                reasonTag = summaryReason ?? "summary_corrupt";
                return false;
            }

            for (int i = 0; i < clone.KeyFacts.Count; i++)
            {
                if (TextIntegrityGuard.TryDetectCorruption(clone.KeyFacts[i], out _, out string factReason))
                {
                    reasonTag = factReason ?? "fact_corrupt";
                    return false;
                }
            }

            sanitized = clone;
            return true;
        }

        private void TryQueueSummaryRepair(
            Faction faction,
            CrossChannelSummaryRecord original,
            int maxEntries,
            bool useRpgPool,
            string reasonTag)
        {
            if (faction == null || original == null)
            {
                return;
            }

            if (original.IsLlmFallback)
            {
                return;
            }

            string repairKey = BuildSummaryRepairKey(faction, original);
            lock (_summarySyncRoot)
            {
                if (_summaryRepairAttempted.Contains(repairKey) || _summaryRepairInFlight.Contains(repairKey))
                {
                    return;
                }

                _summaryRepairAttempted.Add(repairKey);
                _summaryRepairInFlight.Add(repairKey);
            }

            if (!AIChatServiceAsync.Instance.IsConfigured())
            {
                lock (_summarySyncRoot)
                {
                    _summaryRepairInFlight.Remove(repairKey);
                }

                Log.Warning($"[RimChat] summary_repair_skip_no_ai source={original.Source} contentHash={original.ContentHash ?? string.Empty} factionId={original.FactionId ?? string.Empty} reason={reasonTag ?? "corrupt"}");
                return;
            }

            DialogueUsageChannel usageChannel = useRpgPool ? DialogueUsageChannel.Rpg : DialogueUsageChannel.Diplomacy;
            var messages = new List<ChatMessageData>
            {
                new ChatMessageData
                {
                    role = "system",
                    content = "Rewrite the provided memory summary into clean readable text. Output plain text only. First line must start with 'Summary:'. Then output up to three '- fact' lines."
                },
                new ChatMessageData
                {
                    role = "user",
                    content =
                        $"source={original.Source}\n" +
                        $"faction={faction.Name}\n" +
                        $"reason={reasonTag}\n" +
                        $"summary={original.SummaryText}\n" +
                        $"facts={(original.KeyFacts == null ? string.Empty : string.Join("; ", original.KeyFacts))}"
                }
            };

            AIChatServiceAsync.Instance.SendChatRequestAsync(
                messages,
                onSuccess: response =>
                {
                    try
                    {
                        ParseRepairResponse(response, out string repairedSummary, out List<string> repairedFacts);
                        CrossChannelSummaryRecord repaired = original.Clone();
                        repaired.SummaryText = repairedSummary;
                        if (repairedFacts.Count > 0)
                        {
                            repaired.KeyFacts = repairedFacts;
                        }

                        repaired.IsLlmFallback = true;
                        repaired.CreatedTimestamp = DateTime.UtcNow.Ticks;
                        if (!TryBuildSanitizedSummaryRecord(repaired, out CrossChannelSummaryRecord sanitized, out string sanitizeReason))
                        {
                            Log.Warning($"[RimChat] summary_repair_drop source={original.Source} contentHash={original.ContentHash ?? string.Empty} factionId={original.FactionId ?? string.Empty} reason={sanitizeReason ?? "repair_failed"}");
                            return;
                        }

                        lock (_summarySyncRoot)
                        {
                            FactionLeaderMemory memory = GetMemory(faction);
                            if (memory == null)
                            {
                                return;
                            }

                            if (useRpgPool)
                            {
                                memory.UpsertRpgDepartSummary(sanitized, maxEntries);
                            }
                            else
                            {
                                memory.UpsertDiplomacySessionSummary(sanitized, maxEntries);
                            }
                        }
                    }
                    finally
                    {
                        lock (_summarySyncRoot)
                        {
                            _summaryRepairInFlight.Remove(repairKey);
                        }
                    }
                },
                onError: error =>
                {
                    lock (_summarySyncRoot)
                    {
                        _summaryRepairInFlight.Remove(repairKey);
                    }

                    Log.Warning($"[RimChat] summary_repair_error source={original.Source} contentHash={original.ContentHash ?? string.Empty} factionId={original.FactionId ?? string.Empty} reason={reasonTag ?? "corrupt"} error={error ?? string.Empty}");
                },
                usageChannel: usageChannel,
                debugSource: AIRequestDebugSource.MemorySummary);
        }

        private static string BuildSummaryRepairKey(Faction faction, CrossChannelSummaryRecord record)
        {
            string factionKey = faction?.GetUniqueLoadID() ?? "unknown_faction";
            string hash = string.IsNullOrWhiteSpace(record?.ContentHash) ? "no_hash" : record.ContentHash;
            return $"{factionKey}|{record?.Source}|{hash}";
        }

        private static void ParseRepairResponse(string raw, out string summary, out List<string> facts)
        {
            summary = string.Empty;
            facts = new List<string>();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            string[] lines = raw.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i]?.Trim() ?? string.Empty;
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.StartsWith("Summary:", StringComparison.OrdinalIgnoreCase))
                {
                    summary = line.Substring("Summary:".Length).Trim();
                    continue;
                }

                if (line.StartsWith("-"))
                {
                    string fact = line.Substring(1).Trim();
                    if (!string.IsNullOrWhiteSpace(fact))
                    {
                        facts.Add(fact);
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(summary))
            {
                summary = raw.Trim();
            }
        }
    }
}
