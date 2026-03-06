using RimWorld;
using Verse;
using System.Collections.Generic;
using UnityEngine;
using RimDiplomacy.Relation;

namespace RimDiplomacy.DiplomacySystem
{
    public class GameComponent_RPGManager : GameComponent
    {
        public static GameComponent_RPGManager Instance;
        private Dictionary<Pawn, RPGRelationValues> pValues = new Dictionary<Pawn, RPGRelationValues>();
        private List<Pawn> pawnKeysWorkingList;
        private List<RPGRelationValues> pawnValuesWorkingList;

        private Dictionary<Pawn, int> pawnDialogueCooldownUntilTick = new Dictionary<Pawn, int>();
        private List<Pawn> cooldownKeysWorkingList;
        private List<int> cooldownValuesWorkingList;

        private const float DefaultExitCooldownHours = 3f;

        public GameComponent_RPGManager(Game game)
        {
            Instance = this;
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            Instance = this;

            // Check if AI Quest Def is loaded
            var questDef = DefDatabase<QuestScriptDef>.GetNamedSilentFail("RimDiplomacy_AIQuest");
            if (questDef == null)
            {
                Log.Warning("[RimDiplomacy] Failed to find QuestScriptDef 'RimDiplomacy_AIQuest'. AI Quests will not be available.");
            }
            else
            {
                Log.Message("[RimDiplomacy] QuestScriptDef 'RimDiplomacy_AIQuest' loaded successfully.");
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(
                ref pValues,
                "pawnRPGValues",
                LookMode.Reference,
                LookMode.Deep,
                ref pawnKeysWorkingList,
                ref pawnValuesWorkingList);

            Scribe_Collections.Look(
                ref pawnDialogueCooldownUntilTick,
                "pawnDialogueCooldownUntilTick",
                LookMode.Reference,
                LookMode.Value,
                ref cooldownKeysWorkingList,
                ref cooldownValuesWorkingList);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (pValues == null)
                {
                    pValues = new Dictionary<Pawn, RPGRelationValues>();
                }
                if (pawnDialogueCooldownUntilTick == null)
                {
                    pawnDialogueCooldownUntilTick = new Dictionary<Pawn, int>();
                }

                pValues.RemoveAll(kvp => kvp.Key == null || kvp.Value == null || kvp.Key.Dead || kvp.Key.Destroyed);

                int currentTick = Find.TickManager?.TicksGame ?? 0;
                pawnDialogueCooldownUntilTick.RemoveAll(kvp => kvp.Key == null || kvp.Key.Dead || kvp.Key.Destroyed || kvp.Value <= currentTick);

                pawnKeysWorkingList = null;
                pawnValuesWorkingList = null;
                cooldownKeysWorkingList = null;
                cooldownValuesWorkingList = null;
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

        public int GetRpgDialogueExitCooldownTicks()
        {
            return Mathf.RoundToInt(DefaultExitCooldownHours * 2500f);
        }

        public void StartRpgDialogueCooldown(Pawn pawn, int cooldownTicks)
        {
            if (pawn == null || cooldownTicks <= 0)
            {
                return;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            int untilTick = currentTick + cooldownTicks;
            if (pawnDialogueCooldownUntilTick.TryGetValue(pawn, out int existing))
            {
                pawnDialogueCooldownUntilTick[pawn] = Mathf.Max(existing, untilTick);
                return;
            }

            pawnDialogueCooldownUntilTick[pawn] = untilTick;
        }

        public bool IsRpgDialogueOnCooldown(Pawn pawn, out int remainingTicks)
        {
            remainingTicks = 0;
            if (pawn == null || pawnDialogueCooldownUntilTick == null)
            {
                return false;
            }

            if (!pawnDialogueCooldownUntilTick.TryGetValue(pawn, out int untilTick))
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            remainingTicks = untilTick - currentTick;
            if (remainingTicks > 0)
            {
                return true;
            }

            pawnDialogueCooldownUntilTick.Remove(pawn);
            remainingTicks = 0;
            return false;
        }
    }
}
