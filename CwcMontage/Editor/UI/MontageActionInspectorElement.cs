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
    /// Standalone action block and asset property inspector panel (MontageActionInspectorElement).
    /// Keeps Action fields out of the 3D viewport and provides a modern Unity-style collapsible card inspector in the sidebar.
    /// </summary>
    public class MontageActionInspectorElement : VisualElement
    {
        #region Private Static Constants

        private static readonly HashSet<string> s_spatialPropertyNames = new()
        {
            "_targetBone",
            "_attachMode",
            "_positionOffset",
            "_rotationOffset",
            "_scale"
        };

        #endregion

        #region Private Fields

        private MontageSequenceAsset _targetAsset;
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

        #region Public Properties

        public int CurrentTabIndex => _currentTabIndex;

        #endregion

        #region Public Events

        public event Action OnDataModified;
        public event Action OnAssetSettingsModified;
        public event Action OnAnimationSegmentClipChanged;

        #endregion

        #region Constructor

        public MontageActionInspectorElement()
        {
            AddToClassList("montage-inspector-sidebar");

            var header = new VisualElement();
            header.AddToClassList("montage-inspector-header");

            var title = new Label("Inspector");
            title.AddToClassList("montage-inspector-title");
            header.Add(title);
            Add(header);

            // Tab switcher bar (works with every Unity version, pinned to the top)
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

        #region Public Methods

        /// <summary>
        /// Switches the sidebar tab (0: Action Block, 1: Asset Settings).
        /// Pure native UI Toolkit implementation, fully compatible with Unity 2021/2022/2023/Unity 6.
        /// </summary>
        /// <param name="index">Target tab index</param>
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
        /// Binds the montage asset currently being edited.
        /// </summary>
        public void BindAsset(MontageSequenceAsset asset, SerializedObject serializedAsset)
        {
            _targetAsset = asset;
            _serializedAsset = serializedAsset;
            _currentSelectedBlock = null;

            RebuildAssetSettingsTab();
            ShowEmptyActionHint();
        }

        /// <summary>
        /// Selects and displays the detailed fields of the specified ActionBlock.
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

            // Lightweight fast path: when the same action block is already inspected, silently update only the frames and hints; never call Clear()/Bind() or switch tabs again
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

            // 1. Top action header banner (includes the enable/disable toggle, placed before the type name)
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

            // Friendly name (CustomName first, then the MontageDisplayName attribute)
            var dispAttr = action?.GetType().GetCustomAttribute<MontageDisplayNameAttribute>();
            string friendlyName = !string.IsNullOrEmpty(action?.CustomName)
                ? action.CustomName
                : (!string.IsNullOrEmpty(dispAttr?.DisplayName) ? dispAttr.DisplayName : actionType);

            var typeLabel = new Label(friendlyName);
            typeLabel.AddToClassList("montage-inspector-banner-title");
            titleGroup.Add(typeLabel);

            // Breadcrumb badge locating the owning track and frame range
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

            // 2. Media Clip Trimming & Speed (high priority: show trim parameters first for blocks with a trimmable clip)
            if (action != null && action.IsTrimmableClip)
            {
                var clipCard = new Foldout { text = "Media Clip Trimming & Speed", value = true };
                clipCard.AddToClassList("montage-inspector-foldout");

                // Stack vertically so input fields are not squeezed in narrow windows
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

                // No extra buttons here (moved to the timeline action block context menu) to keep the UI clean

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

            // 3. Action Parameters foldout card (core parameters and asset references on top, attachment and snapping below)
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

                    // Skip the enable toggle already shown in the top header and the unnecessary custom name
                    if (propName == "_isEnabled" || propName == "_customName")
                    {
                        continue;
                    }

                    // Skip the start/end times already shown in the media trim card
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

                // Render core parameters first (asset references such as _vfxPrefab, _audioClip, _prefab, plus playback control parameters)
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

                // Attachment, snapping, and transform parameters (lower priority, placed below)
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

            // 4. Timing foldout card (lower priority, placed below, input fields stacked vertically)
            var timingFoldout = new Foldout { text = "Timing", value = false };
            timingFoldout.AddToClassList("montage-inspector-foldout");

            // Stack vertically to avoid being squeezed side by side
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

            SelectTab(0); // Automatically switch to the Action tab
        }

        /// <summary>
        /// Safely and lightly updates the frame numbers and hint label only while dragging an action block; never triggers Clear(), Bind(), or tab switching, so mouse capture is not interrupted.
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
        /// Selects and displays the detailed parameters of the specified animation segment (clip reference, playback rate multiplier, start/end times, and crossfade curve).
        /// </summary>
        public void InspectAnimationSegment(MontageAnimationSegment segment, int segmentIndex, MontageSequenceAsset asset)
        {
            _currentSelectedBlock = null;
            _actionContainer.Clear();

            if (segment == null || asset == null)
            {
                ShowEmptyActionHint();
                return;
            }

            // 1. Top header banner
            var banner = new VisualElement();
            banner.AddToClassList("montage-inspector-banner");

            var titleGroup = new VisualElement();
            titleGroup.style.flexDirection = FlexDirection.Row;
            titleGroup.style.alignItems = Align.Center;

            MontageLayerChannel channel = MontageLayerChannel.FullBody;
            List<MontageAnimationSegment> channelSegments = asset.FullBodySegments;
            if (asset.UpperBodySegments != null && asset.UpperBodySegments.Contains(segment))
            {
                channel = MontageLayerChannel.UpperBody;
                channelSegments = asset.UpperBodySegments;
            }
            else if (asset.AdditiveSegments != null && asset.AdditiveSegments.Contains(segment))
            {
                channel = MontageLayerChannel.Additive;
                channelSegments = asset.AdditiveSegments;
            }

            int actualIndex = channelSegments != null ? channelSegments.IndexOf(segment) : segmentIndex;
            if (actualIndex < 0) actualIndex = segmentIndex;

            string clipTitle = segment.Clip != null ? segment.Clip.name : "No Clip";
            var typeLabel = new Label($"Segment #{actualIndex + 1}: {clipTitle}");
            typeLabel.AddToClassList("montage-inspector-banner-title");
            titleGroup.Add(typeLabel);

            var segmentBadge = new Label($"{channel} Track | {segment.StartTime:F2}s - {segment.EndTime:F2}s");
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

            // 2. Animation clip asset selection
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

            // 3. Timeline timing and speed
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

            // 4. Clip trimming (sample range)
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

            // 5. Segment layer intensity envelope (fade in/out)
            var fadeFoldout = new Foldout { text = "Layer Intensity Envelope (Fade In / Out)", value = true };
            fadeFoldout.AddToClassList("montage-inspector-foldout");

            var envHint = new Label("Controls layer blending intensity against base locomotion layers (useful for Additive or standalone segments).");
            envHint.AddToClassList("montage-inspector-time-hint");
            fadeFoldout.Add(envHint);

            var blendInField = new FloatField("Fade In Time (s)") { value = segment.BlendInTime };
            blendInField.tooltip = "Smooth fade-in duration (seconds) when entering segment.";
            blendInField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(asset, "Change Segment Fade In Time");
                segment.BlendInTime = Mathf.Max(0f, evt.newValue);
                EditorUtility.SetDirty(asset);
                OnDataModified?.Invoke();
            });
            fadeFoldout.Add(blendInField);

            var blendOutField = new FloatField("Fade Out Time (s)") { value = segment.BlendOutTime };
            blendOutField.tooltip = "Smooth post-clip fade-out duration (seconds) after segment ends (freezes tail pose and fades to 0).";
            blendOutField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(asset, "Change Segment Fade Out Time");
                segment.BlendOutTime = Mathf.Max(0f, evt.newValue);
                EditorUtility.SetDirty(asset);
                OnDataModified?.Invoke();
            });
            fadeFoldout.Add(blendOutField);

            _actionContainer.Add(fadeFoldout);

            // 6. Incoming crossfade transition from the previous segment
            var blendFoldout = new Foldout { text = "Incoming Crossfade (Transition from Previous)", value = true };
            blendFoldout.AddToClassList("montage-inspector-foldout");

            // Compute the actual physical overlap with the previous segment in the same channel on the timeline
            float prevOverlap = 0f;
            MontageAnimationSegment prevSeg = null;
            if (channelSegments != null && actualIndex > 0 && actualIndex < channelSegments.Count)
            {
                prevSeg = channelSegments[actualIndex - 1];
                if (prevSeg != null && prevSeg.EndTime > segment.StartTime)
                {
                    prevOverlap = Mathf.Max(0f, prevSeg.EndTime - segment.StartTime);
                }
            }

            string overlapStatusText;
            if (actualIndex == 0)
            {
                overlapStatusText = "First segment in track. (Incoming crossfade only activates when overlapping a preceding segment).";
            }
            else if (prevOverlap > 0.0001f)
            {
                string prevClipName = prevSeg?.Clip != null ? prevSeg.Clip.name : $"Segment #{actualIndex}";
                overlapStatusText = $"Active with preceding #{actualIndex} '{prevClipName}': {prevOverlap:F2}s overlap.\nThis curve determines how THIS segment fades in while the preceding segment decays (1.0 - Curve).";
            }
            else
            {
                string prevClipName = prevSeg?.Clip != null ? prevSeg.Clip.name : $"Segment #{actualIndex}";
                overlapStatusText = $"No overlap with preceding #{actualIndex} '{prevClipName}'. Drag segments to overlap on timeline to enable crossfade.";
            }

            var overlapHint = new Label(overlapStatusText);
            overlapHint.AddToClassList("montage-inspector-time-hint");
            blendFoldout.Add(overlapHint);

            _actionContainer.Add(blendFoldout);

            SelectTab(0);
        }

        /// <summary>
        /// Clears the selected action block inspector.
        /// </summary>
        public void ClearActionInspect()
        {
            _currentSelectedBlock = null;
            ShowEmptyActionHint();
        }

        #endregion

        #region Private Methods

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

        private SerializedProperty FindAssetProperty(string propName)
        {
            if (_serializedAsset == null) return null;
            var prop = _serializedAsset.FindProperty($"_data.{propName}");
            if (prop != null) return prop;
            return _serializedAsset.FindProperty(propName);
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

            // 1. Animation Base Foldout (global animation settings first)
            var foldoutGeneral = new Foldout { text = "Animation Base", value = true };
            foldoutGeneral.AddToClassList("montage-inspector-foldout");

            var ikProp = FindAssetProperty("_isFootIK");
            if (ikProp != null) { var f = new PropertyField(ikProp); BindSettingField(f); foldoutGeneral.Add(f); }
            _assetSettingsContainer.Add(foldoutGeneral);

            // 2. Animation Layers Foldout (three animation layers and weights, clearly grouped by subheader)
            var foldoutLayers = new Foldout { text = "Animation Layers", value = true };
            foldoutLayers.AddToClassList("montage-inspector-foldout");

            var enableUpperProp = FindAssetProperty("_enableUpperBodyTrack");
            var upperWeightProp = FindAssetProperty("_upperBodyWeight");
            var decoupleProp = FindAssetProperty("_decoupleUpperBodyOrientation");
            var fullWeightProp = FindAssetProperty("_fullBodyWeight");
            var enableAddProp = FindAssetProperty("_enableAdditiveTrack");
            var addWeightProp = FindAssetProperty("_additiveWeight");

            // Layer 1: UpperBody section
            foldoutLayers.Add(CreateLayerSubHeader("Layer 1: UpperBody", isFirst: true));
            if (enableUpperProp != null) { var f = new PropertyField(enableUpperProp, "Enable UpperBody (Layer 1)"); BindSettingField(f); foldoutLayers.Add(f); }
            if (upperWeightProp != null) { var f = new PropertyField(upperWeightProp, "UpperBody Weight"); BindSettingField(f); foldoutLayers.Add(f); }
            if (decoupleProp != null) { var f = new PropertyField(decoupleProp, "Decouple UpperBody Spine"); BindSettingField(f); foldoutLayers.Add(f); }

            // Layer 2: FullBody (Master) section
            foldoutLayers.Add(CreateLayerSubHeader("Layer 2: FullBody (Master)", isFirst: false));
            if (fullWeightProp != null) { var f = new PropertyField(fullWeightProp, "FullBody Weight (Master)"); BindSettingField(f); foldoutLayers.Add(f); }

            // Layer 3: Additive section
            foldoutLayers.Add(CreateLayerSubHeader("Layer 3: Additive", isFirst: false));
            if (enableAddProp != null) { var f = new PropertyField(enableAddProp, "Enable Additive (Layer 3)"); BindSettingField(f); foldoutLayers.Add(f); }
            if (addWeightProp != null) { var f = new PropertyField(addWeightProp, "Additive Weight"); BindSettingField(f); foldoutLayers.Add(f); }

            _assetSettingsContainer.Add(foldoutLayers);

            // 3. Root Motion Settings Foldout
            var foldoutRootMotion = new Foldout { text = "Root Motion Settings", value = true };
            foldoutRootMotion.AddToClassList("montage-inspector-foldout");

            var applyH = FindAssetProperty("_applyHorizontalRootMotion");
            var applyV = FindAssetProperty("_applyVerticalRootMotion");
            var applyR = FindAssetProperty("_applyRotationRootMotion");

            if (applyH != null) { var f = new PropertyField(applyH); BindSettingField(f); foldoutRootMotion.Add(f); }
            if (applyV != null) { var f = new PropertyField(applyV); BindSettingField(f); foldoutRootMotion.Add(f); }
            if (applyR != null) { var f = new PropertyField(applyR); BindSettingField(f); foldoutRootMotion.Add(f); }
            _assetSettingsContainer.Add(foldoutRootMotion);

            // 4. Physical Sections Foldout
            var foldoutSections = new Foldout { text = "Physical Sections", value = true };
            foldoutSections.AddToClassList("montage-inspector-foldout");

            var splitsProp = FindAssetProperty("_splitTimestamps");
            if (splitsProp != null)
            {
                var f = new PropertyField(splitsProp);
                BindSettingField(f);
                foldoutSections.Add(f);
            }

            // Detailed section list aligned with code indices
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

        private static VisualElement CreateLayerSubHeader(string title, bool isFirst = false)
        {
            var box = new VisualElement();
            box.style.marginTop = isFirst ? 4 : 8;
            box.style.marginBottom = 4;

            if (!isFirst)
            {
                var line = new VisualElement();
                line.style.height = 1;
                line.style.backgroundColor = new Color(0.24f, 0.24f, 0.24f);
                line.style.marginBottom = 6;
                box.Add(line);
            }

            var label = new Label(title);
            label.style.fontSize = 11;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new Color(0.78f, 0.82f, 0.88f);
            label.style.paddingLeft = 2;
            box.Add(label);

            return box;
        }

        #endregion
    }
}
