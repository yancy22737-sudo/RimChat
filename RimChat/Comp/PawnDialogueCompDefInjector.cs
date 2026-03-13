using System;
using System.Collections.Generic;
using Verse;

namespace RimChat.Comp
{
    /// <summary>
    /// Dependencies: DefDatabase ThingDef runtime, CompPawnDialogue/CompProperties_PawnDialogue definitions.
    /// Responsibility: ensure all resolved pawn defs (including inherited and custom def node types) have the RPG dialogue comp.
    /// </summary>
    internal static class PawnDialogueCompDefInjector
    {
        private static bool _injected;

        public static void EnsureInjected()
        {
            if (_injected)
            {
                return;
            }

            _injected = true;
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            if (defs == null || defs.Count == 0)
            {
                Log.Warning("[RimChat] Pawn dialogue comp injector: no ThingDef entries available.");
                return;
            }

            int added = 0;
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                if (!IsEligiblePawnDef(def) || HasDialogueComp(def))
                {
                    continue;
                }

                AddDialogueComp(def);
                added++;
            }

            Log.Message($"[RimChat] Pawn dialogue comp injector finished. Added={added}.");
        }

        private static bool IsEligiblePawnDef(ThingDef def)
        {
            if (def == null || def.race == null)
            {
                return false;
            }

            Type thingClass = def.thingClass;
            return thingClass == null || typeof(Pawn).IsAssignableFrom(thingClass);
        }

        private static bool HasDialogueComp(ThingDef def)
        {
            if (def?.comps == null || def.comps.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < def.comps.Count; i++)
            {
                CompProperties comp = def.comps[i];
                if (comp is CompProperties_PawnDialogue || comp?.compClass == typeof(CompPawnDialogue))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddDialogueComp(ThingDef def)
        {
            def.comps ??= new List<CompProperties>();
            def.comps.Add(new CompProperties_PawnDialogue());
        }
    }
}
