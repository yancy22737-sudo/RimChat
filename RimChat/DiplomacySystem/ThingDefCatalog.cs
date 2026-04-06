using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: DefDatabase<ThingDef>, TradeUtility.
    /// Responsibility: provide cached searchable ThingDef records for airdrop candidates and payment resolution.
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

        public static bool TryGetRecordByDefName(string defName, out ThingDefRecord record)
        {
            record = null;
            string normalized = (defName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            record = GetRecords().FirstOrDefault(candidate =>
                candidate?.Def != null &&
                string.Equals(candidate.DefName, normalized, StringComparison.OrdinalIgnoreCase));
            if (record != null)
            {
                return true;
            }

            ThingDef directDef = DefDatabase<ThingDef>.GetNamedSilentFail(normalized);
            if (!IsFallbackResolvableItemDef(directDef))
            {
                return false;
            }

            record = ThingDefRecord.From(directDef);
            return true;
        }

        public static IReadOnlyList<ThingDefRecord> GetTradeablePaymentRecords()
        {
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            if (defs == null || defs.Count == 0)
            {
                return Array.Empty<ThingDefRecord>();
            }

            return defs
                .Where(IsTradeablePaymentDef)
                .Select(ThingDefRecord.From)
                .GroupBy(record => record.DefName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
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
            if (!IsFallbackResolvableItemDef(def))
            {
                return false;
            }

            if (TradeUtility.EverPlayerSellable(def))
            {
                return true;
            }

            if (def.IsNutritionGivingIngestible ||
                def.IsMedicine ||
                def.IsDrug ||
                def.IsWeapon ||
                def.IsApparel ||
                def.stuffProps != null)
            {
                return true;
            }

            if (def.tradeability != Tradeability.None || def.BaseMarketValue > 0f)
            {
                return true;
            }

            return HasMeaningfulThingCategory(def);
        }

        private static bool IsFallbackResolvableItemDef(ThingDef def)
        {
            if (def == null || def.destroyOnDrop || def.IsBlueprint || def.IsFrame)
            {
                return false;
            }

            if (def.category != ThingCategory.Item)
            {
                return false;
            }

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

        private static bool IsTradeablePaymentDef(ThingDef def)
        {
            return IsFallbackResolvableItemDef(def) && TradeUtility.EverPlayerSellable(def);
        }

        private static bool HasMeaningfulThingCategory(ThingDef def)
        {
            return def?.thingCategories != null && def.thingCategories.Any(category => category != null);
        }
    }

    internal sealed class ThingDefRecord
    {
        private static readonly string[] ImplantSearchKeywords =
        {
            "bionic", "prosthetic", "implant", "artificial", "bodypart", "cybernetic",
            "仿生", "义体", "假肢", "植入", "人工"
        };

        public ThingDef Def { get; private set; }
        public string DefName { get; private set; }
        public string Label { get; private set; }
        public float MarketValue { get; private set; }
        public int StackLimit { get; private set; }
        public string TechLevelName { get; private set; }
        public string SearchText { get; private set; }
        public bool EverPlayerSellable { get; private set; }

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
                EverPlayerSellable = TradeUtility.EverPlayerSellable(def),
                SearchText = BuildSearchText(def)
            };
        }

        private static string BuildSearchText(ThingDef def)
        {
            IEnumerable<string> categoryTokens = EnumerateCategoryTokens(def?.thingCategories);
            var parts = new List<string>
            {
                def.defName ?? string.Empty,
                def.label ?? string.Empty,
                ExpandCamelCase(def.defName),
                ExpandCamelCase(def.label),
                def.techLevel.ToString(),
                def.IsMedicine ? "medicine heal medical" : string.Empty,
                def.IsDrug ? "drug medicine" : string.Empty,
                def.IsWeapon ? "weapon gun melee combat" : string.Empty,
                def.IsApparel ? "apparel armor cloth" : string.Empty,
                def.IsNutritionGivingIngestible ? "food meal nutrition ingestible" : string.Empty,
                def.stuffProps != null ? "resource material stuff raw crafting" : string.Empty,
                TradeUtility.EverPlayerSellable(def) ? "trade sellable barter merchant market" : string.Empty,
                def.stackLimit > 1 ? "stack bulk counted" : string.Empty
            };

            if (HasImplantSearchSignal(def, categoryTokens))
            {
                parts.Add("resource material implant bionic prosthetic artificial bodypart part");
            }

            parts.AddRange(categoryTokens);

            if (def.weaponTags != null)
            {
                parts.AddRange(def.weaponTags);
            }

            return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))).ToLowerInvariant();
        }

        private static IEnumerable<string> EnumerateCategoryTokens(IEnumerable<ThingCategoryDef> categories)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ThingCategoryDef category in categories ?? Enumerable.Empty<ThingCategoryDef>())
            {
                ThingCategoryDef current = category;
                while (current != null)
                {
                    if (!string.IsNullOrWhiteSpace(current.defName) && seen.Add(current.defName))
                    {
                        yield return current.defName;
                        yield return ExpandCamelCase(current.defName);
                    }

                    if (!string.IsNullOrWhiteSpace(current.label) && seen.Add($"label:{current.label}"))
                    {
                        yield return current.label;
                    }

                    current = current.parent;
                }
            }
        }

        private static bool HasImplantSearchSignal(ThingDef def, IEnumerable<string> categoryTokens)
        {
            if (def == null)
            {
                return false;
            }

            return ContainsImplantSearchKeyword(def.defName) ||
                   ContainsImplantSearchKeyword(def.label) ||
                   (categoryTokens != null && categoryTokens.Any(ContainsImplantSearchKeyword));
        }

        private static bool ContainsImplantSearchKeyword(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            for (int i = 0; i < ImplantSearchKeywords.Length; i++)
            {
                if (text.IndexOf(ImplantSearchKeywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string ExpandCamelCase(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var chars = new List<char>(text.Length * 2);
            for (int i = 0; i < text.Length; i++)
            {
                char current = text[i];
                if (i > 0 &&
                    char.IsUpper(current) &&
                    (char.IsLower(text[i - 1]) || char.IsDigit(text[i - 1])))
                {
                    chars.Add(' ');
                }

                chars.Add(current);
            }

            return new string(chars.ToArray());
        }
    }
}
