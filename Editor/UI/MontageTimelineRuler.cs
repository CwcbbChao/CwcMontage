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
        private static readonly float[] s_tickSecondCandidates = new float[]
        {
            0.5f, 1f, 2f, 5f, 10f, 20f, 30f, 60f, 120f, 300f, 600f, 1200f, 1800f, 3600f
        };

        #endregion

        #region 私有字段

        private MontageSequenceSO _targetAsset;
        private float _clipLength = 1f;
        private float _frameRate = 30f;
        private float _currentTime = 0f;
        private float _zoomLevel = 1.0f;
        private float _contentDuration = 0f;

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
            _zoomLevel = Mathf.Clamp(zoomLevel, 0.005f, 20f);

            UpdateContentSize();
            UpdatePlayheadPosition();
            _contentContainer.MarkDirtyRepaint();
        }

        /// <summary>
        /// 设置权威逻辑内容总时长（用于绘制结束标头与边界线）。
        /// </summary>
        public void SetContentDuration(float duration)
        {
            _contentDuration = Mathf.Max(0f, duration);
            _contentContainer?.MarkDirtyRepaint();
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
            _zoomLevel = Mathf.Clamp(zoomLevel, 0.005f, 20f);
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
        /// 根据当前帧的像素宽度智能对数对齐计算主次刻度帧间隔步长（涵盖 1 帧到 1 小时各级尺度，主刻度始终保持 70~140px 舒适间距）。
        /// </summary>
        public static (int majorInterval, int mediumInterval) CalculateTickIntervals(float frameWidth, float frameRate = 30f)
        {
            frameRate = Mathf.Max(1f, frameRate);

            // 1. 微观子秒级刻度候选（1 帧、2 帧、5 帧、10 帧）
            if (frameWidth * 1f >= 75f) return (1, 1);
            if (frameWidth * 2f >= 75f) return (2, 1);
            if (frameWidth * 5f >= 75f) return (5, 1);
            if (frameWidth * 10f >= 75f) return (10, 5);

            // 2. 宏观对数秒/分/时级刻度候选
            float pps = frameRate * frameWidth;
            int chosenMajor = Mathf.RoundToInt(3600f * frameRate);
            for (int i = 0; i < s_tickSecondCandidates.Length; i++)
            {
                float px = s_tickSecondCandidates[i] * pps;
                if (px >= 75f)
                {
                    chosenMajor = Mathf.RoundToInt(s_tickSecondCandidates[i] * frameRate);
                    break;
                }
            }

            // 3. 中刻度依阶数自适应细分
            int chosenMedium;
            if (chosenMajor <= 2)
            {
                chosenMedium = 1;
            }
            else if (chosenMajor % 5 == 0)
            {
                chosenMedium = chosenMajor / 5;
            }
            else if (chosenMajor % 2 == 0)
            {
                chosenMedium = chosenMajor / 2;
            }
            else
            {
                chosenMedium = Mathf.Max(1, chosenMajor / 2);
            }

            return (chosenMajor, chosenMedium);
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
            var (majorInterval, mediumInterval) = CalculateTickIntervals(frameWidth, _frameRate);

            Color majorTickColor = new Color(0.85f, 0.85f, 0.85f, 0.95f);
            Color mediumTickColor = new Color(0.55f, 0.55f, 0.55f, 0.75f);
            Color minorTickColor = new Color(0.35f, 0.35f, 0.35f, 0.45f);

            bool drawMinor = frameWidth >= 4f;
            bool drawMedium = (mediumInterval * frameWidth) >= 6f;
            int step = drawMinor ? 1 : (drawMedium ? mediumInterval : majorInterval);

            for (int f = 0; f <= totalFrames; f += step)
            {
                float x = f * frameWidth;
                bool isMajor = (f % majorInterval == 0);
                bool isMedium = (!isMajor && mediumInterval > 0 && f % mediumInterval == 0);

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
                    tickTop = 16f;
                }

                painter.BeginPath();
                painter.MoveTo(new Vector2(x, totalHeight));
                painter.LineTo(new Vector2(x, tickTop));
                painter.Stroke();
            }

            // 绘制逻辑内容结束线 (Content End Boundary Marker)
            if (_contentDuration > 0.001f && _contentDuration <= _clipLength)
            {
                float endX = _contentDuration * pps;
                painter.strokeColor = new Color(0.35f, 0.65f, 1.0f, 0.9f);
                painter.lineWidth = 1.5f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(endX, 0));
                painter.LineTo(new Vector2(endX, totalHeight));
                painter.Stroke();

                // 顶部向下三角标识 (End Cap Flag)
                painter.fillColor = new Color(0.35f, 0.65f, 1.0f, 0.9f);
                painter.BeginPath();
                painter.MoveTo(new Vector2(endX - 4f, 0));
                painter.LineTo(new Vector2(endX + 4f, 0));
                painter.LineTo(new Vector2(endX, 6f));
                painter.ClosePath();
                painter.Fill();
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

            int totalFrames = Mathf.Max(1, Mathf.RoundToInt(_clipLength * _frameRate));
            float frameWidth = PixelsPerSecond / _frameRate;
            var (majorInterval, _) = CalculateTickIntervals(frameWidth, _frameRate);

            // 复用对象池中的 Label，避免每次缩放/重绘时产生 GC 分配与冗余开销
            int labelIdx = 0;
            for (int f = 0; f <= totalFrames; f += majorInterval)
            {
                float x = f * frameWidth;
                Label lbl;
                if (labelIdx < _tickLabels.Count)
                {
                    lbl = _tickLabels[labelIdx];
                    lbl.style.display = DisplayStyle.Flex;
                }
                else
                {
                    lbl = new Label();
                    lbl.AddToClassList("montage-ruler-tick-label");
                    lbl.style.top = 1f;
                    lbl.pickingMode = PickingMode.Ignore;
                    _tickLabelsContainer.Add(lbl);
                    _tickLabels.Add(lbl);
                }

                lbl.text = f.ToString();
                lbl.style.left = x + 3f;
                labelIdx++;
            }

            for (int i = labelIdx; i < _tickLabels.Count; i++)
            {
                _tickLabels[i].style.display = DisplayStyle.None;
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
            float rawTime = Mathf.Clamp(localPos.x / pps, 0f, _clipLength);
            float frameInterval = 1f / Mathf.Max(1f, _frameRate);
            float clickTime = Mathf.Clamp(Mathf.Round(rawTime / frameInterval) * frameInterval, 0f, _clipLength);

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
                float rawTime = Mathf.Clamp(localPos.x / pps, 0f, _clipLength);
                float frameInterval = 1f / Mathf.Max(1f, _frameRate);
                float newTime = Mathf.Clamp(Mathf.Round(rawTime / frameInterval) * frameInterval, 0f, _clipLength);

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
