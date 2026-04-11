using System.Linq;
using RimChat.Util;
using RimWorld;
using Verse;

namespace RimChat.Memory
{
    /// <summary>/// 依赖: RimWorld.Faction, Verse.Scribe.
 /// 职责: 表示faction在diplomacydialogue中的在线state与缓存信息.
 ///</summary>
    public enum FactionPresenceStatus
    {
        Online = 0,
        Offline = 1,
        DoNotDisturb = 2
    }

    /// <summary>/// 依赖: RimWorld.Faction, Verse.IExposable.
    /// 职责: store单个faction的在线state缓存、强制离线与DND到期信息.
 ///</summary>
    public class FactionPresenceState : IExposable
    {
        public Faction faction;
        public FactionPresenceStatus status = FactionPresenceStatus.Online;
        public int lastResolvedTick = 0;
        public int cacheUntilTick = 0;
        public int forcedOfflineUntilTick = 0;
        public int doNotDisturbUntilTick = 0;
        public string lastReason = "";

        public FactionPresenceState()
        {
        }

        public FactionPresenceState(Faction faction)
        {
            this.faction = faction;
        }

        public bool IsForcedOffline(int currentTick)
        {
            return forcedOfflineUntilTick > currentTick;
        }

        public bool IsDoNotDisturb(int currentTick)
        {
            return doNotDisturbUntilTick > currentTick;
        }

        public bool IsCacheValid(int currentTick)
        {
            return lastResolvedTick > 0 && cacheUntilTick > currentTick;
        }

        public void ExposeData()
        {
            string factionId = faction?.GetUniqueLoadID() ?? string.Empty;
            Scribe_Values.Look(ref factionId, "factionId", string.Empty);
            // Remove legacy <faction> reference node from old saves without registering
            // in CrossRefHandler — prevents "Not all loadIDs consumed" on dead factions.
            LegacyScribeHelper.RemoveLegacyReferenceNode("faction");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (!string.IsNullOrEmpty(factionId))
                {
                    faction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.GetUniqueLoadID() == factionId);
                }
                // If factionId is empty, faction remains null and will be cleaned up
                // by CleanupInvalidPresenceStates() in LoadedGame().
            }
            Scribe_Values.Look(ref status, "status", FactionPresenceStatus.Online);
            Scribe_Values.Look(ref lastResolvedTick, "lastResolvedTick", 0);
            Scribe_Values.Look(ref cacheUntilTick, "cacheUntilTick", 0);
            Scribe_Values.Look(ref forcedOfflineUntilTick, "forcedOfflineUntilTick", 0);
            Scribe_Values.Look(ref doNotDisturbUntilTick, "doNotDisturbUntilTick", 0);
            Scribe_Values.Look(ref lastReason, "lastReason", "");
        }
    }
}
