using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Cwcbb.Tools.CwcMontage;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// Dedicated timeline ruler controller (MontageTimelineRuler).
    /// Handles time tick marks, adaptive frame-number label layout, and playhead scrubbing and dragging,
    /// while section splitting is decoupled into a dedicated system section track.
    /// </summary>
    public class MontageTimelineRuler : VisualElement
    {
        #region Private Constants

        public const float TOTAL_HEADER_HEIGHT = 24f;
        private const float BASE_PIXELS_PER_SECOND = 200f;
        private static readonly float[] s_tickSecondCandidates = new float[]
        {
            0.5f, 1f, 2f, 5f, 10f, 20f, 30f, 60f, 120f, 300f, 600f, 1200f, 1800f, 3600f
        };

        #endregion

        #region Private Fields

        private MontageSequenceAsset _targetAsset;
        private float _clipLength = 1f;
        private float _frameRate = 30f;
        private float _currentTime = 0f;
        private float _zoomLevel = 1.0f;
        private float _contentDuration = 0f;

        private VisualElement _contentContainer;
        private VisualElement _tickLabelsContainer;
        private VisualElement _playheadVisual;
        private VisualElement _playheadCap;

        private readonly List<Label> _tickLabels = new();
        private bool _isDraggingPlayhead;

        #endregion

        #region Public Events

        /// <summary>
        /// Triggered when the timeline playhead is dragged or clicked to jump frames (parameter is the current time in seconds).
        /// </summary>
        public event Action<float> OnTimeScrubbed;

        /// <summary>
        /// Triggered when the user middle-click pans on the ruler (parameter is deltaX in pixels).
        /// </summary>
        public event Action<float> OnPanDelta;

        /// <summary>
        /// Triggered when the user zooms on the ruler with the mouse wheel (parameter is zoomDelta).
        /// </summary>
        public event Action<float> OnZoomDelta;

        #endregion

        #region Public Properties

        /// <summary>
        /// Current playback time (seconds).
        /// </summary>
        public float CurrentTime => _currentTime;

        /// <summary>
        /// Total animation duration (seconds).
        /// </summary>
        public float ClipLength => _clipLength;

        /// <summary>
        /// Animation base frame rate.
        /// </summary>
        public float FrameRate => _frameRate;

        /// <summary>
        /// Current timeline zoom multiplier.
        /// </summary>
        public float ZoomLevel => _zoomLevel;

        /// <summary>
        /// Actual pixels per second at the current zoom.
        /// </summary>
        public float PixelsPerSecond => BASE_PIXELS_PER_SECOND * _zoomLevel;

        /// <summary>
        /// Total physical pixel width of the timeline at the current zoom.
        /// </summary>
        public float ContentPixelWidth => Mathf.Max(100f, _clipLength * PixelsPerSecond);

        #endregion

        #region Constructor

        public MontageTimelineRuler()
        {
            AddToClassList("montage-ruler-area-container");
            style.height = TOTAL_HEADER_HEIGHT;

            // Internal flexible content container
            _contentContainer = new VisualElement();
            _contentContainer.AddToClassList("montage-ruler-content");
            _contentContainer.generateVisualContent += OnGenerateVisualContent;
            Add(_contentContainer);

            // Tick label container
            _tickLabelsContainer = new VisualElement();
            _tickLabelsContainer.style.position = Position.Absolute;
            _tickLabelsContainer.style.top = 0;
            _tickLabelsContainer.style.left = 0;
            _tickLabelsContainer.style.right = 0;
            _tickLabelsContainer.style.bottom = 0;
            _tickLabelsContainer.pickingMode = PickingMode.Ignore;
            _contentContainer.Add(_tickLabelsContainer);

            // Playhead indicator (tech blue)
            _playheadVisual = new VisualElement();
            _playheadVisual.AddToClassList("montage-playhead-line");

            _playheadCap = new VisualElement();
            _playheadCap.AddToClassList("montage-playhead-cap");
            _playheadVisual.Add(_playheadCap);

            _contentContainer.Add(_playheadVisual);

            // Mouse interaction event registration
            RegisterCallback<MouseDownEvent>(OnMouseDown);
            RegisterCallback<MouseMoveEvent>(OnMouseMove);
            RegisterCallback<MouseUpEvent>(OnMouseUp);
            RegisterCallback<MouseLeaveEvent>(OnMouseLeave);
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Bind and set the timeline data source.
        /// </summary>
        public void SetTimelineData(MontageSequenceAsset asset, float currentTime, float clipLength, float frameRate, float zoomLevel)
        {
            _targetAsset = asset;
            _currentTime = Mathf.Clamp(currentTime, 0f, Mathf.Max(0.001f, clipLength));
            _clipLength = Mathf.Max(0.001f, clipLength);
            _frameRate = Mathf.Max(1f, frameRate);
            _zoomLevel = Mathf.Clamp(zoomLevel, 0.005f, 20f);

            UpdateContentSize();
            UpdatePlayheadPosition();
            _contentContainer.MarkDirtyRepaint();
        }

        /// <summary>
        /// Set the authoritative logical content duration (used to draw the end marker and boundary line).
        /// </summary>
        public void SetContentDuration(float duration)
        {
            _contentDuration = Mathf.Max(0f, duration);
            _contentContainer?.MarkDirtyRepaint();
        }

        /// <summary>
        /// Update only the current time and refresh the playhead position.
        /// </summary>
        public void SetTime(float currentTime)
        {
            _currentTime = Mathf.Clamp(currentTime, 0f, _clipLength);
            UpdatePlayheadPosition();
        }

        /// <summary>
        /// Set the current zoom multiplier.
        /// </summary>
        public void SetZoom(float zoomLevel)
        {
            _zoomLevel = Mathf.Clamp(zoomLevel, 0.005f, 20f);
            UpdateContentSize();
            UpdatePlayheadPosition();
            _contentContainer.MarkDirtyRepaint();
        }

        /// <summary>
        /// Synchronize the horizontal scroll offset of the track area.
        /// </summary>
        public void SetHorizontalScrollOffset(float offset)
        {
            _contentContainer.style.left = -offset;
        }

        #endregion

        #region Private Rendering Logic (Vector Graphics Zero-GC Drawing)

        /// <summary>
        /// Calculate major and medium tick frame intervals using smart logarithmic alignment based on the current frame pixel width (covering scales from 1 frame to 1 hour, with major ticks kept at a comfortable 70~140px spacing).
        /// </summary>
        public static (int majorInterval, int mediumInterval) CalculateTickIntervals(float frameWidth, float frameRate = 30f)
        {
            frameRate = Mathf.Max(1f, frameRate);

            // 1. Micro sub-second tick candidates (1, 2, 5, 10 frames)
            if (frameWidth * 1f >= 75f) return (1, 1);
            if (frameWidth * 2f >= 75f) return (2, 1);
            if (frameWidth * 5f >= 75f) return (5, 1);
            if (frameWidth * 10f >= 75f) return (10, 5);

            // 2. Macro logarithmic second/minute/hour tick candidates
            float pps = frameRate * frameWidth;
            int chosenMajor = Mathf.RoundToInt(3600f * frameRate);
            for (int i = 0; i < s_tickSecondCandidates.Length; i++)
            {
                float px = s_tickSecondCandidates[i] * pps;
                if (px >= 75f)
                {
                    chosenMajor = Mathf.RoundToInt(s_tickSecondCandidates[i] * frameRate);
                    break;
                }
            }

            // 3. Adaptively subdivide medium ticks by order of magnitude
            int chosenMedium;
            if (chosenMajor <= 2)
            {
                chosenMedium = 1;
            }
            else if (chosenMajor % 5 == 0)
            {
                chosenMedium = chosenMajor / 5;
            }
            else if (chosenMajor % 2 == 0)
            {
                chosenMedium = chosenMajor / 2;
            }
            else
            {
                chosenMedium = Mathf.Max(1, chosenMajor / 2);
            }

            return (chosenMajor, chosenMedium);
        }

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            float totalWidth = ContentPixelWidth;
            float totalHeight = TOTAL_HEADER_HEIGHT;

            if (totalWidth <= 0 || totalHeight <= 0 || _clipLength <= 0)
            {
                return;
            }

            var painter = mgc.painter2D;
            float pps = PixelsPerSecond;

            // Draw timeline tick marks (Ruler Ticks)
            int totalFrames = Mathf.Max(1, Mathf.RoundToInt(_clipLength * _frameRate));
            float frameWidth = pps / _frameRate;
            var (majorInterval, mediumInterval) = CalculateTickIntervals(frameWidth, _frameRate);

            Color majorTickColor = new Color(0.85f, 0.85f, 0.85f, 0.95f);
            Color mediumTickColor = new Color(0.55f, 0.55f, 0.55f, 0.75f);
            Color minorTickColor = new Color(0.35f, 0.35f, 0.35f, 0.45f);

            bool drawMinor = frameWidth >= 4f;
            bool drawMedium = (mediumInterval * frameWidth) >= 6f;
            int step = drawMinor ? 1 : (drawMedium ? mediumInterval : majorInterval);

            for (int f = 0; f <= totalFrames; f += step)
            {
                float x = f * frameWidth;
                bool isMajor = (f % majorInterval == 0);
                bool isMedium = (!isMajor && mediumInterval > 0 && f % mediumInterval == 0);

                painter.strokeColor = isMajor ? majorTickColor : (isMedium ? mediumTickColor : minorTickColor);
                painter.lineWidth = isMajor ? 1.2f : 1f;

                float tickTop;
                if (isMajor)
                {
                    tickTop = 2f;
                }
                else if (isMedium)
                {
                    tickTop = 10f;
                }
                else
                {
                    tickTop = 16f;
                }

                painter.BeginPath();
                painter.MoveTo(new Vector2(x, totalHeight));
                painter.LineTo(new Vector2(x, tickTop));
                painter.Stroke();
            }

            // Draw logical content end boundary marker (Content End Boundary Marker)
            if (_contentDuration > 0.001f && _contentDuration <= _clipLength)
            {
                float endX = _contentDuration * pps;
                painter.strokeColor = new Color(0.35f, 0.65f, 1.0f, 0.9f);
                painter.lineWidth = 1.5f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(endX, 0));
                painter.LineTo(new Vector2(endX, totalHeight));
                painter.Stroke();

                // Top downward triangle marker (End Cap Flag)
                painter.fillColor = new Color(0.35f, 0.65f, 1.0f, 0.9f);
                painter.BeginPath();
                painter.MoveTo(new Vector2(endX - 4f, 0));
                painter.LineTo(new Vector2(endX + 4f, 0));
                painter.LineTo(new Vector2(endX, 6f));
                painter.ClosePath();
                painter.Fill();
            }

            // Bottom horizontal divider line
            painter.strokeColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0, totalHeight));
            painter.LineTo(new Vector2(totalWidth, totalHeight));
            painter.Stroke();
        }

        private void UpdateContentSize()
        {
            float width = ContentPixelWidth;
            _contentContainer.style.width = width;

            int totalFrames = Mathf.Max(1, Mathf.RoundToInt(_clipLength * _frameRate));
            float frameWidth = PixelsPerSecond / _frameRate;
            var (majorInterval, _) = CalculateTickIntervals(frameWidth, _frameRate);

            // Reuse Labels from the object pool to avoid GC allocations and redundant overhead on every zoom/redraw
            int labelIdx = 0;
            for (int f = 0; f <= totalFrames; f += majorInterval)
            {
                float x = f * frameWidth;
                Label lbl;
                if (labelIdx < _tickLabels.Count)
                {
                    lbl = _tickLabels[labelIdx];
                    lbl.style.display = DisplayStyle.Flex;
                }
                else
                {
                    lbl = new Label();
                    lbl.AddToClassList("montage-ruler-tick-label");
                    lbl.style.top = 1f;
                    lbl.pickingMode = PickingMode.Ignore;
                    _tickLabelsContainer.Add(lbl);
                    _tickLabels.Add(lbl);
                }

                lbl.text = f.ToString();
                lbl.style.left = x + 3f;
                labelIdx++;
            }

            for (int i = labelIdx; i < _tickLabels.Count; i++)
            {
                _tickLabels[i].style.display = DisplayStyle.None;
            }
        }

        private void UpdatePlayheadPosition()
        {
            if (_playheadVisual == null) return;
            float xPos = _currentTime * PixelsPerSecond;
            _playheadVisual.style.left = xPos;
        }

        #endregion

        #region Mouse Interaction and Event Handling

        private void OnMouseDown(MouseDownEvent evt)
        {
            Vector2 localPos = _contentContainer.WorldToLocal(evt.mousePosition);
            float pps = PixelsPerSecond;
            float rawTime = Mathf.Clamp(localPos.x / pps, 0f, _clipLength);
            float frameInterval = 1f / Mathf.Max(1f, _frameRate);
            float clickTime = Mathf.Clamp(Mathf.Round(rawTime / frameInterval) * frameInterval, 0f, _clipLength);

            if (evt.button == 0) // Left-click to jump frames and start dragging the playhead
            {
                _isDraggingPlayhead = true;
                this.CaptureMouse();
                _currentTime = clickTime;
                UpdatePlayheadPosition();
                OnTimeScrubbed?.Invoke(_currentTime);
                evt.StopPropagation();
            }
            else if (evt.button == 2) // Middle-button pan
            {
                this.CaptureMouse();
                evt.StopPropagation();
            }
        }

        private void OnMouseMove(MouseMoveEvent evt)
        {
            if (_isDraggingPlayhead && evt.button == 0)
            {
                Vector2 localPos = _contentContainer.WorldToLocal(evt.mousePosition);
                float pps = PixelsPerSecond;
                float rawTime = Mathf.Clamp(localPos.x / pps, 0f, _clipLength);
                float frameInterval = 1f / Mathf.Max(1f, _frameRate);
                float newTime = Mathf.Clamp(Mathf.Round(rawTime / frameInterval) * frameInterval, 0f, _clipLength);

                _currentTime = newTime;
                UpdatePlayheadPosition();
                OnTimeScrubbed?.Invoke(_currentTime);
                evt.StopPropagation();
            }
            else if (evt.button == 2 && this.HasMouseCapture())
            {
                OnPanDelta?.Invoke(evt.mouseDelta.x);
                evt.StopPropagation();
            }
        }

        private void OnMouseUp(MouseUpEvent evt)
        {
            if (_isDraggingPlayhead && evt.button == 0)
            {
                _isDraggingPlayhead = false;
                this.ReleaseMouse();
                evt.StopPropagation();
            }
            else if (evt.button == 2 && this.HasMouseCapture())
            {
                this.ReleaseMouse();
                evt.StopPropagation();
            }
        }

        private void OnMouseLeave(MouseLeaveEvent evt)
        {
            if (_isDraggingPlayhead && !this.HasMouseCapture())
            {
                _isDraggingPlayhead = false;
            }
        }

        private void OnWheel(WheelEvent evt)
        {
            float delta = -evt.delta.y * 0.15f;
            OnZoomDelta?.Invoke(delta);
            evt.StopPropagation();
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            _contentContainer.style.width = ContentPixelWidth;
            _contentContainer.MarkDirtyRepaint();
        }

        #endregion
    }
}
