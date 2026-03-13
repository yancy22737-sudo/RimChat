using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: RimWorld pawn runtime data (story, apparel, health, equipment, needs).
    /// Responsibility: build hidden selfie prompt injection blocks from negotiator profile switches.
    /// </summary>
    public static class SelfiePromptInjectionBuilder
    {
        public sealed class Switches
        {
            public bool IncludeApparel = true;
            public bool IncludeBodyType = true;
            public bool IncludeHair = true;
            public bool IncludeWeapon = true;
            public bool IncludeImplants = true;
            public bool IncludeStatus = true;
        }

        private const int MaxListItems = 12;
        private const int MaxOutputChars = 2600;

        public static string Build(Pawn pawn, Faction faction, Switches switches)
        {
            if (pawn == null)
            {
                return string.Empty;
            }

            Switches active = switches ?? new Switches();
            var sb = new StringBuilder(1024);
            sb.AppendLine("[SelfieAutoInjectedProfile]");
            sb.AppendLine($"Name: {pawn.LabelShortCap}");
            sb.AppendLine($"Gender: {pawn.gender}");
            sb.AppendLine($"Age: {pawn.ageTracker?.AgeBiologicalYears ?? 0}");
            sb.AppendLine($"Faction: {faction?.Name ?? "Unknown"}");

            if (active.IncludeBodyType)
            {
                AppendBodyType(sb, pawn);
            }

            if (active.IncludeHair)
            {
                AppendHair(sb, pawn);
            }

            if (active.IncludeApparel)
            {
                AppendApparel(sb, pawn);
            }

            if (active.IncludeWeapon)
            {
                AppendWeapon(sb, pawn);
            }

            if (active.IncludeImplants)
            {
                AppendImplants(sb, pawn);
            }

            if (active.IncludeStatus)
            {
                AppendStatus(sb, pawn);
            }

            string output = sb.ToString().TrimEnd();
            if (output.Length <= MaxOutputChars)
            {
                return output;
            }

            return output.Substring(0, MaxOutputChars) + "\n[Truncated]";
        }

        private static void AppendBodyType(StringBuilder sb, Pawn pawn)
        {
            string bodyType = pawn.story?.bodyType?.label ?? "unknown";
            string race = pawn.def?.label ?? "unknown";
            sb.AppendLine($"BodyType: {bodyType}");
            sb.AppendLine($"Race: {race}");
        }

        private static void AppendHair(StringBuilder sb, Pawn pawn)
        {
            string hair = pawn.story?.hairDef?.label ?? "unknown";
            string beard = ResolveBeardLabel(pawn);
            sb.AppendLine($"Hair: {hair}");
            sb.AppendLine($"Beard: {beard}");
        }

        private static string ResolveBeardLabel(Pawn pawn)
        {
            object beard = pawn?.story?.GetType().GetProperty("beardDef")?.GetValue(pawn.story)
                ?? pawn?.style?.GetType().GetProperty("beardDef")?.GetValue(pawn.style);
            string label = beard?.GetType().GetProperty("label")?.GetValue(beard)?.ToString();
            if (!string.IsNullOrWhiteSpace(label))
            {
                return label;
            }

            string defName = beard?.GetType().GetProperty("defName")?.GetValue(beard)?.ToString();
            return string.IsNullOrWhiteSpace(defName) ? "none" : defName;
        }

        private static void AppendApparel(StringBuilder sb, Pawn pawn)
        {
            if (pawn?.apparel?.WornApparel == null || pawn.apparel.WornApparel.Count == 0)
            {
                sb.AppendLine("Apparel: none");
                return;
            }

            List<string> items = pawn.apparel.WornApparel
                .Where(item => item != null)
                .Select(item => item.LabelCap)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Take(MaxListItems)
                .ToList();
            sb.AppendLine($"Apparel: {string.Join(", ", items)}");
        }

        private static void AppendWeapon(StringBuilder sb, Pawn pawn)
        {
            string primary = pawn?.equipment?.Primary?.LabelCap;
            if (string.IsNullOrWhiteSpace(primary))
            {
                sb.AppendLine("Weapon: none");
                return;
            }

            sb.AppendLine($"Weapon: {primary}");
        }

        private static void AppendImplants(StringBuilder sb, Pawn pawn)
        {
            if (pawn?.health?.hediffSet?.hediffs == null)
            {
                sb.AppendLine("Implants: none");
                return;
            }

            List<string> implants = pawn.health.hediffSet.hediffs
                .Where(hediff => hediff != null)
                .Where(IsImplantLike)
                .Select(hediff => hediff.LabelCap)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Distinct()
                .Take(MaxListItems)
                .ToList();

            sb.AppendLine(implants.Count > 0
                ? $"Implants: {string.Join(", ", implants)}"
                : "Implants: none");
        }

        private static bool IsImplantLike(Hediff hediff)
        {
            if (hediff is Hediff_AddedPart)
            {
                return true;
            }

            if (hediff.def?.addedPartProps != null)
            {
                return true;
            }

            string label = hediff.LabelCap ?? string.Empty;
            return label.IndexOf("implant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   label.IndexOf("bionic", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AppendStatus(StringBuilder sb, Pawn pawn)
        {
            float mood = pawn.needs?.mood?.CurLevelPercentage ?? -1f;
            float health = pawn.health?.summaryHealth?.SummaryHealthPercent ?? -1f;
            string moodText = mood >= 0f ? mood.ToString("P0") : "unknown";
            string healthText = health >= 0f ? health.ToString("P0") : "unknown";
            sb.AppendLine($"Status: Mood={moodText}, Health={healthText}");

            if (pawn.needs?.AllNeeds != null)
            {
                List<string> needs = pawn.needs.AllNeeds
                    .Where(need => need?.def != null)
                    .Take(8)
                    .Select(need => $"{need.def.label}:{need.CurLevelPercentage:P0}")
                    .ToList();
                if (needs.Count > 0)
                {
                    sb.AppendLine($"Needs: {string.Join(", ", needs)}");
                }
            }

            if (pawn.health?.hediffSet?.hediffs != null)
            {
                List<string> conditions = pawn.health.hediffSet.hediffs
                    .Where(h => h != null && h.Visible)
                    .Take(8)
                    .Select(h => h.LabelCap)
                    .Where(label => !string.IsNullOrWhiteSpace(label))
                    .ToList();
                if (conditions.Count > 0)
                {
                    sb.AppendLine($"VisibleConditions: {string.Join(", ", conditions)}");
                }
            }
        }
    }
}
