using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.DiplomacySystem;

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

        public void ComputeSuggestions(string query, HashSet<string> blacklist = null)
        {
            string normalized = NormalizeQuery(query ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                cachedSuggestions.Clear();
                return;
            }

            ItemAirdropIntent intent = ItemAirdropIntent.Create(normalized, string.Empty, "trade");
            IReadOnlyList<ThingDefRecord> records = ThingDefCatalog.GetRecords();
            ThingDefMatchRequest request = ThingDefResolver.BuildMatchRequest(intent, 180, MaxSuggestions);
            cachedSuggestions = ThingDefMatchEngine.RankCandidates(records, request)
                .Select(candidate => candidate.Record)
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
