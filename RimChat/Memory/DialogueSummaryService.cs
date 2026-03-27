using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RimChat.AI;
using RimChat.Config;
using RimChat.Persistence;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.Memory
{
    /// <summary>/// Dependencies: AIChatServiceAsync, LeaderMemoryManager, diplomacy/RPG dialogue message models.
 /// Responsibility: create and persist cross-channel summaries with rule-first strategy and LLM fallback.
 ///</summary>
    public static class DialogueSummaryService
    {
        public const int MaxSummaryPoolPerType = 20;
        public const int MaxInjectedSummaryItems = 6;
        public const int MaxInjectedChars = 2200;

        private const float LowConfidenceThreshold = 0.65f;
        private const int MaxKeyFactsPerSummary = 3;

        public static void TryRecordDiplomacySessionSummary(
            Faction faction,
            List<DialogueMessageData> allMessages,
            int baselineMessageCount)
        {
            if (faction == null || faction.IsPlayer || allMessages == null || allMessages.Count <= baselineMessageCount)
            {
                return;
            }

            int start = Mathf.Clamp(baselineMessageCount, 0, allMessages.Count);
            List<DialogueMessageData> delta = allMessages.Skip(start).ToList();
            if (delta.Count == 0)
            {
                return;
            }

            CrossChannelSummaryRecord record = BuildRuleDiplomacySummary(faction, delta);
            if (record == null || string.IsNullOrWhiteSpace(record.SummaryText))
            {
                return;
            }

            LeaderMemoryManager.Instance.AddDiplomacySessionSummary(faction, record, MaxSummaryPoolPerType);
            TryQueueLlmFallback(faction, record, BuildDiplomacyFallbackContext(faction, delta));
        }

        public static void TryRecordRpgDepartSummary(Pawn pawn, RpgDialogueTraceSnapshot trace)
        {
            if (pawn == null || trace == null || trace.Faction == null || trace.Faction.IsPlayer || trace.Faction.defeated)
            {
                return;
            }

            CrossChannelSummaryRecord record = BuildRuleRpgDepartSummary(trace);
            if (record == null || string.IsNullOrWhiteSpace(record.SummaryText))
            {
                return;
            }

            LeaderMemoryManager.Instance.AddRpgDepartSummary(trace.Faction, record, MaxSummaryPoolPerType);
            TryQueueLlmFallback(trace.Faction, record, BuildRpgFallbackContext(trace));
        }

        public static void TryPushRpgSessionSummaryOnClose(Pawn initiator, Pawn target, List<ChatMessageData> chatHistory)
        {
            if (!TryBuildRpgSessionSummaryOnClose(initiator, target, chatHistory, out CrossChannelSummaryRecord record))
            {
                return;
            }
        }

        public static string BuildRpgDynamicFactionMemoryBlock(Faction faction, Pawn targetPawn)
        {
            if (faction == null || faction.IsPlayer || faction.defeated || targetPawn == null)
            {
                return string.Empty;
            }

            FactionLeaderMemory memory = LeaderMemoryManager.Instance.GetMemory(faction);
            if (memory == null)
            {
                return string.Empty;
            }

            int targetPawnId = targetPawn.thingIDNumber;
            if (targetPawnId <= 0)
            {
                return string.Empty;
            }

            List<CrossChannelSummaryRecord> summaries = CollectSortedSummaries(memory, targetPawnId);
            if (summaries.Count == 0 && (memory.SignificantEvents == null || memory.SignificantEvents.Count == 0))
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.AppendLine("=== DYNAMIC FACTION MEMORY (CROSS-CHANNEL, TARGET-PAWN SCOPED) ===");
            sb.AppendLine("Use only target-pawn scoped memories to maintain continuity with the player. Do not overwrite your persona.");

            int remain = MaxInjectedChars - sb.Length;
            int emitted = 0;
            for (int i = 0; i < summaries.Count && emitted < MaxInjectedSummaryItems && remain > 60; i++)
            {
                CrossChannelSummaryRecord item = summaries[i];
                if (item == null || string.IsNullOrWhiteSpace(item.SummaryText))
                {
                    continue;
                }

                string line = FormatSummaryLine(item);
                if (line.Length > remain)
                {
                    line = TrimToMax(line, remain);
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    sb.AppendLine(line);
                    remain = MaxInjectedChars - sb.Length;
                    emitted++;
                }
            }

            AppendSignificantEventHints(sb, memory, ref remain);
            return sb.ToString().Trim();
        }

        private static void AppendSignificantEventHints(StringBuilder sb, FactionLeaderMemory memory, ref int remain)
        {
            if (remain <= 40 || memory?.SignificantEvents == null || memory.SignificantEvents.Count == 0)
            {
                return;
            }

            var events = memory.SignificantEvents
                .OrderByDescending(e => e.OccurredTick)
                .Take(3)
                .ToList();
            if (events.Count == 0)
            {
                return;
            }

            string header = "Recent major events:";
            if (header.Length < remain)
            {
                sb.AppendLine(header);
                remain = MaxInjectedChars - sb.Length;
            }

            for (int i = 0; i < events.Count && remain > 30; i++)
            {
                SignificantEventMemory evt = events[i];
                string line = $"- {evt.EventType}: {TrimToMax(evt.Description, 120)}";
                if (line.Length > remain)
                {
                    line = TrimToMax(line, remain);
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    sb.AppendLine(line);
                    remain = MaxInjectedChars - sb.Length;
                }
            }
        }

        private static List<CrossChannelSummaryRecord> CollectSortedSummaries(FactionLeaderMemory memory, int targetPawnId)
        {
            var combined = new List<CrossChannelSummaryRecord>();
            if (memory.DiplomacySessionSummaries != null)
            {
                combined.AddRange(memory.DiplomacySessionSummaries);
            }
            if (memory.RpgDepartSummaries != null)
            {
                combined.AddRange(memory.RpgDepartSummaries);
            }

            int nowTick = Find.TickManager?.TicksGame ?? 0;

            return combined
                .Where(x => IsSummaryScopedToTargetPawn(x, targetPawnId))
                .OrderByDescending(x => ScoreForRpgInjection(x, nowTick, targetPawnId))
                .ThenByDescending(x => x.GameTick)
                .ToList();
        }

        private static bool IsSummaryScopedToTargetPawn(CrossChannelSummaryRecord record, int targetPawnId)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.SummaryText) || targetPawnId <= 0)
            {
                return false;
            }

            return record.PawnLoadId == targetPawnId;
        }

        private static float ScoreForRpgInjection(CrossChannelSummaryRecord record, int nowTick, int targetPawnId)
        {
            float sourceWeight = record.Source == CrossChannelSummarySource.DiplomacySession ? 1000f : 800f;
            float agePenalty = Mathf.Max(0f, nowTick - record.GameTick) / 1800f;
            float pawnBonus = (targetPawnId >= 0 && record.PawnLoadId == targetPawnId) ? 120f : 0f;
            return sourceWeight + record.Confidence * 100f + pawnBonus - agePenalty;
        }

        private static string FormatSummaryLine(CrossChannelSummaryRecord record)
        {
            string source = record.Source == CrossChannelSummarySource.DiplomacySession ? "Diplomacy" : "RPG-Depart";
            string text = TrimToMax(record.SummaryText, 220);
            if (record.Source == CrossChannelSummarySource.RpgDepart)
            {
                text = SanitizeRpgDepartSummaryText(text);
                return $"- [{source}] {text}";
            }

            if (record.KeyFacts == null || record.KeyFacts.Count == 0)
            {
                return $"- [{source}] {text}";
            }

            string facts = string.Join("; ", record.KeyFacts.Where(f => !string.IsNullOrWhiteSpace(f)).Take(2).Select(f => TrimToMax(f, 70)));
            if (string.IsNullOrWhiteSpace(facts))
            {
                return $"- [{source}] {text}";
            }

            return $"- [{source}] {text} | facts: {facts}";
        }

        private static CrossChannelSummaryRecord BuildRuleDiplomacySummary(Faction faction, List<DialogueMessageData> deltaMessages)
        {
            List<DialogueMessageData> usable = deltaMessages
                .Where(m => m != null && !m.IsSystemMessage() && !string.IsNullOrWhiteSpace(m.message))
                .ToList();
            if (usable.Count == 0)
            {
                return null;
            }

            string factionId = BuildFactionId(faction);
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            string playerLast = usable.LastOrDefault(m => m.isPlayer)?.message ?? string.Empty;
            string aiLast = usable.LastOrDefault(m => !m.isPlayer)?.message ?? string.Empty;
            List<string> topics = ExtractTopics(usable.Select(m => m.message));
            List<string> facts = BuildKeyFacts(usable.Select(m => m.isPlayer ? $"Player: {m.message}" : $"Faction: {m.message}"));
            string topicText = topics.Count > 0 ? string.Join(", ", topics) : "general negotiation";

            string summary = $"Session touched {topicText}. " +
                             $"Last player intent: {TrimToMax(playerLast, 80)}. " +
                             $"Last faction stance: {TrimToMax(aiLast, 80)}.";

            float confidence = EstimateConfidence(usable.Count, topics.Count, !string.IsNullOrWhiteSpace(playerLast), !string.IsNullOrWhiteSpace(aiLast));
            string hashSeed = $"{factionId}|diplomacy|{usable.Count}|{usable.Last().GetGameTick()}|{summary}";

            return new CrossChannelSummaryRecord
            {
                Source = CrossChannelSummarySource.DiplomacySession,
                FactionId = factionId,
                PawnLoadId = -1,
                PawnName = string.Empty,
                SummaryText = summary,
                KeyFacts = facts,
                GameTick = currentTick,
                Confidence = confidence,
                ContentHash = ComputeHash(hashSeed),
                IsLlmFallback = false,
                CreatedTimestamp = DateTime.UtcNow.Ticks
            };
        }

        private static CrossChannelSummaryRecord BuildRuleRpgDepartSummary(RpgDialogueTraceSnapshot trace)
        {
            List<RpgDialogueTurn> turns = trace.Turns?
                .Where(t => t != null && !string.IsNullOrWhiteSpace(t.Text))
                .ToList() ?? new List<RpgDialogueTurn>();
            if (turns.Count == 0)
            {
                return null;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            string factionId = BuildFactionId(trace.Faction);
            string pawnName = trace.Pawn?.LabelShort ?? trace.Pawn?.Name?.ToStringShort ?? "UnknownPawn";
            List<string> topics = ExtractTopics(turns.Select(t => t.Text));
            string finalNpcText = turns.LastOrDefault(t => !t.IsPlayer)?.Text ?? string.Empty;
            string playerIntent = DescribePlayerIntent(turns);
            string npcFinalTone = DescribeNpcTone(finalNpcText);
            List<string> facts = BuildRpgSummaryFacts(topics, playerIntent, npcFinalTone);

            string summary = $"Pawn {pawnName} departed map after RPG dialogue. " +
                              $"Main topics: {(topics.Count > 0 ? string.Join(", ", topics) : "daily interaction")}. " +
                              $"Player intent trend: {playerIntent}. " +
                              $"NPC final tone: {npcFinalTone}.";

            float confidence = EstimateConfidence(turns.Count, topics.Count, true, !string.IsNullOrWhiteSpace(finalNpcText));
            string hashSeed = $"{factionId}|rpg_depart|{trace.Pawn?.thingIDNumber ?? -1}|{trace.LastInteractionTick}|{turns.Count}";

            return new CrossChannelSummaryRecord
            {
                Source = CrossChannelSummarySource.RpgDepart,
                FactionId = factionId,
                PawnLoadId = trace.Pawn?.thingIDNumber ?? -1,
                PawnName = pawnName,
                SummaryText = summary,
                KeyFacts = facts,
                GameTick = currentTick,
                Confidence = confidence,
                ContentHash = ComputeHash(hashSeed),
                IsLlmFallback = false,
                CreatedTimestamp = DateTime.UtcNow.Ticks
            };
        }

        private static bool TryBuildRpgSessionSummaryOnClose(
            Pawn initiator,
            Pawn target,
            List<ChatMessageData> chatHistory,
            out CrossChannelSummaryRecord record)
        {
            record = null;
            if (target == null || chatHistory == null || chatHistory.Count == 0)
            {
                return false;
            }

            List<RpgDialogueTurn> turns = chatHistory
                .Where(message => message != null &&
                    !string.IsNullOrWhiteSpace(message.content) &&
                    !string.Equals(message.role, "system", StringComparison.OrdinalIgnoreCase))
                .Select(message => new RpgDialogueTurn
                {
                    IsPlayer = string.Equals(message.role, "user", StringComparison.OrdinalIgnoreCase),
                    Text = CleanupRpgCloseTurnText(message.content),
                    GameTick = Find.TickManager?.TicksGame ?? 0
                })
                .Where(turn => !string.IsNullOrWhiteSpace(turn.Text))
                .ToList();

            if (turns.Count == 0)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            Faction faction = target.Faction ?? initiator?.Faction;
            string factionId = BuildFactionId(faction);
            string pawnName = target.LabelShort ?? target.Name?.ToStringShort ?? "UnknownPawn";
            List<string> topics = ExtractTopics(turns.Select(t => t.Text));
            string finalNpcText = turns.LastOrDefault(t => !t.IsPlayer)?.Text ?? string.Empty;
            string playerIntent = DescribePlayerIntent(turns);
            string npcFinalTone = DescribeNpcTone(finalNpcText);
            List<string> facts = BuildRpgSummaryFacts(topics, playerIntent, npcFinalTone);

            string summary = $"RPG dialogue session with {pawnName} ended. " +
                              $"Main topics: {(topics.Count > 0 ? string.Join(", ", topics) : "daily interaction")}. " +
                              $"Player intent trend: {playerIntent}. " +
                              $"NPC final tone: {npcFinalTone}.";

            float confidence = EstimateConfidence(turns.Count, topics.Count, true, !string.IsNullOrWhiteSpace(finalNpcText));
            string hashSeed = $"{factionId}|rpg_close|{target.thingIDNumber}|{turns.Count}|{summary}";

            record = new CrossChannelSummaryRecord
            {
                Source = CrossChannelSummarySource.RpgDepart,
                FactionId = factionId,
                PawnLoadId = target.thingIDNumber,
                PawnName = pawnName,
                SummaryText = summary,
                KeyFacts = facts,
                GameTick = currentTick,
                Confidence = confidence,
                ContentHash = ComputeHash(hashSeed),
                IsLlmFallback = false,
                CreatedTimestamp = DateTime.UtcNow.Ticks
            };

            return true;
        }

        private static string CleanupRpgCloseTurnText(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return string.Empty;
            }

            string text = rawText.Trim();
            int codeFence = text.IndexOf("```", StringComparison.Ordinal);
            if (codeFence > 0)
            {
                text = text.Substring(0, codeFence).Trim();
            }

            text = StripParserJsonTail(text);
            return TrimToMax(text, 180);
        }

        private static string StripParserJsonTail(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            int cutIndex = FindFirstJsonMarkerIndex(text);
            if (cutIndex < 0)
            {
                return text;
            }

            return text.Substring(0, cutIndex).Trim();
        }

        private static int FindFirstJsonMarkerIndex(string text)
        {
            string[] markers =
            {
                "{\"actions\"",
                "{ \"actions\"",
                "{\"action\"",
                "{ \"action\""
            };

            int hit = -1;
            for (int i = 0; i < markers.Length; i++)
            {
                int idx = text.IndexOf(markers[i], StringComparison.OrdinalIgnoreCase);
                if (idx >= 0 && (hit < 0 || idx < hit))
                {
                    hit = idx;
                }
            }

            return hit;
        }

        private static void TryQueueLlmFallback(Faction faction, CrossChannelSummaryRecord record, string context)
        {
            if (faction == null || record == null || record.Confidence >= LowConfidenceThreshold)
            {
                return;
            }

            if (!AIChatServiceAsync.Instance.IsConfigured())
            {
                return;
            }

            RimTalkPromptChannel rootChannel = record.Source == CrossChannelSummarySource.RpgDepart
                ? RimTalkPromptChannel.Rpg
                : RimTalkPromptChannel.Diplomacy;
            DialogueScenarioContext scenarioContext = rootChannel == RimTalkPromptChannel.Rpg
                ? DialogueScenarioContext.CreateRpg(null, null, false, new[] { "channel:summary_generation", "source:rpg_fallback" })
                : DialogueScenarioContext.CreateDiplomacy(faction, false, new[] { "channel:summary_generation", "source:diplomacy_fallback" });
            scenarioContext.Faction = faction;
            var variables = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["dialogue.primary_objective"] = "Summarize memory context into one sentence and up to three key facts.",
                ["dialogue.optional_followup"] = "Output plain text only; do not use markdown code blocks.",
                ["dialogue.latest_unresolved_intent"] = string.Empty,
                ["dialogue.summary_context"] = context ?? string.Empty,
                ["world.faction.name"] = faction?.Name ?? string.Empty
            };
            string systemPrompt = PromptPersistenceService.Instance.BuildUnifiedChannelSystemPrompt(
                rootChannel,
                RimTalkPromptEntryChannelCatalog.SummaryGeneration,
                scenarioContext,
                null,
                variables,
                "summary_context",
                context ?? string.Empty);
            var messages = new List<ChatMessageData>
            {
                new ChatMessageData
                {
                    role = "system",
                    content = systemPrompt
                }
            };
            DialogueUsageChannel usageChannel = rootChannel == RimTalkPromptChannel.Rpg
                ? DialogueUsageChannel.Rpg
                : DialogueUsageChannel.Diplomacy;

            AIChatServiceAsync.Instance.SendChatRequestAsync(
                messages,
                onSuccess: response =>
                {
                    if (string.IsNullOrWhiteSpace(response))
                    {
                        return;
                    }

                    var upgraded = record.Clone();
                    ParseFallbackText(response, out string summary, out List<string> facts);
                    if (!string.IsNullOrWhiteSpace(summary))
                    {
                        upgraded.SummaryText = TrimToMax(summary, 280);
                    }
                    if (facts.Count > 0)
                    {
                        upgraded.KeyFacts = facts;
                    }

                    upgraded.Confidence = Mathf.Max(record.Confidence, 0.72f);
                    upgraded.IsLlmFallback = true;
                    upgraded.CreatedTimestamp = DateTime.UtcNow.Ticks;

                    if (upgraded.Source == CrossChannelSummarySource.DiplomacySession)
                    {
                        LeaderMemoryManager.Instance.UpsertDiplomacySessionSummary(faction, upgraded, MaxSummaryPoolPerType);
                    }
                    else if (upgraded.Source == CrossChannelSummarySource.RpgDepart)
                    {
                        LeaderMemoryManager.Instance.UpsertRpgDepartSummary(faction, upgraded, MaxSummaryPoolPerType);
                    }
                },
                onError: _ => { },
                usageChannel: usageChannel,
                debugSource: AIRequestDebugSource.MemorySummary);
        }

        private static void ParseFallbackText(string raw, out string summary, out List<string> facts)
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
                        facts.Add(TrimToMax(fact, 80));
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(summary))
            {
                summary = TrimToMax(raw.Trim(), 220);
            }
            if (facts.Count > MaxKeyFactsPerSummary)
            {
                facts = facts.Take(MaxKeyFactsPerSummary).ToList();
            }
        }

        private static string BuildDiplomacyFallbackContext(Faction faction, List<DialogueMessageData> delta)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Faction: {faction?.Name ?? "Unknown"}");
            sb.AppendLine("Context: diplomacy session closed; summarize new messages only.");
            List<DialogueMessageData> recentMessages = delta
                .Where(x => x != null && !x.IsSystemMessage() && !string.IsNullOrWhiteSpace(x.message))
                .ToList();
            int start = Math.Max(0, recentMessages.Count - 10);
            for (int i = start; i < recentMessages.Count; i++)
            {
                DialogueMessageData msg = recentMessages[i];
                string role = msg.isPlayer ? "Player" : "Faction";
                sb.AppendLine($"{role}: {TrimToMax(msg.message, 180)}");
            }
            return sb.ToString();
        }

        private static string BuildRpgFallbackContext(RpgDialogueTraceSnapshot trace)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Faction: {trace.Faction?.Name ?? "Unknown"}");
            sb.AppendLine($"Pawn: {trace.Pawn?.LabelShort ?? "UnknownPawn"}");
            sb.AppendLine("Context: pawn is exiting map; summarize recent RPG interaction.");
            List<RpgDialogueTurn> turns = trace.Turns ?? new List<RpgDialogueTurn>();
            int start = Math.Max(0, turns.Count - 10);
            for (int i = start; i < turns.Count; i++)
            {
                RpgDialogueTurn turn = turns[i];
                string role = turn.IsPlayer ? "Player" : "NPC";
                sb.AppendLine($"{role}: {TrimToMax(turn.Text, 180)}");
            }
            return sb.ToString();
        }

        private static List<string> BuildKeyFacts(IEnumerable<string> lines)
        {
            return lines
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => TrimToMax(x.Trim(), 90))
                .Distinct()
                .Take(MaxKeyFactsPerSummary)
                .ToList();
        }

        private static List<string> BuildRpgSummaryFacts(List<string> topics, string playerIntent, string npcFinalTone)
        {
            return new List<string>
            {
                "topics: " + (topics != null && topics.Count > 0 ? string.Join(", ", topics.Take(3)) : "daily interaction"),
                "player_intent: " + (string.IsNullOrWhiteSpace(playerIntent) ? "neutral" : playerIntent),
                "npc_final_tone: " + (string.IsNullOrWhiteSpace(npcFinalTone) ? "neutral" : npcFinalTone)
            };
        }

        private static string DescribePlayerIntent(List<RpgDialogueTurn> turns)
        {
            string lastPlayer = turns?
                .Where(t => t != null && t.IsPlayer && !string.IsNullOrWhiteSpace(t.Text))
                .Select(t => t.Text)
                .LastOrDefault() ?? string.Empty;
            if (ContainsAny(lastPlayer, "kill", "murder", "attack", "threat", "杀", "攻击", "威胁")) return "hostile";
            if (ContainsAny(lastPlayer, "help", "ally", "peace", "trade", "合作", "和平", "交易")) return "cooperative";
            return "neutral";
        }

        private static string DescribeNpcTone(string text)
        {
            if (ContainsAny(text, "警惕", "后退", "拒绝", "滚开", "threat", "stay away", "guarded")) return "guarded";
            if (ContainsAny(text, "友好", "欢迎", "感谢", "happy", "glad", "friendly")) return "friendly";
            return "neutral";
        }

        private static bool ContainsAny(string text, params string[] keywords)
        {
            string lower = (text ?? string.Empty).ToLowerInvariant();
            for (int i = 0; i < keywords.Length; i++)
            {
                if (lower.Contains((keywords[i] ?? string.Empty).ToLowerInvariant()))
                {
                    return true;
                }
            }
            return false;
        }

        private static string SanitizeRpgDepartSummaryText(string text)
        {
            string value = text ?? string.Empty;
            int idx = value.IndexOf("Final NPC signal:", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                idx = value.IndexOf("Last NPC signal:", StringComparison.OrdinalIgnoreCase);
            }

            if (idx >= 0)
            {
                value = value.Substring(0, idx).TrimEnd(' ', '.', ';') + ".";
            }

            return TrimToMax(value, 220);
        }

        private static List<string> ExtractTopics(IEnumerable<string> texts)
        {
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string text in texts)
            {
                string lower = (text ?? string.Empty).ToLowerInvariant();
                AddTopicIfContains(lower, tags, "trade", "trade", "caravan", "goods", "交易", "商队");
                AddTopicIfContains(lower, tags, "peace", "peace", "ally", "ceasefire", "和平", "盟友");
                AddTopicIfContains(lower, tags, "threat", "threat", "war", "raid", "attack", "威胁", "战争", "袭击");
                AddTopicIfContains(lower, tags, "aid", "aid", "help", "support", "救援", "支援");
                AddTopicIfContains(lower, tags, "trust", "trust", "respect", "favor", "信任", "尊重");
            }
            return tags.Take(4).ToList();
        }

        private static void AddTopicIfContains(string lowerText, HashSet<string> tags, string label, params string[] keywords)
        {
            for (int i = 0; i < keywords.Length; i++)
            {
                if (lowerText.IndexOf(keywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    tags.Add(label);
                    return;
                }
            }
        }

        private static float EstimateConfidence(int turnCount, int topicCount, bool hasPlayer, bool hasNpc)
        {
            float score = 0.32f;
            score += Mathf.Min(0.42f, turnCount * 0.08f);
            score += Mathf.Min(0.12f, topicCount * 0.04f);
            if (hasPlayer) score += 0.06f;
            if (hasNpc) score += 0.08f;
            return Mathf.Clamp(score, 0.05f, 0.95f);
        }

        private static string BuildFactionId(Faction faction)
        {
            if (faction == null)
            {
                return string.Empty;
            }

            if (faction.def != null && !string.IsNullOrEmpty(faction.def.defName))
            {
                return $"{faction.def.defName}_{faction.loadID}";
            }

            return $"custom_{faction.loadID}";
        }

        private static string ComputeHash(string text)
        {
            string input = text ?? string.Empty;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(input);
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }

        private static string TrimToMax(string value, int maxLen)
        {
            if (string.IsNullOrWhiteSpace(value) || maxLen <= 0)
            {
                return string.Empty;
            }

            string text = value.Trim();
            if (text.Length <= maxLen)
            {
                return text;
            }

            if (maxLen <= 3)
            {
                return text.Substring(0, maxLen);
            }

            return text.Substring(0, maxLen - 3) + "...";
        }
    }
}
