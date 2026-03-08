using System.Collections.Generic;
using RimChat.Persistence;
using Verse;

namespace RimChat.Prompting.Builders
{
    /// <summary>/// Dependencies: PromptPersistenceService hierarchical RPG builder core.
 /// Responsibility: orchestrate RPG prompt build entry without changing output behavior.
 ///</summary>
    internal sealed class RpgPromptBuilder
    {
        private readonly PromptPersistenceService promptService;

        public RpgPromptBuilder(PromptPersistenceService promptService)
        {
            this.promptService = promptService;
        }

        public string Build(
            Pawn initiator,
            Pawn target,
            bool isProactive,
            IEnumerable<string> additionalSceneTags)
        {
            return promptService.BuildRpgSystemPromptHierarchicalCore(
                initiator,
                target,
                isProactive,
                additionalSceneTags);
        }
    }
}
