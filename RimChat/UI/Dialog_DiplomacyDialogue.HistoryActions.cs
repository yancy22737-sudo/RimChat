using RimChat.Memory;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: diplomacy window state and dialogue-history management windows.
    /// Responsibility: open diplomacy-history management from the diplomacy window action-tab row.
    /// </summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private void OpenHistoryWindow()
        {
            if (faction == null)
            {
                Messages.Message("RimChat_DiplomacyHistoryNoFaction".Translate(), RimWorld.MessageTypeDefOf.RejectInput, false);
                return;
            }

            Find.WindowStack.Add(new Dialog_DiplomacyHistory(faction));
        }
    }
}
