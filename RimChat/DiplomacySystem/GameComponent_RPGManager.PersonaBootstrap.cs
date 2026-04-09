using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using RimChat.AI;
using RimChat.Config;
using RimChat.Core;
using RimChat.Dialogue;
using RimChat.Persistence;
using RimChat.Prompting;
using RimChat.Util;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: PromptPersistenceService, PromptTemplateRenderer, RimTalk reflection bridge, and pawn persona storage in this component.
    /// Responsibility: bootstrap/runtime RimTalk persona copy-sync flow for humanlike pawns without external persona-bootstrap requests.
    /// </summary>
    public partial class GameComponent_RPGManager
    {
        private sealed class PendingPersonaGenerationContext
        {
            public Pawn Pawn = null;
            public int Attempt = 0;
            public List<ChatMessageData> Messages = null;
        }

        private const int PersonaBootstrapTickInterval = 150;
        private const int PersonaRuntimeScanIntervalTicks = 900;
        private const int PersonaPromptMaxLength = 1200;
        private const int CurrentNpcPersonaBootstrapVersion = 3;
        private const string RimTalkPersonaServiceTypeName = "RimTalk.Data.PersonaService";

        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex PersonaSentenceStartRegex =
            new Regex(@"\b(?:He|She|They)\s+(?:is|are)\s+(?:a|an)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PersonaTemplateRegex =
            new Regex(
                @"^(?:He|She|They)\s+(?:is|are)\s+(?:a|an)\s+.+?\s+person\s+who\s+.+?,\s+because\s+deep\s+down\s+(?:he|she|they)\s+seek[s]?\s+.+?,\s+but\s+this\s+also\s+makes\s+(?:him|her|them)\s+.+?(?:,\s+often\s+leading\s+to\s+.+?)?[.!]",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private bool npcPersonaBootstrapCompleted;
        private int npcPersonaBootstrapVersion;
        private bool npcPersonaBootstrapQueued;
        private readonly Queue<Pawn> npcPersonaBootstrapTargets = new Queue<Pawn>();
        private readonly Dictionary<string, PendingPersonaGenerationContext> npcPersonaPendingRequests =
            new Dictionary<string, PendingPersonaGenerationContext>();
        private int nextPersonaBootstrapTick;
        private int nextPersonaRuntimeScanTick;
        private bool npcPersonaRuntimeScanDisabledNoRimTalk;
        private static readonly object RimTalkPersonaResolverLock = new object();
        private static bool rimTalkPersonaResolverInitialized;
        private static MethodInfo rimTalkGetPersonalityMethod;
        private static bool rimTalkPersonaResolverLoggedUnavailable;
        private static readonly string[] RimTalkModDetectionTokens =
        {
            "rimtalk"
        };
        private static bool rimTalkPersonaAiBlockLogged;

        private readonly struct PersonaPronouns
        {
            public PersonaPronouns(string subject, string beVerb, string possessive, string objective, string seekVerb)
            {
                Subject = subject;
                BeVerb = beVerb;
                Possessive = possessive;
                Objective = objective;
                SeekVerb = seekVerb;
            }

            public string Subject { get; }
            public string BeVerb { get; }
            public string Possessive { get; }
            public string Objective { get; }
            public string SeekVerb { get; }
            public string SubjectLower => Subject.ToLowerInvariant();
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            ProcessNpcPersonaBootstrapTick();
            ProcessNpcPersonaRuntimeTick();
        }

        private void ExposeData_NpcPersonaBootstrap()
        {
            Scribe_Values.Look(ref npcPersonaBootstrapCompleted, "npcPersonaBootstrapCompleted", false);
            Scribe_Values.Look(ref npcPersonaBootstrapVersion, "npcPersonaBootstrapVersion", 0);
        }

        private void MarkNpcPersonaBootstrapAsNewGame()
        {
            npcPersonaBootstrapCompleted = true;
            npcPersonaBootstrapVersion = CurrentNpcPersonaBootstrapVersion;
            ResetNpcPersonaBootstrapRuntimeState();
        }

        private void ScheduleNpcPersonaBootstrapOnLoad()
        {
            if (!ShouldRunNpcPersonaBootstrap())
            {
                ResetNpcPersonaBootstrapRuntimeState();
                return;
            }

            npcPersonaBootstrapQueued = false;
            nextPersonaBootstrapTick = Find.TickManager?.TicksGame ?? 0;
            nextPersonaRuntimeScanTick = nextPersonaBootstrapTick;
        }

        private void OnPostLoadInit_NpcPersonaBootstrap()
        {
            if (!ShouldRunNpcPersonaBootstrap())
            {
                ResetNpcPersonaBootstrapRuntimeState();
                return;
            }

            npcPersonaBootstrapQueued = false;
            nextPersonaBootstrapTick = Find.TickManager?.TicksGame ?? 0;
            nextPersonaRuntimeScanTick = nextPersonaBootstrapTick;
        }

        private void ProcessNpcPersonaBootstrapTick()
        {
            if (npcPersonaBootstrapCompleted || Current.ProgramState != ProgramState.Playing || Find.TickManager == null)
            {
                return;
            }

            int currentTick = Find.TickManager.TicksGame;
            if (currentTick < nextPersonaBootstrapTick)
            {
                return;
            }

            if (!npcPersonaBootstrapQueued)
            {
                InitializeNpcPersonaBootstrapQueue();
            }

            if (npcPersonaBootstrapCompleted || npcPersonaPendingRequests.Count > 0)
            {
                return;
            }

            if (!IsRimTalkLoadedForPersonaBlock())
            {
                CompleteNpcPersonaBootstrap();
                return;
            }

            if (TryApplyRimTalkPersonaFromBootstrapQueue())
            {
                nextPersonaBootstrapTick = currentTick + PersonaBootstrapTickInterval;
                return;
            }

            CompleteNpcPersonaBootstrap();
        }

        private void ProcessNpcPersonaRuntimeTick()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.TickManager == null)
            {
                return;
            }

            if (npcPersonaPendingRequests.Count > 0)
            {
                return;
            }

            if (npcPersonaRuntimeScanDisabledNoRimTalk)
            {
                return;
            }

            if (!IsRimTalkLoadedForPersonaBlock())
            {
                npcPersonaRuntimeScanDisabledNoRimTalk = true;
                return;
            }

            int currentTick = Find.TickManager.TicksGame;
            if (currentTick < nextPersonaRuntimeScanTick)
            {
                return;
            }

            nextPersonaRuntimeScanTick = currentTick + PersonaRuntimeScanIntervalTicks;
            if (TryApplyRimTalkPersonaFromRuntimeScan())
            {
                nextPersonaRuntimeScanTick = currentTick + PersonaBootstrapTickInterval;
                return;
            }
        }

        private void InitializeNpcPersonaBootstrapQueue()
        {
            ResetNpcPersonaBootstrapRuntimeState();
            npcPersonaBootstrapQueued = true;

            List<Pawn> targets = CollectNpcPersonaBootstrapTargets();
            foreach (Pawn pawn in targets)
            {
                if (!HasPersonaPrompt(pawn))
                {
                    npcPersonaBootstrapTargets.Enqueue(pawn);
                }
            }

            if (npcPersonaBootstrapTargets.Count == 0)
            {
                CompleteNpcPersonaBootstrap();
                return;
            }

            Log.Message($"[RimChat] NPC persona bootstrap queued {npcPersonaBootstrapTargets.Count} existing NPC pawn(s).");
        }

        private List<Pawn> CollectNpcPersonaBootstrapTargets()
        {
            var result = new List<Pawn>();
            var ids = new HashSet<int>();

            foreach (Map map in Find.Maps ?? Enumerable.Empty<Map>())
            {
                if (map?.mapPawns?.AllPawnsSpawned == null)
                {
                    continue;
                }

                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    AppendUniqueNpcTarget(result, ids, pawn);
                }
            }

            foreach (Faction faction in Find.FactionManager?.AllFactionsVisible ?? Enumerable.Empty<Faction>())
            {
                AppendUniqueNpcTarget(result, ids, faction?.leader);
            }

            return result;
        }

        private static void AppendUniqueNpcTarget(List<Pawn> target, HashSet<int> ids, Pawn pawn)
        {
            if (target == null || ids == null || pawn == null)
            {
                return;
            }

            if (pawn.thingIDNumber <= 0 || !ids.Add(pawn.thingIDNumber))
            {
                return;
            }

            target.Add(pawn);
        }

        private static bool IsEligibleNpcPersonaTarget(Pawn pawn)
        {
            return pawn != null &&
                   PawnDialogueRoutingPolicy.IsRpgDialogueEligibleRace(pawn) &&
                   !pawn.Dead &&
                   !pawn.Destroyed;
        }

        private bool HasPersonaPrompt(Pawn pawn)
        {
            return !string.IsNullOrWhiteSpace(GetPawnPersonaPrompt(pawn));
        }

        private bool TryGetNextBootstrapPawn(out Pawn pawn)
        {
            pawn = null;
            while (npcPersonaBootstrapTargets.Count > 0)
            {
                Pawn candidate = npcPersonaBootstrapTargets.Dequeue();
                if (!IsEligibleNpcPersonaTarget(candidate) || HasPersonaPrompt(candidate))
                {
                    continue;
                }

                pawn = candidate;
                return true;
            }

            return false;
        }

        private bool TryApplyRimTalkPersonaFromRuntimeScan()
        {
            foreach (Pawn candidate in CollectNpcPersonaBootstrapTargets())
            {
                if (!CanCopyPawnPersonaFromRimTalk(candidate) ||
                    IsPawnPersonaGenerationPending(candidate))
                {
                    continue;
                }

                if (TrySyncPawnPersonaFromRimTalk(candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryFindMissingPersonaPawn(out Pawn pawn)
        {
            pawn = CollectNpcPersonaBootstrapTargets().FirstOrDefault(candidate =>
                IsEligibleNpcPersonaTarget(candidate) &&
                !CanCopyPawnPersonaFromRimTalk(candidate) &&
                !HasPersonaPrompt(candidate) &&
                !IsPawnPersonaGenerationPending(candidate));
            return pawn != null;
        }

        private bool IsPawnPersonaGenerationPending(Pawn pawn)
        {
            if (pawn == null || npcPersonaPendingRequests.Count == 0)
            {
                return false;
            }

            return npcPersonaPendingRequests.Values.Any(item => item?.Pawn == pawn);
        }

        private static bool CanStartPersonaGeneration()
        {
            AIChatServiceAsync service = AIChatServiceAsync.Instance;
            return service != null && service.IsConfigured();
        }

        private static bool ShouldBlockAiPersonaGeneration()
        {
            if (!IsRimTalkLoadedForPersonaBlock())
            {
                return false;
            }

            if (!rimTalkPersonaAiBlockLogged)
            {
                rimTalkPersonaAiBlockLogged = true;
                Log.Message("[RimChat] RimTalk detected; AI persona generation blocked at runtime.");
            }

            return true;
        }

        private static bool IsRimTalkLoadedForPersonaBlock()
        {
            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;
            if (mods == null || mods.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < mods.Count; i++)
            {
                ModContentPack mod = mods[i];
                if (mod == null)
                {
                    continue;
                }

                string packageId = mod.PackageIdPlayerFacing ?? string.Empty;
                string name = mod.Name ?? string.Empty;
                for (int j = 0; j < RimTalkModDetectionTokens.Length; j++)
                {
                    string token = RimTalkModDetectionTokens[j];
                    if (ContainsToken(packageId, token) || ContainsToken(name, token))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool ContainsToken(string source, string token)
        {
            return !string.IsNullOrWhiteSpace(source) &&
                   !string.IsNullOrWhiteSpace(token) &&
                   source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void StartNpcPersonaGeneration(Pawn pawn, int attempt)
        {
            _ = attempt;
            if (!IsEligibleNpcPersonaTarget(pawn) || IsPawnPersonaGenerationPending(pawn))
            {
                return;
            }

            if (ShouldBlockAiPersonaGeneration())
            {
                return;
            }

            if (TrySyncPawnPersonaFromRimTalk(pawn))
            {
                return;
            }

            if (CanCopyPawnPersonaFromRimTalk(pawn))
            {
                return;
            }
        }

        private bool TryApplyRimTalkPersonaFromBootstrapQueue()
        {
            int count = npcPersonaBootstrapTargets.Count;
            bool copied = false;
            for (int i = 0; i < count; i++)
            {
                Pawn candidate = npcPersonaBootstrapTargets.Dequeue();
                if (!IsEligibleNpcPersonaTarget(candidate) || HasPersonaPrompt(candidate))
                {
                    continue;
                }

                if (!copied && TryCopyPawnPersonaFromRimTalk(candidate))
                {
                    copied = true;
                    continue;
                }

                npcPersonaBootstrapTargets.Enqueue(candidate);
            }

            return copied;
        }

        private bool TryCopyPawnPersonaFromRimTalk(Pawn pawn)
        {
            if (!IsEligibleRimTalkPersonaCopyTarget(pawn) || HasPersonaPrompt(pawn))
            {
                return false;
            }

            if (!TryGetRimTalkSourcePersona(pawn, out string sourcePersona))
            {
                return false;
            }

            string template = RimChatMod.Settings?.GetRimTalkPersonaCopyTemplateOrDefault();
            if (string.IsNullOrWhiteSpace(template))
            {
                DebugLogger.Debug("RimTalk persona copy skipped: template is empty.");
                return false;
            }

            string rendered = RenderPersonaCopyTemplateOrThrow(pawn, template, sourcePersona);
            string normalized = NormalizeCopiedPersonaPrompt(rendered);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw BuildPersonaCopyRenderException(
                    "prompt_templates.rpg_persona_copy",
                    "rpg",
                    $"Persona copy template returned empty normalized text for pawn '{pawn?.LabelShortCap ?? "unknown"}'.");
            }

            SetPawnPersonaPrompt(pawn, normalized);
            TryEnsureRpgPersonaTokenCoverageSafe();
            DebugLogger.Debug($"RimTalk persona copied for pawn '{pawn?.LabelShortCap}'.");
            return true;
        }

        private bool TrySyncPawnPersonaFromRimTalk(Pawn pawn)
        {
            if (!IsEligibleRimTalkPersonaCopyTarget(pawn))
            {
                return false;
            }

            if (!TryGetRimTalkSourcePersona(pawn, out string sourcePersona))
            {
                return false;
            }

            string template = RimChatMod.Settings?.GetRimTalkPersonaCopyTemplateOrDefault();
            if (string.IsNullOrWhiteSpace(template))
            {
                DebugLogger.Debug("RimTalk persona sync skipped: template is empty.");
                return false;
            }

            string rendered = RenderPersonaCopyTemplateOrThrow(pawn, template, sourcePersona);
            string normalized = NormalizeCopiedPersonaPrompt(rendered);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw BuildPersonaCopyRenderException(
                    "prompt_templates.rpg_persona_copy",
                    "rpg",
                    $"Persona sync template returned empty normalized text for pawn '{pawn?.LabelShortCap ?? "unknown"}'.");
            }

            string current = GetPawnPersonaPrompt(pawn)?.Trim() ?? string.Empty;
            if (string.Equals(current, normalized, StringComparison.Ordinal))
            {
                return false;
            }

            SetPawnPersonaPrompt(pawn, normalized);
            TryEnsureRpgPersonaTokenCoverageSafe();
            DebugLogger.Debug($"RimTalk persona synced(update) for pawn '{pawn?.LabelShortCap}'.");
            return true;
        }

        public bool TrySyncAllColonyPawnPersonasFromRimTalk(
            out int updated,
            out int cleared,
            out int unchanged,
            out int skipped)
        {
            updated = 0;
            cleared = 0;
            unchanged = 0;
            skipped = 0;

            foreach (Pawn pawn in CollectNpcPersonaBootstrapTargets())
            {
                if (!CanCopyPawnPersonaFromRimTalk(pawn) || IsPawnPersonaGenerationPending(pawn))
                {
                    skipped++;
                    continue;
                }

                string before = GetPawnPersonaPrompt(pawn)?.Trim() ?? string.Empty;
                if (!TrySyncPawnPersonaFromRimTalk(pawn))
                {
                    unchanged++;
                    continue;
                }

                string after = GetPawnPersonaPrompt(pawn)?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(before) && string.IsNullOrWhiteSpace(after))
                {
                    cleared++;
                }
                else
                {
                    updated++;
                }
            }

            return updated > 0 || cleared > 0;
        }

        private static void TryEnsureRpgPersonaTokenCoverageSafe()
        {
            try
            {
                RimChatMod.Settings?.EnsurePawnPersonalityTokenForRpgChannelsSafe();
            }
            catch (Exception ex)
            {
                DebugLogger.Debug($"Failed to ensure RPG persona token coverage: {ex.Message}");
            }
        }

        private static bool IsEligibleRimTalkPersonaCopyTarget(Pawn pawn)
        {
            return IsEligibleNpcPersonaTarget(pawn) && pawn.Faction == Faction.OfPlayer;
        }

        private static bool CanCopyPawnPersonaFromRimTalk(Pawn pawn)
        {
            return IsEligibleRimTalkPersonaCopyTarget(pawn) && TryGetRimTalkSourcePersona(pawn, out _);
        }

        private static bool TryGetRimTalkSourcePersona(Pawn pawn, out string sourcePersona)
        {
            sourcePersona = string.Empty;
            if (!IsEligibleRimTalkPersonaCopyTarget(pawn))
            {
                return false;
            }

            MethodInfo getPersonality = ResolveRimTalkGetPersonalityMethod();
            if (getPersonality == null)
            {
                return false;
            }

            try
            {
                sourcePersona = CollapseWhitespace(getPersonality.Invoke(null, new object[] { pawn }) as string);
                return !string.IsNullOrWhiteSpace(sourcePersona);
            }
            catch (Exception ex)
            {
                DebugLogger.Debug($"RimTalk persona source read failed for pawn '{pawn?.LabelShortCap}': {ex.Message}");
                return false;
            }
        }

        private static MethodInfo ResolveRimTalkGetPersonalityMethod()
        {
            if (rimTalkPersonaResolverInitialized)
            {
                return rimTalkGetPersonalityMethod;
            }

            lock (RimTalkPersonaResolverLock)
            {
                if (rimTalkPersonaResolverInitialized)
                {
                    return rimTalkGetPersonalityMethod;
                }

                Type personaServiceType = GenTypes.GetTypeInAnyAssembly(RimTalkPersonaServiceTypeName);
                rimTalkGetPersonalityMethod = personaServiceType?.GetMethod(
                    "GetPersonality",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(Pawn) },
                    null);
                rimTalkPersonaResolverInitialized = true;
                if (rimTalkGetPersonalityMethod == null && !rimTalkPersonaResolverLoggedUnavailable)
                {
                    rimTalkPersonaResolverLoggedUnavailable = true;
                    DebugLogger.Debug("RimTalk persona source unavailable: RimTalk.Data.PersonaService.GetPersonality not found.");
                }

                return rimTalkGetPersonalityMethod;
            }
        }

        private static string NormalizeCopiedPersonaPrompt(string raw)
        {
            string normalized = CollapseWhitespace(raw);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            return normalized.Length > PersonaPromptMaxLength
                ? normalized.Substring(0, PersonaPromptMaxLength).TrimEnd()
                : normalized;
        }

        private string RenderPersonaCopyTemplateOrThrow(Pawn pawn, string template, string sourcePersona)
        {
            if (pawn == null || string.IsNullOrWhiteSpace(template))
            {
                throw BuildPersonaCopyRenderException(
                    "prompt_templates.rpg_persona_copy",
                    "rpg",
                    "Persona copy template or pawn is missing.");
            }

            if (string.IsNullOrWhiteSpace(sourcePersona))
            {
                throw BuildPersonaCopyRenderException(
                    "prompt_templates.rpg_persona_copy",
                    "rpg",
                    "Persona copy source is empty.");
            }

            const string templateId = "prompt_templates.rpg_persona_copy";
            const string channel = "rpg";
            DialogueScenarioContext scenarioContext = DialogueScenarioContext.CreateRpg(null, pawn, false);
            var runtimeValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["pawn.target"] = pawn,
                ["pawn.target.name"] = pawn.LabelShort ?? string.Empty,
                ["pawn.personality"] = sourcePersona
            };
            UserDefinedPromptVariableService.PopulateRuntimeValues(
                runtimeValues,
                new PromptRuntimeVariableContext(templateId, channel, scenarioContext, null));
            PromptRenderContext context = PromptRenderContext.Create(templateId, channel);
            context.SetValues(runtimeValues);
            string rendered = PromptTemplateRenderer.RenderOrThrow(templateId, channel, template, context);
            if (string.IsNullOrWhiteSpace(rendered))
            {
                throw BuildPersonaCopyRenderException(
                    templateId,
                    channel,
                    $"Persona copy template rendered empty text for pawn '{pawn?.LabelShortCap ?? "unknown"}'.");
            }

            return rendered;
        }

        private static PromptRenderException BuildPersonaCopyRenderException(
            string templateId,
            string channel,
            string message)
        {
            return new PromptRenderException(
                templateId,
                channel,
                new PromptRenderDiagnostic
                {
                    ErrorCode = PromptRenderErrorCode.TemplateBlocked,
                    Message = message ?? "Persona copy template blocked."
                });
        }

        private List<ChatMessageData> BuildNpcPersonaGenerationMessages(Pawn pawn)
        {
            PersonaPronouns pronouns = ResolvePersonaPronouns(pawn);
            string profile = PromptPersistenceService.Instance.BuildPawnPersonaBootstrapProfile(pawn);
            var variables = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["pawn.profile"] = profile ?? string.Empty,
                ["pawn.target"] = pawn,
                ["pawn.target.name"] = pawn?.LabelShort ?? "Unknown",
                ["pawn.pronouns.subject"] = pronouns.Subject,
                ["pawn.pronouns.subject_lower"] = pronouns.SubjectLower,
                ["pawn.pronouns.be_verb"] = pronouns.BeVerb,
                ["pawn.pronouns.object"] = pronouns.Objective,
                ["pawn.pronouns.possessive"] = pronouns.Possessive,
                ["pawn.pronouns.seek_verb"] = pronouns.SeekVerb,
                ["dialogue.template_line"] = BuildPersonaTemplateLine(pronouns),
                ["dialogue.example_line"] = RpgPromptDefaultsProvider.GetDefaults().PersonaBootstrapExample ?? string.Empty,
                ["dialogue.primary_objective"] = "Generate exactly one persona bootstrap line.",
                ["dialogue.optional_followup"] = "Keep language concise and stable for long-term roleplay continuity.",
                ["dialogue.latest_unresolved_intent"] = string.Empty
            };
            DialogueScenarioContext context = DialogueScenarioContext.CreateRpg(
                null,
                pawn,
                false,
                new[] { "channel:persona_bootstrap", "phase:bootstrap" });
            string systemPrompt = PromptPersistenceService.Instance.BuildUnifiedChannelSystemPrompt(
                RimTalkPromptChannel.Rpg,
                RimTalkPromptEntryChannelCatalog.PersonaBootstrap,
                context,
                null,
                variables,
                "persona_profile",
                profile ?? string.Empty);

            return new List<ChatMessageData>
            {
                new ChatMessageData
                {
                    role = "system",
                    content = systemPrompt
                }
            };
        }

        private static string BuildPersonaTemplateLine(PersonaPronouns pronouns)
        {
            return $"{pronouns.Subject} {pronouns.BeVerb} a [core temperament] person who tends to [emotional pattern], "
                + $"usually handles situations by [behavioral strategy], because deep down {pronouns.SubjectLower} {pronouns.SeekVerb} [core motivation], "
                + $"but this also makes {pronouns.Objective} [defense/weakness], often leading to [personality cost].";
        }

        private void OnNpcPersonaGenerationSuccess(string requestId, string response)
        {
            if (string.IsNullOrWhiteSpace(requestId) ||
                !npcPersonaPendingRequests.TryGetValue(requestId, out PendingPersonaGenerationContext pending))
            {
                return;
            }

            npcPersonaPendingRequests.Remove(requestId);
            if (!IsEligibleNpcPersonaTarget(pending.Pawn) || HasPersonaPrompt(pending.Pawn))
            {
                return;
            }

            if (TryNormalizePersonaPrompt(response, out string normalized))
            {
                SetPawnPersonaPrompt(pending.Pawn, normalized);
                return;
            }

            RetryOrFallbackPersonaPrompt(pending);
        }

        private void OnNpcPersonaGenerationError(string requestId, string error)
        {
            if (string.IsNullOrWhiteSpace(requestId) ||
                !npcPersonaPendingRequests.TryGetValue(requestId, out PendingPersonaGenerationContext pending))
            {
                return;
            }

            npcPersonaPendingRequests.Remove(requestId);
            RetryOrFallbackPersonaPrompt(pending);
        }

        private void RetryOrFallbackPersonaPrompt(PendingPersonaGenerationContext pending)
        {
            if (pending == null || !IsEligibleNpcPersonaTarget(pending.Pawn) || HasPersonaPrompt(pending.Pawn))
            {
                return;
            }

            TrySyncPawnPersonaFromRimTalk(pending.Pawn);
            TryCopyPawnPersonaFromRimTalk(pending.Pawn);
        }

        private static bool TryNormalizePersonaPrompt(string raw, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string text = CollapseWhitespace(raw.Replace("```", " ").Trim(' ', '"', '\'', '`'));
            Match start = PersonaSentenceStartRegex.Match(text);
            if (start.Success)
            {
                text = text.Substring(start.Index).Trim();
            }

            Match match = PersonaTemplateRegex.Match(text);
            if (!match.Success)
            {
                return false;
            }

            string personaLine = CollapseWhitespace(match.Value);
            if (!IsPersonaTemplateFormat(personaLine))
            {
                return false;
            }

            normalized = personaLine.Length > PersonaPromptMaxLength
                ? personaLine.Substring(0, PersonaPromptMaxLength).TrimEnd()
                : personaLine;
            return true;
        }

        private static bool IsPersonaTemplateFormat(string text)
        {
            return PersonaTemplateRegex.IsMatch(text);
        }

        private static bool HasOrderedAnchors(string text, params string[] anchors)
        {
            if (string.IsNullOrWhiteSpace(text) || anchors == null || anchors.Length == 0)
            {
                return false;
            }

            int index = 0;
            for (int i = 0; i < anchors.Length; i++)
            {
                index = text.IndexOf(anchors[i], index, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    return false;
                }

                index += anchors[i].Length;
            }

            return true;
        }

        private static string CollapseWhitespace(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? string.Empty : WhitespaceRegex.Replace(text, " ").Trim();
        }

        private string BuildFallbackPersonaPrompt(Pawn pawn)
        {
            PersonaPronouns pronouns = ResolvePersonaPronouns(pawn);
            string temperament = BuildCoreTemperament(pawn);
            string emotion = BuildEmotionalPattern(pawn);
            string strategy = BuildBehavioralStrategy(pawn);
            string motivation = BuildCoreMotivation(pawn);
            string defense = BuildDefenseWeakness(pawn);
            string cost = BuildPersonalityCost(pawn);
            string article = StartsWithVowelSound(temperament) ? "an" : "a";
            string prompt =
                $"{pronouns.Subject} {pronouns.BeVerb} {article} {temperament} person who tends to {emotion}, " +
                $"usually handles situations by {strategy}, because deep down {pronouns.SubjectLower} {pronouns.SeekVerb} {motivation}, " +
                $"but this also makes {pronouns.Objective} {defense}, often leading to {cost}.";
            return prompt.Length > PersonaPromptMaxLength ? prompt.Substring(0, PersonaPromptMaxLength).TrimEnd() : prompt;
        }

        private static PersonaPronouns ResolvePersonaPronouns(Pawn pawn)
        {
            switch (pawn?.gender ?? Gender.None)
            {
                case Gender.Female:
                    return new PersonaPronouns("She", "is", "her", "her", "seeks");
                case Gender.Male:
                    return new PersonaPronouns("He", "is", "his", "him", "seeks");
                default:
                    return new PersonaPronouns("They", "are", "their", "them", "seek");
            }
        }

        private static string BuildPersonaBootstrapPrompt(RpgPromptDefaultsConfig defaults, PersonaPronouns pronouns, string profile)
        {
            string template = RenderPersonaBootstrapTemplate(defaults?.PersonaBootstrapOutputTemplate, pronouns);
            string userTemplate = defaults?.PersonaBootstrapUserPromptTemplate;
            if (string.IsNullOrWhiteSpace(userTemplate))
            {
                return profile ?? string.Empty;
            }

            const string templateId = "prompt_templates.persona_bootstrap.user";
            PromptRenderContext context = BuildPersonaBootstrapRenderContext(templateId, pronouns);
            context.SetValue("dialogue.template_line", template);
            context.SetValue("dialogue.example_line", defaults?.PersonaBootstrapExample ?? string.Empty);
            context.SetValue("pawn.profile", profile ?? string.Empty);
            return PromptTemplateRenderer.RenderOrThrow(templateId, "rpg", userTemplate, context);
        }

        private static string RenderPersonaBootstrapTemplate(string template, PersonaPronouns pronouns)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return string.Empty;
            }

            const string templateId = "prompt_templates.persona_bootstrap.output";
            PromptRenderContext context = BuildPersonaBootstrapRenderContext(templateId, pronouns);
            return PromptTemplateRenderer.RenderOrThrow(templateId, "rpg", template, context);
        }

        private static PromptRenderContext BuildPersonaBootstrapRenderContext(string templateId, PersonaPronouns pronouns)
        {
            PromptRenderContext context = PromptRenderContext.Create(templateId, "rpg");
            context.SetValue("pawn.pronouns.subject", pronouns.Subject);
            context.SetValue("pawn.pronouns.subject_lower", pronouns.SubjectLower);
            context.SetValue("pawn.pronouns.be_verb", pronouns.BeVerb);
            context.SetValue("pawn.pronouns.object", pronouns.Objective);
            context.SetValue("pawn.pronouns.possessive", pronouns.Possessive);
            context.SetValue("pawn.pronouns.seek_verb", pronouns.SeekVerb);
            return context;
        }

        private static string BuildCoreTemperament(Pawn pawn)
        {
            List<string> traits = pawn?.story?.traits?.allTraits?
                .Select(t => t?.Label)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Take(2)
                .Select(v => v.ToLowerInvariant())
                .ToList();
            if (traits != null && traits.Count > 0)
            {
                return string.Join(" and ", traits);
            }

            int social = pawn?.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
            if (social >= 8)
            {
                return "calm and perceptive";
            }

            return "practical and cautious";
        }

        private static string BuildEmotionalPattern(Pawn pawn)
        {
            int social = pawn?.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
            if (social >= 10)
            {
                return "keep emotions measured and carefully filtered";
            }

            if (social >= 5)
            {
                return "stay polite while keeping feelings under control";
            }

            return "keep feelings guarded and close to the chest";
        }

        private static string BuildBehavioralStrategy(Pawn pawn)
        {
            int intellectual = pawn?.skills?.GetSkill(SkillDefOf.Intellectual)?.Level ?? 0;
            int social = pawn?.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
            int combat = Math.Max(
                pawn?.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0,
                pawn?.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0);
            if (intellectual >= 8)
            {
                return "careful observation and planning";
            }

            if (social >= 8)
            {
                return "reading people first and responding with tact";
            }

            if (combat >= 8)
            {
                return "disciplined action and steady pressure";
            }

            return "steady routines and deliberate choices";
        }

        private static string BuildCoreMotivation(Pawn pawn)
        {
            int intellectual = pawn?.skills?.GetSkill(SkillDefOf.Intellectual)?.Level ?? 0;
            int social = pawn?.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
            if (intellectual >= 8)
            {
                return "clarity and control";
            }

            if (social >= 8)
            {
                return "stable trust and mutual understanding";
            }

            return "security and dependable bonds";
        }

        private static string BuildDefenseWeakness(Pawn pawn)
        {
            int social = pawn?.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
            if (social >= 8)
            {
                return "hard to read and slow to lower defenses";
            }

            return "distant and slow to trust others";
        }

        private static string BuildPersonalityCost(Pawn pawn)
        {
            int social = pawn?.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
            if (social >= 8)
            {
                return "missed chances for deeper closeness";
            }

            return "emotional distance in close relationships";
        }

        private static bool StartsWithVowelSound(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            char c = char.ToLowerInvariant(text[0]);
            return c == 'a' || c == 'e' || c == 'i' || c == 'o' || c == 'u';
        }

        private void CompleteNpcPersonaBootstrap()
        {
            npcPersonaBootstrapCompleted = true;
            npcPersonaBootstrapVersion = CurrentNpcPersonaBootstrapVersion;
            ResetNpcPersonaBootstrapRuntimeState();
            Log.Message("[RimChat] Existing NPC persona bootstrap completed.");
        }

        private bool ShouldRunNpcPersonaBootstrap()
        {
            if (npcPersonaBootstrapVersion < CurrentNpcPersonaBootstrapVersion)
            {
                npcPersonaBootstrapCompleted = false;
            }

            return !npcPersonaBootstrapCompleted;
        }

        private void ResetNpcPersonaBootstrapRuntimeState()
        {
            npcPersonaBootstrapTargets.Clear();
            npcPersonaPendingRequests.Clear();
            nextPersonaBootstrapTick = 0;
            nextPersonaRuntimeScanTick = 0;
            npcPersonaRuntimeScanDisabledNoRimTalk = false;
        }
    }
}
