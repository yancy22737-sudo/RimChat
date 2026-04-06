using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    internal static class QuestGenerationProbe
    {
        private static readonly string[] UnresolvedTokenFragments =
        {
            "[colonistCountToLend]",
            "[lendForDays]",
            "[dutyDescription]",
            "[asker_objective]",
            "[WillSendShuttle]",
            "DEV: Show debug info",
            "debua info"
        };

        public static bool TryValidate(
            Faction faction,
            QuestScriptDef questDef,
            Dictionary<string, object> parameters,
            out string code,
            out string message)
        {
            code = "allowed";
            message = "Allowed";

            if (questDef == null)
            {
                code = "quest_template_missing";
                message = "Quest template is missing.";
                return false;
            }

            if (!QuestSlatePrebuilder.TryBuild(faction, questDef, parameters, out RimWorld.QuestGen.Slate slate, out code, out message))
            {
                return false;
            }

            Map map = slate.Exists("map") ? slate.Get<Map>("map") : (Find.CurrentMap ?? Find.AnyPlayerHomeMap);
            if (map == null)
            {
                code = "player_map_missing";
                message = $"Quest '{questDef.defName}' requires an active player map.";
                return false;
            }

            if (questDef.rootMinPoints > 0f)
            {
                float points = slate.Exists("points") ? slate.Get<float>("points") : StorytellerUtility.DefaultThreatPointsNow(map);
                if (points < questDef.rootMinPoints)
                {
                    code = "quest_points_too_low";
                    message = $"Quest '{questDef.defName}' requires at least {questDef.rootMinPoints:F0} threat points. Current slate provides {points:F0}.";
                    return false;
                }
            }

            if (!ValidateRequiredSlateKeys(questDef, slate, out code, out message))
            {
                return false;
            }

            switch (questDef.defName)
            {
                case "TradeRequest":
                    if (!HasFactionSettlement(faction, map.Tile))
                    {
                        code = "trade_no_reachable_settlement";
                        message = $"Quest '{questDef.defName}' requires a reachable settlement for faction '{faction?.Name ?? "Unknown"}'.";
                        return false;
                    }
                    break;

                case "OpportunitySite_PeaceTalks":
                    if (faction?.leader == null && !HasFactionSettlement(faction, map.Tile))
                    {
                        code = "peace_no_asker_context";
                        message = $"Quest '{questDef.defName}' requires a faction leader or settlement context.";
                        return false;
                    }
                    break;

                case "PawnLend":
                    if ((map.mapPawns?.FreeColonistsSpawnedCount ?? 0) <= 0)
                    {
                        code = "pawnlend_no_free_colonist";
                        message = $"Quest '{questDef.defName}' requires at least one free colonist on the current map.";
                        return false;
                    }
                    if (!ValidatePawnLendContract(slate, out code, out message))
                    {
                        return false;
                    }
                    break;

                case "ThreatReward_Raid_MiscReward":
                    if (!HasEmpireQuestIssuer(faction))
                    {
                        code = "threatreward_no_empire_issuer";
                        message = $"Quest '{questDef.defName}' requires a valid Empire issuer (leader or settlement-backed context).";
                        return false;
                    }
                    if ((map.mapPawns?.FreeColonistsSpawnedCount ?? 0) <= 0)
                    {
                        code = "threatreward_no_colonist";
                        message = $"Quest '{questDef.defName}' requires at least one free colonist on the active map.";
                        return false;
                    }
                    break;

                case "Hospitality_Refugee":
                    if (!HasEmpireQuestIssuer(faction))
                    {
                        code = "refugee_no_empire_issuer";
                        message = $"Quest '{questDef.defName}' requires a valid Empire issuer (leader or settlement-backed context).";
                        return false;
                    }
                    if (!HasPlayerBedsForGuests(map))
                    {
                        code = "refugee_no_guest_capacity";
                        message = $"Quest '{questDef.defName}' requires at least one usable non-prisoner bed for guests on the active map.";
                        return false;
                    }
                    break;

                case "BestowingCeremony":
                    if (!HasEmpireQuestIssuer(faction))
                    {
                        code = "bestowing_no_empire_issuer";
                        message = $"Quest '{questDef.defName}' requires a valid Empire issuer (leader or settlement-backed context).";
                        return false;
                    }
                    if (!TryGetBestowingCandidate(map, out Pawn bestowingTarget, out string candidateReason))
                    {
                        code = "bestowing_no_candidate";
                        message = $"Quest '{questDef.defName}' requires an eligible colonist candidate: {candidateReason}";
                        return false;
                    }
                    if (!HasThroneRoomLikeSpace(bestowingTarget, map))
                    {
                        code = "bestowing_no_throne_context";
                        message = $"Quest '{questDef.defName}' requires the target map to have a valid ceremony space for {bestowingTarget.LabelShortCap}.";
                        return false;
                    }
                    break;
            }

            return true;
        }

        public static bool TryValidateGeneratedQuest(
            Quest quest,
            QuestScriptDef questDef,
            RimWorld.QuestGen.Slate slate,
            out string code,
            out string message)
        {
            code = "allowed";
            message = "Allowed";

            if (questDef == null || slate == null)
            {
                code = "generated_quest_invalid";
                message = "Quest publication probe received invalid input.";
                return false;
            }

            if (string.Equals(questDef.defName, "OpportunitySite_ItemStash", StringComparison.Ordinal))
            {
                object sitePartsParams = slate.Exists("sitePartsParams") ? slate.Get<object>("sitePartsParams") : null;
                if (sitePartsParams == null)
                {
                    code = "itemstash_site_parts_missing";
                    message = $"Quest '{questDef.defName}' generated without sitePartsParams.";
                    return false;
                }
            }

            if (string.Equals(questDef.defName, "PawnLend", StringComparison.Ordinal) &&
                !ValidatePawnLendContract(slate, out code, out message))
            {
                return false;
            }

            if (ContainsUnresolvedTokens(quest, questDef, slate, out string unresolvedToken))
            {
                code = "quest_text_unresolved_token";
                message = $"Quest '{questDef.defName}' still contains unresolved token fragment '{unresolvedToken}'.";
                return false;
            }

            return true;
        }

        private static bool ValidatePawnLendContract(RimWorld.QuestGen.Slate slate, out string code, out string message)
        {
            code = "allowed";
            message = "Allowed";

            string[] required =
            {
                "asker",
                "colonistCountToLend",
                "lendForDays",
                "dutyDescription",
                "asker_objective",
                "WillSendShuttle"
            };

            for (int i = 0; i < required.Length; i++)
            {
                string key = required[i];
                if (!slate.Exists(key))
                {
                    code = "pawnlend_contract_missing";
                    message = $"Quest 'PawnLend' is missing required runtime field '{key}'.";
                    return false;
                }

                object value = slate.Get<object>(key);
                if (value == null)
                {
                    code = "pawnlend_contract_missing";
                    message = $"Quest 'PawnLend' contains null runtime field '{key}'.";
                    return false;
                }

                if (value is string text && string.IsNullOrWhiteSpace(text))
                {
                    code = "pawnlend_contract_missing";
                    message = $"Quest 'PawnLend' contains empty runtime field '{key}'.";
                    return false;
                }

                if ((key == "colonistCountToLend" || key == "lendForDays") && !IsPositiveNumber(value))
                {
                    code = "pawnlend_contract_invalid";
                    message = $"Quest 'PawnLend' requires positive numeric field '{key}'.";
                    return false;
                }
            }

            return true;
        }

        private static bool IsPositiveNumber(object value)
        {
            if (value is int i)
            {
                return i > 0;
            }
            if (value is float f)
            {
                return f > 0f;
            }
            if (value is string s && float.TryParse(s, out float parsed))
            {
                return parsed > 0f;
            }

            return false;
        }

        private static bool ContainsUnresolvedTokens(Quest quest, QuestScriptDef questDef, RimWorld.QuestGen.Slate slate, out string token)
        {
            List<string> sources = new List<string>();
            token = null;

            if (quest?.name != null)
            {
                sources.Add(quest.name);
            }

            AddSlateText(sources, slate, "description");
            AddSlateText(sources, slate, "title");
            AddSlateText(sources, slate, "rewardDescription");
            AddSlateText(sources, slate, "dutyDescription");
            AddSlateText(sources, slate, "asker_objective");

            for (int i = 0; i < sources.Count; i++)
            {
                string source = sources[i];
                if (string.IsNullOrWhiteSpace(source))
                {
                    continue;
                }

                for (int j = 0; j < UnresolvedTokenFragments.Length; j++)
                {
                    if (source.IndexOf(UnresolvedTokenFragments[j], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        token = UnresolvedTokenFragments[j];
                        return true;
                    }
                }
            }

            return false;
        }

        private static void AddSlateText(List<string> sources, RimWorld.QuestGen.Slate slate, string key)
        {
            if (!slate.Exists(key))
            {
                return;
            }

            object value = slate.Get<object>(key);
            if (value is string text && !string.IsNullOrWhiteSpace(text))
            {
                sources.Add(text);
            }
        }

        private static bool HasFactionSettlement(Faction faction, int fromTile)
        {
            if (faction == null || Find.WorldObjects?.Settlements == null)
            {
                return false;
            }

            return Find.WorldObjects.Settlements
                .Where(s => s != null && s.Faction == faction)
                .OrderBy(s => Find.WorldGrid.TraversalDistanceBetween(fromTile, s.Tile))
                .Any();
        }

        private static bool ValidateRequiredSlateKeys(QuestScriptDef questDef, RimWorld.QuestGen.Slate slate, out string code, out string message)
        {
            code = "allowed";
            message = "Allowed";
            if (questDef == null || slate == null)
            {
                code = "slate_probe_invalid";
                message = "Quest slate probe received invalid input.";
                return false;
            }

            string[] requiredKeys = GetRequiredSlateKeys(questDef.defName);
            for (int i = 0; i < requiredKeys.Length; i++)
            {
                string key = requiredKeys[i];
                if (!slate.Exists(key))
                {
                    code = "slate_key_missing";
                    message = $"Quest '{questDef.defName}' is missing required slate key '{key}' during prebuild.";
                    return false;
                }
            }

            return true;
        }

        private static string[] GetRequiredSlateKeys(string questDefName)
        {
            switch (questDefName)
            {
                case "TradeRequest":
                    return new[] { "map", "faction", "askerFaction", "giverFaction", "settlement" };
                case "OpportunitySite_PeaceTalks":
                    return new[] { "map", "faction", "askerFaction", "giverFaction" };
                case "PawnLend":
                    return new[] { "map", "faction", "askerFaction", "giverFaction", "asker" };
                case "ThreatReward_Raid_MiscReward":
                case "Hospitality_Refugee":
                case "BestowingCeremony":
                    return new[] { "map", "faction", "askerFaction", "giverFaction", "asker" };
                default:
                    return new[] { "map", "faction", "askerFaction", "giverFaction" };
            }
        }

        private static bool HasEmpireQuestIssuer(Faction faction)
        {
            if (faction == null)
            {
                return false;
            }

            if (faction.leader != null)
            {
                return true;
            }

            Map map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            return HasFactionSettlement(faction, map?.Tile ?? 0);
        }

        private static bool HasPlayerBedsForGuests(Map map)
        {
            if (map?.listerBuildings?.allBuildingsColonist == null)
            {
                return false;
            }

            return map.listerBuildings.allBuildingsColonist
                .OfType<Building_Bed>()
                .Any(bed => bed != null && !bed.ForPrisoners && bed.SleepingSlotsCount > 0 && bed.Spawned);
        }

        private static bool TryGetBestowingCandidate(Map map, out Pawn candidate, out string reason)
        {
            candidate = null;
            reason = "no free colonist found";
            if (map?.mapPawns?.FreeColonistsSpawned == null)
            {
                reason = "active map has no free colonist list";
                return false;
            }

            candidate = map.mapPawns.FreeColonistsSpawned
                .FirstOrDefault(pawn => pawn != null && !pawn.Dead && pawn.royalty != null);
            if (candidate == null)
            {
                reason = "no colonist with royalty tracker found";
                return false;
            }

            return true;
        }

        private static bool HasThroneRoomLikeSpace(Pawn candidate, Map map)
        {
            if (candidate == null || map == null)
            {
                return false;
            }

            Room ownedRoom = candidate.ownership?.OwnedRoom;
            if (ownedRoom != null && ownedRoom.Role != null)
            {
                string roleId = ownedRoom.Role.defName ?? string.Empty;
                if (roleId.IndexOf("throne", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            if (map.listerBuildings?.allBuildingsColonist == null)
            {
                return false;
            }

            return map.listerBuildings.allBuildingsColonist.Any(building =>
                building != null &&
                building.def != null &&
                building.def.defName.IndexOf("throne", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
