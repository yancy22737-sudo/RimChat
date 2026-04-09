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
        /// Check if a pawn's race is eligible for RPG dialogue (Humanlike, ToolUser, or Mechanoid).
        /// </summary>
        internal static bool IsRpgDialogueEligibleRace(Pawn pawn)
        {
            if (pawn?.RaceProps == null)
            {
                return false;
            }

            return pawn.RaceProps.Humanlike
                || pawn.RaceProps.ToolUser
                || pawn.RaceProps.IsMechanoid;
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

            if (IsTradeCaravanPawn(target))
            {
                reason = "target_trade_caravan";
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
