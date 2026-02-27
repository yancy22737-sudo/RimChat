using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimDiplomacy
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
            
            // 修复 Dictionary 序列化问题 - 使用工作列表
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
        /// 方法冷却时间 (方法名 -> 下次可用 tick)
        /// </summary>
        private Dictionary<string, int> _methodCooldowns;

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
            if (_methodCooldowns == null)
                _methodCooldowns = new Dictionary<string, int>();
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

        #endregion

        #region 初始化与重置

        private void InitializeCooldowns()
        {
            EnsureInitialized();
            
            _methodCooldowns.Clear();
            if (RimDiplomacyMod.Instance == null) return;
            var settings = RimDiplomacyMod.Instance.InstanceSettings;
            if (settings == null) return;

            // 初始化各方法的冷却时间
            _methodCooldowns["AdjustGoodwill"] = 0;
            _methodCooldowns["SendGift"] = 0;
            _methodCooldowns["RequestAid"] = 0;
            _methodCooldowns["DeclareWar"] = 0;
            _methodCooldowns["MakePeace"] = 0;
            _methodCooldowns["SendTradeCaravan"] = 0;
        }

        /// <summary>
        /// 每日重置（由 GameComponent 调用）
        /// </summary>
        public void DailyReset()
        {
            EnsureInitialized();
            _goodwillAdjustmentsToday.Clear();
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

            // 检查冷却
            if (!CheckCooldown("AdjustGoodwill", settings.GoodwillCooldownTicks))
                return APIResult.FailureResult($"Method AdjustGoodwill is on cooldown. Cooldown: {settings.GoodwillCooldownTicks / 2500f} hours");

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
            SetCooldown("AdjustGoodwill");

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

            if (!CheckCooldown("SendGift", settings.GiftCooldownTicks))
                return APIResult.FailureResult($"Method SendGift is on cooldown");

            // 检查礼物上限
            if (silverAmount > settings.MaxGiftSilverAmount)
                return APIResult.FailureResult($"Gift amount {silverAmount} exceeds maximum {settings.MaxGiftSilverAmount}");

            // 检查好感度收益上限
            if (goodwillGain > settings.MaxGiftGoodwillGain)
                return APIResult.FailureResult($"Goodwill gain {goodwillGain} exceeds maximum {settings.MaxGiftGoodwillGain}");

            // 执行礼物发送（模拟）
            faction.TryAffectGoodwillWith(Faction.OfPlayer, goodwillGain, false, true, null);

            RecordAPICall("SendGift", true, $"faction={faction.Name}, silver={silverAmount}, goodwillGain={goodwillGain}");
            SetCooldown("SendGift");

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

            if (!CheckCooldown("RequestAid", settings.AidCooldownTicks))
                return APIResult.FailureResult($"Method RequestAid is on cooldown");

            // 检查关系是否允许请求援助
            if (faction.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Ally)
                return APIResult.FailureResult("Can only request aid from allied factions");

            // 检查好感度是否足够
            if (faction.PlayerGoodwill < settings.MinGoodwillForAid)
                return APIResult.FailureResult($"Need at least {settings.MinGoodwillForAid} goodwill to request aid");

            RecordAPICall("RequestAid", true, $"faction={faction.Name}, aidType={aidType}");
            SetCooldown("RequestAid");

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

            if (!CheckCooldown("DeclareWar", settings.WarCooldownTicks))
                return APIResult.FailureResult($"Method DeclareWar is on cooldown");

            // 检查是否已经是敌对关系
            if (faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile)
                return APIResult.FailureResult("Already at war with this faction");

            // 检查好感度是否允许宣战
            if (faction.PlayerGoodwill > settings.MaxGoodwillForWarDeclaration)
                return APIResult.FailureResult($"Cannot declare war with goodwill above {settings.MaxGoodwillForWarDeclaration}");

            // 设置敌对关系
            faction.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile);

            RecordAPICall("DeclareWar", true, $"faction={faction.Name}, reason={reason}");
            SetCooldown("DeclareWar");

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

            if (!CheckCooldown("MakePeace", settings.PeaceCooldownTicks))
                return APIResult.FailureResult($"Method MakePeace is on cooldown");

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
            SetCooldown("MakePeace");

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

            if (!CheckCooldown("RequestTradeCaravan", settings.CaravanCooldownTicks))
                return APIResult.FailureResult($"Method RequestTradeCaravan is on cooldown");

            // 检查关系
            if (faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile)
                return APIResult.FailureResult("Cannot request caravan from hostile faction");

            RecordAPICall("RequestTradeCaravan", true, $"faction={faction.Name}, goods={requestedGoods}");
            SetCooldown("RequestTradeCaravan");

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
            
            if (_methodCooldowns == null || _methodCooldowns.Count == 0)
            {
                InitializeCooldowns();
            }
        }

        /// <summary>
        /// 检查方法是否处于冷却中
        /// </summary>
        /// <param name="methodName">方法名</param>
        /// <param name="cooldownTicks">冷却tick数</param>
        /// <returns>是否可用</returns>
        private bool CheckCooldown(string methodName, int cooldownTicks)
        {
            InitializeCooldownsIfNeeded();

            if (!_methodCooldowns.TryGetValue(methodName, out int nextAvailableTick))
                return true;

            int currentTick = Find.TickManager.TicksGame;
            return currentTick >= nextAvailableTick;
        }

        /// <summary>
        /// 设置方法冷却
        /// </summary>
        /// <param name="methodName">方法名</param>
        private void SetCooldown(string methodName)
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

            EnsureInitialized();
            if (Find.TickManager != null)
                _methodCooldowns[methodName] = Find.TickManager.TicksGame + cooldownTicks;
        }

        /// <summary>
        /// 获取方法剩余冷却时间（秒）
        /// </summary>
        /// <param name="methodName">方法名</param>
        /// <returns>剩余冷却秒数，0表示可用</returns>
        public int GetRemainingCooldownSeconds(string methodName)
        {
            InitializeCooldownsIfNeeded();
            EnsureInitialized();

            if (!_methodCooldowns.TryGetValue(methodName, out int nextAvailableTick))
                return 0;

            if (Find.TickManager == null) return 0;

            int remainingTicks = nextAvailableTick - Find.TickManager.TicksGame;
            return Math.Max(0, remainingTicks / 60); // 转换为秒
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
    }
}
