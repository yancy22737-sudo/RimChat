using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: RimTalk prompt entry models and localized keyed labels.
    /// Responsibility: centralize prompt-entry channel ids, labels, defaults, and runtime matching policy.
    /// </summary>
    internal static class RimTalkPromptEntryChannelCatalog
    {
        internal const string Any = "any";
        internal const string DiplomacyDialogue = "diplomacy_dialogue";
        internal const string RpgDialogue = "rpg_dialogue";
        internal const string DiplomacyStrategy = "diplomacy_strategy";
        internal const string ProactiveDiplomacyDialogue = "proactive_diplomacy_dialogue";
        internal const string ProactiveRpgDialogue = "proactive_rpg_dialogue";
        internal const string SocialCirclePost = "social_circle_post";
        internal const string PersonaBootstrap = "persona_bootstrap";
        internal const string SummaryGeneration = "summary_generation";
        internal const string RpgArchiveCompression = "rpg_archive_compression";
        internal const string ImageGeneration = "image_generation";

        private static readonly string[] AllChannelIds =
        {
            Any,
            DiplomacyDialogue,
            RpgDialogue,
            DiplomacyStrategy,
            ProactiveDiplomacyDialogue,
            ProactiveRpgDialogue,
            SocialCirclePost,
            PersonaBootstrap,
            SummaryGeneration,
            RpgArchiveCompression,
            ImageGeneration
        };

        private static readonly EntrySeedDefinition[] DiplomacySeeds =
        {
            new EntrySeedDefinition(DiplomacyDialogue, "RimChat_RimTalkEntrySeed_DiplomacyDialogue", "Diplomacy Dialogue", true),
            new EntrySeedDefinition(DiplomacyStrategy, "RimChat_RimTalkEntrySeed_DiplomacyStrategy", "Diplomacy Strategy", false),
            new EntrySeedDefinition(ProactiveDiplomacyDialogue, "RimChat_RimTalkEntrySeed_ProactiveDiplomacy", "Proactive Diplomacy Dialogue", false),
            new EntrySeedDefinition(SocialCirclePost, "RimChat_RimTalkEntrySeed_SocialCirclePost", "Social Circle Post", false),
            new EntrySeedDefinition(SummaryGeneration, "RimChat_RimTalkEntrySeed_SummaryGeneration", "Summary Generation", false),
            new EntrySeedDefinition(ImageGeneration, "RimChat_RimTalkEntrySeed_ImageGeneration", "Image Generation", false)
        };

        private static readonly EntrySeedDefinition[] RpgSeeds =
        {
            new EntrySeedDefinition(RpgDialogue, "RimChat_RimTalkEntrySeed_RpgDialogue", "RPG Dialogue", true),
            new EntrySeedDefinition(ProactiveRpgDialogue, "RimChat_RimTalkEntrySeed_ProactiveRpg", "Proactive RPG Dialogue", false),
            new EntrySeedDefinition(PersonaBootstrap, "RimChat_RimTalkEntrySeed_PersonaBootstrap", "Persona Bootstrap", false),
            new EntrySeedDefinition(SummaryGeneration, "RimChat_RimTalkEntrySeed_SummaryGeneration", "Summary Generation", false),
            new EntrySeedDefinition(RpgArchiveCompression, "RimChat_RimTalkEntrySeed_RpgArchiveCompression", "RPG Archive Compression", false)
        };

        internal static IReadOnlyList<EntrySeedDefinition> GetSeedDefinitions(RimTalkPromptChannel rootChannel)
        {
            return rootChannel == RimTalkPromptChannel.Diplomacy ? DiplomacySeeds : RpgSeeds;
        }

        internal static IReadOnlyList<string> GetSelectableChannels(RimTalkPromptChannel rootChannel)
        {
            EntrySeedDefinition[] seeds = rootChannel == RimTalkPromptChannel.Diplomacy ? DiplomacySeeds : RpgSeeds;
            return seeds.Select(seed => seed.ChannelId).ToList();
        }

        internal static string GetDefaultChannel(RimTalkPromptChannel rootChannel)
        {
            return rootChannel == RimTalkPromptChannel.Diplomacy
                ? DiplomacyDialogue
                : RpgDialogue;
        }

        internal static string NormalizeLoose(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId))
            {
                return Any;
            }

            string normalized = channelId.Trim().ToLowerInvariant();
            return AllChannelIds.Contains(normalized, StringComparer.Ordinal)
                ? normalized
                : Any;
        }

        internal static string NormalizeForRoot(string channelId, RimTalkPromptChannel rootChannel)
        {
            string normalized = NormalizeLoose(channelId);
            IReadOnlyList<string> selectable = GetSelectableChannels(rootChannel);
            if (normalized == Any || selectable.Contains(normalized))
            {
                return normalized;
            }

            return GetDefaultChannel(rootChannel);
        }

        internal static bool MatchesRuntimeChannel(string entryChannelId, RimTalkPromptChannel rootChannel, bool isProactive)
        {
            string normalized = NormalizeLoose(entryChannelId);
            if (normalized == Any)
            {
                return true;
            }

            if (rootChannel == RimTalkPromptChannel.Diplomacy)
            {
                if (isProactive)
                {
                    return normalized == ProactiveDiplomacyDialogue || normalized == DiplomacyDialogue;
                }

                return normalized == DiplomacyDialogue || normalized == DiplomacyStrategy;
            }

            if (isProactive)
            {
                return normalized == ProactiveRpgDialogue || normalized == RpgDialogue;
            }

            return normalized == RpgDialogue;
        }

        internal static string GetLabelKey(string channelId)
        {
            switch (NormalizeLoose(channelId))
            {
                case DiplomacyDialogue:
                    return "RimChat_RimTalkPromptChannel_DiplomacyDialogue";
                case RpgDialogue:
                    return "RimChat_RimTalkPromptChannel_RpgDialogue";
                case DiplomacyStrategy:
                    return "RimChat_RimTalkPromptChannel_DiplomacyStrategy";
                case ProactiveDiplomacyDialogue:
                    return "RimChat_RimTalkPromptChannel_ProactiveDiplomacyDialogue";
                case ProactiveRpgDialogue:
                    return "RimChat_RimTalkPromptChannel_ProactiveRpgDialogue";
                case SocialCirclePost:
                    return "RimChat_RimTalkPromptChannel_SocialCirclePost";
                case PersonaBootstrap:
                    return "RimChat_RimTalkPromptChannel_PersonaBootstrap";
                case SummaryGeneration:
                    return "RimChat_RimTalkPromptChannel_SummaryGeneration";
                case RpgArchiveCompression:
                    return "RimChat_RimTalkPromptChannel_RpgArchiveCompression";
                case ImageGeneration:
                    return "RimChat_RimTalkPromptChannel_ImageGeneration";
                default:
                    return "RimChat_RimTalkPromptChannel_Any";
            }
        }

        internal static string GetLabel(string channelId)
        {
            return ResolveLocalizedText(GetLabelKey(channelId), GetLabelFallback(channelId));
        }

        internal static string ResolveSeedName(EntrySeedDefinition seed)
        {
            return ResolveLocalizedText(seed.NameKey, seed.DefaultName);
        }

        private static string ResolveLocalizedText(string key, string fallback)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return fallback ?? string.Empty;
            }

            if (LanguageDatabase.activeLanguage == null)
            {
                return string.IsNullOrWhiteSpace(fallback) ? key : fallback;
            }

            if (Translator.TryTranslate(key, out TaggedString translated))
            {
                return translated.ToString();
            }

            return string.IsNullOrWhiteSpace(fallback) ? key : fallback;
        }

        private static string GetLabelFallback(string channelId)
        {
            switch (NormalizeLoose(channelId))
            {
                case DiplomacyDialogue:
                    return "Diplomacy Dialogue";
                case RpgDialogue:
                    return "RPG Dialogue";
                case DiplomacyStrategy:
                    return "Diplomacy Strategy";
                case ProactiveDiplomacyDialogue:
                    return "Proactive Diplomacy Dialogue";
                case ProactiveRpgDialogue:
                    return "Proactive RPG Dialogue";
                case SocialCirclePost:
                    return "Social Circle Post";
                case PersonaBootstrap:
                    return "Persona Bootstrap";
                case SummaryGeneration:
                    return "Summary Generation";
                case RpgArchiveCompression:
                    return "RPG Archive Compression";
                case ImageGeneration:
                    return "Image Generation";
                default:
                    return "Generic (All)";
            }
        }

        internal readonly struct EntrySeedDefinition
        {
            internal readonly string ChannelId;
            internal readonly string NameKey;
            internal readonly string DefaultName;
            internal readonly bool EnabledByDefault;

            internal EntrySeedDefinition(string channelId, string nameKey, string defaultName, bool enabledByDefault)
            {
                ChannelId = channelId ?? Any;
                NameKey = nameKey ?? string.Empty;
                DefaultName = defaultName ?? "Entry";
                EnabledByDefault = enabledByDefault;
            }
        }
    }
}
