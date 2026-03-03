using System;
using RimWorld;
using Verse;

namespace RimDiplomacy.DiplomacySystem
{
    public enum DelayedEventType
    {
        Caravan,
        Aid
    }

    public class DelayedDiplomacyEvent : IExposable
    {
        public DelayedEventType EventType;
        public Faction Faction;
        public int ExecuteTick;
        public int CaravanTypeInt;
        public int AidTypeInt;

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
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimDiplomacy] Error executing delayed event: {ex}");
            }
        }
    }
}
