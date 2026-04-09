using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using RimChat.UI;
using RimChat.Dialogue;
using RimChat.DiplomacySystem;
using RimChat.PawnRpgPush;
using RimChat.Prompting;
using RimChat.Core;
using RimChat.Persistence;

namespace RimChat.Config
{
    public partial class RimChatSettings : ModSettings
    {
        private Vector2 _rpgNavScroll = Vector2.zero;
        private Vector2 _rpgEditorScroll = Vector2.zero;
        private Vector2 _rpgPreviewScroll = Vector2.zero;
        private Vector2 _rpgPawnListScroll = Vector2.zero;
        private Vector2 _rpgPawnPromptScroll = Vector2.zero;
        private Vector2 _rpgInjectionScroll = Vector2.zero;
        private Vector2 _rimTalkCompatToolsScroll = Vector2.zero;
        private Vector2 _rimTalkCompatTemplateScroll = Vector2.zero;
        private Vector2 _rimTalkCompatVariableScroll = Vector2.zero;
        
        private int _selectedRPGSectionIndex = 0;
        private bool _rpgPreviewCollapsed = true;
        private float _rpgPreviewFoldAnimTime = 0f;
        private string _cachedRPGPreviewText = "";
        private int _rpgPreviewUpdateCooldown = 0;
        private Pawn _selectedRpgPawnForPersonaPrompt;

        private static readonly string[] RPGSectionNames = new string[] 
        { 
            "RPGRoleSetting", 
            "RPGDialogueStyle", 
            "RPGDynamicInjection",
            "RPGPawnPersonaPrompts",
            "RPGFormatConstraint",
            "RPGFallbackTemplates",
            "RPGApiPromptTemplates"
        };

        private void DrawTab_RPGDialogue(Rect rect)
        {
            // 固定高度, 无滚动条
            float totalHeight = 520f;
            Rect mainRect = new Rect(rect.x, rect.y, rect.width, totalHeight);

            // 主布局: 左侧导航 + 右侧edit区
            float navWidth = mainRect.width / 3.5f;
            float editorWidth = mainRect.width - navWidth - 10f;

            Rect navRect = new Rect(mainRect.x, mainRect.y, navWidth, totalHeight);
            Rect editorRect = new Rect(mainRect.x + navWidth + 10f, mainRect.y, editorWidth, totalHeight);

            // 绘制左侧导航
            DrawRPGNavigationPanel(navRect);

            // 绘制右侧edit区
            DrawRPGEditorPanel(editorRect);
        }

        private void DrawRPGNavigationPanel(Rect rect)
        {
            // 背景
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.14f));
            

            Rect innerRect = rect.ContractedBy(8f);
            float y = innerRect.y;

            // 标题
            Text.Font = GameFont.Small;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(innerRect.x, y, innerRect.width, 24f), "RimChat_RPGDialogueSettings".Translate());
            GUI.color = Color.white;
            y += 30f;

            // 分隔线
            Widgets.DrawLineHorizontal(innerRect.x, y, innerRect.width);
            y += 10f;

            // 分区列表区域
            float listHeight = innerRect.height - y - 40f; // 预留底部savebutton
            Rect listRect = new Rect(innerRect.x, y, innerRect.width, listHeight);
            
            float contentHeight = RPGSectionNames.Length * 32f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, Mathf.Max(contentHeight, listHeight));
            
            _rpgNavScroll = GUI.BeginScrollView(listRect, _rpgNavScroll, viewRect);
            
            for (int i = 0; i < RPGSectionNames.Length; i++)
            {
                string sectionName = RPGSectionNames[i];
                bool isSelected = _selectedRPGSectionIndex == i;

                Rect btnRect = new Rect(0f, i * 32f, viewRect.width, 28f);

                if (isSelected)
                    Widgets.DrawBoxSolid(btnRect, new Color(0.25f, 0.35f, 0.55f));
                else if (Mouse.IsOver(btnRect))
                    Widgets.DrawBoxSolid(btnRect, new Color(0.2f, 0.22f, 0.28f));

                if (isSelected)
                {
                    Rect accentRect = new Rect(btnRect.x, btnRect.y, 3f, btnRect.height);
                    Widgets.DrawBoxSolid(accentRect, new Color(0.4f, 0.7f, 1f));
                }

                GUI.color = isSelected ? Color.white : new Color(0.7f, 0.7f, 0.75f);
                TextAnchor oldAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(btnRect.x + 8f, btnRect.y, btnRect.width - 16f, btnRect.height), GetRPGSectionLabel(sectionName));
                Text.Anchor = oldAnchor;
                GUI.color = Color.white;
                RegisterTooltip(btnRect, GetRpgSectionTooltipKey(sectionName));

                if (Widgets.ButtonInvisible(btnRect))
                {
                    _selectedRPGSectionIndex = i;
                }
            }
            
            GUI.EndScrollView();

            // 底部savebutton
            Rect saveBtnRect = new Rect(innerRect.x, innerRect.yMax - 30f, innerRect.width, 28f);
            if (Widgets.ButtonText(saveBtnRect, "RimChat_SaveRPGPrompt".Translate()))
            {
                SaveRpgPromptTextsToCustom();
                Messages.Message("RimChat_RPGPromptSaved".Translate(), MessageTypeDefOf.NeutralEvent, false);
            }
        }

        private void DrawRPGEditorPanel(Rect rect)
        {
            // 背景
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.12f));
            

            Rect innerRect = rect.ContractedBy(10f);

            string currentSection = RPGSectionNames[_selectedRPGSectionIndex];

            // 布局: edit区 + 预览区
            float titleHeight = 30f;
            float previewHeight = _rpgPreviewCollapsed ? 40f : 240f;
            float previewGap = 10f;
            float editorHeight = innerRect.height - titleHeight - previewGap - previewHeight;

            // 分区标题
            Rect titleRect = new Rect(innerRect.x, innerRect.y, innerRect.width, titleHeight);
            GUI.color = SectionHeaderColor;
            Text.Font = GameFont.Medium;
            Widgets.Label(titleRect, GetRPGSectionLabel(currentSection));
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            // Edit区域
            Rect contentRect = new Rect(innerRect.x, innerRect.y + titleHeight, innerRect.width, editorHeight);
            
            switch (currentSection)
            {
                case "RPGRoleSetting":
                    DrawRPGTextEditor(contentRect, ref RPGRoleSetting, MaxSystemPromptLength, "RimChat_RPGRoleSettingLabel");
                    break;
                case "RPGDialogueStyle":
                    DrawRPGTextEditor(contentRect, ref RPGDialogueStyle, MaxDialoguePromptLength, "RimChat_RPGDialogueStyleLabel");
                    break;
                case "RPGDynamicInjection":
                    DrawRPGInjectionEditor(contentRect);
                    break;
                case "RPGPawnPersonaPrompts":
                    DrawRPGPawnPersonaEditor(contentRect);
                    break;
                case "RPGFormatConstraint":
                    DrawRPGTextEditor(contentRect, ref RPGFormatConstraint, MaxDialoguePromptLength, "RimChat_RPGFormatConstraintLabel");
                    break;
                case "RPGFallbackTemplates":
                    DrawRpgFallbackTemplateEditor(contentRect);
                    break;
                case "RPGApiPromptTemplates":
                    DrawRpgApiPromptTemplateEditor(contentRect);
                    break;
            }

            // 预览区域
            float previewY = innerRect.y + titleHeight + editorHeight + previewGap;
            Rect previewRect = new Rect(innerRect.x, previewY, innerRect.width, previewHeight);
            DrawRPGPreviewFoldable(previewRect);
        }

        private void DrawRPGTextEditor(Rect rect, ref string text, int maxLength, string labelKey)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(rect);
            
            int currentLength = text?.Length ?? 0;
            GUI.color = currentLength > maxLength * 0.9f ? Color.red : Color.gray;
            Rect labelRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(labelRect, $"{labelKey.Translate()} ({currentLength}/{maxLength})");
            GUI.color = Color.white;
            RegisterTooltip(labelRect, GetRpgFieldTooltipKey(labelKey));

            // Get剩余高度
            float textHeight = rect.height - listing.CurHeight - 5f;
            Rect textRect = listing.GetRect(textHeight);
            
            // 限制长度
            if (text != null && text.Length > maxLength)
                text = text.Substring(0, maxLength);

            // 计算实际contents高度
            float contentHeight = Mathf.Max(textRect.height, Text.CalcHeight(text, textRect.width - 16f) + 10f);
            Rect viewRect = new Rect(0f, 0f, textRect.width - 16f, contentHeight);
            _rpgEditorScroll = GUI.BeginScrollView(textRect, _rpgEditorScroll, viewRect);
            
            text = GUI.TextArea(viewRect, text);
            
            GUI.EndScrollView();
            
            listing.End();
        }

        private void DrawRPGInjectionEditor(Rect rect)
        {
            float contentHeight = Mathf.Max(rect.height, 300f);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, contentHeight);
            _rpgInjectionScroll = GUI.BeginScrollView(rect, _rpgInjectionScroll, viewRect);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(new Rect(0f, 0f, viewRect.width, viewRect.height));
            
            listing.Label("RimChat_RPGDynamicInjection".Translate());
            listing.GapLine();
            
            Rect selfStatusRect = listing.GetRect(24f);
            Widgets.CheckboxLabeled(selfStatusRect, "RimChat_RPGInjectSelfStatus".Translate(), ref RPGInjectSelfStatus);
            RegisterTooltip(selfStatusRect, "RimChat_RPGInjectSelfStatusTooltip");
            Rect interlocutorStatusRect = listing.GetRect(24f);
            Widgets.CheckboxLabeled(interlocutorStatusRect, "RimChat_RPGInjectInterlocutorStatus".Translate(), ref RPGInjectInterlocutorStatus);
            RegisterTooltip(interlocutorStatusRect, "RimChat_RPGInjectInterlocutorStatusTooltip");
            Rect factionBackgroundRect = listing.GetRect(24f);
            Widgets.CheckboxLabeled(factionBackgroundRect, "RimChat_RPGInjectFactionBackground".Translate(), ref RPGInjectFactionBackground);
            RegisterTooltip(factionBackgroundRect, "RimChat_RPGInjectFactionBackgroundTooltip");
            Rect nonVerbalRect = listing.GetRect(24f);
            Widgets.CheckboxLabeled(nonVerbalRect, "RimChat_EnableRPGNonVerbalPawnSpeech".Translate(), ref EnableRPGNonVerbalPawnSpeech);
            RegisterTooltip(nonVerbalRect, "RimChat_EnableRPGNonVerbalPawnSpeechTooltip");

            listing.Gap(6f);
            Rect sceneTagsRowRect = listing.GetRect(24f);
            Rect sceneTagsLabelRect = new Rect(sceneTagsRowRect.x, sceneTagsRowRect.y, 120f, sceneTagsRowRect.height);
            Rect sceneTagsInputRect = new Rect(sceneTagsLabelRect.xMax + 6f, sceneTagsRowRect.y, sceneTagsRowRect.width - sceneTagsLabelRect.width - 6f, sceneTagsRowRect.height);
            Widgets.Label(sceneTagsLabelRect, "RimChat_RpgSceneTags".Translate());
            RegisterTooltip(sceneTagsLabelRect, "RimChat_RpgSceneTagsTooltip");
            string sceneTags = RpgManualSceneTagsCsv ?? string.Empty;
            string editedTags = Widgets.TextField(sceneTagsInputRect, sceneTags);
            RegisterTooltip(sceneTagsInputRect, "RimChat_RpgSceneTagsTooltip");
            if (!string.Equals(editedTags, sceneTags, StringComparison.Ordinal))
            {
                RpgManualSceneTagsCsv = editedTags;
                _rpgPreviewUpdateCooldown = 0;
            }
            
            listing.End();
            GUI.EndScrollView();
        }

        private void DrawRPGPawnPersonaEditor(Rect rect)
        {
            var rpgManager = Current.Game?.GetComponent<GameComponent_RPGManager>();
            if (Current.Game == null || rpgManager == null)
            {
                Widgets.Label(rect, "RimChat_RPGPawnPersonaNeedGame".Translate());
                return;
            }

            List<Pawn> editablePawns = GetEditableRpgPersonaPawns();
            if (editablePawns.Count == 0)
            {
                Widgets.Label(rect, "RimChat_RPGPawnPersonaNoPawn".Translate());
                return;
            }

            if (_selectedRpgPawnForPersonaPrompt == null || !editablePawns.Contains(_selectedRpgPawnForPersonaPrompt))
            {
                _selectedRpgPawnForPersonaPrompt = editablePawns[0];
            }

            float listWidth = rect.width * 0.36f;
            Rect listRect = new Rect(rect.x, rect.y, listWidth, rect.height);
            Rect editorRect = new Rect(listRect.xMax + 8f, rect.y, rect.width - listWidth - 8f, rect.height);

            DrawRPGPawnPersonaList(listRect, editablePawns);
            DrawRPGPawnPersonaPromptEditor(editorRect, rpgManager);
        }

        private List<Pawn> GetEditableRpgPersonaPawns()
        {
            List<Map> maps = GetPlayerHomeMaps();
            var pawns = new List<Pawn>();

            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                AppendEditableMapPawns(pawns, map?.mapPawns?.FreeColonistsSpawned);
                AppendEditableMapPawns(pawns, map?.mapPawns?.PrisonersOfColonySpawned);
                AppendEditableMapPawns(pawns, map?.mapPawns?.SlavesOfColonySpawned);
            }

            return pawns
                .Where(IsEditableRpgPersonaPawn)
                .Distinct()
                .OrderBy(pawn => pawn.Name?.ToStringShort ?? pawn.LabelShortCap)
                .ToList();
        }

        private static List<Map> GetPlayerHomeMaps()
        {
            List<Map> homeMaps = Find.Maps?
                .Where(map => map != null && map.IsPlayerHome)
                .ToList();

            if (homeMaps != null && homeMaps.Count > 0)
            {
                return homeMaps;
            }

            return Find.Maps?.Where(map => map != null).ToList() ?? new List<Map>();
        }

        private static void AppendEditableMapPawns(List<Pawn> target, IEnumerable<Pawn> source)
        {
            if (target == null || source == null)
            {
                return;
            }

            foreach (Pawn pawn in source)
            {
                if (pawn != null)
                {
                    target.Add(pawn);
                }
            }
        }

        private bool IsEditableRpgPersonaPawn(Pawn pawn)
        {
            return pawn != null
                && pawn.Faction == Faction.OfPlayer
                && PawnDialogueRoutingPolicy.IsRpgDialogueEligibleRace(pawn)
                && !pawn.Dead
                && !pawn.Destroyed;
        }

        private void DrawRPGPawnPersonaList(Rect rect, List<Pawn> pawns)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.14f));

            Rect innerRect = rect.ContractedBy(6f);
            Widgets.Label(new Rect(innerRect.x, innerRect.y, innerRect.width, 24f), "RimChat_RPGPawnPersonaPawnList".Translate());

            Rect listRect = new Rect(innerRect.x, innerRect.y + 28f, innerRect.width, innerRect.height - 28f);
            float contentHeight = Mathf.Max(listRect.height, pawns.Count * 30f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, contentHeight);

            _rpgPawnListScroll = GUI.BeginScrollView(listRect, _rpgPawnListScroll, viewRect);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                Rect rowRect = new Rect(0f, i * 30f, viewRect.width, 26f);
                bool selected = pawn == _selectedRpgPawnForPersonaPrompt;

                if (selected)
                {
                    Widgets.DrawBoxSolid(rowRect, new Color(0.25f, 0.35f, 0.55f));
                }
                else if (Mouse.IsOver(rowRect))
                {
                    Widgets.DrawBoxSolid(rowRect, new Color(0.2f, 0.22f, 0.28f));
                }

                Widgets.Label(new Rect(rowRect.x + 6f, rowRect.y + 3f, rowRect.width - 10f, rowRect.height), GetPawnDisplayName(pawn));
                if (Widgets.ButtonInvisible(rowRect))
                {
                    _selectedRpgPawnForPersonaPrompt = pawn;
                }
            }

            GUI.EndScrollView();
        }

        private void DrawRPGPawnPersonaPromptEditor(Rect rect, GameComponent_RPGManager rpgManager)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.12f));

            Rect innerRect = rect.ContractedBy(8f);
            string pawnName = GetPawnDisplayName(_selectedRpgPawnForPersonaPrompt);
            Widgets.Label(new Rect(innerRect.x, innerRect.y, innerRect.width, 24f), "RimChat_RPGPawnPersonaPromptLabel".Translate(pawnName));

            Rect hintRect = new Rect(innerRect.x, innerRect.y + 24f, innerRect.width, 24f);
            GUI.color = Color.gray;
            Widgets.Label(hintRect, "RimChat_RPGPawnPersonaPromptDesc".Translate());
            GUI.color = Color.white;

            string originalPrompt = rpgManager.GetPawnPersonaPrompt(_selectedRpgPawnForPersonaPrompt);
            string editingPrompt = originalPrompt;
            int maxLength = MaxDialoguePromptLength;
            if (editingPrompt.Length > maxLength)
            {
                editingPrompt = editingPrompt.Substring(0, maxLength);
            }

            Rect textAreaRect = new Rect(innerRect.x, innerRect.y + 52f, innerRect.width, innerRect.height - 86f);
            float contentHeight = Mathf.Max(textAreaRect.height, Text.CalcHeight(editingPrompt, textAreaRect.width - 16f) + 10f);
            Rect viewRect = new Rect(0f, 0f, textAreaRect.width - 16f, contentHeight);

            _rpgPawnPromptScroll = GUI.BeginScrollView(textAreaRect, _rpgPawnPromptScroll, viewRect);
            string newPrompt = GUI.TextArea(viewRect, editingPrompt);
            GUI.EndScrollView();

            if (!string.Equals(newPrompt, originalPrompt, StringComparison.Ordinal))
            {
                if (newPrompt.Length > maxLength)
                {
                    newPrompt = newPrompt.Substring(0, maxLength);
                }

                rpgManager.SetPawnPersonaPrompt(_selectedRpgPawnForPersonaPrompt, newPrompt);
                _rpgPreviewUpdateCooldown = 0;
            }

            Rect buttonRowRect = new Rect(innerRect.x, rect.yMax - 30f, innerRect.width, 24f);
            DrawRpgPawnPersonaButtons(buttonRowRect, rpgManager);
        }

        private void DrawRpgPawnPersonaButtons(Rect rowRect, GameComponent_RPGManager rpgManager)
        {
            Rect clearButtonRect = new Rect(rowRect.x, rowRect.y, 120f, rowRect.height);
            if (Widgets.ButtonText(clearButtonRect, "RimChat_RPGPawnPersonaReset".Translate()))
            {
                rpgManager.SetPawnPersonaPrompt(_selectedRpgPawnForPersonaPrompt, string.Empty);
                _rpgPreviewUpdateCooldown = 0;
            }

            Rect debugButtonRect = new Rect(clearButtonRect.xMax + 8f, rowRect.y, rowRect.width - clearButtonRect.width - 8f, rowRect.height);
            if (!Widgets.ButtonText(debugButtonRect, "RimChat_PawnRpgPush_DebugForceTrigger".Translate()))
            {
                return;
            }

            bool ok = GameComponent_PawnRpgDialoguePushManager.Instance?.DebugForcePawnRpgProactiveDialogue() == true;
            MessageTypeDef messageType = ok ? MessageTypeDefOf.TaskCompletion : MessageTypeDefOf.RejectInput;
            string key = ok
                ? "RimChat_PawnRpgPush_DebugTriggerSuccess"
                : "RimChat_PawnRpgPush_DebugTriggerFailed";
            Messages.Message(key.Translate(), messageType, false);
        }

        private string GetPawnDisplayName(Pawn pawn)
        {
            return pawn?.Name?.ToStringShort ?? pawn?.LabelShortCap ?? "RimChat_Unknown".Translate();
        }
        private void DrawRPGPreviewFoldable(Rect rect)
        {
            // 动画processing
            if (_rpgPreviewFoldAnimTime > 0f) _rpgPreviewFoldAnimTime -= Time.deltaTime;

            // 标题栏
            Rect titleBarRect = new Rect(rect.x, rect.y, rect.width, 22f);
            Widgets.DrawBoxSolid(titleBarRect, new Color(0.15f, 0.15f, 0.15f));
            
            Rect titleRect = new Rect(rect.x + 5f, rect.y + 2f, rect.width - 30f, 20f);
            GUI.color = new Color(0.5f, 0.8f, 0.5f);
            Widgets.Label(titleRect, "RimChat_PreviewTitleShort".Translate());
            GUI.color = Color.white;

            // 折叠button
            float foldBtnSize = 18f;
            Rect foldBtnRect = new Rect(rect.xMax - foldBtnSize - 5f, rect.y + 2f, foldBtnSize, foldBtnSize);
            
            GUI.color = Mouse.IsOver(foldBtnRect) ? new Color(0.35f, 0.35f, 0.35f) : new Color(0.25f, 0.25f, 0.25f);
            Widgets.DrawBoxSolid(foldBtnRect, GUI.color);
            Widgets.DrawBox(foldBtnRect);
            
            if (Widgets.ButtonInvisible(foldBtnRect))
            {
                _rpgPreviewCollapsed = !_rpgPreviewCollapsed;
                _rpgPreviewFoldAnimTime = 0.2f;
            }

            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.white;
            Widgets.Label(foldBtnRect, _rpgPreviewCollapsed ? "▶" : "▼");
            Text.Anchor = oldAnchor;

            // Contents区
            if (!_rpgPreviewCollapsed || _rpgPreviewFoldAnimTime > 0f)
            {
                float factor = 1f;
                if (_rpgPreviewFoldAnimTime > 0f)
                {
                    float t = 1f - (_rpgPreviewFoldAnimTime / 0.2f);
                    factor = _rpgPreviewCollapsed ? 1f - t : t;
                }

                if (factor > 0.01f)
                {
                    float actualHeight = (rect.height - 24f) * factor;
                    Rect contentRect = new Rect(rect.x, rect.y + 24f, rect.width, actualHeight);

                    if (factor >= 0.95f)
                    {
                        Widgets.DrawBoxSolid(contentRect, new Color(0.08f, 0.1f, 0.08f));
                        Widgets.DrawBox(contentRect);

                        Rect innerRect = contentRect.ContractedBy(4f);
                        DrawRpgPreviewContextControls(innerRect);

                        float textStartY = innerRect.y + 52f;
                        float textHeight = Mathf.Max(20f, innerRect.height - 52f);
                        Rect textRect = new Rect(innerRect.x, textStartY, innerRect.width, textHeight);

                        UpdateRPGPreviewText();
                        Widgets.LabelScrollable(textRect, _cachedRPGPreviewText, ref _rpgPreviewScroll);
                    }
                }
            }
        }

        private string GetRPGSectionLabel(string sectionName)
        {
            return sectionName switch
            {
                "RPGRoleSetting" => "RimChat_RPGRoleSettingLabel".Translate(),
                "RPGDialogueStyle" => "RimChat_RPGDialogueStyleLabel".Translate(),
                "RPGDynamicInjection" => "RimChat_RPGDynamicInjectionSection".Translate(),
                "RPGPawnPersonaPrompts" => "RimChat_RPGPawnPersonaSection".Translate(),
                "RPGFormatConstraint" => "RimChat_RPGFormatConstraintLabel".Translate(),
                "RPGFallbackTemplates" => "RimChat_RPGFallbackTemplatesSection".Translate(),
                "RPGApiPromptTemplates" => "RimChat_RPGApiPromptTemplatesSection".Translate(),
                _ => sectionName.Translate()
            };
        }

        private static int FindRpgSectionIndex(string sectionName)
        {
            if (string.IsNullOrWhiteSpace(sectionName))
            {
                return 0;
            }

            for (int i = 0; i < RPGSectionNames.Length; i++)
            {
                if (string.Equals(RPGSectionNames[i], sectionName, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return 0;
        }

        private void UpdateRPGPreviewText()
        {
            _rpgPreviewUpdateCooldown--;
            if (_rpgPreviewUpdateCooldown <= 0)
            {
                _cachedRPGPreviewText = GenerateRPGPreviewText();
                _rpgPreviewUpdateCooldown = 60;
            }
        }

        private string GenerateRPGPreviewText()
        {
            try
            {
                Pawn initiator = PawnsFinder.AllMapsWorldAndTemporary_Alive.FirstOrDefault(p =>
                    p != null &&
                    p.Faction == Faction.OfPlayer &&
                    PawnDialogueRoutingPolicy.IsRpgDialogueEligibleRace(p) &&
                    !p.Dead &&
                    !p.Destroyed);
                Pawn target = PawnsFinder.AllMapsWorldAndTemporary_Alive.FirstOrDefault(p =>
                    p != null &&
                    p != initiator &&
                    PawnDialogueRoutingPolicy.IsRpgDialogueEligibleRace(p) &&
                    p.Faction != null &&
                    p.Faction != Faction.OfPlayer &&
                    !p.Dead &&
                    !p.Destroyed);
                if (target == null)
                {
                    target = PawnsFinder.AllMapsWorldAndTemporary_Alive.FirstOrDefault(p =>
                        p != null &&
                        p != initiator &&
                        PawnDialogueRoutingPolicy.IsRpgDialogueEligibleRace(p) &&
                        !p.Dead &&
                        !p.Destroyed);
                }

                if (initiator == null || target == null)
                {
                    return "RimChat_RpgPreviewNoContext".Translate();
                }

                var settings = RimChatMod.Settings;
                List<string> tags = ParseSceneTagsCsvForRpg(settings?.RpgPromptPreviewSceneTagsCsv);
                return PromptPersistenceService.Instance.BuildRPGFullSystemPrompt(
                    initiator,
                    target,
                    settings?.RpgPromptPreviewUseProactiveContext == true,
                    tags);
            }
            catch (Exception ex)
            {
                return $"Preview Error: {ex.Message}";
            }
        }

        private void DrawRpgPreviewContextControls(Rect rect)
        {
            var settings = RimChatMod.Settings;
            if (settings == null)
            {
                return;
            }

            Rect proactiveRect = new Rect(rect.x, rect.y, rect.width, 24f);
            bool proactive = settings.RpgPromptPreviewUseProactiveContext;
            Widgets.CheckboxLabeled(proactiveRect, "RimChat_PreviewUseProactiveContext".Translate(), ref proactive);
            if (proactive != settings.RpgPromptPreviewUseProactiveContext)
            {
                settings.RpgPromptPreviewUseProactiveContext = proactive;
                _rpgPreviewUpdateCooldown = 0;
            }

            Rect tagsRect = new Rect(rect.x, rect.y + 26f, rect.width, 24f);
            string tags = settings.RpgPromptPreviewSceneTagsCsv ?? string.Empty;
            Widgets.Label(new Rect(tagsRect.x, tagsRect.y, 120f, tagsRect.height), "RimChat_PreviewSceneTags".Translate());
            string edited = Widgets.TextField(new Rect(tagsRect.x + 124f, tagsRect.y, tagsRect.width - 124f, tagsRect.height), tags);
            if (!string.Equals(edited, tags, StringComparison.Ordinal))
            {
                settings.RpgPromptPreviewSceneTagsCsv = edited;
                _rpgPreviewUpdateCooldown = 0;
            }
        }

        private static List<string> ParseSceneTagsCsvForRpg(string csv)
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
    }
}

