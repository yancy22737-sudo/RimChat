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
        // Cache for GetOrderedMainChainSections to avoid per-frame allocations (C optimization)
        private static string _orderedSectionsCacheKey = string.Empty;
        private static bool _orderedSectionsCacheEnabledOnly;
        private static List<PromptSectionSchemaItem> _orderedSectionsCache;

        private static readonly PromptSectionSchemaItem[] MainChainSections =
        {
            new PromptSectionSchemaItem("system_rules", "System Rules", "系统规则", "RimChat_PromptSectionLabel_SystemRules"),
            new PromptSectionSchemaItem("character_persona", "Persona", "角色人设", "RimChat_PromptSectionLabel_Persona"),
            new PromptSectionSchemaItem("memory_system", "Memory", "记忆", "RimChat_PromptSectionLabel_Memory"),
            new PromptSectionSchemaItem("environment_perception", "Environment", "环境感知", "RimChat_PromptSectionLabel_Environment"),
            new PromptSectionSchemaItem("context", "Context", "上下文", "RimChat_PromptSectionLabel_Context"),
            new PromptSectionSchemaItem("mod_variables", "Mod Variables", "模组变量", "RimChat_PromptSectionLabel_ModVariables"),
            new PromptSectionSchemaItem("action_rules", "Action Rules", "行为规则", "RimChat_PromptSectionLabel_ActionRules"),
            new PromptSectionSchemaItem("repetition_reinforcement", "Reinforcement", "强化规则", "RimChat_PromptSectionLabel_Reinforcement"),
            new PromptSectionSchemaItem("output_specification", "Output Format", "输出格式", "RimChat_PromptSectionLabel_OutputFormat")
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
            RimTalkPromptEntryChannelCatalog.ProactiveRpgDialogue,
            RimTalkPromptEntryChannelCatalog.PersonaBootstrap
        };

        private static readonly HashSet<string> SharedWorkspaceChannels = new HashSet<string>(StringComparer.Ordinal)
        {
        };

        private static readonly string[] AllWorkspaceChannels = DiplomacyWorkspaceChannels
            .Concat(RpgWorkspaceChannels)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        internal static IReadOnlyList<PromptSectionSchemaItem> GetMainChainSections()
        {
            return MainChainSections;
        }

        /// <summary>
        /// Returns main-chain sections ordered by the persisted section layouts.
        /// Sections without a persisted layout entry use their canonical array index as default order.
        /// </summary>
        internal static IReadOnlyList<PromptSectionSchemaItem> GetOrderedMainChainSections(
            List<PromptSectionLayoutConfig> sectionLayouts)
        {
            return GetOrderedMainChainSections(sectionLayouts, enabledOnly: false);
        }

        /// <summary>
        /// Returns main-chain sections ordered by the persisted section layouts.
        /// When enabledOnly is true, sections with Enabled=false in layout are excluded.
        /// </summary>
        internal static IReadOnlyList<PromptSectionSchemaItem> GetOrderedMainChainSections(
            List<PromptSectionLayoutConfig> sectionLayouts,
            bool enabledOnly)
        {
            // Build a cache key from section layout content
            string cacheKey = BuildSectionLayoutCacheKey(sectionLayouts);
            if (string.Equals(_orderedSectionsCacheKey, cacheKey, StringComparison.Ordinal) &&
                _orderedSectionsCacheEnabledOnly == enabledOnly &&
                _orderedSectionsCache != null)
            {
                return _orderedSectionsCache;
            }

            if (sectionLayouts == null || sectionLayouts.Count == 0)
            {
                _orderedSectionsCacheKey = cacheKey;
                _orderedSectionsCacheEnabledOnly = enabledOnly;
                _orderedSectionsCache = new List<PromptSectionSchemaItem>(MainChainSections);
                return _orderedSectionsCache;
            }

            var orderMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var enabledMap = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (PromptSectionLayoutConfig layout in sectionLayouts)
            {
                if (layout != null && !string.IsNullOrWhiteSpace(layout.SectionId))
                {
                    orderMap[layout.SectionId] = layout.Order;
                    enabledMap[layout.SectionId] = layout.Enabled;
                }
            }

            var result = new List<PromptSectionSchemaItem>();
            foreach (PromptSectionSchemaItem section in MainChainSections)
            {
                if (enabledOnly && enabledMap.TryGetValue(section.Id, out bool enabled) && !enabled)
                {
                    continue;
                }

                result.Add(section);
            }

            result.Sort((a, b) =>
            {
                int orderA = orderMap.TryGetValue(a.Id, out int oa) ? oa : Array.IndexOf(MainChainSections, a) * 10;
                int orderB = orderMap.TryGetValue(b.Id, out int ob) ? ob : Array.IndexOf(MainChainSections, b) * 10;
                int cmp = orderA.CompareTo(orderB);
                return cmp != 0 ? cmp : string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
            });

            _orderedSectionsCacheKey = cacheKey;
            _orderedSectionsCacheEnabledOnly = enabledOnly;
            _orderedSectionsCache = result;
            return result;
        }

        private static string BuildSectionLayoutCacheKey(List<PromptSectionLayoutConfig> layouts)
        {
            if (layouts == null || layouts.Count == 0)
            {
                return string.Empty;
            }

            var sb = new System.Text.StringBuilder(layouts.Count * 24);
            for (int i = 0; i < layouts.Count; i++)
            {
                PromptSectionLayoutConfig layout = layouts[i];
                if (layout != null && !string.IsNullOrWhiteSpace(layout.SectionId))
                {
                    sb.Append(layout.SectionId).Append(':')
                      .Append(layout.Order).Append(':')
                      .Append(layout.Enabled ? '1' : '0').Append('|');
                }
            }

            return sb.ToString();
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
