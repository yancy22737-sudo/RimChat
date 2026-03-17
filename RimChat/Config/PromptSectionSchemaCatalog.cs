using System;
using System.Collections.Generic;
using System.Linq;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: RimTalk prompt-channel catalog.
    /// Responsibility: define the canonical main-chain prompt section schema and stable workspace channels.
    /// </summary>
    internal static class PromptSectionSchemaCatalog
    {
        private static readonly PromptSectionSchemaItem[] MainChainSections =
        {
            new PromptSectionSchemaItem("system_rules", "System Rules", "系统规则"),
            new PromptSectionSchemaItem("character_persona", "Character Persona", "人物设定"),
            new PromptSectionSchemaItem("memory_system", "Memory System", "记忆系统"),
            new PromptSectionSchemaItem("environment_perception", "Environment Perception", "环境感知"),
            new PromptSectionSchemaItem("context", "Context", "上下文"),
            new PromptSectionSchemaItem("action_rules", "Action Rules", "行动规则"),
            new PromptSectionSchemaItem("repetition_reinforcement", "Repetition Reinforcement", "重复强化"),
            new PromptSectionSchemaItem("output_specification", "Output Specification", "输出规范")
        };

        private static readonly string[] DiplomacyWorkspaceChannels =
        {
            RimTalkPromptEntryChannelCatalog.DiplomacyDialogue,
            RimTalkPromptEntryChannelCatalog.DiplomacyStrategy,
            RimTalkPromptEntryChannelCatalog.ProactiveDiplomacyDialogue,
            RimTalkPromptEntryChannelCatalog.SocialCirclePost
        };

        private static readonly string[] RpgWorkspaceChannels =
        {
            RimTalkPromptEntryChannelCatalog.RpgDialogue,
            RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue
        };

        internal static IReadOnlyList<PromptSectionSchemaItem> GetMainChainSections()
        {
            return MainChainSections;
        }

        internal static IReadOnlyList<string> GetWorkspaceChannels(RimTalkPromptChannel rootChannel)
        {
            return rootChannel == RimTalkPromptChannel.Diplomacy
                ? DiplomacyWorkspaceChannels
                : RpgWorkspaceChannels;
        }

        internal static string GetDefaultWorkspaceChannel(RimTalkPromptChannel rootChannel)
        {
            return rootChannel == RimTalkPromptChannel.Diplomacy
                ? RimTalkPromptEntryChannelCatalog.DiplomacyDialogue
                : RimTalkPromptEntryChannelCatalog.RpgDialogue;
        }

        internal static string ResolveRuntimePromptChannel(RimTalkPromptChannel rootChannel, bool isProactive)
        {
            if (rootChannel == RimTalkPromptChannel.Diplomacy)
            {
                return isProactive
                    ? RimTalkPromptEntryChannelCatalog.ProactiveDiplomacyDialogue
                    : RimTalkPromptEntryChannelCatalog.DiplomacyDialogue;
            }

            return isProactive
                ? RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue
                : RimTalkPromptEntryChannelCatalog.RpgDialogue;
        }

        internal static bool TryGetSection(string sectionId, out PromptSectionSchemaItem section)
        {
            string normalized = NormalizeSectionId(sectionId);
            section = MainChainSections.FirstOrDefault(item =>
                string.Equals(item.Id, normalized, StringComparison.OrdinalIgnoreCase));
            return !string.IsNullOrWhiteSpace(section.Id);
        }

        internal static string NormalizeSectionId(string sectionId)
        {
            return string.IsNullOrWhiteSpace(sectionId)
                ? string.Empty
                : sectionId.Trim().ToLowerInvariant();
        }
    }

    internal readonly struct PromptSectionSchemaItem
    {
        internal readonly string Id;
        internal readonly string EnglishName;
        internal readonly string ChineseName;

        internal PromptSectionSchemaItem(string id, string englishName, string chineseName)
        {
            Id = id ?? string.Empty;
            EnglishName = englishName ?? string.Empty;
            ChineseName = chineseName ?? string.Empty;
        }
    }
}
