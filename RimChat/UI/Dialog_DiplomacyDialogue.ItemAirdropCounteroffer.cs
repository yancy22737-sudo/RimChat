using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using RimChat.Memory;

namespace RimChat.UI
{
    public partial class Dialog_DiplomacyDialogue
    {
        private static readonly Regex AirdropCounterofferPattern = new Regex(
            @"(?im)^(?:重报价|counteroffer)\s*:\s*item=(?<item>[A-Za-z0-9_\.]+)\s+count=(?<count>\d{1,5})\s+silver=(?<silver>\d{1,9})(?:\s+reason=(?<reason>.+))?\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex AirdropCounterofferChineseNaturalPattern = new Regex(
            @"(?is)(?:(?:关于|这批|这一单|这批货|这笔单子|按现在的库存|按我们的库存)[^。!\n\r]{0,40})?(?:(?<item>[A-Za-z0-9_\.一-龥]+)\s*)?(?:最多|可以|可出|能给你|愿意给你|愿意提供|可以提供|我方最多给你|我们最多给你)?[^。!\n\r]{0,20}?(?<count>\d{1,5})\s*(?:个|份|组|箱|件|单位|x|×|把)?[^。!\n\r]{0,40}?(?:作价|报价|要价|开价|价码|价格|总价|换价|换取|折价|折银|需要你付|需要支付|你付|需付|收你|算你|收|仅收|只需|只要|一共|合计|总计|总共|抹零|折后|实付|应付|给你)[^0-9\n\r]{0,8}(?<silver>\d{1,9})\s*(?:银|银币|块)?",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
        // Fallback: simple pricing without an explicit item count — e.g. "收你220银币".
        private static readonly Regex AirdropCounterofferChineseSimplePricePattern = new Regex(
            @"(?is)(?:收你|算你|收|仅收|只需|只要|一共|合计|总计|总共|抹零|折后|实付|应付|给你|作价|报价|要价)[^0-9\n\r]{0,8}(?<silver>\d{1,9})\s*(?:银|银币|块)?",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex AirdropCounterofferEnglishNaturalPattern = new Regex(
            @"(?is)(?:(?:for this order|for this shipment|with our current stock|with current stock)[^.!?\n\r]{0,40})?(?:(?<item>[A-Za-z0-9_\.]+)\s*)?(?:we can offer|we can send|we can spare|we can provide|our counteroffer is|at most|up to)?[^.!?\n\r]{0,20}?(?<count>\d{1,5})\s*(?:units?|stacks?|items?|x)?[^.!?\n\r]{0,40}?(?:for|at|priced at|price is|costs?|asking|quoted at|in exchange for)[^0-9\n\r]{0,8}(?<silver>\d{1,9})\s*silver",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex AirdropCounterofferReasonPattern = new Regex(
            @"(?is)(?:原因|理由|因为|due to|because|since)\s*[:：,，]?\s*(?<reason>[^\r\n]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static bool TryCaptureAndCacheAirdropCounteroffer(string dialogueText, FactionDialogueSession currentSession)
        {
            if (currentSession == null || string.IsNullOrWhiteSpace(dialogueText))
            {
                return false;
            }

            if (!TryExtractAirdropCounteroffer(dialogueText, currentSession, out string item, out int count, out int silver, out string reason))
            {
                return false;
            }

            currentSession.CacheAirdropCounteroffer(item, count, silver, reason);
            return true;
        }

        private static bool TryExtractAirdropCounteroffer(
            string dialogueText,
            FactionDialogueSession currentSession,
            out string item,
            out int count,
            out int silver,
            out string reason)
        {
            item = string.Empty;
            count = 0;
            silver = 0;
            reason = string.Empty;

            Match legacyMatch = AirdropCounterofferPattern.Match(dialogueText);
            if (legacyMatch.Success &&
                TryReadCounterofferMatch(legacyMatch, out item, out count, out silver, out reason))
            {
                return true;
            }

            // Full pattern: captures both item count and silver.
            Match naturalMatch = AirdropCounterofferChineseNaturalPattern.Match(dialogueText);
            if (!naturalMatch.Success)
            {
                naturalMatch = AirdropCounterofferEnglishNaturalPattern.Match(dialogueText);
            }

            if (naturalMatch.Success)
            {
                item = ResolveCounterofferItemFallback(naturalMatch.Groups["item"].Value, currentSession);
                string countStr = naturalMatch.Groups["count"].Value;
                string silverStr = naturalMatch.Groups["silver"].Value;
                if (!string.IsNullOrWhiteSpace(item) &&
                    int.TryParse(countStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out count) &&
                    int.TryParse(silverStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out silver))
                {
                    reason = ExtractCounterofferReason(dialogueText);
                    return count > 0 && silver >= 0;
                }
            }

            // Simple-price fallback: e.g. "收你220银币" — captures only silver,
            // infer count from the session's pending trade card.
            Match simpleMatch = AirdropCounterofferChineseSimplePricePattern.Match(dialogueText);
            if (simpleMatch.Success &&
                int.TryParse(simpleMatch.Groups["silver"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out silver))
            {
                item = ResolveCounterofferItemFallback(string.Empty, currentSession);
                count = ResolveCounterofferCountFallback(currentSession);
                if (!string.IsNullOrWhiteSpace(item) && count > 0 && silver >= 0)
                {
                    reason = ExtractCounterofferReason(dialogueText);
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadCounterofferMatch(
            Match match,
            out string item,
            out int count,
            out int silver,
            out string reason)
        {
            item = string.Empty;
            count = 0;
            silver = 0;
            reason = string.Empty;
            if (match == null || !match.Success)
            {
                return false;
            }

            item = match.Groups["item"].Value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(item) ||
                !int.TryParse(match.Groups["count"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out count) ||
                !int.TryParse(match.Groups["silver"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out silver))
            {
                return false;
            }

            reason = match.Groups["reason"].Success
                ? (match.Groups["reason"].Value?.Trim() ?? string.Empty)
                : string.Empty;
            return count > 0 && silver >= 0;
        }

        private static int ResolveCounterofferCountFallback(FactionDialogueSession currentSession)
        {
            if (currentSession == null)
            {
                return 0;
            }

            if (currentSession.lastAirdropCounterofferCount > 0)
            {
                return currentSession.lastAirdropCounterofferCount;
            }

            if (currentSession.hasPendingAirdropTradeCardReference &&
                currentSession.pendingAirdropTradeCardRequestedCount > 0)
            {
                return currentSession.pendingAirdropTradeCardRequestedCount;
            }

            // Fall back to the most recent airdrop trade card message count.
            DialogueMessageData lastTradeCard = currentSession.messages?
                .LastOrDefault(m => m != null && m.IsAirdropTradeCard());
            return lastTradeCard?.airdropRequestedCount ?? 0;
        }

        private static string ResolveCounterofferItemFallback(string rawItem, FactionDialogueSession currentSession)
        {
            string item = rawItem?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(item))
            {
                return item;
            }

            if (currentSession == null)
            {
                return string.Empty;
            }

            if (currentSession.hasPendingAirdropTradeCardReference &&
                !string.IsNullOrWhiteSpace(currentSession.pendingAirdropTradeCardNeedDefName))
            {
                return currentSession.pendingAirdropTradeCardNeedDefName.Trim();
            }

            return currentSession.messages?
                .Where(message => message != null && message.IsAirdropTradeCard())
                .Select(message => message.airdropNeedDefName?.Trim() ?? string.Empty)
                .LastOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        }

        private static string ExtractCounterofferReason(string dialogueText)
        {
            if (string.IsNullOrWhiteSpace(dialogueText))
            {
                return string.Empty;
            }

            Match reasonMatch = AirdropCounterofferReasonPattern.Match(dialogueText);
            if (!reasonMatch.Success)
            {
                return string.Empty;
            }

            return reasonMatch.Groups["reason"].Value?.Trim() ?? string.Empty;
        }
    }
}
