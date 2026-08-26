using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Cwcbb.Tools.CwcMontage;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// 独立物理分段系统轨道组件（MontageSectionTrackElement）。
    /// 将 Section Marker 与色块从标尺完全解耦为顶部的专用系统轨道，
    /// 提供纯净直观的分段拖拽调整、右键菜单与新增切分点交互。
    /// </summary>
    public class MontageSectionTrackElement
    {
        #region 私有常量

        public const float TRACK_HEIGHT = 24f;
        private const float BASE_PIXELS_PER_SECOND = 200f;

        #endregion

        #region 私有字段

        private MontageSequenceSO _targetAsset;
        private float _clipLength = 1f;
        private float _frameRate = 30f;
        private float _zoomLevel = 1.0f;

        private VisualElement _headerElement;
        private VisualElement _contentElement;
        private VisualElement _handlesContainer;

        private readonly List<VisualElement> _splitHandleElements = new();
        private readonly List<float> _liveSplitTimes = new();
        private int _draggingSplitIndex = -1;
        private int _selectedSplitIndex = -1;

        #endregion

        #region 公共属性

        public VisualElement HeaderElement => _headerElement;
        public VisualElement ContentElement => _contentElement;
        public float PixelsPerSecond => BASE_PIXELS_PER_SECOND * _zoomLevel;
        public float ContentPixelWidth => Mathf.Max(100f, _clipLength * PixelsPerSecond);

        #endregion

        #region 公共事件

        /// <summary>
        /// 当添加物理切分点时触发（参数为切分时间秒数）。
        /// </summary>
        public event Action<float> OnSplitAdded;

        /// <summary>
        /// 当切分点在拖拽过程中实时移动时触发（参数为索引与实时吸附时间秒数，轻量重绘）。
        /// </summary>
        public event Action<int, float> OnSplitMovedLive;

        /// <summary>
        /// 当切分点拖拽结束或确定更新时触发（参数为索引与新时间秒数，持久化保存）。
        /// </summary>
        public event Action<int, float> OnSplitMoved;

        /// <summary>
        /// 当切分点被删除时触发（参数为切分点索引）。
        /// </summary>
        public event Action<int> OnSplitRemoved;

        #endregion

        #region 构造方法

        public MontageSectionTrackElement(
            MontageSequenceSO targetAsset,
            float clipLength,
            float frameRate,
            float zoomLevel)
        {
            _targetAsset = targetAsset;
            _clipLength = Mathf.Max(0.001f, clipLength);
            _frameRate = Mathf.Max(1f, frameRate);
            _zoomLevel = Mathf.Clamp(zoomLevel, 0.1f, 20f);

            // 1. 左侧 Header
            _headerElement = new VisualElement();
            _headerElement.AddToClassList("montage-section-track-header");
            _headerElement.style.height = TRACK_HEIGHT;
            _headerElement.style.minHeight = TRACK_HEIGHT;
            _headerElement.style.maxHeight = TRACK_HEIGHT;
            _headerElement.style.flexShrink = 0;
            _headerElement.style.flexGrow = 0;

            var titleLabel = new Label("Sections");
            titleLabel.AddToClassList("montage-section-track-title");
            _headerElement.Add(titleLabel);

            var addSplitBtn = new Button(OnAddSplitClicked) { text = "+" };
            addSplitBtn.AddToClassList("montage-track-btn-toggle");
            addSplitBtn.tooltip = "Add a Section split marker at current time";
            _headerElement.Add(addSplitBtn);

            // 2. 右侧 Content
            _contentElement = new VisualElement();
            _contentElement.AddToClassList("montage-section-track-content");
            _contentElement.style.height = TRACK_HEIGHT;
            _contentElement.style.minHeight = TRACK_HEIGHT;
            _contentElement.style.maxHeight = TRACK_HEIGHT;
            _contentElement.style.flexShrink = 0;
            _contentElement.style.flexGrow = 0;
            _contentElement.style.width = ContentPixelWidth;
            _contentElement.generateVisualContent += OnGenerateVisualContent;

            _handlesContainer = new VisualElement();
            _handlesContainer.style.position = Position.Absolute;
            _handlesContainer.style.top = 0;
            _handlesContainer.style.left = 0;
            _handlesContainer.style.right = 0;
            _handlesContainer.style.bottom = 0;
            _contentElement.Add(_handlesContainer);

            // 注册鼠标事件
            _contentElement.RegisterCallback<MouseDownEvent>(OnMouseDown);
            _contentElement.RegisterCallback<MouseMoveEvent>(OnMouseMove);
            _contentElement.RegisterCallback<MouseUpEvent>(OnMouseUp);

            UpdateHandles();
        }

        #endregion

        #region 公共方法

        public void SetTargetAsset(MontageSequenceSO asset, float clipLength, float frameRate)
        {
            _targetAsset = asset;
            _clipLength = Mathf.Max(0.001f, clipLength);
            _frameRate = Mathf.Max(1f, frameRate);
            _contentElement.style.width = ContentPixelWidth;
            UpdateHandles();
            _contentElement.MarkDirtyRepaint();
        }

        public void SetZoom(float zoomLevel)
        {
            _zoomLevel = Mathf.Clamp(zoomLevel, 0.1f, 20f);
            _contentElement.style.width = ContentPixelWidth;
            UpdateHandles();
            _contentElement.MarkDirtyRepaint();
        }

        #endregion

        #region 私有绘制与 Handles 更新

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            float totalWidth = ContentPixelWidth;
            float totalHeight = TRACK_HEIGHT;

            if (totalWidth <= 0 || totalHeight <= 0 || _clipLength <= 0)
            {
                return;
            }

            var painter = mgc.painter2D;
            float pps = PixelsPerSecond;

            int splitCount = _liveSplitTimes.Count;
            int sectionCount = splitCount + 1;

            Color sectionColorEven = new Color(0.18f, 0.24f, 0.32f, 0.85f);
            Color sectionColorOdd = new Color(0.14f, 0.19f, 0.26f, 0.85f);
            Color sectionBorderCol = new Color(0.35f, 0.48f, 0.65f, 0.5f);

            for (int s = 0; s < sectionCount; s++)
            {
                float start = s == 0 ? 0f : _liveSplitTimes[s - 1];
                float end = s == splitCount ? _clipLength : _liveSplitTimes[s];
                float xStart = start * pps;
                float xEnd = end * pps;
                float sWidth = Mathf.Max(1f, xEnd - xStart);

                // 填充色块背景
                painter.fillColor = (s % 2 == 0) ? sectionColorEven : sectionColorOdd;
                painter.BeginPath();
                painter.MoveTo(new Vector2(xStart, 0));
                painter.LineTo(new Vector2(xStart + sWidth, 0));
                painter.LineTo(new Vector2(xStart + sWidth, totalHeight));
                painter.LineTo(new Vector2(xStart, totalHeight));
                painter.ClosePath();
                painter.Fill();

                // 左右切分边框
                painter.strokeColor = sectionBorderCol;
                painter.lineWidth = 1f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(xStart, 0));
                painter.LineTo(new Vector2(xStart, totalHeight));
                painter.MoveTo(new Vector2(xEnd, 0));
                painter.LineTo(new Vector2(xEnd, totalHeight));
                painter.Stroke();
            }

            // 底部横向分割线
            painter.strokeColor = new Color(0f, 0f, 0f, 0.4f);
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0, totalHeight));
            painter.LineTo(new Vector2(totalWidth, totalHeight));
            painter.Stroke();
        }

        private void UpdateHandles()
        {
            _handlesContainer.Clear();
            _splitHandleElements.Clear();
            _liveSplitTimes.Clear();

            if (_targetAsset?.SplitTimestamps != null)
            {
                _liveSplitTimes.AddRange(_targetAsset.SplitTimestamps);
            }

            float pps = PixelsPerSecond;

            // 1. 添加分段文字 Label
            int splitCount = _liveSplitTimes.Count;
            int sectionCount = splitCount + 1;
            for (int s = 0; s < sectionCount; s++)
            {
                float start = s == 0 ? 0f : _liveSplitTimes[s - 1];
                float end = s == splitCount ? _clipLength : _liveSplitTimes[s];
                float xStart = start * pps;
                float xEnd = end * pps;
                float sWidth = xEnd - xStart;

                if (sWidth > 35f)
                {
                    var label = new Label($"Section {s + 1}");
                    label.AddToClassList("montage-section-track-label");
                    label.style.left = xStart + 6f;
                    label.style.top = 3f;
                    label.pickingMode = PickingMode.Ignore;
                    _handlesContainer.Add(label);
                }
            }

            // 2. 添加切分点 Handle 手柄
            for (int i = 0; i < _liveSplitTimes.Count; i++)
            {
                int splitIndex = i;
                float splitTime = _liveSplitTimes[i];
                float xPos = splitTime * pps;

                var handle = new VisualElement();
                handle.AddToClassList("montage-section-split-handle");
                if (splitIndex == _selectedSplitIndex)
                {
                    handle.AddToClassList("montage-section-split-handle-selected");
                }
                handle.style.left = xPos;
                handle.tooltip = $"Split #{splitIndex + 1}: {splitTime:F3}s (Frame {Mathf.RoundToInt(splitTime * _frameRate)})\nDrag to adjust, Right click to remove";

                var icon = new Label("S");
                icon.style.fontSize = 8;
                icon.style.unityFontStyleAndWeight = FontStyle.Bold;
                icon.style.color = Color.white;
                icon.style.unityTextAlign = TextAnchor.MiddleCenter;
                handle.Add(icon);

                _handlesContainer.Add(handle);
                _splitHandleElements.Add(handle);
            }
        }

        #endregion

        #region 鼠标交互与右键菜单

        private void OnAddSplitClicked()
        {
            if (_targetAsset == null) return;
            float targetTime = _clipLength * 0.5f;
            OnSplitAdded?.Invoke(targetTime);
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            Vector2 localPos = evt.localMousePosition;
            float pps = PixelsPerSecond;
            float clickTime = Mathf.Clamp(localPos.x / pps, 0f, _clipLength);

            if (evt.button == 0) // 左键选择或拖拽
            {
                int hitIndex = GetHitSplitHandleIndex(localPos.x);
                if (hitIndex >= 0)
                {
                    _draggingSplitIndex = hitIndex;
                    _selectedSplitIndex = hitIndex;
                    _contentElement.CaptureMouse();
                    UpdateHandles();
                    evt.StopPropagation();
                    return;
                }

                // 双击空白处新增切分点
                if (evt.clickCount == 2)
                {
                    OnSplitAdded?.Invoke(clickTime);
                    evt.StopPropagation();
                }
            }
            else if (evt.button == 1) // 右键菜单
            {
                int hitIndex = GetHitSplitHandleIndex(localPos.x);
                var menu = new GenericMenu();
                if (hitIndex >= 0)
                {
                    int capturedIdx = hitIndex;
                    menu.AddItem(new GUIContent($"Delete Section Split #{capturedIdx + 1}"), false, () =>
                    {
                        OnSplitRemoved?.Invoke(capturedIdx);
                    });
                }
                else
                {
                    menu.AddItem(new GUIContent($"Add Section Split at {clickTime:F3}s"), false, () =>
                    {
                        OnSplitAdded?.Invoke(clickTime);
                    });
                }
                menu.ShowAsContext();
                evt.StopPropagation();
            }
        }

        private void OnMouseMove(MouseMoveEvent evt)
        {
            if (_draggingSplitIndex < 0 || _draggingSplitIndex >= _liveSplitTimes.Count)
            {
                return;
            }

            Vector2 localPos = _contentElement.WorldToLocal(evt.mousePosition);
            float pps = PixelsPerSecond;
            float rawTime = localPos.x / pps;

            // 吸附到最近帧
            float frameInterval = 1f / _frameRate;
            float snappedTime = Mathf.Round(rawTime / frameInterval) * frameInterval;

            // 限制切分范围
            float minTime = _draggingSplitIndex == 0 ? frameInterval : _liveSplitTimes[_draggingSplitIndex - 1] + frameInterval;
            float maxTime = _draggingSplitIndex == _liveSplitTimes.Count - 1 ? _clipLength - frameInterval : _liveSplitTimes[_draggingSplitIndex + 1] - frameInterval;

            snappedTime = Mathf.Clamp(snappedTime, minTime, maxTime);

            _liveSplitTimes[_draggingSplitIndex] = snappedTime;

            if (_draggingSplitIndex < _splitHandleElements.Count)
            {
                _splitHandleElements[_draggingSplitIndex].style.left = snappedTime * pps;
            }

            _contentElement.MarkDirtyRepaint();
            OnSplitMovedLive?.Invoke(_draggingSplitIndex, snappedTime);
            evt.StopPropagation();
        }

        private void OnMouseUp(MouseUpEvent evt)
        {
            if (_draggingSplitIndex >= 0 && evt.button == 0)
            {
                int finishedIndex = _draggingSplitIndex;
                float finishedTime = _liveSplitTimes[finishedIndex];
                _draggingSplitIndex = -1;
                _contentElement.ReleaseMouse();

                OnSplitMoved?.Invoke(finishedIndex, finishedTime);
                UpdateHandles();
                _contentElement.MarkDirtyRepaint();
                evt.StopPropagation();
            }
        }

        private int GetHitSplitHandleIndex(float localX)
        {
            float pps = PixelsPerSecond;
            float hitRadius = 8f;

            for (int i = 0; i < _liveSplitTimes.Count; i++)
            {
                float handleX = _liveSplitTimes[i] * pps;
                if (Mathf.Abs(localX - handleX) <= hitRadius)
                {
                    return i;
                }
            }
            return -1;
        }

        #endregion
    }
}
