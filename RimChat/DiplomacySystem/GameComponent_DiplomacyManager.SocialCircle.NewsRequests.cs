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

            SocialNewsSeed seed = SelectNextScheduledSeed();
            if (seed == null)
            {
                return false;
            }

            seed.DebugReason = reason;
            return TryQueueNewsSeed(seed, currentTick);
        }

        private bool TryQueueNewsSeed(SocialNewsSeed seed, int currentTick)
        {
            if (seed == null || !seed.IsValid() || !CanGenerateSocialNews())
            {
                return false;
            }

            if (socialCircleState.HasHandledOrigin(seed.OriginType, seed.OriginKey))
            {
                return false;
            }

            List<ChatMessageData> messages = SocialNewsPromptBuilder.BuildMessages(seed);
            string requestId = string.Empty;
            socialCircleState.MarkOriginState(seed.OriginType, seed.OriginKey, SocialNewsGenerationState.Pending, currentTick);
            requestId = AIChatServiceAsync.Instance.SendChatRequestAsync(
                messages,
                onSuccess: response => OnSocialNewsRequestSuccess(requestId, response),
                onError: error => OnSocialNewsRequestError(requestId, error),
                usageChannel: DialogueUsageChannel.Diplomacy);
            if (string.IsNullOrEmpty(requestId))
            {
                socialCircleState.MarkOriginState(seed.OriginType, seed.OriginKey, SocialNewsGenerationState.Failed, currentTick);
                return false;
            }

            pendingSocialNewsRequests[requestId] = new PendingSocialNewsRequest
            {
                Seed = seed,
                QueuedTick = currentTick
            };
            return true;
        }

        private bool CanGenerateSocialNews()
        {
            return AIChatServiceAsync.Instance != null
                && AIChatServiceAsync.Instance.IsConfigured()
                && pendingSocialNewsRequests.Count < MaxPendingSocialNewsRequests;
        }

        private SocialNewsSeed SelectNextScheduledSeed()
        {
            return SocialNewsSeedFactory.CollectScheduledSeeds()
                .FirstOrDefault(seed => !HasPublishedOrigin(seed) && !socialCircleState.HasHandledOrigin(seed.OriginType, seed.OriginKey));
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
                return;
            }

            PublicSocialPost post = SocialCircleService.CreatePostFromDraft(pending.Seed, draft);
            if (post == null || HasPublishedOrigin(pending.Seed))
            {
                socialCircleState.MarkOriginState(pending.Seed.OriginType, pending.Seed.OriginKey, SocialNewsGenerationState.Failed, currentTick);
                return;
            }

            AddCompletedSocialPost(post, pending.Seed, currentTick);
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

            socialCircleState.MarkOriginState(seed.OriginType, seed.OriginKey, SocialNewsGenerationState.Completed, currentTick);
        }

        private sealed class PendingSocialNewsRequest
        {
            public SocialNewsSeed Seed;
            public int QueuedTick;
        }
    }
}
