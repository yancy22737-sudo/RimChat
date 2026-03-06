using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using RimDiplomacy.Memory;
using RimDiplomacy.Relation;
using RimDiplomacy.Util;
using RimDiplomacy.Core;
using RimDiplomacy.Config;

namespace RimDiplomacy.DiplomacySystem
{
    public class GameComponent_DiplomacyManager : GameComponent
    {
        private HashSet<Faction> aiControlledFactions = new HashSet<Faction>();
        private List<FactionDialogueSession> dialogueSessions = new List<FactionDialogueSession>();
        private Dictionary<Faction, FactionRelationValues> factionRelationValues = new Dictionary<Faction, FactionRelationValues>();
        private List<DelayedDiplomacyEvent> delayedEvents = new List<DelayedDiplomacyEvent>();

        public static GameComponent_DiplomacyManager Instance = null;

        public GameComponent_DiplomacyManager(Game game)
        {
            Instance = this;
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            InitializeAIControlledFactions();
            InitializeDialogueSessions();
            InitializeFactionRelationValues();
            // 初始化领袖记忆系统
            LeaderMemoryManager.Instance.OnNewGame();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            if (aiControlledFactions == null)
            {
                aiControlledFactions = new HashSet<Faction>();
                InitializeAIControlledFactions();
            }
            if (dialogueSessions == null)
            {
                dialogueSessions = new List<FactionDialogueSession>();
                InitializeDialogueSessions();
            }
            // 初始化五维关系值（如果是旧存档）
            InitializeFactionRelationValues();
            // 清理无效的会话
            CleanupInvalidSessions();
            // 加载领袖记忆系统
            LeaderMemoryManager.Instance.OnLoadedGame();
        }

        private void InitializeAIControlledFactions()
        {
            var allFactions = Find.FactionManager.AllFactions
                .Where(f => !f.IsPlayer && !f.defeated && !f.def.hidden)
                .ToList();

            foreach (var faction in allFactions)
            {
                aiControlledFactions.Add(faction);
            }
        }

        private void InitializeDialogueSessions()
        {
            var allFactions = Find.FactionManager.AllFactions
                .Where(f => !f.IsPlayer && !f.defeated && !f.def.hidden)
                .ToList();

            foreach (var faction in allFactions)
            {
                GetOrCreateSession(faction);
            }
        }

        /// <summary>
        /// 初始化所有派系的五维关系值（从派系好感度）
        /// </summary>
        private void InitializeFactionRelationValues()
        {
            var allFactions = Find.FactionManager.AllFactions
                .Where(f => !f.IsPlayer && !f.defeated && !f.def.hidden)
                .ToList();

            foreach (var faction in allFactions)
            {
                if (!factionRelationValues.ContainsKey(faction))
                {
                    var relations = new FactionRelationValues();
                    relations.InitializeFromGoodwill(faction.PlayerGoodwill);
                    factionRelationValues[faction] = relations;
                    Log.Message($"[RimDiplomacy] Initialized relation values for {faction.Name} from goodwill {faction.PlayerGoodwill}: {relations.GetSummary()}");
                }
            }
        }

        private void CleanupInvalidSessions()
        {
            dialogueSessions.RemoveAll(s => s.faction == null || s.faction.defeated);
        }

        /// <summary>
        /// 获取或创建指定派系的对话会话
        /// </summary>
        public FactionDialogueSession GetOrCreateSession(Faction faction)
        {
            if (faction == null) return null;

            var session = dialogueSessions.FirstOrDefault(s => s.faction == faction);
            if (session == null)
            {
                session = new FactionDialogueSession(faction);
                dialogueSessions.Add(session);
                Log.Message($"[RimDiplomacy] Created dialogue session for {faction.Name}");
            }
            return session;
        }

        /// <summary>
        /// 获取指定派系的对话会话（如果不存在则返回null）
        /// </summary>
        public FactionDialogueSession GetSession(Faction faction)
        {
            if (faction == null) return null;
            return dialogueSessions.FirstOrDefault(s => s.faction == faction);
        }

        /// <summary>
        /// 检查派系是否有未读消息
        /// </summary>
        public bool HasUnreadMessages(Faction faction)
        {
            var session = GetSession(faction);
            return session?.hasUnreadMessages ?? false;
        }

        /// <summary>
        /// 获取所有有对话记录的派系
        /// </summary>
        public List<Faction> GetFactionsWithDialogue()
        {
            return dialogueSessions
                .Where(s => s.faction != null && !s.faction.defeated && s.messages.Count > 0)
                .Select(s => s.faction)
                .ToList();
        }

        /// <summary>
        /// 获取或创建指定派系的五维关系值
        /// </summary>
        public FactionRelationValues GetOrCreateRelationValues(Faction faction)
        {
            if (faction == null) return null;

            if (!factionRelationValues.TryGetValue(faction, out var relations))
            {
                relations = new FactionRelationValues();
                factionRelationValues[faction] = relations;
                Log.Message($"[RimDiplomacy] Created relation values for {faction.Name}");
            }
            return relations;
        }

        /// <summary>
        /// 获取指定派系的五维关系值（如果不存在则返回null）
        /// </summary>
        public FactionRelationValues GetRelationValues(Faction faction)
        {
            if (faction == null) return null;
            factionRelationValues.TryGetValue(faction, out var relations);
            return relations;
        }

        /// <summary>
        /// 更新派系的五维关系值
        /// </summary>
        public void UpdateRelationValues(Faction faction, float trustDelta, float intimacyDelta, float reciprocityDelta, float respectDelta, float influenceDelta, string reason = "")
        {
            var relations = GetOrCreateRelationValues(faction);
            if (relations == null) return;

            relations.UpdateFromLLMResponse(trustDelta, intimacyDelta, reciprocityDelta, respectDelta, influenceDelta);

            Log.Message($"[RimDiplomacy] Updated relation values for {faction.Name}: " +
                       $"Trust{trustDelta:F1}, Intimacy{intimacyDelta:F1}, Reciprocity{reciprocityDelta:F1}, " +
                       $"Respect{respectDelta:F1}, Influence{influenceDelta:F1}. Reason: {reason}");
        }

        private int lastDailyResetTick = 0;

        public override void GameComponentTick()
        {
            int currentTick = Find.TickManager.TicksGame;

            // 每 60 ticks (约 1 秒) 检查一次
            if (currentTick % 60 == 0)
            {
                ProcessDelayedEvents();
            }

            // 每 2000 ticks (约 33 秒) 检查一次 AI 决策
            if (currentTick % 2000 == 0)
            {
                ProcessAIDecisions();
            }

            // 每日重置 (60000 ticks = 1 天)
            if (currentTick - lastDailyResetTick >= 60000)
            {
                DailyReset();
                lastDailyResetTick = currentTick;
            }
        }

        private void ProcessDelayedEvents()
        {
            if (delayedEvents == null) return;

            List<DelayedDiplomacyEvent> eventsToExecute = new List<DelayedDiplomacyEvent>();
            List<DelayedDiplomacyEvent> eventsToRemove = new List<DelayedDiplomacyEvent>();

            foreach (var evt in delayedEvents)
            {
                if (evt.ShouldExecute())
                {
                    eventsToExecute.Add(evt);
                    eventsToRemove.Add(evt);
                }
                else if (evt.Faction == null || evt.Faction.defeated)
                {
                    eventsToRemove.Add(evt);
                }
            }

            foreach (var evt in eventsToRemove)
            {
                delayedEvents.Remove(evt);
            }

            foreach (var evt in eventsToExecute)
            {
                evt.Execute();
            }
        }

        public void AddDelayedEvent(DelayedDiplomacyEvent evt)
        {
            if (delayedEvents == null)
                delayedEvents = new List<DelayedDiplomacyEvent>();

            delayedEvents.Add(evt);
            Log.Message($"[RimDiplomacy] Scheduled delayed {evt.EventType} from {evt.Faction?.Name} at tick {evt.ExecuteTick}");
        }

        private void DailyReset()
        {
            // 重置 GameAIInterface 的每日限制
            GameAIInterface.Instance?.DailyReset();

            Log.Message("[RimDiplomacy] Daily reset completed.");
        }

        private void ProcessAIDecisions()
        {
            // 这里将调用 AI API 进行决策
            // 暂时留空，等待 AI 客户端实现
        }

        public bool IsAIControlled(Faction faction)
        {
            return aiControlledFactions.Contains(faction);
        }

        public override void ExposeData()
        {
            base.ExposeData();

            // 游戏保存时保存所有领袖记忆到文件
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                LeaderMemoryManager.Instance.OnBeforeGameSave();
            }

            Scribe_Collections.Look(ref aiControlledFactions, "aiControlledFactions", LookMode.Reference);
            
            Scribe_Collections.Look(ref dialogueSessions, "dialogueSessions", LookMode.Deep);
            Scribe_Collections.Look(ref delayedEvents, "delayedEvents", LookMode.Deep);
            Scribe_Values.Look(ref lastDailyResetTick, "lastDailyResetTick", 0);

            // 序列化五维关系值
            ExposeFactionRelationValues();

            // 保存/加载 GameAIInterface 数据
            GameAIInterface.Instance?.ExposeData();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (aiControlledFactions == null)
                    aiControlledFactions = new HashSet<Faction>();
                if (dialogueSessions == null)
                    dialogueSessions = new List<FactionDialogueSession>();
                if (delayedEvents == null)
                    delayedEvents = new List<DelayedDiplomacyEvent>();
                if (factionRelationValues == null)
                    factionRelationValues = new Dictionary<Faction, FactionRelationValues>();

                // 修复延迟事件的 ExecuteTick：防止存档加载后出现不合理的长延迟
                if (delayedEvents != null && Find.TickManager != null)
                {
                    int currentTick = Find.TickManager.TicksGame;
                    int baseAidDelay = RimDiplomacyMod.Instance?.InstanceSettings?.AidDelayBaseTicks ?? 90000;
                    int baseCaravanDelay = RimDiplomacyMod.Instance?.InstanceSettings?.CaravanDelayBaseTicks ?? 135000;
                    
                    foreach (var evt in delayedEvents)
                    {
                        int baseDelay = evt.EventType == DelayedEventType.Aid ? baseAidDelay : baseCaravanDelay;
                        
                        if (evt.ExecuteTick <= currentTick)
                        {
                            int minDelay = (int)(baseDelay * 0.2f);
                            int maxDelay = baseDelay;
                            evt.ExecuteTick = currentTick + Rand.Range(minDelay, maxDelay);
                            Log.Message($"[RimDiplomacy] Adjusted delayed {evt.EventType} from {evt.Faction?.Name}: tick was in past, new tick={evt.ExecuteTick}");
                        }
                        else if (evt.ExecuteTick - currentTick > baseDelay * 2)
                        {
                            evt.ExecuteTick = currentTick + Rand.Range(baseDelay, baseDelay * 2);
                            Log.Message($"[RimDiplomacy] Adjusted delayed {evt.EventType} from {evt.Faction?.Name}: delay was too long, new tick={evt.ExecuteTick}");
                        }
                    }
                }
            }

            // 游戏加载完成后，从文件加载记忆数据
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                LeaderMemoryManager.Instance.OnAfterGameLoad();
            }
        }

        /// <summary>
        /// 序列化五维关系值
        /// </summary>
        private void ExposeFactionRelationValues()
        {
            // 使用列表来序列化字典
            List<Faction> relationKeys = null;
            List<FactionRelationValues> relationValues = null;

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                relationKeys = factionRelationValues.Keys.ToList();
                relationValues = factionRelationValues.Values.ToList();
            }

            Scribe_Collections.Look(ref relationKeys, "factionRelationKeys", LookMode.Reference);
            Scribe_Collections.Look(ref relationValues, "factionRelationValues", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                factionRelationValues = new Dictionary<Faction, FactionRelationValues>();
                if (relationKeys != null && relationValues != null)
                {
                    for (int i = 0; i < relationKeys.Count && i < relationValues.Count; i++)
                    {
                        if (relationKeys[i] != null)
                        {
                            factionRelationValues[relationKeys[i]] = relationValues[i];
                        }
                    }
                }
            }
        }
    }
}
