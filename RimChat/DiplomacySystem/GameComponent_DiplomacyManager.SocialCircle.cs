using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Core;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>/// Dependencies: SocialCircleService, SocialCircleActionResolver, RimWorld Faction/Goodwill APIs.
 /// Responsibility: social circle state persistence, scheduling, and runtime orchestration.
 ///</summary>
    public partial class GameComponent_DiplomacyManager
    {
        private const int MaxSocialPosts = 200;
        private SocialCircleState socialCircleState = new SocialCircleState();

        private void InitializeSocialCircleOnNewGame()
        {
            EnsureSocialCircleState();
            socialCircleState.Posts.Clear();
            socialCircleState.ActionIntents.Clear();
            socialCircleState.FactionActionCooldowns.Clear();
            socialCircleState.LastReadPostId = string.Empty;

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            socialCircleState.NextPostTick = currentTick + SocialCircleService.CalculateNextIntervalTicks(RimChatMod.Instance?.InstanceSettings);
        }

        private void InitializeSocialCircleOnLoadedGame()
        {
            EnsureSocialCircleState();
            socialCircleState.CleanupInvalidEntries();
            EnsureNextSocialPostTick(Find.TickManager?.TicksGame ?? 0);
        }

        private void ProcessSocialCircleTick()
        {
            if (!IsSocialCircleEnabled()) return;
            EnsureSocialCircleState();

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick <= 0) return;

            EnsureNextSocialPostTick(currentTick);
            TryGenerateScheduledSocialPost(currentTick);
            SocialCircleActionResolver.ResolveAndExecute(this, socialCircleState, currentTick);
        }

        private void OnSocialCircleDailyReset()
        {
            EnsureSocialCircleState();
            SocialCircleService.DecayIntents(socialCircleState);
            socialCircleState.CleanupInvalidEntries();
        }

        public bool IsSocialCircleEnabled()
        {
            return RimChatMod.Instance?.InstanceSettings?.EnableSocialCircle ?? true;
        }

        public bool ForceGeneratePublicPost(DebugGenerateReason reason = DebugGenerateReason.ManualButton)
        {
            if (!IsSocialCircleEnabled()) return false;

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            bool created = TryGenerateAutomaticPost(reason, currentTick, true);
            if (created)
            {
                socialCircleState.NextPostTick = currentTick + SocialCircleService.CalculateNextIntervalTicks(RimChatMod.Instance?.InstanceSettings);
            }
            return created;
        }

        public bool EnqueuePublicPost(
            Faction sourceFaction,
            Faction targetFaction,
            SocialPostCategory category,
            int sentiment,
            string summary,
            bool isFromPlayerDialogue,
            string intentHint = "",
            DebugGenerateReason reason = DebugGenerateReason.DialogueExplicit)
        {
            if (sourceFaction == null || sourceFaction.defeated) return false;
            if (isFromPlayerDialogue && !(RimChatMod.Instance?.InstanceSettings?.EnablePlayerInfluenceNews ?? true)) return false;

            var post = SocialCircleService.CreatePost(
                sourceFaction,
                targetFaction,
                category,
                sentiment,
                summary,
                isFromPlayerDialogue,
                reason,
                intentHint);

            return EnqueuePublicPostInternal(post, reason);
        }

        public bool TryCreateKeywordDialoguePost(Faction sourceFaction, string playerMessage, string aiResponse)
        {
            if (sourceFaction == null || sourceFaction.defeated) return false;
            if (!(RimChatMod.Instance?.InstanceSettings?.EnablePlayerInfluenceNews ?? true)) return false;

            bool matched = SocialCircleService.TryAnalyzeDialogueKeywords(
                playerMessage,
                aiResponse,
                out SocialPostCategory category,
                out int sentiment,
                out string intentHint);
            if (!matched) return false;

            Faction targetFaction = ResolveMentionedFaction($"{playerMessage} {aiResponse}", sourceFaction);
            string summary = "RimChat_SocialPostSummaryFromDialogue".Translate();
            return EnqueuePublicPost(
                sourceFaction,
                targetFaction,
                category,
                sentiment,
                summary,
                true,
                intentHint,
                DebugGenerateReason.DialogueKeyword);
        }

        public Faction ResolveSocialTargetFaction(string token, Faction sourceFaction = null)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;
            string normalized = token.Trim();
            return GetEligibleSocialFactions()
                .FirstOrDefault(f =>
                    f != sourceFaction &&
                    (string.Equals(f.Name, normalized, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(f.def?.defName, normalized, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(f.def?.label, normalized, StringComparison.OrdinalIgnoreCase)));
        }

        public List<PublicSocialPost> GetSocialPosts(int maxCount = MaxSocialPosts)
        {
            EnsureSocialCircleState();
            int count = Math.Max(1, maxCount);
            return socialCircleState.Posts
                .OrderByDescending(p => p.CreatedTick)
                .Take(count)
                .ToList();
        }

        public int GetUnreadSocialPostCount()
        {
            EnsureSocialCircleState();
            if (socialCircleState.Posts.Count == 0) return 0;
            if (string.IsNullOrEmpty(socialCircleState.LastReadPostId)) return socialCircleState.Posts.Count;

            int index = socialCircleState.Posts.FindLastIndex(p => p.PostId == socialCircleState.LastReadPostId);
            if (index < 0) return socialCircleState.Posts.Count;
            return Math.Max(0, socialCircleState.Posts.Count - index - 1);
        }

        public void MarkSocialPostsRead()
        {
            EnsureSocialCircleState();
            if (socialCircleState.Posts.Count == 0)
            {
                socialCircleState.LastReadPostId = string.Empty;
                return;
            }
            socialCircleState.LastReadPostId = socialCircleState.Posts[socialCircleState.Posts.Count - 1].PostId;
        }

        public bool TryLikeSocialPost(string postId, out int goodwillBonus)
        {
            goodwillBonus = 0;
            EnsureSocialCircleState();
            if (string.IsNullOrWhiteSpace(postId)) return false;

            PublicSocialPost post = socialCircleState.Posts.LastOrDefault(p => p.PostId == postId);
            if (post == null || post.LikedByPlayer) return false;

            post.LikedByPlayer = true;
            post.CurrentLikeCount += SocialCircleService.GenerateLikeIncrement(post);
            goodwillBonus = SocialCircleService.RollLikeGoodwillBonus(post);
            if (goodwillBonus != 0 && post.SourceFaction != null && !post.SourceFaction.defeated)
            {
                post.SourceFaction.TryAffectGoodwillWith(Faction.OfPlayer, goodwillBonus, false, true, null);
            }
            return true;
        }

        private void EnsureSocialCircleState()
        {
            if (socialCircleState == null)
            {
                socialCircleState = new SocialCircleState();
            }
            socialCircleState.Posts = socialCircleState.Posts ?? new List<PublicSocialPost>();
            socialCircleState.ActionIntents = socialCircleState.ActionIntents ?? new List<SocialActionIntent>();
            socialCircleState.FactionActionCooldowns = socialCircleState.FactionActionCooldowns ?? new List<SocialFactionActionCooldown>();
        }

        private void EnsureNextSocialPostTick(int currentTick)
        {
            if (socialCircleState.NextPostTick > currentTick) return;
            socialCircleState.NextPostTick = currentTick + SocialCircleService.CalculateNextIntervalTicks(RimChatMod.Instance?.InstanceSettings);
        }

        private void TryGenerateScheduledSocialPost(int currentTick)
        {
            if (currentTick < socialCircleState.NextPostTick) return;
            bool created = TryGenerateAutomaticPost(DebugGenerateReason.Scheduled, currentTick, false);
            socialCircleState.NextPostTick = currentTick + SocialCircleService.CalculateNextIntervalTicks(RimChatMod.Instance?.InstanceSettings);
            if (!created)
            {
                Log.Warning("[RimChat] Scheduled social post generation skipped due to missing eligible faction.");
            }
        }

        private bool TryGenerateAutomaticPost(DebugGenerateReason reason, int currentTick, bool bypassSimulationToggle)
        {
            if (!bypassSimulationToggle && !(RimChatMod.Instance?.InstanceSettings?.EnableAISimulationNews ?? true))
            {
                return false;
            }

            List<Faction> factions = GetEligibleSocialFactions();
            if (factions.Count == 0) return false;

            Faction sourceFaction = factions.RandomElement();
            Faction targetFaction = PickSocialTargetFaction(sourceFaction, factions);
            PublicSocialPost post = SocialCircleService.CreateScheduledPost(sourceFaction, targetFaction);
            post.CreatedTick = currentTick;
            return EnqueuePublicPostInternal(post, reason);
        }

        private bool EnqueuePublicPostInternal(PublicSocialPost post, DebugGenerateReason reason)
        {
            if (post == null || post.SourceFaction == null || post.SourceFaction.defeated) return false;
            EnsureSocialCircleState();

            if (IsNearDuplicatePost(post))
            {
                Log.Message($"[RimChat] Skipped duplicate social post ({reason}).");
                return false;
            }

            socialCircleState.Posts.Add(post);
            TrimSocialPosts();
            post.EffectSummary = SocialCircleService.ApplyPostImpact(post);
            SocialCircleService.UpdateActionIntents(socialCircleState, post, post.CreatedTick);
            return true;
        }

        private bool IsNearDuplicatePost(PublicSocialPost post)
        {
            int currentTick = post.CreatedTick;
            return socialCircleState.Posts.Any(existing =>
                existing.SourceFaction == post.SourceFaction &&
                existing.TargetFaction == post.TargetFaction &&
                existing.Category == post.Category &&
                existing.Sentiment == post.Sentiment &&
                Math.Abs(existing.CreatedTick - currentTick) <= 2500);
        }

        private void TrimSocialPosts()
        {
            if (socialCircleState.Posts.Count <= MaxSocialPosts) return;
            int removeCount = socialCircleState.Posts.Count - MaxSocialPosts;
            socialCircleState.Posts.RemoveRange(0, removeCount);
        }

        private List<Faction> GetEligibleSocialFactions()
        {
            return Find.FactionManager.AllFactions
                .Where(f => f != null && !f.IsPlayer && !f.defeated && !f.def.hidden)
                .ToList();
        }

        private Faction PickSocialTargetFaction(Faction sourceFaction, List<Faction> factions)
        {
            if (sourceFaction == null || factions == null || factions.Count < 2) return null;
            if (Rand.Chance(0.4f)) return null;

            List<Faction> candidates = factions.Where(f => f != sourceFaction).ToList();
            return candidates.Count == 0 ? null : candidates.RandomElement();
        }

        private Faction ResolveMentionedFaction(string text, Faction sourceFaction)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string source = text.ToLowerInvariant();
            foreach (Faction faction in GetEligibleSocialFactions())
            {
                if (faction == sourceFaction) continue;
                if (!string.IsNullOrEmpty(faction.Name) && source.Contains(faction.Name.ToLowerInvariant()))
                {
                    return faction;
                }
                if (!string.IsNullOrEmpty(faction.def?.label) && source.Contains(faction.def.label.ToLowerInvariant()))
                {
                    return faction;
                }
            }
            return null;
        }
    }
}


