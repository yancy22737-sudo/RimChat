using HarmonyLib;
using RimDiplomacy.NpcDialogue;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimDiplomacy.Patches
{
    /// <summary>
    /// Dependencies: RimWorld.TradeDeal, RimWorld.TradeSession.
    /// Responsibility: Detect low-quality weapon sales and emit causal NPC proactive trigger.
    /// </summary>
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
            QualityCategory worstQuality = QualityCategory.Legendary;
            foreach (Tradeable tradeable in __instance.AllTradeables)
            {
                if (tradeable == null || tradeable.ActionToDo != TradeAction.PlayerSells)
                {
                    continue;
                }

                Thing soldThing = tradeable.AnyThing;
                if (soldThing?.def == null || !soldThing.def.IsWeapon)
                {
                    continue;
                }

                if (!soldThing.TryGetQuality(out QualityCategory quality) || quality > QualityCategory.Poor)
                {
                    continue;
                }

                int count = Mathf.Max(1, System.Math.Abs(tradeable.CountToTransfer));
                lowQualityCount += count;
                if (quality < worstQuality)
                {
                    worstQuality = quality;
                }
            }

            if (lowQualityCount > 0)
            {
                GameComponent_NpcDialoguePushManager.Instance?.RegisterLowQualityTradeTrigger(
                    faction,
                    lowQualityCount,
                    worstQuality);
            }
        }

        private static Faction GetCurrentTraderFaction()
        {
            var trader = Traverse.Create(typeof(TradeSession)).Field("trader").GetValue<ITrader>();
            return trader?.Faction;
        }
    }
}
