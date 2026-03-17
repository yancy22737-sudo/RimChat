using System;
using RimChat.Prompting;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: quick custom-variable helpers, RimWorld runtime faction/pawn instances, and Verse window widgets.
    /// Responsibility: edit one quick faction/pawn persona rule without exposing the full custom-variable editor surface.
    /// </summary>
    internal sealed class Dialog_QuickPromptVariableRuleEditor : Window
    {
        private readonly RimChat.Config.RimChatSettings _settings;
        private readonly QuickPromptTargetKind _kind;
        private readonly Faction _faction;
        private readonly Pawn _pawn;
        private readonly QuickPromptConflictDecision _decision;
        private readonly string _targetLabel;
        private Vector2 _scrollPosition = Vector2.zero;
        private string _templateText = string.Empty;

        public Dialog_QuickPromptVariableRuleEditor(
            RimChat.Config.RimChatSettings settings,
            Faction faction,
            QuickPromptConflictDecision decision)
        {
            _settings = settings;
            _faction = faction;
            _decision = decision;
            _kind = QuickPromptTargetKind.Faction;
            _targetLabel = RimChat.Config.RimChatSettings.GetPromptWorkspaceQuickFactionLabel(faction);
            _templateText = UserDefinedPromptVariableService.GetQuickFactionTemplate(settings, faction);
            ConfigureWindow();
        }

        public Dialog_QuickPromptVariableRuleEditor(
            RimChat.Config.RimChatSettings settings,
            Pawn pawn,
            QuickPromptConflictDecision decision)
        {
            _settings = settings;
            _pawn = pawn;
            _decision = decision;
            _kind = QuickPromptTargetKind.Pawn;
            _targetLabel = RimChat.Config.RimChatSettings.GetPromptWorkspaceQuickPawnLabel(pawn);
            _templateText = UserDefinedPromptVariableService.GetQuickPawnTemplate(settings, pawn);
            ConfigureWindow();
        }

        public override Vector2 InitialSize => new Vector2(900f, 620f);

        public override void DoWindowContents(Rect inRect)
        {
            Rect titleRect = new Rect(inRect.x, inRect.y, inRect.width, 28f);
            Text.Font = GameFont.Medium;
            Widgets.Label(titleRect, GetTitle());
            Text.Font = GameFont.Small;

            float y = titleRect.yMax + 8f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "RimChat_PromptWorkbench_QuickTargetLabel".Translate(_targetLabel));
            y += 28f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "RimChat_PromptWorkbench_QuickTokenLabel".Translate(UserDefinedPromptVariableService.BuildQuickToken(_kind)));
            y += 28f;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 38f), "RimChat_PromptWorkbench_QuickEditorHint".Translate());
            GUI.color = Color.white;
            y += 42f;

            Rect editorRect = new Rect(inRect.x, y, inRect.width, inRect.height - y - 42f);
            DrawEditor(editorRect);

            Rect cancelRect = new Rect(inRect.x, inRect.yMax - 32f, 120f, 32f);
            if (Widgets.ButtonText(cancelRect, "Cancel".Translate()))
            {
                Close();
            }

            Rect saveRect = new Rect(inRect.xMax - 120f, inRect.yMax - 32f, 120f, 32f);
            if (Widgets.ButtonText(saveRect, "RimChat_Save".Translate()))
            {
                Save();
            }
        }

        private void ConfigureWindow()
        {
            doCloseButton = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }

        private string GetTitle()
        {
            return _kind == QuickPromptTargetKind.Faction
                ? "RimChat_PromptWorkbench_QuickFactionEditorTitle".Translate()
                : "RimChat_PromptWorkbench_QuickPawnEditorTitle".Translate();
        }

        private void DrawEditor(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.04f, 0.04f, 0.05f));
            Rect inner = rect.ContractedBy(6f);
            string source = _templateText ?? string.Empty;
            GUIStyle style = new GUIStyle(GUI.skin.textArea)
            {
                wordWrap = true
            };
            float contentWidth = Mathf.Max(1f, inner.width - 16f);
            float contentHeight = Mathf.Max(inner.height, style.CalcHeight(new GUIContent(source), contentWidth) + 12f);
            Rect viewRect = new Rect(0f, 0f, contentWidth, contentHeight);
            _scrollPosition = GUI.BeginScrollView(inner, _scrollPosition, viewRect);
            _templateText = GUI.TextArea(new Rect(0f, 0f, contentWidth, contentHeight), source, style);
            GUI.EndScrollView();
        }

        private void Save()
        {
            RimChat.Config.UserDefinedPromptVariableValidationResult validation;
            bool success = _kind == QuickPromptTargetKind.Faction
                ? UserDefinedPromptVariableService.TrySaveQuickFactionPrompt(_settings, _faction, _templateText, _decision, out validation)
                : UserDefinedPromptVariableService.TrySaveQuickPawnPrompt(_settings, _pawn, _templateText, _decision, out validation);

            if (!success)
            {
                Find.WindowStack.Add(new Dialog_MessageBox(string.Join("\n", validation.Errors), "OK".Translate()));
                return;
            }

            _settings.Write();
            Close();
            _settings.HandlePromptWorkspaceQuickPromptSaved(_kind, _targetLabel);
        }
    }
}
