using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimChat.UI
{
    /// <summary>/// Dependencies: RimWorld text measurement and Verse widgets.
    /// Responsibility: paginate oversized RPG dialogue text and draw message/history navigation.
    /// </summary>
    public partial class Dialog_RPGPawnDialogue
    {
        private const int DialogueRenderFontSize = 34;
        private const float DialogueMeasureSafetyPadding = 8f;
        private const float DialogueMeasureRenderScale = DialogueRenderFontSize / 22f;

        private readonly List<string> currentTextPages = new List<string>();
        private string pagedTextCache = string.Empty;
        private string pagedSpeakerCache = string.Empty;
        private float pagedWidthCache = -1f;
        private float pagedHeightCache = -1f;
        private bool pagedLiveCache;
        private int currentTextPageIndex = 0;

        private string ResolveDialogueTextForDisplay(bool drawLive, string speakerName, string fullText, Rect textArea)
        {
            if (!CanPageCurrentDialogue(drawLive))
            {
                ResetDialogueTextPaging();
                return fullText ?? string.Empty;
            }

            EnsureDialogueTextPages(fullText, speakerName, textArea, drawLive);
            currentTextPageIndex = Mathf.Clamp(currentTextPageIndex, 0, Math.Max(0, currentTextPages.Count - 1));
            return currentTextPages.Count == 0 ? fullText ?? string.Empty : currentTextPages[currentTextPageIndex];
        }

        private bool CanPageCurrentDialogue(bool drawLive)
        {
            if (!drawLive)
            {
                return true;
            }

            bool waitingForNpc = isShowingUserText && isWaitingForDelayAfterUser && !aiResponseReady &&
                Time.realtimeSinceStartup - timeUserTextFinished >= 3.0f;
            return !isTyping && !isSendingInitialMessage && !waitingForNpc;
        }

        private void EnsureDialogueTextPages(string fullText, string speakerName, Rect textArea, bool drawLive)
        {
            string normalizedText = fullText ?? string.Empty;
            if (!RequiresDialogueTextPageRefresh(normalizedText, speakerName, textArea, drawLive))
            {
                return;
            }

            currentTextPages.Clear();
            currentTextPages.AddRange(BuildDialogueTextPages(normalizedText, textArea.width, textArea.height));
            currentTextPageIndex = 0;
            UpdateDialogueTextPageCache(normalizedText, speakerName, textArea, drawLive);
        }

        private bool RequiresDialogueTextPageRefresh(string fullText, string speakerName, Rect textArea, bool drawLive)
        {
            return !string.Equals(pagedTextCache, fullText, StringComparison.Ordinal) ||
                   !string.Equals(pagedSpeakerCache, speakerName, StringComparison.Ordinal) ||
                   Mathf.RoundToInt(pagedWidthCache) != Mathf.RoundToInt(textArea.width) ||
                   Mathf.RoundToInt(pagedHeightCache) != Mathf.RoundToInt(textArea.height) ||
                   pagedLiveCache != drawLive;
        }

        private void UpdateDialogueTextPageCache(string fullText, string speakerName, Rect textArea, bool drawLive)
        {
            pagedTextCache = fullText ?? string.Empty;
            pagedSpeakerCache = speakerName ?? string.Empty;
            pagedWidthCache = textArea.width;
            pagedHeightCache = textArea.height;
            pagedLiveCache = drawLive;
        }

        private List<string> BuildDialogueTextPages(string fullText, float width, float height)
        {
            var pages = new List<string>();
            if (string.IsNullOrWhiteSpace(fullText))
            {
                pages.Add(string.Empty);
                return pages;
            }

            int startIndex = 0;
            while (startIndex < fullText.Length)
            {
                int length = FindDialoguePageLength(fullText, startIndex, width, height);
                pages.Add(ExtractDialoguePageText(fullText, startIndex, length));
                startIndex = SkipDialoguePageSeparators(fullText, startIndex + length);
            }

            return pages.Count == 0 ? new List<string> { fullText.Trim() } : pages;
        }

        private int FindDialoguePageLength(string fullText, int startIndex, float width, float height)
        {
            int remainingLength = Math.Max(1, fullText.Length - startIndex);
            int low = 1;
            int high = remainingLength;
            int best = 1;
            while (low <= high)
            {
                int mid = low + (high - low) / 2;
                if (DoesDialoguePageFit(fullText, startIndex, mid, width, height))
                {
                    best = mid;
                    low = mid + 1;
                    continue;
                }

                high = mid - 1;
            }

            return AdjustDialoguePageLength(fullText, startIndex, best);
        }

        private bool DoesDialoguePageFit(string fullText, int startIndex, int length, float width, float height)
        {
            string candidate = ExtractDialoguePageText(fullText, startIndex, length);
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return true;
            }

            return CalcDialogueTextHeight(candidate, width) <= height;
        }

        private float CalcDialogueTextHeight(string text, float width)
        {
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Medium;
            float measureWidth = Math.Max(1f, width - DialogueMeasureSafetyPadding);
            float baseHeight = Text.CalcHeight(text ?? string.Empty, measureWidth);
            float height = (baseHeight * DialogueMeasureRenderScale) + DialogueMeasureSafetyPadding;
            Text.Font = previousFont;
            return height;
        }

        private int AdjustDialoguePageLength(string fullText, int startIndex, int rawLength)
        {
            if (startIndex + rawLength >= fullText.Length)
            {
                return rawLength;
            }

            int minLength = Math.Max(1, rawLength / 2);
            for (int offset = rawLength - 1; offset >= minLength; offset--)
            {
                if (IsDialoguePageBoundary(fullText[startIndex + offset - 1]))
                {
                    return offset;
                }
            }

            return rawLength;
        }

        private static bool IsDialoguePageBoundary(char character)
        {
            return char.IsWhiteSpace(character) || ",.;:!?，。！？；：、)]}\"'".IndexOf(character) >= 0;
        }

        private int SkipDialoguePageSeparators(string fullText, int startIndex)
        {
            int index = Math.Max(0, startIndex);
            while (index < fullText.Length && char.IsWhiteSpace(fullText[index]))
            {
                index++;
            }

            return index;
        }

        private string ExtractDialoguePageText(string fullText, int startIndex, int length)
        {
            int safeLength = Math.Max(1, Math.Min(length, fullText.Length - startIndex));
            string text = fullText.Substring(startIndex, safeLength).Trim();
            return string.IsNullOrWhiteSpace(text) ? fullText.Substring(startIndex, safeLength) : text;
        }

        private void ResetDialogueTextPaging()
        {
            currentTextPages.Clear();
            currentTextPageIndex = 0;
            pagedTextCache = string.Empty;
            pagedSpeakerCache = string.Empty;
            pagedWidthCache = -1f;
            pagedHeightCache = -1f;
            pagedLiveCache = false;
        }

        private void DrawDialogueNavigation(Rect boxRect)
        {
            DrawTextPageNavigation(boxRect);
            DrawHistoryNavigation(boxRect);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawHistoryNavigation(Rect boxRect)
        {
            if (dialogPages.Count == 0)
            {
                return;
            }

            int currentDisplay = GetCurrentDialogueDisplayIndex();
            Rect historyBox = new Rect(boxRect.xMax - 110f, boxRect.yMax - 30f, 100f, 25f);
            DrawNavigationBox(historyBox, currentDisplay > 0, currentDisplay < dialogPages.Count - 1,
                $"{currentDisplay + 1}/{dialogPages.Count}",
                () => ShowDialogueHistoryAt(currentDisplay - 1),
                () => ShowDialogueHistoryAt(currentDisplay + 1));
        }

        private void DrawTextPageNavigation(Rect boxRect)
        {
            if (currentTextPages.Count <= 1 || !CanPageCurrentDialogue(!isViewingHistory))
            {
                return;
            }

            Rect pageBox = new Rect(boxRect.xMax - 220f, boxRect.yMax - 30f, 100f, 25f);
            DrawNavigationBox(pageBox, currentTextPageIndex > 0, currentTextPageIndex < currentTextPages.Count - 1,
                $"{currentTextPageIndex + 1}/{currentTextPages.Count}",
                () => ChangeDialogueTextPage(-1),
                () => ChangeDialogueTextPage(1));
        }

        private void DrawNavigationBox(Rect boxRect, bool canGoPrev, bool canGoNext, string counterLabel, Action onPrev, Action onNext)
        {
            GUI.color = Mouse.IsOver(boxRect)
                ? new Color(0.9f, 0.9f, 0.9f, 0.9f)
                : new Color(0.5f, 0.5f, 0.5f, 0.4f);

            Rect prevRect = new Rect(boxRect.x, boxRect.y, 30f, 25f);
            Rect countRect = new Rect(boxRect.x + 30f, boxRect.y, 40f, 25f);
            Rect nextRect = new Rect(boxRect.x + 70f, boxRect.y, 30f, 25f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            DrawNavigationButton(prevRect, canGoPrev, "<", onPrev);
            Widgets.Label(countRect, counterLabel);
            DrawNavigationButton(nextRect, canGoNext, ">", onNext);
        }

        private void DrawNavigationButton(Rect rect, bool enabled, string label, Action onClick)
        {
            if (!enabled)
            {
                return;
            }

            if (Widgets.ButtonInvisible(rect))
            {
                onClick?.Invoke();
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }

            Widgets.Label(rect, label);
        }

        private int GetCurrentDialogueDisplayIndex()
        {
            return isViewingHistory ? historyViewIndex : Math.Max(0, dialogPages.Count - 1);
        }

        private void ShowDialogueHistoryAt(int displayIndex)
        {
            historyViewIndex = Mathf.Clamp(displayIndex, 0, Math.Max(0, dialogPages.Count - 1));
            isViewingHistory = historyViewIndex < dialogPages.Count - 1;
            ResetDialogueTextPaging();
        }

        private void ChangeDialogueTextPage(int direction)
        {
            currentTextPageIndex = Mathf.Clamp(currentTextPageIndex + direction, 0, Math.Max(0, currentTextPages.Count - 1));
        }
    }
}
