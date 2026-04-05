using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: RimWorld trade utility APIs and faction relation state.
    /// Responsibility: centralize airdrop trade rules and trade-buy price resolution.
    /// </summary>
    internal static class ItemAirdropTradePolicy
    {
        private const string TradersGuildDefName = "TradersGuild";
        private static readonly MethodInfo[] GetPricePlayerBuyMethods = typeof(TradeUtility)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => string.Equals(method.Name, "GetPricePlayerBuy", StringComparison.Ordinal))
            .OrderByDescending(method => method.GetParameters().Length)
            .ToArray();

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
            if (IsEligibleNegotiator(preferred))
            {
                return preferred;
            }

            var candidates = new List<Pawn>();
            foreach (Map map in (Find.Maps ?? Enumerable.Empty<Map>()).Where(m => m != null && m.IsPlayerHome))
            {
                List<Pawn> colonists = map.mapPawns?.FreeColonistsSpawned;
                if (colonists == null)
                {
                    continue;
                }

                foreach (Pawn pawn in colonists)
                {
                    if (IsEligibleNegotiator(pawn) && !candidates.Contains(pawn))
                    {
                        candidates.Add(pawn);
                    }
                }
            }

            if (candidates.Count == 0)
            {
                foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
                {
                    if (IsEligibleNegotiator(pawn) && !candidates.Contains(pawn))
                    {
                        candidates.Add(pawn);
                    }
                }
            }

            return candidates
                .OrderByDescending(GetSocialSkillLevel)
                .ThenBy(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShortCap)
                .FirstOrDefault();
        }

        internal static bool TryResolvePlayerBuyPrice(
            ThingDef def,
            Faction faction,
            Pawn negotiator,
            Map map,
            out float unitPrice,
            out string failureCode)
        {
            unitPrice = 0f;
            failureCode = string.Empty;

            if (def == null)
            {
                failureCode = "trade_price_def_missing";
                return false;
            }

            if (faction == null)
            {
                failureCode = "trade_price_faction_missing";
                return false;
            }

            if (negotiator == null || !IsEligibleNegotiator(negotiator))
            {
                failureCode = "trade_price_negotiator_missing";
                return false;
            }

            if (map == null)
            {
                failureCode = "trade_price_map_missing";
                return false;
            }

            if (!HasTradeContextSource(faction, map))
            {
                failureCode = "trade_context_unavailable";
                return false;
            }

            Thing transientThing = null;
            try
            {
                transientThing = ThingMaker.MakeThing(def);
                transientThing.stackCount = 1;
                foreach (MethodInfo method in GetPricePlayerBuyMethods)
                {
                    if (!TryInvokeGetPricePlayerBuy(method, transientThing, def, faction, negotiator, map, out float resolved))
                    {
                        continue;
                    }

                    if (resolved > 0f && !float.IsNaN(resolved) && !float.IsInfinity(resolved))
                    {
                        unitPrice = Math.Max(0.01f, resolved);
                        return true;
                    }
                }
            }
            catch
            {
                failureCode = "trade_price_resolve_exception";
                return false;
            }
            finally
            {
                transientThing?.Destroy(DestroyMode.Vanish);
            }

            failureCode = "trade_price_method_unavailable";
            return false;
        }

        private static bool TryInvokeGetPricePlayerBuy(
            MethodInfo method,
            Thing thing,
            ThingDef def,
            Faction faction,
            Pawn negotiator,
            Map map,
            out float resolved)
        {
            resolved = 0f;
            ParameterInfo[] parameters = method.GetParameters();
            var args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                if (!TryMapArgument(parameters[i].ParameterType, thing, def, faction, negotiator, map, out object value))
                {
                    return false;
                }

                args[i] = value;
            }

            object raw = method.Invoke(null, args);
            switch (raw)
            {
                case float floatValue:
                    resolved = floatValue;
                    return true;
                case double doubleValue:
                    resolved = (float)doubleValue;
                    return true;
                case int intValue:
                    resolved = intValue;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryMapArgument(
            Type parameterType,
            Thing thing,
            ThingDef def,
            Faction faction,
            Pawn negotiator,
            Map map,
            out object value)
        {
            value = null;
            if (parameterType == typeof(Thing))
            {
                value = thing;
                return true;
            }

            if (parameterType == typeof(ThingDef))
            {
                value = def;
                return true;
            }

            if (parameterType == typeof(Pawn))
            {
                value = negotiator;
                return true;
            }

            if (parameterType == typeof(Faction))
            {
                value = faction;
                return true;
            }

            if (parameterType == typeof(Map))
            {
                value = map;
                return true;
            }

            if (parameterType == typeof(bool))
            {
                value = false;
                return true;
            }

            if (parameterType == typeof(int))
            {
                value = 1;
                return true;
            }

            if (parameterType == typeof(float))
            {
                value = 1f;
                return true;
            }

            if (!parameterType.IsValueType)
            {
                value = null;
                return true;
            }

            return false;
        }

        private static bool HasTradeContextSource(Faction faction, Map map)
        {
            bool hasOrbitalTradeShip = map.passingShipManager?.passingShips?
                .OfType<TradeShip>()
                .Any(ship => ship?.Faction == faction) == true;
            bool hasGroundTraderProfile = faction?.def?.caravanTraderKinds != null &&
                                          faction.def.caravanTraderKinds.Any(kind => kind != null && !kind.orbital);
            return hasOrbitalTradeShip || hasGroundTraderProfile;
        }

        private static bool IsEligibleNegotiator(Pawn pawn)
        {
            return pawn != null &&
                   pawn.Faction == Faction.OfPlayer &&
                   pawn.RaceProps?.Humanlike == true &&
                   !pawn.Dead &&
                   !pawn.Downed &&
                   !pawn.IsPrisoner;
        }

        private static int GetSocialSkillLevel(Pawn pawn)
        {
            return pawn?.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
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
