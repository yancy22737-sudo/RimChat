using System;
using RimChat.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// CENTRAL AUTHORITY for all airdrop item pricing.
    ///
    /// Architecture — three-tier pricing model:
    ///   Tier 1: Base price formula
    ///     Need side (player buys): BaseMarketValue × needMultiplier
    ///       Normal: ItemAirdropNeedPriceMultiplier (default 1.8)
    ///       ExoticMisc: ItemAirdropExoticMiscNeedPriceMultiplier (default 3.0)
    ///       Precious metal: 1.0 fixed
    ///     Offer side (player sells): BaseMarketValue × offerMultiplier
    ///       Normal: ItemAirdropOfferPriceMultiplier (default 0.6)
    ///       ExoticMisc: ItemAirdropExoticMiscOfferPriceMultiplier (default 0.9)
    ///       Untradeable: ItemAirdropUntradeableOfferPriceMultiplier (default 1.0)
    ///       Precious metal: 1.0 fixed
    ///
    ///   Tier 2: Special item overlay
    ///     Discount ×ItemAirdropSpecialItemDiscountMultiplier (default 0.4)
    ///     Scarce  ×ItemAirdropSpecialItemScarceMultiplier (default 2.0)
    ///
    ///   Tier 3: Untradeable black-market premium (tiered by BaseMarketValue)
    ///     &lt;500: ItemAirdropUntradeableLowValuePriceMultiplier (default 15.0)
    ///     500~1000: ItemAirdropUntradeableMidValuePriceMultiplier (default 8.0)
    ///     &gt;1000: ItemAirdropUntradeablePriceMultiplier (default 6.0)
    ///
    /// All multipliers are user-configurable in Settings → Mod Options → Aid Settings.
    /// </summary>
    internal static class ItemAirdropTradePolicy
    {
        private const string TradersGuildDefName = "TradersGuild";
        private const float OfferPriceMultiplierFallback = 0.6f;
        private const float ExoticMiscOfferPriceMultiplierFallback = 0.9f;
        private const float UntradeableOfferPriceMultiplierFallback = 1.0f;
        private const float UntradeableHighValuePriceMultiplierDefault = 6.0f;
        private const float UntradeableLowValueThreshold = 500f;
        private const float UntradeableHighValueThreshold = 1000f;

        /// <summary>
        /// Active untradeable premium multiplier (high-value tier: >1000 market value), read from user settings.
        /// </summary>
        internal static float UntradeableHighValuePriceMultiplier =>
            RimChatMod.Instance?.InstanceSettings?.ItemAirdropUntradeablePriceMultiplier
            ?? UntradeableHighValuePriceMultiplierDefault;

        /// <summary>
        /// Active untradeable premium multiplier (low-value tier: &lt;500 market value), read from user settings.
        /// </summary>
        internal static float UntradeableLowValuePriceMultiplier =>
            RimChatMod.Instance?.InstanceSettings?.ItemAirdropUntradeableLowValuePriceMultiplier
            ?? 15.0f;

        /// <summary>
        /// Active untradeable premium multiplier (mid-value tier: 500~1000 market value), read from user settings.
        /// </summary>
        internal static float UntradeableMidValuePriceMultiplier =>
            RimChatMod.Instance?.InstanceSettings?.ItemAirdropUntradeableMidValuePriceMultiplier
            ?? 8.0f;

        /// <summary>
        /// Resolve the tiered untradeable multiplier based on the item's BaseMarketValue.
        /// </summary>
        private static float ResolveUntradeableTierMultiplier(float baseMarketValue)
        {
            if (baseMarketValue >= UntradeableHighValueThreshold)
                return UntradeableHighValuePriceMultiplier;
            if (baseMarketValue >= UntradeableLowValueThreshold)
                return UntradeableMidValuePriceMultiplier;
            return UntradeableLowValuePriceMultiplier;
        }

        /// <summary>
        /// Active need price multiplier (base buy price), read from user settings.
        /// </summary>
        internal static float NeedPriceMultiplier =>
            RimChatMod.Instance?.InstanceSettings?.ItemAirdropNeedPriceMultiplier
            ?? 1.8f;

        /// <summary>
        /// Active ExoticMisc need price multiplier, read from user settings.
        /// </summary>
        internal static float ExoticMiscNeedPriceMultiplier =>
            RimChatMod.Instance?.InstanceSettings?.ItemAirdropExoticMiscNeedPriceMultiplier
            ?? 3.0f;

        /// <summary>
        /// Active offer (sell/payment) price multiplier for standard items, read from user settings.
        /// </summary>
        internal static float OfferPriceMultiplier =>
            RimChatMod.Instance?.InstanceSettings?.ItemAirdropOfferPriceMultiplier
            ?? OfferPriceMultiplierFallback;

        /// <summary>
        /// Active offer price multiplier for ExoticMisc items, read from user settings.
        /// </summary>
        internal static float ExoticMiscOfferPriceMultiplier =>
            RimChatMod.Instance?.InstanceSettings?.ItemAirdropExoticMiscOfferPriceMultiplier
            ?? ExoticMiscOfferPriceMultiplierFallback;

        /// <summary>
        /// Active offer price multiplier for untradeable items, read from user settings.
        /// </summary>
        internal static float UntradeableOfferPriceMultiplier =>
            RimChatMod.Instance?.InstanceSettings?.ItemAirdropUntradeableOfferPriceMultiplier
            ?? UntradeableOfferPriceMultiplierFallback;

        /// <summary>
        /// Active special item discount multiplier, read from user settings.
        /// </summary>
        internal static float SpecialItemDiscountMultiplier =>
            RimChatMod.Instance?.InstanceSettings?.ItemAirdropSpecialItemDiscountMultiplier
            ?? 0.4f;

        /// <summary>
        /// Active special item scarce multiplier, read from user settings.
        /// </summary>
        internal static float SpecialItemScarceMultiplier =>
            RimChatMod.Instance?.InstanceSettings?.ItemAirdropSpecialItemScarceMultiplier
            ?? 2.0f;
        private const float BaseFloor = 500f;
        private const float GoodwillCore = 2600f;
        private const float WealthCore = 650f;
        private const float GoodwillWealth = 1250f;
        private const float WealthLate = 380f;
        private const float TradeLinear = 520f;
        private const float TradeWealth = 430f;
        private const float TradeGoodwill = 680f;
        private const float TradeActivationBase = 0.35f;
        private const float TradeActivationGoodwill = 0.35f;
        private const float TradeActivationWealth = 0.20f;
        private const float TradeActivationMin = 0.35f;
        private const float TradeActivationMax = 0.90f;
        private const float TradeActivationWealthOffset = 1.0f;
        private const float TradeActivationWealthRange = 1.2f;

        internal static AirdropTradeRuleSnapshot ResolveRuleSnapshot(Faction faction, float wealthItems, float factionTradeTotalSilver)
        {
            int goodwill = Mathf.Clamp(faction?.GoodwillWith(Faction.OfPlayer) ?? 0, 0, 100);
            bool isMerchantFaction = string.Equals(faction?.def?.defName ?? string.Empty, TradersGuildDefName, StringComparison.Ordinal);
            bool isAlly = faction != null && faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Ally;
            float normalLimit = ResolveNormalTradeLimit(goodwill, wealthItems, factionTradeTotalSilver);
            float resolvedLimit = isMerchantFaction ? normalLimit * 1.4f : normalLimit;
            string tradeLimitRuleText = BuildTradeLimitRuleText(goodwill, wealthItems, factionTradeTotalSilver, isMerchantFaction, isAlly, resolvedLimit);
            int tradeGrowthDeltaSilver = Mathf.RoundToInt(ResolveTradeGrowthDisplayDelta(goodwill, wealthItems, factionTradeTotalSilver));
            return new AirdropTradeRuleSnapshot(
                goodwill,
                isMerchantFaction,
                isAlly,
                ResolveShippingCostPerPod(isMerchantFaction, isAlly),
                Mathf.Max(500, Mathf.RoundToInt(resolvedLimit)),
                tradeLimitRuleText,
                tradeGrowthDeltaSilver);
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
            float multiplier = ResolveOfferPriceMultiplier(def);
            return TryResolveUnifiedPrice(def, multiplier, out unitPrice, out failureCode);
        }

        /// <summary>
        /// Resolve the offer (sell/payment) multiplier for a given item.
        /// Normal items use OfferPriceMultiplier, ExoticMisc uses ExoticMiscOfferPriceMultiplier,
        /// untradeable items use UntradeableOfferPriceMultiplier.
        /// </summary>
        private static float ResolveOfferPriceMultiplier(ThingDef def)
        {
            if (def == null)
                return OfferPriceMultiplier;

            if (def.tradeability == Tradeability.None)
                return UntradeableOfferPriceMultiplier;

            if (def.tradeTags != null && def.tradeTags.Contains("ExoticMisc"))
                return ExoticMiscOfferPriceMultiplier;

            return OfferPriceMultiplier;
        }

        internal static int ResolveTradeGrowthDisplayDelta(int goodwill, float wealthItems, float factionTradeTotalSilver)
        {
            return Mathf.RoundToInt(ResolveTradeGrowthDisplayDeltaInternal(goodwill / 100f, ResolveWealthFactor(wealthItems), factionTradeTotalSilver));
        }

        private static float ResolveNormalTradeLimit(int goodwill, float wealthItems, float factionTradeTotalSilver)
        {
            float goodwillFactor = goodwill / 100f;
            float wealthFactor = ResolveWealthFactor(wealthItems);
            float baseLimit = BaseFloor
                              + GoodwillCore * goodwillFactor
                              + WealthCore * wealthFactor
                              + GoodwillWealth * goodwillFactor * wealthFactor
                              + WealthLate * wealthFactor * wealthFactor;
            float tradeGrowth = ResolveTradeGrowth(goodwillFactor, wealthFactor, factionTradeTotalSilver);
            return baseLimit + tradeGrowth;
        }

        private static float ResolveTradeGrowth(float goodwillFactor, float wealthFactor, float factionTradeTotalSilver)
        {
            float tradeScore = ResolveTradeScore(factionTradeTotalSilver);
            float activation = ResolveTradeActivation(goodwillFactor, wealthFactor);
            return activation * (TradeLinear * tradeScore + TradeWealth * wealthFactor * tradeScore + TradeGoodwill * goodwillFactor * tradeScore);
        }

        private static float ResolveTradeScore(float factionTradeTotalSilver)
        {
            float clamped = Mathf.Max(0f, factionTradeTotalSilver);
            float firstBand = Mathf.Min(clamped, 10000f) * 0.000018f;
            float secondBand = Mathf.Max(0f, Mathf.Min(clamped - 10000f, 20000f)) * 0.000012f;
            float thirdBand = Mathf.Max(0f, Mathf.Min(clamped - 30000f, 50000f)) * 0.000009f;
            float fourthBand = Mathf.Max(0f, Mathf.Min(clamped - 80000f, 270000f)) * 0.0000075f;
            float fifthBand = Mathf.Max(0f, Mathf.Min(clamped - 350000f, 650000f)) * 0.0000060f;
            float sixthBand = Mathf.Max(0f, clamped - 1000000f) * 0.0000050f;
            return firstBand + secondBand + thirdBand + fourthBand + fifthBand + sixthBand;
        }

        private static float ResolveTradeActivation(float goodwillFactor, float wealthFactor)
        {
            float wealthTerm = Mathf.Clamp01((wealthFactor - TradeActivationWealthOffset) / TradeActivationWealthRange);
            float raw = TradeActivationBase + TradeActivationGoodwill * goodwillFactor + TradeActivationWealth * wealthTerm;
            return Mathf.Clamp(raw, TradeActivationMin, TradeActivationMax);
        }

        private static float ResolveWealthFactor(float wealthItems)
        {
            return Mathf.Sqrt(Mathf.Max(0f, wealthItems) / 80000f);
        }

        private static float ResolveTradeGrowthDisplayDeltaInternal(float goodwillFactor, float wealthFactor, float factionTradeTotalSilver)
        {
            if (factionTradeTotalSilver <= 0f)
            {
                return 0f;
            }

            float currentGrowth = ResolveTradeGrowth(goodwillFactor, wealthFactor, factionTradeTotalSilver);
            float previousTradeTotal = Mathf.Max(0f, factionTradeTotalSilver - 10000f);
            float previousGrowth = ResolveTradeGrowth(goodwillFactor, wealthFactor, previousTradeTotal);
            return Mathf.Max(0f, currentGrowth - previousGrowth);
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
            float wealthFactor = ResolveWealthFactor(wealthItems);
            float activation = ResolveTradeActivation(goodwill / 100f, wealthFactor);
            string merchantText = isMerchantFaction ? "商会按最终额度额外放大 x1.4；" : string.Empty;
            string allyText = isAlly ? "盟友身份仍主要影响运费，但高好感会放大额度成长；" : string.Empty;
            return $"阶段规则：前期更看好感，中期更看财富与累计交易额，后期保持稳定增长。当前好感 {goodwill}、财富系数 {wealthFactor:F2}、交易激活 {activation:F2}，累计交易额 {Mathf.RoundToInt(factionTradeTotalSilver)} 对额度提供增量奖励。{merchantText}{allyText}当前上限 {Mathf.RoundToInt(resolvedLimit)}。";
        }

        internal static float ResolveNeedPriceMultiplier(ThingDef def)
        {
            if (def?.tradeTags != null && def.tradeTags.Contains("ExoticMisc"))
            {
                return ExoticMiscNeedPriceMultiplier;
            }

            return NeedPriceMultiplier;
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

        /// <summary>
        /// Tier-3 global modifier: if the item is Tradeability.None, apply black-market premium.
        /// Uses tiered multipliers based on BaseMarketValue:
        ///   &lt;500 → UntradeableLowValuePriceMultiplier (default 15.0)
        ///   500~1000 → UntradeableMidValuePriceMultiplier (default 8.0)
        ///   &gt;1000 → UntradeableHighValuePriceMultiplier (default 6.0)
        /// Call this method from ALL need-unit-price entry points — see class-level conventions.
        /// </summary>
        internal static void ApplyUntradeablePremium(ThingDef def, ref float unitPrice)
        {
            if (def == null || def.tradeability != Tradeability.None) return;

            float basePrice = Math.Max(0.01f, def.BaseMarketValue);
            float needMultiplier = IsPreciousMetalFixedPrice(def)
                ? 1.0f
                : ResolveNeedPriceMultiplier(def);
            float tierMultiplier = ResolveUntradeableTierMultiplier(basePrice);
            float untradeablePrice = basePrice * needMultiplier * tierMultiplier;
            unitPrice = Math.Max(unitPrice, untradeablePrice);
        }

        /// <summary>
        /// Resolve the semantic tag for a need item's price display label.
        /// </summary>
        internal static string ResolveNeedPriceSemantic(ThingDef def, Faction faction)
        {
            if (def == null) return "market_value";

            if (def.tradeability == Tradeability.None)
            {
                float tierMult = ResolveUntradeableTierMultiplier(Math.Max(0.01f, def.BaseMarketValue));
                return $"untradeable_x{tierMult:F1}";
            }

            if (faction != null &&
                FactionSpecialItemsManager.Instance.TryMatchSpecialItem(faction, def.defName, out SpecialItemType specialItemType))
            {
                return specialItemType == SpecialItemType.Discount
                    ? $"special_item_discount_x{SpecialItemDiscountMultiplier:F1}"
                    : $"special_item_scarce_x{SpecialItemScarceMultiplier:F1}";
            }

            if (IsPreciousMetalFixedPrice(def))
                return "market_value";

            if (def.tradeTags != null && def.tradeTags.Contains("ExoticMisc"))
                return $"market_value_x{ExoticMiscNeedPriceMultiplier:F1}";

            return $"market_value_x{NeedPriceMultiplier:F1}";
        }

        internal static bool IsPreciousMetalFixedPrice(ThingDef def)
        {
            string defName = def?.defName ?? string.Empty;
            return string.Equals(defName, "Silver", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(defName, "Gold", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Resolve price for special items (discount/scarce).
        /// Discount multiplier and Scarce multiplier are read from user settings.
        /// Both are applied on top of the base need price multiplier.
        /// </summary>
        internal static bool TryResolveSpecialItemPrice(
            ThingDef def,
            SpecialItemType itemType,
            out float unitPrice,
            out string failureCode)
        {
            unitPrice = 0f;
            if (def == null)
            {
                failureCode = "special_item_def_missing";
                return false;
            }

            float needMultiplier = ResolveNeedPriceMultiplier(def);
            float specialMultiplier = itemType == SpecialItemType.Discount
                ? SpecialItemDiscountMultiplier
                : SpecialItemScarceMultiplier;
            float basePrice = Math.Max(0.01f, def.BaseMarketValue);
            
            // Precious metals use fixed 1.0x base, but still apply special multiplier
            if (IsPreciousMetalFixedPrice(def))
            {
                unitPrice = Math.Max(0.01f, basePrice * specialMultiplier);
            }
            else
            {
                unitPrice = Math.Max(0.01f, basePrice * needMultiplier * specialMultiplier);
            }
            
            failureCode = "ok";
            return true;
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
            string tradeLimitRuleText,
            int tradeGrowthDeltaSilver)
        {
            Goodwill = goodwill;
            IsMerchantFaction = isMerchantFaction;
            IsAlly = isAlly;
            ShippingCostPerPod = Math.Max(0, shippingCostPerPod);
            TradeLimitSilver = Math.Max(0, tradeLimitSilver);
            TradeLimitRuleText = tradeLimitRuleText ?? string.Empty;
            TradeGrowthDeltaSilver = Math.Max(0, tradeGrowthDeltaSilver);
        }

        public int Goodwill { get; }
        public bool IsMerchantFaction { get; }
        public bool IsAlly { get; }
        public int ShippingCostPerPod { get; }
        public int TradeLimitSilver { get; }
        public string TradeLimitRuleText { get; }
        public int TradeGrowthDeltaSilver { get; }
    }
}
