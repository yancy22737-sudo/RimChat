using System;
using UnityEngine;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: Unity RenderTexture API for future GPU-side caching.
    /// Responsibility: signature-based render cache controller for IMGUI panels.
    /// Tracks whether content has changed since the last render and provides
    /// dirty-signaling infrastructure. When the signature matches, downstream
    /// rendering code can skip expensive layout calculations while still
    /// performing IMGUI draw calls (which are required every frame to avoid flicker).
    ///
    /// Future optimization path: when Unity's rendering pipeline allows it,
    /// this class can be extended to cache rendered pixels into a RenderTexture
    /// and replay with a single GUI.DrawTexture call, reducing per-frame DrawCall
    /// count from N to 1 per cached panel.
    /// </summary>
    internal sealed class CachedRenderTexture : IDisposable
    {
        private string _lastRenderSignature = string.Empty;
        private bool _dirty = true;

        /// <summary>
        /// Whether the cached content needs to be recalculated on the next frame.
        /// IMGUI draw calls must still execute every frame regardless of this flag.
        /// </summary>
        internal bool Dirty
        {
            get => _dirty;
            set => _dirty = value;
        }

        /// <summary>
        /// Mark the cache as needing recalculation.
        /// </summary>
        internal void MarkDirty()
        {
            _dirty = true;
        }

        /// <summary>
        /// Mark dirty only if the signature differs from the last render.
        /// Returns true if a recalculation was scheduled.
        /// </summary>
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

        /// <summary>
        /// Clear the dirty flag after rendering is complete.
        /// </summary>
        internal void ClearDirty()
        {
            _dirty = false;
        }

        /// <summary>
        /// Execute drawAction and clear dirty flag. If not dirty, still execute
        /// the drawAction (IMGUI requires rendering every frame) but the action
        /// can check Dirty to skip expensive layout calculations.
        /// </summary>
        internal void DrawWithCacheTracking(Rect targetRect, Action<Rect> drawAction)
        {
            if (drawAction == null)
            {
                return;
            }

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
