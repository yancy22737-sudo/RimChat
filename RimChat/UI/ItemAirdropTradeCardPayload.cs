using System;
using System.Globalization;
using Verse;

namespace RimChat.UI
{
    public class ItemAirdropTradeCardPayload
    {
        public string Need { get; set; }
        public int RequestedCount { get; set; }
        public string OfferItemDefName { get; set; }
        public string OfferItemLabel { get; set; }
        public int OfferItemCount { get; set; }
        public string Scenario { get; set; } = "trade";

        public string NeedDefName { get; set; }
        public string NeedLabel { get; set; }
        public string NeedSearchText { get; set; }
        public float NeedUnitPrice { get; set; }
        public float NeedReferenceTotalPrice { get; set; }
        public float OfferUnitPrice { get; set; }
        public float OfferTotalPrice { get; set; }

        public bool HasBoundNeed => !string.IsNullOrWhiteSpace(NeedDefName);

        public string GetNeedReferenceText()
        {
            return string.IsNullOrWhiteSpace(NeedDefName)
                ? (Need ?? string.Empty)
                : NeedDefName;
        }

        public string ToVisibleSummary()
        {
            string offerLabel = string.IsNullOrWhiteSpace(OfferItemLabel)
                ? (OfferItemDefName ?? string.Empty)
                : OfferItemLabel;
            string needDisplay = string.IsNullOrWhiteSpace(NeedLabel) ? (NeedDefName ?? Need ?? string.Empty) : NeedLabel;
            return "RimChat_AirdropTradeCardSubmitSummary".Translate(
                needDisplay,
                Math.Max(1, RequestedCount),
                offerLabel,
                Math.Max(1, OfferItemCount)).ToString();
        }
    }
}
