using System;
using System.Collections.Generic;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: channel catalog ids/seed definitions and RimTalk prompt entry config models.
    /// Responsibility: normalize per-channel prompt entries and safely seed missing built-in channels.
    /// </summary>
    internal static class RimTalkPromptEntrySeedSynchronizer
    {
        internal static bool EnsureCoverage(RimTalkPromptChannel channel, RimTalkChannelCompatConfig config)
        {
            if (config == null)
            {
                return false;
            }

            bool changed = false;
            config.NormalizeWith(RimTalkChannelCompatConfig.CreateDefault());
            config.PromptEntries ??= new List<RimTalkPromptEntryConfig>();
            changed |= NormalizeEntryChannels(channel, config.PromptEntries);
            changed |= AppendMissingSeedEntries(channel, config.PromptEntries);
            return changed;
        }

        private static bool NormalizeEntryChannels(RimTalkPromptChannel channel, List<RimTalkPromptEntryConfig> entries)
        {
            bool changed = false;
            for (int i = 0; i < entries.Count; i++)
            {
                RimTalkPromptEntryConfig entry = entries[i];
                if (entry == null)
                {
                    continue;
                }

                string normalized = RimTalkPromptEntryChannelCatalog.NormalizeForRoot(entry.PromptChannel, channel);
                if (!string.Equals(normalized, entry.PromptChannel, StringComparison.OrdinalIgnoreCase))
                {
                    entry.PromptChannel = normalized;
                    changed = true;
                }
            }

            return changed;
        }

        private static bool AppendMissingSeedEntries(RimTalkPromptChannel channel, List<RimTalkPromptEntryConfig> entries)
        {
            bool changed = false;
            IReadOnlyList<RimTalkPromptEntryChannelCatalog.EntrySeedDefinition> seeds =
                RimTalkPromptEntryChannelCatalog.GetSeedDefinitions(channel);
            for (int i = 0; i < seeds.Count; i++)
            {
                RimTalkPromptEntryChannelCatalog.EntrySeedDefinition seed = seeds[i];
                if (HasSeedEntry(entries, seed.ChannelId))
                {
                    continue;
                }

                entries.Add(CreateSeedEntry(seed));
                changed = true;
            }

            return changed;
        }

        private static bool HasSeedEntry(List<RimTalkPromptEntryConfig> entries, string channelId)
        {
            string normalizedSeedChannel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(channelId);
            for (int i = 0; i < entries.Count; i++)
            {
                RimTalkPromptEntryConfig entry = entries[i];
                if (entry == null)
                {
                    continue;
                }

                string entryChannel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(entry.PromptChannel);
                if (string.Equals(entryChannel, normalizedSeedChannel, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static RimTalkPromptEntryConfig CreateSeedEntry(RimTalkPromptEntryChannelCatalog.EntrySeedDefinition seed)
        {
            return new RimTalkPromptEntryConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = RimTalkPromptEntryChannelCatalog.ResolveSeedName(seed),
                Role = "System",
                CustomRole = string.Empty,
                Position = "Relative",
                InChatDepth = 0,
                Enabled = seed.EnabledByDefault,
                PromptChannel = seed.ChannelId,
                Content = string.Empty
            };
        }
    }
}
