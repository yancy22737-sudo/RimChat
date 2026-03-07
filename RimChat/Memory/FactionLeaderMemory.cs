using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using RimChat.AI;
using RimChat.Relation;

namespace RimChat.Memory
{
    /// <summary>
    /// 派系领袖的记忆数据结构
    /// 记录该领袖对其他所有派系的认知和交互历史
    /// 包含对玩家派系的5维关系评估系统
    /// </summary>
    public class FactionLeaderMemory : IExposable
    {
        /// <summary>
        /// 领袖所属派系的 ID
        /// </summary>
        public string OwnerFactionId { get; set; }
        
        /// <summary>
        /// 领袖所属派系的名称
        /// </summary>
        public string OwnerFactionName { get; set; }
        
        /// <summary>
        /// 领袖的名字（如果有）
        /// </summary>
        public string LeaderName { get; set; }
        
        /// <summary>
        /// 对其他派系的记忆列表
        /// </summary>
        public List<FactionMemoryEntry> FactionMemories = new List<FactionMemoryEntry>();
        
        /// <summary>
        /// 重要事件记忆（宣战、议和、重大贸易等）
        /// </summary>
        public List<SignificantEventMemory> SignificantEvents = new List<SignificantEventMemory>();
        
        /// <summary>
        /// 对话历史记录
        /// </summary>
        public List<DialogueRecord> DialogueHistory = new List<DialogueRecord>();

        /// <summary>
        /// RPG 通道：非玩家派系 Pawn 离图摘要池
        /// </summary>
        public List<CrossChannelSummaryRecord> RpgDepartSummaries = new List<CrossChannelSummaryRecord>();

        /// <summary>
        /// 外交通道：会话结束摘要池
        /// </summary>
        public List<CrossChannelSummaryRecord> DiplomacySessionSummaries = new List<CrossChannelSummaryRecord>();
        
        /// <summary>
        /// 【新增】对玩家派系的关系值（5维评估）
        /// </summary>
        public FactionRelationValues PlayerRelationValues = new FactionRelationValues();
        
        /// <summary>
        /// 最后更新时间 tick
        /// </summary>
        public int LastUpdatedTick { get; set; }
        
        /// <summary>
        /// 创建时间戳
        /// </summary>
        public long CreatedTimestamp { get; set; }
        
        /// <summary>
        /// 最后保存时间戳
        /// </summary>
        public long LastSavedTimestamp { get; set; }
        
        /// <summary>
        /// 最后衰减检查时间 tick
        /// </summary>
        public int LastDecayCheckTick { get; set; }

        public FactionLeaderMemory()
        {
            CreatedTimestamp = DateTime.Now.Ticks;
        }

        public FactionLeaderMemory(Faction ownerFaction) : this()
        {
            OwnerFactionId = GetUniqueFactionId(ownerFaction);
            OwnerFactionName = ownerFaction.Name;
            LeaderName = ownerFaction.leader?.Name?.ToStringFull ?? "Unknown";
            LastUpdatedTick = Find.TickManager.TicksGame;
            LastDecayCheckTick = LastUpdatedTick;
            
            // 初始化对玩家的关系值
            PlayerRelationValues = new FactionRelationValues();
            
            // 不再预先初始化所有派系记忆，改为按需创建
            // InitializeFactionMemories(ownerFaction);
        }

        /// <summary>
        /// 初始化对所有其他派系的记忆
        /// </summary>
        private void InitializeFactionMemories(Faction ownerFaction)
        {
            var allFactions = Find.FactionManager.AllFactions;
            foreach (var faction in allFactions)
            {
                if (faction != ownerFaction && !faction.IsPlayer && !faction.defeated)
                {
                    FactionMemories.Add(new FactionMemoryEntry
                    {
                        FactionId = GetUniqueFactionId(faction),
                        FactionName = faction.Name,
                        FirstContactTick = Find.TickManager.TicksGame,
                        RelationHistory = new List<RelationSnapshot>()
                    });
                }
            }
        }

        /// <summary>
        /// 获取或创建对指定派系的记忆
        /// </summary>
        public FactionMemoryEntry GetOrCreateMemory(Faction targetFaction)
        {
            var factionId = GetUniqueFactionId(targetFaction);
            var memory = FactionMemories.Find(m => m.FactionId == factionId);
            
            if (memory == null)
            {
                memory = new FactionMemoryEntry
                {
                    FactionId = factionId,
                    FactionName = targetFaction.Name,
                    FirstContactTick = Find.TickManager.TicksGame,
                    RelationHistory = new List<RelationSnapshot>()
                };
                FactionMemories.Add(memory);
            }
            
            return memory;
        }

        /// <summary>
        /// 添加重要事件记忆
        /// </summary>
        public void AddSignificantEvent(SignificantEventType eventType, Faction involvedFaction, string description)
        {
            SignificantEvents.Add(new SignificantEventMemory
            {
                EventType = eventType,
                InvolvedFactionId = GetUniqueFactionId(involvedFaction),
                InvolvedFactionName = involvedFaction.Name,
                Description = description,
                OccurredTick = Find.TickManager.TicksGame,
                Timestamp = DateTime.Now.Ticks
            });
            
            LastUpdatedTick = Find.TickManager.TicksGame;
        }

        /// <summary>
        /// 从对话记录更新记忆
        /// </summary>
        public void UpdateFromDialogue(List<DialogueMessageData> messages)
        {
            foreach (var message in messages)
            {
                // 分析对话内容，提取关键信息
                AnalyzeDialogueMessage(message);
            }
            
            LastUpdatedTick = Find.TickManager.TicksGame;
        }

        /// <summary>
        /// 分析单条对话消息
        /// </summary>
        private void AnalyzeDialogueMessage(DialogueMessageData message)
        {
            // 检测对话中是否提到其他派系
            var allFactions = Find.FactionManager.AllFactions;
            foreach (var faction in allFactions)
            {
                if (message.message.Contains(faction.Name))
                {
                    var memory = GetOrCreateMemory(faction);
                    memory.LastMentionedTick = Find.TickManager.TicksGame;
                    memory.MentionCount++;
                    
                    // 根据上下文判断情感倾向
                    if (IsNegativeContext(message.message, faction.Name))
                    {
                        memory.NegativeInteractions++;
                    }
                    else if (IsPositiveContext(message.message, faction.Name))
                    {
                        memory.PositiveInteractions++;
                    }
                }
            }
        }

        /// <summary>
        /// 检测负面上下文
        /// </summary>
        private bool IsNegativeContext(string message, string factionName)
        {
            var negativeWords = new[] { "enemy", "attack", "war", "hostile", "threat", "destroy", "hate", "敌", "战争", "攻击", "威胁" };
            foreach (var word in negativeWords)
            {
                if (message.ToLower().Contains(word.ToLower()))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 检测正面上下文
        /// </summary>
        private bool IsPositiveContext(string message, string factionName)
        {
            var positiveWords = new[] { "ally", "friend", "peace", "trade", "help", "support", "友好", "和平", "贸易", "盟友", "帮助" };
            foreach (var word in positiveWords)
            {
                if (message.ToLower().Contains(word.ToLower()))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 更新对某派系的关系快照
        /// </summary>
        public void UpdateRelationSnapshot(Faction targetFaction)
        {
            var memory = GetOrCreateMemory(targetFaction);
            var currentRelation = targetFaction.RelationKindWith(Faction.OfPlayer);
            
            memory.RelationHistory.Add(new RelationSnapshot
            {
                Tick = Find.TickManager.TicksGame,
                Relation = currentRelation.ToString(),
                Goodwill = targetFaction.PlayerGoodwill
            });
            
            // 限制历史记录数量
            if (memory.RelationHistory.Count > 50)
            {
                memory.RelationHistory.RemoveAt(0);
            }
            
            LastUpdatedTick = Find.TickManager.TicksGame;
        }

        /// <summary>
        /// 获取唯一派系 ID（用于跨存档识别）
        /// </summary>
        private static string GetUniqueFactionId(Faction faction)
        {
            if (faction.def != null && !string.IsNullOrEmpty(faction.def.defName))
            {
                return $"{faction.def.defName}_{faction.loadID}";
            }
            return $"custom_{faction.loadID}";
        }

        /// <summary>
        /// 刷新领袖名称
        /// </summary>
        public void RefreshLeaderInfo()
        {
            var faction = Find.FactionManager.AllFactions.Where(f => GetUniqueFactionId(f) == OwnerFactionId).FirstOrDefault();
            if (faction != null)
            {
                LeaderName = faction.leader?.Name?.ToStringFull ?? "Unknown";
                OwnerFactionName = faction.Name;
            }
        }

        // ========== 关系值系统方法 ==========

        /// <summary>
        /// 【新增】获取对玩家的关系值（如果不存在则创建）
        /// </summary>
        public FactionRelationValues GetOrCreatePlayerRelations()
        {
            if (PlayerRelationValues == null)
            {
                PlayerRelationValues = new FactionRelationValues();
            }
            return PlayerRelationValues;
        }

        /// <summary>
        /// 【新增】从LLM响应更新关系值
        /// </summary>
        public void UpdateRelationsFromLLMResponse(LLMRelationResponse response)
        {
            if (response == null || !response.IsValid)
                return;

            var relations = GetOrCreatePlayerRelations();
            response.ApplyTo(relations);
            relations.RecordDialogue();
            
            LastUpdatedTick = Find.TickManager.TicksGame;
        }

        /// <summary>
        /// 【新增】执行关系值衰减检查
        /// </summary>
        public void CheckAndApplyDecay()
        {
            int currentTick = Find.TickManager.TicksGame;
            
            // 检查是否到达衰减检查间隔
            if (currentTick - LastDecayCheckTick < FactionRelationValues.DecayCheckInterval)
                return;

            var relations = GetOrCreatePlayerRelations();
            relations.ApplyDecay();
            
            LastDecayCheckTick = currentTick;
            LastUpdatedTick = currentTick;
        }

        /// <summary>
        /// 【新增】记录对话互动（防止衰减）
        /// </summary>
        public void RecordPlayerDialogue()
        {
            var relations = GetOrCreatePlayerRelations();
            relations.RecordDialogue();
            LastUpdatedTick = Find.TickManager.TicksGame;
        }

        /// <summary>
        /// 【新增】获取关系值摘要（用于调试）
        /// </summary>
        public string GetPlayerRelationSummary()
        {
            var relations = GetOrCreatePlayerRelations();
            return relations.GetSummary();
        }

        /// <summary>
        /// 【新增】检查是否允许特定行为（基于关系阈值）
        /// </summary>
        public bool IsBehaviorAllowed(RelationBehaviorType behaviorType)
        {
            return RelationThresholdBehavior.IsBehaviorAllowed(
                GetOrCreatePlayerRelations(), 
                behaviorType
            );
        }

        public void UpsertRpgDepartSummary(CrossChannelSummaryRecord record, int maxEntries)
        {
            UpsertSummary(RpgDepartSummaries, record, maxEntries);
        }

        public void UpsertDiplomacySessionSummary(CrossChannelSummaryRecord record, int maxEntries)
        {
            UpsertSummary(DiplomacySessionSummaries, record, maxEntries);
        }

        private static void UpsertSummary(List<CrossChannelSummaryRecord> pool, CrossChannelSummaryRecord record, int maxEntries)
        {
            if (pool == null || record == null || string.IsNullOrWhiteSpace(record.SummaryText))
            {
                return;
            }

            int existingIndex = pool.FindIndex(x =>
                x != null &&
                !string.IsNullOrWhiteSpace(x.ContentHash) &&
                string.Equals(x.ContentHash, record.ContentHash, StringComparison.Ordinal));

            if (existingIndex >= 0)
            {
                pool[existingIndex] = record;
            }
            else
            {
                pool.Add(record);
            }

            pool.Sort((a, b) =>
            {
                if (a == null && b == null) return 0;
                if (a == null) return 1;
                if (b == null) return -1;
                return b.GameTick.CompareTo(a.GameTick);
            });

            int cap = Math.Max(1, maxEntries);
            if (pool.Count > cap)
            {
                pool.RemoveRange(cap, pool.Count - cap);
            }
        }

        /// <summary>
        /// 【新增】序列化数据
        /// </summary>
        public void ExposeData()
        {
            string ownerFactionId = OwnerFactionId;
            string ownerFactionName = OwnerFactionName;
            string leaderName = LeaderName;
            int lastUpdatedTick = LastUpdatedTick;
            int lastDecayCheckTick = LastDecayCheckTick;
            long createdTimestamp = CreatedTimestamp;
            long lastSavedTimestamp = LastSavedTimestamp;
            
            Scribe_Values.Look(ref ownerFactionId, "ownerFactionId", "");
            Scribe_Values.Look(ref ownerFactionName, "ownerFactionName", "");
            Scribe_Values.Look(ref leaderName, "leaderName", "");
            Scribe_Values.Look(ref lastUpdatedTick, "lastUpdatedTick", 0);
            Scribe_Values.Look(ref lastDecayCheckTick, "lastDecayCheckTick", 0);
            Scribe_Values.Look(ref createdTimestamp, "createdTimestamp", 0);
            Scribe_Values.Look(ref lastSavedTimestamp, "lastSavedTimestamp", 0);
            
            OwnerFactionId = ownerFactionId;
            OwnerFactionName = ownerFactionName;
            LeaderName = leaderName;
            LastUpdatedTick = lastUpdatedTick;
            LastDecayCheckTick = lastDecayCheckTick;
            CreatedTimestamp = createdTimestamp;
            LastSavedTimestamp = lastSavedTimestamp;
            
            Scribe_Collections.Look(ref FactionMemories, "factionMemories", LookMode.Deep);
            Scribe_Collections.Look(ref SignificantEvents, "significantEvents", LookMode.Deep);
            Scribe_Collections.Look(ref DialogueHistory, "dialogueHistory", LookMode.Deep);
            Scribe_Collections.Look(ref RpgDepartSummaries, "rpgDepartSummaries", LookMode.Deep);
            Scribe_Collections.Look(ref DiplomacySessionSummaries, "diplomacySessionSummaries", LookMode.Deep);
            
            Scribe_Deep.Look(ref PlayerRelationValues, "playerRelationValues");
            
            if (PlayerRelationValues == null)
            {
                PlayerRelationValues = new FactionRelationValues();
            }
            if (RpgDepartSummaries == null)
            {
                RpgDepartSummaries = new List<CrossChannelSummaryRecord>();
            }
            if (DiplomacySessionSummaries == null)
            {
                DiplomacySessionSummaries = new List<CrossChannelSummaryRecord>();
            }
        }
    }

    /// <summary>
    /// 对单个派系的记忆条目
    /// </summary>
    public class FactionMemoryEntry
    {
        /// <summary>
        /// 派系唯一 ID
        /// </summary>
        public string FactionId { get; set; }
        
        /// <summary>
        /// 派系名称
        /// </summary>
        public string FactionName { get; set; }
        
        /// <summary>
        /// 首次接触时间 tick
        /// </summary>
        public int FirstContactTick { get; set; }
        
        /// <summary>
        /// 最后被提及的时间 tick
        /// </summary>
        public int LastMentionedTick { get; set; }
        
        /// <summary>
        /// 被提及的次数
        /// </summary>
        public int MentionCount { get; set; }
        
        /// <summary>
        /// 正面交互次数
        /// </summary>
        public int PositiveInteractions { get; set; }
        
        /// <summary>
        /// 负面交互次数
        /// </summary>
        public int NegativeInteractions { get; set; }
        
        /// <summary>
        /// 关系历史快照
        /// </summary>
        public List<RelationSnapshot> RelationHistory { get; set; } = new List<RelationSnapshot>();
    }

    /// <summary>
    /// 关系快照
    /// </summary>
    public class RelationSnapshot
    {
        /// <summary>
        /// 记录时间的 tick
        /// </summary>
        public int Tick { get; set; }
        
        /// <summary>
        /// 关系类型
        /// </summary>
        public string Relation { get; set; }
        
        /// <summary>
        /// 好感度值
        /// </summary>
        public int Goodwill { get; set; }
    }

    /// <summary>
    /// 重要事件记忆
    /// </summary>
    public class SignificantEventMemory
    {
        /// <summary>
        /// 事件类型
        /// </summary>
        public SignificantEventType EventType { get; set; }
        
        /// <summary>
        /// 涉及派系的 ID
        /// </summary>
        public string InvolvedFactionId { get; set; }
        
        /// <summary>
        /// 涉及派系的名称
        /// </summary>
        public string InvolvedFactionName { get; set; }
        
        /// <summary>
        /// 事件描述
        /// </summary>
        public string Description { get; set; }
        
        /// <summary>
        /// 事件发生的 tick
        /// </summary>
        public int OccurredTick { get; set; }
        
        /// <summary>
        /// 时间戳
        /// </summary>
        public long Timestamp { get; set; }
    }

    /// <summary>
    /// 重要事件类型
    /// </summary>
    public enum SignificantEventType
    {
        WarDeclared,      // 宣战
        PeaceMade,        // 议和
        TradeCaravan,     // 贸易商队
        GiftSent,         // 发送礼物
        AidRequested,     // 请求援助
        GoodwillChanged,  // 好感度重大变化
        AllianceFormed,   // 结盟
        Betrayal          // 背叛
    }

    /// <summary>
    /// 对话记录
    /// </summary>
    public class DialogueRecord
    {
        /// <summary>
        /// 是否是玩家（true=玩家，false=AI）
        /// </summary>
        public bool IsPlayer { get; set; }
        
        /// <summary>
        /// 对话内容
        /// </summary>
        public string Message { get; set; }
        
        /// <summary>
        /// 对话发生的游戏 tick
        /// </summary>
        public int GameTick { get; set; }
    }
}
