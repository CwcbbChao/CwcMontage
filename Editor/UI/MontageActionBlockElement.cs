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

        private const float RESIZE_HANDLE_WIDTH = 6f;
        private const float BASE_PIXELS_PER_SECOND = 200f;

        #endregion

        #region 私有字段

        private MontageActionBlockData _data;
        private int _trackIndex;
        private int _blockIndex;
        private float _clipLength = 1f;
        private float _frameRate = 30f;
        private float _zoomLevel = 1.0f;

        private Label _label;
        private VisualElement _leftHandle;
        private VisualElement _rightHandle;

        private bool _isDragging;
        private bool _isResizingLeft;
        private bool _isResizingRight;
        private float _dragStartMouseX;
        private int _initialStartFrame;
        private int _initialEndFrame;

        #endregion

        #region 公共事件

        public event Action<MontageActionBlockElement> OnBlockSelected;
        public event Action<MontageActionBlockElement> OnBlockCopied;
        public event Action<MontageActionBlockElement> OnBlockDeleted;
        public event Action<MontageActionBlockElement> OnBlockModified;

        #endregion

        #region 公共属性

        public MontageActionBlockData Data => _data;
        public int TrackIndex => _trackIndex;
        public int BlockIndex => _blockIndex;

        #endregion

        #region 构造方法

        public MontageActionBlockElement(
            MontageActionBlockData data,
            int trackIndex,
            int blockIndex,
            float clipLength,
            float frameRate,
            float zoomLevel)
        {
            _data = data;
            _trackIndex = trackIndex;
            _blockIndex = blockIndex;
            _clipLength = Mathf.Max(0.001f, clipLength);
            _frameRate = Mathf.Max(1f, frameRate);
            _zoomLevel = Mathf.Clamp(zoomLevel, 0.1f, 20f);

            AddToClassList("montage-action-block");

            // 动作类型背景色适配
            if (data?.Action != null)
            {
                style.backgroundColor = GetActionTypeColor(data.Action.GetType());
            }

            // 标签文本
            _label = new Label();
            _label.AddToClassList("montage-action-block-label");
            Add(_label);

            // 左右缩放手柄
            _leftHandle = new VisualElement();
            _leftHandle.AddToClassList("montage-resize-handle");
            _leftHandle.AddToClassList("montage-resize-handle-left");
            Add(_leftHandle);

            _rightHandle = new VisualElement();
            _rightHandle.AddToClassList("montage-resize-handle");
            _rightHandle.AddToClassList("montage-resize-handle-right");
            Add(_rightHandle);

            UpdateVisual();

            // 注册鼠标事件
            RegisterCallback<MouseDownEvent>(OnMouseDown);
            RegisterCallback<MouseMoveEvent>(OnMouseMove);
            RegisterCallback<MouseUpEvent>(OnMouseUp);
            RegisterCallback<MouseLeaveEvent>(OnMouseLeave);
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

            float pps = BASE_PIXELS_PER_SECOND * _zoomLevel;
            float frameWidth = pps / _frameRate;

            float leftPos = _data.StartFrame * frameWidth;
            float width = Mathf.Max(4f, (_data.EndFrame - _data.StartFrame) * frameWidth);

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

            _label.text = $"{displayName} [{_data.StartFrame}-{_data.EndFrame}]";

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
            _zoomLevel = Mathf.Clamp(zoomLevel, 0.1f, 20f);
            UpdateVisual();
        }

        /// <summary>
        /// 设置当前块的高亮选中状态。
        /// </summary>
        public void SetSelected(bool isSelected)
        {
            if (isSelected)
            {
                AddToClassList("montage-action-block-selected");
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

        private void OnMouseDown(MouseDownEvent evt)
        {
            if (evt.button == 0) // 左键
            {
                OnBlockSelected?.Invoke(this);

                _dragStartMouseX = evt.mousePosition.x;
                _initialStartFrame = _data.StartFrame;
                _initialEndFrame = _data.EndFrame;

                float localX = evt.localMousePosition.x;
                float width = layout.width;

                if (localX <= RESIZE_HANDLE_WIDTH)
                {
                    _isResizingLeft = true;
                }
                else if (localX >= width - RESIZE_HANDLE_WIDTH)
                {
                    _isResizingRight = true;
                }
                else
                {
                    _isDragging = true;
                }

                this.CaptureMouse();
                evt.StopPropagation();
            }
            else if (evt.button == 1) // 右键菜单
            {
                ShowContextMenu(evt.mousePosition);
                evt.StopPropagation();
            }
        }

        private void OnMouseMove(MouseMoveEvent evt)
        {
            if (!_isDragging && !_isResizingLeft && !_isResizingRight)
            {
                return;
            }

            float pps = BASE_PIXELS_PER_SECOND * _zoomLevel;
            float frameWidth = pps / _frameRate;

            if (frameWidth <= 0.001f)
            {
                return;
            }

            int totalFrames = Mathf.Max(1, Mathf.RoundToInt(_clipLength * _frameRate));
            float deltaX = evt.mousePosition.x - _dragStartMouseX;
            int deltaFrames = Mathf.RoundToInt(deltaX / frameWidth);

            if (_isDragging)
            {
                int duration = _initialEndFrame - _initialStartFrame;
                int newStart = Mathf.Clamp(_initialStartFrame + deltaFrames, 0, totalFrames - duration);
                int newEnd = newStart + duration;

                _data.StartFrame = newStart;
                _data.EndFrame = newEnd;
                _data.StartTime = newStart / _frameRate;
                _data.EndTime = newEnd / _frameRate;
            }
            else if (_isResizingLeft)
            {
                int newStart = Mathf.Clamp(_initialStartFrame + deltaFrames, 0, _initialEndFrame - 1);
                _data.StartFrame = newStart;
                _data.StartTime = newStart / _frameRate;
            }
            else if (_isResizingRight)
            {
                int newEnd = Mathf.Clamp(_initialEndFrame + deltaFrames, _initialStartFrame + 1, totalFrames);
                _data.EndFrame = newEnd;
                _data.EndTime = newEnd / _frameRate;
            }

            UpdateVisual();
            OnBlockModified?.Invoke(this);
            evt.StopPropagation();
        }

        private void OnMouseUp(MouseUpEvent evt)
        {
            if (evt.button == 0 && (_isDragging || _isResizingLeft || _isResizingRight))
            {
                _isDragging = false;
                _isResizingLeft = false;
                _isResizingRight = false;
                this.ReleaseMouse();
                evt.StopPropagation();
            }
        }

        private void OnMouseLeave(MouseLeaveEvent evt)
        {
            if (!this.HasMouseCapture())
            {
                _isDragging = false;
                _isResizingLeft = false;
                _isResizingRight = false;
            }
        }

        private void ShowContextMenu(Vector2 mousePos)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Copy Action Block (Ctrl+C)"), false, () => OnBlockCopied?.Invoke(this));
            menu.AddItem(new GUIContent("Duplicate Action Block (Ctrl+D)"), false, () =>
            {
                MontageClipboard.CopyActionBlock(_data);
                OnBlockCopied?.Invoke(this);
            });
            menu.AddItem(new GUIContent(_data.IsEnabled ? "Disable Action Block" : "Enable Action Block"), false, () =>
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
            menu.AddItem(new GUIContent("Edit Script"), false, OpenScript);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Delete Action Block (Delete)"), false, () => OnBlockDeleted?.Invoke(this));
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
