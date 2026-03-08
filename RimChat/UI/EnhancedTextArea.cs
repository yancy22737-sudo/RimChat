using System;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimChat.UI
{
    /// <summary>/// 增强型text框component
 /// 特性: 滚动条支持, 字数统计与限制, 键盘快捷键, 焦点state优化
 ///</summary>
    public class EnhancedTextArea
    {
        #region 字段

        // Textcontents
        private string text = "";

        // 滚动位置
        private Vector2 scrollPosition = Vector2.zero;

        // 焦点控制
        private string controlName;
        private bool hasFocus = false;
        private bool wasFocused = false;

        // 字数限制
        private int maxLength = int.MaxValue;
        private bool enforceLimit = false;

        // 视觉state
        private Color normalBorderColor = new Color(0.3f, 0.3f, 0.3f);
        private Color focusedBorderColor = new Color(0.4f, 0.6f, 0.9f);
        private Color exceededBorderColor = new Color(0.9f, 0.3f, 0.3f);
        private Color exceededBackgroundColor = new Color(0.2f, 0.1f, 0.1f, 0.3f);

        // 统计信息display
        private bool showCharacterCount = true;
        private Vector2 countLabelPosition = new Vector2(5f, 2f);

        // Event回调
        public event Action<string> OnTextChanged;
        public event Action OnFocusGained;
        public event Action OnFocusLost;
        public event Action OnTextSubmitted;

        #endregion

        #region 属性

        public string Text
        {
            get => text;
            set
            {
                if (text != value)
                {
                    text = value ?? "";
                    EnforceLengthLimit();
                }
            }
        }

        public int MaxLength
        {
            get => maxLength;
            set
            {
                maxLength = value;
                enforceLimit = maxLength > 0 && maxLength < int.MaxValue;
                EnforceLengthLimit();
            }
        }

        public int CurrentLength => text?.Length ?? 0;
        public bool IsAtLimit => enforceLimit && CurrentLength >= maxLength;
        public bool HasExceededLimit => CurrentLength > maxLength;
        public bool IsFocused => hasFocus;

        #endregion

        #region 构造函数

        public EnhancedTextArea(string controlName, int maxLength = int.MaxValue)
        {
            this.controlName = controlName ?? $"EnhancedTextArea_{Rand.Int}";
            this.maxLength = maxLength;
            this.enforceLimit = maxLength > 0 && maxLength < int.MaxValue;
        }

        #endregion

        #region 公共方法

        /// <summary>/// 绘制text框
 ///</summary>
        public void Draw(Rect rect)
        {
            // 绘制边框背景
            DrawBackground(rect);

            // 计算内部区域 (减去边框)
            Rect innerRect = rect.ContractedBy(2f);

            // 计算text区域 (为滚动条预留空间)
            float scrollbarWidth = 16f;
            Rect textViewRect = new Rect(0, 0, innerRect.width - scrollbarWidth, CalculateContentHeight(innerRect.width - scrollbarWidth));
            Rect textVisibleRect = new Rect(innerRect.x, innerRect.y, innerRect.width - scrollbarWidth, innerRect.height);

            // 确保滚动位置合理
            ClampScrollPosition(textViewRect, textVisibleRect);

            // 开始滚动视图
            GUI.SetNextControlName(controlName);
            scrollPosition = GUI.BeginScrollView(textVisibleRect, scrollPosition, textViewRect, false, true);

            // 计算textedit区域
            Rect editRect = new Rect(0, 0, textViewRect.width, Mathf.Max(textViewRect.height, textVisibleRect.height));

            // Processing焦点state
            UpdateFocusState();

            // Processing键盘快捷键
            HandleKeyboardShortcuts();

            // 绘制并edittext
            string newText = GUI.TextArea(editRect, text, GetTextAreaStyle());

            // Apply字数限制
            if (enforceLimit && newText.Length > maxLength)
            {
                newText = newText.Substring(0, maxLength);
                GUI.changed = true;
            }

            GUI.EndScrollView();

            // Processingtext变化
            if (newText != text)
            {
                text = newText;
                OnTextChanged?.Invoke(text);
            }

            // 绘制字数统计
            if (showCharacterCount)
            {
                DrawCharacterCount(rect);
            }

            // 绘制边框
            DrawBorder(rect);

            // 更新前一帧焦点state
            wasFocused = hasFocus;
        }

        /// <summary>/// settings焦点到此text框
 ///</summary>
        public void Focus()
        {
            GUI.FocusControl(controlName);
        }

        /// <summary>/// 清除焦点
 ///</summary>
        public void Blur()
        {
            if (hasFocus)
            {
                GUI.UnfocusWindow();
            }
        }

        /// <summary>/// 全选text
 ///</summary>
        public void SelectAll()
        {
            TextEditor editor = GetTextEditor();
            if (editor != null)
            {
                editor.SelectAll();
            }
        }

        /// <summary>/// 清空text
 ///</summary>
        public void Clear()
        {
            if (!string.IsNullOrEmpty(text))
            {
                text = "";
                OnTextChanged?.Invoke(text);
            }
        }

        /// <summary>/// 在光标位置插入text
 ///</summary>
        public void InsertAtCursor(string insertText)
        {
            if (string.IsNullOrEmpty(insertText)) return;

            TextEditor editor = GetTextEditor();
            if (editor != null)
            {
                int cursorIndex = editor.cursorIndex;
                text = text.Insert(cursorIndex, insertText);
                editor.cursorIndex = cursorIndex + insertText.Length;
                OnTextChanged?.Invoke(text);
            }
            else
            {
                text += insertText;
                OnTextChanged?.Invoke(text);
            }
        }

        /// <summary>/// 滚动到底部
 ///</summary>
        public void ScrollToBottom()
        {
            scrollPosition.y = float.MaxValue;
        }

        /// <summary>/// 滚动到顶部
 ///</summary>
        public void ScrollToTop()
        {
            scrollPosition.y = 0;
        }

        #endregion

        #region 私有方法

        private void DrawBackground(Rect rect)
        {
            // 如果超出限制, 绘制警告背景
            if (HasExceededLimit)
            {
                Widgets.DrawBoxSolid(rect, exceededBackgroundColor);
            }
            else
            {
                Widgets.DrawBoxSolid(rect, new Color(0.15f, 0.15f, 0.15f, 0.8f));
            }
        }

        private void DrawBorder(Rect rect)
        {
            Color borderColor;
            if (HasExceededLimit)
            {
                borderColor = exceededBorderColor;
            }
            else if (hasFocus)
            {
                borderColor = focusedBorderColor;
            }
            else
            {
                borderColor = normalBorderColor;
            }

            GUI.color = borderColor;
            Widgets.DrawBox(rect, 2);
            GUI.color = Color.white;
        }

        private void DrawCharacterCount(Rect rect)
        {
            string countText = $"{CurrentLength}/{maxLength}";

            // 计算label位置 (右下角)
            float labelWidth = 80f;
            float labelHeight = 18f;
            Rect countRect = new Rect(
                rect.xMax - labelWidth - 5f,
                rect.yMax - labelHeight - 2f,
                labelWidth,
                labelHeight
            );

            // 根据字数stateselect颜色
            float usageRatio = (float)CurrentLength / maxLength;
            Color countColor;
            if (usageRatio > 1f)
            {
                countColor = Color.red;
            }
            else if (usageRatio > 0.9f)
            {
                countColor = Color.yellow;
            }
            else
            {
                countColor = new Color(0.6f, 0.6f, 0.6f);
            }

            // 绘制半透明背景
            Widgets.DrawBoxSolid(countRect, new Color(0.1f, 0.1f, 0.1f, 0.7f));

            // 绘制文字
            TextAnchor oldAnchor = Verse.Text.Anchor;
            Verse.Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = countColor;
            Verse.Text.Font = GameFont.Tiny;
            Widgets.Label(countRect, countText);
            Verse.Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Verse.Text.Anchor = oldAnchor;
        }

        private float CalculateContentHeight(float width)
        {
            if (string.IsNullOrEmpty(text)) return 20f;

            // 估算text高度
            GUIStyle style = GetTextAreaStyle();
            float height = style.CalcHeight(new GUIContent(text), width);
            return Mathf.Max(height + 20f, 50f);
        }

        private void ClampScrollPosition(Rect viewRect, Rect visibleRect)
        {
            float maxScrollY = Mathf.Max(0, viewRect.height - visibleRect.height);
            scrollPosition.y = Mathf.Clamp(scrollPosition.y, 0, maxScrollY);
        }

        private void UpdateFocusState()
        {
            hasFocus = GUI.GetNameOfFocusedControl() == controlName;

            // 检测焦点变化
            if (hasFocus && !wasFocused)
            {
                OnFocusGained?.Invoke();
            }
            else if (!hasFocus && wasFocused)
            {
                OnFocusLost?.Invoke();
            }
        }

        private void HandleKeyboardShortcuts()
        {
            if (!hasFocus) return;

            Event e = Event.current;
            if (e.type != EventType.KeyDown) return;

            // Ctrl+A - 全选
            if (e.control && e.keyCode == KeyCode.A)
            {
                SelectAll();
                e.Use();
            }
            // Ctrl+X - 剪切
            else if (e.control && e.keyCode == KeyCode.X)
            {
                TextEditor editor = GetTextEditor();
                if (editor != null && editor.hasSelection)
                {
                    editor.Cut();
                    e.Use();
                }
            }
            // Ctrl+V - 粘贴 (Unity自动processing, 但我们可以添加限制检查)
            else if (e.control && e.keyCode == KeyCode.V)
            {
                if (enforceLimit)
                {
                    // 延迟检查粘贴后的长度
                    // 实际限制在text变化时processing
                }
            }
            // Tab - 插入缩进 (可选)
            else if (e.keyCode == KeyCode.Tab)
            {
                InsertAtCursor("    ");
                e.Use();
            }
            // Enter - 提交 (如果settings了回调)
            else if (e.keyCode == KeyCode.Return && e.control)
            {
                OnTextSubmitted?.Invoke();
                e.Use();
            }
        }

        private void EnforceLengthLimit()
        {
            if (enforceLimit && text.Length > maxLength)
            {
                text = text.Substring(0, maxLength);
            }
        }

        private GUIStyle GetTextAreaStyle()
        {
            GUIStyle style = new GUIStyle(GUI.skin.textArea);
            style.wordWrap = true;
            style.padding = new RectOffset(6, 6, 4, 4);
            style.fontSize = 12;
            return style;
        }

        private TextEditor GetTextEditor()
        {
            // 通过reflectionget当前textedit器
            try
            {
                var field = typeof(GUIUtility).GetField("s_TextEditor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (field != null)
                {
                    return field.GetValue(null) as TextEditor;
                }
            }
            catch { }
            return null;
        }

        #endregion
    }
}
