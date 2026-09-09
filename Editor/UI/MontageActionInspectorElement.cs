using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Cwcbb.Tools.CwcMontage;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// 独立动作块与资产属性检查器面板（MontageActionInspectorElement）。
    /// 将 Action 字段从 3D 视口彻底剥离，在侧边栏提供现代 Unity 风格的可折叠卡片属性检查体验。
    /// </summary>
    public class MontageActionInspectorElement : VisualElement
    {
        #region 私有静态常量

        private static readonly HashSet<string> s_spatialPropertyNames = new()
        {
            "_targetBone",
            "_attachMode",
            "_positionOffset",
            "_rotationOffset",
            "_scale"
        };

        #endregion

        #region 私有字段

        private MontageSequenceSO _targetAsset;
        private SerializedObject _serializedAsset;
        private MontageActionBlockElement _currentSelectedBlock;

        private ScrollView _scrollView;
        private VisualElement _actionContainer;
        private VisualElement _assetSettingsContainer;
        private Button _actionTabButton;
        private Button _assetTabButton;
        private int _currentTabIndex = 0;
        private Toggle _actionEnabledToggle;
        private FloatField _actionClipStartField;
        private FloatField _actionClipEndField;
        private IntegerField _actionStartFrameField;
        private IntegerField _actionEndFrameField;
        private Label _actionTimingHint;
        private Label _clipSpeedInfoLabel;
        private Label _actionTargetBadge;

        #endregion

        #region 公共属性

        public int CurrentTabIndex => _currentTabIndex;

        #endregion

        #region 公共事件

        public event Action OnDataModified;
        public event Action OnAssetSettingsModified;
        public event Action OnAnimationSegmentClipChanged;

        #endregion

        #region 构造方法

        public MontageActionInspectorElement()
        {
            AddToClassList("montage-inspector-sidebar");

            var header = new VisualElement();
            header.AddToClassList("montage-inspector-header");

            var title = new Label("Inspector");
            title.AddToClassList("montage-inspector-title");
            header.Add(title);
            Add(header);

            // 标签切换栏 (兼容全版本 Unity，吸附固定于顶部)
            var tabHeader = new VisualElement();
            tabHeader.AddToClassList("montage-inspector-tabs");

            _actionTabButton = new Button(() => SelectTab(0)) { text = "Action Block" };
            _actionTabButton.AddToClassList("montage-inspector-tab-btn");

            _assetTabButton = new Button(() => SelectTab(1)) { text = "Asset Settings" };
            _assetTabButton.AddToClassList("montage-inspector-tab-btn");

            tabHeader.Add(_actionTabButton);
            tabHeader.Add(_assetTabButton);
            Add(tabHeader);

            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _scrollView.AddToClassList("montage-inspector-body");
            Add(_scrollView);

            _actionContainer = new VisualElement();
            _scrollView.Add(_actionContainer);

            _assetSettingsContainer = new VisualElement();
            _scrollView.Add(_assetSettingsContainer);

            SelectTab(0);
            ShowEmptyActionHint();
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 切换侧边栏标签页 (0: Action Block, 1: Asset Settings)。
        /// 纯原生 UI Toolkit 实现，100% 兼容 Unity 2021/2022/2023/Unity 6 全版本。
        /// </summary>
        /// <param name="index">目标标签页索引</param>
        public void SelectTab(int index)
        {
            _currentTabIndex = Mathf.Clamp(index, 0, 1);
            if (_currentTabIndex == 0)
            {
                _actionTabButton?.AddToClassList("montage-inspector-tab-btn-active");
                _assetTabButton?.RemoveFromClassList("montage-inspector-tab-btn-active");
                if (_actionContainer != null) _actionContainer.style.display = DisplayStyle.Flex;
                if (_assetSettingsContainer != null) _assetSettingsContainer.style.display = DisplayStyle.None;
            }
            else
            {
                _actionTabButton?.RemoveFromClassList("montage-inspector-tab-btn-active");
                _assetTabButton?.AddToClassList("montage-inspector-tab-btn-active");
                if (_actionContainer != null) _actionContainer.style.display = DisplayStyle.None;
                if (_assetSettingsContainer != null) _assetSettingsContainer.style.display = DisplayStyle.Flex;
            }
        }

        /// <summary>
        /// 绑定当前编辑的蒙太奇资产。
        /// </summary>
        public void BindAsset(MontageSequenceSO asset, SerializedObject serializedAsset)
        {
            _targetAsset = asset;
            _serializedAsset = serializedAsset;
            _currentSelectedBlock = null;

            RebuildAssetSettingsTab();
            ShowEmptyActionHint();
        }

        /// <summary>
        /// 选中并展示指定 ActionBlock 的详细字段。
        /// </summary>
        public void InspectActionBlock(MontageActionBlockElement blockElement)
        {
            if (blockElement?.Data == null || blockElement.Data.Action == null || _serializedAsset == null)
            {
                _currentSelectedBlock = null;
                _actionEnabledToggle = null;
                _actionClipStartField = null;
                _actionClipEndField = null;
                _actionStartFrameField = null;
                _actionEndFrameField = null;
                _actionTimingHint = null;
                _clipSpeedInfoLabel = null;
                ShowEmptyActionHint();
                return;
            }

            // 轻量快速路径：若当前正在检查同一动作块，仅静默更新帧数与提示，绝不重新 Clear()、Bind() 和切 Tab
            if (_currentSelectedBlock == blockElement && _actionStartFrameField != null && _actionEndFrameField != null && _actionContainer.childCount > 0)
            {
                _serializedAsset?.Update();
                _actionEnabledToggle?.SetValueWithoutNotify(blockElement.Data.IsEnabled);
                _actionStartFrameField.SetValueWithoutNotify(blockElement.Data.StartFrame);
                _actionEndFrameField.SetValueWithoutNotify(blockElement.Data.EndFrame);
                if (_actionClipStartField != null && _actionClipEndField != null && blockElement.Data.Action != null)
                {
                    _actionClipStartField.SetValueWithoutNotify(blockElement.Data.Action.ClipStartTime);
                    _actionClipEndField.SetValueWithoutNotify(blockElement.Data.Action.ClipEndTime);
                }
                float fps = _targetAsset != null ? Mathf.Max(1f, _targetAsset.FrameRate) : 30f;
                float currentDur = (blockElement.Data.EndFrame - blockElement.Data.StartFrame) / fps;
                if (blockElement.Data.Action != null)
                {
                    blockElement.Data.Action.BlockDuration = currentDur;
                }
                string baseInfo = $"Time: {blockElement.Data.StartFrame / fps:F2}s - {blockElement.Data.EndFrame / fps:F2}s (Duration: {currentDur:F2}s / {blockElement.Data.EndFrame - blockElement.Data.StartFrame} frames)";
                string custom = blockElement.Data.Action?.GetTimingCustomHint();
                if (_actionTimingHint != null)
                {
                    _actionTimingHint.text = !string.IsNullOrEmpty(custom) ? $"{baseInfo}\n{custom}" : baseInfo;
                }
                if (_clipSpeedInfoLabel != null && blockElement.Data.Action != null && blockElement.Data.Action.IsTrimmableClip)
                {
                    var act = blockElement.Data.Action;
                    _clipSpeedInfoLabel.text = $"Effective Clip: {act.EffectiveClipDuration:F2}s | Block Duration: {act.BlockDuration:F2}s | Speed: {act.SpeedMultiplier:F2}x";
                }
                if (_actionTargetBadge != null)
                {
                    int trackIdx = blockElement.TrackIndex;
                    string trackStr = _targetAsset != null && trackIdx >= 0 && trackIdx < _targetAsset.Tracks.Count
                        ? _targetAsset.Tracks[trackIdx].TrackName
                        : $"Track {trackIdx + 1}";
                    _actionTargetBadge.text = $"{trackStr} | F{blockElement.Data.StartFrame}-{blockElement.Data.EndFrame}";
                }
                return;
            }

            _currentSelectedBlock = blockElement;
            _actionContainer.Clear();

            _serializedAsset.Update();

            var data = blockElement.Data;
            var action = data.Action;
            string actionType = action.GetType().Name;

            // 1. 顶部操作 Header Banner（包含启用/禁用勾选框，直观置顶于类型名称前）
            var banner = new VisualElement();
            banner.AddToClassList("montage-inspector-banner");

            var titleGroup = new VisualElement();
            titleGroup.style.flexDirection = FlexDirection.Row;
            titleGroup.style.alignItems = Align.Center;

            var enabledToggle = new Toggle();
            enabledToggle.value = data.IsEnabled;
            enabledToggle.tooltip = "Enable / Disable Action Block";
            enabledToggle.style.marginRight = 6;
            enabledToggle.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(_targetAsset, evt.newValue ? "Enable Action Block" : "Disable Action Block");
                if (action != null)
                {
                    var field = typeof(MontageActionBlockBase).GetField("_isEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (field != null)
                    {
                        field.SetValue(action, evt.newValue);
                    }
                }
                blockElement.UpdateVisual();
                EditorUtility.SetDirty(_targetAsset);
                OnDataModified?.Invoke();
            });
            _actionEnabledToggle = enabledToggle;
            titleGroup.Add(enabledToggle);

            // 友好名称（优先 CustomName，其次 MontageDisplayName 特性）
            var dispAttr = action?.GetType().GetCustomAttribute<MontageDisplayNameAttribute>();
            string friendlyName = !string.IsNullOrEmpty(action?.CustomName)
                ? action.CustomName
                : (!string.IsNullOrEmpty(dispAttr?.DisplayName) ? dispAttr.DisplayName : actionType);

            var typeLabel = new Label(friendlyName);
            typeLabel.AddToClassList("montage-inspector-banner-title");
            titleGroup.Add(typeLabel);

            // 所属轨道与帧范围定位面包屑徽标
            int tIdx = blockElement.TrackIndex;
            string trackName = _targetAsset != null && tIdx >= 0 && tIdx < _targetAsset.Tracks.Count
                ? _targetAsset.Tracks[tIdx].TrackName
                : $"Track {tIdx + 1}";
            var targetBadge = new Label($"{trackName} | F{data.StartFrame}-{data.EndFrame}");
            targetBadge.AddToClassList("montage-inspector-target-badge");
            targetBadge.tooltip = $"Track: {trackName}\nStart Frame: {data.StartFrame}\nEnd Frame: {data.EndFrame}";
            _actionTargetBadge = targetBadge;
            titleGroup.Add(targetBadge);

            banner.Add(titleGroup);

            var editScriptBtn = new Button(blockElement.OpenScript) { text = "Edit Script" };
            editScriptBtn.AddToClassList("montage-toolbar-btn");
            editScriptBtn.tooltip = "Open action block source code in IDE (Z)";
            banner.Add(editScriptBtn);

            _actionContainer.Add(banner);

            float frameRate = _targetAsset != null ? Mathf.Max(1f, _targetAsset.FrameRate) : 30f;
            var timeHint = new Label();
            timeHint.AddToClassList("montage-inspector-time-hint");

            void UpdateTimingHintText()
            {
                float currentDur = (data.EndFrame - data.StartFrame) / frameRate;
                if (action != null)
                {
                    action.BlockDuration = currentDur;
                }
                string baseInfo = $"Time: {data.StartFrame / frameRate:F2}s - {data.EndFrame / frameRate:F2}s (Duration: {currentDur:F2}s / {data.EndFrame - data.StartFrame} frames)";
                string custom = action?.GetTimingCustomHint();
                timeHint.text = !string.IsNullOrEmpty(custom) ? $"{baseInfo}\n{custom}" : baseInfo;
            }

            UpdateTimingHintText();

            // 2. Media Clip Trimming & Speed（高优先级：对于有缩放截取的 block 优先展示截取参数）
            if (action != null && action.IsTrimmableClip)
            {
                var clipCard = new Foldout { text = "Media Clip Trimming & Speed", value = true };
                clipCard.AddToClassList("montage-inspector-foldout");

                // 垂直排列，避免在较窄窗口下输入框被挤压
                var startField = new FloatField("Clip Start (s)") { value = action.ClipStartTime };
                startField.style.marginBottom = 4;
                clipCard.Add(startField);

                var endField = new FloatField("Clip End (s)") { value = action.ClipEndTime };
                endField.style.marginBottom = 4;
                clipCard.Add(endField);

                _actionClipStartField = startField;
                _actionClipEndField = endField;

                var speedInfoLabel = new Label();
                speedInfoLabel.AddToClassList("montage-inspector-time-hint");

                void RefreshSpeedInfo()
                {
                    float effDur = action.EffectiveClipDuration;
                    float blkDur = action.BlockDuration;
                    float speed = action.SpeedMultiplier;
                    speedInfoLabel.text = $"Effective Clip: {effDur:F2}s | Block Duration: {blkDur:F2}s | Speed: {speed:F2}x";
                }
                RefreshSpeedInfo();
                clipCard.Add(speedInfoLabel);
                _clipSpeedInfoLabel = speedInfoLabel;

                // 面板不再放置多余按钮（已移至时间轴动作块右键菜单），界面更清爽

                startField.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(_targetAsset, "Change Clip Start Time");
                    action.ClipStartTime = Mathf.Max(0f, evt.newValue);
                    RefreshSpeedInfo();
                    UpdateTimingHintText();
                    blockElement.UpdateVisual();
                    EditorUtility.SetDirty(_targetAsset);
                    OnDataModified?.Invoke();
                });

                endField.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(_targetAsset, "Change Clip End Time");
                    action.ClipEndTime = evt.newValue;
                    RefreshSpeedInfo();
                    UpdateTimingHintText();
                    blockElement.UpdateVisual();
                    EditorUtility.SetDirty(_targetAsset);
                    OnDataModified?.Invoke();
                });

                _actionContainer.Add(clipCard);
            }
            else
            {
                _actionClipStartField = null;
                _actionClipEndField = null;
                _clipSpeedInfoLabel = null;
            }

            // 3. Action Parameters 可折叠卡片（核心参数与资产引用置顶，挂载与吸附放下方）
            var actionFoldout = new Foldout { text = "Action Parameters", value = true };
            actionFoldout.AddToClassList("montage-inspector-foldout");

            string propPath = $"_tracks.Array.data[{blockElement.TrackIndex}]._actionBlocks.Array.data[{blockElement.BlockIndex}]._action";
            var actionProp = _serializedAsset.FindProperty(propPath);

            if (actionProp != null)
            {
                var endProp = actionProp.GetEndProperty();
                var childProp = actionProp.Copy();
                bool enterChildren = true;

                var primaryProps = new List<SerializedProperty>();
                var spatialProps = new List<SerializedProperty>();

                while (childProp.NextVisible(enterChildren) && !SerializedProperty.EqualContents(childProp, endProp))
                {
                    enterChildren = false;
                    string propName = childProp.name;

                    // 跳过已在顶部 Header 呈现的启用开关和不必要的自定义名称
                    if (propName == "_isEnabled" || propName == "_customName")
                    {
                        continue;
                    }

                    // 跳过已在媒体截取卡片中专门呈现的起止时间
                    if (action != null && action.IsTrimmableClip && (propName == "_clipStartTime" || propName == "_clipEndTime"))
                    {
                        continue;
                    }

                    if (s_spatialPropertyNames.Contains(propName))
                    {
                        spatialProps.Add(childProp.Copy());
                    }
                    else
                    {
                        primaryProps.Add(childProp.Copy());
                    }
                }

                bool isBinding = true;

                // 优先渲染核心参数（包含资产引用如 _vfxPrefab, _audioClip, _prefab 及播放控制参数等）
                for (int i = 0; i < primaryProps.Count; i++)
                {
                    var field = new PropertyField(primaryProps[i]);
                    actionFoldout.Add(field);
                    field.RegisterValueChangeCallback(evt =>
                    {
                        if (isBinding) return;
                        _serializedAsset.ApplyModifiedProperties();
                        EditorUtility.SetDirty(_targetAsset);
                        blockElement.UpdateVisual();
                        OnDataModified?.Invoke();
                    });
                }

                // 挂载吸附与变换参数（优先级较低，排在下方）
                for (int i = 0; i < spatialProps.Count; i++)
                {
                    var field = new PropertyField(spatialProps[i]);
                    actionFoldout.Add(field);
                    field.RegisterValueChangeCallback(evt =>
                    {
                        if (isBinding) return;
                        _serializedAsset.ApplyModifiedProperties();
                        EditorUtility.SetDirty(_targetAsset);
                        blockElement.UpdateVisual();
                        OnDataModified?.Invoke();
                    });
                }

                actionFoldout.Bind(_serializedAsset);
                isBinding = false;
            }
            else
            {
                var errorLabel = new Label("Unable to bind action serialized property.");
                errorLabel.style.color = Color.red;
                actionFoldout.Add(errorLabel);
            }

            _actionContainer.Add(actionFoldout);

            // 4. Timing 可折叠卡片（优先级较低，放置在下方，垂直排列输入字段）
            var timingFoldout = new Foldout { text = "Timing", value = false };
            timingFoldout.AddToClassList("montage-inspector-foldout");

            // 垂直排列，避免并排被挤压
            var startFrameField = new IntegerField("Start Frame") { value = data.StartFrame };
            startFrameField.style.marginBottom = 4;
            timingFoldout.Add(startFrameField);

            var endFrameField = new IntegerField("End Frame") { value = data.EndFrame };
            endFrameField.style.marginBottom = 4;
            timingFoldout.Add(endFrameField);

            timingFoldout.Add(timeHint);

            _actionStartFrameField = startFrameField;
            _actionEndFrameField = endFrameField;
            _actionTimingHint = timeHint;

            startFrameField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(_targetAsset, "Change Action Block Start Frame");
                data.StartFrame = Mathf.Max(0, evt.newValue);
                data.StartTime = data.StartFrame / frameRate;
                UpdateTimingHintText();
                blockElement.UpdateVisual();
                EditorUtility.SetDirty(_targetAsset);
                OnDataModified?.Invoke();
            });

            endFrameField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(_targetAsset, "Change Action Block End Frame");
                data.EndFrame = Mathf.Max(data.StartFrame + 1, evt.newValue);
                data.EndTime = data.EndFrame / frameRate;
                UpdateTimingHintText();
                blockElement.UpdateVisual();
                EditorUtility.SetDirty(_targetAsset);
                OnDataModified?.Invoke();
            });

            _actionContainer.Add(timingFoldout);

            SelectTab(0); // 自动切换到 Action Tab
        }

        /// <summary>
        /// 仅在动作块拖拽过程中安全轻量更新帧数字与提示标签，绝对不触发 Clear()、Bind() 或切 Tab，杜绝打断鼠标捕获。
        /// </summary>
        public void UpdateTimingDisplayDuringDrag(MontageActionBlockElement blockElement)
        {
            if (blockElement?.Data == null) return;
            if (_currentSelectedBlock != blockElement || _actionStartFrameField == null || _actionEndFrameField == null)
            {
                return;
            }

            _actionStartFrameField.SetValueWithoutNotify(blockElement.Data.StartFrame);
            _actionEndFrameField.SetValueWithoutNotify(blockElement.Data.EndFrame);

            float fps = _targetAsset != null ? Mathf.Max(1f, _targetAsset.FrameRate) : 30f;
            float currentDur = (blockElement.Data.EndFrame - blockElement.Data.StartFrame) / fps;
            if (blockElement.Data.Action != null)
            {
                blockElement.Data.Action.BlockDuration = currentDur;
            }

            string baseInfo = $"Time: {blockElement.Data.StartFrame / fps:F2}s - {blockElement.Data.EndFrame / fps:F2}s (Duration: {currentDur:F2}s / {blockElement.Data.EndFrame - blockElement.Data.StartFrame} frames)";
            string custom = blockElement.Data.Action?.GetTimingCustomHint();
            if (_actionTimingHint != null)
            {
                _actionTimingHint.text = !string.IsNullOrEmpty(custom) ? $"{baseInfo}\n{custom}" : baseInfo;
            }

            if (_clipSpeedInfoLabel != null && blockElement.Data.Action != null && blockElement.Data.Action.IsTrimmableClip)
            {
                var act = blockElement.Data.Action;
                _clipSpeedInfoLabel.text = $"Effective Clip: {act.EffectiveClipDuration:F2}s | Block Duration: {act.BlockDuration:F2}s | Speed: {act.SpeedMultiplier:F2}x";
            }

            if (_actionTargetBadge != null)
            {
                int trackIdx = blockElement.TrackIndex;
                string trackStr = _targetAsset != null && trackIdx >= 0 && trackIdx < _targetAsset.Tracks.Count
                    ? _targetAsset.Tracks[trackIdx].TrackName
                    : $"Track {trackIdx + 1}";
                _actionTargetBadge.text = $"{trackStr} | F{blockElement.Data.StartFrame}-{blockElement.Data.EndFrame}";
            }
        }

        /// <summary>
        /// 选中并展示指定动画片段（Animation Segment）的详细参数（Clip引用、调速倍率、起止时间与交叉过渡曲线）。
        /// </summary>
        public void InspectAnimationSegment(MontageAnimationSegment segment, int segmentIndex, MontageSequenceSO asset)
        {
            _currentSelectedBlock = null;
            _actionContainer.Clear();

            if (segment == null || asset == null)
            {
                ShowEmptyActionHint();
                return;
            }

            // 1. 顶部 Header Banner
            var banner = new VisualElement();
            banner.AddToClassList("montage-inspector-banner");

            var titleGroup = new VisualElement();
            titleGroup.style.flexDirection = FlexDirection.Row;
            titleGroup.style.alignItems = Align.Center;

            string clipTitle = segment.Clip != null ? segment.Clip.name : "No Clip";
            var typeLabel = new Label($"Segment #{segmentIndex + 1}: {clipTitle}");
            typeLabel.AddToClassList("montage-inspector-banner-title");
            titleGroup.Add(typeLabel);

            var segmentBadge = new Label($"Animation Track | {segment.StartTime:F2}s - {segment.EndTime:F2}s");
            segmentBadge.AddToClassList("montage-inspector-target-badge");
            titleGroup.Add(segmentBadge);

            banner.Add(titleGroup);

            if (segment.Clip != null)
            {
                var pingBtn = new Button(() => EditorGUIUtility.PingObject(segment.Clip)) { text = "Ping Clip" };
                pingBtn.AddToClassList("montage-toolbar-btn");
                pingBtn.tooltip = "Ping animation clip in Project view";
                banner.Add(pingBtn);
            }
            _actionContainer.Add(banner);

            // 2. 动画片段资源选择
            var clipFoldout = new Foldout { text = "Clip Reference", value = true };
            clipFoldout.AddToClassList("montage-inspector-foldout");

            var clipField = new ObjectField("Animation Clip")
            {
                objectType = typeof(AnimationClip),
                value = segment.Clip
            };
            clipField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(asset, "Change Segment Clip");
                segment.Clip = evt.newValue as AnimationClip;
                segment.Duration = segment.CalculateNaturalDuration();
                EditorUtility.SetDirty(asset);
                OnAnimationSegmentClipChanged?.Invoke();
                OnDataModified?.Invoke();
            });
            clipFoldout.Add(clipField);
            _actionContainer.Add(clipFoldout);

            // 3. 时间轴与调速 (Timeline Timing & Speed)
            var timingFoldout = new Foldout { text = "Timeline Timing & Speed", value = true };
            timingFoldout.AddToClassList("montage-inspector-foldout");

            float inspectorFps = asset != null && asset.FrameRate > 0.01f ? asset.FrameRate : 30f;
            float inspectorFrameInterval = 1f / inspectorFps;

            var startTimeField = new FloatField("Start Time (s)") { value = segment.StartTime };
            timingFoldout.Add(startTimeField);

            var durField = new FloatField("Duration (s)") { value = segment.Duration };
            durField.tooltip = "Authoritative duration on timeline. Modifying this directly updates playback speed.";
            timingFoldout.Add(durField);

            var rateField = new FloatField("Play Rate (Derived)") { value = segment.PlayRate };
            rateField.SetEnabled(false);
            rateField.tooltip = "Derived playback speed multiplier: EffectiveClipLength / Duration (Read Only). To change speed, adjust Duration or drag segment handles.";
            timingFoldout.Add(rateField);

            var frameHint = new Label($"Frames: {Mathf.RoundToInt(segment.StartTime * inspectorFps)} - {Mathf.RoundToInt(segment.EndTime * inspectorFps)} ({Mathf.RoundToInt(segment.Duration * inspectorFps)} frames)");
            frameHint.AddToClassList("montage-inspector-time-hint");
            timingFoldout.Add(frameHint);

            startTimeField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(asset, "Change Segment Start Time");
                float snappedStart = Mathf.Round(Mathf.Max(0f, evt.newValue) / inspectorFrameInterval) * inspectorFrameInterval;
                segment.StartTime = snappedStart;
                startTimeField.SetValueWithoutNotify(snappedStart);
                frameHint.text = $"Frames: {Mathf.RoundToInt(segment.StartTime * inspectorFps)} - {Mathf.RoundToInt(segment.EndTime * inspectorFps)} ({Mathf.RoundToInt(segment.Duration * inspectorFps)} frames)";
                EditorUtility.SetDirty(asset);
                OnDataModified?.Invoke();
            });

            durField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(asset, "Change Segment Duration");
                float snappedDur = Mathf.Max(inspectorFrameInterval, Mathf.Round(Mathf.Max(inspectorFrameInterval, evt.newValue) / inspectorFrameInterval) * inspectorFrameInterval);
                segment.Duration = snappedDur;
                durField.SetValueWithoutNotify(snappedDur);
                rateField.SetValueWithoutNotify(segment.PlayRate);
                frameHint.text = $"Frames: {Mathf.RoundToInt(segment.StartTime * inspectorFps)} - {Mathf.RoundToInt(segment.EndTime * inspectorFps)} ({Mathf.RoundToInt(segment.Duration * inspectorFps)} frames)";
                EditorUtility.SetDirty(asset);
                OnDataModified?.Invoke();
            });

            _actionContainer.Add(timingFoldout);

            // 4. 动画裁剪 / 截取区间 (Clip Trimming)
            var trimFoldout = new Foldout { text = "Clip Trimming (Sample Range)", value = true };
            trimFoldout.AddToClassList("montage-inspector-foldout");

            var startOffsetField = new FloatField("Start Offset (Trim In, s)") { value = segment.StartOffset };
            startOffsetField.tooltip = "Trim In time (seconds) inside source clip.";
            trimFoldout.Add(startOffsetField);

            var endOffsetField = new FloatField("End Offset (Trim Out, s)") { value = segment.RawEndOffset };
            endOffsetField.tooltip = "Trim Out time (seconds) inside source clip. Set 0 to play to the natural end.";
            trimFoldout.Add(endOffsetField);

            float clipFps = segment.Clip != null ? Mathf.Max(1f, segment.Clip.frameRate) : 30f;
            var trimInfoLabel = new Label($"Effective Length: {segment.EffectiveClipLength:F2}s / {Mathf.RoundToInt(segment.EffectiveClipLength * clipFps)} frames");
            trimInfoLabel.AddToClassList("montage-inspector-time-hint");
            trimFoldout.Add(trimInfoLabel);

            void RefreshTrimVisual()
            {
                durField.SetValueWithoutNotify(segment.Duration);
                rateField.SetValueWithoutNotify(segment.PlayRate);
                startOffsetField.SetValueWithoutNotify(segment.StartOffset);
                endOffsetField.SetValueWithoutNotify(segment.RawEndOffset);
                trimInfoLabel.text = $"Effective Length: {segment.EffectiveClipLength:F2}s / {Mathf.RoundToInt(segment.EffectiveClipLength * clipFps)} frames";
            }

            startOffsetField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(asset, "Change Segment Start Offset");
                segment.StartOffset = Mathf.Max(0f, evt.newValue);
                RefreshTrimVisual();
                EditorUtility.SetDirty(asset);
                OnDataModified?.Invoke();
            });

            endOffsetField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(asset, "Change Segment End Offset");
                segment.EndOffset = Mathf.Max(0f, evt.newValue);
                RefreshTrimVisual();
                EditorUtility.SetDirty(asset);
                OnDataModified?.Invoke();
            });

            _actionContainer.Add(trimFoldout);

            // 5. 交叉混合设置 (Crossfade Blending)
            var blendFoldout = new Foldout { text = "Crossfade Blending", value = true };
            blendFoldout.AddToClassList("montage-inspector-foldout");

            // 计算该片段与前一片段在时间轴上的实际物理重叠时长
            float prevOverlap = 0f;
            if (segmentIndex > 0 && asset.AnimationSegments != null && segmentIndex < asset.AnimationSegments.Count)
            {
                var prevSeg = asset.AnimationSegments[segmentIndex - 1];
                if (prevSeg != null && prevSeg.EndTime > segment.StartTime)
                {
                    prevOverlap = Mathf.Max(0f, prevSeg.EndTime - segment.StartTime);
                }
            }

            var overlapHint = prevOverlap > 0.0001f
                ? new Label($"Overlap Duration: {prevOverlap:F2}s (Crossfade active)")
                : new Label("No overlap with previous segment. Drag segments to overlap for crossfade.");
            overlapHint.AddToClassList("montage-inspector-time-hint");
            blendFoldout.Add(overlapHint);

            var curveField = new CurveField("Blend Curve") { value = segment.BlendCurve };
            curveField.tooltip = "Interpolation curve during timeline crossfade overlap.";
            curveField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(asset, "Change Segment Blend Curve");
                segment.BlendCurve = evt.newValue;
                EditorUtility.SetDirty(asset);
                OnDataModified?.Invoke();
            });
            blendFoldout.Add(curveField);

            _actionContainer.Add(blendFoldout);

            SelectTab(0);
        }

        /// <summary>
        /// 清空选中的动作块检查器。
        /// </summary>
        public void ClearActionInspect()
        {
            _currentSelectedBlock = null;
            ShowEmptyActionHint();
        }

        #endregion

        #region 私有方法

        private void ShowEmptyActionHint()
        {
            _currentSelectedBlock = null;
            _actionStartFrameField = null;
            _actionEndFrameField = null;
            _actionTimingHint = null;
            _actionTargetBadge = null;
            _actionContainer.Clear();
            var hint = new Label("Select an Action Block or Animation Segment on the timeline to inspect its parameters.");
            hint.AddToClassList("montage-empty-hint");
            _actionContainer.Add(hint);
        }

        private void RebuildAssetSettingsTab()
        {
            _assetSettingsContainer.Clear();
            if (_serializedAsset == null)
            {
                return;
            }

            _serializedAsset.Update();

            bool isBinding = true;

            void BindSettingField(PropertyField pf)
            {
                pf.RegisterValueChangeCallback(evt =>
                {
                    if (isBinding) return;
                    _serializedAsset.ApplyModifiedProperties();
                    EditorUtility.SetDirty(_targetAsset);
                    OnAssetSettingsModified?.Invoke();
                });
            }

            // 1. Animation Base Foldout
            var foldoutGeneral = new Foldout { text = "Animation Base", value = true };
            foldoutGeneral.AddToClassList("montage-inspector-foldout");

            var layerProp = _serializedAsset.FindProperty("_animationLayer");
            var rateProp = _serializedAsset.FindProperty("_basePlayRate");
            var loopProp = _serializedAsset.FindProperty("_isLooping");
            var ikProp = _serializedAsset.FindProperty("_isFootIK");

            if (layerProp != null) { var f = new PropertyField(layerProp); BindSettingField(f); foldoutGeneral.Add(f); }
            if (rateProp != null) { var f = new PropertyField(rateProp); BindSettingField(f); foldoutGeneral.Add(f); }
            if (loopProp != null) { var f = new PropertyField(loopProp); BindSettingField(f); foldoutGeneral.Add(f); }
            if (ikProp != null) { var f = new PropertyField(ikProp); BindSettingField(f); foldoutGeneral.Add(f); }
            _assetSettingsContainer.Add(foldoutGeneral);

            // 2. Blending Settings Foldout
            var foldoutBlending = new Foldout { text = "Blending Settings", value = true };
            foldoutBlending.AddToClassList("montage-inspector-foldout");

            var blendInTime = _serializedAsset.FindProperty("_defaultBlendInTime");
            var blendInCurve = _serializedAsset.FindProperty("_blendInCurve");
            var blendOutTime = _serializedAsset.FindProperty("_defaultBlendOutTime");
            var blendOutCurve = _serializedAsset.FindProperty("_blendOutCurve");
            var blendOutOffset = _serializedAsset.FindProperty("_blendOutOffset");

            if (blendInTime != null) { var f = new PropertyField(blendInTime); BindSettingField(f); foldoutBlending.Add(f); }
            if (blendInCurve != null) { var f = new PropertyField(blendInCurve); BindSettingField(f); foldoutBlending.Add(f); }
            if (blendOutTime != null) { var f = new PropertyField(blendOutTime); BindSettingField(f); foldoutBlending.Add(f); }
            if (blendOutCurve != null) { var f = new PropertyField(blendOutCurve); BindSettingField(f); foldoutBlending.Add(f); }
            if (blendOutOffset != null) { var f = new PropertyField(blendOutOffset); BindSettingField(f); foldoutBlending.Add(f); }
            _assetSettingsContainer.Add(foldoutBlending);

            // 3. Root Motion Settings Foldout
            var foldoutRootMotion = new Foldout { text = "Root Motion Settings", value = true };
            foldoutRootMotion.AddToClassList("montage-inspector-foldout");

            var applyH = _serializedAsset.FindProperty("_applyHorizontalRootMotion");
            var applyV = _serializedAsset.FindProperty("_applyVerticalRootMotion");
            var applyR = _serializedAsset.FindProperty("_applyRotationRootMotion");

            if (applyH != null) { var f = new PropertyField(applyH); BindSettingField(f); foldoutRootMotion.Add(f); }
            if (applyV != null) { var f = new PropertyField(applyV); BindSettingField(f); foldoutRootMotion.Add(f); }
            if (applyR != null) { var f = new PropertyField(applyR); BindSettingField(f); foldoutRootMotion.Add(f); }
            _assetSettingsContainer.Add(foldoutRootMotion);

            // 4. Physical Sections Foldout
            var foldoutSections = new Foldout { text = "Physical Sections (去语义化物理分段)", value = true };
            foldoutSections.AddToClassList("montage-inspector-foldout");

            var splitsProp = _serializedAsset.FindProperty("_splitTimestamps");
            if (splitsProp != null)
            {
                var f = new PropertyField(splitsProp);
                BindSettingField(f);
                foldoutSections.Add(f);
            }

            // 详细分段与代码索引对齐清单
            if (_targetAsset != null)
            {
                int count = _targetAsset.SectionCount;
                var listContainer = new VisualElement();
                listContainer.style.marginTop = 6;
                listContainer.style.paddingTop = 6;
                listContainer.style.borderTopWidth = 1;
                listContainer.style.borderTopColor = new Color(0.25f, 0.25f, 0.25f, 0.6f);

                var listHeader = new Label($"Calculated Sections ({count} total):");
                listHeader.style.fontSize = 11;
                listHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                listHeader.style.color = new Color(0.7f, 0.85f, 1f);
                listHeader.style.marginBottom = 4;
                listContainer.Add(listHeader);

                for (int s = 0; s < count; s++)
                {
                    var (start, end) = _targetAsset.GetSectionRange(s);
                    float dur = Mathf.Max(0f, end - start);
                    var secRow = new Label($"  Section {s + 1} [Index: {s}] -> {start:F2}s ~ {end:F2}s ({dur:F2}s)");
                    secRow.style.fontSize = 10;
                    secRow.style.color = new Color(0.85f, 0.85f, 0.85f);
                    listContainer.Add(secRow);
                }

                foldoutSections.Add(listContainer);
            }

            _assetSettingsContainer.Add(foldoutSections);

            _assetSettingsContainer.Bind(_serializedAsset);
            isBinding = false;
        }

        #endregion
    }
}
