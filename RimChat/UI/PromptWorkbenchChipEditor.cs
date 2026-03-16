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

        private static readonly Color ChipFillColor = new Color(37f / 255f, 52f / 255f, 69f / 255f, 0.56f);
        private static readonly Color ChipTextColor = new Color(184f / 255f, 230f / 255f, 184f / 255f, 1f);

        private readonly string _controlName;
        private GUIStyle _chipTextStyle;
        private GUIStyle _editorTextAreaStyle;

        public PromptWorkbenchChipEditor(string controlName)
        {
            _controlName = string.IsNullOrWhiteSpace(controlName)
                ? "RimChat_WorkbenchChipEditor"
                : controlName.Trim();
        }

        public string Draw(Rect rect, string text, ref Vector2 scroll)
        {
            string source = text ?? string.Empty;
            GUIStyle textAreaStyle = GetEditorTextAreaStyle();
            float viewportWidth = Mathf.Max(1f, rect.width - BorderPadding);
            float contentWidth = viewportWidth;
            float contentHeight = ResolveContentHeight(source, textAreaStyle, contentWidth);
            Rect viewRect = new Rect(0f, 0f, contentWidth, contentHeight);
            Rect textRect = new Rect(0f, 0f, contentWidth, viewRect.height);

            scroll = ClampScroll(scroll, rect, viewRect);
            scroll = GUI.BeginScrollView(rect, scroll, viewRect, false, true);
            GUI.SetNextControlName(_controlName);
            string edited = GUI.TextArea(textRect, source, textAreaStyle);
            DrawTokenOverlay(textRect, edited, textAreaStyle);
            GUI.EndScrollView();
            return edited;
        }

        private static Vector2 ClampScroll(Vector2 scroll, Rect viewportRect, Rect viewRect)
        {
            float maxScrollY = Mathf.Max(0f, viewRect.height - viewportRect.height);
            return new Vector2(0f, Mathf.Clamp(scroll.y, 0f, maxScrollY));
        }

        private static float ResolveContentHeight(string text, GUIStyle style, float width)
        {
            float totalHeight = style.CalcHeight(new GUIContent(text ?? string.Empty), Mathf.Max(1f, width));
            return Mathf.Max(MinEditorHeight, totalHeight + 4f);
        }

        private GUIStyle GetEditorTextAreaStyle()
        {
            if (_editorTextAreaStyle == null)
            {
                _editorTextAreaStyle = new GUIStyle(GUI.skin.textArea)
                {
                    wordWrap = true,
                    richText = false
                };
            }

            return _editorTextAreaStyle;
        }

        private void DrawTokenOverlay(Rect textRect, string text, GUIStyle textAreaStyle)
        {
            if (Event.current == null)
            {
                return;
            }

            List<PromptTokenSegment> tokens = CollectTokenSegments(text);
            if (tokens.Count == 0)
            {
                return;
            }

            GUIContent content = new GUIContent(text ?? string.Empty);
            float lineHeight = Mathf.Max(12f, textAreaStyle.lineHeight);
            float lineTolerance = lineHeight * LineToleranceScale;
            bool shouldPaint = Event.current.type == EventType.Repaint;

            for (int i = 0; i < tokens.Count; i++)
            {
                PromptTokenSegment token = tokens[i];
                if (!TryGetTokenRect(textRect, textAreaStyle, content, token, lineTolerance, out Rect chipRect))
                {
                    continue;
                }

                TooltipHandler.TipRegion(chipRect, BuildTooltip(token.VariableName));
                if (!shouldPaint)
                {
                    continue;
                }

                Widgets.DrawBoxSolid(chipRect, ChipFillColor);
                DrawChipLabel(chipRect, token.Text, textAreaStyle);
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

        private void DrawChipLabel(Rect chipRect, string text, GUIStyle textAreaStyle)
        {
            GUIStyle style = GetChipTextStyle(textAreaStyle);
            Rect labelRect = new Rect(
                chipRect.x + ChipPaddingX,
                chipRect.y + ChipPaddingY,
                Mathf.Max(1f, chipRect.width - ChipPaddingX * 2f),
                Mathf.Max(1f, chipRect.height - ChipPaddingY * 2f));
            GUI.Label(labelRect, text ?? string.Empty, style);
        }

        private GUIStyle GetChipTextStyle(GUIStyle textAreaStyle)
        {
            if (_chipTextStyle == null)
            {
                _chipTextStyle = new GUIStyle(textAreaStyle ?? GUI.skin.textArea)
                {
                    wordWrap = false,
                    richText = false,
                    clipping = TextClipping.Clip
                };
                _chipTextStyle.padding = new RectOffset(0, 0, 0, 0);
                _chipTextStyle.margin = new RectOffset(0, 0, 0, 0);
                _chipTextStyle.normal.textColor = ChipTextColor;
                _chipTextStyle.hover.textColor = ChipTextColor;
                _chipTextStyle.focused.textColor = ChipTextColor;
                _chipTextStyle.active.textColor = ChipTextColor;
            }

            return _chipTextStyle;
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
