using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using RimDiplomacy.Config;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimDiplomacy.Persistence
{
    /// <summary>
    /// Responsibility: append player-colony context blocks for RPG pawn prompts.
    /// </summary>
    public partial class PromptPersistenceService
    {
        private const int MaxInventorySummaryItems = 8;
        private const int MaxNativeAlertItems = 8;
        private const int MaxQueuedJobItems = 4;
        private static readonly FieldInfo NativeActiveAlertsField =
            typeof(AlertsReadout).GetField("activeAlerts", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo JobQueueField =
            typeof(Pawn_JobTracker).GetField("jobQueue", BindingFlags.Instance | BindingFlags.NonPublic);

        private void AppendPlayerColonyContextIfEnabled(StringBuilder sb, Pawn pawn, RpgSceneParamSwitchesConfig switches)
        {
            if (sb == null || switches == null || pawn?.Faction != Faction.OfPlayer)
            {
                return;
            }

            if (switches.IncludeRecentJobState)
            {
                AppendPlayerRecentJobState(sb, pawn);
            }

            if (switches.IncludeAttributeLevels)
            {
                AppendPlayerAttributeLevels(sb, pawn);
            }

            List<Map> homeMaps = GetPlayerHomeMaps();
            if (homeMaps.Count == 0)
            {
                return;
            }

            if (switches.IncludeColonyInventorySummary)
            {
                AppendPlayerColonyInventorySummary(sb, homeMaps);
            }

            if (switches.IncludeHomeAlerts)
            {
                AppendPlayerHomeAlerts(sb);
            }
        }

        private List<Map> GetPlayerHomeMaps()
        {
            return Find.Maps?
                .Where(map => map != null && map.IsPlayerHome)
                .ToList() ?? new List<Map>();
        }

        private void AppendPlayerColonyInventorySummary(StringBuilder sb, List<Map> homeMaps)
        {
            Dictionary<ThingDef, int> stock = AggregateColonyStock(homeMaps);
            if (stock.Count == 0)
            {
                return;
            }

            int silver = homeMaps.Sum(map => map?.resourceCounter?.Silver ?? 0);
            float nutrition = homeMaps.Sum(map => map?.resourceCounter?.TotalHumanEdibleNutrition ?? 0f);
            int colonists = homeMaps.Sum(map => map?.mapPawns?.FreeColonistsSpawnedCount ?? 0);
            float foodDays = nutrition / Math.Max(1f, colonists * 1.6f);

            List<string> topStocks = stock
                .OrderByDescending(pair => pair.Value)
                .Take(MaxInventorySummaryItems)
                .Select(pair => $"{pair.Key.LabelCap}:{pair.Value}")
                .ToList();

            sb.AppendLine(
                $"Colony Inventory Summary: Silver={silver}, Food~{foodDays:F1} days, TopStocks={string.Join(", ", topStocks)}");
        }

        private Dictionary<ThingDef, int> AggregateColonyStock(List<Map> homeMaps)
        {
            var aggregate = new Dictionary<ThingDef, int>();
            for (int i = 0; i < homeMaps.Count; i++)
            {
                Dictionary<ThingDef, int> counted = homeMaps[i]?.resourceCounter?.AllCountedAmounts;
                if (counted == null)
                {
                    continue;
                }

                foreach (KeyValuePair<ThingDef, int> pair in counted)
                {
                    if (pair.Key == null || pair.Value <= 0)
                    {
                        continue;
                    }

                    aggregate.TryGetValue(pair.Key, out int existing);
                    aggregate[pair.Key] = existing + pair.Value;
                }
            }

            return aggregate;
        }

        private void AppendPlayerHomeAlerts(StringBuilder sb)
        {
            List<string> alerts = GetNativeActiveAlerts();
            string line = alerts.Count > 0
                ? string.Join("; ", alerts)
                : "No active native alert";
            sb.AppendLine($"Home World Alerts: {line}");
        }

        private List<string> GetNativeActiveAlerts()
        {
            var labels = new List<string>();
            try
            {
                AlertsReadout readout = Find.Alerts;
                if (readout == null)
                {
                    return labels;
                }

                IEnumerable activeAlerts = NativeActiveAlertsField?.GetValue(readout) as IEnumerable;
                if (activeAlerts == null)
                {
                    return labels;
                }

                foreach (object item in activeAlerts)
                {
                    string label = BuildNativeAlertLabel(item as Alert);
                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        labels.Add(label);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimDiplomacy] Failed to reflect native alerts: {ex.Message}");
            }

            return labels.Distinct().Take(MaxNativeAlertItems).ToList();
        }

        private string BuildNativeAlertLabel(Alert alert)
        {
            if (alert == null)
            {
                return string.Empty;
            }

            string label = alert.Label ?? string.Empty;
            if (string.IsNullOrWhiteSpace(label))
            {
                label = alert.GetLabel() ?? string.Empty;
            }

            label = label.Trim();
            if (label.Length == 0)
            {
                return string.Empty;
            }

            return $"[{alert.Priority}] {label}";
        }

        private void AppendPlayerRecentJobState(StringBuilder sb, Pawn pawn)
        {
            if (pawn?.jobs == null)
            {
                return;
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

            if (parts.Count > 0)
            {
                sb.AppendLine($"Recent Job State: {string.Join(" | ", parts)}");
            }
        }

        private string BuildJobSummary(Job job)
        {
            if (job?.def == null)
            {
                return string.Empty;
            }

            string label = job.def.label ?? job.def.defName;
            if (job.targetA.HasThing && job.targetA.Thing != null)
            {
                return $"{label}({job.targetA.Thing.LabelShortCap})";
            }

            if (job.targetA.Cell.IsValid)
            {
                return $"{label}({job.targetA.Cell})";
            }

            return label;
        }

        private List<string> GetQueuedJobSummaries(Pawn pawn)
        {
            var results = new List<string>();
            IEnumerable queue = JobQueueField?.GetValue(pawn?.jobs) as IEnumerable;
            if (queue == null)
            {
                return results;
            }

            foreach (object queued in queue)
            {
                Job job = ExtractQueuedJob(queued);
                string summary = BuildJobSummary(job);
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    results.Add(summary);
                }

                if (results.Count >= MaxQueuedJobItems)
                {
                    break;
                }
            }

            return results;
        }

        private Job ExtractQueuedJob(object queued)
        {
            if (queued == null)
            {
                return null;
            }

            Type type = queued.GetType();
            FieldInfo field = type.GetField("job", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field?.GetValue(queued) is Job fieldJob)
            {
                return fieldJob;
            }

            PropertyInfo property = type.GetProperty("job", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property?.GetValue(queued, null) as Job;
        }

        private void AppendPlayerAttributeLevels(StringBuilder sb, Pawn pawn)
        {
            if (pawn?.health?.capacities == null)
            {
                return;
            }

            var parts = new List<string>();
            AppendCapacityPart(parts, pawn, PawnCapacityDefOf.Consciousness);
            AppendCapacityPart(parts, pawn, PawnCapacityDefOf.Moving);
            AppendCapacityPart(parts, pawn, PawnCapacityDefOf.Manipulation);
            AppendCapacityPart(parts, pawn, PawnCapacityDefOf.Talking);
            AppendCapacityPart(parts, pawn, PawnCapacityDefOf.Sight);
            AppendCapacityPart(parts, pawn, PawnCapacityDefOf.Hearing);

            if (parts.Count > 0)
            {
                sb.AppendLine($"Pawn Attribute Levels: {string.Join(", ", parts)}");
            }
        }

        private void AppendCapacityPart(List<string> parts, Pawn pawn, PawnCapacityDef capacity)
        {
            if (parts == null || pawn?.health?.capacities == null || capacity == null)
            {
                return;
            }

            float value = pawn.health.capacities.GetLevel(capacity);
            parts.Add($"{capacity.defName}:{value:P0}");
        }
    }
}
