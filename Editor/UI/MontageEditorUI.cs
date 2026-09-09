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
    /// 现代化蒙太奇资产核心 UI 控制器（MontageEditorUI）。
    /// 统筹调度 3D 渲染视口、播放控制栏、时间轴缩放引擎、分段标记交互、轨道容器与侧边栏检查器。
    /// </summary>
    public class MontageEditorUI : IDisposable
    {
        #region 私有常量

        private const string PREFS_ZOOM_LEVEL_KEY = "CwcMontage_Editor_ZoomLevel";
        private const float DEFAULT_ZOOM_LEVEL = 2.5f;
        private const string PREFS_PREVIEW_ROOT_MOTION_KEY = "CwcMontage_Preview_RootMotion";
        private const string PREFS_PREVIEW_FOOT_IK_KEY = "CwcMontage_Preview_FootIK";
        private const string PREVIEW_MODEL_PREFS_KEY = "CwcMontage_LastPreviewModelGuid";
        private const string USS_GUID = "1ac56d601272aa740b6f49bbcc195f4f";
        private const string DEFAULT_DUMMY_MODEL_GUID = "a0ee339482ca65344a511b7983d67fd8";

        #endregion

        #region 私有字段

        private readonly MontageSequenceSO _targetAsset;
        private readonly SerializedObject _serializedObject;

        private VisualElement _root;
        private MontagePreviewViewportElement _viewport;
        private MontageActionInspectorElement _inspector;
        private MontageTimelineRuler _ruler;
        private MontageSectionTrackElement _sectionTrackElement;
        private MontageAnimationTrackElement _animationTrackElement;
        private ScrollView _headersScrollView;
        private VisualElement _headersContentWrapper;
        private VisualElement _reorderIndicatorLine;
        private ScrollView _tracksScrollView;
        private VisualElement _tracksContentWrapper;
        private GameObject _currentPreviewPrefab;

        // 预览控制与播放状态
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

        // Playable & 动画状态
        private PlayableGraph _playableGraph;
        private AnimationMixerPlayable _previewMixer;
        private readonly List<AnimationClipPlayable> _previewSegmentPlayables = new();
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

        // 运行时预览动作块与上下文
        private readonly List<MontageActionBlockData> _runtimeActionBlocks = new();
        private readonly HashSet<MontageActionBlockData> _activeActionBlocks = new();

        #endregion

        #region 公共事件

        public event Action OnAssetModified;
        public event Action OnRepaintRequested;

        #endregion

        #region 公共属性

        public VisualElement Root => _root;
        public MontageSequenceSO TargetAsset => _targetAsset;
        public bool IsPlaying => _isPlaying;
        public bool IsViewportInteracting => _viewport != null && _viewport.IsInteracting;

        #endregion

        #region 构造方法与初始化

        public MontageEditorUI(MontageSequenceSO targetAsset, SerializedObject serializedObject)
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

            // 注册全局按键与帧驱动 (使用全局 EditorApplication.update 确保在任何 UI 焦点/交互下永不断播)
            _root.RegisterCallback<KeyDownEvent>(OnKeyDown);
            _lastEditorTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnUpdateTick;
        }

        #endregion

        #region 公共生命周期

        public void RenderViewportImmediate() => _viewport?.RenderImmediate();

        /// <summary>
        /// 处理外部 ObjectPicker 选中的动画片段资源并添加到动画轨道。
        /// </summary>
        public void HandleAnimationPickerResult(AnimationClip picked, bool isClosed = false)
        {
            _animationTrackElement?.HandleObjectPickerResult(picked, isClosed);
        }

        public void Dispose()
        {
            EditorApplication.update -= OnUpdateTick;
            CleanupPlayablesAndPreview();
            MontageAudioPreviewUtility.StopAllClips();
            _root?.Unbind();
            _root?.Clear();
        }

        #endregion

        #region UI 结构构建

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

            // 1. 主分割视窗 (垂直分割: 上部视口/属性面板, 下部时间轴)
            var mainSplit = new TwoPaneSplitView(0, 320, TwoPaneSplitViewOrientation.Vertical);
            mainSplit.AddToClassList("montage-main-split");

            // 上部分区 (水平分割: 3D视口 + 属性检查器)
            var upperSplit = new TwoPaneSplitView(1, 360, TwoPaneSplitViewOrientation.Horizontal);
            upperSplit.AddToClassList("montage-upper-split");

            // 3D 视口容器 (上为画面，下为三段式控制条)
            var viewportContainer = new VisualElement();
            viewportContainer.style.flexGrow = 1;
            viewportContainer.style.flexShrink = 1;
            viewportContainer.style.minWidth = 100;
            viewportContainer.style.flexDirection = FlexDirection.Column;

            _viewport = new MontagePreviewViewportElement();
            _viewport.style.flexGrow = 1;
            _viewport.OnPreviewModelChanged += OnViewportModelChanged;
            _viewport.OnRootMotionToggled += OnPreviewRootMotionChanged;
            _viewport.OnFootIKToggled += OnPreviewFootIKChanged;
            _viewport.SetRootMotionState(_previewRootMotion);
            _viewport.SetFootIKState(_previewFootIK);
            viewportContainer.Add(_viewport);

            var playbackBar = CreateViewportPlaybackBar();
            viewportContainer.Add(playbackBar);

            upperSplit.Add(viewportContainer);

            // 侧边栏检查器
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

                // 1. 全局时间轴与所有轨道自适应联动（同步总时长、视口延展、标尺刻度、垂直网格与内容指示线）
                UpdateTimelineLengthsAndSync(fullRebuild: false, markDirty: false, rebuildRuntimeBlocks: true);

                // 2. 动画轨道即时重构条块几何与相邻交叉混合对角线
                _targetAsset.SortAnimationSegments();
                _animationTrackElement?.RebuildSegments();

                // 3. 所有表现轨道条块外观与提示文本即时刷新（仅轻量更新外观，不销毁重建 DOM，保持选中状态）
                for (int i = 0; i < _trackElements.Count; i++)
                {
                    _trackElements[i].UpdateBlocksVisual();
                }

                // 4. 物理分段轨道即时更新色块与分割线
                _sectionTrackElement?.SetTargetAsset(_targetAsset, _clipLength, _frameRate, _contentDuration);

                // 5. 仅在暂停/静止时立即重绘视口，播放中则由下一帧 Update 自然推进
                if (!_isPlaying)
                {
                    _viewport?.RenderImmediate();
                }

                OnAssetModified?.Invoke();
            };
            upperSplit.Add(_inspector);

            mainSplit.Add(upperSplit);

            // 2. 下部分区 (时间轴轨道区)
            var timelineContainer = CreateTimelineArea();
            mainSplit.Add(timelineContainer);

            _root.Add(mainSplit);
        }

        private VisualElement CreateViewportPlaybackBar()
        {
            var playbackBar = new VisualElement();
            playbackBar.AddToClassList("montage-playback-bar");

            // 1. 左侧区域 (时间、帧数与物理分段指示)
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

            // 2. 中间区域 (居中大播放/暂停与跳帧按钮)
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

            // 3. 右侧区域 (预览 Speed 拖拽输入 与 Loop 复选框)
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
            // A. 左侧固定轨道头部栏 (固定宽度 220px，水平方向永不滚动)
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

            var addTrackBtn = new Button(ShowAddTrackDropdownMenu) { text = "+ Track" };
            addTrackBtn.AddToClassList("montage-toolbar-btn");
            addTrackBtn.AddToClassList("montage-primary-btn");
            addTrackBtn.tooltip = "Add or Paste an animation track";
            trackListHeader.Add(addTrackBtn);

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
            // B. 右侧时间轴主视口栏 (flex: 1, 随水平/垂直双向滚动)
            // -------------------------------------------------------------
            var rightTimelineColumn = new VisualElement();
            rightTimelineColumn.AddToClassList("montage-timeline-right-column");

            // 顶部标尺 (纯净时间刻度标尺)
            _ruler = new MontageTimelineRuler();
            _ruler.OnTimeScrubbed += ScrubToTime;
            _ruler.OnPanDelta += PanTimeline;
            _ruler.OnZoomDelta += delta => HandleWheelZoomDelta(delta, null);
            rightTimelineColumn.Add(_ruler);

            // 独立分段轨道组件
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

            // 独立单条核心动画轨道组件
            _animationTrackElement = new MontageAnimationTrackElement(
                _targetAsset,
                _clipLength,
                _frameRate,
                _zoomLevel
            );
            _animationTrackElement.OnSegmentSelected += (seg, idx) =>
            {
                _selectedBlock?.SetSelected(false);
                _selectedBlock = null;
                _selectedBlockData = null;
                _inspector.InspectAnimationSegment(seg, idx, _targetAsset);
            };
            _animationTrackElement.OnDataModified += HandleAnimationTrackModified;
            _animationTrackElement.OnRequestScrubTime += ScrubToTime;

            // 轨道内容区域
            _tracksScrollView = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            _tracksScrollView.AddToClassList("montage-tracks-scroll-view");
            _tracksScrollView.RegisterCallback<MouseDownEvent>(OnBlankAreaMouseDown);
            _tracksScrollView.RegisterCallback<WheelEvent>(OnTimelineAreaWheel, TrickleDown.TrickleDown);
            _tracksScrollView.RegisterCallback<GeometryChangedEvent>(OnTracksGeometryChanged);

            // 滚动联动绑定
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

        #region 动画与 Playable 初始化

        private void InitializePreviewAndPlayables()
        {
            CleanupPlayablesAndPreview();

            // 智能解析预览模型预设 (用户指定 -> EditorPrefs 记忆 -> 自动查找项目中可用测试人偶)
            _currentPreviewPrefab = ResolvePreviewModel();
            _viewport.Initialize(_targetAsset, _currentPreviewPrefab, ref _previewObject);

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

            if (_targetAsset == null || _targetAsset.AnimationSegments == null || _targetAsset.AnimationSegments.Count == 0)
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

                var segments = _targetAsset.AnimationSegments;
                int segCount = segments.Count;

                _previewMixer = AnimationMixerPlayable.Create(_playableGraph, segCount);
                _previewSegmentPlayables.Clear();

                for (int s = 0; s < segCount; s++)
                {
                    var seg = segments[s];
                    var cp = seg?.Clip != null
                        ? AnimationClipPlayable.Create(_playableGraph, seg.Clip)
                        : default;

                    if (cp.IsValid())
                    {
                        cp.SetApplyFootIK(_previewFootIK);
                        cp.SetSpeed(1.0f);
                        _previewMixer.ConnectInput(s, cp, 0);
                    }

                    _previewMixer.SetInputWeight(s, s == 0 ? 1.0f : 0.0f);
                    _previewSegmentPlayables.Add(cp);
                }

                _playableOutput = AnimationPlayableOutput.Create(_playableGraph, "Animation", _previewAnimator);
                _playableOutput.SetSourcePlayable(_previewMixer);

                EvaluatePreviewPlayables();
                _playableGraph.Evaluate();
            }

            RebuildRuntimeActionBlocks(forceRecreate: true);
            _viewport.RenderImmediate();
        }

        private void EvaluatePreviewPlayables()
        {
            if (!_previewMixer.IsValid() || _previewSegmentPlayables.Count == 0 || _targetAsset == null) return;

            _targetAsset.EvaluateAnimationSegments(_animationTime, _tempEvalIndices, _tempEvalTimes, _tempEvalWeights);

            int total = _previewSegmentPlayables.Count;
            for (int i = 0; i < total; i++)
            {
                _previewMixer.SetInputWeight(i, 0.0f);
            }

            for (int k = 0; k < _tempEvalIndices.Count; k++)
            {
                int segIdx = _tempEvalIndices[k];
                if (segIdx >= 0 && segIdx < total)
                {
                    var cp = _previewSegmentPlayables[segIdx];
                    if (cp.IsValid())
                    {
                        cp.SetTime(_tempEvalTimes[k]);
                        cp.SetSpeed(1.0f);
                    }
                    _previewMixer.SetInputWeight(segIdx, _tempEvalWeights[k]);
                }
            }
        }

        private void HandleAnimationTrackModified()
        {
            bool structureChanged = false;
            int assetSegCount = _targetAsset?.AnimationSegments != null ? _targetAsset.AnimationSegments.Count : 0;
            if (_previewSegmentPlayables.Count != assetSegCount)
            {
                structureChanged = true;
            }
            else if (_targetAsset?.AnimationSegments != null)
            {
                for (int i = 0; i < assetSegCount; i++)
                {
                    var seg = _targetAsset.AnimationSegments[i];
                    var cp = _previewSegmentPlayables[i];
                    if (seg?.Clip == null && cp.IsValid())
                    {
                        structureChanged = true;
                        break;
                    }
                    if (seg?.Clip != null && (!cp.IsValid() || cp.GetAnimationClip() != seg.Clip))
                    {
                        structureChanged = true;
                        break;
                    }
                }
            }

            UpdateTimelineLengthsAndSync(fullRebuild: structureChanged);
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

            // 画布总宽度核心计算公式：
            // 1. 缩小全览时：保底填满当前屏幕视口宽度，消除右侧黑边与 450 帧截断，全屏无缝铺满网格与刻度，滚动条占满 100%；
            // 2. 放大巡览时：严格基于有效内容长度 contentPixels + marginPixels，滚动条手柄占比精确反映实际动画有效长度；
            // 3. 向右平移时：随视口向右滚动自适应延展，保证视野永不撞墙。
            float totalWidth = Mathf.Max(vpWidth, Mathf.Max(contentPixels + marginPixels, currentScrollX + vpWidth));
            float clipLength = totalWidth / pps;

            return (totalWidth, Mathf.Max(clipLength, 1.0f));
        }

        /// <summary>
        /// 全局统一更新时间轴时长、无限延展视界并联动同步所有轨道与标尺。
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

            _animationTrackElement?.SetTargetAsset(_targetAsset, _clipLength, _frameRate, _contentDuration);
            _animationTrackElement?.SetZoom(_zoomLevel);

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

            // 联动同步全局时间轴尺寸、视口延展与标尺刻度
            UpdateTimelineLengthsAndSync(fullRebuild: false, markDirty: false, rebuildRuntimeBlocks: false);

            // 仅同步必要组件属性，绝不销毁 PlayableGraph、模型或中断播放
            if (_targetAsset != null)
            {
                if (_previewAnimator != null)
                {
                    _previewAnimator.applyRootMotion = _previewRootMotion;
                }

                for (int i = 0; i < _previewSegmentPlayables.Count; i++)
                {
                    if (_previewSegmentPlayables[i].IsValid())
                    {
                        _previewSegmentPlayables[i].SetApplyFootIK(_previewFootIK);
                    }
                }

                _sectionTrackElement?.SetTargetAsset(_targetAsset, _clipLength, _frameRate, _contentDuration);
                _animationTrackElement?.SetTargetAsset(_targetAsset, _clipLength, _frameRate, _contentDuration);
            }

            if (!_isPlaying)
            {
                _viewport?.RenderImmediate();
            }

            OnAssetModified?.Invoke();
        }

        private GameObject ResolvePreviewModel()
        {
            // 1. 若当前会话已有显式指定
            if (_currentPreviewPrefab != null)
            {
                return _currentPreviewPrefab;
            }

            // 2. 从 EditorPrefs 全局记忆加载 (用户主动选择并持久化的模型)
            string savedGuid = EditorPrefs.GetString(PREVIEW_MODEL_PREFS_KEY, "");
            // 若本地缓存仍为旧版模型，自动重置为最新的默认模型
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

            // 3. 插件专属默认内置模型 (直接通过固定 GUID 加载)
            return AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(DEFAULT_DUMMY_MODEL_GUID));
        }

        private void OnViewportModelChanged(GameObject newModel)
        {
            _currentPreviewPrefab = newModel;

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

            InitializePreviewAndPlayables();
            _viewport.RenderImmediate();
        }

        private void CleanupPlayablesAndPreview()
        {
            _isPlaying = false;
            ExitAllActiveActionBlocks();
            MontageAudioPreviewUtility.StopAllClips();

            _previewSegmentPlayables.Clear();
            if (_previewMixer.IsValid()) _previewMixer = default;

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

        #region 轨道与动作块重建

        private void RebuildTracks()
        {
            _headersContentWrapper?.Clear();
            _tracksContentWrapper?.Clear();
            _trackElements.Clear();

            // 1. 顶部挂载系统独立分段轨道 (Section Marker Track)
            if (_sectionTrackElement != null)
            {
                _sectionTrackElement.SetTargetAsset(_targetAsset, _clipLength, _frameRate, _contentDuration);
                _headersContentWrapper?.Add(_sectionTrackElement.HeaderElement);
                _tracksContentWrapper?.Add(_sectionTrackElement.ContentElement);
            }

            // 2. 挂载核心单动画主干轨道 (Animation Track)
            if (_animationTrackElement != null)
            {
                _animationTrackElement.SetTargetAsset(_targetAsset, _clipLength, _frameRate, _contentDuration);
                _headersContentWrapper?.Add(_animationTrackElement.HeaderElement);
                _tracksContentWrapper?.Add(_animationTrackElement.ContentElement);
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

                    // 构造后如果有正在选中的块，恢复高亮
                    if (_selectedBlockData != null)
                    {
                        trackElement.RebuildBlocks(_selectedBlockData);
                    }
                }
            }

            // 重新绑定 _selectedBlock 引用到新生成的 Element 实例
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

            // 1. 收集当前资产中所有有效源动作块
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

            // 2. 清理已在资产中删除或已禁用的运行块
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

            // 3. 对比并增量同步现存或新增的动作块
            for (int i = 0; i < validSourceBlocks.Count; i++)
            {
                var src = validSourceBlocks[i];
                var existingRb = _runtimeActionBlocks.Find(r => r.SourceData == src);

                if (existingRb != null)
                {
                    // 同步起止时间与帧范围
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
                        // 原地覆盖序列化参数，保持非序列化预览字段（如现有实例）不受影响
                        SyncActionParameters(src.Action, existingRb.Action);

                        // 若正处于预览中，触发原地更新 Transform，杜绝销毁与闪烁
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
                Debug.LogWarning($"[CwcMontage] 同步动作块参数异常: {ex.Message}");
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

        #region 缩放与平移控制

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
            if (evt.shiftKey) // Shift + 滚轮：右侧区域允许显式垂直平滑滚动
            {
                if (_tracksScrollView != null)
                {
                    float newY = Mathf.Max(0f, _tracksScrollView.scrollOffset.y + evt.delta.y * 28f);
                    _tracksScrollView.scrollOffset = new Vector2(_tracksScrollView.scrollOffset.x, newY);
                }
            }
            else // 默认直接滚轮：纯粹进行横向时间轴缩放，绝对禁止垂直滚动
            {
                float zoomMultiplier = evt.delta.y < 0 ? 1.15f : (1f / 1.15f);
                float oldZoom = _zoomLevel;
                float newZoom = Mathf.Clamp(oldZoom * zoomMultiplier, 0.005f, 20f);

                if (!Mathf.Approximately(oldZoom, newZoom))
                {
                    // 记录当前的垂直滚动位移，缩放过程中完全锁定
                    float savedScrollY = _tracksScrollView != null ? _tracksScrollView.scrollOffset.y : 0f;

                    Vector2 localMouse = _tracksContentWrapper.WorldToLocal(evt.mousePosition);
                    float mouseTime = Mathf.Max(0f, localMouse.x / (200f * oldZoom));

                    // 调整水平滚动位移，保持鼠标所在的时间点在视口中不发生突变
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

        #region 播放与 Tick 循环

        private void OnUpdateTick()
        {
            if (_targetAsset == null || (_targetAsset.TotalDuration <= 0f && _clipLength <= 0f)) return;

            double currentEditorTime = EditorApplication.timeSinceStartup;
            float deltaTime = Mathf.Min((float)(currentEditorTime - _lastEditorTime), 0.1f);
            _lastEditorTime = currentEditorTime;

            float playDuration = _contentDuration > 0.001f ? _contentDuration : _clipLength;
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
            float playDuration = _contentDuration > 0.001f ? _contentDuration : _clipLength;
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

            for (int i = 0; i < _previewSegmentPlayables.Count; i++)
            {
                var cp = _previewSegmentPlayables[i];
                if (cp.IsValid())
                {
                    cp.SetApplyFootIK(enabled);
                }
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
                if (_previewMixer.IsValid())
                {
                    EvaluatePreviewPlayables();
                }
                _playableGraph.Evaluate();
            }

            // 区间扫掠评估所有动作块（调用专用视口预览生命周期）
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

                            // 非播放状态下首次进入（如拖拽/跳转/单帧），立即精确还原到当前帧对应的粒子切片
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
                            // 暂停/拖动时间轴时，执行绝对时间切片采样
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

        #region 分段切分点操作

        private void AddSplitTimestamp(float time)
        {
            if (_targetAsset == null) return;

            var splits = new List<float>(_targetAsset.SplitTimestamps);
            splits.Add(time);
            _targetAsset.SetSplitTimestamps(splits);

            UpdateTimelineLengthsAndSync(fullRebuild: false);
        }

        private void MoveSplitTimestamp(int splitIndex, float newTime)
        {
            if (_targetAsset?.SplitTimestamps == null || splitIndex < 0 || splitIndex >= _targetAsset.SplitTimestamps.Count) return;

            var splits = new List<float>(_targetAsset.SplitTimestamps);
            splits[splitIndex] = newTime;
            _targetAsset.SetSplitTimestamps(splits);

            UpdateTimelineLengthsAndSync(fullRebuild: false);
        }

        private void RemoveSplitTimestamp(int splitIndex)
        {
            if (_targetAsset?.SplitTimestamps == null || splitIndex < 0 || splitIndex >= _targetAsset.SplitTimestamps.Count) return;

            var splits = new List<float>(_targetAsset.SplitTimestamps);
            splits.RemoveAt(splitIndex);
            _targetAsset.SetSplitTimestamps(splits);

            UpdateTimelineLengthsAndSync(fullRebuild: false);
        }

        #endregion

        #region 轨道与动作块操作

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
            _animationTrackElement?.ClearSelection();
            SelectBlockVisual(block);
            if (block != null && block.TrackIndex >= 0 && block.TrackIndex < _trackElements.Count)
            {
                SelectTrack(_trackElements[block.TrackIndex]);
            }
            _inspector?.InspectActionBlock(block);
        }

        public void AddNewTrack()
        {
            _targetAsset.Tracks.Add(new MontageTrackData($"Track {_targetAsset.Tracks.Count + 1}"));
            RebuildTracks();
            EditorUtility.SetDirty(_targetAsset);
            OnAssetModified?.Invoke();
        }

        public void InsertTrackBelow(MontageTrackElement track)
        {
            int index = track != null ? track.TrackIndex + 1 : _targetAsset.Tracks.Count;
            index = Mathf.Clamp(index, 0, _targetAsset.Tracks.Count);

            _targetAsset.Tracks.Insert(index, new MontageTrackData($"Track {index + 1}"));
            RebuildTracks();
            EditorUtility.SetDirty(_targetAsset);
            OnAssetModified?.Invoke();
        }

        public void PasteCopiedTrackAsNew()
        {
            var cloned = MontageClipboard.GetClonedTrack();
            if (cloned == null) return;

            _targetAsset.Tracks.Add(cloned);
            RebuildTracks();
            EditorUtility.SetDirty(_targetAsset);
            OnAssetModified?.Invoke();
        }

        public void PasteNewTrackBelow(MontageTrackElement track)
        {
            var cloned = MontageClipboard.GetClonedTrack();
            if (cloned == null) return;

            int index = track != null ? track.TrackIndex + 1 : _targetAsset.Tracks.Count;
            index = Mathf.Clamp(index, 0, _targetAsset.Tracks.Count);

            _targetAsset.Tracks.Insert(index, cloned);
            RebuildTracks();
            EditorUtility.SetDirty(_targetAsset);
            OnAssetModified?.Invoke();
        }

        public void DuplicateTrack(MontageTrackElement track)
        {
            if (track?.TrackData == null) return;

            var cloned = track.TrackData.Clone();
            int insertIndex = track.TrackIndex + 1;
            _targetAsset.Tracks.Insert(insertIndex, cloned);
            RebuildTracks();
            EditorUtility.SetDirty(_targetAsset);
            OnAssetModified?.Invoke();
        }

        private void DuplicateBlock(MontageActionBlockElement block)
        {
            if (block?.Data == null || block.TrackIndex < 0 || block.TrackIndex >= _targetAsset.Tracks.Count) return;

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
        }

        private void AddBlockToTrack(MontageTrackElement track, Type actionType, float timeAtClick)
        {
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
        }

        private void PasteBlockToTrack(MontageTrackElement track, float timeAtClick)
        {
            var cloned = MontageClipboard.GetClonedBlock();
            if (cloned == null || cloned.Action == null) return;

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
        }

        private void PasteTrackToTrack(MontageTrackElement track)
        {
            var clonedTrack = MontageClipboard.GetClonedTrack();
            if (clonedTrack == null) return;

            _targetAsset.Tracks[track.TrackIndex] = clonedTrack;
            UpdateTimelineLengthsAndSync(fullRebuild: false);
            RebuildTracks();
        }

        private void DeleteTrack(MontageTrackElement track)
        {
            if (track.TrackIndex >= 0 && track.TrackIndex < _targetAsset.Tracks.Count)
            {
                _targetAsset.Tracks.RemoveAt(track.TrackIndex);
                _inspector.ClearActionInspect();
                UpdateTimelineLengthsAndSync(fullRebuild: false);
                RebuildTracks();
            }
        }

        private void MoveTrack(MontageTrackElement track, int delta)
        {
            int oldIndex = track.TrackIndex;
            int newIndex = oldIndex + delta;
            if (newIndex < 0 || newIndex >= _targetAsset.Tracks.Count) return;

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

            // 1. 所见即所得逐帧求值：将播放头与视口实时定位到当前拖拽边缘的时间点
            if (!_isPlaying && activeEdgeTime >= 0f)
            {
                _animationTime = Mathf.Clamp(activeEdgeTime, 0f, Mathf.Max(_clipLength, _targetAsset.TotalDuration));
                _lastAnimationTime = _animationTime;
            }

            // 2. 全局统一更新时间轴、标尺、视界延展、运行时动作块增量同步与 3D 视口实时求值渲染
            UpdateTimelineLengthsAndSync(fullRebuild: false, markDirty: false, rebuildRuntimeBlocks: true);

            // 3. 属性面板 60 FPS 无锁数字更新（轻量快速路径）
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
            // 若当前焦点处于文本输入控件内部，放行输入事件，绝不拦截播放/控制快捷键
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

            // 1. Ctrl / Cmd + S：保存资产
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

            // 2. 播放控制通用快捷键
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
            // 3. 复制 (必须为 Ctrl+C / Cmd+C)
            else if (evt.actionKey && evt.keyCode == KeyCode.C)
            {
                if (_selectedBlock != null)
                {
                    MontageClipboard.CopyActionBlock(_selectedBlock.Data);
                }
                else if (_selectedTrack != null)
                {
                    MontageClipboard.CopyTrack(_selectedTrack.TrackData);
                }
                evt.StopPropagation();
            }
            // 4. 粘贴 (必须为 Ctrl+V / Cmd+V)
            else if (evt.actionKey && evt.keyCode == KeyCode.V)
            {
                if (MontageClipboard.HasCopiedTrack && _selectedBlock == null)
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
            // 5. 复制副本 (Ctrl+D)
            else if (evt.actionKey && evt.keyCode == KeyCode.D)
            {
                if (_selectedBlock != null)
                {
                    DuplicateBlock(_selectedBlock);
                }
                else if (_selectedTrack != null)
                {
                    DuplicateTrack(_selectedTrack);
                }
                evt.StopPropagation();
            }
            // 6. 删除 (Delete 或 Backspace)
            else if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace)
            {
                if (_selectedBlock != null && _selectedTrack != null)
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
