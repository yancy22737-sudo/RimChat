using System;
using System.Collections.Generic;
using System.Linq;
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
            AddAidArrivalSeeds(seeds);
            AddLeaderMemorySeeds(seeds);
            AddSummarySeeds(seeds);
            AddScheduledEventSeeds(seeds);
            return seeds
                .Where(seed => seed != null && seed.IsValid())
                .OrderByDescending(seed => seed.OccurredTick)
                .ToList();
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
                if (ShouldTreatAsAidArrival(record))
                {
                    continue;
                }

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

        private static void AddAidArrivalSeeds(List<SocialNewsSeed> seeds)
        {
            List<WorldEventRecord> events = WorldEventLedgerComponent.Instance
                ?.GetRecentWorldEvents(Faction.OfPlayer, SeedWindowDays, true, true) ?? new List<WorldEventRecord>();
            foreach (WorldEventRecord record in events)
            {
                if (!ShouldTreatAsAidArrival(record))
                {
                    continue;
                }

                seeds.Add(CreateAidArrivalWorldEventSeed(record));
            }
        }

        private static bool ShouldTreatAsAidArrival(WorldEventRecord record)
        {
            string merged = $"{record?.EventType} {record?.Summary}".ToLowerInvariant();
            return merged.Contains("aid")
                   || merged.Contains("援助")
                   || merged.Contains("救援")
                   || merged.Contains("support");
        }

        private static SocialNewsSeed CreateAidArrivalWorldEventSeed(WorldEventRecord record)
        {
            Faction sourceFaction = ResolveKnownFaction(record?.KnownFactionIds, preferPlayer: false);
            return new SocialNewsSeed
            {
                OriginType = SocialNewsOriginType.AidArrival,
                OriginKey = string.IsNullOrWhiteSpace(record?.SourceKey)
                    ? $"aid-arrival:{record?.OccurredTick}:{record?.Summary}"
                    : $"aid-arrival:{record.SourceKey}",
                SourceFaction = sourceFaction,
                TargetFaction = Faction.OfPlayer,
                Category = SocialPostCategory.Economic,
                Sentiment = 2,
                OccurredTick = record?.OccurredTick ?? 0,
                Summary = record?.Summary ?? string.Empty,
                SourceLabel = "RimChat_SocialSourceAidArrival",
                CredibilityLabel = "RimChat_SocialCredibilityPublicReport",
                CredibilityValue = 0.88f,
                Facts = BuildWorldEventFacts(record)
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
                || evt.Description.StartsWith("[init-snapshot]", StringComparison.Ordinal);
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

        private static void AddScheduledEventSeeds(List<SocialNewsSeed> seeds)
        {
            List<ScheduledSocialEventRecord> events = GameComponent_DiplomacyManager.Instance
                ?.GetRecentScheduledSocialEvents(SeedWindowDays) ?? new List<ScheduledSocialEventRecord>();
            for (int index = 0; index < events.Count; index++)
            {
                SocialNewsSeed seed = CreateScheduledEventSeed(events[index]);
                if (seed != null)
                {
                    seeds.Add(seed);
                }
            }
        }

        private static SocialNewsSeed CreateScheduledEventSeed(ScheduledSocialEventRecord record)
        {
            if (record == null || record.EventType == ScheduledSocialEventType.Unknown)
            {
                return null;
            }

            return record.EventType switch
            {
                ScheduledSocialEventType.QuestResult => CreateQuestResultSeed(record),
                ScheduledSocialEventType.TradeDeal => CreateTradeDealSeed(record),
                ScheduledSocialEventType.GoodwillShift => CreateGoodwillShiftSeed(record),
                ScheduledSocialEventType.RelationShift => CreateRelationShiftSeed(record),
                ScheduledSocialEventType.AidArrival => CreateAidArrivalSeed(record),
                _ => null
            };
        }

        private static SocialNewsSeed CreateQuestResultSeed(ScheduledSocialEventRecord record)
        {
            return CreateScheduledSeed(
                record,
                SocialNewsOriginType.QuestResult,
                SocialPostCategory.Diplomatic,
                record.Value >= 0 ? 1 : -1,
                "RimChat_SocialSourceQuestResult",
                "RimChat_SocialCredibilityPublicReport",
                0.86f);
        }

        private static SocialNewsSeed CreateTradeDealSeed(ScheduledSocialEventRecord record)
        {
            int sentiment = record.Value >= 0 ? 1 : -1;
            return CreateScheduledSeed(
                record,
                SocialNewsOriginType.TradeDeal,
                SocialPostCategory.Economic,
                sentiment,
                "RimChat_SocialSourceTradeDeal",
                "RimChat_SocialCredibilityPublicReport",
                0.82f);
        }

        private static SocialNewsSeed CreateGoodwillShiftSeed(ScheduledSocialEventRecord record)
        {
            int sentiment = record.Value > 0 ? 2 : -2;
            return CreateScheduledSeed(
                record,
                SocialNewsOriginType.GoodwillShift,
                SocialPostCategory.Diplomatic,
                sentiment,
                "RimChat_SocialSourceGoodwillShift",
                "RimChat_SocialCredibilityObserverNote",
                0.78f);
        }

        private static SocialNewsSeed CreateRelationShiftSeed(ScheduledSocialEventRecord record)
        {
            bool hostile = record.Value < 0 || (record.Detail?.Contains("Hostile") ?? false);
            return CreateScheduledSeed(
                record,
                SocialNewsOriginType.RelationShift,
                hostile ? SocialPostCategory.Military : SocialPostCategory.Diplomatic,
                hostile ? -2 : 2,
                "RimChat_SocialSourceRelationShift",
                "RimChat_SocialCredibilityObserverNote",
                0.84f);
        }

        private static SocialNewsSeed CreateAidArrivalSeed(ScheduledSocialEventRecord record)
        {
            return CreateScheduledSeed(
                record,
                SocialNewsOriginType.AidArrival,
                SocialPostCategory.Economic,
                2,
                "RimChat_SocialSourceAidArrival",
                "RimChat_SocialCredibilityPublicReport",
                0.88f);
        }

        private static SocialNewsSeed CreateScheduledSeed(
            ScheduledSocialEventRecord record,
            SocialNewsOriginType originType,
            SocialPostCategory category,
            int sentiment,
            string sourceLabel,
            string credibilityLabel,
            float credibility)
        {
            return new SocialNewsSeed
            {
                OriginType = originType,
                OriginKey = $"scheduled:{record.EventType}:{record.SourceKey}",
                SourceFaction = record.SourceFaction,
                TargetFaction = record.TargetFaction,
                Category = category,
                Sentiment = sentiment,
                OccurredTick = record.OccurredTick,
                Summary = record.Summary ?? string.Empty,
                SourceLabel = sourceLabel,
                CredibilityLabel = credibilityLabel,
                CredibilityValue = credibility,
                Facts = BuildScheduledFacts(record)
            };
        }

        private static List<string> BuildScheduledFacts(ScheduledSocialEventRecord record)
        {
            return new List<string>
            {
                record?.Summary ?? string.Empty,
                $"Event type: {record?.EventType.ToString() ?? "Unknown"}",
                $"Source faction: {record?.SourceFaction?.Name ?? "Unknown"}",
                $"Target faction: {record?.TargetFaction?.Name ?? "None"}",
                $"Detail: {record?.Detail ?? string.Empty}",
                $"Value: {record?.Value ?? 0}"
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
