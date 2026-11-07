using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Cwcbb.Tools.CwcMontage;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// Modern animation track element (MontageTrackElement).
    /// The left side contains a Timeline-standard Track Header (track rename, Mute, Lock, options menu);
    /// the right side is the timeline content area with an absolute-pixel-aligned zoom system (ActionBlocks container and section boundary lines).
    /// </summary>
    public class MontageTrackElement : VisualElement
    {
        #region Private Constants

        public const float TRACK_HEIGHT = 28f;
        private const float BASE_PIXELS_PER_SECOND = 200f;

        #endregion

        #region Private Fields

        private MontageTrackData _trackData;
        private MontageSequenceAsset _targetAsset;
        private int _trackIndex;
        private float _clipLength = 1f;
        private float _frameRate = 30f;
        private float _zoomLevel = 1.0f;
        private float _contentDuration = 0f;

        private Type[] _actionTypes;
        private string[] _actionTypeNames;

        private VisualElement _headerElement;
        private VisualElement _contentElement;
        private TextField _trackTitleField;
        private Button _muteButton;
        private Button _lockButton;
        private Button _addBlockButton;
        private Button _optionsButton;
        private Vector2 _headerDragStartPos;
        private bool _isDraggingHeader;

        private readonly List<MontageActionBlockElement> _blockElements = new();

        #endregion

        #region Public Events

        public event Action<MontageTrackElement> OnTrackSelected;
        public event Action<MontageActionBlockElement> OnBlockSelected;
        public event Action<MontageActionBlockElement> OnBlockClicked;
        public event Action<MontageActionBlockElement> OnBlockMoving;
        public event Action<MontageActionBlockElement, float> OnBlockDraggingGlobalSync;
        public event Action<MontageTrackElement, Type, float> OnAddBlockAtTime;
        public event Action<MontageTrackElement, float> OnPasteBlockAtTime;
        public event Action<MontageTrackElement> OnTrackCopied;
        public event Action<MontageTrackElement> OnTrackPasteOverrideRequested;
        public event Action<MontageTrackElement> OnInsertTrackBelowRequested;
        public event Action<MontageTrackElement> OnPasteNewTrackBelowRequested;
        public event Action<MontageTrackElement> OnDuplicateTrackRequested;
        public event Action<MontageTrackElement> OnTrackDeleteRequested;
        public event Action<MontageTrackElement, int> OnTrackMoveRequested;
        public event Action<MontageTrackElement, Vector2> OnTrackHeaderDragging;
        public event Action<MontageTrackElement, Vector2> OnTrackHeaderDropped;
        public event Action OnDataModified;

        #endregion

        #region Public Properties

        public MontageTrackData TrackData => _trackData;
        public int TrackIndex => _trackIndex;
        public VisualElement HeaderElement => _headerElement;
        public VisualElement ContentElement => _contentElement;
        public IReadOnlyList<MontageActionBlockElement> BlockElements => _blockElements;

        /// <summary>
        /// Actual pixels per second at the current zoom.
        /// </summary>
        public float PixelsPerSecond => BASE_PIXELS_PER_SECOND * _zoomLevel;

        /// <summary>
        /// Pixel width of the content area at the current zoom.
        /// </summary>
        public float ContentPixelWidth => Mathf.Max(100f, _clipLength * PixelsPerSecond);

        #endregion

        #region Constructor

        public MontageTrackElement(
            MontageTrackData trackData,
            MontageSequenceAsset targetAsset,
            int trackIndex,
            float clipLength,
            float frameRate,
            float zoomLevel,
            Type[] actionTypes,
            string[] actionTypeNames)
        {
            _trackData = trackData;
            _targetAsset = targetAsset;
            _trackIndex = trackIndex;
            _clipLength = Mathf.Max(0.001f, clipLength);
            _frameRate = Mathf.Max(1f, frameRate);
            _zoomLevel = Mathf.Clamp(zoomLevel, 0.005f, 20f);
            _actionTypes = actionTypes ?? Array.Empty<Type>();
            _actionTypeNames = actionTypeNames ?? Array.Empty<string>();

            AddToClassList("montage-track-row");

            // 1. Left track header
            _headerElement = new VisualElement();
            _headerElement.AddToClassList("montage-track-header");
            _headerElement.style.height = TRACK_HEIGHT;
            _headerElement.style.minHeight = TRACK_HEIGHT;
            _headerElement.style.maxHeight = TRACK_HEIGHT;
            _headerElement.style.flexShrink = 0;
            _headerElement.style.flexGrow = 0;

            var titleBox = new VisualElement();
            titleBox.AddToClassList("montage-track-title-box");

            _trackTitleField = new TextField { value = _trackData.TrackName };
            _trackTitleField.AddToClassList("montage-track-title");
            _trackTitleField.isDelayed = true;
            _trackTitleField.RegisterValueChangedCallback(evt =>
            {
                if (_targetAsset != null)
                {
                    Undo.RecordObject(_targetAsset, "Rename Track");
                }
                _trackData.TrackName = evt.newValue;
                if (_targetAsset != null)
                {
                    EditorUtility.SetDirty(_targetAsset);
                }
                OnDataModified?.Invoke();
            });
            titleBox.Add(_trackTitleField);
            _headerElement.Add(titleBox);

            var buttonsGroup = new VisualElement();
            buttonsGroup.style.flexDirection = FlexDirection.Row;
            buttonsGroup.style.alignItems = Align.Center;

            // Mute button
            _muteButton = new Button(ToggleMute) { text = "M" };
            _muteButton.AddToClassList("montage-track-btn-toggle");
            _muteButton.tooltip = "Mute Track (Skip execution)";
            UpdateMuteVisual();
            buttonsGroup.Add(_muteButton);

            // Lock button
            _lockButton = new Button(ToggleLock) { text = "L" };
            _lockButton.AddToClassList("montage-track-btn-toggle");
            _lockButton.tooltip = "Lock Track (Prevent editing)";
            UpdateLockVisual();
            buttonsGroup.Add(_lockButton);

            // Add block button
            _addBlockButton = new Button(ShowAddBlockMenu) { text = "+" };
            _addBlockButton.AddToClassList("montage-toolbar-btn");
            _addBlockButton.tooltip = "Add Action Block to this track";
            buttonsGroup.Add(_addBlockButton);

            _headerElement.Add(buttonsGroup);
            _headerElement.RegisterCallback<MouseDownEvent>(OnHeaderMouseDown);
            _headerElement.RegisterCallback<MouseMoveEvent>(OnHeaderMouseMove);
            _headerElement.RegisterCallback<MouseUpEvent>(OnHeaderMouseUp);

            // 2. Right-side action block content container
            _contentElement = new VisualElement();
            _contentElement.AddToClassList("montage-track-content");
            _contentElement.style.width = ContentPixelWidth;
            _contentElement.style.height = TRACK_HEIGHT;
            _contentElement.style.minHeight = TRACK_HEIGHT;
            _contentElement.style.maxHeight = TRACK_HEIGHT;
            _contentElement.style.flexShrink = 0;
            _contentElement.style.flexGrow = 0;
            _contentElement.generateVisualContent += OnGenerateContentVisual;
            _contentElement.RegisterCallback<MouseDownEvent>(OnContentMouseDown);

            RebuildBlocks();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Rebuild all ActionBlock views in the current track.
        /// Optionally pass the currently selected action block data to restore its selection highlight after rebuilding.
        /// </summary>
        /// <param name="selectedData">Currently selected action block data (optional)</param>
        public void RebuildBlocks(MontageActionBlockData selectedData = null)
        {
            _contentElement.Clear();
            _blockElements.Clear();

            _contentElement.style.width = ContentPixelWidth;

            if (_trackData?.ActionBlocks == null)
            {
                return;
            }

            for (int i = 0; i < _trackData.ActionBlocks.Count; i++)
            {
                var blockData = _trackData.ActionBlocks[i];
                var blockElement = new MontageActionBlockElement(
                    blockData,
                    _trackIndex,
                    i,
                    _clipLength,
                    _frameRate,
                    _zoomLevel,
                    _targetAsset);

                if (selectedData != null && blockData == selectedData)
                {
                    blockElement.SetSelected(true);
                }

                _blockElements.Add(blockElement);
                _contentElement.Add(blockElement);

                blockElement.OnBlockSelected += block => OnBlockSelected?.Invoke(block);
                blockElement.OnBlockClicked += block => OnBlockClicked?.Invoke(block);
                blockElement.OnBlockMoving += block => OnBlockMoving?.Invoke(block);
                blockElement.OnBlockDraggingGlobalSync += (block, time) => OnBlockDraggingGlobalSync?.Invoke(block, time);
                blockElement.OnBlockCopied += block =>
                {
                    MontageClipboard.CopyActionBlock(block.Data);
                };
                blockElement.OnBlockDuplicated += block =>
                {
                    if (!_trackData.IsLocked && block.Data != null)
                    {
                        if (_targetAsset != null)
                        {
                            Undo.RecordObject(_targetAsset, "Duplicate Action Block");
                        }
                        var cloned = block.Data.Clone();
                        int frameLen = Mathf.Max(1, block.Data.EndFrame - block.Data.StartFrame);
                        cloned.StartFrame = block.Data.EndFrame;
                        cloned.EndFrame = cloned.StartFrame + frameLen;
                        float frameInterval = 1f / Mathf.Max(1f, _frameRate);
                        cloned.StartTime = cloned.StartFrame * frameInterval;
                        cloned.EndTime = cloned.EndFrame * frameInterval;
                        _trackData.ActionBlocks.Add(cloned);
                        RebuildBlocks();
                        if (_targetAsset != null)
                        {
                            EditorUtility.SetDirty(_targetAsset);
                        }
                        OnDataModified?.Invoke();
                    }
                };
                blockElement.OnBlockDeleted += block =>
                {
                    if (!_trackData.IsLocked)
                    {
                        if (_targetAsset != null)
                        {
                            Undo.RecordObject(_targetAsset, "Delete Action Block");
                        }
                        _trackData.ActionBlocks.RemoveAt(block.BlockIndex);
                        RebuildBlocks();
                        if (_targetAsset != null)
                        {
                            EditorUtility.SetDirty(_targetAsset);
                        }
                        OnDataModified?.Invoke();
                    }
                };
                blockElement.OnBlockModified += block =>
                {
                    OnDataModified?.Invoke();
                };
            }

            _contentElement.MarkDirtyRepaint();
        }

        /// <summary>
        /// Lightweight refresh only: updates the position, name, and badge of all action blocks in the current track without destroying and rebuilding the DOM, so the selection state is never lost.
        /// </summary>
        public void UpdateBlocksVisual()
        {
            for (int i = 0; i < _blockElements.Count; i++)
            {
                _blockElements[i]?.UpdateVisual();
            }
            _contentElement.MarkDirtyRepaint();
        }

        /// <summary>
        /// Set the current track zoom multiplier.
        /// </summary>
        public void SetZoom(float zoomLevel)
        {
            _zoomLevel = Mathf.Clamp(zoomLevel, 0.005f, 20f);
            _contentElement.style.width = ContentPixelWidth;

            for (int i = 0; i < _blockElements.Count; i++)
            {
                _blockElements[i].SetZoom(_zoomLevel);
            }

            _contentElement.MarkDirtyRepaint();
        }

        /// <summary>
        /// Set the current track's highlighted selection state.
        /// </summary>
        public void SetSelected(bool isSelected)
        {
            if (isSelected)
            {
                _headerElement?.AddToClassList("montage-track-header-selected");
                _contentElement?.AddToClassList("montage-track-content-selected");
            }
            else
            {
                _headerElement?.RemoveFromClassList("montage-track-header-selected");
                _contentElement?.RemoveFromClassList("montage-track-content-selected");
            }
        }

        /// <summary>
        /// Update the time and frame rate configuration and the playhead time.
        /// </summary>
        public void UpdateTimelineConfig(float clipLength, float frameRate, float contentDuration = -1f, float playheadTime = -1f)
        {
            _clipLength = Mathf.Max(0.001f, clipLength);
            _frameRate = Mathf.Max(1f, frameRate);
            _contentDuration = contentDuration >= 0f ? contentDuration : (_targetAsset != null ? _targetAsset.TotalDuration : _clipLength);
            _contentElement.style.width = ContentPixelWidth;

            for (int i = 0; i < _blockElements.Count; i++)
            {
                _blockElements[i].UpdateTimelineConfig(_clipLength, _frameRate, playheadTime);
            }

            _contentElement.MarkDirtyRepaint();
        }

        #endregion

        #region Private Methods and Menus

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

            // 2. Draw physical section boundary helper lines (Physical Section Boundaries)
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

            // 3. Draw content end boundary line (Content End Boundary Line)
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

            // 4. Draw bottom divider line
            painter.strokeColor = new Color(0f, 0f, 0f, 0.35f);
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0, height));
            painter.LineTo(new Vector2(width, height));
            painter.Stroke();
        }

        private void ToggleMute()
        {
            if (_targetAsset != null)
            {
                Undo.RecordObject(_targetAsset, _trackData.IsMuted ? "Unmute Track" : "Mute Track");
            }
            _trackData.IsMuted = !_trackData.IsMuted;
            UpdateMuteVisual();
            if (_targetAsset != null)
            {
                EditorUtility.SetDirty(_targetAsset);
            }
            OnDataModified?.Invoke();
        }

        private void UpdateMuteVisual()
        {
            if (_muteButton == null) return;

            if (_trackData.IsMuted)
            {
                _muteButton.AddToClassList("montage-track-btn-toggle-active");
            }
            else
            {
                _muteButton.RemoveFromClassList("montage-track-btn-toggle-active");
            }
        }

        private void ToggleLock()
        {
            if (_targetAsset != null)
            {
                Undo.RecordObject(_targetAsset, _trackData.IsLocked ? "Unlock Track" : "Lock Track");
            }
            _trackData.IsLocked = !_trackData.IsLocked;
            UpdateLockVisual();
            if (_targetAsset != null)
            {
                EditorUtility.SetDirty(_targetAsset);
            }
            OnDataModified?.Invoke();
        }

        private void UpdateLockVisual()
        {
            if (_lockButton == null) return;

            if (_trackData.IsLocked)
            {
                _lockButton.AddToClassList("montage-track-btn-toggle-active");
            }
            else
            {
                _lockButton.RemoveFromClassList("montage-track-btn-toggle-active");
            }
        }

        private void OnHeaderMouseDown(MouseDownEvent evt)
        {
            OnTrackSelected?.Invoke(this);

            if (evt.button == 1) // Right-click menu
            {
                ShowTrackOptionsMenu();
                evt.StopPropagation();
            }
            else if (evt.button == 0) // Prepare left-button drag reordering
            {
                _headerDragStartPos = evt.mousePosition;
                _isDraggingHeader = false;
            }
        }

        private void OnHeaderMouseMove(MouseMoveEvent evt)
        {
            if (evt.pressedButtons == 1) // Left-button drag
            {
                float deltaY = evt.mousePosition.y - _headerDragStartPos.y;
                if (!_isDraggingHeader && Mathf.Abs(deltaY) > 6f)
                {
                    _isDraggingHeader = true;
                    _headerElement.CaptureMouse();
                    _headerElement.AddToClassList("montage-track-header-selected");
                }

                if (_isDraggingHeader)
                {
                    OnTrackHeaderDragging?.Invoke(this, evt.mousePosition);
                }
            }
        }

        private void OnHeaderMouseUp(MouseUpEvent evt)
        {
            if (_isDraggingHeader && evt.button == 0)
            {
                _isDraggingHeader = false;
                _headerElement.ReleaseMouse();
                _headerElement.RemoveFromClassList("montage-track-header-selected");

                OnTrackHeaderDropped?.Invoke(this, evt.mousePosition);
                evt.StopPropagation();
            }
        }

        private void OnContentMouseDown(MouseDownEvent evt)
        {
            OnTrackSelected?.Invoke(this);

            float localX = evt.localMousePosition.x;
            float pps = PixelsPerSecond;
            float clickTime = (pps > 0.001f) ? Mathf.Clamp(localX / pps, 0f, _clipLength) : 0f;

            if (evt.button == 1) // Right-click on empty area
            {
                ShowContentContextMenu(evt.mousePosition, clickTime);
                evt.StopPropagation();
            }
            else if (evt.button == 0 && evt.clickCount == 2 && !_trackData.IsLocked) // Double-click empty area to add an action block
            {
                ShowContentContextMenu(evt.mousePosition, clickTime);
                evt.StopPropagation();
            }
        }

        private void ShowAddBlockMenu()
        {
            if (_trackData.IsLocked)
            {
                EditorUtility.DisplayDialog("Notice", "Track is locked. Unlock it first to add action blocks.", "OK");
                return;
            }

            var menu = new GenericMenu();
            for (int i = 0; i < _actionTypes.Length; i++)
            {
                var type = _actionTypes[i];
                string name = _actionTypeNames[i];
                menu.AddItem(new GUIContent($"Add Block/{name}"), false, () =>
                {
                    OnAddBlockAtTime?.Invoke(this, type, 0f);
                });
            }
            menu.ShowAsContext();
        }

        private void ShowTrackOptionsMenu()
        {
            var menu = new GenericMenu();

            // 1. Keep frequent track organization actions at the top
            menu.AddItem(new GUIContent("Duplicate Track (Ctrl+D)"), false, () => OnDuplicateTrackRequested?.Invoke(this));
            menu.AddItem(new GUIContent("Delete Track (Delete)"), false, () => OnTrackDeleteRequested?.Invoke(this));

            menu.AddSeparator("");

            // 2. Clipboard-safe operations
            menu.AddItem(new GUIContent("Copy Track (Ctrl+C)"), false, () =>
            {
                MontageClipboard.CopyTrack(_trackData);
                OnTrackCopied?.Invoke(this);
            });

            if (MontageClipboard.HasCopiedTrack)
            {
                menu.AddItem(new GUIContent("Paste as Track Below"), false, () => OnPasteNewTrackBelowRequested?.Invoke(this));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Paste as Track Below"));
            }

            menu.AddSeparator("");

            // 3. High-risk destructive overwrite (isolated at the bottom, with confirmation dialog)
            if (MontageClipboard.HasCopiedTrack && !_trackData.IsLocked)
            {
                menu.AddItem(new GUIContent("Paste Track (Overwrite)"), false, () =>
                {
                    if (EditorUtility.DisplayDialog("Paste Track (Overwrite)", 
                        $"Are you sure you want to overwrite all action blocks in '{_trackData.TrackName}'? This action cannot be undone.", 
                        "Overwrite", "Cancel"))
                    {
                        OnTrackPasteOverrideRequested?.Invoke(this);
                    }
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Paste Track (Overwrite)"));
            }

            menu.ShowAsContext();
        }

        private void ShowContentContextMenu(Vector2 mousePos, float timeAtClick)
        {
            if (_trackData.IsLocked)
            {
                return;
            }

            var menu = new GenericMenu();

            // 1. Keep frequent paste actions at the top, concise wording
            if (MontageClipboard.HasCopiedBlock)
            {
                menu.AddItem(new GUIContent("Paste (Ctrl+V)"), false, () =>
                {
                    OnPasteBlockAtTime?.Invoke(this, timeAtClick);
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Paste (Ctrl+V)"));
            }

            menu.AddSeparator("");

            // 2. Add action block type submenu
            for (int i = 0; i < _actionTypes.Length; i++)
            {
                var type = _actionTypes[i];
                string name = _actionTypeNames[i];
                menu.AddItem(new GUIContent($"Add Action Block/{name}"), false, () =>
                {
                    OnAddBlockAtTime?.Invoke(this, type, timeAtClick);
                });
            }

            menu.AddSeparator("");

            // 3. Track-level quick actions
            menu.AddItem(new GUIContent("Add New Track Below"), false, () => OnInsertTrackBelowRequested?.Invoke(this));
            if (MontageClipboard.HasCopiedTrack)
            {
                menu.AddItem(new GUIContent("Paste as Track Below"), false, () => OnPasteNewTrackBelowRequested?.Invoke(this));
            }

            menu.ShowAsContext();
        }

        #endregion
    }
}
