using System.Linq;
using HarmonyLib;
using RimChat.DiplomacySystem;
using RimChat.PawnRpgPush;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.Patches
{
    /// <summary>/// Dependencies: RimWorld.TradeDeal, RimWorld.TradeSession.
 /// Responsibility: Report trade outcomes to proactive channels.
 ///</summary>
    [HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.TryExecute))]
    public static class TradeDealPatch_NpcDialogue
    {
        private const float LargeTradeWealthThreshold = 0.015f;

        private static void Postfix(TradeDeal __instance, bool __result, bool actuallyTraded)
        {
            if (!__result || !actuallyTraded || __instance == null)
            {
                return;
            }

            Faction faction = GetCurrentTraderFaction();
            if (faction == null || faction.IsPlayer || faction.defeated)
            {
                return;
            }

            int soldCount = 0;
            int boughtCount = 0;
            int totalTradeAmount = 0;
            foreach (Tradeable tradeable in __instance.AllTradeables)
            {
                if (tradeable == null)
                {
                    continue;
                }

                int count = Mathf.Max(1, System.Math.Abs(tradeable.CountToTransfer));
                if (tradeable.ActionToDo == TradeAction.PlayerSells)
                {
                    soldCount += count;
                    totalTradeAmount += count;
                }
                else if (tradeable.ActionToDo == TradeAction.PlayerBuys)
                {
                    boughtCount += count;
                    totalTradeAmount += count;
                }
            }

            GameComponent_PawnRpgDialoguePushManager.Instance?.RegisterTradeCompletedTrigger(
                faction,
                soldCount,
                boughtCount);

            GameComponent_DiplomacyManager.Instance?.RecordScheduledSocialEvent(
                ScheduledSocialEventType.TradeDeal,
                faction,
                Faction.OfPlayer,
                $"Trade deal completed with {faction.Name}.",
                $"sold={soldCount}, bought={boughtCount}",
                boughtCount - soldCount,
                $"trade:{faction.GetUniqueLoadID()}:{Find.TickManager?.TicksGame ?? 0}:{soldCount}:{boughtCount}");

            TryTriggerLargeTradeEconomicSocialPost(faction, soldCount, boughtCount, totalTradeAmount);
        }

        private static void TryTriggerLargeTradeEconomicSocialPost(Faction faction, int soldCount, int boughtCount, int totalTradeAmount)
        {
            if (faction == null || totalTradeAmount <= 0)
            {
                return;
            }

            float colonyWealth = Find.Maps == null
                ? 0f
                : Find.Maps.Where(map => map != null && map.IsPlayerHome).Sum(map => map.wealthWatcher?.WealthTotal ?? 0f);
            if (colonyWealth <= 0f)
            {
                return;
            }

            float threshold = colonyWealth * LargeTradeWealthThreshold;
            if (totalTradeAmount <= threshold)
            {
                return;
            }

            string summary = $"Major trade agreement completed with {faction.Name}: sold {soldCount}, bought {boughtCount}.";
            GameComponent_DiplomacyManager.Instance?.EnqueuePublicPost(
                faction,
                Faction.OfPlayer,
                SocialPostCategory.Economic,
                sentiment: 2,
                summary: summary,
                isFromPlayerDialogue: false,
                reason: DebugGenerateReason.DialogueExplicit);
        }

        private static Faction GetCurrentTraderFaction()
        {
            var trader = Traverse.Create(typeof(TradeSession)).Field("trader").GetValue<ITrader>();
            return trader?.Faction;
        }
    }
}
