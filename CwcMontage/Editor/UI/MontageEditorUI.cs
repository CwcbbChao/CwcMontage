using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.UIElements;
using Cwcbb.Tools.CwcMontage;
using Object = UnityEngine.Object;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// Core modern UI controller for montage assets (MontageEditorUI).
    /// Coordinates the 3D render viewport, playback control bar, timeline zoom engine, section marker interaction, track container, and sidebar inspector.
    /// </summary>
    public class MontageEditorUI : IDisposable
    {
        #region Private Constants

        private const string PREFS_ZOOM_LEVEL_KEY = "CwcMontage_Editor_ZoomLevel";
        private const float DEFAULT_ZOOM_LEVEL = 2.5f;
        private const string PREFS_PREVIEW_ROOT_MOTION_KEY = "CwcMontage_Preview_RootMotion";
        private const string PREFS_PREVIEW_FOOT_IK_KEY = "CwcMontage_Preview_FootIK";
        private const string PREVIEW_MODEL_PREFS_KEY = "CwcMontage_LastPreviewModelGuid";
        private const string USS_GUID = "1ac56d601272aa740b6f49bbcc195f4f";
        private const string DEFAULT_DUMMY_MODEL_GUID = "a0ee339482ca65344a511b7983d67fd8";
        private const string PREVIEW_BASE_MOTION_PREFS_KEY = "CwcMontage_PreviewBaseMotionGuid";

        #endregion

        #region Private Fields

        private readonly MontageSequenceAsset _targetAsset;
        private readonly SerializedObject _serializedObject;

        private VisualElement _root;
        private MontagePreviewViewportElement _viewport;
        private MontageActionInspectorElement _inspector;
        private MontageTimelineRuler _ruler;
        private MontageSectionTrackElement _sectionTrackElement;
        private MontageAnimationTrackElement _upperBodyTrackElement;
        private MontageAnimationTrackElement _fullBodyTrackElement;
        private MontageAnimationTrackElement _additiveTrackElement;
        private MontageAnimationSegment _selectedSegment;
        private MontageAnimationTrackElement _selectedAnimationTrack;
        private Button _layersDropdownBtn;
        private ScrollView _headersScrollView;
        private VisualElement _headersContentWrapper;
        private VisualElement _reorderIndicatorLine;
        private ScrollView _tracksScrollView;
        private VisualElement _tracksContentWrapper;
        private GameObject _currentPreviewPrefab;
        private AnimationClip _currentPreviewBaseMotion;
        private AvatarMask _currentPreviewUpperBodyMask;

        // Preview control and playback state
        private float _previewSpeed = 1.0f;
        private bool _previewLoop = true;
        private bool _previewRootMotion = false;
        private bool _previewFootIK = true;
        private float _zoomLevel = DEFAULT_ZOOM_LEVEL;

        private Button _playPauseButton;
        private Image _playPauseIcon;
        private Button _stopButton;
        private Button _prevFrameButton;
        private Button _nextFrameButton;
        private Button _lastFrameButton;
        private Label _timeValueLabel;
        private Label _frameValueLabel;
        private Label _sectionBadgeLabel;
        private FloatField _playRateField;
        private Toggle _loopToggle;

        // Playable & animation state
        private PlayableGraph _playableGraph;
        private AnimationLayerMixerPlayable _topLevelPreviewMixer;
        private AnimationClipPlayable _baseMotionPlayable;
        private AnimationMixerPlayable _upperBodyMixer;
        private AnimationMixerPlayable _fullBodyMixer;
        private AnimationMixerPlayable _additiveMixer;
        private readonly List<AnimationClipPlayable> _upperBodySegmentPlayables = new();
        private readonly List<AnimationClipPlayable> _fullBodySegmentPlayables = new();
        private readonly List<AnimationClipPlayable> _additiveSegmentPlayables = new();
        private readonly List<int> _tempEvalIndices = new();
        private readonly List<float> _tempEvalTimes = new();
        private readonly List<float> _tempEvalWeights = new();
        private AnimationPlayableOutput _playableOutput;
        private Animator _previewAnimator;
        private GameObject _previewObject;

        private bool _isPlaying;
        private float _animationTime;
        private float _lastAnimationTime;
        private double _lastEditorTime;
        private float _clipLength = 1f;
        private float _contentDuration = 0f;
        private float _frameRate = 30f;
        private int _totalFrames = 30;

        private Type[] _actionTypes;
        private string[] _actionTypeNames;

        private readonly List<MontageTrackElement> _trackElements = new();
        private MontageActionBlockElement _selectedBlock;
        private MontageActionBlockData _selectedBlockData;
        private MontageTrackElement _selectedTrack;

        // Runtime preview action blocks and context
        private readonly List<MontageActionBlockData> _runtimeActionBlocks = new();
        private readonly HashSet<MontageActionBlockData> _activeActionBlocks = new();

        #endregion

        #region Public Events

        public event Action OnAssetModified;
        public event Action OnRepaintRequested;

        #endregion

        #region Public Properties

        public VisualElement Root => _root;
        public MontageSequenceAsset TargetAsset => _targetAsset;
        public bool IsPlaying => _isPlaying;
        public bool IsViewportInteracting => _viewport != null && _viewport.IsInteracting;

        #endregion

        #region Constructor and Initialization

        public MontageEditorUI(MontageSequenceAsset targetAsset, SerializedObject serializedObject)
        {
            _targetAsset = targetAsset;
            _serializedObject = serializedObject;
            _zoomLevel = EditorPrefs.GetFloat(PREFS_ZOOM_LEVEL_KEY, DEFAULT_ZOOM_LEVEL);
            _zoomLevel = Mathf.Clamp(_zoomLevel, 0.005f, 20.0f);
            _previewRootMotion = EditorPrefs.GetBool(PREFS_PREVIEW_ROOT_MOTION_KEY, false);
            _previewFootIK = EditorPrefs.GetBool(PREFS_PREVIEW_FOOT_IK_KEY, true);

            CollectActionTypes();
            BuildUIHierarchy();
            MontageAudioPreviewUtility.EnsureInitialized();
            InitializePreviewAndPlayables();
            RebuildTracks();

            // Register global key and frame driving (use global EditorApplication.update so playback never stalls under any UI focus/interaction)
            _root.RegisterCallback<KeyDownEvent>(OnKeyDown);
            _lastEditorTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnUpdateTick;

            // Register a global Undo/Redo transaction listener for zero-latency refresh on undo/redo
            Undo.undoRedoPerformed += HandleUndoRedoPerformed;
        }

        #endregion

        #region Public Lifecycle

        public void RenderViewportImmediate() => _viewport?.RenderImmediate();

        /// <summary>
        /// Handles an animation clip asset selected from an external ObjectPicker and adds it to the animation track currently waiting for input.
        /// </summary>
        public void HandleAnimationPickerResult(AnimationClip picked, bool isClosed = false)
        {
            _additiveTrackElement?.HandleObjectPickerResult(picked, isClosed);
            _fullBodyTrackElement?.HandleObjectPickerResult(picked, isClosed);
            _upperBodyTrackElement?.HandleObjectPickerResult(picked, isClosed);
        }

        public void Dispose()
        {
            Undo.undoRedoPerformed -= HandleUndoRedoPerformed;
            EditorApplication.update -= OnUpdateTick;
            CleanupPlayablesAndPreview();
            MontageAudioPreviewUtility.StopAllClips();
            _root?.Unbind();
            _root?.Clear();
        }

        private void HandleUndoRedoPerformed()
        {
            if (_targetAsset == null) return;

            // 1. Sync the serialized object cache
            if (_serializedObject != null)
            {
                _serializedObject.Update();
            }

            // 2. Re-sort the segments of each channel so ordering and validation stay valid
            _targetAsset.SortAllChannelSegments();
            _targetAsset.EnsureSegmentsValid();

            // 3. Rebuild the action track and animation track elements
            RebuildTracks();
            _additiveTrackElement?.RebuildSegments();
            _fullBodyTrackElement?.RebuildSegments();
            _upperBodyTrackElement?.RebuildSegments();

            // 4. Sync timeline dimensions, grid, ruler, section track, and layers dropdown
            UpdateTimelineLengthsAndSync(fullRebuild: true, markDirty: false, rebuildRuntimeBlocks: true);
            UpdateLayersDropdownVisual();

            // 5. Validate and refresh the selection in the Inspector
            RefreshInspectorSelectionAfterUndo();

            // 6. Re-evaluate and redraw the viewport immediately while paused
            if (!_isPlaying)
            {
                EvaluateTimeAndPreviewLogic(0f);
                _viewport?.RenderImmediate();
            }

            _root?.MarkDirtyRepaint();
            OnAssetModified?.Invoke();
        }

        private void RefreshInspectorSelectionAfterUndo()
        {
            if (_inspector == null || _targetAsset == null) return;

            // Check whether the currently selected ActionBlock still exists
            if (_selectedBlock != null)
            {
                bool blockFound = false;
                int trackIdx = _selectedBlock.TrackIndex;
                if (trackIdx >= 0 && trackIdx < _targetAsset.Tracks.Count)
                {
                    var track = _targetAsset.Tracks[trackIdx];
                    if (track?.ActionBlocks != null && _selectedBlock.BlockIndex >= 0 && _selectedBlock.BlockIndex < track.ActionBlocks.Count)
                    {
                        var data = track.ActionBlocks[_selectedBlock.BlockIndex];
                        if (data == _selectedBlock.Data)
                        {
                            blockFound = true;
                            _inspector.InspectActionBlock(_selectedBlock);
                        }
                    }
                }

                if (!blockFound)
                {
                    _selectedBlock = null;
                    _inspector.ClearActionInspect();
                }
            }

            // Check whether the currently selected AnimationSegment still exists
            if (_selectedSegment != null && _selectedAnimationTrack != null)
            {
                var segments = _targetAsset.GetSegments(_selectedAnimationTrack.Channel);
                if (segments != null && segments.Contains(_selectedSegment))
                {
                    int segIdx = segments.IndexOf(_selectedSegment);
                    _inspector.InspectAnimationSegment(_selectedSegment, segIdx, _targetAsset);
                }
                else
                {
                    _selectedSegment = null;
                    _selectedAnimationTrack = null;
                    _inspector.ClearActionInspect();
                }
            }
        }

        #endregion

        #region UI Construction

        private void BuildUIHierarchy()
        {
            _root = new VisualElement();
            _root.AddToClassList("montage-editor-root");
            _root.focusable = true;

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(USS_GUID));
            if (styleSheet != null)
            {
                _root.styleSheets.Add(styleSheet);
            }

            // 1. Main split view (vertical split: viewport/inspector on top, timeline below)
            var mainSplit = new TwoPaneSplitView(0, 320, TwoPaneSplitViewOrientation.Vertical);
            mainSplit.AddToClassList("montage-main-split");

            // Upper pane (horizontal split: 3D viewport + property inspector)
            var upperSplit = new TwoPaneSplitView(1, 360, TwoPaneSplitViewOrientation.Horizontal);
            upperSplit.AddToClassList("montage-upper-split");

            // 3D viewport container (render area on top, three-part control bar below)
            var viewportContainer = new VisualElement();
            viewportContainer.style.flexGrow = 1;
            viewportContainer.style.flexShrink = 1;
            viewportContainer.style.minWidth = 100;
            viewportContainer.style.flexDirection = FlexDirection.Column;

            _viewport = new MontagePreviewViewportElement();
            _viewport.style.flexGrow = 1;
            _viewport.OnPreviewModelChanged += OnViewportModelChanged;
            _viewport.OnOverrideModelToggled += OnViewportOverrideModelToggled;
            _viewport.OnPreviewBaseMotionChanged += OnViewportBaseMotionChanged;
            _viewport.OnPreviewUpperBodyMaskChanged += OnViewportUpperBodyMaskChanged;
            _viewport.OnRootMotionToggled += OnPreviewRootMotionChanged;
            _viewport.OnFootIKToggled += OnPreviewFootIKChanged;
            _viewport.SetRootMotionState(_previewRootMotion);
            _viewport.SetFootIKState(_previewFootIK);
            viewportContainer.Add(_viewport);

            var playbackBar = CreateViewportPlaybackBar();
            viewportContainer.Add(playbackBar);

            upperSplit.Add(viewportContainer);

            // Sidebar inspector
            _inspector = new MontageActionInspectorElement();
            _inspector.BindAsset(_targetAsset, _serializedObject);
            _inspector.OnAssetSettingsModified += HandleGeneralAssetSettingsModified;
            _inspector.OnAnimationSegmentClipChanged += () =>
            {
                UpdateTimelineLengthsAndSync(fullRebuild: true, markDirty: true, rebuildRuntimeBlocks: true);
            };
            _inspector.OnDataModified += () =>
            {
                _serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(_targetAsset);

                // 1. Adaptively sync the global timeline with all tracks (total duration, viewport extension, ruler ticks, vertical grid, and content indicator line)
                UpdateTimelineLengthsAndSync(fullRebuild: false, markDirty: false, rebuildRuntimeBlocks: true);

                // 2. Immediately rebuild animation track block geometry and adjacent cross-blend diagonals
                _targetAsset.SortAllChannelSegments();
                _additiveTrackElement?.RebuildSegments();
                _fullBodyTrackElement?.RebuildSegments();
                _upperBodyTrackElement?.RebuildSegments();

                // 3. Immediately refresh the appearance and tooltip text of all action track blocks (lightweight visual update only; no DOM teardown, preserving selection)
                for (int i = 0; i < _trackElements.Count; i++)
                {
                    _trackElements[i].UpdateBlocksVisual();
                }

                // 4. Immediately update section track colors and divider lines
                _sectionTrackElement?.SetTargetAsset(_targetAsset, _clipLength, _frameRate, _contentDuration);

                // 5. Redraw the viewport immediately only when paused/static; during playback the next Update tick advances it naturally
                if (!_isPlaying)
                {
                    _viewport?.RenderImmediate();
                }

                OnAssetModified?.Invoke();
            };
            upperSplit.Add(_inspector);

            mainSplit.Add(upperSplit);

            // 2. Lower pane (timeline track area)
            var timelineContainer = CreateTimelineArea();
            mainSplit.Add(timelineContainer);

            _root.Add(mainSplit);
        }

        private VisualElement CreateViewportPlaybackBar()
        {
            var playbackBar = new VisualElement();
            playbackBar.AddToClassList("montage-playback-bar");

            // 1. Left area (time, frame count, and physical section indicator)
            var leftSection = new VisualElement();
            leftSection.AddToClassList("montage-playback-left");

            var timeBox = new VisualElement();
            timeBox.AddToClassList("montage-time-info-box");

            _timeValueLabel = new Label("0.00s / 0.00s");
            _timeValueLabel.AddToClassList("montage-time-val");
            _timeValueLabel.tooltip = "Current Playback Time / Total Clip Duration";
            timeBox.Add(_timeValueLabel);

            var divider = new Label("|");
            divider.AddToClassList("montage-time-divider");
            timeBox.Add(divider);

            _frameValueLabel = new Label("F 0 / 0");
            _frameValueLabel.AddToClassList("montage-frame-val");
            _frameValueLabel.tooltip = "Current Frame / Total Frame Count";
            timeBox.Add(_frameValueLabel);

            _sectionBadgeLabel = new Label("S1");
            _sectionBadgeLabel.AddToClassList("montage-section-info-badge");
            timeBox.Add(_sectionBadgeLabel);

            leftSection.Add(timeBox);
            playbackBar.Add(leftSection);

            // 2. Center area (large centered play/pause and frame step buttons)
            var centerSection = new VisualElement();
            centerSection.AddToClassList("montage-playback-center");

            _stopButton = CreateIconButton("d_Animation.FirstKey", "|<", StopAnimation, "Stop and Reset to Frame 0 (Home)");
            _stopButton.AddToClassList("montage-playback-btn");
            centerSection.Add(_stopButton);

            _prevFrameButton = CreateIconButton("d_Animation.PrevKey", "<", StepPrevFrame, "Previous Frame (, or [)");
            _prevFrameButton.AddToClassList("montage-playback-btn");
            centerSection.Add(_prevFrameButton);

            _playPauseButton = new Button(TogglePlayPause);
            _playPauseButton.AddToClassList("montage-play-center-btn");
            _playPauseButton.tooltip = "Play / Pause (Space)";
            _playPauseIcon = new Image { pickingMode = PickingMode.Ignore };
            _playPauseIcon.style.width = 16;
            _playPauseIcon.style.height = 16;
            _playPauseButton.Add(_playPauseIcon);
            UpdatePlayPauseVisual();
            centerSection.Add(_playPauseButton);

            _nextFrameButton = CreateIconButton("d_Animation.NextKey", ">", StepNextFrame, "Next Frame (. or ])");
            _nextFrameButton.AddToClassList("montage-playback-btn");
            centerSection.Add(_nextFrameButton);

            _lastFrameButton = CreateIconButton("d_Animation.LastKey", ">|", JumpToLastFrame, "Jump to Last Frame (End)");
            _lastFrameButton.AddToClassList("montage-playback-btn");
            centerSection.Add(_lastFrameButton);

            playbackBar.Add(centerSection);

            // 3. Right area (draggable preview speed input and Loop toggle)
            var rightSection = new VisualElement();
            rightSection.AddToClassList("montage-playback-right");

            var speedBox = new VisualElement();
            speedBox.AddToClassList("montage-preview-speed-box");

            var speedLbl = new Label("Speed");
            speedLbl.AddToClassList("montage-preview-label-draggable");
            speedLbl.tooltip = "Drag left/right to adjust preview speed";

            Vector2 dragStartPos = Vector2.zero;
            float dragStartSpeed = 1f;

            speedLbl.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    dragStartPos = evt.mousePosition;
                    dragStartSpeed = _previewSpeed;
                    speedLbl.CaptureMouse();
                    evt.StopPropagation();
                }
            });

            speedLbl.RegisterCallback<MouseMoveEvent>(evt =>
            {
                if (speedLbl.HasMouseCapture())
                {
                    float deltaX = evt.mousePosition.x - dragStartPos.x;
                    float sensitivity = evt.shiftKey ? 0.05f : 0.01f;
                    float newSpeed = Mathf.Max(0.01f, (float)Math.Round(dragStartSpeed + deltaX * sensitivity, 2));
                    _previewSpeed = newSpeed;
                    _playRateField.SetValueWithoutNotify(newSpeed);
                    evt.StopPropagation();
                }
            });

            speedLbl.RegisterCallback<MouseUpEvent>(evt =>
            {
                if (speedLbl.HasMouseCapture())
                {
                    speedLbl.ReleaseMouse();
                    evt.StopPropagation();
                }
            });

            speedBox.Add(speedLbl);

            _playRateField = new FloatField();
            _playRateField.AddToClassList("montage-preview-speed-input");
            _playRateField.value = _previewSpeed;
            _playRateField.RegisterValueChangedCallback(evt =>
            {
                _previewSpeed = Mathf.Max(0.01f, evt.newValue);
            });
            speedBox.Add(_playRateField);

            var speedSuffix = new Label("x");
            speedSuffix.AddToClassList("montage-preview-speed-suffix");
            speedBox.Add(speedSuffix);

            rightSection.Add(speedBox);

            _loopToggle = new Toggle("Loop") { value = _previewLoop };
            _loopToggle.AddToClassList("montage-preview-loop-toggle");
            _loopToggle.tooltip = "Loop preview playback";
            _loopToggle.RegisterValueChangedCallback(evt =>
            {
                _previewLoop = evt.newValue;
            });
            rightSection.Add(_loopToggle);

            playbackBar.Add(rightSection);

            return playbackBar;
        }

        private Button CreateIconButton(string iconName, string fallbackText, Action onClick, string tooltip)
        {
            var btn = new Button(onClick);
            btn.AddToClassList("montage-toolbar-btn");
            btn.tooltip = tooltip;

            var icon = EditorGUIUtility.IconContent(iconName);
            if (icon != null && icon.image != null)
            {
                var img = new Image { image = icon.image, pickingMode = PickingMode.Ignore };
                img.style.width = 14;
                img.style.height = 14;
                btn.Add(img);
            }
            else
            {
                btn.text = fallbackText;
            }
            return btn;
        }

        private VisualElement CreateTimelineArea()
        {
            var timelineContainer = new VisualElement();
            timelineContainer.AddToClassList("montage-timeline-container");

            // -------------------------------------------------------------
            // A. Fixed left track header column (fixed width 220px, never scrolls horizontally)
            // -------------------------------------------------------------
            var leftHeaderColumn = new VisualElement();
            leftHeaderColumn.AddToClassList("montage-timeline-left-column");

            var trackListHeader = new VisualElement();
            trackListHeader.AddToClassList("montage-track-list-header");

            var headerTitle = new Label("Tracks");
            headerTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            headerTitle.style.fontSize = 12;
            headerTitle.style.color = new Color(0.85f, 0.85f, 0.85f);
            trackListHeader.Add(headerTitle);

            var headerRightGroup = new VisualElement();
            headerRightGroup.style.flexDirection = FlexDirection.Row;
            headerRightGroup.style.alignItems = Align.Center;

            // Unified animation layer management dropdown (toggles the UpperBody and Additive channels)
            _layersDropdownBtn = new Button(ShowChannelsMenu) { text = "Layers ▾" };
            _layersDropdownBtn.AddToClassList("montage-toolbar-btn");
            _layersDropdownBtn.style.paddingLeft = 6;
            _layersDropdownBtn.style.paddingRight = 6;
            _layersDropdownBtn.style.height = 20;
            _layersDropdownBtn.style.fontSize = 11;
            _layersDropdownBtn.tooltip = "Manage animation layer tracks (UpperBody, FullBody, Additive)";
            headerRightGroup.Add(_layersDropdownBtn);

            UpdateLayersDropdownVisual();

            var addTrackBtn = new Button(ShowAddTrackDropdownMenu) { text = "+ Track" };
            addTrackBtn.AddToClassList("montage-toolbar-btn");
            addTrackBtn.AddToClassList("montage-primary-btn");
            addTrackBtn.style.marginLeft = 4;
            addTrackBtn.tooltip = "Add Action/Notify track";
            headerRightGroup.Add(addTrackBtn);

            trackListHeader.Add(headerRightGroup);
            leftHeaderColumn.Add(trackListHeader);

            _headersScrollView = new ScrollView(ScrollViewMode.Vertical);
            _headersScrollView.AddToClassList("montage-track-headers-scroll-view");
            _headersScrollView.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _headersScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _headersScrollView.RegisterCallback<WheelEvent>(OnHeadersAreaWheel, TrickleDown.TrickleDown);
            _headersScrollView.RegisterCallback<MouseDownEvent>(OnBlankAreaMouseDown);

            _headersContentWrapper = new VisualElement();
            _headersContentWrapper.AddToClassList("montage-track-headers-content-wrapper");
            _headersContentWrapper.RegisterCallback<MouseDownEvent>(OnBlankAreaMouseDown);

            _reorderIndicatorLine = new VisualElement();
            _reorderIndicatorLine.AddToClassList("montage-track-reorder-line");
            var indicatorDot = new VisualElement();
            indicatorDot.AddToClassList("montage-track-reorder-dot");
            _reorderIndicatorLine.Add(indicatorDot);

            _headersScrollView.Add(_headersContentWrapper);

            leftHeaderColumn.Add(_headersScrollView);
            timelineContainer.Add(leftHeaderColumn);

            // -------------------------------------------------------------
            // B. Right timeline main viewport column (flex: 1, scrolls both horizontally and vertically)
            // -------------------------------------------------------------
            var rightTimelineColumn = new VisualElement();
            rightTimelineColumn.AddToClassList("montage-timeline-right-column");

            // Top ruler (clean time scale ruler)
            _ruler = new MontageTimelineRuler();
            _ruler.OnTimeScrubbed += ScrubToTime;
            _ruler.OnPanDelta += PanTimeline;
            _ruler.OnZoomDelta += delta => HandleWheelZoomDelta(delta, null);
            rightTimelineColumn.Add(_ruler);

            // Standalone section track element
            _sectionTrackElement = new MontageSectionTrackElement(
                _targetAsset,
                _clipLength,
                _frameRate,
                _zoomLevel
            );
            _sectionTrackElement.OnSplitAdded += AddSplitTimestamp;
            _sectionTrackElement.OnSplitMovedLive += (idx, time) =>
            {
                for (int i = 0; i < _trackElements.Count; i++)
                {
                    _trackElements[i].ContentElement.MarkDirtyRepaint();
                }
                UpdateToolbarTimeDisplay();
            };
            _sectionTrackElement.OnSplitMoved += MoveSplitTimestamp;
            _sectionTrackElement.OnSplitRemoved += RemoveSplitTimestamp;

            // Standalone animation track elements for the three humanoid channels
            _additiveTrackElement = new MontageAnimationTrackElement(
                _targetAsset,
                _clipLength,
                _frameRate,
                _zoomLevel,
                MontageLayerChannel.Additive
            );
            BindAnimationTrackCallbacks(_additiveTrackElement);

            _fullBodyTrackElement = new MontageAnimationTrackElement(
                _targetAsset,
                _clipLength,
                _frameRate,
                _zoomLevel,
                MontageLayerChannel.FullBody
            );
            BindAnimationTrackCallbacks(_fullBodyTrackElement);

            _upperBodyTrackElement = new MontageAnimationTrackElement(
                _targetAsset,
                _clipLength,
                _frameRate,
                _zoomLevel,
                MontageLayerChannel.UpperBody
            );
            BindAnimationTrackCallbacks(_upperBodyTrackElement);

            // Track content area
            _tracksScrollView = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            _tracksScrollView.AddToClassList("montage-tracks-scroll-view");
            _tracksScrollView.RegisterCallback<MouseDownEvent>(OnBlankAreaMouseDown);
            _tracksScrollView.RegisterCallback<WheelEvent>(OnTimelineAreaWheel, TrickleDown.TrickleDown);
            _tracksScrollView.RegisterCallback<GeometryChangedEvent>(OnTracksGeometryChanged);

            // Scroll synchronization bindings
            _tracksScrollView.verticalScroller.valueChanged += val =>
            {
                _headersScrollView.verticalScroller.value = val;
            };
            _tracksScrollView.horizontalScroller.valueChanged += val =>
            {
                _ruler.SetHorizontalScrollOffset(val);
            };

            _tracksContentWrapper = new VisualElement();
            _tracksContentWrapper.AddToClassList("montage-tracks-content-wrapper");
            _tracksContentWrapper.RegisterCallback<MouseDownEvent>(OnBlankAreaMouseDown);
            _tracksScrollView.Add(_tracksContentWrapper);

            rightTimelineColumn.Add(_tracksScrollView);
            timelineContainer.Add(rightTimelineColumn);

            return timelineContainer;
        }

        #endregion

        #region Animation and Playable Initialization

        private void InitializePreviewAndPlayables()
        {
            CleanupPlayablesAndPreview();

            // Resolve the preview model preset and base motion (user-assigned -> EditorPrefs memory -> built-in fallback asset)
            _currentPreviewPrefab = ResolvePreviewModel();
            _currentPreviewBaseMotion = ResolvePreviewBaseMotion();
            _viewport.Initialize(_targetAsset, _currentPreviewPrefab, ref _previewObject);
            _viewport.SetSelectedBaseMotion(_currentPreviewBaseMotion);
            _viewport.SetOverrideModelState(_targetAsset != null && _targetAsset.OverridePreviewModel);
            _viewport.SetSelectedUpperBodyMask(ResolvePreviewUpperBodyMask());

            _contentDuration = _targetAsset != null ? _targetAsset.TotalDuration : 0f;
            var (initTotalWidth, initClipLength) = CalculateTimelineDimensions();
            _clipLength = initClipLength;
            _frameRate = _targetAsset != null ? Mathf.Max(1f, _targetAsset.FrameRate) : 30f;
            _totalFrames = Mathf.Max(1, Mathf.RoundToInt(_contentDuration * _frameRate));

            _ruler.SetTimelineData(_targetAsset, _animationTime, _clipLength, _frameRate, _zoomLevel);
            _ruler.SetContentDuration(_contentDuration);
            if (_tracksContentWrapper != null)
            {
                _tracksContentWrapper.style.width = initTotalWidth;
            }

            if (_targetAsset == null)
            {
                RebuildRuntimeActionBlocks(forceRecreate: true);
                _viewport.RenderImmediate();
                return;
            }

            if (_previewObject != null)
            {
                _previewAnimator = _previewObject.GetComponent<Animator>() ?? _previewObject.AddComponent<Animator>();
                _previewAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _previewAnimator.applyRootMotion = _previewRootMotion;

                _playableGraph = PlayableGraph.Create("CwcMontageEditorPreviewGraph");
                _playableGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

                // Fixed four-layer topology:
                // Input 0: Locomotion / Base Motion (looping idle/run, etc.)
                // Input 1: UpperBody (built-in generic Humanoid AvatarMask)
                // Input 2: FullBody (authoritative override)
                // Input 3: Additive (hit reactions/overlays)
                _topLevelPreviewMixer = AnimationLayerMixerPlayable.Create(_playableGraph, 4);

                // 0. Base Motion channel setup (Input 0)
                if (_currentPreviewBaseMotion != null)
                {
                    _baseMotionPlayable = AnimationClipPlayable.Create(_playableGraph, _currentPreviewBaseMotion);
                    _baseMotionPlayable.SetApplyFootIK(_previewFootIK);
                    _baseMotionPlayable.SetSpeed(1.0f);
                    _topLevelPreviewMixer.ConnectInput(0, _baseMotionPlayable, 0);
                    _topLevelPreviewMixer.SetInputWeight(0, 1.0f);
                }
                else
                {
                    _baseMotionPlayable = default;
                    _topLevelPreviewMixer.SetInputWeight(0, 0.0f);
                }

                // 1. UpperBody channel setup (resolve a custom or built-in humanoid mask)
                var upperMask = ResolvePreviewUpperBodyMask();
                _topLevelPreviewMixer.SetLayerMaskFromAvatarMask(1, upperMask);
                _topLevelPreviewMixer.SetLayerAdditive(1, false);

                var upperSegments = _targetAsset.UpperBodySegments;
                int upperCount = upperSegments != null ? upperSegments.Count : 0;
                _upperBodyMixer = AnimationMixerPlayable.Create(_playableGraph, Mathf.Max(1, upperCount));
                _upperBodySegmentPlayables.Clear();
                for (int s = 0; s < upperCount; s++)
                {
                    var seg = upperSegments[s];
                    var cp = seg?.Clip != null ? AnimationClipPlayable.Create(_playableGraph, seg.Clip) : default;
                    if (cp.IsValid())
                    {
                        cp.SetApplyFootIK(_previewFootIK);
                        cp.SetSpeed(1.0f);
                        _upperBodyMixer.ConnectInput(s, cp, 0);
                    }
                    _upperBodyMixer.SetInputWeight(s, s == 0 ? 1.0f : 0.0f);
                    _upperBodySegmentPlayables.Add(cp);
                }
                _topLevelPreviewMixer.ConnectInput(1, _upperBodyMixer, 0);
                _topLevelPreviewMixer.SetInputWeight(1, 0.0f);

                // 2. FullBody channel setup
                _topLevelPreviewMixer.SetLayerAdditive(2, false);
                var fullSegments = _targetAsset.FullBodySegments;
                int fullCount = fullSegments != null ? fullSegments.Count : 0;
                _fullBodyMixer = AnimationMixerPlayable.Create(_playableGraph, Mathf.Max(1, fullCount));
                _fullBodySegmentPlayables.Clear();
                for (int s = 0; s < fullCount; s++)
                {
                    var seg = fullSegments[s];
                    var cp = seg?.Clip != null ? AnimationClipPlayable.Create(_playableGraph, seg.Clip) : default;
                    if (cp.IsValid())
                    {
                        cp.SetApplyFootIK(_previewFootIK);
                        cp.SetSpeed(1.0f);
                        _fullBodyMixer.ConnectInput(s, cp, 0);
                    }
                    _fullBodyMixer.SetInputWeight(s, s == 0 ? 1.0f : 0.0f);
                    _fullBodySegmentPlayables.Add(cp);
                }
                _topLevelPreviewMixer.ConnectInput(2, _fullBodyMixer, 0);
                _topLevelPreviewMixer.SetInputWeight(2, 0.0f);

                // 3. Additive channel setup
                _topLevelPreviewMixer.SetLayerAdditive(3, true);
                var addSegments = _targetAsset.AdditiveSegments;
                int addCount = addSegments != null ? addSegments.Count : 0;
                _additiveMixer = AnimationMixerPlayable.Create(_playableGraph, Mathf.Max(1, addCount));
                _additiveSegmentPlayables.Clear();
                for (int s = 0; s < addCount; s++)
                {
                    var seg = addSegments[s];
                    var cp = seg?.Clip != null ? AnimationClipPlayable.Create(_playableGraph, seg.Clip) : default;
                    if (cp.IsValid())
                    {
                        cp.SetApplyFootIK(false);
                        cp.SetSpeed(1.0f);
                        _additiveMixer.ConnectInput(s, cp, 0);
                    }
                    _additiveMixer.SetInputWeight(s, s == 0 ? 1.0f : 0.0f);
                    _additiveSegmentPlayables.Add(cp);
                }
                _topLevelPreviewMixer.ConnectInput(3, _additiveMixer, 0);
                _topLevelPreviewMixer.SetInputWeight(3, 0.0f);

                _playableOutput = AnimationPlayableOutput.Create(_playableGraph, "Animation", _previewAnimator);
                _playableOutput.SetSourcePlayable(_topLevelPreviewMixer);

                EvaluatePreviewPlayables();
                _playableGraph.Evaluate();
            }

            RebuildRuntimeActionBlocks(forceRecreate: true);
            _viewport.RenderImmediate();
        }

        private void EvaluatePreviewPlayables()
        {
            if (!_topLevelPreviewMixer.IsValid() || _targetAsset == null) return;

            // 0. Evaluate the looping base motion (Input 0)
            if (_baseMotionPlayable.IsValid() && _currentPreviewBaseMotion != null)
            {
                float baseDuration = Mathf.Max(0.001f, _currentPreviewBaseMotion.length);
                float baseLoopTime = _animationTime % baseDuration;
                _baseMotionPlayable.SetTime(baseLoopTime);
                _baseMotionPlayable.SetSpeed(1.0f);
                _topLevelPreviewMixer.SetInputWeight(0, 1.0f);
            }
            else
            {
                _topLevelPreviewMixer.SetInputWeight(0, 0.0f);
            }

            // 1. Evaluate FullBody (Layer 2)
            float fullBodyWeight = 0f;
            if (_fullBodyMixer.IsValid() && _fullBodySegmentPlayables.Count > 0)
            {
                _targetAsset.EvaluateChannelSegments(MontageLayerChannel.FullBody, _animationTime, _tempEvalIndices, _tempEvalTimes, _tempEvalWeights);

                int total = _fullBodySegmentPlayables.Count;
                for (int i = 0; i < total; i++)
                {
                    _fullBodyMixer.SetInputWeight(i, 0.0f);
                }

                float totalSegWeight = 0f;
                for (int w = 0; w < _tempEvalWeights.Count; w++)
                {
                    totalSegWeight += _tempEvalWeights[w];
                }

                // [Core fix]: normalize child mixer input weights strictly to 1.0f so AnimationMixerPlayable never auto-fills a BindPose 0 pose
                for (int k = 0; k < _tempEvalIndices.Count; k++)
                {
                    int segIdx = _tempEvalIndices[k];
                    if (segIdx >= 0 && segIdx < total)
                    {
                        var cp = _fullBodySegmentPlayables[segIdx];
                        if (cp.IsValid())
                        {
                            cp.SetTime(_tempEvalTimes[k]);
                            cp.SetSpeed(1.0f);
                        }
                        float normalizedW = totalSegWeight > 0.0001f ? (_tempEvalWeights[k] / totalSegWeight) : 0f;
                        _fullBodyMixer.SetInputWeight(segIdx, normalizedW);
                    }
                }

                fullBodyWeight = Mathf.Clamp01(totalSegWeight) * _targetAsset.FullBodyWeight;
            }
            _topLevelPreviewMixer.SetInputWeight(2, fullBodyWeight);

            // 2. Evaluate UpperBody (Layer 1)
            float upperBodyWeight = 0f;
            if (_targetAsset.EnableUpperBody && _upperBodyMixer.IsValid() && _upperBodySegmentPlayables.Count > 0)
            {
                _targetAsset.EvaluateChannelSegments(MontageLayerChannel.UpperBody, _animationTime, _tempEvalIndices, _tempEvalTimes, _tempEvalWeights);

                int total = _upperBodySegmentPlayables.Count;
                for (int i = 0; i < total; i++)
                {
                    _upperBodyMixer.SetInputWeight(i, 0.0f);
                }

                float totalSegWeight = 0f;
                for (int w = 0; w < _tempEvalWeights.Count; w++)
                {
                    totalSegWeight += _tempEvalWeights[w];
                }

                for (int k = 0; k < _tempEvalIndices.Count; k++)
                {
                    int segIdx = _tempEvalIndices[k];
                    if (segIdx >= 0 && segIdx < total)
                    {
                        var cp = _upperBodySegmentPlayables[segIdx];
                        if (cp.IsValid())
                        {
                            cp.SetTime(_tempEvalTimes[k]);
                            cp.SetSpeed(1.0f);
                        }
                        float normalizedW = totalSegWeight > 0.0001f ? (_tempEvalWeights[k] / totalSegWeight) : 0f;
                        _upperBodyMixer.SetInputWeight(segIdx, normalizedW);
                    }
                }

                upperBodyWeight = Mathf.Clamp01(totalSegWeight) * _targetAsset.UpperBodyWeight;
            }
            // Empty regions release their weight to zero and hand back control without locking the first/last frame pose
            _topLevelPreviewMixer.SetInputWeight(1, upperBodyWeight);

            // 3. Evaluate Additive (Layer 3)
            float additiveWeight = 0f;
            if (_targetAsset.EnableAdditive && _additiveMixer.IsValid() && _additiveSegmentPlayables.Count > 0)
            {
                _targetAsset.EvaluateChannelSegments(MontageLayerChannel.Additive, _animationTime, _tempEvalIndices, _tempEvalTimes, _tempEvalWeights);

                int total = _additiveSegmentPlayables.Count;
                for (int i = 0; i < total; i++)
                {
                    _additiveMixer.SetInputWeight(i, 0.0f);
                }

                float totalSegWeight = 0f;
                for (int w = 0; w < _tempEvalWeights.Count; w++)
                {
                    totalSegWeight += _tempEvalWeights[w];
                }

                for (int k = 0; k < _tempEvalIndices.Count; k++)
                {
                    int segIdx = _tempEvalIndices[k];
                    if (segIdx >= 0 && segIdx < total)
                    {
                        var cp = _additiveSegmentPlayables[segIdx];
                        if (cp.IsValid())
                        {
                            cp.SetTime(_tempEvalTimes[k]);
                            cp.SetSpeed(1.0f);
                        }
                        float normalizedW = totalSegWeight > 0.0001f ? (_tempEvalWeights[k] / totalSegWeight) : 0f;
                        _additiveMixer.SetInputWeight(segIdx, normalizedW);
                    }
                }

                additiveWeight = Mathf.Clamp01(totalSegWeight) * _targetAsset.AdditiveWeight;
            }
            _topLevelPreviewMixer.SetInputWeight(3, additiveWeight);
        }

        private void ApplyUpperBodySpineDecouplingPreview()
        {
            if (_previewAnimator == null || !_previewAnimator.isHuman || _targetAsset == null || !_targetAsset.DecoupleUpperBodyOrientation) return;
            // Spine decoupling only applies to the generic procedural standard mask; if the viewport or asset defines a custom mask, defer to it entirely and never override it
            if (_currentPreviewUpperBodyMask != null || _targetAsset.CustomUpperBodyMask != null) return;
            if (!_targetAsset.EnableUpperBody || !_upperBodyMixer.IsValid() || _upperBodySegmentPlayables.Count == 0) return;

            // 0. If FullBody is currently playing with valid weight, FullBody is authoritative — never modify the Spine pose!
            if (_fullBodyMixer.IsValid() && _fullBodySegmentPlayables.Count > 0)
            {
                _targetAsset.EvaluateChannelSegments(MontageLayerChannel.FullBody, _animationTime, _tempEvalIndices, _tempEvalTimes, _tempEvalWeights);
                if (_tempEvalIndices.Count > 0)
                {
                    float fullWeight = 0f;
                    for (int w = 0; w < _tempEvalWeights.Count; w++) fullWeight += _tempEvalWeights[w];
                    if (fullWeight > 0.0001f)
                    {
                        return; // full-body animation is dominant, so exit early
                    }
                }
            }

            var upperSegments = _targetAsset.UpperBodySegments;
            if (upperSegments == null || upperSegments.Count == 0) return;

            Transform root = _previewAnimator != null ? _previewAnimator.transform : (_previewObject != null ? _previewObject.transform : null);
            Transform hips = _previewAnimator.GetBoneTransform(HumanBodyBones.Hips);
            Transform spine = _previewAnimator.GetBoneTransform(HumanBodyBones.Spine);
            if (root == null || hips == null || spine == null) return;

            _targetAsset.EvaluateChannelSegments(MontageLayerChannel.UpperBody, _animationTime, _tempEvalIndices, _tempEvalTimes, _tempEvalWeights);
            if (_tempEvalIndices.Count == 0) return;

            Quaternion targetSpineInRoot = Quaternion.identity;
            float totalWeight = 0f;

            for (int k = 0; k < _tempEvalIndices.Count; k++)
            {
                int segIdx = _tempEvalIndices[k];
                if (segIdx >= 0 && segIdx < upperSegments.Count)
                {
                    var seg = upperSegments[segIdx];
                    if (seg?.Clip != null)
                    {
                        var track = MontageSpineDecoupleUtility.GetOrCreateTrack(seg.Clip, _previewAnimator);
                        if (track != null)
                        {
                            Quaternion sample = track.Evaluate(_tempEvalTimes[k]);
                            float w = _tempEvalWeights[k];
                            if (totalWeight <= 0.0001f)
                            {
                                targetSpineInRoot = sample;
                                totalWeight = w;
                            }
                            else
                            {
                                float blendT = w / (totalWeight + w);
                                targetSpineInRoot = Quaternion.Slerp(targetSpineInRoot, sample, blendT);
                                totalWeight += w;
                            }
                        }
                    }
                }
            }

            if (totalWeight <= 0.0001f) return;

            // Core decoupling solve: compute the base local rotation of the Spine under the current Hips so the Spine's world orientation is exactly locked to targetSpineInRoot in character root space
            Quaternion targetSpineWorld = root.rotation * targetSpineInRoot;
            Quaternion decoupledLocal = Quaternion.Inverse(hips.rotation) * targetSpineWorld;

            // Additive layer pose delta: keep decoupling limited to UpperBody during preview while smoothly layering additive hit/fire shakes on top of the decoupled pose
            Quaternion finalDecoupledLocal = decoupledLocal;
            if (_targetAsset.EnableAdditive && _additiveMixer.IsValid() && _additiveSegmentPlayables.Count > 0)
            {
                var addSegments = _targetAsset.AdditiveSegments;
                if (addSegments != null && addSegments.Count > 0)
                {
                    _targetAsset.EvaluateChannelSegments(MontageLayerChannel.Additive, _animationTime, _tempEvalIndices, _tempEvalTimes, _tempEvalWeights);
                    if (_tempEvalIndices.Count > 0)
                    {
                        Quaternion additiveSpineDelta = Quaternion.identity;
                        float addTotalWeight = 0f;

                        for (int a = 0; a < _tempEvalIndices.Count; a++)
                        {
                            int addSegIdx = _tempEvalIndices[a];
                            if (addSegIdx >= 0 && addSegIdx < addSegments.Count)
                            {
                                var seg = addSegments[addSegIdx];
                                if (seg?.Clip != null)
                                {
                                    var addTrack = MontageSpineDecoupleUtility.GetOrCreateAdditiveTrack(seg.Clip, _previewAnimator);
                                    if (addTrack != null)
                                    {
                                        Quaternion sample = addTrack.Evaluate(_tempEvalTimes[a]);
                                        float w = _tempEvalWeights[a];
                                        if (addTotalWeight <= 0.0001f)
                                        {
                                            additiveSpineDelta = sample;
                                            addTotalWeight = w;
                                        }
                                        else
                                        {
                                            float blendT = w / (addTotalWeight + w);
                                            additiveSpineDelta = Quaternion.Slerp(additiveSpineDelta, sample, blendT);
                                            addTotalWeight += w;
                                        }
                                    }
                                }
                            }
                        }

                        if (addTotalWeight > 0.0001f)
                        {
                            float effectiveAddWeight = Mathf.Clamp01(addTotalWeight * _targetAsset.AdditiveWeight);
                            Quaternion appliedDelta = Quaternion.Slerp(Quaternion.identity, additiveSpineDelta, effectiveAddWeight);
                            finalDecoupledLocal = decoupledLocal * appliedDelta;
                        }
                    }
                }
            }

            float blendWeight = Mathf.Clamp01(totalWeight * _targetAsset.UpperBodyWeight);
            spine.localRotation = Quaternion.Slerp(spine.localRotation, finalDecoupledLocal, blendWeight);
        }

        private void HandleAnimationTrackModified()
        {
            bool structureChanged =
                CheckChannelStructureChanged(_targetAsset?.UpperBodySegments, _upperBodySegmentPlayables) ||
                CheckChannelStructureChanged(_targetAsset?.FullBodySegments, _fullBodySegmentPlayables) ||
                CheckChannelStructureChanged(_targetAsset?.AdditiveSegments, _additiveSegmentPlayables);

            UpdateTimelineLengthsAndSync(fullRebuild: structureChanged);

            if (!_isPlaying)
            {
                EvaluateTimeAndPreviewLogic(0f);
                _viewport?.RenderImmediate();
            }
        }

        private static bool CheckChannelStructureChanged(List<MontageAnimationSegment> segments, List<AnimationClipPlayable> playables)
        {
            int segCount = segments != null ? segments.Count : 0;
            if (playables.Count != segCount) return true;
            if (segments != null)
            {
                for (int i = 0; i < segCount; i++)
                {
                    var seg = segments[i];
                    var cp = playables[i];
                    if (seg?.Clip == null && cp.IsValid()) return true;
                    if (seg?.Clip != null && (!cp.IsValid() || cp.GetAnimationClip() != seg.Clip)) return true;
                }
            }
            return false;
        }

        private void BindAnimationTrackCallbacks(MontageAnimationTrackElement trackElement)
        {
            trackElement.OnSegmentSelected += (seg, idx) =>
            {
                if (_additiveTrackElement != trackElement) _additiveTrackElement?.ClearSelection();
                if (_fullBodyTrackElement != trackElement) _fullBodyTrackElement?.ClearSelection();
                if (_upperBodyTrackElement != trackElement) _upperBodyTrackElement?.ClearSelection();

                _selectedSegment = seg;
                _selectedAnimationTrack = trackElement;

                _selectedBlock?.SetSelected(false);
                _selectedBlock = null;
                _selectedBlockData = null;
                _inspector.InspectAnimationSegment(seg, idx, _targetAsset);
            };
            trackElement.OnDataModified += HandleAnimationTrackModified;
            trackElement.OnRequestScrubTime += ScrubToTime;
            trackElement.RequestTargetTrack = GetAnimationTrackAtPosition;
            trackElement.RequestCrossTrackMove = HandleCrossTrackSegmentMove;
            trackElement.OnTrackDropHighlightChanged = HandleTrackDropHighlightChanged;
        }

        private MontageAnimationTrackElement GetAnimationTrackAtPosition(Vector2 worldPos)
        {
            // Hit-test against the animation tracks visible in the viewport
            if (_targetAsset != null && _targetAsset.EnableAdditive && _additiveTrackElement != null)
            {
                var bound = _additiveTrackElement.ContentElement.worldBound;
                if (worldPos.y >= bound.yMin && worldPos.y <= bound.yMax)
                {
                    return _additiveTrackElement;
                }
            }

            if (_fullBodyTrackElement != null)
            {
                var bound = _fullBodyTrackElement.ContentElement.worldBound;
                if (worldPos.y >= bound.yMin && worldPos.y <= bound.yMax)
                {
                    return _fullBodyTrackElement;
                }
            }

            if (_targetAsset != null && _targetAsset.EnableUpperBody && _upperBodyTrackElement != null)
            {
                var bound = _upperBodyTrackElement.ContentElement.worldBound;
                if (worldPos.y >= bound.yMin && worldPos.y <= bound.yMax)
                {
                    return _upperBodyTrackElement;
                }
            }

            return null;
        }

        private void HandleTrackDropHighlightChanged(MontageAnimationTrackElement activeTrack, bool isHighlighted)
        {
            if (_additiveTrackElement != null && _additiveTrackElement != activeTrack)
            {
                _additiveTrackElement.SetDropHighlight(false);
            }
            if (_fullBodyTrackElement != null && _fullBodyTrackElement != activeTrack)
            {
                _fullBodyTrackElement.SetDropHighlight(false);
            }
            if (_upperBodyTrackElement != null && _upperBodyTrackElement != activeTrack)
            {
                _upperBodyTrackElement.SetDropHighlight(false);
            }
        }

        private void HandleCrossTrackSegmentMove(
            MontageAnimationTrackElement sourceTrack,
            MontageAnimationTrackElement targetTrack,
            MontageAnimationSegment segment,
            float targetStartTime)
        {
            if (_targetAsset == null || sourceTrack == null || targetTrack == null || segment == null) return;
            if (sourceTrack == targetTrack) return;

            Undo.RecordObject(_targetAsset, $"Move Segment to {targetTrack.Channel}");

            float frameInterval = 1f / Mathf.Max(1f, _frameRate);
            float snappedTime = Mathf.Round(Mathf.Max(0f, targetStartTime) / frameInterval) * frameInterval;
            segment.StartTime = snappedTime;

            _targetAsset.MoveSegmentChannel(segment, sourceTrack.Channel, targetTrack.Channel);

            _targetAsset.SortChannelSegments(sourceTrack.Channel);
            _targetAsset.SortChannelSegments(targetTrack.Channel);
            _targetAsset.EnsureSegmentsValid();

            sourceTrack.RebuildSegments();
            targetTrack.RebuildSegments();

            targetTrack.SelectSegment(segment);
            _selectedSegment = segment;
            _selectedAnimationTrack = targetTrack;

            EditorUtility.SetDirty(_targetAsset);
            HandleAnimationTrackModified();
        }

        private void PasteCopiedSegment(MontageAnimationTrackElement targetTrack, float targetTime)
        {
            var track = targetTrack ?? _selectedAnimationTrack ?? _fullBodyTrackElement;
            if (track != null)
            {
                track.PasteCopiedSegmentAt(targetTime);
                _selectedAnimationTrack = track;
                _selectedSegment = track.SelectedSegment;
            }
        }

        private void UpdateLayersDropdownVisual()
        {
            if (_layersDropdownBtn == null || _targetAsset == null) return;

            int activeCount = 1; // FullBody is the core master layer, always on by default
            if (_targetAsset.EnableUpperBody) activeCount++;
            if (_targetAsset.EnableAdditive) activeCount++;

            _layersDropdownBtn.text = $"Layers ({activeCount}/3) ▾";
        }

        private void ShowChannelsMenu()
        {
            if (_targetAsset == null) return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Layer 3: Additive (Tremor & Overlay)"), _targetAsset.EnableAdditive, () => ToggleChannel(MontageLayerChannel.Additive));
            menu.AddDisabledItem(new GUIContent("Layer 2: FullBody (Master - Always Active)"), true);
            menu.AddItem(new GUIContent("Layer 1: UpperBody (Masked Locomotion)"), _targetAsset.EnableUpperBody, () => ToggleChannel(MontageLayerChannel.UpperBody));
            menu.ShowAsContext();
        }

        private void ToggleChannel(MontageLayerChannel channel)
        {
            if (_targetAsset == null) return;
            Undo.RecordObject(_targetAsset, "Toggle Channel Track");
            if (channel == MontageLayerChannel.Additive)
            {
                _targetAsset.EnableAdditive = !_targetAsset.EnableAdditive;
            }
            else if (channel == MontageLayerChannel.UpperBody)
            {
                _targetAsset.EnableUpperBody = !_targetAsset.EnableUpperBody;
            }
            EditorUtility.SetDirty(_targetAsset);
            UpdateLayersDropdownVisual();
            RebuildTracks();
            InitializePreviewAndPlayables();
            OnAssetModified?.Invoke();
        }

        private float GetViewportWidth()
        {
            float vpWidth = 1000f;
            if (_tracksScrollView != null)
            {
                float w = _tracksScrollView.contentViewport?.resolvedStyle.width ?? 0f;
                if (float.IsNaN(w) || w <= 0f)
                {
                    w = _tracksScrollView.resolvedStyle.width;
                }
                if (!float.IsNaN(w) && w > 0f)
                {
                    vpWidth = w;
                }
            }
            return vpWidth;
        }

        private (float totalWidth, float clipLength) CalculateTimelineDimensions(float? predictedScrollX = null)
        {
            float pps = 200f * _zoomLevel;
            float vpWidth = GetViewportWidth();
            float currentScrollX = predictedScrollX ?? (_tracksScrollView != null ? _tracksScrollView.scrollOffset.x : 0f);

            float contentPixels = _contentDuration * pps;
            float marginPixels = Mathf.Max(150f, vpWidth * 0.15f);

            // Core formula for the total canvas width:
            // 1. Zoomed out: fill at least the current viewport width, eliminating the right-side dead zone and 450-frame truncation so the grid and ticks span the whole screen and the scrollbar sits at 100%;
            // 2. Zoomed in: base the width strictly on the effective content length contentPixels + marginPixels so the scrollbar thumb ratio reflects the actual animation length;
            // 3. Panning right: extend adaptively as the viewport scrolls right so the view never hits a wall.
            float totalWidth = Mathf.Max(vpWidth, Mathf.Max(contentPixels + marginPixels, currentScrollX + vpWidth));
            float clipLength = totalWidth / pps;

            return (totalWidth, Mathf.Max(clipLength, 1.0f));
        }

        /// <summary>
        /// Globally updates the timeline duration, extends the viewport indefinitely, and synchronizes all tracks and the ruler.
        /// </summary>
        private void UpdateTimelineLengthsAndSync(bool fullRebuild = false, bool markDirty = true, bool rebuildRuntimeBlocks = true, float? predictedScrollX = null)
        {
            if (_serializedObject != null)
            {
                _serializedObject.Update();
            }

            if (_targetAsset == null) return;

            float contentDuration = _targetAsset.TotalDuration;
            _contentDuration = contentDuration;

            var (totalWidth, clipLength) = CalculateTimelineDimensions(predictedScrollX);
            _clipLength = clipLength;
            _frameRate = Mathf.Max(1f, _targetAsset.FrameRate);
            _totalFrames = Mathf.Max(1, Mathf.RoundToInt(_contentDuration * _frameRate));

            _ruler?.SetTimelineData(_targetAsset, _animationTime, _clipLength, _frameRate, _zoomLevel);
            _ruler?.SetContentDuration(_contentDuration);

            _sectionTrackElement?.SetTargetAsset(_targetAsset, _clipLength, _frameRate, _contentDuration);
            _sectionTrackElement?.SetZoom(_zoomLevel);

            _additiveTrackElement?.SetTargetAsset(_targetAsset, _clipLength, _frameRate, _contentDuration);
            _additiveTrackElement?.SetZoom(_zoomLevel);

            _fullBodyTrackElement?.SetTargetAsset(_targetAsset, _clipLength, _frameRate, _contentDuration);
            _fullBodyTrackElement?.SetZoom(_zoomLevel);

            _upperBodyTrackElement?.SetTargetAsset(_targetAsset, _clipLength, _frameRate, _contentDuration);
            _upperBodyTrackElement?.SetZoom(_zoomLevel);

            for (int i = 0; i < _trackElements.Count; i++)
            {
                _trackElements[i].UpdateTimelineConfig(_clipLength, _frameRate, _contentDuration, _animationTime);
                _trackElements[i].SetZoom(_zoomLevel);
            }

            if (_tracksContentWrapper != null)
            {
                _tracksContentWrapper.style.width = totalWidth;
            }

            UpdateToolbarTimeDisplay();

            if (fullRebuild)
            {
                InitializePreviewAndPlayables();
            }
            else if (rebuildRuntimeBlocks)
            {
                RebuildRuntimeActionBlocks();
            }

            if (!_isPlaying)
            {
                EvaluateTimeAndPreviewLogic(0f);
                _viewport?.RenderImmediate();
            }

            if (markDirty)
            {
                EditorUtility.SetDirty(_targetAsset);
                OnAssetModified?.Invoke();
            }
        }

        private void HandleGeneralAssetSettingsModified()
        {
            if (_serializedObject != null)
            {
                _serializedObject.Update();
            }

            EditorUtility.SetDirty(_targetAsset);

            // Synchronize global timeline dimensions, viewport extension, and ruler ticks
            UpdateTimelineLengthsAndSync(fullRebuild: false, markDirty: false, rebuildRuntimeBlocks: false);

            // Sync only the necessary component properties — never destroy the PlayableGraph or model, nor interrupt playback
            if (_targetAsset != null)
            {
                if (_previewAnimator != null)
                {
                    _previewAnimator.applyRootMotion = _previewRootMotion;
                }

                for (int i = 0; i < _upperBodySegmentPlayables.Count; i++)
                {
                    if (_upperBodySegmentPlayables[i].IsValid())
                    {
                        _upperBodySegmentPlayables[i].SetApplyFootIK(_previewFootIK);
                    }
                }

                for (int i = 0; i < _fullBodySegmentPlayables.Count; i++)
                {
                    if (_fullBodySegmentPlayables[i].IsValid())
                    {
                        _fullBodySegmentPlayables[i].SetApplyFootIK(_previewFootIK);
                    }
                }

                _sectionTrackElement?.SetTargetAsset(_targetAsset, _clipLength, _frameRate, _contentDuration);
                _additiveTrackElement?.SetTargetAsset(_targetAsset, _clipLength, _frameRate, _contentDuration);
                _fullBodyTrackElement?.SetTargetAsset(_targetAsset, _clipLength, _frameRate, _contentDuration);
                _upperBodyTrackElement?.SetTargetAsset(_targetAsset, _clipLength, _frameRate, _contentDuration);

                UpdateLayersDropdownVisual();
                RebuildTracks();
            }

            if (!_isPlaying)
            {
                EvaluateTimeAndPreviewLogic(0f);
                _viewport?.RenderImmediate();
            }

            OnAssetModified?.Invoke();
        }

        private GameObject ResolvePreviewModel()
        {
            // 0. If the montage asset enables a dedicated preview model override and a model is assigned
            if (_targetAsset != null && _targetAsset.OverridePreviewModel && _targetAsset.PreviewModel != null)
            {
                return _targetAsset.PreviewModel;
            }

            // 1. If the current session already has an explicit assignment
            if (_currentPreviewPrefab != null)
            {
                return _currentPreviewPrefab;
            }

            // 2. Load from global EditorPrefs memory (a model explicitly chosen and persisted by the user)
            string savedGuid = EditorPrefs.GetString(PREVIEW_MODEL_PREFS_KEY, "");
            // If the local cache still points to a legacy model, reset it to the latest default model
            if (savedGuid == "48d95c910780a8343ae08eee4cf18f1c" || 
                savedGuid == "e05b8732575271b4aab0e4e700f2c05f" || 
                savedGuid == "9b367696fbf6a204c8520223412179fb")
            {
                savedGuid = DEFAULT_DUMMY_MODEL_GUID;
                EditorPrefs.SetString(PREVIEW_MODEL_PREFS_KEY, savedGuid);
            }

            if (!string.IsNullOrEmpty(savedGuid))
            {
                string path = AssetDatabase.GUIDToAssetPath(savedGuid);
                if (!string.IsNullOrEmpty(path))
                {
                    var savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (savedPrefab != null)
                    {
                        return savedPrefab;
                    }
                }
            }

            // 3. Plugin-specific built-in default model (resolved by fixed GUID, adapting seamlessly if the plugin folder moves)
            string defaultPath = AssetDatabase.GUIDToAssetPath(DEFAULT_DUMMY_MODEL_GUID);
            if (!string.IsNullOrEmpty(defaultPath))
            {
                var defaultModel = AssetDatabase.LoadAssetAtPath<GameObject>(defaultPath);
                if (defaultModel != null)
                {
                    return defaultModel;
                }
            }

            // 4. If the user removed the sample model asset or never configured one, degrade gracefully by returning null (the viewport model stays empty, no exception is thrown)
            return null;
        }

        private void OnViewportModelChanged(GameObject newModel)
        {
            _currentPreviewPrefab = newModel;

            if (_targetAsset != null && _targetAsset.OverridePreviewModel)
            {
                _targetAsset.PreviewModel = newModel;
                EditorUtility.SetDirty(_targetAsset);
            }
            else
            {
                if (newModel != null)
                {
                    string path = AssetDatabase.GetAssetPath(newModel);
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    if (!string.IsNullOrEmpty(guid))
                    {
                        EditorPrefs.SetString(PREVIEW_MODEL_PREFS_KEY, guid);
                    }
                }
                else
                {
                    EditorPrefs.DeleteKey(PREVIEW_MODEL_PREFS_KEY);
                }
            }

            InitializePreviewAndPlayables();
            _viewport.RenderImmediate();
        }

        private void OnViewportOverrideModelToggled(bool isOverride)
        {
            if (_targetAsset == null) return;

            _targetAsset.OverridePreviewModel = isOverride;
            if (isOverride)
            {
                if (_currentPreviewPrefab != null)
                {
                    _targetAsset.PreviewModel = _currentPreviewPrefab;
                }
                if (_currentPreviewUpperBodyMask != null)
                {
                    _targetAsset.CustomUpperBodyMask = _currentPreviewUpperBodyMask;
                }
            }
            else
            {
                _currentPreviewPrefab = null;
                _currentPreviewUpperBodyMask = null;
            }

            EditorUtility.SetDirty(_targetAsset);
            InitializePreviewAndPlayables();
            _viewport.RenderImmediate();
        }

        private void OnViewportUpperBodyMaskChanged(AvatarMask newMask)
        {
            _currentPreviewUpperBodyMask = newMask;

            if (_targetAsset != null && _targetAsset.OverridePreviewModel)
            {
                _targetAsset.CustomUpperBodyMask = newMask;
                EditorUtility.SetDirty(_targetAsset);
            }

            if (_topLevelPreviewMixer.IsValid())
            {
                var upperMask = ResolvePreviewUpperBodyMask();
                _topLevelPreviewMixer.SetLayerMaskFromAvatarMask(1, upperMask);
            }

            _viewport.RenderImmediate();
        }

        private AvatarMask ResolvePreviewUpperBodyMask()
        {
            if (_currentPreviewUpperBodyMask != null)
            {
                return _currentPreviewUpperBodyMask;
            }

            if (_targetAsset != null && _targetAsset.CustomUpperBodyMask != null)
            {
                return _targetAsset.CustomUpperBodyMask;
            }

            if (_previewObject != null)
            {
                var coordinator = _previewObject.GetComponentInParent<MontageController>()
                               ?? _previewObject.GetComponentInChildren<MontageController>(true);
                if (coordinator != null)
                {
                    var so = new SerializedObject(coordinator);
                    var prop = so.FindProperty("_customUpperBodyMask");
                    var custom = prop != null ? prop.objectReferenceValue as AvatarMask : null;
                    if (custom != null) return custom;
                }
            }

            bool isHumanoid = _previewAnimator != null && _previewAnimator.avatar != null && _previewAnimator.isHuman;
            if (!isHumanoid && _previewObject != null)
            {
                Debug.LogWarning(
                    "[CwcMontage] The preview model rig is not Humanoid: the built-in procedural upper body mask has no effect on this rig, so the UpperBody layer falls back to full-body coverage." +
                    "Specify a path-based AvatarMask in the viewport View panel, or configure CustomUpperBodyMask on the MontageSequenceAsset asset.");
            }

            return MontageMaskUtility.GetOrCreateHumanoidUpperBodyMask();
        }

        private AnimationClip ResolvePreviewBaseMotion()
        {
            // 1. Read the persisted setting first (supports GUIDs of standalone .anim files and the "GUID#ClipName" format for FBX sub-assets)
            string savedValue = EditorPrefs.GetString(PREVIEW_BASE_MOTION_PREFS_KEY, "");
            if (savedValue == "NONE")
            {
                savedValue = "";
                EditorPrefs.DeleteKey(PREVIEW_BASE_MOTION_PREFS_KEY);
            }

            if (!string.IsNullOrEmpty(savedValue))
            {
                string guid = savedValue;
                string subName = null;
                int hashIdx = savedValue.IndexOf('#');
                if (hashIdx >= 0)
                {
                    guid = savedValue.Substring(0, hashIdx);
                    subName = savedValue.Substring(hashIdx + 1);
                }

                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                {
                    var clip = LoadAnimationClipByPathAndName(path, subName);
                    if (clip != null)
                    {
                        return clip;
                    }
                }
            }

            // 2. Plugin-specific built-in fallback: locate the sample model by fixed GUID and load its internal looping Walk clip (adapts to any folder move)
            string defaultFbxPath = AssetDatabase.GUIDToAssetPath(DEFAULT_DUMMY_MODEL_GUID);
            if (!string.IsNullOrEmpty(defaultFbxPath))
            {
                var defaultWalkClip = LoadAnimationClipByPathAndName(defaultFbxPath, "Walk_Loop")
                                   ?? LoadAnimationClipByPathAndName(defaultFbxPath, "Walk");
                if (defaultWalkClip != null)
                {
                    return defaultWalkClip;
                }
            }

            // 3. If the user removed the sample model asset, degrade gracefully by returning null (keeping the static pose)
            return null;
        }

        private static AnimationClip LoadAnimationClipByPathAndName(string assetPath, string clipName)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;

            var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (assets == null || assets.Length == 0) return null;

            AnimationClip fallbackClip = null;
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is AnimationClip clip)
                {
                    if (clip.name.StartsWith("__preview__")) continue;

                    if (!string.IsNullOrEmpty(clipName))
                    {
                        if (string.Equals(clip.name, clipName, StringComparison.OrdinalIgnoreCase) ||
                            clip.name.IndexOf(clipName, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return clip;
                        }
                    }

                    if (fallbackClip == null)
                    {
                        fallbackClip = clip;
                    }
                }
            }

            return string.IsNullOrEmpty(clipName) ? fallbackClip : null;
        }

        private void SaveBaseMotionPreference(AnimationClip clip)
        {
            if (clip != null)
            {
                string path = AssetDatabase.GetAssetPath(clip);
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(guid))
                {
                    if (AssetDatabase.IsSubAsset(clip))
                    {
                        EditorPrefs.SetString(PREVIEW_BASE_MOTION_PREFS_KEY, $"{guid}#{clip.name}");
                    }
                    else
                    {
                        EditorPrefs.SetString(PREVIEW_BASE_MOTION_PREFS_KEY, guid);
                    }
                }
            }
            else
            {
                // Removing the preference record when cleared so the next load falls back to the default looping Walk motion
                EditorPrefs.DeleteKey(PREVIEW_BASE_MOTION_PREFS_KEY);
            }
        }

        private void OnViewportBaseMotionChanged(AnimationClip newClip)
        {
            _currentPreviewBaseMotion = newClip;
            SaveBaseMotionPreference(newClip);

            InitializePreviewAndPlayables();
            _viewport.RenderImmediate();
        }

        private void CleanupPlayablesAndPreview()
        {
            _isPlaying = false;
            ExitAllActiveActionBlocks();
            MontageAudioPreviewUtility.StopAllClips();

            _upperBodySegmentPlayables.Clear();
            _fullBodySegmentPlayables.Clear();
            _additiveSegmentPlayables.Clear();

            if (_baseMotionPlayable.IsValid()) _baseMotionPlayable = default;
            if (_upperBodyMixer.IsValid()) _upperBodyMixer = default;
            if (_fullBodyMixer.IsValid()) _fullBodyMixer = default;
            if (_additiveMixer.IsValid()) _additiveMixer = default;
            if (_topLevelPreviewMixer.IsValid()) _topLevelPreviewMixer = default;

            if (_playableGraph.IsValid())
            {
                _playableGraph.Destroy();
                _playableGraph = default;
            }

            _viewport?.Cleanup();
            _previewObject = null;
            _previewAnimator = null;
        }

        #endregion

        #region Track and Action Block Rebuild

        private void RebuildTracks()
        {
            _headersContentWrapper?.Clear();
            _tracksContentWrapper?.Clear();
            _trackElements.Clear();

            // 1. Mount the standalone section marker track at the top (Section Marker Track)
            if (_sectionTrackElement != null)
            {
                _sectionTrackElement.SetTargetAsset(_targetAsset, _clipLength, _frameRate, _contentDuration);
                _headersContentWrapper?.Add(_sectionTrackElement.HeaderElement);
                _tracksContentWrapper?.Add(_sectionTrackElement.ContentElement);
            }

            // 2. Mount the three humanoid channel animation tracks (top to bottom by layer priority: Additive -> FullBody -> UpperBody)
            if (_targetAsset != null && _targetAsset.EnableAdditive && _additiveTrackElement != null)
            {
                _additiveTrackElement.SetTargetAsset(_targetAsset, _clipLength, _frameRate, _contentDuration);
                _headersContentWrapper?.Add(_additiveTrackElement.HeaderElement);
                _tracksContentWrapper?.Add(_additiveTrackElement.ContentElement);
            }

            if (_fullBodyTrackElement != null)
            {
                _fullBodyTrackElement.SetTargetAsset(_targetAsset, _clipLength, _frameRate, _contentDuration);
                _headersContentWrapper?.Add(_fullBodyTrackElement.HeaderElement);
                _tracksContentWrapper?.Add(_fullBodyTrackElement.ContentElement);
            }

            if (_targetAsset != null && _targetAsset.EnableUpperBody && _upperBodyTrackElement != null)
            {
                _upperBodyTrackElement.SetTargetAsset(_targetAsset, _clipLength, _frameRate, _contentDuration);
                _headersContentWrapper?.Add(_upperBodyTrackElement.HeaderElement);
                _tracksContentWrapper?.Add(_upperBodyTrackElement.ContentElement);
            }

            if (_targetAsset?.Tracks != null)
            {
                for (int i = 0; i < _targetAsset.Tracks.Count; i++)
                {
                    var trackData = _targetAsset.Tracks[i];
                    var trackElement = new MontageTrackElement(
                        trackData,
                        _targetAsset,
                        i,
                        _clipLength,
                        _frameRate,
                        _zoomLevel,
                        _actionTypes,
                        _actionTypeNames
                    );

                    trackElement.OnTrackSelected += SelectTrack;
                    trackElement.OnBlockSelected += SelectBlock;
                    trackElement.OnBlockClicked += SelectBlock;
                    trackElement.OnBlockMoving += block =>
                    {
                        if (_selectedBlock == block)
                        {
                            _inspector?.UpdateTimingDisplayDuringDrag(block);
                        }
                    };
                    trackElement.OnBlockDraggingGlobalSync += HandleBlockDraggingGlobalSync;
                    trackElement.OnAddBlockAtTime += AddBlockToTrack;
                    trackElement.OnPasteBlockAtTime += PasteBlockToTrack;
                    trackElement.OnTrackPasteOverrideRequested += PasteTrackToTrack;
                    trackElement.OnInsertTrackBelowRequested += InsertTrackBelow;
                    trackElement.OnPasteNewTrackBelowRequested += PasteNewTrackBelow;
                    trackElement.OnDuplicateTrackRequested += DuplicateTrack;
                    trackElement.OnTrackDeleteRequested += DeleteTrack;
                    trackElement.OnTrackMoveRequested += MoveTrack;
                    trackElement.OnTrackHeaderDragging += HandleTrackHeaderDragging;
                    trackElement.OnTrackHeaderDropped += HandleTrackHeaderDropped;
                    trackElement.OnDataModified += () =>
                    {
                        UpdateTimelineLengthsAndSync(fullRebuild: false);
                        if (_selectedBlock != null)
                        {
                            _inspector?.InspectActionBlock(_selectedBlock);
                        }
                    };

                    _trackElements.Add(trackElement);
                    _headersContentWrapper?.Add(trackElement.HeaderElement);
                    _tracksContentWrapper?.Add(trackElement.ContentElement);

                    // Restore the highlight on the currently selected block after construction
                    if (_selectedBlockData != null)
                    {
                        trackElement.RebuildBlocks(_selectedBlockData);
                    }
                }
            }

            // Rebind the _selectedBlock reference to the newly created Element instance
            if (_selectedBlockData != null)
            {
                MontageActionBlockElement found = null;
                for (int t = 0; t < _trackElements.Count; t++)
                {
                    var blocks = _trackElements[t].BlockElements;
                    for (int b = 0; b < blocks.Count; b++)
                    {
                        if (blocks[b]?.Data == _selectedBlockData)
                        {
                            found = blocks[b];
                            break;
                        }
                    }
                    if (found != null) break;
                }
                _selectedBlock = found;
                _selectedBlock?.SetSelected(true);
            }

            if (_reorderIndicatorLine != null)
            {
                _headersContentWrapper?.Add(_reorderIndicatorLine);
            }

            RebuildRuntimeActionBlocks();
        }

        private void RebuildRuntimeActionBlocks(bool forceRecreate = false)
        {
            if (forceRecreate)
            {
                ExitAllActiveActionBlocks();
                _runtimeActionBlocks.Clear();
                if (_targetAsset?.Tracks == null) return;

                float fps = _frameRate;
                for (int i = 0; i < _targetAsset.Tracks.Count; i++)
                {
                    var track = _targetAsset.Tracks[i];
                    if (track == null || track.IsMuted || track.ActionBlocks == null) continue;

                    for (int j = 0; j < track.ActionBlocks.Count; j++)
                    {
                        var block = track.ActionBlocks[j];
                        if (block != null && block.IsEnabled && block.Action != null)
                        {
                            var cloned = block.Clone();
                            cloned.EnsureValid(fps);
                            _runtimeActionBlocks.Add(cloned);
                        }
                    }
                }
                return;
            }

            SyncRuntimeActionBlocks();
        }

        private void SyncRuntimeActionBlocks()
        {
            if (_targetAsset?.Tracks == null)
            {
                ExitAllActiveActionBlocks();
                _runtimeActionBlocks.Clear();
                return;
            }

            var context = CreateCurrentContext();
            float fps = _frameRate;

            // 1. Collect all valid source action blocks in the current asset
            var validSourceBlocks = new List<MontageActionBlockData>();
            for (int i = 0; i < _targetAsset.Tracks.Count; i++)
            {
                var track = _targetAsset.Tracks[i];
                if (track == null || track.IsMuted || track.ActionBlocks == null) continue;

                for (int j = 0; j < track.ActionBlocks.Count; j++)
                {
                    var block = track.ActionBlocks[j];
                    if (block != null && block.IsEnabled && block.Action != null)
                    {
                        validSourceBlocks.Add(block);
                    }
                }
            }

            // 2. Remove runtime blocks that were deleted from the asset or disabled
            for (int i = _runtimeActionBlocks.Count - 1; i >= 0; i--)
            {
                var rb = _runtimeActionBlocks[i];
                if (rb.SourceData == null || !validSourceBlocks.Contains(rb.SourceData))
                {
                    if (_activeActionBlocks.Contains(rb))
                    {
                        rb.Action?.OnPreviewExit(context);
                        _activeActionBlocks.Remove(rb);
                    }
                    _runtimeActionBlocks.RemoveAt(i);
                }
            }

            // 3. Diff and incrementally sync existing or newly added action blocks
            for (int i = 0; i < validSourceBlocks.Count; i++)
            {
                var src = validSourceBlocks[i];
                var existingRb = _runtimeActionBlocks.Find(r => r.SourceData == src);

                if (existingRb != null)
                {
                    // Sync start/end times and the frame range
                    existingRb.StartFrame = src.StartFrame;
                    existingRb.EndFrame = src.EndFrame;
                    existingRb.StartTime = src.StartTime;
                    existingRb.EndTime = src.EndTime;
                    existingRb.EnsureValid(fps);

                    bool needRecreate = existingRb.Action.RequiresPreviewRecreate(src.Action);

                    if (needRecreate)
                    {
                        if (_activeActionBlocks.Contains(existingRb))
                        {
                            existingRb.Action?.OnPreviewExit(context);
                            _activeActionBlocks.Remove(existingRb);
                        }
                        var cloned = src.Clone();
                        cloned.EnsureValid(fps);
                        int idx = _runtimeActionBlocks.IndexOf(existingRb);
                        _runtimeActionBlocks[idx] = cloned;
                    }
                    else
                    {
                        // Overwrite serialized parameters in place, leaving non-serialized preview fields (such as existing instances) untouched
                        SyncActionParameters(src.Action, existingRb.Action);

                        // If currently previewing, update the Transform in place to avoid destruction and flicker
                        if (_activeActionBlocks.Contains(existingRb))
                        {
                            existingRb.Action?.OnPreviewParametersChanged(context);
                        }
                    }
                }
                else
                {
                    var cloned = src.Clone();
                    cloned.EnsureValid(fps);
                    _runtimeActionBlocks.Add(cloned);
                }
            }
        }

        private void SyncActionParameters(MontageActionBlockBase source, MontageActionBlockBase target)
        {
            if (source == null || target == null || source.GetType() != target.GetType()) return;

            try
            {
                CopySerializedFieldsReflectively(source, target);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CwcMontage] Failed to sync action block parameters: {ex.Message}");
            }
        }

        private static void CopySerializedFieldsReflectively(MontageActionBlockBase source, MontageActionBlockBase target)
        {
            Type type = source.GetType();
            while (type != null && type != typeof(object))
            {
                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (int i = 0; i < fields.Length; i++)
                {
                    var f = fields[i];
                    if (f.IsInitOnly || Attribute.IsDefined(f, typeof(NonSerializedAttribute))) continue;
                    if (f.IsPublic || Attribute.IsDefined(f, typeof(SerializeField)))
                    {
                        f.SetValue(target, f.GetValue(source));
                    }
                }
                type = type.BaseType;
            }
        }

        #endregion

        #region Zoom and Pan Control

        private void OnTracksGeometryChanged(GeometryChangedEvent evt)
        {
            if (Mathf.Abs(evt.newRect.width - evt.oldRect.width) > 1f)
            {
                UpdateTimelineLengthsAndSync(fullRebuild: false, markDirty: false, rebuildRuntimeBlocks: false);
            }
        }

        private void SetZoom(float newZoom, float? targetScrollX = null)
        {
            _zoomLevel = Mathf.Clamp(newZoom, 0.005f, 20.0f);
            EditorPrefs.SetFloat(PREFS_ZOOM_LEVEL_KEY, _zoomLevel);

            UpdateTimelineLengthsAndSync(fullRebuild: false, markDirty: false, rebuildRuntimeBlocks: false, predictedScrollX: targetScrollX);
        }

        private void HandleWheelZoomDelta(float delta, Vector2? mousePos)
        {
            float zoomMultiplier = delta > 0 ? 1.15f : (1f / 1.15f);
            float oldZoom = _zoomLevel;
            float newZoom = Mathf.Clamp(oldZoom * zoomMultiplier, 0.005f, 20f);
            if (Mathf.Approximately(oldZoom, newZoom)) return;

            SetZoom(newZoom);
        }

        private void OnHeadersAreaWheel(WheelEvent evt)
        {
            if (_tracksScrollView != null)
            {
                float newY = Mathf.Max(0f, _tracksScrollView.scrollOffset.y + evt.delta.y * 28f);
                _tracksScrollView.scrollOffset = new Vector2(_tracksScrollView.scrollOffset.x, newY);
            }

            evt.StopImmediatePropagation();
            evt.PreventDefault();
        }

        private void OnTimelineAreaWheel(WheelEvent evt)
        {
            if (evt.shiftKey) // Shift + wheel: allow explicit smooth vertical scrolling in the right area
            {
                if (_tracksScrollView != null)
                {
                    float newY = Mathf.Max(0f, _tracksScrollView.scrollOffset.y + evt.delta.y * 28f);
                    _tracksScrollView.scrollOffset = new Vector2(_tracksScrollView.scrollOffset.x, newY);
                }
            }
            else // Default wheel: horizontal timeline zoom only, vertical scrolling strictly disabled
            {
                float zoomMultiplier = evt.delta.y < 0 ? 1.15f : (1f / 1.15f);
                float oldZoom = _zoomLevel;
                float newZoom = Mathf.Clamp(oldZoom * zoomMultiplier, 0.005f, 20f);

                if (!Mathf.Approximately(oldZoom, newZoom))
                {
                    // Record the current vertical scroll offset and keep it fully locked during zooming
                    float savedScrollY = _tracksScrollView != null ? _tracksScrollView.scrollOffset.y : 0f;

                    Vector2 localMouse = _tracksContentWrapper.WorldToLocal(evt.mousePosition);
                    float mouseTime = Mathf.Max(0f, localMouse.x / (200f * oldZoom));

                    // Adjust the horizontal scroll offset so the time under the mouse stays fixed in the viewport
                    float newMouseX = mouseTime * (200f * newZoom);
                    float viewportMouseX = evt.mousePosition.x - _tracksScrollView.worldBound.xMin;
                    float targetScrollX = Mathf.Max(0f, newMouseX - viewportMouseX);

                    SetZoom(newZoom, targetScrollX);

                    if (_tracksScrollView != null)
                    {
                        _tracksScrollView.scrollOffset = new Vector2(targetScrollX, savedScrollY);
                    }
                }
            }

            evt.StopImmediatePropagation();
            evt.PreventDefault();
        }

        private void PanTimeline(float deltaX)
        {
            if (_tracksScrollView != null)
            {
                float targetX = Mathf.Max(0f, _tracksScrollView.scrollOffset.x - deltaX);
                float vpWidth = GetViewportWidth();
                float currentTotalWidth = _tracksContentWrapper != null ? _tracksContentWrapper.style.width.value.value : 0f;

                if (targetX + vpWidth > currentTotalWidth)
                {
                    UpdateTimelineLengthsAndSync(fullRebuild: false, markDirty: false, rebuildRuntimeBlocks: false, predictedScrollX: targetX);
                }

                _tracksScrollView.scrollOffset = new Vector2(targetX, _tracksScrollView.scrollOffset.y);
            }
        }

        #endregion

        #region Playback and Tick Loop

        private void OnUpdateTick()
        {
            if (_targetAsset == null || (_targetAsset.TotalDuration <= 0f && _clipLength <= 0f)) return;

            double currentEditorTime = EditorApplication.timeSinceStartup;
            float deltaTime = Mathf.Min((float)(currentEditorTime - _lastEditorTime), 0.1f);
            _lastEditorTime = currentEditorTime;

            float playDuration = _targetAsset != null ? _targetAsset.NaturalEndTime : (_contentDuration > 0.001f ? _contentDuration : _clipLength);
            if (_isPlaying && playDuration > 0f)
            {
                float step = deltaTime * _previewSpeed;
                _animationTime += step;

                if (_animationTime >= playDuration)
                {
                    if (_previewLoop)
                    {
                        _animationTime %= playDuration;
                        ExitAllActiveActionBlocks();
                        ResetPreviewModelTransform();
                    }
                    else
                    {
                        _animationTime = playDuration;
                        _isPlaying = false;
                        UpdatePlayPauseVisual();
                    }
                }

                EvaluateTimeAndPreviewLogic(deltaTime);
                _ruler.SetTime(_animationTime);
                UpdateToolbarTimeDisplay();
                _viewport.RenderFrame();
                _lastAnimationTime = _animationTime;
                OnRepaintRequested?.Invoke();
            }
            else if (_viewport != null && _viewport.IsInteracting)
            {
                _viewport.RenderFrame();
                OnRepaintRequested?.Invoke();
            }
        }

        private void ScrubToTime(float time)
        {
            _animationTime = Mathf.Clamp(time, 0f, _clipLength);
            _isPlaying = false;
            MontageAudioPreviewUtility.StopAllClips();
            UpdatePlayPauseVisual();

            EvaluateTimeAndPreviewLogic(0f);
            _ruler.SetTime(_animationTime);
            UpdateToolbarTimeDisplay();
            _viewport.RenderFrame();
            _lastAnimationTime = _animationTime;
        }

        private void TogglePlayPause()
        {
            float playDuration = _targetAsset != null ? _targetAsset.NaturalEndTime : (_contentDuration > 0.001f ? _contentDuration : _clipLength);
            _isPlaying = !_isPlaying;
            if (_isPlaying)
            {
                if (_animationTime >= playDuration)
                {
                    _animationTime = 0f;
                    ResetPreviewModelTransform();
                }
                _lastEditorTime = EditorApplication.timeSinceStartup;
            }
            else
            {
                MontageAudioPreviewUtility.StopAllClips();
            }

            UpdatePlayPauseVisual();
        }

        private void StopAnimation()
        {
            _isPlaying = false;
            _animationTime = 0f;
            UpdatePlayPauseVisual();

            ExitAllActiveActionBlocks();
            MontageAudioPreviewUtility.StopAllClips();
            ResetPreviewModelTransform();
            EvaluateTimeAndPreviewLogic(0f);
            _ruler.SetTime(0f);
            UpdateToolbarTimeDisplay();
            _viewport.RenderFrame();
        }

        private void ResetPreviewModelTransform()
        {
            if (_previewObject != null)
            {
                _previewObject.transform.localPosition = Vector3.zero;
                _previewObject.transform.localRotation = Quaternion.identity;
            }
        }

        private void OnPreviewRootMotionChanged(bool enabled)
        {
            _previewRootMotion = enabled;
            EditorPrefs.SetBool(PREFS_PREVIEW_ROOT_MOTION_KEY, enabled);

            if (_previewAnimator != null)
            {
                _previewAnimator.applyRootMotion = enabled;
            }

            ResetPreviewModelTransform();
            _viewport.RenderFrame();
        }

        private void OnPreviewFootIKChanged(bool enabled)
        {
            _previewFootIK = enabled;
            EditorPrefs.SetBool(PREFS_PREVIEW_FOOT_IK_KEY, enabled);

            for (int i = 0; i < _upperBodySegmentPlayables.Count; i++)
            {
                var cp = _upperBodySegmentPlayables[i];
                if (cp.IsValid())
                {
                    cp.SetApplyFootIK(enabled);
                }
            }

            for (int i = 0; i < _fullBodySegmentPlayables.Count; i++)
            {
                var cp = _fullBodySegmentPlayables[i];
                if (cp.IsValid())
                {
                    cp.SetApplyFootIK(enabled);
                }
            }

            if (_baseMotionPlayable.IsValid())
            {
                _baseMotionPlayable.SetApplyFootIK(enabled);
            }

            _viewport.RenderFrame();
        }

        private void StepPrevFrame()
        {
            float frameStep = 1f / _frameRate;
            ScrubToTime(_animationTime - frameStep);
        }

        private void StepNextFrame()
        {
            float frameStep = 1f / _frameRate;
            ScrubToTime(_animationTime + frameStep);
        }

        private void JumpToLastFrame()
        {
            ScrubToTime(_contentDuration > 0.001f ? _contentDuration : _clipLength);
        }

        private void EvaluateTimeAndPreviewLogic(float deltaTime)
        {
            if (_playableGraph.IsValid())
            {
                if (_topLevelPreviewMixer.IsValid())
                {
                    EvaluatePreviewPlayables();
                }
                _playableGraph.Evaluate();
                ApplyUpperBodySpineDecouplingPreview();
            }

            // Sweep-evaluate all action blocks across the interval (invoking the viewport preview lifecycle)
            var context = CreateCurrentContext();

            for (int i = 0; i < _runtimeActionBlocks.Count; i++)
            {
                var blockData = _runtimeActionBlocks[i];
                if (blockData?.Action == null || !blockData.IsEnabled) continue;

                var action = blockData.Action;
                bool isInside = _animationTime >= blockData.StartTime && _animationTime < blockData.EndTime;
                float localTime = Mathf.Max(0f, _animationTime - blockData.StartTime);

                if (isInside)
                {
                    action.BlockDuration = blockData.Duration;
                    if (!_activeActionBlocks.Contains(blockData))
                    {
                        if (action.CanPreviewEnter(context))
                        {
                            action.OnPreviewEnter(context);
                            _activeActionBlocks.Add(blockData);

                            // On first entry while not playing (drag/seek/single frame), immediately restore the exact particle slice for the current frame
                            if (!_isPlaying)
                            {
                                action.OnPreviewScrub(context, localTime);
                            }
                        }
                    }
                    else
                    {
                        if (_isPlaying)
                        {
                            action.OnPreviewUpdate(context, deltaTime);
                        }
                        else
                        {
                            // While paused or scrubbing the timeline, sample an absolute time slice
                            action.OnPreviewScrub(context, localTime);
                        }
                    }
                }
                else if (_activeActionBlocks.Contains(blockData))
                {
                    action.OnPreviewExit(context);
                    _activeActionBlocks.Remove(blockData);
                }
            }
        }

        private void ExitAllActiveActionBlocks()
        {
            if (_activeActionBlocks.Count == 0) return;

            var context = CreateCurrentContext();
            foreach (var block in _activeActionBlocks)
            {
                block?.Action?.OnPreviewExit(context);
            }
            _activeActionBlocks.Clear();
        }

        private MontageActionContext CreateCurrentContext()
        {
            float totalDuration = _contentDuration > 0.001f ? _contentDuration : _clipLength;
            int curSection = _targetAsset != null ? _targetAsset.GetSectionIndexAtTime(_animationTime) : 0;
            var (start, end) = _targetAsset != null ? _targetAsset.GetSectionRange(curSection) : (start: 0f, end: totalDuration);
            float sectionLen = Mathf.Max(0.0001f, end - start);
            float sectionProgress = Mathf.Clamp01((_animationTime - start) / sectionLen);
            float normProgress = totalDuration > 0.0001f ? Mathf.Clamp01(_animationTime / totalDuration) : 0f;

            return new MontageActionContext(
                _previewObject,
                _previewAnimator,
                _animationTime,
                totalDuration,
                curSection,
                sectionProgress,
                normProgress,
                _previewSpeed,
                isPreview: true);
        }

        private void UpdatePlayPauseVisual()
        {
            if (_playPauseButton == null) return;

            string iconName = _isPlaying ? "d_PauseButton" : "d_PlayButton";
            var icon = EditorGUIUtility.IconContent(iconName) ?? EditorGUIUtility.IconContent(_isPlaying ? "PauseButton" : "PlayButton");

            if (_playPauseIcon != null && icon != null && icon.image != null)
            {
                _playPauseIcon.image = icon.image;
                _playPauseButton.text = "";
            }
            else
            {
                _playPauseButton.text = _isPlaying ? "||" : ">";
            }

            if (_isPlaying)
            {
                _playPauseButton.AddToClassList("montage-play-center-btn-playing");
            }
            else
            {
                _playPauseButton.RemoveFromClassList("montage-play-center-btn-playing");
            }
        }

        private void UpdateToolbarTimeDisplay()
        {
            float contentDuration = _contentDuration > 0.001f ? _contentDuration : (_targetAsset != null ? _targetAsset.TotalDuration : 0f);
            if (_timeValueLabel != null)
            {
                _timeValueLabel.text = $"{_animationTime:F2}s / {contentDuration:F2}s";
            }
            if (_frameValueLabel != null)
            {
                int curFrame = Mathf.FloorToInt(_animationTime * _frameRate);
                int totalFrames = Mathf.RoundToInt(contentDuration * _frameRate);
                _frameValueLabel.text = $"F {curFrame} / {totalFrames}";
            }
            if (_sectionBadgeLabel != null && _targetAsset != null)
            {
                int totalSections = Mathf.Max(1, _targetAsset.SectionCount);
                int sectionIdx = _targetAsset.GetSectionIndexAtTime(_animationTime);
                _sectionBadgeLabel.text = totalSections > 1 ? $"S{sectionIdx + 1}/{totalSections}" : "S1";
                _sectionBadgeLabel.tooltip = $"Physical Section: {sectionIdx + 1} of {totalSections}\nProgram Index: {sectionIdx} (for C# API: GetSectionRange({sectionIdx}))";
            }
        }

        #endregion

        #region Section Split Operations

        private void AddSplitTimestamp(float time)
        {
            if (_targetAsset == null) return;

            Undo.RecordObject(_targetAsset, "Add Section Split");
            var splits = new List<float>(_targetAsset.SplitTimestamps);
            splits.Add(time);
            _targetAsset.SetSplitTimestamps(splits);
            EditorUtility.SetDirty(_targetAsset);

            UpdateTimelineLengthsAndSync(fullRebuild: false);
            OnAssetModified?.Invoke();
        }

        private void MoveSplitTimestamp(int splitIndex, float newTime)
        {
            if (_targetAsset?.SplitTimestamps == null || splitIndex < 0 || splitIndex >= _targetAsset.SplitTimestamps.Count) return;

            Undo.RecordObject(_targetAsset, "Move Section Split");
            var splits = new List<float>(_targetAsset.SplitTimestamps);
            splits[splitIndex] = newTime;
            _targetAsset.SetSplitTimestamps(splits);
            EditorUtility.SetDirty(_targetAsset);

            UpdateTimelineLengthsAndSync(fullRebuild: false);
            OnAssetModified?.Invoke();
        }

        private void RemoveSplitTimestamp(int splitIndex)
        {
            if (_targetAsset?.SplitTimestamps == null || splitIndex < 0 || splitIndex >= _targetAsset.SplitTimestamps.Count) return;

            Undo.RecordObject(_targetAsset, "Remove Section Split");
            var splits = new List<float>(_targetAsset.SplitTimestamps);
            splits.RemoveAt(splitIndex);
            _targetAsset.SetSplitTimestamps(splits);
            EditorUtility.SetDirty(_targetAsset);

            UpdateTimelineLengthsAndSync(fullRebuild: false);
            OnAssetModified?.Invoke();
        }

        #endregion

        #region Track and Action Block Operations

        private void SelectTrack(MontageTrackElement track)
        {
            _selectedTrack?.SetSelected(false);
            _selectedTrack = track;
            _selectedTrack?.SetSelected(true);
        }

        private void SelectBlockVisual(MontageActionBlockElement block)
        {
            if (_selectedBlock == block && block != null && block.IsSelected) return;

            _selectedBlock?.SetSelected(false);
            _selectedBlock = block;
            _selectedBlockData = block?.Data;
            _selectedBlock?.SetSelected(true);
        }

        private void SelectBlock(MontageActionBlockElement block)
        {
            _additiveTrackElement?.ClearSelection();
            _fullBodyTrackElement?.ClearSelection();
            _upperBodyTrackElement?.ClearSelection();
            _selectedSegment = null;
            SelectBlockVisual(block);
            if (block != null && block.TrackIndex >= 0 && block.TrackIndex < _trackElements.Count)
            {
                SelectTrack(_trackElements[block.TrackIndex]);
            }
            _inspector?.InspectActionBlock(block);
        }

        public void AddNewTrack()
        {
            if (_targetAsset == null) return;
            Undo.RecordObject(_targetAsset, "Add New Track");
            _targetAsset.Tracks.Add(new MontageTrackData($"Track {_targetAsset.Tracks.Count + 1}"));
            RebuildTracks();
            EditorUtility.SetDirty(_targetAsset);
            OnAssetModified?.Invoke();
        }

        public void InsertTrackBelow(MontageTrackElement track)
        {
            if (_targetAsset == null) return;
            Undo.RecordObject(_targetAsset, "Insert Track Below");
            int index = track != null ? track.TrackIndex + 1 : _targetAsset.Tracks.Count;
            index = Mathf.Clamp(index, 0, _targetAsset.Tracks.Count);

            _targetAsset.Tracks.Insert(index, new MontageTrackData($"Track {index + 1}"));
            RebuildTracks();
            EditorUtility.SetDirty(_targetAsset);
            OnAssetModified?.Invoke();
        }

        public void PasteCopiedTrackAsNew()
        {
            if (_targetAsset == null) return;
            var cloned = MontageClipboard.GetClonedTrack();
            if (cloned == null) return;

            Undo.RecordObject(_targetAsset, "Paste Copied Track As New");
            _targetAsset.Tracks.Add(cloned);
            RebuildTracks();
            EditorUtility.SetDirty(_targetAsset);
            OnAssetModified?.Invoke();
        }

        public void PasteNewTrackBelow(MontageTrackElement track)
        {
            if (_targetAsset == null) return;
            var cloned = MontageClipboard.GetClonedTrack();
            if (cloned == null) return;

            Undo.RecordObject(_targetAsset, "Paste New Track Below");
            int index = track != null ? track.TrackIndex + 1 : _targetAsset.Tracks.Count;
            index = Mathf.Clamp(index, 0, _targetAsset.Tracks.Count);

            _targetAsset.Tracks.Insert(index, cloned);
            RebuildTracks();
            EditorUtility.SetDirty(_targetAsset);
            OnAssetModified?.Invoke();
        }

        public void DuplicateTrack(MontageTrackElement track)
        {
            if (_targetAsset == null || track?.TrackData == null) return;

            Undo.RecordObject(_targetAsset, "Duplicate Track");
            var cloned = track.TrackData.Clone();
            int insertIndex = track.TrackIndex + 1;
            _targetAsset.Tracks.Insert(insertIndex, cloned);
            RebuildTracks();
            EditorUtility.SetDirty(_targetAsset);
            OnAssetModified?.Invoke();
        }

        private void DuplicateBlock(MontageActionBlockElement block)
        {
            if (_targetAsset == null || block?.Data == null || block.TrackIndex < 0 || block.TrackIndex >= _targetAsset.Tracks.Count) return;

            Undo.RecordObject(_targetAsset, "Duplicate Action Block");
            var cloned = block.Data.Clone();
            int duration = Mathf.Max(1, cloned.EndFrame - cloned.StartFrame);
            int newStart = block.Data.EndFrame;
            int newEnd = newStart + duration;

            cloned.StartFrame = newStart;
            cloned.EndFrame = newEnd;
            cloned.StartTime = newStart / _frameRate;
            cloned.EndTime = newEnd / _frameRate;

            _targetAsset.Tracks[block.TrackIndex].ActionBlocks.Add(cloned);
            UpdateTimelineLengthsAndSync(fullRebuild: false);
            RebuildTracks();
            EditorUtility.SetDirty(_targetAsset);
            OnAssetModified?.Invoke();
        }

        private void AddBlockToTrack(MontageTrackElement track, Type actionType, float timeAtClick)
        {
            if (_targetAsset == null || track?.TrackData == null) return;

            Undo.RecordObject(_targetAsset, "Add Action Block");
            int startFrame = Mathf.Max(0, Mathf.RoundToInt(timeAtClick * _frameRate));
            int endFrame = startFrame + 5;

            var newBlock = new MontageActionBlockData
            {
                StartFrame = startFrame,
                EndFrame = endFrame,
                StartTime = startFrame / _frameRate,
                EndTime = endFrame / _frameRate,
                Action = (MontageActionBlockBase)Activator.CreateInstance(actionType)
            };

            track.TrackData.ActionBlocks.Add(newBlock);
            UpdateTimelineLengthsAndSync(fullRebuild: false);
            track.RebuildBlocks();
            EditorUtility.SetDirty(_targetAsset);
            OnAssetModified?.Invoke();
        }

        private void PasteBlockToTrack(MontageTrackElement track, float timeAtClick)
        {
            if (_targetAsset == null || track?.TrackData == null) return;
            var cloned = MontageClipboard.GetClonedBlock();
            if (cloned == null || cloned.Action == null) return;

            Undo.RecordObject(_targetAsset, "Paste Action Block");
            int duration = Mathf.Max(1, cloned.EndFrame - cloned.StartFrame);
            int startFrame = Mathf.Max(0, Mathf.RoundToInt(timeAtClick * _frameRate));
            int endFrame = startFrame + duration;

            cloned.StartFrame = startFrame;
            cloned.EndFrame = endFrame;
            cloned.StartTime = startFrame / _frameRate;
            cloned.EndTime = endFrame / _frameRate;

            track.TrackData.ActionBlocks.Add(cloned);
            UpdateTimelineLengthsAndSync(fullRebuild: false);
            track.RebuildBlocks();
            EditorUtility.SetDirty(_targetAsset);
            OnAssetModified?.Invoke();
        }

        private void PasteTrackToTrack(MontageTrackElement track)
        {
            if (_targetAsset == null || track == null) return;
            var clonedTrack = MontageClipboard.GetClonedTrack();
            if (clonedTrack == null) return;

            Undo.RecordObject(_targetAsset, "Paste Track Override");
            _targetAsset.Tracks[track.TrackIndex] = clonedTrack;
            UpdateTimelineLengthsAndSync(fullRebuild: false);
            RebuildTracks();
            EditorUtility.SetDirty(_targetAsset);
            OnAssetModified?.Invoke();
        }

        private void DeleteTrack(MontageTrackElement track)
        {
            if (_targetAsset == null || track == null) return;
            if (track.TrackIndex >= 0 && track.TrackIndex < _targetAsset.Tracks.Count)
            {
                Undo.RecordObject(_targetAsset, "Delete Track");
                _targetAsset.Tracks.RemoveAt(track.TrackIndex);
                _inspector.ClearActionInspect();
                UpdateTimelineLengthsAndSync(fullRebuild: false);
                RebuildTracks();
                EditorUtility.SetDirty(_targetAsset);
                OnAssetModified?.Invoke();
            }
        }

        private void MoveTrack(MontageTrackElement track, int delta)
        {
            if (_targetAsset == null || track == null) return;
            int oldIndex = track.TrackIndex;
            int newIndex = oldIndex + delta;
            if (newIndex < 0 || newIndex >= _targetAsset.Tracks.Count) return;

            Undo.RecordObject(_targetAsset, "Move Track");
            var t = _targetAsset.Tracks[oldIndex];
            _targetAsset.Tracks.RemoveAt(oldIndex);
            _targetAsset.Tracks.Insert(newIndex, t);

            RebuildTracks();
            EditorUtility.SetDirty(_targetAsset);
            OnAssetModified?.Invoke();
        }

        private void HandleTrackHeaderDragging(MontageTrackElement draggingTrack, Vector2 mouseWorldPos)
        {
            if (_headersContentWrapper == null || _targetAsset?.Tracks == null || _targetAsset.Tracks.Count == 0 || _reorderIndicatorLine == null) return;

            Vector2 localPos = _headersContentWrapper.WorldToLocal(mouseWorldPos);
            float localY = localPos.y;

            float sectionHeight = MontageSectionTrackElement.TRACK_HEIGHT;
            float trackHeight = MontageTrackElement.TRACK_HEIGHT;

            float relativeY = localY - sectionHeight;
            int targetIndex = Mathf.Clamp(Mathf.RoundToInt(relativeY / trackHeight), 0, _targetAsset.Tracks.Count);

            float indicatorTop = sectionHeight + targetIndex * trackHeight - 1f;
            _reorderIndicatorLine.style.top = indicatorTop;
            _reorderIndicatorLine.style.display = DisplayStyle.Flex;
        }

        private void HandleTrackHeaderDropped(MontageTrackElement draggingTrack, Vector2 mouseWorldPos)
        {
            if (_reorderIndicatorLine != null)
            {
                _reorderIndicatorLine.style.display = DisplayStyle.None;
            }

            if (_headersContentWrapper == null || _targetAsset?.Tracks == null || _targetAsset.Tracks.Count == 0) return;

            Vector2 localPos = _headersContentWrapper.WorldToLocal(mouseWorldPos);
            float localY = localPos.y;

            float sectionHeight = MontageSectionTrackElement.TRACK_HEIGHT;
            float trackHeight = MontageTrackElement.TRACK_HEIGHT;

            float relativeY = localY - sectionHeight;
            int targetIndex = Mathf.Clamp(Mathf.RoundToInt(relativeY / trackHeight), 0, _targetAsset.Tracks.Count);

            int fromIndex = draggingTrack.TrackIndex;
            if (targetIndex != fromIndex && targetIndex != fromIndex + 1)
            {
                Undo.RecordObject(_targetAsset, "Reorder Tracks");
                var t = _targetAsset.Tracks[fromIndex];
                _targetAsset.Tracks.RemoveAt(fromIndex);
                int insertIndex = (targetIndex > fromIndex) ? targetIndex - 1 : targetIndex;
                _targetAsset.Tracks.Insert(insertIndex, t);

                RebuildTracks();
                EditorUtility.SetDirty(_targetAsset);
                OnAssetModified?.Invoke();
            }
        }

        private void HandleBlockDraggingGlobalSync(MontageActionBlockElement block, float activeEdgeTime)
        {
            if (block?.Data == null || _targetAsset == null) return;

            // 1. WYSIWYG per-frame evaluation: position the playhead and viewport at the dragged edge time in real time
            if (!_isPlaying && activeEdgeTime >= 0f)
            {
                _animationTime = Mathf.Clamp(activeEdgeTime, 0f, Mathf.Max(_clipLength, _targetAsset.TotalDuration));
                _lastAnimationTime = _animationTime;
            }

            // 2. Globally update the timeline, ruler, view extension, incremental runtime action block sync, and real-time 3D viewport evaluation
            UpdateTimelineLengthsAndSync(fullRebuild: false, markDirty: false, rebuildRuntimeBlocks: true);

            // 3. 60 FPS lock-free numeric update of the inspector (lightweight fast path)
            if (_selectedBlock == block)
            {
                _inspector?.UpdateTimingDisplayDuringDrag(block);
            }
        }

        private void ShowAddTrackDropdownMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Add New Track"), false, AddNewTrack);

            if (MontageClipboard.HasCopiedTrack)
            {
                menu.AddItem(new GUIContent("Paste as New Track"), false, PasteCopiedTrackAsNew);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Paste as New Track"));
            }

            menu.ShowAsContext();
        }

        private void OnBlankAreaMouseDown(MouseDownEvent evt)
        {
            if (evt.button == 1)
            {
                ShowAddTrackDropdownMenu();
                evt.StopPropagation();
            }
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            // If focus is inside a text input control, let the input event through and never intercept playback/control shortcuts
            if (evt.target != null && (
                evt.target is TextInputBaseField<string> ||
                evt.target is TextInputBaseField<int> ||
                evt.target is TextInputBaseField<float> ||
                evt.target is TextInputBaseField<double> ||
                evt.target is TextInputBaseField<long> ||
                evt.target.GetType().Name.Contains("TextInput")))
            {
                return;
            }

            // 1. Ctrl / Cmd + S: save the asset
            if (evt.actionKey && evt.keyCode == KeyCode.S)
            {
                if (_targetAsset != null)
                {
                    EditorUtility.SetDirty(_targetAsset);
                    AssetDatabase.SaveAssets();
                }
                evt.StopPropagation();
                return;
            }

            // 2. General playback control shortcuts
            if (evt.keyCode == KeyCode.Space)
            {
                TogglePlayPause();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Home)
            {
                StopAnimation();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.End)
            {
                JumpToLastFrame();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Comma || evt.keyCode == KeyCode.LeftBracket)
            {
                StepPrevFrame();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Period || evt.keyCode == KeyCode.RightBracket)
            {
                StepNextFrame();
                evt.StopPropagation();
            }
            // 3. Copy (must be Ctrl+C / Cmd+C)
            else if (evt.actionKey && evt.keyCode == KeyCode.C)
            {
                if (_selectedSegment != null)
                {
                    MontageClipboard.CopySegment(_selectedSegment);
                }
                else if (_selectedBlock != null)
                {
                    MontageClipboard.CopyActionBlock(_selectedBlock.Data);
                }
                else if (_selectedTrack != null)
                {
                    MontageClipboard.CopyTrack(_selectedTrack.TrackData);
                }
                evt.StopPropagation();
            }
            // 4. Paste (must be Ctrl+V / Cmd+V)
            else if (evt.actionKey && evt.keyCode == KeyCode.V)
            {
                if (MontageClipboard.HasCopiedSegment && (_selectedAnimationTrack != null || _selectedBlock == null))
                {
                    PasteCopiedSegment(_selectedAnimationTrack ?? _fullBodyTrackElement, _animationTime);
                }
                else if (MontageClipboard.HasCopiedTrack && _selectedBlock == null)
                {
                    if (_selectedTrack != null)
                    {
                        PasteNewTrackBelow(_selectedTrack);
                    }
                    else
                    {
                        PasteCopiedTrackAsNew();
                    }
                }
                else if (MontageClipboard.HasCopiedBlock && _selectedTrack != null)
                {
                    PasteBlockToTrack(_selectedTrack, _animationTime);
                }
                evt.StopPropagation();
            }
            // 5. Duplicate (Ctrl+D)
            else if (evt.actionKey && evt.keyCode == KeyCode.D)
            {
                if (_selectedSegment != null && _selectedAnimationTrack != null)
                {
                    _selectedAnimationTrack.DuplicateSelectedSegment();
                }
                else if (_selectedBlock != null)
                {
                    DuplicateBlock(_selectedBlock);
                }
                else if (_selectedTrack != null)
                {
                    DuplicateTrack(_selectedTrack);
                }
                evt.StopPropagation();
            }
            // 6. Delete (Delete or Backspace)
            else if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace)
            {
                if (_selectedSegment != null && _selectedAnimationTrack != null)
                {
                    _selectedAnimationTrack.DeleteSelectedSegment();
                    _selectedSegment = null;
                    _inspector?.ClearActionInspect();
                }
                else if (_selectedBlock != null && _selectedTrack != null)
                {
                    _selectedTrack.TrackData.ActionBlocks.Remove(_selectedBlock.Data);
                    _inspector.ClearActionInspect();
                    _selectedTrack.RebuildBlocks();
                    RebuildRuntimeActionBlocks();
                    EditorUtility.SetDirty(_targetAsset);
                    OnAssetModified?.Invoke();
                }
                else if (_selectedTrack != null)
                {
                    DeleteTrack(_selectedTrack);
                }
                evt.StopPropagation();
            }
        }

        private void CollectActionTypes()
        {
            _actionTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(asm =>
                {
                    try { return asm.GetTypes(); }
                    catch { return Array.Empty<Type>(); }
                })
                .Where(t => t.IsSubclassOf(typeof(MontageActionBlockBase)) && !t.IsAbstract)
                .ToArray();

            _actionTypeNames = _actionTypes.Select(t =>
            {
                var catAttr = t.GetCustomAttribute<MontageCategoryAttribute>();
                var dispAttr = t.GetCustomAttribute<MontageDisplayNameAttribute>();
                string cat = !string.IsNullOrEmpty(catAttr?.Category) ? catAttr.Category + "/" : "";
                string n = !string.IsNullOrEmpty(dispAttr?.DisplayName) ? dispAttr.DisplayName : t.Name;
                return $"{cat}{n}";
            }).ToArray();
        }

        #endregion
    }
}
