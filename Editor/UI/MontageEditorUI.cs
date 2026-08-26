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
        private const string DEFAULT_PLUGIN_PREFAB_PATH = "Assets/CwcPlugins/CwcMontage/Editor/Models/Character/TestCharacter.prefab";
        private const string DEFAULT_PLUGIN_FBX_PATH = "Assets/CwcPlugins/CwcMontage/Editor/Models/Character/X Bot.fbx";

        #endregion

        #region 私有字段

        private readonly MontageSequenceSO _targetAsset;
        private readonly SerializedObject _serializedObject;

        private VisualElement _root;
        private MontagePreviewViewportElement _viewport;
        private MontageActionInspectorElement _inspector;
        private MontageTimelineRuler _ruler;
        private MontageSectionTrackElement _sectionTrackElement;
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
        private AnimationClipPlayable _clipPlayable;
        private AnimationPlayableOutput _playableOutput;
        private Animator _previewAnimator;
        private GameObject _previewObject;

        private bool _isPlaying;
        private float _animationTime;
        private float _lastAnimationTime;
        private double _lastEditorTime;
        private float _clipLength = 1f;
        private float _frameRate = 30f;
        private int _totalFrames = 30;

        private Type[] _actionTypes;
        private string[] _actionTypeNames;

        private readonly List<MontageTrackElement> _trackElements = new();
        private MontageActionBlockElement _selectedBlock;
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
            _zoomLevel = Mathf.Clamp(_zoomLevel, 0.1f, 15.0f);
            _previewRootMotion = EditorPrefs.GetBool(PREFS_PREVIEW_ROOT_MOTION_KEY, false);
            _previewFootIK = EditorPrefs.GetBool(PREFS_PREVIEW_FOOT_IK_KEY, true);

            CollectActionTypes();
            BuildUIHierarchy();
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

        public void Dispose()
        {
            EditorApplication.update -= OnUpdateTick;
            CleanupPlayablesAndPreview();
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

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/CwcPlugins/CwcMontage/Editor/Styles/MontageEditor.uss");
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
            _inspector.OnAnimationClipChanged += HandleAnimationClipChanged;
            _inspector.OnAssetSettingsModified += HandleGeneralAssetSettingsModified;
            _inspector.OnDataModified += () =>
            {
                _serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(_targetAsset);
                RebuildRuntimeActionBlocks();

                // 仅在暂停/静止时立即求值与重绘，播放中则由下一帧 Update 自然更新，杜绝打断连续播放
                if (!_isPlaying)
                {
                    ExitAllActiveActionBlocks();
                    EvaluateTimeAndPreviewLogic(0f);
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

            _headersContentWrapper = new VisualElement();
            _headersContentWrapper.AddToClassList("montage-track-headers-content-wrapper");

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

            // 轨道内容区域
            _tracksScrollView = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            _tracksScrollView.AddToClassList("montage-tracks-scroll-view");
            _tracksScrollView.RegisterCallback<MouseDownEvent>(OnTracksScrollViewMouseDown);
            _tracksScrollView.RegisterCallback<WheelEvent>(OnTimelineAreaWheel, TrickleDown.TrickleDown);

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

            if (_targetAsset == null || _targetAsset.AnimationClip == null)
            {
                _clipLength = 1f;
                _frameRate = 30f;
                _totalFrames = 30;
                _ruler.SetTimelineData(_targetAsset, 0f, _clipLength, _frameRate, _zoomLevel);
                _viewport.RenderImmediate();
                return;
            }

            _clipLength = Mathf.Max(0.001f, _targetAsset.AnimationClip.length);
            _frameRate = Mathf.Max(1f, _targetAsset.AnimationClip.frameRate);
            _totalFrames = Mathf.Max(1, Mathf.RoundToInt(_clipLength * _frameRate));

            _ruler.SetTimelineData(_targetAsset, _animationTime, _clipLength, _frameRate, _zoomLevel);

            if (_previewObject != null)
            {
                _previewAnimator = _previewObject.GetComponent<Animator>() ?? _previewObject.AddComponent<Animator>();
                _previewAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _previewAnimator.applyRootMotion = _previewRootMotion;

                _playableGraph = PlayableGraph.Create("CwcMontageEditorPreviewGraph");
                _playableGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

                _clipPlayable = AnimationClipPlayable.Create(_playableGraph, _targetAsset.AnimationClip);
                _clipPlayable.SetApplyFootIK(_previewFootIK);

                _playableOutput = AnimationPlayableOutput.Create(_playableGraph, "Animation", _previewAnimator);
                _playableOutput.SetSourcePlayable(_clipPlayable);

                _clipPlayable.SetTime(_animationTime);
                _playableGraph.Evaluate();
            }

            RebuildRuntimeActionBlocks();
            _viewport.RenderImmediate();
        }

        private void HandleAnimationClipChanged()
        {
            bool wasPlaying = _isPlaying;

            if (_serializedObject != null)
            {
                _serializedObject.Update();
            }

            EditorUtility.SetDirty(_targetAsset);
            InitializePreviewAndPlayables();
            RebuildTracks();
            _sectionTrackElement?.SetTargetAsset(_targetAsset, _clipLength, _frameRate);
            UpdateToolbarTimeDisplay();

            if (wasPlaying && _targetAsset?.AnimationClip != null)
            {
                _isPlaying = true;
                _lastEditorTime = EditorApplication.timeSinceStartup;
                UpdatePlayPauseVisual();
            }

            _viewport?.RenderImmediate();
            OnAssetModified?.Invoke();
        }

        private void HandleGeneralAssetSettingsModified()
        {
            if (_serializedObject != null)
            {
                _serializedObject.Update();
            }

            EditorUtility.SetDirty(_targetAsset);

            // 仅同步必要组件属性，绝不销毁 PlayableGraph、模型或中断播放
            if (_targetAsset != null)
            {
                if (_previewAnimator != null)
                {
                    _previewAnimator.applyRootMotion = _previewRootMotion;
                }

                if (_clipPlayable.IsValid())
                {
                    _clipPlayable.SetApplyFootIK(_previewFootIK);
                }

                _sectionTrackElement?.SetTargetAsset(_targetAsset, _clipLength, _frameRate);
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

            // 3. 插件专属固定内置模型 (完全自包含，精准查找，无任何外部模糊搜索)
            var defaultPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DEFAULT_PLUGIN_PREFAB_PATH);
            if (defaultPrefab != null)
            {
                return defaultPrefab;
            }

            var defaultFbx = AssetDatabase.LoadAssetAtPath<GameObject>(DEFAULT_PLUGIN_FBX_PATH);
            if (defaultFbx != null)
            {
                return defaultFbx;
            }

            // 4. 若未配置且插件路径下无模型，直接返回 null，不进行任何全局搜索
            return null;
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
            MontageObjectPool.ClearPreviewPool();

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
                _sectionTrackElement.SetTargetAsset(_targetAsset, _clipLength, _frameRate);
                _headersContentWrapper?.Add(_sectionTrackElement.HeaderElement);
                _tracksContentWrapper?.Add(_sectionTrackElement.ContentElement);
            }

            if (_targetAsset?.Tracks == null || _targetAsset.Tracks.Count == 0)
            {
                ShowEmptyTracksGuide();
                return;
            }

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
                    RebuildRuntimeActionBlocks();
                    EditorUtility.SetDirty(_targetAsset);
                    OnAssetModified?.Invoke();
                };

                _trackElements.Add(trackElement);
                _headersContentWrapper?.Add(trackElement.HeaderElement);
                _tracksContentWrapper?.Add(trackElement.ContentElement);
            }

            if (_reorderIndicatorLine != null)
            {
                _headersContentWrapper?.Add(_reorderIndicatorLine);
            }

            RebuildRuntimeActionBlocks();
        }

        private void ShowEmptyTracksGuide()
        {
            var container = new VisualElement();
            container.AddToClassList("montage-empty-tracks-container");

            var card = new VisualElement();
            card.AddToClassList("montage-empty-tracks-card");

            var title = new Label("No Performance Tracks");
            title.AddToClassList("montage-empty-tracks-title");
            card.Add(title);

            var desc = new Label("Click below to add a performance track for audio, VFX, camera shake or hitstop.");
            desc.AddToClassList("montage-empty-tracks-desc");
            card.Add(desc);

            var btnGroup = new VisualElement();
            btnGroup.AddToClassList("montage-empty-tracks-btn-group");

            var addBtn = new Button(AddNewTrack) { text = "+ Add New Track" };
            addBtn.AddToClassList("montage-toolbar-btn");
            addBtn.AddToClassList("montage-primary-btn");
            addBtn.AddToClassList("montage-empty-tracks-btn");
            btnGroup.Add(addBtn);

            if (MontageClipboard.HasCopiedTrack)
            {
                var pasteBtn = new Button(PasteCopiedTrackAsNew) { text = "Paste Copied Track" };
                pasteBtn.AddToClassList("montage-toolbar-btn");
                pasteBtn.AddToClassList("montage-empty-tracks-btn");
                btnGroup.Add(pasteBtn);
            }

            card.Add(btnGroup);
            container.Add(card);
            _tracksContentWrapper.Add(container);
        }

        private void RebuildRuntimeActionBlocks()
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
        }

        #endregion

        #region 缩放与平移控制

        private void SetZoom(float newZoom)
        {
            _zoomLevel = Mathf.Clamp(newZoom, 0.1f, 15.0f);
            EditorPrefs.SetFloat(PREFS_ZOOM_LEVEL_KEY, _zoomLevel);

            _ruler?.SetZoom(_zoomLevel);
            _sectionTrackElement?.SetZoom(_zoomLevel);

            for (int i = 0; i < _trackElements.Count; i++)
            {
                _trackElements[i].SetZoom(_zoomLevel);
            }
        }

        private void HandleWheelZoomDelta(float delta, Vector2? mousePos)
        {
            float zoomMultiplier = delta > 0 ? 1.15f : (1f / 1.15f);
            float oldZoom = _zoomLevel;
            float newZoom = Mathf.Clamp(oldZoom * zoomMultiplier, 0.1f, 15f);
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
                float newZoom = Mathf.Clamp(oldZoom * zoomMultiplier, 0.1f, 15f);

                if (!Mathf.Approximately(oldZoom, newZoom))
                {
                    // 记录当前的垂直滚动位移，缩放过程中完全锁定
                    float savedScrollY = _tracksScrollView != null ? _tracksScrollView.scrollOffset.y : 0f;

                    Vector2 localMouse = _tracksContentWrapper.WorldToLocal(evt.mousePosition);
                    float mouseTime = Mathf.Max(0f, localMouse.x / (200f * oldZoom));

                    SetZoom(newZoom);

                    // 调整水平滚动位移，保持鼠标所在的时间点在视口中不发生突变
                    float newMouseX = mouseTime * (200f * newZoom);
                    float viewportMouseX = evt.mousePosition.x - _tracksScrollView.worldBound.xMin;
                    float targetScrollX = Mathf.Max(0f, newMouseX - viewportMouseX);

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
                _tracksScrollView.scrollOffset = new Vector2(
                    Mathf.Max(0f, _tracksScrollView.scrollOffset.x - deltaX),
                    _tracksScrollView.scrollOffset.y);
            }
        }

        #endregion

        #region 播放与 Tick 循环

        private void OnUpdateTick()
        {
            if (_targetAsset?.AnimationClip == null) return;

            double currentEditorTime = EditorApplication.timeSinceStartup;
            float deltaTime = Mathf.Min((float)(currentEditorTime - _lastEditorTime), 0.1f);
            _lastEditorTime = currentEditorTime;

            if (_isPlaying && _clipLength > 0f)
            {
                float step = deltaTime * _previewSpeed;
                _animationTime += step;

                if (_animationTime >= _clipLength)
                {
                    if (_previewLoop)
                    {
                        _animationTime %= _clipLength;
                        ExitAllActiveActionBlocks();
                        ResetPreviewModelTransform();
                    }
                    else
                    {
                        _animationTime = _clipLength;
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
            _isPlaying = !_isPlaying;
            if (_isPlaying)
            {
                if (_animationTime >= _clipLength)
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

            if (_clipPlayable.IsValid())
            {
                _clipPlayable.SetApplyFootIK(enabled);
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
            ScrubToTime(_clipLength);
        }

        private void EvaluateTimeAndPreviewLogic(float deltaTime)
        {
            if (_clipPlayable.IsValid() && _playableGraph.IsValid())
            {
                _clipPlayable.SetTime(_animationTime);
                _playableGraph.Evaluate();
            }

            // 区间扫掠评估所有动作块
            var context = CreateCurrentContext();

            for (int i = 0; i < _runtimeActionBlocks.Count; i++)
            {
                var blockData = _runtimeActionBlocks[i];
                if (blockData?.Action == null || !blockData.IsEnabled) continue;

                var action = blockData.Action;
                bool isInside = _animationTime >= blockData.StartTime && _animationTime < blockData.EndTime;

                if (isInside)
                {
                    if (!_activeActionBlocks.Contains(blockData))
                    {
                        if (action.CanEnter(context))
                        {
                            action.OnEnter(context);
                            _activeActionBlocks.Add(blockData);
                        }
                    }
                    else
                    {
                        action.OnUpdate(context, deltaTime);
                    }
                }
                else if (_activeActionBlocks.Contains(blockData))
                {
                    action.OnExit(context);
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
                block?.Action?.OnExit(context);
            }
            _activeActionBlocks.Clear();
        }

        private MontageActionContext CreateCurrentContext()
        {
            int curSection = _targetAsset != null ? _targetAsset.GetSectionIndexAtTime(_animationTime) : 0;
            var (start, end) = _targetAsset != null ? _targetAsset.GetSectionRange(curSection) : (start: 0f, end: _clipLength);
            float sectionLen = Mathf.Max(0.0001f, end - start);
            float sectionProgress = Mathf.Clamp01((_animationTime - start) / sectionLen);
            float normProgress = _clipLength > 0.0001f ? Mathf.Clamp01(_animationTime / _clipLength) : 0f;

            return new MontageActionContext(
                _previewObject,
                _previewAnimator,
                _animationTime,
                _clipLength,
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
            if (_timeValueLabel != null)
            {
                _timeValueLabel.text = $"{_animationTime:F2}s / {_clipLength:F2}s";
            }
            if (_frameValueLabel != null)
            {
                int curFrame = Mathf.FloorToInt(_animationTime * _frameRate);
                _frameValueLabel.text = $"F {curFrame} / {_totalFrames}";
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

            _sectionTrackElement?.SetTargetAsset(_targetAsset, _clipLength, _frameRate);
            for (int i = 0; i < _trackElements.Count; i++)
            {
                _trackElements[i].ContentElement.MarkDirtyRepaint();
            }

            EditorUtility.SetDirty(_targetAsset);
            OnAssetModified?.Invoke();
        }

        private void MoveSplitTimestamp(int splitIndex, float newTime)
        {
            if (_targetAsset?.SplitTimestamps == null || splitIndex < 0 || splitIndex >= _targetAsset.SplitTimestamps.Count) return;

            var splits = new List<float>(_targetAsset.SplitTimestamps);
            splits[splitIndex] = newTime;
            _targetAsset.SetSplitTimestamps(splits);

            _sectionTrackElement?.SetTargetAsset(_targetAsset, _clipLength, _frameRate);
            for (int i = 0; i < _trackElements.Count; i++)
            {
                _trackElements[i].ContentElement.MarkDirtyRepaint();
            }

            EditorUtility.SetDirty(_targetAsset);
            OnAssetModified?.Invoke();
        }

        private void RemoveSplitTimestamp(int splitIndex)
        {
            if (_targetAsset?.SplitTimestamps == null || splitIndex < 0 || splitIndex >= _targetAsset.SplitTimestamps.Count) return;

            var splits = new List<float>(_targetAsset.SplitTimestamps);
            splits.RemoveAt(splitIndex);
            _targetAsset.SetSplitTimestamps(splits);

            _sectionTrackElement?.SetTargetAsset(_targetAsset, _clipLength, _frameRate);
            for (int i = 0; i < _trackElements.Count; i++)
            {
                _trackElements[i].ContentElement.MarkDirtyRepaint();
            }

            EditorUtility.SetDirty(_targetAsset);
            OnAssetModified?.Invoke();
        }

        #endregion

        #region 轨道与动作块操作

        private void SelectTrack(MontageTrackElement track)
        {
            _selectedTrack?.SetSelected(false);
            _selectedTrack = track;
            _selectedTrack?.SetSelected(true);
        }

        private void SelectBlock(MontageActionBlockElement block)
        {
            _selectedBlock?.SetSelected(false);
            _selectedBlock = block;
            _selectedBlock?.SetSelected(true);

            _inspector.InspectActionBlock(block);
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
            int duration = cloned.EndFrame - cloned.StartFrame;
            int newStart = block.Data.EndFrame;
            int newEnd = Mathf.Min(newStart + duration, _totalFrames);
            if (newEnd <= newStart) newEnd = newStart + 1;

            cloned.StartFrame = newStart;
            cloned.EndFrame = newEnd;
            cloned.StartTime = newStart / _frameRate;
            cloned.EndTime = newEnd / _frameRate;

            _targetAsset.Tracks[block.TrackIndex].ActionBlocks.Add(cloned);
            RebuildTracks();
            EditorUtility.SetDirty(_targetAsset);
            OnAssetModified?.Invoke();
        }

        private void AddBlockToTrack(MontageTrackElement track, Type actionType, float timeAtClick)
        {
            int startFrame = Mathf.RoundToInt(timeAtClick * _frameRate);
            int endFrame = Mathf.Min(startFrame + 5, _totalFrames);
            if (endFrame <= startFrame) endFrame = startFrame + 1;

            var newBlock = new MontageActionBlockData
            {
                StartFrame = startFrame,
                EndFrame = endFrame,
                StartTime = startFrame / _frameRate,
                EndTime = endFrame / _frameRate,
                Action = (MontageActionBlockBase)Activator.CreateInstance(actionType)
            };

            track.TrackData.ActionBlocks.Add(newBlock);
            track.RebuildBlocks();
            RebuildRuntimeActionBlocks();
            EditorUtility.SetDirty(_targetAsset);
            OnAssetModified?.Invoke();
        }

        private void PasteBlockToTrack(MontageTrackElement track, float timeAtClick)
        {
            var cloned = MontageClipboard.GetClonedBlock();
            if (cloned == null || cloned.Action == null) return;

            int duration = Mathf.Max(1, cloned.EndFrame - cloned.StartFrame);
            int startFrame = Mathf.RoundToInt(timeAtClick * _frameRate);
            int endFrame = Mathf.Min(startFrame + duration, _totalFrames);

            cloned.StartFrame = startFrame;
            cloned.EndFrame = endFrame;
            cloned.StartTime = startFrame / _frameRate;
            cloned.EndTime = endFrame / _frameRate;

            track.TrackData.ActionBlocks.Add(cloned);
            track.RebuildBlocks();
            RebuildRuntimeActionBlocks();
            EditorUtility.SetDirty(_targetAsset);
            OnAssetModified?.Invoke();
        }

        private void PasteTrackToTrack(MontageTrackElement track)
        {
            var clonedTrack = MontageClipboard.GetClonedTrack();
            if (clonedTrack == null) return;

            _targetAsset.Tracks[track.TrackIndex] = clonedTrack;
            RebuildTracks();
            EditorUtility.SetDirty(_targetAsset);
            OnAssetModified?.Invoke();
        }

        private void DeleteTrack(MontageTrackElement track)
        {
            if (track.TrackIndex >= 0 && track.TrackIndex < _targetAsset.Tracks.Count)
            {
                _targetAsset.Tracks.RemoveAt(track.TrackIndex);
                _inspector.ClearActionInspect();
                RebuildTracks();
                EditorUtility.SetDirty(_targetAsset);
                OnAssetModified?.Invoke();
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

        private void ShowAddTrackDropdownMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Add Empty Track"), false, AddNewTrack);

            if (MontageClipboard.HasCopiedTrack)
            {
                menu.AddItem(new GUIContent("Paste Copied Track as New"), false, PasteCopiedTrackAsNew);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Paste Copied Track as New"));
            }

            menu.ShowAsContext();
        }

        private void OnTracksScrollViewMouseDown(MouseDownEvent evt)
        {
            if (evt.button == 1)
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

                if (_targetAsset.Tracks.Count > 0)
                {
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent("Clear All Tracks"), false, () =>
                    {
                        if (EditorUtility.DisplayDialog("Clear All Tracks", "Are you sure you want to remove all tracks?", "Yes", "Cancel"))
                        {
                            _targetAsset.Tracks.Clear();
                            _inspector.ClearActionInspect();
                            RebuildTracks();
                            EditorUtility.SetDirty(_targetAsset);
                            OnAssetModified?.Invoke();
                        }
                    });
                }

                menu.DropDown(new Rect(evt.mousePosition, Vector2.zero));
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
