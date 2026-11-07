using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Cwcbb.Tools.CwcMontage;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// Standalone physical section system track component (MontageSectionTrackElement).
    /// Fully decouples the Section Marker and color blocks from the ruler into a dedicated system track at the top,
    /// providing clean, intuitive section dragging, a context menu, and add-split-point interaction.
    /// </summary>
    public class MontageSectionTrackElement
    {
        #region Private Constants

        public const float TRACK_HEIGHT = 24f;
        private const float BASE_PIXELS_PER_SECOND = 200f;

        #endregion

        #region Private Fields

        private MontageSequenceAsset _targetAsset;
        private float _clipLength = 1f;
        private float _frameRate = 30f;
        private float _zoomLevel = 1.0f;
        private float _contentDuration = 0f;

        private VisualElement _headerElement;
        private VisualElement _contentElement;
        private VisualElement _handlesContainer;

        private readonly List<VisualElement> _splitHandleElements = new();
        private readonly List<float> _liveSplitTimes = new();
        private int _draggingSplitIndex = -1;
        private int _selectedSplitIndex = -1;

        #endregion

        #region Public Properties

        public VisualElement HeaderElement => _headerElement;
        public VisualElement ContentElement => _contentElement;
        public float PixelsPerSecond => BASE_PIXELS_PER_SECOND * _zoomLevel;
        public float ContentPixelWidth => Mathf.Max(100f, _clipLength * PixelsPerSecond);

        #endregion

        #region Public Events

        /// <summary>
        /// Raised when a physical split point is added (parameter is the split time in seconds).
        /// </summary>
        public event Action<float> OnSplitAdded;

        /// <summary>
        /// Raised while a split point is moved during dragging (parameters are the index and live snapped time in seconds; lightweight redraw).
        /// </summary>
        public event Action<int, float> OnSplitMovedLive;

        /// <summary>
        /// Raised when split point dragging ends or an update is confirmed (parameters are the index and new time in seconds; persisted).
        /// </summary>
        public event Action<int, float> OnSplitMoved;

        /// <summary>
        /// Raised when a split point is deleted (parameter is the split point index).
        /// </summary>
        public event Action<int> OnSplitRemoved;

        #endregion

        #region Constructors

        public MontageSectionTrackElement(
            MontageSequenceAsset targetAsset,
            float clipLength,
            float frameRate,
            float zoomLevel)
        {
            _targetAsset = targetAsset;
            _clipLength = Mathf.Max(0.001f, clipLength);
            _frameRate = Mathf.Max(1f, frameRate);
            _zoomLevel = Mathf.Clamp(zoomLevel, 0.005f, 20f);

            // 1. Left header
            _headerElement = new VisualElement();
            _headerElement.AddToClassList("montage-section-track-header");
            _headerElement.style.height = TRACK_HEIGHT;
            _headerElement.style.minHeight = TRACK_HEIGHT;
            _headerElement.style.maxHeight = TRACK_HEIGHT;
            _headerElement.style.flexShrink = 0;
            _headerElement.style.flexGrow = 0;

            var titleLabel = new Label("Sections");
            titleLabel.AddToClassList("montage-section-track-title");
            _headerElement.Add(titleLabel);

            var addSplitBtn = new Button(OnAddSplitClicked) { text = "+" };
            addSplitBtn.AddToClassList("montage-track-btn-toggle");
            addSplitBtn.tooltip = "Add a Section split marker at current time";
            _headerElement.Add(addSplitBtn);

            // 2. Right content
            _contentElement = new VisualElement();
            _contentElement.AddToClassList("montage-section-track-content");
            _contentElement.style.height = TRACK_HEIGHT;
            _contentElement.style.minHeight = TRACK_HEIGHT;
            _contentElement.style.maxHeight = TRACK_HEIGHT;
            _contentElement.style.flexShrink = 0;
            _contentElement.style.flexGrow = 0;
            _contentElement.style.width = ContentPixelWidth;
            _contentElement.generateVisualContent += OnGenerateVisualContent;

            _handlesContainer = new VisualElement();
            _handlesContainer.style.position = Position.Absolute;
            _handlesContainer.style.top = 0;
            _handlesContainer.style.left = 0;
            _handlesContainer.style.right = 0;
            _handlesContainer.style.bottom = 0;
            _contentElement.Add(_handlesContainer);

            // Register mouse events
            _contentElement.RegisterCallback<MouseDownEvent>(OnMouseDown);
            _contentElement.RegisterCallback<MouseMoveEvent>(OnMouseMove);
            _contentElement.RegisterCallback<MouseUpEvent>(OnMouseUp);

            UpdateHandles();
        }

        #endregion

        #region Public Methods

        public void SetTargetAsset(MontageSequenceAsset asset, float clipLength, float frameRate, float contentDuration = -1f)
        {
            _targetAsset = asset;
            _clipLength = Mathf.Max(0.001f, clipLength);
            _frameRate = Mathf.Max(1f, frameRate);
            _contentDuration = contentDuration >= 0f ? contentDuration : (_targetAsset != null ? _targetAsset.TotalDuration : _clipLength);
            _contentElement.style.width = ContentPixelWidth;
            UpdateHandles();
            _contentElement.MarkDirtyRepaint();
        }

        public void SetZoom(float zoomLevel)
        {
            _zoomLevel = Mathf.Clamp(zoomLevel, 0.005f, 20f);
            _contentElement.style.width = ContentPixelWidth;
            UpdateHandles();
            _contentElement.MarkDirtyRepaint();
        }

        #endregion

        #region Private Drawing and Handles Updates

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            float totalWidth = ContentPixelWidth;
            float totalHeight = TRACK_HEIGHT;

            if (totalWidth <= 0 || totalHeight <= 0 || _clipLength <= 0)
            {
                return;
            }

            var painter = mgc.painter2D;
            float pps = PixelsPerSecond;

            int splitCount = _liveSplitTimes.Count;
            int sectionCount = splitCount + 1;
            float activeEnd = _contentDuration > 0.001f ? _contentDuration : _clipLength;

            Color sectionColorEven = new Color(0.18f, 0.24f, 0.32f, 0.85f);
            Color sectionColorOdd = new Color(0.14f, 0.19f, 0.26f, 0.85f);
            Color sectionBorderCol = new Color(0.35f, 0.48f, 0.65f, 0.5f);

            for (int s = 0; s < sectionCount; s++)
            {
                float start = s == 0 ? 0f : _liveSplitTimes[s - 1];
                float end = s == splitCount ? Mathf.Max(start, activeEnd) : _liveSplitTimes[s];
                float xStart = start * pps;
                float xEnd = end * pps;
                float sWidth = Mathf.Max(0f, xEnd - xStart);
                if (sWidth <= 0.001f) continue;

                // Fill the color block background
                painter.fillColor = (s % 2 == 0) ? sectionColorEven : sectionColorOdd;
                painter.BeginPath();
                painter.MoveTo(new Vector2(xStart, 0));
                painter.LineTo(new Vector2(xStart + sWidth, 0));
                painter.LineTo(new Vector2(xStart + sWidth, totalHeight));
                painter.LineTo(new Vector2(xStart, totalHeight));
                painter.ClosePath();
                painter.Fill();

                // Left/right split borders
                painter.strokeColor = sectionBorderCol;
                painter.lineWidth = 1f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(xStart, 0));
                painter.LineTo(new Vector2(xStart, totalHeight));
                painter.MoveTo(new Vector2(xEnd, 0));
                painter.LineTo(new Vector2(xEnd, totalHeight));
                painter.Stroke();
            }

            // Draw the content end boundary marker
            if (_contentDuration > 0.001f && _contentDuration <= _clipLength)
            {
                float endX = _contentDuration * pps;
                painter.strokeColor = new Color(0.35f, 0.65f, 1.0f, 0.65f);
                painter.lineWidth = 1.5f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(endX, 0));
                painter.LineTo(new Vector2(endX, totalHeight));
                painter.Stroke();
            }

            // Bottom horizontal divider line
            painter.strokeColor = new Color(0f, 0f, 0f, 0.4f);
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0, totalHeight));
            painter.LineTo(new Vector2(totalWidth, totalHeight));
            painter.Stroke();
        }

        private void UpdateHandles()
        {
            _handlesContainer.Clear();
            _splitHandleElements.Clear();
            _liveSplitTimes.Clear();

            if (_targetAsset?.SplitTimestamps != null)
            {
                _liveSplitTimes.AddRange(_targetAsset.SplitTimestamps);
            }

            float pps = PixelsPerSecond;

            // 1. Add section text labels
            int splitCount = _liveSplitTimes.Count;
            int sectionCount = splitCount + 1;
            float activeEnd = _contentDuration > 0.001f ? _contentDuration : _clipLength;
            for (int s = 0; s < sectionCount; s++)
            {
                float start = s == 0 ? 0f : _liveSplitTimes[s - 1];
                float end = s == splitCount ? Mathf.Max(start, activeEnd) : _liveSplitTimes[s];
                float xStart = start * pps;
                float xEnd = end * pps;
                float sWidth = xEnd - xStart;

                if (sWidth > 35f)
                {
                    var label = new Label($"Section {s + 1}");
                    label.AddToClassList("montage-section-track-label");
                    label.style.left = xStart + 6f;
                    label.style.top = 3f;
                    label.pickingMode = PickingMode.Ignore;
                    _handlesContainer.Add(label);
                }
            }

            // 2. Add split point handles
            for (int i = 0; i < _liveSplitTimes.Count; i++)
            {
                int splitIndex = i;
                float splitTime = _liveSplitTimes[i];
                float xPos = splitTime * pps;

                var handle = new VisualElement();
                handle.AddToClassList("montage-section-split-handle");
                if (splitIndex == _selectedSplitIndex)
                {
                    handle.AddToClassList("montage-section-split-handle-selected");
                }
                handle.style.left = xPos;
                handle.tooltip = $"Split #{splitIndex + 1}: {splitTime:F3}s (Frame {Mathf.RoundToInt(splitTime * _frameRate)})\nDrag to adjust, Right click to remove";

                var icon = new Label("S");
                icon.style.fontSize = 8;
                icon.style.unityFontStyleAndWeight = FontStyle.Bold;
                icon.style.color = Color.white;
                icon.style.unityTextAlign = TextAnchor.MiddleCenter;
                handle.Add(icon);

                _handlesContainer.Add(handle);
                _splitHandleElements.Add(handle);
            }
        }

        #endregion

        #region Mouse Interaction and Context Menu

        private void OnAddSplitClicked()
        {
            if (_targetAsset == null) return;
            float targetTime = _clipLength * 0.5f;
            OnSplitAdded?.Invoke(targetTime);
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            Vector2 localPos = evt.localMousePosition;
            float pps = PixelsPerSecond;
            float rawTime = Mathf.Clamp(localPos.x / pps, 0f, _clipLength);
            float frameInterval = 1f / Mathf.Max(1f, _frameRate);
            float clickTime = Mathf.Clamp(Mathf.Round(rawTime / frameInterval) * frameInterval, 0f, _clipLength);

            if (evt.button == 0) // Left click to select or drag
            {
                int hitIndex = GetHitSplitHandleIndex(localPos.x);
                if (hitIndex >= 0)
                {
                    _draggingSplitIndex = hitIndex;
                    _selectedSplitIndex = hitIndex;
                    _contentElement.CaptureMouse();
                    UpdateHandles();
                    evt.StopPropagation();
                    return;
                }

                // Double-click empty space to add a split point
                if (evt.clickCount == 2)
                {
                    OnSplitAdded?.Invoke(clickTime);
                    evt.StopPropagation();
                }
            }
            else if (evt.button == 1) // Right-click menu
            {
                int hitIndex = GetHitSplitHandleIndex(localPos.x);
                var menu = new GenericMenu();
                if (hitIndex >= 0)
                {
                    int capturedIdx = hitIndex;
                    menu.AddItem(new GUIContent("Delete Section Split"), false, () =>
                    {
                        OnSplitRemoved?.Invoke(capturedIdx);
                    });
                }
                else
                {
                    menu.AddItem(new GUIContent("Add Section Split"), false, () =>
                    {
                        OnSplitAdded?.Invoke(clickTime);
                    });
                }
                menu.ShowAsContext();
                evt.StopPropagation();
            }
        }

        private void OnMouseMove(MouseMoveEvent evt)
        {
            if (_draggingSplitIndex < 0 || _draggingSplitIndex >= _liveSplitTimes.Count)
            {
                return;
            }

            Vector2 localPos = _contentElement.WorldToLocal(evt.mousePosition);
            float pps = PixelsPerSecond;
            float rawTime = localPos.x / pps;

            // Snap to the nearest frame
            float frameInterval = 1f / _frameRate;
            float snappedTime = Mathf.Round(rawTime / frameInterval) * frameInterval;

            // Limit split range: the last split point can extend freely into the blank area at the canvas edge
            float minTime = _draggingSplitIndex == 0 ? frameInterval : _liveSplitTimes[_draggingSplitIndex - 1] + frameInterval;
            float maxTime = _draggingSplitIndex == _liveSplitTimes.Count - 1
                ? Mathf.Max(minTime + frameInterval, _clipLength - frameInterval)
                : _liveSplitTimes[_draggingSplitIndex + 1] - frameInterval;

            snappedTime = Mathf.Clamp(snappedTime, minTime, maxTime);

            _liveSplitTimes[_draggingSplitIndex] = snappedTime;

            if (_draggingSplitIndex < _splitHandleElements.Count)
            {
                _splitHandleElements[_draggingSplitIndex].style.left = snappedTime * pps;
            }

            _contentElement.MarkDirtyRepaint();
            OnSplitMovedLive?.Invoke(_draggingSplitIndex, snappedTime);
            evt.StopPropagation();
        }

        private void OnMouseUp(MouseUpEvent evt)
        {
            if (_draggingSplitIndex >= 0 && evt.button == 0)
            {
                int finishedIndex = _draggingSplitIndex;
                float finishedTime = _liveSplitTimes[finishedIndex];
                _draggingSplitIndex = -1;
                _contentElement.ReleaseMouse();

                OnSplitMoved?.Invoke(finishedIndex, finishedTime);
                UpdateHandles();
                _contentElement.MarkDirtyRepaint();
                evt.StopPropagation();
            }
        }

        private int GetHitSplitHandleIndex(float localX)
        {
            float pps = PixelsPerSecond;
            float hitRadius = 8f;

            for (int i = 0; i < _liveSplitTimes.Count; i++)
            {
                float handleX = _liveSplitTimes[i] * pps;
                if (Mathf.Abs(localX - handleX) <= hitRadius)
                {
                    return i;
                }
            }
            return -1;
        }

        #endregion
    }
}
