using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Dialogue;
using RimWorld;
using Verse;

namespace RimChat.Memory
{
    /// <summary>/// store单个factiondialoguesession的数据
 ///</summary>
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

        // AI requeststate (不save到存档, 重启后需要重新request)
        public string pendingRequestId = null;
        public DialogueRequestLease pendingRequestLease = null;
        public bool isWaitingForResponse = false;
        public int lastDiplomacyRequestQueuedTick = int.MinValue;
        public float lastDiplomacyRequestQueuedRealtime = -1f;
        public int pendingImageRequests = 0;
        public float aiRequestProgress = 0f;
        public string aiError = null;
        public string pendingAirdropRequestId = null;
        public DialogueRequestLease pendingAirdropRequestLease = null;
        public bool isWaitingForAirdropSelection = false;
        public float pendingAirdropRequestStartedRealtime = -1f;
        public int pendingAirdropRequestTimeoutSeconds = 0;
        public bool isWaitingForRansomTargetSelection = false;
        public int boundRansomTargetPawnLoadId = 0;
        public string boundRansomTargetFactionId = string.Empty;
        public bool hasCompletedRansomInfoRequest = false;
        public float ransomAutoReplyCooldownUntilRealtime = -1f;
        public string ransomAutoReplyCooldownCategory = string.Empty;
        
        // 策略建议运行态 (不save到存档)
        public List<PendingStrategySuggestion> pendingStrategySuggestions = new List<PendingStrategySuggestion>();
        public int strategyUsesConsumed = 0;

        // 外交延迟动作意图运行态 (不save到存档)
        public PendingDelayedActionIntent pendingDelayedActionIntent;
        public PendingDelayedActionIntent lastDelayedActionIntent;
        public string lastDelayedActionExecutionSignature = string.Empty;
        public int lastDelayedActionExecutionAssistantRound = -999;

        // Periodic snapshot tracking: last message index already summarized to RPG archive
        // Increments on each periodic snapshot, never decreases. Guards against double-summarize.
        public int lastSummarizedMessageIndex = 0;

        public FactionDialogueSession() { }

        public FactionDialogueSession(Faction faction)
        {
            this.faction = faction;
        }

        public void AddMessage(
            string sender,
            string message,
            bool isPlayer,
            DialogueMessageType messageType = DialogueMessageType.Normal,
            Pawn speakerPawn = null)
        {
            var msg = new DialogueMessageData
            {
                sender = sender,
                message = message,
                isPlayer = isPlayer,
                messageType = messageType
            };
            msg.SetSpeakerPawn(speakerPawn);
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
            
            // 限制message数量, 避免存档过大
            if (messages.Count > 100)
            {
                messages.RemoveAt(0);
            }
        }

        public void AddImageMessage(
            string sender,
            string caption,
            bool isPlayer,
            string imageLocalPath,
            string imageSourceUrl,
            Pawn speakerPawn = null)
        {
            var msg = new DialogueMessageData
            {
                sender = sender,
                message = caption ?? string.Empty,
                isPlayer = isPlayer,
                messageType = DialogueMessageType.Image,
                imageLocalPath = imageLocalPath ?? string.Empty,
                imageSourceUrl = imageSourceUrl ?? string.Empty
            };
            msg.SetSpeakerPawn(speakerPawn);
            msg.SetTimestampFromCurrentGameTick();
            messages.Add(msg);
            lastInteractionTick = Find.TickManager.TicksGame;

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
            pendingImageRequests = 0;
            strategyUsesConsumed = 0;
            pendingStrategySuggestions?.Clear();
            isWaitingForRansomTargetSelection = false;
            boundRansomTargetPawnLoadId = 0;
            boundRansomTargetFactionId = string.Empty;
            hasCompletedRansomInfoRequest = false;
            ransomAutoReplyCooldownUntilRealtime = -1f;
            ransomAutoReplyCooldownCategory = string.Empty;
        }

        public bool HasPendingImageRequests()
        {
            return pendingImageRequests > 0;
        }

        public void BeginImageRequest()
        {
            if (pendingImageRequests < int.MaxValue)
            {
                pendingImageRequests++;
            }
        }

        public void EndImageRequest()
        {
            pendingImageRequests = Math.Max(0, pendingImageRequests - 1);
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
            Scribe_Values.Look(ref lastSummarizedMessageIndex, "lastSummarizedMessageIndex", 0);
        }
    }

    /// <summary>/// message类型枚举
 ///</summary>
    public enum DialogueMessageType
    {
        Normal,    // 普通message (玩家/AI dialogue)
        System,    // Systemmessage (通知, error提示等)
        Image      // Inline image card message
    }

    /// <summary>/// 运行态策略建议 (来自 LLM)
 ///</summary>
    public class PendingStrategySuggestion
    {
        public string StrategyName = string.Empty;
        public string FactReason = string.Empty;
        public List<string> StrategyKeywords = new List<string>();
        public string Content = string.Empty;
    }

    /// <summary>/// 外交延迟动作运行态意图（不持久化）。
    ///</summary>
    public class PendingDelayedActionIntent
    {
        public string ActionType = string.Empty;
        public Dictionary<string, object> Parameters = new Dictionary<string, object>();
        public string Signature = string.Empty;
        public string RequiredParameter = string.Empty;
        public bool AwaitingConfirmation;
        public int CreatedAssistantRound;
        public int UpdatedAssistantRound;

        public PendingDelayedActionIntent Clone()
        {
            var clone = new PendingDelayedActionIntent
            {
                ActionType = ActionType ?? string.Empty,
                Signature = Signature ?? string.Empty,
                RequiredParameter = RequiredParameter ?? string.Empty,
                AwaitingConfirmation = AwaitingConfirmation,
                CreatedAssistantRound = CreatedAssistantRound,
                UpdatedAssistantRound = UpdatedAssistantRound,
                Parameters = new Dictionary<string, object>()
            };

            if (Parameters != null)
            {
                foreach (KeyValuePair<string, object> entry in Parameters)
                {
                    clone.Parameters[entry.Key] = entry.Value;
                }
            }

            return clone;
        }
    }

    /// <summary>/// 可序列化的dialoguemessage数据
 ///</summary>
    public class DialogueMessageData : IExposable
    {
        public string sender;
        public string message;
        public bool isPlayer;
        public DateTime timestamp;
        public DialogueMessageType messageType;
        public string imageLocalPath;
        public string imageSourceUrl;
        public string speakerPawnThingId;
        private Pawn speakerPawn;
        
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
            Scribe_Values.Look(ref imageLocalPath, "imageLocalPath", string.Empty);
            Scribe_Values.Look(ref imageSourceUrl, "imageSourceUrl", string.Empty);
            Scribe_Values.Look(ref speakerPawnThingId, "speakerPawnThingId", string.Empty);
            Scribe_References.Look(ref speakerPawn, "speakerPawn");
            
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

        public bool HasInlineImage()
        {
            return messageType == DialogueMessageType.Image &&
                   !string.IsNullOrWhiteSpace(imageLocalPath);
        }

        public void SetSpeakerPawn(Pawn pawn)
        {
            speakerPawn = pawn;
            speakerPawnThingId = pawn?.ThingID ?? string.Empty;
        }

        public Pawn ResolveSpeakerPawn()
        {
            if (IsPawnReferenceValid(speakerPawn))
            {
                if (string.IsNullOrWhiteSpace(speakerPawnThingId))
                {
                    speakerPawnThingId = speakerPawn.ThingID;
                }
                return speakerPawn;
            }

            if (string.IsNullOrWhiteSpace(speakerPawnThingId))
            {
                speakerPawn = null;
                return null;
            }

            speakerPawn = ResolvePawnByThingId(speakerPawnThingId);
            return speakerPawn;
        }

        private static Pawn ResolvePawnByThingId(string thingId)
        {
            if (string.IsNullOrWhiteSpace(thingId))
            {
                return null;
            }

            Pawn worldPawn = Find.WorldPawns?.AllPawnsAliveOrDead?
                .FirstOrDefault(pawn => string.Equals(pawn?.ThingID, thingId, StringComparison.Ordinal));
            if (IsPawnReferenceValid(worldPawn))
            {
                return worldPawn;
            }

            foreach (Map map in Find.Maps ?? Enumerable.Empty<Map>())
            {
                Pawn mapPawn = map?.mapPawns?.AllPawnsSpawned?
                    .FirstOrDefault(pawn => string.Equals(pawn?.ThingID, thingId, StringComparison.Ordinal));
                if (IsPawnReferenceValid(mapPawn))
                {
                    return mapPawn;
                }
            }

            return null;
        }

        private static bool IsPawnReferenceValid(Pawn pawn)
        {
            return pawn != null && !pawn.Destroyed && !pawn.Dead;
        }
    }
}
