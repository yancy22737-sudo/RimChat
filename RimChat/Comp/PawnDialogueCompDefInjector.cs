using System;
using System.Collections.Generic;
using Verse;
using RimChat.Comp;

namespace RimChat.Comp
{
    /// <summary>
    /// Dependencies: Verse.DefDatabase, Verse.ThingDef, RimChat.Comp.CompProperties_PawnDialogue.
    /// Responsibility: Inject CompPawnDialogue to all eligible pawn ThingDefs at startup.
    /// This covers mod races, mechanoids, and other pawn types that may not have the comp via XML patch.
    /// </summary>
    internal static class PawnDialogueCompDefInjector
    {
        private static bool _injected;

        public static void EnsureInjected()
        {
            if (_injected)
                return;

            _injected = true;

            try
            {
                var defs = DefDatabase<ThingDef>.AllDefsListForReading;
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
                        continue;

                    AddDialogueComp(def);
                    added++;
                }

                if (added > 0)
                    Log.Message($"[RimChat] Pawn dialogue comp injector finished. Added={added}.");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Pawn dialogue comp injector failed: {ex}");
            }
        }

        private static bool IsEligiblePawnDef(ThingDef def)
        {
            if (def == null || def.race == null)
                return false;

            Type thingClass = def.thingClass;
            if (thingClass == null || !typeof(Pawn).IsAssignableFrom(thingClass))
                return false;

            return true;
        }

        private static bool HasDialogueComp(ThingDef def)
        {
            if (def.comps == null || def.comps.Count == 0)
                return false;

            for (int i = 0; i < def.comps.Count; i++)
            {
                CompProperties comp = def.comps[i];
                if (comp is CompProperties_PawnDialogue || comp?.compClass == typeof(CompPawnDialogue))
                    return true;
            }

            return false;
        }

        private static void AddDialogueComp(ThingDef def)
        {
            if (def.comps == null)
                def.comps = new List<CompProperties>();

            def.comps.Add(new CompProperties_PawnDialogue());
        }
    }
}
