using System;
using System.Collections.Generic;
using System.Linq;

namespace RimChat.DiplomacySystem
{
    internal sealed class ThingDefMatchRequest
    {
        public string Query { get; set; } = string.Empty;
        public IReadOnlyCollection<string> Tokens { get; set; } = Array.Empty<string>();
        public IReadOnlyCollection<string> Aliases { get; set; } = Array.Empty<string>();
        public IReadOnlyCollection<string> SemanticTokens { get; set; } = Array.Empty<string>();
        public int MinScore { get; set; } = 1;
        public int MaxResults { get; set; } = 6;
    }

    internal sealed class ThingDefMatchCandidate
    {
        public ThingDefRecord Record { get; set; }
        public int Score { get; set; }
        public List<string> Breakdown { get; set; } = new List<string>();
    }

    internal sealed class ThingDefMatchResult
    {
        public bool Success { get; set; }
        public bool IsAmbiguous { get; set; }
        public ThingDefMatchCandidate BestCandidate { get; set; }
        public List<ThingDefMatchCandidate> Candidates { get; set; } = new List<ThingDefMatchCandidate>();
    }

    internal static class ThingDefMatchEngine
    {
        public static IReadOnlyList<ThingDefMatchCandidate> RankCandidates(
            IReadOnlyList<ThingDefRecord> records,
            ThingDefMatchRequest request)
        {
            if (records == null || records.Count == 0 || request == null)
            {
                return Array.Empty<ThingDefMatchCandidate>();
            }

            var candidates = records
                .Select(record => ScoreRecord(record, request))
                .Where(candidate => candidate != null && candidate.Score >= Math.Max(1, request.MinScore))
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.Record?.MarketValue ?? 0f)
                .ThenBy(candidate => candidate.Record?.DefName ?? string.Empty, StringComparer.Ordinal)
                .Take(Math.Max(1, request.MaxResults))
                .ToList();
            return candidates;
        }

        public static ThingDefMatchResult ResolveSingle(
            IReadOnlyList<ThingDefRecord> records,
            ThingDefMatchRequest request,
            int ambiguityWindow = 0)
        {
            IReadOnlyList<ThingDefMatchCandidate> ranked = RankCandidates(records, request);
            if (ranked.Count == 0)
            {
                return new ThingDefMatchResult();
            }

            ThingDefMatchCandidate best = ranked[0];
            bool ambiguous = ranked.Count > 1 && ranked[1].Score >= best.Score - Math.Max(0, ambiguityWindow);
            return new ThingDefMatchResult
            {
                Success = !ambiguous,
                IsAmbiguous = ambiguous,
                BestCandidate = ambiguous ? null : best,
                Candidates = ranked.Take(3).ToList()
            };
        }

        public static ThingDefMatchCandidate ScoreRecord(ThingDefRecord record, ThingDefMatchRequest request)
        {
            if (record?.Def == null || request == null)
            {
                return null;
            }

            string query = (request.Query ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            string rawQuery = query.ToLowerInvariant();
            string normalizedQuery = NormalizeToken(query);
            string normalizedDef = NormalizeToken(record.DefName);
            string normalizedLabel = NormalizeToken(record.Label);
            string search = (record.SearchText ?? string.Empty).ToLowerInvariant();
            HashSet<string> semanticTargetTokens = ExtractSemanticTokens(record.DefName);
            semanticTargetTokens.UnionWith(ExtractSemanticTokens(record.Label));
            semanticTargetTokens.UnionWith(ExtractSemanticTokens(record.SearchText));

            int score = 0;
            var breakdown = new List<string>();
            var aliasSet = new HashSet<string>(request.Aliases ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (!aliasSet.Contains(query))
            {
                aliasSet.Add(query);
            }

            if (string.Equals(record.DefName, query, StringComparison.OrdinalIgnoreCase))
            {
                score += 1000;
                breakdown.Add("exact_def");
            }

            if (string.Equals(record.Label, query, StringComparison.OrdinalIgnoreCase))
            {
                score += 920;
                breakdown.Add("exact_label");
            }

            if (!string.IsNullOrWhiteSpace(normalizedQuery) && string.Equals(normalizedDef, normalizedQuery, StringComparison.OrdinalIgnoreCase))
            {
                score += 820;
                breakdown.Add("normalized_def");
            }

            if (!string.IsNullOrWhiteSpace(normalizedQuery) && string.Equals(normalizedLabel, normalizedQuery, StringComparison.OrdinalIgnoreCase))
            {
                score += 760;
                breakdown.Add("normalized_label");
            }

            foreach (string alias in aliasSet)
            {
                string normalizedAlias = NormalizeToken(alias);
                if (string.IsNullOrWhiteSpace(alias))
                {
                    continue;
                }

                if (string.Equals(record.DefName, alias, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(normalizedAlias) && string.Equals(normalizedDef, normalizedAlias, StringComparison.OrdinalIgnoreCase)))
                {
                    score += 520;
                    breakdown.Add("alias_def");
                    break;
                }

                if (string.Equals(record.Label, alias, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(normalizedAlias) && string.Equals(normalizedLabel, normalizedAlias, StringComparison.OrdinalIgnoreCase)))
                {
                    score += 480;
                    breakdown.Add("alias_label");
                    break;
                }
            }

            HashSet<string> requestSemanticTokens = new HashSet<string>(request.SemanticTokens ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (requestSemanticTokens.Count == 0)
            {
                requestSemanticTokens = ExtractSemanticTokens(query);
            }

            if (requestSemanticTokens.Count >= 2 && requestSemanticTokens.All(token => semanticTargetTokens.Contains(token)))
            {
                score += 340;
                breakdown.Add("semantic_all");
            }

            if (!string.IsNullOrWhiteSpace(normalizedQuery) &&
                ((normalizedDef.Contains(normalizedQuery) || normalizedQuery.Contains(normalizedDef)) ||
                 (normalizedLabel.Contains(normalizedQuery) || normalizedQuery.Contains(normalizedLabel))))
            {
                score += 260;
                breakdown.Add("normalized_contains");
            }

            if (search.Contains(rawQuery))
            {
                score += 220;
                breakdown.Add("search_query");
            }

            int tokenCoverage = 0;
            foreach (string token in request.Tokens ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(token) || token.Length < 2)
                {
                    continue;
                }

                string normalizedToken = NormalizeToken(token);
                bool tokenMatched = false;
                if (!string.IsNullOrWhiteSpace(normalizedToken) &&
                    (normalizedDef.Contains(normalizedToken) || normalizedLabel.Contains(normalizedToken)))
                {
                    score += 120;
                    tokenCoverage++;
                    tokenMatched = true;
                }

                if (!tokenMatched && search.Contains(token.ToLowerInvariant()))
                {
                    score += 72;
                    tokenMatched = true;
                }

                if (!tokenMatched)
                {
                    int overlap = semanticTargetTokens.Contains(token) || (!string.IsNullOrWhiteSpace(normalizedToken) && semanticTargetTokens.Contains(normalizedToken))
                        ? 1
                        : 0;
                    if (overlap > 0)
                    {
                        score += 52;
                    }
                }
            }

            if ((request.Tokens?.Count ?? 0) > 0 && tokenCoverage == request.Tokens.Count)
            {
                score += 110;
                breakdown.Add("token_full_cover");
            }

            score += ScoreNearMatch(normalizedQuery, normalizedDef, 90, breakdown, "near_def");
            score += ScoreNearMatch(normalizedQuery, normalizedLabel, 76, breakdown, "near_label");
            if (score <= 0)
            {
                return null;
            }

            return new ThingDefMatchCandidate
            {
                Record = record,
                Score = score,
                Breakdown = breakdown.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        public static string NormalizeToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return new string(text.Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-').ToArray())
                .ToLowerInvariant();
        }

        public static HashSet<string> ExtractSemanticTokens(string text)
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
                if (token.Length >= 2)
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

        private static int ScoreNearMatch(string normalizedQuery, string normalizedTarget, int maxScore, List<string> breakdown, string tag)
        {
            if (string.IsNullOrWhiteSpace(normalizedQuery) || string.IsNullOrWhiteSpace(normalizedTarget))
            {
                return 0;
            }

            int maxLength = Math.Max(normalizedQuery.Length, normalizedTarget.Length);
            if (maxLength < 4)
            {
                return 0;
            }

            int distance = ComputeLevenshteinDistance(normalizedQuery, normalizedTarget);
            if (distance == 1)
            {
                breakdown.Add(tag);
                return maxScore;
            }

            if (distance == 2 && maxLength >= 5)
            {
                breakdown.Add(tag);
                return Math.Max(0, maxScore - 24);
            }

            if (distance == 3 && maxLength >= 8)
            {
                breakdown.Add(tag);
                return Math.Max(0, maxScore - 48);
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
                    matrix[i, j] = Math.Min(
                        Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost);
                }
            }

            return matrix[leftLength, rightLength];
        }
    }
}
