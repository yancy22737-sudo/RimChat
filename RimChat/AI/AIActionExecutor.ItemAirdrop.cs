using RimChat.DiplomacySystem;

namespace RimChat.AI
{
    /// <summary>
    /// Dependencies: GameAIInterface.
    /// Responsibility: execute request_item_airdrop action in diplomacy dialogue pipeline.
    /// </summary>
    public partial class AIActionExecutor
    {
        private ActionResult ExecuteRequestItemAirdrop(AIAction action)
        {
            if (action?.Parameters == null)
            {
                return ActionResult.Failure("request_item_airdrop requires parameters.");
            }

            var result = gameInterface.RequestItemAirdrop(faction, action.Parameters);
            return result.Success
                ? ActionResult.Success(result.Message, result.Data)
                : ActionResult.Failure(result.Message);
        }
    }
}
