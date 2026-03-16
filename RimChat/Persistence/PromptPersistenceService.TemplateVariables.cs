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

        private static readonly IReadOnlyList<PromptTemplateVariableDefinition> TemplateVariableDefinitions =
            PromptVariableCatalog.GetDefinitions()
                .Where(item => item != null)
                .Select(item => item.ToTemplateDefinition())
                .ToList();

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
            renderContext.SetValues(BuildTemplateVariableValues(templateId, channel, context, envConfig));
            return renderContext;
        }

        private Dictionary<string, object> BuildTemplateVariableValues(
            string templateId,
            string channel,
            DialogueScenarioContext context,
            EnvironmentPromptConfig envConfig)
        {
            var values = CreatePromptVariableSeed();
            var variableContext = new PromptRuntimeVariableContext(templateId, channel, context, envConfig);
            List<IPromptRuntimeVariableProvider> providers = PromptRuntimeVariableRegistry.CreateRuntimeProviders(
                (path, runtimeContext) => ResolveTemplateVariableValue(path, runtimeContext.ScenarioContext, runtimeContext.EnvironmentConfig));
            for (int i = 0; i < providers.Count; i++)
            {
                IPromptRuntimeVariableProvider provider = providers[i];
                if (provider == null || !provider.IsAvailable(variableContext))
                {
                    continue;
                }

                provider.PopulateValues(values, variableContext);
            }

            values["system.game_language"] = LanguageDatabase.activeLanguage?.FriendlyNameNative
                ?? (RimChatMod.Settings?.GetEffectivePromptLanguage() ?? string.Empty);
            values["pawn.initiator"] = context?.Initiator;
            values["pawn.target"] = context?.Target;
            values["world.faction"] = context?.Faction;
            values["dialogue.diplomacy_dialogue.system_rules"] = PromptEntryStaticTextCatalog.DiplomacyDialogueRequest.SystemRules;
            values["dialogue.diplomacy_dialogue.character_persona"] = PromptEntryStaticTextCatalog.DiplomacyDialogueRequest.CharacterPersona;
            values["dialogue.diplomacy_dialogue.memory_system"] = PromptEntryStaticTextCatalog.DiplomacyDialogueRequest.MemorySystem;
            values["dialogue.diplomacy_dialogue.environment_perception"] = PromptEntryStaticTextCatalog.DiplomacyDialogueRequest.EnvironmentPerception;
            values["dialogue.diplomacy_dialogue.context"] = PromptEntryStaticTextCatalog.DiplomacyDialogueRequest.Context;
            values["dialogue.diplomacy_dialogue.action_rules"] = PromptEntryStaticTextCatalog.DiplomacyDialogueRequest.ActionRules;
            values["dialogue.diplomacy_dialogue.repetition_reinforcement"] = PromptEntryStaticTextCatalog.DiplomacyDialogueRequest.RepetitionReinforcement;
            values["dialogue.diplomacy_dialogue.output_specification"] = PromptEntryStaticTextCatalog.DiplomacyDialogueRequest.OutputSpecification;
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

        private object ResolveTemplateVariableValue(
            string variableName,
            DialogueScenarioContext context,
            EnvironmentPromptConfig envConfig)
        {
            switch (variableName)
            {
                case "ctx.channel":
                    return context?.IsRpg == true ? "rpg" : "diplomacy";
                case "ctx.mode":
                    return context?.IsProactive == true ? "proactive" : "manual";
                case "system.target_language":
                    return RimChatMod.Settings?.GetEffectivePromptLanguage() ?? string.Empty;
                case "world.faction.name":
                    return context?.Faction?.Name ?? "Unknown Faction";
                case "pawn.initiator.name":
                    return context?.Initiator?.LabelShort ?? "Unknown";
                case "pawn.target.name":
                    return context?.Target?.LabelShort ?? "Unknown";
                case "world.scene_tags":
                    return BuildSceneTagsVariableText(context);
                case "world.environment_params":
                    return BuildEnvironmentParamsVariableText(context, envConfig);
                case "world.recent_world_events":
                    return BuildRecentWorldEventsVariableText(context, envConfig);
                case "world.colony_status":
                    return BuildColonyStatusVariableText();
                case "world.colony_factions":
                    return BuildColonyFactionsVariableText();
                case "world.current_faction_profile":
                    return BuildCurrentFactionProfileVariableText(context);
                case "pawn.target.profile":
                    return BuildPawnProfileVariableText(context?.Target);
                case "pawn.initiator.profile":
                    return BuildPawnProfileVariableText(context?.Initiator);
                case "pawn.player.profile":
                    return BuildPlayerPawnProfileVariableText(context);
                case "pawn.player.royalty_summary":
                    return BuildPlayerRoyaltySummaryVariableText(context);
                case "world.faction_settlement_summary":
                    return BuildFactionSettlementSummaryVariableText(context);
                default:
                    return null;
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
