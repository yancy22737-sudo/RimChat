using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Core;
using RimChat.Memory;
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
        private List<PublicSocialPost> cachedSortedPosts = new List<PublicSocialPost>();
        private bool socialPostsCacheDirty = true;
        private List<Faction> cachedEligibleFactions;
        private int eligibleFactionsCacheTick = -1;
        private const int EligibleFactionsCacheIntervalTicks = 60000;

        private void InitializeSocialCircleOnNewGame()
        {
            EnsureSocialCircleState();
            socialCircleState.Posts.Clear();
            socialCircleState.ActionIntents.Clear();
            socialCircleState.FactionActionCooldowns.Clear();
            socialCircleState.ProcessedOrigins.Clear();
            socialCircleState.ScheduledEvents.Clear();
            socialCircleState.LastReadPostId = string.Empty;
            ClearSocialTransientState();
            socialPostsCacheDirty = true;
            ScheduleNextSocialPost(Find.TickManager?.TicksGame ?? 0);
        }

        private void InitializeSocialCircleOnLoadedGame()
        {
            EnsureSocialCircleState();
            socialCircleState.CleanupInvalidEntries();
            socialCircleState.ClearPendingOrigins();
            ClearSocialTransientState();
            socialPostsCacheDirty = true;
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

        public bool TryForceGeneratePublicPost(
            DebugGenerateReason reason,
            out SocialForceGenerateFailureReason failureReason)
        {
            failureReason = SocialForceGenerateFailureReason.Unknown;

            if (!IsSocialCircleEnabled())
            {
                failureReason = SocialForceGenerateFailureReason.Disabled;
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            bool queued = TryQueueNextScheduledNews(reason, currentTick, true, out failureReason);

            if (!queued && failureReason == SocialForceGenerateFailureReason.NoAvailableSeed)
            {
                WorldState.WorldEventLedgerComponent.Instance?.CollectNow();
                queued = TryQueueNextScheduledNews(reason, currentTick, true, out failureReason);
            }

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
            return EnqueuePublicPost(
                sourceFaction,
                targetFaction,
                category,
                sentiment,
                summary,
                isFromPlayerDialogue,
                out _,
                intentHint,
                reason);
        }

        public bool EnqueuePublicPost(
            Faction sourceFaction,
            Faction targetFaction,
            SocialPostCategory category,
            int sentiment,
            string summary,
            bool isFromPlayerDialogue,
            out SocialPostEnqueueResult enqueueResult,
            string intentHint = "",
            DebugGenerateReason reason = DebugGenerateReason.DialogueExplicit)
        {
            enqueueResult = new SocialPostEnqueueResult
            {
                Triggered = true,
                FailureReason = SocialPostEnqueueFailureReason.Unknown
            };

            if (!IsSocialCircleEnabled())
            {
                enqueueResult.FailureReason = SocialPostEnqueueFailureReason.Disabled;
                return false;
            }

            if (sourceFaction == null)
            {
                enqueueResult.FailureReason = SocialPostEnqueueFailureReason.MissingSourceFaction;
                return false;
            }

            if (sourceFaction.defeated)
            {
                enqueueResult.FailureReason = SocialPostEnqueueFailureReason.SourceFactionDefeated;
                return false;
            }

            if (isFromPlayerDialogue && !(RimChatMod.Instance?.InstanceSettings?.EnablePlayerInfluenceNews ?? true))
            {
                enqueueResult.FailureReason = SocialPostEnqueueFailureReason.PlayerInfluenceDisabled;
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

            enqueueResult.OriginType = seed?.OriginType ?? SocialNewsOriginType.Unknown;
            enqueueResult.OriginKey = seed?.OriginKey ?? string.Empty;

            bool queued = TryQueueNewsSeed(
                seed,
                Find.TickManager?.TicksGame ?? 0,
                out string requestId,
                out SocialPostEnqueueFailureReason failureReason);
            enqueueResult.Queued = queued;
            enqueueResult.RequestId = requestId ?? string.Empty;
            enqueueResult.FailureReason = queued ? SocialPostEnqueueFailureReason.None : failureReason;
            return queued;
        }

        public bool TryCreateKeywordDialoguePost(Faction sourceFaction, string playerMessage, string aiResponse)
        {
            return TryCreateKeywordDialoguePost(sourceFaction, playerMessage, aiResponse, out _);
        }

        public bool TryCreateKeywordDialoguePost(
            Faction sourceFaction,
            string playerMessage,
            string aiResponse,
            out SocialPostEnqueueResult enqueueResult)
        {
            enqueueResult = new SocialPostEnqueueResult
            {
                Triggered = false,
                FailureReason = SocialPostEnqueueFailureReason.KeywordNotMatched
            };

            if (!IsSocialCircleEnabled())
            {
                enqueueResult.FailureReason = SocialPostEnqueueFailureReason.Disabled;
                return false;
            }

            if (sourceFaction == null)
            {
                enqueueResult.FailureReason = SocialPostEnqueueFailureReason.MissingSourceFaction;
                return false;
            }

            if (sourceFaction.defeated)
            {
                enqueueResult.FailureReason = SocialPostEnqueueFailureReason.SourceFactionDefeated;
                return false;
            }

            if (!(RimChatMod.Instance?.InstanceSettings?.EnablePlayerInfluenceNews ?? true))
            {
                enqueueResult.FailureReason = SocialPostEnqueueFailureReason.PlayerInfluenceDisabled;
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

            enqueueResult.Triggered = true;
            Faction targetFaction = ResolveMentionedFaction($"{playerMessage} {aiResponse}", sourceFaction);
            string summary = SocialNewsSeedFactory.TryBuildFactionDialoguePublicClaim(
                sourceFaction,
                category,
                sentiment,
                aiResponse,
                intentHint,
                targetFaction);
            return EnqueuePublicPost(
                sourceFaction,
                targetFaction,
                category,
                sentiment,
                summary,
                true,
                out enqueueResult,
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
            if (socialPostsCacheDirty)
            {
                cachedSortedPosts.Clear();
                for (int i = 0; i < socialCircleState.Posts.Count; i++)
                {
                    var post = socialCircleState.Posts[i];
                    if (post != null)
                        cachedSortedPosts.Add(post);
                }
                cachedSortedPosts.Sort((a, b) => (b?.CreatedTick ?? 0).CompareTo(a?.CreatedTick ?? 0));
                socialPostsCacheDirty = false;
            }
            if (cachedSortedPosts.Count <= count)
                return new List<PublicSocialPost>(cachedSortedPosts);
            return cachedSortedPosts.GetRange(0, count);
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
            socialCircleState.ScheduledEvents = socialCircleState.ScheduledEvents ?? new List<ScheduledSocialEventRecord>();
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
            socialPostsCacheDirty = true;
        }

        private List<Faction> GetEligibleSocialFactions()
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (cachedEligibleFactions != null && currentTick - eligibleFactionsCacheTick < EligibleFactionsCacheIntervalTicks)
                return cachedEligibleFactions;

            cachedEligibleFactions = Find.FactionManager.AllFactions
                .Where(faction => faction != null && !faction.defeated && !faction.def.hidden)
                .ToList();
            eligibleFactionsCacheTick = currentTick;
            return cachedEligibleFactions;
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

        private void AddSocialSystemMessage(Faction sourceFaction, string message)
        {
            if (sourceFaction == null || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            HandleInboundFactionMessage(
                sourceFaction,
                "System",
                message,
                DialogueMessageType.System,
                null,
                markUnread: false,
                forcePresenceOnline: true);
        }

        public void RecordScheduledSocialEvent(
            ScheduledSocialEventType eventType,
            Faction sourceFaction,
            Faction targetFaction,
            string summary,
            string detail,
            int value,
            string sourceKey)
        {
            if (eventType == ScheduledSocialEventType.Unknown || string.IsNullOrWhiteSpace(sourceKey))
            {
                return;
            }

            EnsureSocialCircleState();
            socialCircleState.AddScheduledEvent(new ScheduledSocialEventRecord
            {
                EventType = eventType,
                SourceKey = sourceKey,
                OccurredTick = Find.TickManager?.TicksGame ?? 0,
                SourceFaction = sourceFaction,
                TargetFaction = targetFaction,
                Summary = summary?.Trim() ?? string.Empty,
                Detail = detail?.Trim() ?? string.Empty,
                Value = value
            });
        }

        public List<ScheduledSocialEventRecord> GetRecentScheduledSocialEvents(int daysWindow)
        {
            EnsureSocialCircleState();
            int safeDays = Math.Max(1, daysWindow);
            int nowTick = Find.TickManager?.TicksGame ?? 0;
            int minTick = nowTick - (safeDays * GenDate.TicksPerDay);
            return socialCircleState.GetRecentScheduledEvents(minTick);
        }

        private void AddSocialGenerationMessage(
            SocialNewsSeed seed,
            bool success,
            SocialPostGenerationFailureReason failureReason = SocialPostGenerationFailureReason.None)
        {
            if (seed?.SourceFaction == null)
            {
                return;
            }

            if (success)
            {
                AddSocialSystemMessage(seed.SourceFaction, "RimChat_SocialActionGenerated".Translate());
                return;
            }

            string reasonLabel = GetSocialFailureReasonLabel(failureReason);
            AddSocialSystemMessage(seed.SourceFaction, "RimChat_SocialActionFailedReason".Translate(reasonLabel));
        }

        public static string GetSocialFailureReasonLabel(SocialPostEnqueueFailureReason reason)
        {
            return GetSocialFailureReasonKey(reason).Translate();
        }

        public static string GetSocialFailureReasonLabel(SocialPostGenerationFailureReason reason)
        {
            return GetSocialFailureReasonKey(reason).Translate();
        }

        private static string GetSocialFailureReasonKey(SocialPostEnqueueFailureReason reason)
        {
            switch (reason)
            {
                case SocialPostEnqueueFailureReason.Disabled:
                    return "RimChat_SocialFailureReason_disabled";
                case SocialPostEnqueueFailureReason.PlayerInfluenceDisabled:
                    return "RimChat_SocialFailureReason_player_influence_disabled";
                case SocialPostEnqueueFailureReason.MissingSourceFaction:
                case SocialPostEnqueueFailureReason.SourceFactionDefeated:
                    return "RimChat_SocialFailureReason_missing_source_faction";
                case SocialPostEnqueueFailureReason.AiUnavailable:
                    return "RimChat_SocialFailureReason_ai_unavailable";
                case SocialPostEnqueueFailureReason.QueueFull:
                    return "RimChat_SocialFailureReason_queue_full";
                case SocialPostEnqueueFailureReason.InvalidSeed:
                    return "RimChat_SocialFailureReason_invalid_seed";
                case SocialPostEnqueueFailureReason.OriginBlocked:
                    return "RimChat_SocialFailureReason_origin_blocked";
                case SocialPostEnqueueFailureReason.RequestDispatchFailed:
                    return "RimChat_SocialFailureReason_request_dispatch_failed";
                case SocialPostEnqueueFailureReason.KeywordNotMatched:
                    return "RimChat_SocialFailureReason_keyword_not_matched";
                case SocialPostEnqueueFailureReason.PromptRenderIncompatible:
                    return "RimChat_SocialFailureReason_prompt_render_incompatible";
                default:
                    return "RimChat_SocialFailureReason_unknown";
            }
        }

        private static string GetSocialFailureReasonKey(SocialPostGenerationFailureReason reason)
        {
            switch (reason)
            {
                case SocialPostGenerationFailureReason.ParseFailed:
                    return "RimChat_SocialFailureReason_parse_failed";
                case SocialPostGenerationFailureReason.AiError:
                    return "RimChat_SocialFailureReason_ai_error";
                case SocialPostGenerationFailureReason.InvalidDraft:
                    return "RimChat_SocialFailureReason_invalid_draft";
                case SocialPostGenerationFailureReason.PromptRenderIncompatible:
                    return "RimChat_SocialFailureReason_prompt_render_incompatible";
                default:
                    return "RimChat_SocialFailureReason_unknown";
            }
        }
    }
}
