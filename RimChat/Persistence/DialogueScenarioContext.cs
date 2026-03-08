using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimChat.Persistence
{
    /// <summary>/// Responsibility: carry channel/source/entity signals for environment-scene prompt matching.
 ///</summary>
    public class DialogueScenarioContext
    {
        public bool IsRpg;
        public bool IsProactive;
        public Faction Faction;
        public Pawn Initiator;
        public Pawn Target;
        public readonly HashSet<string> Tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static DialogueScenarioContext CreateDiplomacy(Faction faction, bool isProactive, IEnumerable<string> additionalTags = null)
        {
            var context = new DialogueScenarioContext
            {
                IsRpg = false,
                IsProactive = isProactive,
                Faction = faction
            };

            context.AddTag("channel:diplomacy");
            context.AddTag(isProactive ? "source:proactive" : "source:manual");
            context.AddTags(additionalTags);
            return context;
        }

        public static DialogueScenarioContext CreateRpg(Pawn initiator, Pawn target, bool isProactive, IEnumerable<string> additionalTags = null)
        {
            var context = new DialogueScenarioContext
            {
                IsRpg = true,
                IsProactive = isProactive,
                Initiator = initiator,
                Target = target,
                Faction = target?.Faction ?? initiator?.Faction
            };

            context.AddTag("channel:rpg");
            context.AddTag(isProactive ? "source:proactive" : "source:manual");
            context.AddTags(additionalTags);
            return context;
        }

        public void AddTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                return;
            }

            Tags.Add(tag.Trim().ToLowerInvariant());
        }

        public void AddTags(IEnumerable<string> tags)
        {
            if (tags == null)
            {
                return;
            }

            foreach (string tag in tags)
            {
                AddTag(tag);
            }
        }
    }
}
