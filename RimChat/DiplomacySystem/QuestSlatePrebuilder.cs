using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimChat.DiplomacySystem
{
    internal static class QuestSlatePrebuilder
    {
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
