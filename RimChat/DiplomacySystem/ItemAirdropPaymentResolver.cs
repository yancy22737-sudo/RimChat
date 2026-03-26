using System;
using System.Collections.Generic;
using System.Linq;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: ThingDefRecord.
    /// Responsibility: resolve payment_items.item into a single sellable ThingDef with fail-fast ambiguity reporting.
    /// </summary>
    internal static class ItemAirdropPaymentResolver
    {
        public static ItemAirdropPaymentResolveResult Resolve(string itemText, IReadOnlyList<ThingDefRecord> records)
        {
            string query = (itemText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                return ItemAirdropPaymentResolveResult.Fail("payment_item_unresolved", "Payment item text cannot be empty.");
            }

            if (records == null || records.Count == 0)
            {
                return ItemAirdropPaymentResolveResult.Fail(
                    "payment_item_unresolved",
                    $"Payment item '{query}' could not be resolved.");
            }

            List<ThingDefRecord> exactDefMatches = records
                .Where(record => string.Equals(record.DefName, query, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (exactDefMatches.Count == 1)
            {
                return ItemAirdropPaymentResolveResult.FromSuccess(exactDefMatches[0]);
            }

            if (exactDefMatches.Count > 1)
            {
                return BuildAmbiguousResult(query, exactDefMatches, "payment_item_ambiguous");
            }

            List<ThingDefRecord> exactLabelMatches = records
                .Where(record => string.Equals(record.Label, query, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (exactLabelMatches.Count == 1)
            {
                return ItemAirdropPaymentResolveResult.FromSuccess(exactLabelMatches[0]);
            }

            if (exactLabelMatches.Count > 1)
            {
                return BuildAmbiguousResult(query, exactLabelMatches, "payment_item_ambiguous");
            }

            ItemAirdropIntent intent = ItemAirdropIntent.Create(query, string.Empty, "trade");
            ThingDefMatchRequest request = ThingDefResolver.BuildMatchRequest(intent, 180, 3);
            ThingDefMatchResult resolution = ThingDefMatchEngine.ResolveSingle(records, request);
            if (resolution == null || resolution.Candidates.Count == 0)
            {
                return ItemAirdropPaymentResolveResult.Fail(
                    "payment_item_unresolved",
                    $"Payment item '{query}' could not be resolved.");
            }

            if (resolution.IsAmbiguous)
            {
                List<ThingDefRecord> topCandidates = resolution.Candidates
                    .Select(candidate => candidate.Record)
                    .Where(record => record != null)
                    .ToList();
                return BuildAmbiguousResult(query, topCandidates, "payment_item_ambiguous");
            }

            return ItemAirdropPaymentResolveResult.FromSuccess(resolution.BestCandidate.Record);
        }

        private static ItemAirdropPaymentResolveResult BuildAmbiguousResult(
            string query,
            IEnumerable<ThingDefRecord> records,
            string code)
        {
            List<ItemAirdropPaymentResolveCandidateView> candidates = (records ?? Enumerable.Empty<ThingDefRecord>())
                .Where(record => record != null)
                .OrderByDescending(record => record.MarketValue)
                .ThenBy(record => record.DefName, StringComparer.Ordinal)
                .Take(3)
                .Select(record => new ItemAirdropPaymentResolveCandidateView
                {
                    DefName = record.DefName ?? string.Empty,
                    Label = record.Label ?? record.DefName ?? string.Empty
                })
                .ToList();
            string summary = candidates.Count == 0
                ? "none"
                : string.Join(", ", candidates.Select(candidate => $"{candidate.DefName}({candidate.Label})"));
            string message = $"Payment item '{query}' is ambiguous. Top candidates: {summary}.";
            return ItemAirdropPaymentResolveResult.Fail(code, message, candidates);
        }
    }

    internal sealed class ItemAirdropPaymentResolveResult
    {
        public bool Success { get; private set; }
        public ThingDefRecord ResolvedRecord { get; private set; }
        public string FailureCode { get; private set; }
        public string FailureMessage { get; private set; }
        public List<ItemAirdropPaymentResolveCandidateView> Candidates { get; private set; } = new List<ItemAirdropPaymentResolveCandidateView>();

        public static ItemAirdropPaymentResolveResult FromSuccess(ThingDefRecord record)
        {
            return new ItemAirdropPaymentResolveResult
            {
                Success = true,
                ResolvedRecord = record,
                FailureCode = string.Empty,
                FailureMessage = string.Empty
            };
        }

        public static ItemAirdropPaymentResolveResult Fail(
            string code,
            string message,
            List<ItemAirdropPaymentResolveCandidateView> candidates = null)
        {
            return new ItemAirdropPaymentResolveResult
            {
                Success = false,
                ResolvedRecord = null,
                FailureCode = code ?? "payment_item_unresolved",
                FailureMessage = message ?? "Payment item could not be resolved.",
                Candidates = candidates ?? new List<ItemAirdropPaymentResolveCandidateView>()
            };
        }
    }

    internal sealed class ItemAirdropPaymentResolveCandidate
    {
        public ThingDefRecord Record { get; }
        public int Score { get; }

        public ItemAirdropPaymentResolveCandidate(ThingDefRecord record, int score)
        {
            Record = record;
            Score = score;
        }
    }

    internal sealed class ItemAirdropPaymentResolveCandidateView
    {
        public string DefName { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }
}
