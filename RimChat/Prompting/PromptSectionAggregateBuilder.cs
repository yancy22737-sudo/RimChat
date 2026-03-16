using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimChat.Config;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: prompt section schema catalog, prompt section defaults, and prompt-channel ids.
    /// Responsibility: build one canonical section aggregate from PromptSectionCatalog for runtime and editor preview.
    /// </summary>
    internal static class PromptSectionAggregateBuilder
    {
        internal static PromptSectionAggregate Build(
            RimTalkPromptEntryDefaultsConfig catalog,
            string promptChannel,
            Func<string, string, string> renderSection)
        {
            string normalizedChannel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            RimTalkPromptEntryDefaultsConfig normalizedCatalog = catalog?.Clone() ?? RimTalkPromptEntryDefaultsProvider.GetDefaultsSnapshot();
            normalizedCatalog.NormalizeWith(RimTalkPromptEntryDefaultsProvider.GetDefaultsSnapshot());

            var aggregate = new PromptSectionAggregate
            {
                PromptChannel = normalizedChannel
            };

            foreach (PromptSectionSchemaItem section in PromptSectionSchemaCatalog.GetMainChainSections())
            {
                string template = ResolveTemplate(normalizedCatalog, normalizedChannel, section.Id);
                string rendered = renderSection == null ? template : renderSection(section.Id, template);
                if (string.IsNullOrWhiteSpace(rendered))
                {
                    continue;
                }

                aggregate.Sections.Add(new PromptSectionAggregateSection
                {
                    SectionId = section.Id,
                    SectionLabel = section.EnglishName,
                    Content = rendered.Trim()
                });
            }

            aggregate.RenderedText = RenderAggregateText(aggregate.Sections);
            return aggregate;
        }

        internal static string ResolveTemplate(
            RimTalkPromptEntryDefaultsConfig catalog,
            string promptChannel,
            string sectionId)
        {
            RimTalkPromptEntryDefaultsConfig normalizedCatalog = catalog?.Clone() ?? RimTalkPromptEntryDefaultsProvider.GetDefaultsSnapshot();
            normalizedCatalog.NormalizeWith(RimTalkPromptEntryDefaultsProvider.GetDefaultsSnapshot());

            string normalizedChannel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            string normalizedSection = PromptSectionSchemaCatalog.NormalizeSectionId(sectionId);
            string template = normalizedCatalog.ResolveContent(normalizedChannel, normalizedSection);
            if (IsSelfAlias(template, normalizedChannel, normalizedSection))
            {
                template = ResolveDefaultTemplate(normalizedChannel, normalizedSection);
            }

            return template?.Trim() ?? string.Empty;
        }

        private static string ResolveDefaultTemplate(string promptChannel, string sectionId)
        {
            string fromChannelDefault = RimTalkPromptEntryDefaultsProvider.ResolveContent(promptChannel, sectionId);
            if (!IsSelfAlias(fromChannelDefault, promptChannel, sectionId))
            {
                return fromChannelDefault?.Trim() ?? string.Empty;
            }

            string fromAny = RimTalkPromptEntryDefaultsProvider.ResolveContent(RimTalkPromptEntryChannelCatalog.Any, sectionId);
            if (!string.IsNullOrWhiteSpace(fromAny))
            {
                return fromAny.Trim();
            }

            RimTalkPromptEntryDefaultsConfig fallback = RimTalkPromptEntryDefaultsConfig.CreateFallback();
            return fallback.ResolveContent(promptChannel, sectionId)?.Trim() ?? string.Empty;
        }

        private static bool IsSelfAlias(string template, string promptChannel, string sectionId)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return false;
            }

            string normalized = template.Trim();
            string directAlias = "{{ dialogue." + promptChannel + "." + sectionId + " }}";
            string compactAlias = "{{dialogue." + promptChannel + "." + sectionId + "}}";
            return string.Equals(normalized, directAlias, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, compactAlias, StringComparison.OrdinalIgnoreCase);
        }

        private static string RenderAggregateText(IEnumerable<PromptSectionAggregateSection> sections)
        {
            var builder = new StringBuilder();
            foreach (PromptSectionAggregateSection section in sections ?? Enumerable.Empty<PromptSectionAggregateSection>())
            {
                if (section == null || string.IsNullOrWhiteSpace(section.Content))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                    builder.AppendLine();
                }

                builder.AppendLine("[SECTION: " + (section.SectionLabel ?? section.SectionId ?? string.Empty) + "]");
                builder.Append(section.Content.Trim());
            }

            return builder.ToString().Trim();
        }
    }

    internal sealed class PromptSectionAggregate
    {
        public string PromptChannel = string.Empty;
        public string RenderedText = string.Empty;
        public List<PromptSectionAggregateSection> Sections = new List<PromptSectionAggregateSection>();
    }

    internal sealed class PromptSectionAggregateSection
    {
        public string SectionId = string.Empty;
        public string SectionLabel = string.Empty;
        public string Content = string.Empty;
    }
}
