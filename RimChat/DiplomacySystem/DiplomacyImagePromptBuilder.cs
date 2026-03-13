using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using RimChat.Config;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>/// Dependencies: RimWorld faction/pawn data and image prompt template settings.
 /// Responsibility: build final send_image prompt by combining template, extra prompt, and leader profile block.
 ///</summary>
    public static class DiplomacyImagePromptBuilder
    {
        public static string BuildPrompt(
            Faction faction,
            DiplomacyImagePromptTemplate template,
            string extraPrompt)
        {
            var sb = new StringBuilder(1024);
            string templateText = (template?.Text ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(templateText))
            {
                sb.AppendLine(templateText);
            }

            string extra = (extraPrompt ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(extra))
            {
                sb.AppendLine();
                sb.AppendLine(extra);
            }

            sb.AppendLine();
            sb.AppendLine(BuildLeaderProfileBlock(faction));
            return sb.ToString().Trim();
        }

        public static string BuildLeaderProfileBlock(Faction faction)
        {
            var sb = new StringBuilder(768);
            Pawn leader = faction?.leader;

            sb.AppendLine("[LeaderProfile]");
            sb.AppendLine($"Identity: {BuildIdentityLine(faction, leader)}");
            sb.AppendLine($"Appearance: {BuildAppearanceLine(leader)}");
            sb.AppendLine($"FactionContext: {BuildFactionContextLine(faction)}");
            return sb.ToString().TrimEnd();
        }

        private static string BuildIdentityLine(Faction faction, Pawn leader)
        {
            if (leader == null)
            {
                string fallbackName = string.IsNullOrWhiteSpace(faction?.Name) ? "UnknownFaction" : faction.Name;
                return $"Leader data unavailable; faction fallback={fallbackName}; keep portrayal consistent with known faction profile.";
            }

            string leaderName = leader.Name?.ToStringFull ?? leader.LabelShortCap ?? "Unknown";
            string title = ResolveLeaderTitle(leader, faction);
            string race = ResolveRaceLabel(leader);
            string gender = leader.gender.ToString();
            return $"name={leaderName}; title={title}; race={race}; gender={gender}.";
        }

        private static string BuildAppearanceLine(Pawn leader)
        {
            if (leader == null)
            {
                return "No live leader pawn available; infer visuals from faction technology, icon style, and known cultural background.";
            }

            string bodyType = leader.story?.bodyType?.label ?? "unknown";
            string hair = leader.story?.hairDef?.label ?? "unknown";
            string beard = ResolveBeardLabel(leader);
            string apparel = ResolveApparelSummary(leader);

            return $"bodyType={bodyType}; hair={hair}; beard={beard}; visibleClothing={apparel}.";
        }

        private static string BuildFactionContextLine(Faction faction)
        {
            if (faction == null)
            {
                return "Unknown faction context.";
            }

            string factionType = faction.def?.label ?? faction.Name ?? "unknown";
            string techLevel = faction.def?.techLevel.ToString() ?? "Unknown";
            string relation = faction.RelationKindWith(Faction.OfPlayer).ToString();
            string background = (faction.def?.description ?? string.Empty).Trim();
            if (background.Length > 240)
            {
                background = background.Substring(0, 240).TrimEnd() + "...";
            }
            if (string.IsNullOrWhiteSpace(background))
            {
                background = "No explicit faction description available; keep visuals lore-consistent with technology and diplomacy state.";
            }

            return $"factionType={factionType}; techLevel={techLevel}; playerRelation={relation}; background={background}";
        }

        private static string ResolveLeaderTitle(Pawn leader, Faction faction)
        {
            string leaderTitle = (faction?.def?.leaderTitle ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(leaderTitle))
            {
                return leaderTitle;
            }

            string kind = leader?.kindDef?.label ?? string.Empty;
            return string.IsNullOrWhiteSpace(kind) ? "leader" : kind.Trim();
        }

        private static string ResolveRaceLabel(Pawn leader)
        {
            if (leader == null)
            {
                return "unknown";
            }

            string xenotype = ResolveXenotypeLabel(leader);
            string race = leader.def?.label ?? "unknown";
            if (string.IsNullOrWhiteSpace(xenotype))
            {
                return race;
            }

            return $"{race}/{xenotype}";
        }

        private static string ResolveXenotypeLabel(Pawn leader)
        {
            if (leader?.genes == null)
            {
                return string.Empty;
            }

            string label = leader.genes.XenotypeLabelCap ?? leader.genes.xenotypeName;
            return string.IsNullOrWhiteSpace(label) ? string.Empty : label.Trim();
        }

        private static string ResolveBeardLabel(Pawn leader)
        {
            if (leader == null)
            {
                return "none";
            }

            object beardDef = GetMemberValue(leader.story, "beardDef")
                ?? GetMemberValue(leader.style, "beardDef");
            if (beardDef == null)
            {
                return "none";
            }

            string label = ReadDefLabel(beardDef);
            return string.IsNullOrWhiteSpace(label) ? "none" : label.Trim();
        }

        private static string ResolveApparelSummary(Pawn leader)
        {
            if (leader?.apparel?.WornApparel == null || leader.apparel.WornApparel.Count == 0)
            {
                return "none";
            }

            IEnumerable<string> labels = leader.apparel.WornApparel
                .Where(item => item != null)
                .Select(item => item.LabelCap)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Take(6);

            string summary = string.Join(", ", labels);
            return string.IsNullOrWhiteSpace(summary) ? "none" : summary;
        }

        private static object GetMemberValue(object target, string memberName)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName))
            {
                return null;
            }

            const BindingFlags flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            try
            {
                PropertyInfo prop = target.GetType().GetProperty(memberName, flags);
                if (prop != null)
                {
                    return prop.GetValue(target, null);
                }

                FieldInfo field = target.GetType().GetField(memberName, flags);
                if (field != null)
                {
                    return field.GetValue(target);
                }
            }
            catch
            {
            }

            return null;
        }

        private static string ReadDefLabel(object defObject)
        {
            if (defObject == null)
            {
                return string.Empty;
            }

            object label = GetMemberValue(defObject, "label");
            if (label is string labelText && !string.IsNullOrWhiteSpace(labelText))
            {
                return labelText;
            }

            object defName = GetMemberValue(defObject, "defName");
            return defName as string ?? string.Empty;
        }
    }
}
