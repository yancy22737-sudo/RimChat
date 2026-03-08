using Verse;

namespace RimChat.Config
{
    /// <summary>/// Responsibility: control world-event and battle-intel prompt injection behavior.
 ///</summary>
    public class EventIntelPromptConfig : IExposable
    {
        public bool Enabled;
        public bool ApplyToDiplomacy;
        public bool ApplyToRpg;
        public bool IncludeMapEvents;
        public bool IncludeRaidBattleReports;
        public int DaysWindow;
        public int MaxStoredRecords;
        public int MaxInjectedItems;
        public int MaxInjectedChars;

        public EventIntelPromptConfig()
        {
            Enabled = true;
            ApplyToDiplomacy = true;
            ApplyToRpg = true;
            IncludeMapEvents = true;
            IncludeRaidBattleReports = true;
            DaysWindow = 15;
            MaxStoredRecords = 50;
            MaxInjectedItems = 8;
            MaxInjectedChars = 1200;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Values.Look(ref ApplyToDiplomacy, "applyToDiplomacy", true);
            Scribe_Values.Look(ref ApplyToRpg, "applyToRpg", true);
            Scribe_Values.Look(ref IncludeMapEvents, "includeMapEvents", true);
            Scribe_Values.Look(ref IncludeRaidBattleReports, "includeRaidBattleReports", true);
            Scribe_Values.Look(ref DaysWindow, "daysWindow", 15);
            Scribe_Values.Look(ref MaxStoredRecords, "maxStoredRecords", 50);
            Scribe_Values.Look(ref MaxInjectedItems, "maxInjectedItems", 8);
            Scribe_Values.Look(ref MaxInjectedChars, "maxInjectedChars", 1200);
        }

        public EventIntelPromptConfig Clone()
        {
            return new EventIntelPromptConfig
            {
                Enabled = Enabled,
                ApplyToDiplomacy = ApplyToDiplomacy,
                ApplyToRpg = ApplyToRpg,
                IncludeMapEvents = IncludeMapEvents,
                IncludeRaidBattleReports = IncludeRaidBattleReports,
                DaysWindow = DaysWindow,
                MaxStoredRecords = MaxStoredRecords,
                MaxInjectedItems = MaxInjectedItems,
                MaxInjectedChars = MaxInjectedChars
            };
        }
    }
}
