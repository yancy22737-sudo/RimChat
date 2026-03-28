using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: prompt variable registry metadata and localization keys.
    /// Responsibility: provide stable static metadata used by workbench hover tooltips.
    /// </summary>
    internal static class PromptVariableTooltipCatalog
    {
        private static readonly Dictionary<string, string[]> ExplicitTypicalValues = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["ctx.channel"] = new[] { "diplomacy", "rpg" },
            ["ctx.mode"] = new[] { "manual", "proactive" },
            ["system.game_language"] = new[] { "English", "ChineseSimplified" },
            ["system.punctuation.open_paren"] = new[] { "(" },
            ["system.punctuation.close_paren"] = new[] { ")" },
            ["dialogue.summary"] = new[]
            {
                "Primary objective resolved; goodwill unchanged; awaiting player reply",
                "Trade request acknowledged; caravan route still pending"
            },
            ["dialogue.guidance"] = new[]
            {
                "Stay in character and answer the latest player intent first",
                "Keep the reply grounded in current faction context"
            },
            ["dialogue.intent_hint"] = new[]
            {
                "Player is asking for trade terms",
                "Player wants to reopen a stalled request"
            },
            ["dialogue.template_line"] = new[]
            {
                "[Core trait] [social style] [value anchor]",
                "[Outlook] [habit] [tone cue]"
            },
            ["dialogue.example_line"] = new[]
            {
                "Calm mediator, patient listener, values practical trust"
            },
            ["dialogue.examples"] = new[]
            {
                "1. Calm mediator who values practical trust",
                "2. Direct worker with little patience for ceremony"
            },
            ["dialogue.action_names"] = new[]
            {
                "give_gift",
                "start_trade"
            },
            ["dialogue.primary_objective"] = new[]
            {
                "Resolve the player's latest diplomacy request",
                "Answer the NPC conversation opener in character"
            },
            ["dialogue.optional_followup"] = new[]
            {
                "Offer one natural next step after the main reply",
                "Add one brief follow-up suggestion if it helps"
            },
            ["dialogue.latest_unresolved_intent"] = new[]
            {
                "Player still wants a goodwill gesture",
                "Previous trade negotiation ended without a clear answer"
            },
            ["dialogue.topic_shift_rule"] = new[]
            {
                "Complete the primary objective first, then allow at most one natural extension"
            },
            ["dialogue.api_limits_body"] = new[]
            {
                "Only use actions when gameplay impact is required",
                "Append at most one trailing actions JSON block"
            },
            ["dialogue.quest_guidance_body"] = new[]
            {
                "Choose only valid quest templates already exposed by the game",
                "Do not invent unsupported quest rewards or steps"
            },
            ["dialogue.response_contract_body"] = new[]
            {
                "Natural language first; append one trailing JSON object only when effects are needed",
                "No JSON when there is no gameplay action to execute"
            },
            ["dialogue.mandatory_race_profile_body"] = new[]
            {
                "Role: Leader; Name: X; RaceKind: Humanlike; RaceDef: Human; Xenotype: Baseliner",
                "Role: Target; Name: Y; RaceKind: Animal; RaceDef: Muffalo; Xenotype: N/A"
            },
            ["pawn.initiator"] = new[] { "Current initiator pawn reference" },
            ["pawn.target"] = new[] { "Current target pawn reference" },
            ["pawn.recipient"] = new[] { "Alias of the current target pawn" },
            ["pawn.profile"] = new[]
            {
                "Name: Risa; role: negotiator; mood: calm",
                "Short persona summary used by prompt templates"
            },
            ["pawn.personality"] = new[]
            {
                "Pragmatic, patient, distrusts empty promises",
                "Warm but cautious around strangers"
            },
            ["world.faction.description"] = new[]
            {
                "Core style: pragmatic industrial negotiators focused on trust and trade.",
                "Tone: measured, strategic, and aligned with faction boundaries."
            },
            ["world.social.origin_type"] = new[] { "letter", "radio", "rumor" },
            ["world.social.category"] = new[] { "trade", "war", "rumor" },
            ["world.social.source_faction"] = new[] { "The Southern Empire", "Rough Outlander Union" },
            ["world.social.target_faction"] = new[] { "Player Colony", "Blue Moon Compact" },
            ["world.social.source_label"] = new[] { "Imperial envoy report", "Traveler gossip" },
            ["world.social.credibility_label"] = new[] { "Low", "Medium", "High" },
            ["world.social.credibility_value"] = new[] { "0.15", "0.50", "0.92" },
            ["world.social.fact_lines"] = new[]
            {
                "- Empire caravan was seen near the eastern road",
                "- Local rumor claims a raid may happen soon"
            }
        };

        private static readonly Dictionary<string, string> ScopeDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ctx"] = "Request context and channel metadata.",
            ["system"] = "System-level language and output constraints.",
            ["world"] = "World, faction, time, and environment context.",
            ["pawn"] = "Speaker, target, and pawn state context.",
            ["dialogue"] = "Dialogue guidance, policy, and generated prompt context."
        };

        private static readonly Dictionary<string, string> ExplicitDataTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ctx.channel"] = "Enum text",
            ["ctx.mode"] = "Enum text",
            ["system.target_language"] = "Language text",
            ["system.game_language"] = "Language text",
            ["world.time.hour"] = "Integer",
            ["world.time.day"] = "Integer",
            ["world.time.year"] = "Integer",
            ["world.time.quadrum"] = "Enum text",
            ["world.time.season"] = "Enum text",
            ["world.time.date"] = "Date text",
            ["world.weather"] = "Label text",
            ["world.temperature"] = "Integer",
            ["world.faction.name"] = "Label text",
            ["world.faction.description"] = "Structured text block",
            ["world.scene_tags"] = "Tag list text",
            ["world.environment_params"] = "Structured text block",
            ["world.recent_world_events"] = "Structured text block",
            ["world.colony_status"] = "Structured text block",
            ["world.colony_factions"] = "Structured text block",
            ["world.current_faction_profile"] = "Structured text block",
            ["world.faction_settlement_summary"] = "Structured text block",
            ["world.social.origin_type"] = "Enum text",
            ["world.social.category"] = "Enum text",
            ["world.social.source_faction"] = "Label text",
            ["world.social.target_faction"] = "Label text",
            ["world.social.source_label"] = "Label text",
            ["world.social.credibility_label"] = "Label text",
            ["world.social.credibility_value"] = "Float",
            ["world.social.fact_lines"] = "Structured text block",
            ["pawn.initiator"] = "Pawn reference",
            ["pawn.target"] = "Pawn reference",
            ["pawn.recipient"] = "Pawn reference",
            ["pawn.initiator.name"] = "Label text",
            ["pawn.target.name"] = "Label text",
            ["pawn.recipient.name"] = "Label text",
            ["pawn.target.profile"] = "Structured text block",
            ["pawn.initiator.profile"] = "Structured text block",
            ["pawn.player.profile"] = "Structured text block",
            ["pawn.player.royalty_summary"] = "Structured text block",
            ["pawn.relation.kinship"] = "Label text",
            ["pawn.relation.romance_state"] = "Label text",
            ["pawn.relation.social_summary"] = "Structured text block",
            ["pawn.speaker.kind"] = "Label text",
            ["pawn.speaker.default_sound"] = "Label text",
            ["pawn.speaker.animal_sound"] = "Label text",
            ["pawn.speaker.baby_sound"] = "Label text",
            ["pawn.speaker.mechanoid_sound"] = "Label text",
            ["pawn.pronouns.subject"] = "Pronoun text",
            ["pawn.pronouns.object"] = "Pronoun text",
            ["pawn.pronouns.possessive"] = "Pronoun text",
            ["pawn.pronouns.subject_lower"] = "Pronoun text",
            ["pawn.pronouns.be_verb"] = "Verb text",
            ["pawn.pronouns.seek_verb"] = "Verb text",
            ["pawn.profile"] = "Structured text block",
            ["pawn.personality"] = "Structured text block",
            ["pawn.rimtalk.context"] = "Structured text block",
            ["dialogue.summary"] = "Structured text block",
            ["dialogue.guidance"] = "Structured text block",
            ["dialogue.intent_hint"] = "Structured text block",
            ["dialogue.template_line"] = "Template line",
            ["dialogue.example_line"] = "Template line",
            ["dialogue.examples"] = "Example list text",
            ["dialogue.action_names"] = "Action list text",
            ["dialogue.primary_objective"] = "Instruction text",
            ["dialogue.optional_followup"] = "Instruction text",
            ["dialogue.latest_unresolved_intent"] = "Instruction text",
            ["dialogue.topic_shift_rule"] = "Policy text",
            ["dialogue.api_limits_body"] = "Structured text block",
            ["dialogue.quest_guidance_body"] = "Structured text block",
            ["dialogue.response_contract_body"] = "Structured text block",
            ["dialogue.mandatory_race_profile_body"] = "Structured text block",
            ["dialogue.rimtalk.prompt"] = "Structured text block",
            ["dialogue.rimtalk.history"] = "Structured text block",
            ["dialogue.rimtalk.history_simplified"] = "Structured text block",
            ["system.punctuation.open_paren"] = "Single character",
            ["system.punctuation.close_paren"] = "Single character"
        };

        public static PromptVariableTooltipInfo Resolve(string variableName)
        {
            string normalized = (variableName ?? string.Empty).Trim();
            if (UserDefinedPromptVariableService.IsUserDefinedPath(normalized))
            {
                PromptVariableTooltipInfo dynamicInfo = UserDefinedPromptVariableService.BuildTooltipInfo(normalized);
                if (dynamicInfo != null)
                {
                    return dynamicInfo;
                }
            }

            string scope = ResolveScope(normalized);
            string description = ResolveDescription(normalized, scope);
            string dataType = ResolveDataType(normalized, scope);
            List<string> typicalValues = ResolveTypicalValues(normalized, scope);
            return new PromptVariableTooltipInfo(normalized, dataType, description, typicalValues);
        }

        private static string ResolveScope(string variableName)
        {
            int dot = variableName.IndexOf('.');
            if (dot <= 0)
            {
                return "general";
            }

            return variableName.Substring(0, dot).Trim().ToLowerInvariant();
        }

        private static string ResolveDescription(string variableName, string scope)
        {
            if (string.IsNullOrWhiteSpace(variableName))
            {
                return string.Empty;
            }

            PromptRuntimeVariableDefinition definition = PromptRuntimeVariableRegistry.Resolve(variableName);
            if (definition == null)
            {
                return "Unknown or custom variable token.";
            }

            string localized = ResolveLocalizedDescription(definition.DescriptionKey);
            if (!string.IsNullOrWhiteSpace(localized))
            {
                return localized;
            }

            if (ScopeDescriptions.TryGetValue(scope, out string description))
            {
                return description;
            }

            return "Prompt variable metadata.";
        }

        private static string ResolveDataType(string variableName, string scope)
        {
            if (string.IsNullOrWhiteSpace(variableName))
            {
                return string.Empty;
            }

            if (ExplicitDataTypes.TryGetValue(variableName, out string explicitType))
            {
                return explicitType;
            }

            if (variableName.EndsWith(".name", StringComparison.OrdinalIgnoreCase) ||
                variableName.EndsWith(".label", StringComparison.OrdinalIgnoreCase))
            {
                return "Label text";
            }

            if (variableName.EndsWith(".profile", StringComparison.OrdinalIgnoreCase) ||
                variableName.EndsWith(".summary", StringComparison.OrdinalIgnoreCase) ||
                variableName.EndsWith(".history", StringComparison.OrdinalIgnoreCase) ||
                variableName.EndsWith(".guidance", StringComparison.OrdinalIgnoreCase))
            {
                return "Structured text block";
            }

            if (variableName.EndsWith(".value", StringComparison.OrdinalIgnoreCase) ||
                variableName.EndsWith(".score", StringComparison.OrdinalIgnoreCase))
            {
                return "Float";
            }

            return scope == "pawn" ? "Runtime value" : "Text";
        }

        private static List<string> ResolveTypicalValues(string variableName, string scope)
        {
            if (string.IsNullOrWhiteSpace(variableName))
            {
                return CreateTypicalValues("runtime-resolved value", "empty string", "not available");
            }

            if (ExplicitTypicalValues.TryGetValue(variableName, out string[] explicitValues))
            {
                return CreateTypicalValues(explicitValues);
            }

            if (variableName.EndsWith(".hour", StringComparison.OrdinalIgnoreCase))
            {
                return CreateTypicalValues("6", "14", "22");
            }

            if (variableName.EndsWith(".day", StringComparison.OrdinalIgnoreCase))
            {
                return CreateTypicalValues("1", "7", "15");
            }

            if (variableName.EndsWith(".year", StringComparison.OrdinalIgnoreCase))
            {
                return CreateTypicalValues("5500", "5503", "5512");
            }

            if (variableName.EndsWith(".temperature", StringComparison.OrdinalIgnoreCase))
            {
                return CreateTypicalValues("-18", "8", "27");
            }

            if (variableName.EndsWith(".name", StringComparison.OrdinalIgnoreCase))
            {
                return CreateTypicalValues("The Southern Empire", "Risa", "Player Colony");
            }

            if (variableName.EndsWith(".season", StringComparison.OrdinalIgnoreCase))
            {
                return CreateTypicalValues("Aprimay", "Jugust", "Decembary");
            }

            if (variableName.EndsWith(".quadrum", StringComparison.OrdinalIgnoreCase))
            {
                return CreateTypicalValues("Aprimay", "Jugust", "Septober");
            }

            if (variableName.EndsWith(".weather", StringComparison.OrdinalIgnoreCase))
            {
                return CreateTypicalValues("Clear", "Rain", "FoggyRain");
            }

            if (variableName.IndexOf("language", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return CreateTypicalValues("English", "ChineseSimplified", "Japanese");
            }

            if (variableName.IndexOf("credibility_value", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return CreateTypicalValues("0.15", "0.50", "0.92");
            }

            if (variableName.IndexOf("credibility_label", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return CreateTypicalValues("Low", "Medium", "High");
            }

            if (variableName.IndexOf("category", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return CreateTypicalValues("trade", "war", "rumor");
            }

            if (variableName.IndexOf("origin_type", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return CreateTypicalValues("letter", "radio", "rumor");
            }

            if (variableName.IndexOf("pronouns", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return CreateTypicalValues("he", "him", "his");
            }

            if (variableName.EndsWith(".kind", StringComparison.OrdinalIgnoreCase))
            {
                return CreateTypicalValues("Human", "Animal", "Mechanoid");
            }

            if (variableName.EndsWith(".date", StringComparison.OrdinalIgnoreCase))
            {
                return CreateTypicalValues("1st of Aprimay, 5501", "7th of Jugust, 5504", "15th of Decembary, 5510");
            }

            if (variableName.EndsWith(".profile", StringComparison.OrdinalIgnoreCase))
            {
                return CreateTypicalValues(
                    "Name: Risa; role: negotiator; mood: calm",
                    "Pawn profile with traits, health, and faction hints",
                    "Short persona summary used by prompt templates");
            }

            if (variableName.EndsWith(".summary", StringComparison.OrdinalIgnoreCase) ||
                variableName.EndsWith(".history", StringComparison.OrdinalIgnoreCase) ||
                variableName.EndsWith(".guidance", StringComparison.OrdinalIgnoreCase) ||
                variableName.EndsWith(".params", StringComparison.OrdinalIgnoreCase) ||
                variableName.EndsWith(".events", StringComparison.OrdinalIgnoreCase) ||
                variableName.EndsWith(".lines", StringComparison.OrdinalIgnoreCase) ||
                variableName.EndsWith(".body", StringComparison.OrdinalIgnoreCase))
            {
                return CreateTypicalValues(
                    "Multiple rendered lines of structured prompt text",
                    "Can contain line breaks and formatted bullet-style content",
                    "Often changes with current map, pawn, or dialogue context");
            }

            switch (scope)
            {
                case "ctx":
                    return CreateTypicalValues("context-dependent request value", "resolved from the active channel", "resolved from the active mode");
                case "world":
                    return CreateTypicalValues("context-dependent world value", "empty when no active map", "localized label text");
                case "pawn":
                    return CreateTypicalValues("current speaker pawn", "target pawn", "context-sensitive pawn text");
                case "dialogue":
                    return CreateTypicalValues("current turn objective", "rendered prompt text", "generated guidance snippet");
                default:
                    return CreateTypicalValues("runtime-resolved value", "empty string", "not available");
            }
        }

        private static List<string> CreateTypicalValues(string first, string second, string third)
        {
            return new List<string>
            {
                first ?? string.Empty,
                second ?? string.Empty,
                third ?? string.Empty
            };
        }

        private static List<string> CreateTypicalValues(params string[] values)
        {
            return (values ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Take(3)
                .ToList();
        }

        private static string ResolveLocalizedDescription(string descriptionKey)
        {
            if (string.IsNullOrWhiteSpace(descriptionKey))
            {
                return string.Empty;
            }

            string key = descriptionKey.Trim();
            string translated = key.Translate().ToString().Trim();
            if (string.IsNullOrWhiteSpace(translated))
            {
                return string.Empty;
            }

            return translated == key && key.StartsWith("RimChat_", StringComparison.Ordinal)
                ? string.Empty
                : translated;
        }
    }

    internal sealed class PromptVariableTooltipInfo
    {
        public PromptVariableTooltipInfo(
            string name,
            string dataType,
            string description,
            IEnumerable<string> typicalValues)
        {
            Name = name ?? string.Empty;
            DataType = dataType ?? string.Empty;
            Description = description ?? string.Empty;
            TypicalValues = (typicalValues ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Take(3)
                .ToList();
        }

        public string Name { get; }
        public string DataType { get; }
        public string Description { get; }
        public IReadOnlyList<string> TypicalValues { get; }
    }
}
