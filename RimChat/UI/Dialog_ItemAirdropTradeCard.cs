using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimChat.Memory;
using RimWorld;
using UnityEngine;
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

        public string ToVisibleSummary()
        {
            string offerLabel = string.IsNullOrWhiteSpace(OfferItemLabel)
                ? (OfferItemDefName ?? string.Empty)
                : OfferItemLabel;
            return "RimChat_AirdropTradeCardSubmitSummary".Translate(
                Need ?? string.Empty,
                Math.Max(1, RequestedCount),
                offerLabel,
                Math.Max(1, OfferItemCount)).ToString();
        }
    }

    public class Dialog_ItemAirdropTradeCard : Window
    {
        private readonly FactionDialogueSession session;
        private readonly Action<ItemAirdropTradeCardPayload> onSubmitted;

        private string needText = string.Empty;
        private string requestedCountText = "1";
        private string offerCountText = "200";
        private string selectedOfferDefName = string.Empty;
        private string selectedOfferLabel = string.Empty;
        private Vector2 inventoryScrollPos = Vector2.zero;
        private readonly List<InventoryDisplayEntry> inventoryItems = new List<InventoryDisplayEntry>();

        private const float TitleHeight = 40f;
        private const float NeedAreaHeight = 52f;
        private const float FooterHeight = 120f;
        private const float Padding = 10f;
        private const float RowHeight = 30f;

        public override Vector2 InitialSize => new Vector2(760f, 620f);

        public Dialog_ItemAirdropTradeCard(
            FactionDialogueSession session,
            Faction faction,
            Action<ItemAirdropTradeCardPayload> onSubmitted)
        {
            this.session = session;
            this.onSubmitted = onSubmitted;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
            closeOnAccept = false;
            forcePause = true;
            draggable = true;

            LoadInventoryItems();
        }

        public override void PreOpen()
        {
            base.PreOpen();
            ApplyCounterofferDefaults();
            SeedNeedFromLatestPlayerMessage();
            EnsureDefaultOfferSelection();
        }

        private void SeedNeedFromLatestPlayerMessage()
        {
            if (!string.IsNullOrWhiteSpace(needText) || session?.messages == null)
            {
                return;
            }

            DialogueMessageData latestPlayer = session.messages
                .LastOrDefault(msg => msg != null && msg.isPlayer && !msg.IsSystemMessage());
            string text = latestPlayer?.message?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text))
            {
                needText = text;
            }
        }

        private void ApplyCounterofferDefaults()
        {
            if (session == null || string.IsNullOrWhiteSpace(session.lastAirdropCounterofferDefName))
            {
                return;
            }

            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(session.lastAirdropCounterofferDefName.Trim());
            string label = def?.label ?? session.lastAirdropCounterofferDefName.Trim();
            needText = string.IsNullOrWhiteSpace(needText) ? label : needText;

            if (session.lastAirdropCounterofferCount > 0)
            {
                requestedCountText = session.lastAirdropCounterofferCount.ToString(CultureInfo.InvariantCulture);
            }

            if (session.lastAirdropCounterofferSilver > 0)
            {
                offerCountText = session.lastAirdropCounterofferSilver.ToString(CultureInfo.InvariantCulture);
            }
        }

        private void EnsureDefaultOfferSelection()
        {
            if (!string.IsNullOrWhiteSpace(selectedOfferDefName))
            {
                return;
            }

            InventoryDisplayEntry silver = inventoryItems.FirstOrDefault(entry =>
                string.Equals(entry.DefName, "Silver", StringComparison.OrdinalIgnoreCase));
            InventoryDisplayEntry first = silver ?? inventoryItems.FirstOrDefault();
            if (first != null)
            {
                selectedOfferDefName = first.DefName;
                selectedOfferLabel = first.Label;
            }
        }

        private void LoadInventoryItems()
        {
            inventoryItems.Clear();
            Map map = Find.AnyPlayerHomeMap ?? Find.CurrentMap;
            if (map == null)
            {
                return;
            }

            List<Thing> tradeables = CollectBeaconTradeableThings(map);
            if (tradeables.Count == 0)
            {
                return;
            }

            var grouped = tradeables
                .Where(thing => thing?.def != null)
                .GroupBy(thing => thing.def.defName)
                .Select(group => new InventoryDisplayEntry
                {
                    DefName = group.Key,
                    Label = group.First().def.label ?? group.Key,
                    Count = group.Sum(thing => Math.Max(0, thing.stackCount)),
                    UnitPrice = Math.Max(0f, group.First().MarketValue)
                })
                .Where(entry => entry.Count > 0)
                .OrderByDescending(entry => entry.Count)
                .ThenBy(entry => entry.Label)
                .ToList();

            inventoryItems.AddRange(grouped);
        }

        private static List<Thing> CollectBeaconTradeableThings(Map map)
        {
            var result = new List<Thing>();
            List<Building_OrbitalTradeBeacon> beacons = Building_OrbitalTradeBeacon.AllPowered(map)?.ToList();
            if (beacons == null || beacons.Count == 0)
            {
                return result;
            }

            var cells = new HashSet<IntVec3>();
            foreach (Building_OrbitalTradeBeacon beacon in beacons)
            {
                if (beacon == null || !beacon.Spawned || beacon.Map != map)
                {
                    continue;
                }

                foreach (IntVec3 cell in beacon.TradeableCells)
                {
                    cells.Add(cell);
                }
            }

            var seenThingIds = new HashSet<int>();
            foreach (IntVec3 cell in cells)
            {
                List<Thing> thingsAt = map.thingGrid?.ThingsListAt(cell);
                if (thingsAt == null)
                {
                    continue;
                }

                foreach (Thing thing in thingsAt)
                {
                    if (!IsValidBeaconPaymentThing(thing) || !seenThingIds.Add(thing.thingIDNumber))
                    {
                        continue;
                    }

                    result.Add(thing);
                }
            }

            return result;
        }

        private static bool IsValidBeaconPaymentThing(Thing thing)
        {
            return thing != null &&
                   thing.Spawned &&
                   !thing.Destroyed &&
                   thing.stackCount > 0 &&
                   thing.def != null &&
                   thing.def.category == ThingCategory.Item &&
                   !thing.def.IsCorpse &&
                   TradeUtility.EverPlayerSellable(thing.def) &&
                   !thing.IsForbidden(Faction.OfPlayer);
        }

        public override void DoWindowContents(Rect inRect)
        {
            float y = inRect.y;
            Rect titleRect = new Rect(inRect.x, y, inRect.width, TitleHeight);
            DrawTitle(titleRect);
            y += TitleHeight + Padding;

            Rect needRect = new Rect(inRect.x, y, inRect.width, NeedAreaHeight);
            DrawNeedInput(needRect);
            y += NeedAreaHeight + Padding;

            float panelHeight = Mathf.Max(120f, inRect.height - (y - inRect.y) - FooterHeight - Padding);
            Rect inventoryRect = new Rect(inRect.x, y, inRect.width, panelHeight);
            DrawInventoryPanel(inventoryRect);

            Rect footerRect = new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight);
            DrawFooter(footerRect);
        }

        private void DrawTitle(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.15f, 0.15f, 0.18f));
            GUI.color = new Color(0.92f, 0.92f, 0.96f);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 7f, rect.width - 24f, 28f), "RimChat_AirdropTradeCard_Title".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawNeedInput(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.13f));
            Rect labelRect = new Rect(rect.x + 10f, rect.y + 5f, 90f, 22f);
            Widgets.Label(labelRect, "RimChat_AirdropTradeCard_NeedLabel".Translate());

            Rect inputRect = new Rect(rect.x + 100f, rect.y + 4f, rect.width - 110f, 28f);
            Widgets.DrawBoxSolid(inputRect, new Color(0.18f, 0.18f, 0.22f));
            string newNeed = Widgets.TextField(inputRect, needText ?? string.Empty);
            if (newNeed.Length <= 240)
            {
                needText = newNeed;
            }
        }

        private void DrawInventoryPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.08f, 0.11f, 0.98f));
            GUI.color = new Color(0.26f, 0.26f, 0.32f, 0.95f);
            Widgets.DrawBox(rect);
            GUI.color = Color.white;

            Rect headerRect = new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 22f);
            Widgets.Label(headerRect, "RimChat_AirdropTradeCard_Inventory".Translate());

            Rect infoRect = new Rect(rect.x + 8f, rect.y + 28f, rect.width - 16f, 20f);
            string selectedLabel = string.IsNullOrWhiteSpace(selectedOfferLabel)
                ? "RimChat_AirdropTradeCard_NoOfferItem".Translate().ToString()
                : "RimChat_AirdropTradeCard_SelectedOfferItem".Translate(selectedOfferLabel).ToString();
            GUI.color = new Color(0.75f, 0.82f, 0.92f, 0.95f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(infoRect, selectedLabel);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Rect listRect = new Rect(rect.x + 4f, rect.y + 50f, rect.width - 8f, rect.height - 54f);
            if (inventoryItems.Count == 0)
            {
                Widgets.Label(new Rect(listRect.x + 6f, listRect.y + 6f, listRect.width - 12f, 24f), "RimChat_AirdropTradeCard_NoInventory".Translate());
                return;
            }

            float contentHeight = Mathf.Max(1f, inventoryItems.Count * RowHeight);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, contentHeight);
            inventoryScrollPos = GUI.BeginScrollView(listRect, inventoryScrollPos, viewRect);

            float rowY = 0f;
            foreach (InventoryDisplayEntry entry in inventoryItems)
            {
                DrawInventoryRow(entry, viewRect.width, rowY);
                rowY += RowHeight;
            }

            GUI.EndScrollView();
        }

        private void DrawInventoryRow(InventoryDisplayEntry entry, float width, float y)
        {
            Rect rowRect = new Rect(2f, y, width - 4f, RowHeight - 2f);
            bool selected = string.Equals(selectedOfferDefName, entry.DefName, StringComparison.OrdinalIgnoreCase);
            Color bg = selected ? new Color(0.18f, 0.4f, 0.66f, 0.78f) : new Color(0.12f, 0.13f, 0.17f, 0.82f);
            Widgets.DrawBoxSolid(rowRect, bg);
            if (selected)
            {
                GUI.color = new Color(0.42f, 0.58f, 0.85f, 0.95f);
                Widgets.DrawBox(rowRect);
                GUI.color = Color.white;
            }

            Text.Font = GameFont.Tiny;
            string text = $"{entry.Label} ({entry.DefName}) x{entry.Count} @{entry.UnitPrice:F1}";
            Widgets.Label(new Rect(rowRect.x + 4f, rowRect.y + 6f, rowRect.width - 8f, rowRect.height - 6f), text);
            Text.Font = GameFont.Small;

            if (Widgets.ButtonInvisible(rowRect))
            {
                selectedOfferDefName = entry.DefName;
                selectedOfferLabel = entry.Label;
            }
        }

        private void DrawFooter(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.13f));
            float left = rect.x + 10f;
            float top = rect.y + 8f;

            float buttonWidth = Mathf.Clamp(rect.width * 0.24f, 130f, 180f);
            Rect cancelRect = new Rect(rect.xMax - buttonWidth - 10f, rect.yMax - 40f, buttonWidth, 32f);
            Rect submitRect = new Rect(cancelRect.x - buttonWidth - 10f, cancelRect.y, buttonWidth, 32f);

            float inputAreaWidth = Mathf.Max(200f, submitRect.x - left - 12f);
            DrawFooterInputs(new Rect(left, top, inputAreaWidth, 56f));

            bool canSubmit = CanSubmit();
            GUI.enabled = canSubmit;
            if (Widgets.ButtonText(submitRect, "RimChat_AirdropTradeCard_Submit".Translate()))
            {
                Submit();
            }
            GUI.enabled = true;

            if (Widgets.ButtonText(cancelRect, "RimChat_AirdropTradeCard_Cancel".Translate()))
            {
                Close();
            }
        }

        private void DrawFooterInputs(Rect rect)
        {
            float groupWidth = (rect.width - 10f) * 0.5f;
            DrawIntegerField(
                new Rect(rect.x, rect.y, groupWidth, rect.height),
                "RimChat_AirdropTradeCard_CountLabel".Translate().ToString(),
                requestedCountText,
                out requestedCountText,
                1,
                5000);

            DrawIntegerField(
                new Rect(rect.x + groupWidth + 10f, rect.y, groupWidth, rect.height),
                "RimChat_AirdropTradeCard_OfferCountLabel".Translate().ToString(),
                offerCountText,
                out offerCountText,
                1,
                1000000);
        }

        private static void DrawIntegerField(Rect rect, string label, string current, out string updated, int min, int max)
        {
            updated = current;
            float labelWidth = Mathf.Min(72f, rect.width * 0.42f);
            Widgets.Label(new Rect(rect.x, rect.y + 2f, labelWidth, 22f), label);

            Rect fieldRect = new Rect(rect.x + labelWidth + 4f, rect.y, rect.width - labelWidth - 4f, 24f);
            Widgets.DrawBoxSolid(fieldRect, new Color(0.18f, 0.18f, 0.22f));
            string input = Widgets.TextField(fieldRect, current ?? string.Empty);
            if (!int.TryParse(input, out int parsed))
            {
                return;
            }

            if (parsed < min || parsed > max)
            {
                return;
            }

            updated = parsed.ToString(CultureInfo.InvariantCulture);
        }

        private bool CanSubmit()
        {
            return !string.IsNullOrWhiteSpace(needText) &&
                   !string.IsNullOrWhiteSpace(selectedOfferDefName) &&
                   int.TryParse(requestedCountText, out int requestedCount) && requestedCount > 0 &&
                   int.TryParse(offerCountText, out int offerCount) && offerCount > 0;
        }

        private void Submit()
        {
            if (!CanSubmit())
            {
                return;
            }

            var payload = new ItemAirdropTradeCardPayload
            {
                Need = needText?.Trim() ?? string.Empty,
                RequestedCount = int.Parse(requestedCountText, CultureInfo.InvariantCulture),
                OfferItemDefName = selectedOfferDefName,
                OfferItemLabel = selectedOfferLabel,
                OfferItemCount = int.Parse(offerCountText, CultureInfo.InvariantCulture),
                Scenario = "trade"
            };

            onSubmitted?.Invoke(payload);
            Close();
        }

        private class InventoryDisplayEntry
        {
            public string DefName { get; set; }
            public string Label { get; set; }
            public int Count { get; set; }
            public float UnitPrice { get; set; }
        }
    }
}
