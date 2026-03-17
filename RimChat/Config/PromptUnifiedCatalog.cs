using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: Verse Scribe and prompt section/node schema catalogs.
    /// Responsibility: single prompt source of truth for channel sections and non-section nodes.
    /// </summary>
    [Serializable]
    internal sealed class PromptUnifiedCatalog : IExposable
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion = CurrentSchemaVersion;
        public int MigrationVersion = 1;
        public bool LegacyMigrated;
        public List<PromptUnifiedChannelConfig> Channels = new List<PromptUnifiedChannelConfig>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref SchemaVersion, "schemaVersion", CurrentSchemaVersion);
            Scribe_Values.Look(ref MigrationVersion, "migrationVersion", 1);
            Scribe_Values.Look(ref LegacyMigrated, "legacyMigrated", false);
            Scribe_Collections.Look(ref Channels, "channels", LookMode.Deep);
            Channels ??= new List<PromptUnifiedChannelConfig>();
        }

        public PromptUnifiedCatalog Clone()
        {
            return new PromptUnifiedCatalog
            {
                SchemaVersion = SchemaVersion,
                MigrationVersion = MigrationVersion,
                LegacyMigrated = LegacyMigrated,
                Channels = Channels?
                    .Where(c => c != null)
                    .Select(c => c.Clone())
                    .ToList() ?? new List<PromptUnifiedChannelConfig>()
            };
        }

        public void NormalizeWith(PromptUnifiedCatalog fallback)
        {
            fallback ??= CreateFallback();
            Channels ??= new List<PromptUnifiedChannelConfig>();
            var merged = new Dictionary<string, PromptUnifiedChannelConfig>(StringComparer.OrdinalIgnoreCase);
            MergeChannels(merged, fallback.Channels);
            MergeChannels(merged, Channels);
            Channels = merged.Values.ToList();
            for (int i = 0; i < Channels.Count; i++)
            {
                Channels[i].Normalize();
            }

            if (SchemaVersion <= 0)
            {
                SchemaVersion = CurrentSchemaVersion;
            }
        }

        public string ResolveSection(string promptChannel, string sectionId)
        {
            string channel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            string section = PromptSectionSchemaCatalog.NormalizeSectionId(sectionId);
            if (string.IsNullOrWhiteSpace(section))
            {
                return string.Empty;
            }

            string text = ResolveChannel(channel)?.ResolveSection(section) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            return ResolveChannel(RimTalkPromptEntryChannelCatalog.Any)?.ResolveSection(section) ?? string.Empty;
        }

        public string ResolveNode(string promptChannel, string nodeId)
        {
            string channel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            string normalizedNode = PromptUnifiedNodeSchemaCatalog.NormalizeId(nodeId);
            if (string.IsNullOrWhiteSpace(normalizedNode))
            {
                return string.Empty;
            }

            string text = ResolveChannel(channel)?.ResolveNode(normalizedNode) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            return ResolveChannel(RimTalkPromptEntryChannelCatalog.Any)?.ResolveNode(normalizedNode) ?? string.Empty;
        }

        public void SetSection(string promptChannel, string sectionId, string content)
        {
            string channel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            string section = PromptSectionSchemaCatalog.NormalizeSectionId(sectionId);
            if (string.IsNullOrWhiteSpace(section))
            {
                return;
            }

            GetOrCreateChannel(channel).SetSection(section, content);
        }

        public void SetNode(string promptChannel, string nodeId, string content)
        {
            string channel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            string normalizedNode = PromptUnifiedNodeSchemaCatalog.NormalizeId(nodeId);
            if (string.IsNullOrWhiteSpace(normalizedNode))
            {
                return;
            }

            GetOrCreateChannel(channel).SetNode(normalizedNode, content);
        }

        public RimTalkPromptEntryDefaultsConfig ToSectionCatalog()
        {
            var sectionConfig = new RimTalkPromptEntryDefaultsConfig
            {
                Channels = new List<RimTalkPromptChannelDefaultsConfig>()
            };

            foreach (PromptUnifiedChannelConfig channel in Channels ?? Enumerable.Empty<PromptUnifiedChannelConfig>())
            {
                if (channel == null)
                {
                    continue;
                }

                var sections = channel.Sections?
                    .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Content))
                    .Select(s => RimTalkPromptSectionDefaultConfig.Create(s.SectionId, s.Content))
                    .ToList() ?? new List<RimTalkPromptSectionDefaultConfig>();
                sectionConfig.Channels.Add(RimTalkPromptChannelDefaultsConfig.Create(channel.PromptChannel, sections));
            }

            sectionConfig.NormalizeWith(RimTalkPromptEntryDefaultsConfig.CreateFallback());
            return sectionConfig;
        }

        public static PromptUnifiedCatalog FromLegacy(
            RimTalkPromptEntryDefaultsConfig sections,
            PromptTemplateTextConfig templates)
        {
            var catalog = CreateFallback();
            catalog.LegacyMigrated = true;
            RimTalkPromptEntryDefaultsConfig normalizedSections = sections?.Clone() ?? RimTalkPromptEntryDefaultsProvider.GetDefaultsSnapshot();
            normalizedSections.NormalizeWith(RimTalkPromptEntryDefaultsProvider.GetDefaultsSnapshot());
            foreach (RimTalkPromptChannelDefaultsConfig channel in normalizedSections.Channels ?? new List<RimTalkPromptChannelDefaultsConfig>())
            {
                if (channel == null)
                {
                    continue;
                }

                foreach (RimTalkPromptSectionDefaultConfig section in channel.Sections ?? new List<RimTalkPromptSectionDefaultConfig>())
                {
                    if (section == null || string.IsNullOrWhiteSpace(section.SectionId))
                    {
                        continue;
                    }

                    catalog.SetSection(channel.PromptChannel, section.SectionId, section.Content);
                }
            }

            if (templates != null)
            {
                ApplyLegacyTemplatesToNodes(catalog, templates);
            }

            return catalog;
        }

        public static PromptUnifiedCatalog CreateFallback()
        {
            var fallback = new PromptUnifiedCatalog();
            foreach (RimTalkPromptChannelDefaultsConfig channel in RimTalkPromptEntryDefaultsProvider.GetDefaultsSnapshot().Channels ?? new List<RimTalkPromptChannelDefaultsConfig>())
            {
                if (channel == null)
                {
                    continue;
                }

                foreach (RimTalkPromptSectionDefaultConfig section in channel.Sections ?? new List<RimTalkPromptSectionDefaultConfig>())
                {
                    if (section == null)
                    {
                        continue;
                    }

                    fallback.SetSection(channel.PromptChannel, section.SectionId, section.Content);
                }
            }

            PromptUnifiedDefaults.ApplyFallbackNodes(fallback);
            return fallback;
        }

        private static void ApplyLegacyTemplatesToNodes(PromptUnifiedCatalog catalog, PromptTemplateTextConfig templates)
        {
            if (catalog == null || templates == null)
            {
                return;
            }

            SetNodeIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.Any, "fact_grounding", templates.FactGroundingTemplate);
            SetNodeIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.Any, "output_language", templates.OutputLanguageTemplate);
            SetNodeIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.Any, "decision_policy", templates.DecisionPolicyTemplate);
            SetNodeIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.Any, "turn_objective", templates.TurnObjectiveTemplate);
            SetNodeIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.Any, "opening_objective", templates.OpeningObjectiveTemplate);
            SetNodeIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.Any, "topic_shift_rule", templates.TopicShiftRuleTemplate);
            SetNodeIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.Any, "diplomacy_fallback_role", templates.DiplomacyFallbackRoleTemplate);
            SetNodeIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.Any, "social_circle_action_rule", templates.SocialCircleActionRuleTemplate);
            SetNodeIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.Any, "api_limits_node_template", templates.ApiLimitsNodeTemplate);
            SetNodeIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.Any, "quest_guidance_node_template", templates.QuestGuidanceNodeTemplate);
            SetNodeIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.Any, "response_contract_node_template", templates.ResponseContractNodeTemplate);
            SetNodeIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.Any, "social_news_style", templates.SocialCircleNewsStyleTemplate);
            SetNodeIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.Any, "social_news_json_contract", templates.SocialCircleNewsJsonContractTemplate);
            SetNodeIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.Any, "social_news_fact", templates.SocialCircleNewsFactTemplate);
            SetNodeIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.Any, "rpg_role_setting_fallback", templates.RpgRoleSettingTemplate);
            SetNodeIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.Any, "rpg_relationship_profile", templates.RpgCompactFormatConstraintTemplate);
            SetNodeIfNotEmpty(catalog, RimTalkPromptEntryChannelCatalog.Any, "rpg_kinship_boundary", templates.RpgActionReliabilityRuleTemplate);
        }

        private static void SetNodeIfNotEmpty(PromptUnifiedCatalog catalog, string channel, string nodeId, string content)
        {
            string text = content?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                return;
            }

            catalog.SetNode(channel, nodeId, text);
        }

        private PromptUnifiedChannelConfig ResolveChannel(string promptChannel)
        {
            return Channels?.FirstOrDefault(c =>
                c != null && string.Equals(c.PromptChannel, promptChannel, StringComparison.OrdinalIgnoreCase));
        }

        private PromptUnifiedChannelConfig GetOrCreateChannel(string promptChannel)
        {
            Channels ??= new List<PromptUnifiedChannelConfig>();
            PromptUnifiedChannelConfig existing = ResolveChannel(promptChannel);
            if (existing != null)
            {
                return existing;
            }

            existing = new PromptUnifiedChannelConfig { PromptChannel = promptChannel };
            Channels.Add(existing);
            return existing;
        }

        private static void MergeChannels(
            IDictionary<string, PromptUnifiedChannelConfig> target,
            IEnumerable<PromptUnifiedChannelConfig> source)
        {
            if (source == null)
            {
                return;
            }

            foreach (PromptUnifiedChannelConfig channel in source)
            {
                if (channel == null)
                {
                    continue;
                }

                string channelId = RimTalkPromptEntryChannelCatalog.NormalizeLoose(channel.PromptChannel);
                if (!target.TryGetValue(channelId, out PromptUnifiedChannelConfig merged))
                {
                    merged = new PromptUnifiedChannelConfig { PromptChannel = channelId };
                    target[channelId] = merged;
                }

                merged.Merge(channel);
            }
        }
    }

    [Serializable]
    internal sealed class PromptUnifiedChannelConfig : IExposable
    {
        public string PromptChannel = RimTalkPromptEntryChannelCatalog.Any;
        public List<PromptUnifiedSectionContent> Sections = new List<PromptUnifiedSectionContent>();
        public List<PromptUnifiedNodeContent> Nodes = new List<PromptUnifiedNodeContent>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref PromptChannel, "promptChannel", RimTalkPromptEntryChannelCatalog.Any);
            Scribe_Collections.Look(ref Sections, "sections", LookMode.Deep);
            Scribe_Collections.Look(ref Nodes, "nodes", LookMode.Deep);
            PromptChannel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(PromptChannel);
            Sections ??= new List<PromptUnifiedSectionContent>();
            Nodes ??= new List<PromptUnifiedNodeContent>();
        }

        public PromptUnifiedChannelConfig Clone()
        {
            return new PromptUnifiedChannelConfig
            {
                PromptChannel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(PromptChannel),
                Sections = Sections?.Where(s => s != null).Select(s => s.Clone()).ToList() ?? new List<PromptUnifiedSectionContent>(),
                Nodes = Nodes?.Where(n => n != null).Select(n => n.Clone()).ToList() ?? new List<PromptUnifiedNodeContent>()
            };
        }

        public void Normalize()
        {
            PromptChannel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(PromptChannel);
            Sections = NormalizeSections(Sections);
            Nodes = NormalizeNodes(Nodes);
        }

        public string ResolveSection(string sectionId)
        {
            string normalized = PromptSectionSchemaCatalog.NormalizeSectionId(sectionId);
            return Sections?.FirstOrDefault(s =>
                s != null && string.Equals(s.SectionId, normalized, StringComparison.OrdinalIgnoreCase))?.Content ?? string.Empty;
        }

        public string ResolveNode(string nodeId)
        {
            string normalized = PromptUnifiedNodeSchemaCatalog.NormalizeId(nodeId);
            return Nodes?.FirstOrDefault(n =>
                n != null && string.Equals(n.NodeId, normalized, StringComparison.OrdinalIgnoreCase))?.Content ?? string.Empty;
        }

        public void SetSection(string sectionId, string content)
        {
            string normalized = PromptSectionSchemaCatalog.NormalizeSectionId(sectionId);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            Sections ??= new List<PromptUnifiedSectionContent>();
            PromptUnifiedSectionContent existing = Sections.FirstOrDefault(s =>
                s != null && string.Equals(s.SectionId, normalized, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                Sections.Add(PromptUnifiedSectionContent.Create(normalized, content));
                return;
            }

            existing.Content = content?.Trim() ?? string.Empty;
        }

        public void SetNode(string nodeId, string content)
        {
            string normalized = PromptUnifiedNodeSchemaCatalog.NormalizeId(nodeId);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            Nodes ??= new List<PromptUnifiedNodeContent>();
            PromptUnifiedNodeContent existing = Nodes.FirstOrDefault(n =>
                n != null && string.Equals(n.NodeId, normalized, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                Nodes.Add(PromptUnifiedNodeContent.Create(normalized, content));
                return;
            }

            existing.Content = content?.Trim() ?? string.Empty;
        }

        public void Merge(PromptUnifiedChannelConfig source)
        {
            if (source == null)
            {
                return;
            }

            foreach (PromptUnifiedSectionContent section in source.Sections ?? new List<PromptUnifiedSectionContent>())
            {
                if (section != null)
                {
                    SetSection(section.SectionId, section.Content);
                }
            }

            foreach (PromptUnifiedNodeContent node in source.Nodes ?? new List<PromptUnifiedNodeContent>())
            {
                if (node != null)
                {
                    SetNode(node.NodeId, node.Content);
                }
            }
        }

        private static List<PromptUnifiedSectionContent> NormalizeSections(List<PromptUnifiedSectionContent> source)
        {
            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (PromptUnifiedSectionContent section in source ?? new List<PromptUnifiedSectionContent>())
            {
                if (section == null)
                {
                    continue;
                }

                string id = PromptSectionSchemaCatalog.NormalizeSectionId(section.SectionId);
                string content = section.Content?.Trim() ?? string.Empty;
                if (id.Length == 0 || content.Length == 0)
                {
                    continue;
                }

                merged[id] = content;
            }

            return merged.Select(i => PromptUnifiedSectionContent.Create(i.Key, i.Value)).ToList();
        }

        private static List<PromptUnifiedNodeContent> NormalizeNodes(List<PromptUnifiedNodeContent> source)
        {
            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (PromptUnifiedNodeContent node in source ?? new List<PromptUnifiedNodeContent>())
            {
                if (node == null)
                {
                    continue;
                }

                string id = PromptUnifiedNodeSchemaCatalog.NormalizeId(node.NodeId);
                string content = node.Content?.Trim() ?? string.Empty;
                if (id.Length == 0 || content.Length == 0)
                {
                    continue;
                }

                merged[id] = content;
            }

            return merged.Select(i => PromptUnifiedNodeContent.Create(i.Key, i.Value)).ToList();
        }
    }

    [Serializable]
    internal sealed class PromptUnifiedSectionContent : IExposable
    {
        public string SectionId = string.Empty;
        public string Content = string.Empty;

        public void ExposeData()
        {
            Scribe_Values.Look(ref SectionId, "sectionId", string.Empty);
            Scribe_Values.Look(ref Content, "content", string.Empty);
            SectionId = PromptSectionSchemaCatalog.NormalizeSectionId(SectionId);
            Content = Content?.Trim() ?? string.Empty;
        }

        public PromptUnifiedSectionContent Clone()
        {
            return Create(SectionId, Content);
        }

        public static PromptUnifiedSectionContent Create(string sectionId, string content)
        {
            return new PromptUnifiedSectionContent
            {
                SectionId = PromptSectionSchemaCatalog.NormalizeSectionId(sectionId),
                Content = content?.Trim() ?? string.Empty
            };
        }
    }

    [Serializable]
    internal sealed class PromptUnifiedNodeContent : IExposable
    {
        public string NodeId = string.Empty;
        public string Content = string.Empty;

        public void ExposeData()
        {
            Scribe_Values.Look(ref NodeId, "nodeId", string.Empty);
            Scribe_Values.Look(ref Content, "content", string.Empty);
            NodeId = PromptUnifiedNodeSchemaCatalog.NormalizeId(NodeId);
            Content = Content?.Trim() ?? string.Empty;
        }

        public PromptUnifiedNodeContent Clone()
        {
            return Create(NodeId, Content);
        }

        public static PromptUnifiedNodeContent Create(string nodeId, string content)
        {
            return new PromptUnifiedNodeContent
            {
                NodeId = PromptUnifiedNodeSchemaCatalog.NormalizeId(nodeId),
                Content = content?.Trim() ?? string.Empty
            };
        }
    }
}
