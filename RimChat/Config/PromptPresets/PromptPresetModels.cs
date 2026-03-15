using System;
using System.Collections.Generic;

namespace RimChat.Config
{
    [Serializable]
    internal sealed class PromptPresetStoreConfig
    {
        public int SchemaVersion = 1;
        public string ActivePresetId = string.Empty;
        public List<PromptPresetConfig> Presets = new List<PromptPresetConfig>();
    }

    [Serializable]
    internal sealed class PromptPresetConfig
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
    internal sealed class PromptPresetChannelPayloads
    {
        public PromptChannelPayload Diplomacy = new PromptChannelPayload();
        public PromptChannelPayload Rpg = new PromptChannelPayload();
        public RimTalkChannelCompatConfig RimTalkDiplomacy = RimTalkChannelCompatConfig.CreateDefault();
        public RimTalkChannelCompatConfig RimTalkRpg = RimTalkChannelCompatConfig.CreateDefault();
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
                RimTalkDiplomacy = RimTalkDiplomacy?.Clone() ?? RimTalkChannelCompatConfig.CreateDefault(),
                RimTalkRpg = RimTalkRpg?.Clone() ?? RimTalkChannelCompatConfig.CreateDefault(),
                RimTalkSummaryHistoryLimit = RimTalkSummaryHistoryLimit,
                RimTalkAutoPushSessionSummary = RimTalkAutoPushSessionSummary,
                RimTalkAutoInjectCompatPreset = RimTalkAutoInjectCompatPreset,
                RimTalkPersonaCopyTemplate = RimTalkPersonaCopyTemplate ?? RimChatSettings.DefaultRimTalkPersonaCopyTemplate
            };
        }
    }

    [Serializable]
    internal sealed class PromptChannelPayload
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
        public int DiplomacyChars;
        public int RpgChars;
        public int RimTalkDiplomacyChars;
        public int RimTalkRpgChars;
    }
}
