using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.DiplomacySystem;
using RimWorld;

namespace RimChat.UI
{
    internal sealed class SearchStateManager
    {
        private const int MaxSuggestions = 6;

        private List<ThingDefRecord> cachedSuggestions = new List<ThingDefRecord>();

        private string currentBindingDefName = string.Empty;
        private string currentBindingLabel = string.Empty;
        private string currentBindingSearchText = string.Empty;

        public IReadOnlyList<ThingDefRecord> Suggestions => cachedSuggestions;
        public bool HasBinding => !string.IsNullOrWhiteSpace(currentBindingDefName);
        public string BindingDefName => currentBindingDefName;
        public string BindingLabel => currentBindingLabel;
        public string BindingSearchText => currentBindingSearchText;

        public void ComputeSuggestions(
            string query,
            HashSet<string> blacklist = null,
            TechLevel maxTechLevel = TechLevel.Archotech)
        {
            string normalized = NormalizeQuery(query ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                cachedSuggestions.Clear();
                return;
            }

            ItemAirdropIntent intent = ItemAirdropIntent.Create(normalized, string.Empty, "trade");
            IReadOnlyList<ThingDefRecord> records = ThingDefCatalog.GetRecords();
            cachedSuggestions = BuildSuggestions(records, intent, normalized, 180, maxTechLevel);
            if (cachedSuggestions.Count > 0)
            {
                return;
            }

            // Fallback: keep search usable for high-tech needs (for example BionicEye) when strict filters over-prune results.
            cachedSuggestions = BuildSuggestions(records, intent, normalized, 1, TechLevel.Archotech);
        }

        private static bool IsWithinTechLevel(ThingDefRecord record, TechLevel maxTechLevel)
        {
            if (record?.Def == null)
            {
                return false;
            }
            if (record.Def.techLevel == TechLevel.Undefined || record.Def.techLevel == 0)
            {
                return true;
            }
            return record.Def.techLevel <= maxTechLevel;
        }

        private static List<ThingDefRecord> BuildSuggestions(
            IReadOnlyList<ThingDefRecord> records,
            ItemAirdropIntent intent,
            string normalizedQuery,
            int minScore,
            TechLevel maxTechLevel)
        {
            ThingDefMatchRequest request = ThingDefResolver.BuildMatchRequest(intent, minScore, Math.Max(24, MaxSuggestions * 4));
            IEnumerable<ThingDefRecord> ranked = ThingDefMatchEngine.RankCandidates(records, request)
                .Select(candidate => candidate.Record)
                .Where(record => record?.Def != null);

            if (maxTechLevel < TechLevel.Archotech)
            {
                ranked = ranked.Where(record => IsWithinTechLevel(record, maxTechLevel));
            }

            List<ThingDefRecord> result = ranked.Take(MaxSuggestions).ToList();
            if (result.Count > 0)
            {
                return result;
            }

            if (string.IsNullOrWhiteSpace(normalizedQuery))
            {
                return result;
            }

            return records
                .Where(record => record?.Def != null)
                .Where(record =>
                    NormalizeQuery(record.DefName).Contains(normalizedQuery) ||
                    NormalizeQuery(record.Label).Contains(normalizedQuery) ||
                    NormalizeQuery(record.SearchText).Contains(normalizedQuery))
                .Take(MaxSuggestions)
                .ToList();
        }

        public bool TryBindToRecord(ThingDefRecord record)
        {
            if (record?.Def == null)
            {
                return false;
            }

            currentBindingDefName = record.DefName;
            currentBindingLabel = record.Label;
            currentBindingSearchText = record.SearchText;
            return true;
        }

        public void ClearBinding()
        {
            currentBindingDefName = string.Empty;
            currentBindingLabel = string.Empty;
            currentBindingSearchText = string.Empty;
        }

        public bool IsSearchTextStillMatchingBinding(string searchText)
        {
            if (!HasBinding || string.IsNullOrWhiteSpace(searchText))
            {
                return true;
            }

            string normalized = NormalizeQuery(searchText);
            if (string.Equals(normalized, currentBindingSearchText, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(normalized, currentBindingDefName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(normalized, currentBindingLabel, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        public void ClearSuggestions()
        {
            cachedSuggestions.Clear();
        }

        private static string NormalizeQuery(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return text.Trim().ToLowerInvariant();
        }
    }
}
