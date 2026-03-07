using RimChat.AI;
using RimWorld;
using UnityEngine;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: AIActionExecutor, social intent state.
    /// Responsibility: convert high-scored intents into executable AI actions.
    /// </summary>
    public static class SocialCircleActionResolver
    {
        private const float TriggerThreshold = 1.0f;
        private const int ExecuteCooldownTicks = 60000;

        public static void ResolveAndExecute(GameComponent_DiplomacyManager manager, SocialCircleState state, int currentTick)
        {
            if (manager == null || state == null || state.ActionIntents == null) return;
            if (!(RimChat.Core.RimChatMod.Instance?.InstanceSettings?.EnableSocialCircleAutoActions ?? false)) return;

            for (int i = 0; i < state.ActionIntents.Count; i++)
            {
                SocialActionIntent intent = state.ActionIntents[i];
                if (!CanAttemptExecution(state, intent, currentTick)) continue;
                if (!TryBuildAction(intent, out AIAction action)) continue;

                var executor = new AIActionExecutor(intent.Faction);
                ActionResult result = executor.ExecuteAction(action);
                if (result.IsSuccess)
                {
                    intent.Score = 0f;
                    intent.LastExecuteTick = currentTick;
                    state.SetFactionNextActionTick(intent.Faction, currentTick + ExecuteCooldownTicks);
                }
                else
                {
                    intent.Score = Mathf.Max(0f, intent.Score - 0.2f);
                }
            }
        }

        private static bool CanAttemptExecution(SocialCircleState state, SocialActionIntent intent, int currentTick)
        {
            if (intent == null || intent.Faction == null || intent.Faction.defeated) return false;
            if (intent.Score < TriggerThreshold) return false;
            return state.GetFactionNextActionTick(intent.Faction) <= currentTick;
        }

        private static bool TryBuildAction(SocialActionIntent intent, out AIAction action)
        {
            action = null;
            if (intent == null || intent.Faction == null) return false;

            switch (intent.IntentType)
            {
                case SocialIntentType.Raid:
                    if (intent.Faction.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile) return false;
                    action = new AIAction
                    {
                        ActionType = "request_raid",
                        Parameters = new System.Collections.Generic.Dictionary<string, object>
                        {
                            { "strategy", "ImmediateAttackSmart" },
                            { "arrival", "EdgeWalkIn" }
                        },
                        Reason = "social_circle_escalation"
                    };
                    return true;
                case SocialIntentType.Aid:
                    if (intent.Faction.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Ally) return false;
                    action = new AIAction
                    {
                        ActionType = "request_aid",
                        Parameters = new System.Collections.Generic.Dictionary<string, object>
                        {
                            { "type", "Military" }
                        },
                        Reason = "social_circle_positive_trend"
                    };
                    return true;
                case SocialIntentType.Caravan:
                    if (intent.Faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile) return false;
                    action = new AIAction
                    {
                        ActionType = "request_caravan",
                        Parameters = new System.Collections.Generic.Dictionary<string, object>
                        {
                            { "type", "General" }
                        },
                        Reason = "social_circle_trade_opportunity"
                    };
                    return true;
                default:
                    return false;
            }
        }
    }
}
