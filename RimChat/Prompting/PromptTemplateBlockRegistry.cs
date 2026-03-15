using System;
using System.Collections.Generic;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: none.
    /// Responsibility: persist runtime blocked-template flags produced by migration validation.
    /// </summary>
    internal static class PromptTemplateBlockRegistry
    {
        private static readonly object Guard = new object();
        private static readonly Dictionary<string, string> BlockedReasons =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void MarkBlocked(string templateId, string channel, string reason)
        {
            string key = BuildKey(templateId, channel);
            string normalizedReason = string.IsNullOrWhiteSpace(reason) ? "Blocked by migration validation." : reason.Trim();
            lock (Guard)
            {
                BlockedReasons[key] = normalizedReason;
            }
        }

        public static void Clear(string templateId, string channel)
        {
            string key = BuildKey(templateId, channel);
            lock (Guard)
            {
                BlockedReasons.Remove(key);
            }
        }

        public static bool TryGetReason(string templateId, string channel, out string reason)
        {
            string key = BuildKey(templateId, channel);
            lock (Guard)
            {
                return BlockedReasons.TryGetValue(key, out reason);
            }
        }

        private static string BuildKey(string templateId, string channel)
        {
            string id = string.IsNullOrWhiteSpace(templateId) ? "unknown_template" : templateId.Trim();
            string ch = string.IsNullOrWhiteSpace(channel) ? "unknown_channel" : channel.Trim();
            return $"{ch}::{id}";
        }
    }
}
