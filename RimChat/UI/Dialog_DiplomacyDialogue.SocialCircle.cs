using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.AI;
using RimChat.DiplomacySystem;
using RimChat.Memory;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>/// Dependencies: AIAction parser output, GameComponent_DiplomacyManager social APIs.
 /// Responsibility: handle explicit social post actions and dialogue keyword fallback.
 ///</summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private bool TryHandleSocialCircleAction(AIAction action, FactionDialogueSession currentSession, Faction currentFaction)
        {
            if (action == null || !string.Equals(action.ActionType, AIActionNames.PublishPublicPost, StringComparison.Ordinal))
            {
                return false;
            }

            var manager = GameComponent_DiplomacyManager.Instance;
            if (manager == null || currentFaction == null)
            {
                return true;
            }

            if (!(RimChat.Core.RimChatMod.Instance?.InstanceSettings?.EnablePlayerInfluenceNews ?? true))
            {
                currentSession?.AddMessage("System", "RimChat_SocialActionBlocked".Translate(), false, DialogueMessageType.System);
                return true;
            }

            Dictionary<string, object> parameters = action.Parameters ?? new Dictionary<string, object>();
            string targetToken = GetStringParameter(parameters, "targetFaction");
            string summary = GetStringParameter(parameters, "summary");
            string intentHint = GetStringParameter(parameters, "intentHint");
            string categoryToken = GetStringParameter(parameters, "category");
            int sentiment = ParseSentiment(parameters);

            Faction targetFaction = manager.ResolveSocialTargetFaction(targetToken, currentFaction);
            SocialPostCategory category = ParseCategory(categoryToken);
            bool ok = manager.EnqueuePublicPost(
                currentFaction,
                targetFaction,
                category,
                sentiment,
                summary,
                true,
                out SocialPostEnqueueResult enqueueResult,
                intentHint,
                DebugGenerateReason.DialogueExplicit);

            string systemMessage = ok
                ? "RimChat_SocialActionQueued".Translate()
                : "RimChat_SocialActionFailedReason".Translate(
                    GameComponent_DiplomacyManager.GetSocialFailureReasonLabel(enqueueResult.FailureReason));
            currentSession?.AddMessage("System", systemMessage, false, DialogueMessageType.System);
            return true;
        }

        private void TryGenerateDialogueKeywordSocialPost(
            string playerMessage,
            string aiText,
            List<AIAction> actions,
            Faction currentFaction,
            FactionDialogueSession currentSession)
        {
            if (currentFaction == null || string.IsNullOrWhiteSpace(playerMessage)) return;
            if (!(RimChat.Core.RimChatMod.Instance?.InstanceSettings?.EnablePlayerInfluenceNews ?? true)) return;

            bool hasExplicitSocialAction = actions != null &&
                                           actions.Any(a => string.Equals(a?.ActionType, AIActionNames.PublishPublicPost, StringComparison.Ordinal));
            if (hasExplicitSocialAction) return;

            SocialPostEnqueueResult enqueueResult = new SocialPostEnqueueResult
            {
                Triggered = false,
                FailureReason = SocialPostEnqueueFailureReason.Unknown
            };
            bool created = GameComponent_DiplomacyManager.Instance != null &&
                           GameComponent_DiplomacyManager.Instance.TryCreateKeywordDialoguePost(
                               currentFaction,
                               playerMessage,
                               aiText,
                               out enqueueResult);
            if (!enqueueResult.Triggered)
            {
                return;
            }

            if (created)
            {
                currentSession?.AddMessage("System", "RimChat_SocialActionQueued".Translate(), false, DialogueMessageType.System);
            }
            else
            {
                string reasonLabel = GameComponent_DiplomacyManager.GetSocialFailureReasonLabel(enqueueResult.FailureReason);
                currentSession?.AddMessage(
                    "System",
                    "RimChat_SocialActionFailedReason".Translate(reasonLabel),
                    false,
                    DialogueMessageType.System);
            }
        }

        private static string GetStringParameter(Dictionary<string, object> parameters, string key)
        {
            if (parameters == null || string.IsNullOrEmpty(key)) return string.Empty;
            if (!parameters.TryGetValue(key, out object value) || value == null) return string.Empty;
            return value.ToString().Trim();
        }

        private static int ParseSentiment(Dictionary<string, object> parameters)
        {
            if (TryReadInt(parameters, "sentiment", out int sentiment))
            {
                return Math.Max(-2, Math.Min(2, sentiment));
            }
            if (TryReadInt(parameters, "amount", out int amount))
            {
                return Math.Max(-2, Math.Min(2, amount));
            }
            return 0;
        }

        private static bool TryReadInt(Dictionary<string, object> parameters, string key, out int value)
        {
            value = 0;
            if (parameters == null || !parameters.TryGetValue(key, out object raw) || raw == null)
            {
                return false;
            }

            if (raw is int intValue)
            {
                value = intValue;
                return true;
            }

            if (raw is float floatValue)
            {
                value = Mathf.RoundToInt(floatValue);
                return true;
            }

            return int.TryParse(raw.ToString(), out value);
        }

        private static SocialPostCategory ParseCategory(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return SocialPostCategory.Diplomatic;
            }

            if (Enum.TryParse(token, true, out SocialPostCategory parsed))
            {
                return parsed;
            }
            return SocialPostCategory.Diplomatic;
        }
    }
}


