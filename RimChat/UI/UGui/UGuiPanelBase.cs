using System;
using UnityEngine;

namespace RimChat.UI.UGui
{
    /// <summary>
    /// Dependencies: UGuiCanvasHost for parent transform and lifecycle.
    /// Responsibility: base class for UGUI panels that manage their own GameObject hierarchy,
    /// data binding, and dirty tracking. Subclasses build UI elements in BuildUI(),
    /// update them in RefreshData(), and are destroyed in Dispose().
    /// </summary>
    internal abstract class UGuiPanelBase : IDisposable
    {
        private GameObject _panelGo;
        private RectTransform _panelRect;
        private UGuiCanvasHost _host;
        private bool _disposed;
        private bool _isDirty = true;
        private string _lastDataSignature = string.Empty;

        /// <summary>
        /// Panel RectTransform for positioning within the Canvas.
        /// </summary>
        internal RectTransform PanelRect => _panelRect;

        /// <summary>
        /// Whether the panel data has changed and needs re-rendering.
        /// </summary>
        internal bool IsDirty => _isDirty;

        /// <summary>
        /// Build the UGUI element hierarchy and parent it to the Canvas root.
        /// </summary>
        internal void Initialize(UGuiCanvasHost host, string panelName = "Panel")
        {
            if (_panelGo != null)
            {
                return;
            }

            _host = host ?? throw new ArgumentNullException(nameof(host));

            // Ensure CJK font is available for all TMP_Text children
            UGuiFontManager.EnsureInitialized();

            _panelGo = new GameObject(panelName);
            _panelGo.transform.SetParent(host.CanvasRoot, false);
            _panelGo.hideFlags = HideFlags.DontSaveInEditor | HideFlags.NotEditable;
            _panelGo.layer = host.CanvasRoot.gameObject.layer;

            _panelRect = _panelGo.AddComponent<RectTransform>();
            _panelRect.anchorMin = Vector2.zero;
            _panelRect.anchorMax = Vector2.one;
            _panelRect.offsetMin = Vector2.zero;
            _panelRect.offsetMax = Vector2.zero;

            BuildUI(_panelRect);
        }

        /// <summary>
        /// Set the panel size within the Canvas coordinate space.
        /// </summary>
        internal void SetSize(float width, float height)
        {
            if (_panelRect == null)
            {
                return;
            }

            _panelRect.sizeDelta = new Vector2(width, height);
        }

        /// <summary>
        /// Set the anchored position within the Canvas.
        /// </summary>
        internal void SetPosition(float x, float y)
        {
            if (_panelRect == null)
            {
                return;
            }

            _panelRect.anchoredPosition = new Vector2(x, -y); // IMGUI Y-down to UGUI Y-up
        }

        /// <summary>
        /// Mark the panel as needing re-rendering.
        /// </summary>
        internal void MarkDirty()
        {
            _isDirty = true;
        }

        /// <summary>
        /// Mark dirty only if the data signature has changed.
        /// Returns true if a refresh was scheduled.
        /// </summary>
        internal bool MarkDirtyIfChanged(string signature)
        {
            if (string.Equals(_lastDataSignature, signature ?? string.Empty, StringComparison.Ordinal))
            {
                return false;
            }

            _lastDataSignature = signature ?? string.Empty;
            _isDirty = true;
            return true;
        }

        /// <summary>
        /// Refresh data bindings and clear dirty flag.
        /// Only performs work if the panel is dirty.
        /// </summary>
        internal void RefreshIfDirty()
        {
            if (!_isDirty || _disposed)
            {
                return;
            }

            RefreshData();
            _isDirty = false;
        }

        /// <summary>
        /// Force a full refresh regardless of dirty state.
        /// </summary>
        internal void ForceRefresh()
        {
            _isDirty = true;
            RefreshIfDirty();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            OnBeforeDispose();

            if (_panelGo != null)
            {
                UnityEngine.Object.Destroy(_panelGo);
                _panelGo = null;
            }

            _panelRect = null;
            _host = null;
        }

        /// <summary>
        /// Build the UGUI element hierarchy. Called once during Initialize().
        /// </summary>
        protected abstract void BuildUI(RectTransform parent);

        /// <summary>
        /// Update UGUI elements with current data. Called when IsDirty is true.
        /// </summary>
        protected abstract void RefreshData();

        /// <summary>
        /// Optional cleanup before GameObject destruction.
        /// </summary>
        protected virtual void OnBeforeDispose() { }
    }
}
