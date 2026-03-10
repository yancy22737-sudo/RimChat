using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimChat.AI;
using RimChat.Core;
using RimChat.Persistence;

namespace RimChat.UI
{
    // Responsibilities: build request-time prompt context for RPG pawn dialogue turns.
    // Dependencies: RimChat.AI.ChatMessageData, RimChat.Persistence.PromptPersistenceService.
    public partial class Dialog_RPGPawnDialogue
    {
        private const string OpeningFallbackUserPrompt =
            "Start the conversation naturally in-character with one concise opening line.";

        private static string NormalizeHistoryAssistantContent(string rawResponse, string visibleDialogueText)
        {
            if (!string.IsNullOrWhiteSpace(visibleDialogueText))
            {
                return NormalizeVisibleNpcDialogueText(visibleDialogueText);
            }

            return ExtractNarrativeOnly(rawResponse);
        }

        private static string ExtractNarrativeOnly(string rawResponse)
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

        private static string NormalizeVisibleNpcDialogueText(string content)
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

            using (RpgPromptTurnContextScope.Push(currentTurnUserIntent))
            {
                return RimChat.Persistence.PromptPersistenceService.Instance.BuildRPGFullSystemPrompt(
                    initiator,
                    target,
                    false,
                    tags);
            }
        }
    }
}
