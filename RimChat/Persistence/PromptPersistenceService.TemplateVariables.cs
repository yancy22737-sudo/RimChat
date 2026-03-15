using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RimChat.Config;
using RimChat.Core;
using RimChat.Prompting;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.Persistence
{
    public partial class PromptPersistenceService
    {
        private static readonly Regex TemplateVariableRegex = new Regex(@"\{\{\s*([a-zA-Z0-9_.]+)\s*\}\}", RegexOptions.Compiled);

        private static readonly List<PromptTemplateVariableDefinition> TemplateVariableDefinitions = new List<PromptTemplateVariableDefinition>
        {
            new PromptTemplateVariableDefinition("ctx.channel", "RimChat_TemplateVar_ctx_channel_Desc"),
            new PromptTemplateVariableDefinition("ctx.mode", "RimChat_TemplateVar_ctx_mode_Desc"),
            new PromptTemplateVariableDefinition("system.target_language", "RimChat_TemplateVar_system_target_language_Desc"),
            new PromptTemplateVariableDefinition("world.faction.name", "RimChat_TemplateVar_world_faction_name_Desc"),
            new PromptTemplateVariableDefinition("pawn.initiator.name", "RimChat_TemplateVar_pawn_initiator_name_Desc"),
            new PromptTemplateVariableDefinition("pawn.target.name", "RimChat_TemplateVar_pawn_target_name_Desc"),
            new PromptTemplateVariableDefinition("world.scene_tags", "RimChat_TemplateVar_scene_tags_Desc"),
            new PromptTemplateVariableDefinition("world.environment_params", "RimChat_TemplateVar_environment_params_Desc"),
            new PromptTemplateVariableDefinition("world.recent_world_events", "RimChat_TemplateVar_recent_world_events_Desc"),
            new PromptTemplateVariableDefinition("world.colony_status", "RimChat_TemplateVar_colony_status_Desc"),
            new PromptTemplateVariableDefinition("world.colony_factions", "RimChat_TemplateVar_colony_factions_Desc"),
            new PromptTemplateVariableDefinition("world.current_faction_profile", "RimChat_TemplateVar_current_faction_profile_Desc"),
            new PromptTemplateVariableDefinition("pawn.target.profile", "RimChat_TemplateVar_rpg_target_profile_Desc"),
            new PromptTemplateVariableDefinition("pawn.initiator.profile", "RimChat_TemplateVar_rpg_initiator_profile_Desc"),
            new PromptTemplateVariableDefinition("pawn.player.profile", "RimChat_TemplateVar_player_pawn_profile_Desc"),
            new PromptTemplateVariableDefinition("pawn.player.royalty_summary", "RimChat_TemplateVar_player_royalty_summary_Desc"),
            new PromptTemplateVariableDefinition("world.faction_settlement_summary", "RimChat_TemplateVar_faction_settlement_summary_Desc"),
            new PromptTemplateVariableDefinition("dialogue.primary_objective", "RimChat_TemplateVar_dialogue_primary_objective_Desc"),
            new PromptTemplateVariableDefinition("dialogue.optional_followup", "RimChat_TemplateVar_dialogue_optional_followup_Desc"),
            new PromptTemplateVariableDefinition("dialogue.latest_unresolved_intent", "RimChat_TemplateVar_dialogue_latest_unresolved_intent_Desc"),
            new PromptTemplateVariableDefinition("dialogue.topic_shift_rule", "RimChat_TemplateVar_dialogue_topic_shift_rule_Desc"),
            new PromptTemplateVariableDefinition("dialogue.api_limits_body", "RimChat_TemplateVar_dialogue_api_limits_body_Desc"),
            new PromptTemplateVariableDefinition("dialogue.quest_guidance_body", "RimChat_TemplateVar_dialogue_quest_guidance_body_Desc"),
            new PromptTemplateVariableDefinition("dialogue.response_contract_body", "RimChat_TemplateVar_dialogue_response_contract_body_Desc")
        };

        public IReadOnlyList<PromptTemplateVariableDefinition> GetTemplateVariableDefinitions()
        {
            return TemplateVariableDefinitions;
        }

        public TemplateVariableValidationResult ValidateTemplateVariables(string templateText)
        {
            var result = new TemplateVariableValidationResult();
            if (string.IsNullOrWhiteSpace(templateText))
            {
                return result;
            }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var validationPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            MatchCollection matches = TemplateVariableRegex.Matches(templateText);
            for (int i = 0; i < matches.Count; i++)
            {
                string name = NormalizeTemplateVariableName(matches[i].Groups[1].Value);
                if (name.Length == 0)
                {
                    continue;
                }

                if (PromptVariableCatalog.Contains(name))
                {
                    used.Add(name);
                }
                else
                {
                    unknown.Add(name);
                }

                if (IsNamespacedVariablePath(name))
                {
                    validationPaths.Add(name);
                }
            }

            TryCollectScribanDiagnostic(templateText, validationPaths, result);
            result.UsedVariables.AddRange(used.OrderBy(item => item));
            result.UnknownVariables.AddRange(unknown.OrderBy(item => item));
            return result;
        }

        internal string RenderTemplateVariables(
            string templateText,
            DialogueScenarioContext context,
            EnvironmentPromptConfig envConfig,
            out List<string> usedVariables,
            out List<string> unknownVariables)
        {
            if (string.IsNullOrWhiteSpace(templateText) || templateText.IndexOf("{{", StringComparison.Ordinal) < 0)
            {
                usedVariables = new List<string>();
                unknownVariables = new List<string>();
                return templateText ?? string.Empty;
            }

            TemplateVariableValidationResult validation = ValidateTemplateVariables(templateText);
            usedVariables = validation.UsedVariables.OrderBy(item => item).ToList();
            unknownVariables = validation.UnknownVariables.OrderBy(item => item).ToList();
            if (unknownVariables.Count > 0)
            {
                string channel = context?.IsRpg == true ? "rpg" : "diplomacy";
                throw new PromptRenderException(
                    "scene_entry.template",
                    channel,
                    new PromptRenderDiagnostic
                    {
                        ErrorCode = PromptRenderErrorCode.UnknownVariable,
                        Message = $"Unknown namespaced variable: {unknownVariables[0]}"
                    });
            }

            string resolvedChannel = context?.IsRpg == true ? "rpg" : "diplomacy";
            const string templateId = "scene_entry.template";
            PromptRenderContext renderContext = BuildTemplateRenderContext(templateId, resolvedChannel, context, envConfig);
            return PromptTemplateRenderer.RenderOrThrow(templateId, resolvedChannel, templateText, renderContext);
        }

        private PromptRenderContext BuildTemplateRenderContext(
            string templateId,
            string channel,
            DialogueScenarioContext context,
            EnvironmentPromptConfig envConfig)
        {
            PromptRenderContext renderContext = PromptRenderContext.Create(templateId, channel);
            renderContext.SetValues(BuildTemplateVariableValues(context, envConfig));
            return renderContext;
        }

        private Dictionary<string, object> BuildTemplateVariableValues(
            DialogueScenarioContext context,
            EnvironmentPromptConfig envConfig)
        {
            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (string variablePath in PromptVariableCatalog.GetAll())
            {
                values[variablePath] = string.Empty;
            }

            for (int i = 0; i < TemplateVariableDefinitions.Count; i++)
            {
                string name = TemplateVariableDefinitions[i].Name;
                if (TryResolveTemplateVariable(name, context, envConfig, out string resolved))
                {
                    values[name] = resolved ?? string.Empty;
                }
            }

            values["system.game_language"] = LanguageDatabase.activeLanguage?.FriendlyNameNative
                ?? (RimChatMod.Settings?.GetEffectivePromptLanguage() ?? string.Empty);
            values["pawn.initiator"] = context?.Initiator;
            values["pawn.target"] = context?.Target;
            values["world.faction"] = context?.Faction;
            return values;
        }

        private static string NormalizeTemplateVariableName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return string.Empty;
            }

            return rawName.Trim().ToLowerInvariant();
        }

        private static bool IsNamespacedVariablePath(string variableName)
        {
            return !string.IsNullOrWhiteSpace(variableName) && variableName.IndexOf('.') > 0;
        }

        private static void TryCollectScribanDiagnostic(
            string templateText,
            IEnumerable<string> variablePaths,
            TemplateVariableValidationResult result)
        {
            const string templateId = "editor.template_validation";
            const string channel = "editor";
            try
            {
                PromptRenderContext context = PromptTemplateRenderer.BuildValidationContext(templateId, channel, variablePaths);
                PromptTemplateRenderer.ValidateOrThrow(templateId, channel, templateText, context);
            }
            catch (PromptRenderException ex)
            {
                result.ScribanErrorCode = (int)ex.ErrorCode;
                result.ScribanErrorLine = ex.ErrorLine;
                result.ScribanErrorColumn = ex.ErrorColumn;
                result.ScribanErrorMessage = ex.Message ?? string.Empty;
            }
        }

        private bool TryResolveTemplateVariable(
            string variableName,
            DialogueScenarioContext context,
            EnvironmentPromptConfig envConfig,
            out string value)
        {
            value = string.Empty;
            switch (variableName)
            {
                case "ctx.channel":
                    value = context?.IsRpg == true ? "rpg" : "diplomacy";
                    return true;
                case "ctx.mode":
                    value = context?.IsProactive == true ? "proactive" : "manual";
                    return true;
                case "system.target_language":
                    value = RimChatMod.Settings?.GetEffectivePromptLanguage() ?? string.Empty;
                    return true;
                case "world.faction.name":
                    value = context?.Faction?.Name ?? "Unknown Faction";
                    return true;
                case "pawn.initiator.name":
                    value = context?.Initiator?.LabelShort ?? "Unknown";
                    return true;
                case "pawn.target.name":
                    value = context?.Target?.LabelShort ?? "Unknown";
                    return true;
                case "world.scene_tags":
                    value = BuildSceneTagsVariableText(context);
                    return true;
                case "world.environment_params":
                    value = BuildEnvironmentParamsVariableText(context, envConfig);
                    return true;
                case "world.recent_world_events":
                    value = BuildRecentWorldEventsVariableText(context, envConfig);
                    return true;
                case "world.colony_status":
                    value = BuildColonyStatusVariableText();
                    return true;
                case "world.colony_factions":
                    value = BuildColonyFactionsVariableText();
                    return true;
                case "world.current_faction_profile":
                    value = BuildCurrentFactionProfileVariableText(context);
                    return true;
                case "pawn.target.profile":
                    value = BuildPawnProfileVariableText(context?.Target);
                    return true;
                case "pawn.initiator.profile":
                    value = BuildPawnProfileVariableText(context?.Initiator);
                    return true;
                case "pawn.player.profile":
                    value = BuildPlayerPawnProfileVariableText(context);
                    return true;
                case "pawn.player.royalty_summary":
                    value = BuildPlayerRoyaltySummaryVariableText(context);
                    return true;
                case "world.faction_settlement_summary":
                    value = BuildFactionSettlementSummaryVariableText(context);
                    return true;
                default:
                    return false;
            }
        }

        private string BuildSceneTagsVariableText(DialogueScenarioContext context)
        {
            HashSet<string> tags = BuildScenarioTags(context, includePresetTags: true);
            if (tags == null || tags.Count == 0)
            {
                return "none";
            }

            return string.Join(", ", tags.OrderBy(tag => tag));
        }

        private string BuildEnvironmentParamsVariableText(DialogueScenarioContext context, EnvironmentPromptConfig envConfig)
        {
            Map map = ResolveEnvironmentMap(context);
            if (map == null)
            {
                return "No map context.";
            }

            if (!TryResolveFocusCell(map, context, out IntVec3 focusCell))
            {
                return "No focus cell.";
            }

            EnvironmentContextSwitchesConfig switches = envConfig?.EnvironmentContextSwitches ?? new EnvironmentContextSwitchesConfig();
            List<string> lines = BuildEnvironmentContextLines(map, focusCell, context, switches);
            if (lines == null || lines.Count == 0)
            {
                return "No environment parameters.";
            }

            return string.Join("\n", lines);
        }

        private string BuildRecentWorldEventsVariableText(DialogueScenarioContext context, EnvironmentPromptConfig envConfig)
        {
            var clonedEnv = envConfig?.Clone() ?? new EnvironmentPromptConfig();
            if (clonedEnv.EventIntelPrompt == null)
            {
                clonedEnv.EventIntelPrompt = new EventIntelPromptConfig();
            }

            clonedEnv.EventIntelPrompt.Enabled = true;
            clonedEnv.EventIntelPrompt.ApplyToDiplomacy = true;
            clonedEnv.EventIntelPrompt.ApplyToRpg = true;
            var sb = new StringBuilder();
            AppendRecentWorldEventIntel(sb, clonedEnv, context);
            string text = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(text) ? "No recent world events." : text;
        }

        private string BuildColonyStatusVariableText()
        {
            List<Map> homeMaps = Find.Maps?.Where(map => map != null && map.IsPlayerHome).ToList();
            if (homeMaps == null || homeMaps.Count == 0)
            {
                return "No active colony.";
            }

            int colonists = homeMaps.Sum(map => map.mapPawns?.FreeColonists?.Count ?? 0);
            int wealth = (int)homeMaps.Sum(map => map.wealthWatcher?.WealthTotal ?? 0f);
            string colonyName = Faction.OfPlayer?.Name ?? "Player Colony";
            int absTicks = Find.TickManager?.TicksAbs ?? 0;
            Vector2 longLat = Find.WorldGrid != null ? Find.WorldGrid.LongLatOf(homeMaps[0].Tile) : Vector2.zero;
            string dateText = GenDate.DateFullStringAt(absTicks, longLat);
            return $"Colony: {colonyName}\nHomeMaps: {homeMaps.Count}\nColonists: {colonists}\nTotalWealth: {wealth}\nDate: {dateText}";
        }

        private string BuildColonyFactionsVariableText()
        {
            IEnumerable<Faction> factions = Find.FactionManager?.AllFactionsVisible?
                .Where(faction => faction != null && !faction.IsPlayer && !faction.defeated)
                .OrderByDescending(faction => faction.PlayerGoodwill)
                .Take(12);
            if (factions == null)
            {
                return "No known factions.";
            }

            var lines = new List<string>();
            foreach (Faction faction in factions)
            {
                string relation = faction.RelationKindWith(Faction.OfPlayer).ToString();
                lines.Add($"- {faction.Name}: goodwill={faction.PlayerGoodwill}, relation={relation}, tech={faction.def?.techLevel}");
            }

            return lines.Count == 0 ? "No known factions." : string.Join("\n", lines);
        }

        private string BuildCurrentFactionProfileVariableText(DialogueScenarioContext context)
        {
            Faction faction = context?.Faction ?? context?.Target?.Faction ?? context?.Initiator?.Faction;
            if (faction == null)
            {
                return "No faction context.";
            }

            string leader = faction.leader?.Name?.ToStringFull ?? "Unknown";
            string relation = faction.RelationKindWith(Faction.OfPlayer).ToString();
            return $"Faction: {faction.Name}\nDef: {faction.def?.defName}\nTech: {faction.def?.techLevel}\nGoodwill: {faction.PlayerGoodwill}\nRelation: {relation}\nLeader: {leader}";
        }

        private string BuildPawnProfileVariableText(Pawn pawn)
        {
            if (pawn == null)
            {
                return "No pawn context.";
            }

            float mood = pawn.needs?.mood?.CurLevelPercentage ?? -1f;
            float health = pawn.health?.summaryHealth?.SummaryHealthPercent ?? -1f;
            string moodText = mood >= 0f ? $"{mood:P0}" : "N/A";
            string healthText = health >= 0f ? $"{health:P0}" : "N/A";
            return $"Name: {pawn.LabelShortCap}\nKind: {pawn.KindLabel}\nFaction: {pawn.Faction?.Name ?? "None"}\nMood: {moodText}\nHealth: {healthText}";
        }

        private string BuildPlayerPawnProfileVariableText(DialogueScenarioContext context)
        {
            Faction faction = context?.Faction ?? context?.Target?.Faction ?? context?.Initiator?.Faction;
            Pawn preferred = context?.Initiator != null && context.Initiator.Faction == Faction.OfPlayer
                ? context.Initiator
                : null;
            string text = BuildPlayerPawnContextForPrompt(faction, preferred);
            return string.IsNullOrWhiteSpace(text) ? "No player pawn context." : text;
        }

        private string BuildPlayerRoyaltySummaryVariableText(DialogueScenarioContext context)
        {
            Faction faction = context?.Faction ?? context?.Target?.Faction ?? context?.Initiator?.Faction;
            Pawn preferred = context?.Initiator != null && context.Initiator.Faction == Faction.OfPlayer
                ? context.Initiator
                : null;
            string text = BuildPlayerRoyaltySummaryForPrompt(faction, preferred);
            return string.IsNullOrWhiteSpace(text) ? "No empire royalty context." : text;
        }

        private string BuildFactionSettlementSummaryVariableText(DialogueScenarioContext context)
        {
            Faction faction = context?.Faction ?? context?.Target?.Faction ?? context?.Initiator?.Faction;
            string text = BuildFactionSettlementSummaryForPrompt(faction);
            return string.IsNullOrWhiteSpace(text) ? "No settlement context." : text;
        }
    }
}
