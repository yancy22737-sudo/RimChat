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
        RaidDepartureMessage,        // 袭击离开后的主动消息
        RaidWaveEndMessage           // 最终波次结束的主动消息
    }

    public enum CallEveryoneActionKind
    {
        Auto = 0,
        Raid = 1,
        MilitaryAidVanilla = 2
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
        public int CallEveryoneActionKindInt;
        public List<int> ParticipantPawnThingIds;
        public bool TriggerWaveEndAfterDeparture;

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

        public CallEveryoneActionKind CallEveryoneAction
        {
            get => (CallEveryoneActionKind)CallEveryoneActionKindInt;
            set => CallEveryoneActionKindInt = (int)value;
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
            Scribe_Values.Look(ref CallEveryoneActionKindInt, "callEveryoneActionKindInt", 0);
            Scribe_Collections.Look(ref ParticipantPawnThingIds, "participantPawnThingIds", LookMode.Value);
            Scribe_Values.Look(ref TriggerWaveEndAfterDeparture, "triggerWaveEndAfterDeparture", false);

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
                if (ParticipantPawnThingIds == null)
                {
                    ParticipantPawnThingIds = new List<int>();
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
                        success = ExecuteRaidOrWaveEvent();
                        break;
                    case DelayedEventType.RaidCallEveryone:
                        success = ExecuteRaidCallEveryoneEvent();
                        break;
                    case DelayedEventType.RaidCallEveryoneAnnounce:
                        success = ExecuteRaidCallEveryoneAnnounceEvent();
                        break;
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

            bool wasFriendly = IsFriendlyOrNeutral(targetFaction);
            List<int> before = CaptureFactionCombatPawnIds(targetFaction);

            bool success = CallEveryoneAction switch
            {
                CallEveryoneActionKind.MilitaryAidVanilla => DiplomacyEventManager.TriggerMilitaryAidEvent(targetFaction),
                CallEveryoneActionKind.Raid => DiplomacyEventManager.TriggerRaidEvent(targetFaction, -1f, null, null),
                _ => wasFriendly
                    ? DiplomacyEventManager.TriggerMilitaryAidEvent(targetFaction)
                    : DiplomacyEventManager.TriggerRaidEvent(targetFaction, -1f, null, null)
            };

            if (success)
            {
                bool isAid = CallEveryoneAction == CallEveryoneActionKind.MilitaryAidVanilla || (CallEveryoneAction == CallEveryoneActionKind.Auto && wasFriendly);
                ParticipantPawnThingIds = CaptureNewParticipantPawnIds(targetFaction, before);
                Log.Message($"[RimChat] RaidCallEveryone: Triggered {(isAid ? "military aid" : "raid")} from {targetFaction.Name}");
                ScheduleRaidDepartureMonitor(targetFaction, isAid, isFinalWave: false);
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
                message = "RimChat_RaidCallEveryoneAnnounce_Friendly".Translate();
            }
            else if (isNeutralOrBetter)
            {
                sourceTag = "aid_announce";
                message = "RimChat_RaidCallEveryoneAnnounce_Neutral".Translate();
            }
            else
            {
                sourceTag = "raid_announce";
                message = "RimChat_RaidCallEveryoneAnnounce_Hostile".Translate();
            }
            
            TriggerNpcDialogueMessage(Faction, sourceTag, message);
            return true;
        }

        private bool ExecuteRaidNpcMessageEvent()
        {
            if (Faction == null || Faction.defeated) return true;

            bool isAid = CallEveryoneAction == CallEveryoneActionKind.MilitaryAidVanilla ||
                         (CallEveryoneAction == CallEveryoneActionKind.Auto && IsFriendlyOrNeutral(Faction));
            string messageType;
            string message;

            if (HasTrackedParticipantsOnPlayerHomeMaps())
            {
                return false;
            }

            messageType = isAid ? "aid_departure" : "raid_departure";
            message = isAid
                ? "RimChat_RaidCallEveryoneDeparture_Aid".Translate()
                : "RimChat_RaidCallEveryoneDeparture_Raid".Translate();
            TriggerNpcDialogueMessage(Faction, messageType, message);
            if (TriggerWaveEndAfterDeparture)
            {
                TriggerRaidWaveEndNpcMessage(Faction);
            }
            return true;
        }

        private bool ExecuteRaidWaveEndMessageEvent()
        {
            if (Faction == null || Faction.defeated) return true;
            
            TriggerNpcDialogueMessage(Faction, "raid_waves_end", 
                "RimChat_RaidWavesEndMessage".Translate());
            return true;
        }

        private bool ExecuteRaidOrWaveEvent()
        {
            ResolveRaidDefsFromNames();
            List<int> before = CaptureFactionCombatPawnIds(Faction);
            bool success = DiplomacyEventManager.TriggerRaidEvent(Faction, RaidPoints, RaidStrategy, ArrivalMode);
            CacheRaidDefNames();
            if (!success)
            {
                return false;
            }

            ParticipantPawnThingIds = CaptureNewParticipantPawnIds(Faction, before);
            bool isFinalWave = EventType == DelayedEventType.RaidWave && TotalWaves > 0 && WaveIndex >= TotalWaves - 1;
            ScheduleRaidDepartureMonitor(Faction, isAid: false, isFinalWave);
            return true;
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
                    Category = sourceTag.StartsWith("raid_", StringComparison.OrdinalIgnoreCase)
                        ? NpcDialogue.NpcDialogueCategory.WarningThreat
                        : NpcDialogue.NpcDialogueCategory.DiplomacyTask,
                    SourceTag = sourceTag,
                    Reason = fallbackMessage,
                    Severity = sourceTag.StartsWith("raid_", StringComparison.OrdinalIgnoreCase) ? 3 : 2,
                    CreatedTick = Find.TickManager.TicksGame,
                    BypassRateLimit = true,
                    BypassCategoryGate = true,
                    BypassPlayerBusyGate = true
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

        private bool IsFriendlyOrNeutral(Faction faction)
        {
            if (faction == null)
            {
                return false;
            }

            return faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Ally || faction.PlayerGoodwill >= 0;
        }

        private static List<int> CaptureFactionCombatPawnIds(Faction faction)
        {
            if (faction == null || Find.Maps == null)
            {
                return new List<int>();
            }

            return Find.Maps
                .Where(m => m != null && m.IsPlayerHome && m.mapPawns?.AllPawnsSpawned != null)
                .SelectMany(m => m.mapPawns.AllPawnsSpawned)
                .Where(p => p != null && p.Spawned && p.Faction == faction && !p.Dead && !p.Destroyed)
                .Select(p => p.thingIDNumber)
                .Distinct()
                .ToList();
        }

        private static List<int> CaptureNewParticipantPawnIds(Faction faction, List<int> before)
        {
            var previous = before ?? new List<int>();
            HashSet<int> previousSet = new HashSet<int>(previous);
            return CaptureFactionCombatPawnIds(faction).Where(id => !previousSet.Contains(id)).ToList();
        }

        private bool HasTrackedParticipantsOnPlayerHomeMaps()
        {
            if (ParticipantPawnThingIds == null || ParticipantPawnThingIds.Count == 0)
            {
                return false;
            }

            if (Find.Maps == null)
            {
                return false;
            }

            HashSet<int> tracked = new HashSet<int>(ParticipantPawnThingIds);
            foreach (Map map in Find.Maps)
            {
                if (map == null || !map.IsPlayerHome || map.mapPawns?.AllPawnsSpawned == null)
                {
                    continue;
                }

                bool any = map.mapPawns.AllPawnsSpawned.Any(p =>
                    p != null &&
                    p.Spawned &&
                    !p.Dead &&
                    !p.Destroyed &&
                    p.Faction == Faction &&
                    tracked.Contains(p.thingIDNumber));
                if (any)
                {
                    return true;
                }
            }

            return false;
        }

        private void ScheduleRaidDepartureMonitor(Faction targetFaction, bool isAid, bool isFinalWave)
        {
            if (targetFaction == null)
            {
                return;
            }

            var evt = new DelayedDiplomacyEvent(DelayedEventType.RaidDepartureMessage, targetFaction, Find.TickManager.TicksGame + 600)
            {
                MaxRetryCount = 240,
                ParticipantPawnThingIds = ParticipantPawnThingIds?.ToList() ?? new List<int>(),
                TriggerWaveEndAfterDeparture = isFinalWave,
                CallEveryoneAction = isAid ? CallEveryoneActionKind.MilitaryAidVanilla : CallEveryoneActionKind.Raid
            };
            GameComponent_DiplomacyManager.Instance?.AddDelayedEvent(evt);
        }

        private static void TriggerRaidWaveEndNpcMessage(Faction targetFaction)
        {
            if (targetFaction == null)
            {
                return;
            }

            var evt = new DelayedDiplomacyEvent(DelayedEventType.RaidWaveEndMessage, targetFaction, Find.TickManager.TicksGame + 60);
            GameComponent_DiplomacyManager.Instance?.AddDelayedEvent(evt);
        }
    }
}
