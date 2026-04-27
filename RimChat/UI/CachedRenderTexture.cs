using System;
using UnityEngine;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: Unity IMGUI dirty-flag tracking.
    /// Responsibility: signature-based render cache controller for IMGUI panels.
    /// Tracks whether content has changed since the last render and provides
    /// dirty-signaling infrastructure.
    /// </summary>
    internal sealed class CachedRenderTexture : IDisposable
    {
        private string _lastRenderSignature = string.Empty;
        private bool _dirty = true;

        internal bool Dirty
        {
            get => _dirty;
            set => _dirty = value;
        }

        internal void MarkDirty()
        {
            _dirty = true;
        }

        internal bool MarkDirtyIfChanged(string signature)
        {
            if (string.Equals(_lastRenderSignature, signature ?? string.Empty, StringComparison.Ordinal))
            {
                return false;
            }

            _lastRenderSignature = signature ?? string.Empty;
            _dirty = true;
            return true;
        }

        internal void ClearDirty()
        {
            _dirty = false;
        }

        internal void DrawWithCacheTracking(Rect targetRect, Action<Rect> drawAction)
        {
            if (drawAction == null) return;
            drawAction(targetRect);
            _dirty = false;
        }

        public void Dispose()
        {
            _lastRenderSignature = string.Empty;
            _dirty = true;
        }
    }
}
