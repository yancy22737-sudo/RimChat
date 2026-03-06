using System;
using RimWorld;
using RimDiplomacy.Config;

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
        string BuildFullSystemPrompt(Faction faction, SystemPromptConfig config);
    }
}
