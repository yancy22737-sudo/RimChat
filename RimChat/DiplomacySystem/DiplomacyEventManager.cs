using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
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

    public static class DiplomacyEventManager
    {
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
            List<TraderKindDef> allTraders = DefDatabase<TraderKindDef>.AllDefsListForReading;
            
            List<TraderKindDef> matchingTraders = new List<TraderKindDef>();
            
            foreach (TraderKindDef trader in allTraders)
            {
                if (trader.orbital) continue;
                
                bool matches = false;
                switch (caravanType)
                {
                    case CaravanType.General:
                        matches = trader.defName.ToLower().Contains("standard") || 
                                  trader.defName.ToLower().Contains("general");
                        break;
                    case CaravanType.BulkGoods:
                        matches = trader.defName.ToLower().Contains("bulk");
                        break;
                    case CaravanType.CombatSupplier:
                        matches = trader.defName.ToLower().Contains("combat") || 
                                  trader.defName.ToLower().Contains("weapon");
                        break;
                    case CaravanType.Exotic:
                        matches = trader.defName.ToLower().Contains("exotic");
                        break;
                    case CaravanType.Slaver:
                        matches = trader.defName.ToLower().Contains("slave");
                        break;
                }
                
                if (matches)
                {
                    matchingTraders.Add(trader);
                }
            }
            
            Log.Message($"[RimChat] Found {matchingTraders.Count} matching traders for {caravanType}");
            foreach (var t in matchingTraders)
            {
                Log.Message($"[RimChat]   - {t.defName}");
            }
            
            if (matchingTraders.Count > 0)
            {
                var selected = matchingTraders.RandomElement();
                Log.Message($"[RimChat] Selected trader: {selected.defName}");
                return selected;
            }
            
            Log.Warning($"[RimChat] No matching traders found for {caravanType}, using null");
            return null;
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

        private static bool TriggerMilitaryAid(Faction faction, Map map)
        {
            IncidentParms parms = new IncidentParms();
            parms.target = map;
            parms.faction = faction;
            parms.points = StorytellerUtility.DefaultThreatPointsNow(map) * 0.5f;

            IncidentDef militaryAidDef = IncidentDef.Named("FriendlyRaid");
            if (militaryAidDef != null && militaryAidDef.Worker != null)
            {
                bool success = militaryAidDef.Worker.TryExecute(parms);
                if (success)
                {
                    Log.Message($"[RimChat] Triggered military aid from {faction.Name}");
                    SendAidLetter(faction, "MilitaryAidArrived".Translate(), 
                        $"{faction.Name} has sent military reinforcements to aid your colony!");
                }
                return success;
            }
            else
            {
                SendAidLetter(faction, "AidOffered".Translate(), 
                    $"{faction.Name} has offered military aid, but their forces are delayed.");
                return true;
            }
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

            SendAidLetter(faction, "MedicalAidArrived".Translate(), 
                $"{faction.Name} has sent medical supplies via drop pod to aid your colony!");
            
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

            SendAidLetter(faction, "ResourceAidArrived".Translate(), 
                $"{faction.Name} has sent resource supplies via drop pod to aid your colony!");
            
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
            Find.LetterStack.ReceiveLetter(
                title,
                message,
                LetterDefOf.PositiveEvent,
                null,
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
                Map map = Find.AnyPlayerHomeMap;
                if (map == null)
                {
                    Log.Warning("[RimChat] No player home map found for raid event");
                    return false;
                }

                // Calculate points if not provided or invalid
                float raidPoints = points;
                if (raidPoints <= 0)
                {
                    raidPoints = StorytellerUtility.DefaultThreatPointsNow(map) * 0.5f;
                }

                IncidentParms parms = new IncidentParms
                {
                    target = map,
                    faction = faction,
                    points = raidPoints,
                    raidStrategy = strategy,
                    raidArrivalMode = arrivalMode,
                    forced = true
                };

                IncidentDef incidentDef = IncidentDefOf.RaidEnemy;
                bool success = incidentDef.Worker.TryExecute(parms);

                if (success)
                {
                    Log.Message($"[RimChat] Triggered raid from {faction.Name} with strategy {strategy?.defName} and arrival {arrivalMode?.defName}");
                }
                else
                {
                    Log.Warning($"[RimChat] Failed to trigger raid from {faction.Name}");
                }

                return success;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimChat] Error triggering raid event: {ex}");
                return false;
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
    }
}
