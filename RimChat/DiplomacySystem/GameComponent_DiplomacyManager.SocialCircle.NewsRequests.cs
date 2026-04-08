using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.AI;
using RimChat.Memory;
using RimChat.Prompting;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>/// Dependencies: AIChatServiceAsync, native prompt fail-fast exceptions, social news seed factory, social news JSON parser, leader-memory services.
 /// Responsibility: queue, track, finalize social-news generation requests, and mirror published post summaries into faction leader memory.
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

            List<ChatMessageData> messages;
            try
            {
                messages = SocialNewsPromptBuilder.BuildMessages(seed);
                Log.Message(
                    "[RimChat][SocialNewsPrompt] "
                    + $"origin_type={seed.OriginType}, origin_key={seed.OriginKey ?? string.Empty}, "
                    + $"source_faction={seed.SourceFaction?.Name ?? "None"}, target_faction={seed.TargetFaction?.Name ?? "None"}, "
                    + $"facts={BuildResponsePreview(string.Join(" | ", (seed.Facts ?? new List<string>()).Where(item => !string.IsNullOrWhiteSpace(item))), 800)}, "
                    + $"prompt_input={BuildResponsePreview(SocialNewsPromptBuilder.BuildPromptInputPayloadForDebug(seed), 1000)}");
            }
            catch (RimTalkPromptRenderCompatibilityException ex)
            {
                RimTalkNativeRenderDiagnostic diagnostic = ex.Diagnostic;
                Log.Warning(
                    "[RimChat] Social news render fail-fast. " +
                    $"requestId=not_dispatched, debugSource={AIRequestDebugSource.SocialNews}, stage=render_failfast, " +
                    $"origin_type={seed.OriginType}, origin_key={seed.OriginKey ?? string.Empty}, " +
                    $"channel={diagnostic?.PromptChannel ?? string.Empty}, " +
                    $"bound_method={diagnostic?.BoundMethod ?? string.Empty}, " +
                    $"bound_variant={diagnostic?.BoundMethodVariant ?? string.Empty}, " +
                    $"failure_stage={diagnostic?.FailureStage ?? string.Empty}, " +
                    $"error={(ex.Message ?? diagnostic?.ErrorMessage ?? string.Empty)}");
                socialCircleState.MarkOriginState(seed.OriginType, seed.OriginKey, SocialNewsGenerationState.Failed, currentTick);
                AddSocialGenerationMessage(seed, false, SocialPostGenerationFailureReason.PromptRenderIncompatible);
                failureReason = SocialPostEnqueueFailureReason.PromptRenderIncompatible;
                return false;
            }

            socialCircleState.MarkOriginState(seed.OriginType, seed.OriginKey, SocialNewsGenerationState.Pending, currentTick);
            string localRequestId = string.Empty;
            localRequestId = AIChatServiceAsync.Instance.SendChatRequestAsync(
                messages,
                onSuccess: response => OnSocialNewsRequestSuccess(localRequestId, response),
                onError: error => OnSocialNewsRequestError(localRequestId, error),
                usageChannel: DialogueUsageChannel.Diplomacy,
                debugSource: AIRequestDebugSource.SocialNews);
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
            if (!SocialNewsJsonParser.TryParse(
                    response,
                    out SocialNewsDraft draft,
                    out string error,
                    pending.Seed?.PrimaryClaim ?? string.Empty,
                    pending.Seed?.QuoteAttributionHint ?? string.Empty))
            {
                Log.Warning(
                    "[RimChat] Social news generation failed to parse. " +
                    $"requestId={requestId ?? string.Empty}, debugSource={AIRequestDebugSource.SocialNews}, stage=parse_fail, " +
                    $"error={error}, response_preview={BuildResponsePreview(response, 260)}");
                socialCircleState.MarkOriginState(pending.Seed.OriginType, pending.Seed.OriginKey, SocialNewsGenerationState.Failed, currentTick);
                AddSocialGenerationMessage(pending.Seed, false, SocialPostGenerationFailureReason.ParseFailed);
                return;
            }

            PublicSocialPost post = SocialCircleService.CreatePostFromDraft(pending.Seed, draft);
            Log.Message(
                "[RimChat][SocialNewsDraft] "
                + $"origin_type={pending.Seed?.OriginType.ToString() ?? "Unknown"}, origin_key={pending.Seed?.OriginKey ?? string.Empty}, "
                + $"location_name={draft?.LocationName ?? string.Empty}, quote_attribution={draft?.QuoteAttribution ?? string.Empty}, "
                + $"headline={BuildResponsePreview(draft?.Headline ?? string.Empty, 160)}, lead={BuildResponsePreview(draft?.Lead ?? string.Empty, 220)}, "
                + $"quote={BuildResponsePreview(draft?.Quote ?? string.Empty, 220)}");
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

        private static string BuildResponsePreview(string response, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return string.Empty;
            }

            string normalized = response
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
            if (normalized.Length <= maxLength)
            {
                return normalized;
            }

            return normalized.Substring(0, Math.Max(0, maxLength)) + "...";
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

            if (ShouldSendSocialNewsLetter(post))
            {
                TrySendSocialNewsLetter(post);
            }
            MirrorSocialPostSummaryToLeaderMemories(post, currentTick);
            socialCircleState.MarkOriginState(seed.OriginType, seed.OriginKey, SocialNewsGenerationState.Completed, currentTick);
        }

        private static bool ShouldSendSocialNewsLetter(PublicSocialPost post)
        {
            return post != null && post.OriginType != SocialNewsOriginType.PlayerManual;
        }

        private void MirrorSocialPostSummaryToLeaderMemories(PublicSocialPost post, int fallbackTick)
        {
            if (!ShouldMirrorSocialPostSummary(post))
            {
                return;
            }

            string summary = BuildSocialPostSummaryText(post);
            if (string.IsNullOrWhiteSpace(summary))
            {
                return;
            }

            int tick = post.CreatedTick > 0 ? post.CreatedTick : fallbackTick;
            string contentHash = BuildSocialPostContentHash(post, tick);
            foreach (Faction targetFaction in GetSummaryMirrorTargetFactions())
            {
                CrossChannelSummaryRecord record = CreateSocialPostSummaryRecord(post, targetFaction, summary, tick, contentHash);
                LeaderMemoryManager.Instance.AddDiplomacySessionSummary(
                    targetFaction,
                    record,
                    DialogueSummaryService.MaxSummaryPoolPerType);
            }
        }

        private static bool ShouldMirrorSocialPostSummary(PublicSocialPost post)
        {
            return post != null
                && post.OriginType != SocialNewsOriginType.DiplomacySummary;
        }

        private static IEnumerable<Faction> GetSummaryMirrorTargetFactions()
        {
            return Find.FactionManager.AllFactions
                .Where(faction => faction != null && !faction.IsPlayer && !faction.defeated && !faction.def.hidden);
        }

        private static string BuildSocialPostSummaryText(PublicSocialPost post)
        {
            string headline = post?.Headline?.Trim() ?? string.Empty;
            string lead = post?.Lead?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(headline) && !string.IsNullOrWhiteSpace(lead))
            {
                return $"{headline} {lead}";
            }

            if (!string.IsNullOrWhiteSpace(headline))
            {
                return headline;
            }

            if (!string.IsNullOrWhiteSpace(lead))
            {
                return lead;
            }

            return post?.Content?.Trim() ?? string.Empty;
        }

        private static string BuildSocialPostContentHash(PublicSocialPost post, int tick)
        {
            if (!string.IsNullOrWhiteSpace(post?.PostId))
            {
                return $"social-post:{post.PostId}";
            }

            return $"social-post:{post?.OriginType}:{post?.OriginKey}:{tick}";
        }

        private static CrossChannelSummaryRecord CreateSocialPostSummaryRecord(
            PublicSocialPost post,
            Faction targetFaction,
            string summary,
            int tick,
            string contentHash)
        {
            return new CrossChannelSummaryRecord
            {
                Source = CrossChannelSummarySource.DiplomacySession,
                FactionId = targetFaction?.GetUniqueLoadID() ?? string.Empty,
                PawnLoadId = -1,
                PawnName = string.Empty,
                SummaryText = summary,
                KeyFacts = BuildSocialPostSummaryFacts(post),
                GameTick = tick,
                Confidence = 0.70f,
                ContentHash = contentHash ?? string.Empty,
                IsLlmFallback = false,
                CreatedTimestamp = System.DateTime.UtcNow.Ticks
            };
        }

        private static List<string> BuildSocialPostSummaryFacts(PublicSocialPost post)
        {
            string sourceName = post?.SourceFaction?.Name ?? "Unknown";
            string targetName = post?.TargetFaction?.Name ?? "None";
            string postId = string.IsNullOrWhiteSpace(post?.PostId) ? "none" : post.PostId;
            return new List<string>
            {
                $"post_id: {postId}",
                $"origin: {post?.OriginType.ToString() ?? "Unknown"}",
                $"category: {SocialCircleService.GetCategoryLabel(post?.Category ?? SocialPostCategory.Diplomatic)}",
                $"sentiment: {post?.Sentiment ?? 0}",
                $"source: {sourceName}",
                $"target: {targetName}"
            };
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
