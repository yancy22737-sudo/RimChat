using System;

namespace RimChat.Persistence
{
    /// <summary>
    /// Dependencies: none.
    /// Responsibility: carry precomputed diplomacy-strategy prompt blocks into the dedicated strategy builder.
    /// </summary>
    public sealed class DiplomacyStrategyPromptContext
    {
        public string NegotiatorContextText = string.Empty;
        public string StrategyFactPackText = string.Empty;
        public string ScenarioDossierText = string.Empty;

        public DiplomacyStrategyPromptContext Clone()
        {
            return new DiplomacyStrategyPromptContext
            {
                NegotiatorContextText = NegotiatorContextText ?? string.Empty,
                StrategyFactPackText = StrategyFactPackText ?? string.Empty,
                ScenarioDossierText = ScenarioDossierText ?? string.Empty
            };
        }

        public bool HasAnyContent()
        {
            return !string.IsNullOrWhiteSpace(NegotiatorContextText) ||
                   !string.IsNullOrWhiteSpace(StrategyFactPackText) ||
                   !string.IsNullOrWhiteSpace(ScenarioDossierText);
        }
    }
}
