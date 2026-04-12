using Verse;
using Verse.AI.Group;

namespace RimChat.Dialogue
{
    /// <summary>
    /// Dependencies: Pawn lord/duty runtime state.
    /// Responsibility: classify whether a pawn should route through RPG dialogue or vanilla caravan trade.
    /// </summary>
    internal static class PawnDialogueRoutingPolicy
    {
        /// <summary>
        /// Check if a pawn's race is eligible for RPG dialogue.
        /// Uses capability-based whitelist: Humanlike, Mechanoid and Animal always eligible;
        /// ToolUser requires story/skills subsystems (excludes VehiclePawn etc.).
        /// </summary>
        internal static bool IsRpgDialogueEligibleRace(Pawn pawn)
        {
            if (pawn?.RaceProps == null)
            {
                return false;
            }

            // Humanlike, Mechanoid and Animal always have the required subsystems
            if (pawn.RaceProps.Humanlike || pawn.RaceProps.IsMechanoid || pawn.RaceProps.Animal)
            {
                return true;
            }

            // ToolUser may include non-standard Pawn subclasses (e.g. VehiclePawn)
            // that lack story/skills — verify capability before allowing
            if (pawn.RaceProps.ToolUser && pawn.story != null && pawn.skills != null)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Return a reason key explaining why a pawn's race is not eligible for RPG dialogue.
        /// Returns null if the race IS eligible.
        /// </summary>
        internal static string GetIneligibleRaceReason(Pawn pawn)
        {
            if (pawn?.RaceProps == null)
            {
                return "RimChat_Converse_Disabled_NoRace";
            }

            if (pawn.RaceProps.Humanlike || pawn.RaceProps.IsMechanoid || pawn.RaceProps.Animal)
            {
                return null;
            }

            if (pawn.RaceProps.ToolUser)
            {
                if (pawn.story == null || pawn.skills == null)
                {
                    return "RimChat_Converse_Disabled_IncompatibleRace";
                }
                return null;
            }

            // Pawn has RaceProps but none of the known categories
            return "RimChat_Converse_Disabled_IncompatibleRace";
        }

        internal static bool ShouldUseRpgDialogue(Pawn initiator, Pawn target, out string reason)
        {
            reason = string.Empty;
            if (initiator == null)
            {
                reason = "initiator_null";
                return false;
            }

            if (target == null)
            {
                reason = "target_null";
                return false;
            }

            return true;
        }

        internal static bool IsTradeCaravanPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Dead)
            {
                return false;
            }

            Lord lord = pawn.GetLord();
            string lordJobName = lord?.LordJob?.GetType().Name ?? string.Empty;
            if (lordJobName.IndexOf("TradeWithColony", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string dutyName = pawn.mindState?.duty?.def?.defName ?? string.Empty;
            return dutyName.IndexOf("TradeWithColony", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   dutyName.IndexOf("Trader", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
