using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Cwcbb.Tools.CwcMontage;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// 现代化动作块 UI 元素（MontageActionBlockElement）。
    /// 基于像素级绝对坐标对齐时间轴缩放体系，提供类似 Timeline Clip 的交互与视觉体验：
    /// 左右边缘手柄调节时长、整体平移、选中高亮、快捷键与右键菜单。
    /// </summary>
    public class MontageActionBlockElement : VisualElement
    {
        #region 私有常量

        private const float BASE_PIXELS_PER_SECOND = 200f;

        #endregion

        #region 私有字段

        private MontageActionBlockData _data;
        private MontageSequenceSO _targetAsset;
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

        #region 公共事件

        public event Action<MontageActionBlockElement> OnBlockSelected;
        public event Action<MontageActionBlockElement> OnBlockClicked;
        public event Action<MontageActionBlockElement> OnBlockCopied;
        public event Action<MontageActionBlockElement> OnBlockDuplicated;
        public event Action<MontageActionBlockElement> OnBlockDeleted;
        public event Action<MontageActionBlockElement> OnBlockModified;
        public event Action<MontageActionBlockElement> OnBlockMoving;
        public event Action<MontageActionBlockElement, float> OnBlockDraggingGlobalSync;

        #endregion

        #region 公共属性

        public MontageActionBlockData Data => _data;
        public int TrackIndex => _trackIndex;
        public int BlockIndex => _blockIndex;
        public bool IsSelected { get; private set; }

        #endregion

        #region 构造方法

        public MontageActionBlockElement(
            MontageActionBlockData data,
            int trackIndex,
            int blockIndex,
            float clipLength,
            float frameRate,
            float zoomLevel,
            MontageSequenceSO targetAsset = null)
        {
            _data = data;
            _targetAsset = targetAsset;
            _trackIndex = trackIndex;
            _blockIndex = blockIndex;
            _clipLength = Mathf.Max(0.001f, clipLength);
            _frameRate = Mathf.Max(1f, frameRate);
            _zoomLevel = Mathf.Clamp(zoomLevel, 0.005f, 20f);

            AddToClassList("montage-action-block");

            // 动作类型背景色适配
            if (data?.Action != null)
            {
                style.backgroundColor = GetActionTypeColor(data.Action.GetType());
            }

            // 自身内容容器：排布名称居左，角标居右，与动画片段完全对齐
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

            // 左右缩放手柄（独立绑定事件，阻断向父级冒泡误判）
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

            // 注册主体鼠标事件（平移与右键上下文菜单）
            RegisterCallback<MouseDownEvent>(OnMouseDown);
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 更新动作块在时间轴上的绝对像素布局与文本显示。
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

            // 特性显示名称
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

            // 调速角标（右对齐，格式 1.25x 与动画片段完全一致）
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

            // 悬停提示
            string speedInfo = _data.Action != null && _data.Action.IsTrimmableClip
                ? $"\nRate: {_data.Action.SpeedMultiplier:F2}x (Clip: {_data.Action.ClipStartTime:F2}s - {_data.Action.ClipEndTime:F2}s)"
                : "";
            _contentContainer.tooltip = $"{displayName}\nStart: {_data.StartFrame / _frameRate:F2}s (Frame {_data.StartFrame})\nDuration: {currentDuration:F2}s ({_data.EndFrame - _data.StartFrame} frames){speedInfo}";

            // 禁用状态置灰
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
        /// 更新时间轴缩放倍率。
        /// </summary>
        public void SetZoom(float zoomLevel)
        {
            _zoomLevel = Mathf.Clamp(zoomLevel, 0.005f, 20f);
            UpdateVisual();
        }

        /// <summary>
        /// 更新时间轴参考时长与基准帧率及播放头时间。
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
        /// 更新当前播放头时间（用于磁吸对齐）。
        /// </summary>
        public void SetPlayheadTime(float playheadTime)
        {
            _currentPlayheadTime = playheadTime;
        }

        /// <summary>
        /// 设置当前块的高亮选中状态。
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
        /// 打开对应 ActionBlock 的 C# 脚本源码。
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

        #region 私有方法与鼠标交互

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

            // 依据单轨互斥原则：计算同轨道左右相邻块的硬碰撞边界
            CalculateTrackBoundaryConstraints();

            // 使用稳定不动且范围广阔的轨道容器捕获鼠标与注册监听，杜绝自身位置变化引起事件跳变
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
            if (evt.button == 0) // 左键平移
            {
                StartDrag(evt, 0); // 0: Move
                evt.StopPropagation();
            }
            else if (evt.button == 1) // 右键上下文菜单
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

            if (_dragMode == 0) // Move 整体平移
            {
                int durationFrames = Mathf.Max(1, _initialEndFrame - _initialStartFrame);
                float initialStartTime = _initialStartFrame / _frameRate;
                float candidateStartTime = Mathf.Max(0f, initialStartTime + deltaTime);

                // 统一整数帧离散与多级磁吸（吸附 0点、分段点、播放头、动画片段与其他轨道动作块）
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

                // 单轨互斥硬边界 Clamping
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
            else if (_dragMode == 1) // StretchRight 右边缘拉伸调速
            {
                float initialEndTime = _initialEndFrame / _frameRate;
                float candidateEndTime = Mathf.Max((_initialStartFrame + 1) / _frameRate, initialEndTime + deltaTime);

                // 统一整数帧离散与多级磁吸
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

                // 单轨互斥硬边界 Clamping
                targetEndFrame = Mathf.Clamp(targetEndFrame, _initialStartFrame + 1, _maxAllowedEndFrame);

                _data.EndFrame = targetEndFrame;
                _data.EndTime = targetEndFrame / _frameRate;

                // 采样点定位在右边缘内侧半帧处，确保落在动作块的有效求值区间内
                activeEdgeTime = Mathf.Max(_data.StartTime, (_data.EndFrame - 0.5f) / _frameRate);
            }
            else if (_dragMode == 2) // StretchLeft 左边缘拉伸调速
            {
                float initialStartTime = _initialStartFrame / _frameRate;
                float candidateStartTime = Mathf.Max(0f, initialStartTime + deltaTime);

                // 统一整数帧离散与多级磁吸
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

                // 单轨互斥硬边界 Clamping
                targetStartFrame = Mathf.Clamp(targetStartFrame, _minAllowedStartFrame, _initialEndFrame - 1);

                _data.StartFrame = targetStartFrame;
                _data.StartTime = targetStartFrame / _frameRate;

                activeEdgeTime = _data.StartTime;
            }

            bool frameChanged = (_data.StartFrame != prevStartFrame || _data.EndFrame != prevEndFrame);

            // 轻量更新当前元素绝对定位与标签、角标
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
                    Debug.LogError($"[CwcMontage] 动作块拖动通知异常: {ex.Message}");
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

            // 1. 若用户只是普通点击（Click，未发生位移），触发 OnBlockClicked 进入检查器详情
            if (!_hasDragMoved)
            {
                OnBlockClicked?.Invoke(this);
            }
            // 2. 若产生了有效拖拽位移且帧范围真正发生改变，才持久化并广播修改
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

            // 1. 核心高频编辑操作
            menu.AddItem(new GUIContent("Copy (Ctrl+C)"), false, () => OnBlockCopied?.Invoke(this));
            menu.AddItem(new GUIContent("Duplicate (Ctrl+D)"), false, () => OnBlockDuplicated?.Invoke(this));
            menu.AddItem(new GUIContent("Delete (Delete)"), false, () => OnBlockDeleted?.Invoke(this));

            menu.AddSeparator("");

            // 2. 状态启停控制
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

            // 3. 媒体片段截取与时长匹配 (若支持截取调速)
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

            // 4. 开发者辅助
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
