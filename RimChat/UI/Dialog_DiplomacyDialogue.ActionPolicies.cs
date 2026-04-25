using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RimChat.AI;
using RimChat.DiplomacySystem;
using RimChat.Memory;
using RimWorld;
using Verse;

namespace RimChat.UI
{
    /// <summary>/// Dependencies: ParsedResponse, AIAction, FactionDialogueSession runtime state.
    /// Responsibility: diplomacy intent-driven delayed-action mapping, clarification-first policy, and short-window dedupe.
    ///</summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private const int DelayedActionDedupeAssistantTurns = 2;
        private const string SendInfoDirectiveStart = "[SendInfoDirective]";
        private const string SendInfoDirectiveEnd = "[/SendInfoDirective]";

        private static readonly HashSet<string> DelayedActionTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            AIActionNames.RequestItemAirdrop,
            AIActionNames.RequestCaravan,
            AIActionNames.RequestVisitor,
            AIActionNames.RequestAid,
            AIActionNames.RequestRaid,
            AIActionNames.TriggerIncident,
            AIActionNames.CreateQuest
        };

        private static readonly string[] AmbiguousFollowupHints =
        {
            "再发一次", "再发", "发送请求", "还是没收到", "没收到", "再来一次", "催一下",
            "send request", "resend", "still not received", "not received", "send it again"
        };

        private static readonly string[] ConfirmationHints =
        {
            "确认", "是的", "是", "好", "行", "就这个", "下单", "发送吧", "发吧",
            "yes", "confirm", "do it", "go ahead", "place it", "submit it"
        };

        private static readonly string[] CancellationHints =
        {
            "取消", "算了", "不用了", "不用", "不要", "别发", "不需要",
            "cancel", "stop", "no need", "never mind"
        };

        private sealed class SendInfoForcedActionDirective
        {
            public string ActionType;
            public int? Waves;
            public bool ExplicitChallengeRequest;
            public string QuestDefName;
        }

        private static readonly Regex AirdropSingleAmountShorthandPattern = new Regex(
            @"^\s*(?<amount>\d{1,3}(?:,\d{3})*|\d{1,9})\s*(?:银|银币|silver|silvers)?\s*(?:[。.!！?？])?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);


        private ParsedResponse ApplyDiplomacyIntentDrivenActionMapping(
            ParsedResponse parsedResponse,
            FactionDialogueSession currentSession,
            string playerMessage)
        {
            ParsedResponse response = parsedResponse ?? CreateEmptyParsedResponse();
            if (response.Actions == null)
            {
                response.Actions = new List<AIAction>();
            }

            int assistantRound = GetAssistantDialogueRound(currentSession);

            bool hasPendingAirdropSelection = HasPendingAirdropSelection(currentSession);
            if (hasPendingAirdropSelection)
            {
                TryMapAirdropPendingSelectionFollowup(response, currentSession, currentSession.pendingDelayedActionIntent, playerMessage, assistantRound);
            }

            RemoveDelayedActionsWithMissingRequiredParameters(response, currentSession, assistantRound);

            if (!HasDelayedActions(response.Actions))
            {
                TryMapDelayedIntentFromPlayerFollowup(response, currentSession, playerMessage, assistantRound);
            }

            ApplyForcedSendInfoDirective(response, playerMessage);
            RemoveDelayedActionsBlockedByShortDedupe(response, currentSession, assistantRound);
            if (!TryInjectPendingAirdropTradeCardMetadata(response.Actions, currentSession))
            {
                response.Actions = response.Actions
                    .Where(action => !IsRequestItemAirdropAction(action))
                    .ToList();

                string failureMessage = BuildPendingAirdropTradeCardStateLostMessage();
                response.DialogueText = string.IsNullOrWhiteSpace(response.DialogueText)
                    ? failureMessage
                    : $"{response.DialogueText}\n\n{failureMessage}";
            }

            CaptureDelayedIntentFromParsedActions(response.Actions, currentSession, assistantRound);
            return response;
        }

        private static void ApplyForcedSendInfoDirective(ParsedResponse response, string playerMessage)
        {
            if (response == null || !TryParseSendInfoForcedActionDirective(playerMessage, out SendInfoForcedActionDirective directive))
            {
                return;
            }

            response.Actions ??= new List<AIAction>();
            response.Actions = response.Actions
                .Where(action => action != null && !IsConflictingForcedSendInfoAction(action.ActionType))
                .ToList();

            var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
            if (directive.Waves.HasValue)
            {
                parameters["waves"] = directive.Waves.Value;
            }

            if (directive.ExplicitChallengeRequest)
            {
                parameters["explicit_challenge_request"] = true;
            }

            if (!string.IsNullOrWhiteSpace(directive.QuestDefName))
            {
                parameters["questDefName"] = directive.QuestDefName;
            }

            response.Actions.Add(new AIAction
            {
                ActionType = directive.ActionType,
                Parameters = parameters
            });
        }

        private static bool IsConflictingForcedSendInfoAction(string actionType)
        {
            if (string.IsNullOrWhiteSpace(actionType))
            {
                return false;
            }

            return string.Equals(actionType, AIActionNames.RequestRaid, StringComparison.Ordinal) ||
                   string.Equals(actionType, AIActionNames.RequestRaidWaves, StringComparison.Ordinal) ||
                   string.Equals(actionType, AIActionNames.RequestRaidCallEveryone, StringComparison.Ordinal) ||
                   string.Equals(actionType, AIActionNames.RequestCaravan, StringComparison.Ordinal) ||
                   string.Equals(actionType, AIActionNames.RequestVisitor, StringComparison.Ordinal);
        }

        private static bool TryParseSendInfoForcedActionDirective(
            string playerMessage,
            out SendInfoForcedActionDirective directive)
        {
            directive = null;
            if (string.IsNullOrWhiteSpace(playerMessage))
            {
                return false;
            }

            int start = playerMessage.IndexOf(SendInfoDirectiveStart, StringComparison.Ordinal);
            if (start < 0)
            {
                return false;
            }

            int end = playerMessage.IndexOf(SendInfoDirectiveEnd, start, StringComparison.Ordinal);
            if (end < 0)
            {
                return false;
            }

            string block = playerMessage.Substring(start + SendInfoDirectiveStart.Length, end - start - SendInfoDirectiveStart.Length);
            string actionType = ReadDirectiveValue(block, "force_action");
            if (string.IsNullOrWhiteSpace(actionType))
            {
                return false;
            }

            directive = new SendInfoForcedActionDirective
            {
                ActionType = actionType.Trim(),
                ExplicitChallengeRequest = string.Equals(ReadDirectiveValue(block, "explicit_challenge_request"), "true", StringComparison.OrdinalIgnoreCase),
                QuestDefName = ReadDirectiveValue(block, "questDefName")
            };

            if (int.TryParse(ReadDirectiveValue(block, "waves"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int waves))
            {
                directive.Waves = waves;
            }

            return true;
        }

        private static string ReadDirectiveValue(string block, string key)
        {
            if (string.IsNullOrWhiteSpace(block) || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            string[] lines = block.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawLine in lines)
            {
                string line = rawLine?.Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (!line.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return line.Substring(key.Length + 1).Trim();
            }

            return string.Empty;
        }

        private static bool HasPendingAirdropSelection(FactionDialogueSession currentSession)
        {
            if (currentSession?.pendingDelayedActionIntent == null)
            {
                return false;
            }

            if (!string.Equals(currentSession.pendingDelayedActionIntent.ActionType, AIActionNames.RequestItemAirdrop, StringComparison.Ordinal))
            {
                return false;
            }

            return TryReadPendingAirdropCandidates(currentSession.pendingDelayedActionIntent.Parameters, out List<PendingAirdropSelectionCandidate> candidates) && candidates.Count > 0;
        }

        private void RecordDelayedActionRuntimeState(
            List<ActionExecutionOutcome> actionOutcomes,
            FactionDialogueSession currentSession)
        {
            if (currentSession == null || actionOutcomes == null || actionOutcomes.Count == 0)
            {
                return;
            }

            int assistantRoundAfterResponse = GetAssistantDialogueRound(currentSession) + 1;
            bool consumedPending = false;

            foreach (ActionExecutionOutcome outcome in actionOutcomes)
            {
                if (outcome?.Action == null || !outcome.IsSuccess || !IsDelayedActionType(outcome.Action.ActionType))
                {
                    continue;
                }

                if (outcome.Data is ItemAirdropPreparedTradeData ||
                    outcome.Data is ItemAirdropPendingSelectionData ||
                    outcome.Data is ItemAirdropAsyncQueuedData)
                {
                    // Airdrop trades queued for player confirmation are not executed yet.
                    continue;
                }

                var executedIntent = CreatePendingDelayedIntent(outcome.Action, assistantRoundAfterResponse, false, string.Empty);
                if (executedIntent != null)
                {
                    currentSession.lastDelayedActionIntent = executedIntent;
                }

                string signature = BuildActionSignature(outcome.Action.ActionType, outcome.Action.Parameters);
                if (!string.IsNullOrWhiteSpace(signature))
                {
                    currentSession.lastDelayedActionExecutionSignature = signature;
                    currentSession.lastDelayedActionExecutionAssistantRound = assistantRoundAfterResponse;
                }

                consumedPending = true;
            }

            if (consumedPending)
            {
                currentSession.pendingDelayedActionIntent = null;
            }
        }

        private static ParsedResponse CreateEmptyParsedResponse()
        {
            return new ParsedResponse
            {
                Success = true,
                DialogueText = string.Empty,
                Actions = new List<AIAction>(),
                StrategySuggestions = new List<StrategySuggestion>()
            };
        }

        private static int GetAssistantDialogueRound(FactionDialogueSession currentSession)
        {
            if (currentSession?.messages == null)
            {
                return 0;
            }

            return currentSession.messages.Count(msg =>
                msg != null &&
                !msg.isPlayer &&
                msg.messageType == DialogueMessageType.Normal);
        }

        private static bool IsDelayedActionType(string actionType)
        {
            return !string.IsNullOrWhiteSpace(actionType) && DelayedActionTypes.Contains(actionType);
        }

        private static bool HasDelayedActions(List<AIAction> actions)
        {
            return actions != null && actions.Any(action => IsDelayedActionType(action?.ActionType));
        }

        private static void CaptureDelayedIntentFromParsedActions(
            List<AIAction> actions,
            FactionDialogueSession currentSession,
            int assistantRound)
        {
            if (currentSession == null || actions == null)
            {
                return;
            }

            AIAction latestDelayed = actions.LastOrDefault(action => IsDelayedActionType(action?.ActionType));
            if (latestDelayed == null)
            {
                return;
            }

            var intent = CreatePendingDelayedIntent(latestDelayed, assistantRound, false, string.Empty);
            if (intent != null)
            {
                currentSession.lastDelayedActionIntent = intent;
            }
        }

        private static void RemoveDelayedActionsWithMissingRequiredParameters(
            ParsedResponse response,
            FactionDialogueSession currentSession,
            int assistantRound)
        {
            if (response?.Actions == null || response.Actions.Count == 0)
            {
                return;
            }

            var filtered = new List<AIAction>();
            string firstClarification = string.Empty;

            foreach (AIAction action in response.Actions)
            {
                if (action == null || !IsDelayedActionType(action.ActionType))
                {
                    filtered.Add(action);
                    continue;
                }

                string missingParameter = GetMissingRequiredParameter(action.ActionType, action.Parameters);
                if (string.IsNullOrWhiteSpace(missingParameter))
                {
                    filtered.Add(action);
                    continue;
                }

                if (currentSession != null)
                {
                    var pending = CreatePendingDelayedIntent(action, assistantRound, true, missingParameter);
                    if (pending != null)
                    {
                        currentSession.pendingDelayedActionIntent = pending;
                        currentSession.lastDelayedActionIntent = pending.Clone();
                    }
                }

                if (string.IsNullOrWhiteSpace(firstClarification))
                {
                    firstClarification = BuildMissingParameterClarification(action.ActionType, missingParameter, action.Parameters);
                }
            }

            response.Actions = filtered;
            if (!string.IsNullOrWhiteSpace(firstClarification))
            {
                response.DialogueText = firstClarification;
            }
        }

        private static void TryMapDelayedIntentFromPlayerFollowup(
            ParsedResponse response,
            FactionDialogueSession currentSession,
            string playerMessage,
            int assistantRound)
        {
            if (response == null || currentSession == null)
            {
                return;
            }

            string normalizedPlayer = (playerMessage ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedPlayer))
            {
                return;
            }

            if (ContainsAnyHint(normalizedPlayer, CancellationHints))
            {
                currentSession.pendingDelayedActionIntent = null;
                if (currentSession.hasPendingAirdropTradeCardReference)
                {
                    currentSession.ClearPendingAirdropTradeCardReference();
                }

                if (string.IsNullOrWhiteSpace(response.DialogueText))
                {
                    response.DialogueText = "好，这次请求先取消。";
                }
                return;
            }

            PendingDelayedActionIntent baseIntent = currentSession.pendingDelayedActionIntent ?? currentSession.lastDelayedActionIntent;
            if (baseIntent == null)
            {
                return;
            }

            if (TryMapAirdropPendingSelectionFollowup(response, currentSession, baseIntent, playerMessage, assistantRound))
            {
                return;
            }

            if (TryMapAirdropAmountShorthandFollowup(response, currentSession, baseIntent, playerMessage, assistantRound))
            {
                return;
            }

            if (ContainsAnyHint(normalizedPlayer, ConfirmationHints))
            {
                TryMapConfirmedIntentToAction(response, currentSession, baseIntent, assistantRound);
                return;
            }

            if (!ContainsAnyHint(normalizedPlayer, AmbiguousFollowupHints))
            {
                return;
            }

            string missingParameter = GetMissingRequiredParameter(baseIntent.ActionType, baseIntent.Parameters);
            if (!string.IsNullOrWhiteSpace(missingParameter))
            {
                PendingDelayedActionIntent missingIntent = baseIntent.Clone();
                missingIntent.RequiredParameter = missingParameter;
                missingIntent.AwaitingConfirmation = true;
                missingIntent.UpdatedAssistantRound = assistantRound;
                currentSession.pendingDelayedActionIntent = missingIntent;
                if (string.IsNullOrWhiteSpace(response.DialogueText))
                {
                    response.DialogueText = BuildMissingParameterClarification(
                        missingIntent.ActionType,
                        missingParameter,
                        missingIntent.Parameters);
                }
                return;
            }

            PendingDelayedActionIntent confirmIntent = baseIntent.Clone();
            confirmIntent.AwaitingConfirmation = true;
            confirmIntent.RequiredParameter = string.Empty;
            confirmIntent.UpdatedAssistantRound = assistantRound;
            currentSession.pendingDelayedActionIntent = confirmIntent;
            if (string.IsNullOrWhiteSpace(response.DialogueText))
            {
                response.DialogueText = BuildResendConfirmationQuestion(confirmIntent);
            }
        }

        private static bool TryMapAirdropAmountShorthandFollowup(
            ParsedResponse response,
            FactionDialogueSession currentSession,
            PendingDelayedActionIntent baseIntent,
            string playerMessage,
            int assistantRound)
        {
            if (!CanMapAirdropAmountShorthand(response, currentSession, baseIntent, playerMessage, out int amount))
            {
                return false;
            }

            if (TryQueueMissingParameterClarification(response, currentSession, baseIntent, assistantRound))
            {
                return true;
            }

            AIAction mappedAction = BuildAirdropAmountShorthandAction(baseIntent, amount);
            if (response.Actions == null)
            {
                response.Actions = new List<AIAction>();
            }
            response.Actions.Add(mappedAction);

            if (string.IsNullOrWhiteSpace(response.DialogueText))
            {
                response.DialogueText = "RimChat_DiplomacyAirdropAmountMapped".Translate(amount, amount).ToString();
            }

            currentSession.pendingDelayedActionIntent = null;
            currentSession.lastDelayedActionIntent = CreatePendingDelayedIntent(mappedAction, assistantRound, false, string.Empty);
            return true;
        }

        private static bool CanMapAirdropAmountShorthand(
            ParsedResponse response,
            FactionDialogueSession currentSession,
            PendingDelayedActionIntent baseIntent,
            string playerMessage,
            out int amount)
        {
            amount = 0;
            if (response == null || currentSession == null || baseIntent == null)
            {
                return false;
            }

            if (!string.Equals(baseIntent.ActionType, AIActionNames.RequestItemAirdrop, StringComparison.Ordinal))
            {
                return false;
            }

            return TryParseSingleAirdropAmountShorthand(playerMessage, out amount);
        }

        private static bool TryQueueMissingParameterClarification(
            ParsedResponse response,
            FactionDialogueSession currentSession,
            PendingDelayedActionIntent baseIntent,
            int assistantRound)
        {
            string missingParameter = GetMissingRequiredParameter(baseIntent.ActionType, baseIntent.Parameters);
            if (string.IsNullOrWhiteSpace(missingParameter))
            {
                return false;
            }

            PendingDelayedActionIntent missingIntent = baseIntent.Clone();
            missingIntent.RequiredParameter = missingParameter;
            missingIntent.AwaitingConfirmation = true;
            missingIntent.UpdatedAssistantRound = assistantRound;
            currentSession.pendingDelayedActionIntent = missingIntent;
            if (string.IsNullOrWhiteSpace(response.DialogueText))
            {
                response.DialogueText = BuildMissingParameterClarification(
                    missingIntent.ActionType,
                    missingParameter,
                    missingIntent.Parameters);
            }
            return true;
        }

        private static AIAction BuildAirdropAmountShorthandAction(PendingDelayedActionIntent baseIntent, int amount)
        {
            Dictionary<string, object> mappedParameters = CloneParameters(baseIntent.Parameters);
            mappedParameters.Remove("budget_silver");
            mappedParameters["payment_items"] = BuildDefaultSilverPaymentItems(amount);
            return new AIAction
            {
                ActionType = AIActionNames.RequestItemAirdrop,
                Parameters = mappedParameters,
                Reason = "intent_map_amount_shorthand"
            };
        }

        private static bool TryParseSingleAirdropAmountShorthand(string playerMessage, out int amount)
        {
            amount = 0;
            string rawText = (playerMessage ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return false;
            }

            Match amountMatch = AirdropSingleAmountShorthandPattern.Match(rawText);
            if (!amountMatch.Success)
            {
                return false;
            }

            string amountText = amountMatch.Groups["amount"].Value.Replace(",", string.Empty);
            if (!int.TryParse(amountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedAmount) || parsedAmount <= 0)
            {
                return false;
            }

            amount = parsedAmount;
            return true;
        }

        private static List<object> BuildDefaultSilverPaymentItems(int amount)
        {
            var paymentLine = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["item"] = "Silver",
                ["count"] = amount
            };
            return new List<object> { paymentLine };
        }

        private static void TryMapConfirmedIntentToAction(
            ParsedResponse response,
            FactionDialogueSession currentSession,
            PendingDelayedActionIntent baseIntent,
            int assistantRound)
        {
            if (response == null || currentSession == null || baseIntent == null)
            {
                return;
            }

            string missingParameter = GetMissingRequiredParameter(baseIntent.ActionType, baseIntent.Parameters);
            if (!string.IsNullOrWhiteSpace(missingParameter))
            {
                PendingDelayedActionIntent missingIntent = baseIntent.Clone();
                missingIntent.RequiredParameter = missingParameter;
                missingIntent.AwaitingConfirmation = true;
                missingIntent.UpdatedAssistantRound = assistantRound;
                currentSession.pendingDelayedActionIntent = missingIntent;
                if (string.IsNullOrWhiteSpace(response.DialogueText))
                {
                    response.DialogueText = BuildMissingParameterClarification(
                        missingIntent.ActionType,
                        missingParameter,
                        missingIntent.Parameters);
                }
                return;
            }

            string signature = BuildActionSignature(baseIntent.ActionType, baseIntent.Parameters);
            if (IsWithinDelayedDedupeWindow(currentSession, signature, assistantRound))
            {
                if (string.IsNullOrWhiteSpace(response.DialogueText))
                {
                    response.DialogueText = BuildDedupeClarification(baseIntent);
                }
                return;
            }

            if (response.Actions == null)
            {
                response.Actions = new List<AIAction>();
            }
            response.Actions.Add(new AIAction
            {
                ActionType = baseIntent.ActionType,
                Parameters = CloneParameters(baseIntent.Parameters),
                Reason = "intent_map_confirmation"
            });

            if (string.IsNullOrWhiteSpace(response.DialogueText))
            {
                response.DialogueText = BuildConfirmationAcceptedLine(baseIntent);
            }

            currentSession.pendingDelayedActionIntent = null;
        }

        private static void RemoveDelayedActionsBlockedByShortDedupe(
            ParsedResponse response,
            FactionDialogueSession currentSession,
            int assistantRound)
        {
            if (response?.Actions == null || response.Actions.Count == 0 || currentSession == null)
            {
                return;
            }

            var filtered = new List<AIAction>();
            AIAction blockedAction = null;

            foreach (AIAction action in response.Actions)
            {
                if (action == null || !IsDelayedActionType(action.ActionType))
                {
                    filtered.Add(action);
                    continue;
                }

                string signature = BuildActionSignature(action.ActionType, action.Parameters);
                if (IsWithinDelayedDedupeWindow(currentSession, signature, assistantRound))
                {
                    if (blockedAction == null)
                    {
                        blockedAction = action;
                    }
                    continue;
                }

                filtered.Add(action);
            }

            response.Actions = filtered;
            if (blockedAction != null && string.IsNullOrWhiteSpace(response.DialogueText))
            {
                var blockedIntent = CreatePendingDelayedIntent(blockedAction, assistantRound, false, string.Empty);
                response.DialogueText = BuildDedupeClarification(blockedIntent);
            }
        }

        private static bool IsWithinDelayedDedupeWindow(
            FactionDialogueSession currentSession,
            string signature,
            int currentAssistantRound)
        {
            if (currentSession == null || string.IsNullOrWhiteSpace(signature))
            {
                return false;
            }

            if (!string.Equals(
                    currentSession.lastDelayedActionExecutionSignature ?? string.Empty,
                    signature,
                    StringComparison.Ordinal))
            {
                return false;
            }

            int roundDelta = currentAssistantRound - currentSession.lastDelayedActionExecutionAssistantRound;
            return roundDelta >= 0 && roundDelta < DelayedActionDedupeAssistantTurns;
        }

        private static PendingDelayedActionIntent CreatePendingDelayedIntent(
            AIAction action,
            int assistantRound,
            bool awaitingConfirmation,
            string requiredParameter)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.ActionType))
            {
                return null;
            }

            var intent = new PendingDelayedActionIntent
            {
                ActionType = action.ActionType,
                Parameters = CloneParameters(action.Parameters),
                Signature = BuildActionSignature(action.ActionType, action.Parameters),
                RequiredParameter = requiredParameter ?? string.Empty,
                AwaitingConfirmation = awaitingConfirmation,
                CreatedAssistantRound = assistantRound,
                UpdatedAssistantRound = assistantRound
            };
            return intent;
        }

        private static Dictionary<string, object> CloneParameters(Dictionary<string, object> source)
        {
            var clone = new Dictionary<string, object>();
            if (source == null)
            {
                return clone;
            }

            foreach (KeyValuePair<string, object> entry in source)
            {
                clone[entry.Key] = entry.Value;
            }
            return clone;
        }

        private static string BuildActionSignature(string actionType, Dictionary<string, object> parameters)
        {
            if (string.IsNullOrWhiteSpace(actionType))
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.Append(actionType.Trim().ToLowerInvariant());
            if (parameters == null || parameters.Count == 0)
            {
                return sb.ToString();
            }

            foreach (KeyValuePair<string, object> entry in parameters.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                sb.Append('|');
                sb.Append((entry.Key ?? string.Empty).Trim().ToLowerInvariant());
                sb.Append('=');
                sb.Append(NormalizeParameterValue(entry.Value));
            }

            return sb.ToString();
        }

        private static string NormalizeParameterValue(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value is float floatValue)
            {
                return floatValue.ToString(CultureInfo.InvariantCulture);
            }
            if (value is double doubleValue)
            {
                return doubleValue.ToString(CultureInfo.InvariantCulture);
            }
            if (value is decimal decimalValue)
            {
                return decimalValue.ToString(CultureInfo.InvariantCulture);
            }

            return value.ToString()?.Trim().ToLowerInvariant() ?? string.Empty;
        }

        private static string GetMissingRequiredParameter(string actionType, Dictionary<string, object> parameters)
        {
            switch (actionType)
            {
                case AIActionNames.RequestItemAirdrop:
                    return HasNonEmptyParameter(parameters, "need") ? string.Empty : "need";
                case AIActionNames.RequestAid:
                    return HasNonEmptyParameter(parameters, "type") ? string.Empty : "type";
                case AIActionNames.TriggerIncident:
                    return HasNonEmptyParameter(parameters, "defName") ? string.Empty : "defName";
                case AIActionNames.CreateQuest:
                    return HasNonEmptyParameter(parameters, "questDefName") ? string.Empty : "questDefName";
                default:
                    return string.Empty;
            }
        }

        private static bool HasNonEmptyParameter(Dictionary<string, object> parameters, string key)
        {
            if (parameters == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (!parameters.TryGetValue(key, out object value) || value == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(value.ToString());
        }

        private static string BuildMissingParameterClarification(
            string actionType,
            string missingParameter,
            Dictionary<string, object> parameters)
        {
            switch (actionType)
            {
                case AIActionNames.RequestItemAirdrop:
                    return "你这次要我空投什么物资？你准备用哪些物资支付（或直接说一个银币金额）？";
                case AIActionNames.RequestAid:
                    return "你要哪类援助：军事、医疗还是资源？";
                case AIActionNames.TriggerIncident:
                    return "你要我触发哪个事件（defName）？";
                case AIActionNames.CreateQuest:
                    return "你要发布哪一个任务模板（questDefName）？";
                default:
                    return $"要继续这个请求，我还需要补充参数：{missingParameter}。";
            }
        }

        private static string BuildResendConfirmationQuestion(PendingDelayedActionIntent intent)
        {
            string summary = BuildIntentSummary(intent);
            return $"你是要我按这条请求再执行一次吗：{summary}？请回复“确认”或“取消”。";
        }

        private static string BuildConfirmationAcceptedLine(PendingDelayedActionIntent intent)
        {
            return $"明白，我按你确认的内容继续安排：{BuildIntentSummary(intent)}。";
        }

        private static string BuildDedupeClarification(PendingDelayedActionIntent intent)
        {
            return "这条请求刚刚处理过，为避免重复执行，我先不重复提交。";
        }

        private static string BuildIntentSummary(PendingDelayedActionIntent intent)
        {
            if (intent == null)
            {
                return "无可用请求";
            }

            Dictionary<string, object> parameters = intent.Parameters ?? new Dictionary<string, object>();
            switch (intent.ActionType)
            {
                case AIActionNames.RequestItemAirdrop:
                    string need = GetParameterText(parameters, "need", "未指定物资");
                    string payment = BuildPaymentIntentSummary(parameters);
                    return $"空投 {need}（支付：{payment}）";
                case AIActionNames.RequestCaravan:
                    return $"请求商队（goods={GetParameterText(parameters, "goods", "未指定")})";
                case AIActionNames.RequestAid:
                    return $"请求援助（type={GetParameterText(parameters, "type", "未指定")})";
                case AIActionNames.RequestRaid:
                    return $"请求袭击（strategy={GetParameterText(parameters, "strategy", "未指定")}）";
                case AIActionNames.TriggerIncident:
                    return $"触发事件（defName={GetParameterText(parameters, "defName", "未指定")})";
                case AIActionNames.CreateQuest:
                    return $"创建任务（questDefName={GetParameterText(parameters, "questDefName", "未指定")})";
                default:
                    return intent.ActionType;
            }
        }

        private static string GetParameterText(Dictionary<string, object> parameters, string key, string fallback)
        {
            if (parameters != null && parameters.TryGetValue(key, out object value) && value != null)
            {
                string text = value.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            return fallback;
        }

        private static string BuildPaymentIntentSummary(Dictionary<string, object> parameters)
        {
            if (parameters == null ||
                !parameters.TryGetValue("payment_items", out object raw) ||
                !(raw is IEnumerable<object> rows))
            {
                return "未指定";
            }

            List<string> items = rows
                .OfType<Dictionary<string, object>>()
                .Select(row =>
                {
                    string item = GetDictionaryText(row, "item");
                    string count = GetDictionaryText(row, "count");
                    if (string.IsNullOrWhiteSpace(item) || string.IsNullOrWhiteSpace(count))
                    {
                        return string.Empty;
                    }

                    return $"{item}x{count}";
                })
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Take(2)
                .ToList();
            if (items.Count == 0)
            {
                return "未指定";
            }

            return string.Join(" + ", items);
        }

        private static string GetDictionaryText(Dictionary<string, object> values, string key)
        {
            if (values == null || !values.TryGetValue(key, out object raw) || raw == null)
            {
                return string.Empty;
            }

            return raw.ToString()?.Trim() ?? string.Empty;
        }

        private static bool ContainsAnyHint(string normalizedLowerText, string[] hints)
        {
            if (string.IsNullOrWhiteSpace(normalizedLowerText) || hints == null || hints.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < hints.Length; i++)
            {
                string hint = hints[i];
                if (string.IsNullOrWhiteSpace(hint))
                {
                    continue;
                }

                if (normalizedLowerText.Contains(hint.ToLowerInvariant()))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
