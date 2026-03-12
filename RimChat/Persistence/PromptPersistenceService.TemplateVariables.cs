using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RimChat.Config;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.Persistence
{
    public partial class PromptPersistenceService
    {
        private static readonly Regex TemplateVariableRegex = new Regex(@"\{\{\s*([a-zA-Z0-9_]+)\s*\}\}", RegexOptions.Compiled);

        private static readonly List<PromptTemplateVariableDefinition> TemplateVariableDefinitions = new List<PromptTemplateVariableDefinition>
        {
            new PromptTemplateVariableDefinition("scene_tags", "RimChat_TemplateVar_scene_tags_Desc"),
            new PromptTemplateVariableDefinition("environment_params", "RimChat_TemplateVar_environment_params_Desc"),
            new PromptTemplateVariableDefinition("recent_world_events", "RimChat_TemplateVar_recent_world_events_Desc"),
            new PromptTemplateVariableDefinition("colony_status", "RimChat_TemplateVar_colony_status_Desc"),
            new PromptTemplateVariableDefinition("colony_factions", "RimChat_TemplateVar_colony_factions_Desc"),
            new PromptTemplateVariableDefinition("current_faction_profile", "RimChat_TemplateVar_current_faction_profile_Desc"),
            new PromptTemplateVariableDefinition("rpg_target_profile", "RimChat_TemplateVar_rpg_target_profile_Desc"),
            new PromptTemplateVariableDefinition("rpg_initiator_profile", "RimChat_TemplateVar_rpg_initiator_profile_Desc"),
            new PromptTemplateVariableDefinition("player_pawn_profile", "RimChat_TemplateVar_player_pawn_profile_Desc"),
            new PromptTemplateVariableDefinition("player_royalty_summary", "RimChat_TemplateVar_player_royalty_summary_Desc"),
            new PromptTemplateVariableDefinition("faction_settlement_summary", "RimChat_TemplateVar_faction_settlement_summary_Desc")
        };

        private static readonly HashSet<string> TemplateVariableNameSet =
            new HashSet<string>(TemplateVariableDefinitions.Select(def => def.Name), StringComparer.OrdinalIgnoreCase);

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
            MatchCollection matches = TemplateVariableRegex.Matches(templateText);

            for (int i = 0; i < matches.Count; i++)
            {
                string name = NormalizeTemplateVariableName(matches[i].Groups[1].Value);
                if (name.Length == 0)
                {
                    continue;
                }

                if (TemplateVariableNameSet.Contains(name))
                {
                    used.Add(name);
                }
                else
                {
                    unknown.Add(name);
                }
            }

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
            usedVariables = new List<string>();
            unknownVariables = new List<string>();

            if (string.IsNullOrWhiteSpace(templateText) || templateText.IndexOf("{{", StringComparison.Ordinal) < 0)
            {
                return templateText ?? string.Empty;
            }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string rendered = TemplateVariableRegex.Replace(templateText, match =>
            {
                string variableName = NormalizeTemplateVariableName(match.Groups[1].Value);
                if (variableName.Length == 0)
                {
                    return match.Value;
                }

                if (!TryResolveTemplateVariable(variableName, context, envConfig, out string value))
                {
                    unknown.Add(variableName);
                    return match.Value;
                }

                used.Add(variableName);
                return value ?? string.Empty;
            });

            usedVariables = used.OrderBy(item => item).ToList();
            unknownVariables = unknown.OrderBy(item => item).ToList();
            return rendered;
        }

        private static string NormalizeTemplateVariableName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return string.Empty;
            }

            return rawName.Trim().ToLowerInvariant();
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
                case "scene_tags":
                    value = BuildSceneTagsVariableText(context);
                    return true;
                case "environment_params":
                    value = BuildEnvironmentParamsVariableText(context, envConfig);
                    return true;
                case "recent_world_events":
                    value = BuildRecentWorldEventsVariableText(context, envConfig);
                    return true;
                case "colony_status":
                    value = BuildColonyStatusVariableText();
                    return true;
                case "colony_factions":
                    value = BuildColonyFactionsVariableText();
                    return true;
                case "current_faction_profile":
                    value = BuildCurrentFactionProfileVariableText(context);
                    return true;
                case "rpg_target_profile":
                    value = BuildPawnProfileVariableText(context?.Target);
                    return true;
                case "rpg_initiator_profile":
                    value = BuildPawnProfileVariableText(context?.Initiator);
                    return true;
                case "player_pawn_profile":
                    value = BuildPlayerPawnProfileVariableText(context);
                    return true;
                case "player_royalty_summary":
                    value = BuildPlayerRoyaltySummaryVariableText(context);
                    return true;
                case "faction_settlement_summary":
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
