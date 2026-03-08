using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimChat.Config;
using RimChat.Core;
using RimChat.Memory;

namespace RimChat.AI
{
    /// <summary>/// Dependencies: ChatMessageData, DialogueMessageData.
 /// Responsibility: build token-efficient dialogue context with 10/20/25 tier compression and event-sentence summaries.
 ///</summary>
    public static class DialogueContextCompressionService
    {
        private const string SystemRole = "system";
        private const string UserRole = "user";
        private const string AssistantRole = "assistant";

        private static readonly Dictionary<string, string[]> TopicKeywords = new Dictionary<string, string[]>
        {
            { "trade", new[] { "trade", "caravan", "goods", "sell", "buy", "\u4ea4\u6613", "\u5546\u961f", "\u5408\u540c", "\u8ba2\u5355" } },
            { "peace", new[] { "peace", "ceasefire", "ally", "truce", "\u548c\u5e73", "\u505c\u706b", "\u8bae\u548c" } },
            { "threat", new[] { "war", "raid", "attack", "threat", "hostile", "\u6218\u4e89", "\u88ad\u51fb", "\u5a01\u80c1", "\u654c\u5bf9" } },
            { "aid", new[] { "aid", "help", "support", "rescue", "\u63f4\u52a9", "\u5e2e\u52a9", "\u652f\u63f4" } },
            { "trust", new[] { "trust", "respect", "favor", "\u4fe1\u4efb", "\u5c0a\u91cd", "\u597d\u611f" } }
        };

        private static readonly Dictionary<string, string> TopicLabelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "trade", "\u4ea4\u6613" },
            { "peace", "\u548c\u5e73" },
            { "threat", "\u51b2\u7a81" },
            { "aid", "\u63f4\u52a9" },
            { "trust", "\u5173\u7cfb" },
            { "general", "\u8fd1\u51b5" }
        };

        private static readonly List<IntentPattern> IntentPatterns = new List<IntentPattern>
        {
            new IntentPattern("\u8bf7\u6c42\u5546\u961f", 5, "\u5546\u961f", "caravan", "trade caravan"),
            new IntentPattern("\u6c42\u5a5a", 5, "\u6c42\u5a5a", "propose marriage", "marriage proposal", "marry", "\u7ed3\u5a5a", "\u5ac1\u7ed9"),
            new IntentPattern("\u5f00\u59cb\u604b\u7231", 4, "\u604b\u7231", "romance", "date", "\u7ea6\u4f1a"),
            new IntentPattern("\u62d2\u7edd\u8bf7\u6c42", 4, "\u62d2\u7edd", "\u4e0d\u884c", "\u4e0d\u540c\u610f", "decline", "cannot", "can't", "won't"),
            new IntentPattern("\u53d1\u51fa\u5a01\u80c1", 4, "\u5a01\u80c1", "\u6eda\u5f00", "\u79bb\u5f00", "\u5f00\u6218", "\u88ad\u51fb", "threat", "war", "raid", "attack"),
            new IntentPattern("\u8bf7\u6c42\u63f4\u52a9", 3, "\u63f4\u52a9", "\u5e2e\u52a9", "\u652f\u63f4", "aid", "help", "support"),
            new IntentPattern("\u8ba8\u8bba\u4ea4\u6613", 2, "\u4ea4\u6613", "\u8d38\u6613", "\u8ba2\u5355", "\u5408\u540c", "trade", "buy", "sell", "contract", "order"),
            new IntentPattern("\u8bf7\u6c42\u548c\u5e73", 2, "\u548c\u5e73", "\u505c\u706b", "\u8bae\u548c", "peace", "truce", "ceasefire")
        };

        public static List<ChatMessageData> BuildFromDialogueMessages(
            IEnumerable<DialogueMessageData> messages,
            DialogueCompressionOptions options = null)
        {
            List<DialogueCompressionTurn> turns = ConvertFromDialogueMessages(messages);
            return BuildFromTurns(turns, ResolveOptions(options));
        }

        public static List<ChatMessageData> BuildFromChatMessages(
            IEnumerable<ChatMessageData> messages,
            DialogueCompressionOptions options = null)
        {
            List<DialogueCompressionTurn> turns = ConvertFromChatMessages(messages);
            return BuildFromTurns(turns, ResolveOptions(options));
        }

        private static DialogueCompressionOptions ResolveOptions(DialogueCompressionOptions options)
        {
            DialogueCompressionOptions resolved = options ?? DialogueCompressionOptions.Default;
            return resolved ?? new DialogueCompressionOptions();
        }

        private static List<ChatMessageData> BuildFromTurns(
            List<DialogueCompressionTurn> turns,
            DialogueCompressionOptions options)
        {
            var result = new List<ChatMessageData>();
            if (turns == null || turns.Count == 0)
            {
                return result;
            }

            if (!options.Enabled)
            {
                result.AddRange(ToChatMessages(turns));
                return result;
            }

            int keepRecent = Math.Max(1, options.KeepRecentTurns);
            if (turns.Count <= keepRecent)
            {
                result.AddRange(ToChatMessages(turns));
                return result;
            }

            int recentStart = turns.Count - keepRecent;
            List<DialogueCompressionTurn> recent = turns.Skip(recentStart).ToList();
            List<DialogueCompressionSegment> segments = BuildTierSegments(turns, options);
            string summaryMessage = BuildSummaryMessage(segments, options);

            if (!string.IsNullOrWhiteSpace(summaryMessage))
            {
                result.Add(new ChatMessageData { role = SystemRole, content = summaryMessage });
            }

            result.AddRange(ToChatMessages(recent));
            return result;
        }

        private static List<DialogueCompressionSegment> BuildTierSegments(
            List<DialogueCompressionTurn> turns,
            DialogueCompressionOptions options)
        {
            var segments = new List<DialogueCompressionSegment>();
            int totalTurns = turns.Count;
            int keepRecent = Math.Max(1, options.KeepRecentTurns);
            int olderEnd = totalTurns - keepRecent - 1;
            if (olderEnd < 0)
            {
                return segments;
            }

            int tier1Min = keepRecent + 1;
            int tier1Max = Math.Max(tier1Min, options.SecondaryTierStart - 1);
            int tier2Min = Math.Max(tier1Max + 1, options.SecondaryTierStart);
            int tier2Max = Math.Max(tier2Min, options.TertiaryTierStart - 1);
            int tier3Min = Math.Max(tier2Max + 1, options.TertiaryTierStart);

            TryCreateTierSegment(turns, totalTurns, olderEnd, tier3Min, totalTurns, 3, options, segments);
            TryCreateTierSegment(turns, totalTurns, olderEnd, tier2Min, tier2Max, 2, options, segments);
            TryCreateTierSegment(turns, totalTurns, olderEnd, tier1Min, tier1Max, 1, options, segments);

            return segments
                .OrderBy(segment => segment.StartIndex)
                .ToList();
        }

        private static void TryCreateTierSegment(
            List<DialogueCompressionTurn> turns,
            int totalTurns,
            int olderEnd,
            int minRecency,
            int maxRecency,
            int mark,
            DialogueCompressionOptions options,
            List<DialogueCompressionSegment> output)
        {
            if (output == null || turns == null || turns.Count == 0 || minRecency > maxRecency)
            {
                return;
            }

            if (mark > Math.Max(1, options.MaxCompressionMark))
            {
                return;
            }

            int startIndex = Math.Max(0, totalTurns - maxRecency);
            int endIndex = Math.Min(olderEnd, totalTurns - minRecency);
            if (startIndex > endIndex || endIndex < 0)
            {
                return;
            }

            List<DialogueCompressionTurn> slice = turns
                .Skip(startIndex)
                .Take(endIndex - startIndex + 1)
                .ToList();

            string summary = BuildSegmentSummary(slice, options);
            if (string.IsNullOrWhiteSpace(summary))
            {
                return;
            }

            output.Add(new DialogueCompressionSegment
            {
                StartIndex = startIndex,
                EndIndex = endIndex,
                CompressionMark = mark,
                Summary = summary
            });
        }

        private static string BuildSummaryMessage(
            List<DialogueCompressionSegment> segments,
            DialogueCompressionOptions options)
        {
            if (segments == null || segments.Count == 0)
            {
                return string.Empty;
            }

            int tier1Min = options.KeepRecentTurns + 1;
            int tier1Max = Math.Max(tier1Min, options.SecondaryTierStart - 1);
            int tier2Min = options.SecondaryTierStart;
            int tier2Max = Math.Max(tier2Min, options.TertiaryTierStart - 1);
            int tier3Min = options.TertiaryTierStart;

            var sb = new StringBuilder();
            sb.AppendLine("=== COMPRESSED DIALOGUE CONTEXT ===");
            sb.AppendLine(
                $"Policy: keep latest {options.KeepRecentTurns} turns in full; " +
                $"{tier1Min}-{tier1Max}=>+1, {tier2Min}-{tier2Max}=>+2, >={tier3Min}=>+3.");

            int emitted = 0;
            for (int i = 0; i < segments.Count && emitted < options.MaxSummaryLines; i++)
            {
                DialogueCompressionSegment segment = segments[i];
                sb.AppendLine($"- [+{segment.CompressionMark}] #{segment.StartIndex + 1}-{segment.EndIndex + 1}: {segment.Summary}");
                emitted++;
            }

            return sb.ToString().TrimEnd();
        }

        private static string BuildSegmentSummary(List<DialogueCompressionTurn> slice, DialogueCompressionOptions options)
        {
            if (slice == null || slice.Count == 0)
            {
                return string.Empty;
            }

            List<string> events = ExtractEventSentences(slice, options.MaxEventsPerSegment);
            if (events.Count > 0)
            {
                return string.Join("\uff1b", events);
            }

            return BuildFallbackTopicSentence(slice);
        }

        private static List<string> ExtractEventSentences(List<DialogueCompressionTurn> turns, int maxEvents)
        {
            var detected = new List<DetectedEvent>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int cappedMax = Math.Max(1, maxEvents);

            for (int i = turns.Count - 1; i >= 0; i--)
            {
                DialogueCompressionTurn turn = turns[i];
                if (!TryDetectIntent(turn?.Content, out string intent, out int score))
                {
                    continue;
                }

                string dedupeKey = $"{turn.Role}:{intent}";
                if (!seen.Add(dedupeKey))
                {
                    continue;
                }

                detected.Add(new DetectedEvent
                {
                    Index = i,
                    Score = score,
                    Sentence = BuildEventSentence(turn.Role, intent)
                });
            }

            if (detected.Count == 0)
            {
                return new List<string>();
            }

            return detected
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.Index)
                .Take(cappedMax)
                .OrderBy(item => item.Index)
                .Select(item => item.Sentence)
                .Where(sentence => !string.IsNullOrWhiteSpace(sentence))
                .ToList();
        }

        private static bool TryDetectIntent(string text, out string intent, out int score)
        {
            intent = string.Empty;
            score = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string source = text.Trim();
            for (int i = 0; i < IntentPatterns.Count; i++)
            {
                IntentPattern pattern = IntentPatterns[i];
                if (!ContainsAnyKeyword(source, pattern.Keywords))
                {
                    continue;
                }

                intent = pattern.Intent;
                score = pattern.Score;
                return true;
            }

            return false;
        }

        private static string BuildEventSentence(string role, string intent)
        {
            string speaker = string.Equals(role, UserRole, StringComparison.OrdinalIgnoreCase) ? "A" : "B";
            string listener = speaker == "A" ? "B" : "A";
            return $"{speaker}\u5411{listener}{intent}";
        }

        private static string BuildFallbackTopicSentence(IEnumerable<DialogueCompressionTurn> turns)
        {
            string topic = BuildTopicSummary(turns.Select(turn => turn.Content));
            if (!TopicLabelMap.TryGetValue(topic, out string label))
            {
                label = TopicLabelMap["general"];
            }

            return $"A\u4e0eB\u8ba8\u8bba\u4e86{label}";
        }

        private static string BuildTopicSummary(IEnumerable<string> texts)
        {
            var tags = new List<string>();
            foreach (KeyValuePair<string, string[]> pair in TopicKeywords)
            {
                if (ContainsAnyKeyword(texts, pair.Value))
                {
                    tags.Add(pair.Key);
                }
            }

            return tags.Count == 0 ? "general" : tags[0];
        }

        private static bool ContainsAnyKeyword(IEnumerable<string> texts, IEnumerable<string> keywords)
        {
            if (texts == null || keywords == null)
            {
                return false;
            }

            foreach (string text in texts)
            {
                if (ContainsAnyKeyword(text, keywords))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsAnyKeyword(string text, IEnumerable<string> keywords)
        {
            if (string.IsNullOrWhiteSpace(text) || keywords == null)
            {
                return false;
            }

            foreach (string keyword in keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                if (text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<ChatMessageData> ToChatMessages(IEnumerable<DialogueCompressionTurn> turns)
        {
            return turns
                .Where(turn => turn != null && !string.IsNullOrWhiteSpace(turn.Content))
                .Select(turn => new ChatMessageData { role = turn.Role, content = turn.Content })
                .ToList();
        }

        private static List<DialogueCompressionTurn> ConvertFromDialogueMessages(IEnumerable<DialogueMessageData> messages)
        {
            var result = new List<DialogueCompressionTurn>();
            if (messages == null)
            {
                return result;
            }

            foreach (DialogueMessageData message in messages)
            {
                if (message == null || message.IsSystemMessage() || string.IsNullOrWhiteSpace(message.message))
                {
                    continue;
                }

                result.Add(new DialogueCompressionTurn
                {
                    Role = message.isPlayer ? UserRole : AssistantRole,
                    Content = NormalizeContent(message.message)
                });
            }

            return result;
        }

        private static List<DialogueCompressionTurn> ConvertFromChatMessages(IEnumerable<ChatMessageData> messages)
        {
            var result = new List<DialogueCompressionTurn>();
            if (messages == null)
            {
                return result;
            }

            foreach (ChatMessageData message in messages)
            {
                string role = NormalizeRole(message?.role);
                if (role == SystemRole || string.IsNullOrWhiteSpace(message?.content))
                {
                    continue;
                }

                result.Add(new DialogueCompressionTurn
                {
                    Role = role,
                    Content = NormalizeContent(message.content)
                });
            }

            return result;
        }

        private static string NormalizeRole(string role)
        {
            string normalized = (role ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized == AssistantRole)
            {
                return AssistantRole;
            }

            if (normalized == UserRole)
            {
                return UserRole;
            }

            if (normalized == SystemRole)
            {
                return SystemRole;
            }

            return UserRole;
        }

        private static string NormalizeContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            return content.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private sealed class DialogueCompressionTurn
        {
            public string Role;
            public string Content;
        }

        private sealed class DialogueCompressionSegment
        {
            public int StartIndex;
            public int EndIndex;
            public int CompressionMark;
            public string Summary;
        }

        private sealed class IntentPattern
        {
            public readonly string Intent;
            public readonly int Score;
            public readonly string[] Keywords;

            public IntentPattern(string intent, int score, params string[] keywords)
            {
                Intent = intent ?? string.Empty;
                Score = score;
                Keywords = keywords ?? Array.Empty<string>();
            }
        }

        private sealed class DetectedEvent
        {
            public int Index;
            public int Score;
            public string Sentence;
        }
    }

    /// <summary>/// Dependencies: DialogueContextCompressionService.
 /// Responsibility: tune 10/20/25 staged compression thresholds for dialogue context building.
 ///</summary>
    public sealed class DialogueCompressionOptions
    {
        public bool Enabled = true;
        public int KeepRecentTurns = 10;
        public int FirstPassChunkSize = 10;
        public int SecondaryCompressionTrigger = 20;
        public int SecondaryWindowMinRecency = 21;
        public int SecondaryWindowMaxRecency = 25;
        public int MaxCompressionMark = 3;
        public int SegmentSnippetMaxChars = 28;
        public int MaxSummaryLines = 3;
        public int MaxSecondaryRounds = 3;

        public int SecondaryTierStart = 21;
        public int TertiaryTierStart = 26;
        public int MaxEventsPerSegment = 3;

        public static DialogueCompressionOptions Default => FromSettings(RimChatMod.Settings);

        public static DialogueCompressionOptions FromSettings(RimChatSettings settings)
        {
            var options = new DialogueCompressionOptions();
            if (settings == null)
            {
                return options;
            }

            options.Enabled = settings.EnableDialogueContextCompression;
            options.KeepRecentTurns = Clamp(settings.DialogueCompressionKeepRecentTurns, 6, 30);

            int secondaryTierStart = settings.DialogueCompressionSecondaryTierStart > 0
                ? settings.DialogueCompressionSecondaryTierStart
                : settings.DialogueCompressionSecondaryWindowMinRecency;
            options.SecondaryTierStart = Clamp(secondaryTierStart, options.KeepRecentTurns + 1, 120);

            int tertiaryTierStart = settings.DialogueCompressionTertiaryTierStart > 0
                ? settings.DialogueCompressionTertiaryTierStart
                : settings.DialogueCompressionSecondaryWindowMaxRecency + 1;
            options.TertiaryTierStart = Clamp(tertiaryTierStart, options.SecondaryTierStart + 1, 180);

            options.MaxCompressionMark = 3;
            options.MaxEventsPerSegment = Clamp(settings.DialogueCompressionMaxEventsPerSegment, 1, 3);
            options.MaxSummaryLines = Clamp(settings.DialogueCompressionMaxSummaryLines, 1, 3);

            options.SecondaryCompressionTrigger = options.KeepRecentTurns + 10;
            options.SecondaryWindowMinRecency = options.SecondaryTierStart;
            options.SecondaryWindowMaxRecency = options.TertiaryTierStart - 1;
            options.MaxSecondaryRounds = 3;
            return options;
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Min(max, Math.Max(min, value));
        }
    }
}
