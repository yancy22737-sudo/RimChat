using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

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
            new PromptSectionSchemaItem("system_rules", "System Rules", "系统规则", "RimChat_PromptSectionLabel_SystemRules"),
            new PromptSectionSchemaItem("character_persona", "Persona", "角色人设", "RimChat_PromptSectionLabel_Persona"),
            new PromptSectionSchemaItem("memory_system", "Memory", "记忆", "RimChat_PromptSectionLabel_Memory"),
            new PromptSectionSchemaItem("environment_perception", "Environment", "环境感知", "RimChat_PromptSectionLabel_Environment"),
            new PromptSectionSchemaItem("context", "Context", "上下文", "RimChat_PromptSectionLabel_Context"),
            new PromptSectionSchemaItem("action_rules", "Action Rules", "行为规则", "RimChat_PromptSectionLabel_ActionRules"),
            new PromptSectionSchemaItem("repetition_reinforcement", "Reinforcement", "强化规则", "RimChat_PromptSectionLabel_Reinforcement"),
            new PromptSectionSchemaItem("output_specification", "Output Format", "输出格式", "RimChat_PromptSectionLabel_OutputFormat")
        };

        private static readonly string[] DiplomacyWorkspaceChannels =
        {
            RimTalkPromptEntryChannelCatalog.DiplomacyDialogue,
            RimTalkPromptEntryChannelCatalog.DiplomacyStrategy,
            RimTalkPromptEntryChannelCatalog.ProactiveDiplomacyDialogue,
            RimTalkPromptEntryChannelCatalog.SocialCirclePost,
            RimTalkPromptEntryChannelCatalog.SummaryGeneration,
            RimTalkPromptEntryChannelCatalog.ImageGeneration
        };

        private static readonly string[] RpgWorkspaceChannels =
        {
            RimTalkPromptEntryChannelCatalog.RpgDialogue,
            RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue,
            RimTalkPromptEntryChannelCatalog.PersonaBootstrap,
            RimTalkPromptEntryChannelCatalog.SummaryGeneration,
            RimTalkPromptEntryChannelCatalog.RpgArchiveCompression
        };

        private static readonly HashSet<string> SharedWorkspaceChannels = new HashSet<string>(StringComparer.Ordinal)
        {
            RimTalkPromptEntryChannelCatalog.SummaryGeneration
        };

        private static readonly string[] AllWorkspaceChannels = DiplomacyWorkspaceChannels
            .Concat(RpgWorkspaceChannels)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

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

        internal static IReadOnlyList<string> GetAllWorkspaceChannels()
        {
            return AllWorkspaceChannels;
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

        internal static string NormalizeWorkspaceChannel(string promptChannel, RimTalkPromptChannel rootChannel)
        {
            string normalized = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            if (normalized == RimTalkPromptEntryChannelCatalog.Any)
            {
                return GetDefaultWorkspaceChannel(rootChannel);
            }

            if (DoesChannelBelongToRoot(normalized, rootChannel))
            {
                return normalized;
            }

            return GetDefaultWorkspaceChannel(rootChannel);
        }

        internal static string NormalizeRuntimePromptChannel(
            string promptChannel,
            RimTalkPromptChannel rootChannel,
            bool isProactive)
        {
            string normalized = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            if (normalized == RimTalkPromptEntryChannelCatalog.Any)
            {
                return ResolveRuntimePromptChannel(rootChannel, isProactive);
            }

            if (DoesChannelBelongToRoot(normalized, rootChannel))
            {
                return normalized;
            }

            return ResolveRuntimePromptChannel(rootChannel, isProactive);
        }

        internal static bool IsSharedWorkspaceChannel(string promptChannel)
        {
            string normalized = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            return SharedWorkspaceChannels.Contains(normalized);
        }

        internal static bool DoesChannelBelongToRoot(string promptChannel, RimTalkPromptChannel rootChannel)
        {
            string normalized = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            if (normalized == RimTalkPromptEntryChannelCatalog.Any)
            {
                return false;
            }

            if (IsSharedWorkspaceChannel(normalized))
            {
                return true;
            }

            IReadOnlyList<string> channels = GetWorkspaceChannels(rootChannel);
            return channels.Contains(normalized, StringComparer.Ordinal);
        }

        internal static RimTalkPromptChannel ResolveRootChannel(
            string promptChannel,
            RimTalkPromptChannel fallbackRoot)
        {
            string normalized = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            if (normalized == RimTalkPromptEntryChannelCatalog.Any)
            {
                return fallbackRoot;
            }

            if (IsSharedWorkspaceChannel(normalized))
            {
                return fallbackRoot;
            }

            if (DiplomacyWorkspaceChannels.Contains(normalized, StringComparer.Ordinal))
            {
                return RimTalkPromptChannel.Diplomacy;
            }

            if (RpgWorkspaceChannels.Contains(normalized, StringComparer.Ordinal))
            {
                return RimTalkPromptChannel.Rpg;
            }

            return fallbackRoot;
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
        internal readonly string LabelKey;

        internal PromptSectionSchemaItem(string id, string englishName, string chineseName, string labelKey)
        {
            Id = id ?? string.Empty;
            EnglishName = englishName ?? string.Empty;
            ChineseName = chineseName ?? string.Empty;
            LabelKey = labelKey ?? string.Empty;
        }

        internal string GetDisplayLabel()
        {
            if (!string.IsNullOrWhiteSpace(LabelKey))
            {
                return LabelKey.Translate().ToString();
            }

            return EnglishName;
        }
    }
}
