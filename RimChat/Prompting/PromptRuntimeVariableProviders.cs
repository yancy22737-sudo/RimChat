using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Memory;
using RimWorld;
using Verse;

namespace RimChat.Prompting
{
    internal sealed class RimChatCoreVariableProvider : IPromptRuntimeVariableProvider
    {
        private const string Source = "rimchat.core";
        private static readonly IReadOnlyList<PromptRuntimeVariableDefinition> Definitions = BuildDefinitions();
        private readonly Func<string, PromptRuntimeVariableContext, object> _resolver;

        public RimChatCoreVariableProvider(Func<string, PromptRuntimeVariableContext, object> resolver)
        {
            _resolver = resolver;
        }

        public string SourceId => Source;
        public string SourceLabel => "RimChat Core";
        public bool IsAvailable(PromptRuntimeVariableContext context) => true;
        public IReadOnlyList<PromptRuntimeVariableDefinition> GetDefinitions() => Definitions;
        public bool TryMapLegacyToken(string token, out string namespacedPath)
        {
            namespacedPath = string.Empty;
            return false;
        }

        public void PopulateValues(IDictionary<string, object> values, PromptRuntimeVariableContext context)
        {
            if (values == null || _resolver == null)
            {
                return;
            }

            for (int i = 0; i < Definitions.Count; i++)
            {
                PromptRuntimeVariableDefinition definition = Definitions[i];
                object value = _resolver(definition.Path, context);
                if (value != null)
                {
                    values[definition.Path] = value;
                }
            }
        }

        private static IReadOnlyList<PromptRuntimeVariableDefinition> BuildDefinitions()
        {
            string[] paths =
            {
                "ctx.channel", "ctx.mode", "system.target_language", "system.game_language",
                "world.faction.name", "world.scene_tags", "world.environment_params", "world.recent_world_events",
                "world.colony_status", "world.colony_factions", "world.current_faction_profile", "world.faction_settlement_summary",
                "world.social.origin_type", "world.social.category", "world.social.source_faction", "world.social.target_faction",
                "world.social.source_label", "world.social.credibility_label", "world.social.credibility_value", "world.social.fact_lines",
                "pawn.initiator", "pawn.target", "pawn.initiator.name", "pawn.target.name", "pawn.target.profile",
                "pawn.initiator.profile", "pawn.player.profile", "pawn.player.royalty_summary", "pawn.relation.kinship",
                "pawn.relation.romance_state", "pawn.speaker.kind", "pawn.speaker.default_sound", "pawn.speaker.animal_sound",
                "pawn.speaker.baby_sound", "pawn.speaker.mechanoid_sound", "pawn.pronouns.subject", "pawn.pronouns.object",
                "pawn.pronouns.possessive", "pawn.pronouns.subject_lower", "pawn.pronouns.be_verb", "pawn.pronouns.seek_verb",
                "pawn.profile", "pawn.personality", "dialogue.summary", "dialogue.guidance", "dialogue.intent_hint",
                "dialogue.template_line", "dialogue.example_line", "dialogue.examples", "dialogue.action_names",
                "dialogue.primary_objective", "dialogue.optional_followup", "dialogue.latest_unresolved_intent",
                "dialogue.topic_shift_rule", "dialogue.api_limits_body", "dialogue.quest_guidance_body",
                "dialogue.response_contract_body", "system.punctuation.open_paren", "system.punctuation.close_paren"
            };

            var items = new List<PromptRuntimeVariableDefinition>(paths.Length);
            for (int i = 0; i < paths.Length; i++)
            {
                string key = PromptRuntimeVariableBridge.GetDescriptionKey(paths[i]);
                items.Add(new PromptRuntimeVariableDefinition(paths[i], Source, "RimChat Core", key, true));
            }

            return items;
        }
    }

    internal sealed class RimTalkVariableProvider : ReflectionBridgeVariableProvider
    {
        public override string SourceId => "rimtalk.bridge";
        public override string SourceLabel => "RimTalk Bridge";
        protected override string AvailabilityToken => "rimtalk";

        protected override IReadOnlyList<PromptRuntimeVariableDefinition> GetBuiltinDefinitions()
        {
            return PromptRuntimeVariableBridge.GetBuiltinRimTalkDefinitions(SourceId, SourceLabel);
        }

        protected override bool AcceptCustomVariable(ReflectedCustomVariable variable)
        {
            return variable != null &&
                   !PromptRuntimeVariableBridge.ContainsToken(variable.ModId, "memorypatch") &&
                   !PromptRuntimeVariableBridge.ContainsToken(variable.LegacyName, "memorypatch");
        }

        protected override void PopulateBuiltinValues(IDictionary<string, object> values, PromptRuntimeVariableContext context)
        {
            values["pawn.rimtalk.context"] = PromptRuntimeVariableBridge.BuildRimTalkContextBlock(context);
            values["dialogue.rimtalk.prompt"] = PromptRuntimeVariableBridge.BuildRimTalkPromptBlock(context);
            values["dialogue.rimtalk.history"] = PromptRuntimeVariableBridge.BuildRimTalkHistoryBlock(context, false);
            values["dialogue.rimtalk.history_simplified"] = PromptRuntimeVariableBridge.BuildRimTalkHistoryBlock(context, true);
            values["system.rimtalk.json_format"] = PromptRuntimeVariableBridge.GetJsonInstruction();
        }

        protected override bool TryMapBuiltinLegacyToken(string token, out string namespacedPath)
        {
            switch ((token ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "context":
                    namespacedPath = "pawn.rimtalk.context";
                    return true;
                case "prompt":
                    namespacedPath = "dialogue.rimtalk.prompt";
                    return true;
                case "chat.history":
                    namespacedPath = "dialogue.rimtalk.history";
                    return true;
                case "chat.history_simplified":
                    namespacedPath = "dialogue.rimtalk.history_simplified";
                    return true;
                case "json.format":
                    namespacedPath = "system.rimtalk.json_format";
                    return true;
                default:
                    namespacedPath = string.Empty;
                    return false;
            }
        }
    }

    internal sealed class RimTalkMemoryPatchVariableProvider : ReflectionBridgeVariableProvider
    {
        public override string SourceId => "rimtalk.memorypatch";
        public override string SourceLabel => "MemoryPatch Bridge";
        protected override string AvailabilityToken => "memorypatch";
        protected override IReadOnlyList<PromptRuntimeVariableDefinition> GetBuiltinDefinitions() => Array.Empty<PromptRuntimeVariableDefinition>();
        protected override void PopulateBuiltinValues(IDictionary<string, object> values, PromptRuntimeVariableContext context) { }
        protected override bool TryMapBuiltinLegacyToken(string token, out string namespacedPath)
        {
            namespacedPath = string.Empty;
            return false;
        }

        protected override bool AcceptCustomVariable(ReflectedCustomVariable variable)
        {
            return variable != null &&
                   (PromptRuntimeVariableBridge.ContainsToken(variable.ModId, "memorypatch") ||
                    PromptRuntimeVariableBridge.ContainsToken(variable.LegacyName, "memorypatch"));
        }
    }

    internal abstract class ReflectionBridgeVariableProvider : IPromptRuntimeVariableProvider
    {
        public abstract string SourceId { get; }
        public abstract string SourceLabel { get; }
        protected abstract string AvailabilityToken { get; }
        protected abstract IReadOnlyList<PromptRuntimeVariableDefinition> GetBuiltinDefinitions();
        protected abstract bool AcceptCustomVariable(ReflectedCustomVariable variable);
        protected abstract void PopulateBuiltinValues(IDictionary<string, object> values, PromptRuntimeVariableContext context);
        protected abstract bool TryMapBuiltinLegacyToken(string token, out string namespacedPath);

        public bool IsAvailable(PromptRuntimeVariableContext context) => PromptRuntimeVariableBridge.IsDependencyAvailable(AvailabilityToken);

        public IReadOnlyList<PromptRuntimeVariableDefinition> GetDefinitions()
        {
            var definitions = new List<PromptRuntimeVariableDefinition>();
            definitions.AddRange(GetBuiltinDefinitions());
            definitions.AddRange(PromptRuntimeVariableBridge.GetCustomVariables()
                .Where(AcceptCustomVariable)
                .Select(item => item.ToDefinition(SourceId, SourceLabel)));
            return definitions;
        }

        public void PopulateValues(IDictionary<string, object> values, PromptRuntimeVariableContext context)
        {
            if (values == null || !IsAvailable(context))
            {
                return;
            }

            PopulateBuiltinValues(values, context);
            foreach (ReflectedCustomVariable variable in PromptRuntimeVariableBridge.GetCustomVariables().Where(AcceptCustomVariable))
            {
                if (PromptRuntimeVariableBridge.TryResolveCustomVariableValue(variable, context, out string value))
                {
                    values[variable.Path] = value ?? string.Empty;
                }
            }
        }

        public bool TryMapLegacyToken(string token, out string namespacedPath)
        {
            if (TryMapBuiltinLegacyToken(token, out namespacedPath))
            {
                return true;
            }

            ReflectedCustomVariable custom = PromptRuntimeVariableBridge.GetCustomVariables()
                .FirstOrDefault(item => AcceptCustomVariable(item) && item.MatchesLegacyToken(token));
            namespacedPath = custom?.Path ?? string.Empty;
            return !string.IsNullOrWhiteSpace(namespacedPath);
        }
    }
}
