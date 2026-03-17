using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Config;
using RimWorld;
using Verse;

namespace RimChat.Prompting
{
    internal enum QuickPromptTargetKind
    {
        Faction = 0,
        Pawn = 1
    }

    internal enum QuickPromptConflictDecision
    {
        ReuseExisting = 0,
        TakeOver = 1
    }

    /// <summary>
    /// Dependencies: unified custom-variable CRUD/validation pipeline plus in-game faction/pawn runtime objects.
    /// Responsibility: provide fixed-slot quick prompt helpers for faction and pawn persona rule editing.
    /// </summary>
    internal static partial class UserDefinedPromptVariableService
    {
        private const string QuickFactionPersonaKey = "quick_faction_persona";
        private const string QuickPawnPersonaKey = "quick_pawn_persona";
        private const string QuickFactionVariableIdPrefix = "rimchat_quick_faction_var_";
        private const string QuickPawnVariableIdPrefix = "rimchat_quick_pawn_var_";
        private const string QuickFactionRuleIdPrefix = "rimchat_quick_faction_rule_";
        private const string QuickPawnRuleIdPrefix = "rimchat_quick_pawn_rule_";
        internal const string QuickPawnThingIdPrefix = "thingid:";

        public static string BuildQuickPath(QuickPromptTargetKind kind)
        {
            return BuildPath(GetQuickKey(kind));
        }

        public static string BuildQuickToken(QuickPromptTargetKind kind)
        {
            return "{{ " + BuildQuickPath(kind) + " }}";
        }

        public static bool RequiresQuickConflictResolution(RimChat.Config.RimChatSettings settings, QuickPromptTargetKind kind)
        {
            UserDefinedPromptVariableConfig variable = FindVariableByKey(GetQuickKey(kind), settings);
            return variable != null &&
                   !IsQuickManagedVariable(variable, kind) &&
                   !HasQuickManagedRules(settings, kind);
        }

        public static string GetQuickFactionTemplate(RimChat.Config.RimChatSettings settings, Faction faction)
        {
            if (faction?.def == null)
            {
                return string.Empty;
            }

            string key = GetQuickKey(QuickPromptTargetKind.Faction);
            FactionPromptVariableRuleConfig rule = GetFactionRulesForKey(key, settings)
                .FirstOrDefault(item =>
                    item != null &&
                    string.Equals(item.FactionDefName, faction.def.defName, StringComparison.OrdinalIgnoreCase));
            return rule?.TemplateText ?? string.Empty;
        }

        public static string GetQuickPawnTemplate(RimChat.Config.RimChatSettings settings, Pawn pawn)
        {
            PawnPromptVariableRuleConfig rule = FindQuickPawnRule(GetPawnRulesForKey(GetQuickKey(QuickPromptTargetKind.Pawn), settings), pawn);
            return rule?.TemplateText ?? string.Empty;
        }

        public static bool TrySaveQuickFactionPrompt(
            RimChat.Config.RimChatSettings settings,
            Faction faction,
            string templateText,
            QuickPromptConflictDecision decision,
            out UserDefinedPromptVariableValidationResult validationResult)
        {
            validationResult = new UserDefinedPromptVariableValidationResult();
            if (settings == null || faction?.def == null)
            {
                validationResult.Errors.Add("Quick faction prompt target is unavailable.");
                return false;
            }

            string key = GetQuickKey(QuickPromptTargetKind.Faction);
            UserDefinedPromptVariableConfig originalVariable = FindVariableByKey(key, settings)?.Clone();
            UserDefinedPromptVariableEditModel model = BuildQuickEditModel(settings, QuickPromptTargetKind.Faction, decision);
            FactionPromptVariableRuleConfig rule = model.FactionRules.FirstOrDefault(item =>
                item != null &&
                string.Equals(item.FactionDefName, faction.def.defName, StringComparison.OrdinalIgnoreCase));
            if (rule == null)
            {
                rule = new FactionPromptVariableRuleConfig
                {
                    Id = CreateQuickRuleId(QuickPromptTargetKind.Faction),
                    VariableKey = key,
                    FactionDefName = faction.def.defName,
                    Priority = 0,
                    Enabled = true,
                    Order = model.FactionRules.Count
                };
                model.FactionRules.Add(rule);
            }

            rule.Id = EnsureQuickRuleId(rule.Id, QuickPromptTargetKind.Faction);
            rule.VariableKey = key;
            rule.FactionDefName = faction.def.defName;
            rule.TemplateText = templateText ?? string.Empty;
            rule.Enabled = true;
            return TrySaveEdit(settings, model, originalVariable, out validationResult);
        }

        public static bool TrySaveQuickPawnPrompt(
            RimChat.Config.RimChatSettings settings,
            Pawn pawn,
            string templateText,
            QuickPromptConflictDecision decision,
            out UserDefinedPromptVariableValidationResult validationResult)
        {
            validationResult = new UserDefinedPromptVariableValidationResult();
            if (settings == null || pawn == null)
            {
                validationResult.Errors.Add("Quick pawn prompt target is unavailable.");
                return false;
            }

            string key = GetQuickKey(QuickPromptTargetKind.Pawn);
            UserDefinedPromptVariableConfig originalVariable = FindVariableByKey(key, settings)?.Clone();
            UserDefinedPromptVariableEditModel model = BuildQuickEditModel(settings, QuickPromptTargetKind.Pawn, decision);
            PawnPromptVariableRuleConfig rule = FindQuickPawnRule(model.PawnRules, pawn);
            if (rule == null)
            {
                rule = new PawnPromptVariableRuleConfig
                {
                    Id = CreateQuickRuleId(QuickPromptTargetKind.Pawn),
                    VariableKey = key,
                    Priority = 0,
                    Enabled = true,
                    Order = model.PawnRules.Count
                };
                model.PawnRules.Add(rule);
            }

            rule.Id = EnsureQuickRuleId(rule.Id, QuickPromptTargetKind.Pawn);
            rule.VariableKey = key;
            rule.NameExact = BuildQuickPawnMatchToken(pawn);
            rule.FactionDefName = string.Empty;
            rule.RaceDefName = string.Empty;
            rule.Gender = string.Empty;
            rule.AgeStage = string.Empty;
            rule.XenotypeDefName = string.Empty;
            rule.PlayerControlled = string.Empty;
            rule.TraitsAny = new List<string>();
            rule.TraitsAll = new List<string>();
            rule.TemplateText = templateText ?? string.Empty;
            rule.Enabled = true;
            return TrySaveEdit(settings, model, originalVariable, out validationResult);
        }

        public static string BuildQuickPawnMatchToken(Pawn pawn)
        {
            return pawn == null || string.IsNullOrWhiteSpace(pawn.ThingID)
                ? string.Empty
                : QuickPawnThingIdPrefix + pawn.ThingID.Trim();
        }

        private static UserDefinedPromptVariableEditModel BuildQuickEditModel(
            RimChat.Config.RimChatSettings settings,
            QuickPromptTargetKind kind,
            QuickPromptConflictDecision decision)
        {
            string key = GetQuickKey(kind);
            UserDefinedPromptVariableConfig existing = FindVariableByKey(key, settings)?.Clone();
            UserDefinedPromptVariableConfig variable = existing ?? BuildOfficialQuickVariable(kind);
            if (existing != null && decision == QuickPromptConflictDecision.TakeOver)
            {
                ApplyOfficialQuickVariableMetadata(variable, kind);
            }

            return new UserDefinedPromptVariableEditModel
            {
                Variable = variable,
                FactionRules = GetFactionRulesForKey(key, settings),
                PawnRules = GetPawnRulesForKey(key, settings)
            };
        }

        private static UserDefinedPromptVariableConfig BuildOfficialQuickVariable(QuickPromptTargetKind kind)
        {
            var variable = new UserDefinedPromptVariableConfig();
            ApplyOfficialQuickVariableMetadata(variable, kind);
            return variable;
        }

        private static void ApplyOfficialQuickVariableMetadata(UserDefinedPromptVariableConfig variable, QuickPromptTargetKind kind)
        {
            if (variable == null)
            {
                return;
            }

            variable.Id = CreateQuickVariableId(kind);
            variable.Key = GetQuickKey(kind);
            variable.DisplayName = BuildQuickPath(kind);
            variable.Description = GetQuickDescription(kind);
            variable.DefaultTemplateText = string.Empty;
            variable.Enabled = true;
        }

        private static bool HasQuickManagedRules(RimChat.Config.RimChatSettings settings, QuickPromptTargetKind kind)
        {
            string prefix = GetQuickRuleIdPrefix(kind);
            if (kind == QuickPromptTargetKind.Faction)
            {
                return GetFactionRulesForKey(GetQuickKey(kind), settings)
                    .Any(item => item != null && HasQuickIdPrefix(item.Id, prefix));
            }

            return GetPawnRulesForKey(GetQuickKey(kind), settings)
                .Any(item => item != null && HasQuickIdPrefix(item.Id, prefix));
        }

        private static bool IsQuickManagedVariable(UserDefinedPromptVariableConfig variable, QuickPromptTargetKind kind)
        {
            return variable != null &&
                   string.Equals(NormalizeKey(variable.Key), GetQuickKey(kind), StringComparison.OrdinalIgnoreCase) &&
                   HasQuickIdPrefix(variable.Id, GetQuickVariableIdPrefix(kind));
        }

        private static PawnPromptVariableRuleConfig FindQuickPawnRule(IEnumerable<PawnPromptVariableRuleConfig> rules, Pawn pawn)
        {
            if (pawn == null)
            {
                return null;
            }

            string quickToken = BuildQuickPawnMatchToken(pawn);
            string resolvedName = UserDefinedPromptVariableRuleMatcher.ResolvePawnName(pawn);
            return (rules ?? Enumerable.Empty<PawnPromptVariableRuleConfig>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.NameExact))
                .FirstOrDefault(item =>
                    string.Equals(item.NameExact, quickToken, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.NameExact, resolvedName, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetQuickKey(QuickPromptTargetKind kind)
        {
            return kind == QuickPromptTargetKind.Faction
                ? QuickFactionPersonaKey
                : QuickPawnPersonaKey;
        }

        private static string GetQuickDescription(QuickPromptTargetKind kind)
        {
            return kind == QuickPromptTargetKind.Faction
                ? "RimChat_PromptWorkbench_QuickFactionDescription".Translate().ToString()
                : "RimChat_PromptWorkbench_QuickPawnDescription".Translate().ToString();
        }

        private static string GetQuickVariableIdPrefix(QuickPromptTargetKind kind)
        {
            return kind == QuickPromptTargetKind.Faction
                ? QuickFactionVariableIdPrefix
                : QuickPawnVariableIdPrefix;
        }

        private static string GetQuickRuleIdPrefix(QuickPromptTargetKind kind)
        {
            return kind == QuickPromptTargetKind.Faction
                ? QuickFactionRuleIdPrefix
                : QuickPawnRuleIdPrefix;
        }

        private static string CreateQuickVariableId(QuickPromptTargetKind kind)
        {
            return GetQuickVariableIdPrefix(kind) + Guid.NewGuid().ToString("N");
        }

        private static string CreateQuickRuleId(QuickPromptTargetKind kind)
        {
            return GetQuickRuleIdPrefix(kind) + Guid.NewGuid().ToString("N");
        }

        private static string EnsureQuickRuleId(string existingId, QuickPromptTargetKind kind)
        {
            string prefix = GetQuickRuleIdPrefix(kind);
            return HasQuickIdPrefix(existingId, prefix)
                ? existingId
                : prefix + Guid.NewGuid().ToString("N");
        }

        private static bool HasQuickIdPrefix(string value, string prefix)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
