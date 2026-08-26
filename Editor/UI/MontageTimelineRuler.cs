using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Cwcbb.Tools.CwcMontage;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// 纯粹的时间轴标尺控制器（MontageTimelineRuler）。
    /// 专注于时间刻度线、自适应帧数数字排布以及时间游标（Playhead）洗带拖拽，
    /// 分段切分已解耦至专属的系统分段轨道。
    /// </summary>
    public class MontageTimelineRuler : VisualElement
    {
        #region 私有常量

        public const float TOTAL_HEADER_HEIGHT = 24f;
        private const float BASE_PIXELS_PER_SECOND = 200f;

        #endregion

        #region 私有字段

        private MontageSequenceSO _targetAsset;
        private float _clipLength = 1f;
        private float _frameRate = 30f;
        private float _currentTime = 0f;
        private float _zoomLevel = 1.0f;

        private VisualElement _contentContainer;
        private VisualElement _tickLabelsContainer;
        private VisualElement _playheadVisual;
        private VisualElement _playheadCap;

        private readonly List<Label> _tickLabels = new();
        private bool _isDraggingPlayhead;

        #endregion

        #region 公共事件

        /// <summary>
        /// 当时间轴游标被拖拽或点击跳帧时触发（参数为当前时间秒数）。
        /// </summary>
        public event Action<float> OnTimeScrubbed;

        /// <summary>
        /// 当用户在标尺上使用鼠标中键平移时触发（参数为 deltaX 像素）。
        /// </summary>
        public event Action<float> OnPanDelta;

        /// <summary>
        /// 当用户滚动滚轮在标尺上缩放时触发（参数为 zoomDelta）。
        /// </summary>
        public event Action<float> OnZoomDelta;

        #endregion

        #region 公共属性

        /// <summary>
        /// 当前播放时间（秒）。
        /// </summary>
        public float CurrentTime => _currentTime;

        /// <summary>
        /// 动画总时长（秒）。
        /// </summary>
        public float ClipLength => _clipLength;

        /// <summary>
        /// 动画基准帧率。
        /// </summary>
        public float FrameRate => _frameRate;

        /// <summary>
        /// 当前时间轴缩放倍率。
        /// </summary>
        public float ZoomLevel => _zoomLevel;

        /// <summary>
        /// 当前缩放下的每秒实际像素宽度。
        /// </summary>
        public float PixelsPerSecond => BASE_PIXELS_PER_SECOND * _zoomLevel;

        /// <summary>
        /// 当前缩放下的时间轴总物理像素宽度。
        /// </summary>
        public float ContentPixelWidth => Mathf.Max(100f, _clipLength * PixelsPerSecond);

        #endregion

        #region 构造方法

        public MontageTimelineRuler()
        {
            AddToClassList("montage-ruler-area-container");
            style.height = TOTAL_HEADER_HEIGHT;

            // 内部可伸缩内容容器
            _contentContainer = new VisualElement();
            _contentContainer.AddToClassList("montage-ruler-content");
            _contentContainer.generateVisualContent += OnGenerateVisualContent;
            Add(_contentContainer);

            // 刻度数字标签容器
            _tickLabelsContainer = new VisualElement();
            _tickLabelsContainer.style.position = Position.Absolute;
            _tickLabelsContainer.style.top = 0;
            _tickLabelsContainer.style.left = 0;
            _tickLabelsContainer.style.right = 0;
            _tickLabelsContainer.style.bottom = 0;
            _tickLabelsContainer.pickingMode = PickingMode.Ignore;
            _contentContainer.Add(_tickLabelsContainer);

            // 游标指示器（科技蓝）
            _playheadVisual = new VisualElement();
            _playheadVisual.AddToClassList("montage-playhead-line");

            _playheadCap = new VisualElement();
            _playheadCap.AddToClassList("montage-playhead-cap");
            _playheadVisual.Add(_playheadCap);

            _contentContainer.Add(_playheadVisual);

            // 鼠标交互事件注册
            RegisterCallback<MouseDownEvent>(OnMouseDown);
            RegisterCallback<MouseMoveEvent>(OnMouseMove);
            RegisterCallback<MouseUpEvent>(OnMouseUp);
            RegisterCallback<MouseLeaveEvent>(OnMouseLeave);
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 绑定并设置时间轴数据源。
        /// </summary>
        public void SetTimelineData(MontageSequenceSO asset, float currentTime, float clipLength, float frameRate, float zoomLevel)
        {
            _targetAsset = asset;
            _currentTime = Mathf.Clamp(currentTime, 0f, Mathf.Max(0.001f, clipLength));
            _clipLength = Mathf.Max(0.001f, clipLength);
            _frameRate = Mathf.Max(1f, frameRate);
            _zoomLevel = Mathf.Clamp(zoomLevel, 0.1f, 20f);

            UpdateContentSize();
            UpdatePlayheadPosition();
            _contentContainer.MarkDirtyRepaint();
        }

        /// <summary>
        /// 仅更新当前时间并刷新游标位置。
        /// </summary>
        public void SetTime(float currentTime)
        {
            _currentTime = Mathf.Clamp(currentTime, 0f, _clipLength);
            UpdatePlayheadPosition();
        }

        /// <summary>
        /// 设置当前缩放倍率。
        /// </summary>
        public void SetZoom(float zoomLevel)
        {
            _zoomLevel = Mathf.Clamp(zoomLevel, 0.1f, 20f);
            UpdateContentSize();
            UpdatePlayheadPosition();
            _contentContainer.MarkDirtyRepaint();
        }

        /// <summary>
        /// 同步轨道区域的水平滚动位移。
        /// </summary>
        public void SetHorizontalScrollOffset(float offset)
        {
            _contentContainer.style.left = -offset;
        }

        #endregion

        #region 私有渲染逻辑 (Vector Graphics 零 GC 绘制)

        /// <summary>
        /// 根据当前帧的像素宽度智能计算主次刻度帧间隔步长。
        /// </summary>
        public static (int majorInterval, int mediumInterval) CalculateTickIntervals(float frameWidth)
        {
            if (frameWidth >= 40f) return (1, 1);
            if (frameWidth >= 20f) return (5, 1);
            if (frameWidth >= 8f) return (10, 5);
            if (frameWidth >= 3f) return (30, 5);
            if (frameWidth >= 1f) return (60, 30);
            return (150, 30);
        }

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            float totalWidth = ContentPixelWidth;
            float totalHeight = TOTAL_HEADER_HEIGHT;

            if (totalWidth <= 0 || totalHeight <= 0 || _clipLength <= 0)
            {
                return;
            }

            var painter = mgc.painter2D;
            float pps = PixelsPerSecond;

            // 绘制时间轴刻度线 (Ruler Ticks)
            int totalFrames = Mathf.Max(1, Mathf.RoundToInt(_clipLength * _frameRate));
            float frameWidth = pps / _frameRate;
            var (majorInterval, mediumInterval) = CalculateTickIntervals(frameWidth);

            Color majorTickColor = new Color(0.85f, 0.85f, 0.85f, 0.95f);
            Color mediumTickColor = new Color(0.55f, 0.55f, 0.55f, 0.75f);
            Color minorTickColor = new Color(0.35f, 0.35f, 0.35f, 0.45f);

            for (int f = 0; f <= totalFrames; f++)
            {
                float x = f * frameWidth;
                bool isMajor = (f % majorInterval == 0);
                bool isMedium = (!isMajor && f % mediumInterval == 0);

                painter.strokeColor = isMajor ? majorTickColor : (isMedium ? mediumTickColor : minorTickColor);
                painter.lineWidth = isMajor ? 1.2f : 1f;

                float tickTop;
                if (isMajor)
                {
                    tickTop = 2f;
                }
                else if (isMedium)
                {
                    tickTop = 10f;
                }
                else
                {
                    if (frameWidth < 4f) continue; // 过密时略过微小副刻度
                    tickTop = 16f;
                }

                painter.BeginPath();
                painter.MoveTo(new Vector2(x, totalHeight));
                painter.LineTo(new Vector2(x, tickTop));
                painter.Stroke();
            }

            // 底部横向分割线
            painter.strokeColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0, totalHeight));
            painter.LineTo(new Vector2(totalWidth, totalHeight));
            painter.Stroke();
        }

        private void UpdateContentSize()
        {
            float width = ContentPixelWidth;
            _contentContainer.style.width = width;

            // 重新排布刻度数字 Label
            for (int i = 0; i < _tickLabels.Count; i++)
            {
                _tickLabels[i].RemoveFromHierarchy();
            }
            _tickLabels.Clear();

            int totalFrames = Mathf.Max(1, Mathf.RoundToInt(_clipLength * _frameRate));
            float frameWidth = PixelsPerSecond / _frameRate;
            var (majorInterval, _) = CalculateTickIntervals(frameWidth);

            for (int f = 0; f <= totalFrames; f += majorInterval)
            {
                float x = f * frameWidth;
                var lbl = new Label(f.ToString());
                lbl.AddToClassList("montage-ruler-tick-label");
                lbl.style.left = x + 3f;
                lbl.style.top = 1f;
                lbl.pickingMode = PickingMode.Ignore;
                _tickLabelsContainer.Add(lbl);
                _tickLabels.Add(lbl);
            }
        }

        private void UpdatePlayheadPosition()
        {
            if (_playheadVisual == null) return;
            float xPos = _currentTime * PixelsPerSecond;
            _playheadVisual.style.left = xPos;
        }

        #endregion

        #region 鼠标交互与事件处理

        private void OnMouseDown(MouseDownEvent evt)
        {
            Vector2 localPos = _contentContainer.WorldToLocal(evt.mousePosition);
            float pps = PixelsPerSecond;
            float clickTime = Mathf.Clamp(localPos.x / pps, 0f, _clipLength);

            if (evt.button == 0) // 左键跳帧并开始拖拽游标
            {
                _isDraggingPlayhead = true;
                this.CaptureMouse();
                _currentTime = clickTime;
                UpdatePlayheadPosition();
                OnTimeScrubbed?.Invoke(_currentTime);
                evt.StopPropagation();
            }
            else if (evt.button == 2) // 中键平移
            {
                this.CaptureMouse();
                evt.StopPropagation();
            }
        }

        private void OnMouseMove(MouseMoveEvent evt)
        {
            if (_isDraggingPlayhead && evt.button == 0)
            {
                Vector2 localPos = _contentContainer.WorldToLocal(evt.mousePosition);
                float pps = PixelsPerSecond;
                float newTime = Mathf.Clamp(localPos.x / pps, 0f, _clipLength);

                _currentTime = newTime;
                UpdatePlayheadPosition();
                OnTimeScrubbed?.Invoke(_currentTime);
                evt.StopPropagation();
            }
            else if (evt.button == 2 && this.HasMouseCapture())
            {
                OnPanDelta?.Invoke(evt.mouseDelta.x);
                evt.StopPropagation();
            }
        }

        private void OnMouseUp(MouseUpEvent evt)
        {
            if (_isDraggingPlayhead && evt.button == 0)
            {
                _isDraggingPlayhead = false;
                this.ReleaseMouse();
                evt.StopPropagation();
            }
            else if (evt.button == 2 && this.HasMouseCapture())
            {
                this.ReleaseMouse();
                evt.StopPropagation();
            }
        }

        private void OnMouseLeave(MouseLeaveEvent evt)
        {
            if (_isDraggingPlayhead && !this.HasMouseCapture())
            {
                _isDraggingPlayhead = false;
            }
        }

        private void OnWheel(WheelEvent evt)
        {
            float delta = -evt.delta.y * 0.15f;
            OnZoomDelta?.Invoke(delta);
            evt.StopPropagation();
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            _contentContainer.style.width = ContentPixelWidth;
            _contentContainer.MarkDirtyRepaint();
        }

        #endregion
    }
}
