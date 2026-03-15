using RimWorld;
using Verse;
using System;
using System.Collections.Generic;
using UnityEngine;
using RimChat.Memory;

namespace RimChat.DiplomacySystem
{
    public partial class GameComponent_RPGManager : GameComponent
    {
        public static GameComponent_RPGManager Instance;

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
            MarkNpcPersonaBootstrapAsNewGame();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            Instance = this;
            RpgNpcDialogueArchiveManager.Instance.OnLoadedGame();
            ScheduleNpcPersonaBootstrapOnLoad();
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            Instance = this;

            // Check if AI Quest Def is loaded
            var questDef = DefDatabase<QuestScriptDef>.GetNamedSilentFail("RimChat_AIQuest");
            if (questDef == null)
            {
                Log.Warning("[RimChat] Failed to find QuestScriptDef 'RimChat_AIQuest'. AI Quests will not be available.");
            }
            else
            {
                Log.Message("[RimChat] QuestScriptDef 'RimChat_AIQuest' loaded successfully.");
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
            ExposeData_NpcPersonaBootstrap();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (pawnDialogueCooldownUntilTick == null)
                {
                    pawnDialogueCooldownUntilTick = new Dictionary<Pawn, int>();
                }

                if (pawnPersonaPrompts == null)
                {
                    pawnPersonaPrompts = new Dictionary<Pawn, string>();
                }

                int currentTick = Find.TickManager?.TicksGame ?? 0;
                pawnDialogueCooldownUntilTick.RemoveAll(kvp => kvp.Key == null || kvp.Key.Dead || kvp.Key.Destroyed || kvp.Value <= currentTick);
                pawnPersonaPrompts.RemoveAll(kvp => kvp.Key == null || kvp.Key.Dead || kvp.Key.Destroyed || string.IsNullOrWhiteSpace(kvp.Value));

                cooldownKeysWorkingList = null;
                cooldownValuesWorkingList = null;
                pawnPersonaPromptKeysWorkingList = null;
                pawnPersonaPromptValuesWorkingList = null;

                RpgNpcDialogueArchiveManager.Instance.OnAfterGameLoad();
                OnPostLoadInit_NpcPersonaBootstrap();
            }
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

