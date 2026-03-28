using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.AI;
using RimChat.Config;
using RimChat.Core;
using RimChat.DiplomacySystem;
using RimChat.Memory;
using RimChat.Persistence;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.NpcDialogue
{
    /// <summary>/// Dependencies: AIChatServiceAsync, GameComponent_DiplomacyManager, Verse.GameComponent.
 /// Responsibility: End-to-end orchestration for NPC proactive dialogue triggers, queueing, generation and delivery.
 ///</summary>
    public partial class GameComponent_NpcDialoguePushManager : GameComponent
    {
        private sealed class PendingGenerationContext
        {
            public NpcDialogueTriggerContext Context;
            public List<ChatMessageData> Messages;
            public int Attempt;
        }

        private const int TickPerHour = 2500;
        private const int TickPerDay = 60000;
        private const int RegularEvaluationInterval = 6000;
        private const int QueueProcessInterval = 600;
        private const int IncomingDrainInterval = 120;
        private const int ClickWindowTicks = 360;
        private const int ClickBusyThreshold = 12;
        private const int CausalMinDelayTicks = 250;
        private const int CausalMaxDelayTicks = 1000;
        private const int RecentInteractionWindowTicks = TickPerDay * 15;
        private const int DefaultGlobalDeliveryCooldownTicks = TickPerHour * 6;
        private const int DefaultFactionCooldownMinTicks = TickPerDay * 3;
        private const int DefaultFactionCooldownMaxTicks = TickPerDay * 7;
        private const int CandidateCacheMaintenanceIntervalTicks = 15000;
        private const int CandidateSessionSyncIntervalTicks = 30000;

        public static GameComponent_NpcDialoguePushManager Instance;

        private List<FactionNpcPushState> factionPushStates = new List<FactionNpcPushState>();
        private List<QueuedNpcDialogueTrigger> queuedTriggers = new List<QueuedNpcDialogueTrigger>();

        private readonly Queue<NpcDialogueTriggerContext> incomingTriggers = new Queue<NpcDialogueTriggerContext>();
        private readonly Dictionary<string, PendingGenerationContext> pendingRequests = new Dictionary<string, PendingGenerationContext>();
        private readonly Queue<int> clickTicks = new Queue<int>();
        private readonly HashSet<Faction> activeCandidateFactions = new HashSet<Faction>();
        private readonly Dictionary<Faction, int> candidateTouchTicks = new Dictionary<Faction, int>();
        private int lastGlobalDeliveredTick = -DefaultGlobalDeliveryCooldownTicks;
        private int lastCandidateCacheMaintenanceTick;
        private int lastCandidateSessionSyncTick;

        public GameComponent_NpcDialoguePushManager(Game game) : base()
        {
            Instance = this;
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            Instance = this;
            incomingTriggers.Clear();
            pendingRequests.Clear();
            clickTicks.Clear();
            activeCandidateFactions.Clear();
            candidateTouchTicks.Clear();
            lastGlobalDeliveredTick = -DefaultGlobalDeliveryCooldownTicks;
            lastCandidateCacheMaintenanceTick = 0;
            lastCandidateSessionSyncTick = 0;
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            Instance = this;
            incomingTriggers.Clear();
            pendingRequests.Clear();
            clickTicks.Clear();
            CleanupInvalidState();
            RebuildCandidateCache();
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref factionPushStates, "npcPushFactionStates", LookMode.Deep);
            Scribe_Collections.Look(ref queuedTriggers, "npcPushQueuedTriggers", LookMode.Deep);
            Scribe_Values.Look(ref lastGlobalDeliveredTick, "npcPushLastGlobalDeliveredTick", -DefaultGlobalDeliveryCooldownTicks);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                factionPushStates ??= new List<FactionNpcPushState>();
                queuedTriggers ??= new List<QueuedNpcDialogueTrigger>();
                if (lastGlobalDeliveredTick < -DefaultGlobalDeliveryCooldownTicks)
                {
                    lastGlobalDeliveredTick = -DefaultGlobalDeliveryCooldownTicks;
                }
                CleanupInvalidState();
                RebuildCandidateCache();
            }
        }

        public override void GameComponentTick()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.TickManager == null)
            {
                return;
            }

            int currentTick = Find.TickManager.TicksGame;
            TrackClickSignal(currentTick);

            if (currentTick % IncomingDrainInterval == 0)
            {
                DrainIncomingTriggers(currentTick);
            }

            if (currentTick % QueueProcessInterval == 0)
            {
                ProcessQueuedTriggers(currentTick);
            }

            if (currentTick % RegularEvaluationInterval == 0)
            {
                EvaluateRegularTriggers(currentTick);
            }
        }

        public void RegisterLowQualityTradeTrigger(Faction faction, int lowQualityCount, QualityCategory worstQuality)
        {
            if (!IsValidTargetFaction(faction) || lowQualityCount <= 0)
            {
                return;
            }

            int severity = worstQuality <= QualityCategory.Awful ? 3 : 2;
            string reason = $"low_quality_trade:{lowQualityCount}:{worstQuality}";
            EnqueueIncoming(new NpcDialogueTriggerContext
            {
                Faction = faction,
                TriggerType = NpcDialogueTriggerType.Causal,
                Category = NpcDialogueCategory.WarningThreat,
                SourceTag = "trade_quality",
                Severity = severity,
                Reason = reason,
                CreatedTick = Find.TickManager?.TicksGame ?? 0
            });
        }

        public void RegisterGoodwillShiftTrigger(Faction faction, int goodwillDelta, string reason, bool likelyHostile)
        {
            if (!IsValidTargetFaction(faction) || Math.Abs(goodwillDelta) < 10)
            {
                return;
            }

            NpcDialogueCategory category = goodwillDelta < 0
                ? NpcDialogueCategory.WarningThreat
                : NpcDialogueCategory.DiplomacyTask;
            int severity = likelyHostile ? 3 : (goodwillDelta < 0 ? 2 : 1);

            EnqueueIncoming(new NpcDialogueTriggerContext
            {
                Faction = faction,
                TriggerType = NpcDialogueTriggerType.Causal,
                Category = category,
                SourceTag = "goodwill_shift",
                Severity = severity,
                Reason = reason ?? string.Empty,
                CreatedTick = Find.TickManager?.TicksGame ?? 0,
                GoodwillDelta = goodwillDelta
            });

            if (goodwillDelta < 0)
            {
                AccumulateGoodwillLoss(faction, goodwillDelta);
            }
        }

        /// <summary>/// 注册自定义触发器（用于袭击消息等场景）
        ///</summary>
        public void RegisterCustomTrigger(NpcDialogueTriggerContext context)
        {
            if (context == null || context.Faction == null)
            {
                return;
            }
            EnqueueIncoming(context);
        }

        private void AccumulateGoodwillLoss(Faction faction, int goodwillDelta)
        {
            if (faction == null)
            {
                return;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            FactionNpcPushState state = GetOrCreateState(faction);

            if (currentTick - state.lastGoodwillLossRecordTick > TickPerDay)
            {
                state.accumulatedGoodwillLossLastDay = 0;
            }

            state.accumulatedGoodwillLossLastDay += Math.Abs(goodwillDelta);
            state.lastGoodwillLossRecordTick = currentTick;
        }

        public bool DebugForceRandomProactiveDialogue()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.FactionManager == null || Find.TickManager == null)
            {
                return false;
            }
            if (!AIChatServiceAsync.Instance.IsConfigured())
            {
                return false;
            }

            List<Faction> candidates = Find.FactionManager.AllFactions
                .Where(IsValidTargetFaction)
                .ToList();
            if (candidates.Count == 0)
            {
                return false;
            }

            Faction faction = candidates.RandomElement();
            var category = (NpcDialogueCategory)Rand.RangeInclusive(0, 1);
            int severity = category == NpcDialogueCategory.WarningThreat ? Rand.RangeInclusive(1, 3) : 1;
            var context = new NpcDialogueTriggerContext
            {
                Faction = faction,
                TriggerType = NpcDialogueTriggerType.Causal,
                Category = category,
                SourceTag = "debug_force",
                Reason = "manual_debug_trigger",
                Severity = severity,
                CreatedTick = Find.TickManager.TicksGame
            };

            GetOrCreateState(faction).lastInteractionTick = context.CreatedTick;
            HandleTriggerContext(context, context.CreatedTick);
            return true;
        }

        private void EnqueueIncoming(NpcDialogueTriggerContext context)
        {
            if (context == null || context.Faction == null)
            {
                return;
            }

            incomingTriggers.Enqueue(context);
        }

        private void DrainIncomingTriggers(int currentTick)
        {
            int safeguard = 0;
            while (incomingTriggers.Count > 0 && safeguard++ < 200)
            {
                NpcDialogueTriggerContext context = incomingTriggers.Dequeue();
                HandleTriggerContext(context, currentTick);
            }
        }

        private void HandleTriggerContext(NpcDialogueTriggerContext context, int currentTick)
        {
            if (context == null || !IsValidTargetFaction(context.Faction))
            {
                return;
            }
            if (context.Category == NpcDialogueCategory.WarningThreat && !context.BypassCategoryGate)
            {
                return;
            }

            FactionNpcPushState state = GetOrCreateState(context.Faction);
            state.lastInteractionTick = currentTick;
            MarkFactionCandidate(context.Faction, currentTick);
            if (context.GoodwillDelta <= -10f)
            {
                state.lastNegativeSpikeTick = currentTick;
            }

            int dueTick = currentTick;
            if (context.TriggerType == NpcDialogueTriggerType.Causal)
            {
                dueTick += Rand.RangeInclusive(CausalMinDelayTicks, CausalMaxDelayTicks);
            }

            if (IsFactionPending(context.Faction))
            {
                dueTick = Math.Max(dueTick, currentTick + 300);
            }

            if (ShouldRespectCooldown(context, currentTick))
            {
                dueTick = Math.Max(dueTick, state.nextAllowedTick);
                LogThrottleDebug($"faction_cooldown gate: faction={context.Faction?.Name}, due={dueTick}, now={currentTick}");
            }

            if (!context.BypassRateLimit)
            {
                int globalNextAllowedTick = GetGlobalNextAllowedTick(currentTick);
                dueTick = Math.Max(dueTick, globalNextAllowedTick);
                if (globalNextAllowedTick > currentTick)
                {
                    LogThrottleDebug($"global_cooldown gate: faction={context.Faction?.Name}, due={globalNextAllowedTick}, now={currentTick}");
                }
            }

            int reinitiateRemainingTicks = context.BypassRateLimit
                ? 0
                : GetReinitiateCooldownRemainingTicks(context.Faction, currentTick);
            if (reinitiateRemainingTicks > 0)
            {
                dueTick = Math.Max(dueTick, currentTick + reinitiateRemainingTicks);
            }

            bool bypassBusyGate = context.BypassRateLimit || context.BypassPlayerBusyGate;
            if ((!bypassBusyGate && IsPlayerBusy()) || IsFactionUnavailable(context.Faction))
            {
                dueTick = Math.Max(dueTick, currentTick + 300);
            }

            if (dueTick <= currentTick)
            {
                StartGeneration(context);
                return;
            }

            QueueTrigger(context, dueTick, currentTick);
        }

        private void ProcessQueuedTriggers(int currentTick)
        {
            CleanupExpiredQueue(currentTick);

            List<QueuedNpcDialogueTrigger> dueItems = queuedTriggers
                .Where(q => q != null && q.dueTick <= currentTick)
                .OrderBy(q => q.dueTick)
                .ToList();

            int processed = 0;
            foreach (QueuedNpcDialogueTrigger item in dueItems)
            {
                if (processed >= 3)
                {
                    break;
                }

                if (!IsValidTargetFaction(item.faction))
                {
                    queuedTriggers.Remove(item);
                    continue;
                }

                NpcDialogueTriggerContext context = item.ToContext();
                if (IsFactionPending(context.Faction))
                {
                    continue;
                }

                bool bypassBusyGate = context.BypassRateLimit || context.BypassPlayerBusyGate;
                if ((!bypassBusyGate && IsPlayerBusy()) || IsFactionUnavailable(context.Faction))
                {
                    continue;
                }

                if (ShouldRespectCooldown(context, currentTick))
                {
                    FactionNpcPushState state = GetOrCreateState(context.Faction);
                    item.dueTick = Math.Max(item.dueTick, state.nextAllowedTick);
                    LogThrottleDebug($"queue faction_cooldown gate: faction={context.Faction?.Name}, due={item.dueTick}, now={currentTick}");
                    continue;
                }

                if (!context.BypassRateLimit)
                {
                    int globalNextAllowedTick = GetGlobalNextAllowedTick(currentTick);
                    if (globalNextAllowedTick > currentTick)
                    {
                        item.dueTick = Math.Max(item.dueTick, globalNextAllowedTick);
                        LogThrottleDebug($"queue global_cooldown gate: faction={context.Faction?.Name}, due={item.dueTick}, now={currentTick}");
                        continue;
                    }
                }

                int reinitiateRemainingTicks = context.BypassRateLimit
                    ? 0
                    : GetReinitiateCooldownRemainingTicks(context.Faction, currentTick);
                if (reinitiateRemainingTicks > 0)
                {
                    item.dueTick = Math.Max(item.dueTick, currentTick + reinitiateRemainingTicks);
                    continue;
                }

                queuedTriggers.Remove(item);
                StartGeneration(context);
                processed++;
            }
        }

        private void EvaluateRegularTriggers(int currentTick)
        {
            RimChatSettings settings = RimChatMod.Instance?.InstanceSettings;
            if (settings == null || !settings.EnableNpcInitiatedDialogue)
            {
                return;
            }

            float chance = GetRegularTriggerChance(settings.NpcPushFrequencyMode);
            List<Faction> candidates = GetActiveCandidateFactions(currentTick);
            foreach (Faction faction in candidates)
            {
                if (Rand.Value > chance || IsFactionPending(faction))
                {
                    continue;
                }

                var context = BuildRegularTrigger(faction, currentTick);
                if (context == null || ShouldRespectCooldown(context, currentTick))
                {
                    continue;
                }

                HandleTriggerContext(context, currentTick);
            }
        }

        private NpcDialogueTriggerContext BuildRegularTrigger(Faction faction, int currentTick)
        {
            if (!IsValidTargetFaction(faction))
            {
                return null;
            }

            int goodwill = faction.PlayerGoodwill;
            NpcDialogueCategory category;
            NpcDialogueTriggerType triggerType;
            int severity = 1;
            string reason = "regular_check";

            if (goodwill <= -40)
            {
                return null;
            }
            else if (goodwill >= 40)
            {
                category = NpcDialogueCategory.DiplomacyTask;
                triggerType = NpcDialogueTriggerType.Conditional;
                reason = "friendly_relationship";
            }
            else
            {
                category = NpcDialogueCategory.Social;
                triggerType = NpcDialogueTriggerType.Ambient;
                reason = "ambient_social";
            }

            return new NpcDialogueTriggerContext
            {
                Faction = faction,
                TriggerType = triggerType,
                Category = category,
                Severity = severity,
                Reason = reason,
                SourceTag = "regular",
                CreatedTick = currentTick
            };
        }

        private void StartGeneration(NpcDialogueTriggerContext context)
        {
            if (context == null || !IsValidTargetFaction(context.Faction))
            {
                return;
            }

            if (!AIChatServiceAsync.Instance.IsConfigured())
            {
                Log.Warning($"[RimChat] Proactive push dropped (AI not configured): {context.Faction.Name}");
                return;
            }

            List<ChatMessageData> messages = BuildGenerationMessages(context);
            string requestId = string.Empty;
            requestId = AIChatServiceAsync.Instance.SendChatRequestAsync(
                messages,
                onSuccess: response => OnGenerationSuccess(requestId, response),
                onError: error => OnGenerationError(requestId, error),
                usageChannel: DialogueUsageChannel.Diplomacy,
                debugSource: AIRequestDebugSource.NpcPush);

            if (string.IsNullOrEmpty(requestId))
            {
                return;
            }

            pendingRequests[requestId] = new PendingGenerationContext
            {
                Context = context,
                Messages = messages,
                Attempt = 1
            };
        }

        private void OnGenerationSuccess(string requestId, string response)
        {
            if (string.IsNullOrEmpty(requestId) || !pendingRequests.TryGetValue(requestId, out PendingGenerationContext pending))
            {
                return;
            }

            pendingRequests.Remove(requestId);
            string message = SanitizeModelOutput(response);
            if (string.IsNullOrWhiteSpace(message))
            {
                if (TryDeliverFallbackMessage(pending.Context))
                {
                    return;
                }

                Log.Warning("[RimChat] Proactive push generation empty after sanitize.");
                return;
            }

            DeliverMessage(pending.Context, message);
        }

        private void OnGenerationError(string requestId, string error)
        {
            if (string.IsNullOrEmpty(requestId) || !pendingRequests.TryGetValue(requestId, out PendingGenerationContext pending))
            {
                return;
            }

            pendingRequests.Remove(requestId);
            if (pending.Attempt < 2 && AIChatServiceAsync.Instance.IsConfigured())
            {
                RetryGeneration(pending);
                return;
            }

            if (TryDeliverFallbackMessage(pending.Context))
            {
                return;
            }

            Log.Warning($"[RimChat] Proactive push dropped after retry: {error}");
        }

        private void RetryGeneration(PendingGenerationContext pending)
        {
            string retryId = string.Empty;
            retryId = AIChatServiceAsync.Instance.SendChatRequestAsync(
                pending.Messages,
                onSuccess: response => OnGenerationSuccess(retryId, response),
                onError: error => OnGenerationError(retryId, error),
                usageChannel: DialogueUsageChannel.Diplomacy,
                debugSource: AIRequestDebugSource.NpcPush);

            if (string.IsNullOrEmpty(retryId))
            {
                return;
            }

            pendingRequests[retryId] = new PendingGenerationContext
            {
                Context = pending.Context,
                Messages = pending.Messages,
                Attempt = pending.Attempt + 1
            };
        }

        private void DeliverMessage(NpcDialogueTriggerContext context, string text)
        {
            if (context == null || !IsValidTargetFaction(context.Faction))
            {
                return;
            }

            GameComponent_DiplomacyManager.Instance?.ForcePresenceOnlineForNpcInitiated(context.Faction);

            AddMessageToSession(context.Faction, text);
            if (!ChoiceLetter_NpcInitiatedDialogue.IsDialogueAlreadyOpen(context.Faction))
            {
                SendProactiveLetter(context, text);
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            FactionNpcPushState state = GetOrCreateState(context.Faction);
            state.lastPushTick = currentTick;
            state.lastInteractionTick = currentTick;
            MarkFactionCandidate(context.Faction, currentTick);
            if (!context.BypassRateLimit)
            {
                state.nextAllowedTick = currentTick + Rand.RangeInclusive(GetFactionCooldownMinTicks(), GetFactionCooldownMaxTicks());
                lastGlobalDeliveredTick = currentTick;
            }
        }

        private void AddMessageToSession(Faction faction, string text)
        {
            var diplomacyManager = GameComponent_DiplomacyManager.Instance;
            if (diplomacyManager == null || faction == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string sender = faction.leader?.Name?.ToStringShort ?? faction.Name ?? "Unknown";
            diplomacyManager.HandleInboundFactionMessage(
                faction,
                sender,
                text,
                DialogueMessageType.Normal,
                faction.leader,
                markUnread: true,
                forcePresenceOnline: true);
        }

        private void SendProactiveLetter(NpcDialogueTriggerContext context, string text)
        {
            TaggedString title = GetLetterTitle(context);
            LetterDef def = GetLetterDef(context);
            var letter = new ChoiceLetter_NpcInitiatedDialogue();
            letter.Setup(context.Faction, title, text, def);
            Find.LetterStack.ReceiveLetter(letter, string.Empty, 0, true);
        }

        private TaggedString GetLetterTitle(NpcDialogueTriggerContext context)
        {
            string key = context.Category switch
            {
                NpcDialogueCategory.DiplomacyTask => "RimChat_NpcPush_TitleTask",
                NpcDialogueCategory.WarningThreat => "RimChat_NpcPush_TitleWarning",
                _ => "RimChat_NpcPush_TitleSocial"
            };
            return key.Translate(context.Faction?.Name ?? "Unknown");
        }

        private LetterDef GetLetterDef(NpcDialogueTriggerContext context)
        {
            if (context.Category == NpcDialogueCategory.WarningThreat)
            {
                return context.Severity >= 3 ? LetterDefOf.ThreatBig : LetterDefOf.ThreatSmall;
            }

            return context.Category == NpcDialogueCategory.DiplomacyTask
                ? LetterDefOf.PositiveEvent
                : LetterDefOf.NeutralEvent;
        }

        private List<ChatMessageData> BuildGenerationMessages(NpcDialogueTriggerContext context)
        {
            var messages = new List<ChatMessageData>();
            PromptPersistenceService.Instance.Initialize();
            List<string> sceneTags = BuildProactiveSceneTags(context?.Category ?? NpcDialogueCategory.Social);
            string basePrompt = PromptPersistenceService.Instance.BuildFullSystemPrompt(
                context.Faction,
                PromptPersistenceService.Instance.LoadConfig(),
                true,
                sceneTags);
            messages.Add(new ChatMessageData { role = "system", content = basePrompt });
            AppendRecentSessionContext(messages, context.Faction);

            string categoryText = context.Category switch
            {
                NpcDialogueCategory.DiplomacyTask => "diplomacy_or_task",
                NpcDialogueCategory.WarningThreat => "warning_or_threat",
                _ => "casual_social"
            };

            string userPrompt =
                $"Generate one proactive diplomacy message now.\n" +
                $"Category: {categoryText}\n" +
                $"TriggerType: {context.TriggerType}\n" +
                $"Reason: {context.Reason}\n" +
                $"Severity: {context.Severity}\n";

            int rapidDeclineLoss = GetAccumulatedGoodwillLoss(context.Faction);
            if (rapidDeclineLoss > 30)
            {
                userPrompt += $"\n[DynamicOverride] {rapidDeclineLoss} points of goodwill lost in recent days. The faction's attitude toward the player has deteriorated significantly, making them more inclined to initiate hostile actions or even raids.\n";
            }

            messages.Add(new ChatMessageData { role = "user", content = userPrompt });
            AppendManualSocialPostPrompt(messages, context);
            return messages;
        }

        private int GetAccumulatedGoodwillLoss(Faction faction)
        {
            if (faction == null)
            {
                return 0;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            FactionNpcPushState state = GetOrCreateState(faction);

            if (currentTick - state.lastGoodwillLossRecordTick > TickPerDay)
            {
                return 0;
            }

            return state.accumulatedGoodwillLossLastDay;
        }

        private List<string> BuildProactiveSceneTags(NpcDialogueCategory category)
        {
            var tags = new List<string>();
            switch (category)
            {
                case NpcDialogueCategory.DiplomacyTask:
                    tags.Add("scene:task");
                    break;
                case NpcDialogueCategory.WarningThreat:
                    tags.Add("scene:threat");
                    break;
                default:
                    tags.Add("scene:social");
                    break;
            }

            return tags;
        }

        private void AppendRecentSessionContext(List<ChatMessageData> messages, Faction faction)
        {
            if (messages == null || faction == null)
            {
                return;
            }

            FactionDialogueSession session = GameComponent_DiplomacyManager.Instance?.GetSession(faction);
            if (session?.messages == null || session.messages.Count == 0)
            {
                return;
            }

            int start = Math.Max(0, session.messages.Count - 4);
            for (int i = start; i < session.messages.Count; i++)
            {
                DialogueMessageData msg = session.messages[i];
                messages.Add(new ChatMessageData
                {
                    role = msg.isPlayer ? "user" : "assistant",
                    content = msg.message ?? string.Empty
                });
            }
        }

        private string SanitizeModelOutput(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return string.Empty;
            }

            string cleaned = output.Replace("```json", string.Empty).Replace("```", string.Empty).Trim();
            int jsonIndex = cleaned.IndexOf('{');
            if (jsonIndex >= 0)
            {
                cleaned = cleaned.Substring(0, jsonIndex).Trim();
            }

            string[] lines = cleaned
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();

            if (lines.Length == 0)
            {
                return string.Empty;
            }

            string merged = string.Join(" ", lines);
            int hardLimit = RimChatMod.Settings?.ProactiveMessageHardLimit ?? 0;
            if (hardLimit > 0 && merged.Length > hardLimit)
            {
                merged = merged.Substring(0, hardLimit).TrimEnd();
            }

            ImmersionGuardResult guardResult = ImmersionOutputGuard.ValidateVisibleDialogue(merged);
            if (!guardResult.IsValid)
            {
                Log.Warning($"[RimChat] Immersion guard blocked NPC push text: reason={ImmersionOutputGuard.BuildViolationTag(guardResult.ViolationReason)}, snippet={guardResult.ViolationSnippet}");
                return ImmersionOutputGuard.BuildLocalFallbackDialogue(DialogueUsageChannel.Diplomacy);
            }

            return guardResult.VisibleDialogue;
        }

        private void QueueTrigger(NpcDialogueTriggerContext context, int dueTick, int nowTick)
        {
            RimChatSettings settings = RimChatMod.Instance?.InstanceSettings;
            int maxPerFaction = Mathf.Clamp(settings?.NpcQueueMaxPerFaction ?? 3, 1, 10);
            int expireTicks = Mathf.RoundToInt((settings?.NpcQueueExpireHours ?? 12f) * TickPerHour);
            expireTicks = Mathf.Max(expireTicks, TickPerHour);

            List<QueuedNpcDialogueTrigger> sameFaction = queuedTriggers
                .Where(q => q?.faction == context.Faction)
                .OrderBy(q => q.enqueuedTick)
                .ToList();

            if (sameFaction.Count >= maxPerFaction)
            {
                queuedTriggers.Remove(sameFaction[0]);
            }

            var item = QueuedNpcDialogueTrigger.FromContext(
                context,
                nowTick,
                dueTick,
                nowTick + expireTicks);
            queuedTriggers.Add(item);
            MarkFactionCandidate(context.Faction, nowTick);
            LogThrottleDebug($"queue add: faction={context.Faction?.Name}, due={dueTick}, expire={nowTick + expireTicks}, reason={context.SourceTag}");
        }

        private void CleanupExpiredQueue(int currentTick)
        {
            queuedTriggers.RemoveAll(q =>
                q == null ||
                q.faction == null ||
                q.faction.defeated ||
                q.expireTick <= currentTick);
        }

        public int CancelQueuedTriggersForFaction(Faction faction, string reason = "manual")
        {
            if (faction == null)
            {
                return 0;
            }

            int removed = queuedTriggers.RemoveAll(q => q != null && q.faction == faction);
            if (removed > 0)
            {
                LogThrottleDebug($"queue clear: faction={faction.Name}, removed={removed}, reason={reason}");
            }
            return removed;
        }

        private bool ShouldRespectCooldown(NpcDialogueTriggerContext context, int currentTick)
        {
            if (context == null || context.Faction == null || CanBypassCooldown(context))
            {
                return false;
            }

            return GetOrCreateState(context.Faction).nextAllowedTick > currentTick;
        }

        private int GetReinitiateCooldownRemainingTicks(Faction faction, int currentTick)
        {
            if (faction == null)
            {
                return 0;
            }

            FactionDialogueSession session = GameComponent_DiplomacyManager.Instance?.GetSession(faction);
            if (session == null || !session.isConversationEndedByNpc)
            {
                return 0;
            }

            return Math.Max(0, session.GetReinitiateRemainingTicks(currentTick));
        }

        private int GetGlobalNextAllowedTick(int currentTick)
        {
            if (lastGlobalDeliveredTick <= 0)
            {
                return currentTick;
            }

            return lastGlobalDeliveredTick + GetGlobalDeliveryCooldownTicks();
        }

        private bool CanBypassCooldown(NpcDialogueTriggerContext context)
        {
            return context != null &&
                   (context.BypassRateLimit ||
                    (context.TriggerType == NpcDialogueTriggerType.Causal &&
                     context.Category == NpcDialogueCategory.WarningThreat &&
                     context.Severity >= 3));
        }

        private bool IsFactionUnavailable(Faction faction)
        {
            if (!IsValidTargetFaction(faction))
            {
                return true;
            }

            FactionPresenceStatus status = GameComponent_DiplomacyManager.Instance?.GetPresenceStatus(faction)
                ?? FactionPresenceStatus.Online;
            return status != FactionPresenceStatus.Online;
        }

        private bool IsValidTargetFaction(Faction faction)
        {
            return faction != null && !faction.IsPlayer && !faction.defeated && !(faction.def?.hidden ?? true);
        }

        private bool IsFactionPending(Faction faction)
        {
            if (faction == null)
            {
                return false;
            }

            foreach (var pair in pendingRequests)
            {
                if (pair.Value?.Context?.Faction == faction)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsPlayerBusy()
        {
            RimChatSettings settings = RimChatMod.Instance?.InstanceSettings;
            if (settings == null)
            {
                return false;
            }

            if (settings.EnableBusyByDrafted && IsBusyByDrafted())
            {
                return true;
            }

            if (settings.EnableBusyByHostiles && IsBusyByHostiles())
            {
                return true;
            }

            return settings.EnableBusyByClickRate && clickTicks.Count >= ClickBusyThreshold;
        }

        private bool IsBusyByDrafted()
        {
            if (Find.Maps == null)
            {
                return false;
            }

            foreach (Map map in Find.Maps)
            {
                if (map?.mapPawns?.FreeColonistsSpawned == null)
                {
                    continue;
                }

                if (map.mapPawns.FreeColonistsSpawned.Any(p => p != null && p.Drafted))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsBusyByHostiles()
        {
            if (Find.Maps == null)
            {
                return false;
            }

            foreach (Map map in Find.Maps)
            {
                if (map == null || !map.IsPlayerHome || map.mapPawns?.AllPawnsSpawned == null)
                {
                    continue;
                }

                if (map.mapPawns.AllPawnsSpawned.Any(p => p != null && p.HostileTo(Faction.OfPlayer)))
                {
                    return true;
                }
            }

            return false;
        }

        private void TrackClickSignal(int currentTick)
        {
            RimChatSettings settings = RimChatMod.Instance?.InstanceSettings;
            if (settings?.EnableBusyByClickRate != true)
            {
                clickTicks.Clear();
                return;
            }

            while (clickTicks.Count > 0 && currentTick - clickTicks.Peek() > ClickWindowTicks)
            {
                clickTicks.Dequeue();
            }
        }

        public void RegisterPlayerLeftClick()
        {
            RimChatSettings settings = RimChatMod.Instance?.InstanceSettings;
            if (settings?.EnableBusyByClickRate != true || Find.TickManager == null)
            {
                return;
            }

            clickTicks.Enqueue(Find.TickManager.TicksGame);
        }

        private List<Faction> GetActiveCandidateFactions(int currentTick)
        {
            MaintainCandidateCache(currentTick);
            var results = new List<Faction>(activeCandidateFactions.Count);
            foreach (Faction faction in activeCandidateFactions)
            {
                if (!IsValidTargetFaction(faction))
                {
                    continue;
                }

                if (IsCandidateStillActive(faction, currentTick))
                {
                    results.Add(faction);
                }
            }

            return results;
        }

        private FactionNpcPushState GetOrCreateState(Faction faction)
        {
            FactionNpcPushState state = factionPushStates.FirstOrDefault(s => s?.faction == faction);
            if (state != null)
            {
                return state;
            }

            state = new FactionNpcPushState
            {
                faction = faction,
                lastInteractionTick = Find.TickManager?.TicksGame ?? 0
            };
            factionPushStates.Add(state);
            return state;
        }

        private void CleanupInvalidState()
        {
            factionPushStates.RemoveAll(s => s == null || s.faction == null || s.faction.defeated);
            queuedTriggers.RemoveAll(q =>
                q == null ||
                q.faction == null ||
                q.faction.defeated ||
                (q.category == NpcDialogueCategory.WarningThreat && !q.bypassCategoryGate));
        }

        private void MaintainCandidateCache(int currentTick)
        {
            if (currentTick - lastCandidateSessionSyncTick >= CandidateSessionSyncIntervalTicks)
            {
                SyncCandidateCacheFromRecentSessions(currentTick);
                lastCandidateSessionSyncTick = currentTick;
            }

            if (currentTick - lastCandidateCacheMaintenanceTick < CandidateCacheMaintenanceIntervalTicks)
            {
                return;
            }

            lastCandidateCacheMaintenanceTick = currentTick;
            if (activeCandidateFactions.Count == 0)
            {
                return;
            }

            var stale = new List<Faction>();
            foreach (Faction faction in activeCandidateFactions)
            {
                if (!IsCandidateStillActive(faction, currentTick))
                {
                    stale.Add(faction);
                }
            }

            foreach (Faction faction in stale)
            {
                activeCandidateFactions.Remove(faction);
                candidateTouchTicks.Remove(faction);
            }
        }

        private void SyncCandidateCacheFromRecentSessions(int currentTick)
        {
            GameComponent_DiplomacyManager manager = GameComponent_DiplomacyManager.Instance;
            if (manager == null)
            {
                return;
            }

            foreach (Faction faction in manager.GetFactionsWithDialogue())
            {
                FactionDialogueSession session = manager.GetSession(faction);
                if (session == null || currentTick - session.lastInteractionTick > RecentInteractionWindowTicks)
                {
                    continue;
                }

                MarkFactionCandidate(faction, session.lastInteractionTick);
            }
        }

        private bool IsCandidateStillActive(Faction faction, int currentTick)
        {
            if (!IsValidTargetFaction(faction))
            {
                return false;
            }

            if (IsFactionPending(faction))
            {
                return true;
            }

            if (queuedTriggers.Any(q => q != null && q.faction == faction))
            {
                return true;
            }

            if (candidateTouchTicks.TryGetValue(faction, out int touchedTick) &&
                currentTick - touchedTick <= RecentInteractionWindowTicks)
            {
                return true;
            }

            FactionNpcPushState state = factionPushStates.FirstOrDefault(s => s?.faction == faction);
            if (state != null && currentTick - state.lastInteractionTick <= RecentInteractionWindowTicks)
            {
                MarkFactionCandidate(faction, state.lastInteractionTick);
                return true;
            }

            FactionDialogueSession session = GameComponent_DiplomacyManager.Instance?.GetSession(faction);
            if (session != null && currentTick - session.lastInteractionTick <= RecentInteractionWindowTicks)
            {
                MarkFactionCandidate(faction, session.lastInteractionTick);
                return true;
            }

            return false;
        }

        private void MarkFactionCandidate(Faction faction, int tick)
        {
            if (!IsValidTargetFaction(faction))
            {
                return;
            }

            activeCandidateFactions.Add(faction);
            if (!candidateTouchTicks.TryGetValue(faction, out int existing) || tick > existing)
            {
                candidateTouchTicks[faction] = tick;
            }
        }

        private void RebuildCandidateCache()
        {
            activeCandidateFactions.Clear();
            candidateTouchTicks.Clear();

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            foreach (FactionNpcPushState state in factionPushStates)
            {
                if (state?.faction == null)
                {
                    continue;
                }

                MarkFactionCandidate(state.faction, state.lastInteractionTick);
            }

            foreach (QueuedNpcDialogueTrigger queued in queuedTriggers)
            {
                if (queued?.faction == null)
                {
                    continue;
                }

                MarkFactionCandidate(queued.faction, currentTick);
            }

            SyncCandidateCacheFromRecentSessions(currentTick);
        }

        private int GetGlobalDeliveryCooldownTicks()
        {
            RimChatSettings settings = RimChatMod.Instance?.InstanceSettings;
            float hours = settings?.NpcGlobalDeliveryCooldownHours ?? 6f;
            return Mathf.Max(TickPerHour, Mathf.RoundToInt(hours * TickPerHour));
        }

        private int GetFactionCooldownMinTicks()
        {
            RimChatSettings settings = RimChatMod.Instance?.InstanceSettings;
            int days = settings?.NpcFactionCooldownMinDays ?? 3;
            return Mathf.Max(TickPerDay, days * TickPerDay);
        }

        private int GetFactionCooldownMaxTicks()
        {
            RimChatSettings settings = RimChatMod.Instance?.InstanceSettings;
            int minDays = settings?.NpcFactionCooldownMinDays ?? 3;
            int maxDays = settings?.NpcFactionCooldownMaxDays ?? 7;
            int resolved = Math.Max(minDays, maxDays);
            return Mathf.Max(TickPerDay, resolved * TickPerDay);
        }

        private void LogThrottleDebug(string message)
        {
            RimChatSettings settings = RimChatMod.Instance?.InstanceSettings;
            if (settings?.EnableNpcPushThrottleDebugLog != true)
            {
                return;
            }

            Log.Message($"[RimChat][NpcPushThrottle] {message}");
        }

        private bool TryDeliverFallbackMessage(NpcDialogueTriggerContext context)
        {
            if (context == null || !context.BypassRateLimit || string.IsNullOrWhiteSpace(context.Reason))
            {
                return false;
            }

            DeliverMessage(context, context.Reason.Trim());
            return true;
        }

        private float GetRegularTriggerChance(NpcPushFrequencyMode mode)
        {
            return mode switch
            {
                NpcPushFrequencyMode.High => 0.30f,
                NpcPushFrequencyMode.Medium => 0.20f,
                _ => 0.12f
            };
        }
    }
}

