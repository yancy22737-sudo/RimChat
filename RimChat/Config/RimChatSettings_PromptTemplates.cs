using System;
using UnityEngine;
using Verse;

namespace RimChat.Config
{
    public partial class RimChatSettings
    {
        private static readonly string[] PromptTemplateFieldKeys =
        {
            "FactGroundingTemplate",
            "OutputLanguageTemplate",
            "DiplomacyFallbackRoleTemplate",
            "DecisionPolicyTemplate",
            "TurnObjectiveTemplate",
            "TopicShiftRuleTemplate",
            "ApiLimitsNodeTemplate",
            "QuestGuidanceNodeTemplate",
            "ResponseContractNodeTemplate"
        };

        private int _selectedPromptTemplateFieldIndex;
        private Vector2 _promptTemplateFieldListScroll = Vector2.zero;
        private Vector2 _promptTemplateEditorScroll = Vector2.zero;
        private string _promptTemplateEditorBuffer = string.Empty;
        private string _promptTemplateEditingKey = string.Empty;

        private void DrawPromptTemplatesEditorScrollable(Rect rect)
        {
            PromptTemplateTextConfig templates = EnsurePromptTemplateConfig();

            Rect toggleRect = new Rect(rect.x, rect.y, rect.width, 24f);
            bool enabled = templates.Enabled;
            Widgets.CheckboxLabeled(toggleRect, "RimChat_PromptTemplatesEnabled".Translate(), ref enabled);
            if (enabled != templates.Enabled)
            {
                templates.Enabled = enabled;
                _previewUpdateCooldown = 0;
            }

            float contentTop = rect.y + 28f;
            Rect contentRect = new Rect(rect.x, contentTop, rect.width, rect.height - 28f);
            float listWidth = 240f;
            Rect listRect = new Rect(contentRect.x, contentRect.y, listWidth, contentRect.height);
            Rect editorRect = new Rect(contentRect.x + listWidth + 8f, contentRect.y, contentRect.width - listWidth - 8f, contentRect.height);

            DrawPromptTemplateFieldList(listRect);
            DrawPromptTemplateFieldEditor(editorRect, templates);
        }

        private void DrawPromptTemplateFieldList(Rect rect)
        {
            float rowHeight = 30f;
            float contentHeight = Mathf.Max(rect.height, PromptTemplateFieldKeys.Length * rowHeight);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, contentHeight);
            _promptTemplateFieldListScroll = GUI.BeginScrollView(rect, _promptTemplateFieldListScroll, viewRect);

            for (int i = 0; i < PromptTemplateFieldKeys.Length; i++)
            {
                Rect rowRect = new Rect(0f, i * rowHeight, viewRect.width, rowHeight - 2f);
                bool selected = i == _selectedPromptTemplateFieldIndex;
                if (selected)
                {
                    Widgets.DrawBoxSolid(rowRect, new Color(0.25f, 0.35f, 0.55f, 0.8f));
                }
                else if (Mouse.IsOver(rowRect))
                {
                    Widgets.DrawBoxSolid(rowRect, new Color(0.2f, 0.22f, 0.28f, 0.6f));
                }

                Widgets.Label(rowRect.ContractedBy(5f), GetPromptTemplateFieldLabel(PromptTemplateFieldKeys[i]));
                if (Widgets.ButtonInvisible(rowRect))
                {
                    _selectedPromptTemplateFieldIndex = i;
                }
            }

            GUI.EndScrollView();
        }

        private void DrawPromptTemplateFieldEditor(Rect rect, PromptTemplateTextConfig templates)
        {
            if (_selectedPromptTemplateFieldIndex < 0 || _selectedPromptTemplateFieldIndex >= PromptTemplateFieldKeys.Length)
            {
                _selectedPromptTemplateFieldIndex = 0;
            }

            string key = PromptTemplateFieldKeys[_selectedPromptTemplateFieldIndex];
            if (!string.Equals(_promptTemplateEditingKey, key, StringComparison.Ordinal))
            {
                _promptTemplateEditingKey = key;
                _promptTemplateEditorBuffer = GetPromptTemplateFieldValue(templates, key);
                _promptTemplateEditorScroll = Vector2.zero;
            }

            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), GetPromptTemplateFieldLabel(key));
            Rect textRect = new Rect(rect.x, rect.y + 26f, rect.width, rect.height - 26f);
            float contentHeight = Mathf.Max(textRect.height, Text.CalcHeight(_promptTemplateEditorBuffer ?? string.Empty, textRect.width - 16f) + 10f);
            Rect viewRect = new Rect(0f, 0f, textRect.width - 16f, contentHeight);
            _promptTemplateEditorScroll = GUI.BeginScrollView(textRect, _promptTemplateEditorScroll, viewRect);

            string edited = GUI.TextArea(viewRect, _promptTemplateEditorBuffer ?? string.Empty);
            GUI.EndScrollView();
            if (string.Equals(edited, _promptTemplateEditorBuffer, StringComparison.Ordinal))
            {
                return;
            }

            _promptTemplateEditorBuffer = edited;
            SetPromptTemplateFieldValue(templates, key, edited);
            _previewUpdateCooldown = 0;
        }

        private PromptTemplateTextConfig EnsurePromptTemplateConfig()
        {
            SystemPromptConfigData.PromptTemplates ??= new PromptTemplateTextConfig();
            return SystemPromptConfigData.PromptTemplates;
        }

        private static string GetPromptTemplateFieldValue(PromptTemplateTextConfig templates, string key)
        {
            return key switch
            {
                "FactGroundingTemplate" => templates.FactGroundingTemplate ?? string.Empty,
                "OutputLanguageTemplate" => templates.OutputLanguageTemplate ?? string.Empty,
                "DiplomacyFallbackRoleTemplate" => templates.DiplomacyFallbackRoleTemplate ?? string.Empty,
                "SocialCircleActionRuleTemplate" => templates.SocialCircleActionRuleTemplate ?? string.Empty,
                "RpgRoleSettingTemplate" => templates.RpgRoleSettingTemplate ?? string.Empty,
                "RpgCompactFormatConstraintTemplate" => templates.RpgCompactFormatConstraintTemplate ?? string.Empty,
                "RpgActionReliabilityRuleTemplate" => templates.RpgActionReliabilityRuleTemplate ?? string.Empty,
                "DecisionPolicyTemplate" => templates.DecisionPolicyTemplate ?? string.Empty,
                "TurnObjectiveTemplate" => templates.TurnObjectiveTemplate ?? string.Empty,
                "OpeningObjectiveTemplate" => templates.OpeningObjectiveTemplate ?? string.Empty,
                "TopicShiftRuleTemplate" => templates.TopicShiftRuleTemplate ?? string.Empty,
                "ApiLimitsNodeTemplate" => templates.ApiLimitsNodeTemplate ?? string.Empty,
                "QuestGuidanceNodeTemplate" => templates.QuestGuidanceNodeTemplate ?? string.Empty,
                "ResponseContractNodeTemplate" => templates.ResponseContractNodeTemplate ?? string.Empty,
                _ => string.Empty
            };
        }

        private static void SetPromptTemplateFieldValue(PromptTemplateTextConfig templates, string key, string value)
        {
            switch (key)
            {
                case "FactGroundingTemplate":
                    templates.FactGroundingTemplate = value;
                    break;
                case "OutputLanguageTemplate":
                    templates.OutputLanguageTemplate = value;
                    break;
                case "DiplomacyFallbackRoleTemplate":
                    templates.DiplomacyFallbackRoleTemplate = value;
                    break;
                case "SocialCircleActionRuleTemplate":
                    templates.SocialCircleActionRuleTemplate = value;
                    break;
                case "RpgRoleSettingTemplate":
                    templates.RpgRoleSettingTemplate = value;
                    break;
                case "RpgCompactFormatConstraintTemplate":
                    templates.RpgCompactFormatConstraintTemplate = value;
                    break;
                case "RpgActionReliabilityRuleTemplate":
                    templates.RpgActionReliabilityRuleTemplate = value;
                    break;
                case "DecisionPolicyTemplate":
                    templates.DecisionPolicyTemplate = value;
                    break;
                case "TurnObjectiveTemplate":
                    templates.TurnObjectiveTemplate = value;
                    break;
                case "OpeningObjectiveTemplate":
                    templates.OpeningObjectiveTemplate = value;
                    break;
                case "TopicShiftRuleTemplate":
                    templates.TopicShiftRuleTemplate = value;
                    break;
                case "ApiLimitsNodeTemplate":
                    templates.ApiLimitsNodeTemplate = value;
                    break;
                case "QuestGuidanceNodeTemplate":
                    templates.QuestGuidanceNodeTemplate = value;
                    break;
                case "ResponseContractNodeTemplate":
                    templates.ResponseContractNodeTemplate = value;
                    break;
            }
        }

        private static string GetPromptTemplateFieldLabel(string key)
        {
            return key switch
            {
                "FactGroundingTemplate" => "RimChat_PromptTemplateFactGroundingLabel".Translate(),
                "OutputLanguageTemplate" => "RimChat_PromptTemplateOutputLanguageLabel".Translate(),
                "DiplomacyFallbackRoleTemplate" => "RimChat_PromptTemplateDiplomacyRoleFallbackLabel".Translate(),
                "SocialCircleActionRuleTemplate" => "RimChat_PromptTemplateSocialCircleActionRuleLabel".Translate(),
                "RpgRoleSettingTemplate" => "RimChat_PromptTemplateRpgRoleFallbackLabel".Translate(),
                "RpgCompactFormatConstraintTemplate" => "RimChat_PromptTemplateRpgCompactFormatLabel".Translate(),
                "RpgActionReliabilityRuleTemplate" => "RimChat_PromptTemplateRpgReliabilityRuleLabel".Translate(),
                "DecisionPolicyTemplate" => "RimChat_PromptTemplateDecisionPolicyLabel".Translate(),
                "TurnObjectiveTemplate" => "RimChat_PromptTemplateTurnObjectiveLabel".Translate(),
                "OpeningObjectiveTemplate" => "RimChat_PromptTemplateOpeningObjectiveLabel".Translate(),
                "TopicShiftRuleTemplate" => "RimChat_PromptTemplateTopicShiftRuleLabel".Translate(),
                "ApiLimitsNodeTemplate" => "RimChat_PromptTemplateApiLimitsNodeLabel".Translate(),
                "QuestGuidanceNodeTemplate" => "RimChat_PromptTemplateQuestGuidanceNodeLabel".Translate(),
                "ResponseContractNodeTemplate" => "RimChat_PromptTemplateResponseContractNodeLabel".Translate(),
                _ => key
            };
        }

        private string GetCurrentPromptTemplateEditorText()
        {
            PromptTemplateTextConfig templates = EnsurePromptTemplateConfig();
            if (_selectedPromptTemplateFieldIndex < 0 || _selectedPromptTemplateFieldIndex >= PromptTemplateFieldKeys.Length)
            {
                _selectedPromptTemplateFieldIndex = 0;
            }

            string key = PromptTemplateFieldKeys[_selectedPromptTemplateFieldIndex];
            if (string.Equals(_promptTemplateEditingKey, key, StringComparison.Ordinal))
            {
                return _promptTemplateEditorBuffer ?? string.Empty;
            }

            return GetPromptTemplateFieldValue(templates, key);
        }
    }
}
