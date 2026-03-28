using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RimChat.Config;
using RimChat.Core;
using RimChat.DiplomacySystem;
using RimChat.Memory;
using RimChat.Prompting;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.Persistence
{
    public partial class PromptPersistenceService
    {
        private static readonly Regex TemplateVariableRegex = new Regex(@"\{\{\s*([a-zA-Z0-9_.]+)\s*\}\}", RegexOptions.Compiled);
        private static readonly HashSet<string> AllowedTemplateVariableNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ctx",
            "pawn",
            "world",
            "dialogue",
            "system"
        };

        public IReadOnlyList<PromptTemplateVariableDefinition> GetTemplateVariableDefinitions()
        {
            return PromptVariableCatalog.GetDefinitions()
                .Where(item => item != null)
                .Select(item => item.ToTemplateDefinition())
                .ToList();
        }

        public TemplateVariableValidationResult ValidateTemplateVariables(string templateText)
        {
            return ValidateTemplateVariables(templateText, TemplateVariableValidationContext.CreateDefault());
        }

        public TemplateVariableValidationResult ValidateTemplateVariables(
            string templateText,
            IEnumerable<string> additionalKnownVariables)
        {
            return ValidateTemplateVariables(
                templateText,
                TemplateVariableValidationContext.FromAdditionalKnownVariables(additionalKnownVariables));
        }

        internal TemplateVariableValidationResult ValidateTemplateVariables(
            string templateText,
            TemplateVariableValidationContext validationContext)
        {
            var result = new TemplateVariableValidationResult();
            if (string.IsNullOrWhiteSpace(templateText))
            {
                return result;
            }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var validationPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            TemplateVariableValidationContext context = validationContext ?? TemplateVariableValidationContext.CreateDefault();
            MatchCollection matches = TemplateVariableRegex.Matches(templateText);
            for (int i = 0; i < matches.Count; i++)
            {
                string name = NormalizeTemplateVariableName(matches[i].Groups[1].Value);
                if (name.Length == 0)
                {
                    continue;
                }

                if (context.Contains(name))
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
            values["dialogue.mandatory_race_profile_body"] = BuildMandatoryRaceProfileBody(context);
            bool isPreview = IsPreviewScenario(context);
            if (context?.Initiator != null)
            {
                values["pawn.initiator"] = context.Initiator;
            }
            else if (isPreview)
            {
                values["pawn.initiator"] = CreatePreviewPawnPlaceholder("PreviewInitiator");
            }

            if (context?.Target != null)
            {
                values["pawn.target"] = context.Target;
            }
            else if (isPreview)
            {
                values["pawn.target"] = CreatePreviewPawnPlaceholder("PreviewTarget");
            }

            if (context?.Faction != null)
            {
                values["world.faction"] = context.Faction;
            }
            else if (isPreview)
            {
                values["world.faction"] = CreatePreviewFactionPlaceholder("PreviewFaction");
            }

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
            if (string.IsNullOrWhiteSpace(variableName))
            {
                return false;
            }

            int separator = variableName.IndexOf('.');
            if (separator <= 0)
            {
                return false;
            }

            string rootNamespace = variableName.Substring(0, separator).Trim();
            return AllowedTemplateVariableNamespaces.Contains(rootNamespace);
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
            catch (ArgumentException ex)
            {
                result.ScribanErrorCode = (int)PromptRenderErrorCode.UnknownVariable;
                result.ScribanErrorLine = 0;
                result.ScribanErrorColumn = 0;
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
                case "world.time.hour":
                    return BuildWorldTimeHourVariableValue(context);
                case "world.time.day":
                    return BuildWorldTimeDayVariableValue(context);
                case "world.time.quadrum":
                    return BuildWorldTimeQuadrumVariableValue(context);
                case "world.time.year":
                    return BuildWorldTimeYearVariableValue(context);
                case "world.time.season":
                    return BuildWorldTimeSeasonVariableValue(context);
                case "world.time.date":
                    return BuildWorldTimeDateVariableValue(context);
                case "world.weather":
                    return BuildWorldWeatherVariableValue(context);
                case "world.temperature":
                    return BuildWorldTemperatureVariableValue(context);
                case "world.faction.name":
                    return context?.Faction?.Name ?? "Unknown Faction";
                case "world.faction.description":
                    return BuildFactionDescriptionVariableText(context);
                case "pawn.initiator.name":
                    return context?.Initiator?.LabelShort ?? "Unknown";
                case "pawn.target.name":
                    return context?.Target?.LabelShort ?? "Unknown";
                case "pawn.recipient":
                    return context?.Target;
                case "pawn.recipient.name":
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
                    return BuildPawnProfileVariableText(context?.Target, context, envConfig);
                case "pawn.initiator.profile":
                    return BuildPawnProfileVariableText(context?.Initiator, context, envConfig);
                case "pawn.player.profile":
                    return BuildPlayerPawnProfileVariableText(context);
                case "pawn.player.royalty_summary":
                    return BuildPlayerRoyaltySummaryVariableText(context);
                case "world.faction_settlement_summary":
                    return BuildFactionSettlementSummaryVariableText(context);
                case "pawn.personality":
                    return BuildPawnPersonalityVariableText(context);
                case "dialogue.primary_objective":
                    return ResolveDialoguePrimaryObjectiveVariableValue(context);
                case "dialogue.optional_followup":
                    return ResolveDialogueOptionalFollowupVariableValue(context);
                case "dialogue.latest_unresolved_intent":
                    return ResolveDialogueLatestUnresolvedIntentVariableValue(context);
                case "dialogue.topic_shift_rule":
                    return "Complete the primary objective first, then allow at most one natural topic extension.";
                case "pawn.relation.kinship":
                    return ResolveRpgRelationSnapshot(context).Kinship;
                case "pawn.relation.romance_state":
                    return ResolveRpgRelationSnapshot(context).RomanceState;
                case "pawn.relation.social_summary":
                    return ResolveRpgRelationSnapshot(context).SocialSummary;
                case "dialogue.guidance":
                    return ResolveRpgRelationSnapshot(context).Guidance;
                default:
                    return null;
            }
        }

        private string ResolveDialoguePrimaryObjectiveVariableValue(DialogueScenarioContext context)
        {
            string unresolvedIntent = ResolveDialogueLatestUnresolvedIntentVariableValue(context);
            return BuildPrimaryObjectiveFromIntent(unresolvedIntent);
        }

        private static string ResolveDialogueOptionalFollowupVariableValue(DialogueScenarioContext context)
        {
            if (context?.IsRpg == true)
            {
                return "After completing the primary objective, optionally add one relevant follow-up.";
            }

            return string.Empty;
        }

        private string ResolveDialogueLatestUnresolvedIntentVariableValue(DialogueScenarioContext context)
        {
            if (context?.IsRpg != true || context.Target == null || context.Initiator == null)
            {
                return string.Empty;
            }

            return RpgNpcDialogueArchiveManager.Instance.BuildUnresolvedIntentSummary(context.Target, context.Initiator) ?? string.Empty;
        }

        private RpgRelationSnapshot ResolveRpgRelationSnapshot(DialogueScenarioContext context)
        {
            if (context?.IsRpg != true || context.Initiator == null || context.Target == null)
            {
                return RpgRelationSnapshot.Empty;
            }

            bool kinship = HasAnyBloodRelationBetweenPair(context.Initiator, context.Target);
            string kinshipValue = kinship ? "yes" : "no";
            string romanceState = ResolvePairRomanceState(context.Initiator, context.Target);
            string guidance = BuildRpgKinshipBoundaryGuidanceText(
                RimChatMod.Settings,
                context.Initiator,
                context.Target,
                context) ?? string.Empty;
            string socialSummary = BuildPairSocialSummary(context.Initiator, context.Target, kinshipValue, romanceState);
            return new RpgRelationSnapshot(kinshipValue, romanceState, socialSummary, guidance);
        }

        private readonly struct RpgRelationSnapshot
        {
            public static readonly RpgRelationSnapshot Empty = new RpgRelationSnapshot(string.Empty, string.Empty, string.Empty, string.Empty);

            public RpgRelationSnapshot(string kinship, string romanceState, string socialSummary, string guidance)
            {
                Kinship = kinship ?? string.Empty;
                RomanceState = romanceState ?? string.Empty;
                SocialSummary = socialSummary ?? string.Empty;
                Guidance = guidance ?? string.Empty;
            }

            public string Kinship { get; }
            public string RomanceState { get; }
            public string SocialSummary { get; }
            public string Guidance { get; }
        }

        private int BuildWorldTimeHourVariableValue(DialogueScenarioContext context)
        {
            return GenDate.HourOfDay(GetAbsoluteTicks(), GetLongitude(context));
        }

        private int BuildWorldTimeDayVariableValue(DialogueScenarioContext context)
        {
            return GenDate.DayOfQuadrum(GetAbsoluteTicks(), GetLongitude(context)) + 1;
        }

        private string BuildWorldTimeQuadrumVariableValue(DialogueScenarioContext context)
        {
            return GenDate.Quadrum(GetAbsoluteTicks(), GetLongitude(context)).Label();
        }

        private int BuildWorldTimeYearVariableValue(DialogueScenarioContext context)
        {
            return GenDate.Year(GetAbsoluteTicks(), GetLongitude(context));
        }

        private string BuildWorldTimeSeasonVariableValue(DialogueScenarioContext context)
        {
            Map map = ResolveEnvironmentMap(context);
            return map != null ? GenLocalDate.Season(map).Label() : Season.Undefined.Label();
        }

        private string BuildWorldTimeDateVariableValue(DialogueScenarioContext context)
        {
            return GenDate.DateFullStringAt(GetAbsoluteTicks(), GetLongLat(context));
        }

        private string BuildWorldWeatherVariableValue(DialogueScenarioContext context)
        {
            Map map = ResolveEnvironmentMap(context);
            return map?.weatherManager?.curWeather?.label ?? "Unknown";
        }

        private string BuildWorldTemperatureVariableValue(DialogueScenarioContext context)
        {
            Map map = ResolveEnvironmentMap(context);
            return map == null
                ? "Unknown"
                : Mathf.RoundToInt(map.mapTemperature?.OutdoorTemp ?? 0f).ToString();
        }

        private static int GetAbsoluteTicks()
        {
            return Find.TickManager?.TicksAbs ?? 0;
        }

        private float GetLongitude(DialogueScenarioContext context)
        {
            return GetLongLat(context).x;
        }

        private Vector2 GetLongLat(DialogueScenarioContext context)
        {
            Map map = ResolveEnvironmentMap(context);
            if (map == null || Find.WorldGrid == null)
            {
                return Vector2.zero;
            }

            return Find.WorldGrid.LongLatOf(map.Tile);
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

            string snapshot = BuildEnvironmentSnapshotVariableText(lines, maxItems: 5, maxChars: 220);
            if (string.IsNullOrWhiteSpace(snapshot))
            {
                return "See <environment> for full environment details.";
            }

            return "See <environment> for full environment details. Snapshot: " + snapshot;
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
            string digest = BuildRecentWorldEventIntelCompactDigest(
                clonedEnv,
                context,
                maxItems: 2,
                maxChars: 260);
            return string.IsNullOrWhiteSpace(digest) ? "No recent world events." : digest;
        }

        private static string BuildEnvironmentSnapshotVariableText(
            IEnumerable<string> lines,
            int maxItems,
            int maxChars)
        {
            List<string> source = lines?
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToList() ?? new List<string>();
            if (source.Count == 0)
            {
                return string.Empty;
            }

            string[] preferredPrefixes =
            {
                "Time:", "Date:", "Season:", "Weather:", "Location:", "Terrain:", "MapWealth:"
            };
            var selected = new List<string>();
            for (int i = 0; i < preferredPrefixes.Length; i++)
            {
                string prefix = preferredPrefixes[i];
                string match = source.FirstOrDefault(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match) && !selected.Contains(match))
                {
                    selected.Add(match);
                    if (selected.Count >= maxItems)
                    {
                        break;
                    }
                }
            }

            for (int i = 0; i < source.Count && selected.Count < maxItems; i++)
            {
                string line = source[i];
                if (!selected.Contains(line))
                {
                    selected.Add(line);
                }
            }

            string snapshot = string.Join(" | ", selected);
            if (snapshot.Length <= maxChars)
            {
                return snapshot;
            }

            return snapshot.Substring(0, Math.Max(16, maxChars)).TrimEnd() + "...";
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

            Faction playerFaction = Faction.OfPlayer;
            string leader = faction.leader?.Name?.ToStringFull ?? "Unknown";
            string relation = BuildFactionRelationTowardPlayerText(faction, playerFaction);
            int? goodwill = faction == playerFaction || faction.IsPlayer
                ? null
                : TryGetGoodwillTowardPlayer(faction);
            string goodwillText = goodwill.HasValue ? goodwill.Value.ToString() : "N/A";
            return $"Faction: {faction.Name}\nDef: {faction.def?.defName}\nTech: {faction.def?.techLevel}\nGoodwill: {goodwillText}\nRelation: {relation}\nLeader: {leader}";
        }

        private string BuildFactionDescriptionVariableText(DialogueScenarioContext context)
        {
            Faction faction = context?.Faction ?? context?.Target?.Faction ?? context?.Initiator?.Faction;
            if (faction?.def == null)
            {
                return "No faction context.";
            }

            string prompt = FactionPromptManager.Instance.GetPrompt(faction);
            return string.IsNullOrWhiteSpace(prompt)
                ? "No faction prompt configured."
                : prompt.Trim();
        }

        private static string BuildFactionRelationTowardPlayerText(Faction faction, Faction playerFaction)
        {
            if (faction == null || playerFaction == null)
            {
                return "Unknown";
            }

            if (faction == playerFaction || faction.IsPlayer)
            {
                return "Same faction (ally relation).";
            }

            return faction.RelationKindWith(playerFaction).ToString();
        }

        private static int? TryGetGoodwillTowardPlayer(Faction faction)
        {
            Faction playerFaction = Faction.OfPlayer;
            if (faction == null || playerFaction == null || faction == playerFaction || faction.IsPlayer)
            {
                return null;
            }

            try
            {
                return faction.PlayerGoodwill;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to resolve faction goodwill for '{faction.Name ?? "Unknown"}': {ex.Message}");
                return null;
            }
        }

        private string BuildPawnPersonalityVariableText(DialogueScenarioContext context)
        {
            Pawn primary = context?.Target ?? context?.Initiator;
            if (primary == null)
            {
                return "No pawn context.";
            }

            GameComponent_RPGManager manager =
                GameComponent_RPGManager.Instance ?? Current.Game?.GetComponent<GameComponent_RPGManager>();
            string text = manager?.ResolveEffectivePawnPersonalityPrompt(primary, allowGenerateFallback: true) ?? string.Empty;
            return string.IsNullOrWhiteSpace(text)
                ? "No personality context."
                : text.Trim();
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
