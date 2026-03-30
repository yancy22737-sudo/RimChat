using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: ThingDefRecord.
    /// Responsibility: centralized policy for candidate eligibility, resource classification, and safety scoring.
    /// </summary>
    internal static class ItemAirdropSafetyPolicy
    {
        private const int ResourceDecisionLogWindowMs = 1000;
        private const int ResourceDecisionLogMaxPerWindow = 24;
        private static int resourceDecisionLogWindowStartTick;
        private static int resourceDecisionLogCount;

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
                case ItemAirdropNeedFamily.Resource:
                    return IsResourceCandidate(record);
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

        public static bool IsResourceCandidate(ThingDefRecord record)
        {
            ThingDef def = record?.Def;
            if (def == null || def.IsCorpse)
            {
                return false;
            }

            // Strong resource signals must win before noisy metadata exclusions.
            if (def.category == ThingCategory.Item &&
                def.stuffProps != null &&
                !def.IsNutritionGivingIngestible &&
                !def.IsMedicine &&
                !def.IsDrug &&
                !def.IsApparel)
            {
                LogResourceDecision(def, "pass via strong resource signal (stuffProps)");
                return true;
            }

            if (def.category != ThingCategory.Item ||
                def.IsNutritionGivingIngestible ||
                def.IsMedicine ||
                def.IsDrug ||
                def.IsWeapon ||
                def.IsApparel)
            {
                return false;
            }

            if (HasStructuralResourceSignal(record))
            {
                LogResourceDecision(
                    def,
                    $"pass via structural (value={def.BaseMarketValue:F2}, trade={def.tradeability}, stack={def.stackLimit}, sellable={record.EverPlayerSellable})");
                return true;
            }

            if (HasMetadataResourceSignal(record))
            {
                LogResourceDecision(
                    def,
                    "pass via metadata (category/search text resource signals)");
                return true;
            }

            LogResourceDecision(
                def,
                $"reject - stuffProps=null, category={def.category}, value={def.BaseMarketValue:F2}, trade={def.tradeability}, stack={def.stackLimit}, sellable={record.EverPlayerSellable}");
            return false;
        }

        public static bool IsResourceCandidate(ThingDef def)
        {
            return IsResourceCandidate(def == null ? null : ThingDefRecord.From(def));
        }

        private static void LogResourceDecision(ThingDef def, string decision)
        {
            if (def == null || !Prefs.DevMode || !ShouldLogResourceDecision())
            {
                return;
            }

            Log.Message($"[RimChat][IsResourceCandidate] {def.defName}: {decision}");
        }

        private static bool ShouldLogResourceDecision()
        {
            int nowTick = Environment.TickCount;
            int elapsed = unchecked(nowTick - resourceDecisionLogWindowStartTick);
            if (elapsed < 0 || elapsed >= ResourceDecisionLogWindowMs)
            {
                resourceDecisionLogWindowStartTick = nowTick;
                resourceDecisionLogCount = 0;
            }

            if (resourceDecisionLogCount >= ResourceDecisionLogMaxPerWindow)
            {
                return false;
            }

            resourceDecisionLogCount++;
            return true;
        }

        private static bool HasStructuralResourceSignal(ThingDefRecord record)
        {
            ThingDef def = record?.Def;
            if (def == null)
            {
                return false;
            }

            return def.category == ThingCategory.Item &&
                   def.stackLimit > 1 &&
                   (record.EverPlayerSellable || def.tradeability != Tradeability.None || def.BaseMarketValue > 0f);
        }

        private static bool HasMetadataResourceSignal(ThingDefRecord record)
        {
            string search = (record?.SearchText ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(search))
            {
                return false;
            }

            return search.Contains("resource") ||
                   search.Contains("material") ||
                   search.Contains("stuff") ||
                   search.Contains("raw") ||
                   search.Contains("manufactured") ||
                   search.Contains("metal") ||
                   search.Contains("textile");
        }
    }
}
