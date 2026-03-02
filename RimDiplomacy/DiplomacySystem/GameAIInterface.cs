using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using RimDiplomacy.Relation;
using RimDiplomacy.AI;
using RimDiplomacy.Config;
using RimDiplomacy.Util;
using RimDiplomacy.Core;
using RimDiplomacy.Persistence;
using RimDiplomacy.Memory;

namespace RimDiplomacy.DiplomacySystem
{
    /// <summary>
    /// AI与游戏交互的主接口类
    /// 提供基于对话内容的游戏状态管理功能
    /// </summary>
    public class GameAIInterface : IExposable
    {
        #region 单例与初始化

        private static GameAIInterface _instance;
        public static GameAIInterface Instance => _instance ??= new GameAIInterface();

        private GameAIInterface()
        {
            EnsureInitialized();
            InitializeCooldowns();
        }

        public void ExposeData()
        {
            EnsureInitialized();
            
            Scribe_Values.Look(ref _lastResetTick, "lastResetTick", 0);
            Scribe_Collections.Look(ref _apiCallHistory, "apiCallHistory", LookMode.Deep);
            
            // 序列化好感度调整记录
            ExposeGoodwillAdjustments();
            
            // 序列化派系独立冷却数据
            ExposeFactionCooldowns();
        }

        /// <summary>
        /// 序列化好感度调整记录
        /// </summary>
        private void ExposeGoodwillAdjustments()
        {
            List<Faction> goodwillKeys = null;
            List<int> goodwillValues = null;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                goodwillKeys = _goodwillAdjustmentsToday.Keys.ToList();
                goodwillValues = _goodwillAdjustmentsToday.Values.ToList();
            }
            Scribe_Collections.Look(ref goodwillKeys, "goodwillAdjustmentsTodayKeys", LookMode.Reference);
            Scribe_Collections.Look(ref goodwillValues, "goodwillAdjustmentsTodayValues", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                _goodwillAdjustmentsToday = new Dictionary<Faction, int>();
                if (goodwillKeys != null && goodwillValues != null)
                {
                    for (int i = 0; i < goodwillKeys.Count; i++)
                    {
                        _goodwillAdjustmentsToday[goodwillKeys[i]] = goodwillValues[i];
                    }
                }
            }
        }

        /// <summary>
        /// 序列化派系独立冷却数据
        /// 结构：Dictionary<Faction, Dictionary<string, int>>
        /// </summary>
        private void ExposeFactionCooldowns()
        {
            // 使用列表来序列化嵌套字典
            List<FactionCooldownEntry> cooldownEntries = null;
            
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                cooldownEntries = new List<FactionCooldownEntry>();
                foreach (var factionKvp in _factionCooldowns)
                {
                    if (factionKvp.Key == null) continue;
                    
                    var entry = new FactionCooldownEntry
                    {
                        Faction = factionKvp.Key,
                        MethodCooldowns = factionKvp.Value?.ToList() ?? new List<KeyValuePair<string, int>>()
                    };
                    cooldownEntries.Add(entry);
                }
            }
            
            Scribe_Collections.Look(ref cooldownEntries, "factionCooldownEntries", LookMode.Deep);
            
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                _factionCooldowns = new Dictionary<Faction, Dictionary<string, int>>();
                if (cooldownEntries != null)
                {
                    foreach (var entry in cooldownEntries)
                    {
                        if (entry.Faction == null) continue;
                        
                        var cooldownDict = new Dictionary<string, int>();
                        foreach (var methodKvp in entry.MethodCooldowns)
                        {
                            cooldownDict[methodKvp.Key] = methodKvp.Value;
                        }
                        _factionCooldowns[entry.Faction] = cooldownDict;
                    }
                }
            }
        }

        #endregion

        #region 数据结构

        /// <summary>
        /// API 调用记录
        /// </summary>
        private List<APICallRecord> _apiCallHistory;

        /// <summary>
        /// 今日好感度调整记录 (派系 -> 调整值)
        /// </summary>
        private Dictionary<Faction, int> _goodwillAdjustmentsToday;

        /// <summary>
        /// 派系独立冷却时间 (派系 -> (方法名 -> 下次可用 tick))
        /// </summary>
        private Dictionary<Faction, Dictionary<string, int>> _factionCooldowns;

        /// <summary>
        /// 对话行为冷却时间 (行为类型 -> 派系 -> 下次可用 tick)
        /// </summary>
        private Dictionary<DialogueGoodwillCost.DialogueActionType, Dictionary<Faction, int>> _dialogueActionCooldowns;

        /// <summary>
        /// 对话行为记录（用于每日限制）
        /// </summary>
        private List<DialogueActionRecord> _dialogueActionRecords;

        /// <summary>
        /// 上次重置 tick
        /// </summary>
        private int _lastResetTick = 0;

        /// <summary>
        /// 初始化所有字段
        /// </summary>
        private void EnsureInitialized()
        {
            if (_apiCallHistory == null)
                _apiCallHistory = new List<APICallRecord>();
            if (_goodwillAdjustmentsToday == null)
                _goodwillAdjustmentsToday = new Dictionary<Faction, int>();
            if (_factionCooldowns == null)
                _factionCooldowns = new Dictionary<Faction, Dictionary<string, int>>();
            if (_dialogueActionCooldowns == null)
                _dialogueActionCooldowns = new Dictionary<DialogueGoodwillCost.DialogueActionType, Dictionary<Faction, int>>();
            if (_dialogueActionRecords == null)
                _dialogueActionRecords = new List<DialogueActionRecord>();
        }

        /// <summary>
        /// API调用记录结构
        /// </summary>
        public class APICallRecord : IExposable
        {
            public string MethodName;
            public int TickCalled;
            public string Parameters;
            public bool Success;
            public string ErrorMessage;

            public void ExposeData()
            {
                Scribe_Values.Look(ref MethodName, "methodName", "");
                Scribe_Values.Look(ref TickCalled, "tickCalled", 0);
                Scribe_Values.Look(ref Parameters, "parameters", "");
                Scribe_Values.Look(ref Success, "success", false);
                Scribe_Values.Look(ref ErrorMessage, "errorMessage", "");
            }
        }

        /// <summary>
        /// API调用结果
        /// </summary>
        public class APIResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public object Data { get; set; }

            public static APIResult SuccessResult(string message = "", object data = null)
            {
                return new APIResult { Success = true, Message = message, Data = data };
            }

            public static APIResult FailureResult(string message)
            {
                return new APIResult { Success = false, Message = message };
            }
        }

        /// <summary>
        /// 派系冷却条目（用于序列化）
        /// </summary>
        public class FactionCooldownEntry : IExposable
        {
            public Faction Faction;
            public List<KeyValuePair<string, int>> MethodCooldowns;

            public void ExposeData()
            {
                Scribe_References.Look(ref Faction, "faction");
                
                // 序列化方法冷却列表
                List<string> methodNames = null;
                List<int> cooldownTicks = null;
                
                if (Scribe.mode == LoadSaveMode.Saving)
                {
                    methodNames = MethodCooldowns?.Select(x => x.Key).ToList() ?? new List<string>();
                    cooldownTicks = MethodCooldowns?.Select(x => x.Value).ToList() ?? new List<int>();
                }
                
                Scribe_Collections.Look(ref methodNames, "methodNames", LookMode.Value);
                Scribe_Collections.Look(ref cooldownTicks, "cooldownTicks", LookMode.Value);
                
                if (Scribe.mode == LoadSaveMode.LoadingVars)
                {
                    MethodCooldowns = new List<KeyValuePair<string, int>>();
                    if (methodNames != null && cooldownTicks != null)
                    {
                        for (int i = 0; i < methodNames.Count && i < cooldownTicks.Count; i++)
                        {
                            MethodCooldowns.Add(new KeyValuePair<string, int>(methodNames[i], cooldownTicks[i]));
                        }
                    }
                }
            }
        }

        #endregion

        #region 初始化与重置

        private void InitializeCooldowns()
        {
            EnsureInitialized();
            
            _factionCooldowns.Clear();
        }

        /// <summary>
        /// 获取或创建派系的冷却字典
        /// </summary>
        private Dictionary<string, int> GetOrCreateFactionCooldowns(Faction faction)
        {
            EnsureInitialized();
            
            if (faction == null) return null;
            
            if (!_factionCooldowns.TryGetValue(faction, out var cooldowns))
            {
                cooldowns = new Dictionary<string, int>
                {
                    ["AdjustGoodwill"] = 0,
                    ["SendGift"] = 0,
                    ["RequestAid"] = 0,
                    ["DeclareWar"] = 0,
                    ["MakePeace"] = 0,
                    ["RequestTradeCaravan"] = 0
                };
                _factionCooldowns[faction] = cooldowns;
            }
            
            return cooldowns;
        }

        /// <summary>
        /// 每日重置（由 GameComponent 调用）
        /// </summary>
        public void DailyReset()
        {
            EnsureInitialized();
            _goodwillAdjustmentsToday.Clear();
            _dialogueActionRecords.Clear();
            CleanupOldRecords();
        }

        /// <summary>
        /// 清理过期的 API 调用记录
        /// </summary>
        private void CleanupOldRecords()
        {
            EnsureInitialized();
            
            if (Find.TickManager == null) return;
            
            int currentTick = Find.TickManager.TicksGame;
            int maxAgeTicks = 60000 * 7; // 保留 7 天的记录

            _apiCallHistory.RemoveAll(r => currentTick - r.TickCalled > maxAgeTicks);
        }

        #endregion

        #region 核心API方法 - 好感度管理

        /// <summary>
        /// 调整派系好感度
        /// </summary>
        /// <param name="faction">目标派系</param>
        /// <param name="amount">调整值 (-100 到 100)</param>
        /// <param name="reason">调整原因</param>
        /// <returns>API调用结果</returns>
        public APIResult AdjustGoodwill(Faction faction, int amount, string reason = "")
        {
            if (RimDiplomacyMod.Instance == null)
                return APIResult.FailureResult("Settings not initialized");
            var settings = RimDiplomacyMod.Instance.InstanceSettings;
            if (settings == null)
                return APIResult.FailureResult("Settings not initialized");

            // 参数验证
            if (faction == null)
                return APIResult.FailureResult("Faction cannot be null");

            if (faction.IsPlayer)
                return APIResult.FailureResult("Cannot adjust player faction goodwill");

            // 检查派系独立冷却
            int remainingCooldown = GetRemainingCooldownSeconds(faction, "AdjustGoodwill");
            if (remainingCooldown > 0)
                return APIResult.FailureResult($"Method AdjustGoodwill is on cooldown for {faction.Name}. Remaining: {remainingCooldown} seconds");

            // 检查单次调整上限
            int maxSingleAdjustment = settings.MaxGoodwillAdjustmentPerCall;
            if (Math.Abs(amount) > maxSingleAdjustment)
            {
                Log.Warning($"[RimDiplomacy] AI attempted to adjust goodwill by {amount}, clamped to {maxSingleAdjustment}");
                amount = Math.Sign(amount) * maxSingleAdjustment;
            }

            // 检查每日累计调整上限
            int currentDayAdjustment = _goodwillAdjustmentsToday.ContainsKey(faction) ? _goodwillAdjustmentsToday[faction] : 0;
            int maxDailyAdjustment = settings.MaxDailyGoodwillAdjustment;

            if (Math.Abs(currentDayAdjustment + amount) > maxDailyAdjustment)
            {
                int allowedAdjustment = maxDailyAdjustment - Math.Abs(currentDayAdjustment);
                allowedAdjustment = Math.Sign(amount) * Math.Max(0, Math.Abs(allowedAdjustment));

                if (allowedAdjustment == 0)
                    return APIResult.FailureResult($"Daily goodwill adjustment limit reached for {faction.Name}. Current: {currentDayAdjustment}, Limit: ±{maxDailyAdjustment}");

                Log.Warning($"[RimDiplomacy] AI goodwill adjustment clamped from {amount} to {allowedAdjustment} due to daily limit");
                amount = allowedAdjustment;
            }

            // 执行调整
            int oldGoodwill = faction.PlayerGoodwill;
            faction.TryAffectGoodwillWith(Faction.OfPlayer, amount, false, true, null);
            int newGoodwill = faction.PlayerGoodwill;
            int actualChange = newGoodwill - oldGoodwill;

            // 记录调整
            _goodwillAdjustmentsToday[faction] = currentDayAdjustment + actualChange;
            RecordAPICall("AdjustGoodwill", true, $"faction={faction.Name}, amount={actualChange}, reason={reason}");
            SetCooldown(faction, "AdjustGoodwill");

            // 触发事件通知
            if (Math.Abs(actualChange) >= 10)
            {
                NotifySignificantGoodwillChange(faction, oldGoodwill, newGoodwill, reason);
            }

            return APIResult.SuccessResult(
                $"Goodwill adjusted from {oldGoodwill} to {newGoodwill} (change: {actualChange})",
                new { OldGoodwill = oldGoodwill, NewGoodwill = newGoodwill, Change = actualChange }
            );
        }

        /// <summary>
        /// 获取当前好感度
        /// </summary>
        /// <param name="faction">目标派系</param>
        /// <returns>API调用结果，包含好感度数据</returns>
        public APIResult GetCurrentGoodwill(Faction faction)
        {
            if (faction == null)
                return APIResult.FailureResult("Faction cannot be null");

            int goodwill = faction.PlayerGoodwill;
            var relationKind = faction.RelationKindWith(Faction.OfPlayer);

            RecordAPICall("GetCurrentGoodwill", true, $"faction={faction.Name}");

            return APIResult.SuccessResult(
                $"Current goodwill with {faction.Name}: {goodwill}",
                new
                {
                    FactionName = faction.Name,
                    Goodwill = goodwill,
                    RelationKind = relationKind.ToString(),
                    IsHostile = relationKind == FactionRelationKind.Hostile,
                    IsAlly = relationKind == FactionRelationKind.Ally
                }
            );
        }

        /// <summary>
        /// 获取今日已调整的好感度值
        /// </summary>
        /// <param name="faction">目标派系</param>
        /// <returns>今日累计调整值</returns>
        public int GetTodayGoodwillAdjustment(Faction faction)
        {
            if (faction == null) return 0;
            return _goodwillAdjustmentsToday.ContainsKey(faction) ? _goodwillAdjustmentsToday[faction] : 0;
        }

        #endregion

        #region 核心API方法 - 外交操作

        /// <summary>
        /// 向派系发送礼物
        /// </summary>
        /// <param name="faction">目标派系</param>
        /// <param name="silverAmount">白银数量</param>
        /// <param name="goodwillGain">获得的好感度</param>
        /// <returns>API调用结果</returns>
        public APIResult SendGift(Faction faction, int silverAmount, int goodwillGain)
        {
            if (RimDiplomacyMod.Instance == null)
                return APIResult.FailureResult("Settings not initialized");
            var settings = RimDiplomacyMod.Instance.InstanceSettings;
            if (settings == null)
                return APIResult.FailureResult("Settings not initialized");

            if (faction == null)
                return APIResult.FailureResult("Faction cannot be null");

            // 检查派系独立冷却
            int remainingCooldown = GetRemainingCooldownSeconds(faction, "SendGift");
            if (remainingCooldown > 0)
                return APIResult.FailureResult($"Method SendGift is on cooldown for {faction.Name}. Remaining: {remainingCooldown} seconds");

            // 检查礼物上限
            if (silverAmount > settings.MaxGiftSilverAmount)
                return APIResult.FailureResult($"Gift amount {silverAmount} exceeds maximum {settings.MaxGiftSilverAmount}");

            // 检查好感度收益上限
            if (goodwillGain > settings.MaxGiftGoodwillGain)
                return APIResult.FailureResult($"Goodwill gain {goodwillGain} exceeds maximum {settings.MaxGiftGoodwillGain}");

            // 执行礼物发送（模拟）
            faction.TryAffectGoodwillWith(Faction.OfPlayer, goodwillGain, false, true, null);

            RecordAPICall("SendGift", true, $"faction={faction.Name}, silver={silverAmount}, goodwillGain={goodwillGain}");
            SetCooldown(faction, "SendGift");

            return APIResult.SuccessResult(
                $"Gift of {silverAmount} silver sent to {faction.Name}, gained {goodwillGain} goodwill",
                new { SilverAmount = silverAmount, GoodwillGain = goodwillGain }
            );
        }

        /// <summary>
        /// 请求派系援助
        /// </summary>
        /// <param name="faction">目标派系</param>
        /// <param name="aidType">援助类型 (Military, Medical, Resources)</param>
        /// <returns>API调用结果</returns>
        public APIResult RequestAid(Faction faction, string aidType)
        {
            if (RimDiplomacyMod.Instance == null)
                return APIResult.FailureResult("Settings not initialized");
            var settings = RimDiplomacyMod.Instance.InstanceSettings;
            if (settings == null)
                return APIResult.FailureResult("Settings not initialized");

            if (faction == null)
                return APIResult.FailureResult("Faction cannot be null");

            // 检查派系独立冷却
            int remainingCooldown = GetRemainingCooldownSeconds(faction, "RequestAid");
            if (remainingCooldown > 0)
                return APIResult.FailureResult($"Method RequestAid is on cooldown for {faction.Name}. Remaining: {remainingCooldown} seconds");

            // 检查关系是否允许请求援助
            if (faction.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Ally)
                return APIResult.FailureResult("Can only request aid from allied factions");

            // 检查好感度是否足够
            if (faction.PlayerGoodwill < settings.MinGoodwillForAid)
                return APIResult.FailureResult($"Need at least {settings.MinGoodwillForAid} goodwill to request aid");

            RecordAPICall("RequestAid", true, $"faction={faction.Name}, aidType={aidType}");
            SetCooldown(faction, "RequestAid");

            // 这里可以触发实际的援助事件
            return APIResult.SuccessResult(
                $"Aid request sent to {faction.Name} for {aidType}",
                new { AidType = aidType, Faction = faction.Name }
            );
        }

        /// <summary>
        /// 宣战
        /// </summary>
        /// <param name="faction">目标派系</param>
        /// <param name="reason">宣战原因</param>
        /// <returns>API调用结果</returns>
        public APIResult DeclareWar(Faction faction, string reason = "")
        {
            if (RimDiplomacyMod.Instance == null)
                return APIResult.FailureResult("Settings not initialized");
            var settings = RimDiplomacyMod.Instance.InstanceSettings;
            if (settings == null)
                return APIResult.FailureResult("Settings not initialized");

            if (faction == null)
                return APIResult.FailureResult("Faction cannot be null");

            // 检查派系独立冷却
            int remainingCooldown = GetRemainingCooldownSeconds(faction, "DeclareWar");
            if (remainingCooldown > 0)
                return APIResult.FailureResult($"Method DeclareWar is on cooldown for {faction.Name}. Remaining: {remainingCooldown} seconds");

            // 检查是否已经是敌对关系
            if (faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile)
                return APIResult.FailureResult("Already at war with this faction");

            // 检查好感度是否允许宣战
            if (faction.PlayerGoodwill > settings.MaxGoodwillForWarDeclaration)
                return APIResult.FailureResult($"Cannot declare war with goodwill above {settings.MaxGoodwillForWarDeclaration}");

            // 设置敌对关系
            faction.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile);

            RecordAPICall("DeclareWar", true, $"faction={faction.Name}, reason={reason}");
            SetCooldown(faction, "DeclareWar");

            // 发送通知
            Find.LetterStack.ReceiveLetter(
                "War Declared",
                $"{faction.Name} has declared war on your colony!\n\nReason: {reason}",
                LetterDefOf.ThreatBig
            );

            return APIResult.SuccessResult(
                $"War declared with {faction.Name}",
                new { Faction = faction.Name, Reason = reason }
            );
        }

        /// <summary>
        /// 议和
        /// </summary>
        /// <param name="faction">目标派系</param>
        /// <param name="peaceCost">和平代价（白银）</param>
        /// <returns>API调用结果</returns>
        public APIResult MakePeace(Faction faction, int peaceCost = 0)
        {
            if (RimDiplomacyMod.Instance == null)
                return APIResult.FailureResult("Settings not initialized");
            var settings = RimDiplomacyMod.Instance.InstanceSettings;
            if (settings == null)
                return APIResult.FailureResult("Settings not initialized");

            if (faction == null)
                return APIResult.FailureResult("Faction cannot be null");

            // 检查派系独立冷却
            int remainingCooldown = GetRemainingCooldownSeconds(faction, "MakePeace");
            if (remainingCooldown > 0)
                return APIResult.FailureResult($"Method MakePeace is on cooldown for {faction.Name}. Remaining: {remainingCooldown} seconds");

            // 检查是否处于敌对状态
            if (faction.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile)
                return APIResult.FailureResult("Not at war with this faction");

            // 检查和平代价上限
            if (peaceCost > settings.MaxPeaceCost)
                return APIResult.FailureResult($"Peace cost {peaceCost} exceeds maximum {settings.MaxPeaceCost}");

            // 设置中立关系
            faction.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Neutral);
            faction.TryAffectGoodwillWith(Faction.OfPlayer, settings.PeaceGoodwillReset, false, true, null);

            RecordAPICall("MakePeace", true, $"faction={faction.Name}, cost={peaceCost}");
            SetCooldown(faction, "MakePeace");

            // 发送通知
            Find.LetterStack.ReceiveLetter(
                "Peace Treaty Signed",
                $"A peace treaty has been signed with {faction.Name}.",
                LetterDefOf.PositiveEvent
            );

            return APIResult.SuccessResult(
                $"Peace made with {faction.Name}",
                new { Faction = faction.Name, Cost = peaceCost }
            );
        }

        #endregion

        #region 核心API方法 - 贸易与商队

        /// <summary>
        /// 请求商队
        /// </summary>
        /// <param name="faction">目标派系</param>
        /// <param name="requestedGoods">请求的商品类型</param>
        /// <returns>API调用结果</returns>
        public APIResult RequestTradeCaravan(Faction faction, string requestedGoods = "")
        {
            if (RimDiplomacyMod.Instance == null)
                return APIResult.FailureResult("Settings not initialized");
            var settings = RimDiplomacyMod.Instance.InstanceSettings;
            if (settings == null)
                return APIResult.FailureResult("Settings not initialized");

            if (faction == null)
                return APIResult.FailureResult("Faction cannot be null");

            // 检查派系独立冷却
            int remainingCooldown = GetRemainingCooldownSeconds(faction, "RequestTradeCaravan");
            if (remainingCooldown > 0)
                return APIResult.FailureResult($"Method RequestTradeCaravan is on cooldown for {faction.Name}. Remaining: {remainingCooldown} seconds");

            // 检查关系
            if (faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile)
                return APIResult.FailureResult("Cannot request caravan from hostile faction");

            RecordAPICall("RequestTradeCaravan", true, $"faction={faction.Name}, goods={requestedGoods}");
            SetCooldown(faction, "RequestTradeCaravan");

            // 这里可以触发实际的商队事件
            return APIResult.SuccessResult(
                $"Trade caravan requested from {faction.Name}",
                new { Faction = faction.Name, RequestedGoods = requestedGoods }
            );
        }

        #endregion

        #region 核心API方法 - 状态查询

        /// <summary>
        /// 获取派系详细信息
        /// </summary>
        /// <param name="faction">目标派系</param>
        /// <returns>API调用结果，包含派系详细信息</returns>
        public APIResult GetFactionInfo(Faction faction)
        {
            if (faction == null)
                return APIResult.FailureResult("Faction cannot be null");

            var settlements = Find.WorldObjects.SettlementBases
                .Where(s => s.Faction == faction)
                .Count();

            var info = new
            {
                Name = faction.Name,
                DefName = faction.def?.defName ?? "Unknown",
                Goodwill = faction.PlayerGoodwill,
                RelationKind = faction.RelationKindWith(Faction.OfPlayer).ToString(),
                IsPlayer = faction.IsPlayer,
                IsDefeated = faction.defeated,
                IsHidden = faction.def?.hidden ?? false,
                LeaderName = faction.leader?.Name?.ToStringFull ?? "None",
                SettlementCount = settlements,
                TodayAdjustment = GetTodayGoodwillAdjustment(faction)
            };

            RecordAPICall("GetFactionInfo", true, $"faction={faction.Name}");

            return APIResult.SuccessResult($"Faction info retrieved for {faction.Name}", info);
        }

        /// <summary>
        /// 获取所有可用派系列表
        /// </summary>
        /// <returns>API调用结果，包含派系列表</returns>
        public APIResult GetAllFactions()
        {
            if (Current.Game == null || Find.FactionManager == null)
                return APIResult.FailureResult("Game not initialized");

            var factions = Find.FactionManager.AllFactions
                .Where(f => !f.IsPlayer && !f.defeated)
                .Select(f => new
                {
                    Name = f.Name,
                    Goodwill = f.PlayerGoodwill,
                    RelationKind = f.RelationKindWith(Faction.OfPlayer).ToString(),
                    IsAIControlled = GameComponent_DiplomacyManager.Instance?.IsAIControlled(f) ?? false
                })
                .ToList();

            RecordAPICall("GetAllFactions", true, $"count={factions.Count}");

            return APIResult.SuccessResult($"Retrieved {factions.Count} factions", factions);
        }

        /// <summary>
        /// 获取殖民地状态信息
        /// </summary>
        /// <returns>API调用结果，包含殖民地状态</returns>
        public APIResult GetColonyStatus()
        {
            if (Current.Game == null)
                return APIResult.FailureResult("Game not initialized");

            var playerFaction = Faction.OfPlayer;
            var maps = Find.Maps.Where(m => m.IsPlayerHome).ToList();

            var status = new
            {
                ColonyName = playerFaction.Name,
                MapCount = maps.Count,
                TotalColonists = maps.Sum(m => m.mapPawns.FreeColonists.Count()),
                TotalWealth = maps.Sum(m => m.wealthWatcher.WealthTotal),
                GameDate = GenDate.DateFullStringAt(Find.TickManager.TicksAbs, Find.WorldGrid.LongLatOf(Find.AnyPlayerHomeMap.Tile)),
                ThreatLevel = maps.Any() ? StorytellerUtility.DefaultThreatPointsNow(Find.AnyPlayerHomeMap) : 0
            };

            RecordAPICall("GetColonyStatus", true, "");

            return APIResult.SuccessResult("Colony status retrieved", status);
        }

        #endregion

        #region 安全机制 - 冷却控制

        private void InitializeCooldownsIfNeeded()
        {
            EnsureInitialized();
            
            if (_factionCooldowns == null)
            {
                InitializeCooldowns();
            }
        }

        /// <summary>
        /// 检查派系特定方法是否处于冷却中
        /// </summary>
        /// <param name="faction">目标派系</param>
        /// <param name="methodName">方法名</param>
        /// <param name="cooldownTicks">冷却tick数</param>
        /// <returns>是否可用</returns>
        private bool CheckCooldown(Faction faction, string methodName, int cooldownTicks)
        {
            InitializeCooldownsIfNeeded();

            var factionCooldowns = GetOrCreateFactionCooldowns(faction);
            if (factionCooldowns == null) return true;

            if (!factionCooldowns.TryGetValue(methodName, out int nextAvailableTick))
                return true;

            int currentTick = Find.TickManager.TicksGame;
            return currentTick >= nextAvailableTick;
        }

        /// <summary>
        /// 设置派系特定方法冷却
        /// </summary>
        /// <param name="faction">目标派系</param>
        /// <param name="methodName">方法名</param>
        private void SetCooldown(Faction faction, string methodName)
        {
            InitializeCooldownsIfNeeded();

            if (RimDiplomacyMod.Instance == null) return;
            var settings = RimDiplomacyMod.Instance.InstanceSettings;
            int cooldownTicks = methodName switch
            {
                "AdjustGoodwill" => settings?.GoodwillCooldownTicks ?? 2500,
                "SendGift" => settings?.GiftCooldownTicks ?? 60000,
                "RequestAid" => settings?.AidCooldownTicks ?? 120000,
                "DeclareWar" => settings?.WarCooldownTicks ?? 60000,
                "MakePeace" => settings?.PeaceCooldownTicks ?? 60000,
                "RequestTradeCaravan" => settings?.CaravanCooldownTicks ?? 90000,
                _ => 2500
            };

            var factionCooldowns = GetOrCreateFactionCooldowns(faction);
            if (factionCooldowns != null && Find.TickManager != null)
                factionCooldowns[methodName] = Find.TickManager.TicksGame + cooldownTicks;
        }

        /// <summary>
        /// 获取派系特定方法的剩余冷却时间（秒）
        /// </summary>
        /// <param name="faction">目标派系</param>
        /// <param name="methodName">方法名</param>
        /// <returns>剩余冷却秒数，0表示可用</returns>
        public int GetRemainingCooldownSeconds(Faction faction, string methodName)
        {
            InitializeCooldownsIfNeeded();
            EnsureInitialized();

            if (faction == null) return 0;

            var factionCooldowns = GetOrCreateFactionCooldowns(faction);
            if (factionCooldowns == null) return 0;

            if (!factionCooldowns.TryGetValue(methodName, out int nextAvailableTick))
                return 0;

            if (Find.TickManager == null) return 0;

            int remainingTicks = nextAvailableTick - Find.TickManager.TicksGame;
            return Math.Max(0, remainingTicks / 60); // 转换为秒
        }

        /// <summary>
        /// 获取指定派系的冷却状态概览
        /// </summary>
        /// <param name="faction">目标派系</param>
        /// <returns>各方法的剩余冷却时间字典</returns>
        public Dictionary<string, int> GetFactionCooldownOverview(Faction faction)
        {
            InitializeCooldownsIfNeeded();
            EnsureInitialized();

            if (faction == null) return new Dictionary<string, int>();

            var factionCooldowns = GetOrCreateFactionCooldowns(faction);
            if (factionCooldowns == null) return new Dictionary<string, int>();

            var result = new Dictionary<string, int>();
            int currentTick = Find.TickManager?.TicksGame ?? 0;

            foreach (var kvp in factionCooldowns)
            {
                int remainingTicks = kvp.Value - currentTick;
                result[kvp.Key] = Math.Max(0, remainingTicks / 60); // 转换为秒
            }

            return result;
        }

        #endregion

        #region 安全机制 - 记录与日志

        /// <summary>
        /// 记录 API 调用
        /// </summary>
        /// <param name="methodName">方法名</param>
        /// <param name="success">是否成功</param>
        /// <param name="parameters">参数</param>
        /// <param name="errorMessage">错误信息</param>
        private void RecordAPICall(string methodName, bool success, string parameters, string errorMessage = "")
        {
            try
            {
                EnsureInitialized();
                
                // 检查游戏是否已初始化
                if (Find.TickManager == null)
                {
                    // 游戏未完全初始化，跳过记录
                    return;
                }

                var record = new APICallRecord
                {
                    MethodName = methodName,
                    TickCalled = Find.TickManager.TicksGame,
                    Parameters = parameters,
                    Success = success,
                    ErrorMessage = errorMessage
                };

                _apiCallHistory.Add(record);

                // 调试日志
                if (RimDiplomacyMod.Instance != null && (RimDiplomacyMod.Instance.InstanceSettings?.EnableDebugLogging ?? false))
                {
                    string status = success ? "SUCCESS" : "FAILED";
                    Log.Message($"[RimDiplomacy] API Call [{status}]: {methodName} - {parameters}");
                }
            }
            catch (Exception ex)
            {
                // 防止记录过程中的任何异常影响主流程
                Log.Error($"[RimDiplomacy] Failed to record API call: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取 API 调用历史
        /// </summary>
        /// <param name="methodName">方法名过滤（可选）</param>
        /// <param name="maxRecords">最大记录数</param>
        /// <returns>API 调用记录列表</returns>
        public List<APICallRecord> GetAPICallHistory(string methodName = null, int maxRecords = 50)
        {
            EnsureInitialized();
            
            var query = _apiCallHistory.AsEnumerable();

            if (!string.IsNullOrEmpty(methodName))
                query = query.Where(r => r.MethodName == methodName);

            return query
                .OrderByDescending(r => r.TickCalled)
                .Take(maxRecords)
                .ToList();
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 通知重大好感度变化
        /// </summary>
        private void NotifySignificantGoodwillChange(Faction faction, int oldGoodwill, int newGoodwill, string reason)
        {
            int change = newGoodwill - oldGoodwill;
            string title = change > 0 ? "Improved Relations" : "Worsened Relations";
            string message = $"Relations with {faction.Name} have {(change > 0 ? "improved" : "worsened")} by {Math.Abs(change)}.\n\nReason: {reason}";
            LetterDef letterDef = change > 0 ? LetterDefOf.PositiveEvent : LetterDefOf.NegativeEvent;

            Find.LetterStack.ReceiveLetter(title, message, letterDef);
        }

        /// <summary>
        /// 验证AI是否有权限操作指定派系
        /// </summary>
        /// <param name="faction">目标派系</param>
        /// <returns>是否有权限</returns>
        public bool ValidateAIPermission(Faction faction)
        {
            if (faction == null) return false;
            if (faction.IsPlayer) return false;
            if (faction.defeated) return false;
            if (faction.def?.hidden == true) return false;

            return true;
        }

        #endregion

        #region 对话行为好感度消耗系统

        /// <summary>
        /// 执行对话行为并应用好感度消耗/收益
        /// </summary>
        /// <param name="faction">目标派系</param>
        /// <param name="actionType">行为类型</param>
        /// <param name="relations">5维关系值</param>
        /// <returns>执行结果</returns>
        public APIResult ExecuteDialogueAction(Faction faction, DialogueGoodwillCost.DialogueActionType actionType, FactionRelationValues relations)
        {
            EnsureInitialized();

            if (faction == null)
                return APIResult.FailureResult("Faction cannot be null");

            // 1. 检查行为是否可执行（基于关系阈值）
            if (!RelationBasedCostCalculator.CanExecuteAction(actionType, relations, out string reason))
            {
                return APIResult.FailureResult($"Cannot execute action: {reason}");
            }

            // 2. 检查冷却时间
            if (!CheckDialogueActionCooldown(faction, actionType))
            {
                int remainingTicks = GetDialogueActionCooldownRemaining(faction, actionType);
                float remainingHours = remainingTicks / 2500f;
                return APIResult.FailureResult($"Action is on cooldown. Remaining: {remainingHours:F1} hours");
            }

            // 3. 检查每日限制
            if (!CheckDailyDialogueLimit(faction, actionType, out string limitReason))
            {
                return APIResult.FailureResult($"Daily limit reached: {limitReason}");
            }

            // 4. 计算实际好感度变化
            int goodwillChange = RelationBasedCostCalculator.CalculateCost(actionType, relations, out var costInfo);

            // 5. 执行好感度变化
            if (goodwillChange != 0)
            {
                int oldGoodwill = faction.PlayerGoodwill;
                faction.TryAffectGoodwillWith(Faction.OfPlayer, goodwillChange, false, true, null);
                int newGoodwill = faction.PlayerGoodwill;
                int actualChange = newGoodwill - oldGoodwill;

                // 记录到今日调整
                int currentDayAdjustment = _goodwillAdjustmentsToday.ContainsKey(faction) ? _goodwillAdjustmentsToday[faction] : 0;
                _goodwillAdjustmentsToday[faction] = currentDayAdjustment + actualChange;

                // 记录行为
                RecordDialogueAction(faction, actionType, actualChange);

                // 设置冷却
                SetDialogueActionCooldown(faction, actionType);

                // 记录API调用
                RecordAPICall("ExecuteDialogueAction", true, 
                    $"faction={faction.Name}, action={actionType}, change={actualChange}, modifier={costInfo.RelationModifier:F2}");

                // 触发通知（重大变化）
                if (Math.Abs(actualChange) >= 5)
                {
                    NotifyDialogueActionResult(faction, actionType, actualChange, costInfo);
                }

                return APIResult.SuccessResult(
                    $"Executed {DialogueGoodwillCost.GetActionLabel(actionType)}. Goodwill changed by {actualChange}.",
                    new
                    {
                        Action = actionType.ToString(),
                        ActionLabel = DialogueGoodwillCost.GetActionLabel(actionType),
                        GoodwillChange = actualChange,
                        OldGoodwill = oldGoodwill,
                        NewGoodwill = newGoodwill,
                        BaseValue = costInfo.BaseValue,
                        Modifier = costInfo.RelationModifier,
                        ModifierBreakdown = costInfo.ModifierBreakdown
                    }
                );
            }
            else
            {
                // 无好感度变化但仍记录行为
                RecordDialogueAction(faction, actionType, 0);
                SetDialogueActionCooldown(faction, actionType);

                return APIResult.SuccessResult(
                    $"Executed {DialogueGoodwillCost.GetActionLabel(actionType)}. No goodwill change.",
                    new
                    {
                        Action = actionType.ToString(),
                        ActionLabel = DialogueGoodwillCost.GetActionLabel(actionType),
                        GoodwillChange = 0
                    }
                );
            }
        }

        /// <summary>
        /// 预览对话行为的好感度消耗（不执行）
        /// </summary>
        public APIResult PreviewDialogueActionCost(Faction faction, DialogueGoodwillCost.DialogueActionType actionType, FactionRelationValues relations)
        {
            if (faction == null)
                return APIResult.FailureResult("Faction cannot be null");

            // 检查是否可执行
            bool canExecute = RelationBasedCostCalculator.CanExecuteAction(actionType, relations, out string reason);

            // 计算消耗
            int cost = RelationBasedCostCalculator.CalculateCost(actionType, relations, out var costInfo);

            // 检查冷却
            bool onCooldown = !CheckDialogueActionCooldown(faction, actionType);
            int remainingCooldown = onCooldown ? GetDialogueActionCooldownRemaining(faction, actionType) : 0;

            // 检查每日限制
            bool withinLimit = CheckDailyDialogueLimit(faction, actionType, out string limitReason);

            return APIResult.SuccessResult(
                $"Cost preview for {DialogueGoodwillCost.GetActionLabel(actionType)}",
                new
                {
                    Action = actionType.ToString(),
                    ActionLabel = DialogueGoodwillCost.GetActionLabel(actionType),
                    CanExecute = canExecute,
                    CannotExecuteReason = reason,
                    BaseCost = costInfo.BaseValue,
                    FinalCost = costInfo.FinalValue,
                    Modifier = costInfo.RelationModifier,
                    ModifierBreakdown = costInfo.ModifierBreakdown,
                    OnCooldown = onCooldown,
                    RemainingCooldownTicks = remainingCooldown,
                    RemainingCooldownHours = remainingCooldown / 2500f,
                    WithinDailyLimit = withinLimit,
                    DailyLimitReason = limitReason,
                    CurrentGoodwill = faction.PlayerGoodwill,
                    ExpectedGoodwillAfter = faction.PlayerGoodwill + costInfo.FinalValue
                }
            );
        }

        /// <summary>
        /// 检查对话行为冷却
        /// </summary>
        private bool CheckDialogueActionCooldown(Faction faction, DialogueGoodwillCost.DialogueActionType actionType)
        {
            EnsureInitialized();

            if (!_dialogueActionCooldowns.TryGetValue(actionType, out var factionCooldowns))
                return true;

            if (!factionCooldowns.TryGetValue(faction, out int nextAvailableTick))
                return true;

            int currentTick = Find.TickManager.TicksGame;
            return currentTick >= nextAvailableTick;
        }

        /// <summary>
        /// 获取对话行为剩余冷却时间
        /// </summary>
        private int GetDialogueActionCooldownRemaining(Faction faction, DialogueGoodwillCost.DialogueActionType actionType)
        {
            EnsureInitialized();

            if (!_dialogueActionCooldowns.TryGetValue(actionType, out var factionCooldowns))
                return 0;

            if (!factionCooldowns.TryGetValue(faction, out int nextAvailableTick))
                return 0;

            int currentTick = Find.TickManager.TicksGame;
            return Math.Max(0, nextAvailableTick - currentTick);
        }

        /// <summary>
        /// 设置对话行为冷却
        /// </summary>
        private void SetDialogueActionCooldown(Faction faction, DialogueGoodwillCost.DialogueActionType actionType)
        {
            EnsureInitialized();

            int cooldownTicks = DialogueGoodwillCost.GetCooldownTicks(actionType);
            if (cooldownTicks <= 0) return;

            if (!_dialogueActionCooldowns.TryGetValue(actionType, out var factionCooldowns))
            {
                factionCooldowns = new Dictionary<Faction, int>();
                _dialogueActionCooldowns[actionType] = factionCooldowns;
            }

            factionCooldowns[faction] = Find.TickManager.TicksGame + cooldownTicks;
        }

        /// <summary>
        /// 检查每日对话行为限制
        /// </summary>
        private bool CheckDailyDialogueLimit(Faction faction, DialogueGoodwillCost.DialogueActionType actionType, out string reason)
        {
            EnsureInitialized();
            reason = "";

            int baseValue = DialogueGoodwillCost.GetBaseValue(actionType);
            bool isCostAction = baseValue < 0;

            // 计算今日该派系的累计消耗/收益
            int todayCost = 0;
            int todayGain = 0;

            foreach (var record in _dialogueActionRecords)
            {
                if (record.FactionName == faction.Name)
                {
                    if (record.GoodwillChange < 0)
                        todayCost += Math.Abs(record.GoodwillChange);
                    else if (record.GoodwillChange > 0)
                        todayGain += record.GoodwillChange;
                }
            }

            // 检查是否超出限制
            if (isCostAction)
            {
                int expectedCost = Math.Abs(RelationBasedCostCalculator.CalculateCost(actionType, new FactionRelationValues()));
                if (todayCost + expectedCost > Math.Abs(DialogueGoodwillCost.DailyCostLimit))
                {
                    reason = $"今日消耗已达上限 ({todayCost}/{Math.Abs(DialogueGoodwillCost.DailyCostLimit)})";
                    return false;
                }
            }
            else
            {
                int expectedGain = RelationBasedCostCalculator.CalculateCost(actionType, new FactionRelationValues());
                if (todayGain + expectedGain > DialogueGoodwillCost.DailyGainLimit)
                {
                    reason = $"今日收益已达上限 ({todayGain}/{DialogueGoodwillCost.DailyGainLimit})";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 记录对话行为
        /// </summary>
        private void RecordDialogueAction(Faction faction, DialogueGoodwillCost.DialogueActionType actionType, int goodwillChange)
        {
            EnsureInitialized();

            var record = new DialogueActionRecord
            {
                ActionType = actionType,
                GoodwillChange = goodwillChange,
                Tick = Find.TickManager.TicksGame,
                FactionName = faction.Name
            };

            _dialogueActionRecords.Add(record);
        }

        /// <summary>
        /// 获取今日对话行为统计
        /// </summary>
        public APIResult GetTodayDialogueStats(Faction faction)
        {
            EnsureInitialized();

            if (faction == null)
                return APIResult.FailureResult("Faction cannot be null");

            int totalCost = 0;
            int totalGain = 0;
            var actionCounts = new Dictionary<DialogueGoodwillCost.DialogueActionType, int>();

            foreach (var record in _dialogueActionRecords)
            {
                if (record.FactionName == faction.Name)
                {
                    if (record.GoodwillChange < 0)
                        totalCost += Math.Abs(record.GoodwillChange);
                    else if (record.GoodwillChange > 0)
                        totalGain += record.GoodwillChange;

                    if (!actionCounts.ContainsKey(record.ActionType))
                        actionCounts[record.ActionType] = 0;
                    actionCounts[record.ActionType]++;
                }
            }

            return APIResult.SuccessResult(
                $"Today's dialogue stats for {faction.Name}",
                new
                {
                    FactionName = faction.Name,
                    TotalCost = totalCost,
                    TotalGain = totalGain,
                    CostLimit = Math.Abs(DialogueGoodwillCost.DailyCostLimit),
                    GainLimit = DialogueGoodwillCost.DailyGainLimit,
                    RemainingCostBudget = Math.Abs(DialogueGoodwillCost.DailyCostLimit) - totalCost,
                    RemainingGainBudget = DialogueGoodwillCost.DailyGainLimit - totalGain,
                    ActionCounts = actionCounts
                }
            );
        }

        /// <summary>
        /// 通知对话行为结果
        /// </summary>
        private void NotifyDialogueActionResult(Faction faction, DialogueGoodwillCost.DialogueActionType actionType, int change, CostCalculationInfo costInfo)
        {
            string actionLabel = DialogueGoodwillCost.GetActionLabel(actionType);
            string title;
            string message;
            LetterDef letterDef;

            if (change < 0)
            {
                title = "外交行为消耗";
                message = $"你对 {faction.Name} 进行了{actionLabel}，消耗了 {Math.Abs(change)} 点好感度。\n\n" +
                         $"基础消耗: {Math.Abs(costInfo.BaseValue)}\n" +
                         $"关系减免: {(1 - costInfo.RelationModifier) * 100:F0}%\n" +
                         $"最终消耗: {Math.Abs(change)}";
                letterDef = LetterDefOf.NegativeEvent;
            }
            else
            {
                title = "外交行为收益";
                message = $"你对 {faction.Name} 进行了{actionLabel}，增加了 {change} 点好感度。\n\n" +
                         $"基础收益: {costInfo.BaseValue}\n" +
                         $"关系加成: {(costInfo.RelationModifier - 1) * 100:F0}%\n" +
                         $"最终收益: {change}";
                letterDef = LetterDefOf.PositiveEvent;
            }

            Find.LetterStack.ReceiveLetter(title, message, letterDef);
        }

        #endregion
    }
}
