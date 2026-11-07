using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Cwcbb.Tools.CwcMontage;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// Modern action block UI element (MontageActionBlockElement).
    /// Aligns to the timeline zoom system with pixel-level absolute coordinates, offering a Timeline Clip-like interaction and visual experience:
    /// left/right edge handles for duration adjustment, whole-block translation, selection highlight, shortcuts, and context menu.
    /// </summary>
    public class MontageActionBlockElement : VisualElement
    {
        #region Private Constants

        private const float BASE_PIXELS_PER_SECOND = 200f;

        #endregion

        #region Private Fields

        private MontageActionBlockData _data;
        private MontageSequenceAsset _targetAsset;
        private int _trackIndex;
        private int _blockIndex;
        private float _clipLength = 1f;
        private float _frameRate = 30f;
        private float _zoomLevel = 1.0f;

        private VisualElement _contentContainer;
        private Label _titleLabel;
        private Label _rateBadge;
        private VisualElement _leftHandle;
        private VisualElement _rightHandle;
        private VisualElement _capturedContainer;

        private bool _isInteracting;
        private bool _hasDragMoved;
        private int _dragMode; // 0: Move, 1: ResizeRight, 2: ResizeLeft
        private float _dragStartMouseX;
        private int _initialStartFrame;
        private int _initialEndFrame;
        private int _minAllowedStartFrame;
        private int _maxAllowedEndFrame;
        private float _currentPlayheadTime = -1f;

        #endregion

        #region Public Events

        public event Action<MontageActionBlockElement> OnBlockSelected;
        public event Action<MontageActionBlockElement> OnBlockClicked;
        public event Action<MontageActionBlockElement> OnBlockCopied;
        public event Action<MontageActionBlockElement> OnBlockDuplicated;
        public event Action<MontageActionBlockElement> OnBlockDeleted;
        public event Action<MontageActionBlockElement> OnBlockModified;
        public event Action<MontageActionBlockElement> OnBlockMoving;
        public event Action<MontageActionBlockElement, float> OnBlockDraggingGlobalSync;

        #endregion

        #region Public Properties

        public MontageActionBlockData Data => _data;
        public int TrackIndex => _trackIndex;
        public int BlockIndex => _blockIndex;
        public bool IsSelected { get; private set; }

        #endregion

        #region Constructors

        public MontageActionBlockElement(
            MontageActionBlockData data,
            int trackIndex,
            int blockIndex,
            float clipLength,
            float frameRate,
            float zoomLevel,
            MontageSequenceAsset targetAsset = null)
        {
            _data = data;
            _targetAsset = targetAsset;
            _trackIndex = trackIndex;
            _blockIndex = blockIndex;
            _clipLength = Mathf.Max(0.001f, clipLength);
            _frameRate = Mathf.Max(1f, frameRate);
            _zoomLevel = Mathf.Clamp(zoomLevel, 0.005f, 20f);

            AddToClassList("montage-action-block");

            // Adapt background color to action type
            if (data?.Action != null)
            {
                style.backgroundColor = GetActionTypeColor(data.Action.GetType());
            }

            // Own content container: name left-aligned, badge right-aligned, fully aligned with the animation clip
            _contentContainer = new VisualElement();
            _contentContainer.AddToClassList("montage-action-block-content");
            _contentContainer.pickingMode = PickingMode.Ignore;

            _titleLabel = new Label();
            _titleLabel.AddToClassList("montage-action-block-title");
            _titleLabel.pickingMode = PickingMode.Ignore;
            _contentContainer.Add(_titleLabel);

            _rateBadge = new Label();
            _rateBadge.AddToClassList("montage-action-block-badge");
            _rateBadge.pickingMode = PickingMode.Ignore;
            _contentContainer.Add(_rateBadge);

            Add(_contentContainer);

            // Left/right resize handles (attach events separately to prevent false parent bubble handling)
            _leftHandle = new VisualElement();
            _leftHandle.AddToClassList("montage-action-block-handle");
            _leftHandle.AddToClassList("montage-action-block-handle-left");
            _leftHandle.tooltip = "Drag left edge to resize start frame";
            _leftHandle.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    StartDrag(evt, 2); // 2: ResizeLeft
                    evt.StopPropagation();
                }
            });
            Add(_leftHandle);

            _rightHandle = new VisualElement();
            _rightHandle.AddToClassList("montage-action-block-handle");
            _rightHandle.AddToClassList("montage-action-block-handle-right");
            _rightHandle.tooltip = "Drag right edge to resize end frame";
            _rightHandle.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    StartDrag(evt, 1); // 1: ResizeRight
                    evt.StopPropagation();
                }
            });
            Add(_rightHandle);

            _leftHandle.BringToFront();
            _rightHandle.BringToFront();

            UpdateVisual();

            // Register main body mouse events (move and right-click context menu)
            RegisterCallback<MouseDownEvent>(OnMouseDown);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Updates the action block's absolute pixel layout and text display on the timeline.
        /// </summary>
        public void UpdateVisual()
        {
            if (_data == null)
            {
                return;
            }

            float currentDuration = Mathf.Max(0.0001f, (_data.EndFrame - _data.StartFrame) / _frameRate);
            if (_data.Action != null)
            {
                _data.Action.BlockDuration = currentDuration;
            }

            float pps = BASE_PIXELS_PER_SECOND * _zoomLevel;
            float frameWidth = pps / _frameRate;

            float leftPos = _data.StartFrame * frameWidth;
            float width = Mathf.Max(12f, (_data.EndFrame - _data.StartFrame) * frameWidth);

            style.left = leftPos;
            style.width = width;

            string actionName = _data.Action != null ? _data.Action.GetType().Name : "Empty Action";
            string customName = _data.Action?.CustomName;

            // Attribute display name
            var dispAttr = _data.Action?.GetType().GetCustomAttribute<MontageDisplayNameAttribute>();
            string attrName = dispAttr?.DisplayName;

            string displayName = !string.IsNullOrEmpty(customName)
                ? customName
                : (!string.IsNullOrEmpty(attrName) ? attrName : actionName);

            if (_data.Action is AudioActionBlock audioBlock && audioBlock.AudioClip != null)
            {
                displayName = $"{displayName}: {audioBlock.AudioClip.name}";
            }

            _titleLabel.text = displayName;

            // Speed rate badge (right-aligned, 1.25x format, identical to animation clips)
            if (_data.Action != null && _data.Action.IsTrimmableClip)
            {
                float speed = _data.Action.SpeedMultiplier;
                if (Mathf.Abs(speed - 1.0f) > 0.01f)
                {
                    _rateBadge.text = $"{speed:F2}x";
                    _rateBadge.style.display = DisplayStyle.Flex;
                }
                else
                {
                    _rateBadge.style.display = DisplayStyle.None;
                }
            }
            else
            {
                _rateBadge.style.display = DisplayStyle.None;
            }

            // Hover tooltip
            string speedInfo = _data.Action != null && _data.Action.IsTrimmableClip
                ? $"\nRate: {_data.Action.SpeedMultiplier:F2}x (Clip: {_data.Action.ClipStartTime:F2}s - {_data.Action.ClipEndTime:F2}s)"
                : "";
            _contentContainer.tooltip = $"{displayName}\nStart: {_data.StartFrame / _frameRate:F2}s (Frame {_data.StartFrame})\nDuration: {currentDuration:F2}s ({_data.EndFrame - _data.StartFrame} frames){speedInfo}";

            // Gray out when disabled
            if (!_data.IsEnabled)
            {
                AddToClassList("montage-action-block-disabled");
            }
            else
            {
                RemoveFromClassList("montage-action-block-disabled");
            }
        }

        /// <summary>
        /// Updates the timeline zoom factor.
        /// </summary>
        public void SetZoom(float zoomLevel)
        {
            _zoomLevel = Mathf.Clamp(zoomLevel, 0.005f, 20f);
            UpdateVisual();
        }

        /// <summary>
        /// Updates the timeline reference duration, base frame rate, and playhead time.
        /// </summary>
        public void UpdateTimelineConfig(float clipLength, float frameRate, float playheadTime = -1f)
        {
            _clipLength = Mathf.Max(0.001f, clipLength);
            _frameRate = Mathf.Max(1f, frameRate);
            if (playheadTime >= 0f)
            {
                _currentPlayheadTime = playheadTime;
            }
            UpdateVisual();
        }

        /// <summary>
        /// Updates the current playhead time (used for magnetic snapping).
        /// </summary>
        public void SetPlayheadTime(float playheadTime)
        {
            _currentPlayheadTime = playheadTime;
        }

        /// <summary>
        /// Sets the block's highlighted selection state.
        /// </summary>
        public void SetSelected(bool isSelected)
        {
            IsSelected = isSelected;
            if (isSelected)
            {
                AddToClassList("montage-action-block-selected");
                BringToFront();
            }
            else
            {
                RemoveFromClassList("montage-action-block-selected");
            }
        }

        /// <summary>
        /// Opens the C# script source of the corresponding ActionBlock.
        /// </summary>
        public void OpenScript()
        {
            if (_data?.Action == null)
            {
                return;
            }

            Type type = _data.Action.GetType();
            string[] guids = AssetDatabase.FindAssets($"{type.Name} t:MonoScript");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null)
                {
                    AssetDatabase.OpenAsset(script);
                }
            }
        }

        #endregion

        #region Private Methods and Mouse Interaction

        private void StartDrag(MouseDownEvent evt, int mode)
        {
            if (evt.button != 0 || _data == null)
            {
                return;
            }

            BringToFront();

            if (!IsSelected)
            {
                OnBlockSelected?.Invoke(this);
            }

            _isInteracting = true;
            _dragMode = mode;
            _dragStartMouseX = evt.mousePosition.x;
            _initialStartFrame = _data.StartFrame;
            _initialEndFrame = _data.EndFrame;
            _hasDragMoved = false;

            // Per single-track exclusivity: compute hard collision bounds from adjacent blocks on the same track
            CalculateTrackBoundaryConstraints();

            // Capture the mouse and register listeners on the stable, full-width track container to avoid event jumps caused by the element's own movement
            _capturedContainer = parent ?? this;
            _capturedContainer.CaptureMouse();
            _capturedContainer.RegisterCallback<MouseMoveEvent>(OnDragMove);
            _capturedContainer.RegisterCallback<MouseUpEvent>(OnDragEnd);
        }

        private void CalculateTrackBoundaryConstraints()
        {
            int minStart = 0;
            int maxEnd = int.MaxValue;

            if (_targetAsset?.Tracks != null && _trackIndex >= 0 && _trackIndex < _targetAsset.Tracks.Count)
            {
                var blocks = _targetAsset.Tracks[_trackIndex].ActionBlocks;
                if (blocks != null)
                {
                    for (int i = 0; i < blocks.Count; i++)
                    {
                        var other = blocks[i];
                        if (other == null || other == _data) continue;

                        if (other.EndFrame <= _initialStartFrame)
                        {
                            if (other.EndFrame > minStart)
                            {
                                minStart = other.EndFrame;
                            }
                        }
                        else if (other.StartFrame >= _initialEndFrame)
                        {
                            if (other.StartFrame < maxEnd)
                            {
                                maxEnd = other.StartFrame;
                            }
                        }
                    }
                }
            }

            _minAllowedStartFrame = minStart;
            _maxAllowedEndFrame = maxEnd;
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            if (evt.button == 0) // Left click to move
            {
                StartDrag(evt, 0); // 0: Move
                evt.StopPropagation();
            }
            else if (evt.button == 1) // Right-click context menu
            {
                ShowContextMenu(evt.mousePosition);
                evt.StopPropagation();
            }
        }

        private void OnDragMove(MouseMoveEvent evt)
        {
            if (!_isInteracting || _data == null)
            {
                return;
            }

            float deltaX = evt.mousePosition.x - _dragStartMouseX;
            if (!_hasDragMoved && Mathf.Abs(deltaX) > 1.5f)
            {
                _hasDragMoved = true;
                if (_targetAsset != null)
                {
                    Undo.RecordObject(_targetAsset, _dragMode == 0 ? "Move Action Block" : "Resize Action Block");
                }
            }

            if (!_hasDragMoved)
            {
                return;
            }

            float pps = BASE_PIXELS_PER_SECOND * _zoomLevel;
            if (pps <= 0.001f)
            {
                return;
            }

            float deltaTime = deltaX / pps;
            float activeEdgeTime = 0f;
            int prevStartFrame = _data.StartFrame;
            int prevEndFrame = _data.EndFrame;

            if (_dragMode == 0) // Move whole block
            {
                int durationFrames = Mathf.Max(1, _initialEndFrame - _initialStartFrame);
                float initialStartTime = _initialStartFrame / _frameRate;
                float candidateStartTime = Mathf.Max(0f, initialStartTime + deltaTime);

                // Quantize to integer frames and apply multi-level magnetic snapping (zero point, section markers, playhead, clips, and action blocks on other tracks)
                candidateStartTime = MontageTimelineSnappingUtility.SnapToFrame(candidateStartTime, _frameRate);
                candidateStartTime = MontageTimelineSnappingUtility.ApplyMagneticSnapping(
                    candidateStartTime,
                    pps,
                    _targetAsset,
                    _currentPlayheadTime,
                    isStart: true,
                    excludeSelfStartTime: _initialStartFrame / _frameRate,
                    excludeSelfEndTime: _initialEndFrame / _frameRate);
                candidateStartTime = MontageTimelineSnappingUtility.SnapToFrame(candidateStartTime, _frameRate);

                int targetStartFrame = MontageTimelineSnappingUtility.SecondsToFrame(candidateStartTime, _frameRate);

                // Single-track exclusivity hard-bound clamping
                int maxAllowedStart = (_maxAllowedEndFrame < int.MaxValue)
                    ? Mathf.Max(_minAllowedStartFrame, _maxAllowedEndFrame - durationFrames)
                    : int.MaxValue;
                targetStartFrame = Mathf.Clamp(targetStartFrame, _minAllowedStartFrame, maxAllowedStart);
                int targetEndFrame = targetStartFrame + durationFrames;

                _data.StartFrame = targetStartFrame;
                _data.EndFrame = targetEndFrame;
                _data.StartTime = targetStartFrame / _frameRate;
                _data.EndTime = targetEndFrame / _frameRate;

                activeEdgeTime = _data.StartTime;
            }
            else if (_dragMode == 1) // StretchRight right-edge stretch to adjust rate
            {
                float initialEndTime = _initialEndFrame / _frameRate;
                float candidateEndTime = Mathf.Max((_initialStartFrame + 1) / _frameRate, initialEndTime + deltaTime);

                // Quantize to integer frames and apply multi-level magnetic snapping
                candidateEndTime = MontageTimelineSnappingUtility.SnapToFrame(candidateEndTime, _frameRate);
                candidateEndTime = MontageTimelineSnappingUtility.ApplyMagneticSnapping(
                    candidateEndTime,
                    pps,
                    _targetAsset,
                    _currentPlayheadTime,
                    isStart: false,
                    excludeSelfStartTime: _initialStartFrame / _frameRate,
                    excludeSelfEndTime: _initialEndFrame / _frameRate);
                candidateEndTime = MontageTimelineSnappingUtility.SnapToFrame(candidateEndTime, _frameRate);

                int targetEndFrame = MontageTimelineSnappingUtility.SecondsToFrame(candidateEndTime, _frameRate);

                // Single-track exclusivity hard-bound clamping
                targetEndFrame = Mathf.Clamp(targetEndFrame, _initialStartFrame + 1, _maxAllowedEndFrame);

                _data.EndFrame = targetEndFrame;
                _data.EndTime = targetEndFrame / _frameRate;

                // Place the sample point half a frame inside the right edge so it stays within the block's valid evaluation range
                activeEdgeTime = Mathf.Max(_data.StartTime, (_data.EndFrame - 0.5f) / _frameRate);
            }
            else if (_dragMode == 2) // StretchLeft left-edge stretch to adjust rate
            {
                float initialStartTime = _initialStartFrame / _frameRate;
                float candidateStartTime = Mathf.Max(0f, initialStartTime + deltaTime);

                // Quantize to integer frames and apply multi-level magnetic snapping
                candidateStartTime = MontageTimelineSnappingUtility.SnapToFrame(candidateStartTime, _frameRate);
                candidateStartTime = MontageTimelineSnappingUtility.ApplyMagneticSnapping(
                    candidateStartTime,
                    pps,
                    _targetAsset,
                    _currentPlayheadTime,
                    isStart: true,
                    excludeSelfStartTime: _initialStartFrame / _frameRate,
                    excludeSelfEndTime: _initialEndFrame / _frameRate);
                candidateStartTime = MontageTimelineSnappingUtility.SnapToFrame(candidateStartTime, _frameRate);

                int targetStartFrame = MontageTimelineSnappingUtility.SecondsToFrame(candidateStartTime, _frameRate);

                // Single-track exclusivity hard-bound clamping
                targetStartFrame = Mathf.Clamp(targetStartFrame, _minAllowedStartFrame, _initialEndFrame - 1);

                _data.StartFrame = targetStartFrame;
                _data.StartTime = targetStartFrame / _frameRate;

                activeEdgeTime = _data.StartTime;
            }

            bool frameChanged = (_data.StartFrame != prevStartFrame || _data.EndFrame != prevEndFrame);

            // Lightweight update of the element's absolute position, label, and badge
            UpdateVisual();

            if (frameChanged)
            {
                try
                {
                    OnBlockMoving?.Invoke(this);
                    OnBlockDraggingGlobalSync?.Invoke(this, activeEdgeTime);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[CwcMontage] Action block drag notification error: {ex.Message}");
                }
            }

            evt.StopPropagation();
        }

        private void OnDragEnd(MouseUpEvent evt)
        {
            EndDrag();
            evt.StopPropagation();
        }

        private void EndDrag()
        {
            if (!_isInteracting)
            {
                return;
            }

            _isInteracting = false;

            if (_capturedContainer != null)
            {
                if (_capturedContainer.HasMouseCapture())
                {
                    _capturedContainer.ReleaseMouse();
                }
                _capturedContainer.UnregisterCallback<MouseMoveEvent>(OnDragMove);
                _capturedContainer.UnregisterCallback<MouseUpEvent>(OnDragEnd);
                _capturedContainer = null;
            }

            // 1. If the user just clicked (no movement), raise OnBlockClicked to open inspector details
            if (!_hasDragMoved)
            {
                OnBlockClicked?.Invoke(this);
            }
            // 2. Only persist and broadcast the modification when there is real drag displacement and the frame range actually changed
            else
            {
                bool hasActualChange = _data != null &&
                    (_data.StartFrame != _initialStartFrame || _data.EndFrame != _initialEndFrame);

                if (hasActualChange)
                {
                    if (_targetAsset != null)
                    {
                        EditorUtility.SetDirty(_targetAsset);
                    }
                    OnBlockModified?.Invoke(this);
                }
                else
                {
                    OnBlockClicked?.Invoke(this);
                }
            }
        }

        private void ShowContextMenu(Vector2 mousePos)
        {
            var menu = new GenericMenu();

            // 1. Core high-frequency editing operations
            menu.AddItem(new GUIContent("Copy (Ctrl+C)"), false, () => OnBlockCopied?.Invoke(this));
            menu.AddItem(new GUIContent("Duplicate (Ctrl+D)"), false, () => OnBlockDuplicated?.Invoke(this));
            menu.AddItem(new GUIContent("Delete (Delete)"), false, () => OnBlockDeleted?.Invoke(this));

            menu.AddSeparator("");

            // 2. Enable/disable state control
            menu.AddItem(new GUIContent(_data.IsEnabled ? "Disable Block" : "Enable Block"), false, () =>
            {
                if (_data.Action != null)
                {
                    var field = typeof(MontageActionBlockBase).GetField("_isEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (field != null)
                    {
                        bool cur = (bool)field.GetValue(_data.Action);
                        field.SetValue(_data.Action, !cur);
                    }
                }
                UpdateVisual();
                OnBlockModified?.Invoke(this);
            });

            menu.AddSeparator("");

            // 3. Media clip trimming and duration matching (if trim/rate is supported)
            if (_data.Action != null && _data.Action.IsTrimmableClip)
            {
                menu.AddItem(new GUIContent("Fit Block to Clip (1.0x)"), false, () =>
                {
                    if (_targetAsset != null)
                    {
                        Undo.RecordObject(_targetAsset, "Fit Block To Clip");
                    }
                    float effDur = _data.Action.EffectiveClipDuration;
                    int newSpan = Mathf.Max(1, Mathf.RoundToInt(effDur * _frameRate));
                    _data.EndFrame = _data.StartFrame + newSpan;
                    _data.EndTime = _data.EndFrame / _frameRate;
                    _data.Action.BlockDuration = effDur;
                    UpdateVisual();
                    if (_targetAsset != null)
                    {
                        EditorUtility.SetDirty(_targetAsset);
                    }
                    OnBlockModified?.Invoke(this);
                });

                menu.AddItem(new GUIContent("Fit Clip to Block"), false, () =>
                {
                    if (_targetAsset != null)
                    {
                        Undo.RecordObject(_targetAsset, "Fit Clip To Block");
                    }
                    float currentDur = (_data.EndFrame - _data.StartFrame) / _frameRate;
                    _data.Action.FitClipToDuration(currentDur);
                    UpdateVisual();
                    if (_targetAsset != null)
                    {
                        EditorUtility.SetDirty(_targetAsset);
                    }
                    OnBlockModified?.Invoke(this);
                });

                menu.AddSeparator("");
            }
            else if (_data.Action is AudioActionBlock audioAction && audioAction.AudioClip != null)
            {
                menu.AddItem(new GUIContent("Fit Block to Audio Duration"), false, () =>
                {
                    if (_targetAsset != null)
                    {
                        Undo.RecordObject(_targetAsset, "Fit Block to Audio Duration");
                    }
                    float clipDur = audioAction.AudioClip.length;
                    int newSpan = Mathf.Max(1, Mathf.RoundToInt(clipDur * _frameRate));
                    _data.EndFrame = _data.StartFrame + newSpan;
                    _data.EndTime = _data.EndFrame / _frameRate;
                    _data.Action.BlockDuration = clipDur;
                    UpdateVisual();
                    if (_targetAsset != null)
                    {
                        EditorUtility.SetDirty(_targetAsset);
                    }
                    OnBlockModified?.Invoke(this);
                });

                menu.AddSeparator("");
            }

            // 4. Developer helpers
            menu.AddItem(new GUIContent("Open Script"), false, OpenScript);
            menu.ShowAsContext();
        }

        private static Color GetActionTypeColor(Type blockType)
        {
            if (blockType == null)
            {
                return new Color(0.3f, 0.3f, 0.3f, 0.9f);
            }

            var attr = blockType.GetCustomAttribute<MontageColorAttribute>();
            if (attr != null && ColorUtility.TryParseHtmlString(attr.HexColor, out var parsedColor))
            {
                return new Color(parsedColor.r, parsedColor.g, parsedColor.b, 0.85f);
            }

            int hash = Math.Abs(blockType.Name.GetHashCode());
            float h = (hash % 100) / 100f;
            return Color.HSVToRGB(h, 0.6f, 0.75f);
        }

        #endregion
    }
}
