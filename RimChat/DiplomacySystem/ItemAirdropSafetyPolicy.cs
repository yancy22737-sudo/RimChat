using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: ThingDefRecord.
    /// Responsibility: centralized policy for candidate eligibility and safety scoring.
    /// </summary>
    internal static class ItemAirdropSafetyPolicy
    {
        public static bool CanCandidateForNeed(ThingDefRecord record, ItemAirdropNeedFamily family)
        {
            if (record?.Def == null)
            {
                return false;
            }

            ThingDef def = record.Def;
            switch (family)
            {
                case ItemAirdropNeedFamily.Food:
                    return def.IsNutritionGivingIngestible && !def.IsCorpse;
                case ItemAirdropNeedFamily.Medicine:
                    return def.IsMedicine || def.IsDrug;
                case ItemAirdropNeedFamily.Weapon:
                    return def.IsWeapon;
                case ItemAirdropNeedFamily.Apparel:
                    return def.IsApparel;
                default:
                    return IsGenericCandidate(def);
            }
        }

        public static bool IsBlockedByCategory(ThingDefRecord record, HashSet<string> blockedCategories)
        {
            if (record?.Def == null || blockedCategories == null || blockedCategories.Count == 0)
            {
                return false;
            }

            if (blockedCategories.Contains(record.Def.category.ToString()))
            {
                return true;
            }

            if (record.Def.thingCategories == null)
            {
                return false;
            }

            for (int i = 0; i < record.Def.thingCategories.Count; i++)
            {
                ThingCategoryDef category = record.Def.thingCategories[i];
                if (category == null)
                {
                    continue;
                }

                if (blockedCategories.Contains(category.defName ?? string.Empty) ||
                    blockedCategories.Contains(category.label ?? string.Empty))
                {
                    return true;
                }
            }

            return false;
        }

        public static int BuildSafetyScore(ThingDefRecord record)
        {
            if (record?.Def == null)
            {
                return 0;
            }

            int score = 30;
            ThingDef def = record.Def;
            if (def.IsCorpse)
            {
                score -= 200;
            }

            if (def.destroyOnDrop)
            {
                score -= 80;
            }

            if (def.stackLimit <= 1)
            {
                score -= 10;
            }

            if (def.tradeability == Tradeability.None)
            {
                score -= 40;
            }

            return score;
        }

        public static HashSet<string> ParseBlockedCategories(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return new HashSet<string>(
                csv.Split(new[] { ',', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsGenericCandidate(ThingDef def)
        {
            if (def == null || def.IsCorpse)
            {
                return false;
            }

            return def.category == ThingCategory.Item &&
                   (def.tradeability != Tradeability.None || def.BaseMarketValue > 0f);
        }
    }
}
