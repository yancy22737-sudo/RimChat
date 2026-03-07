using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimChat.Memory
{
    /// <summary>
    /// 存储单个派系对话会话的数据
    /// </summary>
    public class FactionDialogueSession : IExposable
    {
        public Faction faction;
        public List<DialogueMessageData> messages = new List<DialogueMessageData>();
        public int lastInteractionTick = 0;
        public bool hasUnreadMessages = false;
        public bool isConversationEndedByNpc = false;
        public bool allowReinitiate = false;
        public string conversationEndReason = "";
        public int conversationEndedTick = 0;
        public int reinitiateAvailableTick = 0;

        // AI 请求状态（不保存到存档，重启后需要重新请求）
        public string pendingRequestId = null;
        public bool isWaitingForResponse = false;
        public float aiRequestProgress = 0f;
        public string aiError = null;
        
        // 策略建议运行态（不保存到存档）
        public List<PendingStrategySuggestion> pendingStrategySuggestions = new List<PendingStrategySuggestion>();
        public int strategyUsesConsumed = 0;

        public FactionDialogueSession() { }

        public FactionDialogueSession(Faction faction)
        {
            this.faction = faction;
        }

        public void AddMessage(string sender, string message, bool isPlayer, DialogueMessageType messageType = DialogueMessageType.Normal)
        {
            var msg = new DialogueMessageData
            {
                sender = sender,
                message = message,
                isPlayer = isPlayer,
                messageType = messageType
            };
            msg.SetTimestampFromCurrentGameTick();
            messages.Add(msg);
            lastInteractionTick = Find.TickManager.TicksGame;
            if (isPlayer)
            {
                isConversationEndedByNpc = false;
                allowReinitiate = false;
                conversationEndReason = "";
                conversationEndedTick = 0;
                reinitiateAvailableTick = 0;
            }
            
            // 限制消息数量，避免存档过大
            if (messages.Count > 100)
            {
                messages.RemoveAt(0);
            }
        }

        public void MarkConversationEnded(string reason, bool canReinitiate, int reinitiateCooldownTicks = 0)
        {
            isConversationEndedByNpc = true;
            conversationEndReason = reason ?? "";
            conversationEndedTick = Find.TickManager?.TicksGame ?? 0;
            if (!canReinitiate)
            {
                allowReinitiate = false;
                reinitiateAvailableTick = 0;
                return;
            }

            if (reinitiateCooldownTicks <= 0)
            {
                allowReinitiate = true;
                reinitiateAvailableTick = 0;
                return;
            }

            allowReinitiate = false;
            reinitiateAvailableTick = conversationEndedTick + reinitiateCooldownTicks;
        }

        public void ReinitiateConversation()
        {
            isConversationEndedByNpc = false;
            allowReinitiate = false;
            conversationEndReason = "";
            conversationEndedTick = 0;
            reinitiateAvailableTick = 0;
            strategyUsesConsumed = 0;
            pendingStrategySuggestions?.Clear();
        }

        public bool IsReinitiateAvailable(int currentTick)
        {
            if (!isConversationEndedByNpc)
            {
                return false;
            }

            if (allowReinitiate)
            {
                return true;
            }

            if (reinitiateAvailableTick > 0 && currentTick >= reinitiateAvailableTick)
            {
                allowReinitiate = true;
                reinitiateAvailableTick = 0;
                return true;
            }

            return false;
        }

        public int GetReinitiateRemainingTicks(int currentTick)
        {
            if (allowReinitiate || reinitiateAvailableTick <= 0)
            {
                return 0;
            }

            return Math.Max(0, reinitiateAvailableTick - currentTick);
        }

        public void MarkAsRead()
        {
            hasUnreadMessages = false;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref faction, "faction");
            Scribe_Collections.Look(ref messages, "messages", LookMode.Deep);
            Scribe_Values.Look(ref lastInteractionTick, "lastInteractionTick", 0);
            Scribe_Values.Look(ref hasUnreadMessages, "hasUnreadMessages", false);
            Scribe_Values.Look(ref isConversationEndedByNpc, "isConversationEndedByNpc", false);
            Scribe_Values.Look(ref allowReinitiate, "allowReinitiate", false);
            Scribe_Values.Look(ref conversationEndReason, "conversationEndReason", "");
            Scribe_Values.Look(ref conversationEndedTick, "conversationEndedTick", 0);
            Scribe_Values.Look(ref reinitiateAvailableTick, "reinitiateAvailableTick", 0);
        }
    }

    /// <summary>
    /// 消息类型枚举
    /// </summary>
    public enum DialogueMessageType
    {
        Normal,    // 普通消息（玩家/AI 对话）
        System     // 系统消息（通知、错误提示等）
    }

    /// <summary>
    /// 运行态策略建议（来自 LLM）
    /// </summary>
    public class PendingStrategySuggestion
    {
        public string ShortLabel = string.Empty;
        public string TriggerBasis = string.Empty;
        public List<string> StrategyKeywords = new List<string>();
        public string HiddenReply = string.Empty;
    }

    /// <summary>
    /// 可序列化的对话消息数据
    /// </summary>
    public class DialogueMessageData : IExposable
    {
        public string sender;
        public string message;
        public bool isPlayer;
        public DateTime timestamp;
        public DialogueMessageType messageType;
        
        private int gameTick;

        public DialogueMessageData() 
        {
            messageType = DialogueMessageType.Normal;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref sender, "sender", "");
            Scribe_Values.Look(ref message, "message", "");
            Scribe_Values.Look(ref isPlayer, "isPlayer", false);
            Scribe_Values.Look(ref gameTick, "gameTick", 0);
            Scribe_Values.Look(ref messageType, "messageType", DialogueMessageType.Normal);
            
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                timestamp = new DateTime(gameTick);
            }
        }

        public void SetTimestampFromCurrentGameTick()
        {
            gameTick = Find.TickManager.TicksGame;
            timestamp = new DateTime(gameTick);
        }

        public int GetGameTick()
        {
            return gameTick;
        }

        public bool IsSystemMessage()
        {
            return messageType == DialogueMessageType.System;
        }
    }
}
