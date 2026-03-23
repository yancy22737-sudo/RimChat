using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: ThingDefCatalog, ItemAirdropSafetyPolicy.
    /// Responsibility: build and rank airdrop candidates with strict-family-first selection.
    /// </summary>
    internal static class ThingDefResolver
    {
        private static readonly Dictionary<string, string[]> LocalAliasMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["steel"] = new[] { "metal", "钢铁", "钢材" },
            ["metal"] = new[] { "steel", "钢铁", "钢材" },
            ["pemmican"] = new[] { "干肉饼", "肉干", "ration", "dried meat" },
            ["meal"] = new[] { "生存餐", "套餐", "food", "口粮" },
            ["medicine"] = new[] { "药品", "医疗包", "医药", "med" },
            ["med"] = new[] { "medicine", "医疗", "药品" },
            ["component"] = new[] { "零部件", "组件", "部件" },
            ["plasteel"] = new[] { "塑钢", "高级金属" },
            ["wood"] = new[] { "木材", "原木" },
            ["chemfuel"] = new[] { "燃料", "化学燃料" }
        };

        public static ResolverResult ResolveTop1(string need, string constraints, HashSet<string> blacklist)
        {
            ItemAirdropIntent intent = ItemAirdropIntent.Create(need, constraints, "general");
            ItemAirdropCandidatePack pack = BuildCandidates(
                intent,
                maxCandidates: 5,
                blacklist,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            if (pack.Candidates.Count == 0)
            {
                return ResolverResult.Fail("no_match", "No matching spawnable ThingDef found for current constraints.");
            }

            List<ScoredCandidate> top = pack.Candidates
                .Select(c => new ScoredCandidate(c.Record, c.MatchScore + c.SafetyScore))
                .ToList();
            return ResolverResult.FromSuccess(top[0].Record, top);
        }

        public static List<string> ExpandLocalAliases(ItemAirdropIntent intent)
        {
            var result = new List<string>();
            if (intent?.Tokens == null || intent.Tokens.Count == 0)
            {
                return result;
            }

            for (int i = 0; i < intent.Tokens.Count; i++)
            {
                string token = intent.Tokens[i];
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                if (LocalAliasMap.TryGetValue(token, out string[] aliases))
                {
                    result.AddRange(aliases);
                }
            }

            switch (intent.Family)
            {
                case ItemAirdropNeedFamily.Food:
                    result.AddRange(new[] { "food", "meal", "食物", "口粮" });
                    break;
                case ItemAirdropNeedFamily.Medicine:
                    result.AddRange(new[] { "medicine", "medical", "药品", "医疗" });
                    break;
                case ItemAirdropNeedFamily.Weapon:
                    result.AddRange(new[] { "weapon", "gun", "武器", "枪械" });
                    break;
                case ItemAirdropNeedFamily.Apparel:
                    result.AddRange(new[] { "apparel", "armor", "服装", "护甲" });
                    break;
            }

            return result
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();
        }

        public static ItemAirdropCandidatePack BuildCandidates(
            ItemAirdropIntent intent,
            int maxCandidates,
            HashSet<string> blacklist,
            HashSet<string> blockedCategories)
        {
            int limit = Math.Max(1, maxCandidates);
            var result = new ItemAirdropCandidatePack
            {
                Family = intent?.Family ?? ItemAirdropNeedFamily.Unknown
            };

            if (intent == null || intent.Tokens == null || intent.Tokens.Count == 0)
            {
                return result;
            }

            IReadOnlyList<ThingDefRecord> records = ThingDefCatalog.GetRecords();
            result.RecordsScanned = records?.Count ?? 0;
            List<ItemAirdropCandidate> primary = BuildPool(records, intent, blacklist, blockedCategories, true, result, true);
            List<ItemAirdropCandidate> fallback = BuildPool(records, intent, blacklist, blockedCategories, false, result, false);

            if (intent.Family != ItemAirdropNeedFamily.Unknown)
            {
                result.Candidates = primary.Take(limit).ToList();
                result.UsedFallbackPool = false;
                return result;
            }

            result.Candidates = MergeTopCandidates(primary, fallback, limit, out bool usedFallbackPool);
            result.UsedFallbackPool = usedFallbackPool;
            return result;
        }

        public static bool CanCandidateForNeed(ThingDefRecord record, ItemAirdropNeedFamily family)
        {
            return ItemAirdropSafetyPolicy.CanCandidateForNeed(record, family);
        }

        private static List<ItemAirdropCandidate> BuildPool(
            IReadOnlyList<ThingDefRecord> records,
            ItemAirdropIntent intent,
            HashSet<string> blacklist,
            HashSet<string> blockedCategories,
            bool primaryOnly,
            ItemAirdropCandidatePack diagnostics,
            bool collectRejectionCounters)
        {
            var candidates = new List<ItemAirdropCandidate>();
            ItemAirdropNeedFamily family = intent.Family;
            for (int i = 0; i < records.Count; i++)
            {
                ThingDefRecord record = records[i];
                CandidateRejectReason rejectReason = GetRejectReason(record, blacklist, blockedCategories);
                if (rejectReason != CandidateRejectReason.None)
                {
                    if (collectRejectionCounters)
                    {
                        AccumulateRejectCounter(diagnostics, rejectReason);
                    }

                    continue;
                }

                bool isPrimaryFamily = CanCandidateForNeed(record, family);
                if (primaryOnly && !isPrimaryFamily)
                {
                    if (collectRejectionCounters && family != ItemAirdropNeedFamily.Unknown)
                    {
                        diagnostics.RejectedByFamily++;
                        TrackNearMiss(diagnostics, record, intent.Tokens, "family");
                    }

                    continue;
                }

                if (!primaryOnly && family != ItemAirdropNeedFamily.Unknown && isPrimaryFamily)
                {
                    continue;
                }

                int matchScore = ComputeMatchScore(record, intent.Tokens, family, isPrimaryFamily, primaryOnly);
                if (matchScore <= 0)
                {
                    if (collectRejectionCounters)
                    {
                        diagnostics.RejectedByMatchScore++;
                        TrackNearMiss(diagnostics, record, intent.Tokens, "match");
                    }

                    continue;
                }

                int safetyScore = ItemAirdropSafetyPolicy.BuildSafetyScore(record);
                candidates.Add(new ItemAirdropCandidate
                {
                    Record = record,
                    Family = isPrimaryFamily ? family : ItemAirdropNeedFamily.Unknown,
                    MatchScore = matchScore,
                    SafetyScore = safetyScore,
                    Price = Math.Max(0.01f, record.MarketValue)
                });
            }

            return candidates
                .OrderByDescending(c => c.MatchScore)
                .ThenByDescending(c => c.SafetyScore)
                .ThenByDescending(c => c.Price)
                .ThenBy(c => c.Record.DefName, StringComparer.Ordinal)
                .ToList();
        }

        private static List<ItemAirdropCandidate> MergeTopCandidates(
            List<ItemAirdropCandidate> primary,
            List<ItemAirdropCandidate> fallback,
            int maxCandidates,
            out bool usedFallbackPool)
        {
            int limit = Math.Max(1, maxCandidates);
            var merged = new List<ItemAirdropCandidate>(limit);
            usedFallbackPool = false;

            if (primary != null && primary.Count > 0)
            {
                merged.AddRange(primary.Take(limit));
            }

            if (merged.Count >= limit)
            {
                return merged;
            }

            if (fallback == null || fallback.Count == 0)
            {
                return merged;
            }

            usedFallbackPool = true;
            for (int i = 0; i < fallback.Count && merged.Count < limit; i++)
            {
                ItemAirdropCandidate item = fallback[i];
                if (merged.Any(c => string.Equals(c.Record.DefName, item.Record.DefName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                // Apply fallback penalty to keep whitelist-first priority stable.
                merged.Add(new ItemAirdropCandidate
                {
                    Record = item.Record,
                    Family = item.Family,
                    MatchScore = Math.Max(1, item.MatchScore - 20),
                    SafetyScore = item.SafetyScore,
                    Price = item.Price
                });
            }

            return merged
                .OrderByDescending(c => c.MatchScore)
                .ThenByDescending(c => c.SafetyScore)
                .ThenByDescending(c => c.Price)
                .ThenBy(c => c.Record.DefName, StringComparer.Ordinal)
                .Take(limit)
                .ToList();
        }

        private static CandidateRejectReason GetRejectReason(
            ThingDefRecord record,
            HashSet<string> blacklist,
            HashSet<string> blockedCategories)
        {
            if (record?.Def == null)
            {
                return CandidateRejectReason.NullRecord;
            }

            if (blacklist != null && blacklist.Contains(record.DefName))
            {
                return CandidateRejectReason.Blacklist;
            }

            if (ItemAirdropSafetyPolicy.IsBlockedByCategory(record, blockedCategories))
            {
                return CandidateRejectReason.BlockedCategory;
            }

            return CandidateRejectReason.None;
        }

        private static int ComputeMatchScore(
            ThingDefRecord record,
            List<string> tokens,
            ItemAirdropNeedFamily family,
            bool isPrimaryFamily,
            bool primaryPool)
        {
            int score = 0;
            string search = record.SearchText ?? string.Empty;
            string normalizedLabel = NormalizeToken(record.Label);
            string normalizedDefName = NormalizeToken(record.DefName);
            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];
                if (token.Length < 2)
                {
                    continue;
                }

                string normalizedToken = NormalizeToken(token);
                score += ScoreStrongMatch(normalizedToken, normalizedDefName, normalizedLabel);
                if (string.Equals(record.DefName, token, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(normalizedToken) &&
                     string.Equals(normalizedDefName, normalizedToken, StringComparison.OrdinalIgnoreCase)))
                {
                    score += 320;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(normalizedToken) &&
                    string.Equals(normalizedLabel, normalizedToken, StringComparison.OrdinalIgnoreCase))
                {
                    score += 260;
                    continue;
                }

                if (!search.Contains(token))
                {
                    continue;
                }

                score += 6;
            }

            if (isPrimaryFamily)
            {
                score += 40;
            }
            else if (!primaryPool && family != ItemAirdropNeedFamily.Unknown)
            {
                score += 4;
            }

            if (family == ItemAirdropNeedFamily.Food && record.Def.IsNutritionGivingIngestible)
            {
                score += 16;
            }

            if (family == ItemAirdropNeedFamily.Medicine && (record.Def.IsMedicine || record.Def.IsDrug))
            {
                score += 12;
            }

            if (family == ItemAirdropNeedFamily.Weapon && record.Def.IsWeapon)
            {
                score += 12;
            }

            if (family == ItemAirdropNeedFamily.Apparel && record.Def.IsApparel)
            {
                score += 10;
            }

            return score;
        }

        private static int ScoreStrongMatch(string normalizedToken, string normalizedDefName, string normalizedLabel)
        {
            if (string.IsNullOrWhiteSpace(normalizedToken) || normalizedToken.Length < 2)
            {
                return 0;
            }

            if (!string.IsNullOrWhiteSpace(normalizedDefName) &&
                (normalizedDefName.Contains(normalizedToken) || normalizedToken.Contains(normalizedDefName)))
            {
                return 120;
            }

            if (!string.IsNullOrWhiteSpace(normalizedLabel) &&
                (normalizedLabel.Contains(normalizedToken) || normalizedToken.Contains(normalizedLabel)))
            {
                return 100;
            }

            return 0;
        }

        private static void AccumulateRejectCounter(ItemAirdropCandidatePack pack, CandidateRejectReason reason)
        {
            if (pack == null)
            {
                return;
            }

            if (reason == CandidateRejectReason.Blacklist)
            {
                pack.RejectedByBlacklist++;
                return;
            }

            if (reason == CandidateRejectReason.BlockedCategory)
            {
                pack.RejectedByBlockedCategory++;
            }
        }

        private static void TrackNearMiss(
            ItemAirdropCandidatePack pack,
            ThingDefRecord record,
            List<string> tokens,
            string reason)
        {
            if (pack == null || record?.Def == null || tokens == null || tokens.Count == 0)
            {
                return;
            }

            int score = 0;
            string normalizedDef = NormalizeToken(record.DefName);
            string normalizedLabel = NormalizeToken(record.Label);
            string search = record.SearchText ?? string.Empty;
            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                string normalizedToken = NormalizeToken(token);
                if (string.IsNullOrWhiteSpace(normalizedToken))
                {
                    continue;
                }

                if (normalizedDef.Contains(normalizedToken) || normalizedLabel.Contains(normalizedToken))
                {
                    score += 6;
                }
                else if (search.Contains(token))
                {
                    score += 2;
                }
            }

            if (score <= 0)
            {
                return;
            }

            string note = $"{record.DefName}:{reason}:{score}";
            if (pack.NearMisses.Any(x => string.Equals(x, note, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            pack.NearMisses.Add(note);
            pack.NearMisses = pack.NearMisses
                .OrderByDescending(x => ParseNearMissScore(x))
                .Take(5)
                .ToList();
        }

        private static int ParseNearMissScore(string note)
        {
            if (string.IsNullOrWhiteSpace(note))
            {
                return 0;
            }

            int idx = note.LastIndexOf(':');
            if (idx < 0 || idx >= note.Length - 1)
            {
                return 0;
            }

            return int.TryParse(note.Substring(idx + 1), out int value) ? value : 0;
        }

        private static string NormalizeToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return new string(text.Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-').ToArray())
                .ToLowerInvariant();
        }

        private enum CandidateRejectReason
        {
            None = 0,
            NullRecord = 1,
            Blacklist = 2,
            BlockedCategory = 3
        }
    }

    internal sealed class ResolverResult
    {
        public bool Success { get; private set; }
        public string FailureCode { get; private set; }
        public string FailureMessage { get; private set; }
        public ThingDefRecord Selected { get; private set; }
        public List<ScoredCandidate> TopCandidates { get; private set; }

        public static ResolverResult Fail(string code, string message)
        {
            return new ResolverResult
            {
                Success = false,
                FailureCode = code ?? "resolver_failed",
                FailureMessage = message ?? "Resolver failed.",
                TopCandidates = new List<ScoredCandidate>()
            };
        }

        public static ResolverResult FromSuccess(ThingDefRecord selected, List<ScoredCandidate> topCandidates)
        {
            return new ResolverResult
            {
                Success = true,
                Selected = selected,
                TopCandidates = topCandidates ?? new List<ScoredCandidate>()
            };
        }
    }

    internal sealed class ScoredCandidate
    {
        public ThingDefRecord Record { get; }
        public int Score { get; }

        public ScoredCandidate(ThingDefRecord record, int score)
        {
            Record = record;
            Score = score;
        }
    }
}
