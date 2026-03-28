using System;
using RimChat.AI;
using RimChat.DiplomacySystem;
using RimChat.Memory;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimChat.UI
{
    /// <summary>/// 依赖: GameComponent_DiplomacyManager, FactionDialogueSession, AIAction.
 /// 职责: dialoguewindow中的在线state展示, input门控与在线state动作processing.
 ///</summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private const string AiConversationEndSoundDefName = "RimChat_DiplomacyConversationEndedByAi";

        private void RefreshPresenceOnDialogueOpen()
        {
            var manager = GameComponent_DiplomacyManager.Instance;
            if (manager == null) return;
            manager.RefreshPresenceOnDialogueOpen(faction);
            GetAvailableFactions(true);
        }

        private void LockPresenceCacheOnDialogueClose()
        {
            var manager = GameComponent_DiplomacyManager.Instance;
            if (manager == null) return;
            manager.LockPresenceCacheOnDialogueClose(GetAvailableFactions());
        }

        private void DrawCurrentFactionPresenceStatus(Rect rect)
        {
            DrawFactionPresenceStatus(faction, rect, true);
        }

        private void DrawFactionPresenceStatus(Faction factionToDraw, Rect rect, bool compact)
        {
            var status = GameComponent_DiplomacyManager.Instance?.GetPresenceStatus(factionToDraw) ?? FactionPresenceStatus.Online;
            string label = GetPresenceLabel(status);
            string text = compact ? $"● {label}" : $"● {label}";
            GUI.color = GetPresenceColor(status);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, Mathf.Max(rect.height, 18f)), text);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        private Color GetPresenceColor(FactionPresenceStatus status)
        {
            switch (status)
            {
                case FactionPresenceStatus.Online:
                    return new Color(0.35f, 0.95f, 0.35f);
                case FactionPresenceStatus.DoNotDisturb:
                    return new Color(0.95f, 0.35f, 0.35f);
                default:
                    return new Color(0.7f, 0.7f, 0.75f);
            }
        }

        private string GetPresenceLabel(FactionPresenceStatus status)
        {
            switch (status)
            {
                case FactionPresenceStatus.Online:
                    return "RimChat_PresenceOnline".Translate();
                case FactionPresenceStatus.DoNotDisturb:
                    return "RimChat_PresenceDnd".Translate();
                default:
                    return "RimChat_PresenceOffline".Translate();
            }
        }

        private readonly struct SendGateState
        {
            public readonly bool CanSendNow;
            public readonly bool IsHardBlocked;
            public readonly bool IsSoftBlocked;
            public readonly bool ShowReinitiateButton;
            public readonly string BlockedReason;

            public SendGateState(
                bool canSendNow,
                bool isHardBlocked,
                bool isSoftBlocked,
                bool showReinitiateButton,
                string blockedReason)
            {
                CanSendNow = canSendNow;
                IsHardBlocked = isHardBlocked;
                IsSoftBlocked = isSoftBlocked;
                ShowReinitiateButton = showReinitiateButton;
                BlockedReason = blockedReason;
            }
        }

        private SendGateState EvaluateSendGate()
        {
            bool showReinitiateButton = false;
            string blockedReason = null;

            bool blockedByPresence = IsInputBlockedByPresence(out string presenceReason, out showReinitiateButton);
            bool blockedByAiTurn = IsInputLockedByAiTurn(out string aiTurnReason);

            if (blockedByPresence)
            {
                blockedReason = presenceReason;
            }
            else if (blockedByAiTurn)
            {
                blockedReason = aiTurnReason;
            }

            bool canSendNow = !blockedByPresence && !blockedByAiTurn && CanSendMessageNow();
            // Reinitiate is now inbound-driven; never render manual reinitiate button in UI.
            return new SendGateState(canSendNow, blockedByPresence, blockedByAiTurn, false, blockedReason);
        }

        private bool CanSendMessageNow()
        {
            if (session == null || session.HasPendingImageRequests())
            {
                return false;
            }

            if (session.isConversationEndedByNpc)
            {
                return false;
            }

            if (conversationController.IsRequestDebounced(session))
            {
                return false;
            }

            return (GameComponent_DiplomacyManager.Instance?.CanSendMessage(faction) ?? true);
        }

        private bool IsInputBlockedByPresence(out string reason, out bool showReinitiateButton)
        {
            reason = null;
            showReinitiateButton = false;
            if (session == null) return false;

            var status = GameComponent_DiplomacyManager.Instance?.GetPresenceStatus(faction) ?? FactionPresenceStatus.Online;
            if (status == FactionPresenceStatus.Offline)
            {
                reason = "RimChat_PresenceBlockedOffline".Translate();
                return true;
            }

            if (status == FactionPresenceStatus.DoNotDisturb)
            {
                reason = "RimChat_PresenceBlockedDnd".Translate();
                return true;
            }

            if (!session.isConversationEndedByNpc)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(session.conversationEndReason))
            {
                reason = "RimChat_ConversationEndedReason".Translate(session.conversationEndReason);
            }
            else
            {
                reason = "RimChat_ConversationEnded".Translate();
            }

            showReinitiateButton = false;
            return true;
        }

        private void ReinitiateConversation()
        {
            if (session == null) return;
            session.ReinitiateConversation();
            session.AddMessage("System", "RimChat_ConversationReinitiated".Translate(), false, DialogueMessageType.System);
        }

        private bool TryHandlePresenceAction(AIAction action, FactionDialogueSession currentSession, Faction currentFaction)
        {
            if (action == null || string.IsNullOrEmpty(action.ActionType))
            {
                return false;
            }

            if (action.ActionType != AIActionNames.ExitDialogue &&
                action.ActionType != AIActionNames.GoOffline &&
                action.ActionType != AIActionNames.SetDnd)
            {
                return false;
            }

            if (!(RimChat.Core.RimChatMod.Instance?.InstanceSettings?.EnableFactionPresenceStatus ?? true))
            {
                Log.Message($"[RimChat] Presence action ignored because presence system is disabled: {action.ActionType}");
                return false;
            }

            bool wasConversationEnded = currentSession?.isConversationEndedByNpc ?? false;
            string reason = action.Reason;
            if (action.Parameters != null &&
                action.Parameters.TryGetValue("reason", out object reasonObj) &&
                reasonObj != null &&
                !string.IsNullOrWhiteSpace(reasonObj.ToString()))
            {
                reason = reasonObj.ToString();
            }

            GameComponent_DiplomacyManager.Instance?.ApplyPresenceAction(currentFaction, action.ActionType, reason, currentSession);
            Log.Message($"[RimChat] Presence action applied: {action.ActionType}, faction={currentFaction?.Name ?? "null"}, reason={reason ?? "none"}");

            if (currentSession != null)
            {
                currentSession.AddMessage("System", BuildPresenceSystemMessage(action.ActionType, reason), false, DialogueMessageType.System);
            }

            TryPlayAiConversationEndedSound(currentSession, wasConversationEnded);

            return true;
        }

        private bool IsPresenceActionType(string actionType)
        {
            return actionType == AIActionNames.ExitDialogue ||
                   actionType == AIActionNames.GoOffline ||
                   actionType == AIActionNames.SetDnd;
        }

        private void TryAutoApplyPresenceFallback(string dialogueText, FactionDialogueSession currentSession, Faction currentFaction)
        {
            if (currentSession == null || currentFaction == null || currentSession.isConversationEndedByNpc)
            {
                return;
            }

            if (HasStrategyUsesRemaining(currentSession))
            {
                return;
            }

            if (!(RimChat.Core.RimChatMod.Instance?.InstanceSettings?.EnableFactionPresenceStatus ?? true))
            {
                return;
            }

            string actionType = DetectAutoPresenceAction(dialogueText, currentFaction);
            if (string.IsNullOrEmpty(actionType))
            {
                return;
            }

            bool wasConversationEnded = currentSession.isConversationEndedByNpc;
            GameComponent_DiplomacyManager.Instance?.ApplyPresenceAction(currentFaction, actionType, string.Empty, currentSession);
            currentSession.AddMessage("System", BuildPresenceSystemMessage(actionType, string.Empty), false, DialogueMessageType.System);
            TryPlayAiConversationEndedSound(currentSession, wasConversationEnded);
            Log.Message($"[RimChat] Presence fallback action applied: {actionType}, faction={currentFaction.Name}");
        }

        private void TryPlayAiConversationEndedSound(FactionDialogueSession currentSession, bool wasConversationEnded)
        {
            if (currentSession == null || wasConversationEnded || !currentSession.isConversationEndedByNpc)
            {
                return;
            }

            SoundDef shutdownSound = DefDatabase<SoundDef>.GetNamed(AiConversationEndSoundDefName, false);
            shutdownSound?.PlayOneShotOnCamera();
        }

        private string DetectAutoPresenceAction(string dialogueText, Faction currentFaction)
        {
            string text = (dialogueText ?? string.Empty).ToLowerInvariant();

            if (ContainsAny(text, "停止联系", "别再联系", "滚开", "拉黑", "不再回应", "leave me alone", "stop contacting"))
            {
                return AIActionNames.GoOffline;
            }

            if (ContainsAny(text, "请勿打扰", "不要打扰", "忙不过来", "稍后再说", "do not disturb", "don't disturb"))
            {
                return AIActionNames.SetDnd;
            }

            if (currentFaction.PlayerGoodwill <= -75 &&
                ContainsAny(text, "威胁", "挑衅", "冒犯", "threat", "insult"))
            {
                return AIActionNames.ExitDialogue;
            }

            return null;
        }

        private bool ContainsAny(string source, params string[] tokens)
        {
            if (string.IsNullOrEmpty(source) || tokens == null)
            {
                return false;
            }

            for (int i = 0; i < tokens.Length; i++)
            {
                if (!string.IsNullOrEmpty(tokens[i]) && source.Contains(tokens[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private string BuildPresenceSystemMessage(string actionType, string reason)
        {
            bool hasReason = !string.IsNullOrWhiteSpace(reason);
            switch (actionType)
            {
                case AIActionNames.ExitDialogue:
                    return hasReason
                        ? "RimChat_SystemExitDialogueWithReason".Translate(reason)
                        : "RimChat_SystemExitDialogue".Translate();
                case AIActionNames.GoOffline:
                    return hasReason
                        ? "RimChat_SystemGoOfflineWithReason".Translate(reason)
                        : "RimChat_SystemGoOffline".Translate();
                case AIActionNames.SetDnd:
                    return hasReason
                        ? "RimChat_SystemSetDndWithReason".Translate(reason)
                        : "RimChat_SystemSetDnd".Translate();
                default:
                    return "RimChat_SystemExitDialogue".Translate();
            }
        }
    }
}
