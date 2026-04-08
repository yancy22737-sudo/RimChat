using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Memory;
using RimChat.WorldState;
using RimWorld;
using RimWorld.Planet;
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
            string trimmedSummary = (summary ?? string.Empty).Trim();
            string trimmedIntent = (intentHint ?? string.Empty).Trim();
            string publicClaim = BuildDialoguePublicClaim(trimmedSummary, trimmedIntent);
            if (string.IsNullOrWhiteSpace(publicClaim))
            {
                return null;
            }

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
                PrimaryClaim = publicClaim,
                QuoteAttributionHint = BuildDialogueQuoteAttributionHint(sourceFaction),
                Facts = BuildDialogueFacts(sourceFaction, targetFaction, category, sentiment, trimmedSummary, trimmedIntent, isKeyword, publicClaim)
            };
        }

        public static string TryBuildFactionDialoguePublicClaim(
            Faction sourceFaction,
            SocialPostCategory category,
            int sentiment,
            string summary,
            string intentHint,
            Faction targetFaction = null)
        {
            string factionName = sourceFaction?.Name?.Trim();
            if (string.IsNullOrWhiteSpace(factionName))
            {
                return string.Empty;
            }

            string normalizedSummary = NormalizeDialogueClaimCandidate(summary);
            string normalizedIntent = NormalizeDialogueClaimCandidate(intentHint);
            if (TryBuildStructuredClaimFromIntent(factionName, category, sentiment, normalizedIntent, targetFaction, out string structuredFromIntent))
            {
                return structuredFromIntent;
            }

            if (TryBuildStructuredClaimFromIntent(factionName, category, sentiment, normalizedSummary, targetFaction, out string structuredFromSummary))
            {
                return structuredFromSummary;
            }

            return string.Empty;
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
            bool isKeyword,
            string publicClaim)
        {
            string trimmedSummary = (summary ?? string.Empty).Trim();
            string trimmedIntent = (intentHint ?? string.Empty).Trim();
            string sourceName = sourceFaction?.Name ?? "Unknown";
            string targetName = targetFaction?.Name ?? "None";
            string channel = isKeyword ? "keyword-detected public signal" : "explicit official public statement";
            string location = ResolveFactionStrongholdLabel(sourceFaction, targetFaction);
            var facts = new List<string>
            {
                $"Source faction: {BuildFactionFactValue(sourceFaction, sourceName)}",
                $"Target faction: {BuildFactionFactValue(targetFaction, targetName)}",
                $"News category: {SocialCircleService.GetCategoryLabel(category)}",
                $"Public channel: {channel}",
                BuildLocationFact(location),
                BuildSettlementContextFact(location, sourceFaction, targetFaction)
            };

            if (!string.IsNullOrWhiteSpace(publicClaim))
            {
                facts.Add($"Public claim: {publicClaim}");
            }

            string background = BuildDialogueBackground(category, sentiment, targetName, trimmedIntent);
            if (!string.IsNullOrWhiteSpace(background))
            {
                facts.Add($"Background tension: {background}");
            }

            string observedReaction = BuildDialogueObservedReaction(category, sentiment, sourceName, targetName, trimmedIntent);
            if (!string.IsNullOrWhiteSpace(observedReaction))
            {
                facts.Add($"Observed reaction: {observedReaction}");
            }

            string implication = BuildDialogueGameplayImplication(category, sentiment, targetFaction, trimmedIntent);
            if (!string.IsNullOrWhiteSpace(implication))
            {
                facts.Add($"Gameplay implication: {implication}");
            }

            if (!string.IsNullOrWhiteSpace(trimmedIntent))
            {
                facts.Add($"Intent hint: {trimmedIntent}");
            }

            return facts;
        }

        private static string BuildDialoguePublicClaim(string summary, string intentHint)
        {
            string normalizedSummary = NormalizeDialogueClaimCandidate(summary);
            if (IsConcreteDialogueFact(normalizedSummary))
            {
                return normalizedSummary;
            }

            string normalizedIntent = NormalizeDialogueClaimCandidate(intentHint);
            if (IsConcreteDialogueFact(normalizedIntent))
            {
                return normalizedIntent;
            }

            return string.Empty;
        }

        private static bool TryBuildStructuredClaimFromIntent(
            string factionName,
            SocialPostCategory category,
            int sentiment,
            string candidate,
            Faction targetFaction,
            out string claim)
        {
            claim = string.Empty;
            string targetName = targetFaction?.Name?.Trim();
            string text = (candidate ?? string.Empty).Trim();

            if (string.Equals(text, SocialIntentType.Raid.ToString(), StringComparison.OrdinalIgnoreCase)
                || (string.Equals(text, "request_raid", StringComparison.OrdinalIgnoreCase))
                || (category == SocialPostCategory.Military && sentiment <= -1 && string.IsNullOrWhiteSpace(text)))
            {
                claim = string.IsNullOrWhiteSpace(targetName)
                    ? $"{factionName}警告，若再受挑衅，将发动普通袭击。"
                    : $"{factionName}警告{targetName}，若再受挑衅，将发动普通袭击。";
                return true;
            }

            if (string.Equals(text, SocialIntentType.Aid.ToString(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "request_aid", StringComparison.OrdinalIgnoreCase))
            {
                claim = string.IsNullOrWhiteSpace(targetName)
                    ? $"{factionName}表示愿意继续提供援助。"
                    : $"{factionName}表示愿意继续向{targetName}提供援助。";
                return true;
            }

            if (string.Equals(text, SocialIntentType.Caravan.ToString(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "request_caravan", StringComparison.OrdinalIgnoreCase))
            {
                claim = string.IsNullOrWhiteSpace(targetName)
                    ? $"{factionName}表示愿意恢复贸易往来。"
                    : $"{factionName}表示愿意与{targetName}恢复贸易往来。";
                return true;
            }

            if (!IsConcreteDialogueFact(text))
            {
                return false;
            }

            claim = text.IndexOf(factionName, StringComparison.Ordinal) >= 0
                ? text
                : $"{factionName}表示：{text}";
            return true;
        }

        private static string BuildDialogueQuoteAttributionHint(Faction sourceFaction)
        {
            if (!string.IsNullOrWhiteSpace(sourceFaction?.Name))
            {
                return sourceFaction.Name.Trim();
            }

            return "Public statement";
        }

        private static string BuildDialogueBackground(
            SocialPostCategory category,
            int sentiment,
            string targetName,
            string intentHint)
        {
            if (category == SocialPostCategory.Military || sentiment <= -1)
            {
                return targetName == "None"
                    ? "The statement appeared while local security concerns were rising."
                    : $"The statement appeared while tensions around {targetName} were rising.";
            }

            if (category == SocialPostCategory.Economic)
            {
                return "The statement centered on trade expectations, supply movement, or exchange terms.";
            }

            if (category == SocialPostCategory.Anomaly)
            {
                return "The statement followed an unusual incident that people were already trying to explain.";
            }

            if (!string.IsNullOrWhiteSpace(intentHint))
            {
                return "The statement was read as a signal about the faction's next diplomatic move.";
            }

            return "The statement was treated as a public position rather than casual talk.";
        }

        private static string BuildDialogueObservedReaction(
            SocialPostCategory category,
            int sentiment,
            string sourceName,
            string targetName,
            string intentHint)
        {
            if (category == SocialPostCategory.Military || sentiment <= -1)
            {
                return targetName == "None"
                    ? "Guards and caravan crews started talking as if route risk might rise again."
                    : $"Traders and guards started weighing whether contact with {targetName} was becoming more dangerous.";
            }

            if (category == SocialPostCategory.Economic)
            {
                return "Merchants and haulers began comparing whether future deals would tighten, loosen, or change price expectations.";
            }

            if (category == SocialPostCategory.Anomaly)
            {
                return "Witnesses repeated the story as a warning, and nearby settlements treated it as a sign to watch for similar incidents.";
            }

            if (!string.IsNullOrWhiteSpace(intentHint))
            {
                return $"Listeners treated the wording as a deliberate signal of {sourceName}'s next public posture.";
            }

            return "Listeners treated the wording as a public line that others would now have to answer or test.";
        }

        private static string BuildDialogueGameplayImplication(
            SocialPostCategory category,
            int sentiment,
            Faction targetFaction,
            string intentHint)
        {
            if (category == SocialPostCategory.Military || sentiment <= -1)
            {
                return "Possible pressure on security expectations, hostile intent, or future raid risk.";
            }

            if (category == SocialPostCategory.Economic)
            {
                return "Possible pressure on trade expectations, caravan tone, or future aid and exchange terms.";
            }

            if (category == SocialPostCategory.Anomaly)
            {
                return "Possible pressure on regional safety expectations and how outsiders approach the area.";
            }

            if (targetFaction == Faction.OfPlayer || !string.IsNullOrWhiteSpace(intentHint))
            {
                return "Possible pressure on diplomatic attitude, public goodwill, or future cooperation tone.";
            }

            return string.Empty;
        }

        private static bool IsConcreteDialogueFact(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string text = value.Trim();
            if (text.Length < 8)
            {
                return false;
            }

            string lowered = text.ToLowerInvariant();
            string[] blockedFragments =
            {
                "引发讨论",
                "引起讨论",
                "公开社交圈",
                "社交圈",
                "发酵",
                "波澜",
                "关注",
                "热议",
                "议论",
                "讨论",
                "风声",
                "信号",
                "口径",
                "态度",
                "立场",
                "局势",
                "传闻",
                "rumor",
                "discussion",
                "debate",
                "signal",
                "stance",
                "attitude",
                "position",
                "public circle",
                "social circle"
            };
            if (blockedFragments.Any(fragment => lowered.Contains(fragment)))
            {
                return false;
            }

            string[] concreteFragments =
            {
                "要求",
                "主张",
                "表示",
                "宣布",
                "拒绝",
                "支持",
                "反对",
                "停止",
                "继续",
                "允许",
                "禁止",
                "开放",
                "封锁",
                "停火",
                "谈判",
                "贸易",
                "援助",
                "袭击",
                "进攻",
                "威胁",
                "撤军",
                "增兵",
                "赔偿",
                "合作",
                "结盟",
                "归还",
                "交付",
                "释放",
                "trade",
                "truce",
                "aid",
                "raid",
                "attack",
                "threaten",
                "withdraw",
                "deploy",
                "compensation",
                "cooperate",
                "alliance",
                "return",
                "deliver",
                "release",
                "ban",
                "allow",
                "refuse",
                "reject",
                "support",
                "oppose",
                "demand",
                "claim",
                "announce"
            };
            if (concreteFragments.Any(fragment => lowered.Contains(fragment)))
            {
                return true;
            }

            return text.Contains("：")
                || text.Contains(":")
                || text.Contains("“")
                || text.Contains("”")
                || text.Contains("将")
                || text.Contains("会")
                || text.Contains("必须")
                || text.Contains("不得")
                || text.Contains("would")
                || text.Contains("will ")
                || text.Contains("must ")
                || text.Contains("should ");
        }

        private static string NormalizeDialogueClaimCandidate(string value)
        {
            string text = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            int firstSentenceIndex = text.IndexOfAny(new[] { '。', '！', '？', '.', '!', '?', ';', '；' });
            if (firstSentenceIndex > 0)
            {
                text = text.Substring(0, firstSentenceIndex).Trim();
            }

            string[] prefixes =
            {
                "对话内容",
                "公开对话",
                "公开声明",
                "公开表态",
                "消息称",
                "据称",
                "报道称",
                "有声音称"
            };
            foreach (string prefix in prefixes)
            {
                if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    text = text.Substring(prefix.Length).TrimStart('：', ':', '，', ',', ' ');
                }
            }

            return text.Trim().Trim('"', '“', '”');
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
            Faction attacker = ResolveFaction(report?.AttackerFactionId);
            Faction defender = ResolveFaction(report?.DefenderFactionId);
            string location = ResolveFactionStrongholdLabel(attacker, defender);
            return new List<string>
            {
                report?.Summary ?? string.Empty,
                $"Attacker: {BuildFactionFactValue(attacker, report?.AttackerFactionName)}",
                $"Defender: {BuildFactionFactValue(defender, report?.DefenderFactionName)}",
                BuildLocationFact(location),
                BuildSettlementContextFact(location, attacker, defender),
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
            Faction sourceFaction = ResolveKnownFaction(record?.KnownFactionIds, preferPlayer: false);
            Faction targetFaction = ResolveKnownFaction(record?.KnownFactionIds, preferPlayer: true);
            string location = ResolveFactionStrongholdLabel(sourceFaction, targetFaction);
            return new List<string>
            {
                record?.Summary ?? string.Empty,
                $"Event type: {record?.EventType ?? "unknown"}",
                $"Source faction: {BuildFactionFactValue(sourceFaction)}",
                $"Target faction: {BuildFactionFactValue(targetFaction, Faction.OfPlayer?.Name)}",
                BuildLocationFact(location),
                BuildSettlementContextFact(location, sourceFaction, targetFaction),
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
            Faction involvedFaction = ResolveFaction(evt?.InvolvedFactionId);
            string location = ResolveFactionStrongholdLabel(involvedFaction, null);
            return new List<string>
            {
                evt?.Description ?? string.Empty,
                $"Event type: {evt?.EventType.ToString() ?? "Unknown"}",
                $"Involved faction: {BuildFactionFactValue(involvedFaction, evt?.InvolvedFactionName)}",
                BuildLocationFact(location),
                BuildSettlementContextFact(location, involvedFaction, null),
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
            Faction sourceFaction = ResolveFaction(record?.FactionId);
            string location = ResolveFactionStrongholdLabel(sourceFaction, null);
            return new List<string>
            {
                record?.SummaryText ?? string.Empty,
                $"Source faction: {BuildFactionFactValue(sourceFaction)}",
                BuildLocationFact(location),
                BuildSettlementContextFact(location, sourceFaction, null),
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

        private static string BuildLocationFact(string location)
        {
            return string.IsNullOrWhiteSpace(location)
                ? string.Empty
                : $"Stronghold/settlement explicitly tied to this event: {location}";
        }

        private static string BuildSettlementContextFact(string location, Faction primaryFaction, Faction secondaryFaction)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                return string.Empty;
            }

            Faction owner = primaryFaction != null && !primaryFaction.IsPlayer
                ? primaryFaction
                : secondaryFaction != null && !secondaryFaction.IsPlayer
                    ? secondaryFaction
                    : primaryFaction ?? secondaryFaction;
            if (owner == null)
            {
                return $"This location is a concrete settlement name and should be referenced naturally in the article: {location}";
            }

            return $"Settlement context: {location} is a concrete stronghold/settlement associated with {owner.Name}; weave it naturally into the article body instead of leaving it as metadata.";
        }

        private static string BuildFactionFactValue(Faction faction, string fallbackName = null)
        {
            string displayName = string.IsNullOrWhiteSpace(faction?.Name)
                ? (string.IsNullOrWhiteSpace(fallbackName) ? "Unknown" : fallbackName.Trim())
                : faction.Name;
            if (faction?.def == null)
            {
                return displayName;
            }

            string tech = faction.def.techLevel.ToString();
            string kind = faction.def.label ?? faction.def.defName ?? string.Empty;
            string relation = Faction.OfPlayer == null || faction.IsPlayer
                ? string.Empty
                : $", relation to player: {faction.RelationKindWith(Faction.OfPlayer)}";
            return string.IsNullOrWhiteSpace(kind)
                ? $"{displayName} (tech level: {tech}{relation})"
                : $"{displayName} ({kind}, tech level: {tech}{relation})";
        }

        private static string ResolveFactionStrongholdLabel(Faction primaryFaction, Faction secondaryFaction)
        {
            Faction resolvedFaction = primaryFaction != null && !primaryFaction.IsPlayer
                ? primaryFaction
                : secondaryFaction != null && !secondaryFaction.IsPlayer
                    ? secondaryFaction
                    : null;
            if (resolvedFaction == null)
            {
                return string.Empty;
            }

            int homeTile = Find.AnyPlayerHomeMap?.Tile ?? -1;
            IEnumerable<Settlement> candidateSettlements = Find.WorldObjects?.Settlements?
                .Where(settlement => settlement?.Faction == resolvedFaction && settlement.Tile >= 0)
                ?? Enumerable.Empty<Settlement>();
            List<Settlement> settlements = Enumerable.OrderBy<Settlement, int>(
                    candidateSettlements,
                    settlement => homeTile < 0
                        ? settlement.Tile
                        : Find.WorldGrid.TraversalDistanceBetween(homeTile, settlement.Tile))
                .ThenBy(settlement => settlement.ID)
                .Take(3)
                .ToList();
            if (settlements.Count == 0)
            {
                return string.Empty;
            }

            Settlement selected = settlements.RandomElement();
            return selected?.LabelCap ?? string.Empty;
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
