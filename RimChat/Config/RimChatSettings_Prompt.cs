using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using RimChat.Persistence;
using RimChat.UI;
using RimChat.Core;
using RimChat.Prompting;

namespace RimChat.Config
{
    public partial class RimChatSettings
    {
        private SystemPromptConfig _systemPromptConfig;
        private bool _advancedPromptMode = false;
        private bool _promptWorkbenchFailed;

        private int _selectedSectionIndex = 0;
        private int _selectedApiActionIndex = -1;
        private int _selectedDecisionRuleIndex = -1;
        private int _selectedFactionPromptIndex = -1;

        private string _editingApiActionName = "";
        private string _editingApiActionDesc = "";
        private string _editingApiActionParams = "";
        private string _editingApiActionReq = "";
        private string _editingRuleName = "";
        private string _editingRuleContent = "";

        // 鏂囨湰缂撳啿鍖?
        private string _globalPromptBuffer = "";
        private string _globalDialoguePromptBuffer = "";
        private string _jsonTemplateBuffer = "";
        private string _importantRulesBuffer = "";

        // 婊氬姩浣嶇疆
        private Vector2 _globalPromptScroll = Vector2.zero;
        private Vector2 _globalDialoguePromptScroll = Vector2.zero;
        private Vector2 _navigationSectionScroll = Vector2.zero;
        private Vector2 _apiActionListScroll = Vector2.zero;
        private Vector2 _apiActionDescScroll = Vector2.zero;
        private Vector2 _jsonTemplateScroll = Vector2.zero;
        private Vector2 _importantRulesScroll = Vector2.zero;
        private Vector2 _ruleContentScroll = Vector2.zero;
        private Vector2 _previewScroll = Vector2.zero;
        private Vector2 _factionPromptScroll = Vector2.zero;

        private string _cachedPreviewText = "";
        private int _previewUpdateCooldown = 0;
        private bool _previewCollapsed = true;
        private float _previewFoldAnimTime = 0f;
        private TemplateVariableValidationResult _liveValidationResult = new TemplateVariableValidationResult();
        private string _liveValidationSignature = string.Empty;
        private int _liveValidationCooldown = 0;
        private const int LiveValidationRefreshTicks = 15;

        private static readonly Color SectionHeaderColor = new Color(0.9f, 0.7f, 0.4f);

        // 鍒嗗尯瀹氫箟 - 绠€鍗曟ā寮忓拰楂樼骇妯″紡鍏辩敤
        private static readonly string[] SimpleSectionNames = new string[]
        {
            "GlobalPrompt",
            "EnvironmentPrompts",
            "SocialCirclePrompts",
            "DynamicData"
        };

        private static readonly string[] AdvancedSectionNames = new string[]
        {
            "GlobalPrompt",
            "EnvironmentPrompts",
            "FactionPrompts",
            "ApiActions",
            "JsonTemplate",
            "ImportantRules",
            "PromptTemplates",
            "SocialCirclePrompts",
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
            if (_promptWorkbenchFailed)
            {
                DrawLegacyAdvancedPromptSettingsSection(listing);
                return;
            }

            try
            {
                DrawAdvancedPromptWorkbench(listing);
            }
            catch (Exception ex)
            {
                _promptWorkbenchFailed = true;
                Log.Error($"[RimChat] Prompt workbench render failed, fallback to legacy prompt UI: {ex}");
                DrawLegacyAdvancedPromptSettingsSection(listing);
            }
        }

        private void DrawLegacyAdvancedPromptSettingsSection(Listing_Standard listing)
        {
            float totalHeight = 520f;
            Rect mainRect = listing.GetRect(totalHeight);

            InitBuffers();

            float navWidth = mainRect.width / 3.5f;
            float editorWidth = mainRect.width - navWidth - 10f;

            Rect navRect = new Rect(mainRect.x, mainRect.y, navWidth, totalHeight);
            Rect editorRect = new Rect(mainRect.x + navWidth + 10f, mainRect.y, editorWidth, totalHeight);

            DrawNavigationPanelWithButtons(navRect);
            DrawEditorPanelWithPreview(editorRect);
        }

        internal void DrawLegacyPromptPageDirect(Rect rect)
        {
            float totalHeight = Mathf.Min(620f, rect.height);
            Rect mainRect = new Rect(rect.x, rect.y, rect.width, totalHeight);

            InitBuffers();

            float navWidth = mainRect.width / 3.5f;
            float editorWidth = mainRect.width - navWidth - 10f;
            Rect navRect = new Rect(mainRect.x, mainRect.y, navWidth, totalHeight);
            Rect editorRect = new Rect(mainRect.x + navWidth + 10f, mainRect.y, editorWidth, totalHeight);
            DrawNavigationPanelWithButtons(navRect);
            DrawEditorPanelWithPreview(editorRect);
        }

        private void InitBuffers()
        {
            if (string.IsNullOrEmpty(_globalPromptBuffer))
                _globalPromptBuffer = SystemPromptConfigData.GlobalSystemPrompt ?? "";
            if (string.IsNullOrEmpty(_globalDialoguePromptBuffer))
                _globalDialoguePromptBuffer = SystemPromptConfigData.GlobalDialoguePrompt ?? "";
            if (string.IsNullOrEmpty(_jsonTemplateBuffer))
                _jsonTemplateBuffer = SystemPromptConfigData.ResponseFormat?.JsonTemplate ?? "";
            if (string.IsNullOrEmpty(_importantRulesBuffer))
                _importantRulesBuffer = SystemPromptConfigData.ResponseFormat?.ImportantRules ?? "";
        }

        private void DrawNavigationPanelWithButtons(Rect rect)
        {
            // 鑳屾櫙
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.14f));


            Rect innerRect = rect.ContractedBy(8f);
            float y = innerRect.y;

            // 妯″紡鍒囨崲灏忔寜閽紙鏀惧湪宸︿笂瑙掞級
            Rect toggleRect = new Rect(innerRect.x, y, innerRect.width, 24f);
            DrawModeToggleSmall(toggleRect);
            y += 30f;

            // 鍒嗛殧绾?
            Widgets.DrawLineHorizontal(innerRect.x, y, innerRect.width);
            y += 10f;

            // 鏍规嵁妯″紡鑾峰彇鍒嗗尯鍒楄〃
            string[] sections = _advancedPromptMode ? AdvancedSectionNames : SimpleSectionNames;

            // 璁＄畻鍒嗗尯鍒楄〃鍖哄煙楂樺害锛堥鐣欐寜閽尯鍩燂級
            float buttonAreaHeight = 210f;
            float listHeight = innerRect.height - y - buttonAreaHeight;

            // 鍒嗗尯鍒楄〃鍖哄煙锛堝甫婊氬姩锛?
            Rect listRect = new Rect(innerRect.x, y, innerRect.width, listHeight);
            
            // 璁＄畻鍐呭楂樺害
            float contentHeight = sections.Length * 32f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, Mathf.Max(contentHeight, listHeight));
            
            // 浣跨敤鐙珛鐨勬粴鍔ㄤ綅缃?
            _navigationSectionScroll = GUI.BeginScrollView(listRect, _navigationSectionScroll, viewRect);
            
            // 缁樺埗鍒嗗尯鎸夐挳
            for (int i = 0; i < sections.Length; i++)
            {
                string sectionName = sections[i];
                bool isSelected = _selectedSectionIndex == i;

                Rect btnRect = new Rect(0f, i * 32f, viewRect.width, 28f);

                // 閫変腑鐘舵€佽儗鏅?
                if (isSelected)
                {
                    Widgets.DrawBoxSolid(btnRect, new Color(0.25f, 0.35f, 0.55f));
                }
                else if (Mouse.IsOver(btnRect))
                {
                    Widgets.DrawBoxSolid(btnRect, new Color(0.2f, 0.22f, 0.28f));
                }

                // 宸﹁竟妗嗗己璋?
                if (isSelected)
                {
                    Rect accentRect = new Rect(btnRect.x, btnRect.y, 3f, btnRect.height);
                    Widgets.DrawBoxSolid(accentRect, new Color(0.4f, 0.7f, 1f));
                }

                // 鏂囧瓧
                GUI.color = isSelected ? Color.white : new Color(0.7f, 0.7f, 0.75f);
                TextAnchor oldAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleLeft;
                string label = GetSectionLabel(sectionName);
                Widgets.Label(new Rect(btnRect.x + 8f, btnRect.y, btnRect.width - 16f, btnRect.height), label);
                Text.Anchor = oldAnchor;
                GUI.color = Color.white;
                RegisterTooltip(btnRect, GetPromptSectionTooltipKey(sectionName));

                // 鐐瑰嚮澶勭悊
                if (Widgets.ButtonInvisible(btnRect))
                {
                    _selectedSectionIndex = i;
                    _selectedApiActionIndex = -1;
                    _selectedDecisionRuleIndex = -1;
                }
            }
            
            GUI.EndScrollView();

            // 鎸夐挳鍖哄煙锛堝湪瀵艰埅鏍忓簳閮級
            y += listHeight + 10f;
            Rect buttonAreaRect = new Rect(innerRect.x, y, innerRect.width, buttonAreaHeight - 10f);
            
            // 鍒嗛殧绾?
            Widgets.DrawLineHorizontal(innerRect.x, y - 5f, innerRect.width);
            
            // 缁樺埗鎸夐挳
            DrawPromptActionButtonsVertical(buttonAreaRect);
        }

        private void DrawPromptActionButtonsVertical(Rect rect)
        {
            float btnHeight = 26f;
            float gap = 6f;
            float y = rect.y;

            Rect saveRect = new Rect(rect.x, y, rect.width, btnHeight);
            if (Widgets.ButtonText(saveRect, "RimChat_SavePrompt".Translate()))
            {
                SaveSystemPromptConfig();
                Messages.Message("RimChat_PromptSaved".Translate(), MessageTypeDefOf.NeutralEvent, false);
            }
            y += btnHeight + gap;

            Rect resetRect = new Rect(rect.x, y, rect.width, btnHeight);
            if (Widgets.ButtonText(resetRect, "RimChat_ResetToDefault".Translate()))
            {
                ShowResetPromptConfigConfirmation();
            }
            y += btnHeight + gap;

            Rect exportRect = new Rect(rect.x, y, rect.width, btnHeight);
            if (Widgets.ButtonText(exportRect, "RimChat_ExportPrompts".Translate()))
            {
                ShowExportSystemPromptDialog();
            }
            y += btnHeight + gap;

            Rect importRect = new Rect(rect.x, y, rect.width, btnHeight);
            if (Widgets.ButtonText(importRect, "RimChat_ImportPrompts".Translate()))
            {
                ShowImportSystemPromptDialog();
            }
        }

        private void DrawModeToggleSmall(Rect rect)
        {
            float btnWidth = rect.width / 2 - 2f;

            // 绠€鍗曟ā寮忔寜閽?
            Rect simpleRect = new Rect(rect.x, rect.y, btnWidth, rect.height);
            bool isSimple = !_advancedPromptMode;

            GUI.color = isSimple ? new Color(0.3f, 0.6f, 0.9f) : new Color(0.18f, 0.18f, 0.2f);
            Widgets.DrawBoxSolid(simpleRect, GUI.color);
            GUI.color = isSimple ? Color.white : Color.gray;
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(simpleRect, "RimChat_SimpleModeShort".Translate());
            Text.Anchor = oldAnchor;
            GUI.color = Color.white;

            if (Widgets.ButtonInvisible(simpleRect))
            {
                _advancedPromptMode = false;
                _selectedSectionIndex = 0;
                SyncBuffersToData();
            }

            // 楂樼骇妯″紡鎸夐挳
            Rect advancedRect = new Rect(rect.x + btnWidth + 4f, rect.y, btnWidth, rect.height);
            bool isAdvanced = _advancedPromptMode;

            GUI.color = isAdvanced ? new Color(0.9f, 0.5f, 0.25f) : new Color(0.18f, 0.18f, 0.2f);
            Widgets.DrawBoxSolid(advancedRect, GUI.color);
            GUI.color = isAdvanced ? Color.white : Color.gray;
            TextAnchor oldAnchor2 = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(advancedRect, "RimChat_AdvancedModeShort".Translate());
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
            // 鑳屾櫙
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.12f));


            Rect innerRect = rect.ContractedBy(10f);

            // 鑾峰彇褰撳墠鍒嗗尯
            string[] sections = _advancedPromptMode ? AdvancedSectionNames : SimpleSectionNames;
            if (_selectedSectionIndex >= sections.Length)
                _selectedSectionIndex = 0;

            string currentSection = sections[_selectedSectionIndex];

            // 璁＄畻甯冨眬锛氱紪杈戝尯 + 棰勮鍖?
            float titleHeight = 30f;
            float previewHeight = _previewCollapsed ? 40f : 300f;
            float previewGap = 10f;
            float editorHeight = innerRect.height - titleHeight - previewGap - previewHeight;

            // 鍒嗗尯鏍囬
            Rect titleRect = new Rect(innerRect.x, innerRect.y, innerRect.width, titleHeight);
            GUI.color = SectionHeaderColor;
            Text.Font = GameFont.Medium;
            Widgets.Label(titleRect, GetSectionLabel(currentSection));
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            // 缂栬緫鍖哄煙锛堝埌搴曢儴锛?
            Rect contentRect = new Rect(innerRect.x, innerRect.y + titleHeight, innerRect.width, editorHeight);
            switch (currentSection)
            {
                case "GlobalPrompt":
                    DrawGlobalPromptEditorScrollable(contentRect);
                    break;
                case "FactionPrompts":
                    DrawFactionPromptsEditorScrollable(contentRect);
                    break;
                case "EnvironmentPrompts":
                    DrawEnvironmentPromptsEditorScrollable(contentRect);
                    break;
                case "ApiActions":
                    DrawApiActionsEditorScrollable(contentRect);
                    break;
                case "JsonTemplate":
                    DrawJsonTemplateEditorScrollable(contentRect);
                    break;
                case "ImportantRules":
                    DrawImportantRulesEditorScrollable(contentRect);
                    break;
                case "PromptTemplates":
                    DrawPromptTemplatesEditorScrollable(contentRect);
                    break;
                case "SocialCirclePrompts":
                    DrawSocialCirclePromptEditorScrollable(contentRect);
                    break;
                case "DecisionRules":
                    DrawDecisionRulesEditorScrollable(contentRect);
                    break;
                case "DynamicData":
                    DrawDynamicDataEditor(contentRect);
                    break;
            }

            // 棰勮鍖哄煙锛堝湪鍙充晶涓嬫柟锛屽缁堟樉绀猴級
            float previewY = innerRect.y + titleHeight + editorHeight + previewGap;
            Rect previewRect = new Rect(innerRect.x, previewY, innerRect.width, previewHeight);
            DrawPreviewRight(previewRect);
        }

        private void DrawGlobalPromptEditorScrollable(Rect rect)
        {
            float labelHeight = 20f;
            float gap = 8f;
            float available = rect.height - labelHeight - (gap * 2f);
            float editorHeight = Mathf.Max(140f, available);
            float y = rect.y;

            Rect systemLabelRect = new Rect(rect.x, y, rect.width, labelHeight);
            Widgets.Label(systemLabelRect, "RimChat_GlobalSystemPromptSection".Translate());
            RegisterTooltip(systemLabelRect, "RimChat_GlobalSystemPromptSectionTooltip");
            y += labelHeight + 2f;
            DrawGlobalPromptTextArea(new Rect(rect.x, y, rect.width - 16f, editorHeight), ref _globalPromptBuffer, ref _globalPromptScroll, "GlobalPromptTextArea");

            SystemPromptConfigData.GlobalSystemPrompt = _globalPromptBuffer;
        }

        private static void DrawGlobalPromptTextArea(Rect textRect, ref string buffer, ref Vector2 scroll, string controlName)
        {
            float contentHeight = Mathf.Max(textRect.height, Text.CalcHeight(buffer ?? string.Empty, textRect.width - 16f) + 10f);
            Rect viewRect = new Rect(0f, 0f, textRect.width - 16f, contentHeight);
            scroll = GUI.BeginScrollView(textRect, scroll, viewRect);
            GUI.SetNextControlName(controlName);
            buffer = GUI.TextArea(viewRect, buffer ?? string.Empty);
            GUI.EndScrollView();
        }

        private void DrawApiActionsEditorScrollable(Rect rect)
        {
            bool editingEnabledBefore = EnableApiPromptEditing;
            Rect toggleRect = new Rect(rect.x, rect.y, rect.width, 24f);
            Widgets.CheckboxLabeled(toggleRect, "RimChat_EnableApiPromptEditing".Translate(), ref EnableApiPromptEditing);
            if (!editingEnabledBefore && EnableApiPromptEditing)
            {
                ShowApiPromptEditingWarningDialog();
            }

            if (!EnableApiPromptEditing)
            {
                GUI.color = Color.yellow;
                Widgets.Label(new Rect(rect.x, rect.y + 28f, rect.width, 24f), "RimChat_ApiPromptEditingLocked".Translate());
                GUI.color = Color.white;
                return;
            }

            float contentTop = rect.y + 30f;
            Rect contentRect = new Rect(rect.x, contentTop, rect.width, rect.height - 30f);

            var actions = GetEditableApiActions();
            if (actions == null || actions.Count == 0)
            {
                Widgets.Label(contentRect, "RimChat_NoApiActions".Translate());
                return;
            }

            // 宸︿晶鍒楄〃鍖哄煙锛堝浐瀹氬搴︼級
            float listWidth = 220f;
            float buttonHeight = 32f;
            Rect listRect = new Rect(contentRect.x, contentRect.y, listWidth, contentRect.height - buttonHeight);
            
            // 鍙充晶缂栬緫鍖哄煙
            Rect editRect = new Rect(contentRect.x + listWidth + 10f, contentRect.y, contentRect.width - listWidth - 10f, contentRect.height - buttonHeight);

            // 缁樺埗鍔ㄤ綔鍒楄〃锛堝甫婊氬姩锛?
            float itemHeight = 30f;
            float listContentHeight = actions.Count * itemHeight;
            Rect listContentRect = new Rect(0f, 0f, listWidth - 16f, Mathf.Max(listContentHeight, listRect.height));

            _apiActionListScroll = GUI.BeginScrollView(listRect, _apiActionListScroll, listContentRect);
            for (int i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                Rect rowRect = new Rect(0f, i * itemHeight, listContentRect.width, itemHeight - 1f);
                bool isSelected = i == _selectedApiActionIndex;

                if (isSelected)
                    Widgets.DrawBoxSolid(rowRect, new Color(0.25f, 0.35f, 0.55f, 0.8f));
                else if (Mouse.IsOver(rowRect))
                    Widgets.DrawBoxSolid(rowRect, new Color(0.2f, 0.22f, 0.28f, 0.6f));

                string label = $"{(action.IsEnabled ? "[ON]" : "[OFF]")} {action.ActionName}";
                GUI.color = action.IsEnabled ? Color.white : Color.gray;
                TextAnchor oldAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleLeft;
                Rect labelRect = new Rect(rowRect.x + 8f, rowRect.y + 2f, rowRect.width - 16f, rowRect.height - 4f);
                Widgets.Label(labelRect, label.Truncate(labelRect.width));
                Text.Anchor = oldAnchor;
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

            // 鏂板鎸夐挳锛堝垪琛ㄥ簳閮級
            Rect addBtnRect = new Rect(contentRect.x, contentRect.yMax - buttonHeight, listWidth, buttonHeight - 4f);
            if (Widgets.ButtonText(addBtnRect, "RimChat_AddNew".Translate()))
            {
                AddNewApiAction();
            }

            // 缁樺埗鍙充晶缂栬緫鍖哄煙
            if (_selectedApiActionIndex >= 0 && _selectedApiActionIndex < actions.Count)
            {
                var action = actions[_selectedApiActionIndex];
                float y = editRect.y;

                // 鏍囬
                GUI.color = SectionHeaderColor;
                Widgets.Label(new Rect(editRect.x, y, editRect.width, 24f), "RimChat_EditApiAction".Translate());
                GUI.color = Color.white;
                y += 28f;

                // 鍚嶇О
                Widgets.Label(new Rect(editRect.x, y, editRect.width, 20f), "RimChat_ActionName".Translate());
                y += 22f;
                _editingApiActionName = Widgets.TextField(new Rect(editRect.x, y, editRect.width, 24f), _editingApiActionName);
                y += 28f;

                // 鍙傛暟
                Widgets.Label(new Rect(editRect.x, y, editRect.width, 20f), "RimChat_ParametersLabel".Translate());
                y += 22f;
                _editingApiActionParams = Widgets.TextField(new Rect(editRect.x, y, editRect.width, 24f), _editingApiActionParams);
                y += 28f;

                // 闄愬埗鏉′欢
                Widgets.Label(new Rect(editRect.x, y, editRect.width, 20f), "RimChat_RequirementLabel".Translate());
                y += 22f;
                _editingApiActionReq = Widgets.TextField(new Rect(editRect.x, y, editRect.width, 24f), _editingApiActionReq);
                y += 28f;

                // 鎻忚堪锛堝ぇ鏂囨湰妗嗭紝甯︽粴鍔紝濉弧鍓╀綑绌洪棿锛?
                Widgets.Label(new Rect(editRect.x, y, editRect.width, 20f), "RimChat_DescriptionLabel".Translate());
                y += 22f;
                float descHeight = editRect.yMax - y - 40f; // 涓哄簳閮ㄦ寜閽暀鍑虹┖闂?
                Rect descRect = new Rect(editRect.x, y, editRect.width - 16f, descHeight);
                
                // 璁＄畻瀹為檯鍐呭楂樺害锛岀‘淇濆畬鏁存樉绀?
                float descContentHeight = Mathf.Max(descRect.height, Text.CalcHeight(_editingApiActionDesc, descRect.width - 16f) + 10f);
                Rect descViewRect = new Rect(0f, 0f, descRect.width - 16f, descContentHeight);
                _apiActionDescScroll = GUI.BeginScrollView(descRect, _apiActionDescScroll, descViewRect);
                _editingApiActionDesc = GUI.TextArea(descViewRect, _editingApiActionDesc);
                GUI.EndScrollView();
                
                action.Description = _editingApiActionDesc;
                action.ActionName = _editingApiActionName;
                action.Parameters = _editingApiActionParams;
                action.Requirement = _editingApiActionReq;

                // 鎸夐挳鍖哄煙锛堝浐瀹氬湪搴曢儴锛?
                float btnWidth = 100f;
                float btnGap = 10f;
                float btnStartX = editRect.x;
                
                // 鍚敤/绂佺敤鎸夐挳
                Rect enableBtnRect = new Rect(btnStartX, contentRect.yMax - buttonHeight, btnWidth, buttonHeight - 4f);
                if (Widgets.ButtonText(enableBtnRect, action.IsEnabled ? "RimChat_Disable".Translate() : "RimChat_Enable".Translate()))
                    action.IsEnabled = !action.IsEnabled;
                
                // 鍒犻櫎鎸夐挳
                Rect deleteBtnRect = new Rect(btnStartX + btnWidth + btnGap, contentRect.yMax - buttonHeight, btnWidth, buttonHeight - 4f);
                if (Widgets.ButtonText(deleteBtnRect, "RimChat_DeleteSelected".Translate()))
                {
                    ShowDeleteApiActionConfirmation(action);
                }
            }
            else
            {
                // 鏈€変腑浠讳綍椤规椂鏄剧ず鎻愮ず
                GUI.color = Color.gray;
                Text.Font = GameFont.Medium;
                Widgets.Label(editRect.ContractedBy(20f), "RimChat_SelectApiAction".Translate());
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }
        }

        private void ShowApiPromptEditingWarningDialog()
        {
            Find.WindowStack.Add(new Dialog_MessageBox(
                $"[{"RimChat_WarningTitle".Translate()}]\n\n{"RimChat_ApiPromptEditingWarning".Translate()}",
                "OK".Translate(),
                null,
                null,
                null,
                null,
                false,
                null,
                null,
                WindowLayer.Dialog));
        }

        private void AddNewApiAction()
        {
            SystemPromptConfigData.ApiActions ??= new List<ApiActionConfig>();
            var newAction = new ApiActionConfig
            {
                ActionName = "NewAction",
                Description = "",
                Parameters = "",
                Requirement = "",
                IsEnabled = true
            };
            int insertIndex = SystemPromptConfigData.ApiActions.FindIndex(item =>
                string.Equals(item?.ActionName, "publish_public_post", StringComparison.OrdinalIgnoreCase));
            if (insertIndex < 0)
            {
                insertIndex = SystemPromptConfigData.ApiActions.Count;
            }

            SystemPromptConfigData.ApiActions.Insert(insertIndex, newAction);
            _selectedApiActionIndex = GetEditableApiActions().Count - 1;
            _editingApiActionName = "NewAction";
            _editingApiActionDesc = "";
            _editingApiActionParams = "";
            _editingApiActionReq = "";
            Messages.Message("RimChat_ItemAdded".Translate("RimChat_ApiActionsSection".Translate()), MessageTypeDefOf.NeutralEvent, false);
        }

        private void ShowDeleteApiActionConfirmation(ApiActionConfig action)
        {
            Dialog_MessageBox dialog = Dialog_MessageBox.CreateConfirmation(
                "RimChat_DeleteConfirm".Translate("RimChat_ApiActionsSection".Translate()),
                () =>
                {
                    int oldIndex = _selectedApiActionIndex;
                    SystemPromptConfigData.ApiActions.Remove(action);
                    List<ApiActionConfig> editableActions = GetEditableApiActions();
                    if (editableActions.Count == 0)
                    {
                        _selectedApiActionIndex = -1;
                        _editingApiActionName = "";
                        _editingApiActionDesc = "";
                        _editingApiActionParams = "";
                        _editingApiActionReq = "";
                    }
                    else
                    {
                        _selectedApiActionIndex = Mathf.Min(oldIndex, editableActions.Count - 1);
                        if (_selectedApiActionIndex >= 0 && _selectedApiActionIndex < editableActions.Count)
                        {
                            var newAction = editableActions[_selectedApiActionIndex];
                            _editingApiActionName = newAction.ActionName ?? "";
                            _editingApiActionDesc = newAction.Description ?? "";
                            _editingApiActionParams = newAction.Parameters ?? "";
                            _editingApiActionReq = newAction.Requirement ?? "";
                        }
                    }
                    Messages.Message("RimChat_ItemDeleted".Translate(action.ActionName), MessageTypeDefOf.NeutralEvent, false);
                },
                true,
                "RimChat_DeleteConfirmTitle".Translate()
            );
            Find.WindowStack.Add(dialog);
        }

        private List<ApiActionConfig> GetEditableApiActions()
        {
            SystemPromptConfigData.ApiActions ??= new List<ApiActionConfig>();
            return SystemPromptConfigData.ApiActions
                .Where(action => !string.Equals(action?.ActionName, "publish_public_post", StringComparison.OrdinalIgnoreCase))
                .ToList();
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

            // JSON 妯℃澘 - 甯︽粴鍔ㄦ潯锛堝～婊″墿浣欑┖闂达級
            Widgets.Label(new Rect(rect.x, y, rect.width, 20f), "RimChat_JsonTemplateLabel".Translate());
            y += 22f;

            float textHeight = rect.yMax - y - 29f; // 棰勭暀澶嶉€夋绌洪棿
            Rect textRect = new Rect(rect.x, y, rect.width - 16f, textHeight);

            // 璁＄畻瀹為檯鍐呭楂樺害锛岀‘淇濆畬鏁存樉绀?
            float contentHeight = Mathf.Max(textRect.height, Text.CalcHeight(_jsonTemplateBuffer, textRect.width - 16f) + 10f);
            Rect viewRect = new Rect(0f, 0f, textRect.width - 16f, contentHeight);
            _jsonTemplateScroll = GUI.BeginScrollView(textRect, _jsonTemplateScroll, viewRect);

            _jsonTemplateBuffer = GUI.TextArea(viewRect, _jsonTemplateBuffer);

            GUI.EndScrollView();
            format.JsonTemplate = _jsonTemplateBuffer;
        }

        private void DrawJsonTemplateEditorScrollable(Rect rect)
        {
            var format = SystemPromptConfigData.ResponseFormat;
            if (format == null)
            {
                format = new ResponseFormatConfig();
                SystemPromptConfigData.ResponseFormat = format;
            }

            // 鏍囬
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 20f), "RimChat_JsonTemplateLabel".Translate());

            // 甯︽粴鍔ㄦ潯鐨勬枃鏈锛堝～婊″墿浣欑┖闂达級
            float textY = rect.y + 22f;
            float textHeight = rect.yMax - textY;
            Rect textRect = new Rect(rect.x, textY, rect.width, textHeight);

            float contentHeight = Text.CalcHeight(_jsonTemplateBuffer, textRect.width - 20f);
            contentHeight = Mathf.Max(contentHeight, textRect.height);

            Rect viewRect = new Rect(0f, 0f, textRect.width - 20f, contentHeight);
            _jsonTemplateScroll = GUI.BeginScrollView(textRect, _jsonTemplateScroll, viewRect);

            _jsonTemplateBuffer = GUI.TextArea(viewRect, _jsonTemplateBuffer);

            GUI.EndScrollView();
            format.JsonTemplate = _jsonTemplateBuffer;
        }

        private void DrawImportantRulesEditorScrollable(Rect rect)
        {
            var format = SystemPromptConfigData.ResponseFormat;
            if (format == null)
            {
                format = new ResponseFormatConfig();
                SystemPromptConfigData.ResponseFormat = format;
            }

            // 鏍囬
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 20f), "RimChat_ImportantRulesLabel".Translate());

            // 甯︽粴鍔ㄦ潯鐨勬枃鏈锛堝～婊″墿浣欑┖闂达級
            float textY = rect.y + 22f;
            float textHeight = rect.yMax - textY;
            Rect textRect = new Rect(rect.x, textY, rect.width, textHeight);

            float contentHeight = Text.CalcHeight(_importantRulesBuffer, textRect.width - 20f);
            contentHeight = Mathf.Max(contentHeight, textRect.height);

            Rect viewRect = new Rect(0f, 0f, textRect.width - 20f, contentHeight);
            _importantRulesScroll = GUI.BeginScrollView(textRect, _importantRulesScroll, viewRect);

            _importantRulesBuffer = GUI.TextArea(viewRect, _importantRulesBuffer);

            GUI.EndScrollView();
            format.ImportantRules = _importantRulesBuffer;
        }

        private void DrawDecisionRulesEditorScrollable(Rect rect)
        {
            var rules = SystemPromptConfigData.DecisionRules;
            if (rules == null || rules.Count == 0)
            {
                Widgets.Label(rect, "RimChat_NoDecisionRules".Translate());
                return;
            }

            // 宸︿晶鍒楄〃鍖哄煙锛堝浐瀹氬搴︼級
            float listWidth = 220f;
            float buttonHeight = 32f;
            Rect listRect = new Rect(rect.x, rect.y, listWidth, rect.height - buttonHeight);
            
            // 鍙充晶缂栬緫鍖哄煙
            Rect editRect = new Rect(rect.x + listWidth + 10f, rect.y, rect.width - listWidth - 10f, rect.height - buttonHeight);

            // 缁樺埗瑙勫垯鍒楄〃锛堝甫婊氬姩锛?
            float itemHeight = 30f;
            float listContentHeight = rules.Count * itemHeight;
            Rect listContentRect = new Rect(0f, 0f, listWidth - 16f, Mathf.Max(listContentHeight, listRect.height));

            _ruleContentScroll = GUI.BeginScrollView(listRect, _ruleContentScroll, listContentRect);
            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                Rect rowRect = new Rect(0f, i * itemHeight, listContentRect.width, itemHeight - 1f);
                bool isSelected = i == _selectedDecisionRuleIndex;

                if (isSelected)
                    Widgets.DrawBoxSolid(rowRect, new Color(0.25f, 0.35f, 0.55f, 0.8f));
                else if (Mouse.IsOver(rowRect))
                    Widgets.DrawBoxSolid(rowRect, new Color(0.2f, 0.22f, 0.28f, 0.6f));

                string label = $"{(rule.IsEnabled ? "[ON]" : "[OFF]")} {rule.RuleName}";
                GUI.color = rule.IsEnabled ? Color.white : Color.gray;
                TextAnchor oldAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleLeft;
                Rect labelRect = new Rect(rowRect.x + 8f, rowRect.y + 2f, rowRect.width - 16f, rowRect.height - 4f);
                Widgets.Label(labelRect, label.Truncate(labelRect.width));
                Text.Anchor = oldAnchor;
                GUI.color = Color.white;

                if (Widgets.ButtonInvisible(rowRect))
                {
                    _selectedDecisionRuleIndex = i;
                    _editingRuleName = rule.RuleName ?? "";
                    _editingRuleContent = rule.RuleContent ?? "";
                }
            }
            GUI.EndScrollView();

            // 鏂板鎸夐挳锛堝垪琛ㄥ簳閮級
            Rect addBtnRect = new Rect(rect.x, rect.yMax - buttonHeight, listWidth, buttonHeight - 4f);
            if (Widgets.ButtonText(addBtnRect, "RimChat_AddNew".Translate()))
            {
                AddNewDecisionRule();
            }

            // 缁樺埗鍙充晶缂栬緫鍖哄煙
            if (_selectedDecisionRuleIndex >= 0 && _selectedDecisionRuleIndex < rules.Count)
            {
                var rule = rules[_selectedDecisionRuleIndex];
                float y = editRect.y;

                // 鏍囬
                GUI.color = SectionHeaderColor;
                Widgets.Label(new Rect(editRect.x, y, editRect.width, 24f), "RimChat_EditDecisionRule".Translate());
                GUI.color = Color.white;
                y += 28f;

                // 鍚嶇О
                Widgets.Label(new Rect(editRect.x, y, editRect.width, 20f), "RimChat_RuleNameLabel".Translate());
                y += 22f;
                _editingRuleName = Widgets.TextField(new Rect(editRect.x, y, editRect.width, 24f), _editingRuleName);
                y += 28f;

                // 瑙勫垯鍐呭锛堝ぇ鏂囨湰妗嗭紝甯︽粴鍔紝濉弧鍓╀綑绌洪棿锛?
                Widgets.Label(new Rect(editRect.x, y, editRect.width, 20f), "RimChat_RuleContentLabel".Translate());
                y += 22f;
                float contentHeight = editRect.yMax - y;
                Rect contentRect = new Rect(editRect.x, y, editRect.width - 16f, contentHeight);
                
                // 璁＄畻瀹為檯鍐呭楂樺害锛岀‘淇濆畬鏁存樉绀?
                float ruleContentHeight = Mathf.Max(contentRect.height, Text.CalcHeight(_editingRuleContent, contentRect.width - 16f) + 10f);
                Rect contentViewRect = new Rect(0f, 0f, contentRect.width - 16f, ruleContentHeight);
                _jsonTemplateScroll = GUI.BeginScrollView(contentRect, _jsonTemplateScroll, contentViewRect);
                _editingRuleContent = GUI.TextArea(contentViewRect, _editingRuleContent);
                GUI.EndScrollView();
                
                rule.RuleContent = _editingRuleContent;
                rule.RuleName = _editingRuleName;

                // 鎸夐挳鍖哄煙锛堝浐瀹氬湪搴曢儴锛?
                float btnWidth = 100f;
                float btnGap = 10f;
                float btnStartX = editRect.x;
                
                // 鍚敤/绂佺敤鎸夐挳
                Rect enableBtnRect = new Rect(btnStartX, rect.yMax - buttonHeight, btnWidth, buttonHeight - 4f);
                if (Widgets.ButtonText(enableBtnRect, rule.IsEnabled ? "RimChat_Disable".Translate() : "RimChat_Enable".Translate()))
                    rule.IsEnabled = !rule.IsEnabled;
                
                // 鍒犻櫎鎸夐挳
                Rect deleteBtnRect = new Rect(btnStartX + btnWidth + btnGap, rect.yMax - buttonHeight, btnWidth, buttonHeight - 4f);
                if (Widgets.ButtonText(deleteBtnRect, "RimChat_DeleteSelected".Translate()))
                {
                    ShowDeleteDecisionRuleConfirmation(rule);
                }
            }
            else
            {
                // 鏈€変腑浠讳綍椤规椂鏄剧ず鎻愮ず
                GUI.color = Color.gray;
                Text.Font = GameFont.Medium;
                Widgets.Label(editRect.ContractedBy(20f), "RimChat_SelectDecisionRule".Translate());
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }
        }

        private void AddNewDecisionRule()
        {
            var newRule = new DecisionRuleConfig
            {
                RuleName = "NewRule",
                RuleContent = "",
                IsEnabled = true
            };
            SystemPromptConfigData.DecisionRules.Add(newRule);
            _selectedDecisionRuleIndex = SystemPromptConfigData.DecisionRules.Count - 1;
            _editingRuleName = "NewRule";
            _editingRuleContent = "";
            Messages.Message("RimChat_ItemAdded".Translate("RimChat_DecisionRulesSection".Translate()), MessageTypeDefOf.NeutralEvent, false);
        }

        private void ShowDeleteDecisionRuleConfirmation(DecisionRuleConfig rule)
        {
            Dialog_MessageBox dialog = Dialog_MessageBox.CreateConfirmation(
                "RimChat_DeleteConfirm".Translate("RimChat_DecisionRulesSection".Translate()),
                () =>
                {
                    int oldIndex = _selectedDecisionRuleIndex;
                    SystemPromptConfigData.DecisionRules.Remove(rule);
                    if (SystemPromptConfigData.DecisionRules.Count == 0)
                    {
                        _selectedDecisionRuleIndex = -1;
                        _editingRuleName = "";
                        _editingRuleContent = "";
                    }
                    else
                    {
                        _selectedDecisionRuleIndex = Mathf.Min(oldIndex, SystemPromptConfigData.DecisionRules.Count - 1);
                        if (_selectedDecisionRuleIndex >= 0 && _selectedDecisionRuleIndex < SystemPromptConfigData.DecisionRules.Count)
                        {
                            var newRule = SystemPromptConfigData.DecisionRules[_selectedDecisionRuleIndex];
                            _editingRuleName = newRule.RuleName ?? "";
                            _editingRuleContent = newRule.RuleContent ?? "";
                        }
                    }
                    Messages.Message("RimChat_ItemDeleted".Translate(rule.RuleName), MessageTypeDefOf.NeutralEvent, false);
                },
                true,
                "RimChat_DeleteConfirmTitle".Translate()
            );
            Find.WindowStack.Add(dialog);
        }

        private void DrawFactionPromptsEditorScrollable(Rect rect)
        {
            FactionPromptManager manager = FactionPromptManager.Instance;
            var configs = manager.AllConfigs;
            if (configs == null || configs.Count == 0)
            {
                Widgets.Label(rect, "RimChat_NoFactionPrompts".Translate());
                return;
            }

            // 宸︿晶鍒楄〃鍖哄煙锛堝浐瀹氬搴︼級
            float listWidth = 200f;
            Rect listRect = new Rect(rect.x, rect.y, listWidth, rect.height);
            float listHeaderHeight = 30f;
            Rect listHeaderRect = new Rect(listRect.x, listRect.y, listRect.width, listHeaderHeight);
            Rect listScrollRect = new Rect(listRect.x, listRect.y + listHeaderHeight + 4f, listRect.width, listRect.height - listHeaderHeight - 4f);

            float addBtnWidth = 92f;
            Rect listTitleRect = new Rect(listHeaderRect.x, listHeaderRect.y, listHeaderRect.width - addBtnWidth - 6f, listHeaderRect.height);
            Rect addTemplateRect = new Rect(listHeaderRect.xMax - addBtnWidth, listHeaderRect.y, addBtnWidth, listHeaderRect.height - 2f);
            Text.Font = GameFont.Small;
            Widgets.Label(listTitleRect, "RimChat_FactionPromptsSection".Translate());
            if (Widgets.ButtonText(addTemplateRect, "RimChat_AddFactionTemplate".Translate()))
            {
                OpenFactionTemplateAddMenu();
            }

            // 鍙充晶缂栬緫鍖哄煙
            Rect editRect = new Rect(rect.x + listWidth + 10f, rect.y, rect.width - listWidth - 10f, rect.height);

            // 缁樺埗娲剧郴鍒楄〃锛堝甫婊氬姩锛?
            float itemHeight = 32f;
            float listContentHeight = configs.Count * itemHeight;
            Rect listContentRect = new Rect(0f, 0f, listWidth - 16f, Mathf.Max(listContentHeight, listScrollRect.height));

            _factionPromptScroll = GUI.BeginScrollView(listScrollRect, _factionPromptScroll, listContentRect);
            for (int i = 0; i < configs.Count; i++)
            {
                var config = configs[i];
                Rect rowRect = new Rect(0f, i * itemHeight, listContentRect.width, itemHeight - 2f);
                bool isSelected = i == _selectedFactionPromptIndex;

                if (isSelected)
                    Widgets.DrawBoxSolid(rowRect, new Color(0.25f, 0.35f, 0.55f, 0.8f));
                else if (Mouse.IsOver(rowRect))
                    Widgets.DrawBoxSolid(rowRect, new Color(0.2f, 0.22f, 0.28f, 0.6f));

                string stateTag = manager.IsDefaultTemplate(config.FactionDefName)
                    ? "RimChat_FactionTemplateTagDefault".Translate().ToString()
                    : "RimChat_FactionTemplateTagCustom".Translate().ToString();
                string missingTag = manager.IsFactionMissing(config.FactionDefName)
                    ? $" {"RimChat_FactionTemplateTagMissing".Translate()}"
                    : string.Empty;
                string label = $"{stateTag}{missingTag} {GetFactionTemplateDisplayName(config)}";
                GUI.color = manager.IsFactionMissing(config.FactionDefName)
                    ? new Color(1f, 0.7f, 0.7f)
                    : Color.white;
                Widgets.Label(rowRect.ContractedBy(4f), label.Truncate(rowRect.width - 8f));
                GUI.color = Color.white;

                if (Widgets.ButtonInvisible(rowRect))
                {
                    _selectedFactionPromptIndex = i;
                }
            }
            GUI.EndScrollView();

            // 缁樺埗鍙充晶缂栬緫鍖哄煙
            if (_selectedFactionPromptIndex >= 0 && _selectedFactionPromptIndex < configs.Count)
            {
                var selectedConfig = configs[_selectedFactionPromptIndex];
                float y = editRect.y;

                // 鏍囬
                GUI.color = SectionHeaderColor;
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(editRect.x, y, editRect.width, 28f), GetFactionTemplateDisplayName(selectedConfig));
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                y += 32f;

                // 鎻忚堪
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                string editorDesc = "RimChat_FactionPromptEditorDesc".Translate().ToString();
                if (manager.IsFactionMissing(selectedConfig.FactionDefName))
                {
                    editorDesc = $"{editorDesc} {"RimChat_FactionTemplateMissingDesc".Translate()}";
                }

                Widgets.Label(new Rect(editRect.x, y, editRect.width, 40f), editorDesc);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                y += 42f;

                // 浣跨敤鑷畾涔塒rompt澶嶉€夋
                Rect customCheckRect = new Rect(editRect.x, y, editRect.width, 24f);
                bool useCustom = selectedConfig.UseCustomPrompt;
                Widgets.CheckboxLabeled(customCheckRect, "RimChat_UseCustomPrompt".Translate(), ref useCustom);
                if (useCustom != selectedConfig.UseCustomPrompt)
                {
                    selectedConfig.UseCustomPrompt = useCustom;
                    manager.UpdateConfig(selectedConfig);
                }
                y += 28f;

                // 鎸夐挳鍖哄煙
                float btnWidth = (editRect.width - 16f) / 3f;
                float btnHeight = 28f;
                float btnGap = 8f;
                float buttonX = editRect.x;

                Rect editTemplateRect = new Rect(buttonX, y, btnWidth, btnHeight);
                if (Widgets.ButtonText(editTemplateRect, "RimChat_EditTemplate".Translate()))
                {
                    Find.WindowStack.Add(new Dialog_FactionPromptEditor(selectedConfig.Clone()));
                }
                buttonX += btnWidth + btnGap;

                Rect resetRect = new Rect(buttonX, y, btnWidth, btnHeight);
                if (Widgets.ButtonText(resetRect, "RimChat_Reset".Translate()))
                {
                    ShowResetFactionPromptConfirmation(selectedConfig);
                }
                buttonX += btnWidth + btnGap;

                Rect removeRect = new Rect(buttonX, y, btnWidth, btnHeight);
                bool canRemove = !manager.IsDefaultTemplate(selectedConfig.FactionDefName);
                if (!canRemove)
                {
                    GUI.color = Color.gray;
                    TooltipHandler.TipRegion(removeRect, "RimChat_FactionTemplateRemoveDefaultBlocked".Translate());
                }

                if (Widgets.ButtonText(removeRect, "RimChat_RemoveFactionTemplate".Translate()) && canRemove)
                {
                    ShowRemoveFactionPromptConfirmation(selectedConfig);
                }
                GUI.color = Color.white;

                // 棰勮鍖哄煙
                y += btnHeight + 16f;
                Rect previewLabelRect = new Rect(editRect.x, y, editRect.width, 20f);
                GUI.color = new Color(0.5f, 0.8f, 0.5f);
                Widgets.Label(previewLabelRect, "RimChat_PreviewTitleShort".Translate());
                GUI.color = Color.white;
                y += 22f;

                Rect previewRect = new Rect(editRect.x, y, editRect.width, editRect.yMax - y);
                Widgets.DrawBoxSolid(previewRect, new Color(0.08f, 0.1f, 0.08f));
                Widgets.DrawBox(previewRect);

                string previewText = selectedConfig.GetEffectivePrompt();
                Rect innerPreviewRect = previewRect.ContractedBy(8f);

                float previewContentHeight = Text.CalcHeight(previewText, innerPreviewRect.width - 16f);
                Rect previewViewRect = new Rect(0f, 0f, innerPreviewRect.width - 16f, Mathf.Max(previewContentHeight, innerPreviewRect.height));

                _previewScroll = GUI.BeginScrollView(innerPreviewRect, _previewScroll, previewViewRect);
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.6f, 0.7f, 0.6f);
                Widgets.Label(previewViewRect, previewText);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                GUI.EndScrollView();
            }
            else
            {
                // 鏈€変腑浠讳綍椤规椂鏄剧ず鎻愮ず
                GUI.color = Color.gray;
                Text.Font = GameFont.Medium;
                Widgets.Label(editRect.ContractedBy(20f), "RimChat_SelectFactionPrompt".Translate());
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }
        }

        private void ShowResetFactionPromptConfirmation(FactionPromptConfig config)
        {
            Dialog_MessageBox dialog = Dialog_MessageBox.CreateConfirmation(
                "RimChat_ResetFactionPromptConfirm".Translate(config.DisplayName),
                () =>
                {
                    FactionPromptManager.Instance.ResetConfig(config.FactionDefName);
                    Messages.Message("RimChat_FactionPromptReset".Translate(config.DisplayName), MessageTypeDefOf.NeutralEvent, false);
                },
                true,
                "RimChat_ResetConfirmTitle".Translate()
            );
            Find.WindowStack.Add(dialog);
        }

        private void OpenFactionTemplateAddMenu()
        {
            List<FactionDef> defs = DefDatabase<FactionDef>.AllDefsListForReading
                .Where(def => def != null && !string.IsNullOrWhiteSpace(def.defName))
                .OrderBy(def => def.label ?? def.defName)
                .ToList();
            if (defs.Count == 0)
            {
                Messages.Message("RimChat_FactionTemplateNoFactionDefs".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            FactionPromptManager manager = FactionPromptManager.Instance;
            List<FloatMenuOption> options = defs.Select(def =>
            {
                string label = $"{(def.label ?? def.defName)} ({def.defName})";
                return new FloatMenuOption(label, () =>
                {
                    bool added = manager.TryAddTemplateForFaction(def.defName, def.label, out string status);
                    if (added)
                    {
                        List<FactionPromptConfig> refreshed = manager.AllConfigs;
                        _selectedFactionPromptIndex = FindFactionPromptConfigIndex(refreshed, def.defName);
                        Messages.Message("RimChat_FactionTemplateAdded".Translate(label), MessageTypeDefOf.NeutralEvent, false);
                        return;
                    }

                    if (string.Equals(status, "existing", StringComparison.OrdinalIgnoreCase))
                    {
                        List<FactionPromptConfig> refreshed = manager.AllConfigs;
                        _selectedFactionPromptIndex = FindFactionPromptConfigIndex(refreshed, def.defName);
                        Messages.Message("RimChat_FactionTemplateExistingSelected".Translate(label), MessageTypeDefOf.NeutralEvent, false);
                        return;
                    }

                    Messages.Message("RimChat_FactionTemplateAddFailed".Translate(label), MessageTypeDefOf.RejectInput, false);
                });
            }).ToList();

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static int FindFactionPromptConfigIndex(List<FactionPromptConfig> configs, string factionDefName)
        {
            if (configs == null || string.IsNullOrWhiteSpace(factionDefName))
            {
                return -1;
            }

            for (int i = 0; i < configs.Count; i++)
            {
                if (string.Equals(configs[i]?.FactionDefName, factionDefName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string GetFactionTemplateDisplayName(FactionPromptConfig config)
        {
            if (config == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(config.DisplayName)
                ? config.FactionDefName ?? string.Empty
                : config.DisplayName;
        }

        private void ShowRemoveFactionPromptConfirmation(FactionPromptConfig config)
        {
            if (config == null)
            {
                return;
            }

            Dialog_MessageBox dialog = Dialog_MessageBox.CreateConfirmation(
                "RimChat_RemoveFactionTemplateConfirm".Translate(GetFactionTemplateDisplayName(config)),
                () =>
                {
                    bool removed = FactionPromptManager.Instance.TryRemoveTemplate(config.FactionDefName, out string reason);
                    if (removed)
                    {
                        _selectedFactionPromptIndex = -1;
                        _previewScroll = Vector2.zero;
                        Messages.Message("RimChat_FactionTemplateRemoved".Translate(GetFactionTemplateDisplayName(config)), MessageTypeDefOf.NeutralEvent, false);
                        return;
                    }

                    string key = string.Equals(reason, "default_protected", StringComparison.OrdinalIgnoreCase)
                        ? "RimChat_FactionTemplateRemoveDefaultBlocked"
                        : "RimChat_FactionTemplateRemoveFailed";
                    Messages.Message(key.Translate(GetFactionTemplateDisplayName(config)), MessageTypeDefOf.RejectInput, false);
                },
                true,
                "RimChat_DeleteConfirmTitle".Translate());
            Find.WindowStack.Add(dialog);
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
            Widgets.CheckboxLabeled(check1, "RimChat_InjectMemoryData".Translate(), ref dynConfig.InjectMemoryData);
            RegisterTooltip(check1, "RimChat_InjectMemoryDataTooltip");
            y += 28f;

            Rect check2 = new Rect(rect.x, y, rect.width, 24f);
            Widgets.CheckboxLabeled(check2, "RimChat_InjectFactionInfo".Translate(), ref dynConfig.InjectFactionInfo);
            RegisterTooltip(check2, "RimChat_InjectFactionInfoTooltip");
            y += 28f;

            Rect check3 = new Rect(rect.x, y, rect.width, 24f);
            Widgets.CheckboxLabeled(check3, "RimChat_UseHierarchicalPromptFormat".Translate(), ref SystemPromptConfigData.UseHierarchicalPromptFormat);
            RegisterTooltip(check3, "RimChat_UseHierarchicalPromptFormatTooltip");
            y += 30f;

            RimChatSettings settings = RimChatMod.Settings;
            if (settings != null)
            {
                Rect compressionEnabledRect = new Rect(rect.x, y, rect.width, 24f);
                Widgets.CheckboxLabeled(
                    compressionEnabledRect,
                    "RimChat_DialogueCompressionEnabled".Translate(),
                    ref settings.EnableDialogueContextCompression);
                RegisterTooltip(compressionEnabledRect, "RimChat_DialogueCompressionEnabledTooltip");
                y += 28f;

                if (settings.EnableDialogueContextCompression)
                {
                    Widgets.Label(new Rect(rect.x, y, rect.width, 22f), "RimChat_DialogueCompressionProfile102025".Translate());
                    y += 24f;

                    y = DrawCompressionSlider(
                        rect,
                        y,
                        "RimChat_DialogueCompressionKeepRecent".Translate(settings.DialogueCompressionKeepRecentTurns),
                        ref settings.DialogueCompressionKeepRecentTurns,
                        6,
                        30);

                    int tier2Min = settings.DialogueCompressionKeepRecentTurns + 1;
                    y = DrawCompressionSlider(
                        rect,
                        y,
                        "RimChat_DialogueCompressionTier2Start".Translate(settings.DialogueCompressionSecondaryTierStart),
                        ref settings.DialogueCompressionSecondaryTierStart,
                        tier2Min,
                        120);

                    int tier3Min = settings.DialogueCompressionSecondaryTierStart + 1;
                    y = DrawCompressionSlider(
                        rect,
                        y,
                        "RimChat_DialogueCompressionTier3Start".Translate(settings.DialogueCompressionTertiaryTierStart),
                        ref settings.DialogueCompressionTertiaryTierStart,
                        tier3Min,
                        180);

                    y = DrawCompressionSlider(
                        rect,
                        y,
                        "RimChat_DialogueCompressionMaxEvents".Translate(settings.DialogueCompressionMaxEventsPerSegment),
                        ref settings.DialogueCompressionMaxEventsPerSegment,
                        1,
                        3);

                    settings.DialogueCompressionMaxMark = 3;
                    Widgets.Label(
                        new Rect(rect.x, y, rect.width, 22f),
                        "RimChat_DialogueCompressionMaxMark".Translate(settings.DialogueCompressionMaxMark));
                    y += 24f;

                    settings.DialogueCompressionSecondaryTriggerTurns = settings.DialogueCompressionKeepRecentTurns + 10;
                    settings.DialogueCompressionSecondaryWindowMinRecency = settings.DialogueCompressionSecondaryTierStart;
                    settings.DialogueCompressionSecondaryWindowMaxRecency = settings.DialogueCompressionTertiaryTierStart - 1;
                }
            }

            Rect tagsLabelRect = new Rect(rect.x, y, 180f, 24f);
            Widgets.Label(tagsLabelRect, "RimChat_DiplomacySceneTags".Translate());
            RegisterTooltip(tagsLabelRect, "RimChat_DiplomacySceneTagsTooltip");
            string currentTags = RimChatMod.Settings?.DiplomacyManualSceneTagsCsv ?? string.Empty;
            string editedTags = Widgets.TextField(new Rect(rect.x + 184f, y, rect.width - 184f, 24f), currentTags);
            if (RimChatMod.Settings != null && !string.Equals(editedTags, currentTags, StringComparison.Ordinal))
            {
                RimChatMod.Settings.DiplomacyManualSceneTagsCsv = editedTags;
                _previewUpdateCooldown = 0;
            }
        }

        private static float DrawCompressionSlider(
            Rect rootRect,
            float y,
            string label,
            ref int value,
            int min,
            int max)
        {
            Rect labelRect = new Rect(rootRect.x, y, rootRect.width, 22f);
            Widgets.Label(labelRect, label);
            y += 20f;

            Rect sliderRect = new Rect(rootRect.x, y, rootRect.width, 20f);
            value = (int)Widgets.HorizontalSlider(sliderRect, value, min, max);
            return y + 24f;
        }

        private void DrawPreviewRight(Rect rect)
        {
            // Update animation time
            if (_previewFoldAnimTime > 0f)
            {
                _previewFoldAnimTime -= Time.deltaTime;
            }

            // 棰勮鏍囬鏍忚儗鏅?
            Rect titleBarRect = new Rect(rect.x, rect.y, rect.width, 22f);
            Widgets.DrawBoxSolid(titleBarRect, new Color(0.15f, 0.15f, 0.15f));
            
            // 棰勮鏍囬
            Rect titleRect = new Rect(rect.x + 5f, rect.y + 2f, rect.width - 30f, 20f);
            GUI.color = new Color(0.5f, 0.8f, 0.5f);
            Text.Font = GameFont.Small;
            Widgets.Label(titleRect, "RimChat_PreviewTitleShort".Translate());
            GUI.color = Color.white;

            // 鎶樺彔鎸夐挳锛堟爣棰樻爮鍙充晶锛?
            float foldBtnSize = 18f;
            Rect foldBtnRect = new Rect(rect.xMax - foldBtnSize - 5f, rect.y + 2f, foldBtnSize, foldBtnSize);
            
            // 缁樺埗鎸夐挳鑳屾櫙
            GUI.color = new Color(0.25f, 0.25f, 0.25f);
            if (Mouse.IsOver(foldBtnRect))
            {
                GUI.color = new Color(0.35f, 0.35f, 0.35f);
            }
            Widgets.DrawBoxSolid(foldBtnRect, GUI.color);
            Widgets.DrawBox(foldBtnRect);
            
            // 鐐瑰嚮澶勭悊
            if (Widgets.ButtonInvisible(foldBtnRect))
            {
                _previewCollapsed = !_previewCollapsed;
                _previewFoldAnimTime = 0.2f;
            }

            // Use ASCII arrow glyphs to avoid font/encoding issues.
            string arrow = _previewCollapsed ? ">" : "v";
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.white;
            Widgets.Label(foldBtnRect, arrow);
            Text.Anchor = oldAnchor;
            GUI.color = Color.white;

            // 棰勮鍐呭妗嗭紙甯︽姌鍙犲姩鐢伙級
            if (!_previewCollapsed || _previewFoldAnimTime > 0f)
            {
                float contentHeightFactor = 1f;
                if (_previewFoldAnimTime > 0f)
                {
                    float t = 1f - (_previewFoldAnimTime / 0.2f);
                    contentHeightFactor = _previewCollapsed ? 1f - t : t;
                }

                if (contentHeightFactor > 0.01f)
                {
                    float actualContentHeight = (rect.height - 24f) * contentHeightFactor;
                    Rect contentRect = new Rect(rect.x, rect.y + 24f, rect.width, actualContentHeight);

                    if (contentHeightFactor >= 0.95f)
                    {
                        Widgets.DrawBoxSolid(contentRect, new Color(0.08f, 0.1f, 0.08f));
                        Widgets.DrawBox(contentRect);

                        Rect innerRect = contentRect.ContractedBy(4f);
                        DrawPreviewContextControls(innerRect);

                        const float controlsHeight = 112f;
                        float textStartY = innerRect.y + controlsHeight;
                        float textHeight = Mathf.Max(20f, innerRect.height - controlsHeight);
                        Rect textRect = new Rect(innerRect.x, textStartY, innerRect.width, textHeight);

                        UpdatePreviewText();

                        float contentHeight = Text.CalcHeight(_cachedPreviewText, textRect.width - 20f);
                        contentHeight = Mathf.Max(contentHeight, textRect.height);

                        Rect viewRect = new Rect(0f, 0f, textRect.width - 20f, contentHeight);
                        _previewScroll = GUI.BeginScrollView(textRect, _previewScroll, viewRect);

                        Text.Font = GameFont.Tiny;
                        GUI.color = new Color(0.6f, 0.7f, 0.6f);
                        Widgets.Label(viewRect, _cachedPreviewText);
                        GUI.color = Color.white;
                        Text.Font = GameFont.Small;

                        GUI.EndScrollView();
                    }
                }
            }
            else if (_previewCollapsed)
            {
                // 鎶樺彔鏃舵樉绀烘渶灏忓寲鎸囩ず鏉?
                Rect collapsedRect = new Rect(rect.x, rect.y + 24f, rect.width, 16f);
                Widgets.DrawBoxSolid(collapsedRect, new Color(0.15f, 0.15f, 0.15f, 0.5f));
                GUI.color = Color.gray;
                Text.Font = GameFont.Tiny;
                Widgets.Label(collapsedRect, "RimChat_PreviewCollapsedHint".Translate());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }
        }

        private string GetSectionLabel(string sectionName)
        {
            return sectionName switch
            {
                "GlobalPrompt" => "RimChat_GlobalSystemPromptSection".Translate(),
                "EnvironmentPrompts" => "RimChat_EnvironmentPromptsSection".Translate(),
                "FactionPrompts" => "RimChat_FactionPromptsSection".Translate(),
                "ApiActions" => "RimChat_ApiActionsSection".Translate(),
                "JsonTemplate" => "RimChat_JsonTemplateLabel".Translate(),
                "ImportantRules" => "RimChat_ImportantRulesLabel".Translate(),
                "PromptTemplates" => "RimChat_PromptTemplatesSection".Translate(),
                "SocialCirclePrompts" => "RimChat_SocialCirclePromptSection".Translate(),
                "DecisionRules" => "RimChat_DecisionRulesSection".Translate(),
                "DynamicData" => "RimChat_DynamicDataInjectionSection".Translate(),
                _ => sectionName
            };
        }

        private void SyncBuffersToData()
        {
            _globalPromptBuffer = SystemPromptConfigData.GlobalSystemPrompt ?? "";
            _globalDialoguePromptBuffer = SystemPromptConfigData.GlobalDialoguePrompt ?? "";
            _jsonTemplateBuffer = SystemPromptConfigData.ResponseFormat?.JsonTemplate ?? "";
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
                var config = SystemPromptConfigData;
                Faction sampleFaction = Find.FactionManager?.AllFactionsVisible?.FirstOrDefault(f => f != null && !f.IsPlayer);
                if (sampleFaction == null)
                {
                    return "RimChat_EnvironmentPreviewNoContext".Translate();
                }

                var settings = RimChatMod.Settings;
                List<string> tags = ParseSceneTagsCsv(settings?.PromptPreviewSceneTagsCsv);
                string fullPrompt = PromptPersistenceService.Instance.BuildFullSystemPrompt(
                    sampleFaction,
                    config,
                    settings?.PromptPreviewUseProactiveContext == true,
                    tags);

                string diagnostics = BuildEnvironmentPreviewDiagnostics(
                    config,
                    sampleFaction,
                    settings?.PromptPreviewUseProactiveContext == true,
                    tags);

                if (!string.IsNullOrWhiteSpace(diagnostics))
                {
                    fullPrompt += "\n\n=== PREVIEW DIAGNOSTICS ===\n" + diagnostics;
                }

                return fullPrompt;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        private void DrawPreviewContextControls(Rect rect)
        {
            var settings = RimChatMod.Settings;
            if (settings == null)
            {
                return;
            }

            Rect proactiveRect = new Rect(rect.x, rect.y, rect.width, 24f);
            bool proactive = settings.PromptPreviewUseProactiveContext;
            Widgets.CheckboxLabeled(proactiveRect, "RimChat_PreviewUseProactiveContext".Translate(), ref proactive);
            if (proactive != settings.PromptPreviewUseProactiveContext)
            {
                settings.PromptPreviewUseProactiveContext = proactive;
                _previewUpdateCooldown = 0;
            }

            Rect tagsRect = new Rect(rect.x, rect.y + 26f, rect.width, 24f);
            string tags = settings.PromptPreviewSceneTagsCsv ?? string.Empty;
            Widgets.Label(new Rect(tagsRect.x, tagsRect.y, 120f, tagsRect.height), "RimChat_PreviewSceneTags".Translate());
            string edited = Widgets.TextField(new Rect(tagsRect.x + 124f, tagsRect.y, tagsRect.width - 124f, tagsRect.height), tags);
            if (!string.Equals(edited, tags, StringComparison.Ordinal))
            {
                settings.PromptPreviewSceneTagsCsv = edited;
                _previewUpdateCooldown = 0;
            }

            Rect actionsRect = new Rect(rect.x, rect.y + 52f, rect.width, 24f);
            DrawPreviewActionButtons(actionsRect);

            Rect statusRect = new Rect(rect.x, rect.y + 80f, rect.width, 24f);
            DrawLiveValidationStatus(statusRect);
        }

        private void DrawPreviewActionButtons(Rect actionsRect)
        {
            float buttonWidth = (actionsRect.width - 16f) / 3f;
            Rect variableRect = new Rect(actionsRect.x, actionsRect.y, buttonWidth, actionsRect.height);
            Rect validateRect = new Rect(variableRect.xMax + 8f, actionsRect.y, buttonWidth, actionsRect.height);
            Rect migrationRect = new Rect(validateRect.xMax + 8f, actionsRect.y, buttonWidth, actionsRect.height);

            if (Widgets.ButtonText(variableRect, "RimChat_PromptVariables".Translate()))
            {
                OpenPromptVariablePicker();
            }

            if (Widgets.ButtonText(validateRect, "RimChat_ValidateVariables".Translate()))
            {
                ValidateCurrentSectionVariables();
            }

            if (Widgets.ButtonText(migrationRect, "RimChat_PromptMigrationResultButton".Translate()))
            {
                OpenPromptMigrationResultDialog();
            }
        }

        private void DrawLiveValidationStatus(Rect rect)
        {
            UpdateLiveValidationState();
            string currentText = GetCurrentSectionEditableText();
            string statusText = BuildLiveValidationStatusText(_liveValidationResult, currentText);
            Color oldColor = GUI.color;
            GUI.color = ResolveLiveValidationStatusColor(_liveValidationResult, currentText);
            Widgets.Label(rect, statusText);
            GUI.color = oldColor;
        }

        private void OpenPromptMigrationResultDialog()
        {
            PromptTemplateAutoRewriteResult result = PromptPersistenceService.Instance.GetLastSchemaRewriteResult();
            Find.WindowStack.Add(new Dialog_PromptMigrationResult(result));
        }

        private void UpdateLiveValidationState()
        {
            string section = GetCurrentValidationSectionName();
            string text = GetCurrentSectionEditableText();
            string signature = section + "\n" + (text ?? string.Empty);
            _liveValidationCooldown = Math.Max(0, _liveValidationCooldown - 1);
            if (_liveValidationCooldown > 0 &&
                string.Equals(signature, _liveValidationSignature, StringComparison.Ordinal))
            {
                return;
            }

            _liveValidationSignature = signature;
            _liveValidationCooldown = LiveValidationRefreshTicks;
            _liveValidationResult = string.IsNullOrWhiteSpace(text)
                ? new TemplateVariableValidationResult()
                : PromptPersistenceService.Instance.ValidateTemplateVariables(text);
        }

        private string GetCurrentValidationSectionName()
        {
            string[] sections = _advancedPromptMode ? AdvancedSectionNames : SimpleSectionNames;
            if (_selectedSectionIndex < 0 || _selectedSectionIndex >= sections.Length)
            {
                return string.Empty;
            }

            return sections[_selectedSectionIndex];
        }

        private static Color ResolveLiveValidationStatusColor(
            TemplateVariableValidationResult result,
            string currentText)
        {
            if (string.IsNullOrWhiteSpace(currentText))
            {
                return Color.gray;
            }

            if (result?.HasScribanError == true || result?.UnknownVariables?.Count > 0)
            {
                return new Color(1f, 0.55f, 0.55f);
            }

            return new Color(0.55f, 0.95f, 0.55f);
        }

        private static string BuildLiveValidationStatusText(
            TemplateVariableValidationResult result,
            string currentText)
        {
            if (string.IsNullOrWhiteSpace(currentText))
            {
                return "RimChat_PromptLiveValidationIdle".Translate();
            }

            if (result?.HasScribanError == true)
            {
                return "RimChat_PromptLiveValidationError".Translate(
                    result.ScribanErrorCode,
                    result.ScribanErrorLine,
                    result.ScribanErrorColumn);
            }

            if (result?.UnknownVariables?.Count > 0)
            {
                return "RimChat_PromptLiveValidationUnknown".Translate(BuildUnknownVariableSummary(result.UnknownVariables));
            }

            int usedCount = result?.UsedVariables?.Count ?? 0;
            return "RimChat_PromptLiveValidationOk".Translate(usedCount);
        }

        private static string BuildUnknownVariableSummary(IReadOnlyList<string> unknownVariables)
        {
            if (unknownVariables == null || unknownVariables.Count == 0)
            {
                return string.Empty;
            }

            int shownCount = Math.Min(4, unknownVariables.Count);
            string joined = string.Join(", ", unknownVariables.Take(shownCount));
            if (unknownVariables.Count <= shownCount)
            {
                return joined;
            }

            return joined + $" +{unknownVariables.Count - shownCount}";
        }

        private string BuildEnvironmentPreviewDiagnostics(
            SystemPromptConfig config,
            Faction sampleFaction,
            bool proactive,
            List<string> tags)
        {
            DialogueScenarioContext context = DialogueScenarioContext.CreateDiplomacy(sampleFaction, proactive, tags);
            PromptPersistenceService.Instance.BuildEnvironmentPromptBlocksWithDiagnostics(config, context, out EnvironmentPromptBuildDiagnostics diagnostics);
            if (diagnostics == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Tags: {(diagnostics.ScenarioTags.Count > 0 ? string.Join(", ", diagnostics.ScenarioTags) : "none")}");

            List<EnvironmentSceneEntryDiagnostic> topEntries = diagnostics.SceneEntries
                .Take(16)
                .ToList();

            for (int i = 0; i < topEntries.Count; i++)
            {
                EnvironmentSceneEntryDiagnostic item = topEntries[i];
                string state = item.Included
                    ? $"included ({item.AppliedChars}/{item.OriginalChars})"
                    : $"skipped ({item.SkipReason})";

                string truncation = item.TruncatedByPerSceneLimit || item.TruncatedByTotalLimit
                    ? $" trunc:{(item.TruncatedByPerSceneLimit ? "per_scene " : string.Empty)}{(item.TruncatedByTotalLimit ? "total" : string.Empty)}"
                    : string.Empty;

                string unknownVariables = item.UnknownVariables.Count > 0
                    ? $" unknown_vars:{string.Join(",", item.UnknownVariables)}"
                    : string.Empty;

                sb.AppendLine($"- P{item.Priority} [{item.Name}] {state}{truncation}{unknownVariables}");
            }

            if (diagnostics.SceneEntries.Count > topEntries.Count)
            {
                sb.AppendLine($"... {diagnostics.SceneEntries.Count - topEntries.Count} more entries");
            }

            return sb.ToString().TrimEnd();
        }

        private void OpenPromptVariablePicker()
        {
            IReadOnlyList<PromptTemplateVariableDefinition> defs = PromptPersistenceService.Instance.GetTemplateVariableDefinitions();
            Find.WindowStack.Add(new Dialog_PromptVariablePicker(defs, token =>
            {
                if (!TryInsertVariableToken(token))
                {
                    Messages.Message("RimChat_VariableInsertFailed".Translate(), MessageTypeDefOf.RejectInput, false);
                }
                else
                {
                    _previewUpdateCooldown = 0;
                }
            }));
        }

        private void ValidateCurrentSectionVariables()
        {
            string text = GetCurrentSectionEditableText();
            if (string.IsNullOrWhiteSpace(text))
            {
                Messages.Message("RimChat_VariableValidationNoTemplate".Translate(), MessageTypeDefOf.NeutralEvent, false);
                return;
            }

            TemplateVariableValidationResult result = PromptPersistenceService.Instance.ValidateTemplateVariables(text);
            if (result.HasScribanError)
            {
                Messages.Message(
                    "RimChat_VariableValidationCompileError".Translate(
                        result.ScribanErrorCode,
                        result.ScribanErrorLine,
                        result.ScribanErrorColumn,
                        result.ScribanErrorMessage),
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            if (result.UnknownVariables.Count > 0)
            {
                string unknown = string.Join(", ", result.UnknownVariables);
                Messages.Message("RimChat_VariableValidationUnknown".Translate(unknown), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Messages.Message("RimChat_VariableValidationPass".Translate(result.UsedVariables.Count), MessageTypeDefOf.NeutralEvent, false);
        }

        private bool TryInsertVariableToken(string token)
        {
            if (TryInsertVariableTokenToEntryChannel(token))
            {
                return true;
            }

            string[] sections = _advancedPromptMode ? AdvancedSectionNames : SimpleSectionNames;
            if (_selectedSectionIndex < 0 || _selectedSectionIndex >= sections.Length)
            {
                return false;
            }

            string section = sections[_selectedSectionIndex];
            switch (section)
            {
                case "GlobalPrompt":
                    _globalPromptBuffer = (_globalPromptBuffer ?? string.Empty) + token;
                    SystemPromptConfigData.GlobalSystemPrompt = _globalPromptBuffer;
                    return true;
                case "JsonTemplate":
                    _jsonTemplateBuffer = (_jsonTemplateBuffer ?? string.Empty) + token;
                    if (SystemPromptConfigData.ResponseFormat == null) SystemPromptConfigData.ResponseFormat = new ResponseFormatConfig();
                    SystemPromptConfigData.ResponseFormat.JsonTemplate = _jsonTemplateBuffer;
                    return true;
                case "ImportantRules":
                    _importantRulesBuffer = (_importantRulesBuffer ?? string.Empty) + token;
                    if (SystemPromptConfigData.ResponseFormat == null) SystemPromptConfigData.ResponseFormat = new ResponseFormatConfig();
                    SystemPromptConfigData.ResponseFormat.ImportantRules = _importantRulesBuffer;
                    return true;
                case "EnvironmentPrompts":
                    return TryAppendVariableToSelectedEnvironmentScene(token);
                case "PromptTemplates":
                    PromptTemplateTextConfig templates = EnsurePromptTemplateConfig();
                    if (_selectedPromptTemplateFieldIndex < 0 || _selectedPromptTemplateFieldIndex >= PromptTemplateFieldKeys.Length)
                    {
                        _selectedPromptTemplateFieldIndex = 0;
                    }

                    string key = PromptTemplateFieldKeys[_selectedPromptTemplateFieldIndex];
                    if (!string.Equals(_promptTemplateEditingKey, key, StringComparison.Ordinal))
                    {
                        _promptTemplateEditingKey = key;
                        _promptTemplateEditorBuffer = GetPromptTemplateFieldValue(templates, key);
                    }

                    _promptTemplateEditorBuffer = (_promptTemplateEditorBuffer ?? string.Empty) + token;
                    SetPromptTemplateFieldValue(templates, key, _promptTemplateEditorBuffer);
                    return true;
                case "SocialCirclePrompts":
                    return TryAppendVariableToSocialCircleSection(token);
                default:
                    return false;
            }
        }

        private string GetCurrentSectionEditableText()
        {
            string[] sections = _advancedPromptMode ? AdvancedSectionNames : SimpleSectionNames;
            if (_selectedSectionIndex < 0 || _selectedSectionIndex >= sections.Length)
            {
                return string.Empty;
            }

            string section = sections[_selectedSectionIndex];
            return section switch
            {
                "GlobalPrompt" => _globalPromptBuffer ?? string.Empty,
                "JsonTemplate" => _jsonTemplateBuffer ?? string.Empty,
                "ImportantRules" => _importantRulesBuffer ?? string.Empty,
                "EnvironmentPrompts" => GetSelectedEnvironmentSceneContent(),
                "PromptTemplates" => GetCurrentPromptTemplateEditorText(),
                "SocialCirclePrompts" => GetSocialCircleEditableText(),
                _ => string.Empty
            };
        }

        private static List<string> ParseSceneTagsCsv(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
            {
                return null;
            }

            return csv
                .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(tag => tag.Trim().ToLowerInvariant())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct()
                .ToList();
        }

        private void DrawPromptActionButtonsNative(Listing_Standard listing)
        {
            Rect buttonRowRect = listing.GetRect(28f);
            float btnWidth = (buttonRowRect.width - 30f) / 4;

            Rect saveRect = new Rect(buttonRowRect.x, buttonRowRect.y, btnWidth, buttonRowRect.height);
            if (Widgets.ButtonText(saveRect, "RimChat_SavePrompt".Translate()))
            {
                SaveSystemPromptConfig();
                Messages.Message("RimChat_PromptSaved".Translate(), MessageTypeDefOf.NeutralEvent, false);
            }

            Rect resetRect = new Rect(buttonRowRect.x + btnWidth + 10f, buttonRowRect.y, btnWidth, buttonRowRect.height);
            if (Widgets.ButtonText(resetRect, "RimChat_ResetToDefault".Translate()))
            {
                ShowResetPromptConfigConfirmation();
            }

            Rect exportRect = new Rect(buttonRowRect.x + (btnWidth + 10f) * 2, buttonRowRect.y, btnWidth, buttonRowRect.height);
            if (Widgets.ButtonText(exportRect, "RimChat_ExportPrompts".Translate()))
            {
                ShowExportSystemPromptDialog();
            }

            Rect importRect = new Rect(buttonRowRect.x + (btnWidth + 10f) * 3, buttonRowRect.y, btnWidth, buttonRowRect.height);
            if (Widgets.ButtonText(importRect, "RimChat_ImportPrompts".Translate()))
            {
                ShowImportSystemPromptDialog();
            }
        }

        private void SaveSystemPromptConfig()
        {
            SyncLegacyPromptFieldsFromEntryChannels();
            PromptPersistenceService.Instance.SaveConfig(SystemPromptConfigData);
            SaveRpgPromptTextsToCustom();
            _previewUpdateCooldown = 0;
        }

        private void ShowResetPromptConfigConfirmation()
        {
            Dialog_MessageBox dialog = Dialog_MessageBox.CreateConfirmation(
                "RimChat_ResetPromptConfigConfirm".Translate(),
                () =>
                {
                    PromptPersistenceService.Instance.ResetToDefault();
                    _systemPromptConfig = PromptPersistenceService.Instance.LoadConfig();
                    _selectedApiActionIndex = -1;
                    _selectedDecisionRuleIndex = -1;
                    _previewUpdateCooldown = 0;
                    SyncBuffersToData();
                    Messages.Message("RimChat_PromptConfigReset".Translate(), MessageTypeDefOf.NeutralEvent, false);
                },
                true,
                "RimChat_ResetConfirmTitle".Translate()
            );
            Find.WindowStack.Add(dialog);
        }

        private void ShowExportSystemPromptDialog()
        {
            string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "RimChat_PromptBundle.json");
            Find.WindowStack.Add(new Dialog_PromptBundleExport(defaultPath, (path, modules) =>
            {
                try
                {
                    // Export should include the latest in-editor changes, not only last saved files.
                    SyncBuffersToData();
                    SaveSystemPromptConfig();
                    SaveRpgPromptTextsToCustom();
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimChat] Failed to flush latest prompt edits before export: {ex.Message}");
                }

                bool exported = modules == null
                    ? PromptPersistenceService.Instance.ExportConfig(path)
                    : PromptPersistenceService.Instance.ExportConfig(path, modules);
                if (exported)
                {
                    Messages.Message("RimChat_ExportSuccess".Translate(path), MessageTypeDefOf.NeutralEvent, false);
                }
                else
                {
                    Messages.Message("RimChat_ExportFailed".Translate(), MessageTypeDefOf.NegativeEvent, false);
                }
            }));
        }

        private void ShowImportSystemPromptDialog()
        {
            string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "RimChat_PromptBundle.json");
            Find.WindowStack.Add(new Dialog_LoadFile(defaultPath, (path) =>
            {
                try
                {
                    if (PromptPersistenceService.Instance.TryGetImportPreview(path, out PromptBundleImportPreview preview))
                    {
                        Find.WindowStack.Add(new Dialog_PromptBundleImportPreview(preview, modules =>
                        {
                            if (PromptPersistenceService.Instance.ImportConfig(path, modules))
                            {
                                RefreshPromptEditorStateAfterImport();
                                Messages.Message("RimChat_ImportSuccess".Translate(), MessageTypeDefOf.NeutralEvent, false);
                                return;
                            }

                            Messages.Message("RimChat_ImportFailed".Translate(), MessageTypeDefOf.NegativeEvent, false);
                        }));
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimChat] Import preview failed unexpectedly: {ex.Message}");
                }

                Log.Warning("[RimChat] Import preview unavailable. Falling back to direct full-module import.");
                if (PromptPersistenceService.Instance.ImportConfig(path))
                {
                    RefreshPromptEditorStateAfterImport();
                    Messages.Message("RimChat_ImportSuccess".Translate(), MessageTypeDefOf.NeutralEvent, false);
                    return;
                }

                Messages.Message("RimChat_ImportFailed".Translate(), MessageTypeDefOf.NegativeEvent, false);
            }));
        }

        private void RefreshPromptEditorStateAfterImport()
        {
            _systemPromptConfig = PromptPersistenceService.Instance.LoadConfig();
            LoadRpgPromptTextsFromCustom();
            _selectedApiActionIndex = -1;
            _selectedDecisionRuleIndex = -1;
            _previewUpdateCooldown = 0;
            SyncBuffersToData();
        }

    }
}
