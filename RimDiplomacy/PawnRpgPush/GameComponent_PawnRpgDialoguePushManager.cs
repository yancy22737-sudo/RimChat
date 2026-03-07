using System;
using System.Collections.Generic;
using System.Linq;
using RimDiplomacy.AI;
using RimDiplomacy.Config;
using RimDiplomacy.Core;
using RimDiplomacy.NpcDialogue;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimDiplomacy.PawnRpgPush
{
    /// <summary>
    /// Dependencies: AIChatServiceAsync, RimDiplomacy settings, Verse.GameComponent.
    /// Responsibility: Orchestrate PawnRPG proactive trigger intake, queueing, throttling, generation and delivery.
    /// </summary>
    public partial class GameComponent_PawnRpgDialoguePushManager : GameComponent
    {
        private sealed class PendingGenerationContext
        {
            public PawnRpgTriggerContext Context;
            public Pawn NpcPawn;
            public Pawn PlayerPawn;
            public List<ChatMessageData> Messages;
            public int Attempt;
        }

        private const int TickPerHour = 2500;
        private const int TickPerDay = 60000;
        private const int RegularEvaluationInterval = 6000;
        private const int QueueProcessInterval = 600;
        private const int IncomingDrainInterval = 120;
        private const int ThreatScanInterval = 600;
        private const int ClickWindowTicks = 360;
        private const int ClickBusyThreshold = 12;
        private const int CausalMinDelayTicks = 250;
        private const int CausalMaxDelayTicks = 1000;
        private const int NpcEvaluateCooldownTicks = 150000;
        private const int ColonyDeliveryCooldownTicks = 75000;
        private const int BlockedRetryTicks = 300;
        private const float LowMoodThreshold = 0.30f;
        private const int QuestDeadlineWindowTicks = TickPerDay;
        private const int QuestTriggerRepeatTicks = 15000;

        public static GameComponent_PawnRpgDialoguePushManager Instance;

        private List<PawnRpgNpcPushState> npcPushStates = new List<PawnRpgNpcPushState>();
        private List<PawnRpgThreatState> threatStates = new List<PawnRpgThreatState>();
        private List<QueuedPawnRpgTrigger> queuedTriggers = new List<QueuedPawnRpgTrigger>();

        private readonly Queue<PawnRpgTriggerContext> incomingTriggers = new Queue<PawnRpgTriggerContext>();
        private readonly Dictionary<string, PendingGenerationContext> pendingRequests = new Dictionary<string, PendingGenerationContext>();
        private readonly Queue<int> clickTicks = new Queue<int>();
        private readonly Dictionary<string, int> recentQuestTriggerTicks = new Dictionary<string, int>();
        private int lastColonyDeliveredTick = -ColonyDeliveryCooldownTicks;

        public GameComponent_PawnRpgDialoguePushManager(Game game) : base()
        {
            Instance = this;
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            Instance = this;
            ClearTransientState();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            Instance = this;
            ClearTransientState();
            CleanupInvalidState();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref npcPushStates, "pawnRpgNpcPushStates", LookMode.Deep);
            Scribe_Collections.Look(ref threatStates, "pawnRpgThreatStates", LookMode.Deep);
            Scribe_Collections.Look(ref queuedTriggers, "pawnRpgQueuedTriggers", LookMode.Deep);
            Scribe_Values.Look(ref lastColonyDeliveredTick, "pawnRpgLastColonyDeliveredTick", -ColonyDeliveryCooldownTicks);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                npcPushStates ??= new List<PawnRpgNpcPushState>();
                threatStates ??= new List<PawnRpgThreatState>();
                queuedTriggers ??= new List<QueuedPawnRpgTrigger>();
                CleanupInvalidState();
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

            if (!IsFeatureEnabled())
            {
                return;
            }

            if (currentTick % IncomingDrainInterval == 0)
            {
                DrainIncomingTriggers(currentTick);
            }

            if (currentTick % QueueProcessInterval == 0)
            {
                ProcessQueuedTriggers(currentTick);
            }

            if (currentTick % ThreatScanInterval == 0)
            {
                EvaluateThreatTriggers(currentTick);
            }

            if (currentTick % RegularEvaluationInterval == 0)
            {
                EvaluateRegularTriggers(currentTick);
            }
        }

        public void RegisterTradeCompletedTrigger(Faction faction, int soldCount, int boughtCount)
        {
            if (!IsValidTargetFaction(faction) || soldCount <= 0 && boughtCount <= 0)
            {
                return;
            }

            EnqueueIncoming(new PawnRpgTriggerContext
            {
                Faction = faction,
                TriggerType = NpcDialogueTriggerType.Causal,
                Category = NpcDialogueCategory.DiplomacyTask,
                SourceTag = "trade_completed",
                Reason = "trade_completed",
                Severity = 1,
                CreatedTick = Find.TickManager?.TicksGame ?? 0,
                Metadata = $"{soldCount}|{boughtCount}"
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
            EnqueueIncoming(new PawnRpgTriggerContext
            {
                Faction = faction,
                TriggerType = NpcDialogueTriggerType.Causal,
                Category = category,
                SourceTag = "goodwill_shift",
                Reason = reason ?? string.Empty,
                Severity = severity,
                CreatedTick = Find.TickManager?.TicksGame ?? 0,
                Metadata = goodwillDelta.ToString()
            });
        }

        public void RegisterThreatStateTrigger(Faction faction, bool hasHive, bool hasHostiles)
        {
            if (!IsValidTargetFaction(faction) || !hasHive && !hasHostiles)
            {
                return;
            }

            EnqueueIncoming(new PawnRpgTriggerContext
            {
                Faction = faction,
                TriggerType = NpcDialogueTriggerType.Causal,
                Category = NpcDialogueCategory.WarningThreat,
                SourceTag = hasHive ? "hive_nearby" : "hostiles_nearby",
                Reason = hasHive ? "hive_warning" : "hostile_warning",
                Severity = hasHive ? 3 : 2,
                CreatedTick = Find.TickManager?.TicksGame ?? 0
            });
        }

        public void RegisterPlayerLeftClick()
        {
            RimDiplomacySettings settings = RimDiplomacyMod.Instance?.InstanceSettings;
            if (settings?.EnableBusyByClickRate != true || Find.TickManager == null)
            {
                return;
            }

            clickTicks.Enqueue(Find.TickManager.TicksGame);
        }

        public bool DebugForcePawnRpgProactiveDialogue()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.TickManager == null)
            {
                return false;
            }

            if (!AI.AIChatServiceAsync.Instance.IsConfigured())
            {
                return false;
            }

            List<Faction> factions = GetActiveCandidateFactionsOnPlayerMaps();
            if (factions.Count == 0)
            {
                return false;
            }

            factions = factions.InRandomOrder().ToList();
            int now = Find.TickManager.TicksGame;
            foreach (Faction faction in factions)
            {
                if (!TryResolvePairForFaction(faction, now, true, true, true, out Pawn npcPawn, out Pawn playerPawn))
                {
                    continue;
                }

                var context = new PawnRpgTriggerContext
                {
                    Faction = faction,
                    TriggerType = NpcDialogueTriggerType.Causal,
                    Category = NpcDialogueCategory.Social,
                    SourceTag = "debug_force",
                    Reason = "manual_debug_trigger",
                    Severity = 1,
                    CreatedTick = now
                };
                StartGeneration(context, npcPawn, playerPawn);
                return true;
            }

            return false;
        }

        private void ClearTransientState()
        {
            incomingTriggers.Clear();
            pendingRequests.Clear();
            clickTicks.Clear();
            recentQuestTriggerTicks.Clear();
        }

        private void EnqueueIncoming(PawnRpgTriggerContext context)
        {
            if (context == null || !IsValidTargetFaction(context.Faction))
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
                PawnRpgTriggerContext context = incomingTriggers.Dequeue();
                HandleTriggerContext(context, currentTick);
            }
        }

        private void HandleTriggerContext(PawnRpgTriggerContext context, int currentTick)
        {
            if (context == null || !IsValidTargetFaction(context.Faction))
            {
                return;
            }

            int dueTick = currentTick;
            if (context.TriggerType == NpcDialogueTriggerType.Causal)
            {
                dueTick += Rand.RangeInclusive(CausalMinDelayTicks, CausalMaxDelayTicks);
            }

            dueTick = Math.Max(dueTick, GetNextAllowedTickForContext(context, currentTick));
            if (IsFactionPending(context.Faction) || IsPlayerBusy())
            {
                dueTick = Math.Max(dueTick, currentTick + BlockedRetryTicks);
            }

            if (dueTick <= currentTick && TryStartGenerationForContext(context, currentTick))
            {
                return;
            }

            QueueTrigger(context, Math.Max(dueTick, currentTick + BlockedRetryTicks), currentTick);
        }

        private void ProcessQueuedTriggers(int currentTick)
        {
            CleanupExpiredQueue(currentTick);

            List<QueuedPawnRpgTrigger> dueItems = queuedTriggers
                .Where(q => q != null && q.dueTick <= currentTick)
                .OrderBy(q => q.dueTick)
                .ToList();

            int processed = 0;
            foreach (QueuedPawnRpgTrigger item in dueItems)
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

                PawnRpgTriggerContext context = item.ToContext();
                if (IsFactionPending(context.Faction) || IsPlayerBusy())
                {
                    item.dueTick = currentTick + BlockedRetryTicks;
                    continue;
                }

                int nextAllowed = GetNextAllowedTickForContext(context, currentTick);
                if (nextAllowed > currentTick)
                {
                    item.dueTick = nextAllowed;
                    continue;
                }

                if (!TryStartGenerationForContext(context, currentTick))
                {
                    item.dueTick = currentTick + BlockedRetryTicks;
                    continue;
                }

                queuedTriggers.Remove(item);
                processed++;
            }
        }

        private void EvaluateRegularTriggers(int currentTick)
        {
            CleanupQuestTriggerCache(currentTick);
            float chance = GetRegularTriggerChance(RimDiplomacyMod.Instance?.InstanceSettings?.NpcPushFrequencyMode ?? NpcPushFrequencyMode.Low);
            foreach (Faction faction in GetActiveCandidateFactionsOnPlayerMaps())
            {
                if (IsFactionPending(faction))
                {
                    continue;
                }

                if (TryCreateQuestDeadlineContext(faction, currentTick, out PawnRpgTriggerContext questContext))
                {
                    HandleTriggerContext(questContext, currentTick);
                    continue;
                }

                if (TryCreateLowMoodContext(faction, currentTick, out PawnRpgTriggerContext moodContext))
                {
                    HandleTriggerContext(moodContext, currentTick);
                    continue;
                }

                if (Rand.Value > chance)
                {
                    continue;
                }

                var ambientContext = new PawnRpgTriggerContext
                {
                    Faction = faction,
                    TriggerType = NpcDialogueTriggerType.Ambient,
                    Category = NpcDialogueCategory.Social,
                    SourceTag = "ambient",
                    Reason = "ambient_social",
                    Severity = 1,
                    CreatedTick = currentTick
                };
                HandleTriggerContext(ambientContext, currentTick);
            }
        }

        private void EvaluateThreatTriggers(int currentTick)
        {
            bool hasHostiles = IsBusyByHostiles();
            bool hasHive = HasNearbyHiveThreat();
            bool hasThreat = hasHostiles || hasHive;
            foreach (Faction faction in GetActiveCandidateFactionsOnPlayerMaps())
            {
                PawnRpgThreatState state = GetOrCreateThreatState(faction);
                if (!hasThreat)
                {
                    state.hadThreat = false;
                    continue;
                }

                if (state.hadThreat)
                {
                    continue;
                }

                RegisterThreatStateTrigger(faction, hasHive, hasHostiles);
                state.hadThreat = true;
            }
        }

        private bool TryStartGenerationForContext(PawnRpgTriggerContext context, int currentTick)
        {
            if (!TryResolvePairForFaction(context.Faction, currentTick, false, false, false, out Pawn npcPawn, out Pawn playerPawn))
            {
                return false;
            }

            StartGeneration(context, npcPawn, playerPawn);
            return true;
        }

        private bool TryCreateLowMoodContext(Faction faction, int currentTick, out PawnRpgTriggerContext context)
        {
            context = null;
            Pawn worstMoodNpc = null;
            float worstMood = 1f;
            foreach (Pawn npc in GetFactionNpcCandidates(faction))
            {
                if (!TryGetMoodPercent(npc, out float mood) || mood > LowMoodThreshold)
                {
                    continue;
                }

                if (!HasQualifiedPlayerRelation(npc))
                {
                    continue;
                }

                if (mood < worstMood)
                {
                    worstMood = mood;
                    worstMoodNpc = npc;
                }
            }

            if (worstMoodNpc == null)
            {
                return false;
            }

            context = new PawnRpgTriggerContext
            {
                Faction = faction,
                TriggerType = NpcDialogueTriggerType.Conditional,
                Category = NpcDialogueCategory.Social,
                SourceTag = "low_mood",
                Reason = "low_mood",
                Severity = 1,
                CreatedTick = currentTick,
                Metadata = worstMood.ToString("F3")
            };
            return true;
        }

        private bool TryCreateQuestDeadlineContext(Faction faction, int currentTick, out PawnRpgTriggerContext context)
        {
            context = null;
            if (Find.QuestManager?.QuestsListForReading == null)
            {
                return false;
            }

            Quest quest = Find.QuestManager.QuestsListForReading
                .Where(q => q != null && q.State == QuestState.Ongoing && q.EverAccepted && q.TicksUntilExpiry > 0)
                .Where(q => q.TicksUntilExpiry <= QuestDeadlineWindowTicks && q.InvolvedFactions != null && q.InvolvedFactions.Contains(faction))
                .OrderBy(q => q.TicksUntilExpiry)
                .FirstOrDefault();
            if (quest == null)
            {
                return false;
            }

            string key = $"{quest.id}:{faction.loadID}";
            if (recentQuestTriggerTicks.TryGetValue(key, out int lastTick) && currentTick - lastTick < QuestTriggerRepeatTicks)
            {
                return false;
            }

            recentQuestTriggerTicks[key] = currentTick;
            context = new PawnRpgTriggerContext
            {
                Faction = faction,
                TriggerType = NpcDialogueTriggerType.Conditional,
                Category = NpcDialogueCategory.DiplomacyTask,
                SourceTag = "quest_deadline",
                Reason = "quest_deadline",
                Severity = quest.TicksUntilExpiry <= TickPerDay / 2 ? 2 : 1,
                CreatedTick = currentTick,
                Metadata = $"{quest.id}|{quest.name}|{quest.TicksUntilExpiry}"
            };
            return true;
        }

        private void CleanupQuestTriggerCache(int currentTick)
        {
            List<string> staleKeys = recentQuestTriggerTicks
                .Where(pair => currentTick - pair.Value > QuestDeadlineWindowTicks)
                .Select(pair => pair.Key)
                .ToList();
            foreach (string key in staleKeys)
            {
                recentQuestTriggerTicks.Remove(key);
            }
        }

        private int GetNextAllowedTickForContext(PawnRpgTriggerContext context, int currentTick)
        {
            int nextTick = GetFactionNpcReadyTick(context?.Faction, currentTick);
            if (!CanBypassGlobalCooldown(context) && lastColonyDeliveredTick > 0)
            {
                nextTick = Math.Max(nextTick, lastColonyDeliveredTick + ColonyDeliveryCooldownTicks);
            }

            return nextTick;
        }

        private bool CanBypassGlobalCooldown(PawnRpgTriggerContext context)
        {
            return context != null && context.Category == NpcDialogueCategory.WarningThreat;
        }

        private void QueueTrigger(PawnRpgTriggerContext context, int dueTick, int nowTick)
        {
            RimDiplomacySettings settings = RimDiplomacyMod.Instance?.InstanceSettings;
            int maxPerFaction = Mathf.Clamp(settings?.NpcQueueMaxPerFaction ?? 3, 1, 10);
            int expireTicks = Mathf.RoundToInt((settings?.NpcQueueExpireHours ?? 12f) * TickPerHour);
            expireTicks = Mathf.Max(expireTicks, TickPerHour);

            List<QueuedPawnRpgTrigger> sameFaction = queuedTriggers
                .Where(q => q?.faction == context.Faction)
                .OrderBy(q => q.enqueuedTick)
                .ToList();
            if (sameFaction.Count >= maxPerFaction)
            {
                queuedTriggers.Remove(sameFaction[0]);
            }

            queuedTriggers.Add(QueuedPawnRpgTrigger.FromContext(context, nowTick, dueTick, nowTick + expireTicks));
        }

        private void CleanupExpiredQueue(int currentTick)
        {
            queuedTriggers.RemoveAll(q =>
                q == null ||
                q.faction == null ||
                q.faction.defeated ||
                q.expireTick <= currentTick);
        }

        private bool IsFeatureEnabled()
        {
            RimDiplomacySettings settings = RimDiplomacyMod.Instance?.InstanceSettings;
            return settings != null && settings.EnableNpcInitiatedDialogue && settings.EnableRPGDialogue;
        }

        private bool IsValidTargetFaction(Faction faction)
        {
            if (faction == null || faction.defeated)
            {
                return false;
            }

            if (faction.IsPlayer || faction == Faction.OfPlayer)
            {
                return true;
            }

            return !(faction.def?.hidden ?? true);
        }

        private bool IsFactionPending(Faction faction)
        {
            if (faction == null)
            {
                return false;
            }

            foreach (KeyValuePair<string, PendingGenerationContext> pair in pendingRequests)
            {
                if (pair.Value?.Context?.Faction == faction)
                {
                    return true;
                }
            }

            return false;
        }

        private void CleanupInvalidState()
        {
            npcPushStates.RemoveAll(s => s == null || s.pawn == null || s.pawn.Destroyed || s.pawn.Dead);
            threatStates.RemoveAll(s => s == null || s.faction == null || s.faction.defeated);
            queuedTriggers.RemoveAll(q => q == null || q.faction == null || q.faction.defeated);
        }

        private PawnRpgThreatState GetOrCreateThreatState(Faction faction)
        {
            PawnRpgThreatState state = threatStates.FirstOrDefault(s => s?.faction == faction);
            if (state != null)
            {
                return state;
            }

            state = new PawnRpgThreatState { faction = faction };
            threatStates.Add(state);
            return state;
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

