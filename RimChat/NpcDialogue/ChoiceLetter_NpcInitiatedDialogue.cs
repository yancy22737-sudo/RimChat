using System.Collections.Generic;
using HarmonyLib;
using RimChat.UI;
using RimWorld;
using Verse;

namespace RimChat.NpcDialogue
{
    /// <summary>
    /// Dependencies: Verse.ChoiceLetter, RimChat.UI.Dialog_DiplomacyDialogue.
    /// Responsibility: Render proactive NPC letter with one-click "open diplomacy dialogue" action.
    /// </summary>
    public class ChoiceLetter_NpcInitiatedDialogue : ChoiceLetter
    {
        private static readonly System.Reflection.FieldInfo DialogueFactionField =
            AccessTools.Field(typeof(Dialog_DiplomacyDialogue), "faction");
        private static readonly System.Reflection.FieldInfo LetterLabelField =
            AccessTools.Field(typeof(Letter), "label");
        private static readonly System.Reflection.FieldInfo ChoiceTitleField =
            AccessTools.Field(typeof(ChoiceLetter), "title");

        private int factionLoadId = -1;

        public void Setup(Faction faction, TaggedString labelText, TaggedString bodyText, LetterDef letterDef)
        {
            relatedFaction = faction;
            factionLoadId = faction?.loadID ?? -1;
            LetterLabelField?.SetValue(this, labelText);
            ChoiceTitleField?.SetValue(this, labelText.ToString());
            Text = bodyText;
            def = letterDef ?? LetterDefOf.NeutralEvent;
        }

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                Faction faction = ResolveFaction();
                if (faction != null)
                {
                    var openOption = new DiaOption("RimChat_NpcPush_OpenDialogue".Translate())
                    {
                        resolveTree = true,
                        action = delegate { TryOpenDialogue(faction); }
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
            Scribe_Values.Look(ref factionLoadId, "factionLoadId", -1);
        }

        private Faction ResolveFaction()
        {
            if (relatedFaction != null && !relatedFaction.defeated)
            {
                return relatedFaction;
            }

            if (factionLoadId <= 0 || Find.FactionManager == null)
            {
                return null;
            }

            foreach (Faction faction in Find.FactionManager.AllFactions)
            {
                if (faction != null && faction.loadID == factionLoadId && !faction.defeated)
                {
                    relatedFaction = faction;
                    return faction;
                }
            }

            return null;
        }

        private static void TryOpenDialogue(Faction faction)
        {
            if (faction == null || Find.WindowStack == null)
            {
                return;
            }

            if (IsDialogueAlreadyOpen(faction))
            {
                return;
            }

            Find.WindowStack.Add(new Dialog_DiplomacyDialogue(faction, null, false));
        }

        public static bool IsDialogueAlreadyOpen(Faction faction)
        {
            if (faction == null || Find.WindowStack?.Windows == null || DialogueFactionField == null)
            {
                return false;
            }

            foreach (Window window in Find.WindowStack.Windows)
            {
                if (!(window is Dialog_DiplomacyDialogue dialogueWindow))
                {
                    continue;
                }

                Faction openedFaction = DialogueFactionField.GetValue(dialogueWindow) as Faction;
                if (openedFaction == faction)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
