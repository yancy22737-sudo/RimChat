using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    public enum DelayedEventType
    {
        Caravan,
        Aid,
        Raid,
        RaidCallEveryone,
        RaidWave,
        RaidCallEveryoneAnnounce,    // 宣布即将来袭的主动消息
        RaidArrivalMessage,          // 袭击到达时的主动消息
        RaidDepartureMessage,        // 袭击离开后的主动消息
        RaidWaveEndMessage           // 最终波次结束的主动消息
    }

    public class DelayedDiplomacyEvent : IExposable
    {
        public DelayedEventType EventType;
        public Faction Faction;
        public int ExecuteTick;
        public int CaravanTypeInt;
        public int AidTypeInt;

        // Raid parameters
        public float RaidPoints;
        public RaidStrategyDef RaidStrategy;
        public PawnsArrivalModeDef ArrivalMode;
        public string RaidStrategyDefName;
        public string ArrivalModeDefName;

        // RaidWave parameters
        public int WaveIndex;
        public int TotalWaves;

        // RaidCallEveryone parameters
        public List<string> TargetFactionDefNames;
        public int CurrentTargetIndex;

        // Retry parameters
        public int RetryCount;
        public int MaxRetryCount = 3;
        public int NextRetryTick;

        public CaravanType CaravanType
        {
            get => (CaravanType)CaravanTypeInt;
            set => CaravanTypeInt = (int)value;
        }

        public AidType AidType
        {
            get => (AidType)AidTypeInt;
            set => AidTypeInt = (int)value;
        }

        public DelayedDiplomacyEvent()
        {
        }

        public DelayedDiplomacyEvent(DelayedEventType type, Faction faction, int executeTick)
        {
            EventType = type;
            Faction = faction;
            ExecuteTick = executeTick;
        }

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                CacheRaidDefNames();
            }

            Scribe_Values.Look(ref EventType, "eventType");
            Scribe_References.Look(ref Faction, "faction");
            Scribe_Values.Look(ref ExecuteTick, "executeTick");
            Scribe_Values.Look(ref CaravanTypeInt, "caravanTypeInt");
            Scribe_Values.Look(ref AidTypeInt, "aidTypeInt");
            
            // Raid data
            Scribe_Values.Look(ref RaidPoints, "raidPoints");
            Scribe_Defs.Look(ref RaidStrategy, "raidStrategy");
            Scribe_Defs.Look(ref ArrivalMode, "arrivalMode");
            Scribe_Values.Look(ref RaidStrategyDefName, "raidStrategyDefName");
            Scribe_Values.Look(ref ArrivalModeDefName, "arrivalModeDefName");

            Scribe_Values.Look(ref RetryCount, "retryCount", 0);
            Scribe_Values.Look(ref MaxRetryCount, "maxRetryCount", 3);
            Scribe_Values.Look(ref NextRetryTick, "nextRetryTick", 0);

            // RaidWave data
            Scribe_Values.Look(ref WaveIndex, "waveIndex", 0);
            Scribe_Values.Look(ref TotalWaves, "totalWaves", 0);

            // RaidCallEveryone data
            Scribe_Collections.Look(ref TargetFactionDefNames, "targetFactionDefNames", LookMode.Value);
            Scribe_Values.Look(ref CurrentTargetIndex, "currentTargetIndex", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ResolveRaidDefsFromNames();
                if (MaxRetryCount <= 0)
                {
                    MaxRetryCount = 3;
                }

                if (RetryCount < 0)
                {
                    RetryCount = 0;
                }

                if (NextRetryTick < 0)
                {
                    NextRetryTick = 0;
                }
                if (TargetFactionDefNames == null)
                {
                    TargetFactionDefNames = new List<string>();
                }
            }
        }

        public bool ShouldExecute()
        {
            if (Faction == null || Faction.defeated)
                return false;

            int dueTick = NextRetryTick > 0 ? NextRetryTick : ExecuteTick;
            return Find.TickManager.TicksGame >= dueTick;
        }

        public bool Execute()
        {
            if (Faction == null) return false;

            try
            {
                bool success;
                switch (EventType)
                {
                    case DelayedEventType.Caravan:
                        success = DiplomacyEventManager.TriggerCaravanEvent(Faction, CaravanType);
                        break;
                    case DelayedEventType.Aid:
                        success = DiplomacyEventManager.TriggerAidEvent(Faction, AidType);
                        break;
                    case DelayedEventType.Raid:
                    case DelayedEventType.RaidWave:
                        ResolveRaidDefsFromNames();
                        success = DiplomacyEventManager.TriggerRaidEvent(Faction, RaidPoints, RaidStrategy, ArrivalMode);
                        CacheRaidDefNames();
                        // 触发主动消息
                        if (success)
                        {
                            TriggerRaidArrivalNpcMessage();
                        }
                        break;
                    case DelayedEventType.RaidCallEveryone:
                        success = ExecuteRaidCallEveryoneEvent();
                        break;
                    case DelayedEventType.RaidCallEveryoneAnnounce:
                        success = ExecuteRaidCallEveryoneAnnounceEvent();
                        break;
                    case DelayedEventType.RaidArrivalMessage:
                    case DelayedEventType.RaidDepartureMessage:
                        success = ExecuteRaidNpcMessageEvent();
                        break;
                    case DelayedEventType.RaidWaveEndMessage:
                        success = ExecuteRaidWaveEndMessageEvent();
                        break;
                    default:
                        success = false;
                        break;
                }

                if (success)
                {
                    NextRetryTick = 0;
                }

                return success;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Error executing delayed event: {ex}");
                return false;
            }
        }

        private bool ExecuteRaidCallEveryoneEvent()
        {
            if (TargetFactionDefNames == null || TargetFactionDefNames.Count == 0)
            {
                return false;
            }

            if (CurrentTargetIndex >= TargetFactionDefNames.Count)
            {
                return true; // All targets processed
            }

            string factionDefName = TargetFactionDefNames[CurrentTargetIndex];
            Faction targetFaction = Find.FactionManager.AllFactions
                .FirstOrDefault(f => f.def?.defName == factionDefName);

            if (targetFaction == null || targetFaction.defeated)
            {
                Log.Warning($"[RimChat] RaidCallEveryone: Target faction {factionDefName} not found or defeated, skipping.");
                return true; // Mark as success to move on
            }

            // 根据派系关系决定是袭击还是支援
            bool isFriendly = targetFaction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Ally;
            bool isNeutralOrBetter = targetFaction.PlayerGoodwill >= 0;

            bool success;
            if (isFriendly || isNeutralOrBetter)
            {
                // 友好派系：派遣军事支援
                success = DiplomacyEventManager.TriggerMilitaryAidEvent(targetFaction);
                if (success)
                {
                    Log.Message($"[RimChat] RaidCallEveryone: Triggered military aid from friendly faction {targetFaction.Name}");
                    TriggerAidArrivalNpcMessageForFaction(targetFaction);
                }
            }
            else
            {
                // 敌对派系：发动袭击
                success = DiplomacyEventManager.TriggerRaidEvent(targetFaction, -1, null, null);
                if (success)
                {
                    Log.Message($"[RimChat] RaidCallEveryone: Triggered raid from {targetFaction.Name}");
                    TriggerRaidArrivalNpcMessageForFaction(targetFaction);
                }
            }
            return success;
        }

        private bool ExecuteRaidCallEveryoneAnnounceEvent()
        {
            if (Faction == null || Faction.defeated) return true;
            
            // 根据派系关系决定消息类型
            bool isFriendly = Faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Ally;
            bool isNeutralOrBetter = Faction.PlayerGoodwill >= 0;
            
            string sourceTag;
            string message;
            
            if (isFriendly)
            {
                sourceTag = "aid_announce";
                message = $"盟友，我们的军队正在赶来支援你。坚持住！";
            }
            else if (isNeutralOrBetter)
            {
                sourceTag = "aid_announce";
                message = $"我们的军队正在向你进发，将提供军事支援。";
            }
            else
            {
                sourceTag = "raid_announce";
                message = $"我们的军队正在向你进发。准备好迎接后果吧。";
            }
            
            TriggerNpcDialogueMessage(Faction, sourceTag, message);
            return true;
        }

        private bool ExecuteRaidNpcMessageEvent()
        {
            if (Faction == null || Faction.defeated) return true;
            
            // 根据派系关系决定消息内容
            bool isFriendly = Faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Ally;
            bool isNeutralOrBetter = Faction.PlayerGoodwill >= 0;
            
            string messageType;
            string message;
            
            if (EventType == DelayedEventType.RaidArrivalMessage)
            {
                if (isFriendly || isNeutralOrBetter)
                {
                    messageType = "aid_arrival";
                    message = $"我们的支援部队已经抵达。一起战斗吧！";
                }
                else
                {
                    messageType = "raid_arrival";
                    message = $"我们的军队已经抵达。这是你应得的。";
                }
            }
            else
            {
                if (isFriendly || isNeutralOrBetter)
                {
                    messageType = "aid_departure";
                    message = $"我们的支援部队已经完成任务撤离。祝你好运。";
                }
                else
                {
                    messageType = "raid_departure";
                    message = $"我们的军队已经撤离。记住这次教训。";
                }
            }
            
            TriggerNpcDialogueMessage(Faction, messageType, message);
            return true;
        }

        private bool ExecuteRaidWaveEndMessageEvent()
        {
            if (Faction == null || Faction.defeated) return true;
            
            TriggerNpcDialogueMessage(Faction, "raid_waves_end", 
                $"这是最后一波袭击...暂时。我们还会回来的。");
            return true;
        }

        private void TriggerRaidArrivalNpcMessage()
        {
            if (Faction == null) return;
            
            // 延迟1-2小时发送到达消息
            int delayTicks = Rand.Range(2500, 5000);
            int executeTick = Find.TickManager.TicksGame + delayTicks;
            
            var evt = new DelayedDiplomacyEvent(DelayedEventType.RaidArrivalMessage, Faction, executeTick);
            GameComponent_DiplomacyManager.Instance?.AddDelayedEvent(evt);
        }

        private void TriggerRaidArrivalNpcMessageForFaction(Faction targetFaction)
        {
            if (targetFaction == null) return;
            
            int delayTicks = Rand.Range(2500, 5000);
            int executeTick = Find.TickManager.TicksGame + delayTicks;
            
            var evt = new DelayedDiplomacyEvent(DelayedEventType.RaidArrivalMessage, targetFaction, executeTick);
            GameComponent_DiplomacyManager.Instance?.AddDelayedEvent(evt);
        }

        private void TriggerAidArrivalNpcMessageForFaction(Faction targetFaction)
        {
            if (targetFaction == null) return;
            
            int delayTicks = Rand.Range(2500, 5000);
            int executeTick = Find.TickManager.TicksGame + delayTicks;
            
            // 复用 RaidArrivalMessage 类型，在 Execute 时会根据关系判断内容
            var evt = new DelayedDiplomacyEvent(DelayedEventType.RaidArrivalMessage, targetFaction, executeTick);
            GameComponent_DiplomacyManager.Instance?.AddDelayedEvent(evt);
        }

        private void TriggerNpcDialogueMessage(Faction targetFaction, string sourceTag, string fallbackMessage)
        {
            try
            {
                var pushManager = NpcDialogue.GameComponent_NpcDialoguePushManager.Instance;
                if (pushManager == null)
                {
                    Log.Warning($"[RimChat] NpcDialoguePushManager not available for {sourceTag}");
                    return;
                }

                var context = new NpcDialogue.NpcDialogueTriggerContext
                {
                    Faction = targetFaction,
                    TriggerType = NpcDialogue.NpcDialogueTriggerType.Causal,
                    Category = NpcDialogue.NpcDialogueCategory.WarningThreat,
                    SourceTag = sourceTag,
                    Reason = fallbackMessage,
                    Severity = 3,
                    CreatedTick = Find.TickManager.TicksGame
                };

                pushManager.RegisterCustomTrigger(context);
                Log.Message($"[RimChat] Triggered NPC dialogue: {sourceTag} from {targetFaction.Name}");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Error triggering NPC dialogue: {ex}");
            }
        }

        public bool CanRetry()
        {
            return RetryCount < MaxRetryCount;
        }

        public void ScheduleRetry(int delayTicks)
        {
            int safeDelay = Math.Max(60, delayTicks);
            RetryCount++;
            NextRetryTick = Find.TickManager.TicksGame + safeDelay;
        }

        private void CacheRaidDefNames()
        {
            RaidStrategyDefName = RaidStrategy?.defName ?? RaidStrategyDefName;
            ArrivalModeDefName = ArrivalMode?.defName ?? ArrivalModeDefName;
        }

        private void ResolveRaidDefsFromNames()
        {
            if (RaidStrategy == null && !string.IsNullOrEmpty(RaidStrategyDefName))
            {
                RaidStrategy = DefDatabase<RaidStrategyDef>.GetNamedSilentFail(RaidStrategyDefName);
            }

            if (ArrivalMode == null && !string.IsNullOrEmpty(ArrivalModeDefName))
            {
                ArrivalMode = DefDatabase<PawnsArrivalModeDef>.GetNamedSilentFail(ArrivalModeDefName);
            }
        }
    }
}
