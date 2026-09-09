using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Cwcbb.Tools.CwcMontage;

namespace Cwcbb.Tools.CwcMontage.Editor
{
    /// <summary>
    /// 单条核心动画轨道组件（MontageAnimationTrackElement）。
    /// 以 Unity Timeline 工业级规范为设计标杆，提供单轨道多动画片段（Segments）排布、
    /// 独立调速（PlayRate / Time Stretch）、边缘修剪（Trim）、重叠即混合（Overlap-to-Crossfade）与磁吸对齐交互。
    /// </summary>
    public class MontageAnimationTrackElement
    {
        #region 公共常量

        public const float TRACK_HEIGHT = 32f;
        private const float BASE_PIXELS_PER_SECOND = 200f;
        private const float SNAP_PIXEL_THRESHOLD = 8f;

        #endregion

        #region 私有字段

        private MontageSequenceSO _targetAsset;
        private float _clipLength = 1f;
        private float _frameRate = 30f;
        private float _zoomLevel = 1.0f;
        private float _contentDuration = 0f;

        private VisualElement _headerElement;
        private VisualElement _contentElement;
        private VisualElement _segmentsContainer;
        private VisualElement _crossfadeContainer;

        private MontageAnimationSegment _selectedSegment;
        private int _selectedSegmentIndex = -1;
        private bool _isDraggingSegment;
        private int _draggingIndex = -1;
        private Vector2 _dragStartMousePos;
        private float _dragStartSegmentTime;
        private float _dragStartDuration;
        private float _dragStartOffset;
        private float _dragStartPlayRate;
        private int _dragMode; // 0: Move, 1: StretchRight, 2: StretchLeft
        private MontageAnimationSegment _activePickerTargetSegment;
        private float _pendingAddClipStartTime = -1f;
        private VisualElement _draggingBlockElement;
        private bool _hasDragMoved;

        #endregion

        #region 公共属性

        public VisualElement HeaderElement => _headerElement;
        public VisualElement ContentElement => _contentElement;
        public float PixelsPerSecond => BASE_PIXELS_PER_SECOND * _zoomLevel;
        public float ContentPixelWidth => Mathf.Max(100f, _clipLength * PixelsPerSecond);
        public int SelectedSegmentIndex => _selectedSegmentIndex;
        public MontageAnimationSegment SelectedSegment => _selectedSegment;

        #endregion

        #region 公共事件

        /// <summary>
        /// 当在动画轨道上选中某个动画片段时触发（传出选中的片段与索引）。
        /// </summary>
        public event Action<MontageAnimationSegment, int> OnSegmentSelected;

        /// <summary>
        /// 当片段数据被修改（移动、缩放、增删、调速）时触发。
        /// </summary>
        public event Action OnDataModified;

        /// <summary>
        /// 当双击或点击请求跳转时间轴时触发。
        /// </summary>
        public event Action<float> OnRequestScrubTime;

        #endregion

        #region 构造方法

        public MontageAnimationTrackElement(
            MontageSequenceSO targetAsset,
            float clipLength,
            float frameRate,
            float zoomLevel)
        {
            _targetAsset = targetAsset;
            _clipLength = Mathf.Max(0.001f, clipLength);
            _frameRate = Mathf.Max(1f, frameRate);
            _zoomLevel = Mathf.Clamp(zoomLevel, 0.005f, 20f);

            BuildHeader();
            BuildContent();
            RebuildSegments();
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 外部同步资产数据与总时长。
        /// </summary>
        public void SetTargetAsset(MontageSequenceSO targetAsset, float clipLength, float frameRate, float contentDuration = -1f)
        {
            _targetAsset = targetAsset;
            _clipLength = Mathf.Max(0.001f, clipLength);
            _frameRate = Mathf.Max(1f, frameRate);
            _contentDuration = contentDuration >= 0f ? contentDuration : (_targetAsset != null ? _targetAsset.TotalDuration : _clipLength);

            UpdateContentWidth();
            RebuildSegments();
            _contentElement?.MarkDirtyRepaint();
        }

        /// <summary>
        /// 设置当前视图缩放级别并刷新几何布局。
        /// </summary>
        public void SetZoom(float newZoom)
        {
            _zoomLevel = Mathf.Clamp(newZoom, 0.005f, 20f);
            UpdateContentWidth();
            RebuildSegments();
        }

        /// <summary>
        /// 选中指定动画片段（基于对象引用，确保哪怕重叠或排序也绝不串台）。
        /// </summary>
        /// <param name="segment">目标动画片段实例</param>
        public void SelectSegment(MontageAnimationSegment segment)
        {
            _selectedSegment = segment;
            _selectedSegmentIndex = (_targetAsset?.AnimationSegments != null && segment != null)
                ? _targetAsset.AnimationSegments.IndexOf(segment)
                : -1;

            RefreshSelectedSegmentHighlight();

            if (segment != null && _selectedSegmentIndex >= 0)
            {
                OnSegmentSelected?.Invoke(segment, _selectedSegmentIndex);
            }
        }

        /// <summary>
        /// 外部选中指定索引的片段（仅轻量刷新样式，避免重建 DOM）。
        /// </summary>
        /// <param name="index">目标片段在资产中的索引</param>
        public void SelectSegment(int index)
        {
            if (_targetAsset?.AnimationSegments != null && index >= 0 && index < _targetAsset.AnimationSegments.Count)
            {
                SelectSegment(_targetAsset.AnimationSegments[index]);
            }
            else
            {
                ClearSelection();
            }
        }

        /// <summary>
        /// 取消片段选中。
        /// </summary>
        public void ClearSelection()
        {
            if (_selectedSegment == null && _selectedSegmentIndex == -1) return;
            _selectedSegment = null;
            _selectedSegmentIndex = -1;
            RefreshSelectedSegmentHighlight();
        }

        #endregion

        #region UI 布局与构建

        private void BuildHeader()
        {
            _headerElement = new VisualElement();
            _headerElement.AddToClassList("montage-track-header");
            _headerElement.style.height = TRACK_HEIGHT;
            _headerElement.style.minHeight = TRACK_HEIGHT;
            _headerElement.style.maxHeight = TRACK_HEIGHT;
            _headerElement.style.flexShrink = 0;
            _headerElement.style.flexGrow = 0;

            var titleBox = new VisualElement();
            titleBox.AddToClassList("montage-track-title-box");

            var titleLabel = new Label("Animation");
            titleLabel.AddToClassList("montage-track-title");
            titleLabel.tooltip = "Core animation track supporting multiple clips, individual play rates and overlap crossfades.";
            titleBox.Add(titleLabel);

            _headerElement.Add(titleBox);

            var buttonsGroup = new VisualElement();
            buttonsGroup.style.flexDirection = FlexDirection.Row;
            buttonsGroup.style.alignItems = Align.Center;

            // 与普通轨道一致的添加按钮
            var addClipButton = new Button(ShowAddClipPicker) { text = "+" };
            addClipButton.AddToClassList("montage-toolbar-btn");
            addClipButton.tooltip = "Add Animation Clip to this track";
            buttonsGroup.Add(addClipButton);

            _headerElement.Add(buttonsGroup);
        }

        private void BuildContent()
        {
            _contentElement = new VisualElement();
            _contentElement.AddToClassList("montage-anim-track-content");
            _contentElement.style.height = TRACK_HEIGHT;
            _contentElement.style.minHeight = TRACK_HEIGHT;
            _contentElement.style.maxHeight = TRACK_HEIGHT;
            _contentElement.style.position = Position.Relative;
            _contentElement.pickingMode = PickingMode.Position;

            _segmentsContainer = new VisualElement();
            _segmentsContainer.style.position = Position.Absolute;
            _segmentsContainer.style.left = 0;
            _segmentsContainer.style.top = 0;
            _segmentsContainer.style.bottom = 0;
            _segmentsContainer.style.right = 0;
            _segmentsContainer.pickingMode = PickingMode.Position;
            _contentElement.Add(_segmentsContainer);

            // 交叉混合层（置于条块上方作为半透明对角浮层）
            _crossfadeContainer = new VisualElement();
            _crossfadeContainer.style.position = Position.Absolute;
            _crossfadeContainer.style.left = 0;
            _crossfadeContainer.style.top = 0;
            _crossfadeContainer.style.bottom = 0;
            _crossfadeContainer.style.right = 0;
            _crossfadeContainer.pickingMode = PickingMode.Ignore;
            _contentElement.Add(_crossfadeContainer);

            // 注册内容区右键菜单、拖放与快捷键
            _contentElement.focusable = true;
            _contentElement.RegisterCallback<KeyDownEvent>(OnContentKeyDown);
            _contentElement.RegisterCallback<MouseDownEvent>(OnContentMouseDown);
            _contentElement.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            _contentElement.RegisterCallback<DragPerformEvent>(OnDragPerform);
            _contentElement.generateVisualContent += OnGenerateContentVisual;

            UpdateContentWidth();
        }

        private void UpdateContentWidth()
        {
            if (_contentElement != null)
            {
                _contentElement.style.width = ContentPixelWidth;
            }
        }

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
            var (majorInterval, mediumInterval) = MontageTimelineRuler.CalculateTickIntervals(frameWidth, _frameRate);

            Color majorGridColor = new Color(1f, 1f, 1f, 0.09f);
            Color minorGridColor = new Color(1f, 1f, 1f, 0.04f);

            bool drawMinor = frameWidth >= 5f;
            bool drawMedium = (mediumInterval * frameWidth) >= 6f;
            int step = drawMinor ? 1 : (drawMedium ? mediumInterval : majorInterval);

            for (int f = 0; f <= totalFrames; f += step)
            {
                bool isMajor = (f % majorInterval == 0);
                bool isMedium = (!isMajor && mediumInterval > 0 && f % mediumInterval == 0);

                if (!isMajor && !isMedium && !drawMinor) continue;

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

            // 3. 绘制内容结束指示虚线 (Content End Boundary Line)
            float contentEnd = _contentDuration > 0.001f ? _contentDuration : (_targetAsset != null ? _targetAsset.TotalDuration : 0f);
            if (contentEnd > 0.001f && contentEnd <= _clipLength)
            {
                float xEnd = contentEnd * pps;
                painter.strokeColor = new Color(0.35f, 0.65f, 1.0f, 0.65f);
                painter.lineWidth = 1.5f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(xEnd, 0));
                painter.LineTo(new Vector2(xEnd, height));
                painter.Stroke();
            }

            // 4. 绘制底部分割线
            painter.strokeColor = new Color(0f, 0f, 0f, 0.35f);
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0, height));
            painter.LineTo(new Vector2(width, height));
            painter.Stroke();
        }

        #endregion

        #region 片段重构与渲染 (Segments & Crossfade Overlays)

        public void RebuildSegments()
        {
            _segmentsContainer?.Clear();
            _crossfadeContainer?.Clear();

            if (_targetAsset?.AnimationSegments == null || _targetAsset.AnimationSegments.Count == 0)
            {
                _selectedSegment = null;
                _selectedSegmentIndex = -1;
                return;
            }

            // 若之前选中的片段已不在资产中，清空选中；若仍在，同步最新索引
            if (_selectedSegment != null)
            {
                _selectedSegmentIndex = _targetAsset.AnimationSegments.IndexOf(_selectedSegment);
                if (_selectedSegmentIndex < 0)
                {
                    _selectedSegment = null;
                }
            }
            else if (_selectedSegmentIndex >= 0 && _selectedSegmentIndex < _targetAsset.AnimationSegments.Count)
            {
                _selectedSegment = _targetAsset.AnimationSegments[_selectedSegmentIndex];
            }

            var segments = _targetAsset.AnimationSegments;
            float pps = PixelsPerSecond;

            // 构建按 StartTime 升序排序的片段列表，用于计算每个片段的重叠区间与自身独占空间
            var sortedList = new List<MontageAnimationSegment>(segments);
            sortedList.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            // 1. 绘制片段条块
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                if (seg == null) continue;

                // 计算当前片段被前一个片段与后一个片段重叠的时长 (Overlap Duration)
                int sortedIdx = sortedList.IndexOf(seg);
                float leftOverlap = 0f;
                float rightOverlap = 0f;

                if (sortedIdx > 0)
                {
                    var prev = sortedList[sortedIdx - 1];
                    if (prev != null && prev.EndTime > seg.StartTime)
                    {
                        leftOverlap = Mathf.Max(0f, prev.EndTime - seg.StartTime);
                    }
                }

                if (sortedIdx >= 0 && sortedIdx < sortedList.Count - 1)
                {
                    var next = sortedList[sortedIdx + 1];
                    if (next != null && next.StartTime < seg.EndTime)
                    {
                        rightOverlap = Mathf.Max(0f, seg.EndTime - next.StartTime);
                    }
                }

                var block = CreateSegmentBlock(seg, i, pps, leftOverlap, rightOverlap);
                _segmentsContainer?.Add(block);
            }

            // 2. 绘制相邻重叠交叉淡化区域 (Timeline Style Overlap-to-Crossfade)
            UpdateCrossfadeOverlays();
        }

        private VisualElement CreateSegmentBlock(MontageAnimationSegment seg, int index, float pps, float leftOverlap, float rightOverlap)
        {
            float left = seg.StartTime * pps;
            float width = Mathf.Max(12f, seg.Duration * pps);

            var block = new VisualElement();
            block.userData = seg;
            block.AddToClassList("montage-anim-segment-block");
            if (seg == _selectedSegment)
            {
                block.AddToClassList("montage-anim-segment-selected");
            }

            block.style.left = left;
            block.style.width = width;
            block.style.height = TRACK_HEIGHT - 4f;
            block.style.top = 2f;

            // 自身独占区间内容容器：避开前后的交叉淡化混合区域，仅在自身独占非重叠区域内排布名称与角标
            float selfLeftPx = leftOverlap * pps;
            float selfRightPx = rightOverlap * pps;
            float selfWidthPx = width - selfLeftPx - selfRightPx;

            if (selfWidthPx >= 12f)
            {
                var selfContent = new VisualElement();
                selfContent.AddToClassList("montage-anim-segment-self-content");
                selfContent.pickingMode = PickingMode.Ignore;
                selfContent.style.left = selfLeftPx;
                selfContent.style.right = selfRightPx;

                // 片段名称标签（超出自身独占范围显示省略号 Ellipsis）
                var label = new Label(seg.SegmentName);
                label.AddToClassList("montage-anim-segment-title");
                label.tooltip = $"{seg.SegmentName}\nStart: {seg.StartTime:F2}s (Frame {Mathf.RoundToInt(seg.StartTime * _frameRate)})\nDuration: {seg.Duration:F2}s ({Mathf.RoundToInt(seg.Duration * _frameRate)} frames)\nRate: {seg.PlayRate:F2}x";
                selfContent.Add(label);

                // 角标：调速倍率指示（非 1.0x 高亮）
                if (Mathf.Abs(seg.PlayRate - 1.0f) > 0.01f)
                {
                    var rateBadge = new Label($"{seg.PlayRate:F2}x");
                    rateBadge.AddToClassList("montage-anim-segment-badge");
                    selfContent.Add(rateBadge);
                }

                block.Add(selfContent);
            }

            // 左右边缘手柄：两端拖拽均为缩放播放速率 (Time Stretch)
            var leftHandle = new VisualElement();
            leftHandle.AddToClassList("montage-anim-segment-handle");
            leftHandle.AddToClassList("montage-anim-segment-handle-left");
            leftHandle.tooltip = "Drag left edge to stretch duration and adjust PlayRate (adjusts start time)";
            leftHandle.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    SelectSegment(seg);
                    int curIdx = _targetAsset?.AnimationSegments != null ? _targetAsset.AnimationSegments.IndexOf(seg) : index;
                    if (curIdx >= 0)
                    {
                        StartDrag(evt, curIdx, 2); // 2: Stretch Left
                    }
                    evt.StopPropagation();
                }
            });
            block.Add(leftHandle);

            var rightHandle = new VisualElement();
            rightHandle.AddToClassList("montage-anim-segment-handle");
            rightHandle.AddToClassList("montage-anim-segment-handle-right");
            rightHandle.tooltip = "Drag right edge to stretch duration and adjust PlayRate";
            rightHandle.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    SelectSegment(seg);
                    int curIdx = _targetAsset?.AnimationSegments != null ? _targetAsset.AnimationSegments.IndexOf(seg) : index;
                    if (curIdx >= 0)
                    {
                        StartDrag(evt, curIdx, 1); // 1: Stretch Right
                    }
                    evt.StopPropagation();
                }
            });
            block.Add(rightHandle);

            // 主体点击与移动拖拽
            block.RegisterCallback<MouseDownEvent>(evt =>
            {
                SelectSegment(seg);
                int curIdx = _targetAsset?.AnimationSegments != null ? _targetAsset.AnimationSegments.IndexOf(seg) : index;
                if (evt.button == 0)
                {
                    if (curIdx >= 0)
                    {
                        StartDrag(evt, curIdx, 0);
                    }
                    evt.StopPropagation();
                }
                else if (evt.button == 1)
                {
                    if (curIdx >= 0)
                    {
                        ShowSegmentContextMenu(curIdx, evt.mousePosition);
                    }
                    evt.StopPropagation();
                }
            });

            return block;
        }

        private void UpdateCrossfadeOverlays()
        {
            if (_crossfadeContainer == null || _targetAsset?.AnimationSegments == null) return;

            var segments = _targetAsset.AnimationSegments;
            float pps = PixelsPerSecond;

            // 1. 计算当前所有两两相邻片段的重叠区间
            var sortedList = new List<MontageAnimationSegment>(segments);
            sortedList.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            var activeOverlaps = new List<(float start, float duration, MontageAnimationSegment segA, MontageAnimationSegment segB, int idxA, int idxB)>();
            for (int i = 0; i < sortedList.Count - 1; i++)
            {
                var segA = sortedList[i];
                var segB = sortedList[i + 1];
                if (segA == null || segB == null) continue;

                if (segB.StartTime < segA.EndTime)
                {
                    float overlapStart = segB.StartTime;
                    float overlapEnd = Mathf.Min(segA.EndTime, segB.EndTime);
                    float overlapDuration = Mathf.Max(0.0001f, overlapEnd - overlapStart);
                    int indexA = segments.IndexOf(segA);
                    int indexB = segments.IndexOf(segB);
                    activeOverlaps.Add((overlapStart, overlapDuration, segA, segB, indexA, indexB));
                }
            }

            // 2. 动态就地复用或增删 _crossfadeContainer 中的元素
            while (_crossfadeContainer.childCount > activeOverlaps.Count)
            {
                _crossfadeContainer.RemoveAt(_crossfadeContainer.childCount - 1);
            }

            for (int i = 0; i < activeOverlaps.Count; i++)
            {
                var item = activeOverlaps[i];
                if (i < _crossfadeContainer.childCount)
                {
                    var overlay = _crossfadeContainer[i];
                    ApplyCrossfadeOverlayData(overlay, item.start, item.duration, pps, item.segA, item.segB, item.idxA, item.idxB);
                }
                else
                {
                    var newOverlay = CreateCrossfadeOverlay(item.start, item.duration, pps, item.segA, item.segB, item.idxA, item.idxB);
                    _crossfadeContainer.Add(newOverlay);
                }
            }
        }

        private void ApplyCrossfadeOverlayData(VisualElement overlay, float startTime, float duration, float pps, MontageAnimationSegment segA, MontageAnimationSegment segB, int indexA, int indexB)
        {
            if (overlay?.userData is CrossfadeOverlayHolder holder)
            {
                holder.SegmentA = segA;
                holder.SegmentB = segB;
                holder.IndexA = indexA;
                holder.IndexB = indexB;

                float left = startTime * pps;
                float width = Mathf.Max(6f, duration * pps);

                overlay.style.left = left;
                overlay.style.width = width;

                float handleWidth = Mathf.Clamp(width * 0.35f, 2f, 8f);
                if (holder.LeftTrimHandle != null)
                {
                    holder.LeftTrimHandle.style.width = handleWidth;
                }
                if (holder.RightTrimHandle != null)
                {
                    holder.RightTrimHandle.style.width = handleWidth;
                }

                if (holder.DurationLabel != null)
                {
                    holder.DurationLabel.text = $"{duration:F2}s";
                    holder.DurationLabel.tooltip = $"Crossfade Blending: {duration:F2}s ({Mathf.RoundToInt(duration * _frameRate)} frames)";
                    holder.DurationLabel.style.display = (width > 28f) ? DisplayStyle.Flex : DisplayStyle.None;
                }

                overlay.MarkDirtyRepaint();
            }
        }

        private VisualElement CreateCrossfadeOverlay(float startTime, float duration, float pps, MontageAnimationSegment initialSegA, MontageAnimationSegment initialSegB, int indexA, int indexB)
        {
            if (_targetAsset?.AnimationSegments == null) return new VisualElement();
            if (initialSegA == null || initialSegB == null) return new VisualElement();

            float left = startTime * pps;
            float width = Mathf.Max(6f, duration * pps);
            float height = TRACK_HEIGHT - 4f;

            var overlay = new VisualElement();
            overlay.AddToClassList("montage-anim-crossfade-overlay");
            overlay.pickingMode = PickingMode.Position; // 允许捕获事件以支持对角线拾取与分段竖线调节
            overlay.style.left = left;
            overlay.style.width = width;
            overlay.style.height = height;
            overlay.style.top = 2f;
            overlay.style.backgroundColor = Color.clear;

            // 绘制 Timeline 经典对角分割、两端分段竖线与选中高亮边框
            overlay.generateVisualContent += mgc =>
            {
                var painter = mgc.painter2D;
                float w = overlay.contentRect.width > 0f ? overlay.contentRect.width : overlay.style.width.value.value;
                float h = overlay.contentRect.height > 0f ? overlay.contentRect.height : (TRACK_HEIGHT - 4f);
                if (w <= 2f || h <= 2f) return;

                var hld = overlay.userData as CrossfadeOverlayHolder;
                var sA = hld != null ? hld.SegmentA : initialSegA;
                var sB = hld != null ? hld.SegmentB : initialSegB;

                bool isASelected = (sA != null && sA == _selectedSegment);
                bool isBSelected = (sB != null && sB == _selectedSegment);

                // 统一边框线宽为精致的 1.0px，与条块主体 1px 白色边框在像素级严格对齐
                const float BORDER_WIDTH = 1.0f;
                float halfW = BORDER_WIDTH * 0.5f;

                // 1. 绘制左下直角三角形底色 (片段 A 淡出区域)
                // 选中时采用与 USS .montage-anim-segment-selected 完全一致的 #265993，未选中时保持 #1B385C
                Color colorA = isASelected ? new Color(0.15f, 0.35f, 0.58f, 1.0f) : new Color(0.11f, 0.22f, 0.36f, 1.0f);
                painter.fillColor = colorA;
                painter.BeginPath();
                painter.MoveTo(new Vector2(0, 0));
                painter.LineTo(new Vector2(0, h));
                painter.LineTo(new Vector2(w, h));
                painter.ClosePath();
                painter.Fill();

                // 2. 绘制右上直角三角形底色 (片段 B 淡入区域)
                Color colorB = isBSelected ? new Color(0.15f, 0.35f, 0.58f, 1.0f) : new Color(0.11f, 0.22f, 0.36f, 1.0f);
                painter.fillColor = colorB;
                painter.BeginPath();
                painter.MoveTo(new Vector2(0, 0));
                painter.LineTo(new Vector2(w, 0));
                painter.LineTo(new Vector2(w, h));
                painter.ClosePath();
                painter.Fill();

                // 3. 绘制垂直分段边界竖线 (无论相邻剪辑是否被选中，始终绘制过渡区两端分段竖线，清晰标识两端片段起止边界)
                painter.strokeColor = new Color(1.0f, 1.0f, 1.0f, 0.35f);
                painter.lineWidth = BORDER_WIDTH;

                // 左竖线 (x = halfW): 片段 B 起点 / 片段 A 独占区终点
                painter.BeginPath();
                painter.MoveTo(new Vector2(halfW, 0));
                painter.LineTo(new Vector2(halfW, h));
                painter.Stroke();

                // 右竖线 (x = w - halfW): 片段 A 终点 / 片段 B 独占区起点
                painter.BeginPath();
                painter.MoveTo(new Vector2(w - halfW, 0));
                painter.LineTo(new Vector2(w - halfW, h));
                painter.Stroke();

                // 4. 绘制水平外边缘边框 (使用半像素偏移 halfW / h - halfW，保证 1px 笔刷完全落在 contentRect 内，不被裁剪且与条块主体 CSS 边框绝对无缝连续)
                // 动画条块默认未选中状态下的浅蓝色边框 (#2F649B)
                Color defaultBorderColor = new Color(0.184f, 0.392f, 0.608f, 1.0f);

                // 顶边属于片段 B 的外边缘：若 B 选中呈现 1px 纯白高亮；若未选中则呈现动画剪辑自身的 1px 浅蓝边框
                painter.strokeColor = isBSelected ? Color.white : defaultBorderColor;
                painter.lineWidth = BORDER_WIDTH;
                painter.BeginPath();
                painter.MoveTo(new Vector2(0, halfW));
                painter.LineTo(new Vector2(w, halfW));
                painter.Stroke();

                // 底边属于片段 A 的外边缘：若 A 选中呈现 1px 纯白高亮；若未选中则呈现动画剪辑自身的 1px 浅蓝边框
                painter.strokeColor = isASelected ? Color.white : defaultBorderColor;
                painter.lineWidth = BORDER_WIDTH;
                painter.BeginPath();
                painter.MoveTo(new Vector2(0, h - halfW));
                painter.LineTo(new Vector2(w, h - halfW));
                painter.Stroke();

                // 5. 绘制 Timeline 经典单对角分割线 (从 (0, halfW) 到 (w, h - halfW))
                // 若任一片段被选中，对角线高亮为 1.0px 纯白，与顶/底边和主体边框完美闭合；未选中时为 1.0px 半透明白色
                painter.strokeColor = (isASelected || isBSelected) ? Color.white : new Color(1.0f, 1.0f, 1.0f, 0.40f);
                painter.lineWidth = BORDER_WIDTH;
                painter.BeginPath();
                painter.MoveTo(new Vector2(0, halfW));
                painter.LineTo(new Vector2(w, h - halfW));
                painter.Stroke();
            };

            // 垂直手柄：宽度自适应重叠区域，保证窄过渡时互不遮挡且留出对角线点击区
            float handleWidth = Mathf.Clamp(width * 0.35f, 2f, 8f);

            // 垂直手柄 1: 片段 B 左手柄 (调节片段 B 的左起点与 PlayRate)
            var leftTrimHandle = new VisualElement();
            leftTrimHandle.AddToClassList("montage-anim-segment-handle");
            leftTrimHandle.AddToClassList("montage-anim-segment-handle-left");
            leftTrimHandle.tooltip = $"Drag to adjust start time of {initialSegB.SegmentName}";
            leftTrimHandle.style.position = Position.Absolute;
            leftTrimHandle.style.left = 0;
            leftTrimHandle.style.width = handleWidth;
            leftTrimHandle.style.top = 0;
            leftTrimHandle.style.bottom = 0;
            leftTrimHandle.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    var hld = overlay.userData as CrossfadeOverlayHolder;
                    var sB = hld != null ? hld.SegmentB : initialSegB;
                    SelectSegment(sB);
                    int curB = _targetAsset?.AnimationSegments != null && sB != null ? _targetAsset.AnimationSegments.IndexOf(sB) : -1;
                    if (curB >= 0)
                    {
                        StartDrag(evt, curB, 2); // 2: Stretch Left of B
                    }
                    evt.StopPropagation();
                }
            });
            overlay.Add(leftTrimHandle);

            // 垂直手柄 2: 片段 A 右手柄 (调节片段 A 的持续时间与 PlayRate)
            var rightTrimHandle = new VisualElement();
            rightTrimHandle.AddToClassList("montage-anim-segment-handle");
            rightTrimHandle.AddToClassList("montage-anim-segment-handle-right");
            rightTrimHandle.tooltip = $"Drag to adjust duration of {initialSegA.SegmentName}";
            rightTrimHandle.style.position = Position.Absolute;
            rightTrimHandle.style.right = 0;
            rightTrimHandle.style.width = handleWidth;
            rightTrimHandle.style.top = 0;
            rightTrimHandle.style.bottom = 0;
            rightTrimHandle.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    var hld = overlay.userData as CrossfadeOverlayHolder;
                    var sA = hld != null ? hld.SegmentA : initialSegA;
                    SelectSegment(sA);
                    int curA = _targetAsset?.AnimationSegments != null && sA != null ? _targetAsset.AnimationSegments.IndexOf(sA) : -1;
                    if (curA >= 0)
                    {
                        StartDrag(evt, curA, 1); // 1: Stretch Right of A
                    }
                    evt.StopPropagation();
                }
            });
            overlay.Add(rightTrimHandle);

            // 绘制居中的重叠时长标签
            var durLbl = new Label($"{duration:F2}s");
            durLbl.AddToClassList("montage-anim-crossfade-label");
            durLbl.pickingMode = PickingMode.Ignore; // 避免阻挡鼠标对角线判定
            durLbl.tooltip = $"Crossfade Blending: {duration:F2}s ({Mathf.RoundToInt(duration * _frameRate)} frames)";
            durLbl.style.display = (width > 28f) ? DisplayStyle.Flex : DisplayStyle.None;
            overlay.Add(durLbl);

            var holder = new CrossfadeOverlayHolder
            {
                Root = overlay,
                LeftTrimHandle = leftTrimHandle,
                RightTrimHandle = rightTrimHandle,
                DurationLabel = durLbl,
                SegmentA = initialSegA,
                SegmentB = initialSegB,
                IndexA = indexA,
                IndexB = indexB
            };
            overlay.userData = holder;

            // 鼠标移动与悬停：根据落在对角线的左下还是右上动态显示对应片段的 tooltip
            overlay.RegisterCallback<MouseMoveEvent>(evt =>
            {
                float w = overlay.contentRect.width > 0f ? overlay.contentRect.width : overlay.style.width.value.value;
                float h = overlay.contentRect.height > 0f ? overlay.contentRect.height : (TRACK_HEIGHT - 4f);
                if (w <= 0f || h <= 0f) return;

                var hld = overlay.userData as CrossfadeOverlayHolder;
                var sA = hld != null ? hld.SegmentA : initialSegA;
                var sB = hld != null ? hld.SegmentB : initialSegB;

                float diagY = (h / w) * evt.localMousePosition.x;
                bool isHitA = evt.localMousePosition.y >= diagY;
                var targetSeg = isHitA ? sA : sB;
                if (targetSeg != null)
                {
                    overlay.tooltip = $"{targetSeg.SegmentName}\nStart: {targetSeg.StartTime:F2}s\nDuration: {targetSeg.Duration:F2}s\nRate: {targetSeg.PlayRate:F2}x";
                }
            });

            // 鼠标按下：左键点击对角线左下选 A、右上选 B 并可拖动平移；右键点击对角线弹出对应菜单
            overlay.RegisterCallback<MouseDownEvent>(evt =>
            {
                float w = overlay.contentRect.width > 0f ? overlay.contentRect.width : overlay.style.width.value.value;
                float h = overlay.contentRect.height > 0f ? overlay.contentRect.height : (TRACK_HEIGHT - 4f);
                if (w <= 0f || h <= 0f) return;

                var hld = overlay.userData as CrossfadeOverlayHolder;
                var sA = hld != null ? hld.SegmentA : initialSegA;
                var sB = hld != null ? hld.SegmentB : initialSegB;

                float diagY = (h / w) * evt.localMousePosition.x;
                bool isHitA = evt.localMousePosition.y >= diagY;
                var targetSeg = isHitA ? sA : sB;
                if (targetSeg == null) return;

                int targetIdx = _targetAsset?.AnimationSegments != null ? _targetAsset.AnimationSegments.IndexOf(targetSeg) : -1;

                if (evt.button == 0) // 左键
                {
                    SelectSegment(targetSeg);
                    if (targetIdx >= 0)
                    {
                        StartDrag(evt, targetIdx, 0); // 0: Move
                    }
                    evt.StopPropagation();
                }
                else if (evt.button == 1) // 右键
                {
                    SelectSegment(targetSeg);
                    if (targetIdx >= 0)
                    {
                        ShowSegmentContextMenu(targetIdx, evt.mousePosition);
                    }
                    evt.StopPropagation();
                }
            });

            return overlay;
        }

        #endregion

        #region 拖拽、裁剪与时间伸缩调速 (Move, Trim, Stretch & Snapping)

        private void StartDrag(MouseDownEvent evt, int segmentIndex, int mode)
        {
            if (evt.button != 0 || _targetAsset?.AnimationSegments == null) return;
            if (segmentIndex < 0 || segmentIndex >= _targetAsset.AnimationSegments.Count) return;

            var seg = _targetAsset.AnimationSegments[segmentIndex];
            if (seg == null) return;

            _isDraggingSegment = true;
            _draggingIndex = segmentIndex;
            _dragMode = mode;
            _dragStartMousePos = evt.mousePosition;
            _dragStartSegmentTime = seg.StartTime;
            _dragStartDuration = seg.Duration;
            _dragStartOffset = seg.StartOffset;
            _dragStartPlayRate = seg.PlayRate;
            _hasDragMoved = false;

            _draggingBlockElement = null;
            if (_segmentsContainer != null && seg != null)
            {
                for (int i = 0; i < _segmentsContainer.childCount; i++)
                {
                    if (_segmentsContainer[i].userData == seg)
                    {
                        _draggingBlockElement = _segmentsContainer[i];
                        break;
                    }
                }
            }

            if (_selectedSegment != seg || _selectedSegmentIndex != segmentIndex)
            {
                _selectedSegment = seg;
                _selectedSegmentIndex = segmentIndex;
                RefreshSelectedSegmentHighlight();
                OnSegmentSelected?.Invoke(seg, segmentIndex);
            }

            _contentElement.CaptureMouse();
            _contentElement.RegisterCallback<MouseMoveEvent>(OnDragMove);
            _contentElement.RegisterCallback<MouseUpEvent>(OnDragEnd);
            evt.StopPropagation();
        }

        private void OnDragMove(MouseMoveEvent evt)
        {
            if (!_isDraggingSegment || _draggingIndex < 0 || _targetAsset?.AnimationSegments == null) return;
            if (_draggingIndex >= _targetAsset.AnimationSegments.Count) return;

            float deltaPixel = evt.mousePosition.x - _dragStartMousePos.x;
            if (!_hasDragMoved && Mathf.Abs(deltaPixel) > 1.5f)
            {
                _hasDragMoved = true;
                Undo.RecordObject(_targetAsset, _dragMode == 0 ? "Move Animation Segment" : "Stretch Animation Segment");
            }

            if (!_hasDragMoved)
            {
                return;
            }

            var seg = _targetAsset.AnimationSegments[_draggingIndex];
            float pps = PixelsPerSecond;
            float deltaTime = deltaPixel / pps;

            float frameInterval = 1f / Mathf.Max(1f, _frameRate);

            // 模式 0: 整体水平平移移动 (Move) - 离散在每帧上对齐滑行
            if (_dragMode == 0)
            {
                float targetTime = Mathf.Max(0f, _dragStartSegmentTime + deltaTime);
                targetTime = SnapToFrame(targetTime);
                targetTime = ApplyMagneticSnapping(targetTime, _draggingIndex, isStart: true);
                targetTime = SnapToFrame(targetTime);
                seg.StartTime = Mathf.Max(0f, targetTime);
            }
            // 模式 1: 拖拽右边缘拉伸调速 (Stretch Right, Adjust Duration) - 终点离散对齐到每帧
            else if (_dragMode == 1)
            {
                float targetDuration = Mathf.Max(frameInterval, _dragStartDuration + deltaTime);
                float targetEndTime = _dragStartSegmentTime + targetDuration;
                targetEndTime = SnapToFrame(targetEndTime);
                targetEndTime = ApplyMagneticSnapping(targetEndTime, _draggingIndex, isStart: false);
                targetEndTime = SnapToFrame(targetEndTime);
                targetEndTime = Mathf.Max(_dragStartSegmentTime + frameInterval, targetEndTime);
                seg.Duration = Mathf.Max(frameInterval, targetEndTime - _dragStartSegmentTime);
            }
            // 模式 2: 拖拽左边缘拉伸调速 (Stretch Left, Adjust Duration, Keep EndTime) - 起点离散对齐到每帧
            else if (_dragMode == 2)
            {
                float originalEndTime = SnapToFrame(_dragStartSegmentTime + _dragStartDuration);
                float targetStartTime = Mathf.Max(0f, _dragStartSegmentTime + deltaTime);
                targetStartTime = SnapToFrame(targetStartTime);
                targetStartTime = ApplyMagneticSnapping(targetStartTime, _draggingIndex, isStart: true);
                targetStartTime = SnapToFrame(targetStartTime);
                targetStartTime = Mathf.Clamp(targetStartTime, 0f, originalEndTime - frameInterval);

                seg.StartTime = targetStartTime;
                seg.Duration = Mathf.Max(frameInterval, originalEndTime - targetStartTime);
            }

            // 高性能轻量样式更新：不重建 DOM 树，零 GC 分配
            if (_draggingBlockElement != null)
            {
                _draggingBlockElement.style.left = seg.StartTime * pps;
                _draggingBlockElement.style.width = Mathf.Max(12f, seg.Duration * pps);

                // 实时反馈合法性：若当前拖动位置导致非法三重叠或包含冲突，高亮红色边框警示
                bool isInvalid = HasInvalidOverlap();
                Color borderColor = isInvalid ? new Color(0.95f, 0.26f, 0.21f, 1.0f) : new Color(0.24f, 0.55f, 0.89f, 1.0f);
                _draggingBlockElement.style.borderLeftColor = borderColor;
                _draggingBlockElement.style.borderRightColor = borderColor;
                _draggingBlockElement.style.borderTopColor = borderColor;
                _draggingBlockElement.style.borderBottomColor = borderColor;
            }

            // 实时动态更新交叉淡化过渡区域 (Crossfade Overlays)
            UpdateCrossfadeOverlays();

            OnDataModified?.Invoke();
            evt.StopPropagation();
        }

        private void OnDragEnd(MouseUpEvent evt)
        {
            if (!_isDraggingSegment) return;

            var seg = (_draggingIndex >= 0 && _targetAsset?.AnimationSegments != null && _draggingIndex < _targetAsset.AnimationSegments.Count)
                ? _targetAsset.AnimationSegments[_draggingIndex]
                : null;

            if (_draggingBlockElement != null)
            {
                _draggingBlockElement.style.borderLeftColor = StyleKeyword.Null;
                _draggingBlockElement.style.borderRightColor = StyleKeyword.Null;
                _draggingBlockElement.style.borderTopColor = StyleKeyword.Null;
                _draggingBlockElement.style.borderBottomColor = StyleKeyword.Null;
            }

            _isDraggingSegment = false;
            _draggingIndex = -1;
            _draggingBlockElement = null;

            _contentElement.ReleaseMouse();
            _contentElement.UnregisterCallback<MouseMoveEvent>(OnDragMove);
            _contentElement.UnregisterCallback<MouseUpEvent>(OnDragEnd);

            // 若用户只是单击（Click）并未发生位移，直接保持现状，绝不广播 OnDataModified 避免中断预览播放
            bool hasActualChange = _hasDragMoved && seg != null && 
                (Mathf.Abs(seg.StartTime - _dragStartSegmentTime) > 0.0001f || Mathf.Abs(seg.Duration - _dragStartDuration) > 0.0001f);

            if (!hasActualChange)
            {
                RefreshSelectedSegmentHighlight();
                evt.StopPropagation();
                return;
            }

            if (_targetAsset != null && seg != null)
            {
                // 校验放置位置合法性：严禁三重叠或完全包含冲突
                if (HasInvalidOverlap())
                {
                    // 判定失败：回退到拖拽调整前的初始时间与持续时长
                    seg.StartTime = _dragStartSegmentTime;
                    seg.Duration = _dragStartDuration;
                    Debug.LogWarning($"[CwcMontage] 放置位置无效：动画片段 '{seg.SegmentName}' 与其他片段产生超过两个片段的重叠或包含冲突，已自动回退到调整前位置。");
                }

                _targetAsset.SortAnimationSegments();
                _targetAsset.EnsureSegmentsValid();

                _selectedSegmentIndex = _targetAsset.AnimationSegments.IndexOf(seg);
                RebuildSegments();
                EditorUtility.SetDirty(_targetAsset);
                OnDataModified?.Invoke();
            }
            else if (_targetAsset != null)
            {
                RebuildSegments();
            }

            evt.StopPropagation();
        }

        /// <summary>
        /// 仅轻量刷新当前片段的选中高亮，不重建 DOM，不扰乱物理层级，不广播任何数据变更事件。
        /// </summary>
        private void RefreshSelectedSegmentHighlight()
        {
            if (_segmentsContainer != null)
            {
                for (int i = 0; i < _segmentsContainer.childCount; i++)
                {
                    var child = _segmentsContainer[i];
                    if (child.userData is MontageAnimationSegment seg && seg == _selectedSegment)
                    {
                        child.AddToClassList("montage-anim-segment-selected");
                    }
                    else
                    {
                        child.RemoveFromClassList("montage-anim-segment-selected");
                    }
                }
            }

            if (_crossfadeContainer != null)
            {
                for (int i = 0; i < _crossfadeContainer.childCount; i++)
                {
                    _crossfadeContainer[i].MarkDirtyRepaint();
                }
            }
        }

        /// <summary>
        /// 将连续浮点时间戳离散规整到最近的基准帧（Frame Quantization）。
        /// </summary>
        /// <param name="time">时间戳（秒）</param>
        /// <param name="minFrames">最小保留帧数（例如持续时长至少 1 帧）</param>
        /// <returns>规整到帧的离散时间戳（秒）</returns>
        private float SnapToFrame(float time, int minFrames = 0)
        {
            float fps = Mathf.Max(1f, _frameRate);
            float frameInterval = 1f / fps;
            float snapped = Mathf.Round(time / frameInterval) * frameInterval;
            if (minFrames > 0)
            {
                snapped = Mathf.Max(minFrames * frameInterval, snapped);
            }
            return snapped;
        }

        /// <summary>
        /// 磁吸对齐算法：自动吸附到相邻片段边界、跨轨道动作块边界、物理分段标记点及 0 点。
        /// </summary>
        private float ApplyMagneticSnapping(float candidateTime, int currentIndex, bool isStart)
        {
            float excludeStart = -1f;
            float excludeEnd = -1f;
            if (_targetAsset?.AnimationSegments != null && currentIndex >= 0 && currentIndex < _targetAsset.AnimationSegments.Count)
            {
                excludeStart = _dragStartSegmentTime;
                excludeEnd = _dragStartSegmentTime + _dragStartDuration;
            }

            return MontageTimelineSnappingUtility.ApplyMagneticSnapping(
                candidateTime,
                PixelsPerSecond,
                _targetAsset,
                playheadTime: -1f,
                isStart: isStart,
                excludeSelfStartTime: excludeStart,
                excludeSelfEndTime: excludeEnd,
                snapPixelThreshold: SNAP_PIXEL_THRESHOLD);
        }

        /// <summary>
        /// 检查当前动画片段集合中是否存在非法的重叠关系：
        /// 1. 三重及以上重叠 (Triple Overlap)：任意时刻存在 3 个或更多片段同时重叠；
        /// 2. 完全包含 (Containment)：某个片段在时间轴上被另一个片段完全包围吞噬。
        /// 若检测到非法重叠返回 true，否则返回 false。
        /// </summary>
        private bool HasInvalidOverlap()
        {
            var segments = _targetAsset?.AnimationSegments;
            if (segments == null || segments.Count <= 1) return false;

            // 复制列表并按 StartTime 升序排序；若 StartTime 相同，按 EndTime 降序排序（大区间在前）
            var sorted = new List<MontageAnimationSegment>(segments);
            sorted.Sort((a, b) =>
            {
                int cmp = a.StartTime.CompareTo(b.StartTime);
                if (cmp != 0) return cmp;
                return b.EndTime.CompareTo(a.EndTime);
            });

            for (int i = 0; i < sorted.Count; i++)
            {
                var cur = sorted[i];
                if (cur == null) continue;

                // 1. 检查完全包含 (Containment)
                for (int j = i + 1; j < sorted.Count; j++)
                {
                    var next = sorted[j];
                    if (next == null) continue;

                    // 若 next 的起点已经在 cur 的终点之后或重合，后续片段不可能被 cur 包含
                    if (next.StartTime >= cur.EndTime) break;

                    // 若 next 的终点在 cur 的终点之前或重合，说明 next 完全落在 cur 内部（包含吞噬）
                    if (next.EndTime <= cur.EndTime)
                    {
                        return true;
                    }
                }

                // 2. 检查三重叠 (Triple Overlap)
                // 在按 StartTime 升序排序的序列中，若第 i+2 个片段的 StartTime 小于第 i 个片段的 EndTime，
                // 则在时间段 [sorted[i+2].StartTime, min(sorted[i].EndTime, sorted[i+1].EndTime)] 内，
                // 片段 i、i+1、i+2 三者同时存活重叠，构成非法三重叠。
                if (i < sorted.Count - 2)
                {
                    var third = sorted[i + 2];
                    if (third != null && third.StartTime < cur.EndTime)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        #endregion

        #region 菜单与快捷操作

        private void OnContentKeyDown(KeyDownEvent evt)
        {
            if (_targetAsset?.AnimationSegments == null || _selectedSegmentIndex < 0 || _selectedSegmentIndex >= _targetAsset.AnimationSegments.Count)
            {
                return;
            }

            if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace)
            {
                DeleteSegmentAt(_selectedSegmentIndex);
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.D && evt.actionKey)
            {
                DuplicateSegmentAt(_selectedSegmentIndex);
                evt.StopPropagation();
            }
        }

        public void DeleteSegmentAt(int index)
        {
            if (_targetAsset?.AnimationSegments == null || index < 0 || index >= _targetAsset.AnimationSegments.Count) return;

            Undo.RecordObject(_targetAsset, "Delete Animation Segment");
            _targetAsset.AnimationSegments.RemoveAt(index);
            _selectedSegment = null;
            _selectedSegmentIndex = -1;
            RebuildSegments();
            EditorUtility.SetDirty(_targetAsset);
            OnDataModified?.Invoke();
        }

        public void DeleteSegment(MontageAnimationSegment segment)
        {
            if (_targetAsset?.AnimationSegments == null || segment == null) return;
            int idx = _targetAsset.AnimationSegments.IndexOf(segment);
            if (idx >= 0)
            {
                DeleteSegmentAt(idx);
            }
        }

        public void DuplicateSegmentAt(int index)
        {
            if (_targetAsset?.AnimationSegments == null || index < 0 || index >= _targetAsset.AnimationSegments.Count) return;

            Undo.RecordObject(_targetAsset, "Duplicate Animation Segment");
            var seg = _targetAsset.AnimationSegments[index];
            var copy = seg.Clone();
            copy.StartTime = SnapToFrame(seg.EndTime);
            copy.Duration = SnapToFrame(seg.Duration, minFrames: 1);
            _targetAsset.AnimationSegments.Insert(index + 1, copy);
            _targetAsset.SortAnimationSegments();
            SelectSegment(copy);
            EditorUtility.SetDirty(_targetAsset);
            OnDataModified?.Invoke();
        }

        private void ShowAddClipPicker()
        {
            ShowAddClipPickerAt(-1f);
        }

        private void ShowAddClipPickerAt(float clickTime)
        {
            _activePickerTargetSegment = null;
            _pendingAddClipStartTime = clickTime;
            EditorGUIUtility.ShowObjectPicker<AnimationClip>(null, false, "", 202688);
        }

        public void HandleObjectPickerResult(AnimationClip pickedClip, bool isClosed = false)
        {
            if (_targetAsset == null)
            {
                if (isClosed)
                {
                    _activePickerTargetSegment = null;
                    _pendingAddClipStartTime = -1f;
                }
                return;
            }

            if (pickedClip != null)
            {
                Undo.RecordObject(_targetAsset, "Pick Animation Clip");

                // 1. 如果当前 Picker 会话尚未创建片段：在轨道上新建片段并聚焦
                if (_activePickerTargetSegment == null)
                {
                    float startTime = 0f;
                    if (_pendingAddClipStartTime >= 0f)
                    {
                        startTime = SnapToFrame(_pendingAddClipStartTime);
                    }
                    else if (_targetAsset.AnimationSegments.Count > 0)
                    {
                        var last = _targetAsset.AnimationSegments[^1];
                        if (last != null)
                        {
                            startTime = SnapToFrame(last.EndTime);
                        }
                    }

                    _activePickerTargetSegment = new MontageAnimationSegment(pickedClip, startTime, 1.0f);
                    _activePickerTargetSegment.Duration = SnapToFrame(_activePickerTargetSegment.Duration, minFrames: 1);
                    _targetAsset.AnimationSegments.Add(_activePickerTargetSegment);
                    _targetAsset.SortAnimationSegments();
                    _targetAsset.EnsureSegmentsValid();

                    int newIdx = _targetAsset.AnimationSegments.IndexOf(_activePickerTargetSegment);
                    SelectSegment(newIdx);
                }
                // 2. 如果已经在当前会话添加了片段，用户在 Picker 中切换另一个动画：直接切换当前选中片段的引用
                else
                {
                    _activePickerTargetSegment.Clip = pickedClip;
                    _activePickerTargetSegment.Duration = SnapToFrame(_activePickerTargetSegment.CalculateNaturalDuration(), minFrames: 1);
                    _targetAsset.SortAnimationSegments();
                    _targetAsset.EnsureSegmentsValid();

                    int curIdx = _targetAsset.AnimationSegments.IndexOf(_activePickerTargetSegment);
                    SelectSegment(curIdx);
                }

                EditorUtility.SetDirty(_targetAsset);
                OnDataModified?.Invoke();
            }

            if (isClosed)
            {
                _activePickerTargetSegment = null;
                _pendingAddClipStartTime = -1f;
            }
        }

        private void ShowSegmentContextMenu(int index, Vector2 mousePos)
        {
            if (_targetAsset?.AnimationSegments == null || index < 0 || index >= _targetAsset.AnimationSegments.Count) return;
            var seg = _targetAsset.AnimationSegments[index];

            var menu = new GenericMenu();

            // 1. 高频核心编辑操作置顶
            menu.AddItem(new GUIContent("Duplicate (Ctrl+D)"), false, () => DuplicateSegmentAt(index));
            menu.AddItem(new GUIContent("Delete (Del)"), false, () => DeleteSegmentAt(index));

            menu.AddSeparator("");

            // 2. 实用对齐与参数重置
            if (index > 0)
            {
                menu.AddItem(new GUIContent("Snap to Previous"), false, () =>
                {
                    var prev = _targetAsset.AnimationSegments[index - 1];
                    if (prev != null)
                    {
                        Undo.RecordObject(_targetAsset, "Snap Segment to Previous");
                        seg.StartTime = SnapToFrame(prev.EndTime);
                        RebuildSegments();
                        EditorUtility.SetDirty(_targetAsset);
                        OnDataModified?.Invoke();
                    }
                });
            }

            menu.AddItem(new GUIContent("Reset PlayRate (1.0x)"), false, () =>
            {
                Undo.RecordObject(_targetAsset, "Reset PlayRate");
                seg.ResetPlayRate();
                RebuildSegments();
                EditorUtility.SetDirty(_targetAsset);
                OnDataModified?.Invoke();
            });

            menu.AddSeparator("");

            // 3. 资源辅助定位
            menu.AddItem(new GUIContent("Ping Clip"), false, () =>
            {
                if (seg.Clip != null)
                {
                    EditorGUIUtility.PingObject(seg.Clip);
                }
            });

            menu.DropDown(new Rect(mousePos, Vector2.zero));
        }

        private void OnContentMouseDown(MouseDownEvent evt)
        {
            float clickTime = SnapToFrame(evt.localMousePosition.x / PixelsPerSecond);
            if (evt.button == 0)
            {
                ClearSelection();
                OnRequestScrubTime?.Invoke(clickTime);
            }
            else if (evt.button == 1)
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Add Animation Clip..."), false, () => ShowAddClipPickerAt(clickTime));
                menu.DropDown(new Rect(evt.mousePosition, Vector2.zero));
                evt.StopPropagation();
            }
        }

        private void OnDragUpdated(DragUpdatedEvent evt)
        {
            bool hasClips = false;
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj is AnimationClip)
                {
                    hasClips = true;
                    break;
                }
            }

            if (hasClips)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.StopPropagation();
            }
        }

        private void OnDragPerform(DragPerformEvent evt)
        {
            DragAndDrop.AcceptDrag();
            float dropTime = SnapToFrame(Mathf.Max(0f, evt.localMousePosition.x / PixelsPerSecond));

            Undo.RecordObject(_targetAsset, "Drop Animation Clips");

            float curTime = dropTime;
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj is AnimationClip clip)
                {
                    var seg = new MontageAnimationSegment(clip, curTime, 1.0f);
                    seg.Duration = SnapToFrame(seg.Duration, minFrames: 1);
                    _targetAsset.AnimationSegments.Add(seg);
                    curTime = SnapToFrame(seg.EndTime);
                }
            }

            _targetAsset.SortAnimationSegments();
            _targetAsset.EnsureSegmentsValid();
            RebuildSegments();
            EditorUtility.SetDirty(_targetAsset);
            OnDataModified?.Invoke();
            evt.StopPropagation();
        }

        #endregion

        #region 私有辅助类型

        /// <summary>
        /// 交叉淡化 UI 元素持有者，用于零 GC 动态更新样式、手柄宽度与重叠时长文本。
        /// </summary>
        private class CrossfadeOverlayHolder
        {
            public VisualElement Root;
            public VisualElement LeftTrimHandle;
            public VisualElement RightTrimHandle;
            public Label DurationLabel;
            public MontageAnimationSegment SegmentA;
            public MontageAnimationSegment SegmentB;
            public int IndexA;
            public int IndexB;
        }

        #endregion
    }
}
