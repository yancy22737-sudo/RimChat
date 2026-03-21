using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimChat.Config;
using RimWorld;
using Verse;

namespace RimChat.Persistence
{
    /// <summary>
    /// Dependencies: DialogueScenarioContext, RpgSceneParamSwitchesConfig, pawn relation/job runtime APIs.
    /// Responsibility: build expanded RPG pawn profile and bilateral social summary prompt variables.
    /// </summary>
    public partial class PromptPersistenceService
    {
        private string BuildPawnProfileVariableText(Pawn pawn, DialogueScenarioContext context, EnvironmentPromptConfig envConfig)
        {
            if (pawn == null)
            {
                return "No pawn context.";
            }

            List<string> lines = BuildBasePawnProfileLines(pawn);
            if (context?.IsRpg == true)
            {
                AppendRpgProfileExtensions(
                    lines,
                    pawn,
                    envConfig?.RpgSceneParamSwitches ?? new RpgSceneParamSwitchesConfig());
            }

            return string.Join("\n", lines);
        }

        private static List<string> BuildBasePawnProfileLines(Pawn pawn)
        {
            float mood = pawn.needs?.mood?.CurLevelPercentage ?? -1f;
            float health = pawn.health?.summaryHealth?.SummaryHealthPercent ?? -1f;
            string moodText = mood >= 0f ? $"{mood:P0}" : "N/A";
            string healthText = health >= 0f ? $"{health:P0}" : "N/A";
            return new List<string>
            {
                $"Name: {pawn.LabelShortCap}",
                $"Kind: {pawn.KindLabel}",
                $"Faction: {pawn.Faction?.Name ?? "None"}",
                $"Mood: {moodText}",
                $"Health: {healthText}"
            };
        }

        private void AppendRpgProfileExtensions(
            List<string> lines,
            Pawn pawn,
            RpgSceneParamSwitchesConfig switches)
        {
            if (switches.IncludeRecentJobState)
            {
                string jobLine = BuildRecentJobStateLine(pawn);
                if (!string.IsNullOrWhiteSpace(jobLine))
                {
                    lines.Add(jobLine);
                }
            }

            if (switches.IncludeNeeds)
            {
                AddProfileLineFromBuilder(lines, pawn, AppendRpgNeeds);
            }

            if (switches.IncludeHediffs)
            {
                AddProfileLineFromBuilder(lines, pawn, AppendRpgHediffs);
            }

            if (switches.IncludeRecentEvents)
            {
                AddProfileLineFromBuilder(lines, pawn, AppendRpgRecentMemories);
            }

            if (switches.IncludeGenes)
            {
                AddProfileLineFromBuilder(lines, pawn, AppendRpgGenes);
            }

            if (switches.IncludeAttributeLevels)
            {
                AddProfileLineFromBuilder(lines, pawn, AppendPlayerAttributeLevels);
            }

            AppendRpgColonyProfileExtensions(lines, pawn, switches);
        }

        private void AppendRpgColonyProfileExtensions(
            List<string> lines,
            Pawn pawn,
            RpgSceneParamSwitchesConfig switches)
        {
            if (pawn?.Faction != Faction.OfPlayer || switches == null)
            {
                return;
            }

            if (switches.IncludeColonyInventorySummary)
            {
                AddProfileLineFromBuilder(lines, sb =>
                {
                    List<Map> homeMaps = GetPlayerHomeMaps();
                    if (homeMaps.Count > 0)
                    {
                        AppendPlayerColonyInventorySummary(sb, homeMaps);
                    }
                });
            }

            if (switches.IncludeHomeAlerts)
            {
                AddProfileLineFromBuilder(lines, AppendPlayerHomeAlerts);
            }
        }

        private static string BuildPairSocialSummary(Pawn initiator, Pawn target, string kinshipValue, string romanceState)
        {
            if (initiator == null || target == null)
            {
                return string.Empty;
            }

            string initiatorName = initiator.LabelShortCap ?? "Initiator";
            string targetName = target.LabelShortCap ?? "Target";
            int initiatorOpinion = initiator.relations?.OpinionOf(target) ?? 0;
            int targetOpinion = target.relations?.OpinionOf(initiator) ?? 0;
            string directRelations = BuildPairDirectRelationsSummary(initiator, target);
            string initiatorGoodwill = BuildFactionGoodwillSummary(initiator.Faction);
            string targetGoodwill = BuildFactionGoodwillSummary(target.Faction);
            return
                $"Opinions: {initiatorName}->{targetName}={initiatorOpinion}, {targetName}->{initiatorName}={targetOpinion}; " +
                $"DirectRelations: {directRelations}; Kinship={kinshipValue}; Romance={romanceState}; " +
                $"FactionGoodwillToPlayer: {initiatorName}={initiatorGoodwill}, {targetName}={targetGoodwill}.";
        }

        private static string BuildPairDirectRelationsSummary(Pawn first, Pawn second)
        {
            var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddDirectRelationLabels(labels, first, second);
            AddDirectRelationLabels(labels, second, first);
            return labels.Count == 0
                ? "none"
                : string.Join(", ", labels.OrderBy(item => item));
        }

        private static void AddDirectRelationLabels(HashSet<string> labels, Pawn fromPawn, Pawn toPawn)
        {
            if (labels == null || fromPawn?.relations?.DirectRelations == null || toPawn == null)
            {
                return;
            }

            for (int i = 0; i < fromPawn.relations.DirectRelations.Count; i++)
            {
                DirectPawnRelation relation = fromPawn.relations.DirectRelations[i];
                if (relation?.otherPawn != toPawn || relation.def == null)
                {
                    continue;
                }

                string label = relation.def.label ?? relation.def.defName;
                if (!string.IsNullOrWhiteSpace(label))
                {
                    labels.Add(label.Trim());
                }
            }
        }

        private static string BuildFactionGoodwillSummary(Faction faction)
        {
            if (faction == null)
            {
                return "N/A";
            }

            if (faction == Faction.OfPlayer || faction.IsPlayer)
            {
                return "player";
            }

            return faction.PlayerGoodwill.ToString();
        }

        private string BuildRecentJobStateLine(Pawn pawn)
        {
            if (pawn?.jobs == null)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            string current = BuildJobSummary(pawn.CurJob);
            if (!string.IsNullOrWhiteSpace(current))
            {
                parts.Add($"Current={current}");
            }

            string duty = pawn.mindState?.duty?.def?.defName;
            if (!string.IsNullOrWhiteSpace(duty))
            {
                parts.Add($"Duty={duty}");
            }

            List<string> queued = GetQueuedJobSummaries(pawn);
            if (queued.Count > 0)
            {
                parts.Add($"Queued={string.Join(" -> ", queued)}");
            }

            return parts.Count == 0
                ? string.Empty
                : $"Recent Job State: {string.Join(" | ", parts)}";
        }

        private static void AddProfileLineFromBuilder(
            List<string> lines,
            Pawn pawn,
            Action<StringBuilder, Pawn> appendBuilder)
        {
            if (lines == null || pawn == null || appendBuilder == null)
            {
                return;
            }

            var sb = new StringBuilder();
            appendBuilder(sb, pawn);
            string text = sb.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                lines.Add(text);
            }
        }

        private static void AddProfileLineFromBuilder(
            List<string> lines,
            Action<StringBuilder> appendBuilder)
        {
            if (lines == null || appendBuilder == null)
            {
                return;
            }

            var sb = new StringBuilder();
            appendBuilder(sb);
            string text = sb.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                lines.Add(text);
            }
        }
    }
}
