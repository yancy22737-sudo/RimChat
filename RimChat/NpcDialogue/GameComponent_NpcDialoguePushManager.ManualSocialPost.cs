using System;
using System.Collections.Generic;
using RimChat.AI;

namespace RimChat.NpcDialogue
{
    /// <summary>
    /// Dependencies: proactive dialogue generation prompt assembly.
    /// Responsibility: inject manual social-post context into proactive NPC dialogue prompts.
    /// </summary>
    public partial class GameComponent_NpcDialoguePushManager
    {
        private void AppendManualSocialPostPrompt(List<ChatMessageData> messages, NpcDialogueTriggerContext context)
        {
            if (messages == null || context == null || !string.Equals(context.SourceTag, "manual_social_post", StringComparison.Ordinal))
            {
                return;
            }

            if (!TryParseManualSocialPostReason(context.Reason, out string title, out string body))
            {
                return;
            }

            string prompt =
                "This proactive diplomacy message is a direct reaction to a player-authored public social-circle post.\n" +
                "Public context: all factions can potentially see this post.\n" +
                "Author: the player colony.\n" +
                $"Post title: {title}\n" +
                $"Post body: {body}\n" +
                "Your reply must explicitly react to that post's content and stance instead of producing generic small talk.\n" +
                "Allowed tones include support, skepticism, negotiation, warning, pressure, provocation, or recruitment, depending on the faction stance.\n";
            messages.Add(new ChatMessageData { role = "user", content = prompt });
        }

        private static bool TryParseManualSocialPostReason(string reason, out string title, out string body)
        {
            title = string.Empty;
            body = string.Empty;
            if (string.IsNullOrWhiteSpace(reason) || !reason.StartsWith("manual_social_post|", StringComparison.Ordinal))
            {
                return false;
            }

            string payload = reason.Substring("manual_social_post|".Length);
            string[] segments = payload.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i] ?? string.Empty;
                int separator = segment.IndexOf('=');
                if (separator <= 0 || separator >= segment.Length - 1)
                {
                    continue;
                }

                string key = segment.Substring(0, separator).Trim();
                string value = segment.Substring(separator + 1).Trim();
                if (string.Equals(key, "title", StringComparison.OrdinalIgnoreCase))
                {
                    title = value;
                }
                else if (string.Equals(key, "body", StringComparison.OrdinalIgnoreCase))
                {
                    body = value;
                }
            }

            return title.Length > 0 || body.Length > 0;
        }
    }
}
