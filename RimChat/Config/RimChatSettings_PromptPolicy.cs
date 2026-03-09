using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    public partial class RimChatSettings
    {
        private Vector2 _promptPolicyScroll = Vector2.zero;
        private string _promptPolicyTrimPriorityBuffer = string.Empty;
        private string _promptPolicyTrimPrioritySerialized = string.Empty;

        private void DrawPromptPolicyEditorScrollable(Rect rect)
        {
            PromptPolicyConfig policy = EnsurePromptPolicyConfig();
            EnsurePromptPolicyNodeBudgets(policy);

            float baseHeight = 520f;
            float nodeBudgetRows = Mathf.Max(8, policy.NodeBudgets?.Count ?? 0);
            float contentHeight = baseHeight + nodeBudgetRows * 30f;
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(rect.height, contentHeight));
            _promptPolicyScroll = GUI.BeginScrollView(rect, _promptPolicyScroll, viewRect);

            float y = 0f;
            bool enabled = policy.Enabled;
            Widgets.CheckboxLabeled(new Rect(0f, y, viewRect.width, 24f), "RimChat_PromptPolicyEnabled".Translate(), ref enabled);
            policy.Enabled = enabled;
            y += 28f;

            policy.GlobalPromptCharBudget = DrawPolicyIntRow(viewRect, ref y, "RimChat_PromptPolicyGlobalBudgetLabel".Translate(), policy.GlobalPromptCharBudget, 2000, 30000);
            policy.SummaryTimelineTurnLimit = DrawPolicyIntRow(viewRect, ref y, "RimChat_PromptPolicySummaryTurnLimitLabel".Translate(), policy.SummaryTimelineTurnLimit, 3, 24);
            policy.SummaryCharBudget = DrawPolicyIntRow(viewRect, ref y, "RimChat_PromptPolicySummaryCharBudgetLabel".Translate(), policy.SummaryCharBudget, 300, 8000);

            bool intentMapping = policy.EnableIntentDrivenActionMapping;
            Widgets.CheckboxLabeled(new Rect(0f, y, viewRect.width, 24f), "RimChat_PromptPolicyIntentMappingLabel".Translate(), ref intentMapping);
            policy.EnableIntentDrivenActionMapping = intentMapping;
            y += 28f;

            policy.IntentActionCooldownTurns = DrawPolicyIntRow(viewRect, ref y, "RimChat_PromptPolicyIntentCooldownLabel".Translate(), policy.IntentActionCooldownTurns, 0, 12);
            policy.IntentMinAssistantRoundsForMemory = DrawPolicyIntRow(viewRect, ref y, "RimChat_PromptPolicyIntentMinRoundsLabel".Translate(), policy.IntentMinAssistantRoundsForMemory, 0, 20);
            policy.IntentNoActionStreakThreshold = DrawPolicyIntRow(viewRect, ref y, "RimChat_PromptPolicyNoActionThresholdLabel".Translate(), policy.IntentNoActionStreakThreshold, 1, 8);

            bool resetOnUpgrade = policy.ResetPromptCustomOnSchemaUpgrade;
            Widgets.CheckboxLabeled(new Rect(0f, y, viewRect.width, 24f), "RimChat_PromptPolicyResetOnUpgradeLabel".Translate(), ref resetOnUpgrade);
            policy.ResetPromptCustomOnSchemaUpgrade = resetOnUpgrade;
            y += 34f;

            Widgets.Label(new Rect(0f, y, viewRect.width, 22f), "RimChat_PromptPolicyNodeBudgetHeader".Translate());
            y += 24f;
            DrawNodeBudgetRows(viewRect, ref y, policy);

            y += 8f;
            Widgets.Label(new Rect(0f, y, viewRect.width, 22f), "RimChat_PromptPolicyTrimPriorityLabel".Translate());
            y += 24f;
            y = DrawTrimPriorityEditor(viewRect, y, policy);

            GUI.EndScrollView();
        }

        private PromptPolicyConfig EnsurePromptPolicyConfig()
        {
            SystemPromptConfigData.PromptPolicy ??= PromptPolicyConfig.CreateDefault();
            if (SystemPromptConfigData.PromptPolicySchemaVersion <= 0)
            {
                SystemPromptConfigData.PromptPolicySchemaVersion = SystemPromptConfig.CurrentPromptPolicySchemaVersion;
            }

            return SystemPromptConfigData.PromptPolicy;
        }

        private static int DrawPolicyIntRow(Rect viewRect, ref float y, string label, int currentValue, int min, int max)
        {
            Rect rowRect = new Rect(0f, y, viewRect.width, 24f);
            Rect labelRect = new Rect(rowRect.x, rowRect.y, rowRect.width - 110f, rowRect.height);
            Rect inputRect = new Rect(rowRect.xMax - 100f, rowRect.y, 100f, rowRect.height);
            Widgets.Label(labelRect, label);
            string raw = Widgets.TextField(inputRect, currentValue.ToString());
            if (int.TryParse(raw, out int parsed))
            {
                currentValue = Mathf.Clamp(parsed, min, max);
            }

            y += 28f;
            return currentValue;
        }

        private static void DrawNodeBudgetRows(Rect viewRect, ref float y, PromptPolicyConfig policy)
        {
            if (policy?.NodeBudgets == null)
            {
                return;
            }

            policy.NodeBudgets = policy.NodeBudgets
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.NodeId))
                .OrderBy(item => item.NodeId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            for (int i = 0; i < policy.NodeBudgets.Count; i++)
            {
                PromptNodeBudgetConfig item = policy.NodeBudgets[i];
                Rect rowRect = new Rect(0f, y, viewRect.width, 24f);
                Rect labelRect = new Rect(rowRect.x, rowRect.y, rowRect.width - 110f, rowRect.height);
                Rect inputRect = new Rect(rowRect.xMax - 100f, rowRect.y, 100f, rowRect.height);
                Widgets.Label(labelRect, item.NodeId);
                string raw = Widgets.TextField(inputRect, item.MaxChars.ToString());
                if (int.TryParse(raw, out int parsed))
                {
                    item.MaxChars = Mathf.Clamp(parsed, 0, 10000);
                }

                y += 28f;
            }
        }

        private float DrawTrimPriorityEditor(Rect viewRect, float y, PromptPolicyConfig policy)
        {
            string serialized = string.Join("\n", policy?.TrimPriorityNodeIds ?? new List<string>());
            if (!string.Equals(serialized, _promptPolicyTrimPrioritySerialized, StringComparison.Ordinal))
            {
                _promptPolicyTrimPrioritySerialized = serialized;
                _promptPolicyTrimPriorityBuffer = serialized;
            }

            float textHeight = 120f;
            Rect textRect = new Rect(0f, y, viewRect.width, textHeight);
            string edited = GUI.TextArea(textRect, _promptPolicyTrimPriorityBuffer ?? string.Empty);
            if (!string.Equals(edited, _promptPolicyTrimPriorityBuffer, StringComparison.Ordinal))
            {
                _promptPolicyTrimPriorityBuffer = edited;
                policy.TrimPriorityNodeIds = edited
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                _promptPolicyTrimPrioritySerialized = string.Join("\n", policy.TrimPriorityNodeIds);
            }

            return y + textHeight + 4f;
        }

        private static void EnsurePromptPolicyNodeBudgets(PromptPolicyConfig policy)
        {
            if (policy == null)
            {
                return;
            }

            policy.NodeBudgets ??= new List<PromptNodeBudgetConfig>();
            PromptPolicyConfig defaults = PromptPolicyConfig.CreateDefault();
            if (defaults?.NodeBudgets == null)
            {
                return;
            }

            for (int i = 0; i < defaults.NodeBudgets.Count; i++)
            {
                PromptNodeBudgetConfig item = defaults.NodeBudgets[i];
                if (item == null || string.IsNullOrWhiteSpace(item.NodeId))
                {
                    continue;
                }

                bool exists = policy.NodeBudgets.Any(existing =>
                    existing != null &&
                    string.Equals(existing.NodeId, item.NodeId, StringComparison.OrdinalIgnoreCase));
                if (!exists)
                {
                    policy.NodeBudgets.Add(item.Clone());
                }
            }
        }
    }
}
