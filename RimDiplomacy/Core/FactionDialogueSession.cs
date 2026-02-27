using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimDiplomacy
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

        public FactionDialogueSession() { }

        public FactionDialogueSession(Faction faction)
        {
            this.faction = faction;
        }

        public void AddMessage(string sender, string message, bool isPlayer)
        {
            var msg = new DialogueMessageData
            {
                sender = sender,
                message = message,
                isPlayer = isPlayer
            };
            msg.SetTimestampFromCurrentGameTick();
            messages.Add(msg);
            lastInteractionTick = Find.TickManager.TicksGame;
            
            // 限制消息数量，避免存档过大
            if (messages.Count > 100)
            {
                messages.RemoveAt(0);
            }
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
        }
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
        
        private int gameTick;

        public DialogueMessageData() { }

        public void ExposeData()
        {
            Scribe_Values.Look(ref sender, "sender", "");
            Scribe_Values.Look(ref message, "message", "");
            Scribe_Values.Look(ref isPlayer, "isPlayer", false);
            Scribe_Values.Look(ref gameTick, "gameTick", 0);
            
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
    }
}
