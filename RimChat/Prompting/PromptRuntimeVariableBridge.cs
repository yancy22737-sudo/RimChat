using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimChat.Memory;
using RimChat.Persistence;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.Prompting
{
    internal enum ReflectedCustomVariableKind
    {
        Context = 0,
        Environment = 1,
        Pawn = 2
    }

    internal sealed class ReflectedCustomVariable
    {
        public string LegacyName { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string ModId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ReflectedCustomVariableKind Kind { get; set; }

        public bool MatchesLegacyToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            string normalized = token.Trim();
            return string.Equals(normalized, LegacyName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "pawn." + LegacyName, StringComparison.OrdinalIgnoreCase);
        }

        public PromptRuntimeVariableDefinition ToDefinition(string sourceId, string sourceLabel)
        {
            string description = string.IsNullOrWhiteSpace(Description) ? Path : Description;
            return new PromptRuntimeVariableDefinition(Path, sourceId, sourceLabel, description, true);
        }
    }

    internal static class PromptRuntimeVariableBridge
    {
        private static readonly BindingFlags InstancePublic = BindingFlags.Instance | BindingFlags.Public;

        public static IReadOnlyList<PromptRuntimeVariableDefinition> GetBuiltinRimTalkDefinitions(string sourceId, string sourceLabel)
        {
            return new List<PromptRuntimeVariableDefinition>
            {
                new PromptRuntimeVariableDefinition("pawn.rimtalk.context", sourceId, sourceLabel, "RimChat_TemplateVar_rimtalk_context_Desc", true),
                new PromptRuntimeVariableDefinition("dialogue.rimtalk.prompt", sourceId, sourceLabel, "RimChat_TemplateVar_rimtalk_prompt_Desc", true),
                new PromptRuntimeVariableDefinition("dialogue.rimtalk.history", sourceId, sourceLabel, "RimChat_TemplateVar_rimtalk_history_Desc", true),
                new PromptRuntimeVariableDefinition("dialogue.rimtalk.history_simplified", sourceId, sourceLabel, "RimChat_TemplateVar_rimtalk_history_simplified_Desc", true)
            };
        }

        public static string GetDescriptionKey(string path)
        {
            switch (path)
            {
                case "ctx.channel":
                    return "RimChat_TemplateVar_ctx_channel_Desc";
                case "ctx.mode":
                    return "RimChat_TemplateVar_ctx_mode_Desc";
                case "system.target_language":
                    return "RimChat_TemplateVar_system_target_language_Desc";
                case "system.game_language":
                    return "RimChat_TemplateVar_system_game_language_Desc";
                case "world.time.hour":
                    return "RimChat_TemplateVar_world_time_hour_Desc";
                case "world.time.day":
                    return "RimChat_TemplateVar_world_time_day_Desc";
                case "world.time.quadrum":
                    return "RimChat_TemplateVar_world_time_quadrum_Desc";
                case "world.time.year":
                    return "RimChat_TemplateVar_world_time_year_Desc";
                case "world.time.season":
                    return "RimChat_TemplateVar_world_time_season_Desc";
                case "world.time.date":
                    return "RimChat_TemplateVar_world_time_date_Desc";
                case "world.weather":
                    return "RimChat_TemplateVar_world_weather_Desc";
                case "world.temperature":
                    return "RimChat_TemplateVar_world_temperature_Desc";
                case "world.faction.name":
                    return "RimChat_TemplateVar_world_faction_name_Desc";
                case "pawn.initiator.name":
                    return "RimChat_TemplateVar_pawn_initiator_name_Desc";
                case "pawn.initiator":
                    return "RimChat_TemplateVar_pawn_initiator_Desc";
                case "pawn.target.name":
                    return "RimChat_TemplateVar_pawn_target_name_Desc";
                case "pawn.target":
                    return "RimChat_TemplateVar_pawn_target_Desc";
                case "pawn.recipient":
                    return "RimChat_TemplateVar_pawn_recipient_Desc";
                case "pawn.recipient.name":
                    return "RimChat_TemplateVar_pawn_recipient_name_Desc";
                case "world.social.origin_type":
                    return "RimChat_TemplateVar_world_social_origin_type_Desc";
                case "world.social.category":
                    return "RimChat_TemplateVar_world_social_category_Desc";
                case "world.social.source_faction":
                    return "RimChat_TemplateVar_world_social_source_faction_Desc";
                case "world.social.target_faction":
                    return "RimChat_TemplateVar_world_social_target_faction_Desc";
                case "world.social.source_label":
                    return "RimChat_TemplateVar_world_social_source_label_Desc";
                case "world.social.credibility_label":
                    return "RimChat_TemplateVar_world_social_credibility_label_Desc";
                case "world.social.credibility_value":
                    return "RimChat_TemplateVar_world_social_credibility_value_Desc";
                case "world.social.fact_lines":
                    return "RimChat_TemplateVar_world_social_fact_lines_Desc";
                case "world.scene_tags":
                    return "RimChat_TemplateVar_scene_tags_Desc";
                case "world.environment_params":
                    return "RimChat_TemplateVar_environment_params_Desc";
                case "world.recent_world_events":
                    return "RimChat_TemplateVar_recent_world_events_Desc";
                case "world.colony_status":
                    return "RimChat_TemplateVar_colony_status_Desc";
                case "world.colony_factions":
                    return "RimChat_TemplateVar_colony_factions_Desc";
                case "world.current_faction_profile":
                    return "RimChat_TemplateVar_current_faction_profile_Desc";
                case "pawn.target.profile":
                    return "RimChat_TemplateVar_rpg_target_profile_Desc";
                case "pawn.initiator.profile":
                    return "RimChat_TemplateVar_rpg_initiator_profile_Desc";
                case "pawn.player.profile":
                    return "RimChat_TemplateVar_player_pawn_profile_Desc";
                case "pawn.player.royalty_summary":
                    return "RimChat_TemplateVar_player_royalty_summary_Desc";
                case "pawn.profile":
                    return "RimChat_TemplateVar_pawn_profile_Desc";
                case "pawn.personality":
                    return "RimChat_TemplateVar_pawn_personality_Desc";
                case "pawn.relation.kinship":
                    return "RimChat_TemplateVar_pawn_relation_kinship_Desc";
                case "pawn.relation.romance_state":
                    return "RimChat_TemplateVar_pawn_relation_romance_state_Desc";
                case "pawn.speaker.kind":
                    return "RimChat_TemplateVar_pawn_speaker_kind_Desc";
                case "pawn.speaker.default_sound":
                    return "RimChat_TemplateVar_pawn_speaker_default_sound_Desc";
                case "pawn.speaker.animal_sound":
                    return "RimChat_TemplateVar_pawn_speaker_animal_sound_Desc";
                case "pawn.speaker.baby_sound":
                    return "RimChat_TemplateVar_pawn_speaker_baby_sound_Desc";
                case "pawn.speaker.mechanoid_sound":
                    return "RimChat_TemplateVar_pawn_speaker_mechanoid_sound_Desc";
                case "pawn.pronouns.subject":
                    return "RimChat_TemplateVar_pawn_pronouns_subject_Desc";
                case "pawn.pronouns.object":
                    return "RimChat_TemplateVar_pawn_pronouns_object_Desc";
                case "pawn.pronouns.possessive":
                    return "RimChat_TemplateVar_pawn_pronouns_possessive_Desc";
                case "pawn.pronouns.subject_lower":
                    return "RimChat_TemplateVar_pawn_pronouns_subject_lower_Desc";
                case "pawn.pronouns.be_verb":
                    return "RimChat_TemplateVar_pawn_pronouns_be_verb_Desc";
                case "pawn.pronouns.seek_verb":
                    return "RimChat_TemplateVar_pawn_pronouns_seek_verb_Desc";
                case "world.faction_settlement_summary":
                    return "RimChat_TemplateVar_faction_settlement_summary_Desc";
                case "dialogue.summary":
                    return "RimChat_TemplateVar_dialogue_summary_Desc";
                case "dialogue.guidance":
                    return "RimChat_TemplateVar_dialogue_guidance_Desc";
                case "dialogue.intent_hint":
                    return "RimChat_TemplateVar_dialogue_intent_hint_Desc";
                case "dialogue.template_line":
                    return "RimChat_TemplateVar_dialogue_template_line_Desc";
                case "dialogue.example_line":
                    return "RimChat_TemplateVar_dialogue_example_line_Desc";
                case "dialogue.examples":
                    return "RimChat_TemplateVar_dialogue_examples_Desc";
                case "dialogue.action_names":
                    return "RimChat_TemplateVar_dialogue_action_names_Desc";
                case "dialogue.primary_objective":
                    return "RimChat_TemplateVar_dialogue_primary_objective_Desc";
                case "dialogue.optional_followup":
                    return "RimChat_TemplateVar_dialogue_optional_followup_Desc";
                case "dialogue.latest_unresolved_intent":
                    return "RimChat_TemplateVar_dialogue_latest_unresolved_intent_Desc";
                case "dialogue.topic_shift_rule":
                    return "RimChat_TemplateVar_dialogue_topic_shift_rule_Desc";
                case "dialogue.api_limits_body":
                    return "RimChat_TemplateVar_dialogue_api_limits_body_Desc";
                case "dialogue.quest_guidance_body":
                    return "RimChat_TemplateVar_dialogue_quest_guidance_body_Desc";
                case "dialogue.response_contract_body":
                    return "RimChat_TemplateVar_dialogue_response_contract_body_Desc";
                case "dialogue.strategy_player_negotiator_context_body":
                    return "RimChat_TemplateVar_dialogue_strategy_player_negotiator_context_body_Desc";
                case "dialogue.strategy_fact_pack_body":
                    return "RimChat_TemplateVar_dialogue_strategy_fact_pack_body_Desc";
                case "dialogue.strategy_scenario_dossier_body":
                    return "RimChat_TemplateVar_dialogue_strategy_scenario_dossier_body_Desc";
                case "pawn.rimtalk.context":
                    return "RimChat_TemplateVar_rimtalk_context_Desc";
                case "dialogue.rimtalk.prompt":
                    return "RimChat_TemplateVar_rimtalk_prompt_Desc";
                case "dialogue.rimtalk.history":
                    return "RimChat_TemplateVar_rimtalk_history_Desc";
                case "dialogue.rimtalk.history_simplified":
                    return "RimChat_TemplateVar_rimtalk_history_simplified_Desc";
                case "system.punctuation.open_paren":
                    return "RimChat_TemplateVar_system_punctuation_open_paren_Desc";
                case "system.punctuation.close_paren":
                    return "RimChat_TemplateVar_system_punctuation_close_paren_Desc";
                default:
                    return string.Empty;
            }
        }

        public static bool IsDependencyAvailable(string token)
        {
            return LoadedModManager.RunningModsListForReading != null &&
                   LoadedModManager.RunningModsListForReading.Exists(mod =>
                       mod != null &&
                       (ContainsToken(mod.PackageIdPlayerFacing, token) || ContainsToken(mod.Name, token)));
        }

        public static List<ReflectedCustomVariable> GetCustomVariables()
        {
            var results = new List<ReflectedCustomVariable>();
            Type registryType = AccessTools.TypeByName("RimTalk.API.ContextHookRegistry");
            MethodInfo method = registryType == null ? null : AccessTools.Method(registryType, "GetAllCustomVariables");
            if (method == null)
            {
                return results;
            }

            try
            {
                if (!(method.Invoke(null, null) is IEnumerable values))
                {
                    return results;
                }

                foreach (object item in values)
                {
                    ReflectedCustomVariable variable = ParseCustomVariable(item);
                    if (variable != null)
                    {
                        results.Add(variable);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to enumerate RimTalk custom variables: {ex.Message}");
            }

            return results;
        }

        public static bool TryResolveCustomVariableValue(ReflectedCustomVariable variable, PromptRuntimeVariableContext context, out string value)
        {
            value = string.Empty;
            if (variable == null)
            {
                return false;
            }

            if (variable.Kind == ReflectedCustomVariableKind.Pawn)
            {
                return InvokeOutString("TryGetPawnVariable", variable.LegacyName, ResolvePrimaryPawn(context), out value);
            }

            if (variable.Kind == ReflectedCustomVariableKind.Environment)
            {
                return InvokeOutString("TryGetEnvironmentVariable", variable.LegacyName, ResolveMap(context), out value);
            }

            return InvokeOutString("TryGetContextVariable", variable.LegacyName, BuildPromptContextInstance(context), out value);
        }

        public static string BuildRimTalkContextBlock(PromptRuntimeVariableContext context)
        {
            DialogueScenarioContext scenario = context?.ScenarioContext;
            Pawn pawn = ResolvePrimaryPawn(context);
            if (pawn == null)
            {
                return scenario?.Faction?.Name ?? string.Empty;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Pawn: {pawn.LabelShortCap}");
            if (pawn.Faction != null)
            {
                sb.AppendLine("Faction: " + pawn.Faction.Name);
            }

            if (pawn.story?.traits?.allTraits != null && pawn.story.traits.allTraits.Count > 0)
            {
                string traits = string.Join(", ", pawn.story.traits.allTraits.Take(4).Select(item => item?.LabelCap).Where(item => !string.IsNullOrWhiteSpace(item)));
                if (!string.IsNullOrWhiteSpace(traits))
                {
                    sb.AppendLine("Traits: " + traits);
                }
            }

            if (pawn.CurJob != null)
            {
                sb.AppendLine("Job: " + (pawn.CurJob.def?.label ?? string.Empty));
            }

            return sb.ToString().Trim();
        }

        public static string BuildRimTalkPromptBlock(PromptRuntimeVariableContext context)
        {
            var parts = new List<string>();
            string contextBlock = BuildRimTalkContextBlock(context);
            if (!string.IsNullOrWhiteSpace(contextBlock))
            {
                parts.Add(contextBlock);
            }

            Map map = ResolveMap(context);
            if (map != null)
            {
                int ticks = Find.TickManager?.TicksGame ?? 0;
                float longitude = Find.WorldGrid == null ? 0f : Find.WorldGrid.LongLatOf(map.Tile).x;
                parts.Add(
                    $"Time: {GenDate.HourOfDay(ticks, longitude)}h, day {GenDate.DayOfQuadrum(ticks, longitude) + 1}, " +
                    $"{GenDate.Quadrum(ticks, longitude).Label()}, year {GenDate.Year(ticks, longitude)}.");
                parts.Add($"Weather: {map.weatherManager?.curWeather?.label ?? "unknown"}, temperature {Mathf.RoundToInt(map.mapTemperature.OutdoorTemp)}C.");
            }

            string history = BuildRimTalkHistoryBlock(context, true);
            if (!string.IsNullOrWhiteSpace(history))
            {
                parts.Add(history);
            }

            return string.Join("\n", parts.Where(item => !string.IsNullOrWhiteSpace(item))).Trim();
        }

        public static string BuildRimTalkHistoryBlock(PromptRuntimeVariableContext context, bool simplified)
        {
            DialogueScenarioContext scenario = context?.ScenarioContext;
            if (scenario == null)
            {
                return string.Empty;
            }

            string text = string.Empty;
            if (scenario.IsRpg && scenario.Target != null)
            {
                text = RpgNpcDialogueArchiveManager.Instance.BuildPromptMemoryBlock(
                    scenario.Target,
                    scenario.Initiator,
                    simplified ? 4 : 8,
                    simplified ? 420 : 900);
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                text = DialogueSummaryService.BuildRpgDynamicFactionMemoryBlock(scenario.Faction, scenario.Target ?? scenario.Initiator);
            }

            text = NormalizeWhitespace(text);
            if (!simplified || text.Length <= 360)
            {
                return text;
            }

            return text.Substring(0, 360).TrimEnd() + "...";
        }

        public static string GetJsonInstruction()
        {
            try
            {
                Type constantType = AccessTools.TypeByName("RimTalk.Data.Constant");
                MethodInfo method = constantType == null ? null : AccessTools.Method(constantType, "GetJsonInstruction", new[] { typeof(bool) });
                if (method == null)
                {
                    return GetFallbackJsonInstruction();
                }

                bool includeSocialEffects = false;
                Type settingsType = AccessTools.TypeByName("RimTalk.Settings");
                MethodInfo getMethod = settingsType == null ? null : AccessTools.Method(settingsType, "Get");
                object settings = getMethod?.Invoke(null, null);
                PropertyInfo property = settings?.GetType().GetProperty("ApplyMoodAndSocialEffects", InstancePublic);
                if (property != null && property.PropertyType == typeof(bool))
                {
                    includeSocialEffects = (bool)property.GetValue(settings, null);
                }

                return method.Invoke(null, new object[] { includeSocialEffects })?.ToString() ?? GetFallbackJsonInstruction();
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to query RimTalk JSON instruction: {ex.Message}");
                return GetFallbackJsonInstruction();
            }
        }

        public static bool ContainsToken(string value, string token)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static ReflectedCustomVariable ParseCustomVariable(object item)
        {
            if (item == null)
            {
                return null;
            }

            string name = ReadTupleValue(item, "Item1");
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            ReflectedCustomVariableKind kind = ParseKind(ReadTupleValue(item, "Item4"));
            string normalizedName = NormalizeLegacyName(name, kind);
            return new ReflectedCustomVariable
            {
                LegacyName = normalizedName,
                Path = BuildNamespacedPath(normalizedName, kind),
                ModId = ReadTupleValue(item, "Item2"),
                Description = ReadTupleValue(item, "Item3"),
                Kind = kind
            };
        }

        private static string BuildNamespacedPath(string name, ReflectedCustomVariableKind kind)
        {
            switch (kind)
            {
                case ReflectedCustomVariableKind.Pawn:
                    return "pawn.rimtalk." + name;
                case ReflectedCustomVariableKind.Environment:
                    return "world.rimtalk." + name;
                default:
                    return "dialogue.rimtalk." + name;
            }
        }

        private static string NormalizeLegacyName(string name, ReflectedCustomVariableKind kind)
        {
            string normalized = (name ?? string.Empty).Trim().Replace(" ", "_");
            if (kind == ReflectedCustomVariableKind.Pawn &&
                normalized.StartsWith("pawn.", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("pawn.".Length);
            }

            return normalized;
        }

        private static ReflectedCustomVariableKind ParseKind(string raw)
        {
            if (string.Equals(raw, "Pawn", StringComparison.OrdinalIgnoreCase))
            {
                return ReflectedCustomVariableKind.Pawn;
            }

            if (string.Equals(raw, "Environment", StringComparison.OrdinalIgnoreCase))
            {
                return ReflectedCustomVariableKind.Environment;
            }

            return ReflectedCustomVariableKind.Context;
        }

        private static string ReadTupleValue(object item, string memberName)
        {
            Type type = item.GetType();
            FieldInfo field = type.GetField(memberName, InstancePublic);
            if (field != null)
            {
                return field.GetValue(item)?.ToString() ?? string.Empty;
            }

            PropertyInfo property = type.GetProperty(memberName, InstancePublic);
            return property?.GetValue(item, null)?.ToString() ?? string.Empty;
        }

        private static bool InvokeOutString(string methodName, string variableName, object arg, out string value)
        {
            value = string.Empty;
            Type registryType = AccessTools.TypeByName("RimTalk.API.ContextHookRegistry");
            MethodInfo method = registryType == null ? null : AccessTools.Method(registryType, methodName);
            if (method == null)
            {
                return false;
            }

            object[] args = { variableName, arg, null };
            bool success = (bool)method.Invoke(null, args);
            value = args[2]?.ToString() ?? string.Empty;
            return success;
        }

        private static object BuildPromptContextInstance(PromptRuntimeVariableContext context)
        {
            Type promptContextType = AccessTools.TypeByName("RimTalk.Prompt.PromptContext");
            if (promptContextType == null)
            {
                return null;
            }

            try
            {
                object instance = Activator.CreateInstance(promptContextType);
                DialogueScenarioContext scenario = context?.ScenarioContext;
                Pawn pawn = ResolvePrimaryPawn(context);
                List<Pawn> pawns = new List<Pawn>();
                if (scenario?.Initiator != null)
                {
                    pawns.Add(scenario.Initiator);
                }

                if (scenario?.Target != null && !pawns.Contains(scenario.Target))
                {
                    pawns.Add(scenario.Target);
                }

                SetProperty(instance, "CurrentPawn", pawn);
                SetProperty(instance, "AllPawns", pawns);
                SetProperty(instance, "Map", ResolveMap(context));
                SetProperty(instance, "PawnContext", BuildRimTalkContextBlock(context));
                SetProperty(instance, "DialoguePrompt", BuildRimTalkPromptBlock(context));
                SetProperty(instance, "DialogueType", scenario?.IsRpg == true ? "conversation" : "diplomacy");
                SetProperty(instance, "DialogueStatus", scenario?.IsProactive == true ? "proactive" : "manual");
                SetProperty(instance, "IsPreview", false);

                Type variableStoreType = AccessTools.TypeByName("RimTalk.Prompt.VariableStore");
                if (variableStoreType != null)
                {
                    SetProperty(instance, "VariableStore", Activator.CreateInstance(variableStoreType));
                }

                return instance;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to build reflected RimTalk PromptContext: {ex.Message}");
                return null;
            }
        }

        private static void SetProperty(object target, string name, object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            PropertyInfo property = target.GetType().GetProperty(name, InstancePublic);
            if (property == null || !property.CanWrite)
            {
                return;
            }

            if (value == null)
            {
                if (!property.PropertyType.IsValueType)
                {
                    property.SetValue(target, null, null);
                }

                return;
            }

            if (property.PropertyType.IsInstanceOfType(value))
            {
                property.SetValue(target, value, null);
            }
        }

        private static Pawn ResolvePrimaryPawn(PromptRuntimeVariableContext context)
        {
            return context?.ScenarioContext?.Target ?? context?.ScenarioContext?.Initiator;
        }

        private static Map ResolveMap(PromptRuntimeVariableContext context)
        {
            Pawn pawn = ResolvePrimaryPawn(context);
            return pawn?.MapHeld ?? Find.CurrentMap;
        }

        private static string NormalizeWhitespace(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return string.Join(" ",
                text.Replace("\r\n", "\n")
                    .Replace('\r', '\n')
                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim())
                    .Where(item => item.Length > 0));
        }

        private static string GetFallbackJsonInstruction()
        {
            return "Output gameplay effects only as a trailing {\"actions\":[...]} JSON object. Omit the JSON block when no action is needed.";
        }
    }
}
