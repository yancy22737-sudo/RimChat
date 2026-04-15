using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RimChat.UI.UGui
{
    /// <summary>
    /// Dependencies: UnityEngine.UI Canvas/Camera/RenderTexture, RimWorld IMGUI Window lifecycle.
    /// Responsibility: manage an independent UGUI Canvas + orthographic Camera that renders into
    /// a RenderTexture, which is then displayed via GUI.DrawTexture inside an IMGUI Window.
    /// Handles lifecycle (create/resize/dispose) and per-frame rendering coordination.
    /// </summary>
    internal sealed class UGuiCanvasHost : IDisposable
    {
        private GameObject _rootGo;
        private Canvas _canvas;
        private Camera _camera;
        private RenderTexture _renderTexture;
        private EventSystem _eventSystem;
        private RectTransform _canvasRect;

        private int _textureWidth;
        private int _textureHeight;
        private bool _disposed;

        /// <summary>
        /// The RenderTexture that contains the rendered UGUI content.
        /// Draw this inside IMGUI via GUI.DrawTexture.
        /// </summary>
        internal RenderTexture RenderTexture => _renderTexture;

        /// <summary>
        /// Root RectTransform of the Canvas. Child UGUI elements should be parented here.
        /// </summary>
        internal RectTransform CanvasRoot => _canvasRect;

        /// <summary>
        /// Whether the host has been initialized and is ready for rendering.
        /// </summary>
        internal bool IsInitialized => _rootGo != null && _renderTexture != null;

        /// <summary>
        /// Create the independent UGUI Canvas, Camera, and RenderTexture.
        /// </summary>
        /// <param name="width">Initial RenderTexture width in pixels.</param>
        /// <param name="height">Initial RenderTexture height in pixels.</param>
        /// <param name="name">GameObject name prefix for debugging.</param>
        internal void Initialize(int width, int height, string name = "RimChat_UGui")
        {
            if (IsInitialized)
            {
                return;
            }

            _textureWidth = Mathf.Max(1, width);
            _textureHeight = Mathf.Max(1, height);

            // Root GameObject (hidden from scene hierarchy noise)
            _rootGo = new GameObject(name);
            _rootGo.hideFlags = HideFlags.DontSaveInEditor | HideFlags.NotEditable;
            _rootGo.layer = 5; // UI layer for UGUI raycasting

            // Canvas (Screen-Space Camera mode, rendered to our Camera)
            _canvas = _rootGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.sortingOrder = -1; // Render behind everything
            _canvasRect = _rootGo.GetComponent<RectTransform>();
            _canvasRect.sizeDelta = new Vector2(_textureWidth, _textureHeight);

            // CanvasScaler for consistent DPI-independent sizing
            CanvasScaler scaler = _rootGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            // GraphicRaycaster for input events
            _rootGo.AddComponent<GraphicRaycaster>();

            // Camera (orthographic, targets our Canvas)
            GameObject cameraGo = new GameObject(name + "_Camera");
            cameraGo.transform.SetParent(_rootGo.transform, false);
            cameraGo.hideFlags = HideFlags.DontSaveInEditor | HideFlags.NotEditable;
            cameraGo.layer = _rootGo.layer;
            _camera = cameraGo.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0, 0, 0, 0); // Transparent
            _camera.orthographic = true;
            _camera.orthographicSize = _textureHeight * 0.5f;
            _camera.nearClipPlane = -1000f;
            _camera.farClipPlane = 1000f;
            _camera.rect = new Rect(0f, 0f, 1f, 1f);
            _camera.depth = -100; // Render before game cameras
            _camera.cullingMask = 1 << _rootGo.layer;
            _camera.enabled = false; // Manual render only

            _canvas.worldCamera = _camera;

            // EventSystem (independent, not shared with game)
            CreateEventSystem();

            // RenderTexture
            CreateRenderTexture();
        }

        /// <summary>
        /// Resize the RenderTexture to match a new IMGUI rect.
        /// Call this when the parent IMGUI Window resizes.
        /// </summary>
        internal void Resize(int newWidth, int newHeight)
        {
            if (_disposed)
            {
                return;
            }

            newWidth = Mathf.Max(1, newWidth);
            newHeight = Mathf.Max(1, newHeight);

            if (newWidth == _textureWidth && newHeight == _textureHeight && _renderTexture != null)
            {
                return;
            }

            _textureWidth = newWidth;
            _textureHeight = newHeight;

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                UnityEngine.Object.Destroy(_renderTexture);
            }

            CreateRenderTexture();

            if (_canvasRect != null)
            {
                _canvasRect.sizeDelta = new Vector2(_textureWidth, _textureHeight);
            }

            if (_camera != null)
            {
                _camera.orthographicSize = _textureHeight * 0.5f;
            }
        }

        /// <summary>
        /// Render the UGUI Canvas into the RenderTexture.
        /// Call this once per frame during IMGUI EventType.Repaint,
        /// only when content has changed (dirty).
        /// </summary>
        internal void Render()
        {
            if (_disposed || _camera == null || _renderTexture == null)
            {
                return;
            }

            RenderTexture previous = RenderTexture.active;
            try
            {
                _camera.targetTexture = _renderTexture;
                _camera.Render();
            }
            finally
            {
                _camera.targetTexture = null;
                RenderTexture.active = previous;
            }
        }

        /// <summary>
        /// Draw the cached RenderTexture into the IMGUI rect.
        /// Call this inside DoWindowContents during EventType.Repaint.
        /// </summary>
        internal void DrawToImgui(Rect rect)
        {
            if (_disposed || _renderTexture == null)
            {
                return;
            }

            GUI.DrawTexture(rect, _renderTexture, ScaleMode.StretchToFill, true);
        }

        /// <summary>
        /// Get the EventSystem for input bridging.
        /// </summary>
        internal EventSystem GetEventSystem()
        {
            return _eventSystem;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_renderTexture != null)
            {
                RenderTexture.active = null;
                _renderTexture.Release();
                UnityEngine.Object.Destroy(_renderTexture);
                _renderTexture = null;
            }

            if (_eventSystem != null)
            {
                UnityEngine.Object.Destroy(_eventSystem.gameObject);
                _eventSystem = null;
            }

            if (_rootGo != null)
            {
                UnityEngine.Object.Destroy(_rootGo);
                _rootGo = null;
            }

            _canvas = null;
            _camera = null;
            _canvasRect = null;
        }

        private void CreateRenderTexture()
        {
            _renderTexture = new RenderTexture(_textureWidth, _textureHeight, 24, RenderTextureFormat.ARGB32);
            _renderTexture.antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing);
            _renderTexture.filterMode = FilterMode.Bilinear;
            _renderTexture.useMipMap = false;
            _renderTexture.Create();
        }

        private void CreateEventSystem()
        {
            // Check if an EventSystem already exists (shouldn't in our isolated root)
            _eventSystem = _rootGo.GetComponentInChildren<EventSystem>();
            if (_eventSystem != null)
            {
                return;
            }

            GameObject esGo = new GameObject("EventSystem");
            esGo.transform.SetParent(_rootGo.transform, false);
            esGo.hideFlags = HideFlags.DontSaveInEditor | HideFlags.NotEditable;
            esGo.layer = _rootGo.layer;
            _eventSystem = esGo.AddComponent<EventSystem>();
            esGo.AddComponent<StandaloneInputModule>();
        }
    }
}
