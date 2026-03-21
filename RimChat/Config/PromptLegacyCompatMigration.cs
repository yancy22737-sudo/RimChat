using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimChat.Prompting;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: RimTalk channel compat models and prompt template rewrite service.
    /// Responsibility: migrate legacy RimTalk prompt payloads into sanitized import-only channel configs.
    /// </summary>
    internal static partial class PromptLegacyCompatMigration
    {
        [Serializable]
        private sealed class LegacyPromptCompatPayload
        {
            public bool EnableRimTalkPromptCompat = true;
            public int RimTalkPresetInjectionMaxEntries = RimChatSettings.RimTalkPresetInjectionLimitUnlimited;
            public int RimTalkPresetInjectionMaxChars = RimChatSettings.RimTalkPresetInjectionLimitUnlimited;
            public string RimTalkCompatTemplate = string.Empty;
            public RimTalkChannelCompatConfig RimTalkDiplomacy = null;
            public RimTalkChannelCompatConfig RimTalkRpg = null;
        }

        private static readonly PromptSectionDefinition[] SectionDefinitions =
        {
            new PromptSectionDefinition("system_rules", "System Rules", "系统规则"),
            new PromptSectionDefinition("character_persona", "Persona", "角色人设", "Character Persona", "人物设定", "人格"),
            new PromptSectionDefinition("memory_system", "Memory", "记忆", "Memory System", "记忆系统"),
            new PromptSectionDefinition("environment_perception", "Environment", "环境感知", "Environment Perception", "环境"),
            new PromptSectionDefinition("context", "Context", "上下文"),
            new PromptSectionDefinition("action_rules", "Action Rules", "行为规则", "行动规则"),
            new PromptSectionDefinition("repetition_reinforcement", "Reinforcement", "强化规则", "Repetition Reinforcement", "重复强化", "强化"),
            new PromptSectionDefinition("output_specification", "Output Format", "输出格式", "Output Specification", "输出规范")
        };

        public static RimTalkPromptEntryDefaultsConfig ApplyLegacyPayloadToPromptSections(
            RimTalkPromptEntryDefaultsConfig currentSections,
            bool enablePromptCompat,
            int presetInjectionMaxEntries,
            int presetInjectionMaxChars,
            string compatTemplate,
            RimTalkChannelCompatConfig diplomacy,
            RimTalkChannelCompatConfig rpg,
            string sourceIdPrefix)
        {
            RimTalkPromptEntryDefaultsConfig normalized = NormalizePromptSections(currentSections);
            LegacyPromptMigrationReport report = CreateReport(sourceIdPrefix);
            bool hasExplicitChannels =
                HasMeaningfulLegacyChannelConfig(diplomacy) ||
                HasMeaningfulLegacyChannelConfig(rpg);

            if (!hasExplicitChannels && string.IsNullOrWhiteSpace(compatTemplate))
            {
                PublishReport(report);
                return normalized;
            }

            RimTalkChannelCompatConfig diplomacyConfig = hasExplicitChannels
                ? NormalizeChannelConfig(diplomacy, "diplomacy", $"{sourceIdPrefix}.diplomacy")
                : BuildFromLegacyFields(
                    enablePromptCompat,
                    presetInjectionMaxEntries,
                    presetInjectionMaxChars,
                    compatTemplate,
                    diplomacy,
                    "diplomacy",
                    $"{sourceIdPrefix}.diplomacy");
            RimTalkChannelCompatConfig rpgConfig = hasExplicitChannels
                ? NormalizeChannelConfig(rpg, "rpg", $"{sourceIdPrefix}.rpg")
                : BuildFromLegacyFields(
                    enablePromptCompat,
                    presetInjectionMaxEntries,
                    presetInjectionMaxChars,
                    compatTemplate,
                    rpg,
                    "rpg",
                    $"{sourceIdPrefix}.rpg");

            if (HasMeaningfulLegacyChannelConfig(diplomacyConfig))
            {
                normalized = ApplyLegacyAdapterToPromptSections(
                    normalized,
                    diplomacyConfig,
                    RimTalkPromptChannel.Diplomacy,
                    $"{sourceIdPrefix}.diplomacy",
                    report);
            }

            if (HasMeaningfulLegacyChannelConfig(rpgConfig))
            {
                normalized = ApplyLegacyAdapterToPromptSections(
                    normalized,
                    rpgConfig,
                    RimTalkPromptChannel.Rpg,
                    $"{sourceIdPrefix}.rpg",
                    report);
            }

            PublishReport(report);
            return normalized;
        }

        public static RimTalkPromptEntryDefaultsConfig ApplyLegacyPayloadToPromptSections(
            RimTalkPromptEntryDefaultsConfig currentSections,
            string rawJson,
            string sourceIdPrefix)
        {
            LegacyPromptMigrationReport report = CreateReport(sourceIdPrefix);
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                PublishReport(report);
                return NormalizePromptSections(currentSections);
            }

            try
            {
                LegacyPromptCompatPayload payload = JsonUtility.FromJson<LegacyPromptCompatPayload>(rawJson);
                if (payload == null)
                {
                    PublishReport(report);
                    return NormalizePromptSections(currentSections);
                }

                RimTalkPromptEntryDefaultsConfig migrated = ApplyLegacyPayloadToPromptSections(
                    currentSections,
                    payload.EnableRimTalkPromptCompat,
                    payload.RimTalkPresetInjectionMaxEntries,
                    payload.RimTalkPresetInjectionMaxChars,
                    payload.RimTalkCompatTemplate,
                    payload.RimTalkDiplomacy,
                    payload.RimTalkRpg,
                    sourceIdPrefix);
                return migrated;
            }
            catch (Exception ex)
            {
                RecordRejected(
                    report,
                    sourceIdPrefix,
                    string.Empty,
                    string.Empty,
                    $"Failed to parse legacy payload: {ex.Message}",
                    fallbackApplied: false);
                PublishReport(report);
                Log.Warning($"[RimChat] Failed to parse legacy compat payload for {sourceIdPrefix}: {ex.Message}");
                return NormalizePromptSections(currentSections);
            }
        }

        public static RimTalkChannelCompatConfig NormalizeChannelConfig(
            RimTalkChannelCompatConfig config,
            string channel,
            string idPrefix)
        {
            RimTalkChannelCompatConfig normalized = (config ?? RimTalkChannelCompatConfig.CreateDefault()).Clone();
            normalized.NormalizeWith(RimTalkChannelCompatConfig.CreateDefault());
            if (string.IsNullOrWhiteSpace(normalized.CompatTemplate))
            {
                normalized.CompatTemplate = ComposeTemplateFromEntries(normalized.PromptEntries);
            }

            normalized.CompatTemplate = string.IsNullOrWhiteSpace(normalized.CompatTemplate)
                ? RimChatSettings.DefaultRimTalkCompatTemplate
                : normalized.CompatTemplate.Trim();
            PromptTemplateAutoRewriter.RewriteRimTalkChannelConfig(
                normalized,
                channel,
                ScribanPromptEngine.Instance,
                string.IsNullOrWhiteSpace(idPrefix) ? "legacy" : idPrefix);
            return normalized;
        }

        public static RimTalkChannelCompatConfig BuildFromLegacyFields(
            bool enablePromptCompat,
            int presetInjectionMaxEntries,
            int presetInjectionMaxChars,
            string compatTemplate,
            RimTalkChannelCompatConfig fallback,
            string channel,
            string idPrefix)
        {
            RimTalkChannelCompatConfig config = fallback?.Clone() ?? RimTalkChannelCompatConfig.CreateDefault();
            config.EnablePromptCompat = enablePromptCompat;
            config.PresetInjectionMaxEntries = presetInjectionMaxEntries;
            config.PresetInjectionMaxChars = presetInjectionMaxChars;
            if (!string.IsNullOrWhiteSpace(compatTemplate))
            {
                config.CompatTemplate = compatTemplate.Trim();
            }

            return NormalizeChannelConfig(config, channel, idPrefix);
        }

        public static RimTalkPromptEntryDefaultsConfig NormalizePromptSections(RimTalkPromptEntryDefaultsConfig sections)
        {
            RimTalkPromptEntryDefaultsConfig normalized = sections?.Clone() ?? RimTalkPromptEntryDefaultsProvider.GetDefaultsSnapshot();
            normalized.NormalizeWith(RimTalkPromptEntryDefaultsProvider.GetDefaultsSnapshot());
            return normalized;
        }

        public static bool HasMeaningfulLegacyChannelConfig(RimTalkChannelCompatConfig config)
        {
            if (config == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(config.CompatTemplate) &&
                !string.Equals(config.CompatTemplate.Trim(), RimChatSettings.DefaultRimTalkCompatTemplate.Trim(), StringComparison.Ordinal))
            {
                return true;
            }

            return config.PromptEntries != null &&
                   config.PromptEntries.Any(entry => entry != null && !string.IsNullOrWhiteSpace(entry.Content));
        }

        public static RimTalkChannelCompatConfig CreateLegacyAdapterFromPromptSections(
            RimTalkPromptEntryDefaultsConfig sections,
            RimTalkPromptChannel rootChannel)
        {
            RimTalkPromptEntryDefaultsConfig normalizedSections = NormalizePromptSections(sections);
            var config = new RimTalkChannelCompatConfig
            {
                EnablePromptCompat = false,
                PresetInjectionMaxEntries = RimChatSettings.RimTalkPresetInjectionLimitUnlimited,
                PresetInjectionMaxChars = RimChatSettings.RimTalkPresetInjectionLimitUnlimited,
                CompatTemplate = string.Empty,
                PromptEntries = new List<RimTalkPromptEntryConfig>()
            };

            IReadOnlyList<string> channels = RimTalkPromptEntryChannelCatalog.GetSelectableChannels(rootChannel);
            for (int i = 0; i < channels.Count; i++)
            {
                AppendChannelSections(config.PromptEntries, normalizedSections, channels[i]);
            }

            string merged = ComposeTemplateFromEntries(config.PromptEntries);
            config.CompatTemplate = string.IsNullOrWhiteSpace(merged)
                ? RimChatSettings.DefaultRimTalkCompatTemplate
                : merged;
            return config;
        }

        public static RimTalkPromptEntryDefaultsConfig ApplyLegacyAdapterToPromptSections(
            RimTalkPromptEntryDefaultsConfig currentSections,
            RimTalkChannelCompatConfig config,
            RimTalkPromptChannel rootChannel,
            string sourceId,
            LegacyPromptMigrationReport report = null)
        {
            RimTalkPromptEntryDefaultsConfig normalizedSections = NormalizePromptSections(currentSections);
            ImportLegacyChannelConfig(normalizedSections, config, rootChannel, sourceId, report);
            normalizedSections.NormalizeWith(RimTalkPromptEntryDefaultsProvider.GetDefaultsSnapshot());
            return normalizedSections;
        }

        public static void ResetLegacyFields(RpgPromptCustomConfig config)
        {
            if (config == null)
            {
                return;
            }
            config.RimTalkPersonaCopyTemplate = string.IsNullOrWhiteSpace(config.RimTalkPersonaCopyTemplate)
                ? RimChatSettings.DefaultRimTalkPersonaCopyTemplate
                : config.RimTalkPersonaCopyTemplate;
        }

        public static void ResetLegacyFields(RimChatSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.ResetLegacyCompatLoadPayload();
        }

        private static void ImportLegacyChannelConfig(
            RimTalkPromptEntryDefaultsConfig target,
            RimTalkChannelCompatConfig config,
            RimTalkPromptChannel rootChannel,
            string sourceId,
            LegacyPromptMigrationReport report)
        {
            if (target == null || !HasMeaningfulLegacyChannelConfig(config))
            {
                return;
            }

            List<RimTalkPromptEntryConfig> entries = ExtractLegacyEntries(config, rootChannel);
            if (entries.Count == 0)
            {
                Log.Warning($"[RimChat] Legacy prompt migration skipped for {sourceId}: no usable section entries were found.");
                return;
            }

            int migrated = 0;
            int rejected = 0;
            foreach (IGrouping<string, RimTalkPromptEntryConfig> group in entries.GroupBy(entry =>
                         RimTalkPromptEntryChannelCatalog.NormalizeLoose(entry?.PromptChannel)))
            {
                List<RimTalkPromptEntryConfig> scoped = group.Where(entry => entry != null).ToList();
                for (int i = 0; i < scoped.Count; i++)
                {
                    RimTalkPromptEntryConfig entry = scoped[i];
                    string sectionId = ResolveSectionId(entry, i);
                    if (string.IsNullOrWhiteSpace(sectionId))
                    {
                        rejected++;
                        RecordRejected(
                            report,
                            sourceId,
                            group.Key,
                            string.Empty,
                            $"Legacy entry '{entry?.Name ?? "<unnamed>"}' could not be mapped to a canonical section.",
                            fallbackApplied: false);
                        Log.Warning($"[RimChat] Legacy prompt migration rejected entry without section mapping: source={sourceId}, channel={group.Key}, entry={entry?.Name ?? "<unnamed>"}");
                        continue;
                    }

                    string normalized = entry.Content?.Trim() ?? string.Empty;
                    if (ShouldRejectMigratedContent(normalized))
                    {
                        rejected++;
                        ApplyDefaultSectionContent(target, group.Key, sectionId);
                        RecordRejected(
                            report,
                            sourceId,
                            group.Key,
                            sectionId,
                            "Content looked like a rendered or polluted prompt preview and was reset to the default section.",
                            fallbackApplied: true);
                        Log.Warning($"[RimChat] Legacy prompt migration rejected polluted content: source={sourceId}, channel={group.Key}, section={sectionId}");
                        continue;
                    }

                    if (!PromptTemplateAutoRewriter.TryRewriteLegacyTemplate(
                            $"{sourceId}.{group.Key}.{sectionId}",
                            group.Key,
                            normalized,
                            ScribanPromptEngine.Instance,
                            out string rewritten,
                            out string failureReason))
                    {
                        rejected++;
                        ApplyDefaultSectionContent(target, group.Key, sectionId);
                        RecordRejected(
                            report,
                            sourceId,
                            group.Key,
                            sectionId,
                            $"Template rewrite failed: {failureReason}",
                            fallbackApplied: true);
                        Log.Warning($"[RimChat] Legacy prompt migration rejected invalid template: source={sourceId}, channel={group.Key}, section={sectionId}, reason={failureReason}");
                        continue;
                    }

                    target.SetContent(group.Key, sectionId, rewritten);
                    migrated++;
                    RecordImported(
                        report,
                        sourceId,
                        group.Key,
                        sectionId,
                        !string.Equals(normalized, rewritten, StringComparison.Ordinal));
                }
            }

            if (migrated > 0 || rejected > 0)
            {
                Log.Message($"[RimChat] Legacy prompt migration finished: source={sourceId}, migrated={migrated}, rejected={rejected}.");
            }
        }

        private static void ApplyDefaultSectionContent(
            RimTalkPromptEntryDefaultsConfig target,
            string promptChannel,
            string sectionId)
        {
            if (target == null || string.IsNullOrWhiteSpace(sectionId))
            {
                return;
            }

            string fallback = RimTalkPromptEntryDefaultsProvider.ResolveContent(promptChannel, sectionId);
            if (string.IsNullOrWhiteSpace(fallback))
            {
                fallback = RimTalkPromptEntryDefaultsProvider.ResolveContent(RimTalkPromptEntryChannelCatalog.Any, sectionId);
            }

            if (!string.IsNullOrWhiteSpace(fallback))
            {
                target.SetContent(promptChannel, sectionId, fallback);
            }
        }

        private static string ComposeTemplateFromEntries(IEnumerable<RimTalkPromptEntryConfig> entries)
        {
            if (entries == null)
            {
                return string.Empty;
            }

            IEnumerable<string> enabled = entries
                .Where(entry => entry != null && entry.Enabled && !string.IsNullOrWhiteSpace(entry.Content))
                .Select(entry => entry.Content.Trim());
            string combined = string.Join("\n\n", enabled);
            if (!string.IsNullOrWhiteSpace(combined))
            {
                return combined;
            }

            IEnumerable<string> all = entries
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.Content))
                .Select(entry => entry.Content.Trim());
            return string.Join("\n\n", all).Trim();
        }

        private static void AppendChannelSections(
            ICollection<RimTalkPromptEntryConfig> entries,
            RimTalkPromptEntryDefaultsConfig sections,
            string promptChannel)
        {
            if (entries == null || sections == null)
            {
                return;
            }

            for (int i = 0; i < SectionDefinitions.Length; i++)
            {
                PromptSectionDefinition section = SectionDefinitions[i];
                entries.Add(new RimTalkPromptEntryConfig
                {
                    Id = Guid.NewGuid().ToString("N"),
                    SectionId = section.Id,
                    Name = section.EnglishName,
                    Role = "System",
                    CustomRole = string.Empty,
                    Position = "Relative",
                    InChatDepth = 0,
                    Enabled = true,
                    PromptChannel = promptChannel,
                    Content = sections.ResolveContent(promptChannel, section.Id)
                });
            }
        }

        private static List<RimTalkPromptEntryConfig> ExtractLegacyEntries(
            RimTalkChannelCompatConfig config,
            RimTalkPromptChannel rootChannel)
        {
            var extracted = new List<RimTalkPromptEntryConfig>();
            List<RimTalkPromptEntryConfig> sourceEntries = config?.PromptEntries?
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.Content))
                .Select(entry => entry.Clone())
                .ToList() ?? new List<RimTalkPromptEntryConfig>();
            if (sourceEntries.Count > 0)
            {
                return sourceEntries;
            }

            List<LegacyTemplateSeed> seeds = SplitCompatTemplate(config?.CompatTemplate);
            if (seeds.Count == 0)
            {
                return extracted;
            }

            string fallbackChannel = RimTalkPromptEntryChannelCatalog.GetDefaultChannel(rootChannel);
            for (int i = 0; i < seeds.Count; i++)
            {
                LegacyTemplateSeed seed = seeds[i];
                extracted.Add(new RimTalkPromptEntryConfig
                {
                    Id = Guid.NewGuid().ToString("N"),
                    SectionId = seed.SectionId,
                    Name = seed.Name,
                    Role = "System",
                    CustomRole = string.Empty,
                    Position = "Relative",
                    InChatDepth = 0,
                    Enabled = true,
                    PromptChannel = string.IsNullOrWhiteSpace(seed.PromptChannel) ? fallbackChannel : seed.PromptChannel,
                    Content = seed.Content
                });
            }

            return extracted;
        }

        private static List<LegacyTemplateSeed> SplitCompatTemplate(string compatTemplate)
        {
            var result = new List<LegacyTemplateSeed>();
            string normalized = compatTemplate?.Replace("\r\n", "\n").Replace('\r', '\n').Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized) ||
                string.Equals(normalized, RimChatSettings.DefaultRimTalkCompatTemplate.Trim(), StringComparison.Ordinal))
            {
                return result;
            }

            string[] lines = normalized.Split('\n');
            var buffer = new StringBuilder();
            string currentHeader = string.Empty;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i] ?? string.Empty;
                if (IsSectionHeader(line.Trim()))
                {
                    FlushTemplateSeed(result, currentHeader, buffer);
                    buffer.Clear();
                    currentHeader = line.Trim();
                }

                if (buffer.Length > 0)
                {
                    buffer.Append('\n');
                }

                buffer.Append(line);
            }

            FlushTemplateSeed(result, currentHeader, buffer);
            if (result.Count == 0 && !ShouldRejectMigratedContent(normalized))
            {
                result.Add(new LegacyTemplateSeed
                {
                    Name = "Compat Template",
                    PromptChannel = RimTalkPromptEntryChannelCatalog.Any,
                    Content = normalized
                });
            }

            return result;
        }

        private static void FlushTemplateSeed(
            ICollection<LegacyTemplateSeed> target,
            string header,
            StringBuilder buffer)
        {
            if (target == null || buffer == null)
            {
                return;
            }

            string content = buffer.ToString().Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            target.Add(new LegacyTemplateSeed
            {
                Name = string.IsNullOrWhiteSpace(header) ? "Compat Template" : CleanupHeader(header),
                SectionId = ResolveSectionId(CleanupHeader(header)),
                PromptChannel = RimTalkPromptEntryChannelCatalog.Any,
                Content = content
            });
        }

        private static string ResolveSectionId(RimTalkPromptEntryConfig entry, int index)
        {
            string resolved = ResolveSectionId(entry?.SectionId);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }

            resolved = ResolveSectionId(entry?.Name);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }

            return index >= 0 && index < SectionDefinitions.Length
                ? SectionDefinitions[index].Id
                : string.Empty;
        }

        private static string ResolveSectionId(string candidate)
        {
            string normalized = NormalizeToken(candidate);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            for (int i = 0; i < SectionDefinitions.Length; i++)
            {
                if (SectionDefinitions[i].Matches(normalized))
                {
                    return SectionDefinitions[i].Id;
                }
            }

            return string.Empty;
        }

        private static bool ShouldRejectMigratedContent(string content)
        {
            return LooksLikeRenderedStructuredPrompt(content) || LooksLikeCompiledPromptPreview(content);
        }

        private static bool LooksLikeRenderedStructuredPrompt(string content)
        {
            string value = content?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (value.IndexOf("<prompt_context>", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("</prompt_context>", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("=== PREVIEW DIAGNOSTICS ===", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string[] markers =
            {
                "<channel>",
                "<mode>",
                "<environment>",
                "<instruction_stack>",
                "<response_contract>"
            };
            return CountMarkerHits(value, markers) >= 3 && value.Length >= 300;
        }

        private static bool LooksLikeCompiledPromptPreview(string content)
        {
            string value = content?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (value.IndexOf("========== FULL MESSAGE LOG ==========", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return value.IndexOf("[FILE]", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   value.IndexOf("[CODE]", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   value.IndexOf("{{", StringComparison.OrdinalIgnoreCase) < 0 &&
                   value.Length >= 500;
        }

        private static int CountMarkerHits(string content, IEnumerable<string> markers)
        {
            if (string.IsNullOrWhiteSpace(content) || markers == null)
            {
                return 0;
            }

            int hits = 0;
            foreach (string marker in markers)
            {
                if (!string.IsNullOrWhiteSpace(marker) &&
                    content.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hits++;
                }
            }

            return hits;
        }

        private static bool IsSectionHeader(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            if (line.Length > 2 && line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                return true;
            }

            if (line.Length > 6 && line.StartsWith("===", StringComparison.Ordinal) && line.EndsWith("===", StringComparison.Ordinal))
            {
                return true;
            }

            return line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("### ", StringComparison.Ordinal);
        }

        private static string CleanupHeader(string header)
        {
            if (string.IsNullOrWhiteSpace(header))
            {
                return string.Empty;
            }

            string cleaned = header.Trim().Trim('[', ']').Trim('=').Trim('#').Trim();
            return cleaned.Length > 48 ? cleaned.Substring(0, 48).Trim() : cleaned;
        }

        private static string NormalizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }

            return sb.ToString();
        }

        private sealed class LegacyTemplateSeed
        {
            public string SectionId = string.Empty;
            public string Name = string.Empty;
            public string PromptChannel = string.Empty;
            public string Content = string.Empty;
        }

        private readonly struct PromptSectionDefinition
        {
            public readonly string Id;
            public readonly string EnglishName;
            public readonly string[] Aliases;

            public PromptSectionDefinition(string id, string englishName, params string[] aliases)
            {
                Id = id ?? string.Empty;
                EnglishName = englishName ?? string.Empty;
                Aliases = aliases ?? Array.Empty<string>();
            }

            public bool Matches(string normalizedToken)
            {
                if (string.Equals(NormalizeToken(Id), normalizedToken, StringComparison.Ordinal) ||
                    string.Equals(NormalizeToken(EnglishName), normalizedToken, StringComparison.Ordinal))
                {
                    return true;
                }

                for (int i = 0; i < Aliases.Length; i++)
                {
                    if (string.Equals(NormalizeToken(Aliases[i]), normalizedToken, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
