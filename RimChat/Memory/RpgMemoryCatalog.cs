using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimChat.Memory
{
    /// <summary>/// Dependencies: RimWorld `ThoughtDef` database and DefInjected translations.
    /// Responsibility: centralize RPG dialogue memory defs, alias compatibility, and automatic fallback selection.
    ///</summary>
    internal static class RpgMemoryCatalog
    {
        private sealed class MemoryProfile
        {
            public MemoryProfile(string defName, params string[] aliases)
            {
                DefName = defName;
                Aliases = aliases ?? Array.Empty<string>();
            }

            public string DefName { get; }

            public IReadOnlyList<string> Aliases { get; }
        }

        private static readonly MemoryProfile[] Profiles =
        {
            new MemoryProfile("RimChat_BriefJoy", "briefjoy", "momentofjoy", "joy", "pleasantchat", "pleasant_chat", "pleasantchatmemory", "chitchat", "chat", "chatted", "smalltalk", "small_talk", "chit_chat"),
            new MemoryProfile("RimChat_BriefSadness", "briefsadness", "sadness", "fleetingsadness"),
            new MemoryProfile("RimChat_SurpriseReaction", "surprisereaction", "surprise", "surprised"),
            new MemoryProfile("RimChat_DisgustReaction", "disgustreaction", "disgust", "disgusted"),
            new MemoryProfile("RimChat_Encouraged", "encouraged", "encouragement", "supported", "supportive"),
            new MemoryProfile("RimChat_Comforted", "comforted", "comfort", "consoled"),
            new MemoryProfile("RimChat_Mocked", "mocked", "ridiculed", "belittled"),
            new MemoryProfile("RimChat_Teased", "teased", "teasing", "embarrassed"),
            new MemoryProfile("RimChat_Praised", "praised", "praise", "appreciated"),
            new MemoryProfile("RimChat_FeelingInsulted", "feelinginsulted", "insulted", "offended", "insult"),
            new MemoryProfile("RimChat_FeelingIgnored", "feelingignored", "ignored", "dismissed", "slighted", "slight", "slightedmemory"),
            new MemoryProfile("RimChat_HeartfeltCompliment", "heartfeltcompliment", "compliment", "heartfelt", "kindwords", "kindword", "warmwords"),
            new MemoryProfile("RimChat_GratefulFeeling", "gratefulfeeling", "grateful", "gratitude", "thankful"),
            new MemoryProfile("RimChat_ShamefulMoment", "shamefulmoment", "shameful", "ashamed"),
            new MemoryProfile("RimChat_ProudMoment", "proudmoment", "proud"),
            new MemoryProfile("RimChat_ResentfulFeeling", "resentfulfeeling", "resentful", "resentment"),
            new MemoryProfile("RimChat_LonelyFeeling", "lonelyfeeling", "lonely", "loneliness"),
            new MemoryProfile("RimChat_TrustBetrayed", "trustbetrayed", "betrayed", "betrayal"),
            new MemoryProfile("RimChat_DeepConnection", "deepconnection", "deepconversation", "deepconversationmemory", "deeptalk", "deep_talk", "deepchat", "meaningfulconversation", "connection"),
            new MemoryProfile("RimChat_EmpoweringTalk", "empoweringtalk", "empowering", "empowered"),
            new MemoryProfile("RimChat_DebilitatingWords", "debilitatingwords", "debilitating", "crushed", "devastated"),
            new MemoryProfile("RimChat_UnforgettableMoment", "unforgettablemoment", "unforgettable", "memorable"),
            new MemoryProfile("RimChat_WoundingMemory", "woundingmemory", "wounding", "wounded", "lastingwound"),
            new MemoryProfile("RimChat_LoveAndDestruction", "loveanddestruction", "toxiclove", "intertwinedlove"),
            new MemoryProfile("RimChat_GoodAndEvilConflict", "goodandevilconflict", "goodandevil", "moralconflict"),
            new MemoryProfile("RimChat_LateRegret", "lateregret", "regret", "toolate"),
            new MemoryProfile("RimChat_UnconditionalCompassion", "unconditionalcompassion", "compassion", "understanding"),
            new MemoryProfile("RimChat_JoyInSuffering", "joyinsuffering", "absurdjoy", "endurance")
        };

        private static readonly string[] EarlyPositiveAuto =
        {
            "RimChat_BriefJoy",
            "RimChat_Encouraged",
            "RimChat_Praised"
        };

        private static readonly string[] MidPositiveAuto =
        {
            "RimChat_Comforted",
            "RimChat_HeartfeltCompliment",
            "RimChat_GratefulFeeling"
        };

        private static readonly string[] DeepPositiveAuto =
        {
            "RimChat_ProudMoment",
            "RimChat_DeepConnection",
            "RimChat_EmpoweringTalk"
        };

        private static readonly string[] PeakPositiveAuto =
        {
            "RimChat_UnforgettableMoment",
            "RimChat_EmpoweringTalk",
            "RimChat_DeepConnection"
        };

        private static readonly Dictionary<string, string> AliasMap = BuildAliasMap();

        public static ThoughtDef ResolveRequestedThoughtDef(string requestedDefName, out string resolvedFrom)
        {
            resolvedFrom = string.Empty;
            if (string.IsNullOrWhiteSpace(requestedDefName))
            {
                return null;
            }

            return ResolveRequestedThoughtDefCore(requestedDefName, out resolvedFrom);
        }

        private static ThoughtDef ResolveRequestedThoughtDefCore(string requestedDefName, out string resolvedFrom)
        {
            string normalized = NormalizeToken(requestedDefName);
            if (TryResolveCandidate(ResolveExactThoughtDef(requestedDefName, out string exactSource), exactSource, out resolvedFrom, out ThoughtDef exact))
            {
                return exact;
            }

            if (TryResolveCandidate(ResolveAliasThoughtDef(normalized), "catalog-alias", out resolvedFrom, out ThoughtDef alias))
            {
                return alias;
            }

            if (TryResolveCandidate(FindThoughtDefByNormalizedName(normalized), "normalized", out resolvedFrom, out ThoughtDef normalizedDef))
            {
                return normalizedDef;
            }

            return TryResolveCandidate(ResolveChatHeuristic(normalized), "chat-heuristic", out resolvedFrom, out ThoughtDef heuristic)
                ? heuristic
                : null;
        }

        private static bool TryResolveCandidate(ThoughtDef candidate, string source, out string resolvedFrom, out ThoughtDef resolvedDef)
        {
            resolvedFrom = string.Empty;
            resolvedDef = null;
            if (!IsUsableMemoryThoughtDef(candidate))
            {
                return false;
            }

            resolvedFrom = source;
            resolvedDef = candidate;
            return true;
        }

        public static string ResolveAutoDefName(int rounds)
        {
            ThoughtDef def = ResolveAutoThoughtDef(rounds);
            return def?.defName ?? string.Empty;
        }

        public static string BuildPromptExamplesText()
        {
            return string.Join(", ", GetPromptExampleDefNames());
        }

        public static string BuildPromptExamplesTextWithFallback(string fallbackText)
        {
            string examples = BuildPromptExamplesText();
            return string.IsNullOrWhiteSpace(examples) ? fallbackText : examples;
        }

        public static string BuildDisplayName(string defName)
        {
            ThoughtDef def = DefDatabase<ThoughtDef>.GetNamedSilentFail(defName);
            return BuildDisplayName(def);
        }

        public static string BuildDisplayName(ThoughtDef def)
        {
            if (def == null)
            {
                return string.Empty;
            }

            string label = def.LabelCap.ToString();
            return string.IsNullOrWhiteSpace(label) ? def.defName : label;
        }

        public static bool IsUsableMemoryThoughtDef(ThoughtDef def)
        {
            return def != null &&
                   (def.thoughtClass == null || typeof(Thought_Memory).IsAssignableFrom(def.thoughtClass));
        }

        public static string NormalizeToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return string.Empty;
            }

            var chars = new List<char>(token.Length);
            foreach (char character in token)
            {
                if (!char.IsLetterOrDigit(character))
                {
                    continue;
                }

                chars.Add(char.ToLowerInvariant(character));
            }

            return new string(chars.ToArray());
        }

        private static IReadOnlyList<string> GetPromptExampleDefNames()
        {
            var names = new List<string>();
            for (int i = 0; i < Profiles.Length; i++)
            {
                string defName = Profiles[i].DefName;
                if (DefDatabase<ThoughtDef>.GetNamedSilentFail(defName) != null)
                {
                    names.Add(defName);
                }
            }

            if (names.Count > 0)
            {
                return names;
            }

            var fallbackNames = new List<string>();
            for (int i = 0; i < Profiles.Length; i++)
            {
                fallbackNames.Add(Profiles[i].DefName);
            }

            return fallbackNames;
        }

        private static ThoughtDef ResolveExactThoughtDef(string requestedDefName, out string resolvedFrom)
        {
            resolvedFrom = string.Empty;
            ThoughtDef exact = DefDatabase<ThoughtDef>.GetNamedSilentFail(requestedDefName);
            return ResolveVisibleMemoryThoughtDef(exact, out resolvedFrom);
        }

        private static ThoughtDef ResolveVisibleMemoryThoughtDef(ThoughtDef def, out string resolvedFrom)
        {
            resolvedFrom = string.Empty;
            if (def == null)
            {
                return null;
            }

            switch (def.defName)
            {
                case "Chitchat":
                    resolvedFrom = "legacy-visible-chat";
                    return ResolveLoadedThoughtDef("RimChat_BriefJoy") ?? def;
                case "DeepTalk":
                    resolvedFrom = "legacy-visible-deep-talk";
                    return ResolveLoadedThoughtDef("RimChat_DeepConnection") ?? def;
                case "Slighted":
                    resolvedFrom = "legacy-visible-slighted";
                    return ResolveLoadedThoughtDef("RimChat_FeelingIgnored") ?? def;
                case "KindWords":
                    resolvedFrom = "mood-companion";
                    return ResolveLoadedThoughtDef("KindWordsMood") ?? def;
                case "Insulted":
                    resolvedFrom = "mood-companion";
                    return ResolveLoadedThoughtDef("InsultedMood") ?? def;
                default:
                    return def;
            }
        }

        private static ThoughtDef ResolveAliasThoughtDef(string normalized)
        {
            if (!AliasMap.TryGetValue(normalized, out string aliasTarget))
            {
                return null;
            }

            return ResolveLoadedThoughtDef(aliasTarget);
        }

        private static ThoughtDef FindThoughtDefByNormalizedName(string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            List<ThoughtDef> defs = DefDatabase<ThoughtDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                ThoughtDef def = defs[i];
                if (!IsUsableMemoryThoughtDef(def))
                {
                    continue;
                }

                if (NormalizeToken(def.defName) == normalized)
                {
                    return ResolveVisibleMemoryThoughtDef(def, out _);
                }
            }

            return null;
        }

        private static ThoughtDef ResolveChatHeuristic(string normalized)
        {
            if (!normalized.Contains("chat"))
            {
                return null;
            }

            return ResolveLoadedThoughtDef("RimChat_BriefJoy");
        }

        private static ThoughtDef ResolveAutoThoughtDef(int rounds)
        {
            string[] candidates = GetAutoCandidates(rounds);
            for (int i = 0; i < candidates.Length; i++)
            {
                ThoughtDef def = ResolveLoadedThoughtDef(candidates[i]);
                if (def != null)
                {
                    return def;
                }
            }

            return null;
        }

        private static string[] GetAutoCandidates(int rounds)
        {
            if (rounds >= 14)
            {
                return PeakPositiveAuto;
            }

            if (rounds >= 10)
            {
                return DeepPositiveAuto;
            }

            if (rounds >= 6)
            {
                return MidPositiveAuto;
            }

            return EarlyPositiveAuto;
        }

        private static ThoughtDef ResolveLoadedThoughtDef(string defName)
        {
            ThoughtDef def = DefDatabase<ThoughtDef>.GetNamedSilentFail(defName);
            return IsUsableMemoryThoughtDef(def) ? def : null;
        }

        private static Dictionary<string, string> BuildAliasMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Profiles.Length; i++)
            {
                AddAlias(map, Profiles[i].DefName, Profiles[i].DefName);
                IReadOnlyList<string> aliases = Profiles[i].Aliases;
                for (int aliasIndex = 0; aliasIndex < aliases.Count; aliasIndex++)
                {
                    AddAlias(map, aliases[aliasIndex], Profiles[i].DefName);
                }
            }

            AddAlias(map, "KindWordsMood", "KindWordsMood");
            AddAlias(map, "InsultedMood", "InsultedMood");
            AddAlias(map, "RimChat_PleasantChatMemory", "RimChat_BriefJoy");
            AddAlias(map, "RimChat_DeepConversationMemory", "RimChat_DeepConnection");
            AddAlias(map, "RimChat_SlightedMemory", "RimChat_FeelingIgnored");
            return map;
        }

        private static void AddAlias(IDictionary<string, string> map, string alias, string defName)
        {
            string normalized = NormalizeToken(alias);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            map[normalized] = defName;
        }
    }
}
