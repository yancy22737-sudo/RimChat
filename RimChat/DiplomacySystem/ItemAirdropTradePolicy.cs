using System;
using RimWorld;
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
        private const float NeedPriceMultiplier = 1.4f;
        private const float OfferPriceMultiplier = 0.6f;

        internal static AirdropTradeRuleSnapshot ResolveRuleSnapshot(Faction faction)
        {
            int goodwill = faction?.GoodwillWith(Faction.OfPlayer) ?? 0;
            bool isMerchantFaction = string.Equals(faction?.def?.defName ?? string.Empty, TradersGuildDefName, StringComparison.Ordinal);
            bool isAlly = faction != null && faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Ally;

            if (isMerchantFaction && isAlly)
            {
                return new AirdropTradeRuleSnapshot(goodwill, true, true, 150, 12000, "盟友商会固定上限 12000");
            }

            if (isMerchantFaction)
            {
                return new AirdropTradeRuleSnapshot(goodwill, true, false, 200, 800, "商会固定上限 800");
            }

            if (isAlly)
            {
                return new AirdropTradeRuleSnapshot(goodwill, false, true, 200, 8000, "盟友派系固定上限 8000");
            }

            int goodwillSteps = (int)Math.Floor(goodwill / 5f);
            int goodwillLimit = Math.Max(500, 500 + goodwillSteps * 300);
            return new AirdropTradeRuleSnapshot(goodwill, false, false, 250, goodwillLimit, "普通派系: 0好感=500, 每+5好感+300, 下限500");
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
            return TryResolveUnifiedPrice(def, NeedPriceMultiplier, out unitPrice, out failureCode);
        }

        internal static bool TryResolveOfferUnitPrice(
            ThingDef def,
            out float unitPrice,
            out string failureCode)
        {
            return TryResolveUnifiedPrice(def, OfferPriceMultiplier, out unitPrice, out failureCode);
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
