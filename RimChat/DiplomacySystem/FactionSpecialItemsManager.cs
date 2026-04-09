using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: Verse.IExposable, Verse.Find, RimChat.DiplomacySystem.ThingDefCatalog, RimChat.DiplomacySystem.GameAIInterface.
    /// Responsibility: manage per-faction special item slots (discount/scarce), generation, refresh, and persistence.
    /// </summary>
    public class FactionSpecialItemSet : IExposable
    {
        public FactionSpecialItemSlot DiscountItem;
        public FactionSpecialItemSlot ScarceItem;

        private int factionLoadId = -1;

        public FactionSpecialItemSet() { }

        public FactionSpecialItemSet(Faction faction)
        {
            factionLoadId = faction?.loadID ?? -1;
            DiscountItem = new FactionSpecialItemSlot(SpecialItemType.Discount);
            ScarceItem = new FactionSpecialItemSlot(SpecialItemType.Scarce);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref factionLoadId, "factionLoadId", -1);
            Scribe_Deep.Look(ref DiscountItem, "discountItem");
            Scribe_Deep.Look(ref ScarceItem, "scarceItem");
        }

        public bool TryMatchSpecialItem(string defName, out SpecialItemType itemType)
        {
            itemType = SpecialItemType.Discount;
            if (string.IsNullOrEmpty(defName)) return false;

            if (DiscountItem != null && DiscountItem.MatchesDefName(defName) && DiscountItem.IsAvailable)
            {
                itemType = SpecialItemType.Discount;
                return true;
            }

            if (ScarceItem != null && ScarceItem.MatchesDefName(defName) && ScarceItem.IsAvailable)
            {
                itemType = SpecialItemType.Scarce;
                return true;
            }

            return false;
        }

        public void MarkTraded(SpecialItemType itemType, int cooldownTicks)
        {
            var slot = itemType == SpecialItemType.Discount ? DiscountItem : ScarceItem;
            if (slot == null) return;

            slot.IsTraded = true;
            slot.SetCooldown(cooldownTicks);
        }

        public void RefreshIfNeeded(Faction faction)
        {
            if (faction == null) return;

            int cooldownTicks = GameAIInterface.Instance.GetItemAirdropCooldownTicks(faction);

            if (NeedsRefresh(DiscountItem))
            {
                RefreshSlot(DiscountItem, faction, SpecialItemType.Discount);
            }

            if (NeedsRefresh(ScarceItem))
            {
                RefreshSlot(ScarceItem, faction, SpecialItemType.Scarce);
            }
        }

        private bool NeedsRefresh(FactionSpecialItemSlot slot)
        {
            if (slot == null) return true;
            if (string.IsNullOrEmpty(slot.DefName)) return true;
            if (slot.IsExpired) return true;
            return false;
        }

        private void RefreshSlot(FactionSpecialItemSlot slot, Faction faction, SpecialItemType itemType)
        {
            if (slot == null) return;

            var newItem = FactionSpecialItemsManager.GenerateRandomItem(faction, itemType);
            if (newItem == null) return;

            slot.DefName = newItem.DefName;
            slot.Label = newItem.Label;
            slot.BasePrice = newItem.BasePrice;
            slot.GeneratedTick = newItem.GeneratedTick;
            slot.CooldownEndTick = 0;
            slot.IsTraded = false;
        }
    }

    public class FactionSpecialItemsManager : IExposable
    {
        internal static FactionSpecialItemsManager _instance;
        public static FactionSpecialItemsManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new FactionSpecialItemsManager();
                }
                return _instance;
            }
            set { _instance = value; }
        }

        private Dictionary<int, FactionSpecialItemSet> factionItems = new Dictionary<int, FactionSpecialItemSet>();
        private HashSet<int> revealedFactions = new HashSet<int>();
        private int lastTickCheck = 0;
        private const int TickCheckInterval = 60000; // Check every day

        public FactionSpecialItemsManager() { }

        public void ExposeData()
        {
            List<int> factionIds = null;
            List<FactionSpecialItemSet> itemSets = null;

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                factionIds = factionItems.Keys.ToList();
                itemSets = factionItems.Values.ToList();
            }

            Scribe_Collections.Look(ref factionIds, "factionIds", LookMode.Value);
            Scribe_Collections.Look(ref itemSets, "itemSets", LookMode.Deep);
            Scribe_Collections.Look(ref revealedFactions, "revealedFactions", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                factionItems.Clear();
                if (factionIds != null && itemSets != null)
                {
                    for (int i = 0; i < Math.Min(factionIds.Count, itemSets.Count); i++)
                    {
                        if (factionIds[i] >= 0 && itemSets[i] != null)
                        {
                            factionItems[factionIds[i]] = itemSets[i];
                        }
                    }
                }
                
                if (revealedFactions == null)
                {
                    revealedFactions = new HashSet<int>();
                }
            }
        }

        public void MarkRevealed(Faction faction)
        {
            if (faction == null || faction.IsPlayer) return;
            revealedFactions.Add(faction.loadID);
        }

        public bool IsRevealed(Faction faction)
        {
            if (faction == null || faction.IsPlayer) return false;
            return revealedFactions.Contains(faction.loadID);
        }

        public FactionSpecialItemSet GetOrCreate(Faction faction)
        {
            if (faction == null) return null;

            int loadId = faction.loadID;
            if (!factionItems.TryGetValue(loadId, out var itemSet))
            {
                itemSet = new FactionSpecialItemSet(faction);
                factionItems[loadId] = itemSet;
            }

            return itemSet;
        }

        public void Tick()
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick - lastTickCheck < TickCheckInterval) return;
            lastTickCheck = currentTick;

            var allFactions = Find.FactionManager?.AllFactionsVisible;
            if (allFactions == null) return;

            foreach (var faction in allFactions)
            {
                if (faction == null || faction.IsPlayer) continue;
                if (!faction.Hidden)
                {
                    var itemSet = GetOrCreate(faction);
                    itemSet?.RefreshIfNeeded(faction);
                }
            }
        }

        public (string discountText, string scarceText) GetHoverCardDisplay(Faction faction)
        {
            var itemSet = GetOrCreate(faction);
            itemSet?.RefreshIfNeeded(faction);
            
            string discountText = GetSlotDisplayText(faction, SpecialItemType.Discount);
            string scarceText = GetSlotDisplayText(faction, SpecialItemType.Scarce);
            return (discountText, scarceText);
        }

        private string GetSlotDisplayText(Faction faction, SpecialItemType itemType)
        {
            var itemSet = GetOrCreate(faction);
            if (itemSet == null) return "N/A";

            var slot = itemType == SpecialItemType.Discount ? itemSet.DiscountItem : itemSet.ScarceItem;
            if (slot == null || string.IsNullOrEmpty(slot.DefName))
            {
                return "N/A";
            }

            // Show ??? during cooldown period after trade (intel insufficient)
            if (slot.IsTraded && slot.IsOnCooldown)
            {
                string cooldownTime = GetCooldownTimeDisplay(slot);
                return "RimChat_SpecialItemIntelInsufficient".Translate(cooldownTime);
            }

            // Show item name with price tag and refresh countdown
            string priceTag = GetPriceTagDisplay(itemType);
            string refreshCountdown = GetRefreshCountdownDisplay(slot);
            
            if (!string.IsNullOrEmpty(refreshCountdown))
            {
                return "RimChat_SpecialItemWithRefresh".Translate(slot.Label, priceTag, refreshCountdown);
            }
            
            return "RimChat_SpecialItemAvailable".Translate(slot.Label, priceTag);
        }

        private static string GetCooldownTimeDisplay(FactionSpecialItemSlot slot)
        {
            if (slot == null || slot.CooldownEndTick <= 0) return string.Empty;
            
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            int remainingTicks = slot.CooldownEndTick - currentTick;
            
            if (remainingTicks <= 0) return string.Empty;
            
            // Format as dd:hh
            int days = remainingTicks / GenDate.TicksPerDay;
            int hours = (remainingTicks % GenDate.TicksPerDay) / GenDate.TicksPerHour;
            
            return "RimChat_SpecialItemRefreshTime".Translate(days, hours);
        }

        private static string GetPriceTagDisplay(SpecialItemType itemType)
        {
            // Discount: -60%, Scarce: +100%
            return itemType == SpecialItemType.Discount 
                ? "RimChat_SpecialItemDiscountTag".Translate() 
                : "RimChat_SpecialItemScarceTag".Translate();
        }

        private static string GetRefreshCountdownDisplay(FactionSpecialItemSlot slot)
        {
            if (slot == null) return string.Empty;
            
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            int remainingTicks = 0;
            
            // If traded and on cooldown, show cooldown remaining
            if (slot.IsTraded && slot.CooldownEndTick > 0)
            {
                remainingTicks = slot.CooldownEndTick - currentTick;
                if (remainingTicks <= 0) return string.Empty;
            }
            else
            {
                // Not traded - calculate time until expiry (3 days from generation)
                if (slot.GeneratedTick <= 0) return string.Empty;
                int expiryTick = slot.GeneratedTick + (3 * GenDate.TicksPerDay);
                remainingTicks = expiryTick - currentTick;
                if (remainingTicks <= 0) return string.Empty;
            }
            
            // Format as dd:hh
            int days = remainingTicks / GenDate.TicksPerDay;
            int hours = (remainingTicks % GenDate.TicksPerDay) / GenDate.TicksPerHour;
            
            return "RimChat_SpecialItemRefreshTime".Translate(days, hours);
        }

        private static bool IsOnCooldown(FactionSpecialItemSlot slot)
        {
            if (slot == null || string.IsNullOrEmpty(slot.DefName)) return false;
            if (slot.IsTraded && slot.IsOnCooldown)
            {
                return true;
            }

            return false;
        }

        public bool TryMatchSpecialItem(Faction faction, string defName, out SpecialItemType itemType)
        {
            itemType = SpecialItemType.Discount;
            var itemSet = GetOrCreate(faction);
            if (itemSet == null) return false;

            return itemSet.TryMatchSpecialItem(defName, out itemType);
        }

        public string BuildSpecialItemsPromptBlock(Faction faction)
        {
            if (faction == null || faction.IsPlayer) return string.Empty;
            
            var itemSet = GetOrCreate(faction);
            itemSet?.RefreshIfNeeded(faction);
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== FACTION SPECIAL ITEMS ===");
            
            bool factionRevealed = IsRevealed(faction);
            bool discountRevealed = factionRevealed &&
                itemSet?.DiscountItem != null && 
                !string.IsNullOrEmpty(itemSet.DiscountItem.DefName) && 
                !(itemSet.DiscountItem.IsTraded && itemSet.DiscountItem.IsOnCooldown);
            
            if (discountRevealed)
            {
                sb.AppendLine($"DiscountItem: {itemSet.DiscountItem.Label}");
                sb.AppendLine($"DiscountItemDefName: {itemSet.DiscountItem.DefName}");
                sb.AppendLine("DiscountItemNote: This item is available at 40% of market price (60% discount).");
            }
            else if (itemSet?.DiscountItem != null && !string.IsNullOrEmpty(itemSet.DiscountItem.DefName))
            {
                sb.AppendLine("DiscountItem: [INTEL INSUFFICIENT]");
                sb.AppendLine("DiscountItemNote: Discount item info is not available due to insufficient intelligence clearance.");
            }
            else
            {
                sb.AppendLine("DiscountItem: None available");
            }
            
            bool scarceRevealed = factionRevealed &&
                itemSet?.ScarceItem != null && 
                !string.IsNullOrEmpty(itemSet.ScarceItem.DefName) && 
                !(itemSet.ScarceItem.IsTraded && itemSet.ScarceItem.IsOnCooldown);
            
            if (scarceRevealed)
            {
                sb.AppendLine($"ScarceItem: {itemSet.ScarceItem.Label}");
                sb.AppendLine($"ScarceItemDefName: {itemSet.ScarceItem.DefName}");
                sb.AppendLine("ScarceItemNote: This item is available at 200% of market price (premium for scarcity).");
            }
            else if (itemSet?.ScarceItem != null && !string.IsNullOrEmpty(itemSet.ScarceItem.DefName))
            {
                sb.AppendLine("ScarceItem: [INTEL INSUFFICIENT]");
                sb.AppendLine("ScarceItemNote: Scarce item info is not available due to insufficient intelligence clearance.");
            }
            else
            {
                sb.AppendLine("ScarceItem: None available");
            }
            
            return sb.ToString().TrimEnd();
        }

        public void MarkTraded(Faction faction, SpecialItemType itemType)
        {
            var itemSet = GetOrCreate(faction);
            if (itemSet == null) return;

            int cooldownTicks = GameAIInterface.Instance.GetItemAirdropCooldownTicks(faction);
            itemSet.MarkTraded(itemType, cooldownTicks);
        }

        internal static FactionSpecialItemSlot GenerateRandomItem(Faction faction, SpecialItemType itemType)
        {
            if (faction == null) return null;

            TechLevel factionTech = faction.def?.techLevel ?? TechLevel.Industrial;
            var candidates = ThingDefCatalog.GetRecords()
                .Where(r => r.Def != null && r.EverPlayerSellable)
                .Where(r => IsTechLevelCompatible(r.Def, factionTech))
                .Where(r => r.MarketValue >= 50f && r.MarketValue <= 2000f)
                .ToList();

            if (candidates.Count == 0)
            {
                candidates = ThingDefCatalog.GetRecords()
                    .Where(r => r.Def != null && r.EverPlayerSellable)
                    .Take(50)
                    .ToList();
            }

            if (candidates.Count == 0) return null;

            var selected = WeightedRandomSelect(candidates);

            return new FactionSpecialItemSlot(itemType)
            {
                ItemType = itemType,
                DefName = selected.DefName,
                Label = selected.Label,
                BasePrice = selected.MarketValue,
                GeneratedTick = Find.TickManager?.TicksGame ?? 0,
                CooldownEndTick = 0,
                IsTraded = false
            };
        }

        private static bool IsTechLevelCompatible(ThingDef def, TechLevel factionTech)
        {
            if (def == null) return false;
            TechLevel itemTech = def.techLevel;

            // Allow items at or below faction tech level
            return itemTech <= factionTech;
        }

        private static ThingDefRecord WeightedRandomSelect(List<ThingDefRecord> candidates)
        {
            if (candidates == null || candidates.Count == 0) return null;
            if (candidates.Count == 1) return candidates[0];

            // Weight by inverse of market value (prefer mid-range items)
            var weights = candidates.Select(c =>
            {
                float value = c.MarketValue;
                // Peak weight at around 200-500 silver
                float weight = 100f / (1f + Math.Abs(value - 300f) / 100f);
                return Math.Max(1f, weight);
            }).ToArray();

            float totalWeight = weights.Sum();
            float random = Rand.Value * totalWeight;
            float cumulative = 0f;

            for (int i = 0; i < candidates.Count; i++)
            {
                cumulative += weights[i];
                if (random <= cumulative)
                {
                    return candidates[i];
                }
            }

            return candidates[candidates.Count - 1];
        }
    }
}
