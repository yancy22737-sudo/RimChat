using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: ThingDefCatalog, ItemAirdropSafetyPolicy, item-airdrop parameter metadata.
    /// Responsibility: arbitrate card-bound need metadata against fuzzy airdrop candidates.
    /// </summary>
    public partial class GameAIInterface
    {
        private APIResult TryApplyBoundNeedArbitration(
            Faction faction,
            Dictionary<string, object> parameters,
            ItemAirdropIntent intent,
            ItemAirdropCandidatePack candidatePack,
            out ItemAirdropBoundNeedInfo boundNeed)
        {
            boundNeed = null;
            if (!TryResolveBoundNeedInfo(parameters, out ItemAirdropBoundNeedInfo resolvedBoundNeed))
            {
                return APIResult.SuccessResult("No bound need metadata.");
            }

            boundNeed = resolvedBoundNeed;
            if (candidatePack != null)
            {
                candidatePack.BoundNeedDefName = resolvedBoundNeed.DefName;
            }

            if (resolvedBoundNeed.Record?.Def == null)
            {
                string message = "RimChat_ItemAirdropBoundNeedUnresolvedSystem"
                    .Translate(resolvedBoundNeed.DefName)
                    .ToString();
                string diagnostics = BuildBoundNeedAuditDetails(resolvedBoundNeed, intent, "bound_need_unresolved");
                MarkBoundNeedConflict(candidatePack, "bound_need_unresolved", diagnostics, false);
                RegisterBoundNeedConflict(parameters, "bound_need_unresolved", message);
                RecordAPICall("RequestItemAirdrop.BoundNeedReject", false, diagnostics, message);
                return FailFastAirdrop("bound_need_unresolved", message, faction, parameters, diagnostics);
            }

            if (intent != null &&
                intent.Family != ItemAirdropNeedFamily.Unknown &&
                !ThingDefResolver.CanCandidateForNeed(resolvedBoundNeed.Record, intent.Family))
            {
                string message = "RimChat_ItemAirdropBoundNeedFamilyConflictSystem"
                    .Translate(resolvedBoundNeed.Label, resolvedBoundNeed.DefName)
                    .ToString();
                string diagnostics = BuildBoundNeedAuditDetails(resolvedBoundNeed, intent, "bound_need_family_conflict");
                MarkBoundNeedConflict(candidatePack, "bound_need_family_conflict", diagnostics, false);
                RegisterBoundNeedConflict(parameters, "bound_need_family_conflict", message);
                RecordAPICall("RequestItemAirdrop.BoundNeedReject", false, diagnostics, message);
                return FailFastAirdrop("bound_need_family_conflict", message, faction, parameters, diagnostics);
            }

            if (candidatePack?.Candidates == null)
            {
                return APIResult.SuccessResult("Bound need resolved without candidate pack.");
            }

            bool alreadyPresent = candidatePack.Candidates.Any(candidate =>
                candidate?.Record?.Def != null &&
                string.Equals(candidate.Record.DefName, resolvedBoundNeed.DefName, StringComparison.OrdinalIgnoreCase));
            if (alreadyPresent)
            {
                return APIResult.SuccessResult("Bound need already present in candidates.");
            }

            candidatePack.Candidates.Insert(0, BuildInjectedBoundNeedCandidate(candidatePack, resolvedBoundNeed, intent));
            string injectDiagnostics = BuildBoundNeedAuditDetails(resolvedBoundNeed, intent, "bound_need_candidate_conflict");
            candidatePack.Candidates = candidatePack.Candidates
                .GroupBy(candidate => candidate?.Record?.DefName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            MarkBoundNeedConflict(candidatePack, "bound_need_candidate_conflict", injectDiagnostics, true);
            RegisterBoundNeedConflict(
                parameters,
                "bound_need_candidate_conflict",
                "RimChat_ItemAirdropBoundNeedCandidateConflictSystem"
                    .Translate(resolvedBoundNeed.Label, resolvedBoundNeed.DefName)
                    .ToString());
            RecordAPICall("RequestItemAirdrop.BoundNeedArbitrated", true, injectDiagnostics);
            return APIResult.SuccessResult("Bound need injected into candidate pack.", resolvedBoundNeed);
        }

        private static string ResolveEffectiveForcedSelectedDef(
            Dictionary<string, object> parameters,
            string forcedSelectedDefName,
            out bool hasBoundNeed,
            out bool hadForcedSelectionConflict)
        {
            string boundNeedDefName = ReadString(parameters, ItemAirdropParameterKeys.BoundNeedDefName);
            hasBoundNeed = !string.IsNullOrWhiteSpace(boundNeedDefName);
            hadForcedSelectionConflict = false;
            if (!hasBoundNeed)
            {
                return forcedSelectedDefName ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(forcedSelectedDefName) &&
                !string.Equals(forcedSelectedDefName, boundNeedDefName, StringComparison.OrdinalIgnoreCase))
            {
                hadForcedSelectionConflict = true;
            }

            return boundNeedDefName;
        }

        private APIResult ValidatePreparedTradeBoundNeedConsistency(
            Faction faction,
            Dictionary<string, object> parameters,
            ItemAirdropPreparedTradeData preparedTrade)
        {
            if (preparedTrade == null)
            {
                return APIResult.SuccessResult("Prepared trade is null.");
            }

            if (!TryResolveBoundNeedInfo(parameters, out ItemAirdropBoundNeedInfo boundNeed) ||
                string.IsNullOrWhiteSpace(boundNeed?.DefName))
            {
                return APIResult.SuccessResult("No bound need consistency check required.");
            }

            if (string.Equals(preparedTrade.SelectedDefName, boundNeed.DefName, StringComparison.OrdinalIgnoreCase))
            {
                return APIResult.SuccessResult("Prepared trade matches bound need.");
            }

            string selectedLabel = string.IsNullOrWhiteSpace(preparedTrade.ResolvedLabel)
                ? preparedTrade.SelectedDefName
                : preparedTrade.ResolvedLabel;
            string boundLabel = string.IsNullOrWhiteSpace(boundNeed.Label)
                ? boundNeed.DefName
                : boundNeed.Label;
            string message = "RimChat_ItemAirdropBoundNeedPreparedMismatchSystem"
                .Translate(selectedLabel, preparedTrade.SelectedDefName, boundLabel, boundNeed.DefName)
                .ToString();
            string diagnostics =
                $"code=bound_need_prepared_mismatch,boundNeedDef={boundNeed.DefName},preparedSelectedDef={preparedTrade.SelectedDefName},preparedSelectedLabel={selectedLabel}";
            RecordAPICall("RequestItemAirdrop.BoundNeedReject", false, diagnostics, message);
            return FailFastAirdrop("bound_need_prepared_mismatch", message, faction, parameters, diagnostics);
        }

        private static bool TryResolveBoundNeedInfo(
            Dictionary<string, object> parameters,
            out ItemAirdropBoundNeedInfo boundNeed)
        {
            boundNeed = null;
            string defName = ReadString(parameters, ItemAirdropParameterKeys.BoundNeedDefName);
            if (string.IsNullOrWhiteSpace(defName))
            {
                return false;
            }

            ThingDefRecord record = ThingDefCatalog.GetRecords()
                .FirstOrDefault(candidate =>
                    candidate?.Def != null &&
                    string.Equals(candidate.DefName, defName, StringComparison.OrdinalIgnoreCase));
            boundNeed = new ItemAirdropBoundNeedInfo
            {
                DefName = defName.Trim(),
                Label = ReadString(parameters, ItemAirdropParameterKeys.BoundNeedLabel),
                SearchText = ReadString(parameters, ItemAirdropParameterKeys.BoundNeedSearchText),
                Record = record
            };

            if (string.IsNullOrWhiteSpace(boundNeed.Label))
            {
                boundNeed.Label = record?.Label ?? boundNeed.DefName;
            }

            if (string.IsNullOrWhiteSpace(boundNeed.SearchText))
            {
                boundNeed.SearchText = record?.SearchText ?? boundNeed.DefName;
            }

            return true;
        }

        private static ItemAirdropCandidate BuildInjectedBoundNeedCandidate(
            ItemAirdropCandidatePack candidatePack,
            ItemAirdropBoundNeedInfo boundNeed,
            ItemAirdropIntent intent)
        {
            int preferredScore = 200;
            if (candidatePack?.Candidates != null && candidatePack.Candidates.Count > 0)
            {
                preferredScore = Math.Max(preferredScore, candidatePack.Candidates.Max(candidate => candidate?.MatchScore ?? 0) + 32);
            }

            return new ItemAirdropCandidate
            {
                Record = boundNeed.Record,
                Family = intent?.Family ?? ItemAirdropNeedFamily.Unknown,
                MatchScore = preferredScore,
                SafetyScore = ItemAirdropSafetyPolicy.BuildSafetyScore(boundNeed.Record),
                Price = Math.Max(0.01f, boundNeed.Record?.MarketValue ?? 0.01f)
            };
        }

        private static void MarkBoundNeedConflict(
            ItemAirdropCandidatePack candidatePack,
            string conflictCode,
            string conflictDetails,
            bool injected)
        {
            if (candidatePack == null)
            {
                return;
            }

            candidatePack.HasBoundNeedConflict = true;
            candidatePack.BoundNeedConflictCode = conflictCode ?? string.Empty;
            candidatePack.BoundNeedConflictDetails = conflictDetails ?? string.Empty;
            candidatePack.BoundNeedInjectedIntoCandidates = injected;
        }

        private static void RegisterBoundNeedConflict(
            Dictionary<string, object> parameters,
            string conflictCode,
            string message)
        {
            if (parameters == null)
            {
                return;
            }

            parameters[ItemAirdropParameterKeys.BoundNeedConflictCode] = conflictCode ?? string.Empty;
            parameters[ItemAirdropParameterKeys.BoundNeedConflictMessage] = message ?? string.Empty;
        }

        private static string BuildBoundNeedAuditDetails(
            ItemAirdropBoundNeedInfo boundNeed,
            ItemAirdropIntent intent,
            string code)
        {
            ThingDef def = boundNeed?.Record?.Def;
            string family = intent?.Family.ToString() ?? ItemAirdropNeedFamily.Unknown.ToString();
            if (def == null)
            {
                return $"code={code},boundNeedDef={boundNeed?.DefName ?? "none"},family={family},resolved=false";
            }

            return
                $"code={code}," +
                $"boundNeedDef={boundNeed.DefName}," +
                $"boundNeedLabel={boundNeed.Label}," +
                $"family={family}," +
                $"category={def.category}," +
                $"tradeability={def.tradeability}," +
                $"BaseMarketValue={def.BaseMarketValue.ToString("F2")}," +
                $"stackLimit={def.stackLimit}," +
                $"stuffProps={(def.stuffProps != null)}," +
                $"IsNutritionGivingIngestible={def.IsNutritionGivingIngestible}," +
                $"IsMedicine={def.IsMedicine}," +
                $"IsDrug={def.IsDrug}," +
                $"IsWeapon={def.IsWeapon}," +
                $"IsApparel={def.IsApparel}";
        }

        private static void ClearStaleBoundNeedParameters(Dictionary<string, object> parameters)
        {
            if (parameters == null)
            {
                return;
            }

            parameters.Remove(ItemAirdropParameterKeys.BoundNeedDefName);
            parameters.Remove(ItemAirdropParameterKeys.BoundNeedLabel);
            parameters.Remove(ItemAirdropParameterKeys.BoundNeedSearchText);
            parameters.Remove(ItemAirdropParameterKeys.BoundNeedSource);
            parameters.Remove(ItemAirdropParameterKeys.BoundNeedConflictCode);
            parameters.Remove(ItemAirdropParameterKeys.BoundNeedConflictMessage);
        }
    }
}
