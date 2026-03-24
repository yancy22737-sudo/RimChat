using RimChat.DiplomacySystem;

namespace RimChat.AI
{
    /// <summary>
    /// Dependencies: GameAIInterface prisoner-ransom API.
    /// Responsibility: execute pay_prisoner_ransom action in diplomacy dialogue pipeline.
    /// </summary>
    public partial class AIActionExecutor
    {
        private ActionResult ExecutePayPrisonerRansom(AIAction action)
        {
            if (action?.Parameters == null)
            {
                return ActionResult.Failure("pay_prisoner_ransom requires parameters.");
            }

            GameAIInterface.APIResult result = gameInterface.PayPrisonerRansom(faction, action.Parameters);
            return result.Success
                ? ActionResult.Success(result.Message, result.Data)
                : ActionResult.Failure(result.Message);
        }
    }
}
