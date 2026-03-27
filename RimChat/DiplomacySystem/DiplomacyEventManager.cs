using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;
using RimChat.Config;
using RimChat.Core;

namespace RimChat.DiplomacySystem
{
    public enum CaravanType
    {
        General,
        BulkGoods,
        CombatSupplier,
        Exotic,
        Slaver
    }

    public enum AidType
    {
        Military,
        Medical,
        Resources
    }

    public static partial class DiplomacyEventManager
    {
        private static readonly string[] MilitaryAidIncidentCandidates =
        {
            "FriendlyRaid",
            "RaidFriendly"
        };

        public static bool TriggerCaravanEvent(Faction faction, CaravanType caravanType)
        {
            try
            {
                Map map = Find.AnyPlayerHomeMap;
                if (map == null)
                {
                    Log.Warning("[RimChat] No player home map found for caravan event");
                    return false;
                }

                IncidentParms parms = new IncidentParms();
                parms.target = map;
                parms.faction = faction;

                TraderKindDef traderKind = GetTraderKindForType(faction, caravanType);
                if (traderKind != null)
                {
                    parms.traderKind = traderKind;
                }

                IncidentDef incidentDef = IncidentDefOf.TraderCaravanArrival;
                bool success = incidentDef.Worker.TryExecute(parms);

                if (success)
                {
                    Log.Message($"[RimChat] Triggered {caravanType} caravan from {faction.Name}");
                }
                else
                {
                    Log.Warning($"[RimChat] Failed to trigger {caravanType} caravan from {faction.Name}");
                }

                return success;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Error triggering caravan event: {ex}");
                return false;
            }
        }

        private static TraderKindDef GetTraderKindForType(Faction faction, CaravanType caravanType)
        {
            List<TraderKindDef> factionTraders = GetFactionGroundTraderKinds(faction);
            if (factionTraders.Count == 0)
            {
                Log.Warning($"[RimChat] Faction {faction?.Name ?? "null"} has no ground caravan traders; leave traderKind null.");
                return null;
            }

            List<TraderKindDef> matchingTraders = factionTraders
                .Where(trader => MatchesCaravanType(trader, caravanType))
                .ToList();

            Log.Message($"[RimChat] Faction trader pool for {faction?.Name ?? "null"}: total={factionTraders.Count}, typeMatched={matchingTraders.Count}, requestedType={caravanType}");
            foreach (TraderKindDef trader in matchingTraders)
            {
                Log.Message($"[RimChat]   - {trader.defName}");
            }

            if (matchingTraders.Count > 0)
            {
                TraderKindDef selected = matchingTraders.RandomElement();
                Log.Message($"[RimChat] Selected faction-matched trader: {selected.defName}");
                return selected;
            }

            // Fail fast to faction-safe fallback instead of global cross-faction randomization.
            TraderKindDef factionFallback = factionTraders.RandomElement();
            Log.Warning($"[RimChat] No trader matched {caravanType} for {faction?.Name ?? "null"}, fallback to faction trader {factionFallback.defName}.");
            return factionFallback;
        }

        private static List<TraderKindDef> GetFactionGroundTraderKinds(Faction faction)
        {
            List<TraderKindDef> source = faction?.def?.caravanTraderKinds;
            if (source == null || source.Count == 0)
            {
                return new List<TraderKindDef>();
            }

            List<TraderKindDef> result = new List<TraderKindDef>(source.Count);
            foreach (TraderKindDef trader in source)
            {
                if (trader == null || trader.orbital)
                {
                    continue;
                }

                result.Add(trader);
            }

            return result;
        }

        private static bool MatchesCaravanType(TraderKindDef trader, CaravanType caravanType)
        {
            string defName = trader?.defName ?? string.Empty;
            switch (caravanType)
            {
                case CaravanType.General:
                    return DefNameContains(defName, "standard") || DefNameContains(defName, "general");
                case CaravanType.BulkGoods:
                    return DefNameContains(defName, "bulk");
                case CaravanType.CombatSupplier:
                    return DefNameContains(defName, "combat") || DefNameContains(defName, "weapon");
                case CaravanType.Exotic:
                    return DefNameContains(defName, "exotic");
                case CaravanType.Slaver:
                    return DefNameContains(defName, "slave");
                default:
                    return false;
            }
        }

        private static bool DefNameContains(string source, string value)
        {
            return source?.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool TriggerAidEvent(Faction faction, AidType aidType)
        {
            try
            {
                Map map = Find.AnyPlayerHomeMap;
                if (map == null)
                {
                    Log.Warning("[RimChat] No player home map found for aid event");
                    return false;
                }

                switch (aidType)
                {
                    case AidType.Military:
                        return TriggerMilitaryAid(faction, map);
                    case AidType.Medical:
                        return TriggerMedicalAid(faction, map);
                    case AidType.Resources:
                        return TriggerResourceAid(faction, map);
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Error triggering aid event: {ex}");
                return false;
            }
        }

        /// <summary>/// 触发军事支援事件（公共接口，用于 CallEveryone 友好派系支援）
        ///</summary>
        public static bool TriggerMilitaryAidEvent(Faction faction)
        {
            try
            {
                Map map = Find.AnyPlayerHomeMap;
                if (map == null)
                {
                    Log.Warning("[RimChat] No player home map found for military aid event");
                    return false;
                }

                if (faction == null || faction.defeated)
                {
                    Log.Warning("[RimChat] Invalid faction for military aid");
                    return false;
                }

                return TriggerMilitaryAid(faction, map);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Error triggering military aid event: {ex}");
                return false;
            }
        }

        /// <summary>
        /// CallEveryone 专用军事支援：不依赖 RaidFriendly/FriendlyRaid，可用于中立派系援军。
        /// </summary>
        public static bool TriggerMilitaryAidCallEveryoneEvent(Faction faction)
        {
            if (!TryBuildCallEveryoneAidParms(faction, out Map map, out IncidentParms aidParms, out string parmReason))
            {
                Log.Error($"[RimChat][CallEveryoneCustomAidFailFast] faction={faction?.Name ?? "null"}, stage=BuildParms, reason={parmReason}");
                return false;
            }

            if (!TryGenerateCallEveryoneAidPawns(aidParms, out List<Pawn> pawns, out string pawnReason))
            {
                Log.Error($"[RimChat][CallEveryoneCustomAidFailFast] faction={faction?.Name ?? "null"}, stage=GeneratePawns, reason={pawnReason}");
                return false;
            }

            if (!TryArriveCallEveryoneAidPawns(map, aidParms, pawns, out string arriveReason))
            {
                Log.Error($"[RimChat][CallEveryoneCustomAidFailFast] faction={faction?.Name ?? "null"}, stage=Arrive, reason={arriveReason}");
                return false;
            }

            Log.Message($"[RimChat] Triggered custom military aid from {faction.Name}, pawns={pawns.Count}");
            SendAidLetter(faction, "RimChat_MilitaryAidArrivedTitle".Translate(),
                "RimChat_MilitaryAidLetterBody".Translate(faction.Name));
            return true;
        }

        private static bool TriggerMilitaryAid(Faction faction, Map map)
        {
            IncidentParms parms = new IncidentParms
            {
                target = map,
                faction = faction,
                points = StorytellerUtility.DefaultThreatPointsNow(map) * 0.5f,
                forced = true
            };

            if (!TryResolveExecutableMilitaryAidIncident(parms, out IncidentDef militaryAidDef, out string resolveReason))
            {
                Log.Error($"[RimChat][AidIncidentExecuteFailFast] faction={faction?.Name ?? "null"}, reason={resolveReason}");
                return false;
            }

            bool success = militaryAidDef.Worker.TryExecute(parms);
            if (!success)
            {
                Log.Error($"[RimChat][AidIncidentExecuteFailFast] faction={faction?.Name ?? "null"}, incident={militaryAidDef.defName}, reason=TryExecuteReturnedFalse");
                return false;
            }

            Log.Message($"[RimChat][AidIncidentResolve] faction={faction.Name}, incident={militaryAidDef.defName}, result=success");
            Log.Message($"[RimChat] Triggered military aid from {faction.Name}");
            SendAidLetter(faction, "RimChat_MilitaryAidArrivedTitle".Translate(),
                "RimChat_MilitaryAidLetterBody".Translate(faction.Name));
            return true;
        }

        private static bool TryResolveExecutableMilitaryAidIncident(
            IncidentParms parms,
            out IncidentDef incidentDef,
            out string reason)
        {
            incidentDef = null;
            reason = "NoCandidateIncidentDef";

            List<string> observed = new List<string>();
            foreach (string candidate in MilitaryAidIncidentCandidates)
            {
                IncidentDef def = DefDatabase<IncidentDef>.GetNamedSilentFail(candidate);
                if (def == null)
                {
                    observed.Add($"{candidate}:Missing");
                    continue;
                }

                if (def.Worker == null)
                {
                    observed.Add($"{candidate}:NoWorker");
                    continue;
                }

                if (!def.Worker.CanFireNow(parms))
                {
                    observed.Add($"{candidate}:CanFireNowFalse");
                    continue;
                }

                incidentDef = def;
                reason = $"Resolved:{def.defName}";
                Log.Message($"[RimChat][AidIncidentResolve] faction={parms.faction?.Name ?? "null"}, selected={def.defName}, observed={string.Join(",", observed)}");
                return true;
            }

            reason = $"NoExecutableCandidate; observed={string.Join(",", observed)}";
            Log.Error($"[RimChat][AidIncidentResolve] faction={parms.faction?.Name ?? "null"}, selected=<none>, observed={string.Join(",", observed)}");
            return false;
        }

        private static bool TriggerMedicalAid(Faction faction, Map map)
        {
            List<Thing> medicalSupplies = GenerateMedicalSupplies();
            DropPodUtility.DropThingsNear(
                DropCellFinder.TradeDropSpot(map),
                map,
                medicalSupplies,
                110,
                canInstaDropDuringInit: false,
                leaveSlag: false,
                canRoofPunch: false
            );

            SendAidLetter(faction, "RimChat_MedicalAidArrivedTitle".Translate(), 
                "RimChat_MedicalAidLetterBody".Translate(faction.Name));
            
            Log.Message($"[RimChat] Triggered medical aid from {faction.Name}");
            return true;
        }

        private static bool TriggerResourceAid(Faction faction, Map map)
        {
            List<Thing> resources = GenerateResourceSupplies();
            DropPodUtility.DropThingsNear(
                DropCellFinder.TradeDropSpot(map),
                map,
                resources,
                110,
                canInstaDropDuringInit: false,
                leaveSlag: false,
                canRoofPunch: false
            );

            SendAidLetter(faction, "RimChat_ResourceAidArrivedTitle".Translate(), 
                "RimChat_ResourceAidLetterBody".Translate(faction.Name));
            
            Log.Message($"[RimChat] Triggered resource aid from {faction.Name}");
            return true;
        }

        private static List<Thing> GenerateMedicalSupplies()
        {
            List<Thing> supplies = new List<Thing>();
            
            ThingDef medicineDef = ThingDefOf.MedicineIndustrial;
            Thing medicine = ThingMaker.MakeThing(medicineDef);
            medicine.stackCount = Rand.Range(15, 30);
            supplies.Add(medicine);
            
            ThingDef herbalMedicineDef = ThingDefOf.MedicineHerbal;
            Thing herbalMedicine = ThingMaker.MakeThing(herbalMedicineDef);
            herbalMedicine.stackCount = Rand.Range(20, 40);
            supplies.Add(herbalMedicine);
            
            ThingDef bandageDef = ThingDef.Named("Bandage");
            if (bandageDef != null)
            {
                Thing bandages = ThingMaker.MakeThing(bandageDef);
                bandages.stackCount = Rand.Range(10, 20);
                supplies.Add(bandages);
            }
            
            return supplies;
        }

        private static List<Thing> GenerateResourceSupplies()
        {
            List<Thing> supplies = new List<Thing>();
            
            ThingDef woodDef = ThingDefOf.WoodLog;
            Thing wood = ThingMaker.MakeThing(woodDef);
            wood.stackCount = Rand.Range(100, 200);
            supplies.Add(wood);
            
            ThingDef steelDef = ThingDefOf.Steel;
            Thing steel = ThingMaker.MakeThing(steelDef);
            steel.stackCount = Rand.Range(50, 100);
            supplies.Add(steel);
            
            ThingDef foodDef = ThingDefOf.MealSimple;
            Thing food = ThingMaker.MakeThing(foodDef);
            food.stackCount = Rand.Range(30, 50);
            supplies.Add(food);
            
            return supplies;
        }

        private static void SendAidLetter(Faction faction, string title, string message)
        {
            Map map = Find.AnyPlayerHomeMap;
            LookTargets lookTargets = map != null
                ? new LookTargets(new TargetInfo(map.Center, map))
                : null;
            Find.LetterStack.ReceiveLetter(
                title,
                message,
                LetterDefOf.PositiveEvent,
                lookTargets,
                faction
            );
        }

        public static string GetCaravanTypeLabel(CaravanType type)
        {
            return type switch
            {
                CaravanType.General => "GeneralTrader".Translate(),
                CaravanType.BulkGoods => "BulkGoodsTrader".Translate(),
                CaravanType.CombatSupplier => "CombatSupplier".Translate(),
                CaravanType.Exotic => "ExoticTrader".Translate(),
                CaravanType.Slaver => "Slaver".Translate(),
                _ => type.ToString()
            };
        }

        public static string GetAidTypeLabel(AidType type)
        {
            return type switch
            {
                AidType.Military => "MilitaryAid".Translate(),
                AidType.Medical => "MedicalAid".Translate(),
                AidType.Resources => "ResourceAid".Translate(),
                _ => type.ToString()
            };
        }

        public static CaravanType ParseCaravanType(string typeStr)
        {
            if (Enum.TryParse(typeStr, true, out CaravanType type))
            {
                return type;
            }
            return CaravanType.General;
        }

        public static AidType ParseAidType(string typeStr)
        {
            if (Enum.TryParse(typeStr, true, out AidType type))
            {
                return type;
            }
            return AidType.Military;
        }

        public static int CalculateDelayTicks(Faction faction, bool isAid = false)
        {
            int baseTicks = isAid 
                ? (RimChatMod.Instance?.InstanceSettings?.AidDelayBaseTicks ?? 90000)
                : (RimChatMod.Instance?.InstanceSettings?.CaravanDelayBaseTicks ?? 135000);

            float modifier = 1.0f;
            int goodwill = faction.PlayerGoodwill;
            var relation = faction.RelationKindWith(Faction.OfPlayer);

            if (relation == FactionRelationKind.Ally)
            {
                modifier = 0.5f;
            }
            else if (goodwill >= 40)
            {
                modifier = 0.7f;
            }
            else if (goodwill < 0)
            {
                modifier = 1.5f;
            }

            int delayTicks = (int)(baseTicks * modifier);
            return delayTicks;
        }

        public static bool ScheduleDelayedCaravan(Faction faction, CaravanType caravanType)
        {
            try
            {
                int delayTicks = CalculateDelayTicks(faction, false);
                int executeTick = Find.TickManager.TicksGame + delayTicks;

                var evt = new DelayedDiplomacyEvent(DelayedEventType.Caravan, faction, executeTick)
                {
                    CaravanType = caravanType
                };

                GameComponent_DiplomacyManager.Instance?.AddDelayedEvent(evt);

                float delayDays = delayTicks / 60000f;
                string caravanTypeLabel = GetCaravanTypeLabel(caravanType);
                DiplomacyNotificationManager.SendDelayedEventScheduledNotification(faction, DelayedEventType.Caravan, caravanTypeLabel, delayDays);

                Log.Message($"[RimChat] Scheduled delayed caravan from {faction.Name}, type={caravanType}, delay={delayDays:F1} days");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Error scheduling delayed caravan: {ex}");
                return false;
            }
        }

        public static bool ScheduleDelayedAid(Faction faction, AidType aidType)
        {
            try
            {
                int delayTicks = CalculateDelayTicks(faction, true);
                int executeTick = Find.TickManager.TicksGame + delayTicks;

                var evt = new DelayedDiplomacyEvent(DelayedEventType.Aid, faction, executeTick)
                {
                    AidType = aidType
                };

                GameComponent_DiplomacyManager.Instance?.AddDelayedEvent(evt);

                float delayDays = delayTicks / 60000f;
                string aidTypeLabel = GetAidTypeLabel(aidType);
                DiplomacyNotificationManager.SendDelayedEventScheduledNotification(faction, DelayedEventType.Aid, aidTypeLabel, delayDays);

                Log.Message($"[RimChat] Scheduled delayed aid from {faction.Name}, type={aidType}, delay={delayDays:F1} days");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Error scheduling delayed aid: {ex}");
                return false;
            }
        }

        public static bool TriggerRaidEvent(Faction faction, float points, RaidStrategyDef strategy, PawnsArrivalModeDef arrivalMode)
        {
            try
            {
                if (!TryValidateRaidFaction(faction, out string factionValidationReason))
                {
                    Log.Warning($"[RimChat] Raid blocked: {factionValidationReason}");
                    return false;
                }

                Map map = Find.AnyPlayerHomeMap;
                if (map == null)
                {
                    Log.Warning("[RimChat] No player home map found for raid event");
                    return false;
                }

                float raidPoints = ResolveRaidPoints(map, faction, points);

                if (IsMiliraFaction(faction))
                {
                    if (TryExecuteMiliraRaidFallback(map, faction, raidPoints, out string miliraDirectReason))
                    {
                        return true;
                    }

                    Log.Warning($"[RimChat] Milira direct raid fallback failed: {miliraDirectReason}");
                    return false;
                }

                // Normalize strategy: ensure it's valid and executable
                RaidStrategyDef normalizedStrategy = strategy;
                if (normalizedStrategy == null || !IsStrategyExecutable(normalizedStrategy, faction, map))
                {
                    normalizedStrategy = GetFallbackStrategy(faction, map);
                    if (normalizedStrategy == null)
                    {
                        Log.Warning($"[RimChat] Cannot find executable raid strategy for {faction?.Name}; falling back to vanilla raid strategy resolution.");
                    }
                    else
                    {
                        Log.Warning($"[RimChat] Strategy {strategy?.defName} not executable, using fallback {normalizedStrategy.defName}");
                    }
                }

                // Normalize arrival mode: ensure it's valid and compatible
                PawnsArrivalModeDef normalizedArrivalMode = arrivalMode;
                if (normalizedStrategy == null)
                {
                    normalizedArrivalMode = null;
                }
                else if (normalizedArrivalMode == null || !IsArrivalModeCompatible(normalizedArrivalMode, normalizedStrategy))
                {
                    normalizedArrivalMode = GetFallbackArrivalMode(normalizedStrategy);
                    if (normalizedArrivalMode == null)
                    {
                        Log.Error($"[RimChat] Cannot find compatible arrival mode for strategy {normalizedStrategy?.defName}");
                        return false;
                    }
                    Log.Warning($"[RimChat] Arrival mode {arrivalMode?.defName} not compatible, using fallback {normalizedArrivalMode.defName}");
                }

                IncidentDef incidentDef = IncidentDefOf.RaidEnemy;
                if (incidentDef == null || incidentDef.Worker == null)
                {
                    Log.Error("[RimChat] RaidEnemy incident def/worker is unavailable.");
                    return false;
                }

                IncidentParms parms = BuildRaidIncidentParmsWithDefaults(
                    incidentDef,
                    map,
                    faction,
                    raidPoints,
                    normalizedStrategy,
                    normalizedArrivalMode);
                if (!EnsureUsableCombatPawnGroupMakerForParms(faction, parms, out string groupPreflightReason))
                {
                    Log.Warning($"[RimChat] Raid group preflight could not ensure usable combat maker: {groupPreflightReason}");
                }

                if (!incidentDef.Worker.CanFireNow(parms))
                {
                    if (TryExecuteRaidWithVanillaAutoFallback(incidentDef, map, faction, raidPoints, out string vanillaAutoReason))
                    {
                        Log.Warning($"[RimChat] Raid precheck blocked for strategy={normalizedStrategy?.defName ?? "auto"}, arrival={normalizedArrivalMode?.defName ?? "auto"}; forced vanilla auto fallback succeeded.");
                        return true;
                    }

                    if (TryExecuteMiliraRaidFallback(map, faction, raidPoints, out string miliraFallbackReason))
                    {
                        return true;
                    }

                    Log.Warning($"[RimChat] Raid precheck blocked for faction={faction.Name}, def={faction.def?.defName}, relation={faction.RelationKindWith(Faction.OfPlayer)}, points={raidPoints:F1}, strategy={normalizedStrategy?.defName ?? "auto"}, arrival={normalizedArrivalMode?.defName ?? "auto"}, vanillaAuto={vanillaAutoReason}, miliraFallback={miliraFallbackReason}. {DescribeRaidGroupMakerState(faction)}");
                    return false;
                }

                bool success = incidentDef.Worker.TryExecute(parms);

                if (success)
                {
                    Log.Message($"[RimChat] Triggered raid from {faction.Name} with strategy {normalizedStrategy?.defName ?? "auto"} and arrival {normalizedArrivalMode?.defName ?? "auto"}");
                }
                else
                {
                    if (TryExecuteRaidWithVanillaAutoFallback(incidentDef, map, faction, raidPoints, out string vanillaAutoReason))
                    {
                        Log.Warning($"[RimChat] Raid execution failed for strategy={normalizedStrategy?.defName ?? "auto"}, arrival={normalizedArrivalMode?.defName ?? "auto"}; forced vanilla auto fallback succeeded.");
                        return true;
                    }

                    if (TryExecuteMiliraRaidFallback(map, faction, raidPoints, out string miliraFallbackReason))
                    {
                        return true;
                    }

                    Log.Warning($"[RimChat] Failed to trigger raid from {faction.Name}, vanillaAuto={vanillaAutoReason}, miliraFallback={miliraFallbackReason}. {DescribeRaidGroupMakerState(faction)}");
                }

                return success;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Error triggering raid event: {ex}");
                return false;
            }
        }

        public static bool TryValidateRaidFaction(Faction faction, out string reason)
        {
            reason = string.Empty;
            if (faction == null)
            {
                reason = "Faction cannot be null.";
                return false;
            }

            if (faction.defeated)
            {
                reason = $"Faction {faction.Name} is defeated.";
                return false;
            }

            if (faction.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile)
            {
                reason = $"Faction {faction.Name} is not hostile to the player.";
                return false;
            }

            if (!EnsureRaidTemplates(faction, out reason))
            {
                reason = $"Faction {faction.Name} cannot launch raids: {reason}";
                return false;
            }

            return true;
        }

        private static bool HasUsableCombatPawnGroupMaker(Faction faction, out string reason)
        {
            reason = string.Empty;
            List<PawnGroupMaker> makers = faction?.def?.pawnGroupMakers;
            if (makers == null || makers.Count == 0)
            {
                reason = "no pawnGroupMakers defined on faction def.";
                return false;
            }

            List<PawnGroupMaker> combatMakers = makers
                .Where(m => m?.kindDef == PawnGroupKindDefOf.Combat)
                .ToList();
            if (combatMakers.Count == 0)
            {
                string availableKinds = string.Join(", ", makers
                    .Where(m => m?.kindDef != null)
                    .Select(m => m.kindDef.defName)
                    .Distinct()
                    .OrderBy(name => name));
                reason = $"missing Combat pawnGroupMaker (available kinds: {availableKinds}).";
                return false;
            }

            bool hasOptions = combatMakers.Any(m => m.options != null && m.options.Count > 0);
            if (!hasOptions)
            {
                reason = "Combat pawnGroupMaker exists but has no options.";
                return false;
            }

            return true;
        }

        private static string DescribeRaidGroupMakerState(Faction faction)
        {
            if (faction?.def?.pawnGroupMakers == null || faction.def.pawnGroupMakers.Count == 0)
            {
                return "FactionDef has no pawnGroupMakers.";
            }

            int total = faction.def.pawnGroupMakers.Count;
            int combat = faction.def.pawnGroupMakers.Count(m => m?.kindDef == PawnGroupKindDefOf.Combat);
            int combatWithOptions = faction.def.pawnGroupMakers.Count(m =>
                m?.kindDef == PawnGroupKindDefOf.Combat &&
                m.options != null &&
                m.options.Count > 0);
            return $"PawnGroupMakers total={total}, combat={combat}, combatWithOptions={combatWithOptions}.";
        }

        private static bool IsStrategyExecutable(RaidStrategyDef strategy, Faction faction, Map map)
        {
            if (strategy == null || faction == null || map == null)
            {
                return false;
            }

            try
            {
                return strategy.Worker != null && strategy.Worker.CanUseWith(new IncidentParms { target = map, faction = faction }, PawnGroupKindDefOf.Combat);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryExecuteRaidWithVanillaAutoFallback(
            IncidentDef incidentDef,
            Map map,
            Faction faction,
            float raidPoints,
            out string reason)
        {
            reason = "not attempted";
            if (incidentDef?.Worker == null || map == null || faction == null)
            {
                reason = "incident worker/map/faction is unavailable";
                return false;
            }

            IncidentParms autoParms = BuildRaidIncidentParmsWithDefaults(
                incidentDef,
                map,
                faction,
                raidPoints,
                strategy: null,
                arrivalMode: null);
            if (!EnsureUsableCombatPawnGroupMakerForParms(faction, autoParms, out string groupReason))
            {
                Log.Warning($"[RimChat] Vanilla auto fallback preflight warning: {groupReason}");
            }

            if (!incidentDef.Worker.CanFireNow(autoParms))
            {
                reason = "CanFireNow false with auto strategy/arrival";
                return false;
            }

            if (!incidentDef.Worker.TryExecute(autoParms))
            {
                reason = "TryExecute false with auto strategy/arrival";
                return false;
            }

            reason = "success";
            Log.Message($"[RimChat] Triggered raid from {faction.Name} with forced vanilla auto strategy/arrival.");
            return true;
        }

        private static float ResolveRaidPoints(Map map, Faction faction, float requestedPoints)
        {
            float basePoints = requestedPoints;
            if (basePoints <= 0f)
            {
                basePoints = ResolveBaseRaidPointsFromStoryteller(map);
            }

            return ApplyRaidPointTuning(faction, basePoints);
        }

        private static float ResolveBaseRaidPointsFromStoryteller(Map map)
        {
            try
            {
                IncidentParms defaultRaidParms = StorytellerUtility.DefaultParmsNow(IncidentDefOf.RaidEnemy.category, map);
                if (defaultRaidParms != null && defaultRaidParms.points > 0f)
                {
                    return defaultRaidParms.points;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to resolve default raid points from storyteller parms: {ex.Message}");
            }

            float fallbackThreatPoints = StorytellerUtility.DefaultThreatPointsNow(map);
            if (fallbackThreatPoints > 0f)
            {
                return fallbackThreatPoints;
            }

            return 35f;
        }

        private static float ApplyRaidPointTuning(Faction faction, float basePoints)
        {
            var settings = RimChatMod.Instance?.InstanceSettings;
            if (settings == null)
            {
                return basePoints;
            }

            settings.ResolveRaidPointTuning(faction, out float multiplier, out float minRaidPoints);
            float tunedPoints = basePoints * multiplier;
            return tunedPoints < minRaidPoints ? minRaidPoints : tunedPoints;
        }

        private static bool IsArrivalModeCompatible(PawnsArrivalModeDef arrivalMode, RaidStrategyDef strategy)
        {
            if (arrivalMode == null || strategy == null)
            {
                return false;
            }

            try
            {
                // Check if strategy allows this arrival mode
                if (strategy.arriveModes != null && strategy.arriveModes.Count > 0)
                {
                    return strategy.arriveModes.Contains(arrivalMode);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static RaidStrategyDef GetFallbackStrategy(Faction faction, Map map)
        {
            try
            {
                var allStrategies = DefDatabase<RaidStrategyDef>.AllDefsListForReading;
                var executableStrategies = allStrategies
                    .Where(s => s != null && IsStrategyExecutable(s, faction, map))
                    .ToList();

                if (executableStrategies.Count == 0)
                {
                    return null;
                }

                // Prefer ImmediateAttack as default
                var immediateAttack = executableStrategies.FirstOrDefault(s => s.defName == "ImmediateAttack");
                return immediateAttack ?? executableStrategies.First();
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Error getting fallback strategy: {ex}");
                return null;
            }
        }

        private static PawnsArrivalModeDef GetFallbackArrivalMode(RaidStrategyDef strategy)
        {
            try
            {
                // If strategy specifies allowed arrival modes, use first one
                if (strategy?.arriveModes != null && strategy.arriveModes.Count > 0)
                {
                    return strategy.arriveModes.First();
                }

                // Otherwise use EdgeWalkIn as universal fallback
                return PawnsArrivalModeDefOf.EdgeWalkIn;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Error getting fallback arrival mode: {ex}");
                return PawnsArrivalModeDefOf.EdgeWalkIn;
            }
        }

        public static int CalculateRaidDelayTicks(RaidStrategyDef strategy, PawnsArrivalModeDef arrivalMode)
        {
            // Siege strategy usually implies long preparation
            if (strategy != null && strategy.defName.ToLower().Contains("siege"))
            {
                return Rand.Range(15000, 20000); // 6~8 hours
            }

            // EdgeWalkIn implies travel
            if (arrivalMode == PawnsArrivalModeDefOf.EdgeWalkIn)
            {
                return Rand.Range(15000, 20000); // 6~8 hours
            }
            
            // DropPods (CenterDrop, EdgeDrop, etc.) are fast
            if (arrivalMode != null && arrivalMode.defName.ToLower().Contains("drop"))
            {
                return Rand.Range(2500, 5000); // 1~2 hours
            }

            // Default fallback
            return Rand.Range(10000, 15000);
        }

        public static bool ScheduleDelayedRaid(Faction faction, float points, RaidStrategyDef strategy, PawnsArrivalModeDef arrivalMode)
        {
            try
            {
                int delayTicks = CalculateRaidDelayTicks(strategy, arrivalMode);
                int executeTick = Find.TickManager.TicksGame + delayTicks;

                var evt = new DelayedDiplomacyEvent(DelayedEventType.Raid, faction, executeTick)
                {
                    RaidPoints = points,
                    RaidStrategy = strategy,
                    ArrivalMode = arrivalMode
                };

                GameComponent_DiplomacyManager.Instance?.AddDelayedEvent(evt);

                float delayHours = delayTicks / 2500f;
                string strategyLabel = strategy?.label ?? "Standard";
                DiplomacyNotificationManager.SendDelayedEventScheduledNotification(faction, DelayedEventType.Raid, strategyLabel, delayHours);

                Log.Message($"[RimChat] Scheduled delayed raid from {faction.Name}, strategy={strategy?.defName}, delay={delayHours:F1} hours");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Error scheduling delayed raid: {ex}");
                return false;
            }
        }

        /// <summary>/// 调度"呼叫所有人"袭击：敌友统一 16-30 小时窗口；当敌对数量不足时优先剔除最低好感友中立
        ///</summary>
        public static bool ScheduleRaidCallEveryone(Faction sourceFaction, System.Collections.Generic.List<Faction> targetFactions)
        {
            try
            {
                if (targetFactions == null || targetFactions.Count == 0)
                {
                    Log.Warning("[RimChat] ScheduleRaidCallEveryone: No target factions provided.");
                    return false;
                }

                int currentTick = Find.TickManager.TicksGame;
                int windowStartTick = currentTick + (16 * 2500);
                int windowTicks = 14 * 2500; // 16-30 hours
                List<Faction> effectiveFactions = BalanceCallEveryoneParticipants(targetFactions);
                if (effectiveFactions.Count == 0)
                {
                    Log.Warning("[RimChat] ScheduleRaidCallEveryone: No effective factions after balancing.");
                    return false;
                }
                
                // 收集目标派系 defName
                var targetDefNames = effectiveFactions.Select(f => f.def?.defName).Where(n => !string.IsNullOrEmpty(n)).ToList();
                
                // 为每个派系创建延迟事件，统一随机分布在 16-30 小时窗口内
                foreach (var targetFaction in effectiveFactions)
                {
                    bool isNeutralOrBetter = targetFaction.PlayerGoodwill >= 0;
                    int randomOffset = Rand.Range(0, windowTicks);
                    int executeTick = windowStartTick + randomOffset;
                    
                    var evt = new DelayedDiplomacyEvent(DelayedEventType.RaidCallEveryone, targetFaction, executeTick)
                    {
                        RaidPoints = -1, // 自动计算
                        RaidStrategy = null, // 自动选择
                        ArrivalMode = null,
                        TargetFactionDefNames = targetDefNames,
                        CurrentTargetIndex = targetDefNames.IndexOf(targetFaction.def?.defName),
                        MaxRetryCount = 0,
                        CallEveryoneAction = isNeutralOrBetter
                            ? CallEveryoneActionKind.MilitaryAidCustom
                            : CallEveryoneActionKind.Raid
                    };
                    
                    GameComponent_DiplomacyManager.Instance?.AddDelayedEvent(evt);

                    int announceDelay = Rand.Range(2 * 2500, 8 * 2500);
                    int announceTick = currentTick + announceDelay;
                    var announceEvt = new DelayedDiplomacyEvent(DelayedEventType.RaidCallEveryoneAnnounce, targetFaction, announceTick);
                    GameComponent_DiplomacyManager.Instance?.AddDelayedEvent(announceEvt);
                }
                
                // 统计敌对和友好派系数量
                int hostileCount = effectiveFactions.Count(f => f.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile);
                int friendlyCount = effectiveFactions.Count - hostileCount;
                
                Log.Message($"[RimChat] Scheduled raid_call_everyone: {effectiveFactions.Count} factions " +
                           $"({hostileCount} hostile, {friendlyCount} friendly/neutral), " +
                           $"all arrivals scheduled in 16-30 hours window; friendly/neutral uses custom military aid.");

                Faction notifyFaction = sourceFaction ?? effectiveFactions.FirstOrDefault();
                if (notifyFaction != null)
                {
                    string detail = $"{hostileCount}|{friendlyCount}|16|30";
                    DiplomacyNotificationManager.SendDelayedEventScheduledNotification(
                        notifyFaction,
                        DelayedEventType.RaidCallEveryone,
                        detail,
                        0f);
                }

                Faction socialPostFaction = sourceFaction ?? notifyFaction;
                if (socialPostFaction != null)
                {
                    TryEnqueueRaidCallEveryoneSocialPost(socialPostFaction, isFollowup: false);
                    ScheduleRaidCallEveryoneFollowupSocialPost(socialPostFaction, currentTick);
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Error scheduling raid_call_everyone: {ex}");
                return false;
            }
        }

        private static List<Faction> BalanceCallEveryoneParticipants(List<Faction> targetFactions)
        {
            List<Faction> effective = targetFactions
                .Where(f => f != null && !f.defeated && f.def != null)
                .ToList();

            int hostileCount = effective.Count(f => f.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile);
            List<Faction> friendlyOrNeutral = effective
                .Where(f => f.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile)
                .OrderBy(f => f.PlayerGoodwill)
                .ThenBy(f => f.Name)
                .ToList();

            foreach (Faction faction in friendlyOrNeutral)
            {
                int currentFriendly = effective.Count(f => f.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile);
                if (hostileCount > currentFriendly)
                {
                    break;
                }

                effective.Remove(faction);
                Log.Message($"[RimChat][CallEveryoneBalance] removed={faction.Name}, goodwill={faction.PlayerGoodwill}, hostile={hostileCount}, friendlyNeutralAfter={currentFriendly - 1}");
            }

            return effective;
        }

        internal static bool TryEnqueueRaidCallEveryoneSocialPost(Faction sourceFaction, bool isFollowup)
        {
            if (sourceFaction == null || sourceFaction.defeated)
            {
                return false;
            }

            GameComponent_DiplomacyManager manager = GameComponent_DiplomacyManager.Instance;
            if (manager == null)
            {
                Log.Warning($"[RimChat][CallEveryoneSocialPost] manager unavailable, faction={sourceFaction.Name}, followup={isFollowup}");
                return false;
            }

            string summary = isFollowup
                ? "RimChat_RaidCallEveryoneSocialPostFollowup".Translate(sourceFaction.Name)
                : "RimChat_RaidCallEveryoneSocialPostImmediate".Translate(sourceFaction.Name);

            bool queued = manager.EnqueuePublicPost(
                sourceFaction,
                Faction.OfPlayer,
                SocialPostCategory.Military,
                sentiment: -2,
                summary: summary,
                isFromPlayerDialogue: false,
                reason: DebugGenerateReason.DialogueExplicit);

            Log.Message($"[RimChat][CallEveryoneSocialPost] faction={sourceFaction.Name}, followup={isFollowup}, queued={queued}");
            return queued;
        }

        internal static bool TryEnqueueRaidWavesFirstArrivalSocialPost(Faction sourceFaction, int totalWaves)
        {
            if (sourceFaction == null || sourceFaction.defeated)
            {
                return false;
            }

            GameComponent_DiplomacyManager manager = GameComponent_DiplomacyManager.Instance;
            if (manager == null)
            {
                Log.Warning($"[RimChat][RaidWavesSocialPost] manager unavailable, faction={sourceFaction.Name}, totalWaves={totalWaves}");
                return false;
            }

            int safeTotalWaves = Math.Max(2, totalWaves);
            string summary = "RimChat_RaidWavesFirstArrivalSocialPost".Translate(sourceFaction.Name, safeTotalWaves);
            bool queued = manager.EnqueuePublicPost(
                sourceFaction,
                Faction.OfPlayer,
                SocialPostCategory.Military,
                sentiment: -2,
                summary: summary,
                isFromPlayerDialogue: false,
                reason: DebugGenerateReason.DialogueExplicit);

            Log.Message($"[RimChat][RaidWavesSocialPost] faction={sourceFaction.Name}, totalWaves={safeTotalWaves}, queued={queued}");
            return queued;
        }

        private static void ScheduleRaidCallEveryoneFollowupSocialPost(Faction sourceFaction, int currentTick)
        {
            if (sourceFaction == null || sourceFaction.defeated)
            {
                return;
            }

            int executeTick = currentTick + (36 * 2500);
            var evt = new DelayedDiplomacyEvent(DelayedEventType.RaidCallEveryoneSocialPost, sourceFaction, executeTick)
            {
                MaxRetryCount = 3
            };
            GameComponent_DiplomacyManager.Instance?.AddDelayedEvent(evt);
            Log.Message($"[RimChat][CallEveryoneSocialPost] Scheduled follow-up social post for {sourceFaction.Name} at tick {executeTick}");
        }

        private static bool TryBuildCallEveryoneAidParms(
            Faction faction,
            out Map map,
            out IncidentParms aidParms,
            out string reason)
        {
            map = Find.AnyPlayerHomeMap;
            aidParms = null;

            if (map == null)
            {
                reason = "NoPlayerHomeMap";
                return false;
            }

            if (faction == null || faction.defeated)
            {
                reason = "InvalidFaction";
                return false;
            }

            if (faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile)
            {
                reason = "HostileFactionNotAllowedForAid";
                return false;
            }

            float aidPoints = Math.Max(35f, StorytellerUtility.DefaultThreatPointsNow(map) * 0.5f);
            aidParms = BuildRaidIncidentParmsWithDefaults(
                IncidentDefOf.RaidEnemy,
                map,
                faction,
                aidPoints,
                strategy: null,
                arrivalMode: PawnsArrivalModeDefOf.EdgeWalkIn);

            if (aidParms == null)
            {
                reason = "FailedToBuildIncidentParms";
                return false;
            }

            if (!EnsureUsableCombatPawnGroupMakerForParms(faction, aidParms, out string groupReason))
            {
                reason = $"NoUsableCombatMaker:{groupReason}";
                return false;
            }

            reason = "OK";
            return true;
        }

        private static bool TryGenerateCallEveryoneAidPawns(
            IncidentParms aidParms,
            out List<Pawn> pawns,
            out string reason)
        {
            pawns = new List<Pawn>();
            if (aidParms == null || aidParms.faction == null)
            {
                reason = "InvalidAidParms";
                return false;
            }

            PawnGroupMakerParms groupParms = BuildRaidGroupMakerParms(aidParms, out string groupReason);
            if (groupParms == null)
            {
                reason = $"BuildGroupParmsFailed:{groupReason}";
                return false;
            }

            try
            {
                pawns = PawnGroupMakerUtility.GeneratePawns(groupParms, warnOnZeroResults: false)
                    .Where(p => p != null)
                    .ToList();
            }
            catch (Exception ex)
            {
                reason = $"GeneratePawnsException:{ex.Message}";
                return false;
            }

            if (pawns.Count == 0)
            {
                reason = "GeneratedPawnCountZero";
                return false;
            }

            reason = "OK";
            return true;
        }

        private static bool TryArriveCallEveryoneAidPawns(
            Map map,
            IncidentParms aidParms,
            List<Pawn> pawns,
            out string reason)
        {
            if (map == null || aidParms == null || aidParms.faction == null || pawns == null || pawns.Count == 0)
            {
                reason = "InvalidArrivalInput";
                return false;
            }

            if (!TryFindCallEveryoneAidEntryCell(map, out IntVec3 entryCell, out string entryReason))
            {
                reason = $"NoValidEntryCell:{entryReason}";
                return false;
            }

            int attempted = 0;
            int spawnFailed = 0;
            List<Pawn> spawned = new List<Pawn>();
            try
            {
                foreach (Pawn pawn in pawns)
                {
                    if (pawn == null || pawn.Dead || pawn.Destroyed)
                    {
                        continue;
                    }

                    attempted++;
                    if (pawn.Spawned)
                    {
                        if (pawn.Map == map)
                        {
                            spawned.Add(pawn);
                        }

                        continue;
                    }

                    if (!TrySpawnAidPawnNearEntry(map, entryCell, pawn))
                    {
                        spawnFailed++;
                        continue;
                    }

                    if (pawn.Spawned && pawn.Map == map && !pawn.Dead && !pawn.Destroyed)
                    {
                        spawned.Add(pawn);
                    }
                }

                if (spawned.Count == 0)
                {
                    reason = $"NoPawnSpawnedAfterManualSpawn;entry={entryCell};attempted={attempted};spawnFailed={spawnFailed}";
                    return false;
                }

                IntVec3 rallyCell = spawned[0].Position;
                var assistJob = new LordJob_AssistColony(Faction.OfPlayer, rallyCell);
                LordMaker.MakeNewLord(aidParms.faction, assistJob, map, spawned);
            }
            catch (Exception ex)
            {
                reason = $"ManualSpawnException:{ex.Message}";
                return false;
            }

            reason = "OK";
            return true;
        }

        private static bool TryFindCallEveryoneAidEntryCell(Map map, out IntVec3 entryCell, out string reason)
        {
            entryCell = IntVec3.Invalid;
            reason = "NoCandidate";

            if (map == null)
            {
                reason = "MapNull";
                return false;
            }

            bool foundEdge = CellFinder.TryFindRandomEdgeCellWith(
                c => c.InBounds(map) && c.Standable(map) && c.Walkable(map),
                map,
                0f,
                out entryCell);
            if (foundEdge && entryCell.IsValid && entryCell.InBounds(map))
            {
                reason = "OK";
                return true;
            }

            IntVec3 fallback = DropCellFinder.TradeDropSpot(map);
            if (fallback.IsValid && fallback.InBounds(map) && fallback.Standable(map))
            {
                entryCell = fallback;
                reason = "TradeDropSpotFallback";
                return true;
            }

            reason = "EdgeAndFallbackInvalid";
            return false;
        }

        private static bool TrySpawnAidPawnNearEntry(Map map, IntVec3 entryCell, Pawn pawn)
        {
            if (map == null || pawn == null || !entryCell.IsValid || !entryCell.InBounds(map))
            {
                return false;
            }

            bool foundCell = CellFinder.TryFindRandomSpawnCellForPawnNear(
                entryCell,
                map,
                out IntVec3 spawnCell,
                12,
                c => c.InBounds(map) && c.Standable(map) && c.Walkable(map) && !c.Fogged(map));
            if (!foundCell)
            {
                spawnCell = entryCell;
            }

            if (!spawnCell.IsValid || !spawnCell.InBounds(map) || !spawnCell.Standable(map) || !spawnCell.Walkable(map))
            {
                return false;
            }

            try
            {
                GenSpawn.Spawn(pawn, spawnCell, map, WipeMode.Vanish);
                return pawn.Spawned && pawn.Map == map;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>/// 调度袭击波次：n 次袭击，每次间隔 12-20 小时
        ///</summary>
        public static bool ScheduleRaidWaves(Faction faction, int waves)
        {
            try
            {
                if (faction == null)
                {
                    return false;
                }
                
                int currentTick = Find.TickManager.TicksGame;
                int accumulatedDelay = 0;
                
                for (int i = 0; i < waves; i++)
                {
                    // 每波间隔 12-20 小时
                    int intervalTicks = Rand.Range(12 * 2500, 20 * 2500);
                    accumulatedDelay += intervalTicks;
                    
                    int executeTick = currentTick + accumulatedDelay;
                    
                    var evt = new DelayedDiplomacyEvent(DelayedEventType.RaidWave, faction, executeTick)
                    {
                        RaidPoints = -1,
                        RaidStrategy = null,
                        ArrivalMode = null,
                        WaveIndex = i,
                        TotalWaves = waves
                    };
                    
                    GameComponent_DiplomacyManager.Instance?.AddDelayedEvent(evt);
                }

                int firstWaveMinHours = 12;
                int firstWaveMaxHours = 20;
                int finalWaveMinHours = waves * 12;
                int finalWaveMaxHours = waves * 20;
                string detail = $"{waves}|{firstWaveMinHours}|{firstWaveMaxHours}|{finalWaveMinHours}|{finalWaveMaxHours}";
                DiplomacyNotificationManager.SendDelayedEventScheduledNotification(
                    faction,
                    DelayedEventType.RaidWave,
                    detail,
                    0f);
                
                float firstWaveHours = 12f;
                float lastWaveHours = accumulatedDelay / 2500f;
                
                Log.Message($"[RimChat] Scheduled raid_waves from {faction.Name}: {waves} waves, " +
                           $"first wave in ~{firstWaveHours:F0}h, last wave in ~{lastWaveHours:F0}h, " +
                           $"end message will be sent after final wave departure");
                
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Error scheduling raid waves: {ex}");
                return false;
            }
        }
    }
}
