using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimChat.Core;
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
        private static readonly BindingFlags StaticPublic = BindingFlags.Static | BindingFlags.Public;
        private static readonly object LegacyCleanupSyncRoot = new object();
        private static readonly string[] LegacyRimChatContextVariableKeys =
        {
            "rimchat_last_session_summary",
            "rimchat_last_diplomacy_summary",
            "rimchat_last_rpg_summary",
            "rimchat_recent_session_summaries"
        };

        private static readonly string[] VariableRemovalMethods =
        {
            "TryRemoveContextVariable",
            "RemoveContextVariable",
            "DeleteContextVariable",
            "TryDeleteContextVariable",
            "ClearContextVariable",
            "TryClearContextVariable"
        };

        private static readonly string[] VariableSetterMethods =
        {
            "TrySetContextVariable",
            "SetContextVariable"
        };

        private const string RimChatSummaryVariableName = "rimchat_summary";
        private const int RimChatSummaryMaxChars = 1200;
        private const int RimChatSummaryVariablePriority = 100;

        private static readonly object BridgeInitSyncRoot = new object();
        private static readonly object CustomVariableSnapshotSyncRoot = new object();
        private static readonly object CustomVariableRefreshSyncRoot = new object();
        private static readonly List<ReflectedCustomVariable> CustomVariableSnapshot = new List<ReflectedCustomVariable>();
        private const int CustomVariableRefreshCooldownMs = 1000;
        private static readonly string[] KnownRimChatModIds =
        {
            "yancy.rimchat",
            "rimchat",
            "rim_chat",
            "timchat"
        };

        private static bool _legacyCleanupAttempted;
        private static bool _bridgeInitAttempted;
        private static bool _bridgeRuntimeAvailable;
        private static string _bridgeFailureReason = string.Empty;
        private static int _lastCustomVariableRefreshTick = -1;
        private static string _lastCustomVariableTelemetry = string.Empty;

        public static void InitializeBridgeChain()
        {
            if (_bridgeInitAttempted)
            {
                return;
            }

            lock (BridgeInitSyncRoot)
            {
                if (_bridgeInitAttempted)
                {
                    return;
                }

                _bridgeInitAttempted = true;
                if (!IsDependencyAvailable("rimtalk"))
                {
                    _bridgeRuntimeAvailable = false;
                    _bridgeFailureReason = "RimTalk dependency not detected.";
                    return;
                }

                try
                {
                    StrictLegacyCleanup();
                    ValidateRimTalkBridgeSignaturesOrFail();
                    RegisterRimChatSummaryVariable();
                    _bridgeRuntimeAvailable = true;
                    _bridgeFailureReason = string.Empty;
                    RefreshRimTalkCustomVariableSnapshot(force: true);
                }
                catch (Exception ex)
                {
                    _bridgeRuntimeAvailable = false;
                    _bridgeFailureReason = ex.Message ?? "Bridge initialization failed.";
                    Log.Error($"[RimChat] RimTalk bridge initialization failed. Bridge chain blocked: {_bridgeFailureReason}");
                }
            }
        }

        public static string GetBridgeFailureReason()
        {
            return _bridgeFailureReason ?? string.Empty;
        }

        public static void ValidateRimTalkBridgeSignaturesOrFail()
        {
            Type registryType = AccessTools.TypeByName("RimTalk.API.ContextHookRegistry");
            if (registryType == null)
            {
                throw new MissingMemberException("Missing type RimTalk.API.ContextHookRegistry.");
            }

            ValidateRequiredMethod(registryType, "RegisterContextVariable");
            ValidateRequiredMethod(registryType, "GetAllCustomVariables");
            ValidateRequiredMethod(registryType, "UnregisterMod");
            ValidateRequiredMethod(registryType, "TryGetContextVariable");

            Type promptContextType = AccessTools.TypeByName("RimTalk.Prompt.PromptContext");
            if (promptContextType == null)
            {
                throw new MissingMemberException("Missing type RimTalk.Prompt.PromptContext.");
            }

            Type variableStoreType = AccessTools.TypeByName("RimTalk.Prompt.VariableStore");
            if (variableStoreType == null)
            {
                throw new MissingMemberException("Missing type RimTalk.Prompt.VariableStore.");
            }
        }

        public static void StrictLegacyCleanup()
        {
            TryCleanupLegacyRimChatVariables(force: true);
        }

        public static IReadOnlyList<ReflectedCustomVariable> GetRimTalkCustomVariablesSnapshot()
        {
            lock (CustomVariableSnapshotSyncRoot)
            {
                return CustomVariableSnapshot.Select(CloneVariable).ToList();
            }
        }

        public static void RefreshRimTalkCustomVariableSnapshot(bool force = false)
        {
            if (!_bridgeInitAttempted)
            {
                InitializeBridgeChain();
            }

            if (!_bridgeRuntimeAvailable)
            {
                return;
            }

            if (ShouldThrottleCustomVariableRefresh(force))
            {
                return;
            }

            lock (CustomVariableRefreshSyncRoot)
            {
                if (ShouldThrottleCustomVariableRefresh(force))
                {
                    return;
                }

                Type registryType = AccessTools.TypeByName("RimTalk.API.ContextHookRegistry");
                MethodInfo method = registryType == null ? null : AccessTools.Method(registryType, "GetAllCustomVariables");
                if (method == null)
                {
                    return;
                }

                int rawCount = 0;
                int duplicateCount = 0;
                string sampleType = string.Empty;
                var results = new List<ReflectedCustomVariable>();
                var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (method.Invoke(null, null) is IEnumerable values)
                    {
                        foreach (object item in values)
                        {
                            rawCount++;
                            if (string.IsNullOrWhiteSpace(sampleType) && item != null)
                            {
                                sampleType = item.GetType().FullName ?? item.GetType().Name;
                            }

                            ReflectedCustomVariable variable = ParseCustomVariable(item);
                            if (variable == null || string.IsNullOrWhiteSpace(variable.Path))
                            {
                                continue;
                            }

                            if (!uniquePaths.Add(variable.Path))
                            {
                                duplicateCount++;
                                continue;
                            }

                            results.Add(variable);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimChat] Failed to refresh RimTalk custom variable snapshot: {ex.Message}");
                    return;
                }

                if (rawCount > 0 && results.Count == 0)
                {
                    BlockBridgeBySnapshotContractMismatch(rawCount, sampleType);
                    return;
                }

                lock (CustomVariableSnapshotSyncRoot)
                {
                    CustomVariableSnapshot.Clear();
                    CustomVariableSnapshot.AddRange(results);
                    _lastCustomVariableRefreshTick = Environment.TickCount;
                }

                LogCustomVariableSnapshotTelemetry(rawCount, results.Count, duplicateCount, force);
            }
        }

        public static string BuildModVariablesSectionContent()
        {
            List<ReflectedCustomVariable> customVariables = GetCustomVariables()
                .Where(item => item != null)
                .OrderBy(item => item.LegacyName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (customVariables.Count == 0)
            {
                return string.Empty;
            }

            var lines = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < customVariables.Count; i++)
            {
                string token = ResolveRawTokenFromVariable(customVariables[i]);
                if (string.IsNullOrWhiteSpace(token) || !seen.Add(token))
                {
                    continue;
                }

                lines.Add(token);
            }

            return lines.Count == 0 ? string.Empty : string.Join("\n", lines);
        }

        public static string ResolveRawToken(string variablePath)
        {
            string normalized = variablePath?.Trim() ?? string.Empty;
            switch (normalized)
            {
                case "pawn.rimtalk.context":
                    return "{{ context }}";
                case "dialogue.rimtalk.prompt":
                    return "{{ prompt }}";
                case "dialogue.rimtalk.history":
                    return "{{ chat.history }}";
                case "dialogue.rimtalk.history_simplified":
                    return "{{ chat.history_simplified }}";
            }

            ReflectedCustomVariable custom = GetCustomVariables()
                .FirstOrDefault(item => string.Equals(item.Path, normalized, StringComparison.OrdinalIgnoreCase));
            return custom == null
                ? "{{ " + normalized + " }}"
                : ResolveRawTokenFromVariable(custom);
        }

        public static void RegisterRimChatSummaryVariable()
        {
            Type registryType = AccessTools.TypeByName("RimTalk.API.ContextHookRegistry");
            MethodInfo registerMethod = registryType == null ? null : AccessTools.Method(registryType, "RegisterContextVariable");
            if (registerMethod == null)
            {
                throw new MissingMethodException("Missing ContextHookRegistry.RegisterContextVariable.");
            }

            ParameterInfo[] parameters = registerMethod.GetParameters();
            if (parameters.Length < 3)
            {
                throw new MissingMethodException("Unexpected RegisterContextVariable signature.");
            }

            Delegate provider = BuildContextProviderDelegate(parameters[2].ParameterType);
            string modId = SanitizeModId(KnownRimChatModIds[0]);
            var args = new List<object>
            {
                RimChatSummaryVariableName,
                modId,
                provider
            };

            if (parameters.Length >= 4)
            {
                args.Add("RimChat cross-channel summary aggregate.");
            }

            if (parameters.Length >= 5)
            {
                args.Add(RimChatSummaryVariablePriority);
            }

            registerMethod.Invoke(null, args.ToArray());
        }

        public static string BuildRimChatSummaryAggregateText()
        {
            if (Current.Game == null || Find.FactionManager == null)
            {
                return string.Empty;
            }

            var lines = new List<string>();
            foreach (Faction faction in Find.FactionManager.AllFactionsListForReading)
            {
                if (faction == null || faction.IsPlayer || faction.defeated)
                {
                    continue;
                }

                FactionLeaderMemory memory = LeaderMemoryManager.Instance.GetMemory(faction);
                if (memory == null)
                {
                    continue;
                }

                IEnumerable<CrossChannelSummaryRecord> summaries = (memory.DiplomacySessionSummaries ?? new List<CrossChannelSummaryRecord>())
                    .Concat(memory.RpgDepartSummaries ?? new List<CrossChannelSummaryRecord>())
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.SummaryText))
                    .OrderByDescending(item => item.GameTick)
                    .Take(2);

                foreach (CrossChannelSummaryRecord summary in summaries)
                {
                    lines.Add($"[{faction.Name}] {summary.SummaryText.Trim()}");
                }
            }

            if (lines.Count == 0)
            {
                return string.Empty;
            }

            string content = string.Join("\n", lines);
            return TrimToBudget(content, RimChatSummaryMaxChars);
        }

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

        public static bool IsRimTalkBridgeEnabled()
        {
            InitializeBridgeChain();
            return _bridgeRuntimeAvailable;
        }

        public static void TryCleanupLegacyRimChatVariables(bool force = false)
        {
            if (ShouldSkipLegacyCleanup(force))
            {
                return;
            }

            lock (LegacyCleanupSyncRoot)
            {
                if (ShouldSkipLegacyCleanup(force))
                {
                    return;
                }

                _legacyCleanupAttempted = true;
                CleanupLegacyVariablesInternal();
            }
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
                case "world.faction.description":
                    return "RimChat_TemplateVar_world_faction_description_Desc";
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
                case "pawn.relation.social_summary":
                    return "RimChat_TemplateVar_pawn_relation_social_summary_Desc";
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
            InitializeBridgeChain();
            if (!_bridgeRuntimeAvailable)
            {
                return new List<ReflectedCustomVariable>();
            }

            RefreshRimTalkCustomVariableSnapshot();
            return GetRimTalkCustomVariablesSnapshot().ToList();
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
                bool allowMemoryCompressionScheduling = RpgPromptTurnContextScope.Current?.AllowMemoryCompressionScheduling ?? true;
                bool allowMemoryColdLoad = RpgPromptTurnContextScope.Current?.AllowMemoryColdLoad ?? true;
                text = RpgNpcDialogueArchiveManager.Instance.BuildPromptMemoryBlock(
                    scenario.Target,
                    scenario.Initiator,
                    simplified ? 4 : 8,
                    simplified ? 420 : 900,
                    allowCompressionScheduling: allowMemoryCompressionScheduling,
                    allowCacheLoad: allowMemoryColdLoad);
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

            string name = ReadCustomVariableField(item, "Item1", "VariableName", "Name", "LegacyName", "Key");
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            ReflectedCustomVariableKind kind = ParseKind(ReadCustomVariableField(item, "Item4", "Kind", "VariableKind", "Type", "Scope"));
            string normalizedName = NormalizeLegacyName(name, kind);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return null;
            }

            return new ReflectedCustomVariable
            {
                LegacyName = normalizedName,
                Path = BuildNamespacedPath(normalizedName, kind),
                ModId = ReadCustomVariableField(item, "Item2", "SourceModId", "ModId", "SourceId"),
                Description = ReadCustomVariableField(item, "Item3", "Description", "Desc", "Tooltip"),
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
            if (int.TryParse(raw, out int numericKind))
            {
                if (numericKind == 1)
                {
                    return ReflectedCustomVariableKind.Environment;
                }

                if (numericKind == 2)
                {
                    return ReflectedCustomVariableKind.Pawn;
                }
            }

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

        private static string ReadCustomVariableField(object item, params string[] memberNames)
        {
            if (item == null || memberNames == null || memberNames.Length == 0)
            {
                return string.Empty;
            }

            for (int i = 0; i < memberNames.Length; i++)
            {
                string value = ReadTupleValue(item, memberNames[i]);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
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

        private static bool ShouldThrottleCustomVariableRefresh(bool force)
        {
            if (force)
            {
                return false;
            }

            int now = Environment.TickCount;
            lock (CustomVariableSnapshotSyncRoot)
            {
                if (_lastCustomVariableRefreshTick < 0)
                {
                    return false;
                }

                uint elapsed = unchecked((uint)(now - _lastCustomVariableRefreshTick));
                return elapsed < CustomVariableRefreshCooldownMs;
            }
        }

        private static void BlockBridgeBySnapshotContractMismatch(int rawCount, string sampleType)
        {
            _bridgeRuntimeAvailable = false;
            _bridgeFailureReason = "RimTalk custom variable contract mismatch: no variable could be parsed.";
            lock (CustomVariableSnapshotSyncRoot)
            {
                CustomVariableSnapshot.Clear();
                _lastCustomVariableRefreshTick = Environment.TickCount;
            }

            string typeLabel = string.IsNullOrWhiteSpace(sampleType) ? "unknown" : sampleType;
            Log.Error(
                "[RimChat] Bridge blocked due to custom-variable contract mismatch. " +
                $"raw_count={rawCount}, sample_type={typeLabel}. " +
                "Please verify RimTalk GetAllCustomVariables() payload shape.");
        }

        private static void LogCustomVariableSnapshotTelemetry(
            int rawCount,
            int parsedCount,
            int duplicateCount,
            bool force)
        {
            string telemetry = $"{rawCount}|{parsedCount}|{duplicateCount}|{force}";
            if (!force && string.Equals(telemetry, _lastCustomVariableTelemetry, StringComparison.Ordinal))
            {
                return;
            }

            _lastCustomVariableTelemetry = telemetry;
            Log.Message(
                "[RimChat] RimTalk custom variable snapshot refreshed. " +
                $"raw_count={rawCount}, parsed_count={parsedCount}, duplicate_count={duplicateCount}, force={force}.");
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
            return "Output gameplay effects only as a trailing {\"actions\":[...]} JSON object. Omit the JSON block when no action is needed. Hard immersion rules for visible dialogue: start directly in-character and never begin with parenthetical notes/metadata (for example \"(重复问候...)\" or \"（状态说明...）\"); do not expose mechanism terms or system values (goodwill/threshold/cooldown/API/system prompt/token/requestId/api_limits/blocked actions); do not output status-panel sentence patterns such as key:123.";
        }

        private static bool TryRemoveLegacyContextVariable(Type registryType, string key)
        {
            if (registryType == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            return TryInvokeStringMethodCandidates(registryType, VariableRemovalMethods, key, string.Empty) ||
                   TryInvokeStringMethodCandidates(registryType, VariableSetterMethods, key, string.Empty);
        }

        private static bool TryInvokeStringMethodCandidates(
            Type registryType,
            IEnumerable<string> methodNames,
            string key,
            string value)
        {
            if (registryType == null || methodNames == null)
            {
                return false;
            }

            foreach (string methodName in methodNames)
            {
                if (TryInvokeMethodOverloads(registryType, methodName, key, value, out bool success))
                {
                    return success;
                }
            }

            return false;
        }

        private static bool TryInvokeMethodOverloads(
            Type registryType,
            string methodName,
            string key,
            string value,
            out bool success)
        {
            success = false;
            if (registryType == null || string.IsNullOrWhiteSpace(methodName))
            {
                return false;
            }

            MethodInfo[] methods = registryType.GetMethods(StaticPublic)
                .Where(item => string.Equals(item.Name, methodName, StringComparison.Ordinal))
                .ToArray();
            for (int i = 0; i < methods.Length; i++)
            {
                if (TryInvokeStringMethod(methods[i], key, value, out success))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryInvokeStringMethod(
            MethodInfo method,
            string key,
            string value,
            out bool success)
        {
            success = false;
            if (method == null)
            {
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();
            try
            {
                if (TryInvokeSingleStringParameterMethod(method, parameters, key, out success))
                {
                    return true;
                }

                if (TryInvokeTwoStringParameterMethod(method, parameters, key, value, out success))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to invoke RimTalk variable cleanup method '{method.Name}': {ex.Message}");
                return true;
            }

            return false;
        }

        private static bool ToBoolResult(object result)
        {
            return result is bool ok ? ok : true;
        }

        private static bool TryInvokeSingleStringParameterMethod(
            MethodInfo method,
            IReadOnlyList<ParameterInfo> parameters,
            string key,
            out bool success)
        {
            success = false;
            if (parameters.Count != 1 || parameters[0].ParameterType != typeof(string))
            {
                return false;
            }

            object result = method.Invoke(null, new object[] { key });
            success = ToBoolResult(result);
            return true;
        }

        private static bool TryInvokeTwoStringParameterMethod(
            MethodInfo method,
            IReadOnlyList<ParameterInfo> parameters,
            string key,
            string value,
            out bool success)
        {
            success = false;
            if (parameters.Count != 2 ||
                parameters[0].ParameterType != typeof(string) ||
                parameters[1].ParameterType != typeof(string))
            {
                return false;
            }

            object result = method.Invoke(null, new object[] { key, value ?? string.Empty });
            success = ToBoolResult(result);
            return true;
        }

        private static bool ShouldSkipLegacyCleanup(bool force)
        {
            return !force && _legacyCleanupAttempted;
        }

        private static void CleanupLegacyVariablesInternal()
        {
            if (!IsDependencyAvailable("rimtalk"))
            {
                return;
            }

            Type registryType = AccessTools.TypeByName("RimTalk.API.ContextHookRegistry");
            if (registryType == null)
            {
                return;
            }

            int removedContextKeys = 0;
            for (int i = 0; i < LegacyRimChatContextVariableKeys.Length; i++)
            {
                if (TryRemoveLegacyContextVariable(registryType, LegacyRimChatContextVariableKeys[i]))
                {
                    removedContextKeys++;
                }
            }

            int unregisteredMods = UnregisterLegacyRimChatMods(registryType);
            int removedRuntimeKeys = CleanupLegacyRuntimeVariableStoreKeys();
            (int removedEntries, int removedDeletedIds) = CleanupLegacyPromptPresetEntries();

            if (removedContextKeys > 0 ||
                unregisteredMods > 0 ||
                removedRuntimeKeys > 0 ||
                removedEntries > 0 ||
                removedDeletedIds > 0)
            {
                Log.Message(
                    "[RimChat] RimTalk strict legacy cleanup completed. " +
                    $"context_keys={removedContextKeys}, mods_unregistered={unregisteredMods}, " +
                    $"runtime_keys={removedRuntimeKeys}, preset_entries={removedEntries}, " +
                    $"deleted_mod_ids={removedDeletedIds}.");
            }
        }

        private static int UnregisterLegacyRimChatMods(Type registryType)
        {
            MethodInfo unregisterMethod = registryType == null ? null : AccessTools.Method(registryType, "UnregisterMod");
            if (unregisterMethod == null)
            {
                return 0;
            }

            int removed = 0;
            HashSet<string> modIds = CollectLegacyRimChatModIds();
            foreach (string modId in modIds)
            {
                try
                {
                    unregisterMethod.Invoke(null, new object[] { modId });
                    removed++;
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimChat] Failed to unregister legacy mod hooks for '{modId}': {ex.Message}");
                }
            }

            return removed;
        }

        private static int CleanupLegacyRuntimeVariableStoreKeys()
        {
            Type apiType = AccessTools.TypeByName("RimTalk.API.RimTalkPromptAPI");
            MethodInfo getStoreMethod = apiType == null ? null : AccessTools.Method(apiType, "GetVariableStore");
            object store = getStoreMethod?.Invoke(null, null);
            if (store == null)
            {
                return 0;
            }

            MethodInfo getAllMethod = AccessTools.Method(store.GetType(), "GetAllVariables");
            MethodInfo removeMethod = AccessTools.Method(store.GetType(), "RemoveVar");
            if (getAllMethod == null || removeMethod == null)
            {
                return 0;
            }

            var keys = new List<string>();
            object allVariables = getAllMethod.Invoke(store, null);
            if (allVariables is IDictionary dictionary)
            {
                foreach (object key in dictionary.Keys)
                {
                    keys.Add(key?.ToString() ?? string.Empty);
                }
            }
            else if (allVariables is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    string key = ReadMemberValue(item, "Key")?.ToString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        keys.Add(key);
                    }
                }
            }

            int removed = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                bool matchesLegacyKey = LegacyRimChatContextVariableKeys.Any(item =>
                    string.Equals(item, key, StringComparison.OrdinalIgnoreCase));
                if (!matchesLegacyKey && !ContainsToken(key, "rimchat"))
                {
                    continue;
                }

                try
                {
                    object result = removeMethod.Invoke(store, new object[] { key });
                    if (ToBoolResult(result))
                    {
                        removed++;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimChat] Failed to remove legacy runtime key '{key}': {ex.Message}");
                }
            }

            return removed;
        }

        private static (int removedEntries, int removedDeletedIds) CleanupLegacyPromptPresetEntries()
        {
            Type apiType = AccessTools.TypeByName("RimTalk.API.RimTalkPromptAPI");
            MethodInfo getAllPresetsMethod = apiType == null ? null : AccessTools.Method(apiType, "GetAllPresets");
            object presets = getAllPresetsMethod?.Invoke(null, null);
            if (!(presets is IEnumerable enumerable))
            {
                return (0, 0);
            }

            int removedEntries = 0;
            int removedDeletedIds = 0;
            foreach (object preset in enumerable)
            {
                if (preset == null)
                {
                    continue;
                }

                object entries = ReadMemberValue(preset, "Entries");
                if (entries is IList list)
                {
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        object entry = list[i];
                        string sourceModId = ReadMemberValue(entry, "SourceModId")?.ToString() ?? string.Empty;
                        if (!ContainsToken(sourceModId, "rimchat"))
                        {
                            continue;
                        }

                        list.RemoveAt(i);
                        removedEntries++;
                    }
                }

                object deletedCollection = ReadMemberValue(preset, "DeletedModEntryIds");
                removedDeletedIds += RemoveMatchingDeletedEntryIds(deletedCollection, value => ContainsToken(value, "rimchat"));
            }

            return (removedEntries, removedDeletedIds);
        }

        private static int RemoveMatchingDeletedEntryIds(object collection, Func<string, bool> predicate)
        {
            if (collection == null || predicate == null)
            {
                return 0;
            }

            var targets = new List<string>();
            if (collection is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    string text = item?.ToString() ?? string.Empty;
                    if (predicate(text))
                    {
                        targets.Add(text);
                    }
                }
            }

            MethodInfo removeMethod = AccessTools.Method(collection.GetType(), "Remove");
            if (removeMethod == null || targets.Count == 0)
            {
                return 0;
            }

            int removed = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                object result = removeMethod.Invoke(collection, new object[] { targets[i] });
                if (ToBoolResult(result))
                {
                    removed++;
                }
            }

            return removed;
        }

        private static HashSet<string> CollectLegacyRimChatModIds()
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < KnownRimChatModIds.Length; i++)
            {
                string id = KnownRimChatModIds[i];
                if (!string.IsNullOrWhiteSpace(id))
                {
                    ids.Add(id.Trim());
                    ids.Add(SanitizeModId(id));
                }
            }

            if (LoadedModManager.RunningModsListForReading == null)
            {
                return ids;
            }

            foreach (ModContentPack mod in LoadedModManager.RunningModsListForReading)
            {
                string packageId = mod?.PackageIdPlayerFacing ?? string.Empty;
                if (!ContainsToken(packageId, "rimchat"))
                {
                    continue;
                }

                ids.Add(packageId.Trim());
                ids.Add(SanitizeModId(packageId));
            }

            return ids;
        }

        private static void ValidateRequiredMethod(Type type, string methodName)
        {
            if (type == null || string.IsNullOrWhiteSpace(methodName))
            {
                throw new MissingMethodException("Invalid method validation request.");
            }

            MethodInfo method = AccessTools.Method(type, methodName);
            if (method == null)
            {
                throw new MissingMethodException($"Missing {type.FullName}.{methodName}.");
            }
        }

        private static Delegate BuildContextProviderDelegate(Type delegateType)
        {
            Type targetType = delegateType == typeof(Delegate) ? typeof(Func<object, string>) : delegateType;
            MethodInfo invokeMethod = targetType.GetMethod("Invoke");
            if (invokeMethod == null)
            {
                throw new MissingMethodException("Unable to build context provider delegate.");
            }

            ParameterInfo[] parameters = invokeMethod.GetParameters();
            if (parameters.Length != 1 || invokeMethod.ReturnType != typeof(string))
            {
                throw new MissingMethodException("Unsupported RegisterContextVariable delegate signature.");
            }

            ParameterExpression contextParameter = Expression.Parameter(parameters[0].ParameterType, "ctx");
            MethodInfo callback = AccessTools.Method(typeof(PromptRuntimeVariableBridge), nameof(BuildRimChatSummaryAggregateText));
            MethodCallExpression body = Expression.Call(callback);
            return Expression.Lambda(targetType, body, contextParameter).Compile();
        }

        private static object ReadMemberValue(object target, string name)
        {
            if (target == null || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(name, InstancePublic);
            if (property != null)
            {
                return property.GetValue(target, null);
            }

            FieldInfo field = type.GetField(name, InstancePublic);
            return field?.GetValue(target);
        }

        private static string ResolveRawTokenFromVariable(ReflectedCustomVariable variable)
        {
            if (variable == null || string.IsNullOrWhiteSpace(variable.Path))
            {
                return string.Empty;
            }

            string rawName = variable.LegacyName?.Trim() ?? string.Empty;
            if (rawName.Length == 0)
            {
                return "{{ " + variable.Path + " }}";
            }

            if (variable.Kind == ReflectedCustomVariableKind.Pawn)
            {
                return "{{ pawn." + rawName + " }}";
            }

            return "{{ " + rawName + " }}";
        }

        private static ReflectedCustomVariable CloneVariable(ReflectedCustomVariable source)
        {
            if (source == null)
            {
                return null;
            }

            return new ReflectedCustomVariable
            {
                LegacyName = source.LegacyName ?? string.Empty,
                Path = source.Path ?? string.Empty,
                ModId = source.ModId ?? string.Empty,
                Description = source.Description ?? string.Empty,
                Kind = source.Kind
            };
        }

        private static string SanitizeModId(string modId)
        {
            if (string.IsNullOrWhiteSpace(modId))
            {
                return "rimchat";
            }

            var sb = new StringBuilder(modId.Length);
            for (int i = 0; i < modId.Length; i++)
            {
                char c = char.ToLowerInvariant(modId[i]);
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
            }

            return sb.Length == 0 ? "rimchat" : sb.ToString();
        }

        private static string TrimToBudget(string text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string normalized = text.Trim();
            if (normalized.Length <= maxChars)
            {
                return normalized;
            }

            if (maxChars <= 3)
            {
                return normalized.Substring(0, maxChars);
            }

            return normalized.Substring(0, maxChars - 3).TrimEnd() + "...";
        }
    }
}
