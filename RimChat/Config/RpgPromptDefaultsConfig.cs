using System;
using System.Collections.Generic;
using System.IO;
using RimChat.Core;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    /// <summary>/// Dependencies: UnityEngine.JsonUtility, RimWorld mod path APIs, file system.
 /// Responsibility: represent default RPG prompt text loaded from Prompt/Default/PawnDialoguePrompt_Default.json.
 ///</summary>
    [Serializable]
    internal sealed class RpgPromptDefaultsConfig
    {
        public string RoleSetting;
        public string DialogueStyle;
        public string FormatConstraint;
        public string NonVerbalOutputConstraintTemplate;
        public string RoleSettingFallbackTemplate;
        public string FormatConstraintHeader;
        public string CompactFormatFallback;
        public string ActionReliabilityFallback;
        public string ActionReliabilityMarker;
        public string RpgRoleSettingTemplate;
        public string RpgCompactFormatConstraintTemplate;
        public string RpgActionReliabilityRuleTemplate;
        public string DecisionPolicyTemplate;
        public string TurnObjectiveTemplate;
        public string OpeningObjectiveTemplate;
        public string TopicShiftRuleTemplate;
        public string RelationshipProfileTemplate;
        public string KinshipBoundaryRuleTemplate;
        public string PersonaBootstrapSystemPrompt;
        public string PersonaBootstrapUserPromptTemplate;
        public string PersonaBootstrapOutputTemplate;
        public string PersonaBootstrapExample;
        public RpgApiActionPromptConfig ApiActionPrompt;
        public int RimTalkSummaryHistoryLimit;
        public string RimTalkPersonaCopyTemplate;
        public bool RimTalkAutoPushSessionSummary;
        public bool RimTalkAutoInjectCompatPreset;

        public static RpgPromptDefaultsConfig CreateFallback()
        {
            return new RpgPromptDefaultsConfig
            {
                RoleSetting = "You are an AI-controlled NPC in RimWorld. Your goal is to engage in immersive, character-driven dialogue with the player.",
                DialogueStyle = "Output specification authority: see the dedicated response_contract node (dialogue.response_contract_body). This section is reference-only.",
                FormatConstraint =
                    "Return exactly one JSON object. Required field: visible_dialogue. Optional field: actions. visible_dialogue must be one concise in-character NPC line with no \\r or \\n and no reasoning, notes, or markdown fences. If gameplay effects are needed, put only the actually triggered actions into actions; otherwise omit actions. Do not use legacy wrappers such as dialogue/action/content/text.",
                NonVerbalOutputConstraintTemplate =
                    "=== NON-VERBAL SPEECH CONSTRAINT (REQUIRED) ===\n" +
                    "Target category: {{ pawn.speaker.kind }}.\n" +
                    "Visible NPC dialogue must follow this exact style on one line:\n" +
                    "{{ pawn.speaker.default_sound }}{{ system.punctuation.open_paren }}inner thought{{ system.punctuation.close_paren }}\n" +
                    "Keep the inner thought brief and avoid long explanations.\n" +
                    "Category defaults: animal={{ pawn.speaker.animal_sound }}, baby={{ pawn.speaker.baby_sound }}, mechanoid={{ pawn.speaker.mechanoid_sound }}.\n" +
                    "If your original line is not in the required structure, rewrite it to this style while preserving intent.\n" +
                    "Keep gameplay-effect JSON rules unchanged: append exactly one trailing {\"actions\":[...]} object only when needed.",
                RoleSettingFallbackTemplate = "Roleplay as {{ pawn.target.name }} in the current RimWorld context.",
                FormatConstraintHeader = "=== FORMAT CONSTRAINT (REQUIRED) ===",
                CompactFormatFallback = "Return one JSON object with visible_dialogue and optional actions. Keep visible_dialogue as one short in-character sentence. Omit actions when there are no gameplay effects. Do not use legacy wrappers like action/content/text.",
                ActionReliabilityFallback = "Reliability rules: keep actions role-consistent and use the fewest actions necessary. Add TryGainMemory only when context makes that memory meaningful.",
                ActionReliabilityMarker = "Reliability rules:",
                RpgRoleSettingTemplate = "Roleplay as {{ pawn.target.name }} in the current RimWorld context.",
                RpgCompactFormatConstraintTemplate = "Return one JSON object with visible_dialogue and optional actions. Keep visible_dialogue as one short in-character sentence. Omit actions when there are no gameplay effects. Do not use legacy wrappers like action/content/text.",
                RpgActionReliabilityRuleTemplate = "Reliability rules: keep actions role-consistent and use the fewest actions necessary. Add TryGainMemory only when context makes that memory meaningful.",
                DecisionPolicyTemplate =
                    "决策优先级顺序：1）格式与语言正确性；2）引用字段正确性；3）事实约束；4）行为安全性与关系限制；5）连贯性与人设风格。",
                TurnObjectiveTemplate =
                    "主目标：{{dialogue.primary_objective}}可选补充：{{ dialogue.optional_followup }}约束条件：优先完成主目标；最多只能切换一次话题。",
                OpeningObjectiveTemplate =
                    "OpeningObjective: use dialogue history and personal memory to decide whether opening should continue prior context. Carry over only when there is explicit unresolved intent, major emotional swing, or major behavior/event that should persist. If none apply, open naturally in-character based on current environment and scene cues. Never copy prior lines verbatim.",
                TopicShiftRuleTemplate = "话题切换规则：优先完成当前目标；仅当可提升表述清晰度或下一步规划时，才可额外追加一段简短的后续内容。",
                RelationshipProfileTemplate =
                    "=== RELATIONSHIP PROFILE (MANUAL RPG ONLY) ===\n" +
                    "Kinship: {{ pawn.relation.kinship }}\n" +
                    "RomanceState: {{ pawn.relation.romance_state }}\n" +
                    "SocialSummary: {{ pawn.relation.social_summary }}" +
                    "{{ if dialogue.guidance != \"\" }}\nGuidance: {{ dialogue.guidance }}{{ end }}",
                KinshipBoundaryRuleTemplate =
                    "When kinship is {{ pawn.relation.kinship }}, keep family boundaries first. " +
                    "If kinship is yes, do not narratively escalate toward RomanceAttempt, Date, or MarriageProposal. " +
                    "Use respectful, caring, boundary-aware wording instead.",
                PersonaBootstrapSystemPrompt = "You are a concise character profiler for RimWorld NPC roleplay prompts.",
                PersonaBootstrapUserPromptTemplate =
                    "Analyze the NPC personality profile and output exactly one line.\n" +
                    "Template:\n" +
                    "{{ dialogue.template_line }}\n" +
                    "Example:\n" +
                    "{{ dialogue.example_line }}\n" +
                    "Rules:\n" +
                    "- Use the pawn's pronouns consistently: {{ pawn.pronouns.subject }}/{{ pawn.pronouns.object }}/{{ pawn.pronouns.possessive }}.\n" +
                    "- Keep each bracketed phrase concise (2-10 words) and keep the whole line under 70 words.\n" +
                    "- Focus only on stable personality traits, values, habits, and social style.\n" +
                    "- Do not use health, wounds, mood, needs, equipment, genes, or temporary events as personality evidence.\n" +
                    "- No markdown. No bullets. No extra text.\n\n" +
                    "NPC personality profile:\n" +
                    "{{ pawn.profile }}",
                PersonaBootstrapOutputTemplate =
                    "{\"persona\":\"\"}",
                PersonaBootstrapExample =
                    "He is a calm and analytical person who rarely shows his emotions and tends to approach problems through careful observation and planning, because deep down he seeks control and security, but this also makes him distant and slow to trust others.",
                ApiActionPrompt = RpgApiActionPromptConfig.CreateFallback(),
                RimTalkSummaryHistoryLimit = 10,
                RimTalkAutoPushSessionSummary = false,
                RimTalkAutoInjectCompatPreset = false,
                RimTalkPersonaCopyTemplate = RimChatSettings.DefaultRimTalkPersonaCopyTemplate
            };
        }

        public void NormalizeWith(RpgPromptDefaultsConfig fallback)
        {
            if (fallback == null)
            {
                fallback = CreateFallback();
            }

            RoleSetting = Coalesce(RoleSetting, fallback.RoleSetting);
            DialogueStyle = Coalesce(DialogueStyle, fallback.DialogueStyle);
            FormatConstraint = Coalesce(FormatConstraint, fallback.FormatConstraint);
            NonVerbalOutputConstraintTemplate = Coalesce(NonVerbalOutputConstraintTemplate, fallback.NonVerbalOutputConstraintTemplate);
            RoleSettingFallbackTemplate = Coalesce(RoleSettingFallbackTemplate, fallback.RoleSettingFallbackTemplate);
            FormatConstraintHeader = Coalesce(FormatConstraintHeader, fallback.FormatConstraintHeader);
            CompactFormatFallback = Coalesce(CompactFormatFallback, fallback.CompactFormatFallback);
            ActionReliabilityFallback = Coalesce(ActionReliabilityFallback, fallback.ActionReliabilityFallback);
            ActionReliabilityMarker = Coalesce(ActionReliabilityMarker, fallback.ActionReliabilityMarker);
            RpgRoleSettingTemplate = Coalesce(RpgRoleSettingTemplate, fallback.RpgRoleSettingTemplate);
            RpgCompactFormatConstraintTemplate = Coalesce(RpgCompactFormatConstraintTemplate, fallback.RpgCompactFormatConstraintTemplate);
            RpgActionReliabilityRuleTemplate = Coalesce(RpgActionReliabilityRuleTemplate, fallback.RpgActionReliabilityRuleTemplate);
            DecisionPolicyTemplate = Coalesce(DecisionPolicyTemplate, fallback.DecisionPolicyTemplate);
            TurnObjectiveTemplate = Coalesce(TurnObjectiveTemplate, fallback.TurnObjectiveTemplate);
            OpeningObjectiveTemplate = Coalesce(OpeningObjectiveTemplate, fallback.OpeningObjectiveTemplate);
            TopicShiftRuleTemplate = Coalesce(TopicShiftRuleTemplate, fallback.TopicShiftRuleTemplate);
            RelationshipProfileTemplate = Coalesce(RelationshipProfileTemplate, fallback.RelationshipProfileTemplate);
            KinshipBoundaryRuleTemplate = Coalesce(KinshipBoundaryRuleTemplate, fallback.KinshipBoundaryRuleTemplate);
            PersonaBootstrapSystemPrompt = Coalesce(PersonaBootstrapSystemPrompt, fallback.PersonaBootstrapSystemPrompt);
            PersonaBootstrapUserPromptTemplate = Coalesce(PersonaBootstrapUserPromptTemplate, fallback.PersonaBootstrapUserPromptTemplate);
            PersonaBootstrapOutputTemplate = Coalesce(PersonaBootstrapOutputTemplate, fallback.PersonaBootstrapOutputTemplate);
            PersonaBootstrapExample = Coalesce(PersonaBootstrapExample, fallback.PersonaBootstrapExample);
            if (RimTalkSummaryHistoryLimit <= 0)
            {
                RimTalkSummaryHistoryLimit = fallback.RimTalkSummaryHistoryLimit;
            }

            RimTalkPersonaCopyTemplate = Coalesce(RimTalkPersonaCopyTemplate, fallback.RimTalkPersonaCopyTemplate);
            RimTalkAutoPushSessionSummary = RimTalkAutoPushSessionSummary || fallback.RimTalkAutoPushSessionSummary;
            RimTalkAutoInjectCompatPreset = RimTalkAutoInjectCompatPreset || fallback.RimTalkAutoInjectCompatPreset;

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
                FullIntro = "You may trigger gameplay effects by adding actions to the 'actions' array in your JSON output.",
                FullActionObjectHint = "Each action is an object like: {\"action\":\"TryGainMemory\",\"defName\":\"RimChat_Encouraged\"}. Replace action with one allowed action name and include only fields required by that action.",
                FullActionReliabilityGuidance = "Keep actions role-consistent. Use the fewest actions necessary. Avoid conflicting actions. Add TryGainMemory only when the current exchange justifies a memory effect.",
                FullClosureReliabilityGuidance = "If your reply clearly ends or refuses the conversation, include exactly one of: ExitDialogue or ExitDialogueCooldown.",
                FullTryGainMemoryLineTemplate = "- TryGainMemory: Add a visible memory to yourself that reflects the interaction. Requires 'defName'. Use lighter memories for short reactions (RimChat_BriefJoy, RimChat_Encouraged, RimChat_Praised, RimChat_BriefSadness, RimChat_Teased), medium memories for personal exchanges (RimChat_HeartfeltCompliment, RimChat_GratefulFeeling, RimChat_ShamefulMoment, RimChat_DeepConnection, RimChat_ResentfulFeeling), strong memories for major emotional turns (RimChat_TrustBetrayed, RimChat_EmpoweringTalk, RimChat_DebilitatingWords, RimChat_UnforgettableMoment, RimChat_WoundingMemory), and the rare philosophical/core set only for life-changing exchanges (RimChat_LoveAndDestruction, RimChat_GoodAndEvilConflict, RimChat_LateRegret, RimChat_UnconditionalCompassion, RimChat_JoyInSuffering). Valid examples: {{ dialogue.examples }}.",
                SharedActionLines = new List<string>(DefaultSharedActionLines),
                CompactHeader = "=== AVAILABLE NPC ACTIONS (COMPACT) ===",
                CompactIntro = "Use role-consistent actions when gameplay effects are intended. Use the fewest actions necessary.",
                CompactAllowedActionsTemplate = "Allowed actions: {{ dialogue.action_names }}.",
                CompactTryGainMemoryTemplate = "- TryGainMemory: Add a visible memory to yourself. Requires defName. Short exchanges use lighter memories, personal exchanges use medium memories, major emotional turns use strong memories, and the core philosophical set is rare and only for life-changing dialogue. Valid examples: {{ dialogue.examples }}.",
                CompactActionFieldsHint = "Action object fields: action (required), defName/amount/reason (optional by action).",
                CompactClosureGuidance = "When the reply clearly ends or refuses the conversation, include exactly one of: ExitDialogue or ExitDialogueCooldown. Use TryGainMemory only when context clearly supports a memory effect.",
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
 /// Responsibility: load cached RPG default prompts from Prompt/Default/PawnDialoguePrompt_Default.json.
 ///</summary>
    internal static class RpgPromptDefaultsProvider
    {
        private const string PromptFolderName = "Prompt";
        private const string DefaultSubFolderName = "Default";
        private const string DefaultConfigFileName = "PawnDialoguePrompt_Default.json";
        private const string FallbackRoot = "E:\\SteamLibrary\\steamapps\\common\\RimWorld\\Mods\\RimChat";

        private static readonly object SyncRoot = new object();
        private static string cachedPath = string.Empty;
        private static DateTime cachedWriteTimeUtc = DateTime.MinValue;
        private static RpgPromptDefaultsConfig cachedConfig;
        private static string loggedDefaultPath = string.Empty;

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
                    Log.Warning($"[RimChat] RPG prompt defaults deserialized to null from {path}; using fallback.");
                    return false;
                }

                config.NormalizeWith(fallback);
                LogResolvedDefaultPayload(config);
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
            string assemblyPath = ResolveFromAssemblyPath();
            if (!string.IsNullOrWhiteSpace(assemblyPath))
            {
                LogResolvedDefaultPath(assemblyPath, "assembly");
                return assemblyPath;
            }

            string modPath = ResolveFromModPath();
            if (!string.IsNullOrWhiteSpace(modPath))
            {
                LogResolvedDefaultPath(modPath, "mod-root");
                return modPath;
            }

            string fallbackPath = Path.Combine(FallbackRoot, PromptFolderName, DefaultSubFolderName, DefaultConfigFileName);
            LogResolvedDefaultPath(fallbackPath, "hardcoded-fallback");
            return fallbackPath;
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

        private static void LogResolvedDefaultPath(string path, string source)
        {
            if (string.IsNullOrWhiteSpace(path) || string.Equals(loggedDefaultPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            loggedDefaultPath = path;
            Log.Message($"[RimChat] RPG default prompt path ({source}): {path}");
        }

        private static void LogResolvedDefaultPayload(RpgPromptDefaultsConfig config)
        {
            if (config == null)
            {
                return;
            }

            string fullHeader = config.ApiActionPrompt?.FullHeader ?? "<null>";
            string compactHeader = config.ApiActionPrompt?.CompactHeader ?? "<null>";
            string reliability = config.ActionReliabilityFallback ?? "<null>";
            string tryGainMemory = config.ApiActionPrompt?.FullTryGainMemoryLineTemplate ?? "<null>";
            Log.Message(
                $"[RimChat] RPG defaults loaded: FullHeader='{fullHeader}', CompactHeader='{compactHeader}', ActionReliabilityFallback='{reliability}', FullTryGainMemoryLineTemplate='{tryGainMemory}'");
        }
    }
}

