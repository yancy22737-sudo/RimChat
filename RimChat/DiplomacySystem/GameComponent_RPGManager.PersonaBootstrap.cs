using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RimChat.AI;
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

        private bool npcPersonaBootstrapCompleted;
        private int npcPersonaBootstrapVersion;
        private bool npcPersonaBootstrapQueued;
        private readonly Queue<Pawn> npcPersonaBootstrapTargets = new Queue<Pawn>();
        private readonly Dictionary<string, PendingPersonaGenerationContext> npcPersonaPendingRequests =
            new Dictionary<string, PendingPersonaGenerationContext>();
        private int nextPersonaBootstrapTick;

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
            string profile = PromptPersistenceService.Instance.BuildPawnPersonaBootstrapProfile(pawn);
            string prompt =
                "Analyze the NPC personality profile and output exactly one line in this exact format:\n" +
                "You are a person who ___. On a daily basis, you ___. " +
                "When getting along with others, you ___. When facing pressure or conflict, you ___. " +
                "You value ___ the most, so you will instinctively ___.\n" +
                "Keep it concise: each blank phrase should be 4-12 words, and the whole line should stay under 90 words.\n" +
                "Focus only on stable personality traits, values, habits, and social style.\n" +
                "Do not use health, wounds, mood, needs, equipment, genes, or temporary events as personality evidence.\n" +
                "No markdown. No bullets. No extra text.\n\n" +
                profile;

            return new List<ChatMessageData>
            {
                new ChatMessageData
                {
                    role = "system",
                    content = "You are a concise character profiler for RimWorld NPC roleplay prompts."
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
            int head = text.IndexOf("You are a person who", StringComparison.OrdinalIgnoreCase);
            if (head >= 0)
            {
                text = text.Substring(head).Trim();
            }

            if (!IsPersonaTemplateFormat(text))
            {
                return false;
            }

            normalized = text.Length > PersonaPromptMaxLength ? text.Substring(0, PersonaPromptMaxLength).TrimEnd() : text;
            return true;
        }

        private static bool IsPersonaTemplateFormat(string text)
        {
            return HasOrderedAnchors(
                text,
                "You are a person who",
                "On a daily basis, you",
                "When getting along with others, you",
                "When facing pressure or conflict, you",
                "You value",
                "the most, so you will instinctively");
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
            string trait = BuildTraitSummary(pawn);
            string daily = BuildDailySummary(pawn);
            string social = BuildSocialSummary(pawn);
            string conflict = BuildConflictSummary(pawn);
            string value = "stability and trusted bonds";
            string instinct = "choose steady, low-risk actions";

            string prompt =
                $"You are a person who {trait}. " +
                $"On a daily basis, you {daily}. " +
                $"When getting along with others, you {social}. " +
                $"When facing pressure or conflict, you {conflict}. " +
                $"You value {value} the most, so you will instinctively {instinct}.";
            return prompt.Length > PersonaPromptMaxLength ? prompt.Substring(0, PersonaPromptMaxLength).TrimEnd() : prompt;
        }

        private static string BuildTraitSummary(Pawn pawn)
        {
            List<string> traits = pawn?.story?.traits?.allTraits?.Select(t => t?.Label).Where(v => !string.IsNullOrWhiteSpace(v)).Take(3).ToList();
            if (traits == null || traits.Count == 0)
            {
                return "are practical and emotionally guarded";
            }

            return "show " + string.Join(", ", traits).ToLowerInvariant();
        }

        private static string BuildDailySummary(Pawn pawn)
        {
            List<string> skills = pawn?.skills?.skills?
                .Where(s => s?.def != null)
                .OrderByDescending(s => s.Level)
                .Take(2)
                .Select(s => s.def.skillLabel)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();
            if (skills == null || skills.Count == 0)
            {
                return "keep routine work steady and predictable";
            }

            return $"focus on {string.Join(" and ", skills)} in daily life";
        }

        private static string BuildSocialSummary(Pawn pawn)
        {
            int social = pawn?.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
            if (social >= 10)
            {
                return "read people quickly and persuade with calm precision";
            }

            if (social >= 5)
            {
                return "balance caution with cooperation";
            }

            return "speak directly and keep distance until trust is earned";
        }

        private static string BuildConflictSummary(Pawn pawn)
        {
            int melee = pawn?.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
            int shooting = pawn?.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0;
            if (Math.Max(melee, shooting) >= 8)
            {
                return "turn firm and decisive to regain control";
            }

            return "stay tense but disciplined, avoiding reckless risks";
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
