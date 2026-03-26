using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimChat.DiplomacySystem;
using RimChat.Memory;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    public class Dialog_ItemAirdropTradeCard : Window
    {
        private readonly FactionDialogueSession session;
        private readonly Action<ItemAirdropTradeCardPayload> onSubmitted;

        private readonly SearchStateManager searchState = new SearchStateManager();
        private readonly List<InventoryDisplayEntry> inventoryItems = new List<InventoryDisplayEntry>();
        private readonly List<InventoryDisplayEntry> filteredInventoryItems = new List<InventoryDisplayEntry>();

        private string needSearchText = string.Empty;
        private string requestedCountText = "1";
        private string offerCountText = "200";
        private string inventorySearchText = string.Empty;
        private string selectedOfferDefName = string.Empty;
        private string selectedOfferLabel = string.Empty;
        private int selectedOfferStackLimit = 1;
        private float selectedOfferUnitPrice = 1f;
        private Vector2 inventoryScrollPos = Vector2.zero;
        private ThingDefRecord boundNeedRecord;
        private bool showInlineSuggestions;
        private bool isLoadingInventory;
        private float inventoryLoadProgress;

        private const float TitleHeight = 54f;
        private const float SearchAreaHeight = 84f;
        private const float SuggestionRowHeight = 32f;
        private const float FooterHeight = 128f;
        private const float Padding = 12f;
        private const float InventoryRowHeight = 46f;
        private const float CardImageSize = 54f;

        public override Vector2 InitialSize => new Vector2(960f, 660f);

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
            LoadInventoryItemsAsync();
        }

        public override void PreOpen()
        {
            base.PreOpen();
            ApplyCounterofferDefaults();
            EnsureDefaultOfferSelection();
        }

        private void ApplyCounterofferDefaults()
        {
            if (session?.lastAirdropCounterofferCount > 0)
            {
                requestedCountText = session.lastAirdropCounterofferCount.ToString(CultureInfo.InvariantCulture);
            }

            if (session?.lastAirdropCounterofferSilver > 0)
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

            InventoryDisplayEntry silver = filteredInventoryItems.FirstOrDefault(entry =>
                string.Equals(entry.DefName, "Silver", StringComparison.OrdinalIgnoreCase));
            InventoryDisplayEntry fallback = silver ?? filteredInventoryItems.FirstOrDefault();
            if (fallback == null)
            {
                return;
            }

            ApplyOfferSelection(fallback);
        }

        private void LoadInventoryItemsAsync()
        {
            isLoadingInventory = true;
            inventoryLoadProgress = 0f;
            LongEventHandler.QueueLongEvent(() =>
            {
                inventoryItems.Clear();
                Map map = Find.AnyPlayerHomeMap ?? Find.CurrentMap;
                if (map != null)
                {
                    List<Thing> tradeables = CollectBeaconTradeableThings(map);
                    inventoryLoadProgress = 0.35f;
                    inventoryItems.AddRange(tradeables
                        .Where(thing => thing?.def != null)
                        .GroupBy(thing => thing.def.defName)
                        .Select(group => new InventoryDisplayEntry
                        {
                            DefName = group.Key,
                            Label = group.First().def.label ?? group.Key,
                            Count = group.Sum(thing => Math.Max(0, thing.stackCount)),
                            UnitPrice = Math.Max(0.01f, group.First().MarketValue),
                            StackLimit = Math.Max(1, group.First().def.stackLimit)
                        })
                        .Where(entry => entry.Count > 0)
                        .OrderByDescending(entry => entry.Count)
                        .ThenBy(entry => entry.Label)
                        .ToList());
                }

                inventoryLoadProgress = 0.8f;
                ApplyInventoryFilter();
                EnsureDefaultOfferSelection();
                inventoryLoadProgress = 1f;
                isLoadingInventory = false;
            }, "LoadingInventory", false, null);
        }

        private void ApplyInventoryFilter()
        {
            filteredInventoryItems.Clear();
            if (string.IsNullOrWhiteSpace(inventorySearchText))
            {
                filteredInventoryItems.AddRange(inventoryItems);
                return;
            }

            string normalized = inventorySearchText.Trim().ToLowerInvariant();
            filteredInventoryItems.AddRange(inventoryItems.Where(item =>
                item.Label.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0 ||
                item.DefName.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0));
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

            Rect searchRect = new Rect(inRect.x, y, inRect.width, SearchAreaHeight);
            DrawSearchArea(searchRect);
            y += SearchAreaHeight + Padding;

            if (showInlineSuggestions && searchState.Suggestions.Count > 0)
            {
                float suggestionHeight = SuggestionRowHeight * Math.Min(searchState.Suggestions.Count, 6);
                Rect suggestionRect = new Rect(inRect.x, y, inRect.width, suggestionHeight);
                DrawInlineSuggestionDropDown(suggestionRect);
                y += suggestionHeight + Padding;
            }

            float bodyHeight = inRect.height - (y - inRect.y) - FooterHeight - Padding;
            float cardHeight = 150f;
            Rect cardsRect = new Rect(inRect.x, y, inRect.width, cardHeight);
            DrawItemCards(cardsRect);
            y += cardHeight + Padding;

            Rect inventoryRect = new Rect(inRect.x, y, inRect.width, Mathf.Max(140f, bodyHeight - cardHeight - Padding));
            DrawInventoryPanel(inventoryRect);

            Rect footerRect = new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight);
            DrawFooter(footerRect);
        }

        private void DrawTitle(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.14f, 0.14f, 0.18f));
            GUI.color = new Color(0.95f, 0.95f, 0.98f);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x + 14f, rect.y + 8f, rect.width - 28f, 28f), "RimChat_AirdropTradeCard_Title".Translate());
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.68f, 0.72f, 0.82f);
            Widgets.Label(new Rect(rect.x + 14f, rect.y + 34f, rect.width - 28f, 18f), "RimChat_AirdropTradeCard_TitleHint".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawSearchArea(Rect rect)
        {
            DrawPanel(rect, new Color(0.09f, 0.09f, 0.12f, 0.98f));
            Rect labelRect = new Rect(rect.x + 12f, rect.y + 10f, 90f, 22f);
            Widgets.Label(labelRect, "RimChat_AirdropTradeCard_NeedLabel".Translate());

            Rect inputRect = new Rect(rect.x + 104f, rect.y + 8f, rect.width - 116f, 30f);
            Widgets.DrawBoxSolid(inputRect, new Color(0.15f, 0.15f, 0.19f));
            string newText = Widgets.TextField(inputRect, needSearchText ?? string.Empty);
            if (!string.Equals(newText, needSearchText, StringComparison.Ordinal))
            {
                needSearchText = newText;
                if (!searchState.IsSearchTextStillMatchingBinding(needSearchText))
                {
                    ClearNeedBinding();
                }

                if (string.IsNullOrWhiteSpace(needSearchText))
                {
                    showInlineSuggestions = false;
                    searchState.ClearSuggestions();
                }
                else
                {
                    searchState.ComputeSuggestions(needSearchText);
                    showInlineSuggestions = searchState.Suggestions.Count > 0;
                }
            }

            Rect statusRect = new Rect(rect.x + 12f, rect.y + 46f, rect.width - 24f, 28f);
            DrawNeedBindingStatus(statusRect);
        }

        private void DrawNeedBindingStatus(Rect rect)
        {
            if (boundNeedRecord?.Def == null)
            {
                GUI.color = new Color(0.88f, 0.72f, 0.3f);
                Widgets.Label(rect, "RimChat_AirdropTradeCard_NeedBindingMissing".Translate());
                GUI.color = Color.white;
                return;
            }

            GUI.color = new Color(0.62f, 0.85f, 0.62f);
            string text = "RimChat_AirdropTradeCard_NeedBindingReady".Translate(boundNeedRecord.Label, boundNeedRecord.DefName).ToString();
            Widgets.Label(rect, text);
            GUI.color = Color.white;
        }

        private void DrawInlineSuggestionDropDown(Rect rect)
        {
            DrawPanel(rect, new Color(0.1f, 0.1f, 0.14f, 0.98f));
            float rowY = rect.y + 4f;
            for (int i = 0; i < searchState.Suggestions.Count && i < 6; i++)
            {
                ThingDefRecord record = searchState.Suggestions[i];
                Rect rowRect = new Rect(rect.x + 4f, rowY, rect.width - 8f, SuggestionRowHeight - 3f);
                bool hovered = Mouse.IsOver(rowRect);
                Widgets.DrawBoxSolid(rowRect, hovered ? new Color(0.25f, 0.37f, 0.55f, 0.82f) : new Color(0.12f, 0.12f, 0.16f, 0.76f));
                if (Widgets.ButtonInvisible(rowRect))
                {
                    BindNeedRecord(record);
                }

                Text.Font = GameFont.Tiny;
                GUI.color = hovered ? Color.white : new Color(0.88f, 0.9f, 0.94f);
                Widgets.Label(new Rect(rowRect.x + 8f, rowRect.y + 3f, rowRect.width - 16f, 14f), record.Label);
                GUI.color = new Color(0.62f, 0.68f, 0.8f);
                Widgets.Label(new Rect(rowRect.x + 8f, rowRect.y + 17f, rowRect.width - 16f, 12f), record.DefName);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                rowY += SuggestionRowHeight;
            }
        }

        private void DrawItemCards(Rect rect)
        {
            float halfWidth = (rect.width - Padding) * 0.5f;
            DrawNeedItemCard(new Rect(rect.x, rect.y, halfWidth, rect.height));
            DrawOfferItemCard(new Rect(rect.x + halfWidth + Padding, rect.y, halfWidth, rect.height));
        }

        private void DrawNeedItemCard(Rect rect)
        {
            DrawPanel(rect, new Color(0.07f, 0.09f, 0.11f, 0.98f));
            DrawCardHeader(rect, "RimChat_AirdropTradeCard_NeedItemCard");
            if (boundNeedRecord?.Def == null)
            {
                DrawEmptyCard(rect, "RimChat_AirdropTradeCard_NoNeedItemBound");
                return;
            }

            DrawThingDefCardContent(rect, boundNeedRecord, Math.Max(1, ParsePositiveInt(requestedCountText, 1)), boundNeedRecord.MarketValue, ComputeNeedReferenceTotal());
        }

        private void DrawOfferItemCard(Rect rect)
        {
            DrawPanel(rect, new Color(0.07f, 0.09f, 0.11f, 0.98f));
            DrawCardHeader(rect, "RimChat_AirdropTradeCard_OfferItemCard");
            ThingDef offerDef = DefDatabase<ThingDef>.GetNamedSilentFail(selectedOfferDefName);
            if (offerDef == null)
            {
                DrawEmptyCard(rect, "RimChat_AirdropTradeCard_NoOfferItem");
                return;
            }

            ThingDefRecord record = ThingDefRecord.From(offerDef);
            DrawThingDefCardContent(rect, record, Math.Max(1, ParsePositiveInt(offerCountText, 1)), selectedOfferUnitPrice, ComputeOfferTotal());
        }

        private void DrawCardHeader(Rect rect, string key)
        {
            GUI.color = new Color(0.25f, 0.29f, 0.35f, 0.95f);
            Widgets.DrawBox(new Rect(rect.x, rect.y, rect.width, rect.height));
            GUI.color = Color.white;
            Rect headerRect = new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 20f);
            Widgets.Label(headerRect, key.Translate());
        }

        private void DrawEmptyCard(Rect rect, string key)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.64f, 0.66f, 0.72f, 0.92f);
            Widgets.Label(new Rect(rect.x + 12f, rect.y + 44f, rect.width - 24f, 36f), key.Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawThingDefCardContent(Rect rect, ThingDefRecord record, int count, float unitPrice, float totalPrice)
        {
            float contentY = rect.y + 40f;
            Rect iconRect = new Rect(rect.x + 12f, contentY, CardImageSize, CardImageSize);
            if (record.Def.uiIcon != null)
            {
                GUI.DrawTexture(iconRect, record.Def.uiIcon);
            }
            else
            {
                Widgets.DrawBoxSolid(iconRect, new Color(0.15f, 0.15f, 0.18f));
            }

            GUI.color = new Color(0.27f, 0.31f, 0.38f, 0.95f);
            Widgets.DrawBox(iconRect);
            GUI.color = Color.white;

            float textX = iconRect.xMax + 10f;
            float textWidth = rect.width - (textX - rect.x) - 12f;
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.93f, 0.94f, 0.98f);
            Widgets.Label(new Rect(textX, contentY, textWidth, 20f), record.Label);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.6f, 0.66f, 0.78f);
            Widgets.Label(new Rect(textX, contentY + 20f, textWidth, 16f), record.DefName);
            GUI.color = new Color(0.84f, 0.86f, 0.92f);
            Widgets.Label(new Rect(textX, contentY + 38f, textWidth * 0.52f, 14f), "RimChat_Price".Translate() + ": " + unitPrice.ToString("F1", CultureInfo.InvariantCulture));
            Widgets.Label(new Rect(textX + textWidth * 0.52f, contentY + 38f, textWidth * 0.48f, 14f), "RimChat_StackLimit".Translate() + ": " + record.StackLimit);
            GUI.color = new Color(0.78f, 0.83f, 0.9f);
            Widgets.Label(new Rect(textX, contentY + 56f, textWidth * 0.52f, 14f), "RimChat_AirdropTradeCard_CountLabel".Translate() + ": " + count);
            GUI.color = new Color(0.94f, 0.8f, 0.42f);
            Widgets.Label(new Rect(textX + textWidth * 0.52f, contentY + 56f, textWidth * 0.48f, 14f), "RimChat_AirdropTradeCard_TotalPriceLabel".Translate() + ": " + totalPrice.ToString("F1", CultureInfo.InvariantCulture));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawInventoryPanel(Rect rect)
        {
            DrawPanel(rect, new Color(0.08f, 0.08f, 0.11f, 0.98f));
            DrawInventorySearchBar(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 24f));
            GUI.color = new Color(0.74f, 0.8f, 0.9f, 0.95f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 38f, rect.width - 20f, 16f), ResolveSelectedOfferLabel());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Rect listRect = new Rect(rect.x + 4f, rect.y + 58f, rect.width - 8f, rect.height - 62f);
            if (isLoadingInventory)
            {
                DrawLoadingIndicator(listRect);
                return;
            }

            if (filteredInventoryItems.Count == 0)
            {
                string emptyKey = inventoryItems.Count == 0 ? "RimChat_AirdropTradeCard_NoInventory" : "RimChat_AirdropTradeCard_NoSuggestions";
                Widgets.Label(new Rect(listRect.x + 8f, listRect.y + 8f, listRect.width - 16f, 24f), emptyKey.Translate());
                return;
            }

            float contentHeight = Math.Max(1f, filteredInventoryItems.Count * InventoryRowHeight);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, contentHeight);
            inventoryScrollPos = GUI.BeginScrollView(listRect, inventoryScrollPos, viewRect);
            float rowY = 0f;
            foreach (InventoryDisplayEntry entry in filteredInventoryItems)
            {
                DrawInventoryRow(entry, viewRect.width, rowY);
                rowY += InventoryRowHeight;
            }

            GUI.EndScrollView();
        }

        private void DrawInventorySearchBar(Rect rect)
        {
            Widgets.Label(new Rect(rect.x, rect.y, 60f, 22f), "RimChat_Search".Translate());
            Rect inputRect = new Rect(rect.x + 60f, rect.y, rect.width - 60f, 22f);
            Widgets.DrawBoxSolid(inputRect, new Color(0.15f, 0.15f, 0.19f));
            string newText = Widgets.TextField(inputRect, inventorySearchText ?? string.Empty);
            if (!string.Equals(newText, inventorySearchText, StringComparison.Ordinal))
            {
                inventorySearchText = newText;
                ApplyInventoryFilter();
                EnsureDefaultOfferSelection();
            }
        }

        private void DrawLoadingIndicator(Rect rect)
        {
            float barWidth = rect.width * 0.58f;
            Rect progressRect = new Rect(rect.x + (rect.width - barWidth) * 0.5f, rect.y + rect.height * 0.42f, barWidth, 8f);
            Widgets.DrawBoxSolid(progressRect, new Color(0.2f, 0.2f, 0.24f));
            Widgets.DrawBoxSolid(new Rect(progressRect.x, progressRect.y, progressRect.width * inventoryLoadProgress, progressRect.height), new Color(0.38f, 0.58f, 0.84f, 0.85f));
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.77f, 0.82f);
            Widgets.Label(new Rect(rect.x, progressRect.yMax + 8f, rect.width, 18f), "RimChat_Loading".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawInventoryRow(InventoryDisplayEntry entry, float width, float y)
        {
            Rect rowRect = new Rect(2f, y, width - 4f, InventoryRowHeight - 2f);
            bool selected = string.Equals(selectedOfferDefName, entry.DefName, StringComparison.OrdinalIgnoreCase);
            Widgets.DrawBoxSolid(rowRect, selected ? new Color(0.19f, 0.39f, 0.63f, 0.82f) : new Color(0.12f, 0.12f, 0.16f, 0.82f));
            if (selected)
            {
                GUI.color = new Color(0.46f, 0.62f, 0.92f, 0.95f);
                Widgets.DrawBox(rowRect);
                GUI.color = Color.white;
            }

            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(entry.DefName);
            Rect iconRect = new Rect(rowRect.x + 6f, rowRect.y + 10f, 20f, 20f);
            if (def?.uiIcon != null)
            {
                GUI.DrawTexture(iconRect, def.uiIcon);
            }

            float textX = iconRect.xMax + 8f;
            float textWidth = rowRect.width - (textX - rowRect.x) - 8f;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(textX, rowRect.y + 5f, textWidth * 0.62f, 16f), $"{entry.Label} ({entry.DefName})");
            GUI.color = new Color(0.72f, 0.78f, 0.9f);
            Widgets.Label(new Rect(textX, rowRect.y + 24f, textWidth * 0.32f, 16f), "x" + entry.Count.ToString(CultureInfo.InvariantCulture));
            GUI.color = new Color(0.94f, 0.8f, 0.42f);
            Widgets.Label(new Rect(textX + textWidth * 0.62f, rowRect.y + 24f, textWidth * 0.38f, 16f), "@" + entry.UnitPrice.ToString("F0", CultureInfo.InvariantCulture));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            if (Widgets.ButtonInvisible(rowRect))
            {
                ApplyOfferSelection(entry);
            }
        }

        private void DrawFooter(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.13f));
            Rect statRect = new Rect(rect.x + 12f, rect.y + 10f, rect.width * 0.58f, 42f);
            DrawReferencePriceBlock(statRect);

            float inputWidth = rect.width * 0.55f;
            DrawFooterInputs(new Rect(rect.x + 12f, rect.y + 56f, inputWidth, 28f));

            string failReason = GetSubmitDisabledReason();
            if (!string.IsNullOrWhiteSpace(failReason))
            {
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.9f, 0.74f, 0.32f);
                Widgets.Label(new Rect(rect.x + 12f, rect.y + 88f, rect.width * 0.62f, 18f), failReason);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }

            float buttonWidth = 160f;
            Rect cancelRect = new Rect(rect.xMax - buttonWidth - 12f, rect.yMax - 40f, buttonWidth, 32f);
            Rect submitRect = new Rect(cancelRect.x - buttonWidth - 10f, cancelRect.y, buttonWidth, 32f);
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

        private void DrawReferencePriceBlock(Rect rect)
        {
            DrawPanel(rect, new Color(0.11f, 0.11f, 0.15f, 0.96f));
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.74f, 0.78f, 0.88f);
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 6f, 120f, 14f), "RimChat_AirdropTradeCard_ReferencePriceLabel".Translate());
            Text.Font = GameFont.Small;
            GUI.color = boundNeedRecord?.Def == null ? new Color(0.64f, 0.66f, 0.72f) : new Color(0.96f, 0.82f, 0.4f);
            string value = boundNeedRecord?.Def == null
                ? "RimChat_AirdropTradeCard_ReferencePriceEmpty".Translate().ToString()
                : ComputeNeedReferenceTotal().ToString("F1", CultureInfo.InvariantCulture);
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 18f, rect.width - 20f, 20f), value);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawFooterInputs(Rect rect)
        {
            float groupWidth = (rect.width - 12f) * 0.5f;
            DrawIntegerField(new Rect(rect.x, rect.y, groupWidth, rect.height), "RimChat_AirdropTradeCard_RequestCountLabel".Translate().ToString(), requestedCountText, out requestedCountText, 1, 5000);
            DrawIntegerField(new Rect(rect.x + groupWidth + 12f, rect.y, groupWidth, rect.height), "RimChat_AirdropTradeCard_OfferCountLabel".Translate().ToString(), offerCountText, out offerCountText, 1, 1000000);
        }

        private static void DrawIntegerField(Rect rect, string label, string current, out string updated, int min, int max)
        {
            updated = current;
            float labelWidth = Mathf.Min(104f, rect.width * 0.42f);
            Widgets.Label(new Rect(rect.x, rect.y + 2f, labelWidth, 22f), label);
            Rect fieldRect = new Rect(rect.x + labelWidth + 4f, rect.y, rect.width - labelWidth - 4f, 24f);
            Widgets.DrawBoxSolid(fieldRect, new Color(0.17f, 0.17f, 0.21f));
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
            return string.IsNullOrWhiteSpace(GetSubmitDisabledReason());
        }

        private string GetSubmitDisabledReason()
        {
            if (boundNeedRecord?.Def == null || string.IsNullOrWhiteSpace(boundNeedRecord.DefName))
            {
                return "RimChat_AirdropTradeCard_SubmitDisabledNeed".Translate().ToString();
            }

            if (string.IsNullOrWhiteSpace(selectedOfferDefName))
            {
                return "RimChat_AirdropTradeCard_SubmitDisabledOffer".Translate().ToString();
            }

            if (!int.TryParse(requestedCountText, out int requestedCount) || requestedCount <= 0)
            {
                return "RimChat_AirdropTradeCard_SubmitDisabledRequestCount".Translate().ToString();
            }

            if (!int.TryParse(offerCountText, out int offerCount) || offerCount <= 0)
            {
                return "RimChat_AirdropTradeCard_SubmitDisabledOfferCount".Translate().ToString();
            }

            return string.Empty;
        }

        private void Submit()
        {
            if (!CanSubmit())
            {
                return;
            }

            int requestedCount = ParsePositiveInt(requestedCountText, 1);
            int offerCount = ParsePositiveInt(offerCountText, 1);
            var payload = new ItemAirdropTradeCardPayload
            {
                Need = boundNeedRecord.Label,
                RequestedCount = requestedCount,
                OfferItemDefName = selectedOfferDefName,
                OfferItemLabel = selectedOfferLabel,
                OfferItemCount = offerCount,
                Scenario = "trade",
                NeedDefName = boundNeedRecord.DefName,
                NeedLabel = boundNeedRecord.Label,
                NeedSearchText = boundNeedRecord.SearchText,
                NeedUnitPrice = boundNeedRecord.MarketValue,
                NeedReferenceTotalPrice = ComputeNeedReferenceTotal(),
                OfferUnitPrice = selectedOfferUnitPrice,
                OfferTotalPrice = ComputeOfferTotal()
            };

            onSubmitted?.Invoke(payload);
            Close();
        }

        private void BindNeedRecord(ThingDefRecord record)
        {
            if (record?.Def == null)
            {
                return;
            }

            boundNeedRecord = record;
            searchState.TryBindToRecord(record);
            needSearchText = record.Label;
            showInlineSuggestions = false;
        }

        private void ClearNeedBinding()
        {
            boundNeedRecord = null;
            searchState.ClearBinding();
        }

        private void ApplyOfferSelection(InventoryDisplayEntry entry)
        {
            selectedOfferDefName = entry.DefName;
            selectedOfferLabel = entry.Label;
            selectedOfferStackLimit = entry.StackLimit;
            selectedOfferUnitPrice = entry.UnitPrice;
        }

        private string ResolveSelectedOfferLabel()
        {
            return string.IsNullOrWhiteSpace(selectedOfferLabel)
                ? "RimChat_AirdropTradeCard_NoOfferItem".Translate().ToString()
                : "RimChat_AirdropTradeCard_SelectedOfferItem".Translate(selectedOfferLabel).ToString();
        }

        private float ComputeNeedReferenceTotal()
        {
            if (boundNeedRecord?.Def == null)
            {
                return 0f;
            }

            return Math.Max(0f, boundNeedRecord.MarketValue * ParsePositiveInt(requestedCountText, 1));
        }

        private float ComputeOfferTotal()
        {
            return Math.Max(0f, selectedOfferUnitPrice * ParsePositiveInt(offerCountText, 1));
        }

        private static int ParsePositiveInt(string value, int fallback)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
                ? parsed
                : fallback;
        }

        private static void DrawPanel(Rect rect, Color fill)
        {
            Widgets.DrawBoxSolid(rect, fill);
            GUI.color = new Color(0.24f, 0.27f, 0.34f, 0.95f);
            Widgets.DrawBox(rect);
            GUI.color = Color.white;
        }

        private sealed class InventoryDisplayEntry
        {
            public string DefName { get; set; }
            public string Label { get; set; }
            public int Count { get; set; }
            public float UnitPrice { get; set; }
            public int StackLimit { get; set; }
        }
    }
}
