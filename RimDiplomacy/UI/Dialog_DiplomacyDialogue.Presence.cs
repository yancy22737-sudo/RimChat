using System;
using RimDiplomacy.AI;
using RimDiplomacy.DiplomacySystem;
using RimDiplomacy.Memory;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimDiplomacy.UI
{
    /// <summary>
    /// 依赖: GameComponent_DiplomacyManager, FactionDialogueSession, AIAction.
    /// 职责: 对话窗口中的在线状态展示、输入门控与在线状态动作处理。
    /// </summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private void RefreshPresenceOnDialogueOpen()
        {
            var manager = GameComponent_DiplomacyManager.Instance;
            if (manager == null) return;
            manager.RefreshPresenceOnDialogueOpen(faction);
            manager.RefreshPresenceForFactions(GetAvailableFactions());
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
                    return "RimDiplomacy_PresenceOnline".Translate();
                case FactionPresenceStatus.DoNotDisturb:
                    return "RimDiplomacy_PresenceDnd".Translate();
                default:
                    return "RimDiplomacy_PresenceOffline".Translate();
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
                reason = "RimDiplomacy_PresenceBlockedOffline".Translate();
                return true;
            }

            if (status == FactionPresenceStatus.DoNotDisturb)
            {
                reason = "RimDiplomacy_PresenceBlockedDnd".Translate();
                return true;
            }

            if (!session.isConversationEndedByNpc)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(session.conversationEndReason))
            {
                reason = "RimDiplomacy_ConversationEndedReason".Translate(session.conversationEndReason);
            }
            else
            {
                reason = "RimDiplomacy_ConversationEnded".Translate();
            }

            showReinitiateButton = session.allowReinitiate;
            return true;
        }

        private void ReinitiateConversation()
        {
            if (session == null) return;
            session.ReinitiateConversation();
            session.AddMessage("System", "RimDiplomacy_ConversationReinitiated".Translate(), false, DialogueMessageType.System);
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

            if (!(RimDiplomacy.Core.RimDiplomacyMod.Instance?.InstanceSettings?.EnableFactionPresenceStatus ?? true))
            {
                Log.Message($"[RimDiplomacy] Presence action ignored because presence system is disabled: {action.ActionType}");
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
            Log.Message($"[RimDiplomacy] Presence action applied: {action.ActionType}, faction={currentFaction?.Name ?? "null"}, reason={reason ?? "none"}");

            if (currentSession != null)
            {
                currentSession.AddMessage("System", BuildPresenceSystemMessage(action.ActionType, reason), false, DialogueMessageType.System);
            }

            return true;
        }

        private string BuildPresenceSystemMessage(string actionType, string reason)
        {
            bool hasReason = !string.IsNullOrWhiteSpace(reason);
            switch (actionType)
            {
                case "exit_dialogue":
                    return hasReason
                        ? "RimDiplomacy_SystemExitDialogueWithReason".Translate(reason)
                        : "RimDiplomacy_SystemExitDialogue".Translate();
                case "go_offline":
                    return hasReason
                        ? "RimDiplomacy_SystemGoOfflineWithReason".Translate(reason)
                        : "RimDiplomacy_SystemGoOffline".Translate();
                case "set_dnd":
                    return hasReason
                        ? "RimDiplomacy_SystemSetDndWithReason".Translate(reason)
                        : "RimDiplomacy_SystemSetDnd".Translate();
                default:
                    return "RimDiplomacy_SystemExitDialogue".Translate();
            }
        }
    }
}
