using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Core;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>/// Dependencies: social-circle services, social action resolver, RimWorld faction APIs.
 /// Responsibility: own social-circle state, public APIs, and schedule-based news polling.
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
            socialCircleState.ProcessedOrigins.Clear();
            socialCircleState.LastReadPostId = string.Empty;
            ClearSocialTransientState();
            ScheduleNextSocialPost(Find.TickManager?.TicksGame ?? 0);
        }

        private void InitializeSocialCircleOnLoadedGame()
        {
            EnsureSocialCircleState();
            socialCircleState.CleanupInvalidEntries();
            socialCircleState.ClearPendingOrigins();
            ClearSocialTransientState();
            EnsureNextSocialPostTick(Find.TickManager?.TicksGame ?? 0);
        }

        private void ProcessSocialCircleTick()
        {
            if (!IsSocialCircleEnabled())
            {
                return;
            }

            EnsureSocialCircleState();
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick <= 0)
            {
                return;
            }

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
            if (!IsSocialCircleEnabled())
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            bool queued = TryQueueNextScheduledNews(reason, currentTick, true);
            if (queued)
            {
                ScheduleNextSocialPost(currentTick);
            }

            return queued;
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
            if (!IsSocialCircleEnabled() || sourceFaction == null || sourceFaction.defeated)
            {
                return false;
            }

            if (isFromPlayerDialogue && !(RimChatMod.Instance?.InstanceSettings?.EnablePlayerInfluenceNews ?? true))
            {
                return false;
            }

            SocialNewsSeed seed = SocialNewsSeedFactory.CreateDialogueSeed(
                sourceFaction,
                targetFaction,
                category,
                sentiment,
                summary,
                reason == DebugGenerateReason.DialogueKeyword,
                intentHint,
                reason);
            return TryQueueNewsSeed(seed, Find.TickManager?.TicksGame ?? 0);
        }

        public bool TryCreateKeywordDialoguePost(Faction sourceFaction, string playerMessage, string aiResponse)
        {
            if (!IsSocialCircleEnabled() || sourceFaction == null || sourceFaction.defeated)
            {
                return false;
            }

            if (!(RimChatMod.Instance?.InstanceSettings?.EnablePlayerInfluenceNews ?? true))
            {
                return false;
            }

            bool matched = SocialCircleService.TryAnalyzeDialogueKeywords(
                playerMessage,
                aiResponse,
                out SocialPostCategory category,
                out int sentiment,
                out string intentHint);
            if (!matched)
            {
                return false;
            }

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
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            string normalized = token.Trim();
            return GetEligibleSocialFactions()
                .FirstOrDefault(faction =>
                    faction != sourceFaction &&
                    (string.Equals(faction.Name, normalized, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(faction.def?.defName, normalized, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(faction.def?.label, normalized, StringComparison.OrdinalIgnoreCase)));
        }

        public List<PublicSocialPost> GetSocialPosts(int maxCount = MaxSocialPosts)
        {
            EnsureSocialCircleState();
            int count = Math.Max(1, maxCount);
            return socialCircleState.Posts
                .OrderByDescending(post => post.CreatedTick)
                .Take(count)
                .ToList();
        }

        public int GetUnreadSocialPostCount()
        {
            EnsureSocialCircleState();
            if (socialCircleState.Posts.Count == 0)
            {
                return 0;
            }

            if (string.IsNullOrEmpty(socialCircleState.LastReadPostId))
            {
                return socialCircleState.Posts.Count;
            }

            int index = socialCircleState.Posts.FindLastIndex(post => post.PostId == socialCircleState.LastReadPostId);
            return index < 0 ? socialCircleState.Posts.Count : Math.Max(0, socialCircleState.Posts.Count - index - 1);
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

        private void EnsureSocialCircleState()
        {
            if (socialCircleState == null)
            {
                socialCircleState = new SocialCircleState();
            }

            socialCircleState.Posts = socialCircleState.Posts ?? new List<PublicSocialPost>();
            socialCircleState.ActionIntents = socialCircleState.ActionIntents ?? new List<SocialActionIntent>();
            socialCircleState.FactionActionCooldowns = socialCircleState.FactionActionCooldowns ?? new List<SocialFactionActionCooldown>();
            socialCircleState.ProcessedOrigins = socialCircleState.ProcessedOrigins ?? new List<SocialProcessedOrigin>();
        }

        private void EnsureNextSocialPostTick(int currentTick)
        {
            if (socialCircleState.NextPostTick > currentTick)
            {
                return;
            }

            ScheduleNextSocialPost(currentTick);
        }

        private void ScheduleNextSocialPost(int currentTick)
        {
            socialCircleState.NextPostTick = currentTick + SocialCircleService.CalculateNextIntervalTicks(RimChatMod.Instance?.InstanceSettings);
        }

        private void TryGenerateScheduledSocialPost(int currentTick)
        {
            if (currentTick < socialCircleState.NextPostTick)
            {
                return;
            }

            TryQueueNextScheduledNews(DebugGenerateReason.Scheduled, currentTick, false);
            ScheduleNextSocialPost(currentTick);
        }

        private void TrimSocialPosts()
        {
            if (socialCircleState.Posts.Count <= MaxSocialPosts)
            {
                return;
            }

            int removeCount = socialCircleState.Posts.Count - MaxSocialPosts;
            socialCircleState.Posts.RemoveRange(0, removeCount);
        }

        private List<Faction> GetEligibleSocialFactions()
        {
            return Find.FactionManager.AllFactions
                .Where(faction => faction != null && !faction.defeated && !faction.def.hidden)
                .ToList();
        }

        private Faction ResolveMentionedFaction(string text, Faction sourceFaction)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string normalized = text.ToLowerInvariant();
            foreach (Faction faction in GetEligibleSocialFactions())
            {
                if (faction == sourceFaction)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(faction.Name) && normalized.Contains(faction.Name.ToLowerInvariant()))
                {
                    return faction;
                }

                if (!string.IsNullOrEmpty(faction.def?.label) && normalized.Contains(faction.def.label.ToLowerInvariant()))
                {
                    return faction;
                }
            }

            return null;
        }
    }
}
