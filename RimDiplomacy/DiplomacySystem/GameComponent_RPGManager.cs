using RimWorld;
using Verse;
using System;
using System.Collections.Generic;
using UnityEngine;
using RimDiplomacy.Relation;
using RimDiplomacy.Memory;

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

        private Dictionary<Pawn, string> pawnPersonaPrompts = new Dictionary<Pawn, string>();
        private List<Pawn> pawnPersonaPromptKeysWorkingList;
        private List<string> pawnPersonaPromptValuesWorkingList;

        private const float DefaultExitCooldownHours = 24f;

        public GameComponent_RPGManager(Game game)
        {
            Instance = this;
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            Instance = this;
            RpgNpcDialogueArchiveManager.Instance.OnNewGame();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            Instance = this;
            RpgNpcDialogueArchiveManager.Instance.OnLoadedGame();
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

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                RpgNpcDialogueArchiveManager.Instance.OnBeforeGameSave();
            }

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

            Scribe_Collections.Look(
                ref pawnPersonaPrompts,
                "pawnPersonaPrompts",
                LookMode.Reference,
                LookMode.Value,
                ref pawnPersonaPromptKeysWorkingList,
                ref pawnPersonaPromptValuesWorkingList);

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

                if (pawnPersonaPrompts == null)
                {
                    pawnPersonaPrompts = new Dictionary<Pawn, string>();
                }

                pValues.RemoveAll(kvp => kvp.Key == null || kvp.Value == null || kvp.Key.Dead || kvp.Key.Destroyed);

                int currentTick = Find.TickManager?.TicksGame ?? 0;
                pawnDialogueCooldownUntilTick.RemoveAll(kvp => kvp.Key == null || kvp.Key.Dead || kvp.Key.Destroyed || kvp.Value <= currentTick);
                pawnPersonaPrompts.RemoveAll(kvp => kvp.Key == null || kvp.Key.Dead || kvp.Key.Destroyed || string.IsNullOrWhiteSpace(kvp.Value));

                pawnKeysWorkingList = null;
                pawnValuesWorkingList = null;
                cooldownKeysWorkingList = null;
                cooldownValuesWorkingList = null;
                pawnPersonaPromptKeysWorkingList = null;
                pawnPersonaPromptValuesWorkingList = null;

                RpgNpcDialogueArchiveManager.Instance.OnAfterGameLoad();
            }
        }

        public RPGRelationValues GetOrCreateRelation(Pawn pawn)
        {
            if (pawn == null)
            {
                return null;
            }

            if (!pValues.TryGetValue(pawn, out RPGRelationValues rel))
            {
                rel = new RPGRelationValues();
                pValues[pawn] = rel;
            }

            return rel;
        }

        public bool TryGetRelation(Pawn pawn, out RPGRelationValues relation)
        {
            relation = null;
            if (pawn == null || pValues == null)
            {
                return false;
            }

            return pValues.TryGetValue(pawn, out relation) && relation != null;
        }

        public void SetRelationValues(Pawn pawn, RPGRelationValues relationValues)
        {
            if (pawn == null || relationValues == null)
            {
                return;
            }

            if (pValues == null)
            {
                pValues = new Dictionary<Pawn, RPGRelationValues>();
            }

            pValues[pawn] = new RPGRelationValues
            {
                Favorability = relationValues.Favorability,
                Trust = relationValues.Trust,
                Fear = relationValues.Fear,
                Respect = relationValues.Respect,
                Dependency = relationValues.Dependency
            };
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

        public int GetDialogueCooldownUntilTick(Pawn pawn)
        {
            if (pawn == null || pawnDialogueCooldownUntilTick == null)
            {
                return 0;
            }

            return pawnDialogueCooldownUntilTick.TryGetValue(pawn, out int untilTick) ? untilTick : 0;
        }

        public void SetDialogueCooldownUntilTick(Pawn pawn, int untilTick)
        {
            if (pawn == null)
            {
                return;
            }

            if (pawnDialogueCooldownUntilTick == null)
            {
                pawnDialogueCooldownUntilTick = new Dictionary<Pawn, int>();
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (untilTick <= currentTick)
            {
                pawnDialogueCooldownUntilTick.Remove(pawn);
                return;
            }

            pawnDialogueCooldownUntilTick[pawn] = untilTick;
        }

        public string GetPawnPersonaPrompt(Pawn pawn)
        {
            if (pawn == null || pawnPersonaPrompts == null)
            {
                return string.Empty;
            }

            return pawnPersonaPrompts.TryGetValue(pawn, out string prompt) ? prompt ?? string.Empty : string.Empty;
        }

        public void SetPawnPersonaPrompt(Pawn pawn, string prompt)
        {
            if (pawn == null)
            {
                return;
            }

            if (pawnPersonaPrompts == null)
            {
                pawnPersonaPrompts = new Dictionary<Pawn, string>();
            }

            string normalized = prompt?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(normalized))
            {
                pawnPersonaPrompts.Remove(pawn);
                return;
            }

            pawnPersonaPrompts[pawn] = normalized;
        }
    }
}

