using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using RimChat.Relation;
using RimChat.AI;
using RimChat.Config;
using RimChat.Util;
using RimChat.Core;
using RimChat.Persistence;
using RimChat.Memory;
using RimChat.WorldState;

namespace RimChat.DiplomacySystem
{
    /// <summary>/// AI与游戏交互的主接口类
 /// 提供based ondialoguecontents的游戏state管理功能
 ///</summary>
    public partial class GameAIInterface : IExposable
    {
        private const int CaravanFactionCooldownTicks = 7 * GenDate.TicksPerDay;
        private const int AidFactionCooldownTicks = 15 * GenDate.TicksPerDay;

        #region Singleton and initialization

        private static readonly Lazy<GameAIInterface> _lazyInstance = new Lazy<GameAIInterface>(() => new GameAIInterface());
        public static GameAIInterface Instance => _lazyInstance.Value;

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
            
            // 序列化goodwill调整record
            ExposeGoodwillAdjustments();
            
            // 序列化faction独立冷却数据
            ExposeFactionCooldowns();
            ExposeRaidCooldowns();
        }

        /// <summary>/// 序列化全局袭击冷却状态
        ///</summary>
        private void ExposeRaidCooldowns()
        {
            Scribe_Values.Look(ref _raidCallEveryoneNextAvailableTick, "raidCallEveryoneNextAvailableTick", 0);
            Scribe_Collections.Look(ref _raidWavesState, "raidWavesState", LookMode.Deep);
        }

        /// <summary>/// 序列化goodwill调整record
 ///</summary>
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
        /// 序列化faction独立冷却数据
        /// 结构: Dictionary<Faction, Dictionary<string, int>>
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

        /// <summary>/// API 调用record
 ///</summary>
        private List<APICallRecord> _apiCallHistory;

        /// <summary>/// 今日goodwill调整record (faction -> 调整values)
 ///</summary>
        private Dictionary<Faction, int> _goodwillAdjustmentsToday;

        /// <summary>/// faction独立冷却时间 (faction -> (method名 -> 下次可用 tick))
 ///</summary>
        private Dictionary<Faction, Dictionary<string, int>> _factionCooldowns;

        /// <summary>/// dialoguebehavior冷却时间 (behavior类型 -> faction -> 下次可用 tick)
 ///</summary>
        private Dictionary<DialogueGoodwillCost.DialogueActionType, Dictionary<Faction, int>> _dialogueActionCooldowns;

        /// <summary>/// dialoguebehaviorrecord (used for每日限制)
 ///</summary>
        private List<DialogueActionRecord> _dialogueActionRecords;

        /// <summary>/// 上次重置 tick
 ///</summary>
        private int _lastResetTick = 0;
        private const int MakePeaceTargetGoodwill = 0;
        private const int DeclareWarTargetGoodwill = -80;

        /// <summary>/// request_raid_call_everyone 全局冷却 (下次可用 tick)
        ///</summary>
        private int _raidCallEveryoneNextAvailableTick = 0;

        /// <summary>/// 袭击波次状态列表 (用于跟踪持续袭击)
        ///</summary>
        private List<RaidWaveState> _raidWavesState;

        /// <summary>/// initialize所有字段
 ///</summary>
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
            if (_raidWavesState == null)
                _raidWavesState = new List<RaidWaveState>();
        }

        /// <summary>/// API调用record结构
 ///</summary>
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

        /// <summary>/// API调用result
 ///</summary>
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

        public class DialogueApiGoodwillCostResult
        {
            public string SourceAction { get; set; }
            public string Detail { get; set; }
            public DialogueGoodwillCost.DialogueActionType ActionType { get; set; }
            public int BaseCost { get; set; }
            public int ActualChange { get; set; }
            public int OldGoodwill { get; set; }
            public int NewGoodwill { get; set; }
        }

        /// <summary>/// faction冷却entry (used for序列化)
 ///</summary>
        public class FactionCooldownEntry : IExposable
        {
            public Faction Faction;
            public List<KeyValuePair<string, int>> MethodCooldowns;

            public void ExposeData()
            {
                Scribe_References.Look(ref Faction, "faction");
                
                // 序列化method冷却列表
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

        /// <summary>/// get或创建faction的冷却字典
 ///</summary>
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
                    ["RequestTradeCaravan"] = 0,
                    ["RequestRaid"] = 0,
                    ["RequestItemAirdrop"] = 0
                };
                _factionCooldowns[faction] = cooldowns;
            }
            
            return cooldowns;
        }

        /// <summary>/// 每日重置 (由 GameComponent 调用)
 ///</summary>
        public void DailyReset()
        {
            EnsureInitialized();
            _goodwillAdjustmentsToday.Clear();
            _dialogueActionRecords.Clear();
            CleanupOldRecords();
        }

        /// <summary>/// 清理过期的 API 调用record
 ///</summary>
        private void CleanupOldRecords()
        {
            EnsureInitialized();
            
            if (Find.TickManager == null) return;
            
            int currentTick = Find.TickManager.TicksGame;
            int maxAgeTicks = 60000 * 7; // 保留 7 天的record

            _apiCallHistory.RemoveAll(r => currentTick - r.TickCalled > maxAgeTicks);
        }

        #endregion

        #region 核心API方法 - 好感度管理

        /// <summary>/// 调整factiongoodwill
 ///</summary>
        /// <param name="faction">目标faction</param>
        /// <param name="amount">调整values (-100 到 100)</param>
        /// <param name="reason">调整原因</param>
        /// <returns>API调用result</returns>
        public APIResult AdjustGoodwill(Faction faction, int amount, string reason = "")
        {
            if (RimChatMod.Instance == null)
                return APIResult.FailureResult("Settings not initialized");
            var settings = RimChatMod.Instance.InstanceSettings;
            if (settings == null)
                return APIResult.FailureResult("Settings not initialized");

            // 参数验证
            if (faction == null)
                return APIResult.FailureResult("Faction cannot be null");

            if (faction.IsPlayer)
                return APIResult.FailureResult("Cannot adjust player faction goodwill");

            // 检查faction独立冷却
            int remainingCooldown = GetRemainingCooldownSeconds(faction, "AdjustGoodwill");
            if (remainingCooldown > 0)
                return APIResult.FailureResult($"Method AdjustGoodwill is on cooldown for {faction.Name}. Remaining: {remainingCooldown} seconds");

            // 检查单次调整上限
            int maxSingleAdjustment = settings.MaxGoodwillAdjustmentPerCall;
            if (Math.Abs(amount) > maxSingleAdjustment)
            {
                Log.Warning($"[RimChat] AI attempted to adjust goodwill by {amount}, clamped to {maxSingleAdjustment}");
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

                Log.Warning($"[RimChat] AI goodwill adjustment clamped from {amount} to {allowedAdjustment} due to daily limit");
                amount = allowedAdjustment;
            }

            // 执行调整
            int oldGoodwill = faction.PlayerGoodwill;
            faction.TryAffectGoodwillWith(Faction.OfPlayer, amount, false, true, null);
            int newGoodwill = faction.PlayerGoodwill;
            int actualChange = newGoodwill - oldGoodwill;

            // Record调整
            _goodwillAdjustmentsToday[faction] = currentDayAdjustment + actualChange;
            RecordAPICall("AdjustGoodwill", true, $"faction={faction.Name}, amount={actualChange}, reason={reason}");
            SetCooldown(faction, "AdjustGoodwill");

            // 触发event通知
            if (Math.Abs(actualChange) >= 10)
            {
                NotifySignificantGoodwillChange(faction, oldGoodwill, newGoodwill, reason);
            }

            return APIResult.SuccessResult(
                $"Goodwill adjusted from {oldGoodwill} to {newGoodwill} (change: {actualChange})",
                new { OldGoodwill = oldGoodwill, NewGoodwill = newGoodwill, Change = actualChange }
            );
        }

        /// <summary>/// get当前goodwill
 ///</summary>
        /// <param name="faction">目标faction</param>
        /// <returns>API调用result, 包含goodwill数据</returns>
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

        /// <summary>/// get今日已调整的goodwillvalues
 ///</summary>
        /// <param name="faction">目标faction</param>
        /// <returns>今日累计调整values</returns>
        public int GetTodayGoodwillAdjustment(Faction faction)
        {
            if (faction == null) return 0;
            return _goodwillAdjustmentsToday.ContainsKey(faction) ? _goodwillAdjustmentsToday[faction] : 0;
        }

        #endregion

        #region 核心API方法 - 外交操作

        /// <summary>/// 向faction发送礼物
 ///</summary>
        /// <param name="faction">目标faction</param>
        /// <param name="silverAmount">白银数量</param>
        /// <param name="goodwillGain">获得的goodwill</param>
        /// <returns>API调用result</returns>
        public APIResult SendGift(Faction faction, int silverAmount, int goodwillGain)
        {
            if (RimChatMod.Instance == null)
                return APIResult.FailureResult("Settings not initialized");
            var settings = RimChatMod.Instance.InstanceSettings;
            if (settings == null)
                return APIResult.FailureResult("Settings not initialized");

            if (faction == null)
                return APIResult.FailureResult("Faction cannot be null");

            // 检查faction独立冷却
            int remainingCooldown = GetRemainingCooldownSeconds(faction, "SendGift");
            if (remainingCooldown > 0)
                return APIResult.FailureResult($"Method SendGift is on cooldown for {faction.Name}. Remaining: {remainingCooldown} seconds");

            // 检查礼物上限
            if (silverAmount > settings.MaxGiftSilverAmount)
                return APIResult.FailureResult($"Gift amount {silverAmount} exceeds maximum {settings.MaxGiftSilverAmount}");

            // 检查goodwill收益上限
            if (goodwillGain > settings.MaxGiftGoodwillGain)
                return APIResult.FailureResult($"Goodwill gain {goodwillGain} exceeds maximum {settings.MaxGiftGoodwillGain}");

            // 执行礼物发送 (模拟)
            faction.TryAffectGoodwillWith(Faction.OfPlayer, goodwillGain, false, true, null);

            RecordAPICall("SendGift", true, $"faction={faction.Name}, silver={silverAmount}, goodwillGain={goodwillGain}");
            SetCooldown(faction, "SendGift");

            return APIResult.SuccessResult(
                $"Gift of {silverAmount} silver sent to {faction.Name}, gained {goodwillGain} goodwill",
                new { SilverAmount = silverAmount, GoodwillGain = goodwillGain }
            );
        }

        /// <summary>/// requestfaction援助
 ///</summary>
        /// <param name="faction">目标faction</param>
        /// <param name="aidType">援助类型 (Military, Medical, Resources)</param>
        /// <returns>API调用result</returns>
        public APIResult RequestAid(Faction faction, string aidType, bool delayed = true)
        {
            if (RimChatMod.Instance == null)
                return APIResult.FailureResult("Settings not initialized");
            var settings = RimChatMod.Instance.InstanceSettings;
            if (settings == null)
                return APIResult.FailureResult("Settings not initialized");

            if (faction == null)
                return APIResult.FailureResult("Faction cannot be null");

            // 检查faction独立冷却
            int remainingCooldown = GetRemainingCooldownSeconds(faction, "RequestAid");
            if (remainingCooldown > 0)
                return APIResult.FailureResult($"Method RequestAid is on cooldown for {faction.Name}. Remaining: {remainingCooldown} seconds");

            // 检查relationwhether允许request援助
            if (faction.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Ally)
                return APIResult.FailureResult("Can only request aid from allied factions");

            // 检查goodwillwhether足够
            if (faction.PlayerGoodwill < settings.MinGoodwillForAid)
                return APIResult.FailureResult($"Need at least {settings.MinGoodwillForAid} goodwill to request aid");

            // 解析援助类型
            AidType type = DiplomacyEventManager.ParseAidType(aidType);

            RecordAPICall("RequestAid", true, $"faction={faction.Name}, aidType={type}, delayed={delayed}");
            SetCooldown(faction, "RequestAid");

            bool eventSuccess;
            string resultMessage;

            if (delayed)
            {
                eventSuccess = DiplomacyEventManager.ScheduleDelayedAid(faction, type);
                int delayTicks = DiplomacyEventManager.CalculateDelayTicks(faction, true);
                float delayDays = delayTicks / 60000f;
                resultMessage = $"Aid scheduled from {faction.Name} for {DiplomacyEventManager.GetAidTypeLabel(type)}. Arrival in {delayDays:F1} days.";
            }
            else
            {
                eventSuccess = DiplomacyEventManager.TriggerAidEvent(faction, type);
                resultMessage = $"Aid request sent to {faction.Name} for {DiplomacyEventManager.GetAidTypeLabel(type)}";
            }

            return APIResult.SuccessResult(
                resultMessage,
                new { AidType = type.ToString(), Faction = faction.Name, EventSuccess = eventSuccess, Delayed = delayed }
            );
        }

        /// <summary>/// 宣战
 ///</summary>
        /// <param name="faction">目标faction</param>
        /// <param name="reason">宣战原因</param>
        /// <returns>API调用result</returns>
        public APIResult DeclareWar(Faction faction, string reason = "")
        {
            if (RimChatMod.Instance == null)
                return APIResult.FailureResult("Settings not initialized");
            var settings = RimChatMod.Instance.InstanceSettings;
            if (settings == null)
                return APIResult.FailureResult("Settings not initialized");

            if (faction == null)
                return APIResult.FailureResult("Faction cannot be null");

            // 检查faction独立冷却
            int remainingCooldown = GetRemainingCooldownSeconds(faction, "DeclareWar");
            if (remainingCooldown > 0)
                return APIResult.FailureResult($"Method DeclareWar is on cooldown for {faction.Name}. Remaining: {remainingCooldown} seconds");

            // 检查whether已经是敌对relation
            if (faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile)
                return APIResult.FailureResult("Already at war with this faction");

            // 检查goodwillwhether允许宣战
            if (faction.PlayerGoodwill > settings.MaxGoodwillForWarDeclaration)
                return APIResult.FailureResult($"Cannot declare war with goodwill above {settings.MaxGoodwillForWarDeclaration}");

            // Use goodwill-first relation settlement to avoid SetRelationDirect errors on goodwill-driven factions.
            if (!TryApplyRelationTargetGoodwill(
                faction,
                DeclareWarTargetGoodwill,
                FactionRelationKind.Hostile,
                out int appliedGoodwill,
                out string relationError))
            {
                return APIResult.FailureResult(
                    $"Failed to declare war with {faction.Name}: {relationError}");
            }

            RecordAPICall(
                "DeclareWar",
                true,
                $"faction={faction.Name}, reason={reason}, targetGoodwill={DeclareWarTargetGoodwill}, appliedGoodwill={appliedGoodwill}");
            SetCooldown(faction, "DeclareWar");

            // 发送通知
            Find.LetterStack.ReceiveLetter(
                "RimChat_DeclareWarLetterTitle".Translate(),
                "RimChat_DeclareWarLetterBody".Translate(faction.Name, reason ?? string.Empty),
                LetterDefOf.ThreatBig
            );

            return APIResult.SuccessResult(
                $"War declared with {faction.Name}",
                new { Faction = faction.Name, Reason = reason }
            );
        }

        /// <summary>/// 议和
 ///</summary>
        /// <param name="faction">目标faction</param>
        /// <param name="peaceCost">和平代价 (白银) </param>
        /// <returns>API调用result</returns>
        public APIResult MakePeace(Faction faction, int peaceCost = 0)
        {
            if (RimChatMod.Instance == null)
                return APIResult.FailureResult("Settings not initialized");
            var settings = RimChatMod.Instance.InstanceSettings;
            if (settings == null)
                return APIResult.FailureResult("Settings not initialized");

            if (faction == null)
                return APIResult.FailureResult("Faction cannot be null");

            // 检查faction独立冷却
            int remainingCooldown = GetRemainingCooldownSeconds(faction, "MakePeace");
            if (remainingCooldown > 0)
                return APIResult.FailureResult($"Method MakePeace is on cooldown for {faction.Name}. Remaining: {remainingCooldown} seconds");

            // 检查whether处于敌对state
            if (faction.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile)
                return APIResult.FailureResult("Not at war with this faction");

            // 检查和平代价上限
            if (peaceCost > settings.MaxPeaceCost)
                return APIResult.FailureResult($"Peace cost {peaceCost} exceeds maximum {settings.MaxPeaceCost}");

            // Use goodwill-first relation settlement to avoid SetRelationDirect errors on goodwill-driven factions.
            if (!TryApplyRelationTargetGoodwill(
                faction,
                MakePeaceTargetGoodwill,
                FactionRelationKind.Neutral,
                out int appliedGoodwill,
                out string relationError))
            {
                return APIResult.FailureResult(
                    $"Failed to make peace with {faction.Name}: {relationError}");
            }

            RecordAPICall(
                "MakePeace",
                true,
                $"faction={faction.Name}, cost={peaceCost}, targetGoodwill={MakePeaceTargetGoodwill}, appliedGoodwill={appliedGoodwill}");
            SetCooldown(faction, "MakePeace");

            // 发送通知
            Find.LetterStack.ReceiveLetter(
                "RimChat_MakePeaceLetterTitle".Translate(),
                "RimChat_MakePeaceLetterBody".Translate(faction.Name),
                LetterDefOf.PositiveEvent
            );

            return APIResult.SuccessResult(
                $"Peace made with {faction.Name}",
                new { Faction = faction.Name, Cost = peaceCost }
            );
        }

        #endregion

        #region 核心API方法 - 贸易与商队

        /// <summary>/// request袭击 (AI控制)
 ///</summary>
        public APIResult RequestRaid(Faction faction, string strategyDefName = "", string arrivalModeDefName = "", bool delayed = true)
        {
            if (RimChatMod.Instance == null)
                return APIResult.FailureResult("Settings not initialized");
            var settings = RimChatMod.Instance.InstanceSettings;
            if (settings == null)
                return APIResult.FailureResult("Settings not initialized");

            if (faction == null)
                return APIResult.FailureResult("Faction cannot be null");

            RaidDefNameNormalizer.NormalizeRaidRequestParameters(
                strategyDefName,
                arrivalModeDefName,
                out string normalizedStrategyDefName,
                out string normalizedArrivalModeDefName);
            strategyDefName = normalizedStrategyDefName;
            arrivalModeDefName = normalizedArrivalModeDefName;

            // 检查faction独立冷却
            int remainingCooldown = GetRemainingCooldownSeconds(faction, "RequestRaid");
            if (remainingCooldown > 0)
                return APIResult.FailureResult($"Method RequestRaid is on cooldown for {faction.Name}. Remaining: {remainingCooldown} seconds");

            if (!DiplomacyEventManager.TryValidateRaidFaction(faction, out string raidFactionValidationReason))
                return APIResult.FailureResult(raidFactionValidationReason);

            // Resolve Defs
            RaidStrategyDef strategy = null;
            if (!string.IsNullOrEmpty(strategyDefName))
            {
                strategy = DefDatabase<RaidStrategyDef>.GetNamedSilentFail(strategyDefName);
                if (strategy == null) return APIResult.FailureResult($"Invalid RaidStrategyDef: {strategyDefName}");
            }

            PawnsArrivalModeDef arrivalMode = null;
            if (!string.IsNullOrEmpty(arrivalModeDefName))
            {
                arrivalMode = DefDatabase<PawnsArrivalModeDef>.GetNamedSilentFail(arrivalModeDefName);
                if (arrivalMode == null) return APIResult.FailureResult($"Invalid PawnsArrivalModeDef: {arrivalModeDefName}");
            }

            // Points is now handled by system (-1)
            float points = -1;

            // Logic
            bool success;
            string resultMessage;
            if (delayed)
            {
                success = DiplomacyEventManager.ScheduleDelayedRaid(faction, points, strategy, arrivalMode);
                int delayTicks = DiplomacyEventManager.CalculateRaidDelayTicks(strategy, arrivalMode);
                float delayHours = delayTicks / 2500f;
                resultMessage = $"Raid scheduled from {faction.Name}. Arrival in {delayHours:F1} hours.";
            }
            else
            {
                success = DiplomacyEventManager.TriggerRaidEvent(faction, points, strategy, arrivalMode);
                resultMessage = $"Raid triggered from {faction.Name}";
            }

            if (success)
            {
                SetCooldown(faction, "RequestRaid");
                RecordAPICall("RequestRaid", true, $"faction={faction.Name}, strategy={strategyDefName}, arrival={arrivalModeDefName}");
                WorldEventLedgerComponent.Instance?.RecordRaidIntent(faction, delayed, strategy?.defName ?? strategyDefName, arrivalMode?.defName ?? arrivalModeDefName);
                
                return APIResult.SuccessResult(resultMessage, new { Delayed = delayed });
            }
            else
            {
                return APIResult.FailureResult("Failed to trigger raid");
            }
        }

        /// <summary>/// request商队
 ///</summary>
        /// <param name="faction">目标faction</param>
        /// <param name="caravanType">商队类型 (General, BulkGoods, CombatSupplier, Exotic, Slaver)</param>
        /// <returns>API调用result</returns>
        public APIResult RequestTradeCaravan(Faction faction, string caravanType = "General", bool delayed = true)
        {
            if (RimChatMod.Instance == null)
                return APIResult.FailureResult("Settings not initialized");
            var settings = RimChatMod.Instance.InstanceSettings;
            if (settings == null)
                return APIResult.FailureResult("Settings not initialized");

            if (faction == null)
                return APIResult.FailureResult("Faction cannot be null");

            // 检查faction独立冷却
            int remainingCooldown = GetRemainingCooldownSeconds(faction, "RequestTradeCaravan");
            if (remainingCooldown > 0)
                return APIResult.FailureResult($"Method RequestTradeCaravan is on cooldown for {faction.Name}. Remaining: {remainingCooldown} seconds");

            // 检查relation
            if (faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile)
                return APIResult.FailureResult("Cannot request caravan from hostile faction");

            // 解析商队类型
            CaravanType type = DiplomacyEventManager.ParseCaravanType(caravanType);

            RecordAPICall("RequestTradeCaravan", true, $"faction={faction.Name}, caravanType={type}, delayed={delayed}");
            SetCooldown(faction, "RequestTradeCaravan");

            bool eventSuccess;
            string resultMessage;

            if (delayed)
            {
                eventSuccess = DiplomacyEventManager.ScheduleDelayedCaravan(faction, type);
                int delayTicks = DiplomacyEventManager.CalculateDelayTicks(faction, false);
                float delayDays = delayTicks / 60000f;
                resultMessage = $"Trade caravan scheduled from {faction.Name}: {DiplomacyEventManager.GetCaravanTypeLabel(type)}. Arrival in {delayDays:F1} days.";
            }
            else
            {
                eventSuccess = DiplomacyEventManager.TriggerCaravanEvent(faction, type);
                resultMessage = $"Trade caravan requested from {faction.Name}: {DiplomacyEventManager.GetCaravanTypeLabel(type)}";
            }

            return APIResult.SuccessResult(
                resultMessage,
                new { Faction = faction.Name, CaravanType = type.ToString(), EventSuccess = eventSuccess, Delayed = delayed }
            );
        }

        public APIResult ApplySuccessfulDialogueApiGoodwillCost(
            Faction faction,
            DialogueGoodwillCost.DialogueActionType actionType,
            string sourceAction = "",
            string detail = "")
        {
            EnsureInitialized();
            if (faction == null) return APIResult.FailureResult("Faction cannot be null");

            int baseCost = DialogueGoodwillCost.GetBaseValue(actionType);
            int oldGoodwill = faction.PlayerGoodwill;
            faction.TryAffectGoodwillWith(Faction.OfPlayer, baseCost, false, true, null);
            int newGoodwill = faction.PlayerGoodwill;
            int actualChange = newGoodwill - oldGoodwill;
            int currentDayAdjustment = _goodwillAdjustmentsToday.ContainsKey(faction) ? _goodwillAdjustmentsToday[faction] : 0;
            _goodwillAdjustmentsToday[faction] = currentDayAdjustment + actualChange;
            RecordDialogueAction(faction, actionType, actualChange);
            RecordAPICall(
                "ApplySuccessfulDialogueApiGoodwillCost",
                true,
                $"faction={faction.Name}, sourceAction={sourceAction}, actionType={actionType}, baseCost={baseCost}, actualChange={actualChange}, detail={detail}");

            return APIResult.SuccessResult(
                $"Fixed goodwill cost applied: {actualChange}.",
                new DialogueApiGoodwillCostResult
                {
                    SourceAction = sourceAction ?? string.Empty,
                    Detail = detail ?? string.Empty,
                    ActionType = actionType,
                    BaseCost = baseCost,
                    ActualChange = actualChange,
                    OldGoodwill = oldGoodwill,
                    NewGoodwill = newGoodwill
                });
        }

        #endregion

        #region 核心API方法 - 状态查询

        /// <summary>/// getfaction详细信息
 ///</summary>
        /// <param name="faction">目标faction</param>
        /// <returns>API调用result, 包含faction详细信息</returns>
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

        /// <summary>/// get所有可用faction列表
 ///</summary>
        /// <returns>API调用result, 包含faction列表</returns>
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

        /// <summary>/// getcolonystate信息
 ///</summary>
        /// <returns>API调用result, 包含colonystate</returns>
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

        /// <summary>/// 触发specificevent (Incident)
 ///</summary>
        public APIResult TriggerIncident(Faction faction, string incidentDefName, float points = -1)
        {
            if (faction == null)
                return APIResult.FailureResult("Faction cannot be null");

            IncidentDef incDef = DefDatabase<IncidentDef>.GetNamedSilentFail(incidentDefName);
            if (incDef == null)
                return APIResult.FailureResult($"Invalid IncidentDef: {incidentDefName}");

            Map map = Find.CurrentMap;
            if (map == null)
                return APIResult.FailureResult("No valid map to trigger incident");

            IncidentParms parms = StorytellerUtility.DefaultParmsNow(incDef.category, map);
            parms.faction = faction;
            if (points > 0) parms.points = points;

            try
            {
                if (incDef.Worker.TryExecute(parms))
                {
                    RecordAPICall("TriggerIncident", true, $"faction={faction.Name}, incident={incidentDefName}, points={points}");
                    WorldEventLedgerComponent.Instance?.RecordIncidentIntent(faction, incidentDefName, map);
                    return APIResult.SuccessResult($"Incident triggered: {incDef.label}");
                }
                else
                {
                    return APIResult.FailureResult($"Incident worker failed to execute: {incidentDefName}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Error triggering incident {incidentDefName}: {ex}");
                return APIResult.FailureResult($"Execution error: {ex.Message}");
            }
        }

        /// <summary>/// 创建并向玩家发布一个自定义任务 (简单包装)
 ///</summary>
        public APIResult CreateSimpleQuest(Faction faction, string title, string description, string rewardDescription, string callbackId, int durationTicks = 60000)
        {
            var parameters = new Dictionary<string, object>
            {
                { "title", title },
                { "description", description },
                { "rewardDescription", rewardDescription },
                { "callbackId", callbackId },
                { "askerFaction", faction },
                { "durationTicks", durationTicks }
            };

            var result = CreateQuest("RimChat_AIQuest", parameters);
            // Cooldown is set inside CreateQuest if successful
            return result;
        }

        /// <summary>/// 通用任务创建method, 支持原版任务template
 ///</summary>
        /// <param name="questDefName">任务templatename (QuestScriptDef)</param>
        /// <param name="parameters">任务参数 (将存入 Slate)</param>
        public APIResult CreateQuest(string questDefName, Dictionary<string, object> parameters)
        {
            if (string.IsNullOrEmpty(questDefName))
                return APIResult.FailureResult("Quest defName cannot be null");

            bool isItemStashQuest = string.Equals(questDefName, "OpportunitySite_ItemStash", StringComparison.Ordinal);

            // 1. get目标factioncontext (预先解析以确保后续逻辑可用)
            Faction faction = null;
            if (parameters.TryGetValue("askerFaction", out object fObj))
            {
                if (fObj is Faction f) faction = f;
                else if (fObj is string s) faction = ResolveParameter("faction", s) as Faction;
            }
            
            if (faction == null && parameters.TryGetValue("faction", out object fObj2))
            {
                if (fObj2 is Faction f2) faction = f2;
                else if (fObj2 is string s2) faction = ResolveParameter("faction", s2) as Faction;
            }

            if (faction == null)
            {
                Log.Warning($"[RimChat] CreateQuest: Could not resolve faction from parameters. Quest '{questDefName}' might fallback to Empire.");
            }
            else if (RimChatMod.Instance?.InstanceSettings?.EnableDebugLogging ?? false)
            {
                Log.Message($"[RimChat] CreateQuest: Using faction context '{faction.Name}' (Def: {faction.def.defName})");
            }

            // 2. 严格校验: 不再做任务重定向, 失败直接返回
            var questValidation = ApiActionEligibilityService.Instance.ValidateCreateQuest(faction, questDefName, parameters);
            if (!questValidation.Allowed)
            {
                Log.Warning($"[RimChat] CreateQuest denied. def='{questDefName}', faction='{faction?.Name ?? "Unknown"}', code='{questValidation.Code}', message='{questValidation.Message}'");
                return APIResult.FailureResult(questValidation.Message);
            }
            questDefName = questValidation.NormalizedQuestDefName;

            QuestScriptDef questDef = DefDatabase<QuestScriptDef>.GetNamedSilentFail(questDefName);
            if (questDef == null)
                return APIResult.FailureResult($"Quest template '{questDefName}' missing");

            try
            {
                RimWorld.QuestGen.Slate slate = new RimWorld.QuestGen.Slate();
                
                // 预processing并settings参数
                foreach (var kvp in parameters)
                {
                    if (isItemStashQuest && string.Equals(kvp.Key, "siteFaction", StringComparison.OrdinalIgnoreCase))
                    {
                        // OpportunitySite_ItemStash must let vanilla resolver pick siteFaction.
                        continue;
                    }

                    if (kvp.Value == null) continue;

                    object resolvedValue = ResolveParameter(kvp.Key, kvp.Value);
                    slate.Set(kvp.Key, resolvedValue);
                }

                // 自动补全必要参数 (如果未提供)
                if (!slate.Exists("map"))
                {
                    slate.Set("map", Find.CurrentMap ?? Find.AnyPlayerHomeMap);
                }
                
                // 自动提供 Faction context (settings多个别名以compatibility不同原版脚本)
                if (faction != null)
                {
                    if (!slate.Exists("faction")) slate.Set("faction", faction);
                    if (!slate.Exists("askerFaction")) slate.Set("askerFaction", faction);
                    if (!slate.Exists("giverFaction")) slate.Set("giverFaction", faction);
                    if (!isItemStashQuest && !slate.Exists("siteFaction")) slate.Set("siteFaction", faction);
                    
                    if (!slate.Exists("enemyFaction")) 
                    {
                        // 尝试寻找一个永久敌对faction作为敌人 (某些脚本需要这个变量)
                        Faction enemy = Find.FactionManager.RandomEnemyFaction(true, true, true, TechLevel.Undefined);
                        if (enemy != null) slate.Set("enemyFaction", enemy);
                    }
                }

                // 自动提供 Settlement context (如果任务需要定居点label)
                if (!slate.Exists("settlement") && faction != null)
                {
                    // 寻找该faction最近的定居点
                    Settlement settlement = Find.WorldObjects.Settlements
                        .Where(s => s.Faction == faction)
                        .OrderBy(s => Find.WorldGrid.TraversalDistanceBetween(Find.AnyPlayerHomeMap?.Tile ?? 0, s.Tile))
                        .FirstOrDefault();
                    
                    if (settlement != null)
                    {
                        slate.Set("settlement", settlement);
                    }
                }

                // 自动提供 Asker context (factionleader或成员)
                if (!slate.Exists("asker") && faction != null)
                {
                    // 1. 优先使用定居点所属faction的leader
                    Settlement s = slate.Get<Settlement>("settlement");
                    if (s != null && s.Faction?.leader != null)
                    {
                        slate.Set("asker", s.Faction.leader);
                    }
                    // 2. 使用faction主leader
                    else if (faction.leader != null)
                    {
                        slate.Set("asker", faction.leader);
                    }
                    // 3. 回退: 随机挑选该faction的一个人类成员
                    // 仅对我们的自定义任务 (RimChat_AIQuest) enable此回退
                    // 因为原版任务通常要求 Asker 必须是 Leader 或 Royal, 乱塞人会导致 QuestDescription 解析报错
                    else if (questDefName == "RimChat_AIQuest")
                    {
                        Pawn randomPawn = PawnsFinder.AllMapsWorldAndTemporary_Alive
                            .Where(p => p.Faction == faction && p.RaceProps.Humanlike && !p.Dead)
                            .RandomElementWithFallback();
                        
                        if (randomPawn != null)
                        {
                            slate.Set("asker", randomPawn);
                        }
                    }
                    // 4. 特殊processing: OpportunitySite_ItemStash 如果没有 Leader, 必须显式settings askerIsNull
                    else if (questDefName == "OpportunitySite_ItemStash")
                    {
                        slate.Set("askerIsNull", true);
                    }
                }

                // 针对 AncientComplex_Mission 的特殊processing: 必须提供 colonistCount 和 relic
                if (questDefName == "AncientComplex_Mission")
                {
                    // ColonistCount 必须先检查并修正
                    int colonistCount = -1;
                    if (slate.Exists("colonistCount"))
                    {
                        colonistCount = slate.Get<int>("colonistCount");
                    }
                    if (colonistCount <= 0)
                    {
                        Map playerMap = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
                        int freeColonists = playerMap?.mapPawns?.FreeColonistsSpawnedCount ?? 3;
                        int count = Math.Max(2, Math.Min(freeColonists, 5));
                        slate.Set("colonistCount", count);
                        if (!slate.Exists("points"))
                        {
                            slate.Set("points", StorytellerUtility.DefaultThreatPointsNow(playerMap));
                        }
                    }

                    if (ModsConfig.IdeologyActive && !slate.Exists("relic") && Faction.OfPlayer.ideos?.PrimaryIdeo != null)
                    {
                        var relics = Faction.OfPlayer.ideos.PrimaryIdeo.PreceptsListForReading.OfType<Precept_Relic>();
                        if (relics.Any())
                        {
                            slate.Set("relic", relics.RandomElement());
                        }
                    }
                }

                // 针对 OpportunitySite_ItemStash 的特殊processing: 需要完整的factioncontext
                if (isItemStashQuest)
                {
                    Map playerMap = Find.CurrentMap ?? Find.AnyPlayerHomeMap;

                    // Keep a safer points floor because script subs can reduce points before site part selection.
                    float currentPoints = slate.Exists("points") ? slate.Get<float>("points") : 0;
                    float minPoints = Math.Max(800f, questDef.rootMinPoints);
                    if (currentPoints < minPoints)
                    {
                        currentPoints = StorytellerUtility.DefaultThreatPointsNow(playerMap);
                        if (currentPoints < minPoints)
                        {
                            currentPoints = minPoints;
                        }
                        slate.Set("points", currentPoints);
                        Log.Message($"[RimChat] OpportunitySite_ItemStash: Set points to {currentPoints}");
                    }

                    if (!slate.Exists("asker"))
                    {
                        if (faction != null && faction.leader != null)
                        {
                            slate.Set("asker", faction.leader);
                            slate.Set("asker_factionLeader", true);
                        }
                        else
                        {
                            slate.Set("askerIsNull", true);
                        }
                    }
                }

                // 针对 Mission_BanditCamp 的特殊processing
                // 注意: 此任务由 QuestNode_Root_Mission_BanditCamp processing, 它会自动计算 requiredPawnCount
                if (questDefName == "Mission_BanditCamp")
                {
                    Map playerMap = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
                    
                    // EnemyFaction 必须是海盗faction (根据原版 XML 定义)
                    Faction enemyFaction = null;
                    if (slate.Exists("enemyFaction"))
                    {
                        enemyFaction = slate.Get<Faction>("enemyFaction");
                    }
                    if (enemyFaction == null)
                    {
                        enemyFaction = Find.FactionManager.RandomEnemyFaction(true, true, true, TechLevel.Undefined);
                        if (enemyFaction != null)
                        {
                            slate.Set("enemyFaction", enemyFaction);
                        }
                    }
                    
                    // EnemiesLabel 是 Grammar 变量, 需要直接settings
                    if (enemyFaction != null && !slate.Exists("enemiesLabel"))
                    {
                        slate.Set("enemiesLabel", enemyFaction.Name);
                    }
                    
                    // TimeoutTicks used for任务时限
                    if (!slate.Exists("timeoutTicks"))
                    {
                        slate.Set("timeoutTicks", Rand.RangeInclusive(10, 30) * 60000); // 10-30 天
                    }
                    
                    // Points used for威胁点数
                    if (!slate.Exists("points"))
                    {
                        slate.Set("points", StorytellerUtility.DefaultThreatPointsNow(playerMap));
                    }
                }

                // 注入factionname, 确保 [faction_name] 能解析
                if (slate.Exists("faction") && !slate.Exists("faction_name"))
                {
                    slate.Set("faction_name", slate.Get<Faction>("faction").Name);
                }

                if (!slate.Exists("points") && questDef.rootMinPoints > 0)
                {
                    slate.Set("points", StorytellerUtility.DefaultThreatPointsNow(Find.CurrentMap ?? Find.AnyPlayerHomeMap));
                }

                // --- 确保 AI 任务有基本参数, 防止 Grammar 解析失败 ---
                if (questDefName == "RimChat_AIQuest")
                {
                    if (!slate.Exists("title"))
                        slate.Set("title", $"Task from {faction?.Name ?? "Unknown"}");
                    
                    if (!slate.Exists("description"))
                        slate.Set("description", $"We have received a communication from {faction?.Name ?? "Unknown"}. (AI failed to generate description)");
                }

                // --- 锁定核心变量, 开始生成 ---
                Quest quest;
                try
                {
                    RimChat.Patches.QuestGenPatch.LockSlateVariables = true;
                    quest = RimWorld.QuestGen.QuestGen.Generate(questDef, slate);
                }
                finally
                {
                    RimChat.Patches.QuestGenPatch.LockSlateVariables = false;
                }

                // QuestGen 在某些站点参数异常场景会仅record Error 而不抛出异常.
                // 这里做二次硬校验, 避免“报错后仍当作successfully”的伪successfullypath.
                if (questDefName == "OpportunitySite_ItemStash")
                {
                    object sitePartsParams = slate.Exists("sitePartsParams") ? slate.Get<object>("sitePartsParams") : null;
                    if (sitePartsParams == null)
                    {
                        Log.Error($"[RimChat] CreateQuest failed post-validation: sitePartsParams is null. def='{questDefName}', faction='{faction?.Name ?? "Unknown"}'.");
                        Log.Warning($"[RimChat] CreateQuest technical failure. def='{questDefName}', faction='{faction?.Name ?? "Unknown"}'. No fallback quest will be generated.");
                        return APIResult.FailureResult("Quest generation error: invalid sitePartsParams generated for site quest.");
                    }
                }

                Find.QuestManager.Add(quest);
                RimWorld.QuestUtility.SendLetterQuestAvailable(quest);

                string logMsg = $"Quest '{questDefName}' created";

                RecordAPICall("CreateQuest", true, $"defName={questDefName}, paramsCount={parameters.Count}");
                
                // Add Cooldown after a successfully created quest
                SetCooldown(faction, "CreateQuest");
                
                return APIResult.SuccessResult(
                    logMsg,
                    new
                    {
                        QuestDefName = questDefName,
                        Faction = faction?.Name ?? "Unknown"
                    });
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Error creating quest {questDefName}: {ex}");
                Log.Warning($"[RimChat] CreateQuest technical failure. def='{questDefName}', faction='{faction?.Name ?? "Unknown"}'. No fallback quest will be generated.");
                return APIResult.FailureResult($"Quest generation error: {ex.Message}");
            }
        }

        /// <summary>/// compatibility保留: 严格校验迁移到 ApiActionEligibilityService, 不再重定向任务.
 ///</summary>
        private string ValidateAndFixQuestDef(string questDefName, Faction faction)
        {
            return questDefName;
        }

        /// <summary>/// 将 AI 传入的字符串/基础类型参数解析为 RimWorld 对象
 ///</summary>
        private object ResolveParameter(string key, object value)
        {
            if (value == null) return null;

            // 如果已经是目标类型, 直接返回
            if (!(value is string strValue)) return value;

            // Processing Faction 解析
            if (key.ToLower().Contains("faction"))
            {
                Faction faction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.Name == strValue || f.def.defName == strValue);
                if (faction != null) return faction;
            }

            // Processing Pawn 解析 (通过名字)
            if (key.ToLower().Contains("pawn") || key.ToLower() == "asker")
            {
                Pawn pawn = PawnsFinder.AllMapsWorldAndTemporary_Alive.FirstOrDefault(p => p.Name != null && p.Name.ToStringFull == strValue);
                if (pawn != null) return pawn;
            }

            // Processing数字解析 (防御性)
            if (float.TryParse(strValue, out float fResult)) return fResult;

            return value;
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

        /// <summary>/// 检查factionspecificmethodwhether处于冷却中
 ///</summary>
        /// <param name="faction">目标faction</param>
        /// <param name="methodName">method名</param>
        /// <param name="cooldownTicks">冷却tick数</param>
        /// <returns>whether可用</returns>
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

        /// <summary>/// settingsfactionspecificmethod冷却
 ///</summary>
        /// <param name="faction">目标faction</param>
        /// <param name="methodName">method名</param>
        private void SetCooldown(Faction faction, string methodName)
        {
            InitializeCooldownsIfNeeded();

            if (RimChatMod.Instance == null) return;
            var settings = RimChatMod.Instance.InstanceSettings;
            int cooldownTicks;
            if (methodName == "CreateQuest")
            {
                int minDays = settings?.MinQuestCooldownDays ?? 7;
                int maxDays = settings?.MaxQuestCooldownDays ?? 12;
                float randomDays = Rand.Range(minDays, maxDays);
                cooldownTicks = (int)(randomDays * 60000);
            }
            else
            {
                cooldownTicks = methodName switch
                {
                    "AdjustGoodwill" => settings?.GoodwillCooldownTicks ?? 2500,
                    "SendGift" => settings?.GiftCooldownTicks ?? 60000,
                    "RequestAid" => AidFactionCooldownTicks,
                    "DeclareWar" => settings?.WarCooldownTicks ?? 60000,
                    "MakePeace" => settings?.PeaceCooldownTicks ?? 60000,
                    "RequestTradeCaravan" => CaravanFactionCooldownTicks,
                    "RequestRaid" => settings?.RaidCooldownTicks ?? 180000,
                    "RequestRaidWaves" => 5 * 60000, // 5天冷却
                    "RequestItemAirdrop" => settings?.ItemAirdropCooldownTicks ?? 180000,
                    _ => 2500
                };
            }

            var factionCooldowns = GetOrCreateFactionCooldowns(faction);
            if (factionCooldowns != null && Find.TickManager != null)
                factionCooldowns[methodName] = Find.TickManager.TicksGame + cooldownTicks;
        }

        /// <summary>/// getfactionspecificmethod的剩余冷却时间 (秒)
 ///</summary>
        /// <param name="faction">目标faction</param>
        /// <param name="methodName">method名</param>
        /// <returns>剩余冷却秒数, 0表示可用</returns>
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

        /// <summary>/// get指定faction的冷却state概览
 ///</summary>
        /// <param name="faction">目标faction</param>
        /// <returns>各method的剩余冷却时间字典</returns>
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

        #region 全局袭击冷却 (request_raid_call_everyone)

        /// <summary>/// 获取 request_raid_call_everyone 剩余冷却秒数
        ///</summary>
        public int GetRaidCallEveryoneRemainingCooldownSeconds()
        {
            if (Find.TickManager == null) return 0;
            int remaining = _raidCallEveryoneNextAvailableTick - Find.TickManager.TicksGame;
            return Math.Max(0, remaining / 60);
        }

        /// <summary>/// 设置 request_raid_call_everyone 全局冷却 (15天)
        ///</summary>
        public void SetRaidCallEveryoneCooldown()
        {
            if (Find.TickManager == null) return;
            _raidCallEveryoneNextAvailableTick = Find.TickManager.TicksGame + (15 * GenDate.TicksPerDay);
        }

        /// <summary>/// 检查 request_raid_call_everyone 是否可用
        ///</summary>
        public bool IsRaidCallEveryoneAvailable()
        {
            return GetRaidCallEveryoneRemainingCooldownSeconds() <= 0;
        }

        /// <summary>/// 设置派系特定方法的冷却时间（公共接口）
        ///</summary>
        public void SetFactionCooldown(Faction faction, string methodName)
        {
            SetCooldown(faction, methodName);
        }

        #endregion

        #endregion

        #region 安全机制 - 记录与日志

        /// <summary>/// record API 调用
 ///</summary>
        /// <param name="methodName">method名</param>
        /// <param name="success">whethersuccessfully</param>
        /// <param name="parameters">参数</param>
        /// <param name="errorMessage">error信息</param>
        private void RecordAPICall(string methodName, bool success, string parameters, string errorMessage = "")
        {
            try
            {
                EnsureInitialized();
                
                // 检查游戏whether已initialize
                if (Find.TickManager == null)
                {
                    // 游戏未完全initialize, 跳过record
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

                // 调试log
                if (RimChatMod.Instance != null && (RimChatMod.Instance.InstanceSettings?.EnableDebugLogging ?? false))
                {
                    string status = success ? "SUCCESS" : "FAILED";
                    Log.Message($"[RimChat] API Call [{status}]: {methodName} - {parameters}");
                }
            }
            catch (Exception ex)
            {
                // 防止record过程中的任何异常影响主流程
                Log.Error($"[RimChat] Failed to record API call: {ex.Message}");
            }
        }

        /// <summary>/// get API 调用历史
 ///</summary>
        /// <param name="methodName">method名过滤 (可选) </param>
        /// <param name="maxRecords">最大record数</param>
        /// <returns>API 调用record列表</returns>
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

        private bool TryApplyRelationTargetGoodwill(
            Faction faction,
            int targetGoodwill,
            FactionRelationKind expectedRelation,
            out int appliedGoodwill,
            out string failureReason)
        {
            appliedGoodwill = faction?.PlayerGoodwill ?? 0;
            failureReason = string.Empty;
            Faction player = Faction.OfPlayer;
            if (faction == null || player == null)
            {
                failureReason = "Faction or player faction is unavailable.";
                return false;
            }

            int currentGoodwill = faction.PlayerGoodwill;
            int goodwillDelta = targetGoodwill - currentGoodwill;
            bool goodwillApplied = goodwillDelta == 0 ||
                                   faction.TryAffectGoodwillWith(player, goodwillDelta, false, true, null);
            appliedGoodwill = faction.PlayerGoodwill;
            if (goodwillApplied &&
                appliedGoodwill == targetGoodwill &&
                faction.RelationKindWith(player) == expectedRelation)
            {
                return true;
            }

            if (goodwillApplied)
            {
                failureReason =
                    $"goodwill_target_miss(current={currentGoodwill}, target={targetGoodwill}, applied={appliedGoodwill}, relation={faction.RelationKindWith(player)})";
                return false;
            }

            if (faction.HasGoodwill)
            {
                failureReason =
                    $"goodwill_apply_failed(current={currentGoodwill}, target={targetGoodwill}, relation={faction.RelationKindWith(player)})";
                return false;
            }

            try
            {
                faction.SetRelationDirect(player, expectedRelation);
            }
            catch (Exception ex)
            {
                failureReason = $"goodwill_apply_failed_and_set_relation_failed({ex.Message})";
                return false;
            }

            appliedGoodwill = faction.PlayerGoodwill;
            if (faction.RelationKindWith(player) == expectedRelation)
            {
                return true;
            }

            failureReason =
                $"relation_target_miss(current={faction.RelationKindWith(player)}, expected={expectedRelation}, appliedGoodwill={appliedGoodwill})";
            return false;
        }

        /// <summary>/// 通知重大goodwill变化
 ///</summary>
        private void NotifySignificantGoodwillChange(Faction faction, int oldGoodwill, int newGoodwill, string reason)
        {
            int change = newGoodwill - oldGoodwill;
            string titleKey = change > 0
                ? "RimChat_GoodwillImprovedLetterTitle"
                : "RimChat_GoodwillWorsenedLetterTitle";
            string messageKey = change > 0
                ? "RimChat_GoodwillImprovedLetterBody"
                : "RimChat_GoodwillWorsenedLetterBody";
            LetterDef letterDef = change > 0 ? LetterDefOf.PositiveEvent : LetterDefOf.NegativeEvent;

            Find.LetterStack.ReceiveLetter(
                titleKey.Translate(),
                messageKey.Translate(faction.Name, Math.Abs(change), reason ?? string.Empty),
                letterDef);
        }

        /// <summary>/// 验证AIwhether有权限操作指定faction
        ///</summary>
        /// <param name="faction">目标faction</param>
        /// <returns>whether有权限</returns>
        public bool ValidateAIPermission(Faction faction)
        {
            if (faction == null) return false;
            if (faction.IsPlayer) return false;
            if (faction.defeated) return false;
            if (faction.def?.hidden == true)
            {
                if (GameComponent_DiplomacyManager.Instance?.IsHiddenFactionManuallyVisible(faction) != true)
                {
                    return false;
                }
            }

            return true;
        }

        #endregion

        #region 对话行为好感度消耗系统

        /// <summary>/// 执行dialoguebehavior并applygoodwill消耗/收益
 ///</summary>
        /// <param name="faction">目标faction</param>
        /// <param name="actionType">behavior类型</param>
        /// <returns>执行result</returns>
        public APIResult ExecuteDialogueAction(Faction faction, DialogueGoodwillCost.DialogueActionType actionType)
        {
            EnsureInitialized();

            if (faction == null)
                return APIResult.FailureResult("Faction cannot be null");

            // 1. 检查冷却时间
            if (!CheckDialogueActionCooldown(faction, actionType))
            {
                int remainingTicks = GetDialogueActionCooldownRemaining(faction, actionType);
                float remainingHours = remainingTicks / 2500f;
                return APIResult.FailureResult($"Action is on cooldown. Remaining: {remainingHours:F1} hours");
            }

            // 2. 检查每日限制
            if (!CheckDailyDialogueLimit(faction, actionType, out string limitReason))
            {
                return APIResult.FailureResult($"Daily limit reached: {limitReason}");
            }

            // 3. 计算实际goodwill变化
            int goodwillChange = DialogueGoodwillCost.GetBaseValue(actionType);

            // 4. 执行goodwill变化
            if (goodwillChange != 0)
            {
                int oldGoodwill = faction.PlayerGoodwill;
                faction.TryAffectGoodwillWith(Faction.OfPlayer, goodwillChange, false, true, null);
                int newGoodwill = faction.PlayerGoodwill;
                int actualChange = newGoodwill - oldGoodwill;

                // Record到今日调整
                int currentDayAdjustment = _goodwillAdjustmentsToday.ContainsKey(faction) ? _goodwillAdjustmentsToday[faction] : 0;
                _goodwillAdjustmentsToday[faction] = currentDayAdjustment + actualChange;

                // Recordbehavior
                RecordDialogueAction(faction, actionType, actualChange);

                // Settings冷却
                SetDialogueActionCooldown(faction, actionType);

                // RecordAPI调用
                RecordAPICall("ExecuteDialogueAction", true, 
                    $"faction={faction.Name}, action={actionType}, change={actualChange}");

                // 触发通知 (重大变化)
                if (Math.Abs(actualChange) >= 5)
                {
                    NotifyDialogueActionResult(faction, actionType, actualChange, goodwillChange);
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
                        BaseValue = goodwillChange
                    }
                );
            }
            else
            {
                // 无goodwill变化但仍recordbehavior
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

        /// <summary>/// 预览dialoguebehavior的goodwill消耗 (不执行)
 ///</summary>
        public APIResult PreviewDialogueActionCost(Faction faction, DialogueGoodwillCost.DialogueActionType actionType)
        {
            if (faction == null)
                return APIResult.FailureResult("Faction cannot be null");

            // 检查whether可执行
            bool canExecute = true;
            string reason = string.Empty;

            // 计算消耗
            int cost = DialogueGoodwillCost.GetBaseValue(actionType);

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
                    BaseCost = cost,
                    FinalCost = cost,
                    OnCooldown = onCooldown,
                    RemainingCooldownTicks = remainingCooldown,
                    RemainingCooldownHours = remainingCooldown / 2500f,
                    WithinDailyLimit = withinLimit,
                    DailyLimitReason = limitReason,
                    CurrentGoodwill = faction.PlayerGoodwill,
                    ExpectedGoodwillAfter = faction.PlayerGoodwill + cost
                }
            );
        }

        /// <summary>/// 检查dialoguebehavior冷却
 ///</summary>
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

        /// <summary>/// getdialoguebehavior剩余冷却时间
 ///</summary>
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

        /// <summary>/// settingsdialoguebehavior冷却
 ///</summary>
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

        /// <summary>/// 检查每日dialoguebehavior限制
 ///</summary>
        private bool CheckDailyDialogueLimit(Faction faction, DialogueGoodwillCost.DialogueActionType actionType, out string reason)
        {
            EnsureInitialized();
            reason = "";

            int baseValue = DialogueGoodwillCost.GetBaseValue(actionType);
            bool isCostAction = baseValue < 0;

            // 计算今日该faction的累计消耗/收益
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

            // 检查whether超出限制
            if (isCostAction)
            {
                int expectedCost = Math.Abs(DialogueGoodwillCost.GetBaseValue(actionType));
                if (todayCost + expectedCost > Math.Abs(DialogueGoodwillCost.DailyCostLimit))
                {
                    reason = $"今日消耗已达上限 ({todayCost}/{Math.Abs(DialogueGoodwillCost.DailyCostLimit)})";
                    return false;
                }
            }
            else
            {
                int expectedGain = DialogueGoodwillCost.GetBaseValue(actionType);
                if (todayGain + expectedGain > DialogueGoodwillCost.DailyGainLimit)
                {
                    reason = $"今日收益已达上限 ({todayGain}/{DialogueGoodwillCost.DailyGainLimit})";
                    return false;
                }
            }

            return true;
        }

        /// <summary>/// recorddialoguebehavior
 ///</summary>
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

        /// <summary>/// get今日dialoguebehavior统计
 ///</summary>
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

        /// <summary>/// 通知dialoguebehaviorresult
 ///</summary>
        private void NotifyDialogueActionResult(Faction faction, DialogueGoodwillCost.DialogueActionType actionType, int change, int baseValue)
        {
            string actionLabel = DialogueGoodwillCost.GetActionLabelKey(actionType).Translate();
            string titleKey;
            string messageKey;
            LetterDef letterDef;

            if (change < 0)
            {
                titleKey = "RimChat_DialogueActionCostLetterTitle";
                messageKey = "RimChat_DialogueActionCostLetterBody";
                letterDef = LetterDefOf.NegativeEvent;
            }
            else
            {
                titleKey = "RimChat_DialogueActionGainLetterTitle";
                messageKey = "RimChat_DialogueActionGainLetterBody";
                letterDef = LetterDefOf.PositiveEvent;
            }

            Find.LetterStack.ReceiveLetter(
                titleKey.Translate(),
                messageKey.Translate(
                    faction.Name,
                    actionLabel,
                    Math.Abs(baseValue),
                    Math.Abs(change)),
                letterDef);
        }

        #endregion

        #region 袭击波次状态

        /// <summary>/// 袭击波次状态，用于序列化持续袭击调度
        ///</summary>
        public class RaidWaveState : IExposable
        {
            public string SourceFactionDefName;
            public int WavesRemaining;
            public int NextWaveTick;
            public int MinIntervalTicks = 12 * 2500;  // 12小时
            public int MaxIntervalTicks = 20 * 2500;  // 20小时

            public void ExposeData()
            {
                Scribe_Values.Look(ref SourceFactionDefName, "sourceFactionDefName", "");
                Scribe_Values.Look(ref WavesRemaining, "wavesRemaining", 0);
                Scribe_Values.Look(ref NextWaveTick, "nextWaveTick", 0);
                Scribe_Values.Look(ref MinIntervalTicks, "minIntervalTicks", 12 * 2500);
                Scribe_Values.Look(ref MaxIntervalTicks, "maxIntervalTicks", 20 * 2500);
            }
        }

        #endregion
    }
}
