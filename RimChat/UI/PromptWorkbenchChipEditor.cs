using System;
using System.Collections.Generic;
using RimChat.Prompting;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: PromptVariableTokenScanner, PromptVariableTooltipCatalog, and RimWorld GUI widgets.
    /// Responsibility: render prompt text input with lightweight non-interactive variable token chip overlays.
    /// </summary>
    internal sealed class PromptWorkbenchChipEditor
    {
        private const float MinEditorHeight = 24f;
        private const float BorderPadding = 16f;
        private const float ChipPaddingX = 3f;
        private const float ChipPaddingY = 1.5f;
        private const float LineToleranceScale = 0.5f;

        private static readonly Color ChipFillColor = new Color(37f / 255f, 52f / 255f, 69f / 255f, 0.95f);
        private static readonly Color ChipTextColor = new Color(93f / 255f, 159f / 255f, 96f / 255f, 1f);

        private readonly string _controlName;
        private GUIStyle _chipTextStyle;

        public PromptWorkbenchChipEditor(string controlName)
        {
            _controlName = string.IsNullOrWhiteSpace(controlName)
                ? "RimChat_WorkbenchChipEditor"
                : controlName.Trim();
        }

        public string Draw(Rect rect, string text, ref Vector2 scroll)
        {
            string source = text ?? string.Empty;
            float contentHeight = ResolveContentHeight(source, rect.width);
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(1f, rect.width - BorderPadding), contentHeight);
            Rect textRect = new Rect(0f, 0f, viewRect.width, contentHeight);

            scroll = GUI.BeginScrollView(rect, scroll, viewRect);
            GUI.SetNextControlName(_controlName);
            string edited = GUI.TextArea(textRect, source);
            DrawTokenOverlay(textRect, edited);
            GUI.EndScrollView();
            return edited;
        }

        private static float ResolveContentHeight(string text, float width)
        {
            float calcWidth = Mathf.Max(1f, width - BorderPadding);
            float calcHeight = Text.CalcHeight(text ?? string.Empty, calcWidth) + 10f;
            return Mathf.Max(MinEditorHeight, calcHeight);
        }

        private void DrawTokenOverlay(Rect textRect, string text)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint)
            {
                return;
            }

            List<PromptTokenSegment> tokens = CollectTokenSegments(text);
            if (tokens.Count == 0)
            {
                return;
            }

            GUIStyle textAreaStyle = GUI.skin.textArea;
            GUIContent content = new GUIContent(text ?? string.Empty);
            float lineHeight = Mathf.Max(12f, textAreaStyle.lineHeight);
            float lineTolerance = lineHeight * LineToleranceScale;

            for (int i = 0; i < tokens.Count; i++)
            {
                PromptTokenSegment token = tokens[i];
                if (!TryGetTokenRect(textRect, textAreaStyle, content, token, lineTolerance, out Rect chipRect))
                {
                    continue;
                }

                Widgets.DrawBoxSolid(chipRect, ChipFillColor);
                TooltipHandler.TipRegion(chipRect, BuildTooltip(token.VariableName));
                DrawChipLabel(chipRect, token.Text);
            }
        }

        private static List<PromptTokenSegment> CollectTokenSegments(string text)
        {
            List<PromptTokenSegment> segments = PromptVariableTokenScanner.ParseSegments(text);
            return segments.FindAll(segment => segment?.Kind == PromptTokenSegmentKind.VariableToken && segment.Length > 0);
        }

        private static bool TryGetTokenRect(
            Rect textRect,
            GUIStyle style,
            GUIContent content,
            PromptTokenSegment token,
            float lineTolerance,
            out Rect rect)
        {
            rect = default;
            if (token == null || token.StartIndex < 0 || token.Length <= 0)
            {
                return false;
            }

            int maxLength = content.text?.Length ?? 0;
            int start = Mathf.Clamp(token.StartIndex, 0, maxLength);
            int end = Mathf.Clamp(token.EndIndex, start, maxLength);
            if (end <= start)
            {
                return false;
            }

            Vector2 startPos = style.GetCursorPixelPosition(textRect, content, start);
            Vector2 endPos = style.GetCursorPixelPosition(textRect, content, end);
            if (Mathf.Abs(startPos.y - endPos.y) > lineTolerance || endPos.x < startPos.x)
            {
                return false;
            }

            float width = Mathf.Max(12f, endPos.x - startPos.x);
            float height = Mathf.Max(16f, Mathf.Max(12f, style.lineHeight) + ChipPaddingY * 2f);
            rect = new Rect(
                startPos.x - ChipPaddingX,
                startPos.y - ChipPaddingY,
                width + ChipPaddingX * 2f,
                height);
            return rect.width > 2f && rect.height > 2f;
        }

        private void DrawChipLabel(Rect chipRect, string text)
        {
            GUIStyle style = _chipTextStyle ?? (_chipTextStyle = BuildChipTextStyle());
            Color previous = GUI.color;
            GUI.color = ChipTextColor;
            Rect labelRect = new Rect(chipRect.x + 3f, chipRect.y - 0.5f, Mathf.Max(1f, chipRect.width - 6f), chipRect.height + 1f);
            GUI.Label(labelRect, text ?? string.Empty, style);
            GUI.color = previous;
        }

        private static GUIStyle BuildChipTextStyle()
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                clipping = TextClipping.Clip,
                wordWrap = false,
                richText = false
            };
            style.padding = new RectOffset(0, 0, 0, 0);
            style.margin = new RectOffset(0, 0, 0, 0);
            style.normal.textColor = Color.white;
            return style;
        }

        private static string BuildTooltip(string variableName)
        {
            PromptVariableTooltipInfo info = PromptVariableTooltipCatalog.Resolve(variableName);
            string name = "RimChat_PromptVariableTooltip_Name".Translate(info.Name);
            string scope = "RimChat_PromptVariableTooltip_Scope".Translate(info.Scope);
            string description = "RimChat_PromptVariableTooltip_Description".Translate(info.Description);
            string example = "RimChat_PromptVariableTooltip_Example".Translate(info.Example);
            return $"{name}\n{scope}\n{description}\n{example}";
        }
    }
}
