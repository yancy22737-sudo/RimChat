using System;
using System.Collections.Generic;
using System.IO;
using RimChat.Core;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>/// Dependencies: UnityEngine.JsonUtility, RimWorld mod path APIs, file system.
 /// Responsibility: represent default RPG prompt text loaded from Prompt/Default/RpgPrompts_Default.json.
 ///</summary>
    [Serializable]
    internal sealed class RpgPromptDefaultsConfig
    {
        public string RoleSettingDefault;
        public string DialogueStyleDefault;
        public string FormatConstraintDefault;
        public string RoleSettingFallbackTemplate;
        public string FormatConstraintHeader;
        public string CompactFormatFallback;
        public string ActionReliabilityFallback;
        public string ActionReliabilityMarker;
        public RpgApiActionPromptConfig ApiActionPrompt;

        public static RpgPromptDefaultsConfig CreateFallback()
        {
            return new RpgPromptDefaultsConfig
            {
                RoleSettingDefault = "You are an AI-controlled NPC in RimWorld. Your goal is to engage in immersive, character-driven dialogue with the player.",
                DialogueStyleDefault = "Keep your responses concise, oral, and immersive. Avoid robotic or overly formal language.",
                FormatConstraintDefault =
                    "Please output a JSON code block after your text only when you need gameplay effects.\n\nFormat:\n```json\n{\n  \"actions\": [\n    { \"action\": \"TryGainMemory\", \"defName\": \"Chitchat\" },\n    { \"action\": \"RomanceAttempt\" },\n    { \"action\": \"Date\" },\n    { \"action\": \"MarriageProposal\" },\n    { \"action\": \"Breakup\" },\n    { \"action\": \"Divorce\" },\n    { \"action\": \"GrantInspiration\", \"defName\": \"Inspired_Creativity\" },\n    { \"action\": \"TriggerIncident\", \"defName\": \"RaidEnemy\", \"amount\": 500 },\n    { \"action\": \"ExitDialogue\", \"reason\": \"Let's pause here.\" },\n    { \"action\": \"ExitDialogueCooldown\", \"reason\": \"Do not contact me again for now.\" }\n  ]\n}\n```\nIMPORTANT: Use EXACTLY the format above. 'actions' must be an array of objects. If no gameplay effects occur, omit the JSON block.",
                RoleSettingFallbackTemplate = "Roleplay as {{target_name}} in the current RimWorld context.",
                FormatConstraintHeader = "=== FORMAT CONSTRAINT (REQUIRED) ===",
                CompactFormatFallback = "Only emit gameplay-effect JSON when needed; omit it when there are no gameplay effects.",
                ActionReliabilityFallback = "Reliability rules: keep actions role-consistent and avoid prolonged no-action streaks.",
                ActionReliabilityMarker = "Reliability rules:",
                ApiActionPrompt = RpgApiActionPromptConfig.CreateFallback()
            };
        }

        public void NormalizeWith(RpgPromptDefaultsConfig fallback)
        {
            if (fallback == null)
            {
                fallback = CreateFallback();
            }

            RoleSettingDefault = Coalesce(RoleSettingDefault, fallback.RoleSettingDefault);
            DialogueStyleDefault = Coalesce(DialogueStyleDefault, fallback.DialogueStyleDefault);
            FormatConstraintDefault = Coalesce(FormatConstraintDefault, fallback.FormatConstraintDefault);
            RoleSettingFallbackTemplate = Coalesce(RoleSettingFallbackTemplate, fallback.RoleSettingFallbackTemplate);
            FormatConstraintHeader = Coalesce(FormatConstraintHeader, fallback.FormatConstraintHeader);
            CompactFormatFallback = Coalesce(CompactFormatFallback, fallback.CompactFormatFallback);
            ActionReliabilityFallback = Coalesce(ActionReliabilityFallback, fallback.ActionReliabilityFallback);
            ActionReliabilityMarker = Coalesce(ActionReliabilityMarker, fallback.ActionReliabilityMarker);

            if (ApiActionPrompt == null)
            {
                ApiActionPrompt = fallback.ApiActionPrompt?.Clone() ?? RpgApiActionPromptConfig.CreateFallback();
                return;
            }

            ApiActionPrompt.NormalizeWith(fallback.ApiActionPrompt ?? RpgApiActionPromptConfig.CreateFallback());
        }

        private static string Coalesce(string current, string fallback)
        {
            return string.IsNullOrWhiteSpace(current) ? fallback ?? string.Empty : current;
        }
    }

    /// <summary>/// Dependencies: none.
 /// Responsibility: hold structured RPG API prompt lines for full and compact rendering paths.
 ///</summary>
    [Serializable]
    internal sealed class RpgApiActionPromptConfig
    {
        private static readonly List<string> DefaultSharedActionLines = new List<string>
        {
            "- TryAffectSocialGoodwill: Change goodwill between your faction and player. Use when you have a intention to change the relationship. Required 'amount' (int).",
            "- RomanceAttempt: Force-set romantic relationship status with the interlocutor.",
            "- MarriageProposal: Force-set marriage status (spouse) with the interlocutor.",
            "- Breakup: Force-set breakup result (remove romance/marriage and apply ex relation).",
            "- Divorce: Force-set divorce result (remove spouse and set ex-spouse).",
            "- Date: Treated as a romantic status progression action.",
            "- ReduceResistance: If you are a prisoner, reduce your recruitment resistance. Required 'amount' (float/int).",
            "- ReduceWill: If you are a prisoner, reduce your enslavement will. Required 'amount' (float/int).",
            "- Recruit: Immediately join the player's faction (no parameters). Use when relation is warm and trust is stable, and you clearly express willingness to join. If your policy list uses numbered actions, this is Action 4.",
            "- TryTakeOrderedJob: Execute a job. Use 'defName': 'AttackMelee' to attack the interlocutor.",
            "- TriggerIncident: Trigger a game event (incident). Required 'defName'. Optional 'amount' for incident points. Examples: 'RaidEnemy', 'TraderCaravanArrival', 'TravelerGroup'.",
            "- GrantInspiration: Attempt to grant yourself an inspiration. Use when interlocutor inspire you through encouragement, new ideas, emotional impact, or strategic insight. 'defName' (InspirationDef). e.g.:Frenzy_Work/Frenzy_Go/Frenzy_Shoot/Inspired_Trade/Inspired_Recruitment/Inspired_Taming/Inspired_Surgery/Inspired_Creativity",
            "- ExitDialogue: End the current RPG conversation normally. Use when the conversation reaches a natural stopping point, the pawn needs to leave, resume work, rest, or simply has nothing more to say. Optional 'reason'. This is a soft, non-hostile ending and does not prevent future conversations. No cooldown is applied.",
            "- ExitDialogueCooldown: End the current RPG conversation and reject new chats for 1 day. Use when the pawn wants to disengage and be left alone due to anger, stress, fear, exhaustion, humiliation, annoyance, or emotional overwhelm. Optional 'reason'. This is a firm social refusal, not a routine ending, and should be used sparingly.",
            "- Guidance: Prefer ExitDialogue for polite or natural closure. Use ExitDialogueCooldown under hostility, harassment, repeated pressure, or clear refusal context."
        };

        private static readonly List<string> DefaultCompactActionNames = new List<string>
        {
            "TryGainMemory",
            "TryAffectSocialGoodwill",
            "RomanceAttempt",
            "MarriageProposal",
            "Breakup",
            "Divorce",
            "Date",
            "ReduceResistance",
            "ReduceWill",
            "Recruit",
            "TryTakeOrderedJob",
            "TriggerIncident",
            "GrantInspiration",
            "ExitDialogue",
            "ExitDialogueCooldown"
        };

        public string FullHeader;
        public string FullIntro;
        public string FullActionObjectHint;
        public string FullActionReliabilityGuidance;
        public string FullClosureReliabilityGuidance;
        public string FullTryGainMemoryLineTemplate;
        public List<string> SharedActionLines;

        public string CompactHeader;
        public string CompactIntro;
        public string CompactAllowedActionsTemplate;
        public string CompactTryGainMemoryTemplate;
        public string CompactActionFieldsHint;
        public string CompactClosureGuidance;
        public List<string> CompactActionNames;

        public static RpgApiActionPromptConfig CreateFallback()
        {
            return new RpgApiActionPromptConfig
            {
                FullHeader = "=== AVAILABLE NPC ACTIONS ===",
                FullIntro = "You can trigger game effects by including them in the 'actions' array of your JSON output.",
                FullActionObjectHint = "Each action should be an object: { \"action\": \"ActionName\", \"defName\": \"OptionalDef\", \"amount\": 0 }",
                FullActionReliabilityGuidance = "Action reliability guidance: avoid long no-action streaks; if two consecutive replies have no gameplay effect, add a role-consistent TryGainMemory.",
                FullClosureReliabilityGuidance = "Closure reliability guidance: when your reply clearly ends/refuses the chat, include ExitDialogue or ExitDialogueCooldown in actions.",
                FullTryGainMemoryLineTemplate = "- TryGainMemory: Add a thought memory to yourself. Use when you want to express a thought or emotion. Required 'defName'. Tendency guidance: around 80% chance once dialogue reaches 5-10 rounds. Valid examples: {{examples}}.",
                SharedActionLines = new List<string>(DefaultSharedActionLines),
                CompactHeader = "=== AVAILABLE NPC ACTIONS (COMPACT) ===",
                CompactIntro = "Use role-consistent actions when gameplay effects are intended; do not keep long no-action streaks.",
                CompactAllowedActionsTemplate = "Allowed actions: {{action_names}}.",
                CompactTryGainMemoryTemplate = "For TryGainMemory, valid examples include: {{examples}}.",
                CompactActionFieldsHint = "Action object fields: action (required), defName/amount/reason (optional by action).",
                CompactClosureGuidance = "If the reply closes/refuses the conversation, include ExitDialogue or ExitDialogueCooldown.",
                CompactActionNames = new List<string>(DefaultCompactActionNames)
            };
        }

        public void NormalizeWith(RpgApiActionPromptConfig fallback)
        {
            if (fallback == null)
            {
                fallback = CreateFallback();
            }

            FullHeader = Coalesce(FullHeader, fallback.FullHeader);
            FullIntro = Coalesce(FullIntro, fallback.FullIntro);
            FullActionObjectHint = Coalesce(FullActionObjectHint, fallback.FullActionObjectHint);
            FullActionReliabilityGuidance = Coalesce(FullActionReliabilityGuidance, fallback.FullActionReliabilityGuidance);
            FullClosureReliabilityGuidance = Coalesce(FullClosureReliabilityGuidance, fallback.FullClosureReliabilityGuidance);
            FullTryGainMemoryLineTemplate = Coalesce(FullTryGainMemoryLineTemplate, fallback.FullTryGainMemoryLineTemplate);
            CompactHeader = Coalesce(CompactHeader, fallback.CompactHeader);
            CompactIntro = Coalesce(CompactIntro, fallback.CompactIntro);
            CompactAllowedActionsTemplate = Coalesce(CompactAllowedActionsTemplate, fallback.CompactAllowedActionsTemplate);
            CompactTryGainMemoryTemplate = Coalesce(CompactTryGainMemoryTemplate, fallback.CompactTryGainMemoryTemplate);
            CompactActionFieldsHint = Coalesce(CompactActionFieldsHint, fallback.CompactActionFieldsHint);
            CompactClosureGuidance = Coalesce(CompactClosureGuidance, fallback.CompactClosureGuidance);

            if (SharedActionLines == null || SharedActionLines.Count == 0)
            {
                SharedActionLines = new List<string>(fallback.SharedActionLines ?? new List<string>());
            }

            if (CompactActionNames == null || CompactActionNames.Count == 0)
            {
                CompactActionNames = new List<string>(fallback.CompactActionNames ?? new List<string>());
            }
        }

        public RpgApiActionPromptConfig Clone()
        {
            return new RpgApiActionPromptConfig
            {
                FullHeader = FullHeader,
                FullIntro = FullIntro,
                FullActionObjectHint = FullActionObjectHint,
                FullActionReliabilityGuidance = FullActionReliabilityGuidance,
                FullClosureReliabilityGuidance = FullClosureReliabilityGuidance,
                FullTryGainMemoryLineTemplate = FullTryGainMemoryLineTemplate,
                SharedActionLines = SharedActionLines != null ? new List<string>(SharedActionLines) : new List<string>(),
                CompactHeader = CompactHeader,
                CompactIntro = CompactIntro,
                CompactAllowedActionsTemplate = CompactAllowedActionsTemplate,
                CompactTryGainMemoryTemplate = CompactTryGainMemoryTemplate,
                CompactActionFieldsHint = CompactActionFieldsHint,
                CompactClosureGuidance = CompactClosureGuidance,
                CompactActionNames = CompactActionNames != null ? new List<string>(CompactActionNames) : new List<string>()
            };
        }

        private static string Coalesce(string current, string fallback)
        {
            return string.IsNullOrWhiteSpace(current) ? fallback ?? string.Empty : current;
        }
    }

    /// <summary>/// Dependencies: RimWorld mod path APIs, Unity JsonUtility, file I/O.
 /// Responsibility: load cached RPG default prompts from Prompt/Default/RpgPrompts_Default.json.
 ///</summary>
    internal static class RpgPromptDefaultsProvider
    {
        private const string PromptFolderName = "Prompt";
        private const string DefaultSubFolderName = "Default";
        private const string DefaultConfigFileName = "RpgPrompts_Default.json";
        private const string FallbackRoot = "E:\\SteamLibrary\\steamapps\\common\\RimWorld\\Mods\\RimChat";

        private static readonly object SyncRoot = new object();
        private static string cachedPath = string.Empty;
        private static DateTime cachedWriteTimeUtc = DateTime.MinValue;
        private static RpgPromptDefaultsConfig cachedConfig;

        public static RpgPromptDefaultsConfig GetDefaults()
        {
            lock (SyncRoot)
            {
                string path = GetDefaultConfigPath();
                if (IsCached(path, out RpgPromptDefaultsConfig config))
                {
                    return config;
                }

                var fallback = RpgPromptDefaultsConfig.CreateFallback();
                if (!TryLoad(path, fallback, out config))
                {
                    config = fallback;
                }

                cachedPath = path;
                cachedWriteTimeUtc = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                cachedConfig = config;
                return config;
            }
        }

        private static bool IsCached(string path, out RpgPromptDefaultsConfig config)
        {
            config = null;
            if (cachedConfig == null || !string.Equals(cachedPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!File.Exists(path))
            {
                config = cachedConfig;
                return true;
            }

            DateTime writeTime = File.GetLastWriteTimeUtc(path);
            if (writeTime != cachedWriteTimeUtc)
            {
                return false;
            }

            config = cachedConfig;
            return true;
        }

        private static bool TryLoad(string path, RpgPromptDefaultsConfig fallback, out RpgPromptDefaultsConfig config)
        {
            config = null;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(path);
                config = JsonUtility.FromJson<RpgPromptDefaultsConfig>(json);
                if (config == null)
                {
                    return false;
                }

                config.NormalizeWith(fallback);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to load RPG prompt defaults from {path}: {ex.Message}");
                return false;
            }
        }

        private static string GetDefaultConfigPath()
        {
            string modPath = ResolveFromModPath();
            if (!string.IsNullOrWhiteSpace(modPath))
            {
                return modPath;
            }

            string assemblyPath = ResolveFromAssemblyPath();
            if (!string.IsNullOrWhiteSpace(assemblyPath))
            {
                return assemblyPath;
            }

            return Path.Combine(FallbackRoot, PromptFolderName, DefaultSubFolderName, DefaultConfigFileName);
        }

        private static string ResolveFromModPath()
        {
            try
            {
                var mod = LoadedModManager.GetMod<RimChatMod>();
                if (mod?.Content == null)
                {
                    return string.Empty;
                }

                string dir = Path.Combine(mod.Content.RootDir, PromptFolderName, DefaultSubFolderName);
                return Path.Combine(dir, DefaultConfigFileName);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ResolveFromAssemblyPath()
        {
            try
            {
                string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string assemblyDir = Path.GetDirectoryName(assemblyPath);
                string modDir = Directory.GetParent(assemblyDir)?.Parent?.FullName;
                if (string.IsNullOrWhiteSpace(modDir))
                {
                    return string.Empty;
                }

                string dir = Path.Combine(modDir, PromptFolderName, DefaultSubFolderName);
                return Path.Combine(dir, DefaultConfigFileName);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
