using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimDiplomacy
{
    public class GameComponent_DiplomacyManager : GameComponent
    {
        private HashSet<Faction> aiControlledFactions = new HashSet<Faction>();
        private Dictionary<Faction, int> lastPlayerProvokeTick = new Dictionary<Faction, int>();
        private List<FactionDialogueSession> dialogueSessions = new List<FactionDialogueSession>();
        private int lastThreatTick = 0;
        private float threatBudget = 100f;

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
            // 清理无效的会话
            CleanupInvalidSessions();
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

        private int lastDailyResetTick = 0;

        public override void GameComponentTick()
        {
            int currentTick = Find.TickManager.TicksGame;

            // 每 60 ticks (约 1 秒) 检查一次
            if (currentTick % 60 == 0)
            {
                CheckGoodwillDrivenEvents();
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

        private void DailyReset()
        {
            // 重置 GameAIInterface 的每日限制
            GameAIInterface.Instance?.DailyReset();

            Log.Message("[RimDiplomacy] Daily reset completed.");
        }

        private void CheckGoodwillDrivenEvents()
        {
            if (RimDiplomacyMod.Instance == null || RimDiplomacyMod.Instance.InstanceSettings == null || !RimDiplomacyMod.Instance.InstanceSettings.EnableAISupplementRaid)
                return;

            foreach (var faction in aiControlledFactions)
            {
                if (faction.defeated)
                    continue;

                int goodwill = faction.PlayerGoodwill;
                int hostileThreshold = RimDiplomacyMod.Instance.InstanceSettings.GoodwillThresholdHostile;

                // 检查是否达到敌对阈值
                if (goodwill <= hostileThreshold)
                {
                    TryTriggerHostileEvent(faction);
                }
            }
        }

        private void TryTriggerHostileEvent(Faction faction)
        {
            int currentTick = Find.TickManager.TicksGame;
            int cooldownTicks = RimDiplomacyMod.Instance.InstanceSettings.ThreatCooldownDays * 60000;

            // 检查冷却期
            if (currentTick - lastThreatTick < cooldownTicks)
                return;

            // 检查是否已经触发过
            if (lastPlayerProvokeTick.TryGetValue(faction, out int lastProvokeTick))
            {
                int provokeCooldown = RimDiplomacyMod.Instance.InstanceSettings.PlayerProvokeCooldownHours * 2500;
                if (currentTick - lastProvokeTick < provokeCooldown)
                    return;
            }

            // 触发袭击
            if (Rand.Chance(0.3f)) // 30% 概率触发
            {
                TriggerFactionRaid(faction);
                lastThreatTick = currentTick;
                lastPlayerProvokeTick[faction] = currentTick;
            }
        }

        private void TriggerFactionRaid(Faction faction)
        {
            var map = Find.AnyPlayerHomeMap;
            if (map == null) return;

            float points = StorytellerUtility.DefaultThreatPointsNow(map);
            points = Mathf.Clamp(points, 100f, 10000f);

            var parms = new IncidentParms
            {
                target = map,
                faction = faction,
                points = points,
                raidStrategy = RaidStrategyDefOf.ImmediateAttack
            };

            var raidWorker = IncidentDefOf.RaidEnemy.Worker;
            bool success = raidWorker.TryExecute(parms);

            if (success)
            {
                Log.Message("[RimDiplomacy] AI faction " + faction.Name + " launched a raid against player.");
                
                // 发送通知
                Find.LetterStack.ReceiveLetter(
                    "AI Faction Raid",
                    faction.Name + " has launched a raid against your colony due to poor relations.",
                    LetterDefOf.ThreatBig
                );
            }
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

        public void RegisterPlayerProvoke(Faction faction)
        {
            if (!IsAIControlled(faction))
                return;

            lastPlayerProvokeTick[faction] = Find.TickManager.TicksGame;
            
            // 立即尝试触发事件
            TryTriggerHostileEvent(faction);
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref aiControlledFactions, "aiControlledFactions", LookMode.Reference);
            
            // 修复 Dictionary 序列化问题 - 使用工作列表
            List<Faction> provokeTickKeys = null;
            List<int> provokeTickValues = null;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                provokeTickKeys = lastPlayerProvokeTick.Keys.ToList();
                provokeTickValues = lastPlayerProvokeTick.Values.ToList();
            }
            Scribe_Collections.Look(ref provokeTickKeys, "lastPlayerProvokeTickKeys", LookMode.Reference);
            Scribe_Collections.Look(ref provokeTickValues, "lastPlayerProvokeTickValues", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                lastPlayerProvokeTick = new Dictionary<Faction, int>();
                if (provokeTickKeys != null && provokeTickValues != null)
                {
                    for (int i = 0; i < provokeTickKeys.Count; i++)
                    {
                        lastPlayerProvokeTick[provokeTickKeys[i]] = provokeTickValues[i];
                    }
                }
            }
            
            Scribe_Collections.Look(ref dialogueSessions, "dialogueSessions", LookMode.Deep);
            Scribe_Values.Look(ref lastThreatTick, "lastThreatTick", 0);
            Scribe_Values.Look(ref threatBudget, "threatBudget", 100f);
            Scribe_Values.Look(ref lastDailyResetTick, "lastDailyResetTick", 0);

            // 保存/加载 GameAIInterface 数据
            GameAIInterface.Instance?.ExposeData();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (aiControlledFactions == null)
                    aiControlledFactions = new HashSet<Faction>();
                if (dialogueSessions == null)
                    dialogueSessions = new List<FactionDialogueSession>();
                if (lastPlayerProvokeTick == null)
                    lastPlayerProvokeTick = new Dictionary<Faction, int>();
            }
        }
    }
}
