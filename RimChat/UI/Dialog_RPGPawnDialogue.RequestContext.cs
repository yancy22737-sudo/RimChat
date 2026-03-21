using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RimChat.AI;
using RimChat.Config;
using RimChat.Core;
using RimChat.Persistence;
using RimChat.Prompting;
using Verse;

namespace RimChat.UI
{
    // Responsibilities: build request-time prompt context for RPG pawn dialogue turns.
    // Dependencies: RimChat.AI.ChatMessageData, RimChat.Persistence.PromptPersistenceService.
    public partial class Dialog_RPGPawnDialogue
    {
        private const string OpeningFallbackUserPrompt =
            "Start the conversation naturally in-character with one concise opening line.";

        private string NormalizeHistoryAssistantContent(string rawResponse, string visibleDialogueText)
        {
            if (!string.IsNullOrWhiteSpace(visibleDialogueText))
            {
                return NormalizeVisibleNpcDialogueText(visibleDialogueText);
            }

            return ExtractNarrativeOnly(rawResponse);
        }

        private string ExtractNarrativeOnly(string rawResponse)
        {
            string text = rawResponse?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            int firstBrace = text.IndexOf('{');
            if (firstBrace == 0)
            {
                return string.Empty;
            }

            string narrative = firstBrace > 0 ? text.Substring(0, firstBrace).Trim() : text;
            return NormalizeVisibleNpcDialogueText(narrative);
        }

        private string NormalizeVisibleNpcDialogueText(string content)
        {
            string normalized = CollapseWhitespace(content);
            ImmersionGuardResult guardResult = ImmersionOutputGuard.ValidateVisibleDialogue(normalized);
            if (!guardResult.IsValid)
            {
                Log.Warning($"[RimChat] Immersion guard blocked RPG visible text: reason={ImmersionOutputGuard.BuildViolationTag(guardResult.ViolationReason)}, snippet={guardResult.ViolationSnippet}");
                normalized = ImmersionOutputGuard.BuildLocalFallbackDialogue(DialogueUsageChannel.Rpg);
            }
            else
            {
                normalized = guardResult.VisibleDialogue;
            }

            if (!ShouldApplyNonVerbalSpeechFormatting())
            {
                return normalized;
            }

            return EnsureNonVerbalSpeechFormat(normalized);
        }

        private static string CollapseWhitespace(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(content.Length);
            bool previousWasWhitespace = false;
            for (int i = 0; i < content.Length; i++)
            {
                char character = content[i];
                if (!char.IsWhiteSpace(character))
                {
                    sb.Append(character);
                    previousWasWhitespace = false;
                    continue;
                }

                if (previousWasWhitespace)
                {
                    continue;
                }

                sb.Append(' ');
                previousWasWhitespace = true;
            }

            return sb.ToString().Trim();
        }

        private bool ShouldApplyNonVerbalSpeechFormatting()
        {
            return RimChatMod.Settings?.EnableRPGNonVerbalPawnSpeech == true && IsNonVerbalSpeechPawn(target);
        }

        private string EnsureNonVerbalSpeechFormat(string normalized)
        {
            bool useFullWidth = UseFullWidthParentheses();
            string open = useFullWidth ? "（" : "(";
            string close = useFullWidth ? "）" : ")";
            if (TryParseSoundThoughtPair(normalized, out string sound, out string thought))
            {
                return $"{sound}{open}{thought}{close}";
            }

            string defaultSound = ResolveDefaultNonVerbalSound(target);
            string thoughtText = string.IsNullOrWhiteSpace(normalized)
                ? "RimChat_RPGNonVerbalFallbackThought".Translate().ToString()
                : normalized;
            return $"{defaultSound}{open}{thoughtText}{close}";
        }

        private static bool TryParseSoundThoughtPair(string text, out string sound, out string thought)
        {
            sound = string.Empty;
            thought = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            Match match = Regex.Match(text, @"^\s*(?<sound>.+?)\s*[\(（]\s*(?<thought>.+?)\s*[\)）]\s*$");
            if (!match.Success)
            {
                return false;
            }

            sound = match.Groups["sound"].Value.Trim();
            thought = match.Groups["thought"].Value.Trim();
            return !string.IsNullOrWhiteSpace(sound) && !string.IsNullOrWhiteSpace(thought);
        }

        private static bool UseFullWidthParentheses()
        {
            string folder = LanguageDatabase.activeLanguage?.folderName ?? string.Empty;
            return string.Equals(folder, "ChineseSimplified", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(folder, "ChineseTraditional", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNonVerbalSpeechPawn(Pawn pawn)
        {
            return IsAnimalPawn(pawn) || IsMechanoidPawn(pawn) || IsBabyPawn(pawn);
        }

        private static bool IsAnimalPawn(Pawn pawn)
        {
            return pawn?.RaceProps?.Animal == true;
        }

        private static bool IsMechanoidPawn(Pawn pawn)
        {
            string fleshType = pawn?.RaceProps?.FleshType?.defName;
            return string.Equals(fleshType, "Mechanoid", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBabyPawn(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            try
            {
                object stage = pawn.GetType().GetProperty("DevelopmentalStage")?.GetValue(pawn, null);
                return stage != null && string.Equals(stage.ToString(), "Baby", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string ResolveNonVerbalSpeakerKind(Pawn pawn)
        {
            if (IsAnimalPawn(pawn))
            {
                return "animal";
            }

            if (IsBabyPawn(pawn))
            {
                return "baby";
            }

            if (IsMechanoidPawn(pawn))
            {
                return "mechanoid";
            }

            return "nonverbal";
        }

        private static string ResolveDefaultNonVerbalSound(Pawn pawn)
        {
            if (IsAnimalPawn(pawn))
            {
                return "RimChat_RPGNonVerbalSound_Animal".Translate().ToString();
            }

            if (IsBabyPawn(pawn))
            {
                return "RimChat_RPGNonVerbalSound_Baby".Translate().ToString();
            }

            if (IsMechanoidPawn(pawn))
            {
                return "RimChat_RPGNonVerbalSound_Mechanoid".Translate().ToString();
            }

            return "RimChat_RPGNonVerbalSound_Animal".Translate().ToString();
        }

        private string AppendNonVerbalPromptConstraint(string basePrompt)
        {
            if (!ShouldApplyNonVerbalSpeechFormatting())
            {
                return basePrompt ?? string.Empty;
            }

            RpgPromptDefaultsConfig defaults = RpgPromptDefaultsProvider.GetDefaults();
            string template = defaults?.NonVerbalOutputConstraintTemplate;
            if (string.IsNullOrWhiteSpace(template))
            {
                return basePrompt ?? string.Empty;
            }

            bool useFullWidth = UseFullWidthParentheses();
            const string templateId = "prompt_templates.rpg_non_verbal_constraint";
            PromptRenderContext context = PromptRenderContext.Create(templateId, "rpg");
            context.SetValue("pawn.speaker.kind", ResolveNonVerbalSpeakerKind(target));
            context.SetValue("pawn.speaker.default_sound", ResolveDefaultNonVerbalSound(target));
            context.SetValue("pawn.speaker.animal_sound", "RimChat_RPGNonVerbalSound_Animal".Translate().ToString());
            context.SetValue("pawn.speaker.baby_sound", "RimChat_RPGNonVerbalSound_Baby".Translate().ToString());
            context.SetValue("pawn.speaker.mechanoid_sound", "RimChat_RPGNonVerbalSound_Mechanoid".Translate().ToString());
            context.SetValue("system.punctuation.open_paren", useFullWidth ? "（" : "(");
            context.SetValue("system.punctuation.close_paren", useFullWidth ? "）" : ")");
            string rendered = PromptTemplateRenderer.RenderOrThrow(templateId, "rpg", template, context);
            if (string.IsNullOrWhiteSpace(rendered))
            {
                return basePrompt ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(basePrompt))
            {
                return rendered;
            }

            return $"{basePrompt}\n\n{rendered}";
        }

        private static bool HasVisibleAssistantReply(IEnumerable<ChatMessageData> messages)
        {
            if (messages == null)
            {
                return false;
            }

            return messages.Any(message =>
                message != null &&
                string.Equals(message.role, "assistant", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(message.content));
        }

        private static string ExtractLatestVisibleUserIntent(IEnumerable<ChatMessageData> messages)
        {
            if (messages == null)
            {
                return string.Empty;
            }

            List<ChatMessageData> reversed = messages
                .Where(message =>
                    message != null &&
                    string.Equals(message.role, "user", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(message.content))
                .Reverse()
                .ToList();

            for (int i = 0; i < reversed.Count; i++)
            {
                string content = reversed[i].content?.Trim() ?? string.Empty;
                if (!IsPromptSeedUserMessage(content))
                {
                    return content;
                }
            }

            return string.Empty;
        }

        private static bool IsPromptSeedUserMessage(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            return string.Equals(content.Trim(), OpeningFallbackUserPrompt, StringComparison.Ordinal) ||
                content.StartsWith("A proactive trigger opened this chat from NPC side.", StringComparison.Ordinal);
        }

        private string BuildRpgSystemPromptForRequest(bool openingTurn, string currentTurnUserIntent)
        {
            var settings = RimChatMod.Settings;
            List<string> tags = ParseSceneTagsCsv(settings?.RpgManualSceneTagsCsv) ?? new List<string>();
            if (openingTurn && !tags.Contains("phase:opening"))
            {
                tags.Add("phase:opening");
            }

            string prompt;
            using (RpgPromptTurnContextScope.Push(currentTurnUserIntent))
            {
                prompt = RimChat.Persistence.PromptPersistenceService.Instance.BuildRPGFullSystemPrompt(
                    initiator,
                    target,
                    false,
                    tags);
            }

            prompt = AppendNonVerbalPromptConstraint(prompt);
            UpdateRpgActionContractGuard(prompt, settings?.EnableRPGAPI == true);
            return prompt;
        }

        private void UpdateRpgActionContractGuard(string prompt, bool rpgApiEnabled)
        {
            if (!rpgApiEnabled)
            {
                suppressAutoMemoryFallbackForTurn = false;
                return;
            }

            bool hasContract = HasRpgActionContract(prompt);
            suppressAutoMemoryFallbackForTurn = !hasContract;
            if (!hasContract)
            {
                Log.Warning("[RimChat] RPG prompt missing response contract body; auto memory fallback disabled for this turn.");
            }
        }

        private static bool HasRpgActionContract(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return false;
            }

            return prompt.IndexOf("<response_contract>", StringComparison.OrdinalIgnoreCase) >= 0
                || prompt.IndexOf("=== AVAILABLE NPC ACTIONS", StringComparison.OrdinalIgnoreCase) >= 0
                || prompt.IndexOf("Allowed actions:", StringComparison.OrdinalIgnoreCase) >= 0
                || prompt.IndexOf("ExitDialogueCooldown", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
