using System.Collections.Generic;
using RimChat.Memory;
using UnityEngine;

namespace RimChat.UI
{
    public partial class Dialog_DiplomacyDialogue
    {
        private struct MessageLayoutEntry
        {
            public float BubbleWidth;
            public float MessageHeight;
            public int CachedMessageHash;
            public int CachedVisibleChars;
        }

        private readonly Dictionary<DialogueMessageData, MessageLayoutEntry> _layoutCache =
            new Dictionary<DialogueMessageData, MessageLayoutEntry>();

        private int _lastLayoutCacheVersion = -1;
        private float _lastLayoutCacheViewportWidth = -1f;
        private float _cachedTotalContentHeight = -1f;
        private bool _layoutCacheDirty = true;
        private DialogueMessageData _typewriterActiveMsg;

        private void InvalidateLayoutCache()
        {
            _layoutCacheDirty = true;
            _cachedTotalContentHeight = -1f;
        }

        private void MarkLayoutCacheClean()
        {
            _layoutCacheDirty = false;
        }

        private bool IsLayoutCacheValid(float viewportWidth)
        {
            if (_layoutCacheDirty) return false;
            if (session == null || session.messages == null) return false;
            if (session.messageVersion != _lastLayoutCacheVersion) return false;
            if (Mathf.Abs(viewportWidth - _lastLayoutCacheViewportWidth) > 0.5f) return false;
            return true;
        }

        private void RebuildLayoutCache(float viewportWidth)
        {
            _layoutCache.Clear();

            if (session == null || session.messages == null)
            {
                _lastLayoutCacheVersion = 0;
                _lastLayoutCacheViewportWidth = viewportWidth;
                _cachedTotalContentHeight = 20f;
                MarkLayoutCacheClean();
                return;
            }

            float maxSystemWidth = GetMaxSystemMessageWidth(viewportWidth);
            float maxBubbleWidth = GetMaxBubbleWidth(viewportWidth);
            float contentHeight = 10f;
            DialogueMessageData prevMsg = null;

            for (int i = 0; i < session.messages.Count; i++)
            {
                var msg = session.messages[i];
                if (prevMsg != null && ShouldShowTimeGap(prevMsg.GetGameTick(), msg.GetGameTick()))
                {
                    contentHeight += 35f;
                }

                float maxW = msg.IsSystemMessage() ? maxSystemWidth : maxBubbleWidth;
                float bubbleWidth = CalculateBubbleWidth(msg, maxW);
                float msgHeight = CalculateMessageHeight(msg, bubbleWidth);

                _layoutCache[msg] = new MessageLayoutEntry
                {
                    BubbleWidth = bubbleWidth,
                    MessageHeight = msgHeight,
                    CachedMessageHash = (msg.message ?? "").GetHashCode(),
                    CachedVisibleChars = GetTypewriterVisibleChars(msg)
                };

                contentHeight += msgHeight + ResolveMessageBottomGap(msg);
                prevMsg = msg;
            }

            contentHeight += 10f;
            _lastLayoutCacheVersion = session.messageVersion;
            _lastLayoutCacheViewportWidth = viewportWidth;
            _cachedTotalContentHeight = contentHeight;
            MarkLayoutCacheClean();
        }

        private void UpdateTypewriterLayoutEntry(float viewportWidth)
        {
            if (_typewriterActiveMsg == null || session == null) return;

            if (!_layoutCache.TryGetValue(_typewriterActiveMsg, out MessageLayoutEntry entry)) return;

            int currentHash = (_typewriterActiveMsg.message ?? "").GetHashCode();
            int currentVisibleChars = GetTypewriterVisibleChars(_typewriterActiveMsg);

            if (entry.CachedMessageHash == currentHash && entry.CachedVisibleChars == currentVisibleChars)
            {
                return;
            }

            float maxSystemWidth = GetMaxSystemMessageWidth(viewportWidth);
            float maxBubbleWidth = GetMaxBubbleWidth(viewportWidth);
            float maxW = _typewriterActiveMsg.IsSystemMessage() ? maxSystemWidth : maxBubbleWidth;
            float newBubbleWidth = CalculateBubbleWidth(_typewriterActiveMsg, maxW);
            float newMsgHeight = CalculateMessageHeight(_typewriterActiveMsg, newBubbleWidth);

            float oldMsgHeight = entry.MessageHeight;
            float heightDelta = newMsgHeight - oldMsgHeight;

            _layoutCache[_typewriterActiveMsg] = new MessageLayoutEntry
            {
                BubbleWidth = newBubbleWidth,
                MessageHeight = newMsgHeight,
                CachedMessageHash = currentHash,
                CachedVisibleChars = currentVisibleChars
            };

            if (Mathf.Abs(heightDelta) > 0.01f)
            {
                _cachedTotalContentHeight += heightDelta;
            }
        }

        private bool TryGetCachedLayout(DialogueMessageData msg, out MessageLayoutEntry entry)
        {
            if (!_layoutCache.TryGetValue(msg, out entry)) return false;

            int currentHash = (msg.message ?? "").GetHashCode();
            if (entry.CachedMessageHash != currentHash) return false;

            int currentVisibleChars = GetTypewriterVisibleChars(msg);
            if (currentVisibleChars != entry.CachedVisibleChars) return false;

            return true;
        }

        private int GetTypewriterVisibleChars(DialogueMessageData msg)
        {
            if (msg.isPlayer || msg.IsSystemMessage()) return -1;
            if (typewriterStates.TryGetValue(msg, out TypewriterState state) && !state.IsComplete)
            {
                return state.VisibleCharCount;
            }
            return -1;
        }

        private void EnsureLayoutCache(float viewportWidth)
        {
            if (IsLayoutCacheValid(viewportWidth))
            {
                UpdateTypewriterLayoutEntry(viewportWidth);
                return;
            }
            RebuildLayoutCache(viewportWidth);
        }

        private void PreFillTypewriterStatesForExistingMessages()
        {
            if (session?.messages == null) return;

            for (int i = 0; i < session.messages.Count; i++)
            {
                var msg = session.messages[i];
                if (msg == null || msg.isPlayer || msg.IsSystemMessage()) continue;

                string text = msg.message ?? string.Empty;
                typewriterStates[msg] = new TypewriterState
                {
                    FullText = text,
                    VisibleCharCount = text.Length,
                    AccumulatedTime = text.Length / 30f,
                    IsComplete = true,
                    DisplayText = text
                };
            }
        }
    }
}
