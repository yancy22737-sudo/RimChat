using System.Collections.Generic;
using System.Linq;
using RimChat.AI;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>/// Dependencies: AIChatServiceAsync, social news seed factory, social news JSON parser.
 /// Responsibility: queue, track, and finalize asynchronous world-news generation requests for the social circle.
 ///</summary>
    public partial class GameComponent_DiplomacyManager
    {
        private const int MaxPendingSocialNewsRequests = 4;
        private const int FailedOriginAutoRetryDays = 2;
        private readonly Dictionary<string, PendingSocialNewsRequest> pendingSocialNewsRequests =
            new Dictionary<string, PendingSocialNewsRequest>();

        private void ClearSocialTransientState()
        {
            pendingSocialNewsRequests.Clear();
        }

        private bool TryQueueNextScheduledNews(DebugGenerateReason reason, int currentTick, bool bypassSimulationToggle)
        {
            if (!bypassSimulationToggle && !(RimChat.Core.RimChatMod.Instance?.InstanceSettings?.EnableAISimulationNews ?? true))
            {
                return false;
            }

            if (!CanGenerateSocialNews())
            {
                return false;
            }

            bool allowFailedRetry = reason == DebugGenerateReason.ManualButton;
            SocialNewsSeed seed = SelectNextScheduledSeed(allowFailedRetry, currentTick);
            if (seed == null)
            {
                return false;
            }

            seed.DebugReason = reason;
            return TryQueueNewsSeed(seed, currentTick, allowFailedRetry);
        }

        private bool TryQueueNextScheduledNews(
            DebugGenerateReason reason,
            int currentTick,
            bool bypassSimulationToggle,
            out SocialForceGenerateFailureReason failureReason)
        {
            failureReason = SocialForceGenerateFailureReason.Unknown;

            if (!bypassSimulationToggle && !(RimChat.Core.RimChatMod.Instance?.InstanceSettings?.EnableAISimulationNews ?? true))
            {
                failureReason = SocialForceGenerateFailureReason.Disabled;
                return false;
            }

            if (!CanGenerateSocialNews(out failureReason))
            {
                return false;
            }

            bool allowFailedRetry = reason == DebugGenerateReason.ManualButton;
            SocialNewsSeed seed = SelectNextScheduledSeed(allowFailedRetry, currentTick);
            if (seed == null)
            {
                failureReason = SocialForceGenerateFailureReason.NoAvailableSeed;
                return false;
            }

            seed.DebugReason = reason;
            bool queued = TryQueueNewsSeed(seed, currentTick, allowFailedRetry);
            if (!queued)
            {
                failureReason = SocialForceGenerateFailureReason.Unknown;
            }

            return queued;
        }

        private bool TryQueueNewsSeed(SocialNewsSeed seed, int currentTick, bool allowFailedRetry = false)
        {
            return TryQueueNewsSeed(
                seed,
                currentTick,
                out _,
                out _,
                allowFailedRetry);
        }

        private bool TryQueueNewsSeed(
            SocialNewsSeed seed,
            int currentTick,
            out string requestId,
            out SocialPostEnqueueFailureReason failureReason,
            bool allowFailedRetry = false)
        {
            requestId = string.Empty;
            failureReason = SocialPostEnqueueFailureReason.Unknown;

            if (seed == null || !seed.IsValid())
            {
                failureReason = SocialPostEnqueueFailureReason.InvalidSeed;
                return false;
            }

            if (!CanGenerateSocialNews(out SocialForceGenerateFailureReason forceFailure))
            {
                failureReason = MapForceFailureToEnqueueFailure(forceFailure);
                return false;
            }

            if (IsOriginBlocked(seed, allowFailedRetry, currentTick))
            {
                failureReason = SocialPostEnqueueFailureReason.OriginBlocked;
                return false;
            }

            List<ChatMessageData> messages = SocialNewsPromptBuilder.BuildMessages(seed);
            socialCircleState.MarkOriginState(seed.OriginType, seed.OriginKey, SocialNewsGenerationState.Pending, currentTick);
            string localRequestId = string.Empty;
            localRequestId = AIChatServiceAsync.Instance.SendChatRequestAsync(
                messages,
                onSuccess: response => OnSocialNewsRequestSuccess(localRequestId, response),
                onError: error => OnSocialNewsRequestError(localRequestId, error),
                usageChannel: DialogueUsageChannel.Diplomacy);
            requestId = localRequestId;
            if (string.IsNullOrEmpty(localRequestId))
            {
                socialCircleState.MarkOriginState(seed.OriginType, seed.OriginKey, SocialNewsGenerationState.Failed, currentTick);
                failureReason = SocialPostEnqueueFailureReason.RequestDispatchFailed;
                return false;
            }

            pendingSocialNewsRequests[localRequestId] = new PendingSocialNewsRequest
            {
                Seed = seed,
                QueuedTick = currentTick
            };
            failureReason = SocialPostEnqueueFailureReason.None;
            return true;
        }

        private bool CanGenerateSocialNews()
        {
            return AIChatServiceAsync.Instance != null
                && AIChatServiceAsync.Instance.IsConfigured()
                && pendingSocialNewsRequests.Count < MaxPendingSocialNewsRequests;
        }

        private bool CanGenerateSocialNews(out SocialForceGenerateFailureReason failureReason)
        {
            failureReason = SocialForceGenerateFailureReason.Unknown;

            if (AIChatServiceAsync.Instance == null || !AIChatServiceAsync.Instance.IsConfigured())
            {
                failureReason = SocialForceGenerateFailureReason.AiUnavailable;
                return false;
            }

            if (pendingSocialNewsRequests.Count >= MaxPendingSocialNewsRequests)
            {
                failureReason = SocialForceGenerateFailureReason.QueueFull;
                return false;
            }

            return true;
        }

        private SocialNewsSeed SelectNextScheduledSeed(bool allowFailedRetry, int currentTick)
        {
            return SocialNewsSeedFactory.CollectScheduledSeeds()
                .FirstOrDefault(seed =>
                    !HasPublishedOrigin(seed) &&
                    !IsOriginBlocked(seed, allowFailedRetry, currentTick));
        }

        private bool IsOriginBlocked(SocialNewsSeed seed, bool allowFailedRetry, int currentTick)
        {
            SocialProcessedOrigin entry = FindProcessedOrigin(seed);
            if (entry == null)
            {
                return false;
            }

            if (entry.State != SocialNewsGenerationState.Failed)
            {
                return true;
            }

            if (allowFailedRetry)
            {
                return false;
            }

            int retryTicks = FailedOriginAutoRetryDays * GenDate.TicksPerDay;
            return currentTick - entry.ProcessedTick < retryTicks;
        }

        private SocialProcessedOrigin FindProcessedOrigin(SocialNewsSeed seed)
        {
            if (seed == null || string.IsNullOrWhiteSpace(seed.OriginKey))
            {
                return null;
            }

            return socialCircleState.ProcessedOrigins?.FirstOrDefault(item =>
                item != null &&
                item.OriginType == seed.OriginType &&
                string.Equals(item.OriginKey, seed.OriginKey, System.StringComparison.Ordinal));
        }

        private bool HasPublishedOrigin(SocialNewsSeed seed)
        {
            return socialCircleState.Posts.Any(post =>
                post != null &&
                post.OriginType == seed.OriginType &&
                string.Equals(post.OriginKey, seed.OriginKey, System.StringComparison.Ordinal));
        }

        private void OnSocialNewsRequestSuccess(string requestId, string response)
        {
            if (!TryTakePendingSocialRequest(requestId, out PendingSocialNewsRequest pending))
            {
                return;
            }

            int currentTick = Find.TickManager?.TicksGame ?? pending.QueuedTick;
            if (!SocialNewsJsonParser.TryParse(response, out SocialNewsDraft draft, out string error))
            {
                Log.Warning($"[RimChat] Social news generation failed to parse: {error}");
                socialCircleState.MarkOriginState(pending.Seed.OriginType, pending.Seed.OriginKey, SocialNewsGenerationState.Failed, currentTick);
                AddSocialGenerationMessage(pending.Seed, false, SocialPostGenerationFailureReason.ParseFailed);
                return;
            }

            PublicSocialPost post = SocialCircleService.CreatePostFromDraft(pending.Seed, draft);
            if (post == null || HasPublishedOrigin(pending.Seed))
            {
                socialCircleState.MarkOriginState(pending.Seed.OriginType, pending.Seed.OriginKey, SocialNewsGenerationState.Failed, currentTick);
                AddSocialGenerationMessage(pending.Seed, false, SocialPostGenerationFailureReason.InvalidDraft);
                return;
            }

            AddCompletedSocialPost(post, pending.Seed, currentTick);
            AddSocialGenerationMessage(pending.Seed, true);
        }

        private void OnSocialNewsRequestError(string requestId, string error)
        {
            if (!TryTakePendingSocialRequest(requestId, out PendingSocialNewsRequest pending))
            {
                return;
            }

            int currentTick = Find.TickManager?.TicksGame ?? pending.QueuedTick;
            Log.Warning($"[RimChat] Social news generation failed: {error}");
            socialCircleState.MarkOriginState(pending.Seed.OriginType, pending.Seed.OriginKey, SocialNewsGenerationState.Failed, currentTick);
            AddSocialGenerationMessage(pending.Seed, false, SocialPostGenerationFailureReason.AiError);
        }

        private bool TryTakePendingSocialRequest(string requestId, out PendingSocialNewsRequest pending)
        {
            pending = null;
            if (string.IsNullOrEmpty(requestId) || !pendingSocialNewsRequests.TryGetValue(requestId, out pending))
            {
                return false;
            }

            pendingSocialNewsRequests.Remove(requestId);
            return pending?.Seed != null;
        }

        private void AddCompletedSocialPost(PublicSocialPost post, SocialNewsSeed seed, int currentTick)
        {
            socialCircleState.Posts.Add(post);
            TrimSocialPosts();
            if (seed.ApplyDiplomaticImpact)
            {
                SocialCircleService.ApplyDialogueConsequences(socialCircleState, post);
            }

            TrySendSocialNewsLetter(post);
            socialCircleState.MarkOriginState(seed.OriginType, seed.OriginKey, SocialNewsGenerationState.Completed, currentTick);
        }

        private void TrySendSocialNewsLetter(PublicSocialPost post)
        {
            if (post == null || Find.LetterStack == null)
            {
                return;
            }

            string source = post.SourceFaction?.Name;
            if (string.IsNullOrWhiteSpace(source))
            {
                source = "RimChat_SocialNoLeader".Translate();
            }

            string category = SocialCircleService.GetCategoryLabel(post.Category);
            string title = "RimChat_SocialNewsLetterTitle".Translate(source, category);
            string headline = string.IsNullOrWhiteSpace(post.Headline) ? post.Content ?? string.Empty : post.Headline;
            string lead = string.IsNullOrWhiteSpace(post.Lead) ? string.Empty : post.Lead;
            string body = "RimChat_SocialNewsLetterBody".Translate(headline, lead);
            Find.LetterStack.ReceiveLetter(title, body, ResolveSocialNewsLetterDef(post));
        }

        private static LetterDef ResolveSocialNewsLetterDef(PublicSocialPost post)
        {
            if (post == null)
            {
                return LetterDefOf.NeutralEvent;
            }

            if (post.Sentiment <= -2)
            {
                return LetterDefOf.ThreatBig;
            }

            if (post.Sentiment == -1)
            {
                return LetterDefOf.ThreatSmall;
            }

            if (post.Sentiment >= 1)
            {
                return LetterDefOf.PositiveEvent;
            }

            return LetterDefOf.NeutralEvent;
        }

        private sealed class PendingSocialNewsRequest
        {
            public SocialNewsSeed Seed;
            public int QueuedTick;
        }

        private static SocialPostEnqueueFailureReason MapForceFailureToEnqueueFailure(SocialForceGenerateFailureReason failureReason)
        {
            switch (failureReason)
            {
                case SocialForceGenerateFailureReason.Disabled:
                    return SocialPostEnqueueFailureReason.Disabled;
                case SocialForceGenerateFailureReason.AiUnavailable:
                    return SocialPostEnqueueFailureReason.AiUnavailable;
                case SocialForceGenerateFailureReason.QueueFull:
                    return SocialPostEnqueueFailureReason.QueueFull;
                default:
                    return SocialPostEnqueueFailureReason.Unknown;
            }
        }
    }
}
