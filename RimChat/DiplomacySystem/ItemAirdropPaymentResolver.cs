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

            string token = query.ToLowerInvariant();
            string normalizedToken = NormalizeToken(query);
            HashSet<string> semanticQueryTokens = ExtractSemanticTokens(query);
            List<ItemAirdropPaymentResolveCandidate> fuzzy = records
                .Select(record => BuildCandidate(record, token, normalizedToken, semanticQueryTokens))
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.Record.MarketValue)
                .ThenBy(candidate => candidate.Record.DefName, StringComparer.Ordinal)
                .ToList();
            if (fuzzy.Count == 0)
            {
                return ItemAirdropPaymentResolveResult.Fail(
                    "payment_item_unresolved",
                    $"Payment item '{query}' could not be resolved.");
            }

            int topScore = fuzzy[0].Score;
            int topTieCount = fuzzy.Count(candidate => candidate.Score == topScore);
            if (topTieCount > 1)
            {
                List<ThingDefRecord> topCandidates = fuzzy
                    .Take(3)
                    .Select(candidate => candidate.Record)
                    .ToList();
                return BuildAmbiguousResult(query, topCandidates, "payment_item_ambiguous");
            }

            return ItemAirdropPaymentResolveResult.FromSuccess(fuzzy[0].Record);
        }

        private static ItemAirdropPaymentResolveCandidate BuildCandidate(
            ThingDefRecord record,
            string token,
            string normalizedToken,
            HashSet<string> semanticQueryTokens)
        {
            if (record == null || string.IsNullOrWhiteSpace(token))
            {
                return new ItemAirdropPaymentResolveCandidate(record, 0);
            }

            string defName = (record.DefName ?? string.Empty).ToLowerInvariant();
            string label = (record.Label ?? string.Empty).ToLowerInvariant();
            string search = (record.SearchText ?? string.Empty).ToLowerInvariant();
            string normalizedDefName = NormalizeToken(record.DefName);
            string normalizedLabel = NormalizeToken(record.Label);

            int score = 0;
            if (defName == token || (!string.IsNullOrWhiteSpace(normalizedToken) && normalizedDefName == normalizedToken))
            {
                score += 600;
            }

            if (label == token || (!string.IsNullOrWhiteSpace(normalizedToken) && normalizedLabel == normalizedToken))
            {
                score += 560;
            }

            if (!string.IsNullOrWhiteSpace(normalizedToken) &&
                (normalizedDefName.Contains(normalizedToken) || normalizedToken.Contains(normalizedDefName)))
            {
                score += 320;
            }

            if (!string.IsNullOrWhiteSpace(normalizedToken) &&
                (normalizedLabel.Contains(normalizedToken) || normalizedToken.Contains(normalizedLabel)))
            {
                score += 280;
            }

            if (semanticQueryTokens != null && semanticQueryTokens.Count >= 2)
            {
                HashSet<string> defTokens = ExtractSemanticTokens(record.DefName);
                HashSet<string> labelTokens = ExtractSemanticTokens(record.Label);
                if (ContainsAllTokens(defTokens, semanticQueryTokens))
                {
                    score += 260;
                }

                if (ContainsAllTokens(labelTokens, semanticQueryTokens))
                {
                    score += 240;
                }
            }

            if (search.Contains(token))
            {
                score += 40;
            }

            score += ScoreNearMatch(normalizedToken, normalizedDefName, 120);
            score += ScoreNearMatch(normalizedToken, normalizedLabel, 100);
            return new ItemAirdropPaymentResolveCandidate(record, score);
        }

        private static int ScoreNearMatch(string normalizedToken, string normalizedTarget, int maxScore)
        {
            if (string.IsNullOrWhiteSpace(normalizedToken) || string.IsNullOrWhiteSpace(normalizedTarget))
            {
                return 0;
            }

            int maxLength = Math.Max(normalizedToken.Length, normalizedTarget.Length);
            if (maxLength < 3)
            {
                return 0;
            }

            int distance = ComputeLevenshteinDistance(normalizedToken, normalizedTarget);
            if (distance <= 0)
            {
                return 0;
            }

            if (distance == 1)
            {
                return maxScore;
            }

            if (distance == 2 && maxLength >= 4)
            {
                return Math.Max(0, maxScore - 30);
            }

            if (distance == 3 && maxLength >= 8)
            {
                return Math.Max(0, maxScore - 60);
            }

            return 0;
        }

        private static int ComputeLevenshteinDistance(string left, string right)
        {
            int leftLength = left.Length;
            int rightLength = right.Length;
            if (leftLength == 0)
            {
                return rightLength;
            }

            if (rightLength == 0)
            {
                return leftLength;
            }

            var matrix = new int[leftLength + 1, rightLength + 1];
            for (int i = 0; i <= leftLength; i++)
            {
                matrix[i, 0] = i;
            }

            for (int j = 0; j <= rightLength; j++)
            {
                matrix[0, j] = j;
            }

            for (int i = 1; i <= leftLength; i++)
            {
                for (int j = 1; j <= rightLength; j++)
                {
                    int cost = left[i - 1] == right[j - 1] ? 0 : 1;
                    int deletion = matrix[i - 1, j] + 1;
                    int insertion = matrix[i, j - 1] + 1;
                    int substitution = matrix[i - 1, j - 1] + cost;
                    matrix[i, j] = Math.Min(Math.Min(deletion, insertion), substitution);
                }
            }

            return matrix[leftLength, rightLength];
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

        private static HashSet<string> ExtractSemanticTokens(string text)
        {
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(text))
            {
                return tokens;
            }

            string expanded = ExpandCamelCase(text).ToLowerInvariant();
            char[] separators =
            {
                ' ', '\t', '\r', '\n', '_', '-', '/', '\\', ',', '.', ':', ';', '|', '(', ')', '[', ']', '{', '}'
            };

            foreach (string part in expanded.Split(separators, StringSplitOptions.RemoveEmptyEntries))
            {
                string token = part.Trim();
                if (token.Length >= 3)
                {
                    tokens.Add(token);
                }
            }

            return tokens;
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

        private static bool ContainsAllTokens(HashSet<string> targetTokens, HashSet<string> queryTokens)
        {
            if (targetTokens == null || queryTokens == null || targetTokens.Count == 0 || queryTokens.Count == 0)
            {
                return false;
            }

            foreach (string queryToken in queryTokens)
            {
                if (!targetTokens.Contains(queryToken))
                {
                    return false;
                }
            }

            return true;
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
