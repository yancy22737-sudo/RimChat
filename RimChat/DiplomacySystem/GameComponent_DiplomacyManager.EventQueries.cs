using RimWorld;

namespace RimChat.DiplomacySystem
{
    public partial class GameComponent_DiplomacyManager
    {
        public bool HasCaravanDispatchedNow(Faction faction)
        {
            return GameComponent_DelayedEventManager.Instance?.HasCaravanDispatchedNow(faction) ?? false;
        }

        public bool HasRaidScheduledNow(Faction faction)
        {
            return GameComponent_DelayedEventManager.Instance?.HasRaidScheduledNow(faction) ?? false;
        }
    }
}
