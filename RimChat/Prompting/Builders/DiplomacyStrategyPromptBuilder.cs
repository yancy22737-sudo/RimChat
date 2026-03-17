using System.Collections.Generic;
using RimChat.Config;
using RimChat.Persistence;
using RimWorld;

namespace RimChat.Prompting.Builders
{
    /// <summary>
    /// Dependencies: PromptPersistenceService diplomacy-strategy builder core.
    /// Responsibility: orchestrate the dedicated diplomacy-strategy system prompt entry.
    /// </summary>
    internal sealed class DiplomacyStrategyPromptBuilder
    {
        private readonly PromptPersistenceService promptService;

        public DiplomacyStrategyPromptBuilder(PromptPersistenceService promptService)
        {
            this.promptService = promptService;
        }

        public string Build(
            Faction faction,
            SystemPromptConfig config,
            IEnumerable<string> additionalSceneTags,
            DiplomacyStrategyPromptContext strategyContext)
        {
            return promptService.BuildDiplomacyStrategySystemPromptCore(
                faction,
                config,
                additionalSceneTags,
                strategyContext);
        }
    }
}
