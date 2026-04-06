using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: faction relation state and ThingDef market values.
    /// Responsibility: centralize airdrop trade rules and unified market-value pricing.
    /// </summary>
    internal static class ItemAirdropTradePolicy
    {
        private const string TradersGuildDefName = "TradersGuild";
        private const float DefaultNeedPriceMultiplier = 1.8f;
        private const float ExoticMiscNeedPriceMultiplier = 3.0f;
        private const float OfferPriceMultiplier = 0.6f;
        private const float TradeUnlockGoodwillThreshold = 75f;
        private const float TradeUnlockExponent = 1.35f;

        internal static AirdropTradeRuleSnapshot ResolveRuleSnapshot(Faction faction, float wealthItems, float factionTradeTotalSilver)
        {
            int goodwill = Mathf.Clamp(faction?.GoodwillWith(Faction.OfPlayer) ?? 0, 0, 100);
            bool isMerchantFaction = string.Equals(faction?.def?.defName ?? string.Empty, TradersGuildDefName, StringComparison.Ordinal);
            bool isAlly = faction != null && faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Ally;
            float normalLimit = ResolveNormalTradeLimit(goodwill, wealthItems, factionTradeTotalSilver);
            float resolvedLimit = isMerchantFaction ? normalLimit * 1.4f : normalLimit;
            string tradeLimitRuleText = BuildTradeLimitRuleText(goodwill, wealthItems, factionTradeTotalSilver, isMerchantFaction, isAlly, resolvedLimit);
            return new AirdropTradeRuleSnapshot(goodwill, isMerchantFaction, isAlly, ResolveShippingCostPerPod(isMerchantFaction, isAlly), Mathf.Max(500, Mathf.RoundToInt(resolvedLimit)), tradeLimitRuleText);
        }

        internal static Pawn ResolveBestNegotiator(Pawn preferred)
        {
            _ = preferred;
            return null;
        }

        internal static bool TryResolvePlayerBuyPrice(
            ThingDef def,
            Faction faction,
            Pawn negotiator,
            Map map,
            out float unitPrice,
            out string failureCode)
        {
            _ = faction;
            _ = negotiator;
            _ = map;
            return TryResolveNeedUnitPrice(def, out unitPrice, out failureCode);
        }

        internal static bool TryResolvePlayerSellPrice(
            ThingDef def,
            Faction faction,
            Pawn negotiator,
            Map map,
            out float unitPrice,
            out string failureCode)
        {
            _ = faction;
            _ = negotiator;
            _ = map;
            return TryResolveOfferUnitPrice(def, out unitPrice, out failureCode);
        }

        internal static bool TryResolveNeedUnitPrice(
            ThingDef def,
            out float unitPrice,
            out string failureCode)
        {
            return TryResolveUnifiedPrice(def, ResolveNeedPriceMultiplier(def), out unitPrice, out failureCode);
        }

        internal static bool TryResolveOfferUnitPrice(
            ThingDef def,
            out float unitPrice,
            out string failureCode)
        {
            return TryResolveUnifiedPrice(def, OfferPriceMultiplier, out unitPrice, out failureCode);
        }

        private static float ResolveNormalTradeLimit(int goodwill, float wealthItems, float factionTradeTotalSilver)
        {
            float goodwillFactor = goodwill / 100f;
            float wealthFactor = Mathf.Sqrt(Mathf.Max(0f, wealthItems) / 80000f);
            float tradeUnlock = Mathf.Pow(Mathf.Clamp01(goodwill / TradeUnlockGoodwillThreshold), TradeUnlockExponent);
            float tradeScore = ResolveTradeScore(factionTradeTotalSilver);
            float baseLimit = 420f + 90f * wealthFactor + 1850f * goodwillFactor + 520f * goodwillFactor * wealthFactor;
            float tradeLimit = (950f * tradeScore + 2500f * goodwillFactor * tradeScore + 180f * wealthFactor * tradeScore * tradeScore) * tradeUnlock;
            return baseLimit + tradeLimit;
        }

        private static float ResolveTradeScore(float factionTradeTotalSilver)
        {
            float clamped = Mathf.Max(0f, factionTradeTotalSilver);
            float firstBand = Mathf.Min(clamped, 20000f) * 0.000013f;
            float secondBand = Mathf.Max(0f, Mathf.Min(clamped - 20000f, 80000f)) * 0.00001f;
            float thirdBand = Mathf.Max(0f, Mathf.Min(clamped - 100000f, 250000f)) * 0.0000095f;
            float fourthBand = Mathf.Max(0f, clamped - 350000f) * 0.000014f;
            return firstBand + secondBand + thirdBand + fourthBand;
        }

        private static int ResolveShippingCostPerPod(bool isMerchantFaction, bool isAlly)
        {
            if (isMerchantFaction && isAlly)
            {
                return 150;
            }

            if (isMerchantFaction || isAlly)
            {
                return 200;
            }

            return 250;
        }

        private static string BuildTradeLimitRuleText(int goodwill, float wealthItems, float factionTradeTotalSilver, bool isMerchantFaction, bool isAlly, float resolvedLimit)
        {
            float tradeUnlock = Mathf.Pow(Mathf.Clamp01(goodwill / TradeUnlockGoodwillThreshold), TradeUnlockExponent);
            string merchantText = isMerchantFaction ? "商会系数 x1.4；" : string.Empty;
            string allyText = isAlly ? "盟友身份仅影响运费，不额外放大额度；" : string.Empty;
            return $"连续公式: 基础=420+90×√(财富/80000)+1850×好感比+520×好感比×√(财富/80000)；交易成长=({ResolveTradeScore(factionTradeTotalSilver):F2} 对应累计成交额 {Mathf.RoundToInt(factionTradeTotalSilver)}) × 好感解锁 {tradeUnlock:P0}，且350000后成长加快。{merchantText}{allyText}当前上限 {Mathf.RoundToInt(resolvedLimit)}。";
        }

        private static float ResolveNeedPriceMultiplier(ThingDef def)
        {
            if (def?.tradeTags != null && def.tradeTags.Contains("ExoticMisc"))
            {
                return ExoticMiscNeedPriceMultiplier;
            }

            return DefaultNeedPriceMultiplier;
        }

        private static bool TryResolveUnifiedPrice(
            ThingDef def,
            float defaultMultiplier,
            out float unitPrice,
            out string failureCode)
        {
            unitPrice = 0f;
            if (def == null)
            {
                failureCode = "market_value_def_missing";
                return false;
            }

            float basePrice = Math.Max(0.01f, def.BaseMarketValue);
            float multiplier = IsPreciousMetalFixedPrice(def) ? 1f : defaultMultiplier;
            unitPrice = Math.Max(0.01f, basePrice * multiplier);
            failureCode = "ok";
            return true;
        }

        internal static bool IsPreciousMetalFixedPrice(ThingDef def)
        {
            string defName = def?.defName ?? string.Empty;
            return string.Equals(defName, "Silver", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(defName, "Gold", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal readonly struct AirdropTradeRuleSnapshot
    {
        public AirdropTradeRuleSnapshot(
            int goodwill,
            bool isMerchantFaction,
            bool isAlly,
            int shippingCostPerPod,
            int tradeLimitSilver,
            string tradeLimitRuleText)
        {
            Goodwill = goodwill;
            IsMerchantFaction = isMerchantFaction;
            IsAlly = isAlly;
            ShippingCostPerPod = Math.Max(0, shippingCostPerPod);
            TradeLimitSilver = Math.Max(0, tradeLimitSilver);
            TradeLimitRuleText = tradeLimitRuleText ?? string.Empty;
        }

        public int Goodwill { get; }
        public bool IsMerchantFaction { get; }
        public bool IsAlly { get; }
        public int ShippingCostPerPod { get; }
        public int TradeLimitSilver { get; }
        public string TradeLimitRuleText { get; }
    }
}
