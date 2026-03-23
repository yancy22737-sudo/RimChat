using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: DefDatabase<ThingDef>.
    /// Responsibility: provide a cached searchable catalog of spawnable ThingDef records.
    /// </summary>
    internal static class ThingDefCatalog
    {
        private static int lastBuildTick = -1;
        private static readonly List<ThingDefRecord> records = new List<ThingDefRecord>();
        private const int RefreshIntervalTicks = 30000;

        public static IReadOnlyList<ThingDefRecord> GetRecords()
        {
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (records.Count == 0 || tick - lastBuildTick > RefreshIntervalTicks)
            {
                Rebuild();
                lastBuildTick = tick;
            }

            return records;
        }

        public static bool CanCandidateForNeed(ThingDefRecord record, ItemAirdropNeedFamily family)
        {
            return ItemAirdropSafetyPolicy.CanCandidateForNeed(record, family);
        }

        private static void Rebuild()
        {
            records.Clear();
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            if (defs == null)
            {
                return;
            }

            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                if (!IsSpawnableItemDef(def))
                {
                    continue;
                }

                records.Add(ThingDefRecord.From(def));
            }
        }

        private static bool IsSpawnableItemDef(ThingDef def)
        {
            if (def == null || def.destroyOnDrop || def.IsBlueprint || def.IsFrame)
            {
                return false;
            }

            if (def.category != ThingCategory.Item)
            {
                return false;
            }

            if (def.tradeability == Tradeability.None && def.BaseMarketValue <= 0f)
            {
                return false;
            }

            // Corpse defs flood the pool and create false near-miss noise for item airdrop.
            if (def.IsCorpse)
            {
                return false;
            }

            if (def.minifiedDef != null || def.plant != null)
            {
                return false;
            }

            return true;
        }
    }

    internal sealed class ThingDefRecord
    {
        public ThingDef Def { get; private set; }
        public string DefName { get; private set; }
        public string Label { get; private set; }
        public float MarketValue { get; private set; }
        public int StackLimit { get; private set; }
        public string TechLevelName { get; private set; }
        public string SearchText { get; private set; }

        public static ThingDefRecord From(ThingDef def)
        {
            return new ThingDefRecord
            {
                Def = def,
                DefName = def.defName ?? string.Empty,
                Label = def.label ?? def.defName ?? string.Empty,
                MarketValue = Math.Max(0.01f, def.BaseMarketValue),
                StackLimit = Math.Max(1, def.stackLimit),
                TechLevelName = def.techLevel.ToString(),
                SearchText = BuildSearchText(def)
            };
        }

        private static string BuildSearchText(ThingDef def)
        {
            var parts = new List<string>
            {
                def.defName ?? string.Empty,
                def.label ?? string.Empty,
                def.techLevel.ToString(),
                def.IsMedicine ? "medicine heal medical" : string.Empty,
                def.IsDrug ? "drug medicine" : string.Empty,
                def.IsWeapon ? "weapon gun melee combat" : string.Empty,
                def.IsApparel ? "apparel armor cloth" : string.Empty,
                def.IsNutritionGivingIngestible ? "food meal nutrition ingestible" : string.Empty
            };

            if (def.thingCategories != null)
            {
                parts.AddRange(def.thingCategories.Where(x => x != null).Select(x => x.defName));
                parts.AddRange(def.thingCategories.Where(x => x != null).Select(x => x.label));
            }

            if (def.weaponTags != null)
            {
                parts.AddRange(def.weaponTags);
            }

            return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))).ToLowerInvariant();
        }
    }
}
