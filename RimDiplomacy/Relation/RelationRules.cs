using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimDiplomacy.Core;

namespace RimDiplomacy.Relation
{
    public class RelationRules
    {
        private static RelationRules _instance;
        public static RelationRules Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new RelationRules();
                }
                return _instance;
            }
        }

        private const string CONFIG_FILE_NAME = "relation_rules.json";

        public const string RuleFolderName = "RelationRules";

        private RelationRulesConfig _cachedConfig;
        private bool _isInitialized;

        public string BasePath
        {
            get
            {
                try
                {
                    var mod = LoadedModManager.GetMod<RimDiplomacyMod>();
                    if (mod?.Content != null)
                    {
                        string dir = System.IO.Path.Combine(mod.Content.RootDir, RuleFolderName);
                        if (!System.IO.Directory.Exists(dir))
                        {
                            System.IO.Directory.CreateDirectory(dir);
                        }
                        return dir;
                    }
                }
                catch { }

                string fallbackDir = System.IO.Path.Combine(GenFilePaths.SaveDataFolderPath, "RimDiplomacy", "rules");
                if (!System.IO.Directory.Exists(fallbackDir))
                {
                    System.IO.Directory.CreateDirectory(fallbackDir);
                }
                return fallbackDir;
            }
        }

        public string ConfigFilePath
        {
            get
            {
                return System.IO.Path.Combine(BasePath, CONFIG_FILE_NAME);
            }
        }

        public void Initialize()
        {
            if (_isInitialized) return;

            try
            {
                EnsureDirectoryExists();
                _cachedConfig = LoadConfig();
                _isInitialized = true;
                Log.Message($"[RimDiplomacy] RelationRules initialized, config path: {ConfigFilePath}");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimDiplomacy] Failed to initialize RelationRules: {ex}");
                _cachedConfig = CreateDefaultConfig();
            }
        }

        public bool ConfigExists()
        {
            return System.IO.File.Exists(ConfigFilePath);
        }

        public RelationRulesConfig LoadConfig()
        {
            try
            {
                EnsureDirectoryExists();

                if (System.IO.File.Exists(ConfigFilePath))
                {
                    string json = System.IO.File.ReadAllText(ConfigFilePath);
                    var config = ParseJsonToConfig(json);

                    if (config != null)
                    {
                        _cachedConfig = config;
                        Log.Message($"[RimDiplomacy] Loaded RelationRulesConfig from file");
                        return config;
                    }
                }

                Log.Message($"[RimDiplomacy] RelationRules config not found, creating default config");
                _cachedConfig = CreateDefaultConfig();
                SaveConfig(_cachedConfig);
                return _cachedConfig;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimDiplomacy] Failed to load RelationRules config: {ex}");
                return _cachedConfig ?? CreateDefaultConfig();
            }
        }

        public void SaveConfig(RelationRulesConfig config)
        {
            try
            {
                EnsureDirectoryExists();

                if (config == null)
                {
                    Log.Warning("[RimDiplomacy] Attempted to save null RelationRules config");
                    return;
                }

                string json = SerializeConfigToJson(config);
                System.IO.File.WriteAllText(ConfigFilePath, json);
                _cachedConfig = config;

                Log.Message($"[RimDiplomacy] Saved RelationRulesConfig to: {ConfigFilePath}");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimDiplomacy] Failed to save RelationRules config: {ex}");
            }
        }

        public RelationRulesConfig GetConfig()
        {
            if (_cachedConfig == null)
            {
                LoadConfig();
            }
            return _cachedConfig;
        }

        public void ResetToDefault()
        {
            try
            {
                _cachedConfig = CreateDefaultConfig();
                SaveConfig(_cachedConfig);
                Log.Message("[RimDiplomacy] Reset RelationRulesConfig to default");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimDiplomacy] Failed to reset RelationRules config: {ex}");
            }
        }

        public string BuildRulesPrompt(RelationRulesConfig config)
        {
            if (config == null || !config.IsEnabled)
            {
                return "";
            }

            var sb = new System.Text.StringBuilder();

            sb.AppendLine();
            sb.AppendLine("=== RELATION-BASED ACTION RULES ===");
            sb.AppendLine("The following rules determine when and how diplomatic actions can be performed based on 5-dimension relationship values:");
            sb.AppendLine();

            if (config.EnableGiftRules && config.GiftRules != null)
            {
                AppendGiftRules(sb, config.GiftRules);
            }

            if (config.EnableAidRules && config.AidRules != null)
            {
                AppendAidRules(sb, config.AidRules);
            }

            if (config.EnableWarRules && config.WarRules != null)
            {
                AppendWarRules(sb, config.WarRules);
            }

            if (config.EnablePeaceRules && config.PeaceRules != null)
            {
                AppendPeaceRules(sb, config.PeaceRules);
            }

            if (config.EnableTradeRules && config.TradeRules != null)
            {
                AppendTradeRules(sb, config.TradeRules);
            }

            if (config.EnableConsumptionRules)
            {
                AppendConsumptionRules(sb);
            }

            if (config.EnableChainReactionRules)
            {
                AppendChainReactionRules(sb);
            }

            return sb.ToString();
        }

        private void AppendGiftRules(System.Text.StringBuilder sb, ActionRuleConfig rule)
        {
            sb.AppendLine("### 1. SEND GIFT (GIFT)");
            sb.AppendLine($"- **Entry Formula**: {rule.EntryFormula}");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(rule.AcceptanceFormula))
            {
                sb.AppendLine("**Acceptance Calculation (Gift-specific)**");
                sb.AppendLine(rule.AcceptanceFormula);
                sb.AppendLine();
            }

            sb.AppendLine("**Acceptance Result Effects:**");
            if (rule.HighAcceptanceEffects != null)
                sb.AppendLine($"- Acceptance ≥ 70: {rule.HighAcceptanceEffects}");
            if (rule.MediumAcceptanceEffects != null)
                sb.AppendLine($"- Acceptance 30-69: {rule.MediumAcceptanceEffects}");
            if (rule.LowAcceptanceEffects != null)
                sb.AppendLine($"- Acceptance < 30: {rule.LowAcceptanceEffects}");

            sb.AppendLine();
        }

        private void AppendAidRules(System.Text.StringBuilder sb, ActionRuleConfig rule)
        {
            sb.AppendLine("### 2. REQUEST AID (AID)");
            sb.AppendLine($"- **Entry Formula**: {rule.EntryFormula}");
            sb.AppendLine($"- **Veto Red Line**: {rule.VetoFormula}");
            sb.AppendLine();
        }

        private void AppendWarRules(System.Text.StringBuilder sb, ActionRuleConfig rule)
        {
            sb.AppendLine("### 3. DECLARE WAR (WAR)");
            sb.AppendLine($"- **Entry Formula (Any condition met)**:");
            sb.AppendLine(rule.EntryFormula);
            sb.AppendLine();

            if (!string.IsNullOrEmpty(rule.LockCondition))
            {
                sb.AppendLine($"- **Lock Condition**: {rule.LockCondition} (Cannot declare war even if entry conditions are met)");
            }
            sb.AppendLine();
        }

        private void AppendPeaceRules(System.Text.StringBuilder sb, ActionRuleConfig rule)
        {
            sb.AppendLine("### 4. PROPOSE PEACE (PEACE)");
            sb.AppendLine($"- **Entry Formula**: {rule.EntryFormula}");
            if (!string.IsNullOrEmpty(rule.AccelerateFormula))
            {
                sb.AppendLine($"- **Accelerate Condition**: {rule.AccelerateFormula}");
            }
            if (!string.IsNullOrEmpty(rule.VetoFormula))
            {
                sb.AppendLine($"- **Veto Condition**: {rule.VetoFormula}");
            }
            sb.AppendLine();
        }

        private void AppendTradeRules(System.Text.StringBuilder sb, ActionRuleConfig rule)
        {
            sb.AppendLine("### 5. REQUEST TRADE CARAVAN (TRADE)");
            sb.AppendLine($"- **Entry Formula**: {rule.EntryFormula}");
            if (!string.IsNullOrEmpty(rule.VetoFormula))
            {
                sb.AppendLine($"- **Veto Red Line**: {rule.VetoFormula}");
            }
            sb.AppendLine();
        }

        private void AppendConsumptionRules(System.Text.StringBuilder sb)
        {
            sb.AppendLine("### Consumption Principles");
            sb.AppendLine("1. **All actions consume Influence** (Diplomacy is the art of persuasion)");
            sb.AppendLine("2. **Request-type actions additionally consume Reciprocity** (Relationship balance tilts)");
            sb.AppendLine("3. **Give-type actions may consume/supplement Reciprocity** (Depends on acceptance)");
            sb.AppendLine("4. **Trust is never directly consumed**, it only changes with action results");
            sb.AppendLine();
        }

        private void AppendChainReactionRules(System.Text.StringBuilder sb)
        {
            sb.AppendLine("### Chain Reaction Rules");
            sb.AppendLine();
            sb.AppendLine("#### Post-Action Effects");
            sb.AppendLine("1. **Continuous Gifting** (within 3 turns): From the second gift, influence cost doubles, acceptance -20 (diminishing returns)");
            sb.AppendLine("2. **Aid then War**: Trust immediately -80 (betrayal penalty), all faction allies' Intimacy -30");
            sb.AppendLine("3. **Peace then immediate Trade request**: Trust threshold temporarily +20 (push your luck penalty)");
            sb.AppendLine("4. **War during Trade caravan**: Respect -50 (damaged commercial reputation), all third-party Trust -20");
            sb.AppendLine();
            sb.AppendLine("#### Soft Limits");
            sb.AppendLine("- Single action can change values by max ±30");
            sb.AppendLine("- When absolute value ≥80, same-direction change rate is halved (extreme values are harder to reach)");
            sb.AppendLine();
        }

        private void EnsureDirectoryExists()
        {
            try
            {
                if (!System.IO.Directory.Exists(BasePath))
                {
                    System.IO.Directory.CreateDirectory(BasePath);
                    Log.Message($"[RimDiplomacy] Created RelationRules directory: {BasePath}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimDiplomacy] Failed to create RelationRules directory: {ex}");
            }
        }

        private RelationRulesConfig CreateDefaultConfig()
        {
            var config = new RelationRulesConfig();

            config.IsEnabled = true;
            config.EnableGiftRules = true;
            config.GiftRules = new ActionRuleConfig
            {
                Name = "Gift",
                EntryFormula = "Reciprocity ≥ -50 AND Trust ≥ -80",
                AcceptanceFormula = "Acceptance = 50 + (Intimacy × 0.3) + (Trust × 0.2) + (Respect × 0.1) + Random(-10, +10)",
                HighAcceptanceEffects = "Reciprocity +15, Intimacy +5, Trust +5",
                MediumAcceptanceEffects = "Reciprocity +5, Intimacy +2",
                LowAcceptanceEffects = "Reciprocity -5 (seen as insult/bribery), Trust -10"
            };

            config.EnableAidRules = true;
            config.AidRules = new ActionRuleConfig
            {
                Name = "Aid",
                EntryFormula = "(Reciprocity × 0.6) + (Intimacy × 0.3) + (Trust × 0.1) ≥ 15",
                VetoFormula = "Respect < -40 (sees you as burden) OR Influence < -30 (request method is stupid)"
            };

            config.EnableWarRules = true;
            config.WarRules = new ActionRuleConfig
            {
                Name = "War",
                EntryFormula = "A. Intimacy ≤ -60 AND Trust ≤ -30 (emotional + interest break)\nB. Reciprocity ≤ -50 AND Respect ≥ -10 (exploited and can win)\nC. Trust ≤ -70 AND Influence ≤ -20 (predicts betrayal)",
                LockCondition = "Intimacy ≥ +30 (emotional bonds prevent war)"
            };

            config.EnablePeaceRules = true;
            config.PeaceRules = new ActionRuleConfig
            {
                Name = "Peace",
                EntryFormula = "Trust ≥ +20 AND Influence ≥ +10",
                AccelerateFormula = "Reciprocity ≥ +20 (with compensation) OR Intimacy ≥ 0 (old feelings remain)",
                VetoFormula = "Currently at war AND Respect < -20 (thinks it has you beat)"
            };

            config.EnableTradeRules = true;
            config.TradeRules = new ActionRuleConfig
            {
                Name = "Trade",
                EntryFormula = "Trust ≥ +35 AND Respect ≥ +10 AND Reciprocity ≥ -20",
                VetoFormula = "Intimacy ≤ -40 (emotional rejection overrides rational interest)"
            };

            config.EnableConsumptionRules = true;
            config.EnableChainReactionRules = true;

            return config;
        }

        private string SerializeConfigToJson(RelationRulesConfig config, bool prettyPrint = false)
        {
            var sb = new System.Text.StringBuilder();

            if (prettyPrint)
            {
                sb.AppendLine("{");
                sb.AppendLine($"  \"IsEnabled\": {config.IsEnabled.ToString().ToLower()},");
                sb.AppendLine($"  \"EnableGiftRules\": {config.EnableGiftRules.ToString().ToLower()},");
                sb.AppendLine($"  \"EnableAidRules\": {config.EnableAidRules.ToString().ToLower()},");
                sb.AppendLine($"  \"EnableWarRules\": {config.EnableWarRules.ToString().ToLower()},");
                sb.AppendLine($"  \"EnablePeaceRules\": {config.EnablePeaceRules.ToString().ToLower()},");
                sb.AppendLine($"  \"EnableTradeRules\": {config.EnableTradeRules.ToString().ToLower()},");
                sb.AppendLine($"  \"EnableConsumptionRules\": {config.EnableConsumptionRules.ToString().ToLower()},");
                sb.AppendLine($"  \"EnableChainReactionRules\": {config.EnableChainReactionRules.ToString().ToLower()},");
                sb.AppendLine("  \"GiftRules\": " + SerializeActionRule(config.GiftRules, true) + ",");
                sb.AppendLine("  \"AidRules\": " + SerializeActionRule(config.AidRules, true) + ",");
                sb.AppendLine("  \"WarRules\": " + SerializeActionRule(config.WarRules, true) + ",");
                sb.AppendLine("  \"PeaceRules\": " + SerializeActionRule(config.PeaceRules, true) + ",");
                sb.AppendLine("  \"TradeRules\": " + SerializeActionRule(config.TradeRules, true));
                sb.AppendLine("}");
            }
            else
            {
                sb.Append("{");
                sb.Append($"\"IsEnabled\":{config.IsEnabled.ToString().ToLower()},");
                sb.Append($"\"EnableGiftRules\":{config.EnableGiftRules.ToString().ToLower()},");
                sb.Append($"\"EnableAidRules\":{config.EnableAidRules.ToString().ToLower()},");
                sb.Append($"\"EnableWarRules\":{config.EnableWarRules.ToString().ToLower()},");
                sb.Append($"\"EnablePeaceRules\":{config.EnablePeaceRules.ToString().ToLower()},");
                sb.Append($"\"EnableTradeRules\":{config.EnableTradeRules.ToString().ToLower()},");
                sb.Append($"\"EnableConsumptionRules\":{config.EnableConsumptionRules.ToString().ToLower()},");
                sb.Append($"\"EnableChainReactionRules\":{config.EnableChainReactionRules.ToString().ToLower()},");
                sb.Append($"\"GiftRules\":{SerializeActionRule(config.GiftRules, false)},");
                sb.Append($"\"AidRules\":{SerializeActionRule(config.AidRules, false)},");
                sb.Append($"\"WarRules\":{SerializeActionRule(config.WarRules, false)},");
                sb.Append($"\"PeaceRules\":{SerializeActionRule(config.PeaceRules, false)},");
                sb.Append($"\"TradeRules\":{SerializeActionRule(config.TradeRules, false)}");
                sb.Append("}");
            }

            return sb.ToString();
        }

        private string SerializeActionRule(ActionRuleConfig rule, bool prettyPrint)
        {
            if (rule == null) return "null";

            if (prettyPrint)
            {
                return $"{{\"Name\":\"{EscapeJson(rule.Name)}\",\"EntryFormula\":\"{EscapeJson(rule.EntryFormula)}\",\"AcceptanceFormula\":\"{EscapeJson(rule.AcceptanceFormula)}\",\"HighAcceptanceEffects\":\"{EscapeJson(rule.HighAcceptanceEffects)}\",\"MediumAcceptanceEffects\":\"{EscapeJson(rule.MediumAcceptanceEffects)}\",\"LowAcceptanceEffects\":\"{EscapeJson(rule.LowAcceptanceEffects)}\",\"VetoFormula\":\"{EscapeJson(rule.VetoFormula)}\",\"LockCondition\":\"{EscapeJson(rule.LockCondition)}\",\"AccelerateFormula\":\"{EscapeJson(rule.AccelerateFormula)}\"}}";
            }
            else
            {
                return $"{{\"Name\":\"{EscapeJson(rule.Name)}\",\"EntryFormula\":\"{EscapeJson(rule.EntryFormula)}\",\"AcceptanceFormula\":\"{EscapeJson(rule.AcceptanceFormula)}\",\"HighAcceptanceEffects\":\"{EscapeJson(rule.HighAcceptanceEffects)}\",\"MediumAcceptanceEffects\":\"{EscapeJson(rule.MediumAcceptanceEffects)}\",\"LowAcceptanceEffects\":\"{EscapeJson(rule.LowAcceptanceEffects)}\",\"VetoFormula\":\"{EscapeJson(rule.VetoFormula)}\",\"LockCondition\":\"{EscapeJson(rule.LockCondition)}\",\"AccelerateFormula\":\"{EscapeJson(rule.AccelerateFormula)}\"}}";
            }
        }

        private RelationRulesConfig ParseJsonToConfig(string json)
        {
            var config = new RelationRulesConfig();

            try
            {
                config.IsEnabled = ParseBool(json, "IsEnabled", true);
                config.EnableGiftRules = ParseBool(json, "EnableGiftRules", true);
                config.EnableAidRules = ParseBool(json, "EnableAidRules", true);
                config.EnableWarRules = ParseBool(json, "EnableWarRules", true);
                config.EnablePeaceRules = ParseBool(json, "EnablePeaceRules", true);
                config.EnableTradeRules = ParseBool(json, "EnableTradeRules", true);
                config.EnableConsumptionRules = ParseBool(json, "EnableConsumptionRules", true);
                config.EnableChainReactionRules = ParseBool(json, "EnableChainReactionRules", true);

                config.GiftRules = ParseActionRule(json, "GiftRules");
                config.AidRules = ParseActionRule(json, "AidRules");
                config.WarRules = ParseActionRule(json, "WarRules");
                config.PeaceRules = ParseActionRule(json, "PeaceRules");
                config.TradeRules = ParseActionRule(json, "TradeRules");

                return config;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimDiplomacy] Failed to parse RelationRules JSON: {ex.Message}");
                return null;
            }
        }

        private bool ParseBool(string json, string key, bool defaultValue)
        {
            string pattern = $"\"{key}\":";
            int index = json.IndexOf(pattern);
            if (index < 0) return defaultValue;

            int start = index + pattern.Length;
            int end = json.IndexOfAny(new[] { ',', '}', ']' }, start);
            if (end < 0) end = json.Length;

            string value = json.Substring(start, end - start).Trim().ToLower();
            return value == "true";
        }

        private ActionRuleConfig ParseActionRule(string json, string key)
        {
            int ruleStart = json.IndexOf($"\"{key}\":");
            if (ruleStart < 0) return null;

            int objStart = json.IndexOf("{", ruleStart);
            if (objStart < 0) return null;

            int depth = 1;
            int objEnd = objStart + 1;
            while (objEnd < json.Length && depth > 0)
            {
                if (json[objEnd] == '{') depth++;
                else if (json[objEnd] == '}') depth--;
                objEnd++;
            }

            string objContent = json.Substring(objStart, objEnd - objStart);

            return new ActionRuleConfig
            {
                Name = ExtractString(objContent, "Name"),
                EntryFormula = ExtractString(objContent, "EntryFormula"),
                AcceptanceFormula = ExtractString(objContent, "AcceptanceFormula"),
                HighAcceptanceEffects = ExtractString(objContent, "HighAcceptanceEffects"),
                MediumAcceptanceEffects = ExtractString(objContent, "MediumAcceptanceEffects"),
                LowAcceptanceEffects = ExtractString(objContent, "LowAcceptanceEffects"),
                VetoFormula = ExtractString(objContent, "VetoFormula"),
                LockCondition = ExtractString(objContent, "LockCondition"),
                AccelerateFormula = ExtractString(objContent, "AccelerateFormula")
            };
        }

        private string ExtractString(string json, string key)
        {
            string pattern = $"\"{key}\":";
            int index = json.IndexOf(pattern);
            if (index < 0) return "";

            int start = json.IndexOf("\"", index + pattern.Length);
            if (start < 0) return "";

            start++;
            var sb = new System.Text.StringBuilder();

            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"')
                {
                    break;
                }
                else if (c == '\\' && i + 1 < json.Length)
                {
                    char next = json[i + 1];
                    switch (next)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(next); break;
                    }
                    i++;
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private string EscapeJson(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }

    public class RelationRulesConfig
    {
        public bool IsEnabled = true;
        public bool EnableGiftRules = true;
        public bool EnableAidRules = true;
        public bool EnableWarRules = true;
        public bool EnablePeaceRules = true;
        public bool EnableTradeRules = true;
        public bool EnableConsumptionRules = true;
        public bool EnableChainReactionRules = true;

        public ActionRuleConfig GiftRules;
        public ActionRuleConfig AidRules;
        public ActionRuleConfig WarRules;
        public ActionRuleConfig PeaceRules;
        public ActionRuleConfig TradeRules;
    }

    public class ActionRuleConfig
    {
        public string Name = "";
        public string EntryFormula = "";
        public string AcceptanceFormula = "";
        public string HighAcceptanceEffects = "";
        public string MediumAcceptanceEffects = "";
        public string LowAcceptanceEffects = "";
        public string VetoFormula = "";
        public string LockCondition = "";
        public string AccelerateFormula = "";
    }
}
