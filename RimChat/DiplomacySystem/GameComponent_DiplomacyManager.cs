using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using RimChat.Memory;
using RimChat.Util;
using RimChat.Core;
using RimChat.Config;
using RimChat.AI;

namespace RimChat.DiplomacySystem
{
    public partial class GameComponent_DiplomacyManager : GameComponent
    {
        private HashSet<Faction> aiControlledFactions = new HashSet<Faction>();
        private List<FactionDialogueSession> dialogueSessions = new List<FactionDialogueSession>();
        private List<FactionPresenceState> presenceStates = new List<FactionPresenceState>();
        private List<DelayedDiplomacyEvent> delayedEvents = new List<DelayedDiplomacyEvent>();

        public static GameComponent_DiplomacyManager Instance = null;

        public GameComponent_DiplomacyManager(Game game)
        {
            Instance = this;
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            AIChatServiceAsync.NotifyGameContextChanged("Started new game");
            GameAIInterface.Instance?.ResetPrisonerRansomRuntimeState();
            InitializeAIControlledFactions();
            InitializeDialogueSessions();
            InitializePresenceStates();
            InitializeSocialCircleOnNewGame();
            // Initializeleadermemorysystem
            LeaderMemoryManager.Instance.OnNewGame();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            AIChatServiceAsync.NotifyGameContextChanged("Loaded game");
            GameAIInterface.Instance?.ResetPrisonerRansomRuntimeState();
            if (aiControlledFactions == null)
            {
                aiControlledFactions = new HashSet<Faction>();
                InitializeAIControlledFactions();
            }
            if (dialogueSessions == null)
            {
                dialogueSessions = new List<FactionDialogueSession>();
                InitializeDialogueSessions();
            }
            if (presenceStates == null)
            {
                presenceStates = new List<FactionPresenceState>();
            }
            InitializePresenceStates();
            InitializeSocialCircleOnLoadedGame();
            // 清理无效的session
            CleanupInvalidSessions();
            CleanupInvalidPresenceStates();
            // Loadleadermemorysystem
            LeaderMemoryManager.Instance.OnLoadedGame();
        }

        private void InitializeAIControlledFactions()
        {
            var allFactions = Find.FactionManager.AllFactions
                .Where(f => !f.IsPlayer && !f.defeated && !f.def.hidden)
                .ToList();

            foreach (var faction in allFactions)
            {
                aiControlledFactions.Add(faction);
            }
        }

        private void InitializeDialogueSessions()
        {
            var allFactions = Find.FactionManager.AllFactions
                .Where(f => !f.IsPlayer && !f.defeated && !f.def.hidden)
                .ToList();

            foreach (var faction in allFactions)
            {
                GetOrCreateSession(faction);
            }
        }

        private void InitializePresenceStates()
        {
            var allFactions = Find.FactionManager.AllFactions
                .Where(f => !f.IsPlayer && !f.defeated && !f.def.hidden)
                .ToList();

            foreach (var faction in allFactions)
            {
                GetOrCreatePresenceState(faction);
            }
        }

        private void CleanupInvalidSessions()
        {
            dialogueSessions.RemoveAll(s => s.faction == null || s.faction.defeated);
        }

        private void CleanupInvalidPresenceStates()
        {
            presenceStates.RemoveAll(s => s.faction == null || s.faction.defeated);
        }

        /// <summary>/// get或创建指定faction的dialoguesession
 ///</summary>
        public FactionDialogueSession GetOrCreateSession(Faction faction)
        {
            if (faction == null) return null;

            var session = dialogueSessions.FirstOrDefault(s => s.faction == faction);
            if (session == null)
            {
                session = new FactionDialogueSession(faction);
                dialogueSessions.Add(session);
                Log.Message($"[RimChat] Created dialogue session for {faction.Name}");
            }
            return session;
        }

        /// <summary>/// get指定faction的dialoguesession (如果不presence则返回null)
 ///</summary>
        public FactionDialogueSession GetSession(Faction faction)
        {
            if (faction == null) return null;
            return dialogueSessions.FirstOrDefault(s => s.faction == faction);
        }

        public FactionPresenceState GetOrCreatePresenceState(Faction faction)
        {
            if (faction == null) return null;

            var state = presenceStates.FirstOrDefault(s => s.faction == faction);
            if (state == null)
            {
                state = new FactionPresenceState(faction);
                presenceStates.Add(state);
            }
            return state;
        }

        public FactionPresenceState GetPresenceState(Faction faction)
        {
            if (faction == null) return null;
            return presenceStates.FirstOrDefault(s => s.faction == faction);
        }

        public FactionPresenceStatus GetPresenceStatus(Faction faction)
        {
            var state = GetOrCreatePresenceState(faction);
            return state?.status ?? FactionPresenceStatus.Online;
        }

        public bool CanSendMessage(Faction faction)
        {
            return GetPresenceStatus(faction) == FactionPresenceStatus.Online;
        }

        public void ForcePresenceOnlineForNpcInitiated(Faction faction)
        {
            if (faction == null)
            {
                return;
            }

            FactionPresenceState state = GetOrCreatePresenceState(faction);
            if (state == null)
            {
                return;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            state.status = FactionPresenceStatus.Online;
            state.lastReason = string.Empty;
            state.forcedOfflineUntilTick = 0;
            state.doNotDisturbUntilTick = 0;
            int cacheTicks = GetPresenceCacheTicks();
            state.cacheUntilTick = currentTick + cacheTicks;
            state.lastResolvedTick = currentTick;
        }

        public void RefreshPresenceOnDialogueOpen(Faction faction)
        {
            var state = GetOrCreatePresenceState(faction);
            if (state == null) return;

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (!IsPresenceEnabled())
            {
                state.status = FactionPresenceStatus.Online;
                state.lastReason = string.Empty;
                state.lastResolvedTick = currentTick;
                return;
            }

            if (state.IsForcedOffline(currentTick))
            {
                state.status = FactionPresenceStatus.Offline;
                state.lastResolvedTick = currentTick;
                return;
            }

            if (state.IsDoNotDisturb(currentTick))
            {
                state.status = FactionPresenceStatus.DoNotDisturb;
                state.lastResolvedTick = currentTick;
                return;
            }

            if (state.forcedOfflineUntilTick > 0 && state.forcedOfflineUntilTick <= currentTick)
            {
                state.forcedOfflineUntilTick = 0;
            }

            if (state.doNotDisturbUntilTick > 0 && state.doNotDisturbUntilTick <= currentTick)
            {
                state.doNotDisturbUntilTick = 0;
            }

            if (state.IsCacheValid(currentTick))
            {
                return;
            }

            state.status = EvaluateScheduledPresence(faction, currentTick, out string reason);
            state.lastReason = reason ?? string.Empty;
            state.lastResolvedTick = currentTick;
        }

        public void RefreshPresenceForFactions(IEnumerable<Faction> factions)
        {
            if (factions == null) return;
            foreach (var faction in factions)
            {
                RefreshPresenceOnDialogueOpen(faction);
            }
        }

        public void LockPresenceCacheOnDialogueClose(Faction faction)
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            int cacheTicks = GetPresenceCacheTicks();
            if (currentTick <= 0 || cacheTicks <= 0) return;

            var state = GetOrCreatePresenceState(faction);
            if (state == null) return;
            if (state.cacheUntilTick > currentTick)
            {
                return;
            }
            if (state.forcedOfflineUntilTick > currentTick)
            {
                state.cacheUntilTick = Math.Max(state.cacheUntilTick, state.forcedOfflineUntilTick);
                return;
            }

            if (state.doNotDisturbUntilTick > currentTick)
            {
                state.cacheUntilTick = Math.Max(state.cacheUntilTick, state.doNotDisturbUntilTick);
                return;
            }
            state.cacheUntilTick = Math.Max(state.cacheUntilTick, currentTick + cacheTicks);
        }

        public void LockPresenceCacheOnDialogueClose(IEnumerable<Faction> factions)
        {
            if (factions == null) return;
            foreach (var faction in factions)
            {
                LockPresenceCacheOnDialogueClose(faction);
            }
        }

        public void ApplyPresenceAction(Faction faction, string actionType, string reason, FactionDialogueSession session)
        {
            if (faction == null || string.IsNullOrEmpty(actionType)) return;

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            var state = GetOrCreatePresenceState(faction);
            if (state == null) return;

            string normalizedReason = reason ?? string.Empty;
            switch (actionType)
            {
                case "exit_dialogue":
                    session?.MarkConversationEnded(normalizedReason, true, GenDate.TicksPerHour);
                    break;
                case "go_offline":
                    state.status = FactionPresenceStatus.Offline;
                    state.lastReason = normalizedReason;
                    state.lastResolvedTick = currentTick;
                    state.forcedOfflineUntilTick = currentTick + GetPresenceForcedOfflineTicks();
                    state.doNotDisturbUntilTick = 0;
                    state.cacheUntilTick = Math.Max(state.cacheUntilTick, state.forcedOfflineUntilTick);
                    session?.MarkConversationEnded(normalizedReason, false);
                    NpcDialogue.GameComponent_NpcDialoguePushManager.Instance?.CancelQueuedTriggersForFaction(faction);
                    break;
                case "set_dnd":
                    state.status = FactionPresenceStatus.DoNotDisturb;
                    state.lastReason = normalizedReason;
                    state.lastResolvedTick = currentTick;
                    state.forcedOfflineUntilTick = 0;
                    state.doNotDisturbUntilTick = currentTick + GetPresenceDoNotDisturbTicks();
                    state.cacheUntilTick = Math.Max(state.cacheUntilTick, state.doNotDisturbUntilTick);
                    session?.MarkConversationEnded(normalizedReason, false);
                    NpcDialogue.GameComponent_NpcDialoguePushManager.Instance?.CancelQueuedTriggersForFaction(faction);
                    break;
            }
        }

        /// <summary>/// 检查factionwhether有未读message
 ///</summary>
        public bool HasUnreadMessages(Faction faction)
        {
            var session = GetSession(faction);
            return session?.hasUnreadMessages ?? false;
        }

        /// <summary>/// get所有有dialoguerecord的faction
 ///</summary>
        public List<Faction> GetFactionsWithDialogue()
        {
            return dialogueSessions
                .Where(s => s.faction != null && !s.faction.defeated && s.messages.Count > 0)
                .Select(s => s.faction)
                .ToList();
        }

        private int lastDailyResetTick = 0;
        private int lastPeriodicSnapshotTick = 0;
        private const int PeriodicSnapshotIntervalTicks = 1500; // ~30 seconds

        public override void GameComponentTick()
        {
            int currentTick = Find.TickManager.TicksGame;

            // 每 60 ticks (约 1 秒) 检查一次
            if (currentTick % 60 == 0)
            {
                ProcessDelayedEvents();
            }

            // 每 2000 ticks (约 33 秒) 检查一次 AI 决策
            if (currentTick % 2000 == 0)
            {
                ProcessAIDecisions();
                ProcessSocialCircleTick();
            }

            // 每 ~30 秒对所有有活动的外交 session 做增量快照，防止切换派系时丢失对话记忆
            if (currentTick - lastPeriodicSnapshotTick >= PeriodicSnapshotIntervalTicks)
            {
                ProcessPeriodicDiplomacySnapshots();
                lastPeriodicSnapshotTick = currentTick;
            }

            // 每日重置 (60000 ticks = 1 天)
            if (currentTick - lastDailyResetTick >= 60000)
            {
                DailyReset();
                lastDailyResetTick = currentTick;
            }
        }

        private void ProcessPeriodicDiplomacySnapshots()
        {
            if (dialogueSessions == null) return;

            foreach (var session in dialogueSessions)
            {
                if (session == null || session.faction == null || session.faction.defeated) continue;
                if (session.messages == null || session.messages.Count <= session.lastSummarizedMessageIndex) continue;

                Pawn negotiator = GetLastNegotiatorForSession(session);
                RpgNpcDialogueArchiveManager.Instance.RecordDiplomacySummary(
                    negotiator,
                    session.faction,
                    session.messages,
                    session.lastSummarizedMessageIndex);

                session.lastSummarizedMessageIndex = session.messages.Count;
            }
        }

        private Pawn GetLastNegotiatorForSession(FactionDialogueSession session)
        {
            if (session?.messages == null) return null;
            foreach (var msg in session.messages.AsEnumerable().Reverse())
            {
                if (msg == null) continue;
                Pawn speaker = msg.ResolveSpeakerPawn();
                if (speaker != null && !speaker.Destroyed && !speaker.Dead)
                {
                    return speaker;
                }
            }
            return null;
        }

        private void ProcessDelayedEvents()
        {
            if (delayedEvents == null) return;

            List<DelayedDiplomacyEvent> eventsToRemove = new List<DelayedDiplomacyEvent>();

            foreach (var evt in delayedEvents)
            {
                if (evt == null)
                {
                    eventsToRemove.Add(evt);
                    continue;
                }

                if (evt.Faction == null || evt.Faction.defeated)
                {
                    eventsToRemove.Add(evt);
                    continue;
                }

                if (!evt.ShouldExecute())
                {
                    continue;
                }

                bool success = evt.Execute();
                if (success)
                {
                    eventsToRemove.Add(evt);
                    continue;
                }

                if (evt.CanRetry())
                {
                    int retryDelay = Rand.Range(1500, 3000);
                    evt.ScheduleRetry(retryDelay);
                    Log.Warning($"[RimChat] Delayed {evt.EventType} from {evt.Faction?.Name} failed; retry {evt.RetryCount}/{evt.MaxRetryCount} at tick {evt.NextRetryTick}.");
                }
                else
                {
                    Log.Error($"[RimChat] Delayed {evt.EventType} from {evt.Faction?.Name} failed after {evt.RetryCount} retries and was discarded.");
                    eventsToRemove.Add(evt);
                }
            }

            foreach (var evt in eventsToRemove)
            {
                delayedEvents.Remove(evt);
            }
        }

        public void AddDelayedEvent(DelayedDiplomacyEvent evt)
        {
            if (delayedEvents == null)
                delayedEvents = new List<DelayedDiplomacyEvent>();

            delayedEvents.Add(evt);
            Log.Message($"[RimChat] Scheduled delayed {evt.EventType} from {evt.Faction?.Name} at tick {evt.ExecuteTick}");
        }

        private void DailyReset()
        {
            // 重置 GameAIInterface 的每日限制
            GameAIInterface.Instance?.DailyReset();
            OnSocialCircleDailyReset();

            Log.Message("[RimChat] Daily reset completed.");
        }

        private void ProcessAIDecisions()
        {
            // 这里将调用 AI API 进行决策
            // 暂时留空, pending AI client实现
        }

        public bool IsAIControlled(Faction faction)
        {
            return aiControlledFactions.Contains(faction);
        }

        public override void ExposeData()
        {
            base.ExposeData();

            // 游戏save时save所有leadermemory到file
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                LeaderMemoryManager.Instance.OnBeforeGameSave();
            }

            Scribe_Collections.Look(ref aiControlledFactions, "aiControlledFactions", LookMode.Reference);
            
            Scribe_Collections.Look(ref dialogueSessions, "dialogueSessions", LookMode.Deep);
            Scribe_Collections.Look(ref presenceStates, "presenceStates", LookMode.Deep);
            Scribe_Collections.Look(ref delayedEvents, "delayedEvents", LookMode.Deep);
            Scribe_Collections.Look(ref manuallyVisibleHiddenFactions, "manuallyVisibleHiddenFactions", LookMode.Reference);
            Scribe_Collections.Look(ref albumEntries, "albumEntries", LookMode.Deep);
            Scribe_Deep.Look(ref socialCircleState, "socialCircleState");
            Scribe_Values.Look(ref lastDailyResetTick, "lastDailyResetTick", 0);

            // Save/load GameAIInterface 数据
            GameAIInterface.Instance?.ExposeData();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (aiControlledFactions == null)
                    aiControlledFactions = new HashSet<Faction>();
                if (dialogueSessions == null)
                    dialogueSessions = new List<FactionDialogueSession>();
                if (presenceStates == null)
                    presenceStates = new List<FactionPresenceState>();
                if (delayedEvents == null)
                    delayedEvents = new List<DelayedDiplomacyEvent>();
                if (manuallyVisibleHiddenFactions == null)
                    manuallyVisibleHiddenFactions = new HashSet<Faction>();
                if (albumEntries == null)
                    albumEntries = new List<AlbumImageEntry>();
                if (socialCircleState == null)
                    socialCircleState = new SocialCircleState();
                EnsureHiddenFactionVisibilityState();

                // 修复延迟event的 ExecuteTick: 防止存档load后出现不合理的长延迟
                if (delayedEvents != null && Find.TickManager != null)
                {
                    int currentTick = Find.TickManager.TicksGame;
                    int baseAidDelay = RimChatMod.Instance?.InstanceSettings?.AidDelayBaseTicks ?? 90000;
                    int baseCaravanDelay = RimChatMod.Instance?.InstanceSettings?.CaravanDelayBaseTicks ?? 135000;
                    
                    foreach (var evt in delayedEvents)
                    {
                        int baseDelay = evt.EventType == DelayedEventType.Aid ? baseAidDelay : baseCaravanDelay;
                        
                        if (evt.ExecuteTick <= currentTick)
                        {
                            int minDelay = (int)(baseDelay * 0.2f);
                            int maxDelay = baseDelay;
                            evt.ExecuteTick = currentTick + Rand.Range(minDelay, maxDelay);
                            Log.Message($"[RimChat] Adjusted delayed {evt.EventType} from {evt.Faction?.Name}: tick was in past, new tick={evt.ExecuteTick}");
                        }
                        else if (evt.ExecuteTick - currentTick > baseDelay * 2)
                        {
                            evt.ExecuteTick = currentTick + Rand.Range(baseDelay, baseDelay * 2);
                            Log.Message($"[RimChat] Adjusted delayed {evt.EventType} from {evt.Faction?.Name}: delay was too long, new tick={evt.ExecuteTick}");
                        }
                    }
                }
            }

            // 游戏loadcompleted后, 从fileloadmemory数据
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                LeaderMemoryManager.Instance.OnAfterGameLoad(dialogueSessions);
            }
        }

        private bool IsPresenceEnabled()
        {
            return RimChatMod.Instance?.InstanceSettings?.EnableFactionPresenceStatus ?? true;
        }

        private int GetPresenceCacheTicks()
        {
            float cacheHours = RimChatMod.Instance?.InstanceSettings?.PresenceCacheHours ?? 8f;
            return Math.Max(0, Mathf.RoundToInt(cacheHours * 2500f));
        }

        private int GetPresenceForcedOfflineTicks()
        {
            float offlineHours = RimChatMod.Instance?.InstanceSettings?.PresenceForcedOfflineHours ?? 24f;
            return Math.Max(0, Mathf.RoundToInt(offlineHours * 2500f));
        }

        private int GetPresenceDoNotDisturbTicks()
        {
            return GenDate.TicksPerDay * 2; // 2天冷却 (从3天减少)
        }

        private FactionPresenceStatus EvaluateScheduledPresence(Faction faction, int currentTick, out string reason)
        {
            reason = "schedule";
            int currentHour = GetCurrentHourOfDay();
            int dayIndex = currentTick / 60000;
            TechLevel techLevel = faction?.def?.techLevel ?? TechLevel.Undefined;
            GetPresenceScheduleForTechLevel(techLevel, out int startHour, out int durationHours);
            int scheduleOffset = GetScheduleOffsetHours(faction, dayIndex);
            startHour = ModHour(startHour + scheduleOffset);
            bool isOnline = IsHourWithinWindow(currentHour, startHour, durationHours);

            if (!isOnline)
            {
                float offWindowChance = GetOffWindowOnlineChance(techLevel);
                if (offWindowChance > 0f &&
                    GetDeterministicRoll(faction, dayIndex, currentHour + 97) < offWindowChance)
                {
                    isOnline = true;
                    reason = "off_window_chance";
                }
            }

            if (isOnline && IsNightBiasEnabled() && IsInNightWindow(currentHour))
            {
                float offlineBias = Mathf.Clamp01(RimChatMod.Instance?.InstanceSettings?.PresenceNightOfflineBias ?? 0.65f);
                if (GetDeterministicRoll(faction, dayIndex, currentHour) < offlineBias)
                {
                    isOnline = false;
                    reason = "night_bias";
                }
            }

            return isOnline ? FactionPresenceStatus.Online : FactionPresenceStatus.Offline;
        }

        private bool IsNightBiasEnabled()
        {
            return RimChatMod.Instance?.InstanceSettings?.PresenceNightBiasEnabled ?? true;
        }

        private int GetCurrentHourOfDay()
        {
            var map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            if (map != null)
            {
                return GenLocalDate.HourOfDay(map);
            }

            int ticksAbs = Find.TickManager?.TicksAbs ?? 0;
            return Mathf.FloorToInt((ticksAbs / 2500f) % 24f);
        }

        private void GetPresenceScheduleForTechLevel(TechLevel techLevel, out int startHour, out int durationHours)
        {
            var settings = RimChatMod.Instance?.InstanceSettings;
            bool useAdvanced = settings?.PresenceUseAdvancedProfiles ?? false;
            if (!useAdvanced)
            {
                switch (techLevel)
                {
                    case TechLevel.Neolithic:
                        startHour = 8;
                        durationHours = 8;
                        return;
                    case TechLevel.Medieval:
                        startHour = 8;
                        durationHours = 10;
                        return;
                    case TechLevel.Industrial:
                        startHour = 7;
                        durationHours = 14;
                        return;
                    case TechLevel.Spacer:
                        startHour = 6;
                        durationHours = 18;
                        return;
                    case TechLevel.Ultra:
                    case TechLevel.Archotech:
                        startHour = 4;
                        durationHours = 20;
                        return;
                    default:
                        startHour = 7;
                        durationHours = 12;
                        return;
                }
            }

            switch (techLevel)
            {
                case TechLevel.Neolithic:
                    startHour = settings?.PresenceOnlineStart_Neolithic ?? 10;
                    durationHours = settings?.PresenceOnlineDuration_Neolithic ?? 6;
                    break;
                case TechLevel.Medieval:
                    startHour = settings?.PresenceOnlineStart_Medieval ?? 9;
                    durationHours = settings?.PresenceOnlineDuration_Medieval ?? 8;
                    break;
                case TechLevel.Industrial:
                    startHour = settings?.PresenceOnlineStart_Industrial ?? 8;
                    durationHours = settings?.PresenceOnlineDuration_Industrial ?? 12;
                    break;
                case TechLevel.Spacer:
                    startHour = settings?.PresenceOnlineStart_Spacer ?? 7;
                    durationHours = settings?.PresenceOnlineDuration_Spacer ?? 16;
                    break;
                case TechLevel.Ultra:
                    startHour = settings?.PresenceOnlineStart_Ultra ?? 6;
                    durationHours = settings?.PresenceOnlineDuration_Ultra ?? 18;
                    break;
                case TechLevel.Archotech:
                    startHour = settings?.PresenceOnlineStart_Archotech ?? 6;
                    durationHours = settings?.PresenceOnlineDuration_Archotech ?? 18;
                    break;
                default:
                    startHour = settings?.PresenceOnlineStart_Default ?? 8;
                    durationHours = settings?.PresenceOnlineDuration_Default ?? 10;
                    break;
            }

            startHour = Mathf.Clamp(startHour, 0, 23);
            durationHours = Mathf.Clamp(durationHours, 1, 24);
        }

        private bool IsHourWithinWindow(int hour, int startHour, int durationHours)
        {
            hour = Mathf.Clamp(hour, 0, 23);
            startHour = Mathf.Clamp(startHour, 0, 23);
            durationHours = Mathf.Clamp(durationHours, 1, 24);
            if (durationHours >= 24) return true;

            int endHour = (startHour + durationHours) % 24;
            if (startHour < endHour)
            {
                return hour >= startHour && hour < endHour;
            }

            return hour >= startHour || hour < endHour;
        }

        private bool IsInNightWindow(int hour)
        {
            var settings = RimChatMod.Instance?.InstanceSettings;
            int nightStart = Mathf.Clamp(settings?.PresenceNightStartHour ?? 22, 0, 23);
            int nightEnd = Mathf.Clamp(settings?.PresenceNightEndHour ?? 6, 0, 23);

            if (nightStart == nightEnd)
            {
                return true;
            }

            if (nightStart < nightEnd)
            {
                return hour >= nightStart && hour < nightEnd;
            }

            return hour >= nightStart || hour < nightEnd;
        }

        private float GetDeterministicRoll(Faction faction, int dayIndex, int hour)
        {
            int seed = Gen.HashCombineInt(faction?.loadID ?? 0, dayIndex);
            seed = Gen.HashCombineInt(seed, hour);
            Rand.PushState(seed);
            float value = Rand.Value;
            Rand.PopState();
            return value;
        }

        private int GetScheduleOffsetHours(Faction faction, int dayIndex)
        {
            int seed = Gen.HashCombineInt(faction?.loadID ?? 0, dayIndex);
            Rand.PushState(seed);
            int offset = Rand.RangeInclusive(-2, 2);
            Rand.PopState();
            return offset;
        }

        private int ModHour(int hour)
        {
            hour %= 24;
            if (hour < 0)
            {
                hour += 24;
            }
            return hour;
        }

        private float GetOffWindowOnlineChance(TechLevel techLevel)
        {
            switch (techLevel)
            {
                case TechLevel.Neolithic:
                    return 0.05f;
                case TechLevel.Medieval:
                    return 0.08f;
                case TechLevel.Industrial:
                    return 0.12f;
                case TechLevel.Spacer:
                    return 0.18f;
                case TechLevel.Ultra:
                case TechLevel.Archotech:
                    return 0.25f;
                default:
                    return 0.10f;
            }
        }

    }
}


