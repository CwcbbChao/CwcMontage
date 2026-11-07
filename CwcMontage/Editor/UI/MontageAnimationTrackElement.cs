using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Cwcbb.Tools.CwcMontage;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// Single core animation track element (MontageAnimationTrackElement).
    /// Modeled on the Unity Timeline industrial conventions, it provides multiple animation segments per track,
    /// independent playback rate (PlayRate / Time Stretch), edge trimming (Trim), overlap-to-crossfade, and magnetic snapping.
    /// </summary>
    public class MontageAnimationTrackElement
    {
        #region Public Constants

        public const float TRACK_HEIGHT = 32f;
        private const float BASE_PIXELS_PER_SECOND = 200f;
        private const float SNAP_PIXEL_THRESHOLD = 8f;

        #endregion

        #region Private Fields

        private MontageSequenceAsset _targetAsset;
        private float _clipLength = 1f;
        private float _frameRate = 30f;
        private float _zoomLevel = 1.0f;
        private float _contentDuration = 0f;

        private VisualElement _headerElement;
        private VisualElement _contentElement;
        private VisualElement _segmentsContainer;
        private VisualElement _crossfadeContainer;

        private MontageAnimationSegment _selectedSegment;
        private int _selectedSegmentIndex = -1;
        private bool _isDraggingSegment;
        private int _draggingIndex = -1;
        private Vector2 _dragStartMousePos;
        private float _dragStartSegmentTime;
        private float _dragStartDuration;
        private float _dragStartOffset;
        private float _dragStartPlayRate;
        private int _dragMode; // 0: Move, 1: StretchRight, 2: StretchLeft
        private MontageAnimationSegment _activePickerTargetSegment;
        private float _pendingAddClipStartTime = -1f;
        private bool _isWaitingForPicker;
        private VisualElement _draggingBlockElement;
        private bool _hasDragMoved;
        private FloatField _weightField;
        private bool _isDropHighlighted;

        private MontageLayerChannel _channel;
        private List<MontageAnimationSegment> TrackSegments => _targetAsset?.GetSegments(_channel);

        #endregion

        #region Public Properties

        public VisualElement HeaderElement => _headerElement;
        public VisualElement ContentElement => _contentElement;
        public MontageLayerChannel Channel => _channel;
        public float PixelsPerSecond => BASE_PIXELS_PER_SECOND * _zoomLevel;
        public float ContentPixelWidth => Mathf.Max(100f, _clipLength * PixelsPerSecond);
        public int SelectedSegmentIndex => _selectedSegmentIndex;
        public MontageAnimationSegment SelectedSegment => _selectedSegment;

        #endregion

        #region Public Events

        /// <summary>
        /// Fired when an animation segment is selected on the track (passes the selected segment and its index).
        /// </summary>
        public event Action<MontageAnimationSegment, int> OnSegmentSelected;

        /// <summary>
        /// Fired when segment data is modified (move, zoom, add/remove, play rate change).
        /// </summary>
        public event Action OnDataModified;

        /// <summary>
        /// Fired when a double-click or click requests scrubbing the timeline.
        /// </summary>
        public event Action<float> OnRequestScrubTime;

        /// <summary>
        /// Requests finding the target animation track from a mouse screen position.
        /// </summary>
        public Func<Vector2, MontageAnimationTrackElement> RequestTargetTrack;

        /// <summary>
        /// Requests moving an animation segment across tracks.
        /// Parameters: (source track, target track, segment, desired start time)
        /// </summary>
        public Action<MontageAnimationTrackElement, MontageAnimationTrackElement, MontageAnimationSegment, float> RequestCrossTrackMove;

        /// <summary>
        /// Fired when the drag-hover state changes (used to notify other tracks to clear or show highlights).
        /// </summary>
        public Action<MontageAnimationTrackElement, bool> OnTrackDropHighlightChanged;

        #endregion

        #region Constructor

        public MontageAnimationTrackElement(
            MontageSequenceAsset targetAsset,
            float clipLength,
            float frameRate,
            float zoomLevel,
            MontageLayerChannel channel = MontageLayerChannel.FullBody)
        {
            _targetAsset = targetAsset;
            _channel = channel;
            _clipLength = Mathf.Max(0.001f, clipLength);
            _frameRate = Mathf.Max(1f, frameRate);
            _zoomLevel = Mathf.Clamp(zoomLevel, 0.005f, 20f);

            BuildHeader();
            BuildContent();
            RebuildSegments();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Externally synchronizes the asset data and total duration.
        /// </summary>
        public void SetTargetAsset(MontageSequenceAsset targetAsset, float clipLength, float frameRate, float contentDuration = -1f)
        {
            _targetAsset = targetAsset;
            _clipLength = Mathf.Max(0.001f, clipLength);
            _frameRate = Mathf.Max(1f, frameRate);
            _contentDuration = contentDuration >= 0f ? contentDuration : (_targetAsset != null ? _targetAsset.TotalDuration : _clipLength);

            UpdateLayerWeightVisual();
            UpdateContentWidth();
            RebuildSegments();
            _contentElement?.MarkDirtyRepaint();
        }

        /// <summary>
        /// Refreshes the layer blend weight value currently displayed by this animation track.
        /// </summary>
        public void UpdateLayerWeightVisual()
        {
            if (_weightField != null && _targetAsset != null)
            {
                float w = _targetAsset.GetChannelWeight(_channel);
                _weightField.SetValueWithoutNotify((float)Math.Round(w, 2));
            }
        }

        /// <summary>
        /// Sets the current view zoom level and refreshes the geometry layout.
        /// </summary>
        public void SetZoom(float newZoom)
        {
            _zoomLevel = Mathf.Clamp(newZoom, 0.005f, 20f);
            UpdateContentWidth();
            RebuildSegments();
        }

        /// <summary>
        /// Selects the specified animation segment (by object reference, so overlaps or reordering never mix up the selection).
        /// </summary>
        /// <param name="segment">Target animation segment instance</param>
        public void SelectSegment(MontageAnimationSegment segment)
        {
            _selectedSegment = segment;
            var segs = TrackSegments;
            _selectedSegmentIndex = (segs != null && segment != null)
                ? segs.IndexOf(segment)
                : -1;

            RefreshSelectedSegmentHighlight();

            if (segment != null && _selectedSegmentIndex >= 0)
            {
                OnSegmentSelected?.Invoke(segment, _selectedSegmentIndex);
            }
        }

        /// <summary>
        /// Externally selects the segment at the specified index (lightweight style refresh only, avoiding DOM rebuilds).
        /// </summary>
        /// <param name="index">Index of the target segment in the asset</param>
        public void SelectSegment(int index)
        {
            var segs = TrackSegments;
            if (segs != null && index >= 0 && index < segs.Count)
            {
                SelectSegment(segs[index]);
            }
            else
            {
                ClearSelection();
            }
        }

        /// <summary>
        /// Clears the segment selection.
        /// </summary>
        public void ClearSelection()
        {
            if (_selectedSegment == null && _selectedSegmentIndex == -1) return;
            _selectedSegment = null;
            _selectedSegmentIndex = -1;
            RefreshSelectedSegmentHighlight();
        }

        /// <summary>
        /// Sets whether the current track shows the drag-hover drop highlight.
        /// </summary>
        public void SetDropHighlight(bool highlight)
        {
            if (_isDropHighlighted == highlight) return;
            _isDropHighlighted = highlight;

            if (_contentElement != null)
            {
                if (highlight)
                {
                    _contentElement.style.backgroundColor = new Color(0.18f, 0.35f, 0.60f, 0.45f);
                    _contentElement.style.borderTopColor = new Color(0.35f, 0.65f, 1.0f, 0.9f);
                    _contentElement.style.borderBottomColor = new Color(0.35f, 0.65f, 1.0f, 0.9f);
                    _contentElement.style.borderTopWidth = 1.5f;
                    _contentElement.style.borderBottomWidth = 1.5f;
                }
                else
                {
                    _contentElement.style.backgroundColor = StyleKeyword.Null;
                    _contentElement.style.borderTopColor = StyleKeyword.Null;
                    _contentElement.style.borderBottomColor = StyleKeyword.Null;
                    _contentElement.style.borderTopWidth = StyleKeyword.Null;
                    _contentElement.style.borderBottomWidth = StyleKeyword.Null;
                }
            }
        }

        /// <summary>
        /// Copies the currently selected animation segment to the clipboard.
        /// </summary>
        public void CopySelectedSegment()
        {
            if (_selectedSegment != null)
            {
                MontageClipboard.CopySegment(_selectedSegment);
            }
        }

        /// <summary>
        /// Pastes the animation segment from the clipboard at the specified timestamp.
        /// </summary>
        public void PasteCopiedSegmentAt(float clickTime)
        {
            if (!MontageClipboard.HasCopiedSegment || _targetAsset == null) return;
            var segs = TrackSegments;
            if (segs == null) return;

            Undo.RecordObject(_targetAsset, "Paste Animation Clip");
            var clone = MontageClipboard.GetClonedSegment();
            if (clone == null) return;

            float targetTime = Mathf.Max(0f, clickTime);
            clone.StartTime = SnapToFrame(targetTime);
            segs.Add(clone);

            _targetAsset.SortChannelSegments(_channel);
            _targetAsset.EnsureSegmentsValid();

            RebuildSegments();
            SelectSegment(clone);
            EditorUtility.SetDirty(_targetAsset);
            OnDataModified?.Invoke();
        }

        /// <summary>
        /// Duplicates the currently selected animation segment.
        /// </summary>
        public void DuplicateSelectedSegment()
        {
            if (_selectedSegment != null)
            {
                int idx = TrackSegments != null ? TrackSegments.IndexOf(_selectedSegment) : -1;
                if (idx >= 0)
                {
                    DuplicateSegmentAt(idx);
                }
            }
        }

        /// <summary>
        /// Deletes the currently selected animation segment.
        /// </summary>
        public void DeleteSelectedSegment()
        {
            if (_selectedSegment != null)
            {
                DeleteSegment(_selectedSegment);
            }
        }

        #endregion

        #region UI Layout & Construction

        private void BuildHeader()
        {
            _headerElement = new VisualElement();
            _headerElement.AddToClassList("montage-track-header");
            _headerElement.style.height = TRACK_HEIGHT;
            _headerElement.style.minHeight = TRACK_HEIGHT;
            _headerElement.style.maxHeight = TRACK_HEIGHT;
            _headerElement.style.flexShrink = 0;
            _headerElement.style.flexGrow = 0;

            var titleBox = new VisualElement();
            titleBox.AddToClassList("montage-track-title-box");

            string title = _channel switch
            {
                MontageLayerChannel.Additive => "Additive (Layer 3)",
                MontageLayerChannel.FullBody => "FullBody (Layer 2)",
                MontageLayerChannel.UpperBody => "UpperBody (Layer 1)",
                _ => "Animation"
            };

            string tooltip = _channel switch
            {
                MontageLayerChannel.Additive => "Additive animation track (Layer 3, secondary overlays like hit reactions & tremors).",
                MontageLayerChannel.FullBody => "Full body animation track (Layer 2, dominant actions like rolls, attacks & ultimates).",
                MontageLayerChannel.UpperBody => "Upper body animation track (Layer 1, masked actions for locomotion & moving attacks).",
                _ => "Core animation track."
            };

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("montage-track-title");
            titleLabel.tooltip = tooltip;
            titleBox.Add(titleLabel);

            _headerElement.Add(titleBox);

            var buttonsGroup = new VisualElement();
            buttonsGroup.style.flexDirection = FlexDirection.Row;
            buttonsGroup.style.alignItems = Align.Center;

            // Animation layer blend weight control (Weight Field: [W: 1.00])
            var weightBox = new VisualElement();
            weightBox.style.flexDirection = FlexDirection.Row;
            weightBox.style.alignItems = Align.Center;
            weightBox.style.marginRight = 4;
            weightBox.tooltip = "Layer Blend Weight [0.0 - 1.0]. Controls overall blend influence of this animation track.";

            var weightLbl = new Label("W:");
            weightLbl.style.fontSize = 10;
            weightLbl.style.color = new Color(0.62f, 0.62f, 0.62f);
            weightLbl.style.marginRight = 2;
            weightBox.Add(weightLbl);

            float initialWeight = _targetAsset != null ? _targetAsset.GetChannelWeight(_channel) : 1.0f;
            _weightField = new FloatField { value = (float)Math.Round(initialWeight, 2) };
            _weightField.AddToClassList("montage-track-weight-field");
            _weightField.RegisterValueChangedCallback(evt =>
            {
                if (_targetAsset == null) return;
                float clamped = Mathf.Clamp01(evt.newValue);
                if (!Mathf.Approximately(clamped, evt.newValue))
                {
                    _weightField.SetValueWithoutNotify((float)Math.Round(clamped, 2));
                }
                Undo.RecordObject(_targetAsset, "Change Layer Weight");
                _targetAsset.SetChannelWeight(_channel, clamped);
                EditorUtility.SetDirty(_targetAsset);
                OnDataModified?.Invoke();
            });
            weightBox.Add(_weightField);
            buttonsGroup.Add(weightBox);

            // Add button consistent with regular tracks
            var addClipButton = new Button(ShowAddClipPicker) { text = "+" };
            addClipButton.AddToClassList("montage-toolbar-btn");
            addClipButton.tooltip = "Add Animation Clip to this track";
            buttonsGroup.Add(addClipButton);

            _headerElement.Add(buttonsGroup);
        }

        private void BuildContent()
        {
            _contentElement = new VisualElement();
            _contentElement.AddToClassList("montage-anim-track-content");
            _contentElement.style.height = TRACK_HEIGHT;
            _contentElement.style.minHeight = TRACK_HEIGHT;
            _contentElement.style.maxHeight = TRACK_HEIGHT;
            _contentElement.style.position = Position.Relative;
            _contentElement.pickingMode = PickingMode.Position;

            _segmentsContainer = new VisualElement();
            _segmentsContainer.style.position = Position.Absolute;
            _segmentsContainer.style.left = 0;
            _segmentsContainer.style.top = 0;
            _segmentsContainer.style.bottom = 0;
            _segmentsContainer.style.right = 0;
            _segmentsContainer.pickingMode = PickingMode.Position;
            _contentElement.Add(_segmentsContainer);

            // Crossfade layer (placed above the blocks as a translucent diagonal overlay)
            _crossfadeContainer = new VisualElement();
            _crossfadeContainer.style.position = Position.Absolute;
            _crossfadeContainer.style.left = 0;
            _crossfadeContainer.style.top = 0;
            _crossfadeContainer.style.bottom = 0;
            _crossfadeContainer.style.right = 0;
            _crossfadeContainer.pickingMode = PickingMode.Ignore;
            _contentElement.Add(_crossfadeContainer);

            // Register the content area context menu, drag-and-drop and shortcuts
            _contentElement.focusable = true;
            _contentElement.RegisterCallback<KeyDownEvent>(OnContentKeyDown);
            _contentElement.RegisterCallback<MouseDownEvent>(OnContentMouseDown);
            _contentElement.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            _contentElement.RegisterCallback<DragPerformEvent>(OnDragPerform);
            _contentElement.generateVisualContent += OnGenerateContentVisual;

            UpdateContentWidth();
        }

        private void UpdateContentWidth()
        {
            if (_contentElement != null)
            {
                _contentElement.style.width = ContentPixelWidth;
            }
        }

        private void OnGenerateContentVisual(MeshGenerationContext mgc)
        {
            float width = ContentPixelWidth;
            float height = TRACK_HEIGHT;

            if (width <= 0 || _targetAsset == null)
            {
                return;
            }

            var painter = mgc.painter2D;
            float pps = PixelsPerSecond;

            // 1. Draw vertical timeline grid lines (Timeline Vertical Grid Lines)
            int totalFrames = Mathf.Max(1, Mathf.RoundToInt(_clipLength * _frameRate));
            float frameWidth = pps / _frameRate;
            var (majorInterval, mediumInterval) = MontageTimelineRuler.CalculateTickIntervals(frameWidth, _frameRate);

            Color majorGridColor = new Color(1f, 1f, 1f, 0.09f);
            Color minorGridColor = new Color(1f, 1f, 1f, 0.04f);

            bool drawMinor = frameWidth >= 5f;
            bool drawMedium = (mediumInterval * frameWidth) >= 6f;
            int step = drawMinor ? 1 : (drawMedium ? mediumInterval : majorInterval);

            for (int f = 0; f <= totalFrames; f += step)
            {
                bool isMajor = (f % majorInterval == 0);
                bool isMedium = (!isMajor && mediumInterval > 0 && f % mediumInterval == 0);

                if (!isMajor && !isMedium && !drawMinor) continue;

                float x = f * frameWidth;
                painter.strokeColor = isMajor ? majorGridColor : minorGridColor;
                painter.lineWidth = 1f;

                painter.BeginPath();
                painter.MoveTo(new Vector2(x, 0));
                painter.LineTo(new Vector2(x, height));
                painter.Stroke();
            }

            // 2. Draw divider lines across physical section boundaries (Physical Section Boundaries)
            var splits = _targetAsset.SplitTimestamps;
            if (splits != null && splits.Count > 0)
            {
                painter.strokeColor = new Color(0.88f, 0.52f, 0.26f, 0.65f);
                painter.lineWidth = 1.2f;

                for (int i = 0; i < splits.Count; i++)
                {
                    float x = splits[i] * pps;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(x, 0));
                    painter.LineTo(new Vector2(x, height));
                    painter.Stroke();
                }
            }

            // 3. Draw the content end boundary line (Content End Boundary Line)
            float contentEnd = _contentDuration > 0.001f ? _contentDuration : (_targetAsset != null ? _targetAsset.TotalDuration : 0f);
            if (contentEnd > 0.001f && contentEnd <= _clipLength)
            {
                float xEnd = contentEnd * pps;
                painter.strokeColor = new Color(0.35f, 0.65f, 1.0f, 0.65f);
                painter.lineWidth = 1.5f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(xEnd, 0));
                painter.LineTo(new Vector2(xEnd, height));
                painter.Stroke();
            }

            // 4. Draw the bottom divider line
            painter.strokeColor = new Color(0f, 0f, 0f, 0.35f);
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0, height));
            painter.LineTo(new Vector2(width, height));
            painter.Stroke();
        }

        #endregion

        #region Segment Rebuild & Rendering (Segments & Crossfade Overlays)

        public void RebuildSegments()
        {
            _segmentsContainer?.Clear();
            _crossfadeContainer?.Clear();

            var segments = TrackSegments;
            if (segments == null || segments.Count == 0)
            {
                _selectedSegment = null;
                _selectedSegmentIndex = -1;
                return;
            }

            // If the previously selected segment is no longer in the asset, clear the selection; otherwise sync its latest index
            if (_selectedSegment != null)
            {
                _selectedSegmentIndex = segments.IndexOf(_selectedSegment);
                if (_selectedSegmentIndex < 0)
                {
                    _selectedSegment = null;
                }
            }
            else if (_selectedSegmentIndex >= 0 && _selectedSegmentIndex < segments.Count)
            {
                _selectedSegment = segments[_selectedSegmentIndex];
            }

            float pps = PixelsPerSecond;

            // Build a segment list sorted by ascending StartTime to compute each segment's overlap ranges and exclusive space
            var sortedList = new List<MontageAnimationSegment>(segments);
            sortedList.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            // 1. Draw segment blocks
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                if (seg == null) continue;

                // Compute how long the current segment is overlapped by the previous and next segments (Overlap Duration)
                int sortedIdx = sortedList.IndexOf(seg);
                float leftOverlap = 0f;
                float rightOverlap = 0f;

                if (sortedIdx > 0)
                {
                    var prev = sortedList[sortedIdx - 1];
                    if (prev != null && prev.EndTime > seg.StartTime)
                    {
                        leftOverlap = Mathf.Max(0f, prev.EndTime - seg.StartTime);
                    }
                }

                if (sortedIdx >= 0 && sortedIdx < sortedList.Count - 1)
                {
                    var next = sortedList[sortedIdx + 1];
                    if (next != null && next.StartTime < seg.EndTime)
                    {
                        rightOverlap = Mathf.Max(0f, seg.EndTime - next.StartTime);
                    }
                }

                var block = CreateSegmentBlock(seg, i, pps, leftOverlap, rightOverlap);
                _segmentsContainer?.Add(block);
            }

            // 2. Draw adjacent overlap crossfade regions (Timeline Style Overlap-to-Crossfade)
            UpdateCrossfadeOverlays();
        }

        private VisualElement CreateSegmentBlock(MontageAnimationSegment seg, int index, float pps, float leftOverlap, float rightOverlap)
        {
            float left = seg.StartTime * pps;
            float width = Mathf.Max(12f, seg.Duration * pps);

            var block = new VisualElement();
            block.userData = seg;
            block.AddToClassList("montage-anim-segment-block");
            if (seg == _selectedSegment)
            {
                block.AddToClassList("montage-anim-segment-selected");
            }

            // Skeleton type consistency check: use the first valid segment in the asset as the baseline, and show a red warning border on mismatch
            bool isSkeletonMismatch = false;
            bool? anchorIsHuman = _targetAsset?.GetAnchorSkeletonIsHuman();
            if (anchorIsHuman.HasValue && seg.Clip != null && seg.Clip.isHumanMotion != anchorIsHuman.Value)
            {
                isSkeletonMismatch = true;
                block.AddToClassList("montage-anim-segment-mismatch");
            }

            block.style.left = left;
            block.style.width = width;
            block.style.height = TRACK_HEIGHT - 4f;
            block.style.top = 2f;

            // Self-exclusive range content container: skip the adjacent crossfade blend regions and lay out the name and badge only inside the segment's own non-overlapping range
            float selfLeftPx = leftOverlap * pps;
            float selfRightPx = rightOverlap * pps;
            float selfWidthPx = width - selfLeftPx - selfRightPx;

            if (selfWidthPx >= 12f)
            {
                var selfContent = new VisualElement();
                selfContent.AddToClassList("montage-anim-segment-self-content");
                selfContent.pickingMode = PickingMode.Ignore;
                selfContent.style.left = selfLeftPx;
                selfContent.style.right = selfRightPx;

                // Segment name label (ellipsis when it exceeds the self-exclusive range)
                var label = new Label(seg.SegmentName);
                label.AddToClassList("montage-anim-segment-title");
                string mismatchWarning = isSkeletonMismatch
                    ? $"\n[!] Skeleton type mismatch: this segment is {(seg.Clip.isHumanMotion ? "Humanoid" : "Generic")}, which conflicts with the anchor segment ({(anchorIsHuman.Value ? "Humanoid" : "Generic")})!"
                    : "";
                label.tooltip = $"{seg.SegmentName}\nStart: {seg.StartTime:F2}s (Frame {Mathf.RoundToInt(seg.StartTime * _frameRate)})\nDuration: {seg.Duration:F2}s ({Mathf.RoundToInt(seg.Duration * _frameRate)} frames)\nRate: {seg.PlayRate:F2}x{mismatchWarning}";
                selfContent.Add(label);

                // Badge: play rate indicator (highlighted when not 1.0x)
                if (Mathf.Abs(seg.PlayRate - 1.0f) > 0.01f)
                {
                    var rateBadge = new Label($"{seg.PlayRate:F2}x");
                    rateBadge.AddToClassList("montage-anim-segment-badge");
                    selfContent.Add(rateBadge);
                }

                block.Add(selfContent);
            }

            // Left/right edge handles: dragging either end stretches the playback rate (Time Stretch)
            var leftHandle = new VisualElement();
            leftHandle.AddToClassList("montage-anim-segment-handle");
            leftHandle.AddToClassList("montage-anim-segment-handle-left");
            leftHandle.tooltip = "Drag left edge to stretch duration and adjust PlayRate (adjusts start time)";
            leftHandle.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    SelectSegment(seg);
                    int curIdx = TrackSegments != null ? TrackSegments.IndexOf(seg) : index;
                    if (curIdx >= 0)
                    {
                        StartDrag(evt, curIdx, 2); // 2: Stretch Left
                    }
                    evt.StopPropagation();
                }
            });
            block.Add(leftHandle);

            var rightHandle = new VisualElement();
            rightHandle.AddToClassList("montage-anim-segment-handle");
            rightHandle.AddToClassList("montage-anim-segment-handle-right");
            rightHandle.tooltip = "Drag right edge to stretch duration and adjust PlayRate";
            rightHandle.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    SelectSegment(seg);
                    int curIdx = TrackSegments != null ? TrackSegments.IndexOf(seg) : index;
                    if (curIdx >= 0)
                    {
                        StartDrag(evt, curIdx, 1); // 1: Stretch Right
                    }
                    evt.StopPropagation();
                }
            });
            block.Add(rightHandle);

            // Body click and move dragging
            block.RegisterCallback<MouseDownEvent>(evt =>
            {
                SelectSegment(seg);
                int curIdx = TrackSegments != null ? TrackSegments.IndexOf(seg) : index;
                if (evt.button == 0)
                {
                    if (curIdx >= 0)
                    {
                        StartDrag(evt, curIdx, 0);
                    }
                    evt.StopPropagation();
                }
                else if (evt.button == 1)
                {
                    if (curIdx >= 0)
                    {
                        ShowSegmentContextMenu(curIdx, evt.mousePosition);
                    }
                    evt.StopPropagation();
                }
            });

            return block;
        }

        private void UpdateCrossfadeOverlays()
        {
            var segments = TrackSegments;
            if (_crossfadeContainer == null || segments == null) return;

            float pps = PixelsPerSecond;

            // 1. Compute the overlap ranges of all currently adjacent segment pairs
            var sortedList = new List<MontageAnimationSegment>(segments);
            sortedList.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            var activeOverlaps = new List<(float start, float duration, MontageAnimationSegment segA, MontageAnimationSegment segB, int idxA, int idxB)>();
            for (int i = 0; i < sortedList.Count - 1; i++)
            {
                var segA = sortedList[i];
                var segB = sortedList[i + 1];
                if (segA == null || segB == null) continue;

                if (segB.StartTime < segA.EndTime)
                {
                    float overlapStart = segB.StartTime;
                    float overlapEnd = Mathf.Min(segA.EndTime, segB.EndTime);
                    float overlapDuration = Mathf.Max(0.0001f, overlapEnd - overlapStart);
                    int indexA = segments.IndexOf(segA);
                    int indexB = segments.IndexOf(segB);
                    activeOverlaps.Add((overlapStart, overlapDuration, segA, segB, indexA, indexB));
                }
            }

            // 2. Reuse elements in _crossfadeContainer in place, or add/remove them as needed
            while (_crossfadeContainer.childCount > activeOverlaps.Count)
            {
                _crossfadeContainer.RemoveAt(_crossfadeContainer.childCount - 1);
            }

            for (int i = 0; i < activeOverlaps.Count; i++)
            {
                var item = activeOverlaps[i];
                if (i < _crossfadeContainer.childCount)
                {
                    var overlay = _crossfadeContainer[i];
                    ApplyCrossfadeOverlayData(overlay, item.start, item.duration, pps, item.segA, item.segB, item.idxA, item.idxB);
                }
                else
                {
                    var newOverlay = CreateCrossfadeOverlay(item.start, item.duration, pps, item.segA, item.segB, item.idxA, item.idxB);
                    _crossfadeContainer.Add(newOverlay);
                }
            }
        }

        private void ApplyCrossfadeOverlayData(VisualElement overlay, float startTime, float duration, float pps, MontageAnimationSegment segA, MontageAnimationSegment segB, int indexA, int indexB)
        {
            if (overlay?.userData is CrossfadeOverlayHolder holder)
            {
                holder.SegmentA = segA;
                holder.SegmentB = segB;
                holder.IndexA = indexA;
                holder.IndexB = indexB;

                float left = startTime * pps;
                float width = Mathf.Max(6f, duration * pps);

                overlay.style.left = left;
                overlay.style.width = width;

                float handleWidth = Mathf.Clamp(width * 0.35f, 2f, 8f);
                if (holder.LeftTrimHandle != null)
                {
                    holder.LeftTrimHandle.style.width = handleWidth;
                }
                if (holder.RightTrimHandle != null)
                {
                    holder.RightTrimHandle.style.width = handleWidth;
                }

                if (holder.DurationLabel != null)
                {
                    holder.DurationLabel.text = $"{duration:F2}s";
                    holder.DurationLabel.tooltip = $"Crossfade Blending: {duration:F2}s ({Mathf.RoundToInt(duration * _frameRate)} frames)";
                    holder.DurationLabel.style.display = (width > 28f) ? DisplayStyle.Flex : DisplayStyle.None;
                }

                overlay.MarkDirtyRepaint();
            }
        }

        private VisualElement CreateCrossfadeOverlay(float startTime, float duration, float pps, MontageAnimationSegment initialSegA, MontageAnimationSegment initialSegB, int indexA, int indexB)
        {
            if (TrackSegments == null) return new VisualElement();
            if (initialSegA == null || initialSegB == null) return new VisualElement();

            float left = startTime * pps;
            float width = Mathf.Max(6f, duration * pps);
            float height = TRACK_HEIGHT - 4f;

            var overlay = new VisualElement();
            overlay.AddToClassList("montage-anim-crossfade-overlay");
            overlay.pickingMode = PickingMode.Position; // Allow event capture to support diagonal picking and section-edge trimming
            overlay.style.left = left;
            overlay.style.width = width;
            overlay.style.height = height;
            overlay.style.top = 2f;
            overlay.style.backgroundColor = Color.clear;

            // Draw the classic Timeline diagonal split, the section boundary lines at both ends, and the selection highlight border
            overlay.generateVisualContent += mgc =>
            {
                var painter = mgc.painter2D;
                float w = overlay.contentRect.width > 0f ? overlay.contentRect.width : overlay.style.width.value.value;
                float h = overlay.contentRect.height > 0f ? overlay.contentRect.height : (TRACK_HEIGHT - 4f);
                if (w <= 2f || h <= 2f) return;

                var hld = overlay.userData as CrossfadeOverlayHolder;
                var sA = hld != null ? hld.SegmentA : initialSegA;
                var sB = hld != null ? hld.SegmentB : initialSegB;

                bool isASelected = (sA != null && sA == _selectedSegment);
                bool isBSelected = (sB != null && sB == _selectedSegment);

                // Unify the border width to a precise 1.0px, pixel-aligned with the 1px white border of the block body
                const float BORDER_WIDTH = 1.0f;
                float halfW = BORDER_WIDTH * 0.5f;

                // 1. Draw the lower-left right-triangle fill (segment A fade-out region)
                // When selected, use #265993 exactly matching USS .montage-anim-segment-selected; otherwise keep #1B385C
                Color colorA = isASelected ? new Color(0.15f, 0.35f, 0.58f, 1.0f) : new Color(0.11f, 0.22f, 0.36f, 1.0f);
                painter.fillColor = colorA;
                painter.BeginPath();
                painter.MoveTo(new Vector2(0, 0));
                painter.LineTo(new Vector2(0, h));
                painter.LineTo(new Vector2(w, h));
                painter.ClosePath();
                painter.Fill();

                // 2. Draw the upper-right right-triangle fill (segment B fade-in region)
                Color colorB = isBSelected ? new Color(0.15f, 0.35f, 0.58f, 1.0f) : new Color(0.11f, 0.22f, 0.36f, 1.0f);
                painter.fillColor = colorB;
                painter.BeginPath();
                painter.MoveTo(new Vector2(0, 0));
                painter.LineTo(new Vector2(w, 0));
                painter.LineTo(new Vector2(w, h));
                painter.ClosePath();
                painter.Fill();

                // 3. Draw the vertical section boundary lines (always draw the boundary lines at both ends of the transition zone, regardless of selection, to clearly mark each segment's start and end)
                painter.strokeColor = new Color(1.0f, 1.0f, 1.0f, 0.35f);
                painter.lineWidth = BORDER_WIDTH;

                // Left vertical line (x = halfW): segment B start / segment A exclusive range end
                painter.BeginPath();
                painter.MoveTo(new Vector2(halfW, 0));
                painter.LineTo(new Vector2(halfW, h));
                painter.Stroke();

                // Right vertical line (x = w - halfW): segment A end / segment B exclusive range start
                painter.BeginPath();
                painter.MoveTo(new Vector2(w - halfW, 0));
                painter.LineTo(new Vector2(w - halfW, h));
                painter.Stroke();

                // 4. Draw the horizontal outer borders (use half-pixel offsets halfW / h - halfW so the 1px stroke stays fully inside contentRect, is never clipped, and joins the block body CSS border seamlessly)
                // Light-blue border used by animation blocks when not selected (#2F649B)
                Color defaultBorderColor = new Color(0.184f, 0.392f, 0.608f, 1.0f);

                // The top edge belongs to segment B's outer edge: a 1px pure white highlight when B is selected, otherwise the clip's own 1px light-blue border
                painter.strokeColor = isBSelected ? Color.white : defaultBorderColor;
                painter.lineWidth = BORDER_WIDTH;
                painter.BeginPath();
                painter.MoveTo(new Vector2(0, halfW));
                painter.LineTo(new Vector2(w, halfW));
                painter.Stroke();

                // The bottom edge belongs to segment A's outer edge: a 1px pure white highlight when A is selected, otherwise the clip's own 1px light-blue border
                painter.strokeColor = isASelected ? Color.white : defaultBorderColor;
                painter.lineWidth = BORDER_WIDTH;
                painter.BeginPath();
                painter.MoveTo(new Vector2(0, h - halfW));
                painter.LineTo(new Vector2(w, h - halfW));
                painter.Stroke();

                // 5. Draw the classic Timeline single diagonal split line (from (0, halfW) to (w, h - halfW))
                // If either segment is selected, the diagonal is highlighted as a 1.0px pure white line that closes perfectly with the top/bottom edges and the body border; otherwise 1.0px translucent white
                painter.strokeColor = (isASelected || isBSelected) ? Color.white : new Color(1.0f, 1.0f, 1.0f, 0.40f);
                painter.lineWidth = BORDER_WIDTH;
                painter.BeginPath();
                painter.MoveTo(new Vector2(0, halfW));
                painter.LineTo(new Vector2(w, h - halfW));
                painter.Stroke();
            };

            // Vertical handles: width adapts to the overlap region so narrow transitions do not occlude each other and the diagonal stays clickable
            float handleWidth = Mathf.Clamp(width * 0.35f, 2f, 8f);

            // Vertical handle 1: segment B left handle (adjusts segment B's left start point and PlayRate)
            var leftTrimHandle = new VisualElement();
            leftTrimHandle.AddToClassList("montage-anim-segment-handle");
            leftTrimHandle.AddToClassList("montage-anim-segment-handle-left");
            leftTrimHandle.tooltip = $"Drag to adjust start time of {initialSegB.SegmentName}";
            leftTrimHandle.style.position = Position.Absolute;
            leftTrimHandle.style.left = 0;
            leftTrimHandle.style.width = handleWidth;
            leftTrimHandle.style.top = 0;
            leftTrimHandle.style.bottom = 0;
            leftTrimHandle.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    var hld = overlay.userData as CrossfadeOverlayHolder;
                    var sB = hld != null ? hld.SegmentB : initialSegB;
                    SelectSegment(sB);
                    int curB = TrackSegments != null && sB != null ? TrackSegments.IndexOf(sB) : -1;
                    if (curB >= 0)
                    {
                        StartDrag(evt, curB, 2); // 2: Stretch Left of B
                    }
                    evt.StopPropagation();
                }
            });
            overlay.Add(leftTrimHandle);

            // Vertical handle 2: segment A right handle (adjusts segment A's duration and PlayRate)
            var rightTrimHandle = new VisualElement();
            rightTrimHandle.AddToClassList("montage-anim-segment-handle");
            rightTrimHandle.AddToClassList("montage-anim-segment-handle-right");
            rightTrimHandle.tooltip = $"Drag to adjust duration of {initialSegA.SegmentName}";
            rightTrimHandle.style.position = Position.Absolute;
            rightTrimHandle.style.right = 0;
            rightTrimHandle.style.width = handleWidth;
            rightTrimHandle.style.top = 0;
            rightTrimHandle.style.bottom = 0;
            rightTrimHandle.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    var hld = overlay.userData as CrossfadeOverlayHolder;
                    var sA = hld != null ? hld.SegmentA : initialSegA;
                    SelectSegment(sA);
                    int curA = TrackSegments != null && sA != null ? TrackSegments.IndexOf(sA) : -1;
                    if (curA >= 0)
                    {
                        StartDrag(evt, curA, 1); // 1: Stretch Right of A
                    }
                    evt.StopPropagation();
                }
            });
            overlay.Add(rightTrimHandle);

            // Draw the centered overlap duration label
            var durLbl = new Label($"{duration:F2}s");
            durLbl.AddToClassList("montage-anim-crossfade-label");
            durLbl.pickingMode = PickingMode.Ignore; // Avoid blocking the diagonal mouse hit test
            durLbl.tooltip = $"Crossfade Blending: {duration:F2}s ({Mathf.RoundToInt(duration * _frameRate)} frames)";
            durLbl.style.display = (width > 28f) ? DisplayStyle.Flex : DisplayStyle.None;
            overlay.Add(durLbl);

            var holder = new CrossfadeOverlayHolder
            {
                Root = overlay,
                LeftTrimHandle = leftTrimHandle,
                RightTrimHandle = rightTrimHandle,
                DurationLabel = durLbl,
                SegmentA = initialSegA,
                SegmentB = initialSegB,
                IndexA = indexA,
                IndexB = indexB
            };
            overlay.userData = holder;

            // Mouse move and hover: show the tooltip of the segment matching the side of the diagonal (lower-left or upper-right) under the cursor
            overlay.RegisterCallback<MouseMoveEvent>(evt =>
            {
                float w = overlay.contentRect.width > 0f ? overlay.contentRect.width : overlay.style.width.value.value;
                float h = overlay.contentRect.height > 0f ? overlay.contentRect.height : (TRACK_HEIGHT - 4f);
                if (w <= 0f || h <= 0f) return;

                var hld = overlay.userData as CrossfadeOverlayHolder;
                var sA = hld != null ? hld.SegmentA : initialSegA;
                var sB = hld != null ? hld.SegmentB : initialSegB;

                float diagY = (h / w) * evt.localMousePosition.x;
                bool isHitA = evt.localMousePosition.y >= diagY;
                var targetSeg = isHitA ? sA : sB;
                if (targetSeg != null)
                {
                    overlay.tooltip = $"{targetSeg.SegmentName}\nStart: {targetSeg.StartTime:F2}s\nDuration: {targetSeg.Duration:F2}s\nRate: {targetSeg.PlayRate:F2}x";
                }
            });

            // Mouse down: left-click the lower-left of the diagonal to select A or the upper-right to select B, then drag to move; right-click pops up the matching menu
            overlay.RegisterCallback<MouseDownEvent>(evt =>
            {
                float w = overlay.contentRect.width > 0f ? overlay.contentRect.width : overlay.style.width.value.value;
                float h = overlay.contentRect.height > 0f ? overlay.contentRect.height : (TRACK_HEIGHT - 4f);
                if (w <= 0f || h <= 0f) return;

                var hld = overlay.userData as CrossfadeOverlayHolder;
                var sA = hld != null ? hld.SegmentA : initialSegA;
                var sB = hld != null ? hld.SegmentB : initialSegB;

                float diagY = (h / w) * evt.localMousePosition.x;
                bool isHitA = evt.localMousePosition.y >= diagY;
                var targetSeg = isHitA ? sA : sB;
                if (targetSeg == null) return;

                var segs = TrackSegments;
                int targetIdx = segs != null ? segs.IndexOf(targetSeg) : -1;

                if (evt.button == 0) // Left button
                {
                    SelectSegment(targetSeg);
                    if (targetIdx >= 0)
                    {
                        StartDrag(evt, targetIdx, 0); // 0: Move
                    }
                    evt.StopPropagation();
                }
                else if (evt.button == 1) // Right button
                {
                    SelectSegment(targetSeg);
                    if (targetIdx >= 0)
                    {
                        ShowSegmentContextMenu(targetIdx, evt.mousePosition);
                    }
                    evt.StopPropagation();
                }
            });

            return overlay;
        }

        #endregion

        #region Drag, Trim & Time Stretch (Move, Trim, Stretch & Snapping)

        private void StartDrag(MouseDownEvent evt, int segmentIndex, int mode)
        {
            var segs = TrackSegments;
            if (evt.button != 0 || segs == null) return;
            if (segmentIndex < 0 || segmentIndex >= segs.Count) return;

            var seg = segs[segmentIndex];
            if (seg == null) return;

            _isDraggingSegment = true;
            _draggingIndex = segmentIndex;
            _dragMode = mode;
            _dragStartMousePos = evt.mousePosition;
            _dragStartSegmentTime = seg.StartTime;
            _dragStartDuration = seg.Duration;
            _dragStartOffset = seg.StartOffset;
            _dragStartPlayRate = seg.PlayRate;
            _hasDragMoved = false;

            _draggingBlockElement = null;
            if (_segmentsContainer != null && seg != null)
            {
                for (int i = 0; i < _segmentsContainer.childCount; i++)
                {
                    if (_segmentsContainer[i].userData == seg)
                    {
                        _draggingBlockElement = _segmentsContainer[i];
                        break;
                    }
                }
            }

            if (_selectedSegment != seg || _selectedSegmentIndex != segmentIndex)
            {
                _selectedSegment = seg;
                _selectedSegmentIndex = segmentIndex;
                RefreshSelectedSegmentHighlight();
                OnSegmentSelected?.Invoke(seg, segmentIndex);
            }

            _contentElement.CaptureMouse();
            _contentElement.RegisterCallback<MouseMoveEvent>(OnDragMove);
            _contentElement.RegisterCallback<MouseUpEvent>(OnDragEnd);
            evt.StopPropagation();
        }

        private void OnDragMove(MouseMoveEvent evt)
        {
            var segs = TrackSegments;
            if (!_isDraggingSegment || _draggingIndex < 0 || segs == null) return;
            if (_draggingIndex >= segs.Count) return;

            float deltaPixel = evt.mousePosition.x - _dragStartMousePos.x;
            if (!_hasDragMoved && Mathf.Abs(deltaPixel) > 1.5f)
            {
                _hasDragMoved = true;
                Undo.RecordObject(_targetAsset, _dragMode == 0 ? "Move Animation Segment" : "Stretch Animation Segment");
            }

            if (!_hasDragMoved)
            {
                return;
            }

            var seg = segs[_draggingIndex];
            float pps = PixelsPerSecond;
            float deltaTime = deltaPixel / pps;

            float frameInterval = 1f / Mathf.Max(1f, _frameRate);

            // Mode 0: whole-block horizontal move (Move) - slides in discrete steps snapped to each frame
            if (_dragMode == 0)
            {
                float targetTime = Mathf.Max(0f, _dragStartSegmentTime + deltaTime);
                targetTime = SnapToFrame(targetTime);
                targetTime = ApplyMagneticSnapping(targetTime, _draggingIndex, isStart: true);
                targetTime = SnapToFrame(targetTime);
                seg.StartTime = Mathf.Max(0f, targetTime);

                // Cross-track hover highlight feedback
                var targetTrack = RequestTargetTrack?.Invoke(evt.mousePosition);
                if (targetTrack != null && targetTrack != this)
                {
                    targetTrack.SetDropHighlight(true);
                    OnTrackDropHighlightChanged?.Invoke(targetTrack, true);
                }
                else
                {
                    OnTrackDropHighlightChanged?.Invoke(this, false);
                }
            }
            // Mode 1: drag the right edge to stretch and adjust the rate (Stretch Right, Adjust Duration) - end point snapped discretely to each frame
            else if (_dragMode == 1)
            {
                float targetDuration = Mathf.Max(frameInterval, _dragStartDuration + deltaTime);
                float targetEndTime = _dragStartSegmentTime + targetDuration;
                targetEndTime = SnapToFrame(targetEndTime);
                targetEndTime = ApplyMagneticSnapping(targetEndTime, _draggingIndex, isStart: false);
                targetEndTime = SnapToFrame(targetEndTime);
                targetEndTime = Mathf.Max(_dragStartSegmentTime + frameInterval, targetEndTime);
                seg.Duration = Mathf.Max(frameInterval, targetEndTime - _dragStartSegmentTime);
            }
            // Mode 2: drag the left edge to stretch and adjust the rate (Stretch Left, Adjust Duration, Keep EndTime) - start point snapped discretely to each frame
            else if (_dragMode == 2)
            {
                float originalEndTime = SnapToFrame(_dragStartSegmentTime + _dragStartDuration);
                float targetStartTime = Mathf.Max(0f, _dragStartSegmentTime + deltaTime);
                targetStartTime = SnapToFrame(targetStartTime);
                targetStartTime = ApplyMagneticSnapping(targetStartTime, _draggingIndex, isStart: true);
                targetStartTime = SnapToFrame(targetStartTime);
                targetStartTime = Mathf.Clamp(targetStartTime, 0f, originalEndTime - frameInterval);

                seg.StartTime = targetStartTime;
                seg.Duration = Mathf.Max(frameInterval, originalEndTime - targetStartTime);
            }

            // High-performance lightweight style update: no DOM rebuild, zero GC allocations
            if (_draggingBlockElement != null)
            {
                _draggingBlockElement.style.left = seg.StartTime * pps;
                _draggingBlockElement.style.width = Mathf.Max(12f, seg.Duration * pps);

                // Live validity feedback: highlight a red border when the current drag position causes an invalid triple overlap or containment conflict
                bool isInvalid = HasInvalidOverlap();
                Color borderColor = isInvalid ? new Color(0.95f, 0.26f, 0.21f, 1.0f) : new Color(0.24f, 0.55f, 0.89f, 1.0f);
                _draggingBlockElement.style.borderLeftColor = borderColor;
                _draggingBlockElement.style.borderRightColor = borderColor;
                _draggingBlockElement.style.borderTopColor = borderColor;
                _draggingBlockElement.style.borderBottomColor = borderColor;
            }

            // Update the crossfade transition regions live (Crossfade Overlays)
            UpdateCrossfadeOverlays();

            OnDataModified?.Invoke();
            evt.StopPropagation();
        }

        private void OnDragEnd(MouseUpEvent evt)
        {
            if (!_isDraggingSegment) return;

            var segs = TrackSegments;
            var seg = (_draggingIndex >= 0 && segs != null && _draggingIndex < segs.Count)
                ? segs[_draggingIndex]
                : null;

            OnTrackDropHighlightChanged?.Invoke(null, false);
            SetDropHighlight(false);

            if (_draggingBlockElement != null)
            {
                _draggingBlockElement.style.borderLeftColor = StyleKeyword.Null;
                _draggingBlockElement.style.borderRightColor = StyleKeyword.Null;
                _draggingBlockElement.style.borderTopColor = StyleKeyword.Null;
                _draggingBlockElement.style.borderBottomColor = StyleKeyword.Null;
            }

            _isDraggingSegment = false;
            _draggingIndex = -1;
            _draggingBlockElement = null;

            _contentElement.ReleaseMouse();
            _contentElement.UnregisterCallback<MouseMoveEvent>(OnDragMove);
            _contentElement.UnregisterCallback<MouseUpEvent>(OnDragEnd);

            // If the user only clicked without moving, keep the current state and never broadcast OnDataModified, so preview playback is not interrupted
            bool hasActualChange = _hasDragMoved && seg != null && 
                (Mathf.Abs(seg.StartTime - _dragStartSegmentTime) > 0.0001f || Mathf.Abs(seg.Duration - _dragStartDuration) > 0.0001f);

            // Cross-track move trigger: when in move mode and the mouse is released over another valid animation track
            if (_hasDragMoved && _dragMode == 0 && seg != null)
            {
                var targetTrack = RequestTargetTrack?.Invoke(evt.mousePosition);
                if (targetTrack != null && targetTrack != this)
                {
                    RequestCrossTrackMove?.Invoke(this, targetTrack, seg, seg.StartTime);
                    evt.StopPropagation();
                    return;
                }
            }

            if (!hasActualChange)
            {
                RefreshSelectedSegmentHighlight();
                evt.StopPropagation();
                return;
            }

            if (_targetAsset != null && seg != null)
            {
                // Validate the drop position: triple overlaps and full containment conflicts are forbidden
                if (HasInvalidOverlap())
                {
                    // Validation failed: revert to the initial time and duration from before the drag
                    seg.StartTime = _dragStartSegmentTime;
                    seg.Duration = _dragStartDuration;
                    Debug.LogWarning($"[CwcMontage] Invalid drop position: animation segment '{seg.SegmentName}' would overlap more than two segments or contain a conflict with other segments, so it was reverted to its previous position.");
                }

                _targetAsset.SortAnimationSegments();
                _targetAsset.EnsureSegmentsValid();

                var currentSegs = TrackSegments;
                _selectedSegmentIndex = currentSegs != null ? currentSegs.IndexOf(seg) : -1;
                RebuildSegments();
                EditorUtility.SetDirty(_targetAsset);
                OnDataModified?.Invoke();
            }
            else if (_targetAsset != null)
            {
                RebuildSegments();
            }

            evt.StopPropagation();
        }

        /// <summary>
        /// Only lightly refreshes the selection highlight of the current segment: no DOM rebuild, no visual hierarchy disturbance, and no data-change event broadcast.
        /// </summary>
        private void RefreshSelectedSegmentHighlight()
        {
            if (_segmentsContainer != null)
            {
                for (int i = 0; i < _segmentsContainer.childCount; i++)
                {
                    var child = _segmentsContainer[i];
                    if (child.userData is MontageAnimationSegment seg && seg == _selectedSegment)
                    {
                        child.AddToClassList("montage-anim-segment-selected");
                    }
                    else
                    {
                        child.RemoveFromClassList("montage-anim-segment-selected");
                    }
                }
            }

            if (_crossfadeContainer != null)
            {
                for (int i = 0; i < _crossfadeContainer.childCount; i++)
                {
                    _crossfadeContainer[i].MarkDirtyRepaint();
                }
            }
        }

        /// <summary>
        /// Quantizes a continuous floating-point timestamp to the nearest base frame (Frame Quantization).
        /// </summary>
        /// <param name="time">Timestamp (seconds)</param>
        /// <param name="minFrames">Minimum number of frames to keep (for example, always keep at least 1 frame of duration)</param>
        /// <returns>Frame-snapped discrete timestamp (seconds)</returns>
        private float SnapToFrame(float time, int minFrames = 0)
        {
            float fps = Mathf.Max(1f, _frameRate);
            float frameInterval = 1f / fps;
            float snapped = Mathf.Round(time / frameInterval) * frameInterval;
            if (minFrames > 0)
            {
                snapped = Mathf.Max(minFrames * frameInterval, snapped);
            }
            return snapped;
        }

        /// <summary>
        /// Magnetic snapping algorithm: snaps automatically to adjacent segment boundaries, cross-track action block boundaries, physical section markers, and the 0 point.
        /// </summary>
        private float ApplyMagneticSnapping(float candidateTime, int currentIndex, bool isStart)
        {
            float excludeStart = -1f;
            float excludeEnd = -1f;
            var segs = TrackSegments;
            if (segs != null && currentIndex >= 0 && currentIndex < segs.Count)
            {
                excludeStart = _dragStartSegmentTime;
                excludeEnd = _dragStartSegmentTime + _dragStartDuration;
            }

            return MontageTimelineSnappingUtility.ApplyMagneticSnapping(
                candidateTime,
                PixelsPerSecond,
                _targetAsset,
                playheadTime: -1f,
                isStart: isStart,
                excludeSelfStartTime: excludeStart,
                excludeSelfEndTime: excludeEnd,
                snapPixelThreshold: SNAP_PIXEL_THRESHOLD);
        }

        /// <summary>
        /// Checks whether the current set of animation segments contains invalid overlaps:
        /// 1. Triple Overlap: 3 or more segments overlap at the same moment;
        /// 2. Containment: a segment is completely enclosed and swallowed by another segment on the timeline.
        /// Returns true if an invalid overlap is detected, otherwise false.
        /// </summary>
        private bool HasInvalidOverlap()
        {
            var segments = TrackSegments;
            if (segments == null || segments.Count <= 1) return false;

            // Copy the list and sort by ascending StartTime; for equal StartTime, sort by descending EndTime (larger ranges first)
            var sorted = new List<MontageAnimationSegment>(segments);
            sorted.Sort((a, b) =>
            {
                int cmp = a.StartTime.CompareTo(b.StartTime);
                if (cmp != 0) return cmp;
                return b.EndTime.CompareTo(a.EndTime);
            });

            for (int i = 0; i < sorted.Count; i++)
            {
                var cur = sorted[i];
                if (cur == null) continue;

                // 1. Check for containment (Containment)
                for (int j = i + 1; j < sorted.Count; j++)
                {
                    var next = sorted[j];
                    if (next == null) continue;

                    // If next starts at or after cur's end, no later segment can be contained by cur
                    if (next.StartTime >= cur.EndTime) break;

                    // If next ends at or before cur's end, next lies entirely inside cur (contained and swallowed)
                    if (next.EndTime <= cur.EndTime)
                    {
                        return true;
                    }
                }

                // 2. Check for triple overlap (Triple Overlap)
                if (i < sorted.Count - 2)
                {
                    var third = sorted[i + 2];
                    if (third != null && third.StartTime < cur.EndTime)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        #endregion

        #region Menus & Shortcuts

        private void OnContentKeyDown(KeyDownEvent evt)
        {
            var segs = TrackSegments;
            if (segs == null || _selectedSegmentIndex < 0 || _selectedSegmentIndex >= segs.Count)
            {
                return;
            }

            if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace)
            {
                DeleteSegmentAt(_selectedSegmentIndex);
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.D && evt.actionKey)
            {
                DuplicateSegmentAt(_selectedSegmentIndex);
                evt.StopPropagation();
            }
        }

        public void DeleteSegmentAt(int index)
        {
            var segs = TrackSegments;
            if (segs == null || index < 0 || index >= segs.Count) return;

            Undo.RecordObject(_targetAsset, "Delete Animation Segment");
            segs.RemoveAt(index);
            _selectedSegment = null;
            _selectedSegmentIndex = -1;
            RebuildSegments();
            EditorUtility.SetDirty(_targetAsset);
            OnDataModified?.Invoke();
        }

        public void DeleteSegment(MontageAnimationSegment segment)
        {
            var segs = TrackSegments;
            if (segs == null || segment == null) return;
            int idx = segs.IndexOf(segment);
            if (idx >= 0)
            {
                DeleteSegmentAt(idx);
            }
        }

        public void DuplicateSegmentAt(int index)
        {
            var segs = TrackSegments;
            if (segs == null || index < 0 || index >= segs.Count) return;

            Undo.RecordObject(_targetAsset, "Duplicate Animation Segment");
            var seg = segs[index];
            var copy = seg.Clone();
            copy.StartTime = SnapToFrame(seg.EndTime);
            copy.Duration = SnapToFrame(seg.Duration, minFrames: 1);
            segs.Insert(index + 1, copy);
            _targetAsset.SortAnimationSegments();
            SelectSegment(copy);
            EditorUtility.SetDirty(_targetAsset);
            OnDataModified?.Invoke();
        }

        private void ShowAddClipPicker()
        {
            ShowAddClipPickerAt(-1f);
        }

        private void ShowAddClipPickerAt(float clickTime)
        {
            _isWaitingForPicker = true;
            _activePickerTargetSegment = null;
            _pendingAddClipStartTime = clickTime;
            EditorGUIUtility.ShowObjectPicker<AnimationClip>(null, false, "", 202688);
        }

        public void HandleObjectPickerResult(AnimationClip pickedClip, bool isClosed = false)
        {
            if (!_isWaitingForPicker) return;

            if (_targetAsset == null)
            {
                if (isClosed)
                {
                    _isWaitingForPicker = false;
                    _activePickerTargetSegment = null;
                    _pendingAddClipStartTime = -1f;
                }
                return;
            }

            var segs = TrackSegments;
            if (segs == null)
            {
                if (isClosed) _isWaitingForPicker = false;
                return;
            }

            if (pickedClip != null)
            {
                Undo.RecordObject(_targetAsset, "Pick Animation Clip");

                // 1. If this Picker session has not created a segment yet: create a new segment on the track and focus it
                if (_activePickerTargetSegment == null)
                {
                    float startTime = 0f;
                    if (_pendingAddClipStartTime >= 0f)
                    {
                        startTime = SnapToFrame(_pendingAddClipStartTime);
                    }
                    else if (segs.Count > 0)
                    {
                        var last = segs[^1];
                        if (last != null)
                        {
                            startTime = SnapToFrame(last.EndTime);
                        }
                    }

                    _activePickerTargetSegment = new MontageAnimationSegment(pickedClip, startTime, 1.0f);
                    _activePickerTargetSegment.Duration = SnapToFrame(_activePickerTargetSegment.Duration, minFrames: 1);
                    segs.Add(_activePickerTargetSegment);
                    _targetAsset.SortChannelSegments(_channel);
                    _targetAsset.EnsureSegmentsValid();

                    int newIdx = segs.IndexOf(_activePickerTargetSegment);
                    SelectSegment(newIdx);
                }
                // 2. If a segment was already added in this session and the user selects another animation in the Picker: switch the currently selected segment's clip reference directly
                else
                {
                    _activePickerTargetSegment.Clip = pickedClip;
                    _activePickerTargetSegment.Duration = SnapToFrame(_activePickerTargetSegment.CalculateNaturalDuration(), minFrames: 1);
                    _targetAsset.SortChannelSegments(_channel);
                    _targetAsset.EnsureSegmentsValid();

                    int curIdx = segs.IndexOf(_activePickerTargetSegment);
                    SelectSegment(curIdx);
                }

                EditorUtility.SetDirty(_targetAsset);
                OnDataModified?.Invoke();
            }

            if (isClosed)
            {
                _isWaitingForPicker = false;
                _activePickerTargetSegment = null;
                _pendingAddClipStartTime = -1f;
            }
        }

        private void ShowSegmentContextMenu(int index, Vector2 mousePos)
        {
            var segs = TrackSegments;
            if (segs == null || index < 0 || index >= segs.Count) return;
            var seg = segs[index];

            var menu = new GenericMenu();

            // 1. Put high-frequency core edit operations first (fully aligned with the Block menu standard)
            menu.AddItem(new GUIContent("Copy (Ctrl+C)"), false, () => CopySelectedSegment());
            menu.AddItem(new GUIContent("Duplicate (Ctrl+D)"), false, () => DuplicateSegmentAt(index));
            menu.AddItem(new GUIContent("Delete (Del)"), false, () => DeleteSegmentAt(index));

            menu.AddSeparator("");

            // 2. Useful alignment and parameter reset
            if (index > 0)
            {
                menu.AddItem(new GUIContent("Snap to Previous"), false, () =>
                {
                    var prev = segs[index - 1];
                    if (prev != null)
                    {
                        Undo.RecordObject(_targetAsset, "Snap Segment to Previous");
                        seg.StartTime = SnapToFrame(prev.EndTime);
                        RebuildSegments();
                        EditorUtility.SetDirty(_targetAsset);
                        OnDataModified?.Invoke();
                    }
                });
            }

            menu.AddItem(new GUIContent("Reset PlayRate (1.0x)"), false, () =>
            {
                Undo.RecordObject(_targetAsset, "Reset PlayRate");
                seg.ResetPlayRate();
                RebuildSegments();
                EditorUtility.SetDirty(_targetAsset);
                OnDataModified?.Invoke();
            });

            menu.AddSeparator("");

            // 3. Asset location helpers
            menu.AddItem(new GUIContent("Ping Clip"), false, () =>
            {
                if (seg.Clip != null)
                {
                    EditorGUIUtility.PingObject(seg.Clip);
                }
            });

            menu.DropDown(new Rect(mousePos, Vector2.zero));
        }

        private void OnContentMouseDown(MouseDownEvent evt)
        {
            float clickTime = SnapToFrame(evt.localMousePosition.x / PixelsPerSecond);
            if (evt.button == 0)
            {
                ClearSelection();
                OnRequestScrubTime?.Invoke(clickTime);
            }
            else if (evt.button == 1)
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Add Animation Clip..."), false, () => ShowAddClipPickerAt(clickTime));
                menu.AddSeparator("");

                if (MontageClipboard.HasCopiedSegment)
                {
                    menu.AddItem(new GUIContent("Paste Clip (Ctrl+V)"), false, () => PasteCopiedSegmentAt(clickTime));
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent("Paste Clip (Ctrl+V)"));
                }

                menu.DropDown(new Rect(evt.mousePosition, Vector2.zero));
                evt.StopPropagation();
            }
        }

        private void OnDragUpdated(DragUpdatedEvent evt)
        {
            bool hasClips = false;
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj is AnimationClip)
                {
                    hasClips = true;
                    break;
                }
            }

            if (hasClips)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.StopPropagation();
            }
        }

        private void OnDragPerform(DragPerformEvent evt)
        {
            DragAndDrop.AcceptDrag();
            float dropTime = SnapToFrame(Mathf.Max(0f, evt.localMousePosition.x / PixelsPerSecond));

            Undo.RecordObject(_targetAsset, "Drop Animation Clips");

            var segs = TrackSegments;
            if (segs == null) return;

            float curTime = dropTime;
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj is AnimationClip clip)
                {
                    var seg = new MontageAnimationSegment(clip, curTime, 1.0f);
                    seg.Duration = SnapToFrame(seg.Duration, minFrames: 1);
                    segs.Add(seg);
                    curTime = SnapToFrame(seg.EndTime);
                }
            }

            _targetAsset.SortAnimationSegments();
            _targetAsset.EnsureSegmentsValid();
            RebuildSegments();
            EditorUtility.SetDirty(_targetAsset);
            OnDataModified?.Invoke();
            evt.StopPropagation();
        }

        #endregion

        #region Private Helper Types

        /// <summary>
        /// Holder for the crossfade UI elements, used to update styles, handle widths, and overlap duration text with zero GC allocations.
        /// </summary>
        private class CrossfadeOverlayHolder
        {
            public VisualElement Root;
            public VisualElement LeftTrimHandle;
            public VisualElement RightTrimHandle;
            public Label DurationLabel;
            public MontageAnimationSegment SegmentA;
            public MontageAnimationSegment SegmentB;
            public int IndexA;
            public int IndexB;
        }

        #endregion
    }
}
