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

            return PrepareItemAirdropTradeForMap(faction, parameters, playerNegotiator.Map, true, playerNegotiator);
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
                if (MapUtility.IsOrbitalBaseMap(map))
                {
                    return FailFastAirdrop(
                        "orbital_drop_unavailable",
                        "You are on an orbital base and cannot receive supply drops.",
                        faction,
                        preparedData.ParametersSnapshot);
                }
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

            SetCooldown(faction, "RequestItemAirdrop");

            return APIResult.SuccessResult(
                $"Airdrop delivered: {selectedRecord.DefName} x{deliveredCount} (budget {preparedData.BudgetSilver})",
                payload);
        }

        private APIResult PrepareItemAirdropTradeForMap(
            Faction faction,
            Dictionary<string, object> parameters,
            Map map,
            bool requirePlayerHome,
            Pawn playerNegotiator)
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

            bool hasNeed = TryReadRequiredStringParameter(
                parameters,
                "need",
                out string need,
                out string needType,
                out string needRawPreview);
            if (!hasNeed)
            {
                string code = string.Equals(needType, "missing", StringComparison.Ordinal) ? "missing_need" : "need_type_invalid";
                return FailFastAirdrop(code, "request_item_airdrop requires string parameter 'need'.", faction, parameters);
            }

            string scenario = NormalizeScenario(ReadString(parameters, "scenario"));
            string constraints = ReadString(parameters, "constraints");
            bool hasProvidedBudget = TryReadIntParameter(parameters, "budget_silver", out int providedBudgetSilver);

            APIResult paymentPlanResult = BuildPaymentPlan(
                parameters,
                map,
                faction,
                playerNegotiator,
                out List<ItemAirdropPreparedPaymentLine> paymentLines,
                out List<ItemAirdropDeductionPlanLine> deductionPlan,
                out int budget,
                out int paymentTotalSilver);
            if (!paymentPlanResult.Success)
            {
                return FailFastAirdrop(
                    (paymentPlanResult.Data as ItemAirdropResultData)?.FailureCode ?? "payment_plan_failed",
                    paymentPlanResult.Message,
                    faction,
                    parameters);
            }

            if (hasProvidedBudget && providedBudgetSilver != budget)
            {
                string mismatchAudit =
                    $"faction={faction?.Name ?? "unknown"},provided={providedBudgetSilver},derived={budget},delta={providedBudgetSilver - budget},need={need},scenario={scenario}";
                RecordAPICall("RequestItemAirdrop.BudgetMismatch", true, mismatchAudit);
            }

            AirdropTradeRuleSnapshot tradeRule = ItemAirdropTradePolicy.ResolveRuleSnapshot(faction);
            if (paymentTotalSilver > tradeRule.TradeLimitSilver)
            {
                return FailFastAirdrop(
                    "trade_limit_exceeded",
                    $"Offer total {paymentTotalSilver} exceeds current trade limit {tradeRule.TradeLimitSilver}.",
                    faction,
                    parameters,
                    $"goodwill={tradeRule.Goodwill},isMerchant={tradeRule.IsMerchantFaction},isAlly={tradeRule.IsAlly},limit={tradeRule.TradeLimitSilver}");
            }

            ItemAirdropIntent intent = ItemAirdropIntent.Create(need, constraints, scenario);
            APIResult candidateResult = PrepareItemAirdropCandidates(
                intent,
                budget,
                settings,
                out ItemAirdropCandidatePack candidatePack);
            if (!candidateResult.Success)
            {
                return candidateResult;
            }
            List<string> localAliases = new List<string>();
            List<string> aliases = new List<string>();
            if (candidatePack.Candidates.Count == 0)
            {
                localAliases = ThingDefResolver.ExpandLocalAliases(intent);
                if (localAliases.Count > 0)
                {
                    intent = ItemAirdropIntent.Create(need, constraints, scenario, localAliases);
                    candidateResult = PrepareItemAirdropCandidates(
                        intent,
                        budget,
                        settings,
                        out candidatePack);
                    if (!candidateResult.Success)
                    {
                        return candidateResult;
                    }
                }
            }

            if (candidatePack.Candidates.Count == 0)
            {
                aliases = ExpandNeedAliasesWithAi(need, constraints, settings);
                if (aliases.Count > 0)
                {
                    intent = ItemAirdropIntent.Create(need, constraints, scenario, aliases);
                    candidateResult = PrepareItemAirdropCandidates(
                        intent,
                        budget,
                        settings,
                        out candidatePack);
                    if (!candidateResult.Success)
                    {
                        return candidateResult;
                    }
                }
            }

            APIResult boundNeedResult = TryApplyBoundNeedArbitration(
                faction,
                parameters,
                intent,
                candidatePack,
                out _);
            if (!boundNeedResult.Success)
            {
                return boundNeedResult;
            }

            string prepareSummary = BuildPrepareAuditSummary(intent, budget, candidatePack, localAliases, aliases, needType, needRawPreview);
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

            if (ShouldRequireNeedClarification(intent, candidatePack))
            {
                APIResult pendingClarification = BuildTimeoutPendingSelection(
                    intent,
                    candidatePack,
                    budget,
                    settings,
                    "need_relevance_insufficient",
                    BuildNeedClarificationReason(),
                    allowEmptyOptions: true);
                if (pendingClarification.Data is ItemAirdropPendingSelectionData pendingData)
                {
                    RecordStageAudit("selection", null, null, BuildPendingSelectionAuditDetails(pendingData));
                }

                return pendingClarification;
            }

            string forcedSelectedDef = ReadString(parameters, "selected_def");
            APIResult selectionResult = ExecuteItemAirdropSelection(intent, candidatePack, budget, settings, parameters, forcedSelectedDef);
            if (!selectionResult.Success)
            {
                string code = (selectionResult.Data as ItemAirdropResultData)?.FailureCode ?? "selection_failed";
                return FailFastAirdrop(code, selectionResult.Message, faction, parameters);
            }

            if (selectionResult.Data is ItemAirdropPendingSelectionData pendingSelection)
            {
                return APIResult.SuccessResult("Airdrop selection requires player confirmation.", pendingSelection);
            }

            if (!(selectionResult.Data is ItemAirdropSelection selection))
            {
                return FailFastAirdrop("selection_invalid", "Selection result payload is invalid.", faction, parameters);
            }

            RequestedCountExtraction requestedCount = ExtractRequestedCount(intent?.NeedText);
            APIResult validationResult = ValidateAirdropSelection(
                selection,
                candidatePack,
                budget,
                settings,
                requestedCount,
                "llm",
                out ThingDefRecord selectedRecord,
                out int validatedCount,
                out _);
            if (!validationResult.Success)
            {
                return FailFastAirdrop(
                    (validationResult.Data as ItemAirdropResultData)?.FailureCode ?? "selection_invalid",
                    validationResult.Message,
                    faction,
                    parameters);
            }

            int overpay = Math.Max(0, paymentTotalSilver - budget);
            string budgetMismatchSummary = hasProvidedBudget
                ? $"{providedBudgetSilver}->{budget}(delta={providedBudgetSilver - budget})"
                : "none";
            string paymentSummary = $"budget={budget},payment={paymentTotalSilver},overpay={overpay},budgetMismatch={budgetMismatchSummary},paymentLines={paymentLines.Count},deductionRows={deductionPlan.Count}";
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

        private static int ResolveAirdropShippingPodCount(ThingDef selectedDef, int quantity)
        {
            int safeQuantity = Math.Max(0, quantity);
            if (safeQuantity <= 0)
            {
                return 0;
            }

            int stackLimit = Math.Max(1, selectedDef?.stackLimit ?? safeQuantity);
            return (int)Math.Ceiling((double)safeQuantity / stackLimit);
        }

        private static int ResolveAirdropNeedQuotedTotalSilver(
            ThingDefRecord selectedRecord,
            int quantity,
            Faction faction,
            Pawn playerNegotiator,
            Map map,
            ItemAirdropCandidatePack candidatePack)
        {
            float unitPrice = ResolveAirdropNeedQuotedUnitPrice(selectedRecord, faction, playerNegotiator, map, candidatePack);
            float total = Math.Max(0f, unitPrice) * Math.Max(0, quantity);
            return Mathf.Max(0, Mathf.RoundToInt(total));
        }

        private static float ResolveAirdropNeedQuotedUnitPrice(
            ThingDefRecord selectedRecord,
            Faction faction,
            Pawn playerNegotiator,
            Map map,
            ItemAirdropCandidatePack candidatePack)
        {
            _ = faction;
            _ = playerNegotiator;
            _ = map;
            ThingDef def = selectedRecord?.Def;
            if (def != null && ItemAirdropTradePolicy.TryResolvePlayerBuyPrice(def, faction, playerNegotiator, map, out float unitPrice, out _))
            {
                return unitPrice;
            }

            return candidatePack?.ResolveUnitPrice(selectedRecord) ?? Math.Max(0.01f, selectedRecord?.MarketValue ?? 0.01f);
        }

        private APIResult BuildPaymentPlan(
            Dictionary<string, object> parameters,
            Map map,
            Faction faction,
            Pawn playerNegotiator,
            out List<ItemAirdropPreparedPaymentLine> paymentLines,
            out List<ItemAirdropDeductionPlanLine> deductionPlan,
            out int derivedBudgetSilver,
            out int paymentTotalSilver)
        {
            paymentLines = new List<ItemAirdropPreparedPaymentLine>();
            deductionPlan = new List<ItemAirdropDeductionPlanLine>();
            derivedBudgetSilver = 0;
            paymentTotalSilver = 0;

            APIResult parseResult = ParsePaymentItems(parameters, out List<ItemAirdropPaymentRequestLine> requestedLines);
            if (!parseResult.Success)
            {
                Log.Message($"[RimChat][PaymentPlan] ParsePaymentItems failed: {parseResult.Message}");
                return parseResult;
            }

            Log.Message($"[RimChat][PaymentPlan] Parsed {requestedLines.Count} payment_items: {string.Join(", ", requestedLines.Select(l => $"{l.ItemText}x{l.Count}"))}");

            List<Thing> beaconThings = CollectBeaconTradeableThings(map);
            if (beaconThings.Count == 0)
            {
                Log.Message("[RimChat][PaymentPlan] No powered orbital-trade-beacon source items available.");
                return BuildPaymentFailure("beacon_source_unavailable", "No powered orbital-trade-beacon source items are available on this map.");
            }

            Log.Message($"[RimChat][PaymentPlan] Beacon has {beaconThings.Count} tradeable things.");

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

            List<ThingDefRecord> stockedRecords = buckets.Values
                .Select(bucket => bucket.FirstOrDefault()?.def)
                .Where(def => def != null)
                .Select(ThingDefRecord.From)
                .GroupBy(record => record.DefName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            Log.Message($"[RimChat][PaymentPlan] Beacon inventory buckets: {string.Join(", ", buckets.Select(kvp => $"{kvp.Key}x{kvp.Value.Sum(t => t.stackCount)}"))}");

            float totalValueFloat = 0f;
            for (int i = 0; i < requestedLines.Count; i++)
            {
                ItemAirdropPaymentRequestLine line = requestedLines[i];
                APIResult resolveResult = TryResolvePaymentThingDef(line.ItemText, stockedRecords, out ThingDefRecord resolvedRecord);
                if (!resolveResult.Success)
                {
                    APIResult catalogResolveResult = TryResolvePaymentThingDef(
                        line.ItemText,
                        ThingDefCatalog.GetTradeablePaymentRecords(),
                        out ThingDefRecord catalogResolvedRecord);
                    if (catalogResolveResult.Success && catalogResolvedRecord != null)
                    {
                        Log.Message($"[RimChat][PaymentPlan] Payment item '{line.ItemText}' resolved globally to '{catalogResolvedRecord.DefName}' but is absent from beacon stock.");
                        return BuildPaymentFailure(
                            "payment_item_insufficient",
                            $"No tradable beacon stock found for payment item '{line.ItemText}' ({catalogResolvedRecord.DefName}).");
                    }

                    Log.Message($"[RimChat][PaymentPlan] Failed to resolve payment item '{line.ItemText}' against beacon stock: {resolveResult.Message}");
                    return resolveResult;
                }

                if (!buckets.TryGetValue(resolvedRecord.DefName, out List<Thing> stockThings))
                {
                    Log.Message($"[RimChat][PaymentPlan] No beacon stock for payment item '{resolvedRecord.DefName}' ({line.ItemText}). Available: {string.Join(", ", buckets.Keys)}");
                    return BuildPaymentFailure(
                        "payment_item_insufficient",
                        $"No tradable beacon stock found for payment item '{line.ItemText}' ({resolvedRecord.DefName}).");
                }

                int availableCount = stockThings.Sum(thing => Math.Max(0, thing.stackCount));
                if (availableCount < line.Count)
                {
                    Log.Message($"[RimChat][PaymentPlan] Insufficient stock for '{resolvedRecord.DefName}': required={line.Count}, available={availableCount}");
                    return BuildPaymentFailure(
                        "payment_item_insufficient",
                        $"Insufficient stock for '{resolvedRecord.DefName}'. required={line.Count}, available={availableCount}.");
                }

                float unitPrice = ResolveAirdropPaymentUnitPrice(
                    resolvedRecord,
                    faction,
                    playerNegotiator,
                    map,
                    out string unitPriceFailureCode);
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

                Log.Message($"[RimChat][PaymentPlan] Payment line: {resolvedRecord.DefName} x{line.Count} @ {unitPrice:F1} = {subtotal:F1} silver");

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

            int flooredTotalValue = Mathf.FloorToInt(Math.Max(0f, totalValueFloat));
            if (flooredTotalValue <= 0)
            {
                Log.Message($"[RimChat][PaymentPlan] Derived budget is not positive: total={totalValueFloat:F1}");
                return BuildPaymentFailure(
                    "budget_invalid",
                    $"Derived budget from payment_items is not positive. total={totalValueFloat:F1}.");
            }

            Log.Message($"[RimChat][PaymentPlan] Payment plan complete: budget={flooredTotalValue} silver, paymentLines={paymentLines.Count}, deductionRows={deductionPlan.Count}");
            derivedBudgetSilver = flooredTotalValue;
            paymentTotalSilver = flooredTotalValue;
            return APIResult.SuccessResult("Payment plan prepared.");
        }

        private static float ResolveAirdropPaymentUnitPrice(
            ThingDefRecord resolvedRecord,
            Faction faction,
            Pawn playerNegotiator,
            Map map,
            out string failureCode)
        {
            _ = faction;
            _ = playerNegotiator;
            _ = map;
            ThingDef def = resolvedRecord?.Def;
            if (ItemAirdropTradePolicy.TryResolveOfferUnitPrice(def, out float resolved, out failureCode))
            {
                return resolved;
            }

            return Math.Max(0.01f, resolvedRecord?.MarketValue ?? 0.01f);
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

        private APIResult TryResolvePaymentThingDef(
            string itemText,
            IReadOnlyList<ThingDefRecord> candidateRecords,
            out ThingDefRecord resolvedRecord)
        {
            resolvedRecord = null;
            string query = (itemText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                return BuildPaymentFailure("payment_item_unresolved", "Payment item text cannot be empty.");
            }

            List<ThingDefRecord> records = (candidateRecords ?? Array.Empty<ThingDefRecord>())
                .Where(record => record?.Def != null)
                .GroupBy(record => record.DefName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            ItemAirdropPaymentResolveResult resolveResult = ItemAirdropPaymentResolver.Resolve(query, records);
            if (!resolveResult.Success || resolveResult.ResolvedRecord == null)
            {
                string failureCode = string.IsNullOrWhiteSpace(resolveResult?.FailureCode)
                    ? "payment_item_unresolved"
                    : resolveResult.FailureCode;
                string failureMessage = string.IsNullOrWhiteSpace(resolveResult?.FailureMessage)
                    ? $"Payment item '{query}' could not be resolved."
                    : resolveResult.FailureMessage;
                return BuildPaymentFailure(failureCode, failureMessage);
            }

            resolvedRecord = resolveResult.ResolvedRecord;
            return APIResult.SuccessResult("Payment def resolved.");
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
        public float NeedQuotedUnitSilver { get; set; }
        public int PaymentTotalSilver { get; set; }
        public int PaymentItemTotalSilver { get; set; }
        public int ShippingPodCount { get; set; }
        public int ShippingCostSilver { get; set; }
        public int PaymentOverpaySilver { get; set; }
        public string SelectionReason { get; set; }
        public string NeedPriceSemantic { get; set; } = "market_value_x1.4";
        public string PaymentPriceSemantic { get; set; } = "market_value_x0.6";
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
        public string PriceSemantic { get; set; } = "market_value_x0.6";
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
