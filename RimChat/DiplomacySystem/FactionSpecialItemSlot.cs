using System;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: Verse.IExposable, Verse.Find, RimChat.DiplomacySystem.ThingDefCatalog.
    /// Responsibility: represent a single special item slot (discount or scarce) for a faction with persistence support.
    /// </summary>
    public enum SpecialItemType
    {
        Discount = 0,
        Scarce = 1
    }

    /// <summary>
    /// Represents a special item slot (discount or scarce) for a faction.
    /// Persists per-faction, supports IExposable for save/load.
    /// Note: Does NOT implement ILoadReferenceable to avoid save/load crashes with old saves.
    /// </summary>
    public class FactionSpecialItemSlot : IExposable
    {
        public SpecialItemType ItemType;
        public string DefName = string.Empty;
        public string Label = string.Empty;
        public float BasePrice;
        public int GeneratedTick;
        public int CooldownEndTick;
        public bool IsTraded;

        private int uniqueId = -1;
        private static int nextUniqueId = 1;
        private const int ExpiryDays = 3;

        public FactionSpecialItemSlot()
        {
            EnsureUniqueId();
        }

        public FactionSpecialItemSlot(SpecialItemType itemType)
        {
            ItemType = itemType;
            EnsureUniqueId();
        }

        private void EnsureUniqueId()
        {
            if (uniqueId < 0)
            {
                uniqueId = nextUniqueId++;
            }
        }

        internal int GetUniqueIdForFix()
        {
            return uniqueId;
        }

        internal void SetUniqueId(int id)
        {
            uniqueId = id;
        }

        internal static int GetNextUniqueId()
        {
            return nextUniqueId;
        }

        internal static void SetNextUniqueId(int value)
        {
            nextUniqueId = value;
        }

        public bool IsOnCooldown
        {
            get
            {
                if (CooldownEndTick <= 0) return false;
                return Find.TickManager?.TicksGame < CooldownEndTick;
            }
        }

        public bool IsAvailable
        {
            get
            {
                if (string.IsNullOrEmpty(DefName)) return false;
                if (CooldownEndTick > 0 && Find.TickManager?.TicksGame < CooldownEndTick) return false;
                return true;
            }
        }

        public int DaysSinceGeneration
        {
            get
            {
                if (GeneratedTick <= 0) return 0;
                int currentTick = Find.TickManager?.TicksGame ?? 0;
                return (currentTick - GeneratedTick) / GenDate.TicksPerDay;
            }
        }

        public bool IsExpired
        {
            get
            {
                return DaysSinceGeneration >= ExpiryDays && !IsTraded && CooldownEndTick <= 0;
            }
        }

        public int RemainingCooldownTicks
        {
            get
            {
                if (CooldownEndTick <= 0) return 0;
                int currentTick = Find.TickManager?.TicksGame ?? 0;
                return Math.Max(0, CooldownEndTick - currentTick);
            }
        }

        public string RemainingCooldownDisplay
        {
            get
            {
                int ticks = RemainingCooldownTicks;
                if (ticks <= 0) return string.Empty;
                float days = ticks / (float)GenDate.TicksPerDay;
                if (days >= 1f)
                {
                    return $"{days:F1}d";
                }
                float hours = ticks / (float)GenDate.TicksPerHour;
                return $"{hours:F0}h";
            }
        }

        public bool MatchesDefName(string defName)
        {
            if (string.IsNullOrEmpty(DefName) || string.IsNullOrEmpty(defName)) return false;
            return string.Equals(DefName, defName, StringComparison.OrdinalIgnoreCase);
        }

        public void SetCooldown(int cooldownTicks)
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            CooldownEndTick = currentTick + cooldownTicks;
        }

        public void Clear()
        {
            DefName = string.Empty;
            Label = string.Empty;
            BasePrice = 0f;
            GeneratedTick = 0;
            CooldownEndTick = 0;
            IsTraded = false;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref uniqueId, "uniqueId", -1);
            Scribe_Values.Look(ref ItemType, "itemType", SpecialItemType.Discount);
            Scribe_Values.Look(ref DefName, "defName", string.Empty);
            Scribe_Values.Look(ref Label, "label", string.Empty);
            Scribe_Values.Look(ref BasePrice, "basePrice", 0f);
            Scribe_Values.Look(ref GeneratedTick, "generatedTick", 0);
            Scribe_Values.Look(ref CooldownEndTick, "cooldownEndTick", 0);
            Scribe_Values.Look(ref IsTraded, "isTraded", false);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (uniqueId < 0)
                {
                    uniqueId = nextUniqueId++;
                }
                else if (uniqueId >= nextUniqueId)
                {
                    nextUniqueId = uniqueId + 1;
                }
            }
        }
    }
}
