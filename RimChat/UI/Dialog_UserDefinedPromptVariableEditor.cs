using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Config;
using RimChat.Prompting;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: custom variable validation service and Verse window/widgets APIs.
    /// Responsibility: edit one user-defined prompt variable plus its faction and pawn rule sets.
    /// </summary>
    public sealed class Dialog_UserDefinedPromptVariableEditor : Window
    {
        private readonly RimChat.Config.RimChatSettings _settings;
        private readonly UserDefinedPromptVariableConfig _originalVariable;
        private readonly Action _onSaved;
        private readonly UserDefinedPromptVariableEditModel _model;
        private readonly Dictionary<string, string> _priorityTextByRuleId = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _traitsAnyTextByRuleId = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _traitsAllTextByRuleId = new Dictionary<string, string>(StringComparer.Ordinal);

        private Vector2 _scrollPosition;
        private string _statusText = string.Empty;
        private Color _statusColor = Color.gray;
        private bool _showPawnRules;

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

        public override Vector2 InitialSize => new Vector2(980f, 780f);

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

            Rect contentRect = new Rect(inRect.x, statusRect.yMax + 6f, inRect.width, inRect.height - 86f);
            float contentHeight = CalculateContentHeight();
            Rect viewRect = new Rect(0f, 0f, contentRect.width - 16f, contentHeight);
            Widgets.BeginScrollView(contentRect, ref _scrollPosition, viewRect);

            float y = 0f;
            DrawBaseFields(viewRect, ref y);
            DrawDefaultTemplate(viewRect, ref y);
            DrawRuleSection(viewRect, ref y);
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

        private float CalculateContentHeight()
        {
            int factionRuleCount = _model.FactionRules?.Count ?? 0;
            int pawnRuleCount = _model.PawnRules?.Count ?? 0;
            float factionHeight = factionRuleCount * 216f;
            float pawnHeight = pawnRuleCount * 366f;
            return 450f + (_showPawnRules ? pawnHeight : factionHeight);
        }

        private void DrawBaseFields(Rect rect, ref float y)
        {
            Widgets.Label(new Rect(0f, y, rect.width, 24f), "RimChat_CustomVariableBaseInfo".Translate());
            y += 26f;

            float labelWidth = 140f;
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
            _model.Variable.DefaultTemplateText = Widgets.TextArea(templateRect, _model.Variable.DefaultTemplateText ?? string.Empty);
            y += 132f;
        }

        private void DrawRuleSection(Rect rect, ref float y)
        {
            Widgets.Label(new Rect(0f, y, rect.width, 24f), "RimChat_CustomVariableRuleList".Translate());
            y += 28f;

            float tabWidth = 150f;
            Rect factionTabRect = new Rect(0f, y, tabWidth, 28f);
            Rect pawnTabRect = new Rect(factionTabRect.xMax + 8f, y, tabWidth, 28f);
            DrawTabButton(factionTabRect, !_showPawnRules, "RimChat_CustomVariableFactionRules".Translate().ToString(), () => _showPawnRules = false);
            DrawTabButton(pawnTabRect, _showPawnRules, "RimChat_CustomVariablePawnRules".Translate().ToString(), () => _showPawnRules = true);
            y += 36f;

            if (_showPawnRules)
            {
                DrawPawnRules(rect, ref y);
            }
            else
            {
                DrawFactionRules(rect, ref y);
            }
        }

        private void DrawFactionRules(Rect rect, ref float y)
        {
            Rect addRect = new Rect(rect.width - 160f, y - 30f, 160f, 24f);
            if (Widgets.ButtonText(addRect, "RimChat_CustomVariableAddFactionRule".Translate()))
            {
                OpenAddFactionRuleMenu();
            }

            if (_model.FactionRules.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(0f, y, rect.width, 24f), "RimChat_CustomVariableNoFactionRules".Translate());
                GUI.color = Color.white;
                y += 30f;
                return;
            }

            for (int i = 0; i < _model.FactionRules.Count; i++)
            {
                FactionPromptVariableRuleConfig rule = _model.FactionRules[i];
                DrawFactionRuleCard(rect.width - 6f, ref y, rule, i);
            }
        }

        private void DrawPawnRules(Rect rect, ref float y)
        {
            Rect addRect = new Rect(rect.width - 160f, y - 30f, 160f, 24f);
            if (Widgets.ButtonText(addRect, "RimChat_CustomVariableAddPawnRule".Translate()))
            {
                _model.PawnRules.Add(new PawnPromptVariableRuleConfig
                {
                    VariableKey = UserDefinedPromptVariableService.NormalizeKey(_model.Variable.Key),
                    Enabled = true,
                    Order = _model.PawnRules.Count
                });
            }

            if (_model.PawnRules.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(0f, y, rect.width, 24f), "RimChat_CustomVariableNoPawnRules".Translate());
                GUI.color = Color.white;
                y += 30f;
                return;
            }

            for (int i = 0; i < _model.PawnRules.Count; i++)
            {
                PawnPromptVariableRuleConfig rule = _model.PawnRules[i];
                DrawPawnRuleCard(rect.width - 6f, ref y, rule, i);
            }
        }

        private void DrawFactionRuleCard(float width, ref float y, FactionPromptVariableRuleConfig rule, int index)
        {
            Rect boxRect = new Rect(0f, y, width, 206f);
            Widgets.DrawBoxSolid(boxRect, new Color(0.07f, 0.08f, 0.1f));
            Rect inner = boxRect.ContractedBy(6f);

            DrawRuleHeader(inner, index, UserDefinedPromptVariableRuleMatcher.BuildLayerLabel(UserDefinedPromptVariableRuleMatcher.RuleLayer.Faction), ref rule.Enabled, () => _model.FactionRules.Remove(rule));
            DrawPriorityField(new Rect(inner.x, inner.y + 30f, 140f, 24f), rule.Id, rule.Priority, value => rule.Priority = value);
            Widgets.Label(new Rect(inner.x + 150f, inner.y + 30f, inner.width - 150f, 24f), UserDefinedPromptVariableRuleMatcher.BuildFactionRuleSummary(rule));
            float rowY = inner.y + 58f;
            DrawTextFieldRow(inner.width, ref rowY, 110f, "RimChat_CustomVariableField_FactionDefName".Translate().ToString(), ref rule.FactionDefName, inner.x);

            Rect templateLabelRect = new Rect(inner.x, inner.y + 90f, inner.width, 20f);
            Widgets.Label(templateLabelRect, "RimChat_CustomVariableRuleTemplate".Translate(UserDefinedPromptVariableRuleMatcher.BuildTemplateSummary(rule.TemplateText)));
            Rect templateRect = new Rect(inner.x, templateLabelRect.yMax + 4f, inner.width, 82f);
            rule.TemplateText = Widgets.TextArea(templateRect, rule.TemplateText ?? string.Empty);
            y += boxRect.height + 8f;
        }

        private void DrawPawnRuleCard(float width, ref float y, PawnPromptVariableRuleConfig rule, int index)
        {
            Rect boxRect = new Rect(0f, y, width, 356f);
            Widgets.DrawBoxSolid(boxRect, new Color(0.07f, 0.08f, 0.1f));
            Rect inner = boxRect.ContractedBy(6f);

            UserDefinedPromptVariableRuleMatcher.RuleLayer layer = string.IsNullOrWhiteSpace(rule.NameExact)
                ? UserDefinedPromptVariableRuleMatcher.RuleLayer.PawnConditional
                : UserDefinedPromptVariableRuleMatcher.RuleLayer.PawnExact;
            DrawRuleHeader(inner, index, UserDefinedPromptVariableRuleMatcher.BuildLayerLabel(layer), ref rule.Enabled, () => _model.PawnRules.Remove(rule));
            DrawPriorityField(new Rect(inner.x, inner.y + 30f, 140f, 24f), rule.Id, rule.Priority, value => rule.Priority = value);
            Widgets.Label(new Rect(inner.x + 150f, inner.y + 30f, inner.width - 150f, 24f), UserDefinedPromptVariableRuleMatcher.BuildPawnRuleSummary(rule));

            float rowY = inner.y + 58f;
            DrawTextFieldRow(inner.width, ref rowY, 130f, "RimChat_CustomVariableField_NameExact".Translate().ToString(), ref rule.NameExact, inner.x);
            DrawTextFieldRow(inner.width, ref rowY, 130f, "RimChat_CustomVariableField_FactionDefName".Translate().ToString(), ref rule.FactionDefName, inner.x);
            DrawTextFieldRow(inner.width, ref rowY, 130f, "RimChat_CustomVariableField_RaceDefName".Translate().ToString(), ref rule.RaceDefName, inner.x);
            DrawTextFieldRow(inner.width, ref rowY, 130f, "RimChat_CustomVariableField_AgeStage".Translate().ToString(), ref rule.AgeStage, inner.x);
            DrawTextFieldRow(inner.width, ref rowY, 130f, "RimChat_CustomVariableField_XenotypeDefName".Translate().ToString(), ref rule.XenotypeDefName, inner.x);
            DrawGenderRow(inner.width, ref rowY, inner.x, rule);
            DrawPlayerControlledRow(inner.width, ref rowY, inner.x, rule);
            DrawTraitRow(inner.width, ref rowY, inner.x, rule.Id, "RimChat_CustomVariableField_TraitsAll".Translate().ToString(), rule.TraitsAll, value => rule.TraitsAll = value);
            DrawTraitRow(inner.width, ref rowY, inner.x, rule.Id + ".any", "RimChat_CustomVariableField_TraitsAny".Translate().ToString(), rule.TraitsAny, value => rule.TraitsAny = value);

            Rect templateLabelRect = new Rect(inner.x, rowY, inner.width, 20f);
            Widgets.Label(templateLabelRect, "RimChat_CustomVariableRuleTemplate".Translate(UserDefinedPromptVariableRuleMatcher.BuildTemplateSummary(rule.TemplateText)));
            Rect templateRect = new Rect(inner.x, templateLabelRect.yMax + 4f, inner.width, 84f);
            rule.TemplateText = Widgets.TextArea(templateRect, rule.TemplateText ?? string.Empty);
            y += boxRect.height + 8f;
        }

        private void DrawRuleHeader(Rect inner, int index, string layerLabel, ref bool enabled, Action onRemove)
        {
            Widgets.Label(new Rect(inner.x, inner.y, inner.width - 170f, 24f), "RimChat_CustomVariableRuleHeader".Translate(index + 1, layerLabel));
            Rect enabledRect = new Rect(inner.xMax - 160f, inner.y, 70f, 24f);
            Widgets.CheckboxLabeled(enabledRect, "RimChat_CustomVariableEnabledShort".Translate(), ref enabled);
            Rect removeRect = new Rect(inner.xMax - 84f, inner.y, 84f, 24f);
            if (Widgets.ButtonText(removeRect, "RimChat_CustomVariableRemoveRule".Translate()))
            {
                onRemove?.Invoke();
            }
        }

        private void DrawPriorityField(Rect rect, string ruleId, int currentValue, Action<int> applyValue)
        {
            string key = ruleId ?? Guid.NewGuid().ToString("N");
            if (!_priorityTextByRuleId.TryGetValue(key, out string text))
            {
                text = currentValue.ToString();
            }

            Widgets.Label(new Rect(rect.x, rect.y, 60f, rect.height), "RimChat_CustomVariableRulePriority".Translate());
            string edited = Widgets.TextField(new Rect(rect.x + 62f, rect.y, rect.width - 62f, rect.height), text);
            _priorityTextByRuleId[key] = edited;
            if (int.TryParse(edited, out int parsed))
            {
                applyValue(parsed);
            }
        }

        private void DrawTraitRow(float totalWidth, ref float y, float startX, string key, string label, List<string> values, Action<List<string>> applyValue)
        {
            if (!_traitsAnyTextByRuleId.ContainsKey(key) && !_traitsAllTextByRuleId.ContainsKey(key))
            {
                string joined = string.Join(", ", values ?? new List<string>());
                if (key.EndsWith(".any", StringComparison.Ordinal))
                {
                    _traitsAnyTextByRuleId[key] = joined;
                }
                else
                {
                    _traitsAllTextByRuleId[key] = joined;
                }
            }

            Dictionary<string, string> cache = key.EndsWith(".any", StringComparison.Ordinal) ? _traitsAnyTextByRuleId : _traitsAllTextByRuleId;
            string current = cache.TryGetValue(key, out string existing) ? existing : string.Join(", ", values ?? new List<string>());
            Rect labelRect = new Rect(startX, y + 2f, 130f, 24f);
            Rect fieldRect = new Rect(startX + 138f, y, totalWidth - 138f, 24f);
            Widgets.Label(labelRect, label);
            string edited = Widgets.TextField(fieldRect, current ?? string.Empty);
            cache[key] = edited;
            applyValue(edited
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList());
            y += 30f;
        }

        private void DrawGenderRow(float totalWidth, ref float y, float startX, PawnPromptVariableRuleConfig rule)
        {
            Rect labelRect = new Rect(startX, y + 2f, 130f, 24f);
            Rect buttonRect = new Rect(startX + 138f, y, totalWidth - 138f, 24f);
            Widgets.Label(labelRect, "RimChat_CustomVariableField_Gender".Translate());
            if (Widgets.ButtonText(buttonRect, string.IsNullOrWhiteSpace(rule.Gender) ? "RimChat_CustomVariableAnyValue".Translate() : rule.Gender))
            {
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption("RimChat_CustomVariableAnyValue".Translate(), () => rule.Gender = string.Empty),
                    new FloatMenuOption(Gender.None.ToString(), () => rule.Gender = Gender.None.ToString()),
                    new FloatMenuOption(Gender.Male.ToString(), () => rule.Gender = Gender.Male.ToString()),
                    new FloatMenuOption(Gender.Female.ToString(), () => rule.Gender = Gender.Female.ToString())
                }));
            }

            y += 30f;
        }

        private void DrawPlayerControlledRow(float totalWidth, ref float y, float startX, PawnPromptVariableRuleConfig rule)
        {
            Rect labelRect = new Rect(startX, y + 2f, 130f, 24f);
            Rect buttonRect = new Rect(startX + 138f, y, totalWidth - 138f, 24f);
            Widgets.Label(labelRect, "RimChat_CustomVariableField_PlayerControlled".Translate());
            string label = string.IsNullOrWhiteSpace(rule.PlayerControlled)
                ? "RimChat_CustomVariableAnyValue".Translate().ToString()
                : (string.Equals(rule.PlayerControlled, "true", StringComparison.OrdinalIgnoreCase)
                    ? "RimChat_CommonYes".Translate().ToString()
                    : "RimChat_CommonNo".Translate().ToString());
            if (Widgets.ButtonText(buttonRect, label))
            {
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption("RimChat_CustomVariableAnyValue".Translate(), () => rule.PlayerControlled = string.Empty),
                    new FloatMenuOption("RimChat_CommonYes".Translate(), () => rule.PlayerControlled = "true"),
                    new FloatMenuOption("RimChat_CommonNo".Translate(), () => rule.PlayerControlled = "false")
                }));
            }

            y += 30f;
        }

        private void DrawTextFieldRow(float totalWidth, ref float y, float labelWidth, string label, ref string value, float startX = 0f)
        {
            Rect labelRect = new Rect(startX, y + 2f, labelWidth, 24f);
            Rect fieldRect = new Rect(startX + labelWidth + 8f, y, totalWidth - labelWidth - 8f, 24f);
            Widgets.Label(labelRect, label);
            value = Widgets.TextField(fieldRect, value ?? string.Empty);
            y += 30f;
        }

        private void DrawTabButton(Rect rect, bool selected, string label, Action onClick)
        {
            Color oldColor = GUI.color;
            GUI.color = selected ? new Color(0.35f, 0.55f, 0.75f) : Color.white;
            if (Widgets.ButtonText(rect, label))
            {
                onClick?.Invoke();
            }

            GUI.color = oldColor;
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

            _statusColor = Color.green;
            _statusText = "RimChat_CustomVariableStatusReady".Translate();
        }

        private void OpenAddFactionRuleMenu()
        {
            List<FactionDef> defs = DefDatabase<FactionDef>.AllDefsListForReading
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.defName))
                .OrderBy(item => item.label ?? item.defName)
                .ToList();
            List<FloatMenuOption> options = defs.Select(def =>
                new FloatMenuOption($"{(def.label ?? def.defName)} ({def.defName})", () =>
                {
                    _model.FactionRules.Add(new FactionPromptVariableRuleConfig
                    {
                        VariableKey = UserDefinedPromptVariableService.NormalizeKey(_model.Variable.Key),
                        FactionDefName = def.defName,
                        Enabled = true,
                        Order = _model.FactionRules.Count
                    });
                })).ToList();
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
