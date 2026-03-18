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
        public const int CurrentSchemaVersion = 3;

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

            if (!PromptUnifiedNodeSchemaCatalog.IsNodeAllowedForChannel(channel, normalizedNode))
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

        public PromptUnifiedNodeLayoutConfig ResolveNodeLayout(string promptChannel, string nodeId)
        {
            string channel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            string normalizedNode = PromptUnifiedNodeSchemaCatalog.NormalizeId(nodeId);
            if (string.IsNullOrWhiteSpace(normalizedNode))
            {
                return PromptUnifiedNodeLayoutConfig.Create(normalizedNode, PromptUnifiedNodeSlot.MainChainAfter, int.MaxValue, true);
            }

            if (!PromptUnifiedNodeSchemaCatalog.IsNodeAllowedForChannel(channel, normalizedNode))
            {
                return PromptUnifiedNodeLayoutConfig.Create(normalizedNode, PromptUnifiedNodeSlot.MainChainAfter, int.MaxValue, false);
            }

            PromptUnifiedNodeLayoutConfig layout = ResolveChannel(channel)?.ResolveNodeLayout(normalizedNode);
            if (layout != null)
            {
                return layout;
            }

            layout = ResolveChannel(RimTalkPromptEntryChannelCatalog.Any)?.ResolveNodeLayout(normalizedNode);
            if (layout != null)
            {
                return layout;
            }

            return PromptUnifiedNodeLayoutDefaults.BuildDefaultLayout(channel, normalizedNode);
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

        public void SetNodeLayout(string promptChannel, string nodeId, PromptUnifiedNodeSlot slot, int order, bool enabled)
        {
            string channel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            string normalizedNode = PromptUnifiedNodeSchemaCatalog.NormalizeId(nodeId);
            if (string.IsNullOrWhiteSpace(normalizedNode))
            {
                return;
            }

            GetOrCreateChannel(channel).SetNodeLayout(normalizedNode, slot, order, enabled);
        }

        public List<PromptUnifiedNodeLayoutConfig> GetOrderedNodeLayouts(string promptChannel)
        {
            string channel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            return GetOrCreateChannel(channel).GetOrderedNodeLayouts(channel);
        }

        public List<PromptUnifiedTemplateAliasConfig> GetTemplateAliases(string promptChannel)
        {
            string channel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            return GetOrCreateChannel(channel)
                .GetTemplateAliases()
                .Select(item => item.Clone())
                .ToList();
        }

        public PromptUnifiedTemplateAliasConfig ResolveTemplateAlias(string promptChannel, string templateId)
        {
            string channel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            PromptUnifiedTemplateAliasConfig alias = ResolveChannel(channel)?.ResolveTemplateAlias(templateId);
            if (alias != null)
            {
                return alias.Clone();
            }

            return ResolveChannel(RimTalkPromptEntryChannelCatalog.Any)?.ResolveTemplateAlias(templateId)?.Clone();
        }

        public PromptUnifiedTemplateAliasConfig ResolvePreferredTemplateAlias(
            string promptChannel,
            string preferredTemplateId)
        {
            string channel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            PromptUnifiedTemplateAliasConfig alias = ResolveChannel(channel)
                ?.ResolvePreferredTemplateAlias(preferredTemplateId);
            if (alias != null)
            {
                return alias.Clone();
            }

            return ResolveChannel(RimTalkPromptEntryChannelCatalog.Any)
                ?.ResolvePreferredTemplateAlias(preferredTemplateId)
                ?.Clone();
        }

        public void SetTemplateAlias(
            string promptChannel,
            string templateId,
            string name,
            string description,
            string content,
            bool enabled)
        {
            string channel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            GetOrCreateChannel(channel).SetTemplateAlias(
                templateId,
                name,
                description,
                content,
                enabled);
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
        public List<PromptUnifiedNodeLayoutConfig> NodeLayout = new List<PromptUnifiedNodeLayoutConfig>();
        public List<PromptUnifiedTemplateAliasConfig> TemplateAliases = new List<PromptUnifiedTemplateAliasConfig>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref PromptChannel, "promptChannel", RimTalkPromptEntryChannelCatalog.Any);
            Scribe_Collections.Look(ref Sections, "sections", LookMode.Deep);
            Scribe_Collections.Look(ref Nodes, "nodes", LookMode.Deep);
            Scribe_Collections.Look(ref NodeLayout, "nodeLayout", LookMode.Deep);
            Scribe_Collections.Look(ref TemplateAliases, "templateAliases", LookMode.Deep);
            PromptChannel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(PromptChannel);
            Sections ??= new List<PromptUnifiedSectionContent>();
            Nodes ??= new List<PromptUnifiedNodeContent>();
            NodeLayout ??= new List<PromptUnifiedNodeLayoutConfig>();
            TemplateAliases ??= new List<PromptUnifiedTemplateAliasConfig>();
        }

        public PromptUnifiedChannelConfig Clone()
        {
            return new PromptUnifiedChannelConfig
            {
                PromptChannel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(PromptChannel),
                Sections = Sections?.Where(s => s != null).Select(s => s.Clone()).ToList() ?? new List<PromptUnifiedSectionContent>(),
                Nodes = Nodes?.Where(n => n != null).Select(n => n.Clone()).ToList() ?? new List<PromptUnifiedNodeContent>(),
                NodeLayout = NodeLayout?.Where(n => n != null).Select(n => n.Clone()).ToList() ?? new List<PromptUnifiedNodeLayoutConfig>(),
                TemplateAliases = TemplateAliases?.Where(a => a != null).Select(a => a.Clone()).ToList() ?? new List<PromptUnifiedTemplateAliasConfig>()
            };
        }

        public void Normalize()
        {
            PromptChannel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(PromptChannel);
            Sections = NormalizeSections(Sections);
            Nodes = NormalizeNodes(PromptChannel, Nodes);
            NodeLayout = NormalizeNodeLayout(PromptChannel, NodeLayout);
            TemplateAliases = NormalizeTemplateAliases(TemplateAliases);
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

        public PromptUnifiedNodeLayoutConfig ResolveNodeLayout(string nodeId)
        {
            string normalized = PromptUnifiedNodeSchemaCatalog.NormalizeId(nodeId);
            return NodeLayout?.FirstOrDefault(n =>
                n != null && string.Equals(n.NodeId, normalized, StringComparison.OrdinalIgnoreCase))?.Clone();
        }

        public List<PromptUnifiedTemplateAliasConfig> GetTemplateAliases()
        {
            Normalize();
            return TemplateAliases ?? new List<PromptUnifiedTemplateAliasConfig>();
        }

        public PromptUnifiedTemplateAliasConfig ResolveTemplateAlias(string templateId)
        {
            string normalized = PromptUnifiedTemplateAliasConfig.NormalizeTemplateId(templateId);
            if (normalized.Length == 0)
            {
                return null;
            }

            return TemplateAliases?.FirstOrDefault(alias =>
                alias != null &&
                string.Equals(alias.TemplateId, normalized, StringComparison.OrdinalIgnoreCase));
        }

        public PromptUnifiedTemplateAliasConfig ResolvePreferredTemplateAlias(string preferredTemplateId)
        {
            string preferred = PromptUnifiedTemplateAliasConfig.NormalizeTemplateId(preferredTemplateId);
            if (preferred.Length > 0)
            {
                PromptUnifiedTemplateAliasConfig preferredAlias = ResolveTemplateAlias(preferred);
                if (preferredAlias != null && preferredAlias.Enabled)
                {
                    return preferredAlias;
                }
            }

            PromptUnifiedTemplateAliasConfig firstEnabled = TemplateAliases?.FirstOrDefault(alias =>
                alias != null &&
                alias.Enabled &&
                !string.IsNullOrWhiteSpace(alias.TemplateId));
            if (firstEnabled != null)
            {
                return firstEnabled;
            }

            return TemplateAliases?.FirstOrDefault(alias =>
                alias != null &&
                !string.IsNullOrWhiteSpace(alias.TemplateId));
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

        public void SetNodeLayout(string nodeId, PromptUnifiedNodeSlot slot, int order, bool enabled)
        {
            string normalized = PromptUnifiedNodeSchemaCatalog.NormalizeId(nodeId);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            NodeLayout ??= new List<PromptUnifiedNodeLayoutConfig>();
            PromptUnifiedNodeLayoutConfig existing = NodeLayout.FirstOrDefault(n =>
                n != null && string.Equals(n.NodeId, normalized, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                NodeLayout.Add(PromptUnifiedNodeLayoutConfig.Create(normalized, slot, order, enabled));
                return;
            }

            existing.Slot = slot.ToSerializedValue();
            existing.Order = order;
            existing.Enabled = enabled;
        }

        public void SetTemplateAlias(
            string templateId,
            string name,
            string description,
            string content,
            bool enabled)
        {
            string normalizedId = PromptUnifiedTemplateAliasConfig.NormalizeTemplateId(templateId);
            if (normalizedId.Length == 0)
            {
                return;
            }

            TemplateAliases ??= new List<PromptUnifiedTemplateAliasConfig>();
            PromptUnifiedTemplateAliasConfig existing = TemplateAliases.FirstOrDefault(alias =>
                alias != null &&
                string.Equals(alias.TemplateId, normalizedId, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                TemplateAliases.Add(PromptUnifiedTemplateAliasConfig.Create(
                    normalizedId,
                    name,
                    description,
                    content,
                    enabled));
                return;
            }

            existing.Name = name?.Trim() ?? string.Empty;
            existing.Description = description?.Trim() ?? string.Empty;
            existing.Content = content?.Trim() ?? string.Empty;
            existing.Enabled = enabled;
        }

        public List<PromptUnifiedNodeLayoutConfig> GetOrderedNodeLayouts(string promptChannel)
        {
            Normalize();
            return NodeLayout
                .Where(item => item != null)
                .Select(item => item.Clone())
                .OrderBy(item => item.GetSlot())
                .ThenBy(item => item.Order)
                .ThenBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase)
                .ToList();
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

            foreach (PromptUnifiedNodeLayoutConfig layout in source.NodeLayout ?? new List<PromptUnifiedNodeLayoutConfig>())
            {
                if (layout == null)
                {
                    continue;
                }

                SetNodeLayout(layout.NodeId, layout.GetSlot(), layout.Order, layout.Enabled);
            }

            foreach (PromptUnifiedTemplateAliasConfig alias in source.TemplateAliases ?? new List<PromptUnifiedTemplateAliasConfig>())
            {
                if (alias == null)
                {
                    continue;
                }

                SetTemplateAlias(alias.TemplateId, alias.Name, alias.Description, alias.Content, alias.Enabled);
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

        private static List<PromptUnifiedNodeContent> NormalizeNodes(
            string promptChannel,
            List<PromptUnifiedNodeContent> source)
        {
            var allowedNodes = new HashSet<string>(
                PromptUnifiedNodeSchemaCatalog.GetAllowedNodes(promptChannel).Select(item => item.Id),
                StringComparer.OrdinalIgnoreCase);
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

                if (!allowedNodes.Contains(id))
                {
                    Log.Error($"[RimChat] Prompt node '{id}' is not allowed for channel '{promptChannel}'. Entry removed.");
                    continue;
                }

                merged[id] = content;
            }

            return merged.Select(i => PromptUnifiedNodeContent.Create(i.Key, i.Value)).ToList();
        }

        private static List<PromptUnifiedNodeLayoutConfig> NormalizeNodeLayout(
            string promptChannel,
            List<PromptUnifiedNodeLayoutConfig> source)
        {
            var allowedNodes = new HashSet<string>(
                PromptUnifiedNodeSchemaCatalog.GetAllowedNodes(promptChannel).Select(item => item.Id),
                StringComparer.OrdinalIgnoreCase);
            if (allowedNodes.Count == 0)
            {
                return new List<PromptUnifiedNodeLayoutConfig>();
            }

            var merged = new Dictionary<string, PromptUnifiedNodeLayoutConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (PromptUnifiedNodeLayoutConfig layout in source ?? new List<PromptUnifiedNodeLayoutConfig>())
            {
                if (layout == null)
                {
                    continue;
                }

                string id = PromptUnifiedNodeSchemaCatalog.NormalizeId(layout.NodeId);
                if (id.Length == 0)
                {
                    continue;
                }

                if (!allowedNodes.Contains(id))
                {
                    Log.Error($"[RimChat] Prompt node layout '{id}' is not allowed for channel '{promptChannel}'. Layout removed.");
                    continue;
                }

                merged[id] = PromptUnifiedNodeLayoutConfig.Create(id, layout.GetSlot(), layout.Order, layout.Enabled);
            }

            foreach (string nodeId in allowedNodes)
            {
                if (merged.ContainsKey(nodeId))
                {
                    continue;
                }

                PromptUnifiedNodeLayoutConfig fallback = PromptUnifiedNodeLayoutDefaults.BuildDefaultLayout(promptChannel, nodeId);
                merged[nodeId] = fallback;
            }

            return merged.Values
                .OrderBy(item => item.GetSlot())
                .ThenBy(item => item.Order)
                .ThenBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<PromptUnifiedTemplateAliasConfig> NormalizeTemplateAliases(
            List<PromptUnifiedTemplateAliasConfig> source)
        {
            var merged = new Dictionary<string, PromptUnifiedTemplateAliasConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (PromptUnifiedTemplateAliasConfig alias in source ?? new List<PromptUnifiedTemplateAliasConfig>())
            {
                if (alias == null)
                {
                    continue;
                }

                string id = PromptUnifiedTemplateAliasConfig.NormalizeTemplateId(alias.TemplateId);
                if (id.Length == 0)
                {
                    continue;
                }

                merged[id] = PromptUnifiedTemplateAliasConfig.Create(
                    id,
                    alias.Name,
                    alias.Description,
                    alias.Content,
                    alias.Enabled);
            }

            return merged.Values
                .OrderBy(item => item.TemplateId, StringComparer.OrdinalIgnoreCase)
                .ToList();
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

    [Serializable]
    internal sealed class PromptUnifiedTemplateAliasConfig : IExposable
    {
        public string TemplateId = string.Empty;
        public string Name = string.Empty;
        public string Description = string.Empty;
        public string Content = string.Empty;
        public bool Enabled = true;

        public void ExposeData()
        {
            Scribe_Values.Look(ref TemplateId, "templateId", string.Empty);
            Scribe_Values.Look(ref Name, "name", string.Empty);
            Scribe_Values.Look(ref Description, "description", string.Empty);
            Scribe_Values.Look(ref Content, "content", string.Empty);
            Scribe_Values.Look(ref Enabled, "enabled", true);
            TemplateId = NormalizeTemplateId(TemplateId);
            Name = Name?.Trim() ?? string.Empty;
            Description = Description?.Trim() ?? string.Empty;
            Content = Content?.Trim() ?? string.Empty;
        }

        public PromptUnifiedTemplateAliasConfig Clone()
        {
            return Create(TemplateId, Name, Description, Content, Enabled);
        }

        public static PromptUnifiedTemplateAliasConfig Create(
            string templateId,
            string name,
            string description,
            string content,
            bool enabled)
        {
            return new PromptUnifiedTemplateAliasConfig
            {
                TemplateId = NormalizeTemplateId(templateId),
                Name = name?.Trim() ?? string.Empty,
                Description = description?.Trim() ?? string.Empty,
                Content = content?.Trim() ?? string.Empty,
                Enabled = enabled
            };
        }

        public static string NormalizeTemplateId(string templateId)
        {
            return string.IsNullOrWhiteSpace(templateId)
                ? string.Empty
                : templateId.Trim().ToLowerInvariant();
        }
    }

    internal enum PromptUnifiedNodeSlot
    {
        MetadataAfter = 0,
        MainChainBefore = 1,
        MainChainAfter = 2,
        DynamicDataAfter = 3,
        ContractBeforeEnd = 4
    }

    [Serializable]
    internal sealed class PromptUnifiedNodeLayoutConfig : IExposable
    {
        public string NodeId = string.Empty;
        public string Slot = PromptUnifiedNodeSlot.MainChainAfter.ToSerializedValue();
        public int Order = int.MaxValue;
        public bool Enabled = true;

        public void ExposeData()
        {
            Scribe_Values.Look(ref NodeId, "nodeId", string.Empty);
            Scribe_Values.Look(ref Slot, "slot", PromptUnifiedNodeSlot.MainChainAfter.ToSerializedValue());
            Scribe_Values.Look(ref Order, "order", int.MaxValue);
            Scribe_Values.Look(ref Enabled, "enabled", true);
            NodeId = PromptUnifiedNodeSchemaCatalog.NormalizeId(NodeId);
            Slot = PromptUnifiedNodeSlotExtensions.NormalizeSerializedValue(Slot);
            if (Order < 0)
            {
                Order = 0;
            }
        }

        public PromptUnifiedNodeSlot GetSlot()
        {
            return Slot.ToPromptUnifiedNodeSlot();
        }

        public PromptUnifiedNodeLayoutConfig Clone()
        {
            return Create(NodeId, GetSlot(), Order, Enabled);
        }

        public static PromptUnifiedNodeLayoutConfig Create(string nodeId, PromptUnifiedNodeSlot slot, int order, bool enabled)
        {
            return new PromptUnifiedNodeLayoutConfig
            {
                NodeId = PromptUnifiedNodeSchemaCatalog.NormalizeId(nodeId),
                Slot = slot.ToSerializedValue(),
                Order = Math.Max(0, order),
                Enabled = enabled
            };
        }
    }

    internal static class PromptUnifiedNodeLayoutDefaults
    {
        internal static PromptUnifiedNodeLayoutConfig BuildDefaultLayout(string promptChannel, string nodeId)
        {
            string channel = RimTalkPromptEntryChannelCatalog.NormalizeLoose(promptChannel);
            string id = PromptUnifiedNodeSchemaCatalog.NormalizeId(nodeId);
            return PromptUnifiedNodeLayoutConfig.Create(
                id,
                ResolveDefaultSlot(channel, id),
                ResolveDefaultOrder(channel, id),
                true);
        }

        private static PromptUnifiedNodeSlot ResolveDefaultSlot(string promptChannel, string nodeId)
        {
            switch (nodeId)
            {
                case "fact_grounding":
                case "output_language":
                case "decision_policy":
                case "turn_objective":
                case "topic_shift_rule":
                    return PromptUnifiedNodeSlot.MetadataAfter;
                case "api_limits_node_template":
                case "quest_guidance_node_template":
                case "response_contract_node_template":
                case "strategy_output_contract":
                case "strategy_player_negotiator_context_template":
                case "strategy_fact_pack_template":
                case "strategy_scenario_dossier_template":
                    return PromptUnifiedNodeSlot.ContractBeforeEnd;
                case "diplomacy_fallback_role":
                case "social_circle_action_rule":
                case "opening_objective":
                case "rpg_role_setting_fallback":
                case "rpg_relationship_profile":
                case "rpg_kinship_boundary":
                case "social_news_style":
                case "social_news_json_contract":
                case "social_news_fact":
                    return PromptUnifiedNodeSlot.MainChainAfter;
                default:
                    return promptChannel == RimTalkPromptEntryChannelCatalog.Any
                        ? PromptUnifiedNodeSlot.MainChainAfter
                        : PromptUnifiedNodeSlot.MainChainAfter;
            }
        }

        private static int ResolveDefaultOrder(string promptChannel, string nodeId)
        {
            switch (nodeId)
            {
                case "fact_grounding": return 10;
                case "output_language": return 20;
                case "decision_policy": return 30;
                case "turn_objective": return 40;
                case "topic_shift_rule": return 50;
                case "opening_objective": return 60;
                case "diplomacy_fallback_role": return 110;
                case "social_circle_action_rule": return 120;
                case "rpg_role_setting_fallback": return 130;
                case "rpg_relationship_profile": return 140;
                case "rpg_kinship_boundary": return 150;
                case "social_news_style": return 160;
                case "social_news_json_contract": return 170;
                case "social_news_fact": return 180;
                case "api_limits_node_template": return 210;
                case "quest_guidance_node_template": return 220;
                case "response_contract_node_template": return 230;
                case "strategy_output_contract": return 240;
                case "strategy_player_negotiator_context_template": return 250;
                case "strategy_fact_pack_template": return 260;
                case "strategy_scenario_dossier_template": return 270;
                default: return 1000;
            }
        }
    }

    internal static class PromptUnifiedNodeSlotExtensions
    {
        internal static PromptUnifiedNodeSlot ToPromptUnifiedNodeSlot(this string serializedValue)
        {
            string normalized = string.IsNullOrWhiteSpace(serializedValue)
                ? string.Empty
                : serializedValue.Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "metadata_after":
                    return PromptUnifiedNodeSlot.MetadataAfter;
                case "main_chain_before":
                    return PromptUnifiedNodeSlot.MainChainBefore;
                case "main_chain_after":
                    return PromptUnifiedNodeSlot.MainChainAfter;
                case "dynamic_data_after":
                    return PromptUnifiedNodeSlot.DynamicDataAfter;
                case "contract_before_end":
                    return PromptUnifiedNodeSlot.ContractBeforeEnd;
                default:
                    return PromptUnifiedNodeSlot.MainChainAfter;
            }
        }

        internal static string ToSerializedValue(this PromptUnifiedNodeSlot slot)
        {
            switch (slot)
            {
                case PromptUnifiedNodeSlot.MetadataAfter:
                    return "metadata_after";
                case PromptUnifiedNodeSlot.MainChainBefore:
                    return "main_chain_before";
                case PromptUnifiedNodeSlot.MainChainAfter:
                    return "main_chain_after";
                case PromptUnifiedNodeSlot.DynamicDataAfter:
                    return "dynamic_data_after";
                case PromptUnifiedNodeSlot.ContractBeforeEnd:
                    return "contract_before_end";
                default:
                    return "main_chain_after";
            }
        }

        internal static string NormalizeSerializedValue(string serializedValue)
        {
            return ToPromptUnifiedNodeSlot(serializedValue).ToSerializedValue();
        }
    }
}
