using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimDiplomacy
{
    public partial class RimDiplomacySettings
    {
        private SystemPromptConfig _systemPromptConfig;
        private bool _advancedPromptMode = false;

        private int _selectedSectionIndex = 0;
        private int _selectedApiActionIndex = -1;
        private int _selectedDecisionRuleIndex = -1;

        private string _editingApiActionName = "";
        private string _editingApiActionDesc = "";
        private string _editingApiActionParams = "";
        private string _editingApiActionReq = "";
        private string _editingRuleName = "";
        private string _editingRuleContent = "";

        // 文本缓冲区
        private string _globalPromptBuffer = "";
        private string _jsonTemplateBuffer = "";
        private string _relationChangesBuffer = "";
        private string _importantRulesBuffer = "";

        // 滚动位置
        private Vector2 _globalPromptScroll = Vector2.zero;
        private Vector2 _apiDescScroll = Vector2.zero;
        private Vector2 _jsonTemplateScroll = Vector2.zero;
        private Vector2 _ruleContentScroll = Vector2.zero;
        private Vector2 _previewScroll = Vector2.zero;

        private string _cachedPreviewText = "";
        private int _previewUpdateCooldown = 0;
        private bool _showPreview = true;

        private static readonly Color SectionHeaderColor = new Color(0.9f, 0.7f, 0.4f);

        // 分区定义 - 简单模式和高级模式共用
        private static readonly string[] SimpleSectionNames = new string[]
        {
            "GlobalPrompt",
            "DynamicData"
        };

        private static readonly string[] AdvancedSectionNames = new string[]
        {
            "GlobalPrompt",
            "ApiActions",
            "ResponseFormat",
            "DecisionRules",
            "DynamicData"
        };

        public SystemPromptConfig SystemPromptConfigData
        {
            get
            {
                if (_systemPromptConfig == null)
                {
                    _systemPromptConfig = PromptPersistenceService.Instance.LoadConfig();
                }
                return _systemPromptConfig;
            }
        }

        private void DrawAdvancedPromptSettingsSection(Listing_Standard listing)
        {
            // 固定高度，无滚动条
            float totalHeight = 520f;
            Rect mainRect = listing.GetRect(totalHeight);

            // 初始化缓冲区
            InitBuffers();

            // 主布局：左侧导航 + 右侧编辑区
            float navWidth = mainRect.width / 3.5f; // 1:2.5比例
            float editorWidth = mainRect.width - navWidth - 10f;

            Rect navRect = new Rect(mainRect.x, mainRect.y, navWidth, totalHeight);
            Rect editorRect = new Rect(mainRect.x + navWidth + 10f, mainRect.y, editorWidth, totalHeight);

            // 绘制左侧导航（包含按钮）
            DrawNavigationPanelWithButtons(navRect);

            // 绘制右侧编辑区（包含预览）
            DrawEditorPanelWithPreview(editorRect);
        }

        private void InitBuffers()
        {
            if (string.IsNullOrEmpty(_globalPromptBuffer))
                _globalPromptBuffer = SystemPromptConfigData.GlobalSystemPrompt ?? "";
            if (string.IsNullOrEmpty(_jsonTemplateBuffer))
                _jsonTemplateBuffer = SystemPromptConfigData.ResponseFormat?.JsonTemplate ?? "";
            if (string.IsNullOrEmpty(_relationChangesBuffer))
                _relationChangesBuffer = SystemPromptConfigData.ResponseFormat?.RelationChangesTemplate ?? "";
            if (string.IsNullOrEmpty(_importantRulesBuffer))
                _importantRulesBuffer = SystemPromptConfigData.ResponseFormat?.ImportantRules ?? "";
        }

        private void DrawNavigationPanelWithButtons(Rect rect)
        {
            // 背景
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.14f));
            Widgets.DrawBox(rect);

            Rect innerRect = rect.ContractedBy(8f);
            float y = innerRect.y;

            // 模式切换小按钮（放在左上角）
            Rect toggleRect = new Rect(innerRect.x, y, innerRect.width, 24f);
            DrawModeToggleSmall(toggleRect);
            y += 30f;

            // 分隔线
            Widgets.DrawLineHorizontal(innerRect.x, y, innerRect.width);
            y += 10f;

            // 根据模式获取分区列表
            string[] sections = _advancedPromptMode ? AdvancedSectionNames : SimpleSectionNames;

            // 计算分区列表区域高度（预留按钮区域）
            float buttonAreaHeight = 130f;
            float listHeight = innerRect.height - y - buttonAreaHeight;

            // 分区列表区域（带滚动）
            Rect listRect = new Rect(innerRect.x, y, innerRect.width, listHeight);
            
            // 计算内容高度
            float contentHeight = sections.Length * 32f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, Mathf.Max(contentHeight, listHeight));
            
            // 使用独立的滚动位置
            Vector2 navScrollPosition = Vector2.zero;
            navScrollPosition = GUI.BeginScrollView(listRect, navScrollPosition, viewRect);
            
            // 绘制分区按钮
            for (int i = 0; i < sections.Length; i++)
            {
                string sectionName = sections[i];
                bool isSelected = _selectedSectionIndex == i;

                Rect btnRect = new Rect(0f, i * 32f, viewRect.width, 28f);

                // 选中状态背景
                if (isSelected)
                {
                    Widgets.DrawBoxSolid(btnRect, new Color(0.25f, 0.35f, 0.55f));
                }
                else if (Mouse.IsOver(btnRect))
                {
                    Widgets.DrawBoxSolid(btnRect, new Color(0.2f, 0.22f, 0.28f));
                }

                // 左边框强调
                if (isSelected)
                {
                    Rect accentRect = new Rect(btnRect.x, btnRect.y, 3f, btnRect.height);
                    Widgets.DrawBoxSolid(accentRect, new Color(0.4f, 0.7f, 1f));
                }

                // 文字
                GUI.color = isSelected ? Color.white : new Color(0.7f, 0.7f, 0.75f);
                TextAnchor oldAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleLeft;
                string label = GetSectionLabel(sectionName);
                Widgets.Label(new Rect(btnRect.x + 8f, btnRect.y, btnRect.width - 16f, btnRect.height), label);
                Text.Anchor = oldAnchor;
                GUI.color = Color.white;

                // 点击处理
                if (Widgets.ButtonInvisible(btnRect))
                {
                    _selectedSectionIndex = i;
                    _selectedApiActionIndex = -1;
                    _selectedDecisionRuleIndex = -1;
                }
            }
            
            GUI.EndScrollView();

            // 按钮区域（在导航栏底部）
            y += listHeight + 10f;
            Rect buttonAreaRect = new Rect(innerRect.x, y, innerRect.width, buttonAreaHeight - 10f);
            
            // 分隔线
            Widgets.DrawLineHorizontal(innerRect.x, y - 5f, innerRect.width);
            
            // 绘制按钮
            DrawPromptActionButtonsVertical(buttonAreaRect);
        }

        private void DrawPromptActionButtonsVertical(Rect rect)
        {
            float btnHeight = 26f;
            float gap = 6f;
            float y = rect.y;

            Rect saveRect = new Rect(rect.x, y, rect.width, btnHeight);
            if (Widgets.ButtonText(saveRect, "RimDiplomacy_SavePrompt".Translate()))
            {
                SaveSystemPromptConfig();
                Messages.Message("RimDiplomacy_PromptSaved".Translate(), MessageTypeDefOf.NeutralEvent, false);
            }
            y += btnHeight + gap;

            Rect resetRect = new Rect(rect.x, y, rect.width, btnHeight);
            if (Widgets.ButtonText(resetRect, "RimDiplomacy_ResetToDefault".Translate()))
            {
                ShowResetPromptConfigConfirmation();
            }
            y += btnHeight + gap;

            Rect exportRect = new Rect(rect.x, y, rect.width, btnHeight);
            if (Widgets.ButtonText(exportRect, "RimDiplomacy_ExportPrompts".Translate()))
            {
                ShowExportSystemPromptDialog();
            }
            y += btnHeight + gap;

            Rect importRect = new Rect(rect.x, y, rect.width, btnHeight);
            if (Widgets.ButtonText(importRect, "RimDiplomacy_ImportPrompts".Translate()))
            {
                ShowImportSystemPromptDialog();
            }
        }

        private void DrawModeToggleSmall(Rect rect)
        {
            float btnWidth = rect.width / 2 - 2f;

            // 简单模式按钮
            Rect simpleRect = new Rect(rect.x, rect.y, btnWidth, rect.height);
            bool isSimple = !_advancedPromptMode;

            GUI.color = isSimple ? new Color(0.3f, 0.6f, 0.9f) : new Color(0.18f, 0.18f, 0.2f);
            Widgets.DrawBoxSolid(simpleRect, GUI.color);
            GUI.color = isSimple ? Color.white : Color.gray;
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(simpleRect, "RimDiplomacy_SimpleModeShort".Translate());
            Text.Anchor = oldAnchor;
            GUI.color = Color.white;

            if (Widgets.ButtonInvisible(simpleRect))
            {
                _advancedPromptMode = false;
                _selectedSectionIndex = 0;
                SyncBuffersToData();
            }

            // 高级模式按钮
            Rect advancedRect = new Rect(rect.x + btnWidth + 4f, rect.y, btnWidth, rect.height);
            bool isAdvanced = _advancedPromptMode;

            GUI.color = isAdvanced ? new Color(0.9f, 0.5f, 0.25f) : new Color(0.18f, 0.18f, 0.2f);
            Widgets.DrawBoxSolid(advancedRect, GUI.color);
            GUI.color = isAdvanced ? Color.white : Color.gray;
            TextAnchor oldAnchor2 = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(advancedRect, "RimDiplomacy_AdvancedModeShort".Translate());
            Text.Anchor = oldAnchor2;
            GUI.color = Color.white;

            if (Widgets.ButtonInvisible(advancedRect))
            {
                _advancedPromptMode = true;
                _selectedSectionIndex = 0;
                SyncBuffersToData();
            }
        }

        private void DrawEditorPanelWithPreview(Rect rect)
        {
            // 背景
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.12f));
            Widgets.DrawBox(rect);

            Rect innerRect = rect.ContractedBy(10f);

            // 获取当前分区
            string[] sections = _advancedPromptMode ? AdvancedSectionNames : SimpleSectionNames;
            if (_selectedSectionIndex >= sections.Length)
                _selectedSectionIndex = 0;

            string currentSection = sections[_selectedSectionIndex];

            // 计算布局：编辑区 + 预览区
            float titleHeight = 30f;
            float previewToggleWidth = 80f;
            float previewHeight = _showPreview ? 140f : 0f;
            float previewGap = _showPreview ? 10f : 0f;
            float editorHeight = innerRect.height - titleHeight - previewGap - previewHeight;

            // 分区标题
            Rect titleRect = new Rect(innerRect.x, innerRect.y, innerRect.width - previewToggleWidth - 10f, titleHeight);
            GUI.color = SectionHeaderColor;
            Text.Font = GameFont.Medium;
            Widgets.Label(titleRect, GetSectionLabel(currentSection));
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            // 预览开关（右上角）
            Rect previewToggleRect = new Rect(innerRect.xMax - previewToggleWidth, innerRect.y + 3f, previewToggleWidth, 24f);
            Widgets.CheckboxLabeled(previewToggleRect, "RimDiplomacy_PreviewShort".Translate(), ref _showPreview);

            // 编辑区域（到底部）
            Rect contentRect = new Rect(innerRect.x, innerRect.y + titleHeight, innerRect.width, editorHeight);
            switch (currentSection)
            {
                case "GlobalPrompt":
                    DrawGlobalPromptEditorScrollable(contentRect);
                    break;
                case "ApiActions":
                    DrawApiActionsEditorScrollable(contentRect);
                    break;
                case "ResponseFormat":
                    DrawResponseFormatEditorScrollable(contentRect);
                    break;
                case "DecisionRules":
                    DrawDecisionRulesEditorScrollable(contentRect);
                    break;
                case "DynamicData":
                    DrawDynamicDataEditor(contentRect);
                    break;
            }

            // 预览区域（在右侧下方，到底部）
            if (_showPreview)
            {
                float previewY = innerRect.y + titleHeight + editorHeight + previewGap;
                Rect previewRect = new Rect(innerRect.x, previewY, innerRect.width, previewHeight);
                DrawPreviewRight(previewRect);
            }
        }

        private void DrawGlobalPromptEditorScrollable(Rect rect)
        {
            // 字数统计
            int currentLength = _globalPromptBuffer?.Length ?? 0;
            GUI.color = currentLength > MaxSystemPromptLength * 0.9f ? Color.red :
                currentLength > MaxSystemPromptLength * 0.7f ? Color.yellow : Color.gray;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 20f), $"({currentLength}/{MaxSystemPromptLength})");
            GUI.color = Color.white;

            // 带滚动条的文本框（填满剩余空间）
            float textY = rect.y + 22f;
            float textHeight = rect.yMax - textY;
            Rect textRect = new Rect(rect.x, textY, rect.width, textHeight);

            // 限制长度
            if (_globalPromptBuffer.Length > MaxSystemPromptLength)
                _globalPromptBuffer = _globalPromptBuffer.Substring(0, MaxSystemPromptLength);

            // 计算内容高度
            float contentHeight = Text.CalcHeight(_globalPromptBuffer, textRect.width - 20f);
            contentHeight = Mathf.Max(contentHeight, textRect.height);

            Rect viewRect = new Rect(0f, 0f, textRect.width - 20f, contentHeight);
            _globalPromptScroll = GUI.BeginScrollView(textRect, _globalPromptScroll, viewRect);

            GUI.SetNextControlName("GlobalPromptTextArea");
            _globalPromptBuffer = GUI.TextArea(viewRect, _globalPromptBuffer);

            GUI.EndScrollView();
            SystemPromptConfigData.GlobalSystemPrompt = _globalPromptBuffer;
        }

        private void DrawApiActionsEditorScrollable(Rect rect)
        {
            var actions = SystemPromptConfigData.ApiActions;
            if (actions == null || actions.Count == 0)
            {
                Widgets.Label(rect, "RimDiplomacy_NoApiActions".Translate());
                return;
            }

            // 左侧列表区域（固定宽度）
            float listWidth = 200f;
            Rect listRect = new Rect(rect.x, rect.y, listWidth, rect.height);
            
            // 右侧编辑区域
            Rect editRect = new Rect(rect.x + listWidth + 10f, rect.y, rect.width - listWidth - 10f, rect.height);

            // 绘制动作列表（带滚动）
            float itemHeight = 28f;
            float listContentHeight = actions.Count * itemHeight;
            Rect listContentRect = new Rect(0f, 0f, listWidth - 16f, Mathf.Max(listContentHeight, listRect.height));
            _selectedApiActionIndex = (int)_apiDescScroll.y; // 复用滚动变量存储选中项

            _apiDescScroll = GUI.BeginScrollView(listRect, _apiDescScroll, listContentRect);
            for (int i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                Rect rowRect = new Rect(0f, i * itemHeight, listContentRect.width, itemHeight - 2f);
                bool isSelected = i == (int)_selectedApiActionIndex;

                if (isSelected)
                    Widgets.DrawBoxSolid(rowRect, new Color(0.25f, 0.35f, 0.55f, 0.8f));
                else if (Mouse.IsOver(rowRect))
                    Widgets.DrawBoxSolid(rowRect, new Color(0.2f, 0.22f, 0.28f, 0.6f));

                string label = $"{(action.IsEnabled ? "[ON]" : "[OFF]")} {action.ActionName}";
                GUI.color = action.IsEnabled ? Color.white : Color.gray;
                Widgets.Label(rowRect.ContractedBy(4f), label.Truncate(rowRect.width - 8f));
                GUI.color = Color.white;

                if (Widgets.ButtonInvisible(rowRect))
                {
                    _selectedApiActionIndex = i;
                    _editingApiActionName = action.ActionName ?? "";
                    _editingApiActionDesc = action.Description ?? "";
                    _editingApiActionParams = action.Parameters ?? "";
                    _editingApiActionReq = action.Requirement ?? "";
                }
            }
            GUI.EndScrollView();

            // 绘制右侧编辑区域
            if (_selectedApiActionIndex >= 0 && _selectedApiActionIndex < actions.Count)
            {
                var action = actions[_selectedApiActionIndex];
                float y = editRect.y;

                // 标题
                GUI.color = SectionHeaderColor;
                Widgets.Label(new Rect(editRect.x, y, editRect.width, 24f), "RimDiplomacy_EditApiAction".Translate());
                GUI.color = Color.white;
                y += 28f;

                // 名称
                Widgets.Label(new Rect(editRect.x, y, editRect.width, 20f), "RimDiplomacy_ActionName".Translate());
                y += 22f;
                _editingApiActionName = Widgets.TextField(new Rect(editRect.x, y, editRect.width, 24f), _editingApiActionName);
                y += 28f;

                // 描述（大文本框，带滚动，填满剩余空间）
                Widgets.Label(new Rect(editRect.x, y, editRect.width, 20f), "RimDiplomacy_DescriptionLabel".Translate());
                y += 22f;
                float descHeight = editRect.yMax - y - 38f; // 预留按钮空间
                Rect descRect = new Rect(editRect.x, y, editRect.width, descHeight);
                
                float descContentHeight = Text.CalcHeight(_editingApiActionDesc, descRect.width - 16f);
                Rect descViewRect = new Rect(0f, 0f, descRect.width - 16f, Mathf.Max(descContentHeight, descRect.height));
                _globalPromptScroll = GUI.BeginScrollView(descRect, _globalPromptScroll, descViewRect);
                _editingApiActionDesc = GUI.TextArea(descViewRect, _editingApiActionDesc);
                GUI.EndScrollView();
                
                action.Description = _editingApiActionDesc;

                // 启用/禁用按钮（固定在底部）
                Rect btnRect = new Rect(editRect.x, editRect.yMax - 28f, 100f, 28f);
                if (Widgets.ButtonText(btnRect, action.IsEnabled ? "RimDiplomacy_Disable".Translate() : "RimDiplomacy_Enable".Translate()))
                    action.IsEnabled = !action.IsEnabled;
            }
            else
            {
                // 未选中任何项时显示提示
                GUI.color = Color.gray;
                Text.Font = GameFont.Medium;
                Widgets.Label(editRect.ContractedBy(20f), "RimDiplomacy_SelectApiAction".Translate());
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }
        }

        private void DrawResponseFormatEditorScrollable(Rect rect)
        {
            var format = SystemPromptConfigData.ResponseFormat;
            if (format == null)
            {
                format = new ResponseFormatConfig();
                SystemPromptConfigData.ResponseFormat = format;
            }

            float y = rect.y;

            // JSON模板 - 带滚动条（填满剩余空间）
            Widgets.Label(new Rect(rect.x, y, rect.width, 20f), "RimDiplomacy_JsonTemplateLabel".Translate());
            y += 22f;

            float textHeight = rect.yMax - y - 29f; // 预留复选框空间
            Rect textRect = new Rect(rect.x, y, rect.width, textHeight);

            float contentHeight = Text.CalcHeight(_jsonTemplateBuffer, textRect.width - 20f);
            contentHeight = Mathf.Max(contentHeight, textRect.height);

            Rect viewRect = new Rect(0f, 0f, textRect.width - 20f, contentHeight);
            _jsonTemplateScroll = GUI.BeginScrollView(textRect, _jsonTemplateScroll, viewRect);

            _jsonTemplateBuffer = GUI.TextArea(viewRect, _jsonTemplateBuffer);

            GUI.EndScrollView();
            format.JsonTemplate = _jsonTemplateBuffer;

            // 复选框（固定在底部）
            Rect checkRect = new Rect(rect.x, rect.yMax - 24f, rect.width, 24f);
            Widgets.CheckboxLabeled(checkRect, "RimDiplomacy_IncludeRelationChangesLabel".Translate(), ref format.IncludeRelationChanges);
        }

        private void DrawDecisionRulesEditorScrollable(Rect rect)
        {
            var rules = SystemPromptConfigData.DecisionRules;
            if (rules == null || rules.Count == 0)
            {
                Widgets.Label(rect, "RimDiplomacy_NoDecisionRules".Translate());
                return;
            }

            // 左侧列表区域（固定宽度）
            float listWidth = 200f;
            Rect listRect = new Rect(rect.x, rect.y, listWidth, rect.height);
            
            // 右侧编辑区域
            Rect editRect = new Rect(rect.x + listWidth + 10f, rect.y, rect.width - listWidth - 10f, rect.height);

            // 绘制规则列表（带滚动）
            float itemHeight = 28f;
            float listContentHeight = rules.Count * itemHeight;
            Rect listContentRect = new Rect(0f, 0f, listWidth - 16f, Mathf.Max(listContentHeight, listRect.height));

            _ruleContentScroll = GUI.BeginScrollView(listRect, _ruleContentScroll, listContentRect);
            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                Rect rowRect = new Rect(0f, i * itemHeight, listContentRect.width, itemHeight - 2f);
                bool isSelected = i == _selectedDecisionRuleIndex;

                if (isSelected)
                    Widgets.DrawBoxSolid(rowRect, new Color(0.25f, 0.35f, 0.55f, 0.8f));
                else if (Mouse.IsOver(rowRect))
                    Widgets.DrawBoxSolid(rowRect, new Color(0.2f, 0.22f, 0.28f, 0.6f));

                string label = $"{(rule.IsEnabled ? "[ON]" : "[OFF]")} {rule.RuleName}";
                GUI.color = rule.IsEnabled ? Color.white : Color.gray;
                Widgets.Label(rowRect.ContractedBy(4f), label.Truncate(rowRect.width - 8f));
                GUI.color = Color.white;

                if (Widgets.ButtonInvisible(rowRect))
                {
                    _selectedDecisionRuleIndex = i;
                    _editingRuleName = rule.RuleName ?? "";
                    _editingRuleContent = rule.RuleContent ?? "";
                }
            }
            GUI.EndScrollView();

            // 绘制右侧编辑区域
            if (_selectedDecisionRuleIndex >= 0 && _selectedDecisionRuleIndex < rules.Count)
            {
                var rule = rules[_selectedDecisionRuleIndex];
                float y = editRect.y;

                // 标题
                GUI.color = SectionHeaderColor;
                Widgets.Label(new Rect(editRect.x, y, editRect.width, 24f), "RimDiplomacy_EditDecisionRule".Translate());
                GUI.color = Color.white;
                y += 28f;

                // 名称
                Widgets.Label(new Rect(editRect.x, y, editRect.width, 20f), "RimDiplomacy_RuleNameLabel".Translate());
                y += 22f;
                _editingRuleName = Widgets.TextField(new Rect(editRect.x, y, editRect.width, 24f), _editingRuleName);
                y += 28f;

                // 规则内容（大文本框，带滚动，填满剩余空间）
                Widgets.Label(new Rect(editRect.x, y, editRect.width, 20f), "RimDiplomacy_RuleContentLabel".Translate());
                y += 22f;
                float contentHeight = editRect.yMax - y - 38f; // 预留按钮空间
                Rect contentRect = new Rect(editRect.x, y, editRect.width, contentHeight);
                
                float ruleContentHeight = Text.CalcHeight(_editingRuleContent, contentRect.width - 16f);
                Rect contentViewRect = new Rect(0f, 0f, contentRect.width - 16f, Mathf.Max(ruleContentHeight, contentRect.height));
                _jsonTemplateScroll = GUI.BeginScrollView(contentRect, _jsonTemplateScroll, contentViewRect);
                _editingRuleContent = GUI.TextArea(contentViewRect, _editingRuleContent);
                GUI.EndScrollView();
                
                rule.RuleContent = _editingRuleContent;

                // 启用/禁用按钮（固定在底部）
                Rect btnRect = new Rect(editRect.x, editRect.yMax - 28f, 100f, 28f);
                if (Widgets.ButtonText(btnRect, rule.IsEnabled ? "RimDiplomacy_Disable".Translate() : "RimDiplomacy_Enable".Translate()))
                    rule.IsEnabled = !rule.IsEnabled;
            }
            else
            {
                // 未选中任何项时显示提示
                GUI.color = Color.gray;
                Text.Font = GameFont.Medium;
                Widgets.Label(editRect.ContractedBy(20f), "RimDiplomacy_SelectDecisionRule".Translate());
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }
        }

        private void DrawDynamicDataEditor(Rect rect)
        {
            var dynConfig = SystemPromptConfigData.DynamicDataInjection;
            if (dynConfig == null)
            {
                dynConfig = new DynamicDataInjectionConfig();
                SystemPromptConfigData.DynamicDataInjection = dynConfig;
            }

            float y = rect.y;

            Rect check1 = new Rect(rect.x, y, rect.width, 24f);
            Widgets.CheckboxLabeled(check1, "RimDiplomacy_InjectRelationContext".Translate(), ref dynConfig.InjectRelationContext);
            y += 28f;

            Rect check2 = new Rect(rect.x, y, rect.width, 24f);
            Widgets.CheckboxLabeled(check2, "RimDiplomacy_InjectMemoryData".Translate(), ref dynConfig.InjectMemoryData);
            y += 28f;

            Rect check3 = new Rect(rect.x, y, rect.width, 24f);
            Widgets.CheckboxLabeled(check3, "RimDiplomacy_InjectFiveDimensionData".Translate(), ref dynConfig.InjectFiveDimensionData);
            y += 28f;

            Rect check4 = new Rect(rect.x, y, rect.width, 24f);
            Widgets.CheckboxLabeled(check4, "RimDiplomacy_InjectFactionInfo".Translate(), ref dynConfig.InjectFactionInfo);
        }

        private void DrawPreviewRight(Rect rect)
        {
            // 预览标题
            Rect titleRect = new Rect(rect.x, rect.y, rect.width, 20f);
            GUI.color = new Color(0.5f, 0.8f, 0.5f);
            Widgets.Label(titleRect, "RimDiplomacy_PreviewTitleShort".Translate());
            GUI.color = Color.white;

            // 预览内容框
            Rect contentRect = new Rect(rect.x, rect.y + 22f, rect.width, rect.height - 22f);
            Widgets.DrawBoxSolid(contentRect, new Color(0.08f, 0.1f, 0.08f));
            Widgets.DrawBox(contentRect);

            UpdatePreviewText();

            Rect innerRect = contentRect.ContractedBy(4f);

            // 计算预览内容高度
            float contentHeight = Text.CalcHeight(_cachedPreviewText, innerRect.width - 20f);
            contentHeight = Mathf.Max(contentHeight, innerRect.height);

            Rect viewRect = new Rect(0f, 0f, innerRect.width - 20f, contentHeight);
            _previewScroll = GUI.BeginScrollView(innerRect, _previewScroll, viewRect);

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.6f, 0.7f, 0.6f);
            Widgets.Label(viewRect, _cachedPreviewText);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            GUI.EndScrollView();
        }

        private string GetSectionLabel(string sectionName)
        {
            return sectionName switch
            {
                "GlobalPrompt" => "RimDiplomacy_GlobalSystemPromptSection".Translate(),
                "ApiActions" => "RimDiplomacy_ApiActionsSection".Translate(),
                "ResponseFormat" => "RimDiplomacy_ResponseFormatSection".Translate(),
                "DecisionRules" => "RimDiplomacy_DecisionRulesSection".Translate(),
                "DynamicData" => "RimDiplomacy_DynamicDataInjectionSection".Translate(),
                _ => sectionName
            };
        }

        private void SyncBuffersToData()
        {
            _globalPromptBuffer = SystemPromptConfigData.GlobalSystemPrompt ?? "";
            _jsonTemplateBuffer = SystemPromptConfigData.ResponseFormat?.JsonTemplate ?? "";
            _relationChangesBuffer = SystemPromptConfigData.ResponseFormat?.RelationChangesTemplate ?? "";
            _importantRulesBuffer = SystemPromptConfigData.ResponseFormat?.ImportantRules ?? "";
        }

        private void UpdatePreviewText()
        {
            _previewUpdateCooldown--;
            if (_previewUpdateCooldown <= 0)
            {
                _cachedPreviewText = GeneratePreviewText();
                _previewUpdateCooldown = 60;
            }
        }

        private string GeneratePreviewText()
        {
            try
            {
                var sb = new StringBuilder();
                var config = SystemPromptConfigData;

                if (!string.IsNullOrEmpty(config.GlobalSystemPrompt))
                {
                    sb.AppendLine("=== System Prompt ===");
                    sb.AppendLine(config.GlobalSystemPrompt);
                    sb.AppendLine();
                }

                if (config.DynamicDataInjection != null)
                {
                    sb.AppendLine("=== Data Injection ===");
                    if (config.DynamicDataInjection.InjectRelationContext) sb.AppendLine("- Relation Context");
                    if (config.DynamicDataInjection.InjectMemoryData) sb.AppendLine("- Memory Data");
                    if (config.DynamicDataInjection.InjectFiveDimensionData) sb.AppendLine("- Five Dimension");
                    if (config.DynamicDataInjection.InjectFactionInfo) sb.AppendLine("- Faction Info");
                    sb.AppendLine();
                }

                if (config.ApiActions != null && config.ApiActions.Count > 0)
                {
                    sb.AppendLine("=== API Actions ===");
                    foreach (var action in config.ApiActions.Where(a => a.IsEnabled))
                    {
                        sb.AppendLine($"- {action.ActionName}");
                    }
                    sb.AppendLine();
                }

                if (config.DecisionRules != null && config.DecisionRules.Count > 0)
                {
                    sb.AppendLine("=== Decision Rules ===");
                    foreach (var rule in config.DecisionRules.Where(r => r.IsEnabled))
                    {
                        sb.AppendLine($"- {rule.RuleName}");
                    }
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        private void DrawPromptActionButtonsNative(Listing_Standard listing)
        {
            Rect buttonRowRect = listing.GetRect(28f);
            float btnWidth = (buttonRowRect.width - 30f) / 4;

            Rect saveRect = new Rect(buttonRowRect.x, buttonRowRect.y, btnWidth, buttonRowRect.height);
            if (Widgets.ButtonText(saveRect, "RimDiplomacy_SavePrompt".Translate()))
            {
                SaveSystemPromptConfig();
                Messages.Message("RimDiplomacy_PromptSaved".Translate(), MessageTypeDefOf.NeutralEvent, false);
            }

            Rect resetRect = new Rect(buttonRowRect.x + btnWidth + 10f, buttonRowRect.y, btnWidth, buttonRowRect.height);
            if (Widgets.ButtonText(resetRect, "RimDiplomacy_ResetToDefault".Translate()))
            {
                ShowResetPromptConfigConfirmation();
            }

            Rect exportRect = new Rect(buttonRowRect.x + (btnWidth + 10f) * 2, buttonRowRect.y, btnWidth, buttonRowRect.height);
            if (Widgets.ButtonText(exportRect, "RimDiplomacy_ExportPrompts".Translate()))
            {
                ShowExportSystemPromptDialog();
            }

            Rect importRect = new Rect(buttonRowRect.x + (btnWidth + 10f) * 3, buttonRowRect.y, btnWidth, buttonRowRect.height);
            if (Widgets.ButtonText(importRect, "RimDiplomacy_ImportPrompts".Translate()))
            {
                ShowImportSystemPromptDialog();
            }
        }

        private void SaveSystemPromptConfig()
        {
            PromptPersistenceService.Instance.SaveConfig(SystemPromptConfigData);
            _previewUpdateCooldown = 0;
        }

        private void ShowResetPromptConfigConfirmation()
        {
            Dialog_MessageBox dialog = Dialog_MessageBox.CreateConfirmation(
                "RimDiplomacy_ResetPromptConfigConfirm".Translate(),
                () =>
                {
                    PromptPersistenceService.Instance.ResetToDefault();
                    _systemPromptConfig = PromptPersistenceService.Instance.LoadConfig();
                    _selectedApiActionIndex = -1;
                    _selectedDecisionRuleIndex = -1;
                    _previewUpdateCooldown = 0;
                    SyncBuffersToData();
                    Messages.Message("RimDiplomacy_PromptConfigReset".Translate(), MessageTypeDefOf.NeutralEvent, false);
                },
                true,
                "RimDiplomacy_ResetConfirmTitle".Translate()
            );
            Find.WindowStack.Add(dialog);
        }

        private void ShowExportSystemPromptDialog()
        {
            string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "RimDiplomacy_SystemPrompt.json");
            Find.WindowStack.Add(new Dialog_SaveFile(defaultPath, (path) =>
            {
                if (PromptPersistenceService.Instance.ExportConfig(path))
                {
                    Messages.Message("RimDiplomacy_ExportSuccess".Translate(path), MessageTypeDefOf.NeutralEvent, false);
                }
                else
                {
                    Messages.Message("RimDiplomacy_ExportFailed".Translate(), MessageTypeDefOf.NegativeEvent, false);
                }
            }));
        }

        private void ShowImportSystemPromptDialog()
        {
            string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "RimDiplomacy_SystemPrompt.json");
            Find.WindowStack.Add(new Dialog_LoadFile(defaultPath, (path) =>
            {
                if (PromptPersistenceService.Instance.ImportConfig(path))
                {
                    _systemPromptConfig = PromptPersistenceService.Instance.LoadConfig();
                    _selectedApiActionIndex = -1;
                    _selectedDecisionRuleIndex = -1;
                    _previewUpdateCooldown = 0;
                    SyncBuffersToData();
                    Messages.Message("RimDiplomacy_ImportSuccess".Translate(), MessageTypeDefOf.NeutralEvent, false);
                }
                else
                {
                    Messages.Message("RimDiplomacy_ImportFailed".Translate(), MessageTypeDefOf.NegativeEvent, false);
                }
            }));
        }
    }
}
