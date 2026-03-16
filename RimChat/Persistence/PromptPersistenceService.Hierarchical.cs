using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimChat.Config;
using RimChat.Core;
using RimChat.Memory;
using RimChat.Prompting;
using RimWorld;
using Verse;

namespace RimChat.Persistence
{
    /// <summary>/// Dependencies: SystemPromptConfig, DialogueScenarioContext, PromptHierarchyRenderer.
 /// Responsibility: build diplomacy/RPG prompts with strict Scriban rendering and hierarchical policy pipeline.
 ///</summary>
    public partial class PromptPersistenceService
    {
        private const string CodePromptTag = "[CODE]";
        private const string FilePromptTag = "[FILE]";

        internal string BuildFullSystemPromptHierarchicalCore(
            Faction faction,
            SystemPromptConfig config,
            bool isProactive,
            IEnumerable<string> additionalSceneTags)
        {
            return BuildFullSystemPromptHierarchicalCore(
                faction,
                config,
                isProactive,
                additionalSceneTags,
                null);
        }

        internal string BuildFullSystemPromptHierarchicalCore(
            Faction faction,
            SystemPromptConfig config,
            bool isProactive,
            IEnumerable<string> additionalSceneTags,
            Pawn playerNegotiator)
        {
            return BuildFullSystemPromptHierarchical(faction, config, isProactive, additionalSceneTags, playerNegotiator);
        }

        internal string BuildRpgSystemPromptHierarchicalCore(
            Pawn initiator,
            Pawn target,
            bool isProactive,
            IEnumerable<string> additionalSceneTags)
        {
            return BuildRpgSystemPromptHierarchical(initiator, target, isProactive, additionalSceneTags);
        }

        private string BuildFullSystemPromptHierarchical(
            Faction faction,
            SystemPromptConfig config,
            bool isProactive,
            IEnumerable<string> additionalSceneTags,
            Pawn playerNegotiator)
        {
            if (TryBuildEntryDrivenChannelPrompt(
                    RimTalkPromptChannel.Diplomacy,
                    isProactive,
                    null,
                    null,
                    faction,
                    out string entryDrivenPrompt))
            {
                return entryDrivenPrompt;
            }

            var scenarioContext = DialogueScenarioContext.CreateDiplomacy(faction, isProactive, additionalSceneTags);
            var root = new PromptHierarchyNode("prompt_context");
            AddTextNodeIfNotEmpty(root, "channel", "diplomacy");
            AddTextNodeIfNotEmpty(root, "mode", isProactive ? "proactive" : "manual");
            AddTextNodeIfNotEmpty(root, "environment", BuildEnvironmentPromptBlocks(config, scenarioContext));
            AddTextNodeIfNotEmpty(root, "fact_grounding", BuildFactGroundingGuidanceText(config, scenarioContext));
            AddTextNodeIfNotEmpty(root, "output_language", BuildOutputLanguageGuidance(RimChatMod.Settings, config, scenarioContext));
            AddTextNodeIfNotEmpty(root, "decision_policy", BuildDecisionPolicyText(config, scenarioContext));
            AddTextNodeIfNotEmpty(root, "turn_objective", BuildTurnObjectiveText(
                config,
                scenarioContext,
                "Address the player's latest explicit intent from the current turn first.",
                "After finishing the primary objective, you may add one natural follow-up extension."));
            AddTextNodeIfNotEmpty(root, "topic_shift_rule", BuildTopicShiftRuleText(config, scenarioContext));

            var instruction = root.AddChild("instruction_stack");
            AddTextNodeIfNotEmpty(instruction, "global_system_prompt", config.GlobalSystemPrompt, true);
            AddTextNodeIfNotEmpty(instruction, "global_dialogue_prompt", config.GlobalDialoguePrompt, true);
            AddTextNodeIfNotEmpty(instruction, "faction_characteristics", ResolveFactionPromptText(faction, config, scenarioContext));
            AddTextNodeIfNotEmpty(instruction, "social_circle_action_rule", BuildSocialCircleActionRuleText(config, scenarioContext));

            PromptHierarchyNode dynamicData = BuildDiplomacyDynamicDataNode(config, faction, playerNegotiator);
            if (dynamicData != null)
            {
                root.Children.Add(dynamicData);
            }

            string apiLimitsBody = BuildTextBlock(sb => AppendApiLimits(sb, faction));
            AddTextNodeIfNotEmpty(root, "api_limits",
                RenderPromptNodeTemplate(
                    config,
                    scenarioContext,
                    config?.PromptTemplates?.ApiLimitsNodeTemplate,
                    "api_limits_body",
                    apiLimitsBody));

            string questGuidanceBody = BuildTextBlock(sb =>
            {
                AppendDynamicQuestGuidance(sb, faction);
                AppendQuestSelectionHardRules(sb);
            });
            AddTextNodeIfNotEmpty(root, "quest_guidance",
                RenderPromptNodeTemplate(
                    config,
                    scenarioContext,
                    config?.PromptTemplates?.QuestGuidanceNodeTemplate,
                    "quest_guidance_body",
                    questGuidanceBody));

            string responseContractBody = BuildTextBlock(sb =>
            {
                if (config.UseAdvancedMode)
                {
                    AppendAdvancedConfig(sb, config, faction);
                }
                else
                {
                    AppendSimpleConfig(sb, config, faction);
                }
            });
            AddTextNodeIfNotEmpty(root, "response_contract",
                RenderPromptNodeTemplate(
                    config,
                    scenarioContext,
                    config?.PromptTemplates?.ResponseContractNodeTemplate,
                    "response_contract_body",
                    responseContractBody));
            return PromptHierarchyRenderer.Render(root, config.UseHierarchicalPromptFormat);
        }

        private string BuildRpgSystemPromptHierarchical(
            Pawn initiator,
            Pawn target,
            bool isProactive,
            IEnumerable<string> additionalSceneTags)
        {
            var settings = RimChatMod.Settings;
            settings?.EnsureRpgPromptTextsLoaded();
            SystemPromptConfig config = LoadConfig() ?? CreateDefaultConfig();
            if (TryBuildEntryDrivenChannelPrompt(
                    RimTalkPromptChannel.Rpg,
                    isProactive,
                    initiator,
                    target,
                    target?.Faction,
                    out string entryDrivenPrompt))
            {
                return entryDrivenPrompt;
            }

            var scenarioContext = DialogueScenarioContext.CreateRpg(initiator, target, isProactive, additionalSceneTags);
            bool samePlayerFaction =
                initiator?.Faction != null &&
                initiator.Faction == target?.Faction &&
                initiator.Faction.IsPlayer;
            bool preferCompactContext = !isProactive && samePlayerFaction;
            PromptPolicyConfig promptPolicy = ResolvePromptPolicyConfig(config);
            bool includeOpeningObjective = IsOpeningTurnContext(scenarioContext);
            string unresolvedIntent = RpgNpcDialogueArchiveManager.Instance.BuildUnresolvedIntentSummary(target, initiator);

            var root = new PromptHierarchyNode("prompt_context");
            AddTextNodeIfNotEmpty(root, "channel", "rpg");
            AddTextNodeIfNotEmpty(root, "mode", isProactive ? "proactive" : "manual");
            string environmentBlock = BuildEnvironmentPromptBlocks(config, scenarioContext);
            if (preferCompactContext)
            {
                environmentBlock = CompactRpgEnvironmentBlock(environmentBlock);
            }
            AddTextNodeIfNotEmpty(root, "environment", environmentBlock);
            AddTextNodeIfNotEmpty(root, "fact_grounding", BuildFactGroundingGuidanceText(config, scenarioContext));
            AddTextNodeIfNotEmpty(root, "output_language", BuildOutputLanguageGuidance(settings, config, scenarioContext));
            AddTextNodeIfNotEmpty(root, "decision_policy", BuildDecisionPolicyText(config, scenarioContext));
            AddTextNodeIfNotEmpty(root, "turn_objective", BuildTurnObjectiveText(
                config,
                scenarioContext,
                BuildPrimaryObjectiveFromIntent(unresolvedIntent),
                "After completing the primary objective, optionally add one relevant follow-up."));
            AddTextNodeIfNotEmpty(root, "topic_shift_rule", BuildTopicShiftRuleText(config, scenarioContext));
            if (includeOpeningObjective)
            {
                AddTextNodeIfNotEmpty(root, "opening_objective", BuildOpeningObjectiveText(config, scenarioContext, unresolvedIntent));
            }

            var roleStack = root.AddChild("role_stack");
            AddTextNodeIfNotEmpty(roleStack, "role_setting", BuildRpgRoleSettingText(settings, config, scenarioContext, target));
            AddTextNodeIfNotEmpty(roleStack, "personality_override", ResolveRpgPawnPersonaPrompt(target));
            AddTextNodeIfNotEmpty(roleStack, "dialogue_style", settings?.RPGDialogueStyle, true);
            if (!isProactive)
            {
                AddTextNodeIfNotEmpty(root, "relationship_profile", BuildRpgRelationshipProfileText(settings, initiator, target));
            }

            AddTextNodeIfNotEmpty(root, "dynamic_faction_memory",
                DialogueSummaryService.BuildRpgDynamicFactionMemoryBlock(target?.Faction, target));
            AddTextNodeIfNotEmpty(root, "dynamic_npc_personal_memory",
                RpgNpcDialogueArchiveManager.Instance.BuildPromptMemoryBlock(
                    target,
                    initiator,
                    promptPolicy?.SummaryTimelineTurnLimit ?? 8,
                    promptPolicy?.SummaryCharBudget ?? 1200));

            PromptHierarchyNode actorState = BuildRpgActorStateNode(
                settings,
                config,
                initiator,
                target,
                preferCompactContext);
            if (actorState != null)
            {
                root.Children.Add(actorState);
            }

            bool preferCompactApiContract = preferCompactContext;
            AddTextNodeIfNotEmpty(root, "api_contract", BuildRpgApiContractText(settings, config, scenarioContext, preferCompactApiContract));
            return PromptHierarchyRenderer.Render(root, config.UseHierarchicalPromptFormat);
        }

        private PromptHierarchyNode BuildDiplomacyDynamicDataNode(SystemPromptConfig config, Faction faction, Pawn playerNegotiator)
        {
            if (config?.DynamicDataInjection == null)
            {
                return null;
            }

            var node = new PromptHierarchyNode("dynamic_data");
            DynamicDataInjectionConfig dyn = config.DynamicDataInjection;
            if (dyn.InjectMemoryData)
            {
                AddTextNodeIfNotEmpty(node, "memory_data", BuildTextBlock(sb => AppendMemoryData(sb, faction)));
            }

            if (dyn.InjectFactionInfo)
            {
                AddTextNodeIfNotEmpty(node, "faction_info", BuildTextBlock(sb => AppendFactionInfo(sb, faction)));
                AddTextNodeIfNotEmpty(node, "player_pawn_profile", BuildPlayerPawnContextForPrompt(faction, playerNegotiator));
                AddTextNodeIfNotEmpty(node, "player_royalty_summary", BuildPlayerRoyaltySummaryForPrompt(faction, playerNegotiator));
                AddTextNodeIfNotEmpty(node, "faction_settlement_summary", BuildFactionSettlementSummaryForPrompt(faction));
            }

            return node.Children.Count > 0 ? node : null;
        }

        private PromptHierarchyNode BuildRpgActorStateNode(
            RimChatSettings settings,
            SystemPromptConfig config,
            Pawn initiator,
            Pawn target,
            bool preferCompactContext)
        {
            var node = new PromptHierarchyNode("actor_state");
            bool samePlayerFaction =
                initiator?.Faction != null &&
                initiator.Faction == target?.Faction &&
                initiator.Faction.IsPlayer;

            if (settings?.RPGInjectSelfStatus == true)
            {
                AddTextNodeIfNotEmpty(node, "self_status",
                    BuildTextBlock(sb => AppendRPGPawnInfo(
                        sb,
                        target,
                        true,
                        config?.EnvironmentPrompt?.RpgSceneParamSwitches,
                        includePlayerSharedColonyContext: true,
                        includeStaticProfileDetails: !preferCompactContext)));
            }

            if (settings?.RPGInjectInterlocutorStatus == true)
            {
                AddTextNodeIfNotEmpty(node, "interlocutor_status",
                    BuildTextBlock(sb => AppendRPGPawnInfo(
                        sb,
                        initiator,
                        false,
                        config?.EnvironmentPrompt?.RpgSceneParamSwitches,
                        includePlayerSharedColonyContext: !samePlayerFaction,
                        includeStaticProfileDetails: !samePlayerFaction && !preferCompactContext)));
            }

            if (settings?.RPGInjectFactionBackground == true)
            {
                AddTextNodeIfNotEmpty(node, "target_faction_context", BuildTextBlock(sb => AppendRPGFactionContext(sb, target)));
                if (initiator?.Faction != target?.Faction)
                {
                    AddTextNodeIfNotEmpty(node, "interlocutor_faction_context",
                        BuildTextBlock(sb => AppendRPGFactionContext(sb, initiator)));
                }
            }

            return node.Children.Count > 0 ? node : null;
        }

        private bool TryBuildEntryDrivenChannelPrompt(
            RimTalkPromptChannel channel,
            bool isProactive,
            Pawn initiator,
            Pawn target,
            Faction faction,
            out string promptText)
        {
            promptText = string.Empty;
            RimChatSettings settings = RimChatMod.Settings;
            if (settings == null)
            {
                return false;
            }

            RimTalkChannelCompatConfig channelConfig = settings.GetRimTalkChannelConfigClone(channel);
            channelConfig?.NormalizeWith(RimTalkChannelCompatConfig.CreateDefault());
            if (channelConfig == null || !channelConfig.EnablePromptCompat)
            {
                return false;
            }

            List<RimTalkPromptEntryConfig> activeEntries = CollectActivePromptEntries(
                channelConfig.PromptEntries,
                channel,
                isProactive);
            if (activeEntries.Count == 0)
            {
                return false;
            }

            promptText = RenderPromptEntriesAsText(
                activeEntries,
                initiator,
                target,
                faction,
                channel == RimTalkPromptChannel.Diplomacy ? "diplomacy" : "rpg");
            if (string.IsNullOrWhiteSpace(promptText))
            {
                throw new PromptRenderException(
                    $"channel_entries.{channel.ToString().ToLowerInvariant()}",
                    channel == RimTalkPromptChannel.Diplomacy ? "diplomacy" : "rpg",
                    new PromptRenderDiagnostic
                    {
                        ErrorCode = PromptRenderErrorCode.TemplateMissing,
                        Message = "Enabled channel entries rendered empty output in strict mode."
                    });
            }

            return true;
        }

        private static List<RimTalkPromptEntryConfig> CollectActivePromptEntries(
            IEnumerable<RimTalkPromptEntryConfig> entries,
            RimTalkPromptChannel channel,
            bool isProactive)
        {
            var active = new List<RimTalkPromptEntryConfig>();
            if (entries == null)
            {
                return active;
            }

            foreach (RimTalkPromptEntryConfig entry in entries)
            {
                if (entry == null || !entry.Enabled)
                {
                    continue;
                }

                string content = entry.Content?.Trim();
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                if (!RimTalkPromptEntryChannelCatalog.MatchesRuntimeChannel(
                    entry.PromptChannel,
                    channel,
                    isProactive))
                {
                    continue;
                }

                active.Add(entry);
            }

            return active;
        }

        private static string RenderPromptEntriesAsText(
            IEnumerable<RimTalkPromptEntryConfig> entries,
            Pawn initiator,
            Pawn target,
            Faction faction,
            string channel)
        {
            var blocks = new List<string>();
            if (entries == null)
            {
                throw new PromptRenderException(
                    $"channel_entries.{channel}",
                    channel,
                    new PromptRenderDiagnostic
                    {
                        ErrorCode = PromptRenderErrorCode.TemplateMissing,
                        Message = "Prompt entries list is required in strict mode."
                    });
            }

            DialogueScenarioContext scenarioContext = string.Equals(channel, "rpg", StringComparison.OrdinalIgnoreCase)
                ? DialogueScenarioContext.CreateRpg(initiator, target, false)
                : DialogueScenarioContext.CreateDiplomacy(faction, false);
            int index = 0;
            foreach (RimTalkPromptEntryConfig entry in entries)
            {
                string content = entry?.Content;
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                string templateId = $"channel_entries.{channel}.{(string.IsNullOrWhiteSpace(entry?.Id) ? index.ToString() : entry.Id)}";
                Dictionary<string, object> variables = BuildSharedPromptTemplateVariables(scenarioContext, string.Empty);
                variables["pawn.initiator"] = initiator;
                variables["pawn.target"] = target;
                variables["world.faction"] = faction;
                string rendered = RenderTemplateOrThrow(
                    templateId,
                    channel,
                    content,
                    variables);
                if (string.IsNullOrWhiteSpace(rendered))
                {
                    index++;
                    continue;
                }

                blocks.Add(ApplyPromptSourceTag(rendered.Trim(), true));
                index++;
            }

            return string.Join("\n\n", blocks).Trim();
        }

        private static void AddTextNodeIfNotEmpty(PromptHierarchyNode parent, string id, string text, bool fromFile = false)
        {
            if (parent == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            parent.AddChild(id, ApplyPromptSourceTag(text.Trim(), fromFile));
        }

        private static string ApplyPromptSourceTag(string text, bool fromFile)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            if (text.StartsWith(CodePromptTag, StringComparison.Ordinal)
                || text.StartsWith(FilePromptTag, StringComparison.Ordinal))
            {
                return text;
            }

            return fromFile ? $"{FilePromptTag} {text}" : $"{CodePromptTag} {text}";
        }

        private static string BuildTextBlock(Action<StringBuilder> appendAction)
        {
            if (appendAction == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            appendAction(sb);
            return sb.ToString().Trim();
        }

        private static string RenderTemplateOrThrow(
            string templateId,
            string channel,
            string templateText,
            IReadOnlyDictionary<string, object> variables)
        {
            string requiredTemplate = RequireTemplateText(templateId, channel, templateText);
            PromptRenderContext renderContext = PromptRenderContext.Create(templateId, channel);
            renderContext.SetValues(variables);
            return PromptTemplateRenderer.RenderOrThrow(templateId, channel, requiredTemplate, renderContext);
        }

        private static string RequireTemplateText(
            string templateId,
            string channel,
            string templateText)
        {
            if (!string.IsNullOrWhiteSpace(templateText))
            {
                return templateText;
            }

            throw new PromptRenderException(
                templateId,
                channel,
                new PromptRenderDiagnostic
                {
                    ErrorCode = PromptRenderErrorCode.TemplateMissing,
                    Message = "Template text is required in strict Scriban mode."
                });
        }

        private string BuildDecisionPolicyText(SystemPromptConfig config, DialogueScenarioContext context)
        {
            bool isRpg = context?.IsRpg == true;
            string template = isRpg
                ? RpgPromptDefaultsProvider.GetDefaults().DecisionPolicyTemplate
                : config?.PromptTemplates?.DecisionPolicyTemplate;
            string channel = ResolveRenderChannel(context);
            string requiredTemplate = RequireTemplateText("prompt_templates.decision_policy", channel, template);

            return ApplyPromptSourceTag(
                RenderTemplateOrThrow(
                    "prompt_templates.decision_policy",
                    channel,
                    requiredTemplate,
                    BuildPolicyTemplateVariables(context, string.Empty, string.Empty, string.Empty)),
                true);
        }

        private string BuildTurnObjectiveText(
            SystemPromptConfig config,
            DialogueScenarioContext context,
            string primaryObjective,
            string optionalFollowup)
        {
            string primary = primaryObjective?.Trim() ?? string.Empty;
            string followup = optionalFollowup?.Trim() ?? string.Empty;
            bool isRpg = context?.IsRpg == true;
            string template = isRpg
                ? RpgPromptDefaultsProvider.GetDefaults().TurnObjectiveTemplate
                : config?.PromptTemplates?.TurnObjectiveTemplate;
            string channel = ResolveRenderChannel(context);
            string requiredTemplate = RequireTemplateText("prompt_templates.turn_objective", channel, template);

            return ApplyPromptSourceTag(
                RenderTemplateOrThrow(
                    "prompt_templates.turn_objective",
                    channel,
                    requiredTemplate,
                    BuildPolicyTemplateVariables(context, primary, followup, string.Empty)),
                true);
        }

        private string BuildOpeningObjectiveText(
            SystemPromptConfig config,
            DialogueScenarioContext context,
            string unresolvedIntent)
        {
            string normalizedIntent = unresolvedIntent?.Trim() ?? string.Empty;
            string template = RpgPromptDefaultsProvider.GetDefaults().OpeningObjectiveTemplate;
            string channel = ResolveRenderChannel(context);
            string requiredTemplate = RequireTemplateText("prompt_templates.opening_objective", channel, template);

            return ApplyPromptSourceTag(
                RenderTemplateOrThrow(
                    "prompt_templates.opening_objective",
                    channel,
                    requiredTemplate,
                    BuildPolicyTemplateVariables(context, string.Empty, string.Empty, normalizedIntent)),
                true);
        }

        private string BuildTopicShiftRuleText(SystemPromptConfig config, DialogueScenarioContext context)
        {
            bool isRpg = context?.IsRpg == true;
            string template = isRpg
                ? RpgPromptDefaultsProvider.GetDefaults().TopicShiftRuleTemplate
                : config?.PromptTemplates?.TopicShiftRuleTemplate;
            string channel = ResolveRenderChannel(context);
            string requiredTemplate = RequireTemplateText("prompt_templates.topic_shift_rule", channel, template);

            return ApplyPromptSourceTag(
                RenderTemplateOrThrow(
                    "prompt_templates.topic_shift_rule",
                    channel,
                    requiredTemplate,
                    BuildPolicyTemplateVariables(context, string.Empty, string.Empty, string.Empty)),
                true);
        }

        private static string BuildPrimaryObjectiveFromIntent(string unresolvedIntent)
        {
            if (string.IsNullOrWhiteSpace(unresolvedIntent))
            {
                return string.Empty;
            }

            return $"Acknowledge and address unresolved player intent first: {unresolvedIntent.Trim()}";
        }

        private static bool IsOpeningTurnContext(DialogueScenarioContext context)
        {
            if (context?.IsProactive == true)
            {
                return true;
            }

            if (context?.Tags == null || context.Tags.Count == 0)
            {
                return false;
            }

            return context.Tags.Contains("phase:opening")
                || context.Tags.Contains("turn:opening")
                || context.Tags.Contains("opening");
        }

        private static PromptPolicyConfig ResolvePromptPolicyConfig(SystemPromptConfig config)
        {
            return config?.PromptPolicy?.Clone() ?? PromptPolicyConfig.CreateDefault();
        }

        private static Dictionary<string, object> BuildPolicyTemplateVariables(
            DialogueScenarioContext context,
            string primaryObjective,
            string optionalFollowup,
            string unresolvedIntent)
        {
            Dictionary<string, object> variables = BuildSharedPromptTemplateVariables(context, string.Empty);
            variables["dialogue.primary_objective"] = primaryObjective ?? string.Empty;
            variables["dialogue.optional_followup"] = optionalFollowup ?? string.Empty;
            variables["dialogue.latest_unresolved_intent"] = unresolvedIntent ?? string.Empty;
            variables["dialogue.topic_shift_rule"] = "Complete the primary objective first, then allow at most one natural topic extension.";
            return variables;
        }

        private string BuildFactGroundingGuidanceText(SystemPromptConfig config, DialogueScenarioContext context)
        {
            string template = config?.PromptTemplates?.FactGroundingTemplate;
            string channel = ResolveRenderChannel(context);
            string requiredTemplate = RequireTemplateText("prompt_templates.fact_grounding", channel, template);

            return ApplyPromptSourceTag(
                RenderTemplateOrThrow(
                    "prompt_templates.fact_grounding",
                    channel,
                    requiredTemplate,
                    BuildSharedPromptTemplateVariables(context, string.Empty)),
                true);
        }

        private string ResolveFactionPromptText(
            Faction faction,
            SystemPromptConfig config,
            DialogueScenarioContext context)
        {
            string factionPrompt = FactionPromptManager.Instance.GetPrompt(faction?.def?.defName);
            if (!string.IsNullOrWhiteSpace(factionPrompt))
            {
                return ApplyPromptSourceTag(factionPrompt.Trim(), true);
            }

            string template = config?.PromptTemplates?.DiplomacyFallbackRoleTemplate;
            string channel = ResolveRenderChannel(context);
            string requiredTemplate = RequireTemplateText("prompt_templates.diplomacy_fallback_role", channel, template);
            return ApplyPromptSourceTag(
                RenderTemplateOrThrow(
                    "prompt_templates.diplomacy_fallback_role",
                    channel,
                    requiredTemplate,
                    BuildSharedPromptTemplateVariables(context, string.Empty)),
                true);
        }

        private string BuildSocialCircleActionRuleText(SystemPromptConfig config, DialogueScenarioContext context)
        {
            if (RimChatMod.Settings?.EnableSocialCircle != true)
            {
                return string.Empty;
            }

            string template = config?.PromptTemplates?.SocialCircleActionRuleTemplate;
            string channel = ResolveRenderChannel(context);
            string requiredTemplate = RequireTemplateText("prompt_templates.social_circle_action_rule", channel, template);
            return ApplyPromptSourceTag(
                RenderTemplateOrThrow(
                    "prompt_templates.social_circle_action_rule",
                    channel,
                    requiredTemplate,
                    BuildSharedPromptTemplateVariables(context, string.Empty)),
                true);
        }

        private string BuildRpgRoleSettingText(
            RimChatSettings settings,
            SystemPromptConfig config,
            DialogueScenarioContext context,
            Pawn target)
        {
            RpgPromptCustomConfig promptConfig = RpgPromptCustomStore.LoadOrDefault();
            if (!string.IsNullOrWhiteSpace(promptConfig?.RoleSetting))
            {
                return ApplyPromptSourceTag(promptConfig.RoleSetting.Trim(), true);
            }

            Dictionary<string, object> variables = BuildSharedPromptTemplateVariables(context, string.Empty);
            variables["pawn.target.name"] = target?.LabelShort ?? "Unknown";
            variables["pawn.target"] = target;
            string channel = ResolveRenderChannel(context);
            string roleTemplate = ResolveRpgRoleFallbackTemplate(settings);
            string requiredTemplate = RequireTemplateText("prompt_templates.rpg_role_setting_fallback", channel, roleTemplate);
            string roleText = RenderTemplateOrThrow(
                "prompt_templates.rpg_role_setting_fallback",
                channel,
                requiredTemplate,
                variables);
            return ApplyPromptSourceTag(roleText, true);
        }

        private string BuildRpgRelationshipProfileText(
            RimChatSettings settings,
            Pawn initiator,
            Pawn target)
        {
            if (initiator == null || target == null)
            {
                return string.Empty;
            }

            bool kinship = HasAnyBloodRelationBetweenPair(initiator, target);
            string kinshipValue = kinship ? "yes" : "no";
            string romanceState = ResolvePairRomanceState(initiator, target);
            var variables = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["pawn.initiator.name"] = initiator.LabelShort ?? "Unknown",
                ["pawn.target.name"] = target.LabelShort ?? "Unknown",
                ["pawn.relation.kinship"] = kinshipValue,
                ["pawn.relation.romance_state"] = romanceState,
                ["pawn.initiator"] = initiator,
                ["pawn.target"] = target
            };

            string guidance = RenderTemplateOrThrow(
                "prompt_templates.rpg_kinship_boundary",
                "rpg",
                ResolveRpgKinshipBoundaryRuleTemplate(settings),
                variables).Trim();
            variables["dialogue.guidance"] = guidance;
            string profileText = RenderTemplateOrThrow(
                "prompt_templates.rpg_relationship_profile",
                "rpg",
                ResolveRpgRelationshipProfileTemplate(settings),
                variables).Trim();
            return ApplyPromptSourceTag(profileText, true);
        }

        private static bool HasAnyBloodRelationBetweenPair(Pawn first, Pawn second)
        {
            return HasAnyBloodRelationOneWay(first, second) || HasAnyBloodRelationOneWay(second, first);
        }

        private static bool HasAnyBloodRelationOneWay(Pawn fromPawn, Pawn toPawn)
        {
            if (fromPawn?.relations?.DirectRelations == null || toPawn == null)
            {
                return false;
            }

            for (int i = 0; i < fromPawn.relations.DirectRelations.Count; i++)
            {
                DirectPawnRelation relation = fromPawn.relations.DirectRelations[i];
                if (relation?.otherPawn != toPawn || relation.def == null)
                {
                    continue;
                }

                if (relation.def.familyByBloodRelation)
                {
                    return true;
                }
            }

            return false;
        }

        private static string ResolvePairRomanceState(Pawn first, Pawn second)
        {
            if (HasPairRelationEitherDirection(first, second, PawnRelationDefOf.Spouse))
            {
                return "spouse";
            }

            if (HasPairRelationEitherDirection(first, second, PawnRelationDefOf.Fiance))
            {
                return "fiance";
            }

            if (HasPairRelationEitherDirection(first, second, PawnRelationDefOf.Lover))
            {
                return "lover";
            }

            if (HasPairRelationEitherDirection(first, second, PawnRelationDefOf.ExSpouse) ||
                HasPairRelationEitherDirection(first, second, PawnRelationDefOf.ExLover))
            {
                return "ex-or-none";
            }

            return "none";
        }

        private static bool HasPairRelationEitherDirection(Pawn first, Pawn second, PawnRelationDef relationDef)
        {
            if (relationDef == null || first == null || second == null)
            {
                return false;
            }

            return first.relations?.DirectRelationExists(relationDef, second) == true ||
                second.relations?.DirectRelationExists(relationDef, first) == true;
        }

        private string BuildRpgApiContractText(
            RimChatSettings settings,
            SystemPromptConfig config,
            DialogueScenarioContext context,
            bool preferCompact)
        {
            if (settings?.EnableRPGAPI != true)
            {
                return string.Empty;
            }

            return BuildTextBlock(sb =>
            {
                RpgPromptCustomConfig promptConfig = RpgPromptCustomStore.LoadOrDefault();
                if (preferCompact)
                {
                    RpgApiPromptTextBuilder.AppendActionDefinitionsCompact(sb, promptConfig?.ApiActionPrompt);
                }
                else
                {
                    RpgApiPromptTextBuilder.AppendActionDefinitions(sb, promptConfig?.ApiActionPrompt);
                }

                string formatConstraint = BuildRpgFormatConstraintText(settings, config, context, preferCompact);
                if (!string.IsNullOrWhiteSpace(formatConstraint))
                {
                    sb.AppendLine(ResolveRpgFormatConstraintHeader(settings));
                    sb.AppendLine(formatConstraint);
                    sb.AppendLine();
                }
            });
        }

        private string BuildRpgFormatConstraintText(
            RimChatSettings settings,
            SystemPromptConfig config,
            DialogueScenarioContext context,
            bool preferCompact)
        {
            RpgPromptCustomConfig promptConfig = RpgPromptCustomStore.LoadOrDefault();
            string configured = promptConfig?.FormatConstraint?.Trim();
            string baseConstraint;
            if (!preferCompact)
            {
                baseConstraint = ApplyPromptSourceTag(configured ?? string.Empty, true);
                return AppendRpgActionReliabilityConstraint(baseConstraint, settings, config, context);
            }

            if (!string.IsNullOrWhiteSpace(configured) && configured.Length <= 600)
            {
                baseConstraint = ApplyPromptSourceTag(configured, true);
                return AppendRpgActionReliabilityConstraint(baseConstraint, settings, config, context);
            }

            baseConstraint = ApplyPromptSourceTag(
                ResolveRpgCompactFormatFallback(settings),
                false);

            return AppendRpgActionReliabilityConstraint(baseConstraint, settings, config, context);
        }

        private string AppendRpgActionReliabilityConstraint(
            string baseConstraint,
            RimChatSettings settings,
            SystemPromptConfig config,
            DialogueScenarioContext context)
        {
            string reliabilityRule = ApplyPromptSourceTag(
                ResolveRpgActionReliabilityFallback(settings),
                false);

            if (string.IsNullOrWhiteSpace(baseConstraint))
            {
                return reliabilityRule;
            }

            string marker = ResolveRpgActionReliabilityMarker(settings);
            if (baseConstraint.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return baseConstraint;
            }

            if (baseConstraint.IndexOf(reliabilityRule, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return baseConstraint;
            }

            var sb = new StringBuilder(baseConstraint.Length + reliabilityRule.Length + 2);
            sb.Append(baseConstraint.TrimEnd());
            sb.AppendLine();
            sb.Append(reliabilityRule);
            return sb.ToString();
        }

        private static string ResolveRpgRoleFallbackTemplate(RimChatSettings settings)
        {
            RpgPromptCustomConfig promptConfig = RpgPromptCustomStore.LoadOrDefault();
            if (!string.IsNullOrWhiteSpace(promptConfig?.RoleSettingFallbackTemplate))
            {
                return promptConfig.RoleSettingFallbackTemplate;
            }

            return RpgPromptDefaultsProvider.GetDefaults().RoleSettingFallbackTemplate;
        }

        private static string ResolveRpgFormatConstraintHeader(RimChatSettings settings)
        {
            RpgPromptCustomConfig promptConfig = RpgPromptCustomStore.LoadOrDefault();
            if (!string.IsNullOrWhiteSpace(promptConfig?.FormatConstraintHeader))
            {
                return promptConfig.FormatConstraintHeader;
            }

            return RpgPromptDefaultsProvider.GetDefaults().FormatConstraintHeader;
        }

        private static string ResolveRpgCompactFormatFallback(RimChatSettings settings)
        {
            RpgPromptCustomConfig promptConfig = RpgPromptCustomStore.LoadOrDefault();
            if (!string.IsNullOrWhiteSpace(promptConfig?.CompactFormatFallback))
            {
                return promptConfig.CompactFormatFallback;
            }

            return RpgPromptDefaultsProvider.GetDefaults().CompactFormatFallback;
        }

        private static string ResolveRpgActionReliabilityFallback(RimChatSettings settings)
        {
            RpgPromptCustomConfig promptConfig = RpgPromptCustomStore.LoadOrDefault();
            if (!string.IsNullOrWhiteSpace(promptConfig?.ActionReliabilityFallback))
            {
                return promptConfig.ActionReliabilityFallback;
            }

            return RpgPromptDefaultsProvider.GetDefaults().ActionReliabilityFallback;
        }

        private static string ResolveRpgActionReliabilityMarker(RimChatSettings settings)
        {
            RpgPromptCustomConfig promptConfig = RpgPromptCustomStore.LoadOrDefault();
            if (!string.IsNullOrWhiteSpace(promptConfig?.ActionReliabilityMarker))
            {
                return promptConfig.ActionReliabilityMarker;
            }

            return RpgPromptDefaultsProvider.GetDefaults().ActionReliabilityMarker;
        }

        private static string ResolveRpgRelationshipProfileTemplate(RimChatSettings settings)
        {
            RpgPromptCustomConfig promptConfig = RpgPromptCustomStore.LoadOrDefault();
            if (!string.IsNullOrWhiteSpace(promptConfig?.RelationshipProfileTemplate))
            {
                return promptConfig.RelationshipProfileTemplate;
            }

            return RpgPromptDefaultsProvider.GetDefaults().RelationshipProfileTemplate;
        }

        private static string ResolveRpgKinshipBoundaryRuleTemplate(RimChatSettings settings)
        {
            RpgPromptCustomConfig promptConfig = RpgPromptCustomStore.LoadOrDefault();
            if (!string.IsNullOrWhiteSpace(promptConfig?.KinshipBoundaryRuleTemplate))
            {
                return promptConfig.KinshipBoundaryRuleTemplate;
            }

            return RpgPromptDefaultsProvider.GetDefaults().KinshipBoundaryRuleTemplate;
        }

        private static string CompactRpgEnvironmentBlock(string environmentBlock)
        {
            if (string.IsNullOrWhiteSpace(environmentBlock))
            {
                return environmentBlock ?? string.Empty;
            }

            string[] lines = environmentBlock.Replace("\r", string.Empty).Split('\n');
            var sb = new StringBuilder(environmentBlock.Length);
            bool skipWorldview = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();
                if (trimmed.IndexOf("ENVIRONMENT WORLDVIEW", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    skipWorldview = true;
                    continue;
                }

                if (skipWorldview)
                {
                    if (!trimmed.StartsWith("==="))
                    {
                        continue;
                    }

                    skipWorldview = false;
                }

                sb.AppendLine(line);
            }

            return sb.ToString().Trim();
        }

        private string BuildOutputLanguageGuidance(
            RimChatSettings settings,
            SystemPromptConfig config,
            DialogueScenarioContext context)
        {
            string targetLanguage = settings?.GetEffectivePromptLanguage();
            if (string.IsNullOrWhiteSpace(targetLanguage))
            {
                return string.Empty;
            }

            string template = config?.PromptTemplates?.OutputLanguageTemplate;
            string channel = ResolveRenderChannel(context);
            string requiredTemplate = RequireTemplateText("prompt_templates.output_language", channel, template);

            return ApplyPromptSourceTag(
                RenderTemplateOrThrow(
                    "prompt_templates.output_language",
                    channel,
                    requiredTemplate,
                    BuildSharedPromptTemplateVariables(context, targetLanguage)),
                true);
        }

        private static Dictionary<string, object> BuildSharedPromptTemplateVariables(
            DialogueScenarioContext context,
            string targetLanguage)
        {
            string channel = context?.IsRpg == true ? "rpg" : "diplomacy";
            string mode = context?.IsProactive == true ? "proactive" : "manual";
            var variables = CreatePromptVariableSeed();
            variables["ctx.channel"] = channel;
            variables["ctx.mode"] = mode;
            variables["system.target_language"] = targetLanguage ?? string.Empty;
            variables["system.game_language"] = targetLanguage ?? string.Empty;
            variables["world.faction.name"] = context?.Faction?.Name ?? "Unknown Faction";
            variables["world.scene_tags"] = context?.Tags == null ? string.Empty : string.Join(", ", context.Tags.OrderBy(item => item));
            variables["pawn.initiator.name"] = context?.Initiator?.LabelShort ?? "Unknown";
            variables["pawn.target.name"] = context?.Target?.LabelShort ?? "Unknown";
            variables["pawn.initiator"] = context?.Initiator;
            variables["pawn.target"] = context?.Target;
            variables["world.faction"] = context?.Faction;

            return variables;
        }

        private static Dictionary<string, object> CreatePromptVariableSeed()
        {
            var variables = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in PromptVariableCatalog.GetAll())
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                variables[path] = string.Empty;
            }

            return variables;
        }

        private string RenderPromptNodeTemplate(
            SystemPromptConfig config,
            DialogueScenarioContext context,
            string template,
            string bodyVariableName,
            string bodyText)
        {
            string normalizedBody = bodyText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedBody))
            {
                return string.Empty;
            }

            Dictionary<string, object> variables = BuildSharedPromptTemplateVariables(context, string.Empty);
            string namespacedVariable = ResolveNodeBodyVariablePath(bodyVariableName);
            variables[namespacedVariable] = normalizedBody;
            string channel = ResolveRenderChannel(context);
            string templateId = $"prompt_templates.node.{bodyVariableName}";
            string requiredTemplate = RequireTemplateText(templateId, channel, template);
            return ApplyPromptSourceTag(
                RenderTemplateOrThrow(
                    templateId,
                    channel,
                    requiredTemplate,
                    variables),
                true);
        }

        private static string ResolveRenderChannel(DialogueScenarioContext context)
        {
            return context?.IsRpg == true ? "rpg" : "diplomacy";
        }

        private static string ResolveNodeBodyVariablePath(string bodyVariableName)
        {
            if (string.IsNullOrWhiteSpace(bodyVariableName))
            {
                return "dialogue.body";
            }

            switch (bodyVariableName.Trim().ToLowerInvariant())
            {
                case "api_limits_body":
                    return "dialogue.api_limits_body";
                case "quest_guidance_body":
                    return "dialogue.quest_guidance_body";
                case "response_contract_body":
                    return "dialogue.response_contract_body";
                default:
                    return "dialogue." + bodyVariableName.Trim().ToLowerInvariant();
            }
        }
    }
}
