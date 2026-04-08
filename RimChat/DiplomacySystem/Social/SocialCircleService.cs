using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>/// Dependencies: RimWorld faction/goodwill APIs and RimChat settings.
 /// Responsibility: provide social-circle utility helpers for news categorization, post assembly, and lightweight intent updates.
 ///</summary>
    public static class SocialCircleService
    {
        private static readonly HashSet<string> DevGuardWarningKeys = new HashSet<string>();

        private static readonly string[] NegativeKeywords =
        {
            "insult", "humiliate", "threat", "betray", "offend", "raid", "attack",
            "humiliation", "aggression", "war", "hostile", "siege"
        };

        private static readonly string[] PositiveKeywords =
        {
            "help", "aid", "gift", "cooperate", "peace", "alliance",
            "support", "truce", "trade", "caravan"
        };

        private static readonly string[] MilitaryKeywords =
        {
            "raid", "attack", "war", "siege", "strike", "offensive", "battle", "military"
        };

        private static readonly string[] EconomicKeywords =
        {
            "trade", "gift", "caravan", "resource", "crop", "market", "economic"
        };

        private static readonly string[] AnomalyKeywords =
        {
            "anomaly", "flare", "heat wave", "cold snap", "blight", "storm", "incident"
        };

        public static int CalculateNextIntervalTicks(RimChatSettings settings)
        {
            int days = ResolveScheduledDays(settings);
            return days * GenDate.TicksPerDay;
        }

        private static int ResolveScheduledDays(RimChatSettings settings)
        {
            global::RimChat.Config.ScheduledNewsFrequencyLevel level =
                settings?.ScheduledNewsFrequencyLevel ?? global::RimChat.Config.ScheduledNewsFrequencyLevel.Low;
            switch (level)
            {
                case global::RimChat.Config.ScheduledNewsFrequencyLevel.High:
                    return 1;
                case global::RimChat.Config.ScheduledNewsFrequencyLevel.Medium:
                    return Rand.RangeInclusive(1, 2);
                default:
                    return Rand.RangeInclusive(3, 5);
            }
        }

        internal static PublicSocialPost CreatePostFromDraft(SocialNewsSeed seed, SocialNewsDraft draft)
        {
            if (seed == null || draft == null || !seed.IsValid())
            {
                return null;
            }

            string resolvedLocation = ResolvePostLocation(seed, draft);
            string resolvedQuote = SanitizeQuoteForPost(draft.Quote);
            string resolvedAttribution = SanitizeQuoteAttributionForPost(draft.QuoteAttribution, resolvedQuote, seed);
            draft.Quote = resolvedQuote;
            draft.QuoteAttribution = resolvedAttribution;
            draft.LocationName = resolvedLocation;
            var post = new PublicSocialPost
            {
                PostId = Guid.NewGuid().ToString("N"),
                CreatedTick = seed.OccurredTick,
                SourceFaction = seed.SourceFaction,
                TargetFaction = seed.TargetFaction,
                OriginType = seed.OriginType,
                OriginKey = seed.OriginKey,
                Category = seed.Category,
                Sentiment = Mathf.Clamp(seed.Sentiment, -2, 2),
                Credibility = seed.CredibilityValue,
                CredibilityValue = seed.CredibilityValue,
                CredibilityLabel = seed.CredibilityLabel ?? string.Empty,
                SourceLabel = seed.SourceLabel ?? string.Empty,
                GenerationState = SocialNewsGenerationState.Completed,
                Headline = draft.Headline ?? string.Empty,
                Lead = draft.Lead ?? string.Empty,
                Cause = draft.Cause ?? string.Empty,
                Process = draft.Process ?? string.Empty,
                Outlook = draft.Outlook ?? string.Empty,
                Quote = resolvedQuote,
                QuoteAttribution = resolvedAttribution,
                LocationName = resolvedLocation,
                Content = BuildCompositeContent(draft),
                EffectSummary = string.Empty,
                IsFromPlayerDialogue = seed.IsFromPlayerDialogue,
                IntentHint = seed.IntentHint ?? string.Empty,
                SourceLeaderName = GetLeaderName(seed.SourceFaction),
                TargetLeaderName = GetLeaderName(seed.TargetFaction)
            };
            return post;
        }

        public static void ApplyDialogueConsequences(SocialCircleState state, PublicSocialPost post)
        {
            if (state == null || post == null || !post.IsFromPlayerDialogue)
            {
                return;
            }

            ApplySoftImpact(post);
            UpdateActionIntents(state, post, post.CreatedTick);
        }

        public static void UpdateActionIntents(SocialCircleState state, PublicSocialPost post, int currentTick)
        {
            if (state == null || post == null)
            {
                return;
            }

            if (ShouldEscalateRaidIntent(post))
            {
                AddIntentScore(state, post.TargetFaction ?? post.SourceFaction, SocialIntentType.Raid, post, currentTick);
                return;
            }

            if (post.Sentiment < 1 || post.SourceFaction == null)
            {
                return;
            }

            if (post.SourceFaction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Ally)
            {
                AddIntentScore(state, post.SourceFaction, SocialIntentType.Aid, 0.25f * post.CredibilityValue, currentTick);
                return;
            }

            if (post.SourceFaction.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile)
            {
                AddIntentScore(state, post.SourceFaction, SocialIntentType.Caravan, 0.2f * post.CredibilityValue, currentTick);
            }
        }

        public static void DecayIntents(SocialCircleState state)
        {
            if (state?.ActionIntents == null)
            {
                return;
            }

            for (int index = state.ActionIntents.Count - 1; index >= 0; index--)
            {
                SocialActionIntent intent = state.ActionIntents[index];
                if (intent == null || intent.Faction == null || intent.Faction.defeated)
                {
                    state.ActionIntents.RemoveAt(index);
                    continue;
                }

                intent.Score = Mathf.Max(0f, intent.Score - 0.1f);
                if (intent.Score <= 0.001f)
                {
                    state.ActionIntents.RemoveAt(index);
                }
            }
        }

        public static bool TryAnalyzeDialogueKeywords(
            string playerText,
            string aiText,
            out SocialPostCategory category,
            out int sentiment,
            out string intentHint)
        {
            string merged = $"{playerText} {aiText}".ToLowerInvariant();
            category = InferCategory(merged, string.Empty);
            sentiment = InferSentiment(merged);
            intentHint = string.Empty;
            if (sentiment == 0)
            {
                return false;
            }

            if (sentiment <= -1 && ContainsAny(merged, MilitaryKeywords))
            {
                intentHint = SocialIntentType.Raid.ToString();
            }

            return true;
        }

        public static SocialPostCategory InferCategory(string text, string eventType)
        {
            string merged = $"{text} {eventType}".ToLowerInvariant();
            if (ContainsAny(merged, MilitaryKeywords)) return SocialPostCategory.Military;
            if (ContainsAny(merged, EconomicKeywords)) return SocialPostCategory.Economic;
            if (ContainsAny(merged, AnomalyKeywords)) return SocialPostCategory.Anomaly;
            return SocialPostCategory.Diplomatic;
        }

        public static int InferSentiment(string text)
        {
            string merged = (text ?? string.Empty).ToLowerInvariant();
            int negative = CountHits(merged, NegativeKeywords);
            int positive = CountHits(merged, PositiveKeywords);
            if (negative == 0 && positive == 0)
            {
                return 0;
            }

            int sentiment = Mathf.Clamp(positive - negative, -2, 2);
            if (sentiment != 0)
            {
                return sentiment;
            }

            return negative >= positive ? -1 : 1;
        }

        public static string GetCategoryLabel(SocialPostCategory category)
        {
            return GetCategoryLabelKey(category).Translate();
        }

        public static string GetCategoryLabelKey(SocialPostCategory category)
        {
            switch (category)
            {
                case SocialPostCategory.Military:
                    return "RimChat_NewsCategoryMilitary";
                case SocialPostCategory.Economic:
                    return "RimChat_NewsCategoryEconomic";
                case SocialPostCategory.Anomaly:
                    return "RimChat_NewsCategoryAnomaly";
                default:
                    return "RimChat_NewsCategoryDiplomatic";
            }
        }

        public static string ResolveDisplayLabel(string keyOrText)
        {
            if (string.IsNullOrWhiteSpace(keyOrText))
            {
                return string.Empty;
            }

            return keyOrText.StartsWith("RimChat_", StringComparison.Ordinal)
                ? keyOrText.Translate().ToString()
                : keyOrText;
        }

        private static string ResolvePostLocation(SocialNewsSeed seed, SocialNewsDraft draft)
        {
            if (!string.IsNullOrWhiteSpace(draft?.LocationName))
            {
                return draft.LocationName.Trim();
            }

            return ExtractLocationFromFacts(seed?.Facts);
        }

        private static string ExtractLocationFromFacts(IEnumerable<string> facts)
        {
            foreach (string fact in facts ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(fact))
                {
                    continue;
                }

                const string prefix = "Stronghold/settlement explicitly tied to this event:";
                if (fact.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return fact.Substring(prefix.Length).Trim();
                }
            }

            return string.Empty;
        }

        private static string SanitizeQuoteForPost(string quote)
        {
            if (string.IsNullOrWhiteSpace(quote))
            {
                return string.Empty;
            }

            string value = quote.Trim();
            string[] blockedFragments =
            {
                "消息源：",
                "消息来源：",
                "来源：",
                "Source:",
                "source:",
                "公开社交圈转述"
            };

            foreach (string fragment in blockedFragments)
            {
                int index = value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    value = value.Substring(0, index).Trim();
                }
            }

            return value.Trim('"', '“', '”');
        }

        private static string SanitizeQuoteAttributionForPost(string attribution, string quote, SocialNewsSeed seed)
        {
            if (string.IsNullOrWhiteSpace(quote))
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(attribution))
            {
                return attribution.Trim().Trim('"', '“', '”');
            }

            return ResolveDisplayLabel(seed?.SourceLabel);
        }

        private static string BuildCompositeContent(SocialNewsDraft draft)
        {
            List<string> parts = BuildOrderedNarrativeParts(draft);
            return string.Join("\n\n", parts.Where(item => !string.IsNullOrWhiteSpace(item)));
        }

        private static List<string> BuildOrderedNarrativeParts(SocialNewsDraft draft)
        {
            string mode = (draft?.NarrativeMode ?? string.Empty).Trim();
            switch (mode)
            {
                case "rumor_wire":
                    return new List<string>
                    {
                        draft?.Lead ?? string.Empty,
                        draft?.Process ?? string.Empty,
                        draft?.Cause ?? string.Empty,
                        draft?.Outlook ?? string.Empty
                    };
                case "war_dispatch":
                    return new List<string>
                    {
                        draft?.Lead ?? string.Empty,
                        draft?.Cause ?? string.Empty,
                        draft?.Outlook ?? string.Empty,
                        draft?.Process ?? string.Empty
                    };
                case "personal_chronicle":
                    return new List<string>
                    {
                        draft?.Lead ?? string.Empty,
                        draft?.Cause ?? string.Empty,
                        draft?.Process ?? string.Empty,
                        draft?.Outlook ?? string.Empty
                    };
                default:
                    return new List<string>
                    {
                        draft?.Lead ?? string.Empty,
                        draft?.Cause ?? string.Empty,
                        draft?.Process ?? string.Empty,
                        draft?.Outlook ?? string.Empty
                    };
            }
        }

        private static bool ShouldEscalateRaidIntent(PublicSocialPost post)
        {
            return post.Sentiment <= -1
                && (post.Category == SocialPostCategory.Military
                    || string.Equals(post.IntentHint, SocialIntentType.Raid.ToString(), StringComparison.Ordinal));
        }

        private static void AddIntentScore(
            SocialCircleState state,
            Faction faction,
            SocialIntentType intentType,
            PublicSocialPost post,
            int currentTick)
        {
            float gain = Mathf.Clamp(Math.Abs(post.Sentiment) * post.CredibilityValue * 0.35f, 0.1f, 0.8f);
            AddIntentScore(state, faction, intentType, gain, currentTick);
        }

        private static void AddIntentScore(
            SocialCircleState state,
            Faction faction,
            SocialIntentType intentType,
            float gain,
            int currentTick)
        {
            if (state == null || faction == null || faction.defeated || gain <= 0f)
            {
                return;
            }

            if (faction.IsPlayer)
            {
                LogSelfRelationGuardOnce(
                    $"intent:{intentType}:{faction.GetUniqueLoadID()}",
                    $"[RimChat] Blocked social intent registration for player faction ({intentType}).");
                return;
            }

            SocialActionIntent entry = state.ActionIntents.FirstOrDefault(item =>
                item != null &&
                item.Faction == faction &&
                item.IntentType == intentType);
            if (entry == null)
            {
                entry = new SocialActionIntent
                {
                    Faction = faction,
                    IntentType = intentType
                };
                state.ActionIntents.Add(entry);
            }

            entry.Score = Mathf.Clamp01(entry.Score + gain);
            entry.LastUpdatedTick = currentTick;
        }

        private static void ApplySoftImpact(PublicSocialPost post)
        {
            int delta = CalculateSoftImpactDelta(post);
            if (delta == 0)
            {
                return;
            }

            var impactFactions = new HashSet<Faction>();
            if (post?.SourceFaction != null)
            {
                impactFactions.Add(post.SourceFaction);
            }

            if (post?.TargetFaction != null)
            {
                impactFactions.Add(post.TargetFaction);
            }

            foreach (Faction faction in impactFactions)
            {
                TryAffectPlayerGoodwill(faction, delta);
            }
        }

        private static int CalculateSoftImpactDelta(PublicSocialPost post)
        {
            float value = (post?.Sentiment ?? 0) * (post?.CredibilityValue ?? 0.6f) * 2f;
            return Mathf.Clamp(Mathf.RoundToInt(value), -4, 4);
        }

        private static void TryAffectPlayerGoodwill(Faction faction, int delta)
        {
            if (faction == null || faction.defeated || delta == 0)
            {
                return;
            }

            if (faction == Faction.OfPlayer || faction.IsPlayer)
            {
                LogSelfRelationGuardOnce(
                    $"goodwill:{faction.GetUniqueLoadID()}",
                    "[RimChat] Blocked self goodwill adjustment: player faction cannot affect itself.");
                return;
            }

            faction.TryAffectGoodwillWith(Faction.OfPlayer, delta, false, true, null);
        }

        private static void LogSelfRelationGuardOnce(string key, string message)
        {
            if (!Prefs.DevMode || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (DevGuardWarningKeys.Add(key))
            {
                Log.Warning(message);
            }
        }

        private static string GetLeaderName(Faction faction)
        {
            if (faction?.leader == null)
            {
                return string.Empty;
            }

            Name leaderName = faction.leader.Name;
            if (leaderName == null)
            {
                return string.Empty;
            }

            return leaderName.ToStringFull;
        }

        private static bool ContainsAny(string text, IEnumerable<string> keywords)
        {
            return (keywords ?? Enumerable.Empty<string>())
                .Any(keyword => !string.IsNullOrWhiteSpace(keyword) && text.Contains(keyword));
        }

        private static int CountHits(string text, IEnumerable<string> keywords)
        {
            return (keywords ?? Enumerable.Empty<string>())
                .Count(keyword => !string.IsNullOrWhiteSpace(keyword) && text.Contains(keyword));
        }
    }
}
