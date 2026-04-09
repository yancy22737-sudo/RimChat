using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RimChat.AI;
using RimChat.Config;
using RimChat.Core;
using RimChat.Dialogue;
using RimChat.Persistence;
using RimChat.Prompting;
using RimWorld;
using Verse;

namespace RimChat.UI
{
    // Responsibilities: build request-time prompt context for RPG pawn dialogue turns.
    // Dependencies: RimChat.AI.ChatMessageData, RimChat.Persistence.PromptPersistenceService.
    public partial class Dialog_RPGPawnDialogue
    {
        private const string OpeningFallbackUserPrompt =
            "Start the conversation naturally in-character with one concise opening line.";

        private string NormalizeHistoryAssistantContent(DialogueResponseEnvelope envelope, string visibleDialogueText)
        {
            if (envelope != null)
            {
                string normalizedVisible = NormalizeEnvelopeVisibleDialogueForDisplay(envelope, "history");
                if (!string.IsNullOrWhiteSpace(normalizedVisible))
                {
                    return normalizedVisible;
                }
            }

            if (!string.IsNullOrWhiteSpace(visibleDialogueText))
            {
                return NormalizeVisibleNpcDialogueText(visibleDialogueText);
            }

            return ExtractNarrativeOnly(envelope?.RawResponse);
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
            if (!string.IsNullOrWhiteSpace(guardResult?.TrailingActionsJson))
            {
                Log.Warning("[RimChat] RPG display stripped trailing action JSON from visible text path: source=NormalizeVisibleNpcDialogueText");
            }

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

        private string NormalizeEnvelopeVisibleDialogueForDisplay(DialogueResponseEnvelope envelope, string sourceTag)
        {
            if (envelope == null)
            {
                return string.Empty;
            }

            string normalized = NormalizeVisibleNpcDialogueText(envelope.VisibleDialogue ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(envelope.ActionsJson) &&
                envelope.ProtocolKind == DialogueResponseProtocolKind.LegacyText)
            {
                Log.Warning(
                    $"[RimChat] RPG UI consumed legacy dialogue bridge with detached actions JSON: source={sourceTag}, protocol={envelope.ProtocolKind}, visible_len={normalized.Length}, actions_len={envelope.ActionsJson.Length}");
            }

            return normalized;
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

            return "human";
        }

        private static string ResolveRacialType(Pawn pawn)
        {
            if (pawn == null || pawn.RaceProps == null)
            {
                return "unknown";
            }

            if (IsAnimalPawn(pawn))
            {
                return "animal";
            }

            if (IsMechanoidPawn(pawn))
            {
                return "mechanoid";
            }

            if (IsBabyPawn(pawn))
            {
                return "baby";
            }

            if (pawn.RaceProps.intelligence == Intelligence.Humanlike)
            {
                return "human";
            }

            if (pawn.RaceProps.ToolUser)
            {
                return "tool_user";
            }

            return "other";
        }

        private static string ResolveSocialIdentity(Pawn pawn)
        {
            if (pawn == null)
            {
                return "unknown";
            }

            if (pawn.IsPrisonerOfColony)
            {
                return "prisoner";
            }

            if (pawn.guest != null)
            {
                return "guest";
            }

            if (pawn.IsSlave)
            {
                return "slave";
            }

            if (pawn.Faction == null || pawn.Faction.IsPlayer)
            {
                return "colonist";
            }

            if (pawn.Faction != null && pawn.Faction.PlayerRelationKind == FactionRelationKind.Ally)
            {
                return "ally";
            }

            if (pawn.Faction != null && pawn.Faction.PlayerRelationKind == FactionRelationKind.Hostile)
            {
                return "hostile";
            }

            return "visitor";
        }

        private static string ResolveRelationshipStatus(Pawn pawn)
        {
            if (pawn == null)
            {
                return "unknown";
            }

            if (pawn.Faction == null)
            {
                return "neutral";
            }

            if (pawn.Faction != null && pawn.Faction.PlayerRelationKind == FactionRelationKind.Ally)
            {
                return "friendly";
            }

            if (pawn.Faction != null && pawn.Faction.PlayerRelationKind == FactionRelationKind.Hostile)
            {
                return "hostile";
            }

            return "neutral";
        }

        private static string ResolvePersonalityTraits(Pawn pawn)
        {
            if (pawn == null || pawn.story == null)
            {
                return "none";
            }

            var traits = pawn.story.traits;
            if (traits == null || traits.allTraits == null || traits.allTraits.Count == 0)
            {
                return "none";
            }

            List<string> traitNames = new List<string>();
            foreach (var trait in traits.allTraits)
            {
                if (trait != null && trait.def != null)
                {
                    traitNames.Add(trait.def.defName);
                }
            }

            return traitNames.Count > 0 ? string.Join(", ", traitNames) : "none";
        }

        private static string BuildStyleGuidelines(Pawn pawn)
        {
            string racialType = ResolveRacialType(pawn);
            string socialIdentity = ResolveSocialIdentity(pawn);
            string relationshipStatus = ResolveRelationshipStatus(pawn);
            string personalityTraits = ResolvePersonalityTraits(pawn);

            var guidelines = new List<string>();

            switch (racialType)
            {
                case "human":
                    guidelines.Add("- Use normal human language, can express complex emotions and thoughts");
                    guidelines.Add("- Maintain appropriate tone based on relationship and context");
                    break;
                case "animal":
                    guidelines.Add("- Use non-verbal expression (sounds + inner thoughts)");
                    guidelines.Add("- Cannot understand complex language or concepts");
                    guidelines.Add("- React primarily to immediate needs and instincts");
                    break;
                case "baby":
                    guidelines.Add("- Use baby-like sounds and simple words");
                    guidelines.Add("- Express basic needs and emotions");
                    guidelines.Add("- Cannot engage in complex discussions");
                    break;
                case "mechanoid":
                    guidelines.Add("- Use concise, mechanical language");
                    guidelines.Add("- Focus on efficiency and function");
                    guidelines.Add("- May use technical terms or data");
                    guidelines.Add("- Maintain neutral, emotionless tone");
                    break;
                case "tool_user":
                    guidelines.Add("- Use simple, practical language");
                    guidelines.Add("- Focus on immediate needs and survival");
                    guidelines.Add("- May have limited vocabulary");
                    break;
                default:
                    guidelines.Add("- Use appropriate language for the racial type");
                    break;
            }

            switch (socialIdentity)
            {
                case "prisoner":
                    guidelines.Add("- As a prisoner, express desire for freedom or better conditions");
                    guidelines.Add("- May be desperate or willing to negotiate");
                    guidelines.Add("- Limited access to information and resources");
                    break;
                case "slave":
                    guidelines.Add("- As a slave, must obey orders and show submission");
                    guidelines.Add("- May express fear or hope for better treatment");
                    guidelines.Add("- Limited ability to refuse or negotiate");
                    break;
                case "guest":
                    guidelines.Add("- As a guest, be polite and respectful");
                    guidelines.Add("- May express gratitude for hospitality");
                    guidelines.Add("- Avoid controversial topics");
                    break;
                case "hostile":
                    guidelines.Add("- As an enemy, maintain hostile or defensive posture");
                    guidelines.Add("- Refuse cooperation and may threaten or attack");
                    guidelines.Add("- Show no willingness to negotiate");
                    break;
                case "colonist":
                    guidelines.Add("- As a colonist, show loyalty and belonging to the community");
                    guidelines.Add("- Express willingness to help and contribute");
                    guidelines.Add("- Maintain positive, cooperative attitude");
                    break;
                case "trader":
                    guidelines.Add("- As a trader, focus on commerce and fair exchange");
                    guidelines.Add("- Be willing to negotiate prices and deals");
                    guidelines.Add("- Maintain professional but neutral demeanor");
                    break;
                default:
                    guidelines.Add("- Behave according to social identity");
                    break;
            }

            switch (relationshipStatus)
            {
                case "friendly":
                    guidelines.Add("- Be warm, open, and helpful");
                    guidelines.Add("- Share information willingly");
                    guidelines.Add("- Offer assistance when possible");
                    break;
                case "hostile":
                    guidelines.Add("- Be cold, defensive, and uncooperative");
                    guidelines.Add("- Refuse to share information");
                    guidelines.Add("- May threaten or attack");
                    break;
                case "neutral":
                    guidelines.Add("- Be cautious but reserved");
                    guidelines.Add("- Provide only necessary information");
                    guidelines.Add("- Avoid taking sides in conflicts");
                    break;
                default:
                    guidelines.Add("- Adjust tone based on relationship status");
                    break;
            }

            return string.Join("\n", guidelines);
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

        private string ApplyNonVerbalSpeechFormatting(string basePrompt)
        {
            string result = basePrompt ?? string.Empty;

            if (ShouldApplyNonVerbalSpeechFormatting())
            {
                result = ApplyNonVerbalSpeechConstraintTemplate(result);
            }

            result = ApplyCharacterStyleConstraint(result);

            return result;
        }

        private string ApplyNonVerbalSpeechConstraintTemplate(string basePrompt)
        {
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

        private string ApplyCharacterStyleConstraint(string basePrompt)
        {
            RpgPromptDefaultsConfig defaults = RpgPromptDefaultsProvider.GetDefaults();
            string template = defaults?.CharacterStyleTemplate;
            if (string.IsNullOrWhiteSpace(template))
            {
                return basePrompt ?? string.Empty;
            }

            const string templateId = "prompt_templates.rpg_character_style_constraint";
            PromptRenderContext context = PromptRenderContext.Create(templateId, "rpg");
            context.SetValue("pawn.speaker.racial_type", ResolveRacialType(target));
            context.SetValue("pawn.speaker.social_identity", ResolveSocialIdentity(target));
            context.SetValue("pawn.speaker.relationship_status", ResolveRelationshipStatus(target));
            context.SetValue("pawn.speaker.personality_traits", ResolvePersonalityTraits(target));
            context.SetValue("pawn.speaker.style_guidelines", BuildStyleGuidelines(target));
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
            using (RpgPromptTurnContextScope.Push(
                currentTurnUserIntent,
                allowMemoryCompressionScheduling: !openingTurn,
                allowMemoryColdLoad: !openingTurn))
            {
                prompt = RimChat.Persistence.PromptPersistenceService.Instance.BuildRPGFullSystemPrompt(
                    initiator,
                    target,
                    false,
                    tags,
                    allowMemoryCompressionScheduling: !openingTurn,
                    allowMemoryColdLoad: !openingTurn);
            }

            prompt = ApplyNonVerbalSpeechFormatting(prompt);
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
