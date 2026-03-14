using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: Verse.Scribe, RimWorld.Faction.
    /// Responsibility: persist and expose save-scoped hidden faction visibility overrides for diplomacy UI.
    /// </summary>
    public partial class GameComponent_DiplomacyManager
    {
        private HashSet<Faction> manuallyVisibleHiddenFactions = new HashSet<Faction>();

        public List<Faction> GetManuallyVisibleHiddenFactions()
        {
            CleanupManuallyVisibleHiddenFactions();
            return manuallyVisibleHiddenFactions.ToList();
        }

        public bool IsHiddenFactionManuallyVisible(Faction faction)
        {
            if (faction == null)
            {
                return false;
            }

            CleanupManuallyVisibleHiddenFactions();
            return manuallyVisibleHiddenFactions.Contains(faction);
        }

        public void SetManuallyVisibleHiddenFactions(IEnumerable<Faction> factions)
        {
            manuallyVisibleHiddenFactions.Clear();
            if (factions == null)
            {
                return;
            }

            foreach (Faction faction in factions)
            {
                if (IsSelectableHiddenFaction(faction))
                {
                    manuallyVisibleHiddenFactions.Add(faction);
                }
            }
        }

        private void EnsureHiddenFactionVisibilityState()
        {
            manuallyVisibleHiddenFactions ??= new HashSet<Faction>();
            CleanupManuallyVisibleHiddenFactions();
        }

        private void CleanupManuallyVisibleHiddenFactions()
        {
            if (manuallyVisibleHiddenFactions == null || manuallyVisibleHiddenFactions.Count == 0)
            {
                return;
            }

            manuallyVisibleHiddenFactions.RemoveWhere(faction => !IsSelectableHiddenFaction(faction));
        }

        private static bool IsSelectableHiddenFaction(Faction faction)
        {
            return faction != null &&
                   !faction.IsPlayer &&
                   !faction.defeated &&
                   faction.Hidden;
        }
    }
}
