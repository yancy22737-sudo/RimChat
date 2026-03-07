using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace RimChat.Prompting
{
    /// <summary>
    /// Responsibility: build the shared RPG API action-definition prompt block for runtime injection and UI preview.
    /// Dependencies: RimWorld DefDatabase (ThoughtDef), Verse runtime.
    /// </summary>
    internal static class RpgApiPromptTextBuilder
    {
        private static readonly string[] SharedActionLines =
        {
            "- TryAffectSocialGoodwill: Change goodwill between your faction and player. Use when you have a intention to change the relationship. Required 'amount' (int).",
            "- RomanceAttempt: Force-set romantic relationship status with the interlocutor.",
            "- MarriageProposal: Force-set marriage status (spouse) with the interlocutor.",
            "- Breakup: Force-set breakup result (remove romance/marriage and apply ex relation).",
            "- Divorce: Force-set divorce result (remove spouse and set ex-spouse).",
            "- Date: Treated as a romantic status progression action.",
            "- ReduceResistance: If you are a prisoner, reduce your recruitment resistance. Required 'amount' (float/int).",
            "- ReduceWill: If you are a prisoner, reduce your enslavement will. Required 'amount' (float/int).",
            "- Recruit: Immediately join the player's faction (no parameters). Use when relation is warm and trust is stable, and you clearly express willingness to join. If your policy list uses numbered actions, this is Action 4.",
            "- TryTakeOrderedJob: Execute a job. Use 'defName': 'AttackMelee' to attack the interlocutor.",
            "- TriggerIncident: Trigger a game event (incident). Required 'defName'. Optional 'amount' for incident points. Examples: 'RaidEnemy', 'TraderCaravanArrival', 'TravelerGroup'.",
            "- GrantInspiration: Attempt to grant yourself an inspiration. Use when interlocutor inspire you through encouragement, new ideas, emotional impact, or strategic insight. 'defName' (InspirationDef). e.g.:Frenzy_Work/Frenzy_Go/Frenzy_Shoot/Inspired_Trade/Inspired_Recruitment/Inspired_Taming/Inspired_Surgery/Inspired_Creativity",
            "- ExitDialogue: End the current RPG conversation normally. Use when the conversation reaches a natural stopping point, the pawn needs to leave, resume work, rest, or simply has nothing more to say. Optional 'reason'. This is a soft, non-hostile ending and does not prevent future conversations. No cooldown is applied.",
            "- ExitDialogueCooldown: End the current RPG conversation and reject new chats for 1 day. Use when the pawn wants to disengage and be left alone due to anger, stress, fear, exhaustion, humiliation, annoyance, or emotional overwhelm. Optional 'reason'. This is a firm social refusal, not a routine ending, and should be used sparingly.",
            "- Guidance: Prefer ExitDialogue for polite or natural closure. Use ExitDialogueCooldown under hostility, harassment, repeated pressure, or clear refusal context."
        };

        public static void AppendActionDefinitions(StringBuilder sb)
        {
            if (sb == null)
            {
                return;
            }

            sb.AppendLine("=== AVAILABLE NPC ACTIONS ===");
            sb.AppendLine("You can trigger game effects by including them in the 'actions' array of your JSON output. Use only when you agree.");
            sb.AppendLine("Each action should be an object: { \"action\": \"ActionName\", \"defName\": \"OptionalDef\", \"amount\": 0 }");
            sb.AppendLine();
            sb.AppendLine($"- TryGainMemory: Add a thought memory to yourself. Use when you want to express a thought or emotion. Required 'defName'. Tendency guidance: around 80% chance once dialogue reaches 5-10 rounds. Valid examples: {BuildTryGainMemoryExamples()}.");

            for (int i = 0; i < SharedActionLines.Length; i++)
            {
                sb.AppendLine(SharedActionLines[i]);
            }

            sb.AppendLine();
        }

        private static string BuildTryGainMemoryExamples()
        {
            string[] preferred =
            {
                "Chitchat", "DeepTalk", "KindWords", "Slighted", "Insulted", "AteWithoutTable",
                "SleepDisturbed", "SleptOutside", "SleptInCold", "SleptInHeat", "GotSomeLovin", "Catharsis"
            };

            var names = new List<string>();
            for (int i = 0; i < preferred.Length; i++)
            {
                string defName = preferred[i];
                if (DefDatabase<ThoughtDef>.GetNamedSilentFail(defName) != null)
                {
                    names.Add(defName);
                }
            }

            return names.Count > 0 ? string.Join(", ", names) : "Chitchat, DeepTalk, KindWords, Insulted";
        }
    }
}
