using System;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: Verse.Scribe.
    /// Responsibility: central policy controls for RPG intent-action mapping and prompt schema upgrades.
    /// </summary>
    [Serializable]
    public class PromptPolicyConfig : IExposable
    {
        public bool Enabled;
        public bool EnableIntentDrivenActionMapping;
        public int IntentActionCooldownTurns;
        public int IntentMinAssistantRoundsForMemory;
        public int IntentNoActionStreakThreshold;
        public bool ResetPromptCustomOnSchemaUpgrade;
        public int SummaryTimelineTurnLimit;
        public int SummaryCharBudget;

        public PromptPolicyConfig()
        {
            Enabled = true;
            EnableIntentDrivenActionMapping = true;
            IntentActionCooldownTurns = 2;
            IntentMinAssistantRoundsForMemory = 1;
            IntentNoActionStreakThreshold = 2;
            ResetPromptCustomOnSchemaUpgrade = true;
            SummaryTimelineTurnLimit = 8;
            SummaryCharBudget = 1200;
        }

        public static PromptPolicyConfig CreateDefault()
        {
            return new PromptPolicyConfig();
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Values.Look(ref EnableIntentDrivenActionMapping, "enableIntentDrivenActionMapping", true);
            Scribe_Values.Look(ref IntentActionCooldownTurns, "intentActionCooldownTurns", 2);
            Scribe_Values.Look(ref IntentMinAssistantRoundsForMemory, "intentMinAssistantRoundsForMemory", 1);
            Scribe_Values.Look(ref IntentNoActionStreakThreshold, "intentNoActionStreakThreshold", 2);
            Scribe_Values.Look(ref ResetPromptCustomOnSchemaUpgrade, "resetPromptCustomOnSchemaUpgrade", true);
            Scribe_Values.Look(ref SummaryTimelineTurnLimit, "summaryTimelineTurnLimit", 8);
            Scribe_Values.Look(ref SummaryCharBudget, "summaryCharBudget", 1200);
        }

        public PromptPolicyConfig Clone()
        {
            var clone = new PromptPolicyConfig
            {
                Enabled = Enabled,
                EnableIntentDrivenActionMapping = EnableIntentDrivenActionMapping,
                IntentActionCooldownTurns = IntentActionCooldownTurns,
                IntentMinAssistantRoundsForMemory = IntentMinAssistantRoundsForMemory,
                IntentNoActionStreakThreshold = IntentNoActionStreakThreshold,
                ResetPromptCustomOnSchemaUpgrade = ResetPromptCustomOnSchemaUpgrade,
                SummaryTimelineTurnLimit = SummaryTimelineTurnLimit,
                SummaryCharBudget = SummaryCharBudget
            };
            return clone;
        }
    }
}
