using System;
using RimChat.AI;
using RimChat.DiplomacySystem;
using RimChat.Memory;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>/// 依赖: GameComponent_DiplomacyManager, FactionDialogueSession, AIAction.
 /// 职责: dialoguewindow中的在线state展示, input门控与在线state动作processing.
 ///</summary>
    public partial class Dialog_DiplomacyDialogue
    {
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
            Text.Font = compact ? GameFont.Tiny : GameFont.Tiny;
            Widgets.Label(rect, text);
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

        private bool CanSendMessageNow()
        {
            if (session == null || session.isWaitingForResponse)
            {
                return false;
            }

            if (session.isConversationEndedByNpc)
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

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            showReinitiateButton = session.IsReinitiateAvailable(currentTick);
            if (!showReinitiateButton)
            {
                int remainTicks = session.GetReinitiateRemainingTicks(currentTick);
                if (remainTicks > 0)
                {
                    float hours = remainTicks / (float)GenDate.TicksPerHour;
                    reason += "\n" + "RimChat_ReinitiateCooldownHint".Translate(hours.ToString("F1"));
                }
            }
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

            if (action.ActionType != "exit_dialogue" &&
                action.ActionType != "go_offline" &&
                action.ActionType != "set_dnd")
            {
                return false;
            }

            if (!(RimChat.Core.RimChatMod.Instance?.InstanceSettings?.EnableFactionPresenceStatus ?? true))
            {
                Log.Message($"[RimChat] Presence action ignored because presence system is disabled: {action.ActionType}");
                return false;
            }

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

            return true;
        }

        private bool IsPresenceActionType(string actionType)
        {
            return actionType == "exit_dialogue" ||
                   actionType == "go_offline" ||
                   actionType == "set_dnd";
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

            GameComponent_DiplomacyManager.Instance?.ApplyPresenceAction(currentFaction, actionType, string.Empty, currentSession);
            currentSession.AddMessage("System", BuildPresenceSystemMessage(actionType, string.Empty), false, DialogueMessageType.System);
            Log.Message($"[RimChat] Presence fallback action applied: {actionType}, faction={currentFaction.Name}");
        }

        private string DetectAutoPresenceAction(string dialogueText, Faction currentFaction)
        {
            string text = (dialogueText ?? string.Empty).ToLowerInvariant();

            if (ContainsAny(text, "停止联系", "别再联系", "滚开", "拉黑", "不再回应", "leave me alone", "stop contacting"))
            {
                return "go_offline";
            }

            if (ContainsAny(text, "请勿打扰", "不要打扰", "忙不过来", "稍后再说", "do not disturb", "don't disturb"))
            {
                return "set_dnd";
            }

            if (currentFaction.PlayerGoodwill <= -75 &&
                ContainsAny(text, "威胁", "挑衅", "冒犯", "threat", "insult"))
            {
                return "exit_dialogue";
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
                case "exit_dialogue":
                    return hasReason
                        ? "RimChat_SystemExitDialogueWithReason".Translate(reason)
                        : "RimChat_SystemExitDialogue".Translate();
                case "go_offline":
                    return hasReason
                        ? "RimChat_SystemGoOfflineWithReason".Translate(reason)
                        : "RimChat_SystemGoOffline".Translate();
                case "set_dnd":
                    return hasReason
                        ? "RimChat_SystemSetDndWithReason".Translate(reason)
                        : "RimChat_SystemSetDnd".Translate();
                default:
                    return "RimChat_SystemExitDialogue".Translate();
            }
        }
    }
}
