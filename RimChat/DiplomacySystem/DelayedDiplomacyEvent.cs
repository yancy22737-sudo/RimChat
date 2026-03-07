using System;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    public enum DelayedEventType
    {
        Caravan,
        Aid,
        Raid
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
            Scribe_Values.Look(ref EventType, "eventType");
            Scribe_References.Look(ref Faction, "faction");
            Scribe_Values.Look(ref ExecuteTick, "executeTick");
            Scribe_Values.Look(ref CaravanTypeInt, "caravanTypeInt");
            Scribe_Values.Look(ref AidTypeInt, "aidTypeInt");
            
            // Raid data
            Scribe_Values.Look(ref RaidPoints, "raidPoints");
            Scribe_Defs.Look(ref RaidStrategy, "raidStrategy");
            Scribe_Defs.Look(ref ArrivalMode, "arrivalMode");
        }

        public bool ShouldExecute()
        {
            if (Faction == null || Faction.defeated)
                return false;

            return Find.TickManager.TicksGame >= ExecuteTick;
        }

        public void Execute()
        {
            if (Faction == null) return;

            try
            {
                switch (EventType)
                {
                    case DelayedEventType.Caravan:
                        DiplomacyEventManager.TriggerCaravanEvent(Faction, CaravanType);
                        break;
                    case DelayedEventType.Aid:
                        DiplomacyEventManager.TriggerAidEvent(Faction, AidType);
                        break;
                    case DelayedEventType.Raid:
                        DiplomacyEventManager.TriggerRaidEvent(Faction, RaidPoints, RaidStrategy, ArrivalMode);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Error executing delayed event: {ex}");
            }
        }
    }
}
