using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Config;
using RimChat.Persistence;
using RimWorld;
using Verse;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: RimWorld pawn/faction defs, user-defined rule configs, and localized labels.
    /// Responsibility: score, match, and summarize unified custom-variable rules without mutating settings.
    /// </summary>
    internal static class UserDefinedPromptVariableRuleMatcher
    {
        internal enum RuleLayer
        {
            None = 0,
            Default = 1,
            Faction = 2,
            PawnConditional = 3,
            PawnExact = 4
        }

        internal sealed class ResolvedRule
        {
            public RuleLayer Layer { get; set; }
            public string TemplateText { get; set; } = string.Empty;
            public string SourceKey { get; set; } = string.Empty;
            public int Priority { get; set; }
            public int Specificity { get; set; }
            public int Order { get; set; }
        }

        public static Pawn ResolveTargetPawn(PromptRuntimeVariableContext context)
        {
            DialogueScenarioContext scenario = context?.ScenarioContext;
            if (scenario?.IsRpg != true)
            {
                return null;
            }

            return scenario.Target ?? scenario.Initiator;
        }

        public static ResolvedRule ResolveRule(
            UserDefinedPromptVariableConfig variable,
            IEnumerable<FactionPromptVariableRuleConfig> factionRules,
            IEnumerable<PawnPromptVariableRuleConfig> pawnRules,
            PromptRuntimeVariableContext context)
        {
            Pawn targetPawn = ResolveTargetPawn(context);
            Faction scenarioFaction = context?.ScenarioContext?.Faction;

            List<ResolvedRule> matches = new List<ResolvedRule>();
            foreach (PawnPromptVariableRuleConfig rule in pawnRules ?? Enumerable.Empty<PawnPromptVariableRuleConfig>())
            {
                if (!TryCreatePawnRuleMatch(rule, targetPawn, scenarioFaction, out ResolvedRule match))
                {
                    continue;
                }

                matches.Add(match);
            }

            foreach (FactionPromptVariableRuleConfig rule in factionRules ?? Enumerable.Empty<FactionPromptVariableRuleConfig>())
            {
                if (!TryCreateFactionRuleMatch(rule, scenarioFaction, out ResolvedRule match))
                {
                    continue;
                }

                matches.Add(match);
            }

            if (matches.Count == 0)
            {
                return new ResolvedRule
                {
                    Layer = RuleLayer.Default,
                    TemplateText = variable?.DefaultTemplateText ?? string.Empty,
                    SourceKey = "default"
                };
            }

            return matches
                .OrderByDescending(item => item.Layer)
                .ThenByDescending(item => item.Priority)
                .ThenByDescending(item => item.Specificity)
                .ThenBy(item => item.Order)
                .First();
        }

        public static int ComputeSpecificity(PawnPromptVariableRuleConfig rule)
        {
            if (rule == null)
            {
                return 0;
            }

            int score = 0;
            if (!string.IsNullOrWhiteSpace(rule.NameExact))
            {
                score += 100;
            }

            if (!string.IsNullOrWhiteSpace(rule.FactionDefName))
            {
                score += 30;
            }

            if (!string.IsNullOrWhiteSpace(rule.RaceDefName))
            {
                score += 20;
            }

            if (!string.IsNullOrWhiteSpace(rule.XenotypeDefName))
            {
                score += 20;
            }

            score += NormalizeValues(rule.TraitsAll).Count * 10;
            score += NormalizeValues(rule.TraitsAny).Count * 4;

            if (!string.IsNullOrWhiteSpace(rule.Gender))
            {
                score += 5;
            }

            if (!string.IsNullOrWhiteSpace(rule.AgeStage))
            {
                score += 5;
            }

            if (!string.IsNullOrWhiteSpace(rule.PlayerControlled))
            {
                score += 8;
            }

            return score;
        }

        public static string BuildFactionRuleSummary(FactionPromptVariableRuleConfig rule)
        {
            if (rule == null)
            {
                return "RimChat_CustomVariableRuleSummary_None".Translate().ToString();
            }

            string faction = string.IsNullOrWhiteSpace(rule.FactionDefName)
                ? "RimChat_CustomVariableRuleSummary_AnyFaction".Translate().ToString()
                : rule.FactionDefName;
            return "RimChat_CustomVariableRuleSummary_Faction".Translate(faction).ToString();
        }

        public static string BuildPawnRuleSummary(PawnPromptVariableRuleConfig rule)
        {
            if (rule == null)
            {
                return "RimChat_CustomVariableRuleSummary_None".Translate().ToString();
            }

            List<string> parts = new List<string>();
            AddPart(parts, "RimChat_CustomVariableField_NameExact".Translate(), rule.NameExact);
            AddPart(parts, "RimChat_CustomVariableField_FactionDefName".Translate(), rule.FactionDefName);
            AddPart(parts, "RimChat_CustomVariableField_RaceDefName".Translate(), rule.RaceDefName);
            AddPart(parts, "RimChat_CustomVariableField_Gender".Translate(), rule.Gender);
            AddPart(parts, "RimChat_CustomVariableField_AgeStage".Translate(), rule.AgeStage);
            AddPart(parts, "RimChat_CustomVariableField_XenotypeDefName".Translate(), rule.XenotypeDefName);
            AddPart(parts, "RimChat_CustomVariableField_PlayerControlled".Translate(), TranslateBoolToken(rule.PlayerControlled));
            AddPart(parts, "RimChat_CustomVariableField_TraitsAll".Translate(), string.Join(", ", NormalizeValues(rule.TraitsAll)));
            AddPart(parts, "RimChat_CustomVariableField_TraitsAny".Translate(), string.Join(", ", NormalizeValues(rule.TraitsAny)));

            if (parts.Count == 0)
            {
                return "RimChat_CustomVariableRuleSummary_AnyPawn".Translate().ToString();
            }

            return string.Join(" | ", parts);
        }

        public static string BuildLayerLabel(RuleLayer layer)
        {
            switch (layer)
            {
                case RuleLayer.PawnExact:
                    return "RimChat_CustomVariableRuleLayer_PawnExact".Translate().ToString();
                case RuleLayer.PawnConditional:
                    return "RimChat_CustomVariableRuleLayer_PawnConditional".Translate().ToString();
                case RuleLayer.Faction:
                    return "RimChat_CustomVariableRuleLayer_Faction".Translate().ToString();
                case RuleLayer.Default:
                    return "RimChat_CustomVariableRuleLayer_Default".Translate().ToString();
                default:
                    return "RimChat_CustomVariableRuleLayer_Unknown".Translate().ToString();
            }
        }

        public static string BuildTemplateSummary(string templateText)
        {
            string trimmed = (templateText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return "RimChat_CustomVariableRuleSummary_EmptyTemplate".Translate().ToString();
            }

            return trimmed.Length <= 80 ? trimmed : trimmed.Substring(0, 77) + "...";
        }

        public static List<string> NormalizeValues(IEnumerable<string> items)
        {
            return (items ?? Enumerable.Empty<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static string NormalizePawnName(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim();
        }

        public static string ResolvePawnName(Pawn pawn)
        {
            if (pawn == null)
            {
                return string.Empty;
            }

            string fullName = pawn.Name?.ToStringFull ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(fullName))
            {
                return fullName.Trim();
            }

            return pawn.LabelShort ?? string.Empty;
        }

        private static bool TryCreatePawnRuleMatch(
            PawnPromptVariableRuleConfig rule,
            Pawn pawn,
            Faction scenarioFaction,
            out ResolvedRule match)
        {
            match = null;
            if (rule == null || !rule.Enabled || pawn == null)
            {
                return false;
            }

            if (!MatchesPawnRule(rule, pawn, scenarioFaction))
            {
                return false;
            }

            RuleLayer layer = string.IsNullOrWhiteSpace(rule.NameExact)
                ? RuleLayer.PawnConditional
                : RuleLayer.PawnExact;
            match = new ResolvedRule
            {
                Layer = layer,
                TemplateText = rule.TemplateText ?? string.Empty,
                SourceKey = rule.Id ?? string.Empty,
                Priority = rule.Priority,
                Specificity = ComputeSpecificity(rule),
                Order = rule.Order
            };
            return true;
        }

        private static bool TryCreateFactionRuleMatch(
            FactionPromptVariableRuleConfig rule,
            Faction scenarioFaction,
            out ResolvedRule match)
        {
            match = null;
            if (rule == null || !rule.Enabled)
            {
                return false;
            }

            string factionDefName = scenarioFaction?.def?.defName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rule.FactionDefName) ||
                !string.Equals(rule.FactionDefName, factionDefName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            match = new ResolvedRule
            {
                Layer = RuleLayer.Faction,
                TemplateText = rule.TemplateText ?? string.Empty,
                SourceKey = rule.Id ?? string.Empty,
                Priority = rule.Priority,
                Specificity = 30,
                Order = rule.Order
            };
            return true;
        }

        private static bool MatchesPawnRule(
            PawnPromptVariableRuleConfig rule,
            Pawn pawn,
            Faction scenarioFaction)
        {
            if (!MatchesName(rule.NameExact, pawn))
            {
                return false;
            }

            if (!MatchesFaction(rule.FactionDefName, pawn, scenarioFaction))
            {
                return false;
            }

            if (!MatchesString(rule.RaceDefName, pawn.def?.defName))
            {
                return false;
            }

            if (!MatchesString(rule.Gender, pawn.gender.ToString()))
            {
                return false;
            }

            if (!MatchesString(rule.AgeStage, pawn.ageTracker?.CurLifeStage?.defName))
            {
                return false;
            }

            if (!MatchesString(rule.XenotypeDefName, pawn.genes?.Xenotype?.defName))
            {
                return false;
            }

            if (!MatchesPlayerControlled(rule.PlayerControlled, pawn))
            {
                return false;
            }

            if (!MatchesTraitsAny(rule.TraitsAny, pawn))
            {
                return false;
            }

            return MatchesTraitsAll(rule.TraitsAll, pawn);
        }

        private static bool MatchesName(string expectedName, Pawn pawn)
        {
            if (string.IsNullOrWhiteSpace(expectedName))
            {
                return true;
            }

            string normalizedExpected = NormalizePawnName(expectedName);
            if (normalizedExpected.StartsWith(UserDefinedPromptVariableService.QuickPawnThingIdPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string thingId = normalizedExpected.Substring(UserDefinedPromptVariableService.QuickPawnThingIdPrefix.Length).Trim();
                return !string.IsNullOrWhiteSpace(thingId) &&
                       string.Equals(thingId, pawn?.ThingID ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(
                normalizedExpected,
                NormalizePawnName(ResolvePawnName(pawn)),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesFaction(string expectedFactionDefName, Pawn pawn, Faction scenarioFaction)
        {
            if (string.IsNullOrWhiteSpace(expectedFactionDefName))
            {
                return true;
            }

            string pawnFaction = pawn?.Faction?.def?.defName ?? string.Empty;
            string scenarioFactionDef = scenarioFaction?.def?.defName ?? string.Empty;
            return string.Equals(expectedFactionDefName, pawnFaction, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(expectedFactionDefName, scenarioFactionDef, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesString(string expected, string actual)
        {
            if (string.IsNullOrWhiteSpace(expected))
            {
                return true;
            }

            return string.Equals(expected.Trim(), (actual ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesPlayerControlled(string expected, Pawn pawn)
        {
            if (string.IsNullOrWhiteSpace(expected))
            {
                return true;
            }

            bool actual = pawn?.Faction != null && pawn.Faction.IsPlayer;
            bool expectedValue = string.Equals(expected.Trim(), "true", StringComparison.OrdinalIgnoreCase);
            return actual == expectedValue;
        }

        private static bool MatchesTraitsAny(IEnumerable<string> traitDefs, Pawn pawn)
        {
            List<string> expected = NormalizeValues(traitDefs);
            if (expected.Count == 0)
            {
                return true;
            }

            return expected.Any(item => HasTrait(pawn, item));
        }

        private static bool MatchesTraitsAll(IEnumerable<string> traitDefs, Pawn pawn)
        {
            List<string> expected = NormalizeValues(traitDefs);
            if (expected.Count == 0)
            {
                return true;
            }

            return expected.All(item => HasTrait(pawn, item));
        }

        private static bool HasTrait(Pawn pawn, string traitDefName)
        {
            return pawn?.story?.traits?.allTraits?.Any(item =>
                item?.def != null &&
                string.Equals(item.def.defName, traitDefName, StringComparison.OrdinalIgnoreCase)) == true;
        }

        private static void AddPart(List<string> parts, string label, string value)
        {
            if (parts == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            parts.Add($"{label}: {value.Trim()}");
        }

        private static string TranslateBoolToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return string.Empty;
            }

            return string.Equals(token.Trim(), "true", StringComparison.OrdinalIgnoreCase)
                ? "RimChat_CommonYes".Translate().ToString()
                : "RimChat_CommonNo".Translate().ToString();
        }
    }
}
