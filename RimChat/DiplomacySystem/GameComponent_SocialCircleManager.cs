using RimChat.Core;
using RimChat.Memory;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: DiplomacyManager (for session messages), SocialCircleState, AIChatServiceAsync.
    /// Responsibility: own SocialCircle tick cadence, news generation lifecycle, and post caching,
    /// removing per-tick SocialCircle overhead from DiplomacyManager.
    /// </summary>
    public class GameComponent_SocialCircleManager : GameComponent
    {
        public static GameComponent_SocialCircleManager Instance;

        public GameComponent_SocialCircleManager(Game game)
        {
            Instance = this;
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            Instance = this;
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            Instance = this;
        }

        public override void GameComponentTick()
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick <= 0)
                return;

            var dm = GameComponent_DiplomacyManager.Instance;
            if (dm == null)
                return;

            if (currentTick % 60 == 0)
                dm.ProcessDeferredSocialNewsSeeds(currentTick);

            if (currentTick % 2000 == 0)
                dm.ProcessSocialCircleTick();
        }
    }
}
