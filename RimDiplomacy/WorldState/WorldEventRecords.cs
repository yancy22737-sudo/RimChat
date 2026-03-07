using System.Collections.Generic;
using Verse;

namespace RimDiplomacy.WorldState
{
    /// <summary>
    /// Responsibility: persist a concise public/direct-known world event snapshot.
    /// </summary>
    public class WorldEventRecord : IExposable
    {
        public string EventType;
        public int OccurredTick;
        public int MapId;
        public string MapLabel;
        public bool IsPublic;
        public string Summary;
        public string SourceKey;
        public List<string> KnownFactionIds;

        public WorldEventRecord()
        {
            EventType = "generic";
            OccurredTick = 0;
            MapId = -1;
            MapLabel = string.Empty;
            IsPublic = true;
            Summary = string.Empty;
            SourceKey = string.Empty;
            KnownFactionIds = new List<string>();
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref EventType, "eventType", "generic");
            Scribe_Values.Look(ref OccurredTick, "occurredTick", 0);
            Scribe_Values.Look(ref MapId, "mapId", -1);
            Scribe_Values.Look(ref MapLabel, "mapLabel", string.Empty);
            Scribe_Values.Look(ref IsPublic, "isPublic", true);
            Scribe_Values.Look(ref Summary, "summary", string.Empty);
            Scribe_Values.Look(ref SourceKey, "sourceKey", string.Empty);
            Scribe_Collections.Look(ref KnownFactionIds, "knownFactionIds", LookMode.Value);
            if (KnownFactionIds == null)
            {
                KnownFactionIds = new List<string>();
            }
        }
    }

    /// <summary>
    /// Responsibility: persist aggregated raid casualty intel for faction-aware prompt injection.
    /// </summary>
    public class RaidBattleReportRecord : IExposable
    {
        public int BattleStartTick;
        public int BattleEndTick;
        public int MapId;
        public string MapLabel;
        public string AttackerFactionId;
        public string AttackerFactionName;
        public string DefenderFactionId;
        public string DefenderFactionName;
        public int AttackerDeaths;
        public int DefenderDeaths;
        public int DefenderDowned;
        public string Summary;
        public List<string> KnownFactionIds;

        public RaidBattleReportRecord()
        {
            BattleStartTick = 0;
            BattleEndTick = 0;
            MapId = -1;
            MapLabel = string.Empty;
            AttackerFactionId = string.Empty;
            AttackerFactionName = string.Empty;
            DefenderFactionId = string.Empty;
            DefenderFactionName = string.Empty;
            AttackerDeaths = 0;
            DefenderDeaths = 0;
            DefenderDowned = 0;
            Summary = string.Empty;
            KnownFactionIds = new List<string>();
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref BattleStartTick, "battleStartTick", 0);
            Scribe_Values.Look(ref BattleEndTick, "battleEndTick", 0);
            Scribe_Values.Look(ref MapId, "mapId", -1);
            Scribe_Values.Look(ref MapLabel, "mapLabel", string.Empty);
            Scribe_Values.Look(ref AttackerFactionId, "attackerFactionId", string.Empty);
            Scribe_Values.Look(ref AttackerFactionName, "attackerFactionName", string.Empty);
            Scribe_Values.Look(ref DefenderFactionId, "defenderFactionId", string.Empty);
            Scribe_Values.Look(ref DefenderFactionName, "defenderFactionName", string.Empty);
            Scribe_Values.Look(ref AttackerDeaths, "attackerDeaths", 0);
            Scribe_Values.Look(ref DefenderDeaths, "defenderDeaths", 0);
            Scribe_Values.Look(ref DefenderDowned, "defenderDowned", 0);
            Scribe_Values.Look(ref Summary, "summary", string.Empty);
            Scribe_Collections.Look(ref KnownFactionIds, "knownFactionIds", LookMode.Value);
            if (KnownFactionIds == null)
            {
                KnownFactionIds = new List<string>();
            }
        }
    }
}
