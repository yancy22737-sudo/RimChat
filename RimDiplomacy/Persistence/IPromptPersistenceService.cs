using System;
using System.Collections.Generic;
using RimWorld;
using RimDiplomacy.Config;
using Verse;

namespace RimDiplomacy.Persistence
{
    public interface IPromptPersistenceService
    {
        SystemPromptConfig LoadConfig();
        void SaveConfig(SystemPromptConfig config);
        bool ConfigExists();
        void ResetToDefault();
        string GetConfigFilePath();
        bool ExportConfig(string filePath);
        bool ImportConfig(string filePath);
        string BuildFullSystemPrompt(Faction faction, SystemPromptConfig config, bool isProactive, IEnumerable<string> additionalSceneTags);
        string BuildRPGFullSystemPrompt(Pawn initiator, Pawn target, bool isProactive, IEnumerable<string> additionalSceneTags);
    }
}
