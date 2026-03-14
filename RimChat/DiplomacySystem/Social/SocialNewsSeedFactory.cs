using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Core;
using RimChat.Memory;
using RimChat.WorldState;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>/// Dependencies: world-event ledger, leader memory, faction manager.
 /// Responsibility: translate real world-state records into fact-grounded social-news seeds.
 ///</summary>
    internal static class SocialNewsSeedFactory
    {
        private const int SeedWindowDays = 30;
        private const int GoodwillShiftThreshold = 12;
        private static readonly string[] QuestTokens = { "quest", "任务", "completed", "failed", "reward" };
        private static readonly string[] TradeTokens = { "trade", "caravan", "交易", "商队", "deal" };
        private static readonly string[] GoodwillTokens = { "goodwill", "relation", "好感", "关系" };
        private static readonly string[] RelationTokens = { "war", "peace", "alliance", "truce", "宣战", "议和", "结盟", "停战" };
        private static readonly string[] AidTokens = { "aid", "援助", "援军", "medical aid", "resource aid", "arrived" };

        public static SocialNewsSeed CreateDialogueSeed(
            Faction sourceFaction,
            Faction targetFaction,
            SocialPostCategory category,
            int sentiment,
            string summary,
            bool isKeyword,
            string intentHint,
            DebugGenerateReason reason)
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            return new SocialNewsSeed
            {
                OriginType = isKeyword ? SocialNewsOriginType.DialogueKeyword : SocialNewsOriginType.DialogueExplicit,
                OriginKey = BuildDialogueOriginKey(sourceFaction, targetFaction, currentTick, summary, intentHint, isKeyword),
                SourceFaction = sourceFaction,
                TargetFaction = targetFaction,
                Category = category,
                Sentiment = sentiment,
                OccurredTick = currentTick,
                Summary = string.IsNullOrWhiteSpace(summary)
                    ? "RimChat_SocialPostSummaryFromDialogue".Translate().ToString()
                    : summary.Trim(),
                IntentHint = intentHint ?? string.Empty,
                SourceLabel = "RimChat_SocialSourceOfficialStatement",
                CredibilityLabel = isKeyword
                    ? "RimChat_SocialCredibilityMonitoredChannel"
                    : "RimChat_SocialCredibilityOfficialStatement",
                CredibilityValue = isKeyword ? 0.72f : 0.88f,
                IsFromPlayerDialogue = true,
                ApplyDiplomaticImpact = true,
                DebugReason = reason,
                Facts = BuildDialogueFacts(sourceFaction, targetFaction, category, sentiment, summary, intentHint, isKeyword)
            };
        }

        public static List<SocialNewsSeed> CollectScheduledSeeds()
        {
            var seeds = new List<SocialNewsSeed>();
            AddRaidSeeds(seeds);
            AddWorldEventSeeds(seeds);
            AddLeaderMemorySeeds(seeds);
            AddSummarySeeds(seeds);
            if (IsExtendedAutoSeedEnabled())
            {
                AddExtendedWorldEventSeeds(seeds);
                AddExtendedMemorySeeds(seeds);
            }

            return seeds
                .Where(seed => seed != null && seed.IsValid())
                .OrderByDescending(seed => seed.OccurredTick)
                .ToList();
        }

        private static bool IsExtendedAutoSeedEnabled()
        {
            return RimChatMod.Instance?.InstanceSettings?.EnableSocialCircleExtendedAutoSeeds ?? true;
        }

        private static List<string> BuildDialogueFacts(
            Faction sourceFaction,
            Faction targetFaction,
            SocialPostCategory category,
            int sentiment,
            string summary,
            string intentHint,
            bool isKeyword)
        {
            return new List<string>
            {
                $"Source faction: {sourceFaction?.Name ?? "Unknown"}",
                $"Target faction: {targetFaction?.Name ?? "None"}",
                $"Category: {SocialCircleService.GetCategoryLabel(category)}",
                $"Sentiment: {sentiment}",
                $"Summary: {(summary ?? string.Empty).Trim()}",
                $"Channel: {(isKeyword ? "keyword-detected public signal" : "explicit official public statement")}",
                $"Intent hint: {(intentHint ?? string.Empty).Trim()}"
            };
        }

        private static void AddRaidSeeds(List<SocialNewsSeed> seeds)
        {
            List<RaidBattleReportRecord> reports = WorldEventLedgerComponent.Instance
                ?.GetRecentRaidBattleReports(Faction.OfPlayer, SeedWindowDays, true) ?? new List<RaidBattleReportRecord>();
            foreach (RaidBattleReportRecord report in reports)
            {
                seeds.Add(CreateRaidSeed(report));
            }
        }

        private static SocialNewsSeed CreateRaidSeed(RaidBattleReportRecord report)
        {
            Faction attacker = ResolveFaction(report?.AttackerFactionId);
            Faction defender = ResolveFaction(report?.DefenderFactionId);
            int sentiment = CalculateBattleSentiment(report);
            return new SocialNewsSeed
            {
                OriginType = SocialNewsOriginType.RaidBattleReport,
                OriginKey = $"raid:{report?.AttackerFactionId}:{report?.DefenderFactionId}:{report?.BattleEndTick}:{report?.MapId}",
                SourceFaction = attacker,
                TargetFaction = defender,
                Category = SocialPostCategory.Military,
                Sentiment = sentiment,
                OccurredTick = report?.BattleEndTick ?? 0,
                Summary = report?.Summary ?? string.Empty,
                SourceLabel = "RimChat_SocialSourceBattleReport",
                CredibilityLabel = "RimChat_SocialCredibilityBattleReport",
                CredibilityValue = 0.85f,
                Facts = BuildRaidFacts(report)
            };
        }

        private static List<string> BuildRaidFacts(RaidBattleReportRecord report)
        {
            return new List<string>
            {
                report?.Summary ?? string.Empty,
                $"Attacker: {report?.AttackerFactionName ?? "Unknown"}",
                $"Defender: {report?.DefenderFactionName ?? "Unknown"}",
                $"Location: {report?.MapLabel ?? "Unknown"}",
                $"Attacker deaths: {report?.AttackerDeaths ?? 0}",
                $"Defender deaths: {report?.DefenderDeaths ?? 0}",
                $"Defender downed: {report?.DefenderDowned ?? 0}"
            };
        }

        private static int CalculateBattleSentiment(RaidBattleReportRecord report)
        {
            if (report == null)
            {
                return 0;
            }

            int score = report.DefenderDeaths - report.AttackerDeaths;
            if (score >= 4) return 2;
            if (score > 0) return 1;
            if (score <= -4) return -2;
            return score < 0 ? -1 : 0;
        }

        private static void AddWorldEventSeeds(List<SocialNewsSeed> seeds)
        {
            List<WorldEventRecord> events = WorldEventLedgerComponent.Instance
                ?.GetRecentWorldEvents(Faction.OfPlayer, SeedWindowDays, true, true) ?? new List<WorldEventRecord>();
            foreach (WorldEventRecord record in events)
            {
                seeds.Add(CreateWorldEventSeed(record));
            }
        }

        private static SocialNewsSeed CreateWorldEventSeed(WorldEventRecord record)
        {
            Faction sourceFaction = ResolveKnownFaction(record?.KnownFactionIds, preferPlayer: false);
            Faction targetFaction = ResolveKnownFaction(record?.KnownFactionIds, preferPlayer: true);
            SocialPostCategory category = SocialCircleService.InferCategory(record?.Summary, record?.EventType);
            int sentiment = SocialCircleService.InferSentiment(record?.Summary);
            return new SocialNewsSeed
            {
                OriginType = SocialNewsOriginType.WorldEvent,
                OriginKey = string.IsNullOrWhiteSpace(record?.SourceKey)
                    ? $"world:{record?.OccurredTick}:{record?.EventType}:{record?.Summary}"
                    : record.SourceKey,
                SourceFaction = sourceFaction,
                TargetFaction = targetFaction,
                Category = category,
                Sentiment = sentiment,
                OccurredTick = record?.OccurredTick ?? 0,
                Summary = record?.Summary ?? string.Empty,
                SourceLabel = "RimChat_SocialSourceWorldLedger",
                CredibilityLabel = record?.IsPublic == true
                    ? "RimChat_SocialCredibilityPublicReport"
                    : "RimChat_SocialCredibilityObserverNote",
                CredibilityValue = record?.IsPublic == true ? 0.74f : 0.62f,
                Facts = BuildWorldEventFacts(record)
            };
        }

        private static List<string> BuildWorldEventFacts(WorldEventRecord record)
        {
            return new List<string>
            {
                record?.Summary ?? string.Empty,
                $"Event type: {record?.EventType ?? "unknown"}",
                $"Location: {record?.MapLabel ?? "Unknown"}",
                $"Visibility: {(record?.IsPublic == true ? "public" : "direct/limited")}",
                $"Known factions: {string.Join(", ", record?.KnownFactionIds ?? new List<string>())}"
            };
        }

        private static void AddLeaderMemorySeeds(List<SocialNewsSeed> seeds)
        {
            foreach (Faction faction in GetEligibleSourceFactions())
            {
                FactionLeaderMemory memory = LeaderMemoryManager.Instance?.GetMemory(faction);
                if (memory?.SignificantEvents == null)
                {
                    continue;
                }

                foreach (SignificantEventMemory evt in memory.SignificantEvents)
                {
                    if (ShouldSkipMemoryEvent(evt))
                    {
                        continue;
                    }

                    seeds.Add(CreateLeaderMemorySeed(faction, evt));
                }
            }
        }

        private static bool ShouldSkipMemoryEvent(SignificantEventMemory evt)
        {
            return evt == null
                || evt.OccurredTick <= 0
                || string.IsNullOrWhiteSpace(evt.Description)
                || evt.Description.StartsWith("[init-snapshot]", StringComparison.Ordinal)
                || (IsExtendedAutoSeedEnabled() && IsExtendedMemoryEvent(evt));
        }

        private static void AddExtendedWorldEventSeeds(List<SocialNewsSeed> seeds)
        {
            List<WorldEventRecord> events = WorldEventLedgerComponent.Instance
                ?.GetRecentWorldEvents(Faction.OfPlayer, SeedWindowDays, true, true) ?? new List<WorldEventRecord>();
            foreach (WorldEventRecord record in events)
            {
                if (TryCreateExtendedWorldEventSeed(record, out SocialNewsSeed seed))
                {
                    seeds.Add(seed);
                }
            }
        }

        private static bool TryCreateExtendedWorldEventSeed(WorldEventRecord record, out SocialNewsSeed seed)
        {
            seed = null;
            if (record == null || record.OccurredTick <= 0 || string.IsNullOrWhiteSpace(record.Summary))
            {
                return false;
            }

            string merged = $"{record.EventType} {record.Summary}".ToLowerInvariant();
            if (ContainsAny(merged, QuestTokens))
            {
                seed = CreateExtendedWorldSeed(record, SocialNewsOriginType.QuestOutcome, SocialPostCategory.Diplomatic, 0.76f);
                return true;
            }

            if (ContainsAny(merged, TradeTokens))
            {
                seed = CreateExtendedWorldSeed(record, SocialNewsOriginType.TradeDeal, SocialPostCategory.Economic, 0.8f);
                return true;
            }

            if (ContainsAny(merged, AidTokens))
            {
                seed = CreateExtendedWorldSeed(record, SocialNewsOriginType.AidArrival, SocialPostCategory.Diplomatic, 0.82f);
                return true;
            }

            if (ContainsAny(merged, RelationTokens))
            {
                seed = CreateExtendedWorldSeed(record, SocialNewsOriginType.RelationPivot, SocialPostCategory.Diplomatic, 0.84f);
                return true;
            }

            if (ContainsAny(merged, GoodwillTokens) && TryExtractGoodwillDelta(record.Summary, out int delta) && Math.Abs(delta) >= GoodwillShiftThreshold)
            {
                seed = CreateExtendedWorldSeed(record, SocialNewsOriginType.GoodwillShift, SocialPostCategory.Diplomatic, 0.78f);
                return true;
            }

            return false;
        }

        private static SocialNewsSeed CreateExtendedWorldSeed(
            WorldEventRecord record,
            SocialNewsOriginType originType,
            SocialPostCategory category,
            float credibility)
        {
            Faction sourceFaction = ResolveKnownFaction(record?.KnownFactionIds, preferPlayer: false);
            Faction targetFaction = ResolveKnownFaction(record?.KnownFactionIds, preferPlayer: true);
            int sentiment = SocialCircleService.InferSentiment(record?.Summary);
            return new SocialNewsSeed
            {
                OriginType = originType,
                OriginKey = $"extended:{originType}:{record?.SourceKey}:{record?.OccurredTick}",
                SourceFaction = sourceFaction,
                TargetFaction = targetFaction,
                Category = category,
                Sentiment = sentiment,
                OccurredTick = record?.OccurredTick ?? 0,
                Summary = record?.Summary ?? string.Empty,
                SourceLabel = "RimChat_SocialSourceWorldLedger",
                CredibilityLabel = "RimChat_SocialCredibilityPublicReport",
                CredibilityValue = credibility,
                Facts = BuildWorldEventFacts(record)
            };
        }

        private static void AddExtendedMemorySeeds(List<SocialNewsSeed> seeds)
        {
            foreach (Faction faction in GetEligibleSourceFactions())
            {
                FactionLeaderMemory memory = LeaderMemoryManager.Instance?.GetMemory(faction);
                if (memory?.SignificantEvents == null)
                {
                    continue;
                }

                foreach (SignificantEventMemory evt in memory.SignificantEvents)
                {
                    if (TryCreateExtendedMemorySeed(faction, evt, out SocialNewsSeed seed))
                    {
                        seeds.Add(seed);
                    }
                }
            }
        }

        private static bool TryCreateExtendedMemorySeed(Faction ownerFaction, SignificantEventMemory evt, out SocialNewsSeed seed)
        {
            seed = null;
            if (!IsExtendedMemoryEvent(evt))
            {
                return false;
            }

            Faction targetFaction = ResolveFaction(evt?.InvolvedFactionId);
            (SocialNewsOriginType originType, SocialPostCategory category, float credibility, int sentimentOverride) = ResolveExtendedMemoryStyle(evt);
            int sentiment = sentimentOverride != int.MinValue
                ? sentimentOverride
                : SocialCircleService.InferSentiment(evt?.Description);

            seed = new SocialNewsSeed
            {
                OriginType = originType,
                OriginKey = $"extended-memory:{originType}:{ownerFaction?.GetUniqueLoadID()}:{evt?.OccurredTick}:{evt?.Timestamp}",
                SourceFaction = ownerFaction,
                TargetFaction = targetFaction,
                Category = category,
                Sentiment = sentiment,
                OccurredTick = evt?.OccurredTick ?? 0,
                Summary = evt?.Description ?? string.Empty,
                SourceLabel = "RimChat_SocialSourceLeaderMemory",
                CredibilityLabel = "RimChat_SocialCredibilityLeaderMemory",
                CredibilityValue = credibility,
                Facts = BuildLeaderMemoryFacts(evt)
            };
            return true;
        }

        private static (SocialNewsOriginType originType, SocialPostCategory category, float credibility, int sentimentOverride) ResolveExtendedMemoryStyle(SignificantEventMemory evt)
        {
            switch (evt.EventType)
            {
                case SignificantEventType.QuestIssued:
                    return (SocialNewsOriginType.QuestOutcome, SocialPostCategory.Diplomatic, 0.72f, int.MinValue);
                case SignificantEventType.TradeCaravan:
                    return (SocialNewsOriginType.TradeDeal, SocialPostCategory.Economic, 0.8f, 1);
                case SignificantEventType.AidRequested:
                    return (SocialNewsOriginType.AidArrival, SocialPostCategory.Diplomatic, 0.8f, 1);
                case SignificantEventType.WarDeclared:
                    return (SocialNewsOriginType.RelationPivot, SocialPostCategory.Military, 0.86f, -2);
                case SignificantEventType.PeaceMade:
                case SignificantEventType.AllianceFormed:
                    return (SocialNewsOriginType.RelationPivot, SocialPostCategory.Diplomatic, 0.86f, 2);
                case SignificantEventType.Betrayal:
                    return (SocialNewsOriginType.RelationPivot, SocialPostCategory.Military, 0.86f, -2);
                case SignificantEventType.GoodwillChanged:
                    return (SocialNewsOriginType.GoodwillShift, SocialPostCategory.Diplomatic, 0.76f, int.MinValue);
                default:
                    return (SocialNewsOriginType.LeaderMemory, SocialPostCategory.Diplomatic, 0.58f, int.MinValue);
            }
        }

        private static bool IsExtendedMemoryEvent(SignificantEventMemory evt)
        {
            if (evt == null)
            {
                return false;
            }

            if (evt.EventType == SignificantEventType.GoodwillChanged)
            {
                return TryExtractGoodwillDelta(evt.Description, out int delta) && Math.Abs(delta) >= GoodwillShiftThreshold;
            }

            return evt.EventType == SignificantEventType.QuestIssued
                || evt.EventType == SignificantEventType.TradeCaravan
                || evt.EventType == SignificantEventType.AidRequested
                || evt.EventType == SignificantEventType.WarDeclared
                || evt.EventType == SignificantEventType.PeaceMade
                || evt.EventType == SignificantEventType.AllianceFormed
                || evt.EventType == SignificantEventType.Betrayal;
        }

        private static bool TryExtractGoodwillDelta(string text, out int delta)
        {
            delta = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            const string marker = "goodwill by ";
            int markerIndex = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return false;
            }

            string tail = text.Substring(markerIndex + marker.Length).TrimStart();
            if (string.IsNullOrEmpty(tail))
            {
                return false;
            }

            int length = 0;
            if (tail[0] == '+' || tail[0] == '-')
            {
                length = 1;
            }

            while (length < tail.Length && char.IsDigit(tail[length]))
            {
                length++;
            }

            if (length <= 0)
            {
                return false;
            }

            return int.TryParse(tail.Substring(0, length), out delta);
        }

        private static bool ContainsAny(string text, IEnumerable<string> tokens)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return (tokens ?? Enumerable.Empty<string>())
                .Any(token => !string.IsNullOrWhiteSpace(token) && text.Contains(token));
        }

        private static SocialNewsSeed CreateLeaderMemorySeed(Faction ownerFaction, SignificantEventMemory evt)
        {
            Faction targetFaction = ResolveFaction(evt?.InvolvedFactionId);
            SocialPostCategory category = SocialCircleService.InferCategory(evt?.Description, evt?.EventType.ToString());
            int sentiment = SocialCircleService.InferSentiment(evt?.Description);
            return new SocialNewsSeed
            {
                OriginType = SocialNewsOriginType.LeaderMemory,
                OriginKey = $"memory:{ownerFaction?.GetUniqueLoadID()}:{evt?.OccurredTick}:{evt?.Timestamp}:{evt?.EventType}",
                SourceFaction = ownerFaction,
                TargetFaction = targetFaction,
                Category = category,
                Sentiment = sentiment,
                OccurredTick = evt?.OccurredTick ?? 0,
                Summary = evt?.Description ?? string.Empty,
                SourceLabel = "RimChat_SocialSourceLeaderMemory",
                CredibilityLabel = "RimChat_SocialCredibilityLeaderMemory",
                CredibilityValue = 0.58f,
                Facts = BuildLeaderMemoryFacts(evt)
            };
        }

        private static List<string> BuildLeaderMemoryFacts(SignificantEventMemory evt)
        {
            return new List<string>
            {
                evt?.Description ?? string.Empty,
                $"Event type: {evt?.EventType.ToString() ?? "Unknown"}",
                $"Involved faction: {evt?.InvolvedFactionName ?? "None"}",
                $"Occurred tick: {evt?.OccurredTick ?? 0}"
            };
        }

        private static void AddSummarySeeds(List<SocialNewsSeed> seeds)
        {
            foreach (Faction faction in GetEligibleSourceFactions())
            {
                FactionLeaderMemory memory = LeaderMemoryManager.Instance?.GetMemory(faction);
                IEnumerable<CrossChannelSummaryRecord> records = memory?.DiplomacySessionSummaries ?? Enumerable.Empty<CrossChannelSummaryRecord>();
                foreach (CrossChannelSummaryRecord record in records)
                {
                    if (record == null || string.IsNullOrWhiteSpace(record.SummaryText) || record.GameTick <= 0)
                    {
                        continue;
                    }

                    seeds.Add(CreateSummarySeed(faction, record));
                }
            }
        }

        private static SocialNewsSeed CreateSummarySeed(Faction faction, CrossChannelSummaryRecord record)
        {
            SocialPostCategory category = SocialCircleService.InferCategory(record?.SummaryText, record?.Source.ToString());
            int sentiment = SocialCircleService.InferSentiment(record?.SummaryText);
            return new SocialNewsSeed
            {
                OriginType = SocialNewsOriginType.DiplomacySummary,
                OriginKey = $"summary:{record?.Source}:{record?.GameTick}:{record?.ContentHash}",
                SourceFaction = faction,
                TargetFaction = null,
                Category = category,
                Sentiment = sentiment,
                OccurredTick = record?.GameTick ?? 0,
                Summary = record?.SummaryText ?? string.Empty,
                SourceLabel = "RimChat_SocialSourceDiplomacyArchive",
                CredibilityLabel = record?.IsLlmFallback == true
                    ? "RimChat_SocialCredibilityArchiveFallback"
                    : "RimChat_SocialCredibilityArchiveSummary",
                CredibilityValue = record?.IsLlmFallback == true ? 0.55f : 0.68f,
                Facts = BuildSummaryFacts(record)
            };
        }

        private static List<string> BuildSummaryFacts(CrossChannelSummaryRecord record)
        {
            return new List<string>
            {
                record?.SummaryText ?? string.Empty,
                $"Source pool: {record?.Source.ToString() ?? "Unknown"}",
                $"Confidence: {(record?.Confidence ?? 0f):F2}",
                $"Key facts: {string.Join(" | ", record?.KeyFacts ?? new List<string>())}"
            };
        }

        private static IEnumerable<Faction> GetEligibleSourceFactions()
        {
            return Find.FactionManager.AllFactions
                .Where(faction => faction != null && !faction.IsPlayer && !faction.defeated && !faction.def.hidden);
        }

        private static Faction ResolveKnownFaction(IEnumerable<string> ids, bool preferPlayer)
        {
            List<Faction> factions = (ids ?? Enumerable.Empty<string>())
                .Select(ResolveFaction)
                .Where(faction => faction != null)
                .Distinct()
                .ToList();
            if (preferPlayer)
            {
                return factions.FirstOrDefault(faction => faction.IsPlayer);
            }

            return factions.FirstOrDefault(faction => !faction.IsPlayer);
        }

        private static Faction ResolveFaction(string factionId)
        {
            if (string.IsNullOrWhiteSpace(factionId))
            {
                return null;
            }

            return Find.FactionManager.AllFactions.FirstOrDefault(faction =>
                string.Equals(faction?.GetUniqueLoadID(), factionId, StringComparison.Ordinal)
                || string.Equals(BuildMemoryFactionId(faction), factionId, StringComparison.Ordinal));
        }

        private static string BuildMemoryFactionId(Faction faction)
        {
            if (faction?.def != null && !string.IsNullOrWhiteSpace(faction.def.defName))
            {
                return $"{faction.def.defName}_{faction.loadID}";
            }

            return faction == null ? string.Empty : $"custom_{faction.loadID}";
        }

        private static string BuildDialogueOriginKey(
            Faction sourceFaction,
            Faction targetFaction,
            int currentTick,
            string summary,
            string intentHint,
            bool isKeyword)
        {
            string sourceId = sourceFaction?.GetUniqueLoadID() ?? "none";
            string targetId = targetFaction?.GetUniqueLoadID() ?? "none";
            return $"{(isKeyword ? "keyword" : "explicit")}:{sourceId}:{targetId}:{currentTick}:{summary}:{intentHint}";
        }
    }
}
