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
    /// 现代化动画轨道元素（MontageTrackElement）。
    /// 左侧包含符合 Timeline 标准的 Track Header（轨道重命名、Mute静音、Lock锁定、选项菜单）；
    /// 右侧为绝对像素对齐缩放体系的时间轴内容区（ActionBlocks 容器与分段贯穿线）。
    /// </summary>
    public class MontageTrackElement : VisualElement
    {
        #region 私有常量

        public const float TRACK_HEIGHT = 28f;
        private const float BASE_PIXELS_PER_SECOND = 200f;

        #endregion

        #region 私有字段

        private MontageTrackData _trackData;
        private MontageSequenceSO _targetAsset;
        private int _trackIndex;
        private float _clipLength = 1f;
        private float _frameRate = 30f;
        private float _zoomLevel = 1.0f;

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

        #region 公共事件

        public event Action<MontageTrackElement> OnTrackSelected;
        public event Action<MontageActionBlockElement> OnBlockSelected;
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

        #region 公共属性

        public MontageTrackData TrackData => _trackData;
        public int TrackIndex => _trackIndex;
        public VisualElement HeaderElement => _headerElement;
        public VisualElement ContentElement => _contentElement;
        public IReadOnlyList<MontageActionBlockElement> BlockElements => _blockElements;

        /// <summary>
        /// 当前缩放下的每秒实际像素宽度。
        /// </summary>
        public float PixelsPerSecond => BASE_PIXELS_PER_SECOND * _zoomLevel;

        /// <summary>
        /// 当前缩放下的内容区像素宽度。
        /// </summary>
        public float ContentPixelWidth => Mathf.Max(100f, _clipLength * PixelsPerSecond);

        #endregion

        #region 构造方法

        public MontageTrackElement(
            MontageTrackData trackData,
            MontageSequenceSO targetAsset,
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
            _zoomLevel = Mathf.Clamp(zoomLevel, 0.1f, 20f);
            _actionTypes = actionTypes ?? Array.Empty<Type>();
            _actionTypeNames = actionTypeNames ?? Array.Empty<string>();

            AddToClassList("montage-track-row");

            // 1. 左侧轨道头
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
                _trackData.TrackName = evt.newValue;
                OnDataModified?.Invoke();
            });
            titleBox.Add(_trackTitleField);
            _headerElement.Add(titleBox);

            var buttonsGroup = new VisualElement();
            buttonsGroup.style.flexDirection = FlexDirection.Row;
            buttonsGroup.style.alignItems = Align.Center;

            // Mute 按钮
            _muteButton = new Button(ToggleMute) { text = "M" };
            _muteButton.AddToClassList("montage-track-btn-toggle");
            _muteButton.tooltip = "Mute Track (Skip execution)";
            UpdateMuteVisual();
            buttonsGroup.Add(_muteButton);

            // Lock 按钮
            _lockButton = new Button(ToggleLock) { text = "L" };
            _lockButton.AddToClassList("montage-track-btn-toggle");
            _lockButton.tooltip = "Lock Track (Prevent editing)";
            UpdateLockVisual();
            buttonsGroup.Add(_lockButton);

            // 添加块按钮
            _addBlockButton = new Button(ShowAddBlockMenu) { text = "+" };
            _addBlockButton.AddToClassList("montage-toolbar-btn");
            _addBlockButton.tooltip = "Add Action Block to this track";
            buttonsGroup.Add(_addBlockButton);

            _headerElement.Add(buttonsGroup);
            _headerElement.RegisterCallback<MouseDownEvent>(OnHeaderMouseDown);
            _headerElement.RegisterCallback<MouseMoveEvent>(OnHeaderMouseMove);
            _headerElement.RegisterCallback<MouseUpEvent>(OnHeaderMouseUp);

            // 2. 右侧动作块内容容器
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

        #region 公共方法

        /// <summary>
        /// 重建当前轨道内的所有 ActionBlock 视图。
        /// </summary>
        public void RebuildBlocks()
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
                    _zoomLevel);

                blockElement.OnBlockSelected += block => OnBlockSelected?.Invoke(block);
                blockElement.OnBlockCopied += block =>
                {
                    MontageClipboard.CopyActionBlock(block.Data);
                };
                blockElement.OnBlockDeleted += block =>
                {
                    if (!_trackData.IsLocked)
                    {
                        _trackData.ActionBlocks.RemoveAt(block.BlockIndex);
                        RebuildBlocks();
                        OnDataModified?.Invoke();
                    }
                };
                blockElement.OnBlockModified += block =>
                {
                    OnDataModified?.Invoke();
                };

                _blockElements.Add(blockElement);
                _contentElement.Add(blockElement);
            }

            _contentElement.MarkDirtyRepaint();
        }

        /// <summary>
        /// 设置当前轨道缩放倍率。
        /// </summary>
        public void SetZoom(float zoomLevel)
        {
            _zoomLevel = Mathf.Clamp(zoomLevel, 0.1f, 20f);
            _contentElement.style.width = ContentPixelWidth;

            for (int i = 0; i < _blockElements.Count; i++)
            {
                _blockElements[i].SetZoom(_zoomLevel);
            }

            _contentElement.MarkDirtyRepaint();
        }

        /// <summary>
        /// 设置当前轨道高亮选中状态。
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
        /// 更新时间与帧率配置。
        /// </summary>
        public void UpdateTimelineConfig(float clipLength, float frameRate)
        {
            _clipLength = Mathf.Max(0.001f, clipLength);
            _frameRate = Mathf.Max(1f, frameRate);
            _contentElement.style.width = ContentPixelWidth;

            for (int i = 0; i < _blockElements.Count; i++)
            {
                _blockElements[i].UpdateVisual();
            }

            _contentElement.MarkDirtyRepaint();
        }

        #endregion

        #region 私有方法与菜单

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

            // 1. 绘制垂直贯穿时间轴刻度网格线 (Timeline Vertical Grid Lines)
            int totalFrames = Mathf.Max(1, Mathf.RoundToInt(_clipLength * _frameRate));
            float frameWidth = pps / _frameRate;
            var (majorInterval, mediumInterval) = MontageTimelineRuler.CalculateTickIntervals(frameWidth);

            Color majorGridColor = new Color(1f, 1f, 1f, 0.09f);
            Color minorGridColor = new Color(1f, 1f, 1f, 0.04f);

            for (int f = 0; f <= totalFrames; f++)
            {
                bool isMajor = (f % majorInterval == 0);
                bool isMedium = (!isMajor && f % mediumInterval == 0);

                if (!isMajor && !isMedium)
                {
                    if (frameWidth < 5f) continue; // 过密时略过微小副刻度线
                }

                float x = f * frameWidth;
                painter.strokeColor = isMajor ? majorGridColor : minorGridColor;
                painter.lineWidth = 1f;

                painter.BeginPath();
                painter.MoveTo(new Vector2(x, 0));
                painter.LineTo(new Vector2(x, height));
                painter.Stroke();
            }

            // 2. 绘制物理分段贯穿分割辅助线 (Physical Section Boundaries)
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

            // 3. 绘制底部分割线
            painter.strokeColor = new Color(0f, 0f, 0f, 0.35f);
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0, height));
            painter.LineTo(new Vector2(width, height));
            painter.Stroke();
        }

        private void ToggleMute()
        {
            _trackData.IsMuted = !_trackData.IsMuted;
            UpdateMuteVisual();
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
            _trackData.IsLocked = !_trackData.IsLocked;
            UpdateLockVisual();
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

            if (evt.button == 1) // 右键菜单
            {
                ShowTrackOptionsMenu();
                evt.StopPropagation();
            }
            else if (evt.button == 0) // 左键拖拽排序准备
            {
                _headerDragStartPos = evt.mousePosition;
                _isDraggingHeader = false;
            }
        }

        private void OnHeaderMouseMove(MouseMoveEvent evt)
        {
            if (evt.pressedButtons == 1) // 左键拖动
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

            if (evt.button == 1) // 右键点击空白处
            {
                ShowContentContextMenu(evt.mousePosition, clickTime);
                evt.StopPropagation();
            }
            else if (evt.button == 0 && evt.clickCount == 2 && !_trackData.IsLocked) // 双击空白处添加动作块
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

            // 1. 复制轨道
            menu.AddItem(new GUIContent("Copy Track (Ctrl+C)"), false, () =>
            {
                MontageClipboard.CopyTrack(_trackData);
                OnTrackCopied?.Invoke(this);
            });

            // 2. 粘贴覆盖
            if (MontageClipboard.HasCopiedTrack && !_trackData.IsLocked)
            {
                menu.AddItem(new GUIContent("Paste Track (Overwrite)"), false, () => OnTrackPasteOverrideRequested?.Invoke(this));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Paste Track (Overwrite)"));
            }

            // 3. 粘贴为新轨道
            if (MontageClipboard.HasCopiedTrack)
            {
                menu.AddItem(new GUIContent("Paste as New Track Below (Ctrl+V)"), false, () => OnPasteNewTrackBelowRequested?.Invoke(this));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Paste as New Track Below (Ctrl+V)"));
            }

            // 4. 快速克隆
            menu.AddItem(new GUIContent("Duplicate Track (Ctrl+D)"), false, () => OnDuplicateTrackRequested?.Invoke(this));

            menu.AddSeparator("");

            // 5. 删除轨道
            menu.AddItem(new GUIContent("Delete Track (Delete)"), false, () => OnTrackDeleteRequested?.Invoke(this));

            menu.ShowAsContext();
        }

        private void ShowContentContextMenu(Vector2 mousePos, float timeAtClick)
        {
            if (_trackData.IsLocked)
            {
                return;
            }

            var menu = new GenericMenu();

            // 1. 添加动作块 (子菜单)
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

            // 2. 粘贴动作块
            if (MontageClipboard.HasCopiedBlock)
            {
                menu.AddItem(new GUIContent($"Paste Action Block at {timeAtClick:F2}s (Ctrl+V)"), false, () =>
                {
                    OnPasteBlockAtTime?.Invoke(this, timeAtClick);
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Paste Action Block (Ctrl+V)"));
            }

            menu.ShowAsContext();
        }

        #endregion
    }
}
