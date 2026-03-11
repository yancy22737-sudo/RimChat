using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimChat.Compat;
using RimChat.Config;
using RimChat.Core;
using RimChat.Memory;
using RimChat.Prompting;
using RimWorld;
using Verse;

namespace RimChat.Persistence
{
    /// <summary>/// Dependencies: SystemPromptConfig, DialogueScenarioContext, PromptHierarchyRenderer.
 /// Responsibility: build diplomacy/RPG prompts through hierarchical information management.
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
            return BuildFullSystemPromptHierarchical(faction, config, isProactive, additionalSceneTags);
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
            IEnumerable<string> additionalSceneTags)
        {
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
            AppendRimTalkCompatNode(instruction, null, null, faction, "diplomacy");

            PromptHierarchyNode dynamicData = BuildDiplomacyDynamicDataNode(config, faction);
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
            AppendRimTalkCompatNode(roleStack, initiator, target, target?.Faction, "rpg");

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

        private PromptHierarchyNode BuildDiplomacyDynamicDataNode(SystemPromptConfig config, Faction faction)
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

        private string BuildDecisionPolicyText(SystemPromptConfig config, DialogueScenarioContext context)
        {
            bool isRpg = context?.IsRpg == true;
            string fallback = isRpg
                ? RpgPromptDefaultsProvider.GetDefaults().DecisionPolicyTemplate
                : "Decision priority order:\n" +
                  "1) Output format and language correctness.\n" +
                  "2) Resolve the player's current diplomacy objective.\n" +
                  "3) Keep facts grounded in prompt and memory.\n" +
                  "4) Obey diplomacy risk, relationship, and action constraints.\n" +
                  "5) Preserve persona-consistent style and tone.\n" +
                  "6) Add no more than one short follow-up only if it improves clarity or negotiation progress.";
            string template = isRpg
                ? RpgPromptDefaultsProvider.GetDefaults().DecisionPolicyTemplate
                : config?.PromptTemplates?.DecisionPolicyTemplate;
            bool templatesEnabled = isRpg || config?.PromptTemplates?.Enabled == true;
            if (!templatesEnabled || string.IsNullOrWhiteSpace(template))
            {
                return ApplyPromptSourceTag(fallback, false);
            }

            return ApplyPromptSourceTag(
                PromptTemplateRenderer.Render(template, BuildPolicyTemplateVariables(context, string.Empty, string.Empty, string.Empty)),
                true);
        }

        private string BuildTurnObjectiveText(
            SystemPromptConfig config,
            DialogueScenarioContext context,
            string primaryObjective,
            string optionalFollowup)
        {
            string primary = string.IsNullOrWhiteSpace(primaryObjective)
                ? "Address the player's latest explicit intent first."
                : primaryObjective.Trim();
            string followup = string.IsNullOrWhiteSpace(optionalFollowup)
                ? "After the primary objective is completed, optionally add one natural follow-up."
                : optionalFollowup.Trim();
            string fallback =
                $"PrimaryObjective: {primary}\n" +
                $"OptionalFollowup: {followup}\n" +
                "Constraint: at most one topic shift in this turn, and only after PrimaryObjective is satisfied.";
            bool isRpg = context?.IsRpg == true;
            string template = isRpg
                ? RpgPromptDefaultsProvider.GetDefaults().TurnObjectiveTemplate
                : config?.PromptTemplates?.TurnObjectiveTemplate;
            bool templatesEnabled = isRpg || config?.PromptTemplates?.Enabled == true;
            if (!templatesEnabled || string.IsNullOrWhiteSpace(template))
            {
                return ApplyPromptSourceTag(fallback, false);
            }

            return ApplyPromptSourceTag(
                PromptTemplateRenderer.Render(template, BuildPolicyTemplateVariables(context, primary, followup, string.Empty)),
                true);
        }

        private string BuildOpeningObjectiveText(
            SystemPromptConfig config,
            DialogueScenarioContext context,
            string unresolvedIntent)
        {
            string normalizedIntent = string.IsNullOrWhiteSpace(unresolvedIntent)
                ? "none"
                : unresolvedIntent.Trim();
            string fallback =
                "OpeningObjective:\n" +
                $"- First line should acknowledge unresolved player intent when available: {normalizedIntent}\n" +
                "- Start naturally in-character; do not expose out-of-character control instructions.";
            string template = RpgPromptDefaultsProvider.GetDefaults().OpeningObjectiveTemplate;
            if (string.IsNullOrWhiteSpace(template))
            {
                return ApplyPromptSourceTag(fallback, false);
            }

            return ApplyPromptSourceTag(
                PromptTemplateRenderer.Render(
                    template,
                    BuildPolicyTemplateVariables(context, string.Empty, string.Empty, normalizedIntent)),
                true);
        }

        private string BuildTopicShiftRuleText(SystemPromptConfig config, DialogueScenarioContext context)
        {
            bool isRpg = context?.IsRpg == true;
            string fallback = isRpg
                ? "TopicShiftRule: complete the primary objective first; then at most one natural topic extension is allowed."
                : "TopicShiftRule: finish the current diplomacy objective first; only add one short follow-up if it helps clarity, bargaining, or next-step planning.";
            string template = isRpg
                ? RpgPromptDefaultsProvider.GetDefaults().TopicShiftRuleTemplate
                : config?.PromptTemplates?.TopicShiftRuleTemplate;
            bool templatesEnabled = isRpg || config?.PromptTemplates?.Enabled == true;
            if (!templatesEnabled || string.IsNullOrWhiteSpace(template))
            {
                return ApplyPromptSourceTag(fallback, false);
            }

            return ApplyPromptSourceTag(
                PromptTemplateRenderer.Render(template, BuildPolicyTemplateVariables(context, string.Empty, string.Empty, string.Empty)),
                true);
        }

        private static string BuildPrimaryObjectiveFromIntent(string unresolvedIntent)
        {
            if (string.IsNullOrWhiteSpace(unresolvedIntent))
            {
                return "Address the player's latest explicit request or question first.";
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

        private static Dictionary<string, string> BuildPolicyTemplateVariables(
            DialogueScenarioContext context,
            string primaryObjective,
            string optionalFollowup,
            string unresolvedIntent)
        {
            Dictionary<string, string> variables = BuildSharedPromptTemplateVariables(context, string.Empty);
            variables["primary_objective"] = primaryObjective ?? string.Empty;
            variables["optional_followup"] = optionalFollowup ?? string.Empty;
            variables["latest_unresolved_intent"] = unresolvedIntent ?? string.Empty;
            variables["topic_shift_rule"] = "Complete the primary objective first, then allow at most one natural topic extension.";
            return variables;
        }

        private string BuildFactGroundingGuidanceText(SystemPromptConfig config, DialogueScenarioContext context)
        {
            string template = config?.PromptTemplates?.FactGroundingTemplate;
            if (string.IsNullOrWhiteSpace(template) || config?.PromptTemplates?.Enabled != true)
            {
                return ApplyPromptSourceTag(
                    "Use only verifiable prompt facts and explicit memory. Do not fabricate unknown details.",
                    false);
            }

            return ApplyPromptSourceTag(
                PromptTemplateRenderer.Render(template, BuildSharedPromptTemplateVariables(context, string.Empty)),
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
            if (!string.IsNullOrWhiteSpace(template) && config?.PromptTemplates?.Enabled == true)
            {
                return ApplyPromptSourceTag(
                    PromptTemplateRenderer.Render(template, BuildSharedPromptTemplateVariables(context, string.Empty)),
                    true);
            }

            return ApplyPromptSourceTag("Act as the current faction leader and keep responses role-consistent.", false);
        }

        private string BuildSocialCircleActionRuleText(SystemPromptConfig config, DialogueScenarioContext context)
        {
            if (RimChatMod.Settings?.EnableSocialCircle != true)
            {
                return string.Empty;
            }

            string template = config?.PromptTemplates?.SocialCircleActionRuleTemplate;
            if (!string.IsNullOrWhiteSpace(template) && config?.PromptTemplates?.Enabled == true)
            {
                return ApplyPromptSourceTag(
                    PromptTemplateRenderer.Render(template, BuildSharedPromptTemplateVariables(context, string.Empty)),
                    true);
            }

            return ApplyPromptSourceTag(
                "Use publish_public_post only for public, world-facing statements that should influence broader faction sentiment.",
                false);
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

            var variables = BuildSharedPromptTemplateVariables(context, string.Empty);
            variables["target_name"] = target?.LabelShort ?? "Unknown";
            string fallbackTemplate = ResolveRpgRoleFallbackTemplate(settings);
            string fallbackText = PromptTemplateRenderer.Render(fallbackTemplate, variables);
            return ApplyPromptSourceTag(fallbackText, false);
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
            if (string.IsNullOrWhiteSpace(template) || config?.PromptTemplates?.Enabled != true)
            {
                return ApplyPromptSourceTag(
                    $"Respond in {targetLanguage}. Keep structured keys and action identifiers unchanged.",
                    false);
            }

            return ApplyPromptSourceTag(
                PromptTemplateRenderer.Render(template, BuildSharedPromptTemplateVariables(context, targetLanguage)),
                true);
        }

        private static Dictionary<string, string> BuildSharedPromptTemplateVariables(
            DialogueScenarioContext context,
            string targetLanguage)
        {
            string channel = context?.IsRpg == true ? "rpg" : "diplomacy";
            string mode = context?.IsProactive == true ? "proactive" : "manual";

            var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["channel"] = channel,
                ["mode"] = mode,
                ["target_language"] = targetLanguage ?? string.Empty,
                ["faction_name"] = context?.Faction?.Name ?? "Unknown Faction",
                ["initiator_name"] = context?.Initiator?.LabelShort ?? "Unknown",
                ["target_name"] = context?.Target?.LabelShort ?? "Unknown"
            };

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

            if (config?.PromptTemplates?.Enabled != true || string.IsNullOrWhiteSpace(template))
            {
                return ApplyPromptSourceTag(normalizedBody, false);
            }

            var variables = BuildSharedPromptTemplateVariables(context, string.Empty);
            variables[bodyVariableName] = normalizedBody;
            return ApplyPromptSourceTag(PromptTemplateRenderer.Render(template, variables), true);
        }

        private static void AppendRimTalkCompatNode(
            PromptHierarchyNode stackNode,
            Pawn initiator,
            Pawn target,
            Faction faction,
            string channel)
        {
            if (stackNode == null)
            {
                return;
            }

            RimChatSettings settings = RimChatMod.Settings;
            if (settings?.EnableRimTalkPromptCompat != true)
            {
                return;
            }

            string template = settings.GetRimTalkCompatTemplateOrDefault();
            if (string.IsNullOrWhiteSpace(template))
            {
                return;
            }

            string rendered = RimTalkCompatBridge.RenderCompatTemplate(
                template,
                initiator,
                target,
                faction,
                channel);

            AddTextNodeIfNotEmpty(stackNode, "rimtalk_compat", rendered, true);

            if (string.Equals(channel, "rpg", StringComparison.OrdinalIgnoreCase))
            {
                string presetModEntries = RimTalkCompatBridge.RenderActivePresetModEntries(
                    initiator,
                    target,
                    faction,
                    channel);
                AddTextNodeIfNotEmpty(stackNode, "rimtalk_preset_mod_entries", presetModEntries, true);
            }
        }
    }
}
