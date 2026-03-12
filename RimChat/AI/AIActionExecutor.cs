using System;
using System.Collections.Generic;
using System.Globalization;
using RimWorld;
using Verse;
using RimChat.DiplomacySystem;
using RimChat.Core;
using RimChat.Config;
using RimChat.Relation;
using RimChat.Util;

namespace RimChat.AI
{
    /// <summary>/// AI动作executor
 /// 执行LLM解析出的API调用动作
 ///</summary>
    public class AIActionExecutor
    {
        private readonly Faction faction;
        private readonly GameAIInterface gameInterface;
        private readonly bool applyDialogueApiGoodwillCost;

        public AIActionExecutor(Faction faction, bool applyDialogueApiGoodwillCost = false)
        {
            this.faction = faction;
            this.gameInterface = GameAIInterface.Instance;
            this.applyDialogueApiGoodwillCost = applyDialogueApiGoodwillCost;
        }

        /// <summary>/// 执行AI动作
 ///</summary>
        /// <param name="action">要执行的动作</param>
        /// <returns>执行result</returns>
        public ActionResult ExecuteAction(AIAction action)
        {
            if (action == null)
            {
                return ActionResult.Failure("Action is null");
            }

            Log.Message($"[RimChat] Executing AI action: {action.ActionType}");
            if (action.Parameters == null)
            {
                action.Parameters = new Dictionary<string, object>();
            }

            // 检查AIwhether有权限操作此faction
            if (!gameInterface.ValidateAIPermission(faction))
            {
                return ActionResult.Failure("AI does not have permission to interact with this faction");
            }

            // 检查该功能whether被enable
            if (!IsFeatureEnabled(action.ActionType))
            {
                return ActionResult.Failure($"Feature {action.ActionType} is disabled in settings");
            }

            var validation = ApiActionEligibilityService.Instance.ValidateActionExecution(faction, action.ActionType, action.Parameters);
            if (!validation.Allowed)
            {
                return ActionResult.Failure(validation.Message);
            }

            try
            {
                ActionResult result = action.ActionType switch
                {
                    AIActionNames.AdjustGoodwill => ExecuteAdjustGoodwill(action),
                    AIActionNames.SendGift => ExecuteSendGift(action),
                    AIActionNames.RequestAid => ExecuteRequestAid(action),
                    AIActionNames.DeclareWar => ExecuteDeclareWar(action),
                    AIActionNames.MakePeace => ExecuteMakePeace(action),
                    AIActionNames.RequestCaravan => ExecuteRequestCaravan(action),
                    AIActionNames.RequestRaid => ExecuteRequestRaid(action),
                    AIActionNames.RejectRequest => ExecuteRejectRequest(action),
                    AIActionNames.TriggerIncident => ExecuteTriggerIncident(action),
                    AIActionNames.CreateQuest => ExecuteCreateQuest(action),
                    _ => ActionResult.Failure($"Unknown action type: {action.ActionType}")
                };

                return ApplyDialogueApiGoodwillCostIfNeeded(action, result);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Error executing action {action.ActionType}: {ex}");
                return ActionResult.Failure($"Execution error: {ex.Message}");
            }
        }

        private ActionResult ApplyDialogueApiGoodwillCostIfNeeded(AIAction action, ActionResult result)
        {
            if (!applyDialogueApiGoodwillCost || action == null || result == null || !result.IsSuccess)
            {
                return result;
            }

            DialogueGoodwillCost.DialogueActionType? costType = action.ActionType switch
            {
                AIActionNames.RequestAid => ResolveAidDialogueCostType(action),
                AIActionNames.RequestCaravan => DialogueGoodwillCost.DialogueActionType.RequestCaravan,
                AIActionNames.CreateQuest => DialogueGoodwillCost.DialogueActionType.CreateQuest,
                _ => null
            };

            if (!costType.HasValue)
            {
                return result;
            }

            string detail = BuildDialogueApiCostDetail(action);
            var costResult = gameInterface.ApplySuccessfulDialogueApiGoodwillCost(faction, costType.Value, action.ActionType, detail);
            if (!costResult.Success)
            {
                Log.Warning($"[RimChat] Fixed dialogue API goodwill cost failed for {action.ActionType}: {costResult.Message}");
                return result;
            }

            var costData = costResult.Data as GameAIInterface.DialogueApiGoodwillCostResult;
            result.Data = new ActionExecutionDetails
            {
                ApiData = result.Data,
                DialogueCost = costData
            };

            if (!string.IsNullOrWhiteSpace(costResult.Message))
            {
                result.Message = $"{result.Message} {costResult.Message}".Trim();
            }

            return result;
        }

        private static string BuildDialogueApiCostDetail(AIAction action)
        {
            if (action?.Parameters == null)
            {
                return string.Empty;
            }

            return action.ActionType switch
            {
                AIActionNames.RequestAid => ReadDetail(action.Parameters, "type"),
                AIActionNames.RequestCaravan => ReadDetail(action.Parameters, "type", "goods"),
                AIActionNames.CreateQuest => ReadDetail(action.Parameters, "questDefName"),
                _ => string.Empty
            };
        }

        private static DialogueGoodwillCost.DialogueActionType ResolveAidDialogueCostType(AIAction action)
        {
            string aidType = ReadStringParameterOrDefault(action?.Parameters, "type", "Military");
            switch ((aidType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "medical":
                    return DialogueGoodwillCost.DialogueActionType.RequestMedicalAid;
                case "resources":
                case "resource":
                    return DialogueGoodwillCost.DialogueActionType.RequestResourceAid;
                default:
                    return DialogueGoodwillCost.DialogueActionType.RequestMilitaryAid;
            }
        }

        private static string ReadDetail(Dictionary<string, object> parameters, params string[] keys)
        {
            if (parameters == null || keys == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < keys.Length; i++)
            {
                if (parameters.TryGetValue(keys[i], out object value) && value != null)
                {
                    string text = value.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }

            return string.Empty;
        }

        /// <summary>/// 检查功能whetherenable
 ///</summary>
        private bool IsFeatureEnabled(string actionType)
        {
            if (RimChatMod.Instance == null) return false;
            var settings = RimChatMod.Instance.InstanceSettings;
            if (settings == null) return false;

            return actionType switch
            {
                AIActionNames.AdjustGoodwill => settings.EnableAIGoodwillAdjustment,
                AIActionNames.SendGift => settings.EnableAIGiftSending,
                AIActionNames.RequestAid => settings.EnableAIAidRequest,
                AIActionNames.DeclareWar => settings.EnableAIWarDeclaration,
                AIActionNames.MakePeace => settings.EnableAIPeaceMaking,
                AIActionNames.RequestCaravan => settings.EnableAITradeCaravan,
                AIActionNames.RequestRaid => settings.EnableAIRaidRequest,
                AIActionNames.RejectRequest => true, // 拒绝request总是允许
                AIActionNames.TriggerIncident => true, // 默认允许触发event, 可以通过prompt控制
                AIActionNames.CreateQuest => true, // 默认允许创建任务
                AIActionNames.ExitDialogue => settings.EnableFactionPresenceStatus,
                AIActionNames.GoOffline => settings.EnableFactionPresenceStatus,
                AIActionNames.SetDnd => settings.EnableFactionPresenceStatus,
                _ => false
            };
        }

        /// <summary>/// 执行触发event
 ///</summary>
        private ActionResult ExecuteTriggerIncident(AIAction action)
        {
            if (!action.Parameters.TryGetValue("defName", out object defNameObj) || string.IsNullOrEmpty(defNameObj?.ToString()))
            {
                return ActionResult.Failure("Missing 'defName' parameter for TriggerIncident");
            }

            string defName = defNameObj.ToString();
            float points = -1f;
            TryReadFloatParameter(action.Parameters, "amount", out points);

            var result = gameInterface.TriggerIncident(faction, defName, points);
            if (result.Success)
            {
                return ActionResult.Success(result.Message, result.Data);
            }
            else
            {
                return ActionResult.Failure(result.Message);
            }
        }

        /// <summary>/// 执行创建任务
 ///</summary>
        private ActionResult ExecuteCreateQuest(AIAction action)
        {
            if (!action.Parameters.TryGetValue("questDefName", out object questDefObj) || string.IsNullOrEmpty(questDefObj?.ToString()))
            {
                return ActionResult.Failure("create_quest requires parameter 'questDefName' from the currently injected allowed list.");
            }

            string questDefName = questDefObj.ToString();
            action.Parameters["askerFaction"] = faction;
            action.Parameters["faction"] = faction;

            var questValidation = ApiActionEligibilityService.Instance.ValidateCreateQuest(faction, questDefName, action.Parameters);
            if (!questValidation.Allowed)
            {
                return ActionResult.Failure(questValidation.Message);
            }

            var result = gameInterface.CreateQuest(questValidation.NormalizedQuestDefName, action.Parameters);
            return result.Success
                ? ActionResult.Success(result.Message, result.Data)
                : ActionResult.Failure(result.Message);
        }

        /// <summary>/// 执行goodwill调整
 ///</summary>
        private ActionResult ExecuteAdjustGoodwill(AIAction action)
        {
            // Get参数
            if (!TryReadIntParameter(action.Parameters, "amount", out int amount))
            {
                return ActionResult.Failure("Missing or invalid 'amount' parameter");
            }

            string reason = ReadStringParameterOrDefault(action.Parameters, "reason", "Diplomatic dialogue");

            // 检查faction独立冷却
            int cooldownSeconds = gameInterface.GetRemainingCooldownSeconds(faction, "AdjustGoodwill");
            if (cooldownSeconds > 0)
            {
                return ActionResult.Failure($"AdjustGoodwill is on cooldown for {faction.Name}. Remaining: {cooldownSeconds} seconds");
            }

            // 执行调整
            var result = gameInterface.AdjustGoodwill(faction, amount, reason);

            if (result.Success)
            {
                string detail = amount > 0 ? $"+{amount}" : amount.ToString();
                DiplomacySystem.DiplomacyNotificationManager.SendAIActionNotification(faction, DiplomacySystem.AIActionType.AdjustGoodwill, detail);
                return ActionResult.Success(result.Message, result.Data);
            }
            else
            {
                return ActionResult.Failure(result.Message);
            }
        }

        /// <summary>/// 执行发送礼物
 ///</summary>
        private ActionResult ExecuteSendGift(AIAction action)
        {
            // Get参数
            int silver = ReadIntParameterOrDefault(action.Parameters, "silver", 500);
            int goodwillGain = ReadIntParameterOrDefault(action.Parameters, "goodwill_gain", 5);

            // 检查faction独立冷却
            int cooldownSeconds = gameInterface.GetRemainingCooldownSeconds(faction, "SendGift");
            if (cooldownSeconds > 0)
            {
                return ActionResult.Failure($"SendGift is on cooldown for {faction.Name}. Remaining: {cooldownSeconds} seconds");
            }

            // 执行
            var result = gameInterface.SendGift(faction, silver, goodwillGain);

            if (result.Success)
            {
                string detail = $"{silver} {"RimChat_Silver".Translate()}";
                DiplomacySystem.DiplomacyNotificationManager.SendAIActionNotification(faction, DiplomacySystem.AIActionType.SendGift, detail);
                return ActionResult.Success(result.Message, result.Data);
            }
            else
            {
                return ActionResult.Failure(result.Message);
            }
        }

        /// <summary>/// 执行request援助
 ///</summary>
        private ActionResult ExecuteRequestAid(AIAction action)
        {
            // Get参数
            string aidType = ReadStringParameterOrDefault(action.Parameters, "type", "Military");

            // 检查relation
            if (faction.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Ally)
            {
                return ActionResult.Failure("Can only request aid from allied factions");
            }

            // 检查goodwill
            if (RimChatMod.Instance == null) return ActionResult.Failure("Mod not initialized");
            var settings = RimChatMod.Instance.InstanceSettings;
            if (faction.PlayerGoodwill < settings?.MinGoodwillForAid)
            {
                return ActionResult.Failure($"Need at least {settings.MinGoodwillForAid} goodwill to request aid");
            }

            // 检查faction独立冷却
            int cooldownSeconds = gameInterface.GetRemainingCooldownSeconds(faction, "RequestAid");
            if (cooldownSeconds > 0)
            {
                return ActionResult.Failure($"RequestAid is on cooldown for {faction.Name}. Remaining: {cooldownSeconds} seconds");
            }

            // 执行 (使用延迟mode)
            var result = gameInterface.RequestAid(faction, aidType, delayed: true);

            if (result.Success)
            {
                return ActionResult.Success(result.Message, result.Data);
            }
            else
            {
                return ActionResult.Failure(result.Message);
            }
        }

        /// <summary>/// 执行宣战
 ///</summary>
        private ActionResult ExecuteDeclareWar(AIAction action)
        {
            string reason = ReadStringParameterOrDefault(action.Parameters, "reason", "Diplomatic conflict");

            // 检查whether已经是敌对
            if (faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile)
            {
                return ActionResult.Failure("Already at war with this faction");
            }

            // 检查goodwill
            if (RimChatMod.Instance == null) return ActionResult.Failure("Mod not initialized");
            var settings = RimChatMod.Instance.InstanceSettings;
            if (faction.PlayerGoodwill > settings?.MaxGoodwillForWarDeclaration)
            {
                return ActionResult.Failure($"Cannot declare war with goodwill above {settings.MaxGoodwillForWarDeclaration}");
            }

            // 检查faction独立冷却
            int cooldownSeconds = gameInterface.GetRemainingCooldownSeconds(faction, "DeclareWar");
            if (cooldownSeconds > 0)
            {
                return ActionResult.Failure($"DeclareWar is on cooldown for {faction.Name}. Remaining: {cooldownSeconds} seconds");
            }

            // 执行
            var result = gameInterface.DeclareWar(faction, reason);

            if (result.Success)
            {
                DiplomacySystem.DiplomacyNotificationManager.SendAIActionNotification(faction, DiplomacySystem.AIActionType.DeclareWar, reason);
                return ActionResult.Success(result.Message, result.Data);
            }
            else
            {
                return ActionResult.Failure(result.Message);
            }
        }

        /// <summary>/// 执行议和
 ///</summary>
        private ActionResult ExecuteMakePeace(AIAction action)
        {
            int peaceCost = ReadIntParameterOrDefault(action.Parameters, "cost", 0);

            // 检查whether处于敌对state
            if (faction.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile)
            {
                return ActionResult.Failure("Not at war with this faction");
            }

            // 检查faction独立冷却
            int cooldownSeconds = gameInterface.GetRemainingCooldownSeconds(faction, "MakePeace");
            if (cooldownSeconds > 0)
            {
                return ActionResult.Failure($"MakePeace is on cooldown for {faction.Name}. Remaining: {cooldownSeconds} seconds");
            }

            // 执行
            var result = gameInterface.MakePeace(faction, peaceCost);

            if (result.Success)
            {
                DiplomacySystem.DiplomacyNotificationManager.SendAIActionNotification(faction, DiplomacySystem.AIActionType.MakePeace, peaceCost > 0 ? $"{peaceCost} {"RimChat_Silver".Translate()}" : "");
                return ActionResult.Success(result.Message, result.Data);
            }
            else
            {
                return ActionResult.Failure(result.Message);
            }
        }

        /// <summary>/// 执行request商队
 ///</summary>
        private ActionResult ExecuteRequestCaravan(AIAction action)
        {
            string caravanType = ReadStringParameterOrDefault(action.Parameters, "type", string.Empty);
            if (string.IsNullOrWhiteSpace(caravanType))
            {
                caravanType = ReadStringParameterOrDefault(action.Parameters, "goods", "General");
            }

            // 检查relation
            if (faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile)
            {
                return ActionResult.Failure("Cannot request caravan from hostile faction");
            }

            // 检查faction独立冷却
            int cooldownSeconds = gameInterface.GetRemainingCooldownSeconds(faction, "RequestTradeCaravan");
            if (cooldownSeconds > 0)
            {
                return ActionResult.Failure($"RequestTradeCaravan is on cooldown for {faction.Name}. Remaining: {cooldownSeconds} seconds");
            }

            // 执行 (使用延迟mode)
            var result = gameInterface.RequestTradeCaravan(faction, caravanType, delayed: true);

            if (result.Success)
            {
                return ActionResult.Success(result.Message, result.Data);
            }
            else
            {
                return ActionResult.Failure(result.Message);
            }
        }

        /// <summary>/// 执行拒绝request
 ///</summary>
        private ActionResult ExecuteRejectRequest(AIAction action)
        {
            // 拒绝request不需要调用API, 只是返回dialogue
            string reason = ReadStringParameterOrDefault(
                action.Parameters,
                "reason",
                "I cannot fulfill this request at this time.");

            DiplomacySystem.DiplomacyNotificationManager.SendAIActionNotification(faction, DiplomacySystem.AIActionType.RejectRequest, reason);
            return ActionResult.Success($"Request rejected: {reason}");
        }

        /// <summary>/// 执行request袭击
 ///</summary>
        private ActionResult ExecuteRequestRaid(AIAction action)
        {
            if (RimChatMod.Instance == null) return ActionResult.Failure("Mod not initialized");
            var settings = RimChatMod.Instance.InstanceSettings;

            // Get参数
            string rawStrategy = ReadStringParameterOrDefault(action.Parameters, "strategy", string.Empty);
            string rawArrival = ReadStringParameterOrDefault(action.Parameters, "arrival", string.Empty);
            RaidDefNameNormalizer.NormalizeRaidRequestParameters(rawStrategy, rawArrival, out string strategy, out string arrival);

            // 验证策略whetherenable
            if (!string.IsNullOrEmpty(strategy))
            {
                if (strategy.Equals("ImmediateAttack", StringComparison.OrdinalIgnoreCase) && !settings.EnableRaidStrategy_ImmediateAttack)
                    return ActionResult.Failure("Raid strategy 'ImmediateAttack' is disabled in settings");
                if (strategy.Equals("ImmediateAttackSmart", StringComparison.OrdinalIgnoreCase) && !settings.EnableRaidStrategy_ImmediateAttackSmart)
                    return ActionResult.Failure("Raid strategy 'ImmediateAttackSmart' is disabled in settings");
                if (strategy.Equals("StageThenAttack", StringComparison.OrdinalIgnoreCase) && !settings.EnableRaidStrategy_StageThenAttack)
                    return ActionResult.Failure("Raid strategy 'StageThenAttack' is disabled in settings");
                if (strategy.Equals("ImmediateAttackSappers", StringComparison.OrdinalIgnoreCase) && !settings.EnableRaidStrategy_ImmediateAttackSappers)
                    return ActionResult.Failure("Raid strategy 'ImmediateAttackSappers' is disabled in settings");
                if (strategy.Equals("Siege", StringComparison.OrdinalIgnoreCase) && !settings.EnableRaidStrategy_Siege)
                    return ActionResult.Failure("Raid strategy 'Siege' is disabled in settings");
            }

            // 验证到达方式whetherenable
            if (!string.IsNullOrEmpty(arrival))
            {
                if (arrival.Equals("EdgeWalkIn", StringComparison.OrdinalIgnoreCase) && !settings.EnableRaidArrival_EdgeWalkIn)
                    return ActionResult.Failure("Raid arrival 'EdgeWalkIn' is disabled in settings");
                if (arrival.Equals("EdgeDrop", StringComparison.OrdinalIgnoreCase) && !settings.EnableRaidArrival_EdgeDrop)
                    return ActionResult.Failure("Raid arrival 'EdgeDrop' is disabled in settings");
                if (arrival.Equals("EdgeWalkInGroups", StringComparison.OrdinalIgnoreCase) && !settings.EnableRaidArrival_EdgeWalkInGroups)
                    return ActionResult.Failure("Raid arrival 'EdgeWalkInGroups' is disabled in settings");
                if (arrival.Equals("RandomDrop", StringComparison.OrdinalIgnoreCase) && !settings.EnableRaidArrival_RandomDrop)
                    return ActionResult.Failure("Raid arrival 'RandomDrop' is disabled in settings");
                if (arrival.Equals("CenterDrop", StringComparison.OrdinalIgnoreCase) && !settings.EnableRaidArrival_CenterDrop)
                    return ActionResult.Failure("Raid arrival 'CenterDrop' is disabled in settings");
            }

            // 检查relation: 必须是敌对
            if (faction.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile)
            {
                return ActionResult.Failure("AI can only launch raids if the faction is hostile to the player");
            }

            // 检查faction独立冷却
            int cooldownSeconds = gameInterface.GetRemainingCooldownSeconds(faction, "RequestRaid");
            if (cooldownSeconds > 0)
            {
                return ActionResult.Failure($"RequestRaid is on cooldown for {faction.Name}. Remaining: {cooldownSeconds} seconds");
            }

            // 执行 (使用延迟mode, 点数自动计算为 -1)
            var result = gameInterface.RequestRaid(faction, strategy, arrival, delayed: true);

            if (result.Success)
            {
                return ActionResult.Success(result.Message, result.Data);
            }
            else
            {
                return ActionResult.Failure(result.Message);
            }
        }

        private static string ReadStringParameterOrDefault(Dictionary<string, object> parameters, string key, string defaultValue)
        {
            if (parameters == null || !parameters.TryGetValue(key, out object raw) || raw == null)
            {
                return defaultValue;
            }

            string value = raw.ToString()?.Trim();
            return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
        }

        private static int ReadIntParameterOrDefault(Dictionary<string, object> parameters, string key, int defaultValue)
        {
            return TryReadIntParameter(parameters, key, out int value) ? value : defaultValue;
        }

        private static bool TryReadIntParameter(Dictionary<string, object> parameters, string key, out int value)
        {
            value = 0;
            if (parameters == null || !parameters.TryGetValue(key, out object raw) || raw == null)
            {
                return false;
            }

            switch (raw)
            {
                case int intValue:
                    value = intValue;
                    return true;
                case long longValue when longValue <= int.MaxValue && longValue >= int.MinValue:
                    value = (int)longValue;
                    return true;
                case short shortValue:
                    value = shortValue;
                    return true;
                case byte byteValue:
                    value = byteValue;
                    return true;
                case float floatValue when !float.IsNaN(floatValue) && !float.IsInfinity(floatValue):
                    value = (int)Math.Round(floatValue, MidpointRounding.AwayFromZero);
                    return true;
                case double doubleValue when !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue):
                    value = (int)Math.Round(doubleValue, MidpointRounding.AwayFromZero);
                    return true;
                case decimal decimalValue:
                    value = decimal.ToInt32(decimal.Round(decimalValue, MidpointRounding.AwayFromZero));
                    return true;
            }

            string text = raw.ToString();
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedInvariant))
            {
                value = (int)Math.Round(parsedInvariant, MidpointRounding.AwayFromZero);
                return true;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double parsedCurrent))
            {
                value = (int)Math.Round(parsedCurrent, MidpointRounding.AwayFromZero);
                return true;
            }

            return false;
        }

        private static bool TryReadFloatParameter(Dictionary<string, object> parameters, string key, out float value)
        {
            value = 0f;
            if (parameters == null || !parameters.TryGetValue(key, out object raw) || raw == null)
            {
                return false;
            }

            switch (raw)
            {
                case float floatValue when !float.IsNaN(floatValue) && !float.IsInfinity(floatValue):
                    value = floatValue;
                    return true;
                case double doubleValue when !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue):
                    value = (float)doubleValue;
                    return true;
                case decimal decimalValue:
                    value = (float)decimalValue;
                    return true;
                case int intValue:
                    value = intValue;
                    return true;
                case long longValue:
                    value = longValue;
                    return true;
                case short shortValue:
                    value = shortValue;
                    return true;
                case byte byteValue:
                    value = byteValue;
                    return true;
            }

            string text = raw.ToString();
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            return float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }
    }

    /// <summary>/// 动作执行result
 ///</summary>
    public class ActionResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public object Data { get; set; }

        public static ActionResult Success(string message, object data = null)
        {
            return new ActionResult { IsSuccess = true, Message = message, Data = data };
        }

        public static ActionResult Failure(string message)
        {
            return new ActionResult { IsSuccess = false, Message = message };
        }
    }

    public class ActionExecutionDetails
    {
        public object ApiData { get; set; }
        public GameAIInterface.DialogueApiGoodwillCostResult DialogueCost { get; set; }
    }
}
