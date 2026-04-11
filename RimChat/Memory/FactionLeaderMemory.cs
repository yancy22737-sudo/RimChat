using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimChat.Memory
{
    /// <summary>/// factionleader的memory数据结构
 /// record该leader对其他所有faction的认知和交互历史
 /// 包含跨派系互动的持久化记忆
 ///</summary>
    public class FactionLeaderMemory : IExposable
    {
        /// <summary>/// leader所属faction的 ID
 ///</summary>
        public string OwnerFactionId { get; set; }
        
        /// <summary>/// leader所属faction的name
 ///</summary>
        public string OwnerFactionName { get; set; }
        
        /// <summary>/// leader的名字 (如果有)
 ///</summary>
        public string LeaderName { get; set; }
        
        /// <summary>/// 对其他faction的memory列表
 ///</summary>
        public List<FactionMemoryEntry> FactionMemories = new List<FactionMemoryEntry>();
        
        /// <summary>/// 重要eventmemory (宣战, 议和, 重大贸易等)
 ///</summary>
        public List<SignificantEventMemory> SignificantEvents = new List<SignificantEventMemory>();
        
        /// <summary>/// dialogue历史record
 ///</summary>
        public List<DialogueRecord> DialogueHistory = new List<DialogueRecord>();

        /// <summary>/// RPG channel: 非玩家faction Pawn 离图摘要池
 ///</summary>
        public List<CrossChannelSummaryRecord> RpgDepartSummaries = new List<CrossChannelSummaryRecord>();

        /// <summary>/// diplomacychannel: session结束摘要池
 ///</summary>
        public List<CrossChannelSummaryRecord> DiplomacySessionSummaries = new List<CrossChannelSummaryRecord>();
        
        /// <summary>/// 最后更新时间 tick
 ///</summary>
        public int LastUpdatedTick { get; set; }
        
        /// <summary>/// 创建时间戳
 ///</summary>
        public long CreatedTimestamp { get; set; }
        
        /// <summary>/// 最后save时间戳
 ///</summary>
        public long LastSavedTimestamp { get; set; }
        
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
            
            // 不再预先initialize所有factionmemory, 改为按需创建
            // InitializeFactionMemories(ownerFaction);
        }

        /// <summary>/// initialize对所有其他faction的memory
 ///</summary>
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

        /// <summary>/// get或创建对指定faction的memory
 ///</summary>
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

        /// <summary>/// 添加重要eventmemory
 ///</summary>
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

        /// <summary>/// 从dialoguerecord更新memory
 ///</summary>
        public void UpdateFromDialogue(List<DialogueMessageData> messages)
        {
            foreach (var message in messages)
            {
                // 分析dialoguecontents, 提取关键信息
                AnalyzeDialogueMessage(message);
            }
            
            LastUpdatedTick = Find.TickManager.TicksGame;
        }

        /// <summary>/// 分析单条dialoguemessage
 ///</summary>
        private void AnalyzeDialogueMessage(DialogueMessageData message)
        {
            // 检测dialogue中whether提到其他faction
            var allFactions = Find.FactionManager.AllFactions;
            foreach (var faction in allFactions)
            {
                if (message.message.Contains(faction.Name))
                {
                    var memory = GetOrCreateMemory(faction);
                    memory.LastMentionedTick = Find.TickManager.TicksGame;
                    memory.MentionCount++;
                    
                    // 根据context判断情感倾向
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

        /// <summary>/// 检测负面context
 ///</summary>
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

        /// <summary>/// 检测正面context
 ///</summary>
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

        /// <summary>/// 更新对某faction的relation快照
 ///</summary>
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
            
            // 限制历史record数量
            if (memory.RelationHistory.Count > 50)
            {
                memory.RelationHistory.RemoveAt(0);
            }
            
            LastUpdatedTick = Find.TickManager.TicksGame;
        }

        /// <summary>/// get唯一faction ID (used for跨存档识别)
 ///</summary>
        private static string GetUniqueFactionId(Faction faction)
        {
            if (faction.def != null && !string.IsNullOrEmpty(faction.def.defName))
            {
                return $"{faction.def.defName}_{faction.loadID}";
            }
            return $"custom_{faction.loadID}";
        }

        /// <summary>/// 刷新leadername
 ///</summary>
        public void RefreshLeaderInfo()
        {
            var faction = Find.FactionManager.AllFactions.Where(f => GetUniqueFactionId(f) == OwnerFactionId).FirstOrDefault();
            if (faction != null)
            {
                LeaderName = faction.leader?.Name?.ToStringFull ?? "Unknown";
                OwnerFactionName = faction.Name;
            }
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

        /// <summary>/// [新增]序列化数据
 ///</summary>
        public void ExposeData()
        {
            string ownerFactionId = OwnerFactionId;
            string ownerFactionName = OwnerFactionName;
            string leaderName = LeaderName;
            int lastUpdatedTick = LastUpdatedTick;
            long createdTimestamp = CreatedTimestamp;
            long lastSavedTimestamp = LastSavedTimestamp;
            
            Scribe_Values.Look(ref ownerFactionId, "ownerFactionId", "");
            Scribe_Values.Look(ref ownerFactionName, "ownerFactionName", "");
            Scribe_Values.Look(ref leaderName, "leaderName", "");
            Scribe_Values.Look(ref lastUpdatedTick, "lastUpdatedTick", 0);
            Scribe_Values.Look(ref createdTimestamp, "createdTimestamp", 0);
            Scribe_Values.Look(ref lastSavedTimestamp, "lastSavedTimestamp", 0);
            
            OwnerFactionId = ownerFactionId;
            OwnerFactionName = ownerFactionName;
            LeaderName = leaderName;
            LastUpdatedTick = lastUpdatedTick;
            CreatedTimestamp = createdTimestamp;
            LastSavedTimestamp = lastSavedTimestamp;
            
            Scribe_Collections.Look(ref FactionMemories, "factionMemories", LookMode.Deep);
            Scribe_Collections.Look(ref SignificantEvents, "significantEvents", LookMode.Deep);
            Scribe_Collections.Look(ref DialogueHistory, "dialogueHistory", LookMode.Deep);
            Scribe_Collections.Look(ref RpgDepartSummaries, "rpgDepartSummaries", LookMode.Deep);
            Scribe_Collections.Look(ref DiplomacySessionSummaries, "diplomacySessionSummaries", LookMode.Deep);

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

    /// <summary>/// 对单个faction的memoryentry
 ///</summary>
    public class FactionMemoryEntry : IExposable
    {
        public string FactionId = "";
        public string FactionName = "";
        public int FirstContactTick = 0;
        public int LastMentionedTick = 0;
        public int MentionCount = 0;
        public int PositiveInteractions = 0;
        public int NegativeInteractions = 0;
        public List<RelationSnapshot> RelationHistory = new List<RelationSnapshot>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref FactionId, "factionId", "");
            Scribe_Values.Look(ref FactionName, "factionName", "");
            Scribe_Values.Look(ref FirstContactTick, "firstContactTick", 0);
            Scribe_Values.Look(ref LastMentionedTick, "lastMentionedTick", 0);
            Scribe_Values.Look(ref MentionCount, "mentionCount", 0);
            Scribe_Values.Look(ref PositiveInteractions, "positiveInteractions", 0);
            Scribe_Values.Look(ref NegativeInteractions, "negativeInteractions", 0);
            Scribe_Collections.Look(ref RelationHistory, "relationHistory", LookMode.Deep);
            if (RelationHistory == null)
            {
                RelationHistory = new List<RelationSnapshot>();
            }
        }
    }

    /// <summary>/// relation快照
 ///</summary>
    public class RelationSnapshot : IExposable
    {
        public int Tick = 0;
        public string Relation = "";
        public int Goodwill = 0;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Tick, "tick", 0);
            Scribe_Values.Look(ref Relation, "relation", "");
            Scribe_Values.Look(ref Goodwill, "goodwill", 0);
        }
    }

    /// <summary>/// 重要eventmemory
 ///</summary>
    public class SignificantEventMemory : IExposable
    {
        public SignificantEventType EventType = SignificantEventType.GoodwillChanged;
        public string InvolvedFactionId = "";
        public string InvolvedFactionName = "";
        public string Description = "";
        public int OccurredTick = 0;
        public long Timestamp = 0L;

        public void ExposeData()
        {
            Scribe_Values.Look(ref EventType, "eventType", SignificantEventType.GoodwillChanged);
            Scribe_Values.Look(ref InvolvedFactionId, "involvedFactionId", "");
            Scribe_Values.Look(ref InvolvedFactionName, "involvedFactionName", "");
            Scribe_Values.Look(ref Description, "description", "");
            Scribe_Values.Look(ref OccurredTick, "occurredTick", 0);
            Scribe_Values.Look(ref Timestamp, "timestamp", 0L);
        }
    }

    /// <summary>/// 重要event类型
 ///</summary>
    public enum SignificantEventType
    {
        WarDeclared,      // 宣战
        PeaceMade,        // 议和
        TradeCaravan,     // 贸易商队
        GiftSent,         // 发送礼物
        AidRequested,     // Request援助
        QuestIssued,      // 发布任务
        GoodwillChanged,  // Goodwill重大变化
        AllianceFormed,   // 结盟
        Betrayal          // 背叛
    }

    /// <summary>/// dialoguerecord
 ///</summary>
    public class DialogueRecord : IExposable
    {
        public bool IsPlayer = false;
        public string Message = "";
        public int GameTick = 0;

        public void ExposeData()
        {
            Scribe_Values.Look(ref IsPlayer, "isPlayer", false);
            Scribe_Values.Look(ref Message, "message", "");
            Scribe_Values.Look(ref GameTick, "gameTick", 0);
        }
    }
}
