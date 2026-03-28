using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.NpcDialogue;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: social-circle state, NPC proactive dialogue push manager, and RimWorld faction APIs.
    /// Responsibility: publish manual player social posts and orchestrate forced faction reactions.
    /// </summary>
    public partial class GameComponent_DiplomacyManager
    {
        internal const int ManualSocialPostTitleMaxLength = 80;
        internal const int ManualSocialPostBodyMaxLength = 600;

        public ManualSocialPostResult TryPublishManualPlayerSocialPost(string title, string body)
        {
            var result = new ManualSocialPostResult();

            if (!IsSocialCircleEnabled())
            {
                result.FailureReason = ManualSocialPostFailureReason.Disabled;
                return result;
            }

            string normalizedTitle = (title ?? string.Empty).Trim();
            string normalizedBody = (body ?? string.Empty).Trim();

            if (normalizedTitle.Length == 0)
            {
                result.FailureReason = ManualSocialPostFailureReason.MissingTitle;
                return result;
            }

            if (normalizedBody.Length == 0)
            {
                result.FailureReason = ManualSocialPostFailureReason.MissingBody;
                return result;
            }

            if (normalizedTitle.Length > ManualSocialPostTitleMaxLength)
            {
                result.FailureReason = ManualSocialPostFailureReason.TitleTooLong;
                return result;
            }

            if (normalizedBody.Length > ManualSocialPostBodyMaxLength)
            {
                result.FailureReason = ManualSocialPostFailureReason.BodyTooLong;
                return result;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            PublicSocialPost post = CreateManualPlayerSocialPost(normalizedTitle, normalizedBody, currentTick);
            if (post == null)
            {
                result.FailureReason = ManualSocialPostFailureReason.Unknown;
                return result;
            }

            AddCompletedSocialPost(
                post,
                new SocialNewsSeed
                {
                    OriginType = post.OriginType,
                    OriginKey = post.OriginKey,
                    ApplyDiplomaticImpact = false
                },
                currentTick);

            List<Faction> targetFactions = SelectManualReactionFactions(normalizedTitle, normalizedBody);
            TriggerManualPostResponses(post, targetFactions);

            result.Success = true;
            result.PostId = post.PostId ?? string.Empty;
            result.TriggeredFactionCount = targetFactions.Count;
            result.FailureReason = ManualSocialPostFailureReason.None;
            return result;
        }

        public static string GetManualSocialPostFailureReasonLabel(ManualSocialPostFailureReason reason)
        {
            switch (reason)
            {
                case ManualSocialPostFailureReason.Disabled:
                    return "RimChat_SocialFailureReason_disabled".Translate();
                case ManualSocialPostFailureReason.MissingTitle:
                    return "RimChat_ManualSocialPostMissingTitle".Translate();
                case ManualSocialPostFailureReason.MissingBody:
                    return "RimChat_ManualSocialPostMissingBody".Translate();
                case ManualSocialPostFailureReason.TitleTooLong:
                    return "RimChat_ManualSocialPostTitleTooLong".Translate(ManualSocialPostTitleMaxLength);
                case ManualSocialPostFailureReason.BodyTooLong:
                    return "RimChat_ManualSocialPostBodyTooLong".Translate(ManualSocialPostBodyMaxLength);
                default:
                    return "RimChat_SocialFailureReason_unknown".Translate();
            }
        }

        private PublicSocialPost CreateManualPlayerSocialPost(string title, string body, int currentTick)
        {
            Faction playerFaction = Faction.OfPlayer;
            SocialPostCategory category = SocialCircleService.InferCategory($"{title} {body}", string.Empty);
            int sentiment = SocialCircleService.InferSentiment($"{title} {body}");
            string sourceLabel = "RimChat_SocialSourcePlayerBroadcast";
            string credibilityLabel = "RimChat_SocialCredibilityPlayerStatement";

            return new PublicSocialPost
            {
                PostId = Guid.NewGuid().ToString("N"),
                CreatedTick = currentTick,
                SourceFaction = playerFaction,
                TargetFaction = null,
                OriginType = SocialNewsOriginType.PlayerManual,
                OriginKey = $"manual-player-post:{currentTick}:{Guid.NewGuid():N}",
                Category = category,
                Sentiment = Mathf.Clamp(sentiment, -2, 2),
                Credibility = 1f,
                CredibilityValue = 1f,
                CredibilityLabel = credibilityLabel,
                SourceLabel = sourceLabel,
                GenerationState = SocialNewsGenerationState.Completed,
                Headline = title,
                Lead = body,
                Cause = string.Empty,
                Process = string.Empty,
                Outlook = string.Empty,
                Quote = string.Empty,
                QuoteAttribution = string.Empty,
                Content = body,
                EffectSummary = string.Empty,
                IsFromPlayerDialogue = true,
                IntentHint = "manual_social_post",
                SourceLeaderName = playerFaction?.Name ?? string.Empty,
                TargetLeaderName = string.Empty
            };
        }

        private List<Faction> SelectManualReactionFactions(string title, string body)
        {
            List<Faction> candidates = Find.FactionManager?.AllFactions
                ?.Where(IsEligibleManualReactionFaction)
                .ToList() ?? new List<Faction>();
            if (candidates.Count == 0)
            {
                return new List<Faction>();
            }

            string content = $"{title} {body}".Trim();
            SocialPostCategory category = SocialCircleService.InferCategory(content, string.Empty);
            int sentiment = SocialCircleService.InferSentiment(content);
            int desiredCount = Math.Min(candidates.Count, Rand.RangeInclusive(1, 3));

            List<FactionScore> scored = candidates
                .Select(faction => new FactionScore(faction, ScoreManualReactionFaction(faction, content, category, sentiment)))
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Faction.Name)
                .ToList();

            List<Faction> selected = new List<Faction>();
            foreach (FactionScore item in scored)
            {
                if (selected.Count >= desiredCount)
                {
                    break;
                }

                selected.Add(item.Faction);
            }

            return selected;
        }

        private void TriggerManualPostResponses(PublicSocialPost post, List<Faction> targetFactions)
        {
            if (post == null || targetFactions == null || targetFactions.Count == 0)
            {
                return;
            }

            GameComponent_NpcDialoguePushManager pushManager = GameComponent_NpcDialoguePushManager.Instance;
            if (pushManager == null)
            {
                return;
            }

            foreach (Faction faction in targetFactions)
            {
                pushManager.RegisterCustomTrigger(BuildManualPostTriggerContext(faction, post));
            }
        }

        private NpcDialogueTriggerContext BuildManualPostTriggerContext(Faction faction, PublicSocialPost post)
        {
            string body = post?.Content?.Trim() ?? string.Empty;
            string reason =
                $"manual_social_post|title={SanitizeManualReasonSegment(post?.Headline)}|body={SanitizeManualReasonSegment(body)}";
            int sentiment = post?.Sentiment ?? 0;
            bool militaryTone = post != null && post.Category == SocialPostCategory.Military;

            NpcDialogueCategory category = NpcDialogueCategory.Social;
            int severity = 1;
            if (sentiment <= -1)
            {
                category = NpcDialogueCategory.WarningThreat;
                severity = militaryTone || sentiment <= -2 ? 3 : 2;
            }
            else if (sentiment >= 1)
            {
                category = NpcDialogueCategory.DiplomacyTask;
                severity = 1;
            }

            return new NpcDialogueTriggerContext
            {
                Faction = faction,
                TriggerType = NpcDialogueTriggerType.Causal,
                Category = category,
                Reason = reason,
                SourceTag = "manual_social_post",
                Severity = severity,
                CreatedTick = Find.TickManager?.TicksGame ?? 0,
                BypassRateLimit = true,
                BypassCategoryGate = category == NpcDialogueCategory.WarningThreat,
                BypassPlayerBusyGate = true
            };
        }

        private bool IsEligibleManualReactionFaction(Faction faction)
        {
            return faction != null &&
                   !faction.IsPlayer &&
                   !faction.defeated &&
                   !(faction.def?.hidden ?? true);
        }

        private float ScoreManualReactionFaction(Faction faction, string content, SocialPostCategory category, int sentiment)
        {
            if (faction == null)
            {
                return float.MinValue;
            }

            string normalizedContent = (content ?? string.Empty).ToLowerInvariant();
            float score = 0f;
            score += CountMentionHits(normalizedContent, faction.Name) * 5f;
            score += CountMentionHits(normalizedContent, faction.def?.label) * 3f;
            score += CountMentionHits(normalizedContent, faction.def?.defName) * 2f;

            FactionRelationKind relation = faction.RelationKindWith(Faction.OfPlayer);
            if (sentiment <= -1)
            {
                if (relation == FactionRelationKind.Hostile)
                {
                    score += 2.5f;
                }
                else if (relation == FactionRelationKind.Neutral)
                {
                    score += 1.25f;
                }
            }
            else if (sentiment >= 1)
            {
                if (relation == FactionRelationKind.Ally)
                {
                    score += 2.5f;
                }
                else if (relation == FactionRelationKind.Neutral)
                {
                    score += 1.25f;
                }
            }
            else
            {
                score += relation == FactionRelationKind.Neutral ? 1.1f : 0.6f;
            }

            if (category == SocialPostCategory.Military && relation == FactionRelationKind.Hostile)
            {
                score += 1.5f;
            }

            if (category == SocialPostCategory.Economic && relation != FactionRelationKind.Hostile)
            {
                score += 1.25f;
            }

            if (category == SocialPostCategory.Diplomatic)
            {
                score += 0.8f;
            }

            score += (Math.Abs(faction.PlayerGoodwill) / 100f);
            score += Rand.Value * 0.15f;
            return score;
        }

        private static int CountMentionHits(string content, string token)
        {
            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(token))
            {
                return 0;
            }

            string normalizedToken = token.Trim().ToLowerInvariant();
            int count = 0;
            int index = 0;
            while (index >= 0 && index < content.Length)
            {
                index = content.IndexOf(normalizedToken, index, StringComparison.Ordinal);
                if (index < 0)
                {
                    break;
                }

                count++;
                index += normalizedToken.Length;
            }

            return count;
        }

        private static string SanitizeManualReasonSegment(string text)
        {
            string sanitized = (text ?? string.Empty)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("|", "/")
                .Trim();
            if (sanitized.Length <= 180)
            {
                return sanitized;
            }

            return sanitized.Substring(0, 180).TrimEnd();
        }

        private sealed class FactionScore
        {
            public FactionScore(Faction faction, float score)
            {
                Faction = faction;
                Score = score;
            }

            public Faction Faction { get; }
            public float Score { get; }
        }
    }
}
