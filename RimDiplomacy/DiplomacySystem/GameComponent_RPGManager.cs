using RimWorld;
using Verse;
using System.Collections.Generic;
using RimDiplomacy.Relation;

namespace RimDiplomacy.DiplomacySystem
{
    public class GameComponent_RPGManager : GameComponent
    {
        public static GameComponent_RPGManager Instance;
        private Dictionary<Pawn, RPGRelationValues> pValues = new Dictionary<Pawn, RPGRelationValues>();

        public GameComponent_RPGManager(Game game) 
        { 
            Instance = this;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref pValues, "pawnRPGValues", LookMode.Reference, LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (pValues == null) pValues = new Dictionary<Pawn, RPGRelationValues>();
                pValues.RemoveAll(kvp => kvp.Key == null || kvp.Key.Dead || kvp.Key.Destroyed);
            }
        }

        public RPGRelationValues GetOrCreateRelation(Pawn pawn)
        {
            if (pawn == null) return null;
            if (!pValues.TryGetValue(pawn, out var rel))
            {
                rel = new RPGRelationValues();
                pValues[pawn] = rel;
            }
            return rel;
        }
    }
}
