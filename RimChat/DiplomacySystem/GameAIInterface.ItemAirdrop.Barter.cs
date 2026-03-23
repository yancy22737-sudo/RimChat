using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimChat.Config;
using RimChat.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: GameAIInterface item-airdrop core, ThingDefCatalog, Building_OrbitalTradeBeacon.
    /// Responsibility: prepare/commit barter-based airdrop trades with strict beacon-source validation.
    /// </summary>
    public partial class GameAIInterface
    {
        private const float ItemAirdropMaxOverpayPercent = 0.05f;

        public APIResult PrepareItemAirdropTrade(Faction faction, Dictionary<string, object> parameters, Pawn playerNegotiator)
        {
            if (playerNegotiator == null || playerNegotiator.Map == null)
            {
                return FailFastAirdrop(
                    "player_negotiator_required",
                    "Preparing a barter airdrop requires a valid player negotiator on a map.",
                    faction,
                    parameters);
            }

            return PrepareItemAirdropTradeForMap(faction, parameters, playerNegotiator.Map, true);
        }

        public APIResult CommitPreparedItemAirdropTrade(Faction faction, ItemAirdropPreparedTradeData preparedData)
        {
            if (faction == null)
            {
                return APIResult.FailureResult("Faction cannot be null.");
            }

            if (preparedData == null)
            {
                return APIResult.FailureResult("[prepared_trade_missing] Missing prepared airdrop trade payload.");
            }

            Map map = Find.Maps?.FirstOrDefault(m => m != null && m.uniqueID == preparedData.MapUniqueId);
            if (map == null)
            {
                return FailFastAirdrop(
                    "map_unavailable",
                    "Prepared airdrop map is no longer available.",
                    faction,
                    preparedData.ParametersSnapshot);
            }

            ThingDefRecord selectedRecord = ThingDefCatalog.GetRecords()
                .FirstOrDefault(record =>
                    record?.Def != null &&
                    string.Equals(record.DefName, preparedData.SelectedDefName, StringComparison.OrdinalIgnoreCase));
            if (selectedRecord == null)
            {
                return FailFastAirdrop(
                    "selected_def_unresolved",
                    $"Selected def '{preparedData.SelectedDefName}' could not be resolved during commit.",
                    faction,
                    preparedData.ParametersSnapshot);
            }

            if (!TryFindAirdropCell(map, out IntVec3 dropCell))
            {
                return FailFastAirdrop(
                    "dropcell_not_found",
                    "No legal drop cell found near colony center.",
                    faction,
                    preparedData.ParametersSnapshot);
            }

            int maxStacks = RimChatMod.Instance?.InstanceSettings?.ItemAirdropMaxStacksPerDrop ?? 8;
            List<Thing> stacks = BuildStacks(selectedRecord.Def, preparedData.Quantity, maxStacks);
            if (stacks.Count == 0)
            {
                return FailFastAirdrop(
                    "stack_build_failed",
                    "Could not create item stacks for airdrop.",
                    faction,
                    preparedData.ParametersSnapshot);
            }

            APIResult validation = ValidateDeductionPlan(map, preparedData.DeductionPlan, out List<ThingDeductionReservation> reservations);
            if (!validation.Success)
            {
                return FailFastAirdrop(
                    (validation.Data as ItemAirdropResultData)?.FailureCode ?? "payment_item_insufficient",
                    validation.Message,
                    faction,
                    preparedData.ParametersSnapshot);
            }

            ApplyDeductionReservations(reservations);

            DropPodUtility.DropThingsNear(
                dropCell,
                map,
                stacks,
                110,
                canInstaDropDuringInit: false,
                leaveSlag: false,
                canRoofPunch: false);

            int deliveredCount = stacks.Sum(t => t.stackCount);
            string stageText = $"def={selectedRecord.DefName},count={deliveredCount},budget={preparedData.BudgetSilver},reason={preparedData.SelectionReason},drop={dropCell},payment={preparedData.PaymentTotalSilver}";
            RecordStageAudit("execute", faction, preparedData.ParametersSnapshot, stageText);
            RecordAPICall("RequestItemAirdrop", true, stageText);

            string playerTitle = "RimChat_ItemAirdropArrivedTitle".Translate();
            string playerBody = "RimChat_ItemAirdropArrivedBody".Translate(
                faction.Name,
                selectedRecord.Label.CapitalizeFirst(),
                deliveredCount,
                preparedData.BudgetSilver);
            Find.LetterStack.ReceiveLetter(playerTitle, playerBody, LetterDefOf.PositiveEvent, new TargetInfo(dropCell, map), faction);

            var payload = new ItemAirdropResultData
            {
                SelectedDefName = selectedRecord.DefName,
                ResolvedLabel = selectedRecord.Label,
                BudgetUsed = preparedData.BudgetSilver,
                Quantity = deliveredCount,
                DropCell = dropCell.ToString(),
                FailureCode = string.Empty
            };

            return APIResult.SuccessResult(
                $"Airdrop delivered: {selectedRecord.DefName} x{deliveredCount} (budget {preparedData.BudgetSilver})",
                payload);
        }

        private APIResult PrepareItemAirdropTradeForMap(
            Faction faction,
            Dictionary<string, object> parameters,
            Map map,
            bool requirePlayerHome)
        {
            if (RimChatMod.Instance?.InstanceSettings == null)
            {
                return APIResult.FailureResult("Settings not initialized");
            }

            RimChatSettings settings = RimChatMod.Instance.InstanceSettings;
            if (!settings.EnableAIItemAirdrop)
            {
                return APIResult.FailureResult("request_item_airdrop is disabled in settings.");
            }

            if (faction == null)
            {
                return APIResult.FailureResult("Faction cannot be null");
            }

            if (parameters == null)
            {
                return APIResult.FailureResult("request_item_airdrop requires parameters.");
            }

            if (map == null)
            {
                return FailFastAirdrop("no_home_map", "No player map available for item airdrop.", faction, parameters);
            }

            if (requirePlayerHome && !map.IsPlayerHome)
            {
                return FailFastAirdrop("map_not_player_home", "Barter airdrop requires a player home map context.", faction, parameters);
            }

            string need = ReadString(parameters, "need");
            if (string.IsNullOrWhiteSpace(need))
            {
                return FailFastAirdrop("missing_need", "request_item_airdrop requires parameter 'need'.", faction, parameters);
            }

            APIResult budgetResult = ResolveStrictBudget(parameters, settings, out int budget);
            if (!budgetResult.Success)
            {
                return FailFastAirdrop(
                    (budgetResult.Data as ItemAirdropResultData)?.FailureCode ?? "budget_required",
                    budgetResult.Message,
                    faction,
                    parameters);
            }

            string scenario = NormalizeScenario(ReadString(parameters, "scenario"));
            string constraints = ReadString(parameters, "constraints");

            ItemAirdropIntent intent = ItemAirdropIntent.Create(need, constraints, scenario);
            ItemAirdropCandidatePack candidatePack = PrepareItemAirdropCandidates(intent, budget, settings);
            List<string> localAliases = new List<string>();
            List<string> aliases = new List<string>();
            if (candidatePack.Candidates.Count == 0)
            {
                localAliases = ThingDefResolver.ExpandLocalAliases(intent);
                if (localAliases.Count > 0)
                {
                    intent = ItemAirdropIntent.Create(need, constraints, scenario, localAliases);
                    candidatePack = PrepareItemAirdropCandidates(intent, budget, settings);
                }
            }

            if (candidatePack.Candidates.Count == 0)
            {
                aliases = ExpandNeedAliasesWithAi(need, constraints, settings);
                if (aliases.Count > 0)
                {
                    intent = ItemAirdropIntent.Create(need, constraints, scenario, aliases);
                    candidatePack = PrepareItemAirdropCandidates(intent, budget, settings);
                }
            }

            string prepareSummary = BuildPrepareAuditSummary(intent, budget, candidatePack, localAliases, aliases);
            RecordStageAudit("prepare", faction, parameters, prepareSummary);
            if (candidatePack.Candidates.Count == 0)
            {
                if (intent.Family == ItemAirdropNeedFamily.Unknown)
                {
                    return FailFastAirdrop(
                        "need_family_unknown",
                        "Could not classify request need. Try adding multiple CN/EN aliases in need/constraints.",
                        faction,
                        parameters,
                        prepareSummary);
                }

                return FailFastAirdrop(
                    "no_candidates",
                    "No legal airdrop candidates were produced for this request.",
                    faction,
                    parameters,
                    prepareSummary);
            }

            APIResult selectionResult = ExecuteItemAirdropSelection(intent, candidatePack, budget, settings);
            if (!selectionResult.Success || !(selectionResult.Data is ItemAirdropSelection selection))
            {
                string code = (selectionResult.Data as ItemAirdropResultData)?.FailureCode ?? "selection_failed";
                return FailFastAirdrop(code, selectionResult.Message, faction, parameters);
            }

            APIResult validationResult = ValidateAirdropSelection(selection, candidatePack, budget, settings, out ThingDefRecord selectedRecord, out int validatedCount);
            if (!validationResult.Success)
            {
                return FailFastAirdrop(
                    (validationResult.Data as ItemAirdropResultData)?.FailureCode ?? "selection_invalid",
                    validationResult.Message,
                    faction,
                    parameters);
            }

            APIResult paymentPlanResult = BuildPaymentPlan(parameters, map, budget, out List<ItemAirdropPreparedPaymentLine> paymentLines, out List<ItemAirdropDeductionPlanLine> deductionPlan, out int paymentTotalSilver);
            if (!paymentPlanResult.Success)
            {
                return FailFastAirdrop(
                    (paymentPlanResult.Data as ItemAirdropResultData)?.FailureCode ?? "payment_plan_failed",
                    paymentPlanResult.Message,
                    faction,
                    parameters);
            }

            int overpay = Math.Max(0, paymentTotalSilver - budget);
            string paymentSummary = $"budget={budget},payment={paymentTotalSilver},overpay={overpay},paymentLines={paymentLines.Count},deductionRows={deductionPlan.Count}";
            RecordStageAudit("prepare_trade", faction, parameters, paymentSummary);

            var prepared = new ItemAirdropPreparedTradeData
            {
                SelectedDefName = selectedRecord.DefName,
                ResolvedLabel = selectedRecord.Label,
                Quantity = validatedCount,
                BudgetSilver = budget,
                PaymentTotalSilver = paymentTotalSilver,
                PaymentOverpaySilver = overpay,
                MapUniqueId = map.uniqueID,
                NeedText = need,
                Scenario = scenario,
                SelectionReason = selection.Reason ?? string.Empty,
                PaymentLines = paymentLines,
                DeductionPlan = deductionPlan,
                ParametersSnapshot = CloneParameterDictionary(parameters)
            };

            return APIResult.SuccessResult("Airdrop trade prepared.", prepared);
        }

        private static APIResult ResolveStrictBudget(Dictionary<string, object> parameters, RimChatSettings settings, out int budget)
        {
            budget = 0;
            if (!TryReadIntParameter(parameters, "budget_silver", out int directBudget))
            {
                return new APIResult
                {
                    Success = false,
                    Message = "request_item_airdrop requires parameter 'budget_silver' in barter mode.",
                    Data = new ItemAirdropResultData { FailureCode = "budget_required" }
                };
            }

            if (directBudget <= 0)
            {
                return new APIResult
                {
                    Success = false,
                    Message = "budget_silver must be greater than 0.",
                    Data = new ItemAirdropResultData { FailureCode = "budget_invalid" }
                };
            }

            budget = Mathf.Clamp(directBudget, settings.ItemAirdropMinBudgetSilver, settings.ItemAirdropMaxBudgetSilver);
            return APIResult.SuccessResult("Budget resolved.");
        }

        private APIResult BuildPaymentPlan(
            Dictionary<string, object> parameters,
            Map map,
            int budgetSilver,
            out List<ItemAirdropPreparedPaymentLine> paymentLines,
            out List<ItemAirdropDeductionPlanLine> deductionPlan,
            out int paymentTotalSilver)
        {
            paymentLines = new List<ItemAirdropPreparedPaymentLine>();
            deductionPlan = new List<ItemAirdropDeductionPlanLine>();
            paymentTotalSilver = 0;

            APIResult parseResult = ParsePaymentItems(parameters, out List<ItemAirdropPaymentRequestLine> requestedLines);
            if (!parseResult.Success)
            {
                return parseResult;
            }

            List<Thing> beaconThings = CollectBeaconTradeableThings(map);
            if (beaconThings.Count == 0)
            {
                return BuildPaymentFailure("beacon_source_unavailable", "No powered orbital-trade-beacon source items are available on this map.");
            }

            var buckets = new Dictionary<string, List<Thing>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < beaconThings.Count; i++)
            {
                Thing thing = beaconThings[i];
                string defName = thing?.def?.defName ?? string.Empty;
                if (string.IsNullOrWhiteSpace(defName))
                {
                    continue;
                }

                if (!buckets.TryGetValue(defName, out List<Thing> bucket))
                {
                    bucket = new List<Thing>();
                    buckets[defName] = bucket;
                }

                bucket.Add(thing);
            }

            float totalValueFloat = 0f;
            for (int i = 0; i < requestedLines.Count; i++)
            {
                ItemAirdropPaymentRequestLine line = requestedLines[i];
                APIResult resolveResult = TryResolvePaymentThingDef(line.ItemText, out ThingDefRecord resolvedRecord);
                if (!resolveResult.Success)
                {
                    return resolveResult;
                }

                if (!buckets.TryGetValue(resolvedRecord.DefName, out List<Thing> stockThings))
                {
                    return BuildPaymentFailure(
                        "payment_item_insufficient",
                        $"No tradable beacon stock found for payment item '{line.ItemText}' ({resolvedRecord.DefName}).");
                }

                int availableCount = stockThings.Sum(thing => Math.Max(0, thing.stackCount));
                if (availableCount < line.Count)
                {
                    return BuildPaymentFailure(
                        "payment_item_insufficient",
                        $"Insufficient stock for '{resolvedRecord.DefName}'. required={line.Count}, available={availableCount}.");
                }

                float unitPrice = Math.Max(0.01f, resolvedRecord.MarketValue);
                float subtotal = unitPrice * line.Count;
                totalValueFloat += subtotal;
                paymentLines.Add(new ItemAirdropPreparedPaymentLine
                {
                    RequestedItem = line.ItemText,
                    DefName = resolvedRecord.DefName,
                    Label = resolvedRecord.Label,
                    Count = line.Count,
                    UnitMarketValue = unitPrice,
                    SubtotalMarketValue = subtotal
                });

                int remaining = line.Count;
                foreach (Thing thing in stockThings.OrderByDescending(item => item.stackCount))
                {
                    if (remaining <= 0)
                    {
                        break;
                    }

                    int taken = Math.Min(remaining, Math.Max(0, thing.stackCount));
                    if (taken <= 0)
                    {
                        continue;
                    }

                    deductionPlan.Add(new ItemAirdropDeductionPlanLine
                    {
                        ThingId = thing.ThingID,
                        DefName = resolvedRecord.DefName,
                        Count = taken
                    });
                    remaining -= taken;
                }
            }

            if (totalValueFloat + 0.01f < budgetSilver)
            {
                return BuildPaymentFailure(
                    "payment_item_insufficient",
                    $"Payment total value {totalValueFloat:F1} is below required budget {budgetSilver}.");
            }

            float maxAllowedValue = budgetSilver * (1f + ItemAirdropMaxOverpayPercent);
            if (totalValueFloat > maxAllowedValue + 0.01f)
            {
                return BuildPaymentFailure(
                    "payment_overpay_too_high",
                    $"Payment total value {totalValueFloat:F1} exceeds allowed maximum {maxAllowedValue:F1} for budget {budgetSilver}.");
            }

            paymentTotalSilver = Mathf.RoundToInt(totalValueFloat);
            return APIResult.SuccessResult("Payment plan prepared.");
        }

        private APIResult ParsePaymentItems(Dictionary<string, object> parameters, out List<ItemAirdropPaymentRequestLine> lines)
        {
            lines = new List<ItemAirdropPaymentRequestLine>();
            if (parameters == null ||
                !parameters.TryGetValue("payment_items", out object rawItems) ||
                rawItems == null)
            {
                return BuildPaymentFailure("payment_items_missing", "request_item_airdrop requires parameter 'payment_items'.");
            }

            IEnumerable<object> entries = rawItems as IEnumerable<object>;
            if (entries == null)
            {
                return BuildPaymentFailure("payment_items_invalid", "payment_items must be a JSON array.");
            }

            int index = 0;
            foreach (object entry in entries)
            {
                index++;
                if (!(entry is Dictionary<string, object> itemData))
                {
                    return BuildPaymentFailure("payment_items_invalid", $"payment_items[{index}] must be an object.");
                }

                string itemText = ReadDictionaryText(itemData, "item");
                if (string.IsNullOrWhiteSpace(itemText))
                {
                    return BuildPaymentFailure("payment_items_invalid", $"payment_items[{index}] requires non-empty field 'item'.");
                }

                if (!TryReadDictionaryPositiveInt(itemData, "count", out int count))
                {
                    return BuildPaymentFailure("payment_items_invalid", $"payment_items[{index}] requires positive integer field 'count'.");
                }

                lines.Add(new ItemAirdropPaymentRequestLine
                {
                    ItemText = itemText,
                    Count = count
                });
            }

            if (lines.Count == 0)
            {
                return BuildPaymentFailure("payment_items_missing", "payment_items must include at least one item.");
            }

            return APIResult.SuccessResult("Payment items parsed.");
        }

        private static string ReadDictionaryText(Dictionary<string, object> values, string key)
        {
            if (values == null || !values.TryGetValue(key, out object raw) || raw == null)
            {
                return string.Empty;
            }

            return raw.ToString()?.Trim() ?? string.Empty;
        }

        private static bool TryReadDictionaryPositiveInt(Dictionary<string, object> values, string key, out int count)
        {
            count = 0;
            if (values == null || !values.TryGetValue(key, out object raw) || raw == null)
            {
                return false;
            }

            if (raw is int intValue)
            {
                count = intValue;
                return count > 0;
            }

            if (raw is long longValue && longValue <= int.MaxValue && longValue >= int.MinValue)
            {
                count = (int)longValue;
                return count > 0;
            }

            return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out count) && count > 0;
        }

        private APIResult TryResolvePaymentThingDef(string itemText, out ThingDefRecord resolvedRecord)
        {
            resolvedRecord = null;
            string query = (itemText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                return BuildPaymentFailure("payment_item_unresolved", "Payment item text cannot be empty.");
            }

            List<ThingDefRecord> records = ThingDefCatalog.GetRecords()
                .Where(record => record?.Def != null && TradeUtility.EverPlayerSellable(record.Def))
                .ToList();

            List<ThingDefRecord> exactDefMatches = records
                .Where(record => string.Equals(record.DefName, query, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (exactDefMatches.Count == 1)
            {
                resolvedRecord = exactDefMatches[0];
                return APIResult.SuccessResult("Payment def resolved.");
            }

            if (exactDefMatches.Count > 1)
            {
                return BuildPaymentFailure("payment_item_ambiguous", $"Payment item '{query}' matched multiple defs by defName.");
            }

            List<ThingDefRecord> exactLabelMatches = records
                .Where(record => string.Equals(record.Label, query, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (exactLabelMatches.Count == 1)
            {
                resolvedRecord = exactLabelMatches[0];
                return APIResult.SuccessResult("Payment def resolved.");
            }

            if (exactLabelMatches.Count > 1)
            {
                return BuildPaymentFailure("payment_item_ambiguous", $"Payment item '{query}' matched multiple defs by label.");
            }

            string token = query.ToLowerInvariant();
            List<(ThingDefRecord Record, int Score)> fuzzy = records
                .Select(record => (Record: record, Score: ScorePaymentRecord(record, token)))
                .Where(tuple => tuple.Score > 0)
                .OrderByDescending(tuple => tuple.Score)
                .ThenByDescending(tuple => tuple.Record.MarketValue)
                .ToList();
            if (fuzzy.Count == 0)
            {
                return BuildPaymentFailure("payment_item_unresolved", $"Payment item '{query}' could not be resolved.");
            }

            int topScore = fuzzy[0].Score;
            if (fuzzy.Count(tuple => tuple.Score == topScore) > 1)
            {
                return BuildPaymentFailure("payment_item_ambiguous", $"Payment item '{query}' is ambiguous across multiple ThingDefs.");
            }

            resolvedRecord = fuzzy[0].Record;
            return APIResult.SuccessResult("Payment def resolved.");
        }

        private static int ScorePaymentRecord(ThingDefRecord record, string token)
        {
            if (record == null || string.IsNullOrWhiteSpace(token))
            {
                return 0;
            }

            string defName = (record.DefName ?? string.Empty).ToLowerInvariant();
            string label = (record.Label ?? string.Empty).ToLowerInvariant();
            string search = (record.SearchText ?? string.Empty).ToLowerInvariant();
            if (defName == token)
            {
                return 200;
            }

            if (label == token)
            {
                return 160;
            }

            int score = 0;
            if (defName.Contains(token))
            {
                score += 80;
            }

            if (label.Contains(token))
            {
                score += 60;
            }

            if (search.Contains(token))
            {
                score += 20;
            }

            return score;
        }

        private static List<Thing> CollectBeaconTradeableThings(Map map)
        {
            var result = new List<Thing>();
            if (map == null)
            {
                return result;
            }

            List<Building_OrbitalTradeBeacon> beacons = Building_OrbitalTradeBeacon.AllPowered(map)?.ToList();
            if (beacons == null || beacons.Count == 0)
            {
                return result;
            }

            var cells = new HashSet<IntVec3>();
            for (int i = 0; i < beacons.Count; i++)
            {
                Building_OrbitalTradeBeacon beacon = beacons[i];
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
                List<Thing> thingsAt = map.thingGrid.ThingsListAt(cell);
                if (thingsAt == null || thingsAt.Count == 0)
                {
                    continue;
                }

                for (int i = 0; i < thingsAt.Count; i++)
                {
                    Thing thing = thingsAt[i];
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

        private APIResult ValidateDeductionPlan(
            Map map,
            List<ItemAirdropDeductionPlanLine> plan,
            out List<ThingDeductionReservation> reservations)
        {
            reservations = new List<ThingDeductionReservation>();
            if (map == null)
            {
                return BuildPaymentFailure("map_unavailable", "Commit map is unavailable.");
            }

            if (plan == null || plan.Count == 0)
            {
                return BuildPaymentFailure("payment_plan_invalid", "Deduction plan is empty.");
            }

            foreach (ItemAirdropDeductionPlanLine line in plan)
            {
                if (line == null || string.IsNullOrWhiteSpace(line.ThingId) || line.Count <= 0)
                {
                    return BuildPaymentFailure("payment_plan_invalid", "Deduction plan contains invalid rows.");
                }

                Thing thing = map.listerThings?.AllThings?.FirstOrDefault(item =>
                    item != null &&
                    string.Equals(item.ThingID, line.ThingId, StringComparison.Ordinal));
                if (thing == null || thing.Destroyed || !thing.Spawned)
                {
                    return BuildPaymentFailure("payment_item_insufficient", $"Planned payment stack '{line.ThingId}' is missing.");
                }

                if (!string.Equals(thing.def?.defName ?? string.Empty, line.DefName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    return BuildPaymentFailure("payment_plan_invalid", $"Planned payment stack '{line.ThingId}' no longer matches def '{line.DefName}'.");
                }

                if (thing.stackCount < line.Count)
                {
                    return BuildPaymentFailure(
                        "payment_item_insufficient",
                        $"Planned payment stack '{line.ThingId}' is insufficient. required={line.Count}, available={thing.stackCount}.");
                }

                reservations.Add(new ThingDeductionReservation
                {
                    Thing = thing,
                    Count = line.Count
                });
            }

            return APIResult.SuccessResult("Deduction plan validated.");
        }

        private static void ApplyDeductionReservations(List<ThingDeductionReservation> reservations)
        {
            if (reservations == null || reservations.Count == 0)
            {
                return;
            }

            foreach (ThingDeductionReservation reservation in reservations)
            {
                if (reservation?.Thing == null || reservation.Count <= 0)
                {
                    continue;
                }

                reservation.Thing.stackCount -= reservation.Count;
                if (reservation.Thing.stackCount <= 0)
                {
                    reservation.Thing.Destroy(DestroyMode.Vanish);
                }
            }
        }

        private static Dictionary<string, object> CloneParameterDictionary(Dictionary<string, object> source)
        {
            var clone = new Dictionary<string, object>();
            if (source == null)
            {
                return clone;
            }

            foreach (KeyValuePair<string, object> entry in source)
            {
                clone[entry.Key] = entry.Value;
            }

            return clone;
        }

        private static APIResult BuildPaymentFailure(string code, string message)
        {
            return new APIResult
            {
                Success = false,
                Message = $"[{code}] {message}",
                Data = new ItemAirdropResultData { FailureCode = code }
            };
        }
    }

    public sealed class ItemAirdropPreparedTradeData
    {
        public string NeedText { get; set; }
        public string Scenario { get; set; }
        public string SelectedDefName { get; set; }
        public string ResolvedLabel { get; set; }
        public int Quantity { get; set; }
        public int BudgetSilver { get; set; }
        public int PaymentTotalSilver { get; set; }
        public int PaymentOverpaySilver { get; set; }
        public string SelectionReason { get; set; }
        public int MapUniqueId { get; set; }
        public List<ItemAirdropPreparedPaymentLine> PaymentLines { get; set; } = new List<ItemAirdropPreparedPaymentLine>();
        public List<ItemAirdropDeductionPlanLine> DeductionPlan { get; set; } = new List<ItemAirdropDeductionPlanLine>();
        public Dictionary<string, object> ParametersSnapshot { get; set; } = new Dictionary<string, object>();
    }

    public sealed class ItemAirdropPreparedPaymentLine
    {
        public string RequestedItem { get; set; }
        public string DefName { get; set; }
        public string Label { get; set; }
        public int Count { get; set; }
        public float UnitMarketValue { get; set; }
        public float SubtotalMarketValue { get; set; }
    }

    public sealed class ItemAirdropDeductionPlanLine
    {
        public string ThingId { get; set; }
        public string DefName { get; set; }
        public int Count { get; set; }
    }

    internal sealed class ItemAirdropPaymentRequestLine
    {
        public string ItemText { get; set; }
        public int Count { get; set; }
    }

    internal sealed class ThingDeductionReservation
    {
        public Thing Thing { get; set; }
        public int Count { get; set; }
    }
}
