using System.Collections.Generic;
using HarmonyLib;
using RimChat.Dialogue;
using RimChat.UI;
using RimWorld;
using Verse;

namespace RimChat.PawnRpgPush
{
    /// <summary>/// Dependencies: Verse.ChoiceLetter, RimChat.UI.Dialog_RPGPawnDialogue.
 /// Responsibility: Render PawnRPG proactive letter and open RPG dialogue with one click.
 ///</summary>
    public class ChoiceLetter_PawnRpgInitiatedDialogue : ChoiceLetter
    {
        private static readonly System.Reflection.FieldInfo InitiatorField =
            AccessTools.Field(typeof(Dialog_RPGPawnDialogue), "initiator");
        private static readonly System.Reflection.FieldInfo TargetField =
            AccessTools.Field(typeof(Dialog_RPGPawnDialogue), "target");
        private static readonly System.Reflection.FieldInfo LetterLabelField =
            AccessTools.Field(typeof(Letter), "label");
        private static readonly System.Reflection.FieldInfo ChoiceTitleField =
            AccessTools.Field(typeof(ChoiceLetter), "title");
        private static readonly System.Reflection.FieldInfo LetterLoadIDField =
            AccessTools.Field(typeof(Letter), "loadID");

        private static int nextUniqueLoadID = 800001;

        private int npcLoadId = -1;
        private int playerLoadId = -1;

        /// <summary>
        /// Assign the next unique loadID and return it.
        /// Called by LoadedObjectDirectoryPatch_FixLegacyLetterLoadID before
        /// RegisterLoaded to prevent "Letter_0" duplicate key crashes on legacy saves.
        /// </summary>
        public static int AssignNextUniqueLoadID()
        {
            return nextUniqueLoadID++;
        }

        public void Setup(Pawn npcPawn, Pawn playerPawn, TaggedString labelText, TaggedString bodyText, LetterDef letterDef)
        {
            npcLoadId = npcPawn?.thingIDNumber ?? -1;
            playerLoadId = playerPawn?.thingIDNumber ?? -1;
            LetterLabelField?.SetValue(this, labelText);
            ChoiceTitleField?.SetValue(this, labelText.ToString());
            Text = bodyText;
            def = letterDef ?? LetterDefOf.NeutralEvent;
            lookTargets = npcPawn != null ? new LookTargets(npcPawn) : LookTargets.Invalid;
        }

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                Pawn npcPawn = ResolvePawn(npcLoadId, null);
                Pawn playerPawn = ResolvePawn(playerLoadId, null);
                if (npcPawn != null && playerPawn != null)
                {
                    string proactiveOpening = Text.ToString();
                    var openOption = new DiaOption("RimChat_PawnRpgPush_OpenDialogue".Translate())
                    {
                        resolveTree = true,
                        action = delegate { TryOpenDialogue(playerPawn, npcPawn, proactiveOpening); }
                    };
                    yield return openOption;
                }

                yield return Option_Postpone;
                yield return Option_Close;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref npcLoadId, "npcLoadId", -1);
            Scribe_Values.Look(ref playerLoadId, "playerLoadId", -1);
            // Legacy loadID=0 fix is now handled by
            // LoadedObjectDirectoryPatch_FixLegacyLetterLoadID which runs
            // before RegisterLoaded, preventing the "Letter_0" duplicate key
            // crash at the source instead of trying to fix it after the fact.
        }

        private static Pawn ResolvePawn(int thingId, Pawn fallback)
        {
            if (fallback != null && !fallback.Destroyed && !fallback.Dead)
            {
                return fallback;
            }

            if (thingId < 0)
            {
                return null;
            }

            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
            {
                if (pawn != null && pawn.thingIDNumber == thingId && !pawn.Dead && !pawn.Destroyed)
                {
                    return pawn;
                }
            }

            return null;
        }

        private static void TryOpenDialogue(Pawn playerPawn, Pawn npcPawn, string proactiveOpening)
        {
            if (playerPawn == null || npcPawn == null || Find.WindowStack == null)
            {
                return;
            }

            if (!PawnDialogueRoutingPolicy.ShouldUseRpgDialogue(playerPawn, npcPawn, out _))
            {
                return;
            }

            if (playerPawn.Downed || npcPawn.Downed || !RestUtility.Awake(playerPawn) || !RestUtility.Awake(npcPawn))
            {
                return;
            }

            if (IsDialogueAlreadyOpen(playerPawn, npcPawn))
            {
                return;
            }

            DialogueWindowCoordinator.TryOpen(
                DialogueOpenIntent.CreateRpg(playerPawn, npcPawn, playerPawn.Map, proactiveOpening),
                out _);
        }

        public static bool IsDialogueAlreadyOpen(Pawn playerPawn, Pawn npcPawn)
        {
            if (Find.WindowStack?.Windows == null || InitiatorField == null || TargetField == null)
            {
                return false;
            }

            foreach (Window window in Find.WindowStack.Windows)
            {
                if (!(window is Dialog_RPGPawnDialogue rpgWindow))
                {
                    continue;
                }

                Pawn openedInitiator = InitiatorField.GetValue(rpgWindow) as Pawn;
                Pawn openedTarget = TargetField.GetValue(rpgWindow) as Pawn;
                if (openedInitiator == playerPawn && openedTarget == npcPawn)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
