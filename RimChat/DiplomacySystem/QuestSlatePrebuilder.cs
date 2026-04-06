using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimChat.DiplomacySystem
{
    internal static class QuestSlatePrebuilder
    {
        private const string PawnLendDefName = "PawnLend";
        private static readonly string[] PawnLendRequiredKeys =
        {
            "colonistCountToLend",
            "lendForDays",
            "dutyDescription",
            "asker_objective",
            "WillSendShuttle"
        };

        public static bool TryBuild(
            Faction faction,
            QuestScriptDef questDef,
            Dictionary<string, object> parameters,
            out RimWorld.QuestGen.Slate slate,
            out string code,
            out string message)
        {
            slate = null;
            code = "allowed";
            message = "Allowed";

            if (questDef == null)
            {
                code = "quest_template_missing";
                message = "Quest template is missing.";
                return false;
            }

            Dictionary<string, object> source = parameters != null
                ? new Dictionary<string, object>(parameters, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            if (faction != null)
            {
                source["faction"] = faction;
                source["askerFaction"] = faction;
            }

            bool isItemStashQuest = string.Equals(questDef.defName, "OpportunitySite_ItemStash", StringComparison.Ordinal);
            Map playerMap = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            if (playerMap == null)
            {
                code = "player_map_missing";
                message = $"Quest '{questDef.defName}' requires an active player map.";
                return false;
            }

            slate = new RimWorld.QuestGen.Slate();
            foreach (var kvp in source)
            {
                if (isItemStashQuest && string.Equals(kvp.Key, "siteFaction", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (kvp.Value == null)
                {
                    continue;
                }

                slate.Set(kvp.Key, ResolveParameter(kvp.Key, kvp.Value));
            }

            if (!slate.Exists("map"))
            {
                slate.Set("map", playerMap);
            }

            if (faction != null)
            {
                if (!slate.Exists("faction")) slate.Set("faction", faction);
                if (!slate.Exists("askerFaction")) slate.Set("askerFaction", faction);
                if (!slate.Exists("giverFaction")) slate.Set("giverFaction", faction);
                if (!isItemStashQuest && !slate.Exists("siteFaction")) slate.Set("siteFaction", faction);

                if (!slate.Exists("enemyFaction"))
                {
                    Faction enemy = Find.FactionManager.RandomEnemyFaction(true, true, true, TechLevel.Undefined);
                    if (enemy != null)
                    {
                        slate.Set("enemyFaction", enemy);
                    }
                }
            }

            if (!slate.Exists("settlement") && faction != null && Find.WorldObjects?.Settlements != null)
            {
                Settlement settlement = Find.WorldObjects.Settlements
                    .Where(s => s != null && s.Faction == faction)
                    .OrderBy(s => Find.WorldGrid.TraversalDistanceBetween(playerMap.Tile, s.Tile))
                    .FirstOrDefault();
                if (settlement != null)
                {
                    slate.Set("settlement", settlement);
                }
            }

            if (!slate.Exists("asker") && faction != null)
            {
                Settlement settlement = slate.Exists("settlement") ? slate.Get<Settlement>("settlement") : null;
                if (settlement?.Faction?.leader != null)
                {
                    slate.Set("asker", settlement.Faction.leader);
                }
                else if (faction.leader != null)
                {
                    slate.Set("asker", faction.leader);
                }
                else if (string.Equals(questDef.defName, "RimChat_AIQuest", StringComparison.Ordinal))
                {
                    Pawn randomPawn = PawnsFinder.AllMapsWorldAndTemporary_Alive
                        .Where(p => p.Faction == faction && p.RaceProps.Humanlike && !p.Dead)
                        .RandomElementWithFallback();
                    if (randomPawn != null)
                    {
                        slate.Set("asker", randomPawn);
                    }
                }
                else if (isItemStashQuest)
                {
                    slate.Set("askerIsNull", true);
                }
            }

            if (string.Equals(questDef.defName, "AncientComplex_Mission", StringComparison.Ordinal))
            {
                int colonistCount = slate.Exists("colonistCount") ? slate.Get<int>("colonistCount") : -1;
                if (colonistCount <= 0)
                {
                    int freeColonists = playerMap.mapPawns?.FreeColonistsSpawnedCount ?? 3;
                    slate.Set("colonistCount", Math.Max(2, Math.Min(freeColonists, 5)));
                    if (!slate.Exists("points"))
                    {
                        slate.Set("points", StorytellerUtility.DefaultThreatPointsNow(playerMap));
                    }
                }

                if (ModsConfig.IdeologyActive && !slate.Exists("relic") && Faction.OfPlayer.ideos?.PrimaryIdeo != null)
                {
                    var relic = Faction.OfPlayer.ideos.PrimaryIdeo.PreceptsListForReading.OfType<Precept_Relic>().FirstOrDefault();
                    if (relic != null)
                    {
                        slate.Set("relic", relic);
                    }
                }
            }

            if (isItemStashQuest)
            {
                float currentPoints = slate.Exists("points") ? slate.Get<float>("points") : 0f;
                float minPoints = Math.Max(800f, questDef.rootMinPoints);
                if (currentPoints < minPoints)
                {
                    currentPoints = Math.Max(StorytellerUtility.DefaultThreatPointsNow(playerMap), minPoints);
                    slate.Set("points", currentPoints);
                }

                if (!slate.Exists("asker"))
                {
                    if (faction?.leader != null)
                    {
                        slate.Set("asker", faction.leader);
                        slate.Set("asker_factionLeader", true);
                    }
                    else
                    {
                        slate.Set("askerIsNull", true);
                    }
                }
            }

            if (string.Equals(questDef.defName, "Mission_BanditCamp", StringComparison.Ordinal))
            {
                Faction enemyFaction = slate.Exists("enemyFaction") ? slate.Get<Faction>("enemyFaction") : null;
                if (enemyFaction == null)
                {
                    enemyFaction = Find.FactionManager.RandomEnemyFaction(true, true, true, TechLevel.Undefined);
                    if (enemyFaction != null)
                    {
                        slate.Set("enemyFaction", enemyFaction);
                    }
                }

                if (enemyFaction != null && !slate.Exists("enemiesLabel"))
                {
                    slate.Set("enemiesLabel", enemyFaction.Name);
                }

                if (!slate.Exists("timeoutTicks"))
                {
                    slate.Set("timeoutTicks", 10 * 60000);
                }

                if (!slate.Exists("points"))
                {
                    slate.Set("points", StorytellerUtility.DefaultThreatPointsNow(playerMap));
                }
            }

            if (string.Equals(questDef.defName, PawnLendDefName, StringComparison.Ordinal) &&
                !TryBuildPawnLendSlate(faction, slate, playerMap, out code, out message))
            {
                return false;
            }

            if (slate.Exists("faction") && !slate.Exists("faction_name"))
            {
                Faction slateFaction = slate.Get<Faction>("faction");
                if (slateFaction != null)
                {
                    slate.Set("faction_name", slateFaction.Name);
                }
            }

            if (!slate.Exists("points") && questDef.rootMinPoints > 0f)
            {
                slate.Set("points", StorytellerUtility.DefaultThreatPointsNow(playerMap));
            }

            if (string.Equals(questDef.defName, "RimChat_AIQuest", StringComparison.Ordinal))
            {
                if (!slate.Exists("title"))
                {
                    slate.Set("title", $"Task from {faction?.Name ?? "Unknown"}");
                }
                if (!slate.Exists("description"))
                {
                    slate.Set("description", $"We have received a communication from {faction?.Name ?? "Unknown"}. (AI failed to generate description)");
                }
            }

            return true;
        }

        private static bool TryBuildPawnLendSlate(Faction faction, RimWorld.QuestGen.Slate slate, Map playerMap, out string code, out string message)
        {
            code = "allowed";
            message = "Allowed";

            if (faction == null)
            {
                code = "pawnlend_invalid_faction";
                message = "PawnLend requires a valid faction context.";
                return false;
            }

            Pawn asker = ResolvePawnLendAsker(faction, slate);
            if (asker == null)
            {
                code = "pawnlend_missing_asker";
                message = $"Quest '{PawnLendDefName}' requires a valid faction leader or settlement-backed asker.";
                return false;
            }

            slate.Set("asker", asker);
            slate.Set("asker_nameFull", asker.Name?.ToStringFull ?? asker.LabelShortCap ?? asker.LabelCap);
            slate.Set("asker_faction_name", faction.Name);
            slate.Set("asker_faction_leaderTitle", ResolveLeaderTitle(asker, faction));
            slate.Set("asker_objective", ResolvePawnLendObjective(faction, playerMap));
            slate.Set("dutyDescription", ResolvePawnLendDutyDescription(faction, playerMap));

            int lendCount = ResolvePositiveInt(slate, "colonistCountToLend");
            if (lendCount <= 0)
            {
                lendCount = ResolvePawnLendCount(playerMap);
                if (lendCount <= 0)
                {
                    code = "pawnlend_no_lendable_colonist";
                    message = $"Quest '{PawnLendDefName}' requires at least one lendable colonist on the current map.";
                    return false;
                }
                slate.Set("colonistCountToLend", lendCount);
            }

            int lendDays = ResolvePositiveInt(slate, "lendForDays");
            if (lendDays <= 0)
            {
                lendDays = ResolvePawnLendDays(faction);
                if (lendDays <= 0)
                {
                    code = "pawnlend_invalid_duration";
                    message = $"Quest '{PawnLendDefName}' requires a positive lend duration.";
                    return false;
                }
                slate.Set("lendForDays", lendDays);
            }

            if (!slate.Exists("WillSendShuttle"))
            {
                slate.Set("WillSendShuttle", ResolvePawnLendWillSendShuttle(faction));
            }

            for (int i = 0; i < PawnLendRequiredKeys.Length; i++)
            {
                string key = PawnLendRequiredKeys[i];
                if (!slate.Exists(key) || IsInvalidPawnLendValue(slate, key))
                {
                    code = "pawnlend_contract_missing";
                    message = $"Quest '{PawnLendDefName}' is missing required runtime field '{key}'.";
                    return false;
                }
            }

            return true;
        }

        private static Pawn ResolvePawnLendAsker(Faction faction, RimWorld.QuestGen.Slate slate)
        {
            if (slate.Exists("asker"))
            {
                Pawn existing = slate.Get<Pawn>("asker");
                if (existing != null)
                {
                    return existing;
                }
            }

            Settlement settlement = slate.Exists("settlement") ? slate.Get<Settlement>("settlement") : null;
            if (settlement?.Faction?.leader != null)
            {
                return settlement.Faction.leader;
            }

            return faction.leader;
        }

        private static string ResolveLeaderTitle(Pawn asker, Faction faction)
        {
            string title = asker?.kindDef?.label ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(title))
            {
                return title;
            }

            string leaderTitle = faction?.def?.leaderTitle ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(leaderTitle))
            {
                return leaderTitle;
            }

            return "leader";
        }

        private static string ResolvePawnLendObjective(Faction faction, Map map)
        {
            if (map?.Parent?.LabelCap != null)
            {
                return $"support operations near {map.Parent.LabelCap}";
            }

            return $"support {faction.Name}";
        }

        private static string ResolvePawnLendDutyDescription(Faction faction, Map map)
        {
            if (faction?.def?.techLevel >= TechLevel.Spacer)
            {
                return "reinforce shuttle maintenance crews";
            }

            if ((map?.mapPawns?.FreeColonistsSpawnedCount ?? 0) >= 4)
            {
                return "reinforce field logistics";
            }

            return "reinforce urgent labor crews";
        }

        private static int ResolvePawnLendCount(Map map)
        {
            int freeColonists = map?.mapPawns?.FreeColonistsSpawnedCount ?? 0;
            if (freeColonists <= 0)
            {
                return 0;
            }

            return Math.Max(1, Math.Min(freeColonists / 2, 3));
        }

        private static int ResolvePawnLendDays(Faction faction)
        {
            if (faction?.def?.techLevel >= TechLevel.Spacer)
            {
                return 6;
            }

            return 10;
        }

        private static bool ResolvePawnLendWillSendShuttle(Faction faction)
        {
            return faction?.def?.techLevel >= TechLevel.Industrial;
        }

        private static int ResolvePositiveInt(RimWorld.QuestGen.Slate slate, string key)
        {
            if (!slate.Exists(key))
            {
                return 0;
            }

            object value = slate.Get<object>(key);
            if (value is int i)
            {
                return i > 0 ? i : 0;
            }
            if (value is float f)
            {
                int rounded = Mathf.RoundToInt(f);
                return rounded > 0 ? rounded : 0;
            }
            if (value is string s && int.TryParse(s, out int parsed))
            {
                return parsed > 0 ? parsed : 0;
            }

            return 0;
        }

        private static bool IsInvalidPawnLendValue(RimWorld.QuestGen.Slate slate, string key)
        {
            object value = slate.Get<object>(key);
            if (value == null)
            {
                return true;
            }

            if (value is string text)
            {
                return string.IsNullOrWhiteSpace(text);
            }

            if (value is int i)
            {
                return i <= 0;
            }

            return false;
        }

        private static object ResolveParameter(string key, object value)
        {
            if (value == null)
            {
                return null;
            }

            if (!(value is string strValue))
            {
                return value;
            }

            if (key.ToLowerInvariant().Contains("faction"))
            {
                Faction faction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.Name == strValue || f.def.defName == strValue);
                if (faction != null)
                {
                    return faction;
                }
            }

            if (key.ToLowerInvariant().Contains("pawn") || key.Equals("asker", StringComparison.OrdinalIgnoreCase))
            {
                Pawn pawn = PawnsFinder.AllMapsWorldAndTemporary_Alive.FirstOrDefault(p => p.Name != null && p.Name.ToStringFull == strValue);
                if (pawn != null)
                {
                    return pawn;
                }
            }

            if (float.TryParse(strValue, out float fResult))
            {
                return fResult;
            }

            return value;
        }
    }
}
