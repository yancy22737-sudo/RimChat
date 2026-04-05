using System;
using System.Collections.Generic;
using System.Linq;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: none.
    /// Responsibility: shared read models for two-phase item airdrop flow.
    /// </summary>
    internal enum ItemAirdropNeedFamily
    {
        Unknown = 0,
        Food = 1,
        Medicine = 2,
        Weapon = 3,
        Apparel = 4,
        Resource = 5
    }

    internal sealed class ItemAirdropIntent
    {
        public string NeedText { get; private set; }
        public string ConstraintsText { get; private set; }
        public string Scenario { get; private set; }
        public ItemAirdropNeedFamily Family { get; private set; }
        public List<string> Tokens { get; private set; }

        public static ItemAirdropIntent Create(string need, string constraints, string scenario)
        {
            return Create(need, constraints, scenario, null);
        }

        public static ItemAirdropIntent Create(
            string need,
            string constraints,
            string scenario,
            IEnumerable<string> extraTerms)
        {
            string safeNeed = (need ?? string.Empty).Trim();
            string safeConstraints = (constraints ?? string.Empty).Trim();
            string appendedTerms = string.Join(
                " ",
                (extraTerms ?? Enumerable.Empty<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim()));

            string mergedText = $"{safeNeed} {safeConstraints} {appendedTerms}".Trim();
            List<string> tokens = ItemAirdropIntentParser.Tokenize(mergedText);
            ItemAirdropNeedFamily family = ItemAirdropIntentParser.ResolveFamily(tokens);

            return new ItemAirdropIntent
            {
                NeedText = safeNeed,
                ConstraintsText = safeConstraints,
                Scenario = (scenario ?? "general").Trim(),
                Family = family,
                Tokens = tokens
            };
        }
    }

    internal sealed class ItemAirdropCandidate
    {
        public ThingDefRecord Record { get; set; }
        public ItemAirdropNeedFamily Family { get; set; }
        public int MatchScore { get; set; }
        public int SafetyScore { get; set; }
        public float Price { get; set; }
    }

    internal sealed class ItemAirdropSelection
    {
        public string SelectedDefName { get; set; }
        public int Count { get; set; }
        public string Reason { get; set; }
    }

    public sealed class ItemAirdropPendingSelectionOption
    {
        public int Index { get; set; }
        public string DefName { get; set; }
        public string Label { get; set; }
        public float UnitPrice { get; set; }
        public int MaxLegalCount { get; set; }
    }

    public sealed class ItemAirdropPendingSelectionData
    {
        public string NeedText { get; set; }
        public int BudgetSilver { get; set; }
        public string FailureCode { get; set; }
        public string FailureReason { get; set; }
        public List<ItemAirdropPendingSelectionOption> Options { get; set; } = new List<ItemAirdropPendingSelectionOption>();
    }

    internal sealed class ItemAirdropCandidatePack
    {
        public List<ItemAirdropCandidate> Candidates { get; set; } = new List<ItemAirdropCandidate>();
        public Dictionary<string, float> PriceOverridesByDef { get; set; } = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public ItemAirdropNeedFamily Family { get; set; } = ItemAirdropNeedFamily.Unknown;
        public bool UsedFallbackPool { get; set; }
        public int RecordsScanned { get; set; }
        public int RejectedByBlacklist { get; set; }
        public int RejectedByBlockedCategory { get; set; }
        public int RejectedByFamily { get; set; }
        public int RejectedByMatchScore { get; set; }
        public List<string> NearMisses { get; set; } = new List<string>();
        public bool HasBoundNeedConflict { get; set; }
        public bool BoundNeedInjectedIntoCandidates { get; set; }
        public string BoundNeedDefName { get; set; } = string.Empty;
        public string BoundNeedConflictCode { get; set; } = string.Empty;
        public string BoundNeedConflictDetails { get; set; } = string.Empty;

        public string BuildSummary(int top = 3)
        {
            if (Candidates == null || Candidates.Count == 0)
            {
                return "none";
            }

            return string.Join(
                "|",
                Candidates.Take(Math.Max(1, top))
                    .Select(c => $"{c.Record.DefName}:m{c.MatchScore}/s{c.SafetyScore}/v{c.Price:F1}"));
        }

        public string BuildDiagnosticsSummary()
        {
            string nearMiss = NearMisses == null || NearMisses.Count == 0
                ? "none"
                : string.Join("|", NearMisses.Take(3));
            string boundNeed = string.IsNullOrWhiteSpace(BoundNeedDefName)
                ? "none"
                : $"{BoundNeedDefName}:conflict={HasBoundNeedConflict}:injected={BoundNeedInjectedIntoCandidates}:code={(string.IsNullOrWhiteSpace(BoundNeedConflictCode) ? "none" : BoundNeedConflictCode)}";
            return $"records={RecordsScanned},blacklist={RejectedByBlacklist},blockedCategory={RejectedByBlockedCategory},familyReject={RejectedByFamily},matchReject={RejectedByMatchScore},nearMiss={nearMiss},boundNeed={boundNeed}";
        }

        public float ResolveUnitPrice(ThingDefRecord record)
        {
            float overrideValue;
            if (record?.DefName != null &&
                PriceOverridesByDef != null &&
                PriceOverridesByDef.TryGetValue(record.DefName, out overrideValue) &&
                overrideValue > 0f &&
                !float.IsNaN(overrideValue) &&
                !float.IsInfinity(overrideValue))
            {
                return Math.Max(0.01f, overrideValue);
            }

            return Math.Max(0.01f, record?.MarketValue ?? 0.01f);
        }
    }

    internal sealed class ItemAirdropBoundNeedInfo
    {
        public string DefName { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string SearchText { get; set; } = string.Empty;
        public ThingDefRecord Record { get; set; }
    }

    internal static class ItemAirdropParameterKeys
    {
        public const string BoundNeedDefName = "__airdrop_bound_need_def";
        public const string BoundNeedLabel = "__airdrop_bound_need_label";
        public const string BoundNeedSearchText = "__airdrop_bound_need_search_text";
        public const string BoundNeedSource = "__airdrop_bound_need_source";
        public const string BoundNeedConflictCode = "__airdrop_bound_need_conflict_code";
        public const string BoundNeedConflictMessage = "__airdrop_bound_need_conflict_message";
    }
}
