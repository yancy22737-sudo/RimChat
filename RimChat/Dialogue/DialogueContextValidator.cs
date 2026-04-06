using System;
using RimChat.AI;
using RimWorld;
using Verse;

namespace RimChat.Dialogue
{
    /// <summary>
    /// Fail-fast validation gates shared by window open, request send and callback apply.
    /// </summary>
    public static class DialogueContextValidator
    {
        public static bool ValidateWindowOpen(
            DialogueRuntimeContext runtimeContext,
            DialogueLiveContext liveContext,
            out string reason)
        {
            return ValidateSharedState(runtimeContext, liveContext, true, out reason);
        }

        public static bool ValidateRequestSend(
            DialogueRuntimeContext runtimeContext,
            DialogueLiveContext liveContext,
            out string reason)
        {
            return ValidateSharedState(runtimeContext, liveContext, false, out reason);
        }

        public static bool ValidateCallbackApply(
            DialogueRuntimeContext runtimeContext,
            DialogueLiveContext liveContext,
            string expectedDialogueSessionId,
            out string reason)
        {
            if (!ValidateSharedState(runtimeContext, liveContext, false, out reason))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(expectedDialogueSessionId) &&
                !string.Equals(runtimeContext.DialogueSessionId, expectedDialogueSessionId, StringComparison.Ordinal))
            {
                reason = "dialogue_session_mismatch";
                return false;
            }

            if (runtimeContext.GameContextId != DialogueRuntimeContext.GetCurrentGameContextId())
            {
                reason = "game_context_changed";
                return false;
            }

            if (runtimeContext.ContextVersion != AIChatServiceAsync.Instance.GetCurrentContextVersionSnapshot())
            {
                reason = "context_version_changed";
                return false;
            }

            return true;
        }

        public static bool ValidateActionExecution(
            DialogueRuntimeContext runtimeContext,
            DialogueLiveContext liveContext,
            out string reason)
        {
            return ValidateCallbackApply(runtimeContext, liveContext, runtimeContext?.DialogueSessionId, out reason);
        }

        private static bool ValidateSharedState(
            DialogueRuntimeContext runtimeContext,
            DialogueLiveContext liveContext,
            bool requireWindowStack,
            out string reason)
        {
            reason = string.Empty;
            if (runtimeContext == null || liveContext == null)
            {
                reason = "context_null";
                return false;
            }

            if (Current.ProgramState != ProgramState.Playing)
            {
                reason = "program_state_not_playing";
                return false;
            }

            if (Current.Game == null)
            {
                reason = "game_null";
                return false;
            }

            if (requireWindowStack && Find.WindowStack == null)
            {
                reason = "window_stack_null";
                return false;
            }

            if (runtimeContext.MapUniqueId > 0 &&
                (liveContext.Map == null || liveContext.Map.uniqueID != runtimeContext.MapUniqueId))
            {
                reason = "map_invalid";
                return false;
            }

            if (runtimeContext.Channel == DialogueChannel.Diplomacy)
            {
                if (liveContext.Faction == null || liveContext.Faction.defeated)
                {
                    reason = "faction_invalid";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(runtimeContext.NegotiatorPawnId))
                {
                    if (!DialogueContextResolver.IsPawnValid(liveContext.Negotiator))
                    {
                        reason = "negotiator_invalid";
                        return false;
                    }

                    if (liveContext.Map != null && liveContext.Negotiator.Map != liveContext.Map)
                    {
                        reason = "negotiator_map_mismatch";
                        return false;
                    }
                }

                return true;
            }

            if (!DialogueContextResolver.IsPawnValid(liveContext.Initiator))
            {
                reason = "initiator_invalid";
                return false;
            }

            if (!DialogueContextResolver.IsPawnValid(liveContext.Target))
            {
                reason = "target_invalid";
                return false;
            }

            if (!PawnDialogueRoutingPolicy.ShouldUseRpgDialogue(liveContext.Initiator, liveContext.Target, out string routingReason))
            {
                reason = routingReason == "target_trade_caravan"
                    ? "rpg_target_is_trade_caravan"
                    : routingReason;
                return false;
            }

            if (liveContext.Initiator.Map == null ||
                liveContext.Target.Map == null ||
                liveContext.Initiator.Map != liveContext.Target.Map)
            {
                reason = "rpg_map_mismatch";
                return false;
            }

            if (liveContext.Map != null && liveContext.Initiator.Map != liveContext.Map)
            {
                reason = "initiator_map_invalid";
                return false;
            }

            return true;
        }
    }
}
