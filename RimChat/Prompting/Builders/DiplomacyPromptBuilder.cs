using System.Collections.Generic;
using RimChat.Config;
using RimChat.Persistence;
using RimWorld;

namespace RimChat.Prompting.Builders
{
    /// <summary>/// Dependencies: PromptPersistenceService hierarchical diplomacy builder core.
 /// Responsibility: orchestrate diplomacy prompt build entry without changing output behavior.
 ///</summary>
    internal sealed class DiplomacyPromptBuilder
    {
        private readonly PromptPersistenceService promptService;

        public DiplomacyPromptBuilder(PromptPersistenceService promptService)
        {
            this.promptService = promptService;
        }

        public string Build(
            Faction faction,
            SystemPromptConfig config,
            bool isProactive,
            IEnumerable<string> additionalSceneTags)
        {
            return promptService.BuildFullSystemPromptHierarchicalCore(
                faction,
                config,
                isProactive,
                additionalSceneTags);
        }
    }
}
