using RimWorld;

namespace RimChat.Persistence
{
    /// <summary>
    /// Dependencies: faction runtime context and diplomacy prompt snapshot model.
    /// Responsibility: expose warmup, lookup, invalidation, and frame-budget update contract for snapshot cache.
    /// </summary>
    public interface IDiplomacyPromptSnapshotCache
    {
        void WarmupOnLoad();

        bool TryGetSnapshot(Faction faction, out DiplomacyPromptRuntimeSnapshot snapshot);

        void Invalidate(Faction faction = null, string reason = "manual");

        void RequestWarmup(Faction faction, string reason = "request");

        void Tick(int currentTick, int maxBuildsPerTick = 1);
    }
}
