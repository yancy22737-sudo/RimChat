using System;
using System.Collections.Generic;

namespace RimChat.Config
{
    [Serializable]
    public sealed class PromptPresetStoreConfig
    {
        public int SchemaVersion = 2;
        public string ActivePresetId = string.Empty;
        public string DefaultPresetId = string.Empty;
        public List<PromptPresetConfig> Presets = new List<PromptPresetConfig>();
    }

    [Serializable]
    public sealed class PromptPresetConfig
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public bool IsActive;
        public string CreatedAtUtc = string.Empty;
        public string UpdatedAtUtc = string.Empty;
        public PromptPresetChannelPayloads ChannelPayloads = new PromptPresetChannelPayloads();

        public PromptPresetConfig Clone()
        {
            return new PromptPresetConfig
            {
                Id = Id ?? string.Empty,
                Name = Name ?? string.Empty,
                IsActive = IsActive,
                CreatedAtUtc = CreatedAtUtc ?? string.Empty,
                UpdatedAtUtc = UpdatedAtUtc ?? string.Empty,
                ChannelPayloads = ChannelPayloads?.Clone() ?? new PromptPresetChannelPayloads()
            };
        }
    }

    [Serializable]
    public sealed class PromptPresetChannelPayloads
    {
        public PromptChannelPayload Diplomacy = new PromptChannelPayload();
        public PromptChannelPayload Rpg = new PromptChannelPayload();
        public PromptUnifiedCatalog UnifiedPromptCatalog = PromptUnifiedCatalog.CreateFallback();
        public int RimTalkSummaryHistoryLimit = 10;
        public bool RimTalkAutoPushSessionSummary;
        public bool RimTalkAutoInjectCompatPreset;
        public string RimTalkPersonaCopyTemplate = RimChatSettings.DefaultRimTalkPersonaCopyTemplate;

        public PromptPresetChannelPayloads Clone()
        {
            return new PromptPresetChannelPayloads
            {
                Diplomacy = Diplomacy?.Clone() ?? new PromptChannelPayload(),
                Rpg = Rpg?.Clone() ?? new PromptChannelPayload(),
                UnifiedPromptCatalog = UnifiedPromptCatalog?.Clone() ?? PromptUnifiedCatalog.CreateFallback(),
                RimTalkSummaryHistoryLimit = RimTalkSummaryHistoryLimit,
                RimTalkAutoPushSessionSummary = RimTalkAutoPushSessionSummary,
                RimTalkAutoInjectCompatPreset = RimTalkAutoInjectCompatPreset,
                RimTalkPersonaCopyTemplate = RimTalkPersonaCopyTemplate ?? RimChatSettings.DefaultRimTalkPersonaCopyTemplate
            };
        }
    }

    [Serializable]
    public sealed class PromptChannelPayload
    {
        public string SystemPromptCustomJson = string.Empty;
        public string DialoguePromptCustomJson = string.Empty;
        public string PawnPromptCustomJson = string.Empty;
        public string SocialCirclePromptCustomJson = string.Empty;
        public string FactionPromptsCustomJson = string.Empty;

        public PromptChannelPayload Clone()
        {
            return new PromptChannelPayload
            {
                SystemPromptCustomJson = SystemPromptCustomJson ?? string.Empty,
                DialoguePromptCustomJson = DialoguePromptCustomJson ?? string.Empty,
                PawnPromptCustomJson = PawnPromptCustomJson ?? string.Empty,
                SocialCirclePromptCustomJson = SocialCirclePromptCustomJson ?? string.Empty,
                FactionPromptsCustomJson = FactionPromptsCustomJson ?? string.Empty
            };
        }
    }

    internal sealed class PromptPresetSummary
    {
        public string Id;
        public string Name;
        public bool IsActive;
        public bool IsDefault;
        public int DiplomacyChars;
        public int RpgChars;
        public int PromptSectionChars;
    }
}
