using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        private readonly struct ReadOnlyRenderBlock
        {
            public ReadOnlyRenderBlock(string text, string variableName, bool isVariable)
            {
                Text = text ?? string.Empty;
                VariableName = variableName ?? string.Empty;
                IsVariable = isVariable;
            }

            public string Text { get; }
            public string VariableName { get; }
            public bool IsVariable { get; }
        }

        private readonly struct TokenFragment
        {
            public TokenFragment(Rect rect, int startIndex, int endIndex)
            {
                Rect = rect;
                StartIndex = startIndex;
                EndIndex = endIndex;
            }

            public Rect Rect { get; }
            public int StartIndex { get; }
            public int EndIndex { get; }
        }

        private const float MinEditorHeight = 24f;
        private const float BorderPadding = 16f;
        private const float LineToleranceScale = 0.5f;

        private static readonly Color ChipTextColor = new Color(184f / 255f, 230f / 255f, 184f / 255f, 1f);

        private readonly string _controlName;
        private readonly List<PromptTokenSegment> _cachedTokenSegments = new List<PromptTokenSegment>();
        private readonly List<TokenFragment> _tokenFragmentBuffer = new List<TokenFragment>();
        private readonly Dictionary<string, string> _tooltipCache = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly GUIContent _cachedEditorContent = new GUIContent(string.Empty);
        private string _cachedTokenSource = string.Empty;
        private GUIStyle _chipTextStyle;
        private GUIStyle _editorTextAreaStyle;
        private GUIStyle _readOnlyTextStyle;

        public PromptWorkbenchChipEditor(string controlName)
        {
            _controlName = string.IsNullOrWhiteSpace(controlName)
                ? "RimChat_WorkbenchChipEditor"
                : controlName.Trim();
        }

        public string Draw(Rect rect, string text, ref Vector2 scroll)
        {
            return DrawInternal(rect, text, ref scroll, readOnly: false);
        }

        public void DrawReadOnly(Rect rect, string text, ref Vector2 scroll)
        {
            DrawReadOnlyInternal(rect, text, ref scroll);
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

        private string DrawInternal(Rect rect, string text, ref Vector2 scroll, bool readOnly)
        {
            string source = text ?? string.Empty;
            GUIStyle textAreaStyle = GetEditorTextAreaStyle();
            float viewportWidth = Mathf.Max(1f, rect.width - BorderPadding);
            float contentHeight = Mathf.Max(rect.height, ResolveContentHeight(source, textAreaStyle, viewportWidth));
            Rect viewRect = new Rect(0f, 0f, viewportWidth, contentHeight);
            Rect textRect = new Rect(0f, 0f, viewportWidth, viewRect.height);

            scroll = ClampScroll(scroll, rect, viewRect);
            scroll = GUI.BeginScrollView(rect, scroll, viewRect, false, true);
            string rendered = DrawTextArea(textRect, source, textAreaStyle, readOnly);
            DrawTokenOverlay(textRect, rendered, textAreaStyle);
            GUI.EndScrollView();
            return rendered;
        }

        private void DrawReadOnlyInternal(Rect rect, string text, ref Vector2 scroll)
        {
            string source = text ?? string.Empty;
            GUIStyle textStyle = GetReadOnlyTextStyle(GetEditorTextAreaStyle());
            List<ReadOnlyRenderBlock> blocks = BuildReadOnlyBlocks(source);
            float viewportWidth = Mathf.Max(1f, rect.width - BorderPadding);
            float contentHeight = Mathf.Max(rect.height, ResolveReadOnlyContentHeight(blocks, textStyle, viewportWidth));
            Rect viewRect = new Rect(0f, 0f, viewportWidth, contentHeight);

            scroll = ClampScroll(scroll, rect, viewRect);
            scroll = GUI.BeginScrollView(rect, scroll, viewRect, false, true);
            DrawReadOnlyBlocks(viewRect.width, blocks, textStyle);
            GUI.EndScrollView();
        }

        private string DrawTextArea(Rect rect, string source, GUIStyle style, bool readOnly)
        {
            if (!readOnly)
            {
                GUI.SetNextControlName(_controlName);
                return GUI.TextArea(rect, source, style);
            }

            bool oldEnabled = GUI.enabled;
            GUI.enabled = false;
            GUI.TextArea(rect, source, style);
            GUI.enabled = oldEnabled;
            return source;
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

        private GUIStyle GetReadOnlyTextStyle(GUIStyle textAreaStyle)
        {
            if (_readOnlyTextStyle == null)
            {
                _readOnlyTextStyle = new GUIStyle(textAreaStyle ?? GUI.skin.textArea)
                {
                    wordWrap = true,
                    richText = false,
                    clipping = TextClipping.Clip,
                    stretchHeight = false
                };
                _readOnlyTextStyle.padding = new RectOffset(0, 0, 0, 0);
                _readOnlyTextStyle.margin = new RectOffset(0, 0, 0, 0);
                _readOnlyTextStyle.normal.background = null;
                _readOnlyTextStyle.hover.background = null;
                _readOnlyTextStyle.focused.background = null;
                _readOnlyTextStyle.active.background = null;
            }

            return _readOnlyTextStyle;
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

            GUIContent content = GetCachedContent(text);
            float lineHeight = Mathf.Max(12f, textAreaStyle.lineHeight);
            float lineTolerance = lineHeight * LineToleranceScale;
            bool shouldPaint = Event.current.type == EventType.Repaint;

            for (int i = 0; i < tokens.Count; i++)
            {
                PromptTokenSegment token = tokens[i];
                _tokenFragmentBuffer.Clear();
                if (!TryBuildTokenFragments(textRect, textAreaStyle, content, token, lineTolerance, lineHeight, _tokenFragmentBuffer))
                {
                    continue;
                }

                string tooltip = GetTooltipCached(token.VariableName);
                for (int fragmentIndex = 0; fragmentIndex < _tokenFragmentBuffer.Count; fragmentIndex++)
                {
                    TokenFragment fragment = _tokenFragmentBuffer[fragmentIndex];
                    TooltipHandler.TipRegion(fragment.Rect, tooltip);
                    if (!shouldPaint)
                    {
                        continue;
                    }

                    DrawTokenLabel(fragment.Rect, SliceText(text, fragment.StartIndex, fragment.EndIndex), textAreaStyle);
                }
            }
        }

        private List<ReadOnlyRenderBlock> BuildReadOnlyBlocks(string text)
        {
            List<PromptTokenSegment> segments = PromptVariableTokenScanner.ParseSegments(text ?? string.Empty);
            if (segments.Count == 0 || segments.All(segment => segment.Kind != PromptTokenSegmentKind.VariableToken))
            {
                return new List<ReadOnlyRenderBlock> { new ReadOnlyRenderBlock(text ?? string.Empty, string.Empty, false) };
            }

            var blocks = new List<ReadOnlyRenderBlock>(segments.Count);
            var textBuffer = new StringBuilder();
            bool trimLeadingWhitespace = false;

            for (int i = 0; i < segments.Count; i++)
            {
                PromptTokenSegment segment = segments[i];
                if (segment == null)
                {
                    continue;
                }

                if (segment.Kind == PromptTokenSegmentKind.VariableToken)
                {
                    FlushReadOnlyTextBlock(blocks, textBuffer, trimLeadingWhitespace, trimTrailingWhitespace: true);
                    blocks.Add(new ReadOnlyRenderBlock(segment.Text, segment.VariableName, true));
                    trimLeadingWhitespace = true;
                    continue;
                }

                textBuffer.Append(segment.Text);
            }

            FlushReadOnlyTextBlock(blocks, textBuffer, trimLeadingWhitespace, trimTrailingWhitespace: false);
            return blocks.Count == 0
                ? new List<ReadOnlyRenderBlock> { new ReadOnlyRenderBlock(string.Empty, string.Empty, false) }
                : blocks;
        }

        private static void FlushReadOnlyTextBlock(
            ICollection<ReadOnlyRenderBlock> blocks,
            StringBuilder textBuffer,
            bool trimLeadingWhitespace,
            bool trimTrailingWhitespace)
        {
            if (textBuffer.Length == 0)
            {
                return;
            }

            string text = textBuffer.ToString().Replace("\r\n", "\n");
            textBuffer.Clear();
            if (trimLeadingWhitespace)
            {
                text = text.TrimStart();
            }

            if (trimTrailingWhitespace)
            {
                text = text.TrimEnd();
            }

            if (!string.IsNullOrEmpty(text))
            {
                blocks.Add(new ReadOnlyRenderBlock(text, string.Empty, false));
            }
        }

        private float ResolveReadOnlyContentHeight(
            IReadOnlyList<ReadOnlyRenderBlock> blocks,
            GUIStyle textStyle,
            float width)
        {
            if (blocks == null || blocks.Count == 0)
            {
                return MinEditorHeight;
            }

            float totalHeight = 0f;
            for (int i = 0; i < blocks.Count; i++)
            {
                totalHeight += ResolveReadOnlyBlockHeight(blocks[i], textStyle, width);
            }

            return Mathf.Max(MinEditorHeight, totalHeight + 4f);
        }

        private void DrawReadOnlyBlocks(float width, IReadOnlyList<ReadOnlyRenderBlock> blocks, GUIStyle textStyle)
        {
            if (blocks == null || blocks.Count == 0)
            {
                return;
            }

            float y = 0f;
            for (int i = 0; i < blocks.Count; i++)
            {
                ReadOnlyRenderBlock block = blocks[i];
                float height = ResolveReadOnlyBlockHeight(block, textStyle, width);
                Rect blockRect = new Rect(0f, y, width, height);
                if (block.IsVariable)
                {
                    TooltipHandler.TipRegion(blockRect, GetTooltipCached(block.VariableName));
                    DrawTokenLabel(blockRect, block.Text, textStyle);
                }
                else
                {
                    GUI.Label(blockRect, block.Text, textStyle);
                }

                y += height;
            }
        }

        private float ResolveReadOnlyBlockHeight(ReadOnlyRenderBlock block, GUIStyle textStyle, float width)
        {
            if (block.IsVariable)
            {
                return Mathf.Max(12f, textStyle.lineHeight > 0f ? textStyle.lineHeight : textStyle.CalcHeight(new GUIContent(block.Text), width));
            }

            return Mathf.Max(12f, textStyle.CalcHeight(new GUIContent(block.Text), Mathf.Max(1f, width)));
        }

        private List<PromptTokenSegment> CollectTokenSegments(string text)
        {
            string source = text ?? string.Empty;
            if (source.IndexOf("{{", StringComparison.Ordinal) < 0)
            {
                _cachedTokenSource = source;
                _cachedTokenSegments.Clear();
                return _cachedTokenSegments;
            }

            if (string.Equals(_cachedTokenSource, source, StringComparison.Ordinal))
            {
                return _cachedTokenSegments;
            }

            _cachedTokenSource = source;
            _cachedTokenSegments.Clear();
            List<PromptTokenSegment> segments = PromptVariableTokenScanner.ParseSegments(source);
            _cachedTokenSegments.AddRange(
                segments.Where(segment => segment?.Kind == PromptTokenSegmentKind.VariableToken && segment.Length > 0));
            return _cachedTokenSegments;
        }

        private static bool TryBuildTokenFragments(
            Rect textRect,
            GUIStyle style,
            GUIContent content,
            PromptTokenSegment token,
            float lineTolerance,
            float lineHeight,
            List<TokenFragment> fragments)
        {
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
            if (Mathf.Abs(startPos.y - endPos.y) <= lineTolerance && endPos.x >= startPos.x)
            {
                AddTokenFragment(fragments, startPos.x, endPos.x, startPos.y, lineHeight, start, end, maxLength);
                return fragments.Count > 0;
            }

            BuildWrappedTokenFragments(
                textRect,
                style,
                content,
                start,
                end,
                maxLength,
                lineTolerance,
                lineHeight,
                fragments);
            return fragments.Count > 0;
        }

        private static void BuildWrappedTokenFragments(
            Rect textRect,
            GUIStyle style,
            GUIContent content,
            int start,
            int end,
            int maxLength,
            float lineTolerance,
            float lineHeight,
            List<TokenFragment> fragments)
        {
            Vector2 startPos = style.GetCursorPixelPosition(textRect, content, start);
            Vector2 endPos = style.GetCursorPixelPosition(textRect, content, end);
            float segmentStartX = startPos.x;
            float currentY = startPos.y;
            float lineMaxX = textRect.xMax - 1f;
            int segmentStartIndex = start;

            for (int cursor = start + 1; cursor <= end; cursor++)
            {
                Vector2 cursorPos = style.GetCursorPixelPosition(textRect, content, cursor);
                if (Mathf.Abs(cursorPos.y - currentY) <= lineTolerance)
                {
                    continue;
                }

                AddTokenFragment(fragments, segmentStartX, lineMaxX, currentY, lineHeight, segmentStartIndex, cursor, maxLength);
                currentY = cursorPos.y;
                segmentStartX = textRect.x;
                segmentStartIndex = cursor;
            }

            float finalEndX = Mathf.Max(segmentStartX + 4f, endPos.x);
            AddTokenFragment(fragments, segmentStartX, finalEndX, endPos.y, lineHeight, segmentStartIndex, end, maxLength);
        }

        private static void AddTokenFragment(
            List<TokenFragment> fragments,
            float startX,
            float endX,
            float y,
            float lineHeight,
            int startIndex,
            int endIndex,
            int maxLength)
        {
            float width = Mathf.Max(12f, endX - startX);
            float height = Mathf.Max(12f, lineHeight);
            var rect = new Rect(startX, y, width, height);
            int clampedStart = Mathf.Clamp(startIndex, 0, maxLength);
            int clampedEnd = Mathf.Clamp(endIndex, clampedStart, maxLength);
            if (rect.width > 2f && rect.height > 2f && clampedEnd > clampedStart)
            {
                fragments.Add(new TokenFragment(rect, clampedStart, clampedEnd));
            }
        }

        private void DrawTokenLabel(Rect tokenRect, string text, GUIStyle textAreaStyle)
        {
            GUIStyle style = GetChipTextStyle(textAreaStyle);
            Rect labelRect = new Rect(tokenRect.x, tokenRect.y, Mathf.Max(1f, tokenRect.width), Mathf.Max(1f, tokenRect.height));
            GUI.Label(labelRect, text ?? string.Empty, style);
        }

        private static string SliceText(string source, int startIndex, int endIndex)
        {
            string text = source ?? string.Empty;
            int clampedStart = Mathf.Clamp(startIndex, 0, text.Length);
            int clampedEnd = Mathf.Clamp(endIndex, clampedStart, text.Length);
            return clampedEnd <= clampedStart
                ? string.Empty
                : text.Substring(clampedStart, clampedEnd - clampedStart);
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

        private GUIContent GetCachedContent(string text)
        {
            _cachedEditorContent.text = text ?? string.Empty;
            return _cachedEditorContent;
        }

        private string GetTooltipCached(string variableName)
        {
            string key = variableName ?? string.Empty;
            if (!_tooltipCache.TryGetValue(key, out string tooltip))
            {
                tooltip = BuildTooltip(key);
                _tooltipCache[key] = tooltip;
            }

            return tooltip;
        }

        private static string BuildTooltip(string variableName)
        {
            PromptVariableTooltipInfo info = PromptVariableTooltipCatalog.Resolve(variableName);
            string name = "RimChat_PromptVariableTooltip_Name".Translate(info.Name);
            string dataType = "RimChat_PromptVariableTooltip_DataType".Translate(info.DataType);
            string description = "RimChat_PromptVariableTooltip_Description".Translate(info.Description);
            string typicalValues = "RimChat_PromptVariableTooltip_TypicalValues".Translate(BuildTypicalValuesText(info.TypicalValues));
            return $"{name}\n{dataType}\n{description}\n{typicalValues}";
        }

        private static string BuildTypicalValuesText(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return "RimChat_PromptVariableTooltip_NoTypicalValues".Translate().ToString();
            }

            var lines = new List<string>(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                lines.Add($"{i + 1}) {values[i]}");
            }

            return string.Join("\n", lines);
        }
    }
}
