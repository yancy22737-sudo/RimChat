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
 /// 职责: store单个faction的在线state缓存与强制离线信息.
 ///</summary>
    public class FactionPresenceState : IExposable
    {
        public Faction faction;
        public FactionPresenceStatus status = FactionPresenceStatus.Online;
        public int lastResolvedTick = 0;
        public int cacheUntilTick = 0;
        public int forcedOfflineUntilTick = 0;
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

        public bool IsCacheValid(int currentTick)
        {
            return lastResolvedTick > 0 && cacheUntilTick > currentTick;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref status, "status", FactionPresenceStatus.Online);
            Scribe_Values.Look(ref lastResolvedTick, "lastResolvedTick", 0);
            Scribe_Values.Look(ref cacheUntilTick, "cacheUntilTick", 0);
            Scribe_Values.Look(ref forcedOfflineUntilTick, "forcedOfflineUntilTick", 0);
            Scribe_Values.Look(ref lastReason, "lastReason", "");
        }
    }
}
