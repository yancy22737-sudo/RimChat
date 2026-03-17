using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Config;
using RimChat.Persistence;
using RimChat.Prompting;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: custom variable validation service and Verse window/widgets APIs.
    /// Responsibility: edit one user-defined prompt variable plus its faction-scoped override templates.
    /// </summary>
    public sealed class Dialog_UserDefinedPromptVariableEditor : Window
    {
        private readonly RimChat.Config.RimChatSettings _settings;
        private readonly UserDefinedPromptVariableConfig _originalVariable;
        private readonly Action _onSaved;
        private readonly UserDefinedPromptVariableEditModel _model;
        private Vector2 _scrollPosition;
        private string _statusText = string.Empty;
        private Color _statusColor = Color.gray;

        public Dialog_UserDefinedPromptVariableEditor(
            RimChat.Config.RimChatSettings settings,
            UserDefinedPromptVariableEditModel model,
            UserDefinedPromptVariableConfig originalVariable,
            Action onSaved)
        {
            _settings = settings;
            _model = model?.Clone() ?? new UserDefinedPromptVariableEditModel();
            _originalVariable = originalVariable?.Clone();
            _onSaved = onSaved;
            doCloseButton = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }

        public override Vector2 InitialSize => new Vector2(860f, 720f);

        public override void DoWindowContents(Rect inRect)
        {
            Rect titleRect = new Rect(inRect.x, inRect.y, inRect.width, 28f);
            Text.Font = GameFont.Medium;
            Widgets.Label(titleRect, "RimChat_CustomVariableEditorTitle".Translate());
            Text.Font = GameFont.Small;

            Rect statusRect = new Rect(inRect.x, titleRect.yMax + 4f, inRect.width, 22f);
            RefreshStatus();
            Color oldColor = GUI.color;
            GUI.color = _statusColor;
            Widgets.Label(statusRect, _statusText);
            GUI.color = oldColor;

            float footerHeight = 40f;
            Rect contentRect = new Rect(inRect.x, statusRect.yMax + 6f, inRect.width, inRect.height - statusRect.height - footerHeight - 16f);
            float contentHeight = 450f + Mathf.Max(0, _model.Overrides.Count) * 170f;
            Rect viewRect = new Rect(0f, 0f, contentRect.width - 16f, contentHeight);
            Widgets.BeginScrollView(contentRect, ref _scrollPosition, viewRect);

            float y = 0f;
            DrawBaseFields(viewRect, ref y);
            DrawDefaultTemplate(viewRect, ref y);
            DrawOverrides(viewRect, ref y);

            Widgets.EndScrollView();

            Rect cancelRect = new Rect(inRect.x, inRect.yMax - 36f, 120f, 32f);
            if (Widgets.ButtonText(cancelRect, "Cancel".Translate()))
            {
                Close();
            }

            Rect saveRect = new Rect(inRect.xMax - 120f, inRect.yMax - 36f, 120f, 32f);
            if (Widgets.ButtonText(saveRect, "RimChat_Save".Translate()))
            {
                Save();
            }
        }

        private void DrawBaseFields(Rect rect, ref float y)
        {
            Widgets.Label(new Rect(0f, y, rect.width, 24f), "RimChat_CustomVariableBaseInfo".Translate());
            y += 26f;

            float labelWidth = 120f;
            DrawTextFieldRow(rect.width, ref y, labelWidth, "RimChat_CustomVariableKey".Translate().ToString(), ref _model.Variable.Key);
            DrawTextFieldRow(rect.width, ref y, labelWidth, "RimChat_CustomVariableDisplayName".Translate().ToString(), ref _model.Variable.DisplayName);
            DrawTextFieldRow(rect.width, ref y, labelWidth, "RimChat_CustomVariableDescription".Translate().ToString(), ref _model.Variable.Description);

            Rect enabledRect = new Rect(0f, y, rect.width, 24f);
            Widgets.CheckboxLabeled(enabledRect, "RimChat_CustomVariableEnabled".Translate(), ref _model.Variable.Enabled);
            y += 32f;
        }

        private void DrawDefaultTemplate(Rect rect, ref float y)
        {
            Widgets.Label(new Rect(0f, y, rect.width, 24f), "RimChat_CustomVariableDefaultTemplate".Translate());
            y += 24f;
            Rect templateRect = new Rect(0f, y, rect.width - 6f, 120f);
            _model.Variable.TemplateText = Widgets.TextArea(templateRect, _model.Variable.TemplateText ?? string.Empty);
            y += 126f;
        }

        private void DrawOverrides(Rect rect, ref float y)
        {
            Rect headerRect = new Rect(0f, y, rect.width, 28f);
            Widgets.Label(new Rect(headerRect.x, headerRect.y, headerRect.width - 130f, headerRect.height), "RimChat_CustomVariableFactionOverrides".Translate());
            Rect addRect = new Rect(headerRect.xMax - 120f, headerRect.y, 120f, 24f);
            if (Widgets.ButtonText(addRect, "RimChat_CustomVariableAddOverride".Translate()))
            {
                OpenAddOverrideMenu();
            }

            y += 34f;
            if (_model.Overrides.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(0f, y, rect.width, 24f), "RimChat_CustomVariableNoOverrides".Translate());
                GUI.color = Color.white;
                y += 30f;
                return;
            }

            for (int i = 0; i < _model.Overrides.Count; i++)
            {
                FactionScopedPromptVariableOverrideConfig entry = _model.Overrides[i];
                Rect boxRect = new Rect(0f, y, rect.width - 6f, 160f);
                Widgets.DrawBoxSolid(boxRect, new Color(0.07f, 0.08f, 0.1f));
                Rect inner = boxRect.ContractedBy(6f);

                Rect factionRect = new Rect(inner.x, inner.y, inner.width - 170f, 24f);
                if (Widgets.ButtonText(factionRect, string.IsNullOrWhiteSpace(entry.FactionDefName)
                    ? "RimChat_CustomVariableSelectFaction".Translate()
                    : entry.FactionDefName))
                {
                    OpenFactionMenuFor(entry);
                }

                Rect enabledRect = new Rect(factionRect.xMax + 6f, inner.y, 70f, 24f);
                Widgets.CheckboxLabeled(enabledRect, "RimChat_CustomVariableEnabledShort".Translate(), ref entry.Enabled);

                Rect removeRect = new Rect(inner.xMax - 80f, inner.y, 80f, 24f);
                if (Widgets.ButtonText(removeRect, "RimChat_RemoveFactionTemplate".Translate()))
                {
                    _model.Overrides.RemoveAt(i);
                    break;
                }

                Rect textRect = new Rect(inner.x, enabledRect.yMax + 6f, inner.width, 112f);
                entry.TemplateText = Widgets.TextArea(textRect, entry.TemplateText ?? string.Empty);
                y += boxRect.height + 8f;
            }
        }

        private void DrawTextFieldRow(float totalWidth, ref float y, float labelWidth, string label, ref string value)
        {
            Rect labelRect = new Rect(0f, y + 2f, labelWidth, 24f);
            Rect fieldRect = new Rect(labelWidth + 8f, y, totalWidth - labelWidth - 8f, 24f);
            Widgets.Label(labelRect, label);
            value = Widgets.TextField(fieldRect, value ?? string.Empty);
            y += 30f;
        }

        private void RefreshStatus()
        {
            UserDefinedPromptVariableValidationResult validation = UserDefinedPromptVariableService.ValidateEdit(_settings, _model, _originalVariable);
            if (!validation.IsValid)
            {
                _statusColor = new Color(1f, 0.45f, 0.45f);
                _statusText = string.Join(" | ", validation.Errors.Take(3));
                return;
            }

            TemplateVariableValidationResult defaultResult = validation.TemplateResults.TryGetValue("default", out TemplateVariableValidationResult result)
                ? result
                : new TemplateVariableValidationResult();
            if (defaultResult.HasScribanError || defaultResult.UnknownVariables.Count > 0)
            {
                _statusColor = Color.yellow;
                _statusText = "RimChat_CustomVariableStatusValidation".Translate();
                return;
            }

            _statusColor = Color.green;
            _statusText = "RimChat_CustomVariableStatusReady".Translate();
        }

        private void OpenAddOverrideMenu()
        {
            List<FactionDef> defs = DefDatabase<FactionDef>.AllDefsListForReading
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.defName))
                .OrderBy(item => item.label ?? item.defName)
                .ToList();
            List<FloatMenuOption> options = defs.Select(def =>
                new FloatMenuOption($"{(def.label ?? def.defName)} ({def.defName})", () =>
                {
                    if (_model.Overrides.Any(item => string.Equals(item.FactionDefName, def.defName, StringComparison.OrdinalIgnoreCase)))
                    {
                        return;
                    }

                    _model.Overrides.Add(new FactionScopedPromptVariableOverrideConfig
                    {
                        VariableKey = _model.Variable.Key,
                        FactionDefName = def.defName,
                        Enabled = true
                    });
                })).ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenFactionMenuFor(FactionScopedPromptVariableOverrideConfig target)
        {
            List<FactionDef> defs = DefDatabase<FactionDef>.AllDefsListForReading
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.defName))
                .OrderBy(item => item.label ?? item.defName)
                .ToList();
            List<FloatMenuOption> options = defs.Select(def =>
                new FloatMenuOption($"{(def.label ?? def.defName)} ({def.defName})", () => target.FactionDefName = def.defName)).ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void Save()
        {
            if (!UserDefinedPromptVariableService.TrySaveEdit(_settings, _model, _originalVariable, out UserDefinedPromptVariableValidationResult validation))
            {
                Find.WindowStack.Add(new Dialog_MessageBox(string.Join("\n", validation.Errors), "OK".Translate()));
                return;
            }

            _settings.Write();
            _onSaved?.Invoke();
            Messages.Message("RimChat_CustomVariableSaveSuccess".Translate(UserDefinedPromptVariableService.BuildPath(_model.Variable.Key)), MessageTypeDefOf.PositiveEvent, false);
            Close();
        }
    }
}
