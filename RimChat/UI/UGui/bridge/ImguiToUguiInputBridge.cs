using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RimChat.UI.UGui.Bridge
{
    /// <summary>
    /// Dependencies: UnityEngine.EventSystems, IMGUI Event.current API.
    /// Responsibility: bridge IMGUI input events (from RimWorld Window's OnGUI)
    /// to UGUI EventSystem so that UGUI panels rendered via RenderTexture
    /// can receive mouse/keyboard/scroll input.
    /// </summary>
    internal sealed class ImguiToUguiInputBridge : IDisposable
    {
        private readonly UGuiCanvasHost _host;
#pragma warning disable CS0414 // Assigned but never used - reserved for future camera-space coordinate transforms
        private readonly Camera _camera;
#pragma warning restore CS0414
        private bool _disposed;

        // State tracking for drag
        private bool _isDragging;
        private Vector2 _lastPointerPosition;

        internal ImguiToUguiInputBridge(UGuiCanvasHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        /// <summary>
        /// Process the current IMGUI Event and forward it to UGUI EventSystem.
        /// Call this during DoWindowContents BEFORE drawing the RenderTexture.
        /// </summary>
        /// <param name="canvasRect">The IMGUI Rect where the UGUI content is displayed.</param>
        internal void ProcessEvent(Rect canvasRect)
        {
            if (_disposed)
            {
                return;
            }

            Event e = Event.current;
            if (e == null)
            {
                return;
            }

            EventSystem eventSystem = _host.GetEventSystem();
            if (eventSystem == null || !eventSystem.isActiveAndEnabled)
            {
                return;
            }

            Vector2 screenPos = e.mousePosition;
            Vector2 localPos;

            // Convert IMGUI screen position to UGUI Canvas local position
            if (!TryConvertToUguiPosition(screenPos, canvasRect, out localPos))
            {
                return;
            }

            switch (e.type)
            {
                case EventType.MouseDown:
                    ForwardMouseDown(eventSystem, localPos, e);
                    break;

                case EventType.MouseUp:
                    ForwardMouseUp(eventSystem, localPos, e);
                    break;

                case EventType.MouseDrag:
                    ForwardMouseDrag(eventSystem, localPos, e);
                    break;

                case EventType.ScrollWheel:
                    ForwardScroll(eventSystem, localPos, e);
                    break;

                case EventType.KeyDown:
                    ForwardKeyDown(eventSystem, e);
                    break;

                case EventType.KeyUp:
                    ForwardKeyUp(eventSystem, e);
                    break;
            }
        }

        /// <summary>
        /// Check if the mouse position is within the canvas rect.
        /// </summary>
        internal bool IsMouseOverCanvas(Rect canvasRect)
        {
            if (_disposed)
            {
                return false;
            }

            Event e = Event.current;
            if (e == null)
            {
                return false;
            }

            return canvasRect.Contains(e.mousePosition);
        }

        public void Dispose()
        {
            _disposed = true;
        }

        private bool TryConvertToUguiPosition(Vector2 imguiScreenPos, Rect canvasRect, out Vector2 localPos)
        {
            // Check if the mouse is within the canvas area
            if (!canvasRect.Contains(imguiScreenPos))
            {
                localPos = Vector2.zero;
                return false;
            }

            // Convert from IMGUI screen coordinates to Canvas-local coordinates
            // IMGUI: origin top-left, Y down
            // UGUI: origin center, Y up (but Canvas anchored at top-left via our setup)
            float localX = imguiScreenPos.x - canvasRect.x;
            float localY = canvasRect.height - (imguiScreenPos.y - canvasRect.y); // Flip Y

            localPos = new Vector2(localX, localY);
            return true;
        }

        private void ForwardMouseDown(EventSystem eventSystem, Vector2 localPos, Event e)
        {
            PointerEventData pointerData = GetPointerData(eventSystem);
            pointerData.position = ScreenFromLocal(localPos);
            pointerData.button = ConvertMouseButton(e.button);
            pointerData.pressPosition = pointerData.position;
            pointerData.pointerPressRaycast = new RaycastResult();
            pointerData.clickCount = e.clickCount;

            ExecuteEvents.Execute(eventSystem.currentSelectedGameObject, pointerData,
                ExecuteEvents.pointerDownHandler);

            _isDragging = false;
            _lastPointerPosition = localPos;
        }

        private void ForwardMouseUp(EventSystem eventSystem, Vector2 localPos, Event e)
        {
            PointerEventData pointerData = GetPointerData(eventSystem);
            pointerData.position = ScreenFromLocal(localPos);
            pointerData.button = ConvertMouseButton(e.button);

            ExecuteEvents.Execute(eventSystem.currentSelectedGameObject, pointerData,
                ExecuteEvents.pointerUpHandler);

            if (_isDragging)
            {
                ExecuteEvents.Execute(eventSystem.currentSelectedGameObject, pointerData,
                    ExecuteEvents.endDragHandler);
                _isDragging = false;
            }

            ExecuteEvents.Execute(eventSystem.currentSelectedGameObject, pointerData,
                ExecuteEvents.pointerClickHandler);
        }

        private void ForwardMouseDrag(EventSystem eventSystem, Vector2 localPos, Event e)
        {
            PointerEventData pointerData = GetPointerData(eventSystem);
            pointerData.position = ScreenFromLocal(localPos);
            pointerData.delta = ScreenFromLocal(localPos) - ScreenFromLocal(_lastPointerPosition);

            if (!_isDragging)
            {
                ExecuteEvents.Execute(eventSystem.currentSelectedGameObject, pointerData,
                    ExecuteEvents.beginDragHandler);
                _isDragging = true;
            }

            ExecuteEvents.Execute(eventSystem.currentSelectedGameObject, pointerData,
                ExecuteEvents.dragHandler);

            _lastPointerPosition = localPos;
        }

        private void ForwardScroll(EventSystem eventSystem, Vector2 localPos, Event e)
        {
            PointerEventData pointerData = GetPointerData(eventSystem);
            pointerData.position = ScreenFromLocal(localPos);
            pointerData.scrollDelta = e.delta;

            ExecuteEvents.Execute(eventSystem.currentSelectedGameObject, pointerData,
                ExecuteEvents.scrollHandler);
        }

        private void ForwardKeyDown(EventSystem eventSystem, Event e)
        {
            // Forward keyboard events to the currently selected UGUI element
            if (eventSystem.currentSelectedGameObject == null)
            {
                return;
            }

            // Create an AxisEventData for navigation-like keys
            if (IsNavigationKey(e.keyCode))
            {
                AxisEventData axisData = new AxisEventData(eventSystem)
                {
                    moveDir = ConvertToMoveDirection(e.keyCode)
                };
                ExecuteEvents.Execute(eventSystem.currentSelectedGameObject, axisData,
                    ExecuteEvents.moveHandler);
            }
        }

        private void ForwardKeyUp(EventSystem eventSystem, Event e)
        {
            // Keyboard up events are typically not forwarded for UGUI
        }

        private PointerEventData GetPointerData(EventSystem eventSystem)
        {
            return new PointerEventData(eventSystem)
            {
                pointerId = 0
            };
        }

        private Vector2 ScreenFromLocal(Vector2 localPos)
        {
            // Convert Canvas-local position to a screen-like position for the EventSystem
            // Since our Canvas uses ScreenSpaceCamera, we need to map to screen space
            return new Vector2(localPos.x, localPos.y);
        }

        private static PointerEventData.InputButton ConvertMouseButton(int imguiButton)
        {
            switch (imguiButton)
            {
                case 0: return PointerEventData.InputButton.Left;
                case 1: return PointerEventData.InputButton.Right;
                case 2: return PointerEventData.InputButton.Middle;
                default: return PointerEventData.InputButton.Left;
            }
        }

        private static bool IsNavigationKey(KeyCode key)
        {
            return key == KeyCode.UpArrow || key == KeyCode.DownArrow
                || key == KeyCode.LeftArrow || key == KeyCode.RightArrow
                || key == KeyCode.Tab;
        }

        private static MoveDirection ConvertToMoveDirection(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.UpArrow: return MoveDirection.Up;
                case KeyCode.DownArrow: return MoveDirection.Down;
                case KeyCode.LeftArrow: return MoveDirection.Left;
                case KeyCode.RightArrow: return MoveDirection.Right;
                default: return MoveDirection.None;
            }
        }
    }
}
