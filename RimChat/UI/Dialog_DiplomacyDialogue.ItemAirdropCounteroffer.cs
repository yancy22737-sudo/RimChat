using System;
using System.Globalization;
using System.Text.RegularExpressions;
using RimChat.Memory;

namespace RimChat.UI
{
    public partial class Dialog_DiplomacyDialogue
    {
        private static readonly Regex AirdropCounterofferPattern = new Regex(
            @"(?im)^(?:重报价|counteroffer)\s*:\s*item=(?<item>[A-Za-z0-9_\.]+)\s+count=(?<count>\d{1,5})\s+silver=(?<silver>\d{1,9})(?:\s+reason=(?<reason>.+))?\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static bool TryCaptureAndCacheAirdropCounteroffer(string dialogueText, FactionDialogueSession currentSession)
        {
            if (currentSession == null || string.IsNullOrWhiteSpace(dialogueText))
            {
                return false;
            }

            Match match = AirdropCounterofferPattern.Match(dialogueText);
            if (!match.Success)
            {
                return false;
            }

            string item = match.Groups["item"].Value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(item) ||
                !int.TryParse(match.Groups["count"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) ||
                !int.TryParse(match.Groups["silver"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int silver))
            {
                return false;
            }

            string reason = match.Groups["reason"].Success
                ? (match.Groups["reason"].Value?.Trim() ?? string.Empty)
                : string.Empty;
            currentSession.CacheAirdropCounteroffer(item, count, silver, reason);
            return true;
        }
    }
}
