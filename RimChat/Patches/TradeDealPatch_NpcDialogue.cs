using HarmonyLib;
using RimChat.DiplomacySystem;
using RimChat.NpcDialogue;
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

            int lowQualityCount = 0;
            int soldCount = 0;
            int boughtCount = 0;
            QualityCategory worstQuality = QualityCategory.Legendary;
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
                    Thing soldThing = tradeable.AnyThing;
                    if (soldThing?.def == null || !soldThing.def.IsWeapon)
                    {
                        continue;
                    }

                    if (!soldThing.TryGetQuality(out QualityCategory quality) || quality > QualityCategory.Poor)
                    {
                        continue;
                    }

                    lowQualityCount += count;
                    if (quality < worstQuality)
                    {
                        worstQuality = quality;
                    }
                }
                else if (tradeable.ActionToDo == TradeAction.PlayerBuys)
                {
                    boughtCount += count;
                }
            }

            if (lowQualityCount > 0)
            {
                GameComponent_NpcDialoguePushManager.Instance?.RegisterLowQualityTradeTrigger(
                    faction,
                    lowQualityCount,
                    worstQuality);
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
        }

        private static Faction GetCurrentTraderFaction()
        {
            var trader = Traverse.Create(typeof(TradeSession)).Field("trader").GetValue<ITrader>();
            return trader?.Faction;
        }
    }
}
