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
    /// <summary>
    /// Dependencies: SystemPromptConfig, DialogueScenarioContext, PromptHierarchyRenderer.
    /// Responsibility: build diplomacy/RPG prompts through hierarchical information management.
    /// </summary>
    public partial class PromptPersistenceService
    {
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
            AddTextNodeIfNotEmpty(root, "fact_grounding", BuildFactGroundingGuidanceText());
            AddTextNodeIfNotEmpty(root, "output_language", BuildOutputLanguageGuidance(RimChatMod.Settings));

            var instruction = root.AddChild("instruction_stack");
            AddTextNodeIfNotEmpty(instruction, "global_system_prompt", config.GlobalSystemPrompt);
            AddTextNodeIfNotEmpty(instruction, "global_dialogue_prompt", config.GlobalDialoguePrompt);
            AddTextNodeIfNotEmpty(instruction, "faction_characteristics", ResolveFactionPromptText(faction));
            AppendRimTalkCompatNode(instruction, null, null, faction, "diplomacy");

            PromptHierarchyNode dynamicData = BuildDiplomacyDynamicDataNode(config, faction);
            if (dynamicData != null)
            {
                root.Children.Add(dynamicData);
            }

            AddTextNodeIfNotEmpty(root, "api_limits", BuildTextBlock(sb => AppendApiLimits(sb, faction)));
            AddTextNodeIfNotEmpty(root, "quest_guidance", BuildTextBlock(sb =>
            {
                AppendDynamicQuestGuidance(sb, faction);
                AppendQuestSelectionHardRules(sb);
            }));
            AddTextNodeIfNotEmpty(root, "response_contract", BuildTextBlock(sb =>
            {
                if (config.UseAdvancedMode)
                {
                    AppendAdvancedConfig(sb, config, faction);
                }
                else
                {
                    AppendSimpleConfig(sb, config, faction);
                }
            }));

            return PromptHierarchyRenderer.Render(root, config.UseHierarchicalPromptFormat);
        }

        private string BuildRpgSystemPromptHierarchical(
            Pawn initiator,
            Pawn target,
            bool isProactive,
            IEnumerable<string> additionalSceneTags)
        {
            var settings = RimChatMod.Settings;
            SystemPromptConfig config = LoadConfig() ?? CreateDefaultConfig();
            var scenarioContext = DialogueScenarioContext.CreateRpg(initiator, target, isProactive, additionalSceneTags);
            bool samePlayerFaction =
                initiator?.Faction != null &&
                initiator.Faction == target?.Faction &&
                initiator.Faction.IsPlayer;
            bool preferCompactContext = !isProactive && samePlayerFaction;

            var root = new PromptHierarchyNode("prompt_context");
            AddTextNodeIfNotEmpty(root, "channel", "rpg");
            AddTextNodeIfNotEmpty(root, "mode", isProactive ? "proactive" : "manual");
            string environmentBlock = BuildEnvironmentPromptBlocks(config, scenarioContext);
            if (preferCompactContext)
            {
                environmentBlock = CompactRpgEnvironmentBlock(environmentBlock);
            }
            AddTextNodeIfNotEmpty(root, "environment", environmentBlock);
            AddTextNodeIfNotEmpty(root, "fact_grounding", BuildFactGroundingGuidanceText());
            AddTextNodeIfNotEmpty(root, "output_language", BuildOutputLanguageGuidance(settings));

            var roleStack = root.AddChild("role_stack");
            AddTextNodeIfNotEmpty(roleStack, "role_setting", BuildRpgRoleSettingText(settings, target));
            AddTextNodeIfNotEmpty(roleStack, "personality_override", ResolveRpgPawnPersonaPrompt(target));
            AddTextNodeIfNotEmpty(roleStack, "dialogue_style", settings?.RPGDialogueStyle);
            AppendRimTalkCompatNode(roleStack, initiator, target, target?.Faction, "rpg");

            AddTextNodeIfNotEmpty(root, "dynamic_faction_memory",
                DialogueSummaryService.BuildRpgDynamicFactionMemoryBlock(target?.Faction, target));

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
            AddTextNodeIfNotEmpty(root, "api_contract", BuildRpgApiContractText(settings, preferCompactApiContract));
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
            if (dyn.InjectRelationContext && !dyn.InjectFiveDimensionData)
            {
                AddTextNodeIfNotEmpty(node, "relation_context", BuildTextBlock(sb => AppendRelationContext(sb, faction)));
            }

            if (dyn.InjectMemoryData)
            {
                AddTextNodeIfNotEmpty(node, "memory_data", BuildTextBlock(sb => AppendMemoryData(sb, faction)));
            }

            if (dyn.InjectFiveDimensionData)
            {
                AddTextNodeIfNotEmpty(node, "five_dimension_data", BuildTextBlock(sb => AppendFiveDimensionData(sb, faction)));
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

            if (settings?.RPGInjectPsychologicalAssessment == true)
            {
                AddTextNodeIfNotEmpty(node, "psychological_assessment",
                    BuildTextBlock(sb => AppendRPGRelationData(sb, initiator, target)));
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

        private static void AddTextNodeIfNotEmpty(PromptHierarchyNode parent, string id, string text)
        {
            if (parent == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            parent.AddChild(id, text.Trim());
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

        private string BuildFactGroundingGuidanceText()
        {
            return BuildTextBlock(AppendFactGroundingGuidance);
        }

        private string ResolveFactionPromptText(Faction faction)
        {
            string factionPrompt = FactionPromptManager.Instance.GetPrompt(faction?.def?.defName);
            if (!string.IsNullOrWhiteSpace(factionPrompt))
            {
                return factionPrompt.Trim();
            }

            return "You are the leader of a faction in RimWorld.";
        }

        private static string BuildRpgRoleSettingText(RimChatSettings settings, Pawn target)
        {
            if (!string.IsNullOrWhiteSpace(settings?.RPGRoleSetting))
            {
                return settings.RPGRoleSetting.Trim();
            }

            return $"You are roleplaying as {target?.LabelShort ?? "Unknown"} in RimWorld.";
        }

        private static string BuildRpgApiContractText(RimChatSettings settings, bool preferCompact)
        {
            if (settings?.EnableRPGAPI != true)
            {
                return string.Empty;
            }

            return BuildTextBlock(sb =>
            {
                if (preferCompact)
                {
                    RpgApiPromptTextBuilder.AppendActionDefinitionsCompact(sb);
                }
                else
                {
                    RpgApiPromptTextBuilder.AppendActionDefinitions(sb);
                }

                string formatConstraint = BuildRpgFormatConstraintText(settings, preferCompact);
                if (!string.IsNullOrWhiteSpace(formatConstraint))
                {
                    sb.AppendLine("=== FORMAT CONSTRAINT (REQUIRED) ===");
                    sb.AppendLine(formatConstraint);
                    sb.AppendLine();
                }
            });
        }

        private static string BuildRpgFormatConstraintText(RimChatSettings settings, bool preferCompact)
        {
            string configured = settings?.RPGFormatConstraint?.Trim();
            string baseConstraint;
            if (!preferCompact)
            {
                baseConstraint = configured ?? string.Empty;
                return AppendRpgActionReliabilityConstraint(baseConstraint);
            }

            if (!string.IsNullOrWhiteSpace(configured) && configured.Length <= 600)
            {
                baseConstraint = configured;
                return AppendRpgActionReliabilityConstraint(baseConstraint);
            }

            baseConstraint = "Only output an extra JSON block when you need gameplay effects. JSON schema: {\"favorability_delta\":number,\"trust_delta\":number,\"fear_delta\":number,\"respect_delta\":number,\"dependency_delta\":number,\"actions\":[{\"action\":\"ActionName\",\"defName\":\"Optional\",\"amount\":0,\"reason\":\"Optional\"}]}. Omit zero deltas and omit the JSON block entirely if no effects.";
            return AppendRpgActionReliabilityConstraint(baseConstraint);
        }

        private static string AppendRpgActionReliabilityConstraint(string baseConstraint)
        {
            const string reliabilityRule =
                "Reliability rules: avoid long no-action streaks; if two consecutive replies have no gameplay effect, include one role-consistent TryGainMemory action. If your reply clearly closes/refuses the conversation, include ExitDialogue or ExitDialogueCooldown.";

            if (string.IsNullOrWhiteSpace(baseConstraint))
            {
                return reliabilityRule;
            }

            if (baseConstraint.IndexOf("Reliability rules:", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return baseConstraint;
            }

            var sb = new StringBuilder(baseConstraint.Length + reliabilityRule.Length + 2);
            sb.Append(baseConstraint.TrimEnd());
            sb.AppendLine();
            sb.Append(reliabilityRule);
            return sb.ToString();
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

        private static string BuildOutputLanguageGuidance(RimChatSettings settings)
        {
            string targetLanguage = settings?.GetEffectivePromptLanguage();
            if (string.IsNullOrWhiteSpace(targetLanguage))
            {
                return string.Empty;
            }

            return $"Respond in {targetLanguage}. Keep JSON keys, API action names, and code identifiers unchanged.";
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

            AddTextNodeIfNotEmpty(stackNode, "rimtalk_compat", rendered);

            if (string.Equals(channel, "rpg", StringComparison.OrdinalIgnoreCase))
            {
                string presetModEntries = RimTalkCompatBridge.RenderActivePresetModEntries(
                    initiator,
                    target,
                    faction,
                    channel);
                AddTextNodeIfNotEmpty(stackNode, "rimtalk_preset_mod_entries", presetModEntries);
            }
        }
    }
}
