using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimDiplomacy
{
    /// <summary>
    /// AI动作执行器
    /// 执行LLM解析出的API调用动作
    /// </summary>
    public class AIActionExecutor
    {
        private readonly Faction faction;
        private readonly GameAIInterface gameInterface;

        public AIActionExecutor(Faction faction)
        {
            this.faction = faction;
            this.gameInterface = GameAIInterface.Instance;
        }

        /// <summary>
        /// 执行AI动作
        /// </summary>
        /// <param name="action">要执行的动作</param>
        /// <returns>执行结果</returns>
        public ActionResult ExecuteAction(AIAction action)
        {
            if (action == null)
            {
                return ActionResult.Failure("Action is null");
            }

            Log.Message($"[RimDiplomacy] Executing AI action: {action.ActionType}");

            // 检查AI是否有权限操作此派系
            if (!gameInterface.ValidateAIPermission(faction))
            {
                return ActionResult.Failure("AI does not have permission to interact with this faction");
            }

            // 检查该功能是否被启用
            if (!IsFeatureEnabled(action.ActionType))
            {
                return ActionResult.Failure($"Feature {action.ActionType} is disabled in settings");
            }

            try
            {
                return action.ActionType switch
                {
                    "adjust_goodwill" => ExecuteAdjustGoodwill(action),
                    "send_gift" => ExecuteSendGift(action),
                    "request_aid" => ExecuteRequestAid(action),
                    "declare_war" => ExecuteDeclareWar(action),
                    "make_peace" => ExecuteMakePeace(action),
                    "request_caravan" => ExecuteRequestCaravan(action),
                    "reject_request" => ExecuteRejectRequest(action),
                    _ => ActionResult.Failure($"Unknown action type: {action.ActionType}")
                };
            }
            catch (Exception ex)
            {
                Log.Error($"[RimDiplomacy] Error executing action {action.ActionType}: {ex}");
                return ActionResult.Failure($"Execution error: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查功能是否启用
        /// </summary>
        private bool IsFeatureEnabled(string actionType)
        {
            if (RimDiplomacyMod.Instance == null) return false;
            var settings = RimDiplomacyMod.Instance.InstanceSettings;
            if (settings == null) return false;

            return actionType switch
            {
                "adjust_goodwill" => settings.EnableAIGoodwillAdjustment,
                "send_gift" => settings.EnableAIGiftSending,
                "request_aid" => settings.EnableAIAidRequest,
                "declare_war" => settings.EnableAIWarDeclaration,
                "make_peace" => settings.EnableAIPeaceMaking,
                "request_caravan" => settings.EnableAITradeCaravan,
                "reject_request" => true, // 拒绝请求总是允许
                _ => false
            };
        }

        /// <summary>
        /// 执行好感度调整
        /// </summary>
        private ActionResult ExecuteAdjustGoodwill(AIAction action)
        {
            // 获取参数
            if (!action.Parameters.TryGetValue("amount", out object amountObj) || !(amountObj is int amount))
            {
                return ActionResult.Failure("Missing or invalid 'amount' parameter");
            }

            string reason = action.Parameters.TryGetValue("reason", out object reasonObj)
                ? reasonObj?.ToString() ?? "Diplomatic dialogue"
                : "Diplomatic dialogue";

            // 检查派系独立冷却
            int cooldownSeconds = gameInterface.GetRemainingCooldownSeconds(faction, "AdjustGoodwill");
            if (cooldownSeconds > 0)
            {
                return ActionResult.Failure($"AdjustGoodwill is on cooldown for {faction.Name}. Remaining: {cooldownSeconds} seconds");
            }

            // 执行调整
            var result = gameInterface.AdjustGoodwill(faction, amount, reason);

            if (result.Success)
            {
                return ActionResult.Success(result.Message, result.Data);
            }
            else
            {
                return ActionResult.Failure(result.Message);
            }
        }

        /// <summary>
        /// 执行发送礼物
        /// </summary>
        private ActionResult ExecuteSendGift(AIAction action)
        {
            // 获取参数
            if (!action.Parameters.TryGetValue("silver", out object silverObj) || !(silverObj is int silver))
            {
                silver = 500; // 默认值
            }

            if (!action.Parameters.TryGetValue("goodwill_gain", out object gainObj) || !(gainObj is int goodwillGain))
            {
                goodwillGain = 5; // 默认值
            }

            // 检查派系独立冷却
            int cooldownSeconds = gameInterface.GetRemainingCooldownSeconds(faction, "SendGift");
            if (cooldownSeconds > 0)
            {
                return ActionResult.Failure($"SendGift is on cooldown for {faction.Name}. Remaining: {cooldownSeconds} seconds");
            }

            // 执行
            var result = gameInterface.SendGift(faction, silver, goodwillGain);

            if (result.Success)
            {
                return ActionResult.Success(result.Message, result.Data);
            }
            else
            {
                return ActionResult.Failure(result.Message);
            }
        }

        /// <summary>
        /// 执行请求援助
        /// </summary>
        private ActionResult ExecuteRequestAid(AIAction action)
        {
            // 获取参数
            string aidType = action.Parameters.TryGetValue("type", out object typeObj)
                ? typeObj?.ToString() ?? "Military"
                : "Military";

            // 检查关系
            if (faction.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Ally)
            {
                return ActionResult.Failure("Can only request aid from allied factions");
            }

            // 检查好感度
            if (RimDiplomacyMod.Instance == null) return ActionResult.Failure("Mod not initialized");
            var settings = RimDiplomacyMod.Instance.InstanceSettings;
            if (faction.PlayerGoodwill < settings?.MinGoodwillForAid)
            {
                return ActionResult.Failure($"Need at least {settings.MinGoodwillForAid} goodwill to request aid");
            }

            // 检查派系独立冷却
            int cooldownSeconds = gameInterface.GetRemainingCooldownSeconds(faction, "RequestAid");
            if (cooldownSeconds > 0)
            {
                return ActionResult.Failure($"RequestAid is on cooldown for {faction.Name}. Remaining: {cooldownSeconds} seconds");
            }

            // 执行
            var result = gameInterface.RequestAid(faction, aidType);

            if (result.Success)
            {
                // 这里可以触发实际的援助事件
                TriggerAidEvent(aidType);
                return ActionResult.Success(result.Message, result.Data);
            }
            else
            {
                return ActionResult.Failure(result.Message);
            }
        }

        /// <summary>
        /// 执行宣战
        /// </summary>
        private ActionResult ExecuteDeclareWar(AIAction action)
        {
            string reason = action.Parameters.TryGetValue("reason", out object reasonObj)
                ? reasonObj?.ToString() ?? "Diplomatic conflict"
                : "Diplomatic conflict";

            // 检查是否已经是敌对
            if (faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile)
            {
                return ActionResult.Failure("Already at war with this faction");
            }

            // 检查好感度
            if (RimDiplomacyMod.Instance == null) return ActionResult.Failure("Mod not initialized");
            var settings = RimDiplomacyMod.Instance.InstanceSettings;
            if (faction.PlayerGoodwill > settings?.MaxGoodwillForWarDeclaration)
            {
                return ActionResult.Failure($"Cannot declare war with goodwill above {settings.MaxGoodwillForWarDeclaration}");
            }

            // 检查派系独立冷却
            int cooldownSeconds = gameInterface.GetRemainingCooldownSeconds(faction, "DeclareWar");
            if (cooldownSeconds > 0)
            {
                return ActionResult.Failure($"DeclareWar is on cooldown for {faction.Name}. Remaining: {cooldownSeconds} seconds");
            }

            // 执行
            var result = gameInterface.DeclareWar(faction, reason);

            if (result.Success)
            {
                return ActionResult.Success(result.Message, result.Data);
            }
            else
            {
                return ActionResult.Failure(result.Message);
            }
        }

        /// <summary>
        /// 执行议和
        /// </summary>
        private ActionResult ExecuteMakePeace(AIAction action)
        {
            int peaceCost = action.Parameters.TryGetValue("cost", out object costObj) && costObj is int cost
                ? cost
                : 0;

            // 检查是否处于敌对状态
            if (faction.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile)
            {
                return ActionResult.Failure("Not at war with this faction");
            }

            // 检查派系独立冷却
            int cooldownSeconds = gameInterface.GetRemainingCooldownSeconds(faction, "MakePeace");
            if (cooldownSeconds > 0)
            {
                return ActionResult.Failure($"MakePeace is on cooldown for {faction.Name}. Remaining: {cooldownSeconds} seconds");
            }

            // 执行
            var result = gameInterface.MakePeace(faction, peaceCost);

            if (result.Success)
            {
                return ActionResult.Success(result.Message, result.Data);
            }
            else
            {
                return ActionResult.Failure(result.Message);
            }
        }

        /// <summary>
        /// 执行请求商队
        /// </summary>
        private ActionResult ExecuteRequestCaravan(AIAction action)
        {
            string goods = action.Parameters.TryGetValue("goods", out object goodsObj)
                ? goodsObj?.ToString() ?? ""
                : "";

            // 检查关系
            if (faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile)
            {
                return ActionResult.Failure("Cannot request caravan from hostile faction");
            }

            // 检查派系独立冷却
            int cooldownSeconds = gameInterface.GetRemainingCooldownSeconds(faction, "RequestTradeCaravan");
            if (cooldownSeconds > 0)
            {
                return ActionResult.Failure($"RequestTradeCaravan is on cooldown for {faction.Name}. Remaining: {cooldownSeconds} seconds");
            }

            // 执行
            var result = gameInterface.RequestTradeCaravan(faction, goods);

            if (result.Success)
            {
                // 触发商队事件
                TriggerCaravanEvent();
                return ActionResult.Success(result.Message, result.Data);
            }
            else
            {
                return ActionResult.Failure(result.Message);
            }
        }

        /// <summary>
        /// 执行拒绝请求
        /// </summary>
        private ActionResult ExecuteRejectRequest(AIAction action)
        {
            // 拒绝请求不需要调用API，只是返回对话
            string reason = action.Parameters.TryGetValue("reason", out object reasonObj)
                ? reasonObj?.ToString() ?? "I cannot fulfill this request at this time."
                : "I cannot fulfill this request at this time.";

            return ActionResult.Success($"Request rejected: {reason}");
        }

        /// <summary>
        /// 触发援助事件
        /// </summary>
        private void TriggerAidEvent(string aidType)
        {
            // 这里可以触发实际的援助事件，如生成友方单位等
            Log.Message($"[RimDiplomacy] Aid event triggered: {aidType} from {faction.Name}");

            // 发送通知
            Find.LetterStack.ReceiveLetter(
                "Aid Arriving",
                $"{faction.Name} has agreed to send {aidType.ToLower()} aid to your colony.",
                LetterDefOf.PositiveEvent
            );
        }

        /// <summary>
        /// 触发商队事件
        /// </summary>
        private void TriggerCaravanEvent()
        {
            Log.Message($"[RimDiplomacy] Caravan event triggered from {faction.Name}");

            // 发送通知
            Find.LetterStack.ReceiveLetter(
                "Trade Caravan Requested",
                $"A trade caravan from {faction.Name} has been requested and will arrive soon.",
                LetterDefOf.PositiveEvent
            );
        }
    }

    /// <summary>
    /// 动作执行结果
    /// </summary>
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
}
