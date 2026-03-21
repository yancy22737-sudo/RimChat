using System.Collections.Generic;

namespace RimChat.Config
{
    internal interface IPromptPresetService
    {
        PromptPresetStoreConfig LoadAll(RimChatSettings settings);
        void SaveAll(PromptPresetStoreConfig store);
        PromptPresetConfig CreateFromLegacy(RimChatSettings settings, string name);
        PromptPresetConfig Duplicate(RimChatSettings settings, PromptPresetConfig source, string name);
        bool Activate(RimChatSettings settings, PromptPresetStoreConfig store, string presetId, out string error);
        bool IsDefaultPreset(PromptPresetStoreConfig store, string presetId);
        bool EnsureEditablePresetForMutation(
            RimChatSettings settings,
            PromptPresetStoreConfig store,
            string selectedPresetId,
            string forkNamePrefix,
            out PromptPresetConfig editablePreset,
            out bool forked,
            out string error);
        bool SyncPresetPayloadFromSettings(
            RimChatSettings settings,
            PromptPresetStoreConfig store,
            string presetId,
            out string error);
        void ApplyPayloadToSettings(RimChatSettings settings, PromptPresetChannelPayloads payload, bool persistToFiles);
        bool ExportPreset(string filePath, PromptPresetConfig preset, out string error);
        bool ImportPreset(string filePath, PromptPresetStoreConfig store, out PromptPresetConfig imported, out string error);
        List<PromptPresetSummary> BuildSummaries(PromptPresetStoreConfig store);
    }
}
