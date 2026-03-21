using System;
using System.Collections.Generic;
using RimWorld;
using RimChat.Config;
using Verse;

namespace RimChat.Persistence
{
    public interface IPromptPersistenceService
    {
        SystemPromptConfig LoadConfig();
        SystemPromptConfig LoadConfigReadOnly();
        bool RepairAndRewritePromptDomains();
        void SaveConfig(SystemPromptConfig config);
        bool ConfigExists();
        void ResetToDefault();
        string GetConfigFilePath();
        bool ExportConfig(string filePath);
        bool ImportConfig(string filePath);
        string BuildFullSystemPrompt(Faction faction, SystemPromptConfig config, bool isProactive, IEnumerable<string> additionalSceneTags);
        string BuildDiplomacyStrategySystemPrompt(
            Faction faction,
            SystemPromptConfig config,
            IEnumerable<string> additionalSceneTags,
            DiplomacyStrategyPromptContext strategyContext);
        string BuildRPGFullSystemPrompt(Pawn initiator, Pawn target, bool isProactive, IEnumerable<string> additionalSceneTags);
    }
}
