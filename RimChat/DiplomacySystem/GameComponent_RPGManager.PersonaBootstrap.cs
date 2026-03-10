using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RimChat.AI;
using RimChat.Config;
using RimChat.Persistence;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>/// Dependencies: AIChatServiceAsync, PromptPersistenceService, pawn persona storage in this component.
 /// Responsibility: one-time bootstrap persona generation for existing pawns on first loaded save.
 ///</summary>
    public partial class GameComponent_RPGManager
    {
        private sealed class PendingPersonaGenerationContext
        {
            public Pawn Pawn;
            public int Attempt;
            public List<ChatMessageData> Messages;
        }

        private const int PersonaBootstrapTickInterval = 150;
        private const int PersonaBootstrapRetryDelayTicks = 300;
        private const int PersonaBootstrapAiUnavailableRetryTicks = 600;
        private const int MaxPersonaGenerationAttempts = 2;
        private const int PersonaPromptMaxLength = 1200;
        private const int CurrentNpcPersonaBootstrapVersion = 2;

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

            if (!AIChatServiceAsync.Instance.IsConfigured())
            {
                nextPersonaBootstrapTick = currentTick + PersonaBootstrapAiUnavailableRetryTicks;
                return;
            }

            if (!TryGetNextBootstrapPawn(out Pawn pawn))
            {
                CompleteNpcPersonaBootstrap();
                return;
            }

            StartNpcPersonaGeneration(pawn, 1);
            nextPersonaBootstrapTick = currentTick + PersonaBootstrapTickInterval;
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
                   pawn.RaceProps?.Humanlike == true &&
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

        private void StartNpcPersonaGeneration(Pawn pawn, int attempt)
        {
            if (!IsEligibleNpcPersonaTarget(pawn))
            {
                return;
            }

            List<ChatMessageData> messages = BuildNpcPersonaGenerationMessages(pawn);
            string requestId = string.Empty;
            requestId = AIChatServiceAsync.Instance.SendChatRequestAsync(
                messages,
                onSuccess: response => OnNpcPersonaGenerationSuccess(requestId, response),
                onError: error => OnNpcPersonaGenerationError(requestId, error));

            if (string.IsNullOrEmpty(requestId))
            {
                return;
            }

            npcPersonaPendingRequests[requestId] = new PendingPersonaGenerationContext
            {
                Pawn = pawn,
                Attempt = attempt,
                Messages = messages
            };
        }

        private List<ChatMessageData> BuildNpcPersonaGenerationMessages(Pawn pawn)
        {
            PersonaPronouns pronouns = ResolvePersonaPronouns(pawn);
            RpgPromptDefaultsConfig defaults = RpgPromptDefaultsProvider.GetDefaults();
            string profile = PromptPersistenceService.Instance.BuildPawnPersonaBootstrapProfile(pawn);
            string prompt = BuildPersonaBootstrapPrompt(defaults, pronouns, profile);

            return new List<ChatMessageData>
            {
                new ChatMessageData
                {
                    role = "system",
                    content = defaults.PersonaBootstrapSystemPrompt
                },
                new ChatMessageData
                {
                    role = "user",
                    content = prompt
                }
            };
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

            if (pending.Attempt < MaxPersonaGenerationAttempts && AIChatServiceAsync.Instance.IsConfigured())
            {
                StartNpcPersonaGeneration(pending.Pawn, pending.Attempt + 1);
                nextPersonaBootstrapTick = (Find.TickManager?.TicksGame ?? 0) + PersonaBootstrapRetryDelayTicks;
                return;
            }

            SetPawnPersonaPrompt(pending.Pawn, BuildFallbackPersonaPrompt(pending.Pawn));
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

            return userTemplate
                .Replace("{{template_line}}", template)
                .Replace("{{example_line}}", defaults.PersonaBootstrapExample ?? string.Empty)
                .Replace("{{subject_pronoun}}", pronouns.Subject)
                .Replace("{{object_pronoun}}", pronouns.Objective)
                .Replace("{{possessive_pronoun}}", pronouns.Possessive)
                .Replace("{{profile}}", profile ?? string.Empty);
        }

        private static string RenderPersonaBootstrapTemplate(string template, PersonaPronouns pronouns)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return string.Empty;
            }

            return template
                .Replace("{{subject_pronoun}}", pronouns.Subject)
                .Replace("{{subject_pronoun_lower}}", pronouns.SubjectLower)
                .Replace("{{be_verb}}", pronouns.BeVerb)
                .Replace("{{object_pronoun}}", pronouns.Objective)
                .Replace("{{seek_verb}}", pronouns.SeekVerb);
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
        }
    }
}
