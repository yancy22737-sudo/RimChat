using System;
using System.Collections.Generic;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: Verse.Scribe for config persistence.
    /// Responsibility: store Prompt Policy V2 budgets, trim priorities and intent-action mapping controls.
    /// </summary>
    [Serializable]
    public class PromptNodeBudgetConfig : IExposable
    {
        public string NodeId;
        public int MaxChars;

        public PromptNodeBudgetConfig()
        {
            NodeId = string.Empty;
            MaxChars = 0;
        }

        public PromptNodeBudgetConfig(string nodeId, int maxChars)
        {
            NodeId = nodeId ?? string.Empty;
            MaxChars = maxChars;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref NodeId, "nodeId", string.Empty);
            Scribe_Values.Look(ref MaxChars, "maxChars", 0);
        }

        public PromptNodeBudgetConfig Clone()
        {
            return new PromptNodeBudgetConfig
            {
                NodeId = NodeId,
                MaxChars = MaxChars
            };
        }
    }

    /// <summary>
    /// Dependencies: PromptNodeBudgetConfig, Verse.Scribe.
    /// Responsibility: central policy controls for turn objective, budget trimming and action mapping.
    /// </summary>
    [Serializable]
    public class PromptPolicyConfig : IExposable
    {
        public bool Enabled;
        public int GlobalPromptCharBudget;
        public List<PromptNodeBudgetConfig> NodeBudgets;
        public List<string> TrimPriorityNodeIds;
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
            GlobalPromptCharBudget = 7800;
            NodeBudgets = new List<PromptNodeBudgetConfig>();
            TrimPriorityNodeIds = new List<string>();
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
            var config = new PromptPolicyConfig
            {
                NodeBudgets = new List<PromptNodeBudgetConfig>
                {
                    new PromptNodeBudgetConfig("environment", 1900),
                    new PromptNodeBudgetConfig("instruction_stack", 1400),
                    new PromptNodeBudgetConfig("dynamic_npc_personal_memory", 1500),
                    new PromptNodeBudgetConfig("dynamic_faction_memory", 900),
                    new PromptNodeBudgetConfig("actor_state", 1700),
                    new PromptNodeBudgetConfig("api_contract", 1700),
                    new PromptNodeBudgetConfig("response_contract", 1200),
                    new PromptNodeBudgetConfig("api_limits", 700),
                    new PromptNodeBudgetConfig("quest_guidance", 900)
                },
                TrimPriorityNodeIds = new List<string>
                {
                    "rimtalk_preset_mod_entries",
                    "rimtalk_compat",
                    "dynamic_data",
                    "interlocutor_status",
                    "interlocutor_faction_context",
                    "api_limits",
                    "quest_guidance",
                    "response_contract",
                    "environment",
                    "dynamic_faction_memory",
                    "dynamic_npc_personal_memory",
                    "actor_state",
                    "api_contract",
                    "instruction_stack"
                }
            };
            return config;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Values.Look(ref GlobalPromptCharBudget, "globalPromptCharBudget", 7800);
            Scribe_Collections.Look(ref NodeBudgets, "nodeBudgets", LookMode.Deep);
            Scribe_Collections.Look(ref TrimPriorityNodeIds, "trimPriorityNodeIds", LookMode.Value);
            Scribe_Values.Look(ref EnableIntentDrivenActionMapping, "enableIntentDrivenActionMapping", true);
            Scribe_Values.Look(ref IntentActionCooldownTurns, "intentActionCooldownTurns", 2);
            Scribe_Values.Look(ref IntentMinAssistantRoundsForMemory, "intentMinAssistantRoundsForMemory", 1);
            Scribe_Values.Look(ref IntentNoActionStreakThreshold, "intentNoActionStreakThreshold", 2);
            Scribe_Values.Look(ref ResetPromptCustomOnSchemaUpgrade, "resetPromptCustomOnSchemaUpgrade", true);
            Scribe_Values.Look(ref SummaryTimelineTurnLimit, "summaryTimelineTurnLimit", 8);
            Scribe_Values.Look(ref SummaryCharBudget, "summaryCharBudget", 1200);

            NodeBudgets ??= new List<PromptNodeBudgetConfig>();
            TrimPriorityNodeIds ??= new List<string>();
        }

        public PromptPolicyConfig Clone()
        {
            var clone = new PromptPolicyConfig
            {
                Enabled = Enabled,
                GlobalPromptCharBudget = GlobalPromptCharBudget,
                EnableIntentDrivenActionMapping = EnableIntentDrivenActionMapping,
                IntentActionCooldownTurns = IntentActionCooldownTurns,
                IntentMinAssistantRoundsForMemory = IntentMinAssistantRoundsForMemory,
                IntentNoActionStreakThreshold = IntentNoActionStreakThreshold,
                ResetPromptCustomOnSchemaUpgrade = ResetPromptCustomOnSchemaUpgrade,
                SummaryTimelineTurnLimit = SummaryTimelineTurnLimit,
                SummaryCharBudget = SummaryCharBudget,
                NodeBudgets = new List<PromptNodeBudgetConfig>(),
                TrimPriorityNodeIds = new List<string>()
            };

            if (NodeBudgets != null)
            {
                for (int i = 0; i < NodeBudgets.Count; i++)
                {
                    PromptNodeBudgetConfig budget = NodeBudgets[i];
                    if (budget != null)
                    {
                        clone.NodeBudgets.Add(budget.Clone());
                    }
                }
            }

            if (TrimPriorityNodeIds != null)
            {
                for (int i = 0; i < TrimPriorityNodeIds.Count; i++)
                {
                    string value = TrimPriorityNodeIds[i];
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        clone.TrimPriorityNodeIds.Add(value);
                    }
                }
            }

            return clone;
        }
    }
}
