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
        RaidWave
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
                        break;
                    case DelayedEventType.RaidCallEveryone:
                        success = ExecuteRaidCallEveryoneEvent();
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

            bool success = DiplomacyEventManager.TriggerRaidEvent(targetFaction, -1, null, null);
            if (success)
            {
                Log.Message($"[RimChat] RaidCallEveryone: Triggered raid from {targetFaction.Name}");
            }
            return success;
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
